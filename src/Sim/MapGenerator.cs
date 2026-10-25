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

    public static readonly ImmutableArray<string> Kinds = [TrenchLines];

    // The largest base a generated map is checked for: 1" radius (a 50 mm base is 0.984").
    public static readonly Length DesignBaseRadius = Length.Inch;

    public const int Tries = 200;

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
                _ => null,
            };
            if (map is not null && map.Problems(DesignBaseRadius).IsEmpty)
            {
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
        return new MapDefinition(id, $"Trench Lines {seed.ToString(CultureInfo.InvariantCulture)}", TrenchLines, [], battlefield, [])
        {
            RuinIds = [.. ruins.Order(StringComparer.Ordinal)],
        };
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
