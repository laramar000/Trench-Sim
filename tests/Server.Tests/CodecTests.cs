using System.Text;
using System.Text.Json;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

public class CodecTests
{
    public static TheoryData<string> Contents => new() { "ranged", "melee" };

    private static string Path(string content) => content == "ranged" ? Content.DefaultPath() : Content.MeleePath();

    // Every event of real matches survives the wire unchanged: same JSON when written again, and the same state hashes
    // when the read-back log is replayed.
    [Theory]
    [MemberData(nameof(Contents))]
    public void EverySimulatedEventRoundTrips(string content)
    {
        var warbands = Content.Load(Path(content));
        var seen = new HashSet<string>();
        foreach (var seed in Enumerable.Range(1, 40))
        {
            var log = Runner.Run(seed, warbands).Log;
            var back = new List<GameEvent>();
            var state = MatchState.None;
            for (var i = 0; i < log.Count; i++)
            {
                state = Reducer.Apply(state, log[i]);
                var sent = new EventPublished(i + 1, log[i], StateHash.Compute(state));
                var bytes = Codec.Encode(sent);
                var read = Assert.IsType<EventPublished>(Codec.ParseServer(bytes, out var failure));
                Assert.Null(failure);
                Assert.Equal(Encoding.UTF8.GetString(bytes), Encoding.UTF8.GetString(Codec.Encode(read)));
                back.Add(read.Event);
                seen.Add(log[i].GetType().Name);
            }
            Assert.Equal(StateHash.Compute(Reducer.Replay(log)), StateHash.Compute(Reducer.Replay(back)));
        }
        Assert.Contains("MatchCreated", seen);
        Assert.Contains("InjuryRolled", seen);
    }

