"""Builds the first outfit: game/assets/characters/clothing.glb, its textures and clothing.json.

    blender -b --factory-startup --python art/characters/build_clothing.py

Needs art/.cache/human_male.blend from build_body.py. Every garment is a shell cut from the body's own surface, so it
fits, carries the body's weights and regions, and is skinned to the same skeleton:

    knit cap · woven shirt · woven trousers · laced boots · riveted breastplate (front and back plates on straps)

Layers, from the skin out (metres off the skin): trousers 6 mm, shirt 9 mm, boots 12–20 mm, breastplate 30 mm. The
client hides body regions under garments and parts of inner garments under outer ones (GROUND.md §4.8).
"""
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
    bpy.ops.wm.open_mainfile(filepath=os.path.join(H.CACHE, "human_male.blend"))
    body = bpy.data.objects["body_source"]
    rig = bpy.data.objects["Rig"]
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o is not body:
            bpy.data.objects.remove(o)
    marks = Marks(rig)
    textures = os.path.join(out, "textures")
    detail_textures(textures)

    shirt = woven_shirt(body, marks)
    garments = [
        ("knit_cap", knit_cap(body, marks)),
        ("woven_shirt", shirt),
        ("woven_trousers", woven_trousers(body, marks)),
        ("laced_boots", laced_boots(body, marks)),
        # The plate is shaped over the shirt, so it always clears it.
        ("riveted_breastplate", breastplate(body, marks, shirt[0])),
    ]
    surfaces = {}
    exported = []
    counts = {}
    for name, (obj, paint) in garments:
        G.weights_from_body(obj, body)
        G.regions_from_body(obj, body)
        G.pack_uvs(obj)
        surfaces[name] = paint(obj, textures, name)
        obj.data.materials.clear()
        obj.data.materials.append(bpy.data.materials.get(name) or bpy.data.materials.new(name))
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
    H.export_glb(os.path.join(out, "clothing.glb"), [rig] + exported)
    H.write_json(os.path.join(out, "clothing.json"), {
        "source": "art/characters/build_clothing.py (our own work)",
        "meshes": {o.name: {"lods": [int(o.name[-1])], "triangles": counts[o.name]} for o in exported},
        "surfaces": surfaces,
    })
    if a.get("preview"):
        import preview
        for name, _ in garments:
            m = bpy.data.materials[name]
            m["preview_colour"] = surfaces[name]["preview"]
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
    hem_z = m.hip - 0.035
    keep = {R["chest"], R["belly"], R["upper_arm"], R["forearm"]}

    def wanted(f, r):
        c = f.calc_center_median()
        near_wrist = r == R["hand"] and any((c - m.head_of("hand" + s)).length < 0.05 for s in ("_l", "_r"))
        return (r in keep or (r == R["pelvis"] and c.z > hem_z - 0.03) or r == R["collar"] or near_wrist)

    obj = G.extract(body, "woven_shirt", wanted)
    torso = lambda co: abs(co.x) < 0.27  # noqa: E731
    G.cut(obj, (0, 0, hem_z), (0, 0, 1), select=torso)
    neck_y = m.head_of("neck_01").y
    neck_z = m.neck.z - 0.004
    G.round_opening(obj, (0.0, neck_y), 0.092, 0.082, neck_z - 0.04, lambda th: neck_z + 0.03 * math.sin(th))
    for s in ("_l", "_r"):
        wrist = m.head_of("hand" + s)
        fore = (wrist - m.head_of("lowerarm" + s)).normalized()
        G.cut(obj, wrist + fore * 0.012, -fore, select=lambda co, w=wrist: (co - w).length < 0.12)
    arms = G.vertices_in_regions(obj, body, {R["upper_arm"], R["forearm"], R["hand"]})
    G.smooth(obj, 10, 0.5, weight=lambda co: 1.0 if abs(co.x) < 0.2 else 0.4)
    # Roomier towards the hem, so the shirt hangs over the trousers' waist.
    G.offset(obj, lambda co, n: 0.008 + (0.011 * smoothstep(m.waist + 0.06, hem_z, co.z) if abs(co.x) < 0.25 else 0.0))
    # The trunk: round, and hanging straight down from the chest and shoulder blades.
    trunk = set(range(len(obj.data.vertices))) - arms
    G.envelope(obj, (0, m.spine_y, 1.56), (0, 0, -1), sectors=56, blur_theta=3, blur_t=3, drape=(1.56 - 1.37, 0.22),
               extra=0.003, select=lambda co: co.z < 1.52, indices=trunk)
    # Cloth bridges the small bumps of the chest.
    G.smooth(obj, 24, 0.5, weight=lambda co: 1.0 if abs(co.x) < 0.16 and 1.2 < co.z < 1.42 else 0.0)
    for s in ("_l", "_r"):
        shoulder, elbow, wrist = m.head_of("upperarm" + s), m.head_of("lowerarm" + s), m.head_of("hand" + s)
        side = 1 if s == "_l" else -1
        G.envelope(obj, shoulder, elbow - shoulder, sectors=32, blur_theta=2, blur_t=2, extra=0.004,
                   select=near_segment(shoulder, elbow, 0.12, 0.3, 1.0, side), indices=arms)
        G.envelope(obj, elbow, wrist - elbow, sectors=32, blur_theta=2, blur_t=2, extra=0.004,
                   select=near_segment(elbow, wrist, 0.1, 0.0, 1.05, side), indices=arms)
        fore = wrist - elbow

        def cuff(co, t, L=fore.length, sd=side):
            return 0.002 + 0.008 * max(0.0, t / L) if co.x * sd > 0.21 and t > -0.02 else 0.0
        G.radial(obj, elbow, fore, cuff)
        upper = elbow - shoulder
        G.ripples(obj, elbow, fore, -0.05, 0.09, 0.034, 0.005, twist=0.6, mask=lambda co, sd=side: co.x * sd > 0.24)
        G.ripples(obj, elbow, fore, fore.length - 0.1, fore.length + 0.01, 0.026, 0.0045, twist=-0.4,
                  mask=lambda co, sd=side: co.x * sd > 0.24)
        G.ripples(obj, shoulder, upper, 0.06, upper.length - 0.02, 0.05, 0.003, twist=0.9,
                  mask=lambda co, sd=side: co.x * sd > 0.22)
    G.ripples(obj, Vector((0, 0, m.hip)), Vector((0, 0, 1)), 0.0, 0.28, 0.07, 0.004, twist=2.0, mask=torso)
    G.ripples(obj, Vector((0, 0, m.hip)), Vector((0, 0, 1)), 0.18, 0.42, 0.11, 0.003, twist=-3.0,
              mask=lambda co: abs(co.x) < 0.12)
    G.hem(obj, 0.007, bulge=0.0015)
    hems = [("plane", (0, 0, hem_z), (0, 0, 1)), ("plane", (0, 0, neck_z + 0.02), (0, 0.25, -1))]
    for s in ("_l", "_r"):
        fore = (m.head_of("hand" + s) - m.head_of("lowerarm" + s)).normalized()
        hems.append(("plane", m.head_of("hand" + s) + fore * 0.012, -fore))
    colour = (0.47, 0.43, 0.33)
    return obj, lambda o, folder, name: paint_fabric(o, folder, name, base=colour, weave="weave", hems=hems,
                                                     dirt_low=hem_z, dirt_high=hem_z + 0.3, size=1024, preview=colour)


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


