using Match.Protocol;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;
using Xunit;

namespace Server.Tests;

// Trench and maps plan, stage S1: the Claim No Man's Land layouts now come from content/maps. Loading them must give
// exactly the battlefield the C# layouts give, so every setup, state hash and record fingerprint is unchanged; and a
// broken map file must be refused with a clear message.
public class MapFileTests
{
    private static string Maps => Path.Combine(Repository.Root(), "content", MapFiles.Folder);

    private static string Content(string file) => Path.Combine(Repository.Root(), "content", file);

    private static string Json(Scenario scenario) => System.Text.Json.JsonSerializer.Serialize(scenario, Wire.Options);

    [Fact]
    public void ThePlainMapIsTheCodedLayout() =>
        Assert.Equal(Json(ClaimNoMansLand.Create()), Json(MapFiles.Load(Path.Combine(Maps, "claim-no-mans-land.json")).ToScenario()));

    [Fact]
    public void TheTowersMapIsTheCodedLayout() =>
        Assert.Equal(Json(ClaimNoMansLand.CreateWithTowers()), Json(MapFiles.Load(Path.Combine(Maps, "claim-no-mans-land-towers.json")).ToScenario()));

    // Every scenario name the server offers gives the same setup, state hash and record fingerprint as before.
    [Fact]
    public void EveryOfferedSetupIsUnchanged()
    {
        var all = ScenarioSetups.FromFolder(Path.Combine(Repository.Root(), "content"));
        Assert.Equal(16, all.Count);
        foreach (var (name, setup) in all)
        {
            var coded = name.EndsWith(ScenarioSetups.TowersSuffix, StringComparison.Ordinal)
                ? ClaimNoMansLand.CreateTowersSetup(setup.Models)
                : ClaimNoMansLand.CreateSetup(setup.Models);
            Assert.Equal(RecordFormat.ContentFingerprint(coded), RecordFormat.ContentFingerprint(setup));
            Assert.Equal(
                StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(coded))),
                StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))));
        }
    }

    [Fact]
    public void EveryMapFileLoadsAndNamesItsScenario()
    {
        var maps = MapFiles.LoadFolder(Maps);
        Assert.Equal(["claim-no-mans-land", "claim-no-mans-land-towers"], maps.Keys.Order(StringComparer.Ordinal));
        Assert.All(maps.Values, m => Assert.True(m.IsFor(ClaimNoMansLand.ScenarioId)));
    }

    [Fact]
    public void ABuiltOutputHasTheMapFiles() =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "content", MapFiles.Folder, "claim-no-mans-land-towers.json")));

    private static string Edit(string from, string to)
    {
        var text = File.ReadAllText(Path.Combine(Maps, "claim-no-mans-land.json"));
        Assert.Contains(from, text);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> Broken => new()
    {
        { "\"format\": 1", "\"format\": 2", "format must be 1" },
        { "\"name\":", "\"nmae\": \"x\", \"name\":", "unknown field 'nmae'" },
        { "\"archetype\": \"no-mans-land\"", "\"archetype\": \"moon\"", "unknown archetype 'moon'" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"10\", \"4\", \"14\"]", "rect needs four numbers" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"10\", \"4\", \"14\", \"8.0001\"]", "at most three decimals" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"14\", \"4\", \"10\", \"8\"]", "positive width and height" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"34\", \"4\", \"38\", \"8\"]", "must lie on the battlefield" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"10\", \"14\", \"14\", \"18\"]", "'obj-1' and 'rough-1' overlap" },
        { "\"id\": \"obj-2\"", "\"id\": \"obj-1\"", "unique" },
        { "\"profile\": \"wall\"", "\"profile\": \"tall\"", "unknown profile 'tall'" },
        { "\"kind\": [\"difficult\"]", "\"kind\": [\"sticky\"]", "unknown kind 'sticky'" },
        { "\"kind\": [\"difficult\"]", "\"kind\": [\"impassable\"], \"walkable\": true", "never walkable" },
        { "\"profile\": \"wall\"", "\"profile\": \"wall\", \"walkable\": true", "cannot be walked on" },
        { "\"objectives\": [\"obj-1\"", "\"objectives\": [\"wall-1\"", "Objective 'wall-1' must be a walkable terrain piece" },
        { "\"objectives\": [\"obj-1\"", "\"objectives\": [\"nowhere\"", "Objective 'nowhere'" },
        { "{\n      \"id\": \"west\",", "{\n      \"id\": \"north\",\n      \"rect\": [\"6\", \"30\", \"30\", \"36\"]\n    },\n    {\n      \"id\": \"west\",", "exactly two deployment zones" },
        { "\"width\": \"36\"", "\"width\": \"thirty\"", "not a length in inches" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void ABrokenMapIsRefusedWithAClearMessage(string from, string to, string message)
    {
        var e = Assert.Throws<MapFormatException>(() => MapFiles.Parse(Edit(from, to), "claim-no-mans-land.json"));
        Assert.StartsWith("claim-no-mans-land.json", e.Message);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void NotJsonIsRefused() =>
        Assert.Contains("not valid JSON", Assert.Throws<MapFormatException>(() => MapFiles.Parse("{", "m.json")).Message);

    [Fact]
    public void AMapWhoseIdIsNotItsFileNameIsRefused()
    {
        var dir = Directory.CreateTempSubdirectory("maps");
        try
        {
            var path = Path.Combine(dir.FullName, "renamed.json");
            File.WriteAllText(path, File.ReadAllText(Path.Combine(Maps, "claim-no-mans-land.json")));
            Assert.Contains("must match its file name", Assert.Throws<MapFormatException>(() => MapFiles.Load(path)).Message);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void ATowerMapKeepsItsTowerRules()
    {
        // A second tower of a different height breaks the battlefield's one-height rule, and the loader says so.
        var text = File.ReadAllText(Path.Combine(Maps, "claim-no-mans-land-towers.json"));
        var broken = text[..text.LastIndexOf("\"height\": \"2\"", StringComparison.Ordinal)] + "\"height\": \"2.5\"" + text[(text.LastIndexOf("\"height\": \"2\"", StringComparison.Ordinal) + "\"height\": \"2\"".Length)..];
        Assert.Contains("same height", Assert.Throws<MapFormatException>(() => MapFiles.Parse(broken, "t.json")).Message);
    }
}
