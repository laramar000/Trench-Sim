namespace Rules.Core.Dice;

public enum SuccessOutcome
{
    Failure,
    Success,
    CriticalSuccess,
}

public sealed record SuccessRollResult(RollRecord Dice, int NetDice, SuccessOutcome Outcome, bool Risky)
{
    // A failed Risky roll ends the model's activation (or the ACTION, outside an activation).
    // Applying that is the turn sequence's job; this only reports it.
    public bool RiskyFailure => Risky && Outcome == SuccessOutcome.Failure;
}

// Success Rolls, rulebook 1.0.2 pp. 24-25. Summary in docs/rules-notes.md.
public static class SuccessRoll
{
    public const int SuccessFrom = 7;
    public const int CriticalFrom = 12;

    public static SuccessRollResult Roll(IDiceSource dice, int bonusDice = 0, int penaltyDice = 0, bool risky = false)
    {
        var net = RollRecord.NetDice(bonusDice, penaltyDice);
        var record = RollRecord.RollAndKeep(dice, keep: 2, net);
        return new SuccessRollResult(record, net, Classify(record.Total), risky);
    }

    public static SuccessOutcome Classify(int total) => total switch
    {
        >= CriticalFrom => SuccessOutcome.CriticalSuccess,
        >= SuccessFrom => SuccessOutcome.Success,
        _ => SuccessOutcome.Failure,
    };
}
