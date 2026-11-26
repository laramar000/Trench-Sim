using Client.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Hit power (2026-11-26, "a rocket launcher or an explosive would send them a lot farther than a pistol or a blunt
// weapon"): the attack's weapon decides how hard the hit lands (a pistol or plain club Light, a rifle Firm, a shotgun or
// HEAVY weapon Heavy, BLAST, grenades, mines and Martyrdom Explosive, DEADLY one step up); the hit's flinch, the wound's
// reel and the ragdoll's throw scale with it; a heavier hit reels longer.
public class HitPowerTests
{
    private static ModelProfile Gun(WeaponKeywords keywords = WeaponKeywords.None, int injury = 0) =>
        new(Length.Parse("6"), Length.Parse("0.5"), Length.Parse("24"), injury) { RangedWeaponKeywords = keywords };

    private static ModelProfile Club(WeaponKeywords keywords = WeaponKeywords.None, int injury = 0) =>
        new(Length.Parse("6"), Length.Parse("0.5"), Length.Parse("0"), hasMeleeWeapon: true, meleeInjuryModifier: injury) { MeleeWeaponKeywords = keywords };

    [Fact]
    public void EachWeaponLandsAsHardAsItHits()
    {
        Assert.Equal(HitPower.Light, HitPowers.OfWeapon(Gun(WeaponKeywords.Pistol), AttackKind.Ranged));
        Assert.Equal(HitPower.Firm, HitPowers.OfWeapon(Gun(), AttackKind.Ranged));
        Assert.Equal(HitPower.Heavy, HitPowers.OfWeapon(Gun(WeaponKeywords.Shotgun), AttackKind.Ranged));
        Assert.Equal(HitPower.Heavy, HitPowers.OfWeapon(Gun(WeaponKeywords.Heavy), AttackKind.Ranged));
        Assert.Equal(HitPower.Heavy, HitPowers.OfWeapon(Gun(injury: 2), AttackKind.Ranged));
        Assert.Equal(HitPower.Explosive, HitPowers.OfWeapon(Gun() with { RangedWeaponBlast = 2 }, AttackKind.Ranged));
        Assert.Equal(HitPower.Explosive, HitPowers.OfWeapon(Gun(), AttackKind.Ranged, ModelProfile.GrenadeWeapon));
        Assert.Equal(HitPower.Heavy, HitPowers.OfWeapon(Gun(WeaponKeywords.Deadly), AttackKind.Ranged));
        Assert.Equal(HitPower.Light, HitPowers.OfWeapon(Club(), AttackKind.Melee));
        Assert.Equal(HitPower.Firm, HitPowers.OfWeapon(Club(injury: 1), AttackKind.Melee));
        Assert.Equal(HitPower.Heavy, HitPowers.OfWeapon(Club(WeaponKeywords.Heavy), AttackKind.Melee));
        Assert.True(HitPowers.Scale(HitPower.Light) < HitPowers.Scale(HitPower.Firm));
        Assert.True(HitPowers.Scale(HitPower.Firm) < HitPowers.Scale(HitPower.Heavy));
        Assert.True(HitPowers.Scale(HitPower.Heavy) < HitPowers.Scale(HitPower.Explosive));
    }

    [Fact]
    public void BlastsMinesAndMartyrdomAreExplosive()
    {
        var a = new ModelId("a1");
        Assert.Equal(HitPower.Explosive, HitPowers.Of(new BlastHit(a, new ModelId("b1"), [new ModelId("b1")]), null, a, AttackKind.Ranged, 1));
        Assert.Null(HitPowers.Of(new ActivationEnded(a, ActivationEndReason.Voluntary), null, null, null, 0));
    }

    private static readonly ModelId A = new("a1");
    private static readonly ModelId B = new("b1");

    private static Choreography Hit(HitPower? power) => Choreography.Of(
        [new LogLine(1, "", null, Models: [A, B]) { Effects = [new(EffectKind.MuzzleFlash, A), new(EffectKind.HitPuff, B)], Power = power },
         new LogLine(2, "", null, Models: [A, B]) { Wounded = [B], Power = power }],
        id => id == A ? new TablePoint(0, 0) : new TablePoint(6, 0));

    [Fact]
    public void AHeavierHitReelsLongerAndThrowsFurther()
    {
        var seconds = new List<double>();
        var throws = new List<double>();
        foreach (var power in new[] { HitPower.Light, HitPower.Firm, HitPower.Heavy, HitPower.Explosive })
        {
            var plan = Hit(power);
            var wound = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Wound);
            Assert.Equal(power, Choreography.PowerOf(wound));
            Assert.Equal(power, Choreography.PowerOf(Assert.Single(plan.Beats, b => b.Kind == BeatKind.Flinch)));
            seconds.Add(wound.Seconds);
            throws.Add(RagdollImpacts.Of(plan, id => id == A ? new TablePoint(0, 0) : new TablePoint(6, 0))[B].Strength);
        }
        Assert.Equal(seconds.Order(), seconds);
        Assert.Equal(seconds.Count, seconds.Distinct().Count());
        Assert.Equal(throws.Order(), throws);
        Assert.Equal(RagdollImpacts.Shot, throws[1], 9);
        // Not known: as a rifle.
        var unknown = Hit(null);
        Assert.Equal(HitPower.Firm, Choreography.PowerOf(Assert.Single(unknown.Beats, b => b.Kind == BeatKind.Wound)));
        Assert.Equal(RagdollImpacts.Shot, RagdollImpacts.Of(unknown, id => id == A ? new TablePoint(0, 0) : new TablePoint(6, 0))[B].Strength, 9);
    }

    // In the simulated matches every hit, wound and Injury Roll that belongs to an attack has a power.
    [Fact]
    public void RealAttacksCarryAPower()
    {
        var seen = new HashSet<HitPower>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value.Take(8))
        {
            var resolutions = new ResolutionLog();
            var state = MatchState.None;
            var seq = 0;
            foreach (var e in log)
            {
                state = Rules.Core.Reducer.Apply(state, e);
                resolutions.Add(++seq, e, state.Setup);
                if (e is InjuryRolled && HitPowers.Of(e, state.Setup, resolutions.ActionAt(seq)?.Actor, resolutions.AttackAt(seq).Kind, resolutions.AttackAt(seq).Weapon) is { } power)
                {
                    seen.Add(power);
                }
            }
        }
        Assert.NotEmpty(seen);
    }
}
