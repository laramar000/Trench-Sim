namespace TestCenter;

// "Check the Godot editor": opens the project in the real Godot editor and watches for what its Build and Play buttons
// do (the client is compiled; the editor starts the game), so the result rests on what happened, not only on an answer.
public static class EditorCheck
{
    public static async Task<int> Run(Setup setup)
    {
        var report = setup.NewReport("Trench Sim Godot editor check", "editor");
        setup.StopLeftovers(report);
        if (await setup.DotNet(report) is null || await setup.Godot(report) is not { } godot)
        {
            report.Missed("Godot editor check", "not started: see above");
            return report.Finish();
        }
        if (!setup.Interactive)
        {
            report.Missed("Build and Play buttons in the Godot editor", "needs a person at the keyboard and mouse");
            return report.Finish();
        }
        var client = Path.Combine(setup.Repository, "client");
        // The editor rewrites project.godot in its own format on open; the committed file is put back afterwards.
        var projectFile = Path.Combine(client, "project.godot");
        var project = File.ReadAllBytes(projectFile);
        var started = DateTime.UtcNow;
        var assemblies = Path.Combine(client, ".godot", "mono", "temp", "bin", "Debug");
        DateTime Built() => Directory.Exists(assemblies)
            ? Directory.EnumerateFiles(assemblies, "*.dll").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
            : DateTime.MinValue;
        var before = Built();
        string? game = null;
        string? build = null;
        Processes.Child? editor = null;
        try
        {
            editor = setup.Processes.Start("godot-editor", godot, ["--editor", "--path", client], report.Logs);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  YOUR TURN: the Godot editor is opening");
            Console.ResetColor();
            Console.WriteLine("    1. Wait until the editor has finished loading.");
            Console.WriteLine("    2. Click Build (the hammer, top right).");
            Console.WriteLine("    3. Click Play (the triangle, top right) or press F5. The game window opens.");
            Console.WriteLine("    4. Look at the game window, then close it, then close the editor (do not save anything).");
            Console.WriteLine("    Press Q here to stop waiting.");
            while (!editor.HasExited)
            {
                if (!Console.IsInputRedirected && Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q)
                {
                    break;
                }
                var children = Processes.Descendants(editor.Process.Id);
                // Build runs "dotnet build" on the client's project.
                build ??= children.Select(p => p.CommandLine)
                    .FirstOrDefault(c => c.Contains("dotnet", StringComparison.OrdinalIgnoreCase) && c.Split(' ').Contains("build"));
                if (game is null)
                {
                    // The game Play starts is a Godot program started by the editor without --editor (not the build, whose
                    // command line also names Godot).
                    game = children
                        .Where(p => IsGodot(p.CommandLine) && !p.CommandLine.Split(' ').Contains("--editor"))
                        .Select(p => p.CommandLine).FirstOrDefault();
                    if (game is not null)
                    {
                        Console.WriteLine("    The game window was started by the editor.");
                    }
                }
                await Task.Delay(OperatingSystem.IsWindows() ? 2000 : 500);
            }
        }
        finally
        {
            editor?.Kill();
            if (!File.ReadAllBytes(projectFile).AsSpan().SequenceEqual(project))
            {
                File.WriteAllBytes(projectFile, project);
                report.Info("client/project.godot", "the editor reformatted it; the original file was put back");
            }
        }
        var built = Built();
        if (built > before && built >= started.AddSeconds(-2))
        {
            report.Pass("Build button (the editor compiled the client)", $"the client's assemblies were rebuilt at {built.ToLocalTime():HH:mm:ss}");
        }
        else if (build is not null)
        {
            report.Pass("Build button (the editor compiled the client)", $"the editor ran the build (already up to date): {Setup.Short(build)}");
        }
        else
        {
            report.Missed("Build button (the editor compiled the client)", "no build happened while the editor was open");
        }
        if (game is not null)
        {
            report.Pass("Play button (the editor started the game)", Setup.Short(game));
            switch (setup.Ask("Did the game window show the Trench Sim connect screen (server address, Create match, Hot-seat)?"))
            {
                case true:
                    report.Pass("The game window showed the connect screen", "you answered yes");
                    break;
                case false:
                    report.Fail("The game window showed the connect screen", "you answered no: please describe what it showed");
                    break;
                default:
                    report.NotRun("The game window showed the connect screen", "you skipped this question");
                    break;
            }
        }
        else
        {
            report.Missed("Play button (the editor started the game)", "the editor did not start the game while it was open");
        }
        return report.Finish();
    }

    // True when the program a command line runs is Godot itself (Godot_v4.7.2-stable_mono_win64.exe and the like).
    public static bool IsGodot(string commandLine)
    {
        var line = commandLine.TrimStart();
        var program = line.StartsWith('"') ? line[1..].Split('"')[0] : line.Split(' ')[0];
        return Path.GetFileName(program.Replace('\\', '/')).StartsWith("godot", StringComparison.OrdinalIgnoreCase);
    }
}
