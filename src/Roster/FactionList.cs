using System.Text.Json;
using System.Text.RegularExpressions;

namespace Roster;

public enum Alignment { Faithful, Fallen }

// How a model gets battlekit beyond what it starts with: from the whole armoury, from a fixed list only, or not at all.
public enum KitAccess { Armoury, Listed, None }

// Armoury stipulations (Warbands of Trench Crusade p. 11). BayonetLug marks a ranged weapon a bayonet can be fitted to;
// NeedsBayonetLug marks the bayonet itself.
public enum Stipulation { BayonetLug, NeedsBayonetLug, ShieldCombo, Headgear, Consumable }

// "X only": a model qualifies when it has the keyword, or comes from the named warband entry. Several = any of them.
public sealed record OnlyFor(string? Keyword, string? Model);

public sealed record ArmouryItem(
    string Kit,
    Cost? Cost,
    int? Limit,
    int? LimitPerModel,
    IReadOnlyList<OnlyFor> Only,
    IReadOnlySet<Stipulation> Stipulations,
    DataStatus Status,
    string? Note,
    string? From = null,
    string? Group = null,
    string? NeedsKit = null)
{
    // How a roster names this item: the kit id, or "kit@faction" for a piece bought from another faction's armoury.
    public string Ref => KitRef.Of(Kit, From);
}

// A roster's name for a piece of battlekit: a catalogue id, or "id@faction" when the piece comes from another
// faction's armoury (Weapon Collections and the like).
public static class KitRef
{
    public static string Of(string kit, string? from) => from is null ? kit : $"{kit}@{from}";

    public static (string Kit, string? From) Split(string reference)
    {
        var at = reference.IndexOf('@');
        return at < 0 ? (reference, null) : (reference[..at], reference[(at + 1)..]);
    }
}

// Armoury items that share one limit: "1 piece of Battlekit from the New Antioch Armoury", "one of the following
// special abilities". Limit counts pieces in the whole warband, LimitPerModel pieces on one model; NeedsUpgrade is an
// upgrade the model must have bought to take any of them.
public sealed record KitGroup(string Id, string Name, int? Limit, int? LimitPerModel, string? NeedsUpgrade, string? Note);

public sealed record KitPrice(string Kit, Cost? Cost);

public sealed record Ability(string Name, string Summary, int Page);

// Limits on the battlekit a model may be given beyond the armoury's own stipulations (Warbands of Trench Crusade
// warband entries): "apart from Armour", "the only Ranged Weapons they can have are...", "that costs up to 10 ducats",
// "must have a suit of Armour", "may only have Weapons with the HEAVY Keyword". Anything else goes in Note.
public sealed record KitOnly(KitCategory Category, IReadOnlyList<string> Kit);

public sealed record KitRules(
    IReadOnlyList<KitCategory> NoCategories,
    IReadOnlyList<KitOnly> OnlyKit,
    int? MaxCostDucats,
    IReadOnlyList<KitCategory> MustHave,
    bool MustHaveWeapon,
    string? WeaponsNeedKeyword,
    string? Note,
    Arms? Arms = null);

// A model with more arms than a person (an Amalgam, a Fallen Saint): it carries up to Count weapons, in place of the
// rulebook p. 69 hand limits. A 2-handed weapon uses TwoHandedArms of them, except up to HeavyAsOneHanded 2-handed
// HEAVY weapons that use one. A shield uses one and never has Shield Combo. MaxTwoHanded and MaxSameWeapon are the
// entry's own caps.
public sealed record Arms(int Count, int TwoHandedArms, int HeavyAsOneHanded, int? MaxTwoHanded, int? MaxSameWeapon);

// A purchasable upgrade for one model (an ability, keyword or other upgrade). Bought at most once per model.
public sealed record Upgrade(string Id, string Name, Cost? Cost, int? MaxPerWarband, IReadOnlyList<KeywordUse> AddsKeywords, string? Note);

