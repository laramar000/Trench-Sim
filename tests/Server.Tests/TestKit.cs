using System.Text;
using Match.Protocol;
using Match.Server;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

internal static class Scenarios
{
    public static readonly IReadOnlyDictionary<string, MatchSetup> All = ScenarioCatalog.FromContent();

    public static Seat? SeatOf(Command command) => CommandSeats.Of(command);
}

// A dice source that fails the test's expectations loudly if anything rolls.
internal sealed class ThrowingDice : IDiceSource
{
    public int RollD6() => throw new InvalidOperationException("No die should be rolled.");
}

// A lobby with recorded outboxes and test clients that mirror the match from the server's events.
internal sealed class Harness
{
    private long _next;

    // A zero rejoin window gives M5's behaviour (a disconnect aborts at once); the M6 tests pass a window and a clock.
    public Harness(int seed = 1, Func<IDiceSource>? dice = null, int maxMatches = 64, TimeSpan? rejoinWindow = null, TimeProvider? time = null, IMatchRecorder? recorder = null)
    {
        Lobby = new Lobby(
            Scenarios.All,
            dice ?? (() => Dice = new SeededDice(seed)),
            new Outbox((to, message) => Clients[to].Inbox.Add(message), to => Closed.Add(to)),
            () => $"M{++_codes}",
            maxMatches,
            time,
            rejoinWindow ?? TimeSpan.Zero,
            recorder);
    }

    // Connections the Lobby asked the transport to close.
    public List<ConnectionId> Closed { get; } = [];

    private int _codes;

    public Lobby Lobby { get; }
    public SeededDice? Dice { get; private set; }
    public Dictionary<ConnectionId, TestClient> Clients { get; } = [];

    // A new connection; it may keep a copy of the match from an earlier connection (a rejoin after a network drop).
    public TestClient Connect(MatchMirror? mirror = null)
    {
        var client = new TestClient(this, new ConnectionId(++_next), mirror ?? new MatchMirror());
        Clients.Add(client.Id, client);
        return client;
    }

    // Creates a match and joins it; returns (Seat One client, Seat Two client), both in sync.
    public (TestClient One, TestClient Two) Pair(string scenario = "ranged")
    {
        var one = Connect();
        one.Send(new CreateMatch(scenario));
        var code = one.Welcome!.Code;
        var two = Connect();
        two.Send(new JoinMatch(code));
        return (one, two);
    }
}

internal sealed class TestClient(Harness harness, ConnectionId id, MatchMirror mirror)
{
    private int _read;
    private long _seq;
    private int _ids;

    public ConnectionId Id { get; } = id;
    public List<ServerMessage> Inbox { get; } = [];
    public MatchMirror Mirror { get; } = mirror;
    public Welcome? Welcome { get; private set; }
    public List<Welcome> Welcomes { get; } = [];

    // MatchPaused and MatchResumed, in order.
    public List<ServerMessage> Pauses { get; } = [];

    // The newest token received; never null once a Welcome with a token arrived.
    public string? Token { get; private set; }

    public bool Synchronized => Welcome is { } w && Mirror.Events == w.Events && Mirror.Hash == w.Hash;
    public List<Notice> Notices { get; } = [];
    public MatchAborted? Aborted { get; private set; }
    public List<ServerMessage> Answers { get; } = [];

    public ServerMessage? LastAnswer => Answers.Count == 0 ? null : Answers[^1];

    public void SendRaw(string json) => SendBytes(Encoding.UTF8.GetBytes(json));

    public void SendBytes(byte[] bytes)
    {
        harness.Lobby.Receive(Id, bytes);
        harness.Clients.Values.ToList().ForEach(c => c.Pump());
    }

    public void Send(ClientMessage message) => SendBytes(Codec.Encode(message));

    // Sends a command with the next seq, a fresh id and the client's own event count.
    public ServerMessage Command(Command command)
    {
        var submit = new SubmitCommand($"k{Id.Value}-c{++_ids}", ++_seq, Mirror.Events, command);
        LastSubmit = submit;
        Send(submit);
        var answer = LastAnswer!;
        if (answer is Rejected { Kind: RejectionKind.Protocol })
        {
            _seq--;
        }
        return answer;
    }

    public long Seq => _seq;

    public SubmitCommand? LastSubmit { get; private set; }

    // Sends the same id, seq and command again, with the events applied now.
    public ServerMessage Resend(SubmitCommand original)
    {
        Send(new SubmitCommand(original.CommandId, original.Seq, Mirror.Events, original.Command));
        return LastAnswer!;
    }

