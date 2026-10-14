using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Charge ACTIONs (Comprehensive Rules pp. 34, 36-37): declaring, the Charge Bonus, the charge move.
public class ChargeTests
{
    private static ChargeModel Charge(int target = 1) => new(Seat.One, A(1), B(target));

    [Fact]
    [Trait("Rule", "Charging p. 36: roll a D6 and add it to Movement; success is within 1\" of the target")]
    public void AChargeThatHasTheReachStopsAtContactWithTheTarget()
    {
        var play = Skirmish("4", "2", "12", "2"); // gap 7"
        var result = play.Do(Charge(), 3);

        Assert.IsType<ChargeDeclared>(result.Events[0]);
        Assert.Equal(3, ((ChargeRolled)result.Events[1]).Die);
        var moved = result.Events.OfType<ChargeMoved>().Single();
        Assert.True(moved.Reached);
        Assert.Equal(play.M(A(1)).Position, moved.Path[^1]);
        Assert.True(Within(play, A(1), B(1), "1"));
        // Stops as soon as it is in contact (p. 37): the bases touch to within a thousandth, and never overlap.
        Assert.True(Gap(play, A(1), B(1)) <= L("0.002"));
        Assert.False(Measure.Overlap(BodyOf(play, A(1)), BodyOf(play, B(1))));
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Charge));
        Assert.Null(play.State.InProgress);
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: the model stops in contact even when it could go on")]
    public void ASurplusOfMovementIsNotUsedPastContact()
    {
        var play = Skirmish("4", "2", "9", "2"); // gap 4"
        play.Do(Charge(), 6);                    // 12" of movement
        Assert.True(Gap(play, A(1), B(1)) <= L("0.002"));
        Assert.True(play.M(A(1)).Position!.Value.X < play.M(B(1)).Position!.Value.X);
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: finishing within 1\" of the target completes the charge")]
    public void FinishingExactlyAnInchAwayIsASuccessfulCharge()
    {
        var play = Skirmish("4", "2", "13", "2"); // gap 8": 6 + 1 reaches x = 11, gap exactly 1"
        var moved = play.Do(Charge(), 1).Events.OfType<ChargeMoved>().Single();
        Assert.Equal(L("7"), moved.Cost);
        Assert.True(moved.Reached);
        Assert.Equal(L("1"), Gap(play, A(1), B(1)));
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: a charge that does not reach fails, and the model stays where the move ended")]
    public void AChargeThatFallsShortLeavesTheModelWhereItStopped()
    {
        var play = Skirmish("4", "2", "13.5", "2"); // gap 8.5"
        var moved = play.Do(Charge(), 1).Events.OfType<ChargeMoved>().Single();
        Assert.False(moved.Reached);
        Assert.Equal(P("11", "2"), play.M(A(1)).Position);
        Assert.Equal(L("1.5"), Gap(play, A(1), B(1)));
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Charge)); // the ACTION was taken
        Assert.Equal(A(1), play.State.ActiveModel);                  // and the activation goes on
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: Movement plus Charge Bonus is at most 12\"")]
    public void MovementPlusChargeBonusIsCappedAtTwelveInches()
    {
        var play = Skirmish("4", "2", "12", "2", new ModelProfile(L("9"), L("0.5"), Length.Zero, hasMeleeWeapon: true));
        Assert.Equal(L("12"), Board.ChargeReach(play.State, play.M(A(1)), 6));
        Assert.Equal(L("11"), Board.ChargeReach(play.State, play.M(A(1)), 2));
        Assert.Equal(L("12"), Board.ChargeReach(play.State, play.M(A(1)), 3));
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: Movement is halved after standing up, including any Charge Bonus")]
    public void ADownModelsChargeBonusIsHalvedWithItsMovement()
    {
        var play = new BoardPlay(2, Everyone(Brawler)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "13", "2").SetStatus(A(1), ModelStatus.Down);
        var started = play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Contains(started.Events, e => e is ModelStoodUp);
        Assert.Equal(L("5"), Board.ChargeReach(play.State, play.M(A(1)), 4)); // (6 + 4) / 2

        var moved = play.Do(Charge(), 4).Events.OfType<ChargeMoved>().Single();
        Assert.Equal(L("5"), moved.Cost);
        Assert.False(moved.Reached);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: enemy models that are down can be charged")]
    public void ADownEnemyCanBeCharged()
    {
        var play = Skirmish("4", "2", "9", "2");
        play.SetStatus(B(1), ModelStatus.Down);
        Assert.True(play.Do(Charge(), 4).Events.OfType<ChargeMoved>().Single().Reached);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: the target must be within 12\"")]
    public void ATargetBeyondTwelveInchesIsRefusedWithoutADie()
    {
        var play = Skirmish("4", "2", "17.001", "2"); // gap 12.001"
        play.Refused(Charge(), ReasonCode.TargetOutOfRange);
        var ok = Skirmish("4", "2", "17", "2");       // gap 12"
        ok.Do(Charge(), 6);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: the target must be visible to the charging model")]
    public void ATargetBehindAWallIsNotVisibleAndCannotBeCharged()
    {
        var play = Skirmish("17", "8", "17", "14"); // wall-1 spans (14,11)-(20,12)
        play.Refused(Charge(), ReasonCode.NoLineOfSight);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: a model within 1\" of an enemy cannot take a Charge ACTION")]
    public void AModelAlreadyEngagedCannotCharge()
    {
        var play = Skirmish("4", "2", "5.5", "2");
        play.Put(B(2), "9", "2");
        play.Refused(Charge(2), ReasonCode.AlreadyEngaged);
        play.Refused(Charge(1), ReasonCode.AlreadyEngaged);
    }

    [Fact]
    [Trait("Rule", "Interposing Enemy Models p. 36: the route may not pass within 1\" of a different enemy first")]
    public void AnEnemyOnTheRouteMustBeChosenAsTheTargetOrTheChargeIsNotMade()
    {
        var play = Skirmish("4", "2", "13", "2");
        play.Put(B(2), "8", "3"); // 1" off the line: within 1" of the route
        play.Refused(Charge(1), ReasonCode.ChargeRouteBlocked);
        // The interposing model itself is a fine target.
        Assert.True(play.Do(Charge(2), 6).Events.OfType<ChargeMoved>().Single().Reached);
    }

    [Fact]
    [Trait("Rule", "Interposing Enemy Models p. 36: an enemy that is 1\" or more off the route does not interpose")]
    public void AnEnemyFurtherThanAnInchFromTheRouteDoesNotInterpose()
    {
        var play = Skirmish("4", "2", "13", "2");
        play.Put(B(2), "8", "4.1"); // base edge 1.1" from the line
        Assert.True(play.Do(Charge(1), 6).Events.OfType<ChargeMoved>().Single().Reached);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: once within 1\" of the target other enemies no longer interpose")]
    public void FinishingNextToAnotherEnemyAsWellAsTheTargetIsAllowed()
    {
        var play = Skirmish("4", "2", "12", "2");
        play.Put(B(2), "12", "3.3"); // beside the target, 1.3" from it
        var moved = play.Do(Charge(1), 4).Events.OfType<ChargeMoved>().Single();
        Assert.True(moved.Reached);
        Assert.True(Within(play, A(1), B(2), "1"));
    }

    [Fact]
    [Trait("Rule", "Moves p. 35: a model may not finish on another model's base")]
    public void AChargeStopsShortOfAFriendlyBaseInItsWay()
    {
        var play = Skirmish("4", "2", "12", "2");
        play.Put(A(2), "10.6", "2");
        var moved = play.Do(Charge(), 6).Events.OfType<ChargeMoved>().Single();
        Assert.False(Measure.Overlap(BodyOf(play, A(1)), BodyOf(play, A(2))));
        Assert.False(moved.Reached);
        Assert.True(play.M(A(1)).Position!.Value.X <= L("9.6"));
        Assert.True(play.M(A(1)).Position!.Value.X > L("9.59"));
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: Move or Charge or Retreat, not more than one")]
    public void ChargeMoveAndRetreatExcludeEachOther()
    {
        var moved = Skirmish("4", "2", "12", "2");
        moved.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        moved.Refused(Charge(), ReasonCode.ActionAlreadyTaken);

        var charged = Skirmish("4", "2", "13.5", "2");
        charged.Do(Charge(), 1);
        charged.Refused(Charge(), ReasonCode.ActionAlreadyTaken);
        charged.Refused(new MoveModel(Seat.One, A(1), Path(("5", "2"))), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: a Dash can be taken in addition to a Charge, before or after it")]
    public void ADashAndAChargeCanShareAnActivation()
    {
        var play = Skirmish("4", "2", "16", "2");
        play.Do(new DashModel(Seat.One, A(1), Path(("6", "2"))), 6, 5); // Risky: succeeds
        Assert.Equal(P("6", "2"), play.M(A(1)).Position);
        Assert.True(play.Do(Charge(), 3).Events.OfType<ChargeMoved>().Single().Reached);
        // Dash is spent, and a model engaged by its charge can still dash only within its restrictions.
        play.Refused(new DashModel(Seat.One, A(1), Path(("5", "2"))), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34, Ranged Attacks p. 42: no Shoot ACTION with a Charge ACTION (no ASSAULT yet)")]
    public void ShootAndChargeExcludeEachOther()
    {
        var shotFirst = Skirmish("4", "2", "12", "2", Gunman);
        shotFirst.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        shotFirst.Refused(Charge(), ReasonCode.ActionExcluded);

        var chargedFirst = Skirmish("4", "2", "14.5", "2", Gunman);
        chargedFirst.Do(Charge(), 1); // short: gap 9.5, reach 7
        chargedFirst.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.ActionExcluded);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: bad declarations roll no die and change nothing")]
    public void BadChargeDeclarationsAreRefusedWithoutDice()
    {
        var play = Skirmish("4", "2", "12", "2");
        play.Refused(new ChargeModel(Seat.One, A(1), A(2)), ReasonCode.TargetNotEnemy);
        play.Refused(new ChargeModel(Seat.One, A(1), new ModelId("Z9")), ReasonCode.UnknownModel);
        play.Refused(new ChargeModel(Seat.One, A(2), B(1)), ReasonCode.ActivationInProgress);
        play.Refused(new ChargeModel(Seat.Two, A(1), B(1)), ReasonCode.NotYourModel);
        play.SetStatus(B(1), ModelStatus.OutOfAction);
        play.Refused(Charge(), ReasonCode.TargetNotOnTable);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36: a charge before an activation or in the wrong phase is refused")]
    public void ChargesNeedAnActivation()
    {
        var play = new BoardPlay(2, Everyone(Brawler)).ToActivation();
        play.Refused(Charge(), ReasonCode.NoActivationInProgress);
    }

    [Fact]
    [Trait("Rule", "Charging p. 36-37: the Charge Bonus is rolled only after a legal declaration")]
    public void TheBonusDieIsTheOnlyDieAChargeRolls()
    {
        var play = Skirmish("4", "2", "12", "2");
        var dice = new FixedDice(4);
        var result = TurnSequence.Decide(play.State, Charge(), dice);
        Assert.True(result.IsAccepted);
        Assert.Equal(0, dice.Remaining);
        Assert.Equal(new[] { typeof(ChargeDeclared), typeof(ChargeRolled), typeof(ChargeMoved) }, result.Events.Select(e => e.GetType()).ToArray());
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: Difficult terrain costs double, on a charge too")]
    public void RoughGroundShortensWhatTheChargeBonusBuys()
    {
        // rough-1 spans (7,14)-(11,22). A1 charges north-south through 4" of it.
        var play = Skirmish("9", "12", "9", "23.2");
        var moved = play.Do(Charge(), 3).Events.OfType<ChargeMoved>().Single(); // reach 9": 2" clear + 4" double = 10"
        Assert.False(moved.Reached);
        Assert.True(moved.Cost <= L("9"));
        Assert.True(play.M(A(1)).Position!.Value.Y < P("9", "20").Y);
    }
}
