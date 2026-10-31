namespace Sim;

// Trench-maps stage S8: the map generator's own random numbers. SplitMix64 with its published constants, integer
// maths only, so a seed gives the same numbers, and the same map, on every machine and in every build. Not
// System.Random, whose sequence is not promised to stay the same between .NET versions.
public sealed class MapRandom(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    // A whole number from 0 to n - 1, every value equally likely (values past the last whole multiple of n are drawn again).
    public int Below(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        var limit = ulong.MaxValue - (ulong.MaxValue % (ulong)n);
        ulong v;
        do
        {
            v = Next();
        }
        while (v >= limit);
        return (int)(v % (ulong)n);
    }

    // A whole number from low to high, both included.
    public int Between(int low, int high)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(high, low);
        return low + Below(high - low + 1);
    }

    // True one time in n.
    public bool OneIn(int n) => Below(n) == 0;
}
