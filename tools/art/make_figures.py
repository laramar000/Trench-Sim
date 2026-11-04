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
FIGURES = ("rifleman", "skirmisher", "shield", "standard", "trench-pilgrim", "stigmatic-nun", "ecclesiastic-prisoner", "war-prophet", "castigator", "reliquary-walker", "sanctified-giant", "yeoman")   # "pilgrim" is built with --figure until a unit uses it
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
          "gore": spattered("gore", "5a2a1e", spots=0.55, scale=12), "pale": spattered("pale", "d8ccb8", spots=0.4, scale=10),
          "brute": spattered("brute", "c8a294", spots=0.45, scale=7), "skirt": spattered("skirt", "4e5642", spots=0.3, scale=8)})   # the Sanctified Giant (2026-11-03)   # the Castigator   # the War Prophet's anti-tank charge (2026-11-03)   # the Stigmatic Nun (2026-11-03)
# Faceless heads (visual pass Stage 1, 2026-10-27): every figure wears a plain hood of matte canvas over a smooth,
# featureless head (no eyes, nose or mouth); the figures are told apart by silhouette, kit and pose.
FACE_SCALE = 1.12   # the plain head fills the space of the MakeHuman face
FACE_BOX = None
NECK = None
FISTS = {}   # with mitts: each gloved fist's centre and its forearm's direction, so a weapon sits in the hand
BODY_BUDGET = None   # a figure of a larger class sets its body's triangle budget (docs/art-pipeline.md section 3)
TEX_SIZES = None   # and its texture sizes by module name; else 128 for the body and 64 for each module
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

def coat_skirt(lean=0.0):
    # greatcoat skirt: an open, flared tube from the belt to the knee
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.21, radius2=0.165, depth=0.46, end_fill_type='NOTHING', location=(0, -lean*0.08, 0.76))
    o = bpy.context.object; o.scale = (1, 0.78, 1); o.rotation_euler = (lean*0.25, 0, 0)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(o, "coat")
    bpy.ops.mesh.primitive_torus_add(major_radius=0.17, minor_radius=0.018, major_segments=20, minor_segments=4, location=(0, 0, 0.98))
    t = bpy.context.object; t.scale = (1, 0.75, 1); add(t, "leather")
    box((0, -0.125, 0.98), (0.06, 0.02, 0.05), "brass")
    for z in (1.08, 1.18, 1.28): sphere((0, -0.13 + (z-1.0)*0.0, z), 0.012, "brass", seg=6, rings=4)

def brodie(c):
    sphere(tuple(c + Vector((0, 0, 0.03))), 0.125, "helmet", (1, 1.05, 0.55), seg=14)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.18, depth=0.012, location=tuple(c + Vector((0, 0, 0.025)))); add(bpy.context.object, "helmet")

def rifleman():
    def pose(r):
        human.reach(r, "R", P(-0.24, 0.02, 1.08), P(-0.07, -0.22, 1.0))
        human.reach(r, "L", P(0.26, -0.1, 1.12), P(0.1, -0.28, 1.27))
    hands, hp, top = human_body(pose)
    coat_skirt(); brodie(Vector((0, 0, top.z - 0.07)) + Vector((hp.x, hp.y, 0)))
    hr, hl = hands["R"] + Vector((0, -0.04, -0.02)), hands["L"] + Vector((0, -0.04, -0.02))
    d = (hl - hr).normalized(); butt, muz = hr - d*0.25, hl + d*0.45
    bar(butt, hr + d*0.25, 0.028, "wood"); bar(hr + d*0.25, muz - d*0.1, 0.02, "wood")
    bar(hr + d*0.1, muz, 0.01, "metal"); bar(muz, muz + d*0.18, 0.006, "iron", sides=4)
    chain([(0, 0.15, 1.05), (0, 0.15, 1.3)], [(0.15, 0.07), (0.14, 0.07)], "leather")
    skin([(0.15, -0.03, 1.43), (0.0, -0.155, 1.22), (-0.16, -0.12, 0.99)], [(0, 1), (1, 2)], [(0.045, 0.016)]*3, "accent", subdiv=0)
def skirmisher():
    def pose(r):
        human.aim(r, "spine03", P(0, -0.12, 1.4)); human.aim(r, "neck01", P(0, -0.2, 1.6))
        human.reach(r, "R", P(-0.25, -0.05, 1.0), P(-0.1, -0.3, 0.98))
        human.reach(r, "L", P(0.24, -0.2, 1.05), P(0.08, -0.42, 1.08))
    hands, hp, top = human_body(pose)
    coat_skirt(lean=0.2)
    c = Vector((hp.x, hp.y, top.z - 0.08))
    sphere(tuple(c + Vector((0, 0.02, 0.0))), 0.135, "cloth", (1, 1.08, 1.12), seg=12)
    chain([tuple(c + Vector((0, 0.1, 0.0))), tuple(c + Vector((0, 0.22, -0.15)))], [0.06, 0.02], "cloth")
    hr, hl = hands["R"] + Vector((0, -0.04, 0)), hands["L"] + Vector((0, -0.04, 0))
    d = (hl - hr).normalized()
    bar(hr - d*0.18, hl + d*0.05, 0.024, "wood"); bar(hr, hl + d*0.3, 0.01, "metal"); bar(hl + d*0.3, hl + d*0.45, 0.006, "iron", sides=4)
    chain([(0.2, 0.0, 0.85), (0.2, 0.05, 0.75)], [(0.08, 0.05), (0.07, 0.05)], "leather")
    sphere((-0.19, -0.06, 0.92), 0.035, "iron", seg=8, rings=6)
    sphere((0, -0.03, 1.40), 0.21, "accent", (1.1, 0.9, 0.42), seg=12, rings=6)
def shield():
    def pose(r):
        human.reach(r, "L", P(0.3, -0.08, 1.12), P(0.16, -0.3, 1.05))
        human.reach(r, "R", P(-0.33, -0.02, 1.4), P(-0.3, -0.1, 1.62))
    hands, hp, top = human_body(pose)
    coat_skirt()
    c = Vector((hp.x, hp.y, top.z - 0.09))
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.125, depth=0.26, location=tuple(c)); add(bpy.context.object, "iron")
    box(tuple(c + Vector((0, -0.124, 0.02))), (0.17, 0.012, 0.022), "dark")
    box(tuple(c + Vector((0, -0.128, -0.06))), (0.02, 0.01, 0.14), "iron")
    sphere(tuple(c + Vector((0, 0, 0.13))), 0.125, "iron", (1, 1, 0.35))
    for s in (-1, 1): sphere((0.2*s, 0, 1.38), 0.1, "iron", (1.1, 1.1, 0.7))
    hl = hands["L"]
    box((hl.x, hl.y - 0.07, 0.85), (0.42, 0.04, 0.78), "wood")
    box((hl.x, hl.y - 0.095, 0.87), (0.36, 0.012, 0.7), "accent")
    box((hl.x, hl.y - 0.105, 0.9), (0.05, 0.01, 0.5), "bone"); box((hl.x, hl.y - 0.105, 1.02), (0.26, 0.01, 0.05), "bone")
    hr = hands["R"]; up = Vector((0, 0, 1))
    bar(hr - up*0.12, hr + up*0.3, 0.017, "wood")
    sphere(tuple(hr + up*0.34), 0.055, "iron", seg=8, rings=6)
    for a in range(6):
        p = hr + up*0.34; o = Vector((math.cos(a)*0.055, math.sin(a)*0.055, 0))
        bar(p + o*0.5, p + o*1.4, 0.014, "iron", sides=4)
def standard():
    def pose(r):
        human.reach(r, "R", P(-0.24, -0.02, 1.08), P(-0.15, -0.2, 1.02))
        human.reach(r, "L", P(0.12, -0.18, 1.2), P(-0.1, -0.22, 1.36))
    hands, hp, top = human_body(pose)
    coat_skirt(); brodie(Vector((hp.x, hp.y, top.z - 0.07)))
    x, y = hands["R"].x - 0.0, hands["R"].y - 0.04
    base, tip = Vector((x, y, 0.05)), Vector((x, y, 2.0))
    bar(base, tip, 0.017, "wood")
    sphere(tuple(tip + Vector((0, 0, 0.03))), 0.035, "brass", seg=8, rings=6)
    bar(tip + Vector((0, 0, 0.03)), tip + Vector((0, 0, 0.15)), 0.01, "brass", sides=4)
    bar(tip - Vector((0, 0, 0.05)), tip - Vector((-0.42, 0, 0.05)), 0.008, "wood")
    me = bpy.data.meshes.new("pennant"); x0, x1, zt, zb = x, x + 0.42, tip.z - 0.05, tip.z - 0.65
    me.from_pydata([(x0, y, zt), (x1, y, zt), (x1, y, zb), ((x0 + x1)/2, y, zb + 0.15), (x0, y, zb)], [], [(0, 4, 3, 2, 1)])
    o = bpy.data.objects.new("pennant", me); scn.collection.objects.link(o)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.015; add(o, "accent")
    L = tip - Vector((-0.42, 0, 0))
    bar(L - Vector((0, 0, 0.05)), L - Vector((0, 0, 0.2)), 0.004, "metal", sides=4)
    box(tuple(L - Vector((0, 0, 0.27))), (0.07, 0.07, 0.1), "glow")
    box(tuple(L - Vector((0, 0, 0.21))), (0.09, 0.09, 0.02), "iron"); box(tuple(L - Vector((0, 0, 0.33))), (0.09, 0.09, 0.02), "iron")

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

