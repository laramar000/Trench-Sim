using System.Net.WebSockets;
using System.Threading.Channels;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Match.Cli;

// One client connection. A single receive loop reads the server's messages in order: events go into the mirror (each
// checked against the server's hash), answers (Welcome, Accepted, Rejected) into a queue. The client holds no authority:
// its state is only what the server's events build.
public sealed class ClientSession : IAsyncDisposable
{
    public const string SubProtocol = "trench-sim.v2";

    private readonly ClientWebSocket _socket;
    private readonly Channel<ServerMessage> _answers = Channel.CreateUnbounded<ServerMessage>();
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly HashSet<Seat> _paused = [];

    // Command ids must never repeat for a seat, even from a new process after a crash, so each session has its own prefix.
    private readonly string _idPrefix = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _hashes = [];
    private readonly List<Notice> _notices = [];
    private readonly object _gate = new();
    private TaskCompletionSource _changed = NewSignal();
    private long _seq;
    private int _ids;
    private Task _receiving = Task.CompletedTask;

    private ClientSession(ClientWebSocket socket, MatchMirror mirror)
    {
        _socket = socket;
        Mirror = mirror;
    }

    // The client's copy of the match. A session that rejoins after a network drop can keep the copy it had.
    public MatchMirror Mirror { get; }

    // The newest seat token this session received. Never printed or logged.
    public string? SeatToken { get; private set; }

    // Called on the receive loop with each Welcome that carries a new token. Return true once the token is durably
    // saved; only then is TokenSaved sent, and only then does the server stop accepting the previous token.
    public Func<Welcome, bool>? SaveToken { get; set; }

