#!/usr/bin/env python3
"""Graphics stage G3: builds the four original low-poly unit figures as glTF binaries (client/assets/units/*.glb).

Made for this project (licence "Original"), from boxes and prisms only, flat-shaded (PS1 style), no textures.
Every figure is modelled in inches, standing on y = 0, facing +z, exactly 1.2" tall and at most 0.9" wide, so the
client scales it by about 1 to the plain body's height and it stays inside a 25 mm base. The seat colour is never
baked in: the parts the client paints with the model's seat colour (the sash, the pennant) are one node named
"Accent". Run: python3 tools/art/make_figures.py (writes the files; same input gives the same bytes).
"""
import json
import math
import os
import struct
import sys

HEIGHT = 1.2
MAX_WIDTH = 0.9

# Muted palette from the art direction (umber, olive, charcoal, gunmetal, bone, parchment, rust, candlelight).
PALETTE = {
    "coat": (0.30, 0.25, 0.19),
    "olive": (0.31, 0.31, 0.22),
    "charcoal": (0.15, 0.15, 0.16),
    "gunmetal": (0.30, 0.32, 0.35),
    "bone": (0.80, 0.75, 0.62),
    "leather": (0.34, 0.23, 0.15),
    "wood": (0.42, 0.31, 0.19),
    "rust": (0.48, 0.26, 0.15),
    "candle": (0.95, 0.74, 0.30),
    "accent": (0.75, 0.75, 0.75),  # replaced at run time by the seat colour
}


def rot_x(p, a):
    x, y, z = p
    c, s = math.cos(a), math.sin(a)
    return (x, y * c - z * s, y * s + z * c)


def rot_z(p, a):
    x, y, z = p
    c, s = math.cos(a), math.sin(a)
    return (x * c - y * s, x * s + y * c, z)


class Part:
    """Triangles of one material, flat-shaded."""

    def __init__(self):
        self.tris = []

    def quad(self, a, b, c, d):
        self.tris += [(a, b, c), (a, c, d)]

    def box(self, centre, size, tilt_x=0.0, tilt_z=0.0, taper=1.0):
        """A box (taper < 1 narrows its top), turned about x then z around its own centre."""
        cx, cy, cz = centre
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        corners = []
        for y in (-hy, hy):
            k = taper if y > 0 else 1.0
            for x, z in ((-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)):
                corners.append((x * k, y, z * k))
        moved = []
        for p in corners:
            p = rot_x(p, tilt_x)
            p = rot_z(p, tilt_z)
            moved.append((p[0] + cx, p[1] + cy, p[2] + cz))
        b0, b1, b2, b3, t0, t1, t2, t3 = moved
        self.quad(b0, b3, b2, b1)  # bottom
        self.quad(t0, t1, t2, t3)  # top
        self.quad(b0, b1, t1, t0)  # back (-z)
        self.quad(b1, b2, t2, t1)  # right (+x)
        self.quad(b2, b3, t3, t2)  # front (+z)
        self.quad(b3, b0, t0, t3)  # left (-x)

    def pyramid(self, base_centre, half, height):
        """A four-sided point (helmet spike, hood, banner finial)."""
        cx, cy, cz = base_centre
        a, b, c, d = (cx - half, cy, cz - half), (cx + half, cy, cz - half), (cx + half, cy, cz + half), (cx - half, cy, cz + half)
        top = (cx, cy + height, cz)
        self.quad(a, d, c, b)
        self.tris += [(a, b, top), (b, c, top), (c, d, top), (d, a, top)]


