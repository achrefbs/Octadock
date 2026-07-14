# Octadock octopus v2 — reference-first rebuild (replaces the v021 lane geometry).
# Corrections over build_octopus.py, driven by fab-01 / frame-034 / frame-044 and the
# gate-A underside anti-reference:
#   - blunter egg mantle (volume kept high toward the apex), soft S into the head
#   - broad "shoulder" crown ring: arm roots overlap and fuse into one muscular collar
#   - much deeper scalloped interbrachial web (bell reads as one membrane, kills the
#     starfish underside)
#   - arms with per-arm 3D personalities: varied descend angle, reach, hang depth,
#     and true out-of-plane log-spiral tip curls (never eight in-plane hooks)
#   - two featured front-lateral sweepers, like the reference hero pose
# Run: blender --background --python build_octopus2.py -- --octver 101 [--octvoxel 0.009]
#      [--octsuckers 1] [--octdump PATH]
import bpy, bmesh, math, sys, argparse, random, os, json
from mathutils import Vector, Matrix, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--octver", type=int, default=101)
ap.add_argument("--octvoxel", type=float, default=0.009)
ap.add_argument("--octsuckers", type=int, default=1)
ap.add_argument("--octdump", type=str, default="")   # write arm/eye JSON next to blend
ap.add_argument("--octout", type=str, default=r"C:\Users\acera\Desktop\Octadock-Octopus-Fable\work\blender")
args = ap.parse_args(argv)

rng = random.Random(11)

CFG = {
    # body profile: (t 0=apex..1=crown rim, radius, y_nod)  — blunt egg, widest ~38%,
    # width HELD low (soft barrel, no teardrop), then one monotonic flow into the
    # crown: no waist, no collar step — the silhouette must never show a neck ring.
    # y_nod POSITIVE = mantle leans back (+Y), away from the face (eyes at -Y);
    # the head juts slightly forward — one soft anatomical S like fab-01
    "body_ctrl": [
        (0.00, 0.000,  0.320),
        (0.01, 0.100,  0.318),   # blunt apex: radius grows fast, no cone point
        (0.02, 0.155,  0.312),
        (0.06, 0.242,  0.296),
        (0.13, 0.318,  0.262),
        (0.24, 0.366,  0.196),
        (0.36, 0.380,  0.118),   # widest — plump egg, strong backward nod
        (0.52, 0.358,  0.040),
        (0.66, 0.316, -0.004),   # mantle -> head (no neck)
        (0.80, 0.276, -0.018),   # head at eye level, jutting toward the face
        (0.91, 0.276, -0.014),
        (1.00, 0.284, -0.008),   # crown rim: barely wider, web supplies the flare
    ],
    "body_len": 1.24,            # apex..crown along Z (mantle ~0.93 + head ~0.31)
    "body_rings": 72,
    "body_segs": 84,
    "body_ell_x": 1.08,          # a touch wider than deep
    "body_ell_y": 0.95,
    "crown_z": 0.0,
    "crown_r": 0.276,

    # arms
    "n_arms": 8,
    "arm_root_r": 0.134,         # broad muscular base (roots overlap -> fused collar)
    "arm_taper_pow": 0.84,       # mid ~55% of root, 80% ~21%
    "arm_tip_min_r": 0.0068,
    "arm_rings": 120,
    "arm_segs": 16,
    "arm_oral_flatten": 0.94,

    # web
    "web_rows": 36,
    "web_cols": 19,
    "web_thick_root": 0.064,
    "web_thick_rim": 0.032,
    "web_sag": 0.16,

    # eyes / siphon — wide fleshy orbital swellings INTEGRATED into the head,
    # not knobs: large radius, embedded deep, elongated along the head
    "eye_z": 0.375, "eye_x": 0.208, "eye_y": 0.056,
    "eye_bulge_r": 0.150, "eye_squash": 0.85,
    "siphon_azim_deg": 138.0, "siphon_r0": 0.055, "siphon_r1": 0.031, "siphon_len": 0.18,  # left-rear

    # suckers
    "suck_t0": 0.06, "suck_t1": 0.94, "suck_n": 27,
    "suck_row_off_deg": 17.0, "suck_scale": 0.46, "suck_min": 0.008, "suck_max": 0.026,
}

