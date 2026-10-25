using System.Security.Cryptography;
using Match.Protocol;
using Rules.Core.Dice;
using Rules.Core.State;
using Sim;

namespace Match.Server;

public readonly record struct ConnectionId(long Value);

// Where the Lobby sends messages. Called under the Lobby's lock, in order, so it must only enqueue (never block or call
// back into the Lobby). The transport gives each connection one ordered outbound queue.
public sealed class Outbox(
    Action<ConnectionId, ServerMessage> send,
    Action<ConnectionId>? close = null,
    Action<ConnectionId, IReadOnlyList<ServerMessage>>? sendBacklog = null)
{
    public void Send(ConnectionId to, ServerMessage message) => send(to, message);

    // A match's backlog (bounded by the match's own length), queued in one go after a Welcome.
    public void SendBacklog(ConnectionId to, IReadOnlyList<ServerMessage> messages)
    {
        if (sendBacklog is not null)
        {
            sendBacklog(to, messages);
            return;
        }
        foreach (var message in messages)
        {
            send(to, message);
        }
    }

    // Closes a connection after what is queued for it (a policy close: a seat taken over, too many bad tokens).
    public void Close(ConnectionId connection) => close?.Invoke(connection);
}

// The server's matches and which connection holds which seat. Transport-free: the transport passes in each received
// message and each disconnect, and delivers what the Lobby sends through the Outbox. One lock serialises everything, so
// every match sees its commands one at a time, its events go out in log order, and a rejoin's backlog is queued in
// the same hold of the lock as the seat is attached.
public sealed class Lobby
{
    // A connection that offers this many bad seat tokens is closed.
    public const int MaxTokenFailures = 3;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<string, MatchSetup> _scenarios;
    private readonly IReadOnlyDictionary<string, MapDefinition> _maps;
    private readonly Dictionary<string, string> _mapIdByHash = new(StringComparer.Ordinal);
    private readonly Func<IDiceSource> _newDice;
    private readonly Func<string> _newCode;
    private readonly Func<int> _newMapSeed;
    private readonly Outbox _outbox;
    private readonly int _maxMatches;
    private readonly TimeProvider _time;
    private readonly TimeSpan _rejoinWindow;
    private readonly IMatchRecorder _recorder;
    private readonly Dictionary<string, MatchHost> _matches = new(StringComparer.Ordinal);
    private readonly Dictionary<ConnectionId, (MatchHost Host, Seat Seat)> _seated = [];
    private readonly Dictionary<ConnectionId, int> _tokenFailures = [];
    private bool _shutDown;

