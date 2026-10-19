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
            MarkersSpent m => [m.Model],
            RangedAttackDeclared r => [r.Attacker, r.Target],
            RangedSuccessRolled r => [r.Attacker, r.Target],
            ShootingTargetRolled s => [s.Attacker, s.Target],
            FriendlyTargetChosen f => [f.Attacker, f.Victim],
            InjuryRolled i => [i.Attacker, i.Target],
            ChargeDeclared c => [c.Model, c.Target],
            ChargeRolled c => [c.Model, c.Target],
            ChargeMoved c => [c.Model, c.Target],
            RetreatDeclared r => [r.Model],
            RetreatResolved r => [r.Model],
            MeleeAttackDeclared m => [m.Attacker, m.Target],
            MeleeSuccessRolled m => [m.Attacker, m.Target],
            _ => [],
        };
        return [.. ids.Distinct()];
    }
}
