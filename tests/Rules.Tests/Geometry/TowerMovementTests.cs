using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Towers proposal, stage T2: moving on, off and around a tower (U66 to U68, U70, U71). The layout's south tower stands at
// x 16.5-19.5", y 5-8", 2" high, with ladders at (16.5, 6.5) and (19.5, 6.5). For a 1" base the west ladder's foot is
// (16, 6.5) and its top (17, 6.5); the east ladder's top is (19, 6.5) and its foot (20, 6.5).
public class TowerMovementTests
{
    private static readonly Battlefield Field = ClaimNoMansLand.CreateWithTowers().Battlefield;
    private static readonly Length Six = L("6");

    // A base where it stands, at the elevation the battlefield gives that point.
    private static ModelBody At(string id, string x, string y) => new(id, P(x, y), L("0.5"), Towers.ElevationAt(Field, P(x, y)));

    private static MoveResult Check(ModelBody mover, Length allowance, IReadOnlyList<Obstacle>? others, params (string X, string Y)[] waypoints) =>
        Movement.CheckPath(Field, mover, waypoints.Select(w => P(w.X, w.Y)).ToList(), allowance, others ?? []);

    private static MoveResult Check(ModelBody mover, params (string X, string Y)[] waypoints) => Check(mover, Six, null, waypoints);

    private static Obstacle Foe(ModelBody body) => new(body, Relation.Enemy);

    [Fact]
    public void ClimbingALadderCostsTheStepOverTheEdgePlusTheHeight()
    {
        // 1" to the foot, 1" step + 2" climb, 1" across the platform.
        var up = Check(At("A1", "15", "6.5"), ("16", "6.5"), ("17", "6.5"), ("18", "6.5"));
        Assert.True(up.IsLegal, up.Text);
        Assert.Equal(L("5"), up.Cost);
        // Down the other side costs the same climb.
        var down = Check(At("A1", "18", "6.5"), ("19", "6.5"), ("20", "6.5"), ("21", "6.5"));
        Assert.True(down.IsLegal, down.Text);
        Assert.Equal(L("5"), down.Cost);
    }

    [Fact]
    public void TheClimbCountsAgainstTheMovementAllowance()
    {
        var mover = At("A1", "14.5", "6.5");
        Assert.True(Check(mover, L("6"), null, ("16", "6.5"), ("17", "6.5"), ("18.5", "6.5")).IsLegal);
        Assert.Equal(MoveRefusal.PathTooLong, Check(mover, L("5.999"), null, ("16", "6.5"), ("17", "6.5"), ("18.5", "6.5")).Reason);
    }

    [Fact]
    public void FromTheGroundATowerIsSolid()
    {
        var across = Check(At("A1", "14.5", "6.5"), L("10"), null, ("21.5", "6.5"));
        Assert.Equal(MoveRefusal.TowerNotByLadder, across.Reason);
        Assert.Equal("tower-s", across.SubjectId);
        Assert.Equal(Movement.TowerNotByLadderText, across.Text);
        // A base may brush the tower's side: touching is not overlapping.
        Assert.True(Check(At("A1", "16", "4"), ("16", "9")).IsLegal);
        Assert.Equal(MoveRefusal.TowerNotByLadder, Check(At("A1", "16.001", "4"), ("16.001", "9")).Reason);
    }

    [Theory]
    [InlineData("15.9", "6.5", "17", "6.5")]  // not quite at its foot
    [InlineData("16", "6.5", "17.5", "6.5")]  // past its top
    [InlineData("16", "6.5", "17", "7")]      // slanting off it
    [InlineData("16.5", "4.5", "17", "5.5")]  // over the south side, where there is no ladder
    public void OnlyTheLadderLegChangesLevel(string x1, string y1, string x2, string y2)
    {
        var result = Check(At("A1", x1, y1), (x2, y2));
        Assert.Equal(MoveRefusal.TowerNotByLadder, result.Reason);
        Assert.Equal("tower-s", result.SubjectId);
    }

    [Fact]
    public void BesideTheLadderIsASideClimbWhichOnlyAMoveOrDashMayMake()
    {
        // Stage T6 (U75, U76): straight up the side beside the ladder. CheckPath without sideClimb (a Retreat, a Charge's
        // planner) refuses it with its own reason; with it (a Move or Dash) it is legal and says where the climb is.
        var mover = At("A1", "16", "6.4");
        var refused = Check(mover, ("17", "6.4"));
        Assert.Equal(MoveRefusal.SideClimbNotAllowed, refused.Reason);
        Assert.Equal(Movement.SideClimbOnlyMoveText, refused.Text);
        var climbed = Movement.CheckPath(Field, mover, [P("17", "6.4")], L("6"), [], sideClimb: true);
        Assert.True(climbed.IsLegal);
        Assert.Equal(0, climbed.SideClimb);
        Assert.Equal(L("3"), climbed.Cost);
        // A ladder leg is not a side climb.
        Assert.Null(Movement.CheckPath(Field, At("A1", "16", "6.5"), [P("17", "6.5")], L("6"), [], sideClimb: true).SideClimb);
    }

    [Fact]
    public void OnThePlatformTheBaseStaysWhollyOnIt()
    {
        var mover = At("A1", "18", "6.5");
        Assert.Equal(L("2"), mover.Elevation);
        Assert.True(Check(mover, ("18", "7.5"), ("17", "5.5")).IsLegal); // touching the edges from inside
        var off = Check(mover, ("18", "7.501"));
        Assert.Equal(MoveRefusal.OffPlatformEdge, off.Reason);
        Assert.Equal(Movement.OffPlatformEdgeText, off.Text);
        Assert.Equal(MoveRefusal.OffPlatformEdge, Check(mover, ("18", "6.5"), ("19.001", "6.5")).Reason);
    }