def ww1_uniform():
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
    cloth_cone(0, 0, 1.0, 0.82, 0.15, 0.165, "tunic", sy=0.8, tris=50, thick=0.01, rows=4)
    # a pale band at the top of each puttee
    for x in (-0.1, 0.1):   # each band found on its own leg, wherever the pose put it
        f_, b_ = human_.ray_cast(Vector((x, -1, 0.42)), Vector((0, 1, 0))), human_.ray_cast(Vector((x, 1, 0.42)), Vector((0, -1, 0)))
        y_ = (f_[1].y + b_[1].y) / 2 if f_[0] and b_[0] else 0.01
        o_, i_ = human_.ray_cast(Vector((x * 6, y_, 0.42)), Vector((-x / abs(x), 0, 0))), human_.ray_cast(Vector((0, y_, 0.42)), Vector((x / abs(x), 0, 0)))
        cx_ = (o_[1].x + i_[1].x) / 2 if o_[0] and i_[0] else x
        r_ = max(abs(o_[1].x - cx_) if o_[0] else 0.05, (b_[1].y - f_[1].y) / 2 if f_[0] and b_[0] else 0.05) + 0.006
        bpy.ops.mesh.primitive_torus_add(major_radius=r_, minor_radius=0.011, major_segments=8, minor_segments=3, location=(cx_, y_, 0.42))
        add(bpy.context.object, "webbing")
    return surf
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

def trench_pilgrim():
    # Trench Pilgrim: the template infantry figure of docs/art-pipeline.md (2026-11-03). An original figure in the
    # setting's genre, not a copy of any official miniature or artwork. The owner's change (2026-11-03): a WWI trench
    # soldier rather than a robed pilgrim, keeping only the capirote in place of a helmet. Body: a wool service tunic
    # with its skirt, baggy breeches, puttees with a pale top band, ankle boots, gloved hands. Modules: Helmet (capirote
    # with a prayer strip), Weapon (bolt-action rifle with fixed bayonet, at port arms), Chest (belt webbing: belt,
    # braces, ammunition pouches, tunic pockets and buttons, bayonet scabbard), Back (pack with a rolled blanket), Hip
    # (haversack and water bottle), Accent (the capirote band, seat colour). The capirote's base is iron, not cloth.
    global BODY_BUDGET; BODY_BUDGET = 960
    def pose(r):
        human.aim(r, "spine03", P(0, -0.06, 1.4))
        human.reach(r, "R", P(-0.24, 0.0, 1.06), P(-0.08, -0.24, 0.98))
        human.reach(r, "L", P(0.25, -0.12, 1.12), P(0.1, -0.3, 1.25))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "puttee", "thigh": "breeches", "torso": "tunic"}, head=None, mitts="leather",
                                shape="male-young-averagemuscle-minweight")   # lean, under a capirote that hides the head
    import random; rnd = random.Random(1914)
    surf = ww1_uniform()
    human_ = bpy.data.objects["human"]
    module("Helmet")
    # the capirote (the owner's choice, 2026-11-03): a tall stiff cone of sackcloth over a cloth drape that hides the
    # whole face and falls to the shoulders (no eye holes: the face stays blank) and a painted cross on the front; a
    # seat-colour band round the foot of the cone, and a short shoulder mantle in the seat colour, so the side reads from
    # the game camera above
    c = Vector((hp.x, hp.y - 0.005, top.z))
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.075, radius2=0.0, depth=0.46, location=tuple(c + Vector((0, 0, 0.17))))
    o = bpy.context.object; o.scale = (1, 1.15, 1); add(o, "hood")
    # the base (owner, 2026-11-03: "metallic"): a rigid iron casing over the head and neck, flaring to a riveted rim
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.125, radius2=0.08, depth=0.22, location=tuple(c + Vector((0, 0, -0.17))))
    drape_ = bpy.context.object; drape_.scale = (1, 1.15, 1)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True); add(drape_, "iron")
    bpy.ops.mesh.primitive_torus_add(major_radius=0.127, minor_radius=0.01, major_segments=12, minor_segments=3, location=tuple(c + Vector((0, 0, -0.28))))
    bpy.context.object.scale = (1, 1.15, 1); add(bpy.context.object, "metal")
    for k in range(6):
        a_ = k * math.pi / 3 + math.pi / 6
        sphere(tuple(c + Vector((0.118 * math.sin(a_), -0.136 * math.cos(a_), -0.265))), 0.009, "brass", seg=4, rings=3)   # rivets
    bpy.ops.mesh.primitive_torus_add(major_radius=0.079, minor_radius=0.011, major_segments=10, minor_segments=3, location=tuple(c + Vector((0, 0, -0.055))))
    bpy.context.object.scale = (1, 1.15, 1); add(bpy.context.object, "accent")
    f = c + Vector((0, -0.118, -0.15))   # the painted cross on the drape, over where the face is: on the cloth's surface
    ok_, loc_, *_ = drape_.ray_cast(f + Vector((0, -0.5, 0)), Vector((0, 1, 0)))
    if ok_: f = loc_ + Vector((0, -0.004, 0))
    box(tuple(f), (0.022, 0.006, 0.12), "blood", rot=(-0.17, 0, 0)); box(tuple(f + Vector((0, 0.004, 0.02))), (0.08, 0.006, 0.022), "blood", rot=(-0.17, 0, 0))
    # a prayer strip pinned to the side of the cone under a wax seal
    ok_, s_, *_ = drape_.ray_cast(c + Vector((0.6, -0.03, -0.12)), Vector((-1, 0, 0)))   # lying on the drape's side
    s_ = (s_ if ok_ else c + Vector((0.11, -0.03, -0.12))) + Vector((0.012, 0, 0))
    box(tuple(s_), (0.006, 0.035, 0.11), "bone"); sphere(tuple(s_ + Vector((0.006, 0, 0.05))), 0.018, "blood", (0.5, 1, 1), seg=6, rings=4)
    module("Chest")
    ww1_webbing(surf)
    module("Back")
    # a canvas pack between the shoulders with a blanket rolled across the top
    box((0, 0.075, 1.18), (0.2, 0.09, 0.22), "cloth")
    bar((-0.13, 0.075, 1.32), (0.13, 0.075, 1.32), 0.04, "rags", sides=8)
    for x in (-0.08, 0.08): bar((x, 0.1, 1.33), (x, 0.1, 1.06), 0.012, "leather", sides=4)   # from under the mantle
    module("Hip")
    # a satchel on the right hip, a canteen on the left
    box((-0.22, -0.02, 0.86), (0.07, 0.14, 0.12), "leather"); box((-0.25, -0.02, 0.9), (0.012, 0.15, 0.06), "leather")
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.05, depth=0.03, location=(0.195, 0.02, 0.86), rotation=(0, math.pi/2, 0))
    add(bpy.context.object, "metal"); bar((0.195, 0.02, 0.91), (0.195, 0.02, 0.94), 0.012, "brass", sides=4)
    module("Weapon")
    hr, hl = hands["R"] + Vector((0, -0.04, -0.02)), hands["L"] + Vector((0, -0.04, -0.02))
    d = (hl - hr).normalized(); butt, muz = hr - d*0.24, hl + d*0.42
    side = d.cross(Vector((0, -1, 0))).normalized()
    bar(butt, hr + d*0.22, 0.028, "wood", sides=6); bar(hr + d*0.22, muz - d*0.1, 0.02, "wood", sides=6)
    bar(hr + d*0.08, muz, 0.01, "metal", sides=5)
    bar(hr + d*0.12, hr + d*0.12 + side*0.05 - Vector((0, 0.02, 0)), 0.008, "metal", sides=4)    # bolt handle
    bar(hr + d*0.02, hr + d*0.14, 0.016, "metal", sides=4)                                       # magazine and trigger guard
    bar(hr - d*0.18 + side*0.02, muz - d*0.12 + side*0.02, 0.004, "leather", sides=3)          # sling
    bar(muz, muz + d*0.22, 0.006, "iron", sides=4)
    module("Body")

