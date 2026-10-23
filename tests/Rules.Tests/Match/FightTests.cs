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

// Fight ACTIONs and Melee Attacks (Comprehensive Rules pp. 34, 45-46, 49) and the Injury Roll they cause (pp. 46-48).
public class FightTests
{
    // A1 at (4,2) and B1 at (5.4,2): 0.4" apart, in the open.
    private static BoardPlay Brawl(ModelProfile? profile = null, int perSide = 2, Func<ModelId, ModelProfile>? profiles = null)
    {
        if (profiles is null)
        {
            return Skirmish("4", "2", "5.4", "2", profile, perSide);
        }
        var play = new BoardPlay(perSide, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.4", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static FightModel Fight(int target = 1) => new(Seat.One, A(1), B(target));

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: a Success Roll of 7+ hits, then an Injury Roll")]
    public void AHitIsASuccessRollThenAnInjuryRoll()
    {
        var play = Brawl();
        var result = play.Do(Fight(), 4, 3, 5, 3); // 7 hits; injury 8 is Down
        var declared = result.Events.OfType<MeleeAttackDeclared>().Single();
        Assert.False(declared.FreeFight);
        Assert.Null(declared.DefendedObstacleId);
        Assert.Equal(SuccessOutcome.Success, result.Events.OfType<MeleeSuccessRolled>().Single().Check.Outcome);
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((0, 8, InjuryOutcome.Down), (injury.NetDice, injury.Total, injury.Outcome));
        Assert.Equal(ModelStatus.Down, play.M(B(1)).Status);
        Assert.Equal(1, play.M(B(1)).BloodMarkers);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Fight));
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: a Failure misses and nothing further happens")]
    public void AMissRollsNoInjuryDice()
    {
        var play = Brawl();
        var result = play.Do(Fight(), 3, 3);
        Assert.Empty(result.Events.OfType<InjuryRolled>());
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
        Assert.Null(play.State.InProgress);
    }

    [Theory]
    [InlineData(1, 1, ModelStatus.Standing, 1)]    // 2: Minor Hit, the lowest result that marks the target
    [InlineData(3, 2, ModelStatus.Standing, 1)]    // 5: Minor Hit, one Blood Marker
    [InlineData(4, 3, ModelStatus.Down, 1)]        // 7: Down
    [InlineData(5, 4, ModelStatus.OutOfAction, 0)] // 9: Out of Action
    [Trait("Rule", "Injury Rolls pp. 46, 48: the table of results")]
    public void TheInjuryTableDecidesTheTargetsFate(int first, int second, ModelStatus status, int markers)
    {
        var play = Brawl();
        play.Do(Fight(), 4, 3, first, second);
        Assert.Equal(status, play.M(B(1)).Status);
        Assert.Equal(markers, play.M(B(1)).BloodMarkers);
        Assert.Equal(status == ModelStatus.OutOfAction, play.M(B(1)).Position is null);
    }

    [Fact]
    [Trait("Rule", "Injury Rolls p. 46: a result below 2 has no effect")]
    public void ArmourCanTurnAHitIntoNoEffect()
    {
        var play = Brawl(profiles: id => id.Value[0] == 'A' ? Brawler : new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -3, hasMeleeWeapon: true));
        play.Do(Fight(), 4, 3, 1, 1); // 2 - 3
        Assert.Equal(0, play.M(B(1)).BloodMarkers);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
    }

