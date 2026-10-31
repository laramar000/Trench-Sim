using Rules.Core.Geometry;
using Sim;

namespace Server.Tests;

// Line-of-sight review 2026-10-25, Stage 2a (U94): a map file may not hold two separate pieces that block sight and
// touch, along an edge or at a corner, because a sight ray along the seam only grazes each one (U13) and passes
// (SightLimitTests.TwoSeparateWallsPlacedEdgeToEdgeLeakSightAlongTheirSeam). The sight code is unchanged; the map is
// refused instead, and the message names the pieces and says how to fix the map.
public class MapSeamTests
{
    // A 24" map with two zones and the given terrain pieces (JSON objects).
    private static string Map(params string[] terrain) => $$"""
        {
          "format": 1,
          "id": "seams",
          "name": "Seams",
          "width": "24",
          "height": "24",
          "supports": ["claim-no-mans-land"],
          "zones": [ { "id": "west", "rect": ["0", "0", "4", "24"] }, { "id": "east", "rect": ["20", "0", "24", "24"] } ],
          "objectives": [],
          "terrain": [ {{string.Join(", ", terrain)}} ]
        }
        """;

    private static string Piece(string id, string rect, string profile, string extra = "") =>
        $$"""{ "id": "{{id}}", "rect": [{{rect}}], "profile": "{{profile}}"{{extra}} }""";

    private static string Refused(params string[] terrain) =>
        Assert.Throws<MapFormatException>(() => MapFiles.Parse(Map(terrain), "seams.json")).Message;

    [Fact]
    public void TwoWallsSharingAnEdgeAreRefusedByName()
    {
        // The Stage 1 leak scene: walls meeting along y = 12.
        var message = Refused(Piece("south", "\"6\", \"0\", \"7\", \"12\"", "wall"), Piece("north", "\"6\", \"12\", \"7\", \"24\"", "wall"));
        Assert.StartsWith("seams.json ('seams'): terrain 'north' and 'south' touch along an edge", message);
        Assert.Contains("block sight may not touch", message);
        Assert.Contains("(U94)", message);
        Assert.Contains("one piece with \"parts\"", message);
        Assert.Contains("leave a gap", message);
    }

    [Fact]
    public void TwoWallsSharingOnlyACornerAreRefused()
    {
        // Corner contact leaks too: a ray through the shared corner, between the two walls, grazes both.
        var message = Refused(Piece("q1", "\"6\", \"12\", \"9\", \"15\"", "wall"), Piece("q3", "\"3\", \"9\", \"6\", \"12\"", "wall"));
        Assert.Contains("terrain 'q1' and 'q3' touch at a corner", message);
    }

    [Theory]
    [InlineData("cover", "cover")]
    [InlineData("wall", "cover")]
    public void CoverPiecesBlockSightAndAreRefusedToo(string one, string two)
    {
        var message = Refused(Piece("a", "\"6\", \"0\", \"7\", \"12\"", one), Piece("b", "\"6\", \"12\", \"7\", \"24\"", two));
        Assert.Contains("terrain 'a' and 'b' touch along an edge", message);
    }

    [Fact]
    public void ATowerTouchingAWallIsRefused()
    {
        var tower = Piece("tower", "\"10\", \"10\", \"13\", \"13\"", "wall",
            ", \"surface\": \"high\", \"walkable\": true, \"climb\": { \"height\": \"2\", \"ladders\": [ { \"id\": \"tower-w\", \"at\": [\"10\", \"11.5\"] } ] }");
        Assert.Contains("terrain 'tower' and 'wall' touch along an edge", Refused(tower, Piece("wall", "\"13\", \"10\", \"14\", \"16\"", "wall")));
    }

    [Theory]
    [InlineData("12.001")] // a thousandth apart
    [InlineData("12.5")]
    public void WallsWithARealGapBetweenThemLoad(string northFrom)
    {
        var map = MapFiles.Parse(Map(Piece("south", "\"6\", \"0\", \"7\", \"12\"", "wall"), Piece("north", $"\"6\", \"{northFrom}\", \"7\", \"24\"", "wall")), "seams.json");
        Assert.Equal(2, map.Battlefield.Terrain.Count());
    }

    [Fact]
    public void OverlappingWallsAreStillRefusedAsAnOverlap()
    {
        var message = Refused(Piece("south", "\"6\", \"0\", \"7\", \"13\"", "wall"), Piece("north", "\"6\", \"12\", \"7\", \"24\"", "wall"));
        Assert.Contains("'south' and 'north' overlap", message);
    }

    [Fact]
    public void OneJoinedWallLoadsAndBlocksSightAlongItsInnerSeam()
    {
        var joined = """{ "id": "wall", "parts": [["6", "0", "7", "12"], ["6", "12", "7", "24"]], "profile": "wall" }""";
        var map = MapFiles.Parse(Map(joined), "seams.json");
        var sight = HeightSight.Between(map.Battlefield, new ModelBody("a", new Point2(Length.FromInches(2), Length.FromInches(12)), Length.HalfInch),
            new ModelBody("t", new Point2(Length.FromInches(10), Length.FromInches(12)), Length.HalfInch));
        Assert.Equal(Visibility.None, sight.Visibility);
    }

    [Fact]
    public void LowPiecesAndTrenchesMayStillTouch()
    {
        // Touching stays allowed for Low pieces (rough ground) and trench sections (U94). A Low piece is a 0.25" box in the
        // sight code and can affect sight near a trench's edge, but it is under the 1/2" that cover needs, so it never gives
        // cover; touching trench sections are one open space (U83).
        var map = MapFiles.Parse(Map(
            Piece("rough-1", "\"8\", \"2\", \"10\", \"6\"", "low", ", \"kind\": [\"difficult\"]"),
            Piece("rough-2", "\"10\", \"2\", \"12\", \"6\"", "low", ", \"kind\": [\"difficult\"]"),
            Piece("wall", "\"12\", \"2\", \"13\", \"6\"", "wall"),
            Piece("trench-a", "\"8\", \"10\", \"12\", \"12.5\"", "low", ", \"depth\": \"1\""),
            Piece("trench-b", "\"12\", \"10\", \"16\", \"12.5\"", "low", ", \"depth\": \"1\""),
            Piece("cover", "\"10\", \"12.5\", \"11\", \"14\"", "cover")), "seams.json");
        Assert.Equal(6, map.Battlefield.Terrain.Count());
    }

    [Fact]
    public void TheMessageIsTheSameInAnyTerrainOrder()
    {
        string[] pieces =
        [
            Piece("c", "\"14\", \"0\", \"15\", \"12\"", "wall"),
            Piece("a", "\"6\", \"0\", \"7\", \"12\"", "wall"),
            Piece("d", "\"14\", \"12\", \"15\", \"24\"", "cover"),
            Piece("b", "\"6\", \"12\", \"7\", \"24\"", "wall"),
        ];
        var first = Refused(pieces);
        Assert.Contains("terrain 'a' and 'b' touch along an edge; terrain 'c' and 'd' touch along an edge:", first);
        Assert.Equal(first, Refused([.. pieces.Reverse()]));
        Assert.Equal(first, Refused(pieces[2], pieces[0], pieces[3], pieces[1]));
    }

    [Fact]
    public void EveryShippedMapHasNoTouchingSightBlockers()
    {
        // Each content map loads (the loader refuses touching blockers), so none needs correcting.
        var folder = Path.Combine(Repository.Root(), "content", MapFiles.Folder);
        var files = Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            MapFiles.Load(file);
        }
    }
}
