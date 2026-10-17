using Client.Core;
using Match.Client;
using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// M7.3: the match controller behind the Godot match screen, against a real in-process server. It shows only what the
// server's events built, sends one command at a time and reports the answer, follows the connection through
// Reconnector, and handles Stale refusals without reconnecting on its own.
public sealed partial class TransportTests
{
    private readonly List<MatchController> _controllers = [];

    private MatchController Controller(MatchServer? server = null, ReconnectPolicy? policy = null, TimeSpan? staleWait = null,
        Func<Uri, MatchMirror?, CancellationToken, Task<ClientSession>>? connect = null)
    {
        var controller = new MatchController(new MatchControllerOptions
        {
            Endpoint = (server ?? _server).Endpoint,
            Store = Sessions,
            Policy = policy ?? Quick,
            StaleWait = staleWait ?? TimeSpan.FromSeconds(5),
            LogLines = 100_000,
            Connect = connect ?? (async (uri, mirror, ct) =>
            {
                var session = await ClientSession.ConnectAsync(uri, ct, mirror: mirror);
                lock (_clients)
                {
                    _clients.Add(session);
                }
                return session;
            }),
        });
        _controllers.Add(controller);
        return controller;
    }

    // Waits, without polling, until the controller's screen satisfies the condition.
    private async Task<ScreenState> Until(MatchController controller, Func<ScreenState, bool> condition)
    {
        while (true)
        {
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler() => changed.TrySetResult();
            controller.Changed += Handler;
            try
            {
                var screen = controller.Snapshot();
                if (condition(screen))
                {
                    return screen;
                }
                await changed.Task.WaitAsync(Token);
            }
            finally
            {
                controller.Changed -= Handler;
            }
        }
    }

