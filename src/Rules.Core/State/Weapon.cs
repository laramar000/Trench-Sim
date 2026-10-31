using Rules.Core.Geometry;

namespace Rules.Core.State;

// Roster stage R3 (U100): a model's second ranged weapon or second Melee Weapon, with the same numbers the profile
// holds for its first one. Range is zero for a Melee Weapon; AlsoMelee marks a dual-purpose ranged weapon ("12"/Melee").
// Cleave is CLEAVE (X): a Fight with this weapon makes X Melee Attacks (p. 53); 0 means the keyword is absent.
public sealed record Weapon
{
    public Weapon(Length range, int injuryModifier = 0, int dice = 0, int injuryDice = 0, WeaponKeywords keywords = WeaponKeywords.None, bool alsoMelee = false, int cleave = 0)
    {
        if (range < Length.Zero || range > Length.FromInches(60) || range.Thousandths % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "Range must be 0 (a Melee Weapon) to 60 inches, in an even number of thousandths.");
        }
        if (injuryModifier is < -ModelProfile.MaxModifier or > ModelProfile.MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(injuryModifier));
        }
        if (dice is < -ModelProfile.MaxDice or > ModelProfile.MaxDice || injuryDice is < -ModelProfile.MaxDice or > ModelProfile.MaxDice)
        {
            throw new ArgumentOutOfRangeException(nameof(dice), $"Dice are -{ModelProfile.MaxDice} to +{ModelProfile.MaxDice}.");
        }
        if (alsoMelee && range <= Length.Zero)
        {
            throw new ArgumentException("Only a ranged weapon can also be a Melee Weapon.", nameof(alsoMelee));
        }
        if (cleave is < 0 or > ModelProfile.MaxCleave)
        {
            throw new ArgumentOutOfRangeException(nameof(cleave), $"CLEAVE is 0 (none) to {ModelProfile.MaxCleave}.");
        }
        if (cleave > 0 && range > Length.Zero)
        {
            throw new ArgumentException("Only a Melee Weapon has CLEAVE.", nameof(cleave));
        }
        Range = range;
        InjuryModifier = injuryModifier;
        Dice = dice;
        InjuryDice = injuryDice;
        Keywords = ModelProfile.Known(keywords, nameof(keywords));
        AlsoMelee = alsoMelee;
        Cleave = cleave;
    }

    public Length Range { get; }
    public int InjuryModifier { get; }
    public int Dice { get; }
    public int InjuryDice { get; }
    public WeaponKeywords Keywords { get; }
    public bool AlsoMelee { get; }
    public int Cleave { get; }

    public bool IsRanged => Range > Length.Zero;
}
