using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Rules.Tests.Geometry;

// Towers proposal, stage T1: the tower data, where a model stands on it, and measuring between models at different
// heights. Nothing here moves a model; ladders in movement are stage T2.
public class TowerTests
{
    private static Footprint Rect(string x1, string y1, string x2, string y2) =>
        Footprint.Of(new Rect(new Point2(Length.Parse(x1), Length.Parse(y1)), new Point2(Length.Parse(x2), Length.Parse(y2))));

    private static Point2 P(string x, string y) => new(Length.Parse(x), Length.Parse(y));

    private static TerrainBlock Platform(Climb? climb, TerrainKind kind = TerrainKind.Open) =>
        new("t", Rect("10", "10", "13", "13"), TerrainProfile.Wall, kind, SurfaceLevel.High, walkable: true) { Climb = climb };

    private static Climb TwoInches(params Ladder[] ladders) => new(Length.FromInches(2), ladders);

    [Fact]
    public void TheTowerLayoutHasTwoTowersWithLaddersOnOppositeSides()
    {
        var plain = ClaimNoMansLand.Create();
        var scenario = ClaimNoMansLand.CreateWithTowers();
        var towers = scenario.Battlefield.Terrain.Where(t => t.IsTower).ToList();
        Assert.Equal(["tower-n", "tower-s"], towers.Select(t => t.Id));
        // The rest of the layout is the plain one, piece for piece, with the trenches of stage S3 in the zones.
        Assert.Equal(plain.Battlefield.Terrain, scenario.Battlefield.Terrain.Where(t => !t.IsTower && !t.IsTrench));
        Assert.Equal(6, scenario.Battlefield.Terrain.Count(t => t.IsTrench));
        Assert.All(scenario.Battlefield.Terrain.Where(t => t.IsTrench), t => Assert.Contains(scenario.Battlefield.Zones, z => t.Footprint.IsWithin(z.Area)));
        Assert.Equal(plain.ObjectiveIds.ToArray(), scenario.ObjectiveIds.ToArray());
        foreach (var tower in towers)
        {
            Assert.True(tower.IsPlatform);
            Assert.Equal(Length.FromInches(2), tower.Climb!.Height);
            Assert.Equal([Side.West, Side.East], tower.Climb.Ladders.Select(l => Climb.SideOf(tower.Footprint.Bounds, l.At)!.Value).Order());
            // Well clear (more than 1.5") of every other piece and zone, so no ladder foot is crowded and no model on a
            // tower is near an Objective.
            foreach (var other in scenario.Battlefield.Terrain.Where(t => t.Id != tower.Id).Select(t => t.Footprint.Bounds)
                         .Concat(scenario.Battlefield.Zones.Select(z => z.Area.Bounds)))
            {
                Assert.True(Gap(tower.Footprint.Bounds, other) > 1500, $"{tower.Id} is {Gap(tower.Footprint.Bounds, other)} thousandths from {other}");
            }
        }
        // Mirrored across the table's east-west centre line.
        var south = towers.Single(t => t.Id == "tower-s").Footprint.Bounds;
        var north = towers.Single(t => t.Id == "tower-n").Footprint.Bounds;
        Assert.Equal(scenario.Battlefield.Height.Thousandths - south.Max.Y.Thousandths, north.Min.Y.Thousandths);
        Assert.Equal(south.Min.X, north.Min.X);
        Assert.Equal(scenario.Battlefield.Width.Thousandths - south.Max.X.Thousandths, south.Min.X.Thousandths);
    }

