using System.Net.WebSockets;
using System.Threading.Channels;
using Match.Client;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Core;

public enum ConnectionState
{
    NotConnected,
    Connected,
    Reconnecting,
    GaveUp,
    TakenOver,
    Refused,
    CopyFailed,
    Aborted,
    Finished,
}

public enum FeedbackKind
{
    Sending,
    Accepted,
    RulesRefused,
    ProtocolRefused,
    Stale,
    Unknown,
}

// What happened to the last command, in words. RuleRef names the rule the server cited.
public sealed record Feedback(FeedbackKind Kind, string Text, string? RuleRef = null);

// Notes: what the event shows above the models it concerns (UnitNotes).
// Row is the combat log's short form ("A4 FIGHTS B3", proposal G4); Text stays the narrator's full line.
public sealed record LogLine(int Seq, string Text, string? Dice, IReadOnlyList<UnitNote>? Notes = null, EventKind Kind = EventKind.Other, CompactRow? Row = null, Motion? Motion = null, DiceStep? Step = null);

// Everything the screen needs at one moment. Hints are empty whenever input is locked (InputLocked says why).
public sealed record ScreenState(
    long Version,
    ConnectionState Connection,
    string ConnectionText,
    bool CanRejoinNow,
    bool RejoinNeedsConfirmation,
    bool CanResync,
    string? OpponentAway,
    string? Code,
    Seat? Seat,
    MatchView View,
    IReadOnlyList<ActionHint> Hints,
    string? NotYetOffered,
    string? InputLocked,
    Feedback? Feedback,
    string? Warning,
    IReadOnlyList<LogLine> Log,
    BoardScene? Board,
    MatchState State,
    MatchEnd? End = null);

public sealed class MatchControllerOptions
{
    public required Uri Endpoint { get; init; }

    // Saved seats (owner-only). Null: tokens are not saved, so the seat cannot be rejoined after a crash.
    public SessionStore? Store { get; init; }

    // Automatic reconnect limit: set GiveUpAfter to the server's --rejoin-window (ReconnectPolicy.ForRejoinWindow).
    public ReconnectPolicy Policy { get; init; } = new();

    public TimeProvider Time { get; init; } = TimeProvider.System;

    // After a Stale refusal, how long to wait for the missing events before offering a deliberate resync.
    public TimeSpan StaleWait { get; init; } = TimeSpan.FromSeconds(5);

    // How many event lines the screen keeps.
    public int LogLines { get; init; } = 200;

    // Opens a connection; tests replace it. Defaults to ClientSession.ConnectAsync.
    public Func<Uri, MatchMirror?, CancellationToken, Task<ClientSession>>? Connect { get; init; }
}

// The client side of one seat in one match, for the match screen. It shows what the server's events built, offers
// the rules' legal choices for this seat as hints, sends one command at a time and reports the server's answer, and
// follows the connection: automatic reconnect and "Rejoin now" go through Reconnector (one run per seat), never a
// second path. It never decides anything itself. Thread-safe: messages arrive on the session's receive loop, and
// Changed is raised on whatever thread made the change; a UI reads Snapshot() on its own thread.
public sealed class MatchController : IAsyncDisposable
{
    private readonly MatchControllerOptions _options;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SortedList<int, LogLine> _log = [];
    private ClientSession? _session;
    private Welcome? _welcome;
    private ContentNames _names = ContentNames.None;
    private ConnectionState _connection = ConnectionState.NotConnected;
    private string _connectionText = "Not connected.";
    private ProtocolError? _refusedCode;
    private DateTimeOffset? _opponentAwayUntil;

    // The connection that reported the opponent away. The server sends the pause right after a Welcome, often before
    // that connection is attached, so attaching keeps a pause its own connection reported.
    private ClientSession? _awayFrom;
    private Feedback? _feedback;
    private string? _warning;
    private bool _sending;

    // The command whose answer was lost with the connection, and its label. After a rejoin the server's Welcome says what
    // happened to it: its LastAnswer when that answers this command id, otherwise (NextSeq not past it) it was not taken.
    private (SubmitCommand Submit, string Label)? _lost;
    private bool _staleUnresolved;
    private int _staleInARow;
    private long _version;

