using Rules.Core;
using Rules.Core.Geometry;

namespace Client.Core;

// Towers proposal, stage T4: the route a click suggests on a battlefield with towers. The rules accept a change of level
// only along a ladder leg, from a ladder's foot straight to its top or back (Rules.Core.Geometry.Movement, U68, U71), so
// a click on a tower's platform from the ground (or on the ground from a platform) is routed through the ladder that
// makes the route shortest: the foot and top points are put in for the player. A point picked on a platform near its
// edge is moved just far enough in for the whole base to stand on it (U67). Nothing here decides anything: the path is
// the one the command carries, and the preview and the server check it as they check any path. On a battlefield
// without towers the points come back unchanged.
//
// Stage T6: with sides, a change of level climbs straight up or down a side of the tower instead (a Risky Success Roll,
// p. 39, U75), at the point of the sides that makes the route shortest.
public static class TowerRoute
{
    public static List<Point2> Expand(Battlefield field, Length radius, Point2 start, IReadOnlyList<Point2> points, bool sides = false)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(points);
        if (!field.Terrain.Any(t => t.IsTower))
        {
            return [.. points];
        }
        var route = new List<Point2>(points.Count + 4);
        var from = start;
        foreach (var picked in points)
        {
            var to = OnPlatform(field, radius, picked);
            var up = Towers.TowerAt(field, from);
            var down = Towers.TowerAt(field, to);
            if (up != down)
            {
                if (up is not null)
                {
                    // Down from this platform by its ladder nearest the way on.
                    var (foot, top) = sides ? NearestSide(up, radius, to, from) : Nearest(up, radius, to, from);
                    Add(route, from, top);
                    Add(route, from, foot);
                }
                if (down is not null)
                {
                    var last = route.Count > 0 ? route[^1] : from;
                    var (foot, top) = sides ? NearestSide(down, radius, last, to) : Nearest(down, radius, last, to);
                    Add(route, from, foot);
                    Add(route, from, top);
                }
            }
            Add(route, from, to);
            from = to;
        }
        return route;
    }

    // The point itself, or for a point inside a tower's footprint the nearest point where the whole base is on its
    // platform (when the platform is wide enough for the base at all).
    public static Point2 OnPlatform(Battlefield field, Length radius, Point2 p)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (Towers.TowerAt(field, p) is not { } tower)
        {
            return p;
        }
        var r = tower.Footprint.Bounds;
        if (r.Max.X - r.Min.X < radius + radius || r.Max.Y - r.Min.Y < radius + radius)
        {
            return p;
        }
        return new Point2(Clamp(p.X, r.Min.X + radius, r.Max.X - radius), Clamp(p.Y, r.Min.Y + radius, r.Max.Y - radius));
    }

    // The ladder of this tower that makes the way between a point on the ground and a point on its platform shortest
    // (up or down), as its foot and top points for this base. Ties go to the ladder first in id order.
    private static (Point2 Foot, Point2 Top) Nearest(TerrainBlock tower, Length radius, Point2 ground, Point2 platform)
    {
        var rect = tower.Footprint.Bounds;
        return tower.Climb!.Ladders
            .Select(l => (Foot: Climb.Foot(rect, l, radius), Top: Climb.Top(rect, l, radius)))
            .OrderBy(l => Distance(ground, l.Foot) + Distance(l.Top, platform))
            .First();
    }

    // The point of the tower's sides where climbing straight up or down makes the way between a point on the ground and a
    // point on its platform shortest, as the foot and top for this base. Along each side the length is convex, so a
    // search over thousandths finds its least; ties go to the lower coordinate and to the sides in Side order.
    private static (Point2 Foot, Point2 Top) NearestSide(TerrainBlock tower, Length radius, Point2 ground, Point2 platform)
    {
        var r = tower.Footprint.Bounds;
        var best = (Foot: ground, Top: platform);
        var bestLength = double.MaxValue;
        foreach (var side in new[] { Side.West, Side.East, Side.South, Side.North })
        {
            var alongY = side is Side.West or Side.East;
            var low = (alongY ? r.Min.Y : r.Min.X).Thousandths + radius.Thousandths;
            var high = (alongY ? r.Max.Y : r.Max.X).Thousandths - radius.Thousandths;
            if (low > high)
            {
                continue;
            }
            (Point2 Foot, Point2 Top) At(long t)
            {
                var c = Length.FromThousandths(t);
                return side switch
                {
                    Side.West => (new Point2(r.Min.X - radius, c), new Point2(r.Min.X + radius, c)),
                    Side.East => (new Point2(r.Max.X + radius, c), new Point2(r.Max.X - radius, c)),
                    Side.South => (new Point2(c, r.Min.Y - radius), new Point2(c, r.Min.Y + radius)),
                    _ => (new Point2(c, r.Max.Y + radius), new Point2(c, r.Max.Y - radius)),
                };
            }
            double LengthAt(long t)
            {
                var (foot, top) = At(t);
                return Distance(ground, foot) + Distance(top, platform);
            }
            var (a, b) = (low, high);
            while (b - a > 2)
            {
                var m1 = a + (b - a) / 3;
                var m2 = b - (b - a) / 3;
                if (LengthAt(m1) <= LengthAt(m2))
                {
                    b = m2;
                }
                else
                {
                    a = m1;
                }
            }
            for (var t = a; t <= b; t++)
            {
                var length = LengthAt(t);
                if (length < bestLength - 1e-12)
                {
                    bestLength = length;
                    best = At(t);
                }
            }
        }
        return best;
    }

    private static void Add(List<Point2> route, Point2 start, Point2 p)
    {
        var last = route.Count > 0 ? route[^1] : start;
        if (p != last)
        {
            route.Add(p);
        }
    }

    private static Length Clamp(Length v, Length low, Length high) => v < low ? low : v > high ? high : v;

    private static double Distance(Point2 a, Point2 b)
    {
        var dx = (a.X.Thousandths - b.X.Thousandths) / 1000.0;
        var dy = (a.Y.Thousandths - b.Y.Thousandths) / 1000.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
