using System.Net;
using System.Net.Sockets;
using System.Text;
using Client.Core;
using Match.Client;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// M7.4 stage 4c review: a command whose answer is lost with the connection. The screen says UNKNOWN until the seat is
// back; then only the server's own answer to that command (matched by its command id) may say it was done or refused.
// An older command's answer must never be shown as this one's, and a command the server never received is "not taken".
public sealed partial class TransportTests
{
    public enum Loss
    {
        AnswerLostAfterAccepted,
        AnswerLostAfterRulesRefusal,
        CommandNeverArrived,
    }

    [Theory]
    [InlineData(Loss.AnswerLostAfterAccepted)]
    [InlineData(Loss.AnswerLostAfterRulesRefusal)]
    [InlineData(Loss.CommandNeverArrived)]
    public async Task ALostAnswerIsReportedFromTheServersAnswerToThatCommandAfterTheRejoin(Loss loss)
    {
        var relays = new List<CuttingRelay>();
        MatchController Relayed()
        {
            var relay = new CuttingRelay(_server.Endpoint);
            relays.Add(relay);
            var first = true;
            // The first connection goes through the relay; the automatic rejoin connects straight to the server.
            return Controller(connect: async (uri, mirror, ct) =>
            {
                var session = await ClientSession.ConnectAsync(first ? relay.Endpoint : uri, ct, mirror: mirror);
                first = false;
                lock (_clients)
                {
                    _clients.Add(session);
                }
                return session;
            });
        }
        try
        {
            var one = Relayed();
            var welcome = Assert.IsType<Welcome>(await one.CreateAsync("melee", Token));
            var two = Relayed();
            Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
            await Settle(_server, welcome.Code, one.Session!, two.Session!);
            await Until(one, s => s.InputLocked is null || s.View.YourMove == false);
            var mover = one.Snapshot().Hints.Count > 0 ? one : two;
            var relay = relays[mover == one ? 0 : 1];
            await Until(mover, s => s.InputLocked is null);
            var seat = mover.Snapshot().Seat!.Value;
            var hint = mover.Snapshot().Hints[0];
            // No activation is in progress at the start, so the rules refuse this (a rules refusal is answered and counted).
            var refusable = new EndActivation(seat);

            if (loss == Loss.CommandNeverArrived)
            {
                // The seat's last answer on the server is then a rules refusal of an older command.
                Assert.Equal(FeedbackKind.RulesRefused, (await mover.SendAsync(refusable, "End activation", Token)).Kind);
            }
            var log = _server.Lobby.LogOf(welcome.Code)!.Count;
            var dropped = mover.Session!;
            relay.Cut = loss == Loss.CommandNeverArrived ? CuttingRelay.Mode.BeforeTheCommand : CuttingRelay.Mode.AtTheAnswer;
            var (command, label) = loss == Loss.AnswerLostAfterRulesRefusal ? ((Command)refusable, "End activation") : (hint.Command, hint.Label);
            var lost = await mover.SendAsync(command, label, Token);
            Assert.Equal(FeedbackKind.Unknown, lost.Kind);
            Assert.True(relay.HasCut);

            var back = await Until(mover, s => s.Connection == ConnectionState.Connected && !ReferenceEquals(mover.Session, dropped)
                && s.Feedback is { Kind: not FeedbackKind.Unknown });
            var after = _server.Lobby.LogOf(welcome.Code)!.Count;
            switch (loss)
            {
                case Loss.AnswerLostAfterAccepted:
                    Assert.Equal(FeedbackKind.Accepted, back.Feedback!.Kind);
                    Assert.StartsWith($"Done: {label}.", back.Feedback.Text);
                    Assert.True(after > log, "the accepted command's events are on the server");
                    break;
                case Loss.AnswerLostAfterRulesRefusal:
                    Assert.Equal(FeedbackKind.RulesRefused, back.Feedback!.Kind);
                    Assert.StartsWith("Refused by the rules: ", back.Feedback.Text);
                    Assert.NotNull(back.Feedback.RuleRef);
                    Assert.Equal(log, after);
                    break;
                case Loss.CommandNeverArrived:
                    // The server's last answer is the older refusal: it must not be reported as this command's answer.
                    Assert.Equal(FeedbackKind.ProtocolRefused, back.Feedback!.Kind);
                    Assert.Contains("never received", back.Feedback.Text);
                    Assert.Contains(label, back.Feedback.Text);
                    Assert.Equal(log, after);
                    break;
            }
            await Settle(_server, welcome.Code, one.Session!, two.Session!);
            AssertSameAsServer(_server, welcome.Code, one.Session!, two.Session!);
        }
        finally
        {
            foreach (var r in relays)
            {
                r.Dispose();
            }
        }
    }

    // A TCP relay between one client and the server that can lose a message on purpose: the server's answer to a
    // command (the first server message after arming that carries a "commandId"), or the client's next message. Either
    // way it then drops both connections, as a network failure would.
    private sealed class CuttingRelay : IDisposable
    {
        public enum Mode
        {
            None,
            AtTheAnswer,
            BeforeTheCommand,
        }

        private static readonly byte[] AnswerMarker = Encoding.UTF8.GetBytes("\"commandId\"");
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Uri _server;
        private readonly List<Socket> _sockets = [];
        private volatile int _mode;
        private volatile bool _cut;

        public CuttingRelay(Uri server)
        {
            _server = server;
            _listener.Start();
            Endpoint = new Uri($"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{server.AbsolutePath}");
            _ = Task.Run(AcceptAsync);
        }

        public Uri Endpoint { get; }

        public Mode Cut
        {
            get => (Mode)_mode;
            set => _mode = (int)value;
        }

        public bool HasCut => _cut;

        private async Task AcceptAsync()
        {
            try
            {
                var client = await _listener.AcceptSocketAsync();
                var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await server.ConnectAsync(IPAddress.Loopback, _server.Port);
                lock (_sockets)
                {
                    _sockets.Add(client);
                    _sockets.Add(server);
                }
                _ = Task.Run(() => PumpAsync(client, server, fromClient: true));
                _ = Task.Run(() => PumpAsync(server, client, fromClient: false));
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
            }
        }

        private async Task PumpAsync(Socket from, Socket to, bool fromClient)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    var read = await from.ReceiveAsync(buffer, SocketFlags.None);
                    if (read == 0)
                    {
                        break;
                    }
                    var chunk = buffer.AsSpan(0, read);
                    var lose = (fromClient && Cut == Mode.BeforeTheCommand) || (!fromClient && Cut == Mode.AtTheAnswer && chunk.IndexOf(AnswerMarker) >= 0);
                    if (lose)
                    {
                        _cut = true;
                        Drop();
                        return;
                    }
                    await to.SendAsync(buffer.AsMemory(0, read), SocketFlags.None);
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
            }
            Drop();
        }

        private void Drop()
        {
            lock (_sockets)
            {
                foreach (var socket in _sockets)
                {
                    try
                    {
                        socket.LingerState = new LingerOption(true, 0);
                        socket.Close();
                    }
                    catch (Exception e) when (e is SocketException or ObjectDisposedException)
                    {
                    }
                }
                _sockets.Clear();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            Drop();
        }
    }
}
