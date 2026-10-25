using System.Security.Cryptography;
using Match.Protocol;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Trench Warfare's secret duds on the wire (p. 162, U93): the defender's connection is told the duds, the attacker's is
// sent MinesLaid without them, on the live feed and in a rejoin's backlog alike; both copies of the match keep the
// server's hashes, and the record keeps the duds so it verifies.
public class MineWireTests
{
    private const string Name = "ranged-2v2" + ScenarioSetups.TrenchWarfareSuffix;

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static MinesLaid LaidIn(TestClient client) =>
        client.Inbox.OfType<EventPublished>().Select(p => p.Event).OfType<MinesLaid>().Single();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OnlyTheDefendersConnectionIsToldTheDuds(int seed)
    {
        var harness = new Harness(seed);
        var (one, two) = harness.Pair(Name);
        Play.ToEnd(one, two, seed);
        Assert.Equal(Phase.Ended, one.Mirror.State.Phase);

        var attacker = one.Mirror.State.Attacker!.Value;
        var (attacking, defending) = attacker == Seat.One ? (one, two) : (two, one);
        Assert.Empty(LaidIn(attacking).Duds);
        Assert.Equal(4, LaidIn(defending).Duds.Length);
        Assert.False(attacking.Mirror.State.DudsKnown);
        Assert.True(defending.Mirror.State.DudsKnown);
        // Both copies followed every event with the server's hash (MatchMirror checks each one).
        Assert.Equal(attacking.Mirror.Hash, defending.Mirror.Hash);

        // The server's own log keeps the duds.
        var log = harness.Lobby.LogOf(one.Welcome!.Code)!;
        Assert.Equal(LaidIn(defending).Duds, log.Select(p => p.Event).OfType<MinesLaid>().Single().Duds);
    }

    [Fact]
    public void ARejoiningAttackerIsStillNotToldTheDuds()
    {
        var time = new ManualTime();
        var harness = new Harness(5, rejoinWindow: TimeSpan.FromMinutes(5), time: time);
        var (one, two) = harness.Pair(Name);
        Play.Commands(one, two, 5, 3);
        Assert.True(one.Mirror.State.MinesLaid);
        var attacker = one.Mirror.State.Attacker!.Value;
        var (attacking, defending) = attacker == Seat.One ? (one, two) : (two, one);
        var code = attacking.Welcome!.Code;
        var token = attacking.Token!;
        harness.Lobby.Disconnect(attacking.Id);

        // A fresh connection with no copy of the match gets the whole backlog again.
        var back = harness.Connect();
        back.Rejoin(code, token);
        Assert.Equal(0, back.Welcome!.ReplayFrom);
        Assert.Empty(LaidIn(back).Duds);
        Assert.False(back.Mirror.State.DudsKnown);
        Assert.Equal(defending.Mirror.Hash, back.Mirror.Hash);
    }

    [Fact]
    public void ATrenchWarfareRecordWithMinesVerifies()
    {
        var body = TestRecords.Completed(Name, 2);
        var laid = body.Events.Select(e => e.Event).OfType<MinesLaid>().Single();
        Assert.Equal(4, laid.Duds.Length);
        var verdict = RecordVerifier.Verify(RecordFormat.Sign(body, Key), [new TrustedKey(RecordKeys.KeyIdOf(Key), Key)]);
        Assert.Equal(VerdictKind.Completed, verdict.Kind);
    }

    [Fact]
    public void TheSecretCopyTravelsWithAnEmptyDudList()
    {
        var harness = new Harness(1);
        var (one, two) = harness.Pair(Name);
        Play.Commands(one, two, 1, 3);
        var laid = harness.Lobby.LogOf(one.Welcome!.Code)!.Select(p => p.Event).OfType<MinesLaid>().Single();
        var bytes = Codec.Encode(new EventPublished(1, laid.Secret(), "h"));
        Assert.Contains("\"duds\":[]", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(bytes, out _));
        Assert.Empty(Assert.IsType<MinesLaid>(read.Event).Duds);
    }
}
