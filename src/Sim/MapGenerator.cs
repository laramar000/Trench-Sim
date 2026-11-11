using System.Collections.Immutable;
using System.Globalization;
using Rules.Core.Geometry;

namespace Sim;

// Trench-maps stage S8: maps made from a seed. A generated map's id names what it is, the generator version and the
// seed ("generated-trench-lines-v1-42"), so a match record that names the map also says how to make it again; the
// record's map hash says it was that map. The same id gives the same map on every machine and build: the generator
// uses MapRandom and whole half-inches only.
//
// Every map it returns passes the set-up rules for its archetype (MapLayout.Check, pp. 65-67 and p. 146) for a base
// of DesignBaseRadius. A try that does not is thrown away and the next try, from the same seed, is made; the number
// of tries is fixed, so this is repeatable too.
//
// Trench Lines (1.0.2 p. 67, U89):
//   Trenches first: one line of sections, 2.5" or 3" wide, each 4" to 12" long (p. 62), entering on one edge and
//   reaching the opposite edge, with bends (a short cross section) and now and then a dead-end branch or a crossroads.
//   The line runs west to east or south to north.
//   Then two ruined corners, two areas of difficult ground and two landmarks, at least 1" from any trench and 3" from
//   any other piece. Areas of dangerous terrain are left out: dangerous ground has no rules in this game yet (U89).
public static class MapGenerator
{
    public const int Version = 1;
    public const string Prefix = "generated-";
    public const string TrenchLines = "trench-lines";

    // Trench-maps stage S9: No Man's Land maps for the two scenarios this build plays, and Decimated Ruins.
    public const string ClaimNoMansLand = Rules.Core.Scenarios.ClaimNoMansLand.ScenarioId;
    public const string TrenchWarfare = Rules.Core.Scenarios.TrenchWarfare.ScenarioId;
    public const string DecimatedRuins = "decimated-ruins";

    // U163: a Claim No Man's Land battlefield with towers and more kinds of terrain (wire, sandbags, bunkers, ruined walls,
    // mud). A test layout, not a No Man's Land set-up (it names no archetype), so it keeps only the blocking-terrain rule.
    public const string Battleground = "battleground";

    public static readonly ImmutableArray<string> Kinds = [TrenchLines, ClaimNoMansLand, TrenchWarfare, DecimatedRuins, Battleground];

    // The largest base a generated map is checked for: 1" radius (a 50 mm base is 0.984").
    public static readonly Length DesignBaseRadius = Length.Inch;

    public const int Tries = 200;

    // For tests and tuning: how many tries the last map made on this thread took.
    [ThreadStatic]
    public static int LastTries;

    private static readonly Length TableSize = Length.FromInches(36);

    public static string IdOf(string kind, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        return Kinds.Contains(kind) ? $"{Prefix}{kind}-v{Version}-{seed.ToString(CultureInfo.InvariantCulture)}" : throw new ArgumentException($"No generator for '{kind}'.", nameof(kind));
    }

    // The kind and seed in a generated map's id; false for any other id, another generator version, or a seed written
    // in any but the plain way (no sign, no leading zeros), so each map has one id.
    public static bool TryParse(string id, out string kind, out int seed)
    {
        ArgumentNullException.ThrowIfNull(id);
        kind = "";
        seed = 0;
        foreach (var k in Kinds)
        {
            var head = $"{Prefix}{k}-v{Version}-";
            if (!id.StartsWith(head, StringComparison.Ordinal))
            {
                continue;
            }
            var digits = id[head.Length..];
            if (digits.Length is 0 or > 10 || !digits.All(char.IsAsciiDigit) || (digits.Length > 1 && digits[0] == '0')
                || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            {
                return false;
            }
            kind = k;
            return true;
        }
        return false;
    }

    // Stage S9: "generated-<kind>" with no version or seed asks the server to pick a seed (a new map each match).
    public static bool IsRequest(string id, out string kind)
    {
        ArgumentNullException.ThrowIfNull(id);
        kind = Kinds.FirstOrDefault(k => id == Prefix + k) ?? "";
        return kind.Length > 0;
    }

    public static string RequestOf(string kind) => Kinds.Contains(kind) ? Prefix + kind : throw new ArgumentException($"No generator for '{kind}'.", nameof(kind));

    // The map a generated id names, or null when the id is not one.
    public static MapDefinition? FromId(string id) => TryParse(id, out var kind, out var seed) ? Generate(kind, seed) : null;

    public static MapDefinition Generate(string kind, int seed)
    {
        var id = IdOf(kind, seed);
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var random = new MapRandom(((ulong)(uint)seed << 32) | (uint)attempt);
            var map = kind switch
            {
                TrenchLines => TrenchLinesMap(id, seed, random),
                ClaimNoMansLand => ClaimMap(id, seed, random),
                TrenchWarfare => TrenchWarfareMap(id, seed, random),
                DecimatedRuins => RuinsMap(id, seed, random),
                Battleground => BattlegroundMap(id, seed, random, attempt),
                _ => null,
            };
            if (map is not null && map.Problems(DesignBaseRadius).IsEmpty
                && map.Supports.All(s => ScenarioSetups.Definitions[s].Unmet(map.ToScenario(), map.ArchetypeKind).IsEmpty))
            {
                LastTries = attempt + 1;
                return map;
            }
        }
        throw new InvalidOperationException($"The generator made no valid map for '{id}' in {Tries} tries.");
    }

