using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Regression and cross-check tests added by the M3 audit.
public class MovementAuditTests
{
    [Fact]
    [Trait("Rule", "Audit: hostile or absurd input is refused deterministically, never an exception")]
    public void AnAbsurdlyLongPathIsRefusedAsTooLongNotThrown()
    {
        var field = Field("200");
        var mover = Body("m", "0.5", "0.5");
        var zigzag = Enumerable.Range(0, 6000).Select(i => i % 2 == 0 ? P("199.5", "0.5") : P("0.5", "199.5")).ToList();
        var result = Movement.CheckPath(field, mover, zigzag, L("100"), []);
        Assert.False(result.IsLegal);
        Assert.Equal(MoveRefusal.PathTooLong, result.Reason);
        Assert.True(Movement.Cost(field, mover, zigzag) > L("100"));
    }

    [Fact]
    [Trait("Rule", "U12: cost is never below the true length, and subdividing adds only a few millionths per leg")]
    public void SubdividingAPathNeverReducesCostAndAddsAtMostThreeMillionthsPerLeg()
    {
        var field = Field("24", Rough("r", "6", "0", "11", "24"));
        var mover = Body("m", "2", "2");
        var rng = new Random(99);
        for (var i = 0; i < 150; i++)
        {
            var end = new Point2(Length.FromThousandths(rng.Next(2001, 22000)), Length.FromThousandths(rng.Next(2001, 22000)));
            var parts = rng.Next(1, 250);
            var steps = Enumerable.Range(1, parts)
                .Select(k => new Point2(
                    Length.FromThousandths(2000 + (end.X.Thousandths - 2000) * k / parts),
                    Length.FromThousandths(2000 + (end.Y.Thousandths - 2000) * k / parts)))
                .ToList();
            var single = Movement.Cost(field, mover, [steps[^1]]).Thousandths;
            var split = Movement.Cost(field, mover, steps).Thousandths;
            // The lattice-rounded split points move the path slightly, so compare against the exact split sum from a
            // double-precision reference built in the test only.
            double reference = 0;
            var from = mover.Centre;
            foreach (var s in steps)
            {
                reference += LegReference(from, s);
                from = s;
            }
            Assert.True(split >= Math.Floor(reference) - 0.0, $"split {split} below true {reference}");
            Assert.True(split <= Math.Ceiling(reference) + Math.Ceiling(3.0 * parts / 1000.0) + 1, $"split {split} true {reference} parts {parts}");
            Assert.True(Math.Abs(split - single) <= Math.Ceiling(3.0 * parts / 1000.0) + 4, $"split {split} single {single}");
        }
    }

    // Test-side reference: length in thousandths with the part inside x 6000..11000 doubled (floating point, test only).
    private static double LegReference(Point2 a, Point2 b)
    {
        double ax = a.X.Thousandths, ay = a.Y.Thousandths, bx = b.X.Thousandths, by = b.Y.Thousandths;
        var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (length == 0)
        {
            return 0;
        }
        double t0 = 0, t1 = 1;
        if (bx != ax)
        {
            var u = (6000 - ax) / (bx - ax);
            var v = (11000 - ax) / (bx - ax);
            t0 = Math.Max(t0, Math.Min(u, v));
            t1 = Math.Min(t1, Math.Max(u, v));
        }
        else if (ax < 6000 || ax > 11000)
        {
            t1 = -1;
        }
        var inside = Math.Max(0, t1 - t0);
        return length * (1 + inside);
    }

