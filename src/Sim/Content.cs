using System.Text.Json;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Sim;

public sealed record ModelEntry(string Name, ModelProfile Profile);

public sealed record Warband(string Id, string Name, IReadOnlyList<ModelEntry> Models);

// Warband content lives here, outside Rules.Core: the rules engine only ever sees profiles and opaque ids.
public static class Content
{
    public static IReadOnlyList<Warband> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var warbands = new List<Warband>();
        foreach (var band in doc.RootElement.GetProperty("warbands").EnumerateArray())
        {
            var models = new List<ModelEntry>();
            foreach (var m in band.GetProperty("models").EnumerateArray())
            {
                // A 25 mm base is 25 mm across, so the radius is half the diameter (rounded down to a thousandth, even).
                var diameter = Length.FromMillimetres(m.GetProperty("baseMm").GetInt32());
                var radius = Length.FromThousandths(diameter.Thousandths / 2);
                // Roster stage R8d (U132): an oval base also gives its length; its core runs half of what the length has over the width each way.
                var halfSpan = m.TryGetProperty("baseLengthMm", out var baseLength) ? Length.FromThousandths(Math.Max(0, Length.FromMillimetres(baseLength.GetInt32()).Thousandths - diameter.Thousandths) / 2) : Length.Zero;
                models.Add(new ModelEntry(
                    m.GetProperty("name").GetString()!,
                    new ModelProfile(
                        Length.Parse(m.GetProperty("movementInches").GetString()!),
                        radius,
                        Length.Parse(m.GetProperty("rangeInches").GetString()!),
                        m.GetProperty("weaponInjuryModifier").GetInt32(),
                        m.GetProperty("armour").GetInt32(),
                        m.TryGetProperty("meleeInjuryModifier", out var melee),
                        melee.ValueKind == JsonValueKind.Number ? melee.GetInt32() : 0)
                    {
                        RangedAlsoMelee = m.TryGetProperty("rangedAlsoMelee", out var also) && also.GetBoolean(),
                        RangedDice = Dice(m, "rangedDice"),
                        MeleeDice = Dice(m, "meleeDice"),
                        RangedWeaponDice = Dice(m, "rangedWeaponDice"),
                        RangedWeaponInjuryDice = Dice(m, "rangedWeaponInjuryDice"),
                        MeleeWeaponDice = Dice(m, "meleeWeaponDice"),
                        MeleeWeaponInjuryDice = Dice(m, "meleeWeaponInjuryDice"),
                        RangedWeaponKeywords = Keywords(m, "rangedWeaponKeywords"),
                        MeleeWeaponKeywords = Keywords(m, "meleeWeaponKeywords"),
                        // Roster stage R3: after the fields they depend on (a second weapon needs a first one).
                        MeleeWeaponCleave = Dice(m, "meleeWeaponCleave"),
                        RangedWeaponBlast = Dice(m, "rangedWeaponBlast"),
                        Block = m.TryGetProperty("block", out var block) && block.GetBoolean(),
                        ImperviousArmour = Dice(m, "imperviousArmour"),
                        SecondRangedWeapon = SecondWeapon(m, "secondRangedWeapon"),
                        SecondMeleeWeapon = SecondWeapon(m, "secondMeleeWeapon"),
                        Grenade = SecondWeapon(m, "grenade"),
                        Negates = Keywords(m, "negates"),
                        Traits = Traits(m),
                        InjuryDiceTaken = Dice(m, "injuryDiceTaken"),
                        MeleeInjuryBonus = Dice(m, "meleeInjuryBonus"),
                        KeepsGoingAfterRisky = m.TryGetProperty("keepsGoingAfterRisky", out var keeps) && keeps.GetBoolean(),
                        ClimbDice = Dice(m, "climbDice"),
                        Fanfare = m.TryGetProperty("fanfare", out var fanfare) && fanfare.GetBoolean(),
                        BlendsIn = m.TryGetProperty("blendsIn", out var blends) && blends.GetBoolean(),
                        SinisterWhispers = m.TryGetProperty("sinisterWhispers", out var whispers) && whispers.GetBoolean(),
                        WardedByBlood = m.TryGetProperty("wardedByBlood", out var warded) && warded.GetBoolean(),
                        BurnsAtActivationEnd = m.TryGetProperty("burnsAtActivationEnd", out var burnsAtEnd) && burnsAtEnd.GetBoolean(),
                        BurnsWhenTakenOut = m.TryGetProperty("burnsWhenTakenOut", out var burnsWhenOut) && burnsWhenOut.GetBoolean(),
                        Treats = m.TryGetProperty("treats", out var treats) && treats.GetBoolean(),
                        FieldShrine = m.TryGetProperty("fieldShrine", out var fieldShrine) && fieldShrine.GetBoolean(),
                        HealingWaters = m.TryGetProperty("healingWaters", out var healingWaters) && healingWaters.GetBoolean(),
                        GrindsToDust = m.TryGetProperty("grindsToDust", out var grinds) && grinds.GetBoolean(),
                        Martyrdom = m.TryGetProperty("martyrdom", out var martyrdom) && martyrdom.GetBoolean(),
                        Talismans = Dice(m, "talismans"),
                        ShotsIgnoreMarkers = m.TryGetProperty("shotsIgnoreMarkers", out var shotsIgnoreMarkers) && shotsIgnoreMarkers.GetBoolean(),
                        ExpertMedic = m.TryGetProperty("expertMedic", out var expertMedic) && expertMedic.GetBoolean(),
                        FinishesTheFallen = m.TryGetProperty("finishesTheFallen", out var finishesTheFallen) && finishesTheFallen.GetBoolean(),
                        ExtraChargeDie = m.TryGetProperty("extraChargeDie", out var extraChargeDie) && extraChargeDie.GetBoolean(),
                        InspiresFaithful = m.TryGetProperty("inspiresFaithful", out var inspiresFaithful) && inspiresFaithful.GetBoolean(),
                        Aims = m.TryGetProperty("aims", out var aims) && aims.GetBoolean(),
                        Fortifies = m.TryGetProperty("fortifies", out var fortifies) && fortifies.GetBoolean(),
                        Agile = m.TryGetProperty("agile", out var agile) && agile.GetBoolean(),
                        SwiftDash = m.TryGetProperty("swiftDash", out var swiftDash) && swiftDash.GetBoolean(),
                        QuickToRise = m.TryGetProperty("quickToRise", out var quickToRise) && quickToRise.GetBoolean(),
                        Hateful = m.TryGetProperty("hateful", out var hateful) && hateful.GetBoolean(),
                        Hides = m.TryGetProperty("hides", out var hides) && hides.GetBoolean(),
                        Bodyguard = m.TryGetProperty("bodyguard", out var bodyguard) && bodyguard.GetBoolean(),
                        BaseHalfSpan = halfSpan,
                        Loudspeakers = m.TryGetProperty("loudspeakers", out var loudspeakers) && loudspeakers.GetBoolean(),
                        PuppetMaster = m.TryGetProperty("puppetMaster", out var puppetMaster) && puppetMaster.GetBoolean(),
                        Blesses = m.TryGetProperty("blesses", out var blesses) && blesses.GetBoolean(),
                        Heals = m.TryGetProperty("heals", out var heals) && heals.GetBoolean(),
                        Spells = SpellSet(m),
                        DefusesMines = m.TryGetProperty("defusesMines", out var defusesMines) && defusesMines.GetBoolean(),
                        UndeadFortitude = m.TryGetProperty("undeadFortitude", out var undeadFortitude) && undeadFortitude.GetBoolean(),
                        AbioticLife = m.TryGetProperty("abioticLife", out var abioticLife) && abioticLife.GetBoolean(),
                        Stealthy = m.TryGetProperty("stealthy", out var stealthy) && stealthy.GetBoolean(),
                        HuntsTheTough = m.TryGetProperty("huntsTheTough", out var huntsTheTough) && huntsTheTough.GetBoolean(),
                        CounterCharge = m.TryGetProperty("counterCharge", out var counterCharge) && counterCharge.GetBoolean(),
                        MementoMori = m.TryGetProperty("mementoMori", out var mementoMori) && mementoMori.GetBoolean(),
                        Perverse = m.TryGetProperty("perverse", out var perverse) && perverse.GetBoolean(),
                        MortalSuffering = m.TryGetProperty("mortalSuffering", out var mortalSuffering) && mortalSuffering.GetBoolean(),
                        SinVessel = m.TryGetProperty("sinVessel", out var sinVessel) && sinVessel.GetBoolean(),
                        SinBeacon = m.TryGetProperty("sinBeacon", out var sinBeacon) && sinBeacon.GetBoolean(),
                        Horde = m.TryGetProperty("horde", out var horde) && horde.GetBoolean(),
                        Tramples = m.TryGetProperty("tramples", out var tramples) && tramples.GetBoolean(),
                        EnforcesOrthodoxy = m.TryGetProperty("enforcesOrthodoxy", out var enforcesOrthodoxy) && enforcesOrthodoxy.GetBoolean(),
                        LaysOnHands = m.TryGetProperty("laysOnHands", out var laysOnHands) && laysOnHands.GetBoolean(),
                        HasCover = m.TryGetProperty("hasCover", out var covered) && covered.GetBoolean(),
                        DigsIn = m.TryGetProperty("digsIn", out var digs) && digs.GetBoolean(),
                    }));
            }
            warbands.Add(new Warband(band.GetProperty("id").GetString()!, band.GetProperty("name").GetString()!, models));
        }
        return warbands;
    }

    // Roster stage R3: a second ranged or Melee Weapon, null when not given: { "rangeInches": "12", "injuryModifier": 0,
    // "dice": 0, "injuryDice": 0, "keywords": [...], "alsoMelee": false, "cleave": 0 }, each field optional (range 0).
    private static Weapon? SecondWeapon(JsonElement model, string name) =>
        model.TryGetProperty(name, out var w)
            ? new Weapon(
                w.TryGetProperty("rangeInches", out var range) ? Length.Parse(range.GetString()!) : Length.Zero,
                Dice(w, "injuryModifier"),
                Dice(w, "dice"),
                Dice(w, "injuryDice"),
                Keywords(w, "keywords"),
                w.TryGetProperty("alsoMelee", out var also) && also.GetBoolean(),
                Dice(w, "cleave"))
            { Blast = Dice(w, "blast") }
            : null;

    // Roster stage R1: a model's dice (profile Ranged and Melee, weapon DICE and INJURY DICE), zero when not given.
    private static int Dice(JsonElement model, string name) =>
        model.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    // Roster stage R2: a weapon's played keywords, by the engine's names (Rules.Core WeaponKeywords); none when not given.
    private static WeaponKeywords Keywords(JsonElement model, string name) =>
        model.TryGetProperty(name, out var value)
            ? value.EnumerateArray().Aggregate(WeaponKeywords.None, (all, k) => all | Keyword(k.GetString()!))
            : WeaponKeywords.None;

    // Roster stage R5b: the model's played traits (Rules.Core ModelTraits: FEAR, TOUGH, LEADER...); none when not given.
    private static ModelTraits Traits(JsonElement model) =>
        model.TryGetProperty("traits", out var value)
            ? value.EnumerateArray().Aggregate(ModelTraits.None, (all, t) => all | Trait(t.GetString()!))
            : ModelTraits.None;

    // Roster stage R11 (U136): the Court spells the model knows (Rules.Core SpellKind names); none when not given.
    private static SpellKind SpellSet(JsonElement model) =>
        model.TryGetProperty("spells", out var value)
            ? value.EnumerateArray().Aggregate(SpellKind.None, (all, t) => all | (Enum.TryParse<SpellKind>(t.GetString(), out var spell) && Spells.IsOne(spell) ? spell : throw new FormatException($"Unknown spell {t.GetString()}.")))
            : SpellKind.None;

    private static ModelTraits Trait(string name) =>
        Enum.GetValues<ModelTraits>().FirstOrDefault(t => t != ModelTraits.None && t.ToString() == name) is var t && t != ModelTraits.None
            ? t
            : throw new FormatException($"Unknown model trait {name}.");

    private static WeaponKeywords Keyword(string name) =>
        Enum.GetValues<WeaponKeywords>().FirstOrDefault(k => k != WeaponKeywords.None && k.ToString() == name) is var k && k != WeaponKeywords.None
            ? k
            : throw new FormatException($"Unknown weapon keyword {name}.");

    public static IReadOnlyList<Warband> Load(string path) => Parse(File.ReadAllText(path));

    public static string MeleePath() => Path.Combine(AppContext.BaseDirectory, "content", "melee-warbands.json");

    public static string DefaultPath() => Path.Combine(AppContext.BaseDirectory, "content", "test-warbands.json");
}
