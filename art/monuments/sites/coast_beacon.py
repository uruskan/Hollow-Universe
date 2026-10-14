"""Coast Beacon (MONUMENTS.md §5.2 no. 21, small tier): a Seedward coast beacon on a headland, its lamp still turning.
A faceted tower of off-white panels 4.6 m across at the foot, tapering to 3.2 m, banded orange and teal; an open steel
stair winds round it eight times to the gallery at 38 m; above, the lantern room's glazing round the drum lens, a domed
roof, a vent and a lightning rod, red lamps. At its foot the keeper's house with its porch, chimney and rain tank, a
store, the fog-horn house at the cliff's edge with its twin horns and air receiver, a radio mast, a fence along the
edge. A military crate waits at the top of the climb.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, RED_LAMP, TEAL, WARM_LAMP,
                        YELLOW, cable, railing, srgb)
from monkit.surfaces import CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST

KEY = "coast-beacon"
TITLE = "Coast Beacon"
SEED = 2121
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

T = Vector((0.0, 4.0, 0.0))     # the tower's centre
R0, R1 = 2.3, 1.6               # the shaft's radius at the foot and at the gallery
GZ = 38.0                       # the gallery's floor
TURNS = 8
TREAD = 1.1                     # the stair's width
EDGE_Y = -16.0                  # the cliff's edge runs along here


def shaft_r(z):
    return R0 + (R1 - R0) * z / GZ


def tower(b):
    cx, cy = T.x, T.y
    P.grid_box(b, (7.0, 7.0, 0.6), (cx, cy, 0.3), mat=CONCRETE, tint=CONCRETE_T, step=1.5)
    b.cyl(R0 + 0.4, 1.2, (cx, cy, 1.2), mat=FORMWORK, tint=CONCRETE_T, sides=12)
    b.cyl(R0, GZ - 1.8, (cx, cy, 1.8 + (GZ - 1.8) / 2), mat=PANEL, tint=OFFWHITE, sides=12, radius2=R1)
    for k, z in enumerate((8.0, 17.0, 26.0, 33.5)):
        r = shaft_r(z)
        b.cyl(shaft_r(z - 0.7) + 0.04, 1.4, (cx, cy, z), mat=PAINT, tint=ORANGE if k % 2 == 0 else TEAL, sides=12,
              radius2=shaft_r(z + 0.7) + 0.04, solid=False, vary=0, wear=0.5)
    # Small windows up the shaft (on the stair's inner side), the door at the foot.
    for k in range(12):
        z = 4.0 + 2.8 * k
        a = math.radians(-90 + 360.0 * TURNS * (z - 1.8) / (GZ - 1.8) + 18.0)
        r = shaft_r(z)
        c = (cx + (r + 0.01) * math.cos(a), cy + (r + 0.01) * math.sin(a), z + 0.9)
        b.box((0.35, 0.03, 0.6), c, (0, 0, math.degrees(a) + 90), mat=GLASS, solid=False)
        b.box((0.45, 0.04, 0.7), c, (0, 0, math.degrees(a) + 90), mat=PAINT, tint=DARK, solid=False)
    b.box((1.1, 0.4, 2.4), (cx, cy - R0 - 0.35, 1.8 + 1.2 - 0.6), mat=CONCRETE, tint=CONCRETE_T)
    b.box((0.95, 0.06, 2.3), (cx, cy - R0 - 0.56, 1.2 + 1.15), mat=PAINT, tint=TEAL)
    b.box((1.3, 0.6, 0.12), (cx, cy - R0 - 0.5, 1.2 + 2.45), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    b.marker("door", (cx, cy - R0 - 1.2, 1.2), kind_of="tower")
    P.stencil(b, "CB-11", (cx, cy - R0 - 0.585, 1.2 + 2.62), (0, 0, 0), 0.22, DARK, align="centre")
    # The stair: treads cantilevered from the shaft, winding up; an outer stringer and handrail as helices.
    steps = int((GZ - 1.8) / 0.19)
    rise = (GZ - 1.8) / steps
    turn = 360.0 * TURNS / steps
    rail_pts, string_pts = [], []
    for i in range(steps):
        z = 1.8 + rise * (i + 1)
        a = -90.0 + turn * (i + 0.5)
        r = shaft_r(z)
        ra = math.radians(a)
        mid = r + TREAD / 2
        c = (cx + mid * math.cos(ra), cy + mid * math.sin(ra), z - 0.03)
        w_out = (r + TREAD) * math.radians(turn) + 0.06
        b.box((TREAD, w_out * 0.85, 0.05), c, (0, 0, a), mat=GRATE, tint=GREY, solid=False, vary=0)
        b.collide_box(Vector(c) - Vector((0, 0, 0.05)), (TREAD, w_out, 0.1), (0, 0, a))
        if i % 3 == 0:
            ro = r + TREAD - 0.04
            b.box((0.05, 0.05, 1.05), (cx + ro * math.cos(ra), cy + ro * math.sin(ra), z + 0.5), (0, 0, a),
                  mat=PAINT, tint=YELLOW, solid=False, vary=0)
        if i % 2 == 0:
            ro = r + TREAD - 0.04
            rail_pts.append((cx + ro * math.cos(ra), cy + ro * math.sin(ra), z + 1.05))
            string_pts.append((cx + ro * math.cos(ra), cy + ro * math.sin(ra), z - 0.15))
    b.tube(rail_pts, 0.03, mat=PAINT, tint=YELLOW, sides=5, cap=True)
    b.tube(string_pts, 0.07, mat=PAINT, tint=DARK, sides=5, cap=True)
    # The outer rail as a wall, in short straight pieces along the helix.
    for i in range(0, len(rail_pts) - 6, 6):
        p, q = Vector(rail_pts[i]), Vector(rail_pts[i + 6])
        b.collide_box((p + q) / 2 - Vector((0, 0, 0.5)), ((q - p).xy.length, 0.08, 1.1 + abs(q.z - p.z)),
                      (0, 0, math.degrees(math.atan2(q.y - p.y, q.x - p.x))))
    # The gallery: a grated ring on brackets with its rail, a gap where the stair arrives.
    b.cyl(R1 + 2.2, 0.25, (cx, cy, GZ - 0.12), mat=PAINT, tint=DARK, sides=24)
    b.cyl(R1 + 2.25, 0.06, (cx, cy, GZ + 0.03), mat=GRATE, tint=GREY, sides=24, solid=False)
    for k in range(12):
        a = math.radians(15 + 30 * k)
        b.beam((cx + R1 * math.cos(a), cy + R1 * math.sin(a), GZ - 1.6),
               (cx + (R1 + 2.1) * math.cos(a), cy + (R1 + 2.1) * math.sin(a), GZ - 0.2), 0.1, 0.16, mat=PAINT,
               tint=DARK)
    end = -90.0 + 360.0 * TURNS
    pts = []
    for k in range(23):
        a = math.radians(end + 22 + 14 * k)
        pts.append((cx + (R1 + 2.15) * math.cos(a), cy + (R1 + 2.15) * math.sin(a), GZ + 0.06))
    for p, q in zip(pts, pts[1:]):
        railing(b, p, q, 1.1, spacing=1.6, tint=YELLOW)
    b.marker("loot", (cx + R1 + 1.0, cy, GZ + 0.1), table="military-crate")
    # The lantern: a plinth, glazing between mullions round the drum lens, the dome, vent ball and rod.
    lz = GZ + 0.1
    b.cyl(R1 + 0.1, 1.2, (cx, cy, lz + 0.6), mat=PANEL, tint=OFFWHITE, sides=12)
    b.box((0.7, 0.06, 1.0), (cx, cy - R1 - 0.12, lz + 0.55), mat=PAINT, tint=TEAL, solid=False)
    gl = 2.8
    for k in range(12):
        a = math.radians(30 * k)
        w = 2 * (R1 - 0.05) * math.sin(math.radians(15)) - 0.02
        for (zc, hh) in ((lz + 1.2 + gl * 0.25, gl * 0.5 - 0.04), (lz + 1.2 + gl * 0.75, gl * 0.5 - 0.04)):
            if b.rng.random() < 0.3:
                continue
            b.box((w, 0.02, hh), (cx + (R1 - 0.05) * math.cos(math.radians(15)) * math.cos(a),
                                  cy + (R1 - 0.05) * math.cos(math.radians(15)) * math.sin(a), zc),
                  (0, 0, math.degrees(a) + 90), mat=GLASS, solid=False)
    b.collide_box(Vector((cx, cy, lz + 1.2 + gl / 2)), (2 * R1 - 0.2, 2 * R1 - 0.2, gl), (0, 0, 0))
    for k in range(12):
        a = math.radians(15 + 30 * k)
        b.box((0.08, 0.1, gl), (cx + (R1 - 0.02) * math.cos(a), cy + (R1 - 0.02) * math.sin(a), lz + 1.2 + gl / 2),
              (0, 0, math.degrees(a)), mat=PAINT, tint=DARK, solid=False, vary=0)
    for z in (lz + 1.2 + gl * 0.5,):
        b.cyl(R1 - 0.01, 0.06, (cx, cy, z), mat=PAINT, tint=DARK, sides=12, cap=False, solid=False, vary=0)
    b.cyl(0.25, 1.0, (cx, cy, lz + 1.7), mat=PAINT, tint=GREY, sides=10)
    b.cyl(0.75, 1.3, (cx, cy, lz + 2.85), mat=GLASS, sides=16, solid=False)
    for k in range(5):
        b.cyl(0.78, 0.06, (cx, cy, lz + 2.3 + 0.27 * k), mat=PAINT, tint=srgb(0.7, 0.62, 0.3), sides=16, cap=False,
              solid=False, vary=0)
    b.box((0.5, 0.5, 0.5), (cx, cy, lz + 2.85), mat=LAMP, tint=WARM_LAMP, solid=False)
    b.marker("light", (cx, cy, lz + 2.85), colour=[round(c, 3) for c in WARM_LAMP], range=3000.0, rotate=12.0)
    b.lathe([(R1 + 0.25, 0.0), (R1 * 0.9, 0.5), (R1 * 0.55, 1.0), (0.4, 1.3), (0.0, 1.35)],
            (cx, cy, lz + 1.2 + gl), mat=PAINT, tint=srgb(0.2, 0.32, 0.3), sides=16)
    P.ball(b, (cx, cy, lz + 1.2 + gl + 1.6), 0.35, srgb(0.2, 0.32, 0.3), mat=PAINT, sides=10, rings=5)
    b.cyl(0.02, 1.6, (cx, cy, lz + 1.2 + gl + 2.6), mat=PAINT, tint=srgb(0.72, 0.45, 0.28), sides=5, solid=False)
    for a in (0.0, 180.0):
        ra = math.radians(a)
        b.box((0.2, 0.2, 0.25), (cx + (R1 + 0.3) * math.cos(ra), cy + (R1 + 0.3) * math.sin(ra), lz + 1.2 + gl + 0.2),
              mat=LAMP, tint=RED_LAMP, solid=False)
    b.marker("light", (cx, cy, lz + 5.0), colour=[1.0, 0.12, 0.05], range=900.0, blink=1.2)
    b.marker("landmark", (cx, cy, 45.0), kind_of="coast-beacon")


def cottage(b, at, yaw):
    """The keeper's house: an L of panelled walls on a plinth under gable roofs, windows with frames, a porch with a
    door, a chimney, a rain tank on a stand."""
    ox, oy = at
    wall = srgb(0.88, 0.86, 0.8)
    roof = srgb(0.3, 0.33, 0.34)
    with b.frame((ox, oy, 0), (0, 0, yaw)):
        def wing(cx, cy, w, d, h, ridge_along_x=True):
            P.grid_box(b, (w + 0.3, d + 0.3, 0.45), (cx, cy, 0.22), mat=CONCRETE, tint=CONCRETE_T, step=1.5)
            P.grid_box(b, (w, d, h), (cx, cy, 0.45 + h / 2), mat=PANEL, tint=wall, step=1.0)
            rise = (d if ridge_along_x else w) * 0.3
            span = (d if ridge_along_x else w) / 2 + 0.45
            ln = math.hypot(span, rise)
            ang = math.degrees(math.atan2(rise, span))
            for s in (-1, 1):
                if ridge_along_x:
                    P.grid_box(b, (w + 0.6, ln, 0.1), (cx, cy + s * span / 2, 0.45 + h + rise / 2), (-s * ang, 0, 0),
                               mat=CORRUGATED, tint=roof, step=1.5)
                else:
                    P.grid_box(b, (ln, d + 0.6, 0.1), (cx + s * span / 2, cy, 0.45 + h + rise / 2), (0, s * ang, 0),
                               mat=CORRUGATED, tint=roof, step=1.5)
            if ridge_along_x:
                for x in (cx - w / 2, cx + w / 2):
                    b.mesh([(x, cy - d / 2, 0.45 + h), (x, cy + d / 2, 0.45 + h), (x, cy, 0.45 + h + rise)],
                           [[0, 1, 2] if x > cx else [0, 2, 1]], mat=PANEL, tint=wall, solid="hull")
            else:
                for y in (cy - d / 2, cy + d / 2):
                    b.mesh([(cx - w / 2, y, 0.45 + h), (cx + w / 2, y, 0.45 + h), (cx, y, 0.45 + h + rise)],
                           [[0, 1, 2] if y < cy else [0, 2, 1]], mat=PANEL, tint=wall, solid="hull")
            return rise

        rise = wing(0.0, 0.0, 10.0, 6.0, 2.8)
        wing(3.4, 5.0, 3.2, 4.4, 2.6, ridge_along_x=False)
        for x in (-3.4, -0.9, 2.6):
            b.box((1.2, 0.04, 1.2), (x, -3.02, 2.0), mat=GLASS, solid=False)
            b.box((1.35, 0.05, 1.35), (x, -3.01, 2.0), mat=PAINT, tint=TEAL, solid=False)
            b.box((1.45, 0.2, 0.08), (x, -3.1, 1.36), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        for y in (-1.2, 1.4):
            b.box((0.04, 1.0, 1.1), (-5.02, y, 2.0), mat=GLASS, solid=False)
            b.box((0.05, 1.15, 1.25), (-5.01, y, 2.0), mat=PAINT, tint=TEAL, solid=False)
        # Porch and door on the front, steps.
        b.box((1.0, 0.06, 2.3), (1.0, -3.04, 0.45 + 1.15), mat=PAINT, tint=ORANGE)
        b.marker("door", (1.0, -4.2, 0.45), kind_of="house")
        b.box((2.6, 1.8, 0.1), (1.0, -3.9, 0.45 + 2.6), (-8, 0, 0), mat=CORRUGATED, tint=roof)
        for x in (-0.2, 2.2):
            b.box((0.12, 0.12, 2.6), (x, -4.7, 0.45 + 1.3), mat=PAINT, tint=wall, solid=False)
        b.box((2.6, 1.6, 0.45), (1.0, -3.9, 0.22), mat=CONCRETE, tint=CONCRETE_T)
        # Chimney, aerial, the rain tank on its stand fed from the gutter.
        b.box((0.7, 0.7, 2.2), (-3.0, 1.0, 0.45 + 2.8 + 0.9), mat=CONCRETE, tint=srgb(0.62, 0.6, 0.57))
        b.box((0.8, 0.8, 0.12), (-3.0, 1.0, 0.45 + 2.8 + 2.05), mat=CONCRETE, tint=srgb(0.62, 0.6, 0.57),
              solid=False)
        b.cyl(0.02, 2.5, (-1.0, 0.0, 0.45 + 2.8 + rise + 1.2), mat=PAINT, tint=GREY, sides=4, solid=False)
        b.box((1.4, 0.04, 0.04), (-1.0, 0.0, 0.45 + 2.8 + rise + 2.2), mat=PAINT, tint=GREY, solid=False)
        with b.frame((-6.3, 2.0, 0.0), (0, 0, 0)):
            for sx in (-0.6, 0.6):
                for sy in (-0.6, 0.6):
                    b.box((0.1, 0.1, 1.5), (sx, sy, 0.75), mat=PAINT, tint=P.WOOD_GREY, solid=False)
            b.box((1.4, 1.4, 0.1), (0, 0, 1.5), mat=PAINT, tint=P.WOOD_GREY)
            b.cyl(0.75, 1.6, (0, 0, 2.35), mat=CORRUGATED, tint=srgb(0.6, 0.64, 0.62), sides=16)
            b.cyl(0.78, 0.1, (0, 0, 3.2), mat=PAINT, tint=srgb(0.5, 0.52, 0.5), sides=16, radius2=0.2)
            b.tube([(0.5, 0.0, 3.1), (1.3, 0.0, 3.4), (1.3, 0.0, 3.6)], 0.04, mat=PAINT, tint=GREY, sides=5)
        b.marker("loot", (-2.0, 0.5, 0.47), table="crate")
        b.marker("loot", (3.4, 5.5, 0.47), table="chip-amber", chance=0.3)


def foghorn(b, at, yaw):
    """The fog-horn house at the cliff's edge: a squat concrete block with a flat roof, two trumpet horns on its seaward
    face, the compressor's air receiver beside it, a louvred vent, a steel door."""
    with b.frame(at, (0, 0, yaw)):
        P.grid_box(b, (4.6, 3.6, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=CONCRETE_T, step=1.2)
        P.grid_box(b, (4.0, 3.0, 2.8), (0, 0, 0.3 + 1.4), mat=FORMWORK, tint=CONCRETE_T, step=1.0)
        b.box((4.4, 3.4, 0.25), (0, 0, 3.22), mat=CONCRETE, tint=CONCRETE_T)
        for s in (-1, 1):
            x = s * 0.8
            b.box((0.5, 0.4, 0.5), (x, -1.7, 2.2), mat=PAINT, tint=DARK)
            b.lathe([(0.1, 0.0), (0.14, 0.4), (0.22, 0.8), (0.42, 1.2), (0.62, 1.38)], (x, -1.9, 2.2), (90, 0, 0),
                    mat=PAINT, tint=srgb(0.82, 0.6, 0.2), sides=14, close=False, solid=False)
            b.cyl(0.64, 0.05, (x, -3.28, 2.2), (90, 0, 0), mat=PAINT, tint=DARK, sides=14, cap=False, solid=False)
        b.box((1.0, 0.06, 2.2), (1.3, 1.53, 1.4), mat=PAINT, tint=GREY)
        b.marker("door", (1.3, 2.3, 0.3), kind_of="foghorn")
        b.box((0.9, 0.05, 0.6), (-1.2, 1.53, 2.4), mat=PAINT, tint=DARK, solid=False)
        for k in range(4):
            b.box((0.85, 0.08, 0.04), (-1.2, 1.58, 2.18 + 0.14 * k), (40, 0, 0), mat=PAINT, tint=GREY, solid=False,
                  vary=0)
        b.cyl(0.5, 2.0, (2.6, 0.5, 1.3), mat=PAINT, tint=OFFWHITE, sides=14)
        P.ball(b, (2.6, 0.5, 2.3), 0.5, OFFWHITE, mat=PAINT, sides=14, rings=6)
        b.tube([(2.6, 0.5, 2.6), (2.6, 0.5, 2.9), (2.0, 0.5, 2.9)], 0.04, mat=PAINT, tint=GREY, sides=5)
        P.stencil(b, "HORN", (0, -1.52, 0.9), (0, 0, 0), 0.3, DARK, align="centre")


def build(b):
    b.set_pad(("rect", 46.0, 42.0), ground="gravel", blend=10.0, clear=2.0)
    tower(b)
    cottage(b, (-12.0, 8.0), 0.0)
    foghorn(b, (6.0, EDGE_Y + 3.5, 0.0), 0.0)
    # A path of slabs from the house to the tower and on to the horn house.
    pts = [(-11.0, 3.6), (-6.0, 1.8), (-1.0, 0.4), (3.0, -6.0), (6.0, EDGE_Y + 6.0)]
    P.ribbon(b, pts, 1.2, 0.0, srgb(0.62, 0.6, 0.56), mat=CONCRETE, step=1.0)
    # The store shed, a radio mast, the fence along the cliff's edge, rocks at the edge.
    P.prefab_hut(b, (12.0, 10.0, 0.0), (0, 0, -90.0), w=3.6, d=2.6, h=2.4, code="CB-11", door_open=40.0,
                 lamp_on=False, ac=False)
    mx, my = 15.0, -2.0
    b.box((0.8, 0.8, 0.4), (mx, my, 0.2), mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(0.08, 14.0, (mx, my, 7.2), mat=PAINT, tint=P.GALV, sides=8, radius2=0.05)
    for k in range(3):
        a = math.radians(120 * k + 20)
        cable(b, (mx + 5.0 * math.cos(a), my + 5.0 * math.sin(a), 0.1), (mx, my, 11.0), sag=0.003, radius=0.006,
              tint=srgb(0.5, 0.5, 0.5), segments=4)
    for z in (9.0, 12.0):
        b.box((1.6, 0.04, 0.04), (mx, my, z), mat=PAINT, tint=GREY, solid=False)
    b.box((0.16, 0.16, 0.2), (mx, my, 14.3), mat=LAMP, tint=RED_LAMP, solid=False)
    P.post_rail(b, [(-22.0, EDGE_Y + 1.0), (-6.0, EDGE_Y + 0.4), (2.0, EDGE_Y + 0.8), (20.0, EDGE_Y + 0.2)],
                height=1.1, rails=2, tint=srgb(0.62, 0.62, 0.6))
    for k in range(16):
        x = -22.0 + 42.0 * k / 15 + b.rng.uniform(-1, 1)
        s = 0.6 + 1.4 * b.rng.random()
        b.box((s * 1.4, s, s * 0.7), (x, EDGE_Y - 1.0 - b.rng.uniform(0, 2.5), s * 0.2),
              (b.rng.uniform(-15, 15), b.rng.uniform(-15, 15), b.rng.uniform(0, 90)), mat=CONCRETE, tint=P.ROCK,
              solid=s > 1.0, vary=0.1)
    for (x, y, s) in ((-18.0, -6.0, 1.0), (18.0, 14.0, 1.2), (-20.0, 16.0, 1.4), (8.0, 16.0, 0.9),
                      (-4.0, -12.0, 0.8), (19.0, -10.0, 0.9)):
        P.bush(b, (x, y, 0.0), s, P.LEAF if s > 0.9 else P.LEAF_DRY, lumps=4)
    b.zone("no-build", (0, 0, 0), size=(56.0, 52.0, 50.0))
    b.meta.update({"tier": "small", "landmark": True, "spawn": "headland"})


SUN = (28.0, 225.0)
PEOPLE = [((-2.0, -4.0, 0.0), 20.0)]
VIEWS = [
    {"name": "beacon", "target": (0.0, 2.0, 20.0), "distance": 80, "yaw": 26, "pitch": 6, "lens": 30},
    {"name": "foot", "target": (-4.0, 3.0, 4.0), "distance": 22, "yaw": -26, "pitch": 6, "lens": 24},
    {"name": "lantern", "target": (0.0, 4.0, 39.5), "distance": 14, "yaw": 30, "pitch": 4, "lens": 30},
]
