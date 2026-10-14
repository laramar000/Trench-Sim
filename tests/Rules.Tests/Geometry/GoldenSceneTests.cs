using System.Text.Json;
using Rules.Core.Geometry;

namespace Rules.Tests.Geometry;

// Ten golden scenes: each file holds a layout, the written ruling, the expected outcome, and whether the ruling rests on
// the rulebook (confirmed) or on a Trench-Sim interpretation (provisional). Positions are in inches.
public class GoldenSceneTests
{
    public static IEnumerable<object[]> SceneFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Scenes"), "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { Path.GetFileName(f) });

    private static string Num(JsonElement e) => e.GetDecimal().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static ModelBody BodyOf(string id, JsonElement e) => Geo.Body(id, Num(e.GetProperty("x")), Num(e.GetProperty("y")), Num(e.GetProperty("radius")));

    [Fact]
    public void ThereAreTenScenesWithUniqueIds()
    {
        var names = SceneFiles().Select(o => (string)o[0]).ToList();
        Assert.Equal(10, names.Count);
        Assert.Equal(10, names.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(SceneFiles))]
    public void SceneMatchesItsWrittenRuling(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenes", file)));
        var root = doc.RootElement;

        var status = root.GetProperty("status").GetString();
        Assert.True(status is "confirmed" or "provisional", "every scene says whether its ruling is confirmed or provisional");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("basis").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("ruling").GetString()));

        var terrain = root.GetProperty("terrain").EnumerateArray().Select(t =>
        {
            var r = t.GetProperty("rect").EnumerateArray().Select(Num).ToArray();
            return new TerrainBlock(
                t.GetProperty("id").GetString()!,
                Geo.F(r[0], r[1], r[2], r[3]),
                Enum.Parse<TerrainProfile>(t.GetProperty("profile").GetString()!),
                Enum.Parse<TerrainKind>(t.GetProperty("kind").GetString()!),
                Enum.Parse<SurfaceLevel>(t.GetProperty("surface").GetString()!),
                t.TryGetProperty("walkable", out var w) ? w.GetBoolean() : null);
        }).ToList();
        var size = root.GetProperty("fieldSize").GetInt32().ToString();
        var field = new Battlefield(Geo.L(size), Geo.L(size), terrain, []);
        var attacker = BodyOf("attacker", root.GetProperty("attacker"));
        var target = BodyOf("target", root.GetProperty("target"));
        ISightSampler? sampler = root.TryGetProperty("samplePoints", out var pts)
            ? new FixedSampler(pts.EnumerateArray().Select(p => Geo.P(Num(p[0]), Num(p[1]))).ToArray())
            : null;

        var result = Cover.Of(field, attacker, target, sampler);
        var expect = root.GetProperty("expect");
        Assert.Equal(expect.GetProperty("attackerLevel").GetString(), result.Sight.Attacker.Level.ToString());
        Assert.Equal(expect.GetProperty("targetLevel").GetString(), result.Sight.Target.Level.ToString());
        Assert.Equal(expect.GetProperty("visibility").GetString(), result.Sight.Visibility.ToString());
        Assert.Equal((expect.GetProperty("visible").GetInt32(), expect.GetProperty("total").GetInt32()), (result.Sight.Visible, result.Sight.Total));
        Assert.Equal(expect.GetProperty("cover").GetBoolean(), result.InCover);
        Assert.Equal(expect.GetProperty("coverBy").GetString(), result.TerrainId);
    }
}
