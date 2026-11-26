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

// Roster stage R3 (U100): a second ranged or Melee Weapon (pp. 45, 69), CLEAVE (p. 53), BLOCK (p. 53) and IMPERVIOUS
// (p. 55).
public class WeaponChoiceTests
{
    // Movement 6", 1" base, an 18" gun and a Melee Weapon; the second gun is a 6" one with +1 DICE.
    private static ModelProfile TwoGuns => new(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true)
    {
        SecondRangedWeapon = new Weapon(L("6"), dice: 1),
    };

    // A Melee Weapon and a second one with +1 INJURY DICE; cleave is the first weapon's CLEAVE.
    private static ModelProfile TwoBlades(int cleave = 0, int secondCleave = 0) => new(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true)
    {
        MeleeWeaponCleave = cleave,
        SecondMeleeWeapon = new Weapon(Length.Zero, injuryDice: 1, cleave: secondCleave),
    };

    private static ModelProfile Cleaver(int cleave) => new(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true) { MeleeWeaponCleave = cleave };

    private static FightModel Fight(int weapon = 0) => new(Seat.One, A(1), B(1)) { Weapon = weapon };

    [Fact]
    [Trait("Rule", "p. 69: a model may carry two ranged weapons; the Shoot ACTION names the one it uses")]
    public void AShotIsMadeWithTheWeaponChosen()
    {
        var play = Skirmish("4", "2", "12", "2", TwoGuns); // gap 7: within the 18" gun, beyond the 6" one
        play.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 1 }, ReasonCode.TargetOutOfRange);
        var first = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        Assert.Equal(0, first.Events.OfType<RangedAttackDeclared>().Single().Weapon);

        var near = Skirmish("4", "2", "8", "2", TwoGuns); // gap 3: the second gun, with its +1 DICE (three dice)
        var second = near.Do(new Shoot(Seat.One, A(1), B(1)) { Weapon = 1 }, 1, 1, 1);
        Assert.Equal(1, second.Events.OfType<RangedAttackDeclared>().Single().Weapon);
        Assert.Equal(1, near.State.ShotWeapon);
        near.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.ActionAlreadyTaken);

        var single = Skirmish("4", "2", "8", "2", Gunman);
        single.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 1 }, ReasonCode.NoRangedWeapon);
        single.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = -1 }, ReasonCode.NoRangedWeapon);
        near.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, ReasonCode.ActionAlreadyTaken);
        Assert.Equal(ReasonCode.NoRangedWeapon, TurnSequence.Check(Skirmish("4", "2", "8", "2", TwoGuns).State, new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 })!.Code);
    }

    [Fact]
    [Trait("Rule", "ASSAULT p. 52 and HEAVY p. 55 are the keywords of the gun that shoots")]
    public void TheShotWeaponsKeywordsLimitTheActivation()
    {
        var profile = new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true)
        {
            SecondRangedWeapon = new Weapon(L("18"), keywords: WeaponKeywords.Assault),
        };
        var play = Skirmish("4", "2", "12", "2", profile);
        play.Do(new Shoot(Seat.One, A(1), B(1)) { Weapon = 1 }, 1, 1);
        play.Do(new ChargeModel(Seat.One, A(1), B(1)), 3); // allowed after the ASSAULT gun's shot

        var plain = Skirmish("4", "2", "12", "2", profile);
        plain.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        Assert.Equal(ActionLimits.ShootAndMeleeText, plain.Refused(new ChargeModel(Seat.One, A(1), B(1)), ReasonCode.ActionExcluded).Text);

        var heavy = new ModelProfile(L("6"), L("0.5"), L("18")) { SecondRangedWeapon = new Weapon(L("18"), keywords: WeaponKeywords.Heavy) };
        var moved = Skirmish("4", "2", "12", "2", heavy);
        moved.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        moved.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);          // the first gun is not HEAVY
        var movedAgain = Skirmish("4", "2", "12", "2", heavy);
        movedAgain.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        Assert.Equal(ActionLimits.HeavyText, movedAgain.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 1 }, ReasonCode.ActionExcluded).Text);
        Assert.True(heavy.NoChargeBonus);
    }

    [Fact]
    [Trait("Rule", "p. 45: with two Melee Weapons a Fight attacks with both, in the order chosen, -1 DICE for the second")]
    public void AFightWithTwoWeaponsMakesAnOffHandAttack()
    {
        var play = Skirmish("4", "2", "5.1", "2", TwoBlades());
        var first = play.Do(Fight(), 1, 1).Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Equal((0, false), (first.Weapon, first.OffHand));
        Assert.Equal([new MeleeAttackSlot(1, true)], play.State.AttacksLeft!.Next.ToArray());
        Assert.Equal(new MeleeAttackSlot(1, true), TurnSequence.NextFightAttack(play.State, A(1)));
        play.Refused(Fight(), ReasonCode.WrongWeapon);
        var second = play.Do(Fight(1), 1, 1, 1).Events.OfType<MeleeAttackDeclared>().Single(); // 1 penalty die
        Assert.Equal((1, true, 1), (second.Weapon, second.OffHand, second.PenaltyDice));
        Assert.Null(play.State.AttacksLeft);
        play.Refused(Fight(), ReasonCode.ActionAlreadyTaken);
        play.Refused(Fight(1), ReasonCode.ActionAlreadyTaken);

        // The other order: the second weapon first, then the first one as the Off-Hand weapon.
        var other = Skirmish("4", "2", "5.1", "2", TwoBlades());
        other.Do(Fight(1), 1, 1);
        Assert.Equal([new MeleeAttackSlot(0, true)], other.State.AttacksLeft!.Next.ToArray());
        Assert.True(other.Do(Fight(0), 1, 1, 1).Events.OfType<MeleeAttackDeclared>().Single().OffHand);
    }

    [Fact]
    [Trait("Rule", "p. 53: CLEAVE (X) makes X Melee Attacks with the weapon in a Fight, before the Off-Hand weapon's")]
    public void CleaveMakesMoreAttacks()
    {
        var play = Skirmish("4", "2", "5.1", "2", TwoBlades(cleave: 2));
        play.Do(Fight(), 1, 1);
        Assert.Equal([new MeleeAttackSlot(0, false), new MeleeAttackSlot(1, true)], play.State.AttacksLeft!.Next.ToArray());
        play.Refused(Fight(1), ReasonCode.WrongWeapon);
        Assert.Equal(0, play.Do(Fight(), 1, 1).Events.OfType<MeleeAttackDeclared>().Single().PenaltyDice);
        play.Do(Fight(1), 1, 1, 1);
        Assert.Null(play.State.AttacksLeft);

        // The attacks still to come are the active model's to make or leave: ending the activation leaves them.
        var left = Skirmish("4", "2", "5.1", "2", Cleaver(3));
        left.Do(Fight(), 1, 1);
        Assert.Equal(2, left.State.AttacksLeft!.Next.Length);
        left.Do(new EndActivation(Seat.One));
        Assert.Null(left.State.AttacksLeft);
        Assert.Equal(1, Cleaver(3).Weapons(AttackKind.Melee));
        Assert.Equal(3, Cleaver(3).MeleeAttacksWith(0));
        Assert.Equal(1, Brawler.MeleeAttacksWith(0));
    }

    [Fact]
    [Trait("Rule", "p. 37 with p. 53: a free attack uses one weapon, but its CLEAVE attacks follow at once")]
    public void AFreeAttackCleaves()
    {
        var play = Skirmish("4", "2", "5.4", "2", TwoBlades(cleave: 2, secondCleave: 3));
        play.Put(B(2), "30", "30");
        play.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        var result = play.Do(new FreeFight(Seat.Two, B(1)), 1, 1, 1, 1);
        var attacks = result.Events.OfType<MeleeAttackDeclared>().ToList();
        Assert.Equal(2, attacks.Count);
        Assert.All(attacks, a => Assert.True(a.FreeFight && a.Weapon == 0 && !a.OffHand));
        Assert.Null(play.State.AttacksLeft);

        var second = Skirmish("4", "2", "5.4", "2", TwoBlades(cleave: 2, secondCleave: 3));
        second.Put(B(2), "30", "30");
        second.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        var three = second.Do(new FreeFight(Seat.Two, B(1)) { Weapon = 1 }, 1, 1, 1, 1, 1, 1).Events.OfType<MeleeAttackDeclared>().ToList();
        Assert.Equal(3, three.Count);
        Assert.All(three, a => Assert.Equal(1, a.Weapon));

        var plain = Skirmish("4", "2", "5.4", "2", Brawler);
        plain.Put(B(2), "30", "30");
        plain.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        Assert.Equal(ReasonCode.NoMeleeWeapon, TurnSequence.Check(plain.State, new FreeFight(Seat.Two, B(1)) { Weapon = 1 })!.Code);
    }

    [Fact]
    [Trait("Rule", "p. 53: BLOCK gives -1 DICE to the Melee Attacks of a model that charged this turn")]
    public void BlockHindersAChargersAttacks()
    {
        var blocker = new ModelProfile(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true) { MeleeWeaponKeywords = WeaponKeywords.Block };
        var charged = Skirmish("4", "2", "10", "2", blocker);
        charged.Do(new ChargeModel(Seat.One, A(1), B(1)), 3);
        Assert.Contains(A(1), charged.State.ChargedThisTurn);
        var hit = charged.Do(Fight(), 1, 1, 1).Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Equal((true, 1), (hit.Block, hit.PenaltyDice));

        var engaged = Skirmish("4", "2", "5.1", "2", blocker);
        Assert.False(engaged.Do(Fight(), 1, 1).Events.OfType<MeleeAttackDeclared>().Single().Block);

        var unit = Skirmish("4", "2", "10", "2", Brawler with { Block = true });
        unit.Do(new ChargeModel(Seat.One, A(1), B(1)), 3);
        Assert.True(unit.Do(Fight(), 1, 1, 1).Events.OfType<MeleeAttackDeclared>().Single().Block);

        // Nothing is tracked in a match with no BLOCK, so older records are unchanged.
        var none = Skirmish("4", "2", "10", "2", Brawler);
        none.Do(new ChargeModel(Seat.One, A(1), B(1)), 3);
        Assert.True(none.State.ChargedThisTurn.IsDefaultOrEmpty);
    }

    [Fact]
    [Trait("Rule", "p. 55: IGNORE ARMOUR and ARMOUR PIERCING leave the Armour from IMPERVIOUS battlekit alone")]
    public void ImperviousArmourStays()
    {
        Assert.Equal(-1, AttackDice.ArmourAgainst(WeaponKeywords.IgnoreArmour, -2, -1));
        Assert.Equal(-1, AttackDice.ArmourAgainst(WeaponKeywords.ArmourPiercing, -2, -1));
        Assert.Equal(-2, AttackDice.ArmourAgainst(WeaponKeywords.ArmourPiercing, -3, -1));
        Assert.Equal(-2, AttackDice.ArmourAgainst(WeaponKeywords.None, -2, -1));
        Assert.Equal(-2, AttackDice.ArmourAgainst(WeaponKeywords.IgnoreArmour, -2, -2));
        Assert.Equal(0, AttackDice.ArmourAgainst(WeaponKeywords.IgnoreArmour, -2));
        var target = new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -2, hasMeleeWeapon: true) { ImperviousArmour = -1 };
        var attacker = new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.IgnoreArmour };
        Assert.Equal(-1, AttackDice.InjuryModifiers(attacker, target, AttackKind.Ranged)[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), Length.Zero, armour: -1) { ImperviousArmour = -2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelProfile(L("6"), L("0.5"), Length.Zero) { ImperviousArmour = 1 });
    }

    [Fact]
    [Trait("Rule", "Profiles: a second weapon needs a first one of its kind; CLEAVE is a Melee Weapon's")]
    public void SecondWeaponsAreChecked()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true) { SecondRangedWeapon = new Weapon(L("6")) });
        Assert.ThrowsAny<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { SecondMeleeWeapon = new Weapon(Length.Zero) });
        Assert.ThrowsAny<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { SecondMeleeWeapon = new Weapon(L("6")) });
        Assert.ThrowsAny<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { SecondRangedWeapon = new Weapon(Length.Zero) });
        Assert.ThrowsAny<ArgumentException>(() => new Weapon(L("6"), cleave: 2));
        Assert.ThrowsAny<ArgumentException>(() => new Weapon(Length.Zero, cleave: ModelProfile.MaxCleave + 1));
        Assert.ThrowsAny<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { MeleeWeaponCleave = 2 });
        // A dual-purpose Pistol lets a model with no Melee Weapon carry a second one.
        var pistol = new ModelProfile(L("6"), L("0.5"), L("12")) { RangedAlsoMelee = true, SecondMeleeWeapon = new Weapon(Length.Zero) };
        Assert.Equal(2, pistol.Weapons(AttackKind.Melee));
        Assert.True(pistol.Using(AttackKind.Melee, 1).HasMeleeWeapon);
        Assert.Throws<ArgumentOutOfRangeException>(() => Gunman.Using(AttackKind.Ranged, 1));
    }

    [Fact]
    [Trait("Rule", "Records: a forged weapon, Off-Hand or BLOCK in a Melee Attack is refused")]
    public void ForgedMeleeFactsAreRefused()
    {
        var play = Skirmish("4", "2", "5.1", "2", Brawler);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = 1 }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new MeleeAttackDeclared(A(1), B(1), false, null) { OffHand = true }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new MeleeAttackDeclared(A(1), B(1), false, null) { Block = true }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = 2 });

        var two = Skirmish("4", "2", "5.1", "2", TwoBlades());
        two.Do(Fight(), 1, 1);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(two.State, new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = 1 }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(two.State, new MeleeAttackDeclared(A(1), B(1), false, null) { Weapon = 0, OffHand = true }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Skirmish("4", "2", "8", "2", Gunman).State, new RangedAttackDeclared(A(1), B(1), false, false, null, ModelLevel.Ground, ModelLevel.Ground, 0) { Weapon = 1 }));
    }

    [Fact]
    [Trait("Rule", "Records: the attacks to come, the weapon shot and the charges are in the state hash only when set")]
    public void TheNewStateIsHashed()
    {
        var play = Skirmish("4", "2", "5.1", "2", TwoBlades());
        var before = play.State;
        Assert.DoesNotContain("attacksLeft", StateHash.Canonical(before), StringComparison.Ordinal);
        Assert.DoesNotContain("shotWeapon", StateHash.Canonical(before), StringComparison.Ordinal);
        Assert.DoesNotContain("chargedThisTurn", StateHash.Canonical(before), StringComparison.Ordinal);
        play.Do(Fight(), 1, 1);
        Assert.Contains("attacksLeft", StateHash.Canonical(play.State), StringComparison.Ordinal);
        Assert.NotEqual(StateHash.Compute(play.State), StateHash.Compute(play.State with { AttacksLeft = null }));
        Assert.NotEqual(StateHash.Compute(before), StateHash.Compute(before with { ShotWeapon = 1 }));
        Assert.NotEqual(StateHash.Compute(before), StateHash.Compute(before with { ChargedThisTurn = [A(1)] }));
    }

    [Fact]
    [Trait("Rule", "Records: matches with second weapons, CLEAVE, BLOCK and IMPERVIOUS play to the end and replay")]
    public void SimulatedMatchesWithSecondWeaponsReplay()
    {
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
                        MeleeWeaponCleave = m.Profile.HasMeleeWeapon && i >= 2 ? 2 : 0,
                        SecondRangedWeapon = m.Profile.HasRangedWeapon ? new Weapon(L("36"), injuryModifier: 1) : null,
                        SecondMeleeWeapon = m.Profile.CanMakeMeleeAttack && i % 3 != 2 ? new Weapon(Length.Zero, keywords: i % 3 == 0 ? WeaponKeywords.Block : WeaponKeywords.None) : null,
                        ImperviousArmour = m.Profile.Armour < 0 ? -1 : 0,
                    },
                }).ToList(),
            }).ToList();
            var (offHand, secondShots, cleaves) = (0, 0, 0);
            for (var seed = 1; seed <= 10; seed++)
            {
                var run = Sim.Runner.Run(seed, armed);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                offHand += run.Log.OfType<MeleeAttackDeclared>().Count(e => e.OffHand);
                secondShots += run.Log.OfType<RangedAttackDeclared>().Count(e => e.Weapon == 1);
                cleaves += FollowOnAttacks(run.Log);
            }
            Assert.True(path == Sim.Content.MeleePath() ? offHand > 0 && cleaves > 0 : secondShots > 0, $"{path}: off-hand {offHand}, second shots {secondShots}, follow-on {cleaves}");
        }
    }

    // The CLEAVE attacks of Fights: a second Melee Attack with the same weapon by the active model in one activation.
    private static int FollowOnAttacks(IEnumerable<GameEvent> log)
    {
        var count = 0;
        var made = new HashSet<(ModelId, int)>();
        foreach (var e in log)
        {
            if (e is ActivationEnded or TurnEnded)
            {
                made.Clear();
            }
            else if (e is MeleeAttackDeclared { FreeFight: false } m && !made.Add((m.Attacker, m.Weapon)))
            {
                count++;
            }
        }
        return count;
    }
}
