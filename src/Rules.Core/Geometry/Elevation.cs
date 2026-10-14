namespace Rules.Core.Geometry;

public enum ModelLevel
{
    Ground,
    High,
}

// Where a model stands, vertically, in the only terms M3 uses: on the ground, or on a walkable High platform.
// There is no height or vertical coordinate (U17). Level is High only when more than half the base is on a platform.
// PlatformId names the platform whose footprint contains the base centre; for a ground model at a platform corner
// (centre inside, half the base or less on it) that platform is named too, so it never counts as an obstruction
// between that model and anyone else (U19).
public readonly record struct ModelPosition(ModelLevel Level, string? PlatformId)
{
    public static ModelPosition OnGround => new(ModelLevel.Ground, null);
}

public static class Elevation
{
    // A model has high ground if more than half of its base is within a walkable High-surface piece (the same
    // "in terrain" test as p. 38; provisional U17). Walkable platforms never overlap (Battlefield checks), so
    // at most one qualifies. A base too close to call counts as ground, like everywhere else.
    // A ground model whose centre is strictly inside a platform (only possible near a corner) still names it.
    public static ModelPosition Of(Battlefield battlefield, ModelBody body)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(body);
        foreach (var platform in battlefield.Terrain.Where(t => t.IsPlatform))
        {
            if (platform.Footprint.MoreThanHalfInside(body.Centre, body.Radius) == AreaDecision.MoreThanHalf)
            {
                return new ModelPosition(ModelLevel.High, platform.Id);
            }
        }
        foreach (var platform in battlefield.Terrain.Where(t => t.IsPlatform))
        {
            if (platform.Footprint.ContainsInside(body.Centre))
            {
                return new ModelPosition(ModelLevel.Ground, platform.Id);
            }
        }
        return ModelPosition.OnGround;
    }
}
