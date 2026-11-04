namespace Rules.Core.Geometry;

public enum PlacementRefusal
{
    OffBattlefield,
    InBlockingTerrain,
    OverlapsBase,
    UnknownZone,
    OutsideZone,
}

public sealed record PlacementResult(bool IsLegal, PlacementRefusal? Reason, string Text, string RuleRef, string? SubjectId)
{
    internal static PlacementResult Legal { get; } = new(true, null, "The base can be set up there.", "1.0.2 p. 31", null);

    internal static PlacementResult Refuse(PlacementRefusal reason, string text, string rule, string? subject = null) =>
        new(false, reason, text, rule, subject);
}

// Setting a model up (Model Placement p. 31, Deployment Zones p. 146): the base may not overlap Impassable terrain
// or another base, more than half of it must be on the battlefield, and for deployment it must lie wholly within the
// zone. Pure: the same inputs always give the same answer, whatever order the other bodies are listed in.
public static class Placement
{
    public static PlacementResult Check(Battlefield battlefield, ModelBody body, IEnumerable<ModelBody> others, string? deploymentZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(others);

        var half = battlefield.Area.MoreThanHalfInside(body.Centre, body.Radius);
        if (half != AreaDecision.MoreThanHalf)
        {
            return PlacementResult.Refuse(PlacementRefusal.OffBattlefield,
                half == AreaDecision.TooCloseToCall
                    ? "Too close to call whether more than half the base is on the battlefield; treated as not."
                    : "More than half of the base must be on the battlefield.",
                "1.0.2 p. 31");
        }

        var blocker = battlefield.Terrain.FirstOrDefault(t => !t.IsWalkable && t.Footprint.CircleOverlaps(body.Centre, body.Radius));
        if (blocker is not null)
        {
            return PlacementResult.Refuse(PlacementRefusal.InBlockingTerrain, "The base overlaps terrain that cannot be stood on.", "1.0.2 pp. 31, 38, 39", blocker.Id);
        }

        // Towers (stage T2): models are placed on the ground, and a tower is reached by a move (U68).
        var tower = battlefield.Terrain.FirstOrDefault(t => t.IsTower && t.Footprint.CircleOverlaps(body.Centre, body.Radius));
        if (tower is not null)
        {
            return PlacementResult.Refuse(PlacementRefusal.InBlockingTerrain, "Models are placed on the ground: a tower is reached by moving up a ladder or a side.", "1.0.2 pp. 39-40", tower.Id);
        }

        var clash = others.Where(o => o.Id != body.Id && Measure.Overlap(body, o)).OrderBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault();
        if (clash is not null)
        {
            return PlacementResult.Refuse(PlacementRefusal.OverlapsBase, "The base overlaps another model's base.", "1.0.2 p. 31", clash.Id);
        }

        if (deploymentZoneId is not null)
        {
            var zone = battlefield.Zone(deploymentZoneId);
            if (zone is null)
            {
                return PlacementResult.Refuse(PlacementRefusal.UnknownZone, "That is not a deployment zone on this battlefield.", "1.0.2 p. 146", deploymentZoneId);
            }
            if (!zone.Area.CircleInside(body.Centre, body.Radius))
            {
                return PlacementResult.Refuse(PlacementRefusal.OutsideZone, "A deployed model must be wholly within its deployment zone.", "1.0.2 p. 146", deploymentZoneId);
            }
        }
        return PlacementResult.Legal;
    }
}