    // Half-inch units: the table is 72 by 72.
    private const int Table = 72;

    private static Length H(int halves) => Length.FromThousandths(halves * 500L);

    private static Rect R(int x1, int y1, int x2, int y2) => new(new Point2(H(x1), H(y1)), new Point2(H(x2), H(y2)));

    private static readonly DeploymentZone[] Zones =
    [
        new("west", Footprint.Of(R(0, 0, 12, Table))),
        new("east", Footprint.Of(R(Table - 12, 0, Table, Table))),
    ];

    private static readonly Length TrenchDepth = Length.Inch;

    private static MapDefinition? TrenchLinesMap(string id, int seed, MapRandom random)
    {
        // Built along u (the direction the line runs) and across v, then laid west-east or south-north.
        var acrossNorth = random.OneIn(3);
        Rect Laid(int u1, int v1, int u2, int v2) => acrossNorth ? R(v1, u1, v2, u2) : R(u1, v1, u2, v2);
        var width = random.OneIn(2) ? 5 : 6;
        var sections = new List<(int U1, int V1, int U2, int V2)>();
        var u = 0;
        var v = random.Between(10, Table - 10 - width);
        while (true)
        {
            var left = Table - u;
            var length = left <= 24 ? left : random.Between(8, Math.Min(24, left - 8));
            sections.Add((u, v, u + length, v + width));
            u += length;
            if (u >= Table)
            {
                break;
            }
            // Two in three sections end in a bend (a cross section 4" to 8" long), when there is room for one.
            if (random.OneIn(3))
            {
                continue;
            }
            var d = random.Between(8, 16);
            var canUp = v + width + d <= Table - 4;
            var canDown = v - d >= 4;
            if (!canUp && !canDown)
            {
                continue;
            }
            var up = canUp && (!canDown || random.OneIn(2));
            sections.Add(up ? (u - width, v + width, u, v + width + d) : (u - width, v - d, u, v));
            v = up ? v + d : v - d;
        }

        // Branches: a dead end off a straight section, or a crossroads (one each way), kept 2" clear of the rest.
        var branches = random.Between(0, 2);
        var line = sections.ToList();
        for (var b = 0; b < branches; b++)
        {
            var straight = line.Where(s => s.U2 - s.U1 >= 3 * width + 4 && s.V2 - s.V1 == width).ToList();
            if (straight.Count == 0)
            {
                break;
            }
            var host = straight[random.Below(straight.Count)];
            var at = random.Between(host.U1 + width, host.U2 - 2 * width);
            var cross = random.OneIn(3);
            var first = random.OneIn(2);
            foreach (var up in cross ? new[] { true, false } : [first])
            {
                var d = random.Between(8, 12);
                var spur = up ? (at, host.V2, at + width, host.V2 + d) : (at, host.V1 - d, at + width, host.V1);
                if (spur.Item2 < 0 || spur.Item4 > Table || line.Any(s => s != host && GapHalves(s, spur) < 4))
                {
                    continue;
                }
                sections.Add(spur);
                line.Add(spur);
            }
        }

        var trenches = sections.Select((s, i) => new TerrainBlock($"trench-{i + 1}", Footprint.Of(Laid(s.U1, s.V1, s.U2, s.V2)), TerrainProfile.Low) { Depth = TrenchDepth }).ToList();
        var others = new List<TerrainBlock>();
        var ruins = new List<string>();
        var wanted = new (string Id, Func<MapRandom, int, int, Footprint> Shape, Func<string, Footprint, TerrainBlock> Make, bool Ruin)[]
        {
            ("corner-1", Corner, (i, f) => new TerrainBlock(i, f, TerrainProfile.Wall), true),
            ("corner-2", Corner, (i, f) => new TerrainBlock(i, f, TerrainProfile.Wall), true),
            ("crater-1", Patch, (i, f) => new TerrainBlock(i, f, TerrainProfile.Low, TerrainKind.Difficult), false),
            ("crater-2", Patch, (i, f) => new TerrainBlock(i, f, TerrainProfile.Low, TerrainKind.Difficult), false),
            ("landmark-1", Landmark, (i, f) => new TerrainBlock(i, f, TerrainProfile.Cover), false),
            ("landmark-2", Landmark, (i, f) => new TerrainBlock(i, f, TerrainProfile.Cover), false),
        };
        foreach (var (pieceId, shape, make, ruin) in wanted)
        {
            TerrainBlock? placed = null;
            for (var t = 0; t < 100 && placed is null; t++)
            {
                var footprint = shape(random, random.Between(0, Table), random.Between(0, Table));
                var b = footprint.Bounds;
                if (b.Max.X > TableSize || b.Max.Y > TableSize)
                {
                    continue;
                }
                if (trenches.All(x => MapLayout.GapSquared(x.Footprint, footprint) >= Square(Length.Inch))
                    && others.All(x => MapLayout.GapSquared(x.Footprint, footprint) >= Square(Length.FromInches(3))))
                {
                    placed = make(pieceId, footprint);
                }
            }
            if (placed is null)
            {
                return null;
            }
            others.Add(placed);
            if (ruin)
            {
                ruins.Add(pieceId);
            }
        }

        var battlefield = new Battlefield(TableSize, TableSize, [.. trenches, .. others], Zones);
        return new MapDefinition(id, $"Generated Trench Lines {seed.ToString(CultureInfo.InvariantCulture)}", TrenchLines, [], battlefield, [])
        {
            RuinIds = [.. ruins.Order(StringComparer.Ordinal)],
        };
    }

