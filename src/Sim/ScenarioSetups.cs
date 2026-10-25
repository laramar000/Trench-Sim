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
        var all = new Dictionary<string, MatchSetup>(StringComparer.Ordinal);
        foreach (var (name, file) in new[] { ("ranged", "test-warbands.json"), ("melee", "melee-warbands.json") })
        {
            var path = Path.Combine(folder, file);
            all[name] = Setup(path);
            foreach (var size in TestSizes)
            {
                all[$"{name}-{size}v{size}"] = Setup(path, size);
            }
            all[name + TowersSuffix] = Setup(path, towers: true);
            // The small test rosters on the tower layout too ("ranged-1v1-towers"): the Test Center plays on towers by default.
            foreach (var size in TestSizes)
            {
                all[$"{name}-{size}v{size}{TowersSuffix}"] = Setup(path, size, towers: true);
            }
        }
        return all;
    }

    // Towers proposal, stage T3: "ranged-towers" and "melee-towers" are the same matches on the layout with two
    // climbable towers (ClaimNoMansLand.CreateWithTowers). Separate names, so every other scenario is exactly as before.
    public const string TowersSuffix = "-towers";

    // The scenario name for a content scenario and a side size; null size is the normal roster.
    public static string Name(string scenario, int? size) => size is { } n ? $"{scenario}-{n}v{n}" : scenario;

    public static MatchSetup Setup(string contentPath, int? size = null, bool towers = false)
    {
        var warbands = Content.Load(contentPath);
        var take = size ?? int.MaxValue;
        var models = warbands[0].Models.Take(take).Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
            .Concat(warbands[1].Models.Take(take).Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
        var map = MapFiles.Load(Path.Combine(MapFiles.FolderNextTo(contentPath), (towers ? TowersMap : PlainMap) + ".json"));
        if (!map.IsFor(ClaimNoMansLand.ScenarioId))
        {
            throw new MapFormatException($"Map '{map.Id}' is not made for {ClaimNoMansLand.ScenarioId}.");
        }
        return new MatchSetup(ClaimNoMansLand.RulesVersion, ClaimNoMansLand.ScenarioId, ClaimNoMansLand.TurnLimit, RulesProfile.Default,
            [ClaimNoMansLand.West, ClaimNoMansLand.East], models, map.ToScenario());
    }

    // Trench and maps plan, stage S1: the layouts come from the map files in content/maps. They are the same layouts as
    // ClaimNoMansLand.Create and CreateWithTowers, which stay in Rules.Core as the reference the map tests compare with.
    public const string PlainMap = "claim-no-mans-land";
    public const string TowersMap = "claim-no-mans-land-towers";
}
