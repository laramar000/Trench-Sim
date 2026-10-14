using System.Globalization;

namespace Rules.Core.Geometry;

// A distance in thousandths of an inch, held as an integer so that every rule decision is exact and
// identical on every machine. The rulebook's own distances (1/2", 1", 3", 12") are all exact in this unit.
// Millimetre sizes (base sizes) are converted at 25.4 mm to the inch, rounded half up to the nearest
// thousandth: a software interpretation (U1), so 25 mm becomes 0.984".
public readonly record struct Length : IComparable<Length>
{
    // Intermediate arithmetic stays far inside long; coordinates are limited separately (Limits).
    private const long Bound = 1_000_000_000;

    private Length(long thousandths) => Thousandths = thousandths;

    public long Thousandths { get; }

    public static Length Zero => default;
    public static Length Inch => new(1000);
    public static Length HalfInch => new(500);

    public static Length FromThousandths(long thousandths) =>
        Math.Abs(thousandths) > Bound
            ? throw new ArgumentOutOfRangeException(nameof(thousandths), "A length is limited to 1,000,000 inches.")
            : new(thousandths);

    public static Length FromInches(long inches) => FromThousandths(checked(inches * 1000));

    // Converts a whole number of millimetres: round(mm * 1000 / 25.4), halves rounded up.
    public static Length FromMillimetres(int millimetres)
    {
        if (millimetres < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(millimetres), "A length cannot be negative here.");
        }
        return FromThousandths(((long)millimetres * 10_000 + 127) / 254);
    }

    // Reads decimal inches with at most three decimal places, such as "0.5", "12" or "-1.250".
    public static Length Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var s = text.Trim();
        var negative = s.StartsWith('-');
        if (negative)
        {
            s = s[1..];
        }
        var parts = s.Split('.');
        if (parts.Length > 2 || parts[0].Length == 0 && (parts.Length == 1 || parts[1].Length == 0)
            || parts.Any(p => p.Any(c => c is < '0' or > '9')) || parts.Length == 2 && parts[1].Length > 3)
        {
            throw new FormatException($"'{text}' is not a length in inches with at most three decimals.");
        }
        var whole = parts[0].Length == 0 ? 0 : long.Parse(parts[0], CultureInfo.InvariantCulture);
        var fraction = parts.Length == 2 && parts[1].Length > 0
            ? long.Parse(parts[1].PadRight(3, '0'), CultureInfo.InvariantCulture)
            : 0;
        var value = checked(whole * 1000 + fraction);
        return FromThousandths(negative ? -value : value);
    }

    public int CompareTo(Length other) => Thousandths.CompareTo(other.Thousandths);

    public static Length operator +(Length a, Length b) => FromThousandths(checked(a.Thousandths + b.Thousandths));
    public static Length operator -(Length a, Length b) => FromThousandths(checked(a.Thousandths - b.Thousandths));
    public static Length operator -(Length a) => FromThousandths(-a.Thousandths);
    public static Length operator *(Length a, long n) => FromThousandths(checked(a.Thousandths * n));
    public static bool operator <(Length a, Length b) => a.Thousandths < b.Thousandths;
    public static bool operator >(Length a, Length b) => a.Thousandths > b.Thousandths;
    public static bool operator <=(Length a, Length b) => a.Thousandths <= b.Thousandths;
    public static bool operator >=(Length a, Length b) => a.Thousandths >= b.Thousandths;

    public override string ToString()
    {
        var abs = Math.Abs(Thousandths);
        return $"{(Thousandths < 0 ? "-" : "")}{abs / 1000}.{abs % 1000:000}\"";
    }
}

// Coordinates, radii, heights and battlefield sizes are limited to 200 inches so that the squared
// distances and cross products below fit comfortably in long and Int128 (see ExactMath).
internal static class Limits
{
    public const long Coordinate = 200_000;

    public static Length Check(Length value, string what) =>
        Math.Abs(value.Thousandths) > Coordinate
            ? throw new ArgumentOutOfRangeException(what, "Geometry values are limited to 200 inches.")
            : value;
}
