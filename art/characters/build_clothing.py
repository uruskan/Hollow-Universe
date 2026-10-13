"""Builds the garments: game/assets/characters/clothing.glb, its textures and clothing.json.

    blender -b --factory-startup --python art/characters/build_clothing.py

Needs art/.cache/human_male.blend from build_body.py. Every garment is a shell cut from the body's own surface, so it
fits, carries the body's weights and regions, and is skinned to the same skeleton:

    knit cap · woven shirt · woven trousers · laced boots · woven gloves · riveted breastplate (plates, shoulder caps,
    straps, buckles) · metal mask (a welded face plate, band and strap) · woven hood (over the shirt and the mask)

Layers, from the skin out (metres off the skin): trousers 6 mm, shirt 10–25 mm (it hangs), boots 12–20 mm. The
breastplate is not cut from the body: its plates are rigid shells standing 25 mm clear of the shirt. The client hides
body regions under garments and parts of inner garments under outer ones (GROUND.md §4.8).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "lib"))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import garment as G  # noqa: E402
import hollow_art as H  # noqa: E402
import surfaces as S  # noqa: E402

R = H.REGION
LODS = (1.0, 0.42, 0.16)


def main():
    a = H.args()
    out = os.path.abspath(a.get("out", H.CHARACTERS))
    # The male's garments are clothing.glb; the female's (-- --body female) clothing_female.glb, with their own
    # surfaces and textures (suffix "_female"), since they are cut from her body.
    which = a.get("body", "male")
    suffix = "" if which == "male" else "_" + which
    bpy.ops.wm.open_mainfile(filepath=os.path.join(H.CACHE, "human_%s.blend" % which))
    body = bpy.data.objects["body_source"]
    rig = bpy.data.objects["Rig"]
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o is not body:
            bpy.data.objects.remove(o)
    marks = Marks(rig)
    marks.female = which == "female"
    with open(os.path.join(out, "human_%s.json" % which), encoding="utf-8") as f:
        marks.eye_z = json.load(f)["eye_height_m"]
    textures = os.path.join(out, "textures")
    detail_textures(textures)

    shirt = woven_shirt(body, marks)
    mask = metal_mask(body, marks, which)
    garments = [
        ("knit_cap", knit_cap(body, marks)),
        ("woven_shirt", shirt),
        ("woven_trousers", woven_trousers(body, marks)),
        ("laced_boots", laced_boots(body, marks)),
        ("woven_gloves", woven_gloves(body, marks)),
        ("metal_mask", mask),
        # The hood clears the shirt and the mask, so it goes over both.
        ("woven_hood", woven_hood(body, marks, which, shirt[0], mask[0])),
        # The plate is shaped over the shirt, so it always clears it.
        ("riveted_breastplate", breastplate(body, marks, shirt[0])),
    ]
    surfaces = {}
    exported = []
    counts = {}
    for name, (obj, paint) in garments:
        if obj.get("rigid") is None:
            G.weights_from_body(obj, body)
            if obj.get("weight_smoothing"):
                G.smooth_weights(obj, int(obj["weight_smoothing"]))
        else:
            # Armour is skinned part by part as it is built (rigid plates); keep it that way.
            H.limit_and_normalise_weights(obj)
        G.regions_from_body(obj, body)
        G.pack_uvs(obj)
        surfaces[name + suffix] = paint(obj, textures, name + suffix)
        obj.data.materials.clear()
        obj.data.materials.append(bpy.data.materials.get(name + suffix) or bpy.data.materials.new(name + suffix))
        H.parent_to_rig(obj, rig)
        for level, ratio in enumerate(LODS):
            copy = H.duplicate(obj, "%s_lod%d" % (name, level))
            if ratio < 1.0:
                H.decimate(copy, ratio)
                copy.data.validate(clean_customdata=False)
                H.limit_and_normalise_weights(copy)
            counts[copy.name] = H.triangles(copy)
            exported.append(copy)
        bpy.data.objects.remove(obj)
    H.log("garments:", counts)
    H.export_glb(os.path.join(out, "clothing%s.glb" % suffix), [rig] + exported)
    H.write_json(os.path.join(out, "clothing%s.json" % suffix), {
        "source": "art/characters/build_clothing.py (our own work)",
        "meshes": {o.name: {"lods": [int(o.name[-1])], "triangles": counts[o.name]} for o in exported},
        "surfaces": surfaces,
    })
    if a.get("preview"):
        import preview
        for name, _ in garments:
            m = bpy.data.materials[name + suffix]
            m["preview_colour"] = surfaces[name + suffix]["preview"]
        body.hide_render = False
        preview.render(a["preview"], [rig, body] + [o for o in exported if o.name.endswith("lod0")], out)


class Marks:
    """Landmarks of the body in its rest pose (Blender space: up +Z, front −Y, left +X)."""

    def __init__(self, rig):
        b = {x.name: (rig.matrix_world @ x.head_local, rig.matrix_world @ x.tail_local) for x in rig.data.bones}
        self.b = b
        self.hip = b["thigh_l"][0].z
        self.waist = self.hip + 0.045          # top of the underwear
        self.knee = b["calf_l"][0]
        self.ankle_z = b["foot_l"][0].z
        self.boot_top = b["calf_l"][1].z + 0.175
        self.neck = (b["clavicle_l"][0] + b["clavicle_r"][0]) / 2
        self.head = b["head"][0]
        self.spine_y = (b["spine_01"][0].y + b["spine_03"][0].y + b["spine_03"][1].y) / 3
        self.female = False
        self.eye_z = self.head.z + 0.04

    def head_of(self, bone):
        return self.b[bone][0]

    def tail_of(self, bone):
        return self.b[bone][1]


def near_segment(a, b, radius, lo=0.0, hi=1.0, side=0):
    """Selects vertices beside the segment a→b: projected between lo and hi (fractions of its length), within radius,
    and on one side of the body (side = +1 left, −1 right, 0 either)."""
    d = (b - a)
    length = d.length
    d = d / length

    def select(co):
        if side and co.x * side <= 0:
            return False
        t = (co - a).dot(d) / length
        if not lo <= t <= hi:
            return False
        return ((co - a) - d * (co - a).dot(d)).length < radius
    return select


def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- garments

def woven_shirt(body, m):
    """A long-sleeved work shirt: a closed crew neck with a ribbed band, full shoulders and back, sleeves to the wrist
    with snug cuffs, and a hem that hangs over the belt. Cross-sections are made convex, so the cloth spans the chest
    instead of following it, and it hangs straight from the chest and shoulder blades."""
    hem_z = m.hip - 0.035
    keep = {R["chest"], R["belly"], R["upper_arm"], R["forearm"], R["collar"]}

    def wanted(f, r):
        c = f.calc_center_median()
        near_wrist = r == R["hand"] and any((c - m.head_of("hand" + s)).length < 0.05 for s in ("_l", "_r"))
        return (r in keep or (r == R["pelvis"] and c.z > hem_z - 0.03) or (r == R["neck"] and c.z < 1.56)
                or near_wrist)

    obj = G.extract(body, "woven_shirt", wanted)
    torso = lambda co: abs(co.x) < 0.27  # noqa: E731
    G.cut(obj, (0, 0, hem_z), (0, 0, 1), select=torso)
    # Crew neck: cut along the base of the neck (just above the notch between the collarbones in front, where the
    # neck meets the shoulders at the sides, at the knob of the spine behind), so the neckline lies on the body.
    neck = (cx, cy) = (0.0, m.head_of("neck_01").y - 0.006)
    neckline = crew_neckline(m, neck)
    neck_mid = m.neck.z + 0.04
    near_neck = lambda co: math.hypot(co.x - cx, co.y - cy) < 0.14 and co.z > m.neck.z - 0.05  # noqa: E731
    G.cut_height(obj, neckline, select=near_neck)
    for s in ("_l", "_r"):
        wrist = m.head_of("hand" + s)
        fore = (wrist - m.head_of("lowerarm" + s)).normalized()
        G.cut(obj, wrist + fore * 0.012, -fore, select=lambda co, w=wrist: (co - w).length < 0.12)
    arms = G.vertices_in_regions(obj, body, {R["upper_arm"], R["forearm"], R["hand"]})
    G.smooth(obj, 10, 0.5, weight=lambda co: 1.0 if abs(co.x) < 0.2 else 0.4)
    # Roomier towards the hem, so the shirt hangs over the trousers' waist.
    G.offset(obj, lambda co, n: 0.01 + (0.012 * smoothstep(m.waist + 0.06, hem_z, co.z) if abs(co.x) < 0.25 else 0.0))
    # The trunk: convex, round, and hanging straight down from the chest and shoulder blades.
    trunk = set(range(len(obj.data.vertices))) - arms
    G.envelope(obj, (0, m.spine_y, 1.56), (0, 0, -1), sectors=64, blur_theta=4, blur_t=3, drape=(1.56 - 1.33, 0.12),
               extra=0.004, select=lambda co: co.z < neck_mid - 0.03, indices=trunk, hull=True)
    # Over a woman's bust the cloth spans from point to point, so it gets smoothed for longer.
    G.smooth(obj, 40 if m.female else 16, 0.5,
             weight=lambda co: 1.0 if abs(co.x) < 0.17 and (1.05 if m.female else 1.18) < co.z < neck_mid - 0.03 else 0.0)
    for s in ("_l", "_r"):
        shoulder, elbow, wrist = m.head_of("upperarm" + s), m.head_of("lowerarm" + s), m.head_of("hand" + s)
        side = 1 if s == "_l" else -1
        G.envelope(obj, shoulder, elbow - shoulder, sectors=32, blur_theta=2, blur_t=2, extra=0.005,
                   select=near_segment(shoulder, elbow, 0.12, 0.3, 1.0, side), indices=arms, hull=True)
        G.envelope(obj, elbow, wrist - elbow, sectors=32, blur_theta=2, blur_t=2, extra=0.004,
                   select=near_segment(elbow, wrist, 0.1, 0.0, 1.05, side), indices=arms, hull=True)
        fore = wrist - elbow

        # Loose over the forearm, gathered into a snug cuff over the last 5 cm.
        def gather(co, t, L=fore.length, sd=side):
            if co.x * sd <= 0.21 or t < -0.02:
                return 0.0
            cuff = L - 0.055
            return 0.007 * math.sin(math.pi * min(1.0, max(0.0, t / cuff))) if t < cuff else -0.002
        G.radial(obj, elbow, fore, gather)
        upper = elbow - shoulder
        G.ripples(obj, elbow, fore, -0.05, 0.09, 0.034, 0.005, twist=0.6, mask=lambda co, sd=side: co.x * sd > 0.24)
        G.ripples(obj, elbow, fore, fore.length - 0.12, fore.length - 0.05, 0.024, 0.004, twist=-0.4,
                  mask=lambda co, sd=side: co.x * sd > 0.24)
        G.ripples(obj, shoulder, upper, 0.06, upper.length - 0.02, 0.05, 0.003, twist=0.9,
                  mask=lambda co, sd=side: co.x * sd > 0.22)
    G.ripples(obj, Vector((0, 0, m.hip)), Vector((0, 0, 1)), 0.0, 0.28, 0.07, 0.0025, twist=1.4, mask=torso)
    on_neckline = lambda co: near_neck(co) and abs(co.z - float(neckline(co.x, co.y))) < 0.03  # noqa: E731
    G.collar_band(obj, body, neck, on_neckline, rise=0.016)
    collar_top = lambda co: near_neck(co) and co.z > float(neckline(co.x, co.y)) + 0.008  # noqa: E731
    G.hem(obj, 0.007, bulge=0.0015, select=lambda co: not collar_top(co))
    G.hem(obj, 0.003, bulge=0.001, select=collar_top)
    obj["weight_smoothing"] = 6
    hems = [("plane", (0, 0, hem_z), (0, 0, 1))]
    for s in ("_l", "_r"):
        fore = (m.head_of("hand" + s) - m.head_of("lowerarm" + s)).normalized()
        hems.append(("plane", m.head_of("hand" + s) + fore * 0.012, -fore))
    rib = (neck, neckline)
    cuffs = [("cuff", m.head_of("hand" + s), (m.head_of("hand" + s) - m.head_of("lowerarm" + s)).normalized())
             for s in ("_l", "_r")]
    folds = [("drape", (0.0, m.spine_y), 1.3, hem_z, 9, 0.55), ("drape", (0.0, m.spine_y), 1.12, hem_z, 15, 0.3)]
    for s in ("_l", "_r"):
        shoulder, elbow, wrist = m.head_of("upperarm" + s), m.head_of("lowerarm" + s), m.head_of("hand" + s)
        folds.append(("rings", elbow, (wrist - elbow).normalized(), -0.06, 0.1, 0.03, 1.0, 0.7))
        folds.append(("rings", elbow, (wrist - elbow).normalized(), (wrist - elbow).length - 0.13,
                      (wrist - elbow).length - 0.05, 0.022, 0.8, -0.5))
        folds.append(("rings", shoulder, (elbow - shoulder).normalized(), 0.02, 0.11, 0.035, 0.7, 1.4))
    colour = (0.47, 0.43, 0.33)
    return obj, lambda o, folder, name: paint_fabric(o, folder, name, base=colour, weave="weave", hems=hems,
                                                     dirt_low=hem_z, dirt_high=hem_z + 0.3, size=1024, preview=colour,
                                                     rib=rib, cuffs=cuffs, folds=folds)


def crew_neckline(m, centre):
    """Height of a crew neckline round the neck, as z(x, y) (numpy-friendly): low at the throat, higher where the neck
    meets the shoulders, highest at the nape."""
    cx, cy = centre
    front, side, back = m.neck.z + 0.02, m.neck.z + 0.058, m.head_of("neck_01").z - 0.008
    a = (side + (front + back) / 2) / 2
    b = (back - front) / 2
    c = (side - (front + back) / 2) / 2

    def height(x, y):
        th = np.arctan2(np.asarray(y) - cy, np.asarray(x) - cx)
        return a + b * np.sin(th) + c * np.cos(2 * th)
    return height


def woven_trousers(body, m):
    top = m.waist + 0.022
    hem_z = m.ankle_z + 0.004
    keep = {R["pelvis"], R["thigh"], R["calf"], R["ankle"]}

    def wanted(f, r):
        c = f.calc_center_median()
        return r in keep or (r == R["belly"] and c.z < top + 0.04) or (r == R["foot"] and c.z > hem_z - 0.02)

    obj = G.extract(body, "woven_trousers", wanted)
    G.cut(obj, (0, 0, top), (0, 0, -1), select=lambda co: abs(co.x) < 0.27)
    G.cut(obj, (0, 0, hem_z), (0, 0, 1), select=lambda co: co.z < 0.3)
    G.smooth(obj, 8, 0.5, weight=lambda co: 1.0 if co.z > m.hip - 0.25 else 0.5)
    G.offset(obj, lambda co, n: 0.006)
    crotch = m.hip - 0.1
    G.envelope(obj, (0, m.spine_y, top + 0.01), (0, 0, -1), sectors=48, blur_theta=3, blur_t=2, extra=0.003,
               select=lambda co: co.z > crotch + 0.03)
    for s in ("_l", "_r"):
        hip, knee, ankle = m.head_of("thigh" + s), m.head_of("calf" + s), m.head_of("foot" + s)
        side = 1 if s == "_l" else -1
        leg = ankle - hip
        thigh_len = (knee - hip).length
        G.envelope(obj, hip, knee - hip, sectors=36, blur_theta=3, blur_t=2, extra=0.006,
                   select=lambda co, sd=side, sel=near_segment(hip, knee, 0.2, -0.1, 1.0, side): sel(co) and co.z < crotch + 0.02)
        G.envelope(obj, knee, ankle - knee, sectors=36, blur_theta=3, blur_t=2, drape=(0.02, 0.05), extra=0.006,
                   select=near_segment(knee, ankle, 0.16, 0.0, 1.2, side))

        def flare(co, t, L=leg.length, sd=side):
            return 0.008 * smoothstep(0.5 * L, L, t) if co.x * sd > 0.0 and co.z < crotch else 0.0
        G.radial(obj, hip, leg, flare)
        shin = ankle - knee
        below = lambda co, sd=side: co.x * sd > 0.0 and co.z < crotch  # noqa: E731
        G.ripples(obj, knee, shin, -0.07, 0.07, 0.03, 0.005, twist=1.2, mask=below)
        G.ripples(obj, knee, shin, shin.length - 0.15, shin.length + 0.02, 0.034, 0.007, twist=-0.8, mask=below)
        G.ripples(obj, hip, knee - hip, 0.12, thigh_len - 0.05, 0.07, 0.003, twist=2.2, mask=below)
    G.hem(obj, 0.008, bulge=0.001)
    colour = (0.17, 0.155, 0.13)
    return obj, lambda o, folder, name: paint_fabric(o, folder, name, base=colour, weave="twill", hems=[
        ("plane", (0, 0, hem_z), (0, 0, 1)), ("plane", (0, 0, top), (0, 0, -1))],
        belt=(top - 0.036, top), dirt_low=hem_z, dirt_high=0.5, size=1024, preview=colour)


def laced_boots(body, m):
    top = m.boot_top
    keep = {R["foot"], R["ankle"]}

    def wanted(f, r):
        return r in keep or (r == R["calf"] and f.calc_center_median().z < top + 0.03)

    obj = G.extract(body, "laced_boots", wanted)
    G.cut(obj, (0, 0, top), (0, 0, -1), select=lambda co: co.z < 0.45)
    G.smooth(obj, 24, 0.5, weight=lambda co: 1.0 if co.z < 0.12 else 0.4)
    G.offset(obj, lambda co, n: 0.004 if n.z < -0.5 else 0.007)
    for s in ("_l", "_r"):
        foot = m.head_of("foot" + s)
        side = 1 if s == "_l" else -1
        heel_y = foot.y + 0.07
        # A round toe box and a roomy shaft, as boots have.
        ball = m.head_of("ball" + s)
        heel = Vector((foot.x, heel_y, 0.05))
        toe = Vector((ball.x, ball.y - 0.06, 0.022))
        G.envelope(obj, heel, toe - heel, t_step=0.012, sectors=28, blur_theta=3, blur_t=3, extra=0.006,
                   select=lambda co, sd=side: co.x * sd > 0 and co.z < 0.13)
        G.envelope(obj, Vector((foot.x, foot.y + 0.01, 0.05)), Vector((0, 0, 1)), sectors=28, blur_theta=3, blur_t=2,
                   extra=0.007, select=lambda co, sd=side: co.x * sd > 0 and co.z > 0.075)
    for v in obj.data.vertices:
        # A flat sole with a little welt, resting on the ground and never below it.
        if v.co.z < 0.02:
            v.co.z = max(-0.001, v.co.z - 0.004)
            centre = m.head_of("foot_l" if v.co.x > 0 else "foot_r").x
            v.co.x += (v.co.x - centre) * 0.05
    G.ripples(obj, Vector((0, 0, m.ankle_z)), Vector((0, 0, 1)), 0.0, 0.13, 0.028, 0.004, twist=0.5,
              mask=lambda co: co.z > m.ankle_z - 0.01)
    G.hem(obj, 0.009, bulge=0.003)
    return obj, lambda o, folder, name: paint_boots(o, folder, name, top, m)


def knit_cap(body, m):
    regions = [0] * len(body.data.polygons)
    body.data.attributes["region"].data.foreach_get("value", regions)
    scalp = {i for i, r in enumerate(regions) if r == R["scalp"]}
    faces = G.dilate(body, scalp, 1)
    obj = G.extract(body, "knit_cap", lambda f, r: f.index in faces)
    G.smooth(obj, 14, 0.5)
    crown = m.head.z + 0.17

    def puff(co, n):
        return 0.010 + 0.007 * smoothstep(crown - 0.08, crown, co.z) + 0.004 * smoothstep(-0.02, 0.06, co.y - m.head.y)
    G.offset(obj, puff)
    # The turned-up cuff: the bottom 4 cm stands a little proud.
    low = min(v.co.z for v in obj.data.vertices)
    G.offset(obj, lambda co, n: 0.004 * (1.0 - smoothstep(0.035, 0.05, co.z - low_line(co, m))))
    G.hem(obj, 0.012, bulge=0.002)
    return obj, lambda o, folder, name: paint_fabric(o, folder, name, base=(0.29, 0.085, 0.065), weave="rib",
                                                     hems=[], cap_line=m, dirt_low=None, size=512,
                                                     preview=(0.29, 0.085, 0.065))


def low_line(co, m):
    """Height of the cap's lower edge under a point (the scalp line of build_body, front higher than back)."""
    return m.head.z + 0.06 - 0.08 * smoothstep(-0.12, 0.08, co.y - m.head.y)


