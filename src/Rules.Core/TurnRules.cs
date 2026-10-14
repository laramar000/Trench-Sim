using Rules.Core.State;

namespace Rules.Core;

// Questions about a state that both TurnSequence (deciding) and Reducer (checking events) ask.
// Pure functions of the state; no dice.
internal static class TurnRules
{
    public static IEnumerable<ModelState> ModelsOf(MatchState state, Seat seat) =>
        state.Models.Where(m => m.Seat == seat);

    public static int WarbandSize(MatchState state, Seat seat) => ModelsOf(state, seat).Count();

    // Models that are Down or Out of Action (rulebook 1.0.2 p. 50).
    public static int DownOrOut(MatchState state, Seat seat) =>
        ModelsOf(state, seat).Count(m => m.Status != ModelStatus.Standing);

    // Models counted for Initiative. Provisional rule R1 chooses between the two chapters (p. 32 / Core p. 14).
    public static int InitiativeCount(MatchState state, Seat seat)
    {
        var count = state.Setup!.Profile.InitiativeCount;
        return ModelsOf(state, seat).Count(m => m.Deployed && count switch
        {
            InitiativeCounting.ExcludeDownAndOutOfAction => m.Status == ModelStatus.Standing,
            _ => m.Status != ModelStatus.OutOfAction,
        });
    }

    public static bool HasUndeployed(MatchState state, Seat seat) =>
        ModelsOf(state, seat).Any(m => !m.Deployed);

    // Deployment alternates starting with DeploymentFirst; a side with nothing left lets the other continue (p. 152).
    public static Seat? NextDeployer(MatchState state)
    {
        if (state.DeploymentFirst is not { } first)
        {
            return null;
        }
        if (state.LastDeployer is not { } last)
        {
            return HasUndeployed(state, first) ? first : HasUndeployed(state, first.Opponent()) ? first.Opponent() : null;
        }
        if (HasUndeployed(state, last.Opponent()))
        {
            return last.Opponent();
        }
        return HasUndeployed(state, last) ? last : null;
    }

    // A model can be activated if it is in play and has not activated this turn. A Down model can
    // be activated: it stands back up when next activated (Core p. 20).
    public static bool CanActivate(ModelState model) =>
        model.Deployed && model.Status != ModelStatus.OutOfAction && !model.ActivatedThisTurn;

    public static bool HasActivatable(MatchState state, Seat seat) =>
        ModelsOf(state, seat).Any(CanActivate);

    // Players alternate; when one side runs out the other activates its remaining models (p. 33).
    public static Seat? NextActivator(MatchState state)
    {
        if (state.Phase != Phase.Activation)
        {
            return null;
        }
        if (state.LastActivator is not { } last)
        {
            if (state.FirstActivator is not { } first)
            {
                return null;
            }
            return HasActivatable(state, first) ? first : HasActivatable(state, first.Opponent()) ? first.Opponent() : null;
        }
        if (HasActivatable(state, last.Opponent()))
        {
            return last.Opponent();
        }
        return HasActivatable(state, last) ? last : null;
    }

    // A check is needed when half the warband (rounded up) is Down or Out of Action, or the warband is
    // Shaken (pp. 21 and 50). 2 * count >= size is "half or more, rounded up".
    public static bool NeedsMoraleCheck(MatchState state, Seat seat) =>
        state.SeatFor(seat).Shaken || 2 * DownOrOut(state, seat) >= WarbandSize(state, seat);

    // Seats that still have to make a Morale Check this turn.
    public static List<Seat> MoraleRemaining(MatchState state)
    {
        var remaining = new List<Seat>(2);
        foreach (var seat in new[] { Seat.One, Seat.Two })
        {
            var s = state.SeatFor(seat);
            if (!s.MoraleChecked && !s.Fled && NeedsMoraleCheck(state, seat))
            {
                remaining.Add(seat);
            }
        }
        return remaining;
    }

    public static MatchState WithSeat(MatchState state, Seat seat, SeatState value) =>
        seat == Seat.One ? state with { One = value } : state with { Two = value };

    public static MatchState WithModel(MatchState state, ModelState model)
    {
        for (var i = 0; i < state.Models.Length; i++)
        {
            if (state.Models[i].Id == model.Id)
            {
                return state with { Models = state.Models.SetItem(i, model) };
            }
        }
        throw new InvalidOperationException($"Unknown model {model.Id}.");
    }
}
