using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R4a (U101): SHRAPNEL (p. 57), GAS (p. 55) and FIRE (p. 54) put 1 extra BLOOD MARKER on the target after
// the Injury Roll, even on No Effect; NEGATE (p. 56) stops that; a model's grenade (pp. 68-69) is thrown with the Shoot
// ACTION as weapon 2.
public class GrenadeTests
{
    // Armour -2 so an Injury Roll of 1, 1 is No Effect.
    private static readonly ModelProfile Armoured = new(L("6"), L("0.5"), L("18"), armour: -2, hasMeleeWeapon: true);

    private static BoardPlay Duel(ModelProfile attacker, ModelProfile target, string bx = "12")
    {
        var play = new BoardPlay(1, id => id.Value.StartsWith('A') ? attacker : target).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), bx, "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "SHRAPNEL p. 57, GAS p. 55, FIRE p. 54: 1 extra BLOOD MARKER each, even on No Effect")]
    public void EachKeywordAddsABloodMarkerEvenOnNoEffect()
    {
        var gun = new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.Shrapnel | WeaponKeywords.Gas | WeaponKeywords.Fire };
        var play = Duel(gun, Armoured);
        var injury = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((InjuryOutcome.NoEffect, 3), (injury.Injury.Outcome, injury.ExtraMarkers));
        Assert.Equal(3, play.M(B(1)).BloodMarkers);

        // A Minor Hit's marker and the extra one together; a plain gun adds none.
        var one = Duel(new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.Fire }, new ModelProfile(L("6"), L("0.5"), L("18")));
        Assert.Equal(1, one.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 2).Events.OfType<InjuryRolled>().Single().ExtraMarkers);
        Assert.Equal(2, one.M(B(1)).BloodMarkers);
        var plain = Duel(new ModelProfile(L("6"), L("0.5"), L("18")), Armoured);
        Assert.Equal(0, plain.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 1).Events.OfType<InjuryRolled>().Single().ExtraMarkers);
        Assert.Equal(0, plain.M(B(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "FIRE p. 54 on a Melee Weapon: the extra BLOOD MARKER after a Melee Attack's Injury Roll")]
    public void AFireBladeMarksInMelee()
    {
        var blade = new ModelProfile(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true) { MeleeWeaponKeywords = WeaponKeywords.Fire };
        var play = Duel(blade, Armoured, bx: "5.1");
        var injury = play.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 1, 1).Events.OfType<InjuryRolled>().Single();
        Assert.Equal(1, injury.ExtraMarkers);
        Assert.Equal(1, play.M(B(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "NEGATE p. 56: the target is not affected by the keyword it NEGATEs")]
    public void NegateStopsTheExtraMarker()
    {
        var gun = new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.Shrapnel | WeaponKeywords.Fire };
        var play = Duel(gun, Armoured with { Negates = WeaponKeywords.Fire });
        Assert.Equal(1, play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 4, 1, 1).Events.OfType<InjuryRolled>().Single().ExtraMarkers);
        Assert.Equal(1, play.M(B(1)).BloodMarkers);
        Assert.Equal(0, AttackDice.ExtraMarkers(gun, AttackKind.Ranged, Armoured with { Negates = WeaponKeywords.Fire | WeaponKeywords.Shrapnel }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Armoured with { Negates = WeaponKeywords.Critical });
    }

    [Fact]
    [Trait("Rule", "U101: an Injury Roll event with the wrong number of extra BLOOD MARKERS is refused")]
    public void AForgedExtraMarkerIsRefused()
    {
        var gun = new ModelProfile(L("6"), L("0.5"), L("18")) { RangedWeaponKeywords = WeaponKeywords.Gas };
        var play = Duel(gun, Armoured);
        var events = TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice(4, 4, 1, 1)).Events;
        var state = play.State;
        foreach (var e in events.TakeWhile(e => e is not InjuryRolled))
        {
            state = Reducer.Apply(state, e);
        }
        var injury = events.OfType<InjuryRolled>().Single();
        Assert.Equal(1, injury.ExtraMarkers);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, injury with { ExtraMarkers = 0 }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, injury with { ExtraMarkers = 2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => injury with { ExtraMarkers = 4 });
    }

    [Fact]
    [Trait("Rule", "pp. 68-69: grenades are thrown with the Shoot ACTION, at their own range, by a model with no gun too")]
    public void AGrenadeIsThrownAsWeaponTwo()
    {
        var bomber = new ModelProfile(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true)
        {
            Grenade = new Weapon(L("8"), keywords: WeaponKeywords.Shrapnel),
        };
        Assert.True(bomber.Has(AttackKind.Ranged, ModelProfile.GrenadeWeapon));
        Assert.False(bomber.Has(AttackKind.Melee, ModelProfile.GrenadeWeapon));
        var far = Duel(bomber, Armoured, bx: "14"); // gap 9: beyond 8"
        far.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, ReasonCode.TargetOutOfRange);
        Assert.Equal(ReasonCode.NoRangedWeapon, far.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.NoRangedWeapon).Code);

        var play = Duel(bomber, Armoured, bx: "8"); // gap 3: not at Long Range
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, 4, 4, 1, 1);
        Assert.Equal(2, result.Events.OfType<RangedAttackDeclared>().Single().Weapon);
        Assert.Equal(1, result.Events.OfType<InjuryRolled>().Single().ExtraMarkers);
        Assert.Equal(2, play.State.ShotWeapon);
        play.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, ReasonCode.ActionAlreadyTaken);

        // No grenades: refused by name.
        var none = Duel(new ModelProfile(L("6"), L("0.5"), L("18")), Armoured);
        Assert.Equal("That model has no grenades.", none.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, ReasonCode.NoRangedWeapon).Text);
        Assert.Throws<ArgumentException>(() => new ModelProfile(L("6"), L("0.5"), L("18")) { Grenade = new Weapon(Length.Zero) });
    }

    [Fact]
    [Trait("Rule", "HEAVY p. 55 on a grenade: not after moving; ASSAULT p. 52: a charge may follow the throw")]
    public void AGrenadesKeywordsLimitTheActivation()
    {
        var heavy = new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { Grenade = new Weapon(L("8"), keywords: WeaponKeywords.Heavy) };
        var moved = Duel(heavy, Armoured, bx: "8");
        moved.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        Assert.Equal(ActionLimits.HeavyText, moved.Refused(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, ReasonCode.ActionExcluded).Text);
        moved.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1); // the rifle is not HEAVY
        Assert.True(heavy.NoChargeBonus);

        var assault = new ModelProfile(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true) { Grenade = new Weapon(L("8"), keywords: WeaponKeywords.Assault) };
        var play = Duel(assault, Armoured, bx: "8");
        play.Do(new Shoot(Seat.One, A(1), B(1)) { Weapon = 2 }, 1, 1);
        play.Do(new ChargeModel(Seat.One, A(1), B(1)), 3);
    }
}