PART_PLATE, PART_STRAP, PART_CAP, PART_BUCKLE = 0, 1, 2, 3


def breastplate(body, m, shirt):
    """A riveted cuirass of our own design. Front and back plates, with a keel down the front, a flared lower edge and
    a rolled rim, stand 25 mm clear of the shirt and are rigid on the chest bone, so they never flex with the chest.
    Shoulder caps ride the collarbones and upper arms. Leather straps run over the shoulders and round the sides to
    brass buckles. The plates are smooth, mirror-symmetric shells built over the outside of the shirt, so they never
    follow its folds; the caps are fitted to the shirt by casting rays out from the shoulder joints."""
    from mathutils.bvhtree import BVHTree
    axis = Vector((0, m.spine_y, 0))
    over = [v.co.copy() for v in shirt.data.vertices if abs(v.co.x) < 0.2 and 1.0 < v.co.z < 1.56]
    table = RadiusTable(over, axis)
    front = Plate(table, axis, 0.0, 60.0, 1.12, m.neck.z + 0.025, clearance=0.026, bulge=0.11, keel=0.011,
                  flare=0.014, neck_dip=0.04, armhole=0.07, neck_width=0.42, armhole_start=0.45)
    back = Plate(table, axis, 180.0, 56.0, 1.13, m.neck.z + 0.04, clearance=0.024, bulge=0.06, keel=0.0, flare=0.01,
                 neck_dip=0.015, armhole=0.065, neck_width=0.5, armhole_start=0.4)
    tree = BVHTree.FromObject(shirt, bpy.context.evaluated_depsgraph_get(), deform=False)
    shirt_co = np.array([v.co[:] for v in shirt.data.vertices], np.float32)
    around = lambda c: Vector((axis.x, axis.y, c.z))  # noqa: E731
    parts = []

    def add(obj, part, thickness, weights, inside=None):
        if inside is not None:
            orient(obj, inside)
        if thickness:
            G.solidify(obj, thickness)
        attr = obj.data.attributes.get("part") or obj.data.attributes.new("part", 'INT', 'FACE')
        attr.data.foreach_set("value", [part] * len(obj.data.polygons))
        weigh(obj, weights)
        parts.append(obj)

    chest = lambda co: {"spine_03": 1.0}  # noqa: E731
    add(front.build("plate_front"), PART_PLATE, 0.005, chest, around)
    add(back.build("plate_back"), PART_PLATE, 0.005, chest, around)
    for side in (1, -1):
        s = "_l" if side > 0 else "_r"
        strap, end, along, normal, edges = shoulder_strap("strap_shoulder", m, side, axis, front, back, shirt_co)

        def over_shoulder(co, e=edges, sd=s):
            # Pinned to the plates at its ends, lying on the shoulder (which the collarbone lifts) in between.
            edge_z = e[0] if co.y < axis.y else e[1]
            w = 0.5 * smoothstep(edge_z, edge_z + 0.04, co.z)
            return {"spine_03": 1.0 - w, "clavicle" + sd: w}

        add(strap, PART_STRAP, 0.003, over_shoulder, lambda c: Vector((c.x * 0.5, axis.y, c.z - 0.2)))
        for bar in buckle("buckle_shoulder", end + along * 0.012, along, normal):
            add(bar, PART_BUCKLE, 0.0, chest)
        ring, end, along, normal = side_strap("strap_side", axis, side, 1.2, 0.038, front, back, table)
        add(ring, PART_STRAP, 0.003, lambda co: {"spine_03": 0.65, "spine_02": 0.35}, around)
        for bar in buckle("buckle_side", end, along, normal):
            add(bar, PART_BUCKLE, 0.0, chest)
        cap = shoulder_cap("cap", m, side, tree)
        joint = m.head_of("upperarm" + s)
        add(cap, PART_CAP, 0.004, lambda co, sd=s: {"clavicle" + sd: 0.55, "upperarm" + sd: 0.45}, lambda c, j=joint: j)
        lame, centre = arm_lame("arm_lame", m, side, tree)
        add(lame, PART_CAP, 0.0035, lambda co, sd=s: {"upperarm" + sd: 0.85, "clavicle" + sd: 0.15},
            lambda c, o=centre: o(c))
    # Two lames hang from the front plate over the belly, each flaring out from under the one above: the fauld.
    for k, (z_low, z_high, half_width, clearance, weights) in enumerate((
            (1.06, 1.135, 56.0, 0.03, {"spine_03": 0.45, "spine_02": 0.55}),
            (1.0, 1.075, 52.0, 0.036, {"spine_02": 0.5, "spine_01": 0.5}))):
        lame = Plate(table, axis, 0.0, half_width, z_low, z_high, clearance=clearance, bulge=0.0, keel=0.011,
                     flare=0.0, neck_dip=0.0, armhole=0.0, taper=0.014, rows=3)
        add(lame.build("fauld_%d" % k), PART_PLATE, 0.0045, lambda co, w=weights: w, around)
    H.select_only(parts, parts[0])
    bpy.ops.object.join()
    obj = parts[0]
    obj.name = obj.data.name = "riveted_breastplate"
    for mod in list(obj.modifiers):
        obj.modifiers.remove(mod)
    obj.modifiers.new("Armature", 'ARMATURE').object = body.parent
    obj["rigid"] = True
    return obj, lambda o, folder, name: paint_metal(o, folder, name)


