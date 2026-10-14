using System.Collections.Immutable;
using Rules.Core.Geometry;
using Xunit.Abstractions;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// An audit of the charge planner's accuracy claims (docs U58): every route it returns is legal, and no more than about
// 0.5% (plus 0.01") longer than the shortest. Three separate checks, so that agreement between two approximate
// searches is never the only evidence:
//   1. Legality, by Euclidean distance (segment to rectangle, segment to point) computed here in floating point with no
//      polygon outlines at all: every leg of the route keeps the base's radius clear of every wall.
//   2. Taut string: no stretch of the route can be replaced by a straight chord that is legal by the same Euclidean
//      test and shorter than the stretch by more than the tolerance. This needs no reference search at all.
//   3. Cost, against ReferenceRouter (a finely sampled brute-force search) in both directions: the planner may not be
//      shorter than it (that would be an illegal shortcut) nor more than the tolerance longer.
// The planner is exact integer arithmetic; floating point appears only in these tests.
public class ChargeRoutingAuditTests(ITestOutputHelper output)
{
    private static readonly Length Far = L("60");   // more than any route in these scenes: Plan returns the whole route

    private static double In(Length length) => length.Thousandths / 1000.0;

    // ---- Euclidean geometry for the checks (floating point, tests only) ----

    private static double DistPointSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 < 1e-18 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        return Math.Sqrt(Math.Pow(ax + t * dx - px, 2) + Math.Pow(ay + t * dy - py, 2));
    }

    private static double DistPointRect(double px, double py, double[] r) =>
        Math.Sqrt(Math.Pow(Math.Max(Math.Max(r[0] - px, 0), px - r[2]), 2) + Math.Pow(Math.Max(Math.Max(r[1] - py, 0), py - r[3]), 2));

    private static bool SegmentTouchesRect(double ax, double ay, double bx, double by, double[] r)
    {
        double t0 = 0, t1 = 1;
        var dx = bx - ax;
        var dy = by - ay;
        foreach (var (p, q) in new[] { (-dx, ax - r[0]), (dx, r[2] - ax), (-dy, ay - r[1]), (dy, r[3] - ay) })
        {
            if (Math.Abs(p) < 1e-15)
            {
                if (q < 0)
                {
                    return false;
                }
                continue;
            }
            var t = q / p;
            if (p < 0)
            {
                t0 = Math.Max(t0, t);
            }
            else
            {
                t1 = Math.Min(t1, t);
            }
            if (t0 > t1)
            {
                return false;
            }
        }
        return true;
    }

    // The distance between a segment and an axis-aligned rectangle [x1, y1, x2, y2].
    private static double DistSegmentRect(double ax, double ay, double bx, double by, double[] r)
    {
        if (SegmentTouchesRect(ax, ay, bx, by, r))
        {
            return 0;
        }
        var best = Math.Min(DistPointRect(ax, ay, r), DistPointRect(bx, by, r));
        foreach (var (cx, cy) in new[] { (r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3]) })
        {
            best = Math.Min(best, DistPointSegment(cx, cy, ax, ay, bx, by));
        }
        return best;
    }

    private static double[] Rect(TerrainBlock wall)
    {
        var b = wall.Footprint.Bounds;
        return [In(b.Min.X), In(b.Min.Y), In(b.Max.X), In(b.Max.Y)];
    }

    // The things a base must keep clear of: walls (a distance), and discs (friends, the target) by the sum of radii.
    private sealed record Scene(IReadOnlyList<double[]> Walls, IReadOnlyList<(double X, double Y, double Reach)> Discs, double Radius)
    {
        public double Clearance(double ax, double ay, double bx, double by)
        {
            var worst = double.PositiveInfinity;
            foreach (var wall in Walls)
            {
                worst = Math.Min(worst, DistSegmentRect(ax, ay, bx, by, wall) - Radius);
            }
            foreach (var (x, y, reach) in Discs)
            {
                worst = Math.Min(worst, DistPointSegment(x, y, ax, ay, bx, by) - reach);
            }
            return worst;
        }
    }

    private static Scene SceneOf(ModelBody mover, ModelBody target, IEnumerable<TerrainBlock> walls, params ModelBody[] blocking) =>
        new([.. walls.Select(Rect)],
            [(In(target.Centre.X), In(target.Centre.Y), In(mover.Radius + target.Radius)),
             .. blocking.Select(b => (In(b.Centre.X), In(b.Centre.Y), In(mover.Radius + b.Radius)))],
            In(mover.Radius));

    private static List<(double X, double Y)> Points(ModelBody mover, ImmutableArray<Point2> path) =>
        [(In(mover.Centre.X), In(mover.Centre.Y)), .. path.Select(p => (In(p.X), In(p.Y)))];

    private static double Travelled(List<(double X, double Y)> pts, int from, int to)
    {
        var total = 0.0;
        for (var i = from; i < to; i++)
        {
            total += Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2));
        }
        return total;
    }

    // Check 1: every leg keeps clear of everything, to a thousandth or two (the lattice).
    private static void AssertLegal(Scene scene, List<(double X, double Y)> pts, string what)
    {
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var clear = scene.Clearance(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
            Assert.True(clear >= -0.0025, $"{what}: leg {i} ({pts[i]} to {pts[i + 1]}) is {clear:0.0000}\" inside an obstacle");
        }
    }

    // Check 2: a chord that is legal with a margin and is shorter than the stretch it replaces by more than the
    // tolerance means the route was not taut.
    private static void AssertTaut(Scene scene, List<(double X, double Y)> pts, string what)
    {
        for (var i = 0; i < pts.Count; i++)
        {
            for (var j = i + 2; j < pts.Count; j++)
            {
                var chord = Math.Sqrt(Math.Pow(pts[j].X - pts[i].X, 2) + Math.Pow(pts[j].Y - pts[i].Y, 2));
                var stretch = Travelled(pts, i, j);
                var slack = stretch - chord;
                if (slack > 0.006 * stretch + 0.012 && scene.Clearance(pts[i].X, pts[i].Y, pts[j].X, pts[j].Y) >= 0.002)
                {
                    Assert.Fail($"{what}: points {i} to {j} could be joined directly, saving {slack:0.0000}\" of {stretch:0.000}\"");
                }
            }
        }
    }

    // Check 3: against the brute-force search. The planner's outline is a 32-gon just outside the circle (0.32% of the
    // arc, plus a few thousandths of margin); the reference here is a 40-gon (0.16%) with an exact radial finish.
    private double AssertMatchesReference(double planner, double reference, string what)
    {
        Assert.True(planner >= reference * 0.995 - 0.01, $"{what}: planner {planner:0.0000} is shorter than the brute-force {reference:0.0000}");
        Assert.True(planner <= reference * 1.006 + 0.012, $"{what}: planner {planner:0.0000} is longer than the brute-force {reference:0.0000}");
        return planner - reference;
    }

    private const int RefQuarter = 10;

    // Beyond the edge of a 36" table a base's centre may not go (a base must be more than half on the table).
    private static ReferenceRouter.Shape[] OffTable(double size = 36) =>
    [
        new([(-100, -100), (size + 100, -100), (size + 100, 0), (-100, 0)]),
        new([(-100, size), (size + 100, size), (size + 100, size + 100), (-100, size + 100)]),
        new([(-100, -100), (0, -100), (0, size + 100), (-100, size + 100)]),
        new([(size, -100), (size + 100, -100), (size + 100, size + 100), (size, size + 100)]),
    ];

    private static ReferenceRouter.Shape Shape(TerrainBlock wall, double radius = 0.5)
    {
        var r = Rect(wall);
        return ReferenceRouter.RoundedRect(r[0], r[1], r[2], r[3], radius, RefQuarter);
    }

    private static (ModelBody Mover, ModelBody Target, ChargePlan Plan, Scene Scene, double Reference) Run(
        IReadOnlyList<TerrainBlock> walls, string mx, string my, string tx, string ty, string size = "36")
    {
        var field = Field(size, walls.ToArray());
        var mover = Body("m", mx, my);
        var target = Body("t", tx, ty);
        var plan = Charging.Plan(field, mover, target, Far, [Enemy(target)]);
        var reference = ReferenceRouter.ShortestToContact(In(mover.Centre.X), In(mover.Centre.Y), [.. walls.Select(w => Shape(w)), .. OffTable()],
            In(target.Centre.X), In(target.Centre.Y), 1.0, ring: 60);
        return (mover, target, plan, SceneOf(mover, target, walls), reference);
    }

    // ---- adversarial fixtures ----

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: the planner's route is legal, taut and within tolerance of the brute-force search")]
    public void NarrowGapsClosedAndOpenGiveLegalTautRoutes()
    {
        // A wall with a doorway g wide, and a route that must bend inside it. g is just above the base diameter (1").
        foreach (var gap in new[] { "1.02", "1.05", "1.2", "1.5", "2.5" })
        {
            var g = double.Parse(gap, System.Globalization.CultureInfo.InvariantCulture);
            var walls = new[]
            {
                Wall("lo", "7", "0", "8", $"{18 - g / 2:0.000}"),
                Wall("hi", "7", $"{18 + g / 2:0.000}", "8", "36"),
            };
            var run = Run(walls, "3", "12", "12", "24");
            Assert.True(run.Plan.Reached, gap);
            var pts = Points(run.Mover, run.Plan.Path);
            AssertLegal(run.Scene, pts, $"gap {gap}");
            AssertTaut(run.Scene, pts, $"gap {gap}");
            AssertMatchesReference(In(run.Plan.Cost), run.Reference, $"gap {gap}");
        }
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: a doorway narrower than the base is never used; one a little wider counts as closed (U58)")]
    public void TheDoorwayThresholdIsSoundAndWithinTheDocumentedTolerance()
    {
        // The smallest doorway the brute force can pass, by bisection, for a diagonal crossing of a 1"-thick wall.
        bool ReferenceOpens(double g)
        {
            var shapes = new[]
            {
                ReferenceRouter.RoundedRect(7, 0, 8, 18 - g / 2, 0.5, 20),
                ReferenceRouter.RoundedRect(7, 18 + g / 2, 8, 36, 0.5, 20),
            };
            return double.IsFinite(ReferenceRouter.ShortestToContact(3, 12, [.. shapes, .. OffTable()], 12, 24, 1.0, ring: 60));
        }
        double lo = 1.0, hi = 6.0;
        for (var i = 0; i < 16; i++)
        {
            var mid = (lo + hi) / 2;
            if (ReferenceOpens(mid))
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }
        var threshold = hi;

        bool PlannerOpens(long thousandths)
        {
            var walls = new[]
            {
                Wall("lo", "7", "0", "8", Length.FromThousandths(18000 - thousandths / 2).ToString().Replace("\"", "")),
                Wall("hi", "7", Length.FromThousandths(18000 + thousandths / 2).ToString().Replace("\"", ""), "8", "36"),
            };
            var field = Field("36", walls);
            var mover = Body("m", "3", "12");
            var target = Body("t", "12", "24");
            return Charging.CheckRoute(field, mover, target, [Enemy(target)]).IsLegal;
        }

        var first = -1L;
        var previous = false;
        for (var g = (long)(threshold * 1000) - 40; g <= (long)(threshold * 1000) + 40; g += 2)
        {
            var open = PlannerOpens(g);
            Assert.True(open || !previous, $"the doorway closes again at {g / 1000.0:0.000}\"");
            if (open && first < 0)
            {
                first = g;
            }
            previous = open;
            // Sound: nothing narrower than the true threshold is ever opened (the reference is a hair generous).
            if (g < threshold * 1000 - 2)
            {
                Assert.False(open, $"the planner passed a {g / 1000.0:0.000}\" doorway; the brute force needs {threshold:0.0000}\"");
            }
        }
        output.WriteLine($"brute-force threshold {threshold:0.0000}\"; the planner first opens at {first / 1000.0:0.000}\" (excess {first / 1000.0 - threshold:0.0000}\")");
        Assert.True(first > 0, "the planner never opened the doorway");
        Assert.True(first / 1000.0 - threshold <= 0.012, $"the planner needs {first / 1000.0 - threshold:0.0000}\" more doorway than the brute force");
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: closely spaced obstacles (a zig-zag of walls) give a legal, taut route")]
    public void AZigZagOfCloseWallsGivesALegalTautRoute()
    {
        var walls = new[]
        {
            Wall("a", "6", "0", "7", "14"),
            Wall("b", "8.3", "10", "9.3", "36"),
            Wall("c", "10.6", "0", "11.6", "14"),
        };
        var run = Run(walls, "3", "8", "14", "8");
        ReferenceRouter.ShortestToContact(3, 8, [.. walls.Select(w => Shape(w)), .. OffTable()], 14, 8, 1.0, 60, out var theirs);
        output.WriteLine("zig-zag oracle " + string.Join(" ", theirs.Select(p => $"({p.X:0.00},{p.Y:0.00})")));
        Assert.True(run.Plan.Reached);
        var pts = Points(run.Mover, run.Plan.Path);
        AssertLegal(run.Scene, pts, "zig-zag");
        AssertTaut(run.Scene, pts, "zig-zag");
        var excess = AssertMatchesReference(In(run.Plan.Cost), run.Reference, "zig-zag");
        output.WriteLine($"zig-zag: planner {In(run.Plan.Cost):0.000}\" brute force {run.Reference:0.000}\" excess {excess:0.0000}\"");
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: a base round a wall corner then past a second wall (circle-to-wall transitions)")]
    public void RoutesThatTurnRoundSeveralCornersAreLegalAndTaut()
    {
        var walls = new[]
        {
            Wall("a", "7", "5", "8", "11"),
            Wall("b", "9.2", "9", "13", "10"),
            Wall("c", "9.2", "4", "10.2", "8"),
        };
        var run = Run(walls, "3", "8", "12", "7");
        Assert.True(run.Plan.Reached);
        var pts = Points(run.Mover, run.Plan.Path);
        AssertLegal(run.Scene, pts, "corners");
        AssertTaut(run.Scene, pts, "corners");
        AssertMatchesReference(In(run.Plan.Cost), run.Reference, "corners");
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: a friendly base beside a wall, gone round when it cannot be crossed (circle-to-wall)")]
    public void ARouteRoundAFriendBesideAWallIsLegalAndTaut()
    {
        // The friend sits against the target, so the straight way to touch the target ends on it and the route has to
        // go round it. A wall just behind the friend makes the way round pass from the friend's circle to the wall.
        var wall = Wall("w", "9.6", "7.4", "11.6", "12");
        var field = Field("36", wall);
        var mover = Body("m", "3", "6");
        var target = Body("t", "12", "6");
        var friend = Body("f", "10.95", "6");
        var others = new[] { Enemy(target), Friend(friend) };
        Assert.True(Charging.CheckRoute(field, mover, target, others).IsLegal);

        // Reach is too short to pass the friend entirely, so the friends block.
        var plan = Charging.Plan(field, mover, target, L("9.2"), others);
        output.WriteLine($"friend/wall: {string.Join(" ", plan.Path)} cost {plan.Cost} reached {plan.Reached}");
        Assert.False(plan.Path.IsEmpty);
        var scene = SceneOf(mover, target, [wall], friend);
        var pts = Points(mover, plan.Path);
        AssertLegal(scene, pts, "friend and wall");

        // And with plenty of reach it is the full shortest route round the friend.
        var whole = Charging.Plan(field, mover, target, Far, others);
        Assert.True(whole.Reached);
        var wholePts = Points(mover, whole.Path);
        AssertLegal(scene, wholePts, "friend and wall (whole)");
        AssertTaut(scene, wholePts, "friend and wall (whole)");
        var reference = ReferenceRouter.ShortestToContact(3, 6,
            [ReferenceRouter.RoundedRect(9.6, 7.4, 11.6, 12, 0.5, RefQuarter), ReferenceRouter.Circle(10.95, 6, 1.0, RefQuarter), .. OffTable()], 12, 6, 1.0, ring: 60);
        AssertMatchesReference(In(whole.Cost), reference, "friend and wall");
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: random scenes with several walls: legal, taut and within tolerance of the brute force")]
    public void RandomMultiWallScenesAreLegalTautAndMatchTheBruteForce()
    {
        // AUDIT_SCENES and AUDIT_SEED let a longer hunt be run by hand; the defaults are the checked-in test.
        var scenes = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_SCENES"), out var n) ? n : 120;
        var rng = new Random(int.TryParse(Environment.GetEnvironmentVariable("AUDIT_SEED"), out var seed) ? seed : 7001);
        var detours = 0;
        var refused = 0;
        var worstExcess = double.NegativeInfinity;
        var worstShort = double.PositiveInfinity;
        for (var i = 0; i < scenes; i++)
        {
            var walls = new List<TerrainBlock>();
            for (var k = 0; k < 2 + rng.Next(2); k++)
            {
                var x = rng.Next(5500, 10000);
                var y = rng.Next(4000, 14000);
                walls.Add(Wall($"w{k}", Fmt(x), Fmt(y), Fmt(x + rng.Next(400, 2000)), Fmt(y + rng.Next(1500, 7000))));
            }
            if (rng.Next(3) == 0)
            {
                // A second wall set close beside the first, leaving a gap of 0.9" to 1.5": about a base wide.
                var first = Rect(walls[0]);
                var gap = rng.Next(900, 1500);
                var x0 = (long)Math.Round(first[2] * 1000) + gap;
                var y0 = (long)Math.Round(first[1] * 1000) + rng.Next(-1500, 1500);
                walls.Add(Wall("tight", Fmt(x0), Fmt(y0), Fmt(x0 + rng.Next(400, 1500)), Fmt(y0 + rng.Next(1500, 7000))));
            }
            var mx = rng.Next(2000, 4500);
            var my = rng.Next(8000, 16000);
            var tx = rng.Next(11500, 14000);
            var ty = rng.Next(8000, 16000);
            // Skip scenes where a wall is on the start or the target (neither can be placed there).
            var mover = Body("m", Fmt(mx), Fmt(my));
            var target = Body("t", Fmt(tx), Fmt(ty));
            if (walls.Any(w => w.Footprint.CircleOverlaps(mover.Centre, L("1.3")) || w.Footprint.CircleOverlaps(target.Centre, L("2.3"))))
            {
                continue;
            }

            var field = Field("36", [.. walls]);
            var others = new[] { Enemy(target) };
            var reference = ReferenceRouter.ShortestToContact(mx / 1000.0, my / 1000.0, [.. walls.Select(w => Shape(w)), .. OffTable()], tx / 1000.0, ty / 1000.0, 1.0, ring: 60);
            var check = Charging.CheckRoute(field, mover, target, others);
            var scene = SceneOf(mover, target, walls);
            if (!check.IsLegal)
            {
                refused++;
                // Refused: the brute force must agree that there is no way, even with every wall fattened by the
                // documented tolerance for narrow gaps.
                var fat = ReferenceRouter.ShortestToContact(mx / 1000.0, my / 1000.0, [.. walls.Select(w => Shape(w, 0.5 + 0.012)), .. OffTable()], tx / 1000.0, ty / 1000.0, 1.0, ring: 60);
                Assert.True(double.IsInfinity(fat), $"scene {i}: refused ({check.Text}) but a route of {fat:0.000}\" exists with walls fattened by 0.012\"");
                continue;
            }
            Assert.True(double.IsFinite(reference), $"scene {i}: the planner found a route but the brute force found none");
            var plan = Charging.Plan(field, mover, target, Far, others);
            Assert.True(plan.Reached, $"scene {i}");
            var pts = Points(mover, plan.Path);
            AssertLegal(scene, pts, $"scene {i}");
            AssertTaut(scene, pts, $"scene {i}");
            var excess = AssertMatchesReference(In(plan.Cost), reference, $"scene {i}");
            worstExcess = Math.Max(worstExcess, excess);
            worstShort = Math.Min(worstShort, excess);
            if (plan.Path.Length > 2)
            {
                detours++;
            }
        }
        output.WriteLine($"{detours} detours, {refused} refused; planner minus reference ranged {worstShort:0.0000}\" to {worstExcess:0.0000}\"");
        Assert.True(detours >= scenes / 3, $"only {detours} scenes needed a detour");
    }

    [Fact]
    [Trait("Rule", "Movement p. 35 / Charge Moves p. 37: with a friend beside the target the route is between the brute force with and without it")]
    public void RandomScenesWithAFriendBesideTheTargetStayBetweenTheTwoBruteForceBounds()
    {
        // A friend can be crossed when the model moves entirely past it, so the right answer is the route without the
        // friend (if that is legal) or round it (if not). Either way it can be no shorter than the route that ignores the
        // friend, and no longer than the route round it; the end must never overlap the friend.
        var scenes = int.TryParse(Environment.GetEnvironmentVariable("AUDIT_SCENES"), out var n) ? Math.Max(n / 4, 40) : 80;
        var rng = new Random(int.TryParse(Environment.GetEnvironmentVariable("AUDIT_SEED"), out var seed) ? seed + 1000 : 8001);
        var roundFriend = 0;
        for (var i = 0; i < scenes; i++)
        {
            var x = rng.Next(5500, 9000);
            var y = rng.Next(4000, 14000);
            var wall = Wall("w", Fmt(x), Fmt(y), Fmt(x + rng.Next(400, 2000)), Fmt(y + rng.Next(1500, 7000)));
            var mover = Body("m", Fmt(rng.Next(2000, 4500)), Fmt(rng.Next(8000, 16000)));
            var target = Body("t", Fmt(rng.Next(12500, 14500)), Fmt(rng.Next(8000, 16000)));
            var angle = rng.NextDouble() * 2 * Math.PI;
            var spacing = 1.0 + rng.NextDouble() * 1.3;   // friend 1.0" (touching) to 2.3" from the target's centre
            var friend = Body("f", Fmt((long)Math.Round(target.Centre.X.Thousandths + spacing * 1000 * Math.Cos(angle))), Fmt((long)Math.Round(target.Centre.Y.Thousandths + spacing * 1000 * Math.Sin(angle))));
            if (wall.Footprint.CircleOverlaps(mover.Centre, L("1.3")) || wall.Footprint.CircleOverlaps(target.Centre, L("2.3")) || wall.Footprint.CircleOverlaps(friend.Centre, L("1.3")))
            {
                continue;
            }
            var field = Field("36", wall);
            var others = new[] { Enemy(target), Friend(friend) };
            var check = Charging.CheckRoute(field, mover, target, others);
            Assert.True(check.IsLegal, $"scene {i}: {check.Text}");

            var rect = Rect(wall);
            var wallShape = ReferenceRouter.RoundedRect(rect[0], rect[1], rect[2], rect[3], 0.5, RefQuarter);
            double Ref(params ReferenceRouter.Shape[] extra) => ReferenceRouter.ShortestToContact(In(mover.Centre.X), In(mover.Centre.Y),
                [wallShape, .. extra, .. OffTable()], In(target.Centre.X), In(target.Centre.Y), 1.0, ring: 60);
            var without = Ref();
            var round = Ref(ReferenceRouter.Circle(In(friend.Centre.X), In(friend.Centre.Y), 1.0, RefQuarter));

            // Plenty of reach: the whole route.
            var plan = Charging.Plan(field, mover, target, Far, others);
            if (plan.Path.IsEmpty)
            {
                continue;   // the friend is where the model cannot be got past or round; a stop-short with nothing to move
            }
            var pts = Points(mover, plan.Path);
            var scene = SceneOf(mover, target, [wall]);
            AssertLegal(scene, pts, $"scene {i}");
            var cost = In(plan.Cost);
            Assert.True(cost >= without * 0.995 - 0.01, $"scene {i}: {cost:0.000} is shorter than the route ignoring the friend, {without:0.000}");
            if (plan.Reached)
            {
                Assert.True(double.IsInfinity(round) || cost <= round * 1.006 + 0.012, $"scene {i}: {cost:0.000} is longer than the route round the friend, {round:0.000}");
                Assert.False(Measure.Overlap(mover.At(plan.Path[^1]), friend), $"scene {i}: ends on the friend");
                if (cost > without * 1.006 + 0.012)
                {
                    roundFriend++;
                    // Gone round the friend: legal and taut against the friend as well.
                    var withFriend = SceneOf(mover, target, [wall], friend);
                    AssertLegal(withFriend, pts, $"scene {i} (round the friend)");
                    AssertTaut(withFriend, pts, $"scene {i} (round the friend)");
                }
            }
            else
            {
                // Short of the target: only if there really was no way to touch it.
                Assert.True(double.IsInfinity(round), $"scene {i}: stopped short ({cost:0.000}) although a route of {round:0.000} goes round the friend");
            }
        }
        output.WriteLine($"{roundFriend} of the scenes went round the friend");
        Assert.True(roundFriend >= 3, $"only {roundFriend} scenes went round the friend");
    }

    [Fact]
    [Trait("Rule", "Architecture: planning a charge in a crowded scene stays fast enough to run for every candidate and every replay")]
    public void ACrowdedSceneIsPlannedQuickly()
    {
        // Twelve friends and six walls on the table: the largest graph the planner builds (friends block).
        var walls = new[]
        {
            Wall("a", "6", "2", "7", "20"), Wall("b", "9", "10", "10", "30"), Wall("c", "12", "0", "13", "18"),
            Wall("d", "15", "12", "16", "34"), Wall("e", "18", "2", "19", "20"), Wall("f", "21", "10", "22", "30"),
        };
        var field = Field("36", walls);
        var mover = Body("m", "2", "15");
        var target = Body("t", "30", "15");
        var others = new List<Obstacle> { Enemy(target) };
        for (var i = 0; i < 12; i++)
        {
            others.Add(Friend(Body($"f{i}", $"{23 + i % 3 * 1.2:0.0}", $"{12 + i / 3 * 1.2:0.0}")));
        }
        var started = System.Diagnostics.Stopwatch.StartNew();
        var check = Charging.CheckRoute(field, mover, target, others);
        var plan = Charging.Plan(field, mover, target, L("12"), others);
        var whole = Charging.Plan(field, mover, target, Far, others);
        started.Stop();
        output.WriteLine($"crowded scene: {started.ElapsedMilliseconds} ms; legal {check.IsLegal}, move {plan.Path.Length} legs, whole {whole.Path.Length} legs, reached {whole.Reached}");
        Assert.True(started.ElapsedMilliseconds < 20000, $"{started.ElapsedMilliseconds} ms");
    }

    private static string Fmt(long thousandths) => $"{thousandths / 1000.0:0.000}";

    // ---- Difficult terrain ----

    [Fact]
    [Trait("Rule", "Movement p. 35: Difficult terrain costs double; the planner counts it but does not bend a route round it (U58)")]
    public void DifficultTerrainIsCountedInTheCostOfTheRoute()
    {
        // A wall to go round, and a rough patch across the way round it. The cost of the route is its length with the
        // part inside the patch counted twice, whatever route is chosen.
        var wall = Wall("w", "7", "4", "8", "10");
        var rough = Rough("r", "5", "10.2", "10", "12");
        var field = Field("36", wall, rough);
        var mover = Body("m", "3", "7");
        var target = Body("t", "12", "7");
        var others = new[] { Enemy(target) };
        var plan = Charging.Plan(field, mover, target, Far, others);
        Assert.True(plan.Reached);
        var pts = Points(mover, plan.Path);
        var length = Travelled(pts, 0, pts.Count - 1);
        Assert.Equal(Movement.Cost(field, mover, plan.Path), plan.Cost);
        output.WriteLine($"rough: length {length:0.000}\" cost {In(plan.Cost):0.000}\" via {string.Join(" ", plan.Path)}");
        // It went round the cheaper side: below the wall, away from the patch.
        Assert.True(plan.Path.Any(p => p.Y < L("4")));
        Assert.True(In(plan.Cost) >= length - 0.002);
        AssertLegal(SceneOf(mover, target, [wall]), pts, "rough");

        // Mirror image: put the patch below, and the route flips to the clear side. Cost and length are what the
        // planner would have paid on the other side.
        var flipped = Field("36", wall, Rough("r", "5", "2", "10", "3.8"));
        var plan2 = Charging.Plan(flipped, mover, target, Far, others);
        Assert.True(plan2.Reached);
        Assert.True(plan2.Path.Any(p => p.Y > L("10")));
        Assert.Equal(Movement.Cost(flipped, mover, plan2.Path), plan2.Cost);
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: a route is chosen by what it costs in Movement; whether 'shortest' means distance or cost is open")]
    public void WhenOneWayRoundCrossesRoughGroundTheCheaperWayIsTaken()
    {
        // Two ways round a wall. The upper is a little shorter in distance but crosses rough ground that doubles its
        // cost; the lower is clear. The planner minimises Movement spent, so it takes the lower. If the rulebook means
        // distance, it would take the upper: recorded as an open question (rules-notes), not decided here.
        var wall = Wall("w", "7", "5.3", "8", "10");   // centre of the wall's span is a little above the straight line
        var rough = Rough("r", "6", "10.4", "9", "12");
        var field = Field("36", wall, rough);
        var mover = Body("m", "3", "7");
        var target = Body("t", "12", "7");
        var plan = Charging.Plan(field, mover, target, Far, [Enemy(target)]);
        Assert.True(plan.Reached);
        var upper = plan.Path.Any(p => p.Y > L("10"));
        var lower = plan.Path.Any(p => p.Y < L("5.3"));
        output.WriteLine($"upper {upper} lower {lower} cost {plan.Cost}");
        Assert.True(upper ^ lower);
        Assert.True(lower, "the planner is expected to avoid the rough ground when the clear way is cheaper");
    }

    // ---- the reference router itself ----

    [Fact]
    [Trait("Rule", "Test oracle: the brute-force search agrees with a closed-form answer where one exists")]
    public void TheReferenceRouterMatchesTheClosedFormForOneDisc()
    {
        // From S round a disc of radius R at C to T (a point, goal radius ~0): two tangents and an arc.
        foreach (var (sx, sy, tx, ty, cx, cy, radius) in new[]
        {
            (0.0, 0.0, 10.0, 0.0, 5.0, 0.0, 1.0),
            (0.0, 1.0, 12.0, -1.0, 6.0, 0.2, 2.0),
            (2.0, 2.0, 9.0, 9.0, 5.0, 5.5, 1.5),
        })
        {
            var dS = Math.Sqrt(Math.Pow(sx - cx, 2) + Math.Pow(sy - cy, 2));
            var dT = Math.Sqrt(Math.Pow(tx - cx, 2) + Math.Pow(ty - cy, 2));
            var angle = Math.Acos(((sx - cx) * (tx - cx) + (sy - cy) * (ty - cy)) / (dS * dT));
            var exact = Math.Sqrt(dS * dS - radius * radius) + Math.Sqrt(dT * dT - radius * radius)
                + radius * (angle - Math.Acos(radius / dS) - Math.Acos(radius / dT));
            var straight = Math.Sqrt(Math.Pow(sx - tx, 2) + Math.Pow(sy - ty, 2));
            var blocked = DistPointSegment(cx, cy, sx, sy, tx, ty) < radius;
            var found = ReferenceRouter.ShortestToContact(sx, sy, [ReferenceRouter.Circle(cx, cy, radius)], tx, ty, 0.0005);
            var want = blocked ? exact : straight;
            output.WriteLine($"disc: closed form {want:0.00000} brute force {found:0.00000}");
            Assert.True(Math.Abs(found - want) <= 0.0005 * want + 0.002, $"closed form {want}, brute force {found}");
        }
    }

    [Fact]
    [Trait("Rule", "Test oracle: the brute-force answer does not move with finer sampling")]
    public void TheReferenceRouterConvergesAsItsSamplingIsRefined()
    {
        var wall = Rect(Wall("w", "7", "4", "8", "10"));
        double Reference(int perQuarter, int ring) =>
            ReferenceRouter.ShortestToContact(3, 7, [ReferenceRouter.RoundedRect(wall[0], wall[1], wall[2], wall[3], 0.5, perQuarter)], 12, 7.3, 1.0, ring);
        var coarse = Reference(5, 30);
        var medium = Reference(10, 60);
        var fine = Reference(40, 120);
        var finest = Reference(80, 240);
        output.WriteLine($"coarse {coarse:0.0000} medium {medium:0.0000} fine {fine:0.0000} finest {finest:0.0000}");
        Assert.True(coarse >= medium - 1e-9 && medium >= fine - 1e-9 && fine >= finest - 1e-9, "a finer outline cannot be longer");
        Assert.True(medium - finest <= 0.0017 * finest, $"{medium} vs {finest}");
        Assert.True(fine - finest <= 0.0003 * finest, $"{fine} vs {finest}");
    }
}
