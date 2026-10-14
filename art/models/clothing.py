"""Clothing and armour as items, for icons and dropped items: stand-ins for the woven hood, shirt, trousers and
gloves, work boots and chest plate, and the metal mask. The character package models what is worn; the stand-ins only
give the inventory a picture (and a shape on the ground) until its meshes can be rendered instead (models.json marks
them `standIn`). The metal mask is a real item model; its worn version is the character package's.

Garments lie as they would be shown in the inventory: front toward -Y (Godot +Z), upright.

    blender -b --factory-startup --python art/models/clothing.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

from hollow import script_args  # noqa: E402
from hollow.build import CANVAS, GUNMETAL, LEATHER, RUBBER, RUST, STEEL, Model  # noqa: E402

WOVEN = (0.64, 0.57, 0.41)


def garment(m, outline, thickness, bulge=0.02, **kw):
    """A flat garment from a front outline (x, z), with soft rounded edges and a gentle front bulge."""
    o = m.extrude(outline, thickness, bevel=thickness * 0.42, segments=3, **kw)
    lo = min(z for _, z in outline)
    hi = max(z for _, z in outline)
    w = max(abs(x) for x, _ in outline)
    for v in o.data.vertices:
        # Fuller in the middle, like cloth over a body.
        k = (1 - (v.co.x / w) ** 2) * (1 - ((v.co.z - (lo + hi) / 2) / ((hi - lo) / 2)) ** 2)
        v.co.y *= 1 + bulge / thickness * max(0.0, k)
    o.data.update()
    return o


def hood(seed=81):
    m = Model("woven-hood", seed=seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=5, radius=1.0)

    def opening(c):
        # An oval face opening at the front.
        return c.y < -0.3 and (c.x / 0.62) ** 2 + ((c.z - 0.2) / 0.55) ** 2 < 1

    gone = [f for f in bm.faces if f.calc_center_median().z < -0.2 or opening(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bmesh.ops.scale(bm, vec=(0.115, 0.13, 0.15), verts=bm.verts)
    # The hood's back peaks a little.
    for v in bm.verts:
        if v.co.y > 0 and v.co.z > 0.05:
            v.co.y += 0.03 * (v.co.z / 0.14) ** 2
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.012)
    m.add(bm, (0, 0, 0.2), tile=CANVAS, tint=WOVEN, metres=0.35, vary=0.15, vary_scale=20, sharp=0)
    # A rolled hem round the face opening hides the cut.
    hem = []
    for k in range(33):
        t = 2 * math.pi * k / 32
        x, z = 0.62 * math.cos(t), 0.2 + 0.55 * math.sin(t)
        if z < -0.2:
            z = -0.2
        y = -math.sqrt(max(0.0, 1 - x * x - z * z))
        hem.append((x * 0.115, y * 0.13 - 0.004, z * 0.15 + 0.2))
    m.tube(hem, 0.007, sides=6, tile=CANVAS, tint=(0.5, 0.44, 0.31), metres=0.2, cap=False)
    # A short cape over the shoulders.
    cape = [(0.105, -0.02), (0.13, -0.05), (0.165, -0.1), (0.172, -0.115)]
    o = m.lathe(cape, (0, 0, 0.2), sides=32, close=False, tile=CANVAS, tint=WOVEN, metres=0.4, vary=0.15, sharp=0)
    m.lathe([(r * 1.04, z) for r, z in cape][::-1], (0, 0, 0.2), sides=32, close=False, tile=CANVAS, tint=(0.5, 0.44, 0.31),
            metres=0.4, sharp=0)
    m.tube([(0.035, -0.115, 0.22), (0.03, -0.12, 0.17), (0.034, -0.11, 0.12)], 0.0025, sides=5, tile=LEATHER, tint=(0.7, 0.6, 0.5), metres=0.1)
    m.tube([(-0.035, -0.115, 0.22), (-0.03, -0.12, 0.17), (-0.034, -0.11, 0.12)], 0.0025, sides=5, tile=LEATHER, tint=(0.7, 0.6, 0.5), metres=0.1)
    m.export()


def shirt(seed=83):
    m = Model("woven-shirt", seed=seed)
    outline = [(-0.2, 0.0), (0.2, 0.0), (0.21, 0.4), (0.37, 0.27), (0.42, 0.33), (0.25, 0.56), (0.1, 0.6), (0.06, 0.56),
               (0.0, 0.545), (-0.06, 0.56), (-0.1, 0.6), (-0.25, 0.56), (-0.42, 0.33), (-0.37, 0.27), (-0.21, 0.4)]
    garment(m, outline, 0.05, tile=CANVAS, tint=WOVEN, metres=0.6, vary=0.12, vary_scale=12)
    # Collar and hem, a darker binding.
    neck = [(0.1 * math.cos(a), -0.028, 0.6 - 0.055 * math.sin(a) - 0.0) for a in (math.pi * i / 10 for i in range(11))]
    m.tube(neck, 0.009, sides=6, tile=CANVAS, tint=(0.46, 0.4, 0.28), metres=0.2, cap=True)
    m.box((0.4, 0.056, 0.03), (0, 0, 0.018), bevel=0.01, tile=CANVAS, tint=(0.5, 0.44, 0.31), metres=0.4)
    for x in (-0.045, 0.045):
        m.tube([(x, -0.03, 0.54), (x * 0.8, -0.033, 0.48)], 0.003, sides=5, tile=LEATHER, tint=(0.6, 0.5, 0.4), metres=0.1)
    m.export()


def trousers(seed=85):
    m = Model("woven-trousers", seed=seed)
    tint = (0.56, 0.5, 0.36)
    # Hips, then two legs splaying a little; flat shapes with soft edges (no concave outlines).
    garment(m, [(-0.205, 0.6), (0.205, 0.6), (0.19, 0.95), (-0.19, 0.95)], 0.056, bulge=0.01, tile=CANVAS, tint=tint, metres=0.6,
            vary=0.12, vary_scale=12)
    for side in (-1, 1):
        leg = [(-0.1, 0.0), (0.08, 0.0), (0.105, 0.66), (-0.1, 0.66)]
        o = garment(m, leg, 0.05, bulge=0.006, tile=CANVAS, tint=tint, metres=0.6, vary=0.12, vary_scale=12)
        o.data.transform(Matrix.Translation((side * 0.108, 0, 0.62)) @ Matrix.Rotation(math.radians(-4.5 * side), 4, "Y")
                         @ Matrix.Translation((0, 0, -0.62)) @ Matrix.Diagonal((side, 1, 1, 1)))
        if side < 0:
            o.data.flip_normals()
        m.box((0.18, 0.056, 0.035), (side * 0.155, 0, 0.022), (0, -4.5 * side, 0), bevel=0.012, tile=CANVAS, tint=(0.46, 0.4, 0.29), metres=0.3)
    m.box((0.4, 0.062, 0.06), (0, 0, 0.92), bevel=0.012, tile=CANVAS, tint=(0.44, 0.38, 0.27), metres=0.4)
    for x in (-0.02, 0.02):
        m.tube([(x, -0.032, 0.9), (x * 1.75, -0.036, 0.84), (x * 1.5, -0.038, 0.79)], 0.0035, sides=5, tile=LEATHER,
               tint=(0.7, 0.6, 0.5), metres=0.1)
    m.export()


def glove(m, at, mirror, turn):
    s = -1 if mirror else 1
    parts = [m.box((0.088, 0.032, 0.095), (0, 0, 0.0), bevel=0.014, segments=3, tile=CANVAS, tint=WOVEN, metres=0.2, vary=0.1)]
    for x, length in ((-0.031, 0.068), (-0.0105, 0.08), (0.0105, 0.077), (0.031, 0.06)):
        parts.append(m.lathe([(0.0, 0.0), (0.0105, 0.004), (0.0108, length - 0.01), (0.0085, length - 0.003), (0.0, length)],
                             (x * s, 0, 0.035), sides=10, tile=CANVAS, tint=WOVEN, metres=0.15, sharp=0))
    parts.append(m.lathe([(0.0, 0.0), (0.012, 0.004), (0.012, 0.05), (0.009, 0.058), (0.0, 0.062)], (0.04 * s, -0.004, -0.02),
                         (0, 48 * s, 0), sides=10, tile=CANVAS, tint=WOVEN, metres=0.15, sharp=0))
    parts.append(m.box((0.07, 0.034, 0.03), (0, -0.002, -0.012), bevel=0.01, segments=2, tile=LEATHER, tint=(0.72, 0.58, 0.44), metres=0.1))
    parts.append(m.cylinder(0.048, 0.05, (0, 0, -0.07), sides=16, tile=LEATHER, tint=(0.66, 0.54, 0.42), metres=0.2, scale=(1.0, 0.45, 1.0)))
    place = Matrix.LocRotScale(Vector(at), Matrix.Rotation(math.radians(turn), 3, "Y").to_quaternion(), None)
    for p in parts:
        p.data.transform(place)


def gloves(seed=87):
    m = Model("woven-gloves", seed=seed)
    glove(m, (-0.075, 0.0, 0.13), False, -12)
    glove(m, (0.075, 0.02, 0.13), True, 14)
    m.export()


def boot(m, at, turn, mirror):
    s = -1 if mirror else 1
    # Side profile (y forward, z up) of the upper, then the sole under it.
    upper = [(-0.11, 0.03), (0.06, 0.03), (0.13, 0.04), (0.145, 0.07), (0.11, 0.1), (0.04, 0.12), (0.0, 0.2), (0.0, 0.25),
             (-0.1, 0.25), (-0.115, 0.15)]
    o = m.extrude(upper, 0.095, bevel=0.025, segments=3, tile=LEATHER, tint=(0.78, 0.56, 0.38), metres=0.3, vary=0.1)
    o.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    sole = [(-0.12, 0.0), (0.075, 0.0), (0.15, 0.012), (0.155, 0.035), (-0.12, 0.035)]
    so = m.extrude(sole, 0.105, bevel=0.008, segments=2, tile=RUBBER, tint=(0.75, 0.72, 0.7), metres=0.3)
    so.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    laces = []
    for i in range(5):
        z = 0.11 + i * 0.027
        y = 0.04 - i * 0.008
        laces.append(m.tube([(-0.03, y + 0.01 * (z > 0.2), z), (0.03, y + 0.01 * (z > 0.2), z + 0.01)], 0.003, sides=5,
                            tile=CANVAS, tint=(0.85, 0.8, 0.7), metres=0.1))
    place = Matrix.LocRotScale(Vector(at), Matrix.Rotation(math.radians(turn), 3, "Z").to_quaternion(), Vector((s, 1, 1)))
    for p in [o, so] + laces:
        p.data.transform(place)
        if mirror:
            p.data.flip_normals()


def boots(seed=89):
    m = Model("work-boots", seed=seed)
    boot(m, (-0.065, 0.0, 0.0), 8, False)
    boot(m, (0.075, 0.03, 0.0), -6, True)
    m.export()


def chest_plate(seed=91):
    m = Model("chest-plate", seed=seed)
    # A breastplate: curved across and bulged forward, with a scooped neck and arm holes; straps over the shoulders.
    width, height = 0.4, 0.44
    bm = bmesh.new()
    nu, nv = 14, 30

    def half(v):
        return 1.0 - max(0.0, v - 0.55) ** 1.3 * 1.15  # arm holes narrow the top

    def neck(v):
        return 0.36 * math.sqrt(max(0.0, 1 - ((1 - v) / 0.2) ** 2)) if v > 0.8 else 0.0

    def point(u, v):
        return (u * width / 2, -0.075 * (1 - u * u) - 0.025 * math.sin(math.pi * v) * (1 - u * u), 0.04 + v * height)

    for side in (-1, 1):
        grid = {}
        for j in range(nv + 1):
            v = j / nv
            lo, hi = neck(v), half(v)
            for i in range(nu + 1):
                u = side * (lo + (hi - lo) * i / nu)
                grid[i, j] = bm.verts.new(point(u, v))
        for j in range(nv):
            for i in range(nu):
                quad = (grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1])
                bm.faces.new(quad if side > 0 else tuple(reversed(quad)))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.008)
    m.add(bm, tile=RUST, tint=(0.92, 0.9, 0.88), metres=0.5, sharp=50)
    # A rolled rim along the bottom, rivets along the edges.
    rim = [(x, -0.075 * (1 - (2 * x / width) ** 2) - 0.004, 0.045) for x in [width / 2 * (k / 10 * 2 - 1) for k in range(11)]]
    m.tube(rim, 0.007, sides=8, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.3)
    for k in range(7):
        x = width / 2 * (k / 6 * 2 - 1) * 0.86
        m.cylinder(0.0055, 0.006, (x, -0.075 * (1 - (2 * x / width) ** 2) - 0.008, 0.075), (90, 0, 0), sides=8, tile=STEEL,
                   tint=(1.0, 1.0, 1.0), metres=0.03)
    for x in (-0.115, 0.115):
        m.tube([(x, -0.06, 0.43), (x * 1.05, -0.03, 0.5), (x * 1.08, 0.05, 0.52), (x * 1.08, 0.1, 0.45), (x * 1.05, 0.11, 0.33)], 0.012,
               sides=6, tile=LEATHER, tint=(0.66, 0.52, 0.4), metres=0.3)
        m.box((0.036, 0.016, 0.03), (x, -0.062, 0.415), bevel=0.004, tile=STEEL, tint=(0.95, 0.95, 0.95), metres=0.05)
        m.box((0.03, 0.06, 0.03), (x * 1.75, 0.0, 0.2), bevel=0.006, tile=LEATHER, tint=(0.6, 0.48, 0.36), metres=0.1)
    m.export()


def metal_mask(seed=93):
    """The metal mask as an item (the worn one is the character package's): a hammered plate shaped over a face with
    two round eye ports in riveted bezels, a raised brow band, a slotted breathing grille over the mouth and leather
    straps. Front toward -Y, upright."""
    m = Model("metal-mask", seed=seed)
    nu, nv = 40, 48
    eyes = [(-0.034, 0.192), (0.034, 0.192)]  # (x, z) of the eye ports
    eye_r = 0.019

    def halfw(v):
        return 0.09 * (0.66 + 0.34 * math.sqrt(max(0.0, math.sin(math.pi * (0.12 + 0.86 * v)))))

    def surface(u, v):
        x = u * halfw(v)
        z = 0.02 + v * 0.235
        nose = 0.017 * math.exp(-(u / 0.2) ** 2) * math.exp(-((v - 0.46) / 0.16) ** 2)
        chin = 0.008 * math.exp(-((v - 0.05) / 0.1) ** 2) * (1 - u * u)
        y = -0.082 * (1 - u * u) ** 0.7 - nose - chin + 0.012 * (v - 0.5) ** 2
        return Vector((x, y, z))

    def in_eye(p, grow=0.0):
        return any(math.hypot(p.x - ex, p.z - ez) < eye_r + grow for ex, ez in eyes)

    bm = bmesh.new()
    grid = {}
    for j in range(nv + 1):
        for i in range(nu + 1):
            grid[i, j] = bm.verts.new(surface(i / nu * 2 - 1, j / nv))
    for j in range(nv):
        for i in range(nu):
            c = surface((i + 0.5) / nu * 2 - 1, (j + 0.5) / nv)
            if not in_eye(c):
                bm.faces.new((grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.004)

    def hammered(co, tint):
        n = noise.noise(co * 70 + Vector((4.1, 2.2, 0.3)), noise_basis="PERLIN_ORIGINAL")
        r = noise.noise(co * 18 + Vector((1.3, 7.7, 2.9)), noise_basis="PERLIN_ORIGINAL")
        k = 0.92 + 0.12 * n
        if r > 0.3:  # rust blooms
            return [tint[0] * k * 1.05, tint[1] * k * 0.78, tint[2] * k * 0.6]
        return [t * k for t in tint]

    m.add(bm, tile=STEEL, tint=(0.78, 0.76, 0.73), metres=0.3, sharp=50, tint_fn=hammered)

    def normal_at(u, v):
        e = 0.01
        du = surface(u + e, v) - surface(u - e, v)
        dv = surface(u, v + e) - surface(u, v - e)
        n = dv.cross(du).normalized()
        return n if n.y < 0 else -n

    def uv_of(x, z):
        v = (z - 0.02) / 0.235
        return x / halfw(v), v

    # Eye bezels: riveted rings standing proud of the plate, facing out along the surface.
    for ex, ez in eyes:
        u, v = uv_of(ex, ez)
        p, n = surface(u, v), normal_at(u, v)
        rot = n.to_track_quat("Z", "Y").to_euler()
        rot_deg = tuple(math.degrees(a) for a in rot)
        m.lathe([(eye_r - 0.001, -0.002), (eye_r + 0.007, -0.002), (eye_r + 0.008, 0.002), (eye_r + 0.006, 0.0055),
                 (eye_r - 0.001, 0.005)], p + n * 0.0005, rot_deg, sides=28, tile=GUNMETAL, tint=(0.72, 0.7, 0.68),
                metres=0.08, sharp=40)
        for k in range(6):
            a = 2 * math.pi * k / 6 + 0.3
            q = Vector((math.cos(a) * (eye_r + 0.0045), math.sin(a) * (eye_r + 0.0045), 0.0058))
            m.cylinder(0.0022, 0.002, p + n * 0.0005 + rot.to_matrix() @ q, rot_deg, sides=8, tile=STEEL,
                       tint=(0.9, 0.88, 0.85), metres=0.02)
    # A raised brow band across the forehead, riveted.
    band = bmesh.new()
    rows = []
    for j, v in enumerate((0.8, 0.865)):
        rows.append([band.verts.new(surface(u, v) + normal_at(u, v) * 0.004) for u in [k / 20 * 1.9 - 0.95 for k in range(21)]])
    for i in range(20):
        band.faces.new((rows[0][i], rows[0][i + 1], rows[1][i + 1], rows[1][i]))
    bmesh.ops.solidify(band, geom=list(band.faces), thickness=0.004)
    m.add(band, tile=RUST, tint=(0.7, 0.62, 0.55), metres=0.2, sharp=40)
    for k in range(7):
        u = k / 6 * 1.6 - 0.8
        p, n = surface(u, 0.832), normal_at(u, 0.832)
        rot = n.to_track_quat("Z", "Y").to_euler()
        m.cylinder(0.0028, 0.003, p + n * 0.009, tuple(math.degrees(a) for a in rot), sides=8, tile=STEEL, tint=(0.9, 0.88, 0.85),
                   metres=0.02)
    # The breathing grille: a plate over the mouth with four dark slots.
    grille = bmesh.new()
    rows = []
    for v in (0.11, 0.33):
        rows.append([grille.verts.new(surface(u, v) + normal_at(u, v) * 0.004) for u in [k / 12 * 0.9 - 0.45 for k in range(13)]])
    for i in range(12):
        grille.faces.new((rows[0][i], rows[0][i + 1], rows[1][i + 1], rows[1][i]))
    bmesh.ops.solidify(grille, geom=list(grille.faces), thickness=0.004)
    m.add(grille, tile=GUNMETAL, tint=(0.62, 0.6, 0.58), metres=0.15, sharp=40)
    for k in range(4):
        v = 0.15 + k * 0.05
        for side in (-1, 1):
            p, n = surface(side * 0.17, v), normal_at(side * 0.17, v)
            rot = n.to_track_quat("Z", "Y").to_euler()
            m.box((0.024, 0.0045, 0.002), p + n * 0.0092, tuple(math.degrees(a) for a in rot), bevel=0.0, tile=RUBBER,
                  tint=(0.05, 0.05, 0.05), metres=0.02, smooth=False)
    # Rivets round the edge.
    for k in range(18):
        t = k / 17
        for side in (-1, 1):
            if k % 3 == 1:
                continue
            u, v = side * 0.9, 0.08 + t * 0.84
            p, n = surface(u, v), normal_at(u, v)
            rot = n.to_track_quat("Z", "Y").to_euler()
            m.cylinder(0.0024, 0.003, p + n * 0.003, tuple(math.degrees(a) for a in rot), sides=8, tile=STEEL,
                       tint=(0.85, 0.83, 0.8), metres=0.02)
    # Leather straps from the temples and the jaw: flat bands looping round behind, as the mask is worn.
    for v, sag in ((0.62, 0.03), (0.26, 0.015)):
        a, b = surface(-0.995, v), surface(0.995, v)
        depth = 0.13
        bm = bmesh.new()
        rows = []
        n = 24
        for k in range(n + 1):
            t = math.pi * k / n
            p = Vector((b.x * math.cos(t), a.y + depth * math.sin(t), a.z - sag * math.sin(t) ** 2))
            radial = Vector((math.cos(t) / max(abs(b.x), 1e-6), math.sin(t) / depth, 0.0)).normalized()
            rows.append([bm.verts.new(p + radial * dr + Vector((0, 0, dz))) for dr, dz in
                         ((-0.0015, -0.008), (0.0015, -0.008), (0.0015, 0.008), (-0.0015, 0.008))])
        for r0, r1 in zip(rows, rows[1:]):
            for j in range(4):
                k2 = (j + 1) % 4
                bm.faces.new((r0[j], r0[k2], r1[k2], r1[j]))
        bm.faces.new(rows[0])
        bm.faces.new(list(reversed(rows[-1])))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        m.add(bm, tile=LEATHER, tint=(0.5, 0.38, 0.27), metres=0.3, sharp=60)
        for side in (-1, 1):
            p = surface(side * 0.995, v)
            m.box((0.008, 0.02, 0.022), p + Vector((side * 0.004, 0.008, 0)), bevel=0.002, tile=STEEL, tint=(0.8, 0.8, 0.8),
                  metres=0.03)
    m.export()


ALL = {"woven-hood": hood, "woven-shirt": shirt, "woven-trousers": trousers, "woven-gloves": gloves, "work-boots": boots,
       "chest-plate": chest_plate, "metal-mask": metal_mask}

for key in script_args() or list(ALL):
    ALL[key]()
