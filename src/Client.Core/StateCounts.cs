using Rules.Core.State;

namespace Client.Core;

// Counts read straight from a server state, for the Godot UI check, which may not touch the rules' immutable arrays
// itself (the Godot client's assembly guard, ClientAuthorityTest).
public static class StateCounts
{
    public static int Models(MatchState state, Seat seat, ModelStatus status)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Models.Count(m => m.Seat == seat && m.Status == status);
    }
}