    private static long Gap(Rect a, Rect b)
    {
        var dx = Math.Max(0, Math.Max(a.Min.X.Thousandths - b.Max.X.Thousandths, b.Min.X.Thousandths - a.Max.X.Thousandths));
        var dy = Math.Max(0, Math.Max(a.Min.Y.Thousandths - b.Max.Y.Thousandths, b.Min.Y.Thousandths - a.Max.Y.Thousandths));
        return (long)Math.Sqrt(dx * dx + dy * dy);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.5")]
    [InlineData("3")]
    [InlineData("3.5")]
    public void HeightsOfOneInchOrLessOrThreeOrMoreAreRefused(string height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Climb(Length.Parse(height), [new Ladder("l", P("10", "11"))]));

    [Theory]
    [InlineData("1.001")]
    [InlineData("2")]
    [InlineData("2.999")]
    public void HeightsBetweenAreAccepted(string height) =>
        Assert.Equal(Length.Parse(height), new Climb(Length.Parse(height), [new Ladder("l", P("10", "11"))]).Height);

    [Fact]
    public void ATowerNeedsUniqueLaddersOnItsSides()
    {
        Assert.Throws<ArgumentException>(() => TwoInches());
        Assert.Throws<ArgumentException>(() => TwoInches(new Ladder("l", P("10", "11")), new Ladder("l", P("13", "11"))));
        // At a corner, inside, outside: refused when the piece is built.
        foreach (var at in new[] { P("10", "10"), P("13", "13"), P("11", "11"), P("9", "11"), P("10", "14") })
        {
            Assert.Throws<ArgumentException>(() => Platform(TwoInches(new Ladder("l", at))));
        }
        var tower = Platform(TwoInches(new Ladder("w", P("10", "11.5")), new Ladder("n", P("11", "13")), new Ladder("s", P("12", "10")), new Ladder("e", P("13", "12"))));
        Assert.True(tower.IsTower);
        Assert.Equal(Side.West, Climb.SideOf(tower.Footprint.Bounds, P("10", "11.5")));
        Assert.Equal(Side.North, Climb.SideOf(tower.Footprint.Bounds, P("11", "13")));
        Assert.Equal(Side.South, Climb.SideOf(tower.Footprint.Bounds, P("12", "10")));
        Assert.Equal(Side.East, Climb.SideOf(tower.Footprint.Bounds, P("13", "12")));
    }

    [Fact]
    public void OnlyAnOpenWalkablePlatformCanBeATower()
    {
        var climb = TwoInches(new Ladder("l", P("10", "11")));
        Assert.Throws<ArgumentException>(() => new TerrainBlock("c", Rect("10", "10", "13", "13"), TerrainProfile.Cover) { Climb = climb });
        Assert.Throws<ArgumentException>(() => new TerrainBlock("w", Rect("10", "10", "13", "13"), TerrainProfile.Wall) { Climb = climb });
        Assert.Throws<ArgumentException>(() => new TerrainBlock("h", Rect("10", "10", "13", "13"), TerrainProfile.Wall, surface: SurfaceLevel.High, walkable: false) { Climb = climb });
        Assert.Throws<ArgumentException>(() => Platform(climb, TerrainKind.Difficult));
        // A platform without a climb is not a tower and behaves as before.
        Assert.False(Platform(null).IsTower);
        Assert.True(Platform(null).IsPlatform);
    }

    [Theory]
    [InlineData("10", "11.5")]
    [InlineData("13", "11.5")]
    [InlineData("11.5", "10")]
    [InlineData("11.5", "13")]
    public void TheFootTouchesTheSideFromOutsideAndTheTopFromInside(string x, string y)
    {
        var tower = Platform(TwoInches(new Ladder("l", P(x, y))));
        var rect = tower.Footprint.Bounds;
        var ladder = tower.Climb!.Ladders[0];
        foreach (var radius in new[] { Length.Parse("0.5"), Length.Parse("0.63"), Length.Parse("0.787") })
        {
            var foot = Climb.Foot(rect, ladder, radius);
            var top = Climb.Top(rect, ladder, radius);
            Assert.True(Shapes.CircleTouchesRect(foot, radius, rect));
            Assert.False(Shapes.CircleOverlapsRect(foot, radius, rect));
            Assert.True(Shapes.CircleInsideRect(top, radius, rect));
            Assert.Equal(radius * 2, Length.FromThousandths((long)Math.Round(Math.Sqrt(ExactMath.DistanceSquared(foot, top)))));
            var field = new Battlefield(Length.FromInches(36), Length.FromInches(36), [tower], []);
            Assert.Equal(Length.Zero, Towers.ElevationAt(field, foot));
            Assert.Equal(Length.FromInches(2), Towers.ElevationAt(field, top));
            Assert.Equal(Length.Zero, Towers.ElevationAt(field, ladder.At));
        }
    }

    [Fact]
    public void ABodyOnATowerStandsAtItsHeight()
    {
        var setup = ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;
        var towers = ClaimNoMansLand.CreateTowersSetup(setup.Models);
        var state = Reducer.Apply(MatchState.None, new MatchCreated(towers));
        var model = state.Models[0];
        Assert.Equal(Length.FromInches(2), Board.BodyAt(state, model, P("18", "6.5"))!.Elevation);
        Assert.Equal(Length.Zero, Board.BodyAt(state, model, P("18", "12.5"))!.Elevation);
        // The plain layout has no towers: everything is on the ground, as before.
        var plain = Reducer.Apply(MatchState.None, new MatchCreated(setup));
        Assert.Equal(Length.Zero, Board.BodyAt(plain, plain.Models[0], P("18", "6.5"))!.Elevation);
    }

    [Fact]
    public void TheHashNamesATowerAndIsUnchangedWithoutOne()
    {
        var setup = ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;
        var plain = StateHash.Canonical(Reducer.Apply(MatchState.None, new MatchCreated(setup)));
        var towers = StateHash.Canonical(Reducer.Apply(MatchState.None, new MatchCreated(ClaimNoMansLand.CreateTowersSetup(setup.Models))));
        Assert.DoesNotContain("climb=", plain, StringComparison.Ordinal);
        Assert.Contains("climb=tower-n,height=2000,tower-n-east@19500/29500;tower-n-west@16500/29500", towers, StringComparison.Ordinal);
        Assert.Contains("climb=tower-s,height=2000,tower-s-east@19500/6500;tower-s-west@16500/6500", towers, StringComparison.Ordinal);
    }
}

// Measuring between bases at different heights (U70): the nearest points of two flat discs. Same height is exactly the
// old flat measure.
public class Measure3DTests
{
    private static ModelBody Body(string id, long x, long y, long r, long z = 0) =>
        new(id, new Point2(Length.FromThousandths(x), Length.FromThousandths(y)), Length.FromThousandths(r), Length.FromThousandths(z));

