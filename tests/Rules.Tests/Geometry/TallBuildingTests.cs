using Rules.Core.Geometry;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Geometry;

// U168: a Wall on the ground may be 4.5" or 6" high instead of 3".
public class TallBuildingTests
{
    private static TerrainBlock Tall(string id, string height, string x1 = "10", string y1 = "8", string x2 = "10.5", string y2 = "15") =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Wall) { Height = L(height) };

    [Fact]
    public void OnlyAnUnwalkableGroundWallCanBeTallAndOnlyAtTheTwoHeights()
    {
        Assert.Equal(L("4.5"), Tall("a", "4.5").Height);
        Assert.Equal(L("6"), Tall("b", "6").Height);
        Assert.True(Tall("c", "6").IsTall);
        Assert.False(Wall("plain", "1", "1", "2", "2").IsTall);
        Assert.Throws<ArgumentOutOfRangeException>(() => Tall("d", "5"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tall("e", "3"));
        Assert.Throws<ArgumentException>(() => new TerrainBlock("f", F("1", "1", "2", "2"), TerrainProfile.Cover) { Height = L("6") });
        Assert.Throws<ArgumentException>(() => new TerrainBlock("g", F("1", "1", "2", "2"), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true) { Height = L("6") });
        Assert.Throws<ArgumentException>(() => new TerrainBlock("h", F("1", "1", "2", "2"), TerrainProfile.Low) { Height = L("4.5") });
    }

    [Fact]
    public void SightAndCoverReadTheHeight()
    {
        Assert.Equal(SightHeights.Wall, SightHeights.TopOf(Wall("plain", "1", "1", "2", "2")));
        Assert.Equal(L("4.5"), SightHeights.TopOf(Tall("a", "4.5")));
        Assert.Equal(L("6"), SightHeights.TopOf(Tall("b", "6")));
    }

    // A model on a 2" tower, eye at 3.1", just clears a 3" wall right in front of it (a tenth of an inch away), and not a 4.5" one.
    [Fact]
    public void ATallBuildingHidesWhatATowerSeesOverAPlainWall()
    {
        var tower = new TerrainBlock("tower", F("2", "10", "4", "13"), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L("2"), [new Ladder("tower-w", P("2", "11.5"))]),
        };
        var shooter = new ModelBody("a", P("3.9", "11.5"), L("0.5"), L("2"));
        var target = Body("t", "14", "11.5");
        var plain = Field("24", tower, Wall("wall", "4", "8", "4.2", "15"));
        var tall = Field("24", tower, Tall("wall", "4.5", "4", "8", "4.2", "15"));
        Assert.NotEqual(Visibility.None, HeightSight.Between(plain, shooter, target).Visibility);
        Assert.Equal(Visibility.None, HeightSight.Between(tall, shooter, target).Visibility);
        // A model on the ground never sees over either.
        Assert.Equal(Visibility.None, HeightSight.Between(plain, Body("g", "3.9", "9"), Body("u", "14", "9")).Visibility);
    }
}
