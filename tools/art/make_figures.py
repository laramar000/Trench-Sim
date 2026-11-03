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
FIGURES = ("rifleman", "skirmisher", "shield", "standard", "trench-pilgrim", "stigmatic-nun", "ecclesiastic-prisoner", "war-prophet", "castigator")   # "pilgrim" is built with --figure until a unit uses it
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
 "mannequin": ("cfc4b0", 0.03)}.items()}   # the mannequin head: smooth pale plaster, no face (2026-11-03)
M.update({"habit": spattered("habit", "d8d0bc", spots=0.36, scale=7.0), "plate": mat("plate", "7a7c80", 0.3, 18),
          "gauntlet": spattered("gauntlet", "7a1a14", spots=0.5, scale=40),
          "blade": spattered("blade", "9a9ca0", spots=0.42, scale=9),
          "flesh": spattered("flesh", "9c7a6a", spots=0.36, scale=9),      # the Ecclesiastic Prisoner (2026-11-03)
          "rust": mat("rust", "6a4630", 0.35, 14), "bomb": mat("bomb", "4a5430", 0.2, 10),
          "charge": mat("charge", "8a1812", 0.15, 12),
          "rubber": mat("rubber", "2c2a27", 0.02, 6), "mail": mat("mail", "4c4e52", 0.5, 70), "gilt": mat("gilt", "9a7a32", 0.2, 20), "icon": mat("icon", "6a4a2a", 0.4, 14)})   # the Castigator   # the War Prophet's anti-tank charge (2026-11-03)   # the Stigmatic Nun (2026-11-03)
# Faceless heads (visual pass Stage 1, 2026-10-27): every figure wears a plain hood of matte canvas over a smooth,
# featureless head (no eyes, nose or mouth); the figures are told apart by silhouette, kit and pose.
FACE_SCALE = 1.12   # the plain head fills the space of the MakeHuman face
FACE_BOX = None
FISTS = {}   # with mitts: each gloved fist's centre and its forearm's direction, so a weapon sits in the hand
BODY_BUDGET = None   # a figure of a larger class sets its body's triangle budget (docs/art-pipeline.md section 3)
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


DATA = os.path.join(mhdir, "makehuman", "data")
FLOOR = 0.8168
def P(x, y, h): return Vector((x, y, h - FLOOR))      # a point h metres above the floor
def lift(v): return Vector((v.x, v.y, v.z + FLOOR))

def human_body(pose, coat=True, dress=None, head="hood", mitts=None, shape="male-young-averagemuscle-averageweight"):
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
    d = body.modifiers.new("d", "DECIMATE"); d.ratio = 800 / sum(len(p.vertices)-2 for p in body.data.polygons)
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
    bpy.ops.mesh.primitive_cone_add(vertices=18, radius1=0.21, radius2=0.165, depth=0.86, end_fill_type='NOTHING', location=(0, 0, 0.55))
    o = bpy.context.object; o.scale = (1, 0.8, 1)
    for v in o.data.vertices:
        if v.co.z < 0: v.co.z += rnd.uniform(0.0, 0.1)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(o, "robe")
    # rope belt, knot and rosary ending in a small cross
    bpy.ops.mesh.primitive_torus_add(major_radius=0.165, minor_radius=0.016, major_segments=20, minor_segments=4, location=(0, 0, 0.98))
    t = bpy.context.object; t.scale = (1, 0.78, 1); add(t, "rope")
    chain([(0.07, -0.135, 0.97), (0.08, -0.15, 0.8)], [0.014, 0.012], "rope", subdiv=0)
    for i in range(6): sphere((-0.06 + i*0.004, -0.14 - i*0.006, 0.95 - i*0.04), 0.012, "bone", seg=6, rings=4)
    box((-0.036, -0.18, 0.67), (0.012, 0.012, 0.07), "bone"); box((-0.036, -0.18, 0.685), (0.045, 0.012, 0.012), "bone")
    # tabard down the front, ragged at the bottom, with a bone cross
    y0, y1 = -0.135, -0.2
    pts = [(-0.1, y0, 1.38), (0.1, y0, 1.38), (0.11, y1, 0.5), (0.05, y1, 0.44), (0.0, y1, 0.52), (-0.06, y1, 0.42), (-0.11, y1, 0.5)]
    me = bpy.data.meshes.new("tabard"); me.from_pydata(pts, [], [(0, 6, 5, 4, 3, 2, 1)])
    tb = bpy.data.objects.new("tabard", me); scn.collection.objects.link(tb); tb.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(tb, "accent")
    box((0, -0.19, 1.05), (0.045, 0.016, 0.34), "bone"); box((0, -0.186, 1.15), (0.17, 0.016, 0.045), "bone")