    // Stage S9 (pp. 65-66, U90). Pieces are placed one at a time at random spots on the half-inch grid, each at least
    // 3" from every piece already down; a piece with no room after its tries is left out, except where a count is
    // required (Decimated Ruins' six ruined buildings, Trench Warfare's three trench sections).
    private sealed class Layout(MapRandom random)
    {
        public readonly List<TerrainBlock> Pieces = [];
        public readonly List<string> Ruins = [];

        public bool Place(string id, Func<MapRandom, int, int, Footprint> shape, Func<string, Footprint, TerrainBlock> make,
            (int X1, int Y1, int X2, int Y2) area, bool ruin = false, Func<Footprint, bool>? also = null, int gapHalves = 6)
        {
            for (var t = 0; t < 100; t++)
            {
                var footprint = shape(random, random.Between(area.X1, area.X2), random.Between(area.Y1, area.Y2));
                var b = footprint.Bounds;
                if (b.Max.X > H(area.X2) || b.Max.Y > H(area.Y2))
                {
                    continue;
                }
                if (Pieces.All(x => Far(x.Footprint.Bounds, b, H(gapHalves)) || MapLayout.GapSquared(x.Footprint, footprint) >= Square(H(gapHalves))) && (also?.Invoke(footprint) ?? true))
                {
                    Pieces.Add(make(id, footprint));
                    if (ruin)
                    {
                        Ruins.Add(id);
                    }
                    return true;
                }
            }
            return false;
        }

        // True when the two boxes are at least `gap` apart along one axis (so the shapes inside them are at least that far apart).
        private static bool Far(Rect a, Rect b, Length gap) =>
            b.Min.X - a.Max.X >= gap || a.Min.X - b.Max.X >= gap || b.Min.Y - a.Max.Y >= gap || a.Min.Y - b.Max.Y >= gap;

        // The No Man's Land pieces this game has rules for (p. 65): two ruined buildings, two ruined corners, two areas
        // of difficult ground and two landmarks, as many as fit. Hills and dangerous ground are left out (U90).
        public void NoMansLandPieces((int X1, int Y1, int X2, int Y2) area)
        {
            for (var i = 1; i <= 2; i++)
            {
                Place($"ruin-{i}", Building, Cover, area, ruin: true);
                Place($"corner-{i}", Corner, WallPiece, area, ruin: true);
                Place($"crater-{i}", Patch, Rough, area);
                Place($"landmark-{i}", Landmark, Cover, area);
            }
        }

        public MapDefinition Map(string id, string name, string? archetype, ImmutableArray<string> supports, IEnumerable<string> objectives) =>
            new(id, name, archetype, supports, new Battlefield(TableSize, TableSize, Pieces, Zones), [.. objectives.Order(StringComparer.Ordinal)])
            {
                RuinIds = [.. Ruins.Order(StringComparer.Ordinal)],
            };
    }

    private static readonly (int X1, int Y1, int X2, int Y2) WholeTable = (0, 0, Table, Table);

    private static TerrainBlock Cover(string id, Footprint f) => new(id, f, TerrainProfile.Cover);

    private static TerrainBlock WallPiece(string id, Footprint f) => new(id, f, TerrainProfile.Wall);

    private static TerrainBlock Rough(string id, Footprint f) => new(id, f, TerrainProfile.Low, TerrainKind.Difficult);

    private static TerrainBlock Trench(string id, Footprint f) => new(id, f, TerrainProfile.Low) { Depth = TrenchDepth };

