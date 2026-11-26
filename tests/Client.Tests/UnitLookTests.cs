using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// QoL stage A: each model's turn state on the battlefield (activating, activated, can still activate) comes from the
// server's state alone, through whole simulated matches. Only tests call the rules' TurnSequence.Decide.
public class UnitLookTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;
    private static readonly ContentNames Names = ContentNames.None;

    [Fact]
    public void EveryModelsLookIsTheServersState()
    {
        var (activated, eligible, down) = (0, 0, 0);
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null))
        {
            var scene = BoardScene.Of(state, Seat.One, Names)!;
            foreach (var shape in scene.Models)
            {
                var model = state.FindModel(shape.Id)!;
                Assert.Equal(model.ActivatedThisTurn, shape.Activated);
                Assert.Equal(state.ActiveModel == model.Id, shape.Active);
                Assert.Equal(model.Status, shape.Status);
                var expected = state.ActiveModel == model.Id ? UnitLook.Active
                    : model.ActivatedThisTurn ? UnitLook.Activated
                    : state.Phase == Phase.Activation && state.Outcome is null && model.Status != ModelStatus.OutOfAction ? UnitLook.Eligible
                    : UnitLook.Waiting;
                Assert.Equal(expected, shape.Look);
                // In words too: colour is never the only sign.
                if (shape.Look == UnitLook.Activated)
                {
                    Assert.Contains(", activated", shape.Label);
                    activated++;
                    down += shape.Status == ModelStatus.Down ? 1 : 0;
                }
                else
                {
                    Assert.DoesNotContain(", activated", shape.Label);
                }
                eligible += shape.Eligible ? 1 : 0;
            }
        }
        Assert.True(activated > 1000 && eligible > 1000 && down > 0, $"{activated} activated, {eligible} eligible, {down} activated and Down");
    }

    // For the seat to activate next, "can still activate" is exactly the set of units the rules offer.
    [Fact]
    public void EligibleUnitsAreTheOnesTheRulesOffer()
    {
        var checkedCount = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.ActiveModel is null && s.Pending is null && s.Setup?.Scenario is not null))
        {
            if (TurnSequence.NextActivator(state) is not { } seat)
            {
                continue;
            }
            var offered = TurnSequence.LegalCommands(state).OfType<ActivateModel>().Select(a => a.Model.Value).Order(StringComparer.Ordinal).ToList();
            var scene = BoardScene.Of(state, seat, Names)!;
            var shown = scene.Models.Where(m => m.Seat == seat && m.Eligible).Select(m => m.Id.Value).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(offered, shown);
            // Each of them is refused for the other seat: whose turn it is still belongs to the server.
            Assert.All(shown, id => Assert.False(TurnSequence.Decide(state, new ActivateModel(seat.Opponent(), new ModelId(id)), new SeededDice(1)).IsAccepted));
            checkedCount++;
        }
        Assert.True(checkedCount > 300, $"only {checkedCount} states");
    }

    // The activated look goes when the rules make the unit eligible again: at the next turn.
    [Fact]
    public void TheActivatedLookEndsWithTheTurn()
    {
        var resets = 0;
        for (var i = 1; i < States.Count; i++)
        {
            var (before, after) = (States[i - 1], States[i]);
            if (after.Setup?.Scenario is null || before.Setup != after.Setup || after.Turn != before.Turn + 1 || before.Turn == 0)
            {
                continue;
            }
            var was = BoardScene.Of(before, Seat.One, Names)!;
            var now = BoardScene.Of(after, Seat.One, Names)!;
            Assert.All(now.Models, m => Assert.NotEqual(UnitLook.Activated, m.Look));
            resets += was.Models.Any(m => m.Look == UnitLook.Activated) ? 1 : 0;
        }
        Assert.True(resets > 10, $"only {resets} turn changes with activated models");
    }

    // Both seats see the same turn state for every model (only "Yours" differs).
    [Fact]
    public void BothSeatsSeeTheSameLooks()
    {
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null).Take(3000))
        {
            var one = BoardScene.Of(state, Seat.One, Names)!.Models;
            var two = BoardScene.Of(state, Seat.Two, Names)!.Models;
            Assert.Equal(one.Select(m => (m.Id, m.Look, m.Label)), two.Select(m => (m.Id, m.Look, m.Label)));
        }
    }
}
