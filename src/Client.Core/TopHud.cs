using Rules.Core.State;

namespace Client.Core;

// Proposal G, step G3: the one line above the battlefield (it replaces the QoL stage C status strip). Which seat this
// window plays, whose move it is in words, the turn and phase, Victory Points, who controls each objective now (QoL H4),
// and a command waiting for the server's answer. Built only from the server's state, the log and the battlefield's scene; nothing is predicted. The selected unit is in the panel.
public static class TopHud
{
    public const string Waiting = "SENT · waiting for the server";

    public static string Of(MatchView view, BoardScene? scene, IReadOnlyList<LogLine> log, Seat? you, bool waiting, string? prompt = null) =>
        string.Join(" · ", Pieces(view, scene, log, you, waiting, prompt).Select(p => p.Text));

    // Where a piece of the HUD is shown. Battlefield UI 3, stage U2: the top-centre holds only whose move it is and each
    // seat's Victory Points; what happens now (the placing prompt, the model activating, a command waiting) is one smaller
    // line under the top-left block, where the match stands (the seat, the turn and phase, the objectives now).
    public enum Place { Left, Status, Centre }

    // The three places together are Of, word for word.
    public sealed record Places(string Left, string Status, string Centre);

    public static Places Split(MatchView view, BoardScene? scene, IReadOnlyList<LogLine> log, Seat? you, bool waiting, string? prompt = null)
    {
        var pieces = Pieces(view, scene, log, you, waiting, prompt);
        string At(Place place) => string.Join(" · ", pieces.Where(p => p.Place == place).Select(p => p.Text));
        return new(At(Place.Left), At(Place.Status), At(Place.Centre));
    }

    // Stage U2: each seat's Victory Points by seat letter, Seat One first in both windows ("VP A 3 – B 1"), once the
    // match has a turn; else null.
    public static string? Score(MatchView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Seats.Count == 2 && view.Turn.Length > 0
            ? $"VP {string.Join(" – ", view.Seats.OrderBy(s => s.Seat).Select(s => $"{Words.SeatLetter(s.Seat)} {s.VictoryPoints}"))}"
            : null;
    }

    private static List<(string Text, Place Place)> Pieces(MatchView view, BoardScene? scene, IReadOnlyList<LogLine> log, Seat? you, bool waiting, string? prompt)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        var parts = new List<(string Text, Place Place)>
        {
            (you is { } me ? $"[{Words.SeatLetter(me)}] {Words.Seat(me).ToUpperInvariant()}" : "WATCHING", Place.Left),
            (Move(view), Place.Centre),
        };
        if (view.Turn.Length > 0)
        {
            parts.Add((view.Turn, Place.Left));
        }
        parts.Add((view.Phase, Place.Left));
        // Stage K4: what the battlefield does now while you place your models ("Place ... in ZONE A: click a spot").
        if (prompt is { Length: > 0 })
        {
            parts.Add((prompt, Place.Status));
        }
        // Stage K3: the model activating now and what it has done.
        if (view.ActivatingShort is { } activating)
        {
            parts.Add((activating, Place.Status));
        }
        // Stage U2: the scores by seat letter; the count of units left to activate is gone (each unit's look and J say it).
        if (Score(view) is { } score)
        {
            parts.Add((score, Place.Centre));
        }
        // QoL H4: who would control each Objective if it were scored now, by the rules' own test (decision D1); the last
        // scoring is in an objective's panel.
        if (scene?.Terrain.Where(t => t.Objective).OrderBy(t => t.Id, StringComparer.Ordinal).ToList() is { Count: > 0 } objectives)
        {
            parts.Add(($"objectives now: {string.Join(" ", objectives.Select(o => $"{o.Mark} {(o.Controller is { } c ? Words.SeatLetter(c) : "–")}"))}", Place.Left));
        }
        if (waiting)
        {
            parts.Add((Waiting, Place.Status));
        }
        return [.. parts.Where(p => p.Text.Length > 0)];
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
