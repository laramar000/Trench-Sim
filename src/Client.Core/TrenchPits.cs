namespace Client.Core;

// Trench-maps stage S3: how trenches are drawn. The ground is solid around a trench and open inside it down to the
// floor (the rules' U83), so the board draws the ground as boxes covering the table without the trenches' parts, and
// each trench part as a floor at its depth; the ground boxes' sides are the trench walls. Display only: the rules decide
// who is in a trench and what it hides.
public static class TrenchPits
{
    // Every part of every trench on the board.
    public static IReadOnlyList<TableRect> Holes(BoardScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return [.. scene.Terrain.Where(t => t.Trench).SelectMany(t => t.Parts)];
    }

    // The deepest trench, for how far down the ground boxes reach (0 with no trench).
    public static double Deepest(BoardScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Terrain.Where(t => t.Trench).Select(t => t.Depth).DefaultIfEmpty(0).Max();
    }

    // The area without the holes, as rectangles that do not overlap: the grid made by every edge, each row's uncovered
    // cells merged into runs. The whole area when there are no holes.
    public static IReadOnlyList<TableRect> Around(TableRect area, IReadOnlyList<TableRect> holes)
    {
        ArgumentNullException.ThrowIfNull(holes);
        var inside = holes.Where(h => h.X0 < area.X1 && area.X0 < h.X1 && h.Y0 < area.Y1 && area.Y0 < h.Y1).ToList();
        if (inside.Count == 0)
        {
            return [area];
        }
        var xs = inside.SelectMany(h => new[] { h.X0, h.X1 }).Append(area.X0).Append(area.X1).Where(x => x >= area.X0 && x <= area.X1).Distinct().Order().ToList();
        var ys = inside.SelectMany(h => new[] { h.Y0, h.Y1 }).Append(area.Y0).Append(area.Y1).Where(y => y >= area.Y0 && y <= area.Y1).Distinct().Order().ToList();
        var cells = new List<TableRect>();
        for (var j = 0; j + 1 < ys.Count; j++)
        {
            double? start = null;
            for (var i = 0; i + 1 < xs.Count; i++)
            {
                var (cx, cy) = ((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
                var open = !inside.Any(h => h.X0 < cx && cx < h.X1 && h.Y0 < cy && cy < h.Y1);
                if (open && start is null)
                {
                    start = xs[i];
                }
                if (!open && start is { } s)
                {
                    cells.Add(new TableRect(s, ys[j], xs[i], ys[j + 1]));
                    start = null;
                }
            }
            if (start is { } last)
            {
                cells.Add(new TableRect(last, ys[j], xs[^1], ys[j + 1]));
            }
        }
        return cells;
    }

    // The trench a base centred at this table point stands in, as the board draws it: the centre inside a trench part.
    // (The rules' test is more than half the base, U81; at rest the model's own Standing comes from the rules.)
    public static TerrainShape? At(BoardScene scene, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Terrain.FirstOrDefault(t => t.Trench && t.Holds(x, y));
    }
}
