namespace Rules.Core.Geometry;

public sealed record CoverResult(bool InCover, string? TerrainId, SightResult Sight);

// Cover (Terrain, p. 38): a model has cover from another model if it is on or in contact with a terrain piece that is
// at least 1/2" high, at least as wide as its base, and lies between the two so that it partially blocks line of
// sight. Cover is worth -1 DICE to a Ranged Attack (p. 43) and to a Melee Attack across a Defended Obstacle (p. 45).
// Cover is separate from line of sight: a Wall far from the target blocks sight but gives no cover.
//
// Since line-of-sight stage L2 (2026-10-25) cover reads the height-based sight (HeightSight, U77), so sight and cover
// cannot disagree. A piece gives cover only if it passes every test (U78):
//  * it is at least 1/2" high (SightHeights.TopOf: Cover 0.75", Wall 3", a tower its height; Low 0.25" never);
//  * the target's base touches or overlaps it (U15);
//  * its extent across the attacker-to-target line is at least the base diameter (U4);
//  * it hides at least one of the target's points, and the target is still partly seen (Partial). A target seen in
//    full has no cover; a target seen not at all cannot be targeted. A ray merely crossing a piece gives nothing.
// The platform a model stands on never blocks sight to it (U19), so it gives that model no cover (U21).
// A trench (trench-maps stage S3, U83) gives cover to a model in it when its edge hides at least one of the model's
// points (the ray is named for the target's trench) and the model is still partly seen. A trench is at least 1/2" deep
// and holds the base, so the height, touching and width tests are met by what makes a model "in" it.
// If several pieces qualify, the lowest id is reported.
public static class Cover
{
    public static readonly Length MinimumHeight = Length.Parse("0.5");

    public static CoverResult Of(Battlefield battlefield, ModelBody attacker, ModelBody target)
    {
        var sight = HeightSight.Between(battlefield, attacker, target);
        if (sight.Visibility != Visibility.Partial)
        {
            return new CoverResult(false, null, sight);
        }

        var inTrench = battlefield.TrenchDepth is not null ? Trenches.Of(battlefield, target)?.Id : null;
        foreach (var terrain in battlefield.Terrain)
        {
            if (terrain.IsTrench)
            {
                if (terrain.Id == inTrench && sight.Rays.Any(r => r.Obstructing.Contains(terrain.Id)))
                {
                    return new CoverResult(true, terrain.Id, sight);
                }
                continue;
            }
            if (SightHeights.TopOf(terrain) < MinimumHeight
                || !terrain.Footprint.CircleTouches(target.Centre, target.Radius)
                || !terrain.Footprint.IsAtLeastAsWideAcross(attacker.Centre, target.Centre, target.Radius * 2))
            {
                continue;
            }
            if (sight.Rays.Any(r => r.Obstructing.Contains(terrain.Id)))
            {
                return new CoverResult(true, terrain.Id, sight);
            }
        }
        return new CoverResult(false, null, sight);
    }
}
