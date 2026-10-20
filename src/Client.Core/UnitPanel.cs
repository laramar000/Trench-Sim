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
                details.Add($"Injury modifiers of its attacks: ranged {Signed(p.RangedInjuryModifier)}, melee {Signed(p.MeleeInjuryModifier)}.");
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
        var markers = state.FindModel(unit.Id)?.BloodMarkers ?? 0;
        return $"{Words.Seat(unit.Seat)} [{unit.SeatLetter}]{(unit.Yours ? " (yours)" : "")} · {(unit.Status == ModelStatus.Down ? $"{ModelShape.DownMark} " : "")}{Words.Status(unit.Status)}" +
            (markers > 0 ? $" · {ModelShape.MarkerMark}{markers} Blood Marker{(markers == 1 ? "" : "s")}" : "");
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
        return $"Move {move}{halved} · {range} · Armour {p.Armour} · {(p.HasMeleeWeapon ? "Melee weapon" : "No melee weapon")}";
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
            lines.AddRange(preview.Facts.Where(PreviewCard.IsTargetFact).Select(f => f.TrimEnd('.')));
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
