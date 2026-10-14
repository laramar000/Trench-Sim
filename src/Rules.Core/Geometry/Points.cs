namespace Rules.Core.Geometry;

// A point on the table (inches, origin at one corner of the battlefield).
public readonly record struct Point2
{
    public Point2(Length x, Length y)
    {
        X = Limits.Check(x, nameof(x));
        Y = Limits.Check(y, nameof(y));
    }

    public Length X { get; }
    public Length Y { get; }

    public override string ToString() => $"({X}, {Y})";
}

// An axis-aligned rectangle with positive width and height.
public readonly record struct Rect
{
    public Rect(Point2 min, Point2 max)
    {
        if (max.X <= min.X || max.Y <= min.Y)
        {
            throw new ArgumentException("A rectangle needs a positive width and height.");
        }
        Min = min;
        Max = max;
    }

    public Point2 Min { get; }
    public Point2 Max { get; }

    public Length Width => Max.X - Min.X;
    public Length Height => Max.Y - Min.Y;

    public bool Contains(Point2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    public IEnumerable<Point2> Corners()
    {
        yield return Min;
        yield return new Point2(Max.X, Min.Y);
        yield return Max;
        yield return new Point2(Min.X, Max.Y);
    }

    public override string ToString() => $"[{Min} to {Max}]";
}
