using System.Text.RegularExpressions;

namespace TestCenter;

// "Run the essential tests" (the default, Jason 2026-10-31: test runs cut to the essentials): the Debug build with
// warnings as errors, the whole test suite once and the authority checks, a few minutes. "Run the full tests" (--full)
// adds the Release build and suite and the UI checks for this platform. On Windows the UI checks are the existing Windows script
// (tools/UiCheck/Run-WindowsUiCheck.ps1), unchanged; on Linux the same runner it wraps. Nothing is skipped quietly: a
// check that cannot run here is reported as NOT RUN with the reason.
public static partial class AutomatedTests
{
    public static async Task<int> Run(Setup setup, bool full = false)
    {
        var report = setup.NewReport(full ? "Trench Sim automated tests (full)" : "Trench Sim essential tests", "automated");
        var configurations = full ? new[] { "Debug", "Release" } : new[] { "Debug" };
        setup.StopLeftovers(report);
        Step("Checking the .NET SDK");
        if (await setup.DotNet(report) is null)
        {
            return report.Finish();
        }

        var built = new Dictionary<string, bool>();
        foreach (var configuration in configurations)
        {
            Step($"Building the game ({configuration})");
            var (exit, lines) = await setup.Processes.Run($"build-{configuration}", "dotnet",
                ["build", Path.Combine(setup.Repository, "TrenchSim.sln"), "-c", configuration, "-warnaserror", "-nodeReuse:false", "-p:UseSharedCompilation=false"],
                report.Logs, 1200);
            var warnings = Count(lines, "Warning");
            var errors = Count(lines, "Error");
            built[configuration] = exit == 0 && warnings == 0 && errors == 0;
            report.Check($"Build ({configuration}, warnings are errors)", built[configuration],
                exit is null ? "did not finish in 20 minutes"
                : $"exit {exit}, {warnings} warnings, {errors} errors{FirstErrors(lines)}");
        }

        foreach (var configuration in configurations)
        {
            var name = $"Test suite ({configuration})";
            if (!built[configuration])
            {
                report.Missed(name, "not run because the build failed");
                continue;
            }
            Step($"Running the test suite ({configuration}); this takes a few minutes");
            var (exit, lines) = await setup.Processes.Run($"tests-{configuration}", "dotnet",
                ["test", Path.Combine(setup.Repository, "TrenchSim.sln"), "-c", configuration, "--no-build"], report.Logs, 1800);
            var (passed, failed, total) = Totals(lines);
            report.Check(name, exit == 0 && failed == 0 && passed > 0 && passed == total,
                exit is null ? "did not finish in 30 minutes" : $"{passed:N0} passed, {failed} failed of {total:N0}{FailedTests(lines)}");
        }

        // The authority guards are part of the suite above; they are listed on their own because they are what keeps the
        // client from deciding anything (it only shows the server's state and sends commands).
        if (built["Debug"])
        {
            Step("Checking that the client holds no game authority");
            var (exit, lines) = await setup.Processes.Run("authority-checks", "dotnet",
                ["test", Path.Combine(setup.Repository, "tests", "Client.Tests"), "-c", "Debug", "--no-build", "--filter",
                 "FullyQualifiedName~ClientAuthorityTest|FullyQualifiedName~ClientProjectTest"], report.Logs, 600);
            var (passed, failed, total) = Totals(lines);
            report.Check("Authority checks (the client never resolves, rolls or applies anything itself)", exit == 0 && failed == 0 && passed > 0,
                $"{passed} passed, {failed} failed of {total}{FailedTests(lines)}");
        }
        else
        {
            report.Missed("Authority checks (the client never resolves, rolls or applies anything itself)", "not run because the Debug build failed");
        }

        if (full)
        {
            await UiChecks(setup, report);
        }
        else
        {
            report.Info("Left out of the essential run", "the Release build and suite and the UI checks; menu option 8 (or `tests --full`) runs them");
        }
        return report.Finish();
    }

    private static async Task UiChecks(Setup setup, Report report)
    {
        const string windows = "Windows UI checks (real Godot windows, full matches)";
        const string linux = "Linux UI checks (real Godot windows, full matches)";
        if (OperatingSystem.IsWindows())
        {
            report.NotRun(linux, "this computer runs Windows");
        }
        else
        {
            report.NotRun(windows, "this computer is not running Windows; run the test center on the Windows PC for these");
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            report.NotRun("UI checks", "UI checks run on Windows and Linux only");
            return;
        }
        Step("Finding Godot for the UI checks");
        var godot = await setup.Godot(report);
        if (godot is null)
        {
            report.Missed(OperatingSystem.IsWindows() ? windows : linux, "not run: Godot 4.7.2 .NET was not available");
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            await WindowsUiChecks(setup, report, godot, windows);
        }
        else
        {
            await LinuxUiChecks(setup, report, godot, linux);
        }
    }

