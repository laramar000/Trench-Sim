using Rules.Core.Dice;

namespace Rules.Tests;

// Repeatable dice for the random-play tests, and a counter that shows whether a command rolled anything.
public sealed class SeededDice(int seed) : IDiceSource
{
    private readonly Random _random = new(seed);

    public int Rolls { get; private set; }

    public int RollD6()
    {
        Rolls++;
        return _random.Next(1, 7);
    }
}
