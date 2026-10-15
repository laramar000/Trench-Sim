using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Match.Cli;
using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// M6 over real WebSockets: rejoin as a new process with a saved token, a network drop that keeps the copy, takeover,
// expiry, records on disk, and the server's own restart.
public sealed class ReconnectNetworkTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(3));
    private readonly List<ClientSession> _clients = [];
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("trench-m6-net-");
    private readonly ConcurrentQueue<string> _log = new();
    private MatchServer _server = null!;

    private CancellationToken Token => _timeout.Token;
    private ManualTime Time { get; } = new();
    private string Data => Path.Combine(_dir.FullName, "server");
    private SessionStore Sessions => new(Path.Combine(_dir.FullName, "sessions"));

    public async Task InitializeAsync() => _server = await Start();

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        await _server.DisposeAsync();
        _timeout.Dispose();
        _dir.Delete(recursive: true);
    }

    private Task<MatchServer> Start(int seed = 1, int queueLimit = 4096, TimeSpan? sendTimeout = null, TimeSpan? keepAlive = null) =>
        MatchServer.StartAsync(new MatchServerOptions
        {
            NewDice = () => new SeededDice(seed),
            ShutdownGrace = TimeSpan.FromSeconds(3),
            RejoinWindow = TimeSpan.FromMinutes(5),
            Time = Time,
            TickInterval = TimeSpan.FromMilliseconds(20),
            DataDirectory = Data,
            OutboundQueueLimit = queueLimit,
            SendTimeout = sendTimeout ?? TimeSpan.FromSeconds(10),
            KeepAlive = keepAlive ?? TimeSpan.FromSeconds(15),
            Log = _log.Enqueue,
        }, Token);

    private async Task<ClientSession> Connect(MatchMirror? mirror = null, bool saveTokens = true)
    {
        var client = await ClientSession.ConnectAsync(_server.Endpoint, Token, mirror: mirror);
        if (saveTokens)
        {
            var store = Sessions;
            var server = _server.Endpoint.ToString();
            client.SaveToken = w => store.TrySave(new SavedSeat(server, w.Code, w.MatchId, w.Seat, w.SeatToken!), out _);
        }
        _clients.Add(client);
        return client;
    }

    private async Task<(ClientSession One, ClientSession Two, Welcome Welcome)> Pair(string scenario = "ranged")
    {
        var one = await Connect();
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync(scenario, Token));
        var two = await Connect();
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        var count = _server.Lobby.LogOf(welcome.Code)!.Count;
        await one.WaitForAsync(c => c.Mirror.Events == count, Token);
        await two.WaitForAsync(c => c.Mirror.Events == count, Token);
        return (one, two, welcome);
    }

    private static ProtocolError ProtocolCode(ServerMessage? answer)
    {
        var rejected = Assert.IsType<Rejected>(answer);
        Assert.Equal(RejectionKind.Protocol, rejected.Kind);
        return Enum.Parse<ProtocolError>(rejected.Code);
    }

    private async Task PlayQuietly(ClientSession session, int seed)
    {
        try
        {
            await BotPlayer.PlayAsync(session, seed, Token);
        }
        catch (Exception e) when (e is WebSocketException or InvalidOperationException or System.Threading.Channels.ChannelClosedException or IOException
            || (e is OperationCanceledException && !Token.IsCancellationRequested))
        {
            // The connection was cut under the bot (a send in flight is cancelled): that is the test. A test timeout is not.
        }
    }

    private string PublicKeyFile()
    {
        var path = Path.Combine(_dir.FullName, "server.pub");
        File.WriteAllText(path, _server.SigningKey!.PublicPem);
        return path;
    }

    private (int Exit, string Output) VerifyCli(string recordPath)
    {
        var output = new StringWriter();
        var exit = Verify.Run([recordPath, "--key", PublicKeyFile()], output, new StringWriter(), trustedKeysFolder: Path.Combine(_dir.FullName, "no-trust"));
        return (exit, output.ToString());
    }

    private void AssertNoTokenLogged(params string?[] tokens)
    {
        var all = string.Join("\n", _log);
        foreach (var token in tokens.Where(t => t is not null))
        {
            Assert.DoesNotContain(token!, all, StringComparison.Ordinal);
        }
    }

    // The plan's acceptance test: a client killed in the middle of its own activation comes back as a new process with
    // its saved token, reaches the same hash, and the match finishes; the record replays to the same final hash.
    [Fact]
    public async Task AKilledClientRejoinsAsANewProcessAndTheMatchFinishesWithTheSameHashes()
    {
        var (one, two, welcome) = await Pair("melee");
        var killed = new TaskCompletionSource();
        two.Received += m =>
        {
            if (m is EventPublished { Event: ActivationStarted { Seat: Seat.Two } } && two.Mirror.Events > 40 && !killed.Task.IsCompleted)
            {
                two.Abort(); // the process dies mid-activation
                killed.TrySetResult();
            }
        };
        var playOne = PlayQuietly(one, 3);
        var playTwo = PlayQuietly(two, 4);
        await killed.Task.WaitAsync(Token);
        await playTwo;
        await one.WaitForAsync(c => c.IsPaused, Token);
        Assert.Equal(MatchStatus.Paused, _server.Lobby.StatusOf(welcome.Code));

        // A new process: nothing in memory, only the session file.
        var lookup = Sessions.Find(_server.Endpoint.ToString(), welcome.Code, Seat.Two);
        Assert.Equal(SavedSeatStatus.Found, lookup.Status);
        var reborn = await Connect();
        var back = Assert.IsType<Welcome>(await reborn.RejoinAsync(lookup.Seat!.Code, lookup.Seat.Token, Token));
        Assert.Equal(Seat.Two, back.Seat);
        Assert.Equal(0, back.ReplayFrom);
        await reborn.WaitForAsync(c => c.Synchronized, Token);
        Assert.Null(reborn.Failure);

        await Task.WhenAll(playOne, PlayQuietly(reborn, 4));
        await one.WaitForAsync(c => c.Mirror.State.Phase == Phase.Ended, Token);
        await reborn.WaitForAsync(c => c.Mirror.State.Phase == Phase.Ended, Token);

        var log = _server.Lobby.LogOf(welcome.Code)!;
        Assert.IsType<MatchEnded>(log[^1].Event);
        Assert.Equal(log.Select(e => e.Hash), one.Hashes);
        Assert.Equal(log.Select(e => e.Hash), reborn.Hashes);
        Assert.Null(one.Aborted);

        Assert.True(await _server.Records!.DrainAsync(TimeSpan.FromSeconds(30)));
        var (exit, output) = VerifyCli(_server.Records.Store.RecordPath(welcome.MatchId));
        Assert.Equal(Verify.Completed, exit);
        Assert.Contains($"Final hash {log[^1].Hash}: hash matches.", output, StringComparison.Ordinal);
        Assert.Contains("does not prove the dice were fair", output, StringComparison.Ordinal);
        AssertNoTokenLogged(one.SeatToken, reborn.SeatToken, lookup.Seat.Token);
    }

    // A short outage: the client keeps its copy, gets exactly the events it missed, and a resend of its last command is
    // answered from the cache.
    [Fact]
    public async Task ANetworkDropKeepsTheCopyAndResendsAreAnsweredFromTheCache()
    {
        var (one, two, welcome) = await Pair();
        var playing = new[] { PlayQuietly(one, 1), PlayQuietly(two, 2) };
        await one.WaitForAsync(c => c.Mirror.Events > 30, Token);
        var last = one.LastSubmit;
        one.Abort();
        await playing[0];
        await two.WaitForAsync(c => c.IsPaused, Token);
        var known = one.Mirror.Events;
        var server = _server.Lobby.LogOf(welcome.Code)!.Count;

        var again = await Connect(one.Mirror);
        var received = new System.Collections.Concurrent.ConcurrentBag<int>();
        again.Received += m => { if (m is EventPublished published) received.Add(published.Seq); };
        var back = Assert.IsType<Welcome>(await again.RejoinAsync(welcome.Code, one.SeatToken!, Token));
        // Synchronized is set just before Received fires for the same event, so wait for the handler to see it too.
        await again.WaitForAsync(c => c.Synchronized && (back.Events == known || received.Contains(back.Events)), Token);
        Assert.Equal(known, back.ReplayFrom);
        Assert.True(back.Events >= server);
        // The opponent resumes as soon as the seat is back, so live events can follow the backlog; count the backlog only.
        Assert.Equal(Enumerable.Range(known + 1, back.Events - known), received.Where(s => s <= back.Events).Order());

        if (last is not null && back.NextSeq > last.Seq)
        {
            // The opponent is playing again, so the log can grow meanwhile; the cached answer must point only at events
            // that were already there. (ReconnectTests checks exactly-once without a live opponent.)
            var before = _server.Lobby.LogOf(welcome.Code)!.Count;
            var answer = await again.ResendAsync(last, Token);
            Assert.True(answer is Accepted { Duplicate: true } or Rejected { Duplicate: true });
            if (answer is Accepted accepted)
            {
                Assert.True(accepted.LastEvent <= before);
            }
        }
        await two.WaitForAsync(c => !c.IsPaused, Token);
        // Seat Two's bot waited through the pause and carries on.
        await Task.WhenAll(PlayQuietly(again, 1), playing[1]);
        Assert.Equal(_server.Lobby.LogOf(welcome.Code)![^1].Hash, again.Mirror.Hash);
    }

    // A command sent and then the connection dropped at once: whichever side of the drop it landed on, it is applied
    // exactly once.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ACommandSentRightBeforeADropIsAppliedExactlyOnce(int seed)
    {
        var (one, two, welcome) = await Pair();
        var bot = new Bot(new Random(seed));
        var command = bot.Propose(one.Mirror.State).First(c => CommandSeats.Of(c) == Seat.One);
        var submit = new SubmitCommand("drop-1", one.Seq + 1, one.Mirror.Events, command);
        await one.SendAsync(submit, Token);
        if (seed % 2 == 0)
        {
            await Task.Delay(50, Token);
        }
        one.Abort();
        await two.WaitForAsync(c => c.IsPaused, Token);
        var afterDrop = _server.Lobby.LogOf(welcome.Code)!.Count;

        var again = await Connect();
        var back = Assert.IsType<Welcome>(await again.RejoinAsync(welcome.Code, one.SeatToken!, Token));
        await again.WaitForAsync(c => c.Synchronized, Token);
        var processed = back.NextSeq > submit.Seq;
        var answer = await again.ResendAsync(submit, Token);
        var final = _server.Lobby.LogOf(welcome.Code)!.Count;
        if (processed)
        {
            // It ran before the drop: the resend is the cached answer and nothing runs again.
            Assert.Equal(submit.Seq + 1, back.NextSeq);
            Assert.Equal(AsDuplicate(back.LastAnswer!), answer);
            Assert.Equal(afterDrop, final);
        }
        else
        {
            // It was cut off: the resend is its first and only run.
            Assert.Equal(submit.Seq, back.NextSeq);
            Assert.False(answer is Accepted { Duplicate: true } or Rejected { Duplicate: true });
            Assert.Equal(afterDrop + (answer is Accepted a ? a.LastEvent - a.FirstEvent + 1 : 0), final);
        }
        Assert.Equal(submit.Seq, again.Seq);
        // A further resend is always the cached answer now.
        var third = await again.ResendAsync(submit, Token);
        Assert.True(third is Accepted { Duplicate: true } or Rejected { Duplicate: true });
        Assert.Equal(final, _server.Lobby.LogOf(welcome.Code)!.Count);
    }

    private static ServerMessage AsDuplicate(ServerMessage answer) => answer switch
    {
        Accepted a => a with { Duplicate = true },
        Rejected r => r with { Duplicate = true },
        _ => answer,
    };

    [Fact]
    public async Task ATakeoverClosesTheOldSocketAndTheNewOnePlaysOn()
    {
        var (one, two, welcome) = await Pair();
        var second = await Connect(saveTokens: false);
        Assert.IsType<Welcome>(await second.RejoinAsync(welcome.Code, one.SeatToken!, Token));
        var taken = await one.NextAnswerAsync(Token);
        Assert.Equal(ProtocolError.SeatTakenOver, ProtocolCode(taken));
        await one.Completion.WaitAsync(Token);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, one.CloseStatus);
        Assert.Equal(MatchStatus.Playing, _server.Lobby.StatusOf(welcome.Code));
        await second.WaitForAsync(c => c.Synchronized, Token);
        await Task.WhenAll(PlayQuietly(second, 1), PlayQuietly(two, 2));
        Assert.Equal(Phase.Ended, second.Mirror.State.Phase);
    }

    [Fact]
    public async Task SeatsStayWithTheirTokens()
    {
        var (one, two, welcome) = await Pair();
        var third = await Connect(saveTokens: false);
        Assert.Equal(ProtocolError.MatchFull, ProtocolCode(await third.JoinAsync(welcome.Code, Token)));
        var forged = RecordFormat.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(await third.RejoinAsync(welcome.Code, forged, Token)));

        // Rotate and confirm Seat Two's token; the first one is then stale.
        var first = two.SeatToken!;
        two.Abort();
        await one.WaitForAsync(c => c.IsPaused, Token);
        var back = await Connect();
        var rejoined = Assert.IsType<Welcome>(await back.RejoinAsync(welcome.Code, first, Token));
        Assert.NotEqual(first, rejoined.SeatToken);
        await back.WaitForAsync(c => c.Synchronized, Token);
        // The TokenSaved went out (the session file was saved); a round trip makes sure it arrived.
        Assert.IsType<Rejected>(await back.SubmitAsync(new ActivateModel(Seat.Two, new ModelId("zz9")), Token));
        var stale = await Connect(saveTokens: false);
        Assert.Equal(ProtocolError.InvalidToken, ProtocolCode(await stale.RejoinAsync(welcome.Code, first, Token)));
        Assert.Equal(SavedSeatStatus.Found, Sessions.Find(_server.Endpoint.ToString(), welcome.Code, Seat.Two).Status);
        Assert.Equal(SavedSeatStatus.Ambiguous, Sessions.Find(_server.Endpoint.ToString(), welcome.Code).Status);
        AssertNoTokenLogged(first, rejoined.SeatToken);
    }

    [Fact]
    public async Task AnExpiredRejoinAbortsAndTheRecordSaysAbortedNotCompleted()
    {
        var (one, two, welcome) = await Pair();
        await one.SubmitAsync(new ActivateModel(Seat.One, new ModelId("zz9")), Token);
        two.Abort();
        await one.WaitForAsync(c => c.IsPaused, Token);
        Time.Advance(TimeSpan.FromMinutes(5));
        await one.WaitForAsync(c => c.Aborted is not null, Token);
        Assert.Equal(AbortReason.RejoinExpired, one.Aborted!.Reason);
        Assert.Equal(Seat.Two, one.Aborted.Seat);

        Assert.True(await _server.Records!.DrainAsync(TimeSpan.FromSeconds(30)));
        var (exit, output) = VerifyCli(_server.Records.Store.RecordPath(welcome.MatchId));
        Assert.Equal(Verify.Aborted, exit);
        Assert.Contains("not a completed match", output, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_server.Records.Store.OpenDirectory));
    }

    [Fact]
    public async Task ShutdownWhilePausedWritesAnAbortedRecordBeforeStopping()
    {
        var (one, two, welcome) = await Pair();
        two.Abort();
        await one.WaitForAsync(c => c.IsPaused, Token);
        var store = _server.Records!.Store;
        var key = _server.SigningKey!.Trusted;
        await _server.StopAsync();
        await one.WaitForAsync(c => c.Aborted is not null || c.IsClosed, Token);
        Assert.Equal(AbortReason.ServerShutdown, one.Aborted?.Reason);
        var verdict = RecordVerifier.VerifyFile(store.RecordPath(welcome.MatchId), [key]);
        Assert.Equal(VerdictKind.Aborted, verdict.Kind);
        Assert.Equal(nameof(AbortReason.ServerShutdown), verdict.Body!.Result.Reason);
        Assert.Empty(Directory.EnumerateFiles(store.OpenDirectory));
    }

    // The backlog after a rejoin is the whole match; it is exempt from the live queue limit.
    [Fact]
    public async Task AWholeMatchBacklogIsDeliveredEvenWithASmallQueueLimit()
    {
        await _server.DisposeAsync();
        _server = await Start(seed: 3, queueLimit: 64);
        var (one, two, welcome) = await Pair("melee");
        await Task.WhenAll(PlayQuietly(one, 3), PlayQuietly(two, 4));
        var log = _server.Lobby.LogOf(welcome.Code)!;
        Assert.True(log.Count > 64, $"{log.Count} events");
        two.Abort();

        var reader = await Connect(saveTokens: false);
        var back = Assert.IsType<Welcome>(await reader.RejoinAsync(welcome.Code, two.SeatToken!, Token));
        Assert.Equal(MatchStatus.Finished, back.Status);
        Assert.Null(back.SeatToken);
        await reader.WaitForAsync(c => c.Synchronized, Token);
        Assert.Equal(log.Select(e => e.Hash), reader.Hashes);
        Assert.Equal(ProtocolError.MatchFinished, ProtocolCode(await reader.SubmitAsync(new EndActivation(Seat.Two), Token)));
    }

    // M5's stalled-reader case with pausing on: the stalled seat is dropped and the match pauses (other matches carry
    // on), then expires.
    [Fact]
    public async Task AStalledReaderPausesItsMatchOnlyAndThenExpires()
    {
        await _server.DisposeAsync();
        _server = await Start(sendTimeout: TimeSpan.FromSeconds(1), queueLimit: 1_000_000, keepAlive: TimeSpan.Zero);
        var (other1, other2, otherWelcome) = await Pair("melee");

        var one = await Connect();
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync("ranged", Token));
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
        await stalled.SendAsync(Codec.Encode(new JoinMatch(welcome.Code)), WebSocketMessageType.Text, true, Token);
        await one.WaitForAsync(c => c.Notices.Any(n => n.Kind == NoticeKind.OpponentJoined), Token);

        var junk = Encoding.UTF8.GetBytes("x");
        while (_server.Lobby.StatusOf(welcome.Code) != MatchStatus.Paused)
        {
            for (var i = 0; i < 16; i++)
            {
                try
                {
                    await stalled.SendAsync(junk, WebSocketMessageType.Text, true, Token);
                }
                catch (WebSocketException)
                {
                    break;
                }
            }
            await Task.Delay(1, Token);
        }
        await one.WaitForAsync(c => c.IsPaused, Token);
        Assert.Equal(MatchStatus.Playing, _server.Lobby.StatusOf(otherWelcome.Code));

        Time.Advance(TimeSpan.FromMinutes(5));
        await one.WaitForAsync(c => c.Aborted is not null, Token);
        Assert.Equal(AbortReason.RejoinExpired, one.Aborted!.Reason);
        Assert.Equal(MatchStatus.Playing, _server.Lobby.StatusOf(otherWelcome.Code));
        await Task.WhenAll(PlayQuietly(other1, 1), PlayQuietly(other2, 2));
        Assert.Equal(Phase.Ended, other1.Mirror.State.Phase);
    }

    // The real server binary, killed without warning in the middle of a match, then started again on the same data.
    [Fact]
    public async Task AServerKilledMidMatchRecordsItAsUnrecordedAtTheNextStart()
    {
        await _server.DisposeAsync();
        var data = Path.Combine(_dir.FullName, "process");
        using (var process = ServerProcess.Start(data))
        {
            var endpoint = await process.EndpointAsync(Token);
            var one = await ClientSession.ConnectAsync(endpoint, Token);
            _clients.Add(one);
            var welcome = Assert.IsType<Welcome>(await one.CreateAsync("ranged", Token));
            var two = await ClientSession.ConnectAsync(endpoint, Token);
            _clients.Add(two);
            Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
            await one.WaitForAsync(c => c.Mirror.Events > 1, Token);
            var marker = Path.Combine(data, "matches", "open", welcome.MatchId + ".json");
            await WaitUntil(() => File.Exists(marker));

            process.Kill();
            await one.Completion.WaitAsync(Token);

            using var restarted = ServerProcess.Start(data);
            var newEndpoint = await restarted.EndpointAsync(Token);
            Assert.Contains("1 match(es) recorded as Unrecorded", restarted.Output, StringComparison.Ordinal);
            var key = RecordKeys.FromPem(File.ReadAllText(Path.Combine(data, "keys", "signing.pub")))!;
            var verdict = RecordVerifier.VerifyFile(Path.Combine(data, "matches", welcome.MatchId + ".json"), [key]);
            Assert.Equal(VerdictKind.Unrecorded, verdict.Kind);
            Assert.False(verdict.IsCompletedMatch);
            Assert.False(File.Exists(marker));

            // The match is gone: an old token finds nothing, and nothing is resumed.
            var late = await ClientSession.ConnectAsync(newEndpoint, Token);
            _clients.Add(late);
            Assert.Equal(ProtocolError.UnknownMatch, ProtocolCode(await late.RejoinAsync(welcome.Code, two.SeatToken!, Token)));
            Assert.DoesNotContain(two.SeatToken!, restarted.Output, StringComparison.Ordinal);
            restarted.Kill();
        }
    }

    [Fact]
    public async Task AServerStoppedWithSigtermRecordsTheMatchAsAborted()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        await _server.DisposeAsync();
        var data = Path.Combine(_dir.FullName, "process");
        using var process = ServerProcess.Start(data);
        var endpoint = await process.EndpointAsync(Token);
        var one = await ClientSession.ConnectAsync(endpoint, Token);
        _clients.Add(one);
        var welcome = Assert.IsType<Welcome>(await one.CreateAsync("ranged", Token));
        var two = await ClientSession.ConnectAsync(endpoint, Token);
        _clients.Add(two);
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, Token));
        await one.WaitForAsync(c => c.Mirror.Events > 1, Token);

        process.Terminate();
        await one.WaitForAsync(c => c.Aborted is not null || c.IsClosed, Token);
        Assert.Equal(AbortReason.ServerShutdown, one.Aborted?.Reason);
        await process.ExitAsync(Token);
        var key = RecordKeys.FromPem(File.ReadAllText(Path.Combine(data, "keys", "signing.pub")))!;
        var verdict = RecordVerifier.VerifyFile(Path.Combine(data, "matches", welcome.MatchId + ".json"), [key]);
        Assert.Equal(VerdictKind.Aborted, verdict.Kind);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(data, "matches", "open")));
    }

    // The real server binary keeps one identity across restarts, and refuses to start, without touching the key, when the
    // key can be read by others. The refusal names the path and mode and prints nothing of the key.
    [Fact]
    public async Task TheRealServerKeepsItsKeyAndRefusesAKeyOthersCanRead()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        await _server.DisposeAsync();
        var data = Path.Combine(_dir.FullName, "process");
        string firstLine;
        using (var first = ServerProcess.Start(data))
        {
            await first.EndpointAsync(Token);
            firstLine = first.Output.Split('\n').Single(l => l.StartsWith("Records in ", StringComparison.Ordinal)).Trim();
            Assert.EndsWith("(new).", firstLine, StringComparison.Ordinal);
            first.Kill();
        }
        var keyPath = Path.Combine(data, "keys", "signing.key");
        var privatePem = File.ReadAllText(keyPath);
        using (var second = ServerProcess.Start(data))
        {
            await second.EndpointAsync(Token);
            Assert.Contains(firstLine.Replace(" (new).", ".", StringComparison.Ordinal), second.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("(new)", second.Output, StringComparison.Ordinal);
            second.Kill();
        }

        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        using (var refused = ServerProcess.Start(data))
        {
            await refused.ExitAsync(Token);
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains($"Cannot start: {keyPath} has mode 0644", refused.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("Listening on", refused.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE KEY", refused.Output, StringComparison.Ordinal);
            foreach (var line in privatePem.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith("-----", StringComparison.Ordinal)))
            {
                Assert.DoesNotContain(line, refused.Output, StringComparison.Ordinal);
            }
        }
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(keyPath));
        Assert.Equal(privatePem, File.ReadAllText(keyPath));
    }

    private async Task WaitUntil(Func<bool> condition)
    {
        while (!condition())
        {
            await Task.Delay(10, Token);
        }
    }
}

// The real trench-server program, run from the test's output folder.
internal sealed class ServerProcess : IDisposable
{
    private readonly System.Diagnostics.Process _process;
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource<Uri> _endpoint = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ServerProcess(System.Diagnostics.Process process)
    {
        _process = process;
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public static ServerProcess Start(string data)
    {
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Match.Server.dll"), "--port", "0", "--data", data })
        {
            info.ArgumentList.Add(arg);
        }
        return new ServerProcess(System.Diagnostics.Process.Start(info)!);
    }

    public Task<Uri> EndpointAsync(CancellationToken cancellationToken) => _endpoint.Task.WaitAsync(cancellationToken);

    public void Kill()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }
    }

    public void Terminate()
    {
        using var kill = System.Diagnostics.Process.Start("kill", ["-TERM", _process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        kill.WaitForExit();
    }

    public Task ExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    public int ExitCode => _process.ExitCode;

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }

    private void OnLine(string? line)
    {
        if (line is null)
        {
            return;
        }
        lock (_output)
        {
            _output.AppendLine(line);
        }
        const string prefix = "Listening on ";
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            _endpoint.TrySetResult(new Uri(line[prefix.Length..].Split(' ')[0]));
        }
    }
}