    // A ruined building: 3" to 5" each way, cover a model can stand in (as the preset ruined houses).
    private static Footprint Building(MapRandom random, int x, int y) =>
        Footprint.Of(R(x, y, x + random.Between(6, 10), y + random.Between(6, 10)));

    // Claim No Man's Land: the five Objectives on the printed marker positions (U32), each a sandbag position, a ruined
    // house or a shell hole; up to two trench sections 4" to 8" long in each deployment zone (p. 65: trenches only
    // wholly within a zone); then the other pieces anywhere.
    private static readonly (string Id, int X1, int Y1, int X2, int Y2)[] ClaimObjectives =
    [
        ("obj-1", 20, 8, 28, 16), ("obj-2", 20, 56, 28, 64), ("obj-3", 44, 8, 52, 16), ("obj-4", 44, 56, 52, 64), ("obj-5", 32, 32, 40, 40),
    ];

    private static MapDefinition? ClaimMap(string id, int seed, MapRandom random)
    {
        var layout = new Layout(random);
        ObjectivesAndZoneTrenches(layout, random);
        layout.NoMansLandPieces(WholeTable);
        return layout.Map(id, $"Generated No Man's Land {seed.ToString(CultureInfo.InvariantCulture)}", "no-mans-land", [ClaimNoMansLand], ClaimObjectives.Select(o => o.Id));
    }

    // The five Objectives (a sandbag position, a ruined house or a shell hole each), then up to two trench sections in each zone.
    private static void ObjectivesAndZoneTrenches(Layout layout, MapRandom random)
    {
        foreach (var (objective, x1, y1, x2, y2) in ClaimObjectives)
        {
            var f = Footprint.Of(R(x1, y1, x2, y2));
            switch (random.Below(3))
            {
                case 0:
                    layout.Pieces.Add(Cover(objective, f));
                    break;
                case 1:
                    layout.Pieces.Add(Cover(objective, f));
                    layout.Ruins.Add(objective);
                    break;
                default:
                    layout.Pieces.Add(new TerrainBlock(objective, f, TerrainProfile.Low));
                    break;
            }
        }
        foreach (var (zone, x1) in new[] { ("west", 0), ("east", Table - 12) })
        {
            var count = random.Between(0, 2);
            for (var i = 1; i <= count; i++)
            {
                var width = random.OneIn(2) ? 5 : 6;
                layout.Place($"trench-{zone}-{i}", (r, x, y) => Footprint.Of(R(x, y, x + width, y + r.Between(8, 16))), Trench, (x1, 0, x1 + 12, Table));
            }
        }
    }

