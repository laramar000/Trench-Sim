using Rules.Core.Events;
using Rules.Core.Dice;

namespace Sim;

// One readable line per event, for `--seed N`. Names come from the rules' opaque ids only.
public static class Narrator
{
    public static string? Describe(GameEvent e) => e switch
    {
        MatchCreated c => $"Match created: scenario {c.Setup.ScenarioId}, {c.Setup.Models.Length} models, {c.Setup.TurnLimit} turns.",
        RollOffResolved r => $"Roll-off for {r.Purpose}: {string.Join(", ", r.Rounds.Select(x => $"{x.SeatOne}-{x.SeatTwo}"))}; {r.Winner} wins.",
        DeploymentZoneChosen z => $"{z.Seat} takes the {z.ZoneId} zone.",
        DeploymentOrderSet o => $"{o.First} deploys first.",
        ModelDeployed d => $"{d.Seat} sets up {d.Model} at {d.Position}.",
        TurnStarted t => $"--- Turn {t.Turn} ---",
        InitiativeDetermined i => $"Initiative: {i.CountSeatOne} against {i.CountSeatTwo} models, {i.Holder} holds it.",
        FirstActivationChosen f => $"{f.FirstActivator} activates first.",
        ActivationStarted a => $"{a.Seat} activates {a.Model}.",
        ModelStoodUp s => $"  {s.Model} stands up (Movement halved).",
        ModelMoved m => $"  {m.Model} {(m.Kind == MoveKind.Dash ? "dashes" : "moves")} to {m.Path[^1]} (cost {m.Cost}).",
        DashRolled d => $"  {d.Model} Dash roll {d.Check.Dice.Total}: {d.Check.Outcome}.",
        SideClimbRolled c => $"  {c.Model} climb roll {c.Check.Dice.Total}: {c.Check.Outcome}.",
        RangedAttackDeclared r => $"  {r.Attacker} shoots {r.Target}{(r.Elevated ? " from an elevated position" : "")}{(r.LongRange ? " at long range" : "")}{(r.InCover ? $" (cover: {r.CoverTerrainId})" : "")}.",
        ChargeDeclared c => $"  {c.Model} declares a charge on {c.Target}.",
        ChargeRolled c => $"  {c.Model} Charge Bonus {c.Die}.",
        ChargeMoved c => c.Path.IsEmpty ? $"  {c.Model} cannot move; the charge fails." : $"  {c.Model} charges to {c.Path[^1]} (cost {c.Cost}): {(c.Reached ? "reaches" : "falls short of")} {c.Target}.",
        RetreatDeclared r => $"  {r.Model} retreats.",
        FreeFightsDeclined f => $"  {f.Seat} declines the remaining free attacks.",
        RetreatResolved r => r.Moved ? $"  {r.Model} retreats to {r.Path[^1]} (cost {r.Cost})." : $"  {r.Model} is taken Down or Out of Action and stays where it is.",
        MeleeAttackDeclared m => $"  {m.Attacker} {(m.FreeFight ? "attacks the retreating" : "fights")} {m.Target}{(m.DefendedObstacleId is { } o ? $" (defended obstacle: {o})" : "")}.",
        MeleeSuccessRolled r => $"  Melee Success Roll {r.Check.Dice.Total} ({r.Check.NetDice:+0;-0;0} dice): {r.Check.Outcome}.",
        ShootingTargetRolled t => $"  Shot into melee: D6 {t.Die}, {(t.HitsFriend ? "a friendly model must be targeted" : "the enemy model may be targeted")}.",
        FriendlyTargetChosen f => $"  {f.Attacker} must shoot its own {f.Victim}.",
        MarkersSpent m => m.Count == 0 ? $"  {m.Spender} spends no Blood Markers." : $"  {m.Spender} spends {m.Count} Blood Marker(s) from {m.Model}.",
        RangedSuccessRolled r => $"  Success Roll {r.Check.Dice.Total} ({r.Check.NetDice:+0;-0;0} dice): {r.Check.Outcome}.",
        InjuryRolled i => $"  Injury Roll on {i.Target}: {i.Injury.Total} ({i.Injury.NetDice:+0;-0;0} injury dice): {i.Injury.Outcome}.",
        ActivationEnded a => $"  {a.Model} ends its activation ({a.Reason}).",
        MoraleCheckRolled m => $"{m.Seat} Morale Check ({m.ModelsDownOrOut} of {m.WarbandSize} down or out): {m.Check.Outcome}.",
        WarbandShaken w => $"{w.Seat} is Shaken.",
        WarbandRecovered w => $"{w.Seat} recovers.",
        WarbandFled w => $"{w.Seat} flees ({w.Reason}).",
        ObjectivesScored o => $"Turn {o.Turn} scoring: {string.Join(", ", o.Results.Where(r => r.Controller is not null).Select(r => $"{r.ObjectiveId} to {r.Controller}"))}{(o.Results.All(r => r.Controller is null) ? "nobody controls an Objective" : "")}.",
        MatchEnded m => $"MATCH ENDED: {(m.Winner is { } w ? $"{w} wins" : "draw")} ({m.Reason}).",
        _ => null,
    };
}
