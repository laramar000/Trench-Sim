using System.Text;
using Match.Protocol;
using Match.Server;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Trench-maps stage S5: a scenario's rules apart from its map. A setup is a scenario definition, a map that fits it,
// and the models; CreateMatch may name another map, and the record says which map was played and its hash.
public class ScenarioMapTests
{
    private static IReadOnlyDictionary<string, MapDefinition> Maps => ScenarioCatalog.Maps;

    private static string Fingerprint(MatchSetup setup) => RecordFormat.ContentFingerprint(setup);

    [Fact]
    public void ClaimNoMansLandIsBuiltFromItsDefinitionExactlyAsBefore()
    {
        foreach (var (name, setup) in Scenarios.All.Where(s => s.Value.ScenarioId == ClaimNoMansLand.ScenarioId))
        {
            var coded = name.EndsWith(ScenarioSetups.TowersSuffix, StringComparison.Ordinal)
                ? ClaimNoMansLand.CreateTowersSetup(setup.Models)
                : ClaimNoMansLand.CreateSetup(setup.Models);
            Assert.Equal(Fingerprint(coded), Fingerprint(setup));
        }
        Assert.Equal(ClaimNoMansLand.ScenarioId, ClaimNoMansLand.Definition.Id);
        Assert.Equal([ClaimNoMansLand.West, ClaimNoMansLand.East], ClaimNoMansLand.Definition.ZoneIds.AsEnumerable());
    }

    [Fact]
    public void TheSameModelsOnAnotherMapAreThatMapsSetup()
    {
        Assert.Equal(Fingerprint(Scenarios.All["ranged-towers"]), Fingerprint(ScenarioSetups.OnMap(Scenarios.All["ranged"], Maps[ScenarioSetups.TowersMap])));
        Assert.Equal(Fingerprint(Scenarios.All["melee-2v2"]), Fingerprint(ScenarioSetups.OnMap(Scenarios.All["melee-2v2-towers"], Maps[ScenarioSetups.PlainMap])));
    }

    [Fact]
    public void AMapThatDoesNotFitTheScenarioIsRefused()
    {
        var plain = Maps[ScenarioSetups.PlainMap];
        var definition = ClaimNoMansLand.Definition;
        Assert.Empty(definition.Unmet(plain.ToScenario(), plain.ArchetypeKind));
        // Not made for the scenario.
        var other = plain with { Supports = ["trench-warfare"] };
        Assert.Contains("not made for claim-no-mans-land", Assert.Throws<MapFormatException>(() => ScenarioSetups.OnMap(Scenarios.All["ranged"], other)).Message);
        // Another archetype.
        Assert.Equal(["claim-no-mans-land is played on the NoMansLand archetype; the map is TrenchLines."],
            definition.Unmet(plain.ToScenario(), Archetype.TrenchLines).AsEnumerable());
        // Four Objectives, other zones.
        var four = new Scenario(plain.Battlefield, ["obj-1", "obj-2", "obj-3", "obj-4"]);
        Assert.Equal(["claim-no-mans-land needs 5 Objective pieces; the map has 4."], definition.Unmet(four, null).AsEnumerable());
        var zones = new Battlefield(plain.Battlefield.Width, plain.Battlefield.Height, plain.Battlefield.Terrain,
            [new DeploymentZone("north", Footprint.Of(new Rect(new Point2(Length.Zero, Length.FromInches(30)), new Point2(Length.FromInches(36), Length.FromInches(36))))),
             new DeploymentZone("south", Footprint.Of(new Rect(new Point2(Length.Zero, Length.Zero), new Point2(Length.FromInches(36), Length.FromInches(3)))))]);
        Assert.Equal(["claim-no-mans-land needs deployment zones east and west; the map has north and south."],
            definition.Unmet(new Scenario(zones, plain.ObjectiveIds), null).AsEnumerable());
        Assert.Throws<ArgumentException>(() => definition.Setup(four, null, Scenarios.All["ranged"].Models));
    }

    [Fact]
    public void CreateMatchLeavesOutAMapItDoesNotName()
    {
        Assert.Equal("{\"v\":2,\"type\":\"CreateMatch\",\"scenario\":\"ranged\"}", Encoding.UTF8.GetString(Codec.Encode(new CreateMatch("ranged"))));
        var named = new CreateMatch("ranged") { Map = ScenarioSetups.TowersMap };
        var json = Encoding.UTF8.GetString(Codec.Encode(named));
        Assert.Equal("{\"v\":2,\"type\":\"CreateMatch\",\"scenario\":\"ranged\",\"map\":\"claim-no-mans-land-towers\"}", json);
        Assert.Equal(named, Codec.ParseClient(Encoding.UTF8.GetBytes(json), out _));
        Assert.Throws<ArgumentException>(() => new CreateMatch("ranged") { Map = "bad map" });
    }

