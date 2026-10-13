"""Bunker Hatch (MONUMENTS.md §5.1 no. 10, roadside tier): a Bulwark shelter's way in, left on a cracked concrete pad
anywhere in the open: a raised collar with a heavy hatch dogged shut, its panel waiting for a power cell; a mushroom
vent, a periscope head and a broken sandbag wall. Under it a ladder shaft drops four metres into one small room: bunk,
shelves, a desk with a terminal, the air duct and a sealed blast door; a crate, and a chance of a cyan chip.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, DARK, GREY, LIGHTGREY, OLIVE, ORANGE, RED_LAMP, WARM_LAMP, YELLOW, ladder, srgb,
                        stripes)
from monkit.surfaces import CONCRETE, FORMWORK, GLASS, LAMP, MATTE, PAINT, RUST

KEY = "bunker-hatch"
TITLE = "Bunker Hatch"
SEED = 1010
BUILDER = {"chunk": 32.0, "max_edge": 1.0}

PAD = 8.0
TOP = 0.12           # the pad's surface
COLLAR = 0.62        # the collar's top
HX, HY = 0.0, 0.6    # the hatch's centre
OPEN = 1.0           # the shaft's clear opening
FLOOR = -3.4         # the room's floor
CEIL = -0.9          # its ceiling (underside)
R0, R1 = (-2.6, -0.4), (2.6, 3.4)    # the room's inside, x and y extents
BUNKER_GREY = srgb(0.66, 0.66, 0.62)


def room(b):
    """The room under the pad, its walls, the shaft, the furniture."""
    t = 0.3
    (x0, y0), (x1, y1) = (R0[0], R0[1]), (R1[0], R1[1])
    w, d = x1 - x0, y1 - y0
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    h = CEIL - FLOOR
    P.grid_box(b, (w + 2 * t, d + 2 * t, t), (cx, cy, FLOOR - t / 2), mat=CONCRETE, tint=srgb(0.55, 0.55, 0.52))
    for (px, py, sx, sy) in ((cx, y0 - t / 2, w + 2 * t, t), (cx, y1 + t / 2, w + 2 * t, t),
                             (x0 - t / 2, cy, t, d), (x1 + t / 2, cy, t, d)):
        P.grid_box(b, (sx, sy, h), (px, py, FLOOR + h / 2), mat=FORMWORK, tint=BUNKER_GREY)
    # Ceiling around the shaft's opening.
    sx0, sx1 = HX - OPEN / 2, HX + OPEN / 2
    sy0, sy1 = HY - OPEN / 2, HY + OPEN / 2
    for (ax, ay, bx, by) in ((x0 - t, y0 - t, sx0, y1 + t), (sx1, y0 - t, x1 + t, y1 + t), (sx0, y0 - t, sx1, sy0),
                             (sx0, sy1, sx1, y1 + t)):
        P.grid_box(b, (bx - ax, by - ay, t + 0.2), ((ax + bx) / 2, (ay + by) / 2, CEIL + (t + 0.2) / 2),
                   mat=CONCRETE, tint=BUNKER_GREY)
    # The shaft from the ceiling up through the collar.
    for (px, py, sx, sy) in ((HX, sy0 - 0.1, OPEN + 0.4, 0.2), (HX, sy1 + 0.1, OPEN + 0.4, 0.2),
                             (sx0 - 0.1, HY, 0.2, OPEN), (sx1 + 0.1, HY, 0.2, OPEN)):
        P.grid_box(b, (sx, sy, COLLAR - CEIL - 0.5), (px, py, CEIL + 0.5 + (COLLAR - CEIL - 0.5) / 2), mat=CONCRETE,
                   tint=BUNKER_GREY)
    ladder(b, (HX, sy1 - 0.12, FLOOR), COLLAR - FLOOR, (0, 0, 180.0), cage=False, tint=YELLOW)
    # Furniture: a bunk against the left wall, shelves at the back, the desk and terminal on the right.
    with b.frame((x0 + 0.45, cy + 0.4, FLOOR), (0, 0, 0)):
        for y in (-0.95, 0.95):
            for x in (-0.38, 0.38):
                b.box((0.05, 0.05, 1.75), (x, y, 0.875), mat=PAINT, tint=OLIVE, solid=False, vary=0)
        for z in (0.45, 1.35):
            b.box((0.8, 2.0, 0.06), (0, 0, z), mat=PAINT, tint=OLIVE, solid=False)
            b.box((0.72, 1.9, 0.12), (0, 0, z + 0.09), mat=MATTE, tint=srgb(0.4, 0.42, 0.34), solid=False)
        b.box((0.7, 0.8, 0.08), (0.02, 0.4, 0.62), (0, 0, 8), mat=MATTE, tint=srgb(0.5, 0.48, 0.4), solid=False)
    with b.frame((cx + 0.6, y1 - 0.25, FLOOR), (0, 0, 0)):
        for x in (-0.9, 0.9):
            for y in (-0.18, 0.18):
                b.box((0.04, 0.04, 2.0), (x, y, 1.0), mat=PAINT, tint=GREY, solid=False, vary=0)
        for k, z in enumerate((0.15, 0.7, 1.25, 1.8)):
            b.box((1.84, 0.42, 0.03), (0, 0, z), mat=PAINT, tint=GREY, solid=False, vary=0)
            for j in range(3):
                if (k + j) % 3 == 2:
                    continue
                b.box((0.45, 0.32, 0.26), (-0.6 + 0.6 * j, 0, z + 0.145), (0, 0, 4 * (j - 1)), mat=PAINT,
                      tint=(OLIVE, srgb(0.3, 0.32, 0.28), srgb(0.55, 0.5, 0.35))[(k + j) % 3], solid=False)
    with b.frame((x1 - 0.4, cy - 0.6, FLOOR), (0, 0, 90)):
        b.box((1.4, 0.7, 0.05), (0, 0, 0.76), mat=PAINT, tint=srgb(0.35, 0.37, 0.33), solid=False)
        for x in (-0.65, 0.65):
            b.box((0.05, 0.6, 0.74), (x, 0, 0.37), mat=PAINT, tint=DARK, solid=False)
        b.box((0.5, 0.35, 0.38), (-0.2, 0.1, 0.98), mat=PAINT, tint=srgb(0.3, 0.32, 0.3), solid=False)
        b.box((0.4, 0.02, 0.26), (-0.2, -0.081, 1.0), mat=LAMP, tint=srgb(0.3, 0.9, 0.5), solid=False, vary=0)
        b.box((0.45, 0.18, 0.03), (-0.2, -0.2, 0.8), mat=PAINT, tint=DARK, solid=False)
        b.box((0.45, 0.45, 0.05), (0.35, -0.6, 0.46), mat=PAINT, tint=DARK, solid=False)
    b.marker("loot", (x1 - 0.5, cy - 0.3, FLOOR + 0.82), table="chip-cyan", chance=0.25)
    b.marker("loot", (cx - 0.4, y0 + 0.6, FLOOR + 0.02), table="crate")
    # The sealed blast door in the front wall, the duct along the ceiling, a lamp, a water tank.
    with b.frame((cx - 0.9, y0 + 0.02, FLOOR), (0, 0, 0)):
        b.box((1.3, 0.1, 2.2), (0, 0, 1.1), mat=PAINT, tint=srgb(0.4, 0.42, 0.38), bevel=0.02)
        b.cyl(0.25, 0.06, (0, -0.08, 1.1), (90, 0, 0), mat=PAINT, tint=srgb(0.6, 0.15, 0.1),
              sides=16, cap=False, solid=False)
        for a in range(3):
            b.box((0.5, 0.03, 0.03), (0, -0.08, 1.1), (0, 60 * a, 0), mat=PAINT, tint=srgb(0.6, 0.15, 0.1),
                  solid=False)
        stripes(b, (-0.75, -0.06, 0), (0.75, -0.06, 0), 0.15, width=0.15, at_z=2.25)
        P.stencil(b, "SEALED", (0, -0.056, 1.6), (0, 0, 0), 0.12, srgb(0.9, 0.85, 0.6), align="centre")
    b.box((0.4, d, 0.3), (x1 - 0.5, cy, CEIL - 0.2), mat=PAINT, tint=LIGHTGREY, solid=False)
    b.box((0.8, 0.3, 0.4), (x1 - 0.5, y1 - 0.4, CEIL - 0.25), mat=PAINT, tint=GREY, solid=False)
    b.box((0.5, 0.2, 0.12), (cx - 0.6, cy, CEIL - 0.06), mat=PAINT, tint=DARK, solid=False)
    b.box((0.42, 0.14, 0.03), (cx - 0.6, cy, CEIL - 0.125), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
    b.marker("light", (cx - 0.6, cy, CEIL - 0.3), colour=[round(c, 3) for c in WARM_LAMP], range=6.0)
    b.cyl(0.35, 1.2, (x0 + 0.5, y0 + 0.5, FLOOR + 0.6), mat=PAINT, tint=srgb(0.3, 0.38, 0.4), sides=16)


def build(b):
    b.set_pad(("rect", PAD + 3.0, PAD + 3.0), ground="dirt", blend=6.0, clear=1.0)

    # The pad: a slab with chamfered edges, cracks, moss and stains.
    h = PAD / 2
    c = 0.25
    outline = [(-h + c, -h), (h - c, -h), (h, -h + c), (h, h - c), (h - c, h), (-h + c, h), (-h, h - c), (-h, -h + c)]
    b.slab(outline, TOP + 0.25, (0, 0, -0.25), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), solid="hull")
    for pts in (((-3.2, -3.0), (-1.4, -1.6), (-0.9, -0.2)), ((3.6, -1.0), (2.2, 0.2), (1.8, 1.8), (2.6, 3.4)),
                ((-3.8, 2.0), (-2.4, 2.4), (-1.6, 3.8))):
        P.ribbon(b, pts, 0.035, TOP, srgb(0.2, 0.19, 0.17), step=0.5)
    for x, y, r in ((-2.8, 2.9, 0.8), (3.0, -3.0, 0.6), (-3.4, -2.6, 0.5)):
        P.blob(b, (x, y, TOP), r, srgb(0.26, 0.32, 0.16), points=12, rough=0.4)
    P.blob(b, (HX + 0.9, HY - 1.4, TOP), 0.9, srgb(0.45, 0.26, 0.15), points=12, rough=0.4, squash=0.5)

    # The collar and the hatch, dogged shut.
    P.grid_box(b, (2.4, 2.4, COLLAR - TOP), (HX, HY, TOP + (COLLAR - TOP) / 2), mat=CONCRETE, tint=BUNKER_GREY,
               step=0.8)
    stripes(b, (HX - 1.2, HY - 1.205, 0), (HX + 1.2, HY - 1.205, 0), 0.14, width=0.18, at_z=COLLAR - 0.16)
    with b.frame((HX, HY, COLLAR), (0, 0, 0)):
        b.box((1.5, 1.5, 0.08), (0, 0, 0.04), mat=PAINT, tint=srgb(0.5, 0.5, 0.46))
        P.grid_box(b, (1.3, 1.3, 0.12), (0, 0, 0.14), mat=PAINT, tint=OLIVE, step=0.5)
        for a in (0, 90):
            b.box((1.25, 0.08, 0.06), (0, 0, 0.23), (0, 0, a), mat=PAINT, tint=OLIVE, solid=False)
        # The handwheel and its spokes, two locking dogs, the hinge along the back edge.
        b.cyl(0.32, 0.04, (0, 0, 0.36), mat=PAINT, tint=srgb(0.6, 0.14, 0.1), sides=18, cap=False, solid=False)
        b.cyl(0.29, 0.04, (0, 0, 0.36), mat=PAINT, tint=srgb(0.6, 0.14, 0.1), sides=18, cap=False, solid=False)
        for a in range(3):
            b.box((0.6, 0.03, 0.03), (0, 0, 0.36), (0, 0, 60 * a), mat=PAINT, tint=srgb(0.6, 0.14, 0.1), solid=False)
        b.cyl(0.05, 0.14, (0, 0, 0.29), mat=PAINT, tint=GREY, sides=8, solid=False)
        for x in (-0.58, 0.58):
            b.box((0.1, 0.22, 0.08), (x, -0.62, 0.12), mat=PAINT, tint=GREY, solid=False)
            b.box((0.16, 0.06, 0.06), (x, -0.73, 0.2), (0, 0, 20), mat=PAINT, tint=GREY, solid=False)
        for x in (-0.4, 0.4):
            b.cyl(0.07, 0.3, (x, 0.68, 0.16), (0, 90, 0), mat=PAINT, tint=DARK, sides=10, solid=False)
        P.stencil(b, "SHELTER 7", (0, -0.25, 0.201), (90, 0, 0), 0.11, srgb(0.85, 0.8, 0.55), align="centre")
    b.marker("door", (HX, HY - 1.0, COLLAR + 0.2), kind_of="hatch", cell=True)
    # The panel on a post beside the hatch: a cell socket and a red lamp.
    with b.frame((HX - 1.75, HY - 0.9, TOP), (0, 0, 20)):
        b.box((0.16, 0.16, 1.1), (0, 0, 0.55), mat=PAINT, tint=GREY)
        b.box((0.46, 0.22, 0.56), (0, -0.05, 1.25), mat=PAINT, tint=OLIVE, bevel=0.02)
        b.box((0.2, 0.03, 0.12), (0, -0.17, 1.15), mat=PAINT, tint=ORANGE, solid=False)
        b.box((0.12, 0.035, 0.06), (0, -0.175, 1.15), mat=PAINT, tint=BLACK, solid=False)
        b.box((0.05, 0.03, 0.05), (0.14, -0.17, 1.42), mat=LAMP, tint=RED_LAMP, solid=False, vary=0)
        P.stencil(b, "CELL", (0, -0.165, 1.28), (0, 0, 0), 0.06, YELLOW, align="centre")
        b.marker("fuse-box", (0, -0.3, 1.15), takes="power-cell", opens="hatch")
    P.ground_cable(b, [(HX - 1.75, HY - 0.85, 0.5), (HX - 1.4, HY - 0.6, TOP + 0.02), (HX - 1.2, HY - 0.4, 0.3)],
                   0.02, DARK, wobble=0.05, step=0.3)

    # Mushroom vent, a gooseneck vent, the periscope head; a broken sandbag wall.
    vx, vy = 2.3, 2.4
    b.box((1.0, 1.0, 0.45), (vx, vy, TOP + 0.22), mat=CONCRETE, tint=BUNKER_GREY, bevel=0.03)
    b.cyl(0.3, 1.2, (vx, vy, TOP + 1.05), mat=PAINT, tint=OLIVE, sides=16)
    b.cyl(0.32, 0.25, (vx, vy, TOP + 1.65), mat="M_Mon_Grate", tint=GREY, sides=16, cap=False, solid=False)
    b.cyl(0.62, 0.3, (vx, vy, TOP + 1.92), mat=PAINT, tint=OLIVE, sides=16, radius2=0.1)
    b.cyl(0.63, 0.06, (vx, vy, TOP + 1.78), mat=PAINT, tint=OLIVE, sides=16, solid=False)
    b.tube([(vx - 1.2, vy + 0.3, TOP), (vx - 1.2, vy + 0.3, TOP + 0.9), (vx - 1.2, vy + 0.15, TOP + 1.15),
            (vx - 1.2, vy - 0.2, TOP + 1.05), (vx - 1.2, vy - 0.3, TOP + 0.85)], 0.09, tint=OLIVE, sides=10)
    px, py = -2.2, 2.6
    b.cyl(0.12, 1.3, (px, py, TOP + 0.65), mat=PAINT, tint=OLIVE, sides=12)
    b.box((0.36, 0.3, 0.3), (px, py, TOP + 1.42), mat=PAINT, tint=OLIVE, bevel=0.02)
    b.box((0.28, 0.02, 0.12), (px, py - 0.16, TOP + 1.45), mat=GLASS, solid=False)
    b.box((0.4, 0.12, 0.05), (px, py - 0.15, TOP + 1.54), mat=PAINT, tint=OLIVE, solid=False)
    b.piece("sandbags-2m", (-2.7, -2.4, TOP), (0, 0, 12.0))
    with b.frame((-0.6, -3.2, TOP), (0, 0, -8.0)):
        for k in range(5):
            b.box((0.6, 0.38, 0.17), (b.rng.uniform(-0.8, 0.8), b.rng.uniform(-0.5, 0.4), 0.085 + 0.16 * (k % 2)),
                  (b.rng.uniform(-10, 10), 0, b.rng.uniform(0, 180)), mat=MATTE, tint=P.SANDBAG, bevel=0.05,
                  solid=False, vary=0.1, wear=0.6)
    P.debris(b, (2.6, -2.2, TOP), 1.5, count=4, size=0.5, tint=OLIVE)

    room(b)
    b.zone("no-build", (0, 0, 0), radius=9.0)
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "open", "family": "bulwark",
                   "underground": {"floor": FLOOR, "box": [R0[0] - 0.3, R0[1] - 0.3, R1[0] + 0.3, R1[1] + 0.3]}})


SUN = (32.0, 225.0)
PEOPLE = [((-1.0, -1.6, TOP), 20.0)]
VIEWS = [
    {"name": "pad", "target": (0.0, 0.4, 0.6), "distance": 13, "yaw": 28, "pitch": 30, "lens": 30},
    {"name": "hatch", "target": (-0.4, 0.4, 0.8), "distance": 5.6, "yaw": -22, "pitch": 12, "lens": 26},
    {"name": "room", "target": (-1.0, 0.3, FLOOR + 0.8), "distance": 4.3, "yaw": 132, "pitch": 13, "lens": 16},
]
