using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// Roster stage R4b (U102): BLAST (X") (p. 53). The blast radius is measured from the centre of the target model's base
// to the closest point of the other model's base, including vertically: the horizontal gap from the centre to the base
// and the difference in standing heights (SightHeights.StandingHeight: a tower's top, a platform, a trench floor) make
// one straight line, which must be at most X". All maths is exact, in thousandths.
public static class Blast
{
    public static bool Reaches(Point2 centre, Length centreHeight, ModelBody other, Length otherHeight, Length radius)
    {
        ArgumentNullException.ThrowIfNull(other);
        var v = Math.Abs(otherHeight.Thousandths - centreHeight.Thousandths);
        var big = (Int128)radius.Thousandths;
        if (v > radius.Thousandths)
        {
            return false;
        }
        // The base is reached when the centre-to-centre distance d is at most r + s, with s the horizontal reach left
        // once the height is used up: s * s = R * R - v * v. Squared twice so no square root is taken.
        var s2 = big * big - (Int128)v * v;
        // Roster stage R8c (U132): an oval base is reached through its core: d is the distance from the centre to the nearest
        // point of the core, and the base reaches Radius beyond it. For a round base that is the centre distance.
        var d2 = Shapes.PointToCoreGapSquared(other, centre);
        var r = (Int128)other.Radius.Thousandths;
        var left = d2 - r * r - s2; // d * d - r * r - s * s <= 2 * r * s
        return left <= 0 || left * left <= 4 * r * r * s2;
    }

    // Roster stage R4c (U103): SCATTER (p. 57). The opponent moves a missed attack exactly (7 - the Success Roll) inches
    // from the target's base centre, in any direction. A point counts as that far when it is within ScatterTolerance of
    // it, so points on the thousandths grid can be. Whether any point is possible is decided on 360 directions, one
    // degree apart, from due east (+x) turning towards +y: their unit vectors are held in millionths, rounded once, so
    // every machine finds the same points.
    public static readonly Length ScatterTolerance = Length.FromThousandths(2);

    public const int ScatterDirections = 360;

    private static readonly ImmutableArray<(long X, long Y)> Units = Enumerable.Range(0, ScatterDirections).Select(d =>
    {
        var radians = d * Math.PI / 180;
        return ((long)Math.Round(Math.Cos(radians) * 1_000_000, MidpointRounding.AwayFromZero), (long)Math.Round(Math.Sin(radians) * 1_000_000, MidpointRounding.AwayFromZero));
    }).ToImmutableArray();

    // The point inches from centre in that whole-degree direction, rounded to the nearest thousandth (halves away from 0).
    public static Point2 ScatterPoint(Point2 centre, int inches, int degree)
    {
        var (ux, uy) = Units[((degree % ScatterDirections) + ScatterDirections) % ScatterDirections];
        static long Scaled(long unit, int inches) => (long)Math.Round(unit * (decimal)inches / 1000m, MidpointRounding.AwayFromZero);
        return new Point2(Length.FromThousandths(centre.X.Thousandths + Scaled(ux, inches)), Length.FromThousandths(centre.Y.Thousandths + Scaled(uy, inches)));
    }

    // Whether at is the scatter distance from centre, to within ScatterTolerance. Exact, in thousandths.
    public static bool IsScatterDistance(Point2 centre, Point2 at, int inches)
    {
        var dx = (Int128)(at.X.Thousandths - centre.X.Thousandths);
        var dy = (Int128)(at.Y.Thousandths - centre.Y.Thousandths);
        var d2 = dx * dx + dy * dy;
        var low = (Int128)(inches * 1000L - ScatterTolerance.Thousandths);
        var high = (Int128)(inches * 1000L + ScatterTolerance.Thousandths);
        return d2 >= low * low && d2 <= high * high;
    }
}
