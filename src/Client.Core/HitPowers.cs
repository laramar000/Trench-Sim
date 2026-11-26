using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// Hit power (Jason, 2026-11-26: "a rocket launcher or an explosive would send them a lot farther than a pistol or a blunt
// weapon"): how hard a hit lands, for how far the battlefield knocks, reels and throws the model it hits. Read from the
// attack's weapon as the server's setup gives it (no weapon names are kept, so by what they do): a pistol or a plain
// hand weapon is Light; a rifle, or a hand weapon that adds injury, Firm; a shotgun, a HEAVY weapon or one that adds a lot
// of injury (an anti-tank rifle, a great hammer) Heavy; a grenade, a BLAST weapon, a mine or a Martyrdom Device
// Explosive. DEADLY raises it one step. Display only: it never changes a roll or an outcome.
public enum HitPower
{
    Light,
    Firm,
    Heavy,
    Explosive,
}

public static class HitPowers
{
    // How far a hit of each power knocks and throws, relative to a Firm one (a rifle).
    public static double Scale(HitPower power) => power switch
    {
        HitPower.Light => 0.65,
        HitPower.Heavy => 1.45,
        HitPower.Explosive => 1.9,
        _ => 1.0,
    };

    // The power of the hit this event is part of, or null outside an attack. attacker: the attack's model (the action's
    // actor); kind and weapon: the attack's (ResolutionLog.AttackAt). place: where the table last had a model, for a
    // shotgun's distance to its target (null: the distance is not known).
    public static HitPower? Of(GameEvent e, MatchSetup? setup, ModelId? attacker, AttackKind? kind, int weapon, Func<ModelId, TablePoint?>? place = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e)
        {
            case MineExploded or MartyrdomTriggered or BlastHit or PointBlastHit or AttackScattered:
                return HitPower.Explosive;
            case InjuryRolled i when kind is null && setup?.ProfileOf(i.Attacker) is { Martyrdom: true }:
                return HitPower.Explosive;
        }
        var who = e is InjuryRolled injury ? injury.Attacker : attacker;
        if (kind is not { } attack || who is not { } by || setup?.ProfileOf(by) is not { } profile)
        {
            return null;
        }
        var armed = weapon > 0 && profile.Has(attack, weapon) ? profile.Using(attack, weapon) : profile;
        // Shotgun falloff (2026-11-26): how far the target stood, base edge to base edge, as a share of the gun's range.
        double? reach = null;
        if (attack == AttackKind.Ranged && TargetOf(e) is { } target && place?.Invoke(by) is { } from && place(target) is { } to
            && TableRect.Inches(armed.RangedRange) is > 0 and var range)
        {
            var apart = Math.Sqrt((from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y))
                - TableRect.Inches(profile.BaseRadius) - (setup.ProfileOf(target) is { } t ? TableRect.Inches(t.BaseRadius) : 0);
            reach = Math.Max(0, apart) / range;
        }
        return OfWeapon(armed, attack, weapon, reach);
    }

    // The model an attack event names as its target (the Injury Roll's, or the second model the event names).
    private static ModelId? TargetOf(GameEvent e) => e is InjuryRolled i ? i.Target : EventModels.Of(e) is { Count: > 1 } models ? models[1] : null;

    // Shotgun falloff (Jason, 2026-11-26: "a max distance shotgun shot ... doesn't make them fly away; right next to them,
    // fly a bit farther"): a shotgun's spread loses its punch with distance (a rifle's or a pistol's barely changes at a
    // table's ranges; a blast already weakens away from its centre): point blank (within a quarter of its range) it hits as
    // hard as a blast, out to 60% Heavy, out to 85% Firm, beyond that, at the edge of its range, no harder than a pistol.
    public static HitPower ShotgunAt(double reach) => reach switch
    {
        <= 0.25 => HitPower.Explosive,
        <= 0.6 => HitPower.Heavy,
        <= 0.85 => HitPower.Firm,
        _ => HitPower.Light,
    };

    public static HitPower OfWeapon(ModelProfile armed, AttackKind kind, int weapon = 0, double? reach = null)
    {
        ArgumentNullException.ThrowIfNull(armed);
        var keywords = armed.AttackKeywords(kind);
        HitPower power;
        if (kind == AttackKind.Ranged)
        {
            if (armed.RangedWeaponBlast > 0 || weapon == ModelProfile.GrenadeWeapon)
            {
                return HitPower.Explosive;
            }
            var extra = armed.RangedWeaponInjuryDice + armed.RangedInjuryModifier;
            power = keywords.HasFlag(WeaponKeywords.Pistol) ? HitPower.Light
                : keywords.HasFlag(WeaponKeywords.Shotgun) && reach is { } r ? ShotgunAt(r)
                : keywords.HasFlag(WeaponKeywords.Heavy) || keywords.HasFlag(WeaponKeywords.Shotgun) || extra >= 2 ? HitPower.Heavy
                : HitPower.Firm;
        }
        else
        {
            var extra = armed.MeleeWeaponInjuryDice + armed.MeleeInjuryModifier;
            power = keywords.HasFlag(WeaponKeywords.Heavy) || extra >= 2 ? HitPower.Heavy
                : extra >= 1 ? HitPower.Firm
                : HitPower.Light;
        }
        return keywords.HasFlag(WeaponKeywords.Deadly) && power < HitPower.Explosive ? power + 1 : power;
    }

    // Flamethrowers (Jason, 2026-11-26: "flamethrowers are going to need a burning animation"): a ranged attack whose
    // weapon has FIRE and no BLAST (a fire bomb bursts like a grenade) is a jet of flame, not a shot.
    public static bool IsFlame(GameEvent e, MatchSetup? setup, ModelId? attacker, AttackKind? kind, int weapon)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (kind != AttackKind.Ranged || weapon == ModelProfile.GrenadeWeapon || attacker is not { } by || setup?.ProfileOf(by) is not { } profile)
        {
            return false;
        }
        var armed = weapon > 0 && profile.Has(AttackKind.Ranged, weapon) ? profile.Using(AttackKind.Ranged, weapon) : profile;
        return armed.AttackKeywords(AttackKind.Ranged).HasFlag(WeaponKeywords.Fire) && armed.RangedWeaponBlast == 0;
    }
}
