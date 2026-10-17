using System.Diagnostics;
using Client.Core;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Single-player (hot-seat) mode: one window holds both seats of a match on the authoritative server. Every command goes
// through the shown seat's own MatchController, the server checks it like any other, and switching never moves a
// command or an answer to the other seat. The local server process is started and stopped with nothing left behind.
public sealed partial class TransportTests
{
    private MatchController HotController(Uri endpoint) => new(new MatchControllerOptions
    {
        Endpoint = endpoint,
        Policy = Quick,
        StaleWait = TimeSpan.FromSeconds(5),
        LogLines = 100_000,
    });

    // Both seats on the in-process test server (its dice are seeded, so matches repeat).
    private async Task<HotSeat> StartHotSeat(string scenario = "melee")
    {
        var hot = await HotSeat.StartAsync(() => HotController(_server.Endpoint), scenario, null, Token);
        await Until(hot.One, s => s.Seat == Seat.One && s.Code is not null);
        await Until(hot.Two, s => s.Seat == Seat.Two);
        await Settle(_server, hot.One.Snapshot().Code!, hot.One.Session!, hot.Two.Session!);
        await Until(hot.One, s => s.InputLocked is null || !s.View.YourMove);
        await Until(hot.Two, s => s.InputLocked is null || !s.View.YourMove);
        return hot;
    }

    [Fact]
    public async Task HotSeatHoldsBothSeatsAndSwitchesTheShownSeat()
    {
        await using var hot = await StartHotSeat();
        Assert.Equal(Seat.One, hot.Active);
        Assert.Same(hot.One, hot.Controller);
        Assert.Equal(hot.One.Snapshot().Code, hot.Two.Snapshot().Code);
        Assert.Contains("you control Seat One [A]", hot.Banner());

        // Following the turn shows the seat that must act, only when the shown one has nothing to do.
        var mover = hot.One.Snapshot().View.YourMove ? Seat.One : Seat.Two;
        Assert.True(hot.Two.Snapshot().View.YourMove == (mover == Seat.Two));
        Assert.Equal(mover != Seat.One, hot.FollowIfNeeded());
        Assert.Equal(mover, hot.Active);
        Assert.False(hot.FollowIfNeeded());

        // A switch by hand (F2) shows the other seat and is kept until the match moves on.
        var switched = 0;
        hot.Switched += () => switched++;
        Assert.True(hot.Switch());
        Assert.Equal(mover.Opponent(), hot.Active);
        Assert.Equal(mover.Opponent(), hot.Controller.Snapshot().Seat);
        Assert.Contains($"you control {Words.Seat(mover.Opponent())} [{Words.SeatLetter(mover.Opponent())}]", hot.Banner());
        Assert.Contains($"{Words.Seat(mover)} to move.", hot.Banner());
        Assert.False(hot.FollowIfNeeded());
        Assert.Equal(mover.Opponent(), hot.Active);
        Assert.False(hot.Switch(mover.Opponent()));
        Assert.True(hot.Switch(mover));
        Assert.Same(mover == Seat.One ? hot.One : hot.Two, hot.Controller);
        Assert.Equal(2, switched);

        // With following off, the window never switches on its own, even when the match moves on.
        hot.Switch();
        hot.FollowTurn = false;
        var hint = (mover == Seat.One ? hot.One : hot.Two).Snapshot().Hints[0];
        Assert.Equal(FeedbackKind.Accepted, (await (mover == Seat.One ? hot.One : hot.Two).SendAsync(hint.Command, hint.Label, Token)).Kind);
        await Settle(_server, hot.One.Snapshot().Code!, hot.One.Session!, hot.Two.Session!);
        Assert.False(hot.FollowIfNeeded());
        Assert.Equal(mover.Opponent(), hot.Active);
        Assert.Contains("switching is by hand", hot.Banner());
    }

