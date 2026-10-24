using Client.Core;
using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Graphics stage G4: the models terrain is drawn with stay inside each piece's footprint and under its height, so the
// rules' shapes (and what they mean for movement and sight) are what the player sees.
public class TerrainDressTests
{
    private static IEnumerable<TerrainShape> Pieces => PreviewAgreementTests.States.Value
        .Where(s => s.Phase == Phase.Activation)
        .Select(s => BoardScene.Of(s, Seat.One, ContentNames.None))
        .OfType<BoardScene>()
        .SelectMany(s => s.Terrain)
        .GroupBy(t => (t.Id, t.Area, t.Height))
        .Select(g => g.First());

    public static TheoryData<TerrainShape> Shapes()
    {
        var data = new TheoryData<TerrainShape>();
        foreach (var piece in Pieces)
        {
            data.Add(piece);
        }
        // Odd sizes the scenarios do not have yet: thin, small, upright.
        data.Add(new TerrainShape("odd-wall", new TableRect(1, 1, 2, 7), TerrainProfile.Wall, 3, false, false, "Wall"));
        data.Add(new TerrainShape("odd-cover", new TableRect(3, 3, 4.2, 5), TerrainProfile.Cover, 0.75, false, false, "Cover"));
        data.Add(new TerrainShape("odd-low", new TableRect(5, 5, 6, 9), TerrainProfile.Low, 0.25, false, false, "Low"));
        return data;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryPartStaysInsideTheFootprintAndUnderTheHeight(TerrainShape piece)
    {
        var parts = TerrainDress.For(piece);
        Assert.NotEmpty(parts);
        Assert.All(parts, p => Assert.True(TerrainDress.Inside(piece, p), $"{piece.Id}: {p}"));
        Assert.All(parts, p => Assert.True(p.SizeX > 0 && p.SizeY > 0 && p.SizeZ > 0, $"{piece.Id}: {p}"));
    }

    [Fact]
    public void TheScenariosHaveEveryKindOfPieceAndTheSamePieceGetsTheSameParts()
    {
        var pieces = Pieces.ToList();
        Assert.Contains(pieces, p => p.Tower);
        Assert.Contains(pieces, p => p.Profile == TerrainProfile.Wall && !p.Tower);
        Assert.Contains(pieces, p => p.Profile == TerrainProfile.Cover);
        Assert.Contains(pieces, p => p.Profile == TerrainProfile.Low);
        Assert.All(pieces, p => Assert.Equal(TerrainDress.For(p), TerrainDress.For(p with { Controller = Seat.Two })));
    }

    [Fact]
    public void ATowerIsTimberAndOtherPiecesUseTheirProfile()
    {
        var tower = Pieces.First(p => p.Tower);
        Assert.Equal("terrain:Tower", TerrainDress.TextureKey(tower));
        Assert.Equal("terrain:Cover", TerrainDress.TextureKey(Pieces.First(p => p.Profile == TerrainProfile.Cover)));
    }
}
