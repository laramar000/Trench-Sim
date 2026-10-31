using Rules.Core.State;

namespace Rules.Core.Geometry;

// A model as geometry: a round base. Limbs, weapons and model heights play no part (U2). The caller supplies the radius;
// Rules.Core invents no real model sizes. Elevation is how high the base stands: zero on the ground, a tower's height on
// its platform (towers proposal, stage T1); Board.BodyOf fills it in from the battlefield.
public sealed record ModelBody
{
    public ModelBody(string id, Point2 centre, Length radius, Length elevation = default)
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
    }

    public string Id { get; }
    public Point2 Centre { get; }
    public Length Radius { get; }
    public Length Elevation { get; }

    // The same base moved on the table at the same elevation. A move that can change level uses Board.BodyAt instead.
    public ModelBody At(Point2 centre) => new(Id, centre, Radius, Elevation);

    public ModelBody AtElevation(Length elevation) => new(Id, Centre, Radius, elevation);

    public override string ToString() => Elevation == Length.Zero
        ? $"{Id} at {Centre}, radius {Radius}"
        : $"{Id} at {Centre}, radius {Radius}, {Elevation} up";
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
            return ExactMath.Near(a.Centre, b.Centre, reach + distance, strict: false);
        }
        var d = (Int128)distance.Thousandths;
        if (dz > d)
        {
            return false;
        }
        // Horizontal gap h = max(0, c - R) with c the centres' distance and R the two radii; within when h^2 + dz^2 <= d^2,
        // that is c <= R + s with s^2 = d^2 - dz^2. Squared without roots: c^2 - R^2 - s^2 <= 2Rs.
        var c2 = (Int128)ExactMath.DistanceSquared(a.Centre, b.Centre);
        var r = (Int128)reach.Thousandths;
        var s2 = d * d - dz * dz;
        var left = c2 - r * r - s2;
        return left <= 0 || left * left <= 4 * r * r * s2;
    }

    // Do the bases overlap? Touching bases do not overlap, and bases at different elevations never do.
    public static bool Overlap(ModelBody a, ModelBody b) =>
        a.Elevation == b.Elevation && ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius, strict: true);

    // In contact: the bases touch or overlap (p. 30); bases at different elevations are not in contact.
    public static bool InContact(ModelBody a, ModelBody b) =>
        a.Elevation == b.Elevation && ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius, strict: false);

    // The distance between the nearest points rounded up to the next thousandth of an inch (overlapping bases report
    // zero). For display and reports only; rules use IsWithin and Overlap.
    public static Length Gap(ModelBody a, ModelBody b)
    {
        var centres = (long)ExactMath.CeilSqrt(ExactMath.DistanceSquared(a.Centre, b.Centre));
        var flat = Math.Max(0, centres - a.Radius.Thousandths - b.Radius.Thousandths);
        var dz = Math.Abs(a.Elevation.Thousandths - b.Elevation.Thousandths);
        return Length.FromThousandths(dz == 0 ? flat : (long)ExactMath.CeilSqrt((Int128)flat * flat + (Int128)dz * dz));
    }
}
