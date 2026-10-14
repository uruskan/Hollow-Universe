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

import bmesh

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from mathutils import Matrix, Vector, geometry, noise  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

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


# The bag dropped items lie in (dropped_bag).
BAG_HALF = (0.192, 0.148)  # half the wrapped load's length (x) and depth (y)
BAG_HEIGHT = 0.283
BAG_ROUND = (0.03, 0.035, 0.015)  # how round its upright, top and bottom edges are
STRAP_HALF, STRAP_THICK = 0.021, 0.004
CANVAS_TINT = (0.74, 0.76, 0.72)


def dropped_bag(seed=119):
    """Whatever lies on the ground (GROUND.md §6.1, decision U4): a bundle wrapped in a canvas tarp and strapped shut.
    The tarp is wrapped round a box-like load, its hemmed edge (with grommets) ending over itself across the front, its
    ends folded in like a parcel's, the top flap over the bottom one, wrinkled where the folds pull it round the
    corners. Two dark leather straps cross over the top, pressing the cloth in so it pillows between them, each with an
    iron buckle: the cross strap's on top, the long strap's on one end. 0.42 x 0.32 m and 0.3 m high. Our own design:
    only the idea of a strapped bundle came from the owner's picture."""
    m = Model("dropped-bag", WORLD, seed=seed)
    off = Vector((m.rng.uniform(0, 50), m.rng.uniform(0, 50), m.rng.uniform(0, 50)))

    def grime(co, t):
        # Dirt splashed up from the ground, grubby edges and corners, a few darker stains.
        dirt = 1 - 0.5 * (1 - smoothstep(0.0, 0.08, co.z + 0.03 * noise.noise(co * 13 + off)))
        x, y, z = abs(co.x) / BAG_HALF[0], abs(co.y) / BAG_HALF[1], co.z / BAG_HEIGHT
        rim = max(smoothstep(0.8, 1.0, x), smoothstep(0.75, 1.0, y))
        edge = max(smoothstep(0.8, 1.0, x) * smoothstep(0.75, 1.0, y), smoothstep(0.85, 1.0, z) * rim)
        stain = 1 - 0.28 * smoothstep(0.1, 0.5, noise.noise(co * 5.5 + off * 1.7))
        k = dirt * stain * (1 - 0.16 * edge)
        return (t[0] * k, t[1] * k * (0.97 + 0.03 * dirt), t[2] * k * (0.88 + 0.12 * dirt))

    canvas = dict(tile=CANVAS, tint=CANVAS_TINT, metres=0.45, vary=0.1, vary_scale=9, sharp=0, tint_fn=grime)
    leather = dict(tile=LEATHER, tint=(0.8, 0.74, 0.68), metres=0.3, vary=0.12, vary_scale=14, sharp=50)
    iron = dict(tile=STEEL, tint=(0.72, 0.7, 0.66), metres=0.1, sharp=0)

    # The load under the tarp, then each layer laid on what is already there: the ends' bottom flaps folded up, the top
    # flaps folded down over them, the hem where the wrap ends across the front.
    h = BAG_HEIGHT
    (xs, _, zs), verts, faces, flat = bundle(off)
    cloth = Cloth(verts, faces, flat, wrap_uv(m.from_mesh(verts, faces, **canvas), flat, m.rng))
    layers = [(verts, faces)]
    # Wrinkles where the folded ends pull the cloth round the corners: front -1 or back +1, which end, from (x, height
    # as a share of the bundle's) to (x, height), width, height of the ridge, bow.
    for side, end, (x0, z0), (x1, z1), width, peak, bow in (
            (-1, -1, (0.172, 0.66), (0.088, 0.87), 0.028, 0.0024, 0.007),
            (-1, -1, (0.17, 0.56), (0.112, 0.62), 0.02, 0.0017, -0.003),
            (-1, 1, (0.172, 0.745), (0.104, 0.885), 0.024, 0.0021, -0.005),
            (-1, 1, (0.172, 0.59), (0.082, 0.715), 0.03, 0.0026, 0.008),
            (1, -1, (0.172, 0.69), (0.096, 0.88), 0.026, 0.0024, -0.006),
            (1, -1, (0.172, 0.385), (0.09, 0.55), 0.028, 0.0024, 0.006),
            (1, 1, (0.172, 0.63), (0.09, 0.84), 0.026, 0.0026, 0.006),
            (1, 1, (0.17, 0.355), (0.11, 0.465), 0.02, 0.0017, -0.004)):
        rows, ups, uvs = crease(cloth, side, (end * x0, z0 * h), (end * x1, z1 * h), width, peak, bow)
        sheet(m, rows, ups, uvs=uvs, **canvas)
    for fold_z, tip_z, tip_half, thick in ((0.004, 0.6 * h, 0.03, 0.006), (h - 0.012, 0.32 * h, 0.035, 0.008)):
        for end in (-1, 1):
            rows, ups = end_fold(tree(layers), end, fold_z, tip_z, BAG_HALF[1] - 0.014, tip_half, thick)
            # Textured flat across the end, as the cloth under it is (a box projection would change over the fold).
            uvs = [cloth.to_uv(q.y + BAG_HALF[1], q.z) for row in rows for q in row]
            layers.append(sheet(m, rows, ups, uvs=uvs, **canvas))
    hem_top = min(zs, key=lambda z: abs(z - 0.4 * h))  # on a grid line of the cloth, so it follows the cloth exactly
    hem_z = hem_top - 0.025
    rows, ups, uvs = hem(cloth, [x for x in xs if abs(x) <= BAG_HALF[0] - 0.01], hem_z, hem_top, 0.0035)
    layers.append(sheet(m, rows, ups, uvs=uvs, **dict(canvas, tint_fn=lambda co, c: [
        k * (0.84 + 0.16 * smoothstep(hem_z + 0.003, hem_top, co.z)) for k in grime(co, c)])))

    # The long strap round the ends, then the cross strap over it; they bridge the folds' edges.
    centre = Vector((0, 0, h * 0.5))
    long_path = girdle(tree(layers), centre, Vector((1, 0, 0)), Vector((0, 0, 1)))
    layers.append(strap(m, long_path, centre, Vector((0, 1, 0)), **leather))
    cross_path = girdle(tree(layers), centre, Vector((0, 1, 0)), Vector((0, 0, 1)))
    layers.append(strap(m, cross_path, centre, Vector((1, 0, 0)), **leather))

    # Grommets in the hem, either side of the cross strap.
    surface = tree(layers)
    for x in (-0.15, -0.085, 0.085, 0.15):
        at = cast(surface, Vector((x, -0.6, hem_z + 0.011)), Vector((0, 1, 0)))
        m.lathe([(0.0034, 0.0), (0.0066, 0.0), (0.006, 0.0024), (0.0034, 0.0024), (0.0034, 0.0)], at, (90, 0, 0), sides=7,
                close=False, **iron)
        m.cylinder(0.0034, 0.0016, at - Vector((0, 0.0006, 0)), (90, 0, 0), sides=6, tile=RUBBER, tint=(0.3, 0.27, 0.24),
                   metres=0.05)

    # The buckles: the cross strap's on top toward the front, its end running on toward the middle; the long strap's
    # on the +x end, over the top flap, its end hanging down.
    buckle(m, cross_path, centre, Vector((1, 0, 0)), lambda p: p.z > centre.z and p.y < 0, 1, -0.08, 0.045, leather, iron)
    buckle(m, long_path, centre, Vector((0, 1, 0)), lambda p: p.x > 0, 2, 0.7 * h, -0.06, leather, iron)
    m.socket("use", (0, -0.55, 0.25))
    m.export()


