using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Match.Protocol;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Rules.Core.Dice;
using Rules.Core.State;
using Sim;

namespace Match.Server;

public sealed class MatchServerOptions
{
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    // 0 picks a free port; Endpoint says which.
    public int Port { get; init; }

    public IReadOnlyDictionary<string, MatchSetup>? Scenarios { get; init; }

    // Trench-maps stage S5: the maps a CreateMatch may name; null is the content maps next to the program.
    public IReadOnlyDictionary<string, MapDefinition>? Maps { get; init; }

    // The server's dice, one source per match. Tests inject seeded dice; the real server uses RandomDice.
    public Func<IDiceSource> NewDice { get; init; } = () => new RandomDice();

    // A connection that has not created or joined a match by then is refused and closed.
    public TimeSpan JoinTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(15);

    // How long shutdown waits for connections to take their last messages and close.
    public TimeSpan ShutdownGrace { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxMatches { get; init; } = 64;
    public int MaxConnections { get; init; } = 256;

    // Messages waiting for a client that is not reading. Past this the connection is dropped.
    public int OutboundQueueLimit { get; init; } = 4096;

    // How long one message may take to send. A client that stops reading fills its TCP window, and the next send then
    // waits; past this the connection is dropped. Keep-alive cannot catch that case, because pings queue behind the
    // blocked send.
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(10);

    // How long a lost seat has to rejoin before its paused match is aborted, and how long a match that is over stays
    // open for a read-only rejoin. Zero aborts at once on a disconnect, as M5 did.
    public TimeSpan RejoinWindow { get; init; } = Lobby.DefaultRejoinWindow;

    public TimeProvider Time { get; init; } = TimeProvider.System;

    // How often rejoin deadlines are checked.
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    // Where records, markers and the signing key live. Null keeps nothing on disk (tests that do not need records).
    public string? DataDirectory { get; init; }

    // Creates a signing key even though the keys folder exists (only after the old key was retired).
    public bool NewSigningKey { get; init; }

    // Test hook for record and marker writes.
    public IWriteFaults? WriteFaults { get; init; }

    public Action<string>? Log { get; init; }
}

// The WebSocket transport around the Lobby. One connection is one WebSocket at /match speaking subprotocol
// "trench-sim.v2". The transport only moves whole text messages: every decision is the Lobby's.
public sealed class MatchServer : IAsyncDisposable
{
    public const string SubProtocol = "trench-sim.v2";
    public const string Path = "/match";

    // A message over the size limit is read and thrown away, and refused as TooLarge. One that goes on past this many
    // bytes is not worth reading: the connection is closed with MessageTooBig.
    public const int MaxDiscardBytes = 1024 * 1024;

    private readonly MatchServerOptions _options;
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<ConnectionId, Connection> _connections = new();
    private long _nextConnection;
    private volatile bool _stopping;
    private readonly ServerData? _data;
    private readonly ITimer _ticker;

    private MatchServer(MatchServerOptions options, ServerData? data)
    {
        _data = data;
        _options = options;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(options.Address, options.Port));
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = options.ShutdownGrace);
        _app = builder.Build();

        Lobby = new Lobby(
            options.Scenarios ?? ScenarioCatalog.FromContent(),
            options.NewDice,
            new Outbox(Send, CloseForPolicy, SendBacklog),
            maxMatches: options.MaxMatches,
            time: options.Time,
            rejoinWindow: options.RejoinWindow,
            recorder: (IMatchRecorder?)data?.Writer ?? NullRecorder.Instance,
            maps: options.Maps ?? ScenarioCatalog.Maps);
        _ticker = options.Time.CreateTimer(_ => Lobby.Tick(), null, options.TickInterval, options.TickInterval);

