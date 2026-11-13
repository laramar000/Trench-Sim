using Client.Core;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// QoL I5: the battlefield description carries exactly the enemy pairs the rules say are engaged now
// (Rules.Core.Engagement.Pairs), over the states of the simulated matches; melee matches give some.
public class MeleeLinkTests
{
    [Fact]
    public void TheSceneLinksAreTheRulesEngagedPairs()
    {
        var engaged = 0;
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.Setup?.Scenario is not null))
        {
            var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
            var pairs = Engagement.Pairs(state);
            Assert.Equal(pairs, scene.Links);
            Assert.All(scene.Links, l => Assert.True(scene.Find(l.One) is { Seat: Seat.One } && scene.Find(l.Two) is { Seat: Seat.Two }));
            engaged += pairs.Count > 0 ? 1 : 0;
        }
        Assert.True(engaged > 20, $"{engaged} states with engaged models");
    }
}