def weigh(obj, weights):
    """Skins a part: weights(co) gives {bone: weight} per vertex (a constant for rigid parts)."""
    groups = {}
    for v in obj.data.vertices:
        for bone, w in weights(v.co).items():
            if w <= 0.0:
                continue
            if bone not in groups:
                groups[bone] = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            groups[bone].add([v.index], w, 'REPLACE')


def orient(obj, inside):
    """Turns the faces so that, on the whole, they face away from inside(face centre)."""
    me = obj.data
    me.update()
    total = sum(p.normal.dot(p.center - inside(p.center)) for p in me.polygons)
    if total < 0:
        flip(obj)


def flip(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()


class RadiusTable:
    """Largest radius of a set of points around the trunk's vertical axis, by height and angle, mirrored left to right
    and blurred: the outside of the shirt, as a smooth function a plate can follow."""

    def __init__(self, points, axis, z_step=0.01, sectors=72):
        self.z0 = min(p.z for p in points)
        self.z_step = z_step
        self.sectors = sectors
        nz = int((max(p.z for p in points) - self.z0) / z_step) + 2
        t = np.zeros((nz, sectors))
        for p in points:
            rel = p - axis
            i = int((p.z - self.z0) / z_step)
            j = int((math.atan2(rel.x, -rel.y) + math.pi) / math.tau * sectors) % sectors
            t[i, j] = max(t[i, j], math.hypot(rel.x, rel.y))
        for _ in range(sectors):
            empty = t == 0
            if not empty.any():
                break
            t[empty] = np.maximum(np.roll(t, 1, axis=1), np.roll(t, -1, axis=1))[empty]
        # Mirror: angle a and -a share the larger radius.
        mirror = t[:, ::-1]
        mirror = np.roll(mirror, 1, axis=1)
        t = np.maximum(t, mirror)
        for _ in range(6):
            t = (np.roll(t, 1, axis=1) + np.roll(t, -1, axis=1) + 2 * t) / 4
        for _ in range(4):
            up = np.vstack([t[:1], t[:-1]])
            down = np.vstack([t[1:], t[-1:]])
            t = (up + down + 2 * t) / 4
        self.t = t

    def radius(self, z, degrees):
        """Bilinear between the cells' centres, so shells built on it are smooth."""
        fi = min(self.t.shape[0] - 1.0, max(0.0, (z - self.z0) / self.z_step - 0.5))
        i0 = int(math.floor(fi))
        i1 = min(i0 + 1, self.t.shape[0] - 1)
        ti = fi - i0
        fj = (math.radians(degrees) + math.pi) / math.tau * self.sectors - 0.5
        j0 = int(math.floor(fj))
        tj = fj - j0
        j0, j1 = j0 % self.sectors, (j0 + 1) % self.sectors
        a = self.t[i0, j0] * (1 - tj) + self.t[i0, j1] * tj
        b = self.t[i1, j0] * (1 - tj) + self.t[i1, j1] * tj
        return float(a * (1 - ti) + b * ti)


def polar(axis, z, degrees, r):
    a = math.radians(degrees)
    return Vector((axis.x + r * math.sin(a), axis.y - r * math.cos(a), z))


def radial(axis, point):
    return Vector((point.x - axis.x, point.y - axis.y, 0.0)).normalized()


def grid_object(name, rows, cols, point):
    """A quad grid; point(i, j) gives each vertex; UVs follow the grid."""
    mesh = bpy.data.meshes.new(name)
    verts = [point(i, j) for i in range(rows + 1) for j in range(cols + 1)]
    faces = []
    for i in range(rows):
        for j in range(cols):
            a = i * (cols + 1) + j
            faces.append((a, a + 1, a + cols + 2, a + cols + 1))
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    uv = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            i, j = divmod(vi, cols + 1)
            uv.data[li].uv = (j / cols, i / rows)
    for poly in mesh.polygons:
        poly.use_smooth = True
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


class Plate:
    """A plate over the trunk: centre ± half_width degrees round the axis, from z_low up to z_high. The top edge dips
    by neck_dip in the middle for the neck and by armhole at the sides, where the arms swing. It stands `clearance`
    over the shirt, with a bulge, a keel down the middle, a flared lower edge (or a taper outwards all the way down,
    for the lames of a fauld) and a rolled rim."""

    def __init__(self, table, axis, centre, half_width, z_low, z_high, clearance, bulge, keel, flare, neck_dip,
                 armhole, rim=0.0055, taper=0.0, rows=20, cols=28, neck_width=0.36, armhole_start=0.5):
        self.table, self.axis, self.centre, self.half_width = table, axis, centre, half_width
        self.z_low, self.z_high = z_low, z_high
        self.clearance, self.bulge, self.keel, self.flare, self.rim = clearance, bulge, keel, flare, rim
        self.neck_dip, self.armhole, self.taper = neck_dip, armhole, taper
        self.neck_width, self.armhole_start = neck_width, armhole_start
        self.rows, self.cols = rows, cols

    def z_at(self, u, v):
        top = (self.z_high - self.neck_dip * math.exp(-(u / self.neck_width) ** 2)
               - self.armhole * smoothstep(self.armhole_start, 1.0, abs(u)) ** 1.2)
        return self.z_low + (top - self.z_low) * v

    def radius(self, u, v):
        z = self.z_at(u, v)
        r = self.table.radius(z, self.centre + self.half_width * u) + self.clearance
        r += self.bulge * 0.12 * (1 - u * u) * math.sin(math.pi * v)
        r += self.keel * math.exp(-(u / 0.13) ** 2) * (0.55 + 0.45 * v)
        r += self.flare * max(0.0, (0.16 - v) / 0.16) ** 2
        r += self.taper * (1.0 - v)
        return r

    def point(self, i, j):
        u, v = j / self.cols * 2 - 1, i / self.rows
        edge = min(i, self.rows - i, j, self.cols - j)
        r = self.radius(u, v) + self.rim * (1.0, 0.35)[edge] if edge < 2 else self.radius(u, v)
        return polar(self.axis, self.z_at(u, v), self.centre + self.half_width * u, r)

    def build(self, name):
        return grid_object(name, self.rows, self.cols, self.point)

    def uv_of(self, z, degrees):
        u = (degrees - self.centre) / self.half_width
        lo, hi = 0.0, 1.0
        for _ in range(32):
            mid = (lo + hi) / 2
            lo, hi = (mid, hi) if self.z_at(u, mid) < z else (lo, mid)
        return u, lo

    def surface(self, z, degrees, lift=0.0):
        """The point on the plate's outer surface at a height and angle (inside its outline), lifted off it."""
        u, v = self.uv_of(z, degrees)
        return polar(self.axis, z, degrees, self.radius(u, v) + lift)

    def top(self, degrees):
        return self.z_at((degrees - self.centre) / self.half_width, 1.0)

    def degrees_at_x(self, x, z):
        """The angle at which the plate's surface passes over a given x (on the plate's side of the body)."""
        back = abs(self.centre) > 90
        deg = self.centre + (-1 if back else 1) * math.copysign(38.0, x)
        for _ in range(6):
            p = self.surface(z, deg)
            r = math.hypot(p.x - self.axis.x, p.y - self.axis.y)
            a = math.degrees(math.asin(max(-1.0, min(1.0, (x - self.axis.x) / r))))
            deg = 180.0 - a if back else a
        return deg


def shoulder_strap(name, m, side, axis, front, back, shirt_co, width=0.04, x=0.105):
    """A leather strap over the shoulder at x = ±10.5 cm: 4 cm down the front plate, over the shoulder lying on the
    shirt, and 4 cm down the back plate. Returns the strap, its front end with the direction up the strap and the
    plate's normal there (for the buckle), and the plates' top edges under it."""
    x = side * x
    on_plate = 0.007      # over the plate's rim
    deg_f = front.degrees_at_x(x, front.top(side * 38.0) - 0.02)
    deg_b = back.degrees_at_x(x, back.top(180.0 - side * 38.0) - 0.02)
    top_f, top_b = front.top(deg_f), back.top(deg_b)
    near = shirt_co[np.abs(shirt_co[:, 0] - x) < 0.025]

    def shirt_top(y):
        z = near[np.abs(near[:, 1] - y) < 0.02][:, 2]
        return float(z.max()) if len(z) else m.neck.z

    centre, normals = [], []
    for k in range(7):
        z = top_f - 0.04 + 0.04 * k / 6
        centre.append(front.surface(z, deg_f, on_plate))
        normals.append(radial(axis, centre[-1]))
    e_f, e_b = centre[-1], back.surface(top_b, deg_b, on_plate)
    steps = 18
    for k in range(1, steps):
        t = k / steps
        p = e_f.lerp(e_b, t)
        w = math.sin(math.pi * t) ** 0.6
        z = p.z * (1 - w) + max(shirt_top(p.y) + 0.016, p.z) * w
        centre.append(Vector((p.x, p.y, z)))
        normals.append((radial(axis, p) * (1 - w) + Vector((0, 0, 1)) * w).normalized())
    for k in range(7):
        z = top_b - 0.04 * k / 6
        centre.append(back.surface(z, deg_b, on_plate))
        normals.append(radial(axis, centre[-1]))
    # Ease the bends over the plates' edges.
    for _ in range(3):
        centre = [centre[0]] + [(centre[i - 1] + centre[i] * 2 + centre[i + 1]) / 4
                                for i in range(1, len(centre) - 1)] + [centre[-1]]
    across = []
    for i in range(len(centre)):
        t = (centre[min(i + 1, len(centre) - 1)] - centre[max(i - 1, 0)]).normalized()
        across.append(t.cross(normals[i]).normalized())
    rows, cols = len(centre) - 1, 2
    obj = grid_object("%s_%d" % (name, side), rows, cols,
                      lambda i, j: centre[i] + across[i] * (j / cols - 0.5) * width)
    along = (centre[1] - centre[0]).normalized()
    return obj, centre[0], along, normals[0], (top_f, top_b)


def side_strap(name, axis, side, z, width, front, back, table):
    """Round the side under the arm, from 10° in from the front plate's edge to 8° in from the back plate's, lying on
    the plates and drawn straight between them (but never into the shirt). Returns the strap and where its buckle
    goes on the front plate, with the strap's direction and the plate's normal there."""
    on_plate = 0.007
    start, stop = front.centre + front.half_width - 10.0, back.centre - back.half_width + 8.0
    edge_f, edge_b = front.centre + front.half_width - 1.0, back.centre - back.half_width + 1.0
    rows, cols = 2, 24

    def r_at(zz, deg):
        if deg <= edge_f:
            return front.radius(*front.uv_of(zz, deg)) + on_plate
        if deg >= edge_b:
            return back.radius(*back.uv_of(zz, deg)) + on_plate
        rf = front.radius(*front.uv_of(zz, edge_f)) + on_plate
        rb = back.radius(*back.uv_of(zz, edge_b)) + on_plate
        t = (deg - edge_f) / (edge_b - edge_f)
        taut = rf + (rb - rf) * t - 0.01 * math.sin(math.pi * t)
        return max(taut, table.radius(zz, deg) + 0.012)

    def point(i, j):
        deg = start + (stop - start) * j / cols
        zz = z - width / 2 + width * i / rows
        return polar(axis, zz, side * deg, r_at(zz, deg))

    obj = grid_object("%s_%d" % (name, side), rows, cols, point)
    deg = start + 3.0
    end = polar(axis, z, side * deg, r_at(z, deg))
    along = (polar(axis, z, side * (deg + 1.0), r_at(z, deg + 1.0)) - end).normalized()
    return obj, end, along, radial(axis, end)


def arm_lame(name, m, side, tree, t0=0.075, t1=0.135, half_angle=80.0, clearance=0.02, rows=4, cols=16):
    """A curved band of steel round the outside of the upper arm, below the shoulder cap and tucked under it: from t0
    to t1 metres down the arm, ± half_angle degrees round its outer side, fitted to the sleeve by rays cast out from
    the arm's axis, flaring a little at its lower edge. Returns it and the axis point for any vertex (to orient it)."""
    s = "_l" if side > 0 else "_r"
    shoulder, elbow = m.head_of("upperarm" + s), m.head_of("lowerarm" + s)
    axis = (elbow - shoulder).normalized()
    out = Vector((side, 0.0, 0.6))
    out = (out - axis * out.dot(axis)).normalized()
    across = axis.cross(out).normalized()
    raw = np.zeros((rows + 1, cols + 1))
    dirs, bases = {}, {}
    for i in range(rows + 1):
        t = t0 + (t1 - t0) * i / rows
        base = shoulder + axis * t
        for j in range(cols + 1):
            phi = math.radians(-half_angle + 2 * half_angle * j / cols)
            d = (out * math.cos(phi) + across * math.sin(phi)).normalized()
            hit, _, _, dist = tree.ray_cast(base, d)
            raw[i, j] = (dist if hit is not None else 0.055) + clearance
            dirs[i, j], bases[i] = d, base
    radii = raw.copy()
    for _ in range(8):
        p = np.pad(radii, 1, mode='edge')
        radii = np.maximum(raw, (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] + 4 * radii) / 8)

    def point(i, j):
        edge = min(i, rows - i, j, cols - j)
        r = radii[i, j] + 0.012 * (i / rows) ** 2 + (0.0045 * (1.0, 0.35)[edge] if edge < 2 else 0.0)
        return bases[i] + dirs[i, j] * r

    def centre(c):
        return shoulder + axis * (c - shoulder).dot(axis)

    return grid_object("%s_%d" % (name, side), rows, cols, point), centre