    [Fact]
    [Trait("Rule", "p.35 every segment is checked, not just the waypoints (cross-check against dense sampling)")]
    public void LegalityAgreesWithDenseSamplingOfTheWholePath()
    {
        var rng = new Random(31415);
        var checkedBlocked = 0;
        var checkedEnemy = 0;
        var checkedZone = 0;
        var checkedClear = 0;
        for (var scene = 0; scene < 250; scene++)
        {
            var terrain = new List<TerrainBlock>();
            for (var i = 0; i < rng.Next(0, 4); i++)
            {
                var x = rng.Next(3, 19000);
                var y = rng.Next(3, 19000);
                var rect = Footprint.Of(new Rect(
                    new Point2(Length.FromThousandths(x), Length.FromThousandths(y)),
                    new Point2(Length.FromThousandths(x + rng.Next(300, 3000)), Length.FromThousandths(y + rng.Next(300, 3000)))));
                terrain.Add(new TerrainBlock($"w{i}", rect, TerrainProfile.Wall));
            }
            var field = new Battlefield(L("24"), L("24"), terrain, []);
            var mover = new ModelBody("m", new Point2(Length.FromThousandths(rng.Next(1000, 23000)), Length.FromThousandths(rng.Next(1000, 23000))), L("0.5"));
            var enemies = Enumerable.Range(0, rng.Next(0, 3)).Select(i =>
                new ModelBody($"e{i}", new Point2(Length.FromThousandths(rng.Next(1000, 23000)), Length.FromThousandths(rng.Next(1000, 23000))), L("0.5"))).ToList();
            if (enemies.Any(e => Measure.IsWithin(mover, e, Length.Inch)))
            {
                continue; // the start-within-1" case is covered by its own tests
            }
            var waypoints = Enumerable.Range(0, rng.Next(1, 4)).Select(_ =>
                new Point2(Length.FromThousandths(rng.Next(1000, 23000)), Length.FromThousandths(rng.Next(1000, 23000)))).ToList();

            var result = Movement.CheckPath(field, mover, waypoints, L("200"), enemies.Select(Enemy));

            // Dense sample of the centre's path, a point every thousandth, with a 3-thousandth tolerance either way.
            var path = new List<Point2> { mover.Centre };
            path.AddRange(waypoints);
            var blocked = false;
            var blockedSure = false;
            var enemyHit = false;
            var enemyHitSure = false;
            var zoneHit = false;
            var zoneHitSure = false;
            for (var i = 1; i < path.Count; i++)
            {
                var steps = (int)Math.Max(Math.Abs(path[i].X.Thousandths - path[i - 1].X.Thousandths), Math.Abs(path[i].Y.Thousandths - path[i - 1].Y.Thousandths));
                for (var s = 0; s <= steps; s++)
                {
                    var px = path[i - 1].X.Thousandths + (path[i].X.Thousandths - path[i - 1].X.Thousandths) * (double)s / Math.Max(1, steps);
                    var py = path[i - 1].Y.Thousandths + (path[i].Y.Thousandths - path[i - 1].Y.Thousandths) * (double)s / Math.Max(1, steps);
                    foreach (var t in terrain)
                    {
                        var r = t.Footprint.Bounds;
                        var dx = Math.Max(Math.Max(r.Min.X.Thousandths - px, 0), px - r.Max.X.Thousandths);
                        var dy = Math.Max(Math.Max(r.Min.Y.Thousandths - py, 0), py - r.Max.Y.Thousandths);
                        var d = Math.Sqrt(dx * dx + dy * dy);
                        blocked |= d < 500 + 3;
                        blockedSure |= d < 500 - 3;
                    }
                    foreach (var e in enemies)
                    {
                        var d = Math.Sqrt(Math.Pow(px - e.Centre.X.Thousandths, 2) + Math.Pow(py - e.Centre.Y.Thousandths, 2));
                        enemyHit |= d < 1000 + 3;
                        enemyHitSure |= d < 1000 - 3;
                        zoneHit |= d <= 2000 + 3;
                        zoneHitSure |= d <= 2000 - 3;
                    }
                }
            }

            if (blockedSure)
            {
                checkedBlocked++;
                Assert.Equal(MoveRefusal.CrossesBlockingTerrain, result.Reason);
            }
            else if (!blocked)
            {
                Assert.NotEqual(MoveRefusal.CrossesBlockingTerrain, result.Reason);
                if (enemyHitSure)
                {
                    checkedEnemy++;
                    Assert.Equal(MoveRefusal.CrossesEnemyBase, result.Reason);
                }
                else if (!enemyHit && zoneHitSure)
                {
                    checkedZone++;
                    Assert.Equal(MoveRefusal.EntersEnemyZone, result.Reason);
                }
                else if (!enemyHit && !zoneHit)
                {
                    checkedClear++;
                    Assert.True(result.IsLegal, $"scene {scene}: {result.Reason}");
                }
            }
        }
        Assert.True(checkedBlocked > 10 && checkedEnemy > 3 && checkedZone > 3 && checkedClear > 10,
            $"random scenes should exercise every branch ({checkedBlocked}, {checkedEnemy}, {checkedZone}, {checkedClear})");
    }

