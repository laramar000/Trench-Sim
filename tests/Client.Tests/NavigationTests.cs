using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// QoL H2: J goes through exactly the units the rules offer to activate (yours, in a fixed order, wrapping round), and a
// double-click finds the Objective piece under the pointer. Navigation only: nothing is sent.
public class NavigationTests
{
    [Fact]
    public void JGoesThroughExactlyTheUnitsTheRulesOfferToActivate()
    {
        var states = PreviewAgreementTests.States.Value.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.Outcome is null && s.ActiveModel is null).Take(200).ToList();
        var (checkedStates, several) = (0, 0);
        foreach (var state in states)
        {
            var seat = TurnSequence.LegalCommands(state).OfType<ActivateModel>().FirstOrDefault()?.Seat ?? Seat.One;
            var scene = BoardScene.Of(state, seat, ContentNames.None)!;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            var offered = TurnSequence.LegalCommands(state).OfType<ActivateModel>().Select(a => a.Model).OrderBy(m => m.Value, StringComparer.Ordinal).ToList();
            var visited = new List<ModelId>();
            ModelId? at = null;
            for (var i = 0; i < offered.Count; i++)
            {
                at = scene.NextEligible(at, interaction.CanChoose)!.Id;
                visited.Add(at.Value);
            }
            Assert.Equal(offered, visited);
            if (offered.Count > 0)
            {
                // It wraps round, and Shift+J goes back.
                Assert.Equal(offered[0], scene.NextEligible(at, interaction.CanChoose)!.Id);
                Assert.Equal(offered[^1], scene.NextEligible(offered[0], interaction.CanChoose, forward: false)!.Id);
            }
            else
            {
                Assert.Null(scene.NextEligible(null, interaction.CanChoose));
            }
            checkedStates++;
            several += offered.Count > 1 ? 1 : 0;
        }
        Assert.True(checkedStates > 50 && several > 20, $"{checkedStates} states, {several} with several units to choose from");
    }

    [Fact]
    public void TheObjectiveUnderAPointIsFoundAndNothingElseIs()
    {
        var state = PreviewAgreementTests.States.Value.First(s => s.Setup?.Scenario is { ObjectiveIds.Length: > 0 });
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        var objectives = scene.Terrain.Where(t => t.Objective).ToList();
        Assert.Equal(state.Setup!.Scenario!.ObjectiveIds.Order(StringComparer.Ordinal), objectives.Select(o => o.Id).Order(StringComparer.Ordinal));
        foreach (var piece in objectives)
        {
            Assert.Equal(piece, scene.ObjectiveAt(piece.Area.CentreX, piece.Area.CentreY));
            Assert.Equal(piece, scene.Objective(piece.Id));
        }
        foreach (var other in scene.Terrain.Where(t => !t.Objective && !objectives.Any(o => o.Area.Contains(t.Area.CentreX, t.Area.CentreY))))
        {
            Assert.Null(scene.ObjectiveAt(other.Area.CentreX, other.Area.CentreY));
        }
    }
}
