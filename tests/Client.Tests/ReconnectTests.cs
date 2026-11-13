using System.Diagnostics;
using System.Net.WebSockets;
using Match.Client;
using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// M7.2 follow-up: one rejoin at a time per seat, every attempt bounded by the deadline, nothing left open after a
// timeout or cancellation, "Rejoin now" after automatic attempts stop, and what a lost takeover notice can and cannot do.
// Time limits here are short and bounded; nothing waits on a long real-time sleep.
public sealed partial class TransportTests
{
    private static readonly string AnyToken = RecordFormat.Base64Url(new byte[32]);

    private static Rejected Refusal(ProtocolError error) =>
        new(null, null, RejectionKind.Protocol, error.ToString(), "test", null, 0, 1, false);

    private async Task<FakeServer> Fake(Func<int, FakeReply> script)
    {
        var fake = new FakeServer(script);
        _fakes.Add(fake);
        await Task.Yield();
        return fake;
    }

    private readonly List<FakeServer> _fakes = [];

    private async Task Dropped(ClientSession session)
    {
        session.Abort();
        await session.Completion.WaitAsync(Token);
    }

    // The automatic loop, a second callback and "Rejoin now" on the same dropped seat share one run: one connection,
    // one session, and no session that reports success after another took the seat from it. The dropped session is
    // then superseded and cannot start another rejoin.
    [Fact]
    public async Task ConcurrentReconnectsOfOneSeatShareOneRunAndOneSession()
    {
        var (one, two, code) = await Pair();
        await Dropped(two);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect = async (mirror, ct) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task.WaitAsync(ct);
            return await Connect(mirror: mirror, cancellationToken: ct);
        };

        var automatic = Reconnector.RunAsync(two, connect, policy: Quick, cancellationToken: Token);
        var callback = Reconnector.RunAsync(two, connect, policy: Quick, cancellationToken: Token);
        var manual = Reconnector.RejoinNowAsync(two, connect, policy: Quick, cancellationToken: Token);
        Assert.True(Reconnector.IsReconnecting(two.Mirror));
        gate.SetResult();
        var results = await Task.WhenAll(automatic, callback, manual);

        Assert.Equal(1, calls);
        var back = Assert.IsType<ClientSession>(results[0].Session);
        Assert.All(results, r =>
        {
            Assert.Equal(ReconnectOutcome.Rejoined, r.Outcome);
            Assert.Same(back, r.Session);
        });
        Assert.False(Reconnector.IsReconnecting(two.Mirror));

        // The one session really holds the seat: play goes on and nobody was taken over.
        await one.WaitForAsync(c => !c.IsPaused, Token);
        await Play(_server, code, one, back, 10);
        await Settle(_server, code, one, back);
        AssertSameAsServer(_server, code, one, back);
        Assert.False(back.TakenOver);
        Assert.False(back.IsClosed);

        // A late callback on the superseded session cannot start a second rejoin, by either path.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(two, connect, policy: Quick, cancellationToken: Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RejoinNowAsync(two, connect, policy: Quick, cancellationToken: Token));
        Assert.Equal(1, calls);

