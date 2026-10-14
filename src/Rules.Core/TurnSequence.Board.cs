using System.Collections.Immutable;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// The part of the turn sequence that needs positions (M4): setting models up, Move, Dash, Shoot, Blood Marker
// decisions and the staged rolls behind them. The same contract as the rest of Decide: validation first with no dice,
// then events; the geometry itself is always asked of the M3 types.
public static partial class TurnSequence
{
    private const string RefActions = "1.0.2 p. 34";
    private const string RefMarkers = "1.0.2 pp. 27-28";

    // ---- validation ----

    private static Rejection? ValidatePlace(MatchState state, PlaceModel c)
    {
        if (state.Setup!.Scenario is null)
        {
            return new(ReasonCode.NoBattlefield, "This match has no battlefield; use DeployModel.", RefDeployment);
        }
        if (state.Phase != Phase.Deployment)
        {
            return new(ReasonCode.WrongPhase, "Models are set up only during Deployment.", RefDeployment);
        }
        if (TurnRules.NextDeployer(state) != c.Seat)
        {
            return new(ReasonCode.NotYourTurn, "It is the other player's turn to deploy.", RefDeployment);
        }
        var model = state.FindModel(c.Model);
        if (model is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such model.", RefDeployment);
        }
        if (model.Seat != c.Seat)
        {
            return new(ReasonCode.NotYourModel, "That model belongs to the other player.", RefDeployment);
        }
        if (model.Deployed)
        {
            return new(ReasonCode.ModelAlreadyDeployed, "That model is already deployed.", RefDeployment);
        }
        var placement = Board.CheckPlacement(state, c.Seat, model, c.At);
        return placement.IsLegal ? null : new(ReasonCode.PlacementIllegal, placement.Text, placement.RuleRef);
    }

    // The checks every action of the active model shares. Returns the model when all is well.
    private static Rejection? ValidateActing(MatchState state, Seat seat, ModelId id, out ModelState? model)
    {
        model = null;
        if (state.Setup!.Scenario is null)
        {
            return new(ReasonCode.NoBattlefield, "This match has no battlefield.", RefActions);
        }
        if (state.Phase != Phase.Activation)
        {
            return new(ReasonCode.WrongPhase, "Actions are taken only during the Activation Phase.", RefActions);
        }
        if (state.ActiveModel is not { } active)
        {
            return new(ReasonCode.NoActivationInProgress, "No model is activating.", RefActions);
        }
        var found = state.FindModel(id);
        if (found is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such model.", RefActions);
        }
        if (found.Seat != seat)
        {
            return new(ReasonCode.NotYourModel, "That model belongs to the other player.", RefActions);
        }
        if (active != id)
        {
            return new(ReasonCode.ActivationInProgress, "Only the activating model can take actions.", RefActions);
        }
        if (found.Status != ModelStatus.Standing)
        {
            return new(ReasonCode.ModelNotStanding, "A Down model cannot take that action.", RefActions);
        }
        if (found.Position is null)
        {
            return new(ReasonCode.ModelNotOnTable, "The model is not on the battlefield.", RefActions);
        }
        model = found;
        return null;
    }

    private static Rejection? ValidateMove(MatchState state, Seat seat, ModelId id, ImmutableArray<Point2> path, ActionsTaken action)
    {
        if (ValidateActing(state, seat, id, out var model) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(action))
        {
            return new(ReasonCode.ActionAlreadyTaken, $"A {action} ACTION can be taken only once per activation.", RefActions);
        }
        if (!path.IsDefault && path.Length > ModelMoved.MaxWaypoints)
        {
            return new(ReasonCode.TooManyWaypoints, $"A move has at most {ModelMoved.MaxWaypoints} waypoints.", Board.RefMove);
        }
        var result = Board.CheckMove(state, model!, path.IsDefault ? [] : path);
        return result.IsLegal ? null : new(ReasonCode.MoveIllegal, result.Text, result.RuleRef);
    }

    private static Rejection? ValidateShoot(MatchState state, Shoot c)
    {
        if (ValidateActing(state, c.Seat, c.Attacker, out var attacker) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Shoot ACTION can be taken only once per activation.", RefActions);
        }
        var target = state.FindModel(c.Target);
        if (target is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", Board.RefAttack);
        }
        var assessment = Board.Assess(state, attacker!, target);
        return assessment.IsLegal ? null : new(assessment.Refusal!.Value, assessment.Text, assessment.RuleRef);
    }

    private static int MarkersAvailable(MatchState state, DecisionKind kind)
    {
        var progress = state.InProgress;
        if (progress is null)
        {
            return 0;
        }
        var holder = kind == DecisionKind.SpendMarkersOnSuccessRoll ? progress.Actor : progress.Target;
        return holder is { } id ? state.FindModel(id)?.BloodMarkers ?? 0 : 0;
    }

