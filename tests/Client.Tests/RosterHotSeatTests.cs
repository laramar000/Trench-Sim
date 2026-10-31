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

    // Roster stage 5c (rosters online): each player hosts or joins with a saved roster; both windows see the real
    // setup and both rosters' names, which the server sent (not this window's own content).
    [Fact]
    public async Task TwoPlayersHostAndJoinWithTheirOwnRosters()
    {
        var folder = Path.Combine(_dir.FullName, "rosters");
        var hostFile = SaveRoster("Iron Line", ("lieutenant", ["pistol", "sword-axe"]), ("yeoman", ["bolt-action-rifle"]));
        var joinFile = SaveRoster("Mud Saints", ("lieutenant", ["bolt-action-rifle", "standard-armour"]));
        var hostRoster = RosterMatch.PrepareOne(folder, hostFile);
        var joinRoster = RosterMatch.PrepareOne(folder, joinFile);
        Assert.Null(hostRoster.Problem);
        Assert.Null(joinRoster.Problem);
        ContentNames.RosterContent = null;

        await using var one = HotController(_server.Endpoint);
        await using var two = HotController(_server.Endpoint);
        var welcome = Assert.IsType<Match.Protocol.Welcome>(await one.CreateWithRosterAsync(hostRoster.Json!, null, Token));
        Assert.IsType<Match.Protocol.Welcome>(await two.JoinAsync(welcome.Code, joinRoster.Json, Token));
        foreach (var controller in new[] { one, two })
        {
            var screen = await Until(controller, s => s.View.Seats.Count == 2 && s.View.Seats.All(seat => seat.Warband is "Iron Line" or "Mud Saints")
                && s.View.Models.Count == 3);
            Assert.Equal(["Iron Line", "Mud Saints"], screen.View.Seats.Select(seat => seat.Warband));
            Assert.Contains(screen.View.Models, m => m.Name == "Lieutenant (b1)");
            Assert.Null(screen.View.NamesProblem);
        }
    }

    [Fact]
    public void ARosterTheGameCannotPlayIsNotSent()
    {
        var file = SaveRoster("Bombers", ("lieutenant", ["frag-grenades"]));
        var prepared = RosterMatch.PrepareOne(Path.Combine(_dir.FullName, "rosters"), file);
        Assert.Null(prepared.Json);
        Assert.Contains("Your roster Bombers has what the game cannot play yet", prepared.Problem, StringComparison.Ordinal);
    }
}
