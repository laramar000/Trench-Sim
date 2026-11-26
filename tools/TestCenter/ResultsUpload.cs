using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TestCenter;

// Sends each run's results to GitHub (Jason, 2026-11-26: "make it so you are able to see the results of tests without me
// needing to copy"): after every run the report, the logs and the UI checks' summaries go to the branch "test-results"
// of the repository, one folder per run under runs/, and LATEST.md names the newest. Claude reads them from there with
// git fetch. Only when a GitHub key was given once (menu option U); without one nothing leaves this computer.
//
// The repository is public, so every file is cleaned first: this computer's name, the user name and the folders on it
// (the Trench Sim folder, the user folder, the app data folder) are replaced by <computer>, <user>, <repo>, <home> and
// <appdata>. The key is kept in this user's app data folder (Setup.SettingsFolder), never in the Trench Sim folder.
public static partial class ResultsUpload
{
    public const string Repository = "laramar000/Trench-Sim";

    public const string Branch = "test-results";

    // A log larger than this is cut to its last part (dotnet test and build logs are far smaller).
    private const int MaxFileBytes = 1_000_000;

    private static string KeyFile => Path.Combine(Setup.SettingsFolder, "github-upload-key.txt");

    public static bool Configured => File.Exists(KeyFile) && File.ReadAllText(KeyFile).Trim().Length > 0;

    // Menu option U: explains, opens GitHub's page for a new key, asks for it, checks it and keeps it. Or turns uploads off.
    public static async Task<int> SetUp()
    {
        Console.WriteLine();
        Console.WriteLine("  Send test results to GitHub, so Claude can read them without you copying anything.");
        Console.WriteLine($"  After each run, its report goes to the \"{Branch}\" branch of {Repository} (public).");
        Console.WriteLine("  Your computer's name, your user name and your folder paths are removed first.");
        if (Configured)
        {
            Console.Write("  Uploads are ON. Type OFF to turn them off, or press Enter to keep them: ");
            if ((Console.ReadLine() ?? "").Trim().Equals("OFF", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(KeyFile);
                Console.WriteLine("  Uploads are off. The key was deleted from this computer (you can also delete it on GitHub).");
            }
            return 0;
        }
        Console.WriteLine();
        Console.WriteLine("  A GitHub key is needed once. On the page that opens:");
        Console.WriteLine("    1. Token name: Trench Sim test results. Expiration: your choice.");
        Console.WriteLine($"    2. Repository access: Only select repositories, then {Repository}.");
        Console.WriteLine("    3. Permissions, Repository permissions: Contents, Read and write.");
        Console.WriteLine("    4. Generate token, then copy it.");
        const string page = "https://github.com/settings/personal-access-tokens/new";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Console.WriteLine($"  (Open this page yourself: {page})");
        }
        // (2026-11-26: a hidden prompt read key by key took Ctrl+V as a control key and dropped the paste; the key is now
        // taken from the clipboard, or from a normal line where pasting works.)
        Console.WriteLine("  Copy the key on GitHub, come back to this window and press Enter: the test center takes it from the");
        Console.Write("  clipboard. (Or paste it here first, then press Enter.) Type C and Enter to cancel: ");
        var typed = (Console.ReadLine() ?? "").Trim();
        if (typed.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("  Cancelled. Nothing was saved.");
            return 0;
        }
        var fromClipboard = typed.Length == 0;
        var key = fromClipboard ? await Clipboard() : typed;
        if (!KeyPattern().IsMatch(key))
        {
            Console.WriteLine(fromClipboard
                ? "  The clipboard does not hold a GitHub key (they start with github_pat_). Copy it on GitHub and choose U again."
                : "  That is not a GitHub key (they start with github_pat_). Nothing was saved; choose U again.");
            return 1;
        }
        Console.WriteLine($"  Got a key ending in {key[^4..]}. Checking it with GitHub...");
        using var http = Client(key);
        var answer = await http.GetAsync($"https://api.github.com/repos/{Repository}");
        if (!answer.IsSuccessStatusCode)
        {
            Console.WriteLine($"  GitHub did not accept that key ({(int)answer.StatusCode}). Nothing was saved; try again with a new key.");
            return 1;
        }
        Directory.CreateDirectory(Setup.SettingsFolder);
        await File.WriteAllTextAsync(KeyFile, key);
        if (fromClipboard)
        {
            await ClearClipboard();
        }
        Console.WriteLine("  Saved. From now on every run's results go to GitHub by themselves.");
        return 0;
    }

    // After a run: sends its folder when a key was given; says in one line what happened. Never fails the run.
    public static void Send(Report report)
    {
        if (!Configured)
        {
            Console.WriteLine("  (Menu option U sends results to GitHub, so Claude can read them without copying.)");
            return;
        }
        try
        {
            var where = SendAsync(report).GetAwaiter().GetResult();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Sent to GitHub for Claude: {where}");
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Could not send the results to GitHub: {e.Message}");
            Console.ResetColor();
        }
    }