    public Lobby(
        IReadOnlyDictionary<string, MatchSetup> scenarios,
        Func<IDiceSource> newDice,
        Outbox outbox,
        Func<string>? newCode = null,
        int maxMatches = 64,
        TimeProvider? time = null,
        TimeSpan? rejoinWindow = null,
        IMatchRecorder? recorder = null,
        IReadOnlyDictionary<string, MapDefinition>? maps = null,
        Func<int>? newMapSeed = null)
    {
        _scenarios = scenarios;
        _maps = maps ?? new Dictionary<string, MapDefinition>(StringComparer.Ordinal);
        foreach (var map in _maps.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            _mapIdByHash.TryAdd(RecordFormat.MapHash(map.ToScenario()), map.Id);
        }
        _newDice = newDice;
        _outbox = outbox;
        _newCode = newCode ?? RandomCode;
        _maxMatches = maxMatches;
        _time = time ?? TimeProvider.System;
        _rejoinWindow = rejoinWindow ?? DefaultRejoinWindow;
        _recorder = recorder ?? NullRecorder.Instance;
        _newMapSeed = newMapSeed ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue));
    }

    public static TimeSpan DefaultRejoinWindow { get; } = TimeSpan.FromMinutes(5);

    public TimeSpan RejoinWindow => _rejoinWindow;

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
            var now = _time.GetUtcNow();
            switch (message)
            {
                case CreateMatch create:
                    Create(from, create, now);
                    break;
                case JoinMatch join:
                    Join(from, join, now);
                    break;
                case Rejoin rejoin:
                    RejoinSeat(from, rejoin, now);
                    break;
                case TokenSaved:
                    SaveToken(from);
                    break;
                case SubmitCommand submit:
                    if (!_seated.TryGetValue(from, out var seated))
                    {
                        Refuse(from, ProtocolError.NotJoined, "Create or join a match first.", submit);
                        return;
                    }
                    seated.Host.Submit(seated.Seat, from, submit, now);
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
            _tokenFailures.Remove(connection);
            if (!_seated.Remove(connection, out var seated))
            {
                return;
            }
            var now = _time.GetUtcNow();
            seated.Host.Disconnect(seated.Seat, now);
            RemoveIfDone(seated.Host, now);
        }
    }

    // Expires paused matches whose earliest rejoin deadline has passed, and drops matches nobody can come back to. The
    // transport calls it on a timer; tests call it after moving their clock.
    public void Tick()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var host in _matches.Values.ToList())
            {
                host.Tick(now);
                RemoveIfDone(host, now);
            }
        }
    }

    // Tells every seated connection, and refuses everything from now on. The transport then closes the connections.
    public void Shutdown()
    {
        lock (_gate)
        {
            _shutDown = true;
            var now = _time.GetUtcNow();
            foreach (var host in _matches.Values)
            {
                host.Shutdown(now);
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

    public ConnectionId? ConnectionOf(string code, Seat seat)
    {
        lock (_gate)
        {
            return _matches.TryGetValue(code, out var host) ? host.ConnectionOf(seat) : null;
        }
    }

    public DateTimeOffset? DeadlineOf(string code, Seat seat)
    {
        lock (_gate)
        {
            return _matches.TryGetValue(code, out var host) ? host.DeadlineOf(seat) : null;
        }
    }

    private void Create(ConnectionId from, CreateMatch create, DateTimeOffset now)
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
        var mapId = create.Map;
        // Trench-maps stage S9: "generated-<kind>" asks for a new map: the server picks the seed, and the record names
        // the map it made ("generated-<kind>-v1-<seed>").
        if (mapId is not null && MapGenerator.IsRequest(mapId, out var kind))
        {
            mapId = MapGenerator.IdOf(kind, _newMapSeed());
        }
        if (mapId is not null)
        {
            // Trench-maps stage S8: a generated id ("generated-trench-lines-v1-42") is made from its seed here.
            if (ScenarioSetups.MapById(_maps, mapId) is not { } map)
            {
                Refuse(from, ProtocolError.UnknownMap, "No such map on this server.");
                return;
            }
            try
            {
                setup = ScenarioSetups.OnMap(setup, map);
            }
            catch (MapFormatException e)
            {
                Refuse(from, ProtocolError.MapDoesNotFit, e.Message);
                return;
            }
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

        var host = new MatchHost(code, MatchIds.New(), create.Scenario, setup, _newDice(), _outbox, _recorder, _rejoinWindow, now)
        {
            Map = mapId ?? (setup.Scenario is { } played ? _mapIdByHash.GetValueOrDefault(RecordFormat.MapHash(played)) : null),
        };
        _matches.Add(code, host);
        _seated.Add(from, (host, Seat.One));
        host.Seat(Seat.One, from, now);
    }

    private void Join(ConnectionId from, JoinMatch join, DateTimeOffset now)
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
        host.Seat(Seat.Two, from, now);
    }

    // Takes a seat back with its token. A valid rejoin takes the seat from whatever connection holds it, which is told
    // and closed; the last valid rejoin wins, and a seat is never held by two connections.
    private void RejoinSeat(ConnectionId from, Rejoin rejoin, DateTimeOffset now)
    {
        if (_seated.ContainsKey(from))
        {
            Refuse(from, ProtocolError.AlreadyJoined, "This connection already has a seat.");
            return;
        }
        if (!_matches.TryGetValue(rejoin.Code, out var host))
        {
            Refuse(from, ProtocolError.UnknownMatch, "No match has this code.");
            return;
        }
        if (host.SeatOfToken(rejoin.SeatToken) is not { } seat)
        {
            var failures = _tokenFailures[from] = _tokenFailures.GetValueOrDefault(from) + 1;
            Refuse(from, ProtocolError.InvalidToken, "That seat token is not valid for this match.");
            if (failures >= MaxTokenFailures)
            {
                _outbox.Close(from);
            }
            return;
        }
        _tokenFailures.Remove(from);
        if (host.ConnectionOf(seat) is { } previous)
        {
            Refuse(previous, ProtocolError.SeatTakenOver, "Another connection took this seat back with its token.");
            _seated.Remove(previous);
            _outbox.Close(previous);
        }
        _seated.Add(from, (host, seat));
        host.Rejoin(seat, from, rejoin, now);
    }

    private void SaveToken(ConnectionId from)
    {
        if (!_seated.TryGetValue(from, out var seated))
        {
            Refuse(from, ProtocolError.NotJoined, "Create, join or rejoin a match first.");
            return;
        }
        if (seated.Host.IsOver)
        {
            Refuse(from, seated.Host.Status == MatchStatus.Aborted ? ProtocolError.MatchAborted : ProtocolError.MatchFinished,
                "This match is over; its seat tokens no longer change.");
            return;
        }
        if (!seated.Host.TokenSaved(seated.Seat, from))
        {
            Refuse(from, ProtocolError.InvalidToken, "No token was sent on this connection to confirm.");
        }
    }

    private void RemoveIfDone(MatchHost host, DateTimeOffset now)
    {
        if (host.CanRemove(now))
        {
            _matches.Remove(host.Code);
        }
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
