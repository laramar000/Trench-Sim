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

// Retreat ACTIONs (Comprehensive Rules pp. 34-35, 37, 49): the opponent's free attacks, then the move.
public class RetreatTests
{
    // A1 (4,2) is activated and engaged with B1 (5.4,2); B2 (4,3.4) also touches its 1" zone when `second` is set.
    private static BoardPlay Engaged(bool second = false, ModelProfile? profile = null, int perSide = 2)
    {
        var play = Skirmish("4", "2", "5.4", "2", profile, Math.Max(perSide, second ? 2 : perSide));
        if (second)
        {
            play.Put(B(2), "4", "3.4");
        }
        else if (perSide > 1)
        {
            play.Put(B(2), "30", "30");
        }
        return play;
    }

    private static RetreatModel Retreat(params (string X, string Y)[] path) =>
        new(Seat.One, A(1), path.Length == 0 ? Path(("1.5", "2")) : Path(path));

    private static FreeFight Attack(int by = 1) => new(Seat.Two, B(by));

    [Fact]
    [Trait("Rule", "Retreating p. 37: the opponent can Fight with each engaged enemy before the retreating model is moved")]
    public void FreeAttacksComeBeforeTheMove()
    {
        var play = Engaged();
        var declared = play.Do(Retreat()); // no dice yet
        Assert.IsType<RetreatDeclared>(declared.Events.Single(e => e is RetreatDeclared));
        Assert.Equal(new PendingDecision(DecisionKind.FreeFight, Seat.Two), play.State.Pending);
        Assert.Equal(P("4", "2"), play.M(A(1)).Position); // nothing has moved
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Retreat));

        var result = play.Do(Attack(), 3, 3); // the free attack misses, so the model moves
        Assert.True(result.Events.OfType<MeleeAttackDeclared>().Single().FreeFight);
        var resolved = result.Events.OfType<RetreatResolved>().Single();
        Assert.True(resolved.Moved);
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
        Assert.Equal(L("2.5"), resolved.Cost);
        Assert.Null(play.State.Retreat);
        Assert.Null(play.State.Pending);
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: the opponent 'can' fight, so it may decline")]
    public void TheOpponentMayDeclineEveryFreeAttack()
    {
        var play = Engaged();
        play.Do(Retreat());
        var result = play.Do(new DeclineFreeFights(Seat.Two)); // no dice
        Assert.Contains(result.Events, e => e is FreeFightsDeclined);
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: a free attack is a Melee Attack with 1 weapon, resolved with the usual Injury Roll")]
    public void AFreeAttackUsesTheNormalMeleeSequence()
    {
        var play = Engaged();
        play.Do(Retreat());
        var result = play.Do(Attack(), 4, 3, 2, 3); // hit, Minor Hit: still moves
        Assert.Single(result.Events.OfType<MeleeSuccessRolled>());
        Assert.Equal(InjuryOutcome.MinorHit, result.Events.OfType<InjuryRolled>().Single().Injury.Outcome);
        Assert.Equal(1, play.M(A(1)).BloodMarkers);
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
        Assert.Equal(ModelStatus.Standing, play.M(A(1)).Status);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: no move if the model suffered a Down result; Down Results p. 49: its Activation ends")]
    public void AModelTakenDownByAFreeAttackStaysPutAndItsActivationEnds()
    {
        var play = Engaged();
        play.Do(Retreat());
        var result = play.Do(Attack(), 4, 3, 4, 3); // Injury 7: Down
        var resolved = result.Events.OfType<RetreatResolved>().Single();
        Assert.False(resolved.Moved);
        Assert.Equal(P("4", "2"), play.M(A(1)).Position);
        Assert.Equal(ModelStatus.Down, play.M(A(1)).Status);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.TakenDown });
        Assert.Null(play.State.ActiveModel);
        Assert.Null(play.State.Retreat);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: no move if the model suffered an Out of Action result")]
    public void AModelTakenOutOfActionByAFreeAttackIsRemoved()
    {
        var play = Engaged();
        play.Do(Retreat());
        var result = play.Do(Attack(), 4, 3, 5, 4); // 9
        Assert.Equal(ModelStatus.OutOfAction, play.M(A(1)).Status);
        Assert.Null(play.M(A(1)).Position);
        Assert.False(result.Events.OfType<RetreatResolved>().Single().Moved);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.TakenDown });
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: each enemy model within 1\" may attack once; the opponent is asked again after each")]
    public void EachEngagedEnemyGetsOneFreeAttackAndTheOpponentIsAskedAgain()
    {
        var play = Engaged(second: true);
        play.Do(Retreat());
        Assert.Equal(new[] { B(1), B(2) }, Board.FreeFighters(play.State, play.State.Retreat!).Select(m => m.Id).ToArray());

        play.Do(Attack(1), 3, 3);
        Assert.Equal(new PendingDecision(DecisionKind.FreeFight, Seat.Two), play.State.Pending);
        Assert.Equal(P("4", "2"), play.M(A(1)).Position); // still waiting
        play.Refused(Attack(1), ReasonCode.NotAFreeFighter);   // B1 already attacked

        play.Do(Attack(2), 3, 3);
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: declining ends the remaining free attacks")]
    public void DecliningAfterOneAttackSkipsTheRest()
    {
        var play = Engaged(second: true);
        play.Do(Retreat());
        play.Do(Attack(1), 3, 3);
        play.Do(new DeclineFreeFights(Seat.Two));
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37 with Down Results p. 49: later free attacks hit a Down model with +1 INJURY DICE")]
    public void ASecondAttackerFindsTheRetreatingModelDown()
    {
        var play = Engaged(second: true);
        play.Do(Retreat());
        play.Do(Attack(1), 4, 3, 4, 3);                 // Down
        Assert.Equal(ModelStatus.Down, play.M(A(1)).Status);
        Assert.NotNull(play.State.Retreat);             // B2 may still attack
        play.Do(Attack(2), 4, 3);                       // the Down model has a marker: B2 is asked whether to spend it
        var result = play.Do(new SpendMarkers(Seat.Two, 0), 1, 1, 1); // 3 injury dice, the highest two
        Assert.Equal(1, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
        Assert.True(result.Events.OfType<InjuryRolled>().Single().Injury.TargetAlreadyDown);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.TakenDown });
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: once the retreating model is Out of Action nobody is left to attack")]
    public void NoMoreFreeAttacksOnceTheModelIsGone()
    {
        var play = Engaged(second: true);
        play.Do(Retreat());
        var result = play.Do(Attack(1), 4, 3, 6, 6);
        Assert.Null(play.State.Pending);
        Assert.Single(result.Events.OfType<MeleeAttackDeclared>());
        Assert.Null(play.State.Retreat);
    }

    [Fact]
    [Trait("Rule", "Critical Success (confirmed ruling): applies to a free attack too")]
    public void ACriticalFreeAttackAddsAnInjuryDie()
    {
        var play = Engaged();
        play.Do(Retreat());
        var result = play.Do(Attack(), 6, 6, 1, 2, 3);
        Assert.Equal(1, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
    }

    [Fact]
    [Trait("Rule", "Blood Markers pp. 27-28: the retreating model's owner may spend the free attacker's markers; the attacker may spend the retreating model's")]
    public void MarkerDecisionsRunBothWaysDuringAFreeAttack()
    {
        var play = Engaged();
        play.Mark(B(1), 1).Mark(A(1), 1);
        play.Do(Retreat());
        play.Do(Attack()); // no dice: the owner of the retreating model is asked first
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.One), play.State.Pending);
        play.Do(new SpendMarkers(Seat.One, 1), 6, 5, 1); // -1 DICE: lowest two 1 + 5 = 6, a miss
        Assert.Equal(0, play.M(B(1)).BloodMarkers);
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);

        var hit = Engaged();
        hit.Mark(A(1), 1);
        hit.Do(Retreat());
        hit.Do(Attack(), 4, 3);
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, Seat.Two), hit.State.Pending);
        var injured = hit.Do(new SpendMarkers(Seat.Two, 1), 2, 6, 5);
        Assert.Equal(1, injured.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
        Assert.Equal(ModelStatus.OutOfAction, hit.M(A(1)).Status);
    }

    [Fact]
    [Trait("Rule", "Success Rolls p. 50: a Risky free attack that fails does not end the retreating model's Activation")]
    public void AShakenFreeAttackerThatFailsJustMisses()
    {
        var play = Engaged();
        play.Shaken(Seat.Two);
        play.Do(Retreat());
        var result = play.Do(Attack(), 3, 3);
        Assert.True(result.Events.OfType<MeleeSuccessRolled>().Single().Check.RiskyFailure);
        Assert.DoesNotContain(result.Events, e => e is ActivationEnded);
        Assert.Equal(A(1), play.State.ActiveModel);
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Winning p. 51: a free attack can take the last model out of action")]
    public void ALastModelLostToAFreeAttackEndsTheMatch()
    {
        var play = Skirmish("4", "2", "5.4", "2", perSide: 1);
        play.Do(Retreat());
        var result = play.Do(Attack(), 4, 3, 5, 5);
        var ended = result.Events.OfType<MatchEnded>().Single();
        Assert.Equal((Seat.Two, OutcomeReason.AllEnemiesOutOfAction), (ended.Winner, ended.Reason));
        Assert.Null(play.State.Retreat);
    }

    // ---- what a Retreat may not do ----

    [Fact]
    [Trait("Rule", "Actions p. 34: a Retreat is for a model within 1\" of an enemy")]
    public void AFreeModelCannotRetreat()
    {
        var play = Skirmish("4", "2", "12", "2");
        play.Refused(Retreat(("2", "2")), ReasonCode.NotEngaged);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: the model must end more than 1\" from all enemies")]
    public void ARetreatEndingWithinAnInchOfAnyEnemyIsRefused()
    {
        var play = Engaged();
        play.Refused(Retreat(("3.5", "2")), ReasonCode.RetreatEndsEngaged);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: more than 1\" means a gap of 1\" is still engaged")]
    public void AGapOfExactlyAnInchIsNotEnough()
    {
        // B1 at (5.4,2): its base edge is at 4.9. The model's right edge must be more than 1" from it: x + 0.5 > 3.9.
        var play = Engaged();
        play.Refused(Retreat(("3.4", "2")), ReasonCode.RetreatEndsEngaged);
        play.Do(Retreat(("3.399", "2")));
    }

    [Fact]
    [Trait("Rule", "Moves p. 35: a retreat is no longer than the model's Movement")]
    public void ARetreatLongerThanMovementIsRefused()
    {
        var play = Skirmish("12", "2", "13.4", "2");
        play.Refused(Retreat(("5.999", "2")), ReasonCode.MoveIllegal);  // 6.001" away
        play.Do(Retreat(("6", "2")));                                      // exactly 6"
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: Movement is halved after standing up, a retreat included")]
    public void ADownModelRetreatsHalfAsFar()
    {
        var play = new BoardPlay(2, Everyone(Brawler)).ToActivation();
        play.Put(A(1), "10", "2").Put(B(1), "11.4", "2").Put(B(2), "30", "30").SetStatus(A(1), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Refused(new RetreatModel(Seat.One, A(1), Path(("6.999", "2"))), ReasonCode.MoveIllegal);
        play.Do(new RetreatModel(Seat.One, A(1), Path(("7", "2"))));
    }

    [Fact]
    [Trait("Rule", "Moves p. 35: no moving through an enemy base, even when retreating")]
    public void ARetreatCannotPassThroughAnEnemyBase()
    {
        var play = Engaged(second: true);
        play.Refused(new RetreatModel(Seat.One, A(1), Path(("4", "6"))), ReasonCode.MoveIllegal); // B2 is at (4,3.4)
    }

    [Fact]
    [Trait("Rule", "Moves p. 35: a retreat may not run into the 1\" of a different enemy")]
    public void ARetreatCannotEnterTheZoneOfAnEnemyItWasNotEngagedWith()
    {
        var play = Engaged();
        play.Put(B(2), "1.5", "4.2"); // 2.2" from the destination: its zone (2" to centre) is crossed on the way
        play.Refused(Retreat(("1.5", "2"), ("1", "2.5")), ReasonCode.MoveIllegal);
    }

    [Fact]
    [Trait("Rule", "Moves p. 35: a model may not finish on another model's base")]
    public void ARetreatCannotEndOnAFriend()
    {
        var play = Engaged();
        play.Put(A(2), "1.5", "2");
        play.Refused(Retreat(), ReasonCode.MoveIllegal);
    }

    [Fact]
    [Trait("Rule", "Moves p. 35: a retreat may pass over a friendly model")]
    public void ARetreatMayCrossAFriendlyModel()
    {
        var play = Engaged();
        play.Put(A(2), "2.8", "2");
        play.Do(Retreat());
        play.Do(new DeclineFreeFights(Seat.Two));
        Assert.Equal(P("1.5", "2"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: an empty or oversized retreat path is refused")]
    public void BadPathsAreRefusedWithoutDice()
    {
        var play = Engaged();
        play.Refused(new RetreatModel(Seat.One, A(1), []), ReasonCode.MoveIllegal);
        play.Refused(new RetreatModel(Seat.One, A(1), default), ReasonCode.MoveIllegal);
        play.Refused(new RetreatModel(Seat.One, A(1), [.. Enumerable.Repeat(P("1.5", "2"), ModelMoved.MaxWaypoints + 1)]), ReasonCode.TooManyWaypoints);
        play.Refused(new RetreatModel(Seat.One, A(1), Path(("-1", "2"))), ReasonCode.MoveIllegal); // off the table
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: Move or Charge or Retreat, not more than one")]
    public void RetreatExcludesMoveAndCharge()
    {
        var moved = Engaged();
        moved.Do(new MoveModel(Seat.One, A(1), Path(("4", "2.3")))); // a Move that stays within 1" of B1
        moved.Refused(Retreat(), ReasonCode.ActionAlreadyTaken);

        var charged = Skirmish("4", "2", "9", "2");
        charged.Do(new ChargeModel(Seat.One, A(1), B(1)), 4);
        charged.Refused(Retreat(("1", "2")), ReasonCode.ActionAlreadyTaken);

        var retreated = Engaged();
        retreated.Do(Retreat());
        retreated.Do(new DeclineFreeFights(Seat.Two));
        retreated.Refused(new MoveModel(Seat.One, A(1), Path(("1.4", "2"))), ReasonCode.ActionAlreadyTaken);
        retreated.Refused(new ChargeModel(Seat.One, A(1), B(1)), ReasonCode.ActionAlreadyTaken);
        retreated.Refused(Retreat(("1", "2")), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: Dash, Shoot and Retreat are different ACTIONs; a retreating model ends clear of the enemy")]
    public void ADashBeforeAndAShotAfterARetreatAreAllowed()
    {
        var play = Skirmish("12", "2", "13.4", "2", Gunman);
        play.Put(B(2), "30", "30");
        play.Do(new DashModel(Seat.One, A(1), Path(("11.8", "2"))), 6, 5); // a Dash that stays engaged
        play.Do(new RetreatModel(Seat.One, A(1), Path(("8", "2"))));
        play.Do(new DeclineFreeFights(Seat.Two));
        Assert.Equal(P("8", "2"), play.M(A(1)).Position);
        play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1); // not engaged any more, so it may shoot
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: a decision is pending, so nothing else is accepted")]
    public void WhileFreeAttacksAreOfferedOnlyTheOpponentsAnswerIsAccepted()
    {
        var play = Engaged(second: true);
        play.Do(Retreat());
        play.Refused(new EndActivation(Seat.One), ReasonCode.DecisionPending);
        play.Refused(new FightModel(Seat.One, A(1), B(1)), ReasonCode.DecisionPending);
        play.Refused(new FreeFight(Seat.One, B(1)), ReasonCode.NotYourTurn);
        play.Refused(new DeclineFreeFights(Seat.One), ReasonCode.NotYourTurn);
        play.Refused(new SpendMarkers(Seat.Two, 0), ReasonCode.WrongDecision);
        play.Refused(new FreeFight(Seat.Two, new ModelId("Z9")), ReasonCode.NotAFreeFighter);
        var legal = TurnSequence.LegalCommands(play.State);
        Assert.Equal(3, legal.Count);
        Assert.Contains(new DeclineFreeFights(Seat.Two), legal);
        Assert.Contains(Attack(1), legal);
        Assert.Contains(Attack(2), legal);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: only enemy models within 1\" that can fight make free attacks")]
    public void ADownOrDistantOrUnarmedEnemyCannotAttackTheRetreatingModel()
    {
        var down = Engaged(second: true);
        down.SetStatus(B(2), ModelStatus.Down);
        down.Do(Retreat());
        down.Refused(Attack(2), ReasonCode.NotAFreeFighter);

        var far = Engaged();
        far.Do(Retreat());
        far.Refused(Attack(2), ReasonCode.NotAFreeFighter); // B2 is at (30,30)

        var unarmed = Engaged(profile: Rifleman);
        unarmed.Put(A(1), "4", "2");
        unarmed.Do(Retreat());
        // Nobody with a Melee Weapon is within 1": the retreat simply happens.
        Assert.Equal(P("1.5", "2"), unarmed.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: a retreat with no one able to attack is just a move")]
    public void WithNoFreeAttackersTheMoveHappensAtOnce()
    {
        var play = Engaged();
        play.SetStatus(B(1), ModelStatus.Down);
        var result = play.Do(Retreat());
        Assert.Contains(result.Events, e => e is RetreatResolved { Moved: true });
        Assert.Null(play.State.Pending);
    }
}
