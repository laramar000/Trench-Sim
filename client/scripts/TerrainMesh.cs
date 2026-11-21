using System;
using System.Collections.Generic;
using Client.Core;
using Godot;

// Graphics stage G4: builds one low-poly mesh from a terrain piece's parts (TerrainDress), so a piece costs one draw call.
// Each part's shade is a vertex colour the piece's texture is multiplied by. Table inches map to Godot units as
// everywhere on the battlefield: x = table X, y = up, z = table Y.
public static class TerrainMesh
{
    public static ArrayMesh Build(IReadOnlyList<DressPart> parts)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var part in parts)
        {
            var basis = new Basis(Vector3.Up, Mathf.DegToRad((float)part.Yaw)) * new Basis(Vector3.Back, Mathf.DegToRad((float)part.Tilt));
            var at = new Transform3D(basis, new Vector3((float)part.X, (float)part.Y, (float)part.Z));
            var half = new Vector3((float)part.SizeX, (float)part.SizeY, (float)part.SizeZ) / 2;
            var shade = new Color((float)part.Shade, (float)part.Shade, (float)part.Shade);
            foreach (var (a, b, c) in Triangles(part.Shape, half))
            {
                var n = (b - a).Cross(c - a).Normalized();
                // Faces are wound so Godot shows their front (clockwise seen from outside).
                foreach (var p in new[] { a, c, b })
                {
                    tool.SetColor(shade);
                    tool.SetNormal((at.Basis * n).Normalized());
                    tool.AddVertex(at * p);
                }
            }
        }
        return tool.Commit();
    }

    // Counter-clockwise triangles seen from outside, inside the box -half..half.
    private static IEnumerable<(Vector3, Vector3, Vector3)> Triangles(PartShape shape, Vector3 half) => shape switch
    {
        PartShape.Bag => Lump(half, 6, 4),
        PartShape.Arch => Arch(half),
        PartShape.Coil => Coil(half),
        _ => Box(half),
    };

    private static IEnumerable<(Vector3, Vector3, Vector3)> Box(Vector3 h)
    {
        Vector3 P(int x, int y, int z) => new(x * h.X, y * h.Y, z * h.Z);
        var faces = new[]
        {
            (P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1)),
            (P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1)),
            (P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1)),
            (P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1)),
            (P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)),
            (P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)),
        };
        foreach (var (a, b, c, d) in faces)
        {
            yield return (a, b, c);
            yield return (a, c, d);
        }
    }

    // A squashed sphere that fills the box: a sandbag.
    private static IEnumerable<(Vector3, Vector3, Vector3)> Lump(Vector3 h, int around, int rings)
    {
        Vector3 P(int ring, int step)
        {
            var lat = Mathf.Pi * ring / rings - Mathf.Pi / 2;
            var lon = Mathf.Tau * step / around;
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * h.X, Mathf.Sin(lat) * h.Y, Mathf.Cos(lat) * Mathf.Sin(lon) * h.Z);
        }
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < around; s++)
            {
                var a = P(r, s);
                var b = P(r, s + 1);
                var c = P(r + 1, s + 1);
                var d = P(r + 1, s);
                if (r > 0)
                {
                    yield return (a, d, b);
                }
                if (r < rings - 1)
                {
                    yield return (b, d, c);
                }
            }
        }
    }

    // A pointed arch panel facing +Z and -Z: straight sides to 60% of its height, then two arcs meeting at the top.
    private static IEnumerable<(Vector3, Vector3, Vector3)> Arch(Vector3 h)
    {
        var outline = new List<Vector2> { new(-h.X, -h.Y), new(h.X, -h.Y), new(h.X, h.Y * 0.2f) };
        for (var i = 1; i <= 3; i++)
        {
            var t = i / 4f;
            outline.Add(new Vector2(h.X * (1 - t) * (1 - t * 0.35f), h.Y * 0.2f + h.Y * 0.8f * Mathf.Sin(t * Mathf.Pi / 2)));
        }
        outline.Add(new Vector2(0, h.Y));
        for (var i = 3; i >= 1; i--)
        {
            var t = i / 4f;
            outline.Add(new Vector2(-h.X * (1 - t) * (1 - t * 0.35f), h.Y * 0.2f + h.Y * 0.8f * Mathf.Sin(t * Mathf.Pi / 2)));
        }
        outline.Add(new Vector2(-h.X, h.Y * 0.2f));
        var centre = new Vector2(0, 0);
        for (var i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            var front = (new Vector3(centre.X, centre.Y, h.Z), new Vector3(p.X, p.Y, h.Z), new Vector3(q.X, q.Y, h.Z));
            yield return front;
            yield return (new Vector3(centre.X, centre.Y, -h.Z), new Vector3(q.X, q.Y, -h.Z), new Vector3(p.X, p.Y, -h.Z));
            yield return (new Vector3(p.X, p.Y, -h.Z), new Vector3(q.X, q.Y, -h.Z), new Vector3(q.X, q.Y, h.Z));
            yield return (new Vector3(p.X, p.Y, -h.Z), new Vector3(q.X, q.Y, h.Z), new Vector3(p.X, p.Y, h.Z));
        }
    }

    // Barbed wire: a thin three-sided tube wound in loops along X, inside the box.
    private static IEnumerable<(Vector3, Vector3, Vector3)> Coil(Vector3 h)
    {
        const float wire = 0.012f;
        const int perLoop = 6;
        var loops = Math.Max(1, (int)(h.X * 2 / 0.15f));
        var steps = loops * perLoop;
        var radiusY = h.Y - wire;
        var radiusZ = h.Z - wire;
        Vector3 Centre(int i)
        {
            var angle = Mathf.Tau * i / perLoop;
            return new Vector3(-h.X + wire + (h.X * 2 - wire * 2) * i / steps, Mathf.Sin(angle) * radiusY, Mathf.Cos(angle) * radiusZ);
        }
        Vector3[] Ring(int i)
        {
            var c = Centre(i);
            var ring = new Vector3[3];
            for (var k = 0; k < 3; k++)
            {
                var a = Mathf.Tau * k / 3;
                ring[k] = c + new Vector3(0, Mathf.Sin(a) * wire, Mathf.Cos(a) * wire);
            }
            return ring;
        }
        var previous = Ring(0);
        for (var i = 1; i <= steps; i++)
        {
            var next = Ring(i);
            for (var k = 0; k < 3; k++)
            {
                var k1 = (k + 1) % 3;
                yield return (previous[k], next[k], next[k1]);
                yield return (previous[k], next[k1], previous[k1]);
            }
            previous = next;
        }
    }
}
