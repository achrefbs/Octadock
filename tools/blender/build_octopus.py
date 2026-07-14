"""Build Octadock's production octopus asset in Blender.

Run with:
  blender --background --python tools/blender/build_octopus.py

The script creates a continuous voxel-welded body, a deterministic armature,
an idle animation, an editable .blend file, a GLB, and a rendered preview.
"""

from __future__ import annotations

import json
import math
import base64
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


ROOT = Path(__file__).resolve().parents[2]
MODEL_DIR = ROOT / "web" / "assets" / "models"
ARTIFACT_DIR = ROOT / "artifacts" / "octopus"
BLEND_PATH = MODEL_DIR / "octopus-production.blend"
GLB_PATH = MODEL_DIR / "octopus-production.glb"
PREVIEW_PATH = ARTIFACT_DIR / "octopus-production-preview.png"
METADATA_PATH = MODEL_DIR / "octopus-production.json"
FILE_PREVIEW_DATA_PATH = MODEL_DIR / "octopus-production-data.js"

ARM_COUNT = 8
ARM_BONES = 6
TAU = math.tau


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.armatures,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
    ):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def mesh_object(name: str, vertices, faces) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def lathe(name: str, profile, segments: int = 64) -> bpy.types.Object:
    """Create a closed, softly asymmetric surface of revolution."""
    vertices = []
    faces = []
    for ring, row in enumerate(profile):
        z, radius, x_offset, y_scale = row[:4]
        y_offset = row[4] if len(row) > 4 else 0.0
        for side in range(segments):
            angle = side / segments * TAU
            # A small second harmonic keeps the mantle organic, not spherical.
            organic = 1.0 + 0.025 * math.cos(angle * 2.0) * math.sin(ring / max(1, len(profile) - 1) * math.pi)
            vertices.append(
                (
                    x_offset + math.cos(angle) * radius * organic,
                    y_offset + math.sin(angle) * radius * y_scale,
                    z,
                )
            )
    rings = len(profile)
    for ring in range(rings - 1):
        for side in range(segments):
            a = ring * segments + side
            b = ring * segments + (side + 1) % segments
            c = (ring + 1) * segments + (side + 1) % segments
            d = (ring + 1) * segments + side
            faces.append((a, b, c, d))
    return mesh_object(name, vertices, faces)


def ellipsoid(name: str, center, scale, rings: int = 22, segments: int = 44) -> bpy.types.Object:
    vertices = []
    faces = []
    cx, cy, cz = center
    sx, sy, sz = scale
    for ring in range(rings + 1):
        phi = ring / rings * math.pi
        sp, cp = math.sin(phi), math.cos(phi)
        for side in range(segments):
            theta = side / segments * TAU
            vertices.append((cx + math.cos(theta) * sp * sx, cy + math.sin(theta) * sp * sy, cz + cp * sz))
    for ring in range(rings):
        for side in range(segments):
            a = ring * segments + side
            b = ring * segments + (side + 1) % segments
            c = (ring + 1) * segments + (side + 1) % segments
            d = (ring + 1) * segments + side
            faces.append((a, b, c, d))
    return mesh_object(name, vertices, faces)


def cubic_bezier(points, t: float) -> Vector:
    p0, p1, p2, p3 = (Vector(p) for p in points)
    u = 1.0 - t
    return p0 * (u**3) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t**3)


def make_arm_paths():
    paths = []
    for arm in range(ARM_COUNT):
        angle = (arm + 0.5) / ARM_COUNT * TAU
        side_bias = -0.055 if arm % 2 else 0.055
        length = 2.82 + 0.12 * math.sin(arm * 1.73 + 0.4)
        curl = (0.18 + 0.035 * math.cos(arm * 1.19)) * (-1.0 if arm % 2 else 1.0)
        radial = Vector((math.cos(angle), math.sin(angle), 0.0))
        tangent = Vector((-math.sin(angle), math.cos(angle), 0.0))
        p0 = radial * 0.43 + Vector((0.0, 0.0, -0.07))
        p1 = radial * 0.90 + tangent * side_bias + Vector((0.0, 0.0, -0.16))
        p2 = radial * (length * 0.66) + tangent * curl + Vector((0.0, 0.0, -0.32))
        p3 = radial * length + tangent * (curl * 1.85) + Vector((0.0, 0.0, -0.26 + 0.045 * math.sin(arm * 0.9)))
        paths.append(tuple(tuple(p) for p in (p0, p1, p2, p3)))
    return paths


