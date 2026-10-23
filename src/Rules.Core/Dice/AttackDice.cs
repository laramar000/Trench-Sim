using Rules.Core.State;

namespace Rules.Core.Dice;

// The dice an attack's rolls start with, before any Blood Markers are spent, and the Injury Roll's modifier. The rules
// use these numbers when an attack is declared and rolled; the client's previews read the same numbers, so the dice a
// preview shows are the rules' own, not a copy.
public static class AttackDice
{
    // Ranged Attack Success Roll: 1 penalty die for Long Range and 1 for a target in Cover (pp. 42-44).
    public static int ShotPenalty(bool longRange, bool inCover) => (longRange ? 1 : 0) + (inCover ? 1 : 0);

    // Melee Attack Success Roll: 1 penalty die when the target defends an obstacle (pp. 45-46).
    public static int FightPenalty(bool defendedObstacle) => defendedObstacle ? 1 : 0;

    // Injury Roll: +1 INJURY DICE for a Critical Success (confirmed ruling) and for a Melee Attack on a Down target (p. 49).
    public static int InjuryBonus(bool critical, bool meleeOnDownTarget) => (critical ? 1 : 0) + (meleeOnDownTarget ? 1 : 0);

    // Injury Roll modifiers: the attacking weapon's modifier and the target's Armour (pp. 46, 48).
    public static int[] InjuryModifiers(ModelProfile attacker, ModelProfile target, AttackKind kind) =>
        [kind == AttackKind.Melee ? attacker.MeleeAttackInjuryModifier : attacker.RangedInjuryModifier, target.Armour];
}
