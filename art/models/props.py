"""World props for G7: two roadside loot barrels, a basic loot crate, a military crate and a recycler.

World objects: front toward -Y (Godot +Z), footprint centred on the origin at ground level. Crate lids are parts
hinged along their back edge, so they can open.

    blender -b --factory-startup --python art/models/props.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from mathutils import Vector, noise  # noqa: E402

from hollow import WORLD, script_args  # noqa: E402
from hollow.build import (CANVAS, GLOW, GUNMETAL, IVORY, OCHRE, PARCHMENT, RUBBER, RUST, STEEL, TEAL, TIMBER,  # noqa: E402
                          Model)

DARK = (0.3, 0.3, 0.32)


def drum(m, tile, tint, dents=0.0, band=None):
    """A 200-litre steel drum: rolled hoops, chimes at both ends, a lid with two bungs."""
    r, h = 0.29, 0.88
    profile = [(0.0, 0.004), (r - 0.012, 0.004), (r + 0.004, 0.012), (r + 0.006, 0.03), (r, 0.04)]
    for z in (h / 3, 2 * h / 3):
        profile += [(r, z - 0.03), (r + 0.012, z - 0.012), (r + 0.012, z + 0.012), (r, z + 0.03)]
    profile += [(r, h - 0.04), (r + 0.006, h - 0.03), (r + 0.004, h - 0.012), (r - 0.012, h - 0.004), (0.0, h - 0.004)]
    seed = Vector((m.rng.random() * 9, m.rng.random() * 9, m.rng.random() * 9))

    def wear(co, t):
        # Rust breaking through the paint in patches, darker toward the ground.
        n = noise.noise(co * 6 + seed, noise_basis="PERLIN_ORIGINAL")
        k = 0.55 if n > 0.32 else 1.0
        g = 0.75 + 0.25 * min(1.0, co.z / 0.3)
        c = [x * k * g for x in t]
        if n > 0.32:
            c = [c[0] * 1.35, c[1] * 0.85, c[2] * 0.6]
        if band and abs(co.z - 0.5 * h) < 0.07:
            c = [b * g for b in band]
        return c

    body = m.lathe(profile, sides=32, tile=tile, tint=tint, metres=0.9, vary=0.08, sharp=40, tint_fn=wear)
    if dents:
        for v in body.data.vertices:
            n = noise.noise(v.co * 4 + seed, noise_basis="PERLIN_ORIGINAL")
            rad = math.hypot(v.co.x, v.co.y)
            if rad > 0.1 and 0.06 < v.co.z < h - 0.06 and n > 0.2:
                k = 1 - dents * (n - 0.2)
                v.co.x *= k
                v.co.y *= k
    for x, y, rr in ((0.16, 0.05, 0.028), (-0.17, -0.06, 0.018)):
        m.cylinder(rr, 0.016, (x, y, h - 0.002), sides=14, bevel=0.002, tile=STEEL, tint=(0.75, 0.75, 0.75), metres=0.05)


def loot_barrel(key, tile, tint, dents, band, seed):
    m = Model(key, WORLD, seed=seed)
    drum(m, tile, tint, dents, band)
    m.socket("use", (0, -0.6, 0.6))
    m.export()


def crate_body(m, w, d, h, tile, tint, slats=True):
    """Planked walls on a frame of corner posts, with a floor; returns nothing."""
    for side, (cx, cy, sx, sy) in enumerate(((0, -d / 2, w, 0.024), (0, d / 2, w, 0.024), (-w / 2, 0, 0.024, d), (w / 2, 0, 0.024, d))):
        n = 4 if slats else 1
        for k in range(n):
            z = 0.025 + (h - 0.03) / n * (k + 0.5)
            kk = m.rng.uniform(0.85, 1.05)
            m.box((sx - (0.05 if sx > 0.1 else 0), sy - (0.05 if sy > 0.1 else 0), (h - 0.03) / n - 0.012), (cx, cy, z),
                  bevel=0.004, tile=tile, tint=tuple(c * kk for c in tint), metres=1.0, vary=0.06)
    for x in (-w / 2 + 0.025, w / 2 - 0.025):
        for y in (-d / 2 + 0.025, d / 2 - 0.025):
            m.box((0.05, 0.05, h), (x, y, h / 2), bevel=0.005, tile=tile, tint=tuple(c * 0.8 for c in tint), metres=0.8)
    m.box((w, d, 0.022), (0, 0, 0.012), bevel=0.003, tile=tile, tint=tuple(c * 0.75 for c in tint), metres=1.0)


def loot_crate(seed=401):
    m = Model("loot-crate", WORLD, seed=seed)
    w, d, h = 0.95, 0.6, 0.5
    crate_body(m, w, d, h, TIMBER, (0.98, 0.9, 0.78))
    for x in (-w / 2 - 0.012, w / 2 + 0.012):
        m.tube([(x, -0.1, 0.38), (x * 1.04, -0.07, 0.32), (x * 1.04, 0.07, 0.32), (x, 0.1, 0.38)], 0.01, sides=8, tile=CANVAS,
               tint=(0.82, 0.74, 0.58), metres=0.2)
    # A stencilled board on the front, bands of strap iron.
    m.box((0.36, 0.006, 0.12), (0, -d / 2 - 0.014, h * 0.5), bevel=0.002, tile=PARCHMENT, tint=(0.95, 0.9, 0.8), metres=0.4)
    for x in (-0.3, 0.3):
        m.box((0.04, d + 0.012, 0.008), (x, 0, h + 0.004), bevel=0.002, tile=RUST, tint=(0.75, 0.72, 0.7), metres=0.4)
    m.part("Lid", (0, d / 2, h + 0.01))
    for k in range(4):
        y = -d / 2 + d / 4 * (k + 0.5)
        kk = m.rng.uniform(0.85, 1.05)
        m.box((w + 0.02, d / 4 - 0.008, 0.026), (0, y, h + 0.023), bevel=0.004, tile=TIMBER, tint=(0.95 * kk, 0.86 * kk, 0.74 * kk),
              metres=1.0, vary=0.05)
    for x in (-w / 2 + 0.08, w / 2 - 0.08):
        m.box((0.06, d + 0.02, 0.024), (x, 0, h + 0.048), bevel=0.004, tile=TIMBER, tint=(0.72, 0.64, 0.54), metres=0.6)
    m.part("Body")
    m.socket("use", (0, -0.8, 0.45))
    m.export(hulls=[((0, 0, (h + 0.06) / 2), (w + 0.04, d + 0.04, h + 0.06), (0, 0, 0))])


def military_crate(seed=403):
    m = Model("military-crate", WORLD, seed=seed)
    w, d, h = 1.2, 0.62, 0.5
    olive = (0.62, 0.68, 0.46)
    m.box((w, d, h), (0, 0, h / 2), bevel=0.012, segments=2, tile=TEAL, tint=olive, metres=1.2, vary=0.06)
    # Reinforced corners and skids, two latches and side handles, stencil bands.
    for x in (-w / 2 + 0.03, w / 2 - 0.03):
        for y in (-d / 2 + 0.03, d / 2 - 0.03):
            m.box((0.07, 0.07, h + 0.01), (x, y, h / 2), bevel=0.008, tile=GUNMETAL, tint=(0.62, 0.62, 0.6), metres=0.6)
    for y in (-d / 2 + 0.1, d / 2 - 0.1):
        m.box((w - 0.1, 0.06, 0.04), (0, y, 0.02), bevel=0.006, tile=GUNMETAL, tint=DARK, metres=1.0)
    for x in (-0.35, 0.35):
        m.box((0.07, 0.03, 0.09), (x, -d / 2 - 0.012, h - 0.05), bevel=0.006, tile=STEEL, tint=(0.7, 0.7, 0.72), metres=0.1)
        m.box((0.06, 0.012, 0.03), (x, -d / 2 - 0.03, h - 0.035), bevel=0.003, tile=STEEL, tint=(0.8, 0.8, 0.82), metres=0.05)
    for x in (-w / 2 - 0.014, w / 2 + 0.014):
        m.tube([(x, -0.1, 0.3), (x * 1.04, -0.08, 0.26), (x * 1.04, 0.08, 0.26), (x, 0.1, 0.3)], 0.011, sides=8, tile=GUNMETAL,
               tint=(0.5, 0.5, 0.52), metres=0.2)
    m.box((w * 0.62, 0.006, 0.07), (0, -d / 2 - 0.004, h * 0.48), bevel=0.0, tile=IVORY, tint=(0.95, 0.9, 0.62), metres=0.4)
    m.box((w * 0.62, 0.006, 0.02), (0, -d / 2 - 0.0045, h * 0.34), bevel=0.0, tile=IVORY, tint=(0.95, 0.9, 0.62), metres=0.4)
    m.part("Lid", (0, d / 2, h + 0.01))
    m.box((w + 0.01, d + 0.01, 0.07), (0, 0, h + 0.035), bevel=0.012, segments=2, tile=TEAL, tint=(0.58, 0.64, 0.43), metres=1.2)
    m.part("Body")
    m.socket("use", (0, -0.85, 0.5))
    m.export(hulls=[((0, 0, (h + 0.07) / 2), (w + 0.05, d + 0.05, h + 0.07), (0, 0, 0))])


def recycler(seed=405):
    m = Model("recycler", WORLD, seed=seed)
    w, d, h = 1.5, 1.0, 1.25
    green = (0.72, 0.86, 0.74)
    # Housing on feet, ribbed sides, a hopper on top, a chute and tray at the front, a motor on the side.
    for x in (-w / 2 + 0.08, w / 2 - 0.08):
        for y in (-d / 2 + 0.08, d / 2 - 0.08):
            m.box((0.12, 0.12, 0.1), (x, y, 0.05), bevel=0.01, tile=GUNMETAL, tint=DARK, metres=0.3)
    m.box((w, d, h - 0.1), (0, 0, 0.1 + (h - 0.1) / 2), bevel=0.02, segments=2, tile=TEAL, tint=green, metres=1.5, vary=0.05)
    for k in range(5):
        x = -w / 2 + 0.15 + k * (w - 0.3) / 4
        m.box((0.05, d + 0.03, 0.06), (x, 0, h - 0.06), bevel=0.008, tile=TEAL, tint=(0.6, 0.72, 0.62), metres=0.5)
        m.box((0.05, 0.03, h - 0.25), (x, -d / 2 - 0.012, 0.1 + (h - 0.25) / 2), bevel=0.008, tile=TEAL, tint=(0.6, 0.72, 0.62), metres=0.5)
    hopper = [(-0.42, 0.0), (0.42, 0.0), (0.56, 0.36), (-0.56, 0.36)]
    for y in (-0.36, 0.36):
        m.extrude(hopper, 0.03, (0, y, h), bevel=0.004, tile=GUNMETAL, tint=(0.7, 0.7, 0.72), metres=0.8)
    for sx in (-1, 1):
        m.box((0.03, 0.75, 0.38), (sx * 0.49, 0, h + 0.18), (0, sx * -21, 0), bevel=0.004, tile=GUNMETAL, tint=(0.7, 0.7, 0.72), metres=0.8)
    for k in range(6):
        x = -0.5 + k * 0.2
        m.box((0.1, 0.034, 0.03), (x, -0.375, h + 0.37), (0, 30, 0), bevel=0.002, tile=OCHRE if k % 2 == 0 else RUBBER,
              tint=(1.0, 0.95, 0.85) if k % 2 == 0 else (0.25, 0.25, 0.25), metres=0.2)
    m.box((0.7, 0.16, 0.06), (0, -d / 2 - 0.08, 0.32), (-12, 0, 0), bevel=0.006, tile=GUNMETAL, tint=(0.75, 0.75, 0.77), metres=0.6)
    for x in (-0.36, 0.36):
        m.box((0.02, 0.18, 0.1), (x, -d / 2 - 0.085, 0.35), bevel=0.003, tile=GUNMETAL, tint=(0.75, 0.75, 0.77), metres=0.3)
    m.box((0.62, 0.02, 0.26), (0, -d / 2 - 0.005, 0.5), bevel=0.004, tile=RUBBER, tint=(0.25, 0.25, 0.25), metres=0.6)
    fins = [(0.2, -0.24), (0.2, 0.24)]
    m.cylinder(0.2, 0.36, (w / 2 + 0.18, 0.1, 0.62), (0, 90, 0), sides=24, bevel=0.01, tile=GUNMETAL, tint=DARK, metres=0.6)
    for k in range(7):
        m.cylinder(0.215, 0.012, (w / 2 + 0.04 + k * 0.045, 0.1, 0.62), (0, 90, 0), sides=24, tile=GUNMETAL, tint=(0.4, 0.4, 0.42), metres=0.5)
    _ = fins
    # Control panel: two lamps and a big button, a lever.
    m.box((0.36, 0.06, 0.3), (-w / 2 + 0.3, -d / 2 - 0.03, 0.95), bevel=0.01, tile=GUNMETAL, tint=(0.62, 0.62, 0.64), metres=0.4)
    for x, tint in ((-w / 2 + 0.2, (1.0, 1.0, 1.0)), (-w / 2 + 0.3, (1.0, 1.0, 1.0))):
        m.cylinder(0.022, 0.03, (x, -d / 2 - 0.07, 1.04), (90, 0, 0), sides=14, tile=GLOW, tint=tint, metres=0.05)
    m.cylinder(0.04, 0.035, (-w / 2 + 0.4, -d / 2 - 0.072, 0.9), (90, 0, 0), sides=18, bevel=0.006, tile=IVORY, tint=(0.85, 0.15, 0.1), metres=0.08)
    m.tube([(-w / 2 + 0.2, -d / 2 - 0.06, 0.88), (-w / 2 + 0.2, -d / 2 - 0.16, 0.95)], 0.012, sides=8, tile=STEEL, tint=(0.85, 0.85, 0.85), metres=0.1)
    m.blob((0.05, 0.05, 0.05), (-w / 2 + 0.2, -d / 2 - 0.165, 0.955), subdivisions=2, rough=0.0, tile=RUBBER, tint=(0.2, 0.2, 0.2), metres=0.05)
    # Exhaust stack at the back.
    m.cylinder(0.06, 0.55, (w / 2 - 0.2, d / 2 - 0.15, h + 0.25), sides=16, tile=RUST, tint=(0.85, 0.82, 0.8), metres=0.5)
    m.cylinder(0.085, 0.04, (w / 2 - 0.2, d / 2 - 0.15, h + 0.54), sides=16, radius2=0.05, tile=GUNMETAL, tint=DARK, metres=0.2)
    m.socket("input", (0, 0, h + 0.4))
    m.socket("output", (0, -d / 2 - 0.12, 0.38))
    m.socket("use", (0, -1.2, 0.9))
    m.export(hulls=[((0, 0, h / 2), (w, d, h), (0, 0, 0)), ((0, 0, h + 0.2), (1.1, 0.8, 0.4), (0, 0, 0)),
                    ((w / 2 + 0.18, 0.1, 0.62), (0.36, 0.44, 0.44), (0, 0, 0))])


ALL = {
    "loot-barrel-1": lambda: loot_barrel("loot-barrel-1", TEAL, (0.62, 0.86, 1.0), 0.0, None, 407),
    "loot-barrel-2": lambda: loot_barrel("loot-barrel-2", OCHRE, (1.0, 0.95, 0.9), 0.06, (0.2, 0.2, 0.2), 409),
    "loot-crate": loot_crate,
    "military-crate": military_crate,
    "recycler": recycler,
}

for key in script_args() or list(ALL):
    ALL[key]()
