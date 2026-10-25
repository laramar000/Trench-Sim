namespace Rules.Core.Geometry;

// Trench-maps stage S3: which trench a model is in (U81). A model is in a trench when more than half of its base is
// inside the trench network, all trench sections taken together (the "in terrain" test of p. 38, which Scenario IV
// scores, p. 163), so a base on the seam where two sections meet is in the trench. Its base then stands on the floor,
// the battlefield's trench depth below the ground. A base too close to call is on the ground, as everywhere else.
public static class Trenches
{
    // The trench piece the model is in: the lowest id whose footprint holds the base centre, else the lowest id the base
    // overlaps. Null when the model is not in a trench (or stands on a tower: towers and trenches never overlap).
    public static TerrainBlock? Of(Battlefield battlefield, ModelBody body)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(body);
        if (battlefield.TrenchParts.IsEmpty || body.Elevation != Length.Zero)
        {
            return null;
        }
        var parts = battlefield.TrenchParts.Select(p => p.Part).ToList();
        if (BaseArea.MoreThanHalfInside(body.Centre, body.Radius, parts) != AreaDecision.MoreThanHalf)
        {
            return null;
        }
        var trenches = battlefield.Terrain.Where(t => t.IsTrench).ToList();
        return trenches.FirstOrDefault(t => t.Footprint.Parts.Any(p => p.Contains(body.Centre)))
            ?? trenches.First(t => t.Footprint.CircleOverlaps(body.Centre, body.Radius));
    }
}