    [Fact]
    public async Task HotSeatSendsEachCommandAsTheShownSeatAndNeverSwitchesWhileOneIsOnItsWay()
    {
        await using var hot = await StartHotSeat();
        hot.FollowIfNeeded();
        var mover = hot.Active;
        var hint = hot.Controller.Snapshot().Hints[0];
        Assert.Equal(mover, CommandSeats.Of(hint.Command));

        // While the command waits for its answer the seat cannot be switched (checked on the sending thread, before the
        // answer can arrive).
        var thread = Environment.CurrentManagedThreadId;
        bool? switchedWhileSending = null;
        string? blocked = null;
        void OnChanged()
        {
            if (Environment.CurrentManagedThreadId == thread && switchedWhileSending is null)
            {
                switchedWhileSending = hot.Switch();
                blocked = hot.SwitchBlocked();
            }
        }
        var controller = hot.Controller;
        controller.Changed += OnChanged;
        var sending = controller.SendAsync(hint.Command, hint.Label, Token);
        controller.Changed -= OnChanged;
        Assert.False(switchedWhileSending);
        Assert.StartsWith("Waiting for the server's answer", blocked);
        Assert.Equal(mover, hot.Active);
        Assert.Equal(FeedbackKind.Accepted, (await sending).Kind);

        // The battlefield's own send counts too.
        var busy = true;
        hot.Busy = () => busy;
        Assert.False(hot.Switch());
        busy = false;
        Assert.True(hot.Switch());

        // A command for the seat that is not shown, sent through the shown seat's connection, is refused by the server.
        await Settle(_server, hot.One.Snapshot().Code!, hot.One.Session!, hot.Two.Session!);
        var shown = hot.Active;
        var wrong = new EndActivation(shown.Opponent());
        var refused = await hot.Controller.SendAsync(wrong, "End activation", Token);
        Assert.Equal(FeedbackKind.ProtocolRefused, refused.Kind);
        Assert.Contains(nameof(ProtocolError.SeatMismatch), refused.Text);
    }

