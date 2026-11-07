namespace Rules.Core.Dice;

public enum SuccessOutcome
{
    Failure,
    Success,
    CriticalSuccess,
}

// The outcome is worked out from the dice, never stored separately, so a result can't
// claim an outcome its dice don't support. The constructor checks the dice match the net
// bonus or penalty dice, so a result loaded from a log is as trustworthy as a fresh one.
public sealed record SuccessRollResult
{
    public SuccessRollResult(RollRecord dice, int netDice, bool risky)
    {
        ArgumentNullException.ThrowIfNull(dice);
        dice.EnsureKeptFor(SuccessRoll.KeptDice, netDice, nameof(dice));
        Dice = dice;
        NetDice = netDice;
        Risky = risky;
    }

    public RollRecord Dice { get; }
    public int NetDice { get; }
    public bool Risky { get; }
    // Roster stage R13 (U138): a Charm of Acedia made this roll a Success whatever the dice said (a Critical Success stays one).
    // The outcome is still worked out from the dice and this flag, which the rules check against the state.
    public bool Charmed { get; init; }

    public SuccessOutcome Outcome => Charmed && SuccessRoll.Classify(Dice.Total) == SuccessOutcome.Failure ? SuccessOutcome.Success : SuccessRoll.Classify(Dice.Total);

    // A failed Risky roll ends the model's activation (or the ACTION, outside an activation).
    // Applying that is the turn sequence's job; this only reports it.
    public bool RiskyFailure => Risky && Outcome == SuccessOutcome.Failure;
}

// Success Rolls, rulebook 1.0.2 pp. 24-25. Summary in docs/rules-notes.md.
public static class SuccessRoll
{
    public const int KeptDice = 2;
    public const int SuccessFrom = 7;
    public const int CriticalFrom = 12;

    public static SuccessRollResult Roll(IDiceSource dice, int bonusDice = 0, int penaltyDice = 0, bool risky = false)
    {
        var net = RollRecord.NetDice(bonusDice, penaltyDice);
        var record = RollRecord.RollAndKeep(dice, KeptDice, net);
        return new SuccessRollResult(record, net, risky);
    }

    public static SuccessOutcome Classify(int total) => total switch
    {
        >= CriticalFrom => SuccessOutcome.CriticalSuccess,
        >= SuccessFrom => SuccessOutcome.Success,
        _ => SuccessOutcome.Failure,
    };
}