def tube(name: str, bezier_points, samples: int = 30, sides: int = 14) -> bpy.types.Object:
    centers = [cubic_bezier(bezier_points, i / (samples - 1)) for i in range(samples)]
    vertices = []
    faces = []
    normal = Vector((0.0, 0.0, 1.0))
    previous_tangent = None
    for sample, center in enumerate(centers):
        if sample == 0:
            tangent = (centers[1] - center).normalized()
        elif sample == samples - 1:
            tangent = (center - centers[sample - 1]).normalized()
        else:
            tangent = (centers[sample + 1] - centers[sample - 1]).normalized()
        if previous_tangent is None:
            if abs(tangent.dot(normal)) > 0.92:
                normal = Vector((0.0, 1.0, 0.0))
            normal = (normal - tangent * tangent.dot(normal)).normalized()
        else:
            axis = previous_tangent.cross(tangent)
            if axis.length > 1e-6:
                normal.rotate(Matrix.Rotation(previous_tangent.angle(tangent), 3, axis.normalized()))
            normal = (normal - tangent * tangent.dot(normal)).normalized()
        binormal = tangent.cross(normal).normalized()
        t = sample / (samples - 1)
        radius = 0.19 * ((1.0 - t) ** 1.03) + 0.024
        # Flatten the arm slightly; octopus arms are not round cables.
        vertical_radius = radius * (0.74 + 0.12 * t)
        for side in range(sides):
            angle = side / sides * TAU
            offset = normal * (math.cos(angle) * radius) + binormal * (math.sin(angle) * vertical_radius)
            vertices.append(tuple(center + offset))
        previous_tangent = tangent
    for sample in range(samples - 1):
        for side in range(sides):
            a = sample * sides + side
            b = sample * sides + (side + 1) % sides
            c = (sample + 1) * sides + (side + 1) % sides
            d = (sample + 1) * sides + side
            faces.append((a, b, c, d))
    return mesh_object(name, vertices, faces)


def join_and_remesh(parts) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    body = bpy.context.object
    body.name = "OctopusBody"
    body.data.name = "OctopusBodyMesh"

    # A single voxel-remesh pass welds the mantle, head, crown, web, and arms
    # into one uninterrupted surface. This is the key anatomical constraint.
    body.data.remesh_voxel_size = 0.042
    body.data.remesh_voxel_adaptivity = 0.0
    body.data.use_remesh_fix_poles = True
    bpy.ops.object.voxel_remesh()

    smooth = body.modifiers.new("SurfaceRelax", "SMOOTH")
    smooth.factor = 1.25
    smooth.iterations = 5
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    decimate = body.modifiers.new("WebDecimate", "DECIMATE")
    decimate.decimate_type = "COLLAPSE"
    decimate.ratio = 0.64
    decimate.use_collapse_triangulate = False
    bpy.ops.object.modifier_apply(modifier=decimate.name)

    for polygon in body.data.polygons:
        polygon.use_smooth = True
    return body


def add_shape_keys(body: bpy.types.Object) -> None:
    basis = body.shape_key_add(name="Basis")
    breath = body.shape_key_add(name="Mantle_Breath")
    jet = body.shape_key_add(name="Mantle_Jet")
    web = body.shape_key_add(name="Web_Pulse")
    del basis

    for index, vertex in enumerate(body.data.vertices):
        co = vertex.co
        mantle = max(0.0, min(1.0, (co.z - 0.55) / 1.25))
        crown = math.exp(-((co.z + 0.05) / 0.28) ** 2) * max(0.0, 1.0 - co.xy.length / 1.15)
        breath.data[index].co.x *= 1.0 + 0.085 * mantle
        breath.data[index].co.y *= 1.0 + 0.085 * mantle
        breath.data[index].co.z -= 0.045 * mantle
        jet.data[index].co.x *= 1.0 - 0.14 * mantle
        jet.data[index].co.y *= 1.0 - 0.14 * mantle
        jet.data[index].co.z += 0.19 * mantle
        web.data[index].co.z -= 0.07 * crown
        web.data[index].co.x *= 1.0 + 0.035 * crown
        web.data[index].co.y *= 1.0 + 0.035 * crown


