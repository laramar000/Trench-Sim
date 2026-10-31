using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// Proposal J, stage J1a: why a roll had the dice and modifier it had, and what one action came to, read only from the
// server's events and the match's setup. Each part is named from the event that caused it and sized by the rules' own
// functions (AttackDice, InjuryRoll.CombineModifiers); the client rolls nothing and decides nothing. When the named parts
// do not add up exactly to the server's numbers, the breakdown is not shown: Explained is false and only the server's
// totals are given.

// One reason a roll had more or fewer dice ("Long Range", -1), or one Injury modifier ("weapon", +1).
public sealed record ResolutionPart(string Label, int Value)
{
    public string Text(string unit) => $"{Value:+0;-0} {unit} {Label}".Replace("  ", " ", StringComparison.Ordinal);
}

public sealed record RollWhy(IReadOnlyList<ResolutionPart> Dice, int NetDice, IReadOnlyList<ResolutionPart> Modifiers, int? Modifier, bool Explained)
{
    public const string NotAvailable = "breakdown not available";

    // "-1 DICE Long Range · -1 DICE Cover (obj-2) · weapon +1, Armour -1"; empty when nothing changed the roll.
    public string Text
    {
        get
        {
            if (!Explained)
            {
                var totals = new List<string>();
                if (NetDice != 0)
                {
                    totals.Add($"{NetDice:+0;-0} DICE");
                }
                if (Modifier is { } m && m != 0)
                {
                    totals.Add($"modifier {m:+0;-0}");
                }
                return string.Join(" · ", totals.Append(NotAvailable));
            }
            var parts = Dice.Where(p => p.Value != 0).Select(p => p.Text("DICE")).ToList();
            var modifiers = Modifiers.Where(p => p.Value != 0).ToList();
            if (modifiers.Count > 0)
            {
                var sum = modifiers.Sum(p => p.Value);
                var capped = Modifier is { } total && total != sum ? $" (negatives capped: {total:+0;-0})" : "";
                parts.Add(string.Join(", ", modifiers.Select(p => $"{p.Label} {p.Value:+0;-0}")) + capped);
            }
            return string.Join(" · ", parts);
        }
    }
}

// One action that rolled dice (a Dash, a Ranged Attack, a Melee Attack, a Charge), as its events came: who did what to
// whom, each step as a short line, and how it ended. Outcome separates the roll ("HIT ✓ 9") from what it did ("injury 8
// → DOWN, +1 Blood Marker").
public sealed record ActionResolution(int FirstSeq, ModelId Actor, string Headline, IReadOnlyList<string> Steps, string Outcome);

public sealed class ResolutionLog
{
    private readonly Dictionary<int, RollWhy> _why = [];
    private readonly List<Builder> _actions = [];
    private Builder? _current;

    private sealed class Builder(int seq, ModelId actor, string headline, AttackKind? kind, ModelId? target)
    {
        public int Seq { get; } = seq;
        public ModelId Actor { get; } = actor;
        public string Headline { get; set; } = headline;
        public AttackKind? Kind { get; } = kind;
        public ModelId? Target { get; set; } = target;
        public List<ResolutionPart> Dice { get; } = [];
        public bool Critical { get; set; }
        public bool FreeFight { get; set; }
        public bool Climb { get; set; }
        public bool Failed { get; set; }
        public List<string> Steps { get; } = [];
        public List<string> Outcome { get; } = [];

        public ActionResolution Build() => new(Seq, Actor, Headline, [.. Steps], Outcome.Count == 0 ? "…" : string.Join(" → ", Outcome));
    }

    public void Clear()
    {
        _why.Clear();
        _actions.Clear();
        _owner.Clear();
        _current = null;
        _last = -1;
    }

    // The breakdown of the roll in the event with this number, or null for an event that is not a Success or Injury Roll.
    public RollWhy? WhyOf(int seq) => _why.GetValueOrDefault(seq);

    public IReadOnlyList<ActionResolution> Actions => [.. _actions.Select(a => a.Build())];

    // The action the event with this number belongs to, or null.
    public ActionResolution? ActionAt(int seq) => _owner.TryGetValue(seq, out var first) ? _actions.First(a => a.Seq == first).Build() : null;

    private readonly Dictionary<int, int> _owner = [];
    private int _last = -1;

