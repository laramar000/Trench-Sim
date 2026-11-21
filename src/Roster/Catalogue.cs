using System.Text.Json;
using System.Text.RegularExpressions;

namespace Roster;

public enum KitCategory { Ranged, Melee, Grenade, Shield, Armour, Equipment, Special }

public enum Currency { Ducats, Glory }

// verified: every value checked against its source page. unverified: entered but not checked, or a value is still
// missing. discrepancy: two sources disagree and Jason has not ruled yet (the entry carries a note saying how).
public enum DataStatus { Verified, Unverified, Discrepancy }

// The naming policy's verdict for the display name: keep a generic name, rename a distinctive one, or review.
public enum Naming { Keep, Rename, Review }

public enum RestrictionKind { EliteOnly, Limit, ShieldCombo, Other }

public sealed record Cost(int Amount, Currency Currency);

public sealed record KeywordUse(string Id, int? Number, string? Text);

public sealed record SourceRef(string Book, string Version, int Page);

public sealed record RuleRef(string Summary, int Page);

public sealed record Restriction(RestrictionKind Kind, int? Value, string? Text);

// One piece of battlekit. Name is our display name; the source's own name is kept outside the repository.
// A null Cost means the value is unresolved: never guessed, and never allowed in a match roster.
public sealed record CatalogueEntry(
    string Id,
    string Name,
    KitCategory Category,
    int? Hands,
    int? RangeInches,
    bool AlsoMelee,
    Cost? Cost,
    IReadOnlyList<KeywordUse> Keywords,
    int Dice,
    int InjuryDice,
    int InjuryModifier,
    RuleRef? Rule,
    IReadOnlyList<Restriction> Restrictions,
    SourceRef Source,
    DataStatus Status,
    string? Note,
    Naming Naming,
    bool CampaignOnly);

public sealed class Catalogue
{
    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, CatalogueEntry> _byId;

    private Catalogue(IReadOnlyList<CatalogueEntry> entries)
    {
        Entries = entries;
        _byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<CatalogueEntry> Entries { get; }

    public CatalogueEntry? Find(string id) => _byId.GetValueOrDefault(id);

    public static Checked<Catalogue> Load(string path, KeywordVocabulary vocabulary) => Parse(File.ReadAllText(path), vocabulary);

    public static Checked<Catalogue> Parse(string json, KeywordVocabulary vocabulary)
    {
        var problems = new List<string>();
        var entries = new List<CatalogueEntry>();
        if (!Json.TryParse(json, problems, out var doc))
            return new(null, problems);
        using (doc)
        {
            var root = doc!.RootElement;
            Json.CheckHeader(root, "catalogue", problems);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (e, item) in Json.Items(root, "entries", "catalogue", problems))
            {
                var id = Json.Text(e, "id", item, problems);
                var at = id is null ? item : $"entry \"{id}\"";
                var before = problems.Count;
                var entry = ReadEntry(e, id, at, vocabulary, problems);
                if (id is not null && !ids.Add(id))
                    problems.Add($"{at}: the id is used twice");
                if (entry is not null && !names.Add(entry.Name))
                    problems.Add($"{at}: the name \"{entry.Name}\" is used by another entry");
                if (entry is not null && problems.Count == before)
                    entries.Add(entry);
            }
        }
        return new(problems.Count == 0 ? new Catalogue(entries) : null, problems);
    }

    private static CatalogueEntry? ReadEntry(JsonElement e, string? id, string at, KeywordVocabulary vocabulary, List<string> problems)
    {
        if (id is not null && !IdPattern.IsMatch(id))
            problems.Add($"{at}: an id is lower-case letters and digits joined by single hyphens");
        var name = Json.Text(e, "name", at, problems);
        if (name is not null && name != name.Trim())
            problems.Add($"{at}: the name has spaces at its start or end");
        var category = Json.Choice<KitCategory>(e, "category", at, problems);
        var hands = Json.Whole(e, "hands", at, problems, required: false);
        var range = Json.Whole(e, "rangeInches", at, problems, required: false);
        var alsoMelee = Json.Flag(e, "alsoMelee", at, problems) ?? false;
        var cost = ReadCost(e, at, problems);
        var keywords = ReadKeywords(e, at, vocabulary, problems);
        var dice = Json.Whole(e, "dice", at, problems, required: false) ?? 0;
        var injuryDice = Json.Whole(e, "injuryDice", at, problems, required: false) ?? 0;
        var injuryModifier = Json.Whole(e, "injuryModifier", at, problems, required: false) ?? 0;
        var rule = ReadRule(e, at, problems);
        var restrictions = ReadRestrictions(e, at, problems);
        var source = ReadSource(e, at, problems);
        var status = Json.Choice<DataStatus>(e, "status", at, problems);
        var note = Json.Text(e, "note", at, problems, required: false);
        var naming = Json.Choice<Naming>(e, "naming", at, problems);
        var campaignOnly = Json.Flag(e, "campaignOnly", at, problems) ?? false;

        if (category is { } c)
        {
            var weapon = c is KitCategory.Ranged or KitCategory.Melee;
            if (weapon && hands is null)
                problems.Add($"{at}: a {Json.CamelCase(c.ToString())} weapon needs \"hands\" (1 or 2)");
            if (hands is not null and not (1 or 2))
                problems.Add($"{at}: \"hands\" must be 1 or 2");
            if (hands is not null && c is KitCategory.Grenade or KitCategory.Armour or KitCategory.Equipment)
                problems.Add($"{at}: {Json.CamelCase(c.ToString())} does not take \"hands\"");
            if (c is KitCategory.Ranged or KitCategory.Grenade && range is null)
                problems.Add($"{at}: a {Json.CamelCase(c.ToString())} entry needs \"rangeInches\"");
            if (range is not null && c is not (KitCategory.Ranged or KitCategory.Grenade or KitCategory.Special))
                problems.Add($"{at}: only ranged, grenade and special entries have a range");
            if (alsoMelee && c != KitCategory.Ranged)
                problems.Add($"{at}: only a ranged weapon can also be used in melee");
        }
        if (range is not null and <= 0)
            problems.Add($"{at}: \"rangeInches\" must be 1 or more");
        if (injuryModifier is < -3 or > 3 || dice is < -3 or > 3 || injuryDice is < -3 or > 3)
            problems.Add($"{at}: dice and modifiers must be between -3 and +3");
        if (status == DataStatus.Discrepancy && note is null)
            problems.Add($"{at}: a discrepancy needs a \"note\" saying what the sources disagree on");

        if (id is null || name is null || category is null || source is null || status is null || naming is null)
            return null;
        return new CatalogueEntry(id, name, category.Value, hands, range, alsoMelee, cost, keywords, dice, injuryDice,
            injuryModifier, rule, restrictions, source, status.Value, note, naming.Value, campaignOnly);
    }

