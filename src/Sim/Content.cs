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
                    }));
            }
            warbands.Add(new Warband(band.GetProperty("id").GetString()!, band.GetProperty("name").GetString()!, models));
        }
        return warbands;
    }

    // Roster stage R1: a model's dice (profile Ranged and Melee, weapon DICE and INJURY DICE), zero when not given.
    private static int Dice(JsonElement model, string name) =>
        model.TryGetProperty(name, out var value) ? value.GetInt32() : 0;

    public static IReadOnlyList<Warband> Load(string path) => Parse(File.ReadAllText(path));

    public static string MeleePath() => Path.Combine(AppContext.BaseDirectory, "content", "melee-warbands.json");

    public static string DefaultPath() => Path.Combine(AppContext.BaseDirectory, "content", "test-warbands.json");
}