# Per-arm personality (relaxed open bell like fab-01-left, front pair straddling camera).
# Front = -Y (the camera side). azim index 0 at 22.5deg, CCW.
#   descend: launch angle below horizontal (deg)   reach: radial distance at hang start
#   hang: depth of hang point                      turns: spiral tip revolutions
#   crl_r: spiral start radius                     tilt: spiral plane tilt off radial (deg)
#   pitch: out-of-plane helix rise per rev         wander: azimuth drift along arm (deg)
#   length: total arc length
PERS = [
    # az~22   rear-right accent (second sweeper, opens away from the front pair)
    dict(descend=39, reach=1.00, hang=1.34, turns=1.25, crl_r=0.28, tilt= 30, pitch= 0.11, wander= 7, length=2.60),
    # az~67   right
    dict(descend=33, reach=0.84, hang=1.58, turns=0.80, crl_r=0.19, tilt=-18, pitch=-0.09, wander=-5, length=2.42),
    # az~112  right-rear
    dict(descend=29, reach=0.74, hang=1.70, turns=0.55, crl_r=0.15, tilt= 14, pitch= 0.06, wander= 2, length=2.34),
    # az~157  rear
    dict(descend=32, reach=0.80, hang=1.64, turns=1.00, crl_r=0.21, tilt= 42, pitch= 0.14, wander=-6, length=2.46),
    # az~202  left-rear
    dict(descend=28, reach=0.72, hang=1.86, turns=0.50, crl_r=0.14, tilt=-12, pitch=-0.06, wander= 9, length=2.30),
    # az~247  front-left of pair (solid spiral, sweeps wide left)
    dict(descend=38, reach=0.98, hang=1.40, turns=1.05, crl_r=0.24, tilt= 22, pitch= 0.09, wander=-8, length=2.54),
    # az~292  front-right of pair (featured, biggest spiral, like fab-01 hero)
    dict(descend=44, reach=1.14, hang=1.26, turns=1.45, crl_r=0.33, tilt=-26, pitch=-0.12, wander= 6, length=2.68),
    # az~337  front accent
    dict(descend=34, reach=0.86, hang=1.50, turns=0.72, crl_r=0.17, tilt= 18, pitch= 0.08, wander=-4, length=2.40),
]

# ------------------------------------------------------------------ helpers
def catmull_rom(pts, n):
    out = []
    ts = [p[0] for p in pts]
    for i in range(n):
        t = i / (n - 1)
        k = 0
        while k < len(ts) - 2 and t > ts[k + 1]:
            k += 1
        p0 = pts[max(k - 1, 0)]; p1 = pts[k]; p2 = pts[min(k + 1, len(pts) - 1)]
        p3 = pts[min(k + 2, len(pts) - 1)]
        span = (p2[0] - p1[0]) or 1e-9
        u = (t - p1[0]) / span
        vals = []
        for c in range(1, len(p1)):
            a, b, cc, d = p0[c], p1[c], p2[c], p3[c]
            u2, u3 = u * u, u * u * u
            v = 0.5 * ((2 * b) + (-a + cc) * u + (2 * a - 5 * b + 4 * cc - d) * u2 + (-a + 3 * b - 3 * cc + d) * u3)
            vals.append(v)
        out.append(tuple(vals))
    return out

def bezier(pts, t):
    tmp = [p.copy() for p in pts]
    n = len(tmp)
    for r in range(1, n):
        for i in range(n - r):
            tmp[i] = tmp[i].lerp(tmp[i + 1], t)
    return tmp[0]

