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
        ModelStoodUp s => $"  {s.Model} stands up{(s.Quick ? " (Movement not halved: Frightening Speed)" : " (Movement halved)")}.",
        ModelMoved m => $"  {m.Model} {(m.Kind == MoveKind.Dash ? "dashes" : "moves")} to {m.Path[^1]} (cost {m.Cost}).",
        TreatRolled t => $"  {t.Model} Treat roll {t.Check.Dice.Total}: {t.Check.Outcome}{(t.Treated ? t.Choice == TreatChoice.StandUp ? $"; {t.Target} stands up." : $"; 1 Blood Marker removed from {t.Target}." : t.Check.Outcome == SuccessOutcome.Failure ? "." : $"; {t.Target} has no Blood Marker left to remove.")}",
        SelfActionRolled s => $"  {s.Model} {Words.SelfAction(s.Action)} roll {s.Check.Dice.Total}: {s.Check.Outcome}{(s.Succeeded ? $"; {Words.SelfActionResult(s)}." : ".")}",
        DashRolled d => $"  {d.Model} Dash roll {d.Check.Dice.Total}: {d.Check.Outcome}.",
        SideClimbRolled c => $"  {c.Model} climb roll {c.Check.Dice.Total}: {c.Check.Outcome}.",
        RangedAttackDeclared r => $"  {r.Attacker} shoots {r.Target}{(r.Elevated ? " from an elevated position" : "")}{(r.LongRange ? " at long range" : "")}{(r.InCover ? $" (cover: {r.CoverTerrainId})" : "")}.",
        ChargeDeclared c => $"  {c.Model} declares a charge on {c.Target}.",
        ChargeRolled c => $"  {c.Model} Charge Bonus {c.Die}{(c.Dropped == 0 ? "" : $" (Shock Charge, {c.Dropped} dropped)")}.",
        ChargeMoved c => c.Path.IsEmpty ? $"  {c.Model} cannot move; the charge fails." : $"  {c.Model} charges to {c.Path[^1]} (cost {c.Cost}): {(c.Reached ? "reaches" : "falls short of")} {c.Target}.",
        RetreatDeclared r => $"  {r.Model} retreats.",
        FreeFightsDeclined f => $"  {f.Seat} declines the remaining free attacks.",
        RetreatResolved r => r.Moved ? $"  {r.Model} retreats to {r.Path[^1]} (cost {r.Cost})." : $"  {r.Model} is taken Down or Out of Action and stays where it is.",
        MeleeAttackDeclared m => $"  {m.Attacker} {(m.FreeFight ? "attacks the retreating" : m.Weapon == ModelProfile.GrindWeapon ? "grinds to dust" : m.Weapon == ModelProfile.TrampleWeapon ? "tramples" : "fights")} {m.Target}{(m.DefendedObstacleId is { } o ? $" (defended obstacle: {o})" : "")}.",
        MeleeSuccessRolled r => $"  Melee Success Roll {r.Check.Dice.Total} ({r.Check.NetDice:+0;-0;0} dice): {r.Check.Outcome}.",
        ShootingTargetRolled t => $"  Shot into melee: D6 {t.Die}, {(t.HitsFriend ? "a friendly model must be targeted" : "the enemy model may be targeted")}.",
        FriendlyTargetChosen f => $"  {f.Attacker} must shoot its own {f.Victim}.",
        BlastHit b => $"  The blast hits {string.Join(", ", b.Hit)}.",
        PointAttackDeclared p => $"  {p.Attacker} {(p.Weapon == ModelProfile.GrenadeWeapon ? "throws a grenade at" : "shoots at")} the point {p.At}{(p.LongRange ? " at long range" : "")}.",
        PointSuccessRolled r => $"  Success Roll {r.Check.Dice.Total} ({r.Check.NetDice:+0;-0;0} dice): {r.Check.Outcome}.",
        PointBlastHit b => b.Hit.IsEmpty ? "  The blast hits nobody." : $"  The blast hits {string.Join(", ", b.Hit)}.",
        AttackScattered s => $"  The attack scatters {s.Inches}\" to {s.At}: {(s.Hit.IsEmpty ? "the blast hits nobody" : $"the blast hits {string.Join(", ", s.Hit)}")}.",
        ScatterMissed s => $"  The attack would scatter {s.Inches}\" but has nowhere to land: it misses.",
        MarkersSpent { Purpose: MarkerSpendPurpose.Bloodbath } m => $"  {m.Spender} spends {m.Count} Blood Marker(s) from {m.Model}: a Bloodbath Roll.",
        MarkersSpent m => m.Count == 0 ? $"  {m.Spender} spends no Blood Markers." : $"  {m.Spender} spends {m.Count} Blood Marker(s) from {m.Model}.",
        RangedSuccessRolled r => $"  Success Roll {r.Check.Dice.Total} ({r.Check.NetDice:+0;-0;0} dice): {r.Check.Outcome}.",
        InjuryRolled i => $"  Injury Roll on {i.Target}: {i.Injury.Total} ({(i.Bloodbath ? $"Bloodbath Roll, {i.Injury.Dice.Kept.Count} dice added, {i.Injury.NetDice:+0;-0;0} injury dice" : i.Injury.AllFour ? "4D6 added together" : $"{i.Injury.NetDice:+0;-0;0} injury dice")}): {i.Injury.Outcome}.{(i.Tough ? " TOUGH: Down instead." : "")}{(i.Spared ? " Memento Mori: No Effect instead." : "")}{(i.Cloaked ? " Oracle Beast Cloak: No Effect instead." : "")}{(i.Upheld ? " Too Proud to Fall: it stays standing." : "")}{(i.Warded ? Warded : "")}",
        ActivationEnded a => $"  {a.Model} ends its activation ({a.Reason}).",
        MoraleCheckRolled m => $"{m.Seat} Morale Check ({m.ModelsDownOrOut} of {m.WarbandSize} down or out{(m.Check.NetDice > 0 ? ", +1 DICE from its LEADER" : "")}): {m.Check.Outcome}.",
        WarbandShaken w => $"{w.Seat} is Shaken.",
        WarbandRecovered w => $"{w.Seat} recovers.",
        WarbandFled w => $"{w.Seat} flees ({w.Reason}).",
        ObjectivesScored o => $"Turn {o.Turn} scoring: {string.Join(", ", o.Results.Where(r => r.Controller is not null).Select(r => $"{r.ObjectiveId} to {r.Controller}"))}{(o.Results.All(r => r.Controller is null) ? "nobody controls an Objective" : "")}.",
        AttackerChosen a => $"{a.Attacker} attacks; {a.Attacker.Opponent()} defends.",
        TrenchesScored t => $"Turn {t.Turn} trench scoring: {string.Join(", ", t.Results.Select(r => $"{r.TrenchId} defender +{r.DefenderPoints}, attacker +{r.AttackerPoints}"))}.",
        InjuryRollMade m => $"  Injury Roll on {m.Target}: {m.Injury.Total} ({m.Injury.NetDice:+0;-0;0} injury dice): {m.Injury.Outcome}, held while {m.Target}'s player may answer it.",
        ReactionDeclined d => $"  {d.Seat} casts nothing.",
        SpellCast s => $"  {s.Model} casts {Spells.Name(s.Spell)}{(s.Target is { } t ? $" on {t}" : "")}{(s.TakeBlessing ? " (a BLESSING MARKER)" : "")}, paid with {string.Join(", ", s.Payment.Select(p => $"{p.Count} from {p.Model}"))}.",
        ModelsMoved m => m.Moves.IsEmpty ? $"  {m.Source}'s ACTION moves nobody." : $"  {m.Source}'s ACTION moves {string.Join(", ", m.Moves.Select(x => $"{x.Model} to {x.To}"))}{(m.Die > 0 ? $" (D6: {m.Die})" : "")}.",
        BaseTurned t => $"  {t.Model} turns its base to lie along {t.Axis}.",
        BodyguardAnswered { Guard: { } g } b => $"  {g} takes the hit meant for {b.Target} (Bodyguard).",
        BodyguardAnswered b => $"  {b.Target} keeps the hit; no Bodyguard steps in.",
        TalismanUsed t => $"  {t.Model} uses a Talisman: the failed Risky roll does not end its activation.",
        MartyrdomTriggered m => $"  {m.Model} detonates its Martyrdom Device: Injury Rolls for {string.Join(", ", m.Hit)}{(m.Close.IsEmpty ? "" : $" (+1 INJURY DICE within 1\": {string.Join(", ", m.Close)})")}.",
        BloodMarkersPlaced b => $"  Flames from {b.Source} put 1 Blood Marker on each enemy within 1\": {string.Join(", ", b.Targets)}.{(b.Warded.IsEmpty ? "" : $" The Sacrificial Lamb's blood keeps it off {string.Join(", ", b.Warded)}.")}",
        GloriousDeedCompleted d => $"{d.Seat} completes the Glorious Deed {Rules.Core.Deeds.Name(d.Deed)}{(d.Model is { } m ? $" ({m})" : "")}: +{Rules.Core.Deeds.VictoryPoints} VP.",
        GameLengthRolled g => $"{g.Seat} rolls {g.Die} for the game's length: {(g.LastTurn == Rules.Core.Scenarios.TrenchWarfare.LengthRollTurn ? "the game ends now" : $"the game ends after Turn {g.LastTurn}")}.",
        ShrinePlaced h => $"  {h.Carrier} deploys its Field Shrine at ({h.At.X}, {h.At.Y}).",
        ShrineDeclined h => $"  {h.Carrier} does not deploy its Field Shrine.",
        MinesLaid l => $"{l.Seat} sets up {l.Markers.Length} Mine Markers; {Rules.Core.Geometry.Mines.Duds} of them are secret duds.",
        MineRevealed r => $"  {r.Model} halts by {r.Marker}: {(r.Live ? "a live mine" : "a dud")}.",
        MineDefuseRolled d => $"  {d.Model} Defuse Mine roll {d.Check.Dice.Total}: {d.Check.Outcome}{(d.Defused ? $"; {d.Marker} is removed." : "; it could not.")}",
        MineExploded x => $"  {x.Marker} explodes under {x.Model}: Injury Roll {x.Injury.Total}: {x.Injury.Outcome}{(x.Tough ? " (TOUGH: Down instead)" : "")}{(x.Spared ? " (Memento Mori: No Effect instead)" : "")}{(x.Negated ? ", and its NEGATE SHRAPNEL stops the extra Blood Marker." : ", and SHRAPNEL adds a Blood Marker.")}{(x.Warded ? Warded : "")}",
        MoveResumed m => m.Path.IsEmpty ? $"  {m.Model} carries on." : $"  {m.Model} carries on to {m.Path[^1]}.",
        MatchEnded m => $"MATCH ENDED: {(m.Winner is { } w ? $"{w} wins" : "draw")} ({m.Reason}).",
        _ => null,
    };

    // Every event as a line: the narrator's text, or for the events it leaves out, a short line of their own.
    // Roster stage R5e (U109).
    private const string Warded = " The Sacrificial Lamb's blood keeps off the first Blood Marker.";

    public static string Line(GameEvent e) => Describe(e) ?? e switch
    {
        DecisionRequested d => $"  {Words.Seat(d.Seat)} must decide: {Words.Decision(d.Kind)}.",
        PhaseEntered p => $"Phase: {Words.Phase(p.Phase)}.",
        MoraleOrderSet m => $"{Words.Seat(m.First)} takes its Morale Check first{(m.Chosen ? " (chosen)" : "")}.",
        TurnEnded t => $"End of turn {t.Turn}.",
        DashDeclared d => $"  {d.Model} declares a Dash.",
        TreatDeclared t => $"  {t.Model} declares a Treat ACTION on {t.Target} ({(t.Choice == TreatChoice.StandUp ? "stand it up" : "remove 1 Blood Marker")}).",
        SelfActionDeclared s => $"  {s.Model} declares {Words.SelfActionWithArticle(s.Action)} ACTION{(s.Target is { } t ? $" on {t}" : "")}.",
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
        TreatDeclared t => new(Id(t.Model), "TREATS", $"{Id(t.Target)}: {(t.Choice == TreatChoice.StandUp ? "stand up" : "remove 1 Blood Marker")}"),
        TreatRolled t => new(Id(t.Model), "ROLLS", $"Treat {t.Check.Dice.Total}: {t.Check.Outcome}"),
        SelfActionDeclared s => new(Id(s.Model), Words.SelfActionVerb(s.Action), s.Target is { } t ? Id(t) : "Risky roll"),
        SelfActionRolled s => new(Id(s.Model), "ROLLS", $"{Words.SelfAction(s.Action)} {s.Check.Dice.Total}: {s.Check.Outcome}"),
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
        MeleeAttackDeclared m => new(Id(m.Attacker), m.FreeFight ? "ATTACKS" : m.Weapon == ModelProfile.GrindWeapon ? "GRINDS" : m.Weapon == ModelProfile.TrampleWeapon ? "TRAMPLES" : "FIGHTS", Id(m.Target) + (m.FreeFight ? " (retreating)" : "") + (m.DefendedObstacleId is { } o ? $" (defended obstacle: {o})" : "")),
        MeleeSuccessRolled r => new(Id(r.Attacker), "ROLLS", $"{r.Check.Dice.Total} to hit {Id(r.Target)}: {r.Check.Outcome}"),
        FriendlyTargetChosen f => new(Id(f.Attacker), "MUST SHOOT", $"its own {Id(f.Victim)}"),
        BlastHit b => new(Id(b.Attacker), "BLAST HITS", string.Join(", ", b.Hit.Select(Id))),
        PointAttackDeclared p => new(Id(p.Attacker), p.Weapon == ModelProfile.GrenadeWeapon ? "THROWS AT" : "SHOOTS AT", At(p.At) + (p.LongRange ? " (long range)" : "")),
        PointSuccessRolled r => new(Id(r.Attacker), "ROLLS", $"{r.Check.Dice.Total} to hit {At(r.At)}: {r.Check.Outcome}"),
        MartyrdomTriggered m => new(Id(m.Model), "DETONATES", string.Join(", ", m.Hit.Select(Id))),
        InjuryRollMade m => new(Id(m.Attacker), "ROLLS", $"{Id(m.Target)}: {m.Injury.Total} (held for a reaction)"),
        ReactionDeclined d => new(null, "NO SPELL", $"{d.Seat} casts nothing"),
        SpellCast s => new(Id(s.Model), "CASTS", Spells.Name(s.Spell) + (s.Target is { } t ? $" on {Id(t)}" : "")),
        ModelsMoved m => new(Id(m.Source), "MOVES", m.Moves.IsEmpty ? "nobody" : string.Join(", ", m.Moves.Select(x => Id(x.Model))) + (m.Die > 0 ? $" (D6: {m.Die})" : "")),
        BaseTurned t => new(Id(t.Model), "TURNS ITS BASE", $"to lie along {t.Axis}"),
        BodyguardAnswered { Guard: { } g } b => new(Id(g), "TAKES THE HIT", $"meant for {Id(b.Target)} (Bodyguard)"),
        BodyguardAnswered b => new(Id(b.Target), "KEEPS THE HIT", "no Bodyguard steps in"),
        TalismanUsed t => new(Id(t.Model), "USES A TALISMAN", "the activation goes on"),
        BloodMarkersPlaced b => new(Id(b.Source), "BURNS", string.Join(", ", b.Targets.Select(Id)) + " (+1 Blood Marker)"),
        PointBlastHit b => new(Id(b.Attacker), "BLAST HITS", b.Hit.IsEmpty ? "nobody" : string.Join(", ", b.Hit.Select(Id))),
        AttackScattered s => new(Id(s.Attacker), "SCATTERS", $"{s.Inches}\" to {At(s.At)}: {(s.Hit.IsEmpty ? "hits nobody" : "hits " + string.Join(", ", s.Hit.Select(Id)))}"),
        ScatterMissed s => new(Id(s.Attacker), "MISSES", $"nowhere to scatter {s.Inches}\""),
        MineRevealed r => new(Id(r.Model), "HALTS", $"by {r.Marker}: {(r.Live ? "LIVE MINE" : "a dud")}"),
        MineDefuseRolled d => new(Id(d.Model), "ROLLS", $"Defuse Mine {d.Check.Dice.Total}: {d.Check.Outcome}"),
        MineExploded x => x.Result switch
        {
            InjuryOutcome.OutOfAction => new(Id(x.Model), "BLOWN UP", $"by {x.Marker} (Out of Action)"),
            InjuryOutcome.Down => new(Id(x.Model), "BLOWN DOWN", $"by {x.Marker}{(x.Tough ? " (TOUGH)" : "")}"),
            InjuryOutcome.MinorHit => new(Id(x.Model), "HIT", $"by {x.Marker} (Minor Hit{(x.Negated ? "" : ", +1 SHRAPNEL")})"),
            _ => new(Id(x.Model), "HIT", $"by {x.Marker} (No Effect{(x.Spared ? ", Memento Mori" : "")}{(x.Negated ? ", NEGATE SHRAPNEL" : ", +1 SHRAPNEL")})"),
        },
        MoveResumed m => new(Id(m.Model), "CARRIES ON", m.Path.IsEmpty ? "" : $"→ {At(m.Path[^1])}"),
        InjuryRolled i => i.Result switch
        {
            InjuryOutcome.OutOfAction => new(Id(i.Attacker), "KILLS", $"{Id(i.Target)} (Out of Action)"),
            InjuryOutcome.Down => new(Id(i.Attacker), "DOWNS", $"{Id(i.Target)}{(i.Tough ? " (TOUGH)" : "")}"),
            InjuryOutcome.MinorHit => new(Id(i.Attacker), "HITS", $"{Id(i.Target)} (Minor Hit)"),
            _ => new(Id(i.Attacker), "HITS", $"{Id(i.Target)} (No Effect{(i.Spared ? ", Memento Mori" : "")})"),
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
        TreatRolled t => Of(t.Check),
        MineDefuseRolled d => Of(d.Check),
        SelfActionRolled s => Of(s.Check),
        SideClimbRolled c => Of(c.Check),
        RangedSuccessRolled r => Of(r.Check),
        PointSuccessRolled r => Of(r.Check),
        MeleeSuccessRolled m => Of(m.Check),
        MoraleCheckRolled m => Of(m.Check),
        InjuryRolled i => i.Result switch
        {
            InjuryOutcome.OutOfAction or InjuryOutcome.Down => RowOutcome.Success,
            InjuryOutcome.MinorHit => RowOutcome.None,
            _ => RowOutcome.Failure,
        },
        ChargeMoved c => c.Reached && !c.Path.IsEmpty ? RowOutcome.Success : RowOutcome.Failure,
        // From the moving model's side: a dud is a relief, a live mine is not.
        MineRevealed r => r.Live ? RowOutcome.Failure : RowOutcome.Success,
        ScatterMissed => RowOutcome.Failure,
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
        MatchCreated or RollOffResolved or DeploymentZoneChosen or DeploymentOrderSet or ModelDeployed or AttackerChosen or MinesLaid or ShrinePlaced or ShrineDeclined => EventKind.Setup,
        TurnStarted or TurnEnded or PhaseEntered or InitiativeDetermined or FirstActivationChosen or MoraleOrderSet => EventKind.Turn,
        ActivationStarted or ActivationEnded or TalismanUsed or BaseTurned => EventKind.Activation,
        ModelMoved or DashDeclared or DashRolled or SideClimbDeclared or SideClimbRolled or RetreatDeclared or RetreatResolved or MineRevealed or MineDefuseRolled or MoveResumed or ModelsMoved => EventKind.Movement,
        SpellCast or ReactionDeclined => EventKind.Status,
        InjuryRollMade => EventKind.Shooting,
        RangedAttackDeclared or ShootingTargetRolled or FriendlyTargetChosen or RangedSuccessRolled or BlastHit or AttackScattered or ScatterMissed
            or PointAttackDeclared or PointSuccessRolled or PointBlastHit or BodyguardAnswered => EventKind.Shooting,
        ChargeDeclared or ChargeRolled or ChargeMoved => EventKind.Charge,
        MeleeAttackDeclared or MeleeSuccessRolled or FreeFightsDeclined => EventKind.Fight,
        InjuryRolled or MarkersSpent or MineExploded or BloodMarkersPlaced or MartyrdomTriggered => EventKind.Injury,
        TreatDeclared or TreatRolled or SelfActionDeclared or SelfActionRolled or ModelStoodUp or MoraleCheckRolled or WarbandShaken or WarbandRecovered or WarbandFled => EventKind.Status,
        DecisionRequested => EventKind.Decision,
        ObjectivesScored or TrenchesScored or GloriousDeedCompleted => EventKind.Scoring,
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
        TreatRolled t => Faces(t.Check.Dice, t.Check.NetDice),
        MineDefuseRolled d => Faces(d.Check.Dice, d.Check.NetDice),
        SelfActionRolled s => Faces(s.Check.Dice, s.Check.NetDice),
        SideClimbRolled c => Faces(c.Check.Dice, c.Check.NetDice),
        RangedSuccessRolled r => Faces(r.Check.Dice, r.Check.NetDice),
        MeleeSuccessRolled m => Faces(m.Check.Dice, m.Check.NetDice),
        MoraleCheckRolled m => Faces(m.Check.Dice, m.Check.NetDice),
        InjuryRolled i => Faces(i.Injury.Dice, i.Injury.NetDice) + (i.Injury.Modifier == 0 ? "" : $", modifier {i.Injury.Modifier:+0;-0}"),
        MineExploded x => Faces(x.Injury.Dice, x.Injury.NetDice) + (x.Injury.Modifier == 0 ? "" : $", modifier {x.Injury.Modifier:+0;-0}"),
        ChargeRolled c => c.Dropped == 0 ? $"Dice: D6 {c.Die}" : $"Dice: 2D6 {c.Die}, {c.Dropped} (highest kept)",
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