// A warband entry. Ranged or Melee null means "-": the model may not make that kind of attack.
public sealed record ModelEntry(
    string Id,
    string Name,
    int Min,
    int? Max,
    Cost? Cost,
    int MovementInches,
    string MovementType,
    int? Ranged,
    int? Melee,
    int Armour,
    int BaseWidthMm,
    int? BaseLengthMm,
    IReadOnlyList<KeywordUse> Keywords,
    KitAccess Access,
    IReadOnlyList<string> Listed,
    IReadOnlyList<string> StartsWith,
    IReadOnlyList<KitPrice> Prices,
    IReadOnlyList<Upgrade> Upgrades,
    IReadOnlyList<Ability> Abilities,
    SourceRef Source,
    DataStatus Status,
    string? Note,
    Naming Naming,
    KitRules? KitRules = null)
{
    public bool HasKeyword(string id) => Keywords.Any(k => k.Id == id);
}

// A variant warband: the faction's list with typed changes. Nothing else of the faction changes.
public sealed record Variant(
    string Id,
    string Name,
    IReadOnlyList<string> RemoveModels,
    IReadOnlyList<ModelEntry> AddModels,
    IReadOnlyList<KitPrice> ModelCosts,
    IReadOnlyList<string> RemoveArmoury,
    IReadOnlyList<ArmouryItem> AddArmoury,
    IReadOnlyList<Ability> SpecialRules,
    SourceRef Source,
    DataStatus Status,
    string? Note,
    Naming Naming,
    int? BudgetDucats = null,
    int? BudgetGlory = null,
    IReadOnlyList<KitGroup>? KitGroups = null);

public sealed record Faction(
    string Id,
    string Name,
    Alignment Alignment,
    int BudgetDucats,
    IReadOnlyList<ModelEntry> Models,
    IReadOnlyList<ArmouryItem> Armoury,
    IReadOnlyList<Variant> Variants,
    IReadOnlyList<Ability> SpecialRules,
    SourceRef Source,
    DataStatus Status,
    string? Note,
    Naming Naming,
    IReadOnlyList<KitGroup>? KitGroups = null)
{
    // The kit groups a list uses: the faction's and its variant's.
    public IReadOnlyList<KitGroup> GroupsFor(string? variantId) =>
        (KitGroups ?? []).Concat(Variants.FirstOrDefault(v => v.Id == variantId)?.KitGroups ?? []).ToList();

    // The faction as a given variant (or the standard list for null) presents it: models and armoury after its changes.
    public (IReadOnlyList<ModelEntry> Models, IReadOnlyList<ArmouryItem> Armoury)? As(string? variantId)
    {
        if (variantId is null)
            return (Models, Armoury);
        var variant = Variants.FirstOrDefault(v => v.Id == variantId);
        if (variant is null)
            return null;
        var models = Models.Where(m => !variant.RemoveModels.Contains(m.Id))
            .Select(m => variant.ModelCosts.FirstOrDefault(c => c.Kit == m.Id) is { } price ? m with { Cost = price.Cost } : m)
            .Concat(variant.AddModels).ToList();
        var armoury = Armoury.Where(a => !variant.RemoveArmoury.Contains(a.Ref) && variant.AddArmoury.All(n => n.Ref != a.Ref))
            .Concat(variant.AddArmoury).ToList();
        return (models, armoury);
    }

    public static Checked<Faction> Load(string path, Catalogue catalogue, KeywordVocabulary vocabulary) =>
        Parse(File.ReadAllText(path), catalogue, vocabulary);

    public static Checked<Faction> Parse(string json, Catalogue catalogue, KeywordVocabulary vocabulary)
    {
        var problems = new List<string>();
        if (!Json.TryParse(json, problems, out var doc))
            return new(null, problems);
        using (doc)
        {
            var reader = new FactionReader(catalogue, vocabulary, problems);
            var faction = reader.Read(doc!.RootElement);
            return new(problems.Count == 0 ? faction : null, problems);
        }
    }
}

