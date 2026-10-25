using Rules.Core.Geometry;

namespace Client.Core;

// Graphics stage G4: what a terrain piece is drawn with instead of a plain box. Decoration only: every part stays inside
// the piece's footprint and under its drawn height, the rules still use the footprint and height, and no part adds a
// walkway or new vertical terrain. The parts depend only on the piece (its id, footprint, kind and height), so every
// player and every redraw draws the same ones.
//
// Coordinates are table inches: X and Z across the table (Z is the table's Y), Y up from the ground.
public enum PartShape
{
    // A box.
    Block,

    // A sandbag: a squashed rounded lump that fills its box.
    Bag,

    // A pointed (gothic) arch panel facing along Z, filling its box.
    Arch,

    // A coil of barbed wire running along X, filling its box.
    Coil,
}

// One part: its shape, the centre and size of its box (before turning), a turn about the vertical (Yaw) and then a tilt
// about its own Z axis (Tilt, used by braces), and a colour the piece's texture is multiplied by.
public sealed record DressPart(PartShape Shape, double X, double Y, double Z, double SizeX, double SizeY, double SizeZ, double Yaw, double Tilt, double Shade)
{
    // The corners of the part's turned box.
    public IEnumerable<(double X, double Y, double Z)> Corners()
    {
        var yaw = Yaw * Math.PI / 180;
        var tilt = Tilt * Math.PI / 180;
        foreach (var sx in new[] { -1, 1 })
        {
            foreach (var sy in new[] { -1, 1 })
            {
                foreach (var sz in new[] { -1, 1 })
                {
                    var (x, y, z) = (sx * SizeX / 2, sy * SizeY / 2, sz * SizeZ / 2);
                    // tilt about Z, then yaw about Y
                    (x, y) = (x * Math.Cos(tilt) - y * Math.Sin(tilt), x * Math.Sin(tilt) + y * Math.Cos(tilt));
                    (x, z) = (x * Math.Cos(yaw) + z * Math.Sin(yaw), -x * Math.Sin(yaw) + z * Math.Cos(yaw));
                    yield return (X + x, Y + y, Z + z);
                }
            }
        }
    }
}

public static class TerrainDress
{
    // Which texture a piece is dressed with: catalog key "terrain:Tower" for towers, otherwise "terrain:<profile>".
    public static string TextureKey(TerrainShape piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return piece.Tower ? VisualCatalog.TowerKey : VisualCatalog.TerrainKey(piece.Profile);
    }

    public static IReadOnlyList<DressPart> For(TerrainShape piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        var random = new Spread(piece.Id);
        var parts = new List<DressPart>();
        // A trench is drawn as a pit in the ground (BoardView, TrenchPits), not dressed.
        if (piece.Trench)
        {
            return parts;
        }
        if (piece.Tower)
        {
            Tower(piece, parts);
        }
        else if (piece.Profile == TerrainProfile.Wall)
        {
            Wall(piece, random, parts);
        }
        else if (piece.Profile == TerrainProfile.Cover)
        {
            Sandbags(piece, random, parts);
        }
        else
        {
            Rough(piece, random, parts);
        }
        return parts;
    }

