using Rules.Core.State;

namespace Client.Core;

// Test match (tools/TestCenter, "Play a test match"): the test center starts two client windows that connect on their
// own, so nobody types a server address or copies a code. It is the ordinary connect screen pressed for the player:
// create, join with the code or rejoin a saved seat, all answered by the server like any other request. Only when the
// client is started with --test-match; a normal start is unchanged.
//
// godot --path client -- --test-match create --server <url> [--scenario ranged|melee] [--test-window A|B] [--test-report <file>]
// godot --path client -- --test-match join --server <url> --code <code> ...
// godot --path client -- --test-match rejoin --server <url> --code <code> ...   (from this home's saved seat)
public sealed record TestMatch(string Mode, string Server, string Scenario, string? Code, string? Window, string? Report)
{
    // The options, or null when the client was not started for a test match. Problem says what is wrong when they are
    // given but cannot be used.
    public static (TestMatch? Test, string? Problem) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var mode = Arg(args, "--test-match");
        if (mode is null)
        {
            return (null, null);
        }
        var server = Arg(args, "--server");
        var scenario = Arg(args, "--scenario") ?? "ranged";
        var code = Arg(args, "--code")?.Trim().ToUpperInvariant();
        if (mode is not ("create" or "join" or "rejoin"))
        {
            return (null, $"--test-match must be create, join or rejoin, not '{mode}'.");
        }
        if (server is null || !Uri.TryCreate(server, UriKind.Absolute, out _))
        {
            return (null, "--test-match needs --server <ws://…/match>.");
        }
        if (mode != "create" && string.IsNullOrEmpty(code))
        {
            return (null, $"--test-match {mode} needs --code <match code>.");
        }
        if (!ContentNames.Choices.Contains(scenario))
        {
            return (null, $"--scenario must be one of {string.Join(", ", ContentNames.Choices)}, not '{scenario}'.");
        }
        return (new TestMatch(mode, server, scenario, code, Arg(args, "--test-window"), Arg(args, "--test-report")), null);
    }

    // What the window's title bar says, so the player can tell the two windows apart.
    public string Title(Seat? seat) =>
        $"Trench Sim test match{(Window is null ? "" : $": window {Window}")}{(seat is { } s ? $" ({Words.Seat(s)})" : "")}";

    // The report the client writes once its match is over: what the end-of-match section shows, as key=value lines, so
    // the test center can compare both windows with the server's signed record (no copying of hashes by hand).
    public static string ReportText(string? code, Seat? seat, MatchEnd end)
    {
        ArgumentNullException.ThrowIfNull(end);
        return string.Join("\n",
            $"code={code}",
            $"seat={(seat is { } s ? s.ToString() : "")}",
            $"aborted={(end.Aborted ? "yes" : "no")}",
            $"result={end.Result}",
            $"events={end.Events}",
            $"final_hash={end.Hash}") + "\n";
    }

    // Written to a temporary file first, then moved into place, so the test center never reads half a report.
    public static void WriteReport(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    private static string? Arg(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
