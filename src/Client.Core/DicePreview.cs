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

    internal static DicePreview Of(MatchState state, ModelState attacker, ModelState target, AttackKind kind, int hitPenalty, IReadOnlyList<string> reasons, int hitBonus = 0)
    {
        var setup = state.Setup!;
        var modifier = InjuryRoll.CombineModifiers(AttackDice.InjuryModifiers(setup.ProfileOf(attacker.Id)!, setup.ProfileOf(target.Id)!, kind));
        var bonus = AttackDice.InjuryBonus(critical: false, meleeOnDownTarget: kind == AttackKind.Melee && target.Status == ModelStatus.Down);
        return new(hitPenalty, reasons, state.IsShaken(attacker.Seat), bonus, modifier, attacker.BloodMarkers, target.BloodMarkers) { HitBonus = hitBonus };
    }

    // The lines the preview card and the panel show, short enough to read at a glance.
    public IReadOnlyList<string> Lines(ModelId attacker, ModelId target)
    {
        var a = attacker.Value.ToUpperInvariant();
        var t = target.Value.ToUpperInvariant();
        var lines = new List<string>
        {
            HitPenalty == 0 ? "Hit roll: no penalty dice" : $"Hit roll: -{HitPenalty} DICE ({string.Join(", ", HitReasons)})",
        };
        if (HitBonus > 0)
        {
            lines.Insert(0, $"Hit roll: +{HitBonus} DICE (elevated position, 3\" or more above the target, p. 43)");
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
