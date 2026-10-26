using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Known limits of the nine-point sight (U77), pinned so any change to them is deliberate.
// Line-of-sight review 2026-10-25 (Stage 1, tests only) recorded them; stage 2b-2 (2026-10-26, U95) made the gap search
// part of the rules' sight, so a slit between the columns is now found (the grids below are still the nine rays').
// The seam leak stays (U94 refuses such maps instead).
public class SightLimitTests
{
    // The shooter's eye and the target used throughout: a 1" base at (10, 12) seen from (2, 12), so the three columns
    // reach the target at y 11.6, 12 and 12.4 (0.8 of the radius either side), and the rays are 0.4" apart there.
    private static readonly ModelBody Shooter = Body("a", "2", "12");
    private static readonly ModelBody Target = Body("t", "10", "12");

    // The nine rays (legs, body, head; left to right), before any extra ray of the gap search (U95).
    private static string Grid(SightResult r) =>
        string.Join("/", r.Rays.Take(9).Chunk(3).Select(row => new string(row.Select(ray => ray.Obstructing.Count == 0 ? 'o' : 'x').ToArray())));

    // A ray from the shooter's eye to a point of the target model, tested against every piece with the same exact box test
    // the sight code uses (Footprint.SegmentCrossesBoxInside). Unblocked means that part of the model can be seen.
    private static bool PartSeen(Battlefield field, Point2 at, Length height) =>
        !field.Terrain.Any(t => t.Footprint.SegmentCrossesBoxInside(Shooter.Centre, SightHeights.Eye, at, height, Length.Zero, SightHeights.TopOf(t)));

    [Fact]
    [Trait("Rule", "U95: a slit narrower than the ray spacing is found by the gap search (the nine rays alone missed it, U77)")]
    public void ASlitBetweenTheColumnsIsFoundByTheGapSearch()
    {
        // Two 3" walls 0.1" in front of the target's base edge (x 8.9 to 9), leaving a 0.1" slit at y 12.15 to 12.25.
        var field = Field("24", Wall("south", "8.9", "0", "9", "12.15"), Wall("north", "8.9", "12.25", "9", "24"));

        // The nine points: the middle column (y 12) is behind the south wall and the left column (y 12.4) behind the north
        // wall; the slit lies between them, so every one of the nine rays is blocked. Before U95 the target could not be
        // attacked; now the gap search adds rays between the columns, one of them through the slit, and it is partly seen.
        var sight = HeightSight.Between(field, Shooter, Target);
        Assert.Equal("xxx/xxx/xxx", Grid(sight));
        Assert.Equal(Visibility.Partial, sight.Visibility);
        Assert.True(sight.Rays.Count > 9);
        var seen = sight.Rays.Skip(9).First(r => r.Obstructing.Count == 0);
        Assert.InRange(seen.To.Y.Thousandths, 12_150, 12_300);
        Assert.True(PartSeen(field, seen.To, seen.ToHeight));

        // Yet part of the model shows through the slit: the point (10, 12.2), 0.7" up, is inside the model (0.2" from its
        // centre, under its 1.2" height), and the ray to it passes the walls at y 12.1725 to 12.175, inside the slit.
        var through = P("10", "12.2");
        Assert.True(PartSeen(field, through, SightHeights.Points[1]));
        Assert.True(PartSeen(field, through, SightHeights.Points[0]));
        Assert.True(PartSeen(field, through, SightHeights.Points[2]));
        // The same check agrees with the sight code on its own points: the middle-column body ray is blocked.
        Assert.False(PartSeen(field, Target.Centre, SightHeights.Points[1]));
    }

