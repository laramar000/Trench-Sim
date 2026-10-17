using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

public enum NoteTone
{
    Success,
    Failure,
    Damage,
    Info,
}

// A short word shown for a moment above one model on the battlefield ("MISSED", "DOWN", "2 BLOOD SPENT").
public sealed record UnitNote(ModelId Model, string Text, NoteTone Tone);

// The notes an event gives, read only from the server's event (every event is public to both seats, so both players
// see the same notes and nothing hidden is shown). The client's own refusals (a move it would not send) are added by
// the battlefield, not here.
public static class UnitNotes
{
    public static IReadOnlyList<UnitNote> Of(GameEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e switch
        {
            DashRolled d => [d.Check.Outcome == SuccessOutcome.Failure ? new(d.Model, "DASH FAILED", NoteTone.Failure) : new(d.Model, "DASH", NoteTone.Success)],
            RangedSuccessRolled r => [Hit(r.Attacker, r.Check)],
            MeleeSuccessRolled m => [Hit(m.Attacker, m.Check)],
            InjuryRolled i => [Injury(i.Target, i.Injury)],
            MarkersSpent { Count: > 0 } m => [new(m.Model, $"{m.Count} BLOOD SPENT", NoteTone.Info)],
            ChargeRolled c => [new(c.Model, $"CHARGE +{c.Die}\"", NoteTone.Info)],
            ChargeMoved { Reached: false } c => [new(c.Model, "CHARGE FAILED", NoteTone.Failure)],
            ModelStoodUp s => [new(s.Model, "STANDS UP", NoteTone.Info)],
            ShootingTargetRolled { HitsFriend: true } t => [new(t.Attacker, "FRIENDLY FIRE", NoteTone.Failure)],
            _ => [],
        };
    }

    private static UnitNote Hit(ModelId attacker, SuccessRollResult check) => check.Outcome switch
    {
        SuccessOutcome.Failure => new(attacker, "MISSED", NoteTone.Failure),
        SuccessOutcome.CriticalSuccess => new(attacker, "CRITICAL HIT", NoteTone.Success),
        _ => new(attacker, "HIT", NoteTone.Success),
    };

    private static UnitNote Injury(ModelId target, InjuryRollResult injury)
    {
        var blood = injury.BloodMarkers > 0 ? $" +{injury.BloodMarkers} BLOOD" : "";
        return injury.Outcome switch
        {
            InjuryOutcome.NoEffect => new(target, "NO EFFECT", NoteTone.Info),
            InjuryOutcome.MinorHit => new(target, "MINOR HIT" + blood, NoteTone.Damage),
            InjuryOutcome.Down => new(target, "DOWN" + blood, NoteTone.Damage),
            _ => new(target, "OUT OF ACTION", NoteTone.Damage),
        };
    }
}
