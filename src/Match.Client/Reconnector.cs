using System.Net.WebSockets;
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

    // The server could not be reached before the policy's time ran out.
    GaveUp,
}

// The result of one reconnect run. Session is set when Rejoined; Code names the refusal when Refused.
public sealed record ReconnectResult(ReconnectOutcome Outcome, ClientSession? Session, int Attempts, ProtocolError? Code = null);

// How long to wait between rejoin attempts and when to stop. The defaults wait 1, 2, 4 and 8 seconds, then every 15,
// and give up after five minutes, the server's default rejoin window.
public sealed record ReconnectPolicy
{
    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    public TimeSpan Repeat { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan GiveUpAfter { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan DelayBefore(int attempt) =>
        attempt <= 0 ? TimeSpan.Zero : attempt <= Delays.Count ? Delays[attempt - 1] : Repeat;
}

// Takes a seat back after a transient drop, with the existing M6 rejoin: the seat token, and the copy of the match the
// dropped session held, so the server replays only what is missing. It never retries after a refusal, a takeover or a
// finished or aborted match, and it never invents state: the new session's copy is checked against the server's hash
// exactly as any other session's is.
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

    // Rejoins the seat the dropped session held. connect opens a new connection with the given copy of the match;
    // configure is called on each new session before it rejoins (to set SaveToken and handlers). The dropped session is
    // not disposed here; the caller owns it.
    public static async Task<ReconnectResult> RunAsync(
        ClientSession dropped,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure = null,
        ReconnectPolicy? policy = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        ArgumentNullException.ThrowIfNull(connect);
        if (Decide(dropped) != ReconnectDecision.Reconnect)
        {
            throw new InvalidOperationException($"This session should not be reconnected ({Decide(dropped)}).");
        }
        var code = dropped.Welcome!.Code;
        var token = dropped.SeatToken!;
        return await RunAsync(code, token, dropped.Mirror, connect, configure, policy, time, cancellationToken);
    }

    // Rejoins a seat by code and token, starting from the given copy of the match (an empty copy replays everything).
    public static async Task<ReconnectResult> RunAsync(
        string code,
        string seatToken,
        MatchMirror mirror,
        Func<MatchMirror, CancellationToken, Task<ClientSession>> connect,
        Action<ClientSession>? configure = null,
        ReconnectPolicy? policy = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(connect);
        policy ??= new ReconnectPolicy();
        time ??= TimeProvider.System;
        var started = time.GetTimestamp();
        for (var attempt = 0; ; attempt++)
        {
            var delay = policy.DelayBefore(attempt);
            if (time.GetElapsedTime(started) + delay > policy.GiveUpAfter)
            {
                return new ReconnectResult(ReconnectOutcome.GaveUp, null, attempt);
            }
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, time, cancellationToken);
            }

            ClientSession? session = null;
            try
            {
                session = await connect(mirror, cancellationToken);
                configure?.Invoke(session);
                var answer = await session.RejoinAsync(code, seatToken, cancellationToken);
                if (answer is Welcome)
                {
                    return new ReconnectResult(ReconnectOutcome.Rejoined, session, attempt + 1);
                }
                if (answer is Rejected { Kind: RejectionKind.Protocol } rejected
                    && Enum.TryParse<ProtocolError>(rejected.Code, out var error) && Final.Contains(error))
                {
                    await session.DisposeAsync();
                    return new ReconnectResult(ReconnectOutcome.Refused, null, attempt + 1, error);
                }
                // Anything else (busy, shutting down, an unexpected answer): try again later on a fresh connection.
                await session.DisposeAsync();
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested
                && e is WebSocketException or IOException or HttpRequestException or InvalidOperationException
                    or System.Threading.Channels.ChannelClosedException or OperationCanceledException)
            {
                // The server could not be reached, or the connection dropped during the rejoin.
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }
        }
    }
}