    [Fact]
    public void ThereAreNoWalkwaysBetweenTowers()
    {
        // Two towers 1" apart: a step from one platform straight onto the other is refused.
        var pair = Geo.Field("24", TowerAt("a", "2"), TowerAt("b", "6"));
        var mover = new ModelBody("A1", P("4.5", "11.5"), L("0.5"), L("2"));
        var result = Movement.CheckPath(pair, mover, [P("6.5", "11.5")], Six, []);
        Assert.Equal(MoveRefusal.TowerNotByLadder, result.Reason);
        Assert.Equal("b", result.SubjectId);
    }

    private static TerrainBlock TowerAt(string id, string x, string height = "2") =>
        new(id, F(x, "10", (decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture) + 3).ToString(System.Globalization.CultureInfo.InvariantCulture), "13"), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L(height), [new Ladder($"{id}-w", P(x, "11"))]),
        };

    [Fact]
    public void AModelOnTheTowerIsNotInTheWayOfAGroundMoveBelowIt()
    {
        // The enemy stands on the platform 0.5" from the south edge; the ground path runs along the edge, 1" from its
        // centre on the table. On the ground that would cross its base; 2" up, it is out of reach (U70).
        var above = Foe(At("B1", "18", "5.5"));
        Assert.Equal(L("2"), above.Body.Elevation);
        Assert.True(Check(At("A1", "14.5", "4.5"), Six, [above], ("20.5", "4.5")).IsLegal);
        // The same enemy on the ground (a plain battlefield) would be in the way.
        var flat = ClaimNoMansLand.Create().Battlefield;
        var ground = new Obstacle(new ModelBody("B1", P("18", "5.5"), L("0.5")), Relation.Enemy);
        Assert.Equal(MoveRefusal.EntersEnemyZone, Movement.CheckPath(flat, At("A1", "14.5", "4.5"), [P("20.5", "4.5")], Six, [ground]).Reason);
    }

    [Fact]
    public void ClimbingUpNextToAnEnemyOnThePlatformEntersItsInch()
    {
        var result = Check(At("A1", "15", "6.5"), Six, [Foe(At("B1", "18", "6.5"))], ("16", "6.5"), ("17", "6.5"));
        Assert.Equal(MoveRefusal.EntersEnemyZone, result.Reason);
        // Exactly 1" from the top is still within 1"; a little further lets the model up.
        Assert.Equal(MoveRefusal.EntersEnemyZone, Check(At("A1", "15", "6.5"), Six, [Foe(At("B1", "19", "6.5"))], ("16", "6.5"), ("17", "6.5")).Reason);
        Assert.True(Check(At("A1", "15", "6.5"), Six, [Foe(At("B1", "19", "7.5"))], ("16", "6.5"), ("17", "6.5")).IsLegal);
    }

    [Fact]
    public void AnEngagedModelCannotLeaveByTheLadderExceptByRetreating()
    {
        var mover = At("A1", "16", "6.5");
        var foe = Foe(At("B1", "15", "6.5")); // bases touching, on the ground at the foot
        var move = Movement.CheckPath(Field, mover, [P("17", "6.5")], Six, [foe]);
        Assert.Equal(MoveRefusal.LeavesEnemyZone, move.Reason);
        var retreat = Movement.CheckPath(Field, mover, [P("17", "6.5"), P("18", "6.5")], Six, [foe], retreat: true);
        Assert.True(retreat.IsLegal, retreat.Text);
        Assert.Equal(L("4"), retreat.Cost);
    }

    [Fact]
    public void AFriendOnThePlatformBlocksTheTop()
    {
        var friendAbove = new Obstacle(At("A2", "17.5", "6.5"), Relation.Friendly);
        Assert.Equal(MoveRefusal.EndsOnBase, Check(At("A1", "16", "6.5"), Six, [friendAbove], ("17", "6.5")).Reason);
    }

    [Fact]
    public void AFlatBattlefieldIsUnchanged()
    {
        // The same path on the plain layout and on the tower layout, away from the towers: same verdict, same cost.
        var plain = ClaimNoMansLand.Create().Battlefield;
        var path = new[] { P("2", "2"), P("8", "15"), P("3", "18") };
        var mover = new ModelBody("A1", P("1", "1"), L("0.5"));
        var a = Movement.CheckPath(plain, mover, path, L("30"), []);
        var b = Movement.CheckPath(Field, mover, path, L("30"), []);
        Assert.Equal(a, b);
        Assert.True(a.IsLegal);
    }

    [Fact]
    public void ModelsArePlacedOnTheGroundNotOnATower()
    {
        var on = Placement.Check(Field, At("A1", "18", "6.5"), []);
        Assert.False(on.IsLegal);
        Assert.Equal(PlacementRefusal.InBlockingTerrain, on.Reason);
        Assert.Equal("tower-s", on.SubjectId);
        Assert.True(Placement.Check(Field, At("A1", "16", "6.5"), []).IsLegal);
    }

    [Fact]
    public void TowersShareOneHeightAndNothingStandsOnThem()
    {
        Assert.Throws<ArgumentException>(() => Geo.Field("24", TowerAt("a", "2", "2"), TowerAt("b", "10", "2.5")));
        Assert.NotNull(Geo.Field("24", TowerAt("a", "2", "2"), TowerAt("b", "10", "2")));
        Assert.Throws<ArgumentException>(() => Geo.Field("24", TowerAt("a", "2", "2"), Rough("r", "4", "4", "6", "11")));
    }
}
