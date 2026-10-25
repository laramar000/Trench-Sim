using System.Collections.Immutable;
using System.Text.Json;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Sim;

// Trench and maps plan, stage S1 (approved by Jason on 2026-10-25): a map is a content file, content/maps/<id>.json,
// not C# code. A map holds only the physical table: its size, terrain pieces, deployment zones and which pieces are
// Objectives, plus the scenarios it was built and checked for. The rules engine never reads these files; it receives
// the Battlefield the loader builds, through the match setup, exactly as before.
//
// Format 1 (every field below is required unless marked optional; unknown fields are refused, so a typo cannot be
// silently ignored):
//   format      1
//   id          the map's id; must equal the file name without ".json"
//   name        shown to players
//   notes       optional; free text for people (where the layout comes from, what was chosen)
//   width, height   inches, as decimal strings with at most three decimals ("36", "16.5")
//   archetype   optional; "no-mans-land", "decimated-ruins" or "trench-lines" (1.0.2 pp. 65-67); not used by the rules yet
//   supports    scenario ids the map was built for
//   zones       exactly two deployment zones: { id, rect }
//   objectives  ids of terrain pieces that are Objectives (may be empty)
//   terrain     pieces: { id, rect, profile: "low" | "cover" | "wall", kind (optional): any of "difficult",
//               "dangerous", "impassable", surface (optional): "ground" | "high", walkable (optional),
//               climb (optional): { height, ladders: [{ id, at: [x, y] }] } }
//   rect        [x1, y1, x2, y2] in inches: the lower-left and upper-right corners
// Every rule a TerrainBlock, Battlefield or Scenario checks still applies, and the loader adds map rules on top:
// pieces may touch but not overlap, and the file name must match the id.
public sealed record MapDefinition(
    string Id,
    string Name,
    string? Archetype,
    ImmutableArray<string> Supports,
    Battlefield Battlefield,
    ImmutableArray<string> ObjectiveIds)
{
    public bool IsFor(string scenarioId) => Supports.Contains(scenarioId, StringComparer.Ordinal);

    public Scenario ToScenario() => new(Battlefield, ObjectiveIds);
}

public sealed class MapFormatException(string message, Exception? inner = null) : Exception(message, inner);

public static class MapFiles
{
    public const int Format = 1;
    public const string Folder = "maps";
    public static readonly ImmutableArray<string> Archetypes = ["no-mans-land", "decimated-ruins", "trench-lines"];

