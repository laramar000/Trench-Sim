using System.Text.Json;

namespace Roster;

// Roster stage 5a: what a legal roster becomes in a match, as the rules engine can play it today. The engine's model
// profile holds Movement, a round base, one ranged weapon (range, injury modifier, "/Melee"), Armour and one Melee
// Weapon (injury modifier); nothing else (Rules.Core ModelProfile). So:
// - Refused, by name: what the engine cannot hold at all (an oval base, a second ranged or melee weapon, grenades,
//   equipment and other special battlekit). The match is not created while anything is refused (Jason 2026-10-30,
//   decision 2: the builder allows it, match creation refuses it by name).
// - Played without, by name: what the engine leaves out of a piece or model it does play (weapon keywords and DICE, the
//   model's Ranged and Melee DICE, its keywords, abilities and upgrades). Shown to both players before the match.
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
    int MeleeInjuryModifier);

public sealed record MatchReady(
    IReadOnlyList<ReadyModel> Models,
    IReadOnlyList<string> Refused,
    IReadOnlyList<string> PlayedWithout,
    IReadOnlyList<string> RosterProblems)
{
    // A match can be created from it: the roster is legal and nothing is refused.
    public bool Ok => RosterProblems.Count == 0 && Refused.Count == 0 && Models.Count > 0;

    // The warband in the content format of content/test-warbands.json (one warband).
    public string ToContentJson(string id, string name)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("note", "Made from a roster by the roster builder (stage 5a).");
            w.WriteStartArray("warbands");
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteString("name", name);
            w.WriteStartArray("models");
            foreach (var m in Models)
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
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
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
            if (entry.Ranged is { } r && r != 0)
            {
                without.Add($"{who}: its Ranged {r:+0;-0} DICE");
            }
            if (entry.Melee is { } me && me != 0)
            {
                without.Add($"{who}: its Melee {me:+0;-0} DICE");
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
            CatalogueEntry? ranged = null;
            CatalogueEntry? melee = null;
            var armour = entry.Armour;
            foreach (var piece in kit)
            {
                switch (piece.Category)
                {
                    case KitCategory.Ranged when ranged is null && piece.RangeInches is { } range && range <= MaxRangeInches:
                        ranged = piece;
                        Without(who, piece, without);
                        break;
                    case KitCategory.Melee when melee is null:
                        melee = piece;
                        Without(who, piece, without);
                        break;
                    case KitCategory.Ranged or KitCategory.Melee:
                        refused.Add($"{who}: {piece.Name} (the engine plays one ranged and one melee weapon per model)");
                        break;
                    case KitCategory.Armour or KitCategory.Shield:
                        armour += piece.InjuryModifier;
                        Without(who, piece, without);
                        break;
                    default:
                        refused.Add($"{who}: {piece.Name} (the engine has no {Words(piece.Category)} yet)");
                        break;
                }
            }
            if (armour < -MaxModifier)
            {
                without.Add($"{who}: Armour beyond -{MaxModifier} (played as -{MaxModifier})");
                armour = -MaxModifier;
            }
            if (refused.Count > problems)
            {
                continue;
            }
            models.Add(new ReadyModel(name, entry.MovementInches, entry.BaseWidthMm,
                ranged?.RangeInches ?? 0, Clamp(ranged?.InjuryModifier ?? 0), ranged?.AlsoMelee == true,
                armour, melee is not null, Clamp(melee?.InjuryModifier ?? 0)));
        }
        return new MatchReady(models, refused, without.Distinct().ToList(), check.Problems);
    }

    private static int Clamp(int modifier) => Math.Clamp(modifier, -MaxModifier, MaxModifier);

    // What a played piece loses: its keywords, DICE and INJURY DICE (only range, injury modifier and "/Melee" are played).
    private static void Without(string who, CatalogueEntry piece, List<string> without)
    {
        var parts = piece.Keywords.Select(k => k.Id).ToList();
        if (piece.Dice != 0)
        {
            parts.Add($"{piece.Dice:+0;-0} DICE");
        }
        if (piece.InjuryDice != 0)
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