def sample_arm(path, t: float) -> Vector:
    return cubic_bezier(path, max(0.0, min(1.0, t)))


def build_armature(paths) -> bpy.types.Object:
    armature = bpy.data.armatures.new("OctopusRig")
    rig = bpy.data.objects.new("OctopusRig", armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    root = armature.edit_bones.new("root")
    root.head = (0.0, 0.0, -0.12)
    root.tail = (0.0, 0.0, 0.42)

    head = armature.edit_bones.new("head")
    head.head = (0.0, 0.0, 0.05)
    head.tail = (0.0, 0.0, 0.82)
    head.parent = root

    mantle = armature.edit_bones.new("mantle")
    mantle.head = (0.0, 0.0, 0.52)
    mantle.tail = (0.0, 0.10, 2.12)
    mantle.parent = head

    for arm, path in enumerate(paths):
        parent = root
        for bone_index in range(ARM_BONES):
            t0 = bone_index / ARM_BONES
            t1 = (bone_index + 1) / ARM_BONES
            bone = armature.edit_bones.new(f"arm_{arm}_{bone_index}")
            bone.head = sample_arm(path, t0)
            bone.tail = sample_arm(path, t1)
            bone.parent = parent
            bone.use_connect = bone_index > 0
            parent = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.show_in_front = True
    return rig


def closest_arm_sample(point: Vector, paths):
    best = None
    for arm, path in enumerate(paths):
        previous = sample_arm(path, 0.0)
        steps = 42
        for step in range(steps):
            current = sample_arm(path, (step + 1) / steps)
            segment = current - previous
            length_sq = segment.length_squared
            u = 0.0 if length_sq < 1e-10 else max(0.0, min(1.0, (point - previous).dot(segment) / length_sq))
            projected = previous + segment * u
            distance = (point - projected).length
            t = (step + u) / steps
            normalized_distance = distance / (0.19 * ((1.0 - t) ** 1.03) + 0.034)
            if best is None or normalized_distance < best[0]:
                best = (normalized_distance, arm, t)
            previous = current
    return best


def skin_body(body: bpy.types.Object, rig: bpy.types.Object, paths) -> None:
    groups = {bone.name: body.vertex_groups.new(name=bone.name) for bone in rig.data.bones}
    for vertex in body.data.vertices:
        point = vertex.co.copy()
        normalized_distance, arm, t = closest_arm_sample(point, paths)
        influences = []
        if normalized_distance < 1.55 and point.z < 0.34:
            scaled = t * (ARM_BONES - 1)
            first = min(ARM_BONES - 1, int(math.floor(scaled)))
            second = min(ARM_BONES - 1, first + 1)
            blend = scaled - first
            base_blend = max(0.0, min(0.62, (0.16 - t) / 0.16 * 0.62))
            influences.append((f"arm_{arm}_{first}", (1.0 - blend) * (1.0 - base_blend)))
            if second != first:
                influences.append((f"arm_{arm}_{second}", blend * (1.0 - base_blend)))
            if base_blend > 0.0:
                influences.append(("root", base_blend))
        else:
            mantle_weight = max(0.0, min(1.0, (point.z - 0.48) / 0.72))
            head_weight = max(0.0, min(1.0, 1.0 - abs(point.z - 0.42) / 0.72)) * (1.0 - mantle_weight)
            root_weight = max(0.0, 1.0 - mantle_weight - head_weight)
            influences.extend((("root", root_weight), ("head", head_weight), ("mantle", mantle_weight)))
        total = sum(weight for _, weight in influences) or 1.0
        for name, weight in influences:
            if weight > 1e-5:
                groups[name].add([vertex.index], weight / total, "REPLACE")

    modifier = body.modifiers.new("OctopusArmature", "ARMATURE")
    modifier.object = rig
    modifier.use_deform_preserve_volume = True
    body.parent = rig


def material(name: str, color, metallic=0.0, roughness=0.35, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def add_eyes(rig: bpy.types.Object):
    eye_material = material("EyeGlass", (0.025, 0.08, 0.085), metallic=0.15, roughness=0.18)
    pupil_material = material("EyeGlow", (0.015, 0.4, 0.35), roughness=0.2, emission=(0.02, 0.7, 0.58), emission_strength=4.0)
    objects = []
    for side in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, location=(0.39 * side, -0.57, 0.36))
        eye = bpy.context.object
        eye.name = f"Eye_{'L' if side < 0 else 'R'}"
        eye.scale = (0.145, 0.075, 0.12)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        eye.data.materials.append(eye_material)
        eye.parent = rig
        objects.append(eye)

        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, location=(0.39 * side, -0.637, 0.36))
        pupil = bpy.context.object
        pupil.name = f"Pupil_{'L' if side < 0 else 'R'}"
        pupil.scale = (0.066, 0.028, 0.064)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        pupil.data.materials.append(pupil_material)
        pupil.parent = rig
        objects.append(pupil)
    return objects