    private static async Task<string> SendAsync(Report report)
    {
        var key = File.ReadAllText(KeyFile).Trim();
        var repository = report.Facts.FirstOrDefault(f => f.Name == "Folder").Value ?? "";
        var commit = report.Facts.FirstOrDefault(f => f.Name.StartsWith("Trench Sim version", StringComparison.Ordinal)).Value ?? "";
        var sha = ShaPattern().Match(commit) is { Success: true } m ? m.Value[..7] : "unknown";
        var run = $"runs/{Path.GetFileName(report.Folder)}-{sha}";
        var files = Collect(report.Folder)
            .Select(f => (Path: $"{run}/{f.Relative.Replace('\\', '/')}", Text: Clean(Read(f.Full), repository)))
            .ToList();
        files.Add(("LATEST.md", $"# Newest test run\n\n[{run}]({run}/report.md): {report.Title}, {report.Verdict}, version {sha}, sent {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.\n"));

        using var http = Client(key);
        var api = $"https://api.github.com/repos/{Repository}/git";
        string? parent = null;
        string? baseTree = null;
        var head = await http.GetAsync($"{api}/ref/heads/{Branch}");
        if (head.IsSuccessStatusCode)
        {
            parent = (await Json(head))["object"]!["sha"]!.GetValue<string>();
            baseTree = (await Json(await Ok(await http.GetAsync($"{api}/commits/{parent}"))))["tree"]!["sha"]!.GetValue<string>();
        }
        var entries = new JsonArray();
        foreach (var (path, text) in files)
        {
            var blob = await Post(http, $"{api}/blobs", new JsonObject { ["content"] = text, ["encoding"] = "utf-8" });
            entries.Add(new JsonObject { ["path"] = path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = blob["sha"]!.GetValue<string>() });
        }
        var treeBody = new JsonObject { ["tree"] = entries };
        if (baseTree is not null)
        {
            treeBody["base_tree"] = baseTree;
        }
        var tree = await Post(http, $"{api}/trees", treeBody);
        var commitBody = new JsonObject
        {
            ["message"] = $"Test results: {report.Title}, {report.Verdict}, version {sha}",
            ["tree"] = tree["sha"]!.GetValue<string>(),
            ["parents"] = parent is null ? new JsonArray() : new JsonArray(parent),
        };
        var made = (await Post(http, $"{api}/commits", commitBody))["sha"]!.GetValue<string>();
        if (parent is null)
        {
            await Post(http, $"{api}/refs", new JsonObject { ["ref"] = $"refs/heads/{Branch}", ["sha"] = made });
        }
        else
        {
            await Ok(await http.PatchAsync($"{api}/refs/heads/{Branch}", Body(new JsonObject { ["sha"] = made })));
        }
        File.WriteAllText(Path.Combine(report.Folder, "sent-to-github.txt"), $"{Branch}/{run} ({files.Count} files), commit {made}\n");
        return $"{files.Count} files in {Branch}/{run}";
    }

    // The run's report, its logs and the UI checks' summaries (not the screenshots or Godot's own logs: too large).
    private static IEnumerable<(string Full, string Relative)> Collect(string folder)
    {
        var report = Path.Combine(folder, "report.md");
        if (File.Exists(report))
        {
            yield return (report, "report.md");
        }
        foreach (var log in Directory.EnumerateFiles(Path.Combine(folder, "logs"), "*.txt").Order())
        {
            yield return (log, Path.GetRelativePath(folder, log));
        }
        var ui = Path.Combine(folder, "ui-check");
        if (Directory.Exists(ui))
        {
            foreach (var summary in Directory.EnumerateFiles(ui, "summary.md", SearchOption.AllDirectories).Order())
            {
                yield return (summary, Path.GetRelativePath(folder, summary));
            }
        }
    }

    private static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length <= MaxFileBytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
        return $"(cut: the first {bytes.Length - MaxFileBytes:N0} bytes left out)\n" + Encoding.UTF8.GetString(bytes, bytes.Length - MaxFileBytes, MaxFileBytes);
    }

    // Replaces what names this computer or its user: the Trench Sim folder, the app data and user folders (each also
    // written with forward or doubled slashes), the computer's name and the user name.
    public static string Clean(string text, string repository) =>
        Clean(text, repository, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.MachineName, Environment.UserName);

    public static string Clean(string text, string repository, string appData, string home, string computer, string user)
    {
        foreach (var (folder, name) in new[] { (repository, "<repo>"), (appData, "<appdata>"), (home, "<home>") })
        {
            if (folder.Length < 4)
            {
                continue;
            }
            foreach (var spelling in new[] { folder, folder.Replace('\\', '/'), folder.Replace("\\", "\\\\"), folder.Replace('/', '\\') }.Distinct())
            {
                text = text.Replace(spelling, name, StringComparison.OrdinalIgnoreCase);
            }
        }
        foreach (var (word, name) in new[] { (computer, "<computer>"), (user, "<user>") })
        {
            if (word.Length >= 2)
            {
                text = Regex.Replace(text, $@"(?<![A-Za-z0-9]){Regex.Escape(word)}(?![A-Za-z0-9])", name, RegexOptions.IgnoreCase);
            }
        }
        return text;
    }

    private static HttpClient Client(string key)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TrenchSim-TestCenter");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return http;
    }

    private static StringContent Body(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> Post(HttpClient http, string url, JsonNode body) => await Json(await Ok(await http.PostAsync(url, Body(body))));

    private static async Task<HttpResponseMessage> Ok(HttpResponseMessage answer)
    {
        if (!answer.IsSuccessStatusCode)
        {
            var said = await answer.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"GitHub answered {(int)answer.StatusCode}: {(said.Length > 200 ? said[..200] : said)}");
        }
        return answer;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage answer) =>
        JsonNode.Parse(await answer.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException("GitHub sent an empty answer.");

    // The clipboard's text (Windows: PowerShell's Get-Clipboard), or "" when it cannot be read.
    private static async Task<string> Clipboard()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "";
        }
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell", ["-NoProfile", "-Command", "Get-Clipboard -Raw"])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            var text = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return text.Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    // Leaves no key on the clipboard once it is saved.
    private static async Task ClearClipboard()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell", ["-NoProfile", "-Command", "Set-Clipboard -Value ' '"])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            await process.WaitForExitAsync();
        }
        catch (Exception)
        {
        }
    }

    [GeneratedRegex(@"^(github_pat_|ghp_)[A-Za-z0-9_]{20,}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("[0-9a-f]{40}")]
    private static partial Regex ShaPattern();
}
