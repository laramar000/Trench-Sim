using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// One die as the server rolled it: its face, and whether it counted (bonus or penalty dice drop the others).
public sealed record DieFace(int Value, bool Kept);

public enum DiceCue
{
    None,
    NewAction,
    EndAction,
    OwnBox,
}

// One event's part in the dice display: a roll, a cue, or the result that ends a charge.
public sealed record DiceStep(DiceRoll? Roll, DiceCue Cue, string? FollowUp = null, RollVerdict FollowUpVerdict = RollVerdict.Neutral);

public enum RollVerdict
{
    Pass,
    Fail,
    Critical,
    Neutral,
}

// One roll as shown on screen (visible dice rolls, QoL stage F): who rolled, what for, every die the server rolled,
// what the rules count as passing, and the result. Model is null for a warband's roll (morale, roll-offs).
// Result starts with a symbol (✓, ✓✓, ✗ or •) so colour is never the only cue.
public sealed record DiceRoll(ModelId? Model, string Title, IReadOnlyList<DieFace> Dice, string Rule, string Result, RollVerdict Verdict)
{
    public bool Charge => Title.EndsWith(": charge bonus", StringComparison.Ordinal);

    // The other model in the roll (the target, or the attacker of an injury), which the dice must not cover either.
    public ModelId? Other { get; init; }

    public IReadOnlyList<int> Faces => Dice.Select(d => d.Value).ToList();
}

// Reads a roll only from the server's event: the faces are the event's own dice (kept and dropped as recorded), and
// the result comes from the rules' own Outcome and Classify. The client rolls nothing; events without dice give null.
public static class DiceRolls
{
    public static DiceRoll? Of(GameEvent e, MatchSetup? setup = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        ModelId? other = e switch
        {
            RangedSuccessRolled r => r.Target,
            MeleeSuccessRolled m => m.Target,
            InjuryRolled i => i.Attacker,
            ChargeRolled c => c.Target,
            ShootingTargetRolled t => t.Target,
            _ => null,
        };
        return Roll(e, setup) is { } roll ? roll with { Other = other } : null;
    }

    private static DiceRoll? Roll(GameEvent e, MatchSetup? setup)
    {
        return e switch
        {
            DashRolled d => Success(d.Model, $"{Id(d.Model)} DASHES: Risky roll", d.Check, setup, "DASH", "DASH FAILED"),
            TreatRolled t => Success(t.Model, $"{Id(t.Model)} TREATS {Id(t.Target)}: Risky roll", t.Check, setup, "TREATED", "TREAT FAILED"),
            SelfActionRolled s => Success(s.Model, $"{Id(s.Model)} {Words.SelfActionVerb(s.Action)}: {(s.Check.Risky ? "Risky roll" : "Success Roll")}", s.Check, setup, Words.SelfActionVerdicts(s.Action).Pass, Words.SelfActionVerdicts(s.Action).Fail),
            SideClimbRolled c => Success(c.Model, $"{Id(c.Model)} CLIMBS THE SIDE: Risky roll", c.Check, setup, "CLIMB", "CLIMB FAILED"),
            RangedSuccessRolled r => Success(r.Attacker, $"{Id(r.Attacker)} SHOOTS {Id(r.Target)}: hit roll", r.Check, setup, "HIT", "MISSED"),
            MeleeSuccessRolled m => Success(m.Attacker, $"{Id(m.Attacker)} FIGHTS {Id(m.Target)}: hit roll", m.Check, setup, "HIT", "MISSED"),
            MoraleCheckRolled m => Success(null, $"{Words.Seat(m.Seat)}: morale check", m.Check, setup, "HOLDS", "FAILS"),
            InjuryRolled i => Injury(i),
            ChargeRolled c => new(c.Model, $"{Id(c.Model)} CHARGES {Id(c.Target)}: charge bonus", c.Dropped == 0 ? [new(c.Die, true)] : [new(c.Die, true), new(c.Dropped, false)],
                c.Dropped == 0 ? "1 die, added to the charge move" : "2 dice, the highest added to the charge move (Shock Charge)",
                $"• +{c.Die}\"", RollVerdict.Neutral),
            ShootingTargetRolled t => new(t.Attacker, $"{Id(t.Attacker)} SHOOTS INTO MELEE: target roll", [new(t.Die, true)],
                "1 die: 1-3 a friend is the target, 4-6 the enemy may be",
                t.HitsFriend ? $"✗ FRIENDLY FIRE {t.Die}" : $"✓ ENEMY TARGET {t.Die}", t.HitsFriend ? RollVerdict.Fail : RollVerdict.Pass),
            RollOffResolved r => RollOff(r),
            _ => null,
        };
    }

