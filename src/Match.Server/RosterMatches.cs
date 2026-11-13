using Roster;
using Sim;

namespace Match.Server;

// Roster stage 5c (rosters online): what the server needs to set up a match from the rosters players send: its own
// roster data (it checks every roster itself) and the map a roster match is played on unless another is chosen.
public sealed class RosterMatches(RosterData data, MapDefinition plainMap)
{
    public RosterData Data { get; } = data;
    public MapDefinition PlainMap { get; } = plainMap;

    // The shipped roster data next to the program and the server's plain map; null when the data is missing or bad,
    // and then rosters are refused by name.
    public static RosterMatches? FromContent(IReadOnlyDictionary<string, MapDefinition> maps, Action<string>? log = null)
    {
        if (!maps.TryGetValue(ScenarioSetups.PlainMap, out var plain))
        {
            log?.Invoke("Rosters online are off: the plain map is missing.");
            return null;
        }
        var folder = RosterData.DefaultFolder();
        if (!Directory.Exists(folder))
        {
            log?.Invoke($"Rosters online are off: no roster data at {folder}.");
            return null;
        }
        var loaded = RosterData.Load(folder);
        if (loaded.Value is not { } data)
        {
            log?.Invoke("Rosters online are off: the roster data is invalid: " + string.Join("; ", loaded.Problems.Take(3)));
            return null;
        }
        return new RosterMatches(data, plain);
    }
}

// Seat One's roster while a roster match waits for its second player, and the map it is played on.
internal sealed record WaitingRoster(string Name, MatchReady Ready, MapDefinition Map);
