namespace TestCenter;

// Menu option 7 (Jason, 2026-10-28): "Play Trenches" opens the game on its main menu, as a player sees it, with no
// Godot editor and no questions. It builds the game first (as option 2 does) and uses this PC's own settings and saved
// seats. The test center waits until the game window is closed (or Exit is chosen), then returns to its menu.
public static class PlayGame
{
    public static async Task<int> Run(Setup setup, bool openGl)
    {
        var report = setup.NewReport("Play Trenches", "play");
        setup.StopLeftovers(report);
        Step("Checking .NET and Godot");
        if (await setup.DotNet(report) is null || await setup.Godot(report) is not { } godot)
        {
            report.Missed("The game opened", "not started: see above");
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
        args.AddRange(["--path", Path.Combine(setup.Repository, "client")]);
        Step("Opening Trenches on its main menu. Close its window or choose Exit when you are done");
        var game = setup.Processes.Start("game", godot, args, report.Logs);
        var exit = await game.Exit(TimeSpan.FromDays(1).TotalSeconds);
        report.Check("The game opened on its main menu and closed", exit == 0,
            exit == 0 ? "closed normally" : $"exit {exit?.ToString() ?? "none"} (see {game.LogPath})");
        return report.Finish();
    }

    private static void Step(string text)
    {
        Console.WriteLine();
        Console.WriteLine($"  > {text}…");
    }
}
