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
//   terrain     pieces: { id, rect or parts, profile: "low" | "cover" | "wall", kind (optional): any of "difficult",
//               "dangerous", "impassable", surface (optional): "ground" | "high", walkable (optional),
//               climb (optional): { height, ladders: [{ id, at: [x, y] }] },
//               depth (optional): inches, makes a "low" piece a trench section (trench-maps stage S3, U80),
//               ruin (optional): true for a ruined building or corner, spaced by the Decimated Ruins rules (stage S4) }
//   rect        [x1, y1, x2, y2] in inches: the lower-left and upper-right corners
//   parts       instead of rect, for a ground piece shaped like an L, T or plus: a list of two or more rects that
//               do not overlap and are joined along shared edges (trench-maps stage S2)
// Every rule a TerrainBlock, Battlefield or Scenario checks still applies, and the loader adds map rules on top:
// pieces may touch but not overlap (two separate pieces that both block sight may not touch at all, U94), and the
// file name must match the id. The set-up rules that need the whole layout
// (blocking terrain, archetype spacing; trench-maps stage S4) are MapDefinition.Problems: content maps must have none.
public sealed record MapDefinition(
    string Id,
    string Name,
    string? Archetype,
    ImmutableArray<string> Supports,
    Battlefield Battlefield,
    ImmutableArray<string> ObjectiveIds)
{
    // Pieces marked as ruins (only the Decimated Ruins spacing reads them).
    public ImmutableArray<string> RuinIds { get; init; } = [];

    public Archetype? ArchetypeKind => Archetype switch
    {
        "no-mans-land" => Rules.Core.Geometry.Archetype.NoMansLand,
        "decimated-ruins" => Rules.Core.Geometry.Archetype.DecimatedRuins,
        "trench-lines" => Rules.Core.Geometry.Archetype.TrenchLines,
        _ => null,
    };

    // What the layout breaks of the set-up rules (p. 146, pp. 65-67), for a base of the given radius; empty when none.
    public ImmutableArray<LayoutProblem> Problems(Length baseRadius) =>
        MapLayout.Check(Battlefield, ObjectiveIds, baseRadius, ArchetypeKind, RuinIds);

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
                var pieces = Array(root, "terrain").Select((t, i) => Piece(t, i)).ToList();
                var terrain = pieces.Select(p => p.Block).ToList();
                for (var i = 0; i < terrain.Count; i++)
                {
                    for (var j = i + 1; j < terrain.Count; j++)
                    {
                        if (terrain[i].Footprint.Parts.Any(a => terrain[j].Footprint.Parts.Any(b => Overlap(a, b))))
                        {
                            throw new MapFormatException($"terrain '{terrain[i].Id}' and '{terrain[j].Id}' overlap (pieces may touch, not overlap).");
                        }
                    }
                }
                if (TouchingSightBlockers(terrain) is { Count: > 0 } touching)
                {
                    throw new MapFormatException(
                        $"{string.Join("; ", touching.Select(t => $"terrain '{t.A}' and '{t.B}' touch {t.How}"))}: two separate pieces that block sight " +
                        "may not touch, because sight would pass along the seam between them (U94). Make them one piece with \"parts\" " +
                        "(ground pieces of one profile) or leave a gap between them.");
                }
                var battlefield = Build("the battlefield", () => new Battlefield(LengthOf(root, "width"), LengthOf(root, "height"), terrain, zones));
                var objectives = Texts(root, "objectives");
                // The Scenario's own checks: two zones, objectives are walkable pieces on this battlefield, no repeats.
                Build("the objectives", () => new Scenario(battlefield, objectives));
                return new MapDefinition(id, name, archetype, [.. supports], battlefield, [.. objectives.Order(StringComparer.Ordinal)])
                {
                    RuinIds = [.. pieces.Where(p => p.Ruin).Select(p => p.Block.Id).Order(StringComparer.Ordinal)],
                };
            }
            catch (MapFormatException e) when (!e.Message.StartsWith(source, StringComparison.Ordinal))
            {
                throw new MapFormatException($"{where}: {e.Message}", e.InnerException);
            }
        }
    }

    // Trench-maps stage S8: a map written as a format-1 file, so a generated map can be saved, read and checked like
    // any other. Parse(ToJson(map)) gives the same battlefield (the generator tests check this for every kind).
    public static string ToJson(MapDefinition map, string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        static string In(Length l)
        {
            var abs = Math.Abs(l.Thousandths);
            var text = $"{(l.Thousandths < 0 ? "-" : "")}{abs / 1000}.{abs % 1000:000}".TrimEnd('0').TrimEnd('.');
            return text;
        }
        static string[] RectText(Rect r) => [In(r.Min.X), In(r.Min.Y), In(r.Max.X), In(r.Max.Y)];
        var ruins = map.RuinIds.ToHashSet(StringComparer.Ordinal);
        var field = map.Battlefield;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            void Strings(string name, IEnumerable<string> values)
            {
                w.WriteStartArray(name);
                foreach (var v in values)
                {
                    w.WriteStringValue(v);
                }
                w.WriteEndArray();
            }
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("id", map.Id);
            w.WriteString("name", map.Name);
            if (notes is not null)
            {
                w.WriteString("notes", notes);
            }
            w.WriteString("width", In(field.Width));
            w.WriteString("height", In(field.Height));
            if (map.Archetype is { } archetype)
            {
                w.WriteString("archetype", archetype);
            }
            Strings("supports", map.Supports);
            w.WriteStartArray("zones");
            foreach (var z in field.Zones)
            {
                w.WriteStartObject();
                w.WriteString("id", z.ZoneId);
                Strings("rect", RectText(z.Area.Bounds));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            Strings("objectives", map.ObjectiveIds);
            w.WriteStartArray("terrain");
            foreach (var t in field.Terrain)
            {
                w.WriteStartObject();
                w.WriteString("id", t.Id);
                if (t.Footprint.Parts.Length == 1)
                {
                    Strings("rect", RectText(t.Footprint.Parts[0]));
                }
                else
                {
                    w.WriteStartArray("parts");
                    foreach (var part in t.Footprint.Parts)
                    {
                        w.WriteStartArray();
                        foreach (var v in RectText(part))
                        {
                            w.WriteStringValue(v);
                        }
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                }
                w.WriteString("profile", t.Profile switch { TerrainProfile.Low => "low", TerrainProfile.Cover => "cover", _ => "wall" });
                var kinds = new List<string>();
                if (t.Kind.HasFlag(TerrainKind.Difficult))
                {
                    kinds.Add("difficult");
                }
                if (t.Kind.HasFlag(TerrainKind.Dangerous))
                {
                    kinds.Add("dangerous");
                }
                if (t.Kind.HasFlag(TerrainKind.Impassable))
                {
                    kinds.Add("impassable");
                }
                if (kinds.Count > 0)
                {
                    Strings("kind", kinds);
                }
                if (t.Surface == SurfaceLevel.High)
                {
                    w.WriteString("surface", "high");
                }
                if (t.IsWalkable != new TerrainBlock(t.Id, t.Footprint, t.Profile, t.Kind, t.Surface).IsWalkable)
                {
                    w.WriteBoolean("walkable", t.IsWalkable);
                }
                if (t.Climb is { } climb)
                {
                    w.WriteStartObject("climb");
                    w.WriteString("height", In(climb.Height));
                    w.WriteStartArray("ladders");
                    foreach (var l in climb.Ladders)
                    {
                        w.WriteStartObject();
                        w.WriteString("id", l.Id);
                        Strings("at", [In(l.At.X), In(l.At.Y)]);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                if (t.Depth is { } depth)
                {
                    w.WriteString("depth", In(depth));
                }
                if (ruins.Contains(t.Id))
                {
                    w.WriteBoolean("ruin", true);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static (TerrainBlock Block, bool Ruin) Piece(JsonElement element, int index)
    {
        var o = Object(element, $"terrain piece {index + 1}");
        Only(o, $"terrain piece {index + 1}", "id", "rect", "parts", "profile", "kind", "surface", "walkable", "climb", "depth", "ruin");
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
        var footprint = FootprintOf(o, what);
        Length? depth = o.TryGetProperty("depth", out _) ? LengthOf(o, "depth") : null;
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
        var ruin = false;
        if (o.TryGetProperty("ruin", out var r))
        {
            ruin = r.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new MapFormatException($"{what}: ruin must be true or false."),
            };
        }
        return (Build(what, () => new TerrainBlock(id, footprint, profile, kind, surface, walkable) { Climb = climb, Depth = depth }), ruin);
    }

    // U94: separate Cover or Wall pieces (towers included) whose footprints touch, along an edge or at a corner. A sight
    // ray running exactly along the seam only grazes each piece (U13), so neither blocks it. Low pieces and trenches are
    // left out on purpose: a Low piece is a 0.25" box in the sight code and can block a ray to or from a model in a trench
    // near its edge, so two touching Low pieces can leak too, but a Low piece is under the 1/2" that cover needs and never
    // gives cover; touching trench sections are one open space (U83). Pairs by id (the lower first), sorted, so the answer
    // never depends on the file's order.
    private static List<(string A, string B, string How)> TouchingSightBlockers(IReadOnlyList<TerrainBlock> terrain)
    {
        var blockers = terrain.Where(t => !t.IsTrench && t.Profile is TerrainProfile.Cover or TerrainProfile.Wall).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var found = new List<(string A, string B, string How)>();
        for (var i = 0; i < blockers.Count; i++)
        {
            for (var j = i + 1; j < blockers.Count; j++)
            {
                var edge = false;
                var corner = false;
                foreach (var a in blockers[i].Footprint.Parts)
                {
                    foreach (var b in blockers[j].Footprint.Parts)
                    {
                        var xOverlap = Math.Min(a.Max.X.Thousandths, b.Max.X.Thousandths) - Math.Max(a.Min.X.Thousandths, b.Min.X.Thousandths);
                        var yOverlap = Math.Min(a.Max.Y.Thousandths, b.Max.Y.Thousandths) - Math.Max(a.Min.Y.Thousandths, b.Min.Y.Thousandths);
                        if (xOverlap < 0 || yOverlap < 0)
                        {
                            continue;
                        }
                        if (xOverlap == 0 && yOverlap == 0)
                        {
                            corner = true;
                        }
                        else if (xOverlap == 0 || yOverlap == 0)
                        {
                            edge = true;
                        }
                    }
                }
                if (edge || corner)
                {
                    found.Add((blockers[i].Id, blockers[j].Id, edge ? "along an edge" : "at a corner"));
                }
            }
        }
        return found;
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

    // A piece's "rect", or its "parts" (two or more joined rects), never both.
    private static Footprint FootprintOf(JsonElement o, string what)
    {
        var hasRect = o.TryGetProperty("rect", out _);
        if (o.TryGetProperty("parts", out _))
        {
            if (hasRect)
            {
                throw new MapFormatException($"{what}: give rect or parts, not both.");
            }
            var parts = Array(o, "parts").Select((part, i) =>
            {
                if (part.ValueKind != JsonValueKind.Array)
                {
                    throw new MapFormatException($"{what}: part {i + 1} must be a list [x1, y1, x2, y2].");
                }
                return RectOf(part.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } s ? s : throw new MapFormatException($"{what}: part {i + 1} must be a list of text.")).ToList(), $"{what} part {i + 1}");
            }).ToList();
            if (parts.Count < 2)
            {
                throw new MapFormatException($"{what}: parts needs two or more rects (use rect for one).");
            }
            return Build(what, () => Footprint.Join(parts));
        }
        return Footprint.Of(RectOf(o, what));
    }

    private static Rect RectOf(JsonElement o, string what) => RectOf(Texts(o, "rect"), what);

    private static Rect RectOf(List<string> r, string what)
    {
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