    // setup: the match's setup (model profiles); events must come in the server's order, each once.
    public void Add(int seq, GameEvent e, MatchSetup? setup)
    {
        ArgumentNullException.ThrowIfNull(e);
        // An event the log already has (sent again on rejoining) changes nothing.
        if (seq <= _last)
        {
            return;
        }
        _last = seq;
        switch (e)
        {
            case DashDeclared d:
                Start(seq, d.Model, $"{Id(d.Model)} DASHES", null, null);
                break;
            case SideClimbDeclared c:
                // Towers stage T6: a Move or Dash that climbs a tower's side is its own action with its own roll.
                Start(seq, c.Model, $"{Id(c.Model)} CLIMBS A TOWER'S SIDE", null, null);
                _current!.Climb = true;
                break;
            case RangedAttackDeclared r:
                Start(seq, r.Attacker, $"{Id(r.Attacker)} SHOOTS {Id(r.Target)}", AttackKind.Ranged, r.Target);
                ShotParts(r.LongRange, r.CoverTerrainId, r.Elevated);
                break;
            case MeleeAttackDeclared m:
                Start(seq, m.Attacker, $"{Id(m.Attacker)} {(m.FreeFight ? "MAKES A FREE ATTACK ON" : "FIGHTS")} {Id(m.Target)}", AttackKind.Melee, m.Target);
                _current!.FreeFight = m.FreeFight;
                if (m.DefendedObstacleId is { } obstacle)
                {
                    _current!.Dice.Add(new($"Defended obstacle ({obstacle})", -AttackDice.FightPenalty(true)));
                }
                break;
            case ChargeDeclared c:
                Start(seq, c.Model, $"{Id(c.Model)} CHARGES {Id(c.Target)}", null, c.Target);
                break;
            case ActivationEnded or TurnEnded or MatchEnded or RetreatDeclared:
                _current = null;
                break;
        }
        if (_current is not { } action)
        {
            return;
        }
        _owner[seq] = action.Seq;
        switch (e)
        {
            case ShootingTargetRolled t:
                action.Steps.Add($"Into melee: target roll {t.Die} ({(t.HitsFriend ? "1-3, a friend must be the target" : "4-6, the enemy may be the target")})");
                if (t.HitsFriend)
                {
                    action.Outcome.Add($"target roll {t.Die}: FRIENDLY FIRE");
                }
                break;
            case FriendlyTargetChosen f:
                action.Target = f.Victim;
                action.Dice.Clear();
                ShotParts(f.LongRange, f.CoverTerrainId, f.Elevated);
                action.Steps.Add($"Friendly target: {Id(f.Victim)}");
                action.Outcome.Add($"shoots {Id(f.Victim)}");
                break;
            case MarkersSpent { Count: 0 } none:
                action.Steps.Add($"{Words.Seat(none.Spender)} spends no Blood Markers of {Id(none.Model)}");
                break;
            case MarkersSpent spent:
                var markers = $"{spent.Count} Blood Marker{(spent.Count == 1 ? "" : "s")}";
                if (spent.Purpose == MarkerSpendPurpose.ReduceSuccessRoll)
                {
                    action.Dice.Add(new($"{markers} of {Id(spent.Model)} spent by {Words.Seat(spent.Spender)}", -spent.Count));
                    action.Steps.Add($"{Words.Seat(spent.Spender)} spends {markers} of {Id(spent.Model)}: {-spent.Count} DICE on the Success Roll");
                }
                else
                {
                    action.Dice.Add(new($"{markers} of {Id(spent.Model)} spent by {Words.Seat(spent.Spender)}", spent.Count));
                    action.Steps.Add($"{Words.Seat(spent.Spender)} spends {markers} of {Id(spent.Model)}: +{spent.Count} DICE on the Injury Roll");
                }
                break;
            case DashRolled d:
                Success(seq, action, d.Check, "Risky roll", "DASH", "DASH FAILED");
                break;
            case SideClimbRolled c:
                Success(seq, action, c.Check, "Risky roll", "CLIMB", "CLIMB FAILED");
                action.Failed = c.Check.Outcome == SuccessOutcome.Failure;
                break;
            case RangedSuccessRolled r:
                Success(seq, action, r.Check, "Hit roll", "HIT", "MISSED");
                break;
            case MeleeSuccessRolled m:
                Success(seq, action, m.Check, "Hit roll", "HIT", "MISSED");
                break;
            case InjuryRolled i:
                Injury(seq, action, i, setup);
                break;
            case ChargeRolled c:
                action.Steps.Add($"Charge roll {c.Die}: +{c.Die}\" to the charge move");
                action.Outcome.Add($"charge +{c.Die}\"");
                break;
            case ChargeMoved m:
                action.Steps.Add(m.Reached ? $"Reaches {Id(m.Target)}" : $"Stops short of {Id(m.Target)}");
                action.Outcome.Add(m.Reached ? "REACHED ✓" : "STOPS SHORT ✗");
                break;
            case ModelMoved when action.Climb:
                action.Steps.Add(action.Failed ? "Moves to where the climb starts and stops" : "Moves, up or down the side");
                break;
            case ModelMoved { Kind: MoveKind.Dash }:
                action.Steps.Add("Moves");
                break;
        }
    }

