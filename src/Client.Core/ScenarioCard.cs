using Rules.Core;
using Rules.Core.State;

namespace Client.Core;

// Proposal J, stage J3b: the Scenario card (button "Scenario (G)" or key G). Its words are Rules.Core.ScenarioRules (stage
// J3a); each objective row and the PREVIEW line use the rules' own control test (Objectives.ControllerNow) and points
// (decision D3). Nothing is scored here: the server's Victory Points stay in the HUD, and the PREVIEW line says what the
// end-of-turn scoring would give if the turn ended now.
public sealed record ScenarioRow(string ObjectiveId, string Mark, Seat? Controller, string Text);

public sealed record ScenarioCardView(string Title, IReadOnlyList<string> Lines, IReadOnlyList<ScenarioRow> Rows, string? Projection)
{
    public string Text => string.Join("\n", Lines.Concat(Rows.Select(r => r.Text)).Concat(Projection is null ? [] : [Projection]));
}

public static class ScenarioCard
{
    public const string Title = "Scenario";
    public const string NotAvailable = "The rules for this scenario are not available in this build.";
    public const string PreviewPrefix = "PREVIEW: if the turn ended now: ";

    public static ScenarioCardView Of(MatchState state, Seat? you)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ScenarioRules.Of(state.Setup) is not { } rules)
        {
            return new ScenarioCardView(Title, [NotAvailable], [], null);
        }
        var lines = new List<string> { $"{rules.Name} · {rules.Pages}", "", "SCORING" };
        lines.AddRange(rules.Scoring.Select(s => "  " + s));
        lines.Add("HOW THE MATCH ENDS");
        lines.AddRange(rules.MatchEnds.Select(s => "  " + s));
        lines.Add("PROVISIONAL READINGS");
        lines.AddRange(rules.Provisional.Select(s => "  " + s));
        if (state.Setup!.Scenario is { } scenario && scenario.Battlefield.Terrain.Where(t => t.IsTower).ToList() is { Count: > 0 } towers)
        {
            lines.Add("TOWERS");
            lines.AddRange(TowerLines(towers).Select(s => "  " + s));
        }
        if (state.Setup!.Scenario is { } field && field.Battlefield.TrenchDepth is { } depth)
        {
            lines.Add("TRENCHES");
            lines.AddRange(TrenchLines(field.Battlefield.Terrain.Count(t => t.IsTrench), depth).Select(s => "  " + s));
        }
        lines.Add("OBJECTIVES (who controls each now; click one to centre on it)");
        var rows = rules.ObjectiveIds.Order(StringComparer.Ordinal).Select(id =>
        {
            var controller = Objectives.ControllerNow(state, id);
            var mark = $"{TerrainShape.ObjectiveMark}{(id.StartsWith("obj-", StringComparison.OrdinalIgnoreCase) ? id[4..] : id)}";
            var who = controller is { } c ? $"{Words.Seat(c)}{(c == you ? " (you)" : "")}" : "nobody";
            return new ScenarioRow(id, mark, controller, $"{mark} {id}: {who}");
        }).ToList();
        return new ScenarioCardView(Title, lines, rows, Projection(state, rows, rules.PointsPerObjective, you));
    }

    // "PREVIEW: if the turn ended now: you +2, opponent +1" (or by seat for a watcher). Only during a turn of a match in
    // progress; the server's scoring at the end of the turn is the one that counts.
    private static string? Projection(MatchState state, IReadOnlyList<ScenarioRow> rows, int points, Seat? you)
    {
        if (state.Turn < 1 || state.Outcome is not null)
        {
            return null;
        }
        int Of(Seat seat) => rows.Count(r => r.Controller == seat) * points;
        return you is { } me
            ? $"{PreviewPrefix}you +{Of(me)}, opponent +{Of(Other(me))}"
            : $"{PreviewPrefix}{Words.Seat(Seat.One)} +{Of(Seat.One)}, {Words.Seat(Seat.Two)} +{Of(Seat.Two)}";
    }

    private static Seat Other(Seat seat) => seat == Seat.One ? Seat.Two : Seat.One;

    // Towers proposal, stage T4: what the towers allow, in the rules' own numbers and with their provisional readings.
    // Trench-maps stage S3: what the trenches do, in words, with their rulings.
    public static IReadOnlyList<string> TrenchLines(int count, Rules.Core.Geometry.Length depth)
    {
        var deep = BoardScene.Inch(TableRect.Inches(depth));
        return
        [
            $"{count} trench section{(count == 1 ? "" : "s")}, {deep} deep. Their sides are crossed as open ground: no extra cost, no roll (p. 62, U80).",
            $"A model more than half in a trench stands on its floor, {deep} below the ground (U81). Distances are measured on the table, so models fight across the edge (U82).",
            "Seen from far off, a model in a trench may show only its head: the trench's edge hides the rest and gives it cover while it is partly seen (p. 38, U83).",
        ];
    }

    public static IReadOnlyList<string> TowerLines(IReadOnlyList<Rules.Core.Geometry.TerrainBlock> towers)
    {
        ArgumentNullException.ThrowIfNull(towers);
        var height = BoardScene.Inch(TableRect.Inches(towers[0].Climb!.Height));
        return
        [
            $"{towers.Count} tower{(towers.Count == 1 ? "" : "s")}, {height} high ({string.Join(", ", towers.Select(t => t.Id))}). Models stand and fight on a platform, wholly on it (U67).",
            $"A platform is reached and left by its ladders, from the foot straight to the top or back; a climb costs {height} of movement on top of the step (U66, U71).",
            "Or climb straight up or down a side (key K in a Move or Dash): a Risky Success Roll; on a Failure the model stops where the climb starts and its activation ends (p. 39, U75, U76).",
            "Distances between heights are measured in 3D, so no model on a tower is within 1\" of one on the ground (U70).",
            "No charges up or down a ladder in this version (U72). A model on a tower never counts for an Objective (U74).",
            "Sight and cover as before: a tower blocks sight between ground models; a model on it sees over ground walls (U73).",
        ];
    }
}
