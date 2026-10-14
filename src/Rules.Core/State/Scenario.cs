using System.Collections.Immutable;
using Rules.Core.Geometry;

namespace Rules.Core.State;

// The geometry of a scenario: the table with its terrain and deployment zones, and which terrain pieces are
// Objectives (Scenario I, p. 152: the whole terrain piece is the Objective). Fixed when the match is created.
public sealed record Scenario
{
    public Scenario(Battlefield battlefield, IEnumerable<string> objectiveTerrainIds)
    {
        ArgumentNullException.ThrowIfNull(battlefield);
        ArgumentNullException.ThrowIfNull(objectiveTerrainIds);
        if (battlefield.Zones.Length != 2)
        {
            throw new ArgumentException("A scenario battlefield has exactly two deployment zones.", nameof(battlefield));
        }
        var objectives = objectiveTerrainIds.Select(id => IdRules.Check(id, "objective")).OrderBy(i => i, StringComparer.Ordinal).ToImmutableArray();
        if (objectives.Distinct(StringComparer.Ordinal).Count() != objectives.Length)
        {
            throw new ArgumentException("Objective ids must be unique.", nameof(objectiveTerrainIds));
        }
        foreach (var id in objectives)
        {
            var piece = battlefield.Terrain.FirstOrDefault(t => t.Id == id);
            if (piece is null || !piece.IsWalkable)
            {
                throw new ArgumentException($"Objective '{id}' must be a walkable terrain piece on the battlefield.", nameof(objectiveTerrainIds));
            }
        }
        Battlefield = battlefield;
        ObjectiveIds = objectives;
    }

    public Battlefield Battlefield { get; }
    public ImmutableArray<string> ObjectiveIds { get; }
}
