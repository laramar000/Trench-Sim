using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// Regression and limitation tests added by the M3 audit.
public class SightAuditTests
{
    [Fact]
    [Trait("Rule", "Audit defect: a ground base overlapping a platform corner must not be obstructed by, or take cover from, that platform (U19)")]
    public void AGroundModelAtAPlatformCornerIsNotObstructedByThePlatformItIsPartlyOn()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        var pocket = Body("pocket", "10.1", "10.1");   // centre inside the corner, but 0.5" base has half or less on the platform
        Assert.Equal(ModelLevel.Ground, Elevation.Of(field, pocket).Level);
        var far = Body("far", "4", "4");

        var asTarget = Cover.Of(field, far, pocket);
        Assert.Equal(Visibility.Full, asTarget.Sight.Visibility);
        Assert.False(asTarget.InCover);

        var asAttacker = LineOfSight.Between(field, pocket, far);
        Assert.Equal(Visibility.Full, asAttacker.Visibility);
    }

    [Fact]
    [Trait("Rule", "Audit: the platform exemption needs the centre strictly inside; touching the edge from outside is not standing on it")]
    public void AModelBesideAPlatformStillSeesItAsAnObstruction()
    {
        var field = Field("24", Platform("p", "10", "10", "14", "14"));
        var beside = Body("beside", "9.6", "12");       // base overlaps the platform edge; centre outside, so ground and not exempt
        var behind = Body("behind", "16", "12");
        Assert.Equal(Visibility.None, LineOfSight.Between(field, beside, behind).Visibility);
        Assert.Equal(Visibility.Full, LineOfSight.Between(field, Body("on", "10.1", "12"), behind).Visibility); // more than half on it: high ground, exempt
    }

    [Fact]
    [Trait("Rule", "Known limitation (U16): fixed rays can miss a real sliver of a wider target")]
    public void AGapNarrowerThanTheRaySpacingCanBeMissedByTheDefaultSampler()
    {
        // This tests the older flat sight class (LineOfSight with LatticeSampler, U16), which the rules no longer use
        // since line-of-sight stage L2 (U77: shots, cover and previews go through HeightSight). The same limit in the
        // live nine-point sight is pinned in SightLimitTests.
        var attacker = Body("a", "3", "12");
        var target = Body("t", "10", "12");
        var field = Field("24", Wall("south", "6", "6", "7", "12.2"), Wall("north", "6", "12.3", "7", "18"));

        // Default sampler: centre, west rim, and the two diagonal rim points; all are stopped by the walls.
        Assert.Equal(Visibility.None, LineOfSight.Between(field, attacker, target).Visibility);
        // The north rim point (10, 12.5) is actually visible through the 0.1" slit, but the default sampler never tries it
        // (rim points perpendicular to the line of sight are skipped). A different sampler sees it.
        Assert.Equal(Visibility.Full, LineOfSight.Between(field, attacker, target, new FixedSampler(P("10", "12.5"))).Visibility);
    }

    [Fact]
    [Trait("Rule", "U16: only rim points on the attacker's side are sampled")]
    public void DefaultSamplerUsesTheCentreAndTheFacingSideOnly()
    {
        var points = new LatticeSampler().Sample(Body("a", "3", "12"), Body("t", "10", "12"));
        Assert.Equal(4, points.Count);
        Assert.Contains(P("10", "12"), points);
        Assert.Contains(P("9.5", "12"), points);
        Assert.DoesNotContain(P("10.5", "12"), points);
        Assert.DoesNotContain(P("10", "12.5"), points);
        // Same fixed points every call.
        Assert.Equal(points, new LatticeSampler().Sample(Body("a", "3", "12"), Body("t", "10", "12")));
    }

    [Fact]
    [Trait("Rule", "U5/U15: a model standing inside a Cover piece gets cover from it even when most of it lies behind (documented reading)")]
    public void StandingInsideACoverPieceGivesCover()
    {
        var field = Field("24", Barricade("b", "8", "11", "9.5", "13"));
        Assert.True(Cover.Of(field, Body("a", "3", "12"), Body("t", "8.2", "12")).InCover);
        Assert.False(Cover.Of(field, Body("a", "3", "12"), Body("t", "7.5", "12")).InCover); // touching only, rays stop short of it
    }

    [Fact]
    [Trait("Rule", "Audit: a Wall that blocks every sampled ray is never reported as non-obstructing on any of them")]
    public void AWallBlockingEverythingObstructsEveryRay()
    {
        var field = Field("24", Wall("w", "6", "8", "7", "16"));
        var result = LineOfSight.Between(field, Body("a", "3", "12"), Body("t", "10", "12"));
        Assert.Equal(Visibility.None, result.Visibility);
        Assert.All(result.Rays, r => Assert.Equal(["w"], r.Obstructing));
        Assert.All(result.Rays, r => Assert.Equal(r.Crossed, r.Obstructing));
    }
}
