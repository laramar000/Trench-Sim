namespace Rules.Core.Geometry;

// Exact circle, segment and rectangle tests on the thousandth-inch lattice.
internal static class Shapes
{
    // Squared distance from a point to the nearest point of a rectangle (0 inside or on it).
    public static long DistanceSquaredToRect(Point2 p, Rect rect)
    {
        var x = p.X.Thousandths;
        var y = p.Y.Thousandths;
        var dx = x < rect.Min.X.Thousandths ? rect.Min.X.Thousandths - x : x > rect.Max.X.Thousandths ? x - rect.Max.X.Thousandths : 0;
        var dy = y < rect.Min.Y.Thousandths ? rect.Min.Y.Thousandths - y : y > rect.Max.Y.Thousandths ? y - rect.Max.Y.Thousandths : 0;
        return dx * dx + dy * dy;
    }

    // The circle's inside overlaps the rectangle's inside (touching does not count).
    public static bool CircleOverlapsRect(Point2 centre, Length radius, Rect rect) =>
        (Int128)DistanceSquaredToRect(centre, rect) < (Int128)radius.Thousandths * radius.Thousandths;

    // The circle touches or overlaps the rectangle.
    public static bool CircleTouchesRect(Point2 centre, Length radius, Rect rect) =>
        (Int128)DistanceSquaredToRect(centre, rect) <= (Int128)radius.Thousandths * radius.Thousandths;

    public static bool CircleInsideRect(Point2 centre, Length radius, Rect rect) =>
        centre.X - radius >= rect.Min.X && centre.X + radius <= rect.Max.X
        && centre.Y - radius >= rect.Min.Y && centre.Y + radius <= rect.Max.Y;

    // Is the point within radius of the segment a-b (less than radius if strict, otherwise at most)?
    public static bool PointNearSegment(Point2 p, Point2 a, Point2 b, Length radius, bool strict)
    {
        Int128 r2 = (Int128)radius.Thousandths * radius.Thousandths;
        Int128 dx = b.X.Thousandths - a.X.Thousandths;
        Int128 dy = b.Y.Thousandths - a.Y.Thousandths;
        Int128 wx = p.X.Thousandths - a.X.Thousandths;
        Int128 wy = p.Y.Thousandths - a.Y.Thousandths;
        var dd = dx * dx + dy * dy;
        var along = wx * dx + wy * dy;
        bool Test(Int128 lhs, Int128 rhs) => strict ? lhs < rhs : lhs <= rhs;
        if (dd == 0 || along <= 0)
        {
            return Test(wx * wx + wy * wy, r2);
        }
        if (along >= dd)
        {
            return Test(ExactMath.DistanceSquared(p, b), r2);
        }
        // Perpendicular distance squared is (|w|^2 * dd - along^2) / dd; compare without dividing.
        return Test((wx * wx + wy * wy) * dd - along * along, r2 * dd);
    }

    // Does the closed segment a-b touch the closed rectangle?
    public static bool SegmentTouchesRect(Point2 a, Point2 b, Rect rect)
    {
        var lo = ExactMath.Ratio.Of(0, 1);
        var hi = ExactMath.Ratio.Of(1, 1);
        return ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, rect.Min.X.Thousandths, rect.Max.X.Thousandths, false, ref lo, ref hi)
            && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, rect.Min.Y.Thousandths, rect.Max.Y.Thousandths, false, ref lo, ref hi)
            && lo.CompareTo(hi) <= 0;
    }

    // Does a disc of this radius, swept from a to b, overlap the rectangle's inside? Touching does not count.
    // The nearest approach of two convex shapes is at a vertex of one of them, so only the segment's
    // end points and the rectangle's corners need testing once the segment is known not to cross it.
    public static bool SweptCircleOverlapsRect(Point2 a, Point2 b, Length radius, Rect rect)
    {
        if (CircleOverlapsRect(a, radius, rect) || CircleOverlapsRect(b, radius, rect))
        {
            return true;
        }
        if (SegmentTouchesRect(a, b, rect))
        {
            return true;
        }
        return rect.Corners().Any(corner => PointNearSegment(corner, a, b, radius, strict: true));
    }
}