def yeoman():
    # Yeoman (2026-11-03, first figure of the Principality, class A, 25 mm): an original WWI line infantryman in the
    # setting's genre, on the owner's WWI uniform references, not a copy of any miniature or artwork. The shared
    # service dress and webbing (ww1_uniform, ww1_webbing); a canvas gas hood over the whole head with two sealed iron
    # eyepieces (no glass: nothing reads as an eye) and a short filter snout, its hose down to the respirator bag on the
    # chest; a steel helmet painted in the seat colour; advancing, the rifle levelled at the hip with its bayonet fixed.
    # Modules: Helmet (gas hood fittings), Chest (webbing, respirator bag), Back (pack, entrenching tool), Hip
    # (haversack, water bottle), Weapon (rifle), Accent (the helmet).
    global BODY_BUDGET; BODY_BUDGET = 960
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
    import random; rnd = random.Random(1920)
    surf = ww1_uniform()
    module("Helmet")
    # the WWI gas mask over the canvas hood (owner's references, 2026-11-03), set a little forward so the hood never
    # shows through it; its spare drum carried in the tin on the chest
    fb = FACE_BOX; centre = fb["mid"] - fb["fwd"] * 0.06 - fb["up"] * 0.02
    ww1_gas_mask(fb, centre + fb["fwd"] * 0.012, fit=[o for o in MODULES["Body"] if o.data.materials and o.data.materials[0].name == "hood"])
    bag = surf(0.05, 1.22, out=0.045)
    # (the helmet below goes to the Accent node by its material)
    # the steel helmet: a shallow dome with a wide flat brim, painted in the seat colour so the camera above sees it
    hc = Vector((hp.x, hp.y - 0.012, top.z - 0.075))   # set down on the head, its dome over the crown and the mask's top
    sphere(tuple(hc + Vector((0, 0.0, 0.03))), 0.125, "accent", (1, 1.08, 0.72), seg=10, rings=6)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.165, radius2=0.12, depth=0.025, location=tuple(hc + Vector((0, 0, 0.03))))   # the brim, a shallow slope
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "accent")
    module("Chest")
    ww1_webbing(surf)
    box(tuple(bag), (0.11, 0.05, 0.09), "cloth")                                         # the respirator bag
    module("Back")
    box((0, 0.075, 1.18), (0.2, 0.09, 0.22), "cloth")                                    # pack, blanket roll on top
    bar((-0.13, 0.075, 1.32), (0.13, 0.075, 1.32), 0.04, "rags", sides=8)
    bar((0.06, 0.13, 1.1), (0.06, 0.13, 0.86), 0.012, "wood", sides=4); box((0.06, 0.14, 0.82), (0.07, 0.012, 0.08), "metal")   # entrenching tool
    module("Hip")
    box((-0.22, -0.02, 0.86), (0.07, 0.14, 0.12), "cloth"); box((-0.25, -0.02, 0.9), (0.012, 0.15, 0.06), "cloth")   # haversack
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.05, depth=0.03, location=(0.195, 0.02, 0.86), rotation=(0, math.pi/2, 0))
    add(bpy.context.object, "metal"); bar((0.195, 0.02, 0.91), (0.195, 0.02, 0.94), 0.012, "brass", sides=4)   # water bottle
    module("Weapon")
    # the rifle levelled at the hip: the butt back under the right forearm, the bayonet pointing ahead
    hr, hl = FISTS["R"][0], FISTS["L"][0]
    d = (hl - hr).normalized(); butt, muz = hr - d * 0.26, hl + d * 0.32
    side = d.cross(Vector((0, 0, 1))).normalized()
    bar(butt, hr + d * 0.2, 0.028, "wood", sides=6); bar(hr + d * 0.2, muz - d * 0.1, 0.02, "wood", sides=6)
    bar(hr + d * 0.06, muz, 0.01, "metal", sides=5)
    bar(hr + d * 0.1, hr + d * 0.1 + side * 0.05 + Vector((0, 0, 0.01)), 0.008, "metal", sides=4)   # bolt handle
    bar(hr, hr + d * 0.12 - Vector((0, 0, 0.03)), 0.016, "metal", sides=4)                           # magazine and trigger guard
    bar(muz, muz + d * 0.22, 0.006, "iron", sides=4)                                                  # bayonet
    module("Body")

def stigmatic_nun():
    # Stigmatic Nun (2026-11-03, second figure of docs/art-pipeline.md, class A): an original figure on the owner's
    # reference cues (a white, blood-spattered habit and veil, a barred iron face guard, plate over the waist and skirt,
    # bloody gauntlets, red shoulder tabs, a long blade), not a copy of either reference. No bare skin; the face guard
    # covers the blank head. Modules: Helmet (veil and face guard), Chest (breastplate band and straps), Hip (tassets and
    # a medical case), Weapon (pistol), Weapon2 (long blade), Accent (shoulder tabs, seat colour).
    def pose(r):
        human.aim(r, "spine03", P(0, -0.06, 1.4))
        human.reach(r, "R", P(-0.22, -0.16, 1.24), P(-0.2, -0.42, 1.34))
        human.reach(r, "L", P(0.24, -0.06, 1.0), P(0.22, -0.22, 0.9))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "habit", "thigh": "habit", "torso": "habit"}, head="mannequin",
                                mitts="gauntlet", shape="female-young-averagemuscle-minweight")
    import random; rnd = random.Random(1917)
    # the habit: long, falling to the feet, its hem dragged in the mud
    cloth_cone(0, 0, 0.98, 0.08, 0.15, 0.21, "habit", sy=0.85, ragged=0.03, rnd=rnd, tris=150, thick=0.01)
    # shoulder tabs in the seat colour, seen from above
    for x in (-0.17, 0.17):
        box((x, -0.02, 1.43), (0.09, 0.12, 0.012), "accent", rot=(0, 0.35 if x > 0 else -0.35, 0))
        box((x * 1.06, -0.075, 1.38), (0.085, 0.012, 0.1), "accent")
    module("Helmet")
    # the veil: a white hood over the crown, falling either side of the face to the chest and down the back
    c = Vector((hp.x, hp.y, top.z))
    veil = sphere(tuple(c + Vector((0, 0.01, -0.05))), 0.112, "habit", (1, 1.1, 0.9), seg=10, rings=6)
    # the hood: a drape from the crown to the shoulders, open at the front where the face guard is
    bpy.context.view_layer.update()
    cloth_cone(c.x, c.y + 0.01, c.z - 0.05, c.z - 0.35, 0.115, 0.2, "habit", sy=1.05, keep=FRONT(32), tris=48, thick=0.01, extra=[veil])
    # the face guard: an iron plate over the whole face with dark horizontal slots, a blood-red cross on the brow band
    # placed from the head's own frame (FACE_BOX: the eye line and its forward direction), so it covers the whole face
    fb = FACE_BOX; g = fb["mid"] + fb["fwd"] * 0.012 - fb["up"] * 0.03
    rot = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
    centre = fb["mid"] - fb["fwd"] * 0.06 - fb["up"] * 0.02   # about the middle of the head
    face = os.environ.get("NUN_FACE", "mask")   # the owner chose the votive mask (2026-11-03); visor, plate and sallet kept as options
    def front_arc(radius, depth, at, m, keep=0.35):
        # a band round the front of the head: a cylinder about the head's up axis with its back half removed
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=radius, depth=depth, end_fill_type='NOTHING', location=tuple(at))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(fb["up"])
        bpy.context.view_layer.update()
        import bmesh as _bm
        bm = _bm.new(); bm.from_mesh(o.data)
        mw = o.matrix_world
        _bm.ops.delete(bm, geom=[f for f in bm.faces if ((mw @ f.calc_center_median()) - at).normalized().dot(fb["fwd"]) < keep], context='FACES')
        bm.to_mesh(o.data); bm.free()
        o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01
        return add(o, m)
    if face == "visor":
        # a curved grille wrapped round the front of the head, its dark slots following the curve
        front_arc(0.092, 0.2, centre, "iron")
        for i in range(4): front_arc(0.097, 0.012, centre + fb["up"] * (0.05 - i * 0.034), "dark", keep=0.5)
    elif face == "mask":
        # a votive mask of glazed porcelain: a thin shell that follows the curve of the head, cut to a face-shaped oval
        # (broad at the brow, narrowing to the chin), with a dark cross-shaped slit and nothing else
        bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=10, radius=1, location=tuple(centre))
        m_ = bpy.context.object; m_.scale = (0.088, 0.094, 0.125); m_.rotation_euler = rot
        bpy.context.view_layer.update(); mw = m_.matrix_world
        import bmesh as _bm
        bm = _bm.new(); bm.from_mesh(m_.data)
        def outside(f):
            q = (mw @ f.calc_center_median()) - centre
            a_, u_, f_ = q.dot(fb["ax"]), q.dot(fb["up"]) + 0.012, q.dot(fb["fwd"])
            w = 0.072 if u_ > 0 else 0.072 - 0.25 * (-u_)   # narrower towards the chin
            return f_ < 0.02 or (a_ / max(w, 0.03)) ** 2 + (u_ / (0.105 if u_ > 0 else 0.118)) ** 2 > 1
        _bm.ops.delete(bm, geom=[f for f in bm.faces if outside(f)], context='FACES')
        bm.to_mesh(m_.data); bm.free()
        m_.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; bpy.ops.object.shade_smooth(); add(m_, "bone")
        # the cross slit, set into the surface of the mask
        # in short pieces that follow the curve, standing just proud of the surface so it reads as one clean cross
        def on(a_, u):
            k = 1 - (a_ / 0.088) ** 2 - (u / 0.125) ** 2
            return centre + fb["fwd"] * (0.094 * max(k, 0.0) ** 0.5 + 0.005) + fb["up"] * u + fb["ax"] * a_
        ups = [0.065, 0.03, -0.005, -0.04, -0.065]
        for u0, u1 in zip(ups, ups[1:]): bar(on(0, u0), on(0, u1), 0.007, "dark", sides=4)
        across = [-0.04, -0.013, 0.013, 0.04]
        for a0, a1 in zip(across, across[1:]): bar(on(a0, 0.03), on(a1, 0.03), 0.007, "dark", sides=4)
    elif face == "sallet":
        # a close iron sallet under the hood, covering the whole face, with one narrow dark eye slit
        sl = sphere(tuple(centre + fb["up"] * 0.01), 1, "plate", (0.098, 0.118, 0.13), seg=12, rings=7)
        sl.rotation_euler = rot
        box(tuple(centre + fb["fwd"] * 0.112 + fb["up"] * 0.035), (0.12, 0.01, 0.014), "dark", rot=rot)
        bar(centre + fb["fwd"] * 0.1 + fb["up"] * 0.12, centre + fb["fwd"] * 0.115 - fb["up"] * 0.08, 0.008, "plate", sides=4)   # a ridge down the face
    else:
        box(tuple(g), (0.14, 0.03, 0.2), "iron", rot=rot)
        for i in range(4): box(tuple(g + fb["fwd"] * 0.017 + fb["up"] * (0.05 - i * 0.034)), (0.105, 0.006, 0.014), "dark", rot=rot)
    # a blood-red cross painted on the front of the hood's crown, above the mask, lying on its curve
    cross = Vector((c.x, c.y - 0.07, c.z + 0.028)); tilt = (-0.65, 0, rot[2])
    box(tuple(cross), (0.013, 0.006, 0.036), "blood", rot=tilt); box(tuple(cross + Vector((0, 0.002, 0.004))), (0.036, 0.006, 0.012), "blood", rot=tilt)
    module("Chest")
    # a fitted plate corset round the waist and ribs: a band shrink-wrapped onto the torso so it follows the body,
    # standing just proud of the habit, and a strap up to the throat
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.16, depth=0.15, end_fill_type='NOTHING', location=(0, -0.01, 1.075))
    o = bpy.context.object
    import bmesh as _bm
    bm = _bm.new(); bm.from_mesh(o.data)
    _bm.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 0.01], cuts=1)
    bm.to_mesh(o.data); bm.free()
    sw = o.modifiers.new("fit", "SHRINKWRAP"); sw.target = bpy.data.objects["human"]; sw.wrap_method = 'PROJECT'; sw.use_project_x = sw.use_project_y = sw.use_project_z = False
    sw.use_negative_direction = True; sw.use_positive_direction = True; sw.offset = 0.014   # along each vertex's own outward line: onto the torso, never an arm
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="fit")   # now, before the body is joined
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "plate")
    bar((0, -0.13, 1.15), (0, -0.115, 1.45), 0.012, "leather", sides=4)
    box((0, -0.133, 1.3), (0.03, 0.01, 0.03), "brass")
    module("Hip")
    # layered plates (tassets) over the front of the skirt, and a medical case on the right hip
    for x in (-0.09, 0.09):
        for i in range(2):
            z = 0.9 - i * 0.12
            box((x, -0.145 - i * 0.014, z), (0.1, 0.012, 0.115), "plate", rot=(-0.16, 0, 0.1 if x > 0 else -0.1))
    box((-0.21, 0.0, 0.88), (0.06, 0.16, 0.13), "leather"); box((-0.24, 0.0, 0.88), (0.008, 0.06, 0.03), "metal")
    module("Weapon")
    # pistol in the right fist: the grip through the fist, the barrel above it pointing along the raised forearm
    fr, d = FISTS["R"]; up_g = (Vector((0, 0, 1)) - d * d.z).normalized()
    bar(fr - up_g * 0.04, fr + up_g * 0.03, 0.016, "wood", sides=4)
    bar(fr + up_g * 0.04 - d * 0.03, fr + up_g * 0.04 + d * 0.15, 0.014, "metal", sides=5)
    module("Weapon2")
    # a long blade held low, its point forward and up: a flat blade broad at the guard and tapering to a point, its back
    # edge sawn into teeth, blood dried on the steel; a crossguard, a wrapped grip and a round pommel
    # the grip runs through the left fist at right angles to the forearm, as a hand closes round it
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
    module("Body")

