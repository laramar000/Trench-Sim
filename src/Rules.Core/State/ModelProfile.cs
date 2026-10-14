using Rules.Core.Geometry;

namespace Rules.Core.State;

// What a model can do, as the rules need it: how far it moves, how big its base is, and its one ranged weapon.
// Names, art and warband entries live in content outside Rules.Core; nothing here says who the model is.
// RangedRange zero means no ranged weapon; HasMeleeWeapon says whether the model has one Melee Weapon (pp. 45-46;
// a second Melee Weapon, Off-Hand attacks and CLEAVE are not modelled yet). Injury modifiers follow Injury Rolls (pp. 46, 48): the weapon's
// modifier is added by the attacker, Armour (zero or negative) belongs to the target.
public sealed record ModelProfile
{
    public const int MaxModifier = 3;

    public ModelProfile(Length movement, Length baseRadius, Length rangedRange, int rangedInjuryModifier = 0, int armour = 0, bool hasMeleeWeapon = false, int meleeInjuryModifier = 0)
    {
        if (movement <= Length.Zero || movement > Length.FromInches(24))
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "Movement must be above 0 and at most 24 inches.");
        }
        if (baseRadius <= Length.Zero || baseRadius > Length.FromInches(3))
        {
            throw new ArgumentOutOfRangeException(nameof(baseRadius), "A base radius must be above 0 and at most 3 inches.");
        }
        if (rangedRange < Length.Zero || rangedRange > Length.FromInches(60))
        {
            throw new ArgumentOutOfRangeException(nameof(rangedRange), "Range must be 0 (no ranged weapon) to 60 inches.");
        }
        if (rangedRange.Thousandths % 2 != 0)
        {
            throw new ArgumentException("Range must be an even number of thousandths so half the range is exact.", nameof(rangedRange));
        }
        if (rangedInjuryModifier is < -MaxModifier or > MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(rangedInjuryModifier));
        }
        if (armour is > 0 or < -MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(armour), "Armour is an injury modifier of 0 to -3.");
        }
        if (meleeInjuryModifier is < -MaxModifier or > MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(meleeInjuryModifier));
        }
        if (!hasMeleeWeapon && meleeInjuryModifier != 0)
        {
            throw new ArgumentException("A model without a Melee Weapon has no melee injury modifier.", nameof(meleeInjuryModifier));
        }
        Movement = movement;
        BaseRadius = baseRadius;
        RangedRange = rangedRange;
        RangedInjuryModifier = rangedInjuryModifier;
        Armour = armour;
        HasMeleeWeapon = hasMeleeWeapon;
        MeleeInjuryModifier = meleeInjuryModifier;
    }

    public Length Movement { get; }
    public Length BaseRadius { get; }
    public Length RangedRange { get; }
    public int RangedInjuryModifier { get; }
    public int Armour { get; }
    public bool HasMeleeWeapon { get; }
    public int MeleeInjuryModifier { get; }

    public bool HasRangedWeapon => RangedRange > Length.Zero;
}
