namespace Rules.Core.Dice;

public enum InjuryOutcome
{
    NoEffect,
    MinorHit,
    Down,
    OutOfAction,
}

// Total, outcome and Blood Markers are worked out from the dice and modifier, never stored
// separately. The constructor checks the dice match the net injury dice and that the
// modifier respects the -3 floor, so a loaded result is as trustworthy as a fresh one.
public sealed record InjuryRollResult
{
    public InjuryRollResult(RollRecord dice, int netDice, int modifier, bool targetAlreadyDown)
    {
        ArgumentNullException.ThrowIfNull(dice);
        dice.EnsureKeptFor(InjuryRoll.KeptDice, netDice, nameof(dice));
        // Combined modifiers can't be below the floor: negatives are floored, positives only add.
        ArgumentOutOfRangeException.ThrowIfLessThan(modifier, InjuryRoll.NegativeModifierFloor);
        Dice = dice;
        NetDice = netDice;
        Modifier = modifier;
        TargetAlreadyDown = targetAlreadyDown;
    }

    public RollRecord Dice { get; }
    public int NetDice { get; }
    public int Modifier { get; }
    public bool TargetAlreadyDown { get; }
    public int Total => Dice.Total + Modifier;
    public InjuryOutcome Outcome => InjuryRoll.Classify(Total);
    public int BloodMarkers => InjuryRoll.BloodMarkersFor(Outcome, TargetAlreadyDown);
}

// Injury Rolls, rulebook 1.0.2 pp. 46 and 48. Summary in docs/rules-notes.md.
// Extra injury dice from a critical hit or a melee attack on a Down target are added by
// the attack that causes the roll (M4); this class only takes the totals.
public static class InjuryRoll
{
    public const int KeptDice = 2;
    public const int NegativeModifierFloor = -3;

    public static InjuryRollResult Roll(
        IDiceSource dice,
        int bonusDice = 0,
        int penaltyDice = 0,
        IEnumerable<int>? modifiers = null,
        bool targetAlreadyDown = false)
    {
        var net = RollRecord.NetDice(bonusDice, penaltyDice);
        var record = RollRecord.RollAndKeep(dice, KeptDice, net);
        return new InjuryRollResult(record, net, CombineModifiers(modifiers ?? []), targetAlreadyDown);
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
