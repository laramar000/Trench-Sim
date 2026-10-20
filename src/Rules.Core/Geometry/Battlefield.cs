using System.Collections.Immutable;
using Rules.Core.State;

namespace Rules.Core.Geometry;

[Flags]
public enum TerrainKind
{
    Open = 0,
    Difficult = 1,
    Dangerous = 2,
    Impassable = 4,
}

// What a terrain piece does to sight and cover. A setup designation, not a measured height (U14): thresholds come from
// the rulebook (cover needs 1/2" or more, p. 38; up to 1" counts as Open, p. 39) but a scenario author assigns them.
public enum TerrainProfile
{
    // Under 1/2": no effect on sight, never cover.
    Low,

    // 1/2" to 1": never blocks sight, can give cover.
    Cover,

    // Substantial (buildings, high walls): blocks sight, can give cover, cannot be entered unless it is a walkable platform.
    Wall,
}

// Is the top of the piece high ground? Only Walls can be High (a raised surface of 1" or less is Open, p. 39).
public enum SurfaceLevel
{
    Ground,
    High,
}

// A terrain piece: a footprint, a profile, the rulebook's terrain types (p. 38) and whether models can stand on it.
// Allowed combinations (anything else throws ArgumentException, checked in this order):
//   Low or Cover: Ground surface, walkable (non-walkable only with Impassable).
//   Wall + Ground: not walkable.
//   Wall + High: walkable (a platform or roof) or not (a tower).
//   Impassable: never walkable. Difficult and Dangerous: walkable only.
public sealed record TerrainBlock
{
    public TerrainBlock(
        string id,
        Footprint footprint,
        TerrainProfile profile,
        TerrainKind kind = TerrainKind.Open,
        SurfaceLevel surface = SurfaceLevel.Ground,
        bool? walkable = null)
    {
        Id = IdRules.Check(id, nameof(id));
        ArgumentNullException.ThrowIfNull(footprint);
        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentException("Unknown terrain profile.", nameof(profile));
        }
        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentException("Unknown surface level.", nameof(surface));
        }
        if ((kind & ~(TerrainKind.Difficult | TerrainKind.Dangerous | TerrainKind.Impassable)) != 0)
        {
            throw new ArgumentException("Unknown terrain kind.", nameof(kind));
        }
        if (surface == SurfaceLevel.High && profile != TerrainProfile.Wall)
        {
            throw new ArgumentException("Only a Wall can have a High surface.", nameof(surface));
        }

        var impassable = kind.HasFlag(TerrainKind.Impassable);
        var effective = walkable ?? (!impassable && (profile != TerrainProfile.Wall || surface == SurfaceLevel.High));
        if (impassable && effective)
        {
            throw new ArgumentException("Impassable terrain is never walkable.", nameof(walkable));
        }
        if (profile == TerrainProfile.Wall && surface == SurfaceLevel.Ground && effective)
        {
            throw new ArgumentException("A Wall on the ground cannot be walked on (climbing is not implemented).", nameof(walkable));
        }
        if (profile != TerrainProfile.Wall && !effective && !impassable)
        {
            throw new ArgumentException("Low and Cover terrain are walkable unless Impassable.", nameof(walkable));
        }
        if (!effective && (kind.HasFlag(TerrainKind.Difficult) || kind.HasFlag(TerrainKind.Dangerous)))
        {
            throw new ArgumentException("Difficult and Dangerous terrain must be walkable.", nameof(kind));
        }

        Footprint = footprint;
        Profile = profile;
        Kind = kind;
        Surface = surface;
        IsWalkable = effective;
    }

    public string Id { get; }
    public Footprint Footprint { get; }
    public TerrainProfile Profile { get; }
    public TerrainKind Kind { get; }
    public SurfaceLevel Surface { get; }

    // Can models stand on and move across it?
    public bool IsWalkable { get; }

    public bool IsDifficult => Kind.HasFlag(TerrainKind.Difficult);

    // A walkable High surface: a model on it has high ground.
    public bool IsPlatform => Surface == SurfaceLevel.High && IsWalkable;

    // Towers proposal, stage T1: a platform with a height and ladders is a tower, reached only by its ladders. Null for
    // every other piece, which then behaves exactly as before (an untouched platform keeps U18). Set after
    // construction; only an Open, walkable High platform can be a tower, and each ladder must stand on a side of the
    // footprint, not at a corner.
    public Climb? Climb
    {
        get => _climb;
        init
        {
            if (value is not null)
            {
                if (!IsPlatform || Kind != TerrainKind.Open)
                {
                    throw new ArgumentException("Only an Open, walkable High platform can be a tower.", nameof(Climb));
                }
                if (value.Ladders.FirstOrDefault(l => Climb.SideOf(Footprint.Bounds, l.At) is null) is { } off)
                {
                    throw new ArgumentException($"Ladder '{off.Id}' must stand on a side of the tower, not at a corner or away from it.", nameof(Climb));
                }
            }
            _climb = value;
        }
    }

    private readonly Climb? _climb;

    public bool IsTower => _climb is not null;
}

public sealed record DeploymentZone
{
    public DeploymentZone(string zoneId, Footprint area)
    {
        ZoneId = IdRules.Check(zoneId, nameof(zoneId));
        ArgumentNullException.ThrowIfNull(area);
        Area = area;
    }

    // The same opaque id the match setup uses (M2).
    public string ZoneId { get; }
    public Footprint Area { get; }
}

// The table: a rectangle from (0, 0) to (width, height), terrain and deployment zones. Immutable; terrain and
// zones are held in id order so nothing depends on the order they were supplied in.
public sealed record Battlefield
{
    public Battlefield(Length width, Length height, IEnumerable<TerrainBlock> terrain, IEnumerable<DeploymentZone> zones)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(zones);
        if (width <= Length.Zero || height <= Length.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The battlefield needs a positive size.");
        }
        Width = Limits.Check(width, nameof(width));
        Height = Limits.Check(height, nameof(height));
        Area = Footprint.Of(new Rect(new Point2(Length.Zero, Length.Zero), new Point2(width, height)));

        Terrain = terrain.OrderBy(t => t.Id, StringComparer.Ordinal).ToImmutableArray();
        Zones = zones.OrderBy(z => z.ZoneId, StringComparer.Ordinal).ToImmutableArray();
        if (Terrain.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != Terrain.Length
            || Zones.Select(z => z.ZoneId).Distinct(StringComparer.Ordinal).Count() != Zones.Length)
        {
            throw new ArgumentException("Terrain ids and zone ids must be unique.");
        }
        if (Terrain.Any(t => !t.Footprint.IsWithin(Area)) || Zones.Any(z => !z.Area.IsWithin(Area)))
        {
            throw new ArgumentException("Terrain and zones must lie on the battlefield.");
        }
        var platforms = Terrain.Where(t => t.IsPlatform).ToList();
        for (var i = 0; i < platforms.Count; i++)
        {
            for (var j = i + 1; j < platforms.Count; j++)
            {
                if (platforms[i].Footprint.OverlapsInside(platforms[j].Footprint))
                {
                    throw new ArgumentException($"Walkable high-ground platforms '{platforms[i].Id}' and '{platforms[j].Id}' overlap, so a model's level would be ambiguous.");
                }
            }
        }
    }

    public Length Width { get; }
    public Length Height { get; }
    public Footprint Area { get; }
    public ImmutableArray<TerrainBlock> Terrain { get; }
    public ImmutableArray<DeploymentZone> Zones { get; }

    public DeploymentZone? Zone(string zoneId) => Zones.FirstOrDefault(z => z.ZoneId == zoneId);
}
