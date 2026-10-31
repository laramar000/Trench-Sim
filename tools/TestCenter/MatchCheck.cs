using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestCenter;

// Checks a finished match against the server's signed record, with trench-cli verify (the same check anyone can run):
// the signature, every event replayed from the recorded dice, the final state hash and the result. When the windows
// that played it wrote their end-of-match report (test matches), their hash, event count and result must agree too.
public static partial class MatchCheck
{
    // What a client window showed at the end of its match (written by the client with --test-report).
    public sealed record ClientEnd(string Window, string Seat, bool Aborted, string Result, int Events, string Hash);

    public static ClientEnd? ReadClient(string path, string window)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var values = File.ReadAllLines(path).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        return new ClientEnd(window, values.GetValueOrDefault("seat", ""), values.GetValueOrDefault("aborted") == "yes",
            values.GetValueOrDefault("result", ""), int.TryParse(values.GetValueOrDefault("events"), out var events) ? events : -1,
            values.GetValueOrDefault("final_hash", ""));
    }

    // Records and their data folder: the given record's folder is <data>/matches, and the server's public key is
    // <data>/keys/signing.pub.
    public static string KeyFor(string record) =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(record)))!, "keys", "signing.pub");

    public static async Task Verify(Setup setup, Report report, string record, IReadOnlyList<ClientEnd> clients)
    {
        var cli = Path.Combine(setup.Repository, "src", "Match.Cli", "bin", "Debug", "net10.0", "trench-cli.dll");
        if (!File.Exists(cli))
        {
            var (built, _) = await setup.Processes.Run("build-trench-cli", "dotnet",
                ["build", Path.Combine(setup.Repository, "src", "Match.Cli"), "-c", "Debug", "-nodeReuse:false", "-p:UseSharedCompilation=false"], report.Logs, 600);
            if (built != 0)
            {
                report.Fail("trench-cli builds", $"dotnet build src/Match.Cli: exit {built} (see logs/build-trench-cli.txt)");
                return;
            }
        }
        var key = KeyFor(record);
        var (exit, lines) = await setup.Processes.Run("verify", "dotnet", [cli, "verify", record, "--key", key], report.Logs, 120);
        var output = string.Join("\n", lines);
        var verdict = lines.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        report.Check("The server's signed record verifies (signature, every event replayed, final hash)", exit == 0 && verdict.StartsWith("Completed: ", StringComparison.Ordinal),
            $"trench-cli verify {Path.GetFileName(record)}: exit {exit} ({ExitMeaning(exit)}): {verdict}");
        var finalHash = Regex.Match(output, @"Final hash ([0-9a-f]{64}): hash matches\.");
        var hash = finalHash.Success ? finalHash.Groups[1].Value : null;
        int? count = null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(record));
            count = json.RootElement.GetProperty("body").GetProperty("events").GetArrayLength();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            report.Fail("The record can be read", $"{Path.GetFileName(record)}: {e.Message}");
        }
        report.Info("Match", $"{lines.FirstOrDefault(l => l.StartsWith("Match ", StringComparison.Ordinal))?.Trim() ?? Path.GetFileName(record)}; {count} events; final hash {hash ?? "none"}");
        if (clients.Count == 0)
        {
            return;
        }

        report.Check("Final state hash: the record and both windows agree", hash is not null && clients.All(c => c.Hash == hash),
            $"record: {hash ?? "none"} || " + string.Join(" || ", clients.Select(c => $"window {c.Window}: {c.Hash}")));
        report.Check("Event count: the record and both windows agree", count is > 0 && clients.All(c => c.Events == count),
            $"record: {count} || " + string.Join(" || ", clients.Select(c => $"window {c.Window}: {c.Events}")));
        var said = Outcome(verdict);
        var agree = said is { } outcome && clients.All(c => Agrees(outcome, c));
        report.Check("Result: the record and both windows agree", agree,
            $"record: {verdict} || " + string.Join(" || ", clients.Select(c => $"window {c.Window} ({SeatName(c.Seat)}): {c.Result}")));
    }

    // "Completed: Completed: Seat One won (VictoryPoints)." or "... a draw (Reason)." from trench-cli verify.
    public static (string? Winner, string Reason)? Outcome(string verdict)
    {
        var m = Regex.Match(verdict, @"^Completed: Completed: (?:Seat (One|Two) won|a draw) \((\w+)\)\.$");
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : null, m.Groups[2].Value) : null;
    }

    // A window's "You won: <reason>." (or You lost / Draw) names the same result as the record, seen from its seat.
    public static bool Agrees((string? Winner, string Reason) outcome, ClientEnd client)
    {
        var m = Regex.Match(client.Result, @"^(You won|You lost|Draw): (.+)\.$");
        if (!m.Success || client.Aborted)
        {
            return false;
        }
        var expected = outcome.Winner is null ? "Draw" : outcome.Winner == client.Seat ? "You won" : "You lost";
        var reason = m.Groups[2].Value switch
        {
            "a warband fled" => "OpponentFled",
            "the final turn ended" => "FinalTurnComplete",
            "every enemy model is out of action" => "AllEnemiesOutOfAction",
            "victory points after the final turn" => "VictoryPoints",
            var other => other,
        };
        return m.Groups[1].Value == expected && reason == outcome.Reason;
    }

    public static string SeatName(string seat) => seat is "One" or "Two" ? $"Seat {seat}" : "unknown seat";

    private static string ExitMeaning(int? exit) => exit switch
    {
        0 => "completed match, record valid",
        10 => "the match was aborted",
        11 => "the match was not recorded",
        20 => "INVALID record",
        30 => "cannot be verified with this key",
        64 => "usage error",
        null => "did not finish",
        _ => "unexpected",
    };

    // "Check a finished match": the newest records from test matches and from hot-seat play on this computer.
    public static List<string> RecentRecords(Setup setup)
    {
        var folders = new List<string>();
        if (Directory.Exists(setup.Results))
        {
            folders.AddRange(Directory.EnumerateDirectories(setup.Results).Select(d => Path.Combine(d, "server-data", "matches")));
        }
        var home = Environment.GetEnvironmentVariable("TRENCH_SIM_HOME") is { Length: > 0 } h
            ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trench-sim");
        folders.Add(Path.Combine(home, "hotseat-server", "matches"));
        return [.. folders.Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.json", SearchOption.TopDirectoryOnly))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(9)];
    }

    public static async Task<int> CheckFinished(Setup setup, string? given)
    {
        var report = setup.NewReport("Trench Sim match check", "check");
        var record = given;
        if (record is null)
        {
            var recent = RecentRecords(setup);
            if (recent.Count == 0)
            {
                report.Missed("A finished match was found", "no match records yet: finish a hot-seat or test match first");
                return report.Finish();
            }
            Console.WriteLine();
            for (var i = 0; i < recent.Count; i++)
            {
                Console.WriteLine($"    {i + 1}  {File.GetLastWriteTime(recent[i]):yyyy-MM-dd HH:mm}  {Path.GetFileName(recent[i])}  ({Path.GetDirectoryName(recent[i])})");
            }
            record = recent[0];
            if (setup.Interactive)
            {
                Console.Write("  Which match? Press Enter for the newest (1): ");
                if (int.TryParse(Console.ReadLine(), out var chosen) && chosen >= 1 && chosen <= recent.Count)
                {
                    record = recent[chosen - 1];
                }
            }
        }
        if (!File.Exists(record))
        {
            report.Fail("The match record exists", record);
            return report.Finish();
        }
        report.Fact("Record", record);
        await Verify(setup, report, record, []);
        return report.Finish();
    }
}
