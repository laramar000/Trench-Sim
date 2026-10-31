using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

// M7.3 verification runner for the Godot client. It checks the build, launches the project in Godot, then plays a
// scripted session end to end with real processes: the match server, a proxy it can hold or cut, two Godot clients
// driven by key presses (client/scripts/UiCheck.cs) and trench-cli bots. Every result is written to <out>/summary.md.
//
// dotnet run --project tools/UiCheck -- --godot <path to the Godot 4.7.2 .NET executable> [--out <folder>] [--skip-build] [--opengl]
// On Linux without a display, run it under xvfb-run so Godot has a window to draw in.
namespace UiCheck;

public static class Program
{
    private static readonly List<(string Id, string Result, string Detail)> Results = [];
    private static string _out = "";
    private static string _repo = "";
    private static string[] _renderer = [];

    public static async Task<int> Main(string[] args)
    {
        var godot = Arg(args, "--godot") ?? Environment.GetEnvironmentVariable("GODOT");
        _repo = FindRepository();
        _out = Path.GetFullPath(Arg(args, "--out") ?? Path.Combine(_repo, "ui-check-results", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        if (godot is null || !File.Exists(godot))
        {
            Console.Error.WriteLine("Give the Godot 4.7.2 .NET executable with --godot <path> (or the GODOT environment variable).");
            return 2;
        }
        if (args.Contains("--opengl"))
        {
            // For machines without Vulkan (for example a Linux container under xvfb-run).
            _renderer = ["--rendering-driver", "opengl3"];
        }
        Directory.CreateDirectory(_out);
        Directory.CreateDirectory(Path.Combine(_out, "logs"));
        Console.WriteLine($"Results in {_out}");
        Record("environment", "INFO", $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}; .NET {Environment.Version}; repository {_repo}; Godot {godot}");
        try
        {
            await Checks(godot, args.Contains("--skip-build"));
        }
        catch (Exception e)
        {
            Record("runner", "FAIL", $"{e.GetType().Name}: {e.Message}");
        }
        return WriteSummary();
    }

    private static async Task Checks(string godot, bool skipBuild)
    {
        // 1. Versions and builds.
        var version = await Run("godot-version", godot, ["--version"], timeout: 60);
        Record("godot-version", version.Exit == 0 && version.Output.Contains("4.7.2") && version.Output.Contains("mono") ? "PASS" : "FAIL",
            $"exit {version.Exit}: {version.Output.Trim()}");
        if (!skipBuild)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var build = await Run($"client-build-{configuration}", "dotnet", ["build", Path.Combine(_repo, "client", "Client.csproj"), "-c", configuration, "-warnaserror", "--no-incremental"], timeout: 600);
                Record($"client-build-{configuration}", build.Exit == 0 && build.Output.Contains(" 0 Warning(s)") && build.Output.Contains(" 0 Error(s)") ? "PASS" : "FAIL",
                    $"dotnet build client/Client.csproj -c {configuration} -warnaserror: exit {build.Exit}, {Summary(build.Output)}");
            }
            foreach (var project in new[] { "Match.Server", "Match.Cli" })
            {
                var build = await Run($"build-{project}", "dotnet", ["build", Path.Combine(_repo, "src", project), "-c", "Debug", "-warnaserror"], timeout: 600);
                Record($"build-{project}", build.Exit == 0 ? "PASS" : "FAIL", $"exit {build.Exit}, {Summary(build.Output)}");
            }
        }
        var editorBuild = await Run("godot-build-solutions", godot, ["--headless", "--path", Path.Combine(_repo, "client"), "--build-solutions", "--quit"], timeout: 600);
        Record("godot-build-solutions", editorBuild.Exit == 0 && !editorBuild.Output.Contains("ERROR") ? "PASS" : "FAIL",
            $"godot --headless --build-solutions --quit: exit {editorBuild.Exit}{Errors(editorBuild.Output)}");

        // 2. The scene itself, launched by Godot in a window (not only built).
        var launch = await Run("godot-launch", godot, [.. _renderer, "--path", Path.Combine(_repo, "client"), "--quit-after", "120"], timeout: 120);
        Record("godot-launch-scene", launch.Exit == 0 && launch.Output.Contains("Godot Engine v4.7.2") && EngineErrors([.. launch.Output.Split('\n')]).Count == 0 ? "PASS" : "FAIL",
            $"godot --path client --quit-after 120 (main scene, real window, 120 frames): exit {launch.Exit}{Errors(launch.Output)}");

        // 3. End to end.
        await EndToEnd(godot);

        // 4. Single player (hot-seat): one Godot window starts its own local server and plays both seats.
        await HotSeat(godot);

        // 4b. Mines stage M3: a hot-seat window on Trench Warfare lays the defender's Mine Markers by mouse and buttons.
        await HotSeatMines(godot);

        // 5. M7.5 stage 5b: two Godot clients play a whole melee match, one is killed and rejoins, and the signed record
        // is checked with trench-cli verify.
        await PvpMatch(godot);

        // 6. M7.5 stage 5c: a Godot client plays a whole ranged match against trench-cli and its test-only --bot, and the
        // Godot client, trench-cli and the signed record (trench-cli verify) must agree.
        await GodotVsTerminal(godot);
    }

