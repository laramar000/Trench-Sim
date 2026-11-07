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

    // Roster stage R8a (U132): an oval base (a capsule along a table axis) against a rectangle. The capsule is every point
    // within Radius of its core segment, which is a flat box, so the gap to the rectangle is exact on each axis.
    public static Int128 CoreGapSquaredToRect(ModelBody body, Rect rect)
    {
        var (x0, x1, y0, y1) = body.CoreBox();
        var dx = (Int128)Math.Max(0, Math.Max(rect.Min.X.Thousandths - x1, x0 - rect.Max.X.Thousandths));
        var dy = (Int128)Math.Max(0, Math.Max(rect.Min.Y.Thousandths - y1, y0 - rect.Max.Y.Thousandths));
        return dx * dx + dy * dy;
    }

    public static bool BodyOverlapsRect(ModelBody body, Rect rect) =>
        CoreGapSquaredToRect(body, rect) < (Int128)body.Radius.Thousandths * body.Radius.Thousandths;

    public static bool BodyTouchesRect(ModelBody body, Rect rect) =>
        CoreGapSquaredToRect(body, rect) <= (Int128)body.Radius.Thousandths * body.Radius.Thousandths;

    // A capsule touches all four sides of its bounding box, so it is inside a rectangle when the box is.
    public static bool BodyInsideRect(ModelBody body, Rect rect) =>
        body.Centre.X - body.ExtentX >= rect.Min.X && body.Centre.X + body.ExtentX <= rect.Max.X
        && body.Centre.Y - body.ExtentY >= rect.Min.Y && body.Centre.Y + body.ExtentY <= rect.Max.Y;

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

    // ---- Roster stage R8b (U132): an oval mover. Its core segment lies along a table axis and is carried along each
    // straight leg without turning, so what its centre-line sweeps is a parallelogram (the core and the leg), and the base
    // is every point within Radius of it. Every test below is exact orientation and distance arithmetic. ----

    // The sign of the turn a -> b -> c: positive left, negative right, zero collinear.
    private static int Turn(Point2 a, Point2 b, Point2 c)
    {
        Int128 cross = (Int128)(b.X.Thousandths - a.X.Thousandths) * (c.Y.Thousandths - a.Y.Thousandths)
            - (Int128)(b.Y.Thousandths - a.Y.Thousandths) * (c.X.Thousandths - a.X.Thousandths);
        return Int128.Sign(cross);
    }

    private static bool OnSegment(Point2 a, Point2 b, Point2 p) =>
        Turn(a, b, p) == 0
        && p.X.Thousandths >= Math.Min(a.X.Thousandths, b.X.Thousandths) && p.X.Thousandths <= Math.Max(a.X.Thousandths, b.X.Thousandths)
        && p.Y.Thousandths >= Math.Min(a.Y.Thousandths, b.Y.Thousandths) && p.Y.Thousandths <= Math.Max(a.Y.Thousandths, b.Y.Thousandths);

    // Do the closed segments a0-a1 and b0-b1 share a point? (A segment may be a single point.)
    public static bool SegmentsMeet(Point2 a0, Point2 a1, Point2 b0, Point2 b1)
    {
        var d1 = Turn(a0, a1, b0);
        var d2 = Turn(a0, a1, b1);
        var d3 = Turn(b0, b1, a0);
        var d4 = Turn(b0, b1, a1);
        if (d1 * d2 < 0 && d3 * d4 < 0)
        {
            return true;
        }
        return OnSegment(a0, a1, b0) || OnSegment(a0, a1, b1) || OnSegment(b0, b1, a0) || OnSegment(b0, b1, a1);
    }

    // Is the distance between the closed segments less than the radius (strict) or at most it? Two segments that do not
    // meet are nearest at an end of one of them.
    public static bool SegmentsNear(Point2 a0, Point2 a1, Point2 b0, Point2 b1, Length radius, bool strict) =>
        SegmentsMeet(a0, a1, b0, b1) ? (!strict || radius > Length.Zero)
        : PointNearSegment(a0, b0, b1, radius, strict) || PointNearSegment(a1, b0, b1, radius, strict)
        || PointNearSegment(b0, a0, a1, radius, strict) || PointNearSegment(b1, a0, a1, radius, strict);

    // The core segment of a base set down with its centre at the given point.
    private static (Point2 P, Point2 Q) Core(ModelBody body, Point2 at)
    {
        var (x0, x1, y0, y1) = body.At(at).CoreBox();
        return (new Point2(Length.FromThousandths(x0), Length.FromThousandths(y0)), new Point2(Length.FromThousandths(x1), Length.FromThousandths(y1)));
    }

    // The corners of what the mover's core sweeps along the leg a-b: the core at a, then the core at b (in order round the
    // outline). Degenerate (fewer distinct points, no area) for a round mover or a leg along the core.
    private static Point2[] SweptCore(ModelBody mover, Point2 a, Point2 b)
    {
        var (p0, q0) = Core(mover, a);
        var (p1, q1) = Core(mover, b);
        return [p0, q0, q1, p1];
    }

    // Is the point inside the closed convex quadrilateral (corners in order, either way round)? False for no area.
    private static bool InsideConvex(Point2[] corners, Point2 p)
    {
        var sign = 0;
        for (var i = 0; i < corners.Length; i++)
        {
            var t = Turn(corners[i], corners[(i + 1) % corners.Length], p);
            if (t == 0)
            {
                continue;
            }
            if (sign == 0)
            {
                sign = t;
            }
            else if (sign != t)
            {
                return false;
            }
        }
        // All turns zero means the corners and the point are collinear: no area to be inside of.
        return sign != 0 && HasArea(corners);
    }

    private static bool HasArea(Point2[] corners) =>
        Turn(corners[0], corners[1], corners[2]) != 0 || Turn(corners[0], corners[2], corners[3]) != 0
        || Turn(corners[1], corners[2], corners[3]) != 0 || Turn(corners[0], corners[1], corners[3]) != 0;

    // Is the other base (a core segment, within Radius of it) within `reach` of every point the mover's base sweeps along
    // the leg a-b, nearest point first: that is, is the distance from the other's core to the swept core less than (strict) or
    // at most `reach` minus nothing; callers pass the sum of radii and any extra distance. Round against round is the old
    // point-to-segment test, unchanged.
    public static bool SweptNear(ModelBody mover, Point2 a, Point2 b, ModelBody other, Length reach, bool strict)
    {
        if (mover.IsRound && other.IsRound)
        {
            return PointNearSegment(other.Centre, a, b, reach, strict);
        }
        var swept = SweptCore(mover, a, b);
        var (e0, e1) = Core(other, other.Centre);
        if (InsideConvex(swept, e0) || InsideConvex(swept, e1))
        {
            return !strict || reach > Length.Zero;
        }
        for (var i = 0; i < swept.Length; i++)
        {
            if (SegmentsNear(swept[i], swept[(i + 1) % swept.Length], e0, e1, reach, strict))
            {
                return true;
            }
        }
        return false;
    }

    // Does the mover's base, carried along the leg a-b, overlap the rectangle's inside? Touching does not count. A round
    // mover takes the old test.
    public static bool SweptBodyOverlapsRect(ModelBody mover, Point2 a, Point2 b, Rect rect)
    {
        if (mover.IsRound)
        {
            return SweptCircleOverlapsRect(a, b, mover.Radius, rect);
        }
        var swept = SweptCore(mover, a, b);
        var r = (Int128)mover.Radius.Thousandths * mover.Radius.Thousandths;
        if (swept.Any(v => (Int128)DistanceSquaredToRect(v, rect) < r))
        {
            return true;
        }
        var corners = rect.Corners().ToArray();
        if (corners.Any(c => InsideConvex(swept, c)))
        {
            return true;
        }
        for (var i = 0; i < swept.Length; i++)
        {
            var (s, t) = (swept[i], swept[(i + 1) % swept.Length]);
            if (SegmentTouchesRect(s, t, rect) || corners.Any(c => PointNearSegment(c, s, t, mover.Radius, strict: true)))
            {
                return true;
            }
        }
        return false;
    }
}
