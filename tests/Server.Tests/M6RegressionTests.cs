using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// The approved M6 behaviours, each pinned in one place: the five-minute pause and expiry, MatchFinished refusals that
// use up no seq, stale and duplicate commands across a pause and rejoin, and a finished match that no rejoin can change.
public class M6RegressionTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    private static ProtocolError ProtocolCode(ServerMessage? answer)
    {
        var rejected = Assert.IsType<Rejected>(answer);
        Assert.Equal(RejectionKind.Protocol, rejected.Kind);
        return Enum.Parse<ProtocolError>(rejected.Code);
    }

    private static ServerMessage Submit(TestClient client, SubmitCommand submit)
    {
        var count = client.Answers.Count;
        client.Send(submit);
        Assert.Equal(count + 1, client.Answers.Count);
        return client.LastAnswer!;
    }

    [Fact]
    public void TheDefaultRejoinWindowIsFiveMinutesAndItIsEnforced()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Lobby.DefaultRejoinWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), new MatchServerOptions().RejoinWindow);

        var time = new ManualTime();
        var recorder = new CapturingRecorder();
        var harness = new Harness(4, rejoinWindow: Lobby.DefaultRejoinWindow, time: time, recorder: recorder);
        var (one, two) = harness.Pair("melee");
        Play.Commands(one, two, 4, 6);
        var dropped = time.GetUtcNow();
        harness.Lobby.Disconnect(two.Id);
        one.Pump();

        var code = one.Welcome!.Code;
        Assert.Equal(MatchStatus.Paused, harness.Lobby.StatusOf(code));
        Assert.Equal(dropped + TimeSpan.FromMinutes(5), Assert.IsType<MatchPaused>(one.Pauses[^1]).RejoinBy);
        Assert.Empty(recorder.Closed);

        time.Advance(TimeSpan.FromMinutes(5) - Tick);
        harness.Lobby.Tick();
        Assert.Equal(MatchStatus.Paused, harness.Lobby.StatusOf(code));

        time.Advance(Tick);
        harness.Lobby.Tick();
        one.Pump();
        Assert.Equal(MatchStatus.Aborted, harness.Lobby.StatusOf(code));
        Assert.Equal(AbortReason.RejoinExpired, one.Aborted!.Reason);
        Assert.Equal(RecordKind.Aborted, Assert.Single(recorder.Closed).Result.Kind);
    }

    // After the end, every command is refused as MatchFinished before the seq is looked at: the right seq, a repeated
    // one, a skipped one and a resend of an answered command alike. None uses up the seq, and the seat's last answer is
    // still its last real one.
    [Fact]
    public void MatchFinishedRefusalsDoNotUseUpTheSeq()
    {
        var time = new ManualTime();
        var harness = new Harness(2, rejoinWindow: TimeSpan.FromMinutes(5), time: time);
        var (one, two) = harness.Pair();
        Play.ToEnd(one, two, 2);
        var code = one.Welcome!.Code;
        Assert.Equal(MatchStatus.Finished, harness.Lobby.StatusOf(code));
        var log = harness.Lobby.LogOf(code)!.Count;
        var lastReal = one.Answers.Last(a => a is Accepted or Rejected { Kind: RejectionKind.Rules });
        var next = one.Seq + 1;
        var answered = one.LastSubmit!;

        foreach (var submit in new[]
                 {
                     new SubmitCommand("after-1", next, one.Mirror.Events, new EndActivation(Seat.One)),
                     new SubmitCommand("after-2", next, one.Mirror.Events, new EndActivation(Seat.One)),
                     new SubmitCommand("after-3", next + 5, one.Mirror.Events, new EndActivation(Seat.One)),
                     new SubmitCommand("after-4", next, one.Mirror.Events - 1, new EndActivation(Seat.One)),
                     answered,
                 })
        {
            var refused = Assert.IsType<Rejected>(Submit(one, submit));
            Assert.Equal(ProtocolError.MatchFinished, ProtocolCode(refused));
            Assert.Equal(next, refused.NextSeq);
            Assert.False(refused.Duplicate);
        }
        Assert.Equal(log, harness.Lobby.LogOf(code)!.Count);

        // A read-only rejoin sees the same next seq and the last real answer, not a refusal.
        var token = one.Token!;
        harness.Lobby.Disconnect(one.Id);
        var back = harness.Connect();
        back.Rejoin(code, token);
        Assert.Equal(next, back.Welcome!.NextSeq);
        Assert.Equal(lastReal, back.Welcome.LastAnswer);
    }

    // Across a pause and a rejoin: an answered command resent is a duplicate and is not applied again, its id with
    // another command is refused, a command built on old events is stale, a skipped seq is out of order, none of these
    // uses up the seq, and the next real command is decided as new.
    [Fact]
    public void StaleAndDuplicateCommandsStaySafeAcrossAPauseAndRejoin()
    {
        var time = new ManualTime();
        var harness = new Harness(3, rejoinWindow: TimeSpan.FromMinutes(5), time: time);
        var (one, two) = harness.Pair();
        Play.Commands(one, two, 3, 5);
        var code = one.Welcome!.Code;

        var bot = new Bot(new Random(3));
        var proposals = bot.Propose(one.Mirror.State).ToList();
        var seat = Scenarios.SeatOf(proposals[0])!.Value;
        var client = seat == Seat.One ? one : two;
        var other = seat == Seat.One ? two : one;
        var answer = proposals.Where(c => Scenarios.SeatOf(c) == seat).Select(client.Command).First(a => a is Accepted);
        var sent = client.LastSubmit!;
        var token = client.Token!;

        harness.Lobby.Disconnect(client.Id);
        other.Pump();
        Assert.Equal(MatchStatus.Paused, harness.Lobby.StatusOf(code));
        // While paused the other seat's command is refused and its seq is kept.
        var otherSeq = other.Seq;
        Assert.Equal(ProtocolError.MatchPaused, ProtocolCode(other.Command(new EndActivation(other.Welcome!.Seat))));
        Assert.Equal(otherSeq, other.Seq);

        var back = harness.Connect();
        back.Rejoin(code, token);
        Assert.True(back.Synchronized);
        Assert.Equal(answer, back.Welcome!.LastAnswer);
        var next = back.Welcome.NextSeq;
        Assert.Equal(sent.Seq + 1, next);
        var log = harness.Lobby.LogOf(code)!.Count;

        var duplicate = Submit(back, new SubmitCommand(sent.CommandId, sent.Seq, back.Mirror.Events, sent.Command));
        Assert.Equal(Assert.IsType<Accepted>(answer) with { Duplicate = true }, duplicate);

        var reused = Assert.IsType<Rejected>(Submit(back, new SubmitCommand(sent.CommandId, sent.Seq, back.Mirror.Events, new EndActivation(seat))));
        Assert.Equal(ProtocolError.IdReused, ProtocolCode(reused));

        var stale = Assert.IsType<Rejected>(Submit(back, new SubmitCommand("stale-1", next, back.Mirror.Events - 1, new EndActivation(seat))));
        Assert.Equal(ProtocolError.Stale, ProtocolCode(stale));
        Assert.Equal(next, stale.NextSeq);

        var skipped = Assert.IsType<Rejected>(Submit(back, new SubmitCommand("skip-1", next + 3, back.Mirror.Events, new EndActivation(seat))));
        Assert.Equal(ProtocolError.OutOfOrder, ProtocolCode(skipped));
        Assert.Equal(next, skipped.NextSeq);

        Assert.Equal(log, harness.Lobby.LogOf(code)!.Count);
        Assert.Equal(MatchStatus.Playing, harness.Lobby.StatusOf(code));

        // The next real command, at the seq the Welcome gave, is decided as new.
        var fresh = Submit(back, new SubmitCommand("fresh-1", next, back.Mirror.Events, new EndActivation(seat)));
        Assert.True(fresh is Accepted { Duplicate: false } or Rejected { Kind: RejectionKind.Rules, Duplicate: false });
    }

    // Every way back into a finished match (both seats, repeated rejoins, a takeover, a forged token, commands and
    // TokenSaved) leaves the log, the result, the record and the seat tokens exactly as they were.
    [Fact]
    public void ARejoinCannotChangeAFinishedMatch()
    {
        var time = new ManualTime();
        var recorder = new CapturingRecorder();
        var harness = new Harness(5, rejoinWindow: TimeSpan.FromMinutes(5), time: time, recorder: recorder);
        var (one, two) = harness.Pair("melee");
        Play.ToEnd(one, two, 5);
        var code = one.Welcome!.Code;
        var log = harness.Lobby.LogOf(code)!.ToList();
        var outcome = one.Mirror.State.Outcome;
        Assert.NotNull(outcome);
        var record = Assert.Single(recorder.Closed);
        var tokens = new Dictionary<Seat, string> { [Seat.One] = one.Token!, [Seat.Two] = two.Token! };
        harness.Lobby.Disconnect(one.Id);
        harness.Lobby.Disconnect(two.Id);

        for (var round = 0; round < 2; round++)
        {
            foreach (var (seat, token) in tokens)
            {
                var first = harness.Connect();
                first.Rejoin(code, token);
                var second = harness.Connect();
                second.Rejoin(code, token); // takes the seat over from the first
                first.Pump();
                Assert.Equal(ProtocolError.SeatTakenOver, ProtocolCode(first.Answers.Last()));

                Assert.Equal(MatchStatus.Finished, second.Welcome!.Status);
                Assert.Null(second.Welcome.SeatToken);
                Assert.True(second.Synchronized);
                Assert.Equal(outcome, second.Mirror.State.Outcome);
                Assert.Equal(ProtocolError.MatchFinished, ProtocolCode(second.Command(new EndActivation(seat))));
                second.Send(new TokenSaved());
                Assert.Equal(ProtocolError.MatchFinished, ProtocolCode(second.LastAnswer));
                harness.Lobby.Disconnect(second.Id);
            }
        }
        var forged = harness.Connect();
        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(forged.Rejoin(code, RecordFormat.Base64Url(new byte[32]))));

        Assert.Equal(log, harness.Lobby.LogOf(code));
        Assert.Equal(MatchStatus.Finished, harness.Lobby.StatusOf(code));
        Assert.Same(record, Assert.Single(recorder.Closed));
        // The original tokens still open the seats (none was rotated), until the window ends.
        foreach (var token in tokens.Values)
        {
            var check = harness.Connect();
            Assert.IsType<Welcome>(check.Rejoin(code, token));
            harness.Lobby.Disconnect(check.Id);
        }
        time.Advance(TimeSpan.FromMinutes(5));
        harness.Lobby.Tick();
        Assert.Equal(ProtocolError.UnknownMatch, ProtocolCode(harness.Connect().Rejoin(code, tokens[Seat.One])));
    }
}