def ecclesiastic_prisoner():
    # Ecclesiastic Prisoner (2026-11-03, third figure of docs/art-pipeline.md, class A): an original figure on the owner's
    # reference cues, not a copy of the miniature or the art. A gaunt, stooped penitent, bare and grimed with blood, in a
    # rag loincloth with ropes bound round his body, his head locked in a rusted iron helm (breathing holes and spikes, no
    # face and no head modelled under it), his hands shackled in a wooden stocks board, iron cuffs at wrists and ankles,
    # and the Martyrdom Device, a finned aerial bomb, strapped across his back. Modules: Helmet (iron helm), Weapon (the
    # stocks board), Back (bomb and its strap), Hip (loincloth), Chest (ropes), Accent (the cloth band round the helm and
    # the loincloth's front flap).
    def pose(r):
        human.aim(r, "spine03", P(0, -0.28, 1.32)); human.aim(r, "neck01", P(0, -0.38, 1.44))
        human.reach(r, "R", P(-0.2, -0.16, 1.12), P(-0.07, -0.3, 0.98))
        human.reach(r, "L", P(0.2, -0.16, 1.12), P(0.07, -0.3, 0.98))
    hands, hp, top = human_body(pose, coat=False, dress={"foot": "flesh", "shin": "flesh", "thigh": "flesh", "torso": "flesh"}, head=None,
                                mitts="flesh", shape="male-young-minmuscle-minweight")
    import random; rnd = random.Random(1915)
    # the bowed back's surface, ray-cast from behind, so the ropes and the bomb sit on it
    human_ = bpy.data.objects["human"]
    def back_y(x, z):
        hit, loc, *_ = human_.ray_cast(Vector((x, 1.0, z)), Vector((0, -1, 0)))
        return loc.y if hit else None
    # iron cuffs at both ankles
    for x in (-0.1, 0.1):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.05, minor_radius=0.014, major_segments=8, minor_segments=4, location=(x, -0.02, 0.12))
        add(bpy.context.object, "iron")
    module("Hip")
    # the rag loincloth, its hem torn, and a front flap in the seat colour
    lc = cloth_cone(0, 0, 0.97, 0.75, 0.155, 0.17, "rags", sy=0.8, ragged=0.06, rnd=rnd, tris=50, thick=0.01, rows=5)
    cloth_panel([(-0.07, -0.135, 0.96), (0.07, -0.135, 0.96)], 0.34, "accent", extra=[lc], cols=2, rows=5, ragged=0.05, rnd=rnd, tris=None, bend=8.0)
    module("Chest")
    # ropes: a girdle at the waist and two crossing the chest over the shoulders
    bpy.ops.mesh.primitive_torus_add(major_radius=0.15, minor_radius=0.013, major_segments=10, minor_segments=3, location=(0, 0, 0.97))
    bpy.context.object.scale = (1, 0.82, 1); add(bpy.context.object, "rope")
    for x in (-1, 1):
        top_r = Vector((0.12 * x, back_y(0.12 * x, 1.28) + 0.008, 1.28))   # over the shoulder blade
        bar(top_r, (0.0, -0.14, 1.12), 0.01, "rope", sides=4); bar((0.0, -0.14, 1.12), (-0.13 * x, -0.09, 0.99), 0.01, "rope", sides=4)
    module("Helmet")
    # the iron helm, locked over the whole head: a rusted sphere bowed forward with the stoop, a riveted rim, breathing
    # holes in a ring round the face side and short spikes
    fb = FACE_BOX; centre = fb["mid"] - fb["fwd"] * 0.05 + fb["up"] * 0.01
    sphere(tuple(centre), 0.128, "rust", (1, 1.05, 1.02), seg=10, rings=6)
    # a cloth band in the seat colour bound round the helm's rim: the top of the figure, so the camera above sees it
    bpy.ops.mesh.primitive_torus_add(major_radius=0.122, minor_radius=0.018, major_segments=10, minor_segments=3, location=tuple(centre - fb["up"] * 0.075))
    o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(fb["up"]); add(o, "accent")
    for i, (a_, u_) in enumerate(((-0.05, 0.03), (0.05, 0.03), (0.0, -0.02), (-0.07, -0.04), (0.07, -0.04), (0.0, 0.07))):
        q = (fb["fwd"] * 0.96 + fb["ax"] * a_ / 0.13 + fb["up"] * u_ / 0.13).normalized()
        bar(centre + q * 0.124, centre + q * 0.136, 0.011, "dark", sides=4)
    for d_ in (fb["up"], (fb["up"] + fb["fwd"]).normalized(), (fb["up"] + fb["ax"]).normalized(), (fb["up"] - fb["ax"]).normalized(),
               (fb["fwd"] + fb["ax"] * 0.9).normalized(), (fb["fwd"] - fb["ax"] * 0.9).normalized()):
        tip = centre + d_ * 0.18
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.016, radius2=0.0, depth=0.06, location=tuple(centre + d_ * 0.15))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d_); add(o, "iron")
    module("Weapon")
    # the stocks board: a heavy wooden plank at the waist with both fists locked through it, iron bands and cuffs
    fr, _ = FISTS["R"]; fl, _ = FISTS["L"]; mid = (fr + fl) / 2
    across = (fl - fr).normalized()
    bar(mid - across * 0.17, mid + across * 0.17, 0.03, "wood", sides=4)
    for f in (fr, fl):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.034, minor_radius=0.011, major_segments=6, minor_segments=3, location=tuple(f))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(across); add(o, "iron")
    for t in (-0.13, 0.13): bar(mid + across * t - Vector((0, 0, 0.035)), mid + across * t + Vector((0, 0, 0.035)), 0.012, "iron", sides=4)
    module("Back")
    # the Martyrdom Device: a finned aerial bomb strapped along the bowed back, its tail over the right shoulder
    # Resting on the back: the body is ray-cast from behind along the bomb's line, and the bomb slides in until its
    # surface touches the most prominent point of the back (never sinking into it).
    r_ = 0.075
    # each end on the back at its own height (the lower back, the shoulder blades), so it leans with the stoop
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
        bar(tip_ - ax * 0.06 - v_ * 0.075, tip_ - ax * 0.06 + v_ * 0.075, 0.012, "bomb", sides=4)
        bar(tip_ - ax * 0.11 - v_ * 0.075, tip_ - ax * 0.01 - v_ * 0.075, 0.008, "bomb", sides=3)
        bar(tip_ - ax * 0.11 + v_ * 0.075, tip_ - ax * 0.01 + v_ * 0.075, 0.008, "bomb", sides=3)
    for t in (0.3,):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.08, minor_radius=0.01, major_segments=8, minor_segments=3, location=tuple(base_ + ax * t))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(ax); add(o, "rope")
    module("Body")