def animate(rig: bpy.types.Object, body: bpy.types.Object) -> bpy.types.Action:
    action = bpy.data.actions.new("Octopus_Idle")
    rig.animation_data_create()
    rig.animation_data.action = action
    for frame in (1, 31, 61, 91, 121):
        phase = (frame - 1) / 120.0 * TAU
        for arm in range(ARM_COUNT):
            for bone_index in range(ARM_BONES):
                pose_bone = rig.pose.bones[f"arm_{arm}_{bone_index}"]
                pose_bone.rotation_mode = "XYZ"
                falloff = (bone_index + 1) / ARM_BONES
                pose_bone.rotation_euler.x = math.sin(phase + arm * 0.73 + bone_index * 0.52) * 0.075 * falloff
                pose_bone.rotation_euler.y = math.cos(phase * 0.72 + arm * 0.48 + bone_index * 0.67) * 0.10 * falloff
                pose_bone.rotation_euler.z = math.sin(phase * 0.58 + arm * 0.91 + bone_index * 0.35) * 0.055 * falloff
                pose_bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=f"Arm {arm + 1}")
        mantle = rig.pose.bones["mantle"]
        mantle.rotation_mode = "XYZ"
        mantle.rotation_euler.y = math.sin(phase) * 0.025
        mantle.keyframe_insert(data_path="rotation_euler", frame=frame, group="Mantle")

        body.data.shape_keys.key_blocks["Mantle_Breath"].value = 0.5 + 0.5 * math.sin(phase - math.pi / 2)
        body.data.shape_keys.key_blocks["Mantle_Breath"].keyframe_insert("value", frame=frame)
        body.data.shape_keys.key_blocks["Web_Pulse"].value = 0.16 + 0.12 * math.sin(phase + 0.4)
        body.data.shape_keys.key_blocks["Web_Pulse"].keyframe_insert("value", frame=frame)
    action.frame_range = (1.0, 121.0)
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 121
    bpy.context.scene.render.fps = 30
    return action


def point_at(obj: bpy.types.Object, target) -> None:
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def setup_preview(body: bpy.types.Object, rig: bpy.types.Object) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.filepath = str(PREVIEW_PATH)
    scene.world.color = (0.0015, 0.003, 0.007)
    world = scene.world
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.0015, 0.004, 0.008, 1.0)
    background.inputs["Strength"].default_value = 0.08

    body.data.materials.clear()
    body.data.materials.append(material("OctadockGlass", (0.018, 0.34, 0.31), metallic=0.35, roughness=0.24, emission=(0.01, 0.16, 0.14), emission_strength=0.45))

    bpy.ops.object.camera_add(location=(3.8, -8.5, 3.15))
    camera = bpy.context.object
    camera.data.lens = 58
    camera.data.sensor_width = 36
    point_at(camera, (0.0, 0.0, 0.35))
    scene.camera = camera

    for name, location, energy, color, size in (
        ("Key", (-4.0, -4.5, 5.8), 1150, (0.16, 0.92, 0.78), 4.5),
        ("Rim", (4.8, 1.2, 4.0), 1250, (0.12, 0.48, 1.0), 3.2),
        ("Fill", (0.0, -2.0, -1.5), 650, (0.08, 0.58, 0.52), 3.0),
    ):
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = energy
        data.color = color
        data.shape = "DISK"
        data.size = size
        light = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(light)
        light.location = location
        point_at(light, (0.0, 0.0, 0.25))

    # A dark floor catches a restrained contact shadow without reading as land.
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0.0, 0.0, -0.72))
    floor = bpy.context.object
    floor.name = "PreviewFloor"
    floor.data.materials.append(material("Abyss", (0.002, 0.006, 0.012), roughness=0.82))

    scene.frame_set(42)
    bpy.context.view_layer.update()


