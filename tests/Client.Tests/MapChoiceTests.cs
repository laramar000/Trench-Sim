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
        Assert.Equal(maps.Keys.Except(own).Order(), MapChoices.All.Select(m => m.Id).Order());
        Assert.All(MapChoices.All, m =>
        {
            Assert.True(maps[m.Id].IsFor(m.ScenarioId), m.Id);
            Assert.Equal(maps[m.Id].Name, m.Name);
        });
    }

    [Fact]
    public void ANameOffersThePresetsOfItsScenario()
    {
        Assert.Equal(["ruined-village", "shell-holes", "crossfire"], MapChoices.For("melee-2v2-towers").Select(m => m.Id));
        Assert.Equal(["dust", "broken-line"], MapChoices.For("ranged-trench-warfare").Select(m => m.Id));
        Assert.All(ContentNames.Choices, c => Assert.NotEmpty(MapChoices.For(c)));
    }
}