internal sealed class FactionReader(Catalogue catalogue, KeywordVocabulary vocabulary, List<string> problems)
{
    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    public Faction? Read(JsonElement root)
    {
        Json.CheckHeader(root, "faction list", problems);
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        var id = Json.Text(root, "id", "faction list", problems);
        CheckId(id, "faction list");
        var name = Json.Text(root, "name", "faction list", problems);
        var alignment = Json.Choice<Alignment>(root, "alignment", "faction list", problems);
        var budget = Json.Whole(root, "budgetDucats", "faction list", problems);
        if (budget is <= 0)
            problems.Add("faction list: \"budgetDucats\" must be 1 or more");
        var source = Catalogue.ReadSource(root, "faction list", problems);
        var status = Json.Choice<DataStatus>(root, "status", "faction list", problems);
        var note = Json.Text(root, "note", "faction list", problems, required: false);
        var naming = Json.Choice<Naming>(root, "naming", "faction list", problems);
        var special = Abilities(root, "specialRules", "faction list");

        var models = Models(root, "models", "faction list");
        if (models.Count == 0)
            problems.Add("faction list: it has no models");
        var groups = Groups(root, "faction list", []);
        var armoury = Armoury(root, "armoury", "faction list");
        CheckModelKit(models, armoury, "faction list");
        CheckArmouryLinks(models, armoury, groups, "faction list");

        var variants = new List<Variant>();
        var variantIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in Json.Objects(root, "variants", "faction list", problems))
        {
            var variant = ReadVariant(v, models, armoury, groups);
            if (variant is null)
                continue;
            if (!variantIds.Add(variant.Id))
                problems.Add($"variant \"{variant.Id}\": the id is used twice");
            variants.Add(variant);
        }

