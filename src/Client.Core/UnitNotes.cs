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
            // Roster stage R5h (U113): a Treat ACTION.
            TreatRolled t => t.Check.Outcome == SuccessOutcome.Failure ? [new(t.Model, "TREAT FAILED", NoteTone.Failure)]
                : t.Treated ? [new(t.Target, t.Choice == TreatChoice.StandUp ? "STANDS UP" : "-1 BLOOD", NoteTone.Success)]
                : [new(t.Target, "NOTHING TO TREAT", NoteTone.Info)],
            // Roster stage R6b (U118): an Aim or Fortify ACTION.
            SelfActionRolled s => [s.Succeeded ? new(s.Model, s.Action == SelfActionKind.Aim ? "AIMED +2" : "FORTIFIED", NoteTone.Success)
                : new(s.Model, s.Action == SelfActionKind.Aim ? "AIM FAILED" : "FORTIFY FAILED", NoteTone.Failure)],
            DashRolled d => [d.Check.Outcome == SuccessOutcome.Failure ? new(d.Model, "DASH FAILED", NoteTone.Failure) : new(d.Model, "DASH", NoteTone.Success)],
            SideClimbRolled c => [c.Check.Outcome == SuccessOutcome.Failure ? new(c.Model, "CLIMB FAILED", NoteTone.Failure) : new(c.Model, "CLIMB", NoteTone.Success)],
            RangedSuccessRolled r => [Hit(r.Attacker, r.Check)],
            PointSuccessRolled r => [Hit(r.Attacker, r.Check)],
            MeleeSuccessRolled m => [Hit(m.Attacker, m.Check)],
            InjuryRolled i => [Injury(i.Target, i.Result, i.PlacedMarkers, i.Tough, i.Spared)],
            MarkersSpent { Count: > 0 } m => [new(m.Model, $"{m.Count} BLOOD SPENT", NoteTone.Info)],
            ChargeRolled c => [new(c.Model, $"CHARGE +{c.Die}\"", NoteTone.Info)],
            ChargeMoved { Reached: false } c => [new(c.Model, "CHARGE FAILED", NoteTone.Failure)],
            ModelStoodUp s => [new(s.Model, "STANDS UP", NoteTone.Info)],
            ShootingTargetRolled { HitsFriend: true } t => [new(t.Attacker, "FRIENDLY FIRE", NoteTone.Failure)],
            BlastHit b => [.. b.Hit.Select(h => new UnitNote(h, "IN THE BLAST", NoteTone.Failure))],
            AttackScattered s => [.. s.Hit.Select(h => new UnitNote(h, "IN THE BLAST", NoteTone.Failure))],
            PointBlastHit b => [.. b.Hit.Select(h => new UnitNote(h, "IN THE BLAST", NoteTone.Failure))],
            // Roster stage R5g (U112): a Crown of Hellfire or Damned Pact burns the enemies within 1".
            BloodMarkersPlaced b => [.. b.Targets.Select(t => new UnitNote(t, b.Warded.Contains(t) ? "BLOOD KEPT OFF" : "+1 BLOOD", b.Warded.Contains(t) ? NoteTone.Info : NoteTone.Failure))],
            _ => [],
        };
    }

    private static UnitNote Hit(ModelId attacker, SuccessRollResult check) => check.Outcome switch
    {
        SuccessOutcome.Failure => new(attacker, "MISSED", NoteTone.Failure),
        SuccessOutcome.CriticalSuccess => new(attacker, "CRITICAL HIT", NoteTone.Success),
        _ => new(attacker, "HIT", NoteTone.Success),
    };

    // Roster stage R4a: placed counts the BLOOD MARKERS SHRAPNEL, GAS and FIRE add, even on No Effect.
    // Roster stage R5b: TOUGH turns the first Out of Action into Down (U106).
    // Roster stage R5e: placed, the BLOOD MARKERS placed (a Sacrificial Lamb keeps the first off, U109).
    private static UnitNote Injury(ModelId target, InjuryOutcome outcome, int placed, bool tough, bool spared)
    {
        var blood = placed > 0 ? $" +{placed} BLOOD" : "";
        return outcome switch
        {
            InjuryOutcome.Down when tough => new(target, "TOUGH: DOWN" + blood, NoteTone.Damage),
            // Roster stage R6c (U119).
            InjuryOutcome.NoEffect when spared => new(target, "MEMENTO MORI: NO EFFECT" + blood, placed > 0 ? NoteTone.Damage : NoteTone.Info),
            InjuryOutcome.NoEffect => new(target, "NO EFFECT" + blood, placed > 0 ? NoteTone.Damage : NoteTone.Info),
            InjuryOutcome.MinorHit => new(target, "MINOR HIT" + blood, NoteTone.Damage),
            InjuryOutcome.Down => new(target, "DOWN" + blood, NoteTone.Damage),
            _ => new(target, "OUT OF ACTION", NoteTone.Damage),
        };
    }
}
