using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Rules.Tests;

// QoL I5: Engagement.Pairs, the read-only door the client draws melee links from, agrees with the rules' own engaged
// test (Board.IsEngaged) after every event of the simulated melee matches, and lists each enemy pair once, from Seat One to Seat Two.
public class EngagementPairsTests
{
    [Fact]
    public void ThePairsAreExactlyTheModelsTheRulesSayAreEngaged()
    {
        var states = 0;
        var withPairs = 0;
        var warbands = Content.Load(Content.MeleePath());
        for (var seed = 1; seed <= 10; seed++)
        {
            var state = MatchState.None;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                state = Reducer.Apply(state, e);
                var pairs = Engagement.Pairs(state);
                states++;
                withPairs += pairs.Count > 0 ? 1 : 0;
                Assert.Equal(pairs.Count, pairs.Distinct().Count());
                foreach (var pair in pairs)
                {
                    var one = state.FindModel(pair.One)!;
                    var two = state.FindModel(pair.Two)!;
                    Assert.Equal(Seat.One, one.Seat);
                    Assert.Equal(Seat.Two, two.Seat);
                    Assert.True(Measure.IsWithin(Board.BodyOf(state, one)!, Board.BodyOf(state, two)!, Board.EngagementRange));
                }
                foreach (var model in Board.OnTable(state))
                {
                    Assert.Equal(Board.IsEngaged(state, model), pairs.Any(p => p.One == model.Id || p.Two == model.Id));
                }
            }
        }
        Assert.True(withPairs > 20, $"{withPairs} of {states} states had engaged models");
    }

    [Fact]
    public void AMatchWithoutASetupHasNoPairs() => Assert.Empty(Engagement.Pairs(MatchState.None));
}
