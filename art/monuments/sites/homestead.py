"""Homestead (MONUMENTS.md §5.2 no. 18, small tier): a colonist farm on open grassland, left in the Quiet. Round a gravel
yard: the dome farmhouse with its walk-in hab wing, an arched prefab barn open to the yard, a red timber barn with its
hayloft door, a row of grain bins with their elevator leg, a glasshouse gone wild, a chicken coop. Out in the fields:
crop rows run to weed, a solar field, a wind pump over the stock tank, a turbine with a blade gone, post-and-rail
paddocks, the tractor and a trailer of bales where they stopped. Food, seeds and cloth; a crate in the barn.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, srgb)
from monkit.structures import silo, solar_array, wind_turbine
from monkit.surfaces import CONCRETE, CORRUGATED, GLASS, MATTE, PAINT, PANEL

KEY = "homestead"
TITLE = "Homestead"
SEED = 1818
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

HOUSE = Vector((-27.0, 2.0, 0.0))


def dome_house(b):
    """The farmhouse: a panelled dome on a ring wall with windows, a skylight and a flue; its hab wing joins it to the
    yard."""
    x, y = HOUSE.x, HOUSE.y
    r = 5.6
    P.grid_box(b, (2 * r + 1.4, 2 * r + 1.4, 0.3), (x, y, 0.15), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), step=2.5)
    b.cyl(r, 1.4, (x, y, 0.3 + 0.7), mat=PANEL, tint=srgb(0.8, 0.79, 0.74), sides=32, cap=False, solid=True)
    prof = [(r + 0.05, 0.0)] + [(r * math.cos(t), r * 0.82 * math.sin(t)) for t in (0.3, 0.6, 0.9, 1.2, 1.42)] + \
        [(0.7, r * 0.8), (0.0, r * 0.8)]
    b.lathe(prof, (x, y, 1.7), mat=PANEL, tint=OFFWHITE, sides=32, solid="hull")
    b.cyl(r + 0.06, 0.25, (x, y, 1.75), mat=PAINT, tint=TEAL, sides=32, cap=False, solid=False, vary=0)
    for k in range(7):
        a = math.radians(200 + 26 * k)
        n = Vector((math.cos(a), math.sin(a), 0.0))
        c = Vector((x, y, 0)) + n * (r * math.cos(0.45) + 0.02)
        rot = (n.to_track_quat("-Y", "Z").to_euler())
        b.box((1.0, 0.02, 0.7), (c.x, c.y, 1.7 + r * 0.82 * math.sin(0.45)), tuple(math.degrees(v) for v in rot),
              mat=GLASS, solid=False)
    b.cyl(0.9, 0.5, (x, y, 1.7 + r * 0.8 + 0.25), mat=PAINT, tint=GREY, sides=12)
    b.cyl(1.0, 0.3, (x, y, 1.7 + r * 0.8 + 0.6), mat=GLASS, sides=12, radius2=0.2, solid=False)
    b.cyl(0.18, 1.6, (x - 2.2, y + 2.0, 6.0), mat=PAINT, tint=DARK, sides=8)
    b.cyl(0.26, 0.2, (x - 2.2, y + 2.0, 6.9), mat=PAINT, tint=DARK, sides=8, radius2=0.1, solid=False)
    wing = P.hab_pod(b, (x + r + 3.2, y, 0), (0, 0, 0), length=7.0, radius=1.8, tint=srgb(0.86, 0.85, 0.8),
                     band=TEAL, stripe=ORANGE, interior="bunk", door_open=70.0, code="PLOT 12")
    b.marker("loot", (wing.x - 1.0, wing.y - 0.5, wing.z + 0.02), table="cloth")
    # A water tank on a stand and solar panels beside the house.
    with b.frame((x - 2.0, y - r - 3.0, 0), (0, 0, 0)):
        for sx in (-0.8, 0.8):
            for sy in (-0.8, 0.8):
                b.box((0.12, 0.12, 2.4), (sx, sy, 1.2), mat=PAINT, tint=DARK, solid=False)
        b.box((2.0, 2.0, 0.1), (0, 0, 2.45), mat=PAINT, tint=DARK)
        b.cyl(1.0, 1.6, (0, 0, 3.3), mat=PAINT, tint=srgb(0.2, 0.32, 0.46), sides=18)
        b.cyl(1.02, 0.08, (0, 0, 4.12), mat=PAINT, tint=srgb(0.2, 0.32, 0.46), sides=18, solid=False)
        b.tube([(0.9, 0.3, 2.6), (1.4, 0.4, 1.0), (2.6, 1.6, 0.3), (3.6, 3.2, 0.3)], 0.04, mat=PAINT, tint=DARK,
               sides=5)


def elevator_leg(b, at, height, bins):
    """A bucket elevator for the grain bins: a lattice tower with the boot and head, spouts to each bin's top, a
    caged ladder."""
    x, y = at
    with b.frame((x, y, 0.3), (0, 0, 0)):
        P.lattice(b, 0.0, height - 0.3, 1.6, 1.2, 6, member=0.07, tint=P.GALV, solid=True)
    b.box((1.0, 1.0, 1.2), (x, y, 0.6), mat=PAINT, tint=srgb(0.6, 0.62, 0.6))
    b.box((0.4, 0.6, height), (x + 0.15, y, height / 2), mat=PAINT, tint=srgb(0.65, 0.66, 0.62))
    b.box((1.4, 1.2, 1.1), (x, y, height + 0.4), mat=PAINT, tint=srgb(0.65, 0.66, 0.62), bevel=0.04)
    b.box((1.5, 1.3, 0.08), (x, y, height + 1.0), mat=PAINT, tint=DARK, solid=False)
    for (bx, by, bz) in bins:
        P.rod(b, (x + 0.5, y, height), (bx, by, bz + 0.2), 0.14, tint=srgb(0.6, 0.62, 0.6), sides=8, trim=0.0)
    from monkit.kit import ladder
    ladder(b, (x - 0.9, y, 0.0), height, (0, 0, 90), cage=True)
    b.marker("landmark", (x, y, height + 1.0), kind_of="grain-leg")


def gambrel_barn(b, at, yaw, L=16.0, W=10.0, eave=4.0, knee=(3.3, 6.3), ridge=7.7, tint=P.BARN_RED):
    """A timber barn with a gambrel roof: board-and-batten walls, white corner boards, a two-pitch roof of dark
    sheeting over the hayloft; the big doors on the -X gable (one slid aside, white cross braces), the hayloft door
    above with its hoist beam, small windows down the sides."""
    hl, hw = L / 2, W / 2
    trim = srgb(0.86, 0.84, 0.78)
    with b.frame(at, (0, 0, yaw)):
        P.grid_box(b, (L + 0.6, W + 0.6, 0.25), (0, 0, 0.125), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), step=2.5)
        z0 = 0.25
        for s in (-1, 1):
            P.grid_box(b, (L, 0.12, eave), (0, s * (hw - 0.06), z0 + eave / 2), mat=PAINT, tint=tint, step=1.2)
            for k in range(int(L / 0.6) + 1):
                x = -hl + 0.3 + (L - 0.6) * k / int(L / 0.6)
                b.box((0.06, 0.04, eave - 0.1), (x, s * (hw + 0.02), z0 + eave / 2), mat=PAINT, tint=tint,
                      solid=False, vary=0.04)
            for x in (-4.0, 0.5, 5.0):
                b.box((1.0, 0.03, 0.8), (x, s * (hw + 0.05), z0 + 2.4), mat=GLASS, solid=False)
                b.box((1.15, 0.04, 0.95), (x, s * (hw + 0.045), z0 + 2.4), mat=PAINT, tint=trim, solid=False)
            for x in (-hl, hl):
                b.box((0.18, 0.06, eave), (x + (0.09 if x < 0 else -0.09), s * (hw + 0.04), z0 + eave / 2),
                      mat=PAINT, tint=trim, solid=False, vary=0)
        ky, kz = knee
        gable = [(-hw, 0.0), (hw, 0.0), (hw, eave), (ky, kz), (0.0, ridge), (-ky, kz), (-hw, eave)]
        P.end_wall(b, (hl - 0.06, 0, z0), 90.0, gable, tint=tint, mat=PAINT, thick=0.12)
        P.end_wall(b, (-hl + 0.06, 0, z0), -90.0, gable, door=(-2.1, 2.1, 3.7), tint=tint, mat=PAINT, thick=0.12)
        b.box((0.08, 1.8, 1.6), (-hl - 0.03, 0, z0 + 5.0), mat=MATTE, tint=srgb(0.05, 0.05, 0.05), solid=False,
              vary=0)
        with b.frame((-hl - 0.06, 0.9, z0 + 4.2), (0, 0, 35.0)):
            b.box((0.06, 0.9, 1.6), (0, -0.45, 0.8), mat=PAINT, tint=tint, solid=False)
        b.box((1.4, 0.18, 0.2), (-hl - 0.6, 0, z0 + 6.6), mat=PAINT, tint=srgb(0.3, 0.26, 0.22))
        b.cyl(0.1, 0.08, (-hl - 1.15, 0, z0 + 6.42), (90, 0, 0), mat=PAINT, tint=DARK, sides=8, solid=False)
        b.tube([(-hl - 1.15, 0, z0 + 6.35), (-hl - 1.18, 0.05, z0 + 3.0)], 0.012, mat=PAINT, tint=srgb(0.6, 0.55,
                                                                                                    0.42),
               sides=4, cap=False)
        # Big doors: a track over the opening, one leaf closed, one slid away along the wall.
        b.box((0.12, 8.6, 0.14), (-hl - 0.12, 0, z0 + 3.85), mat=PAINT, tint=DARK, solid=False)
        for yy, shut in ((-1.05, True), (3.1, False)):
            with b.frame((-hl - 0.16, yy, z0), (0, 0, 0)):
                P.grid_box(b, (0.08, 2.1, 3.6), (0, 0, 1.82), mat=PAINT, tint=tint, step=1.0)
                for (cy, cz, sy, sz) in ((0, 0.1, 2.1, 0.16), (0, 3.55, 2.1, 0.16), (-1.0, 1.82, 0.14, 3.6),
                                         (1.0, 1.82, 0.14, 3.6)):
                    b.box((0.05, sy, sz), (-0.06, cy, cz), mat=PAINT, tint=trim, solid=False, vary=0)
                ln = math.hypot(2.0, 3.4)
                for sgn in (-1, 1):
                    b.box((0.05, 0.14, ln), (-0.07, 0, 1.82), (sgn * math.degrees(math.atan2(2.0, 3.4)), 0, 0),
                          mat=PAINT, tint=trim, solid=False, vary=0)
        b.marker("door", (-hl - 1.0, 0, 0.25), kind_of="barn")
        # The gambrel roof: lower and upper slopes each side, dark sheeting, a ridge cap.
        roof = srgb(0.28, 0.29, 0.31)
        for s in (-1, 1):
            for (y0, z0r, y1, z1r) in ((hw + 0.35, eave - 0.15, ky, kz), (ky, kz, 0.0, ridge)):
                ln = math.hypot(y0 - y1, z1r - z0r) + 0.08
                ang = math.degrees(math.atan2(z1r - z0r, y0 - y1))
                P.grid_box(b, (L + 0.6, ln, 0.07), (0, s * (y0 + y1) / 2, z0 + (z0r + z1r) / 2 + 0.05), (-s * ang, 0, 0),
                           mat=CORRUGATED, tint=roof, step=2.0)
        b.box((L + 0.7, 0.4, 0.12), (0, 0, z0 + ridge + 0.1), mat=PAINT, tint=roof, solid=False)
        for x in (-hl - 0.32, hl + 0.32):
            for (ya, za, yb, zb) in ((-hw - 0.35, eave - 0.15, -ky, kz), (-ky, kz, 0.0, ridge), (0.0, ridge, ky, kz),
                                     (ky, kz, hw + 0.35, eave - 0.15)):
                b.beam((x, ya, z0 + za + 0.1), (x, yb, z0 + zb + 0.1), 0.06, 0.2, mat=PAINT, tint=trim)
        # Hay inside.
        for k in range(4):
            P.round_bale(b, (hl - 2.0, -3.0 + 1.6 * k, z0), 0.0, P.STRAW)
        b.box((4.0, 3.0, 1.2), (hl - 5.0, 2.5, z0 + 0.6), mat=MATTE, tint=P.STRAW, solid=True, vary=0.05)


def farm_trailer(b, at, yaw):
    """A two-wheeled farm trailer with its drawbar on a stand, four round bales on the bed."""
    with b.frame(at, (0, 0, yaw)):
        b.box((2.3, 4.6, 0.12), (0, 0, 0.95), mat=PAINT, tint=srgb(0.5, 0.42, 0.3))
        for s in (-1, 1):
            b.box((0.1, 4.6, 0.3), (s * 1.12, 0, 1.15), mat=PAINT, tint=srgb(0.55, 0.2, 0.14), solid=False)
            b.piece("wheel-road", (s * 1.3, 0.4, 0.0), (0, 0, 0 if s < 0 else 180))
        b.box((0.2, 4.4, 0.2), (0, 0, 0.82), mat=PAINT, tint=DARK)
        b.beam((0, -2.3, 0.85), (0, -3.6, 0.5), 0.12, 0.14, mat=PAINT, tint=DARK)
        b.box((0.1, 0.1, 0.5), (0, -3.5, 0.25), mat=PAINT, tint=DARK, solid=False)
        for k, (yy, zz) in enumerate(((-1.4, 1.0), (0.0, 1.0), (1.4, 1.0), (-0.7, 2.45))):
            if k == 3:
                continue
            P.round_bale(b, (0, yy, zz), 90.0, P.WRAP_GREEN if k % 2 else P.STRAW)


def coop(b, at, yaw):
    """A chicken coop on legs with a ramp and nest boxes, inside a wire run."""
    with b.frame(at, (0, 0, yaw)):
        for sx in (-0.9, 0.9):
            for sy in (-0.6, 0.6):
                b.box((0.1, 0.1, 0.7), (sx, sy, 0.35), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        P.grid_box(b, (2.0, 1.4, 1.1), (0, 0, 0.7 + 0.55), mat=PAINT, tint=srgb(0.62, 0.5, 0.36), step=0.7)
        b.box((2.3, 1.7, 0.06), (0, 0.1, 1.95), (-12, 0, 0), mat=CORRUGATED, tint=srgb(0.5, 0.55, 0.5))
        b.box((0.6, 0.5, 0.5), (1.25, 0, 1.2), mat=PAINT, tint=srgb(0.62, 0.5, 0.36), solid=False)
        b.box((0.35, 1.2, 0.03), (-0.6, -1.2, 0.35), (35, 0, 0), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        for (cx, cy, sx, sy) in ((0, -2.4, 4.0, 0.02), (0, 2.0, 4.0, 0.02), (-2.0, -0.2, 0.02, 4.4),
                                 (2.0, -0.2, 0.02, 4.4)):
            b.box((sx, sy, 1.5), (cx, cy, 0.75), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
        for (px, py) in ((-2.0, -2.4), (2.0, -2.4), (-2.0, 2.0), (2.0, 2.0)):
            b.box((0.08, 0.08, 1.6), (px, py, 0.8), mat=PAINT, tint=P.WOOD_GREY, solid=False)


def crop_rows(b, x0, x1, y0, y1, spacing=1.8):
    """Tilled rows run to weed: soil ridges with crops gone to seed and weeds along them."""
    n = int((y1 - y0) / spacing)
    for k in range(n):
        y = y0 + spacing * (k + 0.5)
        b.box((x1 - x0, 0.7, 0.18), ((x0 + x1) / 2, y, 0.06), mat=CONCRETE, tint=P.SOIL, solid=False, vary=0.05,
              wear=0.9)
        for j in range(int((x1 - x0) / 4.0)):
            if b.rng.random() < 0.55:
                x = x0 + 2.0 + 4.0 * j + b.rng.uniform(-1.2, 1.2)
                if b.rng.random() < 0.5:
                    P.ball(b, (x, y, 0.3), 0.18 + 0.1 * b.rng.random(), P.LEAF_DRY if k % 3 else P.LEAF,
                           mat=MATTE, sides=8, rings=4)
                else:
                    for m in range(3):
                        P.rod(b, (x + 0.1 * m, y, 0.1), (x + 0.1 * m + b.rng.uniform(-0.2, 0.2),
                                                         y + b.rng.uniform(-0.2, 0.2), 0.9 + 0.4 * b.rng.random()),
                              0.015, tint=P.LEAF_DRY, sides=4)


def build(b):
    b.set_pad(("rect", 120.0, 80.0), ground="dirt", blend=12.0, clear=0.0)

    # The road along the front, the farm track up to the yard, the yard's gravel.
    P.long_road(b, (-60.0, -37.0), (60.0, -37.0), width=7.0)
    for off in (-1.0, 1.0):
        P.ribbon(b, [(2.0 + off, -33.5), (1.0 + off, -22.0), (-1.0 + off, -12.0)], 0.6, 0.0, P.EARTH_DARK,
                 mat=CONCRETE, step=2.0)
    P.blob(b, (-2.0, 3.0, 0.0), 21.0, srgb(0.55, 0.52, 0.47), points=24, rough=0.18, thick=0.02, mat=CONCRETE,
           squash=0.62)

    dome_house(b)
    # The arched barn open to the yard; hay, feed sacks and a crate inside.
    barn = P.arch_barn(b, (0.0, 18.0, 0.0), (0, 0, -90.0), radius=6.0, length=24.0)
    for k in range(5):
        P.round_bale(b, (-3.5 + 1.6 * (k % 3) - 1.0, 24.0 + 1.5 * (k // 3), 0.2 + 1.5 * (k // 3) * 0),
                     b.rng.uniform(-10, 10), P.STRAW, lying=k % 2 == 0)
    for k in range(6):
        b.box((0.6, 0.4, 0.25), (3.5 + b.rng.uniform(-0.6, 0.6), 22.0 + 0.45 * k, 0.33 + 0.25 * (k % 2)),
              (0, 0, b.rng.uniform(-10, 10)), mat=MATTE, tint=srgb(0.82, 0.78, 0.66), solid=False)
    b.marker("loot", (3.0, 25.0, 0.25), table="food")

    # The red timber barn with its hayloft door; the tractor and its bale trailer in the yard.
    gambrel_barn(b, (22.0, -1.0, 0.0), 0.0)
    b.marker("loot", (19.0, 2.0, 0.3), table="crate")
    P.tractor(b, (8.0, -6.0, 0.0), 120.0)
    farm_trailer(b, (12.5, -10.5, 0.0), 120.0)

    # Grain bins and their elevator leg.
    silo(b, (36.0, 22.0, 0.0), radius=3.0, height=10.0, cone=2.5, legs=3.0, tint=srgb(0.72, 0.74, 0.74), count=3,
         spacing=7.5)
    tops = [(36.0 + dx, 22.0, 3.0 + 2.5 + 10.0 + 0.9) for dx in (-7.5, 0.0, 7.5)]
    elevator_leg(b, (24.6, 22.0), 22.0, tops)
    for dx in (-7.5, 0.0, 7.5):
        for k in range(4):
            b.cyl(3.04, 0.12, (36.0 + dx, 22.0, 5.5 + 2.0 + 2.2 * k), mat=PAINT, tint=srgb(0.6, 0.62, 0.62), sides=32,
                  cap=False, solid=False, vary=0)
        b.cyl(0.4, 0.5, (36.0 + dx, 22.0, 16.6), mat=PAINT, tint=GREY, sides=10)
        b.cyl(0.55, 0.2, (36.0 + dx, 22.0, 16.95), mat=PAINT, tint=GREY, sides=10, radius2=0.1, solid=False)

    # The glasshouse, the coop, the wind pump, the solar field, the turbine.
    P.greenhouse(b, (-28.0, -21.0, 0.0), (0, 0, 0), 8.0, 20.0)
    b.marker("loot", (-24.0, -21.0, 0.32), table="seeds")
    coop(b, (-12.0, -14.0, 0.0), 20.0)
    P.wind_pump(b, (-46.0, -8.0, 0.0), 35.0)
    solar_array(b, (-44.0, 22.0, 0.0), (0, 0, 0), rows=3, cols=8, tilt=28.0, broken=0.2)
    wind_turbine(b, (50.0, 31.0, 0.0), height=26.0, blade=10.0, yaw=200.0, broken=True)

    # Fields and paddocks.
    crop_rows(b, 26.0, 56.0, -34.0, -14.0)
    P.post_rail(b, [(25.0, -35.0), (57.0, -35.0), (57.0, -13.0), (25.0, -13.0), (25.0, -24.0)])
    P.post_rail(b, [(33.0, -9.0), (57.0, -9.0), (57.0, 12.0), (33.0, 12.0), (33.0, 3.0)])
    with b.frame((45.0, 2.0, 0.0), (0, 0, 15.0)):
        b.cyl(0.5, 3.0, (0, 0, 0.45), (0, 90, 0), mat=PAINT, tint=srgb(0.5, 0.55, 0.55), sides=12, radius2=0.5)
        b.box((3.0, 0.9, 0.06), (0, 0, 0.62), mat="M_Mon_Gloss", tint=srgb(0.12, 0.17, 0.18), solid=False)
        b.box((2.0, 1.2, 1.6), (0, 3.0, 0.8), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        P.round_bale(b, (0, 3.0, 0.4), 0.0, P.STRAW, lying=False)
    P.post_rail(b, [(-58.0, -30.0), (-34.0, -30.0)], rails=2)
    for k, (x, y) in enumerate(((12.0, 30.0), (13.6, 30.2), (15.2, 30.0), (12.8, 30.1), (14.4, 30.1))):
        P.round_bale(b, (x, y, 1.5 if k > 2 else 0.0), 0.0, P.WRAP_GREEN if k % 2 else P.STRAW)

    # The hand-painted sign at the track.
    with b.frame((5.0, -33.5, 0.0), (0, 0, -8.0)):
        for x in (-0.9, 0.9):
            b.box((0.12, 0.12, 2.0), (x, 0, 1.0), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        b.box((2.2, 0.05, 0.9), (0, -0.07, 1.55), (0, 2.0, 0), mat=PAINT, tint=P.WOOD, solid=False)
        P.stencil(b, "PLOT 12", (0, -0.1, 1.68), (0, 2.0, 0), 0.28, OFFWHITE, align="centre", jitter=0.7)
        P.stencil(b, "SEED + BREAD", (0, -0.1, 1.25), (0, 2.0, 0), 0.15, TEAL, align="centre", jitter=0.7)
    for (x, y, s) in ((-8.0, 24.0, 1.2), (16.0, 12.0, 1.0), (-40.0, 6.0, 1.4), (-14.0, -30.0, 1.1),
                      (20.0, -24.0, 1.2), (-52.0, 34.0, 1.5), (58.0, -2.0, 1.2)):
        P.bush(b, (x, y, 0.0), s, P.LEAF, lumps=4)

    b.zone("no-build", (0, 0, 0), size=(130.0, 90.0, 40.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "farmland", "family": "colonist"})


SUN = (32.0, 225.0)
PEOPLE = [((2.0, -12.0, 0.0), 20.0)]
VIEWS = [
    {"name": "farm", "target": (2.0, 0.0, 4.0), "distance": 112, "yaw": 22, "pitch": 24, "lens": 30},
    {"name": "yard", "target": (2.0, 6.0, 3.5), "distance": 30, "yaw": -12, "pitch": 6, "lens": 26},
    {"name": "bins", "target": (32.0, 16.0, 7.0), "distance": 36, "yaw": 52, "pitch": 6, "lens": 30},
]
