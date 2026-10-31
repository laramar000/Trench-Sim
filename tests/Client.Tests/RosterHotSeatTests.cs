using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// Roster stage 5b (hot-seat first): two saved rosters are played hot-seat on the real local server, started with
// --warbands; the models carry the rosters' names and profiles. A roster the game cannot play yet starts nothing.
public sealed partial class TransportTests
{
    private string SaveRoster(string name, params (string Entry, string[] Kit)[] models)
    {
        var builder = RosterBuilder.Shipped();
        builder.Name = name;
        foreach (var (entry, kit) in models)
        {
            builder.Select(builder.Add(entry));
            foreach (var k in kit)
            {
                builder.AddKit(k);
            }
        }
        return Path.GetFileName(builder.Save(Path.Combine(_dir.FullName, "rosters")));
    }

    [Fact]
    public async Task TwoSavedRostersArePlayedHotSeat()
    {
        var one = SaveRoster("Iron Line", ("lieutenant", ["pistol", "sword-axe"]), ("yeoman", ["bolt-action-rifle"]));
        var two = SaveRoster("Mud Saints", ("lieutenant", ["bolt-action-rifle", "standard-armour"]));
        var prepared = RosterMatch.Prepare(Path.Combine(_dir.FullName, "rosters"), one, two);
        Assert.Null(prepared.Problem);
        var file = Path.Combine(_dir.FullName, "roster-warbands.json");
        File.WriteAllText(file, prepared.Json);
        ContentNames.RosterContent = prepared.Json;

        await using var server = await LocalServer.StartBuiltAsync(ServerDll(), Path.Combine(_dir.FullName, "hotseat-rosters"),
            cancellationToken: Token, serverArgs: ["--warbands", file]);
        await using var hot = await HotSeat.StartAsync(() => HotController(server.Endpoint), ContentNames.RosterScenario, server, Token);
        await Until(hot.Two, s => s.Seat == Seat.Two && s.View.Phase != Words.Phase(Phase.PreGame));
        await Until(hot.One, s => s.Seat == Seat.One && s.View.Phase != Words.Phase(Phase.PreGame));
        var started = DateTime.UtcNow;
        while (hot.One.Session?.Mirror.State.Setup is null && DateTime.UtcNow - started < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(50, Token);
        }
        var setup = hot.One.Session!.Mirror.State.Setup!;
        Assert.Equal(3, setup.Models.Length);
        var names = ContentNames.For(ContentNames.RosterScenario, setup);
        Assert.Null(names.Problem);
        Assert.Equal(("Iron Line", "Mud Saints"), (names.WarbandOne, names.WarbandTwo));
        Assert.Equal("Lieutenant (a1)", names.Of(new ModelId("a1")));
        Assert.Equal(-1, setup.Models.Single(m => m.Id.Value == "b1").Profile!.Armour);
    }

    [Fact]
    public void ARosterTheGameCannotPlayStartsNothing()
    {
        var one = SaveRoster("Bombers", ("lieutenant", ["frag-grenades"]));
        var two = SaveRoster("Plain", ("lieutenant", []));
        var prepared = RosterMatch.Prepare(Path.Combine(_dir.FullName, "rosters"), one, two);
        Assert.Null(prepared.Json);
        Assert.Contains("Seat One's roster Bombers has what the game cannot play yet", prepared.Problem, StringComparison.Ordinal);
        Assert.Contains("Frag Grenades", prepared.Problem, StringComparison.Ordinal);
    }
}
