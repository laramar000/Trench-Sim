using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Line-of-sight stage L8: sight to a point (p. 30): the point is 1 mm across and 1 mm high, and is seen if any part of
// it is (U92).
public class PointSightTests
{
    private static readonly ModelBody Shooter = Body("s", "12", "2");

    [Fact]
    [Trait("Rule", "Points on the battlefield p. 30")]
    public void APointOnOpenGroundIsSeen()
    {
        var ray = HeightSight.ToPoint(Field(), Shooter, P("12", "20"));
        Assert.Empty(ray.Obstructing);
        Assert.Equal(SightHeights.Eye, ray.FromHeight);
        Assert.Equal(L("0.039"), ray.ToHeight);
        Assert.True(HeightSight.SeesPoint(Field(), Shooter, P("12", "20")));
    }

    [Fact]
    public void AWallBetweenHidesIt()
    {
        var field = Field("24", Wall("wall", "4", "10", "20", "11"));
        Assert.Equal(["wall"], HeightSight.ToPoint(field, Shooter, P("12", "20")).Obstructing);
        Assert.False(HeightSight.SeesPoint(field, Shooter, P("12", "20")));
        // On the near side of the wall: seen.
        Assert.True(HeightSight.SeesPoint(field, Shooter, P("12", "8")));
    }

    [Fact]
    public void ALowPieceIsSeenOverButSandbagsHideAPointBehindThem()
    {
        // The ray falls from 1.1" to 0.039" over 18"; at the far side of a piece at y 10-11 it is 0.57" high.
        Assert.True(HeightSight.SeesPoint(Field("24", Rough("rough", "4", "10", "20", "11")), Shooter, P("12", "20")));
        Assert.Equal(["sandbags"], HeightSight.ToPoint(Field("24", Barricade("sandbags", "4", "10", "20", "11")), Shooter, P("12", "20")).Obstructing);
    }

    [Fact]
    public void APointOnAPlatformStandsOnItsTop()
    {
        var field = Field("24", Platform("roof", "8", "14", "16", "18"));
        var ray = HeightSight.ToPoint(field, Shooter, P("12", "16"));
        Assert.Equal(L("3.039"), ray.ToHeight);
        // The platform it is on does not hide it.
        Assert.Empty(ray.Obstructing);
    }

    [Fact]
    public void APointOnATrenchFloorIsHiddenByTheNearEdgeButSeenFromTheRim()
    {
        var field = Field("24", new TerrainBlock("trench", F("4", "10", "20", "12.5"), TerrainProfile.Low) { Depth = L("1") });
        var far = HeightSight.ToPoint(field, Shooter, P("12", "11.25"));
        Assert.Equal(L("-0.961"), far.ToHeight);
        Assert.Equal(["trench"], far.Obstructing);
        Assert.True(HeightSight.SeesPoint(field, Body("r", "12", "9.5"), P("12", "11.25")));
    }
}
