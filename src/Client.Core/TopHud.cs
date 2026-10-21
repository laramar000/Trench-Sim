using Rules.Core.State;

namespace Client.Core;

// Proposal G, step G3: the one line above the battlefield (it replaces the QoL stage C status strip). Which seat this
// window plays, whose move it is in words, the turn and phase, Victory Points, how many units each side can still
// activate, who controls each objective now (QoL H4), and a command waiting for the server's answer. Built only from the
// server's state, the log and the battlefield's scene; nothing is predicted. The selected unit is in the panel.
public static class TopHud
{
    public const string Waiting = "SENT · waiting for the server";

    public static string Of(MatchView view, BoardScene? scene, IReadOnlyList<LogLine> log, Seat? you, bool waiting, string? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        var parts = new List<string>
        {
            you is { } me ? $"[{Words.SeatLetter(me)}] {Words.Seat(me).ToUpperInvariant()}" : "WATCHING",
            Move(view),
        };
        if (view.Turn.Length > 0)
        {
            parts.Add(view.Turn);
        }
        parts.Add(view.Phase);
        // Stage K4: what the battlefield does now while you place your models ("Place ... in ZONE A: click a spot").
        if (prompt is { Length: > 0 })
        {
            parts.Add(prompt);
        }
        // Stage K3: the model activating now and what it has done.
        if (view.ActivatingShort is { } activating)
        {
            parts.Add(activating);
        }
        var vp = view.Seats.OrderByDescending(s => s.IsYou).Select(s => $"{(s.IsYou ? "you" : "opponent")} {s.VictoryPoints}").ToList();
        if (vp.Count == 2 && view.Turn.Length > 0)
        {
            parts.Add($"VP {string.Join(", ", vp)}");
        }
        if (scene is not null && view.Phase == Words.Phase(Phase.Activation) && you is not null)
        {
            int Left(bool yours) => scene.Models.Count(m => m.Eligible && m.Yours == yours);
            parts.Add($"to activate: you {Left(true)}, opponent {Left(false)}");
        }
        // QoL H4: who would control each Objective if it were scored now, by the rules' own test (decision D1); the last
        // scoring is in an objective's panel.
        if (scene?.Terrain.Where(t => t.Objective).OrderBy(t => t.Id, StringComparer.Ordinal).ToList() is { Count: > 0 } objectives)
        {
            parts.Add($"objectives now: {string.Join(" ", objectives.Select(o => $"{o.Mark} {(o.Controller is { } c ? Words.SeatLetter(c) : "–")}"))}");
        }
        if (waiting)
        {
            parts.Add(Waiting);
        }
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    // The last end-of-turn scoring of the Objectives as the combat log has it ("obj-1 to One, obj-2 to nobody"), or null.
    public static string? LastScoring(IReadOnlyList<LogLine> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.LastOrDefault(l => l.Text.StartsWith("Turn ", StringComparison.Ordinal) && l.Text.Contains(" scoring: ", StringComparison.Ordinal)) is { } scoring
            ? scoring.Text[(scoring.Text.IndexOf(" scoring: ", StringComparison.Ordinal) + 10)..].TrimEnd('.')
            : null;
    }

    // "YOUR MOVE" or "OPPONENT'S MOVE" in words (the HUD's colour only repeats it), else what the match is waiting for.
    private static string Move(MatchView view) =>
        view.YourMove ? "YOUR MOVE"
        : view.WhoseMove.StartsWith("Opponent's move", StringComparison.Ordinal) ? "OPPONENT'S MOVE"
        : view.WhoseMove.TrimEnd('.');
}