    public ServerMessage Rejoin(string code, string token)
    {
        var count = Answers.Count;
        Send(new Rejoin(code, token, Mirror.Events, Mirror.Events == 0 ? null : Mirror.Hash));
        return Answers.Count > count ? Answers[^1] : Welcome!;
    }

    // Every message goes through the wire format, as it would over a socket.
    public void Pump()
    {
        for (; _read < Inbox.Count; _read++)
        {
            var bytes = Codec.Encode(Inbox[_read]);
            var message = Codec.ParseServer(bytes, out var failure) ?? throw new InvalidOperationException(failure!.Text);
            switch (message)
            {
                case Welcome w:
                    if (w.ReplayFrom == 0)
                    {
                        Mirror.Reset();
                    }
                    Assert.Equal(w.ReplayFrom, Mirror.Events);
                    Welcome = w;
                    Welcomes.Add(w);
                    Token = w.SeatToken ?? Token;
                    _seq = w.NextSeq - 1;
                    break;
                case MatchPaused or MatchResumed:
                    Pauses.Add(message);
                    break;
                case EventPublished e:
                    Mirror.Apply(e);
                    break;
                case Notice n:
                    Notices.Add(n);
                    break;
                case MatchAborted a:
                    Assert.Equal(Mirror.Events, a.Events);
                    Assert.Equal(Mirror.Hash, a.Hash);
                    Aborted = a;
                    break;
                default:
                    Answers.Add(message);
                    break;
            }
        }
    }
}

// A clock the test moves by hand. Timers still run on real time (the server's tick), but read this clock.
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public ManualTime()
        : this(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

// Keeps what the Lobby reports, in order.
internal sealed class CapturingRecorder : IMatchRecorder
{
    public List<OpenMatch> Started { get; } = [];
    public List<MatchRecordBody> Closed { get; } = [];

    public void MatchStarted(OpenMatch match)
    {
        lock (Started)
        {
            Started.Add(match);
        }
    }

    public void MatchClosed(MatchRecordBody record)
    {
        lock (Closed)
        {
            Closed.Add(record);
        }
    }
}

internal static class Play
{
    // Plays the match to its end: each command goes from the connection of the seat it is for.
    public static void ToEnd(TestClient one, TestClient two, int seed, int maxCommands = 5000) => Commands(one, two, seed, maxCommands);

    // Plays up to n accepted commands (or to the end).
    public static void Commands(TestClient one, TestClient two, int seed, int n)
    {
        var bot = new Bot(new Random(seed));
        for (var i = 0; i < n && one.Mirror.State.Phase != Phase.Ended; i++)
        {
            var accepted = false;
            foreach (var command in bot.Propose(one.Mirror.State))
            {
                var client = Scenarios.SeatOf(command) == Seat.One ? one : two;
                if (client.Command(command) is Accepted)
                {
                    accepted = true;
                    break;
                }
            }
            Assert.True(accepted, "the bot found no legal command");
        }
    }
}

// Records made by real matches through the Lobby, for the record tests.
internal static class TestRecords
{
    public static MatchRecordBody Completed(string scenario = "ranged", int seed = 1)
    {
        var recorder = new CapturingRecorder();
        var harness = new Harness(seed, recorder: recorder);
        var (one, two) = harness.Pair(scenario);
        Play.ToEnd(one, two, seed);
        return Assert.Single(recorder.Closed);
    }

    public static MatchRecordBody Aborted(string scenario = "ranged", int seed = 1, int commands = 10)
    {
        var recorder = new CapturingRecorder();
        var harness = new Harness(seed, recorder: recorder);
        var (one, two) = harness.Pair(scenario);
        Play.Commands(one, two, seed, commands);
        harness.Lobby.Disconnect(two.Id);
        return Assert.Single(recorder.Closed);
    }

    // A record body with other events or another result, keeping everything else.
    public static MatchRecordBody With(MatchRecordBody body, IEnumerable<RecordedEvent>? events = null, RecordResult? result = null,
        RecordEngine? engine = null, string? fingerprint = null) =>
        new(body.MatchId, body.Code, body.CreatedAt, body.ClosedAt, engine ?? body.Engine, body.Scenario,
            fingerprint ?? body.ContentFingerprint, events ?? body.Events, result ?? body.Result);

    // The result that matches a list of events, for a forger who re-signs consistently.
    public static RecordResult ResultFor(RecordResult result, IReadOnlyList<RecordedEvent> events) =>
        result with { Events = events.Count, Hash = events.Count == 0 ? null : events[^1].Hash };
}
