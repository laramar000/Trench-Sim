namespace Rules.Core.Geometry;

public enum ModelLevel
{
    Ground,
    High,
}

// Where a model stands, vertically, in the only terms M3 uses: on the ground, or on a walkable High platform.
// There is no height or vertical coordinate (U17).
public readonly record struct ModelPosition(ModelLevel Level, string? PlatformId)
{
    public static ModelPosition OnGround => new(ModelLevel.Ground, null);
}

public static class Elevation
{
    // A model has high ground if more than half of its base is within a walkable High-surface piece (the same
    // "in terrain" test as p. 38; provisional U17). Walkable platforms never overlap (Battlefield checks), so
    // at most one qualifies. A base too close to call counts as ground, like everywhere else.
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
        return ModelPosition.OnGround;
    }
}
