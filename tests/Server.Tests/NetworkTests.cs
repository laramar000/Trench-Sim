using System.Net.WebSockets;
using System.Text;
using Match.Cli;
using Match.Client;
using Match.Protocol;
using Match.Server;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Real Kestrel on a free port, real WebSocket clients (the terminal client's ClientSession), seeded server dice.
public sealed class NetworkTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly CancellationTokenSource _timeout = new(Patience);
    private readonly List<ClientSession> _clients = [];
    private MatchServer _server = null!;

    private CancellationToken Token => _timeout.Token;

    public async Task InitializeAsync() => _server = await Start();

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        await _server.DisposeAsync();
        _timeout.Dispose();
    }

    private static Task<MatchServer> Start(
        int seed = 1, TimeSpan? joinTimeout = null, TimeSpan? keepAlive = null, int queueLimit = 4096, TimeSpan? sendTimeout = null) =>
        MatchServer.StartAsync(new MatchServerOptions
        {
            NewDice = () => new SeededDice(seed),
            JoinTimeout = joinTimeout ?? TimeSpan.FromSeconds(30),
            KeepAlive = keepAlive ?? TimeSpan.FromSeconds(15),
            OutboundQueueLimit = queueLimit,
            SendTimeout = sendTimeout ?? TimeSpan.FromSeconds(10),
            ShutdownGrace = TimeSpan.FromSeconds(3),
            // These are the M5 tests: a disconnect aborts at once, as M5 did. M6's pause is tested in ReconnectTests.
            RejoinWindow = TimeSpan.Zero,
        });

    private async Task<ClientSession> Connect(MatchServer? server = null)
    {
        var client = await ClientSession.ConnectAsync((server ?? _server).Endpoint, Token);
        _clients.Add(client);
        return client;
    }

    private async Task<(ClientSession One, ClientSession Two, string Code)> Pair(string scenario = "ranged", MatchServer? server = null)
    {
        var one = await Connect(server);
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync(scenario, Token));
        var two = await Connect(server);
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        var events = (server ?? _server).Lobby.LogOf(welcome.Code)!.Count;
        await one.WaitForAsync(c => c.Mirror.Events == events, Token);
        await two.WaitForAsync(c => c.Mirror.Events == events, Token);
        return (one, two, welcome.Code);
    }

    private async Task Until(Func<bool> condition)
    {
        while (!condition())
        {
            await Task.Delay(10, Token);
        }
    }

    private static ProtocolError ProtocolCode(ServerMessage answer)
    {
        var rejected = Assert.IsType<Rejected>(answer);
        Assert.Equal(RejectionKind.Protocol, rejected.Kind);
        return Enum.Parse<ProtocolError>(rejected.Code);
    }

    // Every event the server logged has the hash of replaying the log up to it, and both clients saw exactly those
    // events and hashes in that order.
    private void AssertAgreement(string code, params ClientSession[] clients)
    {
        var log = _server.Lobby.LogOf(code)!;
        var state = MatchState.None;
        foreach (var published in log)
        {
            state = Reducer.Apply(state, published.Event);
            Assert.Equal(StateHash.Compute(state), published.Hash);
        }
        foreach (var client in clients)
        {
            Assert.Null(client.Failure);
            Assert.Equal(log.Select(e => e.Hash), client.Hashes);
            Assert.Equal(log[^1].Hash, client.Mirror.Hash);
        }
    }

    [Theory]
    [InlineData("ranged", 1)]
    [InlineData("ranged", 4)]
    [InlineData("melee", 366)]
    public async Task TwoTerminalClientsPlayAWholeMatchAndAgreeOnEveryHash(string scenario, int seed)
    {
        await _server.DisposeAsync();
        _server = await Start(seed);
        var (one, two, code) = await Pair(scenario);
        Assert.Equal(Seat.One, one.Seat);
        Assert.Equal(Seat.Two, two.Seat);
        Assert.Contains(one.Notices, n => n.Kind == NoticeKind.OpponentJoined);

        await Task.WhenAll(BotPlayer.PlayAsync(one, seed, Token), BotPlayer.PlayAsync(two, seed + 1, Token));

        Assert.Equal(Phase.Ended, one.Mirror.State.Phase);
        Assert.NotNull(one.Mirror.State.Outcome);
        Assert.Equal(MatchStatus.Finished, _server.Lobby.StatusOf(code));
        Assert.IsType<MatchEnded>(_server.Lobby.LogOf(code)![^1].Event);
        await one.WaitForAsync(c => c.Mirror.Events == _server.Lobby.LogOf(code)!.Count, Token);
        await two.WaitForAsync(c => c.Mirror.Events == _server.Lobby.LogOf(code)!.Count, Token);
        AssertAgreement(code, one, two);
    }

    [Fact]
    public async Task SeatsAreAssignedAndEnforcedByTheServer()
    {
        var (one, two, code) = await Pair();
        var events = one.Mirror.Events;

        // Whatever Seat Two may do now, Seat One's connection cannot send it.
        foreach (var command in TurnSequence.LegalCommands(one.Mirror.State).Where(c => CommandSeats.Of(c) == Seat.Two)
                     .Append(new ChooseDeploymentZone(Seat.Two, "north")).Append(new EndActivation(Seat.Two)))
        {
            Assert.Equal(ProtocolError.SeatMismatch, ProtocolCode(await one.SubmitAsync(command, Token)));
        }
        Assert.Equal(ProtocolError.ServerOnlyCommand, ProtocolCode(await one.SubmitAsync(new StartMatch(), Token)));

        // Nobody can ask for a seat, take a full match, or hold two seats.
        var third = await Connect();
        await third.SendRawAsync(Encoding.UTF8.GetBytes($$"""{"v":2,"type":"JoinMatch","code":"{{code}}","seat":"One"}"""), cancellationToken: Token);
        Assert.Equal(ProtocolError.Malformed, ProtocolCode(await third.NextAnswerAsync(Token)));
        Assert.Equal(ProtocolError.MatchFull, ProtocolCode(await third.JoinAsync(code, Token)));
        Assert.Equal(ProtocolError.AlreadyJoined, ProtocolCode(await two.CreateAsync("melee", Token)));

        Assert.Equal(events, _server.Lobby.LogOf(code)!.Count);
        AssertAgreement(code, one, two);
    }

    [Fact]
    public async Task ACommandFlowsFromOneClientToTheServerAndBothClients()
    {
        var (one, two, code) = await Pair();
        var pending = one.Mirror.State.Pending!;
        var actor = pending.Seat == Seat.One ? one : two;
        var zone = one.Mirror.State.Setup!.ZoneIds[0];

        var accepted = Assert.IsType<Accepted>(await actor.SubmitAsync(new ChooseDeploymentZone(pending.Seat, zone), Token));
        Assert.Equal(accepted.LastEvent, actor.Mirror.Events);
        await one.WaitForAsync(c => c.Mirror.Events == accepted.LastEvent, Token);
        await two.WaitForAsync(c => c.Mirror.Events == accepted.LastEvent, Token);
        Assert.Equal(zone, one.Mirror.State.SeatFor(pending.Seat).ZoneId);
        AssertAgreement(code, one, two);
    }

    [Fact]
    public async Task ARulesRefusalOverTheWireChangesNothing()
    {
        var (one, two, code) = await Pair();
        var log = _server.Lobby.LogOf(code)!.Count;
        var answer = Assert.IsType<Rejected>(await one.SubmitAsync(new ActivateModel(Seat.One, new ModelId("zz9")), Token));
        Assert.Equal(RejectionKind.Rules, answer.Kind);
        Assert.Equal(2, answer.NextSeq);
        Assert.Equal(log, _server.Lobby.LogOf(code)!.Count);
        AssertAgreement(code, one, two);
    }

    [Fact]
    public async Task VersionsAreNegotiatedAtTheHandshakeAndCheckedOnEveryMessage()
    {
        // A client that offers only the old protocol, trench-sim.v1, never gets a socket (v2 is the only version).
        await Assert.ThrowsAnyAsync<WebSocketException>(() => ClientSession.ConnectAsync(_server.Endpoint, Token, subProtocol: "trench-sim.v1"));
        using (var http = new HttpClient())
        {
            var response = await http.GetAsync(new UriBuilder(_server.Endpoint) { Scheme = "http" }.Uri, Token);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        var client = await Connect();
        await client.SendRawAsync(Encoding.UTF8.GetBytes("""{"v":1,"type":"CreateMatch","scenario":"ranged"}"""), cancellationToken: Token);
        Assert.Equal(ProtocolError.UnsupportedVersion, ProtocolCode(await client.NextAnswerAsync(Token)));
        Assert.IsType<Welcome>(await client.CreateAsync("ranged", Token));
    }

    [Fact]
    public async Task MalformedAndOversizedMessagesAreRefusedAndTheConnectionCarriesOn()
    {
        var client = await Connect();
        var cases = new (byte[] Bytes, WebSocketMessageType Type, ProtocolError Expected)[]
        {
            (Encoding.UTF8.GetBytes("not json"), WebSocketMessageType.Text, ProtocolError.Malformed),
            (Encoding.UTF8.GetBytes("""{"v":2,"type":"CreateMatch","scenario":"ranged","""), WebSocketMessageType.Text, ProtocolError.Malformed),
            (Encoding.UTF8.GetBytes("""{"v":2,"type":"CreateMatch","scenario":"ranged"}"""), WebSocketMessageType.Binary, ProtocolError.Malformed),
            (Encoding.UTF8.GetBytes("""{"v":2,"type":"Teleport"}"""), WebSocketMessageType.Text, ProtocolError.UnknownType),
            (Encoding.UTF8.GetBytes("""{"v":2,"type":"CreateMatch","scenario":"ranged","dice":[6]}"""), WebSocketMessageType.Text, ProtocolError.Malformed),
            (new byte[ProtocolLimits.MaxClientMessageBytes + 1], WebSocketMessageType.Text, ProtocolError.TooLarge),
            (Encoding.UTF8.GetBytes(new string(' ', 200_000)), WebSocketMessageType.Text, ProtocolError.TooLarge),
        };
        foreach (var (bytes, type, expected) in cases)
        {
            await client.SendRawAsync(bytes, type, Token, frameSize: 4096);
            Assert.Equal(expected, ProtocolCode(await client.NextAnswerAsync(Token)));
        }

        // A whole message split over many small frames is read as one.
        await client.SendRawAsync(Codec.Encode(new CreateMatch("melee")), cancellationToken: Token, frameSize: 7);
        Assert.IsType<Welcome>(await client.NextAnswerAsync(Token));
        Assert.False(client.IsClosed);
    }

    // RFC 6455 requires a text frame that is not UTF-8 to fail the connection (1007), and the WebSocket layer does so
    // before the server sees a message. For a seated player that is a disconnect: the match is aborted, nothing else.
    [Fact]
    public async Task ATextFrameThatIsNotUtf8FailsTheConnection()
    {
        var (one, two, code) = await Pair();
        var events = one.Mirror.Events;
        await one.SendRawAsync([0xC3, 0x28], WebSocketMessageType.Text, Token);
        await one.Completion.WaitAsync(Token);
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, one.CloseStatus);
        await two.WaitForAsync(c => c.Aborted is not null, Token);
        Assert.Equal(new MatchAborted(AbortReason.Disconnected, Seat.One, events, two.Mirror.Hash), two.Aborted);
        Assert.Equal(events, _server.Lobby.LogOf(code)!.Count);

        // The server carries on.
        var next = await Connect();
        Assert.IsType<Welcome>(await next.CreateAsync("ranged", Token));
    }

    [Fact]
    public async Task OutOfRangeValuesAreRefused()
    {
        var (one, _, _) = await Pair();
        var json = """{"v":2,"type":"SubmitCommand","commandId":"x","seq":1,"knownEvents":""" + one.Mirror.Events
            + ""","command":{"type":"PlaceModel","seat":"One","model":"a1","at":{"x":999999999,"y":0}}}""";
        await one.SendRawAsync(Encoding.UTF8.GetBytes(json), cancellationToken: Token);
        Assert.Equal(ProtocolError.InvalidValue, ProtocolCode(await one.NextAnswerAsync(Token)));
        await one.SendRawAsync(Encoding.UTF8.GetBytes(json.Replace("\"seat\":\"One\"", "\"seat\":7")), cancellationToken: Token);
        Assert.Equal(ProtocolError.Malformed, ProtocolCode(await one.NextAnswerAsync(Token)));
    }

    [Fact]
    public async Task AMessageFarOverTheLimitClosesTheConnection()
    {
        var client = await Connect();
        await client.SendRawAsync(new byte[MatchServer.MaxDiscardBytes + 10], cancellationToken: Token, frameSize: 65536);
        Assert.Equal(ProtocolError.TooLarge, ProtocolCode(await client.NextAnswerAsync(Token)));
        await client.Completion.WaitAsync(Token);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, client.CloseStatus);
    }

    [Fact]
    public async Task DuplicateAndStaleCommandsOverTheWire()
    {
        var (one, two, code) = await Pair();
        var pending = one.Mirror.State.Pending!;
        var actor = pending.Seat == Seat.One ? one : two;
        var other = actor == one ? two : one;
        var command = new ChooseDeploymentZone(pending.Seat, one.Mirror.State.Setup!.ZoneIds[1]);
        var known = actor.Mirror.Events;

        var first = Assert.IsType<Accepted>(await actor.RequestAsync(new SubmitCommand("zone", 1, known, command), Token));
        var log = _server.Lobby.LogOf(code)!.Count;

        // A retry of the same command (say, after a lost answer) gets the same answer and changes nothing.
        Assert.Equal(first with { Duplicate = true }, await actor.RequestAsync(new SubmitCommand("zone", 1, known, command), Token));
        Assert.Equal(first with { Duplicate = true }, await actor.RequestAsync(new SubmitCommand("zone", 1, actor.Mirror.Events, command), Token));
        Assert.Equal(log, _server.Lobby.LogOf(code)!.Count);
        Assert.Single(_server.Lobby.LogOf(code)!, e => e.Event is DeploymentZoneChosen);

        // The same id for something else, a repeated or skipped number, and an old view are all refused.
        Assert.Equal(ProtocolError.IdReused, ProtocolCode(await actor.RequestAsync(new SubmitCommand("zone", 2, actor.Mirror.Events, new EndActivation(pending.Seat)), Token)));
        Assert.Equal(ProtocolError.OutOfOrder, ProtocolCode(await actor.RequestAsync(new SubmitCommand("again", 1, actor.Mirror.Events, command), Token)));
        Assert.Equal(ProtocolError.OutOfOrder, ProtocolCode(await actor.RequestAsync(new SubmitCommand("skip", 5, actor.Mirror.Events, command), Token)));
        var stale = Assert.IsType<Rejected>(await actor.RequestAsync(new SubmitCommand("old", 2, known, new EndActivation(pending.Seat)), Token));
        Assert.Equal(ProtocolError.Stale.ToString(), stale.Code);
        Assert.Equal(log, stale.Events);

        // The opponent's numbering is its own.
        Assert.Equal(RejectionKind.Rules, Assert.IsType<Rejected>(await other.SubmitAsync(new EndActivation(other.Seat!.Value), Token)).Kind);
        Assert.Equal(log, _server.Lobby.LogOf(code)!.Count);
        await other.WaitForAsync(c => c.Mirror.Events == log, Token);
        AssertAgreement(code, one, two);
    }

    // The sender sends a command and closes at once. Its whole message reached the server before the close, so the
    // command is applied exactly once, the match is then aborted, and the opponent's copy holds exactly the server's log.
    [Fact]
    public async Task ACommandSentJustBeforeADisconnectIsAppliedExactlyOnce()
    {
        var (one, two, code) = await Pair();
        var pending = one.Mirror.State.Pending!;
        var actor = pending.Seat == Seat.One ? one : two;
        var watcher = actor == one ? two : one;

        await actor.SendAsync(new SubmitCommand("last", 1, actor.Mirror.Events, new ChooseDeploymentZone(pending.Seat, one.Mirror.State.Setup!.ZoneIds[0])), Token);
        await actor.CloseOutputAsync(Token);

        await watcher.WaitForAsync(c => c.Aborted is not null, Token);
        var log = _server.Lobby.LogOf(code)!;
        Assert.Single(log, e => e.Event is DeploymentZoneChosen);
        Assert.Equal(new MatchAborted(AbortReason.Disconnected, actor.Seat, log.Count, log[^1].Hash), watcher.Aborted);
        Assert.Null(watcher.Mirror.State.Outcome);
        Assert.Equal(MatchStatus.Aborted, _server.Lobby.StatusOf(code));
        AssertAgreement(code, watcher);

        // The match accepts nothing more.
        Assert.Equal(ProtocolError.MatchAborted, ProtocolCode(await watcher.SubmitAsync(new EndActivation(watcher.Seat!.Value), Token)));
    }

    // A dropped connection (no close handshake) right after sending: the command is applied at most once, never twice,
    // and the survivor's copy matches the server either way.
    [Fact]
    public async Task ACommandFollowedByADroppedConnectionIsNeverAppliedTwice()
    {
        for (var round = 0; round < 10; round++)
        {
            var (one, two, code) = await Pair();
            var pending = one.Mirror.State.Pending!;
            var actor = pending.Seat == Seat.One ? one : two;
            var watcher = actor == one ? two : one;
            var command = new ChooseDeploymentZone(pending.Seat, one.Mirror.State.Setup!.ZoneIds[0]);
            await actor.SendAsync(new SubmitCommand("drop", 1, actor.Mirror.Events, command), Token);
            await actor.SendAsync(new SubmitCommand("drop", 1, actor.Mirror.Events, command), Token);
            actor.Abort();

            await watcher.WaitForAsync(c => c.Aborted is not null, Token);
            var log = _server.Lobby.LogOf(code)!;
            Assert.InRange(log.Count(e => e.Event is DeploymentZoneChosen), 0, 1);
            Assert.Equal(AbortReason.Disconnected, watcher.Aborted!.Reason);
            Assert.Equal(log.Count, watcher.Aborted.Events);
            Assert.Equal(log.Select(e => e.Hash), watcher.Hashes);
        }
    }

    [Fact]
    public async Task ADisconnectMidMatchAbortsItWithNoWinner()
    {
        var (one, two, code) = await Pair("melee");
        await two.CloseAsync(Token);
        await one.WaitForAsync(c => c.Aborted is not null, Token);
        Assert.Equal(AbortReason.Disconnected, one.Aborted!.Reason);
        Assert.Equal(Seat.Two, one.Aborted.Seat);
        Assert.Null(one.Mirror.State.Outcome);
        Assert.DoesNotContain(_server.Lobby.LogOf(code)!, e => e.Event is MatchEnded);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, two.CloseStatus);

        // When the last player leaves, the match is gone.
        await one.CloseAsync(Token);
        await Until(() => _server.Lobby.LogOf(code) is null);
    }

    // A client that stops reading. It joins a match as Seat Two on a socket with a 4 KB receive buffer and never calls
    // receive. The server has three ways to notice, and each case leaves only one of them on:
    // - "queue": the client keeps sending at a steady pace, each message earning an answer it never reads. Once its TCP
    //   window is full the server's send blocks and its outbound queue for the client fills up.
    // - "send-timeout": the same traffic, but the queue is too large to fill; the blocked send times out.
    // - "keep-alive": the client goes quiet after joining. Nothing blocks, but it never answers a ping (a WebSocket
    //   only answers pings while it is reading).
    // A control client sends the same traffic at the same pace but reads its answers, and must not be dropped.
    [Theory]
    [InlineData("queue")]
    [InlineData("send-timeout")]
    [InlineData("keep-alive")]
    public async Task AClientThatStopsReadingIsDroppedAndOnlyItsMatchIsAborted(string detection)
    {
        var off = TimeSpan.FromMinutes(10);
        await _server.DisposeAsync();
        _server = detection switch
        {
            "queue" => await Start(keepAlive: TimeSpan.Zero, queueLimit: 256, sendTimeout: off),
            "send-timeout" => await Start(keepAlive: TimeSpan.Zero, queueLimit: 1_000_000, sendTimeout: TimeSpan.FromSeconds(1)),
            _ => await Start(keepAlive: TimeSpan.FromMilliseconds(250), sendTimeout: off),
        };

        // Another match, which must carry on untouched.
        var (other1, other2, otherCode) = await Pair("melee");

        var one = await Connect();
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync("ranged", Token));
        var aborts = 0;
        one.Received += m => { if (m is MatchAborted) Interlocked.Increment(ref aborts); };
        using var stalled = new ClientWebSocket();
        stalled.Options.AddSubProtocol(MatchServer.SubProtocol);
        stalled.Options.KeepAliveInterval = TimeSpan.Zero;
        using var smallBuffer = new HttpMessageInvoker(new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
                {
                    ReceiveBufferSize = 4096,
                    NoDelay = true,
                };
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            },
        });
        await stalled.ConnectAsync(_server.Endpoint, smallBuffer, Token);
        var control = await Connect();
        await stalled.SendAsync(Codec.Encode(new JoinMatch(welcome.Code)), WebSocketMessageType.Text, true, Token);
        await one.WaitForAsync(c => c.Notices.Any(n => n.Kind == NoticeKind.OpponentJoined) && c.Mirror.Events == _server.Lobby.LogOf(welcome.Code)!.Count, Token);
        var events = one.Mirror.Events;

        // Until the server gives up on the stalled client: in the traffic cases, 16 messages per tick from each client,
        // each refused with an answer (the stalled client never reads its own; the control client does). The loop ends
        // when the server has dropped the stalled connection; the test's own timeout bounds it.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var junk = Encoding.UTF8.GetBytes("x");
        var sent = 0;
        var stalledOpen = true;
        while (_server.Lobby.StatusOf(welcome.Code) != MatchStatus.Aborted)
        {
            for (var i = 0; i < (detection == "keep-alive" ? 0 : 16); i++)
            {
                if (stalledOpen)
                {
                    try
                    {
                        await stalled.SendAsync(junk, WebSocketMessageType.Text, true, Token);
                    }
                    catch (WebSocketException)
                    {
                        // The server dropped the socket under a send; wait for the Lobby to show the abort.
                        stalledOpen = false;
                    }
                }
                await control.SendRawAsync(junk, cancellationToken: Token);
                sent++;
            }
            await Task.Delay(1, Token);
        }
        await one.WaitForAsync(c => c.Aborted is not null, Token);
        var detected = watch.Elapsed;
        Assert.True(detected < TimeSpan.FromSeconds(30), $"{detection}: detection took {detected} ({sent} messages)");

        // The documented aborted state: Seat Two disconnected, no winner, no MatchEnded, no outcome.
        var log = _server.Lobby.LogOf(welcome.Code)!;
        Assert.Equal(new MatchAborted(AbortReason.Disconnected, Seat.Two, events, log[^1].Hash), one.Aborted);
        Assert.Equal(events, log.Count);
        Assert.DoesNotContain(log, e => e.Event is MatchEnded);
        Assert.Null(one.Mirror.State.Outcome);
        Assert.Equal(MatchStatus.Aborted, _server.Lobby.StatusOf(welcome.Code));

        // Exactly once: the answer below is queued after any abort message, so a second one would already be here.
        Assert.Equal(ProtocolError.MatchAborted, ProtocolCode(await one.SubmitAsync(new EndActivation(Seat.One), Token)));
        Assert.Equal(1, Volatile.Read(ref aborts));
        AssertAgreement(welcome.Code, one);

        // The other match plays on, and the server still takes new matches.
        var pending = other1.Mirror.State.Pending!;
        var actor = pending.Seat == Seat.One ? other1 : other2;
        var accepted = Assert.IsType<Accepted>(await actor.SubmitAsync(new ChooseDeploymentZone(pending.Seat, other1.Mirror.State.Setup!.ZoneIds[0]), Token));
        await other1.WaitForAsync(c => c.Mirror.Events == accepted.LastEvent, Token);
        await other2.WaitForAsync(c => c.Mirror.Events == accepted.LastEvent, Token);
        Assert.Equal(MatchStatus.Playing, _server.Lobby.StatusOf(otherCode));
        Assert.Null(other1.Aborted);
        Assert.Null(other2.Aborted);
        AssertAgreement(otherCode, other1, other2);
        var late = await Connect();
        Assert.IsType<Welcome>(await late.CreateAsync("ranged", Token));

        // The control client, which sent as much at the same pace but read its answers, is still connected and got an
        // answer for every message.
        Assert.False(control.IsClosed);
        for (var i = 0; i < sent; i++)
        {
            Assert.Equal(ProtocolError.Malformed, ProtocolCode(await control.NextAnswerAsync(Token)));
        }
        await Until(() => _server.ConnectionCount == 5);
    }

    [Fact]
    public async Task AConnectionThatNeverJoinsIsTimedOut()
    {
        await _server.DisposeAsync();
        _server = await Start(joinTimeout: TimeSpan.FromMilliseconds(300));
        var idle = await Connect();
        Assert.Equal(ProtocolError.JoinTimeout, ProtocolCode(await idle.NextAnswerAsync(Token)));
        await idle.Completion.WaitAsync(Token);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, idle.CloseStatus);

        // A seated player is not timed out.
        var (one, _, _) = await Pair();
        await Task.Delay(600, Token);
        Assert.False(one.IsClosed);
    }

    [Fact]
    public async Task GracefulShutdownTellsEveryoneAndClosesCleanly()
    {
        var (one, two, code) = await Pair();
        var waiting = await Connect();
        Assert.IsType<Welcome>(await waiting.CreateAsync("melee", Token));
        var idle = await Connect();
        var events = _server.Lobby.LogOf(code)!.Count;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await _server.StopAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"shutdown took {watch.Elapsed}");

        foreach (var client in new[] { one, two, waiting })
        {
            await client.Completion.WaitAsync(Token);
            Assert.Equal(AbortReason.ServerShutdown, client.Aborted?.Reason);
            Assert.Contains(client.Notices, n => n.Kind == NoticeKind.ServerShutdown);
            Assert.Null(client.Mirror.State.Outcome);
            Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, client.CloseStatus);
            Assert.Null(client.Failure);
        }
        Assert.Equal(events, one.Mirror.Events);
        Assert.Equal(one.Mirror.Hash, two.Mirror.Hash);
        await idle.Completion.WaitAsync(Token);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, idle.CloseStatus);

        await Assert.ThrowsAnyAsync<Exception>(() => ClientSession.ConnectAsync(_server.Endpoint, Token));
    }
}