def trench_pilgrim():
    # Trench Pilgrim: the template infantry figure of docs/art-pipeline.md (2026-11-03). An original figure in the
    # setting's genre (WWI trench kit over a medieval pilgrim's robe), not a copy of any official miniature or artwork.
    # Body: a blank mannequin head, gloved hands, the long torn robe, rope belt, rosary and bone cross. Modules: Helmet
    # (capirote with a prayer strip), Weapon (bolt-action rifle with fixed bayonet, at port arms), Chest
    # (bandolier with pouches and a wax seal), Back (pack with a rolled blanket), Hip (satchel and canteen), Accent
    # (the tabard, seat colour).
    def pose(r):
        human.aim(r, "spine03", P(0, -0.06, 1.4))
        human.reach(r, "R", P(-0.24, 0.0, 1.06), P(-0.08, -0.24, 0.98))
        human.reach(r, "L", P(0.25, -0.12, 1.12), P(0.1, -0.3, 1.25))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "rags", "thigh": "robe", "torso": "robe"}, head=None, mitts="leather",
                                shape="male-young-minmuscle-minweight")   # gaunt, under a capirote that hides the head
    import random; rnd = random.Random(1914)
    robe_and_tabard(rnd)
    module("Helmet")
    # the capirote (the owner's choice, 2026-11-03): a tall stiff cone of sackcloth over a cloth drape that hides the
    # whole face and falls to the shoulders (no eye holes: the face stays blank) and a painted cross on the front; a
    # seat-colour band round the foot of the cone, and a short shoulder mantle in the seat colour, so the side reads from
    # the game camera above
    c = Vector((hp.x, hp.y - 0.005, top.z))
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.075, radius2=0.0, depth=0.46, location=tuple(c + Vector((0, 0, 0.17))))
    o = bpy.context.object; o.scale = (1, 1.15, 1); add(o, "hood")
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.12, radius2=0.08, depth=0.24, end_fill_type='NOTHING', location=tuple(c + Vector((0, 0, -0.18))))
    o = bpy.context.object; o.scale = (1, 1.15, 1); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.008; add(o, "hood")
    bpy.ops.mesh.primitive_torus_add(major_radius=0.079, minor_radius=0.011, major_segments=12, minor_segments=4, location=tuple(c + Vector((0, 0, -0.055))))
    bpy.context.object.scale = (1, 1.15, 1); add(bpy.context.object, "accent")
    f = c + Vector((0, -0.118, -0.15))   # the painted cross on the drape, over where the face is
    box(tuple(f), (0.022, 0.006, 0.12), "blood", rot=(-0.17, 0, 0)); box(tuple(f + Vector((0, 0.004, 0.02))), (0.08, 0.006, 0.022), "blood", rot=(-0.17, 0, 0))
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.21, radius2=0.11, depth=0.13, end_fill_type='NOTHING', location=(hp.x, hp.y - 0.01, 1.41))
    m_ = bpy.context.object; m_.scale = (1, 0.82, 1); m_.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(m_, "accent")
    # a prayer strip pinned to the side of the cone under a wax seal
    sphere(tuple(c + Vector((0.07, -0.03, -0.02))), 0.018, "blood", (0.5, 1, 1), seg=6, rings=4)
    box(tuple(c + Vector((0.085, -0.03, -0.09))), (0.006, 0.035, 0.11), "bone", rot=(0, 0.12, 0))
    module("Chest")
    # bandolier from the left shoulder to the right hip, with ammunition pouches and a wax seal with its parchment
    skin([(0.15, -0.02, 1.42), (0.02, -0.16, 1.2), (-0.15, -0.12, 0.98)], [(0, 1), (1, 2)], [(0.035, 0.014)]*3, "leather", subdiv=0)
    for i in range(4): box((0.1 - i*0.06, -0.165 + abs(i - 1.5)*0.01, 1.32 - i*0.07), (0.035, 0.025, 0.04), "leather")
    sphere((0.08, -0.17, 1.4), 0.022, "blood", (1, 0.45, 1), seg=6, rings=4)
    box((0.08, -0.172, 1.33), (0.035, 0.005, 0.11), "bone")
    module("Back")
    # a canvas pack between the shoulders with a blanket rolled across the top
    box((0, 0.075, 1.18), (0.2, 0.09, 0.22), "cloth")
    bar((-0.13, 0.075, 1.32), (0.13, 0.075, 1.32), 0.04, "rags", sides=8)
    for x in (-0.08, 0.08): bar((x, 0.1, 1.42), (x, 0.1, 1.06), 0.012, "leather", sides=4)
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
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.21, radius2=0.15, depth=0.9, end_fill_type='NOTHING', location=(0, 0, 0.53))
    o = bpy.context.object; o.scale = (1, 0.85, 1)
    for v in o.data.vertices:
        if v.co.z < 0: v.co.z += rnd.uniform(0.0, 0.03)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "habit")
    # shoulder tabs in the seat colour, seen from above
    for x in (-0.17, 0.17):
        box((x, -0.02, 1.43), (0.09, 0.12, 0.012), "accent", rot=(0, 0.35 if x > 0 else -0.35, 0))
        box((x * 1.06, -0.075, 1.38), (0.085, 0.012, 0.1), "accent")
    module("Helmet")
    # the veil: a white hood over the crown, falling either side of the face to the chest and down the back
    c = Vector((hp.x, hp.y, top.z))
    sphere(tuple(c + Vector((0, 0.01, -0.05))), 0.112, "habit", (1, 1.1, 0.9), seg=10, rings=6)
    # the hood: a drape from the crown to the shoulders, open at the front where the face guard is
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.2, radius2=0.115, depth=0.3, end_fill_type='NOTHING', location=tuple(c + Vector((0, 0.01, -0.2))))
    h = bpy.context.object; h.scale = (1, 1.05, 1)
    import bmesh as _bm
    bm = _bm.new(); bm.from_mesh(h.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y < -0.07 and abs(f.calc_center_median().x) < 0.08], context='FACES')
    bm.to_mesh(h.data); bm.free()
    h.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(h, "habit")
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
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.17, radius2=0.155, depth=0.22, end_fill_type='NOTHING', location=(0, 0.0, 0.86))
    o = bpy.context.object; o.scale = (1, 0.8, 1)
    for v in o.data.vertices:
        if v.co.z < 0: v.co.z += rnd.uniform(0.0, 0.06)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "rags")
    me = bpy.data.meshes.new("flap")
    me.from_pydata([(-0.07, -0.135, 0.96), (0.07, -0.135, 0.96), (0.05, -0.16, 0.62), (0.0, -0.16, 0.66), (-0.05, -0.16, 0.6)], [], [(0, 4, 3, 2, 1)])
    o = bpy.data.objects.new("flap", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(o, "accent")
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
    global BODY_BUDGET; BODY_BUDGET = 1200
    def pose(r):
        human.reach(r, "R", P(-0.28, 0.02, 1.12), P(-0.3, -0.12, 1.22))
        human.reach(r, "L", P(0.25, 0.0, 1.08), P(0.27, -0.06, 0.88))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "robe", "thigh": "robe", "torso": "plate"}, head=None,
                                mitts="plate", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1918)
    # the long robe under the armour, its hem ragged
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.25, radius2=0.18, depth=0.86, end_fill_type='NOTHING', location=(0, 0, 0.55))
    o = bpy.context.object; o.scale = (1, 0.84, 1)
    for v in o.data.vertices:
        if v.co.z < 0: v.co.z += rnd.uniform(0.0, 0.06)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(o, "robe")
    # tabard panels in the seat colour, front and back, hanging from the belt
    for y_top, y_low in ((-0.17, -0.22), (0.15, 0.2)):
        for x in (-0.075, 0.075):
            me = bpy.data.meshes.new("panel")
            me.from_pydata([(x - 0.06, y_top, 1.0), (x + 0.06, y_top, 1.0), (x + 0.06, y_low, 0.5), (x - 0.06, y_low, 0.5)], [], [(0, 3, 2, 1)])
            o = bpy.data.objects.new("panel", me); scn.collection.objects.link(o); o.modifiers.new("sol", "SOLIDIFY").thickness = 0.01
            add(o, "accent")
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
    for x in (-0.075, 0.075):   # a bone cross on each front panel
        box((x, -0.205, 0.8), (0.016, 0.006, 0.12), "bone", rot=(0.1, 0, 0)); box((x, -0.203, 0.82), (0.06, 0.006, 0.016), "bone", rot=(0.1, 0, 0))
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

