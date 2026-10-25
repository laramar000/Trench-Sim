using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
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
        RequireFollowUp(state, e);

        return e switch
        {
            RollOffResolved x => OnRollOffResolved(state, x),
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
            SideClimbDeclared x => OnSideClimbDeclared(state, x),
            SideClimbRolled x => OnSideClimbRolled(state, x),
            ModelMoved x => OnModelMoved(state, x),
            MarkersSpent x => OnMarkersSpent(state, x),
            RangedAttackDeclared x => OnRangedAttackDeclared(state, x),
            RangedSuccessRolled x => OnRangedSuccessRolled(state, x),
            InjuryRolled x => OnInjuryRolled(state, x),
            ObjectivesScored x => OnObjectivesScored(state, x),
            ChargeDeclared x => OnChargeDeclared(state, x),
            ChargeRolled x => OnChargeRolled(state, x),
            ChargeMoved x => OnChargeMoved(state, x),
            RetreatDeclared x => OnRetreatDeclared(state, x),
            FreeFightsDeclined x => OnFreeFightsDeclined(state, x),
            RetreatResolved x => OnRetreatResolved(state, x),
            MeleeAttackDeclared x => OnMeleeAttackDeclared(state, x),
            MeleeSuccessRolled x => OnMeleeSuccessRolled(state, x),
            ShootingTargetRolled x => OnShootingTargetRolled(state, x),
            FriendlyTargetChosen x => OnFriendlyTargetChosen(state, x),
            AttackerChosen x => OnAttackerChosen(state, x),
            TrenchesScored x => OnTrenchesScored(state, x),
            GameLengthRolled x => OnGameLengthRolled(state, x),
            MinesLaid x => OnMinesLaid(state, x),
            MineRevealed x => OnMineRevealed(state, x),
            MineExploded x => OnMineExploded(state, x),
            MoveResumed x => OnMoveResumed(state, x),
            _ => throw new InvalidEventException($"Unknown event type {e.GetType().Name}."),
        };
    }

    // Some states leave exactly one event that can come next, because the rules give nobody a choice: the match is
    // over, a roll-off has to be used, a Risky roll failed, a Down model stands, a Morale result takes effect, or the
    // opponent must be asked about Blood Markers. Decide always emits that event; a log that does anything else was
    // edited. Everything here is worked out from the state, except the two facts it cannot show (MustEndActivation,
    // SeatState.MustFlee) and the roll-off result.
    private static void RequireFollowUp(MatchState state, GameEvent e)
    {
        if (TerminalReason(state) is { } reason)
        {
            Require(e is MatchEnded ended && ended.Reason == reason, $"The match is over ({reason}); only the end of the match can follow.");
            return;
        }

        if (state.RollOff is { } rollOff)
        {
            var uses = rollOff.Purpose switch
            {
                RollOffPurpose.DeploymentZone => e is PhaseEntered { Phase: Phase.Deployment }
                    || e is DecisionRequested { Kind: DecisionKind.ChooseDeploymentZone },
                RollOffPurpose.DeploymentOrder => e is DeploymentOrderSet,
                RollOffPurpose.Roles => e is PhaseEntered { Phase: Phase.Deployment }
                    || e is DecisionRequested { Kind: DecisionKind.ChooseAttacker },
                _ => e is InitiativeDetermined,
            };
            Require(uses, $"The {rollOff.Purpose} roll-off has to be used before anything else happens.");
            return;
        }

        if (LengthRollOwed(state))
        {
            Require(e is GameLengthRolled, "At the end of Turn 4 the attacker rolls for the game's length.");
            return;
        }

        if (state.MineStop is { } stop)
        {
            // A halted move: the marker is revealed, a live one detonates, then the model carries on (p. 162).
            var next = !stop.Revealed ? e is MineRevealed : stop.Live ? e is MineExploded : e is MoveResumed;
            Require(next, "A move halted at a Mine Marker: the marker is revealed, a live mine detonates, and the model carries on.");
            return;
        }

        if (state.MustEndActivation)
        {
            Require(e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure } lost && lost.Model == state.ActiveModel,
                "A failed Risky roll ends the activation at once.");
            return;
        }

        foreach (var seat in new[] { Seat.One, Seat.Two })
        {
            var s = state.SeatFor(seat);
            if (s.MustFlee)
            {
                Require(e is WarbandFled { Reason: FledReason.FailedWhileShaken } fled && fled.Seat == seat, "A Shaken warband that fails its Morale Check flees.");
                return;
            }
            if (s.MoraleChecked && s.MoraleOutcome == SuccessOutcome.Success && s.Shaken)
            {
                Require(e is WarbandRecovered recovered && recovered.Seat == seat, "A Shaken warband that passes its Morale Check recovers.");
                return;
            }
            if (s.MoraleChecked && s.MoraleOutcome == SuccessOutcome.Failure && !s.Shaken && !s.Fled && state.Pending is null)
            {
                Require(e is DecisionRequested { Kind: DecisionKind.FailedMorale } asked && asked.Seat == seat,
                    "A warband that fails its Morale Check chooses at once between becoming Shaken and Sounding the Retreat.");
                return;
            }
        }

        if (state.Phase == Phase.Activation && state.ActiveModel is { } active && state.Pending is null && state.InProgress is null
            && state.Retreat is null && state.MineStop is null && Board.ScenarioOf(state) is not null && state.FindModel(active) is { } model && model.Status != ModelStatus.Standing)
        {
            if (state.Actions == ActionsTaken.None && !state.MovementHalved)
            {
                // U30: a Down model stands up as soon as its activation starts.
                Require(e is ModelStoodUp up && up.Model == active, "A Down model stands up as soon as it activates.");
            }
            else
            {
                // Taken Down or Out of Action by a free attack during its own Retreat: the activation ends (p. 49).
                Require(e is ActivationEnded { Reason: ActivationEndReason.TakenDown } down && down.Model == active,
                    "A model taken Down during its own activation ends it.");
            }
            return;
        }

        if (MarkerOfferOwed(state) is { } owed)
        {
            Require(e is DecisionRequested offer && offer.Kind == owed.Kind && offer.Seat == owed.Seat,
                "The opponent must be asked about Blood Markers before this roll.");
        }
    }

    // The match is over once a warband has fled, a warband has no model left on the battlefield, or the final turn
    // has ended. MatchEnded with this reason is then the only event that can follow.
    internal static OutcomeReason? TerminalReason(MatchState state)
    {
        if (state.Phase is Phase.None or Phase.PreGame or Phase.Ended)
        {
            return null;
        }
        if (state.One.Fled || state.Two.Fled)
        {
            return OutcomeReason.OpponentFled;
        }
        var scenario = Board.ScenarioOf(state);
        if (scenario is not null && state.Phase == Phase.Activation
            && (!HasModelOnBattlefield(state, Seat.One) || !HasModelOnBattlefield(state, Seat.Two)))
        {
            return OutcomeReason.AllEnemiesOutOfAction;
        }
        if (state.Phase == Phase.Morale && state.TurnEnded && state.Turn >= state.FinalTurn && !LengthRollOwed(state))
        {
            return scenario is null ? OutcomeReason.FinalTurnComplete : OutcomeReason.VictoryPoints;
        }
        return null;
    }

    // Decide asks the opponent about Blood Markers whenever the model whose markers count has any and nobody has been
    // asked yet for this roll (pp. 27-28): before a Success Roll the rolling model's opponent, before an Injury Roll
    // the attacker (never on a friendly-fire injury, U52).
    private static PendingDecision? MarkerOfferOwed(MatchState state)
    {
        if (state.Pending is not null || state.InProgress is not { MarkersAsked: false } progress || state.FindModel(progress.Actor) is not { } actor)
        {
            return null;
        }
        if (progress.Stage is RollStage.DashRoll or RollStage.ShotRoll or RollStage.FightRoll or RollStage.ClimbRoll && actor.BloodMarkers > 0)
        {
            return new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, actor.Seat.Opponent());
        }
        if (progress.Stage == RollStage.InjuryRoll && !progress.FriendlyFire && progress.Target is { } target
            && state.FindModel(target) is { BloodMarkers: > 0 })
        {
            return new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, actor.Seat);
        }
        return null;
    }

    // Each round is checked again here and the winner is read from the last round's dice, so a log cannot name a
    // different winner than its dice show. The roll-off must be one the sequence calls for at this point.
    private static MatchState OnRollOffResolved(MatchState state, RollOffResolved e)
    {
        var rounds = e.Rounds;
        Require(!rounds.IsDefaultOrEmpty && rounds.All(r => r is not null && r.SeatOne is >= 1 and <= 6 && r.SeatTwo is >= 1 and <= 6),
            "A roll-off has at least one round of two D6.");
        Require(rounds.Take(rounds.Length - 1).All(r => r.IsTie) && !rounds[^1].IsTie, "Every round but the last is a tie, and the last is not.");
        var winner = rounds[^1].SeatOne > rounds[^1].SeatTwo ? Seat.One : Seat.Two;
        Require(e.Winner == winner, "The winner is the seat with the higher die in the last round.");
        switch (e.Purpose)
        {
            case RollOffPurpose.DeploymentZone:
                Require(state.Phase == Phase.PreGame && state.Pending is null && !TrenchWarfare.Is(state.Setup), "The deployment-zone roll-off comes first, before Deployment.");
                break;
            case RollOffPurpose.Roles:
                Require(state.Phase == Phase.PreGame && state.Pending is null && TrenchWarfare.Is(state.Setup) && state.Attacker is null,
                    "In Trench Warfare the roles roll-off comes first, before Deployment.");
                break;
            case RollOffPurpose.DeploymentOrder:
                Require(state.Phase == Phase.Deployment && state.Pending is null && state.One.ZoneId is not null && state.DeploymentFirst is null
                    && TurnRules.WarbandSize(state, Seat.One) == TurnRules.WarbandSize(state, Seat.Two),
                    "The deployment-order roll-off happens once, after the zones are chosen, and only between equal warbands.");
                break;
            case RollOffPurpose.Initiative:
                Require(state.Phase == Phase.Initiative && state.Pending is null && state.InitiativeHolder is null
                    && TurnRules.InitiativeCount(state, Seat.One) == TurnRules.InitiativeCount(state, Seat.Two),
                    "The Initiative roll-off happens once per turn, and only when both sides count the same models.");
                break;
            default:
                throw new InvalidEventException("Unknown roll-off purpose.");
        }
        return state with { RollOff = new RollOffResult(e.Purpose, winner) };
    }

    private static MatchState OnDecisionRequested(MatchState state, DecisionRequested e)
    {
        Require(state.Pending is null, "A decision is already pending.");
        switch (e.Kind)
        {
            case DecisionKind.ChooseDeploymentZone:
                Require(state.Phase == Phase.Deployment && state.One.ZoneId is null, "Zones are chosen once, at the start of Deployment.");
                Require(state.RollOff == new RollOffResult(RollOffPurpose.DeploymentZone, e.Seat), "The winner of the deployment-zone roll-off chooses the zone.");
                state = state with { RollOff = null };
                break;
            case DecisionKind.ChooseAttacker:
                Require(state.Phase == Phase.Deployment && state.Attacker is null && state.One.ZoneId is null, "The roles are chosen once, at the start of Deployment.");
                Require(state.RollOff == new RollOffResult(RollOffPurpose.Roles, e.Seat), "The winner of the roles roll-off chooses who attacks.");
                state = state with { RollOff = null };
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
                Require(rolling is { Stage: RollStage.DashRoll or RollStage.ShotRoll or RollStage.FightRoll or RollStage.ClimbRoll, MarkersAsked: false }
                    && state.FindModel(rolling.Actor) is { BloodMarkers: > 0 } holder
                    && holder.Seat.Opponent() == e.Seat, "Only the rolling model's opponent may spend its markers, once, before a Success Roll.");
                state = state with { InProgress = rolling! with { MarkersAsked = true } };
                break;
            case DecisionKind.SpendMarkersOnInjuryRoll:
                var injuring = state.InProgress;
                // U52: nobody spends a friendly model's markers on a forced shot's Injury Roll.
                Require(injuring is { Stage: RollStage.InjuryRoll, MarkersAsked: false, FriendlyFire: false, Target: { } victim }
                    && state.FindModel(injuring.Actor) is { } shooter && shooter.Seat == e.Seat
                    && state.FindModel(victim) is { BloodMarkers: > 0 }, "Only the attacker may spend the target's markers, once, before an Injury Roll.");
                state = state with { InProgress = injuring! with { MarkersAsked = true } };
                break;
            case DecisionKind.LayMines:
                Require(TrenchWarfare.Is(state.Setup) && state.Phase == Phase.Deployment && state.Attacker is { } attacking && attacking.Opponent() == e.Seat
                    && !state.MinesLaid && state.DeploymentFirst is null, "The defender sets up the Mine Markers once, after the roles are chosen and before deployment.");
                break;
            case DecisionKind.FreeFight:
                Require(state.Retreat is { Closed: false } retreat && state.InProgress is null && state.Phase == Phase.Activation
                    && state.FindModel(retreat.Model) is { } retreater && retreater.Seat.Opponent() == e.Seat
                    && Board.FreeFighters(state, retreat).Count > 0, "Free attacks are offered to the opponent of a retreating model while someone can still attack.");
                break;
            case DecisionKind.ChooseFriendlyTarget:
                Require(state.InProgress is { Stage: RollStage.TargetChoice } choosing
                    && state.FindModel(choosing.Actor) is { } chooser && chooser.Seat == e.Seat, "Only the shooter chooses which of its own models is shot.");
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
        // Trench Warfare: the attacker deploys first (p. 162).
        if (state.Attacker is { } attacker)
        {
            Require(state.RollOff is null && e.First == attacker, "In Trench Warfare the attacker deploys first.");
            Require(state.MinesLaid, "In Trench Warfare the defender sets up the Mine Markers before deployment.");
            return state with { DeploymentFirst = e.First };
        }
        // The warband with more models deploys first; equal warbands roll off and the winner starts (p. 152, R7).
        var one = TurnRules.WarbandSize(state, Seat.One);
        var two = TurnRules.WarbandSize(state, Seat.Two);
        if (one != two)
        {
            Require(state.RollOff is null && e.First == (one > two ? Seat.One : Seat.Two), "The warband with more models deploys first.");
        }
        else
        {
            Require(state.RollOff == new RollOffResult(RollOffPurpose.DeploymentOrder, e.First), "Equal warbands roll off, and the winner deploys first.");
        }
        return state with { DeploymentFirst = e.First, RollOff = null };
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
                Require(state.RollOff is { Purpose: RollOffPurpose.DeploymentZone or RollOffPurpose.Roles }, "Deployment starts after the deployment-zone (or roles) roll-off.");
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
        Require(e.Turn <= state.FinalTurn, "The match has no more turns.");
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
        if (one != two)
        {
            Require(state.RollOff is null && e.Holder == (one < two ? Seat.One : Seat.Two), "The seat with fewer models holds the Initiative.");
        }
        else
        {
            Require(state.RollOff == new RollOffResult(RollOffPurpose.Initiative, e.Holder), "On equal counts the Initiative roll-off decides who holds it.");
        }
        return state with { InitiativeHolder = e.Holder, RollOff = null };
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
        Require(state.InProgress is null && state.Pending is null && state.Retreat is null && state.MineStop is null, "An action is still being resolved.");
        if (e.Reason == ActivationEndReason.TakenDown)
        {
            Require(state.FindModel(e.Model)!.Status != ModelStatus.Standing, "An activation ends as taken Down only when the model is Down or Out of Action.");
        }
        if (e.Reason == ActivationEndReason.RiskyFailure)
        {
            Require(state.MustEndActivation, "An activation ends for a failed Risky roll only after one.");
        }
        return state with { ActiveModel = null, Actions = ActionsTaken.None, MovementHalved = false, MustEndActivation = false };
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
        var failed = e.Check.Outcome == Dice.SuccessOutcome.Failure;
        var seat = state.SeatFor(e.Seat) with
        {
            MoraleChecked = true,
            MoraleOutcome = failed ? Dice.SuccessOutcome.Failure : Dice.SuccessOutcome.Success,
            MustFlee = failed && e.WasShaken,
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
                Require(s.Shaken && s.MustFlee && state.Pending is null, "Only a warband that failed while already Shaken flees without a choice.");
                break;
            case FledReason.SoundedRetreat:
                Require(!s.Shaken && state.Pending == new PendingDecision(DecisionKind.FailedMorale, e.Seat), "No retreat choice is pending.");
                break;
            default:
                throw new InvalidEventException("Unknown reason.");
        }
        return TurnRules.WithSeat(state, e.Seat, s with { Fled = true, MustFlee = false }) with { Pending = null };
    }

    private static MatchState OnTurnEnded(MatchState state, TurnEnded e)
    {
        Require(state.Phase == Phase.Morale && e.Turn == state.Turn && !state.TurnEnded && state.Pending is null
            && TurnRules.MoraleRemaining(state).Count == 0 && !state.One.Fled && !state.Two.Fled,
            "The turn cannot end yet.");
        Require(Board.ScenarioOf(state) is not { ObjectiveIds.Length: > 0 } || state.ScoredTurn == state.Turn, "The turn's Objectives have not been scored.");
        Require(!TrenchWarfare.Is(state.Setup) || state.ScoredTurn == state.Turn, "The turn's trench sections have not been scored.");
        return state with { TurnEnded = true };
    }

    private static MatchState OnMatchEnded(MatchState state, MatchEnded e)
    {
        Require(TerminalReason(state) == e.Reason, "The match ends only when that outcome has actually happened.");
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
                Require(scenario is not null && state.Phase == Phase.Morale && state.TurnEnded && state.Turn == state.FinalTurn
                    && !state.One.Fled && !state.Two.Fled && e.Winner == WinnerByPoints(state),
                    "The winner must be the warband with more Victory Points once the final turn is complete.");
                break;
            default:
                throw new InvalidEventException("Unknown outcome reason.");
        }
        return state with { Phase = Phase.Ended, Pending = null, InProgress = null, Retreat = null, Outcome = new MatchOutcome(e.Winner, e.Reason) };
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
            MustEndActivation = e.Check.Outcome == SuccessOutcome.Failure,
        };
    }

    // Towers stage T6 (p. 39, U75, U76): a Move or Dash that climbs a tower's side. A Move is taken here; a Dash has
    // already succeeded its own roll and reached its move.
    private static MatchState OnSideClimbDeclared(MatchState state, SideClimbDeclared e)
    {
        var model = Active(state, e.Model, "A climb");
        Require(Enum.IsDefined(e.Kind), "Unknown move kind.");
        if (e.Kind == MoveKind.Move)
        {
            Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0,
                "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.");
        }
        else
        {
            Require(state.InProgress is { Stage: RollStage.DashMove } p && p.Actor == e.Model && p.DashPath.SequenceEqual(e.Path),
                "A Dash climbs only after its own roll succeeded, along the declared path.");
        }
        Require(Board.CheckMove(state, model, e.Path) is { IsLegal: true, SideClimb: not null }, "A climb must be part of a legal move that climbs a tower's side.");
        var action = e.Kind == MoveKind.Move ? ActionsTaken.Move : ActionsTaken.Dash;
        return state with
        {
            Actions = state.Actions | action,
            InProgress = new RollInProgress(RollStage.ClimbRoll, e.Model, DashPath: e.Path, ClimbAction: action),
        };
    }

    private static MatchState OnSideClimbRolled(MatchState state, SideClimbRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ClimbRoll } p && p.Actor == e.Model && state.Pending is null, "No climb roll is waiting.");
        var progress = state.InProgress!;
        Require(e.Check.Risky && e.Check.NetDice == progress.BonusDice - progress.PenaltyDice, "A climb is a Risky Success Roll with the spent markers as penalty dice.");
        if (e.Check.Outcome != SuccessOutcome.Failure)
        {
            return state with { InProgress = progress with { Stage = RollStage.ClimbMove } };
        }
        // Failed: the model moves only as far as the climb's start, if it is not there already, and then stops.
        var stops = ClimbStopPath(state, progress).Length > 0;
        return state with
        {
            InProgress = stops ? progress with { Stage = RollStage.ClimbStop } : null,
            MustEndActivation = !stops,
        };
    }

    // The part of a climbing move before the climb: where a failed climb leaves the model (may be empty).
    internal static ImmutableArray<Point2> ClimbStopPath(MatchState state, RollInProgress progress)
    {
        var model = state.FindModel(progress.Actor)!;
        var at = Board.CheckMove(state, model, progress.DashPath).SideClimb!.Value;
        return progress.DashPath.Take(at).ToImmutableArray();
    }

    private static MatchState OnModelMoved(MatchState state, ModelMoved e)
    {
        var model = Active(state, e.Model, "A move");
        if (state.InProgress is { Stage: RollStage.ClimbMove or RollStage.ClimbStop } climb && climb.Actor == e.Model)
        {
            var expected = climb.Stage == RollStage.ClimbMove ? climb.DashPath : ClimbStopPath(state, climb);
            Require(e.Kind == (climb.ClimbAction == ActionsTaken.Move ? MoveKind.Move : MoveKind.Dash) && expected.SequenceEqual(e.Path),
                "A climbing move follows its roll: the whole path on a Success, only up to the climb on a Failure.");
            var climbed = Board.CheckMove(state, model, e.Path);
            Require(climbed.IsLegal && climbed.Cost == e.Cost, "The move must follow a legal path at the recorded cost.");
            var climbedTo = TurnRules.WithModel(state, model with { Position = e.Path[^1] }) with
            {
                InProgress = null,
                MustEndActivation = climb.Stage == RollStage.ClimbStop,
            };
            return HaltAtMine(state, climbedTo, e.Model, e.Path);
        }
        if (e.Kind == MoveKind.Move)
        {
            Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0,
                "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.");
        }
        else
        {
            Require(state.InProgress is { Stage: RollStage.DashMove } p && p.Actor == e.Model && p.DashPath.SequenceEqual(e.Path), "A Dash moves only after its roll succeeded, along the declared path.");
        }
        var check = Board.CheckMove(state, model, e.Path);
        Require(check.IsLegal && check.Cost == e.Cost, "The move must follow a legal path at the recorded cost.");
        Require(check.SideClimb is null, "A move that climbs a tower's side needs its Risky Success Roll first.");
        var after = TurnRules.WithModel(state, model with { Position = e.Path[^1] }) with
        {
            Actions = e.Kind == MoveKind.Move ? state.Actions | ActionsTaken.Move : state.Actions,
            InProgress = null,
        };
        return HaltAtMine(state, after, e.Model, e.Path);
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
        Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Shoot | ActionsTaken.Charge | ActionsTaken.Fight)) == 0,
            "A Shoot ACTION can be taken once per activation, and not with a Charge or Fight ACTION.");
        var target = state.FindModel(e.Target);
        Require(target is not null, "Unknown target.");
        var facts = Board.Assess(state, attacker, target!);
        Require(facts.IsLegal, "The attack is not legal here.");
        Require(facts.LongRange == e.LongRange && facts.InCover == e.InCover && facts.CoverTerrainId == e.CoverTerrainId
            && facts.AttackerLevel == e.AttackerLevel && facts.TargetLevel == e.TargetLevel && facts.Elevated == e.Elevated, "The declared modifiers do not match the positions.");
        Require(facts.IntoMelee == e.IntoMelee, "A target within 1\" of a friendly model needs the D6 that decides who is shot, and no other does.");
        return state with
        {
            Actions = state.Actions | ActionsTaken.Shoot,
            InProgress = new RollInProgress(e.IntoMelee ? RollStage.TargetRoll : RollStage.ShotRoll, e.Attacker, e.Target, e.BonusDice, e.PenaltyDice),
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
            return state with { InProgress = null, MustEndActivation = e.Check.RiskyFailure };
        }
        // Critical Success: +1 INJURY DICE on the Injury Roll (confirmed ruling; the print on pp. 44-45 says "+1 DICE").
        var critical = e.Check.Outcome == SuccessOutcome.CriticalSuccess;
        return state with
        {
            InProgress = progress with { Stage = RollStage.InjuryRoll, BonusDice = AttackDice.InjuryBonus(critical, meleeOnDownTarget: false), PenaltyDice = 0, Critical = critical, MarkersAsked = false },
        };
    }

    private static MatchState OnInjuryRolled(MatchState state, InjuryRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.InjuryRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No Injury Roll is waiting.");
        var progress = state.InProgress!;
        var attacker = state.FindModel(e.Attacker)!;
        var target = state.FindModel(e.Target)!;
        Require(target.Id != attacker.Id && (target.Seat == attacker.Seat) == progress.FriendlyFire,
            "An attack injures an enemy model, unless a shot into melee was forced onto a friendly one.");
        var profile = state.Setup!.ProfileOf(e.Attacker)!;
        var weapon = progress.Kind == AttackKind.Melee ? profile.MeleeAttackInjuryModifier : profile.RangedInjuryModifier;
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

    // ---- M4 Slice B: Charge, Retreat, Fight, shooting into melee ----

    private static MatchState OnChargeDeclared(MatchState state, ChargeDeclared e)
    {
        var charger = Active(state, e.Model, "A Charge");
        Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat | ActionsTaken.Shoot)) == 0,
            "A Charge is one of Move, Charge or Retreat, and cannot follow a Shoot ACTION.");
        var target = state.FindModel(e.Target);
        Require(target is not null && Board.AssessCharge(state, charger, target) is null, "The charge is not legal here.");
        return state with { Actions = state.Actions | ActionsTaken.Charge, InProgress = new RollInProgress(RollStage.ChargeRoll, e.Model, e.Target) };
    }

    private static MatchState OnChargeRolled(MatchState state, ChargeRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ChargeRoll } p && p.Actor == e.Model && p.Target == e.Target && state.Pending is null, "No Charge Bonus is waiting.");
        return state with { InProgress = state.InProgress! with { Stage = RollStage.ChargeMove, ChargeDie = e.Die } };
    }

    private static MatchState OnChargeMoved(MatchState state, ChargeMoved e)
    {
        var charger = Active(state, e.Model, "A charge move");
        Require(state.InProgress is { Stage: RollStage.ChargeMove } p && p.Actor == e.Model && p.Target == e.Target, "No charge move is waiting.");
        var progress = state.InProgress!;
        var plan = Board.PlanCharge(state, charger, state.FindModel(e.Target)!, progress.ChargeDie);
        Require(plan.Path.SequenceEqual(e.Path) && plan.Cost == e.Cost && plan.Reached == e.Reached, "The charge move must be the one the rules plan from the Charge Bonus.");
        var moved = e.Path.IsEmpty ? charger : charger with { Position = e.Path[^1] };
        return HaltAtMine(state, TurnRules.WithModel(state, moved) with { InProgress = null }, e.Model, e.Path);
    }

    private static MatchState OnRetreatDeclared(MatchState state, RetreatDeclared e)
    {
        var retreater = Active(state, e.Model, "A Retreat");
        Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0,
            "A Retreat is one of Move, Charge or Retreat.");
        Require(Board.CheckRetreat(state, retreater, e.Path) is null, "A retreat must follow a legal path and end more than 1\" from every enemy.");
        return state with { Actions = state.Actions | ActionsTaken.Retreat, Retreat = new RetreatInProgress(e.Model, e.Path, []) };
    }

    private static MatchState OnFreeFightsDeclined(MatchState state, FreeFightsDeclined e)
    {
        Require(state.Pending is { Kind: DecisionKind.FreeFight } p && p.Seat == e.Seat && state.Retreat is { Closed: false }, "No free attack is on offer to that seat.");
        return state with { Pending = null, Retreat = state.Retreat! with { Closed = true } };
    }

    private static MatchState OnRetreatResolved(MatchState state, RetreatResolved e)
    {
        var current = state.Retreat;
        Require(state.Phase == Phase.Activation && state.ActiveModel == e.Model && state.Pending is null && state.InProgress is null
            && current is not null && current.Model == e.Model, "No retreat is waiting to be finished.");
        var retreat = current!;
        var retreater = state.FindModel(e.Model)!;
        Require(retreat.Closed || Board.FreeFighters(state, retreat).Count == 0, "The opponent can still make free attacks.");
        if (e.Moved)
        {
            Require(retreater.Status == ModelStatus.Standing && e.Path.SequenceEqual(retreat.Path) && Board.CheckRetreat(state, retreater, e.Path) is null
                && Board.RetreatCost(state, retreater, e.Path) == e.Cost, "A standing model finishes its retreat along the declared path at the recorded cost.");
            retreater = retreater with { Position = e.Path[^1] };
            return HaltAtMine(state, TurnRules.WithModel(state, retreater) with { Retreat = null }, e.Model, e.Path);
        }
        Require(retreater.Status != ModelStatus.Standing, "Only a model taken Down or Out of Action by a free attack stays where it is.");
        return TurnRules.WithModel(state, retreater) with { Retreat = null };
    }

    private static MatchState OnMeleeAttackDeclared(MatchState state, MeleeAttackDeclared e)
    {
        var attacker = state.FindModel(e.Attacker);
        var target = state.FindModel(e.Target);
        Require(attacker is not null && target is not null, "Unknown model.");
        if (!e.FreeFight)
        {
            var fighter = Active(state, e.Attacker, "A Fight");
            Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Fight | ActionsTaken.Shoot)) == 0,
                "A Fight ACTION is taken once per activation and not after a Shoot ACTION.");
            var facts = Board.AssessMelee(state, fighter, target!);
            Require(facts.Refusal is null && facts.CoverTerrainId == e.DefendedObstacleId, "The Melee Attack is not legal here, or its modifiers do not match the positions.");
            return state with
            {
                Actions = state.Actions | ActionsTaken.Fight,
                InProgress = new RollInProgress(RollStage.FightRoll, e.Attacker, e.Target, 0, e.PenaltyDice, Kind: AttackKind.Melee),
            };
        }

        var retreat = state.Retreat;
        Require(state.Phase == Phase.Activation && retreat is { Closed: false } && state.InProgress is null && retreat.Model == e.Target
            && state.Pending == new PendingDecision(DecisionKind.FreeFight, target!.Seat.Opponent()), "A free attack answers the offer made to the opponent of a retreating model.");
        Require(Board.FreeFighters(state, retreat!).Any(m => m.Id == e.Attacker), "That model cannot make a free attack.");
        var free = Board.AssessMelee(state, attacker!, target!);
        Require(free.Refusal is null && free.CoverTerrainId == e.DefendedObstacleId, "The free attack is not legal here, or its modifiers do not match the positions.");
        return state with
        {
            Pending = null,
            Retreat = retreat! with { Fought = retreat!.Fought.Add(e.Attacker) },
            InProgress = new RollInProgress(RollStage.FightRoll, e.Attacker, e.Target, 0, e.PenaltyDice, Kind: AttackKind.Melee, FreeFight: true),
        };
    }

    private static MatchState OnMeleeSuccessRolled(MatchState state, MeleeSuccessRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.FightRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No Melee Attack roll is waiting.");
        var progress = state.InProgress!;
        var attacker = state.FindModel(e.Attacker)!;
        Require(e.Check.Risky == state.IsShaken(attacker.Seat) && e.Check.NetDice == progress.BonusDice - progress.PenaltyDice,
            "The roll must carry the declared and spent dice, and be Risky exactly when the warband is Shaken.");
        if (e.Check.Outcome == SuccessOutcome.Failure)
        {
            // Only the active model has an activation to lose; a free attack that fails simply misses (U49).
            return state with { InProgress = null, MustEndActivation = e.Check.RiskyFailure && !progress.FreeFight };
        }
        // +1 INJURY DICE for a Critical Success (confirmed ruling) and for a Melee Attack on a Down target (p. 49).
        var critical = e.Check.Outcome == SuccessOutcome.CriticalSuccess;
        var onDownTarget = state.FindModel(e.Target)!.Status == ModelStatus.Down;
        return state with
        {
            InProgress = progress with
            {
                Stage = RollStage.InjuryRoll,
                BonusDice = AttackDice.InjuryBonus(critical, onDownTarget),
                PenaltyDice = 0,
                Critical = critical,
                MarkersAsked = false,
            },
        };
    }

    private static MatchState OnShootingTargetRolled(MatchState state, ShootingTargetRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.TargetRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No target roll is waiting.");
        return state with { InProgress = state.InProgress! with { Stage = e.HitsFriend ? RollStage.TargetChoice : RollStage.ShotRoll } };
    }

    private static MatchState OnFriendlyTargetChosen(MatchState state, FriendlyTargetChosen e)
    {
        Require(state.InProgress is { Stage: RollStage.TargetChoice } p && p.Actor == e.Attacker && state.Pending is { Kind: DecisionKind.ChooseFriendlyTarget }, "No choice of friendly target is waiting.");
        var progress = state.InProgress!;
        var shooter = state.FindModel(e.Attacker)!;
        Require(state.Pending!.Seat == shooter.Seat, "The shooter makes this choice.");
        var victim = state.FindModel(e.Victim);
        var intended = Board.BodyOf(state, state.FindModel(progress.Target!.Value)!)!;
        Require(victim is not null && Board.FriendsNear(state, shooter, intended).Any(m => m.Id == e.Victim), "The shot must be aimed at one of the shooter's own models within 1\" of the target.");
        var facts = Board.ForcedShotFacts(state, shooter, victim!);
        Require(facts.LongRange == e.LongRange && facts.InCover == e.InCover && facts.CoverTerrainId == e.CoverTerrainId && facts.Elevated == e.Elevated, "The declared modifiers do not match the positions.");
        return state with
        {
            Pending = null,
            InProgress = progress with
            {
                Stage = RollStage.ShotRoll,
                Target = e.Victim,
                FriendlyFire = true,
                BonusDice = AttackDice.ShotBonus(e.Elevated),
                PenaltyDice = AttackDice.ShotPenalty(e.LongRange, e.InCover),
                MarkersAsked = false,
            },
        };
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

    // ---- Trench Warfare (trench-maps stage S6) ----

    // The end of Turn 4 of Trench Warfare, before the attacker's game-length roll.
    internal static bool LengthRollOwed(MatchState state) =>
        TrenchWarfare.Is(state.Setup) && state.Phase == Phase.Morale && state.TurnEnded && state.Turn == TrenchWarfare.LengthRollTurn
        && state.LastTurn is null && !state.One.Fled && !state.Two.Fled;

    private static MatchState OnAttackerChosen(MatchState state, AttackerChosen e)
    {
        Require(state.Pending == new PendingDecision(DecisionKind.ChooseAttacker, e.Seat), "No choice of attacker is pending for that seat.");
        Require(Enum.IsDefined(e.Attacker), "Unknown seat.");
        var zones = state.Setup!.ZoneIds;
        var defenderZone = TrenchWarfare.DefenderZone(Board.ScenarioOf(state)!.Battlefield);
        Require(defenderZone is not null && zones.Contains(defenderZone), "The map has no trench lines in a deployment zone.");
        var attackerZone = zones[0] == defenderZone ? zones[1] : zones[0];
        var next = TurnRules.WithSeat(state, e.Attacker, state.SeatFor(e.Attacker) with { ZoneId = attackerZone });
        next = TurnRules.WithSeat(next, e.Attacker.Opponent(), next.SeatFor(e.Attacker.Opponent()) with { ZoneId = defenderZone });
        return next with { Pending = null, Attacker = e.Attacker };
    }

    private static MatchState OnTrenchesScored(MatchState state, TrenchesScored e)
    {
        Require(TrenchWarfare.Is(state.Setup) && state.Attacker is not null && state.Phase == Phase.Morale && !state.TurnEnded && state.Pending is null
            && TurnRules.MoraleRemaining(state).Count == 0 && e.Turn == state.Turn && state.ScoredTurn < state.Turn, "Trench sections are scored once, at the end of a turn.");
        Require(Board.TrenchResults(state).SequenceEqual(e.Results), "The scores must match who is in each trench section.");
        var attacker = state.Attacker!.Value;
        var defending = e.Results.Sum(r => r.DefenderPoints);
        var attacking = e.Results.Sum(r => r.AttackerPoints);
        var next = TurnRules.WithSeat(state, attacker, state.SeatFor(attacker) with { VictoryPoints = state.SeatFor(attacker).VictoryPoints + attacking });
        next = TurnRules.WithSeat(next, attacker.Opponent(), next.SeatFor(attacker.Opponent()) with { VictoryPoints = next.SeatFor(attacker.Opponent()).VictoryPoints + defending });
        return next with { ScoredTurn = state.Turn };
    }

    private static MatchState OnGameLengthRolled(MatchState state, GameLengthRolled e)
    {
        Require(LengthRollOwed(state), "The game-length roll is made once, at the end of Turn 4.");
        Require(e.Seat == state.Attacker, "The attacker rolls for the game's length.");
        Require(e.Die is >= 1 and <= 6, "A D6 is 1 to 6.");
        Require(e.LastTurn == (e.Die <= TrenchWarfare.EndsOnOrBelow ? TrenchWarfare.LengthRollTurn : TrenchWarfare.TurnLimit),
            "On a 1 or 2 the game ends now; on a 3 or more it ends after Turn 5.");
        return state with { LastTurn = e.LastTurn };
    }

    // ---- Trench Warfare's Mine Markers (p. 162, U93) ----

    private static MatchState OnMinesLaid(MatchState state, MinesLaid e)
    {
        Require(state.Pending == new PendingDecision(DecisionKind.LayMines, e.Seat), "No Mine Markers are waiting on that seat.");
        Require(Board.MinePlacementArea(state) is { } area
            && Mines.Problem(Board.ScenarioOf(state)!.Battlefield, area, [.. e.Markers.Select(m => m.At)]) is null,
            "The Mine Markers must be set up legally within the Mine Placement Area.");
        return state with
        {
            Pending = null,
            Mines = e.Markers,
            MinesLaid = true,
            Duds = e.Duds,
            DudsKnown = !e.Duds.IsEmpty,
        };
    }

    // A move event halted by a Mine Marker: the model stops at the first halt instead of the end of its path, and the
    // rest of the move waits in MineStop. Before is the state the move started from.
    private static MatchState HaltAtMine(MatchState before, MatchState after, ModelId id, ImmutableArray<Point2> path)
    {
        if (after.Mines.IsEmpty || path.IsDefaultOrEmpty)
        {
            return after;
        }
        var stops = Board.MineStops(before, before.FindModel(id)!, path);
        if (stops.IsEmpty)
        {
            return after;
        }
        var moved = after.FindModel(id)!;
        return TurnRules.WithModel(after, moved with { Position = stops[0].At }) with
        {
            MineStop = new MineStop(id, path, stops, EndsActivation: after.MustEndActivation),
            MustEndActivation = false,
        };
    }

    private static MatchState OnMineRevealed(MatchState state, MineRevealed e)
    {
        Require(state.MineStop is { Revealed: false } stop && stop.Model == e.Model && stop.Current.Marker == e.Marker && state.Pending is null,
            "Only the Mine Marker the model halted at is revealed.");
        Require(!state.DudsKnown || e.Live == !state.Duds.Contains(e.Marker), "A dud is revealed as a dud and a live mine as live.");
        return state with
        {
            Mines = [.. state.Mines.Where(m => m.Id != e.Marker)],
            MineStop = state.MineStop! with { Revealed = true, Live = e.Live },
        };
    }

    private static MatchState OnMineExploded(MatchState state, MineExploded e)
    {
        Require(state.MineStop is { Revealed: true, Live: true } stop && stop.Model == e.Model && stop.Current.Marker == e.Marker && state.Pending is null,
            "Only a live mine that was just revealed detonates.");
        var model = state.FindModel(e.Model)!;
        var armour = state.Setup!.ProfileOf(e.Model)!.Armour;
        Require(e.Injury.NetDice == 0 && e.Injury.Modifier == InjuryRoll.CombineModifiers([armour]) && e.Injury.TargetAlreadyDown == (model.Status == ModelStatus.Down),
            "A mine's Injury Roll has no extra dice and no weapon modifier, only the model's armour.");
        var markers = Math.Min(BloodMarkers.Max, model.BloodMarkers + e.Injury.BloodMarkers + MineExploded.ShrapnelMarkers);
        var hurt = e.Injury.Outcome switch
        {
            InjuryOutcome.OutOfAction => model with { Status = ModelStatus.OutOfAction, Position = null, BloodMarkers = markers },
            InjuryOutcome.Down => model with { Status = ModelStatus.Down, BloodMarkers = markers },
            _ => model with { BloodMarkers = markers },
        };
        // Taken Down or Out of Action: the move is over (and so is the activation, p. 49). Otherwise it carries on.
        return TurnRules.WithModel(state, hurt) with
        {
            MineStop = hurt.Status == ModelStatus.Standing ? state.MineStop! with { Live = false } : null,
        };
    }

    private static MatchState OnMoveResumed(MatchState state, MoveResumed e)
    {
        Require(state.MineStop is { Revealed: true, Live: false } stop && stop.Model == e.Model && state.Pending is null && state.Phase == Phase.Activation,
            "A halted move carries on only once its Mine Marker has been dealt with.");
        var halted = state.MineStop!;
        var model = state.FindModel(e.Model)!;
        Require(model.Status == ModelStatus.Standing, "Only a standing model carries on.");
        var next = halted.Index + 1 < halted.Stops.Length ? halted.Stops[halted.Index + 1] : null;
        Require(Mines.Between(halted.Path, halted.Current, next).SequenceEqual(e.Path), "The model carries on along the rest of its path.");
        var at = next?.At ?? halted.Path[^1];
        return TurnRules.WithModel(state, model with { Position = at }) with
        {
            MineStop = next is null ? null : halted with { Index = halted.Index + 1, Revealed = false, Live = false },
            MustEndActivation = next is null && halted.EndsActivation,
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
