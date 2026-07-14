# Export the v2 rigged+animated octopus to GLB with named clips.
# Web variant: decimated, materials stripped, turn clips dropped (runtime never samples
# them), eyeballs joined into the skinned mesh, eye positions printed in glTF mesh space.
# Run: blender --background octopus_v106_animated.blend --python export_glb2.py -- --octfile PATH [--octweblod 0.18]
import bpy, sys, argparse, os

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--octfile", required=True)
ap.add_argument("--octweblod", type=float, default=0.0)
args = ap.parse_args(argv)

rig = bpy.data.objects.get("OctoRig")
web = args.octweblod > 0
for tr in rig.animation_data.nla_tracks:
    tr.mute = False
if web:
    # the page samples idle/swim/swimB/flipLag only — drop turn tracks from the export
    for tr in list(rig.animation_data.nla_tracks):
        if tr.name in ("turnDown", "turnUp"):
            rig.animation_data.nla_tracks.remove(tr)

keep = {"Octopus", "OctoRig", "eyeball_L", "eyeball_R"}
for ob in list(bpy.data.objects):
    if ob.name not in keep and ob.type in {'LIGHT', 'CAMERA', 'MESH', 'EMPTY'}:
        bpy.data.objects.remove(ob)

octo = bpy.data.objects.get("Octopus")
if web:
    for m in list(octo.modifiers):
        if m.type == 'CORRECTIVE_SMOOTH':
            octo.modifiers.remove(m)
    dec = octo.modifiers.new("dec", 'DECIMATE')
    dec.decimate_type = 'COLLAPSE'
    dec.ratio = args.octweblod
    dec.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = octo
    bpy.ops.object.modifier_apply(modifier=dec.name)
    print(f"[glb2] web LOD faces={len(octo.data.polygons)}")

    for enm in ("eyeball_L", "eyeball_R"):
        eob = bpy.data.objects.get(enm)
        if not eob:
            continue
        mw = eob.matrix_world.copy()
        c = mw.translation
        # glTF Y-up mesh space: (x, z, -y)
        print(f"[glb2] eye {enm} gltf=({c.x:.4f}, {c.z:.4f}, {-c.y:.4f})")
        eob.parent = None
        eob.matrix_world = mw
        vg = eob.vertex_groups.new(name="body_1")
        vg.add(range(len(eob.data.vertices)), 1.0, 'REPLACE')
        for ob in bpy.data.objects:
            ob.select_set(False)
        eob.select_set(True)
        octo.select_set(True)
        bpy.context.view_layer.objects.active = octo
        bpy.ops.object.join()
    print(f"[glb2] joined eyeballs; faces={len(octo.data.polygons)}")

os.makedirs(os.path.dirname(args.octfile), exist_ok=True)
bpy.ops.export_scene.gltf(
    filepath=args.octfile,
    export_format='GLB',
    export_animations=True,
    export_animation_mode='NLA_TRACKS',
    export_frame_range=False,
    export_force_sampling=not web,
    export_optimize_animation_size=True,
    export_skins=True,
    export_all_influences=False,
    export_yup=True,
    export_apply=False,
    export_materials='NONE' if web else 'EXPORT',
    export_image_format='NONE' if web else 'AUTO',
)
size = os.path.getsize(args.octfile)
print(f"[glb2] exported {args.octfile} ({size/1e6:.2f} MB)")
