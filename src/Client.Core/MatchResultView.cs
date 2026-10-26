using Rules.Core.State;

namespace Client.Core;

public enum ResultTone
{
    Won,
    Lost,
    Draw,
    SeatWins,
    Aborted,
}

// Battlefield UI 3, stage U6: the top of the end-of-match screen, large and coloured: who won ("YOU WON", "YOU LOST",
// "DRAW", or in hot-seat and for a watcher "SEAT A WINS"), the final scores by seat letter, and why the match ended,
// all from the server's MatchEnded outcome and final state. The technical details (summary, event count, final hash)
// are under Details.
public sealed record MatchResultView(string Headline, ResultTone Tone, string? Score, string Reason)
{
    // Glorious Deeds (U96): the deeds completed, by seat letter, under the score; null when none was.
    public string? Deeds { get; init; }

    public static MatchResultView? Of(MatchState state, Seat? you, bool hotSeat, MatchEnd? end = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (end is { Aborted: true })
        {
            return new("MATCH ABORTED", ResultTone.Aborted, null, end.Result);
        }
        if (state.Outcome is not { } outcome)
        {
            return null;
        }
        var (headline, tone) = outcome.Winner is not { } winner ? ("DRAW", ResultTone.Draw)
            : hotSeat || you is null ? ($"SEAT {Words.SeatLetter(winner)} WINS", ResultTone.SeatWins)
            : winner == you ? ("YOU WON", ResultTone.Won) : ("YOU LOST", ResultTone.Lost);
        var reason = Words.Reason(outcome.Reason);
        var score = state.Setup?.Scenario is null ? null : $"A {state.One.VictoryPoints} – B {state.Two.VictoryPoints}";
        var deeds = state.Deeds.Completed.IsEmpty ? null
            : $"Glorious Deeds: {string.Join(" · ", state.Deeds.Completed.Select(d => $"{Words.SeatLetter(d.Seat)} {Rules.Core.Deeds.Name(d.Deed)}"))}";
        return new(headline, tone, score, char.ToUpperInvariant(reason[0]) + reason[1..]) { Deeds = deeds };
    }
}