def resample_polyline(pts, n):
    """Even arc-length resample of a Vector polyline."""
    seg = [0.0]
    for i in range(1, len(pts)):
        seg.append(seg[-1] + (pts[i] - pts[i - 1]).length)
    total = seg[-1] or 1e-9
    out, j = [], 0
    for i in range(n):
        d = total * i / (n - 1)
        while j < len(seg) - 2 and seg[j + 1] < d:
            j += 1
        f = (d - seg[j]) / ((seg[j + 1] - seg[j]) or 1e-9)
        out.append(pts[j].lerp(pts[j + 1], f))
    return out, total

def parallel_frames(samples):
    frames, tans = [], []
    for i in range(len(samples)):
        if i == 0: t = (samples[1] - samples[0]).normalized()
        elif i == len(samples) - 1: t = (samples[-1] - samples[-2]).normalized()
        else: t = (samples[i + 1] - samples[i - 1]).normalized()
        tans.append(t)
    up = Vector((0, 0, 1))
    n1 = (up - tans[0] * up.dot(tans[0]))
    if n1.length < 1e-6: n1 = Vector((1, 0, 0))
    n1.normalize()
    for i in range(len(samples)):
        if i > 0:
            a, b = tans[i - 1], tans[i]
            axis = a.cross(b)
            if axis.length > 1e-8:
                ang = math.asin(min(1.0, max(-1.0, axis.length)))
                if a.dot(b) < 0: ang = math.pi - ang
                q = Quaternion(axis.normalized(), ang)
                n1 = (q @ n1).normalized()
        n2 = tans[i].cross(n1).normalized()
        frames.append((samples[i], tans[i], n1.copy(), n2))
    return frames

def new_mesh_obj(name):
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob

def grid_to_mesh(name, rows, wrap=False, close_start=False, close_end=False):
    bm = bmesh.new()
    vrows = [[bm.verts.new(v) for v in ring] for ring in rows]
    bm.verts.index_update()
    for i in range(len(vrows) - 1):
        a, b = vrows[i], vrows[i + 1]
        n = len(a)
        rng_j = range(n) if wrap else range(n - 1)
        for j in rng_j:
            j2 = (j + 1) % n
            try: bm.faces.new((a[j], a[j2], b[j2], b[j]))
            except ValueError: pass
    if close_start and len(vrows[0]) > 2:
        try: bm.faces.new(tuple(reversed(vrows[0])))
        except ValueError: pass
    if close_end and len(vrows[-1]) > 2:
        try: bm.faces.new(tuple(vrows[-1]))
        except ValueError: pass
    ob = new_mesh_obj(name)
    bm.to_mesh(ob.data)
    bm.free()
    return ob

def add_uv_sphere(name, pos, r, squash=(1, 1, 1), rot=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=14, radius=r)
    mat = Matrix.Diagonal((*squash, 1.0))
    if rot: mat = rot.to_matrix().to_4x4() @ mat
    bmesh.ops.transform(bm, matrix=mat, verts=bm.verts)
    bmesh.ops.translate(bm, vec=pos, verts=bm.verts)
    ob = new_mesh_obj(name)
    bm.to_mesh(ob.data)
    bm.free()
    return ob

# ------------------------------------------------------------------ scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene
parts = []

# ------------------------------------------------------------------ 1. body
body_rows = []
prof = catmull_rom(CFG["body_ctrl"], CFG["body_rings"])
L = CFG["body_len"]
for i, (r, yoff) in enumerate(prof):
    t = i / (CFG["body_rings"] - 1)
    z = CFG["crown_z"] + (1.0 - t) * L
    ring = []
    for j in range(CFG["body_segs"]):
        a = 2 * math.pi * j / CFG["body_segs"]
        x = math.cos(a) * r * CFG["body_ell_x"]
        y = math.sin(a) * r * CFG["body_ell_y"] + yoff
        ring.append(Vector((x, y, z)))
    body_rows.append(ring)
