using Rules.Core.State;

namespace Client.Core;

// Proposal G, step G3: the one line above the battlefield (it replaces the QoL stage C status strip). Which seat this
// window plays, whose move it is in words, the turn and phase, Victory Points, how many units each side can still
// activate, the last scoring of the objectives, and a command waiting for the server's answer. Built only from the
// server's state, the log and the battlefield's scene; nothing is predicted. The selected unit is in the panel.
public static class TopHud
{
    public const string Waiting = "SENT · waiting for the server";

    public static string Of(MatchView view, BoardScene? scene, IReadOnlyList<LogLine> log, Seat? you, bool waiting)
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
        if (log.LastOrDefault(l => l.Text.StartsWith("Turn ", StringComparison.Ordinal) && l.Text.Contains(" scoring: ", StringComparison.Ordinal)) is { } scoring)
        {
            parts.Add($"objectives: {scoring.Text[(scoring.Text.IndexOf(" scoring: ", StringComparison.Ordinal) + 10)..].TrimEnd('.')}");
        }
        if (waiting)
        {
            parts.Add(Waiting);
        }
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    // "YOUR MOVE" or "OPPONENT'S MOVE" in words (the HUD's colour only repeats it), else what the match is waiting for.
    private static string Move(MatchView view) =>
        view.YourMove ? "YOUR MOVE"
        : view.WhoseMove.StartsWith("Opponent's move", StringComparison.Ordinal) ? "OPPONENT'S MOVE"
        : view.WhoseMove.TrimEnd('.');
}
