using System.Security.Cryptography;
using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Match.Server;

// One match: the canonical state and event log, the server's dice, and both seats. Not thread-safe; the Lobby calls it
// under its lock. Every outcome is produced here, through TurnSequence.Decide with the server's dice.
internal sealed class MatchHost
{
    // How many answers per seat are kept for resends. An older resend is refused as out of order.
    public const int RememberedAnswers = 256;

    // Tokens issued to a seat and not yet confirmed. Each stays valid until one of them is confirmed.
    public const int MaxUnconfirmedTokens = 4;

    private readonly IDiceSource _dice;
    private readonly Outbox _outbox;
    private readonly IMatchRecorder _recorder;
    private readonly TimeSpan _rejoinWindow;
    private readonly SeatSlot[] _slots = [new(), new()];
    private readonly List<EventPublished> _log = [];
    private readonly MatchSetup _setup;
    private MatchState _state = MatchState.None;
    private bool _started;
    private DateTimeOffset? _endedAt;
    private DateTimeOffset? _lastLeftAt;

    public MatchHost(
        string code,
        string matchId,
        string scenario,
        MatchSetup setup,
        IDiceSource dice,
        Outbox outbox,
        IMatchRecorder recorder,
        TimeSpan rejoinWindow,
        DateTimeOffset createdAt)
    {
        Code = code;
        MatchId = matchId;
        Scenario = scenario;
        CreatedAt = createdAt;
        _setup = setup;
        _dice = dice;
        _outbox = outbox;
        _recorder = recorder;
        _rejoinWindow = rejoinWindow;
        Append(new MatchCreated(setup));
    }

    public string Code { get; }
    public string MatchId { get; }
    public string Scenario { get; }
    public DateTimeOffset CreatedAt { get; }
    public MatchStatus Status { get; private set; } = MatchStatus.Waiting;
    public MatchState State => _state;
    public IReadOnlyList<EventPublished> Log => _log;
    public AbortReason? AbortedBecause { get; private set; }

    public bool HasConnections => _slots.Any(s => s.Connection is not null);

    public bool IsOver => Status is MatchStatus.Finished or MatchStatus.Aborted;

    public ConnectionId? ConnectionOf(Seat seat) => Slot(seat).Connection;

    public DateTimeOffset? DeadlineOf(Seat seat) => Slot(seat).AwayDeadline;

    public long NextSeq(Seat seat) => Slot(seat).NextSeq;

    public bool IsFree(Seat seat) => !Slot(seat).Taken;

    // Seats a connection that created or joined the match: Welcome with the seat's first token, then every event so
    // far. The second seat starts the match.
    public void Seat(Seat seat, ConnectionId connection, DateTimeOffset now)
    {
        var slot = Slot(seat);
        slot.Connection = connection;
        slot.Taken = true;
        var token = slot.Issue(connection);
        var starts = seat == Rules.Core.State.Seat.Two;
        if (starts)
        {
            Status = MatchStatus.Playing;
            _started = true;
        }
        _outbox.Send(connection, WelcomeFor(seat, token, replayFrom: 0));
        _outbox.SendBacklog(connection, _log);

        if (starts)
        {
            _recorder.MatchStarted(new OpenMatch(MatchId, Code, CreatedAt, Scenario, RecordFormat.ContentFingerprint(_setup),
                RecordFormat.CurrentEngine(RecordStore.Build)));
            if (Slot(Rules.Core.State.Seat.One).Connection is { } first)
            {
                _outbox.Send(first, new Notice(NoticeKind.OpponentJoined));
            }
            // The server, not a player, starts the match.
            var result = Decide(new StartMatch());
            if (result is null || !result.IsAccepted)
            {
                Fault(now);
                return;
            }
            Commit(result, now);
        }
    }

    // The seat a token belongs to, if it is the seat's confirmed token or one issued since. Constant-time comparisons.
    public Seat? SeatOfToken(string token)
    {
        var hash = TokenHash(token);
        Seat? found = null;
        foreach (var seat in new[] { Rules.Core.State.Seat.One, Rules.Core.State.Seat.Two })
        {
            if (Slot(seat).Holds(hash))
            {
                found = seat;
            }
        }
        return found;
    }

