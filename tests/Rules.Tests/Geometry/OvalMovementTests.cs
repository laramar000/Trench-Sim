using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Roster stage R8b (U132): moving an oval base. Its core segment is carried along each leg without turning; the base is
// every point within its radius of that.
public class OvalMovementTests
{
    private static ModelBody Oval(string id, string x, string y, BaseAxis axis = BaseAxis.Y) =>
        new(id, P(x, y), L("0.5"), default, L("0.5"), axis);

    private static MoveResult Move(Battlefield field, ModelBody mover, IEnumerable<Obstacle>? others, params string[][] waypoints) =>
        Movement.CheckPath(field, mover, waypoints.Select(w => P(w[0], w[1])).ToList(), L("20"), others ?? []);

    private static string[] W(string x, string y) => [x, y];

    [Fact]
    public void AnEnemyBesideTheLongSideIsCloserThanBesideTheShortSide()
    {
        var field = Field("24");
        // Core of the Y oval reaches 0.5" up from the leg; the X oval's core lies on it.
        Assert.Equal(MoveRefusal.EntersEnemyZone, Move(field, Oval("m", "5", "5"), [Enemy(Body("e", "10", "7.4"))], W("15", "5")).Reason);
        Assert.True(Move(field, Oval("m", "5", "5"), [Enemy(Body("e", "10", "7.6"))], W("15", "5")).IsLegal);
        Assert.True(Move(field, Oval("m", "5", "5", BaseAxis.X), [Enemy(Body("e", "10", "7.4"))], W("15", "5")).IsLegal);
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Move(field, Oval("m", "5", "5"), [Enemy(Body("e", "10", "6.4"))], W("15", "5")).Reason);
        Assert.Equal(MoveRefusal.EntersEnemyZone, Move(field, Oval("m", "5", "5", BaseAxis.X), [Enemy(Body("e", "10", "6.4"))], W("15", "5")).Reason);
    }

    [Fact]
    public void ARoundMoverPassesAnOvalEnemyByItsCore()
    {
        var field = Field("24");
        var round = Body("m", "5", "5");
        Assert.True(Move(field, round, [Enemy(Oval("e", "10", "7.6"))], W("15", "5")).IsLegal);
        Assert.Equal(MoveRefusal.EntersEnemyZone, Move(field, round, [Enemy(Oval("e", "10", "7.4"))], W("15", "5")).Reason);
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Move(field, round, [Enemy(Oval("e", "10", "6.4"))], W("15", "5")).Reason);
        Assert.Equal(MoveRefusal.EntersEnemyZone, Move(field, round, [Enemy(Oval("e", "10", "6.4", BaseAxis.X))], W("15", "5")).Reason);
        // An enemy oval lying along the leg: its end is what the mover meets.
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Move(field, round, [Enemy(Oval("e", "10", "5", BaseAxis.X))], W("15", "5")).Reason);
    }

    [Fact]
    public void TerrainAndFriendsAreMetByTheWholeBase()
    {
        var field = Field("24", Wall("w", "10", "5.8", "11", "7"));
        Assert.Equal(MoveRefusal.CrossesBlockingTerrain, Move(field, Oval("m", "5", "5"), null, W("15", "5")).CrossesLeg());
        Assert.True(Move(field, Oval("m", "5", "5", BaseAxis.X), null, W("15", "5")).IsLegal);
        Assert.True(Move(Field("24"), Oval("m", "5", "5"), [new Obstacle(Body("f", "10", "6.9"), Relation.Friendly)], W("10", "5")).IsLegal);
        Assert.Equal(MoveRefusal.EndsOnBase, Move(Field("24"), Oval("m", "5", "5"), [new Obstacle(Body("f", "10", "6.4"), Relation.Friendly)], W("10", "5")).Reason);
        // A friend may be passed over.
        Assert.True(Move(Field("24"), Oval("m", "5", "5"), [new Obstacle(Body("f", "10", "5"), Relation.Friendly)], W("15", "5")).IsLegal);
    }

    [Fact]
    public void StartingNextToAnEnemyTheOvalMustStayWithinOneInch()
    {
        var field = Field("24");
        var enemy = Enemy(Body("e", "5", "7"));
        // The Y oval's core top is (5, 5.5): the enemy's edge is 1" away. Sliding sideways leaves the zone, and a short slide keeps it.
        Assert.Equal(MoveRefusal.LeavesEnemyZone, Move(field, Oval("m", "5", "5"), [enemy], W("9", "5")).Reason);
        Assert.True(Move(field, Oval("m", "5", "5"), [enemy], W("5.5", "5")).IsLegal);
        Assert.True(Move(field, Oval("m", "5", "5"), [enemy], W("5", "4.6")).IsLegal);
        Assert.Equal(MoveRefusal.LeavesEnemyZone, Move(field, Oval("m", "5", "5"), [enemy], W("5", "4.4")).Reason);
    }

    [Fact]
    public void MoreThanHalfOfTheOvalMustStayOnTheTable()
    {
        var field = Field("24");
        Assert.True(Move(field, Oval("m", "10", "20"), null, W("10", "23.9")).IsLegal);
        Assert.Equal(MoveRefusal.OffBattlefield, Move(field, Oval("m", "10", "20"), null, W("10", "24.1")).Reason);
        Assert.True(Move(field, Oval("m", "10", "20", BaseAxis.X), null, W("10", "23.9")).IsLegal);
    }

    [Fact]
    public void AnOvalDoesNotClimbTowers()
    {
        var tower = new TerrainBlock("t", F("10", "10", "13", "13"), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L("2"), [new Ladder("t-w", P("10", "11"))]),
        };
        var field = Field("24", tower);
        Assert.Equal(MoveRefusal.TowerNotByLadder, Move(field, Oval("m", "5", "11.5"), null, W("12", "11.5")).Reason);
        Assert.Equal(MoveRefusal.TowerNotByLadder, Move(field, Oval("m", "5", "11.5"), null, W("9.6", "11.5")).Reason);
        Assert.True(Move(field, Oval("m", "5", "11.5"), null, W("9.4", "11.5")).IsLegal);
        // A round base can still use the ladder.
        Assert.True(Move(field, Body("m", "5", "11"), null, W("9.5", "11")).IsLegal);
    }

    // The exact swept tests against a dense sample of positions along the leg, over random scenes: a hit the samples
    // cannot find (or a miss they find) by a clear margin is a bug.
    [Fact]
    public void SweptTestsAgreeWithDenseSampling()
    {
        var rng = new Random(11);
        const int steps = 3000;
        for (var scene = 0; scene < 400; scene++)
        {
            Length Pick(int lo, int hi) => Length.FromThousandths(rng.Next(lo, hi));
            var axis = rng.Next(2) == 0 ? BaseAxis.X : BaseAxis.Y;
            var span = rng.Next(3) == 0 ? Length.Zero : Pick(200, 900);
            var mover = new ModelBody("m", new Point2(Pick(1000, 9000), Pick(1000, 9000)), Pick(200, 700), default, span, axis);
            var end = new Point2(Pick(1000, 9000), Pick(1000, 9000));
            var other = new ModelBody("o", new Point2(Pick(1000, 9000), Pick(1000, 9000)), Pick(200, 700), default, rng.Next(2) == 0 ? Length.Zero : Pick(200, 900), rng.Next(2) == 0 ? BaseAxis.X : BaseAxis.Y);
            var reach = mover.Radius + other.Radius + (rng.Next(2) == 0 ? Length.Zero : Pick(0, 1500));

            double best = double.MaxValue;
            for (var i = 0; i <= steps; i++)
            {
                var at = new Point2(
                    Length.FromThousandths(mover.Centre.X.Thousandths + (end.X.Thousandths - mover.Centre.X.Thousandths) * i / steps),
                    Length.FromThousandths(mover.Centre.Y.Thousandths + (end.Y.Thousandths - mover.Centre.Y.Thousandths) * i / steps));
                best = Math.Min(best, Math.Sqrt((double)Measure.CoreGapSquared(mover.At(at), other)));
            }
            var exact = Shapes.SweptNear(mover, mover.Centre, end, other, reach, strict: false);
            var r = (double)reach.Thousandths;
            if (exact)
            {
                Assert.True(best <= r * 1.02 + 5, $"scene {scene}: exact says near, samples never get closer than {best} (reach {r})");
            }
            else
            {
                Assert.True(best >= r * 0.98 - 5, $"scene {scene}: exact says clear, samples reach {best} (reach {r})");
            }

            // The same leg against a wall.
            var rect = new Rect(new Point2(Pick(1000, 5000), Pick(1000, 5000)), new Point2(Pick(5100, 9000), Pick(5100, 9000)));
            double deepest = double.MaxValue;
            for (var i = 0; i <= steps; i++)
            {
                var at = new Point2(
                    Length.FromThousandths(mover.Centre.X.Thousandths + (end.X.Thousandths - mover.Centre.X.Thousandths) * i / steps),
                    Length.FromThousandths(mover.Centre.Y.Thousandths + (end.Y.Thousandths - mover.Centre.Y.Thousandths) * i / steps));
                deepest = Math.Min(deepest, Math.Sqrt((double)Shapes.CoreGapSquaredToRect(mover.At(at), rect)));
            }
            var hits = Shapes.SweptBodyOverlapsRect(mover, mover.Centre, end, rect);
            var radius = (double)mover.Radius.Thousandths;
            if (hits)
            {
                Assert.True(deepest <= radius * 1.02 + 5, $"scene {scene}: exact says it overlaps the wall, samples stay {deepest} away (radius {radius})");
            }
            else
            {
                Assert.True(deepest >= radius * 0.98 - 5, $"scene {scene}: exact says clear of the wall, samples come to {deepest} (radius {radius})");
            }
        }
    }
}

internal static class MoveResultExtensions
{
    // The refusal reason, for a readable assert.
    public static MoveRefusal? CrossesLeg(this MoveResult result) => result.Reason;
}
