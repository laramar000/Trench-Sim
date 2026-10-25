using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Line-of-sight plan, stage L1: the height-based sight calculation (U77), tested as pure geometry. The ten ruled
// scenes (GoldenSceneTests) check it through the rules since stage L2.
// Test numbers are the plan's twelve tests (7 waits for trenches, 10 and 11 for stage L2).
public class HeightSightTests
{
    private static TerrainBlock Tower(string id, string x1, string y1, string x2, string y2, string height = "2") =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L(height), [new Ladder($"{id}-w", new Point2(L(x1), Length.FromThousandths((L(y1).Thousandths + L(y2).Thousandths) / 2)))]),
        };

    // Which of the nine points are seen, as three rows (legs, body, head), each left to right: 'o' seen, 'x' hidden.
    private static string Grid(SightResult r) =>
        string.Join("/", r.Rays.Chunk(3).Select(row => new string(row.Select(ray => ray.Obstructing.Count == 0 ? 'o' : 'x').ToArray())));

    [Fact]
    public void Test1_OpenGroundIsClearWithAllNinePoints()
    {
        var r = HeightSight.Between(Field(), Body("a", "4", "12"), Body("t", "10", "12"));
        Assert.Equal(Visibility.Full, r.Visibility);
        Assert.Equal((9, 9), (r.Visible, r.Total));
        Assert.All(r.Rays, ray => Assert.Equal(L("1.1"), ray.FromHeight));
        Assert.Equal([L("0.3"), L("0.3"), L("0.3"), L("0.7"), L("0.7"), L("0.7"), L("1.1"), L("1.1"), L("1.1")], r.Rays.Select(ray => ray.ToHeight));
    }

    [Fact]
    public void TheSidePointsSitAcrossTheLineAtFourFifthsOfTheRadius()
    {
        var r = HeightSight.Between(Field(), Body("a", "4", "12"), Body("t", "10", "12"));
        // Looking along +x, left is +y.
        Assert.Equal([P("10", "12.4"), P("10", "12"), P("10", "11.6")], r.Rays.Take(3).Select(ray => ray.To));
        var diagonal = HeightSight.Between(Field(), Body("a", "2", "2"), Body("t", "5", "6"));
        // Direction (3, 4) of length 5: left is (-4, 3) / 5 * 0.4 = (-0.32, 0.24).
        Assert.Equal([P("4.68", "6.24"), P("5", "6"), P("5.32", "5.76")], diagonal.Rays.Take(3).Select(ray => ray.To));
    }

    [Fact]
    public void Test2_AWallTallerThanTheFigureFullyBetweenBlocks()
    {
        var r = HeightSight.Between(Field("24", Wall("wall", "6", "8", "7", "16")), Body("a", "3", "12"), Body("t", "10", "12"));
        Assert.Equal(Visibility.None, r.Visibility);
        Assert.Equal((0, 9), (r.Visible, r.Total));
        Assert.All(r.Rays, ray => Assert.Equal("wall", ray.BlockedBy));
    }

    [Fact]
    public void Test3_ATargetAtSandbagsShowsItsHeadOnly()
    {
        // 0.75" sandbags touching the target's base. Legs and body are behind them; the head shows over the top.
        var field = Field("24", Barricade("sandbags", "8", "10", "8.5", "14"));
        var r = HeightSight.Between(field, Body("a", "2", "12"), Body("t", "9", "12"));
        Assert.Equal(Visibility.Partial, r.Visibility);
        Assert.Equal("xxx/xxx/ooo", Grid(r));
        Assert.All(r.Rays.Take(6), ray => Assert.Equal("sandbags", ray.BlockedBy));
    }

    [Fact]
    public void Test4_BehindABuildingIsBlockedAndAtItsCornerPartial()
    {
        var field = Field("24", Wall("building", "6", "10", "9", "14"));
        var behind = HeightSight.Between(field, Body("a", "2", "12"), Body("t", "11", "12"));
        Assert.Equal(Visibility.None, behind.Visibility);

        // Past the north-west corner: only the left column clears the corner.
        var corner = HeightSight.Between(field, Body("a", "2", "12"), Body("t", "11", "16.3"));
        Assert.Equal(Visibility.Partial, corner.Visibility);
        Assert.Equal("oxx/oxx/oxx", Grid(corner));
    }

    [Theory]
    [InlineData("11", "13", "ooo/ooo/ooo")]        // a 2" gap: every point is seen
    [InlineData("11.95", "12.05", "xox/xox/xox")]  // a 0.1" gap on the line: the middle column is seen
    [InlineData("12.6", "12.7", "xxx/xxx/xxx")]    // a 0.1" slit off the line: no point is seen (the documented limit)
    public void Test5_NarrowGapsBetweenTwoWalls(string gapFrom, string gapTo, string grid)
    {
        var field = Field("24", Wall("south", "6", "0", "7", gapFrom), Wall("north", "6", gapTo, "7", "24"));
        var r = HeightSight.Between(field, Body("a", "2", "12"), Body("t", "10", "12"));
        Assert.Equal(grid, Grid(r));
    }

    [Fact]
    public void Test6_ATwoInchTowerSeesMoreOverLowCoverThanTheGround()
    {
        var tower = Tower("tower", "2", "10", "5", "13");
        var field = Field("24", tower, Barricade("sandbags", "10", "9", "10.5", "14"));
        var target = Body("t", "12", "11.5");
        var ground = HeightSight.Between(field, Body("a", "6", "11.5"), target);
        var high = HeightSight.Between(field, new ModelBody("a", P("3.5", "11.5"), L("0.5"), L("2")), target);
        Assert.Equal("xxx/ooo/ooo", Grid(ground));
        Assert.Equal("ooo/ooo/ooo", Grid(high));
        Assert.Equal(L("3.1"), high.Rays[0].FromHeight);
        // The elevation fact: 2" up is under the 3" of Elevated position (p. 43), so no +1 DICE (stage L3).
        Assert.True(high.Rays[0].FromHeight - SightHeights.Eye - (ground.Rays[0].ToHeight - SightHeights.Points[0]) < L("3"));
    }

    [Fact]
    public void ATowerNoLongerSeesOverEveryGroundWall()
    {
        // Today's U20 shortcut lets a model on high ground see over any ground Wall. With heights, a 3" wall right in
        // front of the target still hides it from a 2" tower.
        var field = Field("24", Tower("tower", "2", "10", "5", "13"), Wall("wall", "10", "8", "10.5", "15"));
        var target = Body("t", "11", "11.5");
        var shooter = new ModelBody("a", P("3.5", "11.5"), L("0.5"), L("2"));
        Assert.Equal(Visibility.Full, LineOfSight.Between(field, shooter, target).Visibility);
        Assert.Equal(Visibility.None, HeightSight.Between(field, shooter, target).Visibility);
    }

    [Fact]
    public void Test8_TwoObstaclesHideDifferentPointsAndTheHiddenPointsAddUp()
    {
        // Sandbags hide the legs; a post hides the left column.
        var field = Field("24", Barricade("sandbags", "8", "6", "8.5", "18"), Wall("post", "6", "12.15", "6.5", "13"));
        var r = HeightSight.Between(field, Body("a", "2", "12"), Body("t", "10", "12"));
        Assert.Equal("xxx/xoo/xoo", Grid(r));
        Assert.Equal(4, r.Visible);
        Assert.Equal(["post", "sandbags"], r.Rays[0].Obstructing.Order(StringComparer.Ordinal));
        Assert.Equal(["sandbags"], r.Rays[1].Obstructing);
        Assert.Equal(["post"], r.Rays[3].Obstructing);
    }

    [Fact]
    public void Test9_RubbleNeverHidesAnything()
    {
        var field = Field("24", Rough("rubble", "5", "10", "9", "14"));
        foreach (var target in new[] { Body("t", "10", "12"), Body("t", "9.5", "12"), Body("t", "7", "12") })
        {
            var r = HeightSight.Between(field, Body("a", "2", "12"), target);
            Assert.Equal(Visibility.Full, r.Visibility);
        }
        Assert.Contains("rubble", HeightSight.Between(field, Body("a", "2", "12"), Body("t", "10", "12")).Rays[4].Crossed);
    }

    [Fact]
    public void ARayExactlyAlongTheTopOfAPieceIsNotBlocked()
    {
        // The head rays run level at 1.1", exactly along the top of a 1.1" tower between, so they only graze it (as U13).
        var field = Field("24", Tower("tower", "5", "10", "8", "13", "1.1"));
        var r = HeightSight.Between(field, Body("a", "2", "11.5"), Body("t", "10", "11.5"));
        Assert.Equal("xxx/xxx/ooo", Grid(r));
    }

    [Fact]
    public void TheTowerAModelStandsOnNeverBlocksItsSight()
    {
        // U19: a model on a tower sees down past its own edge to a target close below.
        var field = Field("24", Tower("tower", "2", "10", "5", "13"));
        var shooter = new ModelBody("a", P("3.5", "11.5"), L("0.5"), L("2"));
        var r = HeightSight.Between(field, shooter, Body("t", "6", "11.5"));
        Assert.Equal(Visibility.Full, r.Visibility);
        // And the other way round.
        Assert.Equal(Visibility.Full, HeightSight.Between(field, Body("t", "6", "11.5"), shooter).Visibility);
    }

    [Fact]
    public void ARaisedTargetIsSeenOverAWallFromFarAndNotFromClose()
    {
        // A target on a 2" tower behind a 0.75" barricade: the barricade is too low to hide it from anywhere.
        // A 3" wall in front of the tower hides its legs from close by, but less from far away.
        var field = Field("36", Tower("tower", "20", "10", "23", "13"), Wall("wall", "18", "8", "18.5", "15"));
        var target = new ModelBody("t", P("21.5", "11.5"), L("0.5"), L("2"));
        var near = HeightSight.Between(field, Body("a", "16", "11.5"), target);
        var far = HeightSight.Between(field, Body("a", "2", "11.5"), target);
        Assert.True(near.Visible <= far.Visible);
        Assert.Equal(L("2.3"), far.Rays[0].ToHeight);
    }

    [Fact]
    public void Test12_TheSameAnswerInAnyOrderAndEveryRun()
    {
        var field = Field("24",
            Barricade("sandbags", "8", "6", "8.5", "18"), Wall("post", "6", "12.15", "6.5", "13"),
            Tower("tower", "14", "2", "17", "5"), Rough("rubble", "3", "18", "7", "22"));
        var bodies = new[]
        {
            Body("a", "2", "12"), Body("b", "10", "12"), Body("c", "20", "20"), Body("d", "5", "20"),
            new ModelBody("e", P("15.5", "3.5"), L("0.5"), L("2")), Body("f", "12", "8", "0.625"),
        };
        string Run(IEnumerable<(ModelBody A, ModelBody T)> pairs) => string.Join("\n", pairs
            .Select(p => (p, r: HeightSight.Between(field, p.A, p.T)))
            .OrderBy(x => x.p.A.Id, StringComparer.Ordinal).ThenBy(x => x.p.T.Id, StringComparer.Ordinal)
            .Select(x => $"{x.p.A.Id}>{x.p.T.Id} {x.r.Visibility} {Grid(x.r)} {string.Join(";", x.r.Rays.Select(ray => $"{ray.To}@{ray.ToHeight}:{string.Join(",", ray.Obstructing)}"))}"));
        var all = bodies.SelectMany(a => bodies.Where(t => t != a).Select(t => (a, t))).ToList();
        var first = Run(all);
        Assert.Equal(first, Run(Enumerable.Reverse(all)));
        Assert.Equal(first, Run(all.OrderBy(p => p.t.Id, StringComparer.Ordinal)));
        // The answers are pinned, so a Debug run and a Release run must give the same text.
        Assert.Equal("2F114802", Hash(first));
    }

    private static string Hash(string text)
    {
        // FNV-1a, 32 bits: stable across runtimes (string.GetHashCode is randomised).
        var h = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
        {
            h = (h ^ b) * 16777619u;
        }
        return h.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
