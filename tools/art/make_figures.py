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
FIGURES = ("rifleman", "skirmisher", "shield", "standard")
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
from mathutils import Vector, Euler
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
M = {k: mat(k, *v) for k, v in {
 "coat": ("5a5040",), "trousers": ("4a4436",), "skin": ("b89a80", 0.12), "helmet": ("4c5048",),
 "leather": ("4a3020",), "wood": ("6a4a2c", 0.3, 8), "metal": ("3a3a3c",), "iron": ("55555a", 0.35, 20),
 "boots": ("2a2420",), "dark": ("141210", 0.05), "brass": ("8a6a2a", 0.1), "bone": ("c8bca0", 0.15),
 "cloth": ("3e3a34",), "glow": ("e0b050", 0.05), "accent": ("d0d0d0", 0.0)}.items()}
body, accent = [], []
def add(o, m):
    o.data.materials.append(M[m]); (accent if m == "accent" else body).append(o); return o
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

def human_body(pose, coat=True):
    body, rig = human.load(DATA)
    for s in ("L", "R"):      # legs together, straight
        x = 0.1 if s == "L" else -0.1
        for b in ("upperleg01", "upperleg02"): human.aim(rig, f"{b}.{s}", P(x, 0.0, 0.47))
        for b in ("lowerleg01", "lowerleg02"): human.aim(rig, f"{b}.{s}", P(x, 0.02, 0.08))
        human.aim(rig, f"foot.{s}", P(x*1.05, -0.14, 0.02))
    pose(rig)
    hands = {s: lift(human.world(rig, f"wrist.{s}", "head")) for s in ("L", "R")}
    eyes = [lift(human.world(rig, f"eye.{s}", "head")) for s in ("L", "R")]
    headp = lift(human.world(rig, "head", "head")); headtop = lift(human.world(rig, "head", "tail"))
    faces = human.bake_pose(body, rig)
    body.location.z = FLOOR; bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    order = {"head": "skin", "hand": "skin", "foot": "boots", "shin": "cloth", "thigh": "trousers", "torso": "coat"}
    slots = list(dict.fromkeys(order.values()))
    for m in slots: body.data.materials.append(M[m])
    for f, p in zip(body.data.polygons, faces): f.material_index = slots.index(order[p])
    if coat:   # the uniform sits a little proud of the body
        out = {i for f, p in zip(body.data.polygons, faces) if p in ("torso", "thigh") for i in f.vertices}
        for v in body.data.vertices:
            if v.index in out: v.co += v.normal * 0.012
    d = body.modifiers.new("d", "DECIMATE"); d.ratio = 900 / sum(len(p.vertices)-2 for p in body.data.polygons)
    bpy.ops.object.modifier_apply(modifier="d")
    bpy.ops.object.shade_smooth()
    body_parts = body
    globals()["body"].append(body)
    for e in eyes: sphere(tuple(e + Vector((0, -0.012, 0))), 0.012, "dark", seg=6, rings=4)
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

{"rifleman": rifleman, "skirmisher": skirmisher, "shield": shield, "standard": standard}[role]()
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
B = join(body, "Body"); A = join(accent, "Accent")
# decimate body to budget, triangulate
bpy.ops.object.select_all(action='DESELECT'); B.select_set(True); bpy.context.view_layer.objects.active = B
n = len(B.data.polygons)
# count tris properly
def tris(o): return sum(len(p.vertices)-2 for p in o.data.polygons)
t = tris(B)
if t > 1400: d = B.modifiers.new("d","DECIMATE"); d.ratio = 1400/t; bpy.ops.object.modifier_apply(modifier="d")
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.quads_convert_to_tris()
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.02); bpy.ops.object.mode_set(mode='OBJECT')
img = bpy.data.images.new(role, 128, 128)
for m in B.data.materials:
    tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; m.node_tree.nodes.active = tn
scn.render.engine = 'CYCLES'; scn.cycles.device = 'CPU'; scn.render.bake.margin = 2
scn.cycles.samples = 8; bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}); col = list(img.pixels)
scn.cycles.samples = 32; bpy.ops.object.bake(type='AO'); ao = list(img.pixels)
px = []
for i in range(0, len(col), 4):
    for c in range(3):
        v = (col[i+c]*(0.35+0.65*ao[i+c])) ** (1/2.2); px.append((round(v*31)/31) ** 2.2)   # 15-bit colour
    px.append(1.0)
img.pixels = px; img.pack()
m = bpy.data.materials.new(f"{role}Body"); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
b.inputs["Roughness"].default_value = 1.0
tn = m.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img; tn.interpolation = 'Closest'
m.node_tree.links.new(tn.outputs["Color"], b.inputs["Base Color"])
B.data.materials.clear(); B.data.materials.append(m)
# Accent: one flat material, untextured
while A.data.uv_layers: A.data.uv_layers.remove(A.data.uv_layers[0])
A.data.materials.clear(); am = bpy.data.materials.new("Accent"); am.use_nodes = True; A.data.materials.append(am)
bpy.ops.object.select_all(action='DESELECT'); A.select_set(True); bpy.context.view_layer.objects.active = A
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.quads_convert_to_tris(); bpy.ops.object.mode_set(mode='OBJECT')
# scale to the game's 1.2 tall, feet at 0, centred
allz = [ (o.matrix_world @ v.co) for o in (A, B) for v in o.data.vertices]
h = max(p.z for p in allz); k = 1.2 / h
for o in (A, B):
    o.select_set(True)
for o in (A,B): o.scale = (k,k,k)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
low = min((o.matrix_world @ v.co).z for o in (A, B) for v in o.data.vertices)
for o in (A, B):
    for v in o.data.vertices: v.co.z -= low
top = max((o.matrix_world @ v.co).z for o in (A, B) for v in o.data.vertices)
for o in (A, B):
    for v in o.data.vertices: v.co *= 1.2 / top
allp = [(o.matrix_world @ v.co) for o in (A, B) for v in o.data.vertices]
w = max(max(p.x for p in allp)-min(p.x for p in allp), max(p.y for p in allp)-min(p.y for p in allp))
print(f"FIG {role} body_tris={tris(B)} accent_tris={tris(A)} height={max(p.z for p in allp):.3f} width={w:.3f}")
bpy.ops.export_scene.gltf(filepath=f"{outdir}/{role}.glb", export_format='GLB', export_yup=True)
