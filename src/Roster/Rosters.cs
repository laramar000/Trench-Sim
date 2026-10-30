using System.Text.Json;

namespace Roster;

// One recruited model: which warband entry, an optional name the player gives it, the battlekit bought for it (by
// catalogue id; a piece may appear twice, e.g. two pistols) and the upgrades bought for it.
public sealed record RosterModel(string Entry, string? Name, IReadOnlyList<string> Kit, IReadOnlyList<string> Upgrades);

public sealed record WarbandRoster(
    string Name,
    string Faction,
    string? Variant,
    int BudgetDucats,
    int BudgetGlory,
    IReadOnlyList<RosterModel> Models)
{
    public static Checked<WarbandRoster> Parse(string json)
    {
        var problems = new List<string>();
        if (!Json.TryParse(json, problems, out var doc))
            return new(null, problems);
        using (doc)
        {
            var root = doc!.RootElement;
            Json.CheckHeader(root, "roster", problems);
            if (root.ValueKind != JsonValueKind.Object)
                return new(null, problems);
            var name = Json.Text(root, "name", "roster", problems);
            var faction = Json.Text(root, "faction", "roster", problems);
            var variant = Json.Text(root, "variant", "roster", problems, required: false);
            var ducats = Json.Whole(root, "budgetDucats", "roster", problems);
            var glory = Json.Whole(root, "budgetGlory", "roster", problems, required: false) ?? 0;
            if (ducats is < 0 || glory < 0)
                problems.Add("roster: a budget cannot be negative");
            var models = new List<RosterModel>();
            foreach (var (m, at) in Json.Items(root, "models", "roster", problems))
            {
                var entry = Json.Text(m, "entry", at, problems);
                var given = Json.Text(m, "name", at, problems, required: false);
                var kit = Json.Strings(m, "kit", at, problems);
                var upgrades = Json.Strings(m, "upgrades", at, problems);
                if (entry is not null)
                    models.Add(new RosterModel(entry, given, kit, upgrades));
            }
            if (name is null || faction is null || ducats is null || problems.Count > 0)
                return new(null, problems);
            return new(new WarbandRoster(name, faction, variant, ducats.Value, glory, models), problems);
        }
    }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Json.Format);
            w.WriteString("rulesVersion", Json.RulesVersion);
            w.WriteString("name", Name);
            w.WriteString("faction", Faction);
            if (Variant is not null)
                w.WriteString("variant", Variant);
            w.WriteNumber("budgetDucats", BudgetDucats);
            if (BudgetGlory != 0)
                w.WriteNumber("budgetGlory", BudgetGlory);
            w.WriteStartArray("models");
            foreach (var m in Models)
            {
                w.WriteStartObject();
                w.WriteString("entry", m.Entry);
                if (m.Name is not null)
                    w.WriteString("name", m.Name);
                w.WriteStartArray("kit");
                foreach (var k in m.Kit)
                    w.WriteStringValue(k);
                w.WriteEndArray();
                if (m.Upgrades.Count > 0)
                {
                    w.WriteStartArray("upgrades");
                    foreach (var u in m.Upgrades)
                        w.WriteStringValue(u);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
