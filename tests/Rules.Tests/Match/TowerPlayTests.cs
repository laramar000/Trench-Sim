using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Towers proposal, stage T2: commands on the tower layout, through the server's own TurnSequence (U66 to U74). The south
// tower stands at x 16.5-19.5", y 5-8", 2" high; for a 1" base its west ladder's foot is (16, 6.5) and its top (17, 6.5).
public class TowerPlayTests
{
    private static readonly Scenario Towers = ClaimNoMansLand.CreateWithTowers();

    private static BoardPlay On(string ax, string ay, string bx, string by, ModelProfile? profile = null) =>
        SkirmishWith(Towers, ax, ay, bx, by, profile);

    private static ModelBody Body(BoardPlay play, ModelId id) => Board.BodyOf(play.State, play.M(id))!;

    [Fact]
    public void AMoveUpTheLadderIsAcceptedAndPutsTheModelOnThePlatform()
    {
        var play = On("15", "6.5", "30", "30");
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("16", "6.5"), ("17", "6.5"), ("18", "6.5"))));
        Assert.Equal(L("5"), result.Events.OfType<ModelMoved>().Single().Cost);
        Assert.Equal(P("18", "6.5"), play.M(A(1)).Position);
        Assert.Equal(L("2"), Body(play, A(1)).Elevation);
    }

    [Fact]
    public void AMoveStraightOntoTheTowerIsRefusedWithTheLadderReason()
    {
        var play = On("15", "6.5", "30", "30");
        var refusal = play.Refused(new MoveModel(Seat.One, A(1), Path(("18", "6.5"))), ReasonCode.MoveIllegal);
        Assert.Equal(Movement.TowerNotByLadderText, refusal.Text);
    }

    [Fact]
    public void AChargeBetweenHeightsIsRefusedBeforeAnyDie()
    {
        // The ground model sees the model on the platform (U19) and it is in range, but the charge would need the ladder.
        var play = On("14.5", "6.5", "18", "6.5");
        var refusal = play.Refused(new ChargeModel(Seat.One, A(1), B(1)), ReasonCode.ChargeRouteBlocked);
        Assert.Equal(Charging.CrossLevelText, refusal.Text);
        Assert.Equal(TurnSequence.Check(play.State, new ChargeModel(Seat.One, A(1), B(1))), refusal);
    }

    [Fact]
    public void AChargeOnThePlatformStaysOnIt()
    {
        var play = On("17", "5.5", "19", "7.5");
        var moved = play.Do(new ChargeModel(Seat.One, A(1), B(1)), 1).Events.OfType<ChargeMoved>().Single();
        Assert.True(moved.Reached);
        var tower = Towers.Battlefield.Terrain.Single(t => t.Id == "tower-s").Footprint;
        Assert.All(moved.Path, p => Assert.True(tower.CircleInside(p, L("0.5")), $"{p} is off the platform"));
        Assert.Equal(L("2"), Body(play, A(1)).Elevation);
        Assert.True(Measure.IsWithin(Body(play, A(1)), Body(play, B(1)), L("1")));
    }

    [Fact]
    public void AChargeOnTheGroundIsPlannedAroundATower()
    {
        // The tower hides the target from the charger here, so this is the planner alone: the straight line from
        // (14.5, 6.5) to (22, 6.5) crosses the tower, and the route goes round it, on the ground.
        var field = Towers.Battlefield;
        var mover = new ModelBody("A1", P("14.5", "6.5"), L("0.5"));
        var target = new ModelBody("B1", P("22", "6.5"), L("0.5"));
        Assert.True(Charging.CheckRoute(field, mover, target, []).IsLegal);
        var plan = Charging.Plan(field, mover, target, L("12"), []);
        Assert.True(plan.Reached);
        var tower = field.Terrain.Single(t => t.Id == "tower-s").Footprint;
        var from = mover.Centre;
        foreach (var to in plan.Path)
        {
            Assert.False(tower.SweptCircleOverlaps(from, to, L("0.5")), $"{from}-{to} crosses the tower");
            Assert.Equal(Length.Zero, Rules.Core.Geometry.Towers.ElevationAt(field, to));
            from = to;
        }
        Assert.True(Movement.CheckPath(field, mover, plan.Path, L("12"), [new Obstacle(target, Relation.Enemy)], "B1").IsLegal);
    }

    [Fact]
    public void ModelsAtDifferentHeightsCannotFight()
    {
        // Bases touching on the table (the top and the foot of the ladder), but 2" apart in height (U70).
        var play = On("17", "6.5", "16", "6.5");
        Assert.Equal(L("2"), Measure.Gap(Body(play, A(1)), Body(play, B(1))));
        play.Refused(new FightModel(Seat.One, A(1), B(1)), ReasonCode.TargetNotInMelee);
    }

    [Fact]
    public void AModelOnTheTowerCanShootAGroundModel()
    {
        var play = On("18", "6.5", "18", "2", Gunman);
        Assert.Null(TurnSequence.Check(play.State, new Shoot(Seat.One, A(1), B(1))));
    }

    [Fact]
    public void ATowerBlocksSightBetweenGroundModels()
    {
        var field = Towers.Battlefield;
        var west = new ModelBody("A1", P("14.5", "6.5"), L("0.5"));
        var east = new ModelBody("B1", P("21.5", "6.5"), L("0.5"));
        Assert.Equal(Visibility.None, Cover.Of(field, west, east).Sight.Visibility);
        // A model on the platform and one on the ground see each other over its edge (U19).
        var top = new ModelBody("A2", P("18", "6.5"), L("0.5"), L("2"));
        Assert.NotEqual(Visibility.None, Cover.Of(field, top, east).Sight.Visibility);
    }

    [Fact]
    public void AnEngagedModelCanRetreatUpTheLadder()
    {
        var play = On("16", "6.5", "15", "6.5");
        Assert.True(Board.IsEngaged(play.State, play.M(A(1))));
        Assert.Null(TurnSequence.Check(play.State, new RetreatModel(Seat.One, A(1), Path(("17", "6.5"), ("18", "6.5")))));
        // Climbing only the step onto the platform is a retreat of 3", ending 2" above the enemy.
        Assert.Equal(L("3"), Board.RetreatCost(play.State, play.M(A(1)), Path(("17", "6.5"))));
    }

    [Fact]
    public void AModelOnATowerDoesNotControlAGroundObjective()
    {
        // A tower 0.5" from obj-1's east side (x 14): a model on it is within 1" of the piece on the table, 2" above it.
        var tower = new TerrainBlock("tower-x", F("14.5", "5", "17.5", "8"), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L("2"), [new Ladder("tower-x-east", P("17.5", "6.5"))]),
        };
        var high = SkirmishWith(WithTerrain(tower), "15", "6.5", "30", "30");
        Assert.Equal(L("2"), Board.BodyOf(high.State, high.M(A(1)))!.Elevation);
        Assert.Null(Objectives.ControllerNow(high.State, "obj-1"));
        // The same spot on the plain table is within 1" and controls it.
        var flat = SkirmishWith(WithTerrain(), "15", "6.5", "30", "30");
        Assert.Equal(Seat.One, Objectives.ControllerNow(flat.State, "obj-1"));
    }
}
