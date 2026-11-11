#!/usr/bin/env python3
"""Graphics stage G3 (revised 2026-10-24): builds the four unit figures (client/assets/units/*.glb).

Each figure starts from the MakeHuman base body (CC0 1.0, github.com/makehumancommunity/makehuman at commit MAKEHUMAN
below), posed with MakeHuman's own skeleton and weights, reduced to about 1,400 triangles, and dressed with
equipment made for this project (greatcoat skirt, helmet, rifle, shield, banner...). One 128x128 PS1-style texture per
figure: part colours with grime noise and ambient occlusion baked in, cut to 15-bit colour, drawn with nearest
filtering. Each figure stands on y = 0, faces +z and is exactly 1.2" tall. The seat colour is never baked in: the
parts the client paints with it (sash, mantle, shield face, pennant) are one untextured node named "Accent".

Run: python3 -m pip install bpy==5.1.2 (Python 3.11 to 3.13), then python3 tools/art/make_figures.py. It fetches the
MakeHuman body, skeleton and shape files once (git sparse checkout into tools/art/.makehuman, not committed), then runs
Blender once per figure. The texture bake is not byte-for-byte repeatable, so a rerun gives the same shapes with
slightly different texture pixels.
"""
import os, subprocess, sys
FIGURES = ("rifleman", "skirmisher", "shield", "standard", "trench-pilgrim", "stigmatic-nun", "ecclesiastic-prisoner", "war-prophet", "castigator", "reliquary-walker", "sanctified-giant", "yeoman", "shock-trooper", "lieutenant", "blind-marksman", "trench-cleric", "combat-engineer", "mechanized-heavy-infantry", "combat-medic", "holy-warrior")   # "pilgrim" is built with --figure until a unit uses it
MAKEHUMAN = "a8bc2d54ff0ac92e78ff71431b1023eda42bf482"
HERE = os.path.dirname(os.path.abspath(__file__))
if len(sys.argv) < 4 or sys.argv[-4] != "--figure":
    mh = os.path.join(HERE, ".makehuman")
    if not os.path.isdir(os.path.join(mh, "makehuman", "data", "rigs")):
        subprocess.run(["git", "clone", "-q", "--filter=blob:none", "--no-checkout", "https://github.com/makehumancommunity/makehuman", mh], check=True)
        subprocess.run(["git", "-C", mh, "sparse-checkout", "set", "makehuman/data/3dobjs", "makehuman/data/rigs", "makehuman/data/targets/macrodetails"], check=True)
        subprocess.run(["git", "-C", mh, "checkout", "-q", MAKEHUMAN], check=True)
    out = os.path.abspath(os.path.join(HERE, "..", "..", "client", "assets", "units"))
    for name in FIGURES:
        subprocess.run([sys.executable, os.path.abspath(__file__), "--figure", name, out, mh], check=True)
    sys.exit(0)
# Builds one modelled figure (Body textured PSX-style + untextured Accent) in headless Blender.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Euler, Matrix
role, outdir, mhdir = sys.argv[-3], sys.argv[-2], sys.argv[-1]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import makehuman_body as human
bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene
scn.cycles.seed = 0

def lin(h): return tuple(((int(h[i:i+2],16)/255+0.055)/1.055)**2.4 for i in (0,2,4))
def mat(name, hexc, noise=0.25, scale=40.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]; rgb = lin(hexc)
    tex = nt.nodes.new("ShaderNodeTexNoise"); tex.inputs["Scale"].default_value = scale; tex.inputs["Detail"].default_value = 6
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*[c*(1-noise) for c in rgb], 1)
    ramp.color_ramp.elements[1].color = (*[min(1, c*(1+noise*0.6)) for c in rgb], 1)
    nt.links.new(tex.outputs["Fac"], ramp.inputs["Fac"]); nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    return m
def spattered(name, hexc, blood="5a0f0c", mud="4a4034", spots=0.34, scale=22.0):
    # cloth spattered with blood (noise below `spots`), muddier towards the bottom of each part (generated z)
    m = mat(name, hexc, 0.12); nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    base = b.inputs["Base Color"].links[0].from_socket
    n = nt.nodes.new("ShaderNodeTexNoise"); n.inputs["Scale"].default_value = scale; n.inputs["Detail"].default_value = 3
    r = nt.nodes.new("ShaderNodeValToRGB"); r.color_ramp.interpolation = 'CONSTANT'
    r.color_ramp.elements[0].color = (*lin(blood), 1); r.color_ramp.elements[1].position = spots; r.color_ramp.elements[1].color = (1, 1, 1, 1)
    nt.links.new(n.outputs["Fac"], r.inputs["Fac"])
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs["Factor"].default_value = 1
    nt.links.new(base, mul.inputs[6]); nt.links.new(r.outputs["Color"], mul.inputs[7])
    tc = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    mr = nt.nodes.new("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 0.0; mr.inputs["From Max"].default_value = 0.35
    mr.inputs["To Min"].default_value = 0.85; mr.inputs["To Max"].default_value = 0.0; nt.links.new(sep.outputs["Z"], mr.inputs["Value"])
    n2 = nt.nodes.new("ShaderNodeTexNoise"); n2.inputs["Scale"].default_value = 30
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.inputs[7].default_value = (*lin(mud), 1)
    prod = nt.nodes.new("ShaderNodeMath"); prod.operation = 'MULTIPLY'
    nt.links.new(mr.outputs["Result"], prod.inputs[0]); nt.links.new(n2.outputs["Fac"], prod.inputs[1])
    nt.links.new(prod.outputs["Value"], mix.inputs["Factor"]); nt.links.new(mul.outputs[2], mix.inputs[6])
    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    return m
M = {k: mat(k, *v) for k, v in {
 "coat": ("5a5040",), "trousers": ("4a4436",), "skin": ("d6ae8c", 0.04), "helmet": ("4c5048",),
 "leather": ("4a3020",), "wood": ("6a4a2c", 0.3, 8), "metal": ("3a3a3c",), "iron": ("55555a", 0.35, 20),
 "boots": ("2a2420",), "dark": ("141210", 0.05), "brass": ("8a6a2a", 0.1), "bone": ("c8bca0", 0.15),
 "cloth": ("3e3a34",), "hood": ("5e5646", 0.18), "glow": ("e0b050", 0.05), "accent": ("d0d0d0", 0.0),
 "robe": ("3e3428", 0.3), "rags": ("6a5c46", 0.3), "rope": ("7e6c48", 0.2, 60), "blood": ("4e1610", 0.2),
 "mannequin": ("cfc4b0", 0.03), "stone": ("7a7468", 0.3, 16)}.items()}   # the mannequin head: smooth pale plaster, no face (2026-11-03)
M.update({"habit": spattered("habit", "d8d0bc", spots=0.36, scale=7.0), "plate": mat("plate", "7a7c80", 0.3, 18),
          "gauntlet": spattered("gauntlet", "7a1a14", spots=0.5, scale=40),
          "blade": spattered("blade", "9a9ca0", spots=0.42, scale=9),
          "flesh": spattered("flesh", "9c7a6a", spots=0.36, scale=9),      # the Ecclesiastic Prisoner (2026-11-03)
          "rust": mat("rust", "6a4630", 0.35, 14), "bomb": mat("bomb", "4a5430", 0.2, 10),
          "charge": mat("charge", "8a1812", 0.15, 12),
          "rubber": mat("rubber", "2c2a27", 0.02, 6), "mail": mat("mail", "4c4e52", 0.5, 70), "gilt": mat("gilt", "9a7a32", 0.2, 20), "icon": mat("icon", "6a4a2a", 0.4, 14),
          "hull": spattered("hull", "6e5e4c", blood="5a0f0c", mud="4a3a2a", spots=0.4, scale=6.0),   # the Reliquary Walker (2026-11-03)
          "tunic": spattered("tunic", "6e6040", spots=0.22, scale=9), "breeches": mat("breeches", "625636", 0.3, 16),   # the Trench Pilgrim's WW1 kit (2026-11-03)
          "puttee": mat("puttee", "4a4630", 0.3, 30), "webbing": mat("webbing", "a49468", 0.25, 24),
          "maskcloth": mat("maskcloth", "5e5e48", 0.3, 30), "lens": mat("lens", "b8a878", 0.08, 8),   # the WWI gas mask (2026-11-03)
          "cloak": spattered("cloak", "46553a", spots=0.25, scale=8),   # the Blind Marksman's cloak (2026-11-03)
          "greatcoat": spattered("greatcoat", "55603f", spots=0.22, scale=9),   # the Lieutenant's greatcoat (2026-11-03)
          "assault": spattered("assault", "4c4f47", spots=0.25, scale=9),   # the Shock Trooper's dark grey kit (2026-11-03)
          "gore": spattered("gore", "5a2a1e", spots=0.55, scale=12), "pale": spattered("pale", "d8ccb8", spots=0.4, scale=10),
          "brute": spattered("brute", "c8a294", spots=0.45, scale=7), "skirt": spattered("skirt", "4e5642", spots=0.3, scale=8),
          "vestment": spattered("vestment", "bdae86", blood="5a0f0c", mud="3a3026", spots=0.34, scale=7), "olive": mat("olive", "6e7a48", 0.3, 18),   # the Trench Cleric (2026-11-04)
          "parchment": mat("parchment", "dccca2", 0.1, 20),
          "engarmour": mat("engarmour", "808c6c", 0.3, 16),
          "engplate": mat("engplate", "8e9184", 0.35, 18)})   # the Sanctified Giant (2026-11-03)   # the Castigator   # the War Prophet's anti-tank charge (2026-11-03)   # the Stigmatic Nun (2026-11-03)
# Faceless heads (visual pass Stage 1, 2026-10-27): every figure wears a plain hood of matte canvas over a smooth,
# featureless head (no eyes, nose or mouth); the figures are told apart by silhouette, kit and pose.
FACE_SCALE = 1.12   # the plain head fills the space of the MakeHuman face
FACE_BOX = None
NECK = None
FISTS = {}   # with mitts: each gloved fist's centre and its forearm's direction, so a weapon sits in the hand
BODY_BUDGET = None   # a figure of a larger class sets its body's triangle budget (docs/art-pipeline.md section 3)
TEX_SIZES = None   # and its texture sizes by module name; else 128 for the body and 64 for each module
PAINT = None   # a figure painted by hand (PSX style, the Combat Engineer 2026-11-10): paint(material, point, normal, ao, tx, ty) -> sRGB, per texel
HEAD_SEGS, HEAD_RINGS = 14, 11   # the low-poly head: about 280 triangles
def head_frame(eyes, headp, headtop):
    # the head's own frame (across, up, forward), used to size and place the plain head
    mid = (eyes[0] + eyes[1]) / 2
    ax = (eyes[0] - eyes[1]); sep = ax.length or 0.062; ax.normalize()
    if ax.x < 0: ax = -ax
    up = headtop - headp; up = (up - ax * up.dot(ax)).normalized()
    fwd = ax.cross(up)
    if fwd.y > 0: fwd = -fwd
    W = sep * 32 / 11.5 * FACE_SCALE; Hh = W * 1.35   # MakeHuman's face is longer than wide
    global FACE_BOX; FACE_BOX = dict(mid=mid, ax=ax, up=up, fwd=fwd, u0=-16.25 / 32 * W, z0=-(1 - 14 / 32) * Hh, w=W, h=Hh)

# Modules (docs/art-pipeline.md): every part is added to the module being built (module("Helmet") and so on); each module
# is exported as its own mesh node with its own texture, so the game can show, hide or swap it. Body and Accent always
# exist; the older figures put everything in Body.
MODULES = {"Body": [], "Accent": []}
body, accent = MODULES["Body"], MODULES["Accent"]
CUR = "Body"
def module(name):
    global CUR; CUR = name; MODULES.setdefault(name, [])
def add(o, m):
    o.data.materials.append(M[m]); MODULES["Accent" if m == "accent" else CUR].append(o); return o
def skin(verts, edges, radii, m, subdiv=1):
    me = bpy.data.meshes.new("s"); me.from_pydata(verts, edges, [])
    o = bpy.data.objects.new("s", me); scn.collection.objects.link(o)
    o.modifiers.new("skin", "SKIN").use_smooth_shade = True
    for i, r in enumerate(radii): o.data.skin_vertices[0].data[i].radius = r if isinstance(r, tuple) else (r, r)
    o.data.skin_vertices[0].data[0].use_root = True
    if subdiv: o.modifiers.new("sub", "SUBSURF").levels = subdiv
    return add(o, m)
def chain(pts, radii, m, subdiv=1):
    return skin([tuple(p) for p in pts], [(i, i+1) for i in range(len(pts)-1)], radii, m, subdiv)
def sphere(loc, r, m, scale=(1,1,1), seg=12, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = bpy.context.object; o.scale = scale; bpy.ops.object.shade_smooth(); return add(o, m)
def bar(a, b, w, m, sides=6):
    a, b = Vector(a), Vector(b); v = b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=w, depth=v.length, location=(a+b)/2)
    o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0,0,1)).rotation_difference(v)
    return add(o, m)
def box(loc, size, m, rot=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot); o = bpy.context.object; o.scale = size; return add(o, m)
def drape(o, pinned, colliders, frames=60, tris=None, thick=0.008, bend=0.3):
    # Cloth by simulation (owner, 2026-11-03: hand-shaped cloth looked wrong): the cloth hangs from its pinned vertices
    # under gravity and settles on the colliders (the posed body), then is frozen, reduced to `tris` and given thickness.
    # The cloth must start clear of the colliders.
    g = o.vertex_groups.new(name="pin"); g.add(pinned, 1.0, 'REPLACE')
    added = []
    for c in colliders:
        added.append((c, c.modifiers.new("col", "COLLISION"))); c.collision.thickness_outer = 0.014; c.collision.cloth_friction = 20
    cl = o.modifiers.new("cloth", "CLOTH"); st = cl.settings
    st.vertex_group_mass = "pin"; st.quality = 8; st.mass = 0.15; st.air_damping = 3.0
    st.tension_stiffness = st.compression_stiffness = 12; st.bending_stiffness = bend
    cl.collision_settings.use_self_collision = True; cl.collision_settings.self_distance_min = 0.006   # no folds through itself
    cl.collision_settings.distance_min = 0.012; cl.point_cache.frame_start = 1; cl.point_cache.frame_end = frames
    scn.frame_start, scn.frame_end = 1, frames
    for f in range(1, frames + 1): scn.frame_set(f)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=cl.name)
    for c, m in added: c.modifiers.remove(m)
    if (g := o.vertex_groups.get("pin")) is not None: o.vertex_groups.remove(g)
    import bmesh as _bm   # iron out crumples, but only inside: smoothing an open edge would pull a hem or panel in
    bm = _bm.new(); bm.from_mesh(o.data); inner = [v.index for v in bm.verts if not v.is_boundary]; bm.free()
    gi = o.vertex_groups.new(name="inner"); gi.add(inner, 1.0, 'REPLACE')
    sm = o.modifiers.new("sm", "SMOOTH"); sm.factor = 0.5; sm.iterations = 3; sm.vertex_group = "inner"; bpy.ops.object.modifier_apply(modifier="sm")
    o.vertex_groups.remove(o.vertex_groups["inner"])
    scn.frame_set(1)
    if tris:
        n = sum(len(f.vertices) - 2 for f in o.data.polygons)
        if n > tris:
            d = o.modifiers.new("d", "DECIMATE"); d.ratio = tris / n; bpy.ops.object.modifier_apply(modifier="d")
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')   # the reduction can flip a face
    bpy.ops.object.shade_smooth()
    sol = o.modifiers.new("sol", "SOLIDIFY"); sol.thickness = thick; sol.use_rim = False   # no rim: half the cost, a hairline edge at this size
    sol.offset = 1.0   # thickness grows outward, away from what the cloth lies on (no clipping)
    return o
def cloth_cone(cx, cy, z_top, z_bot, r_top, r_bot, m, sy=0.8, keep=None, ragged=0.0, rnd=None, tris=200, thick=0.01,
               extra=(), stand=None, segs=24, rows=8, frames=50, mono=True):
    # A tube of cloth (robe, habit, hood, mantle, cape) round (cx, cy) from z_top down to z_bot, laid out clear of the
    # body (each ring ray-cast onto it from outside, never narrower than the ring above, so it hangs over the legs),
    # then hung from its top ring by drape(). keep(angle, z) cuts openings: angle 0 is the front (-y), +pi/2 is +x.
    # stand=(centre, radius): a stand-in head to drape over where the figure has none; removed afterwards.
    hb = bpy.data.objects["human"]
    verts, faces, prev = [], [], [0.0] * segs
    for j in range(rows + 1):
        t = j / rows; z = z_top + (z_bot - z_top) * t; r0 = r_top + (r_bot - r_top) * t
        for i in range(segs):
            a = 2 * math.pi * i / segs; d = Vector((math.sin(a), -sy * math.cos(a), 0)); u = d.normalized(); ax = Vector((cx, cy, z))
            r = r0 * d.length
            ok, loc, *_ = hb.ray_cast(ax + u * 1.0, -u)
            if ok and (loc - ax).to_2d().length < r * 1.6:   # not an arm held out
                r = max(r, (loc - ax).to_2d().length + 0.012)
            if mono: r = max(r, prev[i]); prev[i] = r   # mono=False: a cape that may follow the back in
            p_ = ax + u * r
            if j == rows and ragged: p_.z += rnd.uniform(0.0, ragged)
            verts.append(tuple(p_))
    for j in range(rows):
        for i in range(segs):
            a = 2 * math.pi * (i + 0.5) / segs; z = z_top + (z_bot - z_top) * (j + 0.5) / rows
            if keep is not None and not keep(a, z): continue
            i2 = (i + 1) % segs; faces.append((j * segs + i, (j + 1) * segs + i, (j + 1) * segs + i2, j * segs + i2))   # facing out: it collides right as a collider
    used = sorted({v for f in faces for v in f}); at = {v: k for k, v in enumerate(used)}
    me = bpy.data.meshes.new("cloth"); me.from_pydata([verts[v] for v in used], [], [tuple(at[v] for v in f) for f in faces])
    o = bpy.data.objects.new("cloth", me); scn.collection.objects.link(o)
    col = [hb, *extra]
    if stand is not None:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=stand[1], location=tuple(stand[0])); st_ = bpy.context.object; col.append(st_)
    drape(o, [at[i] for i in range(segs) if i in at], col, frames=frames, tris=tris, thick=thick)
    if stand is not None: bpy.data.objects.remove(st_)
    return add(o, m)
def cloth_panel(top, length, m, out=Vector((0, -1, 0)), extra=(), cols=6, rows=10, ragged=0.0, rnd=None, tris=40, thick=0.008, frames=50, bend=0.3):
    # A hanging panel (tabard, flap) from its top edge (a list of points), laid out in front of the body and of `extra`
    # (along `out`), then hung from that edge by drape().
    col = [bpy.data.objects["human"], *extra]
    tl, tr = Vector(top[0]), Vector(top[-1]); verts = []
    for j in range(rows + 1):
        for i in range(cols + 1):
            p_ = tl + (tr - tl) * (i / cols) - Vector((0, 0, length * j / rows))
            if j > 0:
                for c_ in col:
                    ok, loc, *_ = c_.ray_cast(p_ + out * 0.6, -out)
                    if ok and (loc - p_).dot(out) > -0.012: p_ = p_ + out * ((loc - p_).dot(out) + 0.014)
                if j == rows and ragged: p_.z += rnd.uniform(0.0, ragged)
            verts.append(tuple(p_))
    faces = [(j * (cols + 1) + i, j * (cols + 1) + i + 1, (j + 1) * (cols + 1) + i + 1, (j + 1) * (cols + 1) + i) for j in range(rows) for i in range(cols)]
    me = bpy.data.meshes.new("panel"); me.from_pydata(verts, [], faces)
    o = bpy.data.objects.new("panel", me); scn.collection.objects.link(o)
    drape(o, list(range(cols + 1)), col, frames=frames, tris=tris, thick=thick, bend=bend)
    for v in o.data.vertices: v.co += out * 0.016   # clear of the thickness the cloth under it grew outward
    return add(o, m)
FRONT = lambda w: (lambda a, z: abs(math.remainder(a, 2 * math.pi)) > math.radians(w))   # keep(): open at the front


DATA = os.path.join(mhdir, "makehuman", "data")
FLOOR = 0.8168
def P(x, y, h): return Vector((x, y, h - FLOOR))      # a point h metres above the floor
def lift(v): return Vector((v.x, v.y, v.z + FLOOR))

def human_body(pose, coat=True, dress=None, head="hood", mitts=None, shape="male-young-averagemuscle-averageweight", tris=800):
    body, rig = human.load(DATA, ((f"macrodetails/universal-{shape}.target", 1.0),))
    for s in ("L", "R"):      # legs together, straight
        x = 0.1 if s == "L" else -0.1
        for b in ("upperleg01", "upperleg02"): human.aim(rig, f"{b}.{s}", P(x, 0.0, 0.47))
        for b in ("lowerleg01", "lowerleg02"): human.aim(rig, f"{b}.{s}", P(x, 0.02, 0.08))
        human.aim(rig, f"foot.{s}", P(x*1.05, -0.14, -0.01))   # the sole flat on the ground, heel and toes alike
    pose(rig)
    hands = {s: lift(human.world(rig, f"wrist.{s}", "head")) for s in ("L", "R")}
    elbows = {s: lift(human.world(rig, f"lowerarm01.{s}", "head")) for s in ("L", "R")}
    eyes = [lift(human.world(rig, f"eye.{s}", "head")) for s in ("L", "R")]
    headp = lift(human.world(rig, "head", "head")); headtop = lift(human.world(rig, "head", "tail"))
    global NECK; NECK = lift(human.world(rig, "neck01", "head"))   # the neck's root, for headgear that sits on it
    global JOINTS; JOINTS = {f"{b}.{s}": lift(human.world(rig, f"{b}.{s}", "head")) for s in ("L", "R")
                             for b in ("upperarm01", "lowerarm01", "wrist", "upperleg01", "lowerleg01", "foot")}   # landmarks for painted figures
    faces = human.bake_pose(body, rig)
    body.location.z = FLOOR; bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    head_frame(eyes, headp, headtop)
    order = {"head": "hood", "hand": "skin", "foot": "boots", "shin": "cloth", "thigh": "trousers", "torso": "coat", **(dress or {})}
    slots = list(dict.fromkeys(order.values()))
    for m in slots: body.data.materials.append(M[m])
    for f, p in zip(body.data.polygons, faces): f.material_index = slots.index(order[p])
    if coat:   # the uniform sits a little proud of the body
        out = {i for f, p in zip(body.data.polygons, faces) if p in ("torso", "thigh") for i in f.vertices}
        for v in body.data.vertices:
            if v.index in out: v.co += v.normal * 0.012
    # The sculpted MakeHuman head (sockets, lips, ears) turns to jagged shards at this budget, so it is swapped for a
    # clean low-poly head of the same size, like a PS1 character's: a smooth, featureless skull with a tapered jaw and
    # a neck, both in the hood's canvas. Sized from the MakeHuman head in the head's own frame.
    fb = FACE_BOX; mid = fb["mid"]
    hv = [body.data.vertices[i].co.copy() for i in {i for f, p in zip(body.data.polygons, faces) if p == "head" for i in f.vertices}]
    loc = [((v - mid).dot(fb["ax"]), (v - mid).dot(fb["up"]), (v - mid).dot(fb["fwd"])) for v in hv]
    eye_band = [q for q in loc if abs(q[1]) < 0.02]
    top = max(q[1] for q in loc)
    front = max(q[2] for q in eye_band if abs(q[0]) < 0.015); back = min(q[2] for q in eye_band)
    half_w = max(abs(q[0]) for q in eye_band)
    chin = min(q[1] for q in loc if q[2] > front - 0.035 and abs(q[0]) < 0.02)
    neck_lo = min(q[1] for q in loc)
    import bmesh as _bm
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
    bc = (top + chin) / 2; rh = (top - chin) / 2; cc = (front + back) / 2; rd = (front - back) / 2; rw = half_w * 0.95
    def inside_head(f):   # any face of the eye sockets weighted to other bones would poke through the plain head
        q = sum((body.data.vertices[i].co for i in f.vertices), Vector()) / len(f.vertices) - mid
        a_, b_, c_ = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
        # the head's column above the chin too: jaw and neck faces hidden inside the plain head still darken its baked
        # occlusion
        return ((a_ / (rw * 1.3)) ** 2 + ((b_ - bc) / (rh * 1.15)) ** 2 + ((c_ - cc) / (rd * (1.5 if c_ > cc else 1.15))) ** 2 <= 1
                or (b_ > chin and abs(a_) < rw * 1.3 and back - 0.02 < c_ < front + 0.05))
    # with mitts, the fingers (which decimate to spikes) go too, for gloved hands made below
    for f, p in zip(body.data.polygons, faces): f.select = p == "head" or inside_head(f) or (mitts is not None and p == "hand")
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.delete(type='FACE'); bpy.ops.object.mode_set(mode='OBJECT')
    if mitts is not None:
        # a gloved hand: a flattened fist along the forearm with a thumb, closed round whatever it holds
        for s in ("L", "R"):
            w = hands[s]; d = (w - elbows[s]).normalized(); q = Vector((0, 0, 1)).rotation_difference(d)
            FISTS[s] = (w + d * 0.045, d)
            g = sphere(tuple(w + d * 0.045), 1, mitts, seg=8, rings=6)
            g.scale = (0.036, 0.03, 0.055); g.rotation_mode = 'QUATERNION'; g.rotation_quaternion = q
            side = d.cross(Vector((0, 0, 1))).normalized() * (1 if s == "L" else -1)
            t = sphere(tuple(w + d * 0.03 + side * 0.03 - Vector((0, 0.02, 0))), 1, mitts, seg=6, rings=4)
            t.scale = (0.014, 0.014, 0.03); t.rotation_mode = 'QUATERNION'; t.rotation_quaternion = q
            for o in (g, t): o.vertex_groups.new(name="head").add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    def frame(a_, b_, c_): return mid + fb["ax"] * a_ + fb["up"] * b_ + fb["fwd"] * c_
    if head is not None:   # head=None: headgear hides the whole head (a capirote), so none is built
        bpy.ops.mesh.primitive_uv_sphere_add(segments=HEAD_SEGS, ring_count=HEAD_RINGS, radius=1)
        h = bpy.context.object
        for v in h.data.vertices:
            x, y, z = v.co.x, -v.co.y, v.co.z                         # y forward
            w = 1 - 0.3 * max(0.0, -z) ** 1.4                         # the jaw narrows to the chin
            d = 1 - 0.18 * max(0.0, -z) ** 1.2 * max(0.0, -y)         # and the back of the jaw tucks in
            yy = abs(y) ** 0.8 * (1 if y >= 0 else -1) if y > 0 else y   # a fuller, flatter face at the front
            a_, c_ = x * rw * w, cc + yy * rd * d
            v.co = frame(a_, bc + z * rh, c_)
        bpy.ops.object.shade_smooth(); add(h, head)
        # the neck: from under the jaw down into the collar
        nb = chin + 0.15 * rh
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=1, depth=1, end_fill_type='NOTHING')
        n = bpy.context.object
        for v in n.data.vertices:
            zz = nb if v.co.z > 0 else neck_lo
            v.co = frame(v.co.x * rw * 0.46, zz, cc - rd * 0.05 + (-v.co.y) * rd * 0.5)
        bpy.ops.object.shade_smooth(); add(n, head)
        for o in (h, n): o.vertex_groups.new(name="head").add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    d = body.modifiers.new("d", "DECIMATE"); d.ratio = tris / sum(len(p.vertices)-2 for p in body.data.polygons)
    bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier="d")
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.shade_smooth()
    globals()["body"].append(body)
    return hands, headp, headtop

# ---- the four generic figures (rifleman, skirmisher, shield, standard), painted PSX look (2026-11-10) ----
# Generic stand-ins for several units each (catalog.json), so neutral: WWI trench kit in the Principality's grimdark
# palette, no faction emblems. Each a smooth low-poly body in the WWI service dress (ww1_paint), toeless boot hulls,
# a blank mannequin face or an enclosed helm; solid only for the silhouette.
WW1_DRESS = {"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}
def generic_palette(**kw):   # WW1 plus the common kit, a khaki greatcoat, a steel helmet and the plain face
    return psx_palette(**{**KIT, **WW1, **dict(coat="5a5038", coatd="3a3324", coatl="746a4c", helmet="4c5048", helmetd="2e312c",
                                               helmetl="6c7268", mannequin="cfc4b0", mannequind="a49a88"), **kw})
def psx_greatcoat(bot=0.5, tris=80):
    # a knee-length greatcoat skirt, closed, draped in its own node (Coat, 128 px); the tunic skirt under it (hidden) is
    # removed. Returns paint(C, base, p, n, ao) for it: belted top, a buttoned overlap, long folds, blood and a muddy hem.
    under = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    prev = CUR; module("Coat")
    coat = cloth_cone(0, 0.005, 0.99, bot, 0.17, 0.23, "greatcoat", sy=0.82, tris=tris, thick=0.012, extra=under, rows=6)
    for o in under: MODULES["Body"].remove(o); bpy.data.objects.remove(o)
    module(prev)
    # the hem's height near p: the nearest bottom-edge vertices across the ground (hem_line's angle bins fail where the
    # closed skirt sags in between the legs)
    import bmesh as _bm
    from mathutils.kdtree import KDTree
    bm = _bm.new(); bm.from_mesh(coat.data); lows = [v.co.copy() for v in bm.verts if v.is_boundary and v.co.z < 0.9]; bm.free()
    kd = KDTree(len(lows))
    for i, v in enumerate(lows): kd.insert(Vector((v.x, v.y, 0)), i)
    kd.balance()
    def hem(p): return sum(lows[i].z for _, i, _ in kd.find_n(Vector((p.x, p.y, 0)), 3)) / 3
    def paint(C, base, p, n, ao):
        if base != "greatcoat": return None
        x, y, z = p
        if z > 0.972: return C["ink"] if z < 0.978 else psx_lit(C["webbing"], n, ao, p=p)   # under the belt
        if y < 0:
            b = psx_dot(p, [Vector((0.05, y, bz)) for bz in (0.9, 0.78, 0.66)], C, 0.0075)
            if b is not None: return b
            if abs(x - 0.025) < 0.0035 and z > hem(p) + 0.01: return C["ink"]   # the overlap's edge
        if y > 0 and abs(x) < 0.004 and z < hem(p) + 0.2: return C["ink"]   # the back vent
        from mathutils import noise as _nz
        h = z - hem(p)
        if h < 0.007: return C["ink"]
        if h < 0.028 + 0.012 * _nz.noise(p * 20): return psx_lit(C["coatd"].lerp(C["mud"], 0.5), n, ao, p=p)   # the muddy hem
        b = psx_blood(C, p, 0.08 * max(0.0, 1 - h / 0.3))
        if b is not None: return b
        f = (math.degrees(math.atan2(x, -y)) / 40.0 + _nz.noise(Vector((x * 4, y * 4, z * 2))) * 0.25) % 1.0   # long folds
        c = C["coatd"] if f < 0.08 else (C["coatl"] if f < 0.13 and n.dot(PSX_LIGHT) > 0.2 else C["coat"])
        return psx_lit(c, n, max(ao, 0.6), grime=0.2 * max(0.0, 1 - h / 0.12), p=p, mud=C["mud"])   # no black pit between the legs
    return paint
def psx_brodie(hp, top):
    # the steel helmet: a shallow dome on a wide brim, in the current module; returns paint(C, base, p, n, ao) for it
    hc = Vector((hp.x, hp.y - 0.008, top.z - 0.075))
    sphere(tuple(hc + Vector((0, 0, 0.03))), 0.125, "helmet", (1, 1.08, 0.62), seg=10, rings=6)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.168, radius2=0.12, depth=0.022, location=tuple(hc + Vector((0, 0, 0.03))))
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "helmet")
    def paint(C, base, p, n, ao):
        if base != "helmet": return None
        q = p - hc; r = Vector((q.x, q.y / 1.08)).length
        if n.z < -0.5: return C["dark"]                                                        # under the brim
        if r > 0.158: return C["ink"]                                                          # the rolled rim
        if q.z < 0.046 and r > 0.118: return C["ink"] if r < 0.124 else psx_lit(C["helmetl"] if r > 0.15 else C["helmet"], n, ao, metal=True, p=p)   # the brim
        b = psx_dot(p, [hc + Vector((0, 0, 0.03 + 0.125 * 0.62))], C, 0.009, m="brass")       # the crown rivet
        if b is not None: return b
        s = n.dot(PSX_LIGHT)
        c = C["helmetl"] if s > 0.62 else (C["helmet"] if s > 0.0 else C["helmetd"])
        from mathutils import noise as _nz
        if _nz.noise(p * 26) > 0.55: c = C["rust"]                                             # chips down to rust
        return psx_lit(c, n, ao, p=p)
    return paint
def plain_face(C, base, p, n, ao):   # the blank mannequin face: smooth pale plaster
    if base != "mannequin": return None
    return psx_lit(C["mannequin"] if n.dot(PSX_LIGHT) > 0.0 else C["mannequind"], n, ao, p=p)
def chain_paint(*fs):   # the first figure part painter that answers
    def paint(base, p, n, ao):
        for f in fs:
            r = f(base, p, n, ao)
            if r is not None: return r
        return None
    return paint
def ww1_rifle(hr, hl, back=0.25, ahead=0.42, bayonet=0.2):
    # a bolt-action rifle through both fists, in the current module: stock, fore-end, barrel, bolt, magazine, bayonet
    d = (hl - hr).normalized(); butt, muz = hr - d * back, hl + d * ahead
    side = d.cross(Vector((0, 0, 1))).normalized()
    bar(butt, hr + d * 0.2, 0.028, "wood", sides=6); bar(hr + d * 0.2, muz - d * 0.1, 0.02, "wood", sides=6)
    bar(hr + d * 0.06, muz, 0.01, "metal", sides=4)
    bar(hr + d * 0.1, hr + d * 0.1 + side * 0.05 + Vector((0, 0, 0.01)), 0.008, "metal", sides=3)   # bolt handle
    bar(hr, hr + d * 0.12 - Vector((0, 0, 0.03)), 0.016, "metal", sides=4)                           # magazine and trigger guard
    if bayonet: bar(muz, muz + d * bayonet, 0.006, "iron", sides=3)
def arm_mark(s, a0, a1, t):
    # a landmark on the outside of a limb (side s, from joint a0 towards a1, t along): (surface point, out, along); out
    # points away from the torso's axis, so a sleeve patch is painted on the outer side whatever the pose
    human_ = bpy.data.objects["human"]
    j0 = JOINTS[f"{a0}.{s}"]; j1 = FISTS[s][0] - FISTS[s][1] * 0.045 if a1 == "wrist" and s in FISTS else JOINTS[f"{a1}.{s}"]
    pt = j0 + (j1 - j0) * t; al = (j1 - j0).normalized()
    out = Vector((pt.x, pt.y + 0.02, 0)); out = (out - al * out.dot(al)).normalized()
    ok, at, *_ = human_.ray_cast(pt + out * 0.5, -out)
    return (at if ok else Vector((9, 9, 9))), out, al
def generic_detail(C, coat=None, patch=None, stripes=None, hc=None):
    # Detail pass (2026-11-10) for the generic figures in greatcoats (Rifleman, Shield-bearer, Standard-bearer). Returns
    # paint(base, p, n, ao) (None: not here): on the coat skirt (coat: its object) a crisp front edge with a worn light
    # lip, hip pocket flaps with a button and a stitched edge, a buttoned half-belt behind, a darned patch, mud thrown up
    # the skirt; on the tunic shoulder straps with a brass button, a sleeve patch (patch=(side, shape, colour key), shape
    # bar/disc/diamond/triangle) and wound stripes (stripes=side) on the forearm; mud blots up the legs; on the steel
    # helmet (hc: its centre, as psx_brodie) chin-strap lugs, a dent and a worn rim.
    from mathutils import noise as _nz
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    seam = C["coatd"].lerp(C["ink"], 0.45)
    hem = None
    if coat is not None:   # the hem's height near p, as psx_greatcoat: the nearest bottom-edge vertices across the ground
        import bmesh as _bm
        from mathutils.kdtree import KDTree
        bm = _bm.new(); bm.from_mesh(coat.data); lows = [v.co.copy() for v in bm.verts if v.is_boundary and v.co.z < 0.9]; bm.free()
        kd = KDTree(len(lows))
        for i, v in enumerate(lows): kd.insert(Vector((v.x, v.y, 0)), i)
        kd.balance()
        def hem(p): return sum(lows[i].z for _, i, _ in kd.find_n(Vector((p.x, p.y, 0)), 3)) / 3
    PATCH = arm_mark(patch[0], "upperarm01", "lowerarm01", 0.3) if patch else None
    STRIPE = arm_mark(stripes, "lowerarm01", "wrist", 0.4) if stripes else None
    def on_mark(p, n, mk):   # (across, along) on a limb landmark's outer face, or None
        at, out, al = mk
        if n.dot(out) < 0.25 or (p - at).length > 0.06: return None
        q = p - at; return q.dot(al.cross(out)), q.dot(al)
    def paint(base, p, n, ao):
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        if base == "greatcoat" and hem is not None:
            h = z - hem(p)
            if h < 0.04 or z > 0.972: return None   # the hem and the belt: psx_greatcoat's
            if y < 0 and 0.019 < x < 0.037 and z < 0.965:   # the front edge: the overlap's ink edge and its worn lip
                return C["ink"] if x < 0.026 else lit(C["coatl"], n, ao, p=p)
            for sx in (-1, 1):   # the hip pocket flaps, a button on each, a shadow under the flap
                u, v = x * sx - 0.125, z - 0.855
                if y < 0.0 and abs(u) < 0.046 and -0.026 < v < 0.02:
                    if v < -0.018: return lit(C["coatd"], n, ao, p=p) if abs(u) < 0.04 else None
                    if abs(u) > 0.039 or abs(v) > 0.013: return C["ink"]
                    b = psx_dot(p, [Vector((sx * 0.125, y, 0.85))], C, 0.0065)
                    if b is not None: return b
                    return lit(C["coatl"] if s_ > -0.1 else C["coat"], n, ao, p=p)
            if y > 0.02 and abs(x) < 0.115 and abs(z - 0.928) < 0.02:   # the half-belt across the back, buttoned at each end
                if abs(z - 0.928) > 0.014 or abs(x) > 0.108: return C["ink"]
                b = psx_dot(p, [Vector((sx * 0.088, y, 0.928)) for sx in (-1, 1)], C, 0.0075)
                if b is not None: return b
                return lit(C["coatl"] if z > 0.93 and s_ > -0.2 else C["coat"], n, ao, p=p)
            if y > 0.02 and abs(x - 0.085) < 0.034 and abs(z - 0.72) < 0.03:   # a darned patch, its stitches round it
                if abs(x - 0.085) > 0.028 or abs(z - 0.72) > 0.024: return seam
                return lit(C["clothd"].lerp(C["coat"], 0.5), n, ao, p=p)
            if h < 0.16 and _nz.noise(p * Vector((24, 24, 12)) + Vector((7, 3, 1))) > 0.38 + h * 2.2:   # mud thrown up the skirt, in blots
                return lit(C["mud"] if _nz.noise(p * 40) > -0.2 else C["coatd"].lerp(C["mud"], 0.6), n, ao, p=p)
            return None
        if base == "helmet" and hc is not None:
            q = p - hc; r = math.hypot(q.x, q.y / 1.08)
            if n.z < -0.5: return None
            b = psx_dot(p, [hc + Vector((sx * 0.118, 0, 0.056)) for sx in (-1, 1)], C, 0.0085)   # the chin-strap lugs
            if b is not None: return b
            if q.z < 0.046 and 0.15 < r < 0.158: return C["ironl"] if s_ > 0.0 else C["helmet"]   # the worn rim
            if q.z > 0.046:
                dd = (q - Vector((0.045, -0.07, 0.085))).length   # a dent on the crown, light on its lower rim, dark in its hollow
                if dd < 0.024: return lit(C["helmetd"] if q.z > 0.083 else C["helmetl"], n, ao, p=p)
                if 0.046 < q.z < 0.056 and r > 0.11: return C["helmetd"]   # the crown's foot, in the brim's shadow
            return None
        if base == "tunic":
            if PATCH is not None:
                m = on_mark(p, n, PATCH)
                if m is not None:
                    u, v = m; shp, ck = patch[1], patch[2]
                    f = {"bar": max(abs(u) / 0.024, abs(v) / 0.014), "disc": math.hypot(u, v) / 0.019,
                         "diamond": (abs(u) + abs(v)) / 0.024, "triangle": max(abs(v) / 0.016, (abs(u) * 1.6 + v) / 0.026)}[shp]
                    if f < 1.0: return C["ink"] if f > 0.78 else lit(C[ck], n, ao, p=p)
            if STRIPE is not None:
                m = on_mark(p, n, STRIPE)
                if m is not None and abs(m[1]) < 0.022 and (abs(m[0] - 0.009) < 0.0055 or abs(m[0] + 0.009) < 0.0055):
                    return C["brassl"] if s_ > 0.2 else C["brass"]
            for sx in (1, -1):   # shoulder straps from the collar out to the shoulder, a brass button at the collar end
                sh = JOINTS["upperarm01.L" if sx > 0 else "upperarm01.R"]
                if n.z > 0.4 and z > sh.z - 0.03 and abs(x) > 0.05:
                    dd, tt = segd(Vector((x, y, 0)), Vector((sx * 0.06, sh.y, 0)), Vector((sh.x, sh.y, 0)))
                    if dd < 0.019:
                        if dd > 0.014 or tt > 0.97: return C["ink"]
                        if tt < 0.24 and dd < 0.01: return C["brassl"] if s_ > 0.6 else C["brass"]
                        return lit(C["tunicl"] if s_ > 0.5 else C["tunic"], n, ao, p=p)
        if base in ("breeches", "puttee") and z < 0.62 and z > 0.16:   # mud splashed up the legs, in blots, thicker low and on the knees
            k_ = (0.62 - z) / 0.45 + (0.25 if n.y < -0.4 else 0.0) - (0.35 if z < 0.43 else 0.0)   # sparing on the puttees' turns
            if _nz.noise(p * Vector((18, 18, 10)) + Vector((3, 1, 7))) > 0.52 - k_ * 0.3:
                return lit(C["mud"] if _nz.noise(p * 40) > -0.3 else C["breechesd"].lerp(C["mud"], 0.5), n, ao, p=p)
        return None
    return paint
def rifleman():
    # Rifleman (the generic line rifle: Sharpshooter, Marksman): standing at port arms, the rifle across the body with
    # its bayonet fixed, a steel helmet, a knee-length greatcoat, a pack with a blanket roll; a sash across the chest
    # and over the shoulder in the seat colour (Accent). Painted PSX look (2026-11-10): ww1_paint for the tunic,
    # webbing, breeches, puttees, boots and gloves; the greatcoat skirt draped (Coat). Solid only for the silhouette:
    # Helmet (the steel helmet), Back (pack), Weapon (rifle), Accent (sash).
    # Detail pass (2026-11-10): the sash (it lay inside the body) is now a flat band laid round the torso, over the left
    # shoulder; a water bottle, bayonet scabbard and haversack hang outside the coat (Hip); a mess tin and an entrenching
    # tool on the pack (Back); a sling under the rifle, its butt plate, bands and bolt painted; generic_detail's coat
    # pockets, half-belt, patch and mud, shoulder straps, a sleeve patch, wound stripes and the helmet's lugs and dent.
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 800; TEX_SIZES = {"Body": 256, "Coat": 256, "Helmet": 128, "Back": 128, "Weapon": 128, "Hip": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    def pose(r):
        human.reach(r, "R", P(-0.24, 0.02, 1.08), P(-0.07, -0.22, 1.0))
        human.reach(r, "L", P(0.26, -0.1, 1.12), P(0.1, -0.28, 1.27))
    hands, hp, top = human_body(pose, dress=WW1_DRESS, head="mannequin", mitts="leather", shape="male-young-averagemuscle-minweight")
    surf = ww1_uniform(bands=False); toeless_feet()
    coat = psx_greatcoat(); coat_o = MODULES["Coat"][0]
    hb = bpy.data.objects["human"]
    def csurf(o_, d_, out=0.0):   # the outermost point of the body or the coat seen from o_ along d_, out from it
        best = None
        for ob in (hb, coat_o):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.8) - d_ * out
    module("Helmet"); helm = psx_brodie(hp, top); hc = Vector((hp.x, hp.y - 0.008, top.z - 0.075))
    module("Back")
    box((0, 0.075, 1.18), (0.2, 0.09, 0.22), "cloth")                                    # pack, blanket roll on top
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.04, depth=0.26, location=(0, 0.075, 1.32), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "rags")
    TIN = Vector((0.0, 0.128, 1.19)); box(tuple(TIN), (0.1, 0.02, 0.075), "metal")       # a mess tin strapped to the pack's back
    ET = Vector((-0.112, 0.075, 1.05))                                                     # an entrenching tool down the pack's side
    box(tuple(ET), (0.014, 0.07, 0.08), "iron"); bar(ET + Vector((0, 0, 0.04)), ET + Vector((0, 0, 0.27)), 0.011, "wood", sides=4)
    module("Weapon")
    hr, hl = FISTS["R"][0], FISTS["L"][0]; ww1_rifle(hr, hl)
    d = (hl - hr).normalized(); butt, muz = hr - d * 0.25, hl + d * 0.42
    side = d.cross(Vector((0, 0, 1))).normalized(); dn = d.cross(side).normalized()
    if dn.y > 0: dn = -dn                                                                  # the rifle's underside, away from the chest
    sl0, sl1 = butt + d * 0.06 + dn * 0.03, muz - d * 0.12 + dn * 0.022                   # the sling, slack under the rifle
    slm = (sl0 + sl1) / 2 + dn * 0.05
    bar(sl0, slm, 0.006, "leather", sides=3); bar(slm, sl1, 0.006, "leather", sides=3)
    module("Hip")
    CANT = csurf(Vector((1.0, 0.0, 0.86)), Vector((-1, 0, 0))) + Vector((0.024, 0, 0))   # an enamel water bottle in a webbing carrier, left hip
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.05, depth=0.036, location=tuple(CANT), rotation=(0, math.pi / 2, 0))
    o = bpy.context.object; o.scale = (1, 0.82, 1); add(o, "cloth")
    bar(CANT + Vector((0, 0, 0.036)), CANT + Vector((0, 0, 0.058)), 0.01, "brass", sides=5)
    SC0 = csurf(Vector((1.0, 0.075, 0.96)), Vector((-1, 0, 0)), 0.014); SC1 = csurf(Vector((1.0, 0.13, 0.71)), Vector((-1, 0, 0)), 0.016)
    bar(SC0, SC1, 0.012, "leather", sides=4)                                               # the bayonet scabbard behind it
    bar(SC1, SC1 + (SC1 - SC0).normalized() * 0.03, 0.009, "brass", sides=4)
    HV = csurf(Vector((-1.0, 0.0, 0.86)), Vector((1, 0, 0))) + Vector((-0.028, 0, 0))     # the haversack on the right hip
    HA = 0.12; box(tuple(HV), (0.05, 0.19, 0.15), "olive", rot=(0, HA, 0))
    module("Body")
    # the sash (Accent): a flat band round the torso in a tilted plane, from the right hip over the left shoulder; each
    # point found from inside the torso outwards, so the arms in front never catch it
    lo_, hi_ = Vector((-0.15, 0.0, 0.98)), Vector((0.12, 0.0, 1.47))
    c_ = (lo_ + hi_) / 2; u_ = (hi_ - lo_).normalized(); v_ = Vector((0, 1, 0)); nrm = u_.cross(v_).normalized()
    K = 20; ring = []
    for i in range(K):
        f_ = 2 * math.pi * i / K; dr = u_ * math.cos(f_) + v_ * math.sin(f_)
        ok, at, nn, _ = hb.ray_cast(c_, dr)
        ring.append((at if ok else c_ + dr * 0.15) + dr * 0.016)
    me = bpy.data.meshes.new("sash")
    me.from_pydata([tuple(q + nrm * s * 0.024) for q in ring for s in (-1, 1)], [], [(2 * i, 2 * ((i + 1) % K), 2 * ((i + 1) % K) + 1, 2 * i + 1) for i in range(K)])
    o = bpy.data.objects.new("sash", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.008
    for f in me.polygons: f.use_smooth = True
    add(o, "accent")
    C = generic_palette(olive="5e6440", olived="3c4029", olivel="7a8058", enamel="3e4a3a", patch="7a2a1e", tinl="70726c")
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    gen = generic_detail(C, coat_o, patch=("R", "bar", "patch"), stripes="L", hc=hc)
    def detail(base, p, n, ao):   # the new kit (None: not one of these)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False, grime=0.0): return lit(c_, n, ao, metal, grime, p)
        if base == "cloth" and (p - CANT).length < 0.075:   # the bottle: a webbing carrier, its straps crossing, dark enamel showing
            q = p - CANT; r_ = math.hypot(q.y / 0.82, q.z)
            if abs(n.x) > 0.6:
                if r_ > 0.046: return C["ink"]
                if abs(q.y) < 0.007 or abs(q.z) < 0.007: return lt(C["webbing"])
                return lt(C["enamel"] if s_ > 0.0 else C["metal"], metal=True)
            if abs(q.z + 0.012) < 0.008: return C["ink"] if abs(q.z + 0.012) > 0.005 else lt(C["webbing"])
            return lt(C["webbingd"] if s_ > 0.3 else C["webbingd"].lerp(C["ink"], 0.3), grime=0.2)
        if base == "brass" and (p - CANT).length < 0.09: return C["brassl"] if s_ > 0.4 else C["brass"]
        if base in ("leather", "brass") and segd(p, SC0, SC1 + (SC1 - SC0).normalized() * 0.03)[0] < 0.03:   # the scabbard: a webbing frog, a brass chape
            if base == "brass": return C["brassl"] if s_ > 0.3 else C["brass"]
            t = segd(p, SC0, SC1)[1]
            if t < 0.2: return C["ink"] if abs(t - 0.2) < 0.03 else lt(C["webbing"])
            return lt(C["leatherl"] if s_ > 0.55 else C["leather"])
        if base == "olive":   # the haversack: a flap with an ink edge, a brass stud, two buckled straps, a stencilled number block
            q = p - HV; ca, sa = math.cos(HA), math.sin(HA); q = Vector((q.x * ca - q.z * sa, q.y, q.x * sa + q.z * ca))
            if q.z < -0.071 or abs(abs(q.y) - 0.095) < 0.005: return C["ink"]
            if abs(q.z - 0.02) < 0.004: return C["ink"]                                                     # the flap's edge
            if abs(abs(q.y) - 0.05) < 0.011 and q.z > -0.03:
                if abs(abs(q.y) - 0.05) > 0.007: return C["ink"]
                return C["brassl"] if abs(q.z + 0.01) < 0.008 else lt(C["leather"])
            if q.z < 0.0 and abs(q.y) < 0.026 and q.z > -0.05 and n.x < -0.5:                              # a pale stencilled block
                return lt(C["olived"]) if abs(q.y) > 0.02 or q.z < -0.044 or q.z > -0.006 else lt(C["pale"])
            return lt(C["olivel"] if q.z > 0.02 and s_ > 0.1 else (C["olive"] if s_ > -0.2 else C["olived"]), grime=0.2)
        if base == "metal" and (p - TIN).length < 0.09:   # the mess tin, a webbing strap round it
            q = p - TIN
            if abs(q.x) < 0.012: return C["ink"] if abs(q.x) > 0.008 else lt(C["webbing"])
            if max(abs(q.x) - 0.05, abs(q.z) - 0.0375) > -0.004: return C["ink"]
            return lt(C["tinl"] if s_ > 0.5 else C["tin"], metal=True)
        if base in ("iron", "wood") and (p - ET).length < 0.3 and x < -0.1:   # the entrenching tool: a bright worn edge, a strap
            q = p - ET
            if base == "wood": return C["ink"] if abs(q.z - 0.2) < 0.008 else psx_kit(C, "wood", p, n, ao)
            if abs(q.z - 0.012) < 0.012: return C["ink"] if abs(q.z - 0.012) > 0.008 else lt(C["webbing"])
            if q.z < -0.03 or abs(q.y) > 0.03: return C["ironl"]
            return lt(C["rust"] if _nz.noise(p * 30) > 0.4 else C["iron"], metal=True)
        if base in ("wood", "metal", "iron", "leather") and segd(p, butt, muz + d * 0.2)[0] < 0.1:   # the rifle
            t = (p - hr).dot(d)
            if base == "leather": return lt(C["leatherl"] if s_ > 0.5 else C["leather"])
            if base == "wood":
                if (p - butt).dot(d) < 0.018: return C["brassl"] if s_ > 0.3 else C["brass"]                   # the butt plate
                for tb in (0.42, (muz - hr).dot(d) - 0.14):                                                 # barrel bands
                    if abs(t - tb) < 0.012: return C["ink"] if abs(t - tb) > 0.008 else lt(C["metall"], metal=True)
                if abs(t + 0.12) < 0.005: return C["woodd"]                                                 # the stock's joint
                return psx_kit(C, base, p, n, ao)
            if base == "metal" and t < 0.14: return C["ink"] if abs(t - 0.06) < 0.005 else lt(C["metall"] if s_ > 0.3 else C["metal"], metal=True)
            if base == "iron": return lt(C["ironl"] if s_ > 0.2 else C["iron"], metal=True)               # the bayonet, bright
        return None
    PAINT = ww1_paint(C, extra=chain_paint(lambda b, p, n, ao: plain_face(C, b, p, n, ao), gen, lambda b, p, n, ao: helm(C, b, p, n, ao),
                                           lambda b, p, n, ao: coat(C, b, p, n, ao), detail))
def skirmisher():
    # Skirmisher (the generic light trooper: Skirmisher, Runner): leaning into a run, a short carbine held low, a
    # close canvas hood with a long tail over a blank mannequin face, a stick grenade in the belt; a short shoulder cape
    # in the seat colour (Accent). Painted PSX look (2026-11-10): ww1_paint for the tunic, webbing, breeches, puttees,
    # boots, gloves and satchel; the hood painted on the head in its own node (Helmet, 128 px: the face opening, its
    # rolled edge, folds). Solid only for the silhouette: Helmet (the hooded head, the hood's tail), Weapon (carbine,
    # grenade), Accent (cape).
    # Detail pass (2026-11-10): a brass clasp at the cape's throat (Helmet), a second grenade and the carbine's sling
    # (Weapon), a water bottle and a sheathed trench knife (Hip); a cartridge bandolier, a bloodied field dressing on the
    # left forearm, a knee patch, the hood's crown seam, side patch, eyelets and tail tie, generic_detail's sleeve patch,
    # wound stripes and mud; the carbine's butt plate, band and bolt painted.
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 820; TEX_SIZES = {"Body": 256, "Helmet": 128, "Weapon": 128, "Hip": 128, "Kit": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.12, 1.4)); human.aim(r, "neck01", P(0, -0.2, 1.6))
        human.reach(r, "R", P(-0.25, -0.05, 1.0), P(-0.1, -0.3, 0.98))
        human.reach(r, "L", P(0.24, -0.2, 1.05), P(0.08, -0.42, 1.08))
    hands, hp, top = human_body(pose, dress=WW1_DRESS, head="hood", mitts="leather", shape="male-young-averagemuscle-minweight")
    surf = ww1_uniform(bands=False); toeless_feet()
    fb = FACE_BOX
    head_ = next(o for o in MODULES["Body"] if o.name.startswith("Sphere") and o.data.materials[0].name == "hood")
    module("Helmet"); MODULES["Body"].remove(head_); MODULES["Helmet"].append(head_)
    back = fb["mid"] - fb["fwd"] * 0.1 + fb["up"] * 0.02   # the hood's tail, from the back of the head down the back
    chain([back, back - fb["fwd"] * 0.07 - Vector((0, 0, 0.1)), back - fb["fwd"] * 0.09 - Vector((0, 0, 0.2))], [0.035, 0.022, 0.012], "hood", subdiv=0)
    # the shoulder cape: a short flared tube lofted round the neck and shoulders along the leaning spine, each ring
    # ray-cast onto the body from outside, never narrower than the ring above
    hb = bpy.data.objects["human"]
    ax = (NECK - (JOINTS["upperleg01.L"] + JOINTS["upperleg01.R"]) / 2).normalized()
    u_ = Vector((1, 0, 0)); v_ = ax.cross(u_).normalized(); K = 14; prev = [0.0] * K; verts = []
    for t_, r0 in ((0.02, 0.07), (-0.05, 0.16), (-0.13, 0.21)):
        c0 = NECK + ax * t_
        for i in range(K):
            a_ = 2 * math.pi * i / K; d_ = u_ * math.sin(a_) + v_ * math.cos(a_) * 0.85; dn = d_.normalized()
            ok, loc, *_ = hb.ray_cast(c0 + dn * 0.6, -dn)
            r = r0 * d_.length
            if ok and (loc - c0).length < 0.3: r = max(r, (loc - c0).length + 0.016)
            r = max(r, prev[i]); prev[i] = r; verts.append(tuple(c0 + dn * r))
    me = bpy.data.meshes.new("cape"); me.from_pydata(verts, [], [(j * K + i, j * K + (i + 1) % K, (j + 1) * K + (i + 1) % K, (j + 1) * K + i) for j in range(2) for i in range(K)])
    o = bpy.data.objects.new("cape", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012
    for f in me.polygons: f.use_smooth = True
    add(o, "accent")
    # detail pass (2026-11-10): a brass clasp closing the cape at the throat
    cf = Vector(verts[K // 2]).lerp(Vector(verts[K + K // 2]), 0.45); co_ = (cf - (NECK + ax * -0.02)); co_ = (co_ - ax * co_.dot(ax)).normalized()
    CLASP = cf + co_ * 0.012
    sphere(tuple(CLASP), 0.02, "brass", (1, 1, 1), seg=8, rings=4); o = MODULES["Helmet"][-1]
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(co_); o.scale = (1, 1, 0.4)
    module("Weapon")
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    ww1_rifle(hr, hl, back=0.18, ahead=0.2, bayonet=0.15)                                # a short carbine
    g = surf(-0.12, 0.99, out=0.035)                                                      # the stick grenade in the belt
    bar(g - Vector((0, 0, 0.07)), g + Vector((0, 0, 0.03)), 0.009, "wood", sides=4)
    bar(g + Vector((0, 0, 0.03)), g + Vector((0, 0, 0.085)), 0.022, "iron", sides=6)
    # detail pass (2026-11-10): a second grenade tucked in the belt at the left, the carbine's sling, slack under it
    g2 = surf(0.075, 0.99, out=0.035); gd = Vector((0.25, 0, 1)).normalized()
    bar(g2 - gd * 0.07, g2 + gd * 0.03, 0.009, "wood", sides=4); bar(g2 + gd * 0.03, g2 + gd * 0.085, 0.022, "iron", sides=6)
    d = (hl - hr).normalized(); butt, muz = hr - d * 0.18, hl + d * 0.2
    side = d.cross(Vector((0, 0, 1))).normalized(); dn = d.cross(side).normalized()
    if dn.z > 0: dn = -dn
    sl0, sl1 = butt + d * 0.05 + dn * 0.028, muz - d * 0.06 + dn * 0.02; slm = (sl0 + sl1) / 2 + dn * 0.05
    bar(sl0, slm, 0.006, "leather", sides=3); bar(slm, sl1, 0.006, "leather", sides=3)
    module("Hip")
    skirts_ = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    def surf2(o_, d_, out=0.012):   # the outermost point of the body or the tunic skirt seen from o_ along d_
        best = None
        for ob in (hb, *skirts_):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * out
    CANT = surf2(Vector((1.0, 0.01, 0.87)), Vector((-1, 0, 0)), 0.0) + Vector((0.022, 0, 0))   # a squat water bottle on the left hip
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.045, depth=0.034, location=tuple(CANT), rotation=(0, math.pi / 2, 0))
    o = bpy.context.object; o.scale = (1, 0.85, 1); add(o, "cloth")
    bar(CANT + Vector((0, 0, 0.032)), CANT + Vector((0, 0, 0.052)), 0.009, "brass", sides=5)
    KN = surf2(Vector((0.13, -1.0, 0.93)), Vector((0, 1, 0)), 0.012); kd = Vector((0.35, -0.15, -1)).normalized()   # a trench knife in its sheath, front left
    bar(KN, KN + kd * 0.15, 0.011, "leather", sides=4); bar(KN - kd * 0.06, KN, 0.009, "wood", sides=4)
    kx = kd.cross(Vector((0, -1, 0))).normalized(); bar(KN - kx * 0.022, KN + kx * 0.022, 0.006, "brass", sides=3)
    module("Kit")
    # a leather cartridge bandolier from the left shoulder (under the cape) across the chest to the right hip: a flat
    # band laid on the front of the torso in a tilted plane, each point found from inside the torso outwards
    hips_ = (JOINTS["upperleg01.L"] + JOINTS["upperleg01.R"]) / 2; tc_ = (NECK + hips_) / 2
    blo, bhi = Vector((-0.16, tc_.y, 0.99)), Vector((0.12, (NECK.y + tc_.y) / 2, 1.42))
    bc = (blo + bhi) / 2; bu = (bhi - blo).normalized(); bv = Vector((0, -1, 0)); bv = (bv - bu * bv.dot(bu)).normalized(); bn = bu.cross(bv).normalized()
    strip = []
    for i in range(9):
        f_ = math.pi * i / 8; dr = bu * math.cos(f_) + bv * math.sin(f_)
        ok, at, *_ = hb.ray_cast(bc, dr)
        strip.append((at if ok else bc + dr * 0.15) + dr * 0.014)
    me = bpy.data.meshes.new("bandolier")
    me.from_pydata([tuple(q + bn * s * 0.022) for q in strip for s in (-1, 1)], [], [(2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1) for i in range(8)])
    o = bpy.data.objects.new("bandolier", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.008
    for f in me.polygons: f.use_smooth = True
    add(o, "olive")
    module("Body")
    C = generic_palette(hood="5a5442", hoodd="3a3628", hoodl="776f58", bandage="b8ae94", bandaged="867c66", patch="5a5a30",
                        knee="4a4a36", kneed="2e2e22")
    def lit(c, n, ao, p=None, metal=False): return psx_lit(c, n, ao, metal, p=p, mud=C["mud"])
    def hood(base, p, n, ao):
        if base != "hood": return None
        q = p - fb["mid"]; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
        if f > 0.0:
            e = (a / 0.052) ** 2 + ((u + 0.01) / 0.075) ** 2
            if e < 1.0:   # the face in the opening, shadowed under the hood's brow
                if e > 0.86: return C["ink"]
                return lit(C["mannequind"] if u > 0.03 or n.dot(PSX_LIGHT) < 0.1 else C["mannequin"], n, ao, p=p)
            if e < 1.5:   # the rolled edge, the drawstring's two eyelets under the chin
                for ex in (-0.022, 0.022):
                    if math.hypot(a - ex, u + 0.098) < 0.008: return C["ink"] if math.hypot(a - ex, u + 0.098) > 0.0045 else C["brassl"]
                return lit(C["hoodl"] if n.dot(PSX_LIGHT) > 0.3 else C["hood"], n, ao, p=p)
            if e < 1.62: return C["ink"]
        # detail pass (2026-11-10): a seam over the crown and down the back, a stitched patch on the left side, the tail's tie
        if abs(a) < 0.0055 and u > -0.02 and f < 0.06 and (p - back).length > 0.04: return C["hoodd"].lerp(C["ink"], 0.5)
        if abs(a - 0.075) < 0.03 and abs(u - 0.02) < 0.026 and f < 0.02:
            if abs(a - 0.075) > 0.024 or abs(u - 0.02) > 0.02: return C["hoodd"].lerp(C["ink"], 0.5)
            return lit(C["patch"], n, ao, p=p)
        tl = segd(p, back, back - fb["fwd"] * 0.07 - Vector((0, 0, 0.1)))
        if tl[0] < 0.05 and abs(tl[1] - 0.55) < 0.07: return C["ink"] if abs(tl[1] - 0.55) > 0.045 else lit(C["webbing"], n, ao, p=p)
        fo = (math.atan2(a, -f) * 3.0 + _nz.noise(p * 12) * 0.8) % 1.0                       # folds drawn back from the face
        return lit(C["hoodd"] if fo < 0.15 else C["hood"], n, ao, p=p)
    gen = generic_detail(C, patch=("R", "triangle", "patch"), stripes="R")
    KNEE = JOINTS["lowerleg01.R"]
    def detail(base, p, n, ao):   # the new kit and painted details (None: not here)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False): return lit(c_, n, ao, p=p, metal=metal)
        if base == "brass" and (p - CLASP).length < 0.04:   # the clasp: a rim, a raised boss
            r_ = (p - CLASP).length; return C["ink"] if 0.011 < r_ < 0.014 else (C["brassl"] if s_ > 0.3 else C["brass"])
        if base == "cloth" and (p - CANT).length < 0.07:   # the bottle: felt cover, a stitched seam, a strap over it
            q = p - CANT
            if abs(q.y) < 0.006 and q.z > 0: return C["ink"] if abs(q.y) > 0.0035 else lt(C["leather"])
            if abs(n.x) > 0.6: return C["ink"] if math.hypot(q.y / 0.85, q.z) > 0.041 else lt(C["clothl"] if s_ > 0.4 else C["cloth"])
            return lt(C["clothd"] if abs(q.x) < 0.003 else C["cloth"])
        if base == "brass" and (p - CANT).length < 0.08: return C["brassl"] if s_ > 0.4 else C["brass"]
        if (p - KN).length < 0.2 and base in ("leather", "wood", "brass") and segd(p, KN - kd * 0.06, KN + kd * 0.15)[0] < 0.03:   # the knife
            t = (p - KN).dot(kd)
            if base == "brass": return C["brassl"] if s_ > 0.2 else C["brass"]
            if base == "wood": return C["ink"] if abs(t + 0.03) < 0.004 else psx_kit(C, "wood", p, n, ao)
            if t > 0.13: return C["metall"]                                                        # the sheath's chape
            return C["ink"] if abs(t - 0.04) < 0.005 else lt(C["leatherl"] if s_ > 0.4 else C["leather"])
        if base in ("wood", "iron") and (min((p - g).length, (p - g2).length) < 0.11):   # the grenades: a ringed iron head, a brass cap
            gg, dd_ = (g, Vector((0, 0, 1))) if (p - g).length < (p - g2).length else (g2, gd)
            t = (p - gg).dot(dd_)
            if base == "wood": return C["brassl"] if t < -0.062 else (C["ink"] if abs(t + 0.02) < 0.004 else psx_kit(C, "wood", p, n, ao))
            if abs(t - 0.058) < 0.006 or abs(t - 0.033) < 0.004: return C["ink"]
            return lt(C["ironl"] if s_ > 0.5 else C["metal"], metal=True)
        if base in ("wood", "metal", "iron", "leather") and segd(p, butt, muz + d * 0.15)[0] < 0.09:   # the carbine
            t = (p - hr).dot(d)
            if base == "leather": return lt(C["leatherl"] if s_ > 0.5 else C["leather"])
            if base == "wood":
                if (p - butt).dot(d) < 0.016: return C["brassl"] if s_ > 0.3 else C["brass"]
                if abs(t - (muz - hr).dot(d) + 0.12) < 0.011: return C["ink"] if abs(t - (muz - hr).dot(d) + 0.12) > 0.007 else lt(C["metall"], True)
                return psx_kit(C, "wood", p, n, ao)
            if base == "metal" and t < 0.13: return C["ink"] if abs(t - 0.055) < 0.005 else lt(C["metall"] if s_ > 0.3 else C["metal"], True)
            if base == "iron": return lt(C["ironl"] if s_ > 0.2 else C["iron"], True)
        if base == "breeches" and (p - KNEE).length < 0.07 and n.y < -0.2:   # a knee patch sewn over a tear
            q = p - KNEE
            if abs(q.x) < 0.04 and abs(q.z) < 0.04:
                if abs(q.x) > 0.033 or abs(q.z) > 0.033: return C["kneed"]
                return lt(C["knee"])
        if base == "tunic" and y > 0.02 and abs(x) < 0.06 and 0.895 < z < 0.97:   # a webbing pouch hung behind the belt: a flap, a brass stud
            if abs(x) > 0.052 or z < 0.903: return C["ink"]
            if abs(z - 0.94) < 0.004: return C["ink"]
            if abs(x) < 0.007 and abs(z - 0.932) < 0.007: return C["brassl"]
            return lt(C["webbingl"] if z > 0.94 else C["webbing"])
        if base == "tunic":
            side_, along, ad = psx_arm(p)
            if side_ == "L" and ad < 0.06 and (abs(x) > 0.16 or y < -0.17) and 1.4 < along < 1.68 and z > 0.9:   # a field dressing round the left forearm, blood soaked through
                if along < 1.42 or along > 1.66: return C["ink"]
                if _nz.noise(p * 16 + Vector((2, 5, 1))) > 0.38: return C["blood"]
                return lt(C["bandaged"] if (along * 30) % 1.0 < 0.25 else C["bandage"])
        if base == "olive":   # the bandolier (leather): pockets, each a flap with a brass stud
            q = p - bc; f_ = math.atan2(q.dot(bv), q.dot(bu)) / math.pi; w_ = q.dot(bn)
            if abs(w_) > 0.022: return C["ink"]
            k = (f_ * 7) % 1.0
            if 0.15 < f_ < 0.9 and n.dot((p - bc).normalized()) > 0.3:
                if k < 0.12 or k > 0.88: return C["ink"]
                if abs(w_) < 0.007 and abs(k - 0.62) < 0.1: return C["brassl"] if s_ > 0.0 else C["brass"]
                if k < 0.45: return C["ink"] if k > 0.38 else lt(C["leatherl"])
            return lt(C["leather"])
        return None
    PAINT = ww1_paint(C, extra=chain_paint(hood, detail, gen))
def shield():
    # Shield-bearer (the generic shield wall: Bulwark, Shieldbearer): an enclosed iron bucket helm (no head under it),
    # rounded iron pauldrons, a knee-length greatcoat, a breastplate; a tall planked shield held out on the left arm, its
    # face in the seat colour (Accent) with two iron bands and a boss, a spiked mace raised in the right. Painted PSX look
    # (2026-11-10): ww1_paint for the tunic, webbing, breeches, puttees, boots and gloves; the breastplate, the helm's
    # eye slit, breaths and rivets, the pauldrons' lames and the shield's planks and rim painted; the greatcoat skirt
    # draped (Coat). Solid only for the silhouette: Helmet (helm, pauldrons), Shield (board, bands, boss), Weapon (mace),
    # Accent (the shield's face).
    # Detail pass (2026-11-10): a rondel dagger and a belt pouch outside the coat (Hip); the helm's side seams and rivet
    # rows, rust run down from its rivets, a dent; buckled straps over the pauldrons, worn lame edges; the shield's bands
    # riveted, the boss ringed, hack marks on the rim; the mace's leather grip, collar and head seam; the breastplate
    # scratched, rusted and dented, buckled side straps; generic_detail's coat pockets, half-belt, patch and mud.
    global BODY_BUDGET, TEX_SIZES, PAINT
    BODY_BUDGET = 760; TEX_SIZES = {"Body": 256, "Coat": 256, "Helmet": 128, "Shield": 128, "Weapon": 128, "Hip": 128}
    from mathutils import noise as _nz
    def pose(r):
        human.reach(r, "L", P(0.3, -0.08, 1.12), P(0.16, -0.3, 1.05))
        human.reach(r, "R", P(-0.33, -0.02, 1.4), P(-0.3, -0.1, 1.62))
    hands, hp, top = human_body(pose, dress=WW1_DRESS, head=None, mitts="leather", shape="male-young-averagemuscle-averageweight")
    ww1_uniform(bands=False); toeless_feet()
    coat = psx_greatcoat(); coat_o = MODULES["Coat"][0]
    hb = bpy.data.objects["human"]
    def csurf(o_, d_, out=0.0):   # the outermost point of the body or the coat seen from o_ along d_, out from it
        best = None
        for ob in (hb, coat_o):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.8) - d_ * out
    module("Hip")
    RD = csurf(Vector((-0.11, -1.0, 0.95)), Vector((0, 1, 0)), 0.014); rdd = Vector((-0.3, -0.12, -1)).normalized()   # the rondel dagger, front right
    bar(RD, RD + rdd * 0.17, 0.011, "leather", sides=4); bar(RD - rdd * 0.07, RD, 0.008, "wood", sides=4)
    for k_ in (0.0, -0.075): sphere(tuple(RD + rdd * k_), 0.017, "brass", (1, 1, 0.35), seg=6, rings=3)   # its two rondels
    PO = csurf(Vector((-1.0, -0.05, 0.9)), Vector((1, 0, 0))) + Vector((-0.024, 0, 0))   # a leather belt pouch on the right hip
    box(tuple(PO), (0.045, 0.1, 0.085), "leather", rot=(0, 0.12, 0))
    module("Helmet")
    c = Vector((hp.x, hp.y, top.z - 0.09))
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.125, depth=0.32, location=tuple(c - Vector((0, 0, 0.03)))); add(bpy.context.object, "iron")   # down onto the shoulders: no gap at the neck
    sphere(tuple(c + Vector((0, 0, 0.13))), 0.125, "iron", (1, 1, 0.35), seg=12, rings=4)
    pads = [Vector((0.2 * s, 0, 1.38)) for s in (-1, 1)]
    for q in pads: sphere(tuple(q), 0.1, "iron", (1.1, 1.1, 0.7), seg=8, rings=5)
    module("Shield")
    hl = hands["L"]; sc = Vector((hl.x, hl.y - 0.07, 0.85))
    box(tuple(sc), (0.42, 0.04, 0.78), "wood")
    box((hl.x, hl.y - 0.095, 0.87), (0.36, 0.012, 0.7), "accent")
    for bz in (0.6, 1.14): box((hl.x, hl.y - 0.105, bz), (0.37, 0.012, 0.04), "iron")   # two iron bands and a boss: no emblem, it stands in for several units
    sphere((hl.x, hl.y - 0.1, 0.87), 0.05, "iron", (1, 0.55, 1), seg=8, rings=4)
    # detail pass (2026-11-10): a vertical strap between the bands, a flange under the boss, iron corner brackets
    fy = hl.y - 0.105
    box((hl.x, fy, 0.87), (0.04, 0.01, 0.5), "iron")
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.068, depth=0.01, location=(hl.x, fy - 0.006, 0.87), rotation=(math.pi / 2, 0, 0)); add(bpy.context.object, "iron")
    CORNERS = [Vector((hl.x + sx * 0.165, fy, 0.87 + sz * 0.335)) for sx in (-1, 1) for sz in (-1, 1)]
    for cq in CORNERS:
        sx, sz = (1 if cq.x > hl.x else -1), (1 if cq.z > 0.87 else -1)
        box(tuple(cq - Vector((sx * 0.03, 0, 0))), (0.085, 0.01, 0.024), "iron"); box(tuple(cq - Vector((0, 0, sz * 0.03))), (0.024, 0.01, 0.085), "iron")
    module("Weapon")
    hr = hands["R"]; up = Vector((0, 0, 1)); mh = hr + up * 0.34
    bar(hr - up * 0.12, hr + up * 0.3, 0.017, "wood")
    sphere(tuple(mh), 0.055, "iron", seg=8, rings=6)
    for a in range(6):
        o = Vector((math.cos(a), math.sin(a), 0)) * 0.055
        bar(mh + o * 0.5, mh + o * 1.4, 0.014, "metal", sides=3)
    module("Body")
    C = generic_palette()
    def lit(c, n, ao, metal=False, p=None): return psx_lit(c, n, ao, metal, 0.0, p, C["mud"])
    def iron(n): s = n.dot(PSX_LIGHT); return C["ironl"] if s > 0.62 else (C["iron"] if s > -0.2 else C["irond"])
    def helm(base, p, n, ao):
        if base != "iron": return None
        q = p - c
        if q.to_2d().length < 0.15 and -0.2 < q.z < 0.2:   # the bucket helm
            if q.z > 0.12:   # the crown plate: a cross of riveted straps
                if abs(q.x) < 0.012 or abs(q.y) < 0.012: return C["ink"] if max(abs(q.x), abs(q.y)) < 0.016 and min(abs(q.x), abs(q.y)) > 0.008 else lit(C["irond"], n, ao, True, p)
                return lit(iron(n), n, ao, True, p)
            if q.z < -0.115: return C["ink"] if abs(q.z + 0.118) < 0.004 else lit(C["irond"], n, ao, True, p)   # the gorget under a seam
            if abs(q.z - 0.115) < 0.006: return C["ink"]                                           # the crown seam
            if abs(q.z - 0.107) < 0.004 and n.dot(PSX_LIGHT) > -0.2: return lit(C["ironl"], n, ao, True, p)   # its worn lip
            RIV = [c + Vector((0.125 * math.sin(k * math.pi / 4), -0.125 * math.cos(k * math.pi / 4), 0.095)) for k in range(8)]
            SEAMR = [c + Vector((sx * 0.125, 0, rz)) for sx in (-1, 1) for rz in (0.045, -0.005, -0.055)]   # the side seams' rivet rows
            r_ = psx_dot(p, RIV + SEAMR, C, 0.008, m="iron")
            if r_ is not None: return r_
            if abs(q.y) < 0.011 and abs(q.x) > 0.1 and q.z < 0.09: return C["ink"] if abs(q.y) < 0.004 else lit(C["irond"], n, ao, True, p)   # the side seams, plate over plate
            for rp in RIV + SEAMR:   # rust run down from each rivet
                e_ = p - rp
                if -0.06 * (0.6 + 0.4 * _nz.noise(rp * 9)) < e_.z < -0.012 and Vector((e_.x, e_.y)).length < 0.0055: return lit(C["rust"], n, ao, False, p)
            if q.y < 0:
                if abs(q.z - 0.02) < 0.016 and abs(q.x) < 0.1 and abs(q.x) > 0.008:              # the eye slits, a nose bar between
                    return C["ink"] if abs(q.z - 0.02) > 0.011 else C["dark"]
                if abs(q.z - 0.042) < 0.005 and abs(q.x) < 0.1: return lit(C["ironl"], n, ao, True, p)   # the brow's light edge over the slits
                if abs(q.x) < 0.012 and q.z < 0.1: return C["ink"] if abs(q.x) > 0.008 else lit(C["ironl"], n, ao, True, p)   # the front ridge
                if q.x < -0.03 and -0.09 < q.z < -0.02 and (q.x * 90) % 1.0 < 0.35 and (q.z * 90) % 1.0 < 0.35: return C["dark"]   # breaths
                dd = math.hypot(q.x - 0.06, q.z + 0.06)   # a dent on the left cheek: dark in the hollow, light on its lower lip
                if dd < 0.022: return lit(C["irond"] if q.z > -0.062 else C["ironl"], n, ao, True, p)
            elif q.z < 0.1:   # the back: a riveted seam down the middle, its plate edge catching the light
                if abs(q.x) < 0.012: return C["ink"] if abs(q.x) < 0.004 else C["ironl"]
                r_ = psx_dot(p, [c + Vector((0.022, 0.125, rz)) for rz in (0.06, 0.0, -0.06)], C, 0.007, m="iron")
                if r_ is not None: return r_
            if _nz.noise(p * 24) > 0.5: return lit(C["rust"] if _nz.noise(p * 70) > 0 else C["rustd"], n, ao, False, p)
            return lit(iron(n) if q.y < 0 else C["iron"], n, ao, True, p)   # the back kept readable, not black
        for pd in pads:   # the pauldrons: three lames down the arm, a rivet at each end, a buckled strap over the top
            e = p - pd
            if e.length < 0.13:
                if n.z < -0.6: return C["dark"]
                sgn = 1 if pd.x > 0 else -1
                if n.z > 0.45 and abs(e.y) < 0.014:
                    if abs(e.y) > 0.01: return C["ink"]
                    if abs(e.x * sgn + 0.025) < 0.011: return C["ink"] if abs(e.x * sgn + 0.025) > 0.007 else C["brassl"]   # the buckle
                    return lit(C["leatherl"] if n.dot(PSX_LIGHT) > 0.5 else C["leather"], n, ao, p=p)
                k = (e.z + 0.07) / 0.035
                if k % 1.0 < 0.14: return C["ink"]
                r_ = psx_dot(p, [pd + Vector((0, sy * 0.105, 0.03)) for sy in (-1, 1)], C, 0.008, m="brass")
                if r_ is not None: return r_
                if k % 1.0 > 0.8 and n.dot(PSX_LIGHT) > -0.3: return lit(C["ironl"], n, ao, True, p)   # each lame's worn lower edge
                if _nz.noise(p * 26 + Vector((4, 0, 0))) > 0.55: return lit(C["rust"], n, ao, False, p)
                return lit(iron(n), n, ao, True, p)
        return None
    def board(base, p, n, ao):
        if base != "wood" or (p - sc).length > 0.5 or abs(p.x - sc.x) > 0.23: return None
        q = p - sc
        if abs(q.x) > 0.19 or abs(q.z) > 0.37:   # the iron rim, hacked and nicked bright in places
            if abs(q.x) > 0.205 or abs(q.z) > 0.385: return C["ink"]
            if _nz.noise(p * Vector((30, 30, 30)) + Vector((1, 2, 3))) > 0.5: return C["ironl"]
            return lit(iron(n), n, ao, True, p)
        r_ = psx_dot(p, [sc + Vector((sx * 0.197, q.y, sz * 0.12)) for sx in (-1, 1) for sz in (-3, -1, 1, 3)], C, 0.007, m="brass")
        if r_ is not None: return r_
        if (q.x + 0.21) % 0.07 < 0.005: return C["ink"]                                         # planks
        if q.y > 0 and abs(q.x) < 0.03 and abs(q.z - 0.02) < 0.05:   # the hand grip's iron straps behind
            return C["ink"] if abs(q.x) > 0.024 or abs(q.z - 0.02) > 0.044 else lit(iron(n), n, ao, True, p)
        if q.y > 0 and abs(abs(q.z) - 0.2) < 0.022: return C["ink"] if abs(abs(q.z) - 0.2) > 0.016 else lit(C["leather"], n, ao, p=p)   # arm straps
        return psx_kit(C, "wood", p, n, ao)
    def fittings(base, p, n, ao):   # the shield's bands (riveted) and boss (ringed); the mace's head
        if base not in ("metal", "iron"): return None
        s_ = n.dot(PSX_LIGHT)
        if (p - mh).length < 0.12:
            if base == "metal": return lit(C["ironl"] if s_ > 0.3 else C["iron"], n, ao, True, p)          # the flanges, bright edged
            return C["ink"] if abs((p - mh).z) < 0.005 else psx_kit(C, "iron", p, n, ao)                     # the head, a forge seam round it
        if (p - sc).length > 0.55: return None
        bo = Vector((hl.x, hl.y - 0.1, 0.87))
        r2 = math.hypot(p.x - bo.x, p.z - bo.z)
        if r2 < 0.047 and p.y < fy - 0.0118:   # the boss: a dark ring at its foot, a hot highlight up and to the left
            if r2 > 0.042: return C["ink"]
            if math.hypot(p.x - bo.x + 0.014, p.z - bo.z - 0.014) < 0.01: return C["ironl"]
            return lit(iron(n), n, ao, True, p)
        if r2 < 0.072 and p.y < fy:   # the flange: an ink rim, six rivets round it
            if r2 > 0.063: return C["ink"]
            r_ = psx_dot(p, [Vector((bo.x + 0.054 * math.cos(k * math.pi / 3), p.y, bo.z + 0.054 * math.sin(k * math.pi / 3))) for k in range(6)], C, 0.0055, m="iron")
            if r_ is not None: return r_
            return lit(C["irond"] if r2 < 0.047 else iron(n), n, ao, True, p)
        if abs(p.x - hl.x) < 0.024 and abs(p.z - 0.87) < 0.26 and p.y < fy + 0.004 and min(abs(p.z - 0.6), abs(p.z - 1.14)) > 0.02:   # the vertical strap, riveted
            if abs(p.x - hl.x) > 0.016: return C["ink"]
            r_ = psx_dot(p, [Vector((hl.x, p.y, 0.87 + k * 0.08)) for k in (-3, -2, 2, 3)], C, 0.0055, m="iron")
            if r_ is not None: return r_
            return lit(C["ironl"] if p.x < hl.x - 0.008 and s_ > -0.3 else iron(n), n, ao, True, p)
        for cq in CORNERS:   # the corner brackets: a rivet in each arm, a bright worn edge
            q = p - cq
            if abs(q.x) < 0.12 and abs(q.z) < 0.12 and p.y < fy + 0.004 and min(abs(p.z - 0.6), abs(p.z - 1.14)) > 0.02:
                sx, sz = (1 if cq.x > hl.x else -1), (1 if cq.z > 0.87 else -1)
                r_ = psx_dot(p, [cq - Vector((sx * 0.05, 0, 0)), cq - Vector((0, 0, sz * 0.05)), cq], C, 0.0055, m="iron")
                if r_ is not None: return r_
                if abs(q.x) > 0.008 and abs(q.z) > 0.008 and min(abs(q.x - sx * 0.0115), abs(q.z - sz * 0.0115)) < 0.003: return C["ink"]
                return lit(C["ironl"] if q.z * sz > 0.004 or q.x * sx > 0.004 else iron(n), n, ao, True, p)
        for bz in (0.6, 1.14):
            q = p - Vector((hl.x, hl.y - 0.105, bz))
            if abs(q.z) < 0.03 and abs(q.x) < 0.2:
                if abs(q.z) > 0.016 or abs(q.x) > 0.178: return C["ink"]
                r_ = psx_dot(p, [Vector((hl.x + k * 0.058, p.y, bz)) for k in range(-3, 4)], C, 0.0065, m="iron")
                if r_ is not None: return r_
                if q.z > 0.009 and s_ > -0.4: return lit(C["ironl"], n, ao, True, p)
                if _nz.noise(p * 28) > 0.5: return lit(C["rust"], n, ao, False, p)
                return lit(iron(n), n, ao, True, p)
        return psx_kit(C, "iron", p, n, ao)
    def haft(base, p, n, ao):   # the mace's haft: a leather grip bound round it, an iron collar under the head
        if base != "wood" or (p - hr).length > 0.45 or abs(p.x - hr.x) > 0.05: return None
        t = p.z - hr.z
        if 0.27 < t < 0.3: return lit(iron(n), n, ao, True, p)
        if -0.1 < t < 0.08:
            fr = (t * 45 + math.atan2(p.x - hr.x, p.y - hr.y) / (2 * math.pi)) % 1.0
            return C["ink"] if fr < 0.2 else lit(C["leatherl"] if fr > 0.75 else C["leather"], n, ao, p=p)
        return psx_kit(C, "wood", p, n, ao)
    def kit(base, p, n, ao):   # the dagger and the pouch
        s_ = n.dot(PSX_LIGHT)
        if (p - RD).length < 0.26 and base in ("leather", "wood", "brass") and segd(p, RD - rdd * 0.08, RD + rdd * 0.17)[0] < 0.03:
            if base == "brass": return C["ink"] if n.dot(rdd) ** 2 < 0.2 else (C["brassl"] if s_ > 0.2 else C["brass"])
            if base == "wood": return C["ink"] if abs((p - RD).dot(rdd) + 0.035) < 0.004 else psx_kit(C, "wood", p, n, ao)
            t = (p - RD).dot(rdd)
            return C["metall"] if t > 0.15 else (C["ink"] if abs(t - 0.05) < 0.005 else lit(C["leatherl"] if s_ > 0.4 else C["leather"], n, ao, p=p))
        if base == "leather" and (p - PO).length < 0.1:
            q = p - PO; ca, sa = math.cos(0.12), math.sin(0.12); q = Vector((q.x * ca - q.z * sa, q.y, q.x * sa + q.z * ca))
            if q.z < -0.038 or abs(q.y) > 0.046: return C["ink"]
            if abs(q.z - 0.012) < 0.004: return C["ink"]
            if abs(q.y) < 0.007 and abs(q.z) < 0.008: return C["brassl"]
            return lit(C["leatherl"] if q.z > 0.012 else C["leather"], n, ao, p=p)
        return None
    def chest(base, p, n, ao):   # the breastplate over the tunic: a ridge, a rolled neck edge, rivets, two lames
        if base != "tunic": return None
        x, y, z = p
        side_, along, ad = psx_arm(p)
        if y > -0.05 or ad < 0.075 and abs(x) > 0.13: return None
        top_ = 1.38 - 3.0 * x * x
        if abs(x) > 0.15 or not (1.03 <= z < top_): return None
        ed = min(z - 1.03, top_ - z, 0.15 - abs(x))
        if ed < 0.008: return C["ink"]
        r_ = psx_dot(p, [Vector((sx * 0.12, y, rz)) for sx in (-1, 1) for rz in (1.1, 1.22)], C, 0.0065)
        if r_ is not None: return r_
        if 0.15 - abs(x) < 0.03 and abs(z - 1.2) < 0.014:   # buckled side straps
            if abs(z - 1.2) > 0.01: return C["ink"]
            return C["brassl"] if 0.15 - abs(x) > 0.02 else lit(C["leather"], n, ao, p=p)
        for L_ in (1.12, 1.07):
            if abs(z - L_) < 0.004: return C["ink"]
        dd = math.hypot(x + 0.06, z - 1.25)   # a dent below the right collarbone
        if dd < 0.02: return lit(C["irond"] if z > 1.25 else C["ironl"], n, ao, True, p)
        if _nz.noise(p * 22 + Vector((9, 9, 9))) > 0.56: return lit(C["rust"] if _nz.noise(p * 60) > 0 else C["rustd"], n, ao, False, p)
        cc = C["ironl"] if (ed < 0.016 and z > 1.28) or abs(x) < 0.006 else iron(n)
        return lit(cc, n, ao, True, p)
    gen = generic_detail(C, coat_o, patch=None)
    PAINT = ww1_paint(C, extra=chain_paint(helm, board, haft, kit, fittings, chest, gen, lambda b, p, n, ao: coat(C, b, p, n, ao)))
def standard():
    # Standard-bearer (the generic banner and lamp: Banner Carrier, Lamp Keeper): a tall pole held upright, its pennant
    # in the seat colour (Accent), a lantern hung from the crossbar, a brass finial; a steel helmet over a blank mannequin
    # face and a knee-length greatcoat. Painted PSX look (2026-11-10): ww1_paint for the tunic, webbing, breeches,
    # puttees, boots and gloves; the greatcoat skirt draped (Coat); the lantern's lit panes and frame, the pole's grain
    # and brass painted. Solid only for the silhouette: Helmet (the steel helmet), Weapon (pole, lantern), Accent.
    # Detail pass (2026-11-10): the lantern's corner posts, peaked cap and bale, brass caps on the crossbar's end and two
    # cord tassels under the finial (Weapon); an oil flask on the left hip, a blanket roll across the small of the back
    # (Hip); the pole's brass ferrules, leather grip
    # wraps and iron shoe, the lantern's sooty cap and hanger chain painted; a whistle on its lanyard, generic_detail's coat
    # pockets, half-belt, patch and mud, shoulder straps, a sleeve patch, wound stripes and the helmet's lugs and dent.
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 820; TEX_SIZES = {"Body": 256, "Coat": 256, "Helmet": 128, "Weapon": 256, "Hip": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    def pose(r):
        human.reach(r, "R", P(-0.24, -0.02, 1.08), P(-0.15, -0.2, 1.02))
        human.reach(r, "L", P(0.12, -0.18, 1.2), P(-0.1, -0.22, 1.36))
    hands, hp, top = human_body(pose, dress=WW1_DRESS, head="mannequin", mitts="leather", shape="male-young-averagemuscle-minweight")
    ww1_uniform(bands=False); toeless_feet()
    coat = psx_greatcoat(); coat_o = MODULES["Coat"][0]
    hb = bpy.data.objects["human"]
    module("Helmet"); helm = psx_brodie(hp, top); hc = Vector((hp.x, hp.y - 0.008, top.z - 0.075))
    module("Hip")
    best = None   # the oil flask on the left hip, outside the coat
    for ob in (hb, coat_o):
        ok, at, *_ = ob.ray_cast(Vector((1.0, -0.01, 0.87)), Vector((-1, 0, 0)))
        if ok and (best is None or at.x > best.x): best = at
    FL = (best if best is not None else Vector((0.2, -0.01, 0.87))) + Vector((0.033, 0, 0))
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.03, depth=0.1, location=tuple(FL)); add(bpy.context.object, "metal")
    bar(FL + Vector((0, 0, 0.05)), FL + Vector((0, 0, 0.072)), 0.011, "wood", sides=4)   # its stopper
    ok, bk, *_ = hb.ray_cast(Vector((0, 1.0, 1.06)), Vector((0, -1, 0)))   # a blanket roll strapped across the small of the back
    BR = (bk if ok else Vector((0, 0.12, 1.06))) + Vector((0, 0.036, 0))
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.036, depth=0.3, location=tuple(BR), rotation=(0, math.pi / 2, 0)); add(bpy.context.object, "rags")
    module("Weapon")
    x, y = FISTS["R"][0].x, FISTS["R"][0].y
    base, tip = Vector((x, y, 0.05)), Vector((x, y, 2.0))
    bar(base, tip, 0.017, "wood")
    sphere(tuple(tip + Vector((0, 0, 0.03))), 0.035, "brass", seg=8, rings=5)
    bar(tip + Vector((0, 0, 0.03)), tip + Vector((0, 0, 0.15)), 0.01, "brass", sides=4)
    bar(tip - Vector((0, 0, 0.05)), tip - Vector((-0.42, 0, 0.05)), 0.008, "wood", sides=4)
    me = bpy.data.meshes.new("pennant"); x0, x1, zt, zb = x, x + 0.42, tip.z - 0.05, tip.z - 0.65
    me.from_pydata([(x0, y, zt), (x1, y, zt), (x1, y, zb), ((x0 + x1)/2, y, zb + 0.15), (x0, y, zb)], [], [(0, 4, 3, 2, 1)])
    o = bpy.data.objects.new("pennant", me); scn.collection.objects.link(o)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.015; add(o, "accent")
    L = tip - Vector((-0.42, 0, 0)); lc = L - Vector((0, 0, 0.27))
    bar(L - Vector((0, 0, 0.05)), L - Vector((0, 0, 0.2)), 0.004, "metal", sides=3)
    box(tuple(lc), (0.07, 0.07, 0.1), "glow")
    box(tuple(L - Vector((0, 0, 0.21))), (0.09, 0.09, 0.02), "iron"); box(tuple(L - Vector((0, 0, 0.33))), (0.09, 0.09, 0.02), "iron")
    # detail pass (2026-11-10): corner posts, a peaked cap, a brass cap on the crossbar's end, two tassels under the finial
    for sx in (-1, 1):
        for sy in (-1, 1): bar(lc + Vector((sx * 0.038, sy * 0.038, -0.055)), lc + Vector((sx * 0.038, sy * 0.038, 0.055)), 0.006, "iron", sides=3)
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.058, radius2=0.012, depth=0.045, location=tuple(L - Vector((0, 0, 0.178))), rotation=(0, 0, math.pi / 4))
    add(bpy.context.object, "iron")
    sphere(tuple(L - Vector((0, 0, 0.05))), 0.014, "brass", seg=6, rings=4)
    TAS = [tip + Vector((sx * 0.026, -0.012, -0.02)) for sx in (-1, 1)]
    for t0 in TAS:
        t1 = t0 + Vector((0.006 * (1 if t0.x > x else -1), 0, -0.13)); bar(t0, t1, 0.0045, "rope", sides=3)
        bar(t1, t1 - Vector((0, 0, 0.04)), 0.012, "rope", sides=5)
    module("Body")
    C = generic_palette(glowd="a07028", glowl="fff0b0", ochre="8a6a2a", pewter="6e706a", pewterl="9a9c94", soot="1e1c1a")
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    gen = generic_detail(C, coat_o, patch=("L", "disc", "ochre"), stripes="R", hc=hc)
    def lantern(base, p, n, ao):
        s_ = n.dot(PSX_LIGHT)
        if base == "glow":   # lit panes behind an iron frame and a cross bar
            q = p - lc; u = q.x if abs(n.x) < 0.5 else q.y
            if abs(u) > 0.028 or abs(q.z) < 0.004: return C["ink"]
            return C["glowl"] if abs(u) < 0.012 and q.z > 0 else (C["glow"] if q.z > -0.03 else C["glowd"])
        if base == "rope":   # the tassels: gilt cord, a bound head, frayed ends
            t1 = min(TAS, key=lambda t0: (p - t0).length)
            if p.z < t1.z - 0.16: return C["giltd"] if (math.atan2(p.x - t1.x, p.y - t1.y) * 4) % 1.0 < 0.35 else C["gilt"]
            if p.z < t1.z - 0.135: return C["ink"] if abs(p.z - (t1.z - 0.145)) < 0.005 else (C["giltl"] if s_ > 0.2 else C["gilt"])
            return C["giltl"] if s_ > 0.4 else C["gilt"]
        if (p - lc).length < 0.2 and base in ("iron", "brass"):   # the lantern's frame and cap: sooty above, rivets at the plate corners
            q = p - lc
            if base == "brass": return C["brassl"] if s_ > 0.3 else C["brass"]
            if q.z > 0.075: return lit(C["soot"] if _nz.noise(p * 40) > -0.2 else C["irond"], n, ao, True, p)
            if abs(abs(q.z) - 0.06) < 0.012:
                r_ = psx_dot(p, [lc + Vector((sx * 0.038, sy * 0.038, q.z)) for sx in (-1, 1) for sy in (-1, 1)], C, 0.006, m="brass")
                if r_ is not None: return r_
                return C["ink"] if abs(abs(q.z) - 0.06) > 0.009 else lit(C["ironl"] if s_ > 0.2 else C["iron"], n, ao, True, p)
            return lit(C["ironl"] if s_ > 0.4 else C["irond"], n, ao, True, p)
        if base == "rags":   # the blanket roll: rolled ends, two buckled straps, a grey stripe woven in
            q = p - BR
            if abs(n.x) > 0.6: return C["ink"] if (math.hypot(q.y, q.z) * 150) % 1.0 < 0.3 else lit(C["rags"], n, ao, p=p)
            if abs(abs(q.x) - 0.09) < 0.013:
                if abs(abs(q.x) - 0.09) > 0.009: return C["ink"]
                return C["brassl"] if q.y > 0.02 and abs(q.z) < 0.012 else lit(C["leather"], n, ao, p=p)
            if abs(abs(q.x) - 0.125) < 0.006: return lit(C["ragsd"], n, ao, p=p)
            return lit(C["ragsd"] if math.sin(q.x * 140 + _nz.noise(p * 20)) > 0.75 else C["rags"], n, ao, grime=0.2, p=p)
        if base == "metal" and (p - FL).length < 0.09:   # the oil flask: pewter, a strap round it, a stamped band
            q = p - FL
            if abs(q.z) < 0.008: return C["ink"] if abs(q.z) > 0.005 else lit(C["leather"], n, ao, p=p)
            if abs(q.z - 0.042) < 0.004 or abs(q.z + 0.046) < 0.004: return C["ink"]
            return lit(C["pewterl"] if s_ > 0.45 else C["pewter"], n, ao, True, p)
        if base == "wood" and (p - FL).length < 0.12: return lit(C["wood"], n, ao, p=p)
        if base == "metal" and (p - L).length < 0.25:   # the hanger: a chain of links
            return C["ink"] if (p.z * 70) % 1.0 < 0.3 else lit(C["metall"], n, ao, True, p)
        if base == "wood" and abs(p.x - x) < 0.03 and abs(p.y - y) < 0.03:   # the pole: an iron shoe, brass ferrules, leather grips where the hands hold it
            z = p.z
            if z < 0.14: return lit(C["ironl"] if s_ > 0.2 else C["iron"], n, ao, True, p)
            if abs(z - 0.14) < 0.012 or abs(z - (tip.z - 0.075)) < 0.014: return C["ink"] if abs(z - 0.14) > 0.009 and z < 1 else (C["brassl"] if s_ > 0.2 else C["brass"])
            for hz in (FISTS["R"][0].z, FISTS["L"][0].z):
                if abs(z - hz) < 0.13:   # wider than the fist, so the binding shows above and below it
                    fr = (z * 50 + math.atan2(p.x - x, p.y - y) / (2 * math.pi)) % 1.0
                    return C["ink"] if fr < 0.18 else lit(C["leatherl"] if fr > 0.7 else C["leather"], n, ao, p=p)
            return psx_kit(C, "wood", p, n, ao)
        if base == "wood" and abs(p.z - (tip.z - 0.05)) < 0.03:   # the crossbar: grain, a brass band at the pole
            return C["brass"] if abs(p.x - x) < 0.05 else psx_kit(C, "wood", p, n, ao)
        if base in ("brass", "iron", "metal", "wood"): return psx_kit(C, base, p, n, ao)
        return None
    def whistle(base, p, n, ao):   # a whistle on a lanyard, from the left shoulder strap to the left breast pocket
        if base != "tunic" or p.y > 0: return None
        dd, t = segd(Vector((p.x, p.z, 0)), Vector((0.12, 1.42, 0)), Vector((0.07, 1.27, 0)))
        if dd < 0.006: return lit(C["pale"], n, ao, p=p)
        q = Vector((p.x - 0.068, p.z - 1.255))
        if abs(q.x) < 0.012 and abs(q.y) < 0.022: return C["ink"] if abs(q.x) > 0.008 or abs(q.y) > 0.018 else (C["brassl"] if q.y > 0 else C["brass"])
        return None
    PAINT = ww1_paint(C, extra=chain_paint(lambda b, p, n, ao: plain_face(C, b, p, n, ao), gen, lambda b, p, n, ao: helm(C, b, p, n, ao),
                                           lambda b, p, n, ao: coat(C, b, p, n, ao), lantern, whistle))

def robe_and_tabard(rnd):
    # long robe with a torn hem
    robe = cloth_cone(0, 0, 0.98, 0.12, 0.165, 0.21, "robe", sy=0.8, ragged=0.1, rnd=rnd, tris=150, thick=0.012)
    # rope belt, knot and rosary ending in a small cross
    bpy.ops.mesh.primitive_torus_add(major_radius=0.165, minor_radius=0.016, major_segments=20, minor_segments=4, location=(0, 0, 0.98))
    t = bpy.context.object; t.scale = (1, 0.78, 1); add(t, "rope")
    chain([(0.07, -0.135, 0.97), (0.08, -0.15, 0.8)], [0.014, 0.012], "rope", subdiv=0)
    for i in range(6): sphere((-0.06 + i*0.004, -0.14 - i*0.006, 0.95 - i*0.04), 0.012, "bone", seg=6, rings=4)
    box((-0.036, -0.18, 0.67), (0.012, 0.012, 0.07), "bone"); box((-0.036, -0.18, 0.685), (0.045, 0.012, 0.012), "bone")
    # tabard down the front, ragged at the bottom, with a bone cross
    y0, y1 = -0.135, -0.2
    cloth_panel([(-0.1, y0, 1.38), (0.1, y0, 1.38)], 0.9, "accent", extra=[robe], ragged=0.08, rnd=rnd, tris=16, thick=0.01)
    box((0, -0.19, 1.05), (0.045, 0.016, 0.34), "bone"); box((0, -0.186, 1.15), (0.17, 0.016, 0.045), "bone")

def ww1_uniform(tunic="tunic", band="webbing", bands=True):
    # The WWI service dress (the Trench Pilgrim, then the Principality, 2026-11-03), on a body dressed with the tunic,
    # breeches, puttee and boots materials: loose wool (breeches ballooning at the sides of the thigh, a tunic a size
    # too big), the tunic's skirt below the belt and a pale band at the top of each puttee. Returns surf(x, z, front,
    # out): a point on the body's front (or back) at (x, z), a little proud of it, for kit laid on the uniform.
    human_ = bpy.data.objects["human"]
    for v in human_.data.vertices:   # loose wool: breeches ballooning at the thigh, a tunic a size too big
        z = v.co.z
        if 0.45 < z < 0.86: v.co += v.normal * 0.03 * math.sin(math.pi * (z - 0.45) / 0.41) * abs(v.normal.x) ** 1.5   # out at the sides, low on the thigh
        elif 0.98 <= z < 1.42: v.co += v.normal * 0.012
    def surf(x, z, front=True, out=0.012):   # a point on the body's front (or back) at (x, z), a little proud of it
        d_ = 1 if front else -1
        ok, loc, *_ = human_.ray_cast(Vector((x, -1.0 * d_, z)), Vector((0, d_, 0)))
        return (loc if ok else Vector((x, -0.15 * d_, z))) + Vector((0, -out * d_, 0))
    # the tunic's skirt below the belt, hanging over the breeches
    cloth_cone(0, 0, 1.0, 0.82, 0.15, 0.165, tunic, sy=0.8, tris=50, thick=0.01, rows=4)
    # a pale band at the top of each puttee (bands=False: a kneeling figure, whose shins are not upright)
    for x in ((-0.1, 0.1) if bands else ()):   # each band found on its own leg, wherever the pose put it
        f_, b_ = human_.ray_cast(Vector((x, -1, 0.42)), Vector((0, 1, 0))), human_.ray_cast(Vector((x, 1, 0.42)), Vector((0, -1, 0)))
        y_ = (f_[1].y + b_[1].y) / 2 if f_[0] and b_[0] else 0.01
        o_, i_ = human_.ray_cast(Vector((x * 6, y_, 0.42)), Vector((-x / abs(x), 0, 0))), human_.ray_cast(Vector((0, y_, 0.42)), Vector((x / abs(x), 0, 0)))
        cx_ = (o_[1].x + i_[1].x) / 2 if o_[0] and i_[0] else x
        r_ = max(abs(o_[1].x - cx_) if o_[0] else 0.05, (b_[1].y - f_[1].y) / 2 if f_[0] and b_[0] else 0.05) + 0.006
        bpy.ops.mesh.primitive_torus_add(major_radius=r_, minor_radius=0.011, major_segments=8, minor_segments=3, location=(cx_, y_, 0.42))
        add(bpy.context.object, band)
    return surf
def boots(m="leather", top=0.15):
    # Laced ankle boots over each posed foot (the Lieutenant, owner 2026-11-03: "add boots", then "add more detail"):
    # one smooth boot lofted heel to toe along the foot's own direction (a shaft round the ankle, the instep sloping
    # down to a rounded toe cap), on a welted sole with a heel block, laces across the instep and a seam round the toe
    # cap; the shaft hugs the ankle. In their own node (Boots), out of the body's decimation.
    import bmesh as _bm
    hb = bpy.data.objects["human"]; prev = CUR; module("Boots")
    # sections heel (u 0) to toe (u 1): (u, width, height above the sole); arcs over the foot
    SEC = [(0.0, 0.66, top), (0.1, 0.88, top), (0.28, 0.94, top - 0.012), (0.42, 0.96, 0.08), (0.58, 0.96, 0.064),
           (0.74, 0.92, 0.054), (0.88, 0.78, 0.045), (1.0, 0.36, 0.032)]   # owner: trim, not clown shoes
    K, SOLE = 8, 0.02
    for sx in (-1, 1):
        fp = [v.co for v in hb.data.vertices if v.co.z < 0.05 and v.co.x * sx > 0.02]
        if not fp: continue
        heel = max(fp, key=lambda v: v.y); toe = min(fp, key=lambda v: v.y)
        d = Vector((toe.x - heel.x, toe.y - heel.y, 0)); L = d.length * 0.92; d.normalize(); side = Vector((-d.y, d.x, 0))
        H = Vector((heel.x, heel.y, 0)) - d * 0.004
        W = max(abs((v - H).dot(side)) for v in fp) * 2 * 0.9
        sh_ = H + d * (L * 0.16)
        # the leg's own centre where it enters the boot: the boot is moved so its shaft sits square under it
        g = Vector((sh_.x, sh_.y, top - 0.01)); hits = {}
        for nm, dd in (("x+", Vector((1, 0, 0))), ("x-", Vector((-1, 0, 0))), ("y+", Vector((0, 1, 0))), ("y-", Vector((0, -1, 0)))):
            ok_, at_, *_ = hb.ray_cast(g + dd * 0.12, -dd)
            if ok_: hits[nm] = at_
        if len(hits) == 4:
            lc = Vector(((hits["x+"].x + hits["x-"].x) / 2, (hits["y+"].y + hits["y-"].y) / 2, 0))
            H = H + (lc - Vector((sh_.x, sh_.y, 0))); sh_ = H + d * (L * 0.16)   # the ankle, found by rays at the shaft's top and below it: the shaft hugs it
        rs = []
        for z_ in (top - 0.03, top):
            for dd in (side, -side, d, -d):
                ok_, at_, *_ = hb.ray_cast(Vector((sh_.x, sh_.y, z_)) + dd * 0.1, -dd)
                if ok_: rs.append(abs((at_ - Vector((sh_.x, sh_.y, z_))).dot(dd)))
        ra = min(max(rs) if rs else W * 0.32, W * 0.36) + 0.008
        sec = [(u, min(w, 2 * ra / W) if u < 0.3 else w, h) for u, w, h in SEC]
        verts, faces = [], []
        for u, w, h in sec:
            c = H + d * (u * L); ex = 0.15 if u < 0.3 else 0.8   # the shaft's sides rise straight to its top
            for k in range(K + 1):
                a_ = math.pi * k / K
                verts.append(tuple(c + side * (math.cos(a_) * W * w / 2) + Vector((0, 0, SOLE + (h - SOLE) * math.sin(a_) ** ex))))
        for j in range(len(sec) - 1):
            for k in range(K):
                faces.append((j * (K + 1) + k, j * (K + 1) + k + 1, (j + 1) * (K + 1) + k + 1, (j + 1) * (K + 1) + k))
        for j in (0, len(sec) - 1):   # close heel and toe
            faces.append(tuple(j * (K + 1) + k for k in range(K + 1)))
        me = bpy.data.meshes.new("boot"); me.from_pydata(verts, [], faces)
        bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
        for f in me.polygons: f.use_smooth = True
        o = bpy.data.objects.new("boot", me); scn.collection.objects.link(o); add(o, m)
        # the welted sole, a little proud of the upper all round, and a heel block
        out = [H + d * (u * L) + side * (W * w / 2 + 0.003) for u, w, h in sec] + [H + d * (L + 0.003)] +               [H + d * (u * L) - side * (W * w / 2 + 0.003) for u, w, h in reversed(sec)] + [H - d * 0.003]
        sm = bpy.data.meshes.new("sole"); sm.from_pydata([tuple(q + Vector((0, 0, SOLE))) for q in out], [], [list(range(len(out)))])
        so = bpy.data.objects.new("sole", sm); scn.collection.objects.link(so); sl = so.modifiers.new("sol", "SOLIDIFY"); sl.thickness = SOLE; sl.offset = 1.0 if sm.polygons[0].normal.z < 0 else -1.0
        add(so, "dark")
        box(tuple(H + d * (L * 0.12) + Vector((0, 0, 0.006))), (W * 0.7, L * 0.22, 0.012), "dark", rot=(0, 0, math.atan2(d.y, d.x) + math.pi / 2))
        def top_at(u, k):   # a point on the upper at section position u (interpolated) and arc step k
            for (u0, w0, h0), (u1, w1, h1) in zip(sec, sec[1:]):
                if u0 <= u <= u1:
                    t = (u - u0) / (u1 - u0); w, h = w0 + (w1 - w0) * t, h0 + (h1 - h0) * t; break
            a_ = math.pi * k
            return H + d * (u * L) + side * (math.cos(a_) * W * w / 2) + Vector((0, 0, SOLE + (h - SOLE) * math.sin(a_) ** 0.8 + 0.004))
        for u in (0.3, 0.38, 0.46):   # laces criss-crossed up the instep, eyelets at the ends
            l_, r_ = top_at(u, 0.36), top_at(u + 0.06, 0.64)
            bar(l_, r_, 0.004, "dark", sides=3); bar(top_at(u, 0.64), top_at(u + 0.06, 0.36), 0.004, "dark", sides=3)
        seam = [top_at(0.76, k) for k in (0.05, 0.25, 0.5, 0.75, 0.95)]
        for a2, b2 in zip(seam, seam[1:]): bar(a2, b2, 0.0035, "dark", sides=3)   # the toe cap's seam
    # the body's own feet, hidden in the boots, would poke through them at the heel and toe: removed
    bm = _bm.new(); bm.from_mesh(hb.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z < top - 0.01], context='FACES')
    bm.to_mesh(hb.data); bm.free()
    module(prev)
def ww1_webbing(surf):
    # belt webbing on the WWI uniform: belt, braces over the shoulders, four ammunition pouches, breast pocket flaps,
    # buttons and a bayonet scabbard on the left hip
    human_ = bpy.data.objects["human"]
    fr_, bk_ = surf(0, 1.0), surf(0, 1.0, front=False)
    ok, lx, *_ = human_.ray_cast(Vector((1, 0, 1.0)), Vector((-1, 0, 0)))
    rx = (lx.x if ok else 0.17) + 0.026; ry = (bk_.y - fr_.y) / 2 + 0.014
    bpy.ops.mesh.primitive_torus_add(major_radius=1, minor_radius=0.016, major_segments=12, minor_segments=3, location=(0, (fr_.y + bk_.y) / 2, 1.0))
    o = bpy.context.object
    for v in o.data.vertices:   # the ring stretched round the waist, the strap's own thickness kept
        k = Vector((v.co.x, v.co.y, 0)); n = k.normalized(); v.co = Vector((n.x * rx, n.y * ry, v.co.z)) + (k - n)
    add(o, "webbing")
    for x in (-0.13, -0.065, 0.065, 0.13):
        box(tuple(surf(x, 0.985, out=0.03)), (0.05, 0.035, 0.06), "webbing")
    for x in (-0.09, 0.09):   # braces up the chest, over the shoulder and down the back
        ok, sh_, *_ = human_.ray_cast(Vector((x * 1.25, -0.01, 2.0)), Vector((0, 0, -1)))
        sh_ = (sh_ if ok else Vector((x * 1.25, -0.01, 1.45))) + Vector((0, 0, 0.014))
        bar(surf(x, 1.01, out=0.02), surf(x * 1.1, 1.36, out=0.016), 0.011, "webbing", sides=4)
        bar(surf(x * 1.1, 1.36, out=0.016), sh_, 0.011, "webbing", sides=4)
        bar(sh_, surf(x * 1.1, 1.36, front=False, out=0.016), 0.011, "webbing", sides=4)
        bar(surf(x * 1.1, 1.36, front=False, out=0.016), surf(x, 1.01, front=False, out=0.02), 0.011, "webbing", sides=4)
        box(tuple(surf(x * 0.85, 1.29, out=0.01)), (0.07, 0.012, 0.028), "tunic")   # breast pocket flap
    for z in (1.31, 1.21, 1.11):
        box(tuple(surf(0, z, out=0.008)), (0.016, 0.01, 0.016), "brass")
    bar(Vector((0.17, 0.05, 0.97)), Vector((0.19, 0.1, 0.68)), 0.016, "leather", sides=4)   # bayonet scabbard

# ---- the painted PSX look, shared (2026-11-10, after the Combat Engineer, which keeps its own copy) ----
# The owner wants every figure as a PSX / PS2-era figure (Silent Hill 3, Shenmue): a smooth low-poly body with the
# clothes, straps, buttons, plates, creases and grime painted texel by texel (PAINT), with dark outlines and hard
# shading bands; solid shapes only where the silhouette needs them.
PSX_LIGHT = Vector((-0.35, -0.55, 0.76)).normalized()
def psx_palette(**kw):   # hex colours, lifted a little: the game's light is dim
    return {k: Vector(tuple(min(1.0, int(h[i:i + 2], 16) / 255 * 1.25) for i in (0, 2, 4))) for k, h in kw.items()}
def segd(p, a, b):   # distance from p to segment ab, and how far along it (0..1)
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (p - (a + ab * t)).length, t
def psx_lit(c, n, ao, metal=False, grime=0.0, p=None, mud=None):   # three hard light bands, a hot metal glint, mottling, grime
    from mathutils import noise as _nz
    s = n.dot(PSX_LIGHT)
    k = 1.2 if s > 0.62 else (1.0 if s > 0.0 else 0.74)
    if metal and s > 0.86: k = 1.55
    k *= 0.7 if ao < 0.5 else (0.86 if ao < 0.75 else 1.0)
    if p is not None:
        k *= 1.0 + 0.04 * _nz.noise(p * 34.0)
        if grime > 0 and mud is not None and _nz.noise(p * 9.0 + Vector((5, 5, 5))) > 0.5 - grime: c = c.lerp(mud, 0.55)
    return Vector([min(1.0, v * k) for v in c])
def psx_arm(p):   # (side, distance along the arm 0 shoulder .. 1 elbow .. 2 wrist, distance to its axis) of the nearer arm
    best = None
    for s in ("L", "R"):
        wr = FISTS[s][0] - FISTS[s][1] * 0.045 if s in FISTS else JOINTS[f"wrist.{s}"]
        d1, t1 = segd(p, JOINTS[f"upperarm01.{s}"], JOINTS[f"lowerarm01.{s}"]); d2, t2 = segd(p, JOINTS[f"lowerarm01.{s}"], wr)
        d, t = (d1, t1) if d1 < d2 else (d2, 1 + t2)
        if best is None or d < best[2]: best = (s, t, d)
    return best
def psx_dot(p, pts, C, r=0.0075, m="brass"):   # a rivet or button: ink rim, light top-left
    for q in pts:
        e = p - q; dd = e.length
        if dd < r: return C[m + "l"] if (e.z > 0.002 and e.x < 0) else C[m]
        if dd < r + 0.004: return C["ink"]
    return None
def toeless_feet(m="boots", foot=None):
    # each foot swapped for its convex hull, a toeless boot shape (the boots themselves are painted on). foot(co, side)
    # picks a foot's vertices for a pose whose feet are not flat on the floor (the kneeling Blind Marksman)
    import bmesh as _bm
    human_ = bpy.data.objects["human"]
    if foot is None: foot = lambda co, sx: co.z < 0.1 and co.x * sx > 0
    for sx in (1, -1):
        bm = _bm.new()
        for v in human_.data.vertices:
            if foot(v.co, sx): bm.verts.new(v.co.copy())
        r_ = _bm.ops.convex_hull(bm, input=bm.verts)
        _bm.ops.delete(bm, geom=[g for g in r_["geom_interior"] if isinstance(g, _bm.types.BMVert)], context='VERTS')
        _bm.ops.dissolve_limit(bm, angle_limit=math.radians(8), verts=bm.verts, edges=bm.edges)
        me = bpy.data.meshes.new("boot"); bm.to_mesh(me); bm.free()
        for f in me.polygons: f.use_smooth = True
        o = bpy.data.objects.new("boot", me); scn.collection.objects.link(o); add(o, m)
    bm = _bm.new(); bm.from_mesh(human_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if all(foot(v.co + Vector((0, 0, 0.005)), 1) or foot(v.co + Vector((0, 0, 0.005)), -1) for v in f.verts)], context='FACES')
    bm.to_mesh(human_.data); bm.free()
# colours for the common kit beyond WW1's (the Pilgrims' painted figures, 2026-11-10)
KIT = dict(rope="7e6c48", roped="45391f", gilt="9a7a32", giltd="5e4a1a", giltl="d0b060", boned="7e735e", bonel="d8ccb0",
           blood="5e140d", bloodd="3a0c08", rust="6a4630", rustd="452a1a", rustl="8a5c3a", stone="6e685c", stoned="47423a",
           stonel="8e8878", charge="8a1812", charged="560e0a", chargel="b0281e", mannequin="cfc4b0", mannequind="9a9282",
           parchment="d4c49a", parchmentd="a89870", glow="e0b050")
def psx_blood(C, p, dense=0.0):   # spatters of old blood, thicker with dense (None: clean here)
    from mathutils import noise as _nz
    if _nz.noise(p * 13) > 0.66 - dense: return C["bloodd"] if _nz.noise(p * 40) > 0.3 else C["blood"]
    if _nz.noise(p * 70 + Vector((4, 4, 4))) > 0.72 - dense * 0.5: return C["blood"]
    return None
def hem_line(o, cx=0.0, cy=0.0, bins=48):   # a draped robe's ragged hem: hem(p) -> the hem's height below p, by angle round (cx, cy)
    lo = [9.0] * bins
    for v in o.data.vertices:
        b = int((math.atan2(v.co.x - cx, -(v.co.y - cy)) / (2 * math.pi) + 0.5) * bins) % bins; lo[b] = min(lo[b], v.co.z)
    for _ in range(bins):   # an empty bin (between vertices, or a slit) takes its neighbour's
        lo = [l if l < 9 else min(lo[i - 1], lo[(i + 1) % bins]) for i, l in enumerate(lo)]
    def hem(p):
        f = (math.atan2(p.x - cx, -(p.y - cy)) / (2 * math.pi) + 0.5) * bins - 0.5; i = math.floor(f); t = f - i
        return lo[i % bins] * (1 - t) + lo[(i + 1) % bins] * t
    return hem
def psx_robe(C, k, p, n, ao, hem, blood=0.0, step=24.0, cx=0.0, cy=0.0):
    # a long robe or skirt (colours k, k+"d", k+"l"): long folds round (cx, cy), old blood thickening to the hem, mud
    # caked along it and an ink line at the ragged edge
    from mathutils import noise as _nz
    a = math.degrees(math.atan2(p.x - cx, -(p.y - cy))); h = p.z - hem(p)
    if h < 0.007: return C["ink"]
    if h < 0.03 + 0.02 * _nz.noise(Vector((a * 0.2, 0, 0))): return psx_lit(C[k + "d"].lerp(C["mud"], 0.6), n, ao)
    b = psx_blood(C, p, blood * max(0.0, 1 - h / 0.4))
    if b is not None: return b
    f = (a / step + _nz.noise(Vector((a * 0.03, 0, p.z * 2))) * 0.3) % 1.0
    c = C[k + "d"] if f < 0.12 else (C[k + "l"] if f < 0.2 and n.dot(PSX_LIGHT) > 0.2 else C[k])
    return psx_lit(c, n, ao, grime=0.45 * max(0.0, 1 - h / 0.25), p=p, mud=C["mud"])
def psx_kit(C, base, p, n, ao):
    # the common solid kit painted: wood grain, iron with rust and a glint, brass and gilt, bone, twisted rope, leather;
    # None: not one of these
    from mathutils import noise as _nz
    s = n.dot(PSX_LIGHT)
    def lit(c, metal=False, grime=0.0): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    if base == "wood":
        g = math.sin(_nz.noise(p * Vector((14, 14, 3))) * 7.0)
        return lit(C["woodd"] if g > 0.9 else (C["woodl"] if s > 0.62 else C["wood"]), grime=0.1)
    if base in ("iron", "metal"):
        if _nz.noise(p * 30) > 0.5: return lit(C["rust"] if _nz.noise(p * 80) > 0 else C["rustd"])
        return lit(C["ironl"] if s > 0.7 else (C[base] if s > -0.3 else C["irond"]), metal=True)
    if base in ("brass", "gilt"):
        return lit(C[base + "l"] if s > 0.5 else (C[base] if s > -0.2 else C["giltd"]), metal=True)
    if base == "bone": return lit(C["bonel"] if s > 0.5 else (C["bone"] if s > -0.2 else C["boned"]), grime=0.2)
    if base == "rope": return C["roped"] if ((p.x + p.y) * 60 + p.z * 90) % 1.0 < 0.3 else lit(C["rope"])
    if base == "leather": return lit(C["leatherl"] if s > 0.6 else C["leather"], grime=0.15)
    if base == "dark": return C["ink"]
    return None
def ww1_painted_mask():
    # The WWI gas mask painted on the canvas hood (the Yeoman, then the Shock Trooper): the masked head moved into its own
    # Helmet node (its own texture, room for the eyepieces); only the squat filter drum on the chin is solid. Leaves the
    # Helmet module current. Returns paint(C, base, p, n, ao) for the drum and the hood (None elsewhere); C is the WW1
    # palette with hood, mask, maskd, lens, lensd and lensl.
    fb = FACE_BOX; mc = fb["mid"] - fb["fwd"] * 0.048 - fb["up"] * 0.02   # the mask's centre, as ww1_gas_mask()
    head_ = next(o for o in MODULES["Body"] if o.name.startswith("Sphere") and o.data.materials[0].name == "hood")   # the plain head human_body() made
    module("Helmet")
    MODULES["Body"].remove(head_); MODULES["Helmet"].append(head_)
    # the filter drum screwed onto the chin, angled down
    dn = (fb["fwd"] * 0.85 - fb["up"] * 0.5).normalized(); s0 = mc - fb["up"] * 0.075
    head_.data.update(); bpy.context.view_layer.update()   # its vertices were moved by hand: refresh before ray casting
    hits = [at_ for u_ in (-0.075, -0.06, -0.045, -0.03) for ok_, at_, *_ in [head_.ray_cast(mc + fb["up"] * u_ + fb["fwd"] * 0.4, -fb["fwd"])] if ok_]
    snout = (hits[0] if hits else s0 + fb["fwd"] * 0.06) - fb["fwd"] * 0.012   # set into the face piece, no gap
    bar(snout - dn * 0.03, snout + dn * 0.07, 0.04, "iron", sides=8)
    def paint(C, base, p, n, ao):
        def lit(c, n, ao, metal=False, p=None): return psx_lit(c, n, ao, metal, 0.0, p, C["mud"])
        if base == "iron" and (p - snout).length < 0.1:   # the drum: a screw ring, a perforated face
            t = (p - snout).dot(dn)
            if t > 0.064:
                e = p - (snout + dn * t); return C["ink"] if (e.x * 160) % 1.0 < 0.35 and (e.z * 160) % 1.0 < 0.35 else lit(C["metal"], n, ao, p=p)
            if abs(t - 0.012) < 0.007: return C["ink"] if abs(t - 0.012) > 0.0045 else lit(C["metall"], n, ao, metal=True, p=p)
            return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.6 else C["iron"], n, ao, metal=True, p=p)
        if base == "hood":
            q = p - mc; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
            if f > -0.01:
                for ex in (-0.041, 0.041):
                    dd = math.hypot(a - ex, u - 0.03)
                    if dd < 0.037:
                        if dd > 0.033: return C["ink"]
                        if dd > 0.024: return lit(C["metall"] if u > 0.03 else C["metal"], n, ao, metal=True, p=p)   # the thick rim
                        if dd > 0.021: return C["ink"]
                        if math.hypot(a - ex + 0.008, u - 0.04) < 0.006: return C["lensl"]                          # a glint, no pupil
                        return C["lensd"] if u < 0.022 else C["lens"]
                if math.hypot(a, u + 0.075) < 0.045: return lit(C["metal"], n, ao, metal=True, p=p)                    # the drum's mount
                e = (a / 0.088) ** 2 + ((u + 0.015) / 0.122) ** 2
                if e < 1.0:   # the proofed-cloth face piece: an ink edge, a seam down the middle, creases at the cheeks
                    if e > 0.86: return C["ink"]
                    if abs(a) < 0.004 and u < -0.03: return C["ink"]
                    c = C["maskd"] if math.sin(a * 220 + u * 60) > 0.85 and abs(a) > 0.05 else C["mask"]
                    return lit(c, n, ao, p=p)
            for su in (0.035, -0.055):   # the elastic straps round the back of the head
                if abs(u - su) < 0.011 and f < 0.02: return C["ink"] if abs(u - su) > 0.007 else lit(C["webbing"], n, ao, p=p)
            return lit(C["hood"], n, ao, p=p)
        return None
    return paint
WW1 = dict(tunic="5a5236", tunicd="383321", tunicl="756c4a", breeches="534b31", breechesd="35301f", puttee="4f4a36",
           putteed="302d20", pale="8a7d58", webbing="7e7250", webbingd="4e4632", webbingl="a09369", boot="2a241d",
           bootl="463c30", sole="151310", lace="6a5e48", glove="30261c", glovel="4c3d2e", leather="3e2d1f",
           leatherl="5e4632", brass="7a6533", brassl="a48d52", ink="14120e", mud="3a3125", blood="4a1510", wood="4f3a26",
           woodd="35261a", woodl="6a4e32", metal="3b3c3a", metall="5c5e5c", iron="4b4c4d", ironl="6e7072", irond="2c2d2e",
           cloth="56503e", clothd="38342a", clothl="6e6650", rags="5c5040", ragsd="3a3228", dark="191714", bone="b8ac90",
           tin="4a4c48")
def ww1_paint(C, extra=None, satchel="leather", hips=True):   # hips=False: no satchel, water bottle or scabbard
    # The WWI service dress painted by body region (the Trench Pilgrim and the Yeoman): a tunic with a standing collar,
    # buttoned front, breast and skirt pockets, sleeves with cuffs; belt webbing with four ammunition pouches and braces;
    # baggy breeches with creases, puttees wound up the shin under a pale tape, laced boots, leather gloves; a satchel on
    # the right hip, a water bottle and the bayonet scabbard on the left; the pack and blanket roll on the back, the
    # rifle. extra(base, p, n, ao) paints a figure's own parts first (None: not its part).
    from mathutils import noise as _nz
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    nz_ = NECK.z
    seam = C["tunicd"].lerp(C["ink"], 0.4)   # stitched seams and pocket edges, softer than the ink outlines
    skirts = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    hem = min((v.co.z for o in skirts for v in o.data.vertices), default=0.82)
    def kit(base, p, n, ao):   # the solid kit
        x, y, z = p
        if base == "wood":
            g = math.sin(_nz.noise(p * Vector((14, 14, 3))) * 7.0)
            return lit(C["woodd"] if g > 0.9 else (C["woodl"] if n.dot(PSX_LIGHT) > 0.62 else C["wood"]), n, ao, p=p)
        if base in ("metal", "iron", "dark"):
            return lit(C["metall"] if n.dot(PSX_LIGHT) > 0.7 else C[base if base != "dark" else "metal"], n, ao, metal=True, p=p)
        if base == "cloth" and y > 0.02:   # the pack: a flap with an ink edge, two webbing straps with buckles
            if abs(abs(x) - 0.06) < 0.013 and z < 1.29:
                if abs(abs(x) - 0.06) > 0.009: return C["ink"]
                if abs(z - 1.2) < 0.012: return C["brassl"] if n.dot(PSX_LIGHT) > 0.3 else C["brass"]
                return lit(C["webbing"], n, ao, p=p)
            if abs(z - 1.235) < 0.004 and n.y > 0.5: return C["ink"]
            if abs(abs(x) - 0.1) < 0.004 or z < 1.075: return lit(C["clothd"], n, ao, p=p)   # the seams, the soiled bottom
            return lit(C["clothl"] if z > 1.235 and n.y > 0.5 else C["cloth"], n, ao, grime=0.15, p=p)
        if base == "rags":   # the blanket roll: rolled ends, two straps
            if abs(n.x) > 0.6:
                r = math.hypot(y - 0.075, z - 1.32); return C["ink"] if (r * 160) % 1.0 < 0.3 else lit(C["rags"], n, ao, p=p)
            if abs(abs(x) - 0.08) < 0.012: return C["ink"] if abs(abs(x) - 0.08) > 0.008 else lit(C["webbing"], n, ao, p=p)
            return lit(C["ragsd"] if math.sin(x * 140 + _nz.noise(p * 20)) > 0.7 else C["rags"], n, ao, p=p)
        c = C.get(base, C["cloth"])
        return lit(c, n, ao, base in ("brass",), p=p)
    def paint(m, p, n, ao, tx, ty):
        x, y, z = p.x, p.y, p.z
        base = m.split(".")[0]
        if extra is not None:
            r = extra(base, p, n, ao)
            if r is not None: return r
        if base == "leather" and min((p - FISTS[s][0]).length for s in FISTS) < 0.08:   # gloves: dark leather, a knuckle seam
            s = "L" if (p - FISTS["L"][0]).length < (p - FISTS["R"][0]).length else "R"; fc, fd = FISTS[s]
            along = (p - fc).dot(fd)
            if along < -0.03: return lit(C["leather"], n, ao, p=p)                             # the cuff
            return C["ink"] if abs(along - 0.012) < 0.004 else lit(C["glovel"] if n.dot(PSX_LIGHT) > 0.7 else C["glove"], n, ao, p=p)
        if base not in ("tunic", "breeches", "puttee", "boots"): return kit(base, p, n, ao)
        if z < 0.16:   # the laced ankle boots
            if z < 0.022: return lit(C["sole"], n, ao, p=p)
            if z < 0.03 or z > 0.142: return C["ink"]
            xc = JOINTS["foot.L" if x > 0 else "foot.R"].x
            c = C["boot"]
            if n.y < -0.35 and n.z > 0.15 and abs(x - xc) < 0.02 and z > 0.045:
                fr = (z * 75) % 1.0
                c = C["lace"] if abs(abs(x - xc) - abs(fr - 0.5) * 0.034) < 0.0035 else C["dark"]
            elif n.z > 0.6: c = C["bootl"]
            return lit(c, n, ao, grime=0.2, p=p)
        if base in ("puttee", "breeches") and z < 0.43:   # puttees wound up the shin, the pale tape at the top
            if z > 0.4: return C["ink"] if z > 0.425 or z < 0.404 else lit(C["pale"], n, ao, p=p)
            kx = JOINTS["lowerleg01.L" if x > 0 else "lowerleg01.R"]
            fr = (z * 20 + math.atan2(x - kx.x, y - kx.y) * 0.25) % 1.0
            c = C["ink"] if fr < 0.16 else (C["putteed"] if fr < 0.34 else C["puttee"])
            return lit(c, n, ao, grime=0.3 * (1 - z / 0.43), p=p)
        # the satchel (right hip), water bottle and bayonet scabbard (left), painted where they hang
        if hips and x < -0.1 and n.x < -0.3 and 0.76 < z < 0.95 and -0.08 < y < 0.07:
            if z > 0.935 or z < 0.775 or y < -0.065 or y > 0.055: return C["ink"]
            if z > 0.885: return C["ink"] if z < 0.89 else lit(C[satchel + "l" if satchel + "l" in C else satchel], n, ao, p=p)
            if abs(y + 0.005) < 0.006 and 0.86 < z < 0.885: return C["brass"]
            return lit(C[satchel], n, ao, grime=0.2, p=p)
        if hips and x > 0.1 and n.x > 0.3:
            r = math.hypot(y - 0.0, z - 0.86)
            if r < 0.052:
                if r > 0.046: return C["ink"]
                if z > 0.895 and abs(y) < 0.01: return C["metall"]
                return lit(C["clothd"] if (y + z * 0.3) * 60 % 1.0 < 0.15 else C["cloth"], n, ao, p=p)   # the cloth-covered bottle
            d_, t_ = segd(Vector((0, y, z)), Vector((0, 0.06, 0.97)), Vector((0, 0.11, 0.66)))
            if d_ < 0.017 and y > 0.0:
                if d_ > 0.013: return C["ink"]
                return lit(C["metall"] if t_ > 0.9 else C["leather"], n, ao, metal=t_ > 0.9, p=p)
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.075 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0
        if base in ("breeches", "puttee") or (z < hem + 0.004 and not on_arm):   # baggy breeches, creased
            w = math.sin(z * 38 + x * 25 + _nz.noise(p * 9) * 2.5) * (0.6 + 0.4 * _nz.noise(p * 4 + Vector((2, 0, 0))))
            c = C["breechesd"] if w > 0.72 else C["breeches"]
            if 0.62 < w <= 0.72: c = C["breeches"].lerp(C["breechesd"], 0.5)
            if z < 0.5 and n.y < -0.4: c = c.lerp(C["mud"], 0.35)   # muddy knees
            return lit(c, n, ao, grime=0.2 * max(0.0, (0.6 - z) / 0.2), p=p)
        if on_arm:   # sleeves: a shoulder seam, creases at the elbow, a cuff
            if along > 1.82: return C["ink"] if along < 1.85 else lit(C["tunicd"], n, ao, p=p)
            if abs(along - 0.15) < 0.025 and ad > 0.045: return seam
            c = C["tunicd"] if 0.82 < along < 1.25 and math.sin(along * 60 + ad * 80) > 0.8 else C["tunic"]
            return lit(c, n, ao, p=p)
        front = y < 0
        if abs(abs(x) - (0.09 + (z - 1.0) * 0.03)) < 0.0135 and 1.0 < z:   # the braces, up the chest, over the shoulder, down the back
            return C["ink"] if abs(abs(x) - (0.09 + (z - 1.0) * 0.03)) > 0.0085 else lit(C["webbing"], n, ao, p=p)
        if front and 0.955 < z < 1.045 and (0.04 < abs(x) < 0.09 or 0.105 < abs(x) < 0.155):   # four ammunition pouches
            u = abs(x) - (0.065 if abs(x) < 0.1 else 0.13)
            if abs(u) > 0.018 or z < 0.962 or z > 1.038: return C["ink"]
            if abs(z - 1.018) < 0.0035: return C["ink"]
            if abs(u) < 0.005 and abs(z - 1.01) < 0.005: return C["brassl"]
            return lit(C["webbingl"] if z > 1.018 else C["webbing"], n, ao, p=p)
        if 0.97 < z < 1.03:   # the belt and its brass clasp
            if z < 0.975 or z > 1.025: return C["ink"]
            if front and abs(x) < 0.03: return C["ink"] if abs(x) > 0.025 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.4 else C["brass"])
            return lit(C["webbing"], n, ao, p=p)
        if z < 0.97:   # the tunic's skirt: front edge, buttons, pockets, hem
            if z < hem + 0.012: return C["ink"]
            if front and abs(x) < 0.005: return seam
            if front and 0.035 < abs(x) < 0.14 and 0.85 < z < 0.955:
                if abs(abs(x) - 0.0875) > 0.045 or z < 0.858: return seam
                if 0.919 < z < 0.926: return seam
                if z > 0.925: return lit(C["tunicl"], n, ao, p=p)
            f = (math.degrees(math.atan2(x, -y)) / 26.0 + _nz.noise(Vector((x * 3, y * 3, z * 3))) * 0.4) % 1.0   # long folds
            c = C["tunicd"] if f < 0.1 else (C["tunicl"] if f < 0.15 else C["tunic"])
            return lit(c, n, ao, grime=0.25 * max(0.0, (0.9 - z) / 0.08), p=p)
        if z > nz_ - 0.035:   # the standing collar, a brass badge each side
            if z < nz_ - 0.029: return C["ink"]
            if front and abs(abs(x) - 0.045) < 0.008: return C["brass"]
            return lit(C["tunicd"] if n.dot(PSX_LIGHT) < 0.4 else C["tunic"], n, ao, p=p)
        if front:
            b = psx_dot(p, [Vector((0, y, bz)) for bz in (1.32, 1.22, 1.12)] + [Vector((sx * 0.085, y, 1.285)) for sx in (-1, 1)], C, 0.007)
            if b is not None: return b
            if abs(x) < 0.005: return seam                                                        # the buttoned front
            if 0.045 < abs(x) < 0.125 and 1.18 < z < 1.31:                                      # breast pockets, flapped
                if abs(abs(x) - 0.085) > 0.033 or z > 1.303 or z < 1.187: return seam
                if abs(z - 1.268) < 0.0035: return C["ink"]
                if abs(abs(x) - 0.085) < 0.003 and z < 1.268: return C["tunicd"]                 # the box pleat
                return lit(C["tunicl"] if z > 1.268 else C["tunic"], n, ao, p=p)
        if not front and abs(x) < 0.004: return seam                                         # the back seam
        return lit(C["tunic"], n, ao, p=p)
    return paint

def trench_pilgrim():
    # Trench Pilgrim: the template infantry figure of docs/art-pipeline.md (2026-11-03). An original figure in the
    # setting's genre, not a copy of any official miniature or artwork. The owner's change (2026-11-03): a WWI trench
    # soldier rather than a robed pilgrim, keeping only the capirote in place of a helmet. Painted PSX look
    # (2026-11-10, as the Combat Engineer): a smooth low-poly body in the WWI service dress, its short tunic skirt
    # draped, toeless boot hulls, and the tunic, webbing (belt, braces, four pouches), pockets, buttons, breeches,
    # puttees with a pale tape, laced boots, gloves, satchel, water bottle and bayonet scabbard painted (ww1_paint).
    # Solid only for the silhouette: Helmet (the capirote: a sackcloth cone on an iron base with a painted blood cross,
    # rivets and a prayer strip under a wax seal), Back (pack with a rolled blanket), Weapon (bolt-action rifle with
    # fixed bayonet, at port arms), Accent (the capirote band, seat colour).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 900; TEX_SIZES = {"Body": 256, "Helmet": 128, "Weapon": 128, "Back": 128, "Hip": 128}
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.06, 1.4))
        human.reach(r, "R", P(-0.24, 0.0, 1.06), P(-0.08, -0.24, 0.98))
        human.reach(r, "L", P(0.25, -0.12, 1.12), P(0.1, -0.3, 1.25))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}, head=None, mitts="leather",
                                shape="male-young-averagemuscle-minweight")   # lean, under a capirote that hides the head
    ww1_uniform(bands=False)   # the loose wool and the tunic's skirt; the puttee tapes are painted
    toeless_feet()
    module("Helmet")
    # the capirote (the owner's choice, 2026-11-03): a tall stiff cone of sackcloth on a rigid iron casing over the
    # head and neck (owner: "metallic"), flaring to a rim; a seat-colour band round the foot of the cone
    c = Vector((hp.x, hp.y - 0.005, top.z))
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.075, radius2=0.0, depth=0.46, location=tuple(c + Vector((0, 0, 0.17))))
    o = bpy.context.object; o.scale = (1, 1.15, 1); add(o, "hood")
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.125, radius2=0.08, depth=0.22, location=tuple(c + Vector((0, 0, -0.17))))
    o = bpy.context.object; o.scale = (1, 1.15, 1); add(o, "iron")
    bpy.ops.mesh.primitive_torus_add(major_radius=0.079, minor_radius=0.011, major_segments=10, minor_segments=3, location=tuple(c + Vector((0, 0, -0.055))))
    bpy.context.object.scale = (1, 1.15, 1); add(bpy.context.object, "accent")
    # detail pass (2026-11-10): two scripture strips hung from wax seals down the casing's front right, lying on its slope
    STRIPS = []
    for ang_ in (-40, -64):
        u_ = Vector((-math.sin(math.radians(-ang_)), -math.cos(math.radians(-ang_)) * 1.15, 0)).normalized()
        t_ = Vector((-u_.y, u_.x, 0)); ell = 1 / math.hypot(u_.x, u_.y / 1.15)   # the casing is 1.15 times deeper than wide
        a_, b_ = c + u_ * (0.088 * ell + 0.005) + Vector((0, 0, -0.1)), c + u_ * (0.133 * ell + 0.004) + Vector((0, 0, -0.3 + (ang_ + 40) * 0.0012))
        me = bpy.data.meshes.new("strip"); me.from_pydata([tuple(a_ - t_ * 0.014), tuple(a_ + t_ * 0.014), tuple(b_ + t_ * 0.013), tuple(b_ - t_ * 0.013)], [], [(0, 1, 2, 3)])
        o = bpy.data.objects.new("strip", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.003; add(o, "parchment")
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.017, depth=0.01, location=tuple(a_ + u_ * 0.005))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(u_); add(o, "charge")
        STRIPS.append((a_, b_, u_))
    module("Back")
    # a canvas pack between the shoulders with a blanket rolled across the top
    box((0, 0.075, 1.18), (0.2, 0.09, 0.22), "cloth")
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.04, depth=0.26, location=(0, 0.075, 1.32), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "rags")
    TIN = Vector((0.0, 0.132, 1.135))
    box(tuple(TIN), (0.11, 0.022, 0.075), "metal")                                               # the mess tin strapped on the pack
    module("Weapon")
    hr, hl = hands["R"] + Vector((0, -0.04, -0.02)), hands["L"] + Vector((0, -0.04, -0.02))
    d = (hl - hr).normalized(); butt, muz = hr - d*0.24, hl + d*0.42
    side = d.cross(Vector((0, -1, 0))).normalized()
    bar(butt, hr + d*0.22, 0.028, "wood", sides=6); bar(hr + d*0.22, muz - d*0.1, 0.02, "wood", sides=6)
    bar(hr + d*0.08, muz, 0.01, "metal", sides=4)
    bar(hr + d*0.12, hr + d*0.12 + side*0.05 - Vector((0, 0.02, 0)), 0.008, "metal", sides=3)    # bolt handle
    bar(hr + d*0.02, hr + d*0.14, 0.016, "metal", sides=4)                                       # magazine and trigger guard
    bar(muz, muz + d*0.22, 0.006, "iron", sides=3)                                               # bayonet
    sl0, sl1 = butt + d * 0.05 - side * 0.026, hl + d * 0.12 - side * 0.02                      # the sling, sagging under the rifle
    slm = (sl0 + sl1) / 2 - Vector((0, 0, 0.05))
    bar(sl0, slm, 0.006, "leather", sides=3); bar(slm, sl1, 0.006, "leather", sides=3)
    module("Hip")
    skirts_ = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    human_ = bpy.data.objects["human"]
    def surf(o_, d_):   # the outermost point of the body or the tunic skirt seen from o_ along d_
        best = None
        for ob in (human_, *skirts_):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * 0.012
    ROS = surf(Vector((0.125, -1.0, 0.95)), Vector((0, 1, 0)))                                   # a wooden rosary cross on a cord from the belt
    RX = ROS + Vector((0.0, -0.01, -0.11))
    bar(ROS + Vector((0, 0, 0.03)), RX + Vector((0, 0, 0.02)), 0.004, "rope", sides=3)
    bar(RX + Vector((0, 0, 0.025)), RX - Vector((0, 0, 0.035)), 0.007, "wood", sides=4)
    bar(RX - Vector((0.018, 0, 0.0)), RX + Vector((0.018, 0, 0.0)), 0.007, "wood", sides=4)
    KN = surf(Vector((-1.0, 0.0, 0.37)), Vector((1, 0, 0)))                                      # a trench knife pushed into the right puttee
    bar(KN + Vector((0, 0, -0.01)), KN + Vector((0, 0, 0.07)), 0.009, "wood", sides=4)
    bar(KN + Vector((0, -0.016, 0.0)), KN + Vector((0, 0.016, 0.0)), 0.005, "iron", sides=3)
    module("Body")
    C = psx_palette(**{**KIT, **WW1, **dict(hood="5e5646", hoodd="3c362a", hoodl="7a705a", rust="5a3a24")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    # detail pass (2026-11-10): landmarks for the painted shoulder straps, the pilgrim's sleeve badge and a mended knee
    ok_, PATCH, *_ = human_.ray_cast(JOINTS["upperarm01.L"] + (JOINTS["lowerarm01.L"] - JOINTS["upperarm01.L"]) * 0.38 + Vector((0.6, 0, 0)), Vector((-1, 0, 0)))
    if not ok_: PATCH = Vector((9, 9, 9))
    ok_, MEND, *_ = human_.ray_cast(Vector((-0.095, -1.0, 0.6)), Vector((0, 1, 0)))
    if not ok_: MEND = Vector((9, 9, 9))
    def detail(base, p, n, ao):   # the new kit and the painted details over the uniform (None: not here)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False, grime=0.0): return lit(c_, n, ao, metal, grime, p)
        if base == "parchment":   # the scripture strips: paper with rows of writing, a darker torn foot
            a_, b_, u_ = min(STRIPS, key=lambda s: segd(p, s[0], s[1])[0]); ax_ = (b_ - a_).normalized(); t = (p - a_).dot(ax_)
            ac = (p - a_).dot(Vector((-u_.y, u_.x, 0)))
            if abs(ac) > 0.011 or t > (b_ - a_).length - 0.014: return C["parchmentd"]
            if t > 0.035 and (t * 30) % 1.0 < 0.34 and abs(ac) < 0.0075: return C["ink"].lerp(C["parchmentd"], 0.4)   # lines of script
            return lt(C["parchmentd"].lerp(C["parchment"], 0.6))
        if base == "charge":   # the wax seals, a light top
            return C["charged"] if s_ > 0.55 else C["bloodd"]
        if base == "metal" and (p - TIN).length < 0.09:   # the mess tin, a strap round it
            q = p - TIN
            if abs(q.x) < 0.012: return C["ink"] if abs(q.x) > 0.008 else lt(C["webbing"])
            return lt(C["tin"] if s_ > 0.5 else C["metal"], metal=True)
        if base in ("wood", "metal", "iron") and (p - hr).length < 0.75 and segd(p, butt, muz + d * 0.22)[0] < 0.06:   # the rifle
            t = (p - hr).dot(d)
            if base == "wood":
                if (p - butt).dot(d) < 0.014: return C["brassl"] if s_ > 0.3 else C["brass"]                     # the butt plate
                for tb in (0.45, (muz - hr).dot(d) - 0.14):                                                   # barrel bands
                    if abs(t - tb) < 0.011: return C["ink"] if abs(t - tb) > 0.008 else lt(C["metall"], metal=True)
                return psx_kit(C, base, p, n, ao)
            return None
        if base == "leather" and segd(p, sl0, slm)[0] < 0.014 or base == "leather" and segd(p, slm, sl1)[0] < 0.014:   # the sling
            return lt(C["leatherl"] if s_ > 0.5 else C["leather"])
        if base in ("rope", "wood") and (p - RX).length < 0.16:   # the rosary
            return psx_kit(C, base, p, n, ao)
        if base in ("wood", "iron") and (p - KN).length < 0.1:   # the trench knife
            return psx_kit(C, base, p, n, ao)
        if base == "tunic":
            q = p - PATCH
            if n.x > 0.2 and abs(q.y) < 0.03 and abs(q.z) < 0.03:   # the pilgrim's badge: a red cross on bone, stitched on
                if max(abs(q.y), abs(q.z)) > 0.024: return C["ink"]
                if min(abs(q.y), abs(q.z)) < 0.0065 and max(abs(q.y), abs(q.z)) < 0.018: return C["charged"]
                return lt(C["bone"])
            for sx in (1, -1):   # shoulder straps from the collar out to the shoulder, a brass button at the collar end
                sh = JOINTS["upperarm01.L" if sx > 0 else "upperarm01.R"]
                if n.z > 0.4 and z > sh.z - 0.03 and abs(x) > 0.05:
                    dd, tt = segd(Vector((x, y, 0)), Vector((sx * 0.06, sh.y, 0)), Vector((sh.x, sh.y, 0)))
                    if dd < 0.019:
                        if dd > 0.014 or tt > 0.97: return C["ink"]
                        if tt < 0.22 and dd < 0.009: return C["brassl"] if s_ > 0.6 else C["brass"]
                        return lt(C["tunicl"] if s_ > 0.5 else C["tunic"])
        if base in ("breeches", "tunic") and z < 1.0:
            q = p - MEND; qa, qz = q.x * 0.98 - q.z * 0.2, q.z * 0.98 + q.x * 0.2   # a mended patch on the right knee, set askew
            if n.y < -0.3 and abs(qa) < 0.032 and abs(qz) < 0.028:
                if abs(qa) > 0.025 or abs(qz) > 0.021: return C["ink"]
                return lt(C["rags"])
            if _nz.noise(p * 10 + Vector((2, 7, 1))) > 0.6: return C["bloodd"] if _nz.noise(p * 25) > 0.25 else C["blood"]   # old blood, in blots
        return None
    def capirote(base, p, n, ao):
        q = p - c
        if base not in ("hood", "iron") or q.to_2d().length > 0.17: return None
        a = math.degrees(math.atan2(q.x, -q.y))   # 0 at the front, + to his left
        if base == "hood":   # coarse sackcloth: a stitched seam down the back, stains, grime towards the foot
            if abs(abs(a) - 180) < 9:
                return C["ink"] if abs(abs(a) - 180) > 6 or (q.z * 90) % 1.0 < 0.35 else lit(C["hoodd"], n, ao, p=p)
            c_ = C["hoodd"] if (q.z * 70 + _nz.noise(p * 20) * 2) % 1.0 < 0.12 else C["hood"]
            if _nz.noise(p * 14 + Vector((3, 1, 2))) > 0.45: c_ = c_.lerp(C["mud"], 0.45)
            return lit(C["hoodl"] if n.dot(PSX_LIGHT) > 0.62 and c_ is C["hood"] else c_, n, ao, p=p)
        if n.z < -0.9: return C["dark"]
        # the iron casing: a riveted rim, riveted seams, rust; the blood cross daubed on the front, the prayer strip
        if q.z < -0.258:
            if q.z > -0.264: return C["ink"]
            for k in range(6):
                if abs(math.remainder(a - (30 + 60 * k), 360)) < 3.2 and abs(q.z + 0.271) < 0.005: return C["brassl"] if q.z > -0.271 else C["brass"]
            return lit(C["irond"], n, ao, metal=True, p=p)
        wob = _nz.noise(p * 60) * 0.004
        if q.y < 0 and ((abs(q.x + wob) < 0.012 and -0.215 < q.z < -0.085) or (abs(q.x) < 0.042 and abs(q.z + 0.125 + wob) < 0.011)):
            return C["blood"] * (0.8 if _nz.noise(p * 80) > 0.3 else 1.0)
        if q.y < 0 and -0.245 < q.z < -0.2 and any(abs(q.x - dx) < 0.003 for dx in (-0.007, 0.004, 0.009)) and q.z > -0.2 - (0.03 + 0.012 * math.sin(q.x * 700)): return C["blood"]   # drips
        if q.x > 0.05 and n.x > 0.4 and abs(q.y + 0.025) < 0.02 and -0.2 < q.z < -0.07:   # the prayer strip, its wax seal
            if math.hypot(q.y + 0.025, q.z + 0.085) < 0.014: return C["blood"] if math.hypot(q.y + 0.025, q.z + 0.085) < 0.011 else C["ink"]
            if abs(q.y + 0.025) > 0.016 or q.z < -0.196: return C["ink"]
            if (q.z * 110) % 1.0 < 0.3 and abs(q.y + 0.025) < 0.011 and _nz.noise(p * 200) > -0.2: return C["ink"]   # scripture
            return lit(C["bone"], n, ao, p=p)
        for sa in (-62, 62, 180):   # riveted seams
            if abs(math.remainder(a - sa, 360)) < 1.6: return C["ink"]
            if abs(math.remainder(a - sa - 4, 360)) < 2.6 and (q.z * 40) % 1.0 < 0.25: return C["brass"]
        rs = _nz.noise(p * 18 + Vector((7, 0, 0))) + max(0.0, -0.2 - q.z) * 3   # rust, heavier towards the rim
        c_ = C["rust"] if rs > 0.45 else (C["ironl"] if n.dot(PSX_LIGHT) > 0.75 else C["iron"])
        return lit(c_, n, ao, p=p)
    PAINT = ww1_paint(C, extra=lambda base, p, n, ao: capirote(base, p, n, ao) or detail(base, p, n, ao))

def yeoman():
    # Yeoman (2026-11-03, first figure of the Principality, class A, 25 mm): an original WWI line infantryman in the
    # setting's genre, on the owner's WWI uniform references, not a copy of any miniature or artwork. Painted PSX look
    # (2026-11-10, as the Combat Engineer): a smooth low-poly body in the WWI service dress (ww1_paint: tunic, webbing,
    # breeches, puttees, boots, gloves, haversack, water bottle, scabbard, the respirator bag on the chest and the
    # entrenching tool on the back), its short tunic skirt draped, toeless boot hulls. The WWI gas mask is painted on the
    # canvas hood (the face piece, two big metal-rimmed eyepieces with fogged blank lenses, the straps round the back);
    # only its squat filter drum on the chin is solid (Helmet). The steel helmet painted in the seat colour (Accent);
    # advancing, the rifle levelled at the hip with its bayonet fixed (Weapon); the pack and blanket roll (Back).
    # Detail pass (2026-11-10): the respirator bag and the entrenching tool made solid, a water bottle, bayonet scabbard,
    # rifle sling and mess tin added (Hip, Weapon, Back); chevron, battalion patch, shoulder straps and mud painted.
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 820; TEX_SIZES = {"Body": 256, "Helmet": 128, "Weapon": 128, "Back": 128, "Hip": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.1, 1.4))                                          # leaning into the advance
        human.aim(r, "upperleg01.L", P(0.11, -0.14, 0.5)); human.aim(r, "upperleg02.L", P(0.11, -0.14, 0.5))
        human.aim(r, "lowerleg01.L", P(0.12, -0.12, 0.08)); human.aim(r, "lowerleg02.L", P(0.12, -0.12, 0.08))
        human.aim(r, "foot.L", P(0.12, -0.28, -0.01))
        human.aim(r, "upperleg01.R", P(-0.1, 0.06, 0.5)); human.aim(r, "upperleg02.R", P(-0.1, 0.06, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.11, 0.16, 0.1)); human.aim(r, "lowerleg02.R", P(-0.11, 0.16, 0.1))
        human.aim(r, "foot.R", P(-0.11, 0.02, -0.02))
        human.reach(r, "R", P(-0.22, 0.06, 1.0), P(-0.12, -0.12, 0.93))                  # right fist on the grip at the hip
        human.reach(r, "L", P(0.2, -0.2, 1.08), P(0.05, -0.42, 1.03))                    # left hand forward under the stock
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}, head="hood",
                                mitts="leather", shape="male-young-averagemuscle-minweight")
    ww1_uniform(bands=False)   # the loose wool and the tunic's skirt; the puttee tapes are painted
    toeless_feet()
    mask = ww1_painted_mask()   # leaves the Helmet module current
    # the steel helmet: a shallow dome with a wide flat brim, painted in the seat colour so the camera above sees it
    hc = Vector((hp.x, hp.y - 0.012, top.z - 0.075))
    sphere(tuple(hc + Vector((0, 0.0, 0.03))), 0.125, "accent", (1, 1.08, 0.72), seg=10, rings=6)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.165, radius2=0.12, depth=0.025, location=tuple(hc + Vector((0, 0, 0.03))))
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "accent")
    module("Back")
    box((0, 0.075, 1.18), (0.2, 0.09, 0.22), "cloth")                                    # pack, blanket roll on top
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.04, depth=0.26, location=(0, 0.075, 1.32), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "rags")
    # detail pass (2026-11-10): a mess tin strapped to the pack's back
    TIN = Vector((0.0, 0.13, 1.14))
    box(tuple(TIN), (0.1, 0.02, 0.07), "metal")
    module("Weapon")
    # the rifle levelled at the hip: the butt back under the right forearm, the bayonet pointing ahead
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    d = (hl - hr).normalized(); butt, muz = hr - d * 0.26, hl + d * 0.32
    side = d.cross(Vector((0, 0, 1))).normalized()
    bar(butt, hr + d * 0.2, 0.028, "wood", sides=6); bar(hr + d * 0.2, muz - d * 0.1, 0.02, "wood", sides=6)
    bar(hr + d * 0.06, muz, 0.01, "metal", sides=4)
    bar(hr + d * 0.1, hr + d * 0.1 + side * 0.05 + Vector((0, 0, 0.01)), 0.008, "metal", sides=3)   # bolt handle
    bar(hr, hr + d * 0.12 - Vector((0, 0, 0.03)), 0.016, "metal", sides=4)                           # magazine and trigger guard
    bar(muz, muz + d * 0.22, 0.006, "iron", sides=3)                                                  # bayonet
    sl0, sl1 = butt + d * 0.06 - Vector((0, 0, 0.026)), muz - d * 0.14 - Vector((0, 0, 0.02))      # the sling, sagging under the rifle
    slm = (sl0 + sl1) / 2 - Vector((0, 0, 0.06))
    bar(sl0, slm, 0.006, "leather", sides=3); bar(slm, sl1, 0.006, "leather", sides=3)
    module("Hip")
    skirts_ = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    human_ = bpy.data.objects["human"]
    def surf(o_, d_, out=0.012):   # the outermost point of the body or the tunic skirt seen from o_ along d_
        best = None
        for ob in (human_, *skirts_):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * out
    CANT = surf(Vector((1.0, 0.0, 0.86)), Vector((-1, 0, 0)), 0.0) + Vector((0.02, 0, 0))   # the felt-covered water bottle on the left hip
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.05, depth=0.036, location=tuple(CANT), rotation=(0, math.pi / 2, 0))
    o = bpy.context.object; o.scale = (1, 0.82, 1); add(o, "cloth")
    bar(CANT + Vector((0, 0, 0.036)), CANT + Vector((0, 0, 0.058)), 0.01, "brass", sides=5)          # its cork and cap
    SC0 = surf(Vector((1.0, 0.065, 0.95)), Vector((-1, 0, 0)), 0.016); SC1 = SC0 + Vector((0.008, 0.05, -0.25))
    bar(SC0, SC1, 0.012, "leather", sides=4)                                                           # the bayonet scabbard behind it
    bar(SC1, SC1 + (SC1 - SC0).normalized() * 0.03, 0.009, "brass", sides=4)                         # its chape
    RESP = surf(Vector((0.05, -1.0, 1.215)), Vector((0, 1, 0)), 0.0) + Vector((0, -0.018, 0))       # the respirator bag on the chest, solid now
    box(tuple(RESP), (0.11, 0.04, 0.09), "cloth", rot=(0.2, 0, 0))
    ET = surf(Vector((0.06, 1.0, 0.85)), Vector((0, -1, 0)), 0.0) + Vector((0, 0.012, 0))         # the entrenching tool hung on the belt behind, solid now
    box(tuple(ET), (0.075, 0.014, 0.085), "iron")
    bar(ET + Vector((0, 0.004, 0.04)), ET + Vector((0, 0.01, 0.17)), 0.011, "wood", sides=4)
    module("Body")
    C = psx_palette(**{**WW1, **dict(hood="5e5646", hoodd="3c362a", mask="565a44", maskd="383b2c", lens="b8b090", lensd="8a8468", lensl="e0dcc0",
                                     stripe="a89a74", stripei="5a1e16", tinl="70726c", rust="5a3a24")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    # detail pass (2026-11-10): landmarks for the painted sleeve chevrons and the unit patch
    ok_, CHEV, *_ = human_.ray_cast(JOINTS["upperarm01.L"] + (JOINTS["lowerarm01.L"] - JOINTS["upperarm01.L"]) * 0.55 + Vector((0.6, 0, 0)), Vector((-1, 0, 0)))
    if not ok_: CHEV = Vector((9, 9, 9))
    ok_, UNIT, *_ = human_.ray_cast(JOINTS["upperarm01.R"] + (JOINTS["lowerarm01.R"] - JOINTS["upperarm01.R"]) * 0.2 + Vector((-0.6, 0, 0)), Vector((1, 0, 0)))
    if not ok_: UNIT = Vector((9, 9, 9))
    def detail(base, p, n, ao):   # the new kit and the painted details over the uniform (None: not here)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False, grime=0.0): return lit(c_, n, ao, metal, grime, p)
        if base == "cloth" and (p - CANT).length < 0.075:   # the water bottle: felt cover, a stitched seam round it, a strap
            q = p - CANT; r_ = math.hypot(q.y / 0.82, q.z)
            if abs(q.y) < 0.006 and q.z > 0: return C["ink"] if abs(q.y) > 0.0035 else lt(C["leather"])   # the carrier strap
            if abs(n.x) > 0.6:
                if r_ > 0.046: return C["ink"]
                if r_ > 0.04: return C["clothd"]
                return lt(C["clothl"] if s_ > 0.5 else C["cloth"])
            if abs(q.x) < 0.003: return C["clothd"]
            return lt(C["clothl"] if s_ > 0.62 else C["cloth"], grime=0.2)
        if base == "brass" and (p - CANT).length < 0.09: return C["brassl"] if s_ > 0.4 else C["brass"]
        if base in ("iron", "wood") and (p - ET).length < 0.2 and p.y > 0.03:   # the entrenching tool: a worn bright edge, rivets, a webbing carrier
            q = p - ET
            if base == "wood": return C["ink"] if abs(q.z - 0.1) < 0.004 else psx_kit(C, "wood", p, n, ao)
            if abs(q.x) > 0.033 or abs(q.z) > 0.038: return C["ink"]
            if q.z < -0.03 or abs(q.x) > 0.028: return C["ironl"]
            if abs(q.z - 0.012) < 0.012 and abs(q.x) < 0.034:   # the carrier's strap over the blade
                return C["ink"] if abs(q.z - 0.012) > 0.009 else lt(C["webbing"])
            r_ = psx_dot(p, [ET + Vector((sx * 0.016, q.y, 0.03)) for sx in (-1, 1)], C, 0.004, "metal")
            if r_ is not None: return r_
            if _nz.noise(p * 30) > 0.4: return lt(C["rust"] if "rust" in C else C["leather"])
            return lt(C["iron"], metal=True)
        if base == "cloth" and (p - RESP).length < 0.09:   # the respirator bag: flap with a brass stud, stitched edges, a stencilled unit mark
            q = p - RESP; ca, sa = math.cos(0.2), math.sin(0.2)
            q = Vector((q.x, q.y * ca + q.z * sa, -q.y * sa + q.z * ca))   # into the bag's own (tilted) frame
            e1 = sum(1 for k, h in enumerate((0.055, 0.02, 0.045)) if h - abs(q[k]) < 0.005)
            if e1 >= 2: return C["ink"]
            if q.y < 0:
                if abs(q.z - 0.012) < 0.004: return C["ink"]                                            # the flap's edge
                if math.hypot(q.x, q.z - 0.004) < 0.007: return C["brassl"] if q.z > 0.004 else C["brass"]
                if q.z < -0.006 and abs(q.x + 0.025) < 0.012 and abs(q.z + 0.025) < 0.009: return C["stripei"]   # a red unit square
                if 0.055 - abs(q.x) < 0.01 or q.z < -0.035: return lt(C["clothd"])                      # the gusset seams
            return lt(C["clothl"] if q.z > 0.012 and s_ > 0.2 else C["cloth"], grime=0.15)
        if base in ("leather", "brass") and segd(p, SC0, SC1 + (SC1 - SC0).normalized() * 0.03)[0] < 0.03 and (p - hr).length > 0.12:   # the scabbard: a frog loop, a brass chape
            t = segd(p, SC0, SC1)[1]
            if base == "brass": return C["brassl"] if s_ > 0.3 else C["brass"]
            if abs(t - 0.12) < 0.05: return C["ink"] if abs(t - 0.12) > 0.035 else lt(C["webbing"])
            return lt(C["leatherl"] if s_ > 0.55 else C["leather"])
        if base == "metal" and (p - TIN).length < 0.09:   # the mess tin, a webbing strap round it
            q = p - TIN
            if abs(q.x) < 0.012: return C["ink"] if abs(q.x) > 0.008 else lt(C["webbing"])
            if max(abs(q.x) - 0.05, abs(q.z) - 0.035) > -0.004: return C["ink"]
            return lt(C["tinl"] if s_ > 0.5 else C["tin"], metal=True)
        if base in ("wood", "metal", "iron", "leather") and segd(p, butt, muz + d * 0.22)[0] < 0.1 and (p - hr).length < 0.8:   # the rifle
            t = (p - hr).dot(d)
            if base == "leather": return lt(C["leatherl"] if s_ > 0.5 else C["leather"])   # the sling
            if base == "wood":
                if (p - butt).dot(d) < 0.016: return C["brassl"] if s_ > 0.3 else C["brass"]                   # the butt plate
                for tb in (0.4, (muz - hr).dot(d) - 0.13):                                                  # barrel bands
                    if abs(t - tb) < 0.011: return C["ink"] if abs(t - tb) > 0.008 else lt(C["metall"], metal=True)
                if abs(t + 0.13) < 0.004: return C["woodd"]                                                 # the stock's joint
                return psx_kit(C, base, p, n, ao)
            if base == "metal" and t < 0.13: return C["ink"] if abs(t - 0.06) < 0.004 else lt(C["metall"] if s_ > 0.4 else C["metal"], metal=True)
            return None
        if base == "tunic":
            q = p - CHEV
            if n.x > 0.2 and abs(q.y) < 0.034 and -0.03 < q.z < 0.02:   # a lance-corporal's chevron on the left sleeve, worsted tape
                v_ = q.z + abs(q.y) * 0.7
                if abs(v_ + 0.004) < 0.0085: return C["ink"] if abs(v_ + 0.004) > 0.0055 else lt(C["stripe"])
            q = p - UNIT
            if n.x < -0.2 and abs(q.y) + abs(q.z) < 0.03:   # the battalion's patch on the right shoulder: a red diamond
                return C["ink"] if abs(q.y) + abs(q.z) > 0.023 else lt(C["stripei"])
            for sx in (1, -1):   # shoulder straps from the collar out to the shoulder, a brass button at the collar end
                sh = JOINTS["upperarm01.L" if sx > 0 else "upperarm01.R"]
                if n.z > 0.4 and z > sh.z - 0.03 and abs(x) > 0.05:
                    dd, tt = segd(Vector((x, y, 0)), Vector((sx * 0.06, sh.y, 0)), Vector((sh.x, sh.y, 0)))
                    if dd < 0.019:
                        if dd > 0.014 or tt > 0.97: return C["ink"]
                        if tt < 0.22 and dd < 0.009: return C["brassl"] if s_ > 0.6 else C["brass"]
                        return lt(C["tunicl"] if s_ > 0.5 else C["tunic"])
            if y < 0 and 0.972 < z < 1.028 and 0.03 < abs(x) < 0.04 and abs(z - 1.0) < 0.009: return C["ink"] if abs(x) < 0.033 else lt(C["webbingl"])   # the clasp's keepers
            if y < 0 and z > RESP.z:   # the respirator bag's sling, up over the right shoulder
                dd = segd(Vector((x, 0, z)), Vector((RESP.x - 0.045, 0, RESP.z + 0.03)), Vector((-0.15, 0, 1.47)))[0]
                if dd < 0.014: return C["ink"] if dd > 0.009 else lt(C["clothl"] if s_ > 0.4 else C["cloth"])
        if base in ("breeches", "puttee", "tunic") and z < 0.62:   # mud splashed up the legs, in blots, thicker low and on the knees
            k_ = (0.62 - z) / 0.45 + (0.25 if n.y < -0.4 else 0.0)
            if _nz.noise(p * Vector((26, 26, 14)) + Vector((3, 1, 7))) > 0.45 - k_ * 0.3: return lt(C["mud"] if _nz.noise(p * 40) > -0.3 else C["breechesd"].lerp(C["mud"], 0.5))
        return None
    def kit(base, p, n, ao):
        x, y, z = p.x, p.y, p.z
        r = mask(C, base, p, n, ao)
        if r is not None: return r
        r = detail(base, p, n, ao)
        if r is not None: return r
        return None
    PAINT = ww1_paint(C, extra=kit, satchel="cloth")

def shock_trooper():
    # Shock Trooper (2026-11-03, second Principality figure, class A, 25 mm): an original WWI assault trooper in the
    # setting's genre, not a copy of any miniature or artwork, and unlike the Yeoman at a glance (owner: "more distinct"):
    # dark grey assault kit, leather knee pads and tall boots instead of puttees; a deep steel helmet in the seat colour
    # with a bolted iron brow plate; the shared WWI gas mask; iron lames on both shoulders and a chest plate of
    # overlapping lames; a grenade bag at the hip; a big spiked two-handed trench hammer swung up overhead (Assault
    # Drill, Shock Charge). Painted PSX look (2026-11-10, as the Combat Engineer): a smooth low-poly body in the dark grey
    # WWI dress (ww1_paint: tunic, webbing, breeches, gloves), its short tunic skirt draped, toeless boot hulls; the
    # tall boots, knee pads, the chest plate of two riveted lames, the grenade bag and the entrenching tool on the back
    # painted; the gas mask painted on the canvas hood (ww1_painted_mask). Solid only for the silhouette: Helmet (the
    # masked head, the mask's drum, the iron brow plate), Chest (the shoulder lames), Hip (the grenade handles), Weapon
    # (hammer), Accent (the helmet). Detail pass (2026-11-10): the grenade bag (three handles) and a short spade on the
    # back made solid, a water bottle and a boot knife added; armband, rivets, rust, mud and the hammer head painted.
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 900; TEX_SIZES = {"Body": 256, "Helmet": 128, "Chest": 128, "Hip": 128, "Weapon": 128, "Back": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.12, 1.4))                                        # leaning into the charge
        human.aim(r, "upperleg01.L", P(0.11, -0.2, 0.52)); human.aim(r, "upperleg02.L", P(0.11, -0.2, 0.52))
        human.aim(r, "lowerleg01.L", P(0.12, -0.18, 0.08)); human.aim(r, "lowerleg02.L", P(0.12, -0.18, 0.08))
        human.aim(r, "foot.L", P(0.12, -0.34, -0.01))
        human.aim(r, "upperleg01.R", P(-0.1, 0.1, 0.5)); human.aim(r, "upperleg02.R", P(-0.1, 0.1, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.11, 0.26, 0.13)); human.aim(r, "lowerleg02.R", P(-0.11, 0.26, 0.13))
        human.aim(r, "foot.R", P(-0.11, 0.14, -0.02))                                     # pushing off the back foot
        human.reach(r, "R", P(-0.33, 0.02, 1.28), P(-0.22, -0.1, 1.48))                  # wound up: the top hand by the right shoulder
        human.reach(r, "L", P(0.16, -0.3, 1.2), P(-0.09, -0.38, 1.3))                   # the lower hand across the chest, under the mask
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "boots", "thigh": "breeches", "torso": "tunic"}, head="hood",
                                mitts="leather", shape="male-young-averagemuscle-minweight")
    ww1_uniform(bands=False)   # the loose wool and the tunic's skirt
    toeless_feet()
    human_ = bpy.data.objects["human"]
    knees = []   # each posed knee, for the painted pads
    for x in (-0.11, 0.11):
        ok, k_, *_ = human_.ray_cast(Vector((x, -1.0, 0.5)), Vector((0, 1, 0)))
        if ok: knees.append(k_)
    skirt = next(o for o in MODULES["Body"] if o.name.startswith("cloth"))
    mask = ww1_painted_mask()   # leaves the Helmet module current
    hc = Vector((hp.x, hp.y - 0.022, top.z - 0.07))
    # the deep helmet: a high dome coming low over the brow, a flared skirt over the ears and neck, in the seat colour
    sphere(tuple(hc + Vector((0, 0.0, 0.03))), 0.122, "accent", (1, 1.1, 0.85), seg=10, rings=7)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.15, radius2=0.12, depth=0.06, end_fill_type='NOTHING', location=tuple(hc + Vector((0, 0.012, -0.0))))
    o = bpy.context.object; o.scale = (1, 1.1, 1); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; add(o, "accent")
    bf = hc + Vector((0, -0.128, 0.05))   # the brow plate, bolted over the front of the helmet
    box(tuple(bf), (0.15, 0.016, 0.075), "iron", rot=(math.radians(-25), 0, 0))
    for x in (-0.085, 0.085): sphere(tuple(bf + Vector((x, 0.0, 0.0))), 0.014, "metal", seg=6, rings=4)   # its lugs
    module("Chest")
    LAMES = []   # (centre, its across axis, its along axis) of each shoulder plate, for its painted rim and rivets
    for x in (-1, 1):   # iron lames over each shoulder, down onto the upper arm
        ok, sh_, nrm, _ = human_.ray_cast(Vector((0.7 * x, -0.03, 1.85)), Vector((-x, 0, -0.75)).normalized())   # down onto the shoulder
        if not ok: continue
        pl = sphere(tuple(sh_ + nrm * 0.022), 1, "iron", (0.07, 0.08, 0.022), seg=8, rings=5)   # a curved plate lying on it
        pl.rotation_mode = 'QUATERNION'; pl.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(nrm)
        m3 = pl.rotation_quaternion.to_matrix(); LAMES.append((sh_ + nrm * 0.022, m3.col[0].normalized(), m3.col[1].normalized()))
    module("Hip")
    # the grenade bag on the left hip, over the tunic skirt (solid since the detail pass, 2026-11-10); three stick
    # grenades' handles stand out of it
    ok, bh, *_ = skirt.ray_cast(Vector((1.0, -0.01, 0.95)), Vector((-1, 0, 0)))
    bag = (bh if ok else Vector((0.18, -0.01, 0.95))) + Vector((0.012, 0, 0))
    SACK = bag + Vector((0.018, -0.004, -0.06))
    sphere(tuple(SACK), 1, "webbing", (0.034, 0.066, 0.075), seg=8, rings=6)
    for k in (-0.026, 0.0, 0.026):
        bar(bag + Vector((0.016, k, -0.03)), bag + Vector((0.03 + abs(k) * 0.3, k * 1.3, 0.08 - abs(k))), 0.007, "wood", sides=4)
        sphere(tuple(bag + Vector((0.03 + abs(k) * 0.3, k * 1.3, 0.08 - abs(k)))), 0.009, "metal", seg=4, rings=3)
    def surf(o_, d_, out=0.012):   # the outermost point of the body or the tunic skirt seen from o_ along d_
        best = None
        for ob in (human_, skirt):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * out
    CANT = surf(Vector((-1.0, 0.04, 0.87)), Vector((1, 0, 0)), 0.0) + Vector((-0.02, 0, 0))   # the water bottle on the right hip
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.048, depth=0.036, location=tuple(CANT), rotation=(0, math.pi / 2, 0))
    o = bpy.context.object; o.scale = (1, 0.8, 1); add(o, "cloth")
    bar(CANT + Vector((0, 0, 0.034)), CANT + Vector((0, 0, 0.056)), 0.01, "brass", sides=5)
    kn_, an_ = JOINTS["lowerleg01.R"], JOINTS["foot.R"]; shin = (an_ - kn_).normalized()   # a trench knife pushed into the right boot top
    KN = surf(kn_ + shin * 0.1 + Vector((-1.0, 0, 0)), Vector((1, 0, 0)), 0.006)
    bar(KN, KN + shin * 0.1, 0.012, "leather", sides=4)                                                # the sheath
    bar(KN - shin * 0.06, KN, 0.009, "wood", sides=4)                                                  # the grip
    bar(KN - Vector((0, 0.016, 0)), KN + Vector((0, 0.016, 0)), 0.005, "iron", sides=3)               # the guard
    module("Back")
    SP0 = surf(Vector((0.075, 1.0, 1.02)), Vector((0, -1, 0)), 0.02); SP1 = surf(Vector((-0.075, 1.0, 1.27)), Vector((0, -1, 0)), 0.02)   # the short spade slung across the back
    spd = (SP1 - SP0).normalized()
    bar(SP0 + spd * 0.04, SP1, 0.011, "wood", sides=5)
    box(tuple(SP0), (0.075, 0.012, 0.09), "iron", rot=(0, math.atan2(spd.x, spd.z), 0))
    bar(SP1 - Vector((0.025, 0, -0.012)), SP1 + Vector((0.025, 0, 0.012)), 0.009, "wood", sides=4)   # its T grip
    module("Weapon")
    # the trench hammer swung up overhead: a long haft through both fists, a big iron head with spikes high behind
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    u = (hr - hl).normalized(); top_ = hr + u * 0.36
    bar(hl - u * 0.05, top_, 0.019, "wood", sides=6)   # the butt just past the lower fist, clear of the chest
    side = u.cross(Vector((0, -1, 0))).normalized(); fwd_ = side.cross(u).normalized()
    if fwd_.y > 0: fwd_ = -fwd_
    box(tuple(top_), (0.13, 0.085, 0.085), "iron")
    hd = bpy.context.object; hd.rotation_mode = 'QUATERNION'; hd.rotation_quaternion = Vector((1, 0, 0)).rotation_difference(fwd_)
    HAX = [hd.rotation_quaternion.to_matrix().col[k].normalized() for k in range(3)]   # the head's own axes, for its painted edges
    bar(top_ + fwd_ * 0.065, top_ + fwd_ * 0.08, 0.05, "metal", sides=8)                              # the striking face
    for d_ in (-fwd_, u, -u):
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.016, radius2=0.0, depth=0.07, location=tuple(top_ + d_ * 0.08))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d_); add(o, "iron")   # spikes
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **dict(tunic="48493e", tunicd="2b2c25", tunicl="606154", breeches="414237", breechesd="282921",
                    hood="4c4e46", hoodd="303229", mask="4a4e40", maskd="2f3229", lens="b8b090", lensd="8a8468", lensl="e0dcc0",
                    plate="676b63", plated="3a3d37", platel="969a8f", boot="221d18", bootd="15120f", bootl="3e342a",
                    pad="6e4c30", padd="45301e", padl="8e6842", bag="5a523c", bagd="3a3528", bagl="726a50",
                    rust="5a3a24", rustd="3e2818", band="5e1a14", bandl="7a2a1e", gren="44483a", grend="2a2d24")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def brace(x, z): return abs(abs(x) - (0.09 + (z - 1.0) * 0.03)) < 0.0135 and 1.0 < z   # as ww1_paint's braces
    def detail(base, p, n, ao):   # detail pass (2026-11-10): the solid kit painted part by part (None: not one of these)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False, grime=0.0): return lit(c_, n, ao, metal, grime, p)
        if base == "webbing" and (p - SACK).length < 0.1:   # the grenade bag: a drawstring neck, sagging folds, a stencilled mark
            q = p - SACK
            if q.z > 0.05: return C["ink"] if (q.y * 160) % 1.0 < 0.3 else lt(C["bagd"])
            if abs(q.z - 0.045) < 0.005: return C["ink"]
            if n.x > 0.5 and abs(q.y) < 0.018 and abs(q.z + 0.005) < 0.02 and abs(q.y) + abs(q.z + 0.005) < 0.022: return C["band"]   # a red diamond
            if math.sin(q.z * 110 + q.y * 35) > 0.88: return lt(C["bagd"])
            return lt(C["bagl"] if s_ > 0.5 else C["bag"], grime=0.25)
        if base in ("wood", "metal") and (p - SACK).length < 0.17 and p.z > SACK.z:   # the grenades' handles and caps
            if base == "metal": return C["ink"] if s_ < -0.2 else lt(C["gren"] if s_ < 0.5 else C["metall"], metal=True)
            return C["brass"] if p.z < SACK.z + 0.04 else psx_kit(C, "wood", p, n, ao)
        if base in ("cloth", "brass") and (p - CANT).length < 0.08:   # the water bottle: felt cover, a seam round the rim, the cap
            q = p - CANT; r_ = math.hypot(q.y / 0.8, q.z)
            if base == "brass": return C["brassl"] if s_ > 0.4 else C["brass"]
            if abs(n.x) > 0.6:
                if r_ > 0.044: return C["ink"]
                if r_ > 0.038: return C["clothd"]
                return lt(C["clothl"] if s_ > 0.5 else C["cloth"])
            if abs(q.x) < 0.003: return C["clothd"]
            return lt(C["cloth"], grime=0.25)
        if base in ("leather", "wood", "iron") and (p - KN).length < 0.13 and p.z < 0.6:   # the boot knife
            if base == "leather":
                t = (p - KN).dot(shin)
                if t > 0.088: return C["metall"]                                                       # the chape
                return C["ink"] if abs(t - 0.03) < 0.004 else lt(C["leatherl"] if s_ > 0.4 else C["leather"])
            if base == "iron": return C["ironl"] if s_ > 0.3 else C["iron"]
            return C["ink"] if abs((p - KN).dot(shin) + 0.03) < 0.004 else psx_kit(C, "wood", p, n, ao)
        if base in ("wood", "iron") and p.y > 0.05 and segd(p, SP0, SP1)[0] < 0.07 and p.z < 1.4:   # the spade on the back
            q = p - SP0; al = q.dot(spd); ac = q.dot(Vector((spd.z, 0, -spd.x)))
            if base == "wood":
                t_ = (p - SP0).dot(spd)
                if abs(t_ - 0.14) < 0.012: return C["ink"] if abs(t_ - 0.14) > 0.009 else lt(C["leather"])   # its strap
                return psx_kit(C, "wood", p, n, ao)
            if n.y < -0.5: return C["dark"]
            if abs(ac) > 0.033 or abs(al) > 0.041: return C["ink"]
            if abs(ac) > 0.027 or al < -0.034: return C["ironl"]                                       # the worn bright edge
            if abs(ac) < 0.004 and al > -0.005: return C["irond"]                                      # the pressed rib
            if _nz.noise(p * 26) > 0.35: return lt(C["rust"] if _nz.noise(p * 60) > -0.2 else C["rustd"])
            return lt(C["iron"], metal=True)
        if base in ("iron", "metal") and (p - top_).length < 0.16:   # the hammer head: bright worn edges, a weld seam, rivets, rust
            q = p - top_; a_, b_, c_ = (q.dot(HAX[k]) for k in range(3))
            if base == "metal":   # the striking face: a bright worn rim round a dark pitted face
                return C["ironl"] if (p - (top_ + fwd_ * 0.08)).length < 0.03 and s_ > -0.4 else lt(C["iron"], metal=True)
            if abs(a_) > 0.067 or abs(b_) > 0.044 or abs(c_) > 0.044:   # a spike: dark root, bright point
                return C["ironl"] if q.length > 0.1 else lt(C["irond"], metal=True)
            e2 = sorted((0.065 - abs(a_), 0.0425 - abs(b_), 0.0425 - abs(c_)))
            if e2[1] < 0.004: return C["ink"]
            if e2[1] < 0.009: return C["ironl"]                                                        # the worn edges
            if abs(a_ + 0.012) < 0.004: return C["irond"]                                               # a weld seam round it
            other = b_ if abs(c_) > abs(b_) else c_   # across the face this point is on
            for ax_ in (-0.035, 0.03):   # two rivets down the middle of each side face
                dd = math.hypot(a_ - ax_, other)
                if dd < 0.006: return C["metall"]
                if dd < 0.009: return C["ink"]
            if _nz.noise(p * 28) > 0.45: return lt(C["rust"] if _nz.noise(p * 70) > -0.2 else C["rustd"])
            return lt(C["plate"] if s_ < 0.6 else C["platel"], metal=True)
        if base == "wood" and (p - hl).dot(u) < 0.07 and segd(p, hl - u * 0.05, top_)[0] < 0.03:   # a leather grip bound round the haft's foot
            t = (p - hl).dot(u); ang = math.atan2((p - hl).dot(side), (p - hl).dot(fwd_))
            return C["ink"] if (t * 70 + ang / math.pi) % 1.0 < 0.22 else lt(C["leatherl"] if s_ > 0.4 else C["leather"])
        if base == "iron" and z > 1.3 and abs(x) > 0.12 and (p - bf).length > 0.12 and LAMES:   # the shoulder lames: a rolled rim, a lame line, rivets
            c0, ax0, ay0 = min(LAMES, key=lambda l: (p - l[0]).length)
            a_, b_ = (p - c0).dot(ax0) / 0.07, (p - c0).dot(ay0) / 0.08
            rr = math.hypot(a_, b_)
            if rr > 0.88: return C["ink"] if rr > 0.95 else C["platel"]
            for ra, rb in ((0.62, 0.0), (-0.62, 0.0), (0.0, 0.66)):
                dd = math.hypot((a_ - ra) * 0.07, (b_ - rb) * 0.08)
                if dd < 0.0065: return C["brassl"] if (a_ - ra) < 0 else C["brass"]
                if dd < 0.0095: return C["ink"]
            if abs(math.sin((z - 1.42) * 90)) < 0.12: return C["ink"]
            if _nz.noise(p * 30) > 0.5: return lt(C["rust"])
            return lt(C["platel"] if s_ > 0.55 else C["plate"], metal=True)
        if base == "iron" and (p - bf).length < 0.12:   # the brow plate: a light top edge, dark under it, a row of bolts, scratches
            q = p - bf
            if abs(q.x) > 0.066: return C["ink"]
            if n.y < -0.3 and abs(abs(q.x) - 0.045) < 0.007 and abs(q.z) < 0.007: return C["metall"] if q.z > 0 else C["ink"]
            if n.y < -0.3 and abs(q.z - (q.x * 0.6 + 0.01)) < 0.0025 and -0.02 < q.x < 0.025: return C["ironl"]   # a bright scratch
            return lt(C["ironl"] if n.z > 0.5 else (C["irond"] if n.z < -0.3 else C["iron"]), metal=True)
        return None
    def kit(base, p, n, ao):
        x, y, z = p.x, p.y, p.z
        r = mask(C, base, p, n, ao)
        if r is not None: return r
        r = detail(base, p, n, ao)
        if r is not None: return r
        if base == "wood":   # the hammer's haft: iron bands near the lower fist
            t = (p - hl).dot(u)
            for b_ in (0.1, 0.24):
                if abs(t - b_) < 0.014: return C["ink"] if abs(t - b_) > 0.01 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
            return None
        if base not in ("tunic", "breeches", "boots"): return None
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.075 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0
        if base == "tunic" and not on_arm and brace(x, z):   # the braces with ink edges a texel wide (ww1_paint's thinner ones broke into dashes on the dark wool)
            dd = abs(abs(x) - (0.09 + (z - 1.0) * 0.03))
            if dd > 0.0072: return C["ink"]
            return lit(C["webbingl"] if dd < 0.003 and n.dot(PSX_LIGHT) > 0.3 else C["webbing"], n, ao, p=p)
        if base == "tunic" and y < 0 and 0.955 < z < 1.045 and (0.04 < abs(x) < 0.09 or 0.105 < abs(x) < 0.155):   # the four pouches, tidier lines
            u_ = abs(x) - (0.065 if abs(x) < 0.1 else 0.13)
            if abs(u_) > 0.017 or z < 0.964 or z > 1.036: return C["ink"]
            if abs(z - 1.016) < 0.0045: return C["ink"]
            if abs(u_) < 0.006 and abs(z - 1.006) < 0.006: return C["brassl"] if z > 1.006 else C["brass"]
            return lit(C["webbingl"] if z > 1.016 else C["webbing"], n, ao, p=p)
        if on_arm and side_ == "L" and 0.38 < along < 0.56 and base == "tunic":   # the assault detachment's armband: red, a pale stripe
            if along < 0.395 or along > 0.545: return C["ink"]
            return lit(C["webbingl"] if abs(along - 0.47) < 0.018 else C["band"], n, ao, p=p)
        if base in ("breeches", "boots") and 0.035 < z < 0.6 and not 0.35 < z < 0.41 and _nz.noise(p * Vector((26, 26, 14)) + Vector((3, 1, 7))) > 0.48 - (0.6 - z) * 0.6:   # mud splashed up the legs
            return lit(C["mud"] if _nz.noise(p * 40) > -0.3 else C["breechesd"].lerp(C["mud"], 0.5), n, ao, p=p)
        if z < 0.405:   # the tall boots: pull-on leather to below the knee, creased at the ankle, a strap at the top
            if z < 0.022: return lit(C["sole"], n, ao, p=p)
            if z < 0.03 or z > 0.396: return C["ink"]
            if 0.356 < z < 0.374: return C["ink"] if z < 0.36 or z > 0.37 else lit(C["leatherl"], n, ao, p=p)
            if 0.1 < z < 0.2 and math.sin(z * 260 + x * 40) > 0.75: c = C["bootd"]
            elif n.dot(PSX_LIGHT) > 0.62: c = C["bootl"]
            else: c = C["boot"]
            return lit(c, n, ao, grime=0.3 * max(0.0, (0.2 - z) / 0.17), p=p)
        for k_ in knees:   # the leather knee pads, strapped round the leg
            e = p - k_
            if abs(e.x) < 0.085 and abs(e.y) < 0.12 and abs(e.z) < 0.09:
                if abs(e.z - 0.075) < 0.008 or abs(e.z + 0.07) < 0.008:   # the straps
                    return C["ink"] if abs(abs(e.z) - 0.0725) > 0.005 else lit(C["leather"], n, ao, p=p)
                if e.y < 0.02 and n.y < -0.1:
                    d = (e.x / 0.048) ** 2 + (e.z / 0.062) ** 2
                    if d < 1.0:
                        if d > 0.8: return C["ink"]
                        if math.hypot(e.x, e.z - 0.035) < 0.006: return C["brassl"]
                        c = C["padl"] if e.z > 0.02 and n.dot(PSX_LIGHT) > 0.3 else (C["padd"] if e.z < -0.03 else C["pad"])
                        return lit(c, n, ao, grime=0.25, p=p)
        if z < 0.44 and base != "tunic":   # the breeches bloused over the boot tops
            return lit(C["breechesd"] if math.sin(z * 300 + x * 50) > 0.6 else C["breeches"], n, ao, grime=0.2, p=p)
        top_p = 1.315 - 1.5 * x * x                                       # the chest plate: two riveted lames, the braces over them
        if base == "tunic" and not on_arm and y < 0 and 1.045 < z < top_p and abs(x) < 0.135 and not brace(x, z):
            ed = min(z - 1.045, top_p - z, 0.135 - abs(x))
            if ed < 0.008: return C["ink"]
            RIV = [(sx * 0.112, rz) for sx in (-1, 1) for rz in (1.075, 1.2)] + [(0, 1.29), (-0.05, 1.075), (0.05, 1.075)]
            r_ = psx_dot(p, [Vector((rx, y, rz)) for rx, rz in RIV], C, 0.0065)
            if r_ is not None: return r_
            for rx, rz in RIV:   # rust weeping down from the rivets (detail pass 2026-11-10)
                if abs(x - rx) < 0.005 and rz - 0.045 < z < rz - 0.01 and _nz.noise(Vector((rx * 40, 0, z * 30))) > -0.1: return lit(C["rust"], n, ao, p=p)
            if 1.1 < z < 1.16 and abs(z - 1.13 - (x - 0.04) * 0.5) < 0.0025 and 0.0 < x < 0.08: return C["platel"]   # a bright gouge
            c = C["plate"]
            if abs(z - 1.17) < 0.005: c = C["ink"]
            elif 0 < 1.17 - z < 0.016: c = C["plated"]
            elif 0 < z - 1.17 < 0.008 or (ed < 0.014 and z > 1.25): c = C["platel"]
            elif abs(x) < 0.006: c = C["platel"]                                                     # the ridge
            return lit(c, n, ao, metal=True, p=p)
        return None
    PAINT = ww1_paint(C, extra=kit, hips=False)

def lieutenant():
    # Lieutenant (2026-11-03, third Principality figure, class B, 32 mm): an original WWI-crusader officer on the owner's
    # reference images as cues, not a copy of the miniature, the art or any paint job. A long green greatcoat worn open
    # with broad lapels over a steel breastplate with medals, a Sam Browne belt and cross strap, breeches, puttees and
    # ankle boots; an open-faced sallet in the seat colour (owner's sallet references: not a WWI helmet) over a mail collar, a blank mannequin face (owner:
    # no gas mask on this one);
    # standing easy, the left fist on the hip, a curved sword resting on the right shoulder, a large oval shield with a
    # patriarchal cross slung on the back (LEADER). Painted PSX look (2026-11-10, as the Combat Engineer): a smooth
    # low-poly body, the greatcoat's long skirt draped, toeless boot hulls; the breastplate with its lames, rivets and
    # medals, the lapels, collar, Sam Browne belt and cross strap, the coat's seams, pockets, buttons and half belt, the
    # map case on the right hip, the breeches, puttees, boots and gloves painted (ww1_paint for the legs and sleeves).
    # Solid only for the silhouette: Coat (the long skirt), Helmet (the sallet with its raised visor, the mail collar), Back (shield), Weapon
    # (sword), Accent (the helm and the shield's face).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1000; TEX_SIZES = {"Body": 256, "Coat": 128, "Back": 128, "Weapon": 128, "Hip": 128, "Helmet": 128}
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.03, 1.4))                                         # upright, chest out
        human.aim(r, "upperleg01.L", P(0.13, -0.06, 0.5)); human.aim(r, "upperleg02.L", P(0.13, -0.06, 0.5))
        human.aim(r, "lowerleg01.L", P(0.15, -0.06, 0.08)); human.aim(r, "lowerleg02.L", P(0.15, -0.06, 0.08))
        human.aim(r, "foot.L", P(0.17, -0.22, -0.01))
        human.aim(r, "upperleg01.R", P(-0.11, 0.03, 0.5)); human.aim(r, "upperleg02.R", P(-0.11, 0.03, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.12, 0.05, 0.08)); human.aim(r, "lowerleg02.R", P(-0.12, 0.05, 0.08))
        human.aim(r, "foot.R", P(-0.15, -0.1, -0.02))
        human.reach(r, "R", P(-0.33, -0.02, 1.02), P(-0.27, -0.14, 1.24))               # the sword hand up by the right shoulder
        human.reach(r, "L", P(0.35, 0.06, 1.18), P(0.24, -0.02, 1.02))                   # the left fist on the hip
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}, head="mannequin",
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    surf = ww1_uniform(bands=False)   # the loose wool and the tunic's skirt; the puttee tapes are painted
    toeless_feet()
    under = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    hem_t = min(v.co.z for o in under for v in o.data.vertices)
    # the greatcoat's long skirt, worn open at the front, hanging to mid-shin over the tunic skirt; in its own node (Coat),
    # so its texture does not take the chest's texels
    module("Coat")
    coat = cloth_cone(0, 0.005, 0.99, 0.3, 0.18, 0.26, "greatcoat", sy=0.82, keep=FRONT(32), tris=130, thick=0.012, extra=under, rows=8)
    hem_c = min(v.co.z for v in coat.data.vertices)
    module("Helmet")
    c0 = sallet(closed=False)   # open: the face shows under the brow
    # a mail collar round the neck under the helm, down onto the shoulders
    nz = NECK.z if hasattr(NECK, "z") else c0.z - 0.16
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.095, radius2=0.064, depth=0.1, end_fill_type='NOTHING', location=(c0.x, c0.y + 0.012, nz + 0.02))
    o = bpy.context.object; o.scale = (1, 0.9, 1); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "mail")
    module("Back")
    # a large oval shield slung on the back, curved round the body, in the seat colour with an iron rim and a brass
    # patriarchal cross (two bars, the upper shorter) with ball ends
    sc = surf(0.02, 1.18, front=False, out=0.1); t_ = 0.2   # leaning a little
    def L(lx, lz, out=0.0):
        x_, z_ = lx * math.cos(t_) - lz * math.sin(t_), lx * math.sin(t_) + lz * math.cos(t_)
        return sc + Vector((x_, -1.2 * lx * lx + out, z_))
    ring = [(0.17 * math.sin(2 * math.pi * k / 24), 0.3 * math.cos(2 * math.pi * k / 24)) for k in range(24)]
    me = bpy.data.meshes.new("shield"); me.from_pydata([tuple(L(0, 0))] + [tuple(L(a_, b_)) for a_, b_ in ring], [], [(0, 1 + k, 1 + (k + 1) % 24) for k in range(24)])
    sh = bpy.data.objects.new("shield", me); scn.collection.objects.link(sh); sh.modifiers.new("sol", "SOLIDIFY").thickness = 0.016; add(sh, "accent")
    for k in range(0, 24, 2): bar(L(*ring[k], out=0.01), L(*ring[(k + 2) % 24], out=0.01), 0.011, "iron", sides=3)   # the rim
    for a_, b_ in (((0, -0.24), (0, 0.24)), ((-0.07, 0.14), (0.07, 0.14)), ((-0.11, 0.03), (0.11, 0.03))):
        bar(L(*a_, out=0.014), L(*b_, out=0.014), 0.012, "brass", sides=4)
        for e_ in (a_, b_): sphere(tuple(L(*e_, out=0.014)), 0.017, "brass", seg=5, rings=3)
    module("Weapon")
    # the curved sword resting on the right shoulder: the grip through the right fist, the blade up and back
    fr, d = FISTS["R"]; want = Vector((-0.04, 0.78, 0.6))
    e = (want - d * want.dot(d)).normalized(); side = e.cross(Vector((1, 0, 0))).normalized()   # the edge faces forward
    if side.y > 0: side = -side
    base, Lb, w = fr + e * 0.055, 0.5, 0.036
    def at(t, c): return base + e * t - side * (0.18 * (t / Lb) ** 2 * Lb * 0.25) + side * c   # the blade bowed back
    outline = [at(0, w / 2), at(Lb * 0.45, w * 0.55), at(Lb * 0.85, w * 0.6), at(Lb, -w * 0.3), at(Lb * 0.85, -w * 0.4), at(Lb * 0.45, -w / 2), at(0, -w / 2)]
    me = bpy.data.meshes.new("blade"); me.from_pydata([tuple(p_) for p_ in outline], [], [list(range(len(outline)))])
    bl = bpy.data.objects.new("blade", me); scn.collection.objects.link(bl); bl.modifiers.new("sol", "SOLIDIFY").thickness = 0.007; add(bl, "metal")
    gx = e.cross(side).normalized()
    bar(base - gx * 0.045, base + gx * 0.045, 0.009, "brass", sides=4)                    # crossguard
    bar(fr - e * 0.06, fr + e * 0.05, 0.012, "leather", sides=4)                          # grip
    sphere(tuple(fr - e * 0.07), 0.016, "brass", seg=5, rings=3)                          # pommel
    module("Hip")
    # detail pass (2026-11-10): a holstered revolver on the left front hip, the map case on the right made solid
    coat.data.update(); bpy.context.view_layer.update()
    human_ = bpy.data.objects["human"]
    def surf2(o_, d_, out=0.012):   # the outermost point of the body, the tunic skirt or the coat seen from o_ along d_
        best = None
        for ob in (human_, coat, *under):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * out
    HOL = surf2(Vector((0.155, -1.0, 0.9)), Vector((0, 1, 0)), 0.0) + Vector((0, -0.02, 0))
    box(tuple(HOL), (0.055, 0.035, 0.12), "leather", rot=(0, math.radians(-12), 0))
    BUTT = HOL + Vector((-0.012, 0.0, 0.075))
    bar(BUTT - Vector((0, 0, 0.02)), BUTT + Vector((-0.012, -0.006, 0.03)), 0.012, "wood", sides=4)   # the revolver's butt out of it
    bar(HOL + Vector((0.01, 0.0, 0.06)), surf2(Vector((0.15, -1.0, 1.0)), Vector((0, 1, 0)), 0.006), 0.006, "leather", sides=3)   # its loop up to the belt
    CASE = surf2(Vector((-1.0, -0.005, 0.885)), Vector((1, 0, 0)), 0.0) + Vector((-0.016, 0, 0))
    box(tuple(CASE), (0.03, 0.13, 0.12), "leather")
    bar(CASE + Vector((0, 0.0, 0.06)), surf2(Vector((-1.0, -0.005, 1.0)), Vector((1, 0, 0)), 0.006), 0.007, "leather", sides=3)   # its strap up to the belt
    BINO = surf2(Vector((-0.095, -1.0, 0.9)), Vector((0, 1, 0)), 0.0) + Vector((0, -0.03, 0))      # a binocular case on the right front of the belt
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.03, depth=0.08, location=tuple(BINO)); add(bpy.context.object, "leather")
    sd_ = Vector((-0.8, -0.6, 0)).normalized()   # the empty scabbard hung from the left hip, down the back of the coat
    SCB = [surf2(Vector((0.0, 0.08, zz)) - sd_ * 1.0, sd_, 0.018) for zz in (0.96, 0.73, 0.5)]
    for a_, b_ in zip(SCB, SCB[1:]): bar(a_, b_, 0.014, "leather", sides=4)
    bar(SCB[-1], SCB[-1] + (SCB[-1] - SCB[-2]).normalized() * 0.04, 0.012, "brass", sides=4)       # its chape
    bar(SCB[0] - (SCB[1] - SCB[0]).normalized() * 0.01, SCB[0] + (SCB[1] - SCB[0]).normalized() * 0.03, 0.017, "brass", sides=4)   # its throat
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **dict(tunic="4e5838", tunicd="2e3521", tunicl="66714a", coat="4e5838", coatd="2e3521", coatl="66714a",
                    plate="5c6062", plated="34363a", platel="8c9092", mannequin="cfc4b0", mannequind="a49a88",
                    leather="4a3020", leatherl="6c4a30", ribbon1="6a1a14", ribbon2="2a3a6a", ribbon3="a49460",
                    mail="3c3e42", maill="5c5e62", steel="8a8c90", steell="b8babc", case="4a3424", casel="6a4c34",
                    steeld="54565a", braid="a48d52", braidd="6a5a30", rust="5a3a24")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def edge(q, h, m1):   # in a box of half-sizes h, local point q: on an edge (within m1 of two faces)
        return sum(1 for k in range(3) if h[k] - abs(q[k]) < m1) >= 2
    def detail(base, p, n, ao):   # detail pass (2026-11-10): the new kit and the painted details (None: not here)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False, grime=0.0): return lit(c_, n, ao, metal, grime, p)
        if base in ("leather", "wood") and (p - HOL).length < 0.12 and z < 1.0:   # the holster: flap with a brass stud, stitched edges, the revolver's chequered butt
            if base == "wood": return C["woodl"] if s_ > 0.6 else (C["wood"] if s_ > 0.0 else C["woodd"])
            q = p - HOL; ca, sa = math.cos(math.radians(12)), math.sin(math.radians(12))
            q = Vector((q.x * ca - q.z * sa, q.y, q.x * sa + q.z * ca))
            if abs(q.x) > 0.03 or abs(q.z) > 0.062: return lt(C["leatherl"] if s_ > 0.5 else C["leather"])   # its loop
            if edge(q, (0.0275, 0.0175, 0.06), 0.004): return C["ink"]
            if q.y < 0 and abs(q.z - 0.025) < 0.004: return C["ink"]                                    # the flap's edge
            if q.y < 0 and math.hypot(q.x, q.z - 0.012) < 0.006: return C["brassl"] if q.z > 0.012 else C["brass"]
            if q.y < 0 and 0.0275 - abs(q.x) < 0.008: return lt(C["leatherl"])                          # stitched edge
            return lt(C["leatherl"] if q.z > 0.025 and s_ > 0.3 else C["leather"], grime=0.15)
        if base == "leather" and (p - CASE).length < 0.12 and x < -0.1:   # the map case: a flap, a brass catch, a window of map
            q = p - CASE
            if abs(q.x) > 0.016 or abs(q.y) > 0.066 or abs(q.z) > 0.061: return lt(C["casel"] if s_ > 0.5 else C["case"])   # the strap
            if edge(q, (0.015, 0.065, 0.06), 0.004): return C["ink"]
            if n.x < -0.5:
                if abs(q.z - 0.028) < 0.004: return C["ink"]
                if abs(q.y) < 0.007 and abs(q.z - 0.016) < 0.007: return C["brassl"] if q.z > 0.016 else C["brass"]
                if q.z < 0.0 and abs(q.y) < 0.045 and q.z > -0.045:   # a celluloid window over the map: pale paper, contour lines
                    if abs(q.y) > 0.04 or q.z < -0.04 or q.z > -0.004: return C["ink"]
                    return C["ribbon3"] if (q.y * 0.6 + q.z) * 90 % 1.0 < 0.25 else C["bone"]
            return lt(C["casel"] if q.z > 0.028 else C["case"], grime=0.15)
        if base == "leather" and (p - BINO).length < 0.07:   # the binocular case: a lid with an ink rim and a brass stud
            q = p - BINO
            if abs(q.z - 0.02) < 0.004 or (n.z > 0.6 and math.hypot(q.x, q.y) > 0.025): return C["ink"]
            if q.y < 0 and abs(q.x) < 0.006 and abs(q.z - 0.01) < 0.006: return C["brassl"] if q.z > 0.01 else C["brass"]
            return lt(C["leatherl"] if q.z > 0.02 or s_ > 0.55 else C["leather"])
        if base == "leather" and min(segd(p, a_, b_)[0] for a_, b_ in zip(SCB, SCB[1:])) < 0.03:   # the scabbard: a sling ring, a worn highlight
            t = (p - SCB[0]).length
            if abs(t - 0.06) < 0.008: return C["ink"] if abs(t - 0.06) > 0.005 else C["brass"]
            return lt(C["leatherl"] if s_ > 0.45 else C["leather"])
        if base == "metal" and (p - fr).length > 0.05:   # the blade: a bright edge, a dark spine, a fuller down its first half
            t = (p - base_).dot(e); c_ = (p - base_ - e * t + side * (0.18 * (max(t, 0) / Lb) ** 2 * Lb * 0.25)).dot(side)
            if c_ > w * 0.32: return C["steell"]
            if c_ < -w * 0.36: return lt(C["steeld"], metal=True)
            if t < Lb * 0.6 and abs(c_ + 0.002) < 0.0045: return C["steeld"]
            return lit(C["steell"] if s_ > 0.5 else C["steel"], n, ao, metal=True, p=p)
        if base == "leather" and segd(p, fr - e * 0.06, fr + e * 0.05)[0] < 0.016:   # the grip (not the glove round it): twisted wire binding
            return C["braidd"] if ((p - fr).dot(e) * 160) % 1.0 < 0.4 else lt(C["leatherl"] if s_ > 0.3 else C["leather"])
        if base == "iron" and p.y > 0.05:   # the shield's iron rim: rivets, rust
            if (math.atan2(p.x - sc.x, p.z - sc.z) * 24 / math.pi) % 1.0 < 0.18: return C["ironl"] if s_ > 0.2 else C["ink"]
            if _nz.noise(p * 30) > 0.45: return lt(C["rust"])
            return lt(C["ironl"] if s_ > 0.6 else C["iron"], metal=True)
        if base == "brass" and p.y > 0.05:   # the shield's brass cross: an engraved line down each bar, worn bright
            return C["brassl"] if s_ > 0.55 else (C["braidd"] if s_ < -0.1 else C["brass"])
        if base == "greatcoat" and z < hem_c + 0.07 and _nz.noise(p * Vector((24, 24, 12)) + Vector((1, 5, 2))) > 0.3 - (hem_c + 0.07 - z) * 6:   # mud thrown up on the hem
            return C["ink"] if z < hem_c + 0.008 else lt(C["mud"] if _nz.noise(p * 50) > -0.3 else C["coatd"].lerp(C["mud"], 0.5))
        if base in ("puttee", "boots", "breeches") and z < 0.3 and z > 0.03 and _nz.noise(p * Vector((26, 26, 14)) + Vector((3, 1, 7))) > 0.45 - (0.3 - z) * 1.2:
            return lt(C["mud"] if _nz.noise(p * 40) > -0.3 else C["putteed"].lerp(C["mud"], 0.5))   # mud splashed up the boots and puttees
        if base == "tunic":
            side_, along, ad = psx_arm(p)
            for sx in (1, -1):   # shoulder boards from the collar to the shoulder, two brass rank stars on each
                sh = JOINTS["upperarm01.L" if sx > 0 else "upperarm01.R"]
                if n.z > 0.35 and z > sh.z - 0.035 and abs(x) > 0.05 and x * sx > 0:
                    dd, tt = segd(Vector((x, y, 0)), Vector((sx * 0.06, sh.y, 0)), Vector((sh.x, sh.y, 0)))
                    if dd < 0.02:
                        if dd > 0.0145 or tt > 0.96: return C["ink"]
                        L_ = (Vector((x, y, 0)) - Vector((sx * 0.06, sh.y, 0))).length
                        for st in (0.035, 0.06):
                            if abs(L_ - st) < 0.007 and dd < 0.007: return C["brassl"] if s_ > 0.5 else C["brass"]
                        if tt < 0.12 and dd < 0.006: return C["braid"]                                   # its button
                        return lt(C["coatl"] if s_ > 0.5 else C["coat"])
            if ad < 0.075 and abs(along - 1.7) < 0.06 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0:   # cuff rank: a band of braid
                return C["braidd"] if abs(along - 1.7) > 0.042 else lt(C["braid"] if s_ > 0.2 else C["braidd"])
            if strap(x, z) < 0.021 and z > 1.03 and y < 0:   # the cross strap's brass stud and stitching
                q2 = Vector((x, z)) - (S0 + (S1 - S0) * 0.3)
                if q2.length < 0.011: return C["brassl"] if q2.y > 0 else C["brass"]
                if q2.length < 0.015: return C["ink"]
            if 0.972 < z < 1.028 and y < 0 and 0.03 < x < 0.07 and any(math.hypot(x - hx_, z - 1.0) < 0.004 for hx_ in (0.04, 0.055)): return C["ink"]   # belt holes
            if 0.972 < z < 1.028 and y < 0 and abs(x + 0.045) < 0.007: return C["ink"] if abs(x + 0.045) > 0.004 else lt(C["leatherl"])   # the keeper
        return None
    base_ = base
    nz_ = NECK.z
    S0, S1 = Vector((0.12, 1.47)), Vector((-0.15, 0.99))   # the cross strap, left shoulder to right hip, front and back (x, z)
    def strap(x, z):
        d_ = S1 - S0; t = max(0.0, min(1.0, (Vector((x, z)) - S0).dot(d_) / d_.length_squared)); return (Vector((x, z)) - (S0 + d_ * t)).length
    def belt(x, y, z, n, ao, p):   # the Sam Browne belt, its brass buckle at the front
        if z < 0.972 or z > 1.028: return C["ink"]
        if y < 0 and abs(x) < 0.026:
            if abs(x) > 0.021 or abs(z - 1.0) > 0.024: return C["ink"]
            return C["dark"] if abs(x) < 0.01 and abs(z - 1.0) < 0.012 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.4 else C["brass"])
        return lit(C["leatherl"] if abs(z - 1.0) < 0.004 else C["leather"], n, ao, p=p)
    def extra(base, p, n, ao):
        x, y, z = p.x, p.y, p.z
        r_ = detail(base, p, n, ao)
        if r_ is not None: return r_
        if base == "mannequin":   # the blank face: smooth pale plaster
            return lit(C["mannequin"] if n.dot(PSX_LIGHT) > 0.0 else C["mannequind"], n, ao, p=p)
        if base == "mail":   # rings in rows
            return C["ink"] if (z * 260) % 1.0 < 0.3 and (math.atan2(x - c0.x, y - c0.y) * 30 + (z * 130 // 1) * 0.5) % 1.0 < 0.4 else lit(C["maill"] if n.dot(PSX_LIGHT) > 0.4 else C["mail"], n, ao, metal=True, p=p)
        if base == "metal" and (p - fr).length > 0.05:   # the blade: a bright edge, a fuller down the middle
            return lit(C["steell"] if n.dot(PSX_LIGHT) > 0.3 else C["steel"], n, ao, metal=True, p=p)
        if base == "greatcoat":   # the coat's long skirt: open front edges, buttons, pockets, the map case, folds, a muddy hem
            a = math.degrees(math.atan2(x, -y)); aa = abs(a)
            if z < hem_c + 0.008: return C["ink"]
            if z > 0.975: return belt(x, y, z, n, ao, p) if z < 1.03 else lit(C["coatd"], n, ao, p=p)
            if aa < 38: return C["ink"] if aa < 34.5 else lit(C["coatl"], n, ao, p=p)               # the front edges
            for bz in (0.92, 0.82, 0.72):
                if a > 0 and (Vector((aa - 42, (z - bz) * 300))).length < 2.8: return C["brassl"] if z > bz else C["brass"]
            if x < -0.15 and n.x < -0.3 and -0.075 < y < 0.065 and 0.82 < z < 0.95:   # the map case on the right hip
                if z > 0.944 or z < 0.826 or y < -0.069 or y > 0.059: return C["ink"]
                if abs(z - 0.915) < 0.003: return C["ink"]
                if abs(y + 0.005) < 0.007 and abs(z - 0.9) < 0.007: return C["brassl"]
                return lit(C["casel"] if z > 0.915 else C["case"], n, ao, p=p)
            if 125 < aa < 129 and z > 0.75: return C["ink"]                                   # side seams
            if aa > 176 and z < hem_c + 0.22: return C["ink"]                                 # the back vent
            if 55 < aa < 95 and 0.86 < z < 0.92 and not (x < -0.15 and z > 0.82):              # pocket flaps
                if z < 0.866 or aa < 57 or aa > 93: return C["ink"]
                return lit(C["coatd"], n, ao, p=p)
            if z < hem_c + 0.04: return lit(C["coatd"], n, ao, grime=0.5, p=p)
            f = (a / 28.0 + _nz.noise(Vector((a * 0.03, 0, z * 2))) * 0.3) % 1.0                  # long folds
            c = C["coatd"] if f < 0.09 else (C["coatl"] if f < 0.14 else C["coat"])
            return lit(c, n, ao, grime=0.35 * max(0.0, (0.6 - z) / 0.3), p=p)
        if base != "tunic": return None
        side_, along, ad = psx_arm(p)
        if ad < 0.075 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0: return None   # the sleeves: ww1_paint's
        front = y < 0
        if z < 0.97:   # the tunic's skirt in the coat's front opening
            return lit(C["coatd"] if z < hem_t + 0.012 or math.sin(x * 90) > 0.8 else C["coat"], n, ao, p=p)
        if z < 1.03: return belt(x, y, z, n, ao, p)
        if strap(x, z) < 0.021:
            dd = strap(x, z)
            return C["ink"] if dd > 0.016 else lit(C["leatherl"] if dd < 0.004 else C["leather"], n, ao, p=p)
        if z > nz_ - 0.04:   # the coat's turned-up collar
            if z < nz_ - 0.033: return C["ink"]
            return lit(C["coatd"] if n.dot(PSX_LIGHT) < 0.4 else C["coat"], n, ao, p=p)
        if front:
            xi = 0.085 + (z - 1.03) * 0.06; xo = xi + 0.035 + (z - 1.03) * 0.06            # the lapel's inner and outer edges
            if xi - 0.005 < abs(x) < xo + 0.005 and z > 1.03:                              # broad lapels, open to the belt
                if 1.296 < z < 1.318 and 0.1 < x < 0.16:                                   # medal ribbons on the left lapel
                    if z < 1.3 or z > 1.314 or x < 0.104 or x > 0.156: return C["ink"]
                    k = int((x - 0.104) / 0.0174)
                    return C["ink"] if (x - 0.104) % 0.0174 < 0.003 else C[("ribbon1", "ribbon2", "ribbon3")[min(k, 2)]]
                for mx in (0.114, 0.144):                                                   # two medals hanging under them
                    md = math.hypot(x - mx, z - 1.266)
                    if md < 0.013: return C["ink"] if md > 0.0095 else (C["brassl"] if z > 1.268 and x < mx else C["brass"])
                    if abs(x - mx) < 0.005 and 1.276 < z < 1.296: return C["ribbon1"] if abs(x - mx) < 0.003 else C["ink"]
                if abs(x) < xi or abs(x) > xo: return C["ink"]
                if abs(x) < xi + 0.006: return lit(C["coatl"], n, ao, p=p)               # the turned edge catching the light
                return lit(C["coatd"] if z > 1.36 else C["coat"], n, ao, p=p)
            xl = xi + 0.036                                                                 # (the breastplate below fills |x| < xi)
            top_ = 1.4 - 3.0 * x * x
            if abs(x) < xl - 0.036 and 1.03 <= z < top_:                                   # the breastplate
                ed = min(z - 1.03, top_ - z, xl - 0.036 - abs(x))
                if ed < 0.008: return C["ink"]
                r_ = psx_dot(p, [Vector((sx * (xi - 0.018), y, rz)) for sx in (-1, 1) for rz in (1.08, 1.2, 1.32)], C, 0.0065)
                if r_ is not None: return r_
                c = C["plate"]
                for L_ in (1.16, 1.1):                                                       # two lames at the bottom
                    if abs(z - L_) < 0.005: c = C["ink"]
                    elif 0 < L_ - z < 0.014: c = C["plated"]
                if c is C["plate"] and (ed < 0.016 and z > 1.3 or abs(x) < 0.006 and z > 1.16): c = C["platel"]   # rolled top edge, ridge
                return lit(c, n, ao, metal=True, p=p)
        else:
            if abs(x) < 0.004 and z > 1.075: return C["ink"]                                    # the back seam
            if 1.04 < z < 1.075 and abs(x) < 0.1:                                              # the half belt, two buttons
                if z < 1.045 or z > 1.07 or abs(x) > 0.095: return C["ink"]
                if math.hypot(abs(x) - 0.075, z - 1.0575) < 0.007: return C["brass"]
                return lit(C["coatl"], n, ao, p=p)
        if abs(n.x) > 0.94: return C["ink"]                                                    # side seams
        return lit(C["coatl"] if n.dot(PSX_LIGHT) > 0.62 else C["coat"], n, ao, p=p)
    PAINT = ww1_paint(C, extra=extra, hips=False)

def sallet(closed=False, m="accent", k=0.86):
    # The sallet (the owner's references, 2026-11-03; Principality headgear), lofted round the figure's head: a tall
    # egg-shaped skull rising to a ridge from brow to nape, a flat visor front, the eye slit straight across it at eye
    # level, a raised band from the slit round each side to a pivot rivet, a stepped seam down the upper visor, the tail
    # flaring out over the nape, and a brass flame (a little sword) on the right of the brow, in the seat colour.
    # Closed: the face plate comes down to the chin and only the dark slit shows; the head under it is removed (the
    # Yeoman and later figures). Open: the face plate is swung up on the side pivots to rest over the brow (owner) and
    # the plain head, shrunk by k to fit, shows under it (the Lieutenant). Returns the head's centre, for a coif or straps.
    import bmesh as _bm
    heads = [o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name in ("mannequin", "hood") and len(o.data.vertices) > 40]
    hd_ = max(heads, key=lambda o: max((o.matrix_world @ v.co).z for v in o.data.vertices))
    vs = [hd_.matrix_world @ v.co for v in hd_.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    c0 = (lo + hi) / 2; sw, sd, sh = (hi.x - lo.x) / 2 * k, (hi.y - lo.y) / 2 * k, (hi.z - lo.z) / 2 * k
    if closed:   # nothing shows under a closed helm
        MODULES["Body"].remove(hd_); bpy.data.objects.remove(hd_)
    else:        # the head shrunk to sit inside the helm
        mi = hd_.matrix_world.inverted()
        for v in hd_.data.vertices: v.co = mi @ (c0 + (hd_.matrix_world @ v.co - c0) * k)
    W, F, B = sw + 0.016, sd + 0.018, sd + 0.022
    zs = FACE_BOX["mid"].z + 0.004                     # the slit, at the eyes
    top, bot = c0.z + sh + 0.024, c0.z - sh - 0.006
    U, D = top - zs, zs - bot
    # rings from the crown down: (height, width, front, back, squareness); the slit is the gap between rings 5 and 6
    rings = [(zs + U * 0.94, 0.3, 0.5, 0.52, 2.0), (zs + U * 0.82, 0.62, 0.72, 0.74, 2.2), (zs + U * 0.62, 0.86, 0.9, 0.91, 2.4),
             (zs + U * 0.36, 0.97, 0.98, 0.98, 2.6), (zs + U * 0.12, 1.0, 1.0, 1.0, 2.8), (zs + 0.008, 1.0, 1.0, 1.0, 3.0),
             (zs - 0.008, 1.0, 1.0, 1.0, 3.0), (zs - D * 0.45, 1.0, 0.99, 1.04, 3.4), (zs - D * 0.8, 1.02, 0.98, 1.12, 3.6),
             (bot, 1.05, 0.97, 1.24, 3.6)]
    SEG = 18
    def ring_pt(z, ws, fs, bs, n, a, j):
        s, c = math.sin(a), math.cos(a)
        x = W * ws * math.copysign(abs(s) ** (2 / n), s)
        y = -(F * fs if c > 0 else B * bs) * math.copysign(abs(c) ** (2 / n), c)
        if j >= 8: z -= 0.03 * max(0.0, -c) * (j - 7) / 2   # the tail drops lower at the back
        return Vector((c0.x + x, c0.y + y, z))
    verts = [tuple(ring_pt(*r, 2 * math.pi * i / SEG, j)) for j, r in enumerate(rings) for i in range(SEG)]
    apex = len(verts); verts.append((c0.x, c0.y + 0.004, top))
    faces, plate = [], []
    for j in range(len(rings) - 1):
        for i in range(SEG):
            am = abs(math.remainder(2 * math.pi * (i + 0.5) / SEG, 2 * math.pi))
            q = (j * SEG + i, j * SEG + (i + 1) % SEG, (j + 1) * SEG + (i + 1) % SEG, (j + 1) * SEG + i)
            if not closed and ((j >= 6 and am < math.radians(64)) or (j == 5 and math.radians(58) <= am < math.radians(64))):
                plate.append(q); continue                                         # open: the face plate, raised below
            if j == 5 and am < math.radians(58): continue                        # the eye slit
            faces.append((j * SEG + i, j * SEG + (i + 1) % SEG, (j + 1) * SEG + (i + 1) % SEG, (j + 1) * SEG + i))
    faces += [(apex, (i + 1) % SEG, i) for i in range(SEG)]
    me = bpy.data.meshes.new("sallet"); me.from_pydata(verts, [], faces)
    bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    sh_ = bpy.data.objects.new("sallet", me); scn.collection.objects.link(sh_)
    def S(a, z, out=0.0):   # a point on the shell at angle a (0 the front) and height z, out from it
        d = Vector((math.sin(a), -math.cos(a), 0)); o_ = Vector((c0.x, c0.y, z))
        ok, at, *_ = sh_.ray_cast(o_ + d, -d)
        return (at if ok else o_ + d * W) + d * out
    sol = sh_.modifiers.new("sol", "SOLIDIFY"); sol.thickness = 0.007; sol.offset = -1.0
    add(sh_, m)
    if plate:   # open: the face plate swung up on the side pivots, resting over the brow
        piv = Vector((c0.x, c0.y, zs + 0.004))
        rot = Matrix.Translation(piv) @ Matrix.Rotation(math.radians(-99), 4, 'X') @ Matrix.Scale(1.02, 4) @ Matrix.Translation(-piv)
        used = sorted({v for f in plate for v in f}); at_ = {v: n for n, v in enumerate(used)}
        pm = bpy.data.meshes.new("visor"); pm.from_pydata([tuple(rot @ Vector(verts[v])) for v in used], [], [tuple(at_[v] for v in f) for f in plate])
        bm = _bm.new(); bm.from_mesh(pm); _bm.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(pm); bm.free()
        for f in pm.polygons: f.use_smooth = True
        vo = bpy.data.objects.new("visor", pm); scn.collection.objects.link(vo)
        vs_ = vo.modifiers.new("sol", "SOLIDIFY"); vs_.thickness = 0.007; vs_.offset = -1.0; add(vo, m)
    if closed:   # dark behind the slit
        bk = [S(a, zs, -0.014) for a in [math.radians(x) for x in range(-50, 51, 20)]]
        for a_, b_ in zip(bk, bk[1:]): bar(a_, b_, 0.012, "dark", sides=4)
    for x in (-1, 1):
        bd = [S(x * math.radians(t), zs - 0.014, 0.003) for t in range(60, 141, 20)]
        for a_, b_ in zip(bd, bd[1:]): bar(a_, b_, 0.009, m, sides=4)             # the band round the side
        sphere(tuple(S(x * math.pi / 2, zs + 0.004, 0.006)), 0.011, m, seg=6, rings=4)   # the pivot rivet
    sm = [S(math.radians(16) * (1 - t), zs + 0.012 + (U - 0.02) * t, 0.003) for t in (0, 0.25, 0.5, 0.75, 1.0)]
    for a_, b_ in zip(sm, sm[1:]): bar(a_, b_, 0.006, m, sides=4)                  # the visor's stepped seam
    f0, f1 = S(math.radians(-24), zs + 0.02, 0.006), S(math.radians(-22), zs + 0.085, 0.006)
    bar(f0, f1, 0.008, "brass", sides=4)                                          # the brass flame, a small sword
    bar(S(math.radians(-30), zs + 0.033, 0.007), S(math.radians(-17), zs + 0.033, 0.007), 0.004, "brass", sides=3)
    return c0

def blind_marksman():
    # Blind Marksman (2026-11-03, fourth Principality figure, class B, 32 mm): an original blind sniper-priest on the
    # owner's reference cues, not a copy of the miniature, the art or any paint job. A deep hood and a long, full open
    # green cloak; under the hood a blank mannequin face bound with a red blindfold;
    # a priest's stole in the seat colour down the front; the WWI service dress with webbing and rows of ammunition
    # pouches up the chest; crouched on one knee at rest, a very long rifle with a brass telescopic sight and a folded bipod held upright,
    # its butt on the ground. Painted PSX look (2026-11-10, as the Combat Engineer): a smooth low-poly body in the WWI
    # dress (ww1_paint: tunic, webbing, breeches, gloves) with the rows of chest pouches, the puttees and boots painted
    # along the kneeling legs (toeless boot hulls), the red blindfold painted on the blank head, folds, a ragged muddy hem
    # and a brass clasp painted on the cloak and hood. Solid only for the silhouette: Back (cloak and hood, their own
    # texture), Helmet (the blindfolded head, its own texture), Hip (canteen), Weapon (rifle), Accent (the stole).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 900; TEX_SIZES = {"Body": 256, "Back": 256, "Helmet": 128, "Weapon": 128, "Hip": 128}
    from mathutils import noise as _nz
    def pose(r):   # crouched at rest on one knee (owner's sniper photos): right knee and shin on the ground, left foot
        # planted with the knee up, the rifle held upright in front, its butt grounded
        human.aim(r, "spine03", P(0, -0.08, 1.4))
        human.aim(r, "upperleg01.R", P(-0.1, -0.06, 0.5)); human.aim(r, "upperleg02.R", P(-0.1, -0.06, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.11, 0.36, 0.52)); human.aim(r, "lowerleg02.R", P(-0.11, 0.36, 0.52))
        human.aim(r, "foot.R", P(-0.11, 0.56, 0.48))
        human.aim(r, "upperleg01.L", P(0.13, -0.42, 0.9)); human.aim(r, "upperleg02.L", P(0.13, -0.42, 0.9))
        human.aim(r, "lowerleg01.L", P(0.14, -0.46, 0.48)); human.aim(r, "lowerleg02.L", P(0.14, -0.46, 0.48))
        human.aim(r, "foot.L", P(0.14, -0.62, 0.46))
        human.reach(r, "R", P(-0.28, -0.1, 1.12), P(-0.1, -0.27, 1.04))                    # right hand on the grip at the cheek
        human.reach(r, "L", P(0.14, -0.24, 1.12), P(-0.14, -0.23, 1.25))                    # left elbow down, hand under the barrel
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}, head="mannequin",
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1923)
    surf = ww1_uniform(bands=False)   # kneeling: no puttee bands
    human_ = bpy.data.objects["human"]
    legs = {}   # each leg's knee, ankle and shin direction, for the kneeling feet and the painted puttees
    for s, sx in (("L", 1), ("R", -1)):
        K, A = JOINTS[f"lowerleg01.{s}"], JOINTS[f"foot.{s}"]; legs[sx] = (K, A, (A - K).normalized())
    toeless_feet(foot=lambda co, sx: co.x * sx > 0 and (co - legs[sx][1]).dot(legs[sx][2]) > -0.015 and (co - legs[sx][1]).length < 0.3)
    ground = min(v.co.z for v in human_.data.vertices)
    module("Back")
    # the long cloak from the shoulders to the shins, open wide at the front so the arms come out of it
    skirt = [o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name == "tunic" and o.name.startswith("cloth")]
    # pinned round the neck so it settles over the shoulders and arms and falls close round the body (owner: drape
    # better, not longer)
    cloak = cloth_cone(NECK.x, NECK.y + 0.03, NECK.z + 0.02, 0.56, 0.09, 0.3, "cloak", sy=0.9, keep=FRONT(42), ragged=0.05, rnd=rnd, tris=140, mono=False,
                       thick=0.012, rows=12, frames=70, extra=skirt)   # over the tunic's skirt too
    hem = min(v.co.z for v in cloak.data.vertices)
    import bmesh as _bm
    for o_ in [human_] + skirt:   # the back under the cloak never shows: removed, so it cannot poke through
        bm = _bm.new(); bm.from_mesh(o_.data)
        _bm.ops.delete(bm, geom=[f for f in bm.faces if f.normal.y > 0.35 and 0.75 < f.calc_center_median().z < 1.3 and f.calc_center_median().y > 0.0], context='FACES')
        bm.to_mesh(o_.data); bm.free()
    # the deep hood (with the cloak, in Back), lofted round the head: rounded over the skull, open round the face
    # and flaring out onto the shoulders over the cloak
    fb = FACE_BOX
    heads = [o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name == "mannequin" and len(o.data.vertices) > 40]
    hd_ = max(heads, key=lambda o: max((o.matrix_world @ v.co).z for v in o.data.vertices))
    vs = [hd_.matrix_world @ v.co for v in hd_.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    c0 = (lo + hi) / 2; sw, sd, sh = (hi - lo) / 2
    # rings: (height from the head's centre, half width, front depth, back depth, shift back)
    R_ = [(sh + 0.035, sw * 0.62, sd * 0.6, sd * 0.66, 0.012), (sh * 0.7, sw + 0.02, sd + 0.02, sd + 0.03, 0.012),
          (sh * 0.25, sw + 0.026, sd + 0.03, sd + 0.035, 0.0), (-sh * 0.3, sw + 0.03, sd + 0.034, sd + 0.04, 0.0),
          (-sh * 0.8, sw + 0.036, sd + 0.04, sd + 0.05, 0.0), (-sh - 0.05, sw + 0.06, sd + 0.05, sd + 0.07, 0.006),
          (-sh - 0.12, 0.19, sd + 0.07, sd + 0.1, 0.01)]
    SEG = 16; verts = []
    for z, w, f, bk, yb in R_:
        for i in range(SEG):
            a_ = 2 * math.pi * i / SEG; sn, cs = math.sin(a_), math.cos(a_)
            verts.append((c0.x + w * sn, c0.y + yb - (f if cs > 0 else bk) * cs, c0.z + z))
    apex = len(verts); verts.append((c0.x, c0.y + 0.012, c0.z + sh + 0.05))   # rounded over the crown (owner: no point)
    faces = []
    for j in range(len(R_) - 1):
        for i in range(SEG):
            am = abs(math.remainder(2 * math.pi * (i + 0.5) / SEG, 2 * math.pi))
            if 1 <= j <= 4 and am < math.radians(52 if j < 4 else 40): continue        # the opening round the face
            faces.append((j * SEG + i, j * SEG + (i + 1) % SEG, (j + 1) * SEG + (i + 1) % SEG, (j + 1) * SEG + i))
    faces += [(apex, (i + 1) % SEG, i) for i in range(SEG)]
    me = bpy.data.meshes.new("hood"); me.from_pydata(verts, [], faces)
    bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    for p_ in me.polygons: p_.use_smooth = True
    ho = bpy.data.objects.new("hood", me); scn.collection.objects.link(ho)
    sl = ho.modifiers.new("sol", "SOLIDIFY"); sl.thickness = 0.01; sl.offset = -1.0; add(ho, "hood")   # painted as the cloak
    module("Helmet")   # the blindfolded head alone: its own texture, so the band stays whole
    MODULES["Body"].remove(hd_); MODULES["Helmet"].append(hd_)
    module("Body")
    # the priest's stole in the seat colour, hanging from the neck down each side of the opening
    for x in (-0.085, 0.085):
        y0 = surf(x, 1.36, out=0.03).y
        cloth_panel([(x - 0.04, y0, 1.38), (x + 0.04, y0, 1.38)], 0.4, "accent", extra=[], cols=1, rows=5, tris=None, bend=30.0)
    module("Hip")
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.05, depth=0.03, location=(0.195, 0.02, 0.86), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "metal"); bar((0.195, 0.02, 0.91), (0.195, 0.02, 0.94), 0.012, "brass", sides=4)   # water bottle
    CANT = Vector((0.195, 0.02, 0.86))
    module("Weapon")
    # detail pass (2026-11-10): a rosary of wooden beads wound round the left fist under the barrel, an iron cross at its end
    hl_ = FISTS["L"][0]
    BEADS = [hl_ + Vector((-0.03, 0.015, -0.03 - 0.035 * k)) for k in range(3)]
    for b0, b1 in zip(BEADS, BEADS[1:]): bar(b0, b1, 0.008, "wood", sides=3)
    RX = BEADS[-1] - Vector((0, 0, 0.03))
    bar(BEADS[-1], RX - Vector((0, 0, 0.022)), 0.005, "iron", sides=4)
    bar(RX - Vector((0.014, 0, 0)), RX + Vector((0.014, 0, 0)), 0.005, "iron", sides=4)
    module("Hip")
    module("Weapon")
    # the long rifle held upright in front (owner's sniper photos): the butt on the ground by the left knee, the long
    # barrel leaning up past the right shoulder through both hands, a brass telescopic sight, a bipod folded by the muzzle
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    B = Vector((0.03, -0.38, 0.0)); B.z = ground + 0.04   # on the ground
    d = ((hr + hl) / 2 - B).normalized(); muz = B + d * 1.58   # owner: taller than him crouching
    fw = Vector((0, -1, 0)); up_ = (fw - d * fw.dot(d)).normalized()          # the rifle's top faces ahead
    sd_ = d.cross(up_).normalized()
    g = B + d * ((hr - B).dot(d))                                              # the grip, at the right fist
    bar(B, g + d * 0.04, 0.036, "wood", sides=6); bar(g + d * 0.04, g + d * 0.48, 0.024, "wood", sides=6)   # stock and fore-end
    bar(g, muz, 0.014, "metal", sides=6); bar(muz - d * 0.1, muz, 0.022, "metal", sides=6)            # barrel and muzzle brake
    s0, s1 = g + d * 0.06 + up_ * 0.05, g + d * 0.42 + up_ * 0.05
    bar(s0, s1, 0.019, "brass", sides=8)                                                                  # the scope
    for e_ in (s0, s1): bar(e_ - d * 0.014, e_ + d * 0.014, 0.028, "brass", sides=8)
    for t in (0.12, 0.36): bar(g + d * t + up_ * 0.012, g + d * t + up_ * 0.035, 0.007, "metal", sides=4)
    bar(g + d * 0.1, g + d * 0.1 + sd_ * 0.045, 0.007, "metal", sides=4)                                 # bolt handle
    bar(g - d * 0.02, g + d * 0.08 - up_ * 0.04, 0.016, "metal", sides=4)                                # magazine
    bp = B + d * 1.12
    for x in (-1, 1):                                                                                     # the folded bipod
        bar(bp, bp - d * 0.3 + sd_ * 0.035 * x - up_ * 0.025, 0.008, "metal", sides=4)
    sl0, sl1 = B + d * 0.2 + sd_ * 0.03, g + d * 0.4 + sd_ * 0.02                                        # the sling down the rifle's left side
    slm = (sl0 + sl1) / 2 + sd_ * 0.03 - up_ * 0.02
    bar(sl0, slm, 0.006, "leather", sides=3); bar(slm, sl1, 0.006, "leather", sides=3)
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **dict(cloak="3e4a32", cloakd="252d1e", cloakl="56644a", mannequin="cfc4b0", mannequind="9a9282",
                    band="8a1a12", bandd="560e0a", bandl="b0342a", glass="1e2a2a", glassl="7a9a90", thread="b8ac88",
                    brassd="4e4020", rust="5a3a24")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    cb = fb["mid"] - fb["fwd"] * 0.06   # about the middle of the head
    def detail(base, p, n, ao):   # detail pass (2026-11-10): the rifle, the kit and the painted details (None: not here)
        x, y, z = p.x, p.y, p.z; s_ = n.dot(PSX_LIGHT)
        def lt(c_, metal=False, grime=0.0): return lit(c_, n, ao, metal, grime, p)
        on_rifle = segd(p, B, muz)[0] < 0.075
        if base == "wood" and min(segd(p, b0, b1)[0] for b0, b1 in zip(BEADS, BEADS[1:])) < 0.012:   # the rosary: round beads, ink between
            t = min((p - b_).length for b_ in BEADS)
            return C["ink"] if t < 0.005 else (C["woodl"] if s_ > 0.3 else C["woodd"])
        if base == "leather" and (segd(p, sl0, slm)[0] < 0.012 or segd(p, slm, sl1)[0] < 0.012):   # the sling, a brass buckle
            return C["brass"] if abs((p - sl0).dot(d) - 0.12) < 0.008 else lt(C["leatherl"] if s_ > 0.4 else C["leather"])
        if on_rifle and base in ("wood", "metal", "brass"):
            t = (p - B).dot(d)
            if base == "brass":   # the scope: dark glass at each end with a glint, its rings, a focus band
                ts, te = (s0 - B).dot(d), (s1 - B).dot(d)
                if t < ts - 0.012 or t > te + 0.012:
                    if abs(n.dot(d)) > 0.6: return C["glassl"] if (p - (s0 if t < ts else s1)).dot(sd_) < -0.006 else C["glass"]
                    return C["ink"]
                if abs(t - ts) < 0.016 or abs(t - te) < 0.016: return C["ink"] if min(abs(t - ts), abs(t - te)) > 0.013 else lt(C["brassl"] if s_ > 0.3 else C["brass"], metal=True)
                if abs(t - (ts + 0.08)) < 0.006: return C["brassd"]
                return lt(C["brassl"] if s_ > 0.5 else (C["brass"] if s_ > -0.2 else C["brassd"]), metal=True)
            if base == "wood":
                if t < 0.014: return C["metall"] if s_ > 0.3 else C["metal"]                                   # the butt plate
                if abs(t - 0.12) < 0.03 and (p - B).dot(sd_) > 0.0 and abs((p - B).dot(up_)) < 0.02: return C["woodd"]   # a cheek-worn hollow
                for tb in ((g - B).dot(d) + 0.3, (g - B).dot(d) + 0.46):                                      # barrel bands
                    if abs(t - tb) < 0.011: return C["ink"] if abs(t - tb) > 0.008 else lt(C["metall"], metal=True)
                return psx_kit(C, "wood", p, n, ao)
            if t > 1.58 - 0.1:   # the muzzle brake: ports
                return C["ink"] if abs(t - 1.53) < 0.012 and abs((p - B).dot(up_)) < 0.01 else lt(C["metall"] if s_ > 0.4 else C["metal"], metal=True)
            if abs(t - (g - B).dot(d) - 0.1) < 0.03 and (p - (B + d * t)).dot(sd_) > 0.02: return C["ironl"] if s_ > 0.2 else C["metall"]   # the bright bolt
            return lt(C["metall"] if s_ > 0.55 else C["metal"], metal=True)
        if base == "metal" and (p - CANT).length < 0.07:   # the water bottle: a felt cover with a stitched seam, a strap
            q = p - CANT; r_ = math.hypot(q.y, q.z)
            if abs(q.y) < 0.006 and q.z > 0: return C["ink"] if abs(q.y) > 0.0035 else lt(C["leather"])
            if abs(n.x) > 0.6:
                if r_ > 0.046: return C["ink"]
                if r_ > 0.04: return C["clothd"]
                return lt(C["clothl"] if s_ > 0.5 else C["cloth"])
            return C["clothd"] if abs(q.x) < 0.003 else lt(C["cloth"], grime=0.2)
        if base == "brass" and (p - CANT).length < 0.1: return C["brassl"] if s_ > 0.4 else C["brass"]
        if base == "iron" and (p - RX).length < 0.07: return C["ironl"] if s_ > 0.4 else C["iron"]
        if base == "cloak" and y > NECK.y and n.y > 0.55:   # an emblem stitched on the back between the shoulders: a cross in a ring of pale thread
            q = Vector((x - NECK.x, 0, z - (NECK.z - 0.2))); r_ = q.length
            if r_ < 0.062:
                if abs(r_ - 0.052) < 0.006: return C["thread"] if s_ > -0.2 else C["thread"].lerp(C["cloakd"], 0.4)
                if (abs(q.x) < 0.0045 and abs(q.z) < 0.04) or (abs(q.z - 0.012) < 0.0045 and abs(q.x) < 0.026): return C["thread"]
        if base in ("puttee", "boots", "breeches") and z < ground + 0.12 and _nz.noise(p * 24 + Vector((2, 2, 9))) > 0.25 - (ground + 0.12 - z) * 4:
            return lt(C["mud"] if _nz.noise(p * 40) > -0.3 else C["putteed"].lerp(C["mud"], 0.5))   # mud where the knee and shin rest on the ground
        if base == "tunic" and y < 0 and 0.972 < z < 1.028 and any(math.hypot(x - hx_, z - 1.0) < 0.004 for hx_ in (0.04, 0.055)): return C["ink"]   # belt holes
        return None
    def extra(base, p, n, ao):
        x, y, z = p.x, p.y, p.z
        r_ = detail(base, p, n, ao)
        if r_ is not None: return r_
        if base == "mannequin":
            q = p - cb; u, f = q.dot(fb["up"]), q.dot(fb["fwd"])
            if (p - cb).length < 0.16 and abs(u - 0.002) < 0.026:   # the red blindfold bound round the eyes
                if abs(u - 0.002) > 0.021: return C["ink"]
                if f > 0.04 and abs(q.dot(fb["ax"])) < 0.012 and (abs(q.dot(fb["ax"])) < 0.0045 or abs(u - 0.006) < 0.0045): return C["thread"]   # a pale cross worked on its front
                if f < -0.02 and abs(q.dot(fb["ax"])) < 0.02: return C["bandd"]                  # the knot at the back
                c = C["bandl"] if u > 0.01 and n.dot(PSX_LIGHT) > 0.3 else (C["bandd"] if math.sin(u * 400 + f * 60) > 0.8 else C["band"])
                return lit(c, n, ao, p=p)
            if (p - cb).length < 0.16 and f < -0.02 and abs(q.dot(fb["ax"])) < 0.016 and -0.08 < u < -0.02:   # its trailing ends
                return C["ink"] if abs(q.dot(fb["ax"])) > 0.012 else lit(C["band"], n, ao, p=p)
            return lit(C["mannequin"] if n.dot(PSX_LIGHT) > 0.0 else C["mannequind"], n, ao, p=p)
        if base in ("cloak", "hood"):   # the cloak and hood: long folds, the hood's rim round the face, a brass clasp, a ragged muddy hem
            q = p - c0
            if base == "hood":
                fwdh = Vector((fb["fwd"].x, fb["fwd"].y, 0)).normalized(); qh = Vector((q.x, q.y, 0))
                ang = math.degrees(qh.angle(fwdh)) if qh.length > 1e-6 else 90
                if ang < 68 and -sh * 0.85 < q.z < sh * 0.8:   # the rim of the opening, its inside in shadow
                    if ang < 56: return lit(C["cloakd"], n, 0.4, p=p)
                    return C["ink"] if ang < 60 else lit(C["cloakl"], n, ao, p=p)
                if abs(q.x) < 0.006 and q.y > 0 and q.z > -sh * 0.5: return C["ink"]           # the seam over the crown
                c = C["cloakd"] if math.sin(math.atan2(q.x, q.y) * 7 + q.z * 20) > 0.6 else C["cloak"]
                return lit(C["cloakl"] if c is C["cloak"] and n.dot(PSX_LIGHT) > 0.62 else c, n, ao, p=p)
            a = math.degrees(math.atan2(x - NECK.x, -(y - NECK.y))); aa = abs(a)
            if z > NECK.z - 0.08 and aa < 60:                                                   # the brass clasp at the throat
                cl = math.hypot(x - NECK.x, z - (NECK.z - 0.05))
                if cl < 0.022: return C["ink"] if cl > 0.017 else (C["brassl"] if z > NECK.z - 0.05 else C["brass"])
            if z < hem + 0.012 + 0.03 * max(0.0, _nz.noise(Vector((a * 0.2, 0, 0)))): return C["ink"] if z < hem + 0.008 else lit(C["cloakd"].lerp(C["mud"], 0.6), n, ao, p=p)
            if aa < 46: return C["ink"] if aa < 44 else lit(C["cloakl"], n, ao, p=p)            # the open front edges
            f = (a / 30.0 + _nz.noise(Vector((a * 0.03, 0, z * 2))) * 0.3) % 1.0                 # long folds from the shoulders
            c = C["cloakd"] if f < 0.22 else (C["cloakl"] if f < 0.32 else C["cloak"])
            if _nz.noise(p * 7 + Vector((3, 3, 3))) > 0.45: c = c.lerp(C["mud"], 0.3)              # stains
            return lit(c, n, ao, grime=0.4 * max(0.0, (0.75 - z) / 0.2), p=p)
        if base in ("puttee", "boots"):   # the kneeling legs: puttees wound down each shin, then the boots
            sx = 1 if x > 0 else -1; K, A, sd2 = legs[sx]
            t = (p - K).dot(sd2) / (A - K).length; beyond = (p - A).dot(sd2)
            if base == "boots" or beyond > -0.015:
                if z < ground + 0.018: return lit(C["sole"], n, ao, p=p)
                if beyond < -0.005: return C["ink"]                                               # the boot top
                return lit(C["bootl"] if n.dot(PSX_LIGHT) > 0.5 else C["boot"], n, ao, grime=0.3, p=p)
            if t < 0.2:   # the pale tape at the top
                return C["ink"] if t < 0.1 or t > 0.17 else lit(C["pale"], n, ao, p=p)
            ax_ = (p - K) - sd2 * (p - K).dot(sd2); side_ = sd2.cross(Vector((0, 0, 1))).normalized()
            fr = (t * 9 + math.atan2(ax_.dot(side_), ax_.z) * 0.25) % 1.0
            c = C["ink"] if fr < 0.16 else (C["putteed"] if fr < 0.34 else C["puttee"])
            return lit(c, n, ao, grime=0.3 * t, p=p)
        if base == "tunic" and y < 0 and 1.1 < z < 1.34:   # rows of ammunition pouches up the chest, inside the stole
            for rz in (1.3, 1.22, 1.14):
                for px_ in (-0.03, 0.03):
                    if abs(x - px_) < 0.025 and abs(z - rz) < 0.034:
                        if abs(x - px_) > 0.02 or abs(z - rz) > 0.029: return C["ink"]
                        if abs(x - px_) < 0.005 and abs(z - rz - 0.012) < 0.005: return C["brassl"]
                        return lit(C["webbingl"] if z > rz + 0.01 else C["webbing"], n, ao, p=p)
        return None
    PAINT = ww1_paint(C, extra=extra, hips=False)

def trench_cleric():
    # Trench Cleric (2026-11-04, fifth Principality figure, class B, 32 mm): an original armoured field priest on the
    # owner's reference images as cues, not a copy of the miniature, the art or any paint job. A tall flat-topped
    # priest's cap in the seat colour over a blank mannequin face (no beard: faces stay blank); an olive steel cuirass
    # of horizontal lames with squared pauldrons (their top lames in the seat colour), a mail fringe at the waist, a belt
    # of pouches, a rope stole across the chest with a small iron cross and a gilt icon on the right shoulder; a long
    # pale vestment to mid-shin, ragged at the hem, with prayer strips down the front; ankle boots. Standing, reading
    # from an open book held up in the left hand, a heavy pistol hanging in the right, a great riveted wooden cross
    # strapped across the back. Painted PSX look (2026-11-10, as the Combat Engineer): a smooth low-poly body, the
    # vestment draped (in its own node, Robe, for its texels), toeless boot hulls; the cuirass (breast and back plates
    # and their lames, side straps, rivets), the plated upper arms, the rope stole, iron cross and gilt icon, the belt,
    # pouches and holster, the mail fringe, the prayer strips, blood and mud on the vestment, the puttees and boots
    # painted (ww1_paint for the legs, sleeves and gloves); the cross's iron bands and rivets and the book's text painted
    # too. Solid only for the silhouette: Helmet (cap fittings), Chest (pauldrons, shrine, mail collar), Back (cross),
    # Weapon (pistol), Weapon2 (book), Robe (vestment), Accent (cap, pauldron tops).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1000; TEX_SIZES = {"Body": 256, "Robe": 256, "Weapon2": 128}
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.03, 1.4))                                         # upright
        human.aim(r, "upperleg01.L", P(0.12, -0.05, 0.5)); human.aim(r, "upperleg02.L", P(0.12, -0.05, 0.5))
        human.aim(r, "lowerleg01.L", P(0.13, -0.05, 0.08)); human.aim(r, "lowerleg02.L", P(0.13, -0.05, 0.08))
        human.aim(r, "foot.L", P(0.15, -0.21, -0.01))
        human.aim(r, "upperleg01.R", P(-0.11, 0.03, 0.5)); human.aim(r, "upperleg02.R", P(-0.11, 0.03, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.12, 0.05, 0.08)); human.aim(r, "lowerleg02.R", P(-0.12, 0.05, 0.08))
        human.aim(r, "foot.R", P(-0.15, -0.1, -0.02))
        human.reach(r, "R", P(-0.24, -0.04, 1.1), P(-0.25, -0.24, 0.96))                 # the pistol hand hanging at the side
        human.reach(r, "L", P(0.25, 0.0, 1.12), P(0.19, -0.3, 1.2))                      # the book held up in front
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}, head="mannequin",
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    import random, bmesh as _bm; rnd = random.Random(1917)
    human_ = bpy.data.objects["human"]
    for v in human_.data.vertices:   # the cuirass's bulk over the chest and back
        if 0.97 < v.co.z < 1.44: v.co += v.normal * 0.014
    def surf(x, z, front=True, out=0.012):   # a point on the body's front (or back) at (x, z), a little proud of it
        d_ = 1 if front else -1
        ok, loc, *_ = human_.ray_cast(Vector((x, -1.0 * d_, z)), Vector((0, d_, 0)))
        return (loc if ok else Vector((x, -0.15 * d_, z))) + Vector((0, -out * d_, 0))
    def obox(c, r_, u_, size, m):   # a box with its own axes (across, along; out = across x along)
        o = box(tuple(c), size, m); o.rotation_mode = 'XYZ'
        o.rotation_euler = Matrix((r_, u_, r_.cross(u_).normalized())).transposed().to_euler(); return o
    toeless_feet()
    module("Robe")
    # the long vestment from the waist to mid-shin, closed, ragged at the hem; in its own node, so it does not take the
    # cuirass's texels
    vest = cloth_cone(0, 0.005, 1.0, 0.3, 0.17, 0.25, "vestment", sy=0.84, ragged=0.07, rnd=rnd, tris=230, thick=0.012, rows=10)
    hem = min(v.co.z for v in vest.data.vertices)
    # the legs inside the closed vestment are never seen: removed (owner: hidden parts go up front)
    bm = _bm.new(); bm.from_mesh(human_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if 0.36 < (c := f.calc_center_median()).z < 0.95 and abs(c.x) < 0.17 and abs(c.y) < 0.16], context='FACES')
    bm.to_mesh(human_.data); bm.free()
    module("Helmet")
    # the cap: a tall drum a little wider at the top, flat-crowned, on a short visor and an iron band; the head under
    # it trimmed away; a brass cross on the front
    heads = [o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name == "mannequin" and len(o.data.vertices) > 40]
    hd_ = max(heads, key=lambda o: max(v.co.z for v in o.data.vertices))
    vs = [v.co for v in hd_.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    c0 = (lo + hi) / 2; rw, rd = (hi.x - lo.x) / 2 + 0.008, (hi.y - lo.y) / 2 + 0.008
    zb = FACE_BOX["mid"].z + 0.045                                                       # the cap's rim, across the brow
    bm = _bm.new(); bm.from_mesh(hd_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z > zb + 0.015], context='FACES')
    bm.to_mesh(hd_.data); bm.free()
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=1.0, radius2=1.1, depth=0.17, location=(c0.x, c0.y + 0.006, zb + 0.085))
    o = bpy.context.object; o.scale = (rw, rd, 1); add(o, "accent")
    bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=1.0, depth=0.024, end_fill_type='NOTHING', location=(c0.x, c0.y + 0.006, zb + 0.008))
    o = bpy.context.object; o.scale = (rw + 0.007, rd + 0.007, 1); add(o, "iron")
    box((c0.x, c0.y - rd - 0.03, zb - 0.002), (rw * 1.5, 0.06, 0.008), "leather", rot=(math.radians(14), 0, 0))   # the visor
    cf = Vector((c0.x, c0.y + 0.006 - rd - 0.006, zb + 0.09))
    box(tuple(cf), (0.014, 0.008, 0.09), "brass"); box(tuple(cf + Vector((0, 0, 0.018))), (0.055, 0.008, 0.014), "brass")
    module("Chest")
    # shoulders (the owner's references): squared, boxy pauldrons: a flat slab over the shoulder in the seat colour, a
    # vertical cap plate on the outside and a second lame stepped down the arm (rivets and edges painted); a mail collar
    # at the neck; a gilt shrine standing on the left pauldron
    shoulders = {}
    for x in (-1, 1):
        ok, sh_, *_ = human_.ray_cast(Vector((0.19 * x, 0.0, 2.0)), Vector((0, 0, -1)))
        if not ok: continue
        shoulders[x] = sh_
        tl = math.radians(10)
        box(tuple(sh_ + Vector((0.04 * x, 0, -0.005))), (0.14, 0.15, 0.085), "olive", rot=(0, x * tl, 0))          # the boxy pauldron
        box(tuple(sh_ + Vector((0.042 * x, 0, 0.05))), (0.16, 0.17, 0.02), "accent", rot=(0, x * tl, 0))           # its slab, in the seat colour
        box(tuple(sh_ + Vector((0.06 * x, 0, -0.068))), (0.11, 0.14, 0.03), "olive", rot=(0, x * math.radians(14), 0))   # the second lame down the arm
        if x > 0:
            sc_ = sh_ + Vector((0.045, 0.04, 0.105)); box(tuple(sc_), (0.05, 0.014, 0.1), "gilt")           # the shrine's body
            box(tuple(sc_ + Vector((-0.032, -0.006, -0.01))), (0.018, 0.01, 0.08), "gilt")                  # its folded wings
            box(tuple(sc_ + Vector((0.032, -0.006, -0.01))), (0.018, 0.01, 0.08), "gilt")
    nz_ = NECK.z if hasattr(NECK, "z") else 1.5
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.125, radius2=0.075, depth=0.07, end_fill_type='NOTHING', location=(0, c0.y + 0.012, nz_ + 0.02))
    o = bpy.context.object; o.scale = (1, 0.92, 1); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "mail")   # the mail collar
    module("Back")
    # the great cross strapped across the back: two planks, leaning with its head over the left shoulder (iron bands and
    # rivets painted), leather straps over the shoulders
    bc_ = surf(0.0, 1.2, front=False, out=0.07); t_ = math.radians(8)
    def CR(lx, lz, out=0.0):   # a point in the cross's own plane (lx across, lz along the upright), out = further back
        return bc_ + Vector((lx * math.cos(t_) + lz * math.sin(t_), out, -lx * math.sin(t_) + lz * math.cos(t_)))
    for (a_, b_), w in ((((0, -0.5), (0, 0.5)), 0.14), (((-0.42, 0.2), (0.42, 0.2)), 0.13)):
        p0, p1 = CR(*a_), CR(*b_); mid = (p0 + p1) / 2; ax = (p1 - p0).normalized()
        obox(mid, Vector((0, 1, 0)).cross(ax).normalized(), ax, (w, (p1 - p0).length, 0.055), "wood")
    for x in (-1, 1):   # straps from the cross over each shoulder
        ok, s_, *_ = human_.ray_cast(Vector((x * 0.15, -0.01, 2.0)), Vector((0, 0, -1)))
        s_ = (s_ if ok else Vector((x * 0.15, -0.01, 1.45))) + Vector((0, 0, 0.02))
        bar(CR(x * 0.06, 0.12, out=-0.025), s_, 0.012, "leather", sides=4)
    module("Weapon")
    # a heavy pistol hanging in the right fist, the barrel along the forearm, the grip back through the fist
    fr, d = FISTS["R"]; f_ = Vector((0, -1, 0)); f_ = (f_ - d * f_.dot(d)).normalized()
    s0 = fr + f_ * 0.035
    bar(s0 - d * 0.04, s0 + d * 0.16, 0.021, "metal", sides=4)                           # the slide and barrel
    bar(s0 + d * 0.16, s0 + d * 0.21, 0.01, "metal", sides=5)                            # the muzzle
    bar(s0 + d * 0.02 - f_ * 0.005, s0 + d * 0.08 - f_ * 0.005, 0.012, "metal", sides=4)  # the magazine ahead of the guard
    bar(fr + f_ * 0.02, fr - f_ * 0.05, 0.017, "wood", sides=4)                          # the grip through the fist
    module("Weapon2")
    # the open book held up on the left hand, its pages tilted up towards the reader (text painted), a red ribbon
    fl, dl = FISTS["L"]
    n_ = Vector((0.15, -0.55, 0.82)).normalized(); r_ = Vector((1, 0, 0)); r_ = (r_ - n_ * r_.dot(n_)).normalized(); u_ = n_.cross(r_).normalized()
    bk = fl + n_ * 0.035 + Vector((0.02, -0.02, 0.0))
    for s in (-1, 1):
        rr = (r_ * math.cos(math.radians(12)) + n_ * math.sin(math.radians(12)) * 1).normalized() if s > 0 else (r_ * math.cos(math.radians(12)) - n_ * math.sin(math.radians(12))).normalized()
        obox(bk + rr * 0.06 * s, rr, u_, (0.12, 0.17, 0.008), "leather")                 # the cover
        obox(bk + rr * 0.056 * s + n_ * 0.012, rr, u_, (0.108, 0.155, 0.016), "parchment")   # the pages
    bar(bk - u_ * 0.07 + n_ * 0.02, bk - u_ * 0.07 + n_ * 0.02 - Vector((0, 0, 0.12)), 0.005, "blood", sides=3)
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **dict(tunic="4a4a36", tunicd="2e2e22", tunicl="62624a",
                    olive="5e6a3c", olived="363d22", olivel="808c58", vest="b4a67e", vestd="7e7456", vestl="d0c49c",
                    rope="7e6c48", roped="45391f", gilt="9a7a32", giltd="5e4a1a", giltl="d0b060", icon="4a1a12",
                    mail="3e4044", maill="62646a", parchment="d4c49a", parchmentd="a89870", mannequin="cfc4b0", mannequind="9a9282",
                    leather="3e2d1f", leatherl="5e4632", strip="cfc2a0", blood="5e140d", bloodd="3a0c08")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    S0, S1 = Vector((0.12, 1.47)), Vector((-0.15, 1.03))   # the rope stole, left shoulder to right hip (x, z), front
    def stole(x, z):
        d_ = S1 - S0; t = max(0.0, min(1.0, (Vector((x, z)) - S0).dot(d_) / d_.length_squared)); return (Vector((x, z)) - (S0 + d_ * t)).length, t
    def rope(dd, along):   # a twisted rope band
        if dd > 0.012: return C["ink"]
        return C["roped"] if (along * 90 + dd * 50) % 1.0 < 0.25 else C["rope"]
    def mail(p, n, ao):
        return C["ink"] if (p.z * 240) % 1.0 < 0.3 and ((p.x + p.y) * 120 + (p.z * 240 // 1) * 0.5) % 1.0 < 0.4 else lit(C["maill"] if n.dot(PSX_LIGHT) > 0.4 else C["mail"], n, ao, metal=True, p=p)
    def blood(p, dense=0.0):
        if _nz.noise(p * 13) > 0.66 - dense: return C["bloodd"] if _nz.noise(p * 40) > 0.3 else C["blood"]
        if _nz.noise(p * 70 + Vector((4, 4, 4))) > 0.72 - dense * 0.5: return C["blood"]
        return None
    def belt(x, y, z, n, ao, p):   # the belt, a brass buckle at the front
        if z < 0.974 or z > 1.02: return C["ink"]
        if y < 0 and abs(x) < 0.024:
            if abs(x) > 0.019 or abs(z - 0.997) > 0.02: return C["ink"]
            return C["dark"] if abs(x) < 0.008 and abs(z - 0.997) < 0.01 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.4 else C["brass"])
        return lit(C["leatherl"] if abs(z - 0.997) < 0.004 else C["leather"], n, ao, p=p)
    def plate(z, n, ao, p, edges):   # olive plate in lames: an ink line at each step, light above it, shadow under it
        c = C["olive"]
        for L_ in edges:
            if abs(z - L_) < 0.005: return C["ink"]
            if 0 < L_ - z < 0.014: c = C["olived"]
            elif 0 < z - L_ < 0.008: c = C["olivel"]
        if c is C["olive"] and n.dot(PSX_LIGHT) > 0.7: c = C["olivel"]
        return lit(c, n, ao, metal=True, p=p)
    LAMES = (1.215, 1.163, 1.111, 1.059)
    def extra(base, p, n, ao):
        x, y, z = p.x, p.y, p.z
        if base == "mannequin":   # the blank face, the neck: smooth pale plaster
            return lit(C["mannequin"] if n.dot(PSX_LIGHT) > 0.0 else C["mannequind"], n, ao, p=p)
        if base == "mail": return mail(p, n, ao)
        if base == "olive":   # the pauldrons: a light top edge, dark undersides, a row of rivets along the top
            if n.z > 0.7: return lit(C["olivel"], n, ao, metal=True, p=p)
            if n.z < -0.5: return lit(C["olived"], n, ao, metal=True, p=p)
            sh_ = shoulders.get(1 if x > 0 else -1)
            if sh_ is not None and abs(z - (sh_.z + 0.025)) < 0.007 and ((y + x) * 40) % 1.0 < 0.3: return C["brassl"]
            if sh_ is not None and abs(z - (sh_.z - 0.05)) < 0.006: return C["ink"]
            if sh_ is not None and x < 0 and n.y < -0.6 and abs(x + 0.17) < 0.03 and abs(z - (sh_.z - 0.02)) < 0.035:   # the gilt icon hung on the right pauldron
                if abs(x + 0.17) > 0.024 or abs(z - (sh_.z - 0.02)) > 0.03: return C["ink"]
                if abs(x + 0.17) > 0.016 or abs(z - (sh_.z - 0.02)) > 0.022: return C["giltl"] if z > sh_.z - 0.02 else C["gilt"]
                return C["icon"] if math.hypot(x + 0.17, z - (sh_.z - 0.005)) > 0.007 else C["giltl"]   # a halo, no face
            return lit(C["olive"], n, ao, metal=True, p=p)
        if base == "gilt":   # the shrine: gilt with an ink edge, a dark painted icon in the middle
            sh_ = shoulders.get(1); sc_ = sh_ + Vector((0.045, 0.04, 0.105)) if sh_ is not None else p
            q = p - sc_
            if n.y < -0.6 and abs(q.x) < 0.017 and abs(q.z) < 0.04: return C["ink"] if abs(q.x) > 0.013 or abs(q.z) > 0.036 else (C["giltl"] if q.z > 0.02 else C["icon"])
            return lit(C["giltl"] if n.dot(PSX_LIGHT) > 0.5 else C["gilt"], n, ao, metal=True, p=p)
        if base == "wood" and (p - bc_).length < 0.6 and y > bc_.y - 0.05:   # the great cross: grain, iron bands at the ends, rivets down the upright
            q = p - bc_; lx = q.x * math.cos(t_) - q.z * math.sin(t_); lz = q.x * math.sin(t_) + q.z * math.cos(t_)
            on_bar = abs(lz - 0.2) < 0.066
            e = min(0.5 - abs(lz), 0.42 - abs(lx) if on_bar else 1.0)
            if e < 0.06: return C["ink"] if abs(e - 0.03) > 0.024 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
            if abs(lx) < 0.07 and q.y > 0.02:
                for rz in (-0.38, -0.2, 0.4):
                    rr = math.hypot(lx, lz - rz)
                    if rr < 0.012: return C["ink"] if rr > 0.008 else C["ironl"]
            g = math.sin((lx if on_bar and abs(lx) > 0.07 else lz) * 70 + _nz.noise(p * 10) * 3)
            return lit(C["woodd"] if g > 0.85 else (C["woodl"] if n.dot(PSX_LIGHT) > 0.6 else C["wood"]), n, ao, grime=0.15, p=p)
        if base == "parchment":   # the open pages: lines of text, a red initial
            q = p - bk; uu, rx = q.dot(u_), q.dot(r_)
            if n.dot(n_) > 0.5:
                if 0.006 < abs(rx) < 0.1 and -0.065 < uu < 0.065 and (uu * 70) % 1.0 < 0.35: return C["blood"] if abs(rx) < 0.02 and uu > 0.05 else C["ink"]
                return C["parchmentd"] if abs(rx) < 0.008 else C["parchment"]
            return lit(C["parchmentd"], n, ao, p=p)
        if base == "leather" and (p - bk).length < 0.14: return lit(C["leatherl"] if n.dot(PSX_LIGHT) > 0.5 else C["leather"], n, ao, p=p)   # the book's cover
        if base == "vestment":   # the vestment: belt and pouches at the top, the mail fringe, prayer strips, the holster, blood, a ragged muddy hem
            a = math.degrees(math.atan2(x, -y)); aa = abs(a)
            if z > 0.974: return belt(x, y, z, n, ao, p)
            if y < 0:
                for px_ in (-0.15, -0.085, 0.085, 0.15):   # the pouches hung on the belt
                    if abs(x - px_) < 0.03 and 0.91 < z < 0.976:
                        if abs(x - px_) > 0.025 or z < 0.916: return C["ink"]
                        if abs(z - 0.952) < 0.003: return C["ink"]
                        if abs(x - px_) < 0.005 and abs(z - 0.945) < 0.005: return C["brassl"]
                        return lit(C["leatherl"] if z > 0.952 else C["leather"], n, ao, p=p)
            if x < -0.14 and n.x < -0.3 and -0.06 < y < 0.05 and 0.84 < z < 0.976:   # the empty holster on the right hip
                if z < 0.846 or y < -0.054 or y > 0.044: return C["ink"]
                if abs(z - 0.94) < 0.003: return C["ink"]
                return lit(C["leatherl"] if z > 0.94 else C["leather"], n, ao, p=p)
            if aa < 112:   # the mail fringe, scalloped
                fz = 0.885 - (0.014 if int((a + 112) / 9) % 2 else 0.0)
                if z > fz:
                    if z < fz + 0.006: return C["ink"]
                    return mail(p, n, ao)
            if y < 0:
                for sx_, z0, h_ in ((-0.08, 0.62, 0.36), (0.0, 0.58, 0.4), (0.08, 0.62, 0.32)):   # prayer strips, a wax seal at the top
                    if abs(x - sx_) < 0.026 and z0 - h_ / 2 < z < min(z0 + h_ / 2, 0.875):
                        if abs(x - sx_) > 0.022 or z < z0 - h_ / 2 + 0.005: return lit(C["vestd"], n, ao, p=p)
                        zt = min(z0 + h_ / 2, 0.875)
                        if math.hypot(x - sx_, z - (zt - 0.03)) < 0.016: return C["blood"] if math.hypot(x - sx_, z - (zt - 0.03)) < 0.012 else C["bloodd"]
                        if abs(x - sx_) < 0.013 and (z * 70) % 1.0 < 0.22 and _nz.noise(p * 150) > 0.0: return C["vestd"]   # scripture, faded
                        return lit(C["strip"], n, ao, p=p)
            if z < hem + 0.01 + 0.02 * max(0.0, _nz.noise(Vector((a * 0.2, 0, 0)))): return C["ink"] if z < hem + 0.006 else lit(C["vestd"].lerp(C["mud"], 0.6), n, ao, p=p)
            b_ = blood(p, 0.1 * max(0.0, (0.7 - z) / 0.4))
            if b_ is not None: return b_
            f = (a / 24.0 + _nz.noise(Vector((a * 0.03, 0, z * 2))) * 0.3) % 1.0
            c = C["vestd"] if f < 0.12 else (C["vestl"] if f < 0.2 and n.dot(PSX_LIGHT) > 0.2 else C["vest"])
            return lit(c, n, ao, grime=0.45 * max(0.0, (0.6 - z) / 0.3), p=p)
        if base != "tunic": return None
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.08 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0
        if on_arm:   # a plated upper arm (lames, two iron bands), a round couter at the elbow; the forearm's sleeve is ww1_paint's
            if along > 1.12: return None
            if abs(along - 1.0) < 0.12:
                if abs(along - 1.0) > 0.1: return C["ink"]
                return lit(C["olivel"] if n.dot(PSX_LIGHT) > 0.5 else C["olive"], n, ao, metal=True, p=p)
            for b_ in (0.35, 0.8):
                if abs(along - b_) < 0.05: return C["ink"] if abs(along - b_) > 0.04 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
            if (along * 9) % 1.0 < 0.12: return C["ink"]
            return lit(C["olivel"] if (along * 9) % 1.0 < 0.3 else (C["olived"] if (along * 9) % 1.0 > 0.85 else C["olive"]), n, ao, metal=True, p=p)
        front = y < 0
        if z < 0.974: return lit(C["tunicd"], n, ao, p=p)                                   # under the vestment's top
        if z < 1.02: return belt(x, y, z, n, ao, p)
        if z > nz_ - 0.03: return mail(p, n, ao)                                              # under the mail collar
        if front:
            dd, t = stole(x, z)
            if dd < 0.016: return rope(dd, t)
            if abs(x + 0.01) < 0.01 and 1.12 < z < 1.2 or abs(x + 0.01) < 0.028 and abs(z - 1.175) < 0.009:   # the iron cross hung from it
                if abs(x + 0.01) > 0.007 and abs(z - 1.175) > 0.006 or z < 1.124: return C["ink"]
                return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.3 else C["iron"], n, ao, metal=True, p=p)
            rv = psx_dot(p, [Vector((sx * 0.12, y, rz + 0.026)) for sx in (-1, 1) for rz in LAMES], C, 0.0065)
            if rv is not None: return rv
            top_ = 1.43 - 3.0 * x * x
            if z > top_ - 0.012: return C["ink"] if z < top_ - 0.004 else lit(C["olivel"], n, ao, metal=True, p=p)   # the rolled neckline
            if abs(x) < 0.006 and z > LAMES[0]: return lit(C["olivel"], n, ao, metal=True, p=p)                      # the ridge
            return plate(z, n, ao, p, LAMES)
        if abs(n.x) > 0.8:   # the side straps joining breast and back plates
            for sz in (1.15, 1.3):
                if abs(z - sz) < 0.014: return C["ink"] if abs(z - sz) > 0.01 else (C["brass"] if abs(y) < 0.008 else lit(C["leather"], n, ao, p=p))
            return lit(C["tunicd"], n, ao, p=p)
        if abs(x) < 0.005 and z < LAMES[0]: return C["ink"]
        return plate(z, n, ao, p, LAMES)
    PAINT = ww1_paint(C, extra=extra, hips=False)

def combat_engineer():
    # Combat Engineer (2026-11-04, sixth Principality figure, class A, 25 mm): an original armoured trench sapper on the
    # owner's reference images as cues, not a copy of any miniature, artwork or photograph. PSX / PS2-era trial
    # (2026-11-10, the owner: clothes painted on the texture, like Silent Hill 3 or Shenmue): a smooth low-poly body,
    # a little bulkier where the greatcoat is, with a draped coat skirt, and everything else painted texel by texel
    # (PAINT) by body region: greatcoat with collar, seams, buttons, pockets and belt, cross strap, breastplate with lames
    # and rivets, trousers with creases, puttees, laced boots, knee plates, scarf, gloves, with hand-drawn dark outlines,
    # hard shading bands and grime. Solid shapes only where the silhouette needs them: the closed sallet in the seat
    # colour (Accent) with a brass cross on the brow (Helmet), two plain pauldrons (Chest), the pack and bedroll (Back),
    # the submachine gun with a drum magazine levelled forward in both hands (Weapon).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1500; TEX_SIZES = {"Body": 256, "Back": 128, "Weapon": 128, "Hip": 128, "Chest": 128}
    def pose(r):
        human.aim(r, "spine03", P(0, -0.09, 1.4))                                          # leaning into the stride
        human.aim(r, "upperleg01.L", P(0.11, -0.16, 0.5)); human.aim(r, "upperleg02.L", P(0.11, -0.16, 0.5))
        human.aim(r, "lowerleg01.L", P(0.12, -0.14, 0.08)); human.aim(r, "lowerleg02.L", P(0.12, -0.14, 0.08))
        human.aim(r, "foot.L", P(0.12, -0.3, -0.01))
        human.aim(r, "upperleg01.R", P(-0.1, 0.07, 0.5)); human.aim(r, "upperleg02.R", P(-0.1, 0.07, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.11, 0.22, 0.13)); human.aim(r, "lowerleg02.R", P(-0.11, 0.22, 0.13))
        human.aim(r, "foot.R", P(-0.11, 0.12, -0.02))                                      # pushing off the back foot
        human.reach(r, "R", P(-0.25, -0.07, 1.13), P(-0.1, -0.3, 1.22))                  # the right fist on the pistol grip, by the chest
        human.reach(r, "L", P(0.22, -0.15, 1.13), P(0.07, -0.55, 1.25))                  # the left hand out on the foregrip: aiming
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "trousers", "torso": "greatcoat"}, head="mannequin",
                                mitts="leather", shape="male-young-averagemuscle-maxweight")
    import bmesh as _bm
    from mathutils import noise as _nz
    human_ = bpy.data.objects["human"]
    for v in human_.data.vertices:   # the greatcoat's bulk over the chest and back
        if 0.95 < v.co.z < 1.44: v.co += v.normal * 0.012
    for v in human_.data.vertices: # bulkier legs (the owner, 2026-11-10): baggy trousers, fullest at the thigh, tapering to the boot
        if 0.1 < v.co.z < 0.97: v.co += v.normal * (0.016 * math.sin(math.pi * min(1.0, (v.co.z - 0.1) / 0.87)) ** 1.5 - 0.004)   # breeches: fuller at the thigh, slim calf like the Trench Pilgrim
    # each foot swapped for its convex hull, a toeless boot shape (the boots themselves are painted on)
    for sx in (1, -1):
        bm = _bm.new()
        for v in human_.data.vertices:
            if v.co.z < 0.1 and v.co.x * sx > 0: bm.verts.new(v.co + Vector((0, 0, 0)))
        r_ = _bm.ops.convex_hull(bm, input=bm.verts)
        _bm.ops.delete(bm, geom=[g for g in r_["geom_interior"] if isinstance(g, _bm.types.BMVert)], context='VERTS')
        _bm.ops.dissolve_limit(bm, angle_limit=math.radians(8), verts=bm.verts, edges=bm.edges)
        me = bpy.data.meshes.new("boot"); bm.to_mesh(me); bm.free()
        for f in me.polygons: f.use_smooth = True
        o = bpy.data.objects.new("boot", me); scn.collection.objects.link(o); add(o, "boots")
    bm = _bm.new(); bm.from_mesh(human_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if max(v.co.z for v in f.verts) < 0.095], context='FACES')
    bm.to_mesh(human_.data); bm.free()
    # landmarks for the painting, from the posed body
    shoulders = {}
    for s, x in (("L", 0.19), ("R", -0.19)):
        ok, sh_, *_ = human_.ray_cast(Vector((x, 0.0, 2.0)), Vector((0, 0, -1)))
        shoulders[s] = (sh_ if ok else Vector((x, 0.0, 1.45))) - Vector((0, 0, 0.05))
    elbows = {"L": Vector((0.22, -0.15, 1.13)), "R": Vector((-0.25, -0.07, 1.13))}
    wrists = {s: FISTS[s][0] - FISTS[s][1] * 0.045 for s in ("L", "R")}
    knees = []
    for x in (0.11, -0.11):
        ok, k_, nrm, _ = human_.ray_cast(Vector((x, -1.0, 0.5)), Vector((0, 1, 0)))
        if ok: knees.append((k_, nrm))
    # the greatcoat's skirt, belt to knee, open at the front for the stride
    skirt = cloth_cone(0, 0.0, 1.02, 0.46, 0.165, 0.175, "coat", sy=0.8, keep=FRONT(16), tris=220, thick=0.012, rows=8)
    hem = min(v.co.z for v in skirt.data.vertices)
    print(f"ENGINEER neck={NECK} shoulders={shoulders} wrists={wrists} knees={[k for k, _ in knees]} hem={hem:.3f}")

    module("Helmet")
    c0 = sallet(closed=True)   # the seat-colour closed sallet, slit across the eyes
    sal = next(o for o in MODULES["Accent"] if o.name.startswith("sallet"))
    for o in MODULES["Body"]:   # the neck thickened into a wound scarf filling the gap under the helmet's tail
        if o.data.materials and o.data.materials[0].name == "mannequin":
            nc = sum((v.co for v in o.data.vertices), Vector()) / len(o.data.vertices)
            for v in o.data.vertices: v.co = Vector((nc.x + (v.co.x - nc.x) * 1.75, nc.y + (v.co.y - nc.y) * 1.6, v.co.z))
    zs = FACE_BOX["mid"].z + 0.004
    def brow(dx, z):   # a point on the sallet's front at (dx across, z), a little proud of it
        ok, at, *_ = sal.ray_cast(Vector((c0.x + dx, c0.y - 1.0, z)), Vector((0, 1, 0)))
        return (at if ok else Vector((c0.x + dx, c0.y - 0.12, z))) + Vector((0, -0.007, 0))
    bar(brow(0, zs + 0.022), brow(0, zs + 0.088), 0.0065, "brass", sides=4)                  # the brass cross on the brow
    bar(brow(-0.024, zs + 0.064), brow(0.024, zs + 0.064), 0.0065, "brass", sides=4)
    module("Chest")
    def shell(c, d, r, L, ang=105, rows=3, taper=0.88):   # a plain curved plate over the top of an arm along d, radius r, length L
        d = d.normalized(); side = d.cross(Vector((0, 0, 1))).normalized(); up = side.cross(d).normalized(); n = 7
        vs = [tuple(c + d * (L * j / rows) + (side * math.sin(math.radians(-ang + 2 * ang * i / (n - 1))) + up * math.cos(math.radians(-ang + 2 * ang * i / (n - 1)))) * (r * (1 - (1 - taper) * j / rows))) for j in range(rows + 1) for i in range(n)]
        fs = [(j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i) for j in range(rows) for i in range(n - 1)]
        me = bpy.data.meshes.new("shell"); me.from_pydata(vs, [], fs)
        bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces)
        f0 = bm.faces[0]
        if f0.normal.dot(f0.calc_center_median() - c) < 0: _bm.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free()
        for p_ in me.polygons: p_.use_smooth = True
        o = bpy.data.objects.new("shell", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012
        return add(o, "engplate")
    SHELLS = []   # (start, direction, radius, length) of each pauldron, for its painted rim and rivets
    for s, x in (("L", 1), ("R", -1)):   # the pauldrons: one curved cap over each shoulder, the left one bigger
        sh_ = shoulders[s] + Vector((0, 0, 0.05)); ad = (elbows[s] - sh_).normalized(); k = 1.0 if x > 0 else 0.85
        shell(sh_ + Vector((0.02 * x, 0, -0.05 * k)) - ad * 0.05, ad, 0.08 * k, 0.16 * k)
        SHELLS.append((sh_ + Vector((0.02 * x, 0, -0.05 * k)) - ad * 0.05, ad, 0.08 * k, 0.16 * k))
    module("Back")
    box((0.0, 0.11, 1.2), (0.22, 0.12, 0.26), "leather")                                     # the pack
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.05, depth=0.27, location=(0.0, 0.11, 1.375), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "cloth")                                                         # the bedroll on top
    # detail pass (2026-11-10): the entrenching spade strapped slantwise across the pack, a coil of wire hung on its side
    SP0, SP1 = Vector((0.085, 0.182, 1.1)), Vector((-0.07, 0.182, 1.31))                      # the spade: blade end, grip end
    spd = (SP1 - SP0).normalized()
    bar(SP0 + spd * 0.05, SP1, 0.011, "wood", sides=5)
    box(tuple(SP0 - spd * 0.005), (0.07, 0.01, 0.1), "iron", rot=(0, math.atan2(spd.x, spd.z), 0))
    bar(SP1 - Vector((0.025, 0, -0.012)), SP1 + Vector((0.025, 0, 0.012)), 0.009, "wood", sides=4)   # the T grip
    COIL = Vector((-0.128, 0.11, 1.19))
    bpy.ops.mesh.primitive_torus_add(major_radius=0.052, minor_radius=0.01, major_segments=12, minor_segments=4, location=tuple(COIL), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "iron")
    module("Hip")
    skirt.data.update(); bpy.context.view_layer.update()
    def surf(o_, d_):   # the outermost point of the body or the coat skirt seen from o_ along d_ (+ the skirt's thickness)
        best = None
        for ob in (human_, skirt):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * 0.01
    CANT = surf(Vector((1.0, 0.03, 0.86)), Vector((-1, 0, 0))) + Vector((0.017, 0, 0))     # the water bottle on the left hip
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.048, depth=0.034, location=tuple(CANT), rotation=(0, math.pi / 2, 0))
    o = bpy.context.object; o.scale = (1, 0.8, 1); add(o, "cloth")
    bar(CANT + Vector((0, 0, 0.034)), CANT + Vector((0, 0, 0.056)), 0.011, "brass", sides=5)  # its cap
    SATC = surf(Vector((-1.0, 0.05, 0.875)), Vector((1, 0, 0))) + Vector((-0.0275, 0, 0))   # the demolition satchel on the right hip
    box(tuple(SATC), (0.055, 0.12, 0.1), "webbing")
    belt_r = surf(Vector((-1.0, 0.05, 1.0)), Vector((1, 0, 0)))
    bar(SATC + Vector((0.0, 0.0, 0.045)), belt_r, 0.009, "leather", sides=4)                # its strap up to the belt
    GREN = [surf(Vector((gx, -1.0, 0.99)), Vector((0, 1, 0))) for gx in (-0.07, -0.115)]  # two stick grenades tucked in the belt
    for g_ in GREN:
        a_, b_ = g_ + Vector((0.0, -0.01, -0.07)), g_ + Vector((0.0, -0.022, 0.05))
        bar(a_, b_, 0.008, "wood", sides=4)
        bar(b_, b_ + Vector((0, -0.004, 0.05)), 0.021, "metal", sides=6)
    POUCH = surf(Vector((0.1, -1.0, 0.985)), Vector((0, 1, 0))) + Vector((0, -0.016, 0))   # a drum pouch on the belt, left front
    box(tuple(POUCH), (0.075, 0.035, 0.07), "webbing")
    module("Weapon")
    # a submachine gun levelled forward, both hands on it: the right fist on the pistol grip, the left on the foregrip; a
    # wooden butt back against the right shoulder, a receiver, a perforated barrel jacket with cooling bands, a drum
    # magazine under the receiver
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    u = (hl - hr).normalized(); down = Vector((0, 0, -1))
    butt, rec, muz = hr - u * 0.24, hr + u * 0.04, hl + u * 0.34
    bar(butt, rec, 0.022, "wood", sides=6)
    bar(rec, hl + u * 0.1, 0.028, "metal", sides=6)
    bar(hl + u * 0.1, muz, 0.021, "metal", sides=8)
    for t in (0.14, 0.21, 0.28): bar(hl + u * t, hl + u * (t + 0.02), 0.026, "dark", sides=6)   # cooling bands
    bar(muz, muz + u * 0.04, 0.012, "iron", sides=5)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.05, depth=0.045, location=tuple(rec + u * 0.05 + down * 0.07), rotation=(0, math.pi / 2, 0))
    add(bpy.context.object, "metal")                                                                         # the drum
    bar(hr + u * 0.0, hr + down * 0.07 + u * 0.0, 0.014, "wood", sides=5)                                    # the pistol grip
    bar(hl, hl + down * 0.07, 0.014, "wood", sides=5)                                                        # the foregrip
    module("Body")

    # ---- the paint (PSX style): flat colours, dark outlines, hard light bands, grime ----
    def hx(h): return Vector(tuple(min(1.0, int(h[i:i + 2], 16) / 255 * 1.25) for i in (0, 2, 4)))   # lifted a little: the game's light is dim
    C = {k: hx(v) for k, v in dict(
        coat="4b4f3b", coatd="2d3024", coatl="666a52", plate="4e524d", plated="2a2c28", platel="777a71",
        leather="3e2d1f", leatherl="5e4632", brass="7a6533", brassl="a48d52", trousers="3d3a2f", troused="27251d",
        puttee="4f4a36", putteed="302d20", boot="2a241d", bootl="463c30", sole="151310", lace="6a5e48",
        scarf="69644f", scarfd="433f2f", glove="30261c", glovel="4c3d2e", ink="14120e", mud="3a3125", blood="3b1913",
        wood="4f3a26", metal="3b3c3a", cloth="56503e", dark="191714", iron="4b4c4d", engplate="5d605a",
        woodd="35261a", woodl="6a4e32", metall="5c5e5c", irond="2c2d2e", ironl="6e7072", rust="5a3a24", rustd="3e2818",
        clothd="38342a", clothl="6e6650", webbing="56503a", webbingd="38331f", webbingl="6c6549", bone="b8ac90",
        patch="5a2a1e", oil="1c1a16", gren="44483a", grend="2a2d24").items()}
    LIGHT = Vector((-0.35, -0.55, 0.76)).normalized()
    def segd(p, a, b):   # distance from p to segment ab, and how far along it (0..1)
        ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)); return (p - (a + ab * t)).length, t
    def lit(c, n, ao, metal=False, grime=0.0, p=None):   # painted light and shade: three hard bands, a hot metal glint
        s = n.dot(LIGHT)
        k = 1.2 if s > 0.62 else (1.0 if s > 0.0 else 0.74)
        if metal and s > 0.86: k = 1.55
        k *= 0.7 if ao < 0.5 else (0.86 if ao < 0.75 else 1.0)
        if p is not None:
            g = _nz.noise(p * 34.0)
            k *= 1.0 + 0.04 * g                                                          # cloth mottling
            if grime > 0 and _nz.noise(p * 9.0 + Vector((5, 5, 5))) > 0.5 - grime: c = c.lerp(C["mud"], 0.55)
        return Vector([min(1.0, v * k) for v in c])
    def arm_of(p):   # (side, distance along the arm 0 shoulder .. 2 wrist, distance to its axis) of the nearer arm
        best = None
        for s in ("L", "R"):
            d1, t1 = segd(p, shoulders[s], elbows[s]); d2, t2 = segd(p, elbows[s], wrists[s])
            d, t = (d1, t1) if d1 < d2 else (d2, 1 + t2)
            if best is None or d < best[2]: best = (s, t, d)
        return best
    def rivet(p, pts, r=0.0075):   # a brass rivet: ink rim, light top-left
        for q in pts:
            e = (p - q); dd = e.length
            if dd < r: return C["brassl"] if (e.z > 0.002 and e.x < 0) else C["brass"]
            if dd < r + 0.004: return C["ink"]
        return None
    # the cross strap from the left shoulder to the right hip, front and back (x across, z up)
    S0, S1 = Vector((0.13, 1.47)), Vector((-0.17, 0.98))
    def strap(x, z):
        d_ = S1 - S0; t = max(0.0, min(1.0, (Vector((x, z)) - S0).dot(d_) / d_.length_squared)); return (Vector((x, z)) - (S0 + d_ * t)).length
    nz_ = NECK.z
    # detail pass (2026-11-10): the solid kit painted part by part, a stitched sapper's patch on the left sleeve
    ok_, PATCH, *_ = human_.ray_cast(shoulders["L"] + (elbows["L"] - shoulders["L"]) * 0.4 + Vector((0.6, 0, 0)), Vector((-1, 0, 0)))
    if not ok_: PATCH = Vector((9, 9, 9))
    DRUM = rec + u * 0.05 + down * 0.07
    def edge(q, h, m1, m2):   # in a box of half-sizes h, local point q: 2 = on an edge (within m1), 1 = near one (within m2)
        e1 = sum(1 for k in range(3) if h[k] - abs(q[k]) < m1); e2 = sum(1 for k in range(3) if h[k] - abs(q[k]) < m2)
        return 2 if e1 >= 2 else (1 if e2 >= 2 else 0)
    def kit(base, p, n, ao):   # None: not one of these parts
        x, y, z = p.x, p.y, p.z
        s_ = n.dot(LIGHT)
        def lt(c, metal=False, grime=0.0): return lit(c, n, ao, metal, grime, p)
        def wood(along):   # straight grain along the part, a lighter top face
            g = math.sin(along * 160 + _nz.noise(p * 9) * 2.0)
            return lt(C["woodd"] if g > 0.82 else (C["woodl"] if s_ > 0.62 else C["wood"]), grime=0.1)
        if y > 0.15 and (p - (SP0 + SP1) / 2).length < 0.17 and base in ("wood", "iron"):   # the spade on the pack
            q = p - (SP0 - spd * 0.005); al = q.dot(spd); ac = q.dot(Vector((spd.z, 0, -spd.x)))
            if base == "wood":
                if abs((p - SP0).dot(spd) - 0.13) < 0.012: return C["ink"] if abs((p - SP0).dot(spd) - 0.13) > 0.009 else lt(C["leather"])   # its strap
                return wood((p - SP0).dot(spd))
            if n.y < -0.5: return C["dark"]
            if abs(ac) > 0.031 or al < -0.046 or al > 0.047: return C["ink"]
            if abs(ac) > 0.025 or al < -0.039: return C["ironl"]                                # the worn bright edge
            if _nz.noise(p * 24) > 0.38: return lt(C["rust"] if _nz.noise(p * 60) > -0.2 else C["rustd"])
            if abs(ac) < 0.004 and al > 0.0: return C["irond"]                                  # the pressed rib up to the socket
            return lt(C["iron"], metal=True)
        if base == "iron" and (p - COIL).length < 0.08:   # the coiled wire: wound strands slanting round the coil, a rusty sheen
            q = p - COIL; ang = math.atan2(q.z, q.y); rr = math.hypot(q.y, q.z) - 0.052
            fr = (ang * 9 / math.pi + math.atan2(q.x, rr) * 0.5) % 1.0
            return C["irond"] if fr < 0.3 else lt(C["rust"] if fr > 0.8 else (C["ironl"] if s_ > 0.4 else C["iron"]), metal=True)
        if base == "cloth" and (p - CANT).length < 0.07:   # the felt-covered water bottle, a seam round its rim
            q = p - CANT; r = math.hypot(q.y / 0.8, q.z)
            if abs(n.x) > 0.6:
                if r > 0.045: return C["ink"]
                if r > 0.039: return C["clothd"]
                return lt(C["clothl"] if s_ > 0.5 else C["cloth"])
            if abs(q.x) < 0.003: return C["clothd"]
            return lt(C["clothl"] if s_ > 0.62 else C["cloth"], grime=0.15)
        if base == "webbing" and (p - SATC).length < 0.1:   # the demolition satchel: flap, two straps with buckles, a charge mark
            q = p - SATC; e_ = edge(q, (0.0275, 0.06, 0.05), 0.004, 0.009)
            if e_ == 2: return C["ink"]
            if abs(q.z - 0.012) < 0.0035 and q.x < 0.02: return C["ink"]                        # the flap's edge
            for sy_ in (-0.03, 0.03):
                if abs(q.y - sy_) < 0.009:
                    if abs(q.y - sy_) > 0.0065: return C["ink"]
                    if abs(q.z - 0.0) < 0.008: return C["brassl"] if abs(q.z) > 0.004 and s_ > 0.0 else C["brass"]
                    return lt(C["webbingd"])
            if n.x < -0.6 and abs(q.y) < 0.016 and -0.042 < q.z < -0.01 and abs(q.y) + abs(q.z + 0.026) < 0.016: return C["patch"]   # a stencilled charge diamond
            if e_ == 1: return C["webbingl"]                                                    # stitched edge
            return lt(C["webbingl"] if q.z > 0.012 and s_ > 0.3 else C["webbing"], grime=0.2)
        if base == "webbing" and (p - POUCH).length < 0.07:   # the drum pouch: flap, a brass stud
            q = p - POUCH; e_ = edge(q, (0.0375, 0.0175, 0.035), 0.004, 0.009)
            if e_ == 2: return C["ink"]
            if abs(q.z - 0.012) < 0.0035: return C["ink"]
            if math.hypot(q.x, q.z - 0.004) < 0.006 and q.y < 0: return C["brassl"] if q.z > 0.004 else C["brass"]
            if e_ == 1: return C["webbingl"]
            return lt(C["webbingl"] if q.z > 0.012 else C["webbing"])
        for g_ in GREN:   # the stick grenades: grey-green heads with a pale band, wooden handles, a brass cap
            hd = g_ + Vector((0.0, -0.022, 0.05))
            if base == "metal" and (p - hd).length < 0.07:
                t_ = p.z - hd.z
                if t_ < 0.004 or t_ > 0.046: return C["ink"] if abs(n.z) < 0.6 else lt(C["grend"])
                if abs(t_ - 0.016) < 0.005: return C["bone"] if s_ > -0.2 else C["webbingd"]
                return lt(C["gren"] if s_ > 0.0 else C["grend"], metal=True)
            if base == "wood" and (p - g_).length < 0.1 and p.z < hd.z:
                if p.z < g_.z - 0.064: return C["brass"]
                return wood(p.z)
        if base in ("wood", "metal", "dark", "iron") and min((p - hr).length, (p - hl).length, (p - muz).length) < 0.45 and y < 0.0:   # the submachine gun
            al = (p - hr).dot(u)
            if base == "wood": return wood(al)
            if base == "dark": return C["ink"] if s_ < 0.3 else C["metall"]
            if base == "metal" and (p - DRUM).length < 0.06:   # the drum: a rim, a winding key in the middle
                q = p - DRUM; r = (q - u * q.dot(u)).length
                if abs(q.dot(u)) > 0.018:
                    if r > 0.044: return C["ink"]
                    if r < 0.012: return C["brassl"] if r < 0.006 else C["ink"]
                    return lt(C["metall"] if s_ > 0.4 else C["metal"], metal=True)
                return C["ink"] if abs(q.dot(u)) < 0.003 else lt(C["metal"], metal=True)
            if base == "metal" and al > (hl - hr).dot(u) + 0.1:   # the perforated barrel jacket: rows of cooling holes
                side_g = u.cross(Vector((0, 0, 1))).normalized(); ang = math.atan2((p - hr).dot(side_g), p.z - (hr.z + u.z * al))
                if (al * 55) % 1.0 < 0.38 and (ang * 3 / math.pi) % 1.0 < 0.4: return C["ink"]
                return lt(C["metall"] if s_ > 0.5 else C["metal"], metal=True)
            if base == "metal" and abs(al - 0.07) < 0.03 and p.z > hr.z + 0.012: return C["ink"] if abs(al - 0.07) < 0.022 else C["metall"]   # the ejection slot
            return lt(C["metall"] if s_ > 0.6 else C[base], metal=True)
        if base == "engplate":   # the pauldrons: a rolled rim, a lame line, rivets
            for c_, d_, r_, L_ in SHELLS:
                q = p - c_; t = q.dot(d_)
                if not -0.02 < t < L_ + 0.02: continue
                sd = d_.cross(Vector((0, 0, 1))).normalized(); up_ = sd.cross(d_).normalized()
                a = math.degrees(math.atan2(q.dot(sd), q.dot(up_)))
                arc = r_ * math.radians(105 - abs(a)); ends = min(t, L_ - t)
                if (q - d_ * t).length > r_ * 1.4: continue
                if min(arc, ends) < 0.005: return C["ink"]
                for a0, t0 in ((a0, t0) for a0 in (-80, 80) for t0 in (0.3, 0.7)):
                    if math.hypot(r_ * math.radians(a - a0), (t / L_ - t0) * L_) < 0.0065: return C["brassl"] if a * a0 > 0 and abs(a) < abs(a0) else C["brass"]
                    if math.hypot(r_ * math.radians(a - a0), (t / L_ - t0) * L_) < 0.0095: return C["ink"]
                if min(arc, ends) < 0.012: return lt(C["platel"], metal=True)
                if abs(t - L_ * 0.5) < 0.003: return C["ink"]
                if 0 < t - L_ * 0.5 < 0.012: return lt(C["plated"], metal=True)
                c = C["platel"] if n.z > 0.85 else (C["plated"] if n.z < -0.2 else C["engplate"])
                if _nz.noise(p * 30) > 0.55: c = C["rust"]
                return lt(c, metal=True)
        if base == "leather" and abs(y - 0.11) < 0.07 and z > 1.06:   # the pack: flap, buckled straps, stitched edges, a chevron
            q = p - Vector((0.0, 0.11, 1.2)); e_ = edge(q, (0.11, 0.06, 0.13), 0.005, 0.011)
            if e_ == 2: return C["ink"]
            if abs(z - 1.25) < 0.0045: return C["ink"]
            if abs(abs(x) - 0.06) < 0.012 and 1.1 < z < 1.25:
                if abs(abs(x) - 0.06) > 0.008: return C["ink"]
                if abs(z - 1.16) < 0.011: return C["ink"] if abs(z - 1.16) > 0.007 or abs(abs(x) - 0.06) < 0.003 else C["brassl"]
                return lt(C["leatherl"])
            if e_ == 1: return lt(C["leatherl"])
            return lt(C["leatherl"] if z > 1.25 else C["leather"], grime=0.15)
        if base == "cloth" and abs(y - 0.11) < 0.07 and z > 1.3:   # the bedroll: rolled ends, two straps
            if abs(n.x) > 0.6:
                r = math.hypot(y - 0.11, z - 1.375); return C["clothd"] if (r * 55) % 1.0 < 0.35 else lt(C["cloth"])
            if abs(abs(x) - 0.08) < 0.012:
                if abs(abs(x) - 0.08) > 0.008: return C["ink"]
                return C["brass"] if abs(z - 1.4) < 0.008 and n.z > 0 else lt(C["leather"])
            return lt(C["clothd"] if math.sin(x * 120) > 0.85 else C["cloth"])
        return None
    def paint(m, p, n, ao, tx, ty):
        x, y, z = p.x, p.y, p.z
        base = m.split(".")[0]
        k_ = kit(base, p, n, ao)
        if k_ is not None: return k_
        if base not in ("greatcoat", "coat", "trousers", "puttee", "boots", "leather", "mannequin"):   # the solid kit: plain bands
            metal = base in ("engplate", "metal", "iron", "brass", "dark")
            c = C.get(base, C["coat"])
            if base == "leather" and abs(p.y - 0.11) < 0.07:                               # the pack's flap and its buckle straps
                if abs(z - 1.25) < 0.005 or (abs(abs(x) - 0.06) < 0.012 and 1.12 < z < 1.25): c = C["ink"] if abs(z - 1.25) < 0.005 or abs(abs(x) - 0.06) > 0.008 else C["leatherl"]
                elif z > 1.25: c = C["leatherl"]
            if base == "cloth" and abs(abs(x) - 0.08) < 0.012: c = C["ink"] if abs(abs(x) - 0.08) > 0.008 else C["leather"]   # bedroll straps
            if base == "engplate":   # pauldron: a rolled light edge where it faces up, ink under it
                c = C["platel"] if n.z > 0.85 else (C["plated"] if n.z < -0.2 else c)
            return lit(c, n, ao, metal, 0.0, p)
        if base == "mannequin":   # the scarf wound round the neck
            a = math.atan2(x - NECK.x, -(y - NECK.y))
            w = math.sin(z * 150 + a * 2.0)
            c = C["scarfd"] if w > 0.7 else (C["ink"] if w > 0.55 else C["scarf"])
            return lit(c, n, ao, p=p)
        if base == "leather":   # gloves: dark leather, a seam over the knuckles, a light glint
            s = "L" if x > 0 else "R"; fc, fd = FISTS[s]
            along = (p - fc).dot(fd)
            c = C["ink"] if abs(along - 0.012) < 0.004 else (C["glovel"] if n.dot(LIGHT) > 0.7 else C["glove"])
            if along < -0.03: c = C["leather"]   # the gauntlet cuff
            return lit(c, n, ao, p=p)
        # boots, from the ankle down whatever the region
        if z < 0.16 and base in ("boots", "puttee", "trousers"):
            if z < 0.022: return lit(C["sole"], n, ao, p=p)
            if z < 0.03: return C["ink"]
            if 0.142 < z: return C["ink"]                                                # the boot top
            xc = 0.12 if x > 0 else -0.11
            c = C["boot"]
            if z < 0.06 + 0.012 * math.sin(x * 90 + y * 60): c = C["mud"]                    # caked mud round the welt
            elif n.y < -0.35 and n.z > 0.15 and abs(x - xc) < 0.018 and z > 0.05:              # laces up the instep: tidy rungs
                c = C["lace"] if (z * 70) % 1.0 < 0.34 else C["dark"]
            elif n.z > 0.6: c = C["bootl"]
            return lit(c, n, ao, p=p)
        # knee plates, painted over trousers and puttees
        for k_, kn in knees:
            e = p - k_
            if abs(e.x) < 0.05 and abs(e.z) < 0.06 and n.y < -0.2 and e.y < 0.05:
                ed = min(0.05 - abs(e.x), 0.06 - abs(e.z))
                if ed < 0.007: return C["ink"]
                rv = rivet(p, [k_ + Vector((dx, -0.01, dz)) for dx in (-0.032, 0.032) for dz in (-0.04, 0.04)], 0.006)
                if rv is not None: return rv
                c = C["platel"] if ed < 0.011 and e.z > 0 else (C["plated"] if abs(e.z) < 0.004 else C["plate"])
                return lit(c, n, ao, metal=True, p=p)
        if base == "puttee" and z < 0.43:   # puttees wound up the shin, a tape at the top
            if z > 0.405: return C["ink"] if z > 0.425 else lit(C["putteed"], n, ao, p=p)
            fr = (z * 20 + (x - (0.115 if x > 0 else -0.105)) * 9 * (1 if y < (-0.15 if x > 0 else 0.05) else -1)) % 1.0                                # a spiral: the wraps slant round the leg
            c = C["ink"] if fr < 0.2 else (C["putteed"] if fr < 0.36 else C["puttee"])
            return lit(c, n, ao, grime=0.3 * (1 - z / 0.43), p=p)
        side_, along, ad = arm_of(p)
        on_arm = ad < 0.075 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0
        if base in ("trousers", "puttee", "boots") or (base == "greatcoat" and z < 0.9 and not on_arm):   # trousers, creased
            w = math.sin(z * 55 + x * 20 + _nz.noise(p * 12) * 1.5)
            c = C["troused"] if w > 0.9 else C["trousers"]
            if 0.45 < z < 0.58 and math.sin(z * 210) > 0.6 and n.y > -0.3: c = C["troused"]   # behind the knee
            return lit(c, n, ao, grime=0.25 * max(0.0, (0.5 - z) / 0.1), p=p)
        if base == "coat":   # the coat skirt (simulated cloth): folds, front edges with buttons, pockets, hem
            a = math.degrees(math.atan2(x, -y))                                              # 0 at the front, + to his left
            aa = abs(a)
            if z < hem + 0.006: return C["ink"]
            if aa < 21: return C["ink"] if aa < 18.5 else lit(C["coatl"], n, ao, p=p)         # the front edges
            for bz in (0.94, 0.86, 0.78):
                if (Vector((aa - 25.5, (z - bz) * 300))).length < 2.6: return C["brassl"] if z > bz else C["brass"]
            if 128 < aa < 132 and 0.8 < z < 0.95: return C["ink"]                           # side seams
            if aa > 176 and z < hem + 0.16: return C["ink"]                                  # the back vent
            if aa > 165 and abs(z - (hem + 0.17)) < 0.009: return C["brass"] if abs(z - (hem + 0.17)) < 0.006 else C["ink"]   # its button
            if 45 < aa < 85 and 0.87 < z < 0.93:                                              # pocket flaps, a button each
                if z < 0.876 or aa < 47 or aa > 83: return C["ink"]
                if abs(aa - 65) < 3.2 and abs(z - 0.887) < 0.007: return C["brassl"] if z > 0.887 else C["brass"]
                return lit(C["coatd"], n, ao, p=p)
            if 45 < aa < 85 and 0.79 < z < 0.87 and (aa < 47.5 or aa > 82.5): return C["coatd"]   # the pocket's stitched sides
            if 0.995 < z: return C["ink"] if z < 1.0 else lit(C["leather"], n, ao, p=p)      # the belt over its top
            if z < hem + 0.035:                                                                # a muddy hem, a stitched line above it
                return lit(C["mud"] if z < hem + 0.02 + 0.008 * math.sin(a * 0.4) else C["coatd"], n, ao, p=p)
            if z < hem + 0.041: return C["coatd"]
            f = (a / 30.0 + _nz.noise(Vector((a * 0.05, 0, z * 0.8))) * 0.4) % 1.0           # long folds, unbroken and wide enough not to break into dashes
            c = C["coatd"] if f < 0.16 and z < 0.95 else (C["coatl"] if 0.16 <= f < 0.24 and z < 0.92 else C["coat"])
            return lit(c, n, ao, grime=0.35 * max(0.0, (0.8 - z) / 0.25), p=p)
        # the body's greatcoat: belt, cross strap, breastplate, collar, sleeves
        if not on_arm and 0.965 < z < 1.04:                                                    # the belt, its buckle
            if z < 0.972 or z > 1.033: return C["ink"]
            if y < 0 and abs(x) < 0.028:
                return C["ink"] if abs(x) > 0.022 or abs(z - 1.0) > 0.026 else (C["dark"] if abs(x) < 0.012 and abs(z - 1.0) < 0.014 else C["brassl"] if n.dot(LIGHT) > 0.5 else C["brass"])
            if y < 0 and any(math.hypot(x - hx_, z - 1.002) < 0.004 for hx_ in (0.042, 0.058)): return C["ink"]   # belt holes
            if y < 0 and abs(x - 0.074) < 0.006: return C["ink"] if abs(x - 0.074) > 0.0035 else C["leatherl"]   # the keeper
            return lit(C["leather"], n, ao, p=p)
        if not on_arm and strap(x, z) < 0.022:                                                 # the cross strap: stitched edges, a brass ring
            dd = strap(x, z)
            if dd > 0.017: return C["ink"]
            ring = S0 + (S1 - S0) * 0.36
            if y < 0 and (Vector((x, z)) - ring).length < 0.016: return C["ink"] if abs((Vector((x, z)) - ring).length - 0.011) > 0.004 else C["brassl"]
            return lit(C["leatherl"] if dd < 0.005 else C["leather"], n, ao, p=p)
        top_ = 1.405 - 3.0 * x * x                                                              # the breastplate's neckline
        if not on_arm and y < 0.0 and 1.045 < z < top_ and abs(x) < 0.155:
            ed = min(z - 1.045, top_ - z, 0.155 - abs(x))
            if ed < 0.008: return C["ink"]
            rv = rivet(p, [Vector((sx * 0.12, p.y, rz)) for sx in (-1, 1) for rz in (1.33, 1.24, 1.19, 1.14, 1.09)] + [Vector((0, p.y, 1.375))])
            if rv is not None: return rv
            c = C["plate"]
            if ed < 0.014 and z > 1.3: c = C["platel"]                                       # rolled top edge
            for L_ in (1.215, 1.165, 1.115):                                                   # the lames, each over the next
                if abs(z - L_) < 0.0055: c = C["ink"]
                elif 0 < L_ - z < 0.014: c = C["plated"]
                elif 0 < z - L_ < 0.007: c = C["platel"]
            cx_, cz_ = abs(x + 0.004), z - 0.004                                              # an embossed cross on the top plate, lit top-left
            if (abs(x) < 0.006 and 1.265 < z < 1.345) or (abs(z - 1.322) < 0.006 and abs(x) < 0.026): c = C["platel"]
            elif (cx_ < 0.006 and 1.265 < cz_ < 1.345) or (abs(cz_ - 1.322) < 0.006 and cx_ < 0.026): c = C["plated"]
            elif c is C["plate"] and abs(x) < 0.006 and 1.22 < z < 1.255: c = C["platel"]       # the ridge down the middle
            return lit(c, n, ao, metal=True, p=p)
        if not on_arm and z > nz_ - 0.035:                                                     # the turned-up collar, a red tab each side
            if z < nz_ - 0.028: return C["ink"]
            if y < 0 and 0.03 < abs(x) < 0.065 and z < nz_ - 0.006:
                if abs(x) < 0.034 or abs(x) > 0.061 or z > nz_ - 0.01: return C["ink"]
                if abs(abs(x) - 0.052) < 0.005 and abs(z - (nz_ - 0.02)) < 0.005: return C["brassl"]
                return lit(C["patch"], n, ao, p=p)
            return lit(C["coatd"] if n.dot(LIGHT) < 0.4 else C["coat"], n, ao, p=p)
        c = C["coat"]
        if on_arm:
            if along > 1.86: return C["ink"] if along < 1.89 else lit(C["coatd"], n, ao, p=p)    # turned-back cuff
            if abs(along - 0.18) < 0.03 and ad > 0.04: return C["ink"]                          # the shoulder seam
            if 0.82 < along < 1.25 and math.sin(along * 60 + ad * 80) > 0.8: c = C["coatd"]       # creases at the elbow
            q = p - PATCH
            if side_ == "L" and n.x > 0.2 and abs(q.x) < 0.04:   # the sapper's patch: a stitched shield, a pale pick and spade crossed
                w_ = 0.024 - max(0.0, -q.z - 0.004) * 0.75
                if abs(q.y) < w_ and -0.034 < q.z < 0.024:
                    if abs(q.y) > w_ - 0.004 or q.z > 0.02 or q.z < -0.03: return C["ink"]
                    if abs(q.y) > w_ - 0.0065 or q.z > 0.0165: return C["brass"]
                    if min(abs(q.y - q.z * 0.8), abs(q.y + q.z * 0.8)) < 0.0035 and -0.02 < q.z < 0.012: return C["bone"]
                    return lit(C["patch"], n, ao, p=p)
            if side_ == "L" and 1.25 < along < 1.8:                                           # a plated vambrace on the gun arm
                if along < 1.28 or along > 1.77: return C["ink"]
                return lit(C["platel"] if n.dot(LIGHT) > 0.55 else C["plate"], n, ao, metal=True, p=p)
            return lit(c, n, ao, p=p)
        if y > 0 and abs(x) < 0.004 and z < 1.38: return C["ink"]                               # the back seam
        if y > 0 and 1.04 < z < 1.075 and abs(x) < 0.1:                                        # the half belt at the back
            if z < 1.045 or z > 1.07 or abs(x) > 0.095: return C["ink"]
            return lit(C["coatl"], n, ao, p=p)
        if abs(n.x) > 0.94 and z < 1.38: return C["ink"]                                       # side seams
        return lit(c, n, ao, p=p)
    PAINT = paint

def stigmatic_nun():
    # Stigmatic Nun (2026-11-03, second figure of docs/art-pipeline.md, class A): an original figure on the owner's
    # reference cues (a white, blood-spattered habit and veil, a porcelain votive mask with a cross slit, plate over the
    # waist and skirt, bloody gauntlets, red shoulder tabs, a long blade), not a copy of either reference. No bare skin.
    # Painted PSX look (2026-11-10, as the Combat Engineer): a smooth low-poly body, the long habit and the hood draped,
    # and everything else painted (PAINT): blood spatter and a muddy hem on the white habit, the plate corset with its
    # lames and rivets, the strap to the throat, the tassets over the skirt, the medical case on the right hip, the
    # gauntlets, the boots; the votive mask with its dark cross slit and blood tears is painted on the blank head, and the
    # blood cross on the hood's crown. Solid only for the silhouette: the veil and hood (Body), Helmet (the masked head), Weapon
    # (pistol), Weapon2 (the serrated blade), Accent (shoulder tabs, seat colour).
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 820; TEX_SIZES = {"Body": 256, "Helmet": 128, "Hip": 128, "Weapon": 128, "Weapon2": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.06, 1.4))
        human.reach(r, "R", P(-0.22, -0.16, 1.24), P(-0.2, -0.42, 1.34))
        human.reach(r, "L", P(0.24, -0.06, 1.0), P(0.22, -0.22, 0.9))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "habit", "thigh": "habit", "torso": "habit"}, head="mannequin",
                                mitts="gauntlet", shape="female-young-averagemuscle-minweight")
    import random; rnd = random.Random(1917)
    toeless_feet()
    # the habit: long, falling to the feet, its hem dragged in the mud
    skirt = cloth_cone(0, 0, 0.98, 0.08, 0.15, 0.21, "habit", sy=0.85, ragged=0.03, rnd=rnd, tris=130, thick=0.01)
    hem = min(v.co.z for v in skirt.data.vertices)
    # shoulder tabs in the seat colour, seen from above
    for x in (-0.17, 0.17):
        box((x, -0.02, 1.43), (0.09, 0.12, 0.012), "accent", rot=(0, 0.35 if x > 0 else -0.35, 0))
        box((x * 1.06, -0.075, 1.38), (0.085, 0.012, 0.1), "accent")
    module("Helmet")
    head_ = next(o for o in MODULES["Body"] if o.name.startswith("Sphere") and o.data.materials[0].name == "mannequin")
    MODULES["Body"].remove(head_); MODULES["Helmet"].append(head_)   # the masked head alone: its own texture, room for the mask
    module("Body")   # the veil and hood are habit: a white hood over the crown, and a drape from the crown to the
    # shoulders, open at the front for the mask
    c = Vector((hp.x, hp.y, top.z))
    veil = sphere(tuple(c + Vector((0, 0.01, -0.05))), 0.112, "habit", (1, 1.1, 0.9), seg=10, rings=6)
    bpy.context.view_layer.update()
    veil_ = cloth_cone(c.x, c.y + 0.01, c.z - 0.05, c.z - 0.35, 0.115, 0.2, "pale", sy=1.05, keep=FRONT(32), tris=48, thick=0.01, extra=[veil])   # "pale": painted as the habit, with its own border
    import bmesh as _bm   # the veil's free edges (not the top ring under the hood), sampled densely, for its painted border
    from mathutils.kdtree import KDTree
    bm = _bm.new(); bm.from_mesh(veil_.data); vtop = max(v.co.z for v in bm.verts); edge_pts = []
    for e in bm.edges:
        if e.is_boundary and min(v.co.z for v in e.verts) < vtop - 0.03:
            a_, b_ = e.verts[0].co, e.verts[1].co; k_ = max(1, int((b_ - a_).length / 0.004))
            edge_pts += [a_.lerp(b_, i / k_) for i in range(k_ + 1)]
    bm.free(); vkd = KDTree(len(edge_pts))
    for i, q_ in enumerate(edge_pts): vkd.insert(q_, i)
    vkd.balance()
    fb = FACE_BOX; centre = fb["mid"] - fb["fwd"] * 0.06 - fb["up"] * 0.02   # about the middle of the head
    module("Weapon")
    # pistol in the right fist: the grip through the fist, the barrel above it pointing along the raised forearm
    fr, d = FISTS["R"]; up_g = (Vector((0, 0, 1)) - d * d.z).normalized()
    bar(fr - up_g * 0.04, fr + up_g * 0.03, 0.016, "wood", sides=4)
    bar(fr + up_g * 0.04 - d * 0.03, fr + up_g * 0.04 + d * 0.15, 0.014, "metal", sides=5)
    module("Weapon2")
    # a long blade held low, its point forward and up: a flat blade broad at the guard and tapering to a point, its back
    # edge sawn into teeth, blood dried on the steel; a crossguard, a wrapped grip and a round pommel
    fl, d = FISTS["L"]; want = Vector((0.03, -0.55, 0.45))
    e = (want - d * want.dot(d)).normalized(); hl = fl
    side = (Vector((1, 0, 0)) - e * e.x).normalized()      # the blade's width lies side to side: its flat faces up and forward
    base, L, w = hl + e * 0.055, 0.44, 0.042
    outline = [(0, w / 2), (L * 0.78, w * 0.4), (L, 0)]   # the cutting edge, then the point
    teeth = 5
    for i in range(teeth, -1, -1):                          # the sawn back edge, from the point back to the guard
        t = L * 0.74 * i / teeth
        outline.append((t, -w / 2 * (0.95 if i % 2 else 0.62) * (1 - 0.35 * t / L)))
    me = bpy.data.meshes.new("blade")
    me.from_pydata([tuple(base + e * a_ + side * c_) for a_, c_ in outline], [], [list(range(len(outline)))])
    bl = bpy.data.objects.new("blade", me); scn.collection.objects.link(bl); bl.modifiers.new("sol", "SOLIDIFY").thickness = 0.008
    add(bl, "blade")
    bar(base - side * 0.055, base + side * 0.055, 0.009, "brass", sides=4)             # crossguard
    bar(hl - e * 0.06, hl + e * 0.05, 0.011, "leather", sides=4)                       # grip
    sphere(tuple(hl - e * 0.07), 0.017, "brass", seg=5, rings=3)                      # pommel
    module("Helmet")
    # detail pass (2026-11-10): a band of thorned iron wire round the hood's crown, seen from above
    THORN = c + Vector((0, 0.01, -0.02))
    bpy.ops.mesh.primitive_torus_add(major_radius=0.109, minor_radius=0.007, major_segments=12, minor_segments=3, location=tuple(THORN))
    bpy.context.object.scale = (1, 1.1, 1); add(bpy.context.object, "iron")
    module("Hip")
    skirt.data.update(); bpy.context.view_layer.update()
    human_ = bpy.data.objects["human"]
    def surf(o_, d_, out=0.012):   # the outermost point of the body or the habit seen from o_ along d_
        best = None
        for ob in (human_, skirt):
            ok, at, *_ = ob.ray_cast(o_, d_)
            if ok and (best is None or (at - o_).length < (best - o_).length): best = at
        return (best if best is not None else o_ + d_ * 0.9) - d_ * out
    R0 = surf(Vector((0.055, -1.0, 0.97)), Vector((0, 1, 0)))                       # a rosary of bone beads down the front, a cross at its end
    RX = surf(Vector((0.07, -1.0, 0.7)), Vector((0, 1, 0)), 0.018)
    BEADS = [R0 + (RX - R0) * (k / 6) for k in range(6)]
    for b0, b1 in zip(BEADS, BEADS[1:] + [RX + Vector((0, 0, 0.03))]): bar(b0, b1, 0.0085, "bone", sides=3)
    bar(RX + Vector((0, 0, 0.03)), RX - Vector((0, 0, 0.045)), 0.0075, "bone", sides=4)
    bar(RX - Vector((0.024, 0, 0.0)), RX + Vector((0.024, 0, 0.0)), 0.0075, "bone", sides=4)
    BOOK = surf(Vector((1.0, 0.045, 0.85)), Vector((-1, 0, 0))) + Vector((0.016, 0, 0))   # a prayer book chained to the left hip
    box(tuple(BOOK), (0.03, 0.065, 0.085), "leather")
    bar(BOOK + Vector((0, 0, 0.043)), surf(Vector((1.0, 0.03, 0.99)), Vector((-1, 0, 0))), 0.004, "iron", sides=3)
    SC0, SC1 = surf(Vector((1.0, 0.1, 0.93)), Vector((-1, 0, 0)), 0.03), surf(Vector((1.0, 0.16, 0.5)), Vector((-1, 0, 0)), 0.03)   # the blade's scabbard, behind it
    bar(SC0, SC1, 0.016, "leather", sides=4)
    module("Body")

    # ---- the paint ----
    C = psx_palette(habit="bdb59f", habitd="7e7764", habitl="d8d2be", blood="5e140d", bloodd="3a0c08", mud="4a4034",
                    plate="5e6064", plated="34363a", platel="8e9094", gaunt="6e1812", gauntd="400e0a", gauntl="923026",
                    bone="d8d0c0", boned="9a9282", leather="3e2d1f", leatherl="5e4632", brass="7a6533", brassl="a48d52",
                    boot="2a241d", bootl="463c30", sole="151310", ink="14120e", wood="4f3a26", metal="3b3c3a", metall="5c5e5c",
                    steel="7e8084", steell="b0b2b4", dark="191714", iron="4b4c4d", irond="2c2d2e", ironl="6e7072",
                    linen="aea48a", lineni="8a816a")
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def blood(p, dense=0.0):   # coherent blots and runs dripping down from them (detail pass 2026-11-10: no single-texel spray)
        if _nz.noise(p * 12) > 0.6 - dense: return C["bloodd"] if _nz.noise(p * 30) > 0.35 else C["blood"]
        if _nz.noise(Vector((p.x * 30, p.y * 30, 0))) > 0.6 and _nz.noise(Vector((p.x * 12, p.y * 12, p.z * 3.5))) > 0.42 - dense: return C["blood"]
        return None
    def dot(p, pts, r=0.0065): return psx_dot(p, pts, C, r)
    def detail(base, p, n, ao):   # detail pass (2026-11-10): the new kit (None: not one of these)
        s_ = n.dot(PSX_LIGHT)
        if base == "iron" and (p - THORN).length < 0.14:   # the thorned wire: twisted strands, barbs catching the light
            a = math.atan2(p.x - THORN.x, -(p.y - THORN.y))
            if (a * 24 / math.pi) % 1.0 < 0.18: return C["ironl"]
            return C["irond"] if (a * 48 / math.pi + p.z * 300) % 1.0 < 0.4 else lit(C["iron"], n, ao, metal=True, p=p)
        if base == "iron": return lit(C["ironl"] if s_ > 0.4 else C["iron"], n, ao, metal=True, p=p)   # the book's chain
        if base == "pale":   # the veil: a red border worked along its edge, a seam down the back, blood
            h = vkd.find(p)[2] - 0.006   # from its free edge (the cloth grew 0.01 thick outward from it)
            if h < 0.008: return C["ink"]
            if h < 0.026: return lit(C["gauntd"], n, ao, p=p)
            if h < 0.032: return C["habitd"]
            if p.y > c.y + 0.02 and abs(p.x - c.x) < 0.006: return C["habitd"]
            b_ = blood(p * 0.9)
            if b_ is not None: return b_
            return lit(C["habitl"] if s_ > 0.62 else C["habit"], n, ao, p=p)
        if base == "bone":   # the rosary: beads with ink between, the cross
            t = (p - R0).dot((RX - R0).normalized()) / (RX - R0).length
            if (p - RX).length > 0.05 and (t * 6) % 1.0 < 0.14: return C["ink"]
            return C["bone"] if s_ > 0.3 else C["boned"]
        if base == "leather" and (p - BOOK).length < 0.08:   # the prayer book: a gilt-edged leather cover, a clasp, a cross on it
            q = p - BOOK
            if abs(n.x) > 0.6:
                if abs(q.y) > 0.026 or abs(q.z) > 0.036: return C["ink"]
                if abs(q.y) > 0.02 or abs(q.z) > 0.03: return C["brass"]
                if (abs(q.y) < 0.004 and abs(q.z) < 0.022) or (abs(q.z - 0.008) < 0.004 and abs(q.y) < 0.013): return C["brassl"]
                return lit(C["leather"], n, ao, p=p)
            if abs(q.x) < 0.011 and n.y < 0.5: return C["habitl"] if (q.x * 400) % 1.0 < 0.5 else C["habitd"]   # the page edges
            return lit(C["leatherl"] if s_ > 0.5 else C["leather"], n, ao, p=p)
        if base == "leather" and segd(p, SC0, SC1)[0] < 0.03:   # the scabbard: a brass throat and chape
            t = segd(p, SC0, SC1)[1]
            if t < 0.08 or t > 0.93: return C["brassl"] if s_ > 0.4 else C["brass"]
            if abs(t - 0.1) < 0.02 or abs(t - 0.91) < 0.02: return C["ink"]
            return lit(C["leatherl"] if s_ > 0.55 else C["leather"], n, ao, p=p)
        return None
    def paint(m, p, n, ao, tx, ty):
        x, y, z = p.x, p.y, p.z
        base = m.split(".")[0]
        d_ = detail(base, p, n, ao)
        if d_ is not None: return d_
        if base == "mannequin":   # the votive mask: glazed porcelain, a dark cross slit, blood weeping from it
            q = p - centre; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]) + 0.012, q.dot(fb["fwd"])
            wd = 0.072 if u > 0 else 0.072 - 0.25 * (-u)
            e_ = (a / max(wd, 0.03)) ** 2 + (u / (0.105 if u > 0 else 0.118)) ** 2
            if f < 0.02 or e_ > 1.0 or u < -0.11: return lit(C["habitd"], n, ao, p=p)        # under the hood, in its shadow (the neck too)
            if e_ > 0.84: return C["ink"]
            uu = u - 0.012
            if (abs(a) < 0.0055 and -0.066 < uu < 0.066) or (abs(uu - 0.03) < 0.0055 and abs(a) < 0.041): return C["dark"]   # the slit
            if (abs(a) < 0.0085 and -0.069 < uu < 0.069) or (abs(uu - 0.03) < 0.0085 and abs(a) < 0.044): return C["boned"]
            for tx_ in (-0.03, 0.03):   # blood tears from the cross's arms
                if abs(a - tx_ - math.sin(uu * 90) * 0.002) < 0.003 and -0.012 + abs(tx_) * 0.3 < uu < 0.024: return C["blood"]
            return lit(C["bone"] if n.dot(PSX_LIGHT) < 0.75 else C["habitl"], n, ao, p=p)
        if base == "gauntlet":   # plate gauntlets, red with blood: knuckle lames, a flared cuff
            s = "L" if (p - FISTS["L"][0]).length < (p - FISTS["R"][0]).length else "R"; fc, fd = FISTS[s]
            along = (p - fc).dot(fd)
            if any(abs(along - k) < 0.0035 for k in (-0.025, 0.0, 0.02)): return C["ink"]
            return lit(C["gauntl"] if n.dot(PSX_LIGHT) > 0.6 else (C["gauntd"] if _nz.noise(p * 50) > 0.4 else C["gaunt"]), n, ao, metal=True, p=p)
        if base in ("wood", "metal", "brass", "leather", "blade"):   # the weapons
            if base == "blade":
                b_ = blood(p * 1.4, 0.15)
                if b_ is not None: return b_
                return lit(C["steell"] if n.dot(PSX_LIGHT) > 0.5 else C["steel"], n, ao, metal=True, p=p)
            return lit(C.get(base, C["metal"]), n, ao, base in ("metal", "brass"), p=p)
        if z < 0.16 and base == "boots":   # boots under the hem
            if z < 0.022: return lit(C["sole"], n, ao, p=p)
            return lit(C["bootl"] if n.z > 0.6 else C["boot"], n, ao, grime=0.3, p=p)
        # the white habit: shade folds, blood, a muddy hem; plate, straps and the case painted over it
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.07 and (abs(x) > 0.15 or y < -0.15 or z > 1.41) and 1.0 < z < 1.6
        hq = p - c
        if z > NECK.z + 0.02 or hq.to_2d().length < 0.13 and z > NECK.z - 0.1:   # the hood and veil: the blood cross on the crown
            if hq.y < -0.04 and ((abs(hq.x) < 0.0075 and -0.005 < hq.z < 0.06) or (abs(hq.x) < 0.024 and abs(hq.z - 0.036) < 0.007)): return C["blood"]
            return lit(C["habitl"] if n.dot(PSX_LIGHT) > 0.62 else C["habit"], n, ao, p=p)
        if on_arm:   # sleeves: wide cuffs, blood thickest at the forearm
            if along > 1.68:
                if along < 1.71: return C["ink"]
                return lit(C["gauntl"] if n.dot(PSX_LIGHT) > 0.6 else C["gaunt"], n, ao, metal=True, p=p)   # the vambrace
            if 1.42 < along < 1.68:   # the stigmata: linen bandage wound round the forearm, blood seeping through
                if _nz.noise(p * 9 + Vector((1, 1, 1))) > 0.3: return C["blood"]
                return C["lineni"] if (along * 16) % 1.0 < 0.25 else lit(C["linen"], n, ao, p=p)
            if abs(along - 0.15) < 0.03 and ad > 0.04: return C["habitd"]                    # the shoulder seam
            b_ = blood(p, 0.1 * along)
            if b_ is not None: return b_
            return lit(C["habitd"] if 0.85 < along < 1.2 and math.sin(along * 50 + ad * 90) > 0.75 else C["habit"], n, ao, p=p)
        front = y < 0
        if 1.0 < z < 1.16 and not on_arm:   # the plate corset: lames, rivets at the sides
            if z < 1.007 or z > 1.153: return C["ink"]
            r_ = dot(p, [Vector((sx * 0.1, y, rz)) for sx in (-1, 1) for rz in (1.03, 1.08, 1.13)] if front else [])
            if r_ is not None: return r_
            if not front and abs(x) < 0.03:   # laced up the back: eyelet edges, leather rungs between
                if abs(x) > 0.022: return C["ink"] if abs(x) > 0.026 else lit(C["platel"], n, ao, metal=True, p=p)
                return lit(C["leather"], n, ao, p=p) if (z * 40) % 1.0 < 0.4 else C["dark"]
            for L_ in (1.06, 1.11):
                if abs(z - L_) < 0.0045: return C["ink"]
                if 0 < L_ - z < 0.012: return lit(C["plated"], n, ao, metal=True, p=p)
            return lit(C["platel"] if n.dot(PSX_LIGHT) > 0.55 else C["plate"], n, ao, metal=True, p=p)
        if front and abs(x) < 0.014 and 1.16 < z < NECK.z:   # the strap up to the throat, its brass buckle
            if abs(x) > 0.01: return C["ink"]
            if abs(z - 1.3) < 0.015: return C["brassl"] if abs(z - 1.3) < 0.01 else C["ink"]
            return lit(C["leatherl"] if abs(x) < 0.003 else C["leather"], n, ao, p=p)
        if z < 1.0:   # the skirt: tassets over the front, the medical case on the right hip
            if front:
                for tx_ in (-0.09, 0.09):
                    for z0, z1 in ((0.84, 0.965), (0.72, 0.85)):
                        if abs(x - tx_) < 0.052 and z0 < z < z1:
                            if abs(x - tx_) > 0.046 or z < z0 + 0.006 or z > z1 - 0.004: return C["ink"]
                            r_ = dot(p, [Vector((tx_ + sx * 0.032, y, z1 - 0.018)) for sx in (-1, 1)], 0.006)
                            if r_ is not None: return r_
                            c_ = C["platel"] if z > z1 - 0.022 else (C["plated"] if z < z0 + 0.02 else C["plate"])
                            return lit(c_, n, ao, metal=True, grime=0.2, p=p)
            if x < -0.12 and n.x < -0.3 and -0.08 < y < 0.08 and 0.815 < z < 0.945:
                if abs(y) > 0.074 or z < 0.821 or z > 0.94: return C["ink"]
                if abs(z - 0.905) < 0.003: return C["ink"]
                if abs(y) < 0.016 and abs(z - 0.888) < 0.012: return C["metall"]
                return lit(C["leatherl"] if z > 0.905 else C["leather"], n, ao, p=p)
            if z < hem + 0.01: return C["ink"]
            a = math.degrees(math.atan2(x, -y))
            if z < hem + 0.05 + 0.025 * _nz.noise(Vector((a * 0.08, 0, 0))): return lit(C["habitd"].lerp(C["mud"], 0.6), n, ao, p=p)   # the hem dragged in the mud
            if front and z < 0.69 and ((abs(x) < 0.013 and z > 0.4) or (abs(z - 0.6) < 0.013 and abs(x) < 0.055)):   # a great red cross stitched on the front
                return C["ink"] if (abs(x) > 0.009 and abs(z - 0.6) > 0.009) or z < 0.405 or z > 0.685 or abs(x) > 0.051 else lit(C["gauntd"], n, ao, p=p)
            b_ = blood(p, 0.12 * max(0.0, (0.7 - z) / 0.6))
            if b_ is not None: return b_
            f = (a / 34.0 + _nz.noise(Vector((a * 0.04, 0, z * 0.7))) * 0.35) % 1.0   # long unbroken folds down the habit, soft on the white
            c_ = C["habitd"].lerp(C["habit"], 0.45) if f < 0.16 else (C["habitl"] if f < 0.25 and n.dot(PSX_LIGHT) > 0.2 else C["habit"])
            if not front and abs(x) < 0.006: c_ = C["habitd"]                                   # the back seam
            return lit(c_, n, ao, grime=0.3 * max(0.0, (0.45 - z) / 0.3), p=p)
        b_ = blood(p)
        if b_ is not None: return b_
        if not front and abs(x) < 0.004: return lit(C["habitd"], n, ao, p=p)                   # the back seam
        return lit(C["habitl"] if n.dot(PSX_LIGHT) > 0.62 else C["habit"], n, ao, p=p)
    PAINT = paint

def ecclesiastic_prisoner():
    # Ecclesiastic Prisoner (2026-11-03, third figure of docs/art-pipeline.md, class A): an original figure on the owner's
    # reference cues, not a copy of the miniature or the art. A gaunt, stooped penitent, bare and grimed with blood, in a
    # rag loincloth with ropes bound round his body, his head locked in a rusted iron helm (no face and no head modelled
    # under it), his hands shackled in a wooden stocks board, iron cuffs at wrists and ankles, and the Martyrdom Device,
    # a finned aerial bomb, strapped across his back. Painted PSX look (2026-11-10, as the Combat Engineer): the smooth
    # low-poly body with ribs, grime, lash wounds and blood painted on the skin, and the ropes (girdle, chest cross), the
    # wrist and ankle cuffs painted; the helm's rivets and breathing holes, the board's iron bands, the bomb's stencil
    # band, rivets, rope and scrawled prayers painted. Solid only for the silhouette: Helmet (iron helm with spikes),
    # Weapon (the stocks board), Back (bomb), Hip (loincloth), Accent (the cloth band round the helm and the loincloth's
    # front flap).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 820; TEX_SIZES = {"Body": 256, "Back": 128, "Weapon": 128, "Hip": 128, "Helmet": 128}
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.28, 1.32)); human.aim(r, "neck01", P(0, -0.38, 1.44))
        human.reach(r, "R", P(-0.2, -0.16, 1.12), P(-0.07, -0.3, 0.98))
        human.reach(r, "L", P(0.2, -0.16, 1.12), P(0.07, -0.3, 0.98))
    hands, hp, top = human_body(pose, coat=False, dress={"foot": "flesh", "shin": "flesh", "thigh": "flesh", "torso": "flesh"}, head=None,
                                mitts="flesh", shape="male-young-minmuscle-minweight")
    import random; rnd = random.Random(1915)
    # the bowed back's surface, ray-cast from behind, so the bomb sits on it
    human_ = bpy.data.objects["human"]
    def back_y(x, z):
        hit, loc, *_ = human_.ray_cast(Vector((x, 1.0, z)), Vector((0, -1, 0)))
        return loc.y if hit else None
    module("Hip")
    # the rag loincloth, its hem torn, and a front flap in the seat colour
    lc = cloth_cone(0, 0, 0.97, 0.75, 0.155, 0.17, "rags", sy=0.8, ragged=0.06, rnd=rnd, tris=50, thick=0.01, rows=5)
    cloth_panel([(-0.07, -0.135, 0.96), (0.07, -0.135, 0.96)], 0.34, "accent", extra=[lc], cols=2, rows=5, ragged=0.05, rnd=rnd, tris=None, bend=8.0)
    module("Helmet")
    # the iron helm, locked over the whole head: a rusted sphere bowed forward with the stoop and short spikes
    fb = FACE_BOX; centre = fb["mid"] - fb["fwd"] * 0.05 + fb["up"] * 0.01
    sphere(tuple(centre), 0.128, "rust", (1, 1.05, 1.02), seg=10, rings=6)
    # a cloth band in the seat colour bound round the helm's rim: the top of the figure, so the camera above sees it
    bpy.ops.mesh.primitive_torus_add(major_radius=0.122, minor_radius=0.018, major_segments=10, minor_segments=3, location=tuple(centre - fb["up"] * 0.075))
    o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(fb["up"]); add(o, "accent")
    for d_ in (fb["up"], (fb["up"] + fb["fwd"]).normalized(), (fb["up"] + fb["ax"]).normalized(), (fb["up"] - fb["ax"]).normalized(),
               (fb["fwd"] + fb["ax"] * 0.9).normalized(), (fb["fwd"] - fb["ax"] * 0.9).normalized()):
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.016, radius2=0.0, depth=0.06, location=tuple(centre + d_ * 0.15))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d_); add(o, "iron")
    module("Weapon")
    # the stocks board: a heavy wooden plank at the waist with both fists locked through it
    fr, _ = FISTS["R"]; fl, _ = FISTS["L"]; mid = (fr + fl) / 2
    across = (fl - fr).normalized()
    bar(mid - across * 0.17, mid + across * 0.17, 0.03, "wood", sides=4)
    module("Back")
    # the Martyrdom Device: a finned aerial bomb strapped along the bowed back, its tail over the right shoulder, resting
    # on the most prominent point of the back (never sinking into it)
    r_ = 0.075
    base_, tip_ = Vector((0.06, 0.0, 1.02)), Vector((-0.08, 0.0, 1.3))
    base_.y, tip_.y = back_y(base_.x, base_.z) + r_, back_y(tip_.x, tip_.z) + r_
    tip_ = base_ + (tip_ - base_).normalized() * 0.5   # the bomb's full length along that line
    lift_ = max((y + r_) - (base_.y + (tip_.y - base_.y) * t)
                for t in [i / 8 for i in range(9)]
                if (y := back_y(base_.x + (tip_.x - base_.x) * t, base_.z + (tip_.z - base_.z) * t)) is not None)
    base_.y += lift_; tip_.y += lift_
    ax = (tip_ - base_).normalized()
    bar(base_, tip_ - ax * 0.08, 0.075, "bomb", sides=8)
    sphere(tuple(base_), 0.075, "bomb", seg=8, rings=4)
    bar(tip_ - ax * 0.08, tip_, 0.04, "bomb", sides=6)
    side_ = ax.cross(Vector((0, 1, 0))).normalized(); out_ = ax.cross(side_).normalized()
    for v_ in (side_, out_):   # two crossed fin plates
        bar(tip_ - ax * 0.06 - v_ * 0.075, tip_ - ax * 0.06 + v_ * 0.075, 0.012, "iron", sides=4)
    # detail pass (2026-11-10): a brass fuse on the nose, two prayer strips sealed along the casing, and the detonator
    # wire looping from the fuse round his left side to the stocks board, where his hands can pull it
    bar(base_ - ax * 0.06, base_ - ax * 0.1, 0.02, "brass", sides=6)
    BSTRIPS = []
    for t0, rot in ((0.17, 0.35), (0.3, -0.5)):
        nrm = (out_ * math.cos(rot) + side_ * math.sin(rot)).normalized(); acr = ax.cross(nrm).normalized()
        s0, s1 = base_ + ax * t0 + nrm * 0.079, base_ + ax * (t0 + 0.13) + nrm * 0.079
        me = bpy.data.meshes.new("strip"); me.from_pydata([tuple(s0 - acr * 0.013), tuple(s0 + acr * 0.013), tuple(s1 + acr * 0.012), tuple(s1 - acr * 0.012)], [], [(0, 1, 2, 3)])
        o = bpy.data.objects.new("strip", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.003; add(o, "parchment")
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.016, depth=0.008, location=tuple(s0 + nrm * 0.004))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(nrm); add(o, "charge")
        BSTRIPS.append((s0, s1, acr))
    WIRE = [base_ - ax * 0.1, Vector((0.2, 0.03, 0.93)), Vector((0.21, -0.12, 0.95)), fl + across * 0.06]
    hose(WIRE, 0.0055, "rubber", sides=3)
    module("Weapon")
    CH = [mid - Vector((0, 0, 0.03)) + Vector((0.006 * math.sin(k * 1.3), -0.01 * k, -0.045 * k)) for k in range(6)]   # a broken chain hanging from the board
    for a_, b_ in zip(CH, CH[1:]): bar(a_, b_, 0.006, "iron", sides=4)
    module("Hip")
    # the ankle chain between the cuffs, sagging to the ground
    ank = {s: human_.ray_cast(Vector((0.0, JOINTS[("foot.L" if s > 0 else "foot.R")].y, 0.1)), Vector((s, 0, 0)))[1] for s in (1, -1)}   # the cuffs' inner sides
    AC = [ank[1].lerp(ank[-1], k / 4) - Vector((0, 0.01, 0.055 * math.sin(math.pi * k / 4))) for k in range(5)]
    for a_, b_ in zip(AC, AC[1:]): bar(a_, b_, 0.006, "iron", sides=4)
    module("Helmet")
    # a padlock hanging from the helm's rim at the front, where the band is fastened
    rim_ = centre + fb["fwd"] * 0.11 - fb["up"] * 0.07
    LOCK = rim_ + Vector((0, -0.01, -0.06))
    bar(rim_, LOCK + Vector((0, 0, 0.015)), 0.005, "iron", sides=4)
    box(tuple(LOCK), (0.04, 0.018, 0.034), "iron")
    module("Body")

    # ---- the paint ----
    C = psx_palette(skin="8e7262", skind="5e4a40", skinl="a88a76", sore="6a3a30", blood="5e140d", bloodd="3a0c08",
                    mud="4a4034", rags="6a5c46", ragsd="463c2e", ragsl="827258", rope="9a8458", roped="3e2e1c",
                    rust="5e3e2a", rustd="3a2618", rustl="8a5c3a", iron="4b4c4d", irond="2c2d2e", ironl="6e7072",
                    wood="56402a", woodd="38281a", woodl="725636", bomb="4a5430", bombd="2e3420", bombl="66704a",
                    stencil="9a8a4a", chalk="b8b0a0", brass="7a6533", brassl="a48d52", ink="14120e", dark="191714")
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def rope(d_, along):   # a twisted rope band, d_ from its centre line
        if d_ > 0.011: return C["roped"]
        return C["roped"] if (along * 36) % 1.0 < 0.25 else C["rope"]   # twists wide enough to stay whole at this texel size
    def blood(p, dense=0.0):   # coherent blots and runs (detail pass 2026-11-10: no single-texel spray)
        if _nz.noise(p * 11) > 0.6 - dense: return C["bloodd"] if _nz.noise(p * 28) > 0.35 else C["blood"]
        if _nz.noise(Vector((p.x * 28, p.y * 28, 0))) > 0.62 and _nz.noise(Vector((p.x * 11, p.y * 11, p.z * 3.5))) > 0.45 - dense: return C["blood"]
        return None
    S = [(Vector((sx * 0.11, 1.42)), Vector((-sx * 0.12, 0.99))) for sx in (-1, 1)]   # the chest ropes, crossed front and back (x, z)
    def links(p, n, pts, lit_):   # a chain: dark iron links, each with a light top, ink where they meet
        k = min(range(len(pts) - 1), key=lambda i: segd(p, pts[i], pts[i + 1])[0]); t = segd(p, pts[k], pts[k + 1])[1]
        if t < 0.12 or t > 0.88: return C["ink"]
        return lit_(C["ironl"] if (k % 2 == 0) == (n.dot(PSX_LIGHT) > 0.3) else C["irond"])
    def detail(base, p, n, ao):   # the new kit (None: not one of these)
        def lt(c_, metal=True): return lit(c_, n, ao, metal, 0.0, p)
        s_ = n.dot(PSX_LIGHT)
        if base == "iron" and (p - LOCK).length < 0.07:   # the padlock: a keyhole, rivets, rust
            q = p - LOCK
            if abs(q.y) > 0.006 and math.hypot(q.x, q.z + 0.002) < 0.005: return C["dark"]
            if 0.017 - abs(q.z) < 0.004 or 0.02 - abs(q.x) < 0.004: return C["ink"]
            return lt(C["rust"] if _nz.noise(p * 40) > 0.3 else (C["ironl"] if s_ > 0.4 else C["iron"]))
        if base == "iron" and min(segd(p, a_, b_)[0] for a_, b_ in zip(CH, CH[1:])) < 0.02: return links(p, n, CH, lt)
        if base == "iron" and min(segd(p, a_, b_)[0] for a_, b_ in zip(AC, AC[1:])) < 0.02: return links(p, n, AC, lt)
        if base == "rubber": return C["ink"] if s_ < 0.4 else C["irond"]   # the detonator wire
        if base == "brass": return lt(C["brassl"] if s_ > 0.4 else C["brass"])
        if base == "charge": return C["bloodd"] if s_ < 0.5 else C["blood"]   # wax seals
        if base == "parchment":   # the prayer strips: lines of script, a darker torn end
            s0, s1, acr = min(BSTRIPS, key=lambda s: segd(p, s[0], s[1])[0]); t = (p - s0).dot((s1 - s0).normalized()); ac = (p - s0).dot(acr)
            if abs(ac) > 0.0105 or t > 0.115: return C["chalk"].lerp(C["ink"], 0.4)
            if t > 0.03 and (t * 30) % 1.0 < 0.34 and abs(ac) < 0.0075: return C["ink"].lerp(C["chalk"], 0.3)
            return lit(C["chalk"], n, ao, p=p)
        return None
    def paint(m, p, n, ao, tx, ty):
        x, y, z = p.x, p.y, p.z
        base = m.split(".")[0]
        d_ = detail(base, p, n, ao)
        if d_ is not None: return d_
        if base in ("rust", "iron") and (p - centre).length < 0.25:   # the helm: a riveted rim, a seam, breathing holes, rust
            q = p - centre; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
            if base == "iron": return lit(C["rustl"] if n.dot(PSX_LIGHT) > 0.5 else C["rust"], n, ao, metal=True, p=p)   # spikes
            for a_, u_ in [(k * 0.028, r_ * 0.03 - 0.01) for k in range(-2, 3) for r_ in (0, -1)]:   # a breathing grille, rows of holes (nothing that reads as a face)
                if f > 0:
                    dd = math.hypot(a - a_, u - u_)
                    if dd < 0.007: return C["dark"]
                    if dd < 0.0105: return C["rustl"] if u > u_ else C["ink"]
            if -0.07 < u < -0.05: return C["ink"] if abs(u + 0.06) > 0.007 else lit(C["rustd"], n, ao, metal=True, p=p)   # the rim
            if abs(a) < 0.004 and u > -0.05: return C["ink"]                                     # the seam over the crown
            if abs(a) < 0.014 and u > -0.05 and (u * 40) % 1.0 < 0.25: return C["ironl"]        # its rivets
            c_ = C["rustd"] if _nz.noise(p * 22) > 0.35 else (C["rustl"] if n.dot(PSX_LIGHT) > 0.62 else C["rust"])
            if abs(math.sin(a * 140)) < 0.12 and u < 0.0 and _nz.noise(Vector((a * 30, 0, u * 4))) > 0.2: c_ = C["rustd"]   # rust runs
            return lit(c_, n, ao, metal=True, p=p)
        if base == "wood":   # the stocks board: grain, two iron bands, the holes the wrists are locked in
            t = (p - mid).dot(across)
            if abs(abs(t) - 0.13) < 0.016: return C["ink"] if abs(abs(t) - 0.13) > 0.012 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
            if min((p - fr).length, (p - fl).length) < 0.045: return C["ink"]
            if abs(abs(t) - 0.155) < 0.008 and n.dot(PSX_LIGHT) > 0.0 and abs(p.z - mid.z) < 0.012: return C["ironl"]   # nail heads at the ends
            if abs(t) < 0.035 and n.z > 0.5 and (abs(t) < 0.007 or abs((p - mid).dot(across.cross(Vector((0, 0, 1))).normalized())) < 0.007): return C["woodd"]   # a cross cut into the top
            g = math.sin(t * 90 + _nz.noise(p * 12) * 3)
            return lit(C["woodd"] if g > 0.8 else (C["woodl"] if n.dot(PSX_LIGHT) > 0.6 else C["wood"]), n, ao, grime=0.2, p=p)
        if base in ("bomb", "iron"):   # the bomb: olive drab, a stencilled band at the nose, rivet rows, the rope round it, scrawled prayers
            t = (p - base_).dot(ax)
            if base == "iron": return lit(C["bombd"], n, ao, metal=True, p=p)
            if abs(t - 0.3) < 0.016: return rope(abs(t - 0.3), (p - base_).dot(side_))
            if 0.05 < t < 0.09: return C["ink"] if abs(t - 0.07) > 0.015 else lit(C["stencil"], n, ao, p=p)
            if abs(t - 0.41) < 0.003 or abs(t - 0.13) < 0.003: return C["ink"]
            if abs(t - 0.405) < 0.012 and (math.atan2((p - base_).dot(side_), (p - base_).dot(out_)) * 6) % 1.0 < 0.2: return C["ironl"]
            r2 = (p - base_).dot(side_)
            if 0.15 < t < 0.27 and abs(r2) < 0.05 and n.dot(p - (base_ + ax * t)) > 0 and (t * 150) % 1.0 < 0.3 and _nz.noise(p * 90) > -0.1 and (p - base_).dot(out_) > 0.03: return C["chalk"]
            c_ = C["bombl"] if n.dot(PSX_LIGHT) > 0.62 else C["bomb"]
            if _nz.noise(p * 20) > 0.45: c_ = C["rust"]
            return lit(c_, n, ao, metal=True, p=p)
        if base == "rags":   # the loincloth: filthy, a rope girdle, a torn dark hem, blood
            if z > 0.94: return rope(abs(z - 0.955) * 0.6, math.atan2(x, y))
            b_ = blood(p, 0.05)
            if b_ is not None: return b_
            if z < 0.83: return lit(C["ragsd"], n, ao, grime=0.4, p=p)
            return lit(C["ragsd"] if math.sin(x * 120 + _nz.noise(p * 8) * 3) > 0.8 else C["rags"], n, ao, grime=0.25, p=p)
        # the bare skin: gaunt, grimed, lashed and bloodied; ropes, cuffs, ribs
        side, along, ad = psx_arm(p)
        if 0.08 < z < 0.15:   # the iron cuffs at the ankles, a rivet at the front, chafed raw above
            if abs(z - 0.115) > 0.024: return C["ink"]
            if n.y < -0.6 and abs(z - 0.115) < 0.008 and abs(n.x) < 0.25: return C["ironl"] if z > 0.115 else C["ink"]
            return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.5 else C["irond"], n, ao, metal=True, p=p)
        if 0.15 < z < 0.175: return C["sore"]
        if ad < 0.06 and abs(along - 1.82) < 0.06:   # and at the wrists
            if abs(along - 1.82) > 0.045: return C["ink"]
            return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.5 else C["irond"], n, ao, metal=True, p=p)
        on_arm = ad < 0.055 and z > 1.0
        in_fist = min((p - FISTS[s][0]).length for s in FISTS) < 0.07   # the bare fists, never painted with the ropes
        if not on_arm and not in_fist and 0.95 < z < 1.45:
            for a_, b_ in S:   # the ropes crossed over the chest and back
                d_ = Vector((x, z)) - a_; ab = b_ - a_; t = max(0.0, min(1.0, d_.dot(ab) / ab.length_squared))
                dd = (Vector((x, z)) - (a_ + ab * t)).length
                if dd < 0.018: return C["roped"] if dd > 0.0135 else lit(C["rope"], n, ao, p=p)   # plain: the (x, z) frame squashes twists on the stooped chest into specks
            if 0.955 < z < 0.985: return rope(abs(z - 0.97) * 0.8, x + y)                      # the girdle
        c_ = C["skin"]
        if not on_arm and y < 0.0 and 1.08 < z < 1.3 and 0.03 < abs(x) < 0.13:   # ribs showing through
            if math.sin(z * 150 - abs(x) * 60) > 0.75: c_ = C["skind"]
        if not on_arm and y > 0.0 and 1.0 < z < 1.42:   # lash wounds across the back
            if abs(math.sin((z + x * 0.6) * 95)) < 0.09 and _nz.noise(p * 15) > -0.1: return C["blood"] if _nz.noise(p * 50) > 0 else C["bloodd"]
        if not on_arm and y < 0.0 and 1.22 < z < 1.32 and 0.04 < x < 0.12:   # a penitent's brand on the left breast, a cross burned in
            q = Vector((x - 0.08, z - 1.27))
            if (abs(q.x) < 0.008 and abs(q.y) < 0.04) or (abs(q.y - 0.012) < 0.008 and abs(q.x) < 0.026): return C["sore"]
            if (abs(q.x) < 0.013 and abs(q.y) < 0.045) or (abs(q.y - 0.012) < 0.013 and abs(q.x) < 0.031): return C["skind"]
        b_ = blood(p, 0.06 if z < 0.5 else 0.0)
        if b_ is not None: return b_
        if _nz.noise(p * 13 + Vector((9, 0, 0))) > 0.64: c_ = C["sore"]                           # bruises and sores, in patches
        if n.dot(PSX_LIGHT) > 0.62 and c_ is C["skin"]: c_ = C["skinl"]
        return lit(c_, n, ao, grime=0.35 if z < 0.4 else 0.12, p=p)
    PAINT = paint

def war_prophet():
    # War Prophet (2026-11-03, fourth figure of docs/art-pipeline.md, class B, 32 mm): an original figure on the owner's
    # reference cues, not a copy of the miniature, the art or any paint job. A plate-armoured preacher in a long robe, a
    # closed pointed iron helm with a dark eye slit (no face, no head under it), the wooden cross frame on his back with
    # loudspeaker horns and bells, a scroll of scripture on his chest, and the anti-tank hammer: a long pole with a pick
    # head and a red explosive charge. Painted PSX look (2026-11-10, as the Combat Engineer): a smooth low-poly body, the
    # robe draped (in its own node, Robe, for its texels), toeless boot hulls; the cuirass (breastplate, faulds, ridge,
    # rivets, rust), the plated arms and gauntlets, the gorget, the scroll and its seal, the belt, the frame's straps, the
    # boots, the robe's folds, blood and mud painted; the helm's slit, breathing holes, brass cross and rivet band, the
    # horns' mouths, the lashings and the charge's white cross painted on their solid shapes. Solid only for the
    # silhouette: Helmet (the pointed helm), Chest (pauldrons), Back (cross frame, horns, bells), Hip (the tabard
    # crosses), Weapon (anti-tank hammer), Robe, Accent (tabard panels, pauldron tops, prayer pennants). Detail pass
    # (2026-11-10): a chained prayer book and two belt pouches (Hip), bell clappers, corner braces and a reliquary box on
    # the frame, the charge's fuse cap and clamp made solid; a woven border, patches and a rope cincture on the robe,
    # helm rivets, scratches and rust, gilt edging, dents and gouges on the cuirass, boot buckles and mud painted.
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1000
    TEX_SIZES = {"Body": 256, "Robe": 256, "Helmet": 128, "Chest": 128, "Back": 128, "Weapon": 128, "Hip": 64}
    from mathutils import noise as _nz
    import bmesh as _bm
    for k_ in ("helm", "pauldron"): M.setdefault(k_, mat(k_, "7a7c80", 0.3, 18))   # plate, told apart from the body's when painted
    def pose(r):
        human.reach(r, "R", P(-0.28, 0.02, 1.12), P(-0.3, -0.12, 1.22))
        human.reach(r, "L", P(0.25, 0.0, 1.08), P(0.27, -0.06, 0.88))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "boots", "thigh": "boots", "torso": "plate"}, head=None,
                                mitts="plate", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1918)
    human_ = bpy.data.objects["human"]
    for v in human_.data.vertices:   # the cuirass's bulk over the chest and back
        if 1.0 < v.co.z < 1.46: v.co += v.normal * 0.012
    toeless_feet()
    module("Robe")
    # the long robe under the armour, its hem ragged; in its own node, so it does not take the cuirass's texels
    robe = cloth_cone(0, 0, 0.98, 0.12, 0.18, 0.25, "robe", sy=0.84, ragged=0.06, rnd=rnd, tris=220, thick=0.012, rows=10)
    hem = hem_line(robe)
    # the legs inside the closed robe are never seen: removed (owner: hidden parts go up front)
    bm = _bm.new(); bm.from_mesh(human_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if 0.22 < (c := f.calc_center_median()).z < 0.95 and abs(c.x) < 0.2 and abs(c.y) < 0.18], context='FACES')
    bm.to_mesh(human_.data); bm.free()
    # tabard panels in the seat colour, front and back, hanging from the belt
    fronts = []
    for y_top, out in ((-0.17, Vector((0, -1, 0))), (0.15, Vector((0, 1, 0)))):
        for x in (-0.075, 0.075):
            pn = cloth_panel([(x - 0.06, y_top, 1.0), (x + 0.06, y_top, 1.0)], 0.5, "accent", out=out, extra=[robe], cols=1, rows=4, tris=None, bend=30.0)
            if out.y < 0: fronts.append((x, pn))
    module("Chest")
    # pauldrons of two lames on each shoulder (the cuirass and the scroll are painted on the body)
    for x in (-1, 1):
        for i, (z, sc) in enumerate(((1.47, 1.0), (1.41, 0.92))):
            # the top lame in the seat colour, a heraldic shoulder the camera above looks straight down on
            sphere((0.2 * x, -0.0, z), 1, "accent" if i == 0 else "pauldron", (0.11 * sc, 0.12 * sc, 0.06), seg=8, rings=4)
    module("Hip")
    for x, pn in fronts:   # a bone cross on each front panel, lying on it
        ok_, at_, *_ = pn.ray_cast(Vector((x, -0.8, 0.8)), Vector((0, 1, 0)))
        at_ = (at_ if ok_ else Vector((x, -0.205, 0.8))) - Vector((0, 0.006, 0))
        box(tuple(at_), (0.016, 0.006, 0.12), "bone"); box(tuple(at_ + Vector((0, 0, 0.02))), (0.06, 0.006, 0.016), "bone")
    # detail pass (2026-11-10): a chained prayer book on the left hip, two pouches on the belt at the right front
    def on_robe(x_, z_, d_):   # a point on the robe at height z_, seen from outside along d_
        o_ = Vector((x_, 0, z_)) - d_ * 0.9; ok_, at_, *_ = robe.ray_cast(o_, d_)
        return at_ if ok_ else o_ + d_ * 0.7
    BOOK = on_robe(0, 0.86, Vector((-1, 0.35, 0)).normalized()) + Vector((0.032, -0.012, 0))
    box(tuple(BOOK), (0.034, 0.062, 0.08), "leather", rot=(0, 0, math.radians(-19)))   # (its page edges painted)
    bar(BOOK + Vector((-0.012, 0.004, 0.04)), Vector((0.165, 0.03, 0.97)), 0.005, "iron", sides=3)                  # its chain to the belt
    POUCH = []
    for x_ in (-0.1, -0.155):
        at_ = on_robe(x_, 0.94, Vector((0, 1, 0))) + Vector((0, -0.016, 0)); POUCH.append(at_)
        box(tuple(at_), (0.044, 0.026, 0.05), "leather", rot=(0, 0, math.atan2(at_.x, -at_.y) * 0.8))
    module("Helmet")
    # the closed helm: a tall iron shell over the whole head rising to a point (eye slit and brass cross painted)
    fb = FACE_BOX; centre = fb["mid"] - fb["fwd"] * 0.04
    # one shell, so the face part and the point meet with no ledge: a ring at the chin, a ring at the point's base (the
    # widest), then the apex; the face part narrows a little to the chin
    rings = [(centre - fb["up"] * 0.15, 0.07), (centre + fb["up"] * 0.09, 0.08)]
    apex = centre + fb["up"] * 0.47
    n_ = 12; verts = []
    for at, r in rings:
        verts += [tuple(at + fb["ax"] * (r * math.cos(2 * math.pi * k / n_)) + fb["fwd"] * (r * 1.12 * math.sin(2 * math.pi * k / n_))) for k in range(n_)]
    verts.append(tuple(apex))
    faces = [(k, (k + 1) % n_, n_ + (k + 1) % n_, n_ + k) for k in range(n_)]
    faces += [(n_ + k, n_ + (k + 1) % n_, 2 * n_) for k in range(n_)]
    faces.append(tuple(reversed(range(n_))))   # closed under the chin
    me = bpy.data.meshes.new("helm"); me.from_pydata(verts, [], faces); me.validate()
    bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free(); me.update()   # facing out
    hm = bpy.data.objects.new("helm", me); scn.collection.objects.link(hm); add(hm, "helm")
    module("Back")
    # the cross frame: a post behind the head, a crossbar above the shoulders with a loudspeaker horn at each end, a short
    # bar at the top, bells hung under the crossbar, and prayer pennants in the seat colour
    post_y = 0.17
    bar((0, post_y, 1.05), (0, post_y, 2.0), 0.024, "wood", sides=4)
    bar((-0.33, post_y, 1.74), (0.33, post_y, 1.74), 0.022, "wood", sides=4)
    bar((-0.09, post_y, 1.92), (0.09, post_y, 1.92), 0.018, "wood", sides=4)
    horns = []
    for x in (-1, 1):
        mouth = Vector((0.36 * x, post_y - 0.05, 1.8)); hx = Vector((-0.6 * x, 0.6, -0.5)).normalized()
        bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.07, radius2=0.022, depth=0.15, end_fill_type='NOTHING', location=tuple(mouth))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'
        o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(hx)
        o.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; add(o, "brass"); horns.append((mouth, hx))
        bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.04, radius2=0.018, depth=0.06, location=(0.2 * x, post_y, 1.66)); add(bpy.context.object, "iron")
        sphere((0.2 * x, post_y, 1.624), 0.012, "brass", seg=5, rings=3)                                   # the bell's clapper (detail pass)
        bar((0.2 * x, post_y, 1.69), (0.2 * x, post_y, 1.74), 0.004, "iron", sides=3)                        # and its hanger
        box((0.27 * x, post_y + 0.01, 1.6), (0.035, 0.008, 0.2), "accent")
    # detail pass (2026-11-10): the frame's iron corner braces and a reliquary box at the cross's heart
    for x in (-1, 1):
        bar((0.012 * x, post_y + 0.02, 1.64), (0.11 * x, post_y + 0.02, 1.725), 0.008, "iron", sides=3)
    RELIQ = Vector((0, post_y + 0.03, 1.74))
    box(tuple(RELIQ), (0.07, 0.04, 0.08), "gilt")
    module("Weapon")
    # the anti-tank hammer, held upright in the right fist: a long pole, a pick head and a red explosive charge (its
    # white cross painted)
    fr, _ = FISTS["R"]
    low, high = Vector((fr.x, fr.y, 0.05)), Vector((fr.x, fr.y, 1.9))
    bar(low, high, 0.016, "wood", sides=5)
    bar(high - Vector((0, 0, 0.03)), high + Vector((0, -0.2, -0.06)), 0.014, "iron", sides=3)
    bar(high - Vector((0, 0, 0.03)), high + Vector((0, 0.08, 0.0)), 0.012, "iron", sides=3)
    bar(high, high + Vector((0, 0, 0.07)), 0.01, "iron", sides=3)
    ch = high + Vector((0, -0.07, -0.16))
    bar(ch + Vector((0, 0, 0.07)), ch - Vector((0, 0, 0.07)), 0.034, "charge", sides=8)
    bar(ch + Vector((0, 0, 0.07)), ch + Vector((0, 0, 0.095)), 0.012, "brass", sides=5)                 # the fuse cap (detail pass)
    bar(ch + Vector((0, 0.03, 0.03)), Vector((fr.x, fr.y, ch.z + 0.03)), 0.007, "iron", sides=3)        # the clamp holding the charge to the pole
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **KIT, **dict(plate="4c4e52", plated="2c2e32", platel="72747a", robe="3e3428", robed="221c15",
                    robel="574a3a", boot="2a241d", bootl="463c30")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    nz_ = NECK.z
    def steel(c, n, ao, p, rust=0.45):   # plate: hard bands, a glint, rust streaks running down
        if _nz.noise(Vector((p.x * 40, p.y * 40, p.z * 5))) > rust: c = c.lerp(C["rustd"], 0.6)
        return lit(C["platel"] if c is C["plate"] and n.dot(PSX_LIGHT) > 0.72 else c, n, ao, metal=True, p=p)
    def mail(p, n, ao):
        return C["ink"] if (p.z * 240) % 1.0 < 0.3 and ((p.x + p.y) * 120 + (p.z * 240 // 1) * 0.5) % 1.0 < 0.4 else lit(C["metall"] if n.dot(PSX_LIGHT) > 0.4 else C["metal"], n, ao, metal=True, p=p)
    def lames(z, n, ao, p, edges):   # plate in lames: an ink line at each step, light above it, shadow under it
        c = C["plate"]
        for L_ in edges:
            if abs(z - L_) < 0.0075: return C["ink"]
            if 0 < L_ - z < 0.016: c = C["plated"]
            elif 0 < z - L_ < 0.008: c = C["platel"]
        return steel(c, n, ao, p)
    def belt(x, y, z, n, ao, p):   # the leather belt over the robe's top, a brass buckle at the front
        if z < 0.96 or z > 1.026: return C["ink"]
        if y < 0 and abs(x) < 0.026:
            if abs(x) > 0.021 or abs(z - 0.993) > 0.026: return C["ink"]
            return C["dark"] if abs(x) < 0.009 and abs(z - 0.993) < 0.012 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.4 else C["brass"])
        return lit(C["leatherl"] if abs(z - 0.993) < 0.004 else C["leather"], n, ao, p=p)
    SC = (0.08, 1.21, 1.39)   # the scroll on the chest: x, bottom, top
    LAMES = (1.165, 1.12, 1.075, 1.03)
    def paint(m, p, n, ao, tx, ty):
        base = m.split(".")[0]; x, y, z = p.x, p.y, p.z
        if base == "robe":
            if z > 0.955: return belt(x, y, z, n, ao, p)
            h_ = z - hem(p); a_ = math.degrees(math.atan2(x, -y))
            # detail pass (2026-11-10): a woven border above the hem with a row of small crosses, two stitched patches, a
            # knotted rope cincture down the left front, mud caked up from the hem
            BZ = 0.25   # the border sits at a fixed height (the ragged hem's lowest points would hide one that followed it)
            if h_ > 0.03 and abs(z - BZ) < 0.03:
                if abs(z - BZ) > 0.024: return C["ink"]
                cx_ = ((a_ + 180) / 15) % 1.0 - 0.5; cz_ = z - BZ
                if (abs(cx_) < 0.07 and abs(cz_) < 0.016) or (abs(cx_) < 0.22 and abs(cz_ - 0.004) < 0.005): return lit(C["bonel"].lerp(C["robe"], 0.45), n, ao, p=p)
                if abs(abs(cz_) - 0.018) < 0.003: return lit(C["robel"], n, ao, p=p)
                return lit(C["robed"], n, ao, p=p)
            for pa, pz, pw, ph in ((-56, 0.42, 7.0, 0.07), (128, 0.6, 8.0, 0.06)):   # patches sewn on: a lighter cloth, an ink rim, stitches
                du, dv = (a_ - pa) / pw, (z - pz) / ph
                if abs(du) < 1 and abs(dv) < 1:
                    if abs(du) > 0.86 or abs(dv) > 0.84: return C["ink"]
                    if (abs(du) > 0.7 or abs(dv) > 0.68) and ((du + dv) * 6) % 1.0 < 0.5: return C["roped"]
                    return lit(C["robel"].lerp(C["mud"], 0.25), n, ao, p=p)
            if y < 0 and 0.62 < z:   # the rope cincture hanging from the belt, two knots and a frayed end
                cxr = 0.158 + (0.955 - z) * 0.05
                if abs(x - cxr) < 0.012:
                    if abs(x - cxr) > 0.008: return C["ink"]
                    if any(abs(z - kz) < 0.013 for kz in (0.86, 0.75)): return lit(C["rope"], n, ao, p=p) if abs(x - cxr) < 0.006 else C["roped"]
                    if z < 0.66: return C["roped"] if (x * 300) % 1.0 < 0.4 else lit(C["rope"], n, ao, p=p)
                    return C["roped"] if ((z - x) * 90) % 1.0 < 0.3 else lit(C["rope"], n, ao, p=p)
            return psx_robe(C, "robe", p, n, ao, hem, blood=0.3)
        if base == "boots":   # tall boots under the robe: strapped shafts, a light top on the foot, a dark sole
            if z < 0.02: return lit(C["sole"], n, ao, p=p)
            for sz in (0.07, 0.15):
                if abs(z - sz) < 0.012:
                    if abs(z - sz) > 0.008: return C["ink"]
                    if n.x * (1 if x > 0 else -1) > 0.5 and abs(y - (-0.01 if sz < 0.1 else 0.0)) < 0.014: return C["brassl"] if z > sz else C["brass"]   # the buckles, outside (detail pass)
                    return lit(C["leather"], n, ao, p=p)
            if z < 0.06 and _nz.noise(p * 45) > 0.0: return lit(C["mud"], n, ao, p=p)                 # caked mud (detail pass)
            if n.y < -0.5 and n.z > 0.3 and z < 0.06: return C["bootl"]                                # the scuffed toe
            return lit(C["bootl"] if n.z > 0.6 else C["boot"], n, ao, grime=0.3, p=p)
        if base == "helm":   # the pointed helm: eye slit, breathing holes, a brass cross, a rivet band, rust
            q = p - centre; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
            if u < -0.135: return C["ink"]                                                     # the rolled rim at the chin
            if f > 0.02:
                if abs(a) < 0.034 and abs(u - 0.02) < 0.011: return C["ink"] if abs(u - 0.02) < 0.008 else lit(C["platel"] if u > 0.02 else C["plated"], n, ao, metal=True, p=p)
                if a > 0.008 and -0.075 < u < -0.02 and (a * 110) % 1.0 < 0.4 and (u * 110) % 1.0 < 0.4: return C["ink"]   # breathing holes, right cheek
                if (abs(a) < 0.0075 and abs(u - 0.165) < 0.04) or (abs(a) < 0.024 and abs(u - 0.18) < 0.0075):   # the brass cross
                    return C["brassl"] if (abs(a) < 0.004 and abs(u - 0.165) < 0.036) or (abs(a) < 0.02 and abs(u - 0.18) < 0.004) else C["ink"]
                if abs(a) < 0.004 and (u > 0.21 or u < -0.03): return lit(C["platel"], n, ao, metal=True, p=p)   # the ridge
            if abs(u - 0.09) < 0.014:                                                           # the band at the point's base
                if abs(u - 0.09) > 0.01: return C["ink"]
                ang = math.atan2(a, f)
                if (ang * 12 / (2 * math.pi)) % 1.0 < 0.3: return C["brassl"] if n.dot(PSX_LIGHT) > 0.2 else C["brass"]
                return lit(C["plated"], n, ao, metal=True, p=p)
            if -0.135 < u < -0.12: return lit(C["platel"], n, ao, metal=True, p=p)
            # detail pass (2026-11-10): rivets framing the face plate, a riveted seam up the back, rust weeping from the
            # slit, bright scratches and a dent catching the light
            def stud(pts):   # a small domed rivet: a bright top, a dark underside, no ring (rings read as eyes)
                for sa, su in pts:
                    if math.hypot(a - sa, u - su) < 0.0055: return C["platel"] if u > su else C["plated"]
                return None
            if f > 0.0:
                rv = stud([(sx * 0.052, rz) for sx in (-1, 1) for rz in (-0.1, -0.05, 0.0, 0.05)])
                if rv is not None: return rv
                if abs(a) < 0.03 and -0.0 < 0.009 - u < 0.05 and abs(a + 0.012 - (u * 0.2)) < 0.004 and _nz.noise(Vector((a * 80, 0, 0))) > -0.3: return lit(C["rust"], n, ao, p=p)
                if -0.024 < a < -0.006 and 0.12 < u < 0.2 and abs((a + 0.015) - (u - 0.16) * 0.35) < 0.0022: return C["platel"]
                if math.hypot(a + 0.03, u - 0.055) < 0.012: return C["platel"] if a + 0.03 < 0 and u > 0.055 else C["plated"]
            elif f < -0.03:
                if abs(a) < 0.004: return C["ink"]
                rv = stud([(sx * 0.014, rz) for sx in (-1, 1) for rz in (-0.09, -0.03, 0.03, 0.15, 0.24)])
                if rv is not None: return rv
            return steel(C["plate"], n, ao, p, rust=0.35)
        if base == "pauldron":   # the lower lame: an ink rim, a light edge above it, a row of brass rivets
            sx = 1 if x > 0 else -1; q = p - Vector((0.2 * sx, 0, 1.41))
            if q.z < -0.038: return C["ink"]
            if q.z < -0.028: return lit(C["platel"], n, ao, metal=True, p=p)
            if abs(q.z + 0.012) < 0.006 and (math.atan2(q.y, q.x) * 10 / (2 * math.pi)) % 1.0 < 0.3: return C["brassl"]
            return steel(C["platel"] if n.z > 0.6 else C["plate"], n, ao, p)
        if base == "brass":   # the horns: an ink rim at the mouth, a dark throat, a band
            for mo, hx in horns:
                q = p - mo; t = q.dot(hx); rr = (q - hx * t).length
                if abs(t) < 0.09 and rr < 0.09:
                    if t < -0.068: return C["ink"]
                    if n.dot((q - hx * t).normalized()) < -0.1: return C["dark"] if t > -0.04 else C["brass"].lerp(C["dark"], 0.6)
                    if abs(t - 0.02) < 0.006: return C["ink"]
                    if t < -0.055: return C["brassl"]
            return psx_kit(C, base, p, n, ao)
        if base == "wood" and abs(y - post_y) < 0.04 and z > 1.0:   # the frame: rope lashings at the joints, iron caps at the bar ends
            for jz in (1.74, 1.92):
                if abs(x) < 0.034 and abs(z - jz) < 0.034: return C["roped"] if ((x + z) * 120) % 1.0 < 0.35 else lit(C["rope"], n, ao, p=p)
            if abs(x) > 0.3 or z > 1.97: return C["ink"] if abs(abs(x) - 0.3) < 0.004 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.5 else C["iron"], n, ao, metal=True, p=p)
            return psx_kit(C, base, p, n, ao)
        if base == "wood" and abs(z - fr.z) < 0.08:   # the hammer's leather grip
            return C["ink"] if (z * 90) % 1.0 < 0.2 else lit(C["leather"], n, ao, p=p)
        if base == "charge":   # the charge: iron end bands, a white cross on its face, a stencilled band
            dz = z - ch.z
            if abs(dz) > 0.054: return C["ink"] if abs(dz) < 0.059 else lit(C["metall"] if n.dot(PSX_LIGHT) > 0.4 else C["metal"], n, ao, metal=True, p=p)
            if n.y < -0.5:
                cx_ = x - ch.x
                if (abs(cx_) < 0.007 and abs(dz) < 0.042) or (abs(cx_) < 0.024 and abs(dz - 0.014) < 0.007): return C["bonel"]
                if (abs(cx_) < 0.011 and abs(dz) < 0.046) or (abs(cx_) < 0.028 and abs(dz - 0.014) < 0.011): return C["ink"]
            if abs(dz - 0.03) < 0.006 or abs(dz + 0.03) < 0.006: return C["ink"] if abs(abs(dz) - 0.03) > 0.004 else lit(C["leather"], n, ao, p=p)   # lashings (detail pass)
            if n.y > 0.3 and abs(dz) < 0.02: return C["parchmentd"] if (dz * 160) % 1.0 < 0.35 else lit(C["parchment"], n, ao, p=p)   # a stencilled warning label at the back
            return lit(C["chargel"] if n.dot(PSX_LIGHT) > 0.6 else C["charge"], n, ao, p=p)
        s_ = n.dot(PSX_LIGHT)
        if base == "leather" and (p - BOOK).length < 0.12:   # the prayer book: tooled cover, brass corners, a gilt cross, a clasp
            q = p - BOOK; u_ = q.dot(Vector((math.cos(math.radians(-19)), math.sin(math.radians(-19)), 0))); w_ = q.dot(Vector((-math.sin(math.radians(-19)), math.cos(math.radians(-19)), 0)))
            if abs(u_) > 0.0145:   # a cover (box half sizes 0.017 thick, 0.031 wide, 0.04 tall)
                if abs(w_) > 0.023 and abs(q.z) > 0.031: return C["brassl"] if s_ > 0.3 else C["brass"]   # the corners
                if abs(w_) > 0.028 or abs(q.z) > 0.037: return C["ink"]
                if (abs(w_) < 0.004 and abs(q.z) < 0.024) or (abs(w_) < 0.014 and abs(q.z - 0.008) < 0.004): return C["giltl"] if s_ > 0.2 else C["gilt"]
                if abs(abs(w_) - 0.021) < 0.002 or abs(abs(q.z) - 0.03) < 0.002: return C["ink"]           # the tooled border
            elif w_ > 0.027: return C["ink"] if (q.z * 70) % 1.0 < 0.2 else lit(C["leather"], n, ao, p=p)   # raised bands on the spine
            elif abs(q.z) < 0.006 and w_ < -0.027: return C["brassl"] if q.z > 0 else C["brass"]       # the clasp over the fore-edge
            elif abs(q.z) < 0.037 and abs(w_) < 0.027 or w_ < -0.027: return C["parchmentd"] if (abs(u_) * 600) % 1.0 < 0.3 else lit(C["parchment"], n, ao, p=p)   # the page edges
            return lit(C["leatherl"] if s_ > 0.55 else C["leather"], n, ao, grime=0.2, p=p)
        if base == "leather" and POUCH and min((p - q_).length for q_ in POUCH) < 0.07:   # the belt pouches (half 0.022 x 0.013 x 0.025): a flap, a stud
            q = p - min(POUCH, key=lambda q_: (p - q_).length)
            if q.z > 0.022: return C["ink"]
            if abs(q.z - 0.002) < 0.0025: return C["ink"]
            if q.z > 0.002:
                if abs(q.z - 0.009) < 0.004 and abs(q.x) < 0.004: return C["brassl"] if q.z > 0.009 else C["brass"]
                return lit(C["leatherl"] if s_ > 0.3 else C["leather"], n, ao, p=p)
            if q.z < -0.012 and _nz.noise(p * 50) > 0.2: return lit(C["mud"], n, ao, p=p)
            return lit(C["leather"], n, ao, grime=0.3, p=p)
        if base == "gilt" and (p - RELIQ).length < 0.13:   # the reliquary (half 0.035 x 0.02 x 0.04): a gilt frame, a dark window with a relic bone
            q = p - RELIQ
            if abs(q.y) > 0.017:
                if abs(q.x) > 0.032 or abs(q.z) > 0.037: return C["ink"]
                if abs(q.x) < 0.02 and abs(q.z) < 0.026:
                    if abs(q.x) > 0.016 or abs(q.z) > 0.022: return C["ink"]
                    if abs(q.x - q.z * 0.3) < 0.005 and abs(q.z) < 0.018: return C["bonel"] if q.x < q.z * 0.3 else C["bone"]
                    return C["bloodd"] if _nz.noise(p * 90) > 0.3 else C["dark"]
                if psx_dot(p, [RELIQ + Vector((sx * 0.026, q.y, sz * 0.031)) for sx in (-1, 1) for sz in (-1, 1)], C, 0.0035) is not None: return C["ink"] if s_ < 0 else C["giltl"]
            return lit(C["giltl"] if s_ > 0.5 else (C["gilt"] if s_ > -0.2 else C["giltd"]), n, ao, metal=True, p=p)
        if base != "plate": return psx_kit(C, base, p, n, ao) or lit(C["metal"], n, ao, p=p)
        # ---- the body's plate ----
        for s_, (fc, fd) in FISTS.items():   # the gauntlets: a flared cuff, knuckle plates
            along = (p - fc).dot(fd)
            if (p - fc).length < 0.075:
                if along < -0.034: return C["ink"] if along > -0.04 else steel(C["plated"], n, ao, p)
                if abs(along + 0.005) < 0.004 or abs(along - 0.018) < 0.004: return C["ink"]
                return steel(C["plate"], n, ao, p, rust=0.6)
        side_, along, ad = psx_arm(p); s_ = n.dot(PSX_LIGHT)   # (the gauntlet loop above reuses s_)
        on_arm = ad < 0.08 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0
        if on_arm:   # rerebrace lames, a round couter at the elbow, the vambrace with two bands, mail at the wrist
            if along < 0.12: return mail(p, n, ao)
            if along > 1.88: return mail(p, n, ao)
            if abs(along - 1.0) < 0.14:
                if abs(along - 1.0) > 0.12: return C["ink"]
                if abs(along - 1.0) < 0.03 and ad > 0.04: return C["brassl"]
                return steel(C["platel"] if along < 1.0 else C["plate"], n, ao, p)
            if along < 1.0:
                fr_ = (along * 6) % 1.0
                if fr_ < 0.14: return C["ink"]
                return steel(C["platel"] if fr_ < 0.25 else (C["plated"] if fr_ > 0.85 else C["plate"]), n, ao, p)
            for b_ in (1.3, 1.74):
                if abs(along - b_) < 0.05: return C["ink"] if abs(along - b_) > 0.035 else lit(C["leather"], n, ao, p=p)
            return steel(C["plate"], n, ao, p)
        if z > nz_ - 0.045:   # the gorget: overlapping rings
            fr_ = (z * 70) % 1.0
            return C["ink"] if fr_ < 0.15 else steel(C["platel"] if fr_ > 0.75 else C["plate"], n, ao, p)
        if z < 1.026: return belt(x, y, z, n, ao, p)
        front = y < 0
        if front:
            if abs(x - SC[0]) < 0.052 and SC[1] < z < SC[2]:   # the scroll: rolled ends, lines of scripture, a red wax seal
                q = x - SC[0]
                if abs(q) > 0.046 or z < SC[1] + 0.005 or z > SC[2] - 0.005: return C["ink"]
                if z > SC[2] - 0.026 or z < SC[1] + 0.022: return lit(C["parchmentd"] if (z * 200) % 1.0 < 0.3 else C["parchment"], n, ao, p=p)
                if math.hypot(q + 0.01, z - (SC[2] - 0.045)) < 0.016: return C["blood"] if math.hypot(q + 0.01, z - (SC[2] - 0.045)) < 0.011 else C["bloodd"]
                if abs(q) < 0.034 and (z * 75) % 1.0 < 0.3 and _nz.noise(p * 150) > -0.2: return C["ink"]
                return lit(C["parchment"], n, ao, p=p)
            for sz in (SC[1] + 0.04, SC[2] - 0.075):   # the straps holding it, round the chest
                if abs(z - sz) < 0.009 and abs(x - SC[0]) < 0.075: return C["ink"] if abs(z - sz) > 0.006 else lit(C["leather"], n, ao, p=p)
            rv = psx_dot(p, [Vector((sx * 0.12, y, rz + 0.02)) for sx in (-1, 1) for rz in LAMES] + [Vector((sx * 0.07, y, nz_ - 0.09)) for sx in (-1, 1)], C, 0.0065)
            if rv is not None: return rv
            top_ = nz_ - 0.06 - 3.0 * x * x
            if z > top_ - 0.012: return C["ink"] if z < top_ - 0.004 else steel(C["platel"], n, ao, p)   # the rolled neckline
            # detail pass (2026-11-10): an etched gilt border under the neckline, rust weeping from the rivets, a dent
            # and two sword gouges on the breast, gilt edges on the faulds
            if top_ - 0.026 < z < top_ - 0.018 and abs(x) < 0.15: return C["gilt"] if s_ > 0.3 else C["giltd"]
            for sx in (-1, 1):
                for rz in LAMES:
                    if abs(x - sx * 0.12) < 0.004 and rz - 0.012 < z < rz + 0.012 and _nz.noise(Vector((sx * 9, 0, rz * 40))) > -0.2: return lit(C["rust"], n, ao, p=p)
            if math.hypot(x + 0.06, z - 1.3) < 0.02: return C["platel"] if (x + 0.06) < -0.004 and z > 1.304 else (C["plated"] if math.hypot(x + 0.06, z - 1.3) > 0.012 else steel(C["plate"], n, ao, p))
            for gx, gz, gk in ((-0.03, 1.24, 0.6), (0.0, 1.21, -0.4)):
                if abs(x - gx) < 0.04 and abs((z - gz) - (x - gx) * gk) < 0.0022: return C["platel"]
            if z < LAMES[0] and any(abs(z - (L_ + 0.009)) < 0.0028 for L_ in LAMES) and abs(x) < 0.11: return C["gilt"] if s_ > 0 else C["giltd"]
            if abs(x) < 0.006 and z > LAMES[0]: return steel(C["platel"], n, ao, p)                     # the ridge
            return lames(z, n, ao, p, LAMES)
        if abs(n.x) > 0.8:   # the side straps joining breast and back plates
            for sz in (1.15, 1.3):
                if abs(z - sz) < 0.014: return C["ink"] if abs(z - sz) > 0.01 else (C["brass"] if abs(y) < 0.008 else lit(C["leather"], n, ao, p=p))
            return lit(C["plated"], n, ao, metal=True, p=p)
        for sx in (-1, 1):   # the frame's harness straps down the back, buckled
            if abs(x - sx * 0.075) < 0.014 and z > 1.05:
                if abs(x - sx * 0.075) > 0.01: return C["ink"]
                if abs(z - 1.25) < 0.012: return C["brassl"] if abs(z - 1.25) < 0.008 else C["ink"]
                return lit(C["leather"], n, ao, p=p)
        if abs(x) < 0.005 and z < LAMES[0]: return C["ink"]
        # detail pass (2026-11-10): rivets along each lame and up the backplate's edges, a scuffed patch where the frame rubs
        rv = psx_dot(p, [Vector((sx * 0.13, y, rz + 0.02)) for sx in (-1, 1) for rz in LAMES] + [Vector((sx * 0.13, y, rz)) for sx in (-1, 1) for rz in (1.25, 1.33)], C, 0.0065, "metal")
        if rv is not None: return rv
        if abs(x) < 0.04 and 1.08 < z < 1.42 and _nz.noise(p * Vector((60, 60, 12))) > 0.25: return C["platel"] if s_ > 0 else C["plate"]
        return lames(z, n, ao, p, LAMES)
    PAINT = paint

def gas_mask(fb, centre, fit=()):
    # The gas mask (the Castigator's, approved by the owner 2026-11-03; shared with the Principality). Built in the
    # head's frame fb (FACE_BOX or one like it) round centre, about the middle of the head. Returns the snout, where a
    # hose leaves it. fit: the head (and neck) objects to lay the harness straps on, where there is a head under it.
    import bmesh as _bm
    rot = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
    # The gas mask (the owner raised this figure's budget for it, 2026-11-03). A rubber face piece cut to a face-shaped
    # oval, its lower half drawn forward into the snout; two round eyepieces sealed with plain iron caps (no glass, no
    # rivets: nothing that reads as an eye); a sealing rim; a large filter canister with screw rings, a crossed grille and air holes; an
    # exhalation valve; a buckled head harness; and a ribbed hose with metal couplings.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=10, radius=1, location=tuple(centre))
    mk = bpy.context.object; mk.scale = (0.086, 0.092, 0.122); mk.rotation_euler = rot
    bpy.context.view_layer.update(); mw = mk.matrix_world
    bm = _bm.new(); bm.from_mesh(mk.data)
    def outside(f):
        q = (mw @ f.calc_center_median()) - centre
        return q.dot(fb["fwd"]) < 0.02 or (q.dot(fb["ax"]) / 0.072) ** 2 + ((q.dot(fb["up"]) + 0.01) / 0.112) ** 2 > 1
    _bm.ops.delete(bm, geom=[f for f in bm.faces if outside(f)], context='FACES')
    bm.to_mesh(mk.data); bm.free()
    mk.data.transform(mk.matrix_world); mk.matrix_world = Matrix.Identity(4)   # work in world space
    for v in mk.data.vertices:   # the snout: the lower face drawn forward, most at the middle
        q = v.co - centre
        u_, a_ = q.dot(fb["up"]), q.dot(fb["ax"])
        pull = max(0.0, (-0.01 - u_) / 0.09) * max(0.0, 1 - abs(a_) / 0.06)
        v.co += fb["fwd"] * 0.03 * min(pull, 1.0) ** 0.7
    mk.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; bpy.ops.object.select_all(action='DESELECT'); mk.select_set(True)
    bpy.context.view_layer.objects.active = mk; bpy.ops.object.shade_smooth(); add(mk, "rubber")
    def on_mask(a_, u_, out=0.0):   # a point on the face piece (before the snout pull), a little proud of it
        k = 1 - (a_ / 0.086) ** 2 - (u_ / 0.122) ** 2
        return centre + fb["ax"] * a_ + fb["up"] * u_ + fb["fwd"] * (0.092 * max(k, 0.0) ** 0.5 + out)
    def disc(at, r, depth, m, n=10, axis=None):
        bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=r, depth=depth, location=tuple(at))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis or fb["fwd"]); return add(o, m)
    def ring_(at, r, m, n=10, axis=None):
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.005, major_segments=n, minor_segments=4, location=tuple(at))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis or fb["fwd"]); return add(o, m)
    for x in (-0.034, 0.034):   # the eyepieces, riveted shut
        at = on_mask(x, 0.035, 0.004)
        ring_(at, 0.021, "metal", n=8); disc(at + fb["fwd"] * 0.002, 0.018, 0.006, "iron", n=8)
    rim = [on_mask(0.072 * math.cos(2 * math.pi * i / 14), -0.01 + 0.112 * math.sin(2 * math.pi * i / 14), 0.005) for i in range(14)]
    for a_, b_ in zip(rim, rim[1:] + rim[:1]): bar(a_, b_, 0.007, "leather", sides=4)
    snout = on_mask(0.0, -0.06, 0.025)
    down = (fb["fwd"] * 0.7 - fb["up"] * 0.71).normalized()                                     # the mouthpiece points down
    side = fb["ax"]; across = down.cross(side).normalized()
    bar(snout - down * 0.01, snout + down * 0.02, 0.03, "rubber", sides=10)                    # the snout's collar
    can0, can1 = snout + down * 0.02, snout + down * 0.08
    bar(can0, can1, 0.04, "iron", sides=12)                                                     # the filter canister
    for t in (0.25, 0.55):
        ring_(can0 + (can1 - can0) * t, 0.041, "metal", n=10, axis=down)                       # screw rings
    face_ = can1 + down * 0.002
    disc(face_, 0.036, 0.004, "metal", n=12, axis=down)
    grille = face_ + down * 0.004                                                               # the crossed grille
    bar(grille - side * 0.03, grille + side * 0.03, 0.005, "dark", sides=4); bar(grille - across * 0.03, grille + across * 0.03, 0.005, "dark", sides=4)
    valve = on_mask(0.0, -0.1, 0.012)                                                           # the exhalation valve
    bar(valve, valve + fb["fwd"] * 0.018 - fb["up"] * 0.008, 0.014, "iron", sides=8)
    disc(valve + fb["fwd"] * 0.02 - fb["up"] * 0.009, 0.016, 0.005, "metal", n=8)
    # the head harness: a strap round the head at the eyepieces, one at the jaw, with buckles at the temples
    if fit:   # the straps laid round the back of the head they hold on, from one side of the mask to the other
        for u_ in (0.035, -0.06):
            pts = []
            for k in range(6):
                t = math.pi * k / 5; o_ = fb["ax"] * math.cos(t) - fb["fwd"] * math.sin(t); far = centre + fb["up"] * u_ + o_ * 0.3
                hits = [loc for ob in fit for ok, loc, *_ in [ob.ray_cast(far, -o_)] if ok]
                if hits: pts.append(min(hits, key=lambda h_: (h_ - far).length) + o_ * 0.007)
            for a_, b_ in zip(pts, pts[1:]): bar(a_, b_, 0.006, "leather", sides=4)
            if u_ > 0 and len(pts) > 1:
                for e_ in (pts[0], pts[-1]): box(tuple(e_), (0.008, 0.02, 0.016), "brass", rot=rot)   # buckles at the temples
        return snout
    for u_ in (0.035, -0.06):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.094, minor_radius=0.006, major_segments=10, minor_segments=3, location=tuple(centre + fb["up"] * u_))
        o = bpy.context.object; o.scale = (1, 1.12, 1)
        bpy.context.view_layer.update(); ow = o.matrix_world
        bm = _bm.new(); bm.from_mesh(o.data)
        _bm.ops.delete(bm, geom=[f for f in bm.faces if ((ow @ f.calc_center_median()) - centre).dot(fb["fwd"]) > 0.02], context='FACES')
        bm.to_mesh(o.data); bm.free(); add(o, "leather")
    for x in (-1, 1):
        box(tuple(centre + fb["ax"] * 0.093 * x + fb["up"] * 0.035), (0.008, 0.02, 0.016), "brass", rot=rot)
    return snout
def ww1_gas_mask(fb, centre, fit=()):
    # A WWI face mask on the owner's references (2026-11-03: the Castigator's on the Yeoman "looks goofy"): a broad
    # proofed-cloth face piece from brow to under the chin, drawn forward at the mouth; two big round eyepieces in thick
    # metal rims with fogged, blank lenses (no pupils, nothing behind them); a squat filter drum screwed straight onto
    # the chin, angled down, with a screw ring; elastic straps round the back of the head (fit: the head objects).
    # Returns the drum's mouth.
    import bmesh as _bm
    rot = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
    A, F, U = 0.09, 0.095, 0.13
    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=10, radius=1, location=tuple(centre))
    mk = bpy.context.object; mk.scale = (A, F, U); mk.rotation_euler = rot
    bpy.context.view_layer.update(); mw = mk.matrix_world
    bm = _bm.new(); bm.from_mesh(mk.data)
    def outside(f):
        q = (mw @ f.calc_center_median()) - centre
        return q.dot(fb["fwd"]) < -0.005 or (q.dot(fb["ax"]) / 0.086) ** 2 + ((q.dot(fb["up"]) + 0.015) / 0.122) ** 2 > 1
    _bm.ops.delete(bm, geom=[f for f in bm.faces if outside(f)], context='FACES')
    bm.to_mesh(mk.data); bm.free()
    mk.data.transform(mk.matrix_world); mk.matrix_world = Matrix.Identity(4)
    for v in mk.data.vertices:   # the cloth drawn forward to the drum at the mouth
        q = v.co - centre; u_, a_ = q.dot(fb["up"]), q.dot(fb["ax"])
        pull = max(0.0, (-0.02 - u_) / 0.08) * max(0.0, 1 - abs(a_) / 0.07)
        v.co += fb["fwd"] * 0.035 * min(pull, 1.0) ** 0.6
    mk.modifiers.new("sol", "SOLIDIFY").thickness = 0.006; bpy.ops.object.select_all(action='DESELECT'); mk.select_set(True)
    bpy.context.view_layer.objects.active = mk; bpy.ops.object.shade_smooth(); add(mk, "maskcloth")
    def on_mask(a_, u_, out=0.0):
        k = 1 - (a_ / A) ** 2 - (u_ / U) ** 2
        return centre + fb["ax"] * a_ + fb["up"] * u_ + fb["fwd"] * (F * max(k, 0.0) ** 0.5 + out)
    def aim(o, axis): o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis)
    for x in (-0.041, 0.041):   # the eyepieces: a thick rim standing proud, a fogged lens set back in it
        ax_ = (fb["fwd"] + fb["ax"] * (0.3 if x > 0 else -0.3)).normalized(); e = on_mask(x, 0.032, 0.004)
        bpy.ops.mesh.primitive_torus_add(major_radius=0.031, minor_radius=0.008, major_segments=10, minor_segments=4, location=tuple(e + ax_ * 0.008))
        aim(bpy.context.object, ax_); add(bpy.context.object, "metal")
        bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.028, depth=0.004, location=tuple(e + ax_ * 0.004))
        aim(bpy.context.object, ax_); add(bpy.context.object, "lens")
    snout = on_mask(0.0, -0.075, 0.03); dn = (fb["fwd"] * 0.85 - fb["up"] * 0.5).normalized()
    bar(snout - dn * 0.012, snout + dn * 0.016, 0.03, "metal", sides=10)                     # the threaded mount
    bar(snout + dn * 0.016, snout + dn * 0.075, 0.041, "iron", sides=12)                      # the filter drum
    bar(snout + dn * 0.022, snout + dn * 0.03, 0.045, "metal", sides=12)                     # its screw ring
    bar(snout + dn * 0.075, snout + dn * 0.079, 0.037, "dark", sides=12)                      # the perforated face
    if fit:   # elastic straps round the back of the head, from one side of the face piece to the other
        for u_ in (0.04, -0.05):
            pts = []
            for k in range(6):
                t = math.pi * k / 5; o_ = fb["ax"] * math.cos(t) - fb["fwd"] * math.sin(t); far = centre + fb["up"] * u_ + o_ * 0.3
                hits = [loc for ob in fit for ok, loc, *_ in [ob.ray_cast(far, -o_)] if ok]
                if hits: pts.append(min(hits, key=lambda h_: (h_ - far).length) + o_ * 0.007)
            for a_, b_ in zip(pts, pts[1:]): bar(a_, b_, 0.007, "webbing", sides=4)
    return snout + dn * 0.079
def castigator():
    # Castigator (2026-11-03, fifth figure of docs/art-pipeline.md, class B, 32 mm): an original figure on the owner's
    # reference cues, not a copy of the miniature, the art or any paint job. A tall flat-topped hat with an Orthodox cross
    # over a dark hood, a gas mask of smooth dark rubber with a filter snout and a hose to the chest (no eye lenses:
    # the face stays blank; no head under it), mail sleeves, a long dark robe and cape, gilded icon plaques on the chest, a tall wooden reliquary
    # shield with a crucifix, severed hands and candles in the left hand, and a spiked flail raised in the right.
    # Painted PSX look (2026-11-10, as the Combat Engineer): a smooth low-poly body, the robe draped in its own node
    # (Robe), toeless boot hulls; the mail, the icon plaques, the belt, pouch and belt icon, the gloves, boots, the robe's
    # and cape's folds, blood and mud painted; the mask's sealed iron eyepieces, leather rim, valve and harness, the
    # canister's screw rings and grille, the hat's gilt Orthodox cross, the shield's crucifix, nailed severed hands and
    # iron corners painted on their solid shapes (gas_mask() is no longer used: the mask is the face piece and canister
    # alone). Modules: Helmet (hat, hood, mask, canister), Chest (hose), Robe, Back (cape), Weapon (flail), Weapon2
    # (shield and candles), Accent (the faction's shoulder mantle and the hat's top). Detail pass (2026-11-10): a third
    # candle, iron bands, a nailed prayer scroll and a chained padlock on the shield, a scourge and a key ring on the belt
    # (Hip) made solid; a studded baldric, forearm straps and rust on the mail, a great cross, seam and mended tear on the
    # cape, patches and a rope cincture on the robe, plank seams and soot on the shield, gilt bands on the hat painted.
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1000
    TEX_SIZES = {"Body": 256, "Robe": 256, "Back": 128, "Helmet": 128, "Weapon2": 128, "Hip": 64}
    from mathutils import noise as _nz
    import bmesh as _bm
    M.setdefault("hat", mat("hat", "141210", 0.05)); M.setdefault("cape", mat("cape", "3e3a34"))
    def pose(r):
        human.reach(r, "R", P(-0.3, -0.06, 1.3), P(-0.4, -0.18, 1.42))
        human.reach(r, "L", P(0.25, -0.1, 1.06), P(0.17, -0.3, 1.1))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "boots", "thigh": "boots", "torso": "mail"}, head=None,
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1919)
    human_ = bpy.data.objects["human"]
    toeless_feet()
    module("Robe")
    # the long robe, its hem ragged
    robe = cloth_cone(0, 0, 0.98, 0.1, 0.17, 0.24, "robe", sy=0.84, ragged=0.07, rnd=rnd, tris=200, thick=0.012, rows=10)
    hem = hem_line(robe)
    # the pilgrims' shoulder mantle in the seat colour
    cloth_cone(hp.x, hp.y - 0.01, 1.47, 1.35, 0.12, 0.255, "accent", sy=0.84, tris=40, thick=0.01)
    module("Back")
    # the cape: falling from the shoulders to the ground behind, open at the front
    cape = cloth_cone(0, 0.03, 1.42, 0.095, 0.12, 0.28, "cape", sy=0.82, keep=FRONT(80), ragged=0.08, rnd=rnd, tris=110, mono=False, thick=0.012,
                      extra=[robe], rows=10)
    chem = hem_line(cape, 0.0, 0.03)
    # the legs inside the closed robe are never seen: removed (owner: hidden parts go up front)
    bm = _bm.new(); bm.from_mesh(human_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if 0.22 < (c := f.calc_center_median()).z < 0.95 and abs(c.x) < 0.2 and abs(c.y) < 0.18], context='FACES')
    bm.to_mesh(human_.data); bm.free()
    module("Helmet")
    # the hood round the head, open at the front; the gas mask's face piece and canister; the tall hat (its cross painted)
    fb = FACE_BOX; crown = fb["mid"] + fb["up"] * 0.1 - fb["fwd"] * 0.05   # the top of the skull
    c = crown
    cloth_cone(c.x, c.y + 0.01, c.z - 0.005, c.z - 0.2, 0.1, 0.13, "cloth", sy=1.05, keep=FRONT(28), tris=40, thick=0.01,
               stand=(fb["mid"] - fb["fwd"] * 0.09, 0.08))   # centred on the (absent) head
    centre = fb["mid"] - fb["fwd"] * 0.06 - fb["up"] * 0.02
    # the face piece: a rubber shell cut to a face-shaped oval, its lower half drawn forward into the snout
    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=10, radius=1, location=tuple(centre))
    mk = bpy.context.object; mk.scale = (0.086, 0.092, 0.122); mk.rotation_euler = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
    bpy.context.view_layer.update(); mw = mk.matrix_world
    bm = _bm.new(); bm.from_mesh(mk.data)
    def outside(f):
        q = (mw @ f.calc_center_median()) - centre
        return q.dot(fb["fwd"]) < 0.02 or (q.dot(fb["ax"]) / 0.072) ** 2 + ((q.dot(fb["up"]) + 0.01) / 0.112) ** 2 > 1
    _bm.ops.delete(bm, geom=[f for f in bm.faces if outside(f)], context='FACES')
    bm.to_mesh(mk.data); bm.free()
    mk.data.transform(mk.matrix_world); mk.matrix_world = Matrix.Identity(4)
    for v in mk.data.vertices:   # the snout: the lower face drawn forward, most at the middle
        q = v.co - centre; u_, a_ = q.dot(fb["up"]), q.dot(fb["ax"])
        v.co += fb["fwd"] * 0.03 * min(max(0.0, (-0.01 - u_) / 0.09) * max(0.0, 1 - abs(a_) / 0.06), 1.0) ** 0.7
    mk.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; bpy.ops.object.select_all(action='DESELECT'); mk.select_set(True)
    bpy.context.view_layer.objects.active = mk; bpy.ops.object.shade_smooth(); add(mk, "rubber")
    def on_mask(a_, u_, out=0.0):
        k = 1 - (a_ / 0.086) ** 2 - (u_ / 0.122) ** 2
        return centre + fb["ax"] * a_ + fb["up"] * u_ + fb["fwd"] * (0.092 * max(k, 0.0) ** 0.5 + out)
    snout = on_mask(0.0, -0.06, 0.025); down = (fb["fwd"] * 0.7 - fb["up"] * 0.71).normalized()   # the mouthpiece points down
    bar(snout - down * 0.01, snout + down * 0.02, 0.03, "rubber", sides=10)                  # the snout's collar
    can0, can1 = snout + down * 0.02, snout + down * 0.084
    bar(can0, can1, 0.04, "iron", sides=12)                                                  # the filter canister
    ht = crown - Vector((0, 0, 0.03))   # the hat sits down over the crown
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.09, depth=0.25, location=tuple(ht + Vector((0, 0, 0.125))))
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "hat")
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.091, depth=0.012, location=tuple(ht + Vector((0, 0, 0.25))))
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "accent")
    module("Chest")
    # the respirator hose from the snout down to a canister on the chest (its ribs painted)
    hose = [snout - fb["up"] * 0.02, Vector((0.05, -0.2, 1.36)), Vector((0.07, -0.21, 1.12))]
    for a_, b_ in zip(hose, hose[1:]): bar(a_, b_, 0.014, "rubber", sides=5)
    bar(Vector((0.07, -0.21, 1.13)), Vector((0.07, -0.21, 1.04)), 0.03, "iron", sides=6)
    module("Weapon")
    # the flail: a short handle in the right fist, a chain of links falling from it, a spiked iron head
    fr, d = FISTS["R"]
    hand_end = fr + d * 0.1
    bar(fr - d * 0.05, hand_end, 0.014, "wood", sides=5)
    links = [hand_end + Vector((-0.025 * i, -0.019 * i, -0.05 * i)) for i in range(5)]
    for a_, b_ in zip(links, links[1:]): bar(a_, b_, 0.008, "iron", sides=4)
    head_ = links[-1] + Vector((-0.01, -0.01, -0.04))
    sphere(tuple(head_), 0.04, "iron", seg=6, rings=4)
    for v_ in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, -1))):
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.012, radius2=0.0, depth=0.035, location=tuple(head_ + v_ * 0.05))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(v_); add(o, "iron")
    module("Weapon2")
    # the reliquary shield: a tall wooden board held before the left side (crucifix and nailed severed hands painted on
    # its face), candles along its top edge
    fl, _ = FISTS["L"]
    sc = fl + Vector((0.02, -0.06, 0.0))
    box(tuple(sc), (0.27, 0.03, 0.56), "wood")
    for cx_ in (-0.07, 0.07):
        bar(Vector((sc.x + cx_, sc.y, sc.z + 0.28)), Vector((sc.x + cx_, sc.y, sc.z + 0.33)), 0.01, "bone", sides=4)
        sphere((sc.x + cx_, sc.y, sc.z + 0.345), 0.009, "glow", seg=4, rings=3)
    # detail pass (2026-11-10): a stub candle between them, two iron bands across the face, a chain hanging from the
    # foot with an iron padlock, a prayer scroll nailed over the top
    bar(Vector((sc.x, sc.y, sc.z + 0.28)), Vector((sc.x, sc.y, sc.z + 0.305)), 0.011, "bone", sides=4)
    sphere((sc.x, sc.y, sc.z + 0.317), 0.008, "glow", seg=4, rings=3)
    BANDS = (0.19, -0.2)
    for bz in BANDS: box((sc.x, sc.y - 0.017, sc.z + bz), (0.275, 0.006, 0.026), "iron")
    CH = [Vector((sc.x + 0.1, sc.y - 0.01, sc.z - 0.28)) + Vector((0.01 * math.sin(i), -0.004 * i, -0.028 * i)) for i in range(5)]
    for a_, b_ in zip(CH, CH[1:]): bar(a_, b_, 0.005, "iron", sides=3)
    LOCK = CH[-1] + Vector((0, 0, -0.022))
    box(tuple(LOCK), (0.03, 0.014, 0.03), "iron")
    SCROLL = Vector((sc.x - 0.05, sc.y - 0.02, sc.z + 0.24))
    box(tuple(SCROLL), (0.09, 0.004, 0.06), "parchment", rot=(0, math.radians(8), 0))
    module("Hip")
    # detail pass: a scourge hanging from the belt on the right hip, three knotted cords; a ring of keys on the left
    def on_robe(x_, z_, d_):
        o_ = Vector((x_, 0, z_)) - d_ * 0.9; ok_, at_, *_ = robe.ray_cast(o_, d_)
        return at_ if ok_ else o_ + d_ * 0.7
    SG = on_robe(0, 0.95, Vector((1, 0.2, 0)).normalized()) + Vector((-0.016, -0.005, 0))
    bar(SG, SG + Vector((-0.012, -0.008, -0.1)), 0.009, "wood", sides=4)
    SGE = SG + Vector((-0.012, -0.008, -0.1))
    for k in (-1, 0, 1):
        tip = SGE + Vector((-0.02 + 0.012 * k, -0.022 * k - 0.01, -0.17))
        bar(SGE, tip, 0.0035, "leather", sides=3); sphere(tuple(tip), 0.007, "iron", seg=4, rings=3)
    KEYS = on_robe(0, 0.93, Vector((-1, 0.25, 0)).normalized()) + Vector((0.014, -0.004, -0.02))
    bar(KEYS + Vector((0, 0.012, 0.02)), KEYS + Vector((0, -0.012, 0.02)), 0.004, "iron", sides=3)
    for k in (-1, 0, 1):
        bar(KEYS + Vector((0, 0.008 * k, 0.018)), KEYS + Vector((0.004, 0.014 * k, -0.045)), 0.0035, "iron", sides=3)
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **KIT, **dict(robe="3a3026", robed="201a13", robel="52463a", cape="34322c", caped="1c1b18", capel="4a473e",
                    cloth="38352f", clothd="221f1b", clothl="4e4a42", rubber="1c1b19", rubberl="312f2b", mail="36383c", maill="565860",
                    icon="4a1a12", hat="1a1815", hatl="2e2b26", flesh="9c7a6a", fleshd="6a4e44")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def mail(p, n, ao):
        return C["ink"] if (p.z * 240) % 1.0 < 0.3 and ((p.x + p.y) * 120 + (p.z * 240 // 1) * 0.5) % 1.0 < 0.4 else lit(C["maill"] if n.dot(PSX_LIGHT) > 0.4 else C["mail"], n, ao, metal=True, p=p)
    def icon(u, v, w, h, n, ao, p):   # a gilt icon plaque, u, v from its centre: a frame, a dark field, a haloed figure with no face
        if abs(u) > w / 2 or abs(v) > h / 2: return None
        if abs(u) > w / 2 - 0.004 or abs(v) > h / 2 - 0.004: return C["ink"]
        if abs(u) > w / 2 - 0.012 or abs(v) > h / 2 - 0.012: return lit(C["giltl"] if v > 0 else C["gilt"], n, ao, metal=True, p=p)
        hr = math.hypot(u, v - h * 0.18)
        if hr < w * 0.16: return C["mannequind"]
        if hr < w * 0.24: return C["giltl"]
        if abs(u) < w * 0.14 * (1 + (h * 0.1 - v) / h) and v < h * 0.1: return C["ink"] if abs(u) > w * 0.14 * (1 + (h * 0.1 - v) / h) - 0.003 else C["robed"]   # the robed figure
        return C["icon"]
    def belt(x, y, z, n, ao, p):
        if z < 0.965 or z > 1.02: return C["ink"]
        if y < 0 and abs(x) < 0.022: return C["ink"] if abs(x) > 0.017 or abs(z - 0.992) > 0.022 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.4 else C["brass"])
        return lit(C["leatherl"] if abs(z - 0.992) < 0.004 else C["leather"], n, ao, p=p)
    ICONS = ((-0.08, 1.24, 0.07, 0.09), (0.08, 1.22, 0.07, 0.1), (-0.02, 1.12, 0.06, 0.08))
    mc = centre
    def paint(m, p, n, ao, tx, ty):
        base = m.split(".")[0]; x, y, z = p.x, p.y, p.z
        if base == "robe":   # the belt, a pouch and an icon plaque on it, the robe's folds, blood and ragged hem
            if z > 0.958: return belt(x, y, z, n, ao, p)
            if y < 0:
                r_ = icon(x, z - 0.88, 0.1, 0.16, n, ao, p)
                if r_ is not None: return r_
                if abs(x + 0.12) < 0.033 and 0.9 < z < 0.965:
                    if abs(x + 0.12) > 0.028 or z < 0.905: return C["ink"]
                    if abs(z - 0.94) < 0.003: return C["ink"]
                    return lit(C["leatherl"] if z > 0.94 else C["leather"], n, ao, p=p)
            # detail pass (2026-11-10): a knotted rope cincture down the right front, two patches sewn on, a scorched
            # and tattered band low on the robe
            a_ = math.degrees(math.atan2(x, -y))
            if y < 0 and 0.6 < z:
                cxr = -0.16 - (0.958 - z) * 0.04
                if abs(x - cxr) < 0.012:
                    if abs(x - cxr) > 0.008: return C["ink"]
                    if any(abs(z - kz) < 0.012 for kz in (0.87, 0.77)): return lit(C["rope"], n, ao, p=p) if abs(x - cxr) < 0.006 else C["roped"]
                    if z < 0.64: return C["roped"] if (x * 300) % 1.0 < 0.4 else lit(C["rope"], n, ao, p=p)
                    return C["roped"] if ((z - x) * 90) % 1.0 < 0.3 else lit(C["rope"], n, ao, p=p)
            for pa, pz, pw, ph in ((-62, 0.5, 8.0, 0.065), (70, 0.36, 7.0, 0.06)):
                du, dv = (a_ - pa) / pw, (z - pz) / ph
                if abs(du) < 1 and abs(dv) < 1:
                    if abs(du) > 0.86 or abs(dv) > 0.84: return C["ink"]
                    if (abs(du) > 0.7 or abs(dv) > 0.68) and ((du + dv) * 6) % 1.0 < 0.5: return C["roped"]
                    return lit(C["robel"].lerp(C["mud"], 0.3), n, ao, p=p)
            return psx_robe(C, "robe", p, n, ao, hem, blood=0.3)
        if base == "cape":   # detail pass: a great faded cross embroidered on the back, a seam down it, a sewn tear
            if y > 0.05:
                if abs(x) < 0.004 and z < 1.38 and not (0.92 < z < 1.1): return C["capel"] if (z * 90) % 1.0 < 0.5 else C["caped"]
                on_ = lambda g: (abs(x) < 0.022 + g and 0.72 < z < 1.3 + g) or (abs(x) < 0.12 + g and abs(z - 1.12) < 0.02 + g) or (abs(x) < 0.07 + g and abs(z - 1.24) < 0.016 + g)
                if on_(0.0): return lit(C["bone"].lerp(C["cape"], 0.55), n, ao, p=p)
                if on_(0.006): return C["ink"]
                tz = 0.55 + (x - 0.1) * 0.6
                if 0.06 < x < 0.17 and abs(z - tz) < 0.006: return C["ink"] if abs(z - tz) < 0.0025 else (C["roped"] if (x * 180) % 1.0 < 0.5 else C["caped"])
            return psx_robe(C, "cape", p, n, ao, chem, blood=0.15, step=20.0, cy=0.03)
        if base == "boots":
            if z < 0.02: return lit(C["sole"], n, ao, p=p)
            for sz in (0.07, 0.15):
                if abs(z - sz) < 0.012:
                    if abs(z - sz) > 0.008: return C["ink"]
                    if n.x * (1 if x > 0 else -1) > 0.5 and abs(y - (-0.01 if sz < 0.1 else 0.0)) < 0.014: return C["brassl"] if z > sz else C["brass"]   # the buckles (detail pass)
                    return lit(C["leather"], n, ao, p=p)
            if z < 0.06 and _nz.noise(p * 45) > 0.0: return lit(C["mud"], n, ao, p=p)
            return lit(C["bootl"] if n.z > 0.6 else C["boot"], n, ao, grime=0.3, p=p)
        if base == "leather" and FISTS and min((p - FISTS[s][0]).length for s in FISTS) < 0.08:   # gloves, a knuckle seam
            s = "L" if (p - FISTS["L"][0]).length < (p - FISTS["R"][0]).length else "R"; fc, fd = FISTS[s]
            along = (p - fc).dot(fd)
            if along < -0.03: return lit(C["leather"], n, ao, p=p)
            if abs(along - 0.02) < 0.005 and ((p - fc).length * 200) % 1.0 < 0.4: return C["metall"]   # iron studs over the knuckles (detail pass)
            return C["ink"] if abs(along - 0.012) < 0.004 else lit(C["glovel"] if n.dot(PSX_LIGHT) > 0.7 else C["glove"], n, ao, p=p)
        if base == "rubber":
            q = p - mc; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
            if (p - snout).length < 0.045 or min(segd(p, hose[0], hose[1])[0], segd(p, hose[1], hose[2])[0]) > 0.02:   # the face piece and the snout, not the hose
                for ex in (-0.034, 0.034):   # eyepieces sealed with plain iron caps: a rim, a flat cap, no glass, no rivets
                    dd = math.hypot(a - ex, u - 0.035)
                    if dd < 0.025:
                        if dd > 0.021: return C["ink"]
                        if dd > 0.016: return lit(C["metall"] if u > 0.035 else C["metal"], n, ao, metal=True, p=p)
                        return lit(C["iron"] if n.dot(PSX_LIGHT) < 0.5 else C["ironl"], n, ao, metal=True, p=p)
                e = (a / 0.072) ** 2 + ((u + 0.01) / 0.112) ** 2
                if e > 0.8: return C["ink"] if e > 0.92 else lit(C["leather"], n, ao, p=p)   # the leather sealing rim
                if math.hypot(a, u + 0.1) < 0.018: return C["ink"] if math.hypot(a, u + 0.1) > 0.014 else lit(C["metall"], n, ao, metal=True, p=p)   # the valve
                return lit(C["rubberl"] if n.dot(PSX_LIGHT) > 0.6 else C["rubber"], n, ao, p=p)
            return C["ink"] if (z * 140) % 1.0 < 0.3 else lit(C["rubberl"] if n.dot(PSX_LIGHT) > 0.5 else C["rubber"], n, ao, p=p)   # the ribbed hose
        if base == "iron" and (p - can1).length < 0.07:   # the canister: screw rings, a crossed grille on its face
            t = (p - can0).dot(down); e = p - (can0 + down * t)
            if t > 0.06:
                if abs(e.dot(fb["ax"])) < 0.005 or abs(e.dot(down.cross(fb["ax"]))) < 0.005: return C["ink"]
                return C["ink"] if (e.length * 220) % 1.0 < 0.3 else lit(C["metal"], n, ao, metal=True, p=p)
            for t_ in (0.016, 0.036):
                if abs(t - t_) < 0.006: return C["ink"] if abs(t - t_) > 0.004 else lit(C["metall"], n, ao, metal=True, p=p)
            return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.5 else C["iron"], n, ao, metal=True, p=p)
        if base == "cloth":   # the hood: folds, the mask's harness round the back of the head, buckles at the temples
            q = p - mc; a, u, f = q.dot(fb["ax"]), q.dot(fb["up"]), q.dot(fb["fwd"])
            for su in (0.035, -0.06):
                if abs(u - su) < 0.011 and f < 0.03:
                    if abs(abs(a) - 0.09) < 0.012 and f > -0.03 and su > 0: return C["brassl"] if abs(u - su) < 0.007 else C["ink"]
                    return C["ink"] if abs(u - su) > 0.007 else lit(C["leather"], n, ao, p=p)
            g = math.sin(math.atan2(a, -f) * 9 + _nz.noise(p * 12) * 2)
            return lit(C["clothd"] if g > 0.75 else C["cloth"], n, ao, p=p)
        if base == "hat":   # the tall hat: an Orthodox cross in gilt on its front, a light rim
            q = p - ht
            if q.z < 0.012: return C["ink"]
            if n.y < -0.2:
                def on(gw):   # upright, top bar, long bar, the slanted foot (gw: grown by the outline)
                    sl = abs((q.z - 0.075) * math.cos(0.4) - q.x * math.sin(0.4))
                    return ((abs(q.x) < 0.009 + gw and abs(q.z - 0.13) < 0.1 + gw) or (abs(q.x) < 0.025 + gw and abs(q.z - 0.198) < 0.007 + gw)
                            or (abs(q.x) < 0.045 + gw and abs(q.z - 0.155) < 0.0085 + gw) or (abs(q.x) < 0.032 + gw and sl < 0.007 + gw))
                if on(0.0): return C["giltl"] if q.x < 0.0 or q.z > 0.15 else C["gilt"]
                if on(0.004): return C["ink"]
            if 0.02 < q.z < 0.036 or 0.232 < q.z < 0.242: return C["gilt"] if n.dot(PSX_LIGHT) > 0.2 else C["giltd"]   # gilt bands at foot and top (detail pass)
            if abs(math.atan2(q.x, q.y) - 0.5) < 0.012 and q.z > 0.04: return C["hatl"]               # the felt's seam
            return lit(C["hatl"] if n.dot(PSX_LIGHT) > 0.5 else C["hat"], n, ao, p=p)
        if base == "wood" and (p - sc).length < 0.4 and abs(y - sc.y) < 0.03:   # the shield: crucifix, nailed hands, iron corners, grain
            u, v = x - sc.x, z - sc.z
            if abs(u) > 0.115 and abs(v) > 0.23: return C["ink"] if abs(u) > 0.13 or abs(v) > 0.274 else lit(C["iron"], n, ao, metal=True, p=p)
            if n.y < -0.5:
                for hx, hz in ((-0.09, 0.14), (0.09, 0.1), (-0.08, -0.1), (0.08, -0.16)):   # severed hands nailed on, bleeding
                    e = ((u - hx) / 0.034) ** 2 + ((v - hz) / 0.05) ** 2
                    if e < 1.0:
                        if math.hypot(u - hx, v - hz - 0.02) < 0.006: return C["ironl"]
                        if e > 0.75: return C["ink"]
                        return C["blood"] if v < hz - 0.025 else lit(C["flesh"] if n.dot(PSX_LIGHT) > 0.3 else C["fleshd"], n, ao, p=p)
                    if abs(u - hx) < 0.006 and hz - 0.09 < v < hz - 0.04 and _nz.noise(p * 60) > -0.3: return C["blood"]   # a drip
                if (abs(u) < 0.011 and abs(v - 0.02) < 0.18) or (abs(u) < 0.085 and abs(v - 0.1) < 0.011):   # the bone crucifix
                    return C["bonel"] if u < 0 else C["bone"]
                if (abs(u) < 0.015 and abs(v - 0.02) < 0.184) or (abs(u) < 0.089 and abs(v - 0.1) < 0.015): return C["ink"]
                # detail pass (2026-11-10): plank seams, soot over the candles, wax run down the top edge, gouges
                if v > 0.22 and any(abs(u - cx_) < 0.016 - (0.27 - v) * 0.2 for cx_ in (-0.07, 0.0, 0.07)): return C["woodd"].lerp(C["ink"], 0.5)
                if v > 0.23 and any(abs(u - cx_ - 0.012) < 0.004 for cx_ in (-0.07, 0.07)): return C["bonel"]
                if any(abs(u - k_) < 0.0025 for k_ in (-0.068, 0.0, 0.068)) and not (abs(u) < 0.016 and abs(v - 0.02) < 0.19): return C["ink"]
                if abs(v + 0.02 - u * 0.4) < 0.002 and 0.02 < u < 0.11: return C["woodl"]
            return psx_kit(C, "wood", p, n, ao)
        if base == "glow": return C["glow"]
        s_ = n.dot(PSX_LIGHT)
        # detail pass (2026-11-10): the shield's new iron bands, scroll, chain and padlock; the scourge and keys at the belt
        if base == "iron" and abs(y - (sc.y - 0.017)) < 0.006 and abs(x - sc.x) < 0.15 and any(abs(z - sc.z - bz) < 0.016 for bz in BANDS):
            bz = min(BANDS, key=lambda b_: abs(z - sc.z - b_)); v_ = z - sc.z - bz
            if abs(v_) > 0.01: return C["ink"]
            if any(abs(x - sc.x - nx) < 0.005 and abs(v_) < 0.005 for nx in (-0.11, -0.04, 0.04, 0.11)): return C["ironl"] if v_ > 0 else C["ink"]   # nail heads
            if _nz.noise(p * 40) > 0.4: return lit(C["rust"], n, ao, p=p)
            return lit(C["ironl"] if v_ > 0.005 else C["iron"], n, ao, metal=True, p=p)
        if base == "iron" and (p - LOCK).length < 0.03:   # the padlock: a keyhole, a bright worn edge
            q = p - LOCK
            if n.y < -0.5 and math.hypot(q.x, q.z + 0.002) < 0.004: return C["ink"]
            if q.z > 0.012: return C["ironl"]
            return lit(C["iron"], n, ao, metal=True, p=p)
        if base == "parchment":   # the scroll nailed on the shield: lines of prayer, a torn corner, a nail
            q = p - SCROLL
            if math.hypot(q.x, q.z - 0.02) < 0.004: return C["ironl"]
            if abs(q.x) > 0.042 or abs(q.z) > 0.027: return C["ink"]
            if q.x > 0.03 and q.z < -0.015: return C["parchmentd"]
            if abs(q.x) < 0.034 and q.z < 0.01 and (q.z * 160) % 1.0 < 0.35 and _nz.noise(p * 140) > -0.25: return C["ink"]
            return lit(C["parchment"], n, ao, p=p)
        if base == "wood" and (p - SGE).length < 0.12: return C["ink"] if ((p - SG).length * 120) % 1.0 < 0.25 else lit(C["leather"], n, ao, p=p)   # the scourge's bound grip
        if base == "leather" and (p - SGE).length < 0.2 and z < SGE.z: return C["roped"] if (z * 120) % 1.0 < 0.3 else lit(C["leatherl"], n, ao, p=p)
        if base == "iron" and ((p - SGE).length < 0.22 or (p - KEYS).length < 0.07): return lit(C["ironl"] if s_ > 0.2 else C["irond"], n, ao, metal=True, p=p)
        if base != "mail": return psx_kit(C, base, p, n, ao) or lit(C["metal"], n, ao, p=p)
        # ---- the body: mail, gilt icon plaques on the chest, the belt
        if z < 1.02: return belt(x, y, z, n, ao, p)
        if y < 0:
            for ix, iz, w, h in ICONS:
                r_ = icon(x - ix, z - iz, w, h, n, ao, p)
                if r_ is not None: return r_
        side_, along, ad = psx_arm(p)
        if ad < 0.08 and along > 1.85: return lit(C["leather"], n, ao, p=p)   # the cuff above the glove
        on_arm = ad < 0.075 and (abs(x) > 0.16 or y < -0.16 or z > 1.41)
        # detail pass (2026-11-10): a studded leather baldric from the left shoulder to the right hip, front and back; a
        # leather trim at the mail's edges (shoulder seam, sleeve hem); rust in the links low on the back
        bd = abs((x - 0.13) - (z - 1.42) * 0.62) * 0.85
        if bd < 0.02 and z < 1.47 and not on_arm:
            if bd > 0.016: return C["ink"]
            if abs(((z - 1.0) * 30) % 1.0 - 0.5) < 0.12 and bd < 0.007: return C["brassl"] if s_ > 0.2 else C["brass"]
            return lit(C["leatherl"] if bd < 0.005 and s_ > 0.4 else C["leather"], n, ao, grime=0.2, p=p)
        if on_arm and abs(along - 1.6) < 0.06: return C["ink"] if abs(along - 1.6) > 0.04 else lit(C["leather"], n, ao, p=p)   # a strap round the forearm
        if y > 0 and z < 1.2 and _nz.noise(p * 24) > 0.35: return lit(C["rust"], n, ao, p=p) if (p.z * 240) % 1.0 > 0.3 else C["ink"]
        return mail(p, n, ao)
    PAINT = paint

def reliquary_walker():
    # Reliquary Walker (2026-11-03, sixth figure of docs/art-pipeline.md, class E, 60 mm): an original figure on the
    # owner's reference cues, not a copy of the miniature, the art or any paint job. A squat armoured shrine on stumpy
    # legs with no head: a gothic tabernacle and a tall spiked spire rise from between the shoulders. Huge rounded
    # pauldrons, two exhaust stacks, a winged emblem on the chest above a dark slit, a mail skirt with a hanging bell and a
    # plucked dove, two arched grave-slab plaques over the thighs; a boiler and chain curtains on the back. Right hand: a
    # long bonebreaker mace, its flanged head at the ground. Left forearm: a spiked Catherine wheel with a martyr bound to
    # it (a blank mannequin head, no face). Modules: Helmet (tabernacle and spire), Chest (breastplate and faceplate),
    # Hip (skirt, plaques, bell, dove),
    # Back (boiler, stacks, chains), Weapon (mace), Weapon2 (wheel and martyr), Accent (pauldron tops, spire band).
    # The owner raised its budget (2026-11-03) to 6,000 triangles, then 8,000: rounder armour, gables, a chain fringe, a finer
    # wheel, and a MakeHuman martyr on it.
    # Painted PSX look (2026-11-10, as the Combat Engineer): the hull's big plates stay solid; their panel seams, rivet
    # rows, worn edges, rust streaks, grime and blood are painted on them, and so are the greaves' and vambraces' iron
    # bands, the pauldrons' iron rims, the winged emblem and the dark slit, the tabernacle's masonry, arched windows and
    # rose slits, the spire's bands and ribs, the plaques' crosses, the chain strands on the mail skirt, the parchment
    # tags, the boiler's hatch, the stacks' soot, the mace's grip and bands, and the martyr's wounds and blood.
    # Detail pass (2026-11-10): bolted iron crosses on the pauldrons, votive candles on the breastplate, hanging lanterns,
    # knee cops, a pressure gauge and valve wheel over the boiler made solid; dents, gouges, a band of crosses on each
    # pauldron front, soot, oil runs, mud, a scratched tally and the chain curtains as strands of links painted.
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 2800
    TEX_SIZES = {"Body": 512, "Helmet": 256, "Chest": 256, "Hip": 256, "Back": 256, "Weapon": 128, "Weapon2": 256}   # the martyr needs the wheel's 256
    # (detail pass 2026-11-10: the body 512, as it is drawn twice a man's height, so its texels match the men's on screen)
    from mathutils import noise as _nz
    BOXES = []   # the hull's and the stone's boxes, for painting each face in its own frame: (centre, inverse rotation, half size)
    def rbox(loc, size, m, bev=0.03, rot=(0, 0, 0)):
        o = box(loc, size, m, rot=rot)
        if bev:
            b_ = o.modifiers.new("bev", "BEVEL"); b_.width = bev; b_.segments = 3 if bev >= 0.1 else (2 if bev >= 0.04 else 1); b_.limit_method = 'NONE'
        if m in ("hull", "stone"): BOXES.append((Vector(loc), Euler(rot).to_matrix().inverted(), Vector(size) / 2))
        return o
    def cone(at, r1, r2, depth, m, n=8, axis=Vector((0, 0, 1))):
        bpy.ops.mesh.primitive_cone_add(vertices=n, radius1=r1, radius2=r2, depth=depth, location=tuple(at))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis); return add(o, m)
    def cyl(at, r, depth, m, n=8, axis=Vector((0, 0, 1))):
        bpy.ops.mesh.primitive_cylinder_add(vertices=n, radius=r, depth=depth, location=tuple(at))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis); return add(o, m)
    def sheet(x0, x1, y, z0, z1, m, sag=0.0, cols=6, rows=3, thick=0.02):
        # a hanging sheet (mail, chains) from z1 down to z0, between x0 and x1 at depth y; its bottom edge ragged
        verts, faces = [], []
        for j in range(rows + 1):
            for i in range(cols + 1):
                t = i / cols; z = z1 - (z1 - z0) * j / rows
                if j == rows: z += (0.04 if i % 2 else 0.0)
                verts.append((x0 + (x1 - x0) * t, y + sag * math.sin(math.pi * t) * j / rows, z))
        for j in range(rows):
            for i in range(cols):
                a_ = j * (cols + 1) + i; faces.append((a_, a_ + 1, a_ + cols + 2, a_ + cols + 1))
        me = bpy.data.meshes.new("sh"); me.from_pydata(verts, [], faces)
        o = bpy.data.objects.new("sh", me); scn.collection.objects.link(o)
        o.modifiers.new("sol", "SOLIDIFY").thickness = thick; return add(o, m)
    # --- Body: legs, hips, the armoured hull, pauldrons and arms (bands, ribs, rims and the slit painted)
    for s in (-1, 1):
        x = 0.24 * s
        rbox((x, -0.05, 0.07), (0.26, 0.4, 0.14), "hull", 0.03)                          # the foot, a broad iron sabaton
        rbox((x, -0.2, 0.05), (0.22, 0.12, 0.1), "hull", 0.02)                            # its toe cap
        rbox((x, -0.04, 0.27), (0.26, 0.26, 0.28), "hull", 0.04)                          # the greave
        rbox((x, 0.0, 0.52), (0.26, 0.3, 0.26), "hull", 0.04)                             # the thigh
        rbox((x, -0.18, 0.41), (0.2, 0.06, 0.13), "hull", 0.025)                           # its knee cop (detail pass 2026-11-10)
    rbox((0, 0.0, 0.72), (0.6, 0.44, 0.2), "hull", 0.05)                                   # the hips
    rbox((0, 0.0, 0.92), (0.66, 0.52, 0.24), "hull", 0.06)                                 # the belly
    rbox((0, 0.0, 1.16), (0.78, 0.62, 0.36), "hull", 0.12)                                 # the hull, its edges rounded
    rbox((0, 0.03, 1.35), (0.5, 0.48, 0.1), "hull", 0.04)                                  # the collar under the tabernacle
    module("Chest")   # the breastplate in its own node, so its emblem has the texels
    rbox((0, -0.29, 1.2), (0.56, 0.12, 0.28), "hull", 0.05, rot=(math.radians(-8), 0, 0))  # the breastplate, sloped back
    rbox((0, -0.29, 0.95), (0.46, 0.1, 0.16), "hull", 0.04)                               # the lower faceplate
    module("Body")
    for s in (-1, 1):
        x = 0.5 * s
        rbox((x, 0.0, 1.22), (0.42, 0.6, 0.44), "hull", 0.14)                             # the pauldron
        for k, z in enumerate((0.96, 0.85)):
            rbox((x * (1.02 + 0.03 * k), 0.0, z), (0.38 - 0.03 * k, 0.56 - 0.04 * k, 0.12), "hull", 0.035)   # its lames
        rbox((x, 0.0, 1.445), (0.3, 0.5, 0.025), "accent", 0.0)                           # the seat colour on its top
    # the right arm hangs with the mace; the left holds the wheel out before it
    cyl((-0.58, -0.02, 0.78), 0.1, 0.2, "metal", n=8)                                     # elbow piston
    rbox((-0.58, -0.06, 0.6), (0.22, 0.24, 0.24), "hull", 0.05)                           # right vambrace
    rbox((-0.58, -0.1, 0.43), (0.2, 0.22, 0.14), "hull", 0.04)                            # right fist
    cyl((0.58, -0.02, 0.78), 0.1, 0.2, "metal", n=8)
    rbox((0.62, -0.2, 0.68), (0.22, 0.32, 0.22), "hull", 0.05)                            # left vambrace, forward
    # detail pass (2026-11-10): an iron cross bolted on each pauldron's outer face, votive candles on the hull's
    # shoulders either side of the tabernacle, a lantern hung on a chain from each pauldron's front
    for s in (-1, 1):
        box((0.718 * s, 0.0, 1.21), (0.03, 0.06, 0.26), "iron"); box((0.718 * s, 0.0, 1.25), (0.03, 0.22, 0.055), "iron")
    CANDLES = []
    for s in (-1, 1):
        for dx, dy, hgt in ((0.0, 0.0, 0.09), (0.035, 0.03, 0.06), (-0.03, 0.035, 0.045)):
            c_ = Vector((0.185 * s + dx * s, -0.275 + dy * 0.4, 1.335))   # on the breastplate's top edge, before the collar
            cyl(c_ + Vector((0, 0, hgt / 2)), 0.014, hgt, "bone", n=5); sphere(tuple(c_ + Vector((0, 0, hgt + 0.01))), 0.009, "glow", seg=4, rings=3)
            CANDLES.append((c_, hgt))
    LANTERNS = []
    for s in (-1, 1):
        hk = Vector((0.36 * s, -0.31, 1.12)); lt_ = Vector((0.36 * s, -0.36, 0.99))
        bar(hk, hk + Vector((0, -0.05, 0)), 0.008, "iron", sides=4)                          # the hook
        bar(hk + Vector((0, -0.05, 0)), lt_ + Vector((0, 0, 0.055)), 0.004, "iron", sides=3)   # its chain
        box(tuple(lt_), (0.06, 0.06, 0.08), "iron"); cone(lt_ + Vector((0, 0, 0.055)), 0.045, 0.0, 0.035, "iron", n=4)
        LANTERNS.append(lt_)
    # --- Helmet: the tabernacle and its spire, where a head would be (windows, rose slits and bands painted)
    module("Helmet")
    rbox((0, 0.02, 1.49), (0.34, 0.34, 0.26), "stone", 0.02)
    for s in (-1, 1):
        for t in (-1, 1):
            cyl((0.17 * s, 0.02 + 0.17 * t, 1.52), 0.03, 0.34, "stone", n=4)               # corner piers
            cone((0.17 * s, 0.02 + 0.17 * t, 1.75), 0.035, 0.0, 0.14, "stone", n=4)      # pinnacles
    rbox((0, 0.02, 1.635), (0.38, 0.38, 0.05), "stone", 0.015)                               # the cornice
    gables = []
    for k in range(4):                                                                    # a gable on each side
        a_ = k * math.pi / 2; n_ = Vector((math.sin(a_), -math.cos(a_), 0)); t_ = Vector((math.cos(a_), math.sin(a_), 0))
        c_ = Vector((0, 0.02, 1.66)) + n_ * 0.185
        verts = [c_ - t_ * 0.17, c_ + t_ * 0.17, c_ + Vector((0, 0, 0.17))]
        verts += [v - n_ * 0.03 for v in verts]
        me = bpy.data.meshes.new("gable"); me.from_pydata([tuple(v) for v in verts], [], [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)])
        me.validate(); me.update(); g = bpy.data.objects.new("gable", me); scn.collection.objects.link(g); add(g, "stone")
        cone(c_ + Vector((0, 0, 0.2)), 0.02, 0.0, 0.08, "stone", n=4)                       # its finial
        gables.append((c_, n_, t_))
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.18, depth=0.03, location=(0, 0.02, 1.67))
    o = bpy.context.object; o.rotation_euler = (0, 0, math.pi / 8); add(o, "accent")       # the seat colour round the spire's foot
    spire = cone((0, 0.02, 2.04), 0.145, 0.0, 0.72, "rust", n=12)
    spire.rotation_quaternion = spire.rotation_quaternion @ Euler((0, 0, math.pi / 8)).to_quaternion()
    for i in range(8):                                                                    # spikes up its ribs
        ang = i * math.pi / 4
        out = Vector((math.cos(ang), math.sin(ang), 0.35)).normalized()
        for k, z in enumerate((1.78, 1.94, 2.1, 2.24)):
            if (i + k) % 2: continue
            r = 0.145 * (1 - (z - 1.68) / 0.72)
            cone(Vector((r * math.cos(ang), 0.02 + r * math.sin(ang), z)) + out * 0.02, 0.012, 0.0, 0.06, "iron", n=4, axis=out)
    # --- Hip: the mail skirt, plaques, bell and the plucked dove (the chain strands painted)
    module("Hip")
    em = Vector((0, -0.375, 1.2))
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.38, radius2=0.33, depth=0.56, end_fill_type='NOTHING', location=(0, 0.0, 0.5))
    sk = bpy.context.object; sk.scale = (1, 0.75, 1)
    for v in sk.data.vertices:
        if v.co.z < 0 and (v.index // 1) % 2: v.co.z += 0.05
    sk.modifiers.new("sol", "SOLIDIFY").thickness = 0.015; add(sk, "mail")
    for s in (-1, 1):                                                                     # arched grave-slab plaques
        x = 0.21 * s
        rbox((x, -0.33, 0.47), (0.24, 0.045, 0.36), "stone", 0.012)
        cyl((x, -0.33, 0.65), 0.12, 0.045, "stone", n=10, axis=Vector((0, 1, 0)))
        bar(Vector((x, -0.33, 0.86)), Vector((x, -0.33, 0.76)), 0.008, "iron", sides=4)                # hung from the hull
    # the plucked dove, hung by its feet from two chains under the slit; a bell below it
    bar(Vector((-0.05, -0.36, 0.95)), Vector((-0.02, -0.4, 0.8)), 0.007, "iron", sides=4)
    bar(Vector((0.05, -0.36, 0.95)), Vector((0.02, -0.4, 0.8)), 0.007, "iron", sides=4)
    sphere((0, -0.41, 0.72), 1, "pale", (0.06, 0.05, 0.1), seg=8, rings=6)
    for s in (-1, 1):
        o = box((0.07 * s, -0.41, 0.7), (0.1, 0.012, 0.06), "pale", rot=(0, 0.6 * s, 0))  # limp wings
    bar(Vector((0, -0.38, 0.78)), Vector((0, -0.4, 0.58)), 0.006, "iron", sides=4)
    cone((0, -0.4, 0.53), 0.05, 0.02, 0.08, "brass", n=8)
    # --- Back: the boiler (its hatch painted), two exhaust stacks, chain curtains (parchment tags painted)
    module("Back")
    cyl((0, 0.36, 1.05), 0.22, 0.14, "iron", n=12, axis=Vector((0, 1, 0)))
    for s in (-1, 1):
        x = 0.3 * s
        cyl((x, 0.2, 1.5), 0.06, 0.36, "iron", n=8)
        cyl((x, 0.2, 1.69), 0.078, 0.04, "metal", n=8)                                    # the stack's lip
        bar(Vector((x, 0.32, 1.2)), Vector((0.08 * s, 0.4, 0.92)), 0.03, "metal", sides=6)   # pipes into the boiler
        sheet(0.3 * s, 0.68 * s, 0.31, 0.3, 1.02, "mail", sag=0.05, cols=5, rows=3)       # chain curtains under the pauldrons
    # detail pass (2026-11-10): a pressure gauge on a pipe over the boiler, a valve wheel on its other side
    GAUGE = Vector((0.15, 0.40, 1.33))
    bar(Vector((0.1, 0.37, 1.24)), GAUGE - Vector((0, 0.01, 0.03)), 0.014, "metal", sides=5)
    cyl(GAUGE, 0.045, 0.03, "brass", n=10, axis=Vector((0, 1, 0)))
    VALVE = Vector((-0.15, 0.42, 1.3))
    bar(Vector((-0.1, 0.37, 1.24)), VALVE, 0.012, "metal", sides=5)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.04, minor_radius=0.007, major_segments=8, minor_segments=3, location=tuple(VALVE + Vector((0, 0.01, 0))))
    o = bpy.context.object; o.rotation_euler = (math.pi / 2, 0, 0); add(o, "iron")
    # --- Weapon: the bonebreaker mace, its haft through the right fist, its flanged head at the ground
    module("Weapon")
    fist = Vector((-0.58, -0.12, 0.43)); tip = Vector((-0.8, -0.6, 0.07)); d = (tip - fist).normalized()
    bar(fist - d * 0.22, tip, 0.026, "wood", sides=6)
    sphere(tuple(fist - d * 0.24), 0.04, "iron", seg=6, rings=4)                          # the pommel
    head_ = tip + d * 0.07
    sphere(tuple(head_), 0.075, "gore", seg=8, rings=6)
    side = d.cross(Vector((0, 0, 1))).normalized(); up_ = side.cross(d).normalized()
    for k in range(6):                                                                    # the flanges
        ang = k * math.pi / 3; v_ = side * math.cos(ang) + up_ * math.sin(ang)
        o = cone(head_ + v_ * 0.085, 0.055, 0.0, 0.12, "gore", n=4, axis=v_)
        cone(head_ + v_ * 0.06 + d * 0.06, 0.03, 0.0, 0.07, "gore", n=4, axis=(v_ + d).normalized())   # a second row
    cone(head_ + d * 0.1, 0.03, 0.0, 0.09, "gore", n=4, axis=d)
    # --- Weapon2: the Catherine wheel on the left forearm, a martyr bound across its outer face
    module("Weapon2")
    wc = Vector((0.8, -0.36, 0.62)); R = 0.43
    ax_ = Vector((math.cos(math.radians(-45)), math.sin(math.radians(-45)), 0)); hz = ax_.cross(Vector((0, 0, 1)))
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=0.05, major_segments=16, minor_segments=6, location=tuple(wc))
    o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(ax_)
    o.scale = (1, 1, 1.6); add(o, "wood")
    def onwheel(a_, r_, out=0.0):   # a point on the wheel at angle a_ (0 = towards the front), radius r_, out along its axle
        return wc + ax_ * out + hz * math.cos(a_) * r_ + Vector((0, 0, math.sin(a_) * r_))
    bar(Vector((0.62, -0.28, 0.68)), wc - ax_ * 0.05, 0.05, "metal", sides=6)            # the mount from the vambrace
    cyl(wc + ax_ * 0.0, 0.07, 0.16, "iron", n=8, axis=ax_)                                # the hub
    for k in range(6):
        a_ = k * math.pi / 3 + math.pi / 6
        bar(onwheel(a_, 0.06), onwheel(a_, R - 0.03), 0.022, "wood", sides=4)             # spokes
    for k in range(8):
        a_ = k * math.pi / 4
        bar(onwheel(a_, R, -0.065), onwheel(a_, R, 0.065), 0.055, "iron", sides=4)        # iron band clamps
        v_ = (onwheel(a_, 1) - wc).normalized()
        if k % 2: cone(onwheel(a_, R + 0.1), 0.035, 0.0, 0.14, "iron", n=4, axis=v_)       # spikes outward
    # The martyr (owner, 2026-11-03: "make the body on the wheel look better"): a gaunt MakeHuman body posed spread-eagled,
    # with the plain mannequin head (no face) and bound fists, built standing in its own space, then scaled onto the
    # wheel's outer face. A loincloth; ropes at the wrists, ankles and chest, and from each wrist and ankle to the rim.
    n0 = len(MODULES["Weapon2"])
    hand_ = {"L": P(0.6, 0.0, 1.72), "R": P(-0.6, 0.0, 1.72)}; elbow_ = {"L": P(0.42, 0.0, 1.6), "R": P(-0.42, 0.0, 1.6)}
    ankle_ = {"L": P(0.46, 0.0, 0.16), "R": P(-0.46, 0.0, 0.16)}
    def spread(r):
        for s_, x in (("L", 1), ("R", -1)):
            human.reach(r, s_, elbow_[s_], hand_[s_])
            for b in ("upperleg01", "upperleg02"): human.aim(r, f"{b}.{s_}", P(0.3 * x, 0.0, 0.55))
            for b in ("lowerleg01", "lowerleg02"): human.aim(r, f"{b}.{s_}", ankle_[s_])
            human.aim(r, f"foot.{s_}", ankle_[s_] + Vector((0.05 * x, -0.12, -0.06)))
    hands_, _, _ = human_body(spread, coat=False, head="mannequin", mitts="flesh", shape="male-young-averagemuscle-minweight",
                              dress={"foot": "flesh", "shin": "flesh", "thigh": "flesh", "torso": "flesh", "hand": "flesh"})
    MODULES["Weapon2"].append(MODULES["Body"].pop())                                  # the martyr's body belongs to the wheel
    cloth_cone(0, 0, 1.03, 0.77, 0.16, 0.2, "rags", sy=0.72, ragged=0.05, rnd=__import__("random").Random(1925), tris=90, thick=0.02, rows=5)   # loincloth
    for z, r in ((1.32, 0.17), (1.12, 0.15)):                                            # ropes round chest and belly
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.022, major_segments=10, minor_segments=3, location=(0, 0.0, z))
        o = bpy.context.object; o.scale = (1, 0.85, 1); add(o, "rope")
    knots = []
    def bind(at, d_, r):   # a rope wrapped round a limb at `at`, the limb running along d_
        bpy.ops.mesh.primitive_torus_add(major_radius=r, minor_radius=0.02, major_segments=8, minor_segments=4, location=tuple(at))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d_); add(o, "rope")
    for s_, x in (("L", 1), ("R", -1)):
        el, an = elbow_[s_] + Vector((0, 0, FLOOR)), ankle_[s_] + Vector((0, 0, FLOOR))
        sh, kn = Vector((0.19 * x, 0, 1.45)), Vector((0.26 * x, 0, 0.56))
        bind(hands_[s_], hands_[s_] - el, 0.036); knots.append(hands_[s_])                  # wrists, tied off to the rim
        bind(an, an - kn, 0.04); knots.append(an)                                       # ankles, tied off to the rim
        bind((sh + el) / 2, el - sh, 0.046)                                              # upper arms
        bind(Vector((0.13 * x, 0, 0.74)), kn - Vector((0.1 * x, 0, 0.92)), 0.068)        # thighs
        bind((kn + an) / 2, an - kn, 0.046)                                               # shins
    # onto the wheel: centred, front turned out along the axle, 0.44 of its size
    k_ = 0.44; cen = Vector((0, 0, 0.98))
    Mw = Matrix.Translation(wc + ax_ * 0.09) @ Matrix.Rotation(math.radians(45), 4, 'Z') @ Matrix.Scale(k_, 4) @ Matrix.Translation(-cen)
    for o in MODULES["Weapon2"][n0:]:
        o.matrix_world = Mw @ o.matrix_world
    for at in knots:                                                                      # each binding tied off to the rim
        w_ = Mw @ at; rim = wc + ((w_ - wc) - ax_ * (w_ - wc).dot(ax_)).normalized() * (R - 0.02) + ax_ * 0.03
        bar(w_, rim, 0.011, "rope", sides=4)
    Mwi = Mw.inverted()
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **KIT, **dict(hull="524739", hulld="332c23", hulll="766856", mail="3a3c40", maill="5c5e64",
                    flesh="9c7a6a", fleshd="6a4e44", fleshl="b8968a", pale="c8bcaa", paled="8a7e6e", gore="4a3a32",
                    rags="6a5c46", ragsd="463c2e", ragsl="84745a")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def mail(p, n, ao):
        return C["ink"] if (p.z * 160) % 1.0 < 0.3 and ((p.x + p.y) * 80 + (p.z * 160 // 1) * 0.5) % 1.0 < 0.4 else lit(C["maill"] if n.dot(PSX_LIGHT) > 0.4 else C["mail"], n, ao, metal=True, p=p)
    def in_box(p):   # the hull or stone box p lies on: (local point, half size), or None
        best = None
        for c_, Ri, h in BOXES:
            q = Ri @ (p - c_); f = max(abs(q.x) - h.x, abs(q.y) - h.y, abs(q.z) - h.z)
            if f < 0.012 and (best is None or f < best[0]): best = (f, q, h)
        return best and (best[1], best[2])
    def face_frame(q, h):   # on the box's face: its two in-face coordinates (a along, b across) and the distance to its nearest edge
        k = max(range(3), key=lambda i: abs(q[i]) / h[i])
        i, j = [a for a in range(3) if a != k]
        return k, q[i], q[j], min(h[i] - abs(q[i]), h[j] - abs(q[j])), i, j
    def hull(p, n, ao):   # the hull's plates: worn light edges, an ink seam inside them, rivet rows, rust streaks, grime, blood
        x, y, z = p
        bq = in_box(p)
        c = C["hulll"] if n.dot(PSX_LIGHT) > 0.7 else C["hull"]
        if bq is not None:
            q, h = bq; k, a, b, e, i, j = face_frame(q, h)
            if max(abs(n.x), abs(n.y), abs(n.z)) < 0.86: return lit(C["hulll"], n, ao, metal=True, p=p)        # a worn, bevelled edge
            if e < 0.008: return C["ink"]
            if min(h[i], h[j]) > 0.06:
                ea, eb = h[i] - abs(a), h[j] - abs(b)
                for ed, al in ((ea, b), (eb, a)):   # a rivet row 0.028 in from each edge
                    if abs(ed - 0.028) < 0.012:
                        rc = round(al / 0.055) * 0.055; dd = math.hypot(ed - 0.028, al - rc)
                        if dd < 0.008: return C["hulll"] if al - rc < 0.002 else C["hull"]   # light on one side
                        if dd < 0.012: return C["ink"]
                if max(h[i], h[j]) > 0.14:   # a panel seam across the larger plates
                    big, other = (a, h[i]) if h[i] > h[j] else (b, h[j])
                    if abs(big) < 0.005: return C["ink"]
                    if 0.005 <= big < 0.011: c = C["hulld"]
        if _nz.noise(Vector((x * 30, y * 30, z * 4))) > 0.5: c = c.lerp(C["rust"], 0.6)                    # rust streaks running down
        b_ = psx_blood(C, p, 0.15 * max(0.0, 1 - z / 0.6))
        if b_ is not None and n.z < 0.6: return b_
        return lit(c, n, ao, metal=True, grime=0.5 * max(0.0, 1 - z / 0.5), p=p)
    def emblem(x, z, g=0.0):   # the winged emblem on the breastplate: a disc with a dark cross, a fanned gilt wing each side
        u, v = x - em.x, z - em.z
        r = math.hypot(u, v)
        if r < 0.05 + g: return "cross" if g == 0 and ((abs(u) < 0.009 and abs(v) < 0.042) or (abs(u) < 0.03 and abs(v - 0.01) < 0.008)) else "gilt"
        du = abs(u); th = math.atan2(v, du)
        if du > 0.03 - g and -0.15 - g * 6 < th < 1.05 + g * 6 and r < 0.19 + g - 0.06 * max(0.0, 0.3 - th):   # raised wings
            return "feather" if g == 0 and (math.degrees(th) / 14.0) % 1.0 < 0.14 else "gilt"   # ink between the feathers
        return None
    CHAINS = [-0.3 + i * 0.1 for i in range(7) if abs(-0.3 + i * 0.1) >= 0.06]
    # detail pass (2026-11-10): hard-surface wear on the hull. Dents (a point on a face, its radius), gouges (segments),
    # a litany painted in bone on a band across each pauldron's front, soot round the stacks, oil run from the elbow
    # pistons, mud up the legs, a tally scratched on the left greave
    DENTS = [(Vector((0.17, -0.36, 1.27)), 0.026), (Vector((-0.19, -0.35, 1.12)), 0.02), (Vector((0.55, -0.3, 1.31)), 0.024),
             (Vector((-0.46, -0.3, 1.14)), 0.022), (Vector((-0.72, 0.12, 1.12)), 0.026), (Vector((0.72, -0.13, 1.33)), 0.02),
             (Vector((-0.27, -0.22, 0.88)), 0.02), (Vector((0.26, -0.17, 0.33)), 0.018)]
    GOUGES = [(Vector((-0.12, -0.36, 1.31)), Vector((0.04, -0.37, 1.22))), (Vector((0.42, -0.3, 1.18)), Vector((0.6, -0.3, 1.1))),
              (Vector((-0.72, -0.16, 1.35)), Vector((-0.72, 0.02, 1.3))), (Vector((0.31, -0.22, 0.9)), Vector((0.2, -0.22, 0.83))),
              (Vector((-0.35, -0.3, 1.36)), Vector((-0.6, -0.3, 1.3)))]
    def wear(p, n, ao):
        x, y, z = p; s_ = n.dot(PSX_LIGHT)
        for c_, r in DENTS:
            q = p - c_
            if q.length < r:
                t_ = q - n * q.dot(n); up_ = Vector((0, 0, 1)) - n * n.z; lu = t_.dot(up_.normalized()) if up_.length > 0.1 else t_.y
                if q.length > r * 0.62: return C["hulll"] if lu > 0 else C["ink"]   # the lit rim above, the shadowed one under
                if q.length < r * 0.2: return C["hulll"]                           # a bright pit
                return lit(C["hulld"], n, ao, metal=True, p=p)
        for a_, b_ in GOUGES:
            dd, t_ = segd(p, a_, b_)
            if dd < 0.009 and abs((p - a_).dot(n)) < 0.03:
                if dd < 0.0035: return C["hulll"] if 0.05 < t_ < 0.95 else C["hull"]
                if (p - (a_ + (b_ - a_) * t_)).z < 0: return C["ink"]
        if y < -0.29 and 0.37 < abs(x) < 0.63 and abs(z - 1.31) < 0.036 and n.y < -0.8:   # the litany band on each pauldron front
            u_ = abs(x) - 0.5
            if abs(z - 1.31) > 0.03 or abs(u_) > 0.124: return C["ink"]
            cu = ((u_ + 0.11) / 0.044) % 1.0 - 0.5; cz = z - 1.31   # a row of small crosses (glyphs read as blots at this size)
            if abs(u_) < 0.11 and ((abs(cu) < 0.1 and abs(cz) < 0.02) or (abs(cu) < 0.3 and abs(cz - 0.006) < 0.0045)): return C["ink"]
            return lit(C["bone"] if s_ > 0.3 else C["boned"], n, ao, grime=0.3, p=p)
        for sx in (-1, 1):   # soot round each stack's foot and a streak down the hull's back below it
            dq = math.hypot(x - 0.3 * sx, y - 0.2)
            if z > 1.28 and dq < 0.13 and _nz.noise(p * 18) > -0.4 + dq * 4: return lit(C["irond"].lerp(C["hulld"], 0.4), n, ao, p=p)
            if y > 0.29 and abs(x - 0.3 * sx) < 0.05 - (1.34 - z) * 0.12 and z > 1.0: return lit(C["hulld"].lerp(C["ink"], 0.4), n, ao, p=p)
        for sx, yc_ in ((-1, -0.18), (1, -0.36)):   # oil run down from each elbow piston
            if abs(x - 0.6 * sx) < 0.012 and y < yc_ + 0.02 and 0.5 < z < 0.74 and z > 0.72 - 0.22 * (0.5 + 0.5 * _nz.noise(Vector((sx * 3, 0, 0)))):
                return C["dark"] if abs(x - 0.6 * sx) < 0.006 else lit(C["hulld"], n, ao, p=p)
        if x > 0.3 and n.x > 0.8 and 0.22 < z < 0.31 and -0.12 < y < 0.0:   # the tally scratched on the left greave
            for k in range(4):
                if abs(y - (-0.1 + 0.022 * k)) < 0.004: return C["hulll"]
            if abs((z - 0.265) - (y + 0.065) * 0.9) < 0.004 and -0.11 < y < -0.02: return C["hulll"]
        if z < 0.46 and abs(abs(x) - 0.24) < 0.15 and _nz.noise(p * 10 + Vector((1, 2, 3))) > 0.42 - (0.46 - z) * 1.6:   # mud up the legs
            return lit(C["mud"] if _nz.noise(p * 30) > -0.35 else C["hulld"].lerp(C["mud"], 0.5), n, ao, p=p)
        return None
    def parts(base, p, n, ao):   # the new solid parts painted (None: not one of these)
        x, y, z = p; s_ = n.dot(PSX_LIGHT)
        if base == "iron" and abs(x) > 0.69 and 1.05 < z < 1.37 and abs(y) < 0.13:   # the bolted pauldron crosses
            q = Vector((0, y, z - 1.21))
            if any(math.hypot(y - by, z - bz) < 0.01 for by, bz in ((0, 1.31), (0, 1.11), (0.085, 1.25), (-0.085, 1.25))): return C["ironl"] if z > 1.0 and s_ > 0 else C["metal"]
            if abs(n.x) < 0.8: return C["ironl"]                                                       # worn bright edges
            if _nz.noise(p * 40) > 0.4: return lit(C["rust"], n, ao, p=p)
            return lit(C["iron"], n, ao, metal=True, p=p)
        if base == "bone" and CANDLES and min((p - c_).length for c_, _ in CANDLES) < 0.12:   # the candles: pale wax, drips, a dark wick end
            c_, h_ = min(CANDLES, key=lambda c: (p - c[0]).length)
            if z > c_.z + h_ - 0.006: return C["bonel"]
            if (math.atan2(p.y - c_.y, p.x - c_.x) * 5 / math.pi) % 2.0 < 0.5 and z > c_.z + h_ * 0.5: return C["bonel"]   # wax runs
            return lit(C["bone"] if s_ > 0 else C["boned"], n, ao, p=p)
        if base == "iron" and LANTERNS and min((p - l_).length for l_ in LANTERNS) < 0.075:   # the lanterns: glowing horn windows in an iron frame
            l_ = min(LANTERNS, key=lambda l: (p - l).length); q = p - l_
            if q.z > 0.038: return lit(C["ironl"] if s_ > 0.3 else C["iron"], n, ao, metal=True, p=p)
            if abs(n.z) < 0.5:
                a_ = q.x if abs(n.y) > abs(n.x) else q.y
                if abs(a_) < 0.02 and abs(q.z) < 0.028: return C["glow"] if abs(a_) < 0.012 and abs(q.z) < 0.02 and abs(a_) > 0.003 else C["glow"].lerp(C["dark"], 0.55)
            return C["ink"] if abs(q.z) > 0.036 else lit(C["iron"], n, ao, metal=True, p=p)
        if base == "brass" and (p - GAUGE).length < 0.06:   # the gauge: a pale dial, ticks, a needle in the red
            q = p - GAUGE
            if n.y > 0.6:
                r = math.hypot(q.x, q.z)
                if r > 0.036: return C["brassl"] if q.z > 0 else C["brass"]
                if r < 0.005: return C["ink"]
                ang = math.atan2(q.z, q.x)
                if abs(ang - 0.5) < 0.12 and r < 0.03: return C["ink"]
                if r > 0.028 and (ang * 12 / (2 * math.pi)) % 1.0 < 0.25: return C["ink"]
                if r > 0.024 and -0.6 < ang < 0.2: return C["charge"]
                return C["parchment"]
            return lit(C["brassl"] if s_ > 0.4 else C["brass"], n, ao, metal=True, p=p)
        return None
    def paint(m, p, n, ao, tx, ty):
        base = m.split(".")[0]; x, y, z = p.x, p.y, p.z
        r_ = parts(base, p, n, ao)
        if r_ is not None: return r_
        if base == "hull":
            if y < -0.3 and 1.0 < z < 1.36:   # the winged emblem
                e_ = emblem(x, z)
                if e_ in ("cross", "feather"): return C["ink"]
                if e_ == "gilt": return lit(C["giltl"] if n.dot(PSX_LIGHT) > 0.3 else C["gilt"], n, ao, metal=True, p=p)
                if emblem(x, z, 0.005): return C["ink"]
            if y < -0.3 and abs(x) < 0.185 and abs(z - 1.0) < 0.02:   # the anchorite's dark slit, across the faceplate's top
                return C["ink"] if abs(z - 1.0) > 0.014 or abs(x) > 0.178 else (C["glow"].lerp(C["dark"], 0.7) if abs(x) < 0.03 else C["dark"])
            if z < 0.42 and abs(abs(x) - 0.24) < 0.15:   # the greaves' iron bands
                for bz in (0.2, 0.32):
                    if abs(z - bz) < 0.02: return C["ink"] if abs(z - bz) > 0.014 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
            if abs(x + 0.58) < 0.13 and 0.48 < z < 0.73:   # the right vambrace's ribs
                for bz in (0.53, 0.67):
                    if abs(z - bz) < 0.017: return C["ink"] if abs(z - bz) > 0.012 else lit(C["iron"], n, ao, metal=True, p=p)
            if abs(x - 0.62) < 0.13 and 0.56 < z < 0.8 and y < -0.04:   # the left vambrace's ribs
                for by in (-0.28, -0.14):
                    if abs(y - by) < 0.017: return C["ink"] if abs(y - by) > 0.012 else lit(C["iron"], n, ao, metal=True, p=p)
            if abs(x) > 0.66 and 1.0 < z < 1.44 and abs(z - 1.22) < 0.032:   # the pauldrons' iron rims, riveted
                if abs(z - 1.22) > 0.026: return C["ink"]
                if (y * 18) % 1.0 < 0.22 and abs(z - 1.22) < 0.01: return C["brassl"]
                return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
            w_ = wear(p, n, ao)
            if w_ is not None: return w_
            return hull(p, n, ao)
        if base == "stone":
            for c_, n_, t_ in gables:   # the gables' rose slits
                q = p - c_
                if abs(q.dot(n_)) < 0.035 and q.z > -0.01:
                    a, v = q.dot(t_), q.z - 0.065; r = math.hypot(a, v)
                    if r < 0.04 and q.dot(n_) > -0.005:
                        if r > 0.034: return C["ink"]
                        if r < 0.03 and (abs(a) < 0.005 or abs(v) < 0.005 or r < 0.008): return lit(C["stone"], n, ao, p=p)
                        return C["dark"] if r > 0.012 else C["glow"]
                    break
            if abs(x) < 0.175 and abs(y - 0.02) < 0.175 and 1.36 < z < 1.62:   # the block: three pointed arched windows each side
                q = Vector((x, y - 0.02, 0)); k = 0 if abs(q.x) > abs(q.y) else 1
                a = q.y if k == 0 else q.x
                for off in (-0.09, 0.0, 0.09):
                    da = abs(a - off); top = 1.55 + max(0.0, 0.025 - da) * 1.6
                    if da < 0.026 and 1.4 < z < top + 0.006:
                        if da > 0.02 or z < 1.406 or z > top: return C["ink"]
                        return C["glow"].lerp(C["dark"], 0.5) if z < 1.43 and da < 0.01 else C["dark"]
            if y < -0.3 and z < 0.86:   # the grave-slab plaques: an iron cross, worn lettering
                for sx in (-1, 1):
                    u = x - 0.21 * sx
                    if abs(u) < 0.13:
                        if (abs(u) < 0.02 and abs(z - 0.5) < 0.13) or (abs(u) < 0.07 and abs(z - 0.56) < 0.02): return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.3 else C["iron"], n, ao, metal=True, p=p)
                        if (abs(u) < 0.028 and abs(z - 0.5) < 0.138) or (abs(u) < 0.078 and abs(z - 0.56) < 0.028): return C["ink"]
                        if abs(u) < 0.08 and 0.33 < z < 0.37 and (u * 90) % 1.0 < 0.5: return C["stoned"]
                        return lit(C["stonel"] if n.dot(PSX_LIGHT) > 0.6 else C["stone"], n, ao, grime=0.25, p=p)   # a plain slab, no courses
            mz = (z * 20) % 1.0; row = int(z * 20)   # masonry courses, staggered joints
            if mz < 0.12 and abs(n.z) < 0.7: return lit(C["stoned"], n, ao, p=p)
            if abs(n.z) < 0.7 and ((x + y) * 12 + row * 0.5) % 1.0 < 0.06: return lit(C["stoned"], n, ao, p=p)
            return lit(C["stonel"] if n.dot(PSX_LIGHT) > 0.6 else C["stone"], n, ao, grime=0.25, p=p)
        if base == "rust":   # the spire: iron bands, ink ribs between its faces, rust streaks
            for bz, r in ((1.8, 0.12), (1.98, 0.084)):
                if abs(z - bz) < 0.016: return C["ink"] if abs(z - bz) > 0.011 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.3 else C["iron"], n, ao, metal=True, p=p)
            ang = math.atan2(y - 0.02, x) - math.pi / 8
            if (ang * 12 / (2 * math.pi)) % 1.0 < 0.08: return C["ink"]
            c = C["rustl"] if n.dot(PSX_LIGHT) > 0.5 else (C["rust"] if _nz.noise(Vector((x * 40, y * 40, z * 4))) < 0.3 else C["rustd"])
            return lit(c, n, ao, metal=True, p=p)
        if base == "mail":
            if y < -0.2 and 0.5 < z < 0.9:   # chain strands hung under the faceplate
                for i, cx_ in enumerate(CHAINS):
                    zz = z + (0.035 if i % 2 else 0.0)
                    if abs(x - cx_) < 0.016 and zz > 0.5:
                        lk = (zz * 14) % 1.0
                        if abs(x - cx_) > 0.011 or lk < 0.15: return C["ink"]
                        return lit(C["ironl"] if lk > 0.6 else C["iron"], n, ao, metal=True, p=p)
            if y > 0.25 and z > 0.4:   # parchment tags on the chain curtains
                for zt in (0.7, 0.55):
                    u = abs(x) - 0.5
                    if abs(u) < 0.034 and abs(z - zt) < 0.044:
                        if abs(u) > 0.028 or abs(z - zt) > 0.038: return C["ink"]
                        if math.hypot(u, z - zt - 0.02) < 0.01: return C["blood"]
                        if (z * 120) % 1.0 < 0.3 and abs(u) < 0.02: return C["ink"]
                        return lit(C["parchment"], n, ao, p=p)
            if y > 0.25 and 0.29 < abs(x) < 0.69:   # detail pass (2026-11-10): the curtains as hanging strands of links, dark between
                k_ = round((abs(x) - 0.3) / 0.054); cx_ = 0.3 + k_ * 0.054; du = abs(abs(x) - cx_)
                if du < 0.016:
                    lk = ((z + (0.03 if k_ % 2 else 0.0)) * 16) % 1.0
                    if du > 0.012 or lk < 0.14: return C["ink"]
                    if lk < 0.55: return C["ink"] if du < 0.004 else lit(C["maill"] if n.dot(PSX_LIGHT) > 0.2 else C["mail"], n, ao, metal=True, p=p)   # a ring, face on
                    return lit(C["maill"] if du < 0.005 else C["mail"], n, ao, metal=True, p=p)                # a ring, edge on
                return C["dark"] if du > 0.022 else lit(C["mail"].lerp(C["dark"], 0.6), n, ao, p=p)
            return mail(p, n, ao)
        if base == "iron" and abs(y - 0.43) < 0.012 and math.hypot(x, z - 1.05) < 0.23:   # the boiler's back: a riveted hatch, its wheel
            r = math.hypot(x, z - 1.05)
            if abs(r - 0.12) < 0.008: return C["ink"]
            if abs(r - 0.2) < 0.01 and (math.atan2(z - 1.05, x) * 16 / (2 * math.pi)) % 1.0 < 0.35: return C["brassl"]
            if r < 0.12:
                if abs(z - 1.05) < 0.01 and abs(x) < 0.1: return C["brassl"] if abs(z - 1.05) < 0.006 else C["ink"]
                return lit(C["metall"] if n.dot(PSX_LIGHT) > 0.3 else C["metal"], n, ao, metal=True, p=p)
        if base == "metal" and n.z > 0.8 and abs(abs(x) - 0.3) < 0.08 and z > 1.68:   # the stacks' sooty mouths
            return C["ink"] if math.hypot(abs(x) - 0.3, y - 0.2) < 0.05 else lit(C["irond"], n, ao, p=p)
        if base == "wood" and segd(p, fist - d * 0.22, tip)[0] < 0.04:   # the mace's haft: a leather grip, iron bands
            t = (p - fist).dot(d)
            if -0.14 < t < 0.12: return C["ink"] if (t * 80) % 1.0 < 0.2 else lit(C["leather"], n, ao, p=p)
            L = (tip - fist).length
            for tb in (0.25, 0.55):
                if abs(t - L * tb) < 0.022: return C["ink"] if abs(t - L * tb) > 0.017 else lit(C["iron"], n, ao, metal=True, p=p)
            g = _nz.noise((p - d * t) * 60 + d * (t * 3))
            return lit(C["woodd"] if g > 0.35 else (C["woodl"] if n.dot(PSX_LIGHT) > 0.5 else C["wood"]), n, ao, p=p)
        if base == "gore":   # the mace's head: dark iron caked in blood
            b_ = psx_blood(C, p, 0.35)
            if b_ is not None: return b_
            return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.7 else C["gore"], n, ao, metal=True, p=p)
        if base == "pale":   # the plucked dove: goose-pimpled pale skin, blood at the neck
            if z < 0.66 or psx_blood(C, p, 0.1) is not None: return C["blood"]
            return lit(C["pale"] if n.dot(PSX_LIGHT) > 0.0 else C["paled"], n, ao, p=p) if (p.x * 300 + p.z * 300) % 1.0 > 0.15 else C["paled"]
        if base == "brass" and abs(x) < 0.06 and abs(y + 0.4) < 0.06 and z < 0.58:   # the bell: an ink lip, a dark mouth
            if n.z < -0.5: return C["dark"]
            if z < 0.5: return C["ink"]
        if base == "mannequin":   # the martyr's blank head: smooth pale plaster
            return lit(C["mannequin"] if n.dot(PSX_LIGHT) > 0.0 else C["mannequind"], n, ao, p=p)
        if base == "flesh":   # the martyr: gaunt skin in hard bands, ribs, lash wounds and blood
            q = Mwi @ p   # in the martyr's own standing frame
            c = C["fleshl"] if n.dot(PSX_LIGHT) > 0.62 else (C["flesh"] if n.dot(PSX_LIGHT) > -0.1 else C["fleshd"])
            if 1.12 < q.z < 1.36 and q.y < 0 and abs(q.x) > 0.03 and (q.z * 28) % 1.0 < 0.18: c = C["fleshd"]   # the ribs
            if (math.sin((q.x * 0.7 + q.z) * 60 + _nz.noise(q * 6) * 2) > 0.94 and _nz.noise(q * 4) > -0.2): return C["blood"]
            b_ = psx_blood(C, q, 0.15)
            if b_ is not None: return b_
            return lit(c, n, ao, p=p)
        if base == "rags":
            q = Mwi @ p; f = (math.atan2(q.x, -q.y) * 6 + _nz.noise(q * 10)) % 1.0
            b_ = psx_blood(C, q, 0.2)
            if b_ is not None: return b_
            return lit(C["ragsd"] if f < 0.2 else C["rags"], n, ao, p=p)
        return psx_kit(C, base, p, n, ao) or lit(C["metal"], n, ao, p=p)
    PAINT = paint

def sanctified_giant():
    # Sanctified Giant (2026-11-03, seventh figure of docs/art-pipeline.md, class C, 40 mm): an original figure on the
    # owner's reference cues, not a copy of the miniature, the art or any paint job. A hulking, stooped brute, bare to the
    # waist, pale and bloodied, iron crosses nailed into both shoulders; his head under a cloth hood with a long iron
    # cross nailed down its front (no face, no head under it: the hood and cross are his capirote and mask); an iron
    # vambrace on the left forearm; a ragged skirt with a rope belt and skulls; an arched reliquary plaque on his back; a
    # long polearm held across the body, a cluster of chains, a bell, spiked balls and skulls hanging from its head.
    # The pose follows the references (owner, 2026-11-03): striding, the left leg forward and the right back on its toes,
    # hunched far forward with the head thrust low between the shoulders, the polearm held high across the body by the
    # right fist at the shoulder and the armoured left fist low at the hip, its chains hanging out at the right.
    # Painted PSX look (2026-11-10, as the Combat Engineer): the smooth bare body painted texel by texel (hard skin
    # bands, lash scars, a stitched wound, blood running from the iron crosses nailed into the shoulders, the riveted
    # vambrace, rag-wrapped fists and ankles), the skirt draped in its own node (Robe: folds, blood, mud, the rope belt
    # and a little grave plaque painted); the hood cross's nails, the skulls' sockets, the plaque's icon, crossed bones and
    # skull, the bells' rims and the blade's edge painted on their solid shapes. Modules: Helmet (hood cross), Robe
    # (skirt), Hip (skulls), Back (plaque), Weapon (polearm and its chains), Accent (the hood). Detail pass (2026-11-10):
    # a penitent's chain wound across the torso, prayer strips with wax seals on the belt, an ankle manacle with a broken
    # chain (Hip) and iron strips on the plaque made solid; chafed skin, a branded cross, a rope on the arm, coherent blood,
    # mud on the shins, the belt's knotted end, sacking patches and a twine seam on the skirt painted.
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1750
    TEX_SIZES = {"Body": 256, "Robe": 256, "Back": 128, "Weapon": 128, "Helmet": 128, "Hip": 128}   # class C: a 256 px body
    from mathutils import noise as _nz
    def pose(r):
        human.aim(r, "spine03", P(0, -0.2, 1.34)); human.aim(r, "neck01", P(0, -0.22, 1.62))
        human.aim(r, "upperleg01.L", P(0.13, -0.24, 0.52)); human.aim(r, "upperleg02.L", P(0.13, -0.24, 0.52))
        human.aim(r, "lowerleg01.L", P(0.14, -0.2, 0.08)); human.aim(r, "lowerleg02.L", P(0.14, -0.2, 0.08))
        human.aim(r, "foot.L", P(0.15, -0.36, -0.01))
        human.aim(r, "upperleg01.R", P(-0.12, 0.1, 0.5)); human.aim(r, "upperleg02.R", P(-0.12, 0.1, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.13, 0.32, 0.13)); human.aim(r, "lowerleg02.R", P(-0.13, 0.32, 0.13))
        human.aim(r, "foot.R", P(-0.13, 0.2, -0.02))                                             # up on its toes
        human.reach(r, "R", P(-0.36, -0.14, 1.14), P(-0.22, -0.4, 1.3))
        human.reach(r, "L", P(0.31, -0.1, 0.98), P(0.19, -0.32, 0.88))
    hands, hp, top = human_body(pose, coat=False, head=None, mitts="brute", shape="male-young-maxmuscle-maxweight", tris=1500,
                                dress={"foot": "brute", "shin": "brute", "thigh": "brute", "torso": "brute", "hand": "brute"})   # bare legs under the skirt
    import random; rnd = random.Random(1923)
    human_ = bpy.data.objects["human"]
    # a giant: the chest, shoulders and arms broadened (across only, so the head and the fists' grip stay put)
    def broad(v):
        return Vector((v.x * (1 + 0.24 * min(1.0, max(0.0, (v.z - 0.95) / 0.4))), v.y, v.z))
    for v in human_.data.vertices: v.co = broad(v.co)
    for o in MODULES["Body"]:
        if o is not human_: o.location = broad(o.location)
    for s_ in ("L", "R"): FISTS[s_] = (broad(FISTS[s_][0]), FISTS[s_][1])
    for k_ in JOINTS: JOINTS[k_] = broad(JOINTS[k_])   # the painted arms follow the broadened ones
    human_.data.update(); bpy.context.view_layer.update()   # moved by hand: refresh before ray casting (the shoulder crosses missed before)
    def hit(origin, d_):
        ok, loc, nrm, _ = human_.ray_cast(Vector(origin), Vector(d_))
        return (loc, nrm) if ok else (None, None)
    skulls = []
    def skull(at, r=0.045, rot=0.0):
        sphere(tuple(at), 1, "bone", (r, r * 1.1, r * 1.05), seg=6, rings=4)
        box(tuple(at + Vector((0, -r * 0.55, -r * 0.85))), (r * 1.1, r * 0.8, r * 0.5), "bone", rot=(0, 0, rot))   # the jaw
        skulls.append((at, r))
    module("Helmet")
    # the hood: a cloth bag over the whole head, falling to the shoulders, in the seat colour; the iron cross nailed down
    # its front where a face would be
    # the bowed neck pitches the head itself to the ground; the hood faces forward, leaning a little, as on the miniature
    # set on the skeleton's own head joint (the top of the neck), not the pitched face
    hup = Vector((0, -0.3, 1)).normalized(); fb = {"ax": Vector((1, 0, 0)), "up": hup, "fwd": Vector((1, 0, 0)).cross(hup)}
    centre = hp + hup * 0.05 + fb["fwd"] * 0.015                                          # low, close on the neck
    # A cloth bag simulated over the head: a cap shaped round a stand-in head (pinned, so it keeps the skull's shape)
    # and a loose skirt below it that falls onto the neck and shoulders. The stand-in head and its collider go after.
    R = 0.112; segs = 20; cap = 9; tail = 5
    side_ = lambda t: fb["ax"] * math.cos(t) + fb["fwd"] * math.sin(t)
    verts, faces, pins = [tuple(centre + hup * R * 1.15)], [], [0]
    for k in range(1, cap + tail + 1):
        if k <= cap:
            ph = math.radians(105) * k / cap; ring_c, ring_r = centre + hup * R * 1.15 * math.cos(ph), R * math.sin(ph)
        else:
            d = 0.045 * (k - cap); ring_c, ring_r = centre + hup * (R * 1.15 * math.cos(math.radians(105)) - d), R * math.sin(math.radians(105)) + 0.5 * d
        for i in range(segs):
            verts.append(tuple(ring_c + side_(2 * math.pi * i / segs) * ring_r))
            if k <= cap - 2: pins.append(len(verts) - 1)
    for i in range(segs): faces.append((0, 1 + i, 1 + (i + 1) % segs))
    for k in range(1, cap + tail):
        for i in range(segs):
            a_, b_ = 1 + (k - 1) * segs + i, 1 + (k - 1) * segs + (i + 1) % segs
            faces.append((a_, b_, b_ + segs, a_ + segs))
    me = bpy.data.meshes.new("hood"); me.from_pydata(verts, [], faces); me.validate()
    hood = bpy.data.objects.new("hood", me); scn.collection.objects.link(hood)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=R * 0.9, location=tuple(centre)); stand = bpy.context.object
    drape(hood, pins, [stand, human_], frames=50, tris=320, thick=0.008); add(hood, "accent")
    bpy.data.objects.remove(stand)
    face = centre + fb["fwd"] * 0.112
    bar(face - fb["up"] * 0.14, face + fb["up"] * 0.22, 0.022, "iron", sides=4)               # the long upright, above the crown
    bar(face + fb["up"] * 0.1 - fb["ax"] * 0.075, face + fb["up"] * 0.1 + fb["ax"] * 0.075, 0.022, "iron", sides=4)   # the crossbar
    nails = [face + fb["up"] * 0.2, face + fb["up"] * 0.1 + fb["ax"] * 0.065, face + fb["up"] * 0.1 - fb["ax"] * 0.065, face - fb["up"] * 0.06]   # nail heads (painted)
    # iron crosses nailed into both shoulders and the left forearm's vambrace are painted on the body
    crosses = []
    for s in (-1, 1):
        ok_, loc, nrm, _ = human_.closest_point_on_mesh(JOINTS["upperarm01." + ("L" if s > 0 else "R")] + Vector((0.12 * s, -0.02, 0.0)))   # the shoulder's outside, clear of the hood
        if not ok_: continue
        up_ = (Vector((0, 0, 1)) - nrm * nrm.z).normalized(); crosses.append((loc, up_, nrm.cross(up_), nrm))
    fl, dl = FISTS["L"]
    module("Robe")
    # the ragged skirt from the waist to the shins (a rope belt and a little grave plaque painted on it); split at the
    # front left, where the striding leg comes out: no cloth in that wedge below the upper thigh
    # Fitted to the body: each ring of the skirt is ray-cast onto the waist and hips from outside, a little proud of
    # them, and never narrower than the ring above (so it hangs over the legs rather than wrapping each one).
    n_, rows = 32, 10
    def slit(a_, z): return math.radians(2) < a_ < math.radians(58) and z < 0.84
    def mid_y(z):
        f_, _ = hit((0, -1.0, z), (0, 1, 0)); k_, _ = hit((0, 1.0, z), (0, -1, 0))
        return (f_.y + k_.y) / 2 if f_ is not None and k_ is not None else 0.0
    yc = mid_y(1.0)
    verts, faces, prev = [], [], [0.0] * n_
    for j in range(rows + 1):
        z = 1.04 - 0.58 * j / rows
        for i in range(n_):
            a_ = 2 * math.pi * i / n_; d_ = Vector((math.sin(a_), -math.cos(a_), 0)); c_ = Vector((0, yc, z))
            loc, _ = hit(c_ + d_ * 1.0, -d_)
            r = ((loc - c_).to_2d().length if loc is not None else 0.18) + (0.014 if j == 0 else 0.04)
            r = max(r, prev[i]); prev[i] = r
            zz = z + (rnd.uniform(0.0, 0.07) if j == rows else 0.0)
            verts.append(tuple(c_ + d_ * r + Vector((0, 0, zz - z))))
    for j in range(rows):
        for i in range(n_):
            a_ = 2 * math.pi * (i + 0.5) / n_; z = 1.04 - 0.58 * (j + 0.5) / rows
            if slit(a_, z): continue
            i2 = (i + 1) % n_; faces.append((j * n_ + i, j * n_ + i2, (j + 1) * n_ + i2, (j + 1) * n_ + i))
    used = sorted({v for f in faces for v in f}); at = {v: k for k, v in enumerate(used)}   # no loose vertices in the split
    me = bpy.data.meshes.new("skirt"); me.from_pydata([verts[v] for v in used], [], [tuple(at[v] for v in f) for f in faces])
    o = bpy.data.objects.new("skirt", me); scn.collection.objects.link(o)
    drape(o, [at[i] for i in range(n_)], [human_], frames=60, tris=420, thick=0.01); add(o, "skirt")
    hem = hem_line(o, 0.0, yc)
    module("Hip")
    skull(Vector((0.12, -0.2, 0.88)), rot=0.2); skull(Vector((-0.17, -0.12, 0.86)), 0.04, rot=-0.4)
    bar(Vector((0.12, -0.18, 0.99)), Vector((0.12, -0.2, 0.93)), 0.006, "rope", sides=3); bar(Vector((-0.17, -0.11, 0.99)), Vector((-0.17, -0.12, 0.9)), 0.006, "rope", sides=3)
    # detail pass (2026-11-10): prayer strips hanging from the rope belt behind the left hip, each with a wax seal; an
    # iron manacle on the right ankle trailing a broken chain
    sk_ = MODULES["Robe"][-1]; STRIPS = []
    for k, a_deg in enumerate((118, 132, 146)):
        a_ = math.radians(a_deg); d_ = Vector((math.sin(a_), -math.cos(a_), 0))
        ok_, at_, *_ = sk_.ray_cast(Vector((0, yc, 0.97)) + d_ * 1.0, -d_)
        at_ = (at_ if ok_ else Vector((0, yc, 0.97)) + d_ * 0.25) + d_ * 0.008
        L_ = 0.14 + 0.03 * (k % 2)
        box(tuple(at_ - Vector((0, 0, L_ / 2))), (0.035, 0.004, L_), "parchment", rot=(0, 0, a_))
        sphere(tuple(at_ + d_ * 0.004 - Vector((0, 0, 0.02))), 0.011, "blood", (1, 0.5, 1), seg=6, rings=3)   # (a wax seal)
        STRIPS.append((at_, d_, L_))
    an_, kn_ = JOINTS["foot.R"], JOINTS["lowerleg01.R"]; shin_ = (kn_ - an_).normalized()
    MAN = an_ + shin_ * 0.06
    bar(MAN - shin_ * 0.018, MAN + shin_ * 0.018, 0.058, "iron", sides=8)
    lk = [MAN + Vector((0.0, 0.04, -0.03)), MAN + Vector((0.01, 0.09, -0.06)), MAN + Vector((-0.01, 0.13, -0.08)), MAN + Vector((0.01, 0.17, -0.09))]
    lk = [Vector((v.x, v.y, max(v.z, 0.012))) for v in lk]
    for a_, b_ in zip(lk, lk[1:]): bar(a_, b_, 0.009, "iron", sides=4)
    # a penitent's chain wound from the right shoulder to the left hip, front and back, lying on the body (ray-cast);
    # links alternately thick and thin, as the polearm's chains
    WOUND = []
    for side_y in (-1, 1):
        pts_ = []
        for k in range(9):
            z_ = 1.5 - 0.5 * k / 8; x_ = -0.2 + (1.5 - z_) * 0.75
            loc, _ = hit((x_, 1.0 * side_y, z_), (0, -side_y, 0))
            if loc is not None: pts_.append(loc + Vector((0, 0.014 * side_y, 0)))
        for j, (a_, b_) in enumerate(zip(pts_, pts_[1:])):
            bar(a_, b_, 0.011 if j % 2 else 0.016, "iron", sides=4); WOUND.append((a_, b_))
    module("Back")
    # the reliquary plaque on the back: an arched slab rising behind the hood (its icon, crossed bones and skull
    # painted); built upright at the origin, then leant with the bowed back (two points of it, ray-cast from behind)
    n0 = len(MODULES["Back"])
    box((0, 0.04, 0.13), (0.3, 0.05, 0.42), "wood")
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.15, depth=0.05, location=(0, 0.04, 0.34))
    o = bpy.context.object; o.rotation_euler = (math.pi / 2, 0, 0); add(o, "wood")
    for sx in (-1, 1): box((0.152 * sx, 0.04, 0.13), (0.016, 0.058, 0.43), "iron")   # iron strips down its sides (detail pass)
    p1, _ = hit((0.0, 1.0, 1.05), (0, -1, 0)); p2, _ = hit((0.0, 1.0, 1.4), (0, -1, 0))
    p1 = p1 or Vector((0, 0.15, 1.05)); p2 = p2 or Vector((0, 0.1, 1.4))
    up_ = (p2 - p1).normalized(); th = math.atan2(-up_.y, up_.z)
    Mp = Matrix.Translation(p1 + (p2 - p1) * 0.45 + Vector((0, 0.05, 0))) @ Matrix.Rotation(th, 4, 'X')   # clear of the hood's collar
    for o in MODULES["Back"][n0:]:
        bpy.context.view_layer.update(); o.matrix_world = Mp @ o.matrix_world
    Mpi = Mp.inverted()
    module("Weapon")
    # the polearm through both fists, its head up and out on the right; a long blade at the top
    fr, _ = FISTS["R"]; u = (fr - fl).normalized(); pole_u = u
    low, top_ = fl - u * 0.3, fr + u * 0.42
    bar(low, top_, 0.022, "wood", sides=6)
    bar(top_ - u * 0.02, top_ + u * 0.06, 0.032, "iron", sides=6)                              # the socket
    side_ = u.cross(Vector((0, -1, 0))).normalized()
    blade = [top_ + u * 0.06 - side_ * 0.03, top_ + u * 0.06 + side_ * 0.03, top_ + u * 0.28]
    me = bpy.data.meshes.new("blade"); me.from_pydata([tuple(v) for v in blade], [], [(0, 1, 2)])
    o = bpy.data.objects.new("blade", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.014; add(o, "blade")
    # the cluster: chains from the polearm's head down to a bell, spiked balls and skulls
    hang = top_ - u * 0.06
    across = u.cross(Vector((0, 0, 1))).normalized()
    bar(hang - across * 0.07, hang + across * 0.07, 0.014, "iron", sides=6)                   # the bar the chains hang from
    ends = [hang + Vector(v) for v in ((-0.01, 0.0, -0.52), (0.05, -0.03, -0.42), (-0.06, -0.02, -0.38), (0.02, 0.05, -0.32),
                                       (-0.04, 0.04, -0.47), (0.03, 0.02, -0.26), (0.06, 0.04, -0.36), (-0.05, -0.05, -0.3))]
    for i, e in enumerate(ends):
        st_ = hang + across * (-0.06 + 0.12 * i / (len(ends) - 1))
        n = 6
        for j in range(n):
            a_, b_ = st_ + (e - st_) * (j / n), st_ + (e - st_) * ((j + 1) / n)
            bar(a_, b_, 0.011 if j % 2 else 0.017, "iron", sides=4)
    bells = []
    for e in ends[6:]:
        bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.04, radius2=0.02, depth=0.06, location=tuple(e - Vector((0, 0, 0.03))))
        add(bpy.context.object, "brass"); bells.append((e - Vector((0, 0, 0.06)), 0.04))   # small bells
    b0 = ends[0]
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=0.085, radius2=0.04, depth=0.13, location=tuple(b0 - Vector((0, 0, 0.07))))
    add(bpy.context.object, "brass"); bells.append((b0 - Vector((0, 0, 0.135)), 0.085))         # the bell
    sphere(tuple(b0 - Vector((0, 0, 0.005))), 0.042, "brass", seg=8, rings=4)
    for e, r in ((ends[1], 0.04), (ends[2], 0.038), (ends[4], 0.032)):                         # spiked balls
        sphere(tuple(e - Vector((0, 0, r))), r, "iron", seg=6, rings=4)
        for v_ in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, -1))):
            bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.01, radius2=0.0, depth=0.03, location=tuple(e - Vector((0, 0, r)) + v_ * (r + 0.012)))
            o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(v_); add(o, "iron")
    skull(ends[3] - Vector((0, 0, 0.04)), 0.035); skull(ends[5] - Vector((0, 0, 0.04)), 0.03)
    sphere(tuple(hang - Vector((0, 0, 0.05))), 0.03, "bone", seg=6, rings=4)                  # a bone charm at the top
    module("Body")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **KIT, **dict(skin="9a7a6c", skind="664a42", skinl="b49484", scar="7a3a30", skirt="434a38", skirtd="272c20",
                    skirtl="5a6248", rag="8a7c60", ragd="5a5040", icon="4a1a12", blade="6e7074", bladel="a0a2a6")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def skin(p, n, ao, grime=0.0):   # pale skin in three hard bands, bruised and dirty
        c = C["skinl"] if n.dot(PSX_LIGHT) > 0.62 else (C["skin"] if n.dot(PSX_LIGHT) > -0.1 else C["skind"])
        if _nz.noise(p * 6 + Vector((3, 1, 2))) > 0.45: c = c.lerp(C["scar"], 0.3)   # bruises
        return lit(c, n, ao, grime=grime, p=p)
    def rag(t, p, n, ao):   # rag wrappings: diagonal turns with ink between
        fr_ = t % 1.0
        return C["ink"] if fr_ < 0.15 else lit(C["ragd"] if fr_ < 0.35 else C["rag"], n, ao, grime=0.3, p=p)
    def skull_paint(p, n, ao):   # a skull's sockets and nose hole (props, not a figure's face)
        for at, r in skulls:
            q = p - at
            if q.length < r * 1.6:
                if q.y < -r * 0.45:
                    for sx in (-1, 1):
                        if math.hypot(q.x - sx * r * 0.38, q.z - r * 0.1) < r * 0.26: return C["ink"]
                    if abs(q.x) < r * 0.1 and -r * 0.45 < q.z < -r * 0.15: return C["ink"]
                    if abs(q.z + r * 0.75) < r * 0.08: return C["ink"]   # the teeth line
                return psx_kit(C, "bone", p, n, ao)
        return None
    def paint(m, p, n, ao, tx, ty):
        base = m.split(".")[0]; x, y, z = p.x, p.y, p.z
        if base == "skirt":   # the rope belt, a grave plaque on the right, the skirt's folds, blood and ragged hem
            if z > 0.995:
                return C["roped"] if ((x + y) * 50 + z * 140) % 1.0 < 0.3 else (C["ink"] if z < 1.0 else lit(C["rope"], n, ao, p=p))
            if x > 0.15 and y > 0.0 and abs(y - 0.08) < 0.045 and 0.77 < z < 0.91:
                if abs(y - 0.08) > 0.039 or z < 0.776 or z > 0.904: return C["ink"]
                if (abs(y - 0.08) < 0.005 and 0.8 < z < 0.88) or (abs(y - 0.08) < 0.02 and abs(z - 0.86) < 0.005): return C["ink"]
                return lit(C["stonel"] if n.dot(PSX_LIGHT) > 0.4 else C["stone"], n, ao, p=p)
            # detail pass (2026-11-10): the knotted rope belt's hanging end, two patches of sacking sewn on, a coarse seam
            # down the back stitched with twine, a dark wet band of mud along the hem
            a_ = math.degrees(math.atan2(x, -(y - yc)))
            if -40 < a_ < -22 and z > 0.78:   # the belt's knotted end hanging down the right front
                ca = -31 + (0.995 - z) * 12
                if abs(a_ - ca) < 2.6:
                    if abs(a_ - ca) > 1.8: return C["ink"]
                    if abs(z - 0.93) < 0.018 or z < 0.8: return C["roped"] if (z * 200) % 1.0 < 0.35 else lit(C["rope"], n, ao, p=p)
                    return C["roped"] if ((z * 3 + a_ * 0.05) * 30) % 1.0 < 0.3 else lit(C["rope"], n, ao, p=p)
            for pa, pz, pw, ph in ((-75, 0.72, 9.0, 0.06), (100, 0.62, 10.0, 0.07)):
                du, dv = (a_ - pa) / pw, (z - pz) / ph
                if abs(du) < 1 and abs(dv) < 1:
                    if abs(du) > 0.86 or abs(dv) > 0.84: return C["ink"]
                    if (abs(du) > 0.7 or abs(dv) > 0.68) and ((du + dv) * 6) % 1.0 < 0.5: return C["roped"]
                    return lit(C["rag"].lerp(C["skirt"], 0.4) if ((du * 5 + dv * 5) % 1.0) > 0.2 else C["ragd"], n, ao, p=p)   # sacking weave
            if abs(abs(a_) - 180) < 1.2 and z < 0.98: return C["ink"]
            if abs(abs(a_) - 180) < 3.2 and z < 0.98 and (z * 70) % 1.0 < 0.3: return C["rope"]
            return psx_robe(C, "skirt", p, n, ao, hem, blood=0.45, step=20.0, cy=yc)
        if base == "bone":
            r_ = skull_paint(p, n, ao)
            return r_ if r_ is not None else psx_kit(C, base, p, n, ao)
        if base == "iron" and min((p - q).length for q in nails) < 0.016:   # the hood cross's nail heads
            dd = min((p - q).length for q in nails)
            return C["ink"] if dd > 0.012 else (C["ironl"] if n.dot(PSX_LIGHT) > 0.2 else C["iron"])
        if base == "brass":   # the bells: an ink rim at the mouth, a dark inside
            for b_, r in bells:
                q = p - b_
                if q.length < r * 1.8 and q.z < r * 0.25:
                    if n.z < -0.3: return C["dark"]
                    return C["ink"] if q.z < r * 0.12 else C["brassl"]
            return psx_kit(C, base, p, n, ao)
        if base == "blade":   # a notched steel blade, a bright edge, blood on it
            b = psx_blood(C, p, 0.2)
            if b is not None: return b
            return lit(C["bladel"] if n.dot(PSX_LIGHT) > 0.5 else C["blade"], n, ao, metal=True, p=p)
        if base == "wood" and (Mpi @ p).length < 0.55 and (Mpi @ p).y > -0.02:   # the plaque: an inset icon, crossed bones, a skull, a strap
            q = Mpi @ p; lx, lz = q.x, q.z
            if q.y > 0.06:
                if lz < -0.03: return C["ink"] if abs(lz + 0.03) < 0.005 else lit(C["leather"], n, ao, p=p)
                inner = abs(lx) < 0.12 and (lz < 0.34 or math.hypot(lx, lz - 0.34) < 0.12)
                edge = abs(lx) < 0.128 and (lz < 0.34 or math.hypot(lx, lz - 0.34) < 0.128)
                if inner and lz > -0.0:
                    sk = math.hypot(lx, lz - 0.32)
                    if sk < 0.045:
                        if sk > 0.039 or (q.z < 0.33 and any(math.hypot(lx - sx * 0.016, lz - 0.325) < 0.011 for sx in (-1, 1))): return C["ink"]
                        return C["bonel"] if lx < 0 else C["bone"]
                    for s_ in (-1, 1):
                        dd, t = segd(Vector((lx, lz, 0)), Vector((-0.08 * s_, 0.08, 0)), Vector((0.08 * s_, 0.26, 0)))
                        if dd < 0.016: return C["ink"] if dd > 0.011 or t < 0.02 or t > 0.98 else C["bone"]
                    return C["icon"] if _nz.noise(p * 40) < 0.4 else C["bloodd"]
                if edge and lz > -0.008: return C["ink"]
                if abs(lx) < 0.135 and abs(lz - 0.02) < 0.03 and abs(abs(lx) - 0.1) < 0.012: return C["brassl"]   # nail heads at the foot
            return psx_kit(C, "wood", p, n, ao)
        s_ = n.dot(PSX_LIGHT)
        # detail pass (2026-11-10): the plaque's iron strips, the prayer strips and their seals, the ankle manacle
        if base == "iron" and abs((Mpi @ p).x) > 0.14 and (Mpi @ p).length < 0.5 and abs((Mpi @ p).y - 0.04) < 0.04:
            q = Mpi @ p
            if abs(((q.z + 0.08) / 0.07) % 1.0 - 0.5) < 0.12 and abs(abs(q.x) - 0.152) < 0.004: return C["ironl"] if s_ > 0 else C["ink"]   # rivets
            if abs(abs(q.x) - 0.152) > 0.0065: return C["ink"]
            return lit(C["rust"] if _nz.noise(p * 35) > 0.4 else (C["ironl"] if s_ > 0.5 else C["iron"]), n, ao, metal=True, p=p)
        if base == "parchment":
            at_, d_, L_ = min(STRIPS, key=lambda s: (p - s[0]).length)
            dz = at_.z - p.z
            if dz > L_ - 0.012 and (p.x * 400) % 1.0 < 0.5: return C["parchmentd"]                     # the torn foot
            if 0.035 < dz < L_ - 0.02 and (dz * 120) % 1.0 < 0.3 and _nz.noise(p * 160) > -0.3: return C["ink"]   # lines of prayer
            return lit(C["parchment"] if s_ > -0.2 else C["parchmentd"], n, ao, grime=0.25, p=p)
        if base == "blood" and any((p - s[0]).length < 0.04 for s in STRIPS):   # the wax seals: a stamped cross
            at_ = min(STRIPS, key=lambda s: (p - s[0]).length)[0]; q = p - (at_ - Vector((0, 0, 0.02)))
            if abs(q.z) < 0.002 or abs(q.x + q.y) < 0.002: return C["bloodd"]
            return C["chargel"] if s_ > 0.4 else C["charge"]
        if base == "iron" and (p - MAN).length < 0.09:   # the manacle: a hinge, a bright worn rim, rust
            t_ = (p - MAN).dot(shin_)
            if abs(t_) > 0.014: return C["ironl"] if t_ > 0 else C["ink"]
            if _nz.noise(p * 50) > 0.3: return lit(C["rust"], n, ao, p=p)
            return lit(C["iron"], n, ao, metal=True, p=p)
        if base == "iron" and WOUND and min(segd(p, a_, b_)[0] for a_, b_ in WOUND) < 0.02:   # the wound chain: each link inked at its ends, a bright top
            d_, t_ = min((segd(p, a_, b_) for a_, b_ in WOUND), key=lambda r: r[0])
            if t_ < 0.08 or t_ > 0.92: return C["ink"]
            return lit(C["ironl"] if s_ > 0.45 else (C["iron"] if s_ > -0.2 else C["irond"]), n, ao, metal=True, p=p) if _nz.noise(p * 40) < 0.45 else lit(C["rust"], n, ao, p=p)
        if base == "iron" and min(segd(p, a_, b_)[0] for a_, b_ in zip(lk, lk[1:])) < 0.015:   # its broken chain
            return C["ink"] if ((p - MAN).length * 110) % 1.0 < 0.25 else lit(C["ironl"] if s_ > 0.3 else C["irond"], n, ao, metal=True, p=p)
        if base == "wood" and segd(p, low, top_)[0] < 0.04:   # the polearm: grain along the haft, leather grips at the fists
            if min((p - FISTS[s_][0]).length for s_ in FISTS) < 0.09: return C["ink"] if ((p - low).dot(pole_u) * 90) % 1.0 < 0.2 else lit(C["leather"], n, ao, p=p)
            g = _nz.noise((p - pole_u * (p - low).dot(pole_u)) * 60 + pole_u * ((p - low).dot(pole_u) * 3))
            return lit(C["woodd"] if g > 0.35 else (C["woodl"] if n.dot(PSX_LIGHT) > 0.5 else C["wood"]), n, ao, p=p)
        if base != "brute": return psx_kit(C, base, p, n, ao) or lit(C["metal"], n, ao, p=p)
        # ---- the bare body ----
        for s_, (fc, fd) in FISTS.items():   # rag-wrapped fists
            if (p - fc).length < 0.075:
                along = (p - fc).dot(fd)
                return rag(along * 45 + (p - fc).dot(Vector((0, 0, 1))) * 20, p, n, ao)
        if z < 0.14:   # bare feet, rags round the ankles
            if z > 0.07:   # (detail pass: broad turns, light and dark, an ink line only between whole turns; thin ones broke into spots)
                fr_ = (z * 26 + x * 8) % 1.0
                return C["ink"] if fr_ < 0.22 else lit(C["ragd"] if fr_ < 0.5 else C["rag"], n, ao, grime=0.4, p=p)
            return skin(p, n, ao, grime=0.6)
        for c_, uu, ss, nn in crosses:   # iron crosses nailed into the shoulders, blood running from the nails
            q = p - c_; u, s, w = q.dot(uu), q.dot(ss), q.dot(nn)
            if abs(w) < 0.05:
                def on(g): return (abs(s) < 0.018 + g and -0.1 - g < u < 0.1 + g) or (abs(u - 0.04) < 0.018 + g and abs(s) < 0.065 + g)
                if math.hypot(s, u - 0.04) < 0.013: return C["ink"] if math.hypot(s, u - 0.04) > 0.009 else C["ironl"]
                if on(0.0): return lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.4 else C["iron"], n, ao, metal=True, p=p)
                if on(0.007): return C["ink"]
                if on(0.016): return C["bloodd"]
                for dx in (-0.03, 0.008, 0.035):   # runs of blood down from it
                    if abs(s - dx - 0.004 * math.sin(u * 90)) < 0.007 and -0.25 < u < -0.05 - abs(dx) * 2: return C["blood"]
        side_, along, ad = psx_arm(p); s_ = n.dot(PSX_LIGHT)   # (the fist loop above reuses s_)
        if side_ == "L" and ad < 0.1 and 1.25 < along < 1.85 and z > 0.85:   # the riveted iron vambrace on the left forearm
            for b_ in (1.32, 1.76):
                if abs(along - b_) < 0.05: return C["ink"] if abs(along - b_) > 0.035 else lit(C["metall"] if n.dot(PSX_LIGHT) > 0.4 else C["metal"], n, ao, metal=True, p=p)
            if abs(along - 1.54) < 0.02 and (math.atan2(p.y - JOINTS["wrist.L"].y, p.z - JOINTS["wrist.L"].z) * 3) % 1.0 < 0.35: return C["brassl"]
            return psx_kit(C, "iron", p, n, ao)
        if z > 0.96 and y > 0.0 and not (ad < 0.1 and along > 0.3):   # lash scars across the back
            if (math.sin((x * 0.8 + z) * 70 + _nz.noise(p * 8) * 2) > 0.93 and _nz.noise(p * 5) > -0.1): return C["scar"] if _nz.noise(p * 50) > 0 else C["blood"]
        if y < 0.0 and 1.0 < z < 1.15 and abs(x - 0.03 - (z - 1.0) * 0.4) < 0.006:   # a stitched wound across the belly
            return C["ink"] if (z * 220) % 1.0 < 0.4 else C["scar"]
        if y < 0.0 and 1.0 < z < 1.15 and abs(x - 0.03 - (z - 1.0) * 0.4) < 0.016 and (z * 110) % 1.0 < 0.18: return C["ink"]   # its stitches
        # detail pass (2026-11-10): a penitent's chain wound from the right shoulder to the left hip, front and back,
        # chafing the skin raw; a cross branded on the left breast; a rope bound round the right upper arm; the blood
        # in coherent blots and runs (no single-texel spray); mud caked up the shins
        on_arm = ad < 0.1 and along > 0.25
        if not on_arm and z > 1.0:
            cd = (x + 0.2) - (1.5 - z) * 0.75   # the chain's line, across the torso
            if abs(cd) < 0.04:   # the skin chafed raw under the (solid) chain, a scab line either side
                if abs(cd) > 0.032: return C["scar"]
                return lit(C["skind"].lerp(C["scar"], 0.55), n, ao, p=p) if abs(cd) > 0.018 else lit(C["scar"].lerp(C["bloodd"], 0.4), n, ao, p=p)
        if y < 0 and not on_arm:   # the brand: a cross of scar tissue, a dark burnt rim
            bx, bz = x - 0.13, z - 1.33
            if (abs(bx) < 0.008 and abs(bz) < 0.04) or (abs(bx) < 0.026 and abs(bz - 0.012) < 0.008): return C["scar"].lerp(C["skinl"], 0.35)
            if (abs(bx) < 0.013 and abs(bz) < 0.045) or (abs(bx) < 0.031 and abs(bz - 0.012) < 0.013): return C["bloodd"]
        if side_ == "R" and ad < 0.1 and abs(along - 0.55) < 0.06: return rag(along * 60 + z * 10, p, n, ao)
        if z < 0.3 and _nz.noise(p * Vector((14, 14, 8)) + Vector((2, 7, 1))) > 0.55 - (0.3 - z) * 3.5: return lit(C["mud"], n, ao, p=p)
        dense = 0.05 + 0.25 * max(0.0, 1.2 - z)
        if y < 0.05:
            if _nz.noise(p * 12) > 0.62 - dense: return C["bloodd"] if _nz.noise(p * 30) > 0.35 else C["blood"]
            if _nz.noise(Vector((p.x * 30, p.y * 30, 0))) > 0.62 and _nz.noise(Vector((p.x * 12, p.y * 12, p.z * 3.5))) > 0.45 - dense: return C["blood"]
        return skin(p, n, ao, grime=0.5 * max(0.0, (0.5 - z) / 0.4))
    PAINT = paint

def psx_shell(c, d, r, L, m, ang=105, rows=3, taper=0.88, n=7, thick=0.012):
    # a plain curved plate over the top of an arm along d (radius r, length L), as the Combat Engineer's pauldrons; returns
    # (object, frame): frame(p) -> (t 0..1 along it, angle in degrees from its top) for painting its edges and rivets
    import bmesh as _bm
    d = d.normalized(); side = d.cross(Vector((0, 0, 1))).normalized(); up = side.cross(d).normalized()
    vs = [tuple(c + d * (L * j / rows) + (side * math.sin(math.radians(-ang + 2 * ang * i / (n - 1))) + up * math.cos(math.radians(-ang + 2 * ang * i / (n - 1)))) * (r * (1 - (1 - taper) * j / rows))) for j in range(rows + 1) for i in range(n)]
    fs = [(j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i) for j in range(rows) for i in range(n - 1)]
    me = bpy.data.meshes.new("shell"); me.from_pydata(vs, [], fs)
    bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces)
    f0 = bm.faces[0]
    if f0.normal.dot(f0.calc_center_median() - c) < 0: _bm.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    for p_ in me.polygons: p_.use_smooth = True
    o = bpy.data.objects.new("shell", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = thick
    add(o, m)
    def frame(p):
        q = p - c; t = q.dot(d) / L; return t, math.degrees(math.atan2(q.dot(side), q.dot(up)))
    return o, frame
def hose(pts, r, m="rubber", sides=6):   # a flexible pipe through pts, smoothed (Catmull-Rom), as short bars
    pts = [Vector(p) for p in pts]; out = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(3):
            t = k / 3
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    for a_, b_ in zip(out, out[1:]): bar(a_, b_, r, m, sides=sides)
    return out

def mechanized_heavy_infantry():
    # Mechanized Heavy Infantry (2026-11-10, seventh Principality figure, class C, 40 mm): an original armoured trench
    # trooper, WWI trench-fantasy, not a copy of any miniature or artwork. A hulking man sealed in riveted plate, braced
    # wide, a water-cooled heavy machine gun held low at the hip in both gauntlets; a squat riveted bucket helm sunk into
    # a high gorget (no head under it), a rubber breathing hose from its chin over the right shoulder to the engine; a
    # back-mounted engine block with a boiler tank and two exhaust stacks rising behind the helm, a second hose down to
    # the gun's water jacket; huge layered pauldrons (their top plates in the seat colour) and solid knee cops. Painted
    # PSX look: the smooth bulked body painted texel by texel (breastplate panels with rivets, rust runs and a faded
    # stencilled cross, abdomen lames, plated arms with couters and vambraces, riveted gauntlets, tassets, greaves,
    # sabatons, mail at the joints, grime); the helm's slit and grille, the engine's panels, louvres, rivets and a little
    # hazard striping, the hoses' ribs and the gun's jacket painted on their solid shapes. Modules: Helmet (helm,
    # gorget), Chest (pauldrons, knee cops), Back (engine, tank, stacks, hoses), Weapon (machine gun), Accent (pauldron
    # tops).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1750
    TEX_SIZES = {"Body": 512, "Back": 256, "Chest": 256, "Helmet": 256, "Weapon": 256}   # class C: a 512 px body, 256 px kit (detail pass)
    from mathutils import noise as _nz
    import bmesh as _bm
    def pose(r):
        human.aim(r, "spine03", P(0, -0.07, 1.4))                                              # braced, a little forward
        human.aim(r, "upperleg01.L", P(0.15, -0.08, 0.5)); human.aim(r, "upperleg02.L", P(0.15, -0.08, 0.5))
        human.aim(r, "lowerleg01.L", P(0.17, -0.09, 0.08)); human.aim(r, "lowerleg02.L", P(0.17, -0.09, 0.08))
        human.aim(r, "foot.L", P(0.2, -0.25, -0.01))
        human.aim(r, "upperleg01.R", P(-0.14, 0.06, 0.5)); human.aim(r, "upperleg02.R", P(-0.14, 0.06, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.16, 0.1, 0.08)); human.aim(r, "lowerleg02.R", P(-0.16, 0.1, 0.08))
        human.aim(r, "foot.R", P(-0.2, -0.05, -0.02))
        human.reach(r, "R", P(-0.25, 0.04, 1.02), P(-0.14, -0.14, 0.93))                     # the right fist on the rear grip at the hip
        human.reach(r, "L", P(0.22, -0.16, 1.05), P(0.07, -0.36, 1.0))                       # the left on the forward handle
    hands, hp, top = human_body(pose, coat=False, head=None, mitts="leather", shape="male-young-maxmuscle-maxweight", tris=1500,
                                dress={"foot": "boots", "shin": "plate", "thigh": "plate", "torso": "plate"})
    human_ = bpy.data.objects["human"]
    # the armour's bulk: chest and shoulders broadened (across only, so the fists' grip stays put), the limbs thickened
    def broad(v): return Vector((v.x * (1 + 0.2 * min(1.0, max(0.0, (v.z - 0.95) / 0.4))), v.y, v.z))
    for v in human_.data.vertices:
        v.co = broad(v.co)
    for v in human_.data.vertices:
        if 0.1 < v.co.z < 1.46: v.co += v.normal * (0.022 if v.co.z > 0.95 else 0.018)
    for o in MODULES["Body"]:
        if o is not human_: o.location = broad(o.location)
    for s_ in ("L", "R"): FISTS[s_] = (broad(FISTS[s_][0]), FISTS[s_][1])
    for k_ in JOINTS: JOINTS[k_] = broad(JOINTS[k_])
    human_.data.update(); bpy.context.view_layer.update()
    toeless_feet()
    human_.data.update(); bpy.context.view_layer.update()
    def hit(o, d_):
        ok, loc, nrm, _ = human_.ray_cast(Vector(o), Vector(d_)); return (loc, nrm) if ok else (None, None)
    nk = NECK.copy()
    module("Helmet")
    # the helm: a squat riveted bucket, a little narrower at the top, a low dome, sunk into a high gorget; nothing under it
    hc = Vector((hp.x, hp.y - 0.01, hp.z + 0.055))                                            # its centre, low on the neck
    hz0, hz1 = hc.z - 0.07, hc.z + 0.11
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.128, radius2=0.118, depth=hz1 - hz0, end_fill_type='NOTHING', location=(hc.x, hc.y, (hz0 + hz1) / 2))
    o = bpy.context.object; o.scale = (1, 1.12, 1); add(o, "iron")
    sphere((hc.x, hc.y, hz1), 0.118, "iron", (1, 1.12, 0.42), seg=12, rings=5)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.13, minor_radius=0.012, major_segments=12, minor_segments=4, location=(hc.x, hc.y, hc.z + 0.035))
    bpy.context.object.scale = (1, 1.12, 1); add(bpy.context.object, "iron")                 # the brow band over the slit
    box((hc.x, hc.y - 0.142, hc.z - 0.03), (0.016, 0.03, 0.12), "iron")                      # the ridge down the face plate
    resp = Vector((hc.x, hc.y - 0.15, hc.z - 0.06))
    bar(resp - Vector((0, -0.02, 0)), resp + Vector((0, -0.03, -0.015)), 0.03, "brass", sides=8)   # the respirator boss on the chin
    # the gorget: a high collar of plate from the shoulders up round the helm's lower edge
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.2, radius2=0.145, depth=0.19, end_fill_type='NOTHING', location=(nk.x, nk.y + 0.0, nk.z + 0.03))
    o = bpy.context.object; o.scale = (1, 0.9, 1); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(o, "iron")
    module("Chest")
    shells = []
    for s, x in (("L", 1), ("R", -1)):   # huge pauldrons: three stacked shells, the top one in the seat colour
        sh_ = JOINTS["upperarm01." + s] + Vector((0.035 * x, 0, 0.07)); ad = (JOINTS["lowerarm01." + s] - JOINTS["upperarm01." + s]).normalized()
        ad = (ad + Vector((0.6 * x, 0, 0))).normalized()                                     # out over the shoulder, not down the arm
        for k, (r_, L_, dz, m_) in enumerate(((0.185, 0.11, 0.0, "accent"), (0.168, 0.17, -0.01, "engplate"), (0.16, 0.23, -0.03, "engplate"))):
            o, fr_ = psx_shell(sh_ + Vector((0.0, 0, dz)) - ad * 0.07, ad, r_, L_, m_, ang=104 + 4 * k, n=9, rows=3, thick=0.016)
            if m_ != "accent": shells.append((fr_, s, k))
    cops = []
    for s, x in (("L", 1), ("R", -1)):   # knee cops: a domed plate over each knee, a fan of wings at the sides
        kj = JOINTS["lowerleg01." + s]
        loc, nrm = hit((kj.x, -1.0, kj.z + 0.01), (0, 1, 0))
        if loc is None: continue
        c_ = loc + Vector((0, -0.012, 0))
        sphere(tuple(c_), 1, "engplate", (0.075, 0.045, 0.085), seg=10, rings=6)
        sphere(tuple(c_ + Vector((0.06 * x, 0.03, 0))), 1, "engplate", (0.03, 0.05, 0.06), seg=6, rings=4)   # the outer wing
        cops.append(c_)
    module("Back")
    # the engine: a block on the back, the boiler tank across its top, two exhaust stacks rising behind the helm
    bk, _ = hit((0, 1.0, 1.22), (0, -1, 0)); bk = bk if bk is not None else Vector((0, 0.16, 1.22))
    eb = Vector((0, bk.y + 0.125, 1.2)); EW, ED, EH = 0.34, 0.22, 0.34
    box(tuple(eb), (EW, ED, EH), "engarmour")
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.085, depth=0.4, location=(0, eb.y + 0.01, eb.z + EH / 2 + 0.07), rotation=(0, math.pi / 2, 0))
    tank = bpy.context.object; add(tank, "rust")
    for x in (-1, 1):
        sphere((x * 0.2, eb.y + 0.01, eb.z + EH / 2 + 0.07), 0.085, "rust", (0.35, 1, 1), seg=10, rings=5)   # its domed ends
    stacks = []
    for x in (-0.11, 0.11):
        s0, s1 = Vector((x, eb.y + 0.07, eb.z + 0.05)), Vector((x * 1.25, eb.y + 0.09, top.z + 0.08))
        bar(s0, s1, 0.032, "metal", sides=8); bar(s1, s1 + Vector((0, 0, 0.05)), 0.042, "metal", sides=8)   # the stack and its cowl
        stacks.append(s1)
    # the breathing hose: the engine, over the right shoulder, to the respirator
    H1 = hose([eb + Vector((-0.12, -0.02, 0.12)), Vector((-0.15, bk.y + 0.02, 1.5)), Vector((-0.17, -0.04, 1.5)), resp + Vector((-0.07, -0.02, -0.04)), resp + Vector((-0.01, -0.02, -0.01))], 0.02)
    module("Weapon")
    # the heavy machine gun held low: spade grips by the right fist, the receiver, a fat water jacket with a muzzle, the
    # ammunition box on its left, a carry handle under the left fist
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    u = (hl - hr); u = Vector((u.x * 0.45, u.y, 0)).normalized()                             # forward, nearly level, a little across the body so it reads from the front
    side = u.cross(Vector((0, 0, 1))).normalized(); dn = Vector((0, 0, -1))
    rz_ = math.atan2(u.y, u.x) - math.pi / 2
    g0 = hl + dn * 0.08                                                                        # the gun's axis, under the handle
    rec0, rec1 = g0 - u * 0.28, g0 + u * 0.07
    box(tuple((rec0 + rec1) / 2), (0.12, (rec1 - rec0).length, 0.13), "metal", rot=(0, 0, rz_))
    rcv = bpy.context.object
    box(tuple((rec0 + rec1) / 2 - dn * 0.075 - u * 0.03), (0.07, 0.14, 0.025), "metal", rot=(0, 0, rz_))   # the feed cover on top
    JL = 0.6
    bar(rec1, rec1 + u * JL, 0.074, "metal", sides=10)                                          # the water jacket
    for t_ in (0.02, JL - 0.02): bar(rec1 + u * (t_ - 0.015), rec1 + u * (t_ + 0.015), 0.082, "iron", sides=10)   # its end collars
    bar(rec1 + u * JL, rec1 + u * (JL + 0.08), 0.026, "metal", sides=6); bar(rec1 + u * (JL + 0.08), rec1 + u * (JL + 0.17), 0.046, "iron", sides=8)   # barrel and the muzzle booster
    bar(rec1 + u * (JL - 0.06) - dn * 0.07, rec1 + u * (JL - 0.06) - dn * 0.115, 0.009, "iron", sides=4)   # the foresight blade
    bar(rec0 + u * 0.05 - dn * 0.065, rec0 + u * 0.05 - dn * 0.11, 0.012, "iron", sides=4)                 # the rear sight leaf
    bar(hr, rec0, 0.018, "wood", sides=5)                                                      # the spade grip's arm to the right fist
    bar(hr + dn * 0.045, hr - dn * 0.045, 0.02, "wood", sides=5)                               # the grip
    bar(hl, g0, 0.013, "metal", sides=4)                                                       # the carry handle
    ab = g0 - u * 0.12 + side * 0.11 + dn * 0.05                                                # the ammunition box hung on the gun's left
    box(tuple(ab), (0.08, 0.17, 0.13), "engarmour", rot=(0, 0, rz_))
    box(tuple(ab + Vector((0, 0, 0.068))), (0.086, 0.176, 0.012), "engarmour", rot=(0, 0, rz_))  # its lid
    bar(ab + Vector((0, 0, 0.074)) - u * 0.04, ab + Vector((0, 0, 0.074)) + u * 0.04, 0.007, "metal", sides=4)   # the lid's handle
    # the ammunition belt: out of the box top, sagging in a loop and up into the feed (cartridges painted on it)
    belt_ = hose([ab + Vector((0, 0, 0.07)), ab + Vector((0, 0, 0.13)) - side * 0.025, g0 - u * 0.14 + side * 0.075 - dn * 0.1, g0 - u * 0.14 + side * 0.055 - dn * 0.05], 0.014, "brass", sides=4)
    jc = rec1 + u * 0.04 - Vector((0, 0, 0.065))                                                # the cooling hose's union, under the jacket's rear
    module("Back")
    # the cooling hose: from the jacket's rear underside back round the left hip (the gun's side) close over the belt,
    # then up the back into the engine's foot (in the Back node, with the engine)
    hp_ = []; wc_ = Vector((0, -0.06, 0))
    for a_, z_ in ((70, 0.975), (100, 0.985), (135, 1.0), (160, 1.03)):   # angles round the waist (0 = front, 90 = the left side)
        d_ = Vector((math.sin(math.radians(a_)), -math.cos(math.radians(a_)), 0))
        loc_, _ = hit(wc_ + Vector((0, 0, z_)) + d_ * 0.8, -d_)
        if loc_ is not None: hp_.append(loc_ + d_ * 0.03)
    H2 = hose([jc + Vector((0, 0, 0.02)), jc - u * 0.1 + dn * 0.02] + hp_ + [eb + Vector((0.12, -0.04, -EH / 2 - 0.015)), eb + Vector((0.12, -0.02, -EH / 2 + 0.01))], 0.018)
    hend = [resp, jc, eb + Vector((-0.12, -0.02, 0.12)), eb + Vector((0.12, -0.02, -EH / 2))]
    def along_hose(p):   # (distance to the nearer hose's axis, distance along it)
        best = (9.0, 0.0)
        for H in (H1, H2):
            s0 = 0.0
            for a_, b_ in zip(H, H[1:]):
                d_, t_ = segd(p, a_, b_)
                if d_ < best[0]: best = (d_, s0 + t_ * (b_ - a_).length)
                s0 += (b_ - a_).length
        return best
    # the engine's pressure gauge and a feed pipe from the boiler to the block
    gz = Vector((0.0, eb.y + ED / 2 + 0.012, eb.z + EH / 2 - 0.05))
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.036, depth=0.02, location=tuple(gz), rotation=(math.pi / 2, 0, 0)); add(bpy.context.object, "brass")
    bar(Vector((0.17, eb.y + 0.03, eb.z + EH / 2 + 0.05)), Vector((0.19, eb.y + 0.06, eb.z + EH / 2 - 0.12)), 0.014, "iron", sides=5)
    module("Body")
    # the body hidden behind the engine and inside the gorget is never seen: removed
    bm = _bm.new(); bm.from_mesh(human_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if (c := f.calc_center_median()).y > bk.y - 0.03 and abs(c.x) < EW / 2 - 0.03 and eb.z - EH / 2 + 0.03 < c.z < eb.z + EH / 2 - 0.02
                                                 or (c - nk).length < 0.11 and c.z > nk.z - 0.02], context='FACES')
    bm.to_mesh(human_.data); bm.free()
    print(f"MHI neck={nk} hc={hc} eb={eb} cops={cops} fists={[FISTS[s][0] for s in FISTS]}")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **KIT, **dict(plate="474b42", plated="282b24", platel="686d60", engplate="474b42", engplated="282b24", engplatel="686d60",
                    eng="4a4c40", engd="2a2c24", engl="6a6c5c", hazard="8a7230", hazardd="5a4a1e", rubber="25231f", rubberl="3e3a34",
                    mail="3a3c3e", maill="5e6064", stencil="a89c80", steam="8e8a80")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def rusty(c, p, k=0.5):   # rust running down from the seams and rivets
        if _nz.noise(Vector((p.x * 14, p.y * 14, p.z * 2.0))) > 0.66 and _nz.noise(p * 5) > 0.1: return c.lerp(C["rust"], k * 0.8)   # fewer, larger rust runs (tidier pixels)
        return c
    # coherent plate shading (detail pass, 2026-11-10): the light bands come from a smoothed normal (the radial direction
    # from the limb's or torso's axis, a little of the real one), so the bulky body's muscle lumps no longer break each
    # plate into mottled patches
    ARMS = [(JOINTS[f"upperarm01.{s}"], JOINTS[f"lowerarm01.{s}"]) for s in ("L", "R")] + [(JOINTS[f"lowerarm01.{s}"], FISTS[s][0] - FISTS[s][1] * 0.045) for s in ("L", "R")]
    LEGS = [(JOINTS[f"upperleg01.{s}"], JOINTS[f"lowerleg01.{s}"]) for s in ("L", "R")] + [(JOINTS[f"lowerleg01.{s}"], JOINTS[f"foot.{s}"]) for s in ("L", "R")]
    yf_, _ = hit((0, -1.0, 1.25), (0, 1, 0)); yc_ = ((yf_.y if yf_ is not None else -0.17) + bk.y) / 2; ry_ = max(0.08, (bk.y - yc_))
    def bn(p, n, kind):
        if kind == "torso": r = Vector((p.x / 0.26, (p.y - yc_) / ry_, 0))
        else:
            best = None
            for a_, b_ in (ARMS if kind == "arm" else LEGS):
                d_, t_ = segd(p, a_, b_)
                if best is None or d_ < best[0]: best = (d_, a_, b_, t_)
            _, a_, b_, t_ = best; ax_ = (b_ - a_).normalized(); r = p - (a_ + (b_ - a_) * t_); r -= ax_ * r.dot(ax_)
        if r.length < 1e-6: return n
        nz_ = n.z * 0.6; r.normalize(); r = r * math.sqrt(max(0.0, 1 - nz_ * nz_)) + Vector((0, 0, nz_))
        return (r * 0.85 + n * 0.15).normalized()
    def pl(p, n, ao, edge=None, light=False, kind=None, grime=None):   # gunmetal plate in three bands, rust runs, grime low down
        nb = bn(p, n, kind) if kind else n; s_ = nb.dot(PSX_LIGHT)
        if kind: ao = 1.0 if ao > 0.55 else (0.8 if ao > 0.3 else 0.6)   # only deep creases darken: the body's lumps would mottle the plate
        c = C["platel"] if (light or s_ > 0.72) else (C["plate"] if s_ > -0.3 else C["plated"])
        g_ = grime if grime is not None else 0.12 * max(0.0, (0.5 - p.z) / 0.4)
        return lit(rusty(c, p), nb, ao, metal=True, grime=g_, p=p)
    def mail(p, n, ao):   # mail in scalloped rows (a texel or more wide, no fine noise), each row lit along its top
        r_ = (p.z * 45 + 0.3 * abs(math.sin(math.atan2(p.x, -p.y) * 16))) % 1.0
        if r_ < 0.3: return C["ink"]
        return lit(C["maill"] if r_ > 0.7 and n.dot(PSX_LIGHT) > -0.2 else C["mail"], n, ao, metal=True, p=p)
    def ring_dot(a, qz, angs, z0, mpd, r):   # a rivet round a ring: a (degrees) and qz near one of angs at height z0; mpd metres per degree
        dd = math.hypot(min(abs(math.remainder(a - g_, 360)) for g_ in angs) * mpd, qz - z0)
        if dd < r: return C["brassl"] if qz > z0 + 0.001 else C["brass"]
        return C["ink"] if dd < r + 0.004 else None
    def hazard(p, k=1.0):   # diagonal stripes, worn
        return C["ink"] if ((p.x + p.z) * 22 * k) % 1.0 < 0.5 else (C["hazardd"] if _nz.noise(p * 40) > 0.3 else C["hazard"])
    def paint(m, p, n, ao, tx, ty):
        base = m.split(".")[0]; x, y, z = p.x, p.y, p.z
        if base == "accent": return None
        if base == "iron" and (p - hc).length < 0.2 and z > hz0 - 0.01:   # the helm: the slit, a grille, rivets round the band
            q = p - hc; a = math.degrees(math.atan2(q.x, -q.y))
            if abs(a) < 52 and abs(q.z - 0.012) < 0.011 and n.z > -0.5: return C["ink"]                    # the vision slit
            if abs(a) < 36 and -0.105 < q.z < -0.035 and abs(q.x) > 0.02:                                     # the breathing grille: four wide slots
                return C["ink"] if (q.z * 55) % 1.0 < 0.5 else lit(C["ironl"] if (q.z * 55) % 1.0 > 0.8 else C["irond"], n, ao, metal=True, p=p)
            if abs(a) < 40 and -0.115 < q.z < -0.025 and abs(q.x) > 0.012: return C["ink"]                 # the grille's frame
            if abs(q.z - 0.035) < 0.02:   # the brow band: brass rivets every 30 degrees, a lit upper edge
                b_ = ring_dot(a, q.z, range(-150, 181, 30), 0.035, 0.0026, 0.008)
                if b_ is not None: return b_
                return lit(C["ironl"] if q.z > 0.04 and n.dot(PSX_LIGHT) > 0.0 else C["iron"], n, ao, metal=True, p=p)
            if abs(q.z - 0.11) < 0.006: return C["ink"]                                                    # the crown's seam to the dome
            if q.z > 0.11:   # the dome: a lit cap, four riveted strap lines over it
                if abs(math.sin(math.radians(a * 2))) < 0.07 * (1 + (q.z - 0.11) * 12): return C["platel"] if math.sin(math.radians(a)) < 0.3 else C["plate"]
                return pl(p, n, ao, light=q.z > 0.15 and n.dot(PSX_LIGHT) > 0.3)
            if abs(q.z + 0.065) < 0.006: return C["ink"]                                                   # the lower rim
            if abs(a) > 60 and abs(q.z + 0.02) < 0.012: return C["ink"] if abs(q.z + 0.02) < 0.006 else lit(C["platel"], n, ao, metal=True, p=p)   # a side seam with its lit bevel
            if abs(a) > 60:
                b_ = ring_dot(a, q.z, (-150, -120, -90, -70, 70, 90, 120, 150, 180), -0.045, 0.0023, 0.007)
                if b_ is not None: return b_
            return pl(p, n, ao)
        if base == "iron" and z > nk.z - 0.08 and (p - nk).length < 0.25:   # the gorget: three lames, rivets, scuffed edges
            q = z - (nk.z - 0.065); f_ = (q * 16) % 1.0
            if f_ < 0.2: return C["ink"]
            if f_ < 0.36 and ((math.atan2(x - nk.x, y - nk.y) * 3) % 1.0) < 0.22: return C["brassl"] if f_ > 0.28 else C["brass"]
            return pl(p, n, ao, light=f_ < 0.4 and n.dot(PSX_LIGHT) > -0.2)
        if base == "engplate":
            for c_ in cops:   # a knee cop: an ink rim, a rivet, a light top
                q = p - c_
                if q.length < 0.13:
                    if abs(q.z) > 0.075 or abs(q.x) > 0.085 and q.y < 0.02: return C["ink"]
                    b_ = psx_dot(p, [c_ + Vector((dx_, q.y, 0.045)) for dx_ in (-0.03, 0.0, 0.03)], C, 0.008)
                    if b_ is not None: return b_
                    if abs(q.z + 0.015) < 0.007: return C["ink"]
                    if -0.008 < q.z + 0.015 < 0.016: return lit(C["platel"], n, ao, metal=True, p=p)   # the lit bevel under the crease
                    return pl(p, n, ao, light=q.z > 0.04 and n.dot(PSX_LIGHT) > 0.2, grime=0.3 if q.z < -0.03 else 0.1)
            for fr_, s_, k_ in shells:   # a pauldron lame: a rolled rim, rivets along the lower edge, a stencilled chevron on the left
                t, a = fr_(p)
                if -0.05 < t < 1.05 and abs(a) < 125:
                    if t > 0.9 or abs(a) > 102: return C["ink"] if t > 0.96 or abs(a) > 108 else lit(C["platel"], n, ao, metal=True, p=p)
                    if abs(t - 0.78) < 0.06:
                        da_ = abs(math.remainder(a, 18.0)) * 0.003
                        if math.hypot(da_, (t - 0.78) * 0.2) < 0.007: return C["brassl"] if t < 0.78 else C["brass"]
                        if math.hypot(da_, (t - 0.78) * 0.2) < 0.011: return C["ink"]
                    if s_ == "L" and k_ == 2 and abs(abs(a) * 0.004 - (t - 0.2) * 0.25) < 0.012 and 0.2 < t < 0.62 and abs(a) < 40: return lit(C["stencil"], n, ao, p=p)
                    if t < 0.12: return lit(C["plated"], n, ao, metal=True, p=p)   # in the shadow of the lame above
                    return pl(p, n, ao, light=t > 0.8 and n.dot(PSX_LIGHT) > -0.1)
            return pl(p, n, ao)
        if base == "engarmour" and (p - eb).length < 0.4:   # the engine block: panels, louvres, rivets, hazard stripes along its foot
            q = p - eb
            if abs(q.z + EH / 2) < 0.04 and n.z > -0.5: return hazard(p)
            if n.y > 0.6:   # the back face: an access panel with a louvre and four bolts
                if abs(q.x) > EW / 2 - 0.012 or abs(q.z) > EH / 2 - 0.012: return C["ink"]
                b = psx_dot(p, [eb + Vector((sx * 0.12, q.y, sz * 0.11)) for sx in (-1, 1) for sz in (-1, 1)], C, 0.008)
                if b is not None: return b
                if abs(q.x) < 0.09 and -0.06 < q.z < 0.07: return C["ink"] if (q.z * 70) % 1.0 < 0.4 else lit(C["engl"], n, ao, metal=True, p=p)
                if abs(abs(q.x) - 0.1) < 0.003 or abs(abs(q.z) - 0.09) < 0.003: return C["engd"]
            if abs(n.x) > 0.6:   # the sides: a riveted frame, vents, a stencilled number plate
                if abs(q.y) > ED / 2 - 0.01 or q.z > EH / 2 - 0.01: return C["ink"]
                if abs(q.y) > ED / 2 - 0.02 or q.z > EH / 2 - 0.02: return lit(C["engl"], n, ao, metal=True, p=p)   # the lit bevelled edge
                b = psx_dot(p, [Vector((x, eb.y + sy * 0.08, eb.z + sz * 0.13)) for sy in (-1, 0, 1) for sz in (-1, 1)], C, 0.007)
                if b is not None: return b
                if -0.07 < q.z < 0.1 and abs(q.y) < 0.07: return C["ink"] if (q.z * 50) % 1.0 < 0.45 else (C["engl"] if (q.z * 50) % 1.0 > 0.8 else C["engd"])   # vents
            if n.z > 0.6 and abs(abs(q.x) - EW / 2) < 0.012: return lit(C["engl"], n, ao, metal=True, p=p)   # the lit top edges
            if n.y > 0.6 and abs(q.x) < 0.09 and abs(q.z + 0.11) < 0.022:   # a pale stencilled plate under the louvre
                if abs(q.z + 0.11) > 0.016 or abs(q.x) > 0.084: return C["ink"]
                return C["ink"] if (abs((q.x + 0.06) * 100) % 3.0 < 0.9 and abs(q.z + 0.11) < 0.008) else lit(C["stencil"], n, ao, p=p)
            c = C["engl"] if n.dot(PSX_LIGHT) > 0.6 else (C["eng"] if n.dot(PSX_LIGHT) > -0.2 else C["engd"])
            return lit(rusty(c, p, 0.4), n, ao, metal=True, grime=0.25 if q.z < -EH / 2 + 0.08 else 0.0, p=p)
        if base == "engarmour":   # the ammunition box: a lid seam, riveted corners, a latch, a stencilled band and cross
            q = p - ab; qu, qs = q.dot(u), q.dot(side)
            if q.z > 0.062: return lit(C["engl"] if n.z > 0.6 else C["eng"], n, ao, metal=True, p=p)   # the lid
            if abs(q.z - 0.058) < 0.006: return C["ink"]
            e_ = abs(qs) if abs(n.dot(u)) > 0.6 else abs(qu); em_ = 0.04 if abs(n.dot(u)) > 0.6 else 0.085   # the face's own width
            if e_ > em_ - 0.01 or q.z < -0.055: return C["ink"] if (e_ > em_ - 0.004 or q.z < -0.06) else lit(C["engl"], n, ao, metal=True, p=p)   # framed, a lit bevel
            if abs(qs) < 0.03 and abs(qu) < 0.012 and abs(q.z - 0.04) < 0.016: return C["ink"] if abs(q.z - 0.04) > 0.01 else C["brassl"]   # the latch
            if abs(q.z + 0.005) < 0.012: return lit(C["stencil"], n, ao, p=p)   # a stencilled band
            if abs(q.z + 0.005) < 0.018: return C["ink"]
            return lit(C["eng"] if n.dot(PSX_LIGHT) > -0.2 else C["engd"], n, ao, metal=True, grime=0.2 if q.z < -0.03 else 0.0, p=p)
        if base == "rust":   # the boiler: riveted bands, rust, a pale gauge dial
            bx = abs(x)
            if bx > 0.14: return lit(C["engd"] if n.dot(PSX_LIGHT) < 0.3 else C["eng"], n, ao, metal=True, p=p)
            if abs(bx - 0.13) < 0.012 or abs(bx - 0.05) < 0.01: return C["ink"] if abs(bx - 0.13) > 0.008 and abs(bx - 0.05) > 0.006 else lit(C["irond"], n, ao, metal=True, p=p)
            if n.y > 0.4 and math.hypot(x - 0.09, z - (eb.z + EH / 2 + 0.07)) < 0.025:
                dd = math.hypot(x - 0.09, z - (eb.z + EH / 2 + 0.07))
                return C["ink"] if dd > 0.02 or abs(x - 0.09 - (z - eb.z - EH / 2 - 0.07) * 0.5) < 0.003 and dd < 0.016 else C["stencil"]
            if _nz.noise(p * 22) > 0.3: return lit(C["rust"] if n.dot(PSX_LIGHT) > 0.3 else C["rustd"], n, ao, p=p)
            return lit(C["engl"] if n.dot(PSX_LIGHT) > 0.62 else (C["eng"] if n.dot(PSX_LIGHT) > -0.2 else C["engd"]), n, ao, metal=True, p=p)
        if base == "metal" and min((p - s_).length for s_ in stacks) < 0.12 and z > eb.z:   # the stacks: sooty mouths, heat-blued bands
            if n.z > 0.5: return C["ink"]
            if min(abs(z - s_.z) for s_ in stacks) < 0.012: return C["ink"]
            return lit(C["dark"].lerp(C["metal"], 0.5 + 0.5 * min(1.0, (top.z - z) / 0.3)), n, ao, metal=True, p=p)
        if base == "metal":   # the gun: a fluted water jacket with a filler cap, a riveted receiver with a brass maker's plate
            t = (p - rec1).dot(u); s_ = n.dot(PSX_LIGHT)
            if 0.0 < t < JL and segd(p, rec1, rec1 + u * JL)[0] < 0.085:
                f_ = (t * 20) % 1.0
                if f_ < 0.22: return C["ink"]
                if math.hypot(t - 0.12, (p - rec1).dot(side)) < 0.022 and n.z > 0.5: return C["brassl"] if n.z > 0.85 else C["brass"]   # the filler cap
                return lit(C["metall"] if (s_ > 0.5 or f_ > 0.78 and s_ > -0.1) else (C["metal"] if s_ > -0.2 else C["irond"]), n, ao, metal=True, p=p)
            if -0.3 < t < 0.01 and segd(p, rec0, rec1)[0] < 0.11:
                q = p - (rec0 + rec1) / 2; qu, qs = q.dot(u), q.dot(side)
                if abs(q.z) > 0.058 and n.z < 0.6 or abs(abs(qu) - 0.165) < 0.008: return C["ink"]
                if abs(qs) > 0.05 and abs(q.z + 0.03) < 0.022 and abs(qu) < 0.07:   # the maker's plate on each side
                    return C["ink"] if (abs(q.z + 0.03) > 0.016 or abs(qu) > 0.064) else (C["brassl"] if abs(q.z + 0.03) < 0.005 else C["brass"])
                b_ = psx_dot(p, [(rec0 + rec1) / 2 + u * cu + side * qs + Vector((0, 0, cz)) for cu in (-0.14, 0.14) for cz in (-0.04, 0.04)], C, 0.007)
                if b_ is not None: return b_
                return lit(C["metall"] if s_ > 0.55 else (C["metal"] if s_ > -0.2 else C["irond"]), n, ao, metal=True, p=p)
            return lit(C["metall"] if s_ > 0.6 else (C["metal"] if s_ > -0.2 else C["irond"]), n, ao, metal=True, p=p)
        if base == "brass" and segd(p, ab, g0)[0] < 0.2 and (p - gz).length > 0.05:   # the belt: brass cartridges on a dark cloth strip
            dmin, sa = 9.0, 0.0; s0 = 0.0
            for a_, b_ in zip(belt_, belt_[1:]):
                d_, t_ = segd(p, a_, b_)
                if d_ < dmin: dmin, sa = d_, s0 + t_ * (b_ - a_).length
                s0 += (b_ - a_).length
            if (sa * 55) % 1.0 < 0.3: return C["ink"]
            return C["brassl"] if n.dot(PSX_LIGHT) > 0.3 else C["brass"]
        if base == "brass" and (p - gz).length < 0.05:   # the engine's pressure gauge: a pale dial, a needle
            q = p - gz
            if n.y < 0.6: return C["brass"]
            dd = math.hypot(q.x, q.z)
            if dd > 0.03: return C["brassl"] if q.z > 0 else C["brass"]
            if dd > 0.025: return C["ink"]
            return C["ink"] if abs(q.x * 0.6 - q.z) < 0.004 and dd < 0.022 else C["stencil"]
        if base == "rubber":   # the hoses: ribbed rubber (rings along each hose), a brass coupling at each end
            if min((p - q_).length for q_ in hend) < 0.045: return C["brassl"] if n.dot(PSX_LIGHT) > 0.3 else C["brass"]
            d_, sa = along_hose(p)
            return C["ink"] if (sa * 40) % 1.0 < 0.3 else lit(C["rubberl"] if n.dot(PSX_LIGHT) > 0.4 else C["rubber"], n, ao, p=p)
        if base not in ("plate", "boots", "leather"): return psx_kit(C, base, p, n, ao) or lit(C["metal"], n, ao, p=p)
        # ---- the armoured body ----
        for s_, (fc, fd) in FISTS.items():   # riveted iron gauntlets: finger lames, knuckle rivets, a flared cuff
            if (p - fc).length < 0.085:
                along = (p - fc).dot(fd)
                if along < -0.03: return C["ink"] if along > -0.038 else pl(p, n, ao, kind="arm", light=along > -0.05)
                if abs(along - 0.005) < 0.009: return C["brassl"] if ((p - fc).dot(Vector((0, 0, 1))) * 45) % 1.0 < 0.45 else C["ink"]
                if (along * 40) % 1.0 < 0.3: return C["ink"]
                return pl(p, n, ao, light=(along * 40) % 1.0 < 0.55 and n.dot(PSX_LIGHT) > -0.2)
        if z < 0.15:   # sabatons: lames over the foot, a dark sole, caked mud
            if z < 0.024: return lit(C["sole"], n, ao, p=p)
            if z < 0.034 or (z * 26) % 1.0 < 0.2: return C["ink"]
            if z < 0.07 and _nz.noise(Vector((x * 18, y * 18, z * 6))) > -0.1: return lit(C["mud"].lerp(C["sole"], 0.3), n, ao, p=p)
            return pl(p, n, ao, light=(z * 26) % 1.0 < 0.36 and n.dot(PSX_LIGHT) > -0.2, grime=0.5)
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.1 and (abs(x) > 0.2 or y < -0.18 or z > 1.45) and z > 0.95
        if on_arm:   # the arm harness: upper lames, a round couter, a riveted vambrace, mail inside the elbow
            if abs(along - 1.0) < 0.14:
                kp = JOINTS["lowerarm01." + side_]
                if abs(along - 1.0) > 0.115: return C["ink"]
                if math.hypot(*(p - kp).to_2d()) < 0.02 and abs(along - 1.0) < 0.03: return C["brassl"]
                return pl(p, n, ao, light=abs(along - 1.0) < 0.05 and n.dot(PSX_LIGHT) > 0.2, kind="arm")
            if along < 1.0:   # the upper arm's lames: each lit along its lower, overlapping edge
                f_ = (along * 3) % 1.0
                if f_ < 0.12: return C["ink"]
                return pl(p, n, ao, light=f_ > 0.84, kind="arm")
            for b_ in (1.3, 1.72):
                if abs(along - b_) < 0.045: return C["ink"] if abs(along - b_) > 0.03 else lit(C["ironl"] if n.dot(PSX_LIGHT) > 0.3 else C["iron"], n, ao, metal=True, p=p)
            if abs(along - 1.51) < 0.025 and (math.atan2(n.y, n.x) * 2.5) % 1.0 < 0.3: return C["brassl"]
            return pl(p, n, ao, kind="arm")
        if z < 0.95:   # the legs: tassets on the thigh, mail behind the knee, greaves; scuffs and mud low down
            kz = JOINTS["lowerleg01.L"].z; lx = JOINTS["lowerleg01." + ("L" if x > 0 else "R")].x
            if abs(z - kz) < 0.07 and n.y > 0.55: return mail(p, n, ao)
            if z > kz + 0.08:
                f_ = (z * 11) % 1.0 if n.y < 0.2 else 0.5                                          # the tassets hang at the front and sides only
                if z > 0.66 and f_ < 0.16: return C["ink"]                                          # the tassets' lames
                if z > 0.66 and n.y < -0.4 and abs(abs(x - lx) - 0.05) < 0.016:                      # a riveted strap down each tasset
                    b_ = psx_dot(p, [Vector((lx + math.copysign(0.05, x - lx), y, (math.floor(z * 11) + 0.55) / 11))], C, 0.008)
                    if b_ is not None: return b_
                    if abs(abs(x - lx) - 0.05) < 0.01: return lit(C["leatherl"] if n.dot(PSX_LIGHT) > 0.4 else C["leather"], n, ao, p=p)
                return pl(p, n, ao, light=z > 0.66 and f_ > 0.84, kind="leg")
            if z > kz - 0.1 and n.y < 0: return pl(p, n, ao, kind="leg")                           # under the knee cop
            if abs(z - (kz - 0.1)) < 0.006: return C["ink"]                                        # the greave's top rim
            if abs(n.y) < 0.3 and abs(n.x) > 0.55:   # the greave's hinge: a seam with three rivets
                b_ = psx_dot(p, [Vector((x, y, kz - 0.17 - 0.1 * i_)) for i_ in range(3)], C, 0.008)
                if b_ is not None: return b_
            if n.y < -0.5 and abs(x - lx) < 0.011: return lit(C["platel"], n, ao, metal=True, p=p)   # its lit ridge
            if n.y < -0.5 and 0.011 <= (x - lx) * (1 if x > 0 else -1) < 0.02: return lit(C["plated"], n, ao, metal=True, p=p)
            return pl(p, n, ao, kind="leg", grime=0.15 + 0.45 * max(0.0, (0.4 - z) / 0.3))
        if z < 1.04:   # the waist: a heavy riveted belt with a brass buckle over a mail skirt
            if z < 0.975: return mail(p, n, ao)
            if z < 0.984 or z > 1.031: return C["ink"]
            a_ = math.degrees(math.atan2(x, -(y - yc_)))
            if abs(a_) < 9:   # the buckle
                return C["ink"] if (abs(a_) > 7.5 or z < 0.99 or z > 1.025) else (C["brassl"] if z > 1.008 else C["brass"])
            if (a_ / 30) % 1.0 < 0.08: return C["ink"]
            b = psx_dot(p, [Vector((x, y, 1.008))], C, 0.007) if abs(math.remainder(a_ - 15, 30)) < 2 else None
            if b is not None: return b
            return lit(C["leatherl"] if (n.dot(PSX_LIGHT) > 0.5 or z > 1.024) else C["leather"], n, ao, p=p)
        front = y < yc_ - 0.02
        if front:   # the breastplate: a ridge, side panels, rows of rivets, a faded stencilled cross and roman numerals, abdomen lames
            if z < 1.17:
                f_ = (z - 1.04) % 0.043
                if f_ < 0.01: return C["ink"]
                b_ = psx_dot(p, [Vector((sx * 0.12, y, 1.04 + 0.043 * (math.floor((z - 1.04) / 0.043) + 0.55))) for sx in (-1, 1)], C, 0.007)
                if b_ is not None: return b_
                return pl(p, n, ao, light=f_ > 0.034, kind="torso")
            if abs(z - 1.17) < 0.007: return C["ink"]
            r_ = psx_dot(p, [Vector((sx * 0.155, y, rz)) for sx in (-1, 1) for rz in (1.21, 1.27, 1.33, 1.39)] + [Vector((sx * 0.06, y, 1.2)) for sx in (-1, 1)], C, 0.0075)
            if r_ is not None: return r_
            if -0.012 < x < 0.0: return lit(C["platel"], n, ao, metal=True, p=p)                  # the ridge: lit on the light side,
            if 0.0 <= x < 0.009: return lit(C["plated"], n, ao, metal=True, p=p)                   # shadowed on the other
            cx_, cz_ = x + 0.07, z - 1.29                                                         # the stencilled patriarchal cross, off centre
            if abs(cx_) < 0.011 and -0.075 < cz_ < 0.065 or abs(cx_) < 0.044 and abs(cz_) < 0.009 or abs(cx_) < 0.027 and abs(cz_ - 0.035) < 0.008:
                return lit(C["stencil"], n, ao, p=p) if _nz.noise(p * 30) > -0.45 else pl(p, n, ao, kind="torso")
            nx_, nz_ = x - 0.075, z - 1.3                                                          # "VII" stencilled on the right breast
            if abs(nz_) < 0.024 and -0.04 < nx_ < 0.04:
                if abs(abs(nx_ + 0.02) - (nz_ + 0.024) * 0.35) < 0.007 or abs(nx_ - 0.012) < 0.006 or abs(nx_ - 0.032) < 0.006: return lit(C["stencil"], n, ao, p=p)
            if abs(abs(x) - 0.13) < 0.006: return C["ink"]                                       # the side panels' seams, lit on the inner side
            if abs(x) > 0.13 and z > 1.42: return pl(p, n, ao, light=n.dot(PSX_LIGHT) > -0.1, kind="torso")   # the rolled neck edge
            return pl(p, n, ao, light=(0.13 - abs(x)) < 0.012 and abs(x) < 0.13, kind="torso")
        if abs(x) < 0.012:   # the backplate's weld seam: a raised bead, lit on top
            return C["ink"] if abs(x) > 0.008 else lit(C["platel"] if int(z * 70) % 2 else C["plate"], n, ao, metal=True, p=p)
        if abs(abs(x) - 0.1) < 0.02 and z > 1.25:   # the engine's harness straps, with a riveted plate where they cross the shoulder
            if abs(abs(x) - 0.1) > 0.016: return C["ink"]
            return lit(C["leatherl"] if n.dot(PSX_LIGHT) > 0.3 else C["leather"], n, ao, p=p)
        return pl(p, n, ao, kind="torso")
    PAINT = paint

def combat_medic():
    # Combat Medic (2026-11-10, eighth Principality figure, class A, 25 mm): an original WWI field medic in the setting's
    # genre, not a copy of any miniature or artwork. A peaked field cap (its crown in the seat colour) with a neck cloth
    # hanging behind, over a blank mannequin face; a short field tunic to mid-thigh with a long stained canvas apron
    # tied over it (detail pass: not the Rifleman's greatcoat); a tall wooden medical chest on the back; a
    # big canvas medi-kit bag on the left hip, slung from the right shoulder, the left hand resting on it; a bandolier of
    # glass vials across the chest; a white armband with a red patriarchal cross on the left sleeve; a pistol hanging in
    # the right hand, a misericordia (a thin mercy dagger) at the belt. Painted PSX look: ww1_paint for the tunic, webbing,
    # breeches, puttees, boots and gloves, the tunic skirt and apron laid over the body (Coat); the armband, the bag's strap
    # and the vial bandolier painted on the body; the bag's flap, buckles and cross, the chest's brass corners, lid and
    # cross, the neck cloth's folds painted on their solid shapes. Modules: Coat, Helmet (cap, neck cloth), Back (chest),
    # Hip (bag, dagger), Weapon (pistol), Accent (the cap's crown).
    global BODY_BUDGET, TEX_SIZES, PAINT, HEAD_SEGS, HEAD_RINGS
    BODY_BUDGET = 900; TEX_SIZES = {"Body": 256, "Coat": 256, "Back": 128, "Hip": 128}; HEAD_SEGS, HEAD_RINGS = 12, 9
    from mathutils import noise as _nz
    import bmesh as _bm
    def pose(r):
        human.aim(r, "spine03", P(0, -0.06, 1.4))                                              # walking, a little forward
        human.aim(r, "upperleg01.L", P(0.11, -0.12, 0.5)); human.aim(r, "upperleg02.L", P(0.11, -0.12, 0.5))
        human.aim(r, "lowerleg01.L", P(0.12, -0.1, 0.08)); human.aim(r, "lowerleg02.L", P(0.12, -0.1, 0.08))
        human.aim(r, "foot.L", P(0.12, -0.26, -0.01))
        human.aim(r, "upperleg01.R", P(-0.1, 0.05, 0.5)); human.aim(r, "upperleg02.R", P(-0.1, 0.05, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.11, 0.14, 0.1)); human.aim(r, "lowerleg02.R", P(-0.11, 0.14, 0.1))
        human.aim(r, "foot.R", P(-0.11, 0.0, -0.02))
        human.reach(r, "R", P(-0.24, 0.0, 1.1), P(-0.25, -0.16, 0.92))                        # the pistol hand hanging, a little forward
        human.reach(r, "L", P(0.27, 0.04, 1.15), P(0.25, -0.1, 0.99))                         # the left hand resting on the bag
    hands, hp, top = human_body(pose, dress=WW1_DRESS, head="mannequin", mitts="leather", shape="male-young-averagemuscle-minweight")
    surf = ww1_uniform(bands=False); toeless_feet()
    human_ = bpy.data.objects["human"]
    # (detail pass, 2026-11-10) not the Rifleman's bell-shaped greatcoat: a short field tunic skirt to mid-thigh, split
    # into tails at the front and back, and a long stained canvas apron tied over it at the front
    under = [o for o in MODULES["Body"] if o.name.startswith("cloth")]
    module("Coat")
    # both are laid out rigidly over the body (a short skirt over full breeches crumpled up in the cloth simulation):
    # each point ray-cast from outside and kept clear of the body, never further in than the row above, so they hang
    def laid(pts_rows, gap, m, thick, faces, rmin=0.0, rmax=9.0):   # rmax: hits further out (a hand, an arm held out) are ignored
        vs = []; prev = {}
        for j, row in enumerate(pts_rows):
            for i, (q_, d_) in enumerate(row):
                lim_ = rmax * d_.length; r_ = rmin * d_.length; d_ = d_.normalized()
                for c_ in (human_, *extra_):   # every surface crossing outward from the axis, up to rmax: the outermost counts
                    s0_ = q_.copy()
                    for _ in range(12):
                        ok, loc, *_ = c_.ray_cast(s0_, d_)
                        if not ok or (loc - q_).dot(d_) > lim_: break
                        r_ = max(r_, (loc - q_).dot(d_) + gap); s0_ = loc + d_ * 0.002
                r_ = max(r_, prev.get(i, 0.0)); prev[i] = r_; vs.append(tuple(q_ + d_ * r_))
        me = bpy.data.meshes.new("laid"); me.from_pydata(vs, [], faces)
        o = bpy.data.objects.new("laid", me); scn.collection.objects.link(o)
        for p_ in me.polygons: p_.use_smooth = True
        s_ = o.modifiers.new("sol", "SOLIDIFY"); s_.thickness = thick; s_.offset = 1.0; s_.use_rim = False
        return add(o, m)
    extra_ = under; SG, SR = 16, 3
    rows_ = [[(Vector((0, 0.005, 0.99 - 0.24 * j / SR)), Vector((math.sin(2 * math.pi * i / SG), -0.82 * math.cos(2 * math.pi * i / SG), 0))) for i in range(SG)] for j in range(SR + 1)]
    fs_ = [(j * SG + i, (j + 1) * SG + i, (j + 1) * SG + (i + 1) % SG, j * SG + (i + 1) % SG) for j in range(SR) for i in range(SG)]
    coat = laid(rows_, 0.016, "greatcoat", 0.011, fs_, rmin=0.165, rmax=0.27)
    for v in coat.data.vertices: v.co += Vector((v.co.x, v.co.y - 0.005, 0)).normalized() * 0.01 * max(0.0, (0.99 - v.co.z) / 0.24)   # a little flare to the hem
    for o in under: MODULES["Body"].remove(o); bpy.data.objects.remove(o)
    bpy.context.view_layer.update()
    extra_ = [coat]; AC, AR = 4, 5
    rows_ = [[(Vector((-0.12 + 0.24 * i / AC, -0.1, 0.975 - 0.42 * j / AR)), Vector((0.25 * (-1 + 2 * i / AC), -1, 0))) for i in range(AC + 1)] for j in range(AR + 1)]
    fs_ = [(j * (AC + 1) + i, j * (AC + 1) + i + 1, (j + 1) * (AC + 1) + i + 1, (j + 1) * (AC + 1) + i) for j in range(AR) for i in range(AC)]
    apron = laid(rows_, 0.02, "pale", 0.008, fs_)
    hem_c = hem_line(coat, 0.0, 0.005); apz = min(v.co.z for v in apron.data.vertices)
    module("Body")
    module("Helmet")
    # the peaked cap: a soft crown wider than the band, tipped back a little, a leather peak; the head under it trimmed
    heads = [o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name == "mannequin" and len(o.data.vertices) > 40]
    hd_ = max(heads, key=lambda o: max(v.co.z for v in o.data.vertices))
    vs = [v.co for v in hd_.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    c0 = (lo + hi) / 2; rw, rd = (hi.x - lo.x) / 2 + 0.008, (hi.y - lo.y) / 2 + 0.008
    zb = FACE_BOX["mid"].z + 0.04
    bm = _bm.new(); bm.from_mesh(hd_.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().z > zb + 0.02], context='FACES')
    bm.to_mesh(hd_.data); bm.free()
    bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=1.0, depth=0.035, location=(c0.x, c0.y + 0.004, zb + 0.016))
    o = bpy.context.object; o.scale = (rw + 0.004, rd + 0.004, 1); add(o, "cloth")                       # the band
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=1.0, radius2=1.22, depth=0.05, location=(c0.x, c0.y + 0.012, zb + 0.056))
    o = bpy.context.object; o.scale = (rw, rd, 1); o.rotation_euler = (math.radians(-8), 0, 0); add(o, "accent")   # the crown, tipped back
    box((c0.x, c0.y - rd - 0.026, zb - 0.002), (rw * 1.3, 0.055, 0.008), "leather", rot=(math.radians(18), 0, 0))   # the peak
    badge = Vector((c0.x, c0.y - rd - 0.006, zb + 0.016))
    module("Back")
    # the medical chest: a tall wooden box on the back, a lid, brass corners and a cross painted
    bk = surf(0.0, 1.2, front=False, out=0.0)
    mc = Vector((0, bk.y + 0.075, 1.27)); MW, MD, MH = 0.26, 0.13, 0.4   # tall: its top shows above the shoulders
    box(tuple(mc), (MW, MD, MH), "wood")
    box(tuple(mc + Vector((0, 0, MH / 2 + 0.015))), (MW + 0.02, MD + 0.02, 0.03), "wood")              # the lid
    module("Hip")
    # the medi-kit bag on the left hip, outside the coat; the misericordia at the front of the belt
    ok, at, *_ = coat.ray_cast(Vector((1.0, -0.02, 0.9)), Vector((-1, 0, 0)))
    bx = (at.x if ok else 0.2) + 0.05
    bag = Vector((bx, -0.03, 0.87)); BW, BD, BH = 0.09, 0.22, 0.17
    # a soft, rounded canvas satchel (not a box): a squashed tube lying front to back, its ends pinched, a flap over the top
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=1.0, depth=BD, location=tuple(bag), rotation=(math.pi / 2, 0, 0))
    o = bpy.context.object; o.scale = (BW / 2, BH / 2, 1.0)   # (local x, local y = world z after the turn, depth)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for v in o.data.vertices:   # the ends pinched in at the top, the bottom flattened
        e_ = abs(v.co.y) / (BD / 2)
        if v.co.z > 0: v.co.x *= 1 - 0.25 * e_
        v.co.z = max(v.co.z, -BH / 2 * 0.8)
    o.data.update(); add(o, "olive")
    box(tuple(bag + Vector((0.008, 0, BH / 2 - 0.035))), (BW * 0.5, BD * 0.86, 0.008), "olive", rot=(0, math.radians(-62), 0))   # the flap over its outer top
    bar(bag + Vector((-0.01, -0.045, BH / 2 - 0.005)), bag + Vector((-0.01, 0.045, BH / 2 - 0.005)), 0.008, "leather", sides=4)   # the carry handle
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.026, depth=0.05, location=tuple(surf(-0.12, 1.0, front=False, out=0.045)), rotation=(0, math.pi / 2, 0))
    roll = bpy.context.object; add(roll, "pale")                                                     # a rolled bandage strapped on the belt, behind the right hip
    rollc = roll.location.copy()
    dg = surf(0.06, 0.99, out=0.03)
    bar(dg + Vector((0, 0, 0.06)), dg - Vector((0, 0, 0.14)), 0.008, "iron", sides=4)                 # the thin blade's scabbard and grip
    bar(dg + Vector((-0.02, 0, 0.0)), dg + Vector((0.02, 0, 0.0)), 0.006, "brass", sides=3)
    module("Weapon")
    # the pistol hanging in the right fist, its barrel down along the hand
    fr, d = FISTS["R"]; f_ = Vector((0, -1, 0)); f_ = (f_ - d * f_.dot(d)).normalized()
    s0 = fr + f_ * 0.03
    bar(s0 - d * 0.03, s0 + d * 0.13, 0.018, "metal", sides=4); bar(s0 + d * 0.13, s0 + d * 0.16, 0.009, "metal", sides=5)
    bar(fr + f_ * 0.015, fr - f_ * 0.045, 0.015, "wood", sides=4)
    module("Body")
    # the coat skirt's front under the bag is hidden: nothing to remove (the bag sits outside it)
    print(f"MED c0={c0} zb={zb:.3f} bag={bag} chest={mc}")

    # ---- the paint ----
    C = generic_palette(coat="4a4636", coatd="2e2c22", coatl="625d48", tunic="4e4a38", tunicd="312f23", tunicl="68634c",
                        canvas="6a6a4c", canvasd="45452f", canvasl="86866a", arm="c8c0a8", armd="948c78", cross="7a1e18",
                        crossd="4e120e", vial="6e8a6a", viall="b8c8a8", amber="8a6a2a", cork="6a5236", hood="4e4a3a", hoodd="302d24")
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    def pcross(u, v, s):   # a patriarchal cross of size s at (u across, v up): (inside, on its ink edge)
        def on(g): return abs(u) < 0.16 * s + g and abs(v) < 0.5 * s + g or abs(u) < 0.36 * s + g and abs(v + 0.05 * s) < 0.1 * s + g or abs(u) < 0.24 * s + g and abs(v - 0.27 * s) < 0.09 * s + g
        return on(0.0), on(0.12 * s)
    S0, S1 = Vector((-0.13, 1.47)), Vector((0.18, 0.95))     # the bag's strap: right shoulder to left hip (x, z)
    V0, V1 = Vector((0.12, 1.47)), Vector((-0.16, 1.0))      # the vial bandolier: left shoulder to right hip
    def band(x, z, a, b):
        d_ = b - a; t = max(0.0, min(1.0, (Vector((x, z)) - a).dot(d_) / d_.length_squared)); return (Vector((x, z)) - (a + d_ * t)).length, t
    def extra(base, p, n, ao):
        x, y, z = p.x, p.y, p.z
        r = plain_face(C, base, p, n, ao)
        if r is not None: return r
        if base == "greatcoat":   # the split tunic skirt: belted top, hemmed tails, a pocket flap each side, folds, mud
            if z > 0.972: return C["ink"] if z < 0.98 else lit(C["webbing"], n, ao, p=p)
            h = z - hem_c(p); a_ = math.degrees(math.atan2(x, -y)); ea_ = 180 - abs(a_)
            if h < 0.008 or ea_ < 2.5 and z < 0.88: return C["ink"]                                   # the hem and the back vent
            if h < 0.022 or ea_ < 6 and z < 0.88: return lit(C["coatd"], n, ao, p=p)                   # the turned hem and the vent's facing
            if 30 < abs(a_) < 75 and 0.86 < z < 0.94:                                                   # the skirt pockets' flaps, a button each
                if z < 0.868 or abs(abs(a_) - 30) < 2.5 or abs(abs(a_) - 75) < 2.5: return C["ink"]
                if abs(abs(a_) - 52) < 4 and abs(z - 0.88) < 0.01: return C["brassl"] if z > 0.88 else C["brass"]
                return lit(C["coatl"] if z > 0.925 else C["coat"], n, ao, p=p)
            if h < 0.06 and _nz.noise(Vector((x * 14, y * 14, z * 4))) > 0.15: return lit(C["coatd"].lerp(C["mud"], 0.55), n, ao, p=p)   # mud splashed up the tails
            f_ = (a_ / 32.0 + _nz.noise(Vector((x * 4, y * 4, z * 2))) * 0.25) % 1.0
            c = C["coatd"] if f_ < 0.1 else (C["coatl"] if f_ < 0.17 and n.dot(PSX_LIGHT) > 0.2 else C["coat"])
            return lit(c, n, max(ao, 0.6), p=p)
        if base == "pale" and (p - rollc).length < 0.06:   # the bandage roll: turns of gauze, a buckled strap round it
            if abs(p.x - rollc.x) < 0.007: return C["ink"] if abs(p.x - rollc.x) > 0.004 else C["leather"]
            return C["armd"] if (math.atan2(p.z - rollc.z, p.y - rollc.y) * 3) % 1.0 < 0.15 and n.dot((p - rollc).normalized()) < 0.9 else lit(C["arm"], n, ao, p=p)
        if base == "pale":   # the apron: stained canvas, a stitched hem, a red cross, a pocket with scissors, blood wiped down it
            q = Vector((x, z)); top_ = 0.975
            hz_ = apz
            if abs(x) > 0.118 or z < hz_ + 0.008: return C["ink"]
            if abs(x) > 0.106 or z < hz_ + 0.02: return lit(C["armd"], n, ao, p=p)                     # the stitched edges
            if z > top_ - 0.018: return lit(C["armd"], n, ao, p=p)                                      # the waist hem
            i_, e_ = pcross(x, z - 0.86, 0.15)
            if i_: return lit(C["cross"], n, ao, p=p)
            if e_: return lit(C["arm"], n, ao, p=p)
            if -0.1 < x < -0.02 and 0.66 < z < 0.74:                                                     # the pocket, scissors' bows above it
                if z > 0.732 or abs(x + 0.1) < 0.006 or abs(x + 0.02) < 0.006 or z < 0.666: return C["armd"].lerp(C["ink"], 0.5)
                return lit(C["arm"], n, ao, p=p)
            if -0.085 < x < -0.055 and 0.74 < z < 0.77 and math.hypot((x + 0.07) % 0.03 - 0.015, z - 0.755) < 0.011: return C["metall"] if z > 0.755 else C["metal"]
            b = psx_blood(C, p, 0.1 * max(0.0, (0.82 - z) / 0.3))
            if b is not None: return b
            if abs(math.sin(x * 70 + _nz.noise(p * 6) * 2)) < 0.12 and z < 0.82: return lit(C["armd"], n, ao, p=p)   # long creases down it
            return lit(C["arm"].lerp(C["mud"], 0.25 * max(0.0, (0.7 - z) / 0.15)) if n.dot(PSX_LIGHT) > -0.2 else C["armd"], n, ao, grime=0.25 * max(0.0, (0.7 - z) / 0.15), p=p)
        if base == "cloth" and (p - c0).length < 0.2:   # the cap band: dark, a brass cross badge at the front
            if (p - badge).length < 0.016 and n.y < -0.3:
                q = p - badge; i_, e_ = pcross(q.x, q.z, 0.03)
                if i_: return C["brassl"]
                if e_: return C["ink"]
            return C["ink"] if abs(z - zb - 0.016) > 0.014 else lit(C["clothd"], n, ao, p=p)
        if base == "leather" and (p - c0).length < 0.2: return lit(C["leatherl"] if n.z > 0.5 else C["leather"], n, ao, metal=True, p=p)   # the peak, a little shine
        if base == "wood" and (p - mc).length < 0.3:   # the chest: planks, brass corners, the lid's edge, a painted cross on the back
            q = p - mc
            if abs(q.z - MH / 2) < 0.006: return C["ink"]
            if abs(abs(q.x) - MW / 2) < 0.025 and abs(abs(q.z) - MH / 2) < 0.03 or q.z > MH / 2 and abs(abs(q.x) - MW / 2 - 0.01) < 0.02:
                return C["brassl"] if n.dot(PSX_LIGHT) > 0.3 else C["brass"]
            if abs(n.x) < 0.6 and abs(abs(q.x) - 0.1) < 0.014 and q.z < MH / 2:   # the carrying straps round the chest, a brass buckle each
                if abs(abs(q.x) - 0.1) > 0.009: return C["ink"]
                if n.y > 0.6 and abs(q.z + 0.12) < 0.016: return C["ink"] if abs(q.z + 0.12) > 0.011 else C["brassl"]
                return lit(C["leatherl"] if n.dot(PSX_LIGHT) > 0.3 else C["leather"], n, ao, p=p)
            if n.y > 0.6 and q.z < MH / 2:
                i_, e_ = pcross(q.x, q.z + 0.01, 0.2)
                if i_: return lit(C["cross"], n, ao, p=p)
                if e_: return lit(C["arm"], n, ao, p=p)
            if (q.z * 22) % 1.0 < 0.1: return C["woodd"]
            return lit(C["woodl"] if n.dot(PSX_LIGHT) > 0.6 else C["wood"], n, ao, grime=0.2, p=p)
        if base == "olive":   # the medi-kit satchel: canvas, a flap with a stitched edge, two buckled straps, a white disc with a red cross
            q = p - bag
            if abs(q.y) > BD / 2 - 0.008: return lit(C["canvasd"], n, ao, p=p) if abs(q.y) > BD / 2 - 0.003 else C["ink"]   # the pinched ends' seams
            if abs(abs(q.y) - 0.06) < 0.013 and n.x > 0.2:   # two leather straps over the flap, brass buckles
                if abs(abs(q.y) - 0.06) > 0.009: return C["ink"]
                if abs(q.z + 0.005) < 0.012: return C["ink"] if abs(q.z + 0.005) > 0.008 else (C["brassl"] if q.z > -0.005 else C["brass"])
                return lit(C["leatherl"] if n.dot(PSX_LIGHT) > 0.3 else C["leather"], n, ao, p=p)
            if n.x > 0.3 and abs(q.y) < 0.04 and abs(q.z + 0.03) < 0.04:   # the white disc with the red cross, low on the side
                dd_ = math.hypot(q.y, q.z + 0.03)
                if dd_ < 0.034:
                    i_, e_ = pcross(q.y, q.z + 0.03, 0.055)
                    return C["cross"] if i_ else C["arm"]
                if dd_ < 0.039: return C["ink"]
            if q.x > 0.01 and abs(q.z - 0.02) < 0.006: return C["ink"]                                    # the flap's edge, stitched just above
            if q.x > 0.01 and abs(q.z - 0.033) < 0.004: return C["canvasd"]
            if q.z < -BH / 2 * 0.8 + 0.006: return C["ink"]
            c = C["canvasl"] if n.dot(PSX_LIGHT) > 0.6 else (C["canvas"] if n.dot(PSX_LIGHT) > -0.1 else C["canvasd"])
            b = psx_blood(C, p, -0.05)
            return b if b is not None else lit(c, n, ao, grime=0.25, p=p)
        if base == "iron" and (p - dg).length < 0.2:   # the misericordia's scabbard: dark leather, a brass chape
            return C["brass"] if z < dg.z - 0.12 else lit(C["leather"], n, ao, p=p)
        if base != "tunic": return None
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.075 and (abs(x) > 0.16 or y < -0.16 or z > 1.41) and z > 1.0
        if on_arm:
            if side_ == "L" and 0.4 < along < 0.72:   # the armband: white, a red patriarchal cross on the outside
                if along < 0.43 or along > 0.69: return C["ink"]
                if n.x > 0.3:
                    q = p - (JOINTS["upperarm01.L"] + (JOINTS["lowerarm01.L"] - JOINTS["upperarm01.L"]) * 0.555)
                    i_, e_ = pcross(q.y, q.z, 0.085)
                    if i_: return C["cross"]
                return lit(C["arm"] if n.dot(PSX_LIGHT) > -0.2 else C["armd"], n, ao, p=p)
            return None
        if 0.965 < z < 1.04:   # a plain leather belt (no ammunition pouches on a medic), a brass clasp
            if z < 0.972 or z > 1.033: return C["ink"]
            if y < 0 and abs(x) < 0.026: return C["ink"] if abs(x) > 0.021 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.4 else C["brass"])
            return lit(C["leatherl"] if abs(z - 1.0) < 0.004 else C["leather"], n, ao, p=p)
        if z < 1.04: return None
        # each strap has a solid ink edge about two texels wide (detail pass: thinner edges broke into speckle)
        dd, t = band(x, z, V0, V1)
        if dd < 0.04:   # the bandolier: a broad leather band, four corked glass vials in its loops on the front
            if dd > 0.028: return C["ink"]
            k = (t * 4) % 1.0
            if y < 0 and 0.22 < k < 0.78 and dd < 0.02:
                if k < 0.36: return C["cork"]
                c = C["amber"] if int(t * 4) % 2 else C["vial"]
                return C["viall"] if (k > 0.6 and dd < 0.012) else c
            return lit(C["leatherl"] if dd < 0.012 and n.dot(PSX_LIGHT) > 0.2 else C["leather"], n, ao, p=p)
        dd, t = band(x, z, S0, S1)
        if dd < 0.032:   # the bag's canvas strap, a brass slide buckle on the chest
            if dd > 0.021: return C["ink"]
            if y < 0 and abs(t - 0.3) < 0.05: return C["ink"] if abs(t - 0.3) > 0.033 else (C["brassl"] if dd < 0.01 else C["brass"])
            return lit(C["canvasl"] if dd < 0.009 else C["canvas"], n, ao, p=p)
        if z > NECK.z - 0.035: return None   # the collar: ww1_paint's
        if y < 0:   # the buttoned front, a pale field-dressing tag tied at the second button
            b = psx_dot(p, [Vector((0.0, y, bz)) for bz in (1.33, 1.23, 1.13)], C, 0.009)
            if b is not None: return b
            if 0.02 < x < 0.07 and 1.19 < z < 1.24: return C["ink"] if (z < 1.2 or z > 1.23 or x < 0.03 or x > 0.06) else lit(C["parchment"], n, ao, p=p)
        if abs(x) < 0.006 and y > 0: return C["tunicd"].lerp(C["ink"], 0.4)                          # the back seam
        return lit(C["tunicl"] if n.dot(PSX_LIGHT) > 0.7 else C["tunic"], n, ao, p=p)
    PAINT = ww1_paint(C, extra=extra, hips=False)
def holy_warrior():
    # Holy Warrior (2026-11-10, ninth Principality figure, class B, 32 mm): an original armoured zealot, WWI
    # trench-fantasy, not a copy of any miniature or artwork. A tall sugar-loaf great helm (no head under it) with a
    # brass cross over its face, a mail aventail to the shoulders; a steel cuirass with a clean gilt patriarchal cross
    # painted on the breast; layered pauldrons (their top plates in the seat colour), a prayer strip under a
    # red wax seal on the left; plated arms, gauntlets, solid knee cops, greaves and sabatons; a tabard front and back
    # from the belt (seat colour); standing vigil, both gauntlets on the crossguard of a two-handed blessed sword
    # resting point down before him. Painted PSX look: the smooth body painted texel by texel (the cuirass's ridge,
    # rivets, rolled neckline and holy symbol, the lames and couters of the arms, mail at the waist and behind the
    # knees, greaves, sabatons, grime); the helm's slit and breaths, the mail rings, the sword's fuller and engraved
    # scripture, the strip's text painted on their solid shapes. Modules: Helmet (helm, cross, aventail), Chest
    # (pauldrons, seal, knee cops), Weapon (sword), Accent (tabards, pauldron tops).
    global BODY_BUDGET, TEX_SIZES, PAINT; BODY_BUDGET = 1100; TEX_SIZES = {"Body": 256, "Helmet": 256, "Chest": 256, "Weapon": 256}
    from mathutils import noise as _nz
    import bmesh as _bm
    def pose(r):
        human.aim(r, "spine03", P(0, -0.02, 1.4))                                              # upright, at vigil
        human.aim(r, "upperleg01.L", P(0.13, -0.03, 0.5)); human.aim(r, "upperleg02.L", P(0.13, -0.03, 0.5))
        human.aim(r, "lowerleg01.L", P(0.15, -0.03, 0.08)); human.aim(r, "lowerleg02.L", P(0.15, -0.03, 0.08))
        human.aim(r, "foot.L", P(0.18, -0.19, -0.01))
        human.aim(r, "upperleg01.R", P(-0.13, 0.01, 0.5)); human.aim(r, "upperleg02.R", P(-0.13, 0.01, 0.5))
        human.aim(r, "lowerleg01.R", P(-0.15, 0.02, 0.08)); human.aim(r, "lowerleg02.R", P(-0.15, 0.02, 0.08))
        human.aim(r, "foot.R", P(-0.18, -0.15, -0.01))
        human.reach(r, "R", P(-0.21, -0.06, 1.1), P(-0.035, -0.27, 1.0))                      # both hands on the crossguard
        human.reach(r, "L", P(0.21, -0.06, 1.1), P(0.035, -0.27, 1.06))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "plate", "thigh": "plate", "torso": "plate"}, head="mannequin",
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    human_ = bpy.data.objects["human"]
    for v in human_.data.vertices:   # the cuirass's and the harness's bulk
        if 0.95 < v.co.z < 1.46: v.co += v.normal * 0.018
        elif 0.1 < v.co.z <= 0.95: v.co += v.normal * 0.008
    human_.data.update(); bpy.context.view_layer.update()
    toeless_feet()
    human_.data.update(); bpy.context.view_layer.update()
    nk = NECK.copy()
    module("Helmet")
    # the sugar-loaf great helm: a barrel from the chin to the brow, a tall cone to a blunt point; the head under it removed
    heads = [o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name == "mannequin"]
    hv = [v.co for o in heads for v in o.data.vertices]
    hi_z = max(v.z for v in hv); hd_ = max(heads, key=lambda o: len(o.data.vertices))
    hvs = [v.co for v in hd_.data.vertices]
    c0 = Vector(((min(v.x for v in hvs) + max(v.x for v in hvs)) / 2, (min(v.y for v in hvs) + max(v.y for v in hvs)) / 2, 0))
    for o in heads: MODULES["Body"].remove(o); bpy.data.objects.remove(o)                      # head and neck: nothing shows under a closed helm
    zs = FACE_BOX["mid"].z + 0.004; hb0, hb1 = nk.z + 0.03, hi_z + 0.02
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.128, depth=hb1 - hb0, end_fill_type='NOTHING', location=(c0.x, c0.y, (hb0 + hb1) / 2))
    o = bpy.context.object; o.scale = (1, 1.1, 1); add(o, "iron")
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.128, radius2=0.02, depth=0.15, location=(c0.x, c0.y, hb1 + 0.075))
    o = bpy.context.object; o.scale = (1, 1.1, 1); add(o, "iron")
    fz = c0.y - 0.128 * 1.1 - 0.006
    box((c0.x, fz, (zs + hb1 + 0.06) / 2 - 0.03), (0.022, 0.012, hb1 + 0.06 - zs + 0.06), "brass")   # the cross's upright down the face
    box((c0.x, fz, zs + 0.05), (0.12, 0.012, 0.022), "brass")                                  # its arms, above the slit
    # the mail aventail: from the helm's foot out over the shoulders
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.2, radius2=0.13, depth=0.12, end_fill_type='NOTHING', location=(c0.x, c0.y + 0.01, hb0 - 0.03))
    o = bpy.context.object; o.scale = (1, 0.88, 1); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "mail")
    module("Chest")
    shells = []
    for s, x in (("L", 1), ("R", -1)):   # layered pauldrons, the top plate in the seat colour
        sh_ = JOINTS["upperarm01." + s] + Vector((0.02 * x, 0, 0.06)); ad = (JOINTS["lowerarm01." + s] - JOINTS["upperarm01." + s]).normalized()
        ad = (ad + Vector((0.7 * x, 0, 0))).normalized()
        for k, (r_, L_, dz, m_) in enumerate(((0.125, 0.09, 0.0, "accent"), (0.115, 0.14, -0.01, "engplate"), (0.11, 0.18, -0.025, "engplate"))):   # engplate: painted apart from the body's plate
            o, fr_ = psx_shell(sh_ + Vector((0, 0, dz)) - ad * 0.05, ad, r_, L_, m_, ang=100 + 4 * k, n=9, rows=3, thick=0.012)
            if m_ != "accent": shells.append(fr_)
        if x > 0:   # a red wax seal and a prayer strip hanging from the left pauldron
            seal = sh_ + ad * 0.06 + Vector((0, -0.11, -0.05))
            sphere(tuple(seal), 0.022, "charge", (1, 0.5, 1), seg=8, rings=4)
            box(tuple(seal + Vector((0, 0.004, -0.09))), (0.035, 0.006, 0.16), "parchment")
    cops = []
    for s, x in (("L", 1), ("R", -1)):
        kj = JOINTS["lowerleg01." + s]
        ok, loc, *_ = human_.ray_cast(Vector((kj.x, -1.0, kj.z + 0.01)), Vector((0, 1, 0)))
        if not ok: continue
        c_ = loc + Vector((0, -0.008, 0)); sphere(tuple(c_), 1, "engplate", (0.06, 0.035, 0.065), seg=8, rings=5); cops.append(c_)
    module("Weapon")
    # the two-handed sword resting point down: the grip up through both fists, a wide crossguard below them, a long
    # straight blade to the ground, a disc pommel
    fc = (FISTS["R"][0] + FISTS["L"][0]) / 2
    gz = min(FISTS["R"][0].z, FISTS["L"][0].z) - 0.06                                            # the crossguard, under the lower fist
    sx_, sy_ = fc.x, fc.y
    bar(Vector((sx_, sy_, gz)), Vector((sx_, sy_, gz + 0.22)), 0.016, "leather", sides=6)       # the grip
    sphere((sx_, sy_, gz + 0.235), 0.03, "brass", (1, 0.45, 1), seg=8, rings=4)                 # the pommel
    box((sx_, sy_, gz), (0.24, 0.026, 0.026), "brass")                                          # the crossguard
    BL0 = gz - 0.015
    me = bpy.data.meshes.new("blade")
    w0, w1 = 0.034, 0.024
    me.from_pydata([(sx_ - w0, sy_, BL0), (sx_ + w0, sy_, BL0), (sx_ + w1, sy_, 0.07), (sx_, sy_, 0.012), (sx_ - w1, sy_, 0.07)], [], [(0, 1, 2, 3, 4)])
    bl = bpy.data.objects.new("blade", me); scn.collection.objects.link(bl); bl.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(bl, "blade")
    box((sx_, sy_ - 0.006, gz - 0.05), (0.03, 0.006, 0.07), "parchment")                     # a prayer strip bound under the guard
    module("Accent")
    # the tabard, front and back, from the belt to below the knees (detail pass, 2026-11-10): laid row by row close over
    # the thighs and knees (each row just clear of the frontmost leg at its height, easing back toward the body by at
    # most 3 cm per row, so it hangs from the knees rather than clinging to the shins), with soft folds across it
    # and a little flare and sway to the hem, not a flat board standing off the legs
    TC, TR, TL = 6, 8, 0.52
    for side in (-1, 1):
        vs, prev_ = [], None
        for j in range(TR + 1):
            z_ = 1.0 - TL * j / TR; t_ = j / TR; far = None
            for i in range(TC + 1):
                xq = (-0.115 + 0.23 * i / TC) * (1 + 0.15 * t_)
                ok, loc, *_ = human_.ray_cast(Vector((xq, side * 1.0, z_)), Vector((0, -side, 0)))
                if ok and abs(loc.y) < 0.3: far = loc.y if far is None else (max(far, loc.y) if side > 0 else min(far, loc.y))
            tgt = (far if far is not None else side * 0.1) + side * (0.03 if j == 0 else 0.016)
            yr = tgt if prev_ is None else (max(tgt, prev_ - 0.03) if side > 0 else min(tgt, prev_ + 0.03))
            prev_ = yr
            for i in range(TC + 1):
                u_ = i / TC; xq = (-0.115 + 0.23 * u_) * (1 + 0.15 * t_)
                fold = side * 0.016 * t_ * math.sin(u_ * math.pi * 3 + 0.6) + side * 0.008 * t_ * t_ * math.sin(u_ * math.pi * 1.0)   # folds, the hem swaying out
                vs.append((xq, yr + fold, z_ - (0.02 * t_ * t_ * math.sin(u_ * math.pi) if j == TR else 0.0)))
        fs = [(j * (TC + 1) + i, j * (TC + 1) + i + 1, (j + 1) * (TC + 1) + i + 1, (j + 1) * (TC + 1) + i) for j in range(TR) for i in range(TC)]
        me = bpy.data.meshes.new("tabard"); me.from_pydata(vs, [], fs if side < 0 else [f[::-1] for f in fs])
        o = bpy.data.objects.new("tabard", me); scn.collection.objects.link(o)
        for p_ in me.polygons: p_.use_smooth = True
        s_ = o.modifiers.new("sol", "SOLIDIFY"); s_.thickness = 0.008; s_.use_rim = False
        add(o, "accent")
    module("Body")
    print(f"HW c0={c0} zs={zs:.3f} hb={hb0:.3f}-{hb1:.3f} guard={gz:.3f} fists={[FISTS[s][0] for s in FISTS]}")

    # ---- the paint ----
    C = psx_palette(**{**WW1, **KIT, **dict(plate="62666a", plated="35383c", platel="9a9ea2", mail="3c3e42", maill="62646a",
                    blade="7e8286", bladel="b8bcc0", steel="8a8c90", sun="7a6024")})
    def lit(c, n, ao, metal=False, grime=0.0, p=None): return psx_lit(c, n, ao, metal, grime, p, C["mud"])
    # coherent plate shading (as the Mechanized Heavy Infantry's): light bands from a smoothed normal, radial from the
    # torso's or the nearer limb's axis, so the body's lumps no longer break the plate into dark blotches
    LIMBS = [(JOINTS[f"upperarm01.{s}"], JOINTS[f"lowerarm01.{s}"]) for s in ("L", "R")] + [(JOINTS[f"lowerarm01.{s}"], FISTS[s][0]) for s in ("L", "R")]         + [(JOINTS[f"upperleg01.{s}"], JOINTS[f"lowerleg01.{s}"]) for s in ("L", "R")] + [(JOINTS[f"lowerleg01.{s}"], JOINTS[f"foot.{s}"]) for s in ("L", "R")]
    ok_, yf_, *_ = human_.ray_cast(Vector((0, -1.0, 1.25)), Vector((0, 1, 0))); ok2_, yb_, *_ = human_.ray_cast(Vector((0, 1.0, 1.25)), Vector((0, -1, 0)))
    yc_ = ((yf_.y if ok_ else -0.14) + (yb_.y if ok2_ else 0.1)) / 2; ry_ = max(0.08, ((yb_.y if ok2_ else 0.1) - (yf_.y if ok_ else -0.14)) / 2)
    def bn(p, n):
        if 0.98 < p.z < 1.45 and psx_arm(p)[2] > 0.075: r = Vector((p.x / 0.2, (p.y - yc_) / ry_, 0))
        else:
            best = min(((segd(p, a_, b_), a_, b_) for a_, b_ in LIMBS), key=lambda e: e[0][0]); (d_, t_), a_, b_ = best
            if d_ > 0.16: return n   # not on a limb (the helm)
            ax_ = (b_ - a_).normalized(); r = p - (a_ + (b_ - a_) * t_); r -= ax_ * r.dot(ax_)
        if r.length < 1e-6: return n
        nz_ = n.z * 0.6; r.normalize(); r = r * math.sqrt(max(0.0, 1 - nz_ * nz_)) + Vector((0, 0, nz_))
        return (r * 0.85 + n * 0.15).normalized()
    def pl(p, n, ao, light=False):
        n = bn(p, n); ao = 1.0 if ao > 0.45 else 0.85   # the aventail's baked occlusion left black patches on the breast
        c = C["platel"] if (light or n.dot(PSX_LIGHT) > 0.72) else (C["plate"] if n.dot(PSX_LIGHT) > -0.3 else C["plated"])
        if p.z < 0.55 and _nz.noise(Vector((p.x * 14, p.y * 14, p.z * 2.0))) > 0.6: c = c.lerp(C["rust"], 0.3)   # kept bright: rust only low on the legs
        if _nz.noise(p * 11 + Vector((3, 3, 3))) > 0.7 and n.dot(PSX_LIGHT) > -0.1: c = c.lerp(C["platel"], 0.5)   # a few bright scuffs
        return lit(c, n, ao, metal=True, grime=0.4 * max(0.0, (0.45 - p.z) / 0.4), p=p)
    def mail(p, n, ao):   # mail in scalloped rows (a texel or more wide, no fine noise), each row lit along its top
        r_ = (p.z * 45 + 0.3 * abs(math.sin(math.atan2(p.x, -p.y) * 16))) % 1.0
        if r_ < 0.3: return C["ink"]
        return lit(C["maill"] if r_ > 0.7 and n.dot(PSX_LIGHT) > -0.2 else C["mail"], n, ao, metal=True, p=p)
    def pcross(u, v, s, g=0.0):   # inside a patriarchal cross of size s at (u across, v up), grown by g
        return abs(u) < 0.13 * s + g and abs(v) < 0.5 * s + g or abs(u) < 0.36 * s + g and abs(v + 0.02 * s) < 0.08 * s + g or abs(u) < 0.24 * s + g and abs(v - 0.26 * s) < 0.07 * s + g
    def paint(m, p, n, ao, tx, ty):
        base = m.split(".")[0]; x, y, z = p.x, p.y, p.z
        if base == "iron":   # the helm: the slit, breaths, a rivet row round the brow, a light ridge
            q = p - Vector((c0.x, c0.y, 0)); a = math.degrees(math.atan2(q.x, -q.y))
            if abs(a) < 70 and abs(z - zs) < 0.011 and abs(q.x) > 0.014: return C["ink"]                 # the sight slit
            if abs(a) < 70 and 0.011 <= z - zs < 0.022 and abs(q.x) > 0.014: return lit(C["platel"], n, ao, metal=True, p=p)   # its lit brow
            if -62 < a < -14 and zs - 0.1 < z < zs - 0.03:                                              # breaths on the right cheek: three rows of slots
                if (z * 40) % 1.0 < 0.45 and abs(math.remainder(a, 12)) < 3.5: return C["ink"]
            if abs(z - (zs + 0.04)) < 0.012:   # a brass-riveted band round the brow
                dd = math.hypot(abs(math.remainder(a, 24)) * 0.0024, z - (zs + 0.04))
                if dd < 0.008: return C["brassl"] if z > zs + 0.04 else C["brass"]
                if dd < 0.012: return C["ink"]
            if abs(z - hb1) < 0.007: return C["ink"]                                                     # the seam to the cone
            if hb1 - 0.02 < z < hb1 - 0.007: return lit(C["platel"], n, ao, metal=True, p=p)            # the barrel's lit top edge
            if z > hb1 and abs(math.remainder(a, 90)) < 3.0: return lit(C["platel"] if math.remainder(a, 180) < 0 or abs(a) < 10 else C["plated"], n, ao, metal=True, p=p)   # the cone's welded ridges
            if z < hb0 + 0.012: return C["ink"]
            if z < hb0 + 0.04:   # the lower rim: a row of rivets for the aventail
                dd = math.hypot(abs(math.remainder(a, 20)) * 0.0024, z - (hb0 + 0.026))
                if dd < 0.007: return C["brassl"]
                if dd < 0.011: return C["ink"]
            return pl(p, n, ao)
        if base == "mail": return mail(p, n, ao)
        if base == "brass" or base == "gilt": return psx_kit(C, base, p, n, ao)
        if base == "charge": return C["chargel"] if n.dot(PSX_LIGHT) > 0.5 else C["charge"]
        if base == "parchment":
            if (z * 50) % 1.0 < 0.35 and abs(x - round(x / 0.03) * 0.03) < 0.009: return C["ink"]   # lines of script
            return C["parchment"] if n.dot(PSX_LIGHT) > -0.2 else C["parchmentd"]
        if base == "blade":   # the blade: bright edges, a dark fuller with engraved script, a little blood near the point
            if abs(x - sx_) < 0.006: return C["ink"] if (z * 50) % 1.0 < 0.35 and z > 0.2 else C["plated"]
            if z < 0.2:
                b = psx_blood(C, p, 0.1)
                if b is not None: return b
            return lit(C["bladel"] if abs(x - sx_) > 0.02 else C["blade"], n, ao, metal=True, p=p)
        if base == "engplate" and (min((p - c_).length for c_ in cops) < 0.09 if cops else False):   # a knee cop: rim, rivet
            c_ = min(cops, key=lambda c: (p - c).length); q = p - c_
            if abs(q.z) > 0.052 or abs(q.x) > 0.048: return C["ink"]
            if abs(q.z) > 0.04 or abs(q.x) > 0.036: return lit(C["giltl"] if q.z > 0 else C["gilt"], n, ao, metal=True, p=p)   # a gilt rim
            if math.hypot(q.x, q.z - 0.015) < 0.01: return C["brassl"]
            return pl(p, n, ao, light=q.z > 0.03)
        for fr_ in shells:   # a pauldron lame: a rolled rim, rivets
            t, a = fr_(p)
            if base == "engplate" and -0.08 < t < 1.08 and abs(a) < 118 and z > 1.2:
                if t > 0.88 or abs(a) > 96: return C["ink"] if t > 0.95 or abs(a) > 102 else lit(C["giltl"] if n.dot(PSX_LIGHT) > 0.2 else C["gilt"], n, ao, metal=True, p=p)   # gilt-edged
                if abs(t - 0.75) < 0.06 and (a / 20) % 1.0 < 0.3: return C["brassl"]
                return pl(p, n, ao)
        if base == "engplate": return pl(p, n, ao)
        if base == "leather" and segd(p, Vector((sx_, sy_, gz)), Vector((sx_, sy_, gz + 0.22)))[0] < 0.03:   # the grip, bound
            return C["ink"] if (z * 45) % 1.0 < 0.3 else lit(C["leatherl"], n, ao, p=p)
        if base not in ("plate", "boots", "leather"): return psx_kit(C, base, p, n, ao) or lit(C["metal"], n, ao, p=p)
        # ---- the armoured body ----
        for s_, (fc_, fd) in FISTS.items():   # steel gauntlets: finger lames, a flared cuff
            if (p - fc_).length < 0.075:
                along = (p - fc_).dot(fd)
                if along < -0.03: return C["ink"] if along > -0.04 else pl(p, n, ao, light=along > -0.055)
                if abs(along - 0.005) < 0.009: return C["brassl"] if ((p - fc_).z * 45) % 1.0 < 0.45 else C["ink"]   # knuckle rivets
                if (along * 40) % 1.0 < 0.3: return C["ink"]
                return pl(p, n, ao, light=(along * 40) % 1.0 < 0.55 and n.dot(PSX_LIGHT) > -0.2)
        if z < 0.15:   # sabatons: lames, a dark sole, mud caked over the toes
            if z < 0.022: return lit(C["sole"], n, ao, p=p)
            if z < 0.032 or (z * 26) % 1.0 < 0.2: return C["ink"]
            if z < 0.06 and _nz.noise(Vector((x * 18, y * 18, z * 6))) > -0.1: return lit(C["mud"].lerp(C["sole"], 0.3), n, ao, p=p)
            return pl(p, n, ao, light=(z * 26) % 1.0 < 0.36 and n.dot(PSX_LIGHT) > -0.2)
        side_, along, ad = psx_arm(p)
        on_arm = ad < 0.08 and (abs(x) > 0.17 or y < -0.17 or z > 1.43) and z > 0.98
        if on_arm:   # the breast just behind the forearms is not arm: the surface must face away from the arm's axis
            wr_ = FISTS[side_][0]; a_, b_ = (JOINTS["upperarm01." + side_], JOINTS["lowerarm01." + side_]) if along < 1.0 else (JOINTS["lowerarm01." + side_], wr_)
            _, t_ = segd(p, a_, b_); r_ = p - (a_ + (b_ - a_) * t_)
            on_arm = r_.length < 1e-6 or r_.normalized().dot(n) > 0.35 and (along >= 1.0 or abs(x) > 0.15)   # the upper arms reach forward past the breast
        if on_arm:   # rerebrace lames, a round couter with a brass boss, a vambrace with two brass bands
            if abs(along - 1.0) < 0.13:
                if abs(along - 1.0) > 0.11: return C["ink"]
                if math.hypot(*(p - JOINTS["lowerarm01." + side_]).to_2d()) < 0.02 and abs(along - 1.0) < 0.04: return C["brassl"]
                return pl(p, n, ao, light=abs(along - 1.0) < 0.04)
            if along < 1.0:   # the lames, each lit along its overlapping lower edge
                f_ = (along * 3) % 1.0
                if f_ < 0.13: return C["ink"]
                return pl(p, n, ao, light=f_ > 0.84)
            for b_ in (1.3, 1.7):
                if abs(along - b_) < 0.045: return C["ink"] if abs(along - b_) > 0.03 else (C["brassl"] if n.dot(PSX_LIGHT) > 0.2 else C["brass"])
            return pl(p, n, ao)
        if z < 0.98:   # the legs: mail at the hips and behind the knee, cuisses with a lit edge, greaves with a ridge and hinge rivets
            kz = JOINTS["lowerleg01.L"].z; lx = JOINTS["lowerleg01." + ("L" if x > 0 else "R")].x
            if z > 0.86: return mail(p, n, ao)
            if abs(z - kz) < 0.06 and n.y > 0.5: return mail(p, n, ao)
            if abs(z - 0.86) < 0.008: return C["ink"]
            if 0.838 < z <= 0.852: return lit(C["platel"], n, ao, metal=True, p=p)                      # the cuisse's rolled top
            if abs(z - (kz + 0.07)) < 0.007: return C["ink"]
            if abs(z - (kz - 0.08)) < 0.007: return C["ink"]                                             # the greave's top
            if abs(n.y) < 0.3 and abs(n.x) > 0.55 and z < kz - 0.08:   # its hinge: three rivets down the outside
                b_ = psx_dot(p, [Vector((x, y, kz - 0.14 - 0.09 * i_)) for i_ in range(3)], C, 0.009)
                if b_ is not None: return b_
            if n.y < -0.5 and abs(x - lx) < 0.011: return lit(C["platel"], n, ao, metal=True, p=p)     # the lit ridge,
            if n.y < -0.5 and 0.011 <= (x - lx) * (1 if x > 0 else -1) < 0.02: return lit(C["plated"], n, ao, metal=True, p=p)   # its shadow side
            return pl(p, n, ao)
        if z < 1.03:   # the sword belt: a brass buckle and studs
            if z < 0.986 or z > 1.026: return C["ink"]
            if y < 0 and abs(x) < 0.03: return C["ink"] if (abs(x) > 0.024 or z < 0.993 or z > 1.019) else (C["brassl"] if z > 1.006 else C["brass"])
            if abs(math.remainder(math.degrees(math.atan2(x, -y)), 30)) < 3: return C["brassl"] if z > 1.006 else C["brass"]
            return lit(C["leatherl"] if z > 1.016 else C["leather"], n, ao, p=p)
        top_ = nk.z + 0.01 - 2.5 * x * x   # high, so the aventail hides the mail above it
        if z > top_: return mail(p, n, ao)                                                            # mail above the neckline
        if z > top_ - 0.02: return lit(C["giltl"] if y < 0 and n.dot(PSX_LIGHT) > 0.2 else C["gilt"], n, ao, metal=True, p=p)   # its gilt trim
        if y < 0:   # the breastplate: a ridge, gilt-trimmed armholes, rivets, a clean gilt patriarchal cross, fauld lames below
            q = Vector((x, 0, z - 1.25))
            if pcross(q.x, q.z, 0.24): return C["giltl"] if n.dot(PSX_LIGHT) > 0.3 and q.x < 0 else C["gilt"]
            if pcross(q.x, q.z, 0.24, 0.011): return C["ink"]
            if z < 1.1:   # the fauld: two lames, each lit along its lower edge, riveted at the ends
                f_ = (z - 1.03) / 0.035 % 1.0
                if f_ < 0.25: return C["ink"]
                b = psx_dot(p, [Vector((sx * 0.12, y, 1.03 + 0.035 * (math.floor((z - 1.03) / 0.035) + 0.6))) for sx in (-1, 1)], C, 0.0085)
                if b is not None: return b
                return pl(p, n, ao, light=f_ < 0.42)
            if abs(z - 1.1) < 0.007: return C["ink"]
            b = psx_dot(p, [Vector((sx * 0.14, y, rz)) for sx in (-1, 1) for rz in (1.14, 1.24, 1.34)], C, 0.0085)
            if b is not None: return b
            if -0.012 < x < 0.0: return lit(C["platel"], n, ao, metal=True, p=p)                       # the ridge: lit on the light side,
            if 0.0 <= x < 0.009: return lit(C["plated"], n, ao, metal=True, p=p)                        # shadowed on the other
            if abs(abs(x) - 0.175) < 0.012 and z > 1.18: return C["gilt"] if abs(abs(x) - 0.175) < 0.007 else C["ink"]   # gilt trim at the armholes
            return pl(p, n, ao)
        if abs(n.x) > 0.8:   # the side straps, buckled
            for sz in (1.12, 1.28):
                if abs(z - sz) < 0.016:
                    if abs(z - sz) > 0.011: return C["ink"]
                    return C["brassl"] if abs(y) < 0.012 else lit(C["leather"], n, ao, p=p)
        if abs(x) < 0.012:   # the backplate's spine: a raised ridge, lit on one side
            return C["ink"] if abs(x) > 0.008 else lit(C["platel"] if x < 0 else C["plated"], n, ao, metal=True, p=p)
        if z < 1.1:   # the back fauld's lames
            f_ = (z - 1.03) / 0.035 % 1.0
            if f_ < 0.25: return C["ink"]
            return pl(p, n, ao, light=f_ < 0.42)
        if abs(z - 1.1) < 0.007: return C["ink"]
        return pl(p, n, ao)
    PAINT = paint

def pilgrim():
    # Pilgrim (an original figure, not used by any unit): a tall pointed iron hood over the whole head (eye slit,
    # riveted rim, a cross) on a ragged sackcloth hood, a long torn robe caked in mud, a rope belt with a rosary, a
    # tabard (seat colour) with a bone cross, rag-wrapped bare feet, a nail-studded club raised in the right hand and an
    # iron cross held out in the left.
    def pose(r):
        human.reach(r, "R", P(-0.3, -0.02, 1.12), P(-0.3, -0.24, 1.3))
        human.reach(r, "L", P(0.25, -0.16, 1.08), P(0.14, -0.36, 1.18))
    hands, hp, top = human_body(pose, dress={"foot": "skin", "shin": "rags", "thigh": "robe", "torso": "robe"})
    import random; rnd = random.Random(1916)
    robe_and_tabard(rnd)
    # sackcloth hood over the shoulders, then the pointed iron hood
    c = Vector((hp.x, hp.y - 0.03, top.z))
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.24, radius2=0.11, depth=0.2, end_fill_type='NOTHING', location=(c.x, c.y, 1.42))
    h = bpy.context.object; h.scale = (1, 0.85, 1); h.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(h, "rags")
    # narrow and tall: a close-fitting iron hood (oval, just wider than the head) rising straight to the crown, then a
    # long spike
    base = top.z - 0.34; crown = top.z + 0.01; tip = top.z + 0.42
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.112, depth=crown - base, location=(c.x, c.y, (base + crown) / 2))
    o = bpy.context.object; o.scale = (1, 1.18, 1); add(o, "iron")
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.112, radius2=0.0, depth=tip - crown, location=(c.x, c.y, (crown + tip) / 2))
    cap = bpy.context.object; cap.scale = (1, 1.18, 1); add(cap, "iron")
    bpy.ops.mesh.primitive_torus_add(major_radius=0.115, minor_radius=0.012, major_segments=12, minor_segments=4, location=(c.x, c.y, base + 0.01))
    bpy.context.object.scale = (1, 1.18, 1); add(bpy.context.object, "metal")
    for a in range(8):   # rivets round the rim
        ang = a * math.pi / 4
        sphere((c.x + math.cos(ang)*0.118, c.y + math.sin(ang)*0.139, base + 0.03), 0.008, "brass", seg=4, rings=3)
    ez = top.z - 0.12   # eye slit, with a cross above it
    box((c.x, c.y - 0.128, ez), (0.12, 0.03, 0.024), "dark")
    box((c.x, c.y - 0.125, ez + 0.1), (0.02, 0.02, 0.11), "brass"); box((c.x, c.y - 0.122, ez + 0.12), (0.075, 0.02, 0.02), "brass")
    # club: a short shaft and a heavy head bound with iron and studded with nails
    hr = hands["R"]; d = (Vector((-0.25, -0.35, 1.0))).normalized()
    head = hr + d*0.3
    bar(hr - d*0.1, head, 0.022, "wood")
    bar(head - d*0.03, head + d*0.2, 0.055, "wood", sides=8)
    bar(head + d*0.03, head + d*0.08, 0.06, "iron", sides=8)
    for i in range(10):
        ang = i * 2.4; along = 0.06 + (i % 3) * 0.045
        side = d.cross(Vector((math.cos(ang), math.sin(ang), 0))).normalized()
        p = head + d*(along + 0.02)
        bar(p + side*0.045, p + side*0.095, 0.008, "metal", sides=4)
    # iron cross held out in the left hand
    hl = hands["L"] + Vector((0, -0.03, 0))
    bar(hl - Vector((0, 0, 0.1)), hl + Vector((0, 0, 0.3)), 0.022, "iron", sides=4)
    bar(hl + Vector((-0.12, 0, 0.17)), hl + Vector((0.12, 0, 0.17)), 0.022, "iron", sides=4)

{"rifleman": rifleman, "skirmisher": skirmisher, "shield": shield, "standard": standard, "pilgrim": pilgrim, "trench-pilgrim": trench_pilgrim, "stigmatic-nun": stigmatic_nun, "ecclesiastic-prisoner": ecclesiastic_prisoner, "war-prophet": war_prophet, "castigator": castigator, "reliquary-walker": reliquary_walker, "sanctified-giant": sanctified_giant, "yeoman": yeoman, "shock-trooper": shock_trooper, "lieutenant": lieutenant, "blind-marksman": blind_marksman, "trench-cleric": trench_cleric, "combat-engineer": combat_engineer, "mechanized-heavy-infantry": mechanized_heavy_infantry, "combat-medic": combat_medic, "holy-warrior": holy_warrior}[role]()
def join(objs, name):
    for o in objs:
        bpy.context.view_layer.objects.active = o
        for mdf in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=mdf.name)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    o = bpy.context.object; o.name = name; o.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o
def tris(o): return sum(len(p.vertices)-2 for p in o.data.polygons)
def only(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
OBJS = {name: join(parts, name) for name, parts in MODULES.items() if parts}
B, A = OBJS["Body"], OBJS["Accent"]
# the body's budget: 1,400 triangles alone, 1,000 when the equipment is in modules (docs/art-pipeline.md)
BUDGET = BODY_BUDGET or (1400 if len(OBJS) == 2 else 1000)
only(B); t = tris(B); r = BUDGET / t
while t > BUDGET:   # the head and hands ("head" group) are kept as they are
    d = B.modifiers.new("d","DECIMATE"); d.ratio = r; d.vertex_group = "head"; d.invert_vertex_group = True
    bpy.ops.object.modifier_apply(modifier="d"); t = tris(B); r = BUDGET / t * 0.98
scn.render.engine = 'CYCLES'; scn.cycles.device = 'CPU'; scn.render.bake.margin = 4   # a wider margin, so no tiny UV island samples an unbaked (black) texel
for name, o in OBJS.items():
    if name == "Accent":
        continue
    only(o)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.03); bpy.ops.object.mode_set(mode='OBJECT')
    size = (TEX_SIZES or {}).get(name, 128 if name == "Body" else 64)   # one 128x128 texture for the body, 64x64 for each module (larger classes more)
    img = bpy.data.images.new(f"{role}-{name}", size, size, alpha=True); img.generated_color = (0, 0, 0, 0)   # alpha 0 marks unbaked texels
    for m in o.data.materials:
        tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; m.node_tree.nodes.active = tn
    scn.cycles.samples = 8; bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}); col = list(img.pixels)
    scn.cycles.samples = 32; bpy.ops.object.bake(type='AO'); ao = list(img.pixels)
    px = []
    for i in range(0, len(col), 4):
        for c in range(3):
            v = (col[i+c]*(0.55+0.45*ao[i+c])) ** (1/2.2); px.append((round(v*31)/31) ** 2.2)   # 15-bit colour
        px.append(1.0)
    # a face smaller than a texel in the UV map is never baked, or samples another island's bleed, and reads as a
    # black blot (the Lieutenant's face, 2026-11-03): its texels, and any unbaked texel, take its material's own colour
    uvl = o.data.uv_layers.active.data
    def uvs(p): return [uvl[l].uv for l in p.loop_indices]
    def texels(p):
        us = uvs(p)
        x0, x1 = int(min(u.x for u in us) * size), int(max(u.x for u in us) * size)
        y0, y1 = int(min(u.y for u in us) * size), int(max(u.y for u in us) * size)
        return [(min(y, size - 1) * size + min(x, size - 1)) for x in range(x0, x1 + 1) for y in range(y0, y1 + 1)]
    def covers(p):   # does any texel centre fall inside the face (a thin sliver may have area and still cover none)
        a, b, c = uvs(p)[:3]
        d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y)
        if abs(d) < 1e-12: return False
        for t in texels(p):
            u = Vector((((t % size) + 0.5) / size, ((t // size) + 0.5) / size))
            l1 = ((b.y - c.y) * (u.x - c.x) + (c.x - b.x) * (u.y - c.y)) / d; l2 = ((c.y - a.y) * (u.x - c.x) + (a.x - c.x) * (u.y - c.y)) / d
            if l1 >= 0 and l2 >= 0 and l1 + l2 <= 1: return True
        return False
    for p in o.data.polygons:
        tiny = not covers(p)
        holes = texels(p) if tiny else [t for t in texels(p) if col[t * 4 + 3] < 0.5]
        if not holes: continue
        ramp = next((n for n in o.data.materials[p.material_index].node_tree.nodes if n.type == 'VALTORGB'), None)
        if ramp is None: continue
        e0, e1 = ramp.color_ramp.elements[0].color, ramp.color_ramp.elements[1].color
        # the material's own colour, in the image's sRGB values (as the baked texels are), then cut to 15-bit like them
        srgb = [((e0[c] + e1[c]) / 2) ** (1 / 2.2) for c in range(3)]
        rgb = [(round(s ** (1 / 2.2) * 31) / 31) ** 2.2 for s in srgb]
        for t in holes: px[t * 4:t * 4 + 3] = rgb
    if PAINT is not None:   # painted: each texel coloured from the surface point it maps to, then grown 4 texels past each island
        V, names, out_ = o.data.vertices, [m_.name for m_ in o.data.materials], [None] * (size * size)
        for p in o.data.polygons:
            a, b, c = [Vector((u.x * size, u.y * size)) for u in uvs(p)]
            d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y)
            if abs(d) < 1e-9: continue
            vs = [V[i].co for i in p.vertices]; ns = [V[i].normal for i in p.vertices]
            for ty in range(max(0, int(min(a.y, b.y, c.y))), min(size, int(max(a.y, b.y, c.y)) + 1)):
                for tx in range(max(0, int(min(a.x, b.x, c.x))), min(size, int(max(a.x, b.x, c.x)) + 1)):
                    ux, uy = tx + 0.5, ty + 0.5
                    l1 = ((b.y - c.y) * (ux - c.x) + (c.x - b.x) * (uy - c.y)) / d; l2 = ((c.y - a.y) * (ux - c.x) + (a.x - c.x) * (uy - c.y)) / d
                    l3 = 1 - l1 - l2
                    if min(l1, l2, l3) < -0.05: continue
                    t = ty * size + tx
                    out_[t] = PAINT(names[p.material_index], vs[0] * l1 + vs[1] * l2 + vs[2] * l3, (ns[0] * l1 + ns[1] * l2 + ns[2] * l3).normalized(), ao[t * 4], tx, ty)
        for _ in range(4):
            grow = {}
            for t in range(size * size):
                if out_[t] is None:
                    for q in (t - 1, t + 1, t - size, t + size):
                        if 0 <= q < size * size and out_[q] is not None: grow[t] = out_[q]; break
            for t, c in grow.items(): out_[t] = c
        BAYER = (0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5)   # PSX ordered dither, then 15-bit colour
        for t, c in enumerate(out_):
            if c is None: continue
            dz = (BAYER[(t // size % 4) * 4 + t % 4] / 16 - 0.47) / 31
            px[t * 4:t * 4 + 3] = [round(max(0.0, min(1.0, c[k] + dz)) * 31) / 31 for k in range(3)]
    img.pixels = px; img.pack()
    m = bpy.data.materials.new(f"{role}{name}"); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 1.0
    tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; tn.interpolation = 'Closest'
    m.node_tree.links.new(tn.outputs["Color"], b.inputs["Base Color"])
    o.data.materials.clear(); o.data.materials.append(m)
# Accent: one flat material, untextured
while A.data.uv_layers: A.data.uv_layers.remove(A.data.uv_layers[0])
A.data.materials.clear(); am = bpy.data.materials.new("Accent"); am.use_nodes = True; A.data.materials.append(am)
only(A); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.quads_convert_to_tris(); bpy.ops.object.mode_set(mode='OBJECT')
# scale to the game's 1.2 tall, feet at 0, centred
ALL = list(OBJS.values())
low = min((o.matrix_world @ v.co).z for o in ALL for v in o.data.vertices)
for o in ALL:
    for v in o.data.vertices: v.co.z -= low
top = max((o.matrix_world @ v.co).z for o in ALL for v in o.data.vertices)
for o in ALL:
    for v in o.data.vertices: v.co *= 1.2 / top
allp = [(o.matrix_world @ v.co) for o in ALL for v in o.data.vertices]
w = max(max(p.x for p in allp)-min(p.x for p in allp), max(p.y for p in allp)-min(p.y for p in allp))
print(f"FIG {role} " + " ".join(f"{n}={tris(o)}" for n, o in OBJS.items()) + f" total={sum(tris(o) for o in ALL)} height={max(p.z for p in allp):.3f} width={w:.3f}")
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=f"{outdir}/{role}.glb", export_format='GLB', export_yup=True)
# the Blender source, for review or hand edits (docs/art-pipeline.md): saved only when asked
if os.environ.get("FIG_BLEND"):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.environ["FIG_BLEND"], f"{role}.blend"), compress=True)
