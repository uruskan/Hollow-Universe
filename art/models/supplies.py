"""Small supplies: bandage, medical injector, berries, tubers, ammunition boxes and the key lock.

    blender -b --factory-startup --python art/models/supplies.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

from hollow import script_args  # noqa: E402
from hollow.build import (CANVAS, GLOSSY, GUNMETAL, IVORY, LEATHER, PARCHMENT, ROCK, RUBBER, RUST, STEEL, TEAL,  # noqa: E402
                          TIMBER, Model)

BRASS = (1.0, 0.78, 0.42)
COPPER = (0.95, 0.58, 0.4)


def ribbon(m, points, width, thickness=0.0015, **kw):
    """A flat strip along `points` (model space), `width` wide across X."""
    bm = bmesh.new()
    rows = []
    for i, p in enumerate(points):
        p = Vector(p)
        t = (Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(i - 1, 0)])).normalized()
        side = Vector((1, 0, 0))
        up = t.cross(side).normalized()
        rows.append([bm.verts.new(p + side * sx * width / 2 + up * sz * thickness / 2)
                     for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for a, b in zip(rows, rows[1:]):
        for j in range(4):
            k = (j + 1) % 4
            bm.faces.new((a[j], a[k], b[k], b[j]))
    bm.faces.new(list(reversed(rows[0])))
    bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return m.add(bm, **kw)


def bandage(seed=61):
    m = Model("bandage", seed=seed, held=True, forward=False)
    # A roll across X with a loose end hanging down the front.
    r, w = 0.032, 0.07
    roll = [(0.012, -w / 2), (r * 0.97, -w / 2), (r, -w / 2 + 0.004), (r * 1.01, 0.0), (r, w / 2 - 0.004), (r * 0.97, w / 2), (0.012, w / 2)]

    def layers(co, tint):
        rr = math.hypot(co.x, co.y) / r
        if abs(co.z) > w / 2 - 0.001:
            return [t * (0.86 + 0.14 * math.cos(rr * 34)) for t in tint]
        return tint

    m.lathe(roll, (0, 0, r), (0, 90, 0), sides=28, tile=CANVAS, tint=(1.0, 0.98, 0.94), metres=0.12, vary=0.05, sharp=60,
            tint_fn=layers)
    m.lathe([(0.012, -w / 2 - 0.0005), (0.006, -w / 2), (0.006, w / 2), (0.012, w / 2 + 0.0005)], (0, 0, r), (0, 90, 0), sides=16,
            close=False, tile=PARCHMENT, tint=(0.8, 0.72, 0.6), metres=0.1)
    # The loose end runs out along the ground toward the front.
    tail = [(0.0, -0.08 * i / 8, 0.0012 + 0.0022 * math.sin(i * 1.4) * (i / 8)) for i in range(9)]
    ribbon(m, tail, w * 0.98, tile=CANVAS, tint=(1.0, 0.98, 0.94), metres=0.12)
    m.export(floor=True)


def injector(seed=63):
    m = Model("medical-injector", seed=seed, held=True)
    # Along +Y, needle end forward; grip at the middle.
    m.lathe([(0.0, -0.075), (0.008, -0.075), (0.0125, -0.07), (0.0125, -0.03), (0.0, -0.03)], (0, 0, 0), (-90, 0, 0),
            sides=20, tile=RUBBER, tint=(0.9, 0.9, 0.9), metres=0.1)
    m.lathe([(0.0, -0.03), (0.0135, -0.03), (0.0135, 0.055), (0.0, 0.055)], (0, 0, 0), (-90, 0, 0), sides=20, tile=IVORY,
            tint=(1.0, 1.0, 1.0), metres=0.12)
    m.lathe([(0.0, -0.008), (0.0141, -0.008), (0.0141, 0.032), (0.0, 0.032)], (0, 0, 0), (-90, 0, 0), sides=20, material=GLOSSY,
            tint=(0.55, 0.06, 0.05), metres=0.1)
    m.lathe([(0.0, 0.055), (0.0128, 0.055), (0.0125, 0.065), (0.0095, 0.082), (0.0, 0.082)], (0, 0, 0), (-90, 0, 0), sides=20,
            tile=TEAL, tint=(1.0, 1.0, 1.0), metres=0.08)
    m.box((0.004, 0.06, 0.006), (0.0, -0.005, 0.0138), bevel=0.0015, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.1)
    m.export(floor=True)


def berries(key, colour, seed):
    m = Model(key, seed=seed, held=True, forward=False)
    stem_tint = (0.42, 0.45, 0.25)
    twig = [(0.0, -0.035, 0.012), (0.0, -0.005, 0.016), (0.004, 0.025, 0.014), (0.006, 0.05, 0.01)]
    m.tube(twig, 0.0018, sides=5, tile=TIMBER, tint=stem_tint, metres=0.1)
    centres = []
    tries = 0
    while len(centres) < 13 and tries < 300:
        tries += 1
        r = m.rng.uniform(0.0075, 0.0105)
        p = Vector((m.rng.uniform(-0.03, 0.03), m.rng.uniform(-0.035, 0.04), 0.0))
        if any((p.xy - c.xy).length < (r + cr) * 0.95 for c, cr in centres):
            continue
        p.z = r
        centres.append((p, r))
    # A second layer resting in the gaps.
    for _ in range(5):
        a, b = m.rng.sample(centres[:10], 2)
        if (a[0] - b[0]).length < 0.03:
            p = (a[0] + b[0]) / 2
            p.z = max(a[0].z, b[0].z) + 0.011
            centres.append((p, 0.009))
    for p, r in centres:
        tone = m.rng.uniform(0.75, 1.1)
        m.blob((2 * r, 2 * r, 2 * r * 0.92), p, (0, 0, m.rng.uniform(0, 360)), subdivisions=2, rough=0.04, material=GLOSSY,
               tint=tuple(c * tone for c in colour), metres=0.1, sharp=0)
        m.cylinder(0.0009, 0.006, p + Vector((0, 0, r * 0.92)), (m.rng.uniform(-20, 20), m.rng.uniform(-20, 20), 0), sides=4,
                   tile=TIMBER, tint=stem_tint, metres=0.05)
    m.export(floor=True)


def tuber(key, roasted, seed):
    """A long, knobbly root with a purple-brown skin; roasted, it is darker and charred in patches."""
    m = Model(key, seed=seed, held=True, forward=False)
    skin = (0.3, 0.15, 0.1) if roasted else (0.62, 0.32, 0.26)

    def marks(co, tint):
        n = noise.noise(co * 55 + Vector((3.1, 1.7, 0.4)), noise_basis="PERLIN_ORIGINAL")
        if roasted:
            return [t * (0.3 if n > 0.2 else 1.0) for t in tint]
        # Pale, earthy blotches on the raw skin.
        return [t * (1.25 if n > 0.35 else 1.0) for t in tint]

    body = m.blob((0.16, 0.06, 0.056), (0, 0, 0.03), (0, 0, 8), subdivisions=4, rough=0.06, freq=3.5, tile=LEATHER,
                  tint=skin, metres=0.12, vary=0.15, vary_scale=40, tint_fn=marks, flatten=-0.75, sharp=0)
    for v in body.data.vertices:
        t = v.co.x / 0.08
        # Tapered at both ends, fuller toward one, a little bent.
        k = max(0.0, 1 - abs(t) ** 3.2) ** 0.6 * (1 + 0.18 * t)
        v.co.y *= max(0.08, k)
        v.co.z = (v.co.z - 0.03) * max(0.08, k) + 0.03 + 0.012 * (1 - t * t) * 0 + 0.008 * t * t
        # Eyes: small dimples.
        e = noise.voronoi(v.co * 70, distance_metric="DISTANCE")[0][0]
        if e < 0.12:
            v.co.y *= 0.97
            v.co.z = 0.03 + (v.co.z - 0.03) * 0.97
    if not roasted:
        for i in range(7):
            x = m.rng.uniform(-0.065, 0.06)
            a = m.rng.uniform(0, 2 * math.pi)
            start = Vector((x, math.cos(a) * 0.024, 0.03 + math.sin(a) * 0.022))
            end = start + Vector((m.rng.uniform(-0.008, 0.008), math.cos(a) * 0.012, math.sin(a) * 0.008 - 0.004))
            m.tube([start, (start + end) / 2 + Vector((0, 0, -0.003)), end], 0.0008, sides=4, tile=LEATHER, tint=(0.55, 0.42, 0.32), metres=0.05)
        m.cylinder(0.0055, 0.016, (0.085, 0.0, 0.036), (0, 70, 0), sides=8, tile=TIMBER, tint=(0.42, 0.46, 0.26), metres=0.05)
    else:
        # The roasted skin has split along the top, showing the soft yellow inside.
        m.blob((0.07, 0.016, 0.006), (0.005, 0.0, 0.0575), (0, 0, 8), subdivisions=2, rough=0.15, freq=4, tile=PARCHMENT,
               tint=(1.0, 0.78, 0.36), metres=0.06, sharp=0)
    m.export(floor=True)


def cartridge(m, at, rot, case=0.019, neck=None, r=0.0048, bullet=0.011):
    """A round standing on local Z: brass case (bottlenecked when `neck` is given) and a copper bullet."""
    if neck:
        prof = [(0.0, 0.0), (r * 1.02, 0.0), (r * 1.02, 0.0015), (r * 0.9, 0.0022), (r, 0.003), (r * 0.95, case * 0.72),
                (neck, case * 0.82), (neck, case), (0.0, case)]
    else:
        prof = [(0.0, 0.0), (r * 1.02, 0.0), (r * 1.02, 0.0012), (r * 0.9, 0.0018), (r, 0.0025), (r, case), (0.0, case)]
    bm_rot = rot
    m.lathe(prof, at, bm_rot, sides=14, tile=STEEL, tint=BRASS, metres=0.05, sharp=30)
    rb = neck or r * 0.94
    tip = [(rb, case - 0.001), (rb, case + bullet * 0.35), (rb * 0.8, case + bullet * 0.75), (0.0, case + bullet)]
    m.lathe(tip, at, bm_rot, sides=14, tile=STEEL, tint=COPPER, metres=0.05, sharp=60)


def pistol_ammo(seed=65):
    m = Model("pistol-ammo", seed=seed)
    w, d, h = 0.085, 0.056, 0.036
    card = (0.86, 0.68, 0.46)
    m.box((w, d, h), (0, 0, h / 2), bevel=0.0012, tile=PARCHMENT, tint=card, metres=0.15)
    # The lid folded back, and a printed band.
    m.box((w, 0.0016, d * 0.9), (0, d / 2 + 0.006, h + d * 0.42), (-12, 0, 0), bevel=0.0006, tile=PARCHMENT, tint=card, metres=0.15)
    m.box((w * 1.002, d * 1.002, 0.012), (0, 0, h * 0.45), bevel=0.0006, tile=IVORY, tint=(0.78, 0.2, 0.16), metres=0.1)
    for i in range(4):
        for j in range(3):
            cartridge(m, (-0.03 + i * 0.02, -0.016 + j * 0.016, h - 0.012), (0, 0, 0))
    cartridge(m, (0.012, -0.05, 0.0048), (90, 0, 70))
    cartridge(m, (-0.022, -0.046, 0.0048), (90, 0, 115))
    m.export()


def rifle_ammo(seed=67):
    m = Model("rifle-ammo", seed=seed)
    # An olive tin with a hinged lid standing open, rounds lying in a row on top of a cloth.
    w, d, h = 0.11, 0.06, 0.05
    olive = (0.72, 0.78, 0.52)
    m.box((w, d, h), (0, 0, h / 2), bevel=0.003, tile=TEAL, tint=olive, metres=0.2)
    m.box((w + 0.004, d + 0.004, 0.006), (0, 0, h - 0.004), bevel=0.0015, tile=TEAL, tint=(0.6, 0.66, 0.44), metres=0.2)
    m.box((w + 0.004, 0.004, d * 0.95), (0, d / 2 + 0.006, h + d * 0.46), (-8, 0, 0), bevel=0.0015, tile=TEAL, tint=olive, metres=0.2)
    m.box((0.03, 0.008, 0.006), (0, -d / 2 - 0.003, h * 0.75), bevel=0.0015, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.05)
    for i in range(5):
        cartridge(m, (-0.04 + i * 0.02, -0.002, h + 0.0045), (-90, 0, 0), case=0.039, neck=0.0041, r=0.0056, bullet=0.017)
    cartridge(m, (0.03, -0.062, 0.0056), (90, 0, 75), case=0.039, neck=0.0041, r=0.0056, bullet=0.017)
    m.export()


def key_lock(seed=69):
    m = Model("key-lock", seed=seed)
    # Mounted on a door: the back plate at the origin, the face toward -Y (Godot +Z).
    m.box((0.075, 0.014, 0.11), (0, -0.007, 0), bevel=0.003, tile=RUST, tint=(0.85, 0.83, 0.8), metres=0.15)
    m.box((0.06, 0.012, 0.085), (0, -0.019, 0), bevel=0.003, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.12)
    m.cylinder(0.014, 0.004, (0, -0.026, 0.012), (90, 0, 0), sides=20, bevel=0.001, tile=STEEL, tint=(1.0, 0.95, 0.8), metres=0.05)
    m.box((0.0035, 0.004, 0.009), (0, -0.0285, 0.008), bevel=0.0, tile=RUBBER, tint=(0.2, 0.2, 0.2), metres=0.02)
    m.cylinder(0.003, 0.004, (0, -0.0285, 0.014), (90, 0, 0), sides=10, tile=RUBBER, tint=(0.2, 0.2, 0.2), metres=0.02)
    m.box((0.05, 0.01, 0.014), (0.0, -0.02, -0.03), bevel=0.002, tile=STEEL, tint=(0.95, 0.95, 0.95), metres=0.05)
    for x in (-0.03, 0.03):
        for z in (-0.047, 0.047):
            m.cylinder(0.0035, 0.004, (x, -0.0155, z), (90, 0, 0), sides=8, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.02)
    m.socket("mount", (0, 0, 0))
    # Held while being placed: the right palm at the middle, +X out of the face.
    m.socket("grip_r", (0, -0.015, 0), (0, 0, -90))
    m.export(floor=False)


ALL = {"bandage": bandage, "medical-injector": injector,
       "red-berries": lambda: berries("red-berries", (0.62, 0.05, 0.06), 71),
       "blue-berries": lambda: berries("blue-berries", (0.13, 0.16, 0.42), 73),
       "wild-tuber": lambda: tuber("wild-tuber", False, 75), "roasted-tuber": lambda: tuber("roasted-tuber", True, 77),
       "pistol-ammo": pistol_ammo, "rifle-ammo": rifle_ammo, "key-lock": key_lock}

for key in script_args() or list(ALL):
    ALL[key]()
