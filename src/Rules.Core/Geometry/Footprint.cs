using System.Collections.Immutable;

namespace Rules.Core.Geometry;

// The ground outline of a terrain piece, deployment zone or the table: one axis-aligned rectangle, or (trench-maps
// stage S2) several joined rectangles ("parts") that do not overlap and are linked through shared edges, such as the
// L, T and plus shapes of trench bends, junctions and crossroads (p. 62). Movement, placement, elevation, sight and
// cover ask this type questions. Every answer for a one-part footprint is computed exactly as it was for a rectangle.
// The shape is the union of the closed parts; a seam where two parts meet is inside it.
public sealed record Footprint
{
    // Enough for a crossroads with arms of two parts each; keeps every question cheap.
    public const int MaxParts = 16;

    // The open cells of the bounding box that no part covers (multi-part only), from the grid of the parts' edges.
    private readonly ImmutableArray<Rect> _uncovered;

    private Footprint(ImmutableArray<Rect> parts)
    {
        Parts = parts;
        if (parts.Length == 1)
        {
            Bounds = parts[0];
            _uncovered = [];
            return;
        }
        Bounds = new Rect(
            new Point2(parts.Min(p => p.Min.X), parts.Min(p => p.Min.Y)),
            new Point2(parts.Max(p => p.Max.X), parts.Max(p => p.Max.Y)));
        _uncovered = Uncovered(parts);
    }

    public static Footprint Of(Rect rectangle) => new([rectangle]);

    // Joined rectangles. Throws ArgumentException unless there are 1 to MaxParts parts, no two overlap (touching is
    // fine) and every part is linked to the others through edges they share along a positive length. The parts are
    // kept in a fixed order (by corners), so the same shape always compares, hashes and travels the same way.
    public static Footprint Join(IEnumerable<Rect> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var list = parts.OrderBy(p => p.Min.X).ThenBy(p => p.Min.Y).ThenBy(p => p.Max.X).ThenBy(p => p.Max.Y).ToImmutableArray();
        if (list.Length == 0 || list.Length > MaxParts)
        {
            throw new ArgumentException($"A footprint needs 1 to {MaxParts} rectangles.", nameof(parts));
        }
        for (var i = 0; i < list.Length; i++)
        {
            for (var j = i + 1; j < list.Length; j++)
            {
                if (Overlap(list[i], list[j]))
                {
                    throw new ArgumentException($"The parts {list[i]} and {list[j]} of a footprint overlap.", nameof(parts));
                }
            }
        }
        var linked = new bool[list.Length];
        var queue = new Queue<int>([0]);
        linked[0] = true;
        while (queue.TryDequeue(out var i))
        {
            for (var j = 0; j < list.Length; j++)
            {
                if (!linked[j] && ShareEdge(list[i], list[j]))
                {
                    linked[j] = true;
                    queue.Enqueue(j);
                }
            }
        }
        if (linked.Any(l => !l))
        {
            throw new ArgumentException("The parts of a footprint must be joined along shared edges.", nameof(parts));
        }
        return new(list);
    }

    // The rectangles the footprint is made of, in a fixed order. One for a plain rectangle.
    public ImmutableArray<Rect> Parts { get; }

    public bool IsSingle => Parts.Length == 1;

    // The smallest rectangle holding the footprint (the footprint itself, for a rectangle).
    public Rect Bounds { get; }

