using Rules.Core.State;

namespace Client.Core;

// Proposal G, step G1: the selected-unit and action panel beside the battlefield. It says who the selected unit is and
// what state it is in, which actions it has, and, while an action is being chosen, what that action would do (the
// preview). It is built only from the server's state, the battlefield's scene and the interaction's preview, so it adds
// no rule and no odds; the server still decides every command. Long rules text and pages sit under Details.
public sealed record UnitPanelView(
    string Title,
    IReadOnlyList<string> Unit,
    string? Unavailable,
    string? ActionTitle,
    IReadOnlyList<string> Action,
    string? Refusal,
    string? Notice,
    IReadOnlyList<string> Details,
    string ConfirmLabel,
    bool CanConfirm,
    bool CanCancel,
    bool CanUndo = false)
{
    public const string PreviewLine = "PREVIEW · the server decides";
    public const string PointingPrefix = "POINTING AT ";

    // QoL H5: under Confirm, Undo and Cancel while an action is being chosen; the same for every action.
    public const string KeysLine = "Keys: Space confirms · Backspace undoes a waypoint · Esc or right-click cancels";

    // The model under the pointer (QoL H1), when it is not the unit or target already shown: who it is and its state,
    // or in Shoot, Charge and Fight what choosing it as the target would give. Nothing is chosen by pointing.
    public IReadOnlyList<string> Pointing { get; init; } = [];
    public const string SentLine = "SENT · waiting for the server";

    // Battlefield UI 3, stage U4: the card's closed form, one short line ("A2 · Marksman · Standing · BM 1"): the unit
    // (or the card's title when no unit is shown), its state and its Blood Markers. The rest opens from it.
    public string Tab { get; init; } = "";

    // Every line shown without opening Details (for tests and the UI check).
    public IEnumerable<string> Shown()
    {
        yield return Title;
        foreach (var line in Unit)
        {
            yield return line;
        }
        if (Unavailable is not null)
        {
            yield return Unavailable;
        }
        if (ActionTitle is not null)
        {
            yield return ActionTitle;
        }
        foreach (var line in Action)
        {
            yield return line;
        }
        if (Refusal is not null)
        {
            yield return Refusal;
        }
        foreach (var line in Pointing)
        {
            yield return line;
        }
        if (Notice is not null)
        {
            yield return Notice;
        }
    }
}

public static class UnitPanel
{
    // The short id drawn on the battlefield and used across the screen: "A1" for model a1.
    public static string ShortId(ModelId id) => id.Value.ToUpperInvariant();

    // "A2 · Skirmisher": the short id, then the name without the id the names may already carry ("Skirmisher (a2)").
    public static string Heading(ModelShape unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var suffix = $" ({unit.Id.Value})";
        var name = unit.Name.EndsWith(suffix, StringComparison.Ordinal) ? unit.Name[..^suffix.Length] : unit.Name;
        return $"{ShortId(unit.Id)} · {name}";
    }

