"""Small supplies: bandage, medical injector, berries, tubers, ammunition boxes, the key lock and its key.

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
    """A knobbly, earthy root: a few lumps grown together along a crooked line, side knobs, eyes, rootlets and soil;
    roasted, it is dark and charred, split open to the pale flesh."""
    m = Model(key, seed=seed, held=True, forward=False)
    skin = (0.27, 0.17, 0.1) if roasted else (0.5, 0.37, 0.25)

    def earth(co, tint):
        n = noise.noise(co * 45 + Vector((3.1, 1.7, 0.4)), noise_basis="PERLIN_ORIGINAL")
        d = noise.noise(co * 12 + Vector((7.3, 0.2, 5.1)), noise_basis="PERLIN_ORIGINAL")
        if roasted:
            return [t * (0.32 if n > 0.15 else 1.0) for t in tint]
        # Darker soil caked in patches, paler skin where it rubbed clean.
        k = 0.62 if d > 0.25 else (1.18 if n > 0.4 else 1.0)
        return [t * k for t in tint]

    lumps = [((-0.055, 0.0, 0.03), 0.062, 0.055), ((-0.01, 0.006, 0.033), 0.072, 0.064), ((0.04, -0.004, 0.03), 0.058, 0.052),
             ((0.075, 0.01, 0.025), 0.038, 0.034), ((-0.02, 0.036, 0.03), 0.034, 0.03), ((0.022, -0.032, 0.026), 0.03, 0.028),
             ((-0.07, -0.026, 0.022), 0.026, 0.024)]
    for i, (at, length, width) in enumerate(lumps):
        m.blob((length, width, width * 0.9), at, (m.rng.uniform(-15, 15), m.rng.uniform(-10, 10), m.rng.uniform(0, 360)),
               subdivisions=3, rough=0.14, freq=3.6, facets=0.12, material=ROCK, tint=skin, metres=0.08, vary=0.12,
               vary_scale=40, tint_fn=earth, flatten=-0.75 if i < 4 else None, sharp=0)
    if not roasted:
        for _ in range(9):
            at, length, width = m.rng.choice(lumps[:4])
            a = m.rng.uniform(0, 2 * math.pi)
            start = Vector(at) + Vector((m.rng.uniform(-0.3, 0.3) * length, math.cos(a) * width * 0.45, math.sin(a) * width * 0.4))
            end = start + Vector((m.rng.uniform(-0.01, 0.01), math.cos(a) * 0.018, math.sin(a) * 0.012 - 0.008))
            m.tube([start, (start + end) / 2 + Vector((0, 0, -0.003)), end], 0.0009, sides=4, tile=LEATHER, tint=(0.5, 0.4, 0.3), metres=0.05)
        m.cylinder(0.006, 0.018, (-0.085, 0.004, 0.04), (0, -70, 0), sides=8, tile=TIMBER, tint=(0.4, 0.4, 0.24), metres=0.05)
    else:
        # The skin split along the top, showing the soft, pale inside.
        m.blob((0.06, 0.014, 0.008), (-0.008, 0.006, 0.062), (0, 0, 10), subdivisions=2, rough=0.15, freq=4, tile=PARCHMENT,
               tint=(1.0, 0.82, 0.48), metres=0.06, sharp=0)
    m.export(floor=True)


def cartridge(m, at, rot, case=0.019, neck=None, r=0.0048, bullet=0.011, tip=None):
    """A round standing on local Z: brass case (bottlenecked when `neck` is given) and a copper bullet, its tip painted
    `tip` when given (the ammunition's type)."""
    if neck:
        prof = [(0.0, 0.0), (r * 1.02, 0.0), (r * 1.02, 0.0015), (r * 0.9, 0.0022), (r, 0.003), (r * 0.95, case * 0.72),
                (neck, case * 0.82), (neck, case), (0.0, case)]
    else:
        prof = [(0.0, 0.0), (r * 1.02, 0.0), (r * 1.02, 0.0012), (r * 0.9, 0.0018), (r, 0.0025), (r, case), (0.0, case)]
    m.lathe(prof, at, rot, sides=14, tile=STEEL, tint=BRASS, metres=0.05, sharp=30)
    rb = neck or r * 0.94
    if tip is None:
        m.lathe([(rb, case - 0.001), (rb, case + bullet * 0.35), (rb * 0.8, case + bullet * 0.75), (0.0, case + bullet)], at, rot,
                sides=14, tile=STEEL, tint=COPPER, metres=0.05, sharp=60)
    else:
        m.lathe([(0.0, case - 0.001), (rb, case - 0.001), (rb, case + bullet * 0.35), (rb * 0.88, case + bullet * 0.6),
                 (0.0, case + bullet * 0.6)], at, rot, sides=14, tile=STEEL, tint=COPPER, metres=0.05, sharp=60)
        m.lathe([(0.0, case + bullet * 0.6), (rb * 0.88, case + bullet * 0.6), (rb * 0.8, case + bullet * 0.75),
                 (0.0, case + bullet)], at, rot, sides=14, tile=IVORY, tint=tip, metres=0.05, sharp=60)


RED = (0.78, 0.2, 0.16)
BLUE = (0.16, 0.34, 0.78)
ORANGE = (0.95, 0.42, 0.08)


def pistol_ammo(key="pistol-ammo", band=RED, tip=None, seed=65):
    m = Model(key, seed=seed)
    w, d, h = 0.085, 0.056, 0.036
    card = (0.86, 0.68, 0.46)
    m.box((w, d, h), (0, 0, h / 2), bevel=0.0012, tile=PARCHMENT, tint=card, metres=0.15)
    # The lid folded back, and a printed band in the ammunition's colour.
    m.box((w, 0.0016, d * 0.9), (0, d / 2 + 0.006, h + d * 0.42), (-12, 0, 0), bevel=0.0006, tile=PARCHMENT, tint=card, metres=0.15)
    m.box((w * 1.002, d * 1.002, 0.012), (0, 0, h * 0.45), bevel=0.0006, tile=IVORY, tint=band, metres=0.1)
    m.box((w * 0.98, 0.0014, d * 0.2), (0, d / 2 + 0.0055, h + d * 0.62), (-12, 0, 0), bevel=0.0, tile=IVORY, tint=band, metres=0.1)
    for i in range(4):
        for j in range(3):
            cartridge(m, (-0.03 + i * 0.02, -0.016 + j * 0.016, h - 0.012), (0, 0, 0), tip=tip)
    cartridge(m, (0.012, -0.05, 0.0048), (90, 0, 70), tip=tip)
    cartridge(m, (-0.022, -0.046, 0.0048), (90, 0, 115), tip=tip)
    m.export()


def hazard_stripes(m, w, d, z, height, stripe=0.006, proud=0.0016):
    """Black diagonal stripes over a band of `height` around a w x d box at height z (the explosive rounds' band)."""
    black = (0.07, 0.07, 0.07)
    for sign in (-1, 1):
        # Front and back faces: parallelograms leaning 45 degrees, kept inside the face.
        x = -w / 2 + 0.002
        while x + stripe + height < w / 2 - 0.001:
            outline = [(x, z - height / 2), (x + stripe, z - height / 2), (x + stripe + height, z + height / 2),
                       (x + height, z + height / 2)]
            m.extrude(outline, proud, (0, sign * (d / 2 + proud / 2 + 0.0008), 0), bevel=0.0, tile=RUBBER, tint=black,
                      metres=0.05, sharp=0)
            x += stripe * 2.2
        # The sides: the same outline across Y, turned to face X.
        y = -d / 2 + 0.002
        while y + stripe + height < d / 2 - 0.001:
            outline = [(y, z - height / 2), (y + stripe, z - height / 2), (y + stripe + height, z + height / 2),
                       (y + height, z + height / 2)]
            o = m.extrude(outline, proud, (0, 0, 0), bevel=0.0, tile=RUBBER, tint=black, metres=0.05, sharp=0)
            o.data.transform(Matrix.Translation((sign * (w / 2 + proud / 2 + 0.0008), 0, 0)) @ Matrix.Rotation(math.radians(90), 4, "Z"))
            o.data.update()
            y += stripe * 2.2


def rifle_ammo(key="rifle-ammo", band=None, tip=None, seed=67, hazard=False):
    m = Model(key, seed=seed)
    # An olive tin with a hinged lid standing open, rounds lying in a row on top; special rounds have a painted band.
    w, d, h = 0.11, 0.06, 0.05
    olive = (0.72, 0.78, 0.52)
    m.box((w, d, h), (0, 0, h / 2), bevel=0.003, tile=TEAL, tint=olive, metres=0.2)
    m.box((w + 0.004, d + 0.004, 0.006), (0, 0, h - 0.004), bevel=0.0015, tile=TEAL, tint=(0.6, 0.66, 0.44), metres=0.2)
    m.box((w + 0.004, 0.004, d * 0.95), (0, d / 2 + 0.006, h + d * 0.46), (-8, 0, 0), bevel=0.0015, tile=TEAL, tint=olive, metres=0.2)
    m.box((0.03, 0.008, 0.006), (0, -d / 2 - 0.003, h * 0.75), bevel=0.0015, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.05)
    if band:
        m.box((w + 0.002, d + 0.002, 0.013), (0, 0, h * 0.42), bevel=0.001, tile=IVORY, tint=band, metres=0.1)
        m.box((w * 0.9, 0.0045, 0.012), (0, d / 2 + 0.0085, h + d * 0.5), (-8, 0, 0), bevel=0.0005, tile=IVORY, tint=band, metres=0.1)
    if hazard:
        # The explosive rounds' band: black stripes over the yellow, on the box and the lid's label.
        hazard_stripes(m, w + 0.002, d + 0.002, h * 0.42, 0.013)
    for i in range(5):
        cartridge(m, (-0.04 + i * 0.02, -0.002, h + 0.0045), (-90, 0, 0), case=0.039, neck=0.0041, r=0.0056, bullet=0.017, tip=tip)
    cartridge(m, (0.03, -0.062, 0.0056), (90, 0, 75), case=0.039, neck=0.0041, r=0.0056, bullet=0.017, tip=tip)
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


def door_key(seed=95):
    """The key a key lock makes (`door-key`): a cut steel key with a round bow, a shoulder and three uneven teeth, on a
    split ring with a stamped brass tag, as a scrap-age locksmith would hand it over. Distinct from the key lock it
    opens. It lies flat, the blade along +X, the tag toward -X."""
    m = Model("door-key", seed=seed)
    t = 0.0026  # the blank's thickness
    ring = [(-0.004 + 0.012 * math.cos(a), 0.012 * math.sin(a), t / 2)
            for a in (2 * math.pi * k / 28 for k in range(29))]
    m.tube(ring, 0.0028, sides=8, cap=False, tile=STEEL, tint=(0.72, 0.72, 0.7), metres=0.03)
    m.box((0.011, 0.012, t), (0.010, 0, t / 2), bevel=0.0008, tile=STEEL, tint=(0.7, 0.7, 0.68), metres=0.03)
    m.box((0.003, 0.016, t + 0.001), (0.0165, 0, t / 2), bevel=0.0006, tile=STEEL, tint=(0.66, 0.66, 0.64), metres=0.03)
    m.box((0.036, 0.0062, t), (0.0355, 0.0018, t / 2), bevel=0.0006, tile=STEEL, tint=(0.74, 0.74, 0.72), metres=0.04)
    # Three teeth of different depths along the blade's lower edge, and a groove down its face.
    for x, depth in ((0.030, 0.0042), (0.039, 0.0026), (0.047, 0.0050)):
        m.box((0.0046, depth, t), (x, -0.0013 - depth / 2, t / 2), bevel=0.0004, tile=STEEL, tint=(0.74, 0.74, 0.72),
              metres=0.02)
    m.box((0.030, 0.0012, 0.0006), (0.037, 0.0022, t + 0.0003), bevel=0.0, tile=GUNMETAL, tint=(0.6, 0.6, 0.6),
          metres=0.02)
    # A split ring through the bow and a stamped brass tag on it.
    split = [(-0.019 + 0.008 * math.cos(a), 0.008 * math.sin(a), 0.0012 + 0.0006 * math.sin(a * 2))
             for a in (2 * math.pi * k / 24 for k in range(25))]
    m.tube(split, 0.0011, sides=6, cap=False, tile=STEEL, tint=(0.82, 0.82, 0.8), metres=0.02)
    tag = [(-0.026, -0.008), (-0.050, -0.010), (-0.054, -0.005), (-0.054, 0.005), (-0.050, 0.010), (-0.026, 0.008)]
    m.extrude(tag, 0.0012, (0, 0, 0.0016), (90, 0, 0), bevel=0.0004, tile=STEEL, tint=BRASS, metres=0.03)
    m.cylinder(0.0022, 0.0016, (-0.029, 0, 0.0018), sides=12, tile=RUBBER, tint=(0.2, 0.2, 0.2), metres=0.01)
    for x in (-0.037, -0.042, -0.047):
        m.box((0.0025, 0.0012, 0.0004), (x, 0, 0.0024), bevel=0.0, tile=GUNMETAL, tint=(0.35, 0.3, 0.22), metres=0.01)
    m.export()


ALL = {"bandage": bandage, "medical-injector": injector,
       "red-berries": lambda: berries("red-berries", (0.62, 0.05, 0.06), 71),
       "blue-berries": lambda: berries("blue-berries", (0.13, 0.16, 0.42), 73),
       "wild-tuber": lambda: tuber("wild-tuber", False, 75), "roasted-tuber": lambda: tuber("roasted-tuber", True, 77),
       "pistol-ammo": lambda: pistol_ammo(),
       "pistol-ammo-hv": lambda: pistol_ammo("pistol-ammo-hv", band=BLUE, tip=BLUE, seed=79),
       "rifle-ammo": lambda: rifle_ammo(),
       "rifle-ammo-hv": lambda: rifle_ammo("rifle-ammo-hv", band=BLUE, tip=BLUE, seed=81),
       "rifle-ammo-incendiary": lambda: rifle_ammo("rifle-ammo-incendiary", band=ORANGE, tip=(0.85, 0.12, 0.06), seed=83),
       "pistol-ammo-incendiary": lambda: pistol_ammo("pistol-ammo-incendiary", band=ORANGE, tip=(0.85, 0.12, 0.06), seed=85),
       # G5: explosive rifle rounds, a yellow band striped black, yellow tips.
       "rifle-ammo-explosive": lambda: rifle_ammo("rifle-ammo-explosive", band=(0.95, 0.74, 0.08), tip=(0.95, 0.74, 0.08),
                                                  seed=87, hazard=True),
       "key-lock": key_lock, "door-key": door_key}

for key in script_args() or list(ALL):
    ALL[key]()
