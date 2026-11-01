using System.Text.Json;

namespace Roster;

// Roster stage 5a: what a legal roster becomes in a match, as the rules engine can play it today. The engine's model
// profile holds Movement, a round base, one ranged weapon (range, injury modifier, "/Melee"), Armour and one Melee
// Weapon (injury modifier); nothing else (Rules.Core ModelProfile). So:
// - Refused, by name: what the engine cannot hold at all (an oval base, a third ranged or melee weapon, a second type of
//   grenade, equipment whose own rule the engine does not play, and other special battlekit). The match is not created while
//   anything is refused (Jason 2026-10-30, decision 2: the builder allows it, match creation refuses it by name).
// - Played without, by name: what the engine leaves out of a piece or model it does play (weapon keywords, the model's
//   keywords, abilities and upgrades). Shown to both players before the match.
// Roster stage R1: the model's Ranged and Melee DICE and its weapons' DICE and INJURY DICE are played.
// Roster stage R2: the weapon keywords in PlayedKeywords are played; the others are named.
// Roster stage R3 (U100): two ranged and two melee weapons (a dual-purpose Pistol may be the second Melee Weapon), CLEAVE,
// BLOCK and IMPERVIOUS armour and shields are played; a third weapon of a kind is refused by name.
// Roster stage R4a (U101): one type of grenade (p. 69) is played as the model's grenade; SHRAPNEL, GAS and FIRE are
// played, and so is NEGATE SHRAPNEL, GAS or FIRE (and DEMONIC, which is NEGATE FIRE, p. 54) from the model's keywords and
// its battlekit. Equipment is played when all it has is NEGATE keywords; a piece's own rule is named.
// Roster stage R4b (U102): BLAST (X") travels as the weapon's blast radius; R4c: SCATTER is played.
// Roster stage R5b (U106): the model keywords FEAR, NEGATE FEAR, TOUGH, STRONG (NEGATE HEAVY), NEGATE HEAVY and LEADER are
// played from the model's keywords and its battlekit, and so is the equipment in PlayedEquipment (its own rule as dice or
// a keyword). A CONSUMABLE piece the model may use when first deployed is always used in a match.
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

    // Roster stage R4a: the model's grenade, and the weapon effects it NEGATEs (the engine's names: Shrapnel, Gas, Fire).
    public ReadyWeapon? Grenade { get; init; }
    public IReadOnlyList<string> Negates { get; init; } = [];

    // Roster stage R4b: BLAST (X") of the ranged weapon, in inches (0: none).
    public int RangedWeaponBlast { get; init; }

    // Roster stage R5b: the model keywords the engine plays, by its names (Rules.Core ModelTraits: Fear, NegateFear,
    // Tough, NegateHeavy, Leader).
    public IReadOnlyList<string> Traits { get; init; } = [];

    // Roster stage R5c (U107): INJURY DICE on the Injury Rolls of attacks that hit the model, and the INJURY MODIFIER its
    // Melee Attacks add.
    public int InjuryDiceTaken { get; init; }
    public int MeleeInjuryBonus { get; init; }

    // Roster stage R5d (U108): a failed Risky roll does not end its activation (Pontiff's Crucifix), +DICE on its Risky
    // climb rolls (Mountaineer Kit), +1 DICE on friendly Dash rolls within 4" (Musical Instrument), and Cover gives -2
    // DICE against it (Cloak of the Mountain).
    public bool KeepsGoingAfterRisky { get; init; }
    public int ClimbDice { get; init; }
    public bool Fanfare { get; init; }
    public bool BlendsIn { get; init; }

    // Roster stage R5e (U109): -1 DICE to Ranged Attacks at the model or within 3" of it (Urn of the Bitter Ashes), and
    // the first BLOOD MARKER placed on it is ignored (a Sacrificial Lamb, sacrificed when it is deployed).
    public bool SinisterWhispers { get; init; }
    public bool WardedByBlood { get; init; }

    // Roster stage R5f (U110): the COVER keyword, always (a shield with it) or while dug in with a Shovel.
    public bool HasCover { get; init; }
    public bool DigsIn { get; init; }
}

