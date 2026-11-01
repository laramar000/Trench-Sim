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
        public int ShotgunInjury { get; set; }
        public bool Failed { get; set; }
        public int Weapon { get; set; }

        // Roster stage R4b: the model a BLAST attack was aimed at, once the blast has hit (only its roll is critical).
        public ModelId? BlastAimed { get; set; }

        // Roster stage R5a: a blast has hit (at a model, at a point or where a missed attack scattered to), so each
        // model it hit has its own Injury Roll.
        public bool Blasting { get; set; }
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
            case TreatDeclared t:
                // Roster stage R5h (U113): a Treat ACTION, with Holy Water's +1 DICE.
                Start(seq, t.Model, $"{Id(t.Model)} TREATS {Id(t.Target)}", null, t.Target);
                if (setup?.ProfileOf(t.Model) is { HealingWaters: true })
                {
                    _current!.Dice.Add(new("Holy Water of Lalibela", 1));
                }
                break;
            case SideClimbDeclared c:
                // Towers stage T6: a Move or Dash that climbs a tower's side is its own action with its own roll.
                Start(seq, c.Model, $"{Id(c.Model)} CLIMBS A TOWER'S SIDE", null, null);
                _current!.Climb = true;
                // Roster stage R5d (U108).
                if (setup?.ProfileOf(c.Model) is { ClimbDice: > 0 } climber)
                {
                    _current!.Dice.Add(new("Mountaineer Kit", climber.ClimbDice));
                }
                break;
            case RangedAttackDeclared r:
                Start(seq, r.Attacker, $"{Id(r.Attacker)} {(r.Weapon == ModelProfile.GrenadeWeapon ? "THROWS A GRENADE AT" : "SHOOTS")} {Id(r.Target)}{(r.Weapon == 1 ? " (2nd weapon)" : "")}", AttackKind.Ranged, r.Target);
                _current!.Weapon = r.Weapon;
                OwnParts(setup, r.Attacker, AttackKind.Ranged);
                ShotParts(setup, r.Attacker, r.LongRange, r.CoverTerrainId, r.Elevated, r.Target);
                break;
            case PointAttackDeclared p:
                // Roster stage R5a (U105): a BLAST attack aimed at a point.
                Start(seq, p.Attacker, $"{Id(p.Attacker)} {(p.Weapon == ModelProfile.GrenadeWeapon ? "THROWS A GRENADE AT" : "SHOOTS AT")} {TablePoint.Of(p.At)}{(p.Weapon == 1 ? " (2nd weapon)" : "")}", AttackKind.Ranged, null);
                _current!.Weapon = p.Weapon;
                OwnParts(setup, p.Attacker, AttackKind.Ranged);
                ShotParts(setup, p.Attacker, p.LongRange, null, false);
                break;
            case MeleeAttackDeclared m:
                Start(seq, m.Attacker, $"{Id(m.Attacker)} {(m.FreeFight ? "MAKES A FREE ATTACK ON" : "FIGHTS")} {Id(m.Target)}{(m.Weapon == 1 ? " (2nd weapon)" : "")}", AttackKind.Melee, m.Target);
                _current!.FreeFight = m.FreeFight;
                _current!.Weapon = m.Weapon;
                OwnParts(setup, m.Attacker, AttackKind.Melee);
                if (m.DefendedObstacleId is { } obstacle)
                {
                    _current!.Dice.Add(new($"Defended obstacle ({(obstacle == Rules.Core.TurnSequence.CoverKeyword ? "its COVER keyword" : obstacle)})", -AttackDice.FightPenalty(true)));
                }
                // Roster stage R3 (U100): Off-Hand weapon and BLOCK after a charge.
                if (m.OffHand)
                {
                    _current!.Dice.Add(new("Off-Hand weapon", -AttackDice.FightPenalty(true)));
                }
                if (m.Block)
                {
                    _current!.Dice.Add(new("BLOCK after a charge", -AttackDice.FightPenalty(true)));
                }
                // Roster stage R5b (U106): the target has FEAR.
                if (m.Fear)
                {
                    _current!.Dice.Add(new("target has FEAR", -AttackDice.FightPenalty(true)));
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
                OwnParts(setup, f.Attacker, AttackKind.Ranged);
                ShotParts(setup, f.Attacker, f.LongRange, f.CoverTerrainId, f.Elevated, f.Victim);
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
                // Roster stage R5d (U108): the Dash roll's die from a Musical Instrument within 4" (worked out from the
                // positions by the rules; here it is what the roll has beyond the markers).
                if (d.Check.NetDice - action.Dice.Sum(p => p.Value) == 1 && setup is not null
                    && setup.Models.Any(m => m.Seat == setup.Models.First(x => x.Id == d.Model).Seat && m.Profile?.Fanfare == true))
                {
                    action.Dice.Add(new("Musical Instrument within 4\"", 1));
                }
                Success(seq, action, d.Check, setup, "Risky roll", "DASH", "DASH FAILED");
                break;
            case TreatRolled t:
                Success(seq, action, t.Check, setup, "Risky roll", "TREATED", "TREAT FAILED");
                if (t.Check.Outcome != SuccessOutcome.Failure)
                {
                    action.Outcome.Add(!t.Treated ? "nothing left to treat" : t.Choice == TreatChoice.StandUp ? $"{Id(t.Target)} stands up" : $"1 Blood Marker off {Id(t.Target)}");
                }
                break;
            case SideClimbRolled c:
                Success(seq, action, c.Check, setup, "Risky roll", "CLIMB", "CLIMB FAILED");
                action.Failed = c.Check.Outcome == SuccessOutcome.Failure;
                break;
            case RangedSuccessRolled r:
                // Roster stage R5e (U109): the Urn of the Bitter Ashes' -1 DICE (worked out from the positions by the
                // rules; here it is what the roll has beyond the known parts).
                if (r.Check.NetDice - action.Dice.Sum(p => p.Value) == -1 && setup is not null && setup.Models.Any(m => m.Profile?.SinisterWhispers == true))
                {
                    action.Dice.Add(new("Urn of the Bitter Ashes within 3\"", -1));
                }
                Success(seq, action, r.Check, setup, "Hit roll", "HIT", "MISSED");
                break;
            case PointSuccessRolled r:
                Success(seq, action, r.Check, setup, "Hit roll", "HIT", "MISSED");
                break;
            case PointBlastHit b:
                // No model was the target, so a Critical Success adds no dice to anyone (U105).
                action.Blasting = true;
                action.Critical = false;
                action.Steps.Add(b.Hit.IsEmpty ? "BLAST hits nobody" : $"BLAST hits {string.Join(", ", b.Hit.Select(Id))}");
                action.Outcome.Add(b.Hit.IsEmpty ? "BLAST hits nobody" : $"BLAST hits {b.Hit.Length}");
                break;
            case MeleeSuccessRolled m:
                Success(seq, action, m.Check, setup, "Hit roll", "HIT", "MISSED");
                break;
            case BlastHit b:
                action.BlastAimed = b.Target;
                action.Blasting = true;
                action.Steps.Add($"BLAST hits {string.Join(", ", b.Hit.Select(Id))}");
                action.Outcome.Add($"BLAST hits {b.Hit.Length}");
                break;
            case AttackScattered s:
                // Roster stage R4c: the attack missed and scatters; no Critical Success, so no model gets extra dice.
                action.BlastAimed = s.Target;
                action.Blasting = true;
                action.Steps.Add($"Scatters {s.Inches}\" to {TablePoint.Of(s.At)}: {(s.Hit.IsEmpty ? "hits nobody" : "BLAST hits " + string.Join(", ", s.Hit.Select(Id)))}");
                action.Outcome.Add(s.Hit.IsEmpty ? $"scatters {s.Inches}\", hits nobody" : $"scatters {s.Inches}\", BLAST hits {s.Hit.Length}");
                break;
            case ScatterMissed s:
                action.Steps.Add($"Nowhere to scatter {s.Inches}\": misses");
                action.Outcome.Add("nowhere to scatter");
                break;
            case InjuryRolled i:
                Injury(seq, action, i, setup);
                if (action.Blasting)
                {
                    // Each model the blast hit has its own Injury Roll, with its own spent markers.
                    action.Dice.Clear();
                }
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

    // Roster stage R3: the attacker's profile as the action's weapon sees it (ModelProfile.Using).
    private static ModelProfile? Armed(MatchSetup? setup, ModelId attacker, AttackKind kind, int weapon) =>
        setup?.ProfileOf(attacker) is { } p ? weapon > 0 && p.Has(kind, weapon) ? p.Using(kind, weapon) : p : null;

    // Roster stage R1: the attacker's own dice on the Success Roll, its characteristic and its weapon's DICE.
    private void OwnParts(MatchSetup? setup, ModelId attacker, AttackKind kind)
    {
        if (Armed(setup, attacker, kind, _current!.Weapon) is not { } profile)
        {
            return;
        }
        var (characteristic, weapon) = DicePreview.OwnSplit(profile, kind);
        if (characteristic != 0)
        {
            _current!.Dice.Add(new(DicePreview.CharacteristicWord(profile, kind), characteristic));
        }
        if (weapon != 0)
        {
            _current!.Dice.Add(new(DicePreview.WeaponWord(profile, kind), weapon));
        }
    }

    // The shot's modifiers as they apply with the weapon's keywords (roster stage R2: IGNORE ..., SHOTGUN).
    // Roster stage R5d (U108): a target with a Cloak of the Mountain makes Cover -2 DICE.
    private void ShotParts(MatchSetup? setup, ModelId attacker, bool longRange, string? coverId, bool elevated, ModelId? target = null)
    {
        var keywords = Armed(setup, attacker, AttackKind.Ranged, _current!.Weapon)?.RangedWeaponKeywords ?? WeaponKeywords.None;
        var cloaked = target is { } t && setup?.ProfileOf(t)?.BlendsIn == true;
        var applied = AttackDice.ShotModifiers(keywords, longRange, coverId is not null, elevated, cloaked);
        if (applied.Elevated != 0)
        {
            _current!.Dice.Add(new("Elevated position", applied.Elevated));
        }
        if (applied.LongRange != 0)
        {
            _current!.Dice.Add(new("Long Range", -applied.LongRange));
        }
        if (applied.Cover != 0)
        {
            _current!.Dice.Add(new($"Cover ({(coverId == Rules.Core.TurnSequence.CoverKeyword ? "its COVER keyword" : coverId)}){(cloaked ? ", Cloak of the Mountain" : "")}", -applied.Cover));
        }
        _current!.ShotgunInjury = applied.ShotgunInjury;
    }

    private void Success(int seq, Builder action, SuccessRollResult check, MatchSetup? setup, string what, string pass, string fail)
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
            // Roster stage R5d (U108): not for a model with a Pontiff's Crucifix.
            action.Outcome.Add(setup?.ProfileOf(action.Actor)?.KeepsGoingAfterRisky == true ? "RISKY: the Crucifix keeps the activation going" : "RISKY: the activation ends");
        }
        action.Critical = check.Outcome == SuccessOutcome.CriticalSuccess;
        // The Injury Roll starts afresh: its dice come from the hit and the target's markers.
        action.Dice.Clear();
    }

    private void Injury(int seq, Builder action, InjuryRolled i, MatchSetup? setup)
    {
        var injury = i.Injury;
        var dice = new List<ResolutionPart>();
        var own = action.Kind is { } armedKind ? Armed(setup, i.Attacker, armedKind, action.Weapon) : setup?.ProfileOf(i.Attacker);
        if (action.Critical && (action.BlastAimed is null || action.BlastAimed == i.Target))
        {
            var critical = own is not null && action.Kind is { } k ? AttackDice.CriticalInjuryDice(own.AttackKeywords(k)) : AttackDice.InjuryBonus(true, false);
            dice.Add(new(critical == 2 ? "critical hit (CRITICAL)" : "critical hit", critical));
        }
        if (action.ShotgunInjury != 0 && action.Kind == AttackKind.Ranged)
        {
            dice.Add(new("SHOTGUN at Long Range", action.ShotgunInjury));
        }
        if (action.Kind == AttackKind.Melee && injury.TargetAlreadyDown)
        {
            dice.Add(new("Melee Attack on a Down model", AttackDice.InjuryBonus(false, true)));
        }
        if (own is not null && action.Kind is { } attackKind && AttackDice.OwnInjuryDice(own, attackKind) is var weaponDice and not 0)
        {
            dice.Add(new(DicePreview.WeaponWord(own, attackKind), weaponDice));
        }
        // Roster stage R5c (U107): the target's Holy Smoke or Zealot's Draught.
        if (setup?.ProfileOf(i.Target) is { InjuryDiceTaken: not 0 } struck)
        {
            dice.Add(new("target's battlekit", struck.InjuryDiceTaken));
        }
        dice.AddRange(action.Dice);
        var modifiers = new List<ResolutionPart>();
        int? combined = null;
        if (own is { } attacker && setup?.ProfileOf(i.Target) is { } target && action.Kind is { } kind)
        {
            var values = AttackDice.InjuryModifiers(attacker, target, kind);
            modifiers.Add(new(kind == AttackKind.Melee ? "melee weapon" : "ranged weapon", values[0]));
            var k2 = attacker.AttackKeywords(kind);
            modifiers.Add(new(k2.HasFlag(WeaponKeywords.IgnoreArmour) && values[1] != 0 ? "Armour (IGNORE ARMOUR; IMPERVIOUS counts)" : k2.HasFlag(WeaponKeywords.IgnoreArmour) ? "Armour (IGNORE ARMOUR)" : k2.HasFlag(WeaponKeywords.ArmourPiercing) && values[1] != target.Armour ? "Armour (ARMOUR PIERCING)" : "Armour", values[1]));
            if (values.Length > 2)
            {
                modifiers.Add(new("Grovelling Devotees", values[2]));
            }
            combined = InjuryRoll.CombineModifiers(values);
        }
        var explained = dice.Sum(p => p.Value) == injury.NetDice && combined == injury.Modifier;
        var why = new RollWhy(dice, injury.NetDice, modifiers, injury.Modifier, explained);
        _why[seq] = why;
        var result = DiceRolls.Of(i)!.Result.TrimStart('✓', '•', ' ');
        action.Steps.Add($"Injury roll on {Id(i.Target)}: {result}{(why.Text.Length > 0 ? $" ({why.Text})" : "")}");
        if (i.ExtraMarkers > 0 && own is not null && action.Kind is { } markerKind)
        {
            var words = string.Join(", ", new[] { WeaponKeywords.Shrapnel, WeaponKeywords.Gas, WeaponKeywords.Fire }
                .Where(k => own.AttackKeywords(markerKind).HasFlag(k)).Select(k => k.ToString().ToUpperInvariant()));
            action.Steps.Add($"+{i.ExtraMarkers} Blood Marker{(i.ExtraMarkers == 1 ? "" : "s")} on {Id(i.Target)} ({words})");
        }
        action.Outcome.Add($"injury {result}");
    }

    private static string Id(ModelId id) => UnitPanel.ShortId(id);
}
