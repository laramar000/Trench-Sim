using Rules.Core.Dice;
using Rules.Core.State;

namespace Client.Core;

// An attack preview's dice: what each roll starts with before any Blood Markers are spent, from the rules' own numbers
// (AttackDice) and the match state. No odds and no expected result: the server rolls. HitPenalty and its reasons are
// for the Success Roll; Risky when the attacker's warband is Shaken; InjuryBonus is +1 for a Melee Attack on a Down
// target (a Critical Success adds one more, which only the dice decide); InjuryModifier is the weapon's modifier and
// the target's Armour together. AttackerMarkers can be spent by the opponent as penalty dice on the Success Roll, and
// TargetMarkers by you as extra injury dice once the attack hits.
public sealed record DicePreview(
    int HitPenalty,
    IReadOnlyList<string> HitReasons,
    bool Risky,
    int InjuryBonus,
    int InjuryModifier,
    int AttackerMarkers,
    int TargetMarkers)
{
    // Elevated position (p. 43, line-of-sight stage L3): +1 DICE on the hit roll when 3" or more above the target.
    public int HitBonus { get; init; }

    // Roster stage R1: the attacker's own dice. OwnHitDice is its Ranged or Melee Characteristic and its weapon's DICE
    // on the Success Roll (OwnHitParts names them); OwnInjuryDice is its weapon's INJURY DICE on the Injury Roll.
    public int OwnHitDice { get; init; }
    public IReadOnlyList<string> OwnHitParts { get; init; } = [];
    public int OwnInjuryDice { get; init; }

    // The Success Roll's dice before markers, as the server rolls them: +DICE and -DICE cancel in pairs (p. 15).
    public int HitNetDice => HitBonus - HitPenalty + OwnHitDice;

    // The Injury Roll's dice before markers and a Critical Success.
    public int InjuryNetDice => InjuryBonus + OwnInjuryDice;

    internal static DicePreview Of(MatchState state, ModelState attacker, ModelState target, AttackKind kind, int hitPenalty, IReadOnlyList<string> reasons, int hitBonus = 0)
    {
        var setup = state.Setup!;
        var modifier = InjuryRoll.CombineModifiers(AttackDice.InjuryModifiers(setup.ProfileOf(attacker.Id)!, setup.ProfileOf(target.Id)!, kind));
        var bonus = AttackDice.InjuryBonus(critical: false, meleeOnDownTarget: kind == AttackKind.Melee && target.Status == ModelStatus.Down);
        var profile = setup.ProfileOf(attacker.Id)!;
        return new(hitPenalty, reasons, state.IsShaken(attacker.Seat), bonus, modifier, attacker.BloodMarkers, target.BloodMarkers)
        {
            HitBonus = hitBonus,
            OwnHitDice = AttackDice.OwnDice(profile, kind),
            OwnHitParts = OwnParts(profile, kind),
            OwnInjuryDice = AttackDice.OwnInjuryDice(profile, kind),
        };
    }

    // "Ranged +1", "ranged weapon +1": the parts of the attacker's own Success Roll dice that are not zero.
    public static IReadOnlyList<string> OwnParts(ModelProfile profile, AttackKind kind)
    {
        var (characteristic, weapon) = OwnSplit(profile, kind);
        var parts = new List<string>();
        if (characteristic != 0)
        {
            parts.Add($"{(kind == AttackKind.Melee ? "Melee" : "Ranged")} {characteristic:+0;-0}");
        }
        if (weapon != 0)
        {
            parts.Add($"{WeaponWord(profile, kind)} {weapon:+0;-0}");
        }
        return parts;
    }

    // The model's characteristic and its weapon's DICE, the two parts of AttackDice.OwnDice.
    public static (int Characteristic, int Weapon) OwnSplit(ModelProfile profile, AttackKind kind) =>
        kind == AttackKind.Melee ? (profile.MeleeDice, profile.MeleeAttackDice - profile.MeleeDice) : (profile.RangedDice, profile.RangedWeaponDice);

    public static string WeaponWord(ModelProfile profile, AttackKind kind) =>
        kind == AttackKind.Melee && profile.HasMeleeWeapon ? "melee weapon" : "ranged weapon";

    // The lines the preview card and the panel show, short enough to read at a glance.
    public IReadOnlyList<string> Lines(ModelId attacker, ModelId target)
    {
        var a = attacker.Value.ToUpperInvariant();
        var t = target.Value.ToUpperInvariant();
        var lines = new List<string>
        {
            HitPenalty == 0 ? "Hit roll: no penalty dice" : $"Hit roll: -{HitPenalty} DICE ({string.Join("; ", HitReasons)})",
        };
        if (HitBonus > 0)
        {
            lines.Insert(0, $"Hit roll: +{HitBonus} DICE (Elevated position, 3\" or more above the target, p. 43)");
        }
        if (OwnHitDice != 0)
        {
            lines.Insert(0, $"Hit roll: {OwnHitDice:+0;-0} DICE ({string.Join(", ", OwnHitParts)})");
        }
        if (Risky)
        {
            lines.Add("RISKY: your warband is Shaken");
        }
        var injury = new List<string>();
        if (InjuryBonus > 0)
        {
            injury.Add($"+{InjuryBonus} DICE (target Down)");
        }
        if (OwnInjuryDice != 0)
        {
            injury.Add($"{OwnInjuryDice:+0;-0} DICE (weapon)");
        }
        if (InjuryModifier != 0)
        {
            injury.Add($"modifier {(InjuryModifier > 0 ? "+" : "")}{InjuryModifier}");
        }
        lines.Add($"Injury roll: {(injury.Count == 0 ? "no extra dice" : string.Join(", ", injury))}; +1 DICE on a critical hit");
        if (AttackerMarkers > 0)
        {
            lines.Add($"Opponent may spend {a}'s {Markers(AttackerMarkers)}: -1 DICE each");
        }
        if (TargetMarkers > 0)
        {
            lines.Add($"You may spend {t}'s {Markers(TargetMarkers)}: +1 INJURY DICE each");
        }
        return lines;
    }

    private static string Markers(int n) => $"{n} Blood Marker{(n == 1 ? "" : "s")}";
}
