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
