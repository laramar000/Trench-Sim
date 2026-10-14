using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

public class BaseAreaTests
{
    private static readonly Rect Table = R("0", "0", "24", "24");

    [Theory]
    [Trait("Rule", "1.0.2 p.31 more than half of the base on the battlefield (confirmed); exact method U6")]
    [InlineData("0", "12", AreaDecision.HalfOrLess)]       // centre on the edge: exactly half
    [InlineData("0.001", "12", AreaDecision.MoreThanHalf)] // a thousandth in
    [InlineData("-0.001", "12", AreaDecision.HalfOrLess)]
    [InlineData("12", "12", AreaDecision.MoreThanHalf)]
    [InlineData("-0.6", "12", AreaDecision.HalfOrLess)]    // wholly outside
    [InlineData("24", "12", AreaDecision.HalfOrLess)]
    [InlineData("23.999", "12", AreaDecision.MoreThanHalf)]
    public void SingleEdgeIsExact(string x, string y, AreaDecision expected)
    {
        Assert.Equal(expected, BaseArea.MoreThanHalfInside(P(x, y), L("0.5"), Table));
    }

    [Theory]
    [Trait("Rule", "1.0.2 p.31 (confirmed); corner needs area integration, U6")]
    [InlineData("0.17", "0.17", AreaDecision.HalfOrLess)]
    [InlineData("0.172", "0.172", AreaDecision.MoreThanHalf)]
    [InlineData("0.171", "0.171", AreaDecision.TooCloseToCall)] // inside the method's resolution; callers treat it as not more than half
    [InlineData("0.1", "0.48", AreaDecision.MoreThanHalf)]
    [InlineData("0", "0", AreaDecision.HalfOrLess)]
    public void CornerUsesAreaNotTheCentre(string x, string y, AreaDecision expected)
    {
        Assert.Equal(expected, BaseArea.MoreThanHalfInside(P(x, y), L("0.5"), Table));
    }

    [Fact]
    [Trait("Rule", "U6")]
    public void CornerIsSymmetricInAllFourCorners()
    {
        var results = new[]
        {
            BaseArea.MoreThanHalfInside(P("0.2", "0.2"), L("0.5"), Table),
            BaseArea.MoreThanHalfInside(P("23.8", "0.2"), L("0.5"), Table),
            BaseArea.MoreThanHalfInside(P("0.2", "23.8"), L("0.5"), Table),
            BaseArea.MoreThanHalfInside(P("23.8", "23.8"), L("0.5"), Table),
        };
        Assert.All(results, r => Assert.Equal(AreaDecision.MoreThanHalf, r));
    }

    [Fact]
    [Trait("Rule", "U6")]
    public void AgreesWithDoublePrecisionReferenceOnRandomCornerBases()
    {
        // The reference uses floating point in the TEST only, to cross-check the integer method.
        var rng = new Random(2026);
        for (var i = 0; i < 300; i++)
        {
            var cx = rng.Next(-200, 700);
            var cy = rng.Next(-200, 700);
            var r = 500;
            var decision = BaseArea.MoreThanHalfInside(
                new Point2(Length.FromThousandths(cx), Length.FromThousandths(cy)), Length.FromThousandths(r), Table);
            var reference = InsideFraction(cx, cy, r);
            if (decision == AreaDecision.MoreThanHalf)
            {
                Assert.True(reference > 0.5 - 1e-3, $"({cx},{cy}) reference {reference}");
            }
            else if (decision == AreaDecision.HalfOrLess)
            {
                Assert.True(reference < 0.5 + 1e-3, $"({cx},{cy}) reference {reference}");
            }
        }
    }

    private static double InsideFraction(double cx, double cy, double r)
    {
        const int n = 20000;
        var inside = 0.0;
        for (var i = 0; i < n; i++)
        {
            var x = cx - r + (i + 0.5) * 2 * r / n;
            var h = Math.Sqrt(Math.Max(0, r * r - (x - cx) * (x - cx)));
            if (x < 0)
            {
                continue;
            }
            inside += Math.Max(0, Math.Min(cy + h, 1e9) - Math.Max(cy - h, 0)) * 2 * r / n;
        }
        return inside / (Math.PI * r * r);
    }
}

public class TerrainConstructionTests
{
    private static readonly Footprint Box = F("2", "2", "4", "4");

    private static TerrainBlock Make(TerrainProfile profile, TerrainKind kind = TerrainKind.Open, SurfaceLevel surface = SurfaceLevel.Ground, bool? walkable = null) =>
        new("t", Box, profile, kind, surface, walkable);

