using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// The reducer is the only thing that changes a state, so it must refuse a Slice B event that Decide could never have
// produced: wrong dice, wrong positions, a decision that was not pending, a move that does not match its roll.
public class ForgedMeleeEventTests
{
    private static void Rejects(MatchState state, GameEvent forged) =>
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, forged));

    private static SuccessRollResult Success(int bonus, int penalty, bool risky, params int[] dice) =>
        SuccessRoll.Roll(new FixedDice(dice), bonus, penalty, risky);

    private static InjuryRollResult Injury(int bonus, int penalty, int[] modifiers, bool down, params int[] dice) =>
        InjuryRoll.Roll(new FixedDice(dice), bonus, penalty, modifiers, down);

    private static MatchState Apply(MatchState state, IEnumerable<GameEvent> events) => events.Aggregate(state, Reducer.Apply);

    // ---- Charge ----

    [Fact]
    [Trait("Rule", "Charging p. 36: the reducer re-checks the declaration")]
    public void AChargeDeclaredOutOfRangeOrWhileEngagedIsRefused()
    {
        var far = Skirmish("4", "2", "20", "2");
        Rejects(far.State, new ChargeDeclared(A(1), B(1)));
        var engaged = Skirmish("4", "2", "5.4", "2");
        Rejects(engaged.State, new ChargeDeclared(A(1), B(1)));
        var friend = Skirmish("4", "2", "12", "2");
        Rejects(friend.State, new ChargeDeclared(A(1), A(2)));
        Rejects(friend.State, new ChargeDeclared(B(1), A(1))); // not the active model
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: the reducer enforces the action ledger")]
    public void AChargeAfterMoveShootOrCharge()
    {
        foreach (var taken in new[] { ActionsTaken.Move, ActionsTaken.Shoot, ActionsTaken.Charge, ActionsTaken.Retreat })
        {
            var play = Skirmish("4", "2", "12", "2");
            play.State = play.State with { Actions = taken };
            Rejects(play.State, new ChargeDeclared(A(1), B(1)));
        }
        var moved = Skirmish("4", "2", "12", "2");
        moved.State = moved.State with { Actions = ActionsTaken.Charge };
        Rejects(moved.State, new ModelMoved(A(1), MoveKind.Move, Path(("5", "2")), L("1")));
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: the charge move must be the one the roll allows")]
    public void AChargeMoveThatDoesNotMatchItsRollIsRefused()
    {
        var play = Skirmish("4", "2", "14", "2"); // gap 9"
        var honest = TurnSequence.Decide(play.State, new ChargeModel(Seat.One, A(1), B(1)), new FixedDice(1)).Events;
        var moved = (ChargeMoved)honest[2];
        Assert.False(moved.Reached);
        var afterRoll = Apply(play.State, honest.Take(2));

        Rejects(afterRoll, new ChargeMoved(A(1), B(1), moved.Path, moved.Cost, true));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), moved.Path, moved.Cost + L("0.001"), false));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), [P("11.001", "2")], moved.Cost, false));
        Rejects(afterRoll, new ChargeMoved(A(1), B(1), [], Length.Zero, false));
        // The path of a bigger roll is not allowed after a small one.
        var big = (ChargeMoved)TurnSequence.Decide(play.State, new ChargeModel(Seat.One, A(1), B(1)), new FixedDice(6)).Events[2];
        Rejects(afterRoll, big);
        // And the right move is accepted.
        Assert.Equal(P("11", "2"), Reducer.Apply(afterRoll, moved).FindModel(A(1))!.Position);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: events out of order are refused")]
    public void ChargeEventsNeedTheirPredecessors()
    {
        var play = Skirmish("4", "2", "12", "2");
        Rejects(play.State, new ChargeRolled(A(1), B(1), 3));
        Rejects(play.State, new ChargeMoved(A(1), B(1), [P("11", "2")], L("7"), true));
        var declared = Reducer.Apply(play.State, new ChargeDeclared(A(1), B(1)));
        Rejects(declared, new ChargeMoved(A(1), B(1), [P("11", "2")], L("7"), true)); // before the roll
        Rejects(declared, new ChargeRolled(A(1), B(2), 3));                              // another target
        Rejects(declared, new ChargeDeclared(A(1), B(1)));                               // twice
        Rejects(declared, new ActivationEnded(A(1), ActivationEndReason.Voluntary));                        // activation cannot end mid-charge
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChargeRolled(A(1), B(1), 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChargeRolled(A(1), B(1), 0));
    }

    // ---- Retreat ----

    [Fact]
    [Trait("Rule", "Retreating p. 37: the reducer re-checks the declared path")]
    public void ARetreatDeclaredByAFreeModelOrToABadPlaceIsRefused()
    {
        var free = Skirmish("4", "2", "12", "2");
        Rejects(free.State, new RetreatDeclared(A(1), Path(("2", "2"))));
        var engaged = Skirmish("4", "2", "5.4", "2");
        Rejects(engaged.State, new RetreatDeclared(A(1), Path(("3.5", "2"))));   // still within 1"
        Rejects(engaged.State, new RetreatDeclared(A(1), Path(("-1", "2"))));    // off the table
        Rejects(engaged.State, new RetreatDeclared(A(1), Path(("-5", "2"))));
        engaged.State = engaged.State with { Actions = ActionsTaken.Move };
        Rejects(engaged.State, new RetreatDeclared(A(1), Path(("1.5", "2"))));
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: the move cannot be finished while free attacks are still on offer")]
    public void ARetreatResolvedTooEarlyOrWithTheWrongMoveIsRefused()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        var declared = Reducer.Apply(play.State, new RetreatDeclared(A(1), Path(("1.5", "2"))));
        var asked = Reducer.Apply(declared, new DecisionRequested(DecisionKind.FreeFight, Seat.Two));
        Rejects(asked, new RetreatResolved(A(1), Path(("1.5", "2")), L("2.5")));  // a decision is pending
        Rejects(declared, new RetreatResolved(A(1), Path(("1.5", "2")), L("2.5"))); // B1 could still attack

        var declined = Reducer.Apply(asked, new FreeFightsDeclined(Seat.Two));
        Rejects(declined, new RetreatResolved(A(1), Path(("1.5", "2")), L("2.4")));  // wrong cost
        Rejects(declined, new RetreatResolved(A(1), Path(("1.4", "2")), L("2.6")));  // not the declared path
        Rejects(declined, new RetreatResolved(A(1), [], Length.Zero));               // standing models move
        Assert.Equal(P("1.5", "2"), Reducer.Apply(declined, new RetreatResolved(A(1), Path(("1.5", "2")), L("2.5"))).FindModel(A(1))!.Position);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: a model taken Down cannot move, and only such a model stays put")]
    public void ADownRetreaterDoesNotMoveAndAStandingOneDoes()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        var declared = Reducer.Apply(play.State, new RetreatDeclared(A(1), Path(("1.5", "2"))));
        var down = declared with { Models = [.. declared.Models.Select(m => m.Id == A(1) ? m with { Status = ModelStatus.Down } : m)] };
        var closed = down with { Retreat = down.Retreat! with { Closed = true } };
        Rejects(closed, new RetreatResolved(A(1), Path(("1.5", "2")), L("2.5")));
        var resolved = Reducer.Apply(closed, new RetreatResolved(A(1), [], Length.Zero));
        Assert.Equal(P("4", "2"), resolved.FindModel(A(1))!.Position);
        Assert.Null(resolved.Retreat);
        Assert.Throws<ArgumentException>(() => new RetreatResolved(A(1), [], L("1")));
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: an activation ends as taken Down only for a model that is Down or gone")]
    public void TakenDownNeedsADownModel()
    {
        var play = Skirmish("4", "2", "12", "2");
        Rejects(play.State, new ActivationEnded(A(1), ActivationEndReason.TakenDown));
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: free attacks answer an offer made to the right seat, for the right target")]
    public void AFreeAttackNeedsItsOffer()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        Rejects(play.State, new MeleeAttackDeclared(B(1), A(1), true, null));      // no retreat
        var declared = Reducer.Apply(play.State, new RetreatDeclared(A(1), Path(("1.5", "2"))));
        Rejects(declared, new MeleeAttackDeclared(B(1), A(1), true, null));        // no offer yet
        Rejects(declared, new DecisionRequested(DecisionKind.FreeFight, Seat.One)); // offered to the wrong seat
        var asked = Reducer.Apply(declared, new DecisionRequested(DecisionKind.FreeFight, Seat.Two));
        Rejects(asked, new MeleeAttackDeclared(B(2), A(1), true, null));           // B2 is not within 1"
        Rejects(asked, new MeleeAttackDeclared(B(1), A(2), true, null));           // not the retreating model
        Rejects(asked, new MeleeAttackDeclared(B(1), A(1), true, "obj-1"));        // no cover there
        Rejects(asked, new FreeFightsDeclined(Seat.One));
        var attacked = Reducer.Apply(asked, new MeleeAttackDeclared(B(1), A(1), true, null));
        Assert.Equal(new[] { B(1) }, attacked.Retreat!.Fought.ToArray());
        Assert.Null(attacked.Pending);
    }

    // ---- Fight ----

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: the reducer re-checks the target, weapon, ledger and cover")]
    public void AMeleeAttackDeclaredAgainstTheRulesIsRefused()
    {
        var apart = Skirmish("4", "2", "9", "2");
        Rejects(apart.State, new MeleeAttackDeclared(A(1), B(1), false, null));          // not within 1"
        var near = Skirmish("4", "2", "5.4", "2");
        Rejects(near.State, new MeleeAttackDeclared(A(1), B(1), false, "obj-1"));        // claims a cover that is not there
        Rejects(near.State, new MeleeAttackDeclared(A(1), A(2), false, null));           // a friend
        Rejects(near.State, new MeleeAttackDeclared(B(1), A(1), false, null));           // not the active model
        near.State = near.State with { Actions = ActionsTaken.Shoot };
        Rejects(near.State, new MeleeAttackDeclared(A(1), B(1), false, null));
        near.State = near.State with { Actions = ActionsTaken.Fight };
        Rejects(near.State, new MeleeAttackDeclared(A(1), B(1), false, null));
        var unarmed = Skirmish("4", "2", "5.4", "2", Rifleman);
        Rejects(unarmed.State, new MeleeAttackDeclared(A(1), B(1), false, null));
    }

    [Fact]
    [Trait("Rule", "Success Rolls pp. 24-25, 50: the melee roll must carry the declared dice and the right Risky flag")]
    public void AMeleeSuccessRollWithTheWrongDiceOrRiskIsRefused()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        var declared = Reducer.Apply(play.State, new MeleeAttackDeclared(A(1), B(1), false, null));
        Rejects(declared, new MeleeSuccessRolled(A(1), B(1), Success(1, 0, false, 4, 3, 2)));  // a die nobody gave
        Rejects(declared, new MeleeSuccessRolled(A(1), B(1), Success(0, 1, false, 4, 3, 2)));
        Rejects(declared, new MeleeSuccessRolled(A(1), B(1), Success(0, 0, true, 4, 3)));      // Risky but not Shaken
        Rejects(declared, new MeleeSuccessRolled(A(1), B(2), Success(0, 0, false, 4, 3)));     // another target
        Rejects(play.State, new MeleeSuccessRolled(A(1), B(1), Success(0, 0, false, 4, 3)));   // nothing declared

        var shaken = Skirmish("4", "2", "5.4", "2");
        shaken.Shaken(Seat.One);
        var risky = Reducer.Apply(shaken.State, new MeleeAttackDeclared(A(1), B(1), false, null));
        Rejects(risky, new MeleeSuccessRolled(A(1), B(1), Success(0, 0, false, 4, 3)));        // must be Risky
        Reducer.Apply(risky, new MeleeSuccessRolled(A(1), B(1), Success(0, 0, true, 4, 3)));
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49 and Critical Success: the Injury Roll must carry exactly the bonus dice it earned")]
    public void AMeleeInjuryRollWithTheWrongDiceModifierOrTargetIsRefused()
    {
        ModelProfile Pick(ModelId id) => id.Value[0] == 'A'
            ? new ModelProfile(L("6"), L("0.5"), Length.Zero, rangedInjuryModifier: 3, hasMeleeWeapon: true, meleeInjuryModifier: 1)
            : new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -1, hasMeleeWeapon: true);
        var play = new BoardPlay(2, Pick).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.4", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.SetStatus(B(1), ModelStatus.Down);

        var declared = Reducer.Apply(play.State, new MeleeAttackDeclared(A(1), B(1), false, null));
        var hit = Reducer.Apply(declared, new MeleeSuccessRolled(A(1), B(1), Success(0, 0, false, 4, 3)));
        Assert.Equal(1, hit.InProgress!.BonusDice); // the Down target's extra die

        var honest = Injury(1, 0, [1, -1], true, 2, 3, 4);
        Reducer.Apply(hit, new InjuryRolled(A(1), B(1), honest));
        Rejects(hit, new InjuryRolled(A(1), B(1), Injury(0, 0, [1, -1], true, 2, 3)));      // the Down bonus is missing
        Rejects(hit, new InjuryRolled(A(1), B(1), Injury(2, 0, [1, -1], true, 2, 3, 4, 5))); // one die too many
        Rejects(hit, new InjuryRolled(A(1), B(1), Injury(1, 0, [3, -1], true, 2, 3, 4)));    // the ranged modifier
        Rejects(hit, new InjuryRolled(A(1), B(1), Injury(1, 0, [1, 0], true, 2, 3, 4)));     // armour left out
        Rejects(hit, new InjuryRolled(A(1), B(1), Injury(1, 0, [1, -1], false, 2, 3, 4)));   // claims the target stood
        Rejects(hit, new InjuryRolled(A(1), A(2), Injury(1, 0, [1, -1], false, 2, 3, 4)));   // a friend, with no forced shot
        Rejects(declared, new InjuryRolled(A(1), B(1), honest));                              // no hit yet
    }

    [Fact]
    [Trait("Rule", "Critical Success (confirmed ruling): the reducer gives a Critical melee hit its extra injury die")]
    public void ACriticalMeleeHitNeedsTheExtraInjuryDie()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        var declared = Reducer.Apply(play.State, new MeleeAttackDeclared(A(1), B(1), false, null));
        var critical = Reducer.Apply(declared, new MeleeSuccessRolled(A(1), B(1), Success(0, 0, false, 6, 6)));
        Assert.Equal(1, critical.InProgress!.BonusDice);
        Rejects(critical, new InjuryRolled(A(1), B(1), Injury(0, 0, [0, 0], false, 3, 3)));
        Reducer.Apply(critical, new InjuryRolled(A(1), B(1), Injury(1, 0, [0, 0], false, 3, 3, 3)));
    }

    // ---- shooting into melee ----

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: the declaration must say whether the D6 applies, truthfully")]
    public void AShotIntoMeleeDeclaredAsOrdinaryOrTheOtherWayRoundIsRefused()
    {
        var play = new BoardPlay(3, Everyone(Gunman)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(A(2), "12", "3.2").Put(A(3), "2", "20").Put(B(2), "30", "30").Put(B(3), "30", "32");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var facts = Board.Assess(play.State, play.M(A(1)), play.M(B(1)));
        Assert.True(facts.IntoMelee);

        RangedAttackDeclared Declare(bool intoMelee) =>
            new(A(1), B(1), false, false, null, facts.AttackerLevel, facts.TargetLevel, 0) { IntoMelee = intoMelee };
        Rejects(play.State, Declare(false));
        var declared = Reducer.Apply(play.State, Declare(true));
        Assert.Equal(RollStage.TargetRoll, declared.InProgress!.Stage);

        var plain = Skirmish("4", "2", "12", "2", Gunman);
        var plainFacts = Board.Assess(plain.State, plain.M(A(1)), plain.M(B(1)));
        Rejects(plain.State, new RangedAttackDeclared(A(1), B(1), false, false, null, plainFacts.AttackerLevel, plainFacts.TargetLevel, 0) { IntoMelee = true });
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: the target roll and the friendly choice follow each other and nothing else")]
    public void TargetRollEventsAreCheckedInSequence()
    {
        var play = new BoardPlay(3, Everyone(Gunman)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(A(2), "12", "3.2").Put(A(3), "2", "20").Put(B(2), "30", "30").Put(B(3), "30", "32");
        play.Do(new ActivateModel(Seat.One, A(1)));
        Rejects(play.State, new ShootingTargetRolled(A(1), B(1), 2));

        var shot = TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice(2)).Events;
        var afterDeclared = Apply(play.State, shot.Take(1));
        Rejects(afterDeclared, new RangedSuccessRolled(A(1), B(1), Success(0, 0, false, 4, 3))); // no D6 yet
        Rejects(afterDeclared, new FriendlyTargetChosen(A(1), A(2), false, false, null));         // no D6 yet
        Rejects(afterDeclared, new ShootingTargetRolled(A(1), B(2), 2));                          // another target

        var friendly = Apply(play.State, shot);
        Assert.Equal(RollStage.TargetChoice, friendly.InProgress!.Stage);
        Rejects(friendly, new ShootingTargetRolled(A(1), B(1), 5));                                  // rolled twice
        Rejects(friendly, new FriendlyTargetChosen(A(1), A(3), false, false, null));                  // A3 is not within 1"
        Rejects(friendly, new FriendlyTargetChosen(A(1), B(1), false, false, null));                  // an enemy
        Rejects(friendly, new FriendlyTargetChosen(A(1), A(1), false, false, null));                  // the shooter
        Rejects(friendly, new FriendlyTargetChosen(A(1), A(2), true, false, null));                   // wrong range band
        Rejects(friendly, new DecisionRequested(DecisionKind.ChooseFriendlyTarget, Seat.Two));        // wrong seat
        var chosen = Reducer.Apply(friendly, new FriendlyTargetChosen(A(1), A(2), false, false, null));
        Assert.Equal((A(2), true), (chosen.InProgress!.Target, chosen.InProgress.FriendlyFire));
        Assert.Null(chosen.Pending);
        // The enemy cannot be hurt by a forced shot.
        var hit = Reducer.Apply(chosen, new RangedSuccessRolled(A(1), A(2), Success(0, 0, false, 4, 3)));
        Rejects(hit, new InjuryRolled(A(1), B(1), Injury(0, 0, [0, 0], false, 3, 3)));

        var enemyOk = Apply(play.State, TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice(5, 1, 1)).Events.Take(2));
        Assert.Equal(RollStage.ShotRoll, enemyOk.InProgress!.Stage);
        Rejects(enemyOk, new FriendlyTargetChosen(A(1), A(2), false, false, null));
    }

    // ---- hashes ----

    [Fact]
    [Trait("Rule", "Architecture: the state hash covers the new state")]
    public void TheStateHashSeesRetreatAndStageDetails()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        var before = StateHash.Compute(play.State);
        var declared = Reducer.Apply(play.State, new RetreatDeclared(A(1), Path(("1.5", "2"))));
        var asked = Reducer.Apply(declared, new DecisionRequested(DecisionKind.FreeFight, Seat.Two));
        var attacked = Reducer.Apply(asked, new MeleeAttackDeclared(B(1), A(1), true, null));
        var hashes = new[] { before, StateHash.Compute(declared), StateHash.Compute(asked), StateHash.Compute(attacked) };
        Assert.Equal(4, hashes.Distinct().Count());
        Assert.Contains("retreat=", StateHash.Canonical(declared));
        Assert.Contains("melee=1,meleeMod=0", StateHash.Canonical(declared));
        Assert.Equal(5, StateHash.Version);
    }
}
