using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// One of the defender's Mine Markers in Trench Warfare (p. 162): a 1" disc on the table. Whether it is a dud is secret
// and is not part of the marker.
public sealed record MineMarker
{
    public MineMarker(string id, Point2 at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        At = at;
    }

    public string Id { get; }
    public Point2 At { get; }
}

// Where a move halts for a Mine Marker: the marker, the point, and the index in the move's waypoints of the first
// waypoint still to go after it (the rest of the path is Waypoints[Next..]).
public sealed record MineStopPoint(string Marker, Point2 At, int Next);

// Mine Markers and Slaughter Mines (Trench Warfare, p. 162; U93). Integer arithmetic only.
//
// A marker is a 1" disc (Markers are up to 40 mm, p. 146) set up wholly within the Mine Placement Area: the 12" of No
// Man's Land next to the defender's deployment zone (the p. 161 map). Markers do not overlap each other and do not
// overlap terrain that cannot be crossed or a tower.
//
// A model halts as soon as its base comes within 3" of a marker it was not already within 3" of. Distances are measured
// across the table between the base and the marker as seen from above. The halt point is where the centre's path first
// comes that close, moved back to whole thousandths of an inch toward the start of that leg. Where the model could not
// stand there (part-way up a ladder or a tower's side, its base on another model's base or on terrain it cannot
// cross, or off the table) it halts instead at the last waypoint before it where it can, or where it started. A halt
// never goes back past an earlier halt of the same move.
public static class Mines
{
    public const int Count = 12;
    public const int Duds = 4;

    public static readonly Length MarkerRadius = Length.HalfInch;
    public static readonly Length TriggerRange = Length.FromInches(3);
    public static readonly Length PlacementDepth = Length.FromInches(12);

    public static string IdOf(int index) => $"mine-{index + 1}";