    public static UnitPanelView Of(MatchState state, BoardScene? scene, Interaction interaction, Seat? seat, ModelShape? selected, ModelShape? pointing = null,
        TerrainShape? objective = null, string? lastScoring = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(interaction);
        var bar = interaction.Bar(selected?.Id);
        var confirm = bar.Items.FirstOrDefault(i => i.Kind == BarKind.Confirm);
        var choosing = interaction.Mode != InteractionMode.None;
        // While an action is being chosen the panel is about the acting model, whatever else is selected.
        var unit = choosing && interaction.Actor is { } actor && scene?.Find(actor) is { } acting ? acting : selected;

        var title = unit is null ? objective is { Objective: true } o ? ObjectiveTitle(o) : "No unit selected" : Heading(unit);
        var lines = new List<string>();
        string? unavailable = null;
        var details = new List<string>();
        if (unit is null && objective is { Objective: true } chosenObjective)
        {
            lines.AddRange(ObjectiveLines(chosenObjective, lastScoring));
            details.Add(ObjectiveRule);
        }
        else if (unit is null)
        {
            lines.Add(seat is null ? "Watching."
                : interaction.Mode == InteractionMode.Place ? "Placing a model in your deployment zone."
                : "Click one of your units to see what it can do.");
        }
        else
        {
            lines.Add(StatusLine(state, unit));
            if (EngagedLine(state, unit.Id) is { } engaged)
            {
                lines.Add(engaged);
            }
            if (TowerLine(unit) is { } tower)
            {
                lines.Add(tower);
            }
            if (State(unit, interaction) is { } turnState)
            {
                lines.Add(turnState);
            }
            if (ProfileLine(state, unit) is { } profile)
            {
                lines.Add(profile);
            }
            if (WarbandLine(state, unit.Seat) is { } warband)
            {
                lines.Add(warband);
            }
            unavailable = choosing ? null : Unavailable(state, scene, interaction, seat, unit) is { } why ? $"— {why}" : null;
            // The actions are listed only for the unit they belong to (the bar's own unit, yours).
            if (!choosing && unit.Yours && (bar.Anchor is null || bar.Anchor == unit.Id))
            {
                var actions = bar.Items.Where(i => i.Enabled && i.Kind is BarKind.Start or BarKind.Activate or BarKind.EndActivation or BarKind.Decide)
                    .Select(i => i.Label).ToList();
                if (actions.Count > 0)
                {
                    lines.Add($"Actions: {string.Join(" · ", actions)}");
                }
            }
            details.Add(unit.Text);
            if (state.Setup?.ProfileOf(unit.Id) is { } p)
            {
                details.Add(p.CanMakeMeleeAttack
                    ? $"Injury modifiers of its attacks: ranged {Signed(p.RangedInjuryModifier)}, melee {Signed(p.MeleeAttackInjuryModifier)}."
                    : $"Injury modifiers of its attacks: ranged {Signed(p.RangedInjuryModifier)}; it has no weapon to fight with.");
            }
        }

        string? actionTitle = null;
        var action = new List<string>();
        string? refusal = null;
        if (choosing)
        {
            var preview = interaction.Preview;
            var verb = Interaction.Words(interaction.Mode).ToUpperInvariant();
            ModelShape? target = preview?.Target is { } t ? scene?.Find(t) : null;
            actionTitle = preview switch
            {
                null when interaction.Mode == InteractionMode.Combat => $"{verb}: choose Shoot, Charge or Fight",
                null => interaction.IsTarget ? $"{verb}: choose a target" : $"{verb}: choose where",
                { Kind: PreviewKind.Place } => $"PLACE {ShortId(preview.Model)} → {preview.Path[0].At}",
                { Kind: PreviewKind.Move or PreviewKind.Dash or PreviewKind.Retreat } => $"{verb} → {preview.Path[^1].At}",
                _ => target is null ? verb : $"{verb} → {Heading(target)}",
            };
            if (interaction.Pending is not null)
            {
                action.Add(UnitPanelView.SentLine);
            }
            if (preview is not null)
            {
                action.Add(UnitPanelView.PreviewLine);
                // The card's lines without its headline, the target line (already in the title) and the refusal (below).
                foreach (var line in PreviewCard.Lines(preview, target).Skip(1))
                {
                    if (line.StartsWith("target ", StringComparison.Ordinal) || (preview.Refusal is { } r && line == $"{r.Text} ({r.RuleRef})")
                        || (preview.Kind == PreviewKind.Place && line == $"{preview.Model.Value} at {preview.Path[0].At}"))
                    {
                        continue;
                    }
                    action.Add(line);
                }
                if (preview.Kind == PreviewKind.Charge && preview.ChargePath.Count > 1)
                {
                    action.Add(preview.ChargeRollNeeded is { } n
                        ? n == 1 ? "reaches on any Charge roll" : $"needs a Charge roll of {n}+"
                        : "stops short of the target");
                }
                if (preview.Refusal is { } refused)
                {
                    refusal = $"REFUSED: {refused.Text}";
                    details.Add($"Rule: {refused.RuleRef}");
                }
                details.AddRange(preview.Facts.Where(f => !action.Contains(f.TrimEnd('.'))));
            }
            if (interaction.IsTarget)
            {
                // The enemies crossed out on the battlefield, and why, in a few words each.
                var notValid = interaction.TargetVerdicts().Where(v => v.Refusal is not null && v.Model != interaction.Target)
                    .Select(v => $"{ShortId(v.Model)} ({ShortReason(v.Refusal!.Code)})").ToList();
                if (notValid.Count > 0)
                {
                    action.Add($"✕ not valid: {string.Join(", ", notValid)}");
                }
            }
            if (preview is null && interaction.UsesCursor)
            {
                details.Add($"Cursor at {interaction.Cursor}.");
            }
        }

        if (scene is not null)
        {
            // The battlefield draws terrain without words; what each piece is sits here.
            var kinds = scene.Terrain.Select(p => p.Label.Replace(", Objective", "", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal).ToList();
            if (kinds.Count > 0)
            {
                details.Add($"Terrain on this table: {string.Join("; ", kinds)}.");
            }
            details.Add("Battlefield marks: ◆n objective (A or B: who controls it now), ZONE deployment edge, •N Blood Markers, ✓ activated, ↓ down, ✕ not a valid target.");
        }

        return new UnitPanelView(title, lines, unavailable, actionTitle, action, refusal, interaction.Notice, details,
            confirm?.Label ?? "Confirm", confirm?.Enabled == true, choosing && interaction.Pending is null,
            bar.Items.Any(i => i.Kind == BarKind.Undo && i.Enabled))
        {
            Pointing = PointingAt(state, interaction, unit, pointing),
            Tab = unit is null ? title : $"{Heading(unit)} · {Words.Status(unit.Status)}{(unit.BloodMarkers > 0 ? $" · BM {unit.BloodMarkers}" : "")}",
        };
    }

    // QoL H4: an Objective in the panel. Who controls it now comes from the rules' own test (the scene's Controller,
    // Rules.Core.Objectives.ControllerNow), the last scoring from the combat log; nothing is judged from the picture.
    public static string ObjectiveTitle(TerrainShape objective)
    {
        ArgumentNullException.ThrowIfNull(objective);
        return $"{objective.Mark} · Objective {objective.Id}";
    }

    public static IReadOnlyList<string> ObjectiveLines(TerrainShape objective, string? lastScoring)
    {
        ArgumentNullException.ThrowIfNull(objective);
        return
        [
            objective.Controller is { } seat ? $"Controlled now: {Words.Seat(seat)} [{Words.SeatLetter(seat)}] (if scored now)" : "Controlled now: nobody (if scored now)",
            $"{Rules.Core.Objectives.PointsPerObjective} VP to its controller at the end of each turn",
            lastScoring is null ? "Not scored yet" : $"Last scoring: {lastScoring}",
        ];
    }

    public const string ObjectiveRule = "Control (Scenario I, pp. 152-153): more of a side's models on, in or within 1\" of the piece than the other side's; if only one side has models on it, that side controls it. Down models count. Scored at the end of each turn.";

    // Seat, Standing or Down, and Blood Markers, with the battlefield's own marks: "Seat Two [B] · ↓ Down · •2 Blood
    // Markers" (the server's state).
    private static string StatusLine(MatchState state, ModelShape unit)
    {
        var found = state.FindModel(unit.Id);
        var markers = found?.BloodMarkers ?? 0;
        // Roster stage R10 (U135): INFECTION and BLESSING MARKERS are listed after the blood.
        var others = (found?.InfectionMarkers ?? 0, found?.BlessingMarkers ?? 0, found?.SinMarkers ?? 0);
        return $"{Words.Seat(unit.Seat)} [{unit.SeatLetter}]{(unit.Yours ? " (yours)" : "")} · {(unit.Status == ModelStatus.Down ? $"{ModelShape.DownMark} " : "")}{Words.Status(unit.Status)}" +
            (markers > 0 ? $" · {ModelShape.MarkerMark}{markers} Blood Marker{(markers == 1 ? "" : "s")}" : "") +
            (others.Item1 > 0 ? $" · {ModelShape.MarkerMark}{others.Item1} Infection Marker{(others.Item1 == 1 ? "" : "s")}" : "") +
            (others.Item2 > 0 ? $" · {ModelShape.MarkerMark}{others.Item2} Blessing Marker{(others.Item2 == 1 ? "" : "s")}" : "") +
            (others.Item3 > 0 ? $" · {ModelShape.MarkerMark}{others.Item3} Sin Marker{(others.Item3 == 1 ? "" : "s")}" : "");
    }

    // QoL H3: the unit's values as the rules hold them (its profile in the match setup): "Move 6.0" · Range 24.0" ·
    // Armour 1 · Melee weapon". While it activates after standing up, the halved Movement too. There are no wounds.
    public static string? ProfileLine(MatchState state, ModelShape unit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(unit);
        if (state.Setup?.ProfileOf(unit.Id) is not { } p)
        {
            return null;
        }
        var move = BoardScene.Inch(TableRect.Inches(p.Movement));
        var halved = unit.Active && state.ActiveModel == unit.Id && state.MovementHalved
            ? $" ({BoardScene.Inch(TableRect.Inches(p.Movement) / 2)} now, halved after standing up)" : "";
        var range = p.HasRangedWeapon ? $"Range {BoardScene.Inch(TableRect.Inches(p.RangedRange))}" : "No ranged weapon";
        return $"Move {move}{halved} · {range} · Armour {p.Armour} · {MeleeWords(p)}{DiceWords(p)}{WeaponWords(p)}";
    }

    // Roster stage R3: second weapons, CLEAVE, BLOCK and IMPERVIOUS Armour, only when the model has them.
    public static string WeaponWords(ModelProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var parts = new List<string>();
        // Roster stage R4b: BLAST (X") of each gun and the grenades.
        static string Blast(int x) => x == 0 ? "" : $" BLAST {x}\"";
        if (p.RangedWeaponBlast > 0)
        {
            parts.Add($"gun{Blast(p.RangedWeaponBlast)}");
        }
        if (p.SecondRangedWeapon is { } gun)
        {
            parts.Add($"2nd gun range {BoardScene.Inch(TableRect.Inches(gun.Range))}{Blast(gun.Blast)}");
        }
        if (p.SecondMeleeWeapon is not null)
        {
            parts.Add("2nd Melee Weapon (Off-Hand -1 DICE)");
        }
        if (p.Grenade is { } grenade)
        {
            parts.Add($"Grenades range {BoardScene.Inch(TableRect.Inches(grenade.Range))}{Blast(grenade.Blast)}");
        }
        if (p.HasMeleeWeapon && p.MeleeWeaponCleave > 1)
        {
            parts.Add($"CLEAVE {p.MeleeWeaponCleave}");
        }
        if (p.HasBlock)
        {
            parts.Add("BLOCK");
        }
        if (p.ImperviousArmour != 0)
        {
            parts.Add($"IMPERVIOUS Armour {p.ImperviousArmour}");
        }
        // Roster stage R5c (U107): battlekit that changes Injury Rolls.
        if (p.InjuryDiceTaken != 0)
        {
            parts.Add($"hit for {p.InjuryDiceTaken:+0;-0} INJURY DICE");
        }
        if (p.MeleeInjuryBonus != 0)
        {
            parts.Add($"Fight injury {p.MeleeInjuryBonus:+0;-0}");
        }
        // Roster stage R5d (U108): battlekit for Risky rolls and Cover.
        if (p.KeepsGoingAfterRisky)
        {
            parts.Add("Risky failure keeps going");
        }
        if (p.ClimbDice != 0)
        {
            parts.Add($"climb {p.ClimbDice:+0;-0} DICE");
        }
        if (p.Fanfare)
        {
            parts.Add("Dash +1 DICE within 4\"");
        }
        if (p.BlendsIn)
        {
            parts.Add("Cover -2 DICE against it");
        }
        // Roster stage R5e (U109).
        if (p.SinisterWhispers)
        {
            parts.Add("shots at it or within 3\" -1 DICE");
        }
        if (p.WardedByBlood)
        {
            parts.Add("first BLOOD MARKER ignored");
        }
        // Roster stage R5f (U110).
        if (p.HasCover)
        {
            parts.Add("COVER");
        }
        if (p.DigsIn)
        {
            parts.Add("COVER while dug in");
        }
        // Roster stage R5g (U112).
        if (p.BurnsAtActivationEnd)
        {
            parts.Add("+1 BLOOD MARKER to enemies within 1\" when it ends an activation");
        }
        if (p.BurnsWhenTakenOut)
        {
            parts.Add("+1 BLOOD MARKER to enemies within 1\" when taken Out of Action");
        }
        // Roster stage R5h (U113).
        if (p.Treats)
        {
            parts.Add(p.HealingWaters ? "Treat ACTION (H), +1 DICE" : "Treat ACTION (H)");
        }
        // Roster stage R5j (U115).
        if (p.Martyrdom)
        {
            parts.Add("Martyrdom Device (B), once per game");
        }
        // Roster stage R6b (U118).
        if (p.Aims)
        {
            parts.Add("Aim ACTION (I)");
        }
        if (p.Fortifies)
        {
            parts.Add("Fortify ACTION (V)");
        }
        if (p.Loudspeakers)
        {
            parts.Add("Loudspeakers ACTION (comma key): a Risky roll at +2 DICE; friends within 8\" move 3\" towards the nearest visible enemy");
        }
        if (p.Spells != SpellKind.None)
        {
            parts.Add("Spells: " + string.Join(", ", Enum.GetValues<SpellKind>().Where(k => k != SpellKind.None && p.Spells.HasFlag(k)).Select(Spells.Name)) + " (cast with the CLI: cast)");
        }
        if (p.Heals)
        {
            parts.Add("Arise and be Healed! ACTION: a Risky roll; on a Success itself or a friend within 3\" stands up free and loses up to D3 BLOOD and/or INFECTION MARKERS (CLI: arise)");
        }
        if (p.Blesses)
        {
            parts.Add("God is With Us! ACTION: a Risky roll; on a Success 1 BLESSING MARKER on itself or a friend within 6\" (CLI: bless)");
        }
        if (p.PuppetMaster)
        {
            parts.Add("Puppet Master ACTION: a Risky roll; moves one model in sight within 12\" D6\" in a straight line (CLI: puppet)");
        }
        if (p.Hides)
        {
            parts.Add("Hide ACTION (G): beside cover, a Risky roll; then it cannot be shot or charged");
        }
        // Roster stage R6e (U121).
        if (p.EnforcesOrthodoxy)
        {
            parts.Add("Enforced Orthodoxy ACTION (Y)");
        }
        if (p.LaysOnHands)
        {
            parts.Add(p.HealingWaters ? "Laying on of Hands ACTION (Z), +1 DICE" : "Laying on of Hands ACTION (Z)");
        }
        // Roster stage R6c (U119).
        foreach (var (on, words) in new[]
        {
            (p.Agile, "Agile: +1 DICE to Dash and climb rolls"),
            (p.SwiftDash, "+1 DICE to its Dash roll"),
            (p.QuickToRise, "Movement not halved when it stands up"),
            (p.Bodyguard, "Bodyguard: may take a hit meant for a friendly model of its faction within 1\" (not a BLAST)"),
            (p.Hateful, "Hateful: must charge the closest non-CULT, non-DEMONIC enemy in sight within 12\" when it activates"),
            (p.DefusesMines, "Defuse Mine: Risky roll to remove a Mine Marker it halts at"),
            (p.UndeadFortitude, "hit for -1 INJURY DICE unless FIRE"),
            (p.AbioticLife, "hit for -1 INJURY DICE by GAS"),
            (p.Stealthy, "-1 DICE to shots at it (Stealth Generator)"),
            (p.HuntsTheTough, "+1 INJURY DICE against TOUGH"),
            (p.CounterCharge, "Counter-Charge: +1 DICE in melee after charging first"),
            (p.MementoMori, "first Out of Action counts as No Effect"),
            (p.Commandable, "can be given Commands by a Slavemaster"),
            (p.CastsInferno, "can cast Burning Inferno: a 36\" BLAST attack paid in SIN MARKERS"),
            (p.Perverse, "Perversion of God's Laws: its enemies may remove its SIN MARKERS instead of placing BLOOD MARKERS"),
            (p.MortalSuffering, "Mortal Suffering: its BLOOD MARKERS on enemies may become SIN MARKERS on a friend"),
            (p.SinVessel, "carries a Sin Vessel (Break Vessel ACTION: 2 SIN MARKERS, once per game)"),
            (p.SinBeacon, "places 1 SIN MARKER on itself when it activates"),
        })
        {
            if (on)
            {
                parts.Add(words);
            }
        }
        // Roster stage R6a (U117): New Antioch abilities.
        if (p.ShotsIgnoreMarkers)
        {
            parts.Add("no BLOOD MARKERS spent on its shots");
        }
        if (p.ExpertMedic)
        {
            parts.Add("Treat +2 DICE");
        }
        if (p.FinishesTheFallen)
        {
            parts.Add("+1 INJURY DICE in melee at a Down model (not CULT or DEMONIC)");
        }
        if (p.ExtraChargeDie)
        {
            parts.Add("Charge Bonus: highest of 2D6");
        }
        if (p.InspiresFaithful)
        {
            parts.Add("friendly PRINCIPALITY models within 8\" have NEGATE FEAR");
        }
        // Roster stage R5k (U116).
        if (p.Talismans > 0)
        {
            parts.Add(p.Talismans == 1 ? "Talisman: once per game a failed Risky roll need not end the activation"
                : $"{p.Talismans} Talismans: each once per game, a failed Risky roll need not end the activation");
        }
        // Roster stage R5i (U114).
        if (p.GrindsToDust)
        {
            parts.Add("Grind to Dust ACTION (Combat 0)");
        }
        // Roster stage R6d (U120).
        if (p.Tramples)
        {
            parts.Add(p.GrindsToDust ? "Trample ACTION (Combat menu)" : "Trample ACTION (Combat 0)");
        }
        if (p.Horde)
        {
            parts.Add("+1 DICE in melee per friend within 3\" (Overwhelming Horde)");
        }
        // Roster stage R5b (U106): the model keywords the engine plays.
        foreach (var (trait, words) in new[] { (ModelTraits.Fear, "FEAR"), (ModelTraits.NegateFear, "NEGATE FEAR"), (ModelTraits.Tough, "TOUGH"), (ModelTraits.NegateHeavy, "NEGATE HEAVY"), (ModelTraits.Leader, "LEADER") })
        {
            if (p.Traits.HasFlag(trait))
            {
                parts.Add(words);
            }
        }
        return string.Concat(parts.Select(x => $" · {x}"));
    }

    // Roster stage R1: the dice the model's own attacks start with, only when not zero (" · Shoot +1 DICE · Fight +2 DICE").
    public static string DiceWords(ModelProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var parts = new List<string>();
        if (p.HasRangedWeapon && Rules.Core.Dice.AttackDice.OwnDice(p, AttackKind.Ranged) is var shoot and not 0)
        {
            parts.Add($"Shoot {shoot:+0;-0} DICE");
        }
        if (p.CanMakeMeleeAttack && Rules.Core.Dice.AttackDice.OwnDice(p, AttackKind.Melee) is var fight and not 0)
        {
            parts.Add($"Fight {fight:+0;-0} DICE");
        }
        return string.Concat(parts.Select(x => $" · {x}"));
    }

    // Proposal M1: what the model can fight with (1.0.2 pp. 45, 68): its Melee Weapon, its ranged weapon that also has
    // "Melee" under its Range (a Pistol, 12"/Melee), or nothing.
    public static string MeleeWords(ModelProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.HasMeleeWeapon ? "Melee weapon" : p.RangedAlsoMelee ? "Ranged weapon also Melee" : "No melee weapon";
    }

    // Towers proposal, stage T4: where a unit on a tower stands, and how it can leave. Null on the ground.
    public static string? TowerLine(ModelShape unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.TowerId is null ? null : $"{TowerPrefix}{BoardScene.Inch(unit.Standing)} up): it leaves by a ladder, or down a side with a Risky roll";
    }

    public const string TowerPrefix = "▲ On the tower (";

    // The unit's warband state when it is Shaken or has Fled (the rules hold these for a warband, not for a model).
    public static string? WarbandLine(MatchState state, Seat seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        var warband = state.SeatFor(seat);
        return warband.Fled ? "Warband: Fled" : warband.Shaken ? "Warband: Shaken" : null;
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // What the panel says about the model under the pointer (QoL H1). In Shoot, Charge and Fight, for an enemy: the rules'
    // facts about it as a target (distance and range, sight, cover, long range) and whether it can be chosen, from the
    // same check as a click. Otherwise: who it is, its state, and the rules' distance from the unit shown.
    public static IReadOnlyList<string> PointingAt(MatchState state, Interaction interaction, ModelShape? unit, ModelShape? pointing)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(interaction);
        if (pointing is null || pointing.Id == unit?.Id || (interaction.IsTarget && pointing.Id == interaction.Target))
        {
            return [];
        }
        var lines = new List<string> { UnitPanelView.PointingPrefix + Heading(pointing) };
        if (!pointing.Yours && interaction.PreviewOf(pointing.Id) is { } preview)
        {
            lines.Add(PreviewCard.RulesHeading);
            lines.AddRange(preview.Facts.Where(PreviewCard.IsTargetFact).Select(f => f.TrimEnd('.')));
            // Line-of-sight stage L5: the hit roll's modifiers with their pages, and the guide's sight points in words.
            if (preview.Dice is { } dice && preview.Target is { } aimed)
            {
                lines.AddRange(dice.Lines(preview.Model, aimed).Where(l => l.StartsWith("Hit roll", StringComparison.Ordinal)));
            }
            if (preview.Kind == PreviewKind.Shoot && ShotGuide.Words(preview.Rays) is { } guide)
            {
                lines.Add(guide);
            }
            lines.Add(preview.Refusal is { } refused ? $"✕ {refused.Text}" : "✓ can be the target: click to choose it");
            return lines;
        }
        lines.Add(StatusLine(state, pointing));
        if (unit is not null && Previews.Gap(state, unit.Id, pointing.Id) is { } gap)
        {
            lines.Add($"{gap} from {ShortId(unit.Id)} (base to base)");
        }
        return lines;
    }

    // Proposal J2b: the enemy models the rules say this model is engaged with now (Rules.Core.Engagement.Pairs), with the
    // base-to-base gap; null when it is engaged with none.
    public static string? EngagedLine(MatchState state, ModelId model)
    {
        ArgumentNullException.ThrowIfNull(state);
        var enemies = Rules.Core.Engagement.Pairs(state).Where(p => p.One == model || p.Two == model).Select(p => p.One == model ? p.Two : p.One)
            .OrderBy(e => e.Value, StringComparer.Ordinal).ToList();
        return enemies.Count == 0 ? null
            : $"{EngagedPrefix}{string.Join(", ", enemies.Select(e => $"{ShortId(e)} ({Previews.Gap(state, model, e) ?? "?"})"))}";
    }

    public const string EngagedPrefix = "Engaged with: ";

    // Why a target cannot be chosen, in a few words (the full reason and page show once it is the previewed target).
    public static string ShortReason(Rules.Core.ReasonCode code) => code switch
    {
        Rules.Core.ReasonCode.TargetOutOfRange => "out of range",
        Rules.Core.ReasonCode.NoLineOfSight => "not in sight",
        Rules.Core.ReasonCode.TargetNotInMelee => "not within 1\"",
        Rules.Core.ReasonCode.ChargeRouteBlocked => "path blocked",
        Rules.Core.ReasonCode.AttackerEngaged or Rules.Core.ReasonCode.AlreadyEngaged => "you are in melee",
        Rules.Core.ReasonCode.NoRangedWeapon => "no ranged weapon",
        Rules.Core.ReasonCode.NoMeleeWeapon => "no melee weapon",
        Rules.Core.ReasonCode.ActionAlreadyTaken or Rules.Core.ReasonCode.ActionExcluded => "action not open",
        _ => "refused",
    };

    // The unit's state this turn, in words (the battlefield shows the same with a ring or a grey wash).
    // QoL H3: each starts with a mark (● activating, ◌ chosen, ✓ activated as on the battlefield, ○ can activate).
    private static string? State(ModelShape unit, Interaction interaction) =>
        unit.Active ? "● Activating now"
        : interaction.Chosen == unit.Id ? "◌ Chosen: not activated yet (nothing sent)"
        : unit.Activated ? $"{ModelShape.ActivatedMark} Activated this turn"
        : unit.Yours && interaction.CanChoose(unit.Id) ? "○ Can activate now"
        : unit.Eligible ? "○ Can still activate this turn"
        : null;

    // Why the selected unit cannot be given an order now, in one line; null when it can (or when nothing is due).
    private static string? Unavailable(MatchState state, BoardScene? scene, Interaction interaction, Seat? seat, ModelShape unit)
    {
        if (seat is null || state.Phase != Phase.Activation || state.Outcome is not null)
        {
            return null;
        }
        if (!unit.Yours)
        {
            return "Opponent's unit: it takes no orders from you.";
        }
        if (unit.Active || interaction.Chosen == unit.Id || interaction.CanChoose(unit.Id))
        {
            return null;
        }
        if (unit.Activated)
        {
            return "Already activated this turn.";
        }
        if (state.ActiveModel is { } active)
        {
            var name = scene?.Find(active) is { } other ? ShortId(other.Id) : active.Value.ToUpperInvariant();
            return $"Wait: {name} is activating.";
        }
        return "Not your turn to activate.";
    }
}