def war_prophet():
    # War Prophet (2026-11-03, fourth figure of docs/art-pipeline.md, class B, 32 mm): an original figure on the owner's
    # reference cues, not a copy of the miniature, the art or any paint job. A plate-armoured preacher in a long robe, a
    # closed pointed iron helm with a dark eye slit (no face, no head under it), the wooden cross frame on his back with
    # loudspeaker horns and bells, a scroll of scripture on his chest, and the anti-tank hammer: a long pole with a pick
    # head and a red explosive charge. Modules: Helmet, Chest (cuirass, pauldrons, scroll), Back (cross frame, horns,
    # bells), Hip (belt and tabard crosses), Weapon (anti-tank hammer), Accent (tabard panels and prayer pennants).
    global BODY_BUDGET; BODY_BUDGET = 1150
    def pose(r):
        human.reach(r, "R", P(-0.28, 0.02, 1.12), P(-0.3, -0.12, 1.22))
        human.reach(r, "L", P(0.25, 0.0, 1.08), P(0.27, -0.06, 0.88))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "robe", "thigh": "robe", "torso": "plate"}, head=None,
                                mitts="plate", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1918)
    # the long robe under the armour, its hem ragged
    robe = cloth_cone(0, 0, 0.98, 0.12, 0.18, 0.25, "robe", sy=0.84, ragged=0.06, rnd=rnd, tris=150, thick=0.012)
    # tabard panels in the seat colour, front and back, hanging from the belt
    fronts = []
    for y_top, out in ((-0.17, Vector((0, -1, 0))), (0.15, Vector((0, 1, 0)))):
        for x in (-0.075, 0.075):
            pn = cloth_panel([(x - 0.06, y_top, 1.0), (x + 0.06, y_top, 1.0)], 0.5, "accent", out=out, extra=[robe], cols=1, rows=4, tris=None, bend=30.0)
            if out.y < 0: fronts.append((x, pn))
    module("Chest")
    # a plate cuirass fitted to the torso, pauldrons of two lames on each shoulder, and the scroll with its seal
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.2, depth=0.42, end_fill_type='NOTHING', location=(0, -0.01, 1.22))
    o = bpy.context.object
    import bmesh as _bm
    bm = _bm.new(); bm.from_mesh(o.data)
    _bm.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 0.01], cuts=1)
    bm.to_mesh(o.data); bm.free()
    sw = o.modifiers.new("fit", "SHRINKWRAP"); sw.target = bpy.data.objects["human"]; sw.wrap_method = 'PROJECT'
    sw.use_project_x = sw.use_project_y = sw.use_project_z = False; sw.use_negative_direction = True; sw.use_positive_direction = True; sw.offset = 0.02
    bpy.context.view_layer.objects.active = o; bpy.ops.object.modifier_apply(modifier="fit")
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(o, "plate")
    for x in (-1, 1):
        for i, (z, sc) in enumerate(((1.47, 1.0), (1.41, 0.92))):
            # the top lame in the seat colour, a heraldic shoulder the camera above looks straight down on
            sphere((0.2 * x, -0.0, z), 1, "accent" if i == 0 else "plate", (0.11 * sc, 0.12 * sc, 0.06), seg=8, rings=4)
    box((0.08, -0.205, 1.3), (0.1, 0.012, 0.16), "bone", rot=(0.1, 0, 0))
    sphere((0.08, -0.215, 1.38), 0.02, "blood", (1, 0.5, 1), seg=6, rings=3)
    module("Hip")
    bpy.ops.mesh.primitive_torus_add(major_radius=0.18, minor_radius=0.016, major_segments=10, minor_segments=3, location=(0, -0.005, 1.0))
    bpy.context.object.scale = (1, 0.85, 1); add(bpy.context.object, "leather")
    for x, pn in fronts:   # a bone cross on each front panel, lying on it
        ok_, at_, *_ = pn.ray_cast(Vector((x, -0.8, 0.8)), Vector((0, 1, 0)))
        at_ = (at_ if ok_ else Vector((x, -0.205, 0.8))) - Vector((0, 0.006, 0))
        box(tuple(at_), (0.016, 0.006, 0.12), "bone"); box(tuple(at_ + Vector((0, 0, 0.02))), (0.06, 0.006, 0.016), "bone")
    module("Helmet")
    # the closed helm: a tall iron shell over the whole head rising to a point, an eye slit and a brass cross
    fb = FACE_BOX; centre = fb["mid"] - fb["fwd"] * 0.04
    rot = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
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
    import bmesh as _bm
    bm = _bm.new(); bm.from_mesh(me); _bm.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free(); me.update()   # facing out
    hm = bpy.data.objects.new("helm", me); scn.collection.objects.link(hm); add(hm, "plate")
    box(tuple(centre + fb["fwd"] * 0.085 + fb["up"] * 0.02), (0.055, 0.012, 0.018), "dark", rot=rot)
    lean = (-0.25, 0, rot[2])   # lying on the cone's slope
    box(tuple(centre + fb["fwd"] * 0.077 + fb["up"] * 0.16), (0.013, 0.01, 0.075), "brass", rot=lean)
    box(tuple(centre + fb["fwd"] * 0.074 + fb["up"] * 0.175), (0.045, 0.01, 0.013), "brass", rot=lean)
    module("Back")
    # the cross frame: a post behind the head, a crossbar above the shoulders with a loudspeaker horn at each end, a short
    # bar at the top, bells hung under the crossbar, and prayer pennants in the seat colour
    post_y = 0.17
    bar((0, post_y, 1.05), (0, post_y, 2.0), 0.024, "wood", sides=4)
    bar((-0.33, post_y, 1.74), (0.33, post_y, 1.74), 0.022, "wood", sides=4)
    bar((-0.09, post_y, 1.92), (0.09, post_y, 1.92), 0.018, "wood", sides=4)
    for x in (-1, 1):
        mouth = Vector((0.36 * x, post_y - 0.05, 1.8))
        bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.07, radius2=0.022, depth=0.15, end_fill_type='NOTHING', location=tuple(mouth))
        o = bpy.context.object; o.rotation_mode = 'QUATERNION'
        o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(Vector((-0.6 * x, 0.6, -0.5)).normalized())
        o.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; add(o, "brass")
        bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.04, radius2=0.018, depth=0.06, location=(0.2 * x, post_y, 1.66)); add(bpy.context.object, "iron")
        box((0.27 * x, post_y + 0.01, 1.6), (0.035, 0.008, 0.2), "accent")
    module("Weapon")
    # the anti-tank hammer, held upright in the right fist: a long pole, a pick head and a red explosive charge with a
    # white cross
    fr, _ = FISTS["R"]
    low, high = Vector((fr.x, fr.y, 0.05)), Vector((fr.x, fr.y, 1.9))
    bar(low, high, 0.016, "wood", sides=5)
    bar(high - Vector((0, 0, 0.03)), high + Vector((0, -0.2, -0.06)), 0.014, "iron", sides=3)
    bar(high - Vector((0, 0, 0.03)), high + Vector((0, 0.08, 0.0)), 0.012, "iron", sides=3)
    bar(high, high + Vector((0, 0, 0.07)), 0.01, "iron", sides=3)
    ch = high + Vector((0, -0.07, -0.16))
    bar(ch + Vector((0, 0, 0.07)), ch - Vector((0, 0, 0.07)), 0.034, "charge", sides=8)
    box(tuple(ch + Vector((0, -0.035, 0.0))), (0.012, 0.006, 0.05), "bone"); box(tuple(ch + Vector((0, -0.035, 0.01))), (0.04, 0.006, 0.012), "bone")
    module("Body")

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
    # Modules: Helmet (hat, hood, mask, respirator), Chest (icons, hose, belt), Back (cape), Weapon (flail), Weapon2
    # (shield), Accent (the faction's shoulder mantle and the hat's top).
    global BODY_BUDGET; BODY_BUDGET = 1000
    def pose(r):
        human.reach(r, "R", P(-0.3, -0.06, 1.3), P(-0.4, -0.18, 1.42))
        human.reach(r, "L", P(0.25, -0.1, 1.06), P(0.17, -0.3, 1.1))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "robe", "thigh": "robe", "torso": "mail"}, head=None,
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1919)
    # the long robe, its hem ragged
    robe = cloth_cone(0, 0, 0.98, 0.1, 0.17, 0.24, "robe", sy=0.84, ragged=0.07, rnd=rnd, tris=120, thick=0.012)
    # the pilgrims' shoulder mantle in the seat colour
    cloth_cone(hp.x, hp.y - 0.01, 1.47, 1.35, 0.12, 0.255, "accent", sy=0.84, tris=40, thick=0.01)
    module("Back")
    # the cape: falling from the shoulders to the ground behind, open at the front
    import bmesh as _bm
    cloth_cone(0, 0.03, 1.42, 0.095, 0.12, 0.28, "cloth", sy=0.82, keep=FRONT(80), ragged=0.08, rnd=rnd, tris=90, mono=False, thick=0.012,
               extra=[robe], rows=10)
    module("Helmet")
    # the hood round the head, open at the front; the porcelain mask; the respirator; the tall hat and its cross
    fb = FACE_BOX; crown = fb["mid"] + fb["up"] * 0.1 - fb["fwd"] * 0.05   # the top of the skull
    c = crown
    cloth_cone(c.x, c.y + 0.01, c.z - 0.005, c.z - 0.2, 0.1, 0.13, "cloth", sy=1.05, keep=FRONT(28), tris=40, thick=0.01,
               stand=(fb["mid"] - fb["fwd"] * 0.09, 0.08))   # centred on the (absent) head
    rot = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
    centre = fb["mid"] - fb["fwd"] * 0.06 - fb["up"] * 0.02
    snout = gas_mask(fb, centre)
    HOSE_START = snout   # where the ribbed hose leaves the mask (used in the chest module)
    ht = crown - Vector((0, 0, 0.03))   # the hat sits down over the crown
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.09, depth=0.25, location=tuple(ht + Vector((0, 0, 0.125))))
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "dark")
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.091, depth=0.012, location=tuple(ht + Vector((0, 0, 0.25))))
    bpy.context.object.scale = (1, 1.08, 1); add(bpy.context.object, "accent")
    cx = ht + Vector((0, -0.098, 0.13))   # an Orthodox cross on the front of the hat: an upright, a short bar, a long bar, a slanted foot
    # raised gilt, larger, so it reads on the black hat
    cx = cx + Vector((0, -0.006, 0))
    box(tuple(cx), (0.018, 0.016, 0.2), "gilt"); box(tuple(cx + Vector((0, 0, 0.068))), (0.05, 0.016, 0.014), "gilt")
    box(tuple(cx + Vector((0, 0, 0.025))), (0.09, 0.016, 0.017), "gilt"); box(tuple(cx - Vector((0, 0, 0.055))), (0.065, 0.016, 0.014), "gilt", rot=(0, 0.4, 0))
    module("Chest")
    # gilded icon plaques on the chest and one on the belt, the respirator hose down to a canister, a belt with pouches
    for x, z, w, hgt in ((-0.08, 1.24, 0.07, 0.09), (0.08, 1.22, 0.07, 0.1), (-0.02, 1.12, 0.06, 0.08), (0.0, 0.88, 0.1, 0.16)):   # below the mantle
        y = -0.17 if z > 1 else -0.215
        box((x, y, z), (w + 0.012, 0.012, hgt + 0.012), "gilt"); box((x, y - 0.007, z), (w, 0.006, hgt), "icon")
    hose = [HOSE_START - fb["up"] * 0.02, Vector((0.05, -0.2, 1.36)), Vector((0.07, -0.21, 1.12))]
    pts = [hose[0] + (hose[1] - hose[0]) * (i / 5) for i in range(5)] + [hose[1] + (hose[2] - hose[1]) * (i / 5) for i in range(6)]
    for i, (a_, b_) in enumerate(zip(pts, pts[1:])): bar(a_, b_, 0.015 if i % 2 else 0.011, "rubber", sides=5)   # ribbed
    for end_ in (hose[0], hose[2]): sphere(tuple(end_), 0.017, "metal", seg=5, rings=3)                    # couplings
    bar(Vector((0.07, -0.21, 1.1)), Vector((0.07, -0.21, 1.04)), 0.03, "iron", sides=6)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.17, minor_radius=0.016, major_segments=8, minor_segments=3, location=(0, -0.005, 0.99))
    bpy.context.object.scale = (1, 0.84, 1); add(bpy.context.object, "leather")
    box((-0.12, -0.14, 0.95), (0.06, 0.04, 0.07), "leather")
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
    # the reliquary shield: a tall wooden board held before the left side, a crucifix on its face, severed hands nailed
    # round it, and candles along its top edge
    fl, _ = FISTS["L"]
    sc = fl + Vector((0.02, -0.06, 0.0))
    box(tuple(sc), (0.27, 0.03, 0.56), "wood")
    front = sc.y - 0.018
    box((sc.x, front, sc.z + 0.02), (0.022, 0.012, 0.36), "bone"); box((sc.x, front, sc.z + 0.1), (0.17, 0.012, 0.022), "bone")
    for hx, hz in ((-0.09, 0.14), (0.09, 0.1), (-0.08, -0.1), (0.08, -0.16)):
        sphere((sc.x + hx, front - 0.01, sc.z + hz), 1, "flesh", (0.034, 0.014, 0.045), seg=5, rings=3)
    for cx_ in (-0.07, 0.07):
        bar(Vector((sc.x + cx_, sc.y, sc.z + 0.28)), Vector((sc.x + cx_, sc.y, sc.z + 0.33)), 0.01, "bone", sides=4)
        sphere((sc.x + cx_, sc.y, sc.z + 0.345), 0.009, "glow", seg=4, rings=3)
    module("Body")

