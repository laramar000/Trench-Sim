#!/usr/bin/env python3
"""Turnaround renders for a figure (docs/art-pipeline.md, review step): front, three-quarter, side and back, each on a
base of the given diameter, side by side in one PNG. Flat studio light and the figure's own baked texture; the
Accent parts in a stand-in seat colour (dark red).

Run: python tools/art/render_turnaround.py client/assets/units/<figure>.glb <out.png> [base_mm] [height_in]
(height_in: the catalog entry's figure height; the .glb is 1.2" tall)
(Python 3.11 to 3.13 with bpy==5.1.2 and pillow.) Game units are inches: a 25 mm base is 0.984" across.
"""
import math, os, sys, tempfile
import bpy
from PIL import Image

glb, out = sys.argv[1], sys.argv[2]
base_mm = float(sys.argv[3]) if len(sys.argv) > 3 else 25.0
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
scn = bpy.context.scene
if len(sys.argv) > 4:
    k = float(sys.argv[4]) / 1.2
    for o in scn.objects:
        if o.type == 'MESH':
            for v in o.data.vertices: v.co *= k
for o in scn.objects:
    if o.type == 'MESH' and o.name.startswith("Accent"):
        m = bpy.data.materials.new("seat"); m.diffuse_color = (0.42, 0.05, 0.04, 1); o.data.materials.clear(); o.data.materials.append(m)
# the base: a disc of the rules' diameter, its top flush with the figure's feet
r = base_mm / 25.4 / 2
bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=r, depth=0.06, location=(0, 0, -0.03))
b = bpy.context.object; bm = bpy.data.materials.new("base"); bm.diffuse_color = (0.12, 0.11, 0.1, 1); b.data.materials.append(bm)
height = max((o.matrix_world @ v.co).z for o in scn.objects if o.type == 'MESH' for v in o.data.vertices)
scn.render.engine = 'BLENDER_WORKBENCH'
scn.display.shading.light = 'STUDIO'; scn.display.shading.color_type = 'TEXTURE'
scn.display.shading.show_shadows = True; scn.display.shading.show_cavity = True
scn.render.resolution_x, scn.render.resolution_y = 520, 760
scn.view_settings.view_transform = 'Standard'
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(1.6, height * 1.3)
frames = []
tmp = tempfile.mkdtemp()
# glTF +Z forward is Blender -Y, so the figure faces -Y: the front camera stands at -Y
for name, deg in (("front", 0), ("three-quarter", 45), ("side", 90), ("back", 180)):
    a = math.radians(deg)
    cam.location = (math.sin(a) * 6, -math.cos(a) * 6, height * 0.5 + 0.05)
    cam.rotation_euler = (math.radians(88), 0, a)
    p = os.path.join(tmp, f"{name}.png"); scn.render.filepath = p
    bpy.ops.render.render(write_still=True); frames.append(Image.open(p).convert("RGB"))
sheet = Image.new("RGB", (sum(f.width for f in frames), frames[0].height))
x = 0
for f in frames:
    sheet.paste(f, (x, 0)); x += f.width
sheet.save(out)
print(f"TURNAROUND {out} height={height:.3f} base={base_mm}mm")