    // The maps folder next to a content file (content/maps for content/test-warbands.json).
    public static string FolderNextTo(string contentPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(contentPath)) ?? ".", Folder);

    public static MapDefinition Load(string path)
    {
        var map = Parse(File.ReadAllText(path), Path.GetFileName(path));
        if (Path.GetFileNameWithoutExtension(path) != map.Id)
        {
            throw new MapFormatException($"{Path.GetFileName(path)}: the map's id '{map.Id}' must match its file name.");
        }
        return map;
    }

    // Every map in a folder, by id.
    public static IReadOnlyDictionary<string, MapDefinition> LoadFolder(string folder) =>
        Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(Load).ToDictionary(m => m.Id, StringComparer.Ordinal);

    public static MapDefinition Parse(string json, string source = "map")
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new MapFormatException($"{source}: not valid JSON ({e.Message}).", e);
        }
        using (doc)
        {
            var where = source;
            try
            {
                var root = Object(doc.RootElement, "the file");
                Only(root, "the file", "format", "id", "name", "notes", "width", "height", "archetype", "supports", "zones", "objectives", "terrain");
                if (Required(root, "format").ValueKind != JsonValueKind.Number || Required(root, "format").GetInt32() != Format)
                {
                    throw new MapFormatException($"format must be {Format}.");
                }
                var id = Text(root, "id");
                where = $"{source} ('{id}')";
                var name = Text(root, "name");
                if (root.TryGetProperty("notes", out var notes) && notes.ValueKind != JsonValueKind.String)
                {
                    throw new MapFormatException("notes must be text.");
                }
                string? archetype = null;
                if (root.TryGetProperty("archetype", out _))
                {
                    archetype = Text(root, "archetype");
                    if (!Archetypes.Contains(archetype))
                    {
                        throw new MapFormatException($"unknown archetype '{archetype}' (use {string.Join(", ", Archetypes)}).");
                    }
                }
                var supports = Texts(root, "supports");
                var zones = Array(root, "zones").Select((z, i) =>
                {
                    var what = $"zone {i + 1}";
                    var o = Object(z, what);
                    Only(o, what, "id", "rect");
                    var zoneId = Text(o, "id");
                    return Build($"zone '{zoneId}'", () => new DeploymentZone(zoneId, Footprint.Of(RectOf(o, $"zone '{zoneId}'"))));
                }).ToList();
                var terrain = Array(root, "terrain").Select((t, i) => Piece(t, i)).ToList();
                for (var i = 0; i < terrain.Count; i++)
                {
                    for (var j = i + 1; j < terrain.Count; j++)
                    {
                        if (Overlap(terrain[i].Footprint.Bounds, terrain[j].Footprint.Bounds))
                        {
                            throw new MapFormatException($"terrain '{terrain[i].Id}' and '{terrain[j].Id}' overlap (pieces may touch, not overlap).");
                        }
                    }
                }
                var battlefield = Build("the battlefield", () => new Battlefield(LengthOf(root, "width"), LengthOf(root, "height"), terrain, zones));
                var objectives = Texts(root, "objectives");
                // The Scenario's own checks: two zones, objectives are walkable pieces on this battlefield, no repeats.
                Build("the objectives", () => new Scenario(battlefield, objectives));
                return new MapDefinition(id, name, archetype, [.. supports], battlefield, [.. objectives.Order(StringComparer.Ordinal)]);
            }
            catch (MapFormatException e) when (!e.Message.StartsWith(source, StringComparison.Ordinal))
            {
                throw new MapFormatException($"{where}: {e.Message}", e.InnerException);
            }
        }
    }

    private static TerrainBlock Piece(JsonElement element, int index)
    {
        var o = Object(element, $"terrain piece {index + 1}");
        Only(o, $"terrain piece {index + 1}", "id", "rect", "profile", "kind", "surface", "walkable", "climb");
        var id = Text(o, "id");
        var what = $"terrain '{id}'";
        var profile = Text(o, "profile") switch
        {
            "low" => TerrainProfile.Low,
            "cover" => TerrainProfile.Cover,
            "wall" => TerrainProfile.Wall,
            var other => throw new MapFormatException($"{what}: unknown profile '{other}' (use low, cover or wall)."),
        };
        var kind = TerrainKind.Open;
        if (o.TryGetProperty("kind", out _))
        {
            foreach (var k in Texts(o, "kind"))
            {
                kind |= k switch
                {
                    "difficult" => TerrainKind.Difficult,
                    "dangerous" => TerrainKind.Dangerous,
                    "impassable" => TerrainKind.Impassable,
                    _ => throw new MapFormatException($"{what}: unknown kind '{k}' (use difficult, dangerous or impassable)."),
                };
            }
        }
        var surface = SurfaceLevel.Ground;
        if (o.TryGetProperty("surface", out _))
        {
            surface = Text(o, "surface") switch
            {
                "ground" => SurfaceLevel.Ground,
                "high" => SurfaceLevel.High,
                var other => throw new MapFormatException($"{what}: unknown surface '{other}' (use ground or high)."),
            };
        }
        bool? walkable = null;
        if (o.TryGetProperty("walkable", out var w))
        {
            walkable = w.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new MapFormatException($"{what}: walkable must be true or false."),
            };
        }
        var rect = RectOf(o, what);
        Climb? climb = null;
        if (o.TryGetProperty("climb", out var c))
        {
            var co = Object(c, $"{what} climb");
            Only(co, $"{what} climb", "height", "ladders");
            var ladders = Array(co, "ladders").Select((l, i) =>
            {
                var lo = Object(l, $"{what} ladder {i + 1}");
                Only(lo, $"{what} ladder {i + 1}", "id", "at");
                var ladderId = Text(lo, "id");
                var at = Texts(lo, "at");
                if (at.Count != 2)
                {
                    throw new MapFormatException($"{what}: ladder '{ladderId}' needs at: [x, y].");
                }
                return Build($"{what} ladder '{ladderId}'", () => new Ladder(ladderId, new Point2(Inches(at[0], what), Inches(at[1], what))));
            }).ToList();
            climb = Build($"{what} climb", () => new Climb(LengthOf(co, "height"), ladders));
        }
        return Build(what, () => new TerrainBlock(id, Footprint.Of(rect), profile, kind, surface, walkable) { Climb = climb });
    }

    private static bool Overlap(Rect a, Rect b) =>
        a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;

    private static T Build<T>(string what, Func<T> make)
    {
        try
        {
            return make();
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            throw new MapFormatException($"{what}: {e.Message}", e);
        }
    }

    private static Rect RectOf(JsonElement o, string what)
    {
        var r = Texts(o, "rect");
        if (r.Count != 4)
        {
            throw new MapFormatException($"{what}: rect needs four numbers [x1, y1, x2, y2].");
        }
        return Build(what, () => new Rect(new Point2(Inches(r[0], what), Inches(r[1], what)), new Point2(Inches(r[2], what), Inches(r[3], what))));
    }

    private static Length Inches(string text, string what) => Build(what, () => Length.Parse(text));

    private static Length LengthOf(JsonElement o, string name) => Inches(Text(o, name), name);

    private static JsonElement Required(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) ? v : throw new MapFormatException($"'{name}' is missing.");

    private static JsonElement Object(JsonElement e, string what) =>
        e.ValueKind == JsonValueKind.Object ? e : throw new MapFormatException($"{what} must be an object.");

    private static void Only(JsonElement o, string what, params string[] allowed)
    {
        foreach (var p in o.EnumerateObject())
        {
            if (!allowed.Contains(p.Name, StringComparer.Ordinal))
            {
                throw new MapFormatException($"{what}: unknown field '{p.Name}'.");
            }
        }
    }

    private static string Text(JsonElement o, string name)
    {
        var v = Required(o, name);
        return v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : throw new MapFormatException($"'{name}' must be text.");
    }

    private static List<JsonElement> Array(JsonElement o, string name)
    {
        var v = Required(o, name);
        return v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray()] : throw new MapFormatException($"'{name}' must be a list.");
    }

    private static List<string> Texts(JsonElement o, string name) =>
        Array(o, name).Select(e => e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw new MapFormatException($"'{name}' must be a list of text.")).ToList();
}