    public bool Equals(Footprint? other) => other is not null && Parts.SequenceEqual(other.Parts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in Parts)
        {
            hash.Add(part);
        }
        return hash.ToHashCode();
    }

    public override string ToString() => IsSingle ? Bounds.ToString() : string.Join(" + ", Parts);

    // Is more than half of the round base inside the footprint? (Placement p. 31, "in terrain" p. 38.)
    internal AreaDecision MoreThanHalfInside(Point2 centre, Length radius) =>
        IsSingle ? BaseArea.MoreThanHalfInside(centre, radius, Bounds) : BaseArea.MoreThanHalfInside(centre, radius, Parts);

    // The base's inside overlaps the footprint's inside (touching does not count).
    internal bool CircleOverlaps(Point2 centre, Length radius)
    {
        foreach (var part in Parts)
        {
            if (Shapes.CircleOverlapsRect(centre, radius, part))
            {
                return true;
            }
        }
        return false;
    }

    // The base touches or overlaps the footprint.
    internal bool CircleTouches(Point2 centre, Length radius)
    {
        foreach (var part in Parts)
        {
            if (Shapes.CircleTouchesRect(centre, radius, part))
            {
                return true;
            }
        }
        return false;
    }

    // The nearest point of the base is at most `distance` from the footprint (touching or overlapping counts).
    // Used for "within 1 inch of a terrain piece" (Scenario I objectives, p. 152).
    internal bool CircleWithin(Point2 centre, Length radius, Length distance) =>
        Parts.Any(p => (Int128)Shapes.DistanceSquaredToRect(centre, p) <= (Int128)(radius + distance).Thousandths * (radius + distance).Thousandths);

    // The whole base lies within the footprint (touching its edge is allowed). For joined parts: within the bounding
    // box, and its inside meets no cell of the box that no part covers.
    internal bool CircleInside(Point2 centre, Length radius)
    {
        if (!Shapes.CircleInsideRect(centre, radius, Bounds))
        {
            return false;
        }
        foreach (var cell in _uncovered)
        {
            if (Shapes.CircleOverlapsRect(centre, radius, cell))
            {
                return false;
            }
        }
        return true;
    }

    // A base swept along the straight path a-b overlaps the footprint's inside.
    internal bool SweptCircleOverlaps(Point2 a, Point2 b, Length radius)
    {
        foreach (var part in Parts)
        {
            if (Shapes.SweptCircleOverlapsRect(a, b, radius, part))
            {
                return true;
            }
        }
        return false;
    }

    // The segment a-b passes through the footprint's inside. Touching an edge or a corner does not count; running
    // along a seam between two parts does (the seam is inside the shape).
    internal bool SegmentCrossesInside(Point2 a, Point2 b)
    {
        if (IsSingle)
        {
            var lo = ExactMath.Ratio.Of(0, 1);
            var hi = ExactMath.Ratio.Of(1, 1);
            return ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, Bounds.Min.X.Thousandths, Bounds.Max.X.Thousandths, true, ref lo, ref hi)
                && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, Bounds.Min.Y.Thousandths, Bounds.Max.Y.Thousandths, true, ref lo, ref hi)
                && lo.CompareTo(hi) < 0;
        }
        return CrossesInside(a, b, ExactMath.Ratio.Of(0, 1), ExactMath.Ratio.Of(1, 1));
    }

    // The 3D segment from (a, aHeight) to (b, bHeight) passes through the inside of the box standing on the footprint
    // from height bottom to top. Touching a face, an edge or a corner does not count (U13). Exact.
    internal bool SegmentCrossesBoxInside(Point2 a, Length aHeight, Point2 b, Length bHeight, Length bottom, Length top)
    {
        var lo = ExactMath.Ratio.Of(0, 1);
        var hi = ExactMath.Ratio.Of(1, 1);
        if (IsSingle)
        {
            return ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, Bounds.Min.X.Thousandths, Bounds.Max.X.Thousandths, true, ref lo, ref hi)
                && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, Bounds.Min.Y.Thousandths, Bounds.Max.Y.Thousandths, true, ref lo, ref hi)
                && ExactMath.Slab(aHeight.Thousandths, bHeight.Thousandths - aHeight.Thousandths, bottom.Thousandths, top.Thousandths, true, ref lo, ref hi)
                && lo.CompareTo(hi) < 0;
        }
        // The heights of the segment inside the box's open height range, then the ground shape over that stretch.
        return ExactMath.Slab(aHeight.Thousandths, bHeight.Thousandths - aHeight.Thousandths, bottom.Thousandths, top.Thousandths, true, ref lo, ref hi)
            && lo.CompareTo(hi) < 0
            && CrossesInside(a, b, lo, hi);
    }

    // The parts of the segment a-b inside the (closed) parts, as fractions lo..hi of its length, one per part it
    // passes through along a positive length. For one part this is the old single crossing. Callers merge them.
    internal IReadOnlyList<(ExactMath.Ratio Lo, ExactMath.Ratio Hi)> Crossings(Point2 a, Point2 b)
    {
        var found = new List<(ExactMath.Ratio, ExactMath.Ratio)>();
        foreach (var part in Parts)
        {
            if (Crossing(a, b, part, out var lo, out var hi))
            {
                found.Add((lo, hi));
            }
        }
        return found;
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
        foreach (var corner in Parts.SelectMany(p => p.Corners()))
        {
            var across = -(Int128)corner.X.Thousandths * dy + (Int128)corner.Y.Thousandths * dx;
            lowest = Int128.Min(lowest, across);
            highest = Int128.Max(highest, across);
        }
        var extent = highest - lowest;
        var needed = (Int128)width.Thousandths;
        return extent * extent >= needed * needed * lineSquared;
    }

    // Is the point strictly inside the footprint (not on its edge)? A point on a seam between parts is inside.
    internal bool ContainsInside(Point2 p) =>
        IsSingle
            ? p.X > Bounds.Min.X && p.X < Bounds.Max.X && p.Y > Bounds.Min.Y && p.Y < Bounds.Max.Y
            : InsideAt(p.X.Thousandths, p.Y.Thousandths, 1);

    internal bool OverlapsInside(Footprint other) => Parts.Any(p => other.Parts.Any(q => Overlap(p, q)));

    internal bool IsWithin(Footprint outer) =>
        Parts.All(p => outer.Bounds.Contains(p.Min) && outer.Bounds.Contains(p.Max) && !outer._uncovered.Any(cell => Overlap(cell, p)));

    private static bool Overlap(Rect a, Rect b) =>
        a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;

    // Two parts share an edge along a positive length.
    private static bool ShareEdge(Rect a, Rect b) =>
        (a.Max.X == b.Min.X || b.Max.X == a.Min.X) && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y
        || (a.Max.Y == b.Min.Y || b.Max.Y == a.Min.Y) && a.Min.X < b.Max.X && b.Min.X < a.Max.X;

    private static ImmutableArray<Rect> Uncovered(ImmutableArray<Rect> parts)
    {
        var xs = parts.SelectMany(p => new[] { p.Min.X, p.Max.X }).Distinct().Order().ToList();
        var ys = parts.SelectMany(p => new[] { p.Min.Y, p.Max.Y }).Distinct().Order().ToList();
        var cells = ImmutableArray.CreateBuilder<Rect>();
        for (var i = 0; i + 1 < xs.Count; i++)
        {
            for (var j = 0; j + 1 < ys.Count; j++)
            {
                var cell = new Rect(new Point2(xs[i], ys[j]), new Point2(xs[i + 1], ys[j + 1]));
                if (!parts.Any(p => p.Contains(cell.Min) && p.Contains(cell.Max)))
                {
                    cells.Add(cell);
                }
            }
        }
        return cells.ToImmutable();
    }

    private static bool Crossing(Point2 a, Point2 b, Rect part, out ExactMath.Ratio lo, out ExactMath.Ratio hi)
    {
        lo = ExactMath.Ratio.Of(0, 1);
        hi = ExactMath.Ratio.Of(1, 1);
        return ExactMath.Slab(a.X.Thousandths, b.X.Thousandths - a.X.Thousandths, part.Min.X.Thousandths, part.Max.X.Thousandths, false, ref lo, ref hi)
            && ExactMath.Slab(a.Y.Thousandths, b.Y.Thousandths - a.Y.Thousandths, part.Min.Y.Thousandths, part.Max.Y.Thousandths, false, ref lo, ref hi)
            && lo.CompareTo(hi) < 0;
    }

    // Does the stretch from fraction `from` to `to` of the segment a-b pass through the inside of the joined shape?
    // Between the points where the segment enters or leaves a part the set of parts it lies in does not change, and
    // neither does whether it is inside the shape, so one point in the middle of each stretch decides it.
    private bool CrossesInside(Point2 a, Point2 b, ExactMath.Ratio from, ExactMath.Ratio to)
    {
        var cuts = new List<ExactMath.Ratio> { from, to };
        foreach (var (lo, hi) in Crossings(a, b))
        {
            if (lo.CompareTo(from) > 0 && lo.CompareTo(to) < 0)
            {
                cuts.Add(lo);
            }
            if (hi.CompareTo(from) > 0 && hi.CompareTo(to) < 0)
            {
                cuts.Add(hi);
            }
        }
        cuts.Sort((x, y) => x.CompareTo(y));
        Int128 dx = b.X.Thousandths - a.X.Thousandths;
        Int128 dy = b.Y.Thousandths - a.Y.Thousandths;
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var (s, t) = (cuts[i], cuts[i + 1]);
            if (s.CompareTo(t) >= 0)
            {
                continue;
            }
            // The middle fraction m = num / den, and the point a + m (b - a) as (x / den, y / den).
            var num = s.Num * t.Den + t.Num * s.Den;
            var den = 2 * s.Den * t.Den;
            if (InsideAt(a.X.Thousandths * den + num * dx, a.Y.Thousandths * den + num * dy, den))
            {
                return true;
            }
        }
        return false;
    }

    // Is the point (x / den, y / den), in thousandths with den > 0, inside the joined shape? It is when each of the
    // four quarters around it is covered by some part, which is true of every point strictly inside a part and of a
    // point on a seam with parts on both sides.
    private bool InsideAt(Int128 x, Int128 y, Int128 den)
    {
        bool Covered(int sx, int sy) => Parts.Any(p =>
            (sx > 0 ? p.Min.X.Thousandths * den <= x && x < p.Max.X.Thousandths * den : p.Min.X.Thousandths * den < x && x <= p.Max.X.Thousandths * den)
            && (sy > 0 ? p.Min.Y.Thousandths * den <= y && y < p.Max.Y.Thousandths * den : p.Min.Y.Thousandths * den < y && y <= p.Max.Y.Thousandths * den));
        return Covered(1, 1) && Covered(-1, 1) && Covered(1, -1) && Covered(-1, -1);
    }
}
