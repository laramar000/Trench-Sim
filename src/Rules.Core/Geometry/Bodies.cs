using Rules.Core.State;

namespace Rules.Core.Geometry;

// Roster stage R8a (U132): which table axis the long side of an oval base lies along. Only the two axes are allowed so every
// measurement stays exact integer maths (a diagonal axis would need irrational numbers).
public enum BaseAxis
{
    Y,
    X,
}

// A model as geometry: a round base, or (roster stage R8a, U132) an oval one, a capsule: a rectangle with half-discs on
// its two short ends. Limbs, weapons and model heights play no part (U2). The caller supplies the radius;
// Rules.Core invents no real model sizes. Elevation is how high the base stands: zero on the ground, a tower's height on
// its platform (towers proposal, stage T1); Board.BodyOf fills it in from the battlefield. Radius is half the base's width;
// HalfSpan is how far each end's centre lies from the base's centre along Axis (zero for a round base, and then Axis means
// nothing). A 30x60 mm oval is Radius 15 mm, HalfSpan 15 mm.
public sealed record ModelBody
{
    public ModelBody(string id, Point2 centre, Length radius, Length elevation = default, Length halfSpan = default, BaseAxis axis = BaseAxis.Y)
    {
        Id = IdRules.Check(id, nameof(id));
        Centre = centre;
        Radius = Limits.Check(radius, nameof(radius));
        if (radius <= Length.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "A base needs a positive radius.");
        }
        if (elevation < Length.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elevation), "A base cannot stand below the ground.");
        }
        Elevation = Limits.Check(elevation, nameof(elevation));
        if (halfSpan < Length.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(halfSpan), "A base cannot have a negative span.");
        }
        if (!Enum.IsDefined(axis))
        {
            throw new ArgumentOutOfRangeException(nameof(axis), "Unknown base axis.");
        }
        HalfSpan = Limits.Check(halfSpan, nameof(halfSpan));
        Axis = halfSpan == Length.Zero ? BaseAxis.Y : axis;
    }

    public string Id { get; }
    public Point2 Centre { get; }
    public Length Radius { get; }
    public Length Elevation { get; }
    public Length HalfSpan { get; }
    public BaseAxis Axis { get; }

    public bool IsRound => HalfSpan == Length.Zero;

    // How far the base reaches from its centre along each table axis: its radius, plus the span along its long axis.
    public Length ExtentX => Axis == BaseAxis.X ? Radius + HalfSpan : Radius;
    public Length ExtentY => Axis == BaseAxis.Y ? Radius + HalfSpan : Radius;

    // The two end centres (equal for a round base); the base is every point within Radius of the segment between them.
    internal (long MinX, long MaxX, long MinY, long MaxY) CoreBox()
    {
        var span = HalfSpan.Thousandths;
        var x = Centre.X.Thousandths;
        var y = Centre.Y.Thousandths;
        return Axis == BaseAxis.X ? (x - span, x + span, y, y) : (x, x, y - span, y + span);
    }

    // The same base moved on the table at the same elevation. A move that can change level uses Board.BodyAt instead.
    public ModelBody At(Point2 centre) => new(Id, centre, Radius, Elevation, HalfSpan, Axis);

    public ModelBody AtElevation(Length elevation) => new(Id, Centre, Radius, elevation, HalfSpan, Axis);

    // The same oval turned to lie along the other table axis (a round base is unchanged).
    public ModelBody WithAxis(BaseAxis axis) => new(Id, Centre, Radius, Elevation, HalfSpan, axis);

    public override string ToString() => (Elevation == Length.Zero
        ? $"{Id} at {Centre}, radius {Radius}"
        : $"{Id} at {Centre}, radius {Radius}, {Elevation} up") + (IsRound ? "" : $", span {HalfSpan} along {Axis}");
}

// Distance helpers between bases. Measurements run between the nearest points of the two bases (p. 30; range p. 43).
// A base is a flat disc at its elevation. Two bases at the same elevation are measured exactly as before (edge to edge
// on the table); at different elevations the nearest points are found in three dimensions (towers proposal, U70).
public static class Measure
{
    // Is the distance between the nearest points of the two bases at most the distance (rulebook "within")? Exact.
    public static bool IsWithin(ModelBody a, ModelBody b, Length distance)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var reach = a.Radius + b.Radius;
        var dz = (Int128)Math.Abs(a.Elevation.Thousandths - b.Elevation.Thousandths);
        if (dz == 0)
        {
            return CoreGapSquared(a, b) <= (Int128)(reach + distance).Thousandths * (reach + distance).Thousandths;
        }
        var d = (Int128)distance.Thousandths;
        if (dz > d)
        {
            return false;
        }
        // Horizontal gap h = max(0, c - R) with c the centres' distance and R the two radii; within when h^2 + dz^2 <= d^2,
        // that is c <= R + s with s^2 = d^2 - dz^2. Squared without roots: c^2 - R^2 - s^2 <= 2Rs.
        var c2 = CoreGapSquared(a, b);
        var r = (Int128)reach.Thousandths;
        var s2 = d * d - dz * dz;
        var left = c2 - r * r - s2;
        return left <= 0 || left * left <= 4 * r * r * s2;
    }

    // Do the bases overlap? Touching bases do not overlap, and bases at different elevations never do.
    public static bool Overlap(ModelBody a, ModelBody b) =>
        a.Elevation == b.Elevation && CoreGapSquared(a, b) < Square(a.Radius + b.Radius);

    // In contact: the bases touch or overlap (p. 30); bases at different elevations are not in contact.
    public static bool InContact(ModelBody a, ModelBody b) =>
        a.Elevation == b.Elevation && CoreGapSquared(a, b) <= Square(a.Radius + b.Radius);

    private static Int128 Square(Length length) => (Int128)length.Thousandths * length.Thousandths;

    // Roster stage R8a (U132): the squared distance between the two bases' core segments (the centres for round bases).
    // Both segments lie along a table axis, so this is the distance between two flat boxes: the gap on each axis, exact.
    // A base is every point within its Radius of its core, so the bases are within d of each other when this is at most
    // (Ra + Rb + d) squared.
    internal static Int128 CoreGapSquared(ModelBody a, ModelBody b)
    {
        var (ax0, ax1, ay0, ay1) = a.CoreBox();
        var (bx0, bx1, by0, by1) = b.CoreBox();
        var dx = (Int128)Math.Max(0, Math.Max(ax0 - bx1, bx0 - ax1));
        var dy = (Int128)Math.Max(0, Math.Max(ay0 - by1, by0 - ay1));
        return dx * dx + dy * dy;
    }

    // The distance between the nearest points rounded up to the next thousandth of an inch (overlapping bases report
    // zero). For display and reports only; rules use IsWithin and Overlap.
    public static Length Gap(ModelBody a, ModelBody b)
    {
        var centres = (long)ExactMath.CeilSqrt(CoreGapSquared(a, b));
        var flat = Math.Max(0, centres - a.Radius.Thousandths - b.Radius.Thousandths);
        var dz = Math.Abs(a.Elevation.Thousandths - b.Elevation.Thousandths);
        return Length.FromThousandths(dz == 0 ? flat : (long)ExactMath.CeilSqrt((Int128)flat * flat + (Int128)dz * dz));
    }
}