def castigator():
    # Castigator (2026-11-03, fifth figure of docs/art-pipeline.md, class B, 32 mm): an original figure on the owner's
    # reference cues, not a copy of the miniature, the art or any paint job. A tall flat-topped hat with an Orthodox cross
    # over a dark hood, a gas mask of smooth dark rubber with a filter snout and a hose to the chest (no eye lenses:
    # the face stays blank; no head under it), mail sleeves, a long dark robe and cape, gilded icon plaques on the chest, a tall wooden reliquary
    # shield with a crucifix, severed hands and candles in the left hand, and a spiked flail raised in the right.
    # Modules: Helmet (hat, hood, mask, respirator), Chest (icons, hose, belt), Back (cape), Weapon (flail), Weapon2
    # (shield), Accent (the faction's shoulder mantle and the hat's top).
    global BODY_BUDGET; BODY_BUDGET = 1100
    def pose(r):
        human.reach(r, "R", P(-0.3, -0.06, 1.3), P(-0.4, -0.18, 1.42))
        human.reach(r, "L", P(0.25, -0.1, 1.06), P(0.17, -0.3, 1.1))
    hands, hp, top = human_body(pose, dress={"foot": "boots", "shin": "robe", "thigh": "robe", "torso": "mail"}, head=None,
                                mitts="leather", shape="male-young-averagemuscle-averageweight")
    import random; rnd = random.Random(1919)
    # the long robe, its hem ragged
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=0.24, radius2=0.17, depth=0.88, end_fill_type='NOTHING', location=(0, 0, 0.54))
    o = bpy.context.object; o.scale = (1, 0.84, 1)
    for v in o.data.vertices:
        if v.co.z < 0: v.co.z += rnd.uniform(0.0, 0.07)
    o.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(o, "robe")
    # the pilgrims' shoulder mantle in the seat colour
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=0.255, radius2=0.12, depth=0.12, end_fill_type='NOTHING', location=(hp.x, hp.y - 0.01, 1.41))
    m_ = bpy.context.object; m_.scale = (1, 0.84, 1); m_.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(m_, "accent")
    module("Back")
    # the cape: falling from the shoulders to the ground behind, open at the front
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=0.27, radius2=0.17, depth=1.25, end_fill_type='NOTHING', location=(0, 0.03, 0.72))
    cp = bpy.context.object; cp.scale = (1, 0.82, 1)
    import bmesh as _bm
    bm = _bm.new(); bm.from_mesh(cp.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y < -0.05], context='FACES')
    bm.to_mesh(cp.data); bm.free()
    for v in cp.data.vertices:
        if v.co.z < -0.5: v.co.z += rnd.uniform(0.0, 0.08)
    cp.modifiers.new("sol", "SOLIDIFY").thickness = 0.012; add(cp, "cloth")
    module("Helmet")
    # the hood round the head, open at the front; the porcelain mask; the respirator; the tall hat and its cross
    fb = FACE_BOX; crown = fb["mid"] + fb["up"] * 0.1 - fb["fwd"] * 0.05   # the top of the skull
    c = crown
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=0.15, radius2=0.1, depth=0.27, end_fill_type='NOTHING', location=tuple(c + Vector((0, 0.01, -0.14))))
    h = bpy.context.object; h.scale = (1, 1.05, 1)
    bm = _bm.new(); bm.from_mesh(h.data)
    _bm.ops.delete(bm, geom=[f for f in bm.faces if f.calc_center_median().y < -0.07 and abs(f.calc_center_median().x) < 0.07], context='FACES')
    bm.to_mesh(h.data); bm.free()
    h.modifiers.new("sol", "SOLIDIFY").thickness = 0.01; add(h, "cloth")
    rot = (0, 0, math.atan2(fb["fwd"].x, -fb["fwd"].y))
    centre = fb["mid"] - fb["fwd"] * 0.06 - fb["up"] * 0.02
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
    for u_ in (0.035, -0.06):
        bpy.ops.mesh.primitive_torus_add(major_radius=0.094, minor_radius=0.006, major_segments=10, minor_segments=3, location=tuple(centre + fb["up"] * u_))
        o = bpy.context.object; o.scale = (1, 1.12, 1)
        bpy.context.view_layer.update(); ow = o.matrix_world
        bm = _bm.new(); bm.from_mesh(o.data)
        _bm.ops.delete(bm, geom=[f for f in bm.faces if ((ow @ f.calc_center_median()) - centre).dot(fb["fwd"]) > 0.02], context='FACES')
        bm.to_mesh(o.data); bm.free(); add(o, "leather")
    for x in (-1, 1):
        box(tuple(centre + fb["ax"] * 0.093 * x + fb["up"] * 0.035), (0.008, 0.02, 0.016), "brass", rot=rot)
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

{"rifleman": rifleman, "skirmisher": skirmisher, "shield": shield, "standard": standard, "pilgrim": pilgrim, "trench-pilgrim": trench_pilgrim, "stigmatic-nun": stigmatic_nun, "ecclesiastic-prisoner": ecclesiastic_prisoner, "war-prophet": war_prophet, "castigator": castigator}[role]()
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
    size = 128 if name == "Body" else 64   # one 128x128 texture for the body, 64x64 for each module
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
