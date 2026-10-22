using Client.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI 3, stage U6: the top of the end-of-match screen says the winner, the final scores and the reason
// exactly as the server's MatchEnded event and final state have them, for every simulated ending and every viewer.
public class MatchResultViewTests
{
    [Fact]
    public void WinnerScoresAndReasonAreTheServersForEveryEnding()
    {
        var (endings, reasons) = (0, new HashSet<OutcomeReason>());
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var state = MatchState.None;
            foreach (var e in log)
            {
                state = Rules.Core.Reducer.Apply(state, e);
            }
            var ended = log.OfType<MatchEnded>().Single();
            Assert.Equal(log[^1], ended);
            Assert.Null(MatchResultView.Of(MatchState.None, Seat.One, false));
            foreach (var (you, hotSeat) in new (Seat?, bool)[] { (Seat.One, false), (Seat.Two, false), (Seat.One, true), (null, false) })
            {
                var view = MatchResultView.Of(state, you, hotSeat)!;
                var expected = ended.Winner is not { } w ? "DRAW" : hotSeat || you is null ? $"SEAT {Words.SeatLetter(w)} WINS" : w == you ? "YOU WON" : "YOU LOST";
                Assert.Equal(expected, view.Headline);
                Assert.Equal(ended.Winner is null ? ResultTone.Draw : hotSeat || you is null ? ResultTone.SeatWins : ended.Winner == you ? ResultTone.Won : ResultTone.Lost, view.Tone);
                Assert.Equal($"A {state.One.VictoryPoints} – B {state.Two.VictoryPoints}", view.Score);
                Assert.Equal(Words.Reason(ended.Reason), view.Reason, StringComparer.OrdinalIgnoreCase);
            }
            reasons.Add(ended.Reason);
            endings++;
        }
        Assert.True(endings > 50 && reasons.Count >= 2, $"{endings} endings, reasons {string.Join(", ", reasons)}");
    }

    [Fact]
    public void AnAbortedMatchSaysSoWithNoScore()
    {
        var view = MatchResultView.Of(MatchState.None, Seat.One, false, new MatchEnd(true, "Match aborted (server lost it).", 3, new string('a', 64), null))!;
        Assert.Equal("MATCH ABORTED", view.Headline);
        Assert.Equal(ResultTone.Aborted, view.Tone);
        Assert.Null(view.Score);
        Assert.Equal("Match aborted (server lost it).", view.Reason);
    }
}
