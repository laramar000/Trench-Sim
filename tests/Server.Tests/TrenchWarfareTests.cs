using Match.Protocol;
using Match.Server;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Trench-maps stage S6: Scenario IV, Trench Warfare (pp. 161-163): the roles roll-off, the defender's trench zone, the
// attacker deploying first, trench-section scoring at the end of each Turn, and the game-length roll after Turn 4.
public class TrenchWarfareTests
{
    private const string Name = "ranged-1v1" + ScenarioSetups.TrenchWarfareSuffix;

    private static List<GameEvent> Played(string scenario, int seed)
    {
        var harness = new Harness(seed);
        var (one, two) = harness.Pair(scenario);
        Play.ToEnd(one, two, seed);
        return [.. harness.Lobby.LogOf(one.Welcome!.Code)!.Select(p => p.Event)];
    }

    private static MatchState Replay(IEnumerable<GameEvent> events) => events.Aggregate(MatchState.None, Reducer.Apply);

    [Fact]
    public void TheServerOffersTrenchWarfareOnItsMap()
    {
        foreach (var name in new[] { "ranged", "melee", "ranged-1v1", "ranged-2v2", "ranged-3v3", "melee-1v1", "melee-2v2", "melee-3v3" })
        {
            var setup = Scenarios.All[name + ScenarioSetups.TrenchWarfareSuffix];
            Assert.Equal(TrenchWarfare.ScenarioId, setup.ScenarioId);
            Assert.Equal(TrenchWarfare.TurnLimit, setup.TurnLimit);
            Assert.Equal(Fingerprint(Scenarios.All[name].Models), Fingerprint(setup.Models));
        }
        var field = Scenarios.All[Name].Scenario!.Battlefield;
        Assert.Equal(["trench-m", "trench-n", "trench-s"], TrenchWarfare.ScoringSections(field).Select(t => t.Id));
        Assert.Equal(TrenchWarfare.East, TrenchWarfare.DefenderZone(field));
    }

    private static string Fingerprint(IEnumerable<SetupModel> models) => string.Join(";", models.Select(m => $"{m.Id.Value}:{m.Seat}:{m.Profile!.BaseRadius}"));

    [Fact]
    public void OnlyAMapWithTheDefendersTrenchLinesFits()
    {
        var maps = ScenarioCatalog.Maps;
        var definition = TrenchWarfare.Definition;
        Assert.Empty(definition.Unmet(maps[ScenarioSetups.TrenchWarfareMap].ToScenario(), Archetype.NoMansLand));
        // The plain map has no trenches and five Objectives.
        Assert.Equal(2, definition.Unmet(maps[ScenarioSetups.PlainMap].ToScenario(), Archetype.NoMansLand).Length);
        // The tower map has 8" trench sections in both zones, and is not made for Trench Warfare.
        Assert.Contains(definition.Unmet(maps[ScenarioSetups.TowersMap].ToScenario(), null), u => u.Contains("in more than one zone", StringComparison.Ordinal));
        Assert.Throws<MapFormatException>(() => ScenarioSetups.OnMap(Scenarios.All[Name], maps[ScenarioSetups.TowersMap]));
        // A piece in a deployment zone that is not the defender's trench.
        var map = maps[ScenarioSetups.TrenchWarfareMap];
        var extra = new TerrainBlock("crate", Footprint.Of(new Rect(new Point2(Length.FromInches(1), Length.FromInches(1)), new Point2(Length.FromInches(3), Length.FromInches(3)))), TerrainProfile.Cover);
        var crowded = new Scenario(new Battlefield(map.Battlefield.Width, map.Battlefield.Height, [.. map.Battlefield.Terrain, extra], map.Battlefield.Zones), []);
        Assert.Equal(["trench-warfare sets up no terrain in the deployment zones but the defender's trenches; 'crate' is in one."], definition.Unmet(crowded, null).AsEnumerable());
        // And a Claim No Man's Land map does not take it.
        Assert.Throws<MapFormatException>(() => ScenarioSetups.OnMap(Scenarios.All["ranged"], map));
    }

