using Client.Core;
using Rules.Core.Geometry;

namespace Client.Tests;

// XCOM-style UX, see-through walls: a wall between the camera and a model is found, and nothing else is.
public class SeeThroughTests
{
    private static TerrainShape Wall(string id, double x0, double y0, double x1, double y1, double height = 3) =>
        new(id, new TableRect(x0, y0, x1, y1), TerrainProfile.Wall, height, false, false, id);

    private static SeeThrough.Point3 P(double x, double y, double z) => new(x, y, z);

    [Fact]
    public void AWallBetweenTheCameraAndAModelHidesItAndOnlyThatWall()
    {
        var walls = new[] { Wall("between", 10, 9, 11, 13), Wall("aside", 20, 0, 21, 4), Wall("behind", 3, 9, 4, 13) };
        // Camera east of the wall, low; model west of it.
        var hiding = SeeThrough.Hiding(P(20, 11, 2), walls, [P(6, 11, 0.6)]);
        Assert.Equal(["between"], hiding);
    }

    [Fact]
    public void AModelInFrontOfTheWallOrSeenOverItIsNotHidden()
    {
        var walls = new[] { Wall("w", 10, 9, 11, 13) };
        Assert.Empty(SeeThrough.Hiding(P(20, 11, 2), walls, [P(15, 11, 0.6)]));
        // From high above, the line passes over the 3" wall.
        Assert.Empty(SeeThrough.Hiding(P(20, 11, 60), walls, [P(8, 11, 0.6)]));
        // A line that only grazes the wall's top edge does not count.
        Assert.False(SeeThrough.SegmentHitsBox(P(20, 11, 3), P(0, 11, 3), new TableRect(10, 9, 11, 13), 3));
    }

    [Fact]
    public void LowTerrainNeverCounts()
    {
        var low = new[] { Wall("low", 10, 9, 11, 13, height: 0.25) };
        Assert.Empty(SeeThrough.Hiding(P(20, 11, 0.1), low, [P(6, 11, 0.1)]));
    }

    [Fact]
    public void AModelStandingOnAPlatformIsNotHiddenByThatPlatform()
    {
        var platform = new[] { new TerrainShape("p", new TableRect(10, 10, 14, 14), TerrainProfile.Wall, 3, true, false, "p") };
        Assert.Empty(SeeThrough.Hiding(P(25, 12, 15), platform, [P(12, 12, 3.6)]));
    }
}
