using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// Two enemy models in base contact or within 1" of each other (pp. 45-46), from Seat One's model to Seat Two's.
public readonly record struct EngagedPair(ModelId One, ModelId Two);

// Read-only, for the client (QoL I5, decision D1 "Rules' test", chosen by Jason on 2026-10-19): which enemy models are
// engaged with each other now, by the very measure the rules use to say a model is engaged (Board.IsEngaged:
// Board.EngagementRange between the bases, Down models included). Nothing here changes a rule or a state.
public static class Engagement
{
    public static IReadOnlyList<EngagedPair> Pairs(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Setup is null)
        {
            return [];
        }
        var onTable = Board.OnTable(state).Where(m => state.Setup.ProfileOf(m.Id) is not null).ToList();
        var pairs = new List<EngagedPair>();
        foreach (var one in onTable.Where(m => m.Seat == Seat.One))
        {
            var body = Board.BodyOf(state, one)!;
            foreach (var two in onTable.Where(m => m.Seat == Seat.Two))
            {
                if (Measure.IsWithin(body, Board.BodyOf(state, two)!, Board.EngagementRange))
                {
                    pairs.Add(new EngagedPair(one.Id, two.Id));
                }
            }
        }
        return pairs;
    }

    // Read-only, for the Retreat preview (proposal J, stage J2b, decision D2 approved by Jason on 2026-10-20): the enemy
    // models that could each make a free attack if this model retreated now, by the very test the rules use once a
    // Retreat is declared (Board.FreeFighters: standing, with a Melee Weapon, within 1"). The server decides when the
    // Retreat is declared; this changes nothing.
    public static IReadOnlyList<ModelId> FreeAttackersIfRetreating(MatchState state, ModelId model)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Setup?.Scenario is null || state.FindModel(model) is null)
        {
            return [];
        }
        return [.. Board.FreeFighters(state, new RetreatInProgress(model, [], [])).Select(m => m.Id)];
    }
}