    // Every command the bot proposes in real matches survives the wire unchanged.
    [Theory]
    [MemberData(nameof(Contents))]
    public void EveryProposedCommandRoundTrips(string content)
    {
        var warbands = Content.Load(Path(content));
        var types = new HashSet<string>();
        foreach (var seed in Enumerable.Range(1, 10))
        {
            var log = Runner.Run(seed, warbands).Log;
            var bot = new Bot(new Random(seed));
            var state = MatchState.None;
            foreach (var e in log)
            {
                state = Reducer.Apply(state, e);
                // Only states where someone must act are meaningful to the bot; the others it may refuse to read.
                List<Command> proposed;
                try
                {
                    proposed = bot.Propose(state).Take(20).ToList();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                foreach (var command in proposed)
                {
                    if (command is StartMatch)
                    {
                        continue;
                    }
                    var submit = new SubmitCommand("id", 1, 0, command);
                    var bytes = Codec.Encode(submit);
                    var read = Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out _));
                    Assert.Equal(Encoding.UTF8.GetString(bytes), Encoding.UTF8.GetString(Codec.Encode(read)));
                    types.Add(command.GetType().Name);
                }
            }
        }
        Assert.Contains("MoveModel", types);
        Assert.Contains("PlaceModel", types);
        Assert.Contains("ChargeModel", types.Union(content == "melee" ? [] : ["ChargeModel"]));
    }

    // Every concrete command and event type can be written and read: a new type cannot silently fail on the wire.
    [Fact]
    public void EveryCommandAndEventTypeHasAWireForm()
    {
        var types = typeof(Command).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(Command)) || t.IsSubclassOf(typeof(GameEvent))));
        foreach (var type in types)
        {
            Assert.NotNull(Wire.Options.GetConverter(type));
        }
    }

    [Fact]
    public void AMessageLooksLikeThis()
    {
        var submit = new SubmitCommand("c1", 1, 7, new DeployModel(Seat.One, new ModelId("a1")));
        Assert.Equal(
            """{"v":1,"type":"SubmitCommand","commandId":"c1","seq":1,"knownEvents":7,"command":{"type":"DeployModel","seat":"One","model":"a1"}}""",
            Encoding.UTF8.GetString(Codec.Encode(submit)));
    }

    private const string Move =
        """{"v":1,"type":"SubmitCommand","commandId":"c1","seq":1,"knownEvents":0,"command":{"type":"MoveModel","seat":"One","model":"a1","path":[{"x":1000,"y":2000}]}}""";

    public static TheoryData<string, ProtocolError> BadMessages => new()
    {
        // Not JSON, or not a whole message.
        { "", ProtocolError.Malformed },
        { "hello", ProtocolError.Malformed },
        { "null", ProtocolError.Malformed },
        { "[]", ProtocolError.Malformed },
        { "{}", ProtocolError.Malformed },
        { Move[..^1], ProtocolError.Malformed },
        { Move[..40], ProtocolError.Malformed },
        { Move + "{}", ProtocolError.Malformed },
        { Move + " x", ProtocolError.Malformed },
        { Move.Replace("\"seq\":1,", "\"seq\":1, /* c */"), ProtocolError.Malformed },
        { Move.Replace("}]}}", "}],}}"), ProtocolError.Malformed },

        // Version and type.
        { Move.Replace("\"v\":1", "\"v\":2"), ProtocolError.UnsupportedVersion },
        { Move.Replace("\"v\":1", "\"v\":\"1\""), ProtocolError.UnsupportedVersion },
        { Move.Replace("{\"v\":1,", "{"), ProtocolError.Malformed },
        { Move.Replace("\"v\":1,\"type\":\"SubmitCommand\"", "\"type\":\"SubmitCommand\",\"v\":1"), ProtocolError.Malformed },
        { Move.Replace("SubmitCommand", "SubmitOrder"), ProtocolError.UnknownType },
        { Move.Replace("MoveModel", "TeleportModel"), ProtocolError.UnknownType },
        { Move.Replace("\"type\":\"MoveModel\",", ""), ProtocolError.Malformed },

        // A client cannot send server messages, events or outcomes.
        { """{"v":1,"type":"EventPublished","seq":1,"hash":"x","event":{"type":"TurnEnded","turn":1}}""", ProtocolError.UnknownType },
        { Move.Replace("MoveModel", "ModelMoved"), ProtocolError.UnknownType },
        { Move.Replace("\"seat\":\"One\",", "\"seat\":\"One\",\"dice\":[6,6],"), ProtocolError.Malformed },
        { Move.Replace("\"knownEvents\":0,", "\"knownEvents\":0,\"hash\":\"abc\","), ProtocolError.Malformed },
        { Move.Replace("\"knownEvents\":0,", "\"knownEvents\":0,\"events\":[],"), ProtocolError.Malformed },

        // Fields: missing, repeated, wrong kind.
        { Move.Replace("\"commandId\":\"c1\",", ""), ProtocolError.Malformed },
        { Move.Replace("\"model\":\"a1\",", ""), ProtocolError.Malformed },
        { Move.Replace("\"seq\":1,", "\"seq\":1,\"seq\":2,"), ProtocolError.Malformed },
        { Move.Replace("\"seq\":1", "\"seq\":\"1\""), ProtocolError.Malformed },
        { Move.Replace("\"seq\":1", "\"seq\":1.5"), ProtocolError.Malformed },
        { Move.Replace("\"seq\":1", "\"seq\":99999999999999999999999"), ProtocolError.Malformed },
        { Move.Replace("\"commandId\":\"c1\"", "\"commandId\":null"), ProtocolError.Malformed },
        { Move.Replace("\"path\":[{\"x\":1000,\"y\":2000}]", "\"path\":null"), ProtocolError.Malformed },
        { Move.Replace("\"x\":1000", "\"x\":1000.5"), ProtocolError.Malformed },

        // Enums are names only.
        { Move.Replace("\"seat\":\"One\"", "\"seat\":0"), ProtocolError.Malformed },
        { Move.Replace("\"seat\":\"One\"", "\"seat\":\"one\""), ProtocolError.InvalidValue },
        { Move.Replace("\"seat\":\"One\"", "\"seat\":\"Three\""), ProtocolError.InvalidValue },
        { Move.Replace("\"seat\":\"One\"", "\"seat\":\"One, Two\""), ProtocolError.InvalidValue },

        // Values out of range.
        { Move.Replace("\"x\":1000", "\"x\":200001"), ProtocolError.InvalidValue },
        { Move.Replace("\"y\":2000", "\"y\":-9000000000"), ProtocolError.InvalidValue },
        { Move.Replace("\"seq\":1", "\"seq\":0"), ProtocolError.InvalidValue },
        { Move.Replace("\"knownEvents\":0", "\"knownEvents\":-1"), ProtocolError.InvalidValue },
        { Move.Replace("\"commandId\":\"c1\"", "\"commandId\":\"\""), ProtocolError.InvalidValue },
        { Move.Replace("\"commandId\":\"c1\"", "\"commandId\":\"c 1\""), ProtocolError.InvalidValue },
        { Move.Replace("\"commandId\":\"c1\"", $"\"commandId\":\"{new string('c', 65)}\""), ProtocolError.InvalidValue },
        { Move.Replace("\"model\":\"a1\"", "\"model\":\"\""), ProtocolError.InvalidValue },
        { """{"v":1,"type":"JoinMatch","code":"é"}""", ProtocolError.InvalidValue },
        { """{"v":1,"type":"SubmitCommand","commandId":"c1","seq":1,"knownEvents":0,"command":{"type":"SpendMarkers","seat":"One","count":2147483648}}""", ProtocolError.Malformed },
    };

    [Theory]
    [MemberData(nameof(BadMessages))]
    public void ABadMessageIsACleanFailure(string json, ProtocolError expected)
    {
        Assert.Null(Codec.ParseClient(Encoding.UTF8.GetBytes(json), out var failure));
        Assert.Equal(expected, failure!.Error);
    }

    [Fact]
    public void TheValidMessageUsedAboveParses()
    {
        Assert.IsType<SubmitCommand>(Codec.ParseClient(Encoding.UTF8.GetBytes(Move), out var failure));
        Assert.Null(failure);
    }

    [Fact]
    public void AnOversizedMessageIsRefusedBeforeParsing()
    {
        var points = string.Join(",", Enumerable.Repeat("{\"x\":1000,\"y\":2000}", 1000));
        var json = Move.Replace("[{\"x\":1000,\"y\":2000}]", $"[{points}]");
        Assert.True(json.Length > ProtocolLimits.MaxClientMessageBytes);
        Assert.Null(Codec.ParseClient(Encoding.UTF8.GetBytes(json), out var failure));
        Assert.Equal(ProtocolError.TooLarge, failure!.Error);
    }

    [Fact]
    public void DeepNestingIsRefused()
    {
        var json = Move.Replace("\"knownEvents\":0,", "\"knownEvents\":0,\"x\":" + new string('[', 100) + new string(']', 100) + ",");
        Assert.Null(Codec.ParseClient(Encoding.UTF8.GetBytes(json), out var failure));
        Assert.Equal(ProtocolError.Malformed, failure!.Error);
    }

    [Fact]
    public void InvalidUtf8IsRefused()
    {
        var bytes = Encoding.UTF8.GetBytes(Move).ToList();
        bytes.InsertRange(Move.IndexOf("c1", StringComparison.Ordinal), new byte[] { 0xC3, 0x28 });
        Assert.Null(Codec.ParseClient(bytes.ToArray(), out var failure));
        Assert.Equal(ProtocolError.Malformed, failure!.Error);
    }

    // Random damage to valid messages never escapes as an exception.
    [Fact]
    public void MangledMessagesNeverThrow()
    {
        var random = new Random(5);
        var samples = new[]
        {
            Move,
            """{"v":1,"type":"CreateMatch","scenario":"ranged"}""",
            """{"v":1,"type":"JoinMatch","code":"ABCD"}""",
            Encoding.UTF8.GetString(Codec.Encode(new SubmitCommand("x", 3, 9, new ChooseFirstActivation(Seat.Two, Seat.One)))),
        };
        for (var i = 0; i < 20000; i++)
        {
            var bytes = Encoding.UTF8.GetBytes(samples[random.Next(samples.Length)]);
            var damage = random.Next(1, 4);
            for (var d = 0; d < damage; d++)
            {
                switch (random.Next(3))
                {
                    case 0:
                        bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                        break;
                    case 1:
                        bytes = bytes[..random.Next(bytes.Length)];
                        break;
                    default:
                        const string Tokens = "{}[]\",:0123456789-eE.ntfu\\";
                        bytes[random.Next(bytes.Length)] = (byte)Tokens[random.Next(Tokens.Length)];
                        break;
                }
                if (bytes.Length == 0)
                {
                    break;
                }
            }
            var message = Codec.ParseClient(bytes, out var failure);
            Assert.True(message is not null ^ failure is not null);
        }
    }

    // A placement outside the table is refused by the wire before the rules ever see it.
    [Fact]
    public void TheWireUsesTheRulesConstructors()
    {
        var json = """{"v":1,"type":"SubmitCommand","commandId":"c1","seq":1,"knownEvents":0,"command":{"type":"PlaceModel","seat":"One","model":"a1","at":{"x":300000,"y":0}}}""";
        Assert.Null(Codec.ParseClient(Encoding.UTF8.GetBytes(json), out var failure));
        Assert.Equal(ProtocolError.InvalidValue, failure!.Error);
        _ = JsonSerializer.Serialize(0);
    }
}
