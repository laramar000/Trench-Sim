using Rules.Core.Dice;
using Rules.Core.Events;

namespace Client.Core;

// One readable line per event, and the dice behind it. Describe is a copy of the simulator's narrator (Sim.Narrator),
// kept here because the client must not reference Sim, which holds a dice source (M7.3, S2); a test checks the two
// agree on every event. Line also covers the events the narrator leaves out, so every event has text.
public static class EventText
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
        RangedAttackDeclared r => $"  {r.Attacker} shoots {r.Target}{(r.LongRange ? " at long range" : "")}{(r.InCover ? $" (cover: {r.CoverTerrainId})" : "")}.",
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

    // Every event as a line: the narrator's text, or for the events it leaves out, a short line of their own.
    public static string Line(GameEvent e) => Describe(e) ?? e switch
    {
        DecisionRequested d => $"  {Words.Seat(d.Seat)} must decide: {Words.Decision(d.Kind)}.",
        PhaseEntered p => $"Phase: {Words.Phase(p.Phase)}.",
        MoraleOrderSet m => $"{Words.Seat(m.First)} takes its Morale Check first{(m.Chosen ? " (chosen)" : "")}.",
        TurnEnded t => $"End of turn {t.Turn}.",
        DashDeclared d => $"  {d.Model} declares a Dash.",
        _ => e.GetType().Name + ".",
    };

    // What kind of event it is, for the event list's tag and colour (QoL stage D). Display only.
    public static EventKind Kind(GameEvent e) => e switch
    {
        MatchCreated or RollOffResolved or DeploymentZoneChosen or DeploymentOrderSet or ModelDeployed => EventKind.Setup,
        TurnStarted or TurnEnded or PhaseEntered or InitiativeDetermined or FirstActivationChosen or MoraleOrderSet => EventKind.Turn,
        ActivationStarted or ActivationEnded => EventKind.Activation,
        ModelMoved or DashDeclared or DashRolled or RetreatDeclared or RetreatResolved => EventKind.Movement,
        RangedAttackDeclared or ShootingTargetRolled or FriendlyTargetChosen or RangedSuccessRolled => EventKind.Shooting,
        ChargeDeclared or ChargeRolled or ChargeMoved => EventKind.Charge,
        MeleeAttackDeclared or MeleeSuccessRolled or FreeFightsDeclined => EventKind.Fight,
        InjuryRolled or MarkersSpent => EventKind.Injury,
        ModelStoodUp or MoraleCheckRolled or WarbandShaken or WarbandRecovered or WarbandFled => EventKind.Status,
        DecisionRequested => EventKind.Decision,
        ObjectivesScored => EventKind.Scoring,
        MatchEnded => EventKind.MatchEnd,
        _ => EventKind.Other,
    };

    // The tag shown before each event line: the kind in one word.
    public static string Tag(EventKind kind) => kind switch
    {
        EventKind.Setup => "SETUP",
        EventKind.Turn => "TURN",
        EventKind.Activation => "ACTIVATE",
        EventKind.Movement => "MOVE",
        EventKind.Shooting => "SHOOT",
        EventKind.Charge => "CHARGE",
        EventKind.Fight => "FIGHT",
        EventKind.Injury => "INJURY",
        EventKind.Status => "STATUS",
        EventKind.Decision => "DECIDE",
        EventKind.Scoring => "SCORE",
        EventKind.MatchEnd => "END",
        _ => "EVENT",
    };

    // The dice behind a roll event, every die as rolled and which were kept; null for an event without dice.
    public static string? Dice(GameEvent e) => e switch
    {
        RollOffResolved r => "Dice: " + string.Join("; ", r.Rounds.Select(x => $"Seat One {x.SeatOne}, Seat Two {x.SeatTwo}{(x.IsTie ? " (tie, rolled again)" : "")}")),
        DashRolled d => Faces(d.Check.Dice, d.Check.NetDice),
        RangedSuccessRolled r => Faces(r.Check.Dice, r.Check.NetDice),
        MeleeSuccessRolled m => Faces(m.Check.Dice, m.Check.NetDice),
        MoraleCheckRolled m => Faces(m.Check.Dice, m.Check.NetDice),
        InjuryRolled i => Faces(i.Injury.Dice, i.Injury.NetDice) + (i.Injury.Modifier == 0 ? "" : $", modifier {i.Injury.Modifier:+0;-0}"),
        ChargeRolled c => $"Dice: D6 {c.Die}",
        ShootingTargetRolled t => $"Dice: D6 {t.Die}",
        _ => null,
    };

    private static string Faces(RollRecord dice, int netDice)
    {
        var which = netDice > 0 ? "highest" : netDice < 0 ? "lowest" : "both";
        return $"Dice: rolled {string.Join(", ", dice.Rolled)}; kept {string.Join(", ", dice.Kept)} ({which}), total {dice.Total}";
    }
}

public enum EventKind
{
    Other,
    Setup,
    Turn,
    Activation,
    Movement,
    Shooting,
    Charge,
    Fight,
    Injury,
    Status,
    Decision,
    Scoring,
    MatchEnd,
}