def export_asset(body: bpy.types.Object, rig: bpy.types.Object, _preview_eyes) -> None:
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)

    # The runtime GLB contains exactly one skinned mesh. The editable .blend
    # keeps the studio eye geometry, while the landing page draws its two tiny
    # eye highlights procedurally to avoid four extra mesh primitives.
    export_objects = [rig, body]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in export_objects:
        obj.hide_render = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    original_frame = bpy.context.scene.frame_current
    bpy.context.scene.frame_set(1)
    for key_name in ("Mantle_Breath", "Mantle_Jet", "Web_Pulse"):
        body.data.shape_keys.key_blocks[key_name].value = 0.0
    bpy.context.view_layer.update()
    bpy.ops.export_scene.gltf(
        filepath=str(GLB_PATH),
        export_format="GLB",
        use_selection=True,
        export_animations=True,
        export_animation_mode="ACTIVE_ACTIONS",
        export_skins=True,
        export_morph=True,
        export_materials="EXPORT",
        export_yup=True,
        export_apply=False,
    )
    bpy.context.scene.frame_set(original_frame)


def main() -> None:
    reset_scene()
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)

    mantle_profile = [
        (2.26, 0.04, 0.00, 0.92, 0.18),
        (2.20, 0.22, 0.00, 0.93, 0.18),
        (2.05, 0.43, 0.00, 0.94, 0.16),
        (1.82, 0.62, 0.00, 0.95, 0.13),
        (1.52, 0.71, 0.00, 0.96, 0.10),
        (1.20, 0.66, 0.00, 0.97, 0.06),
        (0.92, 0.55, 0.00, 0.98, 0.03),
        (0.68, 0.46, 0.00, 1.00, 0.01),
        (0.50, 0.42, 0.00, 1.00, 0.00),
    ]
    mantle = lathe("Mantle", mantle_profile)
    head = ellipsoid("Head", (0.0, -0.03, 0.30), (0.73, 0.62, 0.48))
    crown = ellipsoid("InterbrachialWeb", (0.0, 0.0, -0.07), (0.84, 0.82, 0.17), rings=18, segments=56)
    brow_left = ellipsoid("EyeBrowL", (-0.39, -0.53, 0.36), (0.17, 0.12, 0.15), rings=12, segments=24)
    brow_right = ellipsoid("EyeBrowR", (0.39, -0.53, 0.36), (0.17, 0.12, 0.15), rings=12, segments=24)
    paths = make_arm_paths()
    arms = [tube(f"Arm_{arm}", path) for arm, path in enumerate(paths)]
    body = join_and_remesh([mantle, head, crown, brow_left, brow_right, *arms])
    add_shape_keys(body)
    rig = build_armature(paths)
    skin_body(body, rig, paths)
    eyes = add_eyes(rig)
    animate(rig, body)
    setup_preview(body, rig)

    export_asset(body, rig, eyes)
    encoded_glb = base64.b64encode(GLB_PATH.read_bytes()).decode("ascii")
    FILE_PREVIEW_DATA_PATH.write_text(
        'window.__OCTOPUS_GLB_BASE64="' + encoded_glb + '";\n',
        encoding="ascii",
    )
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    bpy.context.scene.render.filepath = str(PREVIEW_PATH)
    bpy.ops.render.render(write_still=True)

    metadata = {
        "generator": "tools/blender/build_octopus.py",
        "blender": bpy.app.version_string,
        "body_vertices": len(body.data.vertices),
        "body_polygons": len(body.data.polygons),
        "bones": len(rig.data.bones),
        "arms": ARM_COUNT,
        "bones_per_arm": ARM_BONES,
        "arm_socket_spacing_degrees": 360 // ARM_COUNT,
        "runtime_meshes": 1,
        "shape_keys": [key.name for key in body.data.shape_keys.key_blocks],
        "animation": "Octopus_Idle",
        "frame_range": [1, 121],
        "file_preview_data_bytes": FILE_PREVIEW_DATA_PATH.stat().st_size,
    }
    METADATA_PATH.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metadata, indent=2))


if __name__ == "__main__":
    main()
