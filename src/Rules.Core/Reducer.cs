using System.Collections.Immutable;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Core;

// Thrown when an event cannot legally follow the state it is applied to. A log that triggers this
// was corrupted or forged; it is not a player mistake (those are Rejections from TurnSequence.Decide).
public sealed class InvalidEventException(string message) : InvalidOperationException(message);

// Applies one event to one state. Pure: the same state and event always give the same new state.
// It holds no rules of its own beyond refusing an event that cannot follow the state.
public static class Reducer
{
    public static MatchState Replay(IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var state = MatchState.None;
        foreach (var e in events)
        {
            state = Apply(state, e);
        }
        return state;
    }

    public static MatchState Apply(MatchState state, GameEvent e)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(e);

        if (e is MatchCreated created)
        {
            Require(state.Phase == Phase.None, "A match was already created.");
            var models = created.Setup.Models
                .Select(m => new ModelState(m.Id, m.Seat))
                .ToImmutableArray();
            return state with { Setup = created.Setup, Phase = Phase.PreGame, Models = models };
        }

        Require(state.Phase != Phase.None, "No match has been created.");
        Require(state.Phase != Phase.Ended, "The match has ended.");

        return e switch
        {
            RollOffResolved => state,
            DecisionRequested x => OnDecisionRequested(state, x),
            DeploymentZoneChosen x => OnZoneChosen(state, x),
            DeploymentOrderSet x => OnDeploymentOrderSet(state, x),
            ModelDeployed x => OnModelDeployed(state, x),
            PhaseEntered x => OnPhaseEntered(state, x),
            TurnStarted x => OnTurnStarted(state, x),
            InitiativeDetermined x => OnInitiativeDetermined(state, x),
            FirstActivationChosen x => OnFirstActivationChosen(state, x),
            ActivationStarted x => OnActivationStarted(state, x),
            ActivationEnded x => OnActivationEnded(state, x),
            MoraleOrderSet x => OnMoraleOrderSet(state, x),
            MoraleCheckRolled x => OnMoraleCheckRolled(state, x),
            WarbandShaken x => OnWarbandShaken(state, x),
            WarbandRecovered x => OnWarbandRecovered(state, x),
            WarbandFled x => OnWarbandFled(state, x),
            TurnEnded x => OnTurnEnded(state, x),
            MatchEnded x => OnMatchEnded(state, x),
            _ => throw new InvalidEventException($"Unknown event type {e.GetType().Name}."),
        };
    }

    private static MatchState OnDecisionRequested(MatchState state, DecisionRequested e)
    {
        Require(state.Pending is null, "A decision is already pending.");
        switch (e.Kind)
        {
            case DecisionKind.ChooseDeploymentZone:
                Require(state.Phase == Phase.Deployment && state.One.ZoneId is null, "Zones are chosen once, at the start of Deployment.");
                break;
            case DecisionKind.ChooseFirstActivation:
                Require(state.Phase == Phase.Initiative && state.InitiativeHolder == e.Seat && state.FirstActivator is null,
                    "Only the Initiative holder chooses who activates first.");
                break;
            case DecisionKind.ChooseMoraleOrder:
                Require(state.Phase == Phase.Morale && state.InitiativeHolder == e.Seat && state.MoraleFirst is null
                    && TurnRules.MoraleRemaining(state).Count == 2, "Morale order is chosen only when both warbands must check.");
                break;
            case DecisionKind.FailedMorale:
                var s = state.SeatFor(e.Seat);
                Require(state.Phase == Phase.Morale && s.MoraleChecked && s.MoraleOutcome == Dice.SuccessOutcome.Failure && !s.Shaken,
                    "Only a failed, not-yet-Shaken warband chooses how to answer."); 
                break;
            default:
                throw new InvalidEventException("Unknown decision kind.");
        }
        return state with { Pending = new PendingDecision(e.Kind, e.Seat) };
    }

    private static MatchState OnZoneChosen(MatchState state, DeploymentZoneChosen e)
    {
        Require(state.Pending == new PendingDecision(DecisionKind.ChooseDeploymentZone, e.Seat), "No zone choice is pending for that seat.");
        var zones = state.Setup!.ZoneIds;
        Require(zones.Contains(e.ZoneId), "Unknown deployment zone.");
        var other = zones[0] == e.ZoneId ? zones[1] : zones[0];
        var next = TurnRules.WithSeat(state, e.Seat, state.SeatFor(e.Seat) with { ZoneId = e.ZoneId });
        next = TurnRules.WithSeat(next, e.Seat.Opponent(), next.SeatFor(e.Seat.Opponent()) with { ZoneId = other });
        return next with { Pending = null };
    }

    private static MatchState OnDeploymentOrderSet(MatchState state, DeploymentOrderSet e)
    {
        Require(state.Phase == Phase.Deployment && state.Pending is null && state.One.ZoneId is not null && state.DeploymentFirst is null,
            "Deployment order is set once, after the zones are chosen.");
        return state with { DeploymentFirst = e.First };
    }

    private static MatchState OnModelDeployed(MatchState state, ModelDeployed e)
    {
        Require(state.Phase == Phase.Deployment && state.Pending is null, "Models are deployed during Deployment.");
        Require(TurnRules.NextDeployer(state) == e.Seat, "It is not that seat's turn to deploy.");
        var model = state.FindModel(e.Model);
        Require(model is not null && model.Seat == e.Seat && !model.Deployed, "That model cannot be deployed by that seat.");
        return TurnRules.WithModel(state, model! with { Deployed = true }) with { LastDeployer = e.Seat };
    }

    private static MatchState OnPhaseEntered(MatchState state, PhaseEntered e)
    {
        Require(state.Pending is null, "A decision is pending.");
        switch ((state.Phase, e.Phase))
        {
            case (Phase.PreGame, Phase.Deployment):
                break;
            case (Phase.Initiative, Phase.Activation):
                Require(state.FirstActivator is not null, "Nobody has chosen who activates first.");
                break;
            case (Phase.Activation, Phase.Morale):
                Require(state.ActiveModel is null && TurnRules.NextActivator(state) is null, "Models still have to activate.");
                break;
            default:
                throw new InvalidEventException($"Cannot go from {state.Phase} to {e.Phase}.");
        }
        return state with { Phase = e.Phase };
    }

    private static MatchState OnTurnStarted(MatchState state, TurnStarted e)
    {
        Require(e.Turn == state.Turn + 1, "Turns count up by one.");
        if (state.Phase == Phase.Deployment)
        {
            Require(state.Turn == 0 && state.Models.All(m => m.Deployed) && state.Pending is null, "Deployment is not finished.");
        }
        else
        {
            Require(state.Phase == Phase.Morale && state.TurnEnded, "A new turn starts only after the previous one ended.");
        }
        return state with
        {
            Turn = e.Turn,
            TurnEnded = false,
            Models = state.Models.Select(m => m with { ActivatedThisTurn = false }).ToImmutableArray(),
            One = state.One with { MoraleChecked = false, MoraleOutcome = null },
            Two = state.Two with { MoraleChecked = false, MoraleOutcome = null },
            Phase = Phase.Initiative,
            InitiativeHolder = null,
            FirstActivator = null,
            LastActivator = null,
            MoraleFirst = null,
        };
    }

    private static MatchState OnInitiativeDetermined(MatchState state, InitiativeDetermined e)
    {
        Require(state.Phase == Phase.Initiative && state.InitiativeHolder is null && state.Pending is null, "Initiative is decided once per turn.");
        var one = TurnRules.InitiativeCount(state, Seat.One);
        var two = TurnRules.InitiativeCount(state, Seat.Two);
        Require(e.CountSeatOne == one && e.CountSeatTwo == two, "Counts do not match the state.");
        Require(one == two || e.Holder == (one < two ? Seat.One : Seat.Two), "The seat with fewer models holds the Initiative.");
        return state with { InitiativeHolder = e.Holder };
    }

    private static MatchState OnFirstActivationChosen(MatchState state, FirstActivationChosen e)
    {
        Require(state.Phase == Phase.Initiative && state.Pending is { Kind: DecisionKind.ChooseFirstActivation }, "No choice of first activator is pending.");
        Require(Enum.IsDefined(e.FirstActivator), "Unknown seat.");
        return state with { FirstActivator = e.FirstActivator, Pending = null };
    }

    private static MatchState OnActivationStarted(MatchState state, ActivationStarted e)
    {
        Require(state.Phase == Phase.Activation && state.Pending is null && state.ActiveModel is null, "An activation cannot start now.");
        Require(TurnRules.NextActivator(state) == e.Seat, "It is not that seat's turn to activate.");
        var model = state.FindModel(e.Model);
        Require(model is not null && model.Seat == e.Seat && TurnRules.CanActivate(model), "That model cannot activate.");
        return TurnRules.WithModel(state, model! with { ActivatedThisTurn = true }) with { ActiveModel = e.Model, LastActivator = e.Seat };
    }

    private static MatchState OnActivationEnded(MatchState state, ActivationEnded e)
    {
        Require(state.Phase == Phase.Activation && state.ActiveModel == e.Model, "That model is not the one activating.");
        Require(Enum.IsDefined(e.Reason), "Unknown end reason.");
        return state with { ActiveModel = null };
    }

    private static MatchState OnMoraleOrderSet(MatchState state, MoraleOrderSet e)
    {
        Require(state.Phase == Phase.Morale && state.MoraleFirst is null && TurnRules.MoraleRemaining(state).Count == 2,
            "Morale order is set only when both warbands must check.");
        Require(Enum.IsDefined(e.First), "Unknown seat.");
        if (e.Chosen)
        {
            Require(state.Pending is { Kind: DecisionKind.ChooseMoraleOrder }, "No choice of Morale order is pending.");
        }
        else
        {
            Require(state.Pending is null && e.First == state.InitiativeHolder, "The default order puts the Initiative holder first.");
        }
        return state with { MoraleFirst = e.First, Pending = null };
    }

    private static MatchState OnMoraleCheckRolled(MatchState state, MoraleCheckRolled e)
    {
        Require(state.Phase == Phase.Morale && state.Pending is null, "A Morale Check cannot be rolled now.");
        var remaining = TurnRules.MoraleRemaining(state);
        Require(remaining.Contains(e.Seat), "That warband does not need a Morale Check.");
        Require(remaining.Count == 1 || state.MoraleFirst == e.Seat, "It is not that warband's turn to check.");
        Require(e.ModelsDownOrOut == TurnRules.DownOrOut(state, e.Seat)
            && e.WarbandSize == TurnRules.WarbandSize(state, e.Seat)
            && e.WasShaken == state.SeatFor(e.Seat).Shaken, "Counts do not match the state.");
        var seat = state.SeatFor(e.Seat) with
        {
            MoraleChecked = true,
            MoraleOutcome = e.Check.Outcome == Dice.SuccessOutcome.Failure ? Dice.SuccessOutcome.Failure : Dice.SuccessOutcome.Success,
        };
        return TurnRules.WithSeat(state, e.Seat, seat);
    }

    private static MatchState OnWarbandShaken(MatchState state, WarbandShaken e)
    {
        Require(state.Pending == new PendingDecision(DecisionKind.FailedMorale, e.Seat), "No failed-Morale choice is pending for that warband.");
        return TurnRules.WithSeat(state, e.Seat, state.SeatFor(e.Seat) with { Shaken = true }) with { Pending = null };
    }

    private static MatchState OnWarbandRecovered(MatchState state, WarbandRecovered e)
    {
        var s = state.SeatFor(e.Seat);
        Require(state.Phase == Phase.Morale && s.Shaken && s.MoraleChecked && s.MoraleOutcome == Dice.SuccessOutcome.Success,
            "Only a Shaken warband that passed its check recovers.");
        return TurnRules.WithSeat(state, e.Seat, s with { Shaken = false });
    }

    private static MatchState OnWarbandFled(MatchState state, WarbandFled e)
    {
        var s = state.SeatFor(e.Seat);
        Require(state.Phase == Phase.Morale && s.MoraleChecked && s.MoraleOutcome == Dice.SuccessOutcome.Failure && !s.Fled,
            "Only a warband that failed its check can flee.");
        switch (e.Reason)
        {
            case FledReason.FailedWhileShaken:
                Require(s.Shaken && state.Pending is null, "Only a Shaken warband flees without a choice.");
                break;
            case FledReason.SoundedRetreat:
                Require(!s.Shaken && state.Pending == new PendingDecision(DecisionKind.FailedMorale, e.Seat), "No retreat choice is pending.");
                break;
            default:
                throw new InvalidEventException("Unknown reason.");
        }
        return TurnRules.WithSeat(state, e.Seat, s with { Fled = true }) with { Pending = null };
    }

    private static MatchState OnTurnEnded(MatchState state, TurnEnded e)
    {
        Require(state.Phase == Phase.Morale && e.Turn == state.Turn && !state.TurnEnded && state.Pending is null
            && TurnRules.MoraleRemaining(state).Count == 0 && !state.One.Fled && !state.Two.Fled,
            "The turn cannot end yet.");
        return state with { TurnEnded = true };
    }

    private static MatchState OnMatchEnded(MatchState state, MatchEnded e)
    {
        switch (e.Reason)
        {
            case OutcomeReason.OpponentFled:
                Require(e.Winner is { } w && state.SeatFor(w.Opponent()).Fled, "A winner by fleeing needs a warband that fled.");
                break;
            case OutcomeReason.FinalTurnComplete:
                Require(e.Winner is null && state.Phase == Phase.Morale && state.TurnEnded && state.Turn == state.Setup!.TurnLimit,
                    "The final turn is not complete.");
                break;
            default:
                throw new InvalidEventException("Unknown outcome reason.");
        }
        return state with { Phase = Phase.Ended, Pending = null, Outcome = new MatchOutcome(e.Winner, e.Reason) };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidEventException(message);
        }
    }
}
