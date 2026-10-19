using Rules.Core.State;

namespace Rules.Core;

// Scenario I (pp. 152-153), read-only, for the client (QoL H4, decision D1 approved by Jason on 2026-10-18): who would
// control each Objective if it were scored now, by the very test the end-of-turn scoring uses (Board.Controller), and
// the Victory Points an Objective gives. Nothing here changes a rule or a state.
public static class Objectives
{
    public const int PointsPerObjective = Scoring.PointsPerObjective;

    // The seat that controls the Objective now, or null when neither does (or the match has no such Objective).
    public static Seat? ControllerNow(MatchState state, string objectiveId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(objectiveId);
        return state.Setup?.Scenario is { } scenario && scenario.ObjectiveIds.Contains(objectiveId) ? Board.Controller(state, objectiveId) : null;
    }
}