// Roster stage R3: a second weapon, as Rules.Core Weapon holds it (RangeInches 0 for a Melee Weapon).
public sealed record ReadyWeapon(string Name, int RangeInches, int InjuryModifier, int Dice, int InjuryDice, IReadOnlyList<string> Keywords, bool AlsoMelee, int Cleave)
{
    // Roster stage R4b: BLAST (X") in inches (0: none).
    public int Blast { get; init; }
}

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
                    foreach (var (field, words) in new[] { ("rangedWeaponKeywords", m.RangedWeaponKeywords), ("meleeWeaponKeywords", m.MeleeWeaponKeywords), ("negates", m.Negates), ("traits", m.Traits) })
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
                        ("imperviousArmour", m.ImperviousArmour), ("rangedWeaponBlast", m.RangedWeaponBlast),
                        ("injuryDiceTaken", m.InjuryDiceTaken), ("meleeInjuryBonus", m.MeleeInjuryBonus),
                        ("climbDice", m.ClimbDice),
                    })
                    {
                        if (dice != 0)
                        {
                            w.WriteNumber(field, dice);
                        }
                    }
                    foreach (var (field, on) in new[] { ("keepsGoingAfterRisky", m.KeepsGoingAfterRisky), ("fanfare", m.Fanfare), ("blendsIn", m.BlendsIn), ("sinisterWhispers", m.SinisterWhispers), ("wardedByBlood", m.WardedByBlood), ("hasCover", m.HasCover), ("digsIn", m.DigsIn) })
                    {
                        if (on)
                        {
                            w.WriteBoolean(field, true);
                        }
                    }
                    foreach (var (field, weapon) in new[] { ("secondRangedWeapon", m.SecondRangedWeapon), ("secondMeleeWeapon", m.SecondMeleeWeapon), ("grenade", m.Grenade) })
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
                        foreach (var (part, value) in new[] { ("injuryModifier", weapon.InjuryModifier), ("dice", weapon.Dice), ("injuryDice", weapon.InjuryDice), ("cleave", weapon.Cleave), ("blast", weapon.Blast) })
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
    public const int MaxBlast = 6;

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
            var negates = new HashSet<string>(StringComparer.Ordinal);
            var traits = new HashSet<string>(StringComparer.Ordinal);
            var gains = new Gains(negates, traits);
            var unplayed = entry.Keywords.Where(k => !Gain(k, gains)).ToList();
            var rangedBonus = 0;
            var meleeBonus = 0;
            var injuryTaken = 0;
            var meleeInjury = 0;
            var (keepsGoing, climbDice, fanfare, blendsIn) = (false, 0, false, false);
            var (whispers, warded) = (false, false);
            var (hasCover, digsIn) = (false, false);
            var ammunition = new List<CatalogueEntry>();
            if (unplayed.Count > 0)
            {
                without.Add($"{who}: its keywords ({string.Join(", ", unplayed.Select(KeywordText))})");
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
            CatalogueEntry? grenade = null;
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
                        // Roster stage R5f (U110): a shield with COVER gives the model the keyword (p. 54).
                        hasCover |= piece.Keywords.Any(k => k.Id == "COVER");
                        Without(who, piece, without, diceToo: true, gains);
                        break;
                    case KitCategory.Grenade when grenade is null && piece.RangeInches is { } throwRange && throwRange <= MaxRangeInches:
                        grenade = piece;
                        Without(who, piece, without);
                        break;
                    case KitCategory.Grenade when grenade is not null:
                        refused.Add($"{who}: {piece.Name} (a model carries one type of grenade, p. 69)");
                        break;
                    case KitCategory.Grenade:
                        refused.Add($"{who}: {piece.Name} (its range is beyond what the engine allows)");
                        break;
                    case KitCategory.Equipment when piece.Keywords.Any(k => k.Id == "AMMUNITION"):
                        ammunition.Add(piece);
                        break;
                    case KitCategory.Equipment when PlayedEquipment.TryGetValue(piece.Id, out var played):
                        Without(who, piece, without, diceToo: true, gains, ruleToo: false);
                        if (played.NotFor is { } notFor && entry.Keywords.Any(k => k.Id == notFor))
                        {
                            without.Add($"{who}: {piece.Name} (no effect on an {notFor} model, p. {piece.Rule?.Page})");
                            break;
                        }
                        rangedBonus += played.RangedDice;
                        meleeBonus += played.MeleeDice;
                        injuryTaken += played.InjuryDiceTaken;
                        meleeInjury += played.MeleeInjury;
                        keepsGoing |= played.KeepsGoing;
                        climbDice += played.ClimbDice;
                        fanfare |= played.Fanfare;
                        blendsIn |= played.BlendsIn;
                        whispers |= played.Whispers;
                        warded |= played.Warded;
                        digsIn |= played.DigsIn;
                        if (played.Unplayed is { } part)
                        {
                            without.Add($"{who}: {piece.Name} {part}");
                        }
                        if (played.Trait is { } trait)
                        {
                            traits.Add(trait);
                        }
                        break;
                    case KitCategory.Equipment when piece.Rule is null && piece.Keywords.Count > 0 && piece.Keywords.All(k => Gain(k, new Gains([], []))):
                        Without(who, piece, without, diceToo: true, gains);
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
            // Roster stage R5c (U107): AMMUNITION (X) gives one ranged weapon X (p. 52).
            var (ammoFirst, ammoSecond) = Ammunition(who, ammunition, guns, refused, without);
            var ranged = guns.Count > 0 ? guns[0] : null;
            var melee = blades.Count > 0 ? blades[0] : null;
            var offHand = blades.Count > 1 ? blades[1]
                : guns.Skip(blades.Count == 0 ? 1 : 0).FirstOrDefault(g => g.AlsoMelee);
            if (ranged is not null && entry.Ranged is null)
            {
                refused.Add($"{who}: {ranged.Name} (its Ranged is \"-\": it may not make Ranged Attacks)");
            }
            if (grenade is not null && entry.Ranged is null)
            {
                refused.Add($"{who}: {grenade.Name} (its Ranged is \"-\": it may not throw grenades)");
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
                RangedDice = Dice(entry.Ranged is { } r ? r + rangedBonus : 0, who, "Ranged", without),
                MeleeDice = Dice(entry.Melee is { } me ? me + meleeBonus : 0, who, "Melee", without),
                RangedWeaponDice = Dice((ranged?.Dice ?? 0) + (ammoFirst?.Dice ?? 0), who, $"{ranged?.Name}'s DICE", without),
                RangedWeaponInjuryDice = Dice(ranged?.InjuryDice ?? 0, who, $"{ranged?.Name}'s INJURY DICE", without),
                MeleeWeaponDice = Dice(melee?.Dice ?? 0, who, $"{melee?.Name}'s DICE", without),
                MeleeWeaponInjuryDice = Dice(melee?.InjuryDice ?? 0, who, $"{melee?.Name}'s INJURY DICE", without),
                RangedWeaponKeywords = ranged is null ? [] : WithAmmunition(Played(ranged), ammoFirst),
                MeleeWeaponKeywords = melee is null ? [] : Played(melee),
                MeleeWeaponCleave = melee is null ? 0 : Cleave(melee, who, without),
                RangedWeaponBlast = ranged is null ? 0 : BlastOf(ranged, who, without),
                ImperviousArmour = impervious,
                SecondRangedWeapon = guns.Count > 1 ? Loaded(Ready(guns[1], ranged: true, who, without), ammoSecond, who, without) : null,
                // A dual-purpose gun used as the second Melee Weapon keeps its AMMUNITION (the weapon gains the keyword).
                SecondMeleeWeapon = offHand is null || entry.Melee is null || (melee is null && ranged?.AlsoMelee != true) ? null
                    : Loaded(Ready(offHand, ranged: false, who, without), guns.IndexOf(offHand) switch { 0 => ammoFirst, 1 => ammoSecond, _ => null }, who, without),
                Grenade = grenade is null ? null : Ready(grenade, ranged: true, who, without),
                Negates = [.. NegateNames.Where(n => negates.Contains(n.Key)).Select(n => n.Value)],
                Traits = [.. TraitNames.Where(traits.Contains)],
                InjuryDiceTaken = Dice(injuryTaken, who, "INJURY DICE against it", without),
                MeleeInjuryBonus = MeleeInjury(meleeInjury, who, without),
                KeepsGoingAfterRisky = keepsGoing,
                ClimbDice = Dice(climbDice, who, "climb DICE", without),
                Fanfare = fanfare,
                BlendsIn = blendsIn,
                SinisterWhispers = whispers,
                WardedByBlood = warded,
                HasCover = hasCover,
                DigsIn = digsIn,
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

    // Roster stage R4b: BLAST (X") of a ranged weapon or grenade, the X; beyond what the engine holds it is played at the
    // limit.
    private static int BlastOf(CatalogueEntry weapon, string who, List<string> without)
    {
        var x = weapon.Keywords.FirstOrDefault(k => k.Id == "BLAST")?.Number ?? 0;
        if (x > MaxBlast)
        {
            without.Add($"{who}: {weapon.Name}'s BLAST beyond {MaxBlast}\" (played as {MaxBlast}\")");
        }
        return Math.Clamp(x, 0, MaxBlast);
    }

    // A second weapon as the engine holds it. A dual-purpose Pistol used as a Melee Weapon keeps its PISTOL keyword
    // (the model may use its Ranged for it, p. 55) and its injury modifier and dice.
    private static ReadyWeapon Ready(CatalogueEntry weapon, bool ranged, string who, List<string> without) => new ReadyWeapon(
        weapon.Name,
        ranged ? weapon.RangeInches ?? 0 : 0,
        Clamp(weapon.InjuryModifier),
        Dice(weapon.Dice, who, $"{weapon.Name}'s DICE", without),
        Dice(weapon.InjuryDice, who, $"{weapon.Name}'s INJURY DICE", without),
        Played(weapon),
        ranged && weapon.AlsoMelee,
        ranged ? 0 : Cleave(weapon, who, without))
    {
        Blast = ranged ? BlastOf(weapon, who, without) : 0,
    };

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
        // Roster stage R4a (U101): +1 BLOOD MARKER after the Injury Roll, even on No Effect.
        ["SHRAPNEL"] = "Shrapnel",
        ["GAS"] = "Gas",
        ["FIRE"] = "Fire",
        // Roster stage R4b (U102): BLAST travels as the weapon's blast radius; R4c: SCATTER is a flag.
        ["BLAST"] = "",
        ["SCATTER"] = "Scatter",
        // Roster stage R5 (U104): DEADLY Injury Rolls are 3D6.
        ["DEADLY"] = "Deadly",
    };

    // Roster stage R4a: the NEGATE values the engine plays, by the book's word, with the engine's name, in its order.
    private static readonly IReadOnlyList<KeyValuePair<string, string>> NegateNames =
        [new("SHRAPNEL", "Shrapnel"), new("GAS", "Gas"), new("FIRE", "Fire")];

    // A model keyword or a piece's keyword that is a played NEGATE (or DEMONIC, which is NEGATE FIRE, p. 54): adds it and
    // says so.
    private static bool AddNegate(KeywordUse k, HashSet<string> negates)
    {
        var what = k.Id == "DEMONIC" ? "FIRE" : k.Id == "NEGATE" ? k.Text : null;
        if (what is null || !NegateNames.Any(n => n.Key == what))
        {
            return false;
        }
        negates.Add(what);
        return true;
    }

    // Roster stage R5b (U106): the model keywords the engine plays, by the book's word (NEGATE's value for NEGATE), with the
    // engine's name. STRONG is NEGATE HEAVY in a match (its two-handed weapon part is the builder's, p. 57).
    private static readonly Dictionary<string, string> TraitKeywords = new(StringComparer.Ordinal)
    {
        ["FEAR"] = "Fear",
        ["NEGATE FEAR"] = "NegateFear",
        ["TOUGH"] = "Tough",
        ["STRONG"] = "NegateHeavy",
        ["NEGATE HEAVY"] = "NegateHeavy",
        ["LEADER"] = "Leader",
    };

    // The engine's trait names in its order (Rules.Core ModelTraits).
    private static readonly string[] TraitNames = ["Fear", "NegateFear", "Tough", "NegateHeavy", "Leader"];

    // What a model takes from its keywords and battlekit: NEGATE values and traits.
    private sealed record Gains(HashSet<string> Negates, HashSet<string> Traits);

    // A keyword the engine plays on the model: a played NEGATE, a trait, or HELD and CONSUMABLE (HELD's one hand is the
    // builder's, p. 55; CONSUMABLE matters only in a campaign, p. 53). Adds it and says so.
    private static bool Gain(KeywordUse k, Gains gains)
    {
        if (AddNegate(k, gains.Negates))
        {
            return true;
        }
        if (TraitKeywords.TryGetValue(k.Id == "NEGATE" ? $"NEGATE {k.Text}" : k.Id, out var trait))
        {
            gains.Traits.Add(trait);
            return true;
        }
        return k.Id is "HELD" or "CONSUMABLE";
    }

    // Roster stage R5b (U106): equipment whose own rule the engine plays, by catalogue id: dice added to the model's
    // Ranged or Melee Characteristic, or a keyword it gains (NotFor: a keyword whose models it does nothing for). Its
    // keywords are played as above.
    // Roster stage R5c (U107): or INJURY DICE on attacks that hit the model, or an INJURY MODIFIER on its Melee Attacks.
    // Roster stage R5f (U110): or COVER while dug in (Unplayed: the part of its rule a match plays without).
    // Roster stage R5e (U109): or the Urn's -1 DICE aura on Ranged Attacks, or the Lamb's ward against the first BLOOD MARKER.
    // Roster stage R5d (U108): or what a failed Risky roll ends, climb DICE, the Dash DICE it gives friends within 4", or
    // Cover's -2 DICE against the model.
    private sealed record PlayedPiece(int RangedDice = 0, int MeleeDice = 0, string? Trait = null, string? NotFor = null, int InjuryDiceTaken = 0, int MeleeInjury = 0,
        bool KeepsGoing = false, int ClimbDice = 0, bool Fanfare = false, bool BlendsIn = false, bool Whispers = false, bool Warded = false,
        bool DigsIn = false, string? Unplayed = null);

    private static readonly Dictionary<string, PlayedPiece> PlayedEquipment = new(StringComparer.Ordinal)
    {
        ["unholy-relic"] = new(),
        ["troop-flag"] = new(),
        ["iron-capirote"] = new(),
        ["anfarro"] = new(MeleeDice: 1),
        ["sacred-geometry"] = new(RangedDice: 1),
        ["compound-eyes-helmet"] = new(RangedDice: 1),
        ["elixir-of-life"] = new(Trait: "Tough", NotFor: "ARTIFICIAL"),
        ["assassins-leaf"] = new(Trait: "NegateHeavy"),
        ["holy-smoke"] = new(Trait: "NegateFear", InjuryDiceTaken: -1),
        ["zealots-draught"] = new(Trait: "NegateFear", InjuryDiceTaken: -1),
        ["grovelling-devotee"] = new(MeleeInjury: 1),
        ["pontiffs-crucifix"] = new(KeepsGoing: true),
        ["mountaineer-kit"] = new(ClimbDice: 1),
        ["musical-instrument"] = new(Fanfare: true),
        ["cloak-of-the-mountain"] = new(BlendsIn: true),
        ["urn-of-the-bitter-ashes"] = new(Whispers: true),
        ["sacrificial-lamb"] = new(Warded: true),
        ["shovel"] = new(DigsIn: true, Unplayed: "used as a 2-handed Melee Weapon (p. 85)"),
        ["bronze-shovel"] = new(DigsIn: true, Unplayed: "used as a Trench Club"),
    };

    // Roster stage R5c (U107): AMMUNITION (X), what it gives the weapon: +X DICE or a keyword the engine plays.
    private sealed record Ammo(string Name, int Dice, string? Keyword);

    // The guns AMMUNITION may go on: not one with BLAST, FIRE, GAS or SHRAPNEL (p. 52). Alchemical Ammunition goes only on
    // an Alaybozan, Halberd-Gun, Jezzail or Siege Jezzail (even the Alaybozan, which has SHRAPNEL; Warbands p. 76).
    private static readonly string[] AlchemicalGuns = ["alaybozan", "halberd-gun", "jezzail", "siege-jezzail"];

    private static bool TakesAmmunition(CatalogueEntry ammunition, CatalogueEntry gun) => ammunition.Id == "alchemical-ammunition"
        ? AlchemicalGuns.Contains(gun.Id)
        : !gun.Keywords.Any(k => k.Id is "BLAST" or "FIRE" or "GAS" or "SHRAPNEL");

    // The AMMUNITION a model's first and second guns get. One piece on a model with one gun it can go on is played; the
    // player chooses the gun when the model is deployed, so a piece two guns could take, or a second piece, is refused
    // by name (the engine has no deployment choice yet); a piece no gun can take does nothing and is named.
    private static (Ammo? First, Ammo? Second) Ammunition(string who, List<CatalogueEntry> ammunition, List<CatalogueEntry> guns, List<string> refused, List<string> without)
    {
        if (ammunition.Count == 0)
        {
            return (null, null);
        }
        if (ammunition.Count > 1)
        {
            refused.Add($"{who}: {string.Join(" and ", ammunition.Select(a => a.Name))} (which gun each is used for is chosen at deployment, not built yet)");
            return (null, null);
        }
        var piece = ammunition[0];
        var takers = guns.Select((g, i) => (g, i)).Where(x => TakesAmmunition(piece, x.g)).ToList();
        if (takers.Count == 0)
        {
            without.Add($"{who}: {piece.Name} (no ranged weapon it can be used with, p. 52)");
            return (null, null);
        }
        if (takers.Count > 1)
        {
            refused.Add($"{who}: {piece.Name} (which of its guns it is used for is chosen at deployment, not built yet)");
            return (null, null);
        }
        var what = piece.Keywords.First(k => k.Id == "AMMUNITION").Text ?? "";
        var ammo = what.EndsWith(" DICE", StringComparison.Ordinal) && int.TryParse(what[..^5], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var dice)
            ? new Ammo(piece.Name, dice, null)
            : PlayedKeywords.GetValueOrDefault(what) is { Length: > 0 } keyword ? new Ammo(piece.Name, 0, keyword) : null;
        if (ammo is null)
        {
            refused.Add($"{who}: {piece.Name} (the engine does not play AMMUNITION ({what}) yet)");
            return (null, null);
        }
        return takers[0].i == 0 ? (ammo, null) : (null, ammo);
    }

    private static IReadOnlyList<string> WithAmmunition(IReadOnlyList<string> keywords, Ammo? ammo) =>
        ammo?.Keyword is { } k && !keywords.Contains(k) ? [.. keywords, k] : keywords;

    private static ReadyWeapon Loaded(ReadyWeapon weapon, Ammo? ammo, string who, List<string> without) => ammo is null ? weapon
        : weapon with { Dice = Dice(weapon.Dice + ammo.Dice, who, $"{weapon.Name}'s DICE", without), Keywords = WithAmmunition(weapon.Keywords, ammo) };

    // Grovelling Devotees: +1 INJURY MODIFIER each, up to what the engine holds.
    private static int MeleeInjury(int bonus, string who, List<string> without)
    {
        if (bonus > MaxModifier)
        {
            without.Add($"{who}: melee INJURY MODIFIER beyond +{MaxModifier} (played as +{MaxModifier})");
        }
        return Math.Clamp(bonus, 0, MaxModifier);
    }

    private static string KeywordText(KeywordUse k) => k.Id == "NEGATE" && k.Text is { } what ? $"NEGATE {what}" : k.Id;

    // Roster stage R3: the keywords of armour and shields the engine plays.
    private static readonly HashSet<string> PlayedArmourKeywords = new(StringComparer.Ordinal) { "IMPERVIOUS", "COVER" };

    private static string KeywordName(KeywordUse k) => k.Id == "IGNORE" && k.Text is { } what ? $"IGNORE {what}" : k.Id;

    public static bool IsPlayed(KeywordUse k) => PlayedKeywords.ContainsKey(KeywordName(k));

    private static IReadOnlyList<string> Played(CatalogueEntry weapon) =>
        weapon.Keywords.Select(k => PlayedKeywords.GetValueOrDefault(KeywordName(k))).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()!;

    // What a played piece loses: its keywords (a weapon's range, injury modifier, "/Melee", DICE and INJURY DICE are
    // played; armour and shields play only their injury modifier, so their dice are named too).
    // Roster stage R4a: armour, shields and equipment play their NEGATE SHRAPNEL, GAS or FIRE (into negates), and every
    // played piece's own rule is named.
    // Roster stage R5b: and their played model keywords (into gains); a piece in PlayedEquipment plays its rule too.
    private static void Without(string who, CatalogueEntry piece, List<string> without, bool diceToo = false, Gains? gains = null, bool ruleToo = true)
    {
        var parts = piece.Keywords
            .Where(k => diceToo ? !PlayedArmourKeywords.Contains(k.Id) && !(gains is not null && Gain(k, gains)) : !IsPlayed(k))
            .Select(k => k.Id == "NEGATE" ? KeywordText(k) : KeywordName(k)).ToList();
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
        if (ruleToo && piece.Rule is { } rule)
        {
            without.Add($"{who}: {piece.Name}'s own rule (p. {rule.Page})");
        }
    }

    private static string Words(KitCategory category) => category switch
    {
        KitCategory.Grenade => "grenades",
        KitCategory.Equipment => "equipment",
        _ => "special battlekit",
    };
}
