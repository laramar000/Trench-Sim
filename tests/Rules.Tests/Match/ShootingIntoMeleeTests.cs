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

// Shooting into melee (Comprehensive Rules p. 42; Core Rules p. 16): a target within 1" of one of the shooter's own models.
public class ShootingIntoMeleeTests
{
    // A1 (4,2) shoots B1 (12,2); A2 stands 0.2" from B1, at (12,3.2). A3 is the shooter's spare model.
    private static BoardPlay Setup(Func<ModelId, ModelProfile>? profiles = null)
    {
        var play = new BoardPlay(3, profiles ?? Everyone(Gunman)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(A(2), "12", "3.2").Put(A(3), "2", "20");
        play.Put(B(2), "30", "30").Put(B(3), "30", "32");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Shoot Fire(int target = 1) => new(Seat.One, A(1), B(target));

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: on a 4-6 the enemy model is the target and the shot goes on as normal")]
    public void AFourOrMoreLetsTheShooterTargetTheEnemy()
    {
        var play = Setup();
        var result = play.Do(Fire(), 4, 4, 3, 5, 4); // D6 4; Success 7; Injury 9
        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.IntoMelee);
        var target = result.Events.OfType<ShootingTargetRolled>().Single();
        Assert.Equal((4, false), (target.Die, target.HitsFriend));
        Assert.Empty(result.Events.OfType<FriendlyTargetChosen>());
        Assert.Equal(B(1), result.Events.OfType<InjuryRolled>().Single().Target);
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
        Assert.Equal(ModelStatus.Standing, play.M(A(2)).Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [Trait("Rule", "Shooting into Melee p. 42: on a 1-3 the shooter must target one of its own models")]
    public void AOneToThreeForcesTheShooterOntoItsOwnModel(int die)
    {
        var play = Setup();
        var result = play.Do(Fire(), die);
        Assert.True(result.Events.OfType<ShootingTargetRolled>().Single().HitsFriend);
        Assert.Equal(new PendingDecision(DecisionKind.ChooseFriendlyTarget, Seat.One), play.State.Pending);
        Assert.Equal(RollStage.TargetChoice, play.State.InProgress!.Stage);
        Assert.Empty(result.Events.OfType<RangedSuccessRolled>()); // no Success Roll before the choice
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: take a Success Roll as normal once the target has been determined")]
    public void TheForcedShotIsAnOrdinarySuccessRollThenInjuryRollAgainstTheFriend()
    {
        var play = Setup();
        play.Do(Fire(), 2);
        var result = play.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 4, 3, 5, 4);
        Assert.Equal(A(2), result.Events.OfType<FriendlyTargetChosen>().Single().Victim);
        var roll = result.Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal((A(1), A(2)), (roll.Attacker, roll.Target));
        var injury = result.Events.OfType<InjuryRolled>().Single();
        Assert.Equal((A(1), A(2)), (injury.Attacker, injury.Target));
        Assert.Equal(ModelStatus.OutOfAction, play.M(A(2)).Status);
        Assert.Null(play.M(A(2)).Position);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
        Assert.Null(play.State.InProgress);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Shoot));
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: a forced shot that misses hurts nobody")]
    public void AForcedShotCanMiss()
    {
        var play = Setup();
        play.Do(Fire(), 3);
        var result = play.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 2, 2);
        Assert.Empty(result.Events.OfType<InjuryRolled>());
        Assert.Equal(ModelStatus.Standing, play.M(A(2)).Status);
        Assert.Null(play.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: the shooter chooses among its own models within 1\" of the target")]
    public void OnlyAFriendWithin1InchOfTheTargetCanBeChosen()
    {
        var play = Setup();
        play.Put(A(3), "12", "0.8"); // also beside B1
        play.Do(Fire(), 1);
        play.Refused(new ChooseFriendlyTarget(Seat.One, A(1)), ReasonCode.NotACandidate); // the shooter itself
        play.Refused(new ChooseFriendlyTarget(Seat.One, B(1)), ReasonCode.NotACandidate);
        play.Refused(new ChooseFriendlyTarget(Seat.One, B(2)), ReasonCode.NotACandidate);
        play.Refused(new ChooseFriendlyTarget(Seat.One, new ModelId("Z9")), ReasonCode.NotACandidate);
        play.Refused(new ChooseFriendlyTarget(Seat.Two, A(2)), ReasonCode.NotYourTurn);
        var legal = TurnSequence.LegalCommands(play.State);
        Assert.Equal(new Command[] { new ChooseFriendlyTarget(Seat.One, A(2)), new ChooseFriendlyTarget(Seat.One, A(3)) }, legal.ToArray());
        play.Do(new ChooseFriendlyTarget(Seat.One, A(3)), 1, 1);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: any of your models counts, a Down one included")]
    public void ADownFriendIsAValidCandidateAndAnOutOfActionOneIsNot()
    {
        var down = Setup();
        down.SetStatus(A(2), ModelStatus.Down);
        down.Do(Fire(), 1);
        down.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 1, 1);

        var gone = Setup();
        gone.SetStatus(A(2), ModelStatus.OutOfAction);
        gone.Do(Fire(), 1, 1); // nobody is on the table next to B1: an ordinary shot, so the D6 is not rolled
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: only the shooter's own models trigger the roll")]
    public void AnEnemyModelNextToTheTargetDoesNotTriggerTheRoll()
    {
        var play = Setup();
        play.Put(A(2), "30", "20").Put(B(2), "12", "3.2");
        var declared = play.Do(Fire(), 1, 1).Events.OfType<RangedAttackDeclared>().Single();
        Assert.False(declared.IntoMelee);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: at exactly 1\" the target is still in melee, beyond it is not")]
    public void TheRollAppliesWithin1InchEdgeToEdge()
    {
        var edge = Setup();
        edge.Put(A(2), "12", "4"); // gap exactly 1"
        Assert.True(edge.Do(Fire(), 4, 1, 1).Events.OfType<RangedAttackDeclared>().Single().IntoMelee);

        var beyond = Setup();
        beyond.Put(A(2), "12", "4.001");
        Assert.False(beyond.Do(Fire(), 1, 1).Events.OfType<RangedAttackDeclared>().Single().IntoMelee);
    }

    [Fact]
    [Trait("Rule", "Ranged Attacks pp. 42-43: Long Range and Cover are worked out for the model actually shot at (U51)")]
    public void TheForcedShotUsesTheFriendsRangeAndCover()
    {
        // B1 at (15.2,2) is 10.2" away (Long Range for an 18" weapon); A2 at (14,2) is 9" away (Short Range).
        var play = Setup();
        play.Put(B(1), "15.2", "2").Put(A(2), "14", "2");
        var declared = play.Do(Fire(), 2).Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.LongRange);
        var result = play.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 6, 6, 1, 1, 1);
        var chosen = result.Events.OfType<FriendlyTargetChosen>().Single();
        Assert.False(chosen.LongRange);
        Assert.False(chosen.InCover);
        Assert.Equal(0, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 43: Long Range still costs a die when the enemy may be targeted")]
    public void AnEnemyTargetKeepsItsOwnPenaltyDice()
    {
        var play = Setup();
        play.Put(B(1), "15.2", "2").Put(A(2), "14", "2");
        var result = play.Do(Fire(), 5, 6, 5, 1); // D6 5; Long Range: 3 dice, lowest two (5, 1) = 6
        Assert.Equal(-1, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Critical Success (confirmed ruling): applies to a forced shot at a friend too")]
    public void AForcedCriticalAddsAnInjuryDie()
    {
        var play = Setup();
        play.Do(Fire(), 1);
        var result = play.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 6, 6, 1, 2, 3);
        Assert.Equal(1, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: a forced shot's Success Roll can still be reduced by the opponent, after the target is known")]
    public void MarkerDecisionsFollowTheTargetRoll()
    {
        // 4-6: the opponent is asked about the shooter's markers once the enemy is confirmed as the target.
        var enemy = Setup();
        enemy.Mark(A(1), 2);
        enemy.Do(Fire(), 5);
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), enemy.State.Pending);
        Assert.Equal(RollStage.ShotRoll, enemy.State.InProgress!.Stage);
        enemy.Do(new SpendMarkers(Seat.Two, 2), 6, 6, 6, 1, 3, 3); // -2 dice: lowest two of 6, 6, 6, 1 = 7, a hit; Injury 6
        Assert.Equal(0, enemy.M(A(1)).BloodMarkers);

        // 1-3: the shooter chooses first, then the opponent is asked about the Success Roll.
        var friend = Setup();
        friend.Mark(A(1), 1);
        friend.Do(Fire(), 2);
        Assert.Equal(DecisionKind.ChooseFriendlyTarget, friend.State.Pending!.Kind);
        friend.Do(new ChooseFriendlyTarget(Seat.One, A(2)));
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), friend.State.Pending);
        friend.Do(new SpendMarkers(Seat.Two, 0), 1, 1);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: the injury spend is for 'your opponent', so a forced shot at a friend offers none")]
    public void NoOneSpendsTheFriendsMarkersOnAForcedShot()
    {
        var play = Setup();
        play.Mark(A(2), 3);
        play.Do(Fire(), 1);
        var result = play.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 4, 3, 1, 1);
        Assert.Null(play.State.Pending);
        Assert.Equal(InjuryOutcome.MinorHit, result.Events.OfType<InjuryRolled>().Single().Injury.Outcome);
        Assert.Equal(4, play.M(A(2)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: a hit on the enemy after the D6 still offers the injury spend")]
    public void AHitOnTheEnemyOffersTheInjurySpend()
    {
        var play = Setup();
        play.Mark(B(1), 2);
        play.Do(Fire(), 5, 4, 3);
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), play.State.Pending);
        var result = play.Do(new SpendMarkers(Seat.One, 2), 1, 1, 1, 1);
        Assert.Equal(2, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
        Assert.Equal(1, play.M(B(1)).BloodMarkers); // both spent, then a Minor Hit marks it once
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: the shot still needs Line of Sight and range to the intended target")]
    public void TheIntendedTargetMustStillBeLegal()
    {
        var far = Setup();
        far.Put(B(1), "23.1", "2").Put(A(2), "23.1", "3.2"); // gap 18.1" from A1, weapon range 18"
        far.Refused(Fire(), ReasonCode.TargetOutOfRange);

        var hidden = Setup();
        hidden.Put(A(1), "17", "6").Put(B(1), "17", "14").Put(A(2), "17", "15.2"); // wall-1 (14,11)-(20,12) between
        hidden.Refused(Fire(), ReasonCode.NoLineOfSight);
    }

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 42: a model within 1\" of an enemy cannot shoot, melee or not")]
    public void AnEngagedShooterStillCannotShoot()
    {
        var play = Setup();
        play.Put(B(2), "4", "3.2");
        play.Refused(Fire(), ReasonCode.AttackerEngaged);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: decisions of the wrong kind are refused without dice")]
    public void ChoicesOutOfSequenceAreRefused()
    {
        var play = Setup();
        play.Refused(new ChooseFriendlyTarget(Seat.One, A(2)), ReasonCode.NoDecisionPending);
        play.Do(Fire(), 1);
        play.Refused(Fire(), ReasonCode.DecisionPending);
        play.Refused(new SpendMarkers(Seat.One, 0), ReasonCode.WrongDecision);
        play.Refused(new EndActivation(Seat.One), ReasonCode.DecisionPending);
        play.Refused(new FreeFight(Seat.One, B(1)), ReasonCode.WrongDecision);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: a shot into a melee that is a real fight")]
    public void ChargeThenAFriendsShotIntoTheMeleeUsesTheSameRule()
    {
        // A2 charges B1 and is now in melee with it; A1, activating later, shoots into it.
        var play = new BoardPlay(3, Everyone(Gunman)).ToActivation();
        play.Put(A(1), "4", "2").Put(A(2), "9", "2").Put(B(1), "14", "2").Put(B(2), "30", "30").Put(A(3), "2", "20").Put(B(3), "30", "32");
        play.Do(new ActivateModel(Seat.One, A(2)));
        play.Do(new ChargeModel(Seat.One, A(2), B(1)), 6);
        Assert.True(Within(play, A(2), B(1), "1"));
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(2)));
        play.Do(new EndActivation(Seat.Two));
        play.Do(new ActivateModel(Seat.One, A(1)));
        var declared = play.Do(Fire(), 6, 1, 1).Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.IntoMelee);
    }
}
