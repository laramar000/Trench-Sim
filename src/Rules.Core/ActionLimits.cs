using Rules.Core.State;

namespace Rules.Core;

// Roster stage R2b (U99): which ACTIONs exclude each other in one activation, with the ranged weapon's ASSAULT and
// HEAVY. One place for the rules, Decide's checks, the reducer and the client's previews.
// - pp. 34, 52: a Shoot ACTION and a Charge or Fight ACTION exclude each other, unless the weapon has ASSAULT.
// - p. 55: a HEAVY ranged weapon cannot make a Ranged Attack in an activation with a Move, Charge, Retreat or Dash.
public static class ActionLimits
{
    public const string ShootAndMeleeText = "A model cannot take a Shoot ACTION and a Charge or Fight ACTION in the same activation, unless its weapon has ASSAULT.";
    public const string HeavyText = "A HEAVY ranged weapon cannot shoot in an activation with a Move, Charge, Retreat or Dash ACTION.";
    public const string Ref = "1.0.2 pp. 34, 42, 52, 55";

    public const ActionsTaken HeavyMoves = ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat | ActionsTaken.Dash;

    private static bool Assault(ModelProfile? p) => p is not null && p.RangedWeaponKeywords.HasFlag(WeaponKeywords.Assault);

    private static bool HeavyGun(ModelProfile? p) => p is not null && p.RangedWeaponKeywords.HasFlag(WeaponKeywords.Heavy);

    // Why a Shoot ACTION is excluded by what the model has done, or null.
    public static string? ShootExcluded(ActionsTaken done, ModelProfile? p) =>
        !Assault(p) && (done & (ActionsTaken.Charge | ActionsTaken.Fight)) != 0 ? ShootAndMeleeText
        : HeavyGun(p) && (done & HeavyMoves) != 0 ? HeavyText
        : null;

    // Why a Charge or Fight ACTION is excluded by a Shoot ACTION already taken, or null.
    public static string? MeleeExcluded(ActionsTaken done, ModelProfile? p, ActionsTaken action) =>
        !done.HasFlag(ActionsTaken.Shoot) ? null
        : !Assault(p) ? ShootAndMeleeText
        : action == ActionsTaken.Charge && HeavyGun(p) ? HeavyText
        : null;

    // Why a Move, Dash or Retreat is excluded by a Shoot ACTION with a HEAVY ranged weapon, or null.
    public static string? MoveExcluded(ActionsTaken done, ModelProfile? p) =>
        done.HasFlag(ActionsTaken.Shoot) && HeavyGun(p) ? HeavyText : null;

    // RELOAD on the weapon of an attack the active model makes in its own activation (p. 56).
    public static bool Reloads(ModelProfile p, AttackKind kind) => p.AttackKeywords(kind).HasFlag(WeaponKeywords.Reload);
}
