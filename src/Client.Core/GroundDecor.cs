using static System.FormattableString;

namespace Client.Core;

// Graphics stage G2: flat decorations on open ground (shell craters, broken duckboards). Decoration only: the rules
// never see them, they are lower than GroundDecor.MaxHeight, and they keep clear of every terrain piece and deployment
// zone, so they never look like terrain or cover. Where they go depends only on the battlefield's own layout (its size,
// zones and terrain), so every player, every redraw and every replay of a match shows the same ones.
public sealed record DecalSpot(string Kind, double X, double Y, double Width, double Depth, double TurnDegrees)
{
    // The circle around the decal, used to keep it clear of other things.
    public double Reach => Math.Sqrt(Width * Width + Depth * Depth) / 2;
}

public static class GroundDecor
{
    // The highest any decoration outside a terrain piece may reach (inches).
    public const double MaxHeight = 0.1;

    // How high the decals are drawn: above the ground, below the deployment zones' tint (0.02").
    public const double Lift = 0.012;

    // The gap kept between a decal and any terrain piece, zone or table edge (inches).
    public const double Clearance = 0.4;

    public const string Crater = "crater";
    public const string Duckboard = "duckboard";

    public static IReadOnlyList<DecalSpot> For(BoardScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var random = new Spread(Key(scene));
        var spots = new List<DecalSpot>();
        var craters = (int)Math.Round(scene.Width * scene.Depth / 160);
        var duckboards = (int)Math.Round(scene.Width * scene.Depth / 500);
        for (var tries = 0; tries < 400 && spots.Count < craters + duckboards; tries++)
        {
            var crater = spots.Count(s => s.Kind == Crater) < craters;
            var size = crater ? 1.4 + random.Next() * 1.4 : 3.0 + random.Next() * 1.5;
            var spot = new DecalSpot(
                crater ? Crater : Duckboard,
                random.Next() * scene.Width,
                random.Next() * scene.Depth,
                size,
                crater ? size : 0.8,
                random.Next(0, 360));
            if (Clear(scene, spots, spot))
            {
                spots.Add(spot);
            }
        }
        return spots;
    }

    // Away from the table edge, every terrain piece, every zone and every other decal.
    public static bool Clear(BoardScene scene, IEnumerable<DecalSpot> others, DecalSpot spot)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(spot);
        var r = spot.Reach + Clearance;
        if (spot.X - r < 0 || spot.Y - r < 0 || spot.X + r > scene.Width || spot.Y + r > scene.Depth)
        {
            return false;
        }
        bool Near(TableRect area) =>
            Math.Max(0, Math.Max(area.X0 - spot.X, spot.X - area.X1)) is var dx
            && Math.Max(0, Math.Max(area.Y0 - spot.Y, spot.Y - area.Y1)) is var dy
            && dx * dx + dy * dy < r * r;
        return !scene.Terrain.Any(t => Near(t.Area))
            && !scene.Zones.Any(z => Near(z.Area))
            && !others.Any(o => Math.Pow(o.X - spot.X, 2) + Math.Pow(o.Y - spot.Y, 2) < Math.Pow(o.Reach + spot.Reach, 2));
    }

    // The layout as fixed text (sizes and positions, invariant culture), the key of the spread.
    private static string Key(BoardScene scene) =>
        string.Join(";", new[] { Invariant($"{scene.Width:0.###}x{scene.Depth:0.###}") }
            .Concat(scene.Zones.Select(z => Invariant($"{z.Id}:{z.Area.X0:0.###},{z.Area.Y0:0.###},{z.Area.X1:0.###},{z.Area.Y1:0.###}")))
            .Concat(scene.Terrain.Select(t => Invariant($"{t.Id}:{t.Area.X0:0.###},{t.Area.Y0:0.###},{t.Area.X1:0.###},{t.Area.Y1:0.###}"))));
}
