using Match.Protocol;
using Match.Server;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Trench-maps stage S7: the preset maps. Each is a data file that keeps the set-up rules (stage S4), fits the scenario
// it is made for (stage S5), and plays a whole match to its end on the server, recorded with its id and hash.
public class PresetMapTests
{
    public static TheoryData<string, string> Presets => new()
    {
        { "ruined-village", "ranged-1v1" },
        { "shell-holes", "melee-2v2" },
        { "crossfire", "ranged-2v2" },
        { "dust", "ranged-2v2-trench-warfare" },
        { "broken-line", "melee-1v1-trench-warfare" },
    };

    [Theory]
    [MemberData(nameof(Presets))]
    public void APresetKeepsTheRulesFitsItsScenarioAndPlaysToTheEnd(string map, string scenario)
    {
        var definition = ScenarioCatalog.Maps[map];
        Assert.Equal("no-mans-land", definition.Archetype);
        Assert.Empty(definition.Problems(ScenarioSetups.LargestBase(Path.Combine(AppContext.BaseDirectory, "content"))));
        var setup = ScenarioSetups.OnMap(Scenarios.All[scenario], definition);
        Assert.Equal(RecordFormat.MapHash(definition.ToScenario()), RecordFormat.MapHash(setup.Scenario!));

        var recorder = new CapturingRecorder();
        foreach (var seed in new[] { 1, 2 })
        {
            var harness = new Harness(seed, recorder: recorder);
            var one = harness.Connect();
            one.Send(new CreateMatch(scenario) { Map = map });
            var two = harness.Connect();
            two.Send(new JoinMatch(one.Welcome!.Code));
            Play.ToEnd(one, two, seed);
            Assert.Equal(Phase.Ended, one.Mirror.State.Phase);
        }
        Assert.All(recorder.Closed, r => Assert.Equal(map, r.Map));
    }

    [Fact]
    public void TheDustMapHasTwoSitesAndAConnector()
    {
        var field = ScenarioCatalog.Maps["dust"].Battlefield;
        Assert.Equal(["connector", "site-a", "site-b"], Rules.Core.Scenarios.TrenchWarfare.ScoringSections(field).Select(t => t.Id));
        Assert.All(new[] { "site-a", "site-b" }, id => Assert.False(field.Terrain.Single(t => t.Id == id).Footprint.IsSingle));
        Assert.Equal("east", Rules.Core.Scenarios.TrenchWarfare.DefenderZone(field));
    }

    [Fact]
    public void APresetForOneScenarioIsRefusedForTheOther()
    {
        Assert.Throws<MapFormatException>(() => ScenarioSetups.OnMap(Scenarios.All["ranged"], ScenarioCatalog.Maps["dust"]));
        Assert.Throws<MapFormatException>(() => ScenarioSetups.OnMap(Scenarios.All["ranged-trench-warfare"], ScenarioCatalog.Maps["crossfire"]));
    }
}
