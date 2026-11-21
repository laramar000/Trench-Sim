using Client.Core;
using Sim;

namespace Client.Tests;

// Trench-maps stage S7: the client's map list names exactly the server's preset maps, each for the scenario it is made
// for, and a create-match name offers only the presets that fit it.
public class MapChoiceTests
{
    [Fact]
    public void TheListMatchesTheServersPresetMaps()
    {
        var maps = MapFiles.LoadFolder(Path.Combine(Repository.Root, "content", MapFiles.Folder));
        var own = new[] { ScenarioSetups.PlainMap, ScenarioSetups.TowersMap, ScenarioSetups.TrenchWarfareMap };
        var presets = MapChoices.All.Where(m => !m.Id.StartsWith(MapChoices.GeneratedPrefix, StringComparison.Ordinal)).ToList();
        Assert.Equal(maps.Keys.Except(own).Order(), presets.Select(m => m.Id).Order());
        // Stage S9: one random map per scenario, a request the server's generator answers with a map for that scenario.
        var random = MapChoices.All.Except(presets).ToList();
        Assert.Equal([MapChoices.ClaimNoMansLand, MapChoices.TrenchWarfare], random.Select(m => m.ScenarioId));
        Assert.All(random, m =>
        {
            Assert.True(MapGenerator.IsRequest(m.Id, out var kind), m.Id);
            Assert.True(MapGenerator.Generate(kind, 3).IsFor(m.ScenarioId), m.Id);
        });
        Assert.All(presets, m =>
        {
            Assert.True(maps[m.Id].IsFor(m.ScenarioId), m.Id);
            Assert.Equal(maps[m.Id].Name, m.Name);
        });
    }

    [Fact]
    public void ANameOffersThePresetsOfItsScenario()
    {
        Assert.Equal(["ruined-village", "shell-holes", "crossfire", "generated-claim-no-mans-land"], MapChoices.For("melee-2v2-towers").Select(m => m.Id));
        Assert.Equal(["dust", "broken-line", "generated-trench-warfare"], MapChoices.For("ranged-trench-warfare").Select(m => m.Id));
        Assert.All(ContentNames.Choices, c => Assert.NotEmpty(MapChoices.For(c)));
    }
}
