using Client.Core;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// QoL H4: an Objective selected on the battlefield, in the panel: its compact mark, who controls it now by the rules' own
// test (never judged from the picture), the Victory Points it gives and the last scoring from the log.
public class ObjectivePanelTests
{
    [Fact]
    public void AnObjectiveInThePanelSaysWhatTheRulesSay()
    {
        var (controlled, nobody) = (0, 0);
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.Setup?.Scenario is { ObjectiveIds.Length: > 0 } && s.Turn > 0).Take(500))
        {
            var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
            var interaction = new Interaction();
            interaction.Update(state, Seat.One);
            foreach (var id in state.Setup!.Scenario!.ObjectiveIds)
            {
                var objective = scene.Objective(id)!;
                var panel = UnitPanel.Of(state, scene, interaction, Seat.One, null, objective: objective, lastScoring: "obj-1 to One");
                Assert.Equal($"◆{id[4..]} · Objective {id}", panel.Title);
                var now = Objectives.ControllerNow(state, id);
                Assert.Equal(now is { } seat ? $"Controlled now: {Words.Seat(seat)} [{Words.SeatLetter(seat)}] (if scored now)" : "Controlled now: nobody (if scored now)", panel.Unit[0]);
                Assert.Equal($"{Objectives.PointsPerObjective} VP to its controller at the end of each turn", panel.Unit[1]);
                Assert.Equal("Last scoring: obj-1 to One", panel.Unit[2]);
                Assert.Contains(UnitPanel.ObjectiveRule, panel.Details);
                Assert.Equal(now is null ? $"◆{id[4..]}" : $"◆{id[4..]} {Words.SeatLetter(now.Value)}", objective.MarkWithController);
                Assert.False(panel.CanConfirm);
                controlled += now is null ? 0 : 1;
                nobody += now is null ? 1 : 0;
            }
        }
        Assert.True(controlled > 50 && nobody > 50, $"{controlled} controlled, {nobody} by nobody");
    }

    [Fact]
    public void ASelectedUnitComesBeforeAnObjectiveAndNothingSaysNotScoredYetAfterAScoring()
    {
        var state = PreviewAgreementTests.States.Value.First(s => s.Setup?.Scenario is { ObjectiveIds.Length: > 0 } && s.Turn > 0);
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        var interaction = new Interaction();
        interaction.Update(state, Seat.One);
        var objective = scene.Terrain.First(t => t.Objective);
        var unit = scene.Models.First();
        Assert.Equal(UnitPanel.Heading(unit), UnitPanel.Of(state, scene, interaction, Seat.One, unit, objective: objective).Title);
        Assert.Contains("Not scored yet", UnitPanel.Of(state, scene, interaction, Seat.One, null, objective: objective).Unit);
    }
}