crown_ring = body_rows[-1]
disc_rows = [crown_ring]
for k in range(1, 7):
    f = k / 6
    ring = []
    for j in range(CFG["body_segs"]):
        v = crown_ring[j]
        c = Vector((0, 0, v.z))
        p = v.lerp(c, f)
        p.z -= 0.085 * math.sin(math.pi * f) - 0.02 * f
        ring.append(p)
    disc_rows.append(ring)
body = grid_to_mesh("body", body_rows + disc_rows[1:], wrap=True, close_start=True, close_end=True)
parts.append(body)

# ------------------------------------------------------------------ 2. arms
arm_data = []
n = CFG["n_arms"]
for i in range(n):
    P = PERS[i]
    azim = 2 * math.pi * (i + 0.5) / n + math.radians(rng.uniform(-2.5, 2.5))
    u = Vector((math.cos(azim), math.sin(azim), 0))
    side = Vector((-math.sin(azim), math.cos(azim), 0))
    Rc = CFG["crown_r"]
    dsc = math.radians(P["descend"])
    wander = math.radians(P["wander"])

    # shoulder -> hang as a bezier: leave the collar wide and shallow, then descend
    reach, hang = P["reach"], P["hang"]
    hang_pt = u * reach + side * math.sin(wander) * 0.42 + Vector((0, 0, -hang))
    ctrl = [
        u * (Rc - 0.075) + Vector((0, 0, 0.075)),                 # inside collar (fusion)
        u * (Rc + 0.16) + Vector((0, 0, -0.010)),                 # exit wide, near-level
        u * (Rc + 0.42) + Vector((0, 0, -0.42 * math.tan(dsc))),  # committed descend
        u * (reach * 0.86) + side * math.sin(wander) * 0.22 + Vector((0, 0, -hang * 0.62)),
        hang_pt,
    ]
    bez = [bezier(ctrl, tt / 63.0) for tt in range(64)]

    # spiral tip: log spiral in a plane tilted off the radial plane, with helix pitch
    t_end = (bez[-1] - bez[-2]).normalized()
    tilt = math.radians(P["tilt"])
    # plane basis: e1 = incoming tangent; e2 = tilted lateral
    lat = (side * math.cos(tilt) + u * math.sin(tilt) * 0.4 + Vector((0, 0, math.sin(tilt) * 0.35))).normalized()
    e2 = (lat - t_end * lat.dot(t_end)).normalized()
    e3 = t_end.cross(e2).normalized()
    turns, r0 = P["turns"], P["crl_r"]
    r_end = max(r0 * 0.14, 0.02)
    kdec = math.log(r0 / r_end) / (turns * 2 * math.pi)
    Cc = bez[-1] + e2 * r0
    spiral = []
    steps = max(18, int(turns * 26))
    for sidx in range(1, steps + 1):
        th = turns * 2 * math.pi * sidx / steps
        rr = r0 * math.exp(-kdec * th)
        pt = Cc - e2 * (rr * math.cos(th)) + t_end * (rr * math.sin(th)) + e3 * (P["pitch"] * th / (2 * math.pi))
        spiral.append(pt)

    samples, total = resample_polyline(bez + spiral, CFG["arm_rings"])
    # scale to the personality length around the root
    scale = P["length"] / total
    base = samples[0]
    samples = [base + (s - base) * scale for s in samples]
    frames = parallel_frames(samples)
    rows, radii, orals = [], [], []
    for k, (pos, tan, n1, n2) in enumerate(frames):
        t = k / (len(frames) - 1)
        r = max(CFG["arm_root_r"] * (1 - t) ** CFG["arm_taper_pow"], CFG["arm_tip_min_r"])
        radii.append(r)
        to_axis = Vector((0, 0, pos.z)) - pos
        oral = (to_axis - tan * to_axis.dot(tan))
        if oral.length < 1e-6: oral = n1.copy()
        oral.normalize()
        orals.append(oral)
        ring = []
        for j in range(CFG["arm_segs"]):
            a = 2 * math.pi * j / CFG["arm_segs"]
            d = (n1 * math.cos(a) + n2 * math.sin(a))
            flat = 1.0 - (1.0 - CFG["arm_oral_flatten"]) * max(0.0, d.dot(oral))
            ring.append(pos + d * r * flat)
        rows.append(ring)
    ob = grid_to_mesh(f"arm_{i}", rows, wrap=True, close_start=True, close_end=True)
    parts.append(ob)
    arm_data.append({"azim": azim, "frames": frames, "radii": radii, "orals": orals,
                     "len": P["length"], "samples": samples})

