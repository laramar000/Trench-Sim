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
