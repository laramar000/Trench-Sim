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

// Roster stage R2 (U98): the weapon keywords that change one roll of an attack (1.0.2 pp. 52-57).
public class WeaponKeywordTests
{
    private static ModelProfile Gun(WeaponKeywords k, int armour = 0) => new(L("6"), L("0.5"), L("18"), armour: armour) { RangedWeaponKeywords = k };

    private static ModelProfile Blade(WeaponKeywords k, int armour = 0) => new(L("6"), L("0.5"), Length.Zero, armour: armour, hasMeleeWeapon: true) { MeleeWeaponKeywords = k };

    // A1 (4,2) shoots B1; the defenders get their own profile.
    private static BoardPlay Duel(ModelProfile shooter, ModelProfile target, string tx = "10", string ty = "2")
    {
        var play = new BoardPlay(2, id => id.Value[0] == 'A' ? shooter : target).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), tx, ty);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Shoot Fire() => new(Seat.One, A(1), B(1));

    [Fact]
    [Trait("Rule", "CRITICAL p. 53: +2 INJURY DICE instead of +1 for a Critical Success")]
    public void ACriticalWeaponAddsTwoInjuryDiceOnACriticalHit()
    {
        var play = Duel(Gun(WeaponKeywords.Critical), Rifleman);
        var result = play.Do(Fire(), 6, 6, 1, 1, 1, 1); // 12: Critical; injury 4 dice, the highest two 1+1
        Assert.Equal(2, result.Events.OfType<InjuryRolled>().Single().Injury.NetDice);

        var plain = Duel(Rifleman, Rifleman);
        Assert.Equal(1, plain.Do(Fire(), 6, 6, 1, 1, 1).Events.OfType<InjuryRolled>().Single().Injury.NetDice);

        var noCritical = Duel(Gun(WeaponKeywords.Critical), Rifleman);
        Assert.Equal(0, noCritical.Do(Fire(), 4, 3, 1, 1).Events.OfType<InjuryRolled>().Single().Injury.NetDice);
    }

    [Theory]
    [InlineData(WeaponKeywords.None, -2, -2)]
    [InlineData(WeaponKeywords.IgnoreArmour, -2, 0)]
    [InlineData(WeaponKeywords.ArmourPiercing, -2, -1)]
    [InlineData(WeaponKeywords.ArmourPiercing, 0, 0)]
    [InlineData(WeaponKeywords.IgnoreArmour | WeaponKeywords.ArmourPiercing, -3, 0)]
    [Trait("Rule", "IGNORE ARMOUR p. 55 and ARMOUR PIERCING p. 52: the target's Armour modifier")]
    public void ArmourKeywordsChangeTheTargetsArmour(WeaponKeywords k, int armour, int modifier)
    {
        var play = Duel(Gun(k), Gun(WeaponKeywords.None, armour));
        var injury = play.Do(Fire(), 4, 3, 1, 1).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(modifier, injury.Modifier);
    }

    [Fact]
    [Trait("Rule", "IGNORE ARMOUR p. 55: in melee too")]
    public void AMeleeWeaponCanIgnoreArmour()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        play.State = play.State with { Setup = Rebuilt(play.Setup, id => id.Value[0] == 'A' ? Blade(WeaponKeywords.IgnoreArmour) : Blade(WeaponKeywords.None, -2)) };
        var injury = play.Do(new FightModel(Seat.One, A(1), B(1)), 4, 3, 1, 1).Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal(0, injury.Modifier);
    }

    [Fact]
    [Trait("Rule", "SHOTGUN p. 56: at Long Range -1 INJURY DICE instead of -1 DICE")]
    public void AShotgunAtLongRangeLosesAnInjuryDieInstead()
    {
        var play = Duel(Gun(WeaponKeywords.Shotgun), Rifleman, "14.001"); // Long Range
        var result = play.Do(Fire(), 4, 3, 6, 6, 1); // hit 7 with 2 dice; injury 3 dice, the lowest two 6+1
        Assert.True(result.Events.OfType<RangedAttackDeclared>().Single().LongRange);
        Assert.Equal(1, result.Events.OfType<RangedAttackDeclared>().Single().PenaltyDice); // the declared facts are unchanged
        Assert.Equal(0, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        var injury = result.Events.OfType<InjuryRolled>().Single().Injury;
        Assert.Equal((-1, 7), (injury.NetDice, injury.Total));

        var close = Duel(Gun(WeaponKeywords.Shotgun), Rifleman);
        Assert.Equal(0, close.Do(Fire(), 4, 3, 1, 1).Events.OfType<InjuryRolled>().Single().Injury.NetDice);
    }

    [Fact]
    [Trait("Rule", "IGNORE [MODIFIER] p. 55: IGNORE LONG RANGE and IGNORE COVER drop that -1 DICE")]
    public void IgnoredModifiersAddNoPenaltyDie()
    {
        var far = Duel(Gun(WeaponKeywords.IgnoreLongRange), Rifleman, "14.001");
        Assert.Equal(0, far.Do(Fire(), 1, 1).Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);

        // B1 against the rubble obj-1, shot from the north-west (as RangedAttackTests.CoverAddsOnePenaltyDie...).
        var covered = Duel(Gun(WeaponKeywords.IgnoreCover), Rifleman, "12", "3.6");
        covered.Put(A(1), "4", "6");
        var result = covered.Do(Fire(), 1, 1);
        Assert.True(result.Events.OfType<RangedAttackDeclared>().Single().InCover);
        Assert.Equal(0, result.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);

        Assert.Equal((0, 0, 0, 0), AttackDice.ShotModifiers(WeaponKeywords.IgnoreElevatedPosition, false, false, elevated: true));
        Assert.Equal((1, 1, 1, 0), AttackDice.ShotModifiers(WeaponKeywords.None, true, true, true));
        Assert.Equal((1, 0, 1, -1), AttackDice.ShotModifiers(WeaponKeywords.Shotgun, true, true, true));
        Assert.Equal((1, 0, 1, 0), AttackDice.ShotModifiers(WeaponKeywords.Shotgun | WeaponKeywords.IgnoreLongRange, true, true, true));
    }

    [Fact]
    [Trait("Rule", "RISKY p. 55: the attack's Success Roll is Risky; a Failure ends the activation")]
    public void ARiskyWeaponsMissEndsTheActivation()
    {
        var play = Duel(Gun(WeaponKeywords.Risky), Rifleman);
        var result = play.Do(Fire(), 2, 2);
        Assert.True(result.Events.OfType<RangedSuccessRolled>().Single().Check.Risky);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });

        var fight = Skirmish("4", "2", "5.4", "2", Blade(WeaponKeywords.Risky));
        var melee = fight.Do(new FightModel(Seat.One, A(1), B(1)), 4, 3, 1, 1);
        Assert.True(melee.Events.OfType<MeleeSuccessRolled>().Single().Check.Risky);
    }

    [Fact]
    [Trait("Rule", "Records: a roll that leaves out the weapon's RISKY is refused")]
    public void ARollWithoutTheWeaponsRiskyIsRefused()
    {
        var play = Duel(Gun(WeaponKeywords.Risky), Rifleman);
        var before = play.State;
        var events = play.Do(Fire(), 4, 3, 1, 1).Events;
        var declared = Reducer.Apply(before, events.OfType<RangedAttackDeclared>().Single());
        var forged = new RangedSuccessRolled(A(1), B(1), new SuccessRollResult(new RollRecord([4, 3], [4, 3]), 0, risky: false));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, forged));
    }

    [Fact]
    [Trait("Rule", "PISTOL p. 56: in melee it may use the Ranged or the Melee Characteristic")]
    public void APistolInMeleeUsesTheBetterCharacteristic()
    {
        var pistol = new ModelProfile(L("6"), L("0.5"), L("12")) { RangedAlsoMelee = true, RangedWeaponKeywords = WeaponKeywords.Pistol, RangedDice = 2, MeleeDice = 1 };
        Assert.Equal((2, 2), (pistol.MeleeCharacteristicUsed, pistol.MeleeAttackDice));
        var play = Skirmish("4", "2", "5.4", "2", pistol);
        Assert.Equal(2, play.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1, 1, 1).Events.OfType<MeleeSuccessRolled>().Single().Check.NetDice);

        Assert.Equal(1, new ModelProfile(L("6"), L("0.5"), L("12")) { RangedAlsoMelee = true, RangedDice = 2, MeleeDice = 1 }.MeleeAttackDice);
        var withBlade = new ModelProfile(L("6"), L("0.5"), L("12"), hasMeleeWeapon: true) { RangedAlsoMelee = true, RangedWeaponKeywords = WeaponKeywords.Pistol, RangedDice = 2, MeleeDice = 1 };
        Assert.Equal(1, withBlade.MeleeAttackDice); // the Melee Weapon is used (U54)
    }

    [Fact]
    [Trait("Rule", "Profiles: keywords only with that weapon")]
    public void KeywordsNeedTheirWeapon()
    {
        Assert.Throws<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), Length.Zero) { RangedWeaponKeywords = WeaponKeywords.Critical });
        Assert.Throws<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { MeleeWeaponKeywords = WeaponKeywords.Critical });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = (WeaponKeywords)(1 << 20) });
        var pistol = new ModelProfile(L("6"), L("0.5"), L("12")) { RangedAlsoMelee = true, RangedWeaponKeywords = WeaponKeywords.Critical };
        Assert.Equal(WeaponKeywords.Critical, pistol.AttackKeywords(AttackKind.Melee));
        Assert.Equal(WeaponKeywords.None, Rifleman.AttackKeywords(AttackKind.Melee));
    }

    [Fact]
    [Trait("Rule", "Records: matches with weapon keywords play to the end and replay to the same state")]
    public void SimulatedMatchesWithKeywordsReplay()
    {
        var keywords = new[] { WeaponKeywords.Critical | WeaponKeywords.Risky, WeaponKeywords.IgnoreArmour | WeaponKeywords.Shotgun, WeaponKeywords.ArmourPiercing | WeaponKeywords.IgnoreCover };
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var armed = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with
                {
                    Profile = new ModelProfile(m.Profile.Movement, m.Profile.BaseRadius, m.Profile.RangedRange, m.Profile.RangedInjuryModifier,
                        m.Profile.Armour, m.Profile.HasMeleeWeapon, m.Profile.MeleeInjuryModifier)
                    {
                        RangedAlsoMelee = m.Profile.RangedAlsoMelee,
                        RangedWeaponKeywords = m.Profile.HasRangedWeapon ? keywords[i % 3] : WeaponKeywords.None,
                        MeleeWeaponKeywords = m.Profile.HasMeleeWeapon ? keywords[(i + 1) % 3] : WeaponKeywords.None,
                    },
                }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 5; seed++)
            {
                var run = Sim.Runner.Run(seed, armed);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
            }
        }
    }

    private static MatchSetup Rebuilt(MatchSetup setup, Func<ModelId, ModelProfile> profile) =>
        new(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m with { Profile = profile(m.Id) }), setup.Scenario);
}