def bundle(off):
    """The tarp over the load: a rounded box, pillowed where nothing holds it and pressed in under the straps (the cross
    strap at x = 0, the long one at y = 0), a little uneven, its hidden bottom one face. Returns its grid lines (x, y
    and z), the vertices, the faces, and where each vertex was on the box before it was rounded (for its texture)."""
    (hx, hy), h = BAG_HALF, BAG_HEIGHT
    rv, rt, rb = BAG_ROUND
    pinch = (STRAP_HALF + 0.002, STRAP_HALF + 0.037)  # flat under a strap, then rising back
    marks = (pinch[0], (pinch[0] + pinch[1]) / 2 - 0.004, pinch[1])
    axes = (ticks(hx, rv, marks, 0.036), ticks(hy, rv, marks, 0.036),
            [0.0, rb / 2] + fill(rb, h - rt, 0.036) + [h - rt * 2 / 3, h - rt / 3, h])
    lo = Vector((-hx + rv, -hy + rv, rb))
    hi = Vector((hx - rv, hy - rv, h - rt))

    def shape(p):
        q = Vector([min(max(p[c], lo[c]), hi[c]) for c in range(3)])
        r = Vector((rv, rv, rt if p.z > h / 2 else rb))
        e = Vector([(p[c] - q[c]) / r[c] for c in range(3)]).normalized()
        pos = q + Vector([r[c] * e[c] for c in range(3)])
        n = Vector([e[c] / r[c] for c in range(3)]).normalized()
        x, y, z = pos
        along, across = max(0.0, 1 - (x / hx) ** 2), max(0.0, 1 - (y / hy) ** 2)
        up = max(0.0, math.sin(math.pi * min(1.0, z / h)))
        bulge = 0.011 * max(0.0, n.z) ** 2 * along * across + 0.009 * n.y ** 2 * along * up + 0.005 * n.x ** 2 * across * up
        fx = 1 - smoothstep(pinch[0], pinch[1], abs(x))
        fy = 1 - smoothstep(pinch[0], pinch[1], abs(y))
        fe = 1 - smoothstep(pinch[0], pinch[1] + 0.02, abs(y))  # on the ends, under the folds: a softer dent
        fz = fy + (fe - fy) * abs(n.x)  # turning from the top onto an end
        dent = Vector((n.x * fe, n.y * fx, max(0.0, n.z) * max(fx, fz))) * -0.011
        wobble = 0.0025 * noise.noise(pos * 7 + off) * (1 - max(fx, fy)) * (1 - 0.7 * abs(n.x)) if n.z > -0.5 else 0.0
        pos = pos + n * (bulge + wobble) + dent
        pos.z = max(0.0, pos.z - 0.003 * (x / hx + 1) / 2 * (z / h))  # settled a little lower at one end
        return pos

    index, verts, faces, flat = {}, [], [], []

    def vertex(key):
        if key not in index:
            index[key] = len(verts)
            flat.append(Vector([axes[c][key[c]] for c in range(3)]))
            verts.append(shape(flat[-1]))
        return index[key]

    i0, i1, j0, j1 = 3, len(axes[0]) - 4, 3, len(axes[1]) - 4  # the bottom's middle, inside its rounded edge
    for axis in range(3):
        a, b = [c for c in range(3) if c != axis]
        for side in (0, len(axes[axis]) - 1):
            for i in range(len(axes[a]) - 1):
                for j in range(len(axes[b]) - 1):
                    if axis == 2 and side == 0 and i0 <= i < i1 and j0 <= j < j1:
                        continue
                    quad = []
                    for di, dj in ((0, 0), (1, 0), (1, 1), (0, 1)):
                        key = [0, 0, 0]
                        key[axis], key[a], key[b] = side, i + di, j + dj
                        quad.append(vertex(tuple(key)))
                    faces.append(quad)
    ring = ([(i, j0) for i in range(i0, i1)] + [(i1, j) for j in range(j0, j1)] + [(i, j1) for i in range(i1, i0, -1)] +
            [(i0, j) for j in range(j1, j0, -1)])
    faces.append([vertex((i, j, 0)) for i, j in ring])
    return axes, verts, faces, flat


