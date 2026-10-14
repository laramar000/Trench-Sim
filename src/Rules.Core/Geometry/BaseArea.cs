namespace Rules.Core.Geometry;

public enum AreaDecision
{
    MoreThanHalf,
    HalfOrLess,

    // The area is within the method's numeric resolution of exactly half. Callers must treat this as
    // "not more than half" (the conservative answer); it is reported separately so the limit is visible.
    TooCloseToCall,
}

// The rulebook lets part of a base hang over the edge of the battlefield only if MORE THAN HALF of the
// base is on the battlefield (Model Placement, p. 31). This decides that exactly where it can and by
// guaranteed-bound integer integration elsewhere, with no floating point.
//
//  * Circle wholly inside, or not overlapping at all: exact.
//  * Circle crossing exactly one edge line: exact. More than half is inside if and only if the centre is
//    strictly inside that edge.
//  * Circle crossing two or more edge lines (a corner, or a rectangle narrower than the circle): the
//    inside area is bracketed between a lower and an upper bound from slicing the circle into vertical
//    strips (SliceCount), each bound rounded in the safe direction, then compared with the bracket for
//    half the circle's area. Only if the brackets overlap is the answer TooCloseToCall.
internal static class BaseArea
{
    public const int SliceCount = 4000;

    // Pi bracketed by 15 decimal places.
    private const long PiLowE15 = 3_141_592_653_589_793;
    private const long PiHighE15 = 3_141_592_653_589_794;

    public static AreaDecision MoreThanHalfInside(Point2 centre, Length radius, Rect rect)
    {
        if (!Shapes.CircleOverlapsRect(centre, radius, rect))
        {
            return AreaDecision.HalfOrLess;
        }

        long cx = centre.X.Thousandths, cy = centre.Y.Thousandths, r = radius.Thousandths;
        long minX = rect.Min.X.Thousandths, maxX = rect.Max.X.Thousandths;
        long minY = rect.Min.Y.Thousandths, maxY = rect.Max.Y.Thousandths;
        var left = cx - r < minX;
        var right = cx + r > maxX;
        var bottom = cy - r < minY;
        var top = cy + r > maxY;
        var crossed = (left ? 1 : 0) + (right ? 1 : 0) + (bottom ? 1 : 0) + (top ? 1 : 0);

        if (crossed == 0)
        {
            return AreaDecision.MoreThanHalf;
        }
        if (crossed == 1)
        {
            var strictlyInside = left ? cx > minX : right ? cx < maxX : bottom ? cy > minY : cy < maxY;
            return strictlyInside ? AreaDecision.MoreThanHalf : AreaDecision.HalfOrLess;
        }
        return Numeric(cx, cy, r, minX, maxX, minY, maxY);
    }

    // Inside length of the vertical chord at horizontal offset dx from the centre, using a square-root
    // bound that is rounded up (high) or down (low). The chord's half-height is h; the rectangle clips it.
    private static long ChordLength(long cy, long h, long minY, long maxY) =>
        Math.Max(0, Math.Min(cy + h, maxY) - Math.Max(cy - h, minY));

    private static AreaDecision Numeric(long cx, long cy, long r, long minX, long maxX, long minY, long maxY)
    {
        var xlo = Math.Max(cx - r, minX);
        var xhi = Math.Min(cx + r, maxX);
        var width = xhi - xlo;

        long Low(long x) => ChordLengthAt(x, round: false);
        long High(long x) => ChordLengthAt(x, round: true);
        long ChordLengthAt(long x, bool round)
        {
            var dx = x - cx;
            var rest = (Int128)r * r - (Int128)dx * dx;
            if (rest < 0)
            {
                return 0;
            }
            var h = (long)(round ? ExactMath.CeilSqrt(rest) : ExactMath.FloorSqrt(rest));
            return ChordLength(cy, h, minY, maxY);
        }

        Int128 lower = 0, upper = 0;
        var previous = xlo;
        for (var i = 1; i <= SliceCount; i++)
        {
            var next = xlo + width * i / SliceCount;
            var w = next - previous;
            if (w > 0)
            {
                lower += (Int128)w * Math.Min(Low(previous), Low(next));
                // The chord length rises to the centre column and falls after it, so a strip holding the
                // centre column is bounded by the centre's own length and any other by its larger end.
                upper += (Int128)w * (previous <= cx && cx <= next
                    ? High(cx)
                    : Math.Max(High(previous), High(next)));
            }
            previous = next;
        }

        Int128 r2 = (Int128)r * r;
        // Half the circle is pi * r^2 / 2.
        if (lower * 2 * 1_000_000_000_000_000 > (Int128)PiHighE15 * r2)
        {
            return AreaDecision.MoreThanHalf;
        }
        if (upper * 2 * 1_000_000_000_000_000 <= (Int128)PiLowE15 * r2)
        {
            return AreaDecision.HalfOrLess;
        }
        return AreaDecision.TooCloseToCall;
    }
}