        if (id is null || name is null || alignment is null || budget is null || source is null || status is null || naming is null)
            return null;
        if (status == DataStatus.Discrepancy && note is null)
            problems.Add("faction list: a discrepancy needs a \"note\" saying what the sources disagree on");
        return new Faction(id, name, alignment.Value, budget.Value, models, armoury, variants, special, source, status.Value, note, naming.Value,
            groups);
    }

    private Variant? ReadVariant(JsonElement v, List<ModelEntry> baseModels, List<ArmouryItem> baseArmoury, List<KitGroup> baseGroups)
    {
        var id = Json.Text(v, "id", "variant", problems);
        var at = $"variant \"{id}\"";
        CheckId(id, at);
        var name = Json.Text(v, "name", at, problems);
        var remove = Json.Strings(v, "removeModels", at, problems);
        foreach (var m in remove.Where(m => baseModels.All(b => b.Id != m)))
            problems.Add($"{at}: removes model \"{m}\", which the faction does not have");
        var add = Models(v, "addModels", at);
        foreach (var m in add.Where(m => baseModels.Any(b => b.Id == m.Id) && !remove.Contains(m.Id)))
            problems.Add($"{at}: adds model \"{m.Id}\", which the faction already has (remove it first to replace it)");
        var costs = Prices(v, "modelCosts", at);
        foreach (var c in costs.Where(c => baseModels.All(b => b.Id != c.Kit) || remove.Contains(c.Kit)))
            problems.Add($"{at}: changes the cost of \"{c.Kit}\", which is not one of the faction's models in this variant");
        var removeArmoury = Json.Strings(v, "removeArmoury", at, problems);
        foreach (var k in removeArmoury.Where(k => baseArmoury.All(a => a.Ref != k)))
            problems.Add($"{at}: removes \"{k}\" from the armoury, but the faction's armoury does not have it");
        var groups = Groups(v, at, baseGroups);
        var addArmoury = Armoury(v, "addArmoury", at);
        var special = Abilities(v, "specialRules", at);
        var source = Catalogue.ReadSource(v, at, problems);
        var status = Json.Choice<DataStatus>(v, "status", at, problems);
        var note = Json.Text(v, "note", at, problems, required: false);
        var naming = Json.Choice<Naming>(v, "naming", at, problems);
        var budgetDucats = Json.Whole(v, "budgetDucats", at, problems, required: false);
        var budgetGlory = Json.Whole(v, "budgetGlory", at, problems, required: false);
        if (budgetDucats is < 0 || budgetGlory is < 0)
            problems.Add($"{at}: a budget cannot be negative");

        var models = baseModels.Where(m => !remove.Contains(m.Id)).Concat(add).ToList();
        var armoury = baseArmoury.Where(a => !removeArmoury.Contains(a.Ref) && addArmoury.All(n => n.Ref != a.Ref)).Concat(addArmoury).ToList();
        CheckModelKit(add, armoury, at);
        CheckModelKit(models.Except(add).ToList(), armoury, at);
        CheckArmouryLinks(models, armoury, baseGroups.Concat(groups).ToList(), at);

        if (id is null || name is null || source is null || status is null || naming is null)
            return null;
        if (status == DataStatus.Discrepancy && note is null)
            problems.Add($"{at}: a discrepancy needs a \"note\" saying what the sources disagree on");
        return new Variant(id, name, remove, add, costs, removeArmoury, addArmoury, special, source, status.Value, note, naming.Value,
            budgetDucats, budgetGlory, groups);
    }

    private List<ModelEntry> Models(JsonElement e, string field, string at)
    {
        var list = new List<ModelEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Json.Objects(e, field, at, problems))
        {
            var model = ReadModel(m);
            if (model is null)
                continue;
            if (!ids.Add(model.Id))
                problems.Add($"model \"{model.Id}\": the id is used twice");
            if (!names.Add(model.Name))
                problems.Add($"model \"{model.Id}\": the name \"{model.Name}\" is used by another model");
            list.Add(model);
        }
        return list;
    }

    private ModelEntry? ReadModel(JsonElement m)
    {
        var id = Json.Text(m, "id", "model", problems);
        var at = $"model \"{id}\"";
        CheckId(id, at);
        var name = Json.Text(m, "name", at, problems);
        var min = Json.Whole(m, "min", at, problems, required: false) ?? 0;
        var max = Json.Whole(m, "max", at, problems, required: false);
        if (min < 0 || max is < 0)
            problems.Add($"{at}: \"min\" and \"max\" cannot be negative");
        if (max is not null && min > max)
            problems.Add($"{at}: \"min\" is more than \"max\"");
        if (max == 0)
            problems.Add($"{at}: \"max\" of 0 means the model can never be taken; leave the model out instead");
        var cost = Catalogue.ReadCost(m, at, problems);
        var movement = Json.Whole(m, "movementInches", at, problems);
        if (movement is <= 0)
            problems.Add($"{at}: \"movementInches\" must be 1 or more");
        var movementType = Json.Text(m, "movementType", at, problems);
        var ranged = Dice(m, "ranged", at);
        var melee = Dice(m, "melee", at);
        var armour = Json.Whole(m, "armour", at, problems);
        if (armour is < -3 or > 0)
            problems.Add($"{at}: \"armour\" must be between -3 and 0");
        var baseMm = BaseSize(m, at);
        var keywords = Catalogue.ReadKeywords(m, at, vocabulary, problems);
        var access = Json.Choice<KitAccess>(m, "kitAccess", at, problems);
        var listed = Json.Strings(m, "listed", at, problems);
        if (access == KitAccess.Listed && listed.Count == 0)
            problems.Add($"{at}: \"kitAccess\" is \"listed\" but \"listed\" names no battlekit");
        if (access != KitAccess.Listed && listed.Count > 0)
            problems.Add($"{at}: \"listed\" is only used when \"kitAccess\" is \"listed\"");
        var startsWith = Json.Strings(m, "startsWith", at, problems);
        var prices = Prices(m, "prices", at);
        var upgrades = Upgrades(m, at);
        var abilities = Abilities(m, "abilities", at);
        var source = Catalogue.ReadSource(m, at, problems);
        var status = Json.Choice<DataStatus>(m, "status", at, problems);
        var note = Json.Text(m, "note", at, problems, required: false);
        var naming = Json.Choice<Naming>(m, "naming", at, problems);
        var kitRules = ReadKitRules(m, at);
        if (status == DataStatus.Verified && cost is null)
            problems.Add($"{at}: a verified model needs its cost; mark it unverified while the cost is unresolved");
        if (status == DataStatus.Discrepancy && note is null)
            problems.Add($"{at}: a discrepancy needs a \"note\" saying what the sources disagree on");
        foreach (var kit in listed.Concat(startsWith).Concat(prices.Select(p => p.Kit)).Distinct())
            if (catalogue.Find(kit) is null)
                problems.Add($"{at}: battlekit \"{kit}\" is not in the catalogue");

        if (id is null || name is null || movement is null || movementType is null || armour is null || baseMm is null
            || access is null || source is null || status is null || naming is null || ranged.Bad || melee.Bad)
            return null;
        return new ModelEntry(id, name, min, max, cost, movement.Value, movementType, ranged.Value, melee.Value, armour.Value,
            baseMm.Value.Width, baseMm.Value.Length, keywords, access.Value, listed, startsWith, prices, upgrades, abilities,
            source, status.Value, note, naming.Value, kitRules);
    }

    private KitRules? ReadKitRules(JsonElement m, string at)
    {
        if (!m.TryGetProperty("kitRules", out var r))
            return null;
        var rat = $"{at} kitRules";
        if (r.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{rat}: must be an object");
            return null;
        }
        List<KitCategory> Categories(string field)
        {
            var list = new List<KitCategory>();
            foreach (var text in Json.Strings(r, field, rat, problems))
                if (Enum.TryParse<KitCategory>(text, true, out var c) && char.IsLower(text[0]))
                    list.Add(c);
                else
                    problems.Add($"{rat}: \"{text}\" in \"{field}\" is not a battlekit category");
            return list;
        }
        var no = Categories("noCategories");
        var must = Categories("mustHave");
        var only = new List<KitOnly>();
        foreach (var o in Json.Objects(r, "onlyKit", rat, problems))
        {
            var category = Json.Choice<KitCategory>(o, "category", $"{rat} onlyKit", problems);
            var kit = Json.Strings(o, "kit", $"{rat} onlyKit", problems);
            foreach (var k in kit)
                if (catalogue.Find(k) is null)
                    problems.Add($"{rat}: battlekit \"{k}\" is not in the catalogue");
            if (category is not null)
                only.Add(new KitOnly(category.Value, kit));
        }
        var maxCost = Json.Whole(r, "maxCostDucats", rat, problems, required: false);
        if (maxCost is < 0)
            problems.Add($"{rat}: \"maxCostDucats\" cannot be negative");
        var weapon = r.TryGetProperty("mustHaveWeapon", out var w) && w.ValueKind == JsonValueKind.True;
        var needs = Json.Text(r, "weaponsNeedKeyword", rat, problems, required: false);
        if (needs is not null && vocabulary.Find(needs) is null)
            problems.Add($"{rat}: keyword \"{needs}\" is not in the vocabulary");
        var note = Json.Text(r, "note", rat, problems, required: false);
        return new KitRules(no, only, maxCost, must, weapon, needs, note, ReadArms(r, rat));
    }

    private Arms? ReadArms(JsonElement r, string rat)
    {
        if (!r.TryGetProperty("arms", out var a))
            return null;
        var aat = $"{rat} arms";
        if (a.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{aat}: must be an object");
            return null;
        }
        var count = Json.Whole(a, "count", aat, problems);
        var two = Json.Whole(a, "twoHandedArms", aat, problems);
        var heavy = Json.Whole(a, "heavyAsOneHanded", aat, problems, required: false) ?? 0;
        var maxTwo = Json.Whole(a, "maxTwoHanded", aat, problems, required: false);
        var maxSame = Json.Whole(a, "maxSameWeapon", aat, problems, required: false);
        if (count is < 3)
            problems.Add($"{aat}: \"count\" is for models with 3 or more arms; leave \"arms\" out for the normal hand limits");
        if (two is not (null or 1 or 2))
            problems.Add($"{aat}: \"twoHandedArms\" must be 1 or 2");
        if (heavy < 0 || maxTwo is < 0 || maxSame is <= 0)
            problems.Add($"{aat}: \"heavyAsOneHanded\" and \"maxTwoHanded\" cannot be negative and \"maxSameWeapon\" must be 1 or more");
        if (count is null || two is null)
            return null;
        return new Arms(count.Value, two.Value, heavy, maxTwo, maxSame);
    }

    // "+2 DICE" is written 2, "-" (no such attack) is written null. The field itself must be present.
    private (int? Value, bool Bad) Dice(JsonElement m, string field, string at)
    {
        if (!m.TryGetProperty(field, out var v))
        {
            problems.Add($"{at}: \"{field}\" is missing (write null for \"-\")");
            return (null, true);
        }
        if (v.ValueKind == JsonValueKind.Null)
            return (null, false);
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n is < -4 or > 4)
        {
            problems.Add($"{at}: \"{field}\" must be a whole number from -4 to +4, or null for \"-\"");
            return (null, true);
        }
        return (n, false);
    }

    private (int Width, int? Length)? BaseSize(JsonElement m, string at)
    {
        if (!m.TryGetProperty("baseMm", out var b) || b.ValueKind != JsonValueKind.Array || b.GetArrayLength() is not (1 or 2)
            || b.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out var n) || n <= 0))
        {
            problems.Add($"{at}: \"baseMm\" must be [diameter] or [width, length] in whole millimetres");
            return null;
        }
        var sizes = b.EnumerateArray().Select(x => x.GetInt32()).ToList();
        if (sizes.Count == 2 && sizes[0] > sizes[1])
            problems.Add($"{at}: an oval base is written [width, length], the smaller number first");
        return (sizes[0], sizes.Count == 2 ? sizes[1] : null);
    }

    private List<Upgrade> Upgrades(JsonElement m, string at)
    {
        var list = new List<Upgrade>();
        foreach (var u in Json.Objects(m, "upgrades", at, problems))
        {
            var id = Json.Text(u, "id", $"{at} upgrade", problems);
            var uat = $"{at} upgrade \"{id}\"";
            CheckId(id, uat);
            var name = Json.Text(u, "name", uat, problems);
            var cost = Catalogue.ReadCost(u, uat, problems);
            var max = Json.Whole(u, "maxPerWarband", uat, problems, required: false);
            if (max is <= 0)
                problems.Add($"{uat}: \"maxPerWarband\" must be 1 or more");
            var adds = Catalogue.ReadKeywords(u, uat, vocabulary, problems);
            var note = Json.Text(u, "note", uat, problems, required: false);
            if (id is null || name is null)
                continue;
            if (list.Any(x => x.Id == id))
                problems.Add($"{uat}: the id is used twice");
            list.Add(new Upgrade(id, name, cost, max, adds, note));
        }
        return list;
    }

    private List<ArmouryItem> Armoury(JsonElement e, string field, string at)
    {
        var list = new List<ArmouryItem>();
        foreach (var a in Json.Objects(e, field, at, problems))
        {
            var kit = Json.Text(a, "kit", $"{at} armoury", problems);
            var aat = $"{at} armoury \"{kit}\"";
            var cost = Catalogue.ReadCost(a, aat, problems);
            var limit = Json.Whole(a, "limit", aat, problems, required: false);
            var perModel = Json.Whole(a, "limitPerModel", aat, problems, required: false);
            if (limit is <= 0 || perModel is <= 0)
                problems.Add($"{aat}: limits must be 1 or more");
            var only = new List<OnlyFor>();
            foreach (var o in Json.Objects(a, "only", aat, problems))
            {
                var keyword = Json.Text(o, "keyword", $"{aat} only", problems, required: false);
                var model = Json.Text(o, "model", $"{aat} only", problems, required: false);
                if ((keyword is null) == (model is null))
                    problems.Add($"{aat}: each \"only\" item names exactly one of \"keyword\" or \"model\"");
                else if (keyword is not null && vocabulary.Find(keyword) is null)
                    problems.Add($"{aat}: \"only\" names unknown keyword \"{keyword}\"");
                else
                    only.Add(new OnlyFor(keyword, model));
            }
            var stipulations = new HashSet<Stipulation>();
            foreach (var s in Json.Strings(a, "stipulations", aat, problems))
            {
                var match = Enum.GetValues<Stipulation>().Where(v => Json.CamelCase(v.ToString()) == s).ToList();
                if (match.Count == 0)
                    problems.Add($"{aat}: unknown stipulation \"{s}\"; it must be one of {string.Join(", ", Enum.GetValues<Stipulation>().Select(v => Json.CamelCase(v.ToString())))}");
                else if (!stipulations.Add(match[0]))
                    problems.Add($"{aat}: stipulation \"{s}\" is listed twice");
            }
            var status = Json.Choice<DataStatus>(a, "status", aat, problems);
            var note = Json.Text(a, "note", aat, problems, required: false);
            var from = Json.Text(a, "from", aat, problems, required: false);
            CheckId(from, $"{aat} \"from\"");
            var group = Json.Text(a, "group", aat, problems, required: false);
            var needsKit = Json.Text(a, "needsKit", aat, problems, required: false);
            if (needsKit is not null && catalogue.Find(needsKit) is null)
                problems.Add($"{aat}: \"needsKit\" names \"{needsKit}\", which is not in the catalogue");
            if (kit is null || status is null)
                continue;
            var entry = catalogue.Find(kit);
            if (entry is null)
                problems.Add($"{aat}: battlekit \"{kit}\" is not in the catalogue");
            if (list.Any(x => x.Kit == kit && x.From == from))
                problems.Add(from is null ? $"{aat}: listed twice in the same armoury" : $"{aat}: listed twice from \"{from}\" in the same armoury");
            if (status == DataStatus.Verified && cost is null)
                problems.Add($"{aat}: a verified armoury item needs its cost; mark it unverified while the cost is unresolved");
            if (status == DataStatus.Discrepancy && note is null)
                problems.Add($"{aat}: a discrepancy needs a \"note\" saying what the sources disagree on");
            if (entry is not null && stipulations.Contains(Stipulation.BayonetLug) && entry.Category != KitCategory.Ranged)
                problems.Add($"{aat}: only a ranged weapon can have a bayonet lug");
            if (entry is not null && stipulations.Contains(Stipulation.ShieldCombo) && entry.Category is not (KitCategory.Shield or KitCategory.Ranged or KitCategory.Melee))
                problems.Add($"{aat}: Shield Combo is for shields and weapons only");
            list.Add(new ArmouryItem(kit, cost, limit, perModel, only, stipulations, status.Value, note, from, group, needsKit));
        }
        return list;
    }

    private List<KitGroup> Groups(JsonElement e, string at, List<KitGroup> outer)
    {
        var list = new List<KitGroup>();
        foreach (var g in Json.Objects(e, "kitGroups", at, problems))
        {
            var id = Json.Text(g, "id", $"{at} kit group", problems);
            var gat = $"{at} kit group \"{id}\"";
            CheckId(id, gat);
            var name = Json.Text(g, "name", gat, problems);
            var limit = Json.Whole(g, "limit", gat, problems, required: false);
            var perModel = Json.Whole(g, "limitPerModel", gat, problems, required: false);
            if (limit is <= 0 || perModel is <= 0)
                problems.Add($"{gat}: limits must be 1 or more");
            var upgrade = Json.Text(g, "needsUpgrade", gat, problems, required: false);
            var note = Json.Text(g, "note", gat, problems, required: false);
            if (id is null || name is null)
                continue;
            if (list.Concat(outer).Any(x => x.Id == id))
                problems.Add($"{gat}: the id is used twice");
            list.Add(new KitGroup(id, name, limit, perModel, upgrade, note));
        }
        return list;
    }

    // Groups, needed upgrades and needed kit must all be things this list has.
    private void CheckArmouryLinks(IReadOnlyList<ModelEntry> models, IReadOnlyList<ArmouryItem> armoury, IReadOnlyList<KitGroup> groups, string at)
    {
        foreach (var a in armoury)
        {
            if (a.Group is { } g && groups.All(x => x.Id != g))
                problems.Add($"{at}: armoury \"{a.Ref}\" is in kit group \"{g}\", which the list does not define");
            if (a.NeedsKit is { } k && armoury.All(x => x.Kit != k) && models.All(m => !m.StartsWith.Contains(k)))
                problems.Add($"{at}: armoury \"{a.Ref}\" needs \"{k}\", which no model in the list can have");
        }
        foreach (var g in groups.Where(g => g.NeedsUpgrade is not null))
            if (models.All(m => m.Upgrades.All(u => u.Id != g.NeedsUpgrade)))
                problems.Add($"{at}: kit group \"{g.Id}\" needs upgrade \"{g.NeedsUpgrade}\", which no model in the list has");
    }

    private List<KitPrice> Prices(JsonElement e, string field, string at)
    {
        var list = new List<KitPrice>();
        foreach (var p in Json.Objects(e, field, at, problems))
        {
            var key = field == "modelCosts" ? "model" : "kit";
            var kit = Json.Text(p, key, $"{at} {field}", problems);
            var cost = Catalogue.ReadCost(p, $"{at} {field} \"{kit}\"", problems);
            if (kit is null)
                continue;
            if (list.Any(x => x.Kit == kit))
                problems.Add($"{at}: \"{kit}\" is priced twice in \"{field}\"");
            list.Add(new KitPrice(kit, cost));
        }
        return list;
    }

    private List<Ability> Abilities(JsonElement e, string field, string at)
    {
        var list = new List<Ability>();
        foreach (var a in Json.Objects(e, field, at, problems))
        {
            var name = Json.Text(a, "name", $"{at} {field}", problems);
            var summary = Json.Text(a, "summary", $"{at} {field}", problems);
            var page = Json.Whole(a, "page", $"{at} {field}", problems);
            if (page is <= 0)
                problems.Add($"{at}: the page of \"{name}\" must be 1 or more");
            if (name is not null && summary is not null && page is not null)
                list.Add(new Ability(name, summary, page.Value));
        }
        return list;
    }

    // Each model can only be given battlekit the faction (or variant) can price.
    private void CheckModelKit(IReadOnlyList<ModelEntry> models, IReadOnlyList<ArmouryItem> armoury, string at)
    {
        foreach (var m in models)
            foreach (var kit in m.Listed.Where(k => armoury.All(a => a.Kit != k) && m.Prices.All(p => p.Kit != k)))
                problems.Add($"{at}: model \"{m.Id}\" lists \"{kit}\", which has no price in the armoury or the model's own prices");
    }

    private void CheckId(string? id, string at)
    {
        if (id is not null && !IdPattern.IsMatch(id))
            problems.Add($"{at}: an id is lower-case letters and digits joined by single hyphens");
    }
}
