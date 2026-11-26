namespace Client.Core;

// Ragdoll terrain (Jason, 2026-11-25, "bodies should collide with terrain rather than pass through it"): the height of
// what a falling body can lie on, round the place it falls, as the battlefield draws it: the table, a trench's floor (a
// pit, its walls straight down), and the top of every terrain part drawn there (TerrainDress: sandbags, rubble, wire, a
// ruined wall, a tower's platform and posts; the plain box at the piece's height when it is drawn undressed). Ragdoll.Fall
// keeps every joint above it: a body lands on a sandbag ring or a platform, drops into a trench, slides off a tower's edge
// and stops against a wall's side. A grid of heights, built once per fall. Display only: the rules' terrain is untouched.
// Units are the battlefield's inches; X and Z across the table (Z is the table's Y), Y up.
public sealed class RagdollGround
{
    // The grid's spacing (inches): finer than any drawn part's edge matters at a figure's size.
    public const double Step = 0.08;

    // How far round the fall the grid reaches (inches): further than any body is thrown (Ragdoll's reach).
    public const double Window = 6;

    private readonly double[] _heights;
    private readonly double _x0;
    private readonly double _z0;
    private readonly int _nx;
    private readonly int _nz;

    private RagdollGround(double level, double x0, double z0, int nx, int nz, double[] heights)
    {
        Level = level;
        _x0 = x0;
        _z0 = z0;
        _nx = nx;
        _nz = nz;
        _heights = heights;
    }

    // The height outside the grid (the table).
    public double Level { get; }

    public static RagdollGround Flat(double level) => new(level, 0, 0, 0, 0, []);

    // Raises the ground to `top` over a disc (a body already lying there: 2026-11-25, a body thrown onto another lies over
    // it rather than through it). Nothing outside the grid changes.
    public void Raise(double x, double z, double radius, double top)
    {
        if (_nx == 0)
        {
            return;
        }
        var i0 = Math.Max(0, (int)Math.Floor((x - radius - _x0) / Step));
        var i1 = Math.Min(_nx - 1, (int)Math.Floor((x + radius - _x0) / Step));
        var j0 = Math.Max(0, (int)Math.Floor((z - radius - _z0) / Step));
        var j1 = Math.Min(_nz - 1, (int)Math.Floor((z + radius - _z0) / Step));
        for (var j = j0; j <= j1; j++)
        {
            for (var i = i0; i <= i1; i++)
            {
                var cx = _x0 + (i + 0.5) * Step - x;
                var cz = _z0 + (j + 0.5) * Step - z;
                if (cx * cx + cz * cz <= radius * radius && _heights[j * _nx + i] < top)
                {
                    _heights[j * _nx + i] = top;
                }
            }
        }
    }

    // The height at a point: the highest thing drawn there.
    public double At(double x, double z)
    {
        if (_nx == 0)
        {
            return Level;
        }
        var i = (int)Math.Floor((x - _x0) / Step);
        var j = (int)Math.Floor((z - _z0) / Step);
        return i < 0 || j < 0 || i >= _nx || j >= _nz ? Level : _heights[j * _nx + i];
    }

    // The highest thing drawn within `radius` of a point (the point and four round it), for a joint that has thickness.
    public double Under(double x, double z, double radius)
    {
        var r = radius;
        return Math.Max(At(x, z), Math.Max(Math.Max(At(x + r, z), At(x - r, z)), Math.Max(At(x, z + r), At(x, z - r))));
    }