    // Battleground (U163): the Claim No Man's Land objectives and zone trenches, then, between the zones and 2" apart (a gap a
    // model can walk through), towers (2" high, a ladder on each of two sides: the vertical terrain), belts of barbed wire
    // nothing crosses, sandbag lines, ruined buildings, corners and straight walls, bunkers, mud, landmarks. Each kind is as many
    // as fit; the layout must still keep the blocking-terrain rule (a model can leave its zone and reach every Objective).
    private static MapDefinition? BattlegroundMap(string id, int seed, MapRandom random, int attempt)
    {
        var layout = new Layout(random);
        ObjectivesAndZoneTrenches(layout, random);
        var between = (12, 0, Table - 12, Table);
        // Ids count up per kind of piece ("church-1", "stairs-2"), whether it came as a screen or as one of the rest.
        var counts = new Dictionary<string, int>();
        string Next(string name) => $"{name}-{counts[name] = counts.GetValueOrDefault(name) + 1}";
        void Many(string name, int low, int high, Func<MapRandom, int, int, Footprint> shape, Func<string, Footprint, TerrainBlock> make)
        {
            var count = random.Between(low, high);
            for (var i = 1; i <= count; i++)
            {
                layout.Place(Next(name), shape, make, between, gapHalves: 4);
            }
        }
        // Towers first (a tower is a wall too), then screens: tall walls of many shapes (angled stair-stepped walls, ruined churches,
        // houses and courtyards, long straight walls) across the middle, one or two to each of six bands from zone to zone, staggered
        // by chance, so that a line from one zone to the other is nearly always cut (checked below).
        // Not every map has trenches in the middle: many do, one to three sections or networks, laid first so they get room (U166).
        // After 80 tries a seed that cannot be made with them is made without (so that every seed gives a map).
        if (random.Below(100) < 90 && attempt < 80)
        {
            Many("trench-mid", 1, 3, TrenchShape, Trench);
        }
        Many("tower", 2, 4, Tower, TowerPiece);
        Many("barn", 0, 1, Barn, Cover);
        for (var band = 0; band < 6; band++)
        {
            var x0 = 12 + band * 8;
            var window = (x0, 0, Math.Min(Table - 12, x0 + 16), Table);
            var count = random.Between(1, 2);
            for (var i = 1; i <= count; i++)
            {
                var (name, shape) = ScreenShapes[random.Below(ScreenShapes.Length)];
                layout.Place(Next(name), shape, WallPiece, window, gapHalves: 4);
            }
        }
        // The rest: some of each, as many as fit, the buildings first.
        Many("church", 0, 1, Church, WallPiece);
        Many("house", 0, 2, RuinedHouse, WallPiece);
        Many("burnt", 1, 2, BurntHouse, Cover);
        Many("ruin", 0, 2, Building, Cover);
        Many("courtyard", 0, 1, Courtyard, WallPiece);
        Many("silo", 0, 2, Silo, WallPiece);
        Many("wreck", 0, 2, Wreck, Cover);
        Many("fence", 0, 2, Fence, Cover);
        Many("rubble", 1, 3, Rubble, Rough);
        Many("wire", 1, 3, Wire, (i, f) => new TerrainBlock(i, f, TerrainProfile.Low, TerrainKind.Impassable));
        Many("sandbags", 2, 4, Sandbags, Cover);
        Many("corner", 1, 2, Corner, WallPiece);
        Many("bunker", 1, 2, Bunker, WallPiece);
        Many("mud", 2, 3, Patch, Rough);
        Many("landmark", 1, 2, Landmark, Cover);
        // Repair: while too many lines are open, one more screen in a band chosen at random (up to six), so that a layout that
        // crowded out some of its screens is mended and not thrown away.
        MapDefinition Made() => layout.Map(id, $"Generated Battleground {seed.ToString(CultureInfo.InvariantCulture)}", null, [ClaimNoMansLand], ClaimObjectives.Select(o => o.Id));
        var map = Made();
        // The limit loosens a little after 150 tries, so that every seed gives a map.
        var limit = attempt < 150 ? MaxOpenLines : LooseOpenLines;
        var share = OpenLineShare(map.Battlefield, limit);
        for (var repair = 0; repair < 6 && share > limit; repair++)
        {
            var x0 = 12 + random.Below(6) * 8;
            var (name, shape) = ScreenShapes[random.Below(ScreenShapes.Length)];
            if (layout.Place(Next(name), shape, WallPiece, (x0, 0, Math.Min(Table - 12, x0 + 16), Table), gapHalves: 4))
            {
                map = Made();
                share = OpenLineShare(map.Battlefield, limit);
            }
        }
        return share <= limit && map.Battlefield.Terrain.Count(t => t.IsTower) >= 2 ? map : null;
    }

    // U164: "hard to draw a clear line from one deployment zone into the other". Of 36 lines between 6 spots in the west zone and
    // 6 in the east (a model's base at each end), the share that can see across at all (partly or fully, `HeightSight`).
    public const double MaxOpenLines = 0.15;

    public const double LooseOpenLines = 0.25;

    // `stopAbove`: once the share is certainly above this it returns early (the answer then is only "above").
    public static double OpenLineShare(Battlefield field, double stopAbove = 1)
    {
        var base05 = Length.FromThousandths(500);
        var ys = new[] { 3, 9, 15, 21, 27, 33 }.Select(y => Length.FromInches(y)).ToArray();
        var west = ys.SelectMany(y => new[] { 3000L }.Select(x => new ModelBody("west", new Point2(Length.FromThousandths(x), y), base05))).ToList();
        var east = ys.SelectMany(y => new[] { 33000L }.Select(x => new ModelBody("east", new Point2(Length.FromThousandths(x), y), base05))).ToList();
        var open = 0;
        foreach (var a in west)
        {
            foreach (var b in east)
            {
                open += HeightSight.Between(field, a, b).Visibility != Visibility.None ? 1 : 0;
                if (open > stopAbove * west.Count * east.Count)
                {
                    return open / (double)(west.Count * east.Count);
                }
            }
        }
        return open / (double)(west.Count * east.Count);
    }

    // ---- Battleground shapes (U165). Every shape is a few rectangles in half inches, joined along shared edges, then turned and
    // mirrored at random (one of the eight ways a rectangle's corners can go), so the same shape comes out in every direction.