    [Fact]
    public void AMatchOnANamedMapPlaysThereAndTheRecordSaysSo()
    {
        var recorder = new CapturingRecorder();
        var harness = new Harness(recorder: recorder);
        var one = harness.Connect();
        one.Send(new CreateMatch("ranged-1v1") { Map = ScenarioSetups.TowersMap });
        var two = harness.Connect();
        two.Send(new JoinMatch(one.Welcome!.Code));
        var created = Assert.IsType<Rules.Core.Events.MatchCreated>(harness.Lobby.LogOf(one.Welcome.Code)![0].Event);
        Assert.Equal(Fingerprint(Scenarios.All["ranged-1v1-towers"]), Fingerprint(created.Setup));
        Play.ToEnd(one, two, 1);
        var record = Assert.Single(recorder.Closed);
        Assert.Equal("ranged-1v1", record.Scenario);
        Assert.Equal(ScenarioSetups.TowersMap, record.Map);
        Assert.Equal(RecordFormat.MapHash(created.Setup.Scenario!), record.MapHash);
    }

    [Fact]
    public void ARecordOfANamedScenarioShowsItsMap()
    {
        var record = TestRecords.Completed("ranged-1v1-towers");
        Assert.Equal(ScenarioSetups.TowersMap, record.Map);
        Assert.Equal(ScenarioSetups.PlainMap, TestRecords.Completed("ranged-1v1").Map);
        var bytes = Encoding.UTF8.GetString(RecordFormat.EncodeBody(record));
        Assert.Contains("\"map\":\"claim-no-mans-land-towers\",\"mapHash\":\"sha256:", bytes);
    }

    [Fact]
    public void AnUnknownOrUnfitMapIsRefused()
    {
        var harness = new Harness();
        var one = harness.Connect();
        one.Send(new CreateMatch("ranged") { Map = "nowhere" });
        Assert.Equal(nameof(ProtocolError.UnknownMap), Assert.IsType<Rejected>(one.LastAnswer).Code);
        Assert.Null(one.Welcome);
        // A server whose map is not made for the scenario.
        var plain = Maps[ScenarioSetups.PlainMap];
        var answers = new List<ServerMessage>();
        var outbox = new Outbox((_, m) => answers.Add(m), _ => { });
        var lobby = new Lobby(Scenarios.All, () => new SeededDice(1), outbox,
            maps: new Dictionary<string, MapDefinition> { ["elsewhere"] = plain with { Id = "elsewhere", Supports = ["trench-warfare"] } });
        lobby.Receive(new ConnectionId(1), Codec.Encode(new CreateMatch("ranged") { Map = "elsewhere" }));
        var rejected = Assert.IsType<Rejected>(Assert.Single(answers));
        Assert.Equal(nameof(ProtocolError.MapDoesNotFit), rejected.Code);
        Assert.Contains("not made for claim-no-mans-land", rejected.Text);
    }

    [Fact]
    public void AMapHashThatDoesNotMatchTheSetupFailsVerification()
    {
        var record = TestRecords.Completed("ranged-1v1-towers");
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        Assert.Equal(VerdictKind.Completed, Verify(record, key).Kind);
        var forged = record with { MapHash = RecordFormat.MapHash(Maps[ScenarioSetups.PlainMap].ToScenario()) };
        var verdict = Verify(forged, key);
        Assert.Equal(VerdictKind.Invalid, verdict.Kind);
        Assert.Equal(VerdictReason.BadSetup, verdict.Reason);
        // A record without the map fields, as every record before, still verifies.
        Assert.Equal(VerdictKind.Completed, Verify(record with { Map = null, MapHash = null }, key).Kind);
    }

    private static RecordVerdict Verify(MatchRecordBody body, System.Security.Cryptography.ECDsa key) =>
        RecordVerifier.Verify(RecordFormat.Sign(body, key), [new TrustedKey(RecordKeys.KeyIdOf(key), key)]);

    // Stage S10: the campaign tables count as built exactly the scenarios this build plays.
    [Fact]
    public void TheCampaignTablesKnowWhichScenariosAreBuilt() =>
        Assert.Equal(ScenarioSetups.Definitions.Keys.Order(StringComparer.Ordinal), CampaignTables.Built.Order(StringComparer.Ordinal));
}
