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

// Roster stage R1 (U97): a model's own dice. Its Ranged and Melee Characteristics and its weapons' DICE are +DICE or
// -DICE on the attack's Success Roll, its weapons' INJURY DICE on the Injury Roll (pp. 15, 18); +DICE and -DICE cancel
// in pairs (p. 15).
public class ProfileDiceTests
{
    // An 18" rifle; the model shoots at +1 DICE (Ranged +1) and the rifle adds +1 DICE and +1 INJURY DICE.
    private static readonly ModelProfile Marksman = new(L("6"), L("0.5"), L("18"))
    {
        RangedDice = 1, RangedWeaponDice = 1, RangedWeaponInjuryDice = 1,
    };

    // Melee -1 DICE with a weapon of -1 DICE and +2 INJURY DICE: three penalty dice, two extra injury dice.
    private static readonly ModelProfile Clumsy = new(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true)
    {
        MeleeDice = -1, MeleeWeaponDice = -1, MeleeWeaponInjuryDice = 2,
    };

    private static BoardPlay Duel(Func<ModelId, ModelProfile> profiles)
    {
        var play = new BoardPlay(2, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "+Dice p. 15 and Injury Rolls p. 18: the Ranged Characteristic and the weapon's DICE and INJURY DICE")]
    public void AShotRollsTheModelsAndTheWeaponsDice()
    {
        var play = Duel(Everyone(Marksman));
        // Success: 4 dice (+2), the highest two of 1, 2, 4, 3 are 7. Injury: 3 dice (+1), the highest two of 1, 5, 4 are 9.
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 2, 4, 3, 1, 5, 4);
        Assert.Equal(0, result.Events.OfType<RangedAttackDeclared>().Single().PenaltyDice);
        var hit = result.Events.OfType<RangedSuccessRolled>().Single().Check;
        Assert.Equal((2, 7, SuccessOutcome.Success), (hit.NetDice, hit.Dice.Total, hit.Outcome));
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((1, 9, InjuryOutcome.OutOfAction), (injury.NetDice, injury.Total, injury.Outcome));
    }

    [Fact]
    [Trait("Rule", "+Dice and -Dice p. 15: pairs of +DICE and -DICE cancel")]
    public void LongRangeCancelsOneOfTheModelsDice()
    {
        var play = new BoardPlay(2, Everyone(Marksman)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "14.001", "2"); // Long Range, -1 DICE
        play.Do(new ActivateModel(Seat.One, A(1)));
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 2, 3);
        Assert.Equal(1, result.Events.OfType<RangedAttackDeclared>().Single().PenaltyDice);
        var hit = result.Events.OfType<RangedSuccessRolled>().Single().Check;
        Assert.Equal((1, 5, SuccessOutcome.Failure), (hit.NetDice, hit.Dice.Total, hit.Outcome));
    }

    [Fact]
    [Trait("Rule", "-Dice p. 15 and Injury Rolls p. 18: Melee -DICE keep the lowest two; INJURY DICE still add")]
    public void AFightRollsTheMeleeCharacteristicAndTheWeaponsDice()
    {
        var play = Skirmish("4", "2", "5.4", "2", Clumsy);
        // Success: 4 dice (-2), the lowest two of 6, 6, 4, 3 are 7. Injury: 4 dice (+2), the highest two of 1, 1, 4, 3 are 7.
        var result = play.Do(new FightModel(Seat.One, A(1), B(1)), 6, 6, 4, 3, 1, 1, 4, 3);
        var hit = result.Events.OfType<MeleeSuccessRolled>().Single().Check;
        Assert.Equal((-2, 7), (hit.NetDice, hit.Dice.Total));
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((2, 7, InjuryOutcome.Down), (injury.NetDice, injury.Total, injury.Outcome));
    }

    [Fact]
    [Trait("Rule", "Retreat p. 14: a free attack is a Melee Attack with the attacker's own dice")]
    public void AFreeAttackRollsTheAttackersDice()
    {
        var play = Skirmish("4", "2", "5.4", "2", Clumsy);
        play.Put(B(2), "30", "30");
        play.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        var result = play.Do(new FreeFight(Seat.Two, B(1)), 2, 2, 2, 2);
        var hit = result.Events.OfType<MeleeSuccessRolled>().Single().Check;
        Assert.Equal(-2, hit.NetDice);
    }

    [Fact]
    [Trait("Rule", "Shooting into Melee p. 42: the shot at a friend is an ordinary Success Roll, with the shooter's dice")]
    public void AForcedShotAtAFriendKeepsTheShootersDice()
    {
        var play = new BoardPlay(3, Everyone(new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { RangedDice = 2 })).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2").Put(A(2), "12", "3.2").Put(A(3), "2", "20");
        play.Put(B(2), "30", "30").Put(B(3), "30", "32");
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Do(new Shoot(Seat.One, A(1), B(1)), 2);
        var result = play.Do(new ChooseFriendlyTarget(Seat.One, A(2)), 1, 1, 1, 1);
        Assert.Equal(2, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
    }

    [Fact]
    [Trait("Rule", "Records: a forged roll without the model's dice is refused")]
    public void AnAttackRollWithoutTheModelsDiceIsRefused()
    {
        var play = Duel(Everyone(Marksman));
        var before = play.State;
        var events = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 2, 4, 3, 1, 5, 4).Events;
        var declared = Reducer.Apply(before, events.OfType<RangedAttackDeclared>().Single());
        Assert.Equal((2, 0), (declared.InProgress!.BonusDice, declared.InProgress.PenaltyDice));
        var forged = new RangedSuccessRolled(A(1), B(1), new SuccessRollResult(new RollRecord([4, 3], [4, 3]), 0, risky: false));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, forged));
    }

    [Fact]
    [Trait("Rule", "Records: a match whose models have their own dice plays to the end and replays to the same state")]
    public void SimulatedMatchesWithModelDiceReplay()
    {
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var diced = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with
                {
                    Profile = new ModelProfile(m.Profile.Movement, m.Profile.BaseRadius, m.Profile.RangedRange, m.Profile.RangedInjuryModifier,
                        m.Profile.Armour, m.Profile.HasMeleeWeapon, m.Profile.MeleeInjuryModifier)
                    {
                        RangedAlsoMelee = m.Profile.RangedAlsoMelee,
                        RangedDice = i % 3 - 1,
                        MeleeDice = 2 - i % 3,
                        MeleeWeaponInjuryDice = m.Profile.HasMeleeWeapon ? 1 : 0,
                        RangedWeaponDice = m.Profile.HasRangedWeapon ? 1 : 0,
                    },
                }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 5; seed++)
            {
                var run = Sim.Runner.Run(seed, diced);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
            }
        }
    }

    [Fact]
    [Trait("Rule", "Profiles: dice -6 to +6, weapon dice only with that weapon; a dual-purpose weapon's dice in melee")]
    public void AProfilesDiceAreCheckedAndAddUp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { RangedDice = 7 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { MeleeDice = -7 });
        Assert.Throws<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), Length.Zero) { RangedWeaponDice = 1 });
        Assert.Throws<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { MeleeWeaponInjuryDice = 1 });

        Assert.Equal((2, 1), (Marksman.RangedAttackDice, Marksman.RangedAttackInjuryDice));
        Assert.Equal((-2, 2), (Clumsy.MeleeAttackDice, Clumsy.MeleeAttackInjuryDice));
        var pistol = new ModelProfile(L("6"), L("0.5"), L("12")) { RangedAlsoMelee = true, MeleeDice = 1, RangedWeaponDice = 1, RangedWeaponInjuryDice = -1 };
        Assert.Equal((2, -1), (pistol.MeleeAttackDice, pistol.MeleeAttackInjuryDice));
        var both = new ModelProfile(L("6"), L("0.5"), L("12"), hasMeleeWeapon: true) { RangedAlsoMelee = true, RangedWeaponDice = 1, MeleeWeaponDice = -1 };
        Assert.Equal(-1, both.MeleeAttackDice); // the Melee Weapon is used (U54)
        Assert.Equal((0, 0), (Rifleman.RangedAttackDice, Rifleman.MeleeAttackDice));
        Assert.Equal((3, 1), AttackDice.With(1, 1, 2));
        Assert.Equal((1, 3), AttackDice.With(1, 1, -2));
    }
}