class Figure:
    def __init__(self, name):
        self.name = name
        self.parts = {}

    def part(self, material):
        return self.parts.setdefault(material, Part())

    # The body every figure shares, chunky so it reads from the high camera: boots, legs, greatcoat skirt, torso, a sash
    # and a shoulder mantle in the seat colour (Accent, seen from every side and from above), bone mask, helmet.
    def soldier(self, lean=0.0, helmet="brim"):
        p = self.part
        p("charcoal").box((-0.11, 0.05, 0.03), (0.16, 0.10, 0.24))
        p("charcoal").box((0.11, 0.05, 0.03), (0.16, 0.10, 0.24))
        p("olive").box((-0.11, 0.24, 0.0), (0.15, 0.30, 0.16))
        p("olive").box((0.11, 0.24, 0.0), (0.15, 0.30, 0.16))
        p("coat").box((0.0, 0.42, 0.0), (0.50, 0.24, 0.34), taper=0.85)
        p("coat").box((0.0, 0.64, 0.0), (0.44, 0.30, 0.30), tilt_x=lean)
        p("accent").box((0.0, 0.62, 0.0), (0.47, 0.14, 0.33), tilt_x=lean)
        p("leather").box((0.0, 0.53, 0.0), (0.46, 0.04, 0.32))
        shoulder_z = math.sin(lean) * 0.16
        p("accent").box((0.0, 0.81, shoulder_z), (0.58, 0.07, 0.36), taper=0.8)
        head_z = math.sin(lean) * 0.25
        p("bone").box((0.0, 0.92, head_z), (0.19, 0.17, 0.19))
        p("charcoal").box((0.0, 0.93, head_z + 0.097), (0.13, 0.035, 0.01))  # eye slit
        if helmet == "brim":
            p("gunmetal").box((0.0, 1.015, head_z), (0.34, 0.03, 0.34))
            p("gunmetal").box((0.0, 1.06, head_z), (0.22, 0.08, 0.22), taper=0.7)
        elif helmet == "hood":
            p("coat").box((0.0, 0.97, head_z - 0.02), (0.26, 0.15, 0.26))
            p("coat").pyramid((0.0, 1.045, head_z - 0.03), 0.12, 0.12)
        elif helmet == "great":
            p("gunmetal").box((0.0, 0.97, head_z), (0.25, 0.25, 0.25), taper=0.9)
            p("rust").box((0.0, 1.11, head_z), (0.05, 0.05, 0.20))
        return head_z

    def arms(self, left, right):
        """Each arm: (shoulder-to-hand tilt about x, tilt about z)."""
        for side, (tx, tz) in ((-1, left), (1, right)):
            self.part("coat").box((side * 0.28, 0.64, 0.07), (0.12, 0.32, 0.12), tilt_x=tx, tilt_z=tz)


def rifleman():
    f = Figure("rifleman")
    f.soldier()
    f.arms((-0.9, 0.2), (-0.9, -0.2))
    # A long rifle held up across the chest: stock low on the right, barrel high on the left (the tallest point).
    f.part("wood").box((0.04, 0.78, 0.22), (0.07, 0.62, 0.07), tilt_z=0.62)
    f.part("gunmetal").box((-0.19, 1.13, 0.22), (0.04, 0.22, 0.04), tilt_z=0.62)
    f.part("leather").box((0.0, 0.68, -0.18), (0.30, 0.22, 0.08))  # pack
    return f


def skirmisher():
    f = Figure("skirmisher")
    f.soldier(lean=0.28, helmet="hood")
    f.arms((-1.2, 0.1), (-1.2, -0.1))
    # A short carbine thrust forward and up, bayonet first (the tallest point).
    f.part("wood").box((0.05, 0.86, 0.30), (0.07, 0.32, 0.07), tilt_x=0.75)
    f.part("gunmetal").box((0.05, 1.10, 0.43), (0.035, 0.22, 0.035), tilt_x=0.75)
    f.part("leather").box((0.26, 0.47, 0.06), (0.10, 0.15, 0.18))  # satchel
    f.part("rust").box((-0.27, 0.47, 0.0), (0.06, 0.16, 0.06))  # grenade
    return f


def shield():
    f = Figure("shield")
    f.soldier(helmet="great")
    f.part("gunmetal").box((-0.30, 0.84, 0.0), (0.18, 0.09, 0.30))  # pauldrons
    f.part("gunmetal").box((0.30, 0.84, 0.0), (0.18, 0.09, 0.30))
    f.arms((-0.5, 0.15), (0.0, -0.1))
    # A tall iron pavise in front, its face in the seat colour, a pointed top, a rust band and a bone cross.
    f.part("gunmetal").box((0.0, 0.50, 0.28), (0.70, 0.80, 0.05))
    f.part("gunmetal").pyramid((0.0, 0.90, 0.28), 0.12, 0.12)
    f.part("accent").box((0.0, 0.50, 0.31), (0.58, 0.66, 0.015))
    f.part("rust").box((0.0, 0.62, 0.325), (0.66, 0.06, 0.02))
    f.part("bone").box((0.0, 0.42, 0.325), (0.09, 0.32, 0.02))
    f.part("bone").box((0.0, 0.49, 0.325), (0.26, 0.08, 0.02))
    # A raised mace behind the shield (the tallest point).
    f.part("wood").box((0.33, 0.96, 0.0), (0.05, 0.40, 0.05))
    f.part("charcoal").box((0.33, 1.17, 0.0), (0.12, 0.12, 0.12))
    return f


