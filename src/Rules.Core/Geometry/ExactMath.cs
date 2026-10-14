namespace Rules.Core.Geometry;

// Integer-only helpers. Every rule decision in this folder is made with these, never with floating point.
internal static class ExactMath
{
    public static Int128 FloorSqrt(Int128 n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }
        if (n < 2)
        {
            return n;
        }
        // Start at a power of two that is at least the root, then Newton's method down to the floor.
        var x = Int128.One << (int)((128 - Int128.LeadingZeroCount(n) + 1) / 2);
        while (true)
        {
            var y = (x + n / x) / 2;
            if (y >= x)
            {
                return x;
            }
            x = y;
        }
    }

    public static Int128 CeilSqrt(Int128 n)
    {
        var f = FloorSqrt(n);
        return f * f == n ? f : f + 1;
    }

    public static Int128 CeilDiv(Int128 a, Int128 b) => (a + b - 1) / b;

    public static long DistanceSquared(Point2 a, Point2 b)
    {
        var dx = a.X.Thousandths - b.X.Thousandths;
        var dy = a.Y.Thousandths - b.Y.Thousandths;
        return dx * dx + dy * dy;
    }

    // True if the distance between the points is at most (or, if strict, less than) the radius.
    public static bool Near(Point2 a, Point2 b, Length radius, bool strict)
    {
        var r = (Int128)radius.Thousandths;
        var d = (Int128)DistanceSquared(a, b);
        return strict ? d < r * r : d <= r * r;
    }

    // A fraction with a positive denominator; compared by cross-multiplication, so exact.
    public readonly record struct Ratio(Int128 Num, Int128 Den) : IComparable<Ratio>
    {
        public static Ratio Of(long num, long den) =>
            den > 0 ? new(num, den) : new(-(Int128)num, -(Int128)den);

        public int CompareTo(Ratio other) => (Num * other.Den).CompareTo(other.Num * Den);
    }

    // Narrows the parameter range [lo, hi] of the line a + t*d to the part inside [min, max] on one axis.
    // With open = true only the inside of the interval counts (touching a face does not). Returns false
    // when no part of the line can be inside.
    public static bool Slab(long a, long d, long min, long max, bool open, ref Ratio lo, ref Ratio hi)
    {
        if (d == 0)
        {
            return open ? a > min && a < max : a >= min && a <= max;
        }
        var t1 = Ratio.Of(min - a, d);
        var t2 = Ratio.Of(max - a, d);
        if (t1.CompareTo(t2) > 0)
        {
            (t1, t2) = (t2, t1);
        }
        if (t1.CompareTo(lo) > 0)
        {
            lo = t1;
        }
        if (t2.CompareTo(hi) < 0)
        {
            hi = t2;
        }
        return true;
    }
}
