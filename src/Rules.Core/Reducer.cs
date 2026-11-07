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
        state = ForgoAttacks(state, e);
        // Roster stage R13 (U138): a roll is Charmed exactly when its roller is under a Charm of Acedia.
        var charm = CharmedRoll(e);
        if (charm is { } roll)
        {
            Require(roll.Check.Charmed == (state.CharmedModel == roll.Roller), "A Success Roll is a Charm of Acedia's exactly when its roller is under one.");
        }

        var next = e switch
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
            PointAttackDeclared x => OnPointAttackDeclared(state, x),
            PointSuccessRolled x => OnPointSuccessRolled(state, x),
            PointBlastHit x => OnPointBlastHit(state, x),
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
            BlastHit x => OnBlastHit(state, x),
            AttackScattered x => OnAttackScattered(state, x),
            ScatterMissed x => OnScatterMissed(state, x),
            FriendlyTargetChosen x => OnFriendlyTargetChosen(state, x),
            AttackerChosen x => OnAttackerChosen(state, x),
            TrenchesScored x => OnTrenchesScored(state, x),
            GameLengthRolled x => OnGameLengthRolled(state, x),
            MinesLaid x => OnMinesLaid(state, x),
            ShrinePlaced x => OnShrinePlaced(state, x),
            ShrineDeclined x => OnShrineDeclined(state, x),
            MineRevealed x => OnMineRevealed(state, x),
            MineDefuseRolled x => OnMineDefuseRolled(state, x),
            MineExploded x => OnMineExploded(state, x),
            MoveResumed x => OnMoveResumed(state, x),
            GloriousDeedCompleted x => OnDeedCompleted(state, x),
            BloodMarkersPlaced x => OnBloodMarkersPlaced(state, x),
            TreatDeclared x => OnTreatDeclared(state, x),
            TreatRolled x => OnTreatRolled(state, x),
            SelfActionDeclared x => OnSelfActionDeclared(state, x),
            SelfActionRolled x => OnSelfActionRolled(state, x),
            MartyrdomTriggered x => OnMartyrdomTriggered(state, x),
            TalismanUsed x => OnTalismanUsed(state, x),
            BodyguardAnswered x => OnBodyguardAnswered(state, x),
            BaseTurned x => OnBaseTurned(state, x),
            ModelsMoved x => OnModelsMoved(state, x),
            SpellCast x => OnSpellCast(state, x),
            _ => throw new InvalidEventException($"Unknown event type {e.GetType().Name}."),
        };
        // Roster stage R5f (U110): a dug-in model that has moved away from where it started is dug in no more.
        next = Undig(next, e);
        // Roster stage R5g (U112): BLOOD MARKERS this event owes the enemies near a Crown or Damned Pact bearer.
        next = Burns(state, e, next);
        if (charm is { Check.Charmed: true })
        {
            next = next with { CharmedModel = null };
        }
        // Roster stage R14 (U139): a Blood Magic helps one attack: it is spent when that attack's rolls are all done.
        if (state.BloodMagicModel is { } caster && state.InProgress is { Actor: var rolling, Stage: RollStage.ShotRoll or RollStage.InjuryRoll or RollStage.TargetRoll or RollStage.TargetChoice or RollStage.FightRoll or RollStage.Blast or RollStage.Scatter }
            && rolling == caster && next.InProgress is null)
        {
            next = next with { BloodMagicModel = null };
        }
        // Roster stage R14 (U139): a Black Heart is used up by its caster's next Success Roll.
        if (charm is { } heart && state.BlackHeartModel == heart.Roller)
        {
            next = next with { BlackHeartModel = null };
        }
        // Glorious Deeds (U96): what the deeds need to know, and any deed this event completed.
        return e is GloriousDeedCompleted ? next : Deeds.Track(state, e, next);
    }

    // Roster stage R14 (U139): Black Heart adds 1 DICE to its caster's next Success Roll; Blood Magic adds 1 INJURY DICE to the
    // Injury Rolls of its caster's next attack (cleared with the activation if unused).
    public static int HeartDice(MatchState state, ModelId roller) => state.BlackHeartModel == roller ? 1 : 0;

    public static int MagicDice(MatchState state, ModelId attacker) => state.BloodMagicModel == attacker ? 1 : 0;

    // Roster stage R13 (U138): the Success Rolls a Charm of Acedia can make a Success: those of an ACTION (not a Morale Check or a mine).
    private static (ModelId Roller, SuccessRollResult Check)? CharmedRoll(GameEvent e) => e switch
    {
        DashRolled x => (x.Model, x.Check),
        SideClimbRolled x => (x.Model, x.Check),
        RangedSuccessRolled x => (x.Attacker, x.Check),
        PointSuccessRolled x => (x.Attacker, x.Check),
        MeleeSuccessRolled x => (x.Attacker, x.Check),
        TreatRolled x => (x.Model, x.Check),
        SelfActionRolled x => (x.Model, x.Check),
        _ => null,
    };

    // A move that leaves the spot counts even when it ends there again.
    private static MatchState Undig(MatchState state, GameEvent e)
    {
        var walked = e switch
        {
            ModelMoved m => (m.Model, m.Path),
            ChargeMoved c => (c.Model, c.Path),
            RetreatResolved r => (r.Model, r.Path),
            MoveResumed r => (r.Model, r.Path),
            _ => ((ModelId?)null, ImmutableArray<Point2>.Empty),
        };
        bool Away(ModelState m) => m.DugInAt is { } at && (m.Position != at || walked.Item1 == m.Id && !walked.Item2.IsDefault && walked.Item2.Any(p => p != at));
        return state.Models.Any(Away)
            ? state with { Models = state.Models.Select(m => Away(m) ? m with { DugInAt = null } : m).ToImmutableArray() }
            : state;
    }

    // Roster stage R3 (U100): the Melee Attacks still to come of a Fight or free attack are made one after another;
    // anything else that happens ends them (the active model chose to do something else, or the action is over).
    private static MatchState ForgoAttacks(MatchState state, GameEvent e) =>
        state.AttacksLeft is not null && e is not (MeleeAttackDeclared or MeleeSuccessRolled or InjuryRolled or MarkersSpent or DecisionRequested or GloriousDeedCompleted or BloodMarkersPlaced or MatchEnded)
            ? state with { AttacksLeft = null }
            : state;

    // The CLEAVE free attack that must come next: the free attacker has attacks left, nothing is waiting, and the
    // retreating model is still on the battlefield (p. 37 with p. 53).
    internal static MeleeAttacksLeft? FreeAttackDue(MatchState state) =>
        state.AttacksLeft is { Free: true, Next.Length: > 0 } left && state.InProgress is null && state.Pending is null
        && state.Retreat is { } retreat && state.FindModel(retreat.Model) is { Position: not null, Status: not ModelStatus.OutOfAction }
            ? left
            : null;

    // The Melee Attacks a Fight or free attack has after its first: the rest of the first weapon's CLEAVE attacks, then
    // (a Fight with two Melee Weapons) the other weapon's, with the Off-Hand -1 DICE (p. 45).
    internal static ImmutableArray<MeleeAttackSlot> AttacksAfterFirst(ModelProfile profile, int weapon, bool free)
    {
        var next = ImmutableArray.CreateBuilder<MeleeAttackSlot>();
        for (var i = 1; i < profile.MeleeAttacksWith(weapon); i++)
        {
            next.Add(new MeleeAttackSlot(weapon, false));
        }
        if (!free && profile.Weapons(AttackKind.Melee) == 2)
        {
            var other = 1 - weapon;
            for (var i = 0; i < profile.MeleeAttacksWith(other); i++)
            {
                next.Add(new MeleeAttackSlot(other, true));
            }
        }
        return next.ToImmutable();
    }

    // The weapon an attack in progress is made with (roster stage R3). A Martyrdom Device's detonation (U115) has no
    // weapon: no injury modifier or keywords of its own.
    internal static ModelProfile AttackProfile(MatchState state, RollInProgress progress) => progress.Martyrdom
        ? new ModelProfile(state.Setup!.ProfileOf(progress.Actor)!.Movement, state.Setup.ProfileOf(progress.Actor)!.BaseRadius, Length.Zero)
        : state.Setup!.ProfileOf(progress.Actor)!.Using(progress.Kind, progress.Weapon);

    // Roster stage R5j (U115): a Trigger Martyrdom Device ACTION, once per game: the models within 3" (itself last) and
    // the others within 1", worked out again from the positions; then the first one's Injury Roll.
    private static MatchState OnMartyrdomTriggered(MatchState state, MartyrdomTriggered e)
    {
        var model = Active(state, e.Model, "A Martyrdom Device");
        Require(state.InProgress is null && state.Retreat is null, "A Martyrdom Device waits for the last action to finish.");
        Require(MartyrdomRefusal(state, model) is null, "Only a model with a Martyrdom Device not yet detonated can trigger it, standing on the battlefield.");
        var (hit, close) = Board.MartyrdomHits(state, model);
        Require(hit.SequenceEqual(e.Hit) && close.SequenceEqual(e.Close), "The device hits exactly the models within 3\" (its bearer last); +1 INJURY DICE for the others within 1\".");
        var spent = TurnRules.WithModel(state, model with { MartyrdomSpent = true }) with { Actions = state.Actions | ActionsTaken.Trigger, AttacksLeft = null };
        var progress = new RollInProgress(RollStage.InjuryRoll, e.Model, Martyrdom: true, MartyrClose: e.Close);
        return spent with { InProgress = NextBlastRoll(spent, progress, e.Hit) };
    }

    // Why the model cannot trigger its Martyrdom Device now (Decide's check, in the rules' words), or null.
    internal static string? MartyrdomRefusal(MatchState state, ModelState model) =>
        state.Setup?.ProfileOf(model.Id)?.Martyrdom != true ? "Only a model with a Martyrdom Device can take a Trigger Martyrdom Device ACTION."
        : model.MartyrdomSpent ? "The Martyrdom Device has already detonated; it cannot be used again."
        : model.Position is null || model.Status != ModelStatus.Standing ? "A Down model cannot take a Trigger Martyrdom Device ACTION."
        : null;

    // A Glorious Deed is announced right after the event that completed it, in the order they were completed, and gives
    // its seat 1 Victory Point (pp. 149-150; counted at once, U96).
    private static MatchState OnDeedCompleted(MatchState state, GloriousDeedCompleted e)
    {
        Require(state.Deeds.Owed is [var owed, ..] && owed == new CompletedDeed(e.Deed, e.Seat, e.Model), "No such Glorious Deed was just completed.");
        var seat = state.SeatFor(e.Seat);
        var next = TurnRules.WithSeat(state, e.Seat, seat with { VictoryPoints = seat.VictoryPoints + Deeds.VictoryPoints });
        return next with { Deeds = state.Deeds with { Owed = state.Deeds.Owed.RemoveAt(0), Completed = state.Deeds.Completed.Add(state.Deeds.Owed[0]) } };
    }

    // Some states leave exactly one event that can come next, because the rules give nobody a choice: the match is
    // over, a roll-off has to be used, a Risky roll failed, a Down model stands, a Morale result takes effect, or the
    // opponent must be asked about Blood Markers. Decide always emits that event; a log that does anything else was
    // edited. Everything here is worked out from the state, except the two facts it cannot show (MustEndActivation,
    // SeatState.MustFlee) and the roll-off result.
    private static void RequireFollowUp(MatchState state, GameEvent e)
    {
        if (!state.Deeds.Owed.IsEmpty)
        {
            Require(e is GloriousDeedCompleted, "A Glorious Deed was just completed; it is announced before anything else happens.");
            return;
        }
        if (state.ForcedMovesOwed is { } forced)
        {
            Require(e is ModelsMoved moved && moved.Source == forced.Source, "A Loudspeakers or Puppet Master ACTION succeeded; the models it moves are moved before anything else happens.");
            return;
        }
        if (!state.MarkersOwed.IsDefaultOrEmpty)
        {
            Require(e is BloodMarkersPlaced, "BLOOD MARKERS are owed to the enemies near a Crown of Hellfire or Damned Pact; they are placed before anything else happens.");
            return;
        }
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
            var next = !stop.Revealed ? e is MineRevealed || (e is MineDefuseRolled && !stop.DefuseTried) : stop.Live ? e is MineExploded : e is MoveResumed;
            Require(next, "A move halted at a Mine Marker: the marker is revealed, a live mine detonates, and the model carries on.");
            return;
        }

        if (state.MustEndActivation)
        {
            // Roster stage R5k (U116): first the player of a model with a Talisman left is asked whether it uses one.
            if (state.Pending is { Kind: DecisionKind.UseTalisman })
            {
                Require(e is TalismanUsed used && used.Model == state.ActiveModel
                    || e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure } declined && declined.Model == state.ActiveModel,
                    "After a failed Risky roll the model either uses a Talisman or its activation ends.");
                return;
            }
            if (TalismanReady(state, state.ActiveModel))
            {
                Require(e is DecisionRequested { Kind: DecisionKind.UseTalisman } asked && asked.Seat == state.FindModel(state.ActiveModel!.Value)!.Seat,
                    "A model with a Talisman left is asked whether it uses one after a failed Risky roll.");
                return;
            }
            Require(e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure } lost && lost.Model == state.ActiveModel,
                "A failed Risky roll ends the activation at once.");
            return;
        }

        if (state.ReloadEnds)
        {
            Require(e is ActivationEnded { Reason: ActivationEndReason.Reload } reloaded && reloaded.Model == state.ActiveModel,
                "An attack with a RELOAD weapon ends the activation once its ACTION is done.");
            return;
        }

        if (FreeAttackDue(state) is { } due)
        {
            Require(e is MeleeAttackDeclared { FreeFight: true } next && next.Attacker == due.Attacker,
                "A free attack's CLEAVE attacks are made one after another.");
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

        if (state.InProgress is { Stage: RollStage.Scatter } && state.Pending is null)
        {
            // Roster stage R4c (U103): a missed SCATTER attack scatters at once, or misses when it has nowhere to land.
            Require(e is DecisionRequested { Kind: DecisionKind.ChooseScatterPoint } or ScatterMissed, "A missed SCATTER attack scatters at once.");
            return;
        }

        if (BodyguardOfferOwed(state) is { } guardOwed)
        {
            Require(e is DecisionRequested { Kind: DecisionKind.UseBodyguard } guardOffer && guardOffer.Seat == guardOwed.Seat,
                "The player of the model that was hit must be asked about its Bodyguard first.");
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

    // Roster stage R7d (U131): after a hit that was not a BLAST's and before its Injury Roll, the hit model's player is asked
    // whether a Bodyguard beside it takes the hit. GuardOwed is set only when there is one to ask.
    private static PendingDecision? BodyguardOfferOwed(MatchState state) =>
        state.Pending is null && state.InProgress is { Stage: RollStage.InjuryRoll, GuardOwed: true, Target: { } hit }
        && state.FindModel(hit) is { } hitModel && Board.Bodyguards(state, hitModel).Count > 0
            ? new PendingDecision(DecisionKind.UseBodyguard, hitModel.Seat)
            : null;

    // Roster stage R7d (U131): the answer. The Bodyguard becomes the target, and the Injury Roll's dice are worked out for it.
    private static MatchState OnBodyguardAnswered(MatchState state, BodyguardAnswered e)
    {
        Require(state.Pending is { Kind: DecisionKind.UseBodyguard } && state.InProgress is { Stage: RollStage.InjuryRoll, GuardOwed: true } p && p.Target == e.Target,
            "No Bodyguard offer is waiting for that model.");
        var progress = state.InProgress!;
        if (e.Guard is not { } guard)
        {
            return state with { Pending = null, InProgress = progress with { GuardOwed = false } };
        }
        var offered = Board.Bodyguards(state, state.FindModel(e.Target)!);
        Require(offered.Any(g => g.Id == guard), "That model cannot take the hit: it must be a standing friend within 1\" with Bodyguard and the same PILGRIM or CULT keyword.");
        var guardModel = offered.First(g => g.Id == guard);
        var profile = AttackProfile(state, progress);
        var melee = progress.Kind == AttackKind.Melee;
        var (bonus, penalty) = AttackDice.InjuryDice(profile, progress.Kind, progress.Critical, melee && guardModel.Status == ModelStatus.Down,
            melee ? FinishDice(state, progress, guard) : progress.InjuryDiceLater, state.Setup!.ProfileOf(guard)!);
        return state with { Pending = null, InProgress = progress with { Target = guard, GuardOwed = false, BonusDice = bonus, PenaltyDice = penalty } };
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
        if (progress.Stage is RollStage.DashRoll or RollStage.ShotRoll or RollStage.FightRoll or RollStage.ClimbRoll or RollStage.TreatRoll or RollStage.SelfRoll && actor.BloodMarkers > 0
            && !(progress.Stage == RollStage.ShotRoll && ShotsIgnoreMarkers(state, actor.Id)))
        {
            return new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, actor.Seat.Opponent());
        }
        if (progress.Stage == RollStage.InjuryRoll && !progress.FriendlyFire && progress.Target is { } target
            && state.FindModel(target) is { Markers: > 0 })
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
                Require(rolling is { Stage: RollStage.DashRoll or RollStage.ShotRoll or RollStage.FightRoll or RollStage.ClimbRoll or RollStage.TreatRoll or RollStage.SelfRoll, MarkersAsked: false }
                    && state.FindModel(rolling.Actor) is { Markers: > 0 } holder
                    && holder.Seat.Opponent() == e.Seat, "Only the rolling model's opponent may spend its markers, once, before a Success Roll.");
                Require(!(rolling!.Stage == RollStage.ShotRoll && ShotsIgnoreMarkers(state, rolling.Actor)), "No BLOOD MARKERS are spent on the Ranged Attacks of a model with Absolute Faith.");
                state = state with { InProgress = rolling! with { MarkersAsked = true } };
                break;
            case DecisionKind.SpendMarkersOnInjuryRoll:
                var injuring = state.InProgress;
                // U52: nobody spends a friendly model's markers on a forced shot's Injury Roll.
                Require(injuring is { Stage: RollStage.InjuryRoll, MarkersAsked: false, FriendlyFire: false, Target: { } victim }
                    && state.FindModel(injuring.Actor) is { } shooter && shooter.Seat == e.Seat
                    && state.FindModel(victim) is { Markers: > 0 }, "Only the attacker may spend the target's markers, once, before an Injury Roll.");
                state = state with { InProgress = injuring! with { MarkersAsked = true } };
                break;
            case DecisionKind.ChooseScatterPoint:
                // Roster stage R4c (U103): the attacker's opponent picks where a missed SCATTER attack lands, when it can land.
                var scattering = state.InProgress;
                Require(scattering is { Stage: RollStage.Scatter } && state.FindModel(scattering.Actor) is { } thrower && thrower.Seat.Opponent() == e.Seat
                    && Board.ScatterPoints(state, scattering).Count > 0, "The attacker's opponent picks where a missed SCATTER attack lands, when it can land somewhere.");
                break;
            case DecisionKind.LayMines:
                Require(TrenchWarfare.Is(state.Setup) && state.Phase == Phase.Deployment && state.Attacker is { } attacking && attacking.Opponent() == e.Seat
                    && !state.MinesLaid && state.DeploymentFirst is null, "The defender sets up the Mine Markers once, after the roles are chosen and before deployment.");
                break;
            case DecisionKind.PlaceShrine:
                // Field Shrine (p. 82, U127): right after a deployed model that carries one, and before the next deployment.
                Require(state.Phase == Phase.Deployment && Board.ShrineCarrierWaiting(state, e.Seat) is not null,
                    "A Field Shrine is offered to the player of a model that carries one, once it is deployed.");
                break;
            case DecisionKind.FreeFight:
                Require(state.Retreat is { Closed: false } retreat && state.InProgress is null && state.Phase == Phase.Activation
                    && state.FindModel(retreat.Model) is { } retreater && retreater.Seat.Opponent() == e.Seat
                    && Board.FreeFighters(state, retreat).Count > 0, "Free attacks are offered to the opponent of a retreating model while someone can still attack.");
                break;
            case DecisionKind.UseTalisman:
                Require(state.MustEndActivation && TalismanReady(state, state.ActiveModel) && state.FindModel(state.ActiveModel!.Value)!.Seat == e.Seat,
                    "Only the player of an active model with a Talisman left is asked, after its failed Risky roll.");
                break;
            case DecisionKind.UseBodyguard:
                Require(BodyguardOfferOwed(state) is { } guardOffer && guardOffer.Seat == e.Seat, "Only the player of a model that was hit, with a Bodyguard beside it, is asked.");
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
            Require(Enum.IsDefined(e.Axis) && (e.Axis == BaseAxis.Y || state.Setup!.ProfileOf(e.Model)!.IsOval), "Only an oval base has an axis to choose.");
            Require(e.Position is { } at && Board.CheckPlacement(state, e.Seat, model!, at, e.Axis).IsLegal, "A model must be set up at a legal point in its zone.");
        }
        // Roster stage R5f (U110): a model with a Shovel set up on Open terrain is dug in there.
        var dug = e.Position is { } start && state.Setup!.ProfileOf(e.Model)!.DigsIn && Board.OnOpenTerrain(state, start) ? e.Position : null;
        return TurnRules.WithModel(state, model! with { Deployed = true, Position = e.Position, DugInAt = dug, Axis = e.Axis }) with { LastDeployer = e.Seat };
    }

    // Roster stage R11 (U136): a spell is cast between the active model's actions: the payment comes off first, then the effect.
    private static MatchState OnSpellCast(MatchState state, SpellCast e)
    {
        var caster = Active(state, e.Model, "A spell");
        Require(state.InProgress is null && state.Retreat is null && state.MineStop is null && !state.MustEndActivation && !state.ReloadEnds, "A spell is cast between actions, not while one is being resolved.");
        Require(Board.SpellRefusal(state, caster, e.Spell, e.Payment, e.Target, e.TakeBlessing) is null, "That spell cannot be cast like that.");
        var next = state with { SpellsCast = state.SpellsCast | e.Spell };
        foreach (var p in e.Payment)
        {
            var payer = next.FindModel(p.Model)!;
            next = TurnRules.WithModel(next, payer with { BloodMarkers = payer.BloodMarkers - p.Count });
        }
        var total = e.Payment.Sum(p => p.Count);
        // Roster stage R13 (U138): the caster's next Success Roll is a Success.
        if (e.Spell == SpellKind.CharmOfAcedia)
        {
            return next with { CharmedModel = e.Model };
        }
        // Roster stage R14 (U139): Black Heart and Blood Magic are free spells with no target, cast ahead of the roll they help.
        if (e.Spell == SpellKind.BlackHeart)
        {
            return next with { BlackHeartModel = e.Model };
        }
        if (e.Spell == SpellKind.BloodMagic)
        {
            return next with { BloodMagicModel = e.Model };
        }
        // Roster stage R15 (U140): Shadow-walker waits for the Retreat; Call of the Flesh waits for the next enemy to activate.
        if (e.Spell == SpellKind.ShadowWalker)
        {
            return next with { ShadowWalking = e.Model };
        }
        if (e.Spell == SpellKind.CallOfTheFlesh)
        {
            return next with { CallTarget = e.Model, CallActive = false };
        }
        var target = next.FindModel(e.Target!.Value)!;
        caster = next.FindModel(e.Model)!;
        switch (e.Spell)
        {
            case SpellKind.WhatIsYoursIsMine:
                next = TurnRules.WithModel(next, e.TakeBlessing ? target with { BlessingMarkers = target.BlessingMarkers - 1 } : target with { BloodMarkers = target.BloodMarkers - 1 });
                caster = next.FindModel(e.Model)!;
                return TurnRules.WithModel(next, e.TakeBlessing ? caster with { BlessingMarkers = caster.BlessingMarkers + 1 } : caster with { BloodMarkers = caster.BloodMarkers + 1 });
            case SpellKind.ExquisitePain:
                return TurnRules.WithModel(next, target with { BloodMarkers = target.BloodMarkers + total });
            default:
                var (here, there) = (caster.Position, target.Position);
                next = TurnRules.WithModel(next, caster with { Position = there });
                return TurnRules.WithModel(next, target with { Position = here });
        }
    }

    // Roster stage R9a (U134): the models a successful Loudspeakers or Puppet Master ACTION moved, each to where the rules put it.
    private static MatchState OnModelsMoved(MatchState state, ModelsMoved e)
    {
        Require(state.ForcedMovesOwed is { } owed && owed.Source == e.Source, "No forced moves are owed.");
        var owed2 = state.ForcedMovesOwed!;
        var source = state.FindModel(e.Source)!;
        if (owed2.Action == SelfActionKind.Loudspeakers)
        {
            Require(e.Die == 0 && e.Moves.SequenceEqual(Board.LoudspeakerMoves(state, source)), "A Loudspeakers ACTION moves exactly the friends the rules say, to where the rules put them.");
        }
        else
        {
            Require(e.Die is >= 1 and <= 6 && state.FindModel(owed2.Target!.Value) is { } victim && owed2.Toward is { } toward
                && e.Moves.SequenceEqual([Board.PuppetMove(state, victim, toward, e.Die)]), "A Puppet Master ACTION moves its one model D6\" in a straight line, as far as the line is clear.");
        }
        var next = state with { ForcedMovesOwed = null };
        foreach (var move in e.Moves)
        {
            var model = next.FindModel(move.Model)!;
            if (model.Position != move.To)
            {
                next = TurnRules.WithModel(next, model with { Position = move.To });
            }
        }
        return next;
    }

    // Roster stage R8d (U133): the active model turns its oval base in place, a free pivot between its ACTIONS.
    private static MatchState OnBaseTurned(MatchState state, BaseTurned e)
    {
        Require(Enum.IsDefined(e.Axis), "Unknown base axis.");
        Require(state.Phase == Phase.Activation && state.ActiveModel == e.Model && state.Pending is null && state.InProgress is null
            && state.Retreat is null && state.MineStop is null && !state.MustEndActivation && !state.ReloadEnds, "A base is turned by the activating model between its actions.");
        var model = state.FindModel(e.Model)!;
        Require(model.Status == ModelStatus.Standing, "A Down model cannot turn its base.");
        Require(Board.TurnProblem(state, model, e.Axis) is null, "That base cannot be turned to that axis there.");
        return TurnRules.WithModel(state, model with { Axis = e.Axis });
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
        // Roster stage R10 (U135): a model with any INFECTION MARKER gains 1 more when it activates (Infection Markers, p. 128).
        var spread = model!.InfectionMarkers > 0 ? Math.Min(BloodMarkers.Max, model.InfectionMarkers + 1) : 0;
        return TurnRules.WithModel(state, model with { ActivatedThisTurn = true, Hidden = false, InfectionMarkers = spread }) with
        {
            ActiveModel = e.Model,
            LastActivator = e.Seat,
            Actions = ActionsTaken.None,
            Aiming = false,
            CounterCharging = false,
            MovementHalved = false,
            SpellsCast = SpellKind.None,
            ShadowWalking = null,
            // Roster stage R15 (U140): the next enemy to activate after a Call of the Flesh is the one called.
            CallActive = state.CallActive || state.CallTarget is { } caller && state.FindModel(caller)?.Seat != e.Seat,
        };
    }

    private static MatchState OnActivationEnded(MatchState state, ActivationEnded e)
    {
        Require(state.Phase == Phase.Activation && state.ActiveModel == e.Model, "That model is not the one activating.");
        Require(Enum.IsDefined(e.Reason), "Unknown end reason.");
        // A Talisman's player may let the activation end instead of using one (roster stage R5k).
        var declined = state.Pending is { Kind: DecisionKind.UseTalisman } && e.Reason == ActivationEndReason.RiskyFailure;
        Require(state.InProgress is null && (state.Pending is null || declined) && state.Retreat is null && state.MineStop is null, "An action is still being resolved.");
        if (e.Reason == ActivationEndReason.TakenDown)
        {
            Require(state.FindModel(e.Model)!.Status != ModelStatus.Standing, "An activation ends as taken Down only when the model is Down or Out of Action.");
        }
        if (e.Reason == ActivationEndReason.RiskyFailure)
        {
            Require(state.MustEndActivation, "An activation ends for a failed Risky roll only after one.");
        }
        if (e.Reason == ActivationEndReason.Reload)
        {
            Require(state.ReloadEnds, "An activation ends for RELOAD only after an attack with a RELOAD weapon.");
        }
        return state with { ActiveModel = null, Actions = ActionsTaken.None, MovementHalved = false, MustEndActivation = false, ReloadEnds = false, ShotWeapon = 0, Pending = null, Aiming = false, CounterCharging = false, BlackHeartModel = null, BloodMagicModel = null, ShadowWalking = null, CallTarget = state.CallActive ? null : state.CallTarget, CallActive = false };
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
        Require(e.Check.NetDice == TurnRules.LeaderDice(state, e.Seat) && !e.Check.Risky, "The Morale Check's dice do not match the state.");
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
        return state with { TurnEnded = true, ChargedThisTurn = [] };
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
        // Roster stage R6c (U119): Frightening Speed keeps its Movement whole.
        var quick = state.Setup!.ProfileOf(e.Model)!.QuickToRise;
        Require(e.Quick == quick, "A model's Movement is halved when it stands up, unless it has Frightening Speed.");
        return TurnRules.WithModel(state, model with { Status = ModelStatus.Standing }) with { MovementHalved = !quick };
    }

    private static MatchState OnDashDeclared(MatchState state, DashDeclared e)
    {
        var model = Active(state, e.Model, "A Dash");
        Require(state.InProgress is null && !state.Actions.HasFlag(ActionsTaken.Dash), "A Dash can be taken once per activation.");
        Require(ActionLimits.MoveExcluded(state, e.Model) is null, ActionLimits.HeavyText);
        Require(Board.CheckMove(state, model, e.Path).IsLegal, "A Dash must follow a legal path.");
        return state with
        {
            Actions = state.Actions | ActionsTaken.Dash,
            InProgress = new RollInProgress(RollStage.DashRoll, e.Model, BonusDice: DashDice(state, model), DashPath: e.Path),
        };
    }

    private static MatchState OnDashRolled(MatchState state, DashRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.DashRoll } p && p.Actor == e.Model && state.Pending is null, "No Dash roll is waiting.");
        var progress = state.InProgress!;
        Require(e.Check.Risky && e.Check.NetDice == progress.BonusDice + HeartDice(state, progress.Actor) - progress.PenaltyDice, "A Dash is a Risky Success Roll with the spent markers as penalty dice.");
        return state with
        {
            InProgress = e.Check.Outcome == SuccessOutcome.Failure ? null : progress with { Stage = RollStage.DashMove },
            MustEndActivation = e.Check.Outcome == SuccessOutcome.Failure && RiskyEnds(state, e.Model),
        };
    }

    // Roster stage R5h (U113): a Medi-kit's Treat ACTION (p. 84), once per activation, on the model itself or a friendly
    // model within 1": remove 1 BLOOD MARKER from one that has any, or stand up a Down friend.
    private static MatchState OnTreatDeclared(MatchState state, TreatDeclared e)
    {
        var model = Active(state, e.Model, "A Treat");
        Require(Enum.IsDefined(e.Choice), "Unknown Treat choice.");
        Require(state.InProgress is null && state.Retreat is null && !state.Actions.HasFlag(ActionsTaken.Treat), "A Treat ACTION can be taken once per activation.");
        Require(state.Setup!.ProfileOf(e.Model)!.Treats, "Only a model with a Medi-kit can take a Treat ACTION.");
        Require(TreatRefusal(state, model, e.Target, e.Choice) is null, "That model cannot be treated that way.");
        return state with
        {
            Actions = state.Actions | ActionsTaken.Treat,
            InProgress = new RollInProgress(RollStage.TreatRoll, e.Model, Target: e.Target, BonusDice: TreatDice(state, e.Model), TreatStandUp: e.Choice == TreatChoice.StandUp),
        };
    }

    private static MatchState OnTreatRolled(MatchState state, TreatRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.TreatRoll } p && p.Actor == e.Model && p.Target == e.Target && state.Pending is null
            && p.TreatStandUp == (e.Choice == TreatChoice.StandUp), "No such Treat roll is waiting.");
        var progress = state.InProgress!;
        Require(e.Check.Risky && e.Check.NetDice == progress.BonusDice + HeartDice(state, progress.Actor) - progress.PenaltyDice, "A Treat is a Risky Success Roll with the spent markers as penalty dice.");
        var failed = e.Check.Outcome == SuccessOutcome.Failure;
        var target = state.FindModel(e.Target)!;
        // The opponent may have spent the model's own last marker; then there is nothing left to remove.
        var treated = !failed && TreatRefusal(state, state.FindModel(e.Model)!, e.Target, e.Choice) is null;
        Require(e.Treated == treated, "A successful Treat roll carries out its choice when it still can, and only then.");
        var next = state with { InProgress = null, MustEndActivation = failed && RiskyEnds(state, e.Model) };
        return !treated ? next : TurnRules.WithModel(next, e.Choice == TreatChoice.StandUp
            ? target with { Status = ModelStatus.Standing }
            : target with { BloodMarkers = target.BloodMarkers - 1 });
    }

    // Why a Treat on that target with that choice is not possible now, or null (the rules' own words; Decide's check).
    internal static string? TreatRefusal(MatchState state, ModelState medic, ModelId targetId, TreatChoice choice)
    {
        var target = state.FindModel(targetId);
        if (target is null || target.Seat != medic.Seat || target.Position is null || target.Status == ModelStatus.OutOfAction)
        {
            return "A Treat ACTION treats the model itself or a friendly model on the battlefield.";
        }
        if (target.Id != medic.Id && !(Board.BodyOf(state, medic) is { } from && Board.BodyOf(state, target) is { } to && Measure.IsWithin(from, to, TreatReach)))
        {
            return "A Treat ACTION treats the model itself or a friendly model within 1\".";
        }
        return choice switch
        {
            TreatChoice.RemoveMarker when target.BloodMarkers == 0 => "That model has no BLOOD MARKER to remove.",
            TreatChoice.StandUp when target.Id == medic.Id || target.Status != ModelStatus.Down => "A Treat stands up only a Down friendly model.",
            _ => null,
        };
    }

    private static readonly Length TreatReach = Length.FromInches(1);

    // Roster stage R6b (U118): an Aim or Fortify ACTION, once per activation each, by a model whose unit has it.
    // Roster stage R6e (U121): Enforced Orthodoxy and Laying on of Hands (on a friend within 6", its Target) the same way.
    private static MatchState OnSelfActionDeclared(MatchState state, SelfActionDeclared e)
    {
        var model = Active(state, e.Model, "That ACTION");
        Require(Enum.IsDefined(e.Action), "Unknown ACTION.");
        Require(state.InProgress is null && state.Retreat is null && !state.Actions.HasFlag(SelfActionFlag(e.Action)), "That ACTION can be taken once per activation.");
        Require(HasSelfAction(state.Setup!.ProfileOf(e.Model)!, e.Action), "Only a model whose unit has it can take that ACTION.");
        Require(e.Action != SelfActionKind.Hide || HideRefusal(state, model) is null, "A Hide ACTION is taken touching terrain at least 1/2\" high.");
        Require(e.Action switch
        {
            SelfActionKind.LayingOnHands => e.Target is { } hands && HandsRefusal(state, model, hands) is null && e.Direction is null,
            // Roster stage R10 (U135): a God is With Us! is taken on the model itself or a friend within 6".
            SelfActionKind.GodIsWithUs => e.Target is { } blessed && BlessRefusal(state, model, blessed) is null && e.Direction is null,
            // Roster stage R12 (U137): an Arise and be Healed! is taken on the model itself or a friend within 3".
            SelfActionKind.AriseAndBeHealed => e.Target is { } healed && AriseRefusal(state, model, healed) is null && e.Direction is null,
            // Roster stage R9a (U134): a Puppet Master's victim is any other model in sight within 12", and the line it is moved along.
            SelfActionKind.PuppetMaster => e.Target is { } victim && state.FindModel(victim) is { } moved && Board.PuppetRefusal(state, model, moved) is null
                && e.Direction is { } direction && direction != moved.Position,
            _ => e.Target is null && e.Direction is null,
        }, "A Laying on of Hands needs a friend within 6\" with a BLOOD MARKER; a Puppet Master a model in sight within 12\" and a direction; no other ACTION has a target.");
        return state with
        {
            Actions = state.Actions | SelfActionFlag(e.Action),
            InProgress = new RollInProgress(RollStage.SelfRoll, e.Model, Target: e.Target, BonusDice: SelfActionBonus(state.Setup!.ProfileOf(e.Model)!, e.Action), SelfAction: e.Action) { Toward = e.Direction },
        };
    }

    private static MatchState OnSelfActionRolled(MatchState state, SelfActionRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.SelfRoll } p && p.Actor == e.Model && p.SelfAction == e.Action && p.Target == e.Target && state.Pending is null, "No such ACTION roll is waiting.");
        var progress = state.InProgress!;
        var model = state.FindModel(e.Model)!;
        Require(e.Check.Risky == SelfActionRisky(state, model, e.Action) && e.Check.NetDice == progress.BonusDice + HeartDice(state, progress.Actor) - progress.PenaltyDice,
            "The ACTION's Success Roll is Risky (Laying on of Hands only for a Shaken warband), with the spent markers as penalty dice.");
        var arising = e.Succeeded && e.Action == SelfActionKind.AriseAndBeHealed;
        Require(arising ? e.Die is >= 1 and <= 3 : e.Die == 0, "An Arise and be Healed! ACTION that succeeds rolls a D3; nothing else does.");
        var healed = arising ? state.FindModel(e.Target!.Value)! : null;
        var stood = e.Succeeded && e.Action == SelfActionKind.EnforcedOrthodoxy ? OrthodoxyStands(state, model)
            : healed is { Status: ModelStatus.Down } ? [healed.Id] : [];
        var removed = e.Succeeded && e.Action == SelfActionKind.LayingOnHands ? HandsRemove(state, e.Target!.Value, e.Check)
            : healed is not null ? Math.Min(e.Die, healed.BloodMarkers + healed.InfectionMarkers) : 0;
        var placed = e.Succeeded && e.Action == SelfActionKind.GodIsWithUs && state.FindModel(e.Target!.Value)!.BlessingMarkers < BloodMarkers.Max ? 1 : 0;
        Require(e.StoodUp.SequenceEqual(stood) && e.Removed == removed && e.Placed == placed, "The ACTION's result must be what the rules give.");
        var next = state with { InProgress = null, MustEndActivation = e.Check.RiskyFailure && RiskyEnds(state, e.Model) };
        if (!e.Succeeded)
        {
            return next;
        }
        switch (e.Action)
        {
            case SelfActionKind.Aim:
                return next with { Aiming = true };
            case SelfActionKind.Fortify:
                // COVER until it moves, as a dug-in Shovel's (Undig ends both).
                return TurnRules.WithModel(next, model with { DugInAt = model.Position });
            case SelfActionKind.Hide:
                return TurnRules.WithModel(next, model with { Hidden = true });
            case SelfActionKind.EnforcedOrthodoxy:
                return next with { Models = [.. next.Models.Select(m => stood.Contains(m.Id) ? m with { Status = ModelStatus.Standing } : m)] };
            // Roster stage R9a (U134): the forced moves are the next event.
            case SelfActionKind.Loudspeakers or SelfActionKind.PuppetMaster:
                return next with { ForcedMovesOwed = new ForcedOwed(e.Model, e.Action, e.Target, progress.Toward) };
            // Roster stage R12 (U137): the model stands up free; up to D3 BLOOD MARKERS come off, then INFECTION MARKERS.
            case SelfActionKind.AriseAndBeHealed:
                var risen = next.FindModel(e.Target!.Value)!;
                var blood = Math.Min(e.Removed, risen.BloodMarkers);
                return TurnRules.WithModel(next, risen with
                {
                    Status = risen.Status == ModelStatus.Down ? ModelStatus.Standing : risen.Status,
                    BloodMarkers = risen.BloodMarkers - blood,
                    InfectionMarkers = risen.InfectionMarkers - (e.Removed - blood),
                });
            // Roster stage R10 (U135): 1 BLESSING MARKER, up to 6.
            case SelfActionKind.GodIsWithUs:
                var blessed = next.FindModel(e.Target!.Value)!;
                return TurnRules.WithModel(next, blessed with { BlessingMarkers = blessed.BlessingMarkers + placed });
            default:
                var target = next.FindModel(e.Target!.Value)!;
                return TurnRules.WithModel(next, target with { BloodMarkers = target.BloodMarkers - removed });
        }
    }

    internal static ActionsTaken SelfActionFlag(SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => ActionsTaken.Aim,
        SelfActionKind.Fortify => ActionsTaken.Fortify,
        SelfActionKind.EnforcedOrthodoxy => ActionsTaken.Orthodoxy,
        SelfActionKind.Hide => ActionsTaken.Hide,
        SelfActionKind.Loudspeakers => ActionsTaken.Loudspeakers,
        SelfActionKind.PuppetMaster => ActionsTaken.PuppetMaster,
        SelfActionKind.GodIsWithUs => ActionsTaken.Blessing,
        SelfActionKind.AriseAndBeHealed => ActionsTaken.Arise,
        _ => ActionsTaken.LayingOnHands,
    };

    internal static bool HasSelfAction(ModelProfile profile, SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => profile.Aims,
        SelfActionKind.Fortify => profile.Fortifies,
        SelfActionKind.EnforcedOrthodoxy => profile.EnforcesOrthodoxy,
        SelfActionKind.Hide => profile.Hides,
        SelfActionKind.Loudspeakers => profile.Loudspeakers,
        SelfActionKind.PuppetMaster => profile.PuppetMaster,
        SelfActionKind.GodIsWithUs => profile.Blesses,
        SelfActionKind.AriseAndBeHealed => profile.Heals,
        _ => profile.LaysOnHands,
    };

    // The roll's bonus dice: Aim +2, Enforced Orthodoxy +1, Laying on of Hands +1 with Holy Water of Lalibela (an ACTION
    // that can remove BLOOD MARKERS, Warbands p. 76).
    internal static int SelfActionBonus(ModelProfile profile, SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => AimDice,
        SelfActionKind.EnforcedOrthodoxy => 1,
        SelfActionKind.Hide => 1,
        SelfActionKind.Loudspeakers => 2,
        SelfActionKind.LayingOnHands => profile.HealingWaters ? 1 : 0,
        _ => 0,
    };

    // Every one of these ACTIONS is Risky but Laying on of Hands, a plain Success Roll (Risky for a Shaken warband, p. 50).
    internal static bool SelfActionRisky(MatchState state, ModelState model, SelfActionKind action) =>
        action != SelfActionKind.LayingOnHands || state.IsShaken(model.Seat);

    // Roster stage R7c (U130): why a Hide ACTION is not possible here, or null: the model must touch terrain that is
    // 1/2" high or more (a Cover or Wall piece).
    internal static string? HideRefusal(MatchState state, ModelState model) =>
        Board.BodyOf(state, model) is { } body && Board.ScenarioOf(state)?.Battlefield is { } field
            && field.Terrain.Any(t => t.Profile != TerrainProfile.Low && t.Footprint.Touches(body))
            ? null
            : "A Hide ACTION is taken touching terrain at least 1/2\" high.";

    // Roster stage R7c (U130): the model is hidden now: a Hide succeeded, it has not acted since, and no enemy is within 1.5".
    internal static bool IsHidden(MatchState state, ModelState model) =>
        model.Hidden && Board.BodyOf(state, model) is { } body
        && !Board.OnTable(state).Any(o => o.Seat != model.Seat && o.Status != ModelStatus.OutOfAction && Measure.IsWithin(body, Board.BodyOf(state, o)!, HiddenReach));

    private static readonly Length HiddenReach = Length.FromThousandths(1500);

    // Roster stage R6e (U121): the Down friendly models on the battlefield within 8" of an Enforced Orthodoxy, in order.
    internal static ImmutableArray<ModelId> OrthodoxyStands(MatchState state, ModelState model) =>
        Board.BodyOf(state, model) is { } body
            ? [.. state.Models.Where(m => m.Seat == model.Seat && m.Id != model.Id && m.Status == ModelStatus.Down && Board.BodyOf(state, m) is { } friend && Measure.IsWithin(body, friend, OrthodoxyReach)).Select(m => m.Id)]
            : [];

    private static readonly Length OrthodoxyReach = Length.FromInches(8);

    // Why a Laying on of Hands on that target is not possible now, or null.
    internal static string? HandsRefusal(MatchState state, ModelState healer, ModelId targetId)
    {
        var target = state.FindModel(targetId);
        if (target is null || target.Seat != healer.Seat || target.Position is null || target.Status == ModelStatus.OutOfAction)
        {
            return "A Laying on of Hands ACTION is taken on the model itself or a friendly model on the battlefield.";
        }
        if (target.Id != healer.Id && !(Board.BodyOf(state, healer) is { } from && Board.BodyOf(state, target) is { } to && Measure.IsWithin(from, to, HandsReach)))
        {
            return "A Laying on of Hands ACTION is taken on the model itself or a friendly model within 6\".";
        }
        return target.BloodMarkers == 0 ? "That model has no BLOOD MARKER to remove." : null;
    }

    private static readonly Length HandsReach = Length.FromInches(6);

    private static readonly Length AriseReach = Length.FromInches(3);

    // Roster stage R12 (U137): why an Arise and be Healed! on that target is not possible now, or null: the model itself or a
    // friend on the battlefield within 3" that is Down or has a BLOOD or INFECTION MARKER to lose.
    internal static string? AriseRefusal(MatchState state, ModelState healer, ModelId targetId)
    {
        var target = state.FindModel(targetId);
        if (target is null || target.Seat != healer.Seat || target.Position is null || target.Status == ModelStatus.OutOfAction)
        {
            return "An Arise and be Healed! ACTION is taken on the model itself or a friendly model on the battlefield.";
        }
        if (target.Id != healer.Id && !(Board.BodyOf(state, healer) is { } from && Board.BodyOf(state, target) is { } to && Measure.IsWithin(from, to, AriseReach)))
        {
            return "An Arise and be Healed! ACTION is taken on the model itself or a friendly model within 3\".";
        }
        return target.Status != ModelStatus.Down && target.BloodMarkers + target.InfectionMarkers == 0 ? "That model is not Down and has no BLOOD or INFECTION MARKER to lose." : null;
    }

    // Roster stage R10 (U135): why a God is With Us! on that target is not possible now, or null: the model itself or a friend on
    // the battlefield within 6", with room for another BLESSING MARKER (at most 6).
    internal static string? BlessRefusal(MatchState state, ModelState blesser, ModelId targetId)
    {
        var target = state.FindModel(targetId);
        if (target is null || target.Seat != blesser.Seat || target.Position is null || target.Status == ModelStatus.OutOfAction)
        {
            return "A God is With Us! ACTION is taken on the model itself or a friendly model on the battlefield.";
        }
        if (target.Id != blesser.Id && !(Board.BodyOf(state, blesser) is { } from && Board.BodyOf(state, target) is { } to && Measure.IsWithin(from, to, HandsReach)))
        {
            return "A God is With Us! ACTION is taken on the model itself or a friendly model within 6\".";
        }
        return target.BlessingMarkers >= BloodMarkers.Max ? "That model has 6 BLESSING MARKERS already." : null;
    }

    // A successful Laying on of Hands removes 1 BLOOD MARKER, 3 on a Critical Success, at most what the target has left.
    internal static int HandsRemove(MatchState state, ModelId target, SuccessRollResult check) =>
        Math.Min(state.FindModel(target)!.BloodMarkers, check.Outcome == SuccessOutcome.CriticalSuccess ? 3 : 1);

    // Roster stage R6b (U118): +2 DICE on the Aim roll, and on the model's Ranged Attacks once it succeeded.
    internal const int AimDice = 2;

    internal static int AimedDice(MatchState state, ModelId shooter) => state.Aiming && state.ActiveModel == shooter ? AimDice : 0;

    // Roster stage R5h (U113): Holy Water of Lalibela's +1 DICE to the Treat roll (an ACTION that can remove BLOOD MARKERS).
    // Roster stage R6a (U117): +2 DICE more for an Expert Medic.
    internal static int TreatDice(MatchState state, ModelId model) => state.Setup?.ProfileOf(model) is { } p ? (p.HealingWaters ? 1 : 0) + (p.ExpertMedic ? 2 : 0) : 0;

    // Roster stage R6a (U117): Finish the Fallen, +1 INJURY DICE to a Melee Attack's Injury Roll at a Down target that is
    // neither CULT nor DEMONIC.
    internal static int FinishDice(MatchState state, RollInProgress progress, ModelId target) =>
        state.Setup?.ProfileOf(progress.Actor)?.FinishesTheFallen == true && state.FindModel(target) is { Status: ModelStatus.Down }
        && (state.Setup.ProfileOf(target)!.Traits & (ModelTraits.Cult | ModelTraits.Demonic)) == 0 ? 1 : 0;

    // Roster stage R6a (U117): Absolute Faith, no BLOOD MARKERS spent for -DICE on the model's Ranged Attacks.
    internal static bool ShotsIgnoreMarkers(MatchState state, ModelId model) => state.Setup?.ProfileOf(model)?.ShotsIgnoreMarkers == true;

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
            Require(ActionLimits.MoveExcluded(state, e.Model) is null, ActionLimits.HeavyText);
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
            InProgress = new RollInProgress(RollStage.ClimbRoll, e.Model, BonusDice: ClimbDice(state, e.Model), DashPath: e.Path, ClimbAction: action),
        };
    }

    private static MatchState OnSideClimbRolled(MatchState state, SideClimbRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ClimbRoll } p && p.Actor == e.Model && state.Pending is null, "No climb roll is waiting.");
        var progress = state.InProgress!;
        Require(e.Check.Risky && e.Check.NetDice == progress.BonusDice + HeartDice(state, progress.Actor) - progress.PenaltyDice, "A climb is a Risky Success Roll with the spent markers as penalty dice.");
        if (e.Check.Outcome != SuccessOutcome.Failure)
        {
            return state with { InProgress = progress with { Stage = RollStage.ClimbMove } };
        }
        // Failed: the model moves only as far as the climb's start, if it is not there already, and then stops.
        var stops = ClimbStopPath(state, progress).Length > 0;
        return state with
        {
            InProgress = stops ? progress with { Stage = RollStage.ClimbStop } : null,
            MustEndActivation = !stops && RiskyEnds(state, e.Model),
        };
    }

    // Roster stage R5d (U108): a failed Risky roll ends the activation, unless the model keeps going (the Supreme
    // Pontiff's Crucifix).
    internal static bool RiskyEnds(MatchState state, ModelId model) => state.Setup?.ProfileOf(model)?.KeepsGoingAfterRisky != true;

    // Roster stage R5k (U116): the model would lose its activation to a failed Risky roll, is Standing, and has a Talisman
    // it has not used yet, so its player is asked first. A Down or Out of Action model loses its activation anyway.
    internal static bool TalismanReady(MatchState state, ModelId? model) =>
        model is { } id && RiskyEnds(state, id) && state.FindModel(id) is { Status: ModelStatus.Standing } bearer
        && bearer.TalismansUsed < (state.Setup?.ProfileOf(id)?.Talismans ?? 0);

    // A failed Risky roll surely ends the activation: no Crucifix and no Talisman left to use. RELOAD ends it otherwise.
    private static bool RiskyStops(MatchState state, ModelId model) => RiskyEnds(state, model) && !TalismanReady(state, model);

    // Roster stage R5k (U116): the model uses a Talisman, so its failed Risky roll does not end the activation; RELOAD
    // still does, when the attack was made with a RELOAD weapon.
    private static MatchState OnTalismanUsed(MatchState state, TalismanUsed e)
    {
        Require(state.Pending is { Kind: DecisionKind.UseTalisman } && state.ActiveModel == e.Model && state.MustEndActivation,
            "A Talisman is used only when its player is asked, after the model's failed Risky roll.");
        var model = state.FindModel(e.Model)!;
        return TurnRules.WithModel(state, model with { TalismansUsed = model.TalismansUsed + 1 }) with { Pending = null, MustEndActivation = false };
    }

    // Roster stage R5d (U108): a Dash roll's bonus dice: +1 within 4" of a friendly Musical Instrument (the dashing
    // model's own included); a climb roll's: the model's Mountaineer Kit.
    // Roster stage R6c (U119): +1 more for Agile, and +1 for Frightening Speed, Loping Dash or Mad Dash.
    internal static int DashDice(MatchState state, ModelState model) => FanfareDice(state, model) + OwnDashDice(state.Setup!.ProfileOf(model.Id)!);

    internal static int OwnDashDice(ModelProfile profile) => (profile.Agile ? 1 : 0) + (profile.SwiftDash ? 1 : 0);

    internal static int FanfareDice(MatchState state, ModelState model) =>
        Board.BodyOf(state, model) is { } body && state.Models.Any(m => m.Seat == model.Seat && m.Position is not null
            && state.Setup!.ProfileOf(m.Id)!.Fanfare && (m.Id == model.Id || Board.BodyOf(state, m) is { } other && Measure.IsWithin(body, other, FanfareReach))) ? 1 : 0;

    private static readonly Length FanfareReach = Length.FromInches(4);

    internal static int ClimbDice(MatchState state, ModelId model) => state.Setup!.ProfileOf(model) is { } p ? p.ClimbDice + (p.Agile ? 1 : 0) : 0;

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
                MustEndActivation = climb.Stage == RollStage.ClimbStop && RiskyEnds(state, e.Model),
            };
            return HaltAtMine(state, climbedTo, e.Model, e.Path);
        }
        if (e.Kind == MoveKind.Move)
        {
            Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0,
                "A model can take a Move or a Charge or a Retreat ACTION, but not more than one.");
            Require(ActionLimits.MoveExcluded(state, e.Model) is null, ActionLimits.HeavyText);
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
        Require(holder is not null && e.Count <= holder.Markers, "A model cannot spend more markers than it has.");
        var rolling = state.InProgress!;
        MatchState next;
        if (state.Pending!.Kind == DecisionKind.SpendMarkersOnSuccessRoll)
        {
            Require(e.Purpose == MarkerSpendPurpose.ReduceSuccessRoll && e.Model == rolling.Actor, "Those markers belong to the rolling model.");
            next = state with { InProgress = rolling with { PenaltyDice = rolling.PenaltyDice + e.Count } };
        }
        else if (state.Pending.Kind == DecisionKind.SpendMarkersOnInjuryRoll)
        {
            Require((e.Purpose is MarkerSpendPurpose.RaiseInjuryRoll or MarkerSpendPurpose.Bloodbath) && e.Model == rolling.Target, "Those markers belong to the target.");
            if (e.Purpose == MarkerSpendPurpose.Bloodbath)
            {
                // Bloodbath Roll (p. 47, U124): 6 markers (3 if the target is Down) convert the roll, any more are +1 INJURY DICE.
                var cost = BloodbathCost(holder!);
                Require(e.Count >= cost, "A Bloodbath Roll costs 6 Blood Markers, or 3 if the target is Down.");
                next = state with { InProgress = rolling with { BonusDice = rolling.BonusDice + e.Count - cost, Bloodbath = true } };
            }
            else
            {
                next = state with { InProgress = rolling with { BonusDice = rolling.BonusDice + e.Count } };
            }
        }
        else
        {
            throw new InvalidEventException("No marker decision is pending.");
        }
        return TurnRules.WithModel(next, holder!.WithoutMarkers(e.Count)) with { Pending = null };
    }

    // The Blood Markers a Bloodbath Roll costs: 6, or 3 if the target is Down (p. 47).
    internal static int BloodbathCost(ModelState target) => target.Status == ModelStatus.Down ? 3 : 6;

    private static MatchState OnRangedAttackDeclared(MatchState state, RangedAttackDeclared e)
    {
        var attacker = Active(state, e.Attacker, "A Ranged Attack");
        Require(state.InProgress is null && state.Retreat is null && !state.Actions.HasFlag(ActionsTaken.Shoot)
            && ActionLimits.ShootExcluded(state, e.Attacker, e.Weapon) is null,
            "A Shoot ACTION can be taken once per activation, and not with a Charge or Fight ACTION (unless ASSAULT) or, with a HEAVY weapon, a move.");
        var target = state.FindModel(e.Target);
        Require(target is not null, "Unknown target.");
        var facts = Board.Assess(state, attacker, target!, e.Weapon);
        Require(facts.IsLegal, "The attack is not legal here.");
        Require(facts.LongRange == e.LongRange && facts.InCover == e.InCover && facts.CoverTerrainId == e.CoverTerrainId
            && facts.AttackerLevel == e.AttackerLevel && facts.TargetLevel == e.TargetLevel && facts.Elevated == e.Elevated, "The declared modifiers do not match the positions.");
        Require(facts.IntoMelee == e.IntoMelee, "A target within 1\" of a friendly model needs the D6 that decides who is shot, and no other does.");
        // The declared facts with the weapon's keywords and the attacker's own dice (roster stages R1, R2).
        var (bonus, penalty, later) = AttackDice.Shot(state.Setup!.ProfileOf(e.Attacker)!.Using(AttackKind.Ranged, e.Weapon), e.LongRange, e.InCover, e.Elevated, state.Setup.ProfileOf(e.Target),
            Whispers(state, state.FindModel(e.Target)!), AimedDice(state, e.Attacker));
        return state with
        {
            Actions = state.Actions | ActionsTaken.Shoot,
            ShotWeapon = e.Weapon,
            InProgress = new RollInProgress(e.IntoMelee ? RollStage.TargetRoll : RollStage.ShotRoll, e.Attacker, e.Target, bonus, penalty, InjuryDiceLater: later, Weapon: e.Weapon),
        };
    }

    private static MatchState OnRangedSuccessRolled(MatchState state, RangedSuccessRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ShotRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No attack roll is waiting.");
        return AfterShotRoll(state, e.Check);
    }

    // Roster stage R5a (U105): a Ranged Attack with a BLAST weapon at a point (p. 53). Its dice are those of any Ranged
    // Attack (the weapon's keywords, the model's own dice, Long Range); a point has no cover and no height to shoot at.
    private static MatchState OnPointAttackDeclared(MatchState state, PointAttackDeclared e)
    {
        var attacker = Active(state, e.Attacker, "A Ranged Attack");
        Require(state.InProgress is null && state.Retreat is null && !state.Actions.HasFlag(ActionsTaken.Shoot)
            && ActionLimits.ShootExcluded(state, e.Attacker, e.Weapon) is null,
            "A Shoot ACTION can be taken once per activation, and not with a Charge or Fight ACTION (unless ASSAULT) or, with a HEAVY weapon, a move.");
        var facts = Board.AssessPoint(state, attacker, e.At, e.Weapon);
        Require(facts.IsLegal, "The attack is not legal here.");
        Require(facts.LongRange == e.LongRange, "The declared Long Range does not match the positions.");
        var (bonus, penalty, later) = AttackDice.Shot(state.Setup!.ProfileOf(e.Attacker)!.Using(AttackKind.Ranged, e.Weapon), e.LongRange, false, false, aim: AimedDice(state, e.Attacker));
        return state with
        {
            Actions = state.Actions | ActionsTaken.Shoot,
            ShotWeapon = e.Weapon,
            InProgress = new RollInProgress(RollStage.ShotRoll, e.Attacker, null, bonus, penalty, InjuryDiceLater: later, Weapon: e.Weapon) { BlastAt = e.At },
        };
    }

    private static MatchState OnPointSuccessRolled(MatchState state, PointSuccessRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ShotRoll, Target: null } p && p.Actor == e.Attacker && p.BlastAt == e.At && state.Pending is null,
            "No attack roll at that point is waiting.");
        return AfterShotRoll(state, e.Check);
    }

    // Roster stage R5a (U105): the models the blast at the aimed point hits, worked out again from the positions; then
    // the first one's Injury Roll. No model was the attack's target, so a Critical Success adds no dice to any of them.
    private static MatchState OnPointBlastHit(MatchState state, PointBlastHit e)
    {
        Require(state.InProgress is { Stage: RollStage.Blast, Target: null } p && p.Actor == e.Attacker && p.BlastAt == e.At && state.Pending is null,
            "No blast at that point is waiting.");
        var progress = state.InProgress!;
        Require(Board.BlastHitsAt(state, state.FindModel(e.Attacker)!, e.At, progress.Weapon).SequenceEqual(e.Hit),
            "The blast must hit exactly the models within its radius of the point that see it, and the friends of those beside them.");
        return e.Hit.IsEmpty
            ? AttackOver(state, progress)
            : state with { InProgress = NextBlastRoll(state, progress with { Aimed = null, Critical = false }, e.Hit) };
    }

    // A Ranged Attack's Success Roll, at a model or (R5a) at a point: a miss, a scatter, a blast or the Injury Roll.
    private static MatchState AfterShotRoll(MatchState state, SuccessRollResult check)
    {
        var progress = state.InProgress!;
        var attacker = state.FindModel(progress.Actor)!;
        var profile = AttackProfile(state, progress);
        Require(check.Risky == AttackDice.Risky(profile, AttackKind.Ranged, state.IsShaken(attacker.Seat)) && check.NetDice == progress.BonusDice + HeartDice(state, progress.Actor) - progress.PenaltyDice,
            "The roll must carry the declared and spent dice, and be Risky exactly when the warband is Shaken or the weapon is RISKY.");
        if (check.Outcome == SuccessOutcome.Failure && profile.RangedWeaponBlast > 0 && (profile.RangedWeaponKeywords & WeaponKeywords.Scatter) != 0)
        {
            // Roster stage R4c (U103): SCATTER: the attack scatters (7 - the roll)" instead of missing (p. 57); a failed
            // Risky roll still ends the activation, once the attack is over.
            return state with
            {
                InProgress = progress with
                {
                    Stage = RollStage.Scatter,
                    ScatterInches = SuccessRoll.SuccessFrom - check.Dice.Total,
                    RiskyEnds = check.RiskyFailure && RiskyEnds(state, progress.Actor),
                    BonusDice = 0,
                    PenaltyDice = 0,
                    MarkersAsked = false,
                },
            };
        }
        if (check.Outcome == SuccessOutcome.Failure)
        {
            var ends = check.RiskyFailure && RiskyEnds(state, progress.Actor);
            return state with { InProgress = null, MustEndActivation = ends, ReloadEnds = !(ends && RiskyStops(state, progress.Actor)) && ActionLimits.Reloads(profile, AttackKind.Ranged) };
        }
        // Critical Success: +1 INJURY DICE on the Injury Roll (confirmed ruling; the print on pp. 44-45 says "+1 DICE"),
        // +2 with CRITICAL; the weapon's own INJURY DICE and SHOTGUN's -1 at Long Range (roster stages R1, R2).
        var critical = check.Outcome == SuccessOutcome.CriticalSuccess;
        if (profile.RangedWeaponBlast > 0)
        {
            // Roster stage R4b (U102): a BLAST attack: who it hits comes next (BlastHit), then their Injury Rolls.
            return state with { InProgress = progress with { Stage = RollStage.Blast, BonusDice = 0, PenaltyDice = 0, Critical = critical, MarkersAsked = false } };
        }
        var (bonus, penalty) = AttackDice.InjuryDice(profile, AttackKind.Ranged, critical, meleeOnDownTarget: false, progress.InjuryDiceLater, state.Setup!.ProfileOf(progress.Target!.Value)!);
        return state with
        {
            InProgress = progress with { Stage = RollStage.InjuryRoll, BonusDice = bonus, PenaltyDice = penalty, Critical = critical, MarkersAsked = false, GuardOwed = !progress.FriendlyFire && GuardsFor(state, progress.Target!.Value) },
        };
    }

    // Roster stage R4b (U102): the models a BLAST attack hits, worked out again from the positions; then the first
    // one's Injury Roll.
    private static MatchState OnBlastHit(MatchState state, BlastHit e)
    {
        Require(state.InProgress is { Stage: RollStage.Blast } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No blast is waiting.");
        var progress = state.InProgress!;
        var expected = Board.BlastHits(state, state.FindModel(e.Attacker)!, state.FindModel(e.Target)!, progress.Weapon);
        Require(expected.SequenceEqual(e.Hit), "The blast must hit exactly the models within its radius that see its target, and the friends of those beside them.");
        return state with { InProgress = NextBlastRoll(state, progress with { Aimed = e.Target }, e.Hit) };
    }

    // Roster stage R4c (U103): the opponent put the scattered attack at a point it may land at; the blast there hits
    // exactly the models listed, worked out again from the positions; then the first one's Injury Roll.
    private static MatchState OnAttackScattered(MatchState state, AttackScattered e)
    {
        Require(state.InProgress is { Stage: RollStage.Scatter } p && p.Actor == e.Attacker && p.Target == e.Target
            && state.Pending is { Kind: DecisionKind.ChooseScatterPoint }, "No scattered attack is waiting for its point.");
        var progress = state.InProgress!;
        var attacker = state.FindModel(e.Attacker)!;
        Require(state.Pending!.Seat == attacker.Seat.Opponent(), "The attacker's opponent picks where the attack scatters to.");
        Require(e.Inches == progress.ScatterInches, "The attack scatters 7 minus its Success Roll in inches.");
        Require(Board.CanScatterTo(state, progress, e.At), "The attack must scatter exactly that far, to a point on the battlefield in Line of Sight of its target.");
        Require(Board.BlastHitsAt(state, attacker, e.At, progress.Weapon).SequenceEqual(e.Hit),
            "The blast must hit exactly the models within its radius of the point that see it, and the friends of those beside them.");
        var landed = state with { Pending = null };
        return e.Hit.IsEmpty
            ? AttackOver(landed, progress)
            : landed with { InProgress = NextBlastRoll(landed, progress with { Aimed = e.Target, BlastAt = e.At, Critical = false }, e.Hit) };
    }

    // Roster stage R4c (U103): the scattered attack has nowhere to land, so it misses (p. 57).
    private static MatchState OnScatterMissed(MatchState state, ScatterMissed e)
    {
        Require(state.InProgress is { Stage: RollStage.Scatter } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null,
            "No scattered attack is waiting.");
        var progress = state.InProgress!;
        Require(e.Inches == progress.ScatterInches, "The attack scatters 7 minus its Success Roll in inches.");
        Require(Board.ScatterPoints(state, progress).Count == 0, "A scattered attack misses only when it has nowhere to land.");
        return AttackOver(state, progress);
    }

    // A Ranged Attack is over: a failed Risky roll ends the activation now (a scattered attack's, U103), else RELOAD does.
    private static MatchState AttackOver(MatchState state, RollInProgress progress) => state with
    {
        InProgress = null,
        MustEndActivation = state.MustEndActivation || progress.RiskyEnds,
        ReloadEnds = !(progress.RiskyEnds && RiskyStops(state, progress.Actor)) && !progress.FreeFight && ActionLimits.Reloads(AttackProfile(state, progress), progress.Kind),
    };

    // The next model a blast hit: its Injury Roll, with a Critical Success's extra dice only for the model the attack
    // was aimed at (p. 53), friendly fire for the attacker's own models (no markers spent on them, U52).
    private static RollInProgress NextBlastRoll(MatchState state, RollInProgress progress, ImmutableArray<ModelId> queue)
    {
        var victim = queue[0];
        if (progress.Martyrdom)
        {
            // Roster stage R5j (U115): +1 INJURY DICE within 1" and the INJURY DICE the target takes (Holy Smoke); the
            // bearer's own roll is always 4D6 added together.
            var (more, fewer) = victim == progress.Actor ? (0, 0)
                : AttackDice.With(progress.MartyrClose.Contains(victim) ? 1 : 0, 0, AttackDice.TakenDice(state.Setup!.ProfileOf(victim)!, WeaponKeywords.None));
            return progress with
            {
                Target = victim,
                BlastQueue = queue.RemoveAt(0),
                FriendlyFire = state.FindModel(victim)!.Seat == state.FindModel(progress.Actor)!.Seat,
                BonusDice = more,
                PenaltyDice = fewer,
                MarkersAsked = false,
                Bloodbath = false,
            };
        }
        var (bonus, penalty) = AttackDice.InjuryDice(AttackProfile(state, progress), AttackKind.Ranged, progress.Critical && victim == progress.Aimed, meleeOnDownTarget: false, progress.InjuryDiceLater, state.Setup!.ProfileOf(victim)!);
        return progress with
        {
            Stage = RollStage.InjuryRoll,
            Target = victim,
            BlastQueue = queue.RemoveAt(0),
            FriendlyFire = state.FindModel(victim)!.Seat == state.FindModel(progress.Actor)!.Seat,
            BonusDice = bonus,
            PenaltyDice = penalty,
            MarkersAsked = false,
            Bloodbath = false,
        };
    }

    private static MatchState OnInjuryRolled(MatchState state, InjuryRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.InjuryRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No Injury Roll is waiting.");
        var progress = state.InProgress!;
        var attacker = state.FindModel(e.Attacker)!;
        var target = state.FindModel(e.Target)!;
        Require((target.Id != attacker.Id || progress.Aimed is not null || progress.BlastAt is not null || progress.Martyrdom) && (target.Seat == attacker.Seat) == progress.FriendlyFire,
            "An attack injures an enemy model, unless a shot into melee was forced onto a friendly one or a blast hits it.");
        // Roster stage R5j (U115): a Martyrdom Device's bearer rolls 4D6 added together; every other roll keeps two or three.
        var profile = AttackProfile(state, progress);
        var deadly = (profile.AttackKeywords(progress.Kind) & WeaponKeywords.Deadly) != 0;
        // Bloodbath Roll (p. 47, U124): the attacker spent the markers for it, and the event says so.
        Require(e.Bloodbath == progress.Bloodbath, "An Injury Roll is a Bloodbath Roll only if the attacker spent the Blood Markers for one.");
        Require(e.Injury.AllFour == (progress.Martyrdom && target.Id == attacker.Id || progress.Bloodbath && deadly), "Only a Martyrdom Device's bearer, or a DEADLY Bloodbath Roll, rolls 4D6 added together.");
        Require(e.Injury.NetDice == progress.BonusDice + MagicDice(state, progress.Actor) - progress.PenaltyDice
            && e.Injury.Modifier == InjuryRoll.CombineModifiers(AttackDice.InjuryModifiers(profile, state.Setup!.ProfileOf(e.Target)!, progress.Kind))
            && e.Injury.TargetAlreadyDown == (target.Status == ModelStatus.Down), "The Injury Roll must match the dice, modifiers and state of the target.");
        // Roster stage R5 (U104): a DEADLY weapon's Injury Rolls keep three dice, every other one two.
        Require(e.Injury.Deadly == (progress.Bloodbath ? !deadly : deadly), "A DEADLY weapon's Injury Roll is 3D6, any other is 2D6; a Bloodbath Roll is 3D6, or 4D6 with DEADLY.");

        // Roster stage R4a: SHRAPNEL, GAS and FIRE put extra BLOOD MARKERS on the target (U101).
        Require(e.ExtraMarkers == AttackDice.ExtraMarkers(profile, progress.Kind, state.Setup!.ProfileOf(e.Target)!), "The extra BLOOD MARKERS must be the weapon's SHRAPNEL, GAS and FIRE.");
        Require(e.Tough == ToughSaves(state, target, e.Injury.Outcome), "TOUGH turns the model's first Out of Action result into Down, and nothing else.");
        Require(e.Spared == MementoSaves(state, target, e.Injury.Outcome), "Memento Mori turns the model's first Out of Action result into No Effect, and nothing else.");
        Require(e.Infection == AttackDice.PlacesInfection(profile, progress.Kind), "An INFECTION MARKERS weapon places INFECTION MARKERS, any other BLOOD MARKERS.");
        Require(e.Warded == (!e.Infection && Wards(state, target, e.ResultMarkers + e.ExtraMarkers)), "The Sacrificial Lamb keeps off the first BLOOD MARKER placed on the model, and only that one.");
        // Roster stage R10 (U135): an INFECTION MARKERS weapon's markers are INFECTION MARKERS, up to 6 of them.
        var markers = e.Infection ? target.BloodMarkers : Math.Min(BloodMarkers.Max, target.BloodMarkers + e.PlacedMarkers);
        var hurt = Hurt(target, e.Result, markers, e.Tough || e.Spared) with
        {
            WardSpent = target.WardSpent || e.Warded,
            InfectionMarkers = e.Infection ? Math.Min(BloodMarkers.Max, target.InfectionMarkers + e.PlacedMarkers) : target.InfectionMarkers,
        };
        // A blast's next model (U102), once this one is rolled for.
        if (!progress.BlastQueue.IsDefaultOrEmpty)
        {
            var after = TurnRules.WithModel(state, hurt);
            return after with { InProgress = NextBlastRoll(after, progress, progress.BlastQueue) };
        }
        // RELOAD: the attacker's activation ends now its ACTION is done (not after a free attack, which is not its own); a
        // scattered attack's failed Risky roll ends it instead (U103).
        return AttackOver(TurnRules.WithModel(state, hurt), progress);
    }

    // Roster stage R5b (U106): TOUGH turns the first Out of Action result a model suffers into a Down result (p. 57).
    internal static bool ToughSaves(MatchState state, ModelState model, InjuryOutcome outcome) =>
        outcome == InjuryOutcome.OutOfAction && !model.ToughSpent && state.Setup!.ProfileOf(model.Id)! is { MementoMori: false } p && p.Traits.HasFlag(ModelTraits.Tough);

    // Roster stage R6c (U119): Memento Mori turns the first Out of Action result into No Effect; ToughSpent records it,
    // as such a model is never TOUGH.
    internal static bool MementoSaves(MatchState state, ModelState model, InjuryOutcome outcome) =>
        outcome == InjuryOutcome.OutOfAction && !model.ToughSpent && state.Setup!.ProfileOf(model.Id)!.MementoMori;

    // Roster stage R5g (U112): the BLOOD MARKERS an event owes. A Crown of Hellfire's bearer that ends an activation on
    // the battlefield, or a Damned Pact's bearer taken Out of Action by an Injury Roll or a mine (measured from where it
    // stood before it was removed), owes 1 to each enemy model on the battlefield within 1" of it, except a NEGATE FIRE
    // model (Warbands p. 150, p. 83).
    private static MatchState Burns(MatchState before, GameEvent e, MatchState after)
    {
        var source = e switch
        {
            ActivationEnded a when after.Setup?.ProfileOf(a.Model)?.BurnsAtActivationEnd == true => after.FindModel(a.Model),
            InjuryRolled { Result: InjuryOutcome.OutOfAction } i => TakenOut(before, i.Target),
            MineExploded { Result: InjuryOutcome.OutOfAction } m => TakenOut(before, m.Model),
            _ => null,
        };
        if (source is null || Board.BodyOf(before, source) is not { } body)
        {
            return after;
        }
        var targets = after.Models
            .Where(m => m.Seat != source.Seat && m.Position is not null && m.Status != ModelStatus.OutOfAction
                && !after.Setup!.ProfileOf(m.Id)!.Negates.HasFlag(WeaponKeywords.Fire)
                && Board.BodyOf(after, m) is { } other && Measure.IsWithin(body, other, BurnReach))
            .Select(m => m.Id).ToImmutableArray();
        return targets.IsEmpty ? after : after with { MarkersOwed = after.MarkersOwed.Add(new OwedMarkers(source.Id, targets)) };
    }

    private static ModelState? TakenOut(MatchState before, ModelId model) =>
        before.FindModel(model) is { Status: not ModelStatus.OutOfAction, Position: not null } m && before.Setup?.ProfileOf(model)?.BurnsWhenTakenOut == true ? m : null;

    private static readonly Length BurnReach = Length.FromInches(1);

    // Roster stage R5g (U112): the owed BLOOD MARKERS are placed, one on each target, in the order they were owed; a
    // Sacrificial Lamb keeps the first one placed on its model off (U109).
    private static MatchState OnBloodMarkersPlaced(MatchState state, BloodMarkersPlaced e)
    {
        Require(state.MarkersOwed is [var owed, ..] && owed.Source == e.Source && owed.Targets.SequenceEqual(e.Targets), "No such BLOOD MARKERS are owed.");
        var warded = e.Targets.Where(t => Wards(state, state.FindModel(t)!, 1)).ToList();
        Require(e.Warded.SequenceEqual(warded), "The Sacrificial Lamb keeps off the first BLOOD MARKER placed on the model, and only that one.");
        var next = state with { MarkersOwed = state.MarkersOwed.RemoveAt(0) };
        foreach (var id in e.Targets)
        {
            var model = next.FindModel(id)!;
            next = TurnRules.WithModel(next, e.Warded.Contains(id)
                ? model with { WardSpent = true }
                : model with { BloodMarkers = Math.Min(BloodMarkers.Max, model.BloodMarkers + 1) });
        }
        return next;
    }

    // Roster stage R5e (U109): a model with a sacrificed Lamb ignores the first BLOOD MARKER placed on it.
    internal static bool Wards(MatchState state, ModelState model, int placed) =>
        placed > 0 && !model.WardSpent && state.Setup!.ProfileOf(model.Id)!.WardedByBlood;

    // Roster stage R5e (U109): -1 DICE to a Ranged Attack at a model that has the Urn of the Bitter Ashes or is within 3"
    // of a model that has it (friend or enemy; a Down bearer too).
    internal static bool Whispers(MatchState state, ModelState target) =>
        Board.BodyOf(state, target) is { } body && state.Models.Any(m => m.Position is not null && state.Setup!.ProfileOf(m.Id)!.SinisterWhispers
            && (m.Id == target.Id || Board.BodyOf(state, m) is { } urn && Measure.IsWithin(body, urn, UrnReach)));

    private static readonly Length UrnReach = Length.FromInches(3);

    // The model after an Injury Roll's result, with its BLOOD MARKERS (and TOUGH or Memento Mori spent when it saved the model).
    private static ModelState Hurt(ModelState model, InjuryOutcome result, int markers, bool tough) => result switch
    {
        InjuryOutcome.OutOfAction => model with { Status = ModelStatus.OutOfAction, Position = null, BloodMarkers = markers },
        InjuryOutcome.Down => model with { Status = ModelStatus.Down, BloodMarkers = markers, ToughSpent = model.ToughSpent || tough },
        _ => model with { BloodMarkers = markers, ToughSpent = model.ToughSpent || tough },
    };

    // ---- M4 Slice B: Charge, Retreat, Fight, shooting into melee ----

    private static MatchState OnChargeDeclared(MatchState state, ChargeDeclared e)
    {
        var charger = Active(state, e.Model, "A Charge");
        var profile = state.Setup!.ProfileOf(e.Model)!;
        Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0
            && ActionLimits.MeleeExcluded(state, e.Model, ActionsTaken.Charge) is null,
            "A Charge is one of Move, Charge or Retreat, and cannot follow a Shoot ACTION (unless ASSAULT, and never with a HEAVY ranged weapon).");
        var target = state.FindModel(e.Target);
        Require(target is not null && Board.AssessCharge(state, charger, target) is null, "The charge is not legal here.");
        // HEAVY battlekit: no Charge Bonus, so the charge moves at once (roster stage R2b, p. 55).
        var stage = profile.NoChargeBonus ? RollStage.ChargeMove : RollStage.ChargeRoll;
        // Roster stage R3: BLOCK needs to know who charged this turn (p. 53); only matches with BLOCK keep the list.
        var charged = Board.TracksCharges(state.Setup) && !state.ChargedThisTurn.Contains(e.Model) ? state.ChargedThisTurn.Add(e.Model) : state.ChargedThisTurn;
        // Roster stage R6c (U119): Counter-Charge, when the Charge is the activation's first ACTION.
        var counter = profile.CounterCharge && state.Actions == ActionsTaken.None;
        return state with { Actions = state.Actions | ActionsTaken.Charge, InProgress = new RollInProgress(stage, e.Model, e.Target) { CarryOn = e.CarryOn }, ChargedThisTurn = charged, CounterCharging = state.CounterCharging || counter };
    }

    private static MatchState OnChargeRolled(MatchState state, ChargeRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.ChargeRoll } p && p.Actor == e.Model && p.Target == e.Target && state.Pending is null, "No Charge Bonus is waiting.");
        // Roster stage R6a (U117): Shock Charge rolls 2D6 and keeps the highest.
        Require(state.Setup!.ProfileOf(e.Model)!.ExtraChargeDie ? e.Dropped is >= 1 and <= 6 && e.Dropped <= e.Die : e.Dropped == 0,
            "The Charge Bonus is one D6, or the highest of two with Shock Charge.");
        return state with { InProgress = state.InProgress! with { Stage = RollStage.ChargeMove, ChargeDie = e.Die } };
    }

    private static MatchState OnChargeMoved(MatchState state, ChargeMoved e)
    {
        var charger = Active(state, e.Model, "A charge move");
        Require(state.InProgress is { Stage: RollStage.ChargeMove } p && p.Actor == e.Model && p.Target == e.Target, "No charge move is waiting.");
        var progress = state.InProgress!;
        var plan = Board.PlanCharge(state, charger, state.FindModel(e.Target)!, progress.ChargeDie, progress.CarryOn);
        Require(plan.Path.SequenceEqual(e.Path) && plan.Cost == e.Cost && plan.Reached == e.Reached, "The charge move must be the one the rules plan from the Charge Bonus.");
        var moved = e.Path.IsEmpty ? charger : charger with { Position = e.Path[^1] };
        return HaltAtMine(state, TurnRules.WithModel(state, moved) with { InProgress = null }, e.Model, e.Path);
    }

    private static MatchState OnRetreatDeclared(MatchState state, RetreatDeclared e)
    {
        var retreater = Active(state, e.Model, "A Retreat");
        Require(state.InProgress is null && state.Retreat is null && (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) == 0,
            "A Retreat is one of Move, Charge or Retreat.");
        Require(ActionLimits.MoveExcluded(state, e.Model) is null, ActionLimits.HeavyText);
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
        var owned = state.Setup!.ProfileOf(e.Attacker)!;
        Require(ModelProfile.IsActionAttack(e.Weapon) ? owned.Has(AttackKind.Melee, e.Weapon) && !e.FreeFight && !e.OffHand : e.Weapon < owned.Weapons(AttackKind.Melee), "The model has no such Melee Weapon.");
        Require(e.Block == Board.Blocks(state, e.Attacker, e.Target), "BLOCK applies exactly when the target has it and the attacker charged this turn.");
        Require(e.Fear == Board.Fears(state, e.Attacker, e.Target), "FEAR applies exactly when the target has it and the attacker has neither FEAR nor NEGATE FEAR.");
        var left = state.AttacksLeft is { Next.Length: > 0 } l && l.Attacker == e.Attacker && l.Free == e.FreeFight ? l : null;
        ImmutableArray<MeleeAttackSlot> rest;
        if (!e.FreeFight)
        {
            var fighter = Active(state, e.Attacker, "A Fight");
            Require(state.InProgress is null && state.Retreat is null, "A Melee Attack waits for the last action to finish.");
            // Roster stage R5i (U114): a Grind to Dust ACTION, once per activation; it ends a Fight's attacks still to come.
            // Roster stage R6d (U120): a Trample ACTION the same.
            if (ModelProfile.IsActionAttack(e.Weapon))
            {
                var taken = ActionAttackFlag(e.Weapon);
                Require(!state.Actions.HasFlag(taken), "A Grind to Dust or Trample ACTION is taken once per activation.");
                var grind = Board.AssessMelee(state, fighter, target!, e.Weapon);
                Require(grind.Refusal is null && grind.CoverTerrainId == e.DefendedObstacleId, "The Grind to Dust or Trample attack is not legal here, or its modifiers do not match the positions.");
                var (grindBonus, grindPenalty) = MeleeDice(state, e);
                return state with
                {
                    Actions = state.Actions | taken,
                    AttacksLeft = null,
                    InProgress = new RollInProgress(RollStage.FightRoll, e.Attacker, e.Target, grindBonus, grindPenalty, Kind: AttackKind.Melee, Weapon: e.Weapon),
                };
            }
            if (left is null)
            {
                Require(!state.Actions.HasFlag(ActionsTaken.Fight) && ActionLimits.MeleeExcluded(state, e.Attacker, ActionsTaken.Fight) is null,
                    "A Fight ACTION is taken once per activation and not after a Shoot ACTION (unless ASSAULT).");
                Require(!e.OffHand, "A Fight's first attack is not made with the Off-Hand weapon.");
                rest = AttacksAfterFirst(owned, e.Weapon, free: false);
            }
            else
            {
                Require(left.Next[0] == new MeleeAttackSlot(e.Weapon, e.OffHand), "The Fight's next Melee Attack is made with the next weapon in turn.");
                rest = left.Next.RemoveAt(0);
            }
            var facts = Board.AssessMelee(state, fighter, target!, e.Weapon);
            Require(facts.Refusal is null && facts.CoverTerrainId == e.DefendedObstacleId, "The Melee Attack is not legal here, or its modifiers do not match the positions.");
            var (bonus, penalty) = MeleeDice(state, e);
            return state with
            {
                Actions = state.Actions | ActionsTaken.Fight,
                AttacksLeft = rest.IsEmpty ? null : new MeleeAttacksLeft(e.Attacker, false, rest),
                InProgress = new RollInProgress(RollStage.FightRoll, e.Attacker, e.Target, bonus, penalty, Kind: AttackKind.Melee, Weapon: e.Weapon, OffHand: e.OffHand),
            };
        }

        var retreat = state.Retreat;
        Require(!e.OffHand, "A free attack is made with one weapon.");
        if (left is null)
        {
            Require(state.Phase == Phase.Activation && retreat is { Closed: false } && state.InProgress is null && retreat.Model == e.Target
                && state.Pending == new PendingDecision(DecisionKind.FreeFight, target!.Seat.Opponent()), "A free attack answers the offer made to the opponent of a retreating model.");
            Require(Board.FreeFighters(state, retreat!).Any(m => m.Id == e.Attacker), "That model cannot make a free attack.");
            rest = AttacksAfterFirst(owned, e.Weapon, free: true);
        }
        else
        {
            Require(FreeAttackDue(state) == left && retreat!.Model == e.Target && left.Next[0] == new MeleeAttackSlot(e.Weapon, false),
                "A free attack's next CLEAVE attack follows at once, with the same weapon, against the retreating model.");
            rest = left.Next.RemoveAt(0);
        }
        var free = Board.AssessMelee(state, attacker!, target!, e.Weapon);
        Require(free.Refusal is null && free.CoverTerrainId == e.DefendedObstacleId, "The free attack is not legal here, or its modifiers do not match the positions.");
        var (freeBonus, freePenalty) = MeleeDice(state, e);
        return state with
        {
            Pending = null,
            Retreat = left is null ? retreat! with { Fought = retreat!.Fought.Add(e.Attacker) } : retreat,
            AttacksLeft = rest.IsEmpty ? null : new MeleeAttacksLeft(e.Attacker, true, rest),
            InProgress = new RollInProgress(RollStage.FightRoll, e.Attacker, e.Target, freeBonus, freePenalty, Kind: AttackKind.Melee, FreeFight: true, Weapon: e.Weapon),
        };
    }

    // A Melee Attack's Success Roll dice: the declared penalty and the attacker's own dice with that weapon (roster
    // stages R1, R3).
    private static (int Bonus, int Penalty) MeleeDice(MatchState state, MeleeAttackDeclared e) =>
        AttackDice.With(CounterDice(state, e.Attacker) + HordeDice(state, e.Attacker, e.Weapon), e.PenaltyDice, AttackDice.OwnDice(state.Setup!.ProfileOf(e.Attacker)!.Using(AttackKind.Melee, e.Weapon), AttackKind.Melee));

    // Roster stage R6c (U119): +1 DICE to the active model's Melee Attacks after a Counter-Charge this activation.
    internal static int CounterDice(MatchState state, ModelId attacker) => state.CounterCharging && state.ActiveModel == attacker ? 1 : 0;

    // Roster stage R6d (U120): the ACTIONS taken by a Grind to Dust or a Trample attack.
    internal static ActionsTaken ActionAttackFlag(int weapon) => weapon == ModelProfile.TrampleWeapon ? ActionsTaken.Trample : ActionsTaken.Grind;

    // Roster stage R6d (U120): Overwhelming Horde, +1 DICE on its attacks without a weapon (weapon 0) for each other
    // friendly model on the battlefield within 3" (Down ones too, as for the other auras).
    internal static int HordeDice(MatchState state, ModelId attacker, int weapon) =>
        weapon == 0 && state.Setup!.ProfileOf(attacker)!.Horde && state.FindModel(attacker) is { } model && Board.BodyOf(state, model) is { } body
            ? state.Models.Count(m => m.Seat == model.Seat && m.Id != model.Id && m.Position is not null && m.Status != ModelStatus.OutOfAction && Board.BodyOf(state, m) is { } other && Measure.IsWithin(body, other, HordeReach))
            : 0;

    private static readonly Length HordeReach = Length.FromInches(3);

    private static MatchState OnMeleeSuccessRolled(MatchState state, MeleeSuccessRolled e)
    {
        Require(state.InProgress is { Stage: RollStage.FightRoll } p && p.Actor == e.Attacker && p.Target == e.Target && state.Pending is null, "No Melee Attack roll is waiting.");
        var progress = state.InProgress!;
        var attacker = state.FindModel(e.Attacker)!;
        var fighter = AttackProfile(state, progress);
        Require(e.Check.Risky == AttackDice.Risky(fighter, AttackKind.Melee, state.IsShaken(attacker.Seat)) && e.Check.NetDice == progress.BonusDice + HeartDice(state, progress.Actor) - progress.PenaltyDice,
            "The roll must carry the declared and spent dice, and be Risky exactly when the warband is Shaken or the weapon is RISKY.");
        if (e.Check.Outcome == SuccessOutcome.Failure)
        {
            // Only the active model has an activation to lose; a free attack that fails simply misses (U49).
            return state with
            {
                InProgress = null,
                MustEndActivation = e.Check.RiskyFailure && !progress.FreeFight && RiskyEnds(state, progress.Actor),
                ReloadEnds = !progress.FreeFight && !(e.Check.RiskyFailure && RiskyStops(state, progress.Actor)) && ActionLimits.Reloads(fighter, AttackKind.Melee),
            };
        }
        // +1 INJURY DICE for a Critical Success (confirmed ruling) and for a Melee Attack on a Down target (p. 49).
        var critical = e.Check.Outcome == SuccessOutcome.CriticalSuccess;
        var onDownTarget = state.FindModel(e.Target)!.Status == ModelStatus.Down;
        var (bonus, penalty) = AttackDice.InjuryDice(fighter, AttackKind.Melee, critical, onDownTarget, FinishDice(state, progress, e.Target), state.Setup!.ProfileOf(e.Target)!);
        return state with
        {
            InProgress = progress with
            {
                Stage = RollStage.InjuryRoll,
                BonusDice = bonus,
                PenaltyDice = penalty,
                Critical = critical,
                MarkersAsked = false,
                GuardOwed = GuardsFor(state, e.Target),
            },
        };
    }

    // Roster stage R7d (U131): the hit model has a Bodyguard beside it, so its player is asked before the Injury Roll.
    private static bool GuardsFor(MatchState state, ModelId hit) => Board.Bodyguards(state, state.FindModel(hit)!).Count > 0;

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
        var facts = Board.ForcedShotFacts(state, shooter, victim!, progress.Weapon);
        Require(facts.LongRange == e.LongRange && facts.InCover == e.InCover && facts.CoverTerrainId == e.CoverTerrainId && facts.Elevated == e.Elevated, "The declared modifiers do not match the positions.");
        var (bonus, penalty, later) = AttackDice.Shot(AttackProfile(state, progress), e.LongRange, e.InCover, e.Elevated, state.Setup!.ProfileOf(e.Victim), Whispers(state, victim!), AimedDice(state, e.Attacker));
        return state with
        {
            Pending = null,
            InProgress = progress with
            {
                Stage = RollStage.ShotRoll,
                Target = e.Victim,
                FriendlyFire = true,
                BonusDice = bonus,
                PenaltyDice = penalty,
                InjuryDiceLater = later,
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

    // Field Shrine (Battlekit p. 82; U127): the carrier's player deploys the shrine, wholly in the zone and clear of every base,
    // shrine and Impassable piece, or declines. Either way the carrier is settled and the decision is over.
    private static MatchState OnShrinePlaced(MatchState state, ShrinePlaced e)
    {
        var carrier = state.Pending is { Kind: DecisionKind.PlaceShrine } waiting ? Board.ShrineCarrierWaiting(state, waiting.Seat) : null;
        Require(carrier is not null && carrier.Id == e.Carrier, "No Field Shrine is waiting for that model.");
        Require(Board.CheckShrine(state, carrier!.Seat, e.At).IsLegal, "A Field Shrine must stand wholly in the deployment zone, clear of every base, shrine and Impassable piece.");
        return state with
        {
            Pending = null,
            Shrines = state.Shrines.Add(new ShrineState(e.Carrier, carrier.Seat, e.At)),
            ShrineSettled = state.ShrineSettled.Add(e.Carrier),
        };
    }

    private static MatchState OnShrineDeclined(MatchState state, ShrineDeclined e)
    {
        var carrier = state.Pending is { Kind: DecisionKind.PlaceShrine } waiting ? Board.ShrineCarrierWaiting(state, waiting.Seat) : null;
        Require(carrier is not null && carrier.Id == e.Carrier, "No Field Shrine is waiting for that model.");
        return state with { Pending = null, ShrineSettled = state.ShrineSettled.Add(e.Carrier) };
    }

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

    private static MatchState OnMineDefuseRolled(MatchState state, MineDefuseRolled e)
    {
        Require(state.MineStop is { Revealed: false, DefuseTried: false } stop && stop.Model == e.Model && stop.Current.Marker == e.Marker && state.Pending is null
            && state.Setup!.ProfileOf(e.Model)!.DefusesMines, "Only a model with Defuse Mine rolls to defuse the Mine Marker it halted at, once.");
        Require(e.Check.Risky && e.Check.NetDice == 0, "Defusing a mine is a Risky Success Roll with no extra dice.");
        if (!e.Defused)
        {
            var ends = RiskyEnds(state, e.Model);
            return state with { MineStop = state.MineStop! with { DefuseTried = true, EndsActivation = state.MineStop.EndsActivation || ends }, MustEndActivation = state.MustEndActivation || ends };
        }
        return state with
        {
            Mines = [.. state.Mines.Where(m => m.Id != e.Marker)],
            MineStop = state.MineStop! with { Revealed = true, Live = false },
        };
    }

    private static MatchState OnMineExploded(MatchState state, MineExploded e)
    {
        Require(state.MineStop is { Revealed: true, Live: true } stop && stop.Model == e.Model && stop.Current.Marker == e.Marker && state.Pending is null,
            "Only a live mine that was just revealed detonates.");
        var model = state.FindModel(e.Model)!;
        var armour = state.Setup!.ProfileOf(e.Model)!.Armour;
        Require(e.Injury.NetDice == 0 && !e.Injury.Deadly && e.Injury.Modifier == InjuryRoll.CombineModifiers([armour]) && e.Injury.TargetAlreadyDown == (model.Status == ModelStatus.Down),
            "A mine's Injury Roll has no extra dice and no weapon modifier, only the model's armour.");
        Require(e.Negated == state.Setup!.ProfileOf(e.Model)!.Negates.HasFlag(WeaponKeywords.Shrapnel), "A mine adds its SHRAPNEL Blood Marker unless the model has NEGATE SHRAPNEL.");
        Require(e.Tough == ToughSaves(state, model, e.Injury.Outcome), "TOUGH turns the model's first Out of Action result into Down, and nothing else.");
        Require(e.Spared == MementoSaves(state, model, e.Injury.Outcome), "Memento Mori turns the model's first Out of Action result into No Effect, and nothing else.");
        Require(e.Warded == Wards(state, model, e.ResultMarkers + e.ExtraMarkers()), "The Sacrificial Lamb keeps off the first BLOOD MARKER placed on the model, and only that one.");
        var markers = Math.Min(BloodMarkers.Max, model.BloodMarkers + e.PlacedMarkers);
        var hurt = Hurt(model, e.Result, markers, e.Tough || e.Spared) with { WardSpent = model.WardSpent || e.Warded };
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
            MineStop = next is null ? null : halted with { Index = halted.Index + 1, Revealed = false, Live = false, DefuseTried = false },
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
