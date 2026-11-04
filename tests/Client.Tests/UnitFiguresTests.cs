using System.Text;
using System.Text.Json;
using Client.Core;

namespace Client.Tests;

// Graphics stage G3: the original figures (client/assets/units, made by tools/art/make_figures.py): modelled low-poly
// bodies with small PS1-style textures. Every model type the test content has gets a figure, no entry names a type the
// content (or the faction lists) does not have, and each figure is small, 1.2" tall, fits inside the smallest base and
// has the untextured "Accent" part the client paints with the seat colour. The Trench Pilgrim (2026-11-03) is the
// template of docs/art-pipeline.md: a modular figure, one node and one texture per module, class A budget.
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

    // The faction lists' unit names (content/roster/factions), which roster matches use as model types.
    private static IEnumerable<string> RosterUnitNames =>
        Directory.GetFiles(Path.Combine(Repository.Root, "content", "roster", "factions"), "*.json")
            .SelectMany(f => Units(JsonDocument.Parse(File.ReadAllText(f)).RootElement))
            .Distinct();

    private static IEnumerable<string> Units(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => (e.TryGetProperty("baseMm", out _) && e.TryGetProperty("name", out var n) ? [n.GetString()!] : Array.Empty<string>())
            .Concat(e.EnumerateObject().SelectMany(p => Units(p.Value))),
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Units),
        _ => [],
    };

    [Fact]
    public void EveryModelTypeHasAFigureAndNoEntryInventsOne()
    {
        var names = ContentModelNames.ToList();
        Assert.Equal(8, names.Count);
        Assert.All(names, n => Assert.NotNull(Catalog.PathFor(VisualCatalog.UnitKey(n))));
        var known = names.Concat(RosterUnitNames).ToHashSet();
        Assert.Contains("Trench Pilgrim", known);
        Assert.Contains("Stigmatic Nun", known);
        Assert.Contains("Ecclesiastic Prisoner", known);
        Assert.Contains("War Prophet", known);
        Assert.Contains("Castigator", known);
        Assert.Contains("Reliquary Walker", known);
        Assert.Contains("Sanctified Giant", known);
        Assert.Contains("Yeoman", known);
        Assert.Contains("Shock Trooper", known);
        var units = Catalog.Entries.Keys.Where(k => k.StartsWith("unit:", StringComparison.Ordinal)).Select(k => k["unit:".Length..]).ToList();
        Assert.All(units, u => Assert.Contains(u, known));
        Assert.Equal(13, Catalog.Entries.Where(e => e.Key.StartsWith("unit:", StringComparison.Ordinal)).Select(e => e.Value).Distinct().Count());
    }

    // The figures are read at run time, so the editor never imports them: an import would write their textures out as
    // loose PNG files beside them (unlisted art).
    [Fact]
    public void TheEditorLeavesTheFiguresFolderAlone() => Assert.True(File.Exists(Path.Combine(Assets, "units", ".gdignore")));

    // Each figure with its triangle budget, by its size class (docs/art-pipeline.md section 3): 1,850 on a 25 mm base, 2,100
    // on a 32 mm base; the Castigator 3,100, an exception the owner allowed for its gas mask (2026-11-03). Then its largest
    // texture and its widest footprint at the file's 1.2 height: 0.9 fits a 25 mm base at 1.2"; the Reliquary Walker (8,000
    // and 256 px, the owner's exception, 2026-11-03) is drawn 2.4" tall, so 1.1 is 2.2" on its 60 mm (2.36") base; the
    // Sanctified Giant (5,200, the owner allows larger units more) is drawn 1.6", so 1.1 is 1.47" on its 40 mm (1.57") base.
    // The Yeoman 2,400, the owner's exception for its WWI gas mask (2026-11-03); the Shock Trooper
    // 2,700 for the same mask and its plate armour (2026-11-03).
    public static TheoryData<string, int, int, float> Figures => new()
    {
        { "units/rifleman.glb", 1850, 128, 0.9f }, { "units/skirmisher.glb", 1850, 128, 0.9f }, { "units/shield.glb", 1850, 128, 0.9f },
        { "units/standard.glb", 1850, 128, 0.9f }, { "units/trench-pilgrim.glb", 1850, 128, 0.9f },
        { "units/stigmatic-nun.glb", 1850, 128, 0.9f }, { "units/ecclesiastic-prisoner.glb", 1850, 128, 0.9f },
        { "units/war-prophet.glb", 2100, 128, 0.9f }, { "units/castigator.glb", 3100, 128, 0.9f },
        { "units/reliquary-walker.glb", 8000, 256, 1.1f }, { "units/sanctified-giant.glb", 5200, 256, 1.1f },
        { "units/yeoman.glb", 2400, 128, 0.9f }, { "units/shock-trooper.glb", 2700, 128, 0.9f },
    };

    [Theory]
    [MemberData(nameof(Figures))]
    public void AFigureIsSmallFitsItsBaseAndHasAnAccent(string file, int budget, int texture, float width)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Assets, file));
        Assert.True(bytes.Length < Math.Max(160 * 1024, budget * 80) * (texture / 128), $"{bytes.Length} bytes");   // a larger budget or texture, a larger file
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
        // PS1-style budget (docs/art-pipeline.md): at most its class's triangles; PNG textures of at most
        // 128 x 128 (256 for the largest classes; one, or one per module), sampled nearest.
        var triangles = root.GetProperty("meshes").EnumerateArray()
            .SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Sum(p => (p.TryGetProperty("indices", out var i) ? accessors[i.GetInt32()] : accessors[p.GetProperty("attributes").GetProperty("POSITION").GetInt32()]).GetProperty("count").GetInt32() / 3);
        Assert.InRange(triangles, 1, budget);
        var images = root.GetProperty("images").EnumerateArray().ToList();
        Assert.InRange(images.Count, 1, 8);
        int Be(int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
        foreach (var image in images)
        {
            Assert.Equal("image/png", image.GetProperty("mimeType").GetString());
            var view = root.GetProperty("bufferViews")[image.GetProperty("bufferView").GetInt32()];
            var png = 20 + length + 8 + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0);
            Assert.Equal(0x89, bytes[png]);
            Assert.InRange(Be(png + 16), 1, texture);
            Assert.InRange(Be(png + 20), 1, texture);
        }
        Assert.All(root.GetProperty("samplers").EnumerateArray(), s => Assert.Equal(9728, s.GetProperty("magFilter").GetInt32()));
        // The accent stays untextured, so the seat colour the client paints is the only colour it has.
        var accent = root.GetProperty("nodes").EnumerateArray().First(n => n.TryGetProperty("name", out var name) && name.GetString() == "Accent");
        Assert.All(root.GetProperty("meshes")[accent.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray(),
            p => Assert.False(p.GetProperty("attributes").TryGetProperty("TEXCOORD_0", out _)));
        float Min(int i) => positions.Min(a => a.GetProperty("min")[i].GetSingle());
        float Max(int i) => positions.Max(a => a.GetProperty("max")[i].GetSingle());
        Assert.Equal(0f, Min(1), 3);
        Assert.Equal(1.2f, Max(1), 3);
        // Inside its base at its drawn height, so the client never has to shrink it.
        Assert.True(Max(0) - Min(0) <= width && Max(2) - Min(2) <= width, $"{Max(0) - Min(0)} x {Max(2) - Min(2)}");
    }
}