    public MatchController(MatchControllerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public event Action? Changed;

    public ClientSession? Session
    {
        get
        {
            lock (_gate)
            {
                return _session;
            }
        }
    }

    public Task<ServerMessage> CreateAsync(string scenario, CancellationToken cancellationToken = default) =>
        StartAsync((s, ct) => s.CreateAsync(scenario, ct), cancellationToken);

    public Task<ServerMessage> JoinAsync(string code, CancellationToken cancellationToken = default) =>
        StartAsync((s, ct) => s.JoinAsync(code, ct), cancellationToken);

    // Takes a saved seat back (after a crash or a restart of the client).
    public async Task<ServerMessage> RejoinSavedAsync(SavedSeat seat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seat);
        var answer = await StartAsync((s, ct) => s.RejoinAsync(seat.Code, seat.Token, ct), cancellationToken);
        if (answer is Rejected { Code: nameof(ProtocolError.UnknownMatch) })
        {
            // The match is gone (it ended, or the server restarted): the saved seat is no use any more.
            _options.Store?.Delete(seat.MatchId, seat.Seat);
        }
        return answer;
    }

    // Sends one command and reports the server's answer. Only one command is in flight at a time, and none while the
    // match is paused, over, or not connected. Nothing is ever retried automatically.
    public async Task<Feedback> SendAsync(Command command, string label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ClientSession session;
        lock (_gate)
        {
            if (LockedReason() is { } locked)
            {
                return new Feedback(FeedbackKind.ProtocolRefused, $"Not sent: {locked}");
            }
            session = _session!;
            _sending = true;
            _lost = null;
            _feedback = new Feedback(FeedbackKind.Sending, $"Sent: {label}. Waiting for the server…");
        }
        Raise();
        Feedback result;
        try
        {
            var answer = await session.SubmitAsync(command, cancellationToken);
            result = answer switch
            {
                Accepted => new Feedback(FeedbackKind.Accepted, $"Done: {label}."),
                Rejected { Kind: RejectionKind.Rules } r => new Feedback(FeedbackKind.RulesRefused, $"Refused by the rules: {r.Text}", r.RuleRef),
                Rejected { Code: nameof(ProtocolError.Stale) } r => await StaleAsync(session, r, cancellationToken),
                Rejected r => new Feedback(FeedbackKind.ProtocolRefused, $"The server did not take the command ({r.Code}): {r.Text}"),
                _ => new Feedback(FeedbackKind.Unknown, $"Unexpected answer: {answer.GetType().Name}."),
            };
            if (result.Kind != FeedbackKind.Stale)
            {
                lock (_gate)
                {
                    _staleInARow = 0;
                    _staleUnresolved = false;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or InvalidOperationException or ChannelClosedException
            || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The answer was lost with the connection. After a rejoin the server says what happened to it.
            result = new Feedback(FeedbackKind.Unknown, $"The connection dropped before the server answered \"{label}\". After reconnecting, the server reports what happened.");
            lock (_gate)
            {
                _lost = session.LastSubmit is { } submit && ReferenceEquals(submit.Command, command) ? (submit, label) : null;
                // A rejoin can already have happened (a fast reconnect): its Welcome answers now.
                if (!ReferenceEquals(_session, session) && _session?.Welcome is { } newer && AfterRejoin(newer) is { } known)
                {
                    result = known;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _sending = false;
            }
        }
        lock (_gate)
        {
            _feedback = result;
        }
        Raise();
        return result;
    }

    // What happened to the command whose answer was lost, from the Welcome of a rejoin; null when it says nothing about
    // it. Only the server's own answer to that command id counts as accepted or refused (never an older command's).
    private Feedback? AfterRejoin(Welcome welcome)
    {
        if (_lost is not { } lost)
        {
            return null;
        }
        Feedback? known = welcome.LastAnswer switch
        {
            Accepted a when a.CommandId == lost.Submit.CommandId =>
                new Feedback(FeedbackKind.Accepted, $"Done: {lost.Label}. (The answer was lost with the connection; after reconnecting, the server says it accepted it.)"),
            Rejected r when r.CommandId == lost.Submit.CommandId =>
                new Feedback(FeedbackKind.RulesRefused, $"Refused by the rules: {r.Text} (\"{lost.Label}\": the answer was lost with the connection; after reconnecting, the server says it refused it.)", r.RuleRef),
            _ when welcome.NextSeq <= lost.Submit.Seq =>
                new Feedback(FeedbackKind.ProtocolRefused, $"Not taken: the server never received \"{lost.Label}\" before the connection dropped, so nothing happened. Send it again if you still want it."),
            _ => null,
        };
        if (known is not null)
        {
            _lost = null;
        }
        return known;
    }

    // "Rejoin now": one deliberate attempt (or joining the automatic run already in progress), never a second
    // connection alongside one. After a takeover the player must confirm, because the seat is then taken from their
    // other window.
    public async Task<ReconnectResult> RejoinNowAsync(bool takeSeatBack = false, CancellationToken cancellationToken = default)
    {
        ClientSession dropped;
        lock (_gate)
        {
            if (_session is not { IsClosed: true } closed)
            {
                throw new InvalidOperationException("The connection is open; there is nothing to rejoin.");
            }
            if (_connection == ConnectionState.TakenOver && !takeSeatBack)
            {
                throw new InvalidOperationException("This seat was opened from another window. Confirm to take it back.");
            }
            dropped = closed;
            SetConnection(ConnectionState.Reconnecting, "Rejoining…");
        }
        Raise();
        // After a takeover, the player's other window (on this machine) saved a newer token; the old one stops opening
        // the seat once that window confirmed it.
        string? newer = null;
        if (dropped.TakenOver && _options.Store is { } store && dropped.Welcome is { } w
            && store.Find(_options.Endpoint.ToString(), w.Code, w.Seat) is { Status: SavedSeatStatus.Found, Seat: { } saved })
        {
            newer = saved.Token;
        }
        var result = await Reconnector.RejoinNowAsync(dropped, ConnectWith, Configure, _options.Policy, _options.Time, Linked(cancellationToken), newer);
        Handle(dropped, result);
        return result;
    }

    // Deliberate recovery after a Stale refusal whose events did not arrive in time: close this connection and take
    // the seat back on a new one, through the same reconnect path. Only on the player's request.
    public async Task ResyncAsync(CancellationToken cancellationToken = default)
    {
        ClientSession? session;
        lock (_gate)
        {
            session = _session;
            _staleUnresolved = false;
            _staleInARow = 0;
            _feedback = new Feedback(FeedbackKind.Stale, "Reconnecting to fetch the latest events…");
        }
        Raise();
        if (session is not null)
        {
            // The receive loop sees our close, and the watcher reconnects (Reconnector: one run per seat).
            await session.CloseAsync(cancellationToken);
        }
    }

    // The Stale path on its own, for tests: a real Stale refusal depends on message timing.
    internal Task<Feedback> HandleStaleAsync(Rejected rejected, CancellationToken cancellationToken = default)
    {
        var session = Session ?? throw new InvalidOperationException("Not in a match.");
        return StaleAsync(session, rejected, cancellationToken).ContinueWith(t =>
        {
            lock (_gate)
            {
                _feedback = t.Result;
            }
            Raise();
            return t.Result;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public ScreenState Snapshot()
    {
        lock (_gate)
        {
            // One read of the copy: the network thread may apply the next event at any moment.
            var copy = _session?.Mirror.Current;
            var state = copy?.State ?? MatchState.None;
            var seat = _welcome?.Seat;
            var view = MatchView.Of(state, seat, _names);
            var locked = LockedReason();
            var hints = locked is null ? ActionHints.For(state, seat, _names) : [];
            var away = _opponentAwayUntil is { } until && _connection == ConnectionState.Connected
                ? $"Your opponent disconnected. The match is paused until they rejoin, at the latest {until:HH:mm:ss} UTC (server time). If they do not, the match is aborted with no result."
                : null;
            var canRejoin = _connection is ConnectionState.Reconnecting or ConnectionState.GaveUp or ConnectionState.TakenOver or ConnectionState.CopyFailed
                || (_connection == ConnectionState.Refused && _refusedCode is null);
            return new ScreenState(
                _version,
                _connection,
                _connectionText,
                canRejoin && _session is { IsClosed: true },
                _connection == ConnectionState.TakenOver,
                _staleUnresolved && _connection == ConnectionState.Connected,
                away,
                _welcome?.Code,
                seat,
                view,
                hints,
                locked is null ? ActionHints.NotYetOffered(state, seat) : null,
                locked,
                _feedback,
                _warning,
                _log.Values.TakeLast(_options.LogLines).ToList(),
                BoardScene.Of(state, seat, _names),
                state,
                EndOf(view, copy));
        }
    }

    // The result, final hash and event count once the match has ended or been aborted (null before). The hash is the
    // copy's own: every event in it matched the server's hash, and an abort message must match it too (ClientSession).
    private MatchEnd? EndOf(MatchView view, MirrorCopy? copy)
    {
        if (_session is not { } session || copy is null)
        {
            return null;
        }
        var mirror = copy;
        var aborted = _connection == ConnectionState.Aborted || session.Aborted is not null;
        if (!aborted && _connection != ConnectionState.Finished && mirror.State.Phase != Phase.Ended)
        {
            return null;
        }
        if (aborted)
        {
            var note = session.Aborted is null && session.Welcome?.Status != MatchStatus.Aborted
                ? "This window did not receive the server's abort message: the hash is this client's copy after the last event it received."
                : null;
            return new MatchEnd(true, _connectionText, mirror.Events, mirror.Hash, note);
        }
        return new MatchEnd(false, view.Outcome ?? "The match has ended.", mirror.Events, mirror.Hash, null);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        ClientSession? session;
        lock (_gate)
        {
            session = _session;
        }
        if (session is not null)
        {
            await session.DisposeAsync();
        }
        _stop.Dispose();
    }

    private async Task<ServerMessage> StartAsync(Func<ClientSession, CancellationToken, Task<ServerMessage>> request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                throw new InvalidOperationException("This controller already has a match.");
            }
        }
        var session = await ConnectWith(new MatchMirror(), cancellationToken);
        Configure(session);
        lock (_gate)
        {
            _session = session;
        }
        var answer = await request(session, cancellationToken);
        if (answer is Welcome)
        {
            Attach(session);
        }
        else
        {
            lock (_gate)
            {
                _session = null;
            }
            await session.DisposeAsync();
        }
        return answer;
    }

    private Task<ClientSession> ConnectWith(MatchMirror mirror, CancellationToken cancellationToken) =>
        _options.Connect is { } connect
            ? connect(_options.Endpoint, mirror, cancellationToken)
            : ClientSession.ConnectAsync(_options.Endpoint, cancellationToken, mirror: mirror);

    // Every new session (first connection or a reconnect attempt): save tokens before confirming them, and follow its
    // messages. Messages from an attempt that is later abandoned only ever describe events of this same match.
    private void Configure(ClientSession session)
    {
        session.SaveToken = SaveToken;
        session.Received += message => OnReceived(session, message);
    }

    private bool SaveToken(Welcome welcome)
    {
        if (_options.Store is not { } store)
        {
            return false;
        }
        if (store.TrySave(new SavedSeat(_options.Endpoint.ToString(), welcome.Code, welcome.MatchId, welcome.Seat, welcome.SeatToken!), out var problem))
        {
            return true;
        }
        lock (_gate)
        {
            _warning = $"{problem} You cannot rejoin this match after a crash.";
        }
        return false;
    }

    private void OnReceived(ClientSession session, ServerMessage message)
    {
        lock (_gate)
        {
            switch (message)
            {
                case Welcome { ReplayFrom: 0 }:
                    _log.Clear();
                    break;
                case MatchAborted a when ReferenceEquals(session, _session):
                    SetConnection(ConnectionState.Aborted, $"Match aborted ({a.Reason}). No result was recorded as a win or loss.");
                    ForgetSeat();
                    break;
                case EventPublished { Event: Rules.Core.Events.MatchEnded } e when ReferenceEquals(session, _session):
                    _log[e.Seq] = new LogLine(e.Seq, EventText.Line(e.Event), EventText.Dice(e.Event), UnitNotes.Of(e.Event), EventText.Kind(e.Event), EventText.Compact(e.Event), Motions.Of(e.Event), DiceRolls.Step(e.Event));
                    SetConnection(ConnectionState.Finished, "The match has ended. The result above is the server's.");
                    // The match is over: the saved seat has nothing left to rejoin (as trench-cli does).
                    ForgetSeat();
                    break;
                case EventPublished e:
                    _log[e.Seq] = new LogLine(e.Seq, EventText.Line(e.Event), EventText.Dice(e.Event), UnitNotes.Of(e.Event), EventText.Kind(e.Event), EventText.Compact(e.Event), Motions.Of(e.Event), DiceRolls.Step(e.Event));
                    while (_log.Count > _options.LogLines)
                    {
                        _log.RemoveAt(0);
                    }
                    break;
                case MatchPaused p when p.Seat != (session.Welcome?.Seat ?? _welcome?.Seat):
                    _opponentAwayUntil = p.RejoinBy;
                    _awayFrom = session;
                    break;
                case MatchResumed:
                    _opponentAwayUntil = null;
                    _awayFrom = null;
                    break;
            }
            _version++;
        }
        Raise();
    }

    // A session that holds the seat: follow it until it closes.
    private void Attach(ClientSession session)
    {
        lock (_gate)
        {
            _session = session;
            _welcome = session.Welcome;
            _names = ContentNames.For(session.Welcome!.Scenario, session.Mirror.State.Setup);
            if (!ReferenceEquals(_awayFrom, session))
            {
                _opponentAwayUntil = null;
                _awayFrom = null;
            }
            if (session.Welcome.Status == MatchStatus.Aborted)
            {
                SetConnection(ConnectionState.Aborted, "Match aborted. No result was recorded as a win or loss.");
                ForgetSeat();
            }
            else if (session.Welcome.Status == MatchStatus.Finished)
            {
                SetConnection(ConnectionState.Finished, "The match has ended. The result above is the server's.");
            }
            else
            {
                SetConnection(ConnectionState.Connected, "Connected.");
            }
            if (_feedback is { Kind: FeedbackKind.Unknown } && AfterRejoin(session.Welcome) is { } known)
            {
                _feedback = known;
            }
        }
        _ = Task.Run(() => WatchAsync(session));
        Raise();
    }

    private async Task WatchAsync(ClientSession session)
    {
        try
        {
            await session.WaitForAsync(s => s.Synchronized || s.IsClosed, _stop.Token);
        }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException)
        {
        }
        lock (_gate)
        {
            // Names need the setup, which arrives with the first event.
            if (ReferenceEquals(session, _session) && _names.WarbandOne is null && _names.Problem is null)
            {
                _names = ContentNames.For(session.Welcome!.Scenario, session.Mirror.State.Setup);
            }
            _version++;
        }
        Raise();
        try
        {
            await session.Completion.WaitAsync(_stop.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await OnClosedAsync(session);
    }

    private async Task OnClosedAsync(ClientSession session)
    {
        ReconnectDecision decision;
        lock (_gate)
        {
            if (!ReferenceEquals(session, _session) || _stop.IsCancellationRequested)
            {
                return;
            }
            decision = Reconnector.Decide(session);
            switch (decision)
            {
                case ReconnectDecision.Reconnect:
                    SetConnection(ConnectionState.Reconnecting, "Connection lost; reconnecting…");
                    break;
                case ReconnectDecision.TakenOver:
                    SetConnection(ConnectionState.TakenOver, "This seat was opened from another window. This window will not take it back on its own.");
                    break;
                case ReconnectDecision.Refused:
                    _refusedCode = null;
                    SetConnection(ConnectionState.Refused, $"The server closed the connection ({session.CloseText ?? "policy"}).");
                    break;
                case ReconnectDecision.CopyFailed:
                    SetConnection(ConnectionState.CopyFailed, $"This client's copy of the match no longer matches the server's ({session.Failure}).");
                    break;
                case ReconnectDecision.Aborted:
                    var a = session.Aborted;
                    SetConnection(ConnectionState.Aborted, $"Match aborted{(a is null ? "" : $" ({a.Reason})")}. No result was recorded as a win or loss.");
                    ForgetSeat();
                    break;
                case ReconnectDecision.Finished:
                    SetConnection(ConnectionState.Finished, "The match has ended.");
                    ForgetSeat();
                    break;
                default:
                    SetConnection(ConnectionState.NotConnected, "Not connected.");
                    break;
            }
        }
        Raise();
        if (decision != ReconnectDecision.Reconnect)
        {
            return;
        }
        try
        {
            var result = await Reconnector.RunAsync(session, ConnectWith, Configure, _options.Policy, _options.Time, _stop.Token);
            Handle(session, result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // A newer session took over this one's lineage (for example "Rejoin now" already rejoined).
        }
    }

    private void Handle(ClientSession dropped, ReconnectResult result)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(dropped, _session) && !(result.Session is { } s && ReferenceEquals(s, _session)))
            {
                return;
            }
        }
        switch (result.Outcome)
        {
            case ReconnectOutcome.Rejoined when !ReferenceEquals(result.Session, Session):
                Attach(result.Session!);
                return;
            case ReconnectOutcome.Rejoined:
                return;
            case ReconnectOutcome.Refused:
                lock (_gate)
                {
                    _refusedCode = result.Code;
                    if (result.Code is ProtocolError.MatchAborted or ProtocolError.UnknownMatch)
                    {
                        SetConnection(ConnectionState.Aborted, $"The match is no longer on the server ({result.Code}). No result was recorded as a win or loss.");
                        ForgetSeat();
                    }
                    else
                    {
                        SetConnection(ConnectionState.Refused, $"The server refused to give this seat back ({result.Code}).");
                    }
                }
                break;
            case ReconnectOutcome.GaveUp:
                lock (_gate)
                {
                    SetConnection(ConnectionState.GaveUp,
                        "Could not reconnect. The server may still hold your seat until its rejoin window ends: use Rejoin now.");
                }
                break;
        }
        Raise();
    }

    // After a Stale refusal the server has events this copy does not have yet; they are on their way on this same
    // connection. Wait for them (bounded, event-driven, no polling) and let the player choose again. Never rejoin or
    // reconnect on our own while the connection is live.
    private async Task<Feedback> StaleAsync(ClientSession session, Rejected rejected, CancellationToken cancellationToken)
    {
        int inARow;
        lock (_gate)
        {
            inARow = ++_staleInARow;
            _feedback = new Feedback(FeedbackKind.Stale, "The board was out of date. Waiting for the latest events…");
        }
        Raise();
        using var timeout = new CancellationTokenSource(_options.StaleWait, _options.Time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await session.WaitForAsync(s => s.Mirror.Events >= rejected.Events, linked.Token);
            lock (_gate)
            {
                _staleUnresolved = false;
            }
            return new Feedback(FeedbackKind.Stale,
                $"The board was out of date and is up to date now. Nothing was done; choose again.{(inARow > 1 ? $" ({inARow} times in a row.)" : "")}");
        }
        catch (Exception e) when ((e is OperationCanceledException && !cancellationToken.IsCancellationRequested) || e is InvalidOperationException)
        {
            lock (_gate)
            {
                _staleUnresolved = true;
            }
            return new Feedback(FeedbackKind.Stale,
                "The board is out of date and the latest events did not arrive in time. Nothing was done. Use Resync to fetch them on a new connection.");
        }
    }

    // Why the player cannot send a command now, or null.
    private string? LockedReason()
    {
        if (_session is null || _welcome is null)
        {
            return "not in a match.";
        }
        if (_session.Mirror.State.Phase == Phase.Ended || _session.Aborted is not null
            || _connection is ConnectionState.Finished or ConnectionState.Aborted)
        {
            return "the match is over.";
        }
        if (_connection != ConnectionState.Connected || _session.IsClosed)
        {
            return "not connected.";
        }
        if (_sending)
        {
            return "waiting for the server's answer to the last command.";
        }
        if (_opponentAwayUntil is not null)
        {
            return "the match is paused while your opponent is away.";
        }
        if (!_session.Synchronized)
        {
            return "catching up with the server.";
        }
        return null;
    }

    private void SetConnection(ConnectionState state, string text)
    {
        _connection = state;
        _connectionText = text;
        _version++;
    }

    private void ForgetSeat()
    {
        if (_welcome is { } w)
        {
            _options.Store?.Delete(w.MatchId, w.Seat);
        }
    }

    private CancellationToken Linked(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token).Token : _stop.Token;

    private void Raise()
    {
        lock (_gate)
        {
            _version++;
        }
        Changed?.Invoke();
    }
}
