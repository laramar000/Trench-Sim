using Rules.Core.State;

namespace Client.Core;

// Battlefield UI 3, stage U4: one compact line beside the action bar while an action is being chosen, so the numbers
// that matter for it are in view without the full unit card. Every value is the rules' own: the profile in the match
// setup and the allowance the previews use (halved after standing up). Nothing new is worked out here.
public static class ActionStats
{
    // Roster stage R3: weapon 1 is the model's second weapon for a Shoot or a Fight.
    public static string? For(MatchState state, Seat seat, ModelId actor, InteractionMode mode, int weapon = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Setup?.ProfileOf(actor) is not { } owned)
        {
            return null;
        }
        var kind = mode is InteractionMode.Shoot or InteractionMode.Blast ? AttackKind.Ranged : AttackKind.Melee;
        var profile = weapon > 0 && owned.Has(kind, weapon) ? owned.Using(kind, weapon) : owned;
        var second = ReferenceEquals(profile, owned) ? "" : weapon == ModelProfile.GrenadeWeapon ? " (grenades)" : " (2nd weapon)";
        var halved = state.MovementHalved ? " (halved after standing up)" : "";
        return mode switch
        {
            InteractionMode.Move or InteractionMode.Dash or InteractionMode.Retreat => Previews.Reach(state, seat, actor) is { } reach
                ? $"{Interaction.Words(mode)}: up to {BoardScene.Inch(reach)}{halved}"
                : null,
            InteractionMode.Shoot => profile.HasRangedWeapon || weapon == ModelProfile.GrenadeWeapon && owned.Has(kind, weapon) ? $"Shoot{second}: weapon range {BoardScene.Inch(TableRect.Inches(profile.RangedRange))}" : "Shoot: no Ranged Weapon",
            InteractionMode.Blast => $"Blast{second}: weapon range {BoardScene.Inch(TableRect.Inches(profile.RangedRange))}, BLAST {BoardScene.Inch(profile.RangedWeaponBlast)}",
            InteractionMode.Charge => $"Charge: reach Movement {BoardScene.Inch(TableRect.Inches(profile.Movement))} plus a D6, at most {TableRect.Inches(Rules.Core.Geometry.Charging.MaxMovement).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\"{halved}",
            InteractionMode.Fight => profile.HasMeleeWeapon ? $"Fight{second}: Melee Weapon, against an engaged enemy"
                : profile.RangedAlsoMelee ? "Fight: its ranged weapon (also Melee), against an engaged enemy"
                : "Fight: no Melee Weapon",
            _ => null,
        };
    }
}
