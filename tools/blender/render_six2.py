# Six-view inspection renders (Workbench: fast studio + flat silhouette).
# Run: blender --background FILE.blend --python render_six2.py -- --octtag v101 [--octsil 1]
import bpy, math, sys, argparse, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--octtag", type=str, required=True)
ap.add_argument("--octsil", type=int, default=1)
ap.add_argument("--octoutdir", type=str,
                default=r"C:\Users\acera\Desktop\Projects\Octadock\octopus-fable\work\renders")
ap.add_argument("--octcenter", type=str, default="0,0,-0.30")
ap.add_argument("--octdist", type=float, default=5.6)
args = ap.parse_args(argv)

scn = bpy.context.scene
out = os.path.join(args.octoutdir, args.octtag)
os.makedirs(out, exist_ok=True)

cx, cy, cz = [float(v) for v in args.octcenter.split(",")]
C = Vector((cx, cy, cz))
D = args.octdist

cam_data = bpy.data.cameras.new("SixCam")
cam_data.lens = 35
cam = bpy.data.objects.new("SixCam", cam_data)
scn.collection.objects.link(cam)
scn.camera = cam

world = bpy.data.worlds.new("SixWorld") if not scn.world else scn.world
scn.world = world

scn.render.engine = 'BLENDER_WORKBENCH'
scn.render.resolution_x = 820
scn.render.resolution_y = 980
scn.render.film_transparent = False
sh = scn.display.shading

VIEWS = {
    "front":     Vector((0, -D, cz + 0.05)),
    "side":      Vector((D, 0, cz + 0.05)),
    "rear":      Vector((0, D, cz + 0.05)),
    "threequar": Vector((D * 0.72, -D * 0.72, cz + 1.15)),
    "top":       Vector((0.0001, -0.0001, cz + D)),
    "under":     Vector((0.0001, -0.0001, cz - D)),
}

def aim(pos):
    cam.location = pos
    d = (C - pos).normalized()
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

def render(path):
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)

# studio pass (material colors, studio light)
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
sh.show_shadows = False
sh.show_cavity = True
sh.cavity_type = 'WORLD'
sh.cavity_ridge_factor = 0.8
sh.cavity_valley_factor = 1.0
world.color = (0.16, 0.18, 0.20)
for name, pos in VIEWS.items():
    aim(pos)
    render(os.path.join(out, f"{name}_studio.png"))

# silhouette pass
if args.octsil:
    sh.light = 'FLAT'
    sh.color_type = 'SINGLE'
    sh.single_color = (0.0, 0.0, 0.0)
    sh.show_cavity = False
    world.color = (1.0, 1.0, 1.0)
    for name, pos in VIEWS.items():
        aim(pos)
        render(os.path.join(out, f"{name}_sil.png"))

print(f"[six] wrote {out}")
