using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// A stable fingerprint of a match state. The state is written out as plain text in a fixed field order
// (models sorted by id, enums by name, integers in invariant form), and the hash is SHA-256 of that
// text. Two machines with the same state always get the same text and the same hash, so a client can
// compare hashes after every event. Any change to the layout must bump Version.
public static class StateHash
{
    public const int Version = 5;

    public static string Canonical(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sb = new StringBuilder();
        Line(sb, "trench-state", Version);

        if (state.Setup is { } setup)
        {
            Line(sb, "rules", setup.RulesVersion);
            Line(sb, "scenario", setup.ScenarioId);
            Line(sb, "turnLimit", setup.TurnLimit);
            Line(sb, "initiativeCount", setup.Profile.InitiativeCount);
            Line(sb, "moraleOrder", setup.Profile.MoraleOrder);
            Line(sb, "zones", string.Join(",", setup.ZoneIds));
            if (setup.Scenario is { } scenario)
            {
                WriteScenario(sb, scenario);
                foreach (var m in setup.Models)
                {
                    var prof = m.Profile!;
                    Line(sb, "profile", $"{m.Id.Value},move={prof.Movement.Thousandths},radius={prof.BaseRadius.Thousandths},range={prof.RangedRange.Thousandths},weapon={prof.RangedInjuryModifier},armour={prof.Armour},melee={Bool(prof.HasMeleeWeapon)},meleeMod={prof.MeleeInjuryModifier}{(prof.RangedAlsoMelee ? ",rangedAlsoMelee=yes" : "")}{ProfileDice(prof)}");
                }
            }
        }

        Line(sb, "phase", state.Phase);
        Line(sb, "turn", state.Turn);
        Line(sb, "turnEnded", state.TurnEnded);
        Line(sb, "deploymentFirst", state.DeploymentFirst);
        Line(sb, "lastDeployer", state.LastDeployer);
        Line(sb, "initiativeHolder", state.InitiativeHolder);
        Line(sb, "firstActivator", state.FirstActivator);
        Line(sb, "lastActivator", state.LastActivator);
        Line(sb, "activeModel", state.ActiveModel?.Value);
        Line(sb, "moraleFirst", state.MoraleFirst);
        Line(sb, "pending", state.Pending is { } p ? $"{p.Kind}:{p.Seat}" : null);
        Line(sb, "actions", (int)state.Actions);
        Line(sb, "movementHalved", state.MovementHalved);
        Line(sb, "scoredTurn", state.ScoredTurn);
        Line(sb, "inProgress", state.InProgress is { } r
            ? $"{r.Stage}:{r.Actor.Value}:{r.Target?.Value ?? "-"}:{r.BonusDice}:{r.PenaltyDice}:{Bool(r.Critical)}:{Bool(r.MarkersAsked)}:{r.Kind}:{Bool(r.FreeFight)}:{Bool(r.FriendlyFire)}:{r.ChargeDie}:{(r.DashPath.IsDefaultOrEmpty ? "-" : Path(r.DashPath))}{(r.ClimbAction == ActionsTaken.None ? "" : $":climb={(int)r.ClimbAction}")}{(r.InjuryDiceLater == 0 ? "" : $":injuryLater={r.InjuryDiceLater}")}{(r.Weapon == 0 ? "" : $":weapon={r.Weapon}")}{(r.OffHand ? ":offHand" : "")}{(r.Aimed is { } aimed ? $":aimed={aimed.Value}" : "")}{(r.BlastQueue.IsDefaultOrEmpty ? "" : $":blast={string.Join(";", r.BlastQueue.Select(q => q.Value))}")}{(r.ScatterInches == 0 ? "" : $":scatter={r.ScatterInches}")}{(r.BlastAt is { } landed ? $":at={landed.X.Thousandths},{landed.Y.Thousandths}" : "")}{(r.RiskyEnds ? ":riskyEnds" : "")}{(r.TreatStandUp ? ":standUp" : "")}{(r.Martyrdom ? $":martyr={string.Join(";", r.MartyrClose.Select(c => c.Value))}" : "")}{(r.Stage == RollStage.SelfRoll ? $":self={r.SelfAction}" : "")}"
            : null);
        Line(sb, "retreat", state.Retreat is { } t
            ? $"{t.Model.Value}:{Path(t.Path)}:{string.Join(";", t.Fought.Select(f => f.Value))}:{Bool(t.Closed)}"
            : null);
        Line(sb, "outcome", state.Outcome is { } o ? $"{(o.Winner?.ToString() ?? "-")}:{o.Reason}" : null);
        Line(sb, "rollOff", state.RollOff is { } ro ? $"{ro.Purpose}:{ro.Winner}" : null);
        Line(sb, "mustEndActivation", state.MustEndActivation);
        if (state.ReloadEnds)
        {
            Line(sb, "reloadEnds", true);
        }
        // Roster stage R3: only when set, so every match without second weapons, CLEAVE or BLOCK hashes as before.
        if (state.ShotWeapon != 0)
        {
            Line(sb, "shotWeapon", state.ShotWeapon);
        }
        if (state.AttacksLeft is { } left)
        {
            Line(sb, "attacksLeft", $"{left.Attacker.Value}:{Bool(left.Free)}:{string.Join(";", left.Next.Select(n => $"{n.Weapon}{(n.OffHand ? "o" : "")}"))}");
        }
        if (!state.ChargedThisTurn.IsDefaultOrEmpty)
        {
            Line(sb, "chargedThisTurn", string.Join(";", state.ChargedThisTurn.Select(m => m.Value)));
        }
        // Roster stage R6b: only when set.
        if (state.Aiming)
        {
            Line(sb, "aiming", true);
        }
        if (state.CounterCharging)
        {
            Line(sb, "counterCharging", true);
        }
        if (!state.MarkersOwed.IsDefaultOrEmpty)
        {
            Line(sb, "markersOwed", string.Join(";", state.MarkersOwed.Select(o => $"{o.Source.Value}>{string.Join(",", o.Targets.Select(t => t.Value))}")));
        }
        // Trench Warfare only (trench-maps stage S6), so every other match hashes exactly as before.
        if (state.Attacker is { } attacker)
        {
            Line(sb, "attacker", attacker);
        }
        if (state.LastTurn is { } last)
        {
            Line(sb, "lastTurn", last);
        }
        // Mine Markers (Trench Warfare, p. 162), once set up. The duds are secret and never written, so the attacker's view
        // of the match, which does not know them, hashes the same as the server's.
        if (state.MinesLaid)
        {
            Line(sb, "mines", string.Join(";", state.Mines.Select(m => $"{m.Id}@{Point(m.At)}")));
        }
        if (state.MineStop is { } stop)
        {
            Line(sb, "mineStop", $"{stop.Model.Value}:{Path(stop.Path)}:{string.Join(";", stop.Stops.Select(p => $"{p.Marker}@{Point(p.At)}#{p.Next}"))}:{stop.Index}:{Bool(stop.Revealed)}:{Bool(stop.Live)}:{Bool(stop.EndsActivation)}");
        }

        // Glorious Deeds (U96, hash version 5): written only once there is something to say.
        var d = state.Deeds;
        static string Deed(CompletedDeed c) => $"{c.Deed}:{c.Seat}:{c.Model?.Value ?? "-"}";
        static string Ids(IEnumerable<ModelId> ids) => string.Join(";", ids.Select(i => i.Value));
        void Optional(string key, bool present, object value)
        {
            if (present)
            {
                Line(sb, key, value);
            }
        }
        Optional("deeds.completed", !d.Completed.IsEmpty, string.Join(",", d.Completed.Select(Deed)));
        Optional("deeds.owed", !d.Owed.IsEmpty, string.Join(",", d.Owed.Select(Deed)));
        Optional("deeds.longRange", d.ShotAtLongRange, true);
        Optional("deeds.meleeTakedowns", !d.MeleeTakedowns.IsEmpty, Ids(d.MeleeTakedowns));
        Optional("deeds.beganDown", d.ActiveBeganDown, true);
        Optional("deeds.retreatingFrom", !d.RetreatingFrom.IsEmpty, Ids(d.RetreatingFrom));
        Optional("deeds.retreatedFrom", !d.RetreatedFrom.IsEmpty, Ids(d.RetreatedFrom));
        Optional("deeds.chargedInTrench", d.ChargedInTrench is not null, d.ChargedInTrench?.Value ?? "-");
        Optional("deeds.mineSurvivals", !d.MineSurvivals.IsEmpty, Ids(d.MineSurvivals));

        foreach (var seat in new[] { state.One, state.Two })
        {
            Line(sb, $"seat.{seat.Seat}", string.Join(
                ",",
                $"zone={seat.ZoneId ?? "-"}",
                $"shaken={Bool(seat.Shaken)}",
                $"fled={Bool(seat.Fled)}",
                $"checked={Bool(seat.MoraleChecked)}",
                $"outcome={seat.MoraleOutcome?.ToString() ?? "-"}",
                $"vp={seat.VictoryPoints}",
                $"mustFlee={Bool(seat.MustFlee)}"));
        }

        foreach (var m in state.Models.OrderBy(m => m.Id.Value, StringComparer.Ordinal))
        {
            Line(sb, "model", string.Join(
                ",",
                m.Id.Value,
                m.Seat,
                m.Status,
                $"deployed={Bool(m.Deployed)}",
                $"activated={Bool(m.ActivatedThisTurn)}",
                $"at={(m.Position is { } at ? Point(at) : "-")}",
                $"markers={m.BloodMarkers}") + (m.ToughSpent ? ",tough=spent" : "") + (m.WardSpent ? ",ward=spent" : "") + (m.DugInAt is { } dug ? $",dugIn={Point(dug)}" : "") + (m.MartyrdomSpent ? ",martyrdom=spent" : "") + (m.TalismansUsed == 0 ? "" : $",talismansUsed={m.TalismansUsed}"));
        }
        return sb.ToString();
    }

    public static string Compute(MatchState state) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(state))));

    private static string Point(Point2 p) => $"{p.X.Thousandths}/{p.Y.Thousandths}";

    private static string Path(IEnumerable<Point2> path) => string.Join(";", path.Select(Point));

    // One rectangle as before; joined parts (trench-maps stage S2) in their fixed order, joined by "+".
    private static string Rectangle(Footprint f) => string.Join("+", f.Parts.Select(p => $"{Point(p.Min)}-{Point(p.Max)}"));

    // Terrain and zones are written in id order (the Battlefield already sorts them).
    private static void WriteScenario(StringBuilder sb, Scenario scenario)
    {
        var field = scenario.Battlefield;
        Line(sb, "table", $"{field.Width.Thousandths}x{field.Height.Thousandths}");
        foreach (var z in field.Zones)
        {
            Line(sb, "zone", $"{z.ZoneId},{Rectangle(z.Area)}");
        }
        foreach (var t in field.Terrain)
        {
            Line(sb, "terrain", $"{t.Id},{Rectangle(t.Footprint)},{t.Profile},{(int)t.Kind},{t.Surface},walkable={Bool(t.IsWalkable)}");
            // Towers only (towers proposal, T1), so every battlefield without one hashes exactly as before.
            if (t.Climb is { } climb)
            {
                Line(sb, "climb", $"{t.Id},height={climb.Height.Thousandths},{string.Join(";", climb.Ladders.Select(l => $"{l.Id}@{Point(l.At)}"))}");
            }
            // Trenches only (trench-maps stage S3), likewise.
            if (t.Depth is { } depth)
            {
                Line(sb, "trench", $"{t.Id},depth={depth.Thousandths}");
            }
        }
        Line(sb, "objectives", string.Join(",", scenario.ObjectiveIds));
    }

    private static string Bool(bool value) => value ? "1" : "0";

    // Roster stage R1: a profile's dice, written only when not zero so every existing setup hashes as before.
    private static string ProfileDice(ModelProfile p)
    {
        var parts = new[]
        {
            ("rangedDice", p.RangedDice), ("meleeDice", p.MeleeDice), ("rangedWeaponDice", p.RangedWeaponDice),
            ("rangedWeaponInjuryDice", p.RangedWeaponInjuryDice), ("meleeWeaponDice", p.MeleeWeaponDice),
            ("meleeWeaponInjuryDice", p.MeleeWeaponInjuryDice),
        };
        return string.Concat(parts.Where(x => x.Item2 != 0).Select(x => $",{x.Item1}={x.Item2}"))
            + (p.RangedWeaponKeywords == WeaponKeywords.None ? "" : $",rangedWeaponKeywords={p.RangedWeaponKeywords}")
            + (p.MeleeWeaponKeywords == WeaponKeywords.None ? "" : $",meleeWeaponKeywords={p.MeleeWeaponKeywords}")
            // Roster stage R3: second weapons, CLEAVE, BLOCK and IMPERVIOUS, only when present.
            + (p.MeleeWeaponCleave == 0 ? "" : $",meleeWeaponCleave={p.MeleeWeaponCleave}")
            + (p.Block ? ",block=yes" : "")
            + (p.ImperviousArmour == 0 ? "" : $",imperviousArmour={p.ImperviousArmour}")
            + (p.SecondRangedWeapon is { } r ? $",secondRanged={WeaponText(r)}" : "")
            + (p.SecondMeleeWeapon is { } m ? $",secondMelee={WeaponText(m)}" : "")
            // Roster stage R4a: grenades and NEGATE, only when present.
            + (p.Grenade is { } g ? $",grenade={WeaponText(g)}" : "")
            + (p.Negates == WeaponKeywords.None ? "" : $",negates={p.Negates}")
            // Roster stage R4b: BLAST, only when present.
            + (p.RangedWeaponBlast == 0 ? "" : $",blast={p.RangedWeaponBlast}")
            // Roster stage R5b: FEAR, NEGATE FEAR, TOUGH, NEGATE HEAVY and LEADER, only when present.
            + (p.Traits == ModelTraits.None ? "" : $",traits={p.Traits}")
            // Roster stage R5c: battlekit that changes Injury Rolls, only when present.
            + (p.InjuryDiceTaken == 0 ? "" : $",injuryDiceTaken={p.InjuryDiceTaken}")
            + (p.MeleeInjuryBonus == 0 ? "" : $",meleeInjuryBonus={p.MeleeInjuryBonus}")
            // Roster stage R5d: battlekit for Risky rolls and Cover, only when present.
            + (p.KeepsGoingAfterRisky ? ",keepsGoing=yes" : "")
            + (p.ClimbDice == 0 ? "" : $",climbDice={p.ClimbDice}")
            + (p.Fanfare ? ",fanfare=yes" : "")
            + (p.BlendsIn ? ",blendsIn=yes" : "")
            // Roster stage R5e: the Urn's aura and the Lamb's ward, only when present.
            + (p.SinisterWhispers ? ",whispers=yes" : "")
            + (p.WardedByBlood ? ",warded=yes" : "")
            // Roster stage R5f: the COVER keyword, only when present.
            + (p.HasCover ? ",cover=yes" : "")
            + (p.DigsIn ? ",digsIn=yes" : "")
            // Roster stage R5g: BLOOD MARKERS on nearby enemies, only when present.
            + (p.BurnsAtActivationEnd ? ",burnsAtEnd=yes" : "")
            + (p.BurnsWhenTakenOut ? ",burnsWhenOut=yes" : "")
            // Roster stage R5h: the Treat ACTION, only when present.
            + (p.Treats ? ",treats=yes" : "")
            + (p.HealingWaters ? ",healingWaters=yes" : "")
            // Roster stage R5i: the Grind to Dust ACTION, only when present.
            + (p.GrindsToDust ? ",grinds=yes" : "")
            // Roster stage R5j: the Martyrdom Device, only when present.
            + (p.Martyrdom ? ",martyrdom=yes" : "")
            // Roster stage R5k: Talismans, only when present.
            + (p.Talismans == 0 ? "" : $",talismans={p.Talismans}")
            // Roster stage R6a: New Antioch abilities, only when present.
            + (p.ShotsIgnoreMarkers ? ",shotsIgnoreMarkers=yes" : "")
            + (p.ExpertMedic ? ",expertMedic=yes" : "")
            + (p.FinishesTheFallen ? ",finishesTheFallen=yes" : "")
            + (p.ExtraChargeDie ? ",extraChargeDie=yes" : "")
            + (p.InspiresFaithful ? ",inspires=yes" : "")
            // Roster stage R6b: Aim and Fortify ACTIONS, only when present.
            + (p.Aims ? ",aims=yes" : "")
            + (p.Fortifies ? ",fortifies=yes" : "")
            // Roster stage R6c: number-changing abilities, only when present.
            + (p.Agile ? ",agile=yes" : "")
            + (p.SwiftDash ? ",swiftDash=yes" : "")
            + (p.QuickToRise ? ",quickToRise=yes" : "")
            + (p.UndeadFortitude ? ",undeadFortitude=yes" : "")
            + (p.AbioticLife ? ",abioticLife=yes" : "")
            + (p.Stealthy ? ",stealthy=yes" : "")
            + (p.HuntsTheTough ? ",huntsTheTough=yes" : "")
            + (p.CounterCharge ? ",counterCharge=yes" : "")
            + (p.MementoMori ? ",mementoMori=yes" : "")
            // Roster stage R6d: Trample and Overwhelming Horde, only when present.
            + (p.Tramples ? ",tramples=yes" : "")
            + (p.Horde ? ",horde=yes" : "");
    }

    private static string WeaponText(Weapon w) =>
        $"{w.Range.Thousandths}/{w.InjuryModifier}/{w.Dice}/{w.InjuryDice}/{w.Keywords}/{Bool(w.AlsoMelee)}/{w.Cleave}{(w.Blast == 0 ? "" : $"/blast={w.Blast}")}";

    private static void Line(StringBuilder sb, string key, object? value)
    {
        sb.Append(key).Append('=');
        sb.Append(value switch
        {
            null => "-",
            bool b => Bool(b),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        });
        sb.Append('\n');
    }
}
