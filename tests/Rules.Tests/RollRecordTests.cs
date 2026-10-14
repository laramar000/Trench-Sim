using Rules.Core.Dice;

namespace Rules.Tests;

// Roll results are the server's record of what happened: they must not change after the
// roll, must compare by their dice, and must refuse contents no real roll could produce.
public class RollRecordTests
{
    // Immutability

    [Fact]
    public void DiceListsCannotBeCastBackToArrays()
    {
        var result = SuccessRoll.Roll(new FixedDice(3, 4));

        Assert.IsNotType<int[]>(result.Dice.Rolled);
        Assert.IsNotType<int[]>(result.Dice.Kept);
    }

    [Fact]
    public void DiceListsRejectWritesThroughIList()
    {
        var result = SuccessRoll.Roll(new FixedDice(3, 4));

        Assert.Throws<NotSupportedException>(() => ((IList<int>)result.Dice.Kept)[0] = 6);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)result.Dice.Rolled)[0] = 6);
        Assert.Equal(7, result.Dice.Total);
        Assert.Equal(SuccessOutcome.Success, result.Outcome);
    }

    [Fact]
    public void ChangingTheSourceArraysDoesNotChangeTheRecord()
    {
        var rolled = new[] { 2, 5 };
        var kept = new[] { 5, 2 };
        var record = new RollRecord(rolled, kept);

        rolled[0] = 6;
        kept[0] = 6;

        Assert.Equal(new[] { 2, 5 }, record.Rolled);
        Assert.Equal(new[] { 5, 2 }, record.Kept);
        Assert.Equal(7, record.Total);
    }

    [Theory]
    [InlineData(nameof(SuccessRollResult.Outcome))]
    [InlineData(nameof(SuccessRollResult.Dice))]
    [InlineData(nameof(SuccessRollResult.NetDice))]
    [InlineData(nameof(SuccessRollResult.Risky))]
    public void SuccessResultPropertiesHaveNoSetters(string property)
    {
        // Without a setter or init accessor, `with { Outcome = ... }` does not compile.
        Assert.Null(typeof(SuccessRollResult).GetProperty(property)!.SetMethod);
    }

    [Theory]
    [InlineData(nameof(InjuryRollResult.Outcome))]
    [InlineData(nameof(InjuryRollResult.Total))]
    [InlineData(nameof(InjuryRollResult.BloodMarkers))]
    [InlineData(nameof(InjuryRollResult.Modifier))]
    [InlineData(nameof(InjuryRollResult.Dice))]
    public void InjuryResultPropertiesHaveNoSetters(string property)
    {
        Assert.Null(typeof(InjuryRollResult).GetProperty(property)!.SetMethod);
    }

    // Equality

    [Fact]
    public void RecordsWithTheSameDiceAreEqualWithTheSameHash()
    {
        var a = SuccessRoll.Roll(new FixedDice(3, 4)).Dice;
        var b = SuccessRoll.Roll(new FixedDice(3, 4)).Dice;

        Assert.Equal(a, b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RecordsWithDifferentDiceAreNotEqual()
    {
        var a = new RollRecord([3, 4], [4, 3]);

        Assert.NotEqual(a, new RollRecord([4, 3], [4, 3]));   // same dice, different roll order
        Assert.NotEqual(a, new RollRecord([3, 5], [5, 3]));
        Assert.NotEqual(a, new RollRecord([3, 4, 1], [4, 3]));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void SuccessResultsFromTheSameDiceAreEqual()
    {
        var a = SuccessRoll.Roll(new FixedDice(6, 1, 6), bonusDice: 1, risky: true);
        var b = SuccessRoll.Roll(new FixedDice(6, 1, 6), bonusDice: 1, risky: true);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, SuccessRoll.Roll(new FixedDice(6, 1, 6), bonusDice: 1, risky: false));
    }

    [Fact]
    public void InjuryResultsFromTheSameDiceAreEqual()
    {
        var a = InjuryRoll.Roll(new FixedDice(4, 4), modifiers: [1], targetAlreadyDown: true);
        var b = InjuryRoll.Roll(new FixedDice(4, 4), modifiers: [1], targetAlreadyDown: true);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, InjuryRoll.Roll(new FixedDice(4, 4), modifiers: [2], targetAlreadyDown: true));
    }

    // Validation

    [Theory]
    [InlineData(new[] { 0, 3 }, new[] { 3 })]
    [InlineData(new[] { 7, 3 }, new[] { 3 })]
    [InlineData(new[] { 3, 3 }, new[] { 9 })]
    public void DiceOutsideOneToSixAreRejected(int[] rolled, int[] kept)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollRecord(rolled, kept));
    }

    [Theory]
    [InlineData(new[] { 1, 1 }, new[] { 6, 6 })]        // kept dice that were never rolled
    [InlineData(new[] { 1, 1 }, new[] { 1, 1, 1 })]     // more kept than rolled
    [InlineData(new[] { 4, 2 }, new[] { 4, 4 })]        // the same die kept twice
    public void KeptDiceMustComeFromTheRolledDice(int[] rolled, int[] kept)
    {
        Assert.Throws<ArgumentException>(() => new RollRecord(rolled, kept));
    }

    [Fact]
    public void NullDiceListsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new RollRecord(null!, [1]));
        Assert.Throws<ArgumentNullException>(() => new RollRecord([1], null!));
    }

    [Theory]
    [InlineData(new[] { 1, 6, 6 }, new[] { 1, 6 }, 1)]  // bonus die: must keep the highest two
    [InlineData(new[] { 1, 6, 6 }, new[] { 6, 6 }, -1)] // penalty die: must keep the lowest two
    [InlineData(new[] { 1, 6 }, new[] { 6 }, 0)]        // wrong number kept
    [InlineData(new[] { 1, 6, 6 }, new[] { 6, 6 }, 0)]  // wrong number rolled for the net dice
    public void SuccessResultRejectsDiceThatDontMatchTheNetDice(int[] rolled, int[] kept, int net)
    {
        Assert.Throws<ArgumentException>(() => new SuccessRollResult(new RollRecord(rolled, kept), net, risky: false));
    }

    [Fact]
    public void SuccessResultAcceptsKeptDiceInAnyOrder()
    {
        var result = new SuccessRollResult(new RollRecord([1, 6, 5], [5, 6]), netDice: 1, risky: false);

        Assert.Equal(SuccessOutcome.Success, result.Outcome);
    }

    [Fact]
    public void SuccessOutcomeAlwaysFollowsTheDice()
    {
        var failure = new SuccessRollResult(new RollRecord([1, 1], [1, 1]), netDice: 0, risky: false);
        var critical = new SuccessRollResult(new RollRecord([6, 6], [6, 6]), netDice: 0, risky: false);

        Assert.Equal(SuccessOutcome.Failure, failure.Outcome);
        Assert.Equal(SuccessOutcome.CriticalSuccess, critical.Outcome);
        Assert.Equal(failure, failure with { });
    }

    [Fact]
    public void InjuryResultRejectsAModifierBelowTheFloor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InjuryRollResult(new RollRecord([4, 4], [4, 4]), netDice: 0, modifier: -4, targetAlreadyDown: false));
    }

    [Fact]
    public void InjuryResultRejectsDiceThatDontMatchTheNetDice()
    {
        Assert.Throws<ArgumentException>(() =>
            new InjuryRollResult(new RollRecord([1, 6, 6], [6, 6]), netDice: -1, modifier: 0, targetAlreadyDown: false));
    }

    [Fact]
    public void InjuryTotalOutcomeAndMarkersFollowTheDiceAndModifier()
    {
        var result = new InjuryRollResult(new RollRecord([3, 4], [4, 3]), netDice: 0, modifier: -3, targetAlreadyDown: true);

        Assert.Equal(4, result.Total);
        Assert.Equal(InjuryOutcome.MinorHit, result.Outcome);
        Assert.Equal(1, result.BloodMarkers);
    }
}
