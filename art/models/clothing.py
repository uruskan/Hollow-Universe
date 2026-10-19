"""The metal mask as an item, for its icon and the dropped item; its worn version is the character package's.

The other clothing items (woven hood, shirt, trousers and gloves, work boots, chest plate) are made from the worn
garments themselves by art/models/garment_items.py; the stand-ins this script used to build for them are gone.

The mask lies as it would be shown in the inventory: front toward -Y (Godot +Z), upright.

    blender -b --factory-startup --python art/models/clothing.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
from mathutils import Vector, noise  # noqa: E402

from hollow import script_args  # noqa: E402
from hollow.build import GUNMETAL, LEATHER, RUBBER, RUST, STEEL, Model  # noqa: E402


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


ALL = {"metal-mask": metal_mask}

for key in script_args() or list(ALL):
    ALL[key]()