def reliquary_walker():
    # Reliquary Walker (2026-11-03, sixth figure of docs/art-pipeline.md, class E, 60 mm): an original figure on the
    # owner's reference cues, not a copy of the miniature, the art or any paint job. A squat armoured shrine on stumpy
    # legs with no head: a gothic tabernacle and a tall spiked spire rise from between the shoulders. Huge rounded
    # pauldrons, two exhaust stacks, a winged emblem on the chest above a dark slit, a mail skirt with a hanging bell and a
    # plucked dove, two arched grave-slab plaques over the thighs; a boiler and chain curtains on the back. Right hand: a
    # long bonebreaker mace, its flanged head at the ground. Left forearm: a spiked Catherine wheel with a martyr bound to
    # it (a blank mannequin head, no face). Modules: Helmet (tabernacle and spire), Chest (emblem, skirt, plaques, bell, dove),
    # Back (boiler, stacks, chains), Weapon (mace), Weapon2 (wheel and martyr), Accent (pauldron trims, spire band).
    # The owner raised its budget (2026-11-03) to 6,000 triangles, then 8,000: rounder armour, gables, a chain fringe, a finer
    # wheel, and a MakeHuman martyr on it.
    global BODY_BUDGET, TEX_SIZES; BODY_BUDGET = 2800
    TEX_SIZES = {"Body": 256, "Helmet": 128, "Chest": 128, "Back": 128, "Weapon": 128, "Weapon2": 256}   # the martyr needs the wheel's 256
    def rbox(loc, size, m, bev=0.03, rot=(0, 0, 0)):
        o = box(loc, size, m, rot=rot)
        if bev:
            b_ = o.modifiers.new("bev", "BEVEL"); b_.width = bev; b_.segments = 3 if bev >= 0.1 else (2 if bev >= 0.04 else 1); b_.limit_method = 'NONE'
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
    F = -1   # the front is -y
    # --- Body: legs, hips, the armoured hull, pauldrons and arms
    for s in (-1, 1):
        x = 0.24 * s
        rbox((x, -0.05, 0.07), (0.26, 0.4, 0.14), "hull", 0.03)                          # the foot, a broad iron sabaton
        rbox((x, -0.2, 0.05), (0.22, 0.12, 0.1), "hull", 0.02)                            # its toe cap
        rbox((x, -0.04, 0.27), (0.26, 0.26, 0.28), "hull", 0.04)                          # the greave
        for z in (0.2, 0.32):
            box((x, -0.04, z), (0.28, 0.28, 0.035), "iron")                                  # its bands
        rbox((x, 0.0, 0.52), (0.26, 0.3, 0.26), "hull", 0.04)                             # the thigh
    rbox((0, 0.0, 0.72), (0.6, 0.44, 0.2), "hull", 0.05)                                   # the hips
    rbox((0, 0.0, 0.92), (0.66, 0.52, 0.24), "hull", 0.06)                                 # the belly
    rbox((0, 0.0, 1.16), (0.78, 0.62, 0.36), "hull", 0.12)                                 # the hull, its edges rounded
    rbox((0, 0.03, 1.35), (0.5, 0.48, 0.1), "hull", 0.04)                                  # the collar under the tabernacle
    rbox((0, -0.29, 1.2), (0.56, 0.12, 0.28), "hull", 0.05, rot=(math.radians(-8), 0, 0))  # the breastplate, sloped back
    rbox((0, -0.29, 0.95), (0.46, 0.1, 0.16), "hull", 0.04)                               # the lower faceplate
    box((0, -0.35, 1.045), (0.36, 0.04, 0.035), "dark")                                    # the anchorite's dark slit
    for s in (-1, 1):
        x = 0.47 * s
        x = 0.5 * s
        rbox((x, 0.0, 1.22), (0.42, 0.6, 0.44), "hull", 0.14)                             # the pauldron
        for k, z in enumerate((0.96, 0.85)):
            rbox((x * (1.02 + 0.03 * k), 0.0, z), (0.38 - 0.03 * k, 0.56 - 0.04 * k, 0.12), "hull", 0.035)   # its lames
        rbox((x, 0.0, 1.445), (0.3, 0.5, 0.025), "accent", 0.0)                           # the seat colour on its top
        rbox((x + 0.215 * s, 0.0, 1.22), (0.02, 0.5, 0.06), "iron", 0.0)                  # an iron rim
    # the right arm hangs with the mace; the left holds the wheel out before it
    cyl((-0.58, -0.02, 0.78), 0.1, 0.2, "metal", n=8)                                     # elbow piston
    rbox((-0.58, -0.06, 0.6), (0.22, 0.24, 0.24), "hull", 0.05)                           # right vambrace
    for z in (0.53, 0.67):
        box((-0.58, -0.06, z), (0.24, 0.26, 0.03), "iron")                         # its ribs
    rbox((-0.58, -0.1, 0.43), (0.2, 0.22, 0.14), "hull", 0.04)                            # right fist
    cyl((0.58, -0.02, 0.78), 0.1, 0.2, "metal", n=8)
    rbox((0.62, -0.2, 0.68), (0.22, 0.32, 0.22), "hull", 0.05)                            # left vambrace, forward
    for y in (-0.28, -0.14):
        box((0.62, y, 0.68), (0.24, 0.03, 0.24), "iron")                           # its ribs
    # --- Helmet: the tabernacle and its spire, where a head would be
    module("Helmet")
    rbox((0, 0.02, 1.49), (0.34, 0.34, 0.26), "stone", 0.02)
    for s in (-1, 1):
        for t in (-1, 1):
            cyl((0.17 * s, 0.02 + 0.17 * t, 1.52), 0.03, 0.34, "stone", n=4)               # corner piers
            cone((0.17 * s, 0.02 + 0.17 * t, 1.75), 0.035, 0.0, 0.14, "stone", n=4)      # pinnacles
    for v_ in ((0, -1), (0, 1), (-1, 0), (1, 0)):                                         # a pointed arch window each side
        for k in (-1, 0, 1):
            c_ = Vector((0.17 * v_[0], 0.02 + 0.17 * v_[1], 1.48))
            along = Vector((abs(v_[1]), abs(v_[0]), 0)) * 0.09 * k
            box(tuple(c_ + along + Vector((0, 0, -0.01))), (0.045 if v_[0] == 0 else 0.03, 0.03 if v_[0] == 0 else 0.045, 0.14), "dark")
            cone(c_ + along + Vector((0, 0, 0.08)), 0.03, 0.0, 0.06, "dark", n=4, axis=Vector((0, 0, 1)))
    rbox((0, 0.02, 1.635), (0.38, 0.38, 0.05), "stone", 0.015)                               # the cornice
    for k in range(4):                                                                    # a gable on each side
        a_ = k * math.pi / 2; n_ = Vector((math.sin(a_), -math.cos(a_), 0)); t_ = Vector((math.cos(a_), math.sin(a_), 0))
        c_ = Vector((0, 0.02, 1.66)) + n_ * 0.185
        verts = [c_ - t_ * 0.17, c_ + t_ * 0.17, c_ + Vector((0, 0, 0.17))]
        verts += [v - n_ * 0.03 for v in verts]
        me = bpy.data.meshes.new("gable"); me.from_pydata([tuple(v) for v in verts], [], [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)])
        me.validate(); me.update(); g = bpy.data.objects.new("gable", me); scn.collection.objects.link(g); add(g, "stone")
        cone(c_ + Vector((0, 0, 0.2)), 0.02, 0.0, 0.08, "stone", n=4)                       # its finial
        box(tuple(c_ + n_ * 0.005 + Vector((0, 0, 0.06))), (0.04 if abs(n_.y) > 0.5 else 0.012, 0.012 if abs(n_.y) > 0.5 else 0.04, 0.06), "dark")   # a rose slit
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.18, depth=0.03, location=(0, 0.02, 1.67))
    o = bpy.context.object; o.rotation_euler = (0, 0, math.pi / 8); add(o, "accent")       # the seat colour round the spire's foot
    spire = cone((0, 0.02, 2.04), 0.145, 0.0, 0.72, "rust", n=12)
    spire.rotation_quaternion = spire.rotation_quaternion @ Euler((0, 0, math.pi / 8)).to_quaternion()
    for z, r in ((1.8, 0.12), (1.98, 0.084)):                                            # iron bands
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=r, depth=0.025, location=(0, 0.02, z))
        o = bpy.context.object; o.rotation_euler = (0, 0, math.pi / 8); add(o, "iron")
    for i in range(8):                                                                    # spikes up its ribs
        ang = i * math.pi / 4
        out = Vector((math.cos(ang), math.sin(ang), 0.35)).normalized()
        for k, z in enumerate((1.78, 1.94, 2.1, 2.24)):
            if (i + k) % 2: continue
            r = 0.145 * (1 - (z - 1.68) / 0.72)
            cone(Vector((r * math.cos(ang), 0.02 + r * math.sin(ang), z)) + out * 0.02, 0.012, 0.0, 0.06, "iron", n=4, axis=out)
    # --- Chest: the winged emblem, the mail skirt, plaques, bell and the plucked dove
    module("Chest")
    em = Vector((0, -0.375, 1.2))
    cyl(em, 0.05, 0.02, "gilt", n=8, axis=Vector((0, 1, 0)))
    for s in (-1, 1):
        for k, (dx, dz, ln, a_) in enumerate(((0.1, 0.045, 0.16, 0.45), (0.12, 0.015, 0.15, 0.2), (0.12, -0.02, 0.13, -0.05), (0.1, -0.05, 0.1, -0.3))):
            box(tuple(em + Vector((dx * s, 0.005, dz))), (ln, 0.015, 0.03), "gilt", rot=(0, -a_ * s, 0))
    box(tuple(em + Vector((0, -0.01, 0.0))), (0.018, 0.012, 0.09), "dark"); box(tuple(em + Vector((0, -0.01, 0.01))), (0.06, 0.012, 0.016), "dark")
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.38, radius2=0.33, depth=0.56, end_fill_type='NOTHING', location=(0, 0.0, 0.5))
    sk = bpy.context.object; sk.scale = (1, 0.75, 1)
    for v in sk.data.vertices:
        if v.co.z < 0 and (v.index // 1) % 2: v.co.z += 0.05
    sk.modifiers.new("sol", "SOLIDIFY").thickness = 0.015; add(sk, "mail")
    for s in (-1, 1):                                                                     # arched grave-slab plaques
        x = 0.21 * s
        rbox((x, -0.33, 0.47), (0.24, 0.045, 0.36), "stone", 0.012)
        cyl((x, -0.33, 0.65), 0.12, 0.045, "stone", n=10, axis=Vector((0, 1, 0)))
        box((x, -0.36, 0.5), (0.03, 0.012, 0.24), "iron"); box((x, -0.36, 0.56), (0.13, 0.012, 0.03), "iron")
        bar(Vector((x, -0.33, 0.86)), Vector((x, -0.33, 0.76)), 0.008, "iron", sides=4)                # hung from the hull
    for i in range(7):                                                                    # chain strands under the faceplate
        x = -0.3 + i * 0.1
        if abs(x) < 0.06: continue
        for j in range(6):
            z = 0.86 - j * 0.07 - (0.03 if i % 2 else 0)
            box((x, -0.36 + 0.005 * j, z), (0.022, 0.008, 0.05) if j % 2 else (0.008, 0.022, 0.05), "iron")
    # the plucked dove, hung by its feet from two chains under the slit; a bell below it
    bar(Vector((-0.05, -0.36, 0.95)), Vector((-0.02, -0.4, 0.8)), 0.007, "iron", sides=4)
    bar(Vector((0.05, -0.36, 0.95)), Vector((0.02, -0.4, 0.8)), 0.007, "iron", sides=4)
    sphere((0, -0.41, 0.72), 1, "pale", (0.06, 0.05, 0.1), seg=8, rings=6)
    for s in (-1, 1):
        o = box((0.07 * s, -0.41, 0.7), (0.1, 0.012, 0.06), "pale", rot=(0, 0.6 * s, 0))  # limp wings
    bar(Vector((0, -0.38, 0.78)), Vector((0, -0.4, 0.58)), 0.006, "iron", sides=4)
    cone((0, -0.4, 0.53), 0.05, 0.02, 0.08, "brass", n=8)
    # --- Back: the boiler, two exhaust stacks, chain curtains with parchment tags
    module("Back")
    cyl((0, 0.36, 1.05), 0.22, 0.14, "iron", n=12, axis=Vector((0, 1, 0)))
    cyl((0, 0.44, 1.05), 0.12, 0.03, "metal", n=10, axis=Vector((0, 1, 0)))                # the hatch
    bar(Vector((-0.1, 0.46, 1.05)), Vector((0.1, 0.46, 1.05)), 0.012, "brass", sides=4)   # its wheel bar
    for s in (-1, 1):
        x = 0.24 * s
        x = 0.3 * s
        cyl((x, 0.2, 1.5), 0.06, 0.36, "iron", n=8)
        cyl((x, 0.2, 1.69), 0.078, 0.04, "metal", n=8)                                    # the stack's lip
        cyl((x, 0.2, 1.71), 0.05, 0.02, "dark", n=8)
        bar(Vector((x, 0.32, 1.2)), Vector((0.08 * s, 0.4, 0.92)), 0.03, "metal", sides=6)   # pipes into the boiler
        sheet(0.3 * s, 0.68 * s, 0.31, 0.3, 1.02, "mail", sag=0.05, cols=5, rows=3)       # chain curtains under the pauldrons
        for z in (0.7, 0.55):
            box((0.5 * s, 0.35, z), (0.06, 0.012, 0.08), "bone")                         # parchment tags
    # --- Weapon: the bonebreaker mace, its haft through the right fist, its flanged head at the ground
    module("Weapon")
    fist = Vector((-0.58, -0.12, 0.43)); tip = Vector((-0.8, -0.6, 0.07)); d = (tip - fist).normalized()
    bar(fist - d * 0.22, tip, 0.026, "wood", sides=6)
    sphere(tuple(fist - d * 0.24), 0.04, "iron", seg=6, rings=4)                          # the pommel
    bar(fist - d * 0.14, fist + d * 0.12, 0.032, "leather", sides=6)                       # the grip
    for t in (0.25, 0.55):
        bar(fist + (tip - fist) * t - d * 0.02, fist + (tip - fist) * t + d * 0.02, 0.034, "iron", sides=6)   # iron bands
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
    module("Body")

def sanctified_giant():
    # Sanctified Giant (2026-11-03, seventh figure of docs/art-pipeline.md, class C, 40 mm): an original figure on the
    # owner's reference cues, not a copy of the miniature, the art or any paint job. A hulking, stooped brute, bare to the
    # waist, pale and bloodied, iron crosses nailed into both shoulders; his head under a cloth hood with a long iron
    # cross nailed down its front (no face, no head under it: the hood and cross are his capirote and mask); an iron
    # vambrace on the left forearm; a ragged skirt with a rope belt and skulls; an arched reliquary plaque on his back; a
    # long polearm held across the body, a cluster of chains, a bell, spiked balls and skulls hanging from its head.
    # Modules: Helmet (hood and cross), Chest (nailed crosses, vambrace), Hip (skirt, belt, skulls), Back (plaque),
    # Weapon (polearm and its chains), Accent (the hood).
    # The pose follows the references (owner, 2026-11-03): striding, the left leg forward and the right back on its toes,
    # hunched far forward with the head thrust low between the shoulders, the polearm held high across the body by the
    # right fist at the shoulder and the armoured left fist low at the hip, its chains hanging out at the right. The owner
    # allows larger units a larger budget: about 3,800 triangles, most of it in the body.
    global BODY_BUDGET, TEX_SIZES; BODY_BUDGET = 1750; TEX_SIZES = {"Body": 256, "Weapon": 128}   # class C: a 256 px body
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
    def hit(origin, d_):
        ok, loc, nrm, _ = human_.ray_cast(Vector(origin), Vector(d_))
        return (loc, nrm) if ok else (None, None)
    def skull(at, r=0.045, rot=0.0):
        sphere(tuple(at), 1, "bone", (r, r * 1.1, r * 1.05), seg=6, rings=4)
        box(tuple(at + Vector((0, -r * 0.55, -r * 0.85))), (r * 1.1, r * 0.8, r * 0.5), "bone", rot=(0, 0, rot))   # the jaw
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
    for p_ in (face + fb["up"] * 0.2, face + fb["up"] * 0.1 + fb["ax"] * 0.065, face + fb["up"] * 0.1 - fb["ax"] * 0.065, face - fb["up"] * 0.06):
        sphere(tuple(p_ + fb["fwd"] * 0.016), 0.012, "brass", seg=4, rings=3)                 # nail heads
    module("Chest")
    # iron crosses nailed into both shoulders, on the skin
    for s in (-1, 1):
        loc, nrm = hit((0.7 * s, -0.06, 1.8), Vector((-s, 0, -0.75)).normalized())   # down onto the shoulder
        if loc is None: continue
        up_ = (Vector((0, 0, 1)) - nrm * nrm.z).normalized(); side_ = nrm.cross(up_)
        c_ = loc + nrm * 0.012
        bar(c_ - up_ * 0.07, c_ + up_ * 0.08, 0.012, "iron", sides=4); bar(c_ + up_ * 0.03 - side_ * 0.045, c_ + up_ * 0.03 + side_ * 0.045, 0.012, "iron", sides=4)
        sphere(tuple(c_ + up_ * 0.03 + nrm * 0.012), 0.01, "brass", seg=4, rings=3)
    # an iron vambrace round the left forearm, riveted bands
    fl, dl = FISTS["L"]
    bar(fl - dl * 0.05, fl - dl * 0.26, 0.056, "iron", sides=8)
    for t in (0.07, 0.24):
        bar(fl - dl * (t - 0.012), fl - dl * (t + 0.012), 0.062, "metal", sides=8)
    module("Hip")
    # the ragged skirt from the waist to the shins, a rope belt, skulls and a little grave plaque hung on it
    # split at the front left, where the striding leg comes out: no cloth in that wedge below the upper thigh
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
    belt = [Vector(verts[i]) + (Vector(verts[i]) - Vector((0, yc, verts[i][2]))).normalized() * 0.012 for i in range(0, n_, 2)]
    for a_, b_ in zip(belt, belt[1:] + belt[:1]): bar(a_, b_, 0.016, "rope", sides=4)   # the rope belt, on the waist
    skull(Vector((0.12, -0.2, 0.88)), rot=0.2); skull(Vector((-0.17, -0.12, 0.86)), 0.04, rot=-0.4)
    bar(Vector((0.12, -0.18, 0.99)), Vector((0.12, -0.2, 0.93)), 0.006, "rope", sides=3); bar(Vector((-0.17, -0.11, 0.99)), Vector((-0.17, -0.12, 0.9)), 0.006, "rope", sides=3)
    box((0.2, 0.08, 0.84), (0.012, 0.09, 0.14), "stone")
    module("Back")
    # the reliquary plaque on the back: an arched slab rising behind the hood, crossed bones and a skull on its face
    # built upright at the origin, then leant with the bowed back (two points of it, ray-cast from behind)
    n0 = len(MODULES["Back"])
    box((0, 0.04, 0.13), (0.3, 0.05, 0.42), "wood")
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.15, depth=0.05, location=(0, 0.04, 0.34))
    o = bpy.context.object; o.rotation_euler = (math.pi / 2, 0, 0); add(o, "wood")
    box((0, 0.068, 0.2), (0.24, 0.012, 0.42), "icon")                                         # the inset panel
    for s in (-1, 1):
        bar(Vector((-0.08 * s, 0.08, 0.08)), Vector((0.08 * s, 0.08, 0.26)), 0.012, "bone", sides=4)   # crossed bones
    skull(Vector((0, 0.09, 0.32)), 0.04)
    box((0, 0.03, -0.08), (0.32, 0.02, 0.03), "leather")                                      # its strap
    p1, _ = hit((0.0, 1.0, 1.05), (0, -1, 0)); p2, _ = hit((0.0, 1.0, 1.4), (0, -1, 0))
    p1 = p1 or Vector((0, 0.15, 1.05)); p2 = p2 or Vector((0, 0.1, 1.4))
    up_ = (p2 - p1).normalized(); th = math.atan2(-up_.y, up_.z)
    Mp = Matrix.Translation(p1 + (p2 - p1) * 0.45 + Vector((0, 0.05, 0))) @ Matrix.Rotation(th, 4, 'X')   # clear of the hood's collar
    for o in MODULES["Back"][n0:]:
        bpy.context.view_layer.update(); o.matrix_world = Mp @ o.matrix_world
    module("Weapon")
    # the polearm through both fists, its head up and out on the right; a long blade at the top
    fr, _ = FISTS["R"]; u = (fr - fl).normalized()
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
    for e in ends[6:]:
        bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.04, radius2=0.02, depth=0.06, location=tuple(e - Vector((0, 0, 0.03))))
        add(bpy.context.object, "brass")                                                      # small bells
    b0 = ends[0]
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=0.085, radius2=0.04, depth=0.13, location=tuple(b0 - Vector((0, 0, 0.07))))
    add(bpy.context.object, "brass")                                                          # the bell
    sphere(tuple(b0 - Vector((0, 0, 0.005))), 0.042, "brass", seg=8, rings=4)
    for e, r in ((ends[1], 0.04), (ends[2], 0.038), (ends[4], 0.032)):                         # spiked balls
        sphere(tuple(e - Vector((0, 0, r))), r, "iron", seg=6, rings=4)
        for v_ in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, -1))):
            bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.01, radius2=0.0, depth=0.03, location=tuple(e - Vector((0, 0, r)) + v_ * (r + 0.012)))
            o = bpy.context.object; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(v_); add(o, "iron")
    skull(ends[3] - Vector((0, 0, 0.04)), 0.035); skull(ends[5] - Vector((0, 0, 0.04)), 0.03)
    sphere(tuple(hang - Vector((0, 0, 0.05))), 0.03, "bone", seg=6, rings=4)                  # a bone charm at the top
    module("Body")

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

{"rifleman": rifleman, "skirmisher": skirmisher, "shield": shield, "standard": standard, "pilgrim": pilgrim, "trench-pilgrim": trench_pilgrim, "stigmatic-nun": stigmatic_nun, "ecclesiastic-prisoner": ecclesiastic_prisoner, "war-prophet": war_prophet, "castigator": castigator, "reliquary-walker": reliquary_walker, "sanctified-giant": sanctified_giant, "yeoman": yeoman}[role]()
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
    img = bpy.data.images.new(f"{role}-{name}", size, size)
    for m in o.data.materials:
        tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; m.node_tree.nodes.active = tn
    scn.cycles.samples = 8; bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}); col = list(img.pixels)
    scn.cycles.samples = 32; bpy.ops.object.bake(type='AO'); ao = list(img.pixels)
    px = []
    for i in range(0, len(col), 4):
        for c in range(3):
            v = (col[i+c]*(0.55+0.45*ao[i+c])) ** (1/2.2); px.append((round(v*31)/31) ** 2.2)   # 15-bit colour
        px.append(1.0)
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
