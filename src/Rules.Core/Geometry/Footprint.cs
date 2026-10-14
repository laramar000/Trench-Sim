namespace Rules.Core.Geometry;

// The ground outline of a terrain piece, deployment zone or the table. M3 supports axis-aligned rectangles only.
// Movement, placement, elevation, line of sight and cover ask this type questions and never look at the rectangle
// themselves, so a polygon footprint can be added later by changing this type alone.
public sealed record Footprint
{
    private Footprint(Rect bounds)
    {
        Bounds = bounds;
    }

    public static Footprint Of(Rect rectangle) => new(rectangle);

    // The smallest rectangle holding the footprint (the footprint itself, for a rectangle).
    public Rect Bounds { get; }

    public override string ToString() => Bounds.ToString();

    // Is more than half of the round base inside the footprint? (Placement p. 31, "in terrain" p. 38.)
    internal AreaDecision MoreThanHalfInside(Point2 centre, Length radius) => BaseArea.MoreThanHalfInside(centre, radius, Bounds);

    // The base's inside overlaps the footprint's inside (touching does not count).
    internal bool CircleOverlaps(Point2 centre, Length radius) => Shapes.CircleOverlapsRect(centre, radius, Bounds);

    // The base touches or overlaps the footprint.
    internal bool CircleTouches(Point2 centre, Length radius) => Shapes.CircleTouchesRect(centre, radius, Bounds);

    // The whole base lies within the footprint (touching its edge is allowed).
    internal bool CircleInside(Point2 centre, Length radius) => Shapes.CircleInsideRect(centre, radius, Bounds);

    // A base swept along the straight path a-b overlaps the footprint's inside.
    internal bool SweptCircleOverlaps(Point2 a, Point2 b, Length radius) => Shapes.SweptCircleOverlapsRect(a, b, radius, Bounds);

    // The segment a-b passes through the footprint's inside. Touching an edge or a corner does not count.
    internal bool SegmentCrossesInside(Point2 a, Point2 b)
    {
        var lo = ExactMath.Ratio.Of(0, 1);
        var hi = ExactMath.Ratio.Of(1, 1);
        return ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, Bounds.Min.X.Thousandths, Bounds.Max.X.Thousandths, true, ref lo, ref hi)
            && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, Bounds.Min.Y.Thousandths, Bounds.Max.Y.Thousandths, true, ref lo, ref hi)
            && lo.CompareTo(hi) < 0;
    }

    // The part of the segment a-b inside the (closed) footprint, as fractions lo..hi of its length.
    internal bool TryCrossing(Point2 a, Point2 b, out ExactMath.Ratio lo, out ExactMath.Ratio hi)
    {
        lo = ExactMath.Ratio.Of(0, 1);
        hi = ExactMath.Ratio.Of(1, 1);
        return ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, Bounds.Min.X.Thousandths, Bounds.Max.X.Thousandths, false, ref lo, ref hi)
            && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, Bounds.Min.Y.Thousandths, Bounds.Max.Y.Thousandths, false, ref lo, ref hi)
            && lo.CompareTo(hi) < 0;
    }

    // Is the footprint's extent across the line from -> to at least this wide? (U4.) Exact, without dividing.
    internal bool IsAtLeastAsWideAcross(Point2 from, Point2 to, Length width)
    {
        Int128 dx = to.X.Thousandths - from.X.Thousandths;
        Int128 dy = to.Y.Thousandths - from.Y.Thousandths;
        var lineSquared = dx * dx + dy * dy;
        if (lineSquared == 0)
        {
            return true;
        }
        Int128 lowest = Int128.MaxValue, highest = Int128.MinValue;
        foreach (var corner in Bounds.Corners())
        {
            var across = -(Int128)corner.X.Thousandths * dy + (Int128)corner.Y.Thousandths * dx;
            lowest = Int128.Min(lowest, across);
            highest = Int128.Max(highest, across);
        }
        var extent = highest - lowest;
        var needed = (Int128)width.Thousandths;
        return extent * extent >= needed * needed * lineSquared;
    }

    // Is the point strictly inside the footprint (not on its edge)?
    internal bool ContainsInside(Point2 p) =>
        p.X > Bounds.Min.X && p.X < Bounds.Max.X && p.Y > Bounds.Min.Y && p.Y < Bounds.Max.Y;

    internal bool OverlapsInside(Footprint other) =>
        Bounds.Min.X < other.Bounds.Max.X && other.Bounds.Min.X < Bounds.Max.X
        && Bounds.Min.Y < other.Bounds.Max.Y && other.Bounds.Min.Y < Bounds.Max.Y;

    internal bool IsWithin(Footprint outer) => outer.Bounds.Contains(Bounds.Min) && outer.Bounds.Contains(Bounds.Max);
}
