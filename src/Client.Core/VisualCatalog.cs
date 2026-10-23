using System.Text.Json;

namespace Client.Core;

// Graphics stage G0: which art file draws which thing on the battlefield. The art is decoration only: every shape the
// rules use (bases, footprints, heights, positions, sight) still comes from Rules.Core, and a thing with no entry, or
// whose file is missing or broken, is drawn with the plain shapes the client has always drawn. Swapping an asset is a
// file change plus one line in client/assets/catalog.json.
public sealed class VisualCatalog
{
    public const string FileName = "catalog.json";

    // The kinds of things an entry can draw, as the start of its key: "unit:Sharpshooter" (a model by its content name),
    // "unit:default" (any model without its own entry), and "ground" (the battlefield's surface).
    public static readonly IReadOnlyList<string> Kinds = ["unit:", "ground"];

    // The file types an entry may point at (Godot scenes and glTF models, and textures for the ground).
    public static readonly IReadOnlyList<string> FileTypes = [".glb", ".gltf", ".tscn", ".png"];

    private readonly Dictionary<string, string> _entries;

    private VisualCatalog(Dictionary<string, string> entries) => _entries = entries;

    public static VisualCatalog Empty { get; } = new([]);

    public IReadOnlyDictionary<string, string> Entries => _entries;

    public static string UnitKey(string modelName) => $"unit:{modelName}";

    public const string DefaultUnitKey = "unit:default";

    public const string GroundKey = "ground";

    // { "entries": { "unit:default": "units/soldier.glb" } }; paths are relative to client/assets.
    public static VisualCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("entries", out var list))
        {
            foreach (var entry in list.EnumerateObject())
            {
                var path = entry.Value.GetString() ?? "";
                if (Problem(entry.Name, path) is { } problem)
                {
                    throw new FormatException(problem);
                }
                entries[entry.Name] = path;
            }
        }
        return new(entries);
    }

    // The catalog, or null and why it could not be read (the Godot client keeps to its allowed assemblies, so the JSON
    // errors are caught here).
    public static VisualCatalog? TryParse(string json, out string? problem)
    {
        try
        {
            problem = null;
            return Parse(json);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            problem = e.Message;
            return null;
        }
    }

    // Why an entry is not allowed, or null.
    public static string? Problem(string key, string path)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(path);
        if (!Kinds.Any(k => k.EndsWith(':') ? key.StartsWith(k, StringComparison.Ordinal) && key.Length > k.Length : key == k))
        {
            return $"\"{key}\" is not a kind of thing the catalog draws ({string.Join(", ", Kinds)}).";
        }
        if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':'))
        {
            return $"\"{path}\" for {key} must be a path inside client/assets, with forward slashes.";
        }
        if (!FileTypes.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            return $"\"{path}\" for {key} is not one of {string.Join(", ", FileTypes)}.";
        }
        return null;
    }

    // The file for the first of the keys that has an entry, or null (then the plain shape is drawn).
    public string? PathFor(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            if (_entries.TryGetValue(key, out var path))
            {
                return path;
            }
        }
        return null;
    }
}
