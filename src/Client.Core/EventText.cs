using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

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
        AttackerChosen a => $"{a.Attacker} attacks; {a.Attacker.Opponent()} defends.",
        TrenchesScored t => $"Turn {t.Turn} trench scoring: {string.Join(", ", t.Results.Select(r => $"{r.TrenchId} defender +{r.DefenderPoints}, attacker +{r.AttackerPoints}"))}.",
        GameLengthRolled g => $"{g.Seat} rolls {g.Die} for the game's length: {(g.LastTurn == Rules.Core.Scenarios.TrenchWarfare.LengthRollTurn ? "the game ends now" : $"the game ends after Turn {g.LastTurn}")}.",
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
        SideClimbDeclared c => $"  {c.Model} climbs a tower's side ({(c.Kind == MoveKind.Dash ? "Dash" : "Move")}): a Risky roll first.",
        _ => e.GetType().Name + ".",
    };

    // Proposal G, step G4: the combat log's compact row, "A4 FIGHTS B3": the actor's short id, a verb in capitals (drawn
    // bold and coloured) and what follows. Every part comes from the event itself; the narrator's full line stays on the
    // row's tooltip. Events without an actor (turns, phases, scoring, morale) keep the narrator's line, with no verb.
    public static CompactRow Compact(GameEvent e) => e switch
    {
        ModelDeployed d => new(Id(d.Model), "DEPLOYS", d.Position is { } at ? $"→ {At(at)}" : ""),
        ActivationStarted a => new(Id(a.Model), "ACTIVATES", ""),
        ActivationEnded a => new(Id(a.Model), "ENDS", $"activation ({a.Reason})"),
        ModelStoodUp s => new(Id(s.Model), "STANDS UP", "(Movement halved)"),
        ModelMoved m => new(Id(m.Model), m.Kind == MoveKind.Dash ? "DASHES" : "MOVES", $"→ {At(m.Path[^1])} (cost {Cost(m.Cost)})"),
        DashDeclared d => new(Id(d.Model), "DECLARES", "a Dash"),
        DashRolled d => new(Id(d.Model), "ROLLS", $"Dash {d.Check.Dice.Total}: {d.Check.Outcome}"),
        SideClimbDeclared c => new(Id(c.Model), "CLIMBS", "a tower's side (Risky roll)"),
        SideClimbRolled c => new(Id(c.Model), "ROLLS", $"climb {c.Check.Dice.Total}: {c.Check.Outcome}"),
        RangedAttackDeclared r => new(Id(r.Attacker), "SHOOTS", Id(r.Target) + (r.LongRange ? " (long range)" : "") + (r.InCover ? $" (cover: {r.CoverTerrainId})" : "")),
        RangedSuccessRolled r => new(Id(r.Attacker), "ROLLS", $"{r.Check.Dice.Total} to hit {Id(r.Target)}: {r.Check.Outcome}"),
        ChargeDeclared c => new(Id(c.Model), "CHARGES", $"→ {Id(c.Target)}"),
        ChargeRolled c => new(Id(c.Model), "ROLLS", $"Charge Bonus {c.Die}"),
        ChargeMoved c => c.Path.IsEmpty ? new(Id(c.Model), "CANNOT MOVE", "(the charge fails)")
            : c.Reached ? new(Id(c.Model), "REACHES", $"{Id(c.Target)} at {At(c.Path[^1])}") : new(Id(c.Model), "FALLS SHORT", $"of {Id(c.Target)} at {At(c.Path[^1])}"),
        RetreatDeclared r => new(Id(r.Model), "RETREATS", ""),
        RetreatResolved r => r.Moved ? new(Id(r.Model), "RETREATS", $"→ {At(r.Path[^1])} (cost {Cost(r.Cost)})") : new(Id(r.Model), "STAYS", "(Down or Out of Action)"),
        MeleeAttackDeclared m => new(Id(m.Attacker), m.FreeFight ? "ATTACKS" : "FIGHTS", Id(m.Target) + (m.FreeFight ? " (retreating)" : "") + (m.DefendedObstacleId is { } o ? $" (defended obstacle: {o})" : "")),
        MeleeSuccessRolled r => new(Id(r.Attacker), "ROLLS", $"{r.Check.Dice.Total} to hit {Id(r.Target)}: {r.Check.Outcome}"),
        FriendlyTargetChosen f => new(Id(f.Attacker), "MUST SHOOT", $"its own {Id(f.Victim)}"),
        InjuryRolled i => i.Injury.Outcome switch
        {
            InjuryOutcome.OutOfAction => new(Id(i.Attacker), "KILLS", $"{Id(i.Target)} (Out of Action)"),
            InjuryOutcome.Down => new(Id(i.Attacker), "DOWNS", Id(i.Target)),
            InjuryOutcome.MinorHit => new(Id(i.Attacker), "HITS", $"{Id(i.Target)} (Minor Hit)"),
            _ => new(Id(i.Attacker), "HITS", $"{Id(i.Target)} (No Effect)"),
        },
        _ => new(null, null, Line(e).Trim()),
    };

    // Battlefield UI 3, stage U5 (decision D2): whether a log row is a success or a failure, read from the event's own
    // result only. A roll with a pass or fail (Dash, side climb, to-hit, Morale) by its outcome; an Injury Roll that
    // downs or kills is a success and one with no effect a failure (a Minor Hit is neither); a charge that reaches is a
    // success and one that falls short or cannot move a failure. Every other row is neutral.
    public static RowOutcome Outcome(GameEvent e) => e switch
    {
        DashRolled d => Of(d.Check),
        SideClimbRolled c => Of(c.Check),
        RangedSuccessRolled r => Of(r.Check),
        MeleeSuccessRolled m => Of(m.Check),
        MoraleCheckRolled m => Of(m.Check),
        InjuryRolled i => i.Injury.Outcome switch
        {
            InjuryOutcome.OutOfAction or InjuryOutcome.Down => RowOutcome.Success,
            InjuryOutcome.MinorHit => RowOutcome.None,
            _ => RowOutcome.Failure,
        },
        ChargeMoved c => c.Reached && !c.Path.IsEmpty ? RowOutcome.Success : RowOutcome.Failure,
        _ => RowOutcome.None,
    };

    private static RowOutcome Of(SuccessRollResult check) => check.Outcome == SuccessOutcome.Failure ? RowOutcome.Failure : RowOutcome.Success;

    // The mark drawn after the row's number: "✓" for a success, "✕" for a failure, none otherwise.
    public static string Mark(RowOutcome outcome) => outcome switch
    {
        RowOutcome.Success => "✓",
        RowOutcome.Failure => "✕",
        _ => "",
    };

    private static string Id(ModelId id) => id.Value.ToUpperInvariant();

    // Points and lengths as the panel writes them: inches to one decimal.
    private static string At(Rules.Core.Geometry.Point2 p) => TablePoint.Of(p).ToString();

    private static string Cost(Rules.Core.Geometry.Length length) => BoardScene.Inch(TableRect.Inches(length));

    // What kind of event it is, for the event list's tag and colour (QoL stage D). Display only.
    public static EventKind Kind(GameEvent e) => e switch
    {
        MatchCreated or RollOffResolved or DeploymentZoneChosen or DeploymentOrderSet or ModelDeployed or AttackerChosen => EventKind.Setup,
        TurnStarted or TurnEnded or PhaseEntered or InitiativeDetermined or FirstActivationChosen or MoraleOrderSet => EventKind.Turn,
        ActivationStarted or ActivationEnded => EventKind.Activation,
        ModelMoved or DashDeclared or DashRolled or SideClimbDeclared or SideClimbRolled or RetreatDeclared or RetreatResolved => EventKind.Movement,
        RangedAttackDeclared or ShootingTargetRolled or FriendlyTargetChosen or RangedSuccessRolled => EventKind.Shooting,
        ChargeDeclared or ChargeRolled or ChargeMoved => EventKind.Charge,
        MeleeAttackDeclared or MeleeSuccessRolled or FreeFightsDeclined => EventKind.Fight,
        InjuryRolled or MarkersSpent => EventKind.Injury,
        ModelStoodUp or MoraleCheckRolled or WarbandShaken or WarbandRecovered or WarbandFled => EventKind.Status,
        DecisionRequested => EventKind.Decision,
        ObjectivesScored or TrenchesScored => EventKind.Scoring,
        GameLengthRolled => EventKind.Turn,
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
        SideClimbRolled c => Faces(c.Check.Dice, c.Check.NetDice),
        RangedSuccessRolled r => Faces(r.Check.Dice, r.Check.NetDice),
        MeleeSuccessRolled m => Faces(m.Check.Dice, m.Check.NetDice),
        MoraleCheckRolled m => Faces(m.Check.Dice, m.Check.NetDice),
        InjuryRolled i => Faces(i.Injury.Dice, i.Injury.NetDice) + (i.Injury.Modifier == 0 ? "" : $", modifier {i.Injury.Modifier:+0;-0}"),
        ChargeRolled c => $"Dice: D6 {c.Die}",
        ShootingTargetRolled t => $"Dice: D6 {t.Die}",
        GameLengthRolled g => $"Dice: D6 {g.Die}",
        _ => null,
    };

    private static string Faces(RollRecord dice, int netDice)
    {
        var which = netDice > 0 ? "highest" : netDice < 0 ? "lowest" : "both";
        return $"Dice: rolled {string.Join(", ", dice.Rolled)}; kept {string.Join(", ", dice.Kept)} ({which}), total {dice.Total}";
    }
}

// One combat log row in short: the actor's short id, a verb in capitals (null for events without an actor) and the rest.
public sealed record CompactRow(string? Actor, string? Verb, string Rest)
{
    public string Text => string.Join(" ", new[] { Actor, Verb, Rest }.Where(p => !string.IsNullOrEmpty(p)));
}

public enum RowOutcome
{
    None,
    Success,
    Failure,
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
