using Rules.Core.Dice;

namespace Sim;

// Repeatable dice for the simulator, plus a count so a rejected command can be shown to have rolled nothing.
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
