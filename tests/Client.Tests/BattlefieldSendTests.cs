using Client.Core;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// M7.4 stage 4c: whole matches played against a real in-process server, with every positional command (placing,
// Move, Dash, Retreat, Shoot, Charge, Fight) built on a battlefield Interaction and sent by confirming it, through the
// same MatchController as the hint buttons. The simulator's bot chooses what to do; the Interaction must build exactly
// that command from a cursor, waypoints or a target, send it once per confirmation, and report the server's answer.
// Non-positional choices (activate, end, decisions) go through the hint path as before.
public sealed partial class TransportTests
{
    [Theory]
    // Bot seeds whose matches (with the test server's seeded dice) use every positional command, Retreat included.
    [InlineData("melee", 10)]
    [InlineData("melee", 8)]
    [InlineData("ranged", 3)]
    public async Task TheBattlefieldSendsEveryPositionalCommandOfAWholeMatchThroughTheServer(string scenario, int seed)
    {
        // M7.5 stage 5a: this server keeps signed records, so the match played here can be checked against its record.
        var data = Path.Combine(_dir.FullName, $"records-{scenario}-{seed}");
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
        var accepted = new Dictionary<string, int>();
        var refused = 0;

        var bot = new Bot(new Random(seed));
        for (var round = 0; round < 5000 && one.Session!.Mirror.State.Phase != Phase.Ended; round++)
        {
            await Settle(server, code, one.Session!, two.Session!);
            var state = one.Session!.Mirror.State;
            if (state.Phase == Phase.Ended)
            {
                break;
            }
            List<Command> proposals;
            try
            {
                proposals = bot.Propose(state).ToList();
            }
            catch (InvalidOperationException)
            {
                proposals = [];
            }
            foreach (var command in proposals)
            {
                var seat = CommandSeats.Of(command)!.Value;
                var controller = seat == Seat.One ? one : two;
                await Until(controller, s => s.InputLocked is null);
                Feedback result;
                if (IsPositional(command))
                {
                    var board = boards[seat];
                    board.Update(controller.Snapshot().State, seat);
                    var before = sends.Count;
                    var preview = Build(board, command);
                    // A preview the server would refuse is sent only on a second confirmation.
                    var answer = await board.ConfirmAsync() ?? await board.ConfirmAsync();
                    Assert.NotNull(answer);
                    Assert.Equal(before + 1, sends.Count);
                    Assert.True(Same(command, sends[^1].Command), $"sent {sends[^1].Command}");
                    result = answer;
                    // The preview agreed with the server, and only the server's acceptance counts as done.
                    Assert.True(preview.Legal == (result.Kind == FeedbackKind.Accepted), $"{command}: preview {preview.Headline}; server {result.Kind} {result.Text}");
                    if (result.Kind == FeedbackKind.Accepted)
                    {
                        Assert.StartsWith("DONE: ", board.Notice);
                        Assert.Equal(InteractionMode.None, board.Mode);
                        accepted[command.GetType().Name] = accepted.GetValueOrDefault(command.GetType().Name) + 1;
                    }
                    else
                    {
                        Assert.Equal(FeedbackKind.RulesRefused, result.Kind);
                        Assert.StartsWith("REFUSED BY THE RULES: ", board.Notice);
                        refused++;
                        board.Cancel();
                    }
                }
                else
                {
                    var label = controller.Snapshot().Hints.FirstOrDefault(h => h.Command == command)?.Label ?? command.GetType().Name;
                    result = await controller.SendAsync(command, label, Token);
                }
                if (result.Kind == FeedbackKind.Accepted)
                {
                    break;
                }
                await Settle(server, code, one.Session!, two.Session!);
            }
        }
        await Settle(server, code, one.Session!, two.Session!);
        Assert.Equal(Phase.Ended, one.Session!.Mirror.State.Phase);
        AssertSameAsServer(server, code, one.Session!, two.Session!);

        Assert.Equal(8, accepted.GetValueOrDefault(nameof(PlaceModel)));
        Assert.True(refused > 0, "no refused command was sent and shown");
        string[] kinds = scenario == "melee"
            ? [nameof(MoveModel), nameof(DashModel), nameof(RetreatModel), nameof(Shoot), nameof(ChargeModel), nameof(FightModel)]
            : [nameof(MoveModel), nameof(DashModel), nameof(Shoot)];
        Assert.All(kinds, k => Assert.True(accepted.GetValueOrDefault(k) > 0, $"no accepted {k}: {string.Join(", ", accepted)}"));
        // Both screens show the result, final hash and event count, and they are the server's signed record's.
        AssertEndMatchesRecord(data, one, two, VerdictKind.Completed);
        Console.WriteLine($"{scenario} seed {seed}: accepted {string.Join(", ", accepted.OrderBy(a => a.Key).Select(a => $"{a.Key} {a.Value}"))}; refused {refused}");
    }

    private static bool IsPositional(Command command) =>
        command is PlaceModel or MoveModel or DashModel or RetreatModel or Shoot or ChargeModel or FightModel;

    // Builds the command on the battlefield as a player would: the action, then the model to place and the cursor, the
    // waypoints and the cursor, or the target. The preview must describe exactly the bot's command.
    private static Preview Build(Interaction board, Command command)
    {
        switch (command)
        {
            case PlaceModel p:
                Assert.True(board.Start(InteractionMode.Place), board.Notice);
                for (var i = 0; i < 8 && board.Actor != p.Model; i++)
                {
                    board.NextChoice();
                }
                Point(board, p.At);
                break;
            case MoveModel or DashModel or RetreatModel:
                var (mode, path) = command switch
                {
                    MoveModel m => (InteractionMode.Move, m.Path),
                    DashModel d => (InteractionMode.Dash, d.Path),
                    RetreatModel r => (InteractionMode.Retreat, r.Path),
                    _ => throw new InvalidOperationException(),
                };
                Assert.True(board.Start(mode), board.Notice);
                for (var i = 0; i < path.Length - 1; i++)
                {
                    Point(board, path[i]);
                    Assert.True(board.AddPoint());
                }
                Point(board, path[^1]);
                break;
            case Shoot or ChargeModel or FightModel:
                var (targetMode, target) = command switch
                {
                    Shoot s => (InteractionMode.Shoot, s.Target),
                    ChargeModel c => (InteractionMode.Charge, c.Target),
                    FightModel f => (InteractionMode.Fight, f.Target),
                    _ => throw new InvalidOperationException(),
                };
                Assert.True(board.Start(targetMode), board.Notice);
                board.SetTarget(target);
                break;
        }
        var preview = board.Preview!;
        Assert.True(Same(command, preview.Command), $"built {preview.Command}, wanted {command}");
        return preview;
    }

    // Commands with a path hold an ImmutableArray, which records compare by reference; compare the points instead.
    private static bool Same(Command a, Command b) => (a, b) switch
    {
        (MoveModel x, MoveModel y) => x with { Path = default } == y with { Path = default } && x.Path.SequenceEqual(y.Path),
        (DashModel x, DashModel y) => x with { Path = default } == y with { Path = default } && x.Path.SequenceEqual(y.Path),
        (RetreatModel x, RetreatModel y) => x with { Path = default } == y with { Path = default } && x.Path.SequenceEqual(y.Path),
        _ => a == b,
    };

    private static void Point(Interaction board, Rules.Core.Geometry.Point2 at)
    {
        var p = TablePoint.Of(at);
        board.SetCursor(p.X, p.Y);
    }
}
