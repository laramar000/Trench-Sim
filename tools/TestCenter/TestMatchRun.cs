using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestCenter;

// "Play a test match": a local server and two client windows that connect on their own (window A creates the match,
// window B joins it with the code), each with its own saved-seat folder inside this run's folder, so the player's own
// saved seats are never touched. The player plays both sides with the mouse. A window closed during the match is
// reopened and takes its seat back from its saved seat. When the match is over, the server's signed record is found by
// the match's id and checked against what both windows showed. Everything this run started is stopped at the end.
public static partial class TestMatchRun
{
    public sealed record Options(string Scenario, bool OpenGl, string[] ExtraA, string[] ExtraB);

    private sealed class Window(string name, string seat, string home, string report)
    {
        public string Name { get; } = name;

        public string Seat { get; } = seat;

        public string Home { get; } = home;

        public string ReportPath { get; } = report;

        public Processes.Child? Child { get; set; }

        public int Opened { get; set; }

        public List<string> Logs { get; } = [];
    }

    public static async Task<int> Run(Setup setup, Options options)
    {
        var report = setup.NewReport("Trench Sim test match", "match");
        report.Fact("Scenario", options.Scenario);
        setup.StopLeftovers(report);
        Step("Checking .NET and Godot");
        if (await setup.DotNet(report) is null || await setup.Godot(report) is not { } godot)
        {
            report.Missed("Test match", "not started: see above");
            return report.Finish();
        }
        var ownSeats = OwnSavedSeats();
        var server = (Processes.Child?)null;
        var windows = new List<Window>();
        var stopped = false;
        try
        {
            Step("Preparing the game (building the server, trench-cli and the client)");
            if (!await Build(setup, report, godot))
            {
                return report.Finish();
            }

            Step("Starting a local test server");
            var data = Path.Combine(report.Folder, "server-data");
            var serverDll = Path.Combine(setup.Repository, "src", "Match.Server", "bin", "Debug", "net10.0", "Match.Server.dll");
            server = setup.Processes.Start("server", "dotnet", [serverDll, "--port", "0", "--data", data], report.Logs);
            var listening = await server.WaitFor(l => l.Contains("Listening on ", StringComparison.Ordinal), 60);
            var url = listening is null ? null : Regex.Match(listening, @"Listening on (\S+)").Groups[1].Value;
            report.Check("A local test server started", url is { Length: > 0 }, url is null
                ? $"the server did not start (see {server.LogPath}): {string.Join(" | ", server.Lines.TakeLast(3))}"
                : $"{url}, data in {data}");
            if (url is null)
            {
                return report.Finish();
            }

            Step("Opening window A (Seat One), which creates the match");
            var a = new Window("A", "One", Path.Combine(report.Folder, "home-a"), Path.Combine(report.Folder, "window-a-result.txt"));
            var b = new Window("B", "Two", Path.Combine(report.Folder, "home-b"), Path.Combine(report.Folder, "window-b-result.txt"));
            windows.AddRange([a, b]);
            Open(setup, report, godot, options, a, url, "create", null, options.ExtraA);
            var seatA = await SavedSeat(a, null, 120);
            if (seatA is null)
            {
                report.Fail("Both windows connected to the same match on their own", $"window A did not create a match (see {a.Child!.LogPath})");
                return report.Finish();
            }
            var (code, matchId) = seatA.Value;
            Step($"Opening window B (Seat Two), which joins match {code}");
            Open(setup, report, godot, options, b, url, "join", code, options.ExtraB);
            var joined = await SavedSeat(b, matchId, 120);
            report.Check("Both windows connected to the same match on their own", joined is not null,
                joined is null ? $"window B did not join match {code} (see {b.Child!.LogPath})" : $"match {code} (id {matchId}): window A is Seat One, window B is Seat Two");
            if (joined is null)
            {
                return report.Finish();
            }
            report.Fact("Match", $"{code} (id {matchId})");

            await CopyCodeCheck(setup, report, code);
            Instructions(setup);

            var record = Path.Combine(data, "matches", matchId + ".json");
            var closedDuringMatch = new List<string>();
            stopped = !await PlayUntilOver(setup, report, godot, options, windows, url, code, record, closedDuringMatch);
            if (stopped)
            {
                report.Missed("The match was played to the end", "you stopped the test before the match ended");
                return report.Finish();
            }

            // Both windows write their end-of-match report as soon as they show the result.
            Step("The match is over. Checking the result");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && windows.Any(w => !File.Exists(w.ReportPath) && w.Child is { HasExited: false }))
            {
                await Task.Delay(250);
            }
            report.Pass("The server's signed record was found by the match's id", record);
            var ends = new List<MatchCheck.ClientEnd>();
            foreach (var w in windows)
            {
                if (MatchCheck.ReadClient(w.ReportPath, w.Name) is { } end)
                {
                    ends.Add(end);
                    report.Check($"Window {w.Name} shows the result", end.Seat == w.Seat && end.Hash.Length == 64 && end.Events > 0,
                        $"{MatchCheck.SeatName(end.Seat)}: {end.Result} {end.Events} events, final hash {end.Hash}");
                }
                else
                {
                    report.Fail($"Window {w.Name} shows the result", "it wrote no end-of-match report (it was closed before showing the result, or it failed; see its log)");
                }
            }
            await MatchCheck.Verify(setup, report, record, ends);

            if (closedDuringMatch.Count > 0)
            {
                var closed = windows.Where(w => w.Opened > 1).Select(w => w.Name).ToList();
                var rejoined = closed.Count > 0 && closed.All(name => ends.Any(e => e.Window == name));
                report.Check("A window closed during the match came back and took its seat again", rejoined,
                    $"{string.Join("; ", closedDuringMatch)} closed during the match and reopened from its saved seat; " +
                    (rejoined ? "it showed the same final result as the record" : "it did not show the result"));
            }
            else
            {
                report.NotRun("A window closed during the match came back and took its seat again", "no window was closed during the match (close one with its X to test this)");
            }
            await FinalHashCheck(setup, report, ends);
            Ask(setup, report, "You played with the mouse", "Did clicking with the mouse work for everything you tried (buttons, models, the battlefield)?");
            Ask(setup, report, "Text is easy to read", "Was all the text easy to read (size and contrast)? You can try the text size and High contrast settings at the top.");
        }
        catch (FriendlyException e)
        {
            report.Fail("Test match", e.Message);
        }
        finally
        {
            foreach (var w in windows)
            {
                w.Child?.Kill();
            }
            server?.Kill();
            await Cleanup(setup, report, windows, ownSeats, stopped);
        }
        return report.Finish();
    }

    internal static async Task<bool> Build(Setup setup, Report report, string godot)
    {
        foreach (var project in new[] { "Match.Server", "Match.Cli" })
        {
            var (exit, lines) = await setup.Processes.Run($"build-{project}", "dotnet",
                ["build", Path.Combine(setup.Repository, "src", project), "-c", "Debug", "-nodeReuse:false", "-p:UseSharedCompilation=false"], report.Logs, 900);
            if (exit != 0)
            {
                report.Fail($"{project} builds", $"exit {exit}: {string.Join(" | ", lines.Where(l => l.Contains(": error ", StringComparison.Ordinal)).Distinct().Take(3))} (see logs/build-{project}.txt)");
                return false;
            }
        }
        var (built, output) = await setup.Processes.Run("godot-build", godot, ["--headless", "--path", Path.Combine(setup.Repository, "client"), "--build-solutions", "--quit"], report.Logs, 900);
        if (built != 0 || output.Any(l => l.Contains("ERROR", StringComparison.Ordinal)))
        {
            report.Fail("The client builds in Godot", $"godot --build-solutions: exit {built}: {string.Join(" | ", output.Where(l => l.Contains("ERROR", StringComparison.Ordinal)).Take(3))}");
            return false;
        }
        report.Pass("The game builds", "server, trench-cli and the Godot client");
        return true;
    }

    private static void Open(Setup setup, Report report, string godot, Options options, Window window, string url, string mode, string? code, string[] extra)
    {
        window.Opened++;
        var name = window.Opened == 1 ? $"window-{window.Name}" : $"window-{window.Name}-{window.Opened}";
        window.Logs.Add(name);
        var args = new List<string>();
        if (options.OpenGl)
        {
            args.AddRange(["--rendering-driver", "opengl3"]);
        }
        args.AddRange(["--path", Path.Combine(setup.Repository, "client"), "--", "--test-match", mode, "--server", url, "--scenario", options.Scenario,
            "--test-window", window.Name, "--test-report", window.ReportPath]);
        if (code is not null)
        {
            args.AddRange(["--code", code]);
        }
        args.AddRange(extra);
        window.Child = setup.Processes.Start(name, godot, args, report.Logs, new Dictionary<string, string> { ["TRENCH_SIM_HOME"] = window.Home });
    }

    // The window's saved seat (code and match id), once the server has seated it.
    private static async Task<(string Code, string MatchId)?> SavedSeat(Window window, string? matchId, double seconds)
    {
        var sessions = Path.Combine(window.Home, "sessions");
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && window.Child is { HasExited: false })
        {
            var file = Directory.Exists(sessions)
                ? Directory.EnumerateFiles(sessions, $"{matchId ?? "*"}-{window.Seat}.json").FirstOrDefault()
                : null;
            if (file is not null)
            {
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    return (json.RootElement.GetProperty("code").GetString()!, json.RootElement.GetProperty("matchId").GetString()!);
                }
                catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException)
                {
                }
            }
            await Task.Delay(250);
        }
        return null;
    }

    private static void Instructions(Setup setup)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  YOUR TURN: play the match");
        Console.ResetColor();
        Console.WriteLine("    Window A (left) is Seat One. Window B (right) is Seat Two.");
        Console.WriteLine("    1. Close the rules box in each window (OK).");
        Console.WriteLine("    2. Play the whole match with the mouse, switching windows as the turn passes.");
        Console.WriteLine("       Each window says when it is your move. The server decides every result.");
        Console.WriteLine("    3. Once during the match, close one window with the X in its title bar.");
        Console.WriteLine("       The test center reopens it and it takes its seat back by itself.");
        Console.WriteLine("    4. Keep playing until both windows show \"Result of the match\".");
        Console.WriteLine();
        Console.WriteLine(setup.Interactive && !Console.IsInputRedirected
            ? "    Press Q in this window at any time to stop the test."
            : "    (No keyboard here: the test runs until the match ends.)");
        Console.WriteLine();
    }

    // Watches the windows until the server has written the match's record. A window closed before then is reopened and
    // rejoins from its saved seat. Returns false when the player stopped the test.
    private static async Task<bool> PlayUntilOver(Setup setup, Report report, string godot, Options options, List<Window> windows, string url, string code,
        string record, List<string> closedDuringMatch)
    {
        var keys = setup.Interactive && !Console.IsInputRedirected;
        while (!File.Exists(record))
        {
            if (keys && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q)
            {
                if (setup.Ask("Stop the test now? The match will not be finished.") == true)
                {
                    return false;
                }
                Console.WriteLine("    Carrying on.");
            }
            foreach (var w in windows)
            {
                if (w.Child is { HasExited: true } child && !File.Exists(record))
                {
                    await Task.Delay(500);
                    if (File.Exists(record))
                    {
                        break;
                    }
                    var exit = child.Process.ExitCode;
                    closedDuringMatch.Add($"window {w.Name} ({MatchCheck.SeatName(w.Seat)}, exit code {exit})");
                    if (w.Opened >= 6)
                    {
                        throw new FriendlyException($"Window {w.Name} keeps closing ({w.Opened} times); see its logs in {report.Logs}.");
                    }
                    Console.WriteLine($"    Window {w.Name} was closed. Reopening it: it takes its seat back from its saved seat.");
                    Open(setup, report, godot, options, w, url, "rejoin", code, w.Name == "A" ? options.ExtraA : options.ExtraB);
                }
            }
            await Task.Delay(500);
        }
        return true;
    }

    // Copy code puts the match code on the clipboard (checked by reading the clipboard, not by asking).
    private static async Task CopyCodeCheck(Setup setup, Report report, string code)
    {
        const string name = "Copy code puts the match code on the clipboard";
        if (!setup.Interactive)
        {
            report.NotRun(name, "no one at the keyboard to click it");
            return;
        }
        Console.WriteLine();
        Console.WriteLine("  First, one quick check: in window A, close the rules box, click \"Copy code\", then come back here.");
        Console.Write("  Press Enter when you have clicked it (or type S and Enter to skip): ");
        if ((Console.ReadLine() ?? "S").Trim().Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            report.NotRun(name, "skipped");
            return;
        }
        var clip = await Clipboard(setup, report);
        if (clip is null)
        {
            report.NotRun(name, "the clipboard cannot be read on this computer");
            return;
        }
        report.Check(name, clip == code, $"the clipboard holds '{Setup.Short(clip)}', the match code is {code}");
    }

    private static async Task FinalHashCheck(Setup setup, Report report, List<MatchCheck.ClientEnd> ends)
    {
        const string name = "Copy final hash puts the final hash on the clipboard";
        var expected = ends.FirstOrDefault(e => e.Window == "A")?.Hash;
        if (!setup.Interactive || expected is null)
        {
            report.NotRun(name, expected is null ? "window A showed no result" : "no one at the keyboard to click it");
            return;
        }
        Console.WriteLine();
        Console.WriteLine("  Last checks. In window A, click \"Copy final hash\" (under Result of the match), then come back here.");
        Console.Write("  Press Enter when you have clicked it (or type S and Enter to skip): ");
        if ((Console.ReadLine() ?? "S").Trim().Equals("S", StringComparison.OrdinalIgnoreCase))
        {
            report.NotRun(name, "skipped");
            return;
        }
        var clip = await Clipboard(setup, report);
        if (clip is null)
        {
            report.NotRun(name, "the clipboard cannot be read on this computer");
            return;
        }
        report.Check(name, clip == expected, $"the clipboard holds '{Setup.Short(clip)}', window A's final hash is {expected}");
    }

    private static async Task<string?> Clipboard(Setup setup, Report report)
    {
        if (OperatingSystem.IsWindows())
        {
            var (exit, lines) = await setup.Processes.Run("clipboard", "powershell", ["-NoProfile", "-NonInteractive", "-Command", "Get-Clipboard -Raw"], report.Logs, 30);
            return exit == 0 ? string.Join("\n", lines).Trim() : null;
        }
        foreach (var (tool, args) in new[] { ("/usr/bin/xclip", new[] { "-o", "-selection", "clipboard" }), ("/usr/bin/xsel", new[] { "--clipboard", "--output" }) })
        {
            if (File.Exists(tool))
            {
                var (exit, lines) = await setup.Processes.Run("clipboard", tool, args, report.Logs, 30);
                return exit == 0 ? string.Join("\n", lines).Trim() : null;
            }
        }
        return null;
    }

    private static void Ask(Setup setup, Report report, string check, string question)
    {
        switch (setup.Ask(question))
        {
            case true:
                report.Pass(check, "you answered yes");
                break;
            case false:
                report.Fail(check, "you answered no: please describe what went wrong when you report this result");
                break;
            default:
                report.NotRun(check, setup.Interactive ? "you skipped this question" : "no one at the keyboard to ask");
                break;
        }
    }

    // Nothing left running, no saved seats left from the test, and the player's own saved seats untouched.
    private static async Task Cleanup(Setup setup, Report report, List<Window> windows, Dictionary<string, DateTime> ownSeats, bool stopped)
    {
        await Task.Delay(1000);
        var left = setup.Leftovers();
        foreach (var (pid, _) in left)
        {
            Processes.Stop(pid);
        }
        var still = setup.Leftovers();
        report.Check("Nothing from the test is still running", still.Count == 0,
            still.Count == 0
                ? (left.Count == 0 ? "the server and both windows have stopped" : $"stopped {left.Count} leftover programs: {string.Join("; ", left.Select(l => Setup.Short(l.CommandLine)))}")
                : $"still running: {string.Join("; ", still.Select(l => $"{l.Pid} {Setup.Short(l.CommandLine)}"))}");
        var testSeats = windows.SelectMany(w => Directory.Exists(Path.Combine(w.Home, "sessions"))
            ? Directory.EnumerateFiles(Path.Combine(w.Home, "sessions"), "*.json") : []).ToList();
        if (stopped)
        {
            report.Info("The test's saved seats", $"{testSeats.Count} left in this run's folder because the match was stopped; they are never used outside this test");
        }
        else if (windows.Count > 0)
        {
            report.Check("The test's saved seats were removed when the match ended", testSeats.Count == 0,
                testSeats.Count == 0 ? "none left in either window's folder" : $"still there: {string.Join(", ", testSeats.Select(Path.GetFileName))}");
        }
        var now = OwnSavedSeats();
        var same = now.Count == ownSeats.Count && now.All(p => ownSeats.TryGetValue(p.Key, out var time) && time == p.Value);
        report.Check("Your own saved seats were not touched", same,
            $"{ownSeats.Count} saved seats in {SessionsFolder()} before, {now.Count} after{(same ? ", unchanged" : ", CHANGED")}");
        foreach (var w in windows)
        {
            var errors = w.Logs.SelectMany(l => File.Exists(Path.Combine(report.Logs, l + ".txt")) ? File.ReadAllLines(Path.Combine(report.Logs, l + ".txt")) : [])
                .Where(l => (l.Contains("ERROR", StringComparison.Ordinal) && !l.Contains("ERR_CANT_OPEN", StringComparison.Ordinal)) || l.Contains("Unhandled exception", StringComparison.Ordinal))
                .Distinct().Take(5).ToList();
            if (w.Logs.Count > 0)
            {
                report.Check($"Window {w.Name} had no engine errors", errors.Count == 0, errors.Count == 0 ? $"none in {string.Join(", ", w.Logs)}" : string.Join(" | ", errors));
            }
        }
    }

    private static string SessionsFolder()
    {
        var home = Environment.GetEnvironmentVariable("TRENCH_SIM_HOME") is { Length: > 0 } h
            ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trench-sim");
        return Path.Combine(home, "sessions");
    }

    private static Dictionary<string, DateTime> OwnSavedSeats()
    {
        var folder = SessionsFolder();
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).ToDictionary(f => f, File.GetLastWriteTimeUtc)
            : [];
    }

    private static void Step(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"  > {text}…");
    }
}
