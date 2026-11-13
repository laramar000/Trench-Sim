namespace Client.Core;

// XCOM-style UX: which terrain hides a model from the camera, so the battlefield can draw it see-through. Display only:
// line of sight and cover always come from the rules' own geometry. A piece counts when the straight line from the
// camera to the middle of a model's body passes through the piece's drawn box; low pieces (under MinHeight) are left
// solid, since they cannot hide a model's body. Coordinates are the battlefield's: x and y across the table in inches,
// z up (heights are the display heights).
public static class SeeThrough
{
    public const double MinHeight = 0.75;

    // A model's point to keep visible: its centre on the table and the height of the middle of its body.
    public readonly record struct Point3(double X, double Y, double Z);

    public static IReadOnlySet<string> Hiding(Point3 camera, IEnumerable<TerrainShape> terrain, IEnumerable<Point3> models)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(models);
        var points = models.ToList();
        var hiding = new HashSet<string>();
        foreach (var piece in terrain.Where(p => p.Height >= MinHeight))
        {
            if (points.Any(p => piece.Parts.Any(part => SegmentHitsBox(camera, p, part, piece.Height))))
            {
                hiding.Add(piece.Id);
            }
        }
        return hiding;
    }

    // The slab test: whether the segment from a to b passes through the box (the table rectangle up to its height),
    // shrunk by a hair so a line that only grazes an edge or a face does not count.
    internal static bool SegmentHitsBox(Point3 a, Point3 b, TableRect area, double height)
    {
        const double hair = 0.01;
        double[] from = [a.X, a.Y, a.Z];
        double[] delta = [b.X - a.X, b.Y - a.Y, b.Z - a.Z];
        double[] min = [area.X0 + hair, area.Y0 + hair, hair];
        double[] max = [area.X1 - hair, area.Y1 - hair, height - hair];
        double enter = 0, leave = 1;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(delta[axis]) < 1e-12)
            {
                if (from[axis] < min[axis] || from[axis] > max[axis])
                {
                    return false;
                }
                continue;
            }
            var t1 = (min[axis] - from[axis]) / delta[axis];
            var t2 = (max[axis] - from[axis]) / delta[axis];
            enter = Math.Max(enter, Math.Min(t1, t2));
            leave = Math.Min(leave, Math.Max(t1, t2));
            if (enter > leave)
            {
                return false;
            }
        }
        return true;
    }
}