    private static Footprint Compose(MapRandom random, int x, int y, IReadOnlyList<(int X1, int Y1, int X2, int Y2)> rects)
    {
        var (transpose, flipX, flipY) = (random.OneIn(2), random.OneIn(2), random.OneIn(2));
        // Moved so the shape's lowest corner is at 0, 0 (a Z-bend can reach to the left of where it starts).
        var (left, low) = (rects.Min(r => r.X1), rects.Min(r => r.Y1));
        rects = [.. rects.Select(r => (r.X1 - left, r.Y1 - low, r.X2 - left, r.Y2 - low))];
        var (w, h) = (rects.Max(r => r.X2), rects.Max(r => r.Y2));
        var parts = new List<Rect>();
        foreach (var (a, b, c, d) in rects)
        {
            var (x1, y1, x2, y2) = (a, b, c, d);
            if (flipX)
            {
                (x1, x2) = (w - c, w - a);
            }
            if (flipY)
            {
                (y1, y2) = (h - d, h - b);
            }
            if (transpose)
            {
                (x1, y1, x2, y2) = (y1, x1, y2, x2);
            }
            parts.Add(R(x + x1, y + y1, x + x2, y + y2));
        }
        return Footprint.Join(parts);
    }

    // The four walls of a hollow building, half an inch thick, with one breach cut into one wall: a door (1" to 1.5") or a wide
    // breach. One only: a footprint must stay in one piece, and a ring cut twice is two. `breaches` 0 leaves it closed.
    private static List<(int X1, int Y1, int X2, int Y2)> Outline(MapRandom random, int w, int h, int breaches)
    {
        // Wall 0 bottom, 1 top (both w long), 2 left, 3 right (both h - 2 long, between them).
        var walls = new (int X1, int Y1, int X2, int Y2)[] { (0, 0, w, 1), (0, h - 1, w, h), (0, 1, 1, h - 1), (w - 1, 1, w, h - 1) };
        if (breaches == 0)
        {
            return [.. walls];
        }
        var wall = random.Below(4);
        var (x1, y1, x2, y2) = walls[wall];
        var along = wall < 2;
        var length = along ? x2 - x1 : y2 - y1;
        var gap = length >= 8 && random.OneIn(3) ? random.Between(4, length - 4) : random.Between(2, Math.Min(3, length - 4));
        var at = random.Between(2, length - 2 - gap);
        var rest = walls.Where((_, i) => i != wall).ToList();
        if (along)
        {
            rest.Add((x1, y1, x1 + at, y2));
            rest.Add((x1 + at + gap, y1, x2, y2));
        }
        else
        {
            rest.Add((x1, y1, x2, y1 + at));
            rest.Add((x1, y1 + at + gap, x2, y2));
        }
        return rest;
    }

    // A stair-stepped wall at an angle: `steps` runs of `run` halves, each stepping up `rise` halves, half an inch thick.
    private static List<(int X1, int Y1, int X2, int Y2)> Stairs(int steps, int run, int rise)
    {
        var rects = new List<(int, int, int, int)>();
        int x = 0, y = 0;
        for (var k = 0; k < steps; k++)
        {
            rects.Add((x, y, x + run, y + 1));
            if (k < steps - 1)
            {
                rects.Add((x + run - 1, y + 1, x + run, y + 1 + rise));
            }
            (x, y) = (x + run, y + rise);
        }
        return rects;
    }

    // ---- Trenches in the middle (U166): 2.5" or 3" wide, 1" deep, every part 4" to 12" long (p. 62). A straight section, an
    // L-bend, a Z (two bends), a T (a branch off a section) or a crossroads; turned and mirrored at random.

    private static Footprint TrenchShape(MapRandom random, int x, int y)
    {
        var w = random.OneIn(2) ? 5 : 6;
        var a = random.Between(8, 24);
        var b = random.Between(8, 18);
        switch (random.Below(5))
        {
            case 0:
                return Compose(random, x, y, [(0, 0, a, w)]);
            case 1:
                return Compose(random, x, y, [(0, 0, a, w), (a - w, w, a, w + b)]);
            case 2:
                var c = random.Between(8, 16);
                return Compose(random, x, y, [(0, 0, a, w), (a - w, w, a, w + b), (a - w - c, w + b, a, w + b + w)]);
            case 3:
                var at = random.Between(0, a - w);
                return Compose(random, x, y, [(0, 0, a, w), (at, w, at + w, w + b)]);
            default:
                var cross = random.Between(0, a - w);
                return Compose(random, x, y, [(0, b, a, b + w), (cross, 0, cross + w, b), (cross, b + w, cross + w, b + w + b)]);
        }
    }

    // A ruined church: a nave of two long side walls and a back wall, a door gap in the front gable, and a bell tower base at a corner.
    private static Footprint Church(MapRandom random, int x, int y)
    {
        var (w, l) = (random.Between(8, 10), random.Between(14, 20));
        return Compose(random, x, y,
        [
            (0, 0, 1, l), (w - 1, 0, w, l), (1, l - 1, w - 1, l), (1, 0, 3, 1), (w - 3, 0, w - 1, 1), (w, 0, w + 4, 4),
        ]);
    }

    // A ruined house: four tall broken walls, one breach.
    private static Footprint RuinedHouse(MapRandom random, int x, int y) =>
        Compose(random, x, y, Outline(random, random.Between(8, 11), random.Between(8, 12), 1));

