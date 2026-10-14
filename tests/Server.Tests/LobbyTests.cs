using System.Text;
using Match.Protocol;
using Match.Server;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

public class LobbyTests
{
    private static ProtocolError ProtocolCode(ServerMessage? answer)
    {
        var rejected = Assert.IsType<Rejected>(answer);
        Assert.Equal(RejectionKind.Protocol, rejected.Kind);
        return Enum.Parse<ProtocolError>(rejected.Code);
    }

    // Plays the match to its end: each command goes from the connection of the seat it is for.
    private static void PlayOut(Harness harness, TestClient one, TestClient two, int seed, int maxCommands = 5000)
    {
        var bot = new Bot(new Random(seed));
        for (var n = 0; n < maxCommands && one.Mirror.State.Phase != Phase.Ended; n++)
        {
            var accepted = false;
            foreach (var command in bot.Propose(one.Mirror.State))
            {
                var client = Scenarios.SeatOf(command) == Seat.One ? one : two;
                if (client.Command(command) is Accepted)
                {
                    accepted = true;
                    break;
                }
            }
            Assert.True(accepted, "the bot found no legal command");
        }
        Assert.Equal(Phase.Ended, one.Mirror.State.Phase);
    }

    [Fact]
    public void TwoClientsGetTheirSeatsAndTheSameEvents()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();

        Assert.Equal(Seat.One, one.Welcome!.Seat);
        Assert.Equal(Seat.Two, two.Welcome!.Seat);
        Assert.Equal(one.Welcome.Code, two.Welcome.Code);
        Assert.Contains(one.Notices, n => n.Kind == NoticeKind.OpponentJoined);

