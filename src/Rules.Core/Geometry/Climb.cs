using System.Collections.Immutable;
using Rules.Core.State;

namespace Rules.Core.Geometry;

// A side of a rectangular footprint.
public enum Side
{
    West,
    East,
    South,
    North,
}

// A ladder: a point on one side of a tower's footprint where a model climbs (towers proposal, stage T1). Ladders are
// Open terrain (1.0.2 p. 39), so climbing one needs no roll. The ladder is a point: the book gives no ladder width,
// and a model climbs at the point (provisional U71). Its drawn width is decoration.
public sealed record Ladder
{
    public Ladder(string id, Point2 at)
    {
        Id = IdRules.Check(id, nameof(id));
        At = at;
    }

    public string Id { get; }

    // The point on the footprint's edge.
    public Point2 At { get; }
}

// What makes a walkable High platform a tower: its height and its ladders (towers proposal, stage T1). A model reaches
// or leaves the platform only by a ladder (jumping down is not built, U68), and stands either wholly on it or wholly off
// it (U67). Heights must be over 1" (lower terrain is Open, p. 39) and under 3" (from 3" the Elevated position bonus,
// p. 43, and Falling, p. 41, would apply, and neither is built; U69).
public sealed record Climb
{
    public static readonly Length MinHeightExclusive = Length.Inch;
    public static readonly Length MaxHeightExclusive = Length.FromInches(3);

    public Climb(Length height, IEnumerable<Ladder> ladders)
    {
        ArgumentNullException.ThrowIfNull(ladders);
        if (height <= MinHeightExclusive || height >= MaxHeightExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "A tower must be over 1\" high (lower terrain is Open, p. 39) and under 3\" (the Elevated position and Falling rules for 3\" or more are not built).");
        }
        Height = height;
        Ladders = ladders.OrderBy(l => l.Id, StringComparer.Ordinal).ToImmutableArray();
        if (Ladders.Length == 0)
        {
            throw new ArgumentException("A tower needs at least one ladder.", nameof(ladders));
        }
        if (Ladders.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count() != Ladders.Length)
        {
            throw new ArgumentException("Ladder ids must be unique.", nameof(ladders));
        }
    }

    public Length Height { get; }

    // In id order.
    public ImmutableArray<Ladder> Ladders { get; }

    public bool Equals(Climb? other) =>
        other is not null && Height == other.Height && Ladders.SequenceEqual(other.Ladders);

    public override int GetHashCode() => HashCode.Combine(Height, Ladders.Length);

    // The side of the rectangle a ladder stands on, or null when the point is not on a side or is at a corner.
    public static Side? SideOf(Rect rect, Point2 p)
    {
        var onX = p.X > rect.Min.X && p.X < rect.Max.X;
        var onY = p.Y > rect.Min.Y && p.Y < rect.Max.Y;
        if (onY && p.X == rect.Min.X)
        {
            return Side.West;
        }
        if (onY && p.X == rect.Max.X)
        {
            return Side.East;
        }
        if (onX && p.Y == rect.Min.Y)
        {
            return Side.South;
        }
        if (onX && p.Y == rect.Max.Y)
        {
            return Side.North;
        }
        return null;
    }

    // Where a base of this radius stands at the foot of the ladder: on the ground, touching the tower's side.
    public static Point2 Foot(Rect rect, Ladder ladder, Length radius) => Offset(rect, ladder, radius);

    // Where it stands at the top: on the platform, touching the same side from inside.
    public static Point2 Top(Rect rect, Ladder ladder, Length radius) => Offset(rect, ladder, -radius);

    private static Point2 Offset(Rect rect, Ladder ladder, Length outward) => SideOf(rect, ladder.At) switch
    {
        Side.West => new Point2(ladder.At.X - outward, ladder.At.Y),
        Side.East => new Point2(ladder.At.X + outward, ladder.At.Y),
        Side.South => new Point2(ladder.At.X, ladder.At.Y - outward),
        Side.North => new Point2(ladder.At.X, ladder.At.Y + outward),
        _ => throw new InvalidOperationException("The ladder is not on a side of its tower."),
    };
}

public static class Towers
{
    // How high a base centred here stands: the height of the tower whose footprint holds the centre strictly inside,
    // else zero. A base never straddles a tower's side at rest (U67), so the centre decides.
    public static Length ElevationAt(Battlefield battlefield, Point2 centre)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        foreach (var tower in battlefield.Terrain)
        {
            if (tower.Climb is { } climb && tower.Footprint.ContainsInside(centre))
            {
                return climb.Height;
            }
        }
        return Length.Zero;
    }

    // The tower whose platform holds the centre strictly inside, if any.
    public static TerrainBlock? TowerAt(Battlefield battlefield, Point2 centre)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        return battlefield.Terrain.FirstOrDefault(t => t.IsTower && t.Footprint.ContainsInside(centre));
    }
}
