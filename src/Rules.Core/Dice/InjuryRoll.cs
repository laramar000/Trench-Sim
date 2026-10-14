namespace Rules.Core.Dice;

public enum InjuryOutcome
{
    NoEffect,
    MinorHit,
    Down,
    OutOfAction,
}

public sealed record InjuryRollResult(
    RollRecord Dice,
    int NetDice,
    int Modifier,
    int Total,
    InjuryOutcome Outcome,
    int BloodMarkers);

// Injury Rolls, rulebook 1.0.2 pp. 46 and 48. Summary in docs/rules-notes.md.
// Extra injury dice from a critical hit or a melee attack on a Down target are added by
// the attack that causes the roll (M4); this class only takes the totals.
public static class InjuryRoll
{
    public const int NegativeModifierFloor = -3;

    public static InjuryRollResult Roll(
        IDiceSource dice,
        int bonusDice = 0,
        int penaltyDice = 0,
        IEnumerable<int>? modifiers = null,
        bool targetAlreadyDown = false)
    {
        var net = RollRecord.NetDice(bonusDice, penaltyDice);
        var record = RollRecord.RollAndKeep(dice, keep: 2, net);
        var modifier = CombineModifiers(modifiers ?? []);
        var total = record.Total + modifier;
        var outcome = Classify(total);
        return new InjuryRollResult(record, net, modifier, total, outcome, BloodMarkersFor(outcome, targetAlreadyDown));
    }

    // Negative modifiers together count for no less than -3; positive modifiers are not capped.
    public static int CombineModifiers(IEnumerable<int> modifiers)
    {
        var positive = 0;
        var negative = 0;
        foreach (var m in modifiers)
        {
            if (m > 0) positive += m;
            else negative += m;
        }
        return positive + Math.Max(negative, NegativeModifierFloor);
    }

    public static InjuryOutcome Classify(int total) => total switch
    {
        >= 9 => InjuryOutcome.OutOfAction,
        >= 7 => InjuryOutcome.Down,
        >= 2 => InjuryOutcome.MinorHit,
        _ => InjuryOutcome.NoEffect,
    };

    public static int BloodMarkersFor(InjuryOutcome outcome, bool targetAlreadyDown) => outcome switch
    {
        InjuryOutcome.MinorHit => 1,
        InjuryOutcome.Down => targetAlreadyDown ? 2 : 1,
        _ => 0,
    };
}