    // How long the UI checks may run before they are stopped. About 30 minutes on a usual Linux machine; 75 leaves room
    // for a slow one (runs 343 and 344 on a slow one took about 53). On Windows the script's own runner limit (65) applies first.
    private const int UiCheckMinutes = 75;

    // The existing Windows script, as it is: it runs the UI check runner, opens the project in the Godot editor, lists
    // the checks only a person can do, stops everything it started and writes its own verdict.
    private static async Task WindowsUiChecks(Setup setup, Report report, string godot, string name)
    {
        Step("Running the Windows UI checks: Godot windows will open and play by themselves. Please do not use the mouse or keyboard until they finish (about 10 minutes)");
        var script = Path.Combine(setup.Repository, "tools", "UiCheck", "Run-WindowsUiCheck.ps1");
        var (exit, lines) = await setup.Processes.Run("windows-ui-check", "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-GodotPath", godot, "-ResultsRoot", Path.Combine(report.Folder, "ui-check")],
            report.Logs, UiCheckMinutes * 60, echo: line => Console.WriteLine("      " + line));
        var verdict = lines.LastOrDefault(l => l.Contains("Verdict: ", StringComparison.Ordinal)) ?? "";
        var summary = Regex.Match(verdict, @"Summary: (.+)$");
        var said = verdict.IndexOf("Verdict: ", StringComparison.Ordinal) is var at and >= 0 ? verdict[at..] : "no verdict line";
        if (exit == 2)
        {
            report.Missed(name, $"exit 2, the run did not finish every check: {said}");
        }
        else
        {
            report.Check(name, exit == 0, exit is null ? $"did not finish in {UiCheckMinutes} minutes" : $"exit {exit}: {said}");
        }
        if (summary.Success && File.Exists(summary.Groups[1].Value.Trim()))
        {
            foreach (var line in File.ReadLines(summary.Groups[1].Value.Trim()))
            {
                var fail = Regex.Match(line, @"^- \*\*(.+?)\*\*: (.*)$");
                if (fail.Success && !line.Contains("(manual)", StringComparison.Ordinal) && !line.Contains("(optional)", StringComparison.Ordinal))
                {
                    report.Info($"Windows UI check detail: {fail.Groups[1].Value}", fail.Groups[2].Value);
                }
            }
        }
        ManualChecks(report);
    }

    // The same runner the Windows script wraps, under a virtual display when there is no screen.
    private static async Task LinuxUiChecks(Setup setup, Report report, string godot, string name)
    {
        var display = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        var xvfb = !display && File.Exists("/usr/bin/xvfb-run");
        if (!display && !xvfb)
        {
            report.Missed(name, "not run: no display (DISPLAY is not set) and xvfb-run is not installed");
            return;
        }
        Step("Running the Linux UI checks: Godot windows play by themselves (about 30 minutes)");
        var output = Path.Combine(report.Folder, "ui-check");
        var runner = new List<string> { "run", "--project", Path.Combine(setup.Repository, "tools", "UiCheck"), "--", "--godot", godot, "--out", output };
        if (xvfb)
        {
            runner.Add("--opengl");
        }
        var (file, args) = xvfb
            ? ("/usr/bin/xvfb-run", (IEnumerable<string>)["-a", "-s", "-screen 0 1280x800x24", "dotnet", .. runner])
            : ("dotnet", runner);
        var (exit, _) = await setup.Processes.Run("linux-ui-check", file, args, report.Logs, UiCheckMinutes * 60,
            echo: line => { if (Regex.IsMatch(line, "^(PASS|FAIL) ")) { Console.WriteLine("      " + line); } });
        var rows = UiCheckRows(Path.Combine(output, "summary.md"));
        var missing = MissingChecks(Path.Combine(setup.Repository, "tools", "UiCheck", "expected-checks.txt"), rows.Select(r => r.Id).ToList(), out var noChance);
        var failed = rows.Where(r => r.Result == "FAIL").ToList();
        var passed = rows.Count(r => r.Result == "PASS");
        if (exit is null)
        {
            report.Fail(name, $"did not finish in {UiCheckMinutes} minutes; {passed} passed, {failed.Count} failed before it was stopped");
        }
        else if (missing.Count > 0 && failed.Count == 0)
        {
            report.Missed(name, $"{passed} passed, but {missing.Count} expected checks did not report: {string.Join(", ", missing.Take(10))}");
        }
        else
        {
            report.Check(name, exit == 0 && failed.Count == 0 && missing.Count == 0,
                $"exit {exit}: {passed} passed, {failed.Count} failed{(failed.Count > 0 ? ": " + string.Join("; ", failed.Take(5).Select(f => $"{f.Id}: {f.Detail}")) : "")}" +
                $"{(noChance.Count > 0 ? $"; {noChance.Count} optional checks had no chance in this match" : "")}");
        }
        ManualChecks(report);
    }

    // What the automated UI checks cannot do; "Play a test match" and "Check the Godot editor" cover them with the
    // player's help.
    private static void ManualChecks(Report report)
    {
        report.NotRun("Mouse, title-bar X, Copy code, Copy final hash, readability", "need a person: menu option 2, Play a test match");
        report.NotRun("Build and Play buttons in the Godot editor", "need a person: menu option 3, Check the Godot editor");
    }

    public static List<(string Result, string Id, string Detail)> UiCheckRows(string summary)
    {
        var rows = new List<(string, string, string)>();
        if (!File.Exists(summary))
        {
            return rows;
        }
        foreach (var line in File.ReadLines(summary))
        {
            var row = Regex.Match(line, @"^\| (PASS|FAIL|INFO) \| (.+?) \| (.*) \|$");
            if (row.Success)
            {
                rows.Add((row.Groups[1].Value, row.Groups[2].Value, row.Groups[3].Value));
            }
        }
        return rows;
    }

    // The expected checks (tools/UiCheck/expected-checks.txt) that did not report, as the Windows script counts them:
    // "a|b: check" is met by either role, and "optional <id>" may be absent when the match gave no chance for it.
    public static List<string> MissingChecks(string expectedFile, List<string> reported, out List<string> noChance)
    {
        var lines = File.ReadLines(expectedFile).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        var optional = lines.Where(l => l.StartsWith("optional ", StringComparison.Ordinal)).Select(l => l[9..]).ToHashSet();
        var absent = new List<string>();
        foreach (var entry in lines.Select(l => l.StartsWith("optional ", StringComparison.Ordinal) ? l[9..] : l))
        {
            var either = Regex.Match(entry, @"^([^:]+\|[^:]+): (.*)$");
            var present = either.Success
                ? either.Groups[1].Value.Split('|').Any(role => reported.Contains($"{role}: {either.Groups[2].Value}"))
                : reported.Contains(entry);
            if (!present)
            {
                absent.Add(entry);
            }
        }
        noChance = [.. absent.Where(optional.Contains)];
        return [.. absent.Where(a => !optional.Contains(a))];
    }

    private static void Step(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"  > {text}…");
    }

    private static int Count(List<string> lines, string kind) =>
        lines.Select(l => Regex.Match(l.Trim(), $@"^(\d+) {kind}\(s\)$")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Last();

    private static string FirstErrors(List<string> lines)
    {
        var errors = lines.Where(l => l.Contains(": error ", StringComparison.Ordinal) || l.Contains(": warning ", StringComparison.Ordinal)).Distinct().Take(3).ToList();
        return errors.Count == 0 ? "" : ": " + string.Join(" | ", errors.Select(e => e.Trim()));
    }

    // Sums the "Passed!/Failed! - Failed: x, Passed: y, Skipped: z, Total: t" lines of every test project.
    public static (int Passed, int Failed, int Total) Totals(List<string> lines)
    {
        int passed = 0, failed = 0, total = 0;
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"(?:Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)");
            if (m.Success)
            {
                failed += int.Parse(m.Groups[1].Value);
                passed += int.Parse(m.Groups[2].Value);
                total += int.Parse(m.Groups[4].Value);
            }
        }
        return (passed, failed, total);
    }

    private static string FailedTests(List<string> lines)
    {
        var names = lines.Select(l => Regex.Match(l, @"^\s+Failed (\S+)")).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().Take(5).ToList();
        return names.Count == 0 ? "" : "; failed: " + string.Join(", ", names);
    }
}
