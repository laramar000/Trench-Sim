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
    // Roster stage R8d (U132): an oval base's length in mm (BaseMm is its width); 0 for a round base.
    public int BaseLengthMm { get; init; }

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

    // Roster stage R5g (U112): 1 BLOOD MARKER on each enemy within 1" when the model ends an activation (Crown of
    // Hellfire), or when it is taken Out of Action (Damned Pact).
    public bool BurnsAtActivationEnd { get; init; }
    public bool BurnsWhenTakenOut { get; init; }

    // Roster stage R5h (U113): the Treat ACTION (Medi-kit) and +1 DICE to it (Holy Water of Lalibela).
    public bool Treats { get; init; }

    // Field Shrine (Battlekit p. 82; U127): the model carries one, and may deploy it with the model.
    public bool FieldShrine { get; init; }
    public bool HealingWaters { get; init; }

    // Roster stage R5i (U114): the Grind to Dust ACTION (Piston Legs).
    public bool GrindsToDust { get; init; }

    // Roster stage R5k (U116): Talismans (Blessed Icon, Unholy Trinket), each used once per game after a failed Risky roll.
    public int Talismans { get; init; }

    // Roster stage R6a (U117): New Antioch abilities (Absolute Faith, Expert Medic, Finish the Fallen, Shock Charge, Onward
    // Christian Soldiers!).
    public bool ShotsIgnoreMarkers { get; init; }
    public bool ExpertMedic { get; init; }
    public bool FinishesTheFallen { get; init; }
    public bool ExtraChargeDie { get; init; }
    public bool InspiresFaithful { get; init; }

    // Roster stage R6b (U118): the Aim and Fortify ACTIONS.
    public bool Aims { get; init; }
    public bool Fortifies { get; init; }

    // Roster stage R6c (U119): abilities that change a number.
    public bool Agile { get; init; }
    public bool SwiftDash { get; init; }
    public bool QuickToRise { get; init; }
    public bool Hateful { get; init; }
    public bool Bodyguard { get; init; }
    public bool Loudspeakers { get; init; }
    public bool PuppetMaster { get; init; }
    public bool Hides { get; init; }
    public bool DefusesMines { get; init; }
    public bool UndeadFortitude { get; init; }
    public bool AbioticLife { get; init; }
    public bool Stealthy { get; init; }
    public bool HuntsTheTough { get; init; }
    public bool CounterCharge { get; init; }
    public bool MementoMori { get; init; }

    // Roster stage R6d (U120): the Trample ACTION, and Overwhelming Horde's DICE on its attacks without a weapon.
    public bool Tramples { get; init; }
    public bool Horde { get; init; }

    // Roster stage R6e (U121): the Enforced Orthodoxy and Laying on of Hands ACTIONS.
    public bool EnforcesOrthodoxy { get; init; }
    public bool LaysOnHands { get; init; }

    // Roster stage R5j (U115): the Trigger Martyrdom Device ACTION.
    public bool Martyrdom { get; init; }
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
                    if (m.BaseLengthMm > 0)
                    {
                        w.WriteNumber("baseLengthMm", m.BaseLengthMm);
                    }
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
                        ("climbDice", m.ClimbDice), ("talismans", m.Talismans),
                    })
                    {
                        if (dice != 0)
                        {
                            w.WriteNumber(field, dice);
                        }
                    }
                    foreach (var (field, on) in new[] { ("keepsGoingAfterRisky", m.KeepsGoingAfterRisky), ("hateful", m.Hateful), ("bodyguard", m.Bodyguard), ("loudspeakers", m.Loudspeakers), ("puppetMaster", m.PuppetMaster), ("hides", m.Hides), ("defusesMines", m.DefusesMines), ("fanfare", m.Fanfare), ("blendsIn", m.BlendsIn), ("sinisterWhispers", m.SinisterWhispers), ("wardedByBlood", m.WardedByBlood), ("hasCover", m.HasCover), ("digsIn", m.DigsIn), ("burnsAtActivationEnd", m.BurnsAtActivationEnd), ("burnsWhenTakenOut", m.BurnsWhenTakenOut), ("treats", m.Treats), ("fieldShrine", m.FieldShrine), ("healingWaters", m.HealingWaters), ("grindsToDust", m.GrindsToDust), ("martyrdom", m.Martyrdom), ("shotsIgnoreMarkers", m.ShotsIgnoreMarkers), ("expertMedic", m.ExpertMedic), ("finishesTheFallen", m.FinishesTheFallen), ("extraChargeDie", m.ExtraChargeDie), ("inspiresFaithful", m.InspiresFaithful), ("aims", m.Aims), ("fortifies", m.Fortifies), ("agile", m.Agile), ("swiftDash", m.SwiftDash), ("quickToRise", m.QuickToRise), ("undeadFortitude", m.UndeadFortitude), ("abioticLife", m.AbioticLife), ("stealthy", m.Stealthy), ("huntsTheTough", m.HuntsTheTough), ("counterCharge", m.CounterCharge), ("mementoMori", m.MementoMori), ("tramples", m.Tramples), ("horde", m.Horde), ("enforcesOrthodoxy", m.EnforcesOrthodoxy), ("laysOnHands", m.LaysOnHands) })
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

    // Roster stage R5j (U115): the upgrade that gives the Trigger Martyrdom Device ACTION.
    public const string MartyrdomUpgrade = "martyrdom-device";

    // Roster stage R6f (U122): what an upgrade the engine plays gives, beside its keywords (STRONG, FEAR): Ranged or Melee
    // DICE, a Melee Characteristic set to a number (Kavass: +0 instead of -1), +1 DICE to the Dash roll, Counter-Charge,
    // and a base that changes for one entry (a Trench Pilgrim with Zealot Strength has a 32 mm base).
    private sealed record PlayedUpgrade(int RangedDice = 0, int MeleeDice = 0, int? MeleeTo = null, bool Dash = false, bool CounterCharge = false, string? BaseFor = null);

    private static readonly Dictionary<string, PlayedUpgrade> PlayedUpgrades = new(StringComparer.Ordinal)
    {
        ["legionnaire-ranged"] = new(RangedDice: 1),
        ["legionnaire-melee"] = new(MeleeDice: 1),
        ["zealot-strength"] = new(BaseFor: "trench-pilgrim"),
        ["janissary-veteran"] = new(CounterCharge: true),
        ["fear"] = new(),
        ["rapid-assault"] = new(Dash: true),
        ["blind-rage"] = new(Dash: true),
        ["kavass"] = new(MeleeTo: 0),
    };

    private const int ZealotBaseMm = 32;
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

            if (entry.BaseWidthMm > MaxBaseMm || entry.BaseLengthMm > MaxBaseMm || entry.MovementInches > MaxMovementInches)
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
            var (burnsAtEnd, burnsWhenOut) = (false, false);
            var (treats, healingWaters, grinds, martyrdom) = (false, false, false, false);
            var fieldShrine = false;
            var talismans = 0;
            CatalogueEntry? digger = null;
            string? diggerUse = null;
            var ammunition = new List<CatalogueEntry>();
            if (unplayed.Count > 0)
            {
                without.Add($"{who}: its keywords ({string.Join(", ", unplayed.Select(KeywordText))})");
            }
            // Roster stage R6a (U117): the abilities the engine plays, by name; the rest are named.
            var abilities = entry.Abilities.Select(a => a.Name).Where(PlayedAbilities.Contains).ToHashSet(StringComparer.Ordinal);
            if (entry.Abilities.Where(a => !abilities.Contains(a.Name)).Select(a => a.Name).ToList() is { Count: > 0 } skipped)
            {
                without.Add($"{who}: its abilities ({string.Join(", ", skipped)})");
            }
            var baseMm = entry.BaseWidthMm;
            var (upgradeDash, upgradeCounter) = (false, false);
            foreach (var upgradeId in model.Upgrades)
            {
                // Roster stage R5j (U115): the Martyrdom Device upgrade is played (its Trigger ACTION).
                if (upgradeId == MartyrdomUpgrade && entry.Upgrades.Any(u => u.Id == upgradeId))
                {
                    martyrdom = true;
                }
                // Roster stage R6f (U122): the upgrades the engine plays: their keywords and what PlayedUpgrades says.
                else if (entry.Upgrades.FirstOrDefault(u => u.Id == upgradeId) is { } upgrade && PlayedUpgrades.TryGetValue(upgrade.Id, out var played))
                {
                    if (upgrade.AddsKeywords.Where(k => !Gain(k, gains)).ToList() is { Count: > 0 } lost)
                    {
                        without.Add($"{who}: {upgrade.Name}'s keywords ({string.Join(", ", lost.Select(KeywordText))})");
                    }
                    rangedBonus += played.RangedDice;
                    meleeBonus += played.MeleeTo is { } to && entry.Melee is { } own ? to - own : 0;
                    meleeBonus += played.MeleeDice;
                    upgradeDash |= played.Dash;
                    upgradeCounter |= played.CounterCharge;
                    if (played.BaseFor == entry.Id)
                    {
                        baseMm = ZealotBaseMm;
                    }
                }
                else if (entry.Upgrades.FirstOrDefault(u => u.Id == upgradeId) is { } unplayedUpgrade)
                {
                    without.Add($"{who}: {unplayedUpgrade.Name}");
                }
            }

            var kit = entry.StartsWith.Concat(model.Kit)
                .Select(k => catalogue.Find(KitRef.Split(k).Kit)).OfType<CatalogueEntry>().ToList();
            // Roster stage R6c (U119): Nailed Cross, an Iron Capirote and a Gas Mask the model always has.
            if (abilities.Contains(NailedCross))
            {
                kit.AddRange(NailedCrossKit.Where(id => kit.All(k => k.Id != id)).Select(catalogue.Find).OfType<CatalogueEntry>());
            }
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
                        // A Shovel's keywords are named below, unless it is the model's second Shovel.
                        if (played.AsWeapon is null || digger is not null)
                        {
                            Without(who, piece, without, diceToo: true, gains, ruleToo: false);
                        }
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
                        burnsAtEnd |= played.BurnsAtEnd;
                        burnsWhenOut |= played.BurnsWhenOut;
                        treats |= played.Treats;
                        fieldShrine |= played.FieldShrine;
                        healingWaters |= played.HealingWaters;
                        grinds |= played.Grinds;
                        talismans += played.Talisman ? 1 : 0;
                        if (played.AsWeapon is { } use && digger is null)
                        {
                            (digger, diggerUse) = (piece, use);
                        }
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
            // After roster stage R5f (U111): a Shovel can be used as a 2-handed Melee Weapon instead of the model's other Melee
            // Weapons (p. 85), a Bronze Shovel as a Trench Club (Warbands p. 78). A model with no other Melee Weapon (nor
            // a dual-purpose Pistol) fights with it, and the Bronze Shovel keeps its HEAVY; with another one, which to use
            // would be a choice the engine has no command for, so the model fights with its own and the Shovel's melee use
            // is named.
            if (digger is not null)
            {
                if (blades.Count == 0 && !guns.Any(g => g.AlsoMelee))
                {
                    var club = diggerUse == "trench-club" ? catalogue.Find("trench-club") : null;
                    blades.Add(digger with
                    {
                        Category = KitCategory.Melee,
                        Hands = club?.Hands ?? 2,
                        Dice = club?.Dice ?? 0,
                        InjuryDice = club?.InjuryDice ?? 0,
                        InjuryModifier = club?.InjuryModifier ?? 0,
                        Keywords = [.. (club?.Keywords ?? []), .. digger.Keywords],
                        Rule = null,
                    });
                    Without(who, blades[0], without);
                }
                else
                {
                    Without(who, digger, without, diceToo: true, gains, ruleToo: false);
                    without.Add($"{who}: {digger.Name} used as a {(diggerUse == "trench-club" ? "Trench Club" : "2-handed Melee Weapon")} (it fights with its other Melee Weapon)");
                }
            }
            // Roster stage R6d (U120): an ability that fights without a weapon gives a model with no other Melee Weapon (nor a
            // dual-purpose Pistol) its attack: no weapon DICE, INJURY DICE or modifier (U114), with the keywords the ability
            // names. With a weapon it fights with that, and the ability's attack is named.
            var horde = false;
            if (NaturalWeapon(abilities, entry) is { } natural)
            {
                if (blades.Count == 0 && !guns.Any(g => g.AlsoMelee))
                {
                    blades.Add(natural);
                    horde = abilities.Contains(OverwhelmingHorde);
                }
                else
                {
                    without.Add($"{who}: {natural.Name}'s attack without a weapon (it fights with its Melee Weapon)");
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
            var ready = new ReadyModel(name, entry.MovementInches, baseMm,
                ranged?.RangeInches ?? 0, Clamp(ranged?.InjuryModifier ?? 0), ranged?.AlsoMelee == true,
                armour, melee is not null, Clamp(melee?.InjuryModifier ?? 0))
            {
                BaseLengthMm = entry.BaseLengthMm ?? 0,
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
                // Roster stage R6c (U119): a model with Memento Mori can never be TOUGH.
                Traits = [.. TraitNames.Where(t => traits.Contains(t) && !(t == "Tough" && abilities.Contains(MementoMori)))],
                InjuryDiceTaken = Dice(injuryTaken + (abilities.Contains(Corpulent) ? -2 : 0) + (abilities.Contains(ArtificialLife) ? -1 : 0), who, "INJURY DICE against it", without),
                MeleeInjuryBonus = MeleeInjury(meleeInjury, who, without),
                KeepsGoingAfterRisky = keepsGoing,
                ClimbDice = Dice(climbDice, who, "climb DICE", without),
                Fanfare = fanfare,
                BlendsIn = blendsIn,
                SinisterWhispers = whispers,
                WardedByBlood = warded,
                HasCover = hasCover,
                DigsIn = digsIn,
                BurnsAtActivationEnd = burnsAtEnd,
                BurnsWhenTakenOut = burnsWhenOut,
                Treats = treats,
                FieldShrine = fieldShrine,
                HealingWaters = healingWaters,
                GrindsToDust = grinds,
                Martyrdom = martyrdom,
                Talismans = Talismans(talismans, who, without),
                ShotsIgnoreMarkers = abilities.Contains(AbsoluteFaith),
                ExpertMedic = abilities.Contains(ExpertMedic),
                FinishesTheFallen = abilities.Contains(FinishTheFallen),
                ExtraChargeDie = abilities.Contains(ShockCharge),
                InspiresFaithful = abilities.Contains(OnwardSoldiers),
                Aims = abilities.Contains(AimAction),
                Fortifies = abilities.Contains(FortifyAction),
                Agile = abilities.Contains(Agile),
                SwiftDash = upgradeDash || abilities.Contains(FrighteningSpeed) || abilities.Contains(LopingDash) || abilities.Contains(MadDash),
                QuickToRise = abilities.Contains(FrighteningSpeed),
                Hateful = abilities.Contains(HatefulName),
                Bodyguard = abilities.Contains(BodyguardName),
                Loudspeakers = abilities.Contains(LoudspeakersName),
                PuppetMaster = abilities.Contains(PuppetMasterName),
                Hides = abilities.Contains(HideAction),
                DefusesMines = abilities.Contains(DefuseMineName),
                UndeadFortitude = abilities.Contains(UndeadFortitude),
                AbioticLife = abilities.Contains(AbioticLife),
                Stealthy = abilities.Contains(StealthGenerator),
                HuntsTheTough = abilities.Contains(Mubarizun),
                CounterCharge = upgradeCounter || abilities.Contains(CounterCharge),
                MementoMori = abilities.Contains(MementoMori),
                Tramples = abilities.Contains(TrampleAction),
                Horde = horde,
                EnforcesOrthodoxy = abilities.Contains(EnforcedOrthodoxy),
                LaysOnHands = abilities.Contains(LayingOnHands),
            };
            models.Add(Unburdened(ready, abilities, grenade));
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
    // Roster stage R6a (U117): the unit abilities a match plays, by their names in the faction lists.
    private const string AbsoluteFaith = "Absolute Faith";
    private const string ExpertMedic = "Expert Medic";
    private const string FinishTheFallen = "Finish the Fallen";
    private const string ShockCharge = "Shock Charge";
    private const string OnwardSoldiers = "Onward Christian Soldiers!";
    private const string AssaultDrill = "Assault Drill";
    private const string Demolition = "Battlefield Demolition";
    // Roster stage R6b (U118).
    private const string AimAction = "Aim ACTION";
    private const string FortifyAction = "Fortify ACTION";
    // Roster stage R6c (U119).
    private const string Agile = "Agile";
    private const string HatefulName = "Hateful";
    private const string BodyguardName = "Bodyguard";
    private const string LoudspeakersName = "Loudspeakers ACTION";
    private const string PuppetMasterName = "Puppet Master ACTION";
    private const string HideAction = "Hide ACTION";
    private const string DefuseMineName = "Defuse Mine";
    private const string FrighteningSpeed = "Frightening Speed";
    private const string LopingDash = "Loping Dash";
    private const string MadDash = "Mad Dash";
    private const string UndeadFortitude = "Undead Fortitude";
    private const string AbioticLife = "Abiotic Life";
    private const string StealthGenerator = "Stealth Generator";
    private const string Mubarizun = "Mubarizun";
    private const string CounterCharge = "Counter-Charge";
    private const string MementoMori = "Memento Mori";
    private const string Corpulent = "Corpulent";
    private const string ArtificialLife = "Artificial Life";
    private const string NailedCross = "Nailed Cross";
    private static readonly string[] NailedCrossKit = ["iron-capirote", "gas-mask"];
    // Roster stage R6d (U120).
    private const string TeethAndClaws = "Teeth & Claws";
    private const string TeethAndClawsLion = "Teeth and Claws";
    private const string FeebleFlailing = "Feeble Flailing";
    private const string CrushingBlows = "Crushing Blows";
    private const string PoisonStingers = "Poison Stingers";
    private const string OverwhelmingHorde = "Overwhelming Horde";
    private const string TrampleAction = "Trample ACTION";
    // Roster stage R6e (U121).
    private const string EnforcedOrthodoxy = "Enforced Orthodoxy ACTION";
    private const string LayingOnHands = "Laying on of Hands ACTION";

    // The attack an ability makes without a weapon: Poison Stingers CLEAVE 2 and SHRAPNEL, Crushing Blows CLEAVE 2,
    // Feeble Flailing -1 DICE, Teeth & Claws and Overwhelming Horde (its DICE come in the match) plain; null without one.
    private static CatalogueEntry? NaturalWeapon(HashSet<string> abilities, ModelEntry entry)
    {
        var name = new[] { PoisonStingers, CrushingBlows, FeebleFlailing, TeethAndClaws, TeethAndClawsLion, OverwhelmingHorde }.FirstOrDefault(abilities.Contains);
        if (name is null)
        {
            return null;
        }
        IReadOnlyList<KeywordUse> keywords = name switch
        {
            PoisonStingers => [new("CLEAVE", 2, null), new("SHRAPNEL", null, null)],
            CrushingBlows => [new("CLEAVE", 2, null)],
            _ => [],
        };
        var page = entry.Abilities.First(a => a.Name == name).Page;
        return new CatalogueEntry("natural-weapon", name, KitCategory.Melee, 0, null, false, null, keywords, name == FeebleFlailing ? -1 : 0, 0, 0, null, [],
            entry.Source with { Page = page }, DataStatus.Verified, null, Naming.Keep, false);
    }

    private static readonly HashSet<string> PlayedAbilities = new(StringComparer.Ordinal)
    {
        AbsoluteFaith, ExpertMedic, FinishTheFallen, ShockCharge, OnwardSoldiers, AssaultDrill, Demolition, AimAction, FortifyAction,
        Agile, FrighteningSpeed, LopingDash, MadDash, UndeadFortitude, AbioticLife, StealthGenerator, Mubarizun, CounterCharge, MementoMori, Corpulent, ArtificialLife, NailedCross,
        TeethAndClaws, TeethAndClawsLion, FeebleFlailing, CrushingBlows, PoisonStingers, OverwhelmingHorde, TrampleAction,
        EnforcedOrthodoxy, LayingOnHands, HatefulName, BodyguardName, LoudspeakersName, PuppetMasterName, DefuseMineName, HideAction,
    };

    // Assault Drill: HEAVY has no effect for 1 Melee Weapon (the first that has it); Battlefield Demolition: for 1
    // Satchel Charge. The builder still allows only 1 HEAVY weapon.
    private static ReadyModel Unburdened(ReadyModel model, HashSet<string> abilities, CatalogueEntry? grenade)
    {
        const string heavy = "Heavy";
        if (abilities.Contains(AssaultDrill))
        {
            if (model.MeleeWeaponKeywords.Contains(heavy))
            {
                model = model with { MeleeWeaponKeywords = [.. model.MeleeWeaponKeywords.Where(k => k != heavy)] };
            }
            else if (model.SecondMeleeWeapon is { } second && second.Keywords.Contains(heavy))
            {
                model = model with { SecondMeleeWeapon = second with { Keywords = [.. second.Keywords.Where(k => k != heavy)] } };
            }
        }
        if (abilities.Contains(Demolition) && grenade?.Id == SatchelCharge && model.Grenade is { } satchel)
        {
            model = model with { Grenade = satchel with { Keywords = [.. satchel.Keywords.Where(k => k != heavy)] } };
        }
        return model;
    }

    private const string SatchelCharge = "satchel-charge";

    // Roster stage R5k (U116): the engine plays up to two Talismans on one model.
    private static int Talismans(int count, string who, List<string> without)
    {
        if (count <= MaxTalismans)
        {
            return count;
        }
        without.Add($"{who}: Talismans beyond {MaxTalismans}");
        return MaxTalismans;
    }

    private const int MaxTalismans = 2;

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
        // Roster stage R6a (U117): keywords other rules look for.
        ["PRINCIPALITY"] = "Principality",
        ["CULT"] = "Cult",
        ["PILGRIM"] = "Pilgrim",
    };

    // The engine's trait names in its order (Rules.Core ModelTraits).
    private static readonly string[] TraitNames = ["Fear", "NegateFear", "Tough", "NegateHeavy", "Leader", "Principality", "Cult", "Demonic", "Pilgrim"];

    // What a model takes from its keywords and battlekit: NEGATE values and traits.
    private sealed record Gains(HashSet<string> Negates, HashSet<string> Traits);

    // A keyword the engine plays on the model: a played NEGATE, a trait, or HELD and CONSUMABLE (HELD's one hand is the
    // builder's, p. 55; CONSUMABLE matters only in a campaign, p. 53). Adds it and says so.
    private static bool Gain(KeywordUse k, Gains gains)
    {
        // DEMONIC is NEGATE FIRE below, and a keyword Finish the Fallen looks for (roster stage R6a).
        if (k.Id == "DEMONIC")
        {
            gains.Traits.Add("Demonic");
        }
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
    // Roster stage R5g (U112): or BLOOD MARKERS on enemies within 1" (BurnsAtEnd, BurnsWhenOut).
    // Roster stage R5h (U113): or the Treat ACTION and Holy Water's +1 DICE to it.
    // Roster stage R5e (U109): or the Urn's -1 DICE aura on Ranged Attacks, or the Lamb's ward against the first BLOOD MARKER.
    // Roster stage R5d (U108): or what a failed Risky roll ends, climb DICE, the Dash DICE it gives friends within 4", or
    // Cover's -2 DICE against the model.
    private sealed record PlayedPiece(int RangedDice = 0, int MeleeDice = 0, string? Trait = null, string? NotFor = null, int InjuryDiceTaken = 0, int MeleeInjury = 0,
        bool KeepsGoing = false, int ClimbDice = 0, bool Fanfare = false, bool BlendsIn = false, bool Whispers = false, bool Warded = false,
        bool DigsIn = false, string? Unplayed = null, string? AsWeapon = null, bool BurnsAtEnd = false, bool BurnsWhenOut = false, bool Treats = false, bool HealingWaters = false, bool Grinds = false, bool Talisman = false, bool FieldShrine = false);

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
        // The Shovels' melee use (AsWeapon): a 2-handed Melee Weapon with no other rules, or a Trench Club.
        ["shovel"] = new(DigsIn: true, AsWeapon: "2-handed"),
        ["bronze-shovel"] = new(DigsIn: true, AsWeapon: "trench-club"),
        // Roster stage R5g (U112): BLOOD MARKERS on the enemies within 1".
        ["crown-of-hellfire"] = new(BurnsAtEnd: true),
        ["damned-pact"] = new(BurnsWhenOut: true),
        // Roster stage R5h (U113): the Treat ACTION, and Holy Water's +1 DICE to it.
        ["medi-kit"] = new(Treats: true),
        // Field Shrine (U127): the shrine is deployed with its carrier and counts for Morale; it is not a model.
        ["field-shrine"] = new(FieldShrine: true),
        ["holy-water-of-lalibela"] = new(HealingWaters: true),
        // Roster stage R5i (U114).
        ["piston-legs"] = new(Grinds: true),
        // Roster stage R5k (U116): a Talisman each, used once per game after a failed Risky roll.
        ["blessed-icon"] = new(Talisman: true),
        ["unholy-trinket"] = new(Talisman: true),
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
            .Where(k => diceToo ? !PlayedArmourKeywords.Contains(k.Id) && !(gains is not null && Gain(k, gains)) && !(k.Id == "DEPLOYABLE" && piece.Id == "field-shrine") : !IsPlayed(k))
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
