using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Height-gap plan (2026-10-26, Jason chose "docs only"): every piece is a box from the ground up and solid ground blocks
// from below, and a ray to a higher point on the target is higher all along its length. So at any one spot across the
// target the heights seen always form one band reaching the figure's top (1.2"). If 0.2" is seen, 1.1" is too; a top
// between two sampled heights is never missed. Only a target showing nothing below 1.1" can be missed.
public class HeightBandTests
{
    private static readonly Length FigureTop = L("1.2");

    private static TerrainBlock Tower(string id, Rect r, Length height) =>
        new(id, Footprint.Of(r), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(height, [new Ladder($"{id}-w", new Point2(r.Min.X, Length.FromThousandths((r.Min.Y.Thousandths + r.Max.Y.Thousandths) / 2)))]),
        };

    private static TerrainBlock Trench(string id, Rect r, Length depth) => new(id, Footprint.Of(r), TerrainProfile.Low) { Depth = depth };

    // Seen heights at one spot, from 0 to 1.2" in steps of 0.01": once one is seen, every higher one is.
    private static bool OneTopBand(Battlefield field, ModelBody shooter, ModelBody target, Point2 spot, out string detail)
    {
        var seen = Enumerable.Range(0, 121).Select(k => HeightSight.RayAtHeight(field, shooter, target, spot, Length.FromThousandths(k * 10)).Obstructing.Count == 0).ToArray();
        var first = Array.IndexOf(seen, true);
        detail = new string(seen.Select(s => s ? 'o' : 'x').ToArray());
        return first < 0 || seen.Skip(first).All(s => s);
    }

    [Fact]
    public void AtEverySpotTheSeenHeightsAreOneBandUpToTheTop()
    {
        var random = new Random(2026_10_26);
        string G(double v) => (Math.Round(v * 20) / 20).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Rect Box(double x, double y, double w, double h) => R(G(x), G(y), G(x + w), G(y + h));
        var scenes = 0;
        var partBands = 0;
        for (var scene = 0; scene < 600 && scenes < 300; scene++)
        {
            // A trench across the middle, pieces of every kind, and sometimes a tower for the shooter.
            var depth = L(G(0.5 + random.NextDouble() * 2.4));
            var pieces = new List<TerrainBlock> { Trench("trench", Box(14, 4, 3, 16), depth) };
            var towerTop = random.Next(3) == 0 ? L(G(1.1 + random.NextDouble() * 1.8)) : (Length?)null;
            if (towerTop is { } th)
            {
                pieces.Add(Tower("tower", Box(2, 10, 3, 3), th));
            }
            for (var i = 0; i < random.Next(1, 5); i++)
            {
                var r = Box(7 + random.NextDouble() * 6, 8 + random.NextDouble() * 6, 0.1 + random.NextDouble() * 1.5, 0.1 + random.NextDouble() * 2);
                var profile = (TerrainProfile)new[] { TerrainProfile.Low, TerrainProfile.Cover, TerrainProfile.Wall }[random.Next(3)];
                pieces.Add(new TerrainBlock($"p{i}", Footprint.Of(r), profile));
            }
            Battlefield field;
            try
            {
                field = Field("24", [.. pieces]);
            }
            catch (ArgumentException)
            {
                continue; // overlapping pieces: not a battlefield
            }
            scenes++;
            var shooter = towerTop is { } top ? new ModelBody("a", P("3.5", "11.5"), L("0.5"), top) : Body("a", G(2 + random.NextDouble() * 4), G(9 + random.NextDouble() * 6));
            // The target in the trench, on its rim, or beyond it on open ground.
            var target = Body("t", G(14.6 + random.NextDouble() * 5), G(8 + random.NextDouble() * 8));
            var columns = HeightSight.Sample(shooter, target);
            foreach (var spot in columns)
            {
                Assert.True(OneTopBand(field, shooter, target, spot, out var detail), $"scene {scene} at {spot}: {detail}");
                if (detail.Contains('x') && detail.Contains('o'))
                {
                    partBands++;
                }
            }
        }
        // The scenes must exercise it: many battlefields, and many spots where only the upper part is seen.
        Assert.True(scenes >= 200, $"only {scenes} scenes");
        Assert.True(partBands >= 50, $"only {partBands} spots seen in part");
    }

    [Fact]
    public void OnlyTheTopTenthOfAnInchCanBeMissed()
    {
        // A target on a 1.19" deep trench floor, 0.6" behind the trench's near edge, and a shooter 18" off on open ground:
        // the ray clears the edge only above about 1.15", so at every column nothing at or below 1.1" is seen and the
        // nine rays (and the gap search) see nothing, yet the top of the head at 1.2" is seen.
        var field = Field("36", Trench("trench", R("20", "10", "26", "14"), L("1.19")));
        var shooter = Body("a", "2", "12");
        var target = Body("t", "20.6", "12");
        var spots = HeightSight.Sample(shooter, target);
        Assert.All(spots, s => Assert.NotEmpty(HeightSight.RayAtHeight(field, shooter, target, s, L("1.1")).Obstructing));
        Assert.Contains(spots, s => HeightSight.RayAtHeight(field, shooter, target, s, FigureTop).Obstructing.Count == 0);
        Assert.Equal(Visibility.None, HeightSight.Between(field, shooter, target).Visibility);
    }
}
