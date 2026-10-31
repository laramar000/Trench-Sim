namespace Rules.Core.State;

// Roster stage R2: the weapon keywords the rules engine plays (1.0.2 pp. 52-57, Keywords). Each changes one roll or
// one limit of an attack made with the weapon that has it (interpretation U98). Other keywords are not played yet.
[Flags]
public enum WeaponKeywords
{
    None = 0,

    // CRITICAL: +2 INJURY DICE instead of +1 for a Critical Success.
    Critical = 1,

    // IGNORE ARMOUR: the target's Armour (its characteristic, armour and shields) does not modify the Injury Roll.
    IgnoreArmour = 2,

    // ARMOUR PIERCING: the target's total Armour modifier is lowered by 1, to at most 0.
    ArmourPiercing = 4,

    // SHOTGUN: at Long Range, -1 INJURY DICE instead of -1 DICE on the Success Roll.
    Shotgun = 8,

    // RISKY: the attack's Success Roll is a Risky Success Roll.
    Risky = 16,

    // PISTOL: in melee the model may use its Ranged or its Melee Characteristic (the better one is used).
    Pistol = 32,

    // IGNORE COVER, IGNORE LONG RANGE, IGNORE ELEVATED POSITION: that Success Roll modifier does not apply.
    IgnoreCover = 64,
    IgnoreLongRange = 128,
    IgnoreElevatedPosition = 256,

    // Roster stage R2b, keywords that change what else the model may do (ActionLimits):
    // ASSAULT: a Ranged Attack with it does not prevent a Charge or Fight ACTION in the same activation (pp. 34, 52).
    Assault = 512,

    // HEAVY: no Charge Bonus; a HEAVY ranged weapon cannot shoot in an activation with a Move, Charge, Retreat or Dash
    // ACTION (p. 55).
    Heavy = 1024,

    // RELOAD: after an attack with it, the model's activation ends once that ACTION is done (p. 56).
    Reload = 2048,
}