    // Takes a seat back on a new connection; the Lobby has already checked the token and detached any old connection.
    // Everything here happens in one hold of the Lobby's lock: the backlog is queued before any later event can be.
    public void Rejoin(Seat seat, ConnectionId connection, Rejoin rejoin, DateTimeOffset now)
    {
        var slot = Slot(seat);
        slot.Connection = connection;

        // The client's copy is kept only if it is a true prefix of this match's history.
        var replayFrom = 0;
        if (rejoin.KnownEvents >= 1 && rejoin.KnownEvents <= _log.Count && _log[rejoin.KnownEvents - 1].Hash == rejoin.KnownHash)
        {
            replayFrom = rejoin.KnownEvents;
        }

        // A match that is over can be looked at, never changed: no token is issued and nothing else moves.
        string? token = null;
        if (!IsOver)
        {
            slot.PromoteIfIssued(TokenHash(rejoin.SeatToken));
            token = slot.Issue(connection);
        }

        var wasAway = slot.AwayDeadline is not null;
        slot.AwayDeadline = null;
        if (Status == MatchStatus.Paused && _slots.All(s => s.AwayDeadline is null))
        {
            Status = MatchStatus.Playing;
        }

        _outbox.Send(connection, WelcomeFor(seat, token, replayFrom));
        _outbox.SendBacklog(connection, _log.Skip(replayFrom).ToList());
        switch (Status)
        {
            case MatchStatus.Aborted:
                _outbox.Send(connection, new MatchAborted(AbortedBecause!.Value, AbortedSeat, _log.Count, _log[^1].Hash));
                break;
            case MatchStatus.Paused:
                foreach (var away in AwaySeats())
                {
                    _outbox.Send(connection, new MatchPaused(away, Slot(away).AwayDeadline!.Value));
                }
                break;
        }
        if (wasAway && Status == MatchStatus.Playing && Slot(seat.Opponent()).Connection is { } other)
        {
            _outbox.Send(other, new MatchResumed(seat));
        }
    }

    // The client saved the token it was last sent on this connection: from now on only that token is valid.
    public bool TokenSaved(Seat seat, ConnectionId from) => Slot(seat).Confirm(from);

    public void Submit(Seat seat, ConnectionId from, SubmitCommand submit, DateTimeOffset now)
    {
        var slot = Slot(seat);
        switch (Status)
        {
            case MatchStatus.Waiting:
                Refuse(from, submit, slot, ProtocolError.MatchNotStarted, "The match starts when the second player joins.");
                return;
            case MatchStatus.Paused:
                Refuse(from, submit, slot, ProtocolError.MatchPaused, "The match is paused until the other player rejoins.");
                return;
            case MatchStatus.Aborted:
                Refuse(from, submit, slot, ProtocolError.MatchAborted, "This match was aborted.");
                return;
            case MatchStatus.Finished:
                Refuse(from, submit, slot, ProtocolError.MatchFinished, "This match is over.");
                return;
        }

        // A connection plays only its own seat. StartMatch has no seat: only the server issues it.
        var commandSeat = CommandSeats.Of(submit.Command);
        if (commandSeat is null)
        {
            Refuse(from, submit, slot, ProtocolError.ServerOnlyCommand, "Only the server issues this command.");
            return;
        }
        if (commandSeat != seat)
        {
            Refuse(from, submit, slot, ProtocolError.SeatMismatch, "You can only command your own seat.");
            return;
        }

        // A resend of a command already answered gets the same answer, and nothing is applied again. The fingerprint
        // is the seq and the command, not knownEvents, so a resend after a rejoin is still recognised.
        var fingerprint = Fingerprint(submit);
        if (slot.Answers.TryGetValue(submit.CommandId, out var answer))
        {
            if (answer.Fingerprint != fingerprint)
            {
                Refuse(from, submit, slot, ProtocolError.IdReused, "This command id was already used for another command.");
                return;
            }
            _outbox.Send(from, AsDuplicate(answer.Result));
            return;
        }
        if (submit.Seq != slot.NextSeq)
        {
            Refuse(from, submit, slot, ProtocolError.OutOfOrder, $"The next command number is {slot.NextSeq}.");
            return;
        }
        if (submit.KnownEvents != _log.Count)
        {
            Refuse(from, submit, slot, ProtocolError.Stale, $"The match has {_log.Count} events; apply them and decide again.");
            return;
        }

        var result = Decide(submit.Command);
        if (result is null)
        {
            Fault(now);
            return;
        }

        ServerMessage reply;
        if (result.IsAccepted)
        {
            var first = _log.Count + 1;
            if (!Commit(result, now))
            {
                return;
            }
            reply = new Accepted(submit.CommandId, submit.Seq, first, _log.Count, Duplicate: false);
        }
        else
        {
            var rejection = result.Rejection!;
            reply = new Rejected(submit.CommandId, submit.Seq, RejectionKind.Rules, rejection.Code.ToString(), rejection.Text,
                rejection.RuleRef, _log.Count, submit.Seq + 1, Duplicate: false);
        }

        // An accepted or rules-refused command uses up its number, and its answer is kept for resends.
        slot.NextSeq = submit.Seq + 1;
        slot.Remember(submit.CommandId, fingerprint, reply);
        _outbox.Send(from, reply);
    }

