using Rules.Core.State;

namespace Rules.Core.Geometry;

// A model as geometry: a round base. Limbs, weapons and heights play no part (U2). The caller supplies the radius;
// Rules.Core invents no real model sizes.
public sealed record ModelBody
{
    public ModelBody(string id, Point2 centre, Length radius)
    {
        Id = IdRules.Check(id, nameof(id));
        Centre = centre;
        Radius = Limits.Check(radius, nameof(radius));
        if (radius <= Length.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "A base needs a positive radius.");
        }
    }

    public string Id { get; }
    public Point2 Centre { get; }
    public Length Radius { get; }

    public ModelBody At(Point2 centre) => new(Id, centre, Radius);

    public override string ToString() => $"{Id} at {Centre}, radius {Radius}";
}

// Distance helpers between bases. Measurements run from base edge to base edge (p. 30).
public static class Measure
{
    // Is the gap between the two bases at most the distance (rulebook "within")? Exact.
    public static bool IsWithin(ModelBody a, ModelBody b, Length distance) =>
        ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius + distance, strict: false);

    // Do the bases overlap? Touching bases do not overlap.
    public static bool Overlap(ModelBody a, ModelBody b) =>
        ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius, strict: true);

    // In contact: the bases touch or overlap (p. 30).
    public static bool InContact(ModelBody a, ModelBody b) =>
        ExactMath.Near(a.Centre, b.Centre, a.Radius + b.Radius, strict: false);

    // The edge-to-edge gap rounded up to the next thousandth of an inch (overlapping bases report zero).
    // For display and reports only; rules use IsWithin and Overlap.
    public static Length Gap(ModelBody a, ModelBody b)
    {
        var centres = (long)ExactMath.CeilSqrt(ExactMath.DistanceSquared(a.Centre, b.Centre));
        return Length.FromThousandths(Math.Max(0, centres - a.Radius.Thousandths - b.Radius.Thousandths));
    }
}