    // A whole match played by one person: the window follows the turn, every placement, move, shot, charge and fight
    // is built and confirmed on the battlefield of the shown seat, other choices are its hint buttons, and both seats
    // end on the server's hash.
    [Theory]
    [InlineData("melee", 10)]
    [InlineData("ranged", 3)]
    public async Task AWholeHotSeatMatchIsPlayedFromOneWindowThroughTheServer(string scenario, int seed)
    {
        await using var hot = await StartHotSeat(scenario);
        var code = hot.One.Snapshot().Code!;
        var board = new Interaction();
        board.Send = (command, label) => hot.Controller.SendAsync(command, label, Token);
        hot.Busy = () => board.Pending is not null;
        var bySeat = new Dictionary<Seat, int> { [Seat.One] = 0, [Seat.Two] = 0 };
        var switches = 0;
        hot.Switched += () => switches++;
        var bot = new Bot(new Random(seed));
        for (var round = 0; round < 5000 && hot.One.Session!.Mirror.State.Phase != Phase.Ended; round++)
        {
            await Settle(_server, code, hot.One.Session!, hot.Two.Session!);
            var state = hot.One.Session!.Mirror.State;
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
                await Until(hot.Controller, s => s.InputLocked is null || !s.View.YourMove);
                hot.FollowIfNeeded();
                var seat = CommandSeats.Of(command)!.Value;
                // The shown seat is the one that must act: the window followed the turn.
                Assert.Equal(seat, hot.Active);
                await Until(hot.Controller, s => s.InputLocked is null);
                Feedback result;
                if (IsPositional(command))
                {
                    board.Update(hot.Controller.Snapshot().State, hot.Active);
                    var preview = Build(board, command);
                    result = (await board.ConfirmAsync() ?? await board.ConfirmAsync())!;
                    Assert.True(preview.Legal == (result.Kind == FeedbackKind.Accepted), $"{command}: {preview.Headline}; {result.Kind} {result.Text}");
                    if (result.Kind != FeedbackKind.Accepted)
                    {
                        board.Cancel();
                    }
                }
                else
                {
                    var label = hot.Controller.Snapshot().Hints.FirstOrDefault(h => h.Command == command)?.Label ?? command.GetType().Name;
                    result = await hot.Controller.SendAsync(command, label, Token);
                }
                Assert.NotEqual(FeedbackKind.ProtocolRefused, result.Kind);
                if (result.Kind == FeedbackKind.Accepted)
                {
                    bySeat[seat]++;
                    break;
                }
                await Settle(_server, code, hot.One.Session!, hot.Two.Session!);
            }
        }
        await Settle(_server, code, hot.One.Session!, hot.Two.Session!);
        Assert.Equal(Phase.Ended, hot.One.Session!.Mirror.State.Phase);
        AssertSameAsServer(_server, code, hot.One.Session!, hot.Two.Session!);
        Assert.True(bySeat[Seat.One] > 0 && bySeat[Seat.Two] > 0, $"accepted: {bySeat[Seat.One]} and {bySeat[Seat.Two]}");
        Assert.True(switches > 2, $"{switches} switches");
        Assert.NotNull(hot.Controller.Snapshot().View.Outcome);
    }

    // The real server program, started as a separate process the way the Godot client starts it, and stopped when the
    // hot-seat session ends: nothing is left running.
    [Fact]
    public async Task TheLocalServerProcessServesAHotSeatMatchAndStopsWithIt()
    {
        var dll = ServerDll();
        var data = Path.Combine(_dir.FullName, "hotseat-server");
        var server = await LocalServer.StartBuiltAsync(dll, data, cancellationToken: Token);
        var pid = server.ProcessId;
        Assert.Equal("127.0.0.1", server.Endpoint.Host);
        Assert.NotEqual(0, server.Endpoint.Port);
        var hot = await HotSeat.StartAsync(() => HotController(server.Endpoint), "ranged", server, Token);
        await Until(hot.Two, s => s.Seat == Seat.Two && s.View.Phase != Words.Phase(Phase.PreGame));
        Assert.Equal(hot.One.Snapshot().Code, hot.Two.Snapshot().Code);
        Assert.Same(server, hot.Server);

        await hot.DisposeAsync();
        Assert.True(server.HasExited || Gone(pid));
        Assert.True(Gone(pid), $"server process {pid} still running");
        // The data folder is free again: a new server can start on it.
        await using var again = await LocalServer.StartBuiltAsync(dll, data, cancellationToken: Token);
        Assert.False(again.HasExited);
    }

    // Closing the window cannot await: StopServerNow stops the process at once, and the seats' connections then close.
    [Fact]
    public async Task ClosingTheWindowStopsTheLocalServerAtOnce()
    {
        var server = await LocalServer.StartBuiltAsync(ServerDll(), Path.Combine(_dir.FullName, "hotseat-close"), cancellationToken: Token);
        var pid = server.ProcessId;
        var hot = await HotSeat.StartAsync(() => HotController(server.Endpoint), "melee", server, Token);
        hot.StopServerNow();
        Assert.True(server.HasExited);
        Assert.True(Gone(pid), $"server process {pid} still running");
        await Until(hot.One, s => s.Connection != ConnectionState.Connected);
        await hot.DisposeAsync();
    }

    [Fact]
    public async Task ALocalServerThatCannotStartIsReportedAndLeavesNothingRunning()
    {
        var data = Path.Combine(_dir.FullName, "hotseat-locked");
        await using var first = await LocalServer.StartBuiltAsync(ServerDll(), data, cancellationToken: Token);
        // The same data folder is locked by the first server: the second stops at once and says why.
        var failure = await Assert.ThrowsAsync<LocalServerException>(() => LocalServer.StartBuiltAsync(ServerDll(), data, cancellationToken: Token));
        Assert.Contains("Another server is using", failure.Message);
        await Assert.ThrowsAsync<LocalServerException>(() => LocalServer.StartBuiltAsync(Path.Combine(_dir.FullName, "missing.dll"), data, cancellationToken: Token));
        Assert.Equal(Repository.Root, LocalServer.FindRepository(Path.Combine(Repository.Root, "client", "scripts")));
    }

    private static bool Gone(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    // The repository's own build of the server, in the same configuration as these tests.
    private static string ServerDll()
    {
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        var dll = Path.Combine(Repository.Root, "src", "Match.Server", "bin", configuration, "net10.0", "Match.Server.dll");
        Assert.True(File.Exists(dll), $"build the solution first: {dll}");
        return dll;
    }
}