def wrap_uv(o, flat, rng, tile=CANVAS, metres=0.45):
    """The tarp's texture laid on as the cloth wraps: over the top and down the front and back as one sheet (mirrored
    about the long strap, which hides the fold), the ends flat on their own; so no seam runs along the top's long
    edges, as a box projection's would. `flat`: each vertex on the box before it was rounded. Returns the mapping
    from those sheet coordinates (metres) to the atlas, for the layers laid on the cloth."""
    (hx, hy), h = BAG_HALF, BAG_HEIGHT
    scale = 1.0 / metres
    ou = 0.03 + rng.random() * (0.94 - 2 * hx * scale)
    ov = 0.03 + rng.random() * (0.94 - (hy + h) * scale)

    def to_uv(u, v):
        return (tile % 4 + ou + u * scale) / 4, (tile // 4 + ov + v * scale) / 4

    me = o.data
    uv = me.uv_layers["UVMap"]
    for poly in me.polygons:
        corners = [flat[i] for i in poly.vertices]
        for li in poly.loop_indices:
            uv.data[li].uv = to_uv(*wrap_coords(corners, flat[me.loops[li].vertex_index]))
    return to_uv


def wrap_coords(corners, p):
    """Where p (on the unrounded box, on the face with these corners) lies on the tarp's texture, in metres."""
    (hx, hy), h = BAG_HALF, BAG_HEIGHT
    if all(abs(abs(c.x) - hx) < 1e-6 for c in corners):  # an end
        return p.y + hy, p.z
    if all(abs(abs(c.y) - hy) < 1e-6 for c in corners):  # the front or back
        return p.x + hx, hy + h - p.z
    if all(c.z > h - 1e-6 for c in corners):  # the top
        return p.x + hx, abs(p.y)
    return p.x + hx, hy + h  # the bottom, never seen


class Cloth:
    """The tarp's body as built: its faces, where each vertex was on the box before it was rounded, its texture mapping
    and a ray-casting tree, so a layer laid on it can take the cloth's own texture."""

    def __init__(self, verts, faces, flat, to_uv):
        self.verts, self.faces, self.flat, self.to_uv = verts, faces, flat, to_uv
        self.tree = tree([(verts, faces)])

    def hit(self, origin, direction):
        """Where a ray meets the cloth: the point, the cloth's normal there facing the ray, and the texture's UV."""
        point, normal, index, _ = self.tree.ray_cast(origin, direction)
        if point is None:
            raise RuntimeError(f"dropped-bag: no cloth from {tuple(origin)} along {tuple(direction)}")
        ids = self.faces[index]
        corners = [self.flat[i] for i in ids]
        uv = self.to_uv(*wrap_coords(corners, unrounded(point, [self.verts[i] for i in ids], corners)))
        return point, (-normal if normal.dot(direction) > 0 else normal), uv


def unrounded(point, corners, flats):
    """Where a point on a face of the bundle was on the box before it was rounded: the same place in the face's
    triangle (a fan from its first corner, as the ray-casting tree splits it)."""
    for i in range(1, len(corners) - 1):
        if geometry.intersect_point_tri(point, corners[0], corners[i], corners[i + 1]) is not None:
            return geometry.barycentric_transform(point, corners[0], corners[i], corners[i + 1], flats[0], flats[i],
                                                  flats[i + 1])
    return geometry.barycentric_transform(point, corners[0], corners[1], corners[2], flats[0], flats[1], flats[2])


def crease(cloth, side, a, b, width, peak, bow):
    """A wrinkle in the tarp on its front (side -1) or back (+1) from a to b ((x, z) points), bowed `bow` metres to one
    side: a soft ridge `peak` high and `width` across, fading out at both ends, textured as the cloth under it. Returns
    its rows, the surface's normals under them and their UVs."""
    a, b = Vector(a), Vector(b)
    along = b - a
    across = Vector((-along.y, along.x)).normalized()
    look = Vector((0, -side, 0))
    rows, ups, uvs = [], [], []
    for f in (0.0, 0.12, 0.28, 0.5, 0.72, 0.88, 1.0):
        row, up = [], []
        taper = math.sin(math.pi * f) ** 0.6
        for s in (-1.0, -0.45, 0.0, 0.45, 1.0):
            q = a + along * f + across * (bow * math.sin(math.pi * f) + s * width / 2 * (0.55 + 0.45 * taper))
            hit, normal, uv = cloth.hit(Vector((q.x, side * 0.6, q.y)), look)
            row.append(hit + normal * peak * math.sin(math.pi * (s + 1) / 2) ** 2 * taper)
            up.append(normal)
            uvs.append(uv)
        rows.append(row)
        ups.append(up)
    return rows, ups, uvs


def fill(a, b, step):
    """a to b in equal steps no longer than `step`, both ends included."""
    n = max(1, math.ceil((b - a) / step - 1e-9))
    return [a + (b - a) * k / n for k in range(n + 1)]


def ticks(half, rounding, marks, step, border=3):
    """Grid lines across one side of the bundle, symmetric about 0: at `marks`, no wider apart than `step`, then
    `border` lines over the rounded edge."""
    inner = half - rounding
    stops = sorted({0.0, inner, *(x for x in marks if 0.0 < x < inner)})
    line = [0.0]
    for a, b in zip(stops, stops[1:]):
        line += fill(a, b, step)[1:]
    line += [inner + rounding * k / border for k in range(1, border + 1)]
    return [-x for x in reversed(line[1:])] + line


def tree(layers):
    """A ray-casting tree over (vertices, faces) layers."""
    verts, faces = [], []
    for vs, fs in layers:
        base = len(verts)
        verts += [Vector(v) for v in vs]
        faces += [[base + i for i in f] for f in fs]
    return BVHTree.FromPolygons(verts, faces)


def touch(surface, origin, direction):
    """Where a ray first meets the surface, and the surface's normal there facing the ray (None, None if it misses)."""
    hit, normal, _, _ = surface.ray_cast(origin, direction)
    if hit is None:
        return None, None
    return hit, (-normal if normal.dot(direction) > 0 else normal)


def cast(surface, origin, direction):
    hit = touch(surface, origin, direction)[0]
    if hit is None:
        raise RuntimeError(f"dropped-bag: no surface from {tuple(origin)} along {tuple(direction)}")
    return hit


def lip(d, r):
    """How much of a cloth layer's thickness there is d metres in from its edge, rounded over r."""
    return math.sin(min(1.0, max(0.0, d) / r) * math.pi / 2)


def sheet(m, rows, ups, uvs=None, **kw):
    """A layer of cloth lying on the bundle, from a grid of points (rows of equal length) and the normals of the surface
    under them (each quad faces the way its corners' do), with its own UVs (one per point) if given. Returns its
    vertices and faces."""
    bm = bmesh.new()
    grid = [[bm.verts.new(p) for p in row] for row in rows]
    width = len(rows[0])
    faces = []
    for i in range(len(rows) - 1):
        for j in range(width - 1):
            quad = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            ps = [rows[a][b] for a, b in quad]
            if (ps[1] - ps[0]).cross(ps[3] - ps[0]).dot(sum((ups[a][b] for a, b in quad), Vector())) < 0:
                quad.reverse()
            bm.faces.new([grid[a][b] for a, b in quad])
            faces.append([a * width + b for a, b in quad])
    o = m.add(bm, **kw)
    if uvs:
        layer = o.data.uv_layers["UVMap"]
        for loop in o.data.loops:
            layer.data[loop.index].uv = uvs[loop.vertex_index]
    return [p for row in rows for p in row], faces


def end_fold(surface, end, fold_z, tip_z, fold_half, tip_half, thick):
    """One flap of a parcel's end on the side x = end * ...: a trapezoid of cloth folded at fold_z (fold_half either
    side of the middle) narrowing to its tip at tip_z (tip_half either side), `thick` proud of what it lies on and
    rounded over at its free edges, merging into the cloth at the fold."""
    length = abs(tip_z - fold_z)
    rows, ups = [], []
    for v in (0.0, 0.04, 0.09, 0.17, 0.3, 0.48, 0.68, 0.86, 1 - 0.0035 / length, 1.0):
        z = fold_z + (tip_z - fold_z) * v
        half = fold_half + (tip_half - fold_half) * v + 0.005 * math.sin(math.pi * v)
        width = 2 * half
        row, up = [], []
        for d in [0.0, 0.0035] + [0.0035 + (width - 0.007) * k / 12 for k in range(1, 12)] + [width - 0.0035, width]:
            y = d - half
            hit, normal = touch(surface, Vector((end * 0.6, y, z)), Vector((-end, 0, 0)))
            while hit is None and abs(y) > 0.002:  # a corner rounder than the fold: in a little
                y -= math.copysign(0.002, y)
                hit, normal = touch(surface, Vector((end * 0.6, y, z)), Vector((-end, 0, 0)))
            if hit is None:
                raise RuntimeError(f"dropped-bag: no end to fold at z {z:.3f}")
            out = thick * lip(d, 0.005) * lip(width - d, 0.005) * lip((1 - v) * length, 0.005) * smoothstep(0.0, 0.16, v)
            if 0.0 < d < width and v < 1.0:
                out = max(out, 0.0018)
            row.append(hit + normal * out)
            up.append(normal)
        rows.append(row)
        ups.append(up)
    return rows, ups


def hem(cloth, xs, z0, z1, thick):
    """The tarp's hemmed edge across the front, where the wrap ends over itself: a fold standing `thick` proud at z0,
    fading into the cloth above by z1, over the cloth's own grid lines xs (so it follows the cloth exactly), textured
    as the cloth under it. Returns its rows, the cloth's normals under them and their UVs."""
    rows, ups, uvs = [], [], []
    look = Vector((0, 1, 0))
    rise = z0 + 0.01
    for z in (z0, z0 + 0.0015, z0 + 0.004, rise) + tuple(rise + (z1 - rise) * k / 5 for k in range(1, 6)):
        row, up = [], []
        for x in xs:
            hit, normal, uv = cloth.hit(Vector((x, -0.6, z)), look)
            out = thick * lip(z - z0, 0.004) * (1 - smoothstep(rise, z1, z)) * smoothstep(xs[-1], xs[-1] - 0.03, abs(x))
            row.append(hit + normal * (max(out, 0.0006) if z0 < z < z1 - 1e-6 else 0.0))
            up.append(normal)
            uvs.append(uv)
        rows.append(row)
        ups.append(up)
    return rows, ups, uvs


def girdle(surface, centre, u, v, samples=1440, bridge=0.009):
    """Where a strap pulled tight round the bundle lies, in the plane of u and v through `centre`: densely, a closed
    loop on the outermost surface. A strap bridges a dip narrower than about twice `bridge` (a fold's edge, a strap it
    crosses), and bends smoothly."""
    dirs, reach = [], []
    for i in range(samples):
        a = 2 * math.pi * i / samples
        d = u * math.cos(a) + v * math.sin(a)
        dirs.append(d)
        reach.append((cast(surface, centre + d * 0.6, -d) - centre).length)
    step = 2 * math.pi / samples
    bridged = [max(reach[(i + j) % samples] for j in range(-k, k + 1))
               for i, k in ((i, max(1, int(bridge / (r * step)))) for i, r in enumerate(reach))]
    smoothed = [sum(bridged[(i + j) % samples] for j in range(-k, k + 1)) / (2 * k + 1)
                for i, k in ((i, max(1, int(0.006 / (r * step)))) for i, r in enumerate(bridged))]
    return [centre + d * r for d, r in zip(dirs, smoothed)]


def simplify(path, turn=math.radians(8), step=0.04):
    """Every point of a dense path where it has turned by `turn` or run on `step` metres since the last one kept."""
    keep = [path[0]]
    bent = run = 0.0
    for i in range(1, len(path)):
        a, b, c = path[i - 1], path[i], path[(i + 1) % len(path)]
        if (b - a).length > 1e-9 and (c - b).length > 1e-9:
            bent += (b - a).angle(c - b)
        run += (b - a).length
        if bent >= turn or run >= step:
            keep.append(b)
            bent = run = 0.0
    return keep


def normals_of(pts, centre, across, closed):
    """Each point's normal across a path (perpendicular to it and to `across`), pointing away from `centre`."""
    out = []
    n = len(pts)
    for i, p in enumerate(pts):
        a = pts[(i - 1) % n] if closed else pts[max(i - 1, 0)]
        b = pts[(i + 1) % n] if closed else pts[min(i + 1, n - 1)]
        nn = (b - a).cross(across).normalized()
        out.append(nn if nn.dot(p - centre) > 0 else -nn)
    return out


def ribbon(m, pts, across, normals, inner, outer, half, closed=False, floor=False, **kw):
    """A leather strip along pts: `half` either side across, from `inner` to `outer` along each normal (each a number
    or one per point); kept above the ground when `floor`. Returns its vertices and faces."""
    n = len(pts)
    per = [x if isinstance(x, list) else [x] * n for x in (across, inner, outer, half)]
    verts, faces = [], []
    for i, p in enumerate(pts):
        w, lo, hi, hw = (x[i] for x in per)
        for o, s in ((lo, -1), (hi, -1), (hi, 1), (lo, 1)):
            q = p + normals[i] * o + w * (s * hw)
            verts.append(Vector((q.x, q.y, max(0.0, q.z))) if floor else q)
    for i in range(n if closed else n - 1):
        a, b = i * 4, (i + 1) % n * 4
        for k in range(4):
            faces.append((a + k, b + k, b + (k + 1) % 4, a + (k + 1) % 4))
    if not closed:
        last = (n - 1) * 4
        faces += [(3, 2, 1, 0), (last, last + 1, last + 2, last + 3)]
    m.from_mesh(verts, faces, **kw)
    return verts, faces


def strap(m, path, centre, across, **kw):
    """A strap round the bundle along a dense closed path; returns its vertices and faces."""
    pts = simplify(path)
    return ribbon(m, pts, across, normals_of(pts, centre, across, True), -0.0008, STRAP_THICK, STRAP_HALF, closed=True,
                  floor=True, **kw)


def stretch(path, side):
    """The run of a closed path where side(point) holds, in order."""
    n = len(path)
    start = next(i for i in range(n) if not side(path[i - 1]) and side(path[i]))
    out = []
    while len(out) < n and side(path[(start + len(out)) % n]):
        out.append(path[(start + len(out)) % n])
    return out


def buckle(m, path, centre, across, side, axis, at, tail, leather, iron):
    """An iron buckle on a strap where coordinate `axis` of its path is `at` (on the run `side` picks), and the strap's
    end run on through it for `tail` metres along that axis (signed), its tip lifting."""
    run = stretch(path, side)
    k = next(i for i in range(len(run) - 1) if (run[i][axis] - at) * (run[i + 1][axis] - at) <= 0)
    if (run[k + 1][axis] - run[k][axis]) * tail < 0:
        run.reverse()
        k = len(run) - 2 - k
    a, b = run[k], run[k + 1]
    at_point = a.lerp(b, (at - a[axis]) / (b[axis] - a[axis]))
    t = (b - a).normalized()
    n = normals_of([a, at_point, b], centre, across, False)[1]
    # The end: from under the buckle's middle on along the strap, every 5 mm or so.
    pts = [at_point - t * 0.01]
    for p in run[k + 1:]:
        if (p[axis] - (at + tail)) * tail > 0:
            break
        if (p - pts[-1]).length >= 0.005:
            pts.append(p)
    total = sum((q - p).length for p, q in zip(pts, pts[1:]))
    normals = normals_of(pts, centre, across, False)
    lifts, halves, run_on = [], [], 0.0
    for i, p in enumerate(pts):
        run_on += (p - pts[i - 1]).length if i else 0.0
        f = run_on / total
        lifts.append(STRAP_THICK - 0.0004 + 0.007 * smoothstep(0.55, 1.0, f) ** 2)
        halves.append(STRAP_HALF * 0.96 * (1 - 0.3 * smoothstep(0.82, 1.0, f)))
    ribbon(m, pts, across, normals, lifts, [x + STRAP_THICK * 0.9 for x in lifts], halves, **leather)
    # The frame rests on the strap and, past its middle, on the end run through it; the prong across the middle.
    wire, ht, hw, rc = 0.0026, 0.018, STRAP_HALF + 0.007, 0.006

    def frame(u, w, lift=0.0):
        return at_point + t * u + across * w + n * (STRAP_THICK + wire + 0.004 * (u + ht) / (2 * ht) + lift)

    loop = []
    for cu, cw, a0 in ((ht - rc, hw - rc, 0), (-ht + rc, hw - rc, 90), (-ht + rc, -hw + rc, 180), (ht - rc, -hw + rc, 270)):
        loop += [frame(cu + rc * math.cos(math.radians(a0 + 45 * s)), cw + rc * math.sin(math.radians(a0 + 45 * s)))
                 for s in range(3)]
    m.tube(loop + [loop[0]], wire, sides=6, **iron)
    m.tube([frame(-ht + 0.002, 0.0, 0.0012), frame(ht - 0.004, 0.0, 0.0012)], wire * 0.75, sides=6, **iron)


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


QUILT_TILE = 9

ALL = {"campfire": campfire, "furnace": furnace, "small-box": small_box, "bed": bed, "ladder": ladder, "wooden-door": wooden_door,
       "armoured-door": armoured_door, "double-door": double_door, "garage-door": garage_door, "loot-sack": loot_sack,
       "dropped-bag": dropped_bag}

for key in script_args() or list(ALL):
    ALL[key]()
