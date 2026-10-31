namespace Rules.Core.Geometry;

public enum Relation
{
    Friendly,
    Enemy,
}

// Another model on the table, as the mover sees it. Down models are included like any other (p. 36).
public sealed record Obstacle(ModelBody Body, Relation Relation);

public enum MoveRefusal
{
    EmptyPath,
    OffBattlefield,
    PathTooLong,
    CrossesBlockingTerrain,
    CrossesEnemyBase,
    EntersEnemyZone,
    LeavesEnemyZone,
    EndsOnBase,

    // A charge only: no route within the 12" a charge can cover reaches the target around the terrain in the way.
    NoRoute,

    // Towers (towers proposal, stage T2): a tower was entered, left or crossed other than by one of its ladders.
    TowerNotByLadder,

    // A model on a tower's platform would have part of its base off the platform.
    OffPlatformEdge,

    // Towers, stage T6: a climb up or down a tower's side (away from its ladders) where none is allowed: in a Retreat or
    // a Charge, a second one in the same move, or one whose starting point the model could not stop at.
    SideClimbNotAllowed,
}

// SideClimb (towers proposal, stage T6, U75): on a legal path that climbs a tower's side away from its ladders, the index
// in the waypoints of the point the climb reaches. The waypoints before it are where the model stops if the Risky
// Success Roll fails (none: it is already at the side). Null for every other path.
public sealed record MoveResult(bool IsLegal, MoveRefusal? Reason, string Text, string RuleRef, Length Cost, string? SubjectId)
{
    public int? SideClimb { get; init; }
}