def breastplate(body, m, shirt):
    """Front and back plates on leather straps. The plates are built as smooth, mirror-symmetric shells around the
    trunk, a hand's breadth over the shirt; the straps run over the shoulders and round the sides."""
    axis = Vector((0, m.spine_y, 0))
    over = [v.co.copy() for v in shirt.data.vertices if abs(v.co.x) < 0.2 and 1.0 < v.co.z < 1.56]
    table = RadiusTable(over, axis)
    parts = [
        plate_shell("plate_front", table, axis, 0.0, 58.0, 1.13, m.neck.z - 0.03, 0.014, 0.10),
        plate_shell("plate_back", table, axis, 180.0, 54.0, 1.14, m.neck.z + 0.0, 0.014, 0.06),
    ]
    for side in (1, -1):
        parts.append(shoulder_strap("strap_shoulder", shirt, m, side, axis, table))
        parts.append(ring_strap("strap_side", table, axis, side, 1.165, 0.035))
    for part in parts:
        G.solidify(part, 0.0045 if part.name.startswith("plate") else 0.003)
    H.select_only(parts, parts[0])
    bpy.ops.object.join()
    obj = parts[0]
    obj.name = obj.data.name = "riveted_breastplate"
    for mod in list(obj.modifiers):
        obj.modifiers.remove(mod)
    # The plates take the body's own weights (as the shirt under them does), so the two move together and the shirt
    # never comes through, at the price of a plate that flexes a little with the shoulders.
    for g in body.vertex_groups:
        if H.is_deform_group(g.name) and g.name not in obj.vertex_groups:
            obj.vertex_groups.new(name=g.name)
    obj.modifiers.new("Armature", 'ARMATURE').object = body.parent
    body_regions = [0] * len(body.data.polygons)
    body.data.attributes["region"].data.foreach_get("value", body_regions)
    attr = obj.data.attributes.new("region", 'INT', 'FACE')
    attr.data.foreach_set("value", [H.REGION["chest"]] * len(obj.data.polygons))
    return obj, lambda o, folder, name: paint_metal(o, folder, name)


