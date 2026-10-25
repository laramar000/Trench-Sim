using Rules.Core.State;
using Sim;

namespace Match.Server;

// The scenarios this server offers, from the content files next to the program.
public static class ScenarioCatalog
{
    public static IReadOnlyDictionary<string, MatchSetup> FromContent() =>
        ScenarioSetups.FromFolder(Path.Combine(AppContext.BaseDirectory, "content"));

    public static MatchSetup Setup(string contentPath) => ScenarioSetups.Setup(contentPath);

    // Trench-maps stage S5: the content maps, loaded and checked once per process.
    public static IReadOnlyDictionary<string, MapDefinition> Maps => LazyMaps.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, MapDefinition>> LazyMaps =
        new(() => ScenarioSetups.Maps(Path.Combine(AppContext.BaseDirectory, "content")));
}