    // The Mine Placement Area: the strip PlacementDepth deep along the side of the defender's zone that faces the
    // attacker's, as wide as the defender's zone. Null when the zones do not face each other across one axis.
    public static Rect? PlacementArea(Battlefield battlefield, string defenderZoneId, string attackerZoneId)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        var defender = battlefield.Zone(defenderZoneId)?.Area.Bounds;
        var attacker = battlefield.Zone(attackerZoneId)?.Area.Bounds;
        if (defender is not { } d || attacker is not { } a)
        {
            return null;
        }
        var table = battlefield.Area.Bounds;
        Rect? strip = null;
        if (a.Min.X >= d.Max.X)
        {
            strip = new Rect(new Point2(d.Max.X, d.Min.Y), new Point2(Min(d.Max.X + PlacementDepth, a.Min.X), d.Max.Y));
        }
        else if (a.Max.X <= d.Min.X)
        {
            strip = new Rect(new Point2(Max(d.Min.X - PlacementDepth, a.Max.X), d.Min.Y), new Point2(d.Min.X, d.Max.Y));
        }
        else if (a.Min.Y >= d.Max.Y)
        {
            strip = new Rect(new Point2(d.Min.X, d.Max.Y), new Point2(d.Max.X, Min(d.Max.Y + PlacementDepth, a.Min.Y)));
        }
        else if (a.Max.Y <= d.Min.Y)
        {
            strip = new Rect(new Point2(d.Min.X, Max(d.Min.Y - PlacementDepth, a.Max.Y)), new Point2(d.Max.X, d.Min.Y));
        }
        return strip is { } s && s.Width > Length.Zero && s.Height > Length.Zero && table.Contains(s.Min) && table.Contains(s.Max) ? s : null;
    }

    // Why a marker cannot go here, or null when it can. Placed are the markers already set up.
    public static string? Problem(Battlefield battlefield, Rect area, Point2 at, IEnumerable<Point2> placed)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(placed);
        if (!Footprint.Of(area).CircleInside(at, MarkerRadius))
        {
            return "A Mine Marker must be wholly within the Mine Placement Area.";
        }
        if (placed.Any(p => ExactMath.Near(p, at, MarkerRadius + MarkerRadius, strict: true)))
        {
            return "Mine Markers cannot overlap each other.";
        }
        if (battlefield.Terrain.FirstOrDefault(t => (!t.IsWalkable || t.IsTower) && t.Footprint.CircleOverlaps(at, MarkerRadius)) is { } blocked)
        {
            return $"A Mine Marker cannot stand on '{blocked.Id}'.";
        }
        return null;
    }

    // Why a whole set of markers cannot be set up, or null when it can: exactly Count of them, each where Problem allows.
    public static string? Problem(Battlefield battlefield, Rect area, IReadOnlyList<Point2> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (markers.Count != Count)
        {
            return $"The defender sets up {Count} Mine Markers.";
        }
        for (var i = 0; i < markers.Count; i++)
        {
            if (Problem(battlefield, area, markers[i], markers.Take(i)) is { } problem)
            {
                return $"{IdOf(i)}: {problem}";
            }
        }
        return null;
    }

    // Points where markers could go, on a 1" grid across the area in row order, each clear of the ones before it. Used
    // for a default layout; any legal layout may be chosen instead.
    public static ImmutableArray<Point2> Grid(Battlefield battlefield, Rect area)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        var points = new List<Point2>();
        var step = Length.Inch.Thousandths;
        for (var y = area.Min.Y.Thousandths + MarkerRadius.Thousandths; y + MarkerRadius.Thousandths <= area.Max.Y.Thousandths; y += step)
        {
            for (var x = area.Min.X.Thousandths + MarkerRadius.Thousandths; x + MarkerRadius.Thousandths <= area.Max.X.Thousandths; x += step)
            {
                var p = new Point2(Length.FromThousandths(x), Length.FromThousandths(y));
                if (Problem(battlefield, area, p, points) is null)
                {
                    points.Add(p);
                }
            }
        }
        return [.. points];
    }

    // Is the base within TriggerRange of the marker (edge to edge, from above)?
    public static bool IsWithin(Point2 centre, Length radius, Point2 marker) =>
        ExactMath.Near(centre, marker, radius + MarkerRadius + TriggerRange, strict: false);

    // The halts of a move along these waypoints from the mover's centre, in path order. Others are the other models on
    // the table (for where the mover could stand).
    public static ImmutableArray<MineStopPoint> Stops(
        Battlefield battlefield,
        ModelBody mover,
        IReadOnlyList<Point2> waypoints,
        IEnumerable<MineMarker> markers,
        IEnumerable<ModelBody> others)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(others);
        var points = new List<Point2>(waypoints.Count + 1) { mover.Centre };
        points.AddRange(waypoints);
        var reach = (Int128)(mover.Radius + MarkerRadius + TriggerRange).Thousandths;

        // Each marker's first entry: the leg (1-based, ending at points[leg]) and a fraction no later than the exact one.
        var entries = new List<(int Leg, ExactMath.Ratio T, MineMarker Marker)>();
        foreach (var marker in markers)
        {
            var inside = IsWithin(points[0], mover.Radius, marker.At);
            for (var leg = 1; leg < points.Count; leg++)
            {
                if (!inside && Entry(points[leg - 1], points[leg], marker.At, reach) is { } t)
                {
                    entries.Add((leg, t, marker));
                    break;
                }
                inside = IsWithin(points[leg], mover.Radius, marker.At);
            }
        }
        entries.Sort((a, b) =>
        {
            var c = a.Leg.CompareTo(b.Leg);
            if (c == 0)
            {
                c = a.T.CompareTo(b.T);
            }
            return c != 0 ? c : string.CompareOrdinal(a.Marker.Id, b.Marker.Id);
        });

        var bodies = others.Where(o => o.Id != mover.Id).ToList();
        var stops = ImmutableArray.CreateBuilder<MineStopPoint>(entries.Count);
        // The furthest halt so far as (leg, fraction): a later halt never goes back past it.
        (int Leg, ExactMath.Ratio T) last = (0, ExactMath.Ratio.Of(1, 1));
        var lastAt = points[0];
        var lastNext = 0;
        foreach (var (leg, t, marker) in entries)
        {
            var (at, key, next) = StopFor(battlefield, mover.Radius, points, bodies, leg, t);
            if (Compare(key, last) < 0)
            {
                (at, key, next) = (lastAt, last, lastNext);
            }
            stops.Add(new MineStopPoint(marker.Id, at, next));
            (last, lastAt, lastNext) = (key, at, next);
        }
        return stops.MoveToImmutable();
    }

    // The waypoints walked from one halt to the next (or to the end of the move when next is null): the waypoints between
    // them, then the next halt's point unless it is the last of those already. May be empty.
    public static ImmutableArray<Point2> Between(IReadOnlyList<Point2> waypoints, MineStopPoint from, MineStopPoint? next)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(from);
        var end = next?.Next ?? waypoints.Count;
        var walked = new List<Point2>();
        for (var i = from.Next; i < end; i++)
        {
            walked.Add(waypoints[i]);
        }
        if (next is not null)
        {
            var previous = walked.Count > 0 ? walked[^1] : from.At;
            if (previous != next.At)
            {
                walked.Add(next.At);
            }
        }
        return [.. walked];
    }

    // The waypoints walked from the start of the move to its first halt.
    public static ImmutableArray<Point2> ToFirst(IReadOnlyList<Point2> waypoints, MineStopPoint first)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(first);
        var walked = waypoints.Take(first.Next).ToList();
        if (walked.Count == 0 || walked[^1] != first.At)
        {
            walked.Add(first.At);
        }
        return [.. walked];
    }

    private static int Compare((int Leg, ExactMath.Ratio T) a, (int Leg, ExactMath.Ratio T) b) =>
        a.Leg != b.Leg ? a.Leg.CompareTo(b.Leg) : a.T.CompareTo(b.T);

    // Where the halt for an entry on this leg is, as a point, its place on the path, and the next waypoint index.
    private static (Point2 At, (int Leg, ExactMath.Ratio T) Key, int Next) StopFor(
        Battlefield battlefield, Length radius, List<Point2> points, List<ModelBody> others, int leg, ExactMath.Ratio t)
    {
        var a = points[leg - 1];
        var b = points[leg];
        var level = Towers.ElevationAt(battlefield, a) == Towers.ElevationAt(battlefield, b);
        var at = PointAt(a, b, t);
        if (level && CanStand(battlefield, radius, at, others))
        {
            // At the leg's start the remaining waypoints are the same as part-way along it.
            return (at, (leg, t), leg - 1);
        }
        for (var j = leg - 1; j > 0; j--)
        {
            if (CanStand(battlefield, radius, points[j], others))
            {
                return (points[j], (j, ExactMath.Ratio.Of(1, 1)), j);
            }
        }
        return (points[0], (0, ExactMath.Ratio.Of(1, 1)), 0);
    }

    // Could the model's base rest here: more than half on the table, on no other base, clear of terrain it cannot cross,
    // and either wholly on a tower's platform or clear of every tower?
    private static bool CanStand(Battlefield battlefield, Length radius, Point2 at, List<ModelBody> others)
    {
        if (battlefield.Area.MoreThanHalfInside(at, radius) != AreaDecision.MoreThanHalf)
        {
            return false;
        }
        var elevation = Towers.ElevationAt(battlefield, at);
        var body = new ModelBody("mover", at, radius, elevation);
        if (others.Any(o => Measure.Overlap(body, o)))
        {
            return false;
        }
        foreach (var terrain in battlefield.Terrain)
        {
            if (terrain.IsTower)
            {
                var on = elevation > Length.Zero && terrain.Footprint.ContainsInside(at);
                if (on ? !terrain.Footprint.CircleInside(at, radius) : terrain.Footprint.CircleOverlaps(at, radius))
                {
                    return false;
                }
            }
            else if (!terrain.IsWalkable && terrain.Footprint.CircleOverlaps(at, radius))
            {
                return false;
            }
        }
        return true;
    }

    // The point at fraction t of a-b, each coordinate moved toward a to whole thousandths.
    private static Point2 PointAt(Point2 a, Point2 b, ExactMath.Ratio t)
    {
        var dx = (Int128)(b.X.Thousandths - a.X.Thousandths);
        var dy = (Int128)(b.Y.Thousandths - a.Y.Thousandths);
        return new Point2(
            Length.FromThousandths(a.X.Thousandths + (long)(t.Num * dx / t.Den)),
            Length.FromThousandths(a.Y.Thousandths + (long)(t.Num * dy / t.Den)));
    }

    // The first fraction of the leg a-b at which the centre comes within reach of c, starting from outside it, rounded
    // down (never later than the exact point). Null when the leg never comes that close.
    private static ExactMath.Ratio? Entry(Point2 a, Point2 b, Point2 c, Int128 reach)
    {
        var dx = (Int128)(b.X.Thousandths - a.X.Thousandths);
        var dy = (Int128)(b.Y.Thousandths - a.Y.Thousandths);
        var wx = (Int128)(a.X.Thousandths - c.X.Thousandths);
        var wy = (Int128)(a.Y.Thousandths - c.Y.Thousandths);
        var qa = dx * dx + dy * dy;
        if (qa == 0)
        {
            return null;
        }
        var qb = 2 * (dx * wx + dy * wy);
        var qc = wx * wx + wy * wy - reach * reach;
        if (qc <= 0 || qb >= 0)
        {
            // Already within reach at a (the caller rules that out), or moving away from c.
            return null;
        }
        var disc = qb * qb - 4 * qa * qc;
        if (disc < 0)
        {
            return null;
        }
        // The smaller root (-qb - sqrt(disc)) / 2qa lies after a; it is on the leg when it is at most 1.
        var beyond = -qb - 2 * qa;
        if (beyond > 0 && beyond * beyond > disc)
        {
            return null;
        }
        var num = -qb - ExactMath.CeilSqrt(disc);
        return new ExactMath.Ratio(num < 0 ? 0 : num, 2 * qa);
    }

    private static Length Min(Length a, Length b) => a < b ? a : b;

    private static Length Max(Length a, Length b) => a > b ? a : b;
}
