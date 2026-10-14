using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

public class ScenarioFixtureTests
{
    [Fact]
    [Trait("Rule", "Scenario I pp. 151-152: zones 24\" apart, markers 6\" in from the zone edge and the table edge, plus the midpoint")]
    public void TheFixtureFollowsTheMapOfClaimNoMansLand()
    {
        var field = ClaimNoMansLand.Create().Battlefield;
        var west = field.Zone("west")!.Area.Bounds;
        var east = field.Zone("east")!.Area.Bounds;

        Assert.Equal(L("24"), east.Min.X - west.Max.X);
        Assert.Equal((L("36"), L("36")), (field.Width, field.Height));
        var centres = ClaimNoMansLand.Create().ObjectiveIds
            .Select(id => field.Terrain.First(t => t.Id == id).Footprint.Bounds)
            .Select(b => (X: (b.Min.X.Thousandths + b.Max.X.Thousandths) / 2, Y: (b.Min.Y.Thousandths + b.Max.Y.Thousandths) / 2))
            .OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        Assert.Equal(
            [(12000L, 6000L), (12000L, 30000L), (18000L, 18000L), (24000L, 6000L), (24000L, 30000L)],
            centres);
        Assert.Equal(L("6"), west.Max.X - west.Min.X); // zone depth
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152: models are set up wholly within their zone")]
    public void NoTerrainBlocksADeploymentZone()
    {
        var field = ClaimNoMansLand.Create().Battlefield;
        foreach (var zone in field.Zones)
        {
            foreach (var terrain in field.Terrain)
            {
                var overlap = zone.Area.Bounds.Min.X < terrain.Footprint.Bounds.Max.X && terrain.Footprint.Bounds.Min.X < zone.Area.Bounds.Max.X
                    && zone.Area.Bounds.Min.Y < terrain.Footprint.Bounds.Max.Y && terrain.Footprint.Bounds.Min.Y < zone.Area.Bounds.Max.Y;
                Assert.False(overlap, $"{terrain.Id} overlaps {zone.ZoneId}");
            }
        }
    }

    [Fact]
    [Trait("Rule", "Architecture: scenario validation")]
    public void AScenarioChecksItsObjectivesAndZones()
    {
        var field = ClaimNoMansLand.Create().Battlefield;
        Assert.Throws<ArgumentException>(() => new Scenario(field, ["nowhere"]));
        Assert.Throws<ArgumentException>(() => new Scenario(field, ["wall-1"])); // not walkable
        Assert.Throws<ArgumentException>(() => new Scenario(field, ["obj-1", "obj-1"]));

        var models = new[] { new SetupModel(new ModelId("a1"), Seat.One, Rifleman), new SetupModel(new ModelId("b1"), Seat.Two, Rifleman) };
        Assert.Throws<ArgumentException>(() => new MatchSetup("1.0.2", "x", 4, RulesProfile.Default, ["north", "south"], models, ClaimNoMansLand.Create()));
        Assert.Throws<ArgumentException>(() => new MatchSetup(
            "1.0.2", "x", 4, RulesProfile.Default, ["west", "east"],
            [new SetupModel(new ModelId("a1"), Seat.One), new SetupModel(new ModelId("b1"), Seat.Two, Rifleman)],
            ClaimNoMansLand.Create()));
    }
}

public class ProfileAndHashTests
{
    [Fact]
    [Trait("Rule", "Architecture: profile validation")]
    public void AProfileRejectsImpossibleValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(Length.Zero, L("0.5"), L("12")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), Length.Zero, L("12")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("-1")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("12"), armour: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("12"), armour: -4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("12"), rangedInjuryModifier: 4));
        Assert.Throws<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), Length.FromThousandths(12_001)));
        Assert.False(new ModelProfile(L("6"), L("0.5"), Length.Zero).HasRangedWeapon);
    }

    [Fact]
    [Trait("Rule", "Architecture: canonical state hash covers every M4 field")]
    public void EveryM4FieldChangesTheHash()
    {
        var play = new BoardPlay().ToActivation();
        play.Do(new ActivateModel(Seat.One, A(1)));
        var s = play.State;
        var variants = new Dictionary<string, MatchState>
        {
            ["position"] = s with { Models = [.. s.Models.Select(m => m.Id == A(1) ? m with { Position = P("3", "3") } : m)] },
            ["markers"] = s with { Models = [.. s.Models.Select(m => m.Id == A(1) ? m with { BloodMarkers = 1 } : m)] },
            ["actions"] = s with { Actions = ActionsTaken.Move },
            ["halved"] = s with { MovementHalved = true },
            ["scoredTurn"] = s with { ScoredTurn = 1 },
            ["vp"] = s with { One = s.One with { VictoryPoints = 2 } },
            ["inProgress"] = s with { InProgress = new RollInProgress(RollStage.ShotRoll, A(1), B(1), 0, 1) },
            ["inProgressPath"] = s with { InProgress = new RollInProgress(RollStage.DashMove, A(1), DashPath: Path(("4", "3"))) },
            ["profile"] = s with { Setup = Rebuild(s.Setup!, new ModelProfile(L("7"), L("0.5"), L("18"))) },
            ["terrain"] = s with { Setup = Rebuild(s.Setup!, Rifleman, ClaimNoMansLandWithoutRough()) },
        };
        var baseline = StateHash.Compute(s);
        foreach (var (name, variant) in variants)
        {
            Assert.NotEqual(baseline, StateHash.Compute(variant));
            Assert.True(variants.Values.Count(v => StateHash.Compute(v) == StateHash.Compute(variant)) == 1, name);
        }
    }

    private static Scenario ClaimNoMansLandWithoutRough()
    {
        var full = ClaimNoMansLand.Create();
        var battlefield = full.Battlefield;
        return new Scenario(
            new Battlefield(battlefield.Width, battlefield.Height, battlefield.Terrain.Where(t => t.Id != "rough-2"), battlefield.Zones),
            full.ObjectiveIds);
    }

    private static MatchSetup Rebuild(MatchSetup setup, ModelProfile a1Profile, Scenario? scenario = null) =>
        new(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id.Value == "A1" ? m with { Profile = a1Profile } : m),
            scenario ?? setup.Scenario);

    [Fact]
    [Trait("Rule", "Architecture: LegalCommands lists decisions only for geometric choices")]
    public void LegalCommandsOffersTheMarkerChoicesAndLeavesGeometricCommandsToThePlayer()
    {
        var play = new BoardPlay().ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2").Mark(A(1), 2);
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal([new EndActivation(Seat.One)], TurnSequence.LegalCommands(play.State));

        play.Do(new Shoot(Seat.One, A(1), B(1)));
        var legal = TurnSequence.LegalCommands(play.State);
        Assert.Equal([new SpendMarkers(Seat.Two, 0), new SpendMarkers(Seat.Two, 1), new SpendMarkers(Seat.Two, 2)], legal);
        Assert.All(legal, c => Assert.True(TurnSequence.Decide(play.State, c, new SeededDiceForTests()).IsAccepted));

        var deploying = new BoardPlay();
        deploying.Do(new StartMatch(), 6, 1);
        deploying.Do(new ChooseDeploymentZone(Seat.One, "west"), 5, 2);
        Assert.Empty(TurnSequence.LegalCommands(deploying.State));
    }

    private sealed class SeededDiceForTests : Rules.Core.Dice.IDiceSource
    {
        private readonly Random _random = new(3);
        public int RollD6() => _random.Next(1, 7);
    }
}
