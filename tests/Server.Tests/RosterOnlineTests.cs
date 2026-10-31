using System.Text;
using Match.Protocol;
using Match.Server;
using Roster;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Roster stage 5c (rosters online): Host and Join each send a saved roster; the server checks both itself, waits with
// a placeholder setup until the second arrives, then sets the match up from the two rosters and tells Seat One to start
// its copy again. Records are made only of the real match and verify as any other.
public class RosterOnlineTests
{
    private static readonly RosterMatches Rosters = RosterMatches.FromContent(ScenarioCatalog.Maps)
        ?? throw new InvalidOperationException("The shipped roster data did not load.");

    private static string RosterFile(string name, params (string Entry, string[] Kit)[] models) =>
        new WarbandRoster(name, "principality", null, 700, 0, [.. models.Select(m => new RosterModel(m.Entry, null, m.Kit, []))]).ToJson();

    private static readonly string IronLine = RosterFile("Iron Line", ("lieutenant", ["pistol", "sword-axe"]), ("yeoman", ["bolt-action-rifle"]));
    private static readonly string MudSaints = RosterFile("Mud Saints", ("lieutenant", ["bolt-action-rifle", "standard-armour"]));

    private static Harness NewHarness(CapturingRecorder? recorder = null) => new(recorder: recorder, rosters: Rosters);

