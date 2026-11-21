using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TestCenter;

// What a run needs before it starts: the repository, the .NET 10 SDK and Godot 4.7.2 .NET. Godot is found on its own
// (or downloaded with the player's permission) and remembered, so nobody types a path twice.
public sealed partial class Setup(string repository, Processes processes, bool interactive)
{
    public const string GodotVersion = "4.7.2";

    public string Repository { get; } = repository;

    public Processes Processes { get; } = processes;

    // False when nobody is at the keyboard (input redirected, or --no-questions): questions are then skipped.
    public bool Interactive { get; } = interactive;

    public string Results => Path.Combine(Repository, "test-results");

    // Where the test center remembers things between downloads of the game (the Godot path) and keeps a Godot it
    // downloaded: %LOCALAPPDATA%\TrenchSim on Windows, ~/.local/share/TrenchSim elsewhere.
    public static string SettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "TrenchSim");

    public static string FindRepository()
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
        throw new FriendlyException("The Trench Sim folder was not found. Start the test center from inside the Trench Sim folder.");
    }

    // The commit under test: from git when the folder is a clone, else from a GitHub download's folder name.
    public string Commit()
    {
        var head = Path.Combine(Repository, ".git", "HEAD");
        if (File.Exists(head))
        {
            var text = File.ReadAllText(head).Trim();
            if (text.StartsWith("ref: ", StringComparison.Ordinal))
            {
                var reference = Path.Combine(Repository, ".git", text[5..]);
                if (File.Exists(reference))
                {
                    return $"{File.ReadAllText(reference).Trim()} (git, {text[5..]})";
                }
                var packed = Path.Combine(Repository, ".git", "packed-refs");
                if (File.Exists(packed) && File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + text[5..], StringComparison.Ordinal)) is { } line)
                {
                    return $"{line.Split(' ')[0]} (git, {text[5..]})";
                }
            }
            return $"{text} (git)";
        }
        var name = Path.GetFileName(Repository);
        var sha = Sha().Match(name);
        return sha.Success ? $"{sha.Value} (downloaded folder {name})" : $"unknown (folder {name}, no git)";
    }

    public static string Computer() =>
        $"{Environment.MachineName}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; .NET {Environment.Version}";

    public void Describe(Report report)
    {
        report.Fact("Trench Sim version (commit)", Commit());
        report.Fact("Folder", Repository);
        report.Fact("Computer", Computer());
    }

    // The .NET 10 SDK, which builds and runs everything. The test center itself runs on it, so it is normally there.
    public async Task<string?> DotNet(Report report)
    {
        var (exit, lines) = await Processes.Run("dotnet-version", "dotnet", ["--version"], report.Logs, 60);
        var version = lines.FirstOrDefault(l => Regex.IsMatch(l.Trim(), @"^\d+\.\d+\.\d+"))?.Trim();
        if (exit != 0 || version is null || !version.StartsWith("10.", StringComparison.Ordinal))
        {
            report.Fail("The .NET 10 SDK is installed", $"dotnet --version said '{version ?? "nothing"}'. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and try again.");
            return null;
        }
        report.Fact(".NET SDK", version);
        return version;
    }

    // The Godot 4.7.2 .NET executable to run (on Windows the _console.exe, so its output can be read). Null when there is
    // none and the player did not provide one.
    public async Task<string?> Godot(Report report)
    {
        foreach (var (candidate, how) in Candidates())
        {
            if (await GodotVersionOf(candidate, report) is { } version)
            {
                report.Fact("Godot", $"{candidate} ({how}; reports {version})");
                Remember(candidate);
                return candidate;
            }
        }
        if (!Interactive)
        {
            report.Missed("Godot 4.7.2 .NET was found", $"not found (looked in the GODOT variable, {RememberFile}, and the Downloads, Desktop and Documents folders); no one was asked because the test center is running without questions.");
            return null;
        }
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"  Godot {GodotVersion} (.NET edition) was not found on this computer.");
            Console.WriteLine("    D  Download it now (about 80 MB, from github.com/godotengine)");
            Console.WriteLine($"    Or drag the Godot program file ({ExpectedName}) into this window, then press Enter.");
            Console.WriteLine("    S  Skip the checks that need Godot");
            Console.Write("  Your choice: ");
            var answer = (Console.ReadLine() ?? "S").Trim().Trim('"', '\'', '&', ' ');
            if (answer.Equals("S", StringComparison.OrdinalIgnoreCase) || answer.Length == 0)
            {
                report.Missed("Godot 4.7.2 .NET was found", "skipped: no Godot was given.");
                return null;
            }
            var path = answer.Equals("D", StringComparison.OrdinalIgnoreCase) ? await Download(report) : answer;
            if (path is not null && File.Exists(path) && await GodotVersionOf(path, report) is { } version)
            {
                report.Fact("Godot", $"{path} (reports {version})");
                Remember(path);
                return path;
            }
            Console.WriteLine($"  That is not Godot {GodotVersion} .NET ({path ?? "nothing downloaded"}). Try again.");
        }
    }

    private static string ExpectedName =>
        OperatingSystem.IsWindows() ? $"Godot_v{GodotVersion}-stable_mono_win64_console.exe" : $"Godot_v{GodotVersion}-stable_mono_linux.x86_64";

    private static string RememberFile => Path.Combine(SettingsFolder, "godot-path.txt");

    private static void Remember(string path)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(RememberFile, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static IEnumerable<(string Path, string How)> Candidates()
    {
        if (Environment.GetEnvironmentVariable("GODOT") is { Length: > 0 } fromEnvironment)
        {
            yield return (fromEnvironment, "from the GODOT environment variable");
        }
        if (File.Exists(RememberFile) && File.ReadAllText(RememberFile).Trim() is { Length: > 0 } remembered && File.Exists(remembered))
        {
            yield return (remembered, "remembered from an earlier run");
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(SettingsFolder, "godot"), Path.Combine(home, "Downloads"), Path.Combine(home, "Desktop"), Path.Combine(home, "Documents"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"C:\Godot", @"C:\Tools",
        };
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true };
        foreach (var root in roots.Where(r => r.Length > 0 && Directory.Exists(r)).Distinct())
        {
            IEnumerable<string> found;
            try
            {
                found = [.. Directory.EnumerateFiles(root, ExpectedName, options).OrderByDescending(File.GetLastWriteTimeUtc)];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var path in found)
            {
                yield return (path, $"found in {root}");
            }
        }
    }

    private async Task<string?> GodotVersionOf(string path, Report report)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var (exit, lines) = await Processes.Run("godot-version", path, ["--version"], report.Logs, 60);
            var version = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(GodotVersion + ".stable.mono", StringComparison.Ordinal));
            return exit == 0 ? version : null;
        }
        catch (FriendlyException)
        {
            return null;
        }
    }

    // Downloads the official Godot 4.7.2 .NET build into the settings folder (only after the player chose D).
    private static async Task<string?> Download(Report report)
    {
        var file = OperatingSystem.IsWindows() ? $"Godot_v{GodotVersion}-stable_mono_win64" : $"Godot_v{GodotVersion}-stable_mono_linux_x86_64";
        var url = $"https://github.com/godotengine/godot-builds/releases/download/{GodotVersion}-stable/{file}.zip";
        var folder = Path.Combine(SettingsFolder, "godot");
        var zip = Path.Combine(folder, file + ".zip");
        Directory.CreateDirectory(folder);
        Console.WriteLine($"  Downloading {url} …");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            await using (var source = await http.GetStreamAsync(url))
            await using (var target = File.Create(zip))
            {
                await source.CopyToAsync(target);
            }
            ZipFile.ExtractToDirectory(zip, folder, overwriteFiles: true);
            File.Delete(zip);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            report.Info("Godot download", $"failed: {e.Message}");
            Console.WriteLine($"  The download failed: {e.Message}");
            return null;
        }
        var path = Directory.EnumerateFiles(folder, ExpectedName, SearchOption.AllDirectories).FirstOrDefault();
        if (path is not null && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        }
        report.Info("Godot download", $"downloaded {url} to {folder}");
        return path;
    }

    // Asks a yes/no question; null when nobody can answer (no keyboard) or the player skips it.
    public bool? Ask(string question)
    {
        if (!Interactive)
        {
            return null;
        }
        while (true)
        {
            Console.Write($"  {question} [Y = yes, N = no, S = skip]: ");
            var answer = (Console.ReadLine() ?? "S").Trim().ToUpperInvariant();
            switch (answer)
            {
                case "Y" or "YES":
                    return true;
                case "N" or "NO":
                    return false;
                case "S" or "SKIP" or "":
                    return null;
            }
        }
    }

    public void Pause(string text)
    {
        if (!Interactive)
        {
            return;
        }
        Console.Write($"  {text}");
        Console.ReadLine();
    }

    // A new folder for one run's report and logs, under test-results (never shared with normal play).
    public Report NewReport(string title, string kind)
    {
        var folder = Path.Combine(Results, $"{DateTime.Now:yyyyMMdd-HHmmss}-{kind}");
        var report = new Report(title, folder);
        Describe(report);
        return report;
    }

    // Programs a previous test left running (its command line names this repository's test-results folder). Never
    // anything else: the player's own game windows and servers are left alone.
    public List<(int Pid, string CommandLine)> Leftovers() => Processes.Using(Results);

    public void StopLeftovers(Report report)
    {
        var left = Leftovers();
        foreach (var (pid, line) in left)
        {
            Processes.Stop(pid);
        }
        if (left.Count > 0)
        {
            report.Info("Leftovers from an earlier test", $"stopped {left.Count}: {string.Join("; ", left.Select(l => $"{l.Pid} {Short(l.CommandLine)}"))}");
        }
    }

    public static string Short(string text) => text.Length > 160 ? text[..160] + "…" : text;

    [GeneratedRegex("[0-9a-f]{40}")]
    private static partial Regex Sha();
}
