"""Hand tools and simple weapons: torch, metal hatchet and pickaxe, wooden and stone spears, machete, bow, arrow.

Authored pointing +Y with the right hand's grip at the origin, exported pointing +X (the character package's frame,
Godot +X forward, +Y up). Tools stand upright: the handle runs up +Z, the head at the top, the blade facing forward.

    blender -b --factory-startup --python art/models/tools.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from mathutils import Matrix, Vector  # noqa: E402

from hollow import script_args  # noqa: E402
from hollow.build import (BASALT, CANVAS, GUNMETAL, LEATHER, PARCHMENT, ROCK, RUST, STEEL, TIMBER,  # noqa: E402
                          Model)

WOOD = (0.95, 0.9, 0.84)
CHAR = (0.22, 0.2, 0.19)


def handle(m, z0, z1, r0, r1, bend=0.0, oval=0.8, tile=TIMBER, tint=WOOD, sides=12):
    """A wooden handle up the Z axis, tapering from r0 to r1, gently bent toward +Y, oval across X."""
    pts, radii = [], []
    for i in range(9):
        t = i / 8
        z = z0 + (z1 - z0) * t
        pts.append((0.0, bend * math.sin(t * math.pi), z))
        radii.append(r0 + (r1 - r0) * t)
    o = m.tube(pts, r0, sides=sides, radii=radii, tile=tile, tint=tint, metres=0.5, vary=0.06, sharp=0)
    for v in o.data.vertices:
        v.co.x *= oval
    return o


def wrap(m, axis_points, radius, turns, width=0.008, tile=LEATHER, tint=(0.85, 0.75, 0.62)):
    """A binding wound around a straight stretch of handle: a flat helix of `turns` turns."""
    a, b = Vector(axis_points[0]), Vector(axis_points[1])
    d = (b - a).normalized()
    ref = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
    u = d.cross(ref).normalized()
    w = d.cross(u)
    steps = int(turns * 16)
    pts = []
    for i in range(steps + 1):
        t = i / steps
        ang = t * turns * 2 * math.pi
        pts.append(a + (b - a) * t + (u * math.cos(ang) + w * math.sin(ang)) * radius)
    return m.tube(pts, width / 2, sides=6, tile=tile, tint=tint, metres=0.3, sharp=0)


def torch(seed=41):
    m = Model("torch", seed=seed, held=True)
    handle(m, -0.13, 0.4, 0.017, 0.015, bend=0.004)
    # A head of rags wound round the top, soaked and charred.
    head = [(0.016, 0.33), (0.03, 0.34), (0.037, 0.37), (0.04, 0.41), (0.036, 0.45), (0.022, 0.475), (0.0, 0.48)]

    def scorch(co, tint):
        k = max(0.0, min(1.0, (co.z - 0.35) / 0.12))
        return [t * (1 - 0.82 * k ** 0.7) for t in tint]

    m.lathe(head, sides=16, tile=CANVAS, tint=(0.48, 0.41, 0.33), metres=0.2, vary=0.3, vary_scale=40, sharp=0, tint_fn=scorch)
    for z in (0.345, 0.385, 0.43):
        wrap(m, [(0, 0, z - 0.008), (0, 0, z + 0.008)], 0.039 if z > 0.36 else 0.033, 1.2, 0.007, tile=CANVAS,
             tint=(0.36, 0.31, 0.25) if z < 0.4 else (0.16, 0.14, 0.12))
    wrap(m, [(0, 0, -0.06), (0, 0, 0.08)], 0.0175, 6, 0.007, tile=LEATHER)
    m.socket("flame", (0, 0, 0.48))
    m.export(floor=False)


def metal_hatchet(seed=43):
    m = Model("metal-hatchet", seed=seed, held=True)
    handle(m, -0.11, 0.43, 0.018, 0.016, bend=0.012)
    # Forged head: side profile (y forward, z up), thick at the eye, flaring to a curved edge.
    profile = [(-0.04, 0.36), (0.012, 0.355), (0.05, 0.335), (0.095, 0.315), (0.112, 0.34), (0.118, 0.385), (0.112, 0.43),
               (0.095, 0.452), (0.05, 0.43), (0.012, 0.418), (-0.04, 0.42)]
    head = m.extrude(profile, 0.026, bevel=0.0015, tile=RUST, tint=(0.82, 0.8, 0.78), metres=0.18, uv="box")
    # The outline was drawn in X-Z (x forward); a quarter turn puts it in Y-Z with the thickness across X.
    head.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    for v in head.data.vertices:
        # Thin toward the edge.
        t = max(0.0, min(1.0, (v.co.y - 0.015) / 0.1))
        v.co.x *= 1 - 0.88 * t
    head.data.update()
    m.recolour(head, lambda co, t: [c * (1 + 0.4 * max(0.0, (co.y - 0.085) / 0.035)) for c in t])
    m.cylinder(0.0045, 0.03, (0, 0.0, 0.43), sides=8, tile=STEEL, tint=(0.7, 0.7, 0.7), metres=0.1)
    wrap(m, [(0, 0, -0.07), (0, 0, 0.07)], 0.0185, 7, 0.008)
    m.socket("impact", (0, 0.118, 0.385))
    m.export(floor=False)


def metal_pickaxe(seed=45):
    m = Model("metal-pickaxe", seed=seed, held=True)
    handle(m, -0.13, 0.5, 0.019, 0.017, bend=0.01)
    pts, radii = [], []
    for i in range(15):
        t = i / 14 * 2 - 1  # -1 back spike .. 1 front pick
        y = t * 0.235
        z = 0.47 - 0.07 * t * t - 0.012 * t
        pts.append((0.0, y, z))
        radii.append(0.004 + 0.016 * (1 - abs(t)) ** 0.8)
    bar = m.tube(pts, 0.01, sides=6, radii=radii, tile=RUST, tint=(0.8, 0.78, 0.76), metres=0.25, sharp=40)
    for v in bar.data.vertices:
        v.co.x *= 0.7
    m.cylinder(0.026, 0.05, (0, 0, 0.47), sides=10, bevel=0.003, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.15)
    wrap(m, [(0, 0, -0.09), (0, 0, 0.06)], 0.0195, 7, 0.008)
    m.socket("impact", (0, 0.235, 0.41))
    m.export(floor=False)


def shaft(m, y0, y1, r=0.016, point=0.22, char=True):
    """A spear shaft along +Y from y0 (butt) to y1 (tip), whittled to a point over the last `point` metres."""
    pts, radii = [], []
    n = 14
    for i in range(n + 1):
        t = i / n
        y = y0 + (y1 - y0) * t
        pts.append((0.0, y, 0.004 * math.sin(t * 3)))
        taper = max(0.0, (y - (y1 - point)) / point)
        radii.append(max(0.0012, r * (1 - taper ** 1.2)))
    o = m.tube(pts, r, sides=10, radii=radii, tile=TIMBER, tint=WOOD, metres=1.2, vary=0.05, sharp=0)
    if char:
        m.recolour(o, lambda co, t: [c * (1 - 0.7 * max(0.0, min(1.0, (co.y - (y1 - point * 0.6)) / (point * 0.6)))) for c in t])
    return o


def wooden_spear(seed=47):
    m = Model("wooden-spear", seed=seed, held=True)
    shaft(m, -0.78, 1.22)
    wrap(m, [(0, -0.09, 0.0), (0, 0.13, 0.0)], 0.0165, 9, 0.008, tile=CANVAS, tint=(0.75, 0.68, 0.56))
    m.socket("tip", (0, 1.22, 0.0))
    m.export(floor=False)


def stone_spear(seed=49):
    m = Model("stone-spear", seed=seed, held=True)
    shaft(m, -0.78, 1.1, point=0.05, char=False)
    # A knapped stone point: a leaf shape, faceted, lashed into the split tip.
    leaf = []
    for i in range(13):
        a = math.pi * i / 12
        leaf.append((math.sin(a) * 0.036 * (1 - 0.3 * math.cos(a)), 1.06 + (1 - math.cos(a)) * 0.13))
    outline = [(x, y) for x, y in leaf] + [(-x, y) for x, y in reversed(leaf[1:-1])]
    point = m.extrude([(x, y) for x, y in outline], 0.016, tile=BASALT, tint=(0.55, 0.53, 0.5), metres=0.12, uv="box", sharp=20)
    point.data.transform(Matrix.Rotation(math.radians(-90), 4, "X"))
    for v in point.data.vertices:
        v.co.z *= 0.5 + 0.5 * abs(v.co.x) / 0.03
        v.co.z += 0.0015 * math.sin(v.co.y * 300 + v.co.x * 200)
    point.data.update()
    wrap(m, [(0, 1.05, 0.0), (0, 1.11, 0.0)], 0.017, 5, 0.006, tile=LEATHER, tint=(0.8, 0.7, 0.58))
    wrap(m, [(0, -0.09, 0.0), (0, 0.13, 0.0)], 0.0165, 9, 0.008, tile=CANVAS, tint=(0.75, 0.68, 0.56))
    m.socket("tip", (0, 1.32, 0.0))
    m.export(floor=False)


def machete(seed=51):
    m = Model("machete", seed=seed, held=True)
    # Grip: two wooden scales on a full tang, three rivets.
    m.box((0.022, 0.03, 0.15), (0, -0.002, 0.005), bevel=0.006, segments=3, tile=TIMBER, tint=(0.42, 0.3, 0.22), metres=0.25)
    for z in (-0.04, 0.0, 0.04):
        m.cylinder(0.004, 0.024, (0, -0.002, z), (0, 90, 0), sides=8, tile=STEEL, tint=(0.9, 0.9, 0.9), metres=0.05)
    m.box((0.016, 0.05, 0.012), (0, 0.004, 0.086), bevel=0.003, tile=GUNMETAL, tint=(0.9, 0.9, 0.9), metres=0.1)
    # Blade: spine at -Y, edge at +Y, widening toward a clipped tip.
    # Spine at -Y, edge at +Y: the blade widens to a belly near the tip, which is clipped back to the spine.
    outline = [(-0.013, 0.088), (0.017, 0.088), (0.021, 0.2), (0.03, 0.32), (0.04, 0.43), (0.041, 0.48), (0.03, 0.52),
               (0.008, 0.545), (-0.006, 0.535), (-0.01, 0.43), (-0.012, 0.3), (-0.013, 0.2)]
    blade = m.extrude(outline, 0.0045, tile=RUST, tint=(0.88, 0.86, 0.84), metres=0.3, uv="box")
    blade.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    for v in blade.data.vertices:
        t = max(0.0, min(1.0, (v.co.y + 0.005) / 0.03))
        v.co.x *= 1 - 0.85 * t
    blade.data.update()
    m.recolour(blade, lambda co, t: [c * (1 + 0.45 * max(0.0, (co.y - 0.012) / 0.02)) for c in t])
    m.socket("impact", (0, 0.03, 0.4))
    m.export(floor=False)


def bow(seed=53):
    m = Model("bow", seed=seed, forward=True)
    # Held in the left hand at the grip; the back of the bow faces +Y (the target), the string -Y.
    pts, radii, half = [], [], 0.66
    for i in range(25):
        t = i / 24 * 2 - 1
        z = t * half
        y = -0.11 * t * t + 0.02 * max(0.0, abs(t) - 0.85) / 0.15 * 1.0
        pts.append((0.0, y, z))
        radii.append(0.017 * (1 - 0.55 * abs(t)) + 0.004)
    limb = m.tube(pts, 0.016, sides=8, radii=radii, tile=TIMBER, tint=(0.8, 0.62, 0.45), metres=1.0, sharp=0)
    for v in limb.data.vertices:
        v.co.x *= 1.5
        v.co.y = v.co.y * 1.0
    m.cylinder(0.022, 0.12, (0, 0, 0), sides=10, tile=LEATHER, tint=(0.75, 0.62, 0.5), metres=0.15)
    tip_top, tip_bottom = pts[-1], pts[0]
    m.tube([(0, tip_top[1] - 0.004, tip_top[2] - 0.01), (0, tip_bottom[1] - 0.004, tip_bottom[2] + 0.01)], 0.0016, sides=5,
           tile=CANVAS, tint=(1.0, 0.97, 0.9), metres=1.0)
    # Held in the left hand at the grip; the right hand draws the string from the nock.
    m.socket("grip_l", (0, 0, 0))
    m.socket("nock", (0, -0.11, 0.0))
    m.socket("arrow_rest", (0, 0.025, 0.06))
    m.export(floor=False, hulls=[((0, -0.05, 0), (0.05, 0.16, 1.3), (0, 0, 0))])


def arrow(seed=55):
    m = Model("arrow", seed=seed, forward=True)
    # Pivot at the nock; the arrow points +Y.
    m.cylinder(0.0042, 0.72, (0, 0.37, 0), (90, 0, 0), sides=8, tile=TIMBER, tint=(0.95, 0.85, 0.7), metres=0.8)
    m.cylinder(0.0055, 0.02, (0, 0.012, 0), (90, 0, 0), sides=8, tile=GUNMETAL, tint=(0.6, 0.55, 0.5), metres=0.1)
    head = [(0.0042, 0.725), (0.009, 0.735), (0.011, 0.755), (0.0, 0.8)]
    m.lathe([(r, y) for r, y in head], (0, 0, 0), (-90, 0, 0), sides=4, tile=BASALT, tint=(0.7, 0.68, 0.66), metres=0.1, sharp=10)
    for k in range(3):
        a = k * 120
        vane = m.extrude([(0.0, 0.04), (0.012, 0.045), (0.016, 0.07), (0.014, 0.13), (0.0, 0.15)], 0.0008,
                         tile=CANVAS, tint=(0.9, 0.86, 0.8) if k else (0.75, 0.25, 0.18), metres=0.15, sharp=20)
        vane.data.transform(Matrix.Rotation(math.radians(a), 4, "Y") @ Matrix.Translation((0.004, 0, 0))
                            @ Matrix.Rotation(math.radians(-90), 4, "X"))
    m.socket("tip", (0, 0.8, 0))
    m.export(floor=False)


ALL = {"torch": torch, "metal-hatchet": metal_hatchet, "metal-pickaxe": metal_pickaxe, "wooden-spear": wooden_spear,
       "stone-spear": stone_spear, "machete": machete, "bow": bow, "arrow": arrow}

for key in script_args() or list(ALL):
    ALL[key]()
