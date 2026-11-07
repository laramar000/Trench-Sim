using Rules.Core.Events;
using Rules.Core.Dice;
using Rules.Core.State;

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
        ModelStoodUp s => $"  {s.Model} stands up{(s.Quick ? " (Movement not halved: Frightening Speed)" : " (Movement halved)")}.",
        ModelMoved m => $"  {m.Model} {(m.Kind == MoveKind.Dash ? "dashes" : "moves")} to {m.Path[^1]} (cost {m.Cost}).",
        TreatRolled t => $"  {t.Model} Treat roll {t.Check.Dice.Total}: {t.Check.Outcome}{(t.Treated ? t.Choice == TreatChoice.StandUp ? $"; {t.Target} stands up." : $"; 1 Blood Marker removed from {t.Target}." : t.Check.Outcome == SuccessOutcome.Failure ? "." : $"; {t.Target} has no Blood Marker left to remove.")}",
        SelfActionRolled s => $"  {s.Model} {SelfAction(s.Action)} roll {s.Check.Dice.Total}: {s.Check.Outcome}{(s.Succeeded ? $"; {SelfActionResult(s)}." : ".")}",
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
        InjuryRolled i => $"  Injury Roll on {i.Target}: {i.Injury.Total} ({(i.Bloodbath ? $"Bloodbath Roll, {i.Injury.Dice.Kept.Count} dice added, {i.Injury.NetDice:+0;-0;0} injury dice" : i.Injury.AllFour ? "4D6 added together" : $"{i.Injury.NetDice:+0;-0;0} injury dice")}): {i.Injury.Outcome}.{(i.Tough ? " TOUGH: Down instead." : "")}{(i.Spared ? " Memento Mori: No Effect instead." : "")}",
        ActivationEnded a => $"  {a.Model} ends its activation ({a.Reason}).",
        MoraleCheckRolled m => $"{m.Seat} Morale Check ({m.ModelsDownOrOut} of {m.WarbandSize} down or out{(m.Check.NetDice > 0 ? ", +1 DICE from its LEADER" : "")}): {m.Check.Outcome}.",
        WarbandShaken w => $"{w.Seat} is Shaken.",
        WarbandRecovered w => $"{w.Seat} recovers.",
        WarbandFled w => $"{w.Seat} flees ({w.Reason}).",
        ObjectivesScored o => $"Turn {o.Turn} scoring: {string.Join(", ", o.Results.Where(r => r.Controller is not null).Select(r => $"{r.ObjectiveId} to {r.Controller}"))}{(o.Results.All(r => r.Controller is null) ? "nobody controls an Objective" : "")}.",
        AttackerChosen a => $"{a.Attacker} attacks; {a.Attacker.Opponent()} defends.",
        TrenchesScored t => $"Turn {t.Turn} trench scoring: {string.Join(", ", t.Results.Select(r => $"{r.TrenchId} defender +{r.DefenderPoints}, attacker +{r.AttackerPoints}"))}.",
        BaseTurned t => $"  {t.Model} turns its base to lie along {t.Axis}.",
        BodyguardAnswered { Guard: { } g } b => $"  {g} takes the hit meant for {b.Target} (Bodyguard).",
        BodyguardAnswered b => $"  {b.Target} keeps the hit.",
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
        MineExploded x => $"  {x.Marker} explodes under {x.Model}: Injury Roll {x.Injury.Total}: {x.Injury.Outcome}{(x.Tough ? " (TOUGH: Down instead)" : "")}{(x.Spared ? " (Memento Mori: No Effect instead)" : "")}{(x.Negated ? ", and its NEGATE SHRAPNEL stops the extra Blood Marker." : ", and SHRAPNEL adds a Blood Marker.")}",
        MoveResumed m => m.Path.IsEmpty ? $"  {m.Model} carries on." : $"  {m.Model} carries on to {m.Path[^1]}.",
        MatchEnded m => $"MATCH ENDED: {(m.Winner is { } w ? $"{w} wins" : "draw")} ({m.Reason}).",
        _ => null,
    };

    // Roster stage R6b/R6e: the same words as the client's log (Client.Core Words.SelfAction, SelfActionResult).
    private static string SelfAction(SelfActionKind action) => action switch
    {
        SelfActionKind.Aim => "Aim",
        SelfActionKind.Fortify => "Fortify",
        SelfActionKind.Hide => "Hide",
        SelfActionKind.EnforcedOrthodoxy => "Enforced Orthodoxy",
        _ => "Laying on of Hands",
    };

    private static string SelfActionResult(SelfActionRolled rolled) => rolled.Action switch
    {
        SelfActionKind.Aim => "its Ranged Attacks get +2 DICE this activation",
        SelfActionKind.Fortify => "it has COVER until it moves",
        SelfActionKind.Hide => "it cannot be shot or charged until it acts or an enemy comes within 1.5\"",
        SelfActionKind.EnforcedOrthodoxy => rolled.StoodUp.IsEmpty ? "no Down friend within 8\" to stand up" : $"{string.Join(", ", rolled.StoodUp)} stand{(rolled.StoodUp.Length == 1 ? "s" : "")} up",
        _ => rolled.Removed == 0 ? $"{rolled.Target} has no BLOOD MARKER left to remove" : $"{rolled.Removed} BLOOD MARKER{(rolled.Removed == 1 ? "" : "S")} removed from {rolled.Target}",
    };
}