class RadiusTable:
    """Largest radius of a set of points around the trunk's vertical axis, by height and angle, mirrored left to right
    and blurred: the outside of the shirt, as a smooth function a plate can follow."""

    def __init__(self, points, axis, z_step=0.01, sectors=72):
        import numpy as np
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
        i = min(self.t.shape[0] - 1, max(0, int((z - self.z0) / self.z_step)))
        j = int((math.radians(degrees) + math.pi) / math.tau * self.sectors) % self.sectors
        return float(self.t[i, j])


def polar(axis, z, degrees, r):
    a = math.radians(degrees)
    return Vector((axis.x + r * math.sin(a), axis.y - r * math.cos(a), z))


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


def plate_shell(name, table, axis, centre_deg, half_width, z_low, z_high, clearance, bulge):
    rows, cols = 22, 30

    def point(i, j):
        u = j / cols * 2 - 1           # -1..1 across
        v = i / rows                   # 0..1 up
        z = z_low + (z_high - z_low) * v
        # The top edge dips at the middle for the neck; the bottom corners are cut.
        z -= 0.035 * (1 - u * u) * v ** 3
        deg = centre_deg + half_width * u
        r = table.radius(z, deg) + clearance + bulge * 0.12 * (1 - u * u) * math.sin(math.pi * v)
        # Edges turn in a little, like a rolled rim.
        edge = max(abs(u), abs(v * 2 - 1))
        r -= 0.006 * max(0.0, (edge - 0.9) / 0.1) ** 2
        return polar(axis, z, deg, r)

    return grid_object(name, rows, cols, point)


def shoulder_strap(name, shirt, m, side, axis, table):
    """Over the shoulder, from the front plate's top corner to the back plate's."""
    rows, cols = 20, 3
    x = side * 0.105
    top = [v.co for v in shirt.data.vertices if abs(v.co.x - x) < 0.02 and v.co.z > 1.35]
    front_y = axis.y - table.radius(m.neck.z - 0.04, side * 40) - 0.004
    back_y = axis.y + table.radius(m.neck.z - 0.02, 180 - side * 40) + 0.004

    def surface_z(y):
        near = [p.z for p in top if abs(p.y - y) < 0.02]
        return (max(near) if near else m.neck.z) + 0.012

    front_z = m.neck.z - 0.03 - 0.035 * (1 - (40 / 58) ** 2)
    back_z = m.neck.z - 0.035 * (1 - (40 / 54) ** 2)

    def point(i, j):
        v = i / rows
        y = front_y + (back_y - front_y) * v
        # Pinned to the plates' top edges at the ends, lying on the shoulder in between.
        w = math.sin(math.pi * v) ** 0.6
        z = (front_z + (back_z - front_z) * v) * (1 - w) + max(surface_z(y), front_z, back_z) * w
        return Vector((x + (j / cols - 0.5) * 0.04, y, z))

    obj = grid_object("%s_%d" % (name, side), rows, cols, point)
    G.smooth(obj, 6, 0.5)
    return obj


