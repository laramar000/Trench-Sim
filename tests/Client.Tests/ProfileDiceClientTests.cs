using Client.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Roster stage R1: the preview card and the unit panel say what a model's own dice add to its attacks.
public class ProfileDiceClientTests
{
    [Fact]
    public void ThePreviewNamesTheModelsOwnDice()
    {
        var dice = new DicePreview(1, ["Long Range, p. 43"], false, 0, 0, 0, 0)
        {
            OwnHitDice = 2, OwnHitParts = ["Ranged +1", "ranged weapon +1"], OwnInjuryDice = -1,
        };
        var lines = dice.Lines(new ModelId("a1"), new ModelId("b1"));
        Assert.Equal("Hit roll: +2 DICE (Ranged +1, ranged weapon +1)", lines[0]);
        Assert.Contains("Hit roll: -1 DICE (Long Range, p. 43)", lines);
        Assert.Contains(lines, l => l.StartsWith("Injury roll: -1 DICE (weapon)", StringComparison.Ordinal));
        Assert.Equal((1, -1), (dice.HitNetDice, dice.InjuryNetDice));
        Assert.DoesNotContain(new DicePreview(0, [], false, 0, 0, 0, 0).Lines(new("a1"), new("b1")), l => l.Contains("weapon", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePreviewNamesTheWeaponsKeywords()
    {
        var dice = new DicePreview(0, [], true, 0, 0, 0, 0)
        {
            ShakenRisky = false, CriticalDice = 2, ShotgunInjuryDice = -1,
            KeywordNotes = DicePreview.Notes(WeaponKeywords.Critical | WeaponKeywords.Risky | WeaponKeywords.Shotgun, AttackKind.Ranged),
        };
        var lines = dice.Lines(new ModelId("a1"), new ModelId("b1"));
        Assert.Contains("RISKY: the weapon is RISKY", lines);
        Assert.Contains("CRITICAL: +2 INJURY DICE on a critical hit", lines);
        Assert.Contains(lines, l => l.StartsWith("SHOTGUN:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("-1 DICE (SHOTGUN at Long Range)", StringComparison.Ordinal) && l.EndsWith("+2 DICE on a critical hit", StringComparison.Ordinal));
        Assert.Equal(-1, dice.InjuryNetDice);
        Assert.DoesNotContain(DicePreview.Notes(WeaponKeywords.Shotgun | WeaponKeywords.IgnoreCover, AttackKind.Melee), n => n.Length > 0);
        // Roster stage R2b: ASSAULT and HEAVY are about the ranged weapon; RELOAD for either.
        Assert.Equal(["RELOAD: the activation ends after this attack", "ASSAULT: may still Charge or Fight this activation", "HEAVY: no Move, Charge, Retreat or Dash this activation"],
            DicePreview.Notes(WeaponKeywords.Assault | WeaponKeywords.Heavy | WeaponKeywords.Reload, AttackKind.Ranged));
        Assert.Equal(["RELOAD: the activation ends after this attack"], DicePreview.Notes(WeaponKeywords.Assault | WeaponKeywords.Heavy | WeaponKeywords.Reload, AttackKind.Melee));
        var pistol = new ModelProfile(Length.FromInches(6), Length.Parse("0.5"), Length.FromInches(12))
        {
            RangedAlsoMelee = true, RangedWeaponKeywords = WeaponKeywords.Pistol, RangedDice = 2, MeleeDice = 1,
        };
        Assert.Equal(["Ranged (PISTOL) +2"], DicePreview.OwnParts(pistol, AttackKind.Melee));
    }

    [Fact]
    public void TheOwnPartsNameTheCharacteristicAndTheWeapon()
    {
        var pistol = new ModelProfile(Length.FromInches(6), Length.Parse("0.5"), Length.FromInches(12)) { RangedAlsoMelee = true, MeleeDice = 1, RangedWeaponDice = -1 };
        Assert.Equal(["Melee +1", "ranged weapon -1"], DicePreview.OwnParts(pistol, AttackKind.Melee));
        Assert.Equal(["ranged weapon -1"], DicePreview.OwnParts(pistol, AttackKind.Ranged));
        var plain = new ModelProfile(Length.FromInches(6), Length.Parse("0.5"), Length.FromInches(12), hasMeleeWeapon: true);
        Assert.Empty(DicePreview.OwnParts(plain, AttackKind.Melee));
        Assert.Equal("", UnitPanel.DiceWords(plain));
        var armed = new ModelProfile(Length.FromInches(6), Length.Parse("0.5"), Length.FromInches(12), hasMeleeWeapon: true) { RangedDice = -1, MeleeDice = 1, MeleeWeaponDice = 1 };
        Assert.Equal(" · Shoot -1 DICE · Fight +2 DICE", UnitPanel.DiceWords(armed));
        Assert.Equal(" · Fight +1 DICE", UnitPanel.DiceWords(new ModelProfile(Length.FromInches(6), Length.Parse("0.5"), Length.Zero, hasMeleeWeapon: true) { RangedDice = 2, MeleeDice = 1 }));
    }
}
