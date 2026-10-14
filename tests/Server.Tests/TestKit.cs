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

    public Harness(int seed = 1, Func<IDiceSource>? dice = null, int maxMatches = 64)
    {
        Lobby = new Lobby(
            Scenarios.All,
            dice ?? (() => Dice = new SeededDice(seed)),
            new Outbox((to, message) => Clients[to].Inbox.Add(message)),
            () => $"M{++_codes}",
            maxMatches);
    }

    private int _codes;

    public Lobby Lobby { get; }
    public SeededDice? Dice { get; private set; }
    public Dictionary<ConnectionId, TestClient> Clients { get; } = [];

    public TestClient Connect()
    {
        var client = new TestClient(this, new ConnectionId(++_next));
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

internal sealed class TestClient(Harness harness, ConnectionId id)
{
    private int _read;
    private long _seq;
    private int _ids;

    public ConnectionId Id { get; } = id;
    public List<ServerMessage> Inbox { get; } = [];
    public MatchMirror Mirror { get; } = new();
    public Welcome? Welcome { get; private set; }
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
        var submit = new SubmitCommand($"c{++_ids}", ++_seq, Mirror.Events, command);
        Send(submit);
        var answer = LastAnswer!;
        if (answer is Rejected { Kind: RejectionKind.Protocol })
        {
            _seq--;
        }
        return answer;
    }

    public long Seq => _seq;

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
                    Welcome = w;
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
