# Author v2 octopus clips (idle, swim, swimB, turnDown, turnUp, flipLag) as NLA actions.
# Reference grammar (REFERENCE-ANALYSIS.md §4):
#   bloom opens PROXIMALLY (roots/web first, tips lag) -> power stroke sweeps to the
#   bundle while the mantle compresses 5-8% -> glide, tips settle LAST with overshoot.
#   Adjacent arms never share phase; dorsal pair carries independent tip curls.
# Run: blender --background octopus_v105_rigged.blend --python anim_octopus2.py -- --octver 106
import bpy, sys, argparse, os, math

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--octver", type=int, default=106)
ap.add_argument("--octout", type=str, default=r"C:\Users\acera\Desktop\Octadock-Octopus-Fable\work\blender")
args = ap.parse_args(argv)

FPS = 24
scn = bpy.context.scene
scn.render.fps = FPS
rig = bpy.data.objects.get("OctoRig")
assert rig

# per-arm personality: amplitude, phase offset (frames), tip-curl dir, z-sway scale
AMP =   [1.06, 0.88, 1.14, 0.84, 1.02, 0.94, 1.18, 0.90]
PHASE = [ 0.0,  3.2, -2.6,  4.6,  1.4, -2.0,  2.6, -3.8]
TIPD =  [ 1,   -1,    1,    1,   -1,    1,   -1,   -1]
ZS =    [ 0.9,  1.2,  0.7,  1.4,  0.8,  1.1,  0.6,  1.3]
N = 14
BONE_DELAY = 1.25            # proximal->distal propagation, frames per bone

def pb(name): return rig.pose.bones.get(name)

def key_rot(bone, frame, x=0.0, y=0.0, z=0.0):
    b = pb(bone)
    if not b: return
    b.rotation_mode = 'XYZ'
    b.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
    b.keyframe_insert("rotation_euler", frame=frame)

def key_scale(bone, frame, sx=1.0, sy=1.0, sz=1.0):
    b = pb(bone)
    if not b: return
    b.scale = (sx, sy, sz)
    b.keyframe_insert("scale", frame=frame)

def key_loc(bone, frame, x=0.0, y=0.0, z=0.0):
    b = pb(bone)
    if not b: return
    b.location = (x, y, z)
    b.keyframe_insert("location", frame=frame)

def new_action(name):
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    for b in rig.pose.bones:
        b.rotation_mode = 'XYZ'
        b.rotation_euler = (0, 0, 0)
        b.scale = (1, 1, 1)
        b.location = (0, 0, 0)
    return act

def push_nla(act, name):
    tr = rig.animation_data.nla_tracks.new()
    tr.name = name
    tr.strips.new(name, 1, act)
    rig.animation_data.action = None

# ---------------------------------------------------------------- IDLE (120f loop)
def author_idle():
    act = new_action("idle")
    L = 120
    for f0 in range(0, L + 1, 8):
        f = f0 + 1
        t = f0 / L * 2 * math.pi
        br = 1.0 + 0.024 * math.sin(t)          # mantle breathing
        key_scale("body_2", f, br ** 0.5, 1.0 / br, br ** 0.5)
        key_scale("body_3", f, br ** 0.7, 1.0 / (br ** 1.25), br ** 0.7)
        key_scale("body_4", f, br ** 0.5, 1.0 / br, br ** 0.5)
        key_loc("root", f, 0, 0, 0.022 * math.sin(t))
        key_rot("body_1", f, x=1.3 * math.sin(t * 0.5 + 1.0), z=0.8 * math.sin(t * 0.35 + 0.6))
    # arm drift: slow, phase-offset undulation; tips carry extra residual curls
    for i in range(8):
        for k in range(N):
            bone = f"arm{i}_{k:02d}"
            lag = k * BONE_DELAY * 1.7
            amp = (1.5 if k < 5 else (1.0 if k < 9 else 2.5)) * AMP[i]
            for f0 in range(0, L + 1, 8):
                f = f0 + 1
                t = ((f0 - PHASE[i] * 2.4 - lag) / L) * 2 * math.pi
                x = amp * math.sin(t)
                z = 0.6 * amp * math.sin(t * 0.5 + i * 1.1) * (1 if k > 8 else 0) * TIPD[i]
                key_rot(bone, f, x=x, z=z)
    # dorsal pair independent tip pulses (arms 2 and 6 = the featured pair region)
    for i in (2, 6):
        for k in range(10, N):
            bone = f"arm{i}_{k:02d}"
            for f0, xx in ((0, 0.0), (30, 3.4 * TIPD[i]), (66, -1.6 * TIPD[i]), (96, 1.2 * TIPD[i]), (L, 0.0)):
                key_rot(bone, f0 + 1, x=xx + 1.2, z=1.5 * TIPD[i])
    push_nla(act, "idle")

