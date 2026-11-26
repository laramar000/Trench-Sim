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
    // actor); kind and weapon: the attack's (ResolutionLog.AttackAt).
    public static HitPower? Of(GameEvent e, MatchSetup? setup, ModelId? attacker, AttackKind? kind, int weapon)
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
        return OfWeapon(armed, attack, weapon);
    }

    public static HitPower OfWeapon(ModelProfile armed, AttackKind kind, int weapon = 0)
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
}
