namespace Rules.Core.State;

// October 2026 Review (U148): the pieces of Equipment an Uncaring Gluttony can make unusable, as the engine knows them: the effects a
// model's kit gives that are a flag or a count of its profile (the dice a piece adds to a roll are merged into the model's numbers and
// are not named here).
public enum EquipmentPiece
{
    None = 0,
    PontiffsCrucifix = 1,    // KeepsGoingAfterRisky
    MountaineerKit = 2,      // ClimbDice
    MusicalInstrument = 3,   // Fanfare
    CloakOfTheMountain = 4,  // BlendsIn
    UrnOfAshes = 5,          // SinisterWhispers
    SacrificialLamb = 6,     // WardedByBlood
    Shovel = 7,              // DigsIn
    CrownOfHellfire = 8,     // BurnsAtActivationEnd
    DamnedPact = 9,          // BurnsWhenTakenOut
    MediKit = 10,            // Treats
    HolyWater = 11,          // HealingWaters
    PistonLegs = 12,         // GrindsToDust
    Talisman = 13,           // one of Talismans
    MartyrdomDevice = 14,    // Martyrdom
    FieldShrine = 15,        // FieldShrine
    SinVessel = 16,          // SinVessel
}

public static class Equipment
{
    public static readonly EquipmentPiece[] All = [.. Enum.GetValues<EquipmentPiece>().Where(p => p != EquipmentPiece.None)];

    public static bool Has(ModelProfile profile, EquipmentPiece piece) => piece switch
    {
        EquipmentPiece.PontiffsCrucifix => profile.KeepsGoingAfterRisky,
        EquipmentPiece.MountaineerKit => profile.ClimbDice > 0,
        EquipmentPiece.MusicalInstrument => profile.Fanfare,
        EquipmentPiece.CloakOfTheMountain => profile.BlendsIn,
        EquipmentPiece.UrnOfAshes => profile.SinisterWhispers,
        EquipmentPiece.SacrificialLamb => profile.WardedByBlood,
        EquipmentPiece.Shovel => profile.DigsIn,
        EquipmentPiece.CrownOfHellfire => profile.BurnsAtActivationEnd,
        EquipmentPiece.DamnedPact => profile.BurnsWhenTakenOut,
        EquipmentPiece.MediKit => profile.Treats,
        EquipmentPiece.HolyWater => profile.HealingWaters,
        EquipmentPiece.PistonLegs => profile.GrindsToDust,
        EquipmentPiece.Talisman => profile.Talismans > 0,
        EquipmentPiece.MartyrdomDevice => profile.Martyrdom,
        EquipmentPiece.FieldShrine => profile.FieldShrine,
        EquipmentPiece.SinVessel => profile.SinVessel,
        _ => false,
    };

    // The profile with that piece's effect gone (a Talisman: all of them).
    public static ModelProfile Without(ModelProfile profile, EquipmentPiece piece) => piece switch
    {
        EquipmentPiece.PontiffsCrucifix => profile with { KeepsGoingAfterRisky = false },
        EquipmentPiece.MountaineerKit => profile with { ClimbDice = 0 },
        EquipmentPiece.MusicalInstrument => profile with { Fanfare = false },
        EquipmentPiece.CloakOfTheMountain => profile with { BlendsIn = false },
        EquipmentPiece.UrnOfAshes => profile with { SinisterWhispers = false },
        EquipmentPiece.SacrificialLamb => profile with { WardedByBlood = false },
        EquipmentPiece.Shovel => profile with { DigsIn = false },
        EquipmentPiece.CrownOfHellfire => profile with { BurnsAtActivationEnd = false },
        EquipmentPiece.DamnedPact => profile with { BurnsWhenTakenOut = false },
        EquipmentPiece.MediKit => profile with { Treats = false },
        EquipmentPiece.HolyWater => profile with { HealingWaters = false },
        EquipmentPiece.PistonLegs => profile with { GrindsToDust = false },
        EquipmentPiece.Talisman => profile with { Talismans = 0 },
        EquipmentPiece.MartyrdomDevice => profile with { Martyrdom = false },
        EquipmentPiece.FieldShrine => profile with { FieldShrine = false },
        EquipmentPiece.SinVessel => profile with { SinVessel = false },
        _ => profile,
    };
}