def buckle(name, centre, along, normal, width=0.05, length=0.034, bar=0.006, depth=0.005):
    """A rectangular frame buckle with a prong, lying on a surface: four bars and a prong, as boxes."""
    n = normal.normalized()
    l_dir = (along - n * along.dot(n)).normalized()
    c_dir = l_dir.cross(n).normalized()
    boxes = [((-(width - bar) / 2, 0.0), (bar, length)), (((width - bar) / 2, 0.0), (bar, length)),
             ((0.0, (length - bar) / 2), (width, bar)), ((0.0, -(length - bar) / 2), (width, bar)),
             ((0.0, 0.0), (0.0032, length - bar))]
    out = []
    for k, ((cx, cl), (sx, sl)) in enumerate(boxes):
        mesh = bpy.data.meshes.new("%s_%d" % (name, k))
        obj = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            local = v.co.copy()
            v.co = (centre + c_dir * (cx + local.x * sx) + l_dir * (cl + local.y * sl)
                    + n * (depth / 2 + local.z * (depth if k < 4 else depth * 0.6)))
        bm.to_mesh(mesh)
        bm.free()
        uv = mesh.uv_layers.new(name="UVMap")
        for f, poly in enumerate(mesh.polygons):
            ox, oy = f % 3, f // 3
            for li, corner in zip(poly.loop_indices, ((0, 0), (1, 0), (1, 1), (0, 1))):
                uv.data[li].uv = ((ox + 0.1 + 0.8 * corner[0]) / 3, (oy + 0.1 + 0.8 * corner[1]) / 2)
        out.append(obj)
    return out


def shoulder_cap(name, m, side, tree, clearance=0.02, rows=12, cols=14):
    """A steel cap over the point of the shoulder, fitted to the shirt: a patch of directions round a pole tilted out
    from vertical, each pushed out from the shoulder joint to where a ray meets the shirt, plus clearance; smoothed
    without ever moving inwards, flared at its lower edge, with a rolled rim."""
    s = "_l" if side > 0 else "_r"
    joint = m.head_of("upperarm" + s)
    pole = Vector((side * 0.55, 0.0, 1.0)).normalized()
    forward = Vector((0.0, -1.0, 0.0))
    down_arm = (pole.cross(forward) * side).normalized()
    dirs = {}
    raw = np.zeros((rows + 1, cols + 1))
    for i in range(rows + 1):
        b = i / rows * 2 - 1                          # −1 towards the neck … 1 down the arm
        tb = math.tan(math.radians(b * (72.0 if b > 0 else 30.0)))
        for j in range(cols + 1):
            a = j / cols * 2 - 1                      # −1 behind … 1 in front
            ta = math.tan(math.radians(a * (58.0 - 10.0 * max(0.0, b) ** 2)))
            d = (pole + forward * ta + down_arm * tb).normalized()
            hit, _, _, dist = tree.ray_cast(joint, d)
            raw[i, j] = (dist if hit is not None else 0.07) + clearance
            dirs[i, j] = d
    radii = raw.copy()
    for _ in range(10):
        p = np.pad(radii, 1, mode='edge')
        radii = np.maximum(raw, (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] + 4 * radii) / 8)

    def point(i, j):
        b = i / rows * 2 - 1
        edge = min(i, rows - i, j, cols - j)
        r = radii[i, j] + 0.02 * max(0.0, b) ** 2 + (0.005 * (1.0, 0.35)[edge] if edge < 2 else 0.0)
        return joint + dirs[i, j] * r

    return grid_object("%s_%d" % (name, side), rows, cols, point)


# ---------------------------------------------------------------- headwear and gloves

def head_centre(m):
    """A point inside the skull at eye height on the head bone's line: the mask and the hood are fitted by rays cast
    out from it."""
    return Vector((0.0, m.head.y, m.eye_z))


def direction(a, e):
    """Unit direction from the head centre: a radians round from the front (−Y) towards the left (+X), e radians up."""
    return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))


def face_union_tree(body, m, which):
    """A BVH of the body whose face is, at every vertex, the furthest out of all its faces (the base face and the
    shape keys build_body.py made), so what is fitted to it fits every face."""
    from mathutils.bvhtree import BVHTree
    me = body.data
    co = np.array([v.co[:] for v in me.vertices], np.float64)
    best = co.copy()
    path = os.path.join(H.CACHE, "human_%s_faces.npz" % which)
    if os.path.exists(path):
        c = np.array(head_centre(m)[:])
        r = np.linalg.norm(best - c, axis=1)
        with np.load(path) as faces:
            for key in faces.files:
                candidate = co + faces[key]
                rc = np.linalg.norm(candidate - c, axis=1)
                take = rc > r
                best[take] = candidate[take]
                r[take] = rc[take]
    return BVHTree.FromPolygons([tuple(v) for v in best], [tuple(p.vertices) for p in me.polygons])


def last_hit(tree, origin, direction, reach=0.4):
    """How far a ray from inside a body goes before it leaves it for good: the last surface it crosses within
    `reach` (the first may be the inside of the mouth, a nostril or an eye socket)."""
    travelled, last = 0.0, None
    o = origin.copy()
    for _ in range(12):
        hit, _, _, dist = tree.ray_cast(o, direction, reach - travelled)
        if hit is None:
            break
        travelled += dist + 1e-4
        last = travelled
        o = origin + direction * travelled
    return last


def ray_grid(tree, origins, directions, clearance, miss):
    """Distances from origins along directions to where they leave the surface, plus clearance; a ray that misses
    takes `miss` (or its neighbour's)."""
    rows, cols = len(directions), len(directions[0])
    r = np.full((rows, cols), np.nan)
    for i in range(rows):
        for j in range(cols):
            dist = last_hit(tree, origins[i][j], directions[i][j])
            if dist is not None:
                r[i, j] = dist + clearance
    for _ in range(max(rows, cols)):
        empty = np.isnan(r)
        if not empty.any():
            break
        p = np.pad(r, 1, mode='edge')
        near = np.nanmax(np.stack([p[:-2, 1:-1], p[2:, 1:-1], p[1:-1, :-2], p[1:-1, 2:]]), axis=0)
        r[empty] = near[empty]
    r[np.isnan(r)] = miss
    return r


def hang_from(p, q, c, slope):
    """Moves p out from the head's vertical axis so it doesn't draw back from q (above it) by more than `slope`
    metres per metre of drop."""
    drop = max(0.0, q.z - p.z)
    out_q = Vector((q.x - c.x, q.y - c.y, 0.0))
    if out_q.length < 1e-6:
        return
    along = out_q.normalized()
    want = out_q.length - slope * drop
    have = Vector((p.x - c.x, p.y - c.y, 0.0)).dot(along)
    if have < want:
        p += along * (want - have)


def hang(pts, c, start, slope, angles=None):
    """Cloth and plates fall from what is above them: going down the rows from `start`, a point never draws back
    towards the head's vertical axis faster than `slope` metres per metre of drop (under the chin the cloth hangs
    from the chin instead of following the throat in). With `angles` (each point's angle round the axis), a point
    hangs only from the one above it at about the same angle."""
    for i in range(max(1, start), len(pts)):
        for j in range(len(pts[i])):
            if angles is not None:
                gap = abs((angles[i][j] - angles[i - 1][j] + math.pi) % math.tau - math.pi)
                if gap > 0.12:
                    continue
            p, q = pts[i][j], pts[i - 1][j]
            drop = max(0.0, q.z - p.z)
            out_p = Vector((p.x - c.x, p.y - c.y, 0.0))
            out_q = Vector((q.x - c.x, q.y - c.y, 0.0))
            if out_q.length < 1e-6:
                continue
            want = out_q.length - slope * drop
            along = out_q.normalized()
            have = out_p.dot(along)
            if have < want:
                p += along * (want - have)


def outward_smooth_rows(raw, passes, closed):
    """outward_smooth for a grid whose rows close on themselves where `closed` says (the first and last columns are
    one point): those rows smooth round, and their two end columns end equal."""
    radii = raw.copy()
    wrap = np.array(closed)[:, None]
    for _ in range(passes):
        p = np.pad(radii, 1, mode='edge')
        left = np.where(wrap, np.roll(radii, 1, axis=1), p[1:-1, :-2])
        right = np.where(wrap, np.roll(radii, -1, axis=1), p[1:-1, 2:])
        # Columns 0 and -1 are the same point on a closed row: their neighbours are columns 1 and -2.
        left[:, 0] = np.where(wrap[:, 0], radii[:, -2], left[:, 0])
        right[:, -1] = np.where(wrap[:, 0], radii[:, 1], right[:, -1])
        radii = np.maximum(raw, (p[:-2, 1:-1] + p[2:, 1:-1] + left + right + 4 * radii) / 8)
    ends = (radii[:, 0] + radii[:, -1]) / 2
    radii[:, 0] = np.where(wrap[:, 0], ends, radii[:, 0])
    radii[:, -1] = np.where(wrap[:, 0], ends, radii[:, -1])
    return radii


def outward_smooth(raw, passes):
    """Smooths a grid of radii without ever moving inwards (a shell over the surface spans its hollows)."""
    radii = raw.copy()
    for _ in range(passes):
        p = np.pad(radii, 1, mode='edge')
        radii = np.maximum(raw, (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] + 4 * radii) / 8)
    return radii


PART_MASK_PLATE, PART_MASK_BAND = 0, 1
MASK_SLIT = math.radians(4.2)          # half the eye slit's height (as an angle from the head centre)
MASK_SLIT_WIDTH = math.radians(52.0)   # the slit runs this far round either side


def mask_side(e):
    """How far round the face plate reaches at a height (radians each side): narrower at the chin, widest over the
    cheekbones, a little narrower over the brow."""
    knots = [(-1.08, 0.82), (-0.84, 0.98), (-0.35, 1.3), (0.0, 1.38), (0.75, 1.15)]
    for (e0, a0), (e1, a1) in zip(knots, knots[1:]):
        if e <= e1:
            t = min(1.0, max(0.0, (e - e0) / (e1 - e0)))
            return a0 + (a1 - a0) * t
    return knots[-1][1]


