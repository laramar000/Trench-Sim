using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Match.Protocol;

namespace Match.Client;

// Why a closed session should or should not be reconnected on its own (M7, D7).
public enum ReconnectDecision
{
    // A transient drop: rejoin with the seat token, keeping the copy of the match.
    Reconnect,

    // The session is still open; nothing to do.
    StillConnected,

    // Another connection took the seat. Taking it back automatically would fight the player's newer window.
    TakenOver,

    // The server refused the seat token or closed the connection on policy grounds.
    Refused,

    // The match ended by the rules; a rejoin would only show the result.
    Finished,

    // The match was aborted; it cannot continue.
    Aborted,

    // The copy of the match failed its hash check. Only a deliberate, manual rejoin (which rebuilds it) is safe.
    CopyFailed,

    // There is no seat to rejoin: the session never received a Welcome with a seat token.
    NoSeat,
}

public enum ReconnectOutcome
{
    Rejoined,

    // The server answered the rejoin with a refusal that retrying cannot fix (see Code).
    Refused,

    // No attempt succeeded before the policy's time ran out (or the one manual attempt failed). The match may still be
    // waiting on the server: offer "Rejoin now" (Reconnector.RejoinNowAsync) rather than treating the match as over.
    GaveUp,
}

// The result of one reconnect run. Session is set when Rejoined; Code names the refusal when Refused.
public sealed record ReconnectResult(ReconnectOutcome Outcome, ClientSession? Session, int Attempts, ProtocolError? Code = null);

// How long to wait between rejoin attempts and when to stop. The defaults wait 1, 2, 4 and 8 seconds, then every 15,
// and stop automatic attempts after five minutes.
public sealed record ReconnectPolicy
{
    // The server's default --rejoin-window. The client cannot learn the window a server was started with: the protocol
    // tells only the opponent when a dropped seat must be back (MatchPaused.RejoinBy). A server started with a longer
    // --rejoin-window needs a matching GiveUpAfter (see ForRejoinWindow), or the client stops trying while the server
    // still holds the seat; "Rejoin now" still works until the server's window ends.
    public static readonly TimeSpan DefaultGiveUpAfter = TimeSpan.FromMinutes(5);

    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    public TimeSpan Repeat { get; init; } = TimeSpan.FromSeconds(15);

    // How long after the drop automatic attempts continue. No attempt runs past it: connecting, the handshake and the
    // wait for the rejoin answer all stop when it is reached.
    public TimeSpan GiveUpAfter { get; init; } = DefaultGiveUpAfter;

    // The longest a single attempt (connect, handshake and rejoin answer) may take, so one host that never answers
    // cannot use up the whole of GiveUpAfter. A manual "Rejoin now" is one attempt of at most this long.
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(20);

    // A policy that keeps trying for as long as a server started with --rejoin-window <window> holds a dropped seat.
    public static ReconnectPolicy ForRejoinWindow(TimeSpan serverRejoinWindow) => new() { GiveUpAfter = serverRejoinWindow };

    public TimeSpan DelayBefore(int attempt) =>
        attempt <= 0 ? TimeSpan.Zero : attempt <= Delays.Count ? Delays[attempt - 1] : Repeat;

    internal void Check()
    {
        if (GiveUpAfter <= TimeSpan.Zero || AttemptTimeout <= TimeSpan.Zero || Repeat < TimeSpan.Zero || Delays.Any(d => d < TimeSpan.Zero))
        {
            throw new ArgumentException("GiveUpAfter and AttemptTimeout must be positive and no delay may be negative.");
        }
    }
}

// Takes a seat back after a transient drop, with the existing M6 rejoin: the seat token, and the copy of the match the
// dropped session held, so the server replays only what is missing. It never retries after a refusal, a takeover or a
// finished or aborted match, and it never invents state: the new session's copy is checked against the server's hash
// exactly as any other session's is.
//
// Only one rejoin runs at a time for a copy of the match. The copy is shared by the dropped session and every session
// that rejoins with it, so an automatic run and "Rejoin now" (or two callbacks) on the same seat join one run and get
// the same result: there is never a second connection that would take the seat from the first. Once a session has
// rejoined, the sessions before it are superseded and cannot start another rejoin.
public static class Reconnector
{
    // Errors after which another attempt cannot succeed.
    private static readonly HashSet<ProtocolError> Final =
    [
        ProtocolError.InvalidToken,
        ProtocolError.UnknownMatch,
        ProtocolError.MatchAborted,
        ProtocolError.SeatMismatch,
        ProtocolError.UnsupportedVersion,
        ProtocolError.Malformed,
        ProtocolError.InvalidValue,
        ProtocolError.UnknownType,
        ProtocolError.TooLarge,
        ProtocolError.AlreadyJoined,
        ProtocolError.SeatTakenOver,
    ];

    // One per copy of the match: the run in progress, and the newest session that rejoined with this copy.
    private sealed class Lineage
    {
        public readonly object Gate = new();
        public Task<ReconnectResult>? Running;
        public ClientSession? Owner;
        public TaskCompletionSource Wake = NewSignal();
    }

    private static readonly ConditionalWeakTable<MatchMirror, Lineage> Lineages = new();

