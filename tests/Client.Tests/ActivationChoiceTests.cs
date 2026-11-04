using Client.Core;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Choosing a unit to activate and switching to another before it acts. Nothing is sent until the first action, which
// sends ActivateModel and then the action; the server checks both as always. Only tests call TurnSequence.Decide.
public class ActivationChoiceTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    // States where the next player may activate one of several units.
    private static IEnumerable<(MatchState State, Seat Seat, List<ModelId> Units)> Choosing() =>
        States.Where(s => s.Phase == Phase.Activation && s.ActiveModel is null && s.Pending is null)
            .Select(s => (s, Activations: TurnSequence.LegalCommands(s).OfType<ActivateModel>().ToList()))
            .Where(x => x.Activations.Count >= 2)
            .Select(x => (x.s, x.Activations[0].Seat, x.Activations.Select(a => a.Model).ToList()));

    private sealed class Recorder
    {
        public List<Command> Sent { get; } = [];
        public Func<Command, FeedbackKind> Answer { get; set; } = _ => FeedbackKind.Accepted;
        public Task<Feedback> Send(Command command, string label)
        {
            Sent.Add(command);
            var kind = Answer(command);
            return Task.FromResult(new Feedback(kind, kind == FeedbackKind.Accepted ? "Done." : "Refused."));
        }
    }

    // The preview-only state of a chosen unit is exactly the server's state after ActivateModel, Down units included.
    [Fact]
    public void AsIfActivatedIsTheServersStateAfterTheActivation()
    {
        var (checkedCount, down) = (0, 0);
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.ActiveModel is null && s.Pending is null))
        {
            foreach (var activate in TurnSequence.LegalCommands(state).OfType<ActivateModel>())
            {
                var decided = TurnSequence.Decide(state, activate, new SeededDice(1));
                Assert.True(decided.IsAccepted);
                var view = Previews.AsIfActivated(state, activate.Seat, activate.Model);
                Assert.NotNull(view);
                Assert.Equal(StateHash.Compute(decided.State!), StateHash.Compute(view));
                checkedCount++;
                down += state.FindModel(activate.Model)!.Status == ModelStatus.Down ? 1 : 0;
            }
            // Every unit the rules do not offer gets no preview state, and the server refuses its activation.
            foreach (var model in state.Models)
            {
                foreach (var seat in new[] { Seat.One, Seat.Two })
                {
                    var command = new ActivateModel(seat, model.Id);
                    if (!TurnSequence.LegalCommands(state).Contains(command))
                    {
                        Assert.Null(Previews.AsIfActivated(state, seat, model.Id));
                        Assert.False(TurnSequence.Decide(state, command, new SeededDice(1)).IsAccepted);
                    }
                }
            }
        }
        Assert.True(checkedCount > 500, $"only {checkedCount} activations checked");
        Assert.True(down > 0, "no Down model in the sample");
    }

    [Fact]
    public void SwitchingBeforeAnyActionSendsNothing()
    {
        var (state, seat, units) = Choosing().First();
        var recorder = new Recorder();
        var interaction = new Interaction { Send = recorder.Send };
        interaction.Update(state, seat);

        Assert.True(interaction.Choose(units[0]));
        Assert.Equal(units[0], interaction.Chosen);
        Assert.Equal(units[0], interaction.Bar(units[0]).Anchor);
        Assert.Contains(interaction.Bar(units[0]).Items, i => i.Kind == BarKind.Note && i.Label == "Not activated yet");
        // Previewing works on the chosen unit: Move starts and its preview is for that unit.
        if (interaction.Offered.Contains(InteractionMode.Move))
        {
            Assert.True(interaction.Start(InteractionMode.Move));
            Assert.Equal(units[0], interaction.Actor);
        }

        Assert.True(interaction.Choose(units[1]));
        Assert.Equal(units[1], interaction.Chosen);
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Equal(units[1], interaction.Bar(units[1]).Anchor);
        Assert.Empty(recorder.Sent);

        // Dropping the choice sends nothing either, and the bar is gone.
        Assert.True(interaction.Choose(null));
        Assert.Null(interaction.Chosen);
        Assert.Empty(interaction.Bar(null).Items);
        Assert.Empty(recorder.Sent);
    }

    [Fact]
    public async Task TheFirstActionSendsTheActivationThenTheAction()
    {
        var (state, seat, units) = Choosing().First(c => c.Units.Any(u =>
            Previews.AsIfActivated(c.State, c.Seat, u) is { } v && Previews.ActionOpen(v, c.Seat, u, PreviewKind.Move) is null));
        var unit = units.First(u => Previews.ActionOpen(Previews.AsIfActivated(state, seat, u)!, seat, u, PreviewKind.Move) is null);
        var view = Previews.AsIfActivated(state, seat, unit)!;
        var order = new List<string>();
        var first = new TaskCompletionSource<Feedback>();
        var interaction = new Interaction
        {
            Send = (command, _) =>
            {
                order.Add(command.GetType().Name);
                return command is ActivateModel ? first.Task : Task.FromResult(new Feedback(FeedbackKind.Accepted, "Done."));
            },
        };
        interaction.Update(state, seat);
        Assert.True(interaction.Choose(units.First(u => u != unit)));
        Assert.True(interaction.Choose(unit));
        Assert.True(interaction.Start(InteractionMode.Move));
        var step = Previews.Reachable(view, seat, PreviewKind.Move, unit, []).First();
        Assert.Equal(ClickResult.Send, interaction.ClickAt(step.X, step.Y));
        var move = interaction.Preview!.Command;

        var sending = interaction.ConfirmAsync();
        // The action waits for the server's answer to the activation.
        Assert.Equal([nameof(ActivateModel)], order);
        Assert.Equal(new ActivateModel(seat, unit), interaction.Pending);
        first.SetResult(new Feedback(FeedbackKind.Accepted, "Done."));
        var answer = await sending;
        Assert.Equal(FeedbackKind.Accepted, answer!.Kind);
        Assert.Equal([nameof(ActivateModel), nameof(MoveModel)], order);
        Assert.Null(interaction.Chosen);
        // Both commands are what the server accepts, one after the other.
        var activated = TurnSequence.Decide(state, new ActivateModel(seat, unit), new SeededDice(1));
        Assert.True(TurnSequence.Decide(activated.State!, move, new SeededDice(1)).IsAccepted);
    }

    [Fact]
    public async Task ARefusedActivationSendsNothingMore()
    {
        var (state, seat, units) = Choosing().First();
        var recorder = new Recorder { Answer = c => c is ActivateModel ? FeedbackKind.RulesRefused : FeedbackKind.Accepted };
        var interaction = new Interaction { Send = recorder.Send };
        interaction.Update(state, seat);
        interaction.Choose(units[0]);
        var end = interaction.Bar(units[0]).Items.Single(i => i.Kind == BarKind.EndActivation);
        Assert.Equal("Take no action", end.Label);
        await interaction.SendCommandAsync(end.Command!, end.Label);
        Assert.Equal([new ActivateModel(seat, units[0])], recorder.Sent);
        Assert.StartsWith("REFUSED BY THE RULES", interaction.Notice);
    }

    [Fact]
    public async Task TakeNoActionSendsTheActivationThenItsEnd()
    {
        var (state, seat, units) = Choosing().First();
        var recorder = new Recorder();
        var interaction = new Interaction { Send = recorder.Send };
        interaction.Update(state, seat);
        interaction.Choose(units[1]);
        var end = interaction.Bar(units[1]).Items.Single(i => i.Kind == BarKind.EndActivation);
        await interaction.SendCommandAsync(end.Command!, end.Label);
        Assert.Equal<Command>([new ActivateModel(seat, units[1]), new EndActivation(seat)], recorder.Sent);
        var activated = TurnSequence.Decide(state, recorder.Sent[0], new SeededDice(1));
        Assert.True(TurnSequence.Decide(activated.State!, recorder.Sent[1], new SeededDice(1)).IsAccepted);
    }

    // Once the activation is on the server (an action taken, or activated with the hint or key A), no switch.
    [Fact]
    public void AfterTheActivationHasStartedNoSwitchIsOffered()
    {
        var checkedCount = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.ActiveModel is { } a && s.Pending is null).Take(400))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var other = state.Models.FirstOrDefault(m => m.Seat == active.Seat && m.Id != active.Id && m.Deployed && m.Status != ModelStatus.OutOfAction && !m.ActivatedThisTurn);
            if (other is null)
            {
                continue;
            }
            var interaction = new Interaction();
            interaction.Update(state, active.Seat);
            Assert.False(interaction.CanChoose(other.Id));
            Assert.False(interaction.Choose(other.Id));
            Assert.Null(interaction.Chosen);
            Assert.Contains("cannot switch", interaction.Notice);
            // The server refuses it too, as before (pinned).
            var refused = TurnSequence.Decide(state, new ActivateModel(active.Seat, other.Id), new SeededDice(1));
            Assert.Equal(ReasonCode.ActivationInProgress, refused.Rejection!.Code);
            checkedCount++;
            if (state.Actions != ActionsTaken.None)
            {
                Assert.NotEqual(other.Id, interaction.Bar(other.Id).Anchor);
            }
        }
        Assert.True(checkedCount > 20, $"only {checkedCount} states");
    }

    [Fact]
    public void AnIneligibleUnitCannotBeChosen()
    {
        var reasons = new HashSet<ReasonCode>();
        foreach (var (state, seat, units) in Choosing().Take(200))
        {
            var interaction = new Interaction();
            interaction.Update(state, seat);
            foreach (var model in state.Models.Where(m => !units.Contains(m.Id)))
            {
                Assert.False(interaction.CanChoose(model.Id));
                Assert.False(interaction.Choose(model.Id));
                Assert.Null(interaction.Chosen);
                var refused = TurnSequence.Decide(state, new ActivateModel(seat, model.Id), new SeededDice(1));
                Assert.False(refused.IsAccepted);
                reasons.Add(refused.Rejection!.Code);
            }
            // The other player chooses nothing while it is not their turn.
            interaction.Update(state, seat.Opponent());
            Assert.All(units, u => Assert.False(interaction.Choose(u)));
            Assert.Equal(ReasonCode.NotYourTurn, TurnSequence.Decide(state, new ActivateModel(seat.Opponent(), state.Models.First(m => m.Seat != seat).Id), new SeededDice(1)).Rejection!.Code);
        }
        Assert.Contains(ReasonCode.NotYourModel, reasons);
        Assert.Contains(ReasonCode.ModelAlreadyActivated, reasons);
    }
}