    [Theory]
    [Trait("Rule", "U77, U95: the same walls with wider slits")]
    [InlineData("12.15", "12.25", "xxx/xxx/xxx")] // 0.1": between the columns, found by the gap search (above)
    [InlineData("12.10", "12.30", "xxx/xxx/xxx")] // 0.2": still between the columns, found the same way
    [InlineData("12.05", "12.35", "oxx/oxx/oxx")] // 0.3": the left column (12.4 at the target, 12.345 at the walls) passes
    [InlineData("11.95", "12.05", "xox/xox/xox")] // 0.1" on the line: the middle column passes
    public void SlitWidthsInFrontOfTheTarget(string slitFrom, string slitTo, string grid)
    {
        var field = Field("24", Wall("south", "8.9", "0", "9", slitFrom), Wall("north", "8.9", slitTo, "9", "24"));
        var sight = HeightSight.Between(field, Shooter, Target);
        Assert.Equal(grid, Grid(sight));
        // Every one of these slits shows part of the model: seen by a column, or by the gap search between them.
        Assert.Equal(Visibility.Partial, sight.Visibility);
    }

    [Fact]
    [Trait("Rule", "KNOWN DEFECT (review 2026-10-25): sight leaks along the seam of two separate touching walls; map files refuse such pieces (U94)")]
    public void TwoSeparateWallsPlacedEdgeToEdgeLeakSightAlongTheirSeam()
    {
        // Two separate 3" walls meeting along y = 12, exactly on the line from the shooter to the target. Physically one
        // solid wall: nothing should be seen. Each wall only grazes the middle rays (U13), so neither blocks them.
        var separate = Field("24", Wall("south", "6", "0", "7", "12"), Wall("north", "6", "12", "7", "24"));
        var leak = HeightSight.Between(separate, Shooter, Target);
        Assert.Equal(Visibility.Partial, leak.Visibility);
        Assert.Equal("xox/xox/xox", Grid(leak));
        Assert.All(leak.Rays.Where((_, i) => i % 3 == 1), r => Assert.Empty(r.Obstructing));
        // The leak counts as Line of Sight for the rules: a target behind it could be shot (with no cover).
        Assert.False(Cover.Of(separate, Shooter, Target).InCover);

        // The same wall as one piece of two joined parts: the seam is inside the shape, and nothing is seen.
        var joined = Field("24", new TerrainBlock("wall", Footprint.Join([R("6", "0", "7", "12"), R("6", "12", "7", "24")]), TerrainProfile.Wall));
        Assert.Equal(Visibility.None, HeightSight.Between(joined, Shooter, Target).Visibility);

        // The same two separate walls with the seam off the line (y 12.3): no ray runs along it, nothing is seen.
        var offLine = Field("24", Wall("south", "6", "0", "7", "12.3"), Wall("north", "6", "12.3", "7", "24"));
        Assert.Equal(Visibility.None, HeightSight.Between(offLine, Shooter, Target).Visibility);
    }

    [Fact]
    [Trait("Rule", "U13: grazing a face, edge or corner does not block")]
    public void GrazingAFaceAnEdgeOrACornerDoesNotBlock()
    {
        // A wall whose north face lies exactly on the line: the middle rays run along the face and are seen; the left
        // column (north of the line) is clear and the right column (south) is behind the wall.
        var face = HeightSight.Between(Field("24", Wall("w", "6", "0", "7", "12")), Shooter, Target);
        Assert.Equal("oox/oox/oox", Grid(face));
        // A wall corner exactly on the middle ray: grazed, not crossed.
        var corner = HeightSight.Between(Field("24", Wall("w", "6", "10", "7", "12")), Shooter, Target);
        Assert.All(corner.Rays.Where((_, i) => i % 3 == 1), r => Assert.Empty(r.Obstructing));
    }

    [Fact]
    public void TheLimitsGiveTheSameAnswerInAnyOrderAndEveryRun()
    {
        var pieces = new[] { Wall("south", "8.9", "0", "9", "12.15"), Wall("north", "8.9", "12.25", "9", "24"), Wall("s2", "6", "0", "7", "12"), Wall("n2", "6", "12", "7", "24") };
        var first = HeightSight.Between(Field("24", pieces), Shooter, Target);
        for (var i = 0; i < 5; i++)
        {
            var again = HeightSight.Between(Field("24", [.. pieces.Reverse()]), Shooter, Target);
            Assert.Equal(Grid(first), Grid(again));
            Assert.Equal(first.Rays.Select(r => string.Join(",", r.Obstructing)), again.Rays.Select(r => string.Join(",", r.Obstructing)));
        }
    }
}