    [Fact]
    public void TheRolesRollOffWinnerChoosesWhoAttacksAndTheDefenderTakesTheTrenches()
    {
        var setup = Scenarios.All[Name];
        var state = Reducer.Apply(MatchState.None, new MatchCreated(setup));
        var start = TurnSequence.Decide(state, new StartMatch(), new SeededDice(3));
        Assert.True(start.IsAccepted);
        var roll = Assert.IsType<RollOffResolved>(start.Events[0]);
        Assert.Equal(RollOffPurpose.Roles, roll.Purpose);
        Assert.Equal(new DecisionRequested(DecisionKind.ChooseAttacker, roll.Winner), start.Events[^1]);
        state = start.State!;
        Assert.Equal([new ChooseAttacker(roll.Winner, Seat.One), new ChooseAttacker(roll.Winner, Seat.Two)], TurnSequence.LegalCommands(state));
        Assert.False(TurnSequence.Decide(state, new ChooseAttacker(roll.Winner.Opponent(), Seat.One), new SeededDice(1)).IsAccepted);
        Assert.False(TurnSequence.Decide(state, new ChooseDeploymentZone(roll.Winner, "west"), new SeededDice(1)).IsAccepted);

        var chosen = TurnSequence.Decide(state, new ChooseAttacker(roll.Winner, Seat.Two), new SeededDice(1));
        // The defender sets up the Mine Markers next, before anyone deploys (p. 162).
        Assert.Equal(new GameEvent[] { new AttackerChosen(roll.Winner, Seat.Two), new DecisionRequested(DecisionKind.LayMines, Seat.One) }, chosen.Events.ToArray());
        state = chosen.State!;
        var laid = TurnSequence.Decide(state, Assert.Single(TurnSequence.LegalCommands(state)), new SeededDice(1));
        Assert.IsType<MinesLaid>(laid.Events[0]);
        Assert.Equal(new DeploymentOrderSet(Seat.Two), laid.Events[^1]);
        state = laid.State!;
        Assert.Equal(Seat.Two, state.Attacker);
        Assert.Equal("west", state.Two.ZoneId);
        Assert.Equal("east", state.One.ZoneId);
        Assert.Contains("attacker=Two", StateHash.Canonical(state));
    }

