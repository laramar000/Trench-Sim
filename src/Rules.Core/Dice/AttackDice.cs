using Rules.Core.State;

namespace Rules.Core.Dice;

// The dice an attack's rolls start with, before any Blood Markers are spent, and the Injury Roll's modifier. The rules
// use these numbers when an attack is declared and rolled; the client's previews read the same numbers, so the dice a
// preview shows are the rules' own, not a copy.
public static class AttackDice
{
    // Ranged Attack Success Roll: 1 penalty die for Long Range and 1 for a target in Cover (pp. 42-44).
    public static int ShotPenalty(bool longRange, bool inCover) => (longRange ? 1 : 0) + (inCover ? 1 : 0);

    // Ranged Attack Success Roll: 1 bonus die for an Elevated position, 3" or more above the target (p. 43).
    public static int ShotBonus(bool elevated) => elevated ? 1 : 0;

    // Melee Attack Success Roll: 1 penalty die when the target defends an obstacle (pp. 45-46), and (roster stage R3)
    // for an Off-Hand weapon (p. 45) and for BLOCK after a charge (p. 53).
    public static int FightPenalty(bool defendedObstacle) => defendedObstacle ? 1 : 0;

    // Injury Roll: +1 INJURY DICE for a Critical Success (confirmed ruling) and for a Melee Attack on a Down target (p. 49).
    public static int InjuryBonus(bool critical, bool meleeOnDownTarget) => (critical ? 1 : 0) + (meleeOnDownTarget ? 1 : 0);

    // Roster stage R1: the attacker's own +DICE or -DICE (p. 15) on the attack's Success Roll: its Ranged or Melee
    // Characteristic and its weapon's DICE (ModelProfile.RangedAttackDice, MeleeAttackDice).
    public static int OwnDice(ModelProfile attacker, AttackKind kind) =>
        kind == AttackKind.Melee ? attacker.MeleeAttackDice : attacker.RangedAttackDice;

    // ... and on its Injury Roll: the weapon's INJURY DICE (p. 18).
    public static int OwnInjuryDice(ModelProfile attacker, AttackKind kind) =>
        kind == AttackKind.Melee ? attacker.MeleeAttackInjuryDice : attacker.RangedAttackInjuryDice;

    // Signed dice added to a roll's bonus and penalty dice: +DICE are bonus dice, -DICE penalty dice; the pairs
    // cancel when the roll is made (p. 15, RollRecord.NetDice).
    public static (int Bonus, int Penalty) With(int bonus, int penalty, int dice) =>
        dice >= 0 ? (bonus + dice, penalty) : (bonus, penalty - dice);

    // Injury Roll modifiers: the attacking weapon's modifier and the target's Armour (pp. 46, 48). Roster stage R2:
    // IGNORE ARMOUR drops the Armour; ARMOUR PIERCING lowers it by 1, to at most 0 (p. 52).
    // Roster stage R3: the part of the Armour from IMPERVIOUS battlekit is left alone by both (p. 55).
    public static int[] InjuryModifiers(ModelProfile attacker, ModelProfile target, AttackKind kind) =>
        [kind == AttackKind.Melee ? attacker.MeleeAttackInjuryModifier : attacker.RangedInjuryModifier, ArmourAgainst(attacker.AttackKeywords(kind), target.Armour, target.ImperviousArmour)];

    public static int ArmourAgainst(WeaponKeywords keywords, int armour, int impervious = 0)
    {
        var plain = armour - impervious;
        var left = keywords.HasFlag(WeaponKeywords.IgnoreArmour) ? 0
            : keywords.HasFlag(WeaponKeywords.ArmourPiercing) ? Math.Min(0, plain + 1)
            : plain;
        return left + impervious;
    }

    // Roster stage R2: the INJURY DICE a Critical Success adds, +2 with CRITICAL instead of +1 (p. 53).
    public static int CriticalInjuryDice(WeaponKeywords keywords) => keywords.HasFlag(WeaponKeywords.Critical) ? 2 : 1;

    // The injury dice before markers once an attack has hit: a Critical Success, a Melee Attack on a Down target, the
    // weapon's own INJURY DICE and dice carried from the declaration (SHOTGUN at Long Range).
    public static (int Bonus, int Penalty) InjuryDice(ModelProfile attacker, AttackKind kind, bool critical, bool meleeOnDownTarget, int carried)
    {
        var bonus = InjuryBonus(false, meleeOnDownTarget) + (critical ? CriticalInjuryDice(attacker.AttackKeywords(kind)) : 0);
        return With(bonus, 0, OwnInjuryDice(attacker, kind) + carried);
    }

    // Roster stage R2: a Ranged Attack's Success Roll dice from the declared facts and the weapon's keywords (IGNORE
    // COVER, IGNORE LONG RANGE, IGNORE ELEVATED POSITION; SHOTGUN at Long Range moves the -1 to the Injury Roll as
    // InjuryLater), with the attacker's own dice. Without keywords this is ShotBonus, ShotPenalty and OwnDice.
    public static (int Bonus, int Penalty, int InjuryLater) Shot(ModelProfile attacker, bool longRange, bool inCover, bool elevated)
    {
        var k = attacker.RangedWeaponKeywords;
        var counts = ShotModifiers(k, longRange, inCover, elevated);
        var (bonus, penalty) = With(counts.Elevated, counts.LongRange + counts.Cover, OwnDice(attacker, AttackKind.Ranged));
        return (bonus, penalty, counts.ShotgunInjury);
    }

    // Each Success Roll modifier of a shot as it applies with these keywords: +1 Elevated, 1 penalty die each for Long
    // Range and Cover, and SHOTGUN's -1 INJURY DICE at Long Range.
    public static (int Elevated, int LongRange, int Cover, int ShotgunInjury) ShotModifiers(WeaponKeywords k, bool longRange, bool inCover, bool elevated)
    {
        var far = longRange && !k.HasFlag(WeaponKeywords.IgnoreLongRange);
        var shotgun = far && k.HasFlag(WeaponKeywords.Shotgun);
        return (
            ShotBonus(elevated && !k.HasFlag(WeaponKeywords.IgnoreElevatedPosition)),
            far && !shotgun ? ShotPenalty(true, false) : 0,
            ShotPenalty(false, inCover && !k.HasFlag(WeaponKeywords.IgnoreCover)),
            shotgun ? -1 : 0);
    }

    // Roster stage R2: RISKY makes the attack's Success Roll Risky (p. 55), as a Shaken warband does (p. 50).
    public static bool Risky(ModelProfile attacker, AttackKind kind, bool shaken) =>
        shaken || attacker.AttackKeywords(kind).HasFlag(WeaponKeywords.Risky);
}