    // What an event means for the dice on screen (stage F2): a roll to show, the start or end of an action (so the rolls
    // of one action share a box and the next action starts a new one), or the end of a charge. Null for the rest.
    // setup (optional): the match's setup, so a roll can name what a model's profile changes (the Crucifix's RISKY line).
    public static DiceStep? Step(GameEvent e, MatchSetup? setup = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Of(e, setup) is { } roll)
        {
            return new(roll, e is MoraleCheckRolled or RollOffResolved ? DiceCue.OwnBox : DiceCue.None);
        }
        if (FollowUp(e) is { } follow)
        {
            return new(null, DiceCue.None, follow.Result, follow.Verdict);
        }
        return e switch
        {
            DashDeclared or TreatDeclared or SelfActionDeclared or MartyrdomTriggered or SideClimbDeclared or RangedAttackDeclared or MeleeAttackDeclared or ChargeDeclared or RetreatDeclared => new(null, DiceCue.NewAction),
            ActivationEnded or TurnEnded or MatchEnded => new(null, DiceCue.EndAction),
            _ => null,
        };
    }

    // The end of a charge whose bonus was shown: whether it reached its target ("✓ REACHED" or "✗ FAILED").
    public static (string Result, RollVerdict Verdict)? FollowUp(GameEvent e) => e switch
    {
        ChargeMoved { Reached: true } => ("✓ REACHED", RollVerdict.Pass),
        ChargeMoved => ("✗ FAILED", RollVerdict.Fail),
        _ => null,
    };

    // "2+ Minor Hit, 7+ Down, 9+ Out of Action": the lowest total of each injury result, asked of the rules' own
    // Classify (the rules keep these numbers only inside it), so the shown bands can never differ from the rules.
    public static string InjuryBands()
    {
        var parts = new List<string>();
        foreach (var outcome in new[] { InjuryOutcome.MinorHit, InjuryOutcome.Down, InjuryOutcome.OutOfAction })
        {
            var lowest = Enumerable.Range(-20, 60).First(total => InjuryRoll.Classify(total) >= outcome);
            parts.Add($"{lowest}+ {InjuryWord(outcome)}");
        }
        return string.Join(", ", parts);
    }

    private static DiceRoll Success(ModelId? model, string title, SuccessRollResult check, MatchSetup? setup, string pass, string fail)
    {
        var outcome = check.Outcome;
        var result = outcome switch
        {
            SuccessOutcome.CriticalSuccess => $"✓✓ CRITICAL {pass} {check.Dice.Total}",
            SuccessOutcome.Success => $"✓ {pass} {check.Dice.Total}",
            _ => $"✗ {fail} {check.Dice.Total}",
        };
        var verdict = outcome switch
        {
            SuccessOutcome.CriticalSuccess => RollVerdict.Critical,
            SuccessOutcome.Success => RollVerdict.Pass,
            _ => RollVerdict.Fail,
        };
        var rule = $"{Kept(check.Dice, check.NetDice)}; {SuccessRoll.SuccessFrom}+ passes, {SuccessRoll.CriticalFrom}+ critical"
            + (!check.Risky ? ""
                // Roster stage R5d (U108): the Pontiff's Crucifix keeps the activation going after a failed RISKY roll.
                : model is { } m && setup?.ProfileOf(m)?.KeepsGoingAfterRisky == true ? "; RISKY: a fail does not end the activation (Crucifix)"
                // Roster stage R5k (U116): unless the model uses a Talisman.
                : model is { } t && setup?.ProfileOf(t)?.Talismans > 0 ? "; RISKY: a fail ends the activation unless a Talisman is used"
                : "; RISKY: a fail ends the activation");
        return new(model, title, Faces(check.Dice), rule, result, verdict);
    }

    private static DiceRoll Injury(InjuryRolled i)
    {
        var injury = i.Injury;
        // Roster stage R5b: TOUGH turns the first Out of Action into Down (U106).
        var word = InjuryWord(i.Result).ToUpperInvariant() + (i.Tough ? " (TOUGH, rolled Out of Action)" : "") + (i.Spared ? " (MEMENTO MORI, rolled Out of Action)" : "");
        var sum = injury.Modifier == 0 ? "" : $" ({injury.Dice.Total}{injury.Modifier:+0;-0})";
        // Roster stage R4a: with the extra markers of SHRAPNEL, GAS and FIRE (U101).
        // Roster stage R5e (U109): less the first one, which a Sacrificial Lamb's blood keeps off.
        var count = i.PlacedMarkers;
        var blood = (count > 0 ? $", +{count} Blood Marker{(count == 1 ? "" : "s")}{(i.ExtraMarkers > 0 ? $" ({i.ExtraMarkers} from SHRAPNEL, GAS or FIRE)" : "")}" : "")
            + (i.Warded ? ", 1 Blood Marker kept off (Sacrificial Lamb)" : "");
        var verdict = i.Result is InjuryOutcome.Down or InjuryOutcome.OutOfAction ? RollVerdict.Pass : RollVerdict.Neutral;
        var mark = verdict == RollVerdict.Pass ? "✓" : "•";
        var modifier = injury.Modifier == 0 ? "" : $", modifier {injury.Modifier:+0;-0}";
        return new(i.Target, injury.AllFour && !i.Bloodbath ? $"{Id(i.Target)} IS CAUGHT BY ITS OWN DEVICE: 4D6 added together" : $"{Id(i.Attacker)} INJURES {Id(i.Target)}: {(i.Bloodbath ? "Bloodbath Roll" : "injury roll")}", Faces(injury.Dice),
            $"{Kept(injury.Dice, injury.NetDice)}{modifier}; {InjuryBands()}", $"{mark} {word} {injury.Total}{sum}{blood}", verdict);
    }

    private static DiceRoll RollOff(RollOffResolved r)
    {
        var last = r.Rounds[^1];
        var dice = r.Rounds.SelectMany((round, i) => new[] { new DieFace(round.SeatOne, i == r.Rounds.Length - 1), new DieFace(round.SeatTwo, i == r.Rounds.Length - 1) }).ToList();
        var what = r.Purpose switch
        {
            RollOffPurpose.DeploymentZone => "deployment zone",
            RollOffPurpose.DeploymentOrder => "deployment order",
            _ => "initiative",
        };
        var ties = r.Rounds.Length - 1;
        var rule = "1 die each, Seat One then Seat Two; the higher wins" + (ties > 0 ? $"; {ties} tie{(ties == 1 ? "" : "s")} rolled again" : "");
        var winner = r.Winner == Seat.One ? last.SeatOne : last.SeatTwo;
        var loser = r.Winner == Seat.One ? last.SeatTwo : last.SeatOne;
        return new(null, $"Roll-off: {what}", dice, rule, $"• {Words.Seat(r.Winner).ToUpperInvariant()} WINS {winner}-{loser}", RollVerdict.Neutral);
    }

    // Every rolled die in the server's order, marked kept when it is one of the dice the record kept.
    private static List<DieFace> Faces(RollRecord record)
    {
        var left = record.Kept.GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
        var faces = new List<DieFace>();
        foreach (var die in record.Rolled)
        {
            var kept = left.TryGetValue(die, out var n) && n > 0;
            if (kept)
            {
                left[die] = n - 1;
            }
            faces.Add(new(die, kept));
        }
        return faces;
    }

    private static string Kept(RollRecord record, int netDice) => netDice switch
    {
        > 0 => $"{record.Rolled.Count} dice (+{netDice} DICE), kept the highest {record.Kept.Count}",
        < 0 => $"{record.Rolled.Count} dice ({netDice} DICE), kept the lowest {record.Kept.Count}",
        _ => $"{record.Rolled.Count} dice",
    };

    private static string InjuryWord(InjuryOutcome outcome) => outcome switch
    {
        InjuryOutcome.NoEffect => "No effect",
        InjuryOutcome.MinorHit => "Minor Hit",
        InjuryOutcome.Down => "Down",
        _ => "Out of Action",
    };

    private static string Id(ModelId id) => UnitPanel.ShortId(id);
}