    public static ReconnectDecision Decide(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.TakenOver)
        {
            return ReconnectDecision.TakenOver;
        }
        if (session.Failure is not null)
        {
            return ReconnectDecision.CopyFailed;
        }
        if (session.Aborted is not null || session.Welcome?.Status == MatchStatus.Aborted)
        {
            return ReconnectDecision.Aborted;
        }
        if (session.Mirror.State.Outcome is not null || session.Welcome?.Status == MatchStatus.Finished)
        {
            return ReconnectDecision.Finished;
        }
        if (!session.IsClosed)
        {
            return ReconnectDecision.StillConnected;
        }
        if (session.CloseStatus == WebSocketCloseStatus.PolicyViolation)
        {
            return ReconnectDecision.Refused;
        }
        if (session.Welcome is null || session.SeatToken is null)
        {
            return ReconnectDecision.NoSeat;
        }
        return ReconnectDecision.Reconnect;
    }

    // True while a rejoin runs for this copy of the match.
    public static bool IsReconnecting(MatchMirror mirror)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        if (!Lineages.TryGetValue(mirror, out var lineage))
        {
            return false;
        }
        lock (lineage.Gate)
        {
            return lineage.Running is { IsCompleted: false };
        }
    }

    // Automatic reconnect: rejoins the seat the dropped session held, retrying by the policy. connect opens a new
    // connection with the given copy of the match and should honour its cancellation token; configure is called on each
    // new session before it rejoins (to set SaveToken and handlers). If a run is already in progress for this seat, this
    // returns that run (its connect, configure, policy and cancellation apply) instead of starting another. The dropped
    // session is not disposed here; the caller owns it.
    public static Task<ReconnectResult> RunAsync(
        ClientSession dropped,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure = null,
        ReconnectPolicy? policy = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        ArgumentNullException.ThrowIfNull(connect);
        var decision = Decide(dropped);
        if (decision != ReconnectDecision.Reconnect)
        {
            throw new InvalidOperationException($"This session should not be reconnected ({decision}).");
        }
        return Start(dropped, dropped.Welcome!.Code, dropped.SeatToken!, dropped.Mirror, dropped.Mirror, connect, configure, policy, time, automatic: true, cancellationToken);
    }

    // Automatic reconnect by code and token, starting from the given copy of the match (an empty copy replays
    // everything). The copy must not belong to a session that is still open.
    public static Task<ReconnectResult> RunAsync(
        string code,
        string seatToken,
        MatchMirror mirror,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure = null,
        ReconnectPolicy? policy = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(seatToken);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(connect);
        return Start(null, code, seatToken, mirror, mirror, connect, configure, policy, time, automatic: true, cancellationToken);
    }

    // "Rejoin now": the player asks for an attempt, for example after automatic attempts gave up. If a run is already in
    // progress for this seat, it tries at once instead of waiting out its delay, and this returns that run; otherwise it
    // makes one attempt of at most policy.AttemptTimeout. Unlike the automatic path it also rejoins after a takeover or a
    // policy close (the player chose to take the seat back) and after a failed copy, which it rebuilds from an empty copy.
    // It never rejoins a finished or aborted match, a session that never had a seat, or one a newer session superseded.
    public static Task<ReconnectResult> RejoinNowAsync(
        ClientSession dropped,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure = null,
        ReconnectPolicy? policy = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        ArgumentNullException.ThrowIfNull(connect);
        var decision = Decide(dropped);
        if (decision is not (ReconnectDecision.Reconnect or ReconnectDecision.TakenOver or ReconnectDecision.Refused or ReconnectDecision.CopyFailed)
            || dropped.Welcome is null || dropped.SeatToken is null || !dropped.IsClosed)
        {
            throw new InvalidOperationException($"This session cannot be rejoined ({decision}).");
        }
        var copy = decision == ReconnectDecision.CopyFailed ? new MatchMirror() : dropped.Mirror;
        return Start(dropped, dropped.Welcome.Code, dropped.SeatToken, dropped.Mirror, copy, connect, configure, policy, time, automatic: false, cancellationToken);
    }

    private static Task<ReconnectResult> Start(
        ClientSession? dropped,
        string code,
        string seatToken,
        MatchMirror key,
        MatchMirror copy,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure,
        ReconnectPolicy? policy,
        TimeProvider? time,
        bool automatic,
        CancellationToken cancellationToken)
    {
        policy ??= new ReconnectPolicy();
        policy.Check();
        time ??= TimeProvider.System;
        var lineage = Lineages.GetValue(key, _ => new Lineage());
        lock (lineage.Gate)
        {
            if (lineage.Running is { IsCompleted: false } running)
            {
                if (!automatic)
                {
                    lineage.Wake.TrySetResult();
                }
                return running.WaitAsync(cancellationToken);
            }
            if (lineage.Owner is { } owner && !ReferenceEquals(owner, dropped))
            {
                if (!owner.IsClosed)
                {
                    throw new InvalidOperationException("A newer session holds this seat with this copy of the match.");
                }
                if (dropped is not null)
                {
                    throw new InvalidOperationException("A newer session rejoined after this one; reconnect from that session.");
                }
            }
            lineage.Wake = NewSignal();
            // Run off this thread: the first attempt starts at once and must not call connect while holding the lock.
            var run = Task.Run(() => RunCoreAsync(lineage, code, seatToken, copy, connect, configure, policy, time, automatic, cancellationToken), CancellationToken.None);
            lineage.Running = run;
            return run;
        }
    }

    private static async Task<ReconnectResult> RunCoreAsync(
        Lineage lineage,
        string code,
        string seatToken,
        MatchMirror copy,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure,
        ReconnectPolicy policy,
        TimeProvider time,
        bool automatic,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var token = new StrongBox<string>(seatToken);
        var limit = automatic ? policy.GiveUpAfter : policy.AttemptTimeout;
        for (var attempt = 0; ; attempt++)
        {
            if (!automatic && attempt > 0)
            {
                return new ReconnectResult(ReconnectOutcome.GaveUp, null, attempt);
            }
            var delay = automatic ? policy.DelayBefore(attempt) : TimeSpan.Zero;
            if (time.GetElapsedTime(started) + delay > limit)
            {
                return new ReconnectResult(ReconnectOutcome.GaveUp, null, attempt);
            }
            if (delay > TimeSpan.Zero)
            {
                await WaitAsync(lineage, delay, time, cancellationToken);
            }
            var remaining = limit - time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                return new ReconnectResult(ReconnectOutcome.GaveUp, null, attempt);
            }

            var (session, error) = await AttemptAsync(code, token, copy, connect, configure, Min(remaining, policy.AttemptTimeout), time, cancellationToken);
            if (session is not null)
            {
                lock (lineage.Gate)
                {
                    lineage.Owner = session;
                }
                return new ReconnectResult(ReconnectOutcome.Rejoined, session, attempt + 1);
            }
            if (error is not null)
            {
                return new ReconnectResult(ReconnectOutcome.Refused, null, attempt + 1, error);
            }
        }
    }

    // One attempt, bounded by limit: connect, handshake and the rejoin answer. Returns the session on a Welcome, the
    // error on a final refusal, or neither to try again; seatToken is updated to the token to use next. Any session it
    // opened and does not return is disposed, also when the attempt times out or is cancelled.
    private static async Task<(ClientSession? Session, ProtocolError? Error)> AttemptAsync(
        string code,
        StrongBox<string> seatToken,
        MatchMirror copy,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure,
        TimeSpan limit,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        ClientSession? session = null;
        var keep = false;
        try
        {
            session = await ConnectAsync(connect, copy, linked.Token);
            configure?.Invoke(session);
            var answer = await session.RejoinAsync(code, seatToken.Value!, linked.Token);
            if (answer is Welcome)
            {
                keep = true;
                return (session, null);
            }
            if (answer is Rejected { Kind: RejectionKind.Protocol } rejected
                && Enum.TryParse<ProtocolError>(rejected.Code, out var error) && Final.Contains(error))
            {
                return (null, error);
            }
            // Anything else (busy, shutting down, an unexpected answer): try again later on a fresh connection.
            return (null, null);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested
            && e is WebSocketException or IOException or HttpRequestException or InvalidOperationException
                or System.Threading.Channels.ChannelClosedException or OperationCanceledException)
        {
            // The server could not be reached, did not answer in time, or the connection dropped during the rejoin.
            return (null, null);
        }
        finally
        {
            if (!keep && session is not null)
            {
                // A Welcome that arrived after the attempt was given up may have rotated (and confirmed) the token.
                if (session.SeatToken is { } newer)
                {
                    seatToken.Value = newer;
                }
                await session.DisposeAsync();
            }
        }
    }

    // Calls connect, but stops waiting when the token is cancelled even if connect ignores it. A session that connect
    // still returns is disposed: before this returns if connect ends within a moment (as one that honours the token
    // does), and otherwise as soon as it arrives.
    private static async Task<ClientSession> ConnectAsync(
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect, MatchMirror copy, CancellationToken cancellationToken)
    {
        var connecting = connect(copy, cancellationToken);
        try
        {
            return await connecting.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var closing = connecting.ContinueWith(
                t => t.IsCompletedSuccessfully ? t.Result.DisposeAsync().AsTask() : Task.CompletedTask,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            try
            {
                await closing.WaitAsync(ConnectGrace);
            }
            catch (TimeoutException)
            {
            }
            throw;
        }
    }

    private static readonly TimeSpan ConnectGrace = TimeSpan.FromSeconds(1);

    // Waits out a delay, or less if "Rejoin now" asks for an attempt at once.
    private static async Task WaitAsync(Lineage lineage, TimeSpan delay, TimeProvider time, CancellationToken cancellationToken)
    {
        Task wake;
        lock (lineage.Gate)
        {
            wake = lineage.Wake.Task;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sleeping = Task.Delay(delay, time, stop.Token);
        await Task.WhenAny(sleeping, wake);
        stop.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        lock (lineage.Gate)
        {
            if (lineage.Wake.Task.IsCompleted)
            {
                lineage.Wake = NewSignal();
            }
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
