using System.Security.Cryptography;
using Match.Protocol;
using Rules.Core.Dice;
using Rules.Core.State;

namespace Match.Server;

public readonly record struct ConnectionId(long Value);

// Where the Lobby sends messages. Called under the Lobby's lock, in order, so it must only enqueue (never block or call
// back into the Lobby). The transport gives each connection one ordered outbound queue.
public sealed class Outbox(Action<ConnectionId, ServerMessage> send)
{
    public void Send(ConnectionId to, ServerMessage message) => send(to, message);
}

// The server's matches and which connection holds which seat. Transport-free: the transport passes in each received
// message and each disconnect, and delivers what the Lobby sends through the Outbox. One lock serialises everything, so
// every match sees its commands one at a time and its events go out in log order.
public sealed class Lobby
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<string, MatchSetup> _scenarios;
    private readonly Func<IDiceSource> _newDice;
    private readonly Func<string> _newCode;
    private readonly Outbox _outbox;
    private readonly int _maxMatches;
    private readonly Dictionary<string, MatchHost> _matches = new(StringComparer.Ordinal);
    private readonly Dictionary<ConnectionId, (MatchHost Host, Seat Seat)> _seated = [];
    private bool _shutDown;

    public Lobby(
        IReadOnlyDictionary<string, MatchSetup> scenarios,
        Func<IDiceSource> newDice,
        Outbox outbox,
        Func<string>? newCode = null,
        int maxMatches = 64)
    {
        _scenarios = scenarios;
        _newDice = newDice;
        _outbox = outbox;
        _newCode = newCode ?? RandomCode;
        _maxMatches = maxMatches;
    }

    public int MatchCount
    {
        get
        {
            lock (_gate)
            {
                return _matches.Count;
            }
        }
    }

    // Handles one message from a connection. Never throws for anything the client sent.
    public void Receive(ConnectionId from, ReadOnlySpan<byte> utf8)
    {
        // Parsing needs no shared state; it happens outside the lock.
        var message = Codec.ParseClient(utf8, out var failure);
        lock (_gate)
        {
            if (_shutDown)
            {
                Refuse(from, ProtocolError.ShuttingDown, "The server is shutting down.");
                return;
            }
            if (message is null)
            {
                Refuse(from, failure!.Error, failure.Text);
                return;
            }
            switch (message)
            {
                case CreateMatch create:
                    Create(from, create);
                    break;
                case JoinMatch join:
                    Join(from, join);
                    break;
                case SubmitCommand submit:
                    if (!_seated.TryGetValue(from, out var seated))
                    {
                        Refuse(from, ProtocolError.NotJoined, "Create or join a match first.", submit);
                        return;
                    }
                    seated.Host.Submit(seated.Seat, from, submit);
                    break;
            }
        }
    }

    // A message the transport could not hand over (too large, binary, a timeout): answered in order with the rest.
    public void RefuseUnread(ConnectionId to, ProtocolError error, string text)
    {
        lock (_gate)
        {
            Refuse(to, error, text);
        }
    }

    public bool IsSeated(ConnectionId connection)
    {
        lock (_gate)
        {
            return _seated.ContainsKey(connection);
        }
    }

    public void Disconnect(ConnectionId connection)
    {
        lock (_gate)
        {
            if (!_seated.Remove(connection, out var seated))
            {
                return;
            }
            seated.Host.Disconnect(seated.Seat);
            if (!seated.Host.HasConnections)
            {
                _matches.Remove(seated.Host.Code);
            }
        }
    }

    // Tells every seated connection, and refuses everything from now on. The transport then closes the connections.
    public void Shutdown()
    {
        lock (_gate)
        {
            _shutDown = true;
            foreach (var host in _matches.Values)
            {
                host.Shutdown();
            }
        }
    }

    // For tests and diagnostics: a copy of a match's log, or null.
    public IReadOnlyList<EventPublished>? LogOf(string code)
    {
        lock (_gate)
        {
            return _matches.TryGetValue(code, out var host) ? host.Log.ToArray() : null;
        }
    }

    public MatchStatus? StatusOf(string code)
    {
        lock (_gate)
        {
            return _matches.TryGetValue(code, out var host) ? host.Status : null;
        }
    }

    private void Create(ConnectionId from, CreateMatch create)
    {
        if (_seated.ContainsKey(from))
        {
            Refuse(from, ProtocolError.AlreadyJoined, "This connection already has a seat.");
            return;
        }
        if (!_scenarios.TryGetValue(create.Scenario, out var setup))
        {
            Refuse(from, ProtocolError.UnknownScenario, "No such scenario on this server.");
            return;
        }
        if (_matches.Count >= _maxMatches)
        {
            Refuse(from, ProtocolError.ServerBusy, "The server has no room for another match.");
            return;
        }
        string code;
        do
        {
            code = _newCode();
        }
        while (_matches.ContainsKey(code));

        var host = new MatchHost(code, create.Scenario, setup, _newDice(), _outbox);
        _matches.Add(code, host);
        _seated.Add(from, (host, Seat.One));
        host.Seat(Seat.One, from);
    }

    private void Join(ConnectionId from, JoinMatch join)
    {
        if (_seated.ContainsKey(from))
        {
            Refuse(from, ProtocolError.AlreadyJoined, "This connection already has a seat.");
            return;
        }
        if (!_matches.TryGetValue(join.Code, out var host))
        {
            Refuse(from, ProtocolError.UnknownMatch, "No match has this code.");
            return;
        }
        if (host.Status != MatchStatus.Waiting || !host.IsFree(Seat.Two))
        {
            Refuse(from, ProtocolError.MatchFull, "This match already has two players.");
            return;
        }
        _seated.Add(from, (host, Seat.Two));
        host.Seat(Seat.Two, from);
    }

    private void Refuse(ConnectionId to, ProtocolError error, string text, SubmitCommand? submit = null)
    {
        var seated = _seated.TryGetValue(to, out var s) ? s : default;
        _outbox.Send(to, new Rejected(
            submit?.CommandId,
            submit?.Seq,
            RejectionKind.Protocol,
            error.ToString(),
            text,
            RuleRef: null,
            Events: seated.Host?.Log.Count ?? 0,
            NextSeq: seated.Host?.NextSeq(seated.Seat) ?? 1,
            Duplicate: false));
    }

    private static string RandomCode() => RandomNumberGenerator.GetString(CodeAlphabet, 8);
}
