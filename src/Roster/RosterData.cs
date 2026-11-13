namespace Roster;

// Roster stage 5c (rosters online): the shipped roster data as a server holds it, and the server's own check of a
// roster a player sends. The server never trusts the client's check: it parses the roster, finds the list and runs
// RosterValidator and MatchReadiness itself, with its own catalogue.
public sealed class RosterData
{
    // A roster's name and its models' names are shown to the opponent: short, printable text only.
    public const int MaxNameLength = 60;

    private readonly Catalogue _catalogue;
    private readonly IReadOnlyList<Faction> _factions;

    public RosterData(Catalogue catalogue, IReadOnlyList<Faction> factions)
    {
        _catalogue = catalogue;
        _factions = factions;
    }

    // The keyword list, catalogue and every faction file of a content/roster folder (practice-*.json are left out).
    public static Checked<RosterData> Load(string folder)
    {
        var vocabulary = KeywordVocabulary.Load(Path.Combine(folder, "keywords.json"));
        if (vocabulary.Value is not { } words)
        {
            return new(null, vocabulary.Problems);
        }
        var catalogue = Catalogue.Load(Path.Combine(folder, "catalogue.json"), words);
        if (catalogue.Value is not { } kit)
        {
            return new(null, catalogue.Problems);
        }
        var factions = new List<Faction>();
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(folder, "factions"), "*.json").Order(StringComparer.Ordinal))
        {
            var faction = Faction.Load(file, kit, words);
            if (faction.Value is { } f)
            {
                factions.Add(f);
            }
            else
            {
                problems.AddRange(faction.Problems.Select(p => $"{Path.GetFileName(file)}: {p}"));
            }
        }
        return problems.Count > 0 ? new(null, problems) : new(new RosterData(kit, factions), []);
    }

    public static string DefaultFolder() => Path.Combine(AppContext.BaseDirectory, "content", "roster");

    public sealed record Entry(string? Name, MatchReady? Ready, string? Problem);

    // A roster file's text as a player sent it: the roster's name and what it plays as, or why it cannot be played.
    public Entry Check(string json)
    {
        var parsed = WarbandRoster.Parse(json);
        if (parsed.Value is not { } roster)
        {
            return Refuse("The roster cannot be read: " + string.Join("; ", parsed.Problems.Take(3)));
        }
        if (BadName(roster.Name) || roster.Models.Any(m => m.Name is { } n && BadName(n)))
        {
            return Refuse($"A roster's name and its models' names are at most {MaxNameLength} printable characters.");
        }
        if (_factions.FirstOrDefault(f => f.Id == roster.Faction) is not { } faction)
        {
            return Refuse($"The roster {roster.Name} is for a faction this server does not have ({roster.Faction}).");
        }
        if (roster.Variant is not null && faction.Variants.All(v => v.Id != roster.Variant))
        {
            return Refuse($"The roster {roster.Name} is for a list {faction.Name} does not have here ({roster.Variant}).");
        }
        var ready = MatchReadiness.Check(roster, faction, _catalogue);
        if (ready.RosterProblems.Count > 0)
        {
            return Refuse($"The roster {roster.Name} is not legal: {ready.RosterProblems[0]}");
        }
        if (ready.Refused.Count > 0)
        {
            return Refuse($"The roster {roster.Name} has what the game cannot play yet: {string.Join("; ", ready.Refused.Take(3))}");
        }
        if (ready.Models.Count == 0)
        {
            return Refuse($"The roster {roster.Name} has no models.");
        }
        return new(roster.Name, ready, null);
    }

    private static Entry Refuse(string problem) => new(null, null, problem);

    private static bool BadName(string name) =>
        name.Length is 0 or > MaxNameLength || name.Any(char.IsControl);
}
