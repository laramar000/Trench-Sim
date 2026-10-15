using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Match.Client;
using Match.Protocol;

namespace Client.Tests;

// What a fake host does with one connection: complete the WebSocket handshake or never answer it, then send these
// messages after the client's first message (none: never answer).
internal sealed record FakeReply(bool Handshake, params ServerMessage[] Messages)
{
    public static readonly FakeReply NoHandshake = new(false);
    public static readonly FakeReply Silent = new(true);
}

// A host that misbehaves on purpose, for reconnect tests: it accepts connections and then hangs, answers with a refusal,
// or sends what a test scripts. It records when each connection is closed by the client, so tests can check that an
// abandoned attempt left nothing open.
internal sealed class FakeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<int, FakeReply> _script;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _closed = [];
    private readonly TaskCompletionSource _firstAccepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _accepting;

    public FakeServer(Func<int, FakeReply> script)
    {
        _script = script;
        _listener.Start();
        Endpoint = new Uri($"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/match");
        _accepting = Task.Run(AcceptLoopAsync);
    }

    public Uri Endpoint { get; }

    public int Accepted
    {
        get
        {
            lock (_closed)
            {
                return _closed.Count;
            }
        }
    }

    public Task FirstAccepted => _firstAccepted.Task;

    public Func<MatchMirror, CancellationToken, Task<ClientSession>> Connect =>
        (mirror, cancellationToken) => ClientSession.ConnectAsync(Endpoint, cancellationToken, mirror: mirror);

    // Completes once every connection accepted so far has been closed by the client.
    public Task AllClosed()
    {
        lock (_closed)
        {
            return Task.WhenAll(_closed);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (Exception)
        {
        }
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            for (var index = 0; ; index++)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_closed)
                {
                    _closed.Add(ServeAsync(client, _script(index)));
                }
                _firstAccepted.TrySetResult();
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    // Completes when the client closes or drops the connection.
    private async Task ServeAsync(TcpClient client, FakeReply reply)
    {
        using var _ = client;
        var stream = client.GetStream();
        var buffer = new byte[8192];
        try
        {
            if (!reply.Handshake)
            {
                // Read the client's request and never answer it.
                while (await stream.ReadAsync(buffer, _stop.Token) > 0)
                {
                }
                return;
            }
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, _stop.Token);
                if (read == 0)
                {
                    return;
                }
                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            var key = request.ToString().Split("\r\n")
                .First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))["Sec-WebSocket-Key:".Length..].Trim();
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: {ClientSession.SubProtocol}\r\n\r\n"), _stop.Token);
            using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                SubProtocol = ClientSession.SubProtocol,
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
            });
            var answered = false;
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (result.EndOfMessage && !answered)
                {
                    answered = true;
                    foreach (var message in reply.Messages)
                    {
                        await socket.SendAsync(Codec.Encode(message), WebSocketMessageType.Text, true, _stop.Token);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client dropped the connection (or the fake is shutting down).
        }
    }
}