    [Theory]
    [Trait("Rule", "Revision 3 section 10.2 (terrain combinations); provisional U9, U14")]
    [InlineData(TerrainProfile.Low, SurfaceLevel.Ground, null, true)]
    [InlineData(TerrainProfile.Cover, SurfaceLevel.Ground, null, true)]
    [InlineData(TerrainProfile.Wall, SurfaceLevel.Ground, null, false)]
    [InlineData(TerrainProfile.Wall, SurfaceLevel.High, null, true)]
    [InlineData(TerrainProfile.Wall, SurfaceLevel.High, true, true)]
    [InlineData(TerrainProfile.Wall, SurfaceLevel.High, false, false)]
    [InlineData(TerrainProfile.Wall, SurfaceLevel.Ground, false, false)]
    public void AllowedCombinationsAndTheirWalkability(TerrainProfile profile, SurfaceLevel surface, bool? walkable, bool expectedWalkable)
    {
        var block = Make(profile, TerrainKind.Open, surface, walkable);
        Assert.Equal(expectedWalkable, block.IsWalkable);
        Assert.Equal(surface == SurfaceLevel.High && expectedWalkable, block.IsPlatform);
    }

    [Theory]
    [InlineData(TerrainProfile.Low, SurfaceLevel.High, null)]      // High needs a Wall
    [InlineData(TerrainProfile.Cover, SurfaceLevel.High, null)]
    [InlineData(TerrainProfile.Wall, SurfaceLevel.Ground, true)]   // a ground Wall cannot be walked on
    [InlineData(TerrainProfile.Low, SurfaceLevel.Ground, false)]   // Low/Cover walkable unless Impassable
    [InlineData(TerrainProfile.Cover, SurfaceLevel.Ground, false)]
    public void InvalidCombinationsAreRejected(TerrainProfile profile, SurfaceLevel surface, bool? walkable)
    {
        Assert.Throws<ArgumentException>(() => Make(profile, TerrainKind.Open, surface, walkable));
    }

    [Theory]
    [InlineData(TerrainProfile.Low)]
    [InlineData(TerrainProfile.Cover)]
    [InlineData(TerrainProfile.Wall)]
    public void ImpassableIsNeverWalkable(TerrainProfile profile)
    {
        Assert.False(Make(profile, TerrainKind.Impassable).IsWalkable);
        Assert.False(Make(profile, TerrainKind.Impassable, walkable: false).IsWalkable);
        Assert.Throws<ArgumentException>(() => Make(profile, TerrainKind.Impassable, walkable: true));
    }

    [Fact]
    public void ImpassablePlatformIsNotWalkableSoItIsATower()
    {
        var block = Make(TerrainProfile.Wall, TerrainKind.Impassable, SurfaceLevel.High);
        Assert.False(block.IsWalkable);
        Assert.False(block.IsPlatform);
    }

    [Theory]
    [InlineData(TerrainKind.Difficult)]
    [InlineData(TerrainKind.Dangerous)]
    [InlineData(TerrainKind.Difficult | TerrainKind.Dangerous)]
    public void DifficultAndDangerousNeedWalkableTerrain(TerrainKind kind)
    {
        Assert.Throws<ArgumentException>(() => Make(TerrainProfile.Wall, kind));
        Assert.Throws<ArgumentException>(() => Make(TerrainProfile.Wall, kind, SurfaceLevel.High, walkable: false));
        Assert.Throws<ArgumentException>(() => Make(TerrainProfile.Low, kind | TerrainKind.Impassable));
        Assert.True(Make(TerrainProfile.Low, kind).IsWalkable);
        Assert.True(Make(TerrainProfile.Wall, kind, SurfaceLevel.High).IsWalkable);
    }

