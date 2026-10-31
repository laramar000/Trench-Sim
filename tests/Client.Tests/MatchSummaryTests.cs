using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// QoL I4: the match summary's numbers equal the rules' own: the VP of each turn's scoring add up to the final state's
// VP, models Down and Out of Action are the final state's, and the rolls are the server's dice with the rules' verdicts.
// Events sent twice (a rejoin) are counted once.
public class MatchSummaryTests
{
    [Fact]
    public void TheSummaryEqualsTheRulesNumbers()
    {
        var withRolls = 0;
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var state = MatchState.None;
            var tally = new MatchTally();
            var passed = new Dictionary<Seat, int>();
            var failed = new Dictionary<Seat, int>();
            var seq = 0;
            foreach (var e in log)
            {
                state = Reducer.Apply(state, e);
                tally.Add(++seq, e, state);
                if (DiceRolls.Of(e) is { Verdict: not RollVerdict.Neutral } roll)
                {
                    var seat = e is MoraleCheckRolled m ? m.Seat : state.FindModel(roll.Model!.Value)!.Seat;
                    var counts = roll.Verdict == RollVerdict.Fail ? failed : passed;
                    counts[seat] = counts.GetValueOrDefault(seat) + 1;
                }
            }
            var summary = tally.Summary(state);
            Assert.Equal((state.One.VictoryPoints, state.Two.VictoryPoints), summary.Vp);
            // Each turn's scoring, and 1 VP for each Glorious Deed (U96).
            Assert.Equal(state.Deeds.Completed, summary.Deeds);
            Assert.Equal(summary.Vp.One, summary.VpByTurn.Sum(t => t.One) + state.Deeds.Completed.Count(d => d.Seat == Seat.One));
            Assert.Equal(summary.Vp.Two, summary.VpByTurn.Sum(t => t.Two) + state.Deeds.Completed.Count(d => d.Seat == Seat.Two));
            Assert.Equal(state.Models.Count(m => m.Seat == Seat.One && m.Status == ModelStatus.Down), summary.Down.One);
            Assert.Equal(state.Models.Count(m => m.Seat == Seat.Two && m.Status == ModelStatus.OutOfAction), summary.OutOfAction.Two);
            Assert.Equal((passed.GetValueOrDefault(Seat.One), failed.GetValueOrDefault(Seat.One)), summary.RollsOne);
            Assert.Equal((passed.GetValueOrDefault(Seat.Two), failed.GetValueOrDefault(Seat.Two)), summary.RollsTwo);
            // Sent again from the start (a rejoin): the same numbers.
            seq = 0;
            foreach (var e in log)
            {
                tally.Add(++seq, e, state);
            }
            Assert.Equal(summary.Lines(), tally.Summary(state).Lines());
            withRolls += summary.RollsOne.Passed + summary.RollsTwo.Failed > 0 ? 1 : 0;
        }
        Assert.True(withRolls > 10, $"{withRolls} matches with rolls");
    }
}