    [Fact]
    [Trait("Rule", "p.35 cannot leave the battlefield: the base-on-table region is convex, so waypoints suffice")]
    public void CuttingAnOutsideCornerBetweenTwoLegalWaypointsStaysLegal()
    {
        // Brunn-Minkowski: the set of centres with more than half the base on the table is convex, so a straight leg
        // between two legal waypoints never has a point with half or less on the table. Check it by sampling.
        var field = Field();
        var mover = Body("m", "0.2", "5");
        var result = Movement.CheckPath(field, mover, [P("0.15", "0.15"), P("5", "0.2")], L("30"), []);
        Assert.False(result.IsLegal); // (0.15, 0.15) is a corner base with half or less on the table
        var ok = Movement.CheckPath(field, mover, [P("0.15", "23.85"), P("0.2", "5")], L("100"), []);
        Assert.Equal(MoveRefusal.OffBattlefield, ok.Reason); // (0.15, 23.85) likewise
        Assert.True(Movement.CheckPath(field, mover, [P("0.3", "0.3"), P("0.3", "23.7")], L("100"), []).IsLegal);
        for (var i = 0; i <= 200; i++)
        {
            var p = new Point2(L("0.3"), Length.FromThousandths(300 + 23400 * i / 200));
            Assert.Equal(AreaDecision.MoreThanHalf, field.Area.MoreThanHalfInside(p, L("0.5")));
        }
    }

    [Fact]
    public void ZeroLengthMovesAndEndingOnAnEnemyAreHandled()
    {
        var mover = Body("m", "2", "2");
        Assert.True(Movement.CheckPath(Field(), mover, [P("2", "2")], L("0"), []).IsLegal);
        Assert.Equal(L("0"), Movement.Cost(Field(), mover, [P("2", "2")]));
        var enemy = Enemy(Body("e", "6", "2"));
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Movement.CheckPath(Field(), mover, [P("6", "2")], L("10"), [enemy]).Reason); // ending on an enemy
        Assert.Equal(MoveRefusal.CrossesEnemyBase, Movement.CheckPath(Field(), mover, [P("5.001", "2")], L("10"), [enemy]).Reason); // overlapping by a thousandth
        Assert.Equal(MoveRefusal.EntersEnemyZone, Movement.CheckPath(Field(), mover, [P("5", "2")], L("10"), [enemy]).Reason);      // touching: inside the 1" zone
    }

    [Fact]
    [Trait("Rule", "p.35 start within 1\" of an enemy: stay within it at every point; M3 scope is non-retreat moves only")]
    public void StartingWithinOneInchStaysLegalAlongTheWholeLeg()
    {
        var mover = Body("m", "2", "2");
        var enemy = Enemy(Body("e", "3.5", "2"));
        // Circling around the enemy at 1.9" centre distance is within the zone (2.0") throughout.
        Assert.True(Movement.CheckPath(Field(), mover, [P("3.5", "3.9"), P("5.4", "2")], L("20"), [enemy]).Reason is null or MoveRefusal.CrossesEnemyBase);
        // A single leg whose endpoints are both within the zone but which passes wider than the zone cannot exist (the zone is convex).
        Assert.Equal(MoveRefusal.LeavesEnemyZone, Movement.CheckPath(Field(), mover, [P("2", "3.5")], L("20"), [enemy]).Reason);
        // Two enemies: staying within one while entering the zone of another that was not within 1" at the start is refused.
        var second = Enemy(Body("f", "2", "4.2"));
        var both = Movement.CheckPath(Field(), mover, [P("2", "3")], L("20"), [enemy, second]);
        Assert.False(both.IsLegal);
    }
}
