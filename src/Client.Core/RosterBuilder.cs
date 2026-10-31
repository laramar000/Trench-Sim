using Roster;

namespace Client.Core;

// Roster builder stage 3: what the Build Your Own Roster screen shows and does, as plain C# tested without Godot.
// It holds one warband being built, works out its legality with Roster.RosterValidator, and saves and loads roster
// files in a folder. Until the real lists are entered (stage 4) the only faction is the invented practice one.
public sealed class RosterBuilder
{
    public sealed record FactionChoice(string Id, string Name);

    public sealed record EntryRow(string Id, string Name, string CostText, string LimitText);

    public sealed record ModelRow(int Index, string Title, string CostText);

    public sealed record KitRow(string Id, string Name, string Category, string CostText, string Rules);

    public sealed record CarriedRow(int Position, string Id, string Name, string CostText);

    public sealed record UpgradeRow(string Id, string Name, string CostText, bool Bought);

    private readonly Catalogue _catalogue;
    private readonly IReadOnlyList<Faction> _factions;
    private readonly List<RosterModel> _models = [];

    public RosterBuilder(Catalogue catalogue, IReadOnlyList<Faction> factions)
    {
        if (factions.Count == 0)
        {
            throw new ArgumentException("The builder needs at least one faction.", nameof(factions));
        }
        _catalogue = catalogue;
        _factions = factions;
        Faction = factions[0];
        Name = "New warband";
    }

    // The builder with the data shipped in Client.Core: the keyword list, the practice catalogue and faction.
    public static RosterBuilder Shipped()
    {
        var vocabulary = Must(KeywordVocabulary.Parse(ContentNames.Read("roster/keywords.json")));
        var catalogue = Must(Catalogue.Parse(ContentNames.Read("roster/practice-catalogue.json"), vocabulary));
        var faction = Must(Faction.Parse(ContentNames.Read("roster/practice-faction.json"), catalogue, vocabulary));
        return new RosterBuilder(catalogue, [faction]);
    }

    private static T Must<T>(Checked<T> loaded) where T : class =>
        loaded.Value ?? throw new InvalidOperationException("Shipped roster data is invalid: " + string.Join("; ", loaded.Problems));

    public const string PracticeNote = "Practice data: invented factions and battlekit, until the real lists are entered.";

    public Faction Faction { get; private set; }

    public string? Variant { get; private set; }

    public string Name { get; set; }

    public int Selected { get; private set; } = -1;

    public IReadOnlyList<FactionChoice> Factions => _factions.Select(f => new FactionChoice(f.Id, f.Name)).ToList();

    // The standard list first (null), then each variant.
    public IReadOnlyList<FactionChoice?> Variants =>
        [null, .. Faction.Variants.Select(v => new FactionChoice(v.Id, v.Name))];

    public string ListName => Variant is null ? Faction.Name : Faction.Variants.First(v => v.Id == Variant).Name;

    private (IReadOnlyList<ModelEntry> Models, IReadOnlyList<ArmouryItem> Armoury) Shown => Faction.As(Variant)!.Value;

    // Choosing another faction or list starts an empty warband: its models may not exist there.
    public void ChooseFaction(string id)
    {
        Faction = _factions.First(f => f.Id == id);
        Variant = null;
        Clear();
    }

    public void ChooseVariant(string? id)
    {
        if (id is not null && Faction.Variants.All(v => v.Id != id))
        {
            throw new ArgumentException($"{Faction.Name} has no variant {id}.", nameof(id));
        }
        Variant = id;
        Clear();
    }

    public void Clear()
    {
        _models.Clear();
        Selected = -1;
    }

    public IReadOnlyList<EntryRow> Entries => Shown.Models.Select(e => new EntryRow(e.Id, e.Name, CostText(e.Cost), LimitText(e))).ToList();

    private static string LimitText(ModelEntry e) => (e.Min, e.Max) switch
    {
        (1, 1) => "required, 1",
        (0, null) => "any number",
        (0, { } max) => $"0-{max}",
        ({ } min, null) => $"at least {min}",
        ({ } min, { } max) => $"{min}-{max}",
    };

    public static string CostText(Cost? cost) => cost is null ? "cost not verified" :
        cost.Currency == Currency.Ducats ? $"{cost.Amount} ducats" : $"{cost.Amount} Glory";

