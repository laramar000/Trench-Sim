using Rules.Core.Dice;

namespace Rules.Tests;

public class InjuryRollTests
{
    // Every band edge of the Injury Roll Table, reached with dice and flat modifiers.
    [Theory]
    [Trait("Rule", "1.0.2 Injury Roll Table p.48")]
    [InlineData(1, 1, -3, -1, InjuryOutcome.NoEffect)]
    [InlineData(1, 1, -1, 1, InjuryOutcome.NoEffect)]    // top of No Effect
    [InlineData(1, 1, 0, 2, InjuryOutcome.MinorHit)]     // bottom of Minor Hit
    [InlineData(3, 3, 0, 6, InjuryOutcome.MinorHit)]     // top of Minor Hit
    [InlineData(3, 4, 0, 7, InjuryOutcome.Down)]         // bottom of Down
    [InlineData(4, 4, 0, 8, InjuryOutcome.Down)]         // top of Down
    [InlineData(4, 5, 0, 9, InjuryOutcome.OutOfAction)]  // bottom of Out of Action
    [InlineData(6, 6, 2, 14, InjuryOutcome.OutOfAction)]
    public void BandEdges(int first, int second, int modifier, int expectedTotal, InjuryOutcome expected)
    {
        var dice = new FixedDice(first, second);

        var result = InjuryRoll.Roll(dice, modifiers: [modifier]);

        Assert.Equal(expectedTotal, result.Total);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, dice.Remaining);
    }

    [Theory]
    [Trait("Rule", "1.0.2 Injury Roll Table p.48")]
    [InlineData(InjuryOutcome.NoEffect, false, 0)]
    [InlineData(InjuryOutcome.MinorHit, false, 1)]
    [InlineData(InjuryOutcome.Down, false, 1)]
    [InlineData(InjuryOutcome.Down, true, 2)]
    [InlineData(InjuryOutcome.OutOfAction, false, 0)]
    public void BloodMarkersPerResult(InjuryOutcome outcome, bool alreadyDown, int expected)
    {
        Assert.Equal(expected, InjuryRoll.BloodMarkersFor(outcome, alreadyDown));
    }

    [Fact]
    [Trait("Rule", "1.0.2 Injury Roll Table p.48")]
    public void DownResultOnADownTargetGivesTwoBloodMarkers()
    {
        var result = InjuryRoll.Roll(new FixedDice(3, 4), targetAlreadyDown: true);

        Assert.Equal(InjuryOutcome.Down, result.Outcome);
        Assert.Equal(2, result.BloodMarkers);
    }

    // Negative modifiers together count for no less than -3; positive ones are uncapped.
    [Theory]
    [Trait("Rule", "1.0.2 Injuries p.46")]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { -1, -1 }, -2)]
    [InlineData(new[] { -3 }, -3)]
    [InlineData(new[] { -2, -2 }, -3)]
    [InlineData(new[] { -1, -1, -1, -1 }, -3)]
    [InlineData(new[] { -5 }, -3)]
    [InlineData(new[] { 2, 3 }, 5)]
    [InlineData(new[] { 2, -5 }, -1)]  // the cap applies to the negatives before positives are added
    public void ModifierCap(int[] modifiers, int expected)
    {
        Assert.Equal(expected, InjuryRoll.CombineModifiers(modifiers));
    }

    // Regression for the owner's decision of 14 October 2026: the -3 floor applies to the
    // negative modifiers before positive ones are added, so +2 and -5 give -1, not -3.
    [Fact]
    [Trait("Rule", "1.0.2 Injuries p.46")]
    public void PlusTwoAndMinusFiveGiveMinusOne()
    {
        var result = InjuryRoll.Roll(new FixedDice(4, 4), modifiers: [2, -5]);

        Assert.Equal(-1, result.Modifier);
        Assert.Equal(7, result.Total);
        Assert.Equal(InjuryOutcome.Down, result.Outcome);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Injuries p.46")]
    public void CappedModifierIsAppliedToTheRoll()
    {
        var result = InjuryRoll.Roll(new FixedDice(4, 5), modifiers: [-2, -2]);

        Assert.Equal(-3, result.Modifier);
        Assert.Equal(6, result.Total);
        Assert.Equal(InjuryOutcome.MinorHit, result.Outcome);
    }

    // INJURY DICE work like DICE on Success Rolls: they cancel, then 2 are kept.
    [Theory]
    [Trait("Rule", "1.0.2 Injuries p.46")]
    [InlineData(0, 3, new[] { 2, 5, 1, 6, 4 }, new[] { 1, 2 })]
    [InlineData(0, 1, new[] { 2, 5, 1 }, new[] { 1, 2 })]
    [InlineData(0, 0, new[] { 2, 5 }, new[] { 5, 2 })]
    [InlineData(1, 0, new[] { 2, 5, 1 }, new[] { 5, 2 })]
    [InlineData(3, 0, new[] { 2, 5, 1, 6, 6 }, new[] { 6, 6 })]
    [InlineData(2, 2, new[] { 2, 5 }, new[] { 5, 2 })]
    public void InjuryDiceCancelThenKeepTwo(int bonus, int penalty, int[] rolls, int[] expectedKept)
    {
        var dice = new FixedDice(rolls);

        var result = InjuryRoll.Roll(dice, bonus, penalty);

        Assert.Equal(rolls, result.Dice.Rolled);
        Assert.Equal(expectedKept, result.Dice.Kept);
        Assert.Equal(expectedKept.Sum(), result.Total);
        Assert.Equal(0, dice.Remaining);
    }
}
