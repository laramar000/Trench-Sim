using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Match.Server;

public enum MatchStatus
{
    // Created, waiting for the second player.
    Waiting,
    Playing,

    // No more commands: a player left, the server faulted or is shutting down. A match that reached its end stays
    // Playing; the rules refuse further commands themselves.
    Closed,
}

// One match: the canonical state and event log, the server's dice, and both seats. Not thread-safe; the Lobby calls it
// under its lock. Every outcome is produced here, through TurnSequence.Decide with the server's dice.
internal sealed class MatchHost
{
    // How many answers per seat are kept for resends. An older resend is refused as out of order.
    public const int RememberedAnswers = 256;

    private readonly IDiceSource _dice;
    private readonly Outbox _outbox;
    private readonly SeatSlot[] _slots = [new(), new()];
    private readonly List<EventPublished> _log = [];
    private MatchState _state = MatchState.None;

    public MatchHost(string code, string scenario, MatchSetup setup, IDiceSource dice, Outbox outbox)
    {
        Code = code;
        Scenario = scenario;
        _dice = dice;
        _outbox = outbox;
        Append(new MatchCreated(setup));
    }

    public string Code { get; }
    public string Scenario { get; }
    public MatchStatus Status { get; private set; } = MatchStatus.Waiting;
    public MatchState State => _state;
    public IReadOnlyList<EventPublished> Log => _log;

    public bool HasConnections => _slots.Any(s => s.Connection is not null);

    // Seats a connection: Welcome, then every event so far. The second seat starts the match.
    public void Seat(Seat seat, ConnectionId connection)
    {
        var slot = Slot(seat);
        slot.Connection = connection;
        slot.Taken = true;
        _outbox.Send(connection, new Welcome(Code, seat, Scenario));
        foreach (var published in _log)
        {
            _outbox.Send(connection, published);
        }

        if (seat == Rules.Core.State.Seat.Two)
        {
            Status = MatchStatus.Playing;
            if (Slot(Rules.Core.State.Seat.One).Connection is { } first)
            {
                _outbox.Send(first, new Notice(NoticeKind.OpponentJoined));
            }
            // The server, not a player, starts the match.
            var result = Decide(new StartMatch());
            if (result is null || !result.IsAccepted)
            {
                Fault();
                return;
            }
            Commit(result);
        }
    }

    public bool IsFree(Seat seat) => !Slot(seat).Taken;

    public void Submit(Seat seat, ConnectionId from, SubmitCommand submit)
    {
        var slot = Slot(seat);
        if (Status == MatchStatus.Waiting)
        {
            Refuse(from, submit, slot, ProtocolError.MatchNotStarted, "The match starts when the second player joins.");
            return;
        }
        if (Status == MatchStatus.Closed)
        {
            Refuse(from, submit, slot, ProtocolError.MatchClosed, "This match is closed.");
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

        // A resend of a command already answered gets the same answer, and nothing is applied again.
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
            Fault();
            return;
        }

        ServerMessage reply;
        if (result.IsAccepted)
        {
            var first = _log.Count + 1;
            if (!Commit(result))
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

    public void Disconnect(Seat seat)
    {
        var slot = Slot(seat);
        slot.Connection = null;
        if (Status == MatchStatus.Closed)
        {
            return;
        }
        // M5 has no rejoin: the match stops where it is, with no result.
        Status = MatchStatus.Closed;
        if (Slot(seat.Opponent()).Connection is { } other)
        {
            _outbox.Send(other, new Notice(NoticeKind.OpponentDisconnected));
        }
    }

    public void Shutdown()
    {
        Status = MatchStatus.Closed;
        foreach (var slot in _slots)
        {
            if (slot.Connection is { } connection)
            {
                _outbox.Send(connection, new Notice(NoticeKind.ServerShutdown));
            }
        }
    }

    public long NextSeq(Seat seat) => Slot(seat).NextSeq;

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
    private bool Commit(DecideResult result)
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
                Fault();
                return false;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Fault();
            return false;
        }

        _state = state;
        foreach (var p in published)
        {
            _log.Add(p);
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

    private void Fault()
    {
        Status = MatchStatus.Closed;
        Broadcast(new Notice(NoticeKind.MatchFaulted));
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

    private sealed class SeatSlot
    {
        private readonly Queue<string> _order = new();

        public ConnectionId? Connection { get; set; }
        public bool Taken { get; set; }
        public long NextSeq { get; set; } = 1;
        public Dictionary<string, (string Fingerprint, ServerMessage Result)> Answers { get; } = new(StringComparer.Ordinal);

        public void Remember(string commandId, string fingerprint, ServerMessage result)
        {
            Answers[commandId] = (fingerprint, result);
            _order.Enqueue(commandId);
            if (_order.Count > RememberedAnswers)
            {
                Answers.Remove(_order.Dequeue());
            }
        }
    }
}