    // A burnt-out house: low charred walls, now and then with none missing at all.
    private static Footprint BurntHouse(MapRandom random, int x, int y) =>
        Compose(random, x, y, Outline(random, random.Between(8, 10), random.Between(8, 12), random.Between(0, 1)));

    // A walled courtyard: a big square of broken wall.
    private static Footprint Courtyard(MapRandom random, int x, int y) =>
        Compose(random, x, y, Outline(random, random.Between(12, 14), random.Between(12, 14), 1));

    // A barn: a long low outline with one opening.
    private static Footprint Barn(MapRandom random, int x, int y) =>
        Compose(random, x, y, Outline(random, random.Between(8, 10), random.Between(12, 16), 1));

    // A grain silo: a solid post 2" to 3" square.
    private static Footprint Silo(MapRandom random, int x, int y)
    {
        var side = random.Between(4, 6);
        return Footprint.Of(R(x, y, x + side, y + side));
    }

    // A wrecked cart or gun carriage: two joined blocks.
    private static Footprint Wreck(MapRandom random, int x, int y) =>
        Compose(random, x, y, [(0, 0, 5, 3), (1, 3, 4, 5)]);

    // A low fence or hedge running at an angle.
    private static Footprint Fence(MapRandom random, int x, int y) =>
        Compose(random, x, y, Stairs(random.Between(2, 4), random.Between(3, 6), random.Between(1, 3)));

    // A rubble heap: an irregular patch of two or three blocks.
    private static Footprint Rubble(MapRandom random, int x, int y)
    {
        var (a, b, c) = (random.Between(4, 7), random.Between(4, 7), random.Between(2, 4));
        return random.OneIn(2)
            ? Compose(random, x, y, [(0, 0, a, b), (a, 1, a + c, b - 1)])
            : Compose(random, x, y, [(0, 0, a, b), (a, 1, a + c, b - 1), (1, b, a - 1, b + c)]);
    }

    // The shapes a screen across the table takes: a long angled stair-stepped wall (shallow or steep), a straight broken wall, a
    // church, a ruined house, a courtyard. Each is tall wall, so each cuts the sight across the middle.
    private static readonly (string Name, Func<MapRandom, int, int, Footprint> Shape)[] ScreenShapes =
    [
        ("stairs", (r, x, y) => Compose(r, x, y, Stairs(r.Between(3, 5), r.Between(3, 5), r.Between(1, 2)))),
        ("steep", (r, x, y) => Compose(r, x, y, Stairs(r.Between(3, 4), r.Between(2, 3), r.Between(3, 4)))),
        ("stairs", (r, x, y) => Compose(r, x, y, Stairs(r.Between(4, 6), r.Between(3, 4), r.Between(1, 2)))),
        ("longwall", (r, x, y) => Footprint.Of(R(x, y, x + r.Between(1, 2), y + r.Between(14, 26)))),
        ("church", Church),
        ("church", Church),
        ("house", RuinedHouse),
        ("house", RuinedHouse),
        ("courtyard", Courtyard),
    ];

    // A tower: 2.5" or 3" square, 2" high, a ladder at the middle of its west and east sides.
    private static Footprint Tower(MapRandom random, int x, int y)
    {
        var side = random.OneIn(2) ? 5 : 6;
        return Footprint.Of(R(x, y, x + side, y + side));
    }

    private static TerrainBlock TowerPiece(string id, Footprint f)
    {
        var b = f.Bounds;
        var middle = Length.FromThousandths((b.Min.Y.Thousandths + b.Max.Y.Thousandths) / 2);
        return new TerrainBlock(id, f, TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, true)
        {
            Climb = new Climb(Length.FromInches(2), [new Ladder(id + "-west", new Point2(b.Min.X, middle)), new Ladder(id + "-east", new Point2(b.Max.X, middle))]),
        };
    }

    // A belt of barbed wire: 1" wide, 4" to 8" long, either way.
    private static Footprint Wire(MapRandom random, int x, int y) => Strip(random, x, y, 2, 8, 16);

    // A line of sandbags: 1" thick, 3" to 5" long, either way.
    private static Footprint Sandbags(MapRandom random, int x, int y) => Strip(random, x, y, 2, 6, 10);

    private static Footprint Strip(MapRandom random, int x, int y, int thick, int shortest, int longest)
    {
        var length = random.Between(shortest, longest);
        return random.OneIn(2) ? Footprint.Of(R(x, y, x + length, y + thick)) : Footprint.Of(R(x, y, x + thick, y + length));
    }

    // A bunker: a solid block 2.5" to 3" by 2" to 2.5", too tall to see over or enter.
    private static Footprint Bunker(MapRandom random, int x, int y) =>
        Footprint.Of(R(x, y, x + random.Between(5, 6), y + random.Between(4, 5)));

