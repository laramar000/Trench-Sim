using System.Text;
using System.Text.Json;
using Client.Core;

namespace Client.Tests;

// Graphics stage G3: the four original figures (client/assets/units, made by tools/art/make_figures.py): modelled
// low-poly bodies with one small PS1-style texture each. Every model type the content has gets a figure, no entry names
// a type the content does not have, and each figure is small, 1.2" tall, fits inside the smallest base and has the
// untextured "Accent" part the client paints with the seat colour.
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
        Assert.True(bytes.Length < 128 * 1024, $"{bytes.Length} bytes");
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
        // PS1-style budget: at most 1,400 triangles and one PNG texture of at most 128 x 128, sampled nearest.
        var triangles = root.GetProperty("meshes").EnumerateArray()
            .SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Sum(p => (p.TryGetProperty("indices", out var i) ? accessors[i.GetInt32()] : accessors[p.GetProperty("attributes").GetProperty("POSITION").GetInt32()]).GetProperty("count").GetInt32() / 3);
        Assert.InRange(triangles, 1, 1400);
        var images = root.GetProperty("images").EnumerateArray().ToList();
        Assert.Single(images);
        Assert.Equal("image/png", images[0].GetProperty("mimeType").GetString());
        var view = root.GetProperty("bufferViews")[images[0].GetProperty("bufferView").GetInt32()];
        var png = 20 + length + 8 + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0);
        int Be(int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
        Assert.Equal(0x89, bytes[png]);
        Assert.InRange(Be(png + 16), 1, 128);
        Assert.InRange(Be(png + 20), 1, 128);
        Assert.All(root.GetProperty("samplers").EnumerateArray(), s => Assert.Equal(9728, s.GetProperty("magFilter").GetInt32()));
        // The accent stays untextured, so the seat colour the client paints is the only colour it has.
        var accent = root.GetProperty("nodes").EnumerateArray().First(n => n.TryGetProperty("name", out var name) && name.GetString() == "Accent");
        Assert.All(root.GetProperty("meshes")[accent.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray(),
            p => Assert.False(p.GetProperty("attributes").TryGetProperty("TEXCOORD_0", out _)));
        float Min(int i) => positions.Min(a => a.GetProperty("min")[i].GetSingle());
        float Max(int i) => positions.Max(a => a.GetProperty("max")[i].GetSingle());
        Assert.Equal(0f, Min(1), 3);
        Assert.Equal(1.2f, Max(1), 3);
        // Inside a 25 mm base (0.984" across) at that height, so the client never has to shrink it.
        Assert.True(Max(0) - Min(0) <= 0.9f && Max(2) - Min(2) <= 0.9f, $"{Max(0) - Min(0)} x {Max(2) - Min(2)}");
    }
}