def standard():
    f = Figure("standard")
    f.soldier()
    f.arms((-0.4, 0.0), (-0.6, -0.2))
    # A tall pole (the tallest point) with a crossbar, a pennant in the seat colour, a finial and a hanging lantern.
    f.part("wood").box((0.36, 0.64, 0.10), (0.05, 1.16, 0.05))
    f.part("wood").box((0.20, 1.10, 0.10), (0.36, 0.035, 0.035))
    f.part("accent").box((0.17, 0.93, 0.10), (0.30, 0.30, 0.02))
    f.part("bone").pyramid((0.36, 1.22, 0.10), 0.035, 0.07)
    f.part("gunmetal").box((0.05, 1.04, 0.10), (0.012, 0.09, 0.012))
    f.part("candle").box((0.05, 0.95, 0.10), (0.08, 0.10, 0.08))
    return f


FIGURES = [rifleman, skirmisher, shield, standard]


def normal(a, b, c):
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def bounds(figure):
    pts = [p for part in figure.parts.values() for t in part.tris for p in t]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    return lo, hi


def normalise(figure):
    """Stands the figure on y = 0 and scales it to exactly HEIGHT; checks the width."""
    lo, hi = bounds(figure)
    k = HEIGHT / (hi[1] - lo[1])
    for part in figure.parts.values():
        part.tris = [tuple((p[0] * k, (p[1] - lo[1]) * k, p[2] * k) for p in t) for t in part.tris]
    lo, hi = bounds(figure)
    width = max(hi[0] - lo[0], hi[2] - lo[2])
    if width > MAX_WIDTH + 1e-6:
        sys.exit(f"{figure.name} is {width:.3f}\" wide, over {MAX_WIDTH}\"")
    return lo, hi


def r6(x):
    return float(f"{x:.6f}")


def write_glb(figure, path):
    lo, hi = normalise(figure)
    buffer = bytearray()
    accessors, views, materials, primitives = [], [], [], {"Body": [], "Accent": []}
    for material in sorted(figure.parts):
        part = figure.parts[material]
        pos, nor = [], []
        # The shapes above are wound clockwise seen from outside; glTF's front faces are counter-clockwise.
        for t in [(t[0], t[2], t[1]) for t in part.tris]:
            n = normal(*t)
            for p in t:
                pos.append(tuple(r6(x) for x in p))
                nor.append(tuple(r6(x) for x in n))
        index = len(materials)
        # The palette is written as sRGB (as seen on screen); glTF colour factors are linear.
        colour = tuple(r6(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in PALETTE[material])
        mat = {"name": material, "pbrMetallicRoughness": {"baseColorFactor": [*colour, 1.0], "metallicFactor": 0.0, "roughnessFactor": 1.0}}
        if material == "candle":
            mat["emissiveFactor"] = list(colour)
        materials.append(mat)
        attrs = {}
        for key, data in (("POSITION", pos), ("NORMAL", nor)):
            offset = len(buffer)
            for v in data:
                buffer += struct.pack("<3f", *v)
            views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(data) * 12, "target": 34962})
            acc = {"bufferView": len(views) - 1, "componentType": 5126, "count": len(data), "type": "VEC3"}
            if key == "POSITION":
                acc["min"] = [min(v[i] for v in data) for i in range(3)]
                acc["max"] = [max(v[i] for v in data) for i in range(3)]
            accessors.append(acc)
            attrs[key] = len(accessors) - 1
        primitives["Accent" if material == "accent" else "Body"].append({"attributes": attrs, "material": index, "mode": 4})
    meshes, nodes = [], []
    for name in ("Body", "Accent"):
        if primitives[name]:
            meshes.append({"name": name, "primitives": primitives[name]})
            nodes.append({"name": name, "mesh": len(meshes) - 1})
    root = {"name": "Figure", "children": list(range(1, len(nodes) + 1))}
    gltf = {
        "asset": {"version": "2.0", "generator": "Trench Sim tools/art/make_figures.py"},
        "scene": 0,
        "scenes": [{"name": figure.name, "nodes": [0]}],
        "nodes": [root, *nodes],
        "meshes": meshes,
        "materials": materials,
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(buffer)}],
    }
    text = json.dumps(gltf, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    buffer += b"\0" * (-len(buffer) % 4)
    body = struct.pack("<II", len(text), 0x4E4F534A) + text + struct.pack("<II", len(buffer), 0x004E4942) + buffer
    with open(path, "wb") as out:
        out.write(struct.pack("<III", 0x46546C67, 2, 12 + len(body)) + body)
    triangles = sum(len(p.tris) for p in figure.parts.values())
    return triangles, os.path.getsize(path), hi[0] - lo[0], hi[2] - lo[2]


def main():
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "assets", "units")
    for make in FIGURES:
        figure = make()
        triangles, size, wx, wz = write_glb(figure, os.path.join(out, f"{figure.name}.glb"))
        print(f"{figure.name}.glb: {triangles} triangles, {size} bytes, {wx:.2f} x {wz:.2f} x {HEIGHT}\"")


if __name__ == "__main__":
    main()
