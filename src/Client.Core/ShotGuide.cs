namespace Client.Core;

// Line-of-sight stage L5: the guide drawn when a Shoot target is pointed at or chosen. It is a picture of the rules'
// own nine sight rays (the first nine of Preview.Rays; U95's extra rays, when a narrow gap was searched, follow them), labelled "Guide" so it is never mistaken for a rule: the server's sight, cover and
// modifiers are the "Rules" lines beside it.
//  - The shot line is the middle body ray, from the shooter's eye to the target's body point: solid while it is clear,
//    broken from where it enters the piece that blocks it.
//  - The sight points are a 3 x 3 figure (head, body, legs; left to right as the shooter sees the target): light where
//    a ray reaches the point, dark where it is blocked.
public sealed record GuideSegment(TablePoint From, double FromHeight, TablePoint To, double ToHeight, bool Clear);

public static class ShotGuide
{
    public const string Label = "Guide";

    // The rows of the figure, top to bottom, and the ray index each starts at (rays run legs, body, head).
    private static readonly (string Name, int First)[] Rows = [("head", 6), ("body", 3), ("legs", 0)];

    // The shot line in one or two pieces. Empty unless the preview has the nine rays (and maybe the gap search's after them).
    public static IReadOnlyList<GuideSegment> Line(IReadOnlyList<PreviewRay> rays, BoardScene scene)
    {
        ArgumentNullException.ThrowIfNull(rays);
        ArgumentNullException.ThrowIfNull(scene);
        if (rays.Count < 9)
        {
            return [];
        }
        var ray = rays[4];
        if (ray.Clear)
        {
            return [new(ray.From, ray.FromHeight, ray.To, ray.ToHeight, true)];
        }
        var blocker = scene.Terrain.FirstOrDefault(t => t.Id == ray.BlockedBy);
        var t = blocker is null ? 0 : Entry(ray.From, ray.To, blocker.Area);
        var at = new TablePoint(ray.From.X + (ray.To.X - ray.From.X) * t, ray.From.Y + (ray.To.Y - ray.From.Y) * t);
        var height = ray.FromHeight + (ray.ToHeight - ray.FromHeight) * t;
        return t <= 0
            ? [new(ray.From, ray.FromHeight, ray.To, ray.ToHeight, false)]
            : [new(ray.From, ray.FromHeight, at, height, true), new(at, height, ray.To, ray.ToHeight, false)];
    }

    // Where the segment a-b first enters the rectangle, as a fraction of its length (0 when it starts inside or misses).
    private static double Entry(TablePoint a, TablePoint b, TableRect r)
    {
        double lo = 0, hi = 1;
        foreach (var (p, d, min, max) in new[] { (a.X, b.X - a.X, r.X0, r.X1), (a.Y, b.Y - a.Y, r.Y0, r.Y1) })
        {
            if (Math.Abs(d) < 1e-12)
            {
                if (p < min || p > max)
                {
                    return 0;
                }
                continue;
            }
            var t1 = (min - p) / d;
            var t2 = (max - p) / d;
            lo = Math.Max(lo, Math.Min(t1, t2));
            hi = Math.Min(hi, Math.Max(t1, t2));
        }
        return lo <= hi ? lo : 0;
    }

    // The 3 x 3 figure as a 16 x 16 icon mask (SightIcons' format): '#' seen, 'X' hidden, '.' clear. Each point is a
    // 4 x 4 cell with a 1-pixel gap; head on top.
    public static string[] Figure(IReadOnlyList<PreviewRay> rays)
    {
        ArgumentNullException.ThrowIfNull(rays);
        var cells = Enumerable.Range(0, SightIcons.Size).Select(_ => Enumerable.Repeat('.', SightIcons.Size).ToArray()).ToArray();
        if (rays.Count >= 9)
        {
            for (var row = 0; row < 3; row++)
            {
                for (var col = 0; col < 3; col++)
                {
                    var paint = rays[Rows[row].First + col].Clear ? '#' : 'X';
                    for (var y = 0; y < 4; y++)
                    {
                        for (var x = 0; x < 4; x++)
                        {
                            cells[1 + row * 5 + y][1 + col * 5 + x] = paint;
                        }
                    }
                }
            }
        }
        return [.. cells.Select(c => new string(c))];
    }

    // The figure in words, for the panel: "Guide: sight points seen, head 3 of 3, body 1 of 3, legs 0 of 3".
    public static string? Words(IReadOnlyList<PreviewRay> rays)
    {
        ArgumentNullException.ThrowIfNull(rays);
        if (rays.Count < 9)
        {
            return null;
        }
        // U95: when none of the nine points is seen, a clear extra ray means the target shows through a narrow gap.
        var gap = rays.Skip(9).Any(r => r.Clear) ? ", and through a narrow gap" : "";
        return $"{Label}: sight points seen, " + string.Join(", ", Rows.Select(r => $"{r.Name} {Enumerable.Range(r.First, 3).Count(i => rays[i].Clear)} of 3")) + gap;
    }
}
