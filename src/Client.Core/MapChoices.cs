namespace Client.Core;

// Trench-maps stage S7: the preset maps a player can pick when creating a match, besides the map the scenario name
// comes with. The server holds the maps (content/maps) and checks the pick (CreateMatch.Map, stage S5); this list only
// offers them, and a test checks it matches the server's maps.
public sealed record MapChoice(string Id, string Name, string ScenarioId);

public static class MapChoices
{
    public const string ClaimNoMansLand = "claim-no-mans-land";
    public const string TrenchWarfare = "trench-warfare";

    public static IReadOnlyList<MapChoice> All { get; } =
    [
        new("ruined-village", "Ruined Village", ClaimNoMansLand),
        new("shell-holes", "Shell Holes", ClaimNoMansLand),
        new("crossfire", "Crossfire", ClaimNoMansLand),
        new("dust", "Dust", TrenchWarfare),
        new("broken-line", "Broken Line", TrenchWarfare),
    ];

    // The scenario a create-match name plays ("melee-2v2-trench-warfare" is Trench Warfare).
    public static string ScenarioIdOf(string scenario) =>
        scenario.EndsWith(ContentNames.TrenchWarfareSuffix, StringComparison.Ordinal) ? TrenchWarfare : ClaimNoMansLand;

    // The presets that fit the scenario of a create-match name, in list order.
    public static IReadOnlyList<MapChoice> For(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var id = ScenarioIdOf(scenario);
        return [.. All.Where(m => m.ScenarioId == id)];
    }

    public const string DefaultLabel = "The scenario's own map";
}
