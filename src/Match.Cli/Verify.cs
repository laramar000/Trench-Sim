using Match.Protocol;
using Sim;

namespace Match.Cli;

// trench-cli verify <record.json> [--key <public.pem>]... [--content <folder>] [--quiet]
//
// Checks a saved record and nothing else: it opens the file read-only, never connects to a server, and never puts the
// record into a match. Keys come only from --key and the trusted-keys folder, never from the record.
public static class Verify
{
    public const int Completed = 0;
    public const int Aborted = 10;
    public const int Unrecorded = 11;
    public const int Invalid = 20;
    public const int Unverifiable = 30;
    public const int Usage = 64;

    private const string UsageText = "usage: trench-cli verify <record.json> [--key <public.pem>]... [--content <folder>] [--quiet]";

    public static int Run(string[] args, TextWriter output, TextWriter error, string? trustedKeysFolder = null)
    {
        string? file = null;
        string? content = null;
        var quiet = false;
        var keyFiles = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--key" when i + 1 < args.Length:
                    keyFiles.Add(args[++i]);
                    break;
                case "--content" when i + 1 < args.Length:
                    content = args[++i];
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case var path when file is null && !path.StartsWith("--", StringComparison.Ordinal):
                    file = path;
                    break;
                default:
                    error.WriteLine(UsageText);
                    return Usage;
            }
        }
        if (file is null)
        {
            error.WriteLine(UsageText);
            return Usage;
        }

        var keys = new List<TrustedKey>();
        foreach (var keyFile in keyFiles)
        {
            if (!TryReadKey(keyFile, error, out var key))
            {
                return Usage;
            }
            keys.Add(key);
        }
        var folder = trustedKeysFolder ?? DefaultTrustedKeysFolder();
        if (Directory.Exists(folder))
        {
            foreach (var keyFile in Directory.EnumerateFiles(folder, "*.pub").Order(StringComparer.Ordinal))
            {
                if (TryReadKey(keyFile, error, out var key))
                {
                    keys.Add(key);
                }
            }
        }

        RecordVerdict verdict;
        try
        {
            verdict = RecordVerifier.VerifyFile(file, keys);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot read {file} ({e.GetType().Name}).");
            return Usage;
        }

        // --quiet prints the verdict line only; the exit code is the same.
        output.WriteLine($"{verdict.Kind}{(verdict.Reason == VerdictReason.None ? "" : $" ({verdict.Reason})")}: {verdict.Detail}");
        if (quiet)
        {
            return ExitCodeOf(verdict);
        }
        if (verdict.Body is { } body)
        {
            output.WriteLine($"Match {body.MatchId} (code {body.Code}, {body.Scenario}), written by build {body.Engine.Build} under rules {body.Engine.RulesVersion}.");
        }
        if (verdict.Kind == VerdictKind.Completed)
        {
            output.WriteLine($"Final hash {verdict.FinalHash}: hash matches.");
            output.WriteLine("This proves the signing server wrote this record and that every event follows the rules from the recorded dice. " +
                "It does not prove the dice were fair: the server that holds the key chose them.");
        }
        else
        {
            output.WriteLine("This is not a completed match.");
        }
        if (content is not null && verdict.Body is { } checkedBody && verdict.Kind is VerdictKind.Completed or VerdictKind.Aborted)
        {
            bool same;
            try
            {
                var setup = ScenarioSetups.FromFolder(content).GetValueOrDefault(checkedBody.Scenario);
                // A match created on another map (trench-maps stage S5): the same models on that map file, or on the map a
                // generated id makes again from its seed (stage S8).
                if (setup is not null && checkedBody.Map is { } mapId && ScenarioSetups.MapById(ScenarioSetups.Maps(content), mapId) is { } map)
                {
                    setup = ScenarioSetups.OnMap(setup, map);
                }
                same = setup is not null && RecordFormat.ContentFingerprint(setup) == checkedBody.ContentFingerprint;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or MapFormatException)
            {
                same = false;
            }
            output.WriteLine(same
                ? $"The setup matches the content in {content}."
                : $"Warning: the setup differs from the content in {content} (the content may have changed since).");
        }

        return ExitCodeOf(verdict);
    }

    private static int ExitCodeOf(RecordVerdict verdict) =>
        verdict.Kind switch
        {
            VerdictKind.Completed => Completed,
            VerdictKind.Aborted => Aborted,
            VerdictKind.Unrecorded => Unrecorded,
            VerdictKind.Invalid => Invalid,
            _ => Unverifiable,
        };

    public static string DefaultTrustedKeysFolder()
    {
        var home = Environment.GetEnvironmentVariable("TRENCH_SIM_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trench-sim");
        }
        return Path.Combine(home, "trusted-keys");
    }

    private static bool TryReadKey(string path, TextWriter error, out TrustedKey key)
    {
        key = null!;
        string pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Cannot read the key {path} ({e.GetType().Name}).");
            return false;
        }
        if (RecordKeys.FromPem(pem) is not { } read)
        {
            error.WriteLine($"{path} is not a P-256 public key.");
            return false;
        }
        key = read;
        return true;
    }
}