    [Fact]
    public void TheRosterFieldsAreLeftOutWhenNotSent()
    {
        Assert.Equal("{\"v\":2,\"type\":\"JoinMatch\",\"code\":\"ABC\"}", Encoding.UTF8.GetString(Codec.Encode(new JoinMatch("ABC"))));
        var create = new CreateMatch(RosterText.Scenario) { Roster = IronLine };
        Assert.Equal(create, Codec.ParseClient(Codec.Encode(create), out _));
        var join = new JoinMatch("ABC") { Roster = MudSaints };
        Assert.Equal(join, Codec.ParseClient(Codec.Encode(join), out _));
        Assert.Throws<ArgumentException>(() => new JoinMatch("ABC") { Roster = "" });
        var (one, _) = new Harness().Pair("ranged-1v1");
        Assert.Null(one.Welcome!.Warbands);
        Assert.DoesNotContain("warbands", Encoding.UTF8.GetString(Codec.Encode(one.Welcome)), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRostersSentOnlineArePlayedAndRecorded()
    {
        var recorder = new CapturingRecorder();
        var harness = NewHarness(recorder);
        var one = harness.Connect();
        one.Send(new CreateMatch(RosterText.Scenario) { Roster = IronLine });
        var waiting = one.Welcome!;
        Assert.Equal(MatchStatus.Waiting, waiting.Status);
        Assert.Equal(RosterText.Scenario, waiting.Scenario);
        Assert.Contains("Iron Line", waiting.Warbands, StringComparison.Ordinal);
        Assert.Empty(recorder.Started);

        var two = harness.Connect();
        two.Send(new JoinMatch(waiting.Code) { Roster = MudSaints });
        Assert.IsType<Welcome>(two.Welcome);
        // Seat One was told to start its copy again from the real setup, with both rosters' names.
        Assert.Equal(2, one.Welcomes.Count);
        Assert.Equal(0, one.Welcomes[1].ReplayFrom);
        Assert.True(one.Synchronized || one.Mirror.Events > one.Welcomes[1].Events);
        Assert.Equal(one.Mirror.Hash, two.Mirror.Hash);
        var setup = one.Mirror.State.Setup!;
        Assert.Equal(["a1", "a2", "b1"], setup.Models.Select(m => m.Id.Value));
        Assert.Equal(-1, setup.Models.Single(m => m.Id.Value == "b1").Profile!.Armour);
        foreach (var names in new[] { one.Welcomes[1].Warbands, two.Welcome!.Warbands })
        {
            Assert.Contains("Iron Line", names, StringComparison.Ordinal);
            Assert.Contains("Mud Saints", names, StringComparison.Ordinal);
        }
        var log = harness.Lobby.LogOf(waiting.Code)!;
        Assert.Single(log, e => e.Event is MatchCreated);

        Play.ToEnd(one, two, 3);
        var record = Assert.Single(recorder.Closed);
        Assert.Equal(RosterText.Scenario, record.Scenario);
        Assert.Equal(ScenarioSetups.PlainMap, record.Map);
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var verdict = RecordVerifier.Verify(RecordFormat.Sign(record, key), [new TrustedKey(RecordKeys.KeyIdOf(key), key)]);
        Assert.Equal(VerdictKind.Completed, verdict.Kind);
    }

    [Fact]
    public void ARosterMatchCanBePlayedOnAnotherMap()
    {
        var harness = NewHarness();
        var one = harness.Connect();
        one.Send(new CreateMatch(RosterText.Scenario) { Roster = IronLine, Map = ScenarioSetups.TowersMap });
        var two = harness.Connect();
        two.Send(new JoinMatch(one.Welcome!.Code) { Roster = MudSaints });
        var created = Assert.IsType<MatchCreated>(harness.Lobby.LogOf(one.Welcome.Code)![0].Event);
        Assert.Equal(RecordFormat.MapHash(ScenarioCatalog.Maps[ScenarioSetups.TowersMap].ToScenario()), RecordFormat.MapHash(created.Setup.Scenario!));
        Assert.Equal(3, created.Setup.Models.Length);
    }

    [Fact]
    public void ARosterTheServerCannotPlayIsRefusedByName()
    {
        var harness = NewHarness();
        var one = harness.Connect();
        one.Send(new CreateMatch(RosterText.Scenario) { Roster = RosterFile("Bombers", ("lieutenant", ["frag-grenades"])) });
        var rejected = Assert.IsType<Rejected>(one.LastAnswer);
        Assert.Equal(nameof(ProtocolError.RosterRefused), rejected.Code);
        Assert.Contains("Frag Grenades", rejected.Text, StringComparison.Ordinal);
        Assert.Null(one.Welcome);

        // Text that is not a roster, an illegal roster, a faction the server does not have, a roster with another scenario.
        foreach (var (create, words) in new (CreateMatch, string)[]
        {
            (new CreateMatch(RosterText.Scenario) { Roster = "{not json" }, "cannot be read"),
            (new CreateMatch(RosterText.Scenario) { Roster = RosterFile("Crowd", [.. Enumerable.Repeat(("lieutenant", Array.Empty<string>()), 3)]) }, "not legal"),
            (new CreateMatch(RosterText.Scenario) { Roster = IronLine.Replace("principality", "nowhere", StringComparison.Ordinal) }, "does not have"),
            (new CreateMatch("ranged") { Roster = IronLine }, "scenario"),
        })
        {
            var client = harness.Connect();
            client.Send(create);
            var refused = Assert.IsType<Rejected>(client.LastAnswer);
            Assert.Equal(nameof(ProtocolError.RosterRefused), refused.Code);
            Assert.Contains(words, refused.Text, StringComparison.Ordinal);
        }
        Assert.Equal(0, harness.Lobby.MatchCount);
    }

    [Fact]
    public void AMatchIsJoinedWithTheKindOfWarbandItWasHostedWith()
    {
        var harness = NewHarness();
        var host = harness.Connect();
        host.Send(new CreateMatch(RosterText.Scenario) { Roster = IronLine });
        var without = harness.Connect();
        without.Send(new JoinMatch(host.Welcome!.Code));
        Assert.Contains("join with one of your saved rosters", Assert.IsType<Rejected>(without.LastAnswer).Text, StringComparison.Ordinal);
        var bad = harness.Connect();
        bad.Send(new JoinMatch(host.Welcome.Code) { Roster = RosterFile("Bombers", ("lieutenant", ["frag-grenades"])) });
        Assert.Equal(nameof(ProtocolError.RosterRefused), Assert.IsType<Rejected>(bad.LastAnswer).Code);
        // Still waiting, and still joinable with a playable roster.
        Assert.Equal(MatchStatus.Waiting, harness.Lobby.StatusOf(host.Welcome.Code));
        var good = harness.Connect();
        good.Send(new JoinMatch(host.Welcome.Code) { Roster = MudSaints });
        Assert.Equal(MatchStatus.Playing, harness.Lobby.StatusOf(host.Welcome.Code));

        var practice = harness.Connect();
        practice.Send(new CreateMatch("ranged-1v1"));
        var withRoster = harness.Connect();
        withRoster.Send(new JoinMatch(practice.Welcome!.Code) { Roster = MudSaints });
        Assert.Contains("practice warbands", Assert.IsType<Rejected>(withRoster.LastAnswer).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AServerWithoutRosterDataRefusesRosters()
    {
        var one = new Harness().Connect();
        one.Send(new CreateMatch(RosterText.Scenario) { Roster = IronLine });
        var rejected = Assert.IsType<Rejected>(one.LastAnswer);
        Assert.Equal(nameof(ProtocolError.RosterRefused), rejected.Code);
        Assert.Contains("does not take rosters", rejected.Text, StringComparison.Ordinal);
    }
}

// Roster stage 5c: trench-cli hosts and joins with saved roster files (--roster); two of its test-only bots play the
// match to the end on the real server, and both see the same final hash.
public sealed class RosterCliTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("trench-roster-cli-");

    public void Dispose() => _dir.Delete(recursive: true);

    // A trench-cli process whose output lines are collected as they come.
    private sealed class CliRun : IDisposable
    {
        private readonly List<string> _lines = [];

        public CliRun(string home, params string[] args)
        {
            var info = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "trench-cli.dll"));
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }
            info.Environment["TRENCH_SIM_HOME"] = home;
            Process = new System.Diagnostics.Process { StartInfo = info };
            Process.OutputDataReceived += (_, e) => Add(e.Data);
            Process.ErrorDataReceived += (_, e) => Add(e.Data is null ? null : "stderr: " + e.Data);
            Process.Start();
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
        }

        public System.Diagnostics.Process Process { get; }

        public string Output
        {
            get
            {
                lock (_lines)
                {
                    return string.Join('\n', _lines);
                }
            }
        }

        private void Add(string? line)
        {
            if (line is not null)
            {
                lock (_lines)
                {
                    _lines.Add(line);
                }
            }
        }

        public async Task<bool> ExitedWithin(TimeSpan limit)
        {
            using var timeout = new CancellationTokenSource(limit);
            try
            {
                await Process.WaitForExitAsync(timeout.Token);
                Process.WaitForExit();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }
            Process.Dispose();
        }
    }

