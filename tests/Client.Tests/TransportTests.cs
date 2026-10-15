using System.Net.WebSockets;
using Match.Client;
using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// M7.2: the shared client library over real WebSockets against a real in-process server: protocol v2, command
// sequencing, saved seat tokens and reconnecting (D7). The server decides everything; these tests only drive clients.
public sealed partial class TransportTests : IAsyncLifetime
{
    private static readonly ReconnectPolicy Quick = new()
    {
        Delays = [TimeSpan.FromMilliseconds(10)],
        Repeat = TimeSpan.FromMilliseconds(10),
        GiveUpAfter = TimeSpan.FromSeconds(20),
    };

    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(3));
    private readonly List<ClientSession> _clients = [];
    private readonly List<MatchServer> _servers = [];
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("trench-m7-client-");
    private MatchServer _server = null!;

    private CancellationToken Token => _timeout.Token;
    private SessionStore Sessions => new(Path.Combine(_dir.FullName, "sessions"));

    public async Task InitializeAsync() => _server = await Start(TimeSpan.FromMinutes(5));

    public async Task DisposeAsync()
    {
        foreach (var controller in _controllers)
        {
            await controller.DisposeAsync();
        }
        foreach (var fake in _fakes)
        {
            await fake.DisposeAsync();
        }
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
        _timeout.Dispose();
        _dir.Delete(recursive: true);
    }

    private async Task<MatchServer> Start(TimeSpan rejoinWindow, int seed = 7)
    {
        var server = await MatchServer.StartAsync(new MatchServerOptions
        {
            NewDice = () => new SeededDice(seed),
            ShutdownGrace = TimeSpan.FromSeconds(3),
            RejoinWindow = rejoinWindow,
            TickInterval = TimeSpan.FromMilliseconds(20),
        }, Token);
        _servers.Add(server);
        return server;
    }

    private async Task<ClientSession> Connect(MatchServer? server = null, MatchMirror? mirror = null, CancellationToken? cancellationToken = null)
    {
        var client = await ClientSession.ConnectAsync((server ?? _server).Endpoint, cancellationToken ?? Token, mirror: mirror);
        SaveTokens(client, server);
        lock (_clients)
        {
            _clients.Add(client);
        }
        return client;
    }

    private void SaveTokens(ClientSession client, MatchServer? server = null)
    {
        var store = Sessions;
        var endpoint = (server ?? _server).Endpoint.ToString();
        client.SaveToken = w => store.TrySave(new SavedSeat(endpoint, w.Code, w.MatchId, w.Seat, w.SeatToken!), out _);
    }

    private Func<MatchMirror, CancellationToken, Task<ClientSession>> Reconnect(MatchServer? server = null) =>
        (mirror, cancellationToken) => Connect(server, mirror, cancellationToken);

    private async Task<(ClientSession One, ClientSession Two, string Code)> Pair(string scenario = "melee", MatchServer? server = null)
    {
        server ??= _server;
        var one = await Connect(server);
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync(scenario, Token));
        var two = await Connect(server);
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        await Settle(server, welcome.Code, one, two);
        return (one, two, welcome.Code);
    }

    // Waits until every client's copy holds every event the server has logged.
    private async Task Settle(MatchServer server, string code, params ClientSession[] clients)
    {
        var count = server.Lobby.LogOf(code)!.Count;
        foreach (var client in clients)
        {
            await client.WaitForAsync(c => c.Mirror.Events == count, Token);
        }
    }

    private sealed record Sent(ClientSession Client, long SeqBefore, SubmitCommand Submit, ServerMessage Answer, long SeqAfter);

    // One round: the bot proposes commands from the shared state; each is sent by the seat it belongs to until the server
    // accepts one. Returns what was sent, or nothing when the match is over.
    private async Task<List<Sent>> Round(MatchServer server, string code, Bot bot, ClientSession one, ClientSession two)
    {
        await Settle(server, code, one, two);
        var sent = new List<Sent>();
        if (one.Mirror.State.Phase == Phase.Ended)
        {
            return sent;
        }
        List<Command> proposals;
        try
        {
            proposals = bot.Propose(one.Mirror.State).ToList();
        }
        catch (InvalidOperationException)
        {
            proposals = [];
        }
        foreach (var command in proposals)
        {
            var client = CommandSeats.Of(command) == one.Seat ? one : two;
            var before = client.Seq;
            var answer = await client.SubmitAsync(command, Token);
            sent.Add(new Sent(client, before, client.LastSubmit!, answer, client.Seq));
            if (answer is Accepted)
            {
                break;
            }
            await Settle(server, code, one, two);
        }
        return sent;
    }

    private async Task<List<Sent>> Play(MatchServer server, string code, ClientSession one, ClientSession two, int rounds, int seed = 3)
    {
        var bot = new Bot(new Random(seed));
        var all = new List<Sent>();
        for (var i = 0; i < rounds && one.Mirror.State.Phase != Phase.Ended; i++)
        {
            all.AddRange(await Round(server, code, bot, one, two));
        }
        return all;
    }

    private async Task PlayToEnd(MatchServer server, string code, ClientSession one, ClientSession two, int seed = 3)
    {
        await Play(server, code, one, two, 5000, seed);
        await Settle(server, code, one, two);
        Assert.Equal(Phase.Ended, one.Mirror.State.Phase);
    }

    private static void AssertSameAsServer(MatchServer server, string code, params ClientSession[] clients)
    {
        var log = server.Lobby.LogOf(code)!;
        foreach (var client in clients)
        {
            Assert.True(client.Synchronized);
            Assert.Equal(log.Count, client.Mirror.Events);
            Assert.Equal(log[^1].Hash, client.Mirror.Hash);
        }
    }

    private static ProtocolError ProtocolCode(ServerMessage? answer)
    {
        var rejected = Assert.IsType<Rejected>(answer);
        Assert.Equal(RejectionKind.Protocol, rejected.Kind);
        return Enum.Parse<ProtocolError>(rejected.Code);
    }

    [Fact]
    public async Task BothSeatsConnectOverProtocolV2AndHoldTheServersCopy()
    {
        Assert.Equal("trench-sim.v2", ClientSession.SubProtocol);
        var (one, two, code) = await Pair();
        Assert.Equal(Seat.One, one.Seat);
        Assert.Equal(Seat.Two, two.Seat);
        AssertSameAsServer(_server, code, one, two);

        // A client speaking another protocol version is refused at the handshake.
        await Assert.ThrowsAnyAsync<WebSocketException>(() => ClientSession.ConnectAsync(_server.Endpoint, Token, subProtocol: "trench-sim.v1"));
    }

    // Each command takes the next number; only a command the server decided (accepted, or refused by the rules) uses it
    // up, and a protocol refusal leaves it free. Ids never repeat, and each event's hash matches the server's.
    [Fact]
    public async Task CommandsAreNumberedInOrderAndOnlyDecidedCommandsUseANumber()
    {
        var (one, two, code) = await Pair();
        var sent = await Play(_server, code, one, two, 60);
        Assert.Contains(sent, s => s.Answer is Accepted);
        Assert.Contains(sent, s => s.Answer is Rejected { Kind: RejectionKind.Rules });

        // A command for the other seat is a protocol refusal: it does not use up the number.
        var before = one.Seq;
        var refused = await one.SubmitAsync(new EndActivation(Seat.Two), Token);
        Assert.Equal(ProtocolError.SeatMismatch, ProtocolCode(refused));
        sent.Add(new Sent(one, before, one.LastSubmit!, refused, one.Seq));

        foreach (var s in sent)
        {
            Assert.Equal(s.SeqBefore + 1, s.Submit.Seq);
            var decided = s.Answer is Accepted or Rejected { Kind: RejectionKind.Rules };
            Assert.Equal(decided ? s.Submit.Seq : s.SeqBefore, s.SeqAfter);
        }
        Assert.Equal(sent.Count, sent.Select(s => s.Submit.CommandId).Distinct().Count());

        await Settle(_server, code, one, two);
        var log = _server.Lobby.LogOf(code)!.Select(e => e.Hash);
        Assert.Equal(log, one.Hashes);
        Assert.Equal(log, two.Hashes);
    }

    [Fact]
    public async Task AnAnsweredCommandSentAgainIsADuplicateAndIsNotAppliedTwice()
    {
        var (one, two, code) = await Pair();
        var accepted = (await Play(_server, code, one, two, 10)).Last(s => s.Answer is Accepted);
        await Settle(_server, code, one, two);
        var count = _server.Lobby.LogOf(code)!.Count;

        var again = await accepted.Client.ResendAsync(accepted.Submit, Token);
        Assert.Equal(Assert.IsType<Accepted>(accepted.Answer) with { Duplicate = true }, again);
        Assert.Equal(count, _server.Lobby.LogOf(code)!.Count);
    }

    // The token is saved (owner-only) before it is confirmed; a rejoin rotates it, and once the new one is confirmed the
    // old one no longer opens the seat. If the new token cannot be saved it is not confirmed and the old one still works.
    [Fact]
    public async Task SeatTokensAreSavedBeforeTheyAreConfirmedAndRotateOnRejoin()
    {
        var (one, two, code) = await Pair();
        var saved = Sessions.Find(_server.Endpoint.ToString(), code, Seat.Two);
        Assert.Equal(SavedSeatStatus.Found, saved.Status);
        Assert.Equal(two.SeatToken, saved.Seat!.Token);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(saved.Path!));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Sessions.Directory));
        }

        var first = two.SeatToken!;
        two.Abort();
        await two.Completion.WaitAsync(Token);
        var result = await Reconnector.RunAsync(two, Reconnect(), policy: Quick, cancellationToken: Token);
        var back = Assert.IsType<ClientSession>(result.Session);
        var second = back.SeatToken!;
        Assert.NotEqual(first, second);
        Assert.Equal(second, Sessions.Find(_server.Endpoint.ToString(), code, Seat.Two).Seat!.Token);

        // TokenSaved went out before the Welcome was handed over; any answer on the same connection comes after it.
        Assert.Equal(ProtocolError.SeatMismatch, ProtocolCode(await back.SubmitAsync(new EndActivation(Seat.One), Token)));
        var thief = await Connect();
        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(await thief.RejoinAsync(code, first, Token)));

        // Now a save that fails: the rotated token is not confirmed, so the server still honours the current one.
        back.Abort();
        await back.Completion.WaitAsync(Token);
        var unsaved = await Reconnector.RunAsync(back, Reconnect(), configure: c => c.SaveToken = _ => false, policy: Quick, cancellationToken: Token);
        var third = Assert.IsType<ClientSession>(unsaved.Session).SeatToken!;
        Assert.NotEqual(second, third);
        Assert.Equal(ProtocolError.SeatMismatch, ProtocolCode(await unsaved.Session!.SubmitAsync(new EndActivation(Seat.One), Token)));
        var other = await Connect();
        Assert.IsType<Welcome>(await other.RejoinAsync(code, second, Token));
    }

    // The approved D7 behaviour: a seat that drops is taken back with its token and its copy; the server replays only
    // what is missing, the opponent sees the pause end, and the match finishes with every hash agreeing.
    [Fact]
    public async Task ADroppedSeatReconnectsWithItsCopyAndTheMatchFinishes()
    {
        var (one, two, code) = await Pair();
        await Play(_server, code, one, two, 25);
        await Settle(_server, code, one, two);
        var held = two.Mirror.Events;

        two.Abort();
        await one.WaitForAsync(c => c.IsPaused, Token);
        await two.Completion.WaitAsync(Token);
        Assert.Equal(ReconnectDecision.Reconnect, Reconnector.Decide(two));

        var result = await Reconnector.RunAsync(two, Reconnect(), policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, result.Outcome);
        Assert.Equal(1, result.Attempts);
        var back = result.Session!;
        Assert.Same(two.Mirror, back.Mirror);
        Assert.Equal(held, back.Welcome!.ReplayFrom);
        await one.WaitForAsync(c => !c.IsPaused, Token);
        await Settle(_server, code, one, back);
        AssertSameAsServer(_server, code, one, back);

        await PlayToEnd(_server, code, one, back);
        AssertSameAsServer(_server, code, one, back);
        Assert.NotNull(back.Mirror.State.Outcome);
        Assert.Equal(ReconnectDecision.Finished, Reconnector.Decide(back));
    }

    [Fact]
    public async Task ReconnectingKeepsTryingWhileTheServerCannotBeReached()
    {
        var (_, two, _) = await Pair();
        two.Abort();
        await two.Completion.WaitAsync(Token);
        var calls = 0;
        var result = await Reconnector.RunAsync(two, (mirror, ct) =>
        {
            if (Interlocked.Increment(ref calls) <= 3)
            {
                throw new WebSocketException("unreachable");
            }
            return Connect(mirror: mirror, cancellationToken: ct);
        }, policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, result.Outcome);
        Assert.Equal(4, result.Attempts);
        Assert.True(result.Session!.Synchronized);
    }

    [Fact]
    public async Task ReconnectingGivesUpWhenTheServerNeverAnswers()
    {
        var calls = 0;
        var policy = Quick with { GiveUpAfter = TimeSpan.FromMilliseconds(300) };
        var result = await Reconnector.RunAsync("CODE1", RecordFormat.Base64Url(new byte[32]), new MatchMirror(), (_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new WebSocketException("unreachable");
        }, policy: policy, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.GaveUp, result.Outcome);
        Assert.Null(result.Session);
        Assert.True(calls >= 2);
        Assert.Equal(calls, result.Attempts);
    }

    // After a takeover the seat belongs to the player's newer connection; this one must not take it back.
    [Fact]
    public async Task ASeatTakenOverIsNeverTakenBackAutomatically()
    {
        var (_, two, code) = await Pair();
        var newer = await Connect();
        Assert.IsType<Welcome>(await newer.RejoinAsync(code, two.SeatToken!, Token));
        await two.WaitForAsync(c => c.TakenOver, Token);
        Assert.Equal(ReconnectDecision.TakenOver, Reconnector.Decide(two));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(two, Reconnect(), policy: Quick, cancellationToken: Token));
    }

    [Fact]
    public async Task AnInvalidTokenStopsReconnectingAtOnce()
    {
        var (_, _, code) = await Pair();
        var calls = 0;
        var result = await Reconnector.RunAsync(code, RecordFormat.Base64Url(new byte[32]), new MatchMirror(), (mirror, ct) =>
        {
            Interlocked.Increment(ref calls);
            return Connect(mirror: mirror, cancellationToken: ct);
        }, policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Refused, result.Outcome);
        Assert.Equal(ProtocolError.InvalidToken, result.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AnAbortedMatchIsNotReconnected()
    {
        var server = await Start(TimeSpan.Zero);
        var (one, two, _) = await Pair(server: server);
        two.Abort();
        await one.WaitForAsync(c => c.Aborted is not null, Token);
        Assert.Equal(ReconnectDecision.Aborted, Reconnector.Decide(one));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(one, Reconnect(server), policy: Quick, cancellationToken: Token));
    }

    [Fact]
    public async Task AClientThatNeverJoinedHasNoSeatToReconnect()
    {
        var lonely = await Connect();
        lonely.Abort();
        await lonely.Completion.WaitAsync(Token);
        Assert.Equal(ReconnectDecision.NoSeat, Reconnector.Decide(lonely));
    }

    [Fact]
    public void TheDefaultPolicyWaitsOneTwoFourEightThenFifteenSecondsForFiveMinutes()
    {
        var policy = new ReconnectPolicy();
        Assert.Equal(
            new[] { 0, 1, 2, 4, 8, 15, 15 }.Select(s => TimeSpan.FromSeconds(s)),
            Enumerable.Range(0, 7).Select(policy.DelayBefore));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.GiveUpAfter);
        Assert.Equal(TimeSpan.FromMinutes(5), new MatchServerOptions().RejoinWindow);
    }
}
