using System.Text.Json;

namespace Roster;

// Roster stage 5a: what a legal roster becomes in a match, as the rules engine can play it today. The engine's model
// profile holds Movement, a round base, one ranged weapon (range, injury modifier, "/Melee"), Armour and one Melee
// Weapon (injury modifier); nothing else (Rules.Core ModelProfile). So:
// - Refused, by name: what the engine cannot hold at all (an oval base, a second ranged or melee weapon, grenades,
//   equipment and other special battlekit). The match is not created while anything is refused (Jason 2026-10-30,
//   decision 2: the builder allows it, match creation refuses it by name).
// - Played without, by name: what the engine leaves out of a piece or model it does play (weapon keywords, the model's
//   keywords, abilities and upgrades). Shown to both players before the match.
// Roster stage R1: the model's Ranged and Melee DICE and its weapons' DICE and INJURY DICE are played.
// Roster stage R2: the weapon keywords in PlayedKeywords are played; the others are named.
// Roster stage R3 (U100): two ranged and two melee weapons (a dual-purpose Pistol may be the second Melee Weapon), CLEAVE,
// BLOCK and IMPERVIOUS armour and shields are played; a third weapon of a kind is refused by name.
// The result is written in the warband content format the server's scenarios load (content/test-warbands.json).
public sealed record ReadyModel(
    string Name,
    int MovementInches,
    int BaseMm,
    int RangeInches,
    int RangedInjuryModifier,
    bool RangedAlsoMelee,
    int Armour,
    bool HasMeleeWeapon,
    int MeleeInjuryModifier)
{
    // Roster stage R1: the model's Ranged and Melee Characteristics and its weapons' DICE and INJURY DICE.
    public int RangedDice { get; init; }
    public int MeleeDice { get; init; }
    public int RangedWeaponDice { get; init; }
    public int RangedWeaponInjuryDice { get; init; }
    public int MeleeWeaponDice { get; init; }
    public int MeleeWeaponInjuryDice { get; init; }

    // Roster stage R2: the played keywords of the ranged and melee weapon, by the engine's names (Rules.Core
    // WeaponKeywords: Critical, IgnoreArmour, ArmourPiercing, Shotgun, Risky, Pistol, IgnoreCover, IgnoreLongRange,
    // IgnoreElevatedPosition, and stage R2b's Assault, Heavy, Reload).
    public IReadOnlyList<string> RangedWeaponKeywords { get; init; } = [];
    public IReadOnlyList<string> MeleeWeaponKeywords { get; init; } = [];

    // Roster stage R3: the melee weapon's CLEAVE (X), the Armour from IMPERVIOUS battlekit, and the second weapons.
    public int MeleeWeaponCleave { get; init; }
    public int ImperviousArmour { get; init; }
    public ReadyWeapon? SecondRangedWeapon { get; init; }
    public ReadyWeapon? SecondMeleeWeapon { get; init; }
}

// Roster stage R3: a second weapon, as Rules.Core Weapon holds it (RangeInches 0 for a Melee Weapon).
public sealed record ReadyWeapon(string Name, int RangeInches, int InjuryModifier, int Dice, int InjuryDice, IReadOnlyList<string> Keywords, bool AlsoMelee, int Cleave);

