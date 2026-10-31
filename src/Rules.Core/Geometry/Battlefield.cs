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
        // Trench-maps stage S2: joined parts are for ground pieces; levels and towers keep one rectangle.
        if (surface == SurfaceLevel.High && !footprint.IsSingle)
        {
            throw new ArgumentException("A High piece (platform, roof or tower) must be one rectangle.", nameof(footprint));
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

    // Towers proposal, stage T1: a platform with a height and ladders is a tower, reached by its ladders or its sides. Null for
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

    // Trench-maps stage S3: a trench section is a Low, walkable ground piece with a depth (U80). Its sides are under 3"
    // and crossed as Open terrain (p. 62), so movement treats it as any walkable ground piece; its floor is below the
    // ground, which only sight, cover and the Elevated position read (U81 to U83). Null for every other piece, which
    // then behaves exactly as before. Depth is 1/2" (the least that gives cover, p. 38) up to under 3" (from 3" a side
    // must be climbed or jumped, p. 62, which is not built).
    public Length? Depth
    {
        get => _depth;
        init
        {
            if (value is { } depth)
            {
                if (Profile != TerrainProfile.Low || Surface != SurfaceLevel.Ground || !IsWalkable)
                {
                    throw new ArgumentException("A trench must be a Low, walkable piece on the ground.", nameof(Depth));
                }
                if (depth < MinTrenchDepth || depth >= MaxTrenchDepthExclusive)
                {
                    throw new ArgumentOutOfRangeException(nameof(Depth), "A trench must be at least 1/2\" deep (the least that gives cover, p. 38) and under 3\" (deeper sides must be climbed or jumped, p. 62, which is not built).");
                }
            }
            _depth = value;
        }
    }

    private readonly Length? _depth;

    public bool IsTrench => _depth is not null;

    public static readonly Length MinTrenchDepth = Length.Parse("0.5");
    public static readonly Length MaxTrenchDepthExclusive = Length.FromInches(3);
}

public sealed record DeploymentZone
{
    public DeploymentZone(string zoneId, Footprint area)
    {
        ZoneId = IdRules.Check(zoneId, nameof(zoneId));
        ArgumentNullException.ThrowIfNull(area);
        if (!area.IsSingle)
        {
            throw new ArgumentException("A deployment zone must be one rectangle.", nameof(area));
        }
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
        // Towers (towers proposal, stage T2): one height for all, so two models at different elevations are always
        // more than 1" apart (U69, U70); and no other terrain on a tower's footprint, so its platform and sides are plain.
        var towers = Terrain.Where(t => t.IsTower).ToList();
        if (towers.Select(t => t.Climb!.Height).Distinct().Count() > 1)
        {
            throw new ArgumentException("All towers on a battlefield must have the same height.");
        }
        foreach (var tower in towers)
        {
            if (Terrain.FirstOrDefault(t => t.Id != tower.Id && t.Footprint.OverlapsInside(tower.Footprint)) is { } other)
            {
                throw new ArgumentException($"Terrain '{other.Id}' overlaps tower '{tower.Id}'.");
            }
        }
        // Trenches (trench-maps stage S3, U80): one depth for all, so a network of sections that touch has one floor; and
        // nothing overlaps a trench (pieces may touch it), so the ground around it is plain.
        var trenches = Terrain.Where(t => t.IsTrench).ToList();
        if (trenches.Select(t => t.Depth!.Value).Distinct().Count() > 1)
        {
            throw new ArgumentException("All trenches on a battlefield must have the same depth.");
        }
        foreach (var trench in trenches)
        {
            if (Terrain.FirstOrDefault(t => t.Id != trench.Id && t.Footprint.OverlapsInside(trench.Footprint)) is { } other)
            {
                throw new ArgumentException($"Terrain '{other.Id}' overlaps trench '{trench.Id}'.");
            }
        }
        TrenchDepth = trenches.Count > 0 ? trenches[0].Depth!.Value : null;
        TrenchParts = [.. trenches.SelectMany(t => t.Footprint.Parts.Select(p => (t.Id, p)))];
    }

    public Length Width { get; }
    public Length Height { get; }
    public Footprint Area { get; }
    public ImmutableArray<TerrainBlock> Terrain { get; }
    public ImmutableArray<DeploymentZone> Zones { get; }

    // The trenches' one depth (null with no trench), and every part of every trench with its piece's id, in id order.
    public Length? TrenchDepth { get; }

    internal ImmutableArray<(string Id, Rect Part)> TrenchParts { get; }

    public DeploymentZone? Zone(string zoneId) => Zones.FirstOrDefault(z => z.ZoneId == zoneId);
}