def metal_mask(body, m, which):
    """A welded face plate of our own design, worn under the hood: one curved shell from the brow to below the chin,
    a hand's width round each cheek, standing 13 mm clear of every face; a single eye slit across both eyes; a keel
    down the welded centre seam and a heavier ridge over the brow; the lower edge flared off the throat. A steel band
    runs round the back of the head above the ears and a strap over the crown, both riveted to the plate. Rigid on
    the head bone. Breathing holes, welds, rivets and heat tint are painted (paint_mask)."""
    tree = face_union_tree(body, m, which)
    c = head_centre(m)
    e_low, e_high = -1.08, 0.75
    rows, cols = 36, 40
    es = [e_low + (e_high - e_low) * i / rows for i in range(rows + 1)]
    grid_dirs = [[direction(mask_side(e) * (j / cols * 2 - 1), e) for j in range(cols + 1)] for e in es]
    raw = ray_grid(tree, [[c] * (cols + 1)] * (rows + 1), grid_dirs, 0.013, 0.13)
    # Smoothed far more than the face's shape, so the plate is a beaten shell over the nose, not a cast of the face.
    radii = outward_smooth(raw, 44)
    far = np.unravel_index(int(np.argmax(radii)), radii.shape)
    H.log("mask plate: radii %.3f … %.3f m (largest at row %d of %d, column %d)" % (radii.min(), radii.max(), far[0], rows, far[1]))

    def plate_point(i, j):
        e = es[i]
        u = j / cols * 2 - 1
        a = mask_side(e) * u
        r = radii[i, j]
        if e < -MASK_SLIT:
            r += 0.007 * math.exp(-(a / 0.11) ** 2)                       # the keel down the centre seam
        r += 0.009 * math.exp(-((e - (MASK_SLIT + 0.13)) / 0.07) ** 2) * (1 - smoothstep(0.7, 1.0, abs(u)))   # brow
        r += 0.013 * max(0.0, (e_low + 0.22 - e) / 0.22) ** 2             # the chin flare
        edge = min(i, rows - i, j, cols - j)
        if edge < 2:
            r += 0.0035 * (1.0, 0.4)[edge]                                # rolled rim
        return c + direction(a, e) * r

    plate = grid_object("mask_plate", rows, cols, plate_point)
    # The eye slit: the quads whose middle is within the slit's height and width.
    bm = bmesh.new()
    bm.from_mesh(plate.data)
    doomed = []
    for f in bm.faces:
        rel = f.calc_center_median() - c
        e = math.asin(max(-1.0, min(1.0, rel.z / rel.length)))
        a = math.atan2(rel.x, -rel.y)
        if abs(e) < MASK_SLIT and abs(a) < MASK_SLIT_WIDTH:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bm.to_mesh(plate.data)
    bm.free()

    # The band round the back of the head, above the ears, from one side of the plate to the other.
    e_band, half = math.radians(39.0), math.radians(4.5)
    a0 = mask_side(e_band) - math.radians(10)
    b_rows, b_cols = 2, 40
    b_es = [e_band - half + 2 * half * i / b_rows for i in range(b_rows + 1)]
    b_dirs = [[direction(a0 + (math.tau - 2 * a0) * j / b_cols, e) for j in range(b_cols + 1)] for e in b_es]
    b_raw = ray_grid(tree, [[c] * (b_cols + 1)] * (b_rows + 1), b_dirs, 0.007, 0.1)
    b_r = outward_smooth(b_raw, 6)
    band = grid_object("mask_band", b_rows, b_cols, lambda i, j: c + b_dirs[i][j] * (b_r[i, j] + 0.0012 * (i in (0, b_rows))))

    # The strap over the crown: from the brow plate's top, back over the head to the band behind.
    s_rows, s_cols, width = 24, 2, 0.026
    t0, t1 = e_high - math.radians(5), math.radians(158)
    s_th = [t0 + (t1 - t0) * i / s_rows for i in range(s_rows + 1)]
    s_orig = [[c + Vector((width * (j / s_cols - 0.5), 0.0, 0.0)) for j in range(s_cols + 1)] for _ in s_th]
    s_dirs = [[Vector((0.0, -math.cos(t), math.sin(t))) for _ in range(s_cols + 1)] for t in s_th]
    s_raw = ray_grid(tree, s_orig, s_dirs, 0.0075, 0.1)
    s_r = outward_smooth(s_raw, 6)
    strap = grid_object("mask_strap", s_rows, s_cols, lambda i, j: s_orig[i][j] + s_dirs[i][j] * s_r[i, j])

    parts = []
    for obj, part, thickness in ((plate, PART_MASK_PLATE, 0.0025), (band, PART_MASK_BAND, 0.002), (strap, PART_MASK_BAND, 0.002)):
        orient(obj, lambda p: c)
        G.solidify(obj, thickness)
        attr = obj.data.attributes.get("part") or obj.data.attributes.new("part", 'INT', 'FACE')
        attr.data.foreach_set("value", [part] * len(obj.data.polygons))
        weigh(obj, lambda co: {"head": 1.0})
        parts.append(obj)
    H.select_only(parts, parts[0])
    bpy.ops.object.join()
    obj = parts[0]
    obj.name = obj.data.name = "metal_mask"
    for mod in list(obj.modifiers):
        obj.modifiers.remove(mod)
    obj.modifiers.new("Armature", 'ARMATURE').object = body.parent
    obj["rigid"] = True
    return obj, lambda o, folder, name: paint_mask(o, folder, name, c)


def cut_below(obj, height):
    """Deletes what lies below the curved surface z = height(x, y), cutting exactly along it (G.cut_height's shear
    trick, the other way up)."""
    me = obj.data
    for v in me.vertices:
        v.co.z -= float(height(v.co.x, v.co.y))
    G.cut(obj, (0.0, 0.0, 0.0), (0.0, 0.0, 1.0))
    for v in me.vertices:
        v.co.z += float(height(v.co.x, v.co.y))


def push_clear(obj, trees, select=None, passes=5):
    """Moves vertices out along their normals until they stand at least each tree's clearance off its surface (a
    hood over the shirt, the mask and the skin)."""
    me = obj.data
    for _ in range(passes):
        me.update()
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.normal_update()
        moved = 0
        for v in bm.verts:
            if select is not None and not select(v.co):
                continue
            push = 0.0
            for tree, clearance in trees:
                loc, normal, _, dist = tree.find_nearest(v.co)
                if loc is None or dist > clearance + 0.03:
                    continue
                out = (v.co - loc).dot(normal)
                if out < clearance:
                    push = max(push, clearance - out)
            if push > 0.0:
                v.co += v.normal * push
                moved += 1
        bm.to_mesh(me)
        bm.free()
        if moved == 0:
            break


HOOD_OPEN_SIDE = math.radians(60.0)                          # the face opening's half-width at eye height
HOOD_OPEN_TOP, HOOD_OPEN_BOTTOM = math.radians(29.0), math.radians(-50.0)   # its top and bottom (elevation)


def hood_k(a, e):
    """Where a direction from the head centre (a round from the front, e up) lies against the face opening, a
    superellipse in those angles: 1 on its edge, less inside. Its top runs nearly level over the forehead, its bottom
    is round under the chin."""
    mid = (HOOD_OPEN_TOP + HOOD_OPEN_BOTTOM) / 2
    half = (HOOD_OPEN_TOP - HOOD_OPEN_BOTTOM) / 2
    q = (e - mid) / half
    power = 3.2 if q > 0 else 2.2
    a = (a + math.pi) % math.tau - math.pi
    return (abs(a / HOOD_OPEN_SIDE) ** power + abs(q) ** power) ** (1.0 / power)


def hood_rim(a, e):
    """The point on the opening's edge nearest a direction inside it: along the row (round) where the row crosses the
    opening's sides, else up or down (its top or bottom)."""
    mid = (HOOD_OPEN_TOP + HOOD_OPEN_BOTTOM) / 2
    half = (HOOD_OPEN_TOP - HOOD_OPEN_BOTTOM) / 2
    q = (e - mid) / half
    a = (a + math.pi) % math.tau - math.pi
    power = 3.2 if q > 0 else 2.2
    side = 1.0 - abs(q) ** power
    if side > 0.05:
        return math.copysign(HOOD_OPEN_SIDE * side ** (1.0 / power), a), e
    q2 = 1.0 - min(1.0, abs(a / HOOD_OPEN_SIDE)) ** power
    return a, mid + math.copysign(half * max(0.0, q2) ** (1.0 / power), q)


def smooth_head_tree(body, m, which, passes=30):
    """A BVH of the body with every face shape's furthest point (face_union_tree's surface), the head and neck
    smoothed: the ears, nose and lips melt into the head's shape, so rays cast at it give a smooth shell."""
    from mathutils.bvhtree import BVHTree
    me = body.data
    base = np.array([v.co[:] for v in me.vertices], np.float64)
    co = base.copy()
    path = os.path.join(H.CACHE, "human_%s_faces.npz" % which)
    if os.path.exists(path):
        c = np.array(head_centre(m)[:])
        r = np.linalg.norm(co - c, axis=1)
        with np.load(path) as faces:
            for key in faces.files:
                candidate = base + faces[key]
                rc = np.linalg.norm(candidate - c, axis=1)
                take = rc > r
                co[take] = candidate[take]
                r[take] = rc[take]
    regions = np.zeros(len(me.polygons), np.int32)
    me.attributes["region"].data.foreach_get("value", regions)
    head = np.zeros(len(co), bool)
    for poly in me.polygons:
        if regions[poly.index] in (R["face"], R["scalp"], R["neck"]):
            head[list(poly.vertices)] = True
    edges = np.array([e.vertices[:] for e in me.edges], np.int64)
    degree = np.maximum(np.bincount(edges.ravel(), minlength=len(co)), 1).astype(np.float64)
    for _ in range(passes):
        for lam in (0.5, -0.52):
            total = np.zeros_like(co)
            np.add.at(total, edges[:, 0], co[edges[:, 1]])
            np.add.at(total, edges[:, 1], co[edges[:, 0]])
            step = (total / degree[:, None] - co) * lam
            co[head] += step[head]
    return BVHTree.FromPolygons([tuple(v) for v in co], [tuple(p.vertices) for p in me.polygons])