    private void Start(int seq, ModelId actor, string headline, AttackKind? kind, ModelId? target)
    {
        _current = new Builder(seq, actor, headline, kind, target);
        _actions.Add(_current);
    }

    private void ShotParts(bool longRange, string? coverId, bool elevated)
    {
        if (elevated)
        {
            _current!.Dice.Add(new("Elevated position", AttackDice.ShotBonus(true)));
        }
        if (longRange)
        {
            _current!.Dice.Add(new("Long Range", -AttackDice.ShotPenalty(true, false)));
        }
        if (coverId is not null)
        {
            _current!.Dice.Add(new($"Cover ({coverId})", -AttackDice.ShotPenalty(false, true)));
        }
    }

    private void Success(int seq, Builder action, SuccessRollResult check, string what, string pass, string fail)
    {
        var parts = action.Dice.ToList();
        var why = new RollWhy(parts, check.NetDice, [], null, parts.Sum(p => p.Value) == check.NetDice);
        _why[seq] = why;
        var outcome = check.Outcome switch
        {
            SuccessOutcome.CriticalSuccess => $"CRITICAL {pass} ✓✓ {check.Dice.Total}",
            SuccessOutcome.Success => $"{pass} ✓ {check.Dice.Total}",
            _ => $"{fail} ✗ {check.Dice.Total}",
        };
        action.Steps.Add($"{what} {check.Dice.Total}: {outcome}{(why.Text.Length > 0 ? $" ({why.Text})" : "")}");
        action.Outcome.Add(outcome);
        if (check.RiskyFailure && !action.FreeFight)
        {
            action.Outcome.Add("RISKY: the activation ends");
        }
        action.Critical = check.Outcome == SuccessOutcome.CriticalSuccess;
        // The Injury Roll starts afresh: its dice come from the hit and the target's markers.
        action.Dice.Clear();
    }

    private void Injury(int seq, Builder action, InjuryRolled i, MatchSetup? setup)
    {
        var injury = i.Injury;
        var dice = new List<ResolutionPart>();
        if (action.Critical)
        {
            dice.Add(new("critical hit", AttackDice.InjuryBonus(true, false)));
        }
        if (action.Kind == AttackKind.Melee && injury.TargetAlreadyDown)
        {
            dice.Add(new("Melee Attack on a Down model", AttackDice.InjuryBonus(false, true)));
        }
        dice.AddRange(action.Dice);
        var modifiers = new List<ResolutionPart>();
        int? combined = null;
        if (setup?.ProfileOf(i.Attacker) is { } attacker && setup.ProfileOf(i.Target) is { } target && action.Kind is { } kind)
        {
            var values = AttackDice.InjuryModifiers(attacker, target, kind);
            modifiers.Add(new(kind == AttackKind.Melee ? "melee weapon" : "ranged weapon", values[0]));
            modifiers.Add(new("Armour", values[1]));
            combined = InjuryRoll.CombineModifiers(values);
        }
        var explained = dice.Sum(p => p.Value) == injury.NetDice && combined == injury.Modifier;
        var why = new RollWhy(dice, injury.NetDice, modifiers, injury.Modifier, explained);
        _why[seq] = why;
        var result = DiceRolls.Of(i)!.Result.TrimStart('✓', '•', ' ');
        action.Steps.Add($"Injury roll on {Id(i.Target)}: {result}{(why.Text.Length > 0 ? $" ({why.Text})" : "")}");
        action.Outcome.Add($"injury {result}");
    }

    private static string Id(ModelId id) => UnitPanel.ShortId(id);
}
