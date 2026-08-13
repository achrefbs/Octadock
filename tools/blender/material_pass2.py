# Wet-skin SSS material pass for the v2 octopus + Cycles studio render setup.
# Zoning per REFERENCE-ANALYSIS §3: terracotta dorsal -> peachy oral/web, fine
# mottle freckles, convexity (Pointiness) lightening for sucker rims/web edges.
# Run: blender --background octopus_v106_animated.blend --python material_pass2.py -- --octver 107
import bpy, sys, argparse, os

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--octver", type=int, default=107)
ap.add_argument("--octout", type=str, default=r"C:\Users\acera\Desktop\Projects\Octadock\octopus-fable\work\blender")
args = ap.parse_args(argv)

octo = bpy.data.objects.get("Octopus")
assert octo

# ---------------- skin material
mat = bpy.data.materials.get("octo_clay") or bpy.data.materials.new("octo_skin")
mat.name = "octo_skin"
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()
n_out = nt.nodes.new("ShaderNodeOutputMaterial")
n_bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
n_geo = nt.nodes.new("ShaderNodeNewGeometry")
n_sep = nt.nodes.new("ShaderNodeSeparateXYZ")
n_zmap = nt.nodes.new("ShaderNodeMapRange")     # world z -> dorsal/oral factor
n_ramp = nt.nodes.new("ShaderNodeValToRGB")     # dorsal->oral color
n_noise = nt.nodes.new("ShaderNodeTexNoise")    # mottle
n_mix = nt.nodes.new("ShaderNodeMix")
n_point = nt.nodes.new("ShaderNodeValToRGB")    # pointiness ramp (sucker/web rims)
n_mix2 = nt.nodes.new("ShaderNodeMix")

n_zmap.inputs["From Min"].default_value = -1.6
n_zmap.inputs["From Max"].default_value = 1.1

el = n_ramp.color_ramp.elements
el[0].position = 0.0
el[0].color = (0.780, 0.460, 0.310, 1.0)   # oral/web peach (linear-ish)
el[1].position = 1.0
el[1].color = (0.355, 0.082, 0.042, 1.0)   # dorsal terracotta (deep rust)
mid = n_ramp.color_ramp.elements.new(0.52)
mid.color = (0.560, 0.180, 0.095, 1.0)

n_noise.inputs["Scale"].default_value = 34.0
n_noise.inputs["Detail"].default_value = 6.0
n_noise.inputs["Roughness"].default_value = 0.62

n_mix.data_type = 'RGBA'
n_mix.blend_type = 'MULTIPLY'
n_mix.inputs["Factor"].default_value = 0.10   # subtle freckle darkening

pel = n_point.color_ramp.elements
pel[0].position = 0.56                        # smooth skin sits ~0.5 -> zero effect
pel[0].color = (0.0, 0.0, 0.0, 1.0)
pel[1].position = 0.80
pel[1].color = (1.0, 1.0, 1.0, 1.0)
n_pscale = nt.nodes.new("ShaderNodeMath")     # cap rim lightening at 55%
n_pscale.operation = 'MULTIPLY'
n_pscale.inputs[1].default_value = 0.55

n_mix2.data_type = 'RGBA'
n_mix2.blend_type = 'MIX'
n_mix2.inputs["Factor"].default_value = 0.0   # linked below
n_mix2.inputs["B"].default_value = (0.870, 0.640, 0.520, 1.0)  # cream rims

lk = nt.links.new
lk(n_geo.outputs["Position"], n_sep.inputs["Vector"])
lk(n_sep.outputs["Z"], n_zmap.inputs["Value"])
lk(n_zmap.outputs["Result"], n_ramp.inputs["Fac"])
lk(n_geo.outputs["Position"], n_noise.inputs["Vector"])
lk(n_ramp.outputs["Color"], n_mix.inputs["A"])
lk(n_noise.outputs["Color"], n_mix.inputs["B"])
lk(n_geo.outputs["Pointiness"], n_point.inputs["Fac"])
lk(n_mix.outputs["Result"], n_mix2.inputs["A"])
lk(n_point.outputs["Color"], n_pscale.inputs[0])
lk(n_pscale.outputs["Value"], n_mix2.inputs["Factor"])
lk(n_mix2.outputs["Result"], n_bsdf.inputs["Base Color"])
lk(n_bsdf.outputs["BSDF"], n_out.inputs["Surface"])

n_bsdf.inputs["Roughness"].default_value = 0.38
if "Subsurface Weight" in n_bsdf.inputs:
    n_bsdf.inputs["Subsurface Weight"].default_value = 0.08
if "Subsurface Radius" in n_bsdf.inputs:
    n_bsdf.inputs["Subsurface Radius"].default_value = (0.10, 0.035, 0.022)
if "Coat Weight" in n_bsdf.inputs:
    n_bsdf.inputs["Coat Weight"].default_value = 0.12
    n_bsdf.inputs["Coat Roughness"].default_value = 0.18

octo.data.materials.clear()
octo.data.materials.append(mat)

# ---------------- eye material (amber iris, dark pupil via facing gradient)
emat = bpy.data.materials.get("octo_eye") or bpy.data.materials.new("octo_eye")
emat.use_nodes = True
ent = emat.node_tree
ent.nodes.clear()
e_out = ent.nodes.new("ShaderNodeOutputMaterial")
e_bsdf = ent.nodes.new("ShaderNodeBsdfPrincipled")
e_lw = ent.nodes.new("ShaderNodeLayerWeight")
e_ramp = ent.nodes.new("ShaderNodeValToRGB")
eel = e_ramp.color_ramp.elements
eel[0].position = 0.30
eel[0].color = (0.020, 0.010, 0.008, 1.0)   # pupil-dark core
eel[1].position = 0.62
eel[1].color = (0.520, 0.330, 0.140, 1.0)   # amber iris ring
ent.links.new(e_lw.outputs["Facing"], e_ramp.inputs["Fac"])
ent.links.new(e_ramp.outputs["Color"], e_bsdf.inputs["Base Color"])
ent.links.new(e_bsdf.outputs["BSDF"], e_out.inputs["Surface"])
e_bsdf.inputs["Roughness"].default_value = 0.10
for enm in ("eyeball_L", "eyeball_R"):
    eob = bpy.data.objects.get(enm)
    if eob:
        eob.data.materials.clear()
        eob.data.materials.append(emat)

path = os.path.join(args.octout, f"octopus_v{args.octver:03d}_material.blend")
bpy.ops.wm.save_as_mainfile(filepath=path)
print(f"[mat2] saved {path}")
