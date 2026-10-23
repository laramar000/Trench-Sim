using System.Text;
using System.Text.Json;
using Client.Core;

namespace Client.Tests;

// Graphics stage G3: the four original low-poly figures (client/assets/units, made by tools/art/make_figures.py).
// Every model type the content has gets a figure, no entry names a type the content does not have, and each figure is
// small, 1.2" tall, fits inside the smallest base and has the "Accent" part the client paints with the seat colour.
public class UnitFiguresTests
{
    private static string Assets => Path.Combine(Repository.Root, "client", "assets");

    private static VisualCatalog Catalog => VisualCatalog.Parse(File.ReadAllText(Path.Combine(Assets, VisualCatalog.FileName)));

    private static IEnumerable<string> ContentModelNames =>
        new[] { "test-warbands.json", "melee-warbands.json" }
            .SelectMany(f => ContentNames.Parse(File.ReadAllText(Path.Combine(Repository.Root, "content", f))))
            .SelectMany(b => b.Models)
            .Select(m => m.Name)
            .Distinct();

    [Fact]
    public void EveryModelTypeHasAFigureAndNoEntryInventsOne()
    {
        var names = ContentModelNames.ToList();
        Assert.Equal(8, names.Count);
        Assert.All(names, n => Assert.NotNull(Catalog.PathFor(VisualCatalog.UnitKey(n))));
        var units = Catalog.Entries.Keys.Where(k => k.StartsWith("unit:", StringComparison.Ordinal)).Select(k => k["unit:".Length..]).ToList();
        Assert.All(units, u => Assert.Contains(u, names));
        Assert.Equal(4, Catalog.Entries.Where(e => e.Key.StartsWith("unit:", StringComparison.Ordinal)).Select(e => e.Value).Distinct().Count());
    }

    public static TheoryData<string> Figures => new() { "units/rifleman.glb", "units/skirmisher.glb", "units/shield.glb", "units/standard.glb" };

    [Theory]
    [MemberData(nameof(Figures))]
    public void AFigureIsSmallFitsTheSmallestBaseAndHasAnAccent(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Assets, file));
        Assert.True(bytes.Length < 64 * 1024, $"{bytes.Length} bytes");
        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(bytes, 0));
        var length = BitConverter.ToInt32(bytes, 12);
        using var gltf = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 20, length));
        var root = gltf.RootElement;
        Assert.Contains(root.GetProperty("nodes").EnumerateArray(), n => n.TryGetProperty("name", out var name) && name.GetString() == "Accent");
        var accessors = root.GetProperty("accessors").EnumerateArray().ToList();
        var positions = root.GetProperty("meshes").EnumerateArray()
            .SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Select(p => accessors[p.GetProperty("attributes").GetProperty("POSITION").GetInt32()])
            .ToList();
        // PS1-style budget: at most 600 triangles, and no textures.
        Assert.InRange(positions.Sum(a => a.GetProperty("count").GetInt32()) / 3, 1, 600);
        Assert.False(root.TryGetProperty("images", out _));
        float Min(int i) => positions.Min(a => a.GetProperty("min")[i].GetSingle());
        float Max(int i) => positions.Max(a => a.GetProperty("max")[i].GetSingle());
        Assert.Equal(0f, Min(1), 3);
        Assert.Equal(1.2f, Max(1), 3);
        // Inside a 25 mm base (0.984" across) at that height, so the client never has to shrink it.
        Assert.True(Max(0) - Min(0) <= 0.9f && Max(2) - Min(2) <= 0.9f, $"{Max(0) - Min(0)} x {Max(2) - Min(2)}");
    }
}
