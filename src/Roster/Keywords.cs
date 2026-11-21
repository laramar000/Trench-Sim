using System.Text.Json;

namespace Roster;

public enum KeywordKind { Effect, Tag }

// What follows a keyword's name: nothing (CRITICAL), a whole number (AUTOMATIC 2), a distance in inches (BLAST 3")
// or a word or phrase (NEGATE GAS, AMMUNITION (FIRE), IGNORE COVER).
public enum KeywordValue { None, Number, Inches, Text }

public sealed record KeywordDefinition(string Id, KeywordKind Kind, KeywordValue Value, int Page);

// The fixed keyword vocabulary (content/roster/keywords.json): a catalogue entry may only use keywords listed here.
// +/- DICE, +/- INJURY DICE and +/- INJURY MODIFIER are not in it: entries carry them as numbers.
public sealed class KeywordVocabulary
{
    private readonly Dictionary<string, KeywordDefinition> _byId;

    public KeywordVocabulary(IEnumerable<KeywordDefinition> keywords) =>
        _byId = keywords.ToDictionary(k => k.Id, StringComparer.Ordinal);

    public IReadOnlyCollection<KeywordDefinition> All => _byId.Values;

    public KeywordDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    public static string DefaultPath() => Path.Combine(AppContext.BaseDirectory, "content", "roster", "keywords.json");

    public static Checked<KeywordVocabulary> Load(string path) => Parse(File.ReadAllText(path));

    public static Checked<KeywordVocabulary> Parse(string json)
    {
        var problems = new List<string>();
        var keywords = new List<KeywordDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!Json.TryParse(json, problems, out var doc))
            return new(null, problems);
        using (doc)
        {
            var root = doc!.RootElement;
            Json.CheckHeader(root, "keyword list", problems);
            foreach (var (k, at) in Json.Items(root, "keywords", "keyword list", problems))
            {
                var id = Json.Text(k, "id", at, problems);
                var kind = Json.Choice<KeywordKind>(k, "kind", at, problems);
                var value = Json.Choice<KeywordValue>(k, "value", at, problems);
                var page = Json.Whole(k, "page", at, problems);
                if (id is null || kind is null || value is null || page is null)
                    continue;
                if (id != id.Trim().ToUpperInvariant() || id.Length == 0)
                    problems.Add($"{at}: keyword \"{id}\" must be written in capitals with no spaces around it");
                if (page <= 0)
                    problems.Add($"{at}: page must be 1 or more");
                if (!seen.Add(id))
                    problems.Add($"keyword \"{id}\" is listed twice");
                keywords.Add(new KeywordDefinition(id, kind.Value, value.Value, page.Value));
            }
        }
        return new(problems.Count == 0 ? new KeywordVocabulary(keywords) : null, problems);
    }
}

// A loaded file and every problem found in it. Value is null when there is any problem: nothing half-checked is used.
public sealed record Checked<T>(T? Value, IReadOnlyList<string> Problems) where T : class
{
    public bool Ok => Value is not null;
}