def woven_hood(body, m, which, shirt, mask):
    """A hood of rough woven cloth, our own cut. It is built round the head as a grid of rays: rows from the crown
    down to below the shoulders, columns all the way round. Rays leave a line down the middle of the head and neck,
    and each one's length is the furthest of the smoothed head (every face shape), the mask and the shirt, plus room:
    22 mm over the head, less at the neck. The face opening, a superellipse seen from the head centre, is cut out of
    the grid and its edge moved onto the curve. Then: a soft point behind the crown, a rolled edge round the face with
    soft folds running back from it, the cloth under the chin hanging from the chin instead of following the throat,
    gathers at the throat, drape folds in the short cape over the base of the neck and the tops of the shoulders
    (over the shirt, under the breastplate's plates and straps), and a hem along a curve, lowest behind. It clears
    the mask, so it goes over it."""
    from mathutils.bvhtree import BVHTree
    c = head_centre(m)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    trees = [smooth_head_tree(body, m, which), BVHTree.FromObject(shirt, depsgraph, deform=False)]
    if mask is not None:
        trees.append(BVHTree.FromObject(mask, depsgraph, deform=False))
    hem_front = m.neck.z - 0.055

    def hem_height(x, y):
        # Lower in front and lowest behind, lying on the shoulders at the sides.
        return hem_front + 1.7 * np.asarray(x) ** 2 - 0.025 * np.clip((np.asarray(y) - m.head.y) / 0.08, 0, 1)

    rows, cols = 70, 80
    e_top, e_bottom = math.radians(90.0), math.radians(-86.0)
    neck_drop = c.z - (hem_front - 0.04)

    def origin(e):
        # From the head centre down the middle of the neck once the rows are below the chin.
        return Vector((c.x, c.y + 0.012 * smoothstep(-0.9, -1.5, e), c.z - neck_drop * smoothstep(-0.95, -1.5, e)))

    es = [e_top + (e_bottom - e_top) * (i / rows) for i in range(rows + 1)]
    angle = [-math.pi + math.tau * j / cols for j in range(cols + 1)]   # the seam behind, the front in the middle
    raw = np.zeros((rows + 1, cols + 1))
    for i, e in enumerate(es):
        o = origin(e)
        for j, a in enumerate(angle):
            d = direction(a, e)
            best = None
            for tree in trees:
                hit, _, _, dist = tree.ray_cast(o, d, 0.6)
                if hit is not None and (best is None or dist > best) and (best is None or dist < best + 0.07):
                    best = dist
            raw[i, j] = best if best is not None else np.nan
    for _ in range(8):
        empty = np.isnan(raw)
        if not empty.any():
            break
        p = np.pad(raw, 1, mode='edge')
        raw[empty] = np.nanmax(np.stack([p[:-2, 1:-1], p[2:, 1:-1], p[1:-1, :-2], p[1:-1, 2:]]), axis=0)[empty]
    raw = np.nan_to_num(raw, nan=0.1)
    room = np.zeros_like(raw)
    for i, e in enumerate(es):
        head = smoothstep(-0.75, -0.25, e)
        room[i, :] = 0.009 + 0.013 * head + 0.006 * smoothstep(0.2, 1.0, e)
    radii = outward_smooth_rows(raw + room, 20, [True] * (rows + 1))
    radii[0, :] = radii[0, :].mean()

    def point(i, j):
        e, a = es[i], angle[j]
        o = origin(e)
        d = direction(a, e)
        r = radii[i, j]
        if i == 0:
            return o + d * r
        behind = math.cos(a - math.pi) * 0.5 + 0.5
        # A soft point behind the crown.
        r += 0.04 * math.exp(-(((a % math.tau) - math.pi) / 0.5) ** 2) * math.exp(-((e - 0.8) / 0.38) ** 2)
        r += 0.006 * behind * smoothstep(-0.3, 0.6, e)
        # The face opening's edge: rolled and a little proud, with soft folds running back from it.
        k = hood_k(a, e)
        if k > 1.0:
            from_rim = (k - 1.0) * 0.9
            r += 0.005 * math.exp(-(from_rim / 0.07) ** 2)
            zone = math.exp(-((from_rim - 0.16) / 0.11) ** 2)
            fold = math.atan2(e - (HOOD_OPEN_TOP + HOOD_OPEN_BOTTOM) / 2, a)
            r += 0.006 * zone * (0.5 + 0.5 * math.sin(fold * 9.0 + 1.2 * math.sin(fold * 3.0))) ** 2
        # Gathers at the throat.
        r += 0.005 * smoothstep(-0.9, -1.05, e) * smoothstep(-1.45, -1.15, e) * (0.5 + 0.5 * math.sin(a * 11.0)) ** 2
        # Drape folds in the cape, growing towards the hem.
        r += 0.008 * smoothstep(-1.1, -1.5, e) * (0.5 + 0.5 * math.sin(a * 9.0 + 0.8 * math.sin(a * 3.0))) ** 2
        return o + d * r

    pts = [[point(i, j) for j in range(cols + 1)] for i in range(rows + 1)]
    # Under the chin and down the throat the cloth hangs from what is above it.
    start = next(i for i, e in enumerate(es) if e < HOOD_OPEN_BOTTOM + 0.1)
    hang(pts, c, start=start, slope=0.9)
    # The hem: each column ends where it crosses the hem's curve; the points below gather there (and are merged).
    for j in range(cols + 1):
        for i in range(1, rows + 1):
            p, q = pts[i][j], pts[i - 1][j]
            below = p.z - float(hem_height(p.x, p.y))
            if below < 0.0:
                above = q.z - float(hem_height(q.x, q.y))
                t = above / max(1e-9, above - below)
                end = q.lerp(p, min(1.0, max(0.0, t)))
                for k in range(i, rows + 1):
                    pts[k][j] = end.copy()
                break
    obj = grid_object("woven_hood", rows, cols, lambda i, j: pts[i][j])
    # The face opening: the quads whose middle direction is inside the curve go; the edge left is moved onto it.
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    grid = bm.verts.layers.int.new("grid")
    for v in bm.verts:
        v[grid] = v.index
    # Close the seam behind and the crown's pole, and gather the hem, before looking for the opening's edge.
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0006)
    bmesh.ops.dissolve_degenerate(bm, dist=0.0006, edges=bm.edges[:])
    merged = before - len(bm.verts)
    doomed = []
    for f in bm.faces:
        i0 = min(v[grid] // (cols + 1) for v in f.verts)
        js = [v[grid] % (cols + 1) for v in f.verts]
        j0 = min(js) if max(js) - min(js) <= 1 else max(js)
        e_c, a_c = (es[i0] + es[min(rows, i0 + 1)]) / 2, (angle[j0] + angle[min(cols, j0 + 1)]) / 2
        if hood_k(a_c, e_c) < 1.0:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bm.verts.ensure_lookup_table()
    for v in G.boundary_verts(bm):
        if v.co.z < hem_front + 0.06:
            continue
        i, j = v[grid] // (cols + 1), v[grid] % (cols + 1)
        e, a = es[i], angle[j]
        a2, e2 = hood_rim(a, e)
        # A ray of its own along the edge's direction (the grid's origin moves down the neck, so a length doesn't
        # carry over from another row).
        o, d = origin(e2), direction(a2, e2)
        best = None
        for tree in trees:
            hit, _, _, dist = tree.ray_cast(o, d, 0.6)
            if hit is not None and (best is None or dist > best) and (best is None or dist < best + 0.07):
                best = dist
        if best is not None:
            v.co = o + d * (best + 0.009 + 0.013 * smoothstep(-0.75, -0.25, e2) + 0.005)
    H.log("hood: %d faces cut for the face, %d vertices merged, %d open edges" % (
        len(doomed), merged, sum(1 for e in bm.edges if e.is_boundary)))
    bm.to_mesh(obj.data)
    bm.free()
    orient(obj, lambda p: Vector((c.x, c.y, p.z)) if p.z < c.z else c)
    # Clear of the shirt, the skin, every face and the mask, measured square to their surfaces (the rays meet the
    # shoulders at a slant), then the cape flares a little towards its hem.
    clear = [(trees[0], 0.014), (trees[1], 0.011)] + ([(trees[2], 0.006)] if mask is not None else [])
    G.smooth(obj, 6, 0.4)
    neck_down = lambda co: co.z < m.neck.z + 0.08  # noqa: E731 (the head's rays meet it square; the cape's don't)
    push_clear(obj, clear, select=neck_down)
    for v in obj.data.vertices:
        if v.co.z < m.neck.z + 0.02:
            out = Vector((v.co.x - c.x, v.co.y - c.y, 0.0))
            if out.length > 1e-6:
                v.co += out.normalized() * 0.012 * smoothstep(m.neck.z + 0.02, hem_front - 0.02, v.co.z)
    G.smooth(obj, 3, 0.4)
    push_clear(obj, clear, select=neck_down)
    G.hem(obj, 0.01, bulge=0.003, select=lambda co: co.z > hem_front + 0.06)
    G.hem(obj, 0.005, bulge=0.002, select=lambda co: co.z <= hem_front + 0.06)
    inside = 0
    shirt_tree = trees[1]
    for v in obj.data.vertices:
        loc, normal, _, dist = shirt_tree.find_nearest(v.co)
        if loc is not None and dist < 0.02 and (v.co - loc).dot(normal) < 0.002:
            inside += 1
    H.log("hood: %d vertices within 2 mm of the shirt" % inside)
    obj.data.materials.clear()
    obj["weight_smoothing"] = 8
    # Darker than the shirt, so the hood reads as its own garment at a distance.
    colour = (0.255, 0.235, 0.18)
    return obj, lambda o, folder, name: paint_hood(o, folder, name, colour, m, c, hem_height)


def woven_gloves(body, m):
    """Gloves of tight woven fibre: a shell 2.5 mm over the hands (thicker on the back of the hand and the
    fingertips), with a cuff that runs 5 cm up the forearm, under the shirt's sleeve. Drawn in first person too."""
    def near_wrist(c, reach):
        return any((c - m.head_of("hand" + s)).length < reach for s in ("_l", "_r"))

    def wanted(f, r):
        c = f.calc_center_median()
        return r == R["hand"] or (r == R["forearm"] and near_wrist(c, 0.085))

    obj = G.extract(body, "woven_gloves", wanted)
    for s in ("_l", "_r"):
        wrist = m.head_of("hand" + s)
        fore = (wrist - m.head_of("lowerarm" + s)).normalized()
        G.cut(obj, wrist - fore * 0.052, fore, select=lambda co, w=wrist: (co - w).length < 0.14)
    G.smooth(obj, 2, 0.3)
    tips = {s: [m.tail_of(f + "_03" + s) for f in ("thumb", "index", "middle", "ring", "pinky")] for s in ("_l", "_r")}

    def thickness(co, n):
        side = "_l" if co.x > 0 else "_r"
        wrist = m.head_of("hand" + side)
        fore = (wrist - m.head_of("lowerarm" + side)).normalized()
        along = (co - wrist).dot(fore)
        tip = min((co - t).length for t in tips[side])
        return 0.0025 + 0.0012 * math.exp(-(tip / 0.012) ** 2) + 0.001 * smoothstep(0.0, -0.03, along)
    G.offset(obj, thickness)
    G.hem(obj, 0.006, bulge=0.0012)
    return obj, lambda o, folder, name: paint_gloves(o, folder, name, m)


# ---------------------------------------------------------------- textures

def noise3(pos, scale, seed, octaves=3):
    """Smooth 3D pseudo-noise from sums of sines: continuous across UV seams, since it follows the body."""
    rng = np.random.default_rng(seed)
    total = np.zeros(pos.shape[:2], np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        for _ in range(4):
            k = rng.normal(size=3).astype(np.float32)
            k = k / np.linalg.norm(k) * scale * (2 ** o)
            total += amp * np.sin(pos @ k + rng.random() * 6.283)
        norm += amp * 4
        amp *= 0.5
    return total / norm * 0.5 + 0.5


def plane_distance(pos, point, normal):
    n = np.array(normal, np.float32)
    n /= np.linalg.norm(n)
    return (pos - np.array(point, np.float32)) @ n


def save_maps(folder, name, albedo, height, mask, rough=None, metal=None, ao=None, strength=1.0):
    size = albedo.shape[0]
    albedo = G.bleed(albedo, mask)
    H.save_image(S.to_blender_pixels(albedo), size, size, os.path.join(folder, name + "_albedo.png"))
    h = G.bleed(height, mask)
    H.save_image(S.to_blender_pixels(S.height_to_normal(h, strength)), size, size, os.path.join(folder, name + "_normal.png"))
    if rough is not None:
        orm = np.stack([ao if ao is not None else np.ones_like(rough), rough, metal if metal is not None else np.zeros_like(rough)], axis=-1)
        H.save_image(S.to_blender_pixels(G.bleed(orm, mask)), size, size, os.path.join(folder, name + "_orm.png"))


def fold_height(pos, folds):
    """Wrinkles as a height field from 3D positions: 'drape' folds hang down the trunk below the chest, 'rings' gather
    round a limb near a joint."""
    h = np.zeros(pos.shape[:2], np.float32)
    for f in folds:
        if f[0] == "drape":
            _, (cx, cy), top, bottom, count, amp = f
            theta = np.arctan2(pos[..., 0] - cx, -(pos[..., 1] - cy))
            depth = np.clip((top - pos[..., 2]) / max(1e-3, top - bottom), 0, 1)
            wobble = 1.6 * noise3(pos, 6.0, 51)
            ridge = 0.5 + 0.5 * np.sin(theta * count + wobble * 2.0 + pos[..., 2] * 1.5)
            h += amp * depth * ridge ** 3
        else:
            _, origin, axis, t0, t1, wave, amp, twist = f
            o = np.array(origin, np.float32)
            d = np.array(axis, np.float32)
            rel = pos - o
            t = rel @ d
            a = np.cross(d, np.array((0, 0, 1), np.float32))
            if np.linalg.norm(a) < 1e-3:
                a = np.array((1, 0, 0), np.float32)
            a /= np.linalg.norm(a)
            b = np.cross(d, a)
            phi = np.arctan2(rel @ b, rel @ a)
            fade = np.clip(np.sin(np.pi * (t - t0) / (t1 - t0)), 0, 1) * ((t > t0) & (t < t1))
            ridge = 0.5 + 0.5 * np.sin(t / wave * np.pi * 2 + twist * phi + 0.8 * np.sin(phi * 3))
            h += amp * fade * ridge ** 2
    return h


def paint_fabric(obj, folder, name, base, weave, hems, dirt_low=None, dirt_high=None, belt=None, cap_line=None,
                 size=1024, preview=None, rib=None, cuffs=(), folds=()):
    mask, pos, nor, _ = G.rasterise(obj, size)
    base = np.array(base, np.float32)
    tone = 0.9 + 0.2 * noise3(pos, 9.0, 3) + 0.06 * (noise3(pos, 40.0, 4) - 0.5)
    albedo = np.ones((size, size, 3), np.float32) * base * tone[..., None]
    height = np.zeros((size, size), np.float32)
    # Hems: a folded band, double stitched.
    for kind, point, normal in hems:
        d = plane_distance(pos, point, normal)
        band = (d < 0.016) & (d > -0.01) & mask
        albedo[band] *= 0.9
        height += np.clip(1 - np.abs(d - 0.008) / 0.008, 0, 1) * 0.6 * mask
        along = pos[..., 0] * 60 + pos[..., 1] * 60 + pos[..., 2] * 40
        for at in (0.004, 0.013):
            st = G.stitches(along, (d - at) * 1000, 2.2, 0.55) * mask
            albedo *= (1 - 0.35 * st)[..., None]
            height -= st * 0.4
    # Seams where the body's UV islands meet (side seams, sleeve seams).
    edge = G.edge_distance(mask, 10)
    seam = np.clip(1 - np.abs(edge - 4) / 1.2, 0, 1) * (edge < 9) * mask
    along = (pos[..., 0] + pos[..., 1] + pos[..., 2]) * 300
    albedo *= (1 - 0.25 * seam * (np.sin(along) > 0))[..., None]
    height += np.clip(1 - edge / 3.0, 0, 1) * 0.5 * mask
    if belt is not None:
        z0, z1 = belt
        z = pos[..., 2]
        strap = (z > z0) & (z < z1) & mask
        leather = np.array((0.11, 0.07, 0.045), np.float32) * (0.85 + 0.3 * noise3(pos, 30, 9))[..., None]
        albedo[strap] = leather[strap]
        height += np.where(strap, 0.8, 0.0)
        front = strap & (pos[..., 1] < -0.05) & (np.abs(pos[..., 0]) < 0.03)
        albedo[front] = np.array((0.42, 0.40, 0.36), np.float32)
        height += np.where(front, 1.2, 0.0)
    if cap_line is not None:
        rel = pos[..., 2] - (cap_line.head.z + 0.06 - 0.08 * np.clip((pos[..., 1] - cap_line.head.y + 0.12) / 0.2, 0, 1))
        cuff = (rel < 0.05) & mask
        albedo[cuff] *= 0.86
        ribs = 0.5 + 0.5 * np.sin(np.arctan2(pos[..., 0], pos[..., 1] - cap_line.head.y) * 90)
        height += np.where(cuff, ribs * 0.7, ribs * 0.35)
    if rib is not None:
        # The crew neck's ribbed band: everything above the neckline, round the neck.
        (cx, cy), line_of = rib
        th = np.arctan2(pos[..., 1] - cy, pos[..., 0] - cx)
        near = np.hypot(pos[..., 0] - cx, pos[..., 1] - cy) < 0.14
        band = (pos[..., 2] > line_of(pos[..., 0], pos[..., 1]) - 0.004) & near & mask
        ribs = 0.5 + 0.5 * np.sin(th * 70)
        albedo[band] *= 0.88
        height_add = np.where(band, 0.5 + 0.6 * ribs, 0.0)
        height += height_add
    for _, wrist, axis in cuffs:
        d = (pos - np.array(wrist, np.float32)) @ np.array(axis, np.float32)
        band = (d > -0.055) & (d < 0.02) & (np.linalg.norm(pos - np.array(wrist, np.float32), axis=-1) < 0.09) & mask
        albedo[band] *= 0.9
        height += np.where(band, 0.45, 0.0)
        for at in (-0.05, -0.004):
            st = G.stitches(pos[..., 0] * 80 + pos[..., 2] * 80, (d - at) * 1000, 2.0, 0.5) * band
            albedo *= (1 - 0.3 * st)[..., None]
    if folds:
        wrinkles = fold_height(pos, folds) * mask
        height += wrinkles * 2.2
        albedo *= (1 - 0.06 * wrinkles)[..., None]
    if dirt_low is not None:
        z = pos[..., 2]
        grime = np.clip(1 - (z - dirt_low) / max(1e-3, (dirt_high - dirt_low)), 0, 1) * (0.6 + 0.4 * noise3(pos, 25, 7))
        albedo *= (1 - 0.22 * grime)[..., None]
        albedo += (np.array((0.06, 0.05, 0.035), np.float32) * grime[..., None]) * 0.5
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, strength=1.6)
    tiling = G.metres_per_uv(obj) / 0.03
    return {"detail": weave, "tiling": round(tiling, 2), "detail_strength": 0.75, "roughness": 0.92,
            "preview": list(preview or base)}


def paint_boots(obj, folder, name, top, m):
    size = 512
    mask, pos, nor, _ = G.rasterise(obj, size)
    z = pos[..., 2]
    leather = np.array((0.17, 0.105, 0.06), np.float32)
    tone = 0.75 + 0.45 * noise3(pos, 18, 11)
    albedo = leather * tone[..., None] * np.ones((size, size, 1), np.float32)
    height = S_leather(pos) * 0.4
    rough = 0.55 + 0.2 * noise3(pos, 25, 12)
    # Rubber sole and a stitched welt.
    sole = (z < 0.022) & mask
    albedo[sole] = np.array((0.045, 0.04, 0.035), np.float32)
    rough = np.where(sole, 0.85, rough)
    height += np.where(sole, 0.8, 0.0)
    welt = (np.abs(z - 0.026) < 0.004) & mask
    albedo[welt] *= 0.7
    # Toe cap and heel counter: a second layer of leather.
    for s in (1, -1):
        foot = m.head_of("foot_l" if s > 0 else "foot_r")
        ball = m.head_of("ball_l" if s > 0 else "ball_r")
        side = (np.sign(pos[..., 0]) == s)
        toe = side & (pos[..., 1] < ball.y + 0.015) & (z < 0.09) & mask
        albedo[toe] *= 0.82
        height += np.where(toe, 0.6, 0.0)
        heel = side & (pos[..., 1] > foot.y + 0.02) & (z < 0.1) & mask
        albedo[heel] *= 0.85
        height += np.where(heel, 0.5, 0.0)
        # Laces: a strip over the instep with crossings.
        lace_x = foot.x
        across = pos[..., 0] - lace_x
        instep = side & (np.abs(across) < 0.022) & (pos[..., 1] < foot.y - 0.01) & (z > 0.05) & (z < top - 0.01) & mask
        albedo[instep] = albedo[instep] * 0.55
        eyelets = instep & (np.abs(np.abs(across) - 0.017) < 0.004) & (np.sin(z * 260) > 0.6)
        albedo[eyelets] = np.array((0.5, 0.48, 0.42), np.float32)
        cross = instep & (np.abs(np.abs(across) - np.abs(np.sin(z * 130)) * 0.016) < 0.003)
        albedo[cross] = np.array((0.36, 0.32, 0.25), np.float32)
        height += np.where(cross, 1.0, 0.0) + np.where(instep, 0.3, 0.0)
    # Scuffs and dust.
    dust = np.clip(1 - z / 0.12, 0, 1) * noise3(pos, 30, 13)
    albedo = albedo * (1 - 0.25 * dust[..., None]) + np.array((0.12, 0.1, 0.08), np.float32) * dust[..., None] * 0.35
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, rough=np.clip(rough, 0, 1), strength=2.0)
    return {"detail": "leather", "tiling": round(G.metres_per_uv(obj) / 0.05, 2), "detail_strength": 0.5,
            "roughness": 0.6, "specular": 0.4, "preview": [0.17, 0.105, 0.06]}