    // Inside the footprint and under the height (a hair of rounding allowed).
    public static bool Inside(TerrainShape piece, DressPart part)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(part);
        const double hair = 1e-6;
        var a = piece.Area;
        return part.Corners().All(c => c.X >= a.X0 - hair && c.X <= a.X1 + hair && c.Z >= a.Y0 - hair && c.Z <= a.Y1 + hair
            && c.Y >= -hair && c.Y <= piece.Height + hair);
    }

    // Cover: a ring of sandbags round the footprint, in courses with staggered joints, as high as the piece.
    private static void Sandbags(TerrainShape piece, Spread random, List<DressPart> parts)
    {
        var a = piece.Area;
        const double length = 0.6, depth = 0.3;
        var courses = Math.Max(1, (int)Math.Round(piece.Height / 0.25));
        var high = piece.Height / courses;
        for (var course = 0; course < courses; course++)
        {
            var y = high * (course + 0.5);
            var stagger = course % 2 == 0 ? 0 : length / 2;
            // The two sides along X take the corners; the two along Z fit between them.
            foreach (var z in new[] { a.Y0 + depth / 2, a.Y1 - depth / 2 })
            {
                Row(a.X0, a.X1, stagger, (from, to) => parts.Add(new DressPart(PartShape.Bag, (from + to) / 2, y, z, to - from, high, depth, 0, 0, Shade(random))));
            }
            foreach (var x in new[] { a.X0 + depth / 2, a.X1 - depth / 2 })
            {
                Row(a.Y0 + depth, a.Y1 - depth, stagger, (from, to) => parts.Add(new DressPart(PartShape.Bag, x, y, (from + to) / 2, to - from, high, depth, 90, 0, Shade(random))));
            }
        }

        static void Row(double start, double end, double stagger, Action<double, double> bag)
        {
            var at = start;
            var first = stagger > 0 ? stagger : length;
            while (end - at > 0.05)
            {
                var to = Math.Min(end, at + first);
                bag(at, to);
                at = to;
                first = length;
            }
        }
    }

    // Low (rough ground): a flat bed of rubble, scattered chunks and two coils of barbed wire on stakes.
    private static void Rough(TerrainShape piece, Spread random, List<DressPart> parts)
    {
        var a = piece.Area;
        var h = piece.Height;
        parts.Add(new DressPart(PartShape.Block, a.CentreX, 0.02, a.CentreY, a.Width - 0.1, 0.04, a.Depth - 0.1, 0, 0, 0.9));
        var chunks = (int)Math.Round(a.Width * a.Depth / 1.5);
        for (var i = 0; i < chunks; i++)
        {
            var size = 0.2 + random.Next() * 0.3;
            var tall = Math.Min(h * 0.8, 0.06 + random.Next() * 0.12);
            var reach = size * 0.75;
            if (a.Width <= 2 * reach || a.Depth <= 2 * reach)
            {
                break;
            }
            var x = a.X0 + reach + random.Next() * (a.Width - 2 * reach);
            var z = a.Y0 + reach + random.Next() * (a.Depth - 2 * reach);
            parts.Add(new DressPart(PartShape.Block, x, tall / 2, z, size, tall, size * (0.6 + random.Next() * 0.4), random.Next(0, 90), 0, Shade(random)));
        }
        // Wire along the longer side, a third of the way in from each long edge.
        var alongX = a.Width >= a.Depth;
        var (from, to) = alongX ? (a.X0 + 0.25, a.X1 - 0.25) : (a.Y0 + 0.25, a.Y1 - 0.25);
        var coil = Math.Min(h, 0.24);
        foreach (var third in new[] { 1.0 / 3, 2.0 / 3 })
        {
            var across = alongX ? a.Y0 + a.Depth * third : a.X0 + a.Width * third;
            var (x, z) = alongX ? ((from + to) / 2, across) : (across, (from + to) / 2);
            parts.Add(new DressPart(PartShape.Coil, x, coil / 2, z, to - from, coil, coil, alongX ? 0 : 90, 0, 0.35));
            for (var stake = from; stake <= to + 1e-9; stake += Math.Max(0.5, (to - from) / 6))
            {
                var (sx, sz) = alongX ? (stake, across) : (across, stake);
                parts.Add(new DressPart(PartShape.Block, sx, h / 2, sz, 0.04, h, 0.04, 0, 0, 0.5));
            }
        }
    }

    // Wall: ruined masonry. The body, a broken top of uneven blocks no higher than the wall, and pointed arches set in
    // both long faces of a tall enough wall.
    private static void Wall(TerrainShape piece, Spread random, List<DressPart> parts)
    {
        var a = piece.Area;
        var h = piece.Height;
        var alongX = a.Width >= a.Depth;
        var (length, thick) = alongX ? (a.Width, a.Depth) : (a.Depth, a.Width);
        var yaw = alongX ? 0 : 90;
        const double inset = 0.03;
        var body = piece.Platform ? h : h * 0.8;
        parts.Add(new DressPart(PartShape.Block, a.CentreX, body / 2, a.CentreY, alongX ? a.Width : a.Width - 2 * inset, body, alongX ? a.Depth - 2 * inset : a.Depth, 0, 0, 1));
        if (!piece.Platform)
        {
            var count = Math.Max(1, (int)Math.Round(length / 0.5));
            var block = length / count;
            for (var i = 0; i < count; i++)
            {
                var top = random.Next() < 0.25 ? body + (h - body) * 0.2 : body + (h - body) * (0.55 + random.Next() * 0.45);
                var along = -length / 2 + block * (i + 0.5);
                var (x, z) = alongX ? (a.CentreX + along, a.CentreY) : (a.CentreX, a.CentreY + along);
                parts.Add(new DressPart(PartShape.Block, x, (body + top) / 2, z, block * 0.96, top - body, thick - 2 * inset, yaw, 0, Shade(random)));
            }
        }
        if (h >= 1.5 && length >= 2)
        {
            var arches = (int)(length / 2);
            var archHigh = Math.Min(1.4, h * 0.5);
            for (var i = 0; i < arches; i++)
            {
                var along = -length / 2 + length / arches * (i + 0.5);
                foreach (var side in new[] { -1, 1 })
                {
                    var across = side * (thick / 2 - inset / 2);
                    var (x, z) = alongX ? (a.CentreX + along, a.CentreY + across) : (a.CentreX + across, a.CentreY + along);
                    parts.Add(new DressPart(PartShape.Arch, x, 0.3 + archHigh / 2, z, 0.6, archHigh, inset, yaw, 0, 0.3));
                }
            }
        }
    }

    // Tower: a crude timber tower. Plank walls, a post at each corner and an X of braces on each face, all inside the
    // footprint and no higher than the platform (the deck and ladders are drawn as before).
    private static void Tower(TerrainShape piece, List<DressPart> parts)
    {
        var a = piece.Area;
        var h = piece.Height;
        const double band = 0.08, post = 0.18, brace = 0.06;
        parts.Add(new DressPart(PartShape.Block, a.CentreX, h / 2, a.CentreY, a.Width - 2 * band, h, a.Depth - 2 * band, 0, 0, 1));
        foreach (var x in new[] { a.X0 + post / 2, a.X1 - post / 2 })
        {
            foreach (var z in new[] { a.Y0 + post / 2, a.Y1 - post / 2 })
            {
                parts.Add(new DressPart(PartShape.Block, x, h / 2, z, post, h, post, 0, 0, 0.7));
            }
        }
        // Braces lie in the outer band of each face, between the posts.
        foreach (var (alongX, offset) in new[] { (true, a.Y0 + band / 2), (true, a.Y1 - band / 2), (false, a.X0 + band / 2), (false, a.X1 - band / 2) })
        {
            var span = (alongX ? a.Width : a.Depth) - 2 * post;
            var angle = Math.Atan2(h, span) * 180 / Math.PI;
            // The longest brace that fits: its turned box must stay within the span and the height.
            var length = Fit(span, h, brace);
            var (x, z) = alongX ? (a.CentreX, offset) : (offset, a.CentreY);
            foreach (var sign in new[] { -1, 1 })
            {
                parts.Add(new DressPart(PartShape.Block, x, h / 2, z, length, brace, band * 0.9, alongX ? 0 : 90, sign * angle, 0.6));
            }
        }

        static double Fit(double span, double high, double width)
        {
            var angle = Math.Atan2(high, span);
            // A box of this length and width, tilted by the angle, reaches length*cos + width*sin across and
            // length*sin + width*cos up.
            return Math.Min((span - width * Math.Sin(angle)) / Math.Cos(angle), (high - width * Math.Cos(angle)) / Math.Sin(angle));
        }
    }

    private static double Shade(Spread random) => 0.85 + random.Next() * 0.2;
}
