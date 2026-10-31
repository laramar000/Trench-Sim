namespace Roster;

// The outcome of checking a roster. Problems make it illegal; Notes are rules the validator does not check itself
// (an upgrade's special condition, unverified data) and are shown to the player alongside the roster.
public sealed record RosterCheck(IReadOnlyList<string> Problems, IReadOnlyList<string> Notes, int Ducats, int Glory)
{
    public bool Legal => Problems.Count == 0;
}

// Checks a roster against its faction list and the catalogue: Warbands of Trench Crusade pp. 9-13 (budget, model
// limits, Maximum Elites, stipulations, upgrades) and the 1.0.2 rulebook p. 69 (battlekit limits and shields).
public static class RosterValidator
{
    public const int MaximumElites = 6;

    public static RosterCheck Check(WarbandRoster roster, Faction faction, Catalogue catalogue)
    {
        var problems = new List<string>();
        var notes = new List<string>();
        var ducats = 0;
        var glory = 0;

        if (roster.Faction != faction.Id)
            problems.Add($"the roster is for faction \"{roster.Faction}\", not {faction.Name}");
        var shown = faction.As(roster.Variant);
        if (shown is null)
        {
            problems.Add($"{faction.Name} has no variant \"{roster.Variant}\"");
            return new(problems, notes, 0, 0);
        }
        var (entries, armoury) = shown.Value;
        var groups = faction.GroupsFor(roster.Variant);
        var groupCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var listName = roster.Variant is null ? faction.Name : faction.Variants.First(v => v.Id == roster.Variant).Name;

        void Add(Cost? cost, string what)
        {
            if (cost is null)
            {
                problems.Add($"{what} has no verified cost yet, so it cannot be taken");
                return;
            }
            if (cost.Currency == Currency.Ducats)
                ducats += cost.Amount;
            else
                glory += cost.Amount;
        }

        var kitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var upgradeCounts = new Dictionary<(string, string), int>();
        var elites = 0;
        var number = 0;
        foreach (var model in roster.Models)
        {
            number++;
            var entry = entries.FirstOrDefault(e => e.Id == model.Entry);
            var who = model.Name is null ? $"model {number}" : $"model {number} ({model.Name})";
            if (entry is null)
            {
                problems.Add($"{who}: {listName} has no warband entry \"{model.Entry}\"");
                continue;
            }
            who = $"{who}, {entry.Name}";
            Add(entry.Cost, who);
            if (entry.Status != DataStatus.Verified)
                notes.Add($"{entry.Name}: its entry is not verified against the book yet");

            var keywords = entry.Keywords.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var upgradeId in model.Upgrades)
            {
                var upgrade = entry.Upgrades.FirstOrDefault(u => u.Id == upgradeId);
                if (upgrade is null)
                {
                    problems.Add($"{who}: {entry.Name} has no upgrade \"{upgradeId}\"");
                    continue;
                }
                if (model.Upgrades.Count(u => u == upgradeId) > 1)
                {
                    problems.Add($"{who}: {upgrade.Name} can be bought only once for a model");
                    continue;
                }
                Add(upgrade.Cost, $"{who}: {upgrade.Name}");
                foreach (var k in upgrade.AddsKeywords)
                    keywords.Add(k.Id);
                upgradeCounts[(entry.Id, upgrade.Id)] = upgradeCounts.GetValueOrDefault((entry.Id, upgrade.Id)) + 1;
                if (upgrade.Note is not null)
                    notes.Add($"{upgrade.Name}: {upgrade.Note}");
            }
            if (keywords.Contains("ELITE"))
                elites++;

            // Everything the model carries: what it starts with (included in its cost) and what was bought.
            var carried = new List<(CatalogueEntry Kit, ArmouryItem? From, bool Bought)>();
            foreach (var kitId in entry.StartsWith)
                if (catalogue.Find(kitId) is { } kit)
                    carried.Add((kit, armoury.FirstOrDefault(a => a.Kit == kitId && a.From is null), false));
            var modelGroups = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kitRef in model.Kit)
            {
                var (kitId, fromFaction) = KitRef.Split(kitRef);
                var kit = catalogue.Find(kitId);
                if (kit is null)
                {
                    problems.Add($"{who}: battlekit \"{kitId}\" is not in the catalogue");
                    continue;
                }
                var from = armoury.FirstOrDefault(a => a.Kit == kitId && a.From == fromFaction);
                var price = fromFaction is null ? entry.Prices.FirstOrDefault(p => p.Kit == kitId) : null;
                var allowed = entry.Access switch
                {
                    KitAccess.Armoury => from is not null || price is not null,
                    KitAccess.Listed => fromFaction is null && entry.Listed.Contains(kitId),
                    _ => false,
                };
                if (!allowed)
                {
                    problems.Add(entry.Access == KitAccess.None
                        ? $"{who}: {entry.Name} cannot be given battlekit"
                        : fromFaction is null
                            ? $"{who}: {kit.Name} is not available to {entry.Name} in {listName}"
                            : $"{who}: {kit.Name} from \"{fromFaction}\" is not available to {entry.Name} in {listName}");
                    continue;
                }
                if (from?.Group is { } groupId && groups.FirstOrDefault(g => g.Id == groupId) is { } group)
                {
                    if (group.NeedsUpgrade is { } needed && !model.Upgrades.Contains(needed))
                    {
                        problems.Add($"{who}: {kit.Name} ({group.Name}) needs {entry.Upgrades.FirstOrDefault(u => u.Id == needed)?.Name ?? needed}");
                        continue;
                    }
                    modelGroups[groupId] = modelGroups.GetValueOrDefault(groupId) + 1;
                    groupCounts[groupId] = groupCounts.GetValueOrDefault(groupId) + 1;
                    if (group.LimitPerModel is { } most && modelGroups[groupId] > most)
                        problems.Add($"{who}: can have at most {most} from {group.Name} (it has {modelGroups[groupId]})");
                }
                if (entry.KitRules is { } rules && KitRuleProblem(rules, kit, price?.Cost ?? from?.Cost) is { } refused)
                {
                    problems.Add($"{who}: {refused}");
                    continue;
                }
                if (from is not null && from.Only.Count > 0 &&
                    !from.Only.Any(o => o.Model == entry.Id || (o.Keyword is not null && keywords.Contains(o.Keyword))))
                {
                    problems.Add($"{who}: {kit.Name} is only for {string.Join(" or ", from.Only.Select(o => o.Model is not null ? Display(entries, o.Model) : o.Keyword))}");
                    continue;
                }
                Add(price is not null ? price.Cost : from!.Cost, $"{who}: {kit.Name}");
                if (kit.Status != DataStatus.Verified || from?.Status is DataStatus.Unverified or DataStatus.Discrepancy)
                    notes.Add($"{kit.Name}: not verified against the book yet");
                if (kit.CampaignOnly)
                    notes.Add($"{kit.Name}: campaign-only battlekit");
                kitCounts[kitRef] = kitCounts.GetValueOrDefault(kitRef) + 1;
                carried.Add((kit, from, true));
            }
            foreach (var c in carried.Where(c => c.Bought && c.From?.NeedsKit is not null))
                if (!carried.Any(o => o.Kit.Id == c.From!.NeedsKit))
                    problems.Add($"{who}: {c.Kit.Name} needs {catalogue.Find(c.From!.NeedsKit!)?.Name ?? c.From!.NeedsKit}");
            CheckCarried(who, carried, keywords, entry.KitRules?.Arms, problems);
            if (entry.KitRules is { } must)
            {
                foreach (var category in must.MustHave)
                    if (!carried.Any(c => c.Kit.Category == category))
                        problems.Add($"{who}: must have {Words(category)}");
                if (must.MustHaveWeapon && !carried.Any(c => c.Kit.Category is KitCategory.Ranged or KitCategory.Melee or KitCategory.Grenade))
                    problems.Add($"{who}: must have at least 1 weapon");
                if (must.Note is not null)
                    notes.Add($"{entry.Name}: {must.Note}");
            }
        }

        foreach (var entry in entries)
        {
            var taken = roster.Models.Count(m => m.Entry == entry.Id);
            if (taken < entry.Min)
                problems.Add(entry.Min == 1 && entry.Max == 1
                    ? $"{listName} must include 1 {entry.Name}"
                    : $"{listName} must include at least {entry.Min} {entry.Name} (it has {taken})");
            if (entry.Max is { } max && taken > max)
                problems.Add($"{listName} can include at most {max} {entry.Name} (it has {taken})");
            foreach (var upgrade in entry.Upgrades)
                if (upgrade.MaxPerWarband is { } most && upgradeCounts.GetValueOrDefault((entry.Id, upgrade.Id)) > most)
                    problems.Add($"at most {most} {entry.Name} can have {upgrade.Name} (it has {upgradeCounts[(entry.Id, upgrade.Id)]})");
        }
        if (elites > MaximumElites)
            problems.Add($"a warband can never include more than {MaximumElites} ELITE models (it has {elites})");
        foreach (var item in armoury)
            if (item.Limit is { } limit && kitCounts.GetValueOrDefault(item.Ref) > limit)
                problems.Add($"{catalogue.Find(item.Kit)?.Name ?? item.Kit} has Limit: {limit} for the whole warband (it has {kitCounts[item.Ref]})");
        foreach (var group in groups)
            if (group.Limit is { } most && groupCounts.GetValueOrDefault(group.Id) > most)
                problems.Add($"{group.Name}: at most {most} for the whole warband (it has {groupCounts[group.Id]})");

        if (ducats > roster.BudgetDucats)
            problems.Add($"the warband costs {ducats} ducats, {ducats - roster.BudgetDucats} more than its {roster.BudgetDucats}");
        if (glory > roster.BudgetGlory)
            problems.Add($"the warband costs {glory} Glory Points, {glory - roster.BudgetGlory} more than its {roster.BudgetGlory}");
        return new(problems, notes.Distinct().ToList(), ducats, glory);
    }

    // A model's own battlekit limits (KitRules), for one piece it is being given. Null when the piece is allowed.
    private static string? KitRuleProblem(KitRules rules, CatalogueEntry kit, Cost? cost)
    {
        if (rules.NoCategories.Contains(kit.Category))
            return $"cannot be given {Words(kit.Category)} from the armoury";
        if (rules.OnlyKit.FirstOrDefault(o => o.Category == kit.Category) is { } only && !only.Kit.Contains(kit.Id))
            return $"cannot have {kit.Name}: its only {Words(kit.Category)} choices are listed in its entry";
        if (rules.MaxCostDucats is { } most && cost is { } c && (c.Currency != Currency.Ducats || c.Amount > most))
            return $"can only have battlekit that costs up to {most} ducats, and {kit.Name} costs {c.Amount} {(c.Currency == Currency.Ducats ? "ducats" : "Glory")}";
        if (rules.WeaponsNeedKeyword is { } needed && kit.Category is KitCategory.Ranged or KitCategory.Melee
            && kit.Keywords.All(k => k.Id != needed))
            return $"can only have weapons with the {needed} keyword";
        return null;
    }

    private static string Words(KitCategory category) => category switch
    {
        KitCategory.Ranged => "ranged weapons",
        KitCategory.Melee => "melee weapons",
        KitCategory.Grenade => "grenades",
        KitCategory.Shield => "a shield",
        KitCategory.Armour => "armour",
        KitCategory.Equipment => "equipment",
        _ => "special battlekit",
    };

    private static string Display(IReadOnlyList<ModelEntry> entries, string id) =>
        entries.FirstOrDefault(e => e.Id == id)?.Name ?? id;

    // 1.0.2 p. 69 battlekit limits, shields, STRONG and CUMBERSOME (p. 54, 57), and the Warbands book's Headgear,
    // Bayonet Lug and Limit-per-model stipulations.
    private static void CheckCarried(string who, List<(CatalogueEntry Kit, ArmouryItem? From, bool Bought)> carried,
        HashSet<string> keywords, Arms? arms, List<string> problems)
    {
        var strong = keywords.Contains("STRONG");
        bool Has(CatalogueEntry kit, string keyword) => kit.Keywords.Any(k => k.Id == keyword);
        bool Combo((CatalogueEntry Kit, ArmouryItem? From, bool) c) => c.From?.Stipulations.Contains(Stipulation.ShieldCombo) == true;

        // A STRONG model may use one 2-handed melee weapon as if 1-handed, unless it is CUMBERSOME.
        var melee = carried.Where(c => c.Kit.Category == KitCategory.Melee).ToList();
        var meleeHands = melee.Select(c => c.Kit.Hands ?? 1).ToList();
        var eased = strong ? melee.FindIndex(c => c.Kit.Hands == 2 && !Has(c.Kit, "CUMBERSOME")) : -1;
        if (eased >= 0)
            meleeHands[eased] = 1;

        var ranged = carried.Where(c => c.Kit.Category == KitCategory.Ranged).ToList();
        if (arms is not null)
            CheckArms(who, carried, arms, problems);
        else
        {
            if (ranged.Sum(c => c.Kit.Hands ?? 1) > 2)
                problems.Add($"{who}: can carry one 2-handed or two 1-handed ranged weapons");
            if (meleeHands.Sum() > 2)
                problems.Add($"{who}: can carry one 2-handed or two 1-handed melee weapons");
        }

        foreach (var (category, words) in new[] { (KitCategory.Grenade, "one type of grenade"), (KitCategory.Armour, "one suit of armour"), (KitCategory.Shield, "one shield") })
            if (carried.Count(c => c.Kit.Category == category) > 1)
                problems.Add($"{who}: can have only {words}");
        foreach (var dup in carried.Where(c => c.Kit.Category is KitCategory.Equipment or KitCategory.Special)
                     .GroupBy(c => c.Kit.Id).Where(g => g.Count() > 1))
            problems.Add($"{who}: cannot have two pieces of {dup.First().Kit.Name}");
        if (carried.Count(c => c.From?.Stipulations.Contains(Stipulation.Headgear) == true) > 1)
            problems.Add($"{who}: can wear only one piece of Headgear");

        var shield = carried.FirstOrDefault(c => c.Kit.Category == KitCategory.Shield);
        if (shield.Kit is not null && arms is null)
        {
            if (ranged.Count(c => c.Kit.Hands == 1) > 1 || meleeHands.Count(h => h == 1) > 1)
                problems.Add($"{who}: with a shield it can carry at most one 1-handed ranged and one 1-handed melee weapon");
            foreach (var two in ranged.Where(c => c.Kit.Hands == 2).Concat(melee.Where((c, i) => meleeHands[i] == 2)))
                if (!(Combo(two) && Combo(shield)))
                    problems.Add($"{who}: cannot carry {two.Kit.Name} with {shield.Kit.Name}; both need Shield Combo");
        }

        var needsLug = carried.Where(c => c.From?.Stipulations.Contains(Stipulation.NeedsBayonetLug) == true).ToList();
        if (needsLug.Count > 0 && !carried.Any(c => c.From?.Stipulations.Contains(Stipulation.BayonetLug) == true))
            problems.Add($"{who}: {needsLug[0].Kit.Name} needs a ranged weapon with a Bayonet Lug");

        foreach (var group in carried.Where(c => c.Bought && c.From?.LimitPerModel is not null).GroupBy(c => c.Kit.Id))
            if (group.Count() > group.First().From!.LimitPerModel)
                problems.Add($"{who}: can have at most {group.First().From!.LimitPerModel} {group.First().Kit.Name}");
    }

    // A many-armed model's weapons and shield against its arm count (its warband entry replaces p. 69's limits).
    private static void CheckArms(string who, List<(CatalogueEntry Kit, ArmouryItem? From, bool Bought)> carried, Arms arms,
        List<string> problems)
    {
        var held = carried.Where(c => c.Kit.Category is KitCategory.Ranged or KitCategory.Melee or KitCategory.Shield).ToList();
        var heavyLeft = arms.HeavyAsOneHanded;
        var used = 0;
        foreach (var (kit, _, _) in held)
        {
            if (kit.Category == KitCategory.Shield || kit.Hands != 2)
                used += 1;
            else if (heavyLeft > 0 && kit.Keywords.Any(k => k.Id == "HEAVY"))
            {
                heavyLeft--;
                used += 1;
            }
            else
                used += arms.TwoHandedArms;
        }
        if (used > arms.Count)
            problems.Add($"{who}: has {arms.Count} arms, and its weapons{(held.Any(c => c.Kit.Category == KitCategory.Shield) ? " and shield" : "")} need {used}");
        var twoHanded = held.Count(c => c.Kit.Category != KitCategory.Shield && c.Kit.Hands == 2);
        if (arms.MaxTwoHanded is { } maxTwo && twoHanded > maxTwo)
            problems.Add($"{who}: can carry at most {maxTwo} 2-handed weapon{(maxTwo == 1 ? "" : "s")} (it has {twoHanded})");
        if (arms.MaxSameWeapon is { } maxSame)
            foreach (var same in held.Where(c => c.Kit.Category != KitCategory.Shield).GroupBy(c => c.Kit.Id).Where(g => g.Count() > maxSame))
                problems.Add($"{who}: cannot carry {same.First().Kit.Name} more than {(maxSame == 2 ? "twice" : $"{maxSame} times")}");
    }
}
