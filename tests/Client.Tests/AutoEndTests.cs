using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI 2, stage N2 (decision D3): when the rules leave the activating model nothing to do
// (Rules.Core.Activation.Finished), the battlefield says so and sends EndActivation once, AutoEndDelay seconds later,
// only for the seat acting and shown. Esc keeps the activation going; a refusal is shown, never sent again.
public class AutoEndTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static IEnumerable<(MatchState State, Seat Seat)> Finished() =>
        States.Where(Activation.Finished).Select(s => (s, s.FindModel(s.ActiveModel!.Value)!.Seat));

    private sealed class Recorder(FeedbackKind kind = FeedbackKind.Accepted)
    {
        public List<Command> Sent { get; } = [];

        public Task<Feedback> Send(Command command, string label)
        {
            Sent.Add(command);
            return Task.FromResult(new Feedback(kind, kind == FeedbackKind.Accepted ? "Done." : "Refused: the server says no."));
        }
    }

    [Fact]
    public void SimulatedMatchesHaveFinishedActivations()
    {
        Assert.True(Finished().Count() > 5, $"{Finished().Count()} finished activations");
    }

    [Fact]
    public async Task ExactlyOneEndIsSentForTheSeatActingAndOnlyAfterTheDelay()
    {
        foreach (var (state, seat) in Finished().Take(40))
        {
            var recorder = new Recorder();
            var interaction = new Interaction { Send = recorder.Send };
            interaction.Update(state, seat);
            Assert.Null(await interaction.TickAutoEndAsync(100));
            Assert.NotNull(interaction.AutoEndNote);
            Assert.Null(await interaction.TickAutoEndAsync(100 + Interaction.AutoEndDelay - 0.01));
            Assert.Empty(recorder.Sent);
            Assert.Equal(FeedbackKind.Accepted, (await interaction.TickAutoEndAsync(100 + Interaction.AutoEndDelay))!.Kind);
            Assert.Equal([new EndActivation(seat)], recorder.Sent);
            Assert.Null(interaction.AutoEndNote);
            Assert.Equal(1, interaction.AutoEndsSent);
            // The same state again (the server's events not in yet), later on: nothing more.
            interaction.Update(state with { }, seat);
            Assert.Null(await interaction.TickAutoEndAsync(110));
            Assert.Null(await interaction.TickAutoEndAsync(120));
            Assert.Single(recorder.Sent);

            // The other seat's window (or the other seat shown in hot-seat) never ends it.
            var other = new Recorder();
            var watching = new Interaction { Send = other.Send };
            watching.Update(state, seat.Opponent());
            Assert.Null(await watching.TickAutoEndAsync(0));
            Assert.Null(await watching.TickAutoEndAsync(10));
            Assert.Null(watching.AutoEndNote);
            Assert.Empty(other.Sent);
        }
    }

    [Fact]
    public async Task EscKeepsTheActivationGoing()
    {
        var (state, seat) = Finished().First();
        var recorder = new Recorder();
        var interaction = new Interaction { Send = recorder.Send };
        interaction.Update(state, seat);
        Assert.False(interaction.KeepActivation()); // no note yet
        await interaction.TickAutoEndAsync(0);
        Assert.True(interaction.KeepActivation());
        Assert.Null(interaction.AutoEndNote);
        Assert.Null(await interaction.TickAutoEndAsync(5));
        Assert.Null(await interaction.TickAutoEndAsync(50));
        Assert.Null(interaction.AutoEndNote);
        Assert.Empty(recorder.Sent);
        // The End activation button still ends it.
        var end = interaction.Bar(state.ActiveModel).Items.Single(i => i.Kind == BarKind.EndActivation);
        await interaction.SendCommandAsync(end.Command!, "End");
        Assert.Equal([new EndActivation(seat)], recorder.Sent);
    }

    [Fact]
    public async Task ARefusalIsShownAndNotRepeated()
    {
        var (state, seat) = Finished().First();
        var recorder = new Recorder(FeedbackKind.RulesRefused);
        var interaction = new Interaction { Send = recorder.Send };
        interaction.Update(state, seat);
        await interaction.TickAutoEndAsync(0);
        Assert.Equal(FeedbackKind.RulesRefused, (await interaction.TickAutoEndAsync(2))!.Kind);
        Assert.Contains("Refused: the server says no.", interaction.Notice, StringComparison.Ordinal);
        interaction.Update(state with { }, seat);
        Assert.Null(await interaction.TickAutoEndAsync(10));
        Assert.Null(await interaction.TickAutoEndAsync(20));
        Assert.Single(recorder.Sent);
    }

    [Fact]
    public async Task NothingWhileOffWhileACommandWaitsOrWhenTheRulesLeaveSomethingToDo()
    {
        var (state, seat) = Finished().First();
        var off = new Recorder();
        var disabled = new Interaction { Send = off.Send, AutoEnd = false };
        disabled.Update(state, seat);
        Assert.Null(await disabled.TickAutoEndAsync(0));
        Assert.Null(await disabled.TickAutoEndAsync(10));
        Assert.Null(disabled.AutoEndNote);
        Assert.Empty(off.Sent);

        // A command on its way: the server's answer comes first.
        var waiting = new TaskCompletionSource<Feedback>();
        var sent = new List<Command>();
        var busy = new Interaction { Send = (c, _) => { sent.Add(c); return waiting.Task; } };
        busy.Update(state, seat);
        var first = busy.SendCommandAsync(new EndActivation(seat), "End");
        Assert.NotNull(busy.Pending);
        Assert.Null(await busy.TickAutoEndAsync(0));
        Assert.Null(await busy.TickAutoEndAsync(10));
        Assert.Single(sent);
        waiting.SetResult(new Feedback(FeedbackKind.Accepted, "Done."));
        await first;

        // Every activation the rules have not finished: no note and nothing sent, however long.
        foreach (var open in States.Where(s => s.Phase == Phase.Activation && s.ActiveModel is not null && !Activation.Finished(s)).Take(200))
        {
            var recorder = new Recorder();
            var interaction = new Interaction { Send = recorder.Send };
            interaction.Update(open, open.FindModel(open.ActiveModel!.Value)!.Seat);
            Assert.Null(await interaction.TickAutoEndAsync(0));
            Assert.Null(await interaction.TickAutoEndAsync(30));
            Assert.Null(interaction.AutoEndNote);
            Assert.Empty(recorder.Sent);
        }
    }
}
