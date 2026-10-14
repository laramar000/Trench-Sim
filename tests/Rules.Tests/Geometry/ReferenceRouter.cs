namespace Rules.Tests.Geometry;

// An independent check on the charge planner: the shortest route of a disc's centre to touch a goal disc around
// convex obstacles, found in floating point from finely sampled outlines. It shares no code with Rules.Core (no
// polygon sizes, no integer geometry, no visibility rules), so a route the planner calls shortest can be compared with
// what a brute-force search finds. Floating point is fine here: it is a test oracle, never a rule decision.
internal static class ReferenceRouter
{
    public sealed record Shape(IReadOnlyList<(double X, double Y)> Corners);

    // Corners per quarter turn of a rounded outline. The sampled outline is circumscribed, so it is never smaller than
    // the true one; 40 per quarter is within 0.015% of the true length, 10 per quarter within 0.16%.
    public const int PerQuarter = 40;

    // The forbidden region for a disc's centre around a circle: the circle widened by the mover's radius.
    public static Shape Circle(double cx, double cy, double radius, int perQuarter = PerQuarter)
    {
        var n = perQuarter * 4;
        var big = radius / Math.Cos(Math.PI / n);
        return new Shape([.. Enumerable.Range(0, n).Select(k => (cx + big * Math.Cos(2 * Math.PI * k / n), cy + big * Math.Sin(2 * Math.PI * k / n)))]);
    }

    // The forbidden region around a rectangle: the rectangle with quarter-circle corners of the mover's radius.
    public static Shape RoundedRect(double x1, double y1, double x2, double y2, double radius, int perQuarter = PerQuarter)
    {
        var big = radius / Math.Cos(Math.PI / (perQuarter * 4));
        var points = new List<(double, double)>();
        var corners = new[] { (x2, y2, 0.0), (x1, y2, 90.0), (x1, y1, 180.0), (x2, y1, 270.0) };
        foreach (var (cx, cy, start) in corners)
        {
            for (var k = 0; k <= perQuarter; k++)
            {
                var angle = (start + 90.0 * k / perQuarter) * Math.PI / 180;
                points.Add((cx + big * Math.Cos(angle), cy + big * Math.Sin(angle)));
            }
        }
        return new Shape(points);
    }

    // Length of the shortest route from the start to contact with the target (a goal disc of the given radius), or
    // infinity if none exists. Dijkstra over the start, every outline corner and a ring of goal points.
    public static double ShortestToContact(double sx, double sy, IReadOnlyList<Shape> shapes, double tx, double ty, double goalRadius, int ring = 120) =>
        ShortestToContact(sx, sy, shapes, tx, ty, goalRadius, ring, out _);

    // The same, also returning the corners and end point of the best route found (for failure messages).
    public static double ShortestToContact(double sx, double sy, IReadOnlyList<Shape> shapes, double tx, double ty, double goalRadius, int ring, out List<(double X, double Y)> route)
    {
        var nodes = new List<(double X, double Y)> { (sx, sy) };
        foreach (var shape in shapes)
        {
            nodes.AddRange(shape.Corners);
        }
        var firstGoal = nodes.Count;
        for (var k = 0; k < ring; k++)
        {
            nodes.Add((tx + goalRadius * Math.Cos(2 * Math.PI * k / ring), ty + goalRadius * Math.Sin(2 * Math.PI * k / ring)));
        }

        var dist = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var done = new bool[nodes.Count];
        var prev = new int[nodes.Count];
        Array.Fill(prev, -1);
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
            // A ring point is only an end of the route: nothing is gone through it.
            if (u >= firstGoal)
            {
                continue;
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
                    prev[v] = u;
                }
            }
        }

        var best = double.PositiveInfinity;
        var bestNode = -1;
        (double X, double Y)? radialEnd = null;
        for (var g = firstGoal; g < nodes.Count; g++)
        {
            if (dist[g] < best)
            {
                best = dist[g];
                bestNode = g;
            }
        }
        // The nearest point of a disc seen from a node is straight towards its centre: try that exact end as well as the
        // sampled ring, so the answer does not depend on how finely the ring is sampled.
        for (var u = 0; u < firstGoal; u++)
        {
            if (double.IsPositiveInfinity(dist[u]))
            {
                continue;
            }
            var dx = nodes[u].X - tx;
            var dy = nodes[u].Y - ty;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= goalRadius)
            {
                continue;
            }
            var end = (X: tx + dx / d * goalRadius, Y: ty + dy / d * goalRadius);
            if (!Crosses(nodes[u], end, shapes) && dist[u] + d - goalRadius < best)
            {
                best = dist[u] + d - goalRadius;
                bestNode = u;
                radialEnd = end;
            }
        }
        route = [];
        for (var n = bestNode; n > 0; n = prev[n])
        {
            route.Insert(0, nodes[n]);
        }
        if (radialEnd is { } finish)
        {
            route.Add(finish);
        }
        return best;
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