def S_leather(pos):
    return noise3(pos, 120, 21, octaves=2)


def paint_metal(obj, folder, name):
    """Polished steel with hammer marks, a brighter rolled rim and keel, rivets along the edges and a little rust;
    dark leather straps; brass buckles."""
    size = 1024
    mask, pos, nor, face = G.rasterise(obj, size)
    parts = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", parts)
    part = np.where(mask & (face >= 0), parts[np.clip(face, 0, None)], -1)
    steel = (part == PART_PLATE) | (part == PART_CAP)
    edge = G.edge_distance(mask, 16)
    hammer = noise3(pos, 70, 31, octaves=2)
    wear = noise3(pos, 14, 32) * 0.7 + noise3(pos, 45, 33) * 0.3
    bright = np.clip(1 - edge / 6, 0, 1)
    keel = np.clip(1 - np.abs(pos[..., 0]) / 0.012, 0, 1) * (pos[..., 1] < 0)
    bright = np.maximum(bright, keel * 0.8)
    base = np.array((0.46, 0.47, 0.48), np.float32)
    polished = np.array((0.66, 0.66, 0.65), np.float32)
    albedo = base * (0.9 + 0.2 * hammer)[..., None]
    albedo = albedo * (1 - bright[..., None]) + polished * bright[..., None]
    rust = np.clip((noise3(pos, 22, 34) - 0.72) * 6, 0, 1) * (1 - bright) * steel
    grime = np.clip((wear - 0.58) * 3, 0, 1) * (1 - bright) * 0.4
    albedo = albedo * (1 - 0.3 * grime[..., None])
    albedo = albedo * (1 - rust[..., None] * 0.75) + np.array((0.3, 0.13, 0.05), np.float32) * rust[..., None] * 0.75
    metal = np.clip(0.95 - 0.7 * rust - 0.2 * grime, 0, 1)
    rough = np.clip(0.36 + 0.12 * hammer - 0.14 * bright + 0.25 * grime + 0.4 * rust, 0.16, 1.0)
    height = hammer * 0.5 + bright * 0.4
    # Round rivets, evenly spaced in a row about a centimetre in from the edges of the plates and caps.
    line = steel & (np.abs(edge - 9) < 0.5)
    candidates = pos[line]
    heads = np.zeros((0, 3), np.float32)
    for q in candidates[np.lexsort((candidates[:, 1], candidates[:, 0], candidates[:, 2]))]:
        if len(heads) == 0 or np.min(np.linalg.norm(heads - q, axis=1)) > 0.045:
            heads = np.vstack([heads, q[None]])
    dome = np.zeros(mask.shape, np.float32)
    ys, xs = np.nonzero(steel)
    texels = pos[ys, xs]
    for h in heads:
        d = np.linalg.norm(texels - h, axis=1)
        near = d < 0.0045
        dome[ys[near], xs[near]] = np.maximum(dome[ys[near], xs[near]], 1 - d[near] / 0.0045)
    rivet = steel & (dome > 0)
    albedo = np.where(rivet[..., None], polished * (0.8 + 0.35 * dome[..., None]), albedo)
    metal[rivet] = 1.0
    rough[rivet] = 0.25
    height += np.sqrt(dome) * 2.0 * steel
    strap = part == PART_STRAP
    leather = np.array((0.16, 0.095, 0.05), np.float32)
    albedo[strap] = (leather * (0.75 + 0.45 * noise3(pos, 40, 35))[..., None])[strap]
    albedo[strap & (edge < 3)] *= 0.55
    metal[strap] = 0.0
    rough[strap] = 0.7
    height = np.where(strap, noise3(pos, 120, 36, octaves=2) * 0.4 + np.clip(edge / 3, 0, 1) * 0.5, height)
    brass = part == PART_BUCKLE
    albedo[brass] = np.array((0.62, 0.46, 0.2), np.float32)
    metal[brass] = 1.0
    rough[brass] = 0.32
    ao = np.clip(0.7 + 0.3 * np.clip(edge / 6, 0, 1), 0, 1)
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, rough=rough, metal=metal, ao=ao, strength=2.5)
    return {"detail": "metal", "tiling": round(G.metres_per_uv(obj) / 0.12, 2), "detail_strength": 0.2,
            "roughness": 0.36, "metallic": 0.95, "specular": 0.5, "normal_strength": 1.2,
            "preview": [0.46, 0.47, 0.48]}


def nearest_distance(points, targets, chunk=65536):
    """Distance from each of many points (N×3) to the nearest of a few targets (M×3), in chunks."""
    out = np.empty(len(points), np.float32)
    t = np.asarray(targets, np.float32)
    for k in range(0, len(points), chunk):
        p = points[k:k + chunk]
        d = ((p[:, None, :] - t[None, :, :]) ** 2).sum(axis=2)
        out[k:k + chunk] = np.sqrt(d.min(axis=1))
    return out


