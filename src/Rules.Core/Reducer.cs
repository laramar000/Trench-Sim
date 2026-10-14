using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
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
            ModelStoodUp x => OnModelStoodUp(state, x),
            DashDeclared x => OnDashDeclared(state, x),
            DashRolled x => OnDashRolled(state, x),
            ModelMoved x => OnModelMoved(state, x),
            MarkersSpent x => OnMarkersSpent(state, x),
            RangedAttackDeclared x => OnRangedAttackDeclared(state, x),
            RangedSuccessRolled x => OnRangedSuccessRolled(state, x),
            InjuryRolled x => OnInjuryRolled(state, x),
            ObjectivesScored x => OnObjectivesScored(state, x),
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
            case DecisionKind.SpendMarkersOnSuccessRoll:
                var rolling = state.InProgress;
                Require(rolling is { Stage: RollStage.DashRoll or RollStage.ShotRoll, MarkersAsked: false }
                    && state.FindModel(rolling.Actor) is { BloodMarkers: > 0 } holder
                    && holder.Seat.Opponent() == e.Seat, "Only the rolling model's opponent may spend its markers, once, before a Success Roll.");
                state = state with { InProgress = rolling! with { MarkersAsked = true } };
                break;
            case DecisionKind.SpendMarkersOnInjuryRoll:
                var injuring = state.InProgress;
                Require(injuring is { Stage: RollStage.InjuryRoll, MarkersAsked: false, Target: { } victim }
                    && state.FindModel(injuring.Actor) is { } shooter && shooter.Seat == e.Seat
                    && state.FindModel(victim) is { BloodMarkers: > 0 }, "Only the attacker may spend the target's markers, once, before an Injury Roll.");
                state = state with { InProgress = injuring! with { MarkersAsked = true } };
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
        if (Board.ScenarioOf(state) is null)
        {
            Require(e.Position is null, "Without a battlefield a model has no position.");
        }
        else
        {
            Require(e.Position is { } at && Board.CheckPlacement(state, e.Seat, model!, at).IsLegal, "A model must be set up at a legal point in its zone.");
        }
        return TurnRules.WithModel(state, model! with { Deployed = true, Position = e.Position }) with { LastDeployer = e.Seat };
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
        return TurnRules.WithModel(state, model! with { ActivatedThisTurn = true }) with
        {
            ActiveModel = e.Model,
            LastActivator = e.Seat,
            Actions = ActionsTaken.None,
            MovementHalved = false,
        };
    }

    private static MatchState OnActivationEnded(MatchState state, ActivationEnded e)
    {
        Require(state.Phase == Phase.Activation && state.ActiveModel == e.Model, "That model is not the one activating.");
        Require(Enum.IsDefined(e.Reason), "Unknown end reason.");
        Require(state.InProgress is null && state.Pending is null, "An action is still being resolved.");
        return state with { ActiveModel = null, Actions = ActionsTaken.None, MovementHalved = false };
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
        Require(Board.ScenarioOf(state) is not { ObjectiveIds.Length: > 0 } || state.ScoredTurn == state.Turn, "The turn's Objectives have not been scored.");
        return state with { TurnEnded = true };
    }

    private static MatchState OnMatchEnded(MatchState state, MatchEnded e)
    {
        var scenario = Board.ScenarioOf(state);
        switch (e.Reason)
        {
            case OutcomeReason.OpponentFled:
                Require(e.Winner is { } w && state.SeatFor(w.Opponent()).Fled, "A winner by fleeing needs a warband that fled.");
                break;
            case OutcomeReason.FinalTurnComplete:
                Require(scenario is null && e.Winner is null && state.Phase == Phase.Morale && state.TurnEnded && state.Turn == state.Setup!.TurnLimit,
                    "The final turn is not complete.");
                break;
            case OutcomeReason.AllEnemiesOutOfAction:
                Require(scenario is not null && e.Winner is { } victor && state.Phase == Phase.Activation
                    && !HasModelOnBattlefield(state, victor.Opponent()) && HasModelOnBattlefield(state, victor),
                    "A winner by elimination needs a warband with no models left on the battlefield.");
                break;
            case OutcomeReason.VictoryPoints:
                Require(scenario is not null && state.Phase == Phase.Morale && state.TurnEnded && state.Turn == state.Setup!.TurnLimit
                    && !state.One.Fled && !state.Two.Fled && e.Winner == WinnerByPoints(state),
                    "The winner must be the warband with more Victory Points once the final turn is complete.");
                break;
            default:
                throw new InvalidEventException("Unknown outcome reason.");
        }
        return state with { Phase = Phase.Ended, Pending = null, InProgress = null, Outcome = new MatchOutcome(e.Winner, e.Reason) };
    }

    internal static bool HasModelOnBattlefield(MatchState state, Seat seat) =>
        state.Models.Any(m => m.Seat == seat && m.Deployed && m.Status != ModelStatus.OutOfAction);

    internal static Seat? WinnerByPoints(MatchState state) =>
        state.One.VictoryPoints == state.Two.VictoryPoints ? null : state.One.VictoryPoints > state.Two.VictoryPoints ? Seat.One : Seat.Two;

    // ---- M4: positions, movement, Ranged Attacks, Blood Markers, scoring ----

    private static ModelState Active(MatchState state, ModelId id, string what)
    {
        Require(Board.ScenarioOf(state) is not null, "This needs a battlefield.");
        Require(state.Phase == Phase.Activation && state.ActiveModel == id && state.Pending is null, $"{what} needs the active model and no pending decision.");
        var model = state.FindModel(id)!;
        Require(model.Status == ModelStatus.Standing && model.Position is not null, $"{what} needs a standing model on the table.");
        return model;
    }

    private static MatchState OnModelStoodUp(MatchState state, ModelStoodUp e)
    {
        Require(Board.ScenarioOf(state) is not null && state.Phase == Phase.Activation && state.ActiveModel == e.Model
            && state.Actions == ActionsTaken.None && !state.MovementHalved && state.InProgress is null, "A model stands up only at the start of its activation.");
        var model = state.FindModel(e.Model)!;
        Require(model.Status == ModelStatus.Down && model.Position is not null, "Only a Down model on the table stands up.");
        return TurnRules.WithModel(state, model with { Status = ModelStatus.Standing }) with { MovementHalved = true };
    }

    private static MatchState OnDashDeclared(MatchState state, DashDeclared e)
    {
        var model = Active(state, e.Model, "A Dash");
        Require(state.InProgress is null && !state.Actions.HasFlag(ActionsTaken.Dash), "A Dash can be taken once per activation.");
        Require(Board.CheckMove(state, model, e.Path).IsLegal, "A Dash must follow a legal path.");
        return state with
        {
            Actions = state.Actions | ActionsTaken.Dash,
            InProgress = new RollInProgress(RollStage.DashRoll, e.Model, DashPath: e.Path),
        };
    }

    private static MatchState OnDashRolled(MatchState state, DashRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.DashRoll } p && p.Actor == e.Model && state.Pending is null, "No Dash roll is waiting.");
        var progress = state.InProgress!;
        Require(e.Check.Risky && e.Check.NetDice == progress.BonusDice - progress.PenaltyDice, "A Dash is a Risky Success Roll with the spent markers as penalty dice.");
        return state with
        {
            InProgress = e.Check.Outcome == SuccessOutcome.Failure ? null : progress with { Stage = RollStage.DashMove },
        };
    }

    private static MatchState OnModelMoved(MatchState state, ModelMoved e)
    {
        var model = Active(state, e.Model, "A move");
        if (e.Kind == MoveKind.Move)
        {
            Require(state.InProgress is null && !state.Actions.HasFlag(ActionsTaken.Move), "A Move can be taken once per activation.");
        }
        else
        {
            Require(state.InProgress is { Stage: RollStage.DashMove } p && p.Actor == e.Model && p.DashPath.SequenceEqual(e.Path), "A Dash moves only after its roll succeeded, along the declared path.");
        }
        var check = Board.CheckMove(state, model, e.Path);
        Require(check.IsLegal && check.Cost == e.Cost, "The move must follow a legal path at the recorded cost.");
        return TurnRules.WithModel(state, model with { Position = e.Path[^1] }) with
        {
            Actions = e.Kind == MoveKind.Move ? state.Actions | ActionsTaken.Move : state.Actions,
            InProgress = null,
        };
    }

    private static MatchState OnMarkersSpent(MatchState state, MarkersSpent e)
    {
        Require(state.Pending is { } pending && pending.Seat == e.Spender && state.InProgress is { } progress, "No marker decision is pending for that seat.");
        var holder = state.FindModel(e.Model);
        Require(holder is not null && e.Count <= holder.BloodMarkers, "A model cannot spend more markers than it has.");
        var rolling = state.InProgress!;
        MatchState next;
        if (state.Pending!.Kind == DecisionKind.SpendMarkersOnSuccessRoll)
        {
            Require(e.Purpose == MarkerSpendPurpose.ReduceSuccessRoll && e.Model == rolling.Actor, "Those markers belong to the rolling model.");
            next = state with { InProgress = rolling with { PenaltyDice = rolling.PenaltyDice + e.Count } };
        }
        else if (state.Pending.Kind == DecisionKind.SpendMarkersOnInjuryRoll)
        {
            Require(e.Purpose == MarkerSpendPurpose.RaiseInjuryRoll && e.Model == rolling.Target, "Those markers belong to the target.");
            next = state with { InProgress = rolling with { BonusDice = rolling.BonusDice + e.Count } };
        }
        else
        {
            throw new InvalidEventException("No marker decision is pending.");
        }
        return TurnRules.WithModel(next, holder! with { BloodMarkers = holder.BloodMarkers - e.Count }) with { Pending = null };
    }

    private static MatchState OnRangedAttackDeclared(MatchState state, RangedAttackDeclared e)
    {
        var attacker = Active(state, e.Attacker, "A Ranged Attack");
        Require(state.InProgress is null && !state.Actions.HasFlag(ActionsTaken.Shoot), "A Shoot ACTION can be taken once per activation.");
        var target = state.FindModel(e.Target);
        Require(target is not null, "Unknown target.");
        var facts = Board.Assess(state, attacker, target!);
        Require(facts.IsLegal, "The attack is not legal here.");
        Require(facts.LongRange == e.LongRange && facts.InCover == e.InCover && facts.CoverTerrainId == e.CoverTerrainId
            && facts.AttackerLevel == e.AttackerLevel && facts.TargetLevel == e.TargetLevel, "The declared modifiers do not match the positions.");
        return state with
        {
            Actions = state.Actions | ActionsTaken.Shoot,
            InProgress = new RollInProgress(RollStage.ShotRoll, e.Attacker, e.Target, 0, e.PenaltyDice),
        };
    }

    private static MatchState OnRangedSuccessRolled(MatchState state, RangedSuccessRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ShotRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No attack roll is waiting.");
        var progress = state.InProgress!;
        var attacker = state.FindModel(e.Attacker)!;
        Require(e.Check.Risky == state.IsShaken(attacker.Seat) && e.Check.NetDice == progress.BonusDice - progress.PenaltyDice,
            "The roll must carry the declared and spent dice, and be Risky exactly when the warband is Shaken.");
        if (e.Check.Outcome == SuccessOutcome.Failure)
        {
            return state with { InProgress = null };
        }
        // Critical Success: +1 INJURY DICE on the Injury Roll (confirmed ruling; the print on pp. 44-45 says "+1 DICE").
        var critical = e.Check.Outcome == SuccessOutcome.CriticalSuccess;
        return state with
        {
            InProgress = progress with { Stage = RollStage.InjuryRoll, BonusDice = critical ? 1 : 0, PenaltyDice = 0, Critical = critical, MarkersAsked = false },
        };
    }

    private static MatchState OnInjuryRolled(MatchState state, InjuryRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.InjuryRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No Injury Roll is waiting.");
        var progress = state.InProgress!;
        var target = state.FindModel(e.Target)!;
        Require(target.Seat != state.FindModel(e.Attacker)!.Seat && target.Id != state.ActiveModel, "An attack can only injure an enemy model.");
        var weapon = state.Setup!.ProfileOf(e.Attacker)!.RangedInjuryModifier;
        var armour = state.Setup.ProfileOf(e.Target)!.Armour;
        Require(e.Injury.NetDice == progress.BonusDice - progress.PenaltyDice
            && e.Injury.Modifier == InjuryRoll.CombineModifiers([weapon, armour])
            && e.Injury.TargetAlreadyDown == (target.Status == ModelStatus.Down), "The Injury Roll must match the dice, modifiers and state of the target.");

        var markers = Math.Min(BloodMarkers.Max, target.BloodMarkers + e.Injury.BloodMarkers);
        var hurt = e.Injury.Outcome switch
        {
            InjuryOutcome.OutOfAction => target with { Status = ModelStatus.OutOfAction, Position = null, BloodMarkers = markers },
            InjuryOutcome.Down => target with { Status = ModelStatus.Down, BloodMarkers = markers },
            _ => target with { BloodMarkers = markers },
        };
        return TurnRules.WithModel(state, hurt) with { InProgress = null };
    }

    private static MatchState OnObjectivesScored(MatchState state, ObjectivesScored e)
    {
        var scenario = Board.ScenarioOf(state);
        Require(scenario is { ObjectiveIds.Length: > 0 } && state.Phase == Phase.Morale && !state.TurnEnded && state.Pending is null
            && TurnRules.MoraleRemaining(state).Count == 0 && e.Turn == state.Turn && state.ScoredTurn < state.Turn, "Objectives are scored once, at the end of a turn.");
        var expected = scenario!.ObjectiveIds.Select(id => Board.Controller(state, id))
            .Zip(scenario.ObjectiveIds, (seat, id) => new ObjectiveResult(id, seat, seat is null ? 0 : Scoring.PointsPerObjective));
        Require(expected.SequenceEqual(e.Results), "The scores must match who controls each Objective.");
        var one = state.One.VictoryPoints + e.Results.Where(r => r.Controller == Seat.One).Sum(r => r.Points);
        var two = state.Two.VictoryPoints + e.Results.Where(r => r.Controller == Seat.Two).Sum(r => r.Points);
        return state with
        {
            One = state.One with { VictoryPoints = one },
            Two = state.Two with { VictoryPoints = two },
            ScoredTurn = state.Turn,
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidEventException(message);
        }
    }
}
