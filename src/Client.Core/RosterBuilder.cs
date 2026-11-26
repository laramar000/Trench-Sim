using Roster;

namespace Client.Core;

// Roster builder stage 3: what the Build Your Own Roster screen shows and does, as plain C# tested without Godot.
// It holds one warband being built, works out its legality with Roster.RosterValidator, and saves and loads roster
// files in a folder. Stage 4: the shipped factions are the real lists; the invented practice data stays for tests.
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

    // The faction files shipped in Client.Core (content/roster/factions), in the order the screen lists them.
    public static readonly IReadOnlyList<string> ShippedFactions = ["principality", "pilgrims", "sultanate", "heretics", "cult", "court"];

    // The builder with the data shipped in Client.Core: the keyword list, the catalogue and the real faction lists.
    public static RosterBuilder Shipped()
    {
        var vocabulary = Must(KeywordVocabulary.Parse(ContentNames.Read("roster/keywords.json")));
        var catalogue = Must(Catalogue.Parse(ContentNames.Read("roster/catalogue.json"), vocabulary));
        var factions = ShippedFactions
            .Select(f => Must(Faction.Parse(ContentNames.Read($"roster/factions/{f}.json"), catalogue, vocabulary)))
            .ToList();
        return new RosterBuilder(catalogue, factions);
    }

    // The invented practice catalogue and faction, kept so the builder's own tests do not depend on book values.
    public static RosterBuilder Practice()
    {
        var vocabulary = Must(KeywordVocabulary.Parse(ContentNames.Read("roster/keywords.json")));
        var catalogue = Must(Catalogue.Parse(ContentNames.Read("roster/practice-catalogue.json"), vocabulary));
        var faction = Must(Faction.Parse(ContentNames.Read("roster/practice-faction.json"), catalogue, vocabulary));
        return new RosterBuilder(catalogue, [faction]);
    }

    private static T Must<T>(Checked<T> loaded) where T : class =>
        loaded.Value ?? throw new InvalidOperationException("Shipped roster data is invalid: " + string.Join("; ", loaded.Problems));

    public const string SourceNote = "Lists from the official books (rules 1.0.2), under our own names. List rules marked \"not checked by the builder\" are yours to follow.";

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
        var kit = m.Kit.Count == 0 ? "" : ": " + string.Join(", ", m.Kit.Select(KitName));
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
            var model = _models[Selected];
            var entry = EntryOf(model);
            var armoury = Shown.Armoury;
            var groups = Faction.GroupsFor(Variant);
            // Pieces from another faction's armoury come after the list's own; those that need an upgrade show once it is bought.
            bool Offered(ArmouryItem a) =>
                a.Group is not { } g || groups.FirstOrDefault(x => x.Id == g)?.NeedsUpgrade is not { } needed || model.Upgrades.Contains(needed);
            IEnumerable<string> ids = entry.Access switch
            {
                KitAccess.Armoury => armoury.Where(a => a.From is null).Select(a => a.Kit).Concat(entry.Prices.Select(p => p.Kit)).Distinct()
                    .Concat(armoury.Where(a => a.From is not null && Offered(a)).Select(a => a.Ref)),
                KitAccess.Listed => entry.Listed,
                _ => [],
            };
            return ids.Select(id =>
            {
                var (kitId, fromFaction) = KitRef.Split(id);
                var kit = _catalogue.Find(kitId)!;
                var from = armoury.FirstOrDefault(a => a.Kit == kitId && a.From == fromFaction);
                var group = groups.FirstOrDefault(g => g.Id == from?.Group);
                return new KitRow(id, KitName(id), kit.Category.ToString(), CostText(PriceOf(entry, id)), RulesText(kit, from, group));
            }).ToList();
        }
    }

    // A piece's display name; one from another faction's armoury says which.
    public string KitName(string kitRef)
    {
        var (kitId, from) = KitRef.Split(kitRef);
        var name = _catalogue.Find(kitId)?.Name ?? kitId;
        return from is null ? name : $"{name} (from {_factions.FirstOrDefault(f => f.Id == from)?.Name ?? from})";
    }

    private Cost? PriceOf(ModelEntry entry, string kitRef)
    {
        var (kitId, from) = KitRef.Split(kitRef);
        return (from is null ? entry.Prices.FirstOrDefault(p => p.Kit == kitId)?.Cost : null)
            ?? Shown.Armoury.FirstOrDefault(a => a.Kit == kitId && a.From == from)?.Cost;
    }

    private static string RulesText(CatalogueEntry kit, ArmouryItem? from, KitGroup? group)
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
        if (from?.NeedsKit is { } needs)
        {
            parts.Add($"needs {needs.Replace('-', ' ')}");
        }
        if (group is not null)
        {
            parts.Add(group.Limit is { } most ? $"{group.Name}, {most} per warband" : group.Name);
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
            rows.AddRange(m.Kit.Select((id, i) => new CarriedRow(i, id, KitName(id), CostText(PriceOf(entry, id)))));
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

    private global::Roster.Variant? ChosenVariant => Variant is null ? null : Faction.Variants.First(v => v.Id == Variant);

    // A variant may set its own starting budget (e.g. 500 ducats and 11 Glory); otherwise the faction's.
    public int BudgetDucats => ChosenVariant?.BudgetDucats ?? Faction.BudgetDucats;

    public int BudgetGlory => ChosenVariant?.BudgetGlory ?? 0;

    // The list's own special rules: the faction's, then the chosen variant's, as "Name: summary (p. N)".
    public IReadOnlyList<string> RuleLines =>
        Faction.SpecialRules.Concat(ChosenVariant?.SpecialRules ?? []).Select(r => $"{r.Name}: {r.Summary} (p. {r.Page})").ToList();

    public WarbandRoster Roster => new(Name, Faction.Id, Variant, BudgetDucats, BudgetGlory, _models.ToList());

    public RosterCheck Check() => RosterValidator.Check(Roster, Faction, _catalogue);

    public string TotalText
    {
        get
        {
            var check = Check();
            return $"{check.Ducats} of {BudgetDucats} ducats" + (check.Glory > 0 || BudgetGlory > 0 ? $", {check.Glory} of {BudgetGlory} Glory" : "") +
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

    // Roster stage 5a: what the warband becomes in a match today (MatchReadiness).
    public MatchReady MatchCheck() => MatchReadiness.Check(Roster, Faction, _catalogue);

    public IReadOnlyList<string> MatchLines
    {
        get
        {
            if (_models.Count == 0)
            {
                return [];
            }
            var ready = MatchCheck();
            var lines = new List<string>
            {
                ready.RosterProblems.Count > 0 ? "For a match: fix the problems above first."
                : ready.Refused.Count > 0 ? $"For a match: not yet; the game cannot play {ready.Refused.Count} thing{(ready.Refused.Count == 1 ? "" : "s")} below."
                : $"For a match: ready ({ready.Models.Count} model{(ready.Models.Count == 1 ? "" : "s")}). Matches with your own roster come in a later stage.",
            };
            lines.AddRange(ready.Refused.Select(r => "✕ Not playable yet: " + Sentence(r)));
            lines.AddRange(ready.PlayedWithout.Select(w => "– Played without: " + Sentence(w)));
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
        if (roster.Models.SelectMany(m => m.Kit).FirstOrDefault(k => _catalogue.Find(KitRef.Split(k).Kit) is null) is { } kit)
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