def paint_hood(obj, folder, name, base, m, c, hem_height, size=1024):
    """Rough woven cloth: a stitched band round the face opening, a stitched hem round the cape, a seam down the back
    from the point, wrinkles and dirt where hands pull it up."""
    mask, pos, nor, _ = G.rasterise(obj, size)
    base = np.array(base, np.float32)
    tone = 0.88 + 0.22 * noise3(pos, 9.0, 61) + 0.06 * (noise3(pos, 40.0, 62) - 0.5)
    albedo = np.ones((size, size, 3), np.float32) * base * tone[..., None]
    height = np.zeros((size, size), np.float32)
    covered = np.nonzero(mask)
    # The face opening's edge: distance of each texel from it.
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    rim = [v.co[:] for v in G.boundary_verts(bm) if v.co.z > m.neck.z - 0.0]
    bm.free()
    rim_d = np.full(mask.shape, 1.0, np.float32)
    if rim:
        rim_d[covered] = nearest_distance(pos[covered], rim)
    band = (rim_d < 0.02) & mask
    albedo[band] *= 0.9
    height += np.clip(1 - np.abs(rim_d - 0.01) / 0.01, 0, 1) * 0.6 * mask
    along = pos[..., 0] * 60 + pos[..., 1] * 50 + pos[..., 2] * 60
    for at in (0.005, 0.016):
        st = G.stitches(along, (rim_d - at) * 1000, 2.2, 0.55) * mask
        albedo *= (1 - 0.35 * st)[..., None]
        height -= st * 0.4
    # The cape's hem.
    hem_d = pos[..., 2] - hem_height(pos[..., 0], pos[..., 1])
    low = (hem_d < 0.016) & mask
    albedo[low] *= 0.9
    for at in (0.004, 0.012):
        st = G.stitches(along, (hem_d - at) * 1000, 2.2, 0.55) * mask
        albedo *= (1 - 0.35 * st)[..., None]
    # A seam down the back of the head from the point.
    behind = (pos[..., 1] > c.y + 0.02) & mask
    seam_d = np.abs(pos[..., 0]) * 1000
    st = G.stitches(pos[..., 2] * 300, seam_d - 2.5, 2.0, 0.5) * behind + G.stitches(pos[..., 2] * 300, seam_d + 2.5, 2.0, 0.5) * behind
    albedo *= (1 - 0.3 * np.clip(st, 0, 1))[..., None]
    height += np.clip(1 - seam_d / 2.0, 0, 1) * 0.6 * behind
    # Seams where the UV islands meet, wrinkles, dirt.
    edge = G.edge_distance(mask, 10)
    height += np.clip(1 - edge / 3.0, 0, 1) * 0.4 * mask
    wrinkles = (noise3(pos, 26.0, 63, octaves=2) - 0.5) * mask
    height += wrinkles * 1.2
    grime = np.clip(1 - rim_d / 0.05, 0, 1) * (0.5 + 0.5 * noise3(pos, 25, 64)) * 0.6 + np.clip(1 - (hem_d / 0.06), 0, 1) * 0.4
    albedo *= (1 - 0.18 * np.clip(grime, 0, 1))[..., None]
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, strength=1.6)
    tiling = G.metres_per_uv(obj) / 0.025
    return {"detail": "weave", "tiling": round(tiling, 2), "detail_strength": 0.85, "roughness": 0.93,
            "preview": [float(x) for x in base]}


def paint_mask(obj, folder, name, c, size=1024):
    """Scrap steel cut, beaten and welded: a bead with heat tint down the centre seam and where the brow piece meets
    the cheeks, breathing holes drilled either side of the mouth, rivets round the edges and on the band and strap,
    grinding marks, rust in the holes and along the edges."""
    mask, pos, nor, face = G.rasterise(obj, size)
    parts = np.zeros(len(obj.data.polygons), np.int32)
    obj.data.attributes["part"].data.foreach_get("value", parts)
    part = np.where(mask & (face >= 0), parts[np.clip(face, 0, None)], -1)
    plate = part == PART_MASK_PLATE
    band = part == PART_MASK_BAND
    rel = pos - np.array(c[:], np.float32)
    length = np.maximum(np.linalg.norm(rel, axis=-1), 1e-6)
    e = np.arcsin(np.clip(rel[..., 2] / length, -1, 1))
    a = np.arctan2(rel[..., 0], -rel[..., 1])
    edge = G.edge_distance(mask, 16)
    hammer = noise3(pos, 60, 71, octaves=2)
    wear = noise3(pos, 14, 72) * 0.7 + noise3(pos, 45, 73) * 0.3
    steel = np.array((0.34, 0.345, 0.35), np.float32)
    albedo = steel * (0.85 + 0.3 * hammer)[..., None] * np.ones((size, size, 1), np.float32)
    # Grinding marks: fine streaks across the plate.
    streaks = noise3(pos * np.array((1.0, 1.0, 9.0), np.float32), 160, 74, octaves=1)
    albedo *= (0.92 + 0.12 * streaks)[..., None]
    height = hammer * 0.5 + streaks * 0.15
    rough = np.clip(0.42 + 0.14 * hammer - 0.1 * streaks, 0.2, 1.0)
    metal = np.full(mask.shape, 0.9, np.float32)
    # Weld beads with heat tint: down the centre below the slit, and where the brow piece meets the cheeks.
    r = length
    d_centre = np.where(e < -MASK_SLIT, np.abs(a) * r, 1.0)
    d_sides = np.where(np.abs(e) < 0.3, np.abs(np.abs(a) - (MASK_SLIT_WIDTH + 0.06)) * r, 1.0)
    weld_d = np.minimum(d_centre, d_sides) * plate
    weld_d = np.where(plate, weld_d, 1.0)
    bead = np.clip(1 - weld_d / 0.0028, 0, 1)
    ripple = 0.5 + 0.5 * np.sin((e * r + a * r) * 1500.0)
    height += bead * (1.0 + 0.4 * ripple)
    albedo = albedo * (1 - 0.2 * bead[..., None])
    # Heat tint fading from the weld: brown, blue, then straw at its edge.
    tint = np.clip(1 - weld_d / 0.016, 0, 1) * plate
    straw, blue, brown = (np.array(v, np.float32) for v in ((0.52, 0.44, 0.28), (0.24, 0.26, 0.36), (0.33, 0.21, 0.13)))
    k = tint[..., None]
    heat = np.where(k < 0.5, straw + (blue - straw) * np.clip(k / 0.5, 0, 1), blue + (brown - blue) * np.clip((k - 0.5) / 0.5, 0, 1))
    albedo = albedo * (1 - 0.4 * k) + heat * 0.4 * k
    # Breathing holes either side of the mouth: three rows of four.
    holes = np.zeros(mask.shape, np.float32)
    for side in (-1, 1):
        for row in range(3):
            for col in range(4):
                ea = -0.40 - row * 0.065
                aa = side * (0.26 + col * 0.075 + row * 0.02)
                d = np.hypot((a - aa) * np.cos(e) * r, (e - ea) * r)
                holes = np.maximum(holes, np.clip(1 - d / 0.0024, 0, 1) * plate)
    hole = holes > 0.25
    ring = (holes > 0.0) & ~hole
    albedo[hole] = np.array((0.02, 0.018, 0.016), np.float32)
    height -= holes * 1.5
    rough[hole] = 1.0
    metal[hole] = 0.0
    albedo[ring] *= 1.25
    # Rivets round the edges of the plate and along the band and strap.
    line = (plate & (np.abs(edge - 8) < 0.5)) | (band & (np.abs(edge - 5) < 0.5))
    candidates = pos[line]
    heads = np.zeros((0, 3), np.float32)
    for q in candidates[np.lexsort((candidates[:, 1], candidates[:, 0], candidates[:, 2]))]:
        if len(heads) == 0 or np.min(np.linalg.norm(heads - q, axis=1)) > 0.03:
            heads = np.vstack([heads, q[None]])
    dome = np.zeros(mask.shape, np.float32)
    ys, xs = np.nonzero(plate | band)
    texels = pos[ys, xs]
    for hd in heads:
        d = np.linalg.norm(texels - hd, axis=1)
        near = d < 0.004
        dome[ys[near], xs[near]] = np.maximum(dome[ys[near], xs[near]], 1 - d[near] / 0.004)
    rivet = dome > 0
    albedo = np.where(rivet[..., None], np.array((0.55, 0.55, 0.53), np.float32) * (0.8 + 0.3 * dome[..., None]), albedo)
    rough[rivet] = 0.3
    height += np.sqrt(dome) * 2.0
    # Rust in the holes, along the edges and in the welds' heat, grime in the hollows.
    rust = np.clip((noise3(pos, 26, 75) - 0.62) * 4, 0, 1) * np.clip(1 - edge / 10, 0.25, 1)
    rust = np.clip(rust + np.clip(holes * 2.0, 0, 1) * 0.5 + tint * 0.25 * noise3(pos, 50, 76), 0, 1) * (plate | band)
    albedo = albedo * (1 - rust[..., None] * 0.7) + np.array((0.32, 0.14, 0.06), np.float32) * rust[..., None] * 0.7
    metal = np.clip(metal - 0.7 * rust, 0, 1)
    rough = np.clip(rough + 0.4 * rust, 0, 1)
    grime = np.clip((wear - 0.55) * 3, 0, 1) * 0.35
    albedo *= (1 - grime)[..., None]
    ao = np.clip(0.75 + 0.25 * np.clip(edge / 6, 0, 1), 0, 1)
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, rough=rough, metal=metal, ao=ao, strength=2.5)
    return {"detail": "metal", "tiling": round(G.metres_per_uv(obj) / 0.1, 2), "detail_strength": 0.25,
            "roughness": 0.45, "metallic": 0.9, "specular": 0.5, "normal_strength": 1.2, "preview": [0.34, 0.345, 0.35]}


def paint_gloves(obj, folder, name, m, size=1024):
    """Tight woven fibre, darker and harder-wearing on the palm and the inside of the fingers, a ribbed cuff, seams
    along the fingers, dust worked into the fingertips and creases over the knuckles."""
    mask, pos, nor, _ = G.rasterise(obj, size)
    base = np.array((0.25, 0.225, 0.19), np.float32)
    tone = 0.88 + 0.2 * noise3(pos, 14.0, 81) + 0.05 * (noise3(pos, 60.0, 82) - 0.5)
    albedo = base * tone[..., None] * np.ones((size, size, 1), np.float32)
    height = noise3(pos, 140, 83, octaves=2) * 0.25
    palm = np.zeros(mask.shape, bool)
    cuff = np.zeros(mask.shape, np.float32)
    tips = np.zeros(mask.shape, np.float32)
    for s in ("_l", "_r"):
        side = (pos[..., 0] > 0) if s == "_l" else (pos[..., 0] < 0)
        wrist = np.array(m.head_of("hand" + s)[:], np.float32)
        middle = np.array(m.head_of("middle_01" + s)[:], np.float32)
        index = np.array(m.head_of("index_01" + s)[:], np.float32)
        pinky = np.array(m.head_of("pinky_01" + s)[:], np.float32)
        f = (middle - wrist) / np.linalg.norm(middle - wrist)
        t = index - pinky
        t = t - f * (t @ f)
        t /= np.linalg.norm(t)
        z = np.cross(f, t)
        palm_n = -z if s == "_r" else z
        palm |= side & ((nor @ palm_n) > 0.35)
        fore = wrist - np.array(m.head_of("lowerarm" + s)[:], np.float32)
        fore /= np.linalg.norm(fore)
        along = (pos - wrist) @ fore
        cuff = np.maximum(cuff, side * np.clip(1 - (along + 0.045) / 0.025, 0, 1))
        for finger in ("thumb", "index", "middle", "ring", "pinky"):
            tip = np.array(m.tail_of(finger + "_03" + s)[:], np.float32)
            tips = np.maximum(tips, side * np.clip(1 - np.linalg.norm(pos - tip, axis=-1) / 0.014, 0, 1))
    palm &= mask
    albedo[palm] = (albedo * np.array((0.78, 0.74, 0.7), np.float32))[palm]
    height += np.where(palm, noise3(pos, 90, 84, octaves=2) * 0.5, 0.0)
    ribs = 0.5 + 0.5 * np.sin((pos[..., 0] * 0.3 + pos[..., 1] + pos[..., 2]) * 900.0)
    height += cuff * ribs * 0.8
    albedo *= (1 - 0.1 * cuff)[..., None]
    dust = np.array((0.36, 0.33, 0.28), np.float32)
    albedo = albedo * (1 - 0.4 * tips[..., None]) + dust * 0.4 * tips[..., None]
    edge = G.edge_distance(mask, 8)
    seam = np.clip(1 - np.abs(edge - 3) / 1.0, 0, 1) * (edge < 7) * mask
    albedo *= (1 - 0.25 * seam)[..., None]
    height += np.clip(1 - edge / 2.5, 0, 1) * 0.5 * mask
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, strength=1.8)
    return {"detail": "knit", "tiling": round(G.metres_per_uv(obj) / 0.012, 2), "detail_strength": 0.7,
            "roughness": 0.9, "preview": [float(x) for x in base]}


def detail_textures(folder):
    """Tileable surface details shared by all garments: weave, twill, knit, rib, leather, metal."""
    size = 512
    maps = {
        "weave": S.height_to_normal(S.plain_weave(size, 24, 1), 3.0),
        "twill": S.height_to_normal(S.twill(size, 32, 2), 2.6),
        "knit": S.height_to_normal(S.knit(size, 24, 3), 3.0),
        "rib": S.height_to_normal(np.rot90(S.knit(size, 20, 4)).copy(), 3.2),
        "leather": S.height_to_normal(S.leather(size, 5), 4.0),
        "metal": S.height_to_normal(S.value_noise(size, 64, 6, octaves=3) * 0.6, 2.0),
    }
    for key, img in maps.items():
        H.save_image(S.to_blender_pixels(img), size, size, os.path.join(folder, key + "_normal.png"))
    H.log("detail textures:", ", ".join(maps))


if __name__ == "__main__":
    main()
