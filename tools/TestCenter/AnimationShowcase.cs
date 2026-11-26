namespace TestCenter;

// Menu option 9 (Jason, 2026-11-22: "validate the animations visually"): the animation showcase. Opens the game on a
// hot-seat match, deploys both sides, then plays staged scenes of the battlefield's animations close up, each with a
// caption naming what to look for (a shot taking a model Down or Out of Action, a miss, a mine stopping a move, charges,
// a grenade, a scatter, overlapping blasts, a hot-seat seat switch mid-shot, Fast). F5 plays the scene again, F6 the
// next, F7 the one before, F8 switches Full and Fast. Nothing is sent to the match. Close the window when done.
public static class AnimationShowcase
{
    public static async Task<int> Run(Setup setup, bool openGl)
    {
        var report = setup.NewReport("Animation showcase", "showcase");
        setup.StopLeftovers(report);
        Step("Checking .NET and Godot");
        if (await setup.DotNet(report) is null || await setup.Godot(report) is not { } godot)
        {
            report.Missed("The showcase opened", "not started: see above");
            return report.Finish();
        }
        Step("Preparing the game (building the server, trench-cli and the client)");
        if (!await TestMatchRun.Build(setup, report, godot))
        {
            return report.Finish();
        }
        var args = new List<string>();
        if (openGl)
        {
            args.AddRange(["--rendering-driver", "opengl3"]);
        }
        var output = Path.Combine(report.Logs, "showcase");
        Directory.CreateDirectory(output);
        args.AddRange(["--path", Path.Combine(setup.Repository, "client"), "--", "--ui-check", "dev", "--out", output, "--scenario", "ranged"]);
        Step("Opening the showcase: it starts a hot-seat match and deploys both sides by itself (about a minute), then plays "
            + "the first scene. F5 again, F6 next, F7 previous, F8 Full/Fast. Close the window when you are done");
        var game = setup.Processes.Start("showcase", godot, args, report.Logs,
            new Dictionary<string, string> { ["TRENCH_DEV"] = "deploy,showcase", ["TRENCH_SHOWCASE"] = "watch" });
        var exit = await game.Exit(TimeSpan.FromDays(1).TotalSeconds);
        report.Check("The showcase opened and closed", exit is not null,
            exit is not null ? $"closed (exit {exit})" : $"no exit (see {game.LogPath})");
        return report.Finish();
    }

    private static void Step(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"  > {text}…");
    }
}
