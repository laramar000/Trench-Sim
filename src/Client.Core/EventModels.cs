using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// QoL I1: the models a server event names (the actor first), so a click on its combat-log row can point them out on
// the battlefield. Read only from the event; `EventModelsTests` check against every model id the event carries.
public static class EventModels
{
    public static IReadOnlyList<ModelId> Of(GameEvent e)
    {
        IEnumerable<ModelId> ids = e switch
        {
            ModelDeployed d => [d.Model],
            ActivationStarted a => [a.Model],
            ActivationEnded a => [a.Model],
            ModelStoodUp s => [s.Model],
            ModelMoved m => [m.Model],
            DashDeclared d => [d.Model],
            DashRolled d => [d.Model],
            SideClimbDeclared c => [c.Model],
            SideClimbRolled c => [c.Model],
            MarkersSpent m => [m.Model],
            RangedAttackDeclared r => [r.Attacker, r.Target],
            RangedSuccessRolled r => [r.Attacker, r.Target],
            ShootingTargetRolled s => [s.Attacker, s.Target],
            FriendlyTargetChosen f => [f.Attacker, f.Victim],
            InjuryRolled i => [i.Attacker, i.Target],
            BlastHit b => [b.Attacker, .. b.Hit],
            AttackScattered { Target: { } aimed } s => [s.Attacker, aimed, .. s.Hit],
            AttackScattered s => [s.Attacker, .. s.Hit],
            ScatterMissed { Target: { } aimed } s => [s.Attacker, aimed],
            ScatterMissed s => [s.Attacker],
            PointAttackDeclared p => [p.Attacker],
            PointSuccessRolled p => [p.Attacker],
            PointBlastHit b => [b.Attacker, .. b.Hit],
            BloodMarkersPlaced b => [b.Source, .. b.Targets],
            ChargeDeclared c => [c.Model, c.Target],
            ChargeRolled c => [c.Model, c.Target],
            ChargeMoved c => [c.Model, c.Target],
            RetreatDeclared r => [r.Model],
            RetreatResolved r => [r.Model],
            MeleeAttackDeclared m => [m.Attacker, m.Target],
            MeleeSuccessRolled m => [m.Attacker, m.Target],
            GloriousDeedCompleted { Model: { } model } => [model],
            _ => [],
        };
        return [.. ids.Distinct()];
    }
}
