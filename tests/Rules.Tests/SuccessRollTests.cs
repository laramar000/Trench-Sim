using Rules.Core.Dice;

namespace Rules.Tests;

public class SuccessRollTests
{
    // Every band edge of the Success Roll Table with plain 2D6.
    [Theory]
    [Trait("Rule", "1.0.2 Success Roll Table p.24")]
    [InlineData(1, 1, SuccessOutcome.Failure)]          // 2, lowest possible
    [InlineData(3, 3, SuccessOutcome.Failure)]          // 6, top of Failure
    [InlineData(3, 4, SuccessOutcome.Success)]          // 7, bottom of Success
    [InlineData(5, 6, SuccessOutcome.Success)]          // 11, top of Success
    [InlineData(6, 6, SuccessOutcome.CriticalSuccess)]  // 12, Critical threshold
    public void BandEdges(int first, int second, SuccessOutcome expected)
    {
        var dice = new FixedDice(first, second);

        var result = SuccessRoll.Roll(dice);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(first + second, result.Dice.Total);
        Assert.Equal(0, dice.Remaining);
    }

    // Classify covers totals a 2-dice roll can't reach, in case later rules add to the total.
    [Theory]
    [Trait("Rule", "1.0.2 Success Roll Table p.24")]
    [InlineData(0, SuccessOutcome.Failure)]
    [InlineData(6, SuccessOutcome.Failure)]
    [InlineData(7, SuccessOutcome.Success)]
    [InlineData(11, SuccessOutcome.Success)]
    [InlineData(12, SuccessOutcome.CriticalSuccess)]
    [InlineData(13, SuccessOutcome.CriticalSuccess)]
    public void ClassifyBandEdges(int total, SuccessOutcome expected)
    {
        Assert.Equal(expected, SuccessRoll.Classify(total));
    }

    // Net DICE from -3 to +3, all from the same fixed dice: 2 + |net| dice are rolled,
    // and the highest two (bonus) or lowest two (penalty) are kept.
    // -3 to +3 is the range the plan asks tests to cover. It is not a rules limit:
    // no cap was found in the 1.0.2 rulebook, so NetDiceAreNotCapped checks beyond it.
    [Theory]
    [Trait("Rule", "1.0.2 +Dice And -Dice p.25")]
    [InlineData(0, 3, new[] { 2, 5, 1, 6, 4 }, new[] { 1, 2 }, SuccessOutcome.Failure)]
    [InlineData(0, 2, new[] { 2, 5, 1, 6 }, new[] { 1, 2 }, SuccessOutcome.Failure)]
    [InlineData(0, 1, new[] { 2, 5, 1 }, new[] { 1, 2 }, SuccessOutcome.Failure)]
    [InlineData(0, 0, new[] { 2, 5 }, new[] { 5, 2 }, SuccessOutcome.Success)]
    [InlineData(1, 0, new[] { 2, 5, 1 }, new[] { 5, 2 }, SuccessOutcome.Success)]
    [InlineData(2, 0, new[] { 2, 5, 1, 6 }, new[] { 6, 5 }, SuccessOutcome.Success)]
    [InlineData(3, 0, new[] { 2, 5, 1, 6, 6 }, new[] { 6, 6 }, SuccessOutcome.CriticalSuccess)]
    public void EveryDiceCountFromMinusThreeToPlusThree(
        int bonus, int penalty, int[] rolls, int[] expectedKept, SuccessOutcome expected)
    {
        var dice = new FixedDice(rolls);

        var result = SuccessRoll.Roll(dice, bonus, penalty);

        Assert.Equal(bonus - penalty, result.NetDice);
        Assert.Equal(rolls, result.Dice.Rolled);
        Assert.Equal(expectedKept, result.Dice.Kept);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, dice.Remaining);
    }

    // No cap on bonus or penalty dice was found in the 1.0.2 rulebook (automated text check of
    // pp. 15 and 25, not human-verified), so none is applied.
    [Theory]
    [Trait("Rule", "1.0.2 +Dice And -Dice p.25")]
    [InlineData(5, 0, new[] { 6, 5 })]
    [InlineData(0, 5, new[] { 1, 1 })]
    public void NetDiceAreNotCapped(int bonus, int penalty, int[] expectedKept)
    {
        var dice = new FixedDice(1, 2, 3, 4, 5, 6, 1);

        var result = SuccessRoll.Roll(dice, bonus, penalty);

        Assert.Equal(7, result.Dice.Rolled.Count);
        Assert.Equal(expectedKept, result.Dice.Kept);
        Assert.Equal(0, dice.Remaining);
    }

    // Bonus and penalty dice cancel one for one before any extra dice are rolled.
    [Theory]
    [Trait("Rule", "1.0.2 +Dice And -Dice p.25")]
    [InlineData(1, 1, 2)]
    [InlineData(3, 3, 2)]
    [InlineData(3, 1, 4)]
    [InlineData(1, 3, 4)]
    public void BonusAndPenaltyDiceCancel(int bonus, int penalty, int expectedRolled)
    {
        var dice = new FixedDice(4, 4, 4, 4, 4, 4, 4, 4);

        var result = SuccessRoll.Roll(dice, bonus, penalty);

        Assert.Equal(expectedRolled, result.Dice.Rolled.Count);
        Assert.Equal(2, result.Dice.Kept.Count);
    }

    [Fact]
    [Trait("Rule", "1.0.2 +Dice And -Dice p.25")]
    public void CancelledDiceKeepThePlainTwoDice()
    {
        var result = SuccessRoll.Roll(new FixedDice(1, 6), bonusDice: 2, penaltyDice: 2);

        Assert.Equal(new[] { 1, 6 }, result.Dice.Rolled);
        Assert.Equal(7, result.Dice.Total);
    }

    // A penalty die can turn a would-be Critical into a plain Success.
    [Fact]
    [Trait("Rule", "1.0.2 +Dice And -Dice p.25")]
    public void PenaltyDiceCanDenyACritical()
    {
        var result = SuccessRoll.Roll(new FixedDice(6, 6, 5), penaltyDice: 1);

        Assert.Equal(11, result.Dice.Total);
        Assert.Equal(SuccessOutcome.Success, result.Outcome);
    }

    [Theory]
    [Trait("Rule", "1.0.2 Risky p.24")]
    [InlineData(1, 1, true, true)]
    [InlineData(3, 4, true, false)]
    [InlineData(1, 1, false, false)]
    public void RiskyFailureIsReported(int first, int second, bool risky, bool expectedRiskyFailure)
    {
        var result = SuccessRoll.Roll(new FixedDice(first, second), risky: risky);

        Assert.Equal(expectedRiskyFailure, result.RiskyFailure);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void NegativeDiceCountsAreRejected(int bonus, int penalty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SuccessRoll.Roll(new FixedDice(3, 3), bonus, penalty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void DiceOutsideOneToSixAreRejected(int badValue)
    {
        Assert.Throws<InvalidOperationException>(() => SuccessRoll.Roll(new FixedDice(3, badValue)));
    }
}
