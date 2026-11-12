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
        // Trenches are drawn as pits, not dressed (TrenchClientTests).
        .SelectMany(s => s.Terrain.Where(t => !t.Trench))
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
        // U168: tall buildings, thin and thick, one rectangle and joined.
        data.Add(new TerrainShape("tall-45", new TableRect(1, 1, 7, 2), TerrainProfile.Wall, 4.5, false, false, "Wall"));
        data.Add(new TerrainShape("tall-6", new TableRect(1, 1, 4, 4), TerrainProfile.Wall, 6, false, false, "Wall"));
        data.Add(new TerrainShape("tall-ell", new TableRect(10, 10, 14.5, 14), TerrainProfile.Wall, 6, false, false, "Wall")
        {
            Parts = [new TableRect(10, 10, 14.5, 10.5), new TableRect(10, 10.5, 10.5, 14)],
        });
        // Trench-maps stage S8: ruined corners and other joined pieces, dressed one rectangle at a time, nothing in the
        // corner the L leaves open (Inside checks the piece's parts, not the box around them).
        foreach (var (profile, height) in new[] { (TerrainProfile.Wall, 3.0), (TerrainProfile.Cover, 0.75), (TerrainProfile.Low, 0.25) })
        {
            data.Add(new TerrainShape($"corner-{profile}", new TableRect(10, 10, 14.5, 14), profile, height, false, false, "Corner")
            {
                Parts = [new TableRect(10, 10, 14.5, 10.5), new TableRect(10, 10.5, 10.5, 14)],
            });
        }
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

    // U168: a tall building has a row of windows for each storey and a floor line between; a plain wall keeps its one row.
    [Fact]
    public void ATallBuildingHasStoreys()
    {
        var plain = new TerrainShape("w", new TableRect(1, 1, 7, 2), TerrainProfile.Wall, 3, false, false, "Wall");
        var tall = plain with { Id = "w", Height = 6 };
        var windows = (TerrainShape piece) => TerrainDress.For(piece).Where(p => p.Shape == PartShape.Arch).Select(p => Math.Round(p.Y, 1)).Distinct().Count();
        Assert.Equal(1, windows(plain));
        Assert.True(windows(tall) >= 3, $"{windows(tall)} rows of windows");
        Assert.Contains(TerrainDress.For(tall), p => p.Shape == PartShape.Block && p.SizeY < 0.1);
    }

    [Fact]
    public void ATowerIsTimberAndOtherPiecesUseTheirProfile()
    {
        var tower = Pieces.First(p => p.Tower);
        Assert.Equal("terrain:Tower", TerrainDress.TextureKey(tower));
        Assert.Equal("terrain:Cover", TerrainDress.TextureKey(Pieces.First(p => p.Profile == TerrainProfile.Cover)));
    }
}
