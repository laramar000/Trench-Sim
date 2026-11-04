using Client.Core;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Choosing a unit, switching to another, then acting, in whole matches against a real in-process server. Every
// activation the bot wants is chosen on the battlefield after first choosing a different unit; choosing and switching
// must send nothing and log no event, and the first action must send ActivateModel and then the action. Both clients
// end on the server's hash and the signed record verifies.
public sealed partial class TransportTests
{
    [Theory]
    [InlineData("melee", 10)]
    [InlineData("ranged", 3)]
    public async Task ChoosingAndSwitchingUnitsSendsNothingAndTheMatchStaysInStep(string scenario, int seed)
    {
        var data = Path.Combine(_dir.FullName, $"choice-{scenario}-{seed}");
        var server = await Start(TimeSpan.FromMinutes(5), data: data);
        var (one, two, code) = await ControllerPair(scenario, server);
        var sends = new List<(Seat Seat, Command Command)>();
        Interaction Board(MatchController controller, Seat seat) => new()
        {
            Send = (command, label) =>
            {
                sends.Add((seat, command));
                return controller.SendAsync(command, label, Token);
            },
        };
        var boards = new Dictionary<Seat, Interaction> { [Seat.One] = Board(one, Seat.One), [Seat.Two] = Board(two, Seat.Two) };
        var (switches, commits, takeNoAction) = (0, 0, 0);

        var bot = new Bot(new Random(seed));
        for (var round = 0; round < 5000 && one.Session!.Mirror.State.Phase != Phase.Ended; round++)
        {
            await Settle(server, code, one.Session!, two.Session!);
            var state = one.Session!.Mirror.State;
            if (state.Phase == Phase.Ended)
            {
                break;
            }
            // While a unit is only chosen, the bot plays it on the preview state; the server still has no activation.
            var chosenSeat = boards.Where(b => b.Value.Chosen is not null).Select(b => (Seat?)b.Key).FirstOrDefault();
            var botState = chosenSeat is { } cs ? Previews.AsIfActivated(state, cs, boards[cs].Chosen!.Value)! : state;
            List<Command> proposals;
            try
            {
                proposals = bot.Propose(botState).ToList();
            }
            catch (InvalidOperationException)
            {
                proposals = [];
            }
            foreach (var command in proposals)
            {
                var seat = CommandSeats.Of(command)!.Value;
                var controller = seat == Seat.One ? one : two;
                var board = boards[seat];
                await Until(controller, s => s.InputLocked is null);
                board.Update(controller.Snapshot().State, seat);
                if (command is ActivateModel activate && board.CanChoose(activate.Model))
                {
                    var (events, sent) = (server.Lobby.LogOf(code)!.Count, sends.Count);
                    var decoy = TurnSequence.LegalCommands(state).OfType<ActivateModel>().Select(a => (ModelId?)a.Model)
                        .FirstOrDefault(m => m != activate.Model);
                    if (decoy is { } d)
                    {
                        Assert.True(board.Choose(d), board.Notice);
                        switches++;
                    }
                    Assert.True(board.Choose(activate.Model), board.Notice);
                    Assert.Equal(activate.Model, board.Chosen);
                    // Nothing sent, no event, no change on either client.
                    Assert.Equal(sent, sends.Count);
                    Assert.Equal(events, server.Lobby.LogOf(code)!.Count);
                    AssertSameAsServer(server, code, one.Session!, two.Session!);
                    break;
                }
                var chosen = board.Chosen;
                var before = sends.Count;
                Feedback? result;
                if (IsPositional(command))
                {
                    var preview = Build(board, command);
                    result = await board.ConfirmAsync() ?? await board.ConfirmAsync();
                    Assert.NotNull(result);
                    Assert.True(Same(command, sends[^1].Command), $"sent {sends[^1].Command}");
                    Assert.True(preview.Legal == (result.Kind == FeedbackKind.Accepted), $"{command}: preview {preview.Headline}; server {result.Kind} {result.Text}");
                    if (result.Kind != FeedbackKind.Accepted)
                    {
                        board.Cancel();
                    }
                }
                else if (chosen is not null)
                {
                    result = await board.SendCommandAsync(command, command.GetType().Name);
                    Assert.NotNull(result);
                    takeNoAction += command is EndActivation ? 1 : 0;
                }
                else
                {
                    var label = controller.Snapshot().Hints.FirstOrDefault(h => h.Command == command)?.Label ?? command.GetType().Name;
                    result = await controller.SendAsync(command, label, Token);
                }
                if (chosen is { } c)
                {
                    // The first action sent the activation of the chosen unit, then the action itself.
                    Assert.Equal(before + 2, sends.Count);
                    Assert.Equal(new ActivateModel(seat, c), sends[^2].Command);
                    Assert.Null(board.Chosen);
                    commits++;
                }
                else
                {
                    Assert.Equal(before + (IsPositional(command) ? 1 : 0), sends.Count);
                }
                if (result!.Kind == FeedbackKind.Accepted)
                {
                    break;
                }
                await Settle(server, code, one.Session!, two.Session!);
            }
        }
        await Settle(server, code, one.Session!, two.Session!);
        Assert.Equal(Phase.Ended, one.Session!.Mirror.State.Phase);
        AssertSameAsServer(server, code, one.Session!, two.Session!);
        Assert.True(switches > 10, $"only {switches} switches");
        Assert.True(commits > 10, $"only {commits} commits");
        // Every ActivationStarted on the server is for a unit that was finally chosen, never for a decoy.
        var started = server.Lobby.LogOf(code)!.Count(e => e.Event is Rules.Core.Events.ActivationStarted);
        Assert.Equal(commits, started);
        AssertEndMatchesRecord(data, one, two, VerdictKind.Completed);
        // Each screen's event list: the server's events in order, in the narrator's words, each with its kind.
        foreach (var screen in new[] { one, two })
        {
            var shown = screen.Snapshot().Log;
            var events = server.Lobby.LogOf(code)!.TakeLast(shown.Count).ToList();
            Assert.Equal(events.Select(e => e.Seq), shown.Select(l => l.Seq));
            Assert.Equal(events.Select(e => (EventText.Line(e.Event), EventText.Kind(e.Event))), shown.Select(l => (l.Text, l.Kind)));
        }
        Console.WriteLine($"{scenario} seed {seed}: {switches} switches, {commits} commits ({takeNoAction} with no action), {started} activations");
    }
}