    private string Save(string name, string json)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task TwoTrenchCliBotsPlayTheirRostersToTheEnd()
    {
        var one = Save("one.roster.json", new WarbandRoster("Iron Line", "principality", null, 700, 0,
            [new RosterModel("lieutenant", null, ["pistol", "sword-axe"], []), new RosterModel("yeoman", null, ["bolt-action-rifle"], [])]).ToJson());
        var two = Save("two.roster.json", new WarbandRoster("Mud Saints", "principality", null, 700, 0,
            [new RosterModel("lieutenant", null, ["bolt-action-rifle", "standard-armour"], [])]).ToJson());
        await using var server = await MatchServer.StartAsync(new MatchServerOptions { NewDice = () => new Sim.SeededDice(5) });
        var url = server.Endpoint.ToString();
        using var host = new CliRun(Path.Combine(_dir.FullName, "a"), "--server", url, "create", RosterText.Scenario, "--roster", one, "--bot", "1");
        string? code = null;
        for (var i = 0; i < 600 && code is null && !host.Process.HasExited; i++)
        {
            code = System.Text.RegularExpressions.Regex.Match(host.Output, @"Match ([A-Z0-9]+) \(roster\)\. You are Seat One\.") is { Success: true } m ? m.Groups[1].Value : null;
            await Task.Delay(100);
        }
        Assert.True(code is not null, host.Output);

        using var join = new CliRun(Path.Combine(_dir.FullName, "b"), "--server", url, "join", code, "--roster", two, "--bot", "2");
        var joined = await join.ExitedWithin(TimeSpan.FromMinutes(3));
        var hosted = await host.ExitedWithin(TimeSpan.FromSeconds(30));
        var tail = $"join: ...{join.Output[^Math.Min(1500, join.Output.Length)..]} || host: ...{host.Output[^Math.Min(1500, host.Output.Length)..]}";
        Assert.True(joined && hosted, tail);
        Assert.True(join.Process.ExitCode == 0 && host.Process.ExitCode == 0, tail);
        string Final(string output) => System.Text.RegularExpressions.Regex.Match(output, @"events, every one matched the server's hash\. Final hash ([0-9a-f]{64})").Groups[1].Value;
        Assert.Matches("^[0-9a-f]{64}$", Final(join.Output));
        Assert.Equal(Final(join.Output), Final(host.Output));
        Assert.Contains("MATCH ENDED", join.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrenchCliRefusesARosterWithTheWrongAction()
    {
        using var wrong = new CliRun(Path.Combine(_dir.FullName, "c"), "create", "ranged", "--roster", "x.roster.json");
        Assert.True(await wrong.ExitedWithin(TimeSpan.FromMinutes(1)));
        Assert.Equal(2, wrong.Process.ExitCode);
        var error = wrong.Output;
        Assert.Contains("--roster", error, StringComparison.Ordinal);
    }
}

// Roster stage 5c: on the real server, Seat One's connection gets a second Welcome when the second roster arrives. It
// answers no request, so it must not be taken as the answer to Seat One's next command; two bots on two connections
// then play the match to the end, every command with its own answer.
public class RosterSessionTests
{
    [Fact]
    public async Task TheSetUpWelcomeIsNotTakenAsTheAnswerToACommand()
    {
        string F(string n, params (string E, string[] K)[] ms) => new WarbandRoster(n, "principality", null, 700, 0, [.. ms.Select(m => new RosterModel(m.E, null, m.K, []))]).ToJson();
        await using var server = await MatchServer.StartAsync(new MatchServerOptions { NewDice = () => new SeededDice(5) });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var one = await Match.Client.ClientSession.ConnectAsync(server.Endpoint, timeout.Token);
        await using var two = await Match.Client.ClientSession.ConnectAsync(server.Endpoint, timeout.Token);
        var welcome = Assert.IsType<Welcome>(await one.CreateWithRosterAsync(F("Iron Line", ("lieutenant", ["pistol", "sword-axe"]), ("yeoman", ["bolt-action-rifle"])), null, timeout.Token));
        Assert.IsType<Welcome>(await two.JoinAsync(welcome.Code, F("Mud Saints", ("lieutenant", ["bolt-action-rifle", "standard-armour"])), timeout.Token));
        await one.WaitForAsync(s => s.Synchronized && s.Mirror.State.Setup?.Models.Length == 3, timeout.Token);
        Assert.False(one.TryNextAnswer(out var queued), $"a Welcome nobody asked for was queued as an answer: {queued}");
        var bots = new[] { Match.Cli.BotPlayer.PlayAsync(one, 1, timeout.Token), Match.Cli.BotPlayer.PlayAsync(two, 2, timeout.Token) };
        await Task.WhenAll(bots);
        Assert.Equal(Phase.Ended, one.Mirror.State.Phase);
        Assert.Equal(one.Mirror.Hash, two.Mirror.Hash);
    }
}
