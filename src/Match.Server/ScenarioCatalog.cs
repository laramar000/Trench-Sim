using Rules.Core.State;
using Sim;

namespace Match.Server;

// The scenarios this server offers, from the content files next to the program.
public static class ScenarioCatalog
{
    public static IReadOnlyDictionary<string, MatchSetup> FromContent() =>
        ScenarioSetups.FromFolder(Path.Combine(AppContext.BaseDirectory, "content"));

    public static MatchSetup Setup(string contentPath) => ScenarioSetups.Setup(contentPath);

    // Roster stage 5b: the content scenarios plus "roster", the two warbands of a content file written from rosters
    // (Roster.MatchReady), on the plain map; a map can still be chosen when the match is created.
    public const string RosterScenario = "roster";

    public static IReadOnlyDictionary<string, MatchSetup> WithRosterMatch(string warbandsPath)
    {
        var all = new Dictionary<string, MatchSetup>(FromContent(), StringComparer.Ordinal);
        all[RosterScenario] = ScenarioSetups.Setup(warbandsPath, Maps[ScenarioSetups.PlainMap]);
        return all;
    }

    // Trench-maps stage S5: the content maps, loaded and checked once per process.
    public static IReadOnlyDictionary<string, MapDefinition> Maps => LazyMaps.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, MapDefinition>> LazyMaps =
        new(() => ScenarioSetups.Maps(Path.Combine(AppContext.BaseDirectory, "content")));
}
