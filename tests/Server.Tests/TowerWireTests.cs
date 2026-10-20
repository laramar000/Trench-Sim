using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Towers proposal, stage T1: a tower travels on the wire and back unchanged, and a battlefield without one is written
// exactly as before (no "climb" field at all), so every existing record keeps its bytes.
public class TowerWireTests
{
    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(MatchSetup setup)
    {
        var created = new MatchCreated(setup);
        return Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, created, StateHash.Compute(Reducer.Apply(MatchState.None, created)))));
    }

    [Fact]
    public void ATowerBattlefieldRoundTrips()
    {
        var setup = ClaimNoMansLand.CreateTowersSetup(Plain().Models);
        var text = Encoded(setup);
        Assert.Contains("\"climb\":", text, StringComparison.Ordinal);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoding.UTF8.GetString(Codec.Encode(read)));
        var back = ((MatchCreated)read.Event).Setup.Scenario!.Battlefield;
        Assert.Equal(setup.Scenario!.Battlefield.Terrain.ToArray(), back.Terrain.ToArray());
        Assert.Equal(2, back.Terrain.Count(t => t.IsTower));
        Assert.Equal(StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))), StateHash.Compute(Reducer.Apply(MatchState.None, read.Event)));
    }

    [Fact]
    public void ABattlefieldWithoutTowersHasNoClimbField()
    {
        var text = Encoded(Plain());
        Assert.DoesNotContain("climb", text, StringComparison.Ordinal);
        // And a record written before towers existed reads back the same: the field is optional.
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.All(((MatchCreated)read.Event).Setup.Scenario!.Battlefield.Terrain, t => Assert.Null(t.Climb));
    }

    [Fact]
    public void ABadTowerOnTheWireIsAnInvalidValue()
    {
        var text = Encoded(ClaimNoMansLand.CreateTowersSetup(Plain().Models)).Replace("\"height\":2000", "\"height\":3000", StringComparison.Ordinal);
        Assert.Contains("\"height\":3000", text, StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.NotNull(failure);
    }
}