        // The server started the match: MatchCreated, then StartMatch's events, the same for both.
        var log = harness.Lobby.LogOf(one.Welcome.Code)!;
        Assert.True(log.Count > 1);
        Assert.Equal(log.Count, one.Mirror.Events);
        Assert.Equal(log.Count, two.Mirror.Events);
        Assert.Equal(log[^1].Hash, one.Mirror.Hash);
        Assert.Equal(log[^1].Hash, two.Mirror.Hash);
        Assert.Equal(MatchStatus.Playing, harness.Lobby.StatusOf(one.Welcome.Code));
    }

    [Theory]
    [InlineData("ranged", 1)]
    [InlineData("ranged", 2)]
    [InlineData("melee", 3)]
    [InlineData("melee", 366)]
    public void AWholeMatchPlayedThroughTheServerReplaysToTheSameHashes(string scenario, int seed)
    {
        var harness = new Harness(seed);
        var (one, two) = harness.Pair(scenario);
        PlayOut(harness, one, two, seed);

        var log = harness.Lobby.LogOf(one.Welcome!.Code)!;
        Assert.Equal(Enumerable.Range(1, log.Count), log.Select(e => e.Seq));
        Assert.Equal(log[^1].Hash, one.Mirror.Hash);
        Assert.Equal(log[^1].Hash, two.Mirror.Hash);
        Assert.Equal(log[^1].Hash, StateHash.Compute(Reducer.Replay(log.Select(e => e.Event))));
        Assert.NotNull(one.Mirror.State.Outcome);

        // After the end the rules refuse everything, and nothing changes.
        var answer = Assert.IsType<Rejected>(one.Command(new EndActivation(Seat.One)));
        Assert.Equal(RejectionKind.Rules, answer.Kind);
        Assert.Equal(log.Count, harness.Lobby.LogOf(one.Welcome.Code)!.Count);
    }

    [Fact]
    public void TheSameDiceGiveTheSameMatch()
    {
        string Play()
        {
            var harness = new Harness(9);
            var (one, two) = harness.Pair("melee");
            PlayOut(harness, one, two, 9);
            return one.Mirror.Hash;
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void ARulesRefusalChangesNothingRollsNothingAndUsesUpItsNumber()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();
        var code = one.Welcome!.Code;
        var events = harness.Lobby.LogOf(code)!.Count;
        var rolls = harness.Dice!.Rolls;

        var answer = Assert.IsType<Rejected>(one.Command(new ActivateModel(Seat.One, new ModelId("zz9"))));
        Assert.Equal(RejectionKind.Rules, answer.Kind);
        Assert.Equal(2, answer.NextSeq);
        Assert.Equal(events, harness.Lobby.LogOf(code)!.Count);
        Assert.Equal(rolls, harness.Dice.Rolls);
        Assert.Equal(events, two.Mirror.Events);
        Assert.Empty(two.Answers);
    }

    [Fact]
    public void AConnectionCannotCommandTheOtherSeat()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();
        var code = one.Welcome!.Code;
        var events = harness.Lobby.LogOf(code)!.Count;
        var rolls = harness.Dice!.Rolls;

        // Whatever the other seat could legally do now, seat One cannot send it.
        foreach (var command in TurnSequence.LegalCommands(two.Mirror.State).Where(c => Scenarios.SeatOf(c) == Seat.Two).Take(5)
                     .Append(new ChooseDeploymentZone(Seat.Two, "north")))
        {
            Assert.Equal(ProtocolError.SeatMismatch, ProtocolCode(one.Command(command)));
        }
        Assert.Equal(events, harness.Lobby.LogOf(code)!.Count);
        Assert.Equal(rolls, harness.Dice.Rolls);
        Assert.Equal(0, one.Seq);
    }

    [Fact]
    public void OnlyTheServerStartsTheMatch()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        Assert.Equal(ProtocolError.ServerOnlyCommand, ProtocolCode(one.Command(new StartMatch())));
    }

    [Fact]
    public void AResendGetsTheSameAnswerAndIsNotAppliedTwice()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();
        var code = one.Welcome!.Code;
        var actor = TurnSequence.LegalCommands(one.Mirror.State).First();
        var client = Scenarios.SeatOf(actor) == Seat.One ? one : two;

        var submit = new SubmitCommand("first", 1, client.Mirror.Events, actor);
        client.Send(submit);
        var original = Assert.IsType<Accepted>(client.LastAnswer);
        var events = harness.Lobby.LogOf(code)!.Count;
        var rolls = harness.Dice!.Rolls;

        // Same id, seq and command (even with a newer event count): the original answer, marked as a duplicate.
        client.Send(new SubmitCommand("first", 1, events, actor));
        Assert.Equal(original with { Duplicate = true }, client.LastAnswer);
        Assert.Equal(events, harness.Lobby.LogOf(code)!.Count);
        Assert.Equal(rolls, harness.Dice.Rolls);

        // Same id for something else: refused, and the id keeps its first meaning.
        client.Send(new SubmitCommand("first", 2, events, new EndActivation(client.Welcome!.Seat)));
        Assert.Equal(ProtocolError.IdReused, ProtocolCode(client.LastAnswer));
        client.Send(new SubmitCommand("first", 1, events, new EndActivation(client.Welcome!.Seat)));
        Assert.Equal(ProtocolError.IdReused, ProtocolCode(client.LastAnswer));
    }

    [Fact]
    public void ARulesRefusalIsAlsoRememberedForResends()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        var bad = new ActivateModel(Seat.One, new ModelId("zz9"));
        one.Send(new SubmitCommand("bad", 1, one.Mirror.Events, bad));
        var original = Assert.IsType<Rejected>(one.LastAnswer);
        one.Send(new SubmitCommand("bad", 1, one.Mirror.Events, bad));
        Assert.Equal(original with { Duplicate = true }, one.LastAnswer);
    }

    [Fact]
    public void CommandNumbersMustFollowOn()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();
        var code = one.Welcome!.Code;
        var events = harness.Lobby.LogOf(code)!.Count;
        var bad = new ActivateModel(Seat.One, new ModelId("zz9"));

        // A gap, or a number from the past, is refused and uses nothing up.
        one.Send(new SubmitCommand("a", 2, events, bad));
        Assert.Equal(ProtocolError.OutOfOrder, ProtocolCode(one.LastAnswer));
        Assert.Equal(1, ((Rejected)one.LastAnswer!).NextSeq);
        one.Send(new SubmitCommand("b", 1, events, bad));
        Assert.Equal(RejectionKind.Rules, Assert.IsType<Rejected>(one.LastAnswer).Kind);
        one.Send(new SubmitCommand("c", 1, events, bad));
        Assert.Equal(ProtocolError.OutOfOrder, ProtocolCode(one.LastAnswer));
        Assert.Equal(2, ((Rejected)one.LastAnswer!).NextSeq);

        // Each seat counts its own commands.
        two.Send(new SubmitCommand("a", 1, events, new ActivateModel(Seat.Two, new ModelId("zz9"))));
        Assert.Equal(RejectionKind.Rules, Assert.IsType<Rejected>(two.LastAnswer).Kind);
    }

    [Fact]
    public void ACommandBuiltOnAnOldStateIsStale()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        var events = one.Mirror.Events;
        foreach (var known in new[] { 0, events - 1, events + 1 })
        {
            one.Send(new SubmitCommand($"k{known}", 1, known, new ActivateModel(Seat.One, new ModelId("zz9"))));
            var rejected = Assert.IsType<Rejected>(one.LastAnswer);
            Assert.Equal(ProtocolError.Stale.ToString(), rejected.Code);
            Assert.Equal(events, rejected.Events);
        }
    }

    [Fact]
    public void SeatsCannotBeTakenTwiceOrChosen()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        var code = one.Welcome!.Code;

        var third = harness.Connect();
        third.Send(new JoinMatch(code));
        Assert.Equal(ProtocolError.MatchFull, ProtocolCode(third.LastAnswer));
        Assert.Null(third.Welcome);
        Assert.Equal(0, third.Mirror.Events);

        // A connection keeps the one seat it has.
        one.Send(new JoinMatch(code));
        Assert.Equal(ProtocolError.AlreadyJoined, ProtocolCode(one.LastAnswer));
        one.Send(new CreateMatch("ranged"));
        Assert.Equal(ProtocolError.AlreadyJoined, ProtocolCode(one.LastAnswer));

        // There is no field to ask for a seat with.
        third.SendRaw($$"""{"v":1,"type":"JoinMatch","code":"{{code}}","seat":"One"}""");
        Assert.Equal(ProtocolError.Malformed, ProtocolCode(third.LastAnswer));
    }

    [Fact]
    public void LobbyRefusals()
    {
        var harness = new Harness(maxMatches: 1);
        var a = harness.Connect();
        a.Command(new EndActivation(Seat.One));
        Assert.Equal(ProtocolError.NotJoined, ProtocolCode(a.LastAnswer));
        a.Send(new JoinMatch("NOPE"));
        Assert.Equal(ProtocolError.UnknownMatch, ProtocolCode(a.LastAnswer));
        a.Send(new CreateMatch("chess"));
        Assert.Equal(ProtocolError.UnknownScenario, ProtocolCode(a.LastAnswer));

        a.Send(new CreateMatch("ranged"));
        Assert.NotNull(a.Welcome);
        Assert.Equal(1, a.Mirror.Events);
        a.Command(new EndActivation(Seat.One));
        Assert.Equal(ProtocolError.MatchNotStarted, ProtocolCode(a.LastAnswer));

        var b = harness.Connect();
        b.Send(new CreateMatch("ranged"));
        Assert.Equal(ProtocolError.ServerBusy, ProtocolCode(b.LastAnswer));
    }

    [Fact]
    public void GarbageIsAnsweredAndNeverThrows()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        var code = one.Welcome!.Code;
        var events = harness.Lobby.LogOf(code)!.Count;
        var random = new Random(3);
        foreach (var _ in Enumerable.Range(0, 2000))
        {
            var bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);
            one.SendBytes(bytes);
            Assert.IsType<Rejected>(one.LastAnswer);
        }
        one.SendBytes(new byte[ProtocolLimits.MaxClientMessageBytes + 1]);
        Assert.Equal(ProtocolError.TooLarge, ProtocolCode(one.LastAnswer));
        Assert.Equal(events, harness.Lobby.LogOf(code)!.Count);
        Assert.Equal(MatchStatus.Playing, harness.Lobby.StatusOf(code));
    }

    [Fact]
    public void ForgedOutcomesNeverReachTheRules()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        var events = one.Mirror.Events;
        var rolls = harness.Dice!.Rolls;
        var forgeries = new[]
        {
            $$$"""{"v":1,"type":"SubmitCommand","commandId":"f1","seq":1,"knownEvents":{{{events}}},"command":{"type":"InjuryRolled","target":"b1"}}""",
            """{"v":1,"type":"SubmitCommand","commandId":"f2","seq":1,"knownEvents":""" + events + ""","command":{"type":"DashModel","seat":"One","model":"a1","path":[],"roll":{"rolled":[6,6],"kept":[6,6]}}}""",
            $$$"""{"v":1,"type":"SubmitCommand","commandId":"f3","seq":1,"knownEvents":{{{events}}},"hash":"00","command":{"type":"EndActivation","seat":"One"}}""",
            $$$"""{"v":1,"type":"EventPublished","seq":{{{events + 1}}},"hash":"00","event":{"type":"TurnEnded","turn":1}}""",
            $$"""{"v":1,"type":"Accepted","commandId":"f4","seq":1,"firstEvent":1,"lastEvent":1,"duplicate":false}""",
        };
        foreach (var json in forgeries)
        {
            one.SendRaw(json);
            Assert.IsType<Rejected>(one.LastAnswer);
        }
        Assert.Equal(events, one.Mirror.Events);
        Assert.Equal(rolls, harness.Dice.Rolls);
    }

    [Fact]
    public void ADisconnectClosesTheMatchAndTellsTheOpponent()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();
        var code = one.Welcome!.Code;
        var events = harness.Lobby.LogOf(code)!.Count;

        harness.Lobby.Disconnect(two.Id);
        one.Pump();
        Assert.Contains(one.Notices, n => n.Kind == NoticeKind.OpponentDisconnected);
        Assert.Equal(MatchStatus.Closed, harness.Lobby.StatusOf(code));
        Assert.Equal(ProtocolError.MatchClosed, ProtocolCode(one.Command(new EndActivation(Seat.One))));
        Assert.Equal(events, harness.Lobby.LogOf(code)!.Count);

        // Nobody can take the empty seat (rejoining is M6).
        var other = harness.Connect();
        other.Send(new JoinMatch(code));
        Assert.Equal(ProtocolError.MatchFull, ProtocolCode(other.LastAnswer));

        // When both have gone, the match is gone.
        harness.Lobby.Disconnect(one.Id);
        Assert.Null(harness.Lobby.LogOf(code));
        harness.Lobby.Disconnect(one.Id);
    }

    [Fact]
    public void ACreatorLeavingBeforeAnOpponentRemovesTheMatch()
    {
        var harness = new Harness();
        var one = harness.Connect();
        one.Send(new CreateMatch("ranged"));
        harness.Lobby.Disconnect(one.Id);
        Assert.Equal(0, harness.Lobby.MatchCount);
    }

    [Fact]
    public void ShutdownTellsEveryoneAndRefusesEverythingAfter()
    {
        var harness = new Harness();
        var (one, two) = harness.Pair();
        var waiting = harness.Connect();
        waiting.Send(new CreateMatch("melee"));

        harness.Lobby.Shutdown();
        foreach (var client in new[] { one, two, waiting })
        {
            client.Pump();
            Assert.Contains(client.Notices, n => n.Kind == NoticeKind.ServerShutdown);
        }
        Assert.Equal(ProtocolError.ShuttingDown, ProtocolCode(one.Command(new EndActivation(Seat.One))));
        var late = harness.Connect();
        late.Send(new CreateMatch("ranged"));
        Assert.Equal(ProtocolError.ShuttingDown, ProtocolCode(late.LastAnswer));
    }

    // If the rules ever throw, the match stops rather than guessing; the log so far stays consistent.
    [Fact]
    public void AnInternalErrorClosesTheMatch()
    {
        var harness = new Harness(dice: () => new ThrowingDice());
        var one = harness.Connect();
        one.Send(new CreateMatch("ranged"));
        var two = harness.Connect();
        two.Send(new JoinMatch(one.Welcome!.Code));

        // StartMatch rolls off for deployment zones, which this dice source refuses.
        Assert.Contains(one.Notices, n => n.Kind == NoticeKind.MatchFaulted);
        Assert.Contains(two.Notices, n => n.Kind == NoticeKind.MatchFaulted);
        Assert.Equal(MatchStatus.Closed, harness.Lobby.StatusOf(one.Welcome.Code));
        Assert.Single(harness.Lobby.LogOf(one.Welcome.Code)!);
        Assert.Equal(1, two.Mirror.Events);
    }

    [Fact]
    public void TooManyWaypointsIsARulesRefusal()
    {
        var harness = new Harness();
        var (one, _) = harness.Pair();
        var at = new Point2(Length.FromInches(3), Length.FromInches(3));
        var answer = Assert.IsType<Rejected>(one.Command(new MoveModel(Seat.One, new ModelId("a1"), [.. Enumerable.Repeat(at, 200)])));
        Assert.Equal(RejectionKind.Rules, answer.Kind);
    }
}
