using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

public class RangedAttackTests
{
    // A1 is activated at (4,2) and B1 stands in the open on the clear strip along the south edge of the table.
    private static BoardPlay Duel(string targetX = "10", string targetY = "2", Func<ModelId, ModelProfile>? profiles = null, int perSide = 2)
    {
        var play = new BoardPlay(perSide, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), targetX, targetY);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Shoot Fire() => new(Seat.One, A(1), B(1));

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 44: Success Roll 7+, then an Injury Roll")]
    public void AShotAtShortRangeIsTwoDiceThenAnInjuryRoll()
    {
        var play = Duel();
        var result = play.Do(Fire(), 4, 3, 5, 4);

        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.False(declared.LongRange);
        Assert.False(declared.InCover);
        Assert.Equal(0, declared.PenaltyDice);
        var roll = result.Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal((0, 7, SuccessOutcome.Success), (roll.Check.NetDice, roll.Check.Dice.Total, roll.Check.Outcome));
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((0, 9, InjuryOutcome.OutOfAction), (injury.NetDice, injury.Total, injury.Outcome));
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
        Assert.Null(play.M(B(1)).Position);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Shoot));
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 44: a miss ends the attack")]
    public void AMissRollsNoInjuryDice()
    {
        var play = Duel();
        var result = play.Do(Fire(), 3, 3);
        Assert.Empty(result.Events.OfType<InjuryRolled>());
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
        Assert.Null(play.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "Short Range & Long Range p. 44: more than half the range is Long Range, -1 DICE")]
    public void LongRangeAddsOnePenaltyDieAndTheLowestTwoCount()
    {
        // 18" range: gap 9" is Short Range, a hair over is Long. Bases have radius 0.5, so centres 10" apart is gap 9".
        var shortShot = Duel("14", "2");
        Assert.False(shortShot.Do(Fire(), 1, 1).Events.OfType<RangedAttackDeclared>().Single().LongRange);

        var longShot = Duel("14.001", "2");
        var result = longShot.Do(Fire(), 6, 5, 2, 1, 1);
        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.LongRange);
        Assert.Equal(1, declared.PenaltyDice);
        var roll = result.Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal(-1, roll.Check.NetDice);
        Assert.Equal(7, roll.Check.Dice.Total); // the lowest two of 6, 5, 2
    }

    [Fact]
    [Trait("Rule", "Range p. 44: edge to edge, at most the weapon's range")]
    public void ARangeOfExactlyTheWeaponsRangeIsInAndAnyMoreIsOut()
    {
        var inRange = Duel("23", "2"); // gap 18"
        inRange.Do(Fire(), 1, 1, 1);
        var outOfRange = Duel("23.001", "2");
        outOfRange.Refused(Fire(), ReasonCode.TargetOutOfRange);
    }

    [Fact]
    [Trait("Rule", "Cover p. 38 and Ranged Attacks p. 44: Cover is -1 DICE")]
    public void CoverAddsOnePenaltyDieAndMatchesTheGeometry()
    {
        // B1 stands against the south edge of the rubble obj-1 (x 10 to 14, y 4 to 8), A1 shoots over it from the north-west.
        var play = Duel("12", "3.6");
        play.Put(A(1), "4", "6");
        var truth = Cover.Of(play.Setup.Scenario!.Battlefield, new ModelBody("A1", P("4", "6"), L("0.5")), new ModelBody("B1", P("12", "3.6"), L("0.5")));
        Assert.True(truth.InCover);

        var result = play.Do(Fire(), 6, 6, 1, 1, 1);
        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.InCover);
        Assert.Equal("obj-1", declared.CoverTerrainId);
        Assert.False(declared.LongRange);
        Assert.Equal(1, declared.PenaltyDice);
        Assert.Equal(-1, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 44: Long Range and Cover both apply")]
    public void LongRangeAndCoverTogetherAreTwoPenaltyDice()
    {
        var play = Duel("26.4", "6"); // against the east side of obj-3 (x 22 to 26)
        play.Put(A(1), "14.5", "6");
        var result = play.Do(Fire(), 6, 6, 6, 1, 1, 1);
        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.LongRange && declared.InCover);
        Assert.Equal(2, declared.PenaltyDice);
        Assert.Equal(4, result.Events.OfType<RangedSuccessRolled>().Single().Check.Dice.Rolled.Count);
    }

    [Fact]
    [Trait("Rule", "Critical Success p. 44 (confirmed ruling): +1 INJURY DICE on the Injury Roll")]
    public void ACriticalSuccessAddsOneInjuryDieAndNoExtraSuccessDie()
    {
        var play = Duel();
        var result = play.Do(Fire(), 6, 6, 2, 1, 6);

        var roll = result.Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal(SuccessOutcome.CriticalSuccess, roll.Check.Outcome);
        Assert.Equal(2, roll.Check.Dice.Rolled.Count);
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(1, injury.NetDice);
        Assert.Equal(3, injury.Dice.Rolled.Count);
        Assert.Equal(8, injury.Total); // the highest two of 2, 1, 6
        Assert.Equal(InjuryOutcome.Down, injury.Outcome);
    }

    [Fact]
    [Trait("Rule", "Success p. 44: an ordinary Success hits with plain 2D6 injury")]
    public void AnOrdinarySuccessRollsOnlyTwoInjuryDice()
    {
        var play = Duel();
        var injury = play.Do(Fire(), 6, 5, 2, 1).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(0, injury.NetDice);
        Assert.Equal(3, injury.Total);
    }

    [Theory]
    [InlineData(1, 1, ModelStatus.Standing, 1)]      // 2: Minor Hit
    [InlineData(3, 3, ModelStatus.Standing, 1)]      // 6: Minor Hit
    [InlineData(3, 4, ModelStatus.Down, 1)]          // 7: Down
    [InlineData(4, 4, ModelStatus.Down, 1)]          // 8: Down
    [InlineData(4, 5, ModelStatus.OutOfAction, 0)]   // 9: Out of Action
    [Trait("Rule", "Injury Rolls pp. 46, 48")]
    public void TheInjuryChartDecidesTheTargetsFate(int d1, int d2, ModelStatus status, int markers)
    {
        var play = Duel();
        play.Do(Fire(), 4, 3, d1, d2);
        Assert.Equal(status, play.M(B(1)).Status);
        Assert.Equal(markers, play.M(B(1)).BloodMarkers);
        Assert.Equal(status == ModelStatus.OutOfAction, play.M(B(1)).Position is null);
    }

    [Fact]
    [Trait("Rule", "Injury Rolls p. 46: a Down result on a Down model places 2 Blood Markers")]
    public void HittingADownModelPlacesTwoMarkersOnADownResult()
    {
        var play = Duel();
        play.SetStatus(B(1), ModelStatus.Down);
        var result = play.Do(Fire(), 4, 3, 3, 4);
        Assert.True(result.Events.OfType<InjuryRolled>().Single().Injury.TargetAlreadyDown);
        Assert.Equal(2, play.M(B(1)).BloodMarkers);
        Assert.Equal(ModelStatus.Down, play.M(B(1)).Status);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 27: no more than 6")]
    public void MarkersNeverExceedSix()
    {
        var play = Duel();
        play.Mark(B(1), 6);
        // The attacker declines to spend any of the six.
        play.Do(Fire(), 4, 3);
        play.Do(new SpendMarkers(Seat.One, 0), 1, 1);
        Assert.Equal(6, play.M(B(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Injury modifiers pp. 46, 48: the weapon's and the target's Armour combine, negatives floor at -3")]
    public void WeaponAndArmourModifiersAreCombinedByTheInjuryRoll()
    {
        var profiles = new Func<ModelId, ModelProfile>(id => id.Value switch
        {
            "A1" => new ModelProfile(L("6"), L("0.5"), L("18"), rangedInjuryModifier: 1),
            "B1" => new ModelProfile(L("6"), L("0.5"), L("18"), armour: -3),
            _ => Rifleman,
        });
        var play = Duel(profiles: profiles);
        var injury = play.Do(Fire(), 4, 3, 4, 4).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(-2, injury.Modifier); // +1 and -3
        Assert.Equal(6, injury.Total);
    }

    // ---- Blood Markers ----

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: the opponent spends the shooter's markers as -1 DICE")]
    public void TheDefenderMaySpendTheAttackersMarkersBeforeTheSuccessRoll()
    {
        var play = Duel();
        play.Mark(A(1), 2);
        play.Do(Fire()); // paused: no die has been rolled
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), play.State.Pending);

        var result = play.Do(new SpendMarkers(Seat.Two, 1), 6, 5, 1);
        var roll = result.Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal(-1, roll.Check.NetDice);
        Assert.Equal(6, roll.Check.Dice.Total); // the lowest two of 6, 5, 1
        Assert.Equal(1, play.M(A(1)).BloodMarkers);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: declining leaves the markers and the roll unchanged")]
    public void DecliningToSpendLeavesTheRollUnmodified()
    {
        var play = Duel();
        play.Mark(A(1), 2);
        play.Do(Fire());
        var roll = play.Do(new SpendMarkers(Seat.Two, 0), 4, 3, 1, 1).Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal(0, roll.Check.NetDice);
        Assert.Equal(2, play.M(A(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: the attacker spends the target's markers as +1 INJURY DICE")]
    public void TheAttackerMaySpendTheTargetsMarkersBeforeTheInjuryRoll()
    {
        var play = Duel();
        play.Mark(B(1), 3);
        play.Do(Fire(), 4, 3); // the shot hits; the injury roll waits for the attacker's choice
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), play.State.Pending);
        play.Refused(new SpendMarkers(Seat.Two, 1), ReasonCode.NotYourTurn);
        play.Refused(new SpendMarkers(Seat.One, 4), ReasonCode.InvalidMarkerCount);
        play.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.DecisionPending);

        var injury = play.Do(new SpendMarkers(Seat.One, 2), 5, 1, 4, 2).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(2, injury.NetDice);
        Assert.Equal(9, injury.Total); // the highest two of 5, 1, 4, 2
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: spent markers are removed, and the wound then adds its own")]
    public void SpentMarkersLeaveTheTargetAndANewWoundAddsOne()
    {
        var play = Duel();
        play.Mark(B(1), 3);
        play.Do(Fire(), 4, 3);
        play.Do(new SpendMarkers(Seat.One, 2), 1, 1, 1, 1); // total 2: a Minor Hit
        Assert.Equal(2, play.M(B(1)).BloodMarkers); // 3 - 2 spent + 1 wound
    }

    [Fact]
    [Trait("Rule", "Critical Success p. 44 with Blood Markers p. 28: both add INJURY DICE")]
    public void CriticalSuccessAndSpentMarkersStack()
    {
        var play = Duel();
        play.Mark(B(1), 1);
        play.Do(Fire(), 6, 6);
        var injury = play.Do(new SpendMarkers(Seat.One, 1), 1, 1, 6, 5).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(2, injury.NetDice);
        Assert.Equal(4, injury.Dice.Rolled.Count);
        Assert.Equal(11, injury.Total);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: no decision when there is nothing to spend")]
    public void NoMarkerDecisionIsAskedWhenTheModelHasNone()
    {
        var play = Duel();
        var result = play.Do(Fire(), 4, 3, 1, 1);
        Assert.DoesNotContain(result.Events, e => e is DecisionRequested);
    }

    // ---- refusals ----

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 42: legal attacks only")]
    public void IllegalShotsAreRefusedWithoutRollingOrChangingAnything()
    {
        var wall = Duel();
        wall.Put(A(1), "17", "9.5").Put(B(1), "17", "13.5"); // wall-1 (y 11 to 12) between them
        wall.Refused(Fire(), ReasonCode.NoLineOfSight);

        var duel = Duel();
        duel.Refused(new Shoot(Seat.One, A(1), A(2)), ReasonCode.TargetNotEnemy);
        duel.Refused(new Shoot(Seat.One, A(1), new ModelId("Z9")), ReasonCode.UnknownModel);
        duel.Refused(new Shoot(Seat.One, A(2), B(1)), ReasonCode.ActivationInProgress);
        duel.Refused(new Shoot(Seat.Two, A(1), B(1)), ReasonCode.NotYourModel);

        var unarmed = Duel(profiles: id => id.Value == "A1" ? new ModelProfile(L("6"), L("0.5"), Length.Zero) : Rifleman);
        unarmed.Refused(Fire(), ReasonCode.NoRangedWeapon);

        var gone = Duel();
        gone.SetStatus(B(1), ModelStatus.OutOfAction);
        gone.Refused(Fire(), ReasonCode.TargetNotOnTable);
    }

    [Fact]
    [Trait("Rule", "Ranged Attacks p. 42: not within 1\" of an enemy")]
    public void AModelWithinAnInchOfAnEnemyCannotShoot()
    {
        var play = Duel("5.5", "2"); // gap 0.5"
        play.Refused(Fire(), ReasonCode.AttackerEngaged);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 43: not supported in Slice A")]
    public void ShootingAtAModelWithinAnInchOfTheShootersFriendIsRefusedForNow()
    {
        var play = Duel();
        play.Put(A(2), "10", "3.4"); // next to B1 at (10,2)
        play.Refused(Fire(), ReasonCode.ShootingIntoMeleeUnsupported);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: Shoot once per Activation")]
    public void AModelShootsOnlyOncePerActivation()
    {
        var play = Duel();
        play.Do(Fire(), 3, 3);
        play.Refused(Fire(), ReasonCode.ActionAlreadyTaken);
        // Dash, Shoot and Move are different actions and still available.
        play.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
    }

    [Fact]
    [Trait("Rule", "Success Rolls p. 50: a Shaken warband rolls Risky")]
    public void AShakenWarbandsFailedShotEndsTheActivation()
    {
        var play = Duel();
        play.Shaken(Seat.One);
        var result = play.Do(Fire(), 3, 3);
        Assert.True(result.Events.OfType<RangedSuccessRolled>().Single().Check.Risky);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Success Rolls p. 50: a Shaken warband's successful shot is unaffected")]
    public void AShakenWarbandsSuccessfulShotDoesNotEndTheActivation()
    {
        var play = Duel();
        play.Shaken(Seat.One);
        play.Do(Fire(), 4, 3, 1, 1);
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Winning p. 153: a player wins immediately when no enemy models are on the battlefield")]
    public void TakingTheLastEnemyOutOfActionEndsTheMatch()
    {
        var play = Duel(perSide: 1);
        var result = play.Do(Fire(), 4, 3, 6, 5);

        Assert.Equal(Phase.Ended, play.State.Phase);
        Assert.Equal(new MatchOutcome(Seat.One, OutcomeReason.AllEnemiesOutOfAction), play.State.Outcome);
        Assert.IsType<MatchEnded>(result.Events[^1]);
        play.Refused(new EndActivation(Seat.One), ReasonCode.MatchEnded);
    }

    [Fact]
    [Trait("Rule", "Architecture: shooting decisions delegate to the geometry")]
    public void TheDecisionAlwaysMatchesRangeAndSight()
    {
        var play = Duel();
        var field = play.Setup.Scenario!.Battlefield;
        var rng = new Random(23);
        for (var i = 0; i < 400; i++)
        {
            var from = new Point2(Length.FromThousandths(rng.Next(0, 36000)), Length.FromThousandths(rng.Next(0, 36000)));
            var to = new Point2(Length.FromThousandths(rng.Next(0, 36000)), Length.FromThousandths(rng.Next(0, 36000)));
            var a = new ModelBody("A1", from, L("0.5"));
            var b = new ModelBody("B1", to, L("0.5"));
            if (Measure.IsWithin(a, b, Length.Inch) || Measure.Overlap(a, b))
            {
                continue; // engaged: refused for another reason
            }
            var state = play.State with { Models = [.. play.State.Models.Select(m => m.Id == A(1) ? m with { Position = from } : m.Id == B(1) ? m with { Position = to } : m.Id == A(2) ? m with { Position = P("1", "35") } : m.Id == B(2) ? m with { Position = P("35", "35") } : m)] };
            var sight = Cover.Of(field, a, b);
            var inRange = Measure.IsWithin(a, b, L("18"));
            var result = TurnSequence.Decide(state, Fire(), new FixedDice(6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6));
            Assert.Equal(sight.Sight.Visibility != Visibility.None && inRange, result.IsAccepted);
            if (result.IsAccepted)
            {
                var declared = result.Events.OfType<RangedAttackDeclared>().Single();
                Assert.Equal(sight.InCover, declared.InCover);
                Assert.Equal(!Measure.IsWithin(a, b, L("9")), declared.LongRange);
            }
        }
    }
}
