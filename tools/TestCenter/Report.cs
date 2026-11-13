using System.Text;

namespace TestCenter;

// One run's results, in plain words: every line is PASS, FAIL, NOT RUN or INFO. A check counts as PASS only when it was
// actually carried out on this computer and passed; anything that did not run says NOT RUN and why.
public sealed class Report
{
    private readonly List<Row> _rows = [];
    private readonly List<(string Name, string Value)> _facts = [];
    private readonly DateTime _started = DateTime.Now;

    public Report(string title, string folder)
    {
        Title = title;
        Folder = folder;
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
    }

    public string Title { get; }

    public string Folder { get; }

    public string Logs => Path.Combine(Folder, "logs");

    // Required: a check this run is meant to carry out. When it does not run, the run is INCOMPLETE rather than PASS.
    public sealed record Row(string Result, string Check, string Detail, bool Required);

    public IReadOnlyList<Row> Rows => _rows;

    public void Fact(string name, string value) => _facts.Add((name, value));

    public void Pass(string check, string detail) => Add(new Row("PASS", check, detail, true));

    public void Fail(string check, string detail) => Add(new Row("FAIL", check, detail, true));

    public void Check(string check, bool passed, string detail) => Add(new Row(passed ? "PASS" : "FAIL", check, detail, true));

    // A check this run was meant to do that did not happen (the run stopped, a tool is missing).
    public void Missed(string check, string why) => Add(new Row("NOT RUN", check, why, true));

    // A check that is not part of this run on this computer (another platform, or a check done another way).
    public void NotRun(string check, string why) => Add(new Row("NOT RUN", check, why, false));

    public void Info(string check, string detail) => Add(new Row("INFO", check, detail, false));

    public string Verdict =>
        _rows.Any(r => r.Result == "FAIL") ? "FAIL"
        : _rows.Any(r => r.Result == "NOT RUN" && r.Required) ? "INCOMPLETE"
        : _rows.Any(r => r.Result == "PASS") ? "PASS"
        : "INCOMPLETE";

    private void Add(Row row)
    {
        _rows.Add(row);
        Console.ForegroundColor = row.Result switch
        {
            "PASS" => ConsoleColor.Green,
            "FAIL" => ConsoleColor.Red,
            "NOT RUN" => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray,
        };
        Console.Write($"  {row.Result,-7}");
        Console.ResetColor();
        Console.WriteLine($" {row.Check}: {Short(row.Detail)}");
    }

    // Writes report.md in the run's folder and shows the verdict. Returns the exit code: 0 PASS, 1 FAIL, 2 INCOMPLETE.
    public int Finish()
    {
        var verdict = Verdict;
        var ended = DateTime.Now;
        var text = new StringBuilder();
        text.AppendLine($"# {Title}");
        text.AppendLine();
        text.AppendLine($"**Result: {verdict}.** {Count("PASS")} PASS, {Count("FAIL")} FAIL, {Count("NOT RUN")} NOT RUN.");
        text.AppendLine();
        text.AppendLine(verdict switch
        {
            "PASS" => "Every check this run was meant to do ran on this computer and passed. Checks listed under NOT RUN were not carried out by this run.",
            "FAIL" => "At least one check failed: see FAIL below. The logs folder holds every program's full output.",
            _ => "The run did not carry out every check it was meant to: see NOT RUN below.",
        });
        text.AppendLine();
        text.AppendLine("| | |");
        text.AppendLine("|---|---|");
        foreach (var (name, value) in _facts)
        {
            text.AppendLine($"| {name} | {Cell(value)} |");
        }
        text.AppendLine($"| Started | {_started:yyyy-MM-dd HH:mm:ss} |");
        text.AppendLine($"| Ended | {ended:yyyy-MM-dd HH:mm:ss} ({(ended - _started).TotalMinutes:0.0} minutes) |");
        text.AppendLine();
        foreach (var result in new[] { "FAIL", "NOT RUN", "PASS", "INFO" })
        {
            var rows = _rows.Where(r => r.Result == result).ToList();
            if (rows.Count == 0)
            {
                continue;
            }
            text.AppendLine($"## {result}");
            text.AppendLine();
            text.AppendLine("| Check | Detail |");
            text.AppendLine("|---|---|");
            foreach (var row in rows)
            {
                text.AppendLine($"| {Cell(row.Check)} | {Cell(row.Detail)} |");
            }
            text.AppendLine();
        }
        text.AppendLine($"Logs: `{Logs}`");
        var path = Path.Combine(Folder, "report.md");
        File.WriteAllText(path, text.ToString());

        Console.WriteLine();
        Console.ForegroundColor = verdict switch { "PASS" => ConsoleColor.Green, "FAIL" => ConsoleColor.Red, _ => ConsoleColor.Yellow };
        Console.WriteLine($"  ==============================================");
        Console.WriteLine($"   RESULT: {verdict}   ({Count("PASS")} PASS, {Count("FAIL")} FAIL, {Count("NOT RUN")} NOT RUN)");
        Console.WriteLine($"  ==============================================");
        Console.ResetColor();
        foreach (var row in _rows.Where(r => r.Result is "FAIL" or "NOT RUN"))
        {
            Console.WriteLine($"  {row.Result}: {row.Check}: {Short(row.Detail)}");
        }
        Console.WriteLine();
        Console.WriteLine($"  Full report: {path}");
        return verdict switch { "PASS" => 0, "FAIL" => 1, _ => 2 };
    }

    private int Count(string result) => _rows.Count(r => r.Result == result);

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");

    private static string Short(string text)
    {
        var line = text.Replace("\r", "").Replace('\n', ' ');
        return line.Length > 400 ? line[..400] + "…" : line;
    }
}