def ring_strap(name, table, axis, side, z, width):
    """Round the side, from the front plate's edge to the back plate's."""
    rows, cols = 2, 16
    start, stop = 50, 132

    def point(i, j):
        deg = side * (start + (stop - start) * j / cols)
        zz = z - width / 2 + width * i / rows
        return polar(axis, zz, deg, table.radius(zz, deg) + 0.009)

    obj = grid_object("%s_%d" % (name, side), rows, cols, point)
    if side < 0:
        # Keep the faces pointing outwards on the right side, where the grid runs the other way.
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        bm.to_mesh(obj.data)
        bm.free()
    return obj


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


def paint_fabric(obj, folder, name, base, weave, hems, dirt_low=None, dirt_high=None, belt=None, cap_line=None,
                 size=1024, preview=None):
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
    size = 1024
    mask, pos, nor, _ = G.rasterise(obj, size)
    paint = np.array((0.27, 0.285, 0.29), np.float32)
    steel = np.array((0.56, 0.56, 0.55), np.float32)
    leather = np.array((0.13, 0.08, 0.05), np.float32)
    wear = noise3(pos, 14, 31) * 0.7 + noise3(pos, 60, 32) * 0.3
    edge = G.edge_distance(mask, 16)
    chipped = np.clip((wear - 0.62) * 6, 0, 1) + np.clip(1 - edge / 5, 0, 1) * 0.8
    chipped = np.clip(chipped, 0, 1)
    rust = np.clip((noise3(pos, 22, 33) - 0.58) * 5, 0, 1) * (1 - chipped)
    albedo = paint * (0.85 + 0.25 * noise3(pos, 30, 34))[..., None]
    albedo = albedo * (1 - chipped[..., None]) + steel * chipped[..., None]
    albedo = albedo * (1 - rust[..., None] * 0.7) + np.array((0.32, 0.14, 0.06), np.float32) * rust[..., None] * 0.7
    metal = np.clip(0.7 + 0.3 * chipped - 0.6 * rust, 0, 1)
    rough = np.clip(0.5 - 0.22 * chipped + 0.35 * rust, 0.2, 1.0)
    height = wear * 0.2
    # Straps are leather: the thin parts away from the plates (they are narrow in x and high on the shoulder).
    strap = mask & (((np.abs(pos[..., 0]) > 0.06) & (np.abs(pos[..., 0]) < 0.13) & (pos[..., 2] > 1.42)) |
                    ((pos[..., 2] > 1.14) & (pos[..., 2] < 1.21) & (np.abs(pos[..., 0]) > 0.12)))
    albedo[strap] = (leather * (0.8 + 0.4 * noise3(pos, 40, 35))[..., None])[strap]
    metal[strap] = 0.0
    rough[strap] = 0.7
    # Rivets along the plate edges.
    along = (pos[..., 0] + pos[..., 2]) * 70
    rivet = (np.abs(edge - 7) < 2.2) & (np.sin(along) > 0.92) & ~strap
    albedo[rivet] = steel * 1.15
    metal[rivet] = 1.0
    rough[rivet] = 0.35
    height += np.where(rivet, 2.0, 0.0) + np.clip(1 - edge / 4, 0, 1) * 0.6
    ao = np.clip(0.75 + 0.25 * np.clip(edge / 6, 0, 1), 0, 1)
    save_maps(folder, name, np.clip(albedo, 0, 1), height, mask, rough=rough, metal=metal, ao=ao, strength=2.5)
    return {"detail": "metal", "tiling": round(G.metres_per_uv(obj) / 0.12, 2), "detail_strength": 0.25,
            "roughness": 0.5, "metallic": 0.6, "specular": 0.5, "normal_strength": 1.2, "preview": [0.27, 0.285, 0.29]}


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