        // When the newer session drops in turn, it (and only it) can reconnect.
        await Dropped(back);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(two, connect, policy: Quick, cancellationToken: Token));
        var again = await Reconnector.RunAsync(back, connect, policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, again.Outcome);
        Assert.Equal(2, calls);
    }

    // "Rejoin now" while the automatic loop waits out a long delay makes the attempt at once, in that same run.
    [Fact]
    public async Task RejoinNowDuringABackoffTriesAtOnceWithoutASecondRun()
    {
        var (_, two, _) = await Pair();
        await Dropped(two);
        var policy = new ReconnectPolicy { Delays = [TimeSpan.FromHours(1)], Repeat = TimeSpan.FromHours(1), GiveUpAfter = TimeSpan.FromHours(2) };
        var calls = 0;
        var firstFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect = (mirror, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstFailed.SetResult();
                throw new WebSocketException("unreachable");
            }
            return Connect(mirror: mirror, cancellationToken: ct);
        };

        var automatic = Reconnector.RunAsync(two, connect, policy: policy, cancellationToken: Token);
        await firstFailed.Task.WaitAsync(Token);
        var manual = Reconnector.RejoinNowAsync(two, connect, policy: policy, cancellationToken: Token);
        var results = await Task.WhenAll(automatic, manual);
        Assert.Equal(2, calls);
        Assert.All(results, r =>
        {
            Assert.Equal(ReconnectOutcome.Rejoined, r.Outcome);
            Assert.Equal(2, r.Attempts);
        });
        Assert.Same(results[0].Session, results[1].Session);
    }

    // The automatic limit is a setting (five minutes by default, the server's default --rejoin-window). After it runs
    // out the server may still hold the seat, so "Rejoin now" makes a new attempt.
    [Fact]
    public async Task AfterAutomaticAttemptsStopRejoinNowStillTakesTheSeatBack()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), new ReconnectPolicy().GiveUpAfter);
        Assert.Equal(ReconnectPolicy.DefaultGiveUpAfter, new MatchServerOptions().RejoinWindow);
        Assert.Equal(TimeSpan.FromMinutes(30), ReconnectPolicy.ForRejoinWindow(TimeSpan.FromMinutes(30)).GiveUpAfter);
        await Assert.ThrowsAsync<ArgumentException>(() => Reconnector.RunAsync("CODE1", AnyToken, new MatchMirror(), (_, _) => throw new WebSocketException(), policy: Quick with { GiveUpAfter = TimeSpan.Zero }));

        var server = await Start(TimeSpan.FromHours(1));
        var (one, two, code) = await Pair(server: server);
        await Dropped(two);
        var unreachable = await Reconnector.RunAsync(two, (_, _) => throw new WebSocketException("unreachable"),
            policy: Quick with { GiveUpAfter = TimeSpan.FromMilliseconds(200) }, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.GaveUp, unreachable.Outcome);
        Assert.False(Reconnector.IsReconnecting(two.Mirror));
        Assert.Equal(MatchStatus.Paused, server.Lobby.StatusOf(code));

        var manual = await Reconnector.RejoinNowAsync(two, Reconnect(server), policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, manual.Outcome);
        Assert.Equal(1, manual.Attempts);
        await one.WaitForAsync(c => !c.IsPaused, Token);
        await Settle(server, code, one, manual.Session!);
        AssertSameAsServer(server, code, one, manual.Session!);

        // A manual attempt that fails is one attempt, not a new retry loop.
        await Dropped(manual.Session!);
        var failed = await Reconnector.RejoinNowAsync(manual.Session!, (_, _) => throw new WebSocketException("unreachable"), policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.GaveUp, failed.Outcome);
        Assert.Equal(1, failed.Attempts);
    }

    // A host that accepts the TCP connection but never answers the WebSocket handshake cannot hold the run open past
    // its deadline, even with a long per-attempt limit; and every connection it accepted is closed afterwards.
    [Fact]
    public async Task AHostThatNeverAnswersTheHandshakeCannotHoldTheRunPastItsDeadline()
    {
        var fake = await Fake(_ => FakeReply.NoHandshake);
        var policy = Quick with { GiveUpAfter = TimeSpan.FromMilliseconds(600), AttemptTimeout = TimeSpan.FromHours(1) };
        var clock = Stopwatch.StartNew();
        var result = await Reconnector.RunAsync("CODE1", AnyToken, new MatchMirror(), fake.Connect, policy: policy, cancellationToken: Token);
        clock.Stop();
        Assert.Equal(ReconnectOutcome.GaveUp, result.Outcome);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"The run took {clock.Elapsed}.");
        Assert.True(fake.Accepted >= 1);
        await fake.AllClosed().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    // A host that completes the handshake but never answers the rejoin: each attempt stops at AttemptTimeout, the run
    // at GiveUpAfter, and each abandoned connection is closed.
    [Fact]
    public async Task AHostThatNeverAnswersTheRejoinTimesOutEachAttempt()
    {
        var fake = await Fake(_ => FakeReply.Silent);
        // 3 s leaves room for a second 200 ms attempt even when connecting is slow (run 348, a busy machine: with 1 s the
        // first attempt's connect took most of the run and there was only 1 attempt).
        var policy = Quick with { GiveUpAfter = TimeSpan.FromSeconds(3), AttemptTimeout = TimeSpan.FromMilliseconds(200) };
        var clock = Stopwatch.StartNew();
        var result = await Reconnector.RunAsync("CODE1", AnyToken, new MatchMirror(), fake.Connect, policy: policy, cancellationToken: Token);
        clock.Stop();
        Assert.Equal(ReconnectOutcome.GaveUp, result.Outcome);
        Assert.True(result.Attempts >= 2, $"{result.Attempts} attempts.");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"The run took {clock.Elapsed}.");
        Assert.Equal(result.Attempts, fake.Accepted);
        await fake.AllClosed().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    // Cancelling returns promptly, during the handshake or while waiting for the answer, and leaves no run and no
    // connection behind.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingARunReturnsPromptlyAndLeavesNothingOpen(bool handshake)
    {
        var fake = await Fake(_ => handshake ? FakeReply.Silent : FakeReply.NoHandshake);
        var opened = new TaskCompletionSource<ClientSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect = async (mirror, ct) =>
        {
            var session = await fake.Connect(mirror, ct);
            opened.TrySetResult(session);
            return session;
        };
        var mirror = new MatchMirror();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = Reconnector.RunAsync("CODE1", AnyToken, mirror, connect, policy: Quick with { AttemptTimeout = TimeSpan.FromHours(1) }, cancellationToken: cancel.Token);
        await fake.FirstAccepted.WaitAsync(Token);
        if (handshake)
        {
            await opened.Task.WaitAsync(Token);
        }

        var clock = Stopwatch.StartNew();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        Assert.False(Reconnector.IsReconnecting(mirror));
        if (handshake)
        {
            Assert.True((await opened.Task).IsClosed);
        }
        await fake.AllClosed().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    // A busy or shutting-down server is a reason to try again later, not to stop.
    [Fact]
    public async Task ABusyOrShuttingDownServerIsTriedAgain()
    {
        var (_, two, _) = await Pair();
        await Dropped(two);
        var fake = await Fake(i => new FakeReply(true, Refusal(i == 0 ? ProtocolError.ServerBusy : ProtocolError.ShuttingDown)));
        var calls = 0;
        var result = await Reconnector.RunAsync(two, (mirror, ct) =>
            Interlocked.Increment(ref calls) <= 2 ? fake.Connect(mirror, ct) : Connect(mirror: mirror, cancellationToken: ct),
            policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(2, fake.Accepted);
        await fake.AllClosed().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    // A connection the server closed on policy grounds (here: too many bad seat tokens) is not reconnected on its own.
    [Fact]
    public async Task AConnectionClosedOnPolicyGroundsIsNotReconnected()
    {
        var (_, _, code) = await Pair();
        var guesser = await Connect();
        for (var i = 0; i < Lobby.MaxTokenFailures; i++)
        {
            await guesser.SendAsync(new Rejoin(code, AnyToken, 0, null), Token);
        }
        await guesser.Completion.WaitAsync(Token);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, guesser.CloseStatus);
        Assert.Equal(ReconnectDecision.Refused, Reconnector.Decide(guesser));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(guesser, Reconnect(), policy: Quick, cancellationToken: Token));
    }

    // A copy that failed its check is never reconnected automatically; "Rejoin now" rebuilds it from an empty copy.
    [Fact]
    public async Task AFailedCopyIsRejoinedOnlyByHandFromAnEmptyCopy()
    {
        var (_, two, code) = await Pair();
        var first = _server.Lobby.LogOf(code)![0];
        var welcome = new Welcome(code, two.Welcome!.MatchId, Seat.Two, "melee", AnyToken, 0, 2, first.Hash, 1, null, MatchStatus.Playing);
        // The fake skips event 1, so the copy cannot follow.
        var fake = await Fake(_ => new FakeReply(true, welcome, first with { Seq = 2 }));
        var broken = await fake.Connect(new MatchMirror(), Token);
        _clients.Add(broken);
        Assert.IsType<Welcome>(await broken.RejoinAsync(code, AnyToken, Token));
        await broken.Completion.WaitAsync(Token);
        Assert.NotNull(broken.Failure);
        Assert.Equal(ReconnectDecision.CopyFailed, Reconnector.Decide(broken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(broken, Reconnect(), policy: Quick, cancellationToken: Token));

        MatchMirror? offered = null;
        var manual = await Reconnector.RejoinNowAsync(broken, (mirror, _) =>
        {
            offered = mirror;
            throw new WebSocketException("unreachable");
        }, policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.GaveUp, manual.Outcome);
        Assert.NotNull(offered);
        Assert.NotSame(broken.Mirror, offered);
        Assert.Equal(0, offered!.Events);
        await fake.AllClosed().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    // The older window drops before the SeatTakenOver notice reaches it, so it believes it merely dropped. Once the
    // newer window has saved and confirmed its token, the old token is dead: the older window's automatic rejoin is
    // refused (InvalidToken) and stops, and the newer window keeps the seat.
    [Fact]
    public async Task ATakeoverTheOlderWindowNeverHeardAboutDoesNotGiveItTheSeatBack()
    {
        var (_, older, code) = await Pair();
        await Dropped(older);
        var newer = await Connect();
        Assert.IsType<Welcome>(await newer.RejoinAsync(code, older.SeatToken!, Token));
        // An answer on newer's connection comes after the server handled its TokenSaved.
        Assert.Equal(ProtocolError.SeatMismatch, ProtocolCode(await newer.SubmitAsync(new EndActivation(Seat.One), Token)));

        Assert.Equal(ReconnectDecision.Reconnect, Reconnector.Decide(older));
        var result = await Reconnector.RunAsync(older, Reconnect(), policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Refused, result.Outcome);
        Assert.Equal(ProtocolError.InvalidToken, result.Code);
        Assert.Equal(1, result.Attempts);
        Assert.False(newer.TakenOver);
        Assert.False(newer.IsClosed);
    }

    // The known limitation, pinned: if the newer window's token is not yet confirmed (its save failed, or the older
    // window's rejoin arrives first), the old token still opens the seat and the older window takes it back. The newer
    // window is then told it was taken over and does not take it back on its own, so the two never fight. Fixing this
    // needs the server to tell a rejoining client about the takeover, which is a protocol change.
    [Fact]
    public async Task AnUnconfirmedTakeoverCanBeUndoneByTheOlderWindowButTheyNeverFight()
    {
        var (_, older, code) = await Pair();
        await Dropped(older);
        var newer = await Connect();
        newer.SaveToken = _ => false;
        Assert.IsType<Welcome>(await newer.RejoinAsync(code, older.SeatToken!, Token));

        var result = await Reconnector.RunAsync(older, Reconnect(), policy: Quick, cancellationToken: Token);
        Assert.Equal(ReconnectOutcome.Rejoined, result.Outcome);
        await newer.WaitForAsync(c => c.TakenOver, Token);
        await newer.Completion.WaitAsync(Token);
        Assert.Equal(ReconnectDecision.TakenOver, Reconnector.Decide(newer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reconnector.RunAsync(newer, Reconnect(), policy: Quick, cancellationToken: Token));
        Assert.False(result.Session!.TakenOver);
    }
}