# ------------------------------------------------------------------ 3. deep scalloped web
# depth per sector: dorsal (rear, +Y) deepest, ventral (front, -Y) shallowest
for i in range(n):
    a0 = arm_data[i]
    a1 = arm_data[(i + 1) % n]
    mid_az = a0["azim"] + (((a1["azim"] - a0["azim"]) % (2 * math.pi)) / 2)
    m = Vector((math.cos(mid_az), math.sin(mid_az), 0))
    # depth varies per ARM (continuous around the crown, no per-sector steps):
    # dorsal (+Y) deepest, ventral (-Y) shallowest
    def arm_frac(az):
        vn = 0.5 - 0.5 * math.cos(az - math.radians(270))
        return 0.44 * (1 - vn) + 0.30 * vn
    fracA, fracB = arm_frac(a0["azim"]), arm_frac(a1["azim"])
    rows_n, cols = CFG["web_rows"], CFG["web_cols"]

    def surf_point(ad, tt):
        idx = min(int(tt * (len(ad["frames"]) - 1)), len(ad["frames"]) - 1)
        pos, tan, n1, n2 = ad["frames"][idx]
        r = ad["radii"][idx]
        d = (m - tan * m.dot(tan))
        if d.length < 1e-6: d = n1
        d = d.normalized()
        return pos + d * r * 0.78

    top_rows = []
    for kr in range(rows_n):
        s = kr / (rows_n - 1)
        row = []
        pinch = CFG["web_sag"] * (0.22 + 0.78 * s)
        for jc in range(cols):
            uu = jc / (cols - 1)
            w = math.sin(math.pi * uu)
            frac_u = (fracA + (fracB - fracA) * uu) * (1.0 - 0.28 * w)  # catenary arcs
            tt = s * frac_u
            A = surf_point(a0, tt); B = surf_point(a1, tt)
            p = A.lerp(B, uu)
            rad = math.hypot(p.x, p.y)
            new_rad = max(rad * (1.0 - pinch * w), 0.11)
            if rad > 1e-6:
                f = new_rad / rad
                p = Vector((p.x * f, p.y * f, p.z - 0.045 * w * (0.15 + 0.85 * s)))
            row.append(p)
        top_rows.append(row)
    for kr in range(2):   # blend first rows into a ring tucked tight under the rim
        f = 1.0 - kr / 2.0
        for jc in range(cols):
            uu = jc / (cols - 1)
            az = a0["azim"] + (((a1["azim"] - a0["azim"]) % (2 * math.pi)) * uu)
            ring_p = Vector((math.cos(az) * CFG["crown_r"] * (0.94 + 0.03 * kr),
                             math.sin(az) * CFG["crown_r"] * (0.94 + 0.03 * kr), 0.040 - 0.034 * kr))
            top_rows[kr][jc] = top_rows[kr][jc].lerp(ring_p, f)
    bot_rows = []
    for kr, row in enumerate(top_rows):
        s = kr / (rows_n - 1)
        th = CFG["web_thick_root"] * (1 - s) + CFG["web_thick_rim"] * s
        brow = []
        for p in row:
            rad = math.hypot(p.x, p.y)
            if rad > 1e-6:
                f = max(rad - th, 0.06) / rad
                brow.append(Vector((p.x * f, p.y * f, p.z + th * 0.35)))
            else:
                brow.append(p + Vector((0, 0, th)))
        bot_rows.append(brow)
    bm = bmesh.new()
    vt = [[bm.verts.new(v) for v in row] for row in top_rows]
    vb = [[bm.verts.new(v) for v in row] for row in bot_rows]
    def quad(a, b, c, d):
        try: bm.faces.new((a, b, c, d))
        except ValueError: pass
    for r in range(rows_n - 1):
        for c in range(cols - 1):
            quad(vt[r][c], vt[r][c + 1], vt[r + 1][c + 1], vt[r + 1][c])
            quad(vb[r + 1][c], vb[r + 1][c + 1], vb[r][c + 1], vb[r][c])
    for c in range(cols - 1):
        quad(vt[rows_n - 1][c], vt[rows_n - 1][c + 1], vb[rows_n - 1][c + 1], vb[rows_n - 1][c])
        quad(vb[0][c], vb[0][c + 1], vt[0][c + 1], vt[0][c])
    for r in range(rows_n - 1):
        quad(vt[r][0], vt[r + 1][0], vb[r + 1][0], vb[r][0])
        quad(vb[r][cols - 1], vb[r + 1][cols - 1], vt[r + 1][cols - 1], vt[r][cols - 1])
    ob = new_mesh_obj(f"web_{i}")
    bm.to_mesh(ob.data); bm.free()
    parts.append(ob)

