namespace Rules.Core.Dice;

// The full story of one roll: every die rolled, in order, and the dice that counted.
public sealed record RollRecord(IReadOnlyList<int> Rolled, IReadOnlyList<int> Kept)
{
    public int Total => Kept.Sum();

    // Rolls the base dice plus one extra die per point of net bonus or penalty dice,
    // then keeps the highest (bonus) or lowest (penalty) `keep` dice.
    // Bonus and penalty dice cancel before this is called, so only the net counts.
    internal static RollRecord RollAndKeep(IDiceSource dice, int keep, int netDice)
    {
        var rolled = new int[keep + Math.Abs(netDice)];
        for (var i = 0; i < rolled.Length; i++)
        {
            var value = dice.RollD6();
            if (value is < 1 or > 6)
            {
                throw new InvalidOperationException($"Dice source returned {value}; a D6 must be 1 to 6.");
            }
            rolled[i] = value;
        }

        var kept = netDice >= 0
            ? rolled.OrderByDescending(d => d).Take(keep)
            : rolled.OrderBy(d => d).Take(keep);
        return new RollRecord(rolled, kept.ToArray());
    }

    internal static int NetDice(int bonusDice, int penaltyDice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bonusDice);
        ArgumentOutOfRangeException.ThrowIfNegative(penaltyDice);
        return bonusDice - penaltyDice;
    }
}
