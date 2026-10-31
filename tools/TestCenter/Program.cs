using System.Diagnostics;

// The Trench Sim test center: double-click "Test Trench Sim.cmd" in the Trench Sim folder (Windows), or run
// ./test-trench-sim.sh (Linux). With no arguments it shows a menu; each choice can also be run directly:
//
//   dotnet run --project tools/TestCenter -- tests [--full]        essential checks (--full: also Release and UI checks)
//   dotnet run --project tools/TestCenter -- match [ranged|melee|ranged-2v2 ...]  a guided test match in two windows
//   dotnet run --project tools/TestCenter -- editor                the Godot editor's Build and Play buttons
//   dotnet run --project tools/TestCenter -- check [<record>]      verify a finished match's signed record
//   dotnet run --project tools/TestCenter -- cleanup               stop anything a test left running
//   dotnet run --project tools/TestCenter -- play                  open the game on its main menu
//
// Options: --godot <path> (else found or asked once, then remembered), --no-questions (nobody at the keyboard),
// --opengl (Godot's OpenGL renderer, for machines without Vulkan). Every run writes test-results/<time>-<kind>/report.md
// with PASS, FAIL or NOT RUN for each check, and keeps every program's output in its logs folder.
// Exit codes: 0 PASS, 1 FAIL, 2 INCOMPLETE (a check that should have run did not), 3 could not start.
namespace TestCenter;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var processes = new Processes();
        Console.CancelKeyPress += (_, _) => processes.StopAll();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => processes.StopAll();
        try
        {
            var repository = Setup.FindRepository();
            if (Value(args, "--godot") is { } godot)
            {
                Environment.SetEnvironmentVariable("GODOT", godot);
            }
            var interactive = !args.Contains("--no-questions") && !Console.IsInputRedirected;
            var setup = new Setup(repository, processes, interactive);
            var opengl = args.Contains("--opengl");
            var words = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || !Takes(args[i - 1]))).ToList();
            if (words.Count == 0)
            {
                return await Menu(setup, opengl);
            }
            return words[0] switch
            {
                "tests" => await AutomatedTests.Run(setup, args.Contains("--full")),
                "match" => await TestMatchRun.Run(setup, MatchOptions(args, words, opengl)),
                "editor" => await EditorCheck.Run(setup),
                "check" => await MatchCheck.CheckFinished(setup, words.ElementAtOrDefault(1)),
                "cleanup" => Cleanup(setup),
                "play" => await PlayGame.Run(setup, opengl),
                _ => Usage(),
            };
        }
        catch (FriendlyException e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  {e.Message}");
            Console.ResetColor();
            return 3;
        }
        finally
        {
            processes.StopAll();
        }
    }

    private static async Task<int> Menu(Setup setup, bool opengl)
    {
        var last = 0;
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("  ==================================================================");
            Console.WriteLine("   TRENCH SIM TEST CENTER");
            Console.WriteLine($"   Version: {setup.Commit()}");
            Console.WriteLine("  ==================================================================");
            Console.WriteLine("    1  Run the essential tests      no input needed; a few minutes");
            Console.WriteLine("    2  Play a test match            two windows, you play both sides");
            Console.WriteLine("    3  Check the Godot editor       you press Build and Play when asked");
            Console.WriteLine("    4  Check a finished match       verifies the latest match's record");
            Console.WriteLine("    5  Clean up                     stops anything a test left running");
            Console.WriteLine("    6  Get the newest version       downloads it and opens its test center");
            Console.WriteLine("    7  Play Trenches                opens the game on its main menu");
            Console.WriteLine("    8  Run the full tests           adds Release and the UI checks; about 15-25 minutes");
            Console.WriteLine("    Q  Quit");
            Console.Write("  Type a number and press Enter: ");
            var choice = (Console.ReadLine() ?? "Q").Trim().ToUpperInvariant();
            switch (choice)
            {
                case "1":
                    last = await AutomatedTests.Run(setup);
                    break;
                case "2":
                    // Matches are played on the layout with the towers (Jason, 2026-10-21: towers by default). Battlefield
                    // UI stage K7: melee by default, the warbands with shooters and fighters, so a match has both.
                    Console.Write("  Scenario (on the map with towers): 1 = ranged (shooting only), 2 = melee (shooting and close combat). Press Enter for melee: ");
                    var scenario = (Console.ReadLine() ?? "").Trim() == "1" ? "ranged" : "melee";
                    Console.Write("  Side size: 1, 2 or 3 models a side for a quick test, or Enter for the normal roster: ");
                    var size = (Console.ReadLine() ?? "").Trim();
                    if (size is "1" or "2" or "3")
                    {
                        scenario = $"{scenario}-{size}v{size}";
                    }
                    scenario += "-towers";
                    last = await TestMatchRun.Run(setup, new TestMatchRun.Options(scenario, opengl, [], []));
                    break;
                case "3":
                    last = await EditorCheck.Run(setup);
                    break;
                case "4":
                    last = await MatchCheck.CheckFinished(setup, null);
                    break;
                case "5":
                    last = Cleanup(setup);
                    break;
                case "6":
                    if (Update(setup))
                    {
                        return 0;
                    }
                    break;
                case "7":
                    last = await PlayGame.Run(setup, opengl);
                    break;
                case "8":
                    last = await AutomatedTests.Run(setup, full: true);
                    break;
                case "Q" or "":
                    return last;
                default:
                    Console.WriteLine("  Please type a number from 1 to 8, or Q.");
                    continue;
            }
            setup.Pause("Press Enter to go back to the menu.");
        }
    }

    private static int Cleanup(Setup setup)
    {
        var left = setup.Leftovers();
        foreach (var (pid, line) in left)
        {
            Console.WriteLine($"  Stopping {pid}: {Setup.Short(line)}");
            Processes.Stop(pid);
        }
        var still = setup.Leftovers();
        Console.WriteLine(left.Count == 0 ? "  Nothing from a test was running." : $"  Stopped {left.Count - still.Count} of {left.Count}.");
        if (still.Count > 0)
        {
            Console.WriteLine($"  Still running (close them by hand): {string.Join("; ", still.Select(s => $"{s.Pid} {Setup.Short(s.CommandLine)}"))}");
        }
        Console.WriteLine("  Your own game windows, servers and saved seats are never touched by the test center.");
        return still.Count == 0 ? 0 : 1;
    }

    // Downloads the newest version of this branch next to this one and opens its test center (Windows).
    private static bool Update(Setup setup)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("  On Linux, update the folder with git pull, then start ./test-trench-sim.sh again.");
            return false;
        }
        var script = Path.Combine(setup.Repository, "tools", "TestCenter", "Get-TrenchSim.ps1");
        Process.Start(new ProcessStartInfo("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script]) { UseShellExecute = true });
        Console.WriteLine("  The download opens in a new window; this window can be closed.");
        return true;
    }

    private static TestMatchRun.Options MatchOptions(string[] args, List<string> words, bool opengl) => new(
        words.ElementAtOrDefault(1) is { } scenario && Scenarios.Contains(scenario) ? scenario : "melee-towers", opengl,
        Value(args, "--client-args-a")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
        Value(args, "--client-args-b")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []);

    // The scenarios the server offers (Sim.ScenarioSetups): the normal rosters and the small test sizes.
    private static readonly string[] Scenarios = ["ranged", "melee", "ranged-1v1", "ranged-2v2", "ranged-3v3", "melee-1v1", "melee-2v2", "melee-3v3", "ranged-towers", "melee-towers",
        "ranged-1v1-towers", "ranged-2v2-towers", "ranged-3v3-towers", "melee-1v1-towers", "melee-2v2-towers", "melee-3v3-towers",
        "ranged-trench-warfare", "melee-trench-warfare", "ranged-1v1-trench-warfare", "ranged-2v2-trench-warfare", "ranged-3v3-trench-warfare",
        "melee-1v1-trench-warfare", "melee-2v2-trench-warfare", "melee-3v3-trench-warfare"];

    private static bool Takes(string option) => option is "--godot" or "--client-args-a" or "--client-args-b";

    private static string? Value(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static int Usage()
    {
        Console.WriteLine("  Usage: dotnet run --project tools/TestCenter -- [tests [--full] | match [ranged|melee|ranged-1v1 ... melee-3v3, ranged-towers, melee-towers, ranged-1v1-towers ... melee-3v3-towers, ranged-trench-warfare ... melee-3v3-trench-warfare] | editor | check [<record>] | cleanup | play]");
        Console.WriteLine("         [--godot <path>] [--no-questions] [--opengl]");
        return 3;
    }
}
