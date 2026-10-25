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
        // 16 Claim No Man's Land names, and since stage S6 eight Trench Warfare ones (TrenchWarfareTests).
        Assert.Equal(24, all.Count);
        foreach (var (name, setup) in all.Where(s => !s.Key.EndsWith(ScenarioSetups.TrenchWarfareSuffix, StringComparison.Ordinal)))
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
        // Since stage S7 with five presets (PresetMapTests).
        Assert.Equal(["broken-line", "claim-no-mans-land", "claim-no-mans-land-towers", "crossfire", "dust", "ruined-village", "shell-holes", "trench-warfare"],
            maps.Keys.Order(StringComparer.Ordinal));
        string[] trench = [ScenarioSetups.TrenchWarfareMap, "dust", "broken-line"];
        Assert.All(maps.Values.Where(m => !trench.Contains(m.Id)), m => Assert.True(m.IsFor(ClaimNoMansLand.ScenarioId)));
        Assert.All(trench, id => Assert.True(maps[id].IsFor(Rules.Core.Scenarios.TrenchWarfare.ScenarioId)));
    }

    // Trench-maps stage S4: the largest base any content model has.
    private static Rules.Core.Geometry.Length LargestBase =>
        new[] { "test-warbands.json", "melee-warbands.json" }
            .SelectMany(f => Sim.Content.Parse(File.ReadAllText(Content(f))))
            .SelectMany(w => w.Models)
            .Max(m => m.Profile.BaseRadius);

    [Fact]
    public void EveryMapFileKeepsTheSetUpRules()
    {
        Assert.Equal(Rules.Core.Geometry.Length.Parse("0.63"), LargestBase);
        var maps = MapFiles.LoadFolder(Maps);
        // The plain map is a No Man's Land set-up (p. 65); the tower test map is closer than that and names no archetype.
        Assert.Equal("no-mans-land", maps["claim-no-mans-land"].Archetype);
        Assert.Null(maps["claim-no-mans-land-towers"].Archetype);
        foreach (var map in maps.Values)
        {
            Assert.True(map.Problems(LargestBase).IsEmpty, $"{map.Id}: {string.Join("; ", map.Problems(LargestBase))}");
        }
    }

    [Fact]
    public void AMapThatBreaksTheSetUpRulesIsReported()
    {
        // obj-1 moved to 2" from wall-1 (No Man's Land wants 3", p. 65), and a wall in front of the west zone (p. 146).
        var close = MapFiles.Parse(Edit("\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"10\", \"5\", \"14\", \"9\"]"));
        Assert.Equal(["'obj-1' and 'wall-1' must be at least 3.000\" apart. (1.0.2 p. 65)"], close.Problems(LargestBase).Select(p => p.ToString()));
        var walled = MapFiles.Parse(Edit("\"terrain\": [", "\"terrain\": [\n    { \"id\": \"blocker\", \"rect\": [\"6\", \"0\", \"7\", \"36\"], \"profile\": \"wall\" },"));
        var problems = walled.Problems(LargestBase);
        Assert.Contains(problems, p => p.Kind == Rules.Core.Geometry.LayoutProblemKind.ZoneBlockedIn && p.Ids.SequenceEqual(["west"]));
        Assert.Equal(5, problems.Count(p => p.Kind == Rules.Core.Geometry.LayoutProblemKind.ObjectiveOutOfReach));
        // A ruin flag travels, and only true or false is taken.
        var ruins = MapFiles.Parse(Edit("\"id\": \"obj-5\",", "\"id\": \"obj-5\", \"ruin\": true,"));
        Assert.Equal(["obj-5"], ruins.RuinIds.AsEnumerable());
        Assert.Contains("ruin must be true or false", Assert.Throws<MapFormatException>(() => MapFiles.Parse(Edit("\"id\": \"obj-5\",", "\"id\": \"obj-5\", \"ruin\": 1,"))).Message);
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
        // Joined parts (stage S2).
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"rect\": [\"10\", \"4\", \"14\", \"8\"], \"parts\": []", "rect or parts, not both" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"parts\": [[\"10\", \"4\", \"14\", \"8\"]]", "two or more rects" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"parts\": [[\"10\", \"4\", \"14\", \"8\"], [\"12\", \"4\", \"16\", \"8\"]]", "overlap" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"parts\": [[\"10\", \"4\", \"12\", \"8\"], [\"13\", \"4\", \"14\", \"8\"]]", "joined along shared edges" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"parts\": [[\"10\", \"4\", \"14\", \"8\"], \"x\"]", "part 2 must be a list" },
        { "\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"parts\": [[\"10\", \"4\", \"14\", \"8\"], [\"14\", \"4\", \"15\"]]", "rect needs four numbers" },
    };

    [Fact]
    public void AGroundPieceCanBeJoinedParts()
    {
        // obj-1 as an L of two parts instead of a 4" square.
        var map = MapFiles.Parse(Edit("\"rect\": [\"10\", \"4\", \"14\", \"8\"]", "\"parts\": [[\"12\", \"4\", \"14\", \"6\"], [\"10\", \"4\", \"12\", \"8\"]]"), "claim-no-mans-land.json");
        var piece = map.Battlefield.Terrain.Single(t => t.Id == "obj-1");
        Assert.Equal(2, piece.Footprint.Parts.Length);
        Assert.Equal("10.000", piece.Footprint.Parts[0].Min.X.ToString().TrimEnd('"'));
        // A joined piece's setup travels and hashes; the coded layout's hash is not this one.
        var coded = ScenarioSetups.FromFolder(Path.Combine(Repository.Root(), "content")).First(s => !s.Key.EndsWith(ScenarioSetups.TowersSuffix, StringComparison.Ordinal)).Value;
        var setup = new MatchSetup(coded.RulesVersion, coded.ScenarioId, coded.TurnLimit, coded.Profile, coded.ZoneIds, coded.Models, map.ToScenario());
        Assert.NotEqual(
            StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(coded))),
            StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))));
        var json = Json(map.ToScenario());
        Assert.Equal(json, Json(System.Text.Json.JsonSerializer.Deserialize<Scenario>(json, Wire.Options)!));
    }

    private static IReadOnlyDictionary<string, MatchSetup> Setups => ScenarioSetups.FromFolder(Path.Combine(Repository.Root(), "content"));

    // Trench-maps stage S3: a trench's depth travels with it; a piece that is not a trench is written exactly as before.
    [Fact]
    public void ATrenchDepthTravelsAndIsLeftOutForOtherPieces()
    {
        var plain = Json(ClaimNoMansLand.Create());
        Assert.DoesNotContain("depth", plain, StringComparison.Ordinal);
        var towers = Json(ClaimNoMansLand.CreateWithTowers());
        Assert.Equal(6, towers.Split("\"depth\":").Length - 1);
        var read = System.Text.Json.JsonSerializer.Deserialize<Scenario>(towers, Wire.Options)!;
        Assert.Equal(6, read.Battlefield.Terrain.Count(t => t.IsTrench));
        Assert.Equal(towers, Json(read));
        var canonical = StateHash.Canonical(Reducer.Apply(MatchState.None, new MatchCreated(Setups["ranged-towers"])));
        Assert.Equal(6, canonical.Split('\n').Count(l => l.StartsWith("trench=", StringComparison.Ordinal) && l.Contains(",depth=1000", StringComparison.Ordinal)));
        Assert.DoesNotContain("\ntrench=", StateHash.Canonical(Reducer.Apply(MatchState.None, new MatchCreated(Setups["ranged"]))), StringComparison.Ordinal);
    }

    [Fact]
    public void AHighPieceCannotBeJoinedParts()
    {
        var text = File.ReadAllText(Path.Combine(Maps, "claim-no-mans-land-towers.json"));
        var at = text.IndexOf("\"surface\": \"high\"", StringComparison.Ordinal);
        Assert.True(at > 0);
        var rectAt = text.LastIndexOf("\"rect\":", at, StringComparison.Ordinal);
        var end = text.IndexOf(']', rectAt) + 1;
        var broken = text[..rectAt] + "\"parts\": [[\"0\", \"0\", \"1\", \"1\"], [\"1\", \"0\", \"2\", \"1\"]]" + text[end..];
        Assert.Contains("must be one rectangle", Assert.Throws<MapFormatException>(() => MapFiles.Parse(broken, "t.json")).Message);
    }

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