    [Fact]
    public void AtTheSameHeightNothingChanges()
    {
        var random = new Random(7);
        for (var i = 0; i < 20_000; i++)
        {
            var z = random.Next(2) * 2000;
            var a = Body("a", random.Next(0, 36_000), random.Next(0, 36_000), random.Next(400, 1000), z);
            var b = Body("b", random.Next(0, 36_000), random.Next(0, 36_000), random.Next(400, 1000), z);
            var d = Length.FromThousandths(random.Next(0, 30_000));
            Assert.Equal(ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius + d, strict: false), Measure.IsWithin(a, b, d));
            Assert.Equal(ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius, strict: true), Measure.Overlap(a, b));
            Assert.Equal(ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius, strict: false), Measure.InContact(a, b));
        }
    }

    [Fact]
    public void AcrossHeightsItAgreesWithAnIndependentReference()
    {
        var random = new Random(11);
        var checkedCases = 0;
        for (var i = 0; i < 50_000; i++)
        {
            var a = Body("a", random.Next(10_000, 14_000), random.Next(10_000, 14_000), random.Next(400, 1000), 0);
            var b = Body("b", random.Next(8_000, 16_000), random.Next(8_000, 16_000), random.Next(400, 1000), random.Next(1, 3001));
            var d = Length.FromThousandths(random.Next(0, 4_000));
            // Nearest points of two horizontal discs: horizontal gap between the discs, then the height difference.
            var centres = Math.Sqrt((double)ExactMath.DistanceSquared(a.Centre, b.Centre));
            var flat = Math.Max(0, centres - a.Radius.Thousandths - b.Radius.Thousandths);
            var dz = (double)(b.Elevation.Thousandths - a.Elevation.Thousandths);
            var distance = Math.Sqrt(flat * flat + dz * dz);
            if (Math.Abs(distance - d.Thousandths) < 0.01)
            {
                continue;
            }
            checkedCases++;
            Assert.Equal(distance <= d.Thousandths, Measure.IsWithin(a, b, d));
            Assert.Equal(distance <= d.Thousandths, Measure.IsWithin(b, a, d));
            Assert.False(Measure.Overlap(a, b));
            Assert.False(Measure.InContact(a, b));
            Assert.InRange(Measure.Gap(a, b).Thousandths, (long)Math.Ceiling(distance - 0.001), (long)Math.Ceiling(distance) + 2);
        }
        Assert.True(checkedCases > 45_000);
    }

    [Fact]
    public void ExactBoundariesAcrossHeights()
    {
        // Radii 0.5" each; 0.6" apart in height; within 1" when the horizontal gap is at most 0.8" (a 3-4-5 triangle).
        var a = Body("a", 0, 0, 500);
        Assert.True(Measure.IsWithin(a, Body("b", 1_800, 0, 500, 600), Length.Inch));
        Assert.False(Measure.IsWithin(a, Body("b", 1_801, 0, 500, 600), Length.Inch));
        // Directly above: within exactly the height difference, not less.
        Assert.True(Measure.IsWithin(a, Body("b", 0, 0, 500, 2_000), Length.FromInches(2)));
        Assert.False(Measure.IsWithin(a, Body("b", 0, 0, 500, 2_000), Length.FromThousandths(1_999)));
        // A model on a 2" tower is never within 1" of one on the ground: no melee across heights.
        Assert.False(Measure.IsWithin(a, Body("b", 1_000, 0, 500, 2_000), Length.Inch));
        Assert.Equal(Length.FromInches(2), Measure.Gap(a, Body("b", 1_000, 0, 500, 2_000)));
        // Range across heights is measured the same way (p. 43): 24" horizontally and 2" up is just over 24".
        var far = Body("b", 25_000, 0, 500, 2_000);
        Assert.False(Measure.IsWithin(a, far, Length.FromInches(24)));
        Assert.True(Measure.IsWithin(a, far, Length.FromThousandths(24_084)));
        Assert.False(Measure.IsWithin(a, far, Length.FromThousandths(24_083)));
    }

    [Fact]
    public void ABaseCannotStandBelowTheGround() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Body("a", 0, 0, 500, -1));
}
