using Roster;

namespace Client.Core;

// Roster stage 5b (hot-seat first, Jason 2026-10-31): two saved rosters become the content file of a hot-seat match.
// Each must be legal and fully playable (MatchReadiness); otherwise the reason is given by name and nothing starts.
public static class RosterMatch
{
    public sealed record Prepared(string? Json, string? Problem, IReadOnlyList<string> PlayedWithout);

    public static Prepared Prepare(string folder, string fileOne, string fileTwo)
    {
        var warbands = new List<(string, string, MatchReady)>();
        var without = new List<string>();
        foreach (var (file, seat) in new[] { (fileOne, "Seat One"), (fileTwo, "Seat Two") })
        {
            var builder = RosterBuilder.Shipped();
            if (builder.Load(folder, file) is { } problem)
            {
                return new(null, $"{seat}'s roster {file}: {problem}", []);
            }
            var ready = builder.MatchCheck();
            if (ready.RosterProblems.Count > 0)
            {
                return new(null, $"{seat}'s roster {builder.Name} is not legal: {ready.RosterProblems[0]}", []);
            }
            if (ready.Refused.Count > 0)
            {
                return new(null, $"{seat}'s roster {builder.Name} has what the game cannot play yet: {string.Join("; ", ready.Refused)}", []);
            }
            warbands.Add(($"roster-{warbands.Count + 1}", builder.Name, ready));
            without.AddRange(ready.PlayedWithout.Select(w => $"{builder.Name}: {w}"));
        }
        return new(MatchReady.ContentJson(warbands), null, without);
    }

    // Roster stage 5c (rosters online): one saved roster, checked here first so the player hears at once what is wrong;
    // Json is the roster file's text to send (the server checks it again itself).
    public static Prepared PrepareOne(string folder, string file)
    {
        var builder = RosterBuilder.Shipped();
        if (builder.Load(folder, file) is { } problem)
        {
            return new(null, $"Your roster {file}: {problem}", []);
        }
        var ready = builder.MatchCheck();
        if (ready.RosterProblems.Count > 0)
        {
            return new(null, $"Your roster {builder.Name} is not legal: {ready.RosterProblems[0]}", []);
        }
        if (ready.Refused.Count > 0)
        {
            return new(null, $"Your roster {builder.Name} has what the game cannot play yet: {string.Join("; ", ready.Refused)}", []);
        }
        var json = builder.Roster.ToJson();
        return System.Text.Encoding.UTF8.GetByteCount(json) > MaxRosterBytes
            ? new(null, $"Your roster {builder.Name} is too large to send.", [])
            : new(json, null, ready.PlayedWithout);
    }

    // What fits in one client message (Match.Protocol.ProtocolLimits.MaxClientMessageBytes) with room to spare.
    public const int MaxRosterBytes = 12 * 1024;
}