    // The ground round (x, z) on this table. dressed: the pieces are drawn as their models (TerrainDress) rather than plain
    // boxes. (What the falling model stands among, the sandbags or rubble round its feet, is Ragdoll.Fall's to let go of:
    // a joint that starts inside something is free of it until it is clear.)
    public static RagdollGround Of(BoardScene scene, double x, double z, bool dressed = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var nx = (int)Math.Ceiling(2 * Window / Step);
        var nz = nx;
        var x0 = x - Window;
        var z0 = z - Window;
        var heights = new double[nx * nz];
        // The table and the trench floors.
        var pits = scene.Terrain.Where(t => t.Trench && Near(t.Area, x, z)).SelectMany(t => t.Parts.Select(p => (Rect: p, t.Depth))).ToList();
        for (var j = 0; j < nz; j++)
        {
            var cz = z0 + (j + 0.5) * Step;
            for (var i = 0; i < nx; i++)
            {
                var cx = x0 + (i + 0.5) * Step;
                var level = 0.0;
                foreach (var (rect, depth) in pits)
                {
                    if (rect.Contains(cx, cz))
                    {
                        level = Math.Min(level, -depth);
                    }
                }
                heights[j * nx + i] = level;
            }
        }
        // Every drawn part: its turned box's top over the cells its footprint covers.
        foreach (var piece in scene.Terrain.Where(t => !t.Trench && Near(t.Area, x, z)))
        {
            var boxes = dressed && TerrainDress.For(piece) is { Count: > 0 } parts
                ? parts.Select(Box)
                : piece.Parts.Select(p => (X: p.CentreX, Z: p.CentreY, SizeX: p.Width, SizeZ: p.Depth, Yaw: 0.0, Top: piece.Height));
            foreach (var (bx, bz, sx, sz, yaw, top) in boxes)
            {
                var turn = yaw * Math.PI / 180;
                var (cos, sin) = (Math.Cos(turn), Math.Sin(turn));
                var reach = Math.Sqrt(sx * sx + sz * sz) / 2;
                var i0 = Math.Max(0, (int)Math.Floor((bx - reach - x0) / Step));
                var i1 = Math.Min(nx - 1, (int)Math.Floor((bx + reach - x0) / Step));
                var j0 = Math.Max(0, (int)Math.Floor((bz - reach - z0) / Step));
                var j1 = Math.Min(nz - 1, (int)Math.Floor((bz + reach - z0) / Step));
                for (var j = j0; j <= j1; j++)
                {
                    for (var i = i0; i <= i1; i++)
                    {
                        var cx = x0 + (i + 0.5) * Step;
                        var cz = z0 + (j + 0.5) * Step;
                        if (Inside(bx, bz, sx, sz, cos, sin, cx, cz) && heights[j * nx + i] < top)
                        {
                            heights[j * nx + i] = top;
                        }
                    }
                }
            }
        }
        return new RagdollGround(0, x0, z0, nx, nz, heights);
    }

    // A part's box on the ground: a level part as it is; a tilted one (a tower's braces) as the box round its turned corners,
    // up to its highest corner, so a tower's face is solid where the braces cross it.
    private static (double X, double Z, double SizeX, double SizeZ, double Yaw, double Top) Box(DressPart part)
    {
        if (part.Tilt == 0)
        {
            return (part.X, part.Z, part.SizeX, part.SizeZ, part.Yaw, part.Y + part.SizeY / 2);
        }
        var corners = part.Corners().ToList();
        var (x0, x1) = (corners.Min(c => c.X), corners.Max(c => c.X));
        var (z0, z1) = (corners.Min(c => c.Z), corners.Max(c => c.Z));
        return ((x0 + x1) / 2, (z0 + z1) / 2, x1 - x0, z1 - z0, 0, corners.Max(c => c.Y));
    }

    private static bool Near(TableRect area, double x, double z) =>
        area.X1 >= x - Window && area.X0 <= x + Window && area.Y1 >= z - Window && area.Y0 <= z + Window;

    // Is the point inside the box (centre, size along its own X and Z, turned by yaw about the vertical, as
    // DressPart.Corners turns it)?
    private static bool Inside(double bx, double bz, double sx, double sz, double cos, double sin, double px, double pz)
    {
        var (dx, dz) = (px - bx, pz - bz);
        // Undo the turn: Corners maps (x, z) to (x cos + z sin, -x sin + z cos).
        var lx = dx * cos - dz * sin;
        var lz = dx * sin + dz * cos;
        return Math.Abs(lx) <= sx / 2 && Math.Abs(lz) <= sz / 2;
    }
}