        // A client that does not answer a ping within twice the interval is dropped; a zero interval turns pings off.
        _app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = options.KeepAlive,
            KeepAliveTimeout = options.KeepAlive > TimeSpan.Zero ? options.KeepAlive * 2 : Timeout.InfiniteTimeSpan,
        });
        _app.Map(Path, HandleAsync);
        _app.Lifetime.ApplicationStopping.Register(BeginShutdown);
    }

    public Lobby Lobby { get; }

    // Null when the server keeps nothing on disk.
    public RecordWriter? Records => _data?.Writer;
    public SigningKey? SigningKey => _data?.Key;
    public RecoveryReport? Recovery => _data?.Recovery;

    public Uri Endpoint { get; private set; } = null!;

    public int ConnectionCount => _connections.Count;

    public static async Task<MatchServer> StartAsync(MatchServerOptions options, CancellationToken cancellationToken = default)
    {
        var data = options.DataDirectory is null ? null : ServerData.Open(options.DataDirectory, options.NewSigningKey, options.WriteFaults, options.Log, options.Time);
        MatchServer server;
        try
        {
            server = new MatchServer(options, data);
        }
        catch
        {
            data?.Dispose();
            throw;
        }
        await server._app.StartAsync(cancellationToken);
        var address = new Uri(server._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        server.Endpoint = new UriBuilder("ws", address.Host, address.Port, Path).Uri;
        return server;
    }

    // Returns when the host is told to stop (Ctrl+C, SIGTERM), after the graceful shutdown.
    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) => _app.WaitForShutdownAsync(cancellationToken);

    // Graceful shutdown: every seated player is told (MatchAborted for a match in progress, then ServerShutdown), every
    // connection gets its queued messages and a Going Away close, then the host stops.
    public Task StopAsync() => _app.StopAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _ticker.DisposeAsync();
        _data?.Dispose();
    }

    // Aborts what is in progress (each started match queues its Aborted record), closes every connection, and gives
    // the record writer the shutdown grace to make those records durable. What it cannot finish keeps its marker, and
    // the next start records it as Unrecorded.
    private void BeginShutdown()
    {
        _stopping = true;
        _ticker.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Lobby.Shutdown();
        foreach (var connection in _connections.Values)
        {
            connection.Close(WebSocketCloseStatus.EndpointUnavailable, "server shutting down");
        }
        if (_data is not null && !_data.Writer.CompleteAsync(_options.ShutdownGrace).GetAwaiter().GetResult())
        {
            _options.Log?.Invoke("shutdown: some records were not written in time; they will be recorded as Unrecorded at the next start");
        }
    }

    private void Send(ConnectionId to, ServerMessage message)
    {
        if (_connections.TryGetValue(to, out var connection))
        {
            connection.Send(message);
        }
    }

    private void SendBacklog(ConnectionId to, IReadOnlyList<ServerMessage> messages)
    {
        if (_connections.TryGetValue(to, out var connection))
        {
            connection.SendBacklog(messages);
        }
    }

    private void CloseForPolicy(ConnectionId to)
    {
        if (_connections.TryGetValue(to, out var connection))
        {
            connection.Close(WebSocketCloseStatus.PolicyViolation, "seat token");
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("This endpoint speaks WebSocket only.");
            return;
        }
        // Version negotiation happens at the handshake: a client that does not offer this protocol version is refused
        // before any message is read.
        if (!context.WebSockets.WebSocketRequestedProtocols.Contains(SubProtocol))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"This server speaks the WebSocket subprotocol {SubProtocol}.");
            return;
        }
        if (_stopping || _connections.Count >= _options.MaxConnections)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { SubProtocol = SubProtocol });
        var id = new ConnectionId(Interlocked.Increment(ref _nextConnection));
        var connection = new Connection(socket, _options.OutboundQueueLimit, _options.SendTimeout);
        _connections[id] = connection;
        if (_stopping)
        {
            connection.Close(WebSocketCloseStatus.EndpointUnavailable, "server shutting down");
        }
        var writer = connection.RunWriterAsync();
        _ = EnforceJoinTimeoutAsync(id, connection);
        // Once the server decides to close, the client gets ShutdownGrace to answer before the read is cut off.
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        using var deadline = connection.Closing.Register(() => reading.CancelAfter(_options.ShutdownGrace));
        try
        {
            await ReceiveAsync(id, connection, reading.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
        {
            // The connection failed; handled below like any other disconnect.
        }
        finally
        {
            _connections.TryRemove(id, out _);
            // Every whole message received before this point has already been fully processed by the Lobby (Receive
            // returns only when done), so the disconnect is ordered after them: nothing is half-applied or repeated.
            Lobby.Disconnect(id);
            connection.Close(WebSocketCloseStatus.NormalClosure, "bye");
            await connection.FinishAsync(writer, _options.ShutdownGrace);
        }
    }

    private async Task ReceiveAsync(ConnectionId id, Connection connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[ProtocolLimits.MaxClientMessageBytes];
        var discard = new byte[4096];
        var socket = connection.Socket;
        while (socket.State == WebSocketState.Open && !connection.IsClosing)
        {
            var count = 0;
            long total = 0;
            var tooLarge = false;
            ValueWebSocketReceiveResult result;
            do
            {
                var target = tooLarge || count == buffer.Length ? discard.AsMemory() : buffer.AsMemory(count);
                result = await socket.ReceiveAsync(target, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                total += result.Count;
                if (!tooLarge && count < buffer.Length && target.Length != discard.Length)
                {
                    count += result.Count;
                }
                else if (result.Count > 0)
                {
                    tooLarge = true;
                }
                if (total > MaxDiscardBytes)
                {
                    Lobby.RefuseUnread(id, ProtocolError.TooLarge, $"A message is at most {ProtocolLimits.MaxClientMessageBytes} bytes.");
                    connection.Close(WebSocketCloseStatus.MessageTooBig, "message too large");
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                Lobby.RefuseUnread(id, ProtocolError.Malformed, "Messages are UTF-8 text.");
            }
            else if (tooLarge)
            {
                Lobby.RefuseUnread(id, ProtocolError.TooLarge, $"A message is at most {ProtocolLimits.MaxClientMessageBytes} bytes.");
            }
            else
            {
                Lobby.Receive(id, buffer.AsSpan(0, count));
            }
        }
    }

    private async Task EnforceJoinTimeoutAsync(ConnectionId id, Connection connection)
    {
        try
        {
            await Task.Delay(_options.JoinTimeout, connection.Closing);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!Lobby.IsSeated(id))
        {
            Lobby.RefuseUnread(id, ProtocolError.JoinTimeout, "Create or join a match sooner.");
            connection.Close(WebSocketCloseStatus.PolicyViolation, "join timeout");
        }
    }

    // One socket: a single writer task sends queued messages in order, then the close.
    private sealed class Connection(WebSocket socket, int queueLimit, TimeSpan sendTimeout)
    {
        // Live messages count against the limit; a rejoin's backlog (bounded by the match's own length) does not.
        private readonly Channel<(ServerMessage Message, bool Counted)> _outbound = Channel.CreateUnbounded<(ServerMessage, bool)>(new UnboundedChannelOptions { SingleReader = true });
        private int _pending;
        private readonly CancellationTokenSource _closing = new();
        private readonly CancellationTokenSource _abort = new();
        private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
        private string _closeText = "";
        private int _closed;

        public WebSocket Socket { get; } = socket;
        public CancellationToken Closing => _closing.Token;
        public bool IsClosing => Volatile.Read(ref _closed) != 0;

        public void Send(ServerMessage message)
        {
            if (Volatile.Read(ref _pending) >= queueLimit)
            {
                // The client stopped reading. Dropping it is the only safe choice: its copy would fall behind. This runs
                // under the Lobby's lock, so the cancellation (which can run the reader's code inline) goes elsewhere.
                if (!IsClosing)
                {
                    _ = Task.Run(_abort.Cancel);
                }
                return;
            }
            if (_outbound.Writer.TryWrite((message, true)))
            {
                Interlocked.Increment(ref _pending);
            }
        }

        public void SendBacklog(IReadOnlyList<ServerMessage> messages)
        {
            foreach (var message in messages)
            {
                _outbound.Writer.TryWrite((message, false));
            }
        }

        // Sends what is queued, then closes with this status. The first close wins.
        public void Close(WebSocketCloseStatus status, string text)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }
            _closeStatus = status;
            _closeText = text;
            _outbound.Writer.TryComplete();
            _closing.Cancel();
        }

        public async Task RunWriterAsync()
        {
            try
            {
                await foreach (var (message, counted) in _outbound.Reader.ReadAllAsync(_abort.Token))
                {
                    if (counted)
                    {
                        Interlocked.Decrement(ref _pending);
                    }
                    using var send = CancellationTokenSource.CreateLinkedTokenSource(_abort.Token);
                    send.CancelAfter(sendTimeout);
                    await Socket.SendAsync(Codec.Encode(message), WebSocketMessageType.Text, endOfMessage: true, send.Token);
                }
                if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await Socket.CloseOutputAsync(_closeStatus, _closeText, _abort.Token);
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
            {
                Socket.Abort();
            }
        }

        // Lets the writer finish (with a time limit), then makes sure the socket is gone.
        public async Task FinishAsync(Task writer, TimeSpan grace)
        {
            if (await Task.WhenAny(writer, Task.Delay(grace)) != writer)
            {
                _abort.Cancel();
                await writer;
            }
            if (Socket.State != WebSocketState.Closed)
            {
                // Give the client a moment to answer our close, then drop it.
                using var wait = new CancellationTokenSource(grace);
                try
                {
                    while (Socket.State == WebSocketState.CloseSent)
                    {
                        var result = await Socket.ReceiveAsync(new byte[256].AsMemory(), wait.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
                    }
                }
                catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
                {
                }
                Socket.Abort();
            }
        }
    }
}