public sealed record MatchReady(
    IReadOnlyList<ReadyModel> Models,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> PlayedWithout,
    IReadOnlyList<string> RosterProblems)
{
    // A match can be created from it: the roster is legal and nothing is refused.
    public bool Ok => RosterProblems.Count == 0 && Refused.Count == 0 && Models.Count > 0;

    // The warband in the content format of content/test-warbands.json (one warband).
    public string ToContentJson(string id, string name) => ContentJson([(id, name, this)]);

    // Several warbands in one content file, in order: the first takes Seat One, the second Seat Two (Sim.ScenarioSetups).
    public static string ContentJson(IReadOnlyList<(string Id, string Name, MatchReady Ready)> warbands)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("note", "Made from a roster by the roster builder (stage 5a).");
            w.WriteStartArray("warbands");
            foreach (var (id, name, ready) in warbands)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("name", name);
                w.WriteStartArray("models");
                foreach (var m in ready.Models)
                {
                    w.WriteStartObject();
                    w.WriteString("name", m.Name);
                    w.WriteString("movementInches", m.MovementInches.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    w.WriteNumber("baseMm", m.BaseMm);
                    w.WriteString("rangeInches", m.RangeInches.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    w.WriteNumber("weaponInjuryModifier", m.RangedInjuryModifier);
                    w.WriteNumber("armour", m.Armour);
                    if (m.HasMeleeWeapon)
                    {
                        w.WriteNumber("meleeInjuryModifier", m.MeleeInjuryModifier);
                    }
                    if (m.RangedAlsoMelee)
                    {
                        w.WriteBoolean("rangedAlsoMelee", true);
                    }
                    foreach (var (field, words) in new[] { ("rangedWeaponKeywords", m.RangedWeaponKeywords), ("meleeWeaponKeywords", m.MeleeWeaponKeywords) })
                    {
                        if (words.Count > 0)
                        {
                            w.WriteStartArray(field);
                            foreach (var word in words)
                            {
                                w.WriteStringValue(word);
                            }
                            w.WriteEndArray();
                        }
                    }
                    foreach (var (field, dice) in new[]
                    {
                        ("rangedDice", m.RangedDice), ("meleeDice", m.MeleeDice), ("rangedWeaponDice", m.RangedWeaponDice),
                        ("rangedWeaponInjuryDice", m.RangedWeaponInjuryDice), ("meleeWeaponDice", m.MeleeWeaponDice),
                        ("meleeWeaponInjuryDice", m.MeleeWeaponInjuryDice), ("meleeWeaponCleave", m.MeleeWeaponCleave),
                        ("imperviousArmour", m.ImperviousArmour),
                    })
                    {
                        if (dice != 0)
                        {
                            w.WriteNumber(field, dice);
                        }
                    }
                    foreach (var (field, weapon) in new[] { ("secondRangedWeapon", m.SecondRangedWeapon), ("secondMeleeWeapon", m.SecondMeleeWeapon) })
                    {
                        if (weapon is null)
                        {
                            continue;
                        }
                        w.WriteStartObject(field);
                        if (weapon.RangeInches != 0)
                        {
                            w.WriteString("rangeInches", weapon.RangeInches.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        foreach (var (part, value) in new[] { ("injuryModifier", weapon.InjuryModifier), ("dice", weapon.Dice), ("injuryDice", weapon.InjuryDice), ("cleave", weapon.Cleave) })
                        {
                            if (value != 0)
                            {
                                w.WriteNumber(part, value);
                            }
                        }
                        if (weapon.Keywords.Count > 0)
                        {
                            w.WriteStartArray("keywords");
                            foreach (var word in weapon.Keywords)
                            {
                                w.WriteStringValue(word);
                            }
                            w.WriteEndArray();
                        }
                        if (weapon.AlsoMelee)
                        {
                            w.WriteBoolean("alsoMelee", true);
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}

public static class MatchReadiness
{
    // The engine's limits (Rules.Core ModelProfile): Movement above 0 and at most 24", a base radius up to 3", range up
    // to 60", modifiers -3 to +3, Armour 0 to -3.
    public const int MaxMovementInches = 24;
    public const int MaxBaseMm = 152;
    public const int MaxRangeInches = 60;
    public const int MaxModifier = 3;
    public const int MaxDice = 6;
    public const int MaxCleave = 6;

    public static MatchReady Check(WarbandRoster roster, Faction faction, Catalogue catalogue)
    {
        var check = RosterValidator.Check(roster, faction, catalogue);
        var refused = new List<string>();
        var without = new List<string>();
        var models = new List<ReadyModel>();
        var entries = faction.As(roster.Variant)?.Models ?? [];
        var number = 0;
        foreach (var model in roster.Models)
        {
            number++;
            if (entries.FirstOrDefault(e => e.Id == model.Entry) is not { } entry)
            {
                continue; // the validator names it
            }
            var who = $"{(model.Name is null ? $"model {number}" : $"model {number} ({model.Name})")}, {entry.Name}";
            var name = model.Name ?? entry.Name;
            var problems = refused.Count;

            if (entry.BaseLengthMm is not null)
            {
                refused.Add($"{who}: its {entry.BaseWidthMm}x{entry.BaseLengthMm} mm oval base (the engine has round bases only)");
            }
            if (entry.BaseWidthMm > MaxBaseMm || entry.MovementInches > MaxMovementInches)
            {
                refused.Add($"{who}: its base or Movement is beyond what the engine allows");
            }
            if (entry.Keywords.Count > 0)
            {
                without.Add($"{who}: its keywords ({string.Join(", ", entry.Keywords.Select(k => k.Id))})");
            }
            if (entry.Abilities.Count > 0)
            {
                without.Add($"{who}: its abilities ({string.Join(", ", entry.Abilities.Select(a => a.Name))})");
            }
            foreach (var upgradeId in model.Upgrades)
            {
                if (entry.Upgrades.FirstOrDefault(u => u.Id == upgradeId) is { } upgrade)
                {
                    without.Add($"{who}: {upgrade.Name}");
                }
            }

            var kit = entry.StartsWith.Concat(model.Kit)
                .Select(k => catalogue.Find(KitRef.Split(k).Kit)).OfType<CatalogueEntry>().ToList();
            var guns = new List<CatalogueEntry>();
            var blades = new List<CatalogueEntry>();
            var armour = entry.Armour;
            var impervious = 0;
            foreach (var piece in kit)
            {
                switch (piece.Category)
                {
                    case KitCategory.Ranged when guns.Count < 2 && piece.RangeInches is { } range && range <= MaxRangeInches:
                        guns.Add(piece);
                        Without(who, piece, without);
                        break;
                    case KitCategory.Melee when blades.Count < 2:
                        blades.Add(piece);
                        Without(who, piece, without);
                        break;
                    case KitCategory.Ranged when piece.RangeInches is > MaxRangeInches:
                        refused.Add($"{who}: {piece.Name} (its range is beyond what the engine allows)");
                        break;
                    case KitCategory.Ranged or KitCategory.Melee:
                        refused.Add($"{who}: {piece.Name} (the engine plays two ranged and two melee weapons per model)");
                        break;
                    case KitCategory.Armour or KitCategory.Shield:
                        armour += piece.InjuryModifier;
                        if (piece.Keywords.Any(k => k.Id == "IMPERVIOUS"))
                        {
                            impervious += piece.InjuryModifier;
                        }
                        Without(who, piece, without, diceToo: true);
                        break;
                    default:
                        refused.Add($"{who}: {piece.Name} (the engine has no {Words(piece.Category)} yet)");
                        break;
                }
            }
            // A model with no Melee Weapon fights with its dual-purpose Pistol: that one is its first ranged weapon. With
            // fewer than two Melee Weapons, another dual-purpose Pistol is its second Melee Weapon (p. 69, U100).
            if (blades.Count == 0 && guns.Count == 2 && !guns[0].AlsoMelee && guns[1].AlsoMelee)
            {
                guns.Reverse();
            }
            var ranged = guns.Count > 0 ? guns[0] : null;
            var melee = blades.Count > 0 ? blades[0] : null;
            var offHand = blades.Count > 1 ? blades[1]
                : guns.Skip(blades.Count == 0 ? 1 : 0).FirstOrDefault(g => g.AlsoMelee);
            if (ranged is not null && entry.Ranged is null)
            {
                refused.Add($"{who}: {ranged.Name} (its Ranged is \"-\": it may not make Ranged Attacks)");
            }
            if (melee is not null && entry.Melee is null)
            {
                refused.Add($"{who}: {melee.Name} (its Melee is \"-\": it may not make Melee Attacks)");
            }
            if (armour < -MaxModifier)
            {
                without.Add($"{who}: Armour beyond -{MaxModifier} (played as -{MaxModifier})");
                armour = -MaxModifier;
            }
            // IMPERVIOUS (p. 55): the part of the Armour from that battlekit, at most the whole Armour.
            impervious = Math.Clamp(impervious, armour, 0);
            if (refused.Count > problems)
            {
                continue;
            }
            models.Add(new ReadyModel(name, entry.MovementInches, entry.BaseWidthMm,
                ranged?.RangeInches ?? 0, Clamp(ranged?.InjuryModifier ?? 0), ranged?.AlsoMelee == true,
                armour, melee is not null, Clamp(melee?.InjuryModifier ?? 0))
            {
                RangedDice = Dice(entry.Ranged ?? 0, who, "Ranged", without),
                MeleeDice = Dice(entry.Melee ?? 0, who, "Melee", without),
                RangedWeaponDice = Dice(ranged?.Dice ?? 0, who, $"{ranged?.Name}'s DICE", without),
                RangedWeaponInjuryDice = Dice(ranged?.InjuryDice ?? 0, who, $"{ranged?.Name}'s INJURY DICE", without),
                MeleeWeaponDice = Dice(melee?.Dice ?? 0, who, $"{melee?.Name}'s DICE", without),
                MeleeWeaponInjuryDice = Dice(melee?.InjuryDice ?? 0, who, $"{melee?.Name}'s INJURY DICE", without),
                RangedWeaponKeywords = ranged is null ? [] : Played(ranged),
                MeleeWeaponKeywords = melee is null ? [] : Played(melee),
                MeleeWeaponCleave = melee is null ? 0 : Cleave(melee, who, without),
                ImperviousArmour = impervious,
                SecondRangedWeapon = guns.Count > 1 ? Ready(guns[1], ranged: true, who, without) : null,
                SecondMeleeWeapon = offHand is null || entry.Melee is null || (melee is null && ranged?.AlsoMelee != true) ? null : Ready(offHand, ranged: false, who, without),
            });
        }
        return new MatchReady(models, refused, without.Distinct().ToList(), check.Problems);
    }

    private static int Clamp(int modifier) => Math.Clamp(modifier, -MaxModifier, MaxModifier);

    // Roster stage R3: CLEAVE (X) of a Melee Weapon, the X; beyond what the engine holds it is played at the limit.
    private static int Cleave(CatalogueEntry weapon, string who, List<string> without)
    {
        var x = weapon.Keywords.FirstOrDefault(k => k.Id == "CLEAVE")?.Number ?? 0;
        if (x > MaxCleave)
        {
            without.Add($"{who}: {weapon.Name}'s CLEAVE beyond {MaxCleave} (played as {MaxCleave})");
        }
        return Math.Clamp(x, 0, MaxCleave);
    }

    // A second weapon as the engine holds it. A dual-purpose Pistol used as a Melee Weapon keeps its PISTOL keyword
    // (the model may use its Ranged for it, p. 55) and its injury modifier and dice.
    private static ReadyWeapon Ready(CatalogueEntry weapon, bool ranged, string who, List<string> without) => new(
        weapon.Name,
        ranged ? weapon.RangeInches ?? 0 : 0,
        Clamp(weapon.InjuryModifier),
        Dice(weapon.Dice, who, $"{weapon.Name}'s DICE", without),
        Dice(weapon.InjuryDice, who, $"{weapon.Name}'s INJURY DICE", without),
        Played(weapon),
        ranged && weapon.AlsoMelee,
        ranged ? 0 : Cleave(weapon, who, without));

    // Dice the engine holds (-6 to +6 each, Rules.Core ModelProfile.MaxDice); beyond that, played at the limit and named.
    private static int Dice(int dice, string who, string what, List<string> without)
    {
        if (Math.Abs(dice) <= MaxDice)
        {
            return dice;
        }
        var played = Math.Clamp(dice, -MaxDice, MaxDice);
        without.Add($"{who}: {what} beyond {MaxDice} (played as {played:+0;-0})");
        return played;
    }

    // Roster stage R2: the weapon keywords the engine plays, by the book's id (and IGNORE's value), with the engine's
    // name. CUMBERSOME (two hands) is checked by the builder and changes nothing else in a match.
    private static readonly Dictionary<string, string> PlayedKeywords = new(StringComparer.Ordinal)
    {
        ["CRITICAL"] = "Critical",
        ["IGNORE ARMOUR"] = "IgnoreArmour",
        ["ARMOUR PIERCING"] = "ArmourPiercing",
        ["SHOTGUN"] = "Shotgun",
        ["RISKY"] = "Risky",
        ["PISTOL"] = "Pistol",
        ["IGNORE COVER"] = "IgnoreCover",
        ["IGNORE LONG RANGE"] = "IgnoreLongRange",
        ["IGNORE ELEVATED POSITION"] = "IgnoreElevatedPosition",
        ["ASSAULT"] = "Assault",
        ["HEAVY"] = "Heavy",
        ["RELOAD"] = "Reload",
        ["CUMBERSOME"] = "",
        // Roster stage R3 (U100): BLOCK is a flag; CLEAVE travels as the weapon's number of attacks.
        ["BLOCK"] = "Block",
        ["CLEAVE"] = "",
    };

    // Roster stage R3: the keywords of armour and shields the engine plays.
    private static readonly HashSet<string> PlayedArmourKeywords = new(StringComparer.Ordinal) { "IMPERVIOUS" };

    private static string KeywordName(KeywordUse k) => k.Id == "IGNORE" && k.Text is { } what ? $"IGNORE {what}" : k.Id;

    public static bool IsPlayed(KeywordUse k) => PlayedKeywords.ContainsKey(KeywordName(k));

    private static IReadOnlyList<string> Played(CatalogueEntry weapon) =>
        weapon.Keywords.Select(k => PlayedKeywords.GetValueOrDefault(KeywordName(k))).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()!;

    // What a played piece loses: its keywords (a weapon's range, injury modifier, "/Melee", DICE and INJURY DICE are
    // played; armour and shields play only their injury modifier, so their dice are named too).
    private static void Without(string who, CatalogueEntry piece, List<string> without, bool diceToo = false)
    {
        var parts = piece.Keywords.Where(k => diceToo ? !PlayedArmourKeywords.Contains(k.Id) : !IsPlayed(k)).Select(KeywordName).ToList();
        if (diceToo && piece.Dice != 0)
        {
            parts.Add($"{piece.Dice:+0;-0} DICE");
        }
        if (diceToo && piece.InjuryDice != 0)
        {
            parts.Add($"{piece.InjuryDice:+0;-0} INJURY DICE");
        }
        if (parts.Count > 0)
        {
            without.Add($"{who}: {piece.Name}'s {string.Join(", ", parts)}");
        }
    }

    private static string Words(KitCategory category) => category switch
    {
        KitCategory.Grenade => "grenades",
        KitCategory.Equipment => "equipment",
        _ => "special battlekit",
    };
}
