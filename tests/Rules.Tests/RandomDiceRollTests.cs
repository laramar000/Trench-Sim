using Rules.Core.Dice;

namespace Rules.Tests;

public class RandomDiceRollTests
{
    [Fact]
    public void RollsOnlyOneToSixAndEveryFaceAppears()
    {
        var dice = new RandomDice();

        var rolls = Enumerable.Range(0, 10_000).Select(_ => dice.RollD6()).ToList();

        Assert.All(rolls, r => Assert.InRange(r, 1, 6));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, rolls.Distinct().Order());
    }
}