    // Trench Warfare: the defender's three trench sections, 8" to 9.5" long, 2.5" or 3" wide, all in one deployment zone
    // (west or east) and 3" or more apart (p. 162, U88); nothing else in either zone; the other pieces between the zones.
    private static MapDefinition? TrenchWarfareMap(string id, int seed, MapRandom random)
    {
        var layout = new Layout(random);
        var x1 = random.OneIn(2) ? 0 : Table - 12;
        var lengths = new[] { random.Between(16, 19), random.Between(16, 19), random.Between(16, 19) };
        // The spare height, split into the room before, between and after the sections.
        var spare = Table - lengths.Sum() - 2 * 6;
        var cuts = new[] { random.Between(0, spare), random.Between(0, spare), random.Between(0, spare) }.Order().ToArray();
        var y = cuts[0];
        for (var i = 0; i < 3; i++)
        {
            var width = random.OneIn(2) ? 5 : 6;
            var x = random.Between(x1, x1 + 12 - width);
            layout.Pieces.Add(Trench($"trench-{i + 1}", Footprint.Of(R(x, y, x + width, y + lengths[i]))));
            y += lengths[i] + 6 + (i < 2 ? cuts[i + 1] - cuts[i] : 0);
        }
        layout.NoMansLandPieces((12, 0, Table - 12, Table));
        return layout.Map(id, $"Generated Trench Warfare {seed.ToString(CultureInfo.InvariantCulture)}", "no-mans-land", [TrenchWarfare], []);
    }

    // Decimated Ruins (p. 66): the ruins first, each after the first between 3" and 9" from one already down: six ruined
    // buildings and two ruined corners. Then two areas of difficult ground and two landmarks, 3" from everything.
    // Dangerous ground is left out (U90). No scenario this build plays uses Decimated Ruins yet.
    private static MapDefinition? RuinsMap(string id, int seed, MapRandom random)
    {
        var layout = new Layout(random);
        bool NearARuin(Footprint f) =>
            layout.Ruins.Count == 0 || layout.Pieces.Any(p => layout.Ruins.Contains(p.Id) && MapLayout.GapSquared(p.Footprint, f) <= Square(Length.FromInches(9)));
        for (var i = 1; i <= 6; i++)
        {
            if (!layout.Place($"ruin-{i}", Building, Cover, WholeTable, ruin: true, also: NearARuin))
            {
                return null;
            }
        }
        for (var i = 1; i <= 2; i++)
        {
            layout.Place($"corner-{i}", Corner, WallPiece, WholeTable, ruin: true, also: NearARuin);
        }
        for (var i = 1; i <= 2; i++)
        {
            layout.Place($"crater-{i}", Patch, Rough, WholeTable);
            layout.Place($"landmark-{i}", Landmark, Cover, WholeTable);
        }
        return layout.Map(id, $"Generated Decimated Ruins {seed.ToString(CultureInfo.InvariantCulture)}", DecimatedRuins, [], []);
    }

    private static long Square(Length l) => l.Thousandths * l.Thousandths;

    // The gap between two rectangles in half inches along the larger axis (0 when they touch or overlap).
    private static int GapHalves((int U1, int V1, int U2, int V2) a, (int U1, int V1, int U2, int V2) b) =>
        Math.Max(Math.Max(0, Math.Max(b.U1 - a.U2, a.U1 - b.U2)), Math.Max(0, Math.Max(b.V1 - a.V2, a.V1 - b.V2)));

    // A ruined corner: two broken walls 3" to 5" long, half an inch thick, meeting at a corner, in one of four turns.
    private static Footprint Corner(MapRandom random, int x, int y)
    {
        var (a, b) = (random.Between(6, 10), random.Between(6, 10));
        return random.Below(4) switch
        {
            0 => Footprint.Join([R(x, y, x + a, y + 1), R(x, y + 1, x + 1, y + b)]),
            1 => Footprint.Join([R(x, y, x + a, y + 1), R(x + a - 1, y + 1, x + a, y + b)]),
            2 => Footprint.Join([R(x, y + b - 1, x + a, y + b), R(x, y, x + 1, y + b - 1)]),
            _ => Footprint.Join([R(x, y + b - 1, x + a, y + b), R(x + a - 1, y, x + a, y + b - 1)]),
        };
    }

    // An area of difficult ground (a crater, rubble): 2" to 4" each way.
    private static Footprint Patch(MapRandom random, int x, int y) =>
        Footprint.Of(R(x, y, x + random.Between(4, 8), y + random.Between(4, 8)));

    // A landmark (a shrine, a statue, a wreck): 1.5" to 3" each way.
    private static Footprint Landmark(MapRandom random, int x, int y) =>
        Footprint.Of(R(x, y, x + random.Between(3, 6), y + random.Between(3, 6)));
}
