using Rules.Core.State;

namespace Client.Core;

// QoL stage C: one compact line over the battlefield, built only from what the screen already holds: the server's state
// (turn, phase, whose move, Victory Points), the models' turn states (BoardScene), the last scoring event in the log, the
// battlefield's selection and choice, and the command waiting for its answer. Nothing is predicted.
public static class StatusStrip
{
    public static string Of(MatchView view, BoardScene? scene, IReadOnlyList<LogLine> log, Seat? you, ModelShape? selected, ModelId? chosen,
        bool waiting)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        var parts = new List<string>();
        if (view.Turn.Length > 0)
        {
            parts.Add(view.Turn);
        }
        parts.Add(view.Phase);
        if (view.WhoseMove.Length > 0)
        {
            parts.Add(view.WhoseMove.TrimEnd('.'));
        }
        if (selected is not null)
        {
            parts.Add($"selected {selected.Id.Value}: {StateOf(selected, chosen)}");
        }
        if (scene is not null && view.Phase == Words.Phase(Phase.Activation) && you is { } me)
        {
            int Left(bool yours) => scene.Models.Count(m => m.Eligible && m.Yours == yours);
            parts.Add($"can still activate: you {Left(true)}, opponent {Left(false)}");
        }
        var vp = view.Seats.OrderByDescending(s => s.IsYou).Select(s => $"{(s.IsYou ? "you" : "opponent")} {s.VictoryPoints}").ToList();
        if (vp.Count == 2 && view.Turn.Length > 0)
        {
            parts.Add($"VP {string.Join(", ", vp)}");
        }
        if (log.LastOrDefault(l => l.Text.StartsWith("Turn ", StringComparison.Ordinal) && l.Text.Contains(" scoring: ", StringComparison.Ordinal)) is { } scoring)
        {
            parts.Add($"last scoring: {scoring.Text[(scoring.Text.IndexOf(" scoring: ", StringComparison.Ordinal) + 10)..].TrimEnd('.')}");
        }
        if (waiting)
        {
            parts.Add("command sent, waiting for the server");
        }
        return string.Join(" · ", parts);
    }

    private static string StateOf(ModelShape model, ModelId? chosen) =>
        model.Id == chosen ? "chosen, not activated yet"
        : model.Look switch
        {
            UnitLook.Active => "activating",
            UnitLook.Activated => "activated this turn",
            UnitLook.Eligible => "can activate",
            _ => model.Status == ModelStatus.Down ? "Down" : "waiting",
        } + (model.Status == ModelStatus.Down && model.Look != UnitLook.Waiting ? ", Down" : "");
}
