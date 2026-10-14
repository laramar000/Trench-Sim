namespace Rules.Core.Dice;

// Every die the rules roll comes from here. The server owns the real source;
// tests pass fixed dice so each result is known in advance.
public interface IDiceSource
{
    // Returns a value from 1 to 6.
    int RollD6();
}
