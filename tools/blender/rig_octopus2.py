# Rig the v2 production octopus: body chain + 8 arm chains from dumped centerlines.
# Spine follows the v104 profile (mantle nods +Y, body_len 1.24).
# Run: blender --background octopus_v104.blend --python rig_octopus2.py -- --armdata armdata2.json --version 105
import bpy, sys, argparse, os, json, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--armdata", type=str, required=True)
ap.add_argument("--version", type=int, default=22)
ap.add_argument("--bones-per-arm", type=int, default=14)
ap.add_argument("--out", type=str, default=r"C:\Users\acera\Desktop\Octadock-Octopus-Fable\work\blender")
args = ap.parse_args(argv)

scn = bpy.context.scene
octo = bpy.data.objects.get("Octopus")
assert octo, "Octopus mesh not found"
with open(args.armdata) as f:
    AD = json.load(f)

# ---------------- create armature
arm_data = bpy.data.armatures.new("OctoRig")
rig = bpy.data.objects.new("OctoRig", arm_data)
scn.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones

def add_bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head = head
    b.tail = tail
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b

# root at crown center
add_bone("root", Vector((0, 0, -0.12)), Vector((0, 0, 0.02)))

# body chain follows the nod (y offsets from the body profile)
body_pts = [
    Vector((0, 0.000, 0.02)),
    Vector((0, -0.005, 0.36)),  # head (slight forward jut)
    Vector((0, 0.070, 0.70)),   # mantle base
    Vector((0, 0.220, 1.00)),   # mantle mid (backward nod)
    Vector((0, 0.300, 1.18)),   # upper mantle
    Vector((0, 0.320, 1.30)),   # apex
]
prev = "root"
for k in range(len(body_pts) - 1):
    nm = f"body_{k+1}"
    add_bone(nm, body_pts[k], body_pts[k + 1], parent=prev, connect=(k > 0))
    prev = nm

# arm chains along dumped centerlines with azimuth-consistent rolls:
# local Z aligned to the arm's horizontal outward direction, so +X rotation is
# "radial lift/bloom" and -X is "bundle" identically on every arm
N = args.bones_per_arm
for i, arm in enumerate(AD["arms"]):
    pts = [Vector(p) for p in arm["points"]]
    az = arm["azim"]
    out_dir = Vector((math.cos(az), math.sin(az), 0.0))
    idxs = [round(j * (len(pts) - 1) / N) for j in range(N + 1)]
    prev = "root"
    for k in range(N):
        h, t = pts[idxs[k]], pts[idxs[k + 1]]
        nm = f"arm{i}_{k:02d}"
        b = add_bone(nm, h, t, parent=prev, connect=(k > 0))
        b.align_roll(out_dir)
        prev = nm

bpy.ops.object.mode_set(mode='OBJECT')
print(f"[rig] bones={len(rig.data.bones)}")

# ---------------- parent mesh with automatic weights
for ob in bpy.data.objects:
    ob.select_set(False)
octo.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print("[rig] auto weights done")

# corrective smooth AFTER armature
octo.select_set(True)
bpy.context.view_layer.objects.active = octo
cs = octo.modifiers.new("csmooth", 'CORRECTIVE_SMOOTH')
cs.factor = 0.5
cs.iterations = 5
cs.smooth_type = 'LENGTH_WEIGHTED'
cs.use_only_smooth = False
cs.use_pin_boundary = False

# ---------------- eyeballs follow the head bone
for ob in bpy.data.objects:
    if ob.name.startswith("eyeball"):
        mw = ob.matrix_world.copy()
        ob.parent = rig
        ob.parent_type = 'BONE'
        ob.parent_bone = "body_1"
        ob.matrix_world = mw

path = os.path.join(args.out, f"octopus_v{args.version:03d}_rigged.blend")
bpy.ops.wm.save_as_mainfile(filepath=path)
print(f"[rig] saved {path}")
