using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// The two additions to Movement.CheckPath for Slice B (a charge target and a retreat flag) and the straight-line
// charge planner. Comprehensive Rules pp. 35-37. Without the new arguments CheckPath behaves exactly as in M3.
public class ChargeAndRetreatGeometryTests
{
    private static readonly Battlefield Open = Field("36");

    [Fact]
    [Trait("Rule", "Moves p. 35: without a charge or retreat the 1\" rules are unchanged")]
    public void OrdinaryMovesStillMayNotEnterOrLeaveAnEnemyZone()
    {
        var mover = Body("m", "4", "2");
        var enemy = Body("e", "9", "2");
        Assert.Equal(MoveRefusal.EntersEnemyZone, Movement.CheckPath(Open, mover, [P("7.6", "2")], L("6"), [Enemy(enemy)]).Reason);

        var engaged = Body("m", "7.6", "2");
        Assert.Equal(MoveRefusal.LeavesEnemyZone, Movement.CheckPath(Open, engaged, [P("4", "2")], L("6"), [Enemy(enemy)]).Reason);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: a charge may enter the target's 1\", and only the target's")]
    public void ChargeTargetZoneIsExemptButOtherEnemiesAreNot()
    {
        var mover = Body("m", "4", "2");
        var target = Body("t", "9", "2");
        var other = Body("o", "6", "3.5");
        var path = new[] { P("7.6", "2") };
        Assert.True(Movement.CheckPath(Open, mover, path, L("6"), [Enemy(target)], "t").IsLegal);
        Assert.Equal(MoveRefusal.EntersEnemyZone, Movement.CheckPath(Open, mover, path, L("6"), [Enemy(target), Enemy(other)], "t").Reason);
        // Naming a charge target does not allow running through its base.
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Movement.CheckPath(Open, mover, [P("9", "2")], L("6"), [Enemy(target)], "t").Reason);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: once within 1\" of the target other enemies no longer interpose")]
    public void TheTailOfAChargeIgnoresOtherEnemiesZones()
    {
        var mover = Body("m", "4", "2");
        var target = Body("t", "9", "2");
        var other = Body("o", "8.7", "3.6");
        // The first leg ends exactly 1" from the target (centre at 7: gap 9 - 7 - 1 = 1").
        var approach = P("7", "2");
        var tail = P("7.9", "2");
        Assert.True(Movement.CheckPath(Open, mover, [approach, tail], L("6"), [Enemy(target), Enemy(other)], "t").IsLegal);
        // Going straight to the same end point in one leg is refused: the leg starts outside the target's zone.
        Assert.False(Movement.CheckPath(Open, mover, [tail], L("6"), [Enemy(target), Enemy(other)], "t").IsLegal);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: a retreat may leave the enemies the model started next to")]
    public void RetreatMayLeaveEngagementButNotCrossABase()
    {
        var mover = Body("m", "7.6", "2");
        var enemy = Body("e", "9", "2");
        Assert.True(Movement.CheckPath(Open, mover, [P("4", "2")], L("6"), [Enemy(enemy)], retreat: true).IsLegal);
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Movement.CheckPath(Open, mover, [P("12", "2")], L("6"), [Enemy(enemy)], retreat: true).Reason);
        // It may not run into another enemy's 1".
        var other = Body("o", "4", "3.5");
        Assert.Equal(MoveRefusal.EntersEnemyZone, Movement.CheckPath(Open, mover, [P("4", "2")], L("6"), [Enemy(enemy), Enemy(other)], retreat: true).Reason);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: the move stops at contact, never overlapping the target (U47)")]
    public void ThePlannerStopsAtContactAcrossManyPositions()
    {
        var rng = new Random(12345);
        for (var i = 0; i < 400; i++)
        {
            var radius = L(i % 3 == 0 ? "0.75" : "0.5");
            var mover = new ModelBody("m", new Point2(Length.FromThousandths(rng.Next(2000, 6000)), Length.FromThousandths(rng.Next(2000, 6000))), radius);
            var target = new ModelBody("t", new Point2(Length.FromThousandths(rng.Next(7000, 15000)), Length.FromThousandths(rng.Next(2000, 15000))), L(i % 5 == 0 ? "1.25" : "0.5"));
            var reach = Length.FromThousandths(rng.Next(3000, 12001));
            var obstacles = new[] { Enemy(target) };
            var route = Charging.CheckRoute(Open, mover, target, obstacles);
            Assert.True(route.IsLegal, $"{i}: {route.Text}");

            var plan = Charging.Plan(Open, mover, target, reach, obstacles);
            Assert.True(plan.Path.Length <= 2);
            if (plan.Path.IsEmpty)
            {
                continue;
            }
            Assert.True(plan.Cost <= reach, $"{i}");
            var end = mover.At(plan.Path[^1]);
            Assert.False(Measure.Overlap(end, target), $"{i} overlaps");
            Assert.Equal(Measure.IsWithin(end, target, Length.Inch), plan.Reached);
            Assert.True(Movement.CheckPath(Open, mover, plan.Path, reach, obstacles, "t").IsLegal, $"{i}");
            // Either it touched the target (to a thousandth or two) or it used all its movement.
            Assert.True(Measure.Gap(end, target) <= L("0.003") || plan.Cost >= reach - L("0.003"), $"{i}: gap {Measure.Gap(end, target)} cost {plan.Cost} reach {reach}");
        }
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: shortest route directly towards the target")]
    public void TheChargeRouteIsTheStraightLine()
    {
        var mover = Body("m", "4", "2");
        var target = Body("t", "12", "6");
        var plan = Charging.Plan(Open, mover, target, L("12"), [Enemy(target)]);
        Assert.True(plan.Reached);
        // Every waypoint lies on the line from the mover's centre to the target's centre.
        foreach (var point in plan.Path)
        {
            var cross = (point.X.Thousandths - 4000L) * 4000 - (point.Y.Thousandths - 2000L) * 8000;
            Assert.True(Math.Abs(cross) < 8000 * 2, $"{point} is off the line by {cross}");
        }
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: a wall with no way round within 12\" refuses the charge before any die")]
    public void ATerrainWallWithNoWayRoundRefusesTheDeclaration()
    {
        var wall = Wall("w", "7", "0", "8", "36");   // the whole height of the table
        var field = Field("36", wall);
        var mover = Body("m", "4", "2");
        var target = Body("t", "12", "2");
        var route = Charging.CheckRoute(field, mover, target, [Enemy(target)]);
        Assert.False(route.IsLegal);
        Assert.Equal(MoveRefusal.NoRoute, route.Reason);
    }
}