    public int Add(string entryId)
    {
        if (Shown.Models.All(e => e.Id != entryId))
        {
            throw new ArgumentException($"{ListName} has no entry {entryId}.", nameof(entryId));
        }
        _models.Add(new RosterModel(entryId, null, [], []));
        Selected = _models.Count - 1;
        return Selected;
    }

    public void Remove(int index)
    {
        _models.RemoveAt(index);
        if (Selected > index)
        {
            Selected--;
        }
        else if (Selected == index)
        {
            Selected = Math.Min(index, _models.Count - 1);
        }
    }

    public void Select(int index) => Selected = index >= 0 && index < _models.Count ? index : -1;

    private ModelEntry EntryOf(RosterModel m) => Shown.Models.First(e => e.Id == m.Entry);

    // What one model costs with its kit and upgrades, as the validator prices them (null when a piece has no cost yet).
    private int? CostOf(RosterModel m)
    {
        var check = RosterValidator.Check(Roster with { Models = [m], BudgetDucats = int.MaxValue, BudgetGlory = int.MaxValue }, Faction, _catalogue);
        return check.Problems.Any(p => p.Contains("no verified cost", StringComparison.Ordinal)) ? null : check.Ducats;
    }

    public IReadOnlyList<ModelRow> Models => _models.Select((m, i) =>
    {
        var cost = CostOf(m);
        var kit = m.Kit.Count == 0 ? "" : ": " + string.Join(", ", m.Kit.Select(k => _catalogue.Find(k)!.Name));
        return new ModelRow(i, $"{i + 1}. {EntryOf(m).Name}{kit}", cost is null ? "cost not verified" : $"{cost} ducats");
    }).ToList();

    // The battlekit the selected model may be offered, with its price for that model (the model's own price first).
    public IReadOnlyList<KitRow> KitChoices
    {
        get
        {
            if (Selected < 0)
            {
                return [];
            }
            var entry = EntryOf(_models[Selected]);
            var armoury = Shown.Armoury;
            IEnumerable<string> ids = entry.Access switch
            {
                KitAccess.Armoury => armoury.Select(a => a.Kit).Concat(entry.Prices.Select(p => p.Kit)).Distinct(),
                KitAccess.Listed => entry.Listed,
                _ => [],
            };
            return ids.Select(id =>
            {
                var kit = _catalogue.Find(id)!;
                var from = armoury.FirstOrDefault(a => a.Kit == id);
                var cost = entry.Prices.FirstOrDefault(p => p.Kit == id)?.Cost ?? from?.Cost;
                return new KitRow(id, kit.Name, kit.Category.ToString(), CostText(cost), RulesText(kit, from));
            }).ToList();
        }
    }

    private static string RulesText(CatalogueEntry kit, ArmouryItem? from)
    {
        var parts = new List<string>();
        if (kit.Hands is { } hands)
        {
            parts.Add($"{hands}-handed");
        }
        if (kit.RangeInches is { } range)
        {
            parts.Add(kit.AlsoMelee ? $"{range}\"/melee" : $"{range}\"");
        }
        parts.AddRange(kit.Keywords.Select(k => k.Number is { } n ? $"{k.Id} {n}" : k.Text is { } t ? $"{k.Id} {t}" : k.Id));
        if (kit.InjuryModifier != 0)
        {
            parts.Add($"{kit.InjuryModifier:+0;-0} injury");
        }
        if (from?.Limit is { } limit)
        {
            parts.Add($"limit {limit}");
        }
        if (from?.Only.Count > 0)
        {
            parts.Add("only " + string.Join(" or ", from.Only.Select(o => o.Keyword ?? o.Model)));
        }
        return string.Join(", ", parts);
    }

    public IReadOnlyList<CarriedRow> Carried
    {
        get
        {
            if (Selected < 0)
            {
                return [];
            }
            var m = _models[Selected];
            var entry = EntryOf(m);
            var rows = entry.StartsWith.Select(id => new CarriedRow(-1, id, _catalogue.Find(id)!.Name, "included")).ToList();
            rows.AddRange(m.Kit.Select((id, i) => new CarriedRow(i, id, _catalogue.Find(id)!.Name,
                CostText(entry.Prices.FirstOrDefault(p => p.Kit == id)?.Cost ?? Shown.Armoury.FirstOrDefault(a => a.Kit == id)?.Cost))));
            return rows;
        }
    }

