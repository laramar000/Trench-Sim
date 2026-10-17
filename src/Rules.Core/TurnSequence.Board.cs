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
        if (action == ActionsTaken.Move && (state.Actions & (ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
        {
            return new(ReasonCode.ActionAlreadyTaken, "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.", RefActions);
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
        if ((state.Actions & (ActionsTaken.Charge | ActionsTaken.Fight)) != 0)
        {
            return new(ReasonCode.ActionExcluded, "A model cannot take a Shoot ACTION and a Charge or Fight ACTION in the same activation (no ASSAULT weapons yet).", "1.0.2 pp. 34, 42");
        }
        var target = state.FindModel(c.Target);
        if (target is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", Board.RefAttack);
        }
        var assessment = Board.Assess(state, attacker!, target);
        return assessment.IsLegal ? null : new(assessment.Refusal!.Value, assessment.Text, assessment.RuleRef);
    }

    private static Rejection? ValidateCharge(MatchState state, ChargeModel c)
    {
        if (ValidateActing(state, c.Seat, c.Model, out var charger) is { } rejection)
        {
            return rejection;
        }
        if ((state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
        {
            return new(ReasonCode.ActionAlreadyTaken, "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.", RefActions);
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionExcluded, "A model cannot take a Shoot ACTION and a Charge or Fight ACTION in the same activation (no ASSAULT weapons yet).", "1.0.2 pp. 34, 42");
        }
        var target = state.FindModel(c.Target);
        return target is null ? new(ReasonCode.UnknownModel, "There is no such target.", Board.RefCharge) : Board.AssessCharge(state, charger!, target);
    }

    private static Rejection? ValidateRetreat(MatchState state, RetreatModel c)
    {
        if (ValidateActing(state, c.Seat, c.Model, out var model) is { } rejection)
        {
            return rejection;
        }
        if ((state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0)
        {
            return new(ReasonCode.ActionAlreadyTaken, "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.", RefActions);
        }
        if (c.Path.IsDefaultOrEmpty)
        {
            return new(ReasonCode.MoveIllegal, "A retreat needs at least one point to move to.", Board.RefRetreat);
        }
        if (c.Path.Length > ModelMoved.MaxWaypoints)
        {
            return new(ReasonCode.TooManyWaypoints, $"A move has at most {ModelMoved.MaxWaypoints} waypoints.", Board.RefMove);
        }
        return Board.CheckRetreat(state, model!, c.Path);
    }

    private static Rejection? ValidateFight(MatchState state, FightModel c)
    {
        if (ValidateActing(state, c.Seat, c.Model, out var attacker) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(ActionsTaken.Fight))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Fight ACTION can be taken only once per activation.", RefActions);
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionExcluded, "A model cannot take a Shoot ACTION and a Charge or Fight ACTION in the same activation (no ASSAULT weapons yet).", "1.0.2 pp. 34, 42");
        }
        var target = state.FindModel(c.Target);
        return target is null ? new(ReasonCode.UnknownModel, "There is no such target.", Board.RefMelee) : Board.AssessMelee(state, attacker!, target).Refusal;
    }

    // The three answers that are not Blood Marker spends: free attacks on a retreating model, and the friendly model a
    // shot into melee was forced onto.
    private static Rejection? ValidateChoice(MatchState state, Command command)
    {
        var (kind, seat) = command switch
        {
            FreeFight c => (DecisionKind.FreeFight, c.Seat),
            DeclineFreeFights c => (DecisionKind.FreeFight, c.Seat),
            ChooseFriendlyTarget c => (DecisionKind.ChooseFriendlyTarget, c.Seat),
            _ => throw new ArgumentException("Not a choice."),
        };
        if (state.Pending is not { } pending)
        {
            return new(ReasonCode.NoDecisionPending, "There is no decision to make right now.", Board.RefRetreat);
        }
        if (pending.Kind != kind)
        {
            return new(ReasonCode.WrongDecision, $"A different decision is pending ({pending.Kind}).", Board.RefRetreat);
        }
        if (pending.Seat != seat)
        {
            return new(ReasonCode.NotYourTurn, "That decision belongs to the other player.", Board.RefRetreat);
        }
        switch (command)
        {
            case FreeFight c:
                return Board.FreeFighters(state, state.Retreat!).Any(m => m.Id == c.Attacker)
                    ? null
                    : new(ReasonCode.NotAFreeFighter, "That model cannot attack the retreating model: it must be a standing enemy with a Melee Weapon within 1\" that has not attacked yet.", "1.0.2 p. 37");
            case ChooseFriendlyTarget c:
                var progress = state.InProgress!;
                var shooter = state.FindModel(progress.Actor)!;
                var intended = Board.BodyOf(state, state.FindModel(progress.Target!.Value)!)!;
                return Board.FriendsNear(state, shooter, intended).Any(m => m.Id == c.Victim)
                    ? null
                    : new(ReasonCode.NotACandidate, "The shot must be aimed at one of your own models within 1\" of the target.", Board.RefIntoMelee);
            default:
                return null;
        }
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
                    AttackDice.ShotPenalty(facts.LongRange, facts.InCover))
                { IntoMelee = facts.IntoMelee });
                // A shot into melee asks about markers only once the D6 has settled who is shot (p. 42).
                if (attacker.BloodMarkers > 0 && !facts.IntoMelee)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            case ChargeModel c:
                run.Emit(new ChargeDeclared(c.Model, c.Target));
                break;
            case RetreatModel c:
                run.Emit(new RetreatDeclared(c.Model, c.Path));
                break;
            case FightModel c:
                var fighter = state.FindModel(c.Model)!;
                run.Emit(new MeleeAttackDeclared(c.Model, c.Target, false, Board.AssessMelee(state, fighter, state.FindModel(c.Target)!).CoverTerrainId));
                if (fighter.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            case FreeFight c:
                var free = state.FindModel(c.Attacker)!;
                var victim = state.FindModel(state.Retreat!.Model)!;
                run.Emit(new MeleeAttackDeclared(c.Attacker, victim.Id, true, Board.AssessMelee(state, free, victim).CoverTerrainId));
                if (free.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, free.Seat.Opponent()));
                }
                break;
            case DeclineFreeFights c:
                run.Emit(new FreeFightsDeclined(c.Seat));
                break;
            case ChooseFriendlyTarget c:
                var shooter = state.FindModel(state.InProgress!.Actor)!;
                var forced = Board.ForcedShotFacts(state, shooter, state.FindModel(c.Victim)!);
                run.Emit(new FriendlyTargetChosen(shooter.Id, c.Victim, forced.LongRange, forced.InCover, forced.CoverTerrainId));
                if (shooter.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, shooter.Seat.Opponent()));
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
                else if (check.Outcome != SuccessOutcome.Failure && !progress.FriendlyFire && run.State.FindModel(progress.Target.Value)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, actor.Seat));
                }
                return true;
            }
            case RollStage.TargetRoll:
            {
                var die = Roll(dice);
                run.Emit(new ShootingTargetRolled(progress.Actor, progress.Target!.Value, die));
                if (die <= 3)
                {
                    run.Emit(new DecisionRequested(DecisionKind.ChooseFriendlyTarget, actor.Seat));
                }
                else if (actor.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, actor.Seat.Opponent()));
                }
                return true;
            }
            case RollStage.FightRoll:
            {
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, risky: s.IsShaken(actor.Seat));
                run.Emit(new MeleeSuccessRolled(progress.Actor, progress.Target!.Value, check));
                if (check.RiskyFailure)
                {
                    // Only the active model has an activation to lose; a free attack that fails simply misses (U49).
                    if (!progress.FreeFight)
                    {
                        run.Emit(new ActivationEnded(progress.Actor, ActivationEndReason.RiskyFailure));
                    }
                }
                else if (check.Outcome != SuccessOutcome.Failure && run.State.FindModel(progress.Target.Value)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, actor.Seat));
                }
                return true;
            }
            case RollStage.ChargeRoll:
            {
                run.Emit(new ChargeRolled(progress.Actor, progress.Target!.Value, Roll(dice)));
                return true;
            }
            case RollStage.ChargeMove:
            {
                var plan = Board.PlanCharge(s, actor, s.FindModel(progress.Target!.Value)!, progress.ChargeDie);
                run.Emit(new ChargeMoved(progress.Actor, progress.Target.Value, plan.Path, plan.Cost, plan.Reached));
                return true;
            }
            case RollStage.InjuryRoll:
            {
                var target = s.FindModel(progress.Target!.Value)!;
                var weapon = s.Setup!.ProfileOf(actor.Id)!;
                var modifiers = AttackDice.InjuryModifiers(weapon, s.Setup.ProfileOf(target.Id)!, progress.Kind);
                var injury = InjuryRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, modifiers, target.Status == ModelStatus.Down);
                run.Emit(new InjuryRolled(actor.Id, target.Id, injury));
                if (injury.Outcome == InjuryOutcome.OutOfAction && !Reducer.HasModelOnBattlefield(run.State, target.Seat))
                {
                    run.Emit(new MatchEnded(target.Seat.Opponent(), OutcomeReason.AllEnemiesOutOfAction));
                }
                return true;
            }
            default:
                throw new InvalidOperationException("Unknown roll stage.");
        }
    }

    // After a Retreat is declared and each free attack: offer the next free attack, or finish the retreat (p. 37).
    private static bool AdvanceRetreat(Run run, MatchState s, RetreatInProgress retreat)
    {
        var retreater = s.FindModel(retreat.Model)!;
        if (!retreat.Closed && Board.FreeFighters(s, retreat).Count > 0)
        {
            run.Emit(new DecisionRequested(DecisionKind.FreeFight, retreater.Seat.Opponent()));
            return true;
        }
        if (retreater.Status == ModelStatus.Standing)
        {
            run.Emit(new RetreatResolved(retreat.Model, retreat.Path, Board.RetreatCost(s, retreater, retreat.Path)));
        }
        else
        {
            // Taken Down or Out of Action by a free attack: no move, and a model taken Down in its own activation ends it (p. 49).
            run.Emit(new RetreatResolved(retreat.Model, [], Length.Zero));
            run.Emit(new ActivationEnded(retreat.Model, ActivationEndReason.TakenDown));
        }
        return true;
    }
}