# ---------------------------------------------------------------- SWIM
def swim_arm_profile(k, phase_name, amp_scale, tip_dir):
    prox = max(0.0, 1.0 - k / 6.0)
    mid = max(0.0, 1.0 - abs(k - 7) / 5.0)
    tip = max(0.0, (k - 8) / 5.0)
    if phase_name == "neutral":
        return (1.5 * prox - 2.0 * tip) * amp_scale
    if phase_name == "bloom":
        return (10.5 * prox + 4.5 * mid + 1.2 * tip * tip_dir) * amp_scale
    if phase_name == "stroke":
        return (-8.5 * prox - 5.2 * mid - 2.2 * tip) * amp_scale
    if phase_name == "glide":
        return (-4.5 * prox - 2.6 * mid + 1.2 * tip * tip_dir) * amp_scale
    if phase_name == "settle":   # tip overshoot after the body has settled
        return (-2.2 * prox - 1.2 * mid + 3.0 * tip * tip_dir) * amp_scale
    return 0.0

def author_swim(name="swim", amp_mult=1.0, phase_shift=0.0, z_sway=3.2):
    act = new_action(name)
    L = 96
    # mantle: extend through bloom, compress hard at the stroke, relax in glide
    body_keys = [
        (1,             1.000,  0.000),
        (int(L * 0.26), 1.050,  0.018),
        (int(L * 0.45), 0.930, -0.026),
        (int(L * 0.66), 0.985,  0.010),
        (L + 1,         1.000,  0.000),
    ]
    for f, ext, rz in body_keys:
        inv = 1.0 / ext
        key_scale("body_2", f, inv ** 0.5, ext, inv ** 0.5)
        key_scale("body_3", f, inv ** 0.6, ext ** 1.18, inv ** 0.6)
        key_scale("body_4", f, inv ** 0.5, ext, inv ** 0.5)
        key_loc("root", f, 0, 0, rz)
    key_rot("body_1", 1, x=0)
    key_rot("body_1", int(L * 0.28), x=2.2)
    key_rot("body_1", int(L * 0.48), x=-2.8)
    key_rot("body_1", L + 1, x=0)

    for i in range(8):
        a = AMP[i] * amp_mult
        for k in range(N):
            bone = f"arm{i}_{k:02d}"
            lag = k * BONE_DELAY + PHASE[i] + phase_shift
            tipd = TIPD[i]
            keys = [
                (1 + lag,             swim_arm_profile(k, "neutral", a, tipd)),
                (int(L * 0.26) + lag, swim_arm_profile(k, "bloom", a, tipd)),
                (int(L * 0.48) + lag, swim_arm_profile(k, "stroke", a, tipd)),
                (int(L * 0.72) + lag, swim_arm_profile(k, "glide", a, tipd)),
                (int(L * 0.87) + lag, swim_arm_profile(k, "settle", a, tipd)),
                (L + 1 + lag,         swim_arm_profile(k, "neutral", a, tipd)),
            ]
            for f, x in keys:
                z = 0.0
                if k >= 4:
                    z = z_sway * ZS[i] * math.sin(2 * math.pi * (f / L) + i * 0.9) * (k / N) * 0.8
                key_rot(bone, round(f), x=x, z=z)
    push_nla(act, name)

# ---------------------------------------------------------------- TURN (48f, non-loop)
def author_turn(name, sign):
    act = new_action(name)
    L = 48
    for f, ang in ((1, 0.0), (int(L * 0.55), sign * 118.0), (L + 1, sign * 180.0)):
        key_rot("root", f, x=ang)
    for i in range(8):
        a = AMP[i]
        for k in range(N):
            bone = f"arm{i}_{k:02d}"
            lag = k * BONE_DELAY * 1.4 + PHASE[i]
            keys = [
                (1 + lag, swim_arm_profile(k, "neutral", a, TIPD[i])),
                (int(L * 0.5) + lag, swim_arm_profile(k, "bloom", a * 0.85, TIPD[i])),
                (L + 1 + lag, swim_arm_profile(k, "glide", a, TIPD[i])),
            ]
            for f, x in keys:
                key_rot(bone, round(f), x=x)
    key_scale("body_3", 1, 1, 1, 1)
    key_scale("body_3", int(L * 0.3), 1.06, 0.90, 1.06)
    key_scale("body_3", L + 1, 1, 1, 1)
    push_nla(act, name)

# additive arm-inertia overlay while the page flips the body (reversal)
def author_fliplag():
    act = new_action("flipLag")
    L = 42
    for i in range(8):
        a = AMP[i] * 0.65
        for k in range(N):
            bone = f"arm{i}_{k:02d}"
            lag = k * BONE_DELAY * 1.6 + PHASE[i]
            for f, x in ((1 + lag, 0.0),
                         (int(L * 0.42) + lag, swim_arm_profile(k, "bloom", a, TIPD[i])),
                         (int(L * 0.78) + lag, swim_arm_profile(k, "settle", a * 0.7, TIPD[i])),
                         (L + 1 + lag, 0.0)):
                key_rot(bone, round(f), x=x)
    push_nla(act, "flipLag")

author_idle()
author_swim("swim")
author_swim("swimB", amp_mult=0.84, phase_shift=7.0, z_sway=4.4)
author_turn("turnDown", +1)
author_turn("turnUp", -1)
author_fliplag()

scn.frame_start = 1
scn.frame_end = 120

path = os.path.join(args.octout, f"octopus_v{args.octver:03d}_animated.blend")
bpy.ops.wm.save_as_mainfile(filepath=path)
print(f"[anim2] saved {path}; actions={[a.name for a in bpy.data.actions]}")
