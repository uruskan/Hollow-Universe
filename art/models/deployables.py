"""Things placed in the world: campfire, furnace, small box, ladder, the door variants, and the containers left on
the ground (a dead body's backpack, a bag of dropped items).

World objects face Blender -Y (Godot +Z, the model front) with their footprint centred on the origin at ground
level. Doors hang from their lower hinge corner at the origin and close across +X, like the kit's sheet-metal door;
the garage door's origin is the middle of its bottom edge.

    blender -b --factory-startup --python art/models/deployables.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from mathutils import Matrix, Vector  # noqa: E402

from hollow import WORLD, script_args  # noqa: E402
from hollow.build import (BASALT, CANVAS, GUNMETAL, LEATHER, PARCHMENT, ROCK, RUBBER, RUST, STEEL, TIMBER,  # noqa: E402
                          Model)

PLANK = (0.95, 0.9, 0.84)


def plank(m, size, at, rot=(0, 0, 0), tint=PLANK, tile=TIMBER):
    k = m.rng.uniform(0.82, 1.05)
    return m.box(size, at, rot, bevel=0.004, segments=2, tile=tile, tint=tuple(c * k for c in tint), metres=0.9, vary=0.06)


def stick(m, a, b, r, tint=(0.36, 0.28, 0.21), char_to=None, seed=0.0):
    """A charred branch from a to b (charred toward `char_to`)."""
    a, b = Vector(a), Vector(b)
    pts = [a + (b - a) * (i / 6) + Vector((0, 0, 0.01 * math.sin(i + seed))) for i in range(7)]
    o = m.tube(pts, r, sides=10, material=ROCK, tint=tint, metres=0.25, vary=0.15, sharp=0)
    if char_to is not None:
        c = Vector(char_to)
        m.recolour(o, lambda co, t: [x * (0.25 + 0.75 * min(1.0, (co - c).length / 0.22)) for x in t])
    return o


def campfire(seed=101):
    m = Model("campfire", WORLD, seed=seed)
    # Ash bed, a ring of stones, a teepee of charred branches.
    m.lathe([(0.0, 0.02), (0.18, 0.018), (0.3, 0.008), (0.34, 0.0)], sides=24, tile=BASALT, tint=(0.24, 0.23, 0.22),
            metres=0.6, vary=0.25, vary_scale=12, sharp=0)
    for i in range(10):
        a = 2 * math.pi * i / 10 + m.rng.uniform(-0.12, 0.12)
        r = 0.4 + m.rng.uniform(-0.02, 0.03)
        s = m.rng.uniform(0.17, 0.22)
        m.blob((s, s * 0.82, s * 0.66), (math.cos(a) * r, math.sin(a) * r, s * 0.26), (0, 0, math.degrees(a) + 90),
               subdivisions=3, rough=0.15, facets=0.3, material=ROCK, tint=(0.46, 0.44, 0.42), metres=0.4, vary=0.15,
               flatten=-0.5, tint_fn=lambda co, t: [c * (0.45 + 0.55 * min(1.0, max(0.0, -co.y + 0.25) / 0.5)) for c in t])
    top = Vector((0.0, 0.0, 0.34))
    for i in range(5):
        a = 2 * math.pi * i / 5 + 0.3
        foot = Vector((math.cos(a) * 0.25, math.sin(a) * 0.25, 0.03))
        stick(m, foot, top + (foot - top) * -0.08, 0.032, char_to=(0, 0, 0.22), seed=i)
    for i in range(3):
        a = 2 * math.pi * i / 3 + 1.0
        p = Vector((math.cos(a) * 0.12, math.sin(a) * 0.12, 0.03))
        stick(m, p - Vector((math.sin(a), -math.cos(a), 0)) * 0.12, p + Vector((math.sin(a), -math.cos(a), 0)) * 0.12, 0.022,
              tint=(0.2, 0.17, 0.15), char_to=(0, 0, 0.05), seed=i + 7)
    m.socket("fire", (0, 0, 0.12))
    m.socket("use", (0, -0.7, 0.4))
    m.export(hulls=[((0, 0, 0.12), (0.95, 0.95, 0.24), (0, 0, 0))])


def furnace(seed=103):
    m = Model("furnace", WORLD, seed=seed)
    # A stone base (three courses), a tapered clay kiln, iron hoops, a fire door, an ore hatch and a chimney.
    course_h = 0.17
    for course in range(3):
        z = course * course_h + course_h / 2
        for side in range(4):
            n = 4
            for k in range(n):
                t = (k + 0.5 + (0.5 if course % 2 else 0.0)) / n - 0.5
                if abs(t) > 0.5:
                    continue
                along = t * 0.94
                if side == 0 and course < 2 and abs(along) < 0.2:
                    continue  # the fire door's opening
                w = 0.94 / n * 0.97
                x, y = (along, -0.47) if side == 0 else (0.47, along) if side == 1 else (-along, 0.47) if side == 2 else (-0.47, -along)
                dims = (w * 1.08, 0.2, course_h * 1.12) if side in (0, 2) else (0.2, w * 1.08, course_h * 1.12)
                k = m.rng.uniform(0.85, 1.08)
                m.box(dims, (x, y, z), (m.rng.uniform(-2, 2), m.rng.uniform(-2, 2), m.rng.uniform(-3, 3)), bevel=0.03, segments=2,
                      material=ROCK, tint=(0.47 * k, 0.45 * k, 0.42 * k), metres=0.5, vary=0.12)
    m.box((0.88, 0.88, 0.5), (0, 0, 0.26), bevel=0.02, tile=BASALT, tint=(0.35, 0.33, 0.31), metres=0.9)
    kiln = [(0.46, 0.5), (0.45, 0.6), (0.41, 0.85), (0.36, 1.08), (0.3, 1.22), (0.2, 1.3), (0.1, 1.33)]
    body = m.lathe(kiln, sides=20, close=True, material=ROCK, tint=(0.52, 0.4, 0.31), metres=0.5, vary=0.2, vary_scale=5, sharp=0)
    for v in body.data.vertices:
        # Hand-built: a little lumpy.
        a = math.atan2(v.co.y, v.co.x)
        k = 1 + 0.025 * math.sin(a * 3 + v.co.z * 9) + 0.015 * math.sin(a * 7 - v.co.z * 17)
        v.co.x *= k
        v.co.y *= k
    for z, r in ((0.62, 0.448), (0.98, 0.37)):
        m.lathe([(r - 0.004, z - 0.03), (r + 0.012, z - 0.025), (r + 0.012, z + 0.025), (r - 0.004, z + 0.03)], sides=20, close=False,
                tile=GUNMETAL, tint=(0.9, 0.88, 0.86), metres=0.5)
    # Fire door in an iron frame.
    m.box((0.46, 0.06, 0.4), (0, -0.47, 0.24), bevel=0.01, tile=GUNMETAL, tint=(0.8, 0.8, 0.8), metres=0.5)
    m.box((0.34, 0.03, 0.28), (0.0, -0.505, 0.24), bevel=0.006, tile=RUST, tint=(0.82, 0.8, 0.78), metres=0.4)
    for z in (0.19, 0.24, 0.29):
        m.box((0.2, 0.012, 0.016), (0.0, -0.522, z), bevel=0.003, tile=RUBBER, tint=(0.25, 0.22, 0.2), metres=0.1)
    m.tube([(0.12, -0.52, 0.2), (0.12, -0.56, 0.2), (0.12, -0.56, 0.28), (0.12, -0.52, 0.28)], 0.008, sides=8, tile=STEEL,
           tint=(0.85, 0.85, 0.85), metres=0.2)
    # Ore hatch on the kiln front.
    m.box((0.24, 0.05, 0.18), (0, -0.39, 0.9), (-15, 0, 0), bevel=0.008, tile=RUST, tint=(0.82, 0.8, 0.78), metres=0.3)
    m.tube([(-0.05, -0.43, 0.87), (-0.05, -0.45, 0.9), (0.05, -0.45, 0.9), (0.05, -0.43, 0.87)], 0.007, sides=8, tile=STEEL,
           tint=(0.85, 0.85, 0.85), metres=0.2)
    # Chimney.
    m.cylinder(0.075, 0.42, (0, 0.02, 1.52), sides=16, tile=RUST, tint=(0.85, 0.82, 0.8), metres=0.5)
    m.cylinder(0.12, 0.05, (0, 0.02, 1.75), sides=16, radius2=0.04, tile=GUNMETAL, tint=(0.85, 0.85, 0.85), metres=0.3)
    m.socket("fire", (0, 0, 0.3))
    m.socket("smoke", (0, 0.02, 1.78))
    m.socket("use", (0, -0.85, 0.9))
    m.export(hulls=[((0, 0, 0.26), (1.0, 1.0, 0.52), (0, 0, 0)), ((0, 0, 0.92), (0.82, 0.82, 0.84), (0, 0, 0)),
                    ((0, 0.02, 1.52), (0.16, 0.16, 0.5), (0, 0, 0))])


def small_box(seed=105):
    m = Model("small-box", WORLD, seed=seed)
    w, d, h = 0.8, 0.5, 0.46
    # Walls of three planks each, corner posts, a floor.
    for side, (cx, cy, sx, sy) in enumerate(((0, -d / 2, w, 0.022), (0, d / 2, w, 0.022), (-w / 2, 0, 0.022, d), (w / 2, 0, 0.022, d))):
        for k in range(3):
            z = 0.02 + h / 3 * (k + 0.5)
            plank(m, (sx - (0.04 if sx > 0.1 else 0), sy - (0.04 if sy > 0.1 else 0), h / 3 - 0.008), (cx, cy, z))
    for x in (-w / 2 + 0.02, w / 2 - 0.02):
        for y in (-d / 2 + 0.02, d / 2 - 0.02):
            m.box((0.045, 0.045, h), (x, y, h / 2 + 0.02), bevel=0.005, tile=TIMBER, tint=(0.66, 0.56, 0.46), metres=0.6)
            for z in (0.05, h - 0.01):
                m.box((0.052, 0.052, 0.05), (x, y, z), bevel=0.003, tile=RUST, tint=(0.75, 0.72, 0.7), metres=0.15)
    m.box((w, d, 0.02), (0, 0, 0.012), bevel=0.003, tile=TIMBER, tint=(0.6, 0.52, 0.44), metres=0.8)
    # Rope handles.
    for x in (-w / 2 - 0.012, w / 2 + 0.012):
        m.tube([(x, -0.09, 0.32), (x * 1.04, -0.06, 0.27), (x * 1.04, 0.06, 0.27), (x, 0.09, 0.32)], 0.009, sides=8, tile=CANVAS,
               tint=(0.82, 0.74, 0.58), metres=0.2, cap=True)
    # The lid hinges along the back top edge.
    m.part("Lid", (0, d / 2, h + 0.03))
    for k in range(4):
        y = -d / 2 + d / 4 * (k + 0.5)
        plank(m, (w + 0.02, d / 4 - 0.006, 0.024), (0, y, h + 0.042))
    for x in (-0.28, 0.28):
        m.box((0.06, d + 0.02, 0.022), (x, 0, h + 0.065), bevel=0.004, tile=TIMBER, tint=(0.6, 0.5, 0.42), metres=0.6)
    m.box((0.05, 0.02, 0.08), (0, -d / 2 - 0.012, h + 0.0), bevel=0.003, tile=RUST, tint=(0.8, 0.78, 0.76), metres=0.1)
    m.part("Body")
    m.socket("lock", (0, -d / 2 - 0.02, h - 0.03))
    m.socket("use", (0, -0.75, 0.4))
    m.export(hulls=[((0, 0, (h + 0.06) / 2), (w + 0.03, d + 0.03, h + 0.06), (0, 0, 0))])


def bed(seed=121):
    """A rough wooden frame bed: posts, rails, a plank head and foot, a stuffed mattress, a wool blanket turned down at
    the head, a pillow. 2.1 m long, head toward +Y (away from the front)."""
    m = Model("bed", WORLD, seed=seed)
    half_w, half_l = 0.5, 1.03
    post = (0.07, 0.07)
    for x in (-half_w, half_w):
        for y, h in ((-half_l, 0.56), (half_l, 0.86)):
            m.box((post[0], post[1], h), (x, y, h / 2), bevel=0.006, tile=TIMBER, tint=(0.7, 0.6, 0.5), metres=1.0, vary=0.05)
            m.box((0.085, 0.085, 0.02), (x, y, h + 0.01), bevel=0.004, tile=TIMBER, tint=(0.6, 0.5, 0.42), metres=0.2)
        m.box((0.055, 2 * half_l, 0.13), (x, 0, 0.27), bevel=0.006, tile=TIMBER, tint=(0.78, 0.68, 0.56), metres=1.6, vary=0.05)
        for y in (-half_l + 0.06, half_l - 0.06):
            m.box((0.06, 0.12, 0.018), (x * 1.005, y, 0.27), bevel=0.002, tile=RUST, tint=(0.75, 0.72, 0.7), metres=0.15)
    for z in (0.5, 0.62, 0.74):
        plank(m, (2 * half_w - 0.06, 0.035, 0.1), (0, half_l, z), tint=(0.8, 0.7, 0.58))
    for z in (0.36, 0.47):
        plank(m, (2 * half_w - 0.06, 0.035, 0.09), (0, -half_l, z), tint=(0.8, 0.7, 0.58))
    for k in range(7):
        plank(m, (2 * half_w - 0.04, 0.09, 0.02), (0, -half_l + 0.15 + k * 0.29, 0.335), tint=(0.85, 0.76, 0.62))
    # Mattress, blanket, pillow.
    m.box((2 * half_w - 0.06, 2 * half_l - 0.08, 0.15), (0, 0, 0.42), bevel=0.045, segments=3, tile=CANVAS,
          tint=(0.86, 0.83, 0.74), metres=0.9, vary=0.06)
    wool = (0.42, 0.5, 0.56)
    top_end = 0.36
    length = top_end + half_l - 0.02
    centre = (top_end - half_l + 0.02) / 2
    m.box((2 * half_w - 0.02, length, 0.024), (0, centre, 0.5), bevel=0.01, segments=2, tile=CANVAS, tint=wool, metres=0.9, vary=0.1)
    for x in (-half_w + 0.012, half_w - 0.012):
        m.box((0.024, length, 0.15), (x, centre, 0.43), bevel=0.01, segments=2, tile=CANVAS, tint=wool, metres=0.9, vary=0.1)
    m.box((2 * half_w - 0.02, 0.024, 0.13), (0, -half_l + 0.02, 0.44), bevel=0.01, segments=2, tile=CANVAS, tint=wool, metres=0.9)
    m.cylinder(0.03, 2 * half_w - 0.04, (0, top_end, 0.515), (0, 90, 0), sides=16, tile=CANVAS, tint=(0.36, 0.43, 0.49), metres=0.5)
    m.blob((0.6, 0.34, 0.13), (0, half_l - 0.27, 0.55), subdivisions=3, rough=0.03, freq=2.5, tile=CANVAS,
           tint=(0.88, 0.86, 0.8), metres=0.4, vary=0.05)
    m.socket("sleep", (0, 0.2, 0.55))
    m.socket("use", (0, -1.5, 0.6))
    m.export(hulls=[((0, 0, 0.3), (2 * half_w + 0.08, 2 * half_l + 0.08, 0.6), (0, 0, 0))])


def ladder(seed=107):
    m = Model("ladder", WORLD, seed=seed)
    height, half = 3.2, 0.24
    for x in (-half, half):
        m.box((0.05, 0.075, height), (x, 0, height / 2), bevel=0.006, tile=TIMBER, tint=(0.82, 0.74, 0.64), metres=1.6, vary=0.06)
    for k in range(10):
        z = 0.28 + k * 0.3
        m.cylinder(0.018, half * 2 + 0.03, (0, -0.005, z), (0, 90, 0), sides=10, tile=TIMBER, tint=(0.9, 0.82, 0.7), metres=0.6)
        for x in (-half, half):
            m.box((0.056, 0.03, 0.03), (x, -0.04, z - 0.025), bevel=0.003, tile=RUST, tint=(0.8, 0.78, 0.76), metres=0.1)
    # Hooks at the top hang the ladder over a wall edge (on +Y).
    for x in (-half, half):
        m.tube([(x, 0.0, height - 0.08), (x, 0.0, height + 0.06), (x, 0.08, height + 0.12), (x, 0.15, height + 0.06),
                (x, 0.15, height - 0.02)], 0.012, sides=8, tile=GUNMETAL, tint=(0.85, 0.85, 0.85), metres=0.3)
    m.socket("top", (0, 0.15, height))
    m.export(hulls=[((0, 0.03, height / 2), (half * 2 + 0.08, 0.14, height + 0.1), (0, 0, 0))])


def door_hinges(m, x, zs, tile=GUNMETAL):
    for z in zs:
        m.cylinder(0.022, 0.14, (x, -0.02, z), sides=12, bevel=0.003, tile=tile, tint=(0.85, 0.85, 0.85), metres=0.2)


def wooden_door(seed=109):
    m = Model("wooden-door", WORLD, seed=seed)
    width, height, t = 0.94, 2.1, 0.045
    n = 6
    for k in range(n):
        x = width / n * (k + 0.5)
        plank(m, (width / n - 0.006, t, height - 0.01), (x, -t / 2, height / 2), tint=(0.86, 0.78, 0.68))
    for z in (0.32, 1.05, 1.78):
        plank(m, (width - 0.06, 0.03, 0.14), (width / 2, -t - 0.015, z), tint=(0.72, 0.64, 0.56))
    for (z0, z1) in ((0.32, 1.05), (1.05, 1.78)):
        a, b = Vector((0.1, -t - 0.016, z0 + 0.06)), Vector((width - 0.1, -t - 0.016, z1 - 0.06))
        mid = (a + b) / 2
        length = (b - a).length
        ang = math.degrees(math.atan2(b.z - a.z, b.x - a.x))
        plank(m, (length, 0.028, 0.12), mid, (0, -ang, 0), tint=(0.7, 0.62, 0.54))
    for z in (0.32, 1.78):
        m.box((0.42, 0.012, 0.05), (0.21, -t - 0.035, z), bevel=0.003, tile=RUST, tint=(0.8, 0.78, 0.76), metres=0.4)
        for x in (0.05, 0.16, 0.3):
            m.cylinder(0.008, 0.008, (x, -t - 0.043, z), (90, 0, 0), sides=8, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.05)
    door_hinges(m, 0.0, (0.32, 1.78), tile=RUST)
    m.tube([(0.83, -t - 0.005, 0.94), (0.83, -t - 0.06, 0.96), (0.83, -t - 0.06, 1.14), (0.83, -t - 0.005, 1.16)], 0.011, sides=8,
           tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.2)
    m.socket("lock", (0.83, -t - 0.01, 1.05))
    m.socket("hinge", (0, 0, 0))
    m.socket("use", (0.7, -0.77, 1.1))
    m.export(hulls=[((width / 2, -t / 2, height / 2), (width, t, height), (0, 0, 0))])


def armoured_door(seed=111):
    m = Model("armoured-door", WORLD, seed=seed)
    width, height, t = 0.94, 2.1, 0.07
    m.box((width, t, height), (width / 2, -t / 2, height / 2), bevel=0.008, tile=GUNMETAL, tint=(0.8, 0.8, 0.82), metres=1.0)
    # Overlaid plates and bars, rivets round the edge, a closed viewing slot.
    for z0, z1 in ((0.08, 0.98), (1.12, 2.02)):
        m.box((width - 0.12, 0.018, z1 - z0), (width / 2, -t - 0.009, (z0 + z1) / 2), bevel=0.006, tile=RUST, tint=(0.8, 0.78, 0.76), metres=1.0)
    for z in (0.5, 1.05, 1.6):
        m.box((width - 0.04, 0.03, 0.09), (width / 2, -t - 0.03, z), bevel=0.008, tile=STEEL, tint=(0.82, 0.82, 0.84), metres=0.6)
        for x in (0.07, 0.3, 0.64, 0.87):
            m.cylinder(0.011, 0.01, (x, -t - 0.048, z), (90, 0, 0), sides=10, tile=STEEL, tint=(0.9, 0.9, 0.9), metres=0.03)
    for k in range(13):
        z = 0.1 + k * (height - 0.2) / 12
        for x in (0.035, width - 0.035):
            m.cylinder(0.008, 0.01, (x, -t - 0.003, z), (90, 0, 0), sides=8, tile=STEEL, tint=(0.9, 0.9, 0.9), metres=0.03)
    m.box((0.3, 0.03, 0.06), (width / 2, -t - 0.035, 1.72), bevel=0.006, tile=GUNMETAL, tint=(0.6, 0.6, 0.62), metres=0.3)
    m.box((0.26, 0.012, 0.022), (width / 2, -t - 0.05, 1.72), bevel=0.003, tile=RUBBER, tint=(0.2, 0.2, 0.2), metres=0.1)
    door_hinges(m, 0.0, (0.3, 1.05, 1.8))
    m.tube([(0.84, -t - 0.01, 0.92), (0.84, -t - 0.07, 0.95), (0.84, -t - 0.07, 1.15), (0.84, -t - 0.01, 1.18)], 0.014, sides=8,
           tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.2)
    m.socket("lock", (0.84, -t - 0.01, 1.05))
    m.socket("hinge", (0, 0, 0))
    m.socket("use", (0.7, -0.79, 1.1))
    m.export(hulls=[((width / 2, -t / 2, height / 2), (width, t, height), (0, 0, 0))])


def double_door(seed=113):
    m = Model("double-door", WORLD, seed=seed)
    leaf, height, t = 0.94, 2.1, 0.045
    for name, x0, sign in (("Left", 0.0, 1), ("Right", 2 * leaf, -1)):
        m.part(name, (x0, 0, 0))
        cx = x0 + sign * leaf / 2
        m.box((leaf - 0.01, t, height), (cx, -t / 2, height / 2), bevel=0.005, tile=RUST, tint=(0.84, 0.82, 0.8), metres=1.2)
        for z in (0.06, height - 0.06):
            m.box((leaf - 0.01, 0.02, 0.1), (cx, -t - 0.01, z), bevel=0.004, tile=GUNMETAL, tint=(0.8, 0.8, 0.82), metres=0.6)
        for xx in (x0 + sign * 0.05, x0 + sign * (leaf - 0.06)):
            m.box((0.08, 0.02, height - 0.12), (xx, -t - 0.01, height / 2), bevel=0.004, tile=GUNMETAL, tint=(0.8, 0.8, 0.82), metres=1.2)
        # A diagonal brace and rivets.
        length = math.hypot(leaf - 0.2, height - 0.3)
        ang = math.degrees(math.atan2(height - 0.3, (leaf - 0.2))) * sign
        m.box((length, 0.016, 0.08), (cx, -t - 0.008, height / 2), (0, -ang, 0), bevel=0.004, tile=GUNMETAL,
              tint=(0.76, 0.76, 0.78), metres=1.2)
        door_hinges(m, x0, (0.3, 1.05, 1.8))
        hx = x0 + sign * (leaf - 0.1)
        m.tube([(hx, -t - 0.005, 0.95), (hx, -t - 0.055, 0.97), (hx, -t - 0.055, 1.13), (hx, -t - 0.005, 1.15)], 0.011, sides=8,
               tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.2)
    m.part("Body")
    m.socket("lock", (leaf + 0.1, -t - 0.01, 1.05))
    m.socket("use", (leaf, -0.8, 1.1))
    m.export(hulls=[((leaf, -t / 2, height / 2), (2 * leaf, t, height), (0, 0, 0))])


def garage_door(seed=115):
    m = Model("garage-door", WORLD, seed=seed)
    width, height = 2.8, 2.8
    slats = 14
    for k in range(slats):
        z = height / slats * (k + 0.5)
        tile = STEEL if k % 5 else RUST
        m.box((width, 0.04, height / slats - 0.012), (0, -0.02, z), bevel=0.012, segments=2, tile=tile,
              tint=(0.8, 0.8, 0.82) if tile == STEEL else (0.78, 0.76, 0.74), metres=1.4)
        m.box((width - 0.02, 0.012, 0.03), (0, -0.044, z - height / slats / 2 + 0.02), bevel=0.004, tile=GUNMETAL,
              tint=(0.6, 0.6, 0.62), metres=1.0)
    m.box((width + 0.02, 0.07, 0.09), (0, -0.03, 0.045), bevel=0.01, tile=GUNMETAL, tint=(0.7, 0.7, 0.72), metres=1.2)
    for x in (-0.5, 0.5):
        m.tube([(x - 0.1, -0.07, 0.25), (x - 0.1, -0.12, 0.26), (x + 0.1, -0.12, 0.26), (x + 0.1, -0.07, 0.25)], 0.012, sides=8,
               tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.2)
    m.socket("lock", (0, -0.08, 0.5))
    m.socket("use", (0, -0.9, 1.1))
    m.export(hulls=[((0, -0.03, height / 2), (width, 0.08, height), (0, 0, 0))])


def loot_sack(seed=117):
    m = Model("loot-sack", WORLD, seed=seed)
    # A canvas backpack lying on its back: body, top flap with two straps, a side pocket, a rolled blanket.
    olive = (0.58, 0.56, 0.4)
    m.blob((0.38, 0.5, 0.2), (0, 0, 0.1), subdivisions=4, rough=0.05, freq=2.5, flatten=-0.7, tile=CANVAS, tint=olive,
           metres=0.5, vary=0.14, vary_scale=10)
    m.blob((0.34, 0.24, 0.07), (0, -0.16, 0.19), (8, 0, 0), subdivisions=3, rough=0.04, tile=CANVAS, tint=(0.5, 0.48, 0.34),
           metres=0.4, vary=0.1)
    for x in (-0.09, 0.09):
        m.tube([(x, -0.27, 0.12), (x, -0.27, 0.19), (x, -0.2, 0.235), (x, -0.06, 0.215)], 0.012, sides=6, tile=LEATHER,
               tint=(0.6, 0.48, 0.36), metres=0.3)
        m.box((0.032, 0.012, 0.03), (x, -0.27, 0.15), bevel=0.003, tile=STEEL, tint=(0.9, 0.9, 0.9), metres=0.05)
    m.blob((0.1, 0.24, 0.14), (0.2, 0.04, 0.08), subdivisions=3, rough=0.05, tile=CANVAS, tint=(0.52, 0.5, 0.36), metres=0.3)
    m.cylinder(0.065, 0.42, (0, 0.27, 0.16), (0, 90, 0), sides=16, tile=QUILT_TILE, tint=(0.8, 0.75, 0.7), metres=0.4)
    for x in (-0.13, 0.13):
        m.lathe([(0.067, -0.015), (0.071, -0.01), (0.071, 0.01), (0.067, 0.015)], (x, 0.27, 0.16), (0, 90, 0), sides=16, close=False,
                tile=LEATHER, tint=(0.6, 0.48, 0.36), metres=0.2)
    m.socket("use", (0, -0.6, 0.3))
    m.export(hulls=[((0, 0.03, 0.12), (0.5, 0.6, 0.24), (0, 0, 0))])


def dropped_bag(seed=119):
    """Whatever lies on the ground (GROUND.md §6.1, decision U4): a square of burlap wrapped round its load, the corners
    gathered into a knot on top with two ears standing out of it, and a leather strap cinched round the middle with an
    iron buckle at the front. About 0.42 x 0.34 m and 0.3 m high. Our own design."""
    m = Model("dropped-bag", WORLD, seed=seed)
    burlap = (0.76, 0.65, 0.46)
    a, b, c = 0.21, 0.17, 0.125  # half sizes of the load
    belt_z, belt_half = 0.1, 0.019  # the strap: the height of its middle, half its width
    # The load, its bottom flat on the ground.
    body = m.blob((2 * a, 2 * b, 2 * c), (0, 0, 0.8 * c - 0.004), subdivisions=4, rough=0.035, freq=2.4, flatten=-0.8,
                  tile=CANVAS, tint=burlap, metres=0.45, vary=0.16, vary_scale=12)
    top = max(v.co.z for v in body.data.vertices)
    for v in body.data.vertices:
        p = v.co
        ang = math.atan2(p.y, p.x)
        # Gathered toward the knot: the upper part pulls in, folds running up into it.
        g = max(0.0, (p.z - 0.5 * top) / (0.5 * top))
        k = (1.0 - 0.5 * g * g) * (1.0 + g * (0.07 * math.sin(7 * ang + 0.8) + 0.035 * math.sin(13 * ang + 2.1)))
        # Pressed in where the strap runs, so the cloth bulges either side of it.
        d = abs(p.z - belt_z)
        if d < 0.045:
            k *= 1.0 - 0.08 * (1.0 - d / 0.045) ** 2
        p.x *= k
        p.y *= k
    body.data.update()

    bins = 48
    leather = (0.5, 0.35, 0.23)
    belt = girth(body, belt_z, belt_half * 1.2, bins)
    # The strap, following the cloth round; its tongue doubled over it from the buckle at the front (-Y) to one side.
    m.from_mesh(*band(belt, belt_z, belt_half, -0.002, 0.0055, 0, bins), tile=LEATHER, tint=leather, metres=0.3, vary=0.12,
                vary_scale=20, sharp=50)
    front = bins // 4  # the bin facing -Y
    m.from_mesh(*band(belt, belt_z - 0.001, belt_half * 0.85, 0.005, 0.009, front, front + 5), tile=LEATHER,
                tint=(0.44, 0.31, 0.2), metres=0.2, vary=0.1, vary_scale=20, sharp=50)
    # The buckle: an iron frame and its prong, standing on the strap.
    y = -(belt[front] + 0.008)
    for dx, dz, w, h in ((-0.024, 0, 0.007, 0.05), (0.024, 0, 0.007, 0.05), (0, -0.022, 0.055, 0.007), (0, 0.022, 0.055, 0.007)):
        m.box((w, 0.008, h), (dx, y, belt_z + dz), bevel=0.0015, tile=GUNMETAL, tint=(0.62, 0.6, 0.58), metres=0.1)
    m.box((0.004, 0.006, 0.042), (-0.004, y - 0.003, belt_z), (0, 18, 0), bevel=0.001, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.05)

    # The knot: the corners gathered and tied, two of them standing up out of it as ears, a cord bound round the neck.
    knot_z = top * 0.86
    m.blob((0.085, 0.075, 0.06), (0, 0, knot_z + 0.018), subdivisions=3, rough=0.12, freq=2.6, tile=CANVAS,
           tint=(0.68, 0.58, 0.41), metres=0.2, vary=0.18, vary_scale=25)
    for side in (-1, 1):
        m.blob((0.11, 0.032, 0.07), (side * 0.05, 0.004 * side, knot_z + 0.05), (8 * side, -side * 38, side * 16),
               subdivisions=3, rough=0.1, freq=2.2, tile=CANVAS, tint=(0.72, 0.62, 0.44), metres=0.2, vary=0.15, vary_scale=25)
    neck_z = knot_z - 0.012
    neck = girth(body, neck_z, 0.012, 18)
    cord = [((neck[i % 18] + 0.004) * math.cos(t), (neck[i % 18] + 0.004) * math.sin(t), neck_z + 0.003 * math.sin(3 * t))
            for i, t in ((i, (i + 0.5) / 18 * 2 * math.pi - math.pi) for i in range(19))]
    m.tube(cord, 0.0055, sides=6, tile=CANVAS, tint=(0.42, 0.33, 0.22), metres=0.1, cap=False)
    m.socket("use", (0, -0.55, 0.25))
    m.export()


def girth(body, z, half, bins):
    """How far a body reaches from its upright axis at height z (within `half`), in `bins` angles from -pi."""
    radius = [0.0] * bins
    for v in body.data.vertices:
        if abs(v.co.z - z) < half:
            i = int((math.atan2(v.co.y, v.co.x) + math.pi) / (2 * math.pi) * bins) % bins
            radius[i] = max(radius[i], math.hypot(v.co.x, v.co.y))
    for _ in range(bins):  # angles no vertex fell in take their neighbours'
        for i in range(bins):
            if radius[i] == 0.0:
                radius[i] = max(radius[i - 1], radius[(i + 1) % bins])
    return radius


def band(radius, z, half, inner, outer, start, end, bins=None):
    """A strip round a body (vertices and faces): `inner` to `outer` metres off its girth, `half` its width either side
    of height z; over the bins start..end, or right round when end is the bin count."""
    bins = bins or len(radius)
    closed = end - start >= len(radius)
    count = end - start if closed else end - start + 1
    verts, faces = [], []
    for n in range(count):
        i = (start + n) % len(radius)
        ang = (i + 0.5) / len(radius) * 2 * math.pi - math.pi
        for r, h in ((radius[i] + inner, z - half), (radius[i] + outer, z - half), (radius[i] + outer, z + half), (radius[i] + inner, z + half)):
            verts.append((r * math.cos(ang), r * math.sin(ang), h))
    for n in range(count if closed else count - 1):
        a, b = n * 4, (n + 1) % count * 4
        for k in range(4):
            q = (k + 1) % 4
            faces.append((a + k, b + k, b + q, a + q))
    if not closed:
        faces.append((0, 3, 2, 1))
        last = (count - 1) * 4
        faces.append((last, last + 1, last + 2, last + 3))
    return verts, faces


QUILT_TILE = 9

ALL = {"campfire": campfire, "furnace": furnace, "small-box": small_box, "bed": bed, "ladder": ladder, "wooden-door": wooden_door,
       "armoured-door": armoured_door, "double-door": double_door, "garage-door": garage_door, "loot-sack": loot_sack,
       "dropped-bag": dropped_bag}

for key in script_args() or list(ALL):
    ALL[key]()