    private static Rejection? ValidateSpend(MatchState state, SpendMarkers c)
    {
        if (state.Pending is not { } pending)
        {
            return new(ReasonCode.NoDecisionPending, "There is no decision to make right now.", RefMarkers);
        }
        if (pending.Kind is not (DecisionKind.SpendMarkersOnSuccessRoll or DecisionKind.SpendMarkersOnInjuryRoll))
        {
            return new(ReasonCode.WrongDecision, $"A different decision is pending ({pending.Kind}).", RefMarkers);
        }
        if (pending.Seat != c.Seat)
        {
            return new(ReasonCode.NotYourTurn, "That decision belongs to the other player.", RefMarkers);
        }
        return c.Count < 0 || c.Count > MarkersAvailable(state, pending.Kind)
            ? new(ReasonCode.InvalidMarkerCount, "You can spend from none up to all of the model's Blood Markers.", RefMarkers)
            : null;
    }

    // ---- executing ----

    private static void ExecuteBoard(Run run, Command command)
    {
        var state = run.State;
        switch (command)
        {
            case PlaceModel c:
                run.Emit(new ModelDeployed(c.Seat, c.Model, c.At));
                break;
            case MoveModel c:
                var move = Board.CheckMove(state, state.FindModel(c.Model)!, c.Path);
                run.Emit(new ModelMoved(c.Model, MoveKind.Move, c.Path, move.Cost));
                break;
            case DashModel c:
                run.Emit(new DashDeclared(c.Model, c.Path));
                if (run.State.FindModel(c.Model)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            case Shoot c:
                var attacker = state.FindModel(c.Attacker)!;
                var facts = Board.Assess(state, attacker, state.FindModel(c.Target)!);
                run.Emit(new RangedAttackDeclared(
                    c.Attacker,
                    c.Target,
                    facts.LongRange,
                    facts.InCover,
                    facts.CoverTerrainId,
                    facts.AttackerLevel,
                    facts.TargetLevel,
                    (facts.LongRange ? 1 : 0) + (facts.InCover ? 1 : 0)));
                if (attacker.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            case SpendMarkers c:
                var pending = state.Pending!;
                var progress = state.InProgress!;
                var success = pending.Kind == DecisionKind.SpendMarkersOnSuccessRoll;
                run.Emit(new MarkersSpent(
                    c.Seat,
                    success ? progress.Actor : progress.Target!.Value,
                    c.Count,
                    success ? MarkerSpendPurpose.ReduceSuccessRoll : MarkerSpendPurpose.RaiseInjuryRoll));
                break;
        }
    }

    // ---- the staged rolls: each die is rolled only when its stage is reached with no decision pending ----

    private static bool AdvanceRoll(Run run, MatchState s, IDiceSource dice)
    {
        var progress = s.InProgress!;
        var actor = s.FindModel(progress.Actor)!;
        switch (progress.Stage)
        {
            case RollStage.DashRoll:
            {
                // A Dash is always a Risky Success Roll (p. 34); spent markers are penalty dice.
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, risky: true);
                run.Emit(new DashRolled(progress.Actor, check));
                if (check.Outcome == SuccessOutcome.Failure)
                {
                    run.Emit(new ActivationEnded(progress.Actor, ActivationEndReason.RiskyFailure));
                }
                return true;
            }
            case RollStage.DashMove:
            {
                var cost = Board.CheckMove(s, actor, progress.DashPath).Cost;
                run.Emit(new ModelMoved(progress.Actor, MoveKind.Dash, progress.DashPath, cost));
                return true;
            }
            case RollStage.ShotRoll:
            {
                // A Shaken warband's Success Rolls are Risky (p. 50).
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, risky: s.IsShaken(actor.Seat));
                run.Emit(new RangedSuccessRolled(progress.Actor, progress.Target!.Value, check));
                if (check.RiskyFailure)
                {
                    run.Emit(new ActivationEnded(progress.Actor, ActivationEndReason.RiskyFailure));
                }
                else if (check.Outcome != SuccessOutcome.Failure && run.State.FindModel(progress.Target.Value)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, actor.Seat));
                }
                return true;
            }
            case RollStage.InjuryRoll:
            {
                var target = s.FindModel(progress.Target!.Value)!;
                var modifiers = new[] { s.Setup!.ProfileOf(actor.Id)!.RangedInjuryModifier, s.Setup.ProfileOf(target.Id)!.Armour };
                var injury = InjuryRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, modifiers, target.Status == ModelStatus.Down);
                run.Emit(new InjuryRolled(actor.Id, target.Id, injury));
                if (injury.Outcome == InjuryOutcome.OutOfAction && !Reducer.HasModelOnBattlefield(run.State, target.Seat))
                {
                    run.Emit(new MatchEnded(actor.Seat, OutcomeReason.AllEnemiesOutOfAction));
                }
                return true;
            }
            default:
                throw new InvalidOperationException("Unknown roll stage.");
        }
    }
}