    // Seats the server says are away (the match is paused).
    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _paused.Count > 0;
            }
        }
    }

    // True once the copy holds the events and hash the last Welcome named: the client is in step with the server.
    public bool Synchronized { get; private set; }

    // The last command sent with SubmitAsync, for resending after a rejoin.
    public SubmitCommand? LastSubmit { get; private set; }
    public Welcome? Welcome { get; private set; }
    public Seat? Seat => Welcome?.Seat;
    public MatchAborted? Aborted { get; private set; }

    // Set when the server broke the protocol or the replay failed: the copy can no longer be trusted.
    public string? Failure { get; private set; }

    public bool IsClosed { get; private set; }
    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;
    public string? CloseText => _socket.CloseStatusDescription;

    // The hash the server sent with each event, in order.
    public IReadOnlyList<string> Hashes
    {
        get
        {
            lock (_gate)
            {
                return [.. _hashes];
            }
        }
    }

    public IReadOnlyList<Notice> Notices
    {
        get
        {
            lock (_gate)
            {
                return [.. _notices];
            }
        }
    }

    public long Seq => _seq;

    // Raised on the receive loop after each message is handled.
    public event Action<ServerMessage>? Received;

    public static async Task<ClientSession> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default, string subProtocol = SubProtocol, MatchMirror? mirror = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(subProtocol);
        await socket.ConnectAsync(endpoint, cancellationToken);
        var session = new ClientSession(socket, mirror ?? new MatchMirror());
        session._receiving = Task.Run(session.ReceiveLoopAsync);
        return session;
    }

    public async Task<ServerMessage> CreateAsync(string scenario, CancellationToken cancellationToken = default) =>
        await RequestAsync(new CreateMatch(scenario), cancellationToken);

    public async Task<ServerMessage> JoinAsync(string code, CancellationToken cancellationToken = default) =>
        await RequestAsync(new JoinMatch(code), cancellationToken);

    // Takes a seat back with its token, offering the copy this session holds. The server replays what is missing, or
    // everything if the copy is not a prefix of the match.
    public async Task<ServerMessage> RejoinAsync(string code, string seatToken, CancellationToken cancellationToken = default) =>
        await RequestAsync(new Rejoin(code, seatToken, Mirror.Events, Mirror.Events == 0 ? null : Mirror.Hash), cancellationToken);

    // Tells the server the last token is saved. SaveToken does this automatically.
    public Task ConfirmTokenAsync(CancellationToken cancellationToken = default) => SendAsync(new TokenSaved(), cancellationToken);

    // Sends a command again with the same id, seq and command, and the events applied now. The server answers from its
    // cache if it already handled it (duplicate: true), and otherwise decides it as new.
    public async Task<ServerMessage> ResendAsync(SubmitCommand original, CancellationToken cancellationToken = default)
    {
        await _requests.WaitAsync(cancellationToken);
        try
        {
            var submit = new SubmitCommand(original.CommandId, original.Seq, Mirror.Events, original.Command);
            await SendAsync(submit, cancellationToken);
            var answer = await NextAnswerAsync(cancellationToken);
            if ((answer is Accepted || answer is Rejected { Kind: RejectionKind.Rules }) && submit.Seq > _seq)
            {
                _seq = submit.Seq;
            }
            return answer;
        }
        finally
        {
            _requests.Release();
        }
    }

    // Sends a command for this seat with the next number, a fresh id and the events applied so far, and returns the
    // server's answer. The answer comes after the command's events, so the mirror already holds them.
    public async Task<ServerMessage> SubmitAsync(Command command, CancellationToken cancellationToken = default)
    {
        await _requests.WaitAsync(cancellationToken);
        try
        {
            var submit = new SubmitCommand($"{_idPrefix}-{Interlocked.Increment(ref _ids)}", _seq + 1, Mirror.Events, command);
            LastSubmit = submit;
            await SendAsync(submit, cancellationToken);
            var answer = await NextAnswerAsync(cancellationToken);
            if (answer is Accepted || answer is Rejected { Kind: RejectionKind.Rules })
            {
                _seq = submit.Seq;
            }
            return answer;
        }
        finally
        {
            _requests.Release();
        }
    }

    public async Task<ServerMessage> RequestAsync(ClientMessage message, CancellationToken cancellationToken = default)
    {
        await _requests.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(message, cancellationToken);
            return await NextAnswerAsync(cancellationToken);
        }
        finally
        {
            _requests.Release();
        }
    }

    public Task SendAsync(ClientMessage message, CancellationToken cancellationToken = default) =>
        SendRawAsync(Codec.Encode(message), WebSocketMessageType.Text, cancellationToken);

    // For tests and tools: any bytes, in one or more frames.
    public async Task SendRawAsync(byte[] bytes, WebSocketMessageType type = WebSocketMessageType.Text, CancellationToken cancellationToken = default, int frameSize = int.MaxValue)
    {
        await _sending.WaitAsync(cancellationToken);
        try
        {
            for (var offset = 0; ;)
            {
                var length = Math.Min(frameSize, bytes.Length - offset);
                var last = offset + length == bytes.Length;
                await _socket.SendAsync(bytes.AsMemory(offset, length), type, last, cancellationToken);
                offset += length;
                if (last)
                {
                    return;
                }
            }
        }
        finally
        {
            _sending.Release();
        }
    }

    // The next Welcome, Accepted or Rejected from the server, in order.
    public async Task<ServerMessage> NextAnswerAsync(CancellationToken cancellationToken = default) =>
        await _answers.Reader.ReadAsync(cancellationToken);

    public bool TryNextAnswer(out ServerMessage? answer) => _answers.Reader.TryRead(out answer);

    // Waits until the condition holds (checked after every message) or the session ends.
    public async Task WaitForAsync(Func<ClientSession, bool> condition, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                changed = _changed.Task;
            }
            if (condition(this))
            {
                return;
            }
            if (IsClosed)
            {
                throw new InvalidOperationException($"The connection closed ({CloseStatus}: {CloseText}){(Failure is null ? "" : $": {Failure}")}.");
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    // Sends our close; the receive loop (the only reader) takes the server's answer and ends.
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException)
        {
        }
        await _receiving.WaitAsync(cancellationToken);
    }

    // Sends our close but keeps reading what the server still sends.
    public Task CloseOutputAsync(CancellationToken cancellationToken = default) =>
        _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);

    // Drops the connection without a close handshake.
    public void Abort() => _socket.Abort();

    public Task Completion => _receiving;

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _socket.Abort();
        try
        {
            await _receiving;
        }
        catch (Exception)
        {
        }
        _socket.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[ProtocolLimits.MaxServerMessageBytes];
        try
        {
            while (true)
            {
                var count = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (count == buffer.Length)
                    {
                        Fail("The server sent a message over the size limit.");
                        return;
                    }
                    result = await _socket.ReceiveAsync(buffer.AsMemory(count), _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (_socket.State == WebSocketState.CloseReceived)
                        {
                            await _socket.CloseOutputAsync(_socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, "bye", _stop.Token);
                        }
                        return;
                    }
                    count += result.Count;
                }
                while (!result.EndOfMessage);

                var message = Codec.ParseServer(buffer.AsSpan(0, count), out var failure);
                if (message is null)
                {
                    Fail($"The server sent an unreadable message: {failure!.Error}.");
                    return;
                }
                if (!await HandleAsync(message))
                {
                    return;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            IsClosed = true;
            _answers.Writer.TryComplete();
            Signal();
        }
    }

    private async Task<bool> HandleAsync(ServerMessage message)
    {
        switch (message)
        {
            case EventPublished published:
                try
                {
                    Mirror.Apply(published);
                }
                catch (MirrorException e)
                {
                    Fail(e.Message);
                    return false;
                }
                lock (_gate)
                {
                    _hashes.Add(published.Hash);
                }
                if (!CheckSynchronized())
                {
                    return false;
                }
                break;
            case MatchPaused paused:
                lock (_gate)
                {
                    _paused.Add(paused.Seat);
                }
                break;
            case MatchResumed:
                lock (_gate)
                {
                    _paused.Clear();
                }
                break;
            case MatchAborted aborted:
                if (aborted.Events != Mirror.Events || aborted.Hash != Mirror.Hash)
                {
                    Fail($"The match was aborted at event {aborted.Events}, but this copy has {Mirror.Events} events.");
                    return false;
                }
                Aborted = aborted;
                break;
            case Notice notice:
                lock (_gate)
                {
                    _notices.Add(notice);
                }
                break;
            case Welcome welcome:
                if (!Accept(welcome))
                {
                    return false;
                }
                if (welcome.SeatToken is not null && SaveToken?.Invoke(welcome) == true)
                {
                    try
                    {
                        await ConfirmTokenAsync(_stop.Token);
                    }
                    catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException)
                    {
                        // Not confirmed: the server keeps the previous token valid too, so nothing is lost.
                    }
                }
                _answers.Writer.TryWrite(message);
                break;
            default:
                _answers.Writer.TryWrite(message);
                break;
        }
        Received?.Invoke(message);
        Signal();
        return true;
    }

    // A Welcome sets the point the copy must reach: Events events with hash Hash. ReplayFrom 0 means start again.
    private bool Accept(Welcome welcome)
    {
        if (welcome.ReplayFrom == 0 && Mirror.Events > 0)
        {
            Mirror.Reset();
            lock (_gate)
            {
                _hashes.Clear();
            }
        }
        else if (welcome.ReplayFrom != Mirror.Events)
        {
            Fail($"The server replays from event {welcome.ReplayFrom + 1}, but this copy has {Mirror.Events} events.");
            return false;
        }
        Welcome = welcome;
        if (welcome.SeatToken is not null)
        {
            SeatToken = welcome.SeatToken;
        }
        _seq = welcome.NextSeq - 1;
        lock (_gate)
        {
            _paused.Clear();
        }
        Synchronized = false;
        return CheckSynchronized();
    }

    private bool CheckSynchronized()
    {
        if (Synchronized || Welcome is not { } welcome || Mirror.Events != welcome.Events)
        {
            return true;
        }
        if (Mirror.Hash != welcome.Hash)
        {
            Fail($"After {welcome.Events} events this copy's hash differs from the server's.");
            return false;
        }
        Synchronized = true;
        return true;
    }

    private void Fail(string reason)
    {
        Failure = reason;
        _socket.Abort();
    }

    private void Signal()
    {
        TaskCompletionSource old;
        lock (_gate)
        {
            old = _changed;
            _changed = NewSignal();
        }
        old.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
