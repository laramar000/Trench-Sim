namespace Client.Core;

// Graphics stages G2 and G4: an even-looking spread of numbers for laying out decoration (where a crater lies, how a
// sandbag is shaded). It is a fixed hash of the layout and a counter, not chance: the same layout always gives the same
// numbers, and nothing in the rules ever uses it (the client never rolls dice; the server does).
public sealed class Spread
{
    private uint _state;

    public Spread(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = 2166136261u;
        foreach (var c in key)
        {
            hash = (hash ^ c) * 16777619u;
        }
        _state = hash == 0 ? 1u : hash;
    }

    // The next number in 0 (inclusive) to 1 (exclusive).
    public double Next()
    {
        // xorshift32
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return (_state >> 8) / (double)(1 << 24);
    }

    // The next whole number from min (inclusive) to max (exclusive).
    public int Next(int min, int max) => min + (int)(Next() * (max - min));
}
