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
    // "unit:default" (any model without its own entry), "ground" (the battlefield's surface), "terrain:Wall" (the
    // texture of a terrain profile: Low, Cover or Wall) and "decal:crater" (a flat decoration on open ground).
    public static readonly IReadOnlyList<string> Kinds = ["unit:", "ground", "terrain:", "decal:"];

    // The file types an entry may point at (Godot scenes and glTF models, and textures for the ground).
    public static readonly IReadOnlyList<string> FileTypes = [".glb", ".gltf", ".tscn", ".png"];

    // Art pipeline (docs/art-pipeline.md, 2026-11-03): a unit figure's standing height in inches, when it is not the plain
    // body's 1.2" (a figure on a larger base, or one with tall headgear). Decoration only, like the rest of the catalog.
    public const double DefaultFigureHeight = 1.2;
    public const double MinFigureHeight = 0.5;
    public const double MaxFigureHeight = 6.0;

    private readonly Dictionary<string, string> _entries;
    private readonly Dictionary<string, double> _heights;

    private VisualCatalog(Dictionary<string, string> entries, Dictionary<string, double> heights) => (_entries, _heights) = (entries, heights);

    public static VisualCatalog Empty { get; } = new([], []);

    public IReadOnlyDictionary<string, string> Entries => _entries;

    public static string UnitKey(string modelName) => $"unit:{modelName}";

    public const string DefaultUnitKey = "unit:default";

    public const string GroundKey = "ground";

    // Graphics stage G2: the texture of a kind of terrain (by its rules profile), and a ground decoration.
    public static string TerrainKey(Rules.Core.Geometry.TerrainProfile profile) => $"terrain:{profile}";

    public static string DecalKey(string kind) => $"decal:{kind}";

    // Graphics stage G4: the texture of a tower (a Wall that models climb), drawn as timber rather than stone.
    public const string TowerKey = "terrain:Tower";

    // { "entries": { "unit:default": "units/soldier.glb" } }; paths are relative to client/assets. A unit entry may instead
    // be { "file": "units/giant.glb", "height": 1.6 }: the figure's standing height in inches.
    public static VisualCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var heights = new Dictionary<string, double>(StringComparer.Ordinal);
        if (doc.RootElement.TryGetProperty("entries", out var list))
        {
            foreach (var entry in list.EnumerateObject())
            {
                var value = entry.Value;
                string path;
                if (value.ValueKind == JsonValueKind.Object)
                {
                    path = value.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String ? file.GetString()! : "";
                    if (value.TryGetProperty("height", out var h))
                    {
                        if (!entry.Name.StartsWith("unit:", StringComparison.Ordinal) || h.ValueKind != JsonValueKind.Number
                            || h.GetDouble() is not (>= MinFigureHeight and <= MaxFigureHeight))
                        {
                            throw new FormatException($"The height of {entry.Name} must be a unit's, {MinFigureHeight}\" to {MaxFigureHeight}\".");
                        }
                        heights[entry.Name] = h.GetDouble();
                    }
                }
                else
                {
                    path = value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                }
                if (Problem(entry.Name, path) is { } problem)
                {
                    throw new FormatException(problem);
                }
                entries[entry.Name] = path;
            }
        }
        return new(entries, heights);
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

    // The standing height of the figure the first of the keys with an entry draws (1.2" unless its entry says).
    public double HeightFor(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            if (_entries.ContainsKey(key))
            {
                return _heights.TryGetValue(key, out var h) ? h : DefaultFigureHeight;
            }
        }
        return DefaultFigureHeight;
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