// Checking an ordinary Move (Movement p. 35) along a path of straight legs through the given waypoints.
// The model's centre follows the path and its base sweeps out a strip around it. Confirmed rules are marked
// below; provisional readings are U-numbered in docs/interpretations.md. Everything is integer arithmetic.
//
// Length. Each leg costs its true length, with the part inside Difficult terrain counting double (keyword text;
// proportional for a partial crossing, U8, measured along the centre's path). Each leg is rounded UP to a millionth
// of an inch, the legs are added, and the total is rounded UP once to the next thousandth (U12). The total never
// undercounts, a single leg of exactly the allowance is legal and a thousandth more is not, and splitting a path
// into many short legs costs the same to within a few millionths of an inch per leg, not a thousandth per leg.
//
// Order of refusal (the first that applies wins; ties go to the lowest id): empty path, off the battlefield,
// too long, blocking terrain, towers, enemy base, enemy 1" zone, ending on a base.
//
// Towers (towers proposal, stage T2; U66 to U68, U71). On a battlefield without towers nothing below changes. A tower is
// solid from the ground; on its platform the whole base stays on the platform; the only leg that changes level goes
// from a ladder's foot straight to its top or back (Climb.Foot, Climb.Top), and costs its length plus the climb (the
// tower's height). Every base is a disc at its elevation: a leg on the ground or on a platform meets only the models
// at its own elevation, and a ladder leg meets the ground models at its foot (the climb happens there) and the models
// at the top along the step over the edge. Towers on one battlefield share one height over 1", so models at different
// elevations are never within 1" of each other (U70).
//
// Climbing a side (stage T6; p. 39, U75, U76). With sideClimb (a Move or a Dash) one leg may also go straight up or down
// a side away from the ladders, shaped and costed like a ladder leg; the result names it (SideClimb) and the turn
// sequence rolls the Risky Success Roll before the model moves. The point where the climb starts must be one the model
// could stop at, since a failed roll leaves it there. Without sideClimb (a Retreat, a Charge's planner) such a leg is
// refused with its own reason.
public static class Movement
{
    public static MoveResult CheckPath(
        Battlefield battlefield,
        ModelBody mover,
        IReadOnlyList<Point2> waypoints,
        Length allowance,
        IEnumerable<Obstacle> others,
        string? chargeTargetId = null,
        bool retreat = false,
        bool sideClimb = false)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(others);
        if (allowance < Length.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(allowance));
        }

        if (waypoints.Count == 0)
        {
            return Refuse(MoveRefusal.EmptyPath, "A move needs at least one point to move to.", "1.0.2 p. 35", Length.Zero);
        }

        var path = new List<Point2>(waypoints.Count + 1) { mover.Centre };
        path.AddRange(waypoints);

        // Cost of the whole path, so a refusal can report it.
        var micro = PathMicro(battlefield, path);
        var cost = ToLength(micro);

        foreach (var waypoint in waypoints)
        {
            if (battlefield.Area.MoreThanHalfInside(waypoint, mover.Radius) != AreaDecision.MoreThanHalf)
            {
                return Refuse(MoveRefusal.OffBattlefield, "A model cannot move off the battlefield; more than half its base must stay on it.", "1.0.2 pp. 31, 35", cost);
            }
        }

        if (micro > (Int128)allowance.Thousandths * MicroPerThousandth)
        {
            return Refuse(MoveRefusal.PathTooLong, $"The path costs {cost} but the model can move {allowance}.", "1.0.2 p. 35", cost);
        }

        foreach (var terrain in battlefield.Terrain.Where(t => !t.IsWalkable))
        {
            for (var i = 1; i < path.Count; i++)
            {
                if (terrain.Footprint.SweptCircleOverlaps(path[i - 1], path[i], mover.Radius))
                {
                    return Refuse(MoveRefusal.CrossesBlockingTerrain, "The base would pass through terrain that cannot be crossed.", "1.0.2 pp. 31, 38, 39", cost, terrain.Id);
                }
            }
        }

        if (TowerRefusal(battlefield, mover.Radius, path, sideClimb, out var climbLeg) is { } tower)
        {
            return tower with { Cost = cost };
        }

        // What the base travels along, each at one elevation; on a flat battlefield, the legs themselves at the ground.
        var segments = Segments(battlefield, path);
        Length ElevationOf(Point2 p) => Towers.ElevationAt(battlefield, p);

        var sorted = others.Where(o => o.Body.Id != mover.Id).OrderBy(o => o.Body.Id, StringComparer.Ordinal).ToList();
        var enemies = sorted.Where(o => o.Relation == Relation.Enemy).Select(o => o.Body).ToList();

        // 1" is measured between bases and "within" includes exactly 1" (pp. 30, 36).
        bool StartedWithin(ModelBody enemy) => Measure.IsWithin(mover, enemy, Length.Inch);

        foreach (var enemy in enemies)
        {
            var hit = mover.Radius + enemy.Radius;
            foreach (var segment in segments.Where(g => g.Meets(enemy)))
            {
                if (Shapes.PointNearSegment(enemy.Centre, segment.A, segment.B, hit, strict: true))
                {
                    return Refuse(MoveRefusal.CrossesEnemyBase, "A model cannot move through an enemy model's base.", "1.0.2 pp. 31, 35", cost, enemy.Id);
                }
            }
        }

        foreach (var enemy in enemies)
        {
            var zone = mover.Radius + enemy.Radius + Length.Inch;
            if (StartedWithin(enemy))
            {
                if (retreat)
                {
                    // A retreat is the one move that may leave the enemies a model starts next to (p. 35, 37).
                    continue;
                }
                // The zone is a disc, which is convex, so staying within at every waypoint keeps the whole path within.
                // A waypoint at another elevation is out of it (U70).
                if (waypoints.Any(w => !ExactMath.Near(w, enemy.Centre, zone, strict: false) || ElevationOf(w) != enemy.Elevation))
                {
                    return Refuse(MoveRefusal.LeavesEnemyZone, "A model that starts within 1\" of an enemy must stay within 1\" of it throughout (or retreat).", "1.0.2 p. 35", cost, enemy.Id);
                }
            }
            else
            {
                if (enemy.Id == chargeTargetId)
                {
                    // A charge is the one move that may enter its target's 1" (p. 36).
                    continue;
                }
                var target = chargeTargetId is null ? null : enemies.FirstOrDefault(e => e.Id == chargeTargetId);
                foreach (var segment in segments.Where(g => g.Meets(enemy)))
                {
                    // Once a charging model is within 1" of its target, other enemy models no longer interpose (p. 36);
                    // a leg that starts there is the tail of the charge. Their bases still cannot be crossed.
                    if (target is not null && ExactMath.Near(segment.A, target.Centre, mover.Radius + target.Radius + Length.Inch, strict: false))
                    {
                        continue;
                    }
                    if (Shapes.PointNearSegment(enemy.Centre, segment.A, segment.B, zone, strict: false))
                    {
                        return Refuse(MoveRefusal.EntersEnemyZone, "A model can only move within 1\" of an enemy by charging.", "1.0.2 pp. 35, 36", cost, enemy.Id);
                    }
                }
            }
        }

        // Friendly models may be passed over but the base may not finish on one (p. 35, provisional U10).
        var end = path[^1];
        var friends = sorted.Where(o => o.Relation == Relation.Friendly).Select(o => o.Body).ToList();
        foreach (var friend in friends)
        {
            if (friend.Elevation == ElevationOf(end) && ExactMath.Near(end, friend.Centre, mover.Radius + friend.Radius, strict: true))
            {
                return Refuse(MoveRefusal.EndsOnBase, "A model cannot finish its move overlapping another model's base.", "1.0.2 pp. 31, 35", cost, friend.Id);
            }
        }

        if (climbLeg is { } leg)
        {
            // A failed climb stops the model where the climb starts (p. 39), so it must be a place it could stop at (U76).
            var stop = path[leg - 1];
            foreach (var friend in friends)
            {
                if (friend.Elevation == ElevationOf(stop) && ExactMath.Near(stop, friend.Centre, mover.Radius + friend.Radius, strict: true))
                {
                    return Refuse(MoveRefusal.SideClimbNotAllowed, SideClimbStopText, RefSideClimb, cost, friend.Id);
                }
            }
            return new MoveResult(true, null, "The move is legal.", "1.0.2 p. 35", cost, null) { SideClimb = leg - 1 };
        }

        return new MoveResult(true, null, "The move is legal.", "1.0.2 p. 35", cost, null);
    }

    public const string TowerNotByLadderText = "A tower can be climbed only by its ladders: from a ladder's foot straight to its top, or back.";
    public const string SideClimbText = "A tower is reached or left by its ladders, or by climbing straight up or down one of its sides with a Risky Success Roll: from the ground touching the side to the platform touching it from inside, or back.";
    public const string SideClimbOnlyMoveText = "Climbing a tower's side takes a Risky Success Roll and is part of a Move or a Dash only, not a Retreat or a Charge.";
    public const string SideClimbOnceText = "A move can climb a tower's side only once in this version.";
    public const string SideClimbStopText = "A failed climb stops the model where the climb starts, and it cannot stop on another model's base.";
    private const string RefSideClimb = "1.0.2 p. 39";
    public const string OffPlatformEdgeText = "A model on a tower must stay wholly on its platform.";
    private const string RefTowers = "1.0.2 pp. 39-40";

    // The first leg that enters, leaves or crosses a tower other than by a ladder, or puts part of the base off a
    // platform; null when there is none (always null on a battlefield without towers).
    // With sideClimb, one leg straight up or down a side away from a ladder is allowed and its index in the path is given
    // back (U75, U76); without, such a leg is refused with its own reason.
    private static MoveResult? TowerRefusal(Battlefield battlefield, Length radius, IReadOnlyList<Point2> path, bool sideClimb, out int? climbLeg)
    {
        climbLeg = null;
        var towers = battlefield.Terrain.Where(t => t.IsTower).ToList();
        if (towers.Count == 0)
        {
            return null;
        }
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            var from = Towers.TowerAt(battlefield, a);
            var to = Towers.TowerAt(battlefield, b);
            if (from is null && to is null)
            {
                // On the ground a tower is solid (its base cannot pass over the footprint's inside).
                if (towers.FirstOrDefault(t => t.Footprint.SweptCircleOverlaps(a, b, radius)) is { } solid)
                {
                    return Refuse(MoveRefusal.TowerNotByLadder, TowerNotByLadderText, RefTowers, Length.Zero, solid.Id);
                }
            }
            else if (from is not null && from == to)
            {
                // On the platform: the base wholly on it at both ends, so along the whole leg (the set of such centres is
                // a rectangle, which is convex).
                if (!from.Footprint.CircleInside(a, radius) || !from.Footprint.CircleInside(b, radius))
                {
                    return Refuse(MoveRefusal.OffPlatformEdge, OffPlatformEdgeText, RefTowers, Length.Zero, from.Id);
                }
            }
            else if (from is null || to is null)
            {
                var tower = (from ?? to)!;
                var (ground, top) = from is null ? (a, b) : (b, a);
                var rect = tower.Footprint.Bounds;
                if (!tower.Climb!.Ladders.Any(l => Climb.Foot(rect, l, radius) == ground && Climb.Top(rect, l, radius) == top))
                {
                    if (!Towers.IsSideClimb(tower, radius, ground, top))
                    {
                        return Refuse(MoveRefusal.TowerNotByLadder, sideClimb ? SideClimbText : TowerNotByLadderText, RefTowers, Length.Zero, tower.Id);
                    }
                    if (!sideClimb)
                    {
                        return Refuse(MoveRefusal.SideClimbNotAllowed, SideClimbOnlyMoveText, RefSideClimb, Length.Zero, tower.Id);
                    }
                    if (climbLeg is not null)
                    {
                        return Refuse(MoveRefusal.SideClimbNotAllowed, SideClimbOnceText, RefSideClimb, Length.Zero, tower.Id);
                    }
                    climbLeg = i;
                }
            }
            else
            {
                // From one tower straight to another: there are no walkways.
                return Refuse(MoveRefusal.TowerNotByLadder, TowerNotByLadderText, RefTowers, Length.Zero, to!.Id);
            }
        }
        return null;
    }

    // A stretch of the move at one elevation (Low == High), or the climb at a ladder's foot (A == B, Low below High).
    private readonly record struct Segment(Point2 A, Point2 B, Length Low, Length High)
    {
        // Does a base at this elevation share a height with this stretch?
        public bool Meets(ModelBody other) => other.Elevation >= Low && other.Elevation <= High;
    }

    private static List<Segment> Segments(Battlefield battlefield, IReadOnlyList<Point2> path)
    {
        var segments = new List<Segment>(path.Count);
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            var za = Towers.ElevationAt(battlefield, a);
            var zb = Towers.ElevationAt(battlefield, b);
            if (za == zb)
            {
                segments.Add(new Segment(a, b, za, za));
            }
            else if (za < zb)
            {
                // Up a ladder: climb at the foot, then step over the edge at the top's height.
                segments.Add(new Segment(a, a, za, zb));
                segments.Add(new Segment(a, b, zb, zb));
            }
            else
            {
                // Down: step back over the edge at the top's height, then climb down at the foot.
                segments.Add(new Segment(a, b, za, za));
                segments.Add(new Segment(b, b, zb, za));
            }
        }
        return segments;
    }

    private static MoveResult Refuse(MoveRefusal reason, string text, string rule, Length cost, string? subject = null) =>
        new(false, reason, text, rule, cost, subject);

    // Cost of a whole path (starting at the model's centre), rounded up once to the next thousandth.
    public static Length Cost(Battlefield battlefield, ModelBody mover, IReadOnlyList<Point2> waypoints)
    {
        var path = new List<Point2>(waypoints.Count + 1) { mover.Centre };
        path.AddRange(waypoints);
        return PathCost(battlefield, path);
    }

    private static Length PathCost(Battlefield battlefield, IReadOnlyList<Point2> path) => ToLength(PathMicro(battlefield, path));

    private static Int128 PathMicro(Battlefield battlefield, IReadOnlyList<Point2> path)
    {
        var difficult = battlefield.Terrain.Where(t => t.IsDifficult).Select(t => t.Footprint).ToList();
        var towers = battlefield.Terrain.Any(t => t.IsTower);
        Int128 micro = 0;
        for (var i = 1; i < path.Count; i++)
        {
            micro += LegCostMicro(path[i - 1], path[i], difficult);
            if (towers)
            {
                // Climbing a ladder costs its height (p. 40's example counts the climb; U66). Only ladder legs change level.
                var climb = Towers.ElevationAt(battlefield, path[i]).Thousandths - Towers.ElevationAt(battlefield, path[i - 1]).Thousandths;
                micro += (Int128)Math.Abs(climb) * MicroPerThousandth;
            }
        }
        return micro;
    }

    // Rounds up to a thousandth. A path longer than a Length can hold (1,000,000 inches) reports that largest
    // Length; it is far above any allowance, so such a path is refused as too long, not thrown at the caller.
    private static Length ToLength(Int128 micro) =>
        Length.FromThousandths((long)Int128.Min(ExactMath.CeilDiv(micro, MicroPerThousandth), Length.MaxThousandths));

    // Leg costs are carried in millionths of an inch (U12).
    internal const int MicroPerThousandth = 1000;

    // The cost of one straight leg in millionths of an inch, rounded up, with the part inside Difficult terrain
    // counting double. Every step rounds in the safe direction, so the cost is never below the true cost and is
    // above it by under three millionths of an inch per leg.
    internal static Int128 LegCostMicro(Point2 a, Point2 b, IReadOnlyList<Footprint> difficult)
    {
        Int128 dx = b.X.Thousandths - a.X.Thousandths;
        Int128 dy = b.Y.Thousandths - a.Y.Thousandths;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            return 0;
        }

        // The part of the leg inside Difficult terrain, as inside / den of its length, on a common denominator.
        var den = dx == 0 ? Int128.Abs(dy) : dy == 0 ? Int128.Abs(dx) : Int128.Abs(dx) * Int128.Abs(dy);
        var spans = new List<(Int128 Start, Int128 End)>();
        foreach (var footprint in difficult)
        {
            foreach (var (lo, hi) in footprint.Crossings(a, b))
            {
                spans.Add((lo.Num * (den / lo.Den), hi.Num * (den / hi.Den)));
            }
        }
        spans.Sort((x, y) => x.Start.CompareTo(y.Start));
        Int128 inside = 0;
        Int128 coveredTo = 0;
        foreach (var (start, end) in spans)
        {
            var from = Int128.Max(start, coveredTo);
            if (end > from)
            {
                inside += end - from;
                coveredTo = end;
            }
        }

        // length in millionths, rounded up, times (den + inside) / den, rounded up.
        var length = ExactMath.CeilSqrt(lengthSquared * 1_000_000);
        return ExactMath.CeilDiv(length * (den + inside), den);
    }
}