    [Fact]
    public void AWholeMatchScoresTheTrenchesEveryTurnAndEndsAfterTurnFourOrFive()
    {
        var lengths = new HashSet<int>();
        foreach (var seed in Enumerable.Range(1, 12))
        {
            var events = Played(Name, seed);
            var end = Replay(events);
            Assert.Equal(Phase.Ended, end.Phase);
            var scored = events.OfType<TrenchesScored>().ToList();
            Assert.Equal(Enumerable.Range(1, scored.Count), scored.Select(s => s.Turn));
            Assert.All(scored, s => Assert.Equal(["trench-m", "trench-n", "trench-s"], s.Results.Select(r => r.TrenchId)));
            Assert.Empty(events.OfType<ObjectivesScored>());
            if (end.Outcome!.Reason == OutcomeReason.VictoryPoints)
            {
                var length = Assert.Single(events.OfType<GameLengthRolled>());
                Assert.Equal(end.Attacker, length.Seat);
                Assert.Equal(length.LastTurn, end.Turn);
                lengths.Add(length.LastTurn);
                // The points add up to the Victory Points, with 1 for each Glorious Deed (U96).
                var attacker = end.Attacker!.Value;
                var deeds = events.OfType<GloriousDeedCompleted>().ToList();
                Assert.Equal(scored.Sum(s => s.Results.Sum(r => r.AttackerPoints)) + deeds.Count(d => d.Seat == attacker), end.SeatFor(attacker).VictoryPoints);
                Assert.Equal(scored.Sum(s => s.Results.Sum(r => r.DefenderPoints)) + deeds.Count(d => d.Seat != attacker), end.SeatFor(attacker.Opponent()).VictoryPoints);
            }
        }
        Assert.NotEmpty(lengths);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 2, 0)]
    [InlineData(2, 1, 1, 2)]
    [InlineData(0, 3, 0, 3)]
    public void ASectionScoresByWhoIsInIt(int defenders, int attackers, int defenderPoints, int attackerPoints) =>
        Assert.Equal((defenderPoints, attackerPoints), TrenchWarfare.Points(defenders, attackers));

    [Fact]
    public void ModelsInATrenchSectionAreCountedForIt()
    {
        // A match that reaches the end of Turn 1 (a 1v1 match can end sooner, a mine taking out the only attacker).
        var events = Enumerable.Range(2, 20).Select(seed => Played(Name, seed)).First(e => e.Any(x => x is TrenchesScored));
        // The state just before the first scoring, with one model of each side moved into trench-m.
        var index = events.FindIndex(e => e is TrenchesScored);
        var before = Replay(events.Take(index));
        var attacker = before.Attacker!.Value;
        var a = before.Models.First(m => m.Seat == attacker && m.Position is not null);
        var d = before.Models.First(m => m.Seat != attacker && m.Position is not null);
        var moved = before with
        {
            Models = [.. before.Models.Select(m => m.Id == a.Id ? m with { Position = new Point2(Length.Parse("32.75"), Length.FromInches(16)) }
                : m.Id == d.Id ? m with { Position = new Point2(Length.Parse("32.75"), Length.FromInches(20)) } : m)],
        };
        var m = TrenchScores.Now(moved).Single(r => r.TrenchId == "trench-m");
        Assert.Equal(new TrenchResult("trench-m", 1, 1, TrenchWarfare.DefenderHeld, TrenchWarfare.AttackerHeld), m);
    }

    [Fact]
    public void ALogThatScoresWronglyOrSkipsTheLengthRollIsRefused()
    {
        var events = Played(Name, 4);
        var index = events.FindIndex(e => e is TrenchesScored);
        var scored = (TrenchesScored)events[index];
        var forged = new TrenchesScored(scored.Turn, scored.Results.Select(r => r with { AttackerPoints = r.AttackerPoints + 1 }));
        Assert.Throws<InvalidEventException>(() => Replay([.. events.Take(index), forged]));

        if (events.FindIndex(e => e is GameLengthRolled) is var roll and >= 0)
        {
            var g = (GameLengthRolled)events[roll];
            Assert.Throws<InvalidEventException>(() => Replay([.. events.Take(roll), g with { LastTurn = g.LastTurn == 4 ? 5 : 4 }]));
            Assert.Throws<InvalidEventException>(() => Replay([.. events.Take(roll), g with { Seat = g.Seat.Opponent() }]));
            // Skipping it: Turn 5 cannot start, nor can the match end, without the roll.
            Assert.Throws<InvalidEventException>(() => Replay([.. events.Take(roll), new TurnStarted(5)]));
            Assert.Throws<InvalidEventException>(() => Replay([.. events.Take(roll), new MatchEnded(null, OutcomeReason.VictoryPoints)]));
        }
    }

    [Fact]
    public void TheRolesRollOffIsNeededUsedOnceAndDecidesWhoChooses()
    {
        var events = Played(Name, 6);
        var i = events.FindIndex(e => e is RollOffResolved { Purpose: RollOffPurpose.Roles });
        var roll = (RollOffResolved)events[i];
        static List<GameEvent> Edited(List<GameEvent> log, Action<List<GameEvent>> edit)
        {
            var copy = log.ToList();
            edit(copy);
            return copy;
        }
        var flipped = new RollOffResolved(roll.Purpose, roll.Rounds.Select(x => new RollOffRound(x.SeatTwo, x.SeatOne)), roll.Winner.Opponent());
        var forged = new[]
        {
            Edited(events, l => l.RemoveAt(i)),
            Edited(events, l => l.Insert(i + 1, roll)),
            Edited(events, l => l[i] = flipped),
            Edited(events, l => (l[i], l[i + 1]) = (l[i + 1], l[i])),
        };
        foreach (var log in forged)
        {
            Assert.Throws<InvalidEventException>(() => Replay(log));
        }
        Replay(events);
    }

    [Fact]
    public void TheRecordVerifiesAndTheCardDescribesTheScenario()
    {
        var record = TestRecords.Completed(Name, 5);
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var verdict = RecordVerifier.Verify(RecordFormat.Sign(record, key), [new TrustedKey(RecordKeys.KeyIdOf(key), key)]);
        Assert.Equal(VerdictKind.Completed, verdict.Kind);
        Assert.Equal(ScenarioSetups.TrenchWarfareMap, record.Map);

        var rules = ScenarioRules.Of(Scenarios.All[Name])!;
        Assert.Equal("Trench Warfare (Scenario IV)", rules.Name);
        Assert.Contains(rules.Provisional, p => p.Contains("Mine Markers", StringComparison.Ordinal));
        Assert.Contains(rules.Scoring, p => p.Contains("trench-m, trench-n, trench-s", StringComparison.Ordinal));
    }

    [Fact]
    public void ClaimNoMansLandHasNoRolesAndNoLengthRoll()
    {
        var events = Played("ranged-1v1", 1);
        Assert.DoesNotContain(events, e => e is AttackerChosen or TrenchesScored or GameLengthRolled);
        var end = Replay(events);
        Assert.Null(end.Attacker);
        Assert.Null(end.LastTurn);
        Assert.DoesNotContain("attacker=", StateHash.Canonical(end));
        Assert.DoesNotContain("lastTurn=", StateHash.Canonical(end));
    }
}