    internal static Cost? ReadCost(JsonElement e, string at, List<string> problems)
    {
        if (!e.TryGetProperty("cost", out var c) || c.ValueKind == JsonValueKind.Null)
            return null;
        if (c.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{at}: \"cost\" must be an object with \"amount\" and \"currency\", or null while unresolved");
            return null;
        }
        var amount = Json.Whole(c, "amount", $"{at} cost", problems);
        var currency = Json.Choice<Currency>(c, "currency", $"{at} cost", problems);
        if (amount is < 0)
            problems.Add($"{at}: a cost cannot be negative");
        return amount is null || currency is null ? null : new Cost(amount.Value, currency.Value);
    }

    internal static List<KeywordUse> ReadKeywords(JsonElement e, string at, KeywordVocabulary vocabulary, List<string> problems)
    {
        var uses = new List<KeywordUse>();
        if (!e.TryGetProperty("keywords", out var list) || list.ValueKind == JsonValueKind.Null)
            return uses;
        if (list.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{at}: \"keywords\" must be a list");
            return uses;
        }
        foreach (var k in list.EnumerateArray())
        {
            var id = k.ValueKind == JsonValueKind.Object ? Json.Text(k, "id", $"{at} keyword", problems) : null;
            if (k.ValueKind != JsonValueKind.Object)
                problems.Add($"{at}: each keyword must be an object with an \"id\"");
            if (id is null)
                continue;
            var definition = vocabulary.Find(id);
            if (definition is null)
            {
                problems.Add($"{at}: unknown keyword \"{id}\"");
                continue;
            }
            k.TryGetProperty("value", out var v);
            var hasValue = v.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
            switch (definition.Value)
            {
                case KeywordValue.None when hasValue:
                    problems.Add($"{at}: keyword {id} takes no value");
                    break;
                case KeywordValue.Number or KeywordValue.Inches when !hasValue || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n <= 0:
                    problems.Add($"{at}: keyword {id} needs a whole number of 1 or more as its value");
                    break;
                case KeywordValue.Text when !hasValue || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()):
                    problems.Add($"{at}: keyword {id} needs text as its value");
                    break;
                default:
                    if (uses.Any(u => u.Id == id && definition.Value != KeywordValue.Text))
                        problems.Add($"{at}: keyword {id} is listed twice");
                    uses.Add(new KeywordUse(id,
                        definition.Value is KeywordValue.Number or KeywordValue.Inches ? v.GetInt32() : null,
                        definition.Value == KeywordValue.Text ? v.GetString() : null));
                    break;
            }
        }
        return uses;
    }

    private static RuleRef? ReadRule(JsonElement e, string at, List<string> problems)
    {
        if (!e.TryGetProperty("rule", out var r) || r.ValueKind == JsonValueKind.Null)
            return null;
        var summary = Json.Text(r, "summary", $"{at} rule", problems);
        var page = Json.Whole(r, "page", $"{at} rule", problems);
        if (page is <= 0)
            problems.Add($"{at}: the rule's page must be 1 or more");
        return summary is null || page is null ? null : new RuleRef(summary, page.Value);
    }

    private static List<Restriction> ReadRestrictions(JsonElement e, string at, List<string> problems)
    {
        var list = new List<Restriction>();
        if (!e.TryGetProperty("restrictions", out var rs) || rs.ValueKind == JsonValueKind.Null)
            return list;
        if (rs.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{at}: \"restrictions\" must be a list");
            return list;
        }
        foreach (var r in rs.EnumerateArray())
        {
            var kind = Json.Choice<RestrictionKind>(r, "kind", $"{at} restriction", problems);
            var value = Json.Whole(r, "value", $"{at} restriction", problems, required: kind == RestrictionKind.Limit);
            var text = Json.Text(r, "text", $"{at} restriction", problems, required: kind == RestrictionKind.Other);
            if (kind == RestrictionKind.Limit && value is <= 0)
                problems.Add($"{at}: a limit must be 1 or more");
            if (kind is { } k)
                list.Add(new Restriction(k, value, text));
        }
        return list;
    }

    internal static SourceRef? ReadSource(JsonElement e, string at, List<string> problems)
    {
        if (!e.TryGetProperty("source", out var s) || s.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{at}: \"source\" (book, version, page) is missing");
            return null;
        }
        var book = Json.Text(s, "book", $"{at} source", problems);
        var version = Json.Text(s, "version", $"{at} source", problems);
        var page = Json.Whole(s, "page", $"{at} source", problems);
        if (page is <= 0)
            problems.Add($"{at}: the source page must be 1 or more");
        return book is null || version is null || page is null ? null : new SourceRef(book, version, page.Value);
    }
}
