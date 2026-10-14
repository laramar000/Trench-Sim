namespace Rules.Core.Dice;

// The full story of one roll: every die rolled, in order, and the dice that counted.
// Immutable, validated on construction, and equal to any other record with the same dice,
// so it can be logged, replayed and compared safely.
public sealed class RollRecord : IEquatable<RollRecord>
{
    private readonly int[] _rolled;
    private readonly int[] _kept;

    // Copies both lists. Every die must be 1 to 6, and each kept die must be one of the
    // rolled dice (no die kept twice).
    public RollRecord(IEnumerable<int> rolled, IEnumerable<int> kept)
    {
        ArgumentNullException.ThrowIfNull(rolled);
        ArgumentNullException.ThrowIfNull(kept);
        _rolled = rolled.ToArray();
        _kept = kept.ToArray();

        if (_rolled.Concat(_kept).Any(d => d is < 1 or > 6))
        {
            throw new ArgumentOutOfRangeException(nameof(rolled), "Every die must be 1 to 6.");
        }
        if (!IsSubMultiset(_kept, _rolled))
        {
            throw new ArgumentException("Every kept die must be one of the rolled dice.", nameof(kept));
        }

        Rolled = Array.AsReadOnly(_rolled);
        Kept = Array.AsReadOnly(_kept);
    }

    public IReadOnlyList<int> Rolled { get; }
    public IReadOnlyList<int> Kept { get; }
    public int Total => _kept.Sum();

    // Rolls the base dice plus one extra die per point of net bonus or penalty dice,
    // then keeps the highest (bonus) or lowest (penalty) `keep` dice.
    // Bonus and penalty dice cancel before this is called, so only the net counts.
    internal static RollRecord RollAndKeep(IDiceSource dice, int keep, int netDice)
    {
        var rolled = new int[keep + Math.Abs(netDice)];
        for (var i = 0; i < rolled.Length; i++)
        {
            var value = dice.RollD6();
            if (value is < 1 or > 6)
            {
                throw new InvalidOperationException($"Dice source returned {value}; a D6 must be 1 to 6.");
            }
            rolled[i] = value;
        }
        return new RollRecord(rolled, KeepFrom(rolled, keep, netDice));
    }

    // Throws unless this record is exactly what rolling keep + |net| dice and keeping the
    // highest (bonus) or lowest (penalty) `keep` would produce. Used by the result types.
    internal void EnsureKeptFor(int keep, int netDice, string paramName)
    {
        if (_rolled.Length != keep + Math.Abs(netDice) || _kept.Length != keep)
        {
            throw new ArgumentException(
                $"Expected {keep + Math.Abs(netDice)} dice rolled and {keep} kept for net dice {netDice}.", paramName);
        }
        var expected = KeepFrom(_rolled, keep, netDice).Order();
        if (!expected.SequenceEqual(_kept.Order()))
        {
            throw new ArgumentException(
                $"Kept dice must be the {(netDice >= 0 ? "highest" : "lowest")} {keep} rolled.", paramName);
        }
    }

    internal static int NetDice(int bonusDice, int penaltyDice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bonusDice);
        ArgumentOutOfRangeException.ThrowIfNegative(penaltyDice);
        return bonusDice - penaltyDice;
    }

    public bool Equals(RollRecord? other) =>
        other is not null && _rolled.SequenceEqual(other._rolled) && _kept.SequenceEqual(other._kept);

    public override bool Equals(object? obj) => Equals(obj as RollRecord);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_rolled.Length);
        foreach (var d in _rolled)
        {
            hash.Add(d);
        }
        foreach (var d in _kept)
        {
            hash.Add(d);
        }
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"rolled [{string.Join(", ", _rolled)}], kept [{string.Join(", ", _kept)}], total {Total}";

    private static int[] KeepFrom(int[] rolled, int keep, int netDice) =>
        (netDice >= 0 ? rolled.OrderByDescending(d => d) : rolled.OrderBy(d => d)).Take(keep).ToArray();

    private static bool IsSubMultiset(int[] part, int[] whole)
    {
        var counts = new int[7];
        foreach (var d in whole)
        {
            counts[d]++;
        }
        foreach (var d in part)
        {
            if (--counts[d] < 0)
            {
                return false;
            }
        }
        return true;
    }
}