    private async Task<(MatchController One, MatchController Two, string Code)> ControllerPair(string scenario = "melee", MatchServer? server = null,
        ReconnectPolicy? policy = null, TimeSpan? staleWait = null)
    {
        server ??= _server;
        var one = Controller(server, policy, staleWait);
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync(scenario, Token));
        var two = Controller(server, policy, staleWait);
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        await Settle(server, welcome.Code, one.Session!, two.Session!);
        await Until(one, s => s.InputLocked is null || s.View.YourMove == false);
        return (one, two, welcome.Code);
    }

    // Two controllers play a whole match against the server. Whatever the bot chooses is sent through the controller of
    // the seat it belongs to; every rules-legal choice for a seat was offered to that seat as a hint. Both screens end on
    // the server's final hash, with every event as a line and the outcome in words.
    [Fact]
    public async Task TwoControllersShowTheServersMatchAndPlayItToTheEnd()
    {
        var (one, two, code) = await ControllerPair("melee");
        Assert.Equal(Seat.One, one.Snapshot().Seat);
        Assert.Equal(Seat.Two, two.Snapshot().Seat);
        Assert.Equal(code, two.Snapshot().Code);
        Assert.Null(one.Snapshot().View.NamesProblem);
        Assert.Contains("(a1)", one.Snapshot().View.Models[0].Name);

        var bot = new Bot(new Random(5));
        var feedback = new List<Feedback>();
        for (var round = 0; round < 5000 && one.Session!.Mirror.State.Phase != Phase.Ended; round++)
        {
            await Settle(_server, code, one.Session!, two.Session!);
            var state = one.Session!.Mirror.State;
            if (state.Phase == Phase.Ended)
            {
                break;
            }
            foreach (var seat in new[] { Seat.One, Seat.Two })
            {
                var controller = seat == Seat.One ? one : two;
                var screen = await Until(controller, s => s.InputLocked is null);
                var legal = Rules.Core.TurnSequence.LegalCommands(state).Where(c => CommandSeats.Of(c) == seat).ToList();
                Assert.Equal(legal, screen.Hints.Select(h => h.Command));
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
                var controller = CommandSeats.Of(command) == Seat.One ? one : two;
                var label = controller.Snapshot().Hints.FirstOrDefault(h => h.Command == command)?.Label ?? command.GetType().Name;
                var result = await controller.SendAsync(command, label, Token);
                feedback.Add(result);
                if (result.Kind == FeedbackKind.Accepted)
                {
                    break;
                }
                await Settle(_server, code, one.Session!, two.Session!);
            }
        }
        await Settle(_server, code, one.Session!, two.Session!);
        AssertSameAsServer(_server, code, one.Session!, two.Session!);

        Assert.Contains(feedback, f => f.Kind == FeedbackKind.Accepted && f.Text.StartsWith("Done: ", StringComparison.Ordinal));
        Assert.Contains(feedback, f => f.Kind == FeedbackKind.RulesRefused && f.Text.Length > "Refused by the rules: ".Length);
        Assert.DoesNotContain(feedback, f => f.Kind is FeedbackKind.ProtocolRefused or FeedbackKind.Unknown);

        var log = _server.Lobby.LogOf(code)!;
        foreach (var controller in new[] { one, two })
        {
            var screen = await Until(controller, s => s.Log.Count == log.Count && s.View.Outcome is not null);
            Assert.Equal(Enumerable.Range(1, log.Count), screen.Log.Select(l => l.Seq));
            Assert.Equal(log.Select(e => EventText.Line(e.Event)), screen.Log.Select(l => l.Text));
            Assert.Equal(screen.View.Outcome, screen.View.Status);
            Assert.Empty(screen.Hints);
            Assert.Equal("the match is over.", screen.InputLocked);
        }
        Assert.Matches("^(You won|You lost|Draw): ", one.Snapshot().View.Outcome!);
    }

    // One command at a time: while one is unanswered there are no hints, and another is not sent. A refusal of the
    // protocol (here another seat's command) is reported as such, not as a rules decision.
    [Fact]
    public async Task OneCommandAtATimeAndProtocolRefusalsAreReported()
    {
        var (one, two, code) = await ControllerPair("melee");
        var mover = one.Snapshot().Hints.Count > 0 ? one : two;
        var hint = mover.Snapshot().Hints[0];
        // SendAsync raises Changed on this thread before the command goes out, so what the handler sees here cannot race
        // the server's answer.
        var thread = Environment.CurrentManagedThreadId;
        ScreenState? locked = null;
        Feedback? second = null;
        void OnChanged()
        {
            if (Environment.CurrentManagedThreadId == thread && locked is null)
            {
                locked = mover.Snapshot();
                second = mover.SendAsync(hint.Command, hint.Label, Token).Result;
            }
        }
        mover.Changed += OnChanged;
        var sending = mover.SendAsync(hint.Command, hint.Label, Token);
        mover.Changed -= OnChanged;
        Assert.NotNull(locked);
        Assert.Empty(locked.Hints);
        Assert.Equal("waiting for the server's answer to the last command.", locked.InputLocked);
        Assert.Equal(FeedbackKind.Sending, locked.Feedback!.Kind);
        Assert.StartsWith("Not sent: waiting", second!.Text);
        Assert.Equal(FeedbackKind.Accepted, (await sending).Kind);

        await Settle(_server, code, one.Session!, two.Session!);
        var other = mover == one ? Seat.Two : Seat.One;
        var refused = await (mover == one ? one : two).SendAsync(new EndActivation(other), "End activation", Token);
        Assert.Equal(FeedbackKind.ProtocolRefused, refused.Kind);
        Assert.Contains(nameof(ProtocolError.SeatMismatch), refused.Text);
    }

    // S3: after a Stale refusal the controller waits (bounded, without polling) for the missing events on the same
    // connection and lets the player choose again. It never reconnects on its own. If the events do not come, it says
    // so and offers a deliberate resync, which takes the seat back on a new connection through Reconnector.
    [Fact]
    public async Task StaleRefusalsWaitForTheEventsAndNeverReconnectOnTheirOwn()
    {
        var (one, two, code) = await ControllerPair("melee", staleWait: TimeSpan.FromMilliseconds(300));
        var session = one.Session!;
        Rejected Stale(int events) => new(null, 1, RejectionKind.Protocol, nameof(ProtocolError.Stale), "stale", null, events, 1, false);

        // The events are already here: choose again at once.
        var now = await one.HandleStaleAsync(Stale(session.Mirror.Events), Token);
        Assert.Equal(FeedbackKind.Stale, now.Kind);
        Assert.Contains("up to date now", now.Text);
        Assert.False(one.Snapshot().CanResync);

        // The events arrive while it waits (the other seat acts).
        var count = _server.Lobby.LogOf(code)!.Count;
        var waiting = one.HandleStaleAsync(Stale(count + 1), Token);
        var actor = one.Snapshot().Hints.Count > 0 ? one : two;
        var hint = actor.Snapshot().Hints[0];
        Assert.Equal(FeedbackKind.Accepted, (await actor.Session!.SubmitAsync(hint.Command, Token)) is Accepted ? FeedbackKind.Accepted : FeedbackKind.Unknown);
        var arrived = await waiting;
        Assert.Contains("up to date now", arrived.Text);
        Assert.Contains("2 times in a row", arrived.Text);

        // Events that never come: bounded wait, a clear status, no reconnect, and repeating it does not spin.
        await Settle(_server, code, one.Session!, two.Session!);
        var never = _server.Lobby.LogOf(code)!.Count + 50;
        for (var i = 0; i < 3; i++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var late = await one.HandleStaleAsync(Stale(never), Token);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{clock.Elapsed}");
            Assert.Contains("did not arrive in time", late.Text);
        }
        var stuck = one.Snapshot();
        Assert.True(stuck.CanResync);
        Assert.Equal(ConnectionState.Connected, stuck.Connection);
        Assert.Same(session, one.Session);
        Assert.False(session.IsClosed);

        // The player chooses to resync: a new connection takes the seat back; nobody is taken over.
        await one.ResyncAsync(Token);
        var back = await Until(one, s => s.Connection == ConnectionState.Connected && !ReferenceEquals(one.Session, session));
        Assert.False(back.CanResync);
        Assert.False(one.Session!.TakenOver);
        Assert.Equal(Seat.One, back.Seat);
        await Settle(_server, code, one.Session!, two.Session!);
        AssertSameAsServer(_server, code, one.Session!, two.Session!);
    }

    // A drop: "reconnecting", then connected on a new session with the same copy; the opponent sees the pause in words
    // and play goes on.
    [Fact]
    public async Task ADropShowsReconnectingAndTheOpponentSeesThePause()
    {
        // One's reconnect waits until the opponent has seen the pause: otherwise, on a busy machine, the rejoin can reach
        // the server before it has noticed the drop, and there is no pause to see.
        var opponentSawPause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        var one = Controller(connect: async (uri, mirror, ct) =>
        {
            if (!first)
            {
                await opponentSawPause.Task.WaitAsync(ct);
            }
            first = false;
            var session = await ClientSession.ConnectAsync(uri, ct, mirror: mirror);
            lock (_clients)
            {
                _clients.Add(session);
            }
            return session;
        });
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync("melee", Token));
        var code = welcome.Code;
        var two = Controller();
        Assert.IsType<Welcome>(await two.JoinAsync(code, Token));
        await Settle(_server, code, one.Session!, two.Session!);
        var states = new List<ConnectionState>();
        one.Changed += () =>
        {
            lock (states)
            {
                states.Add(one.Snapshot().Connection);
            }
        };
        // The pause can be over before an awaiting check looks again, so the opponent's screen is read as it changes.
        var pause = new TaskCompletionSource<ScreenState>(TaskCreationOptions.RunContinuationsAsynchronously);
        two.Changed += () =>
        {
            if (two.Snapshot() is { OpponentAway: not null } screen)
            {
                pause.TrySetResult(screen);
            }
        };
        var dropped = one.Session!;
        dropped.Abort();
        var paused = await pause.Task.WaitAsync(Token);
        opponentSawPause.SetResult();
        Assert.Contains("paused", paused.OpponentAway);
        Assert.Equal("the match is paused while your opponent is away.", paused.InputLocked);
        Assert.Empty(paused.Hints);

        await Until(one, s => s.Connection == ConnectionState.Connected && !ReferenceEquals(one.Session, dropped));
        lock (states)
        {
            Assert.Contains(ConnectionState.Reconnecting, states);
        }
        Assert.Same(dropped.Mirror, one.Session!.Mirror);
        await Until(two, s => s.OpponentAway is null);
        await Settle(_server, code, one.Session!, two.Session!);
        AssertSameAsServer(_server, code, one.Session!, two.Session!);
    }

    // Automatic attempts stop at the configured limit; "Rejoin now" then takes the seat back through Reconnector.
    [Fact]
    public async Task AfterAutomaticAttemptsStopRejoinNowRecovers()
    {
        var blocked = false;
        var policy = Quick with { GiveUpAfter = TimeSpan.FromMilliseconds(300) };
        var one = Controller(policy: policy, connect: async (uri, mirror, ct) =>
        {
            if (Volatile.Read(ref blocked))
            {
                throw new System.Net.WebSockets.WebSocketException("unreachable");
            }
            var session = await ClientSession.ConnectAsync(uri, ct, mirror: mirror);
            lock (_clients)
            {
                _clients.Add(session);
            }
            return session;
        });
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync("melee", Token));
        var two = Controller(policy: policy);
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        await Settle(_server, welcome.Code, one.Session!, two.Session!);

        Volatile.Write(ref blocked, true);
        one.Session!.Abort();
        var gaveUp = await Until(one, s => s.Connection == ConnectionState.GaveUp);
        Assert.True(gaveUp.CanRejoinNow);
        Assert.Contains("Rejoin now", gaveUp.ConnectionText);
        Assert.Empty(gaveUp.Hints);

        Volatile.Write(ref blocked, false);
        var result = await one.RejoinNowAsync(cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, result.Outcome);
        Assert.Equal(ConnectionState.Connected, (await Until(one, s => s.Connection == ConnectionState.Connected)).Connection);
    }

    // Rejoining while the opponent is away: the server sends the pause right after the Welcome, often before the
    // controller has switched to the new connection. The screen must still say the opponent is away.
    [Fact]
    public async Task ARejoinWhileTheOpponentIsAwayShowsThePause()
    {
        var blocked = false;
        var one = Controller();
        var two = Controller(policy: Quick with { GiveUpAfter = TimeSpan.FromMilliseconds(300) }, connect: async (uri, mirror, ct) =>
        {
            if (Volatile.Read(ref blocked))
            {
                throw new System.Net.WebSockets.WebSocketException("unreachable");
            }
            var session = await ClientSession.ConnectAsync(uri, ct, mirror: mirror);
            lock (_clients)
            {
                _clients.Add(session);
            }
            return session;
        });
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync("melee", Token));
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        await Settle(_server, welcome.Code, one.Session!, two.Session!);

        Volatile.Write(ref blocked, true);
        two.Session!.Abort();
        await Until(two, s => s.Connection == ConnectionState.GaveUp);
        await Until(one, s => s.OpponentAway is not null);

        var dropped = one.Session!;
        dropped.Abort();
        await Until(one, s => s.Connection == ConnectionState.Connected && !ReferenceEquals(one.Session, dropped));
        await one.Session!.WaitForAsync(s => s.IsPaused, Token);
        var screen = one.Snapshot();
        Assert.NotNull(screen.OpponentAway);
        Assert.Equal("the match is paused while your opponent is away.", screen.InputLocked);
    }

    // A seat taken over from another window is never taken back without the player's confirmation.
    [Fact]
    public async Task ATakenOverSeatNeedsConfirmationToTakeBack()
    {
        var (one, _, code) = await ControllerPair("melee");
        var newer = await Connect();
        Assert.IsType<Welcome>(await newer.RejoinAsync(code, one.Session!.SeatToken!, Token));
        var taken = await Until(one, s => s.Connection == ConnectionState.TakenOver);
        Assert.True(taken.RejoinNeedsConfirmation);
        Assert.True(taken.CanRejoinNow);
        Assert.Contains("another window", taken.ConnectionText);
        await Assert.ThrowsAsync<InvalidOperationException>(() => one.RejoinNowAsync(takeSeatBack: false, Token));

        var result = await one.RejoinNowAsync(takeSeatBack: true, Token);
        Assert.Equal(ReconnectOutcome.Rejoined, result.Outcome);
        await newer.WaitForAsync(c => c.TakenOver, Token);
        await Until(one, s => s.Connection == ConnectionState.Connected);
    }

    // An aborted match: both screens say so in words, no result counts, and the saved seats are forgotten.
    [Fact]
    public async Task AnAbortedMatchIsShownAndItsSavedSeatsAreForgotten()
    {
        var server = await Start(TimeSpan.Zero);
        var (one, two, code) = await ControllerPair("melee", server);
        Assert.Equal(SavedSeatStatus.Found, Sessions.Find(server.Endpoint.ToString(), code, Seat.One).Status);
        two.Session!.Abort();
        var aborted = await Until(one, s => s.Connection == ConnectionState.Aborted);
        Assert.Contains("No result was recorded", aborted.ConnectionText);
        Assert.Empty(aborted.Hints);
        await Until(two, s => s.Connection is ConnectionState.Aborted);
        Assert.Equal(SavedSeatStatus.Missing, Sessions.Find(server.Endpoint.ToString(), code, Seat.One).Status);
        Assert.Equal(SavedSeatStatus.Missing, Sessions.Find(server.Endpoint.ToString(), code, Seat.Two).Status);
    }
}
