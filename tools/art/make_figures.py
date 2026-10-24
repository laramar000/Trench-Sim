#!/usr/bin/env python3
"""Graphics stage G3 (revised 2026-10-24): builds the four original unit figures (client/assets/units/*.glb).

Made for this project (licence "Original"). Modelled in Blender (headless, the pip module bpy): rounded bodies from a
skin modifier, then reduced to about 1,100 triangles. One 128x128 PS1-style texture per figure: the part colours with
grime noise and ambient occlusion baked in, cut to 15-bit colour, drawn with nearest filtering. Each figure stands on
y = 0, faces +z and is exactly 1.2" tall. The seat colour is never baked in: the parts the client paints with it (sash,
mantle, shield face, pennant) are one untextured node named "Accent".

Run: python3 -m pip install bpy==5.1.2 (Python 3.11 to 3.13), then python3 tools/art/make_figures.py
(one Blender run per figure; writes client/assets/units/<figure>.glb). The texture bake is not byte-for-byte repeatable,
so a rerun gives the same shapes with slightly different texture pixels.
"""
import os, subprocess, sys
FIGURES = ("rifleman", "skirmisher", "shield", "standard")
if len(sys.argv) < 3 or sys.argv[-3] != "--figure":
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "client", "assets", "units")
    for name in FIGURES:
        subprocess.run([sys.executable, os.path.abspath(__file__), "--figure", name, os.path.abspath(out)], check=True)
    sys.exit(0)
import bpy, bmesh, math
from mathutils import Vector, Euler
role, outdir = sys.argv[-2], sys.argv[-1]
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

# Front is -Y. lean tilts the upper body forward.
def soldier(lean=0.0, head="brodie"):
    f = lambda z: -lean*max(0, z-0.95)   # forward offset grows with height
    for s in (-1, 1):
        x = 0.11*s
        chain([(x,0,0.95),(x*1.05,-0.02 if s<0 else 0.03,0.52),(x*1.05,0,0.12)], [(0.085,0.09),(0.065,0.065),(0.056,0.056)], "trousers")
        for z in (0.16, 0.24, 0.32):  # puttee wraps
            bpy.ops.mesh.primitive_torus_add(major_radius=0.058, minor_radius=0.008, major_segments=10, minor_segments=4, location=(x*1.05,0,z))
            add(bpy.context.object, "cloth")
        chain([(x,0,0.14),(x,0,0.03),(x,-0.09,0.03)], [(0.06,0.06),(0.055,0.05),(0.045,0.04)], "boots")
    chain([(0,0,0.68),(0,f(0.95),0.95),(0,f(1.2),1.2),(0,f(1.42),1.42)], [(0.21,0.16),(0.17,0.12),(0.19,0.12),(0.2,0.11)], "coat")
    for z in (1.0, 1.1, 1.2, 1.3):  # brass buttons down the front
        sphere((0.0, f(z)-0.125, z), 0.014, "brass", seg=6, rings=4)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.175, minor_radius=0.02, major_segments=20, minor_segments=4, location=(0,0,0.95))
    t = bpy.context.object; t.scale = (1,0.72,1); add(t, "leather")
    box((0.0,-0.13,0.95), (0.06,0.02,0.05), "brass")  # buckle
    hy, hz = f(1.6), 1.6
    chain([(0,f(1.42),1.42),(0,f(1.52),1.52)], [0.055,0.05], "skin")
    sphere((0,hy-0.01,hz), 0.1, "skin", (0.9,1.0,1.1))
    for s in (-1,1): sphere((0.035*s, hy-0.095, hz+0.01), 0.013, "dark", seg=6, rings=4)   # eyes
    box((0, hy-0.1, hz-0.05), (0.035,0.01,0.008), "dark")   # mouth line
    if head == "brodie":
        sphere((0,hy-0.01,hz+0.04), 0.13, "helmet", (1,1.05,0.55), seg=14)
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.19, depth=0.015, location=(0,hy-0.01,hz+0.035)); add(bpy.context.object, "helmet")
    elif head == "hood":
        sphere((0,hy+0.02,hz+0.02), 0.14, "cloth", (1,1.05,1.15), seg=12)
        chain([(0,hy+0.1,hz+0.05),(0,hy+0.2,hz-0.1)], [0.06,0.02], "cloth")
    elif head == "greathelm":
        bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.125, depth=0.27, location=(0,hy,hz+0.02)); add(bpy.context.object, "iron")
        box((0,hy-0.125,hz+0.04), (0.17,0.012,0.022), "dark")   # eye slit
        bar((0,hy-0.13,hz-0.09), (0,hy-0.13,hz+0.12), 0.012, "iron")  # nose ridge... cross
        sphere((0,hy,hz+0.155), 0.125, "iron", (1,1,0.35))
    return f
def arm(sh, hand, m="coat", out=0.08):
    sh, hand = Vector(sh), Vector(hand)
    el = (sh+hand)/2 + Vector((out if sh.x>0 else -out, 0.05, -0.12))
    chain([sh, el, hand], [0.06,0.05,0.045], m)
    sphere(tuple(hand), 0.042, "skin", seg=8, rings=6)