# ------------------------------------------------------------------ 4. eyes (bulge + hooded ridge)
for sx in (-1, 1):
    pos_on = Vector((CFG["eye_x"] * sx, -CFG["eye_y"], CFG["eye_z"]))   # slightly toward front (-Y)
    # wide orbital swelling, elongated along the head so it flows into the profile
    ob = add_uv_sphere(f"eyebulge_{'L' if sx < 0 else 'R'}", pos_on, CFG["eye_bulge_r"],
                       squash=(CFG["eye_squash"], 1.18, 0.95))
    parts.append(ob)
    ridge = add_uv_sphere(f"eyeridge_{'L' if sx < 0 else 'R'}",
                          pos_on + Vector((0.008 * sx, 0.022, 0.070)),
                          CFG["eye_bulge_r"] * 0.80,
                          squash=(0.70, 1.14, 0.52))
    parts.append(ridge)

# ------------------------------------------------------------------ 5. siphon
saz = math.radians(CFG["siphon_azim_deg"])
su = Vector((math.cos(saz), math.sin(saz), 0))
sp0 = su * (CFG["crown_r"] * 0.55) + Vector((0, 0, 0.12))
sp1 = su * (CFG["crown_r"] + 0.11) + Vector((0, 0, 0.0))
sp2 = su * (CFG["crown_r"] + 0.18) + Vector((0, 0, -0.09))
s_samples = [bezier([sp0, sp1, sp2], tt / 13.0) for tt in range(14)]
s_frames = parallel_frames(s_samples)
rows = []
for k, (pos, tan, n1, n2) in enumerate(s_frames):
    t = k / (len(s_frames) - 1)
    r = CFG["siphon_r0"] * (1 - t) + CFG["siphon_r1"] * t
    rows.append([pos + (n1 * math.cos(2 * math.pi * j / 12) + n2 * math.sin(2 * math.pi * j / 12)) * r for j in range(12)])
siphon = grid_to_mesh("siphon", rows, wrap=True, close_start=True, close_end=True)
parts.append(siphon)

