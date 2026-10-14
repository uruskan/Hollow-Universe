"""Hab Pods (MONUMENTS.md §5.1 no. 14, roadside tier): a colonist family's plot at a forest edge, abandoned in the Quiet.
Two prefab hab pods face each other across a yard under a sagging tarp, with the fire pit, a table and log benches;
a small storage pod has slumped off one saddle. A garden gone wild: raised beds, bean poles, bushes run riot. A home
wind turbine with a blade gone, a rain barrel, laundry still on the line, a hand-painted sign at the path. Food, cloth
and a crate; sometimes a bed.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLUE, DARK, GREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, cable, srgb)
from monkit.surfaces import CONCRETE, MATTE, PAINT

KEY = "hab-pods"
TITLE = "Hab Pods"
SEED = 1414
BUILDER = {"chunk": 32.0, "max_edge": 1.5}


def build(b):
    b.set_pad(("circle", 11.0), ground="dirt", blend=8.0, clear=1.0)

    # Two living pods across the yard, a storage pod slumped off a saddle.
    a = P.hab_pod(b, (-5.6, 1.6, 0), (0, 0, 0), length=7.0, code="PLOT 9", interior="bunk", door_open=78.0)
    c = P.hab_pod(b, (4.2, 4.6, 0), (0, 0, -90.0), length=6.0, tint=srgb(0.86, 0.84, 0.76), band=TEAL,
                  stripe=ORANGE, interior="kitchen", door_open=40.0, windows=2)
    b.marker("loot", (a.x - 1.6, a.y - 0.6, a.z + 0.02), table="cloth")
    b.marker("loot", (c.x + 0.4, c.y - 0.6, c.z + 0.02), table="food")
    with b.frame((-4.4, -5.6, 0.0), (0, -6.0, 18.0)):
        s = P.hab_pod(b, (0, 0, 0), (0, 0, 0), length=3.6, radius=1.25, legs=0.25, tint=srgb(0.8, 0.8, 0.76),
                      band=ORANGE, stripe=TEAL, interior="store", door_open=96.0, windows=0, steps=False,
                      porthole=False)
    b.marker("loot", tuple(s), table="crate")

    # The tarp over the yard, tied to the big pod's roof and two poles.
    poles = [(1.6, -1.6), (-1.0, -3.4)]
    for x, y in poles:
        b.cyl(0.05, 2.5, (x, y, 1.25), (b.rng.uniform(-3, 3), b.rng.uniform(-3, 3), 0), mat=PAINT, tint=P.WOOD_GREY,
              sides=6)
        cable(b, (x, y, 2.45), (x + 0.9, y - 1.2, 0.05), sag=0.0, radius=0.006, tint=srgb(0.7, 0.68, 0.6),
              segments=2)
        b.cyl(0.02, 0.3, (x + 0.9, y - 1.2, 0.1), mat=PAINT, tint=P.WOOD, sides=4, solid=False)
    P.tarp(b, [(-2.2, 2.9, 3.3), (2.8, 2.6, 2.95), (poles[0][0], poles[0][1], 2.45), (poles[1][0], poles[1][1], 2.4)],
           sag=0.45, tint=P.TARP_BLUE, cells=(7, 5))
    # Fire pit, a grill and a pot, log benches, a table with mugs.
    fx, fy = 0.4, -0.2
    for k in range(10):
        ang = 2 * math.pi * k / 10
        b.box((0.26, 0.2, 0.17), (fx + math.cos(ang) * 0.6, fy + math.sin(ang) * 0.6, 0.075),
              (b.rng.uniform(-10, 10), 0, math.degrees(ang)), mat=CONCRETE, tint=P.ROCK, solid=False, vary=0.1)
    P.blob(b, (fx, fy, 0.0), 0.5, P.SCORCH, points=10, rough=0.25)
    for k in range(4):
        b.cyl(0.05, 0.6, (fx, fy, 0.08), (86, 0, 45 * k + 10), mat=PAINT, tint=srgb(0.14, 0.11, 0.09), sides=6,
              solid=False)
    for k in range(3):
        ang = math.radians(120 * k + 30)
        b.beam((fx + 0.45 * math.cos(ang), fy + 0.45 * math.sin(ang), 0.0), (fx, fy, 0.9), 0.025, 0.025, mat=PAINT,
               tint=DARK)
    b.cyl(0.2, 0.22, (fx, fy, 0.5), mat=PAINT, tint=DARK, sides=12)
    b.box((0.02, 0.02, 0.4), (fx, fy, 0.75), mat=PAINT, tint=DARK, solid=False)
    for (x, y, yaw) in ((fx - 1.4, fy + 0.4, 80.0), (fx + 0.4, fy + 1.5, 10.0), (fx + 1.5, fy - 0.6, 115.0)):
        b.cyl(0.18, 1.5, (x, y, 0.18), (90, 0, yaw), mat=PAINT, tint=P.WOOD, sides=10)
    with b.frame((-1.4, 1.4, 0.0), (0, 0, 8.0)):
        b.box((1.4, 0.8, 0.05), (0, 0, 0.74), mat=PAINT, tint=P.WOOD, solid=True)
        for x in (-0.62, 0.62):
            for y in (-0.33, 0.33):
                b.box((0.06, 0.06, 0.72), (x, y, 0.36), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        for k, x in enumerate((-0.3, 0.1, 0.45)):
            b.cyl(0.045, 0.1, (x, 0.1 * k - 0.1, 0.82), mat=PAINT, tint=(RED, TEAL, YELLOW)[k], sides=8, solid=False)
        b.box((0.4, 0.3, 0.08), (0.2, -0.15, 0.8), (0, 0, 20), mat=MATTE, tint=srgb(0.7, 0.66, 0.55), solid=False)

    # The garden gone wild: raised beds, cabbages run to seed, bean poles, bushes over everything.
    with b.frame((5.6, -4.0, 0.0), (0, 0, -12.0)):
        for k in range(3):
            y = -1.8 + 1.8 * k
            for (sx, sy, x0, y0) in ((3.2, 0.08, 0, -0.55), (3.2, 0.08, 0, 0.55), (0.08, 1.1, -1.6, 0),
                                     (0.08, 1.1, 1.6, 0)):
                b.box((sx, sy, 0.35), (x0, y + y0, 0.17), mat=PAINT, tint=P.WOOD_GREY, solid=False, vary=0.05)
            b.box((3.1, 1.0, 0.3), (0, y, 0.15), mat=CONCRETE, tint=P.SOIL, solid=True, vary=0.05, wear=0.9)
            for j in range(4):
                if (k + j) % 3 == 0:
                    P.bush(b, (-1.1 + 0.75 * j, y, 0.3), 0.55, P.LEAF)
                else:
                    P.ball(b, (-1.1 + 0.75 * j, y + b.rng.uniform(-0.15, 0.15), 0.42), 0.15,
                           srgb(0.42, 0.52, 0.24), mat=MATTE, sides=8, rings=4)
        P.bean_poles(b, (2.4, -1.0, 0.0))
        P.bean_poles(b, (2.4, 0.9, 0.0), 1.6)
        b.marker("loot", (0.0, 1.0, 0.35), table="food")
    for x, y, s in ((8.2, -1.2, 1.3), (7.6, 0.4, 0.9), (-8.6, -3.0, 1.2), (-2.2, 6.0, 1.1), (9.0, -6.8, 1.0),
                    (-9.0, 4.4, 1.4)):
        P.bush(b, (x, y, 0.0), s, P.LEAF if s > 1.0 else P.LEAF_DRY)

    # The wind turbine (a blade gone), the rain barrel, laundry on its line, the sign by the path.
    P.small_turbine(b, (-8.6, -1.2, 0.0), 6.5, yaw=30.0, broken=True)
    with b.frame((-1.9, 3.3, 0.0), (0, 0, 0)):
        b.cyl(0.36, 0.95, (0, 0, 0.48), mat=PAINT, tint=BLUE, sides=14)
        b.cyl(0.38, 0.05, (0, 0, 0.97), mat=PAINT, tint=BLUE, sides=14, solid=False)
        b.cyl(0.2, 0.25, (0, 0, 1.1), mat=PAINT, tint=GREY, sides=10, radius2=0.05, solid=False)
        b.box((0.08, 0.04, 0.05), (0.38, 0, 0.18), mat=PAINT, tint=GREY, solid=False)
    p0, p1 = Vector((7.4, 1.6, 0.0)), Vector((7.2, 7.4, 0.0))
    for p in (p0, p1):
        b.cyl(0.04, 2.0, (p.x, p.y, 1.0), mat=PAINT, tint=P.WOOD_GREY, sides=6, solid=False)
        b.box((0.6, 0.04, 0.04), (p.x, p.y, 1.95), mat=PAINT, tint=P.WOOD_GREY, solid=False)
    cable(b, (p0.x, p0.y, 1.9), (p1.x, p1.y, 1.9), sag=0.02, radius=0.005, tint=srgb(0.8, 0.8, 0.78), segments=6)
    for k, (t, w, h, tint) in enumerate(((0.15, 0.7, 0.9, srgb(0.7, 0.3, 0.2)), (0.32, 0.5, 0.6, OFFWHITE),
                                        (0.5, 0.8, 0.5, srgb(0.3, 0.45, 0.6)), (0.7, 0.45, 0.7, YELLOW))):
        q = p0.lerp(p1, t)
        sag = 4 * 0.02 * (p1 - p0).length * t * (1 - t)
        b.box((0.02, w, h), (q.x, q.y, 1.9 - sag - h / 2), (b.rng.uniform(-4, 4), 0, 0), mat=MATTE, tint=tint,
              solid=False)
    b.marker("loot", (p0.x - 0.6, p0.y + 1.8, 0.05), table="cloth")
    with b.frame((-1.0, -10.0, 0.0), (0, 0, 6.0)):
        b.box((0.1, 0.1, 1.8), (-0.6, 0, 0.9), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        b.box((0.1, 0.1, 1.8), (0.6, 0, 0.9), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        b.box((1.6, 0.05, 0.6), (0, -0.05, 1.45), (0, 2.0, 0), mat=PAINT, tint=P.WOOD, solid=False)
        P.stencil(b, "PLOT 9", (0, -0.08, 1.52), (0, 2.0, 0), 0.22, OFFWHITE, align="centre", jitter=0.7)
        P.stencil(b, "KEEP OUT", (0, -0.08, 1.24), (0, 2.0, 0), 0.13, RED, align="centre", jitter=0.8)
    b.marker("loot", (-6.6, -1.4, 0.05), table="barrel")

    b.zone("no-build", (0, 0, 0), radius=18.0)
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "forest-edge", "family": "colonist"})


SUN = (28.0, 230.0)
PEOPLE = [((1.6, -3.6, 0.0), 30.0)]
VIEWS = [
    {"name": "plot", "target": (0.0, 0.0, 1.5), "distance": 26, "yaw": 24, "pitch": 26, "lens": 30},
    {"name": "yard", "target": (-0.6, 1.0, 1.4), "distance": 9.5, "yaw": 38, "pitch": 4, "lens": 24},
    {"name": "garden", "target": (4.4, -2.0, 1.2), "distance": 11, "yaw": 58, "pitch": 9, "lens": 26},
]