    // The seat's connection ended (or was taken over; then the Lobby does not call this).
    public void Disconnect(Seat seat, DateTimeOffset now)
    {
        var slot = Slot(seat);
        slot.Connection = null;
        if (!HasConnections)
        {
            _lastLeftAt = now;
        }
        switch (Status)
        {
            case MatchStatus.Finished:
                // The rules already decided the match; the result stands.
                if (Slot(seat.Opponent()).Connection is { } other)
                {
                    _outbox.Send(other, new Notice(NoticeKind.OpponentDisconnected));
                }
                break;
            case MatchStatus.Waiting:
                // Nobody to wait for: the match never started.
                Abort(AbortReason.Disconnected, seat, now);
                break;
            case MatchStatus.Playing or MatchStatus.Paused when _rejoinWindow <= TimeSpan.Zero:
                Abort(AbortReason.Disconnected, seat, now);
                break;
            case MatchStatus.Playing or MatchStatus.Paused:
                // The deadline is fixed now; only a successful rejoin clears it.
                slot.AwayDeadline = now + _rejoinWindow;
                Status = MatchStatus.Paused;
                Broadcast(new MatchPaused(seat, slot.AwayDeadline.Value));
                break;
        }
    }

    // Aborts the match when the earliest rejoin deadline has passed.
    public void Tick(DateTimeOffset now)
    {
        if (Status != MatchStatus.Paused)
        {
            return;
        }
        var expired = AwaySeats().Where(s => Slot(s).AwayDeadline <= now).OrderBy(s => Slot(s).AwayDeadline).ToList();
        if (expired.Count > 0)
        {
            Abort(AbortReason.RejoinExpired, expired[0], now);
        }
    }

    // A match is dropped once nobody is connected and: it never started, or it is over and its read-only window has
    // passed. A paused match is never dropped; it expires first.
    public bool CanRemove(DateTimeOffset now)
    {
        if (HasConnections)
        {
            return false;
        }
        if (!_started)
        {
            return true;
        }
        if (!IsOver)
        {
            return false;
        }
        var since = new[] { _endedAt, _lastLeftAt }.Max() ?? now;
        return now >= since + _rejoinWindow;
    }

    public void Shutdown(DateTimeOffset now)
    {
        if (!IsOver)
        {
            Abort(AbortReason.ServerShutdown, null, now);
        }
        Broadcast(new Notice(NoticeKind.ServerShutdown));
    }

    private Seat? AbortedSeat { get; set; }

    private IEnumerable<Seat> AwaySeats() =>
        new[] { Rules.Core.State.Seat.One, Rules.Core.State.Seat.Two }.Where(s => Slot(s).AwayDeadline is not null);

    private Welcome WelcomeFor(Seat seat, string? token, int replayFrom)
    {
        var slot = Slot(seat);
        return new Welcome(Code, MatchId, seat, Scenario, token, replayFrom, _log.Count, _log[^1].Hash, slot.NextSeq, slot.LastAnswer, Status);
    }

    private SeatSlot Slot(Seat seat) => _slots[seat == Rules.Core.State.Seat.One ? 0 : 1];