def rifleman():
    f = soldier()
    hr, hl = Vector((-0.08,-0.24,1.05)), Vector((0.12,-0.26,1.30))
    arm((-0.23,0,1.38), hr); arm((0.23,0,1.38), hl)
    d = (hl-hr).normalized(); butt, muz = hr-d*0.25, hl+d*0.45
    bar(butt, hr+d*0.25, 0.03, "wood"); bar(hr+d*0.25, muz-d*0.1, 0.022, "wood")
    bar(hr+d*0.1, muz, 0.011, "metal"); bar(muz, muz+d*0.18, 0.006, "iron", sides=4)
    chain([(0,0.17,1.05),(0,0.17,1.3)], [(0.15,0.07),(0.14,0.07)], "leather")   # pack
    bar((0,0.17,1.33),(0,0.17,1.33), 0.0, "leather") if False else None
    skin([(0.2,0.05,1.40),(-0.15,-0.13,0.98)], [(0,1)], [(0.04,0.016),(0.04,0.016)], "accent", subdiv=0)  # sash
def skirmisher():
    f = soldier(lean=0.35, head="hood")
    hr, hl = Vector((-0.12,-0.3,1.0)), Vector((0.1,-0.42,1.12))
    arm((-0.23,f(1.38),1.38), hr); arm((0.23,f(1.38),1.38), hl)
    d = (hl-hr).normalized()
    bar(hr-d*0.18, hl+d*0.05, 0.025, "wood"); bar(hr, hl+d*0.3, 0.01, "metal"); bar(hl+d*0.3, hl+d*0.45, 0.006, "iron", sides=4)
    chain([(0.2,0.0,0.85),(0.2,0.05,0.75)], [(0.08,0.05),(0.07,0.05)], "leather")   # satchel
    sphere((-0.18,-0.06,0.9), 0.035, "iron", seg=8, rings=6)  # grenade
    bar((-0.18,-0.06,0.93),(-0.18,-0.06,0.97), 0.008, "wood")
    sphere((0,f(1.42)+0.04,1.42), 0.22, "accent", (1.1,0.85,0.45), seg=12, rings=6)  # mantle
def shield():
    f = soldier(head="greathelm")
    for s in (-1,1): sphere((0.24*s,0,1.42), 0.1, "iron", (1,1,0.7))   # pauldrons
    hl = Vector((0.18,-0.28,1.05)); hr = Vector((-0.28,-0.1,1.55))
    arm((0.24,0,1.38), hl); arm((-0.24,0,1.38), hr, out=0.12)
    # pavise: tall shield in front of left side, with bone cross; face = accent
    box((0.12,-0.36,0.85), (0.42,0.04,0.78), "wood")
    box((0.12,-0.385,0.87), (0.36,0.012,0.7), "accent")
    box((0.12,-0.395,0.9), (0.05,0.01,0.5), "bone"); box((0.12,-0.395,1.02), (0.26,0.01,0.05), "bone")
    bar(hr-Vector((0,0,0.15)), hr+Vector((0,0,0.3)), 0.018, "wood")
    sphere(tuple(hr+Vector((0,0,0.35))), 0.06, "iron", seg=8, rings=6)
    for a in range(6):
        p = hr+Vector((0,0,0.35)); o = Vector((math.cos(a)*0.06, math.sin(a)*0.06, 0))
        bar(p+o*0.5, p+o*1.4, 0.015, "iron", sides=4)
def standard():
    f = soldier()
    hr, hl = Vector((-0.12,-0.18,1.0)), Vector((-0.12,-0.2,1.35))
    arm((-0.23,0,1.38), hr); arm((0.23,0,1.38), hl, out=0.02)
    base, top = Vector((-0.12,-0.2,0.05)), Vector((-0.12,-0.2,2.0))
    bar(base, top, 0.018, "wood")
    sphere(tuple(top+Vector((0,0,0.03))), 0.035, "brass", seg=8, rings=6)
    bar(top+Vector((0,0,0.03)), top+Vector((0,0,0.15)), 0.01, "brass", sides=4)
    bar(top-Vector((0,0,0.05)), top-Vector((-0.42,0,0.05)), 0.008, "wood")    # crossbar
    me = bpy.data.meshes.new("pennant")   # swallow-tail banner hanging from crossbar
    x0, x1, zt, zb = -0.12, 0.30, top.z-0.05, top.z-0.65
    me.from_pydata([(x0,-0.2,zt),(x1,-0.2,zt),(x1,-0.2,zb),((x0+x1)/2,-0.2,zb+0.15),(x0,-0.2,zb)], [], [(0,4,3,2,1)])
    o = bpy.data.objects.new("pennant", me); scn.collection.objects.link(o)
    sol = o.modifiers.new("sol","SOLIDIFY"); sol.thickness = 0.015; add(o, "accent")
    bar(top-Vector((-0.42,0,0.05)), top-Vector((-0.42,0,0.2)), 0.004, "metal", sides=4)   # lantern chain
    box(tuple(top-Vector((-0.42,0,0.27))), (0.07,0.07,0.1), "glow"); 
    box(tuple(top-Vector((-0.42,0,0.21))), (0.09,0.09,0.02), "iron"); box(tuple(top-Vector((-0.42,0,0.33))), (0.09,0.09,0.02), "iron")

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
if t > 1100: d = B.modifiers.new("d","DECIMATE"); d.ratio = 1100/t; bpy.ops.object.modifier_apply(modifier="d")
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
bpy.ops.export_scene.gltf(filepath=f"{outdir}/{role}.glb", export_format='GLB', export_yup=True, export_extras=False, export_animations=False)