    [Fact]
    [Trait("Rule", "Critical Success (confirmed ruling): a Critical melee hit adds +1 INJURY DICE")]
    public void ACriticalSuccessAddsAnInjuryDie()
    {
        var play = Brawl();
        var result = play.Do(Fight(), 6, 6, 2, 5, 3); // critical; injury 3 dice, the highest two (5, 3) = 8
        Assert.Equal(SuccessOutcome.CriticalSuccess, result.Events.OfType<MeleeSuccessRolled>().Single().Check.Outcome);
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(1, injury.NetDice);
        Assert.Equal(8, injury.Total);
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: +1 INJURY DICE for a Melee Attack if the target is Down")]
    public void ADownTargetTakesAnExtraInjuryDieFromMelee()
    {
        var play = Brawl();
        play.SetStatus(B(1), ModelStatus.Down);
        var result = play.Do(Fight(), 4, 3, 1, 5, 4);
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((1, 9, InjuryOutcome.OutOfAction), (injury.NetDice, injury.Total, injury.Outcome));
        Assert.True(injury.TargetAlreadyDown);
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49 with Critical Success: both extra injury dice apply")]
    public void ACriticalHitOnADownTargetAddsTwoInjuryDice()
    {
        var play = Brawl();
        play.SetStatus(B(1), ModelStatus.Down);
        var result = play.Do(Fight(), 6, 6, 1, 1, 2, 3);
        Assert.Equal(2, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: the extra injury die is for Melee Attacks only")]
    public void ARangedHitOnADownTargetGetsNoExtraDie()
    {
        var play = Skirmish("4", "2", "12", "2", Gunman);
        play.SetStatus(B(1), ModelStatus.Down);
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3, 4, 3);
        Assert.Equal(0, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
    }

    [Fact]
    [Trait("Rule", "Injury Rolls p. 46-48: the Melee Weapon's modifier and the target's Armour combine, floored at -3")]
    public void TheMeleeWeaponAndArmourModifyTheInjuryRoll()
    {
        ModelProfile Pick(ModelId id) => id.Value[0] == 'A'
            ? new ModelProfile(L("6"), L("0.5"), Length.Zero, rangedInjuryModifier: 3, hasMeleeWeapon: true, meleeInjuryModifier: 1)
            : new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -2, hasMeleeWeapon: true);
        var play = Brawl(profiles: Pick);
        var injury = play.Do(Fight(), 4, 3, 4, 4).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(-1, injury.Modifier);   // +1 melee weapon (not the ranged +3) and -2 armour
        Assert.Equal(7, injury.Total);
        Assert.Equal(InjuryOutcome.Down, injury.Outcome);
    }

    [Fact]
    [Trait("Rule", "Melee Attack Modifiers p. 45: Defended Obstacle, -1 DICE if the target is in cover behind the piece")]
    public void ATargetCoveredByAPieceBetweenTheModelsCostsADie()
    {
        // B1 stands on the rubble obj-1 (10,4)-(14,8); A1 attacks from just outside it.
        var play = Skirmish("12", "3.1", "12", "4.4");
        var result = play.Do(Fight(), 6, 5, 1);
        var declared = result.Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Equal("obj-1", declared.DefendedObstacleId);
        Assert.Equal(1, declared.PenaltyDice);
        var roll = result.Events.OfType<MeleeSuccessRolled>().Single().Check;
        Assert.Equal(-1, roll.NetDice);
        Assert.Equal(6, roll.Dice.Total); // the lowest two of 6, 5, 1
        Assert.Equal(SuccessOutcome.Failure, roll.Outcome);
    }

    [Fact]
    [Trait("Rule", "Melee Attack Modifiers p. 45: no cover between the models, no penalty")]
    public void NoCoverMeansNoPenaltyDie()
    {
        var play = Brawl();
        var declared = play.Do(Fight(), 1, 1).Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Null(declared.DefendedObstacleId);
        Assert.Equal(0, declared.PenaltyDice);
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: the target must be within 1\"")]
    public void TheTargetMustBeWithinAnInchEdgeToEdge()
    {
        var edge = Skirmish("4", "2", "6", "2"); // gap exactly 1"
        edge.Do(Fight(), 1, 1);
        var out1 = Skirmish("4", "2", "6.001", "2");
        out1.Refused(Fight(), ReasonCode.TargetNotInMelee);
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: a model needs a Melee Weapon")]
    public void AModelWithoutAMeleeWeaponCannotFight()
    {
        var play = Brawl(profiles: _ => Rifleman);
        play.Refused(Fight(), ReasonCode.NoMeleeWeapon);
    }

    // Proposal M1: a Pistol-like ranged weapon, 12"/Melee, with a +1 injury modifier, and no Melee Weapon.
    private static readonly ModelProfile Pistoleer = new(L("6"), L("0.5"), L("12"), rangedInjuryModifier: 1) { RangedAlsoMelee = true };

    [Fact]
    [Trait("Rule", "Battlekit p. 68 and PISTOL p. 56: a weapon with \"Melee\" under its Range is also a Melee Weapon")]
    public void ARangedWeaponWithMeleeUnderItsRangeCanFight()
    {
        ModelProfile Pick(ModelId id) => id.Value[0] == 'A' ? Pistoleer : new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -1, hasMeleeWeapon: true);
        var play = Brawl(profiles: Pick);
        var result = play.Do(Fight(), 4, 3, 4, 4); // 7 hits; injury 8, +1 pistol, -1 armour
        Assert.Single(result.Events.OfType<MeleeAttackDeclared>());
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(0, injury.Modifier);   // the pistol's own +1 and the target's -1 Armour
        Assert.Equal(8, injury.Total);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Fight));
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45 and Actions p. 34: a pistol in melee keeps the 1\" and Shoot limits")]
    public void APistolInMeleeKeepsTheOtherLimits()
    {
        var far = Skirmish("4", "2", "6.001", "2", Pistoleer);
        far.Refused(Fight(), ReasonCode.TargetNotInMelee);

        var shot = Brawl(Pistoleer);
        shot.State = shot.State with { Actions = ActionsTaken.Shoot };
        shot.Refused(Fight(), ReasonCode.ActionExcluded);

        var down = Brawl(Pistoleer);
        down.SetStatus(A(1), ModelStatus.Down);
        Assert.NotNull(Board.AssessMelee(down.State, down.M(A(1)), down.M(B(1))).Refusal);
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: a Melee Weapon is used before a dual-purpose weapon (U54, one weapon)")]
    public void AModelWithBothFightsWithItsMeleeWeapon()
    {
        var both = new ModelProfile(L("6"), L("0.5"), L("12"), rangedInjuryModifier: 2, hasMeleeWeapon: true, meleeInjuryModifier: -1) { RangedAlsoMelee = true };
        Assert.Equal(-1, both.MeleeAttackInjuryModifier);
        Assert.Equal(1, Pistoleer.MeleeAttackInjuryModifier);
        Assert.True(Pistoleer.CanMakeMeleeAttack);
        Assert.Equal(0, Rifleman.MeleeAttackInjuryModifier);
        Assert.False(Rifleman.CanMakeMeleeAttack);
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: one enemy model within 1\" is chosen as the target")]
    public void OnlyAnEnemyModelOnTheTableCanBeFought()
    {
        var play = Brawl();
        play.Put(A(2), "4", "3.2");
        play.Refused(new FightModel(Seat.One, A(1), A(2)), ReasonCode.TargetNotEnemy);
        play.Refused(new FightModel(Seat.One, A(1), new ModelId("Z9")), ReasonCode.UnknownModel);
        play.Refused(new FightModel(Seat.One, A(2), B(1)), ReasonCode.ActivationInProgress);
        play.Refused(new FightModel(Seat.Two, A(1), B(1)), ReasonCode.NotYourModel);
        play.Put(B(2), "4", "0.8");
        play.SetStatus(B(2), ModelStatus.OutOfAction);
        play.Refused(Fight(2), ReasonCode.TargetNotOnTable);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: each type of ACTION once per Activation")]
    public void AModelFightsOnlyOncePerActivation()
    {
        var play = Brawl();
        play.Do(Fight(), 2, 2);
        play.Refused(Fight(), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: no Shoot ACTION with a Fight ACTION")]
    public void ShootAndFightExcludeEachOther()
    {
        var fought = Brawl(profiles: Everyone(Gunman));
        fought.Do(Fight(), 2, 2);
        fought.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.ActionExcluded);

        var shot = Brawl();
        shot.State = shot.State with { Actions = ActionsTaken.Shoot };
        shot.Refused(Fight(), ReasonCode.ActionExcluded);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: Dash, then Charge, then Fight in one Activation")]
    public void ChargeThenFightIsOneActivation()
    {
        var play = Skirmish("4", "2", "9", "2");
        play.Do(new ChargeModel(Seat.One, A(1), B(1)), 4);
        var result = play.Do(Fight(), 5, 3, 5, 4);
        Assert.Equal(InjuryOutcome.OutOfAction, result.Events.OfType<InjuryRolled>().Single().Injury.Outcome);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Charge | ActionsTaken.Fight));
    }

    [Fact]
    [Trait("Rule", "Charging p. 37: completing a charge does not by itself allow a Fight")]
    public void AFailedChargeLeavesNothingToFight()
    {
        var play = Skirmish("4", "2", "14", "2");
        play.Do(new ChargeModel(Seat.One, A(1), B(1)), 1);
        play.Refused(Fight(), ReasonCode.TargetNotInMelee);
    }

    [Fact]
    [Trait("Rule", "Blood Markers pp. 27-28: the opponent may spend the attacker's markers for -1 DICE, before the roll")]
    public void TheOpponentDecidesAboutMarkersBeforeTheMeleeRollIsMade()
    {
        var play = Brawl();
        play.Mark(A(1), 2).Mark(B(1), 1);

        var declared = play.Do(Fight()); // no dice yet
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), play.State.Pending);
        Assert.Equal(RollStage.FightRoll, play.State.InProgress!.Stage);
        Assert.DoesNotContain(declared.Events, e => e is MeleeSuccessRolled);

        var spent = play.Do(new SpendMarkers(Seat.Two, 1), 6, 6, 1); // -1 DICE: the lowest two of 6, 6, 1 = 7
        Assert.Equal(-1, spent.Events.OfType<MeleeSuccessRolled>().Single().Check.NetDice);
        Assert.Equal(1, play.M(A(1)).BloodMarkers);

        // The hit pauses again, before the Injury Roll, for the attacker to spend the target's marker.
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), play.State.Pending);
        Assert.Empty(spent.Events.OfType<InjuryRolled>());
        var injured = play.Do(new SpendMarkers(Seat.One, 1), 2, 6, 5); // +1 INJURY DICE: highest two = 11
        Assert.Equal(1, injured.Events.OfType<InjuryRolled>().Single().Injury.NetDice);
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
        Assert.Null(play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: declining to spend markers leaves the roll as it was")]
    public void DecliningMarkersRollsNormally()
    {
        var play = Brawl();
        play.Mark(A(1), 2).Mark(B(1), 3);
        play.Do(Fight());
        play.Do(new SpendMarkers(Seat.Two, 0), 4, 3);
        play.Do(new SpendMarkers(Seat.One, 0), 1, 2);
        Assert.Equal(2, play.M(A(1)).BloodMarkers);
        Assert.Equal(4, play.M(B(1)).BloodMarkers); // 3, plus a Minor Hit
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: a decision of the wrong kind, seat or size is refused without dice")]
    public void MarkerDecisionsAreCheckedDuringAFight()
    {
        var play = Brawl();
        play.Mark(A(1), 2);
        play.Do(Fight());
        play.Refused(new SpendMarkers(Seat.One, 1), ReasonCode.NotYourTurn);
        play.Refused(new SpendMarkers(Seat.Two, 3), ReasonCode.InvalidMarkerCount);
        play.Refused(new SpendMarkers(Seat.Two, -1), ReasonCode.InvalidMarkerCount);
        play.Refused(new EndActivation(Seat.One), ReasonCode.DecisionPending);
        play.Refused(Fight(), ReasonCode.DecisionPending);
    }

    [Fact]
    [Trait("Rule", "Success Rolls p. 50: a Shaken warband's rolls are Risky, and a failure ends the Activation")]
    public void AFailedRiskyFightEndsTheActivation()
    {
        var play = Brawl();
        play.Shaken(Seat.One);
        var result = play.Do(Fight(), 3, 3);
        Assert.True(result.Events.OfType<MeleeSuccessRolled>().Single().Check.RiskyFailure);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.Null(play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Success Rolls p. 50: a Risky roll that succeeds changes nothing")]
    public void ASuccessfulRiskyFightGoesOn()
    {
        var play = Brawl();
        play.Shaken(Seat.One);
        var result = play.Do(Fight(), 4, 3, 1, 1);
        Assert.True(result.Events.OfType<MeleeSuccessRolled>().Single().Check.Risky);
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Winning p. 51: a warband with no models left on the battlefield loses")]
    public void TakingTheLastEnemyModelOutOfActionEndsTheMatch()
    {
        var play = Skirmish("4", "2", "5.4", "2", perSide: 1);
        var result = play.Do(Fight(), 4, 3, 5, 5);
        var ended = result.Events.OfType<MatchEnded>().Single();
        Assert.Equal((Seat.One, OutcomeReason.AllEnemiesOutOfAction), (ended.Winner, ended.Reason));
        Assert.Equal(Phase.Ended, play.State.Phase);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: a Down model stands up first, with Movement halved, and can then fight")]
    public void ADownModelStandsUpAndCanFight()
    {
        var play = new BoardPlay(2, Everyone(Brawler)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.4", "2").SetStatus(A(1), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(ModelStatus.Standing, play.M(A(1)).Status);
        play.Do(Fight(), 1, 1);
    }

    [Fact]
    [Trait("Rule", "Melee Attacks p. 45: the Line of Sight requirement")]
    public void ATargetWithinAnInchButBehindAWallCannotBeFought()
    {
        var play = Skirmish("17", "10.55", "17", "12.45"); // either side of wall-1 (y 11-12): 0.9" apart
        play.Refused(Fight(), ReasonCode.NoLineOfSight);
    }
}
