namespace Rules.Core.Geometry;

public sealed record CoverResult(bool InCover, string? TerrainId, SightResult Sight);

// Cover (Terrain, p. 38): a model has cover from another model if it is on or in contact with a terrain piece that is
// at least 1/2" high, at least as wide as its base, and lies between the two so that it partially blocks line of
// sight. Cover is worth -1 DICE to a Ranged Attack (p. 43); applying it belongs to M4. Cover is separate from line of
// sight: a Wall far from the target blocks sight but gives no cover.
//
// Cover reads the same sight trace as LineOfSight and the same SightRules.Obstructs, so the two cannot disagree.
// Provisional readings (docs/interpretations.md):
//  * U14 the 1/2" minimum is carried by the profile: Cover and Wall qualify, Low does not.
//  * U4  "as wide as its base": the piece's extent across the attacker-to-target line is at least the base diameter.
//  * U5  "between / partially blocks": a Wall must obstruct at least one ray; a Cover piece (which never blocks)
//        must lie across at least one ray. A Wall that SightRules ignores (a high-ground attacker looking over it)
//        therefore gives no cover (U22). Cover pieces give cover at either level (U23).
//  * U15 "on or in contact": the target's base touches or overlaps the piece's footprint.
// If several pieces qualify, the lowest id is reported.
public static class Cover
{
    public static CoverResult Of(Battlefield battlefield, ModelBody attacker, ModelBody target, ISightSampler? sampler = null)
    {
        var sight = LineOfSight.Between(battlefield, attacker, target, sampler);
        if (sight.Visibility == Visibility.None)
        {
            return new CoverResult(false, null, sight);
        }

        foreach (var terrain in battlefield.Terrain)
        {
            if (terrain.Profile == TerrainProfile.Low
                || !terrain.Footprint.CircleTouches(target.Centre, target.Radius)
                || !terrain.Footprint.IsAtLeastAsWideAcross(attacker.Centre, target.Centre, target.Radius * 2))
            {
                continue;
            }
            var between = terrain.Profile == TerrainProfile.Wall
                ? sight.Rays.Any(r => r.Obstructing.Contains(terrain.Id))
                : sight.Rays.Any(r => r.Crossed.Contains(terrain.Id));
            if (between)
            {
                return new CoverResult(true, terrain.Id, sight);
            }
        }
        return new CoverResult(false, null, sight);
    }
}