    // Null when the rules threw instead of answering: a server fault, never the player's doing.
    private DecideResult? Decide(Command command)
    {
        try
        {
            return TurnSequence.Decide(_state, command, _dice);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    // Applies the accepted events one at a time, checks the result against Decide's own, then publishes them in order.
    private bool Commit(DecideResult result, DateTimeOffset now)
    {
        var state = _state;
        var published = new List<EventPublished>(result.Events.Length);
        try
        {
            foreach (var e in result.Events)
            {
                state = Reducer.Apply(state, e);
                published.Add(new EventPublished(_log.Count + published.Count + 1, e, StateHash.Compute(state)));
            }
            if (StateHash.Compute(state) != StateHash.Compute(result.State!))
            {
                Fault(now);
                return false;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Fault(now);
            return false;
        }

        _state = state;
        _log.AddRange(published);
        // The record is handed over before anyone is told the match ended, so a client that saw the end and then asks
        // the writer to drain always finds the record queued.
        if (_state.Phase == Phase.Ended)
        {
            Status = MatchStatus.Finished;
            _endedAt = now;
            var outcome = _state.Outcome!;
            Record(new RecordResult(RecordKind.Completed, _log.Count, _log[^1].Hash, outcome.Winner, outcome.Reason.ToString(), null), now);
        }
        foreach (var p in published)
        {
            Broadcast(p);
        }
        return true;
    }

    private void Append(GameEvent e)
    {
        _state = Reducer.Apply(_state, e);
        _log.Add(new EventPublished(_log.Count + 1, e, StateHash.Compute(_state)));
    }

    private void Broadcast(ServerMessage message)
    {
        foreach (var slot in _slots)
        {
            if (slot.Connection is { } connection)
            {
                _outbox.Send(connection, message);
            }
        }
    }

    private void Fault(DateTimeOffset now) => Abort(AbortReason.Faulted, null, now);

    private void Abort(AbortReason reason, Seat? seat, DateTimeOffset now)
    {
        Status = MatchStatus.Aborted;
        AbortedBecause = reason;
        AbortedSeat = seat;
        _endedAt = now;
        foreach (var slot in _slots)
        {
            slot.AwayDeadline = null;
        }
        if (_started)
        {
            Record(new RecordResult(RecordKind.Aborted, _log.Count, _log[^1].Hash, null, reason.ToString(), seat), now);
        }
        Broadcast(new MatchAborted(reason, seat, _log.Count, _log[^1].Hash));
    }

    // Hands the closed match to the recorder; the file work happens off the lock.
    private void Record(RecordResult result, DateTimeOffset now)
    {
        var events = _log.Select(p => new RecordedEvent(p.Seq, p.Event, p.Hash)).ToList();
        _recorder.MatchClosed(new MatchRecordBody(MatchId, Code, CreatedAt, now, RecordFormat.CurrentEngine(RecordStore.Build),
            Scenario, RecordFormat.ContentFingerprint(_setup), events, result));
    }

    private void Refuse(ConnectionId to, SubmitCommand submit, SeatSlot slot, ProtocolError error, string text) =>
        _outbox.Send(to, new Rejected(submit.CommandId, submit.Seq, RejectionKind.Protocol, error.ToString(), text, null,
            _log.Count, slot.NextSeq, Duplicate: false));

    private static ServerMessage AsDuplicate(ServerMessage answer) => answer switch
    {
        Accepted a => a with { Duplicate = true },
        Rejected r => r with { Duplicate = true },
        _ => answer,
    };

    private static string Fingerprint(SubmitCommand submit) =>
        submit.Seq + ":" + System.Text.Json.JsonSerializer.Serialize(submit.Command, Wire.Options);

    private static byte[] TokenHash(string token) => SHA256.HashData(Encoding.ASCII.GetBytes(token));

    private sealed class SeatSlot
    {
        private readonly Queue<string> _order = new();
        private readonly List<byte[]> _issued = [];
        private byte[]? _current;
        private (ConnectionId Connection, byte[] Hash)? _lastSent;

        public ConnectionId? Connection { get; set; }
        public bool Taken { get; set; }
        public long NextSeq { get; set; } = 1;
        public ServerMessage? LastAnswer { get; private set; }
        public DateTimeOffset? AwayDeadline { get; set; }
        public Dictionary<string, (string Fingerprint, ServerMessage Result)> Answers { get; } = new(StringComparer.Ordinal);

        public void Remember(string commandId, string fingerprint, ServerMessage result)
        {
            Answers[commandId] = (fingerprint, result);
            LastAnswer = result;
            _order.Enqueue(commandId);
            if (_order.Count > RememberedAnswers)
            {
                Answers.Remove(_order.Dequeue());
            }
        }

        // A new token, valid alongside the older ones until one is confirmed. Only its hash is kept.
        public string Issue(ConnectionId to)
        {
            var token = RecordFormat.Base64Url(RandomNumberGenerator.GetBytes(32));
            var hash = TokenHash(token);
            _issued.Add(hash);
            if (_issued.Count > MaxUnconfirmedTokens)
            {
                _issued.RemoveAt(0);
            }
            _lastSent = (to, hash);
            return token;
        }

        public bool Holds(byte[] hash)
        {
            var found = _current is not null && CryptographicOperations.FixedTimeEquals(_current, hash);
            foreach (var issued in _issued)
            {
                found |= CryptographicOperations.FixedTimeEquals(issued, hash);
            }
            return found;
        }

        // Presenting an issued token proves it arrived: it becomes the only valid one.
        public void PromoteIfIssued(byte[] hash)
        {
            if (_issued.Any(h => CryptographicOperations.FixedTimeEquals(h, hash)))
            {
                _current = hash;
                _issued.Clear();
            }
        }

        // TokenSaved from the connection the last token went to. A repeat after confirming is harmless.
        public bool Confirm(ConnectionId from)
        {
            if (_lastSent is not { } sent || sent.Connection != from)
            {
                return false;
            }
            if (_current is not null && CryptographicOperations.FixedTimeEquals(_current, sent.Hash))
            {
                return true;
            }
            if (!_issued.Any(h => CryptographicOperations.FixedTimeEquals(h, sent.Hash)))
            {
                return false;
            }
            _current = sent.Hash;
            _issued.Clear();
            return true;
        }
    }
}
