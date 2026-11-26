using Rules.Core.Events;
using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Rules.Core;

// Trench Warfare (trench-maps stage S6), read-only, for the client: what each scoring trench section would give if it
// were scored now, by the very test the end-of-turn scoring uses (Board.TrenchResults). Empty in any other scenario
// and before the roles are chosen. Nothing here changes a rule or a state.
public static class TrenchScores
{
    public static IReadOnlyList<TrenchResult> Now(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return TrenchWarfare.Is(state.Setup) && state.Attacker is not null && state.Setup!.Scenario is not null ? Board.TrenchResults(state) : [];
    }
}
