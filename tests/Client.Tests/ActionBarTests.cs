using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// The action bar near the selected model (XCOM-style controls): it offers only the actions the advisory preview checks
// say the model can take now. Those checks must agree with the server: an action left off the bar is one the server
// would refuse for every command tried, and a target action is offered exactly when some enemy would be accepted.
// Only tests call TurnSequence.Decide; the client never does (ClientAuthorityTest).
public class ActionBarTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static IEnumerable<MatchState> ActivationStates() =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)?.Position is not null);

    private static bool Accepted(MatchState state, Command command) => TurnSequence.Decide(state, command, new SeededDice(1)).IsAccepted;

    [Fact]
    public void AnActionLeftOffTheBarIsOneTheServerRefuses()
    {
        var random = new Random(11);
        var (open, closed) = (new Dictionary<PreviewKind, int>(), new Dictionary<PreviewKind, int>());
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var at = TablePoint.Of(active.Position!.Value);
            foreach (var kind in new[] { PreviewKind.Move, PreviewKind.Dash, PreviewKind.Retreat })
            {
                var verdict = Previews.ActionOpen(state, active.Seat, active.Id, kind);
                Count(verdict is null ? open : closed, kind);
                if (verdict is null)
                {
                    continue;
                }
                // Short steps in every direction and a few random paths: the server refuses all of them, and for the
                // first reason the bar gives when it is about the action itself (not the path).
                var paths = new List<List<Point2>>();
                foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0), (2.5, 2.5), (-2.5, -2.5) })
                {
                    paths.Add([Previews.PointAt(Math.Clamp(at.X + dx, 0, 36), Math.Clamp(at.Y + dy, 0, 36))]);
                }
                for (var i = 0; i < 4; i++)
                {
                    paths.Add([Previews.PointAt(Math.Clamp(at.X + random.Next(-12, 13) * 0.5, 0, 36), Math.Clamp(at.Y + random.Next(-12, 13) * 0.5, 0, 36))]);
                }
                foreach (var path in paths)
                {
                    var command = Previews.Path(state, active.Seat, kind, active.Id, path).Command;
                    var decided = TurnSequence.Decide(state, command, new SeededDice(1));
                    Assert.False(decided.IsAccepted, $"{kind} left off the bar ({verdict.Text}) but the server accepted {command}");
                    Assert.Equal(verdict.Code, decided.Rejection!.Code);
                }
            }
        }
        foreach (var kind in new[] { PreviewKind.Move, PreviewKind.Dash, PreviewKind.Retreat })
        {
            Assert.True(open.GetValueOrDefault(kind) > 0, $"{kind} was never offered in the sample");
            Assert.True(closed.GetValueOrDefault(kind) > 0, $"{kind} was never left off in the sample");
        }
    }

    [Fact]
    public void ATargetActionIsOnTheBarExactlyWhenTheServerAcceptsSomeTarget()
    {
        var (open, closed) = (new Dictionary<PreviewKind, int>(), new Dictionary<PreviewKind, int>());
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Charge, PreviewKind.Fight })
            {
                var verdict = Previews.ActionOpen(state, active.Seat, active.Id, kind);
                var someAccepted = state.Models
                    .Any(target => Accepted(state, Previews.Target(state, active.Seat, kind, active.Id, target.Id).Command));
                Assert.True(someAccepted == (verdict is null), $"{kind}: bar {(verdict is null ? "offers it" : $"leaves it off ({verdict.Text})")}, server accepts {(someAccepted ? "a" : "no")} target");
                Count(verdict is null ? open : closed, kind);
            }
        }
        foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Charge, PreviewKind.Fight })
        {
            Assert.True(open.GetValueOrDefault(kind) > 0, $"{kind} was never offered in the sample");
            Assert.True(closed.GetValueOrDefault(kind) > 0, $"{kind} was never left off in the sample");
        }
    }

    [Fact]
    public void TheOpponentAndOtherMomentsGetNoActions()
    {
        foreach (var state in ActivationStates().Take(50))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            Assert.NotNull(Previews.ActionOpen(state, active.Seat.Opponent(), active.Id, PreviewKind.Move));
        }
        var deploying = States.First(s => s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null);
        Assert.Equal(ReasonCode.WrongPhase, Previews.ActionOpen(deploying, Seat.One, deploying.Models[0].Id, PreviewKind.Move)!.Code);
    }

    [Fact]
    public void TheBarOfTheActingModelHoldsItsOfferedActionsAndEndActivation()
    {
        var state = ActivationStates().First(s => s.Actions == ActionsTaken.None);
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var interaction = new Interaction();
        interaction.Update(state, seat);

        var bar = interaction.Bar(null);
        Assert.Equal(state.ActiveModel, bar.Anchor);
        var starts = bar.Items.Where(i => i.Kind == BarKind.Start).Select(i => i.Mode).ToList();
        Assert.Equal(interaction.Offered, starts);
        Assert.All(interaction.Available.Except(interaction.Offered), m => Assert.NotNull(interaction.WhyNot(m)));
        Assert.All(interaction.Offered, m => Assert.Null(interaction.WhyNot(m)));
        var end = Assert.Single(bar.Items, i => i.Kind == BarKind.EndActivation);
        Assert.Equal(TurnSequence.LegalCommands(state).OfType<EndActivation>().Single(), end.Command);
        Assert.All(bar.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Tip)));

        // The opponent sees no bar for that model.
        interaction.Update(state, seat.Opponent());
        Assert.Empty(interaction.Bar(state.ActiveModel).Items);
    }

    [Fact]
    public void ChoosingAnActionTurnsTheBarIntoSendUndoAndCancel()
    {
        var state = ActivationStates().First(s => s.Actions == ActionsTaken.None
            && Previews.ActionOpen(s, s.FindModel(s.ActiveModel!.Value)!.Seat, s.ActiveModel!.Value, PreviewKind.Move) is null);
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Move));

        var bar = interaction.Bar(null);
        Assert.Equal([BarKind.Confirm, BarKind.Cancel], bar.Items.Select(i => i.Kind));
        Assert.Equal("Move here", bar.Items[0].Label);

        var at = TablePoint.Of(state.FindModel(state.ActiveModel!.Value)!.Position!.Value);
        Assert.False(interaction.ClickAt(Math.Clamp(at.X + 1, 0, 36), at.Y));
        Assert.Equal([BarKind.Confirm, BarKind.Undo, BarKind.Cancel], interaction.Bar(null).Items.Select(i => i.Kind));
        Assert.NotNull(interaction.Reach());

        // A click on the last point again asks to send; a click elsewhere adds a point instead.
        Assert.True(interaction.ClickAt(Math.Clamp(at.X + 1, 0, 36) + Interaction.SameSpot / 2, at.Y));
        Assert.False(interaction.ClickAt(Math.Clamp(at.X - 1, 0, 36), at.Y));

        interaction.Cancel();
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Contains(interaction.Bar(null).Items, i => i.Kind == BarKind.Start);
    }

    [Fact]
    public void PlacingAPointNeedsASecondClickOnTheSameSpot()
    {
        var state = States.First(s => s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null && MatchView.NextDeployer(s) is not null);
        var interaction = new Interaction();
        interaction.Update(state, MatchView.NextDeployer(state)!.Value);
        Assert.Equal("Place a model", Assert.Single(interaction.Bar(null).Items).Label);
        Assert.True(interaction.Start(InteractionMode.Place));
        Assert.Null(interaction.Bar(null).Anchor);
        Assert.False(interaction.ClickAt(10, 2));
        Assert.Equal(new TablePoint(10, 2), interaction.Cursor);
        Assert.True(interaction.ClickAt(10.1, 2.1));
        Assert.False(interaction.ClickAt(14, 2));
    }

    [Fact]
    public void ActivateIsOfferedForASelectedModelThatMayActivate()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is null
            && TurnSequence.LegalCommands(s).OfType<ActivateModel>().Any());
        var activate = TurnSequence.LegalCommands(state).OfType<ActivateModel>().First();
        var interaction = new Interaction();
        interaction.Update(state, activate.Seat);
        var bar = interaction.Bar(activate.Model);
        var item = Assert.Single(bar.Items);
        Assert.Equal(BarKind.Activate, item.Kind);
        Assert.Equal("Activate", item.Label);
        Assert.Equal(activate, item.Command);
        Assert.Equal(activate.Model, bar.Anchor);

        // Nothing for an enemy model, nor with no model selected.
        var enemy = state.Models.First(m => m.Seat != activate.Seat).Id;
        Assert.Empty(interaction.Bar(enemy).Items);
        Assert.Empty(interaction.Bar(null).Items);
    }

    private static void Count(Dictionary<PreviewKind, int> counts, PreviewKind kind) => counts[kind] = counts.GetValueOrDefault(kind) + 1;
}