    [Fact]
    public void UnknownEnumValuesAndBadIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Make((TerrainProfile)9));
        Assert.Throws<ArgumentException>(() => Make(TerrainProfile.Low, surface: (SurfaceLevel)9));
        Assert.Throws<ArgumentException>(() => Make(TerrainProfile.Low, (TerrainKind)64));
        Assert.ThrowsAny<ArgumentException>(() => new TerrainBlock("", Box, TerrainProfile.Low));
        Assert.Throws<ArgumentNullException>(() => new TerrainBlock("t", null!, TerrainProfile.Low));
    }

    [Fact]
    public void BattlefieldRejectsDuplicatesOffTableAndOverlappingPlatforms()
    {
        Assert.Throws<ArgumentException>(() => Field("24", Barricade("a", "1", "1", "2", "2"), Barricade("a", "3", "3", "4", "4")));
        Assert.Throws<ArgumentException>(() => Field("24", Barricade("a", "23", "1", "25", "2")));
        Assert.Throws<ArgumentException>(() => Field("24", Platform("p", "2", "2", "6", "6"), Platform("q", "5", "5", "8", "8")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Battlefield(L("0"), L("24"), [], []));
        // Touching platforms, platform and tower overlap, and overlapping non-platforms are all fine.
        _ = Field("24", Platform("p", "2", "2", "6", "6"), Platform("q", "6", "2", "8", "6"));
        _ = Field("24", Platform("p", "2", "2", "6", "6"), Tower("t", "5", "5", "8", "8"));
    }

    [Fact]
    public void ValuesBeyondTheLimitAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => P("201", "0"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Body("m", "1", "1", "201"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromThousandths(2_000_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => Body("m", "1", "1", "0"));
    }
}

public class MeasureAndPlacementTests
{
    [Fact]
    [Trait("Rule", "1.0.2 p.30 base-to-base measurement, 'within' includes the exact distance (confirmed)")]
    public void WithinIncludesTheExactDistanceAndNoMore()
    {
        var a = Body("a", "5", "5");
        Assert.True(Measure.IsWithin(a, Body("b", "7", "5"), L("1")));      // gap exactly 1"
        Assert.False(Measure.IsWithin(a, Body("b", "7.001", "5"), L("1"))); // gap 1.001"
        Assert.True(Measure.InContact(a, Body("b", "6", "5")));
        Assert.False(Measure.Overlap(a, Body("b", "6", "5")));              // touching is not overlapping
        Assert.True(Measure.Overlap(a, Body("b", "5.999", "5")));
        Assert.Equal(L("0.001"), Measure.Gap(a, Body("b", "6.001", "5")));
        Assert.Equal(Length.Zero, Measure.Gap(a, Body("b", "5.5", "5")));
    }

    [Theory]
    [Trait("Rule", "1.0.2 p.31 (confirmed)")]
    [InlineData("12", "12", true, null)]
    [InlineData("0", "12", false, PlacementRefusal.OffBattlefield)]
    [InlineData("0.001", "12", true, null)]
    [InlineData("-3", "12", false, PlacementRefusal.OffBattlefield)]
    public void BattlefieldEdgeUsesTheMoreThanHalfRule(string x, string y, bool legal, PlacementRefusal? reason)
    {
        var result = Placement.Check(Field(), Body("m", x, y), []);
        Assert.Equal(legal, result.IsLegal);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.31 (confirmed); U9, U14 for what counts as impassable")]
    public void NonWalkableTerrainBlocksButWalkableTerrainDoesNot()
    {
        var field = Field("24", Wall("w", "10", "10", "12", "12"), Barricade("b", "2", "2", "4", "4"), Platform("p", "16", "2", "20", "6"), Tower("t", "2", "16", "6", "20"));
        Assert.True(Placement.Check(field, Body("m", "9.5", "11"), []).IsLegal);       // touching the wall edge
        var overlap = Placement.Check(field, Body("m", "9.501", "11"), []);
        Assert.Equal(PlacementRefusal.InBlockingTerrain, overlap.Reason);
        Assert.Equal("w", overlap.SubjectId);
        Assert.True(Placement.Check(field, Body("m", "3", "3"), []).IsLegal);          // standing in a barricade
        Assert.True(Placement.Check(field, Body("m", "18", "4"), []).IsLegal);         // on a platform
        Assert.Equal("t", Placement.Check(field, Body("m", "4", "18"), []).SubjectId); // inside a tower
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.31 bases may not overlap (confirmed)")]
    public void BasesMayTouchButNotOverlap()
    {
        var others = new[] { Body("o", "5", "5") };
        Assert.True(Placement.Check(Field(), Body("m", "6", "5"), others).IsLegal);
        var result = Placement.Check(Field(), Body("m", "5.999", "5"), others);
        Assert.Equal(PlacementRefusal.OverlapsBase, result.Reason);
        Assert.Equal("o", result.SubjectId);
        Assert.True(Placement.Check(Field(), Body("o", "5", "5"), others).IsLegal); // a model does not clash with itself
    }

    [Fact]
    [Trait("Rule", "1.0.2 p.146 wholly within the zone (text-extracted); reuses placement")]
    public void DeploymentUsesPlacementThenRequiresTheWholeBaseInTheZone()
    {
        var field = Field("24", [], [new DeploymentZone("zoneA", F("0", "0", "6", "24"))]);
        Assert.True(Placement.Check(field, Body("m", "0.5", "12"), [], "zoneA").IsLegal);
        Assert.Equal(PlacementRefusal.OutsideZone, Placement.Check(field, Body("m", "0.499", "12"), [], "zoneA").Reason);
        Assert.Equal(PlacementRefusal.OutsideZone, Placement.Check(field, Body("m", "6.001", "12"), [], "zoneA").Reason);
        Assert.Equal(PlacementRefusal.UnknownZone, Placement.Check(field, Body("m", "3", "12"), [], "zoneB").Reason);
        Assert.Equal(PlacementRefusal.OverlapsBase, Placement.Check(field, Body("m", "3", "12"), [Body("o", "3.5", "12")], "zoneA").Reason);
    }

    [Fact]
    public void ExtremeCoordinatesDoNotOverflow()
    {
        var field = new Battlefield(L("200"), L("200"), [], []);
        var a = Body("a", "0.5", "0.5");
        var b = Body("b", "199.5", "199.5");
        Assert.False(Measure.IsWithin(a, b, L("1")));
        Assert.True(Measure.IsWithin(a, b, L("281")));
        Assert.Equal(L("280.429"), Measure.Gap(a, b)); // diagonal 281.428... rounded up, minus the two radii
        Assert.True(Placement.Check(field, b, [a]).IsLegal);
        Assert.Equal(AreaDecision.MoreThanHalf, BaseArea.MoreThanHalfInside(P("199.8", "199.8"), L("0.5"), field.Area.Bounds));
    }
}