    public IReadOnlyList<UpgradeRow> Upgrades => Selected < 0 ? [] :
        EntryOf(_models[Selected]).Upgrades.Select(u => new UpgradeRow(u.Id, u.Name, CostText(u.Cost), _models[Selected].Upgrades.Contains(u.Id))).ToList();

    public void AddKit(string kitId)
    {
        var m = _models[Selected];
        _models[Selected] = m with { Kit = [.. m.Kit, kitId] };
    }

    public void RemoveKit(int position)
    {
        var m = _models[Selected];
        var kit = m.Kit.ToList();
        kit.RemoveAt(position);
        _models[Selected] = m with { Kit = kit };
    }

    public void ToggleUpgrade(string upgradeId)
    {
        var m = _models[Selected];
        _models[Selected] = m with
        {
            Upgrades = m.Upgrades.Contains(upgradeId) ? m.Upgrades.Where(u => u != upgradeId).ToList() : [.. m.Upgrades, upgradeId],
        };
    }

    public WarbandRoster Roster => new(Name, Faction.Id, Variant, Faction.BudgetDucats, 0, _models.ToList());

    public RosterCheck Check() => RosterValidator.Check(Roster, Faction, _catalogue);

    public string TotalText
    {
        get
        {
            var check = Check();
            return $"{check.Ducats} of {Faction.BudgetDucats} ducats" + (check.Glory > 0 ? $", {check.Glory} Glory" : "") +
                   (check.Legal ? " · legal" : $" · {check.Problems.Count} problem{(check.Problems.Count == 1 ? "" : "s")}");
        }
    }

    // The problems in full, then the notes, with the many "not verified" notes folded into one line.
    public IReadOnlyList<string> CheckLines
    {
        get
        {
            var check = Check();
            var unverified = check.Notes.Count(n => n.EndsWith("not verified against the book yet", StringComparison.Ordinal));
            var lines = check.Problems.Select(p => "✕ " + Sentence(p)).ToList();
            lines.AddRange(check.Notes.Where(n => !n.EndsWith("not verified against the book yet", StringComparison.Ordinal)).Select(n => "• " + Sentence(n)));
            if (unverified > 0)
            {
                lines.Add($"• {unverified} entr{(unverified == 1 ? "y is" : "ies are")} not verified against the book yet.");
            }
            return lines;
        }
    }

    private static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..] + (s.EndsWith('.') ? "" : ".");

    // ---- saving and loading: one roster file per warband, named after it ----

    public static string FileNameFor(string name)
    {
        var safe = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray()).Trim();
        return (safe.Length == 0 ? "warband" : safe) + ".roster.json";
    }

    public string Save(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileNameFor(Name));
        File.WriteAllText(path, Roster.ToJson());
        return path;
    }

    public static IReadOnlyList<string> Saved(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.roster.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    // Loads a saved roster; null and the reason when the file cannot be used here.
    public string? Load(string folder, string file)
    {
        var loaded = WarbandRoster.Parse(File.ReadAllText(Path.Combine(folder, file)));
        if (loaded.Value is not { } roster)
        {
            return "This file cannot be read: " + string.Join("; ", loaded.Problems);
        }
        if (_factions.FirstOrDefault(f => f.Id == roster.Faction) is not { } faction)
        {
            return $"This roster is for a faction this version does not have ({roster.Faction}).";
        }
        if (roster.Variant is not null && faction.Variants.All(v => v.Id != roster.Variant))
        {
            return $"This roster is for a list {faction.Name} does not have ({roster.Variant}).";
        }
        var entries = faction.As(roster.Variant)!.Value.Models;
        if (roster.Models.FirstOrDefault(m => entries.All(e => e.Id != m.Entry)) is { } unknown)
        {
            return $"This roster has a model this list does not have ({unknown.Entry}).";
        }
        if (roster.Models.SelectMany(m => m.Kit).FirstOrDefault(k => _catalogue.Find(k) is null) is { } kit)
        {
            return $"This roster has battlekit this version does not have ({kit}).";
        }
        Faction = faction;
        Variant = roster.Variant;
        Name = roster.Name;
        _models.Clear();
        _models.AddRange(roster.Models);
        Selected = _models.Count > 0 ? 0 : -1;
        return null;
    }
}
