# Graphics stage G3: loads the MakeHuman base body (CC0, github.com/makehumancommunity/makehuman) as a posable Blender
# mesh for tools/art/make_figures.py, with the "male, young, average" shape target, its default skeleton and weights.
# Units: metres, z up, facing -y.
import bpy, json, os
from mathutils import Vector, Matrix
def load(data, targets=(("macrodetails/universal-male-young-averagemuscle-averageweight.target", 1.0),)):
    V, F = [], []
    for line in open(os.path.join(data, "3dobjs/base.obj")):
        if line.startswith("v "):
            x, y, z = map(float, line.split()[1:4]); V.append([x, y, z])
        elif line.startswith("g "):
            group = line.split()[1]
        elif line.startswith("f ") and group == "body":
            F.append([int(p.split("/")[0]) - 1 for p in line.split()[1:]])
    for path, w in targets:
        for line in open(os.path.join(data, "targets", path)):
            if line[0].isdigit():
                i, dx, dy, dz = line.split(); i = int(i)
                V[int(i)][0] += w*float(dx); V[i][1] += w*float(dy); V[i][2] += w*float(dz)
    V = [Vector((x*0.1, -z*0.1, y*0.1)) for x, y, z in V]
    used = sorted({i for f in F for i in f}); remap = {o: n for n, o in enumerate(used)}
    me = bpy.data.meshes.new("human"); me.from_pydata([V[i] for i in used], [], [[remap[i] for i in f] for f in F])
    body = bpy.data.objects.new("human", me); bpy.context.scene.collection.objects.link(body)
    skel = json.load(open(os.path.join(data, "rigs/default.mhskel")))
    joint = lambda name: sum((V[i] for i in skel["joints"][name]), Vector()) / len(skel["joints"][name])
    arm = bpy.data.objects.new("rig", bpy.data.armatures.new("rig")); bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for n, b in skel["bones"].items():
        e = arm.data.edit_bones.new(n); e.head = joint(b["head"]); e.tail = joint(b["tail"]); eb[n] = e
    for n, b in skel["bones"].items():
        if b["parent"]: eb[n].parent = eb[b["parent"]]
    bpy.ops.object.mode_set(mode='OBJECT')
    weights = json.load(open(os.path.join(data, "rigs/default_weights.mhw")))["weights"]
    for n, vw in weights.items():
        g = body.vertex_groups.new(name=n)
        for i, w in vw:
            if i in remap: g.add([remap[i]], w, 'REPLACE')
    body.modifiers.new("rig", "ARMATURE").object = arm
    return body, arm

def world(arm, bone, end="tail"):
    pb = arm.pose.bones[bone]; return arm.matrix_world @ (pb.tail if end == "tail" else pb.head)

def aim(arm, bone, target):
    """Turns a pose bone (and its children) about its head so it points at target."""
    bpy.context.view_layer.update()
    pb = arm.pose.bones[bone]; head = pb.head.copy()
    q = (pb.tail - pb.head).rotation_difference(Vector(target) - head)
    pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ pb.matrix
    bpy.context.view_layer.update()

def reach(arm, side, elbow, hand):
    for b in ("upperarm01", "upperarm02"): aim(arm, f"{b}.{side}", elbow)
    for b in ("lowerarm01", "lowerarm02"): aim(arm, f"{b}.{side}", hand)
    w = Vector(hand) + (Vector(hand) - Vector(elbow)).normalized() * 0.08
    aim(arm, f"wrist.{side}", w)

def bake_pose(body, arm):
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier="rig")
    # material regions from the bone weights: which part of the body each face mostly belongs to
    names = {g.index: g.name for g in body.vertex_groups}
    def part(n):
        if any(k in n for k in ("finger", "metacarpal", "wrist")): return "hand"
        if any(k in n for k in ("toe", "foot")): return "foot"
        if "lowerleg" in n: return "shin"
        if any(k in n for k in ("upperleg", "pelvis")): return "thigh"
        if any(k in n for k in ("head", "jaw", "neck", "eye", "oculi", "levator", "oris", "temporalis", "tongue", "nose", "special", "risorius", "masseter", "platysma", "mandible", "cheek")): return "head"
        return "torso"
    vpart = []
    for v in body.data.vertices:
        s = {}
        for g in v.groups: p = part(names[g.group]); s[p] = s.get(p, 0) + g.weight
        vpart.append(s)
    faces = []
    for f in body.data.polygons:
        s = {}
        for i in f.vertices:
            for p, w in vpart[i].items(): s[p] = s.get(p, 0) + w
        faces.append(max(s, key=s.get) if s else "torso")
    bpy.data.objects.remove(arm)
    for g in list(body.vertex_groups): body.vertex_groups.remove(g)
    return faces