    private static async Task GodotVsTerminal(string godot)
    {
        var gvt = Path.Combine(_out, "gvt");
        Directory.CreateDirectory(gvt);
        var serverDll = Path.Combine(_repo, "src", "Match.Server", "bin", "Debug", "net10.0", "Match.Server.dll");
        var cliDll = Path.Combine(_repo, "src", "Match.Cli", "bin", "Debug", "net10.0", "trench-cli.dll");
        var port = FreePort();
        var data = Path.Combine(_out, "gvt-server-data");
        var url = $"ws://127.0.0.1:{port}/match";
        var started = DateTime.UtcNow;
        var server = Start("gvt-server", "dotnet", [serverDll, "--port", port.ToString(), "--data", data], null);
        var processes = new List<Child> { server };
        try
        {
            await server.WaitFor(l => l.Contains("Listening on"), ServerStartSeconds, "server start");
            var godotA = Start("godot-gvt-a", godot, GodotArgs("gvt-a", gvt, url, null, "ranged-towers"), Path.Combine(_out, "home-gvt-a"));
            processes.Add(godotA);
            var code = await Mark(gvt, "gvt-a", "code", 120);
            // The existing test-only bot of trench-cli plays Seat Two (no user-facing bot in the client).
            var cli = Start("gvt-cli", "dotnet", [cliDll, "--server", url, "join", code, "--bot", "7"], Path.Combine(_out, "home-gvt-cli"));
            processes.Add(cli);
            Record("gvt-a-exit", await godotA.Exit(1600) == 0 ? "PASS" : "FAIL", $"Godot client exit code {godotA.ExitCode} (0 = all its checks passed)");
            Record("gvt-cli-exit", await cli.Exit(120) == 0 ? "PASS" : "FAIL", $"trench-cli --bot exit code {cli.ExitCode}");

            // What each side shows at the end: Godot's result section, and trench-cli's last event and final line.
            var end = Dumps(gvt, "gvt-a", "pvp-end");
            var hashGodot = end.GetValueOrDefault("final_hash", "");
            var eventsGodot = end.GetValueOrDefault("events", "");
            var ended = end.GetValueOrDefault("ended", "");
            var cliLines = cli.Lines;
            var cliEnded = cliLines.LastOrDefault(l => Regex.IsMatch(l, @"^\s+\[\d+\] MATCH ENDED: "))?.Trim() ?? "";
            var cliFinal = cliLines.Select(l => Regex.Match(l, @"^(\d+) events, every one matched the server's hash\. Final hash ([0-9a-f]{64})\.$")).LastOrDefault(m => m.Success);
            var hashCli = cliFinal?.Groups[2].Value ?? "";
            var eventsCli = cliFinal?.Groups[1].Value ?? "";
            Record("gvt-both-reach-match-ended", ended.Length > 0 && ended == cliEnded ? "PASS" : "FAIL", $"Godot: {ended} || trench-cli: {cliEnded}");
            var result = end.GetValueOrDefault("result", "");
            var agree = ended.Contains("MATCH ENDED: draw") ? result.StartsWith("Draw") : ended.Contains("MATCH ENDED: One wins") ? result.StartsWith("You won") : ended.Contains("MATCH ENDED: Two wins") && result.StartsWith("You lost");
            Record("gvt-outcome-agrees", agree ? "PASS" : "FAIL", $"Godot (Seat One): {result} || trench-cli (Seat Two): {cliEnded}");
            Record("gvt-same-final-hash", hashGodot.Length == 64 && hashGodot == hashCli ? "PASS" : "FAIL", $"Godot: {hashGodot} || trench-cli: {hashCli}");
            Record("gvt-same-event-count", eventsGodot.Length > 0 && eventsGodot == eventsCli ? "PASS" : "FAIL", $"Godot: {eventsGodot} || trench-cli: {eventsCli}");
            // Every event both list (Godot shows the newest 200) has the same number and text.
            var shown = end.GetValueOrDefault("log", "").Split('|').Select(l => Regex.Match(l, @"^\[(\d+)\] (.*)$")).Where(m => m.Success)
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
            var printed = cliLines.Select(l => Regex.Match(l, @"^\s+\[(\d+)\] (.*)$")).Where(m => m.Success && shown.ContainsKey(m.Groups[1].Value)).ToList();
            var differ = printed.Where(m => shown[m.Groups[1].Value] != m.Groups[2].Value.Trim()).Select(m => $"[{m.Groups[1].Value}] cli \"{m.Groups[2].Value.Trim()}\" Godot \"{shown[m.Groups[1].Value]}\"").ToList();
            Record("gvt-same-event-text", printed.Count > 0 && differ.Count == 0 ? "PASS" : "FAIL",
                differ.Count == 0 ? $"{printed.Count} events trench-cli printed are in Godot's list with the same number and text" : string.Join("; ", differ.Take(5)));
            Record("gvt-commands", "INFO", $"Godot (Seat One): {end.GetValueOrDefault("answers")}");

            // The server's signed record, checked with trench-cli verify (the client never reads it). The first one written:
            // a rematch the client starts afterwards (QoL I4) is never played.
            var matches = Path.Combine(data, "matches");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            string? record = null;
            while (record is null && DateTime.UtcNow < deadline)
            {
                record = Directory.Exists(matches) ? Directory.GetFiles(matches, "*.json").OrderBy(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (record is null)
                {
                    await Task.Delay(200);
                }
            }
            if (record is null)
            {
                Record("gvt-record-verifies-completed", "FAIL", $"no record in {matches}");
                return;
            }
            var verify = await Run("gvt-verify", "dotnet", [cliDll, "verify", record, "--key", Path.Combine(data, "keys", "signing.pub")], 120);
            var verdict = verify.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            Record("gvt-record-verifies-completed", verify.Exit == 0 && verdict.StartsWith("Completed: ") ? "PASS" : "FAIL",
                $"trench-cli verify {Path.GetFileName(record)} --key signing.pub: exit {verify.Exit}: {verify.Output.Trim().Replace('\n', ' ')}");
            var finalHash = Regex.Match(verify.Output, @"Final hash ([0-9a-f]{64}): hash matches\.");
            Record("gvt-record-hash-matches", finalHash.Success && finalHash.Groups[1].Value == hashGodot && hashGodot == hashCli ? "PASS" : "FAIL",
                $"record: {(finalHash.Success ? finalHash.Groups[1].Value : "none")} || Godot: {hashGodot} || trench-cli: {hashCli}");
            using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(record)))
            {
                var count = json.RootElement.GetProperty("body").GetProperty("events").GetArrayLength().ToString();
                Record("gvt-record-event-count-matches", count == eventsGodot && count == eventsCli ? "PASS" : "FAIL",
                    $"record: {count} events || Godot: {eventsGodot} || trench-cli: {eventsCli}");
            }
            var said = Regex.Match(verdict, @"^Completed: Completed: (?:Seat (One|Two) won|a draw) \((\w+)\)\.$");
            var seen = Regex.Match(ended, @"MATCH ENDED: (?:(One|Two) wins|draw) \((\w+)\)\.$");
            Record("gvt-record-outcome-matches", said.Success && seen.Success && said.Groups[1].Value == seen.Groups[1].Value && said.Groups[2].Value == seen.Groups[2].Value ? "PASS" : "FAIL",
                $"record: {verdict} || clients: {ended}");
            Record("gvt-run-time", "INFO", $"{(DateTime.UtcNow - started).TotalSeconds:0} s from the server start to the record check");
        }
        catch (Exception e)
        {
            Record("gvt-runner", "FAIL", $"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Kill();
            }
            var log = Path.Combine(gvt, "gvt-a.log");
            if (File.Exists(log))
            {
                foreach (var line in ReadShared(log))
                {
                    var check = Regex.Match(line, @"CHECK (\S+) (PASS|FAIL) ?(.*)$");
                    if (check.Success)
                    {
                        Record($"gvt-a: {check.Groups[1].Value}", check.Groups[2].Value, check.Groups[3].Value);
                    }
                    else if (line.Contains("WAIT") && line.Contains("timed out"))
                    {
                        Record("gvt-a: wait", "INFO", line[13..]);
                    }
                }
            }
            var errors = EngineErrors(processes.Where(p => p.Name == "godot-gvt-a").SelectMany(p => p.Lines).ToList());
            Record("godot-gvt-a: engine errors", errors.Count == 0 ? "PASS" : "FAIL", errors.Count == 0 ? "none in the output" : string.Join(" | ", errors.Take(5)));
            var left = ServersUsing(data);
            Record("gvt-no-server-left", left.Count == 0 ? "PASS" : "FAIL", left.Count == 0 ? "no Match.Server process uses this match's data folder" : string.Join("; ", left));
        }
    }

    private static async Task PvpMatch(string godot)
    {
        var pvp = Path.Combine(_out, "pvp");
        Directory.CreateDirectory(pvp);
        var serverDll = Path.Combine(_repo, "src", "Match.Server", "bin", "Debug", "net10.0", "Match.Server.dll");
        var cliDll = Path.Combine(_repo, "src", "Match.Cli", "bin", "Debug", "net10.0", "trench-cli.dll");
        var port = FreePort();
        var data = Path.Combine(_out, "pvp-server-data");
        var url = $"ws://127.0.0.1:{port}/match";
        var homeB = Path.Combine(_out, "home-pvp-b");
        var started = DateTime.UtcNow;
        var server = Start("pvp-server", "dotnet", [serverDll, "--port", port.ToString(), "--data", data], null);
        var processes = new List<Child> { server };
        try
        {
            await server.WaitFor(l => l.Contains("Listening on"), ServerStartSeconds, "server start");
            var a = Start("godot-pvp-a", godot, GodotArgs("pvp-a", pvp, url, null, "melee"), Path.Combine(_out, "home-pvp-a"));
            processes.Add(a);
            var code = await Mark(pvp, "pvp-a", "code", 120);
            var b = Start("godot-pvp-b", godot, GodotArgs("pvp-b", pvp, url, code), homeB);
            processes.Add(b);

            // A hard kill (the whole process tree, no clean close) at the start of turn 2, whatever the client is doing.
            var seen = await Mark(pvp, "pvp-b", "turn-2", 900);
            b.Kill();
            Record("pvp-b-hard-killed", b.Process.HasExited && !ReadShared(Path.Combine(pvp, "pvp-b.log")).Any(l => l.Contains("MARK finished")) ? "PASS" : "FAIL",
                $"second Godot client killed at the start of turn 2 with {seen} events on its screen (exit code {b.ExitCode}; it never finished on its own)");
            await Mark(pvp, "pvp-a", "saw-pause", 60);
            var rejoin = Start("godot-pvp-b-rejoin", godot, GodotArgs("pvp-b-rejoin", pvp, url, code), homeB);
            processes.Add(rejoin);
            // Room for the 40 minutes the first window may play (UiCheck.PvpPlay) after the rejoin starts.
            Record("pvp-a-exit", await a.Exit(2700) == 0 ? "PASS" : "FAIL", $"first Godot client exit code {a.ExitCode} (0 = all its checks passed)");
            Record("pvp-b-rejoin-exit", await rejoin.Exit(120) == 0 ? "PASS" : "FAIL", $"rejoined Godot client exit code {rejoin.ExitCode}");

            // Both windows show the same end of the same match.
            var endA = Dumps(pvp, "pvp-a", "pvp-end");
            var endB = Dumps(pvp, "pvp-b-rejoin", "pvp-end");
            var hashA = endA.GetValueOrDefault("final_hash", "");
            var hashB = endB.GetValueOrDefault("final_hash", "");
            var logA = endA.GetValueOrDefault("log", "");
            var logB = endB.GetValueOrDefault("log", "");
            Record("pvp-both-same-final-hash", hashA.Length == 64 && hashA == hashB ? "PASS" : "FAIL", $"Seat One: {hashA} || Seat Two: {hashB}");
            Record("pvp-both-same-event-count", endA.GetValueOrDefault("events") is { } eventsA && int.TryParse(eventsA, out var countA) && countA > 0 && eventsA == endB.GetValueOrDefault("events") ? "PASS" : "FAIL",
                $"Seat One: {endA.GetValueOrDefault("events")} || Seat Two: {endB.GetValueOrDefault("events")}");
            Record("pvp-both-same-events", logA.Length > 0 && logA == logB ? "PASS" : "FAIL", $"{logA.Split('|').Length} event lines on Seat One, {logB.Split('|').Length} on Seat Two");
            // Seat One chose a unit, switched to another, then acted: Seat Two's window shows that one activation, of the unit
            // finally chosen, at the same event number (its screen before the kill holds turn 1; the end screen the newest 200).
            var chosen = Dumps(pvp, "pvp-a", "choice").GetValueOrDefault("activation", "");
            var seenByB = Dumps(pvp, "pvp-b", "before-kill").GetValueOrDefault("log", "").Split('|').Concat(logB.Split('|'));
            var looksA = endA.GetValueOrDefault("looks", "");
            Record("pvp-both-same-unit-looks", looksA.Length > 0 && looksA == endB.GetValueOrDefault("looks", "") ? "PASS" : "FAIL",
                $"Seat One: {looksA} || Seat Two: {endB.GetValueOrDefault("looks", "")}");
            // Visible dice rolls (QoL stage F2): a roll both windows showed has the same dice and result in both, and the window
            // that rejoined showed no roll from before it opened (those stay in its combat log).
            static Dictionary<string, string> DiceOf(string dump) => dump.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(':', 2)).Where(x => x.Length == 2).GroupBy(x => x[0]).ToDictionary(g => g.Key, g => string.Join(" & ", g.Select(x => x[1])));
            var diceA = DiceOf(endA.GetValueOrDefault("dice", ""));
            var diceB = DiceOf(endB.GetValueOrDefault("dice", ""));
            var both = diceA.Keys.Intersect(diceB.Keys).ToList();
            var differ = both.Where(k => diceA[k] != diceB[k]).Select(k => $"[{k}] {diceA[k]} || {diceB[k]}").ToList();
            Record("pvp-both-same-dice", both.Count > 0 && differ.Count == 0 ? "PASS" : "FAIL",
                differ.Count == 0 ? $"{both.Count} rolls shown in both windows, all alike (Seat One showed {diceA.Count}, Seat Two after rejoining {diceB.Count})" : string.Join("; ", differ.Take(5)));
            // The rejoined window catches up on the match so far without showing it: a range of events from 0 up to where it
            // rejoined (mid-match). None of its shown rolls may lie in a range it caught up on.
            var skippedB = endB.GetValueOrDefault("dice_skipped", "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Split('-')).Select(r => (From: int.Parse(r[0]), To: int.Parse(r[1]))).ToList();
            var replayedB = int.TryParse(endB.GetValueOrDefault("dice_replayed"), out var rb) ? rb : 0;
            var oldB = diceB.Keys.Select(k => int.Parse(k)).Where(k => k <= replayedB || skippedB.Any(r => k > r.From && k <= r.To)).ToList();
            var caughtUp = Math.Max(replayedB, skippedB.Select(r => r.To).DefaultIfEmpty(0).Max());
            Record("pvp-rejoin-shows-no-old-dice", caughtUp > 20 && diceB.Count > 0 && oldB.Count == 0 ? "PASS" : "FAIL",
                $"the rejoined window was sent the match again up to event {replayedB} and caught up on {string.Join(", ", skippedB.Select(r => $"{r.From + 1}-{r.To}"))} without showing them; it showed {diceB.Count} rolls, {oldB.Count} of them from before");
            // Both HUD lines at the end: the same turn, phase, objectives and scores (by seat letter since stage U2).
            var stripA = endA.GetValueOrDefault("strip", "");
            var stripB = endB.GetValueOrDefault("strip", "");
            var vpA = Regex.Match(stripA, @"VP A (\d+) – B (\d+)");
            var vpB = Regex.Match(stripB, @"VP A (\d+) – B (\d+)");
            // The HUD (proposal G3) starts with each window's own seat; the rest is shared.
            static string Shared(string strip) => string.Join("|", strip.Split(" · ").Where(p => !p.StartsWith("VP ", StringComparison.Ordinal) && !p.StartsWith('[')));
            Record("pvp-both-same-status-strip", vpA.Success && vpB.Success && vpA.Value == vpB.Value
                && Shared(stripA) == Shared(stripB) && stripA.Contains("objectives now: ", StringComparison.Ordinal)
                && stripA.StartsWith("[A] SEAT ONE", StringComparison.Ordinal) && stripB.StartsWith("[B] SEAT TWO", StringComparison.Ordinal) ? "PASS" : "FAIL",
                $"Seat One: {stripA} || Seat Two: {stripB}");
            Record("pvp-opponent-sees-only-chosen-unit", chosen.Contains(" One activates ", StringComparison.Ordinal) && seenByB.Contains(chosen) ? "PASS" : "FAIL",
                $"Seat One's activation after switching: {(chosen.Length > 0 ? chosen : "none")}; in Seat Two's window: {seenByB.Contains(chosen)}");
            var ended = endA.GetValueOrDefault("ended", "");
            var resultA = endA.GetValueOrDefault("result", "");
            var resultB = endB.GetValueOrDefault("result", "");
            var agree = ended.Contains("draw") ? resultA.StartsWith("Draw") && resultB.StartsWith("Draw")
                : ended.Contains("One wins") ? resultA.StartsWith("You won") && resultB.StartsWith("You lost")
                : ended.Contains("Two wins") && resultA.StartsWith("You lost") && resultB.StartsWith("You won");
            Record("pvp-both-agree-on-outcome", ended.Length > 0 && ended == endB.GetValueOrDefault("ended") && agree ? "PASS" : "FAIL", $"{ended} || Seat One: {resultA} || Seat Two: {resultB}");
            // Nothing seen before the kill was lost or changed, and nothing came twice: the events the rejoined client
            // caught up on start with exactly what the killed one showed (each list numbers its events consecutively).
            var beforeKill = Dumps(pvp, "pvp-b", "before-kill").GetValueOrDefault("log", "");
            var rejoined = Dumps(pvp, "pvp-b-rejoin", "rejoined").GetValueOrDefault("log", "");
            Record("pvp-no-events-lost-or-repeated-across-kill", beforeKill.Length > 0 && (rejoined == beforeKill || rejoined.StartsWith(beforeKill + "|", StringComparison.Ordinal)) ? "PASS" : "FAIL",
                $"the {beforeKill.Split('|').Length} events shown before the kill are the first of the {rejoined.Split('|').Length} shown after rejoining");
            Record("pvp-commands", "INFO", $"Seat One: {endA.GetValueOrDefault("answers")} || Seat Two after rejoining: {endB.GetValueOrDefault("answers")}");

            // The server's signed record, checked with trench-cli verify (the client never reads it). The first one written:
            // a rematch the client starts afterwards (QoL I4) is never played.
            var matches = Path.Combine(data, "matches");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            string? record = null;
            while (record is null && DateTime.UtcNow < deadline)
            {
                record = Directory.Exists(matches) ? Directory.GetFiles(matches, "*.json").OrderBy(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (record is null)
                {
                    await Task.Delay(200);
                }
            }
            if (record is null)
            {
                Record("pvp-record-verifies-completed", "FAIL", $"no record in {matches}");
                return;
            }
            var verify = await Run("pvp-verify", "dotnet", [cliDll, "verify", record, "--key", Path.Combine(data, "keys", "signing.pub")], 120);
            var verdict = verify.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            Record("pvp-record-verifies-completed", verify.Exit == 0 && verdict.StartsWith("Completed: ") ? "PASS" : "FAIL",
                $"trench-cli verify {Path.GetFileName(record)} --key signing.pub: exit {verify.Exit}: {verify.Output.Trim().Replace('\n', ' ')}");
            var finalHash = Regex.Match(verify.Output, @"Final hash ([0-9a-f]{64}): hash matches\.");
            Record("pvp-record-hash-matches-both-clients", finalHash.Success && finalHash.Groups[1].Value == hashA && hashA == hashB ? "PASS" : "FAIL",
                $"record: {(finalHash.Success ? finalHash.Groups[1].Value : "none")} || Seat One: {hashA} || Seat Two: {hashB}");
            using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(record)))
            {
                var body = json.RootElement.GetProperty("body");
                var count = body.GetProperty("events").GetArrayLength();
                Record("pvp-record-event-count-matches-both-clients", count.ToString() == endA.GetValueOrDefault("events") && count.ToString() == endB.GetValueOrDefault("events") ? "PASS" : "FAIL",
                    $"record: {count} events || Seat One: {endA.GetValueOrDefault("events")} || Seat Two: {endB.GetValueOrDefault("events")}");
            }
            // "Completed: Seat One won (Reason)." and "MATCH ENDED: One wins (Reason)." name the same result.
            var said = Regex.Match(verdict, @"^Completed: Completed: (?:Seat (One|Two) won|a draw) \((\w+)\)\.$");
            var shown = Regex.Match(ended, @"MATCH ENDED: (?:(One|Two) wins|draw) \((\w+)\)\.$");
            Record("pvp-record-outcome-matches", said.Success && shown.Success && said.Groups[1].Value == shown.Groups[1].Value && said.Groups[2].Value == shown.Groups[2].Value ? "PASS" : "FAIL",
                $"record: {verdict} || clients: {ended}");
            Record("pvp-run-time", "INFO", $"{(DateTime.UtcNow - started).TotalSeconds:0} s from the server start to the record check");
        }
        catch (Exception e)
        {
            Record("pvp-runner", "FAIL", $"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Kill();
            }
            foreach (var role in new[] { "pvp-a", "pvp-b", "pvp-b-rejoin" })
            {
                var log = Path.Combine(pvp, $"{role}.log");
                if (File.Exists(log))
                {
                    foreach (var line in ReadShared(log))
                    {
                        var check = Regex.Match(line, @"CHECK (\S+) (PASS|FAIL) ?(.*)$");
                        if (check.Success)
                        {
                            Record($"{role}: {check.Groups[1].Value}", check.Groups[2].Value, check.Groups[3].Value);
                        }
                        else if (line.Contains("WAIT") && line.Contains("timed out"))
                        {
                            Record($"{role}: wait", "INFO", line[13..]);
                        }
                    }
                }
            }
            foreach (var process in processes.Where(p => p.Name.StartsWith("godot", StringComparison.Ordinal)))
            {
                var errors = EngineErrors(process.Lines);
                Record($"{process.Name}: engine errors", errors.Count == 0 ? "PASS" : "FAIL", errors.Count == 0 ? "none in the output" : string.Join(" | ", errors.Take(5)));
            }
            var left = ServersUsing(data);
            Record("pvp-no-server-left", left.Count == 0 ? "PASS" : "FAIL", left.Count == 0 ? "no Match.Server process uses this match's data folder" : string.Join("; ", left));
        }
    }

    private static async Task HotSeatMines(string godot)
    {
        var e2e = Path.Combine(_out, "e2e");
        Directory.CreateDirectory(e2e);
        var home = Path.Combine(_out, "home-hotseat-mines");
        var args = new List<string> { "--audio-driver", "Dummy" };
        args.AddRange(_renderer);
        args.AddRange(["--path", Path.Combine(_repo, "client"), "--", "--ui-check", "hotseat-mines", "--out", e2e, "--scenario", "ranged-1v1-trench-warfare"]);
        var hot = Start("godot-hotseat-mines", godot, [.. args], home);
        try
        {
            Record("hotseat-mines-exit", await hot.Exit(600) == 0 ? "PASS" : "FAIL", $"hot-seat Godot exit code {hot.ExitCode} (0 = all its checks passed)");
        }
        catch (Exception e)
        {
            Record("hotseat-mines-exit", "FAIL", $"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            hot.Kill();
            var log = Path.Combine(e2e, "hotseat-mines.log");
            if (File.Exists(log))
            {
                foreach (var line in File.ReadAllLines(log))
                {
                    var check = Regex.Match(line, @"CHECK (\S+) (PASS|FAIL) ?(.*)$");
                    if (check.Success)
                    {
                        Record($"hotseat-mines: {check.Groups[1].Value}", check.Groups[2].Value, check.Groups[3].Value);
                    }
                }
            }
            var errors = EngineErrors(hot.Lines);
            Record("godot-hotseat-mines: engine errors", errors.Count == 0 ? "PASS" : "FAIL", errors.Count == 0 ? "none in the output" : string.Join(" | ", errors.Take(5)));
            var left = ServersUsing(Path.Combine(home, "hotseat-server"));
            Record("hotseat-mines-no-server-left", left.Count == 0 ? "PASS" : "FAIL", left.Count == 0 ? "no Match.Server process uses the hot-seat folder" : string.Join("; ", left));
        }
    }

    private static async Task HotSeat(string godot)
    {
        var e2e = Path.Combine(_out, "e2e");
        Directory.CreateDirectory(e2e);
        var home = Path.Combine(_out, "home-hotseat");
        var args = new List<string> { "--audio-driver", "Dummy" };
        args.AddRange(_renderer);
        args.AddRange(["--path", Path.Combine(_repo, "client"), "--", "--ui-check", "hotseat", "--out", e2e, "--scenario", "ranged"]);
        var hot = Start("godot-hotseat", godot, [.. args], home);
        try
        {
            // Stage N5: the window plays a whole match, both seats, before leaving.
            Record("hotseat-exit", await hot.Exit(1500) == 0 ? "PASS" : "FAIL", $"hot-seat Godot exit code {hot.ExitCode} (0 = all its checks passed)");
        }
        catch (Exception e)
        {
            Record("hotseat-exit", "FAIL", $"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            hot.Kill();
            var log = Path.Combine(e2e, "hotseat.log");
            if (File.Exists(log))
            {
                foreach (var line in File.ReadAllLines(log))
                {
                    var check = Regex.Match(line, @"CHECK (\S+) (PASS|FAIL) ?(.*)$");
                    if (check.Success)
                    {
                        Record($"hotseat: {check.Groups[1].Value}", check.Groups[2].Value, check.Groups[3].Value);
                    }
                }
            }
            var errors = EngineErrors(hot.Lines);
            Record("godot-hotseat: engine errors", errors.Count == 0 ? "PASS" : "FAIL", errors.Count == 0 ? "none in the output" : string.Join(" | ", errors.Take(5)));
            // The window stopped its own server: no server process is left that uses this run's hot-seat folder.
            var left = ServersUsing(Path.Combine(home, "hotseat-server"));
            Record("hotseat-no-server-left", left.Count == 0 ? "PASS" : "FAIL", left.Count == 0 ? "no Match.Server process uses the hot-seat folder" : string.Join("; ", left));
        }

        // Stage N5: the local server's signed record of the match played in the one window, checked with trench-cli
        // verify (the client never reads it), and its final hash is the one the window showed.
        var data = Path.Combine(home, "hotseat-server");
        var matches = Path.Combine(data, "matches");
        var record = Directory.Exists(matches) ? Directory.GetFiles(matches, "*.json").OrderBy(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        var cliDll = Path.Combine(_repo, "src", "Match.Cli", "bin", "Debug", "net10.0", "trench-cli.dll");
        if (record is null || !File.Exists(cliDll))
        {
            Record("hotseat-record-verifies", "FAIL", record is null ? $"no record in {matches}" : $"{cliDll} not built");
            return;
        }
        var verify = await Run("hotseat-verify", "dotnet", [cliDll, "verify", record, "--key", Path.Combine(data, "keys", "signing.pub")], 120);
        var verdict = verify.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        var shownHash = Dumps(e2e, "hotseat", "hotseat-end").GetValueOrDefault("final_hash", "");
        var finalHash = Regex.Match(verify.Output, @"Final hash ([0-9a-f]{64}): hash matches\.");
        Record("hotseat-record-verifies", verify.Exit == 0 && verdict.StartsWith("Completed: ") && finalHash.Success && finalHash.Groups[1].Value == shownHash ? "PASS" : "FAIL",
            $"trench-cli verify {Path.GetFileName(record)} --key signing.pub: exit {verify.Exit}: {verify.Output.Trim().Replace('\n', ' ')} || the window showed {(shownHash.Length == 0 ? "no final hash" : shownHash)}");
    }

    // Running server processes whose command line names the folder (Linux reads /proc; Windows asks WMI through PowerShell).
    private static List<string> ServersUsing(string folder)
    {
        var found = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("powershell", ["-NoProfile", "-Command",
                "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*Match.Server.dll*' } | ForEach-Object { \"$($_.ProcessId) $($_.CommandLine)\" }"])
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var ps = Process.Start(psi)!;
            var text = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit();
            found.AddRange(text.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(folder, StringComparison.OrdinalIgnoreCase)));
        }
        else if (Directory.Exists("/proc"))
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                try
                {
                    var cmd = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ');
                    if (cmd.Contains("Match.Server.dll", StringComparison.Ordinal) && cmd.Contains(folder, StringComparison.Ordinal))
                    {
                        found.Add($"{Path.GetFileName(dir)} {cmd.Trim()}");
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return found;
    }

    // How long a test server may take to say it is listening. Run 304 (2026-10-26) timed out at 60 s with no output at
    // all from the server, on a container started minutes before; the same server started by hand at once afterwards.
    private const int ServerStartSeconds = 120;

    private static async Task EndToEnd(string godot)
    {
        var e2e = Path.Combine(_out, "e2e");
        Directory.CreateDirectory(e2e);
        var serverDll = Path.Combine(_repo, "src", "Match.Server", "bin", "Debug", "net10.0", "Match.Server.dll");
        var cliDll = Path.Combine(_repo, "src", "Match.Cli", "bin", "Debug", "net10.0", "trench-cli.dll");
        var port = FreePort();
        var data = Path.Combine(_out, "server-data");
        var server = Start("server", "dotnet", [serverDll, "--port", port.ToString(), "--data", data], null);
        var serverStarting = DateTime.UtcNow;
        await server.WaitFor(l => l.Contains("Listening on"), ServerStartSeconds, "server start");
        Record("server-start-time", "INFO", $"{(DateTime.UtcNow - serverStarting).TotalSeconds:0.0} s from starting the server to \"Listening on\"");
        using var proxyA = new Proxy(port);
        using var proxyB = new Proxy(port);
        var urlA = $"ws://127.0.0.1:{proxyA.Port}/match";
        var urlB = $"ws://127.0.0.1:{proxyB.Port}/match";
        var homeA = Path.Combine(_out, "home-a");
        var homeB = Path.Combine(_out, "home-b");
        var processes = new List<Child> { server };
        try
        {
            var host = Start("godot-host", godot, GodotArgs("host", e2e, urlA, null), homeA);
            processes.Add(host);
            var code = await Mark(e2e, "host", "code", 120);
            var guest = Start("godot-guest", godot, GodotArgs("guest", e2e, urlB, code), homeB);
            processes.Add(guest);
            await Mark(e2e, "host", "joined", 120);
            await Mark(e2e, "guest", "joined", 120);
            var decider = await FirstMark(e2e, ["host", "guest"], "decider", 60);
            proxyA.Hold();
            proxyB.Hold();
            Signal(e2e, "hold");
            await Mark(e2e, decider, "sent", 60);
            proxyA.Release();
            proxyB.Release();
            await Mark(e2e, "host", "zone-done", 60);
            await Mark(e2e, "guest", "zone-done", 60);

            // Close the second client (it saved its seat), let trench-cli take both seats and play, then stop it.
            Signal(e2e, "close");
            var guestExit = await guest.Exit(60);
            Record("guest-closes-cleanly", guestExit == 0 ? "PASS" : "FAIL", $"second Godot client exit code {guestExit}");
            var cliB = Start("cli-b-1", "dotnet", [cliDll, "--server", urlB, "rejoin", code, "--seat", "Two", "--bot", "7"], homeB);
            await cliB.WaitFor(l => l.Contains("You are Seat Two"), 60, "trench-cli rejoins Seat Two");
            var cliA = Start("cli-a", "dotnet", [cliDll, "--server", urlA, "rejoin", code, "--seat", "One", "--bot", "3"], homeA);
            processes.AddRange([cliB, cliA]);
            var ended = new Regex(@"\]\s+a\d ends its activation");
            await cliA.WaitFor(l => cliA.Lines.Count(x => ended.IsMatch(x)) >= 2 || l.Contains("MATCH ENDED"), 180, "trench-cli plays two activations for Seat One");
            cliA.Kill();
            cliB.Kill();
            var cliEvents = cliA.Lines.Where(l => Regex.IsMatch(l, @"^\s+\[\d+\]")).ToList();
            Record("cli-played", cliEvents.Count(l => l.Contains(" sets up ")) == 8 ? "PASS" : "FAIL",
                $"trench-cli (both seats, bots) deployed {cliEvents.Count(l => l.Contains(" sets up "))} models and showed {cliEvents.Count} events");
            Signal(e2e, "takeback");
            await Mark(e2e, "host", "took-back", 120);

            var cliB2 = Start("cli-b-2", "dotnet", [cliDll, "--server", urlB, "rejoin", code, "--seat", "Two", "--bot", "7"], homeB);
            processes.Add(cliB2);
            await Mark(e2e, "host", "played", 300);
            cliB2.Kill();

            // Reopen the second client: it should list its saved seat and rejoin Seat Two.
            var reopen = Start("godot-reopen", godot, GodotArgs("reopen", e2e, urlB, code), homeB);
            processes.Add(reopen);
            Signal(e2e, "compare");
            await Mark(e2e, "host", "compared", 120);
            await Mark(e2e, "reopen", "compared", 120);
            Compare(Dumps(e2e, "host", "compare"), Dumps(e2e, "reopen", "compare"));
            SameAsCli(cliEvents, Dumps(e2e, "host", "compare").GetValueOrDefault("log", ""));

            // Network interruption for the host only.
            proxyA.Cut();
            Signal(e2e, "cut");
            await Mark(e2e, "host", "gave-up", 120);
            proxyA.Restore();
            Signal(e2e, "restored");
            await Mark(e2e, "host", "rejoined", 120);
            await Mark(e2e, "host", "a11y-done", 300);

            // The server stops and starts again on the same port and data.
            server.Kill();
            Signal(e2e, "server-stopped");
            await Mark(e2e, "host", "server-stop-seen", 60);
            await Mark(e2e, "reopen", "server-stop-seen", 60);
            var restarted = Start("server-2", "dotnet", [serverDll, "--port", port.ToString(), "--data", data], null);
            processes.Add(restarted);
            await restarted.WaitFor(l => l.Contains("Listening on"), 60, "server restart");
            Record("server-restart-recovery", "INFO",
                string.Join(" ", restarted.Lines.Where(l => l.Contains("Recovered") || l.Contains("Listening"))));
            Signal(e2e, "server-started");
            // The windows use the clipboard one after the other (Windows lets one program open it at a time).
            await Mark(e2e, "host", "final-hash-copied", 120);
            Signal(e2e, "host-copied");
            Record("host-exit", await host.Exit(240) == 0 ? "PASS" : "FAIL", $"host Godot exit code {host.ExitCode} (0 = all its checks passed)");
            Record("reopen-exit", await reopen.Exit(120) == 0 ? "PASS" : "FAIL", $"reopened Godot exit code {reopen.ExitCode}");
            // M7.5 stage 5a: both windows lost the same match at the same event, so they show the same final state hash.
            var hostHash = Dumps(e2e, "host", "server-restart").GetValueOrDefault("final_hash", "");
            var reopenHash = Dumps(e2e, "reopen", "server-restart").GetValueOrDefault("final_hash", "");
            Record("both-clients-same-final-hash", hostHash.Length == 64 && hostHash == reopenHash ? "PASS" : "FAIL", $"host: {hostHash} || reopened: {reopenHash}");
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Kill();
            }
            foreach (var role in new[] { "host", "guest", "reopen" })
            {
                var log = Path.Combine(e2e, $"{role}.log");
                if (File.Exists(log))
                {
                    foreach (var line in File.ReadAllLines(log))
                    {
                        var check = Regex.Match(line, @"CHECK (\S+) (PASS|FAIL) ?(.*)$");
                        if (check.Success)
                        {
                            Record($"{role}: {check.Groups[1].Value}", check.Groups[2].Value, check.Groups[3].Value);
                        }
                        else if (line.Contains("WAIT") && line.Contains("timed out"))
                        {
                            Record($"{role}: wait", "INFO", line[13..]);
                        }
                    }
                }
            }
            foreach (var process in processes.Where(p => p.Name.StartsWith("godot", StringComparison.Ordinal)))
            {
                var errors = EngineErrors(process.Lines);
                Record($"{process.Name}: engine errors", errors.Count == 0 ? "PASS" : "FAIL", errors.Count == 0 ? "none in the output" : string.Join(" | ", errors.Take(5)));
            }
        }
    }

    // Both clients must show the same match: the same models and events, and the same seats and VP.
    private static void Compare(Dictionary<string, string> host, Dictionary<string, string> reopen)
    {
        Record("both-clients-same-models", host.GetValueOrDefault("models") is { Length: > 0 } m && m == reopen.GetValueOrDefault("models") ? "PASS" : "FAIL",
            $"host: {host.GetValueOrDefault("models")} || reopened: {reopen.GetValueOrDefault("models")}");
        Record("both-clients-same-events", host.GetValueOrDefault("log") is { Length: > 0 } l && l == reopen.GetValueOrDefault("log") ? "PASS" : "FAIL",
            $"{host.GetValueOrDefault("log", "").Split('|').Length} event lines on the host, {reopen.GetValueOrDefault("log", "").Split('|').Length} on the reopened client");
        static string Seats(string match) => string.Join("|", match.Split('|').Where(x => Regex.IsMatch(x, @"^Seat (One|Two) \[") || x.StartsWith("Turn ", StringComparison.Ordinal)).Select(x => x.Replace(" (you)", "")));
        var a = Seats(host.GetValueOrDefault("match", ""));
        var b = Seats(reopen.GetValueOrDefault("match", ""));
        Record("both-clients-same-seats-vp-turn", a.Length > 0 && a == b ? "PASS" : "FAIL", $"host: {a} || reopened: {b}");
        Record("letters-match-seats", host.GetValueOrDefault("code", "").Contains("Seat One [A]") && reopen.GetValueOrDefault("code", "").Contains("Seat Two [B]") ? "PASS" : "FAIL",
            $"{host.GetValueOrDefault("code")} || {reopen.GetValueOrDefault("code")}");
        var whoseA = host.GetValueOrDefault("match", "").Split('|')[0];
        var whoseB = reopen.GetValueOrDefault("match", "").Split('|')[0];
        Record("whose-move-agrees", (whoseA.Contains("Your move") && whoseB.Contains("Opponent's move (Seat One)")) || (whoseB.Contains("Your move") && whoseA.Contains("Opponent's move (Seat Two)"))
            || (whoseA == whoseB && !whoseA.Contains("move")) ? "PASS" : "FAIL", $"host: {whoseA} || reopened: {whoseB}");
    }

    // Every event trench-cli printed while it played both seats appears in Godot with the same number and text.
    private static void SameAsCli(List<string> cliEvents, string godotLog)
    {
        var godot = godotLog.Split('|').Select(l => Regex.Match(l, @"^\[(\d+)\] (.*)$")).Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var differ = cliEvents.Select(l => Regex.Match(l, @"^\s+\[(\d+)\] (.*)$")).Where(m => m.Success)
            .Where(m => godot.GetValueOrDefault(m.Groups[1].Value) != m.Groups[2].Value.Trim())
            .Select(m => $"[{m.Groups[1].Value}] cli \"{m.Groups[2].Value.Trim()}\" Godot \"{godot.GetValueOrDefault(m.Groups[1].Value)}\"").ToList();
        Record("godot-shows-cli-events", cliEvents.Count > 0 && differ.Count == 0 ? "PASS" : "FAIL",
            differ.Count == 0 ? $"all {cliEvents.Count} events trench-cli printed are in Godot with the same number and text" : string.Join("; ", differ.Take(5)));
    }

    private static string[] GodotArgs(string role, string e2e, string url, string? code, string? scenario = null)
    {
        var args = new List<string> { "--audio-driver", "Dummy" };
        args.AddRange(_renderer);
        args.AddRange(["--path", Path.Combine(_repo, "client"), "--", "--ui-check", role, "--out", e2e, "--server", url]);
        if (code is not null)
        {
            args.AddRange(["--code", code]);
        }
        if (scenario is not null)
        {
            args.AddRange(["--scenario", scenario]);
        }
        return [.. args];
    }

    // ---- marks and signals ----

    private static async Task<string> Mark(string e2e, string role, string name, double seconds)
    {
        var path = Path.Combine(e2e, $"{role}.log");
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var pattern = new Regex($@"^\S+ MARK {Regex.Escape(name)}(?: (.*))?$");
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                foreach (var line in ReadShared(path))
                {
                    var match = pattern.Match(line);
                    if (match.Success)
                    {
                        Console.WriteLine($"  {role}: {name}");
                        return match.Groups[1].Value;
                    }
                    if (line.Contains("MARK finished"))
                    {
                        throw new InvalidOperationException($"{role} finished before '{name}' (see {path})");
                    }
                }
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"{role} did not reach '{name}' in {seconds}s (see {path})");
    }

    private static async Task<string> FirstMark(string e2e, string[] roles, string name, double seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var role in roles)
            {
                var path = Path.Combine(e2e, $"{role}.log");
                if (File.Exists(path) && ReadShared(path).Any(l => l.Contains($" MARK {name}")))
                {
                    return role;
                }
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"no client reached '{name}'");
    }

    private static Dictionary<string, string> Dumps(string e2e, string role, string tag)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in ReadShared(Path.Combine(e2e, $"{role}.log")))
        {
            var match = Regex.Match(line, $@"^\S+ DUMP {Regex.Escape(tag)} (\w+)=(.*)$");
            if (match.Success)
            {
                result[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }
        return result;
    }

    private static void Signal(string e2e, string name)
    {
        File.WriteAllText(Path.Combine(e2e, $"signal-{name}"), DateTime.UtcNow.ToString("O"));
        Console.WriteLine($"  signal {name}");
    }

    private static string[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    // ---- processes ----

    private static async Task<(int Exit, string Output)> Run(string name, string file, string[] args, double timeout)
    {
        var child = Start(name, file, args, null);
        var exit = await child.Exit(timeout);
        return (exit, string.Join("\n", child.Lines));
    }

    private static Child Start(string name, string file, string[] args, string? home)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        if (home is not null)
        {
            info.Environment["TRENCH_SIM_HOME"] = home;
        }
        Console.WriteLine($"  start {name}: {file} {string.Join(' ', args)}");
        return new Child(name, Process.Start(info)!, Path.Combine(_out, "logs", $"{name}.txt"));
    }

    private sealed class Child
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];
        private readonly StreamWriter _log;
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Child(string name, Process process, string log)
        {
            Name = name;
            Process = process;
            _log = new StreamWriter(log) { AutoFlush = true };
            process.OutputDataReceived += (_, e) => Add(e.Data);
            process.ErrorDataReceived += (_, e) => Add(e.Data is null ? null : "stderr: " + e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public string Name { get; }

        public Process Process { get; }

        public int? ExitCode => Process.HasExited ? Process.ExitCode : null;

        public List<string> Lines
        {
            get
            {
                lock (_gate)
                {
                    return [.. _lines];
                }
            }
        }

        private void Add(string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (_gate)
            {
                _lines.Add(line);
                _log.WriteLine(line);
                _changed.TrySetResult();
                _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public async Task WaitFor(Func<string, bool> condition, double seconds, string what)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    if (_lines.Any(condition))
                    {
                        return;
                    }
                    changed = _changed.Task;
                }
                if (Process.HasExited)
                {
                    throw new InvalidOperationException($"{Name} exited ({Process.ExitCode}) before: {what}");
                }
                try
                {
                    await changed.WaitAsync(TimeSpan.FromSeconds(1), timeout.Token);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"{Name}: {what} did not happen in {seconds}s");
                }
            }
        }

        public async Task<int> Exit(double seconds)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                await Process.WaitForExitAsync(timeout.Token);
                return Process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                Kill();
                return -1;
            }
        }

        public void Kill()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(10_000);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    // ---- a TCP proxy between a client and the server that can hold the client's traffic or cut it ----

    private sealed class Proxy : IDisposable
    {
        private readonly int _target;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _open = [];
        private readonly object _gate = new();
        private TaskCompletionSource _released = Completed();
        private bool _cut;

        public Proxy(int target)
        {
            _target = target;
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        // Holds what clients send until Release: their commands do not reach the server in the meantime.
        public void Hold()
        {
            lock (_gate)
            {
                _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                _released.TrySetResult();
            }
        }

        // Closes every connection and refuses new ones until Restore.
        public void Cut()
        {
            lock (_gate)
            {
                _cut = true;
                foreach (var client in _open)
                {
                    client.Client.Close(0);
                }
                _open.Clear();
            }
        }

        public void Restore()
        {
            lock (_gate)
            {
                _cut = false;
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            Cut();
        }

        private async Task AcceptAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException)
                {
                    return;
                }
                _ = ServeAsync(client);
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            lock (_gate)
            {
                if (_cut)
                {
                    client.Client.Close(0);
                    return;
                }
            }
            var server = new TcpClient();
            try
            {
                await server.ConnectAsync(IPAddress.Loopback, _target);
            }
            catch (SocketException)
            {
                client.Client.Close(0);
                return;
            }
            lock (_gate)
            {
                _open.Add(client);
                _open.Add(server);
            }
            await Task.WhenAny(Pump(client.GetStream(), server.GetStream(), hold: true), Pump(server.GetStream(), client.GetStream(), hold: false));
            client.Client.Close(0);
            server.Client.Close(0);
        }

        private async Task Pump(Stream from, Stream to, bool hold)
        {
            var buffer = new byte[16384];
            try
            {
                while (true)
                {
                    var read = await from.ReadAsync(buffer);
                    if (read == 0)
                    {
                        return;
                    }
                    if (hold)
                    {
                        Task released;
                        lock (_gate)
                        {
                            released = _released.Task;
                        }
                        await released;
                    }
                    await to.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
            {
            }
        }

        private static TaskCompletionSource Completed()
        {
            var done = new TaskCompletionSource();
            done.SetResult();
            return done;
        }
    }

    // ---- helpers ----

    private static void Record(string id, string result, string detail)
    {
        lock (Results)
        {
            Results.Add((id, result, detail));
        }
        Console.WriteLine($"{result,-5} {id}: {(detail.Length > 300 ? detail[..300] + "…" : detail)}");
    }

    private static int WriteSummary()
    {
        var text = new StringBuilder();
        text.AppendLine("# M7.3 automated UI check");
        text.AppendLine();
        text.AppendLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Logs and screenshots are in this folder (logs/, e2e/).");
        text.AppendLine();
        text.AppendLine("| Result | Check | Detail |");
        text.AppendLine("|---|---|---|");
        foreach (var (id, result, detail) in Results)
        {
            text.AppendLine($"| {result} | {id} | {detail.Replace("|", "\\|").Replace("\n", " ")} |");
        }
        var failed = Results.Count(r => r.Result == "FAIL");
        text.AppendLine();
        text.AppendLine($"{Results.Count(r => r.Result == "PASS")} passed, {failed} failed.");
        File.WriteAllText(Path.Combine(_out, "summary.md"), text.ToString());
        Console.WriteLine($"{Results.Count(r => r.Result == "PASS")} passed, {failed} failed. Summary: {Path.Combine(_out, "summary.md")}");
        return failed == 0 ? 0 : 1;
    }

    private static string Summary(string output) =>
        string.Join(", ", output.Split('\n').Select(l => l.Trim()).Where(l => Regex.IsMatch(l, @"^\d+ (Warning|Error)\(s\)$")));

    private static string Errors(string output)
    {
        var errors = EngineErrors(output.Split('\n').ToList());
        var audio = output.Contains("falling back to the dummy driver") ? "; no audio device, Godot used its silent audio driver (known, harmless)" : "";
        return (errors.Count == 0 ? "; no errors in the output" : "; " + string.Join(" | ", errors.Take(5))) + audio;
    }

    // Engine error lines, except the one Godot prints when the machine has no audio output device: it then uses its
    // silent audio driver (ERR_CANT_OPEN at init_output_device, then "falling back to the dummy driver").
    private static List<string> EngineErrors(List<string> lines)
    {
        var errors = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!(lines[i].Contains("ERROR") || lines[i].Contains("SCRIPT ERROR") || lines[i].Contains("Unhandled exception")))
            {
                continue;
            }
            // The "at:" line can come after a blank line when stdout and stderr interleave.
            var next = lines.Skip(i + 1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
            if (lines[i].Contains("ERR_CANT_OPEN") && next.Contains("init_output_device") && next.Contains("audio_driver"))
            {
                continue;
            }
            errors.Add(lines[i].Trim());
        }
        return [.. errors.Distinct()];
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepository()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TrenchSim.sln")))
                {
                    return dir.FullName;
                }
            }
        }
        throw new InvalidOperationException("Run this from inside the Trench Sim repository.");
    }

    private static string? Arg(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