# ------------------------------------------------------------------ 6. suckers
if args.octsuckers:
    for ad in arm_data:
        frames, radii, orals = ad["frames"], ad["radii"], ad["orals"]
        N = len(frames)
        for k in range(CFG["suck_n"]):
            t = CFG["suck_t0"] + (CFG["suck_t1"] - CFG["suck_t0"]) * (k / (CFG["suck_n"] - 1)) ** 1.18
            for row_side in (-1, 1):
                stag = (CFG["suck_t1"] - CFG["suck_t0"]) / CFG["suck_n"] * 0.5
                t2 = min(t + (stag if row_side > 0 else 0), 0.975)
                idx2 = min(int(t2 * (N - 1)), N - 1)
                pos2, tan2, n12, n22 = frames[idx2]
                r2 = radii[idx2]; oral2 = orals[idx2]
                srad = max(min(r2 * CFG["suck_scale"], CFG["suck_max"]), CFG["suck_min"])
                ang = math.radians(CFG["suck_row_off_deg"]) * row_side
                d = (oral2 * math.cos(ang) + tan2.cross(oral2).normalized() * math.sin(ang)).normalized()
                center = pos2 + d * (r2 * 0.94 - srad * 0.34)
                ob = add_uv_sphere("suck", center, srad, squash=(1, 1, 0.60),
                                   rot=d.to_track_quat('Z', 'Y'))
                parts.append(ob)

# ------------------------------------------------------------------ dump (arms/eyes for rig+runtime)
if args.octdump:
    dump = []
    for ad in arm_data:
        dump.append({
            "azim": ad["azim"],
            "len": ad["len"],
            "points": [[p.x, p.y, p.z] for p in ad["samples"]],
            "radii": ad["radii"],
        })
    with open(args.octdump, "w") as f:
        json.dump({"arms": dump, "crown_z": CFG["crown_z"], "crown_r": CFG["crown_r"],
                   "body_len": CFG["body_len"],
                   "eye_l": [-CFG["eye_x"] - CFG["eye_bulge_r"] * CFG["eye_squash"] * 0.42, -CFG["eye_y"] - 0.012, CFG["eye_z"] + 0.004],
                   "eye_r": [ CFG["eye_x"] + CFG["eye_bulge_r"] * CFG["eye_squash"] * 0.42, -CFG["eye_y"] - 0.012, CFG["eye_z"] + 0.004]}, f)
    print(f"[build2] dumped arm data -> {args.octdump}")

# ------------------------------------------------------------------ 7. join + fuse
for ob in parts:
    ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
octo = bpy.context.view_layer.objects.active
octo.name = "Octopus"

octo.data.remesh_voxel_size = args.octvoxel
octo.data.remesh_voxel_adaptivity = 0.0
bpy.ops.object.voxel_remesh()

mod = octo.modifiers.new("smooth", 'SMOOTH')
mod.factor = 0.55
mod.iterations = 5
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.object.shade_smooth()

# ------------------------------------------------------------------ 8. eyeballs
for sx in (-1, 1):
    bulge_front = CFG["eye_x"] + CFG["eye_bulge_r"] * CFG["eye_squash"]
    pos = Vector(((bulge_front - 0.036) * sx, -CFG["eye_y"] - 0.014, CFG["eye_z"] + 0.006))
    eye = add_uv_sphere(f"eyeball_{'L' if sx < 0 else 'R'}", pos, 0.060)
    bpy.context.view_layer.objects.active = eye
    eye.select_set(True)
    bpy.ops.object.shade_smooth()
    eye.select_set(False)

# ------------------------------------------------------------------ 9. clay materials
mat = bpy.data.materials.new("octo_clay")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.60, 0.29, 0.21, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.42
octo.data.materials.append(mat)
emat = bpy.data.materials.new("octo_eye")
emat.use_nodes = True
eb = emat.node_tree.nodes.get("Principled BSDF")
if eb:
    eb.inputs["Base Color"].default_value = (0.78, 0.74, 0.62, 1.0)
    eb.inputs["Roughness"].default_value = 0.14
for ob in bpy.data.objects:
    if ob.name.startswith("eyeball"):
        ob.data.materials.append(emat)

os.makedirs(args.octout, exist_ok=True)
path = os.path.join(args.octout, f"octopus_v{args.octver:03d}.blend")
bpy.ops.wm.save_as_mainfile(filepath=path)
print(f"[build2] saved {path}; verts={len(octo.data.vertices)} faces={len(octo.data.polygons)}")
