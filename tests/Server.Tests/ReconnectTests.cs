using System.Collections.Concurrent;
using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Rejoin, pause and expiry, token rotation and takeover, through the transport-free Lobby with a hand-moved clock.
public class ReconnectTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    private sealed class Setup
    {
        public Setup(int seed = 1)
        {
            Harness = new Harness(seed, rejoinWindow: Window, time: Time, recorder: Recorder);
            (One, Two) = Harness.Pair();
            Code = One.Welcome!.Code;
            TokenOne = One.Token!;
            TokenTwo = Two.Token!;
        }

        public ManualTime Time { get; } = new();
        public CapturingRecorder Recorder { get; } = new();
        public Harness Harness { get; }
        public TestClient One { get; }
        public TestClient Two { get; }
        public string Code { get; }
        public string TokenOne { get; }
        public string TokenTwo { get; }
        public Lobby Lobby => Harness.Lobby;
        public int LogCount => Lobby.LogOf(Code)!.Count;

        public void Drop(TestClient client)
        {
            Lobby.Disconnect(client.Id);
            Harness.Clients.Values.ToList().ForEach(c => c.Pump());
        }

        public TestClient Rejoin(string token, MatchMirror? mirror = null)
        {
            var client = Harness.Connect(mirror);
            client.Rejoin(Code, token);
            return client;
        }
    }

    private static ProtocolError ProtocolCode(ServerMessage? answer)
    {
        var rejected = Assert.IsType<Rejected>(answer);
        Assert.Equal(RejectionKind.Protocol, rejected.Kind);
        return Enum.Parse<ProtocolError>(rejected.Code);
    }

    // ---- Pause and resume ----

    [Fact]
    public void ADropPausesTheWholeMatchAndARejoinResumesIt()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 6);
        var before = s.LogCount;
        s.Drop(s.Two);

        Assert.Equal(MatchStatus.Paused, s.Lobby.StatusOf(s.Code));
        var paused = Assert.IsType<MatchPaused>(s.One.Pauses[^1]);
        Assert.Equal(Seat.Two, paused.Seat);
        Assert.Equal(s.Time.GetUtcNow() + Window, paused.RejoinBy);

        // Nothing can happen while paused: no command, no die, and the seq is not used up.
        var seq = s.One.Seq;
        var bot = new Bot(new Random(1));
        foreach (var command in bot.Propose(s.One.Mirror.State).Where(c => CommandSeats.Of(c) == Seat.One).Take(3))
        {
            Assert.Equal(ProtocolError.MatchPaused, ProtocolCode(s.One.Command(command)));
        }
        Assert.Equal(seq, s.One.Seq);
        Assert.Equal(before, s.LogCount);

        var back = s.Rejoin(s.TokenTwo);
        Assert.True(back.Synchronized);
        Assert.Equal(MatchStatus.Playing, back.Welcome!.Status);
        Assert.Equal(0, back.Welcome.ReplayFrom);
        Assert.Equal(MatchStatus.Playing, s.Lobby.StatusOf(s.Code));
        Assert.Equal(Seat.Two, Assert.IsType<MatchResumed>(s.One.Pauses[^1]).Seat);

        Play.ToEnd(s.One, back, 1);
        var log = s.Lobby.LogOf(s.Code)!;
        Assert.Equal(log[^1].Hash, s.One.Mirror.Hash);
        Assert.Equal(log[^1].Hash, back.Mirror.Hash);
        Assert.Equal(RecordKind.Completed, Assert.Single(s.Recorder.Closed).Result.Kind);
    }

    [Fact]
    public void AMissedDeadlineAbortsWithRejoinExpiredAndNoWinner()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 4);
        s.Drop(s.Two);

        s.Time.Advance(Window - Tick);
        s.Lobby.Tick();
        Assert.Equal(MatchStatus.Paused, s.Lobby.StatusOf(s.Code));

        s.Time.Advance(Tick);
        s.Lobby.Tick();
        s.One.Pump();
        Assert.Equal(MatchStatus.Aborted, s.Lobby.StatusOf(s.Code));
        Assert.Equal(AbortReason.RejoinExpired, s.One.Aborted!.Reason);
        Assert.Equal(Seat.Two, s.One.Aborted.Seat);
        Assert.Null(s.One.Mirror.State.Outcome);

        var record = Assert.Single(s.Recorder.Closed);
        Assert.Equal(RecordKind.Aborted, record.Result.Kind);
        Assert.Equal(nameof(AbortReason.RejoinExpired), record.Result.Reason);
        Assert.Null(record.Result.Winner);
    }

    [Fact]
    public void TheDeadlineIsFixedAtTheDropAndNothingButARejoinClearsIt()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var deadline = s.Lobby.DeadlineOf(s.Code, Seat.Two);
        Assert.NotNull(deadline);

        s.Time.Advance(TimeSpan.FromMinutes(2));
        var stranger = s.Harness.Connect();
        stranger.Send(new Rejoin(s.Code, RecordFormat.Base64Url(new byte[32]), 0, null));
        s.One.Command(new EndActivation(Seat.One));
        s.Lobby.Tick();
        Assert.Equal(deadline, s.Lobby.DeadlineOf(s.Code, Seat.Two));

        s.Time.Advance(TimeSpan.FromMinutes(3));
        s.Lobby.Tick();
        Assert.Equal(MatchStatus.Aborted, s.Lobby.StatusOf(s.Code));
    }

    [Fact]
    public void ARejoinAndLeaveAgainStartsAFreshWindowForThatNewDrop()
    {
        var s = new Setup();
        s.Drop(s.Two);
        s.Time.Advance(TimeSpan.FromMinutes(4));
        var back = s.Rejoin(s.TokenTwo);
        s.Drop(back);
        Assert.Equal(s.Time.GetUtcNow() + Window, s.Lobby.DeadlineOf(s.Code, Seat.Two));
        s.Time.Advance(TimeSpan.FromMinutes(4));
        s.Lobby.Tick();
        Assert.Equal(MatchStatus.Paused, s.Lobby.StatusOf(s.Code));
    }

    [Fact]
    public void WithBothSeatsAwayTheEarliestDeadlineDecides()
    {
        var s = new Setup();
        s.Drop(s.Two);
        s.Time.Advance(TimeSpan.FromMinutes(1));
        s.Drop(s.One);
        Assert.Equal(1, s.Lobby.MatchCount);

        // One rejoins: still paused, and it is told who is away.
        var one = s.Rejoin(s.TokenOne);
        Assert.Equal(MatchStatus.Paused, one.Welcome!.Status);
        Assert.Equal(Seat.Two, Assert.IsType<MatchPaused>(one.Pauses[^1]).Seat);

        s.Time.Advance(TimeSpan.FromMinutes(4));
        s.Lobby.Tick();
        one.Pump();
        Assert.Equal(AbortReason.RejoinExpired, one.Aborted!.Reason);
        Assert.Equal(Seat.Two, one.Aborted.Seat);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARejoinRacingTheDeadlineIsDecidedByWhoeverComesFirst(bool rejoinFirst)
    {
        var s = new Setup();
        s.Drop(s.Two);
        s.Time.Advance(Window);
        if (rejoinFirst)
        {
            var back = s.Rejoin(s.TokenTwo);
            s.Lobby.Tick();
            Assert.Equal(MatchStatus.Playing, s.Lobby.StatusOf(s.Code));
            Assert.Null(back.Aborted);
        }
        else
        {
            s.Lobby.Tick();
            var late = s.Rejoin(s.TokenTwo);
            Assert.Equal(MatchStatus.Aborted, late.Welcome!.Status);
            Assert.Null(late.Welcome.SeatToken);
            Assert.Equal(AbortReason.RejoinExpired, late.Aborted!.Reason);
        }
    }

    [Fact]
    public void ShutdownWhilePausedIsAShutdownNotAnExpiry()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 3);
        s.Drop(s.Two);
        s.Lobby.Shutdown();
        s.One.Pump();
        Assert.Equal(AbortReason.ServerShutdown, s.One.Aborted!.Reason);
        Assert.Equal(nameof(AbortReason.ServerShutdown), Assert.Single(s.Recorder.Closed).Result.Reason);
    }

    [Fact]
    public void ACreatorWhoLeavesBeforeAnyoneJoinsTakesTheMatchWithThem()
    {
        var time = new ManualTime();
        var recorder = new CapturingRecorder();
        var harness = new Harness(1, rejoinWindow: Window, time: time, recorder: recorder);
        var one = harness.Connect();
        one.Send(new CreateMatch("ranged"));
        harness.Lobby.Disconnect(one.Id);
        Assert.Equal(0, harness.Lobby.MatchCount);
        Assert.Empty(recorder.Started);
        Assert.Empty(recorder.Closed);
        var back = harness.Connect();
        Assert.Equal(ProtocolError.UnknownMatch, ProtocolCode(back.Rejoin(one.Welcome!.Code, one.Token!)));
    }

    // ---- Tokens ----

    [Fact]
    public void ALostWelcomeLeavesTheOldTokenWorking()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var first = s.Rejoin(s.TokenTwo);
        Assert.NotEqual(s.TokenTwo, first.Token);
        s.Drop(first); // the Welcome with the new token never arrived; nothing confirmed
        var second = s.Rejoin(s.TokenTwo);
        Assert.NotNull(second.Welcome);
        Assert.Equal(Seat.Two, second.Welcome!.Seat);
    }

    [Fact]
    public void ALostConfirmationIsHealedByUsingTheNewToken()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var first = s.Rejoin(s.TokenTwo);
        var saved = first.Token!;
        s.Drop(first); // saved to disk, but TokenSaved was lost

        var second = s.Rejoin(saved);
        Assert.NotNull(second.Welcome);
        s.Drop(second);

        // Using the new token proved it arrived: the old one is gone.
        var stale = s.Harness.Connect();
        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(stale.Rejoin(s.Code, s.TokenTwo)));
        Assert.NotNull(s.Rejoin(second.Token!).Welcome);
    }

    [Fact]
    public void AConfirmedTokenRetiresEveryOlderOne()
    {
        var s = new Setup();
        s.Two.Send(new TokenSaved());
        Assert.DoesNotContain(s.Two.Answers, a => a is Rejected);
        s.Drop(s.Two);
        var back = s.Rejoin(s.TokenTwo);
        back.Send(new TokenSaved());
        back.Send(new TokenSaved()); // a repeat is harmless
        Assert.DoesNotContain(back.Answers, a => a is Rejected);
        s.Drop(back);

        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(s.Harness.Connect().Rejoin(s.Code, s.TokenTwo)));
        Assert.NotNull(s.Rejoin(back.Token!).Welcome);
    }

    [Fact]
    public void AtMostFourUnconfirmedTokensAreKept()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var issued = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var client = s.Rejoin(s.TokenTwo);
            issued.Add(client.Token!);
            s.Drop(client);
        }
        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(s.Harness.Connect().Rejoin(s.Code, issued[0])));
        Assert.NotNull(s.Rejoin(issued[1]).Welcome);
    }

    [Fact]
    public void ADuplicateRejoinOnASeatedConnectionChangesNothing()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var back = s.Rejoin(s.TokenTwo);
        Assert.Equal(ProtocolError.AlreadyJoined, ProtocolCode(back.Rejoin(s.Code, s.TokenTwo)));
        Assert.Equal(back.Id, s.Lobby.ConnectionOf(s.Code, Seat.Two));
    }

    [Fact]
    public void ThreeBadTokensCloseTheConnectionAndForgedTokensNeverSeat()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var attacker = s.Harness.Connect();
        for (var i = 0; i < Lobby.MaxTokenFailures; i++)
        {
            var forged = RecordFormat.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(attacker.Rejoin(s.Code, forged)));
        }
        Assert.Contains(attacker.Id, s.Harness.Closed);
        Assert.False(s.Lobby.IsSeated(attacker.Id));
        // One seat's token never opens the other seat.
        var swapped = s.Rejoin(s.TokenOne);
        Assert.Equal(Seat.One, swapped.Welcome!.Seat);
        Assert.Contains(s.One.Id, s.Harness.Closed);
    }

    [Fact]
    public void TakeoverDetachesTheOldConnectionBeforeAnythingElse()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 4);
        var thief = s.Rejoin(s.TokenTwo);
        s.Two.Pump();
        Assert.Equal(ProtocolError.SeatTakenOver, ProtocolCode(s.Two.LastAnswer));
        Assert.Contains(s.Two.Id, s.Harness.Closed);
        Assert.Equal(thief.Id, s.Lobby.ConnectionOf(s.Code, Seat.Two));

        // The old connection's later messages are never processed.
        var before = s.LogCount;
        Assert.Equal(ProtocolError.NotJoined, ProtocolCode(s.Two.Command(new EndActivation(Seat.Two))));
        Assert.Equal(before, s.LogCount);
        // Its transport-level disconnect afterwards does not pause the match.
        s.Drop(s.Two);
        Assert.Equal(MatchStatus.Playing, s.Lobby.StatusOf(s.Code));
    }

    // ---- What the rejoining client gets ----

    [Fact]
    public void AClientThatKeptItsCopyGetsExactlyTheMissingEvents()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 3);
        var kept = s.Two.Mirror;
        var known = kept.Events;
        s.Drop(s.Two);

        var back = s.Rejoin(s.TokenTwo, kept);
        Assert.Equal(known, back.Welcome!.ReplayFrom);
        Assert.Empty(back.Inbox.OfType<EventPublished>());
        Assert.True(back.Synchronized);

        // Now with events the client missed (they happened before the server noticed the drop).
        Play.Commands(s.One, back, 1, 4);
        var stale = new MatchMirror();
        foreach (var e in s.Lobby.LogOf(s.Code)!.Take(known))
        {
            stale.Apply(e);
        }
        s.Drop(back);
        var again = s.Rejoin(back.Token!, stale);
        var sent = again.Inbox.OfType<EventPublished>().Select(e => e.Seq).ToList();
        Assert.Equal(Enumerable.Range(known + 1, s.LogCount - known), sent);
        Assert.True(again.Synchronized);
    }

    [Fact]
    public void ACopyThatIsNotAPrefixIsReplayedFromTheStart()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 3);
        s.Drop(s.Two);

        // A copy of another match with the same number of events or more: wrong history.
        var other = new Setup(seed: 9);
        Play.Commands(other.One, other.Two, 9, 12);
        Assert.True(other.Two.Mirror.Events >= 2);

        var diverged = s.Rejoin(s.TokenTwo, other.Two.Mirror);
        Assert.Equal(0, diverged.Welcome!.ReplayFrom);
        Assert.Equal(s.LogCount, diverged.Inbox.OfType<EventPublished>().Count());
        Assert.True(diverged.Synchronized);
    }

    [Fact]
    public void AKnownCountBeyondTheLogIsReplayedFromTheStart()
    {
        var s = new Setup();
        s.Drop(s.Two);
        var client = s.Harness.Connect();
        client.SendBytes(Codec.Encode(new Rejoin(s.Code, s.TokenTwo, s.LogCount + 5, new string('a', 64))));
        Assert.Equal(0, client.Welcome!.ReplayFrom);
        Assert.True(client.Synchronized);
    }

    // ---- A command whose answer was lost (design 5.7) ----

    [Fact]
    public void AProcessedCommandWithALostAnswerIsReportedAndNeverReapplied()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 2);
        var bot = new Bot(new Random(1));
        var client = Scenarios.SeatOf(bot.Propose(s.One.Mirror.State).First()) == Seat.One ? s.One : s.Two;
        var command = bot.Propose(client.Mirror.State).First(c => Scenarios.SeatOf(c) == client.Welcome!.Seat);
        var answer = client.Command(command);
        var sent = client.LastSubmit!;
        var token = client.Token!;
        s.Drop(client); // the answer is lost with the connection

        var back = s.Rejoin(token);
        Assert.Equal(answer, back.Welcome!.LastAnswer);
        Assert.Equal(sent.Seq + 1, back.Welcome.NextSeq);

        var count = s.LogCount;
        var again = back.Resend(sent); // newer knownEvents, same id, seq and command
        Assert.True(again is Accepted { Duplicate: true } or Rejected { Duplicate: true });
        Assert.Equal(count, s.LogCount);
    }

    [Fact]
    public void ACommandThatNeverArrivedIsDecidedAsNewAfterTheRejoin()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 2);
        var bot = new Bot(new Random(1));
        var seat = Scenarios.SeatOf(bot.Propose(s.One.Mirror.State).First())!.Value;
        var client = seat == Seat.One ? s.One : s.Two;
        var token = client.Token!;
        var nextSeq = client.Seq + 1;
        var count = s.LogCount;
        s.Drop(client); // the command was cut off by the drop: never processed
        Assert.Equal(count, s.LogCount);

        var back = s.Rejoin(token);
        Assert.Equal(nextSeq, back.Welcome!.NextSeq);
        var accepted = false;
        var attempt = 0;
        foreach (var command in bot.Propose(back.Mirror.State).Where(c => Scenarios.SeatOf(c) == seat))
        {
            var answer = back.Resend(new SubmitCommand($"cut-off-{++attempt}", back.Seq + 1, 0, command));
            Assert.False(answer is Accepted { Duplicate: true } or Rejected { Duplicate: true });
            if (answer is Accepted)
            {
                accepted = true;
                break;
            }
        }
        Assert.True(accepted);
        Assert.True(s.LogCount > count);
    }

    [Fact]
    public void ResendsBeyondTheCacheAreOutOfOrderNotReapplied()
    {
        var s = new Setup();
        var first = s.One.Command(new ActivateModel(Seat.One, new ModelId("zz9")));
        Assert.Equal(RejectionKind.Rules, Assert.IsType<Rejected>(first).Kind);
        var firstSubmit = s.One.LastSubmit!;
        for (var i = 0; i < MatchHost_RememberedAnswers; i++)
        {
            s.One.Command(new ActivateModel(Seat.One, new ModelId("zz9")));
        }
        Assert.Equal(ProtocolError.OutOfOrder, ProtocolCode(s.One.Resend(firstSubmit)));

        // The cache belongs to the seat: it survives a rejoin.
        var recent = s.One.LastSubmit!;
        s.Drop(s.One);
        var back = s.Rejoin(s.TokenOne);
        Assert.True(Assert.IsType<Rejected>(back.Resend(recent)).Duplicate);
    }

    private const int MatchHost_RememberedAnswers = 256;

    // ---- A match that is over: read-only ----

    [Fact]
    public void AFinishedMatchCanBeRejoinedReadOnlyForTheWindowAndNothingChanges()
    {
        var s = new Setup();
        Play.ToEnd(s.One, s.Two, 1);
        var log = s.LogCount;
        var nextSeq = s.Lobby.LogOf(s.Code)!.Count;
        s.Drop(s.Two);
        s.Drop(s.One);
        Assert.Equal(1, s.Lobby.MatchCount);

        var back = s.Rejoin(s.TokenTwo);
        Assert.Equal(MatchStatus.Finished, back.Welcome!.Status);
        Assert.Null(back.Welcome.SeatToken);
        Assert.True(back.Synchronized);
        Assert.NotNull(back.Mirror.State.Outcome);

        Assert.Equal(ProtocolError.MatchFinished, ProtocolCode(back.Command(new EndActivation(Seat.Two))));
        back.Send(new TokenSaved());
        Assert.Equal(ProtocolError.MatchFinished, ProtocolCode(back.LastAnswer));
        Assert.Equal(log, s.LogCount);
        Assert.Equal(nextSeq, s.Lobby.LogOf(s.Code)!.Count);
        Assert.Single(s.Recorder.Closed);

        // The same token still works (it was not rotated), until the window ends.
        s.Drop(back);
        Assert.NotNull(s.Rejoin(s.TokenTwo).Welcome);
        s.Harness.Clients.Values.Where(c => s.Lobby.IsSeated(c.Id)).ToList().ForEach(s.Drop);
        s.Time.Advance(Window);
        s.Lobby.Tick();
        Assert.Equal(0, s.Lobby.MatchCount);
        Assert.Equal(ProtocolError.UnknownMatch, ProtocolCode(s.Harness.Connect().Rejoin(s.Code, s.TokenTwo)));
    }

    [Fact]
    public void AnAbortedMatchRejoinedReadOnlyShowsItsAbort()
    {
        var s = new Setup();
        Play.Commands(s.One, s.Two, 1, 5);
        s.Drop(s.Two);
        s.Time.Advance(Window);
        s.Lobby.Tick();
        var back = s.Rejoin(s.TokenTwo);
        Assert.Equal(MatchStatus.Aborted, back.Welcome!.Status);
        Assert.Equal(AbortReason.RejoinExpired, back.Aborted!.Reason);
        Assert.Equal(ProtocolError.MatchAborted, ProtocolCode(back.Command(new EndActivation(Seat.Two))));
        Assert.Single(s.Recorder.Closed);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task ConcurrentRejoinsLeaveExactlyOneHolderAndEveryStreamIsGapless()
    {
        var time = new ManualTime();
        var inboxes = new ConcurrentDictionary<ConnectionId, ConcurrentQueue<ServerMessage>>();
        var closed = new ConcurrentBag<ConnectionId>();
        var lobby = new Lobby(Scenarios.All, () => new SeededDice(4),
            new Outbox((to, m) => inboxes.GetOrAdd(to, _ => new()).Enqueue(m), closed.Add), () => "C1", 64, time, Window);

        var one = new ConnectionId(1);
        var two = new ConnectionId(2);
        lobby.Receive(one, Codec.Encode(new CreateMatch("ranged")));
        lobby.Receive(two, Codec.Encode(new JoinMatch("C1")));
        var tokenTwo = inboxes[two].OfType<Welcome>().Single().SeatToken!;
        var tokenOne = inboxes[one].OfType<Welcome>().Single().SeatToken!;
        lobby.Disconnect(two);

        // Seat One plays (when it can) while eight connections race to take Seat Two back, over and over.
        var mirror = new MatchMirror();
        foreach (var e in inboxes[one].OfType<EventPublished>())
        {
            mirror.Apply(e);
        }
        var seat2 = Enumerable.Range(0, 8).Select(i => new ConnectionId(100 + i)).ToArray();
        using var start = new ManualResetEventSlim();
        var racers = seat2.Select(id => Task.Run(() =>
        {
            start.Wait();
            for (var round = 0; round < 20; round++)
            {
                var connection = new ConnectionId(id.Value * 1000 + round);
                lobby.Receive(connection, Codec.Encode(new Rejoin("C1", tokenTwo, 0, null)));
            }
        })).ToArray();
        var player = Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 200; i++)
            {
                lobby.Receive(one, Codec.Encode(new SubmitCommand($"p{i}", i + 1, lobby.LogOf("C1")!.Count, new EndActivation(Seat.One))));
            }
        });
        start.Set();
        await Task.WhenAll([.. racers, player]);

        var holder = lobby.ConnectionOf("C1", Seat.Two);
        Assert.NotNull(holder);
        var welcomed = inboxes.Where(kv => kv.Key.Value >= 100_000 && kv.Value.OfType<Welcome>().Any()).Select(kv => kv.Key).ToList();
        Assert.Equal(160, welcomed.Count);
        Assert.Equal(159, welcomed.Count(id => inboxes[id].OfType<Rejected>().Any(r => r.Code == nameof(ProtocolError.SeatTakenOver))));
        Assert.DoesNotContain(holder!.Value, closed);

        // Every connection's events, from its Welcome on, are gapless and match the log.
        var log = lobby.LogOf("C1")!;
        foreach (var id in welcomed)
        {
            var messages = inboxes[id].ToList();
            var welcome = messages.OfType<Welcome>().Single();
            var seqs = messages.OfType<EventPublished>().Select(e => e.Seq).ToList();
            Assert.Equal(Enumerable.Range(welcome.ReplayFrom + 1, seqs.Count), seqs);
            Assert.True(seqs.Count >= welcome.Events - welcome.ReplayFrom);
            foreach (var e in messages.OfType<EventPublished>())
            {
                Assert.Equal(log[e.Seq - 1].Hash, e.Hash);
            }
        }
        Assert.NotNull(tokenOne);
    }
}
