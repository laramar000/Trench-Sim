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

    // Trench-maps stage S2: a footprint of joined rectangles that do not overlap. More than half is inside the whole
    // shape if it is inside one part; otherwise the parts' guaranteed brackets are added up (the parts do not overlap,
    // so the inside area is the sum) and compared with half the circle as for one rectangle.
    public static AreaDecision MoreThanHalfInside(Point2 centre, Length radius, IReadOnlyList<Rect> parts)
    {
        if (parts.Count == 1)
        {
            return MoreThanHalfInside(centre, radius, parts[0]);
        }
        if (parts.Any(part => MoreThanHalfInside(centre, radius, part) == AreaDecision.MoreThanHalf))
        {
            return AreaDecision.MoreThanHalf;
        }
        long cx = centre.X.Thousandths, cy = centre.Y.Thousandths, r = radius.Thousandths;
        Int128 lower = 0, upper = 0;
        foreach (var part in parts.Where(part => Shapes.CircleOverlapsRect(centre, radius, part)))
        {
            var (low, high) = Bracket(cx, cy, r, part.Min.X.Thousandths, part.Max.X.Thousandths, part.Min.Y.Thousandths, part.Max.Y.Thousandths);
            lower += low;
            upper += high;
        }
        return Decide(lower, upper, r);
    }

    // Roster stage R8a (U132): an oval base (a capsule along a table axis). Its area is pi r^2 + 4 r s (r the radius, s the
    // half span). The same cuts as for a disc: wholly inside or clear of the rectangle is exact, so is crossing one edge
    // line (the capsule is symmetric about its centre), and otherwise the area inside is bracketed by thin strips along
    // the long axis (a Y-axis oval is turned on its side first) and compared with half the base's area.
    public static AreaDecision MoreThanHalfInside(ModelBody body, Rect rect)
    {
        if (body.IsRound)
        {
            return MoreThanHalfInside(body.Centre, body.Radius, rect);
        }
        if (!Shapes.BodyOverlapsRect(body, rect))
        {
            return AreaDecision.HalfOrLess;
        }
        long cx = body.Centre.X.Thousandths, cy = body.Centre.Y.Thousandths;
        long minX = rect.Min.X.Thousandths, maxX = rect.Max.X.Thousandths, minY = rect.Min.Y.Thousandths, maxY = rect.Max.Y.Thousandths;
        long ex = body.ExtentX.Thousandths, ey = body.ExtentY.Thousandths;
        var left = cx - ex < minX;
        var right = cx + ex > maxX;
        var bottom = cy - ey < minY;
        var top = cy + ey > maxY;
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
        var (lower, upper) = CapsuleBracket(body, [rect]);
        return DecideCapsule(lower, upper, body);
    }

    public static AreaDecision MoreThanHalfInside(ModelBody body, IReadOnlyList<Rect> parts)
    {
        if (body.IsRound)
        {
            return MoreThanHalfInside(body.Centre, body.Radius, parts);
        }
        if (parts.Count == 1)
        {
            return MoreThanHalfInside(body, parts[0]);
        }
        if (parts.Any(part => MoreThanHalfInside(body, part) == AreaDecision.MoreThanHalf))
        {
            return AreaDecision.MoreThanHalf;
        }
        var (lower, upper) = CapsuleBracket(body, [.. parts.Where(part => Shapes.BodyOverlapsRect(body, part))]);
        return DecideCapsule(lower, upper, body);
    }

    // Bounds on the capsule's area inside the rectangles (which do not overlap), in square thousandths.
    private static (Int128 Lower, Int128 Upper) CapsuleBracket(ModelBody body, IReadOnlyList<Rect> rects)
    {
        // Slices run along x; a Y-axis oval and its rectangles are swapped x for y.
        var swap = body.Axis == BaseAxis.Y;
        long cx = swap ? body.Centre.Y.Thousandths : body.Centre.X.Thousandths;
        long cy = swap ? body.Centre.X.Thousandths : body.Centre.Y.Thousandths;
        long r = body.Radius.Thousandths, s = body.HalfSpan.Thousandths;
        Int128 lower = 0, upper = 0;
        foreach (var rect in rects)
        {
            long minX = swap ? rect.Min.Y.Thousandths : rect.Min.X.Thousandths, maxX = swap ? rect.Max.Y.Thousandths : rect.Max.X.Thousandths;
            long minY = swap ? rect.Min.X.Thousandths : rect.Min.Y.Thousandths, maxY = swap ? rect.Max.X.Thousandths : rect.Max.Y.Thousandths;
            var xlo = Math.Max(cx - r - s, minX);
            var xhi = Math.Min(cx + r + s, maxX);
            var width = xhi - xlo;
            long ChordAt(long x, bool round)
            {
                // Beyond the flat middle (|x - cx| <= s) the chord is a circle's.
                var dx = Math.Max(0, Math.Abs(x - cx) - s);
                var rest = (Int128)r * r - (Int128)dx * dx;
                if (rest < 0)
                {
                    return 0;
                }
                var h = (long)(round ? ExactMath.CeilSqrt(rest) : ExactMath.FloorSqrt(rest));
                return ChordLength(cy, h, minY, maxY);
            }

            var previous = xlo;
            for (var i = 1; i <= SliceCount; i++)
            {
                var next = xlo + width * i / SliceCount;
                var w = next - previous;
                if (w > 0)
                {
                    lower += (Int128)w * Math.Min(ChordAt(previous, false), ChordAt(next, false));
                    // The chord rises to the flat middle, stays level across it, and falls after it.
                    upper += (Int128)w * (previous <= cx + s && cx - s <= next ? ChordAt(cx, true) : Math.Max(ChordAt(previous, true), ChordAt(next, true)));
                }
                previous = next;
            }
        }
        return (lower, upper);
    }

    // Half the capsule's area is (pi r^2 + 4 r s) / 2; pi is bracketed so the answer is safe in both directions.
    private static AreaDecision DecideCapsule(Int128 lower, Int128 upper, ModelBody body)
    {
        Int128 r = body.Radius.Thousandths, s = body.HalfSpan.Thousandths;
        const long one = 1_000_000_000_000_000;
        if (lower * 2 * one > (Int128)PiHighE15 * r * r + 4 * r * s * one)
        {
            return AreaDecision.MoreThanHalf;
        }
        if (upper * 2 * one <= (Int128)PiLowE15 * r * r + 4 * r * s * one)
        {
            return AreaDecision.HalfOrLess;
        }
        return AreaDecision.TooCloseToCall;
    }

    private static AreaDecision Numeric(long cx, long cy, long r, long minX, long maxX, long minY, long maxY)
    {
        var (lower, upper) = Bracket(cx, cy, r, minX, maxX, minY, maxY);
        return Decide(lower, upper, r);
    }

    // Lower and upper bounds on the area of the circle inside the rectangle, in square thousandths.
    private static (Int128 Lower, Int128 Upper) Bracket(long cx, long cy, long r, long minX, long maxX, long minY, long maxY)
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

        return (lower, upper);
    }

    private static AreaDecision Decide(Int128 lower, Int128 upper, long r)
    {
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
