using Rules.Core.Dice;

namespace Rules.Tests;

// Returns the given values in order, so a test knows every die before it is rolled.
// Fails the test if a roll asks for more dice than were given.
public sealed class FixedDice(params int[] values) : IDiceSource
{
    private readonly Queue<int> _values = new(values);

    public int Remaining => _values.Count;

    public int RollD6() =>
        _values.TryDequeue(out var value)
            ? value
            : throw new InvalidOperationException("FixedDice ran out of values.");
}
