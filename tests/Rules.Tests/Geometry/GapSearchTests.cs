using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Line-of-sight stage 2b-1 (approved by Jason 2026-10-26): the gap search, built but not used by the rules. When the
// nine rays see nothing it looks between them for a sideways gap. These tests check it against a brute-force answer: 801
// points along the line between the target's side columns, at the three sample heights, each ray tested with the same
// box test the sight code uses.
public class GapSearchTests
{
    private static readonly ModelBody Shooter = Body("a", "2", "12");
    private static readonly ModelBody Target = Body("t", "10", "12");

    // Any of 801 evenly spaced points from the left column to the right one, at any sample height, unblocked by every
    // piece (no trenches or platforms in these scenes, so the box test is the whole test).
    private static bool BruteForceSeen(Battlefield field, ModelBody shooter, ModelBody target)
    {
        var columns = HeightSight.Sample(shooter, target);
        var (l, r) = (columns[0], columns[2]);
        for (var k = 0; k <= 800; k++)
        {
            var x = l.X.Thousandths + (long)Math.Round((r.X.Thousandths - l.X.Thousandths) * k / 800.0, MidpointRounding.AwayFromZero);
            var y = l.Y.Thousandths + (long)Math.Round((r.Y.Thousandths - l.Y.Thousandths) * k / 800.0, MidpointRounding.AwayFromZero);
            var at = new Point2(Length.FromThousandths(x), Length.FromThousandths(y));
            foreach (var h in SightHeights.Points)
            {
                if (!field.Terrain.Any(t => t.Footprint.SegmentCrossesBoxInside(shooter.Centre, SightHeights.Eye, at, h, Length.Zero, SightHeights.TopOf(t))))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void SeenRayIsOnTheModelAndClear(Battlefield field, ModelBody shooter, ModelBody target, GapSearchResult result)
    {
        var ray = result.FirstSeen!;
        var dx = (double)(ray.To.X.Thousandths - target.Centre.X.Thousandths);
        var dy = (double)(ray.To.Y.Thousandths - target.Centre.Y.Thousandths);
        Assert.True(Math.Sqrt(dx * dx + dy * dy) <= target.Radius.Thousandths, $"{ray.To} is off the model");
        Assert.DoesNotContain(field.Terrain, t => t.Footprint.SegmentCrossesBoxInside(shooter.Centre, ray.FromHeight, ray.To, ray.ToHeight, Length.Zero, SightHeights.TopOf(t)));
    }

    [Fact]
    public void TheSlitTheNineRaysMissIsFound()
    {
        // SightLimitTests: a 0.1" slit at y 12.15 to 12.25, 1" in front of the target, between two columns.
        var field = Field("24", Wall("south", "8.9", "0", "9", "12.15"), Wall("north", "8.9", "12.25", "9", "24"));
        var result = HeightSight.WithGapSearch(field, Shooter, Target);
        Assert.Equal(Visibility.None, result.Nine.Visibility);
        Assert.Equal(Visibility.Partial, result.Visibility);
        SeenRayIsOnTheModelAndClear(field, Shooter, Target, result);
        // Through the slit: the ray passes the walls between y 12.15 and 12.25.
        var y = result.FirstSeen!.To.Y.Thousandths;
        Assert.InRange(y, 12_150, 12_300);
    }

    [Theory]
    // A slit of the given width, its middle at the given y, in a wall 0.1" thick at the given x (shooter at x 2, target
    // at x 10, columns at y 11.6, 12 and 12.4 on the target).
    [InlineData("8.9", "12.2", "0.05")]
    [InlineData("8.9", "12.2", "0.1")]
    [InlineData("8.9", "11.8", "0.2")]
    [InlineData("6", "12.1", "0.05")]
    [InlineData("6", "12.1", "0.02")]
    [InlineData("3", "12.03", "0.01")]
    [InlineData("4", "11.95", "0.04")]
    public void ANarrowSlitIsFoundExactlyWhenPartOfTheModelShowsThroughIt(string x, string middle, string width)
    {
        var half = L(width).Thousandths / 2;
        var m = L(middle).Thousandths;
        var x2 = (L(x).Thousandths + 100) / 1000m;
        var field = Field("24",
            Wall("south", x, "0", x2.ToString(System.Globalization.CultureInfo.InvariantCulture), ((m - half) / 1000m).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Wall("north", x, ((m + half) / 1000m).ToString(System.Globalization.CultureInfo.InvariantCulture), x2.ToString(System.Globalization.CultureInfo.InvariantCulture), "24"));
        var result = HeightSight.WithGapSearch(field, Shooter, Target);
        var truth = BruteForceSeen(field, Shooter, Target);
        Assert.Equal(truth ? Visibility.Partial : Visibility.None, result.Visibility == Visibility.Full ? Visibility.Partial : result.Visibility);
        if (result.FirstSeen is not null)
        {
            SeenRayIsOnTheModelAndClear(field, Shooter, Target, result);
        }
    }

    [Fact]
    public void WhenTheNineRaysSeeAnythingNothingChanges()
    {
        // Open ground, a face grazed along the line, and the seam leak: Full, Partial and Partial, each kept as it is.
        var scenes = new[]
        {
            Field("24"),
            Field("24", Wall("w", "6", "0", "7", "12")),
            Field("24", Wall("south", "6", "0", "7", "12"), Wall("north", "6", "12", "7", "24")),
        };
        foreach (var field in scenes)
        {
            var nine = HeightSight.Between(field, Shooter, Target);
            var result = HeightSight.WithGapSearch(field, Shooter, Target);
            Assert.NotEqual(Visibility.None, nine.Visibility);
            Assert.Equal(nine.Visibility, result.Visibility);
            Assert.Empty(result.Extra);
        }
    }

    [Fact]
    public void AWallWithNoGapStaysUnseen()
    {
        var field = Field("24", Wall("w", "6", "0", "7", "24"));
        var result = HeightSight.WithGapSearch(field, Shooter, Target);
        Assert.Equal(Visibility.None, result.Visibility);
        // No corner or side of the wall falls across the target, so the one strip is the whole width and its middle is
        // the centre column, already tried: no extra ray.
        Assert.Empty(result.Extra);
    }

    [Fact]
    public void RandomScenesAgreeWithTheBruteForceAnswer()
    {
        // 400 scenes of 2 to 6 walls on a 0.05" grid between a shooter and a target at random spots; whenever the nine
        // rays see nothing, the gap search answers exactly as the brute force does.
        var random = new Random(2026_10_26);
        string G(double v) => (Math.Round(v * 20) / 20).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var searched = 0;
        var found = 0;
        for (var scene = 0; scene < 400; scene++)
        {
            var shooter = Body("a", G(2 + random.NextDouble() * 2), G(10 + random.NextDouble() * 4));
            var target = Body("t", G(18 + random.NextDouble() * 2), G(10 + random.NextDouble() * 4));
            var walls = Enumerable.Range(0, random.Next(2, 7)).Select(i =>
            {
                var x = 6 + random.NextDouble() * 10;
                var y = 9 + random.NextDouble() * 6;
                return Wall($"w{i}", G(x), G(y), G(x + 0.05 + random.NextDouble() * 1.5), G(y + 0.05 + random.NextDouble() * 1.5));
            }).ToArray();
            var field = Field("24", walls);
            var result = HeightSight.WithGapSearch(field, shooter, target);
            if (result.Nine.Visibility != Visibility.None)
            {
                Assert.Equal(result.Nine.Visibility, result.Visibility);
                Assert.Empty(result.Extra);
                continue;
            }
            searched++;
            var truth = BruteForceSeen(field, shooter, target);
            Assert.True(truth == (result.Visibility == Visibility.Partial), $"scene {scene}: brute force {truth}, gap search {result.Visibility}");
            if (truth)
            {
                found++;
                SeenRayIsOnTheModelAndClear(field, shooter, target, result);
            }
        }
        // The scenes have to exercise the search: some unseen targets, and some gaps found among them.
        Assert.True(searched >= 20, $"only {searched} scenes had no ray seen");
        Assert.True(found >= 3, $"only {found} gaps found");
    }

    [Fact]
    public void TheSameAnswerInAnyOrderAndEveryRun()
    {
        var pieces = new[] { Wall("south", "8.9", "0", "9", "12.15"), Wall("north", "8.9", "12.25", "9", "24"), Wall("w", "5", "13", "6", "14") };
        string Key(GapSearchResult r) => $"{r.Visibility}|{string.Join(";", r.Extra.Select(x => $"{x.To}@{x.ToHeight}:{string.Join(",", x.Obstructing)}"))}";
        var first = Key(HeightSight.WithGapSearch(Field("24", pieces), Shooter, Target));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(first, Key(HeightSight.WithGapSearch(Field("24", [.. Shuffled(pieces, i)]), Shooter, Target)));
        }
    }

    [Fact]
    public void ATargetDeepInATrenchAgainstTheNearWallStaysUnseen()
    {
        // T1 (open question): at 2.9" deep the near wall hides a target hugging it from an attacker at the rim. There
        // is no sideways gap in solid ground, so the search changes nothing.
        var trench = new TerrainBlock("trench", F("31.5", "14", "34", "22"), TerrainProfile.Low) { Depth = L("2.9") };
        var field = Field("36", trench);
        var result = HeightSight.WithGapSearch(field, Body("a", "31", "18"), Body("t", "32.1", "18"));
        Assert.Equal(Visibility.None, result.Nine.Visibility);
        Assert.Equal(Visibility.None, result.Visibility);
    }
}
