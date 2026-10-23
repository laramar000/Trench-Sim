namespace Client.Core;

// Graphics stage G0: every art file under client/assets is listed in client/assets/ASSETS.md with where it came from,
// who made it, its licence, when it was added and what was changed. Only licences that allow use, change and sharing in
// this project are accepted. The Test Center downloads the branch as a ZIP, so the art stays small and is never a
// Git LFS pointer (GitHub's ZIPs leave LFS files out).
public static class AssetManifest
{
    public const string FileName = "ASSETS.md";

    // CC0 1.0, CC BY 4.0 (credit given in ASSETS.md and the game's credits), the SIL Open Font License 1.1 for fonts,
    // and work made for this project.
    public static readonly IReadOnlyList<string> Licences = ["CC0-1.0", "CC-BY-4.0", "OFL-1.1", "Original"];

    // All the art together, and any one file.
    public const long MaxTotalBytes = 40L * 1024 * 1024;
    public const long MaxFileBytes = 8L * 1024 * 1024;

    // Files that are not art: the manifest, the catalog, folder keepers, and what Godot writes beside imported files.
    private static readonly string[] NotArt = [FileName, VisualCatalog.FileName, ".gitkeep"];
    private static readonly string[] GodotSidecars = [".import", ".uid"];

    public sealed record Entry(string File, string Source, string Author, string Licence, string Date, string Changes);

    // The rows of the table under "## Assets": | File | Source | Author | Licence | Date | Changes |.
    public static IReadOnlyList<Entry> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var rows = new List<Entry>();
        var inTable = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inTable = line == "## Assets";
                continue;
            }
            if (!inTable || !line.StartsWith('|'))
            {
                continue;
            }
            var cells = line.Trim('|').Split('|').Select(c => c.Trim().Trim('`')).ToArray();
            if (cells.Length != 6 || cells[0] == "File" || cells[0].All(c => c is '-' or ':' or ' '))
            {
                continue;
            }
            rows.Add(new Entry(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]));
        }
        return rows;
    }

    // Everything wrong with the art folder: files not listed, rows for missing files, missing details, licences not
    // accepted, LFS pointers, files or a total over the limits, and catalog entries whose file is not listed.
    public static IReadOnlyList<string> Problems(string assetsFolder)
    {
        ArgumentNullException.ThrowIfNull(assetsFolder);
        var problems = new List<string>();
        var manifestPath = Path.Combine(assetsFolder, FileName);
        if (!File.Exists(manifestPath))
        {
            return [$"{FileName} is missing from {assetsFolder}."];
        }
        var entries = Parse(File.ReadAllText(manifestPath));
        var files = Directory.EnumerateFiles(assetsFolder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(assetsFolder, f).Replace('\\', '/'))
            .Where(f => !NotArt.Contains(Path.GetFileName(f)) && !GodotSidecars.Contains(Path.GetExtension(f)))
            .Order(StringComparer.Ordinal)
            .ToList();
        long total = 0;
        foreach (var file in files)
        {
            var full = Path.Combine(assetsFolder, file);
            var size = new FileInfo(full).Length;
            total += size;
            if (size > MaxFileBytes)
            {
                problems.Add($"{file} is {size / 1024} KB, over the {MaxFileBytes / 1024 / 1024} MB limit for one file.");
            }
            if (IsLfsPointer(full))
            {
                problems.Add($"{file} is a Git LFS pointer, not the file itself.");
            }
            if (!entries.Any(e => e.File == file))
            {
                problems.Add($"{file} is not listed in {FileName}.");
            }
        }
        if (total > MaxTotalBytes)
        {
            problems.Add($"The art is {total / 1024 / 1024} MB in all, over the {MaxTotalBytes / 1024 / 1024} MB limit.");
        }
        foreach (var entry in entries)
        {
            if (!files.Contains(entry.File))
            {
                problems.Add($"{FileName} lists {entry.File}, which is not in the folder.");
            }
            if (!Licences.Contains(entry.Licence))
            {
                problems.Add($"{entry.File}: licence \"{entry.Licence}\" is not one of {string.Join(", ", Licences)}.");
            }
            if (new[] { entry.Source, entry.Author, entry.Date, entry.Changes }.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"{entry.File}: source, author, date and changes must all be filled in.");
            }
            if (entry.Licence != "Original" && !entry.Source.StartsWith("https://", StringComparison.Ordinal))
            {
                problems.Add($"{entry.File}: the source must be the https address it came from.");
            }
        }
        var catalogPath = Path.Combine(assetsFolder, VisualCatalog.FileName);
        if (File.Exists(catalogPath))
        {
            try
            {
                foreach (var (key, path) in VisualCatalog.Parse(File.ReadAllText(catalogPath)).Entries)
                {
                    if (!entries.Any(e => e.File == path))
                    {
                        problems.Add($"{VisualCatalog.FileName}: {key} draws {path}, which is not listed in {FileName}.");
                    }
                }
            }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
            {
                problems.Add($"{VisualCatalog.FileName}: {e.Message}");
            }
        }
        return problems;
    }

    public static long TotalBytes(string assetsFolder) =>
        Directory.EnumerateFiles(assetsFolder, "*", SearchOption.AllDirectories)
            .Where(f => !NotArt.Contains(Path.GetFileName(f)) && !GodotSidecars.Contains(Path.GetExtension(f)))
            .Sum(f => new FileInfo(f).Length);

    private static bool IsLfsPointer(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[40];
        var read = stream.Read(head, 0, head.Length);
        return System.Text.Encoding.ASCII.GetString(head, 0, read).StartsWith("version https://git-lfs", StringComparison.Ordinal);
    }
}
