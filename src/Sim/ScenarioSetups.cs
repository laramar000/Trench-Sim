using System.Collections.Immutable;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Sim;

// The scenarios a server offers: Claim No Man's Land, on a map from content/maps, with the practice warbands from the
// content files. The first warband in a file takes Seat One (models a1, a2, ...), the second Seat Two (b1, b2, ...).
public static class ScenarioSetups
{
    // Smaller sides for quick tests: "ranged-1v1" ... "melee-3v3" take the first N models of each warband, in file
    // order. They are separate scenario names, so the normal "ranged" and "melee" matches are exactly as before, and a
    // record says which one was played.
    public static readonly int[] TestSizes = [1, 2, 3];

    public static IReadOnlyDictionary<string, MatchSetup> FromFolder(string folder)
    {
        var maps = Maps(folder);
        var all = new Dictionary<string, MatchSetup>(StringComparer.Ordinal);
        foreach (var (name, file) in new[] { ("ranged", WarbandFiles[0]), ("melee", WarbandFiles[1]) })
        {
            var path = Path.Combine(folder, file);
            all[name] = Setup(path, maps[PlainMap]);
            foreach (var size in TestSizes)
            {
                all[$"{name}-{size}v{size}"] = Setup(path, maps[PlainMap], size);
            }
            all[name + TowersSuffix] = Setup(path, maps[TowersMap]);
            // The small test rosters on the tower layout too ("ranged-1v1-towers"): the Test Center plays on towers by default.
            foreach (var size in TestSizes)
            {
                all[$"{name}-{size}v{size}{TowersSuffix}"] = Setup(path, maps[TowersMap], size);
            }
            // Trench-maps stage S6: Scenario IV on its map ("ranged-trench-warfare", "melee-2v2-trench-warfare").
            all[name + TrenchWarfareSuffix] = Setup(path, maps[TrenchWarfareMap], definition: TrenchWarfare.Definition);
            foreach (var size in TestSizes)
            {
                all[$"{name}-{size}v{size}{TrenchWarfareSuffix}"] = Setup(path, maps[TrenchWarfareMap], size, TrenchWarfare.Definition);
            }
        }
        return all;
    }

    // Towers proposal, stage T3: "ranged-towers" and "melee-towers" are the same matches on the layout with two
    // climbable towers (ClaimNoMansLand.CreateWithTowers). Separate names, so every other scenario is exactly as before.
    public const string TowersSuffix = "-towers";

    // The scenario name for a content scenario and a side size; null size is the normal roster.
    public static string Name(string scenario, int? size) => size is { } n ? $"{scenario}-{n}v{n}" : scenario;

    public static MatchSetup Setup(string contentPath, int? size = null, bool towers = false) =>
        Setup(contentPath, MapFiles.Load(Path.Combine(MapFiles.FolderNextTo(contentPath), (towers ? TowersMap : PlainMap) + ".json")), size);

    public static MatchSetup Setup(string contentPath, MapDefinition map, int? size = null, ScenarioDefinition? definition = null)
    {
        var warbands = Content.Load(contentPath);
        var take = size ?? int.MaxValue;
        var models = warbands[0].Models.Take(take).Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
            .Concat(warbands[1].Models.Take(take).Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
        return OnMap(definition ?? ClaimNoMansLand.Definition, map, models, RulesProfile.Default);
    }

    // Trench-maps stage S5: the scenarios this build plays, by id.
    public static readonly IReadOnlyDictionary<string, ScenarioDefinition> Definitions =
        new Dictionary<string, ScenarioDefinition>(StringComparer.Ordinal)
        {
            [ClaimNoMansLand.ScenarioId] = ClaimNoMansLand.Definition,
            [TrenchWarfare.ScenarioId] = TrenchWarfare.Definition,
        };

    // The same match on another map: the setup's scenario, models and rules profile, with the map's battlefield.
    // Refused (MapFormatException) when the map is not made for the scenario or does not fit what it needs.
    public static MatchSetup OnMap(MatchSetup setup, MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var definition = Definitions.GetValueOrDefault(setup.ScenarioId)
            ?? throw new MapFormatException($"Scenario '{setup.ScenarioId}' has no definition in this build.");
        return OnMap(definition, map, setup.Models, setup.Profile);
    }

    private static MatchSetup OnMap(ScenarioDefinition definition, MapDefinition map, IEnumerable<SetupModel> models, RulesProfile profile)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.IsFor(definition.Id))
        {
            throw new MapFormatException($"Map '{map.Id}' is not made for {definition.Id}.");
        }
        var unmet = definition.Unmet(map.ToScenario(), map.ArchetypeKind);
        if (!unmet.IsEmpty)
        {
            throw new MapFormatException($"Map '{map.Id}' does not fit {definition.Id}: {string.Join(" ", unmet)}");
        }
        return definition.Setup(map.ToScenario(), map.ArchetypeKind, models, profile);
    }

    // Trench-maps stage S5: the maps a server offers, from content/maps next to the warband files, each checked
    // against the set-up rules (stage S4) for the largest base in the content. A map that breaks them stops the load.
    public static IReadOnlyDictionary<string, MapDefinition> Maps(string contentFolder)
    {
        var maps = MapFiles.LoadFolder(Path.Combine(contentFolder, MapFiles.Folder));
        var largest = LargestBase(contentFolder);
        foreach (var map in maps.Values)
        {
            var problems = map.Problems(largest);
            if (!problems.IsEmpty)
            {
                throw new MapFormatException($"Map '{map.Id}' breaks the set-up rules: {string.Join("; ", problems)}");
            }
        }
        return maps;
    }

    // Trench-maps stage S8: a map by id, from the content maps or, for a generated id, made from its seed; null if neither.
    public static MapDefinition? MapById(IReadOnlyDictionary<string, MapDefinition> maps, string id)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(id);
        return maps.GetValueOrDefault(id) ?? MapGenerator.FromId(id);
    }

    // The largest base radius of any model in the content's warband files.
    public static Length LargestBase(string contentFolder) =>
        WarbandFiles.Select(f => Path.Combine(contentFolder, f)).Where(File.Exists)
            .SelectMany(Content.Load).SelectMany(w => w.Models).Select(m => m.Profile.BaseRadius)
            .DefaultIfEmpty(Length.HalfInch).Max();

    public static readonly ImmutableArray<string> WarbandFiles = ["test-warbands.json", "melee-warbands.json"];

    // Trench and maps plan, stage S1: the layouts come from the map files in content/maps. They are the same layouts as
    // ClaimNoMansLand.Create and CreateWithTowers, which stay in Rules.Core as the reference the map tests compare with.
    public const string PlainMap = "claim-no-mans-land";
    public const string TowersMap = "claim-no-mans-land-towers";

    // Trench-maps stage S6.
    public const string TrenchWarfareSuffix = "-trench-warfare";
    public const string TrenchWarfareMap = "trench-warfare";
}
