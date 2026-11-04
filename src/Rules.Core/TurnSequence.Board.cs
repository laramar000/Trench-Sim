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
    private const string RefBloodbath = "1.0.2 pp. 19, 47";
    private const string RefShrine = "1.0.2 p. 82";

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
    // Hateful (U128): until it has charged, a bound model takes no other ACTION and the activation does not end.
    private static Rejection? HatefulRefusal(MatchState state, Command command)
    {
        if (state.Phase != Phase.Activation || state.ActiveModel is not { } active || state.InProgress is not null || state.Pending is not null
            || state.Setup?.Scenario is null || state.FindModel(active) is not { } model)
        {
            return null;
        }
        var acts = command switch
        {
            MoveModel c => c.Model == active,
            DashModel c => c.Model == active,
            Shoot c => c.Attacker == active,
            ShootPoint c => c.Attacker == active,
            TreatModel c => c.Model == active,
            TakeSelfAction c => c.Model == active,
            TriggerMartyrdom c => c.Model == active,
            RetreatModel c => c.Model == active,
            FightModel c => c.Model == active,
            EndActivation => true,
            ChargeModel c => c.Model == active && Board.HatefulTargets(state, model) is { Count: > 0 } t && !t.Any(x => x.Id == c.Target),
            _ => false,
        };
        if (!acts)
        {
            return null;
        }
        var targets = Board.HatefulTargets(state, model);
        return targets.Count == 0
            ? null
            : new(ReasonCode.ActionExcluded, "A Hateful model must charge the closest enemy it sees that is not CULT or DEMONIC.", RefHateful);
    }

    private const string RefHateful = "Warbands p. 169";

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
        if (ActionLimits.MoveExcluded(state, id) is { } heavy)
        {
            return new(ReasonCode.ActionExcluded, heavy, ActionLimits.Ref);
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
        if (ActionLimits.ShootExcluded(state, c.Attacker, c.Weapon) is { } excluded)
        {
            return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
        }
        var target = state.FindModel(c.Target);
        if (target is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such target.", Board.RefAttack);
        }
        var assessment = Board.Assess(state, attacker!, target, c.Weapon);
        return assessment.IsLegal ? null : new(assessment.Refusal!.Value, assessment.Text, assessment.RuleRef);
    }

    // Roster stage R5h (U113): a Medi-kit's Treat ACTION (p. 84).
    private static Rejection? ValidateTreat(MatchState state, TreatModel c)
    {
        if (ValidateActing(state, c.Seat, c.Model, out var medic) is { } rejection)
        {
            return rejection;
        }
        if (!Enum.IsDefined(c.Choice))
        {
            return new(ReasonCode.InvalidChoice, "That is not one of the choices on offer.", RefTreat);
        }
        if (state.Setup!.ProfileOf(c.Model) is not { Treats: true })
        {
            return new(ReasonCode.NoSuchAction, "Only a model with a Medi-kit can take a Treat ACTION.", RefTreat);
        }
        if (state.Actions.HasFlag(ActionsTaken.Treat))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Treat ACTION can be taken only once per activation.", RefActions);
        }
        return Reducer.TreatRefusal(state, medic!, c.Target, c.Choice) is { } why ? new(ReasonCode.NothingToTreat, why, RefTreat) : null;
    }

    public const string RefTreat = "1.0.2 p. 84";

    // Roster stage R6b (U118): an Aim or Fortify ACTION (Warbands pp. 28, 33, 85), once per activation each.
    private static Rejection? ValidateSelfAction(MatchState state, TakeSelfAction c)
    {
        if (ValidateActing(state, c.Seat, c.Model, out var actor) is { } rejection)
        {
            return rejection;
        }
        if (!Enum.IsDefined(c.Action))
        {
            return new(ReasonCode.InvalidChoice, "That is not one of the choices on offer.", RefSelfAction);
        }
        if (!Reducer.HasSelfAction(state.Setup!.ProfileOf(c.Model)!, c.Action))
        {
            return new(ReasonCode.NoSuchAction, $"Only a model whose unit has the {c.Action} ACTION can take it.", RefSelfAction);
        }
        if (state.Actions.HasFlag(Reducer.SelfActionFlag(c.Action)))
        {
            return new(ReasonCode.ActionAlreadyTaken, $"A {c.Action} ACTION can be taken only once per activation.", RefActions);
        }
        // Roster stage R6e (U121): a Laying on of Hands is taken on a friend with a BLOOD MARKER; no other ACTION has a target.
        if (c.Action != SelfActionKind.LayingOnHands)
        {
            return c.Target is null ? null : new(ReasonCode.InvalidChoice, $"A {c.Action} ACTION has no target.", RefSelfAction);
        }
        return c.Target is not { } target ? new(ReasonCode.UnknownModel, "A Laying on of Hands ACTION needs a target.", RefSelfAction)
            : Reducer.HandsRefusal(state, actor!, target) is { } why ? new(ReasonCode.NothingToTreat, why, RefSelfAction) : null;
    }

    public const string RefSelfAction = "Warbands pp. 28, 33, 53, 55, 85";

    // Roster stage R6b: the bonus dice of an Aim or Fortify roll, and what a successful Aim adds to the model's shots now.
    public static int SelfActionDice(SelfActionKind action) => action == SelfActionKind.Aim ? Reducer.AimDice : 0;

    // Roster stage R6e (U121): the bonus dice of any of these ACTIONS' rolls for this model, whether that roll is Risky,
    // the friends an Enforced Orthodoxy would stand up now, and the BLOOD MARKERS a Laying on of Hands' target has.
    public static int SelfActionBonus(MatchState state, ModelId model, SelfActionKind action) =>
        state.Setup?.ProfileOf(model) is { } profile ? Reducer.SelfActionBonus(profile, action) : 0;

    public static bool SelfActionRisky(MatchState state, ModelId model, SelfActionKind action) =>
        state.FindModel(model) is { } m && Reducer.SelfActionRisky(state, m, action);

    // Roster stage R7a (U128): the enemies a Hateful model must charge (the closest it sees), empty when it is free.
    public static IReadOnlyList<ModelId> HatefulTargets(MatchState state, ModelId model) =>
        state.Setup?.Scenario is not null && state.FindModel(model) is { } m ? [.. Board.HatefulTargets(state, m).Select(t => t.Id)] : [];

    public static IReadOnlyList<ModelId> OrthodoxyStands(MatchState state, ModelId model) =>
        state.FindModel(model) is { } m ? Reducer.OrthodoxyStands(state, m) : [];

    public static string? HandsRefusal(MatchState state, ModelId model, ModelId target) =>
        state.FindModel(model) is { } m ? Reducer.HandsRefusal(state, m, target) : "There is no such model.";

    public static int AimedDice(MatchState state, ModelId shooter) => Reducer.AimedDice(state, shooter);

    // Roster stage R6c (U119): the Dash roll's die from a Musical Instrument within 4" alone, and Counter-Charge's die on
    // the active model's Melee Attacks now.
    public static int FanfareDice(MatchState state, ModelId model) => state.FindModel(model) is { } m ? Reducer.FanfareDice(state, m) : 0;

    public static int CounterDice(MatchState state, ModelId attacker) => Reducer.CounterDice(state, attacker);

    // Roster stage R6d (U120): Overwhelming Horde's DICE on a Melee Attack with that weapon now.
    public static int HordeDice(MatchState state, ModelId attacker, int weapon) => Reducer.HordeDice(state, attacker, weapon);

    // Roster stage R5j (U115): a Trigger Martyrdom Device ACTION (Warbands p. 58), once per game.
    private static Rejection? ValidateTrigger(MatchState state, TriggerMartyrdom c)
    {
        if (ValidateActing(state, c.Seat, c.Model, out var bearer) is { } rejection)
        {
            return rejection;
        }
        if (state.Setup!.ProfileOf(c.Model) is not { Martyrdom: true })
        {
            return new(ReasonCode.NoSuchAction, "Only a model with a Martyrdom Device can take a Trigger Martyrdom Device ACTION.", Board.RefMartyrdom);
        }
        return Reducer.MartyrdomRefusal(state, bearer!) is { } why
            ? new(bearer!.MartyrdomSpent ? ReasonCode.ActionAlreadyTaken : ReasonCode.ModelNotStanding, why, Board.RefMartyrdom)
            : null;
    }

    // Roster stage R5j: the models a Martyrdom Device would hit now (its bearer last) and those within 1" of it, for the
    // client's preview.
    public static (IReadOnlyList<ModelId> Hit, IReadOnlyList<ModelId> Close) MartyrdomHits(MatchState state, ModelId bearer) =>
        state.FindModel(bearer) is { } m ? Board.MartyrdomHits(state, m) : ([], []);

    // Roster stage R5h: the bonus dice of a Treat roll (Holy Water of Lalibela).
    public static int TreatDice(MatchState state, ModelId model) => Reducer.TreatDice(state, model);

    // Roster stage R5a (U105): a Shoot ACTION with a BLAST weapon aimed at a point (p. 53).
    private static Rejection? ValidateShootPoint(MatchState state, ShootPoint c)
    {
        if (ValidateActing(state, c.Seat, c.Attacker, out var attacker) is { } rejection)
        {
            return rejection;
        }
        if (state.Actions.HasFlag(ActionsTaken.Shoot))
        {
            return new(ReasonCode.ActionAlreadyTaken, "A Shoot ACTION can be taken only once per activation.", RefActions);
        }
        if (ActionLimits.ShootExcluded(state, c.Attacker, c.Weapon) is { } excluded)
        {
            return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
        }
        var assessment = Board.AssessPoint(state, attacker!, c.At, c.Weapon);
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
        if (ActionLimits.MeleeExcluded(state, c.Model, ActionsTaken.Charge) is { } excluded)
        {
            return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
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
        if (ActionLimits.MoveExcluded(state, c.Model) is { } heavy)
        {
            return new(ReasonCode.ActionExcluded, heavy, ActionLimits.Ref);
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
        // Roster stage R5i (U114): a Grind to Dust ACTION (weapon GrindWeapon) is its own ACTION, once per activation; it
        // is not a Fight ACTION, so the Shoot/Fight limits do not apply, and it ends a Fight's attacks still to come.
        if (c.Weapon == ModelProfile.GrindWeapon)
        {
            if (state.Setup!.ProfileOf(c.Model) is not { GrindsToDust: true })
            {
                return new(ReasonCode.NoSuchAction, "Only a model with Piston Legs can take a Grind to Dust ACTION.", Board.RefGrind);
            }
            if (state.Actions.HasFlag(ActionsTaken.Grind))
            {
                return new(ReasonCode.ActionAlreadyTaken, "A Grind to Dust ACTION can be taken only once per activation.", RefActions);
            }
            return state.FindModel(c.Target) is { } prey ? Board.AssessMelee(state, attacker!, prey, c.Weapon).Refusal
                : new(ReasonCode.UnknownModel, "There is no such target.", Board.RefMelee);
        }
        // Roster stage R6d (U120): a Trample ACTION (weapon TrampleWeapon), the same kind of ACTION.
        if (c.Weapon == ModelProfile.TrampleWeapon)
        {
            if (state.Setup!.ProfileOf(c.Model) is not { Tramples: true })
            {
                return new(ReasonCode.NoSuchAction, "Only a model whose unit has it can take a Trample ACTION.", Board.RefTrample);
            }
            if (state.Actions.HasFlag(ActionsTaken.Trample))
            {
                return new(ReasonCode.ActionAlreadyTaken, "A Trample ACTION can be taken only once per activation.", RefActions);
            }
            return state.FindModel(c.Target) is { } trampled ? Board.AssessMelee(state, attacker!, trampled, c.Weapon).Refusal
                : new(ReasonCode.UnknownModel, "There is no such target.", Board.RefMelee);
        }
        // Roster stage R3 (U100): a Fight with attacks still to come (a second weapon, CLEAVE) goes on with the next one.
        if (NextFightAttack(state, c.Model) is { } next)
        {
            if (c.Weapon != next.Weapon)
            {
                return new(ReasonCode.WrongWeapon, next.OffHand
                    ? "The Fight goes on with the Off-Hand weapon's attack."
                    : "The Fight goes on with the same weapon's next CLEAVE attack.", Board.RefMelee);
            }
        }
        else
        {
            if (state.Actions.HasFlag(ActionsTaken.Fight))
            {
                return new(ReasonCode.ActionAlreadyTaken, "A Fight ACTION can be taken only once per activation.", RefActions);
            }
            if (ActionLimits.MeleeExcluded(state, c.Model, ActionsTaken.Fight) is { } excluded)
            {
                return new(ReasonCode.ActionExcluded, excluded, ActionLimits.Ref);
            }
        }
        var target = state.FindModel(c.Target);
        return target is null ? new(ReasonCode.UnknownModel, "There is no such target.", Board.RefMelee) : Board.AssessMelee(state, attacker!, target, c.Weapon).Refusal;
    }

    // Roster stage R3: the next attack of the active model's Fight ACTION, when it has attacks still to come.
    public static MeleeAttackSlot? NextFightAttack(MatchState state, ModelId model) =>
        state.AttacksLeft is { Free: false, Next.Length: > 0 } left && left.Attacker == model && state.ActiveModel == model ? left.Next[0] : null;

    // Roster stage R4a: why a Shoot ACTION is refused for a weapon the model does not have (the server's words).
    public static string NoSuchGun(int weapon) => Board.NoSuchGun(weapon);

    // Roster stage R4b: the models a BLAST attack by attacker at target with that weapon would hit, the target first
    // (empty without BLAST), for the client's previews.
    public static IReadOnlyList<ModelId> BlastHits(MatchState state, ModelId attacker, ModelId target, int weapon = 0) =>
        state.FindModel(attacker) is { } a && state.FindModel(target) is { } t ? Board.BlastHits(state, a, t, weapon) : [];

    // Roster stage R4c: where a missed SCATTER attack may land (the 360 one-degree directions that are possible), and the
    // models a blast landing at a point would hit, for the client's decision bar and board.
    public static IReadOnlyList<Point2> ScatterPoints(MatchState state) =>
        state.InProgress is { Stage: RollStage.Scatter } progress ? Board.ScatterPoints(state, progress).Select(p => p.At).ToList() : [];

    public static bool CanScatterTo(MatchState state, Point2 at) =>
        state.InProgress is { Stage: RollStage.Scatter } progress && Board.CanScatterTo(state, progress, at);

    // Roster stage R5a (U105): whether a BLAST attack may be aimed at that point, with its Long Range (for previews).
    public static (Rejection? Refusal, bool LongRange) CheckPoint(MatchState state, ModelId attacker, Point2 at, int weapon = 0)
    {
        if (state.FindModel(attacker) is not { } a)
        {
            return (new(ReasonCode.UnknownModel, "There is no such model.", Board.RefBlast), false);
        }
        var facts = Board.AssessPoint(state, a, at, weapon);
        return (facts.IsLegal ? null : new(facts.Refusal!.Value, facts.Text, facts.RuleRef), facts.LongRange);
    }

    // Roster stage R4c/R5a: the centre a scattering attack in progress scatters from (its target's, or its aimed
    // point), or null.
    public static Point2? ScatterOrigin(MatchState state) =>
        state.InProgress is { Stage: RollStage.Scatter } progress ? Board.ScatterCentre(state, progress) : null;

    public static IReadOnlyList<ModelId> BlastHitsAt(MatchState state, ModelId attacker, Point2 at, int weapon = 0) =>
        state.FindModel(attacker) is { } a ? Board.BlastHitsAt(state, a, at, weapon) : [];

    // Roster stage R3: whether BLOCK gives this Melee Attack a penalty die (the target blocks a model that charged this turn).
    public static bool Blocks(MatchState state, ModelId attacker, ModelId target) => Board.Blocks(state, attacker, target);

    public static bool Fears(MatchState state, ModelId attacker, ModelId target) => Board.Fears(state, attacker, target);

    // Roster stage R5d (U108): the bonus dice a Dash roll (Musical Instrument within 4") and a climb roll (Mountaineer
    // Kit) would start with now, and whether a failed Risky roll ends the model's activation (not with a Crucifix).
    public static int DashDice(MatchState state, ModelId model) => state.FindModel(model) is { } m ? Reducer.DashDice(state, m) : 0;

    public static int ClimbDice(MatchState state, ModelId model) => Reducer.ClimbDice(state, model);

    public static bool RiskyEnds(MatchState state, ModelId model) => Reducer.RiskyEnds(state, model);

    // Roster stage R5k (U116): whether the model's player would be asked to use a Talisman after a failed Risky roll now.
    public static bool TalismanReady(MatchState state, ModelId model) => Reducer.TalismanReady(state, model);

    // Roster stage R6a (U117): a Trench Cleric's aura gives this model NEGATE FEAR now.
    public static bool Inspired(MatchState state, ModelId model) => Board.Inspired(state, model);

    // A failed Risky roll ends the activation, unless the model has a Talisman left: then its player is asked first.
    private static void EndForRisky(Run run, ModelId model)
    {
        if (Reducer.TalismanReady(run.State, model))
        {
            run.Emit(new DecisionRequested(DecisionKind.UseTalisman, run.State.FindModel(model)!.Seat));
        }
        else
        {
            run.Emit(new ActivationEnded(model, ActivationEndReason.RiskyFailure));
        }
    }

    // Roster stage R5e (U109): a Ranged Attack at this model now would have the Urn of the Bitter Ashes' -1 DICE.
    public static bool Whispers(MatchState state, ModelId target) => state.FindModel(target) is { } m && Reducer.Whispers(state, m);

    // Roster stage R5f (U110): the model has the COVER keyword now (a shield, or dug in with a Shovel), and the cover id
    // events name for it.
    public static bool HasCover(MatchState state, ModelId model) => state.FindModel(model) is { } m && Board.HasCover(state, m);

    public const string CoverKeyword = Board.CoverKeyword;

    // The answers that are not Blood Marker spends: free attacks on a retreating model, the friendly model a shot into
    // melee was forced onto, and where a missed SCATTER attack lands (roster stage R4c).
    private static Rejection? ValidateChoice(MatchState state, Command command)
    {
        var (kind, seat) = command switch
        {
            FreeFight c => (DecisionKind.FreeFight, c.Seat),
            DeclineFreeFights c => (DecisionKind.FreeFight, c.Seat),
            ChooseFriendlyTarget c => (DecisionKind.ChooseFriendlyTarget, c.Seat),
            ScatterAttack c => (DecisionKind.ChooseScatterPoint, c.Seat),
            UseTalisman c => (DecisionKind.UseTalisman, c.Seat),
            DeclineTalisman c => (DecisionKind.UseTalisman, c.Seat),
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
                if (!Board.FreeFighters(state, state.Retreat!).Any(m => m.Id == c.Attacker))
                {
                    return new(ReasonCode.NotAFreeFighter, "That model cannot attack the retreating model: it must be a standing enemy with a Melee Weapon within 1\" that has not attacked yet.", "1.0.2 p. 37");
                }
                return c.Weapon != 0 && (c.Weapon < 0 || state.Setup!.ProfileOf(c.Attacker)!.Weapons(AttackKind.Melee) <= c.Weapon)
                    ? new(ReasonCode.NoMeleeWeapon, "That model has no second Melee Weapon.", Board.RefMelee)
                    : null;
            case ChooseFriendlyTarget c:
                var progress = state.InProgress!;
                var shooter = state.FindModel(progress.Actor)!;
                var intended = Board.BodyOf(state, state.FindModel(progress.Target!.Value)!)!;
                return Board.FriendsNear(state, shooter, intended).Any(m => m.Id == c.Victim)
                    ? null
                    : new(ReasonCode.NotACandidate, "The shot must be aimed at one of your own models within 1\" of the target.", Board.RefIntoMelee);
            case ScatterAttack c:
                var scattering = state.InProgress!;
                return Board.CanScatterTo(state, scattering, c.At)
                    ? null
                    : new(ReasonCode.NotACandidate, $"The attack must scatter exactly {scattering.ScatterInches}\" from its target, to a point on the battlefield in Line of Sight of the target.", Board.RefScatter);
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
        if (c.Count < 0 || c.Count > MarkersAvailable(state, pending.Kind))
        {
            return new(ReasonCode.InvalidMarkerCount, "You can spend from none up to all of the model's Blood Markers.", RefMarkers);
        }
        // Bloodbath Roll (p. 47, U124): only on an Injury Roll, and only with the 6 markers (3 if Down) it costs.
        if (c.Bloodbath && (pending.Kind != DecisionKind.SpendMarkersOnInjuryRoll || c.Count < Reducer.BloodbathCost(state.FindModel(state.InProgress!.Target!.Value)!)))
        {
            return new(ReasonCode.InvalidMarkerCount, "A Bloodbath Roll is an Injury Roll that costs 6 Blood Markers, or 3 if the target is Down.", RefBloodbath);
        }
        return null;
    }

    // ---- executing ----

    private static void ExecuteBoard(Run run, Command command)
    {
        var state = run.State;
        switch (command)
        {
            case PlaceModel c:
                run.Emit(new ModelDeployed(c.Seat, c.Model, c.At));
                // Field Shrine (p. 82, U127): its player may deploy the shrine now, before the next model is set up.
                if (state.Setup!.ProfileOf(c.Model) is { FieldShrine: true })
                {
                    run.Emit(new DecisionRequested(DecisionKind.PlaceShrine, c.Seat));
                }
                break;
            case MoveModel c:
                var move = Board.CheckMove(state, state.FindModel(c.Model)!, c.Path);
                if (move.SideClimb is not null)
                {
                    DeclareClimb(run, c.Model, MoveKind.Move, c.Path);
                }
                else
                {
                    run.Emit(new ModelMoved(c.Model, MoveKind.Move, c.Path, move.Cost));
                }
                break;
            case TriggerMartyrdom c:
            {
                var (hit, close) = Board.MartyrdomHits(state, state.FindModel(c.Model)!);
                run.Emit(new MartyrdomTriggered(c.Model, hit, close));
                break;
            }
            case TakeSelfAction c:
                run.Emit(new SelfActionDeclared(c.Model, c.Action) { Target = c.Target });
                if (run.State.FindModel(c.Model)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            case TreatModel c:
                run.Emit(new TreatDeclared(c.Model, c.Target, c.Choice));
                if (run.State.FindModel(c.Model)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
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
                var facts = Board.Assess(state, attacker, state.FindModel(c.Target)!, c.Weapon);
                run.Emit(new RangedAttackDeclared(
                    c.Attacker,
                    c.Target,
                    facts.LongRange,
                    facts.InCover,
                    facts.CoverTerrainId,
                    facts.AttackerLevel,
                    facts.TargetLevel,
                    AttackDice.ShotPenalty(facts.LongRange, facts.InCover))
                { IntoMelee = facts.IntoMelee, Elevated = facts.Elevated, Weapon = c.Weapon });
                // A shot into melee asks about markers only once the D6 has settled who is shot (p. 42).
                if (attacker.BloodMarkers > 0 && !facts.IntoMelee && !Reducer.ShotsIgnoreMarkers(state, attacker.Id))
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            case ShootPoint c:
            {
                var aiming = state.FindModel(c.Attacker)!;
                var point = Board.AssessPoint(state, aiming, c.At, c.Weapon);
                run.Emit(new PointAttackDeclared(c.Attacker, c.At, point.LongRange) { Weapon = c.Weapon });
                if (aiming.BloodMarkers > 0 && !Reducer.ShotsIgnoreMarkers(state, aiming.Id))
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, c.Seat.Opponent()));
                }
                break;
            }
            case ChargeModel c:
                run.Emit(new ChargeDeclared(c.Model, c.Target) { CarryOn = c.CarryOn });
                break;
            case RetreatModel c:
                run.Emit(new RetreatDeclared(c.Model, c.Path));
                break;
            case FightModel c:
                DeclareMelee(run, state.FindModel(c.Model)!, state.FindModel(c.Target)!, free: false, c.Weapon, !ModelProfile.IsActionAttack(c.Weapon) && (NextFightAttack(state, c.Model)?.OffHand ?? false));
                break;
            case FreeFight c:
                DeclareMelee(run, state.FindModel(c.Attacker)!, state.FindModel(state.Retreat!.Model)!, free: true, c.Weapon, offHand: false);
                break;
            case DeclineFreeFights c:
                run.Emit(new FreeFightsDeclined(c.Seat));
                break;
            case UseTalisman:
                run.Emit(new TalismanUsed(state.ActiveModel!.Value));
                break;
            case DeclineTalisman:
                run.Emit(new ActivationEnded(state.ActiveModel!.Value, ActivationEndReason.RiskyFailure));
                break;
            case ScatterAttack c:
            {
                var scattering = state.InProgress!;
                var thrower = state.FindModel(scattering.Actor)!;
                run.Emit(new AttackScattered(thrower.Id, scattering.Target, c.At, scattering.ScatterInches, Board.BlastHitsAt(state, thrower, c.At, scattering.Weapon).ToImmutableArray()));
                break;
            }
            case ChooseFriendlyTarget c:
                var shooter = state.FindModel(state.InProgress!.Actor)!;
                var forced = Board.ForcedShotFacts(state, shooter, state.FindModel(c.Victim)!, state.InProgress.Weapon);
                run.Emit(new FriendlyTargetChosen(shooter.Id, c.Victim, forced.LongRange, forced.InCover, forced.CoverTerrainId) { Elevated = forced.Elevated });
                if (shooter.BloodMarkers > 0 && !Reducer.ShotsIgnoreMarkers(state, shooter.Id))
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
                    success ? MarkerSpendPurpose.ReduceSuccessRoll : c.Bloodbath ? MarkerSpendPurpose.Bloodbath : MarkerSpendPurpose.RaiseInjuryRoll));
                break;
        }
    }

    // A Melee Attack with its facts (roster stage R3: the weapon, Off-Hand and BLOCK), then the offer to spend the
    // attacker's Blood Markers.
    private static void DeclareMelee(Run run, ModelState attacker, ModelState target, bool free, int weapon, bool offHand)
    {
        var state = run.State;
        run.Emit(new MeleeAttackDeclared(attacker.Id, target.Id, free, Board.AssessMelee(state, attacker, target, weapon).CoverTerrainId)
        {
            Weapon = weapon,
            OffHand = offHand,
            Block = Board.Blocks(state, attacker.Id, target.Id),
            Fear = Board.Fears(state, attacker.Id, target.Id),
        });
        if (attacker.BloodMarkers > 0)
        {
            run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, attacker.Seat.Opponent()));
        }
    }

    // A free attack's next CLEAVE attack, at once against the retreating model (p. 37 with p. 53).
    private static bool AdvanceFreeAttack(Run run, MatchState s, MeleeAttacksLeft due)
    {
        DeclareMelee(run, s.FindModel(due.Attacker)!, s.FindModel(s.Retreat!.Model)!, free: true, due.Next[0].Weapon, offHand: false);
        return true;
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
                // Roster stage R5d (U108): unless the model keeps going after a failed Risky roll.
                if (run.State.MustEndActivation)
                {
                    EndForRisky(run, progress.Actor);
                }
                return true;
            }
            case RollStage.SelfRoll:
            {
                // Roster stage R6b (U118): an Aim or Fortify ACTION is a Risky Success Roll; spent markers are penalty dice.
                // Roster stage R6e (U121): Laying on of Hands only for a Shaken warband; Enforced Orthodoxy stands up the Down
                // friends within 8", Laying on of Hands removes 1 BLOOD MARKER (3 on a Critical).
                var roll = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, risky: Reducer.SelfActionRisky(s, actor, progress.SelfAction));
                var helped = roll.Outcome != SuccessOutcome.Failure;
                run.Emit(new SelfActionRolled(progress.Actor, progress.SelfAction, roll)
                {
                    Target = progress.Target,
                    StoodUp = helped && progress.SelfAction == SelfActionKind.EnforcedOrthodoxy ? Reducer.OrthodoxyStands(s, actor) : [],
                    Removed = helped && progress.SelfAction == SelfActionKind.LayingOnHands ? Reducer.HandsRemove(s, progress.Target!.Value, roll) : 0,
                });
                if (run.State.MustEndActivation)
                {
                    EndForRisky(run, progress.Actor);
                }
                return true;
            }
            case RollStage.TreatRoll:
            {
                // Roster stage R5h (U113): a Treat is a Risky Success Roll (p. 84); spent markers are penalty dice.
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, risky: true);
                var choice = progress.TreatStandUp ? TreatChoice.StandUp : TreatChoice.RemoveMarker;
                var treated = check.Outcome != SuccessOutcome.Failure && Reducer.TreatRefusal(s, actor, progress.Target!.Value, choice) is null;
                run.Emit(new TreatRolled(progress.Actor, progress.Target!.Value, choice, check, treated));
                if (run.State.MustEndActivation)
                {
                    EndForRisky(run, progress.Actor);
                }
                return true;
            }
            case RollStage.DashMove:
            {
                var dash = Board.CheckMove(s, actor, progress.DashPath);
                if (dash.SideClimb is not null)
                {
                    DeclareClimb(run, progress.Actor, MoveKind.Dash, progress.DashPath);
                }
                else
                {
                    run.Emit(new ModelMoved(progress.Actor, MoveKind.Dash, progress.DashPath, dash.Cost));
                }
                return true;
            }
            case RollStage.ClimbRoll:
            {
                // Climbing a sheer surface is always a Risky Success Roll (p. 39); spent markers are penalty dice.
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, risky: true);
                run.Emit(new SideClimbRolled(progress.Actor, check));
                if (run.State.MustEndActivation && run.State.InProgress is null)
                {
                    EndForRisky(run, progress.Actor);
                }
                return true;
            }
            case RollStage.ClimbMove:
            {
                var kind = progress.ClimbAction == ActionsTaken.Move ? MoveKind.Move : MoveKind.Dash;
                run.Emit(new ModelMoved(progress.Actor, kind, progress.DashPath, Board.CheckMove(s, actor, progress.DashPath).Cost));
                return true;
            }
            case RollStage.ClimbStop:
            {
                var kind = progress.ClimbAction == ActionsTaken.Move ? MoveKind.Move : MoveKind.Dash;
                var stop = Reducer.ClimbStopPath(s, progress);
                run.Emit(new ModelMoved(progress.Actor, kind, stop, Board.CheckMove(s, actor, stop).Cost));
                // A Mine Marker on the way halts the move first; the activation ends once the move is over.
                if (run.State.MineStop is null && run.State.MustEndActivation)
                {
                    EndForRisky(run, progress.Actor);
                }
                return true;
            }
            case RollStage.ShotRoll:
            {
                // A Shaken warband's Success Rolls are Risky (p. 50).
                // RISKY weapons too (roster stage R2, p. 55).
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice,
                    risky: AttackDice.Risky(s.Setup!.ProfileOf(actor.Id)!.Using(AttackKind.Ranged, progress.Weapon), AttackKind.Ranged, s.IsShaken(actor.Seat)));
                run.Emit(progress.Target is { } shotAt
                    ? new RangedSuccessRolled(progress.Actor, shotAt, check)
                    : new PointSuccessRolled(progress.Actor, progress.BlastAt!.Value, check));
                // A scattering attack (roster stage R4c) is resolved first; its activation ends once it is over.
                if (check.RiskyFailure && run.State.MustEndActivation && run.State.InProgress is null)
                {
                    EndForRisky(run, progress.Actor);
                }
                else if (check.Outcome != SuccessOutcome.Failure && run.State.InProgress is { Stage: RollStage.InjuryRoll }
                    && !progress.FriendlyFire && run.State.FindModel(progress.Target!.Value)!.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, actor.Seat));
                }
                return true;
            }
            case RollStage.Blast:
            {
                // Roster stage R4b (U102): who the blast hits; each then has its Injury Roll.
                if (progress.Target is not { } aimed)
                {
                    // Roster stage R5a (U105): an attack aimed at a point hits what is around the point.
                    var point = progress.BlastAt!.Value;
                    run.Emit(new PointBlastHit(actor.Id, point, Board.BlastHitsAt(s, actor, point, progress.Weapon).ToImmutableArray()));
                    return true;
                }
                var aimedAt = s.FindModel(aimed)!;
                run.Emit(new BlastHit(actor.Id, aimedAt.Id, Board.BlastHits(s, actor, aimedAt, progress.Weapon).ToImmutableArray()));
                return true;
            }
            case RollStage.Scatter:
            {
                // Roster stage R4c (U103): the attacker's opponent picks where the missed attack lands, or it misses when
                // it has nowhere to land.
                if (Board.ScatterPoints(s, progress).Count == 0)
                {
                    run.Emit(new ScatterMissed(actor.Id, progress.Target, progress.ScatterInches));
                }
                else
                {
                    run.Emit(new DecisionRequested(DecisionKind.ChooseScatterPoint, actor.Seat.Opponent()));
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
                else if (actor.BloodMarkers > 0 && !Reducer.ShotsIgnoreMarkers(s, actor.Id))
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, actor.Seat.Opponent()));
                }
                return true;
            }
            case RollStage.FightRoll:
            {
                var check = SuccessRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice,
                    risky: AttackDice.Risky(s.Setup!.ProfileOf(actor.Id)!.Using(AttackKind.Melee, progress.Weapon), AttackKind.Melee, s.IsShaken(actor.Seat)));
                run.Emit(new MeleeSuccessRolled(progress.Actor, progress.Target!.Value, check));
                if (check.RiskyFailure)
                {
                    // Only the active model has an activation to lose; a free attack that fails simply misses (U49).
                    if (run.State.MustEndActivation)
                    {
                        EndForRisky(run, progress.Actor);
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
                // Roster stage R6a (U117): Shock Charge rolls 1 extra D6 and keeps the highest.
                var bonus = Roll(dice);
                if (s.Setup!.ProfileOf(progress.Actor)!.ExtraChargeDie)
                {
                    var extra = Roll(dice);
                    run.Emit(new ChargeRolled(progress.Actor, progress.Target!.Value, Math.Max(bonus, extra)) { Dropped = Math.Min(bonus, extra) });
                }
                else
                {
                    run.Emit(new ChargeRolled(progress.Actor, progress.Target!.Value, bonus));
                }
                return true;
            }
            case RollStage.ChargeMove:
            {
                var plan = Board.PlanCharge(s, actor, s.FindModel(progress.Target!.Value)!, progress.ChargeDie, progress.CarryOn);
                run.Emit(new ChargeMoved(progress.Actor, progress.Target.Value, plan.Path, plan.Cost, plan.Reached));
                return true;
            }
            case RollStage.InjuryRoll:
            {
                var target = s.FindModel(progress.Target!.Value)!;
                // A blast's later models (U102): the offer to spend their Blood Markers comes before each roll.
                if (!progress.MarkersAsked && !progress.FriendlyFire && target.BloodMarkers > 0)
                {
                    run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnInjuryRoll, actor.Seat));
                    return true;
                }
                var weapon = Reducer.AttackProfile(s, progress);
                var modifiers = AttackDice.InjuryModifiers(weapon, s.Setup!.ProfileOf(target.Id)!, progress.Kind);
                // Roster stage R5j (U115): a Martyrdom Device's bearer rolls 4D6 added together.
                // Roster stage R5j (U115) and Bloodbath Rolls (p. 47, U124): a Bloodbath Roll keeps 3 dice, 4 with DEADLY.
                var deadly = (weapon.AttackKeywords(progress.Kind) & WeaponKeywords.Deadly) != 0;
                var injury = InjuryRoll.Roll(dice, progress.BonusDice, progress.PenaltyDice, modifiers, target.Status == ModelStatus.Down,
                    deadly: progress.Bloodbath ? !deadly : deadly, allFour: progress.Martyrdom && target.Id == actor.Id || progress.Bloodbath && deadly);
                var rolled = new InjuryRolled(actor.Id, target.Id, injury)
                {
                    Bloodbath = progress.Bloodbath,
                    ExtraMarkers = AttackDice.ExtraMarkers(weapon, progress.Kind, s.Setup.ProfileOf(target.Id)!),
                    Tough = Reducer.ToughSaves(s, target, injury.Outcome),
                    Spared = Reducer.MementoSaves(s, target, injury.Outcome),
                };
                rolled = rolled with { Warded = Reducer.Wards(s, target, rolled.ResultMarkers + rolled.ExtraMarkers) };
                run.Emit(rolled);
                if (rolled.Result == InjuryOutcome.OutOfAction && !Reducer.HasModelOnBattlefield(run.State, target.Seat))
                {
                    run.Emit(new MatchEnded(target.Seat.Opponent(), OutcomeReason.AllEnemiesOutOfAction));
                }
                else if (progress.Martyrdom && target.Id == actor.Id && run.State.FindModel(actor.Id)!.Status != ModelStatus.Standing
                    && run.State.ActiveModel == actor.Id)
                {
                    // Roster stage R5j (U115): the bearer taken Down or Out of Action by its own device: its activation ends (p. 49).
                    run.Emit(new ActivationEnded(actor.Id, ActivationEndReason.TakenDown));
                }
                return true;
            }
            default:
                throw new InvalidOperationException("Unknown roll stage.");
        }
    }

    // ---- Trench Warfare's Mine Markers (p. 162, U93) ----

    private const string RefMines = "1.0.2 pp. 161-162";

    private static Rejection? ValidateShrine(MatchState state, Command command)
    {
        var (seat, carrier) = command is PlaceShrine p ? (p.Seat, p.Carrier) : (((DeclineShrine)command).Seat, ((DeclineShrine)command).Carrier);
        if (state.Pending is not { Kind: DecisionKind.PlaceShrine } pending)
        {
            return new(ReasonCode.WrongDecision, "No Field Shrine is waiting to be deployed.", RefShrine);
        }
        if (pending.Seat != seat)
        {
            return new(ReasonCode.NotYourTurn, "That decision belongs to the other player.", RefShrine);
        }
        if (Board.ShrineCarrierWaiting(state, seat)?.Id != carrier)
        {
            return new(ReasonCode.UnknownModel, "That is not the model whose Field Shrine is waiting.", RefShrine);
        }
        if (command is PlaceShrine place && Board.CheckShrine(state, seat, place.At) is { IsLegal: false } refusal)
        {
            return new(ReasonCode.PlacementIllegal, refusal.Text, RefShrine);
        }
        return null;
    }

    private static Rejection? ValidateMines(MatchState state, LayMines c)
    {
        if (Board.MinePlacementArea(state) is not { } area)
        {
            return new(ReasonCode.MinesIllegal, "This battlefield has no Mine Placement Area.", RefMines);
        }
        if (c.Markers.IsDefault)
        {
            return new(ReasonCode.MinesIllegal, $"The defender sets up {Mines.Count} Mine Markers.", RefMines);
        }
        if (Mines.Problem(Board.ScenarioOf(state)!.Battlefield, area, c.Markers) is { } problem)
        {
            return new(ReasonCode.MinesIllegal, problem, RefMines);
        }
        var ids = Enumerable.Range(0, Mines.Count).Select(Mines.IdOf).ToHashSet(StringComparer.Ordinal);
        if (c.Duds.IsDefault || c.Duds.Length != Mines.Duds || c.Duds.Distinct(StringComparer.Ordinal).Count() != Mines.Duds || c.Duds.Any(d => d is null || !ids.Contains(d)))
        {
            return new(ReasonCode.MinesIllegal, $"Name {Mines.Duds} different Mine Markers as the duds.", RefMines);
        }
        return null;
    }

    // A legal layout spread across the Mine Placement Area: every so many points of Mines.Grid.
    private static ImmutableArray<Point2> SuggestedMines(MatchState state)
    {
        var grid = Mines.Grid(Board.ScenarioOf(state)!.Battlefield, Board.MinePlacementArea(state)!.Value);
        return grid.Length < Mines.Count ? [] : [.. Enumerable.Range(0, Mines.Count).Select(i => grid[(int)((long)i * grid.Length / Mines.Count)])];
    }

    // A move halted at a Mine Marker: reveal it, detonate a live one, then carry on (p. 162). Only a view that knows the
    // duds (the server's) can decide this.
    private static bool AdvanceMine(Run run, MatchState s, MineStop stop, IDiceSource dice)
    {
        var model = s.FindModel(stop.Model)!;
        if (!stop.Revealed && !stop.DefuseTried && s.Setup!.ProfileOf(model.Id)!.DefusesMines)
        {
            run.Emit(new MineDefuseRolled(model.Id, stop.Current.Marker, SuccessRoll.Roll(dice, 0, 0, risky: true)));
            return true;
        }
        if (!stop.Revealed)
        {
            if (!s.DudsKnown)
            {
                throw new InvalidOperationException("Only a view of the match that knows the duds can reveal a Mine Marker.");
            }
            run.Emit(new MineRevealed(stop.Current.Marker, stop.Model, live: !s.Duds.Contains(stop.Current.Marker)));
            return true;
        }
        if (stop.Live)
        {
            var armour = s.Setup!.ProfileOf(model.Id)!.Armour;
            var injury = InjuryRoll.Roll(dice, 0, 0, [armour], model.Status == ModelStatus.Down);
            var exploded = new MineExploded(model.Id, stop.Current.Marker, injury)
            {
                Negated = s.Setup.ProfileOf(model.Id)!.Negates.HasFlag(WeaponKeywords.Shrapnel),
                Tough = Reducer.ToughSaves(s, model, injury.Outcome),
                Spared = Reducer.MementoSaves(s, model, injury.Outcome),
            };
            // Roster stage R5e (U109): the Sacrificial Lamb's blood keeps off the first BLOOD MARKER.
            run.Emit(exploded with { Warded = Reducer.Wards(s, model, exploded.ResultMarkers + exploded.ExtraMarkers()) });
            var hurt = run.State.FindModel(model.Id)!;
            if (hurt.Status == ModelStatus.OutOfAction && !Reducer.HasModelOnBattlefield(run.State, model.Seat))
            {
                run.Emit(new MatchEnded(model.Seat.Opponent(), OutcomeReason.AllEnemiesOutOfAction));
            }
            else if (hurt.Status != ModelStatus.Standing)
            {
                // Taken Down or Out of Action during its own activation: the activation ends (p. 49).
                run.Emit(new ActivationEnded(model.Id, ActivationEndReason.TakenDown));
            }
            return true;
        }
        var next = stop.Index + 1 < stop.Stops.Length ? stop.Stops[stop.Index + 1] : null;
        run.Emit(new MoveResumed(model.Id, Mines.Between(stop.Path, stop.Current, next)));
        if (run.State.MustEndActivation)
        {
            // The move was a failed side climb's (U75): its activation ends now the move is over.
            EndForRisky(run, model.Id);
        }
        return true;
    }

    // A Move or Dash that climbs a tower's side: the roll waits for the opponent's Blood Marker decision, as a Dash does.
    private static void DeclareClimb(Run run, ModelId model, MoveKind kind, ImmutableArray<Point2> path)
    {
        run.Emit(new SideClimbDeclared(model, kind, path));
        var climber = run.State.FindModel(model)!;
        if (climber.BloodMarkers > 0)
        {
            run.Emit(new DecisionRequested(DecisionKind.SpendMarkersOnSuccessRoll, climber.Seat.Opponent()));
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
