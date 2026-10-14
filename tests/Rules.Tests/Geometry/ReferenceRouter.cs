namespace Rules.Tests.Geometry;

// An independent check on the charge planner: the shortest route of a disc's centre to touch a goal disc around
// convex obstacles, found in floating point from finely sampled outlines. It shares no code with Rules.Core (no
// polygon sizes, no integer geometry, no visibility rules), so a route the planner calls shortest can be compared with
// what a brute-force search finds. Floating point is fine here: it is a test oracle, never a rule decision.
internal static class ReferenceRouter
{
    public sealed record Shape(IReadOnlyList<(double X, double Y)> Corners);

    // Corners per quarter turn of a rounded outline. The sampled outline is circumscribed, so it is never smaller than
    // the true one; 40 per quarter is within 0.015% of the true length.
    private const int PerQuarter = 40;

    // The forbidden region for a disc's centre around a circle: the circle widened by the mover's radius.
    public static Shape Circle(double cx, double cy, double radius)
    {
        var n = PerQuarter * 4;
        var big = radius / Math.Cos(Math.PI / n);
        return new Shape([.. Enumerable.Range(0, n).Select(k => (cx + big * Math.Cos(2 * Math.PI * k / n), cy + big * Math.Sin(2 * Math.PI * k / n)))]);
    }

    // The forbidden region around a rectangle: the rectangle with quarter-circle corners of the mover's radius.
    public static Shape RoundedRect(double x1, double y1, double x2, double y2, double radius)
    {
        var big = radius / Math.Cos(Math.PI / (PerQuarter * 4));
        var points = new List<(double, double)>();
        var corners = new[] { (x2, y2, 0.0), (x1, y2, 90.0), (x1, y1, 180.0), (x2, y1, 270.0) };
        foreach (var (cx, cy, start) in corners)
        {
            for (var k = 0; k <= PerQuarter; k++)
            {
                var angle = (start + 90.0 * k / PerQuarter) * Math.PI / 180;
                points.Add((cx + big * Math.Cos(angle), cy + big * Math.Sin(angle)));
            }
        }
        return new Shape(points);
    }

    // Length of the shortest route from the start to contact with the target (a goal disc of the given radius), or
    // infinity if none exists. Dijkstra over the start, every outline corner and a ring of goal points.
    public static double ShortestToContact(double sx, double sy, IReadOnlyList<Shape> shapes, double tx, double ty, double goalRadius)
    {
        var nodes = new List<(double X, double Y)> { (sx, sy) };
        foreach (var shape in shapes)
        {
            nodes.AddRange(shape.Corners);
        }
        var firstGoal = nodes.Count;
        const int ring = 120;
        for (var k = 0; k < ring; k++)
        {
            nodes.Add((tx + goalRadius * Math.Cos(2 * Math.PI * k / ring), ty + goalRadius * Math.Sin(2 * Math.PI * k / ring)));
        }

        var dist = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var done = new bool[nodes.Count];
        dist[0] = 0;
        while (true)
        {
            var u = -1;
            for (var i = 0; i < nodes.Count; i++)
            {
                if (!done[i] && !double.IsPositiveInfinity(dist[i]) && (u < 0 || dist[i] < dist[u]))
                {
                    u = i;
                }
            }
            if (u < 0)
            {
                break;
            }
            done[u] = true;
            if (u >= firstGoal)
            {
                return dist[u];
            }
            for (var v = 1; v < nodes.Count; v++)
            {
                if (done[v])
                {
                    continue;
                }
                // The goal disc is not an obstacle to reach: a leg to a ring point must not cross its inside.
                if (Crosses(nodes[u], nodes[v], shapes) || (v >= firstGoal && CrossesDisc(nodes[u], nodes[v], tx, ty, goalRadius)))
                {
                    continue;
                }
                var alt = dist[u] + Math.Sqrt(Math.Pow(nodes[u].X - nodes[v].X, 2) + Math.Pow(nodes[u].Y - nodes[v].Y, 2));
                if (alt < dist[v])
                {
                    dist[v] = alt;
                }
            }
        }
        return double.PositiveInfinity;
    }

    // Does the segment pass through the inside of the disc (touching does not count)?
    private static bool CrossesDisc((double X, double Y) a, (double X, double Y) b, double cx, double cy, double radius)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-18)
        {
            return false;
        }
        var t = Math.Clamp(((cx - a.X) * dx + (cy - a.Y) * dy) / len2, 0, 1);
        var px = a.X + t * dx - cx;
        var py = a.Y + t * dy - cy;
        return Math.Sqrt(px * px + py * py) < radius - 1e-7;
    }

    // Does the segment pass through the inside of a convex shape (clipping against each edge's half-plane)?
    private static bool Crosses((double X, double Y) a, (double X, double Y) b, IReadOnlyList<Shape> shapes)
    {
        foreach (var shape in shapes)
        {
            var t0 = 0.0;
            var t1 = 1.0;
            var empty = false;
            var c = shape.Corners;
            for (var i = 0; i < c.Count && !empty; i++)
            {
                var p = c[i];
                var q = c[(i + 1) % c.Count];
                // Inside is to the left of each edge of a counter-clockwise outline.
                var ex = q.X - p.X;
                var ey = q.Y - p.Y;
                var length = Math.Sqrt(ex * ex + ey * ey);
                var sa = (ex * (a.Y - p.Y) - ey * (a.X - p.X)) / length;
                var sb = (ex * (b.Y - p.Y) - ey * (b.X - p.X)) / length;
                const double eps = 1e-7;
                if (sa <= eps && sb <= eps)
                {
                    empty = true;
                }
                else if (sa > eps && sb > eps)
                {
                    continue;
                }
                else
                {
                    var t = (eps - sa) / (sb - sa);
                    if (sa <= eps)
                    {
                        t0 = Math.Max(t0, t);
                    }
                    else
                    {
                        t1 = Math.Min(t1, t);
                    }
                    if (t1 - t0 <= 1e-9)
                    {
                        empty = true;
                    }
                }
            }
            if (!empty)
            {
                return true;
            }
        }
        return false;
    }
}
