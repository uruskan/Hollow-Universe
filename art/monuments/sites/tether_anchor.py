"""Tether Anchor (MONUMENTS.md §5.5, landmark tier): the ground station of the old expansion's space elevator, on
the equator, 1.2 km across inside its octagonal wall. A stepped anchor block of board-formed concrete holds the base
clamp, a plated drum with hydraulic jaws tied down by four stays; the snapped stump of the tether rises through a
lattice guide sleeve to a frayed end 440 m up. When the cable broke, the part above fell: its loose end lies at the
block's foot, it crushed one of the two climber hangars on its way and breaks out through the wall, then runs east
across the land for tens of kilometres (two kit pieces, `tether-segment` and `tether-segment-torn`, that the game
lays along the line; this site lays the first 2.6 km). A power plant with two cooling towers, an operations block,
a funicular ramp that carried climbers up to the clamp deck, roads and gates fill the compound.
"""
import math

from mathutils import Vector

from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, FORMWORK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL,
                        YELLOW, block_building, cable, fence, lattice_tower, railing, srgb, stripes, tank)
from monkit.structures import chimney, cooling_tower, road, shed
from monkit.surfaces import ASPHALT, CORRUGATED, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "tether-anchor"
TITLE = "Tether Anchor"
SEED = 4901
BUILDER = {"chunk": 160.0, "max_edge": 14.0, "lod_min": (0.0, 2.0, 14.0)}
LODS = [450.0, 2800.0, 80000.0]

# The anchor block's tiers: (base width, top width, base height, height).
TIERS = [(280.0, 256.0, 0.0, 32.0), (200.0, 184.0, 32.0, 24.0), (136.0, 126.0, 56.0, 18.0)]
DECK = 74.0           # top of the block, where the clamp stands
CLAMP_R = 40.0
SNAP = 440.0          # where the tether broke
WALL_R = 620.0        # the wall's octagon, to its corners
CABLE_DIR = math.radians(8.0)
JUNCTION = Vector((520.0, 262.0, P.TETHER_Z))   # where the laid pieces take over from this site's own cable
LINE = 2600.0         # length of laid tether outside the junction
YARD_Z = 0.12        # top of the compound's paving
CONC = srgb(0.70, 0.69, 0.66)
CONC_DARK = srgb(0.48, 0.47, 0.45)
STEEL = srgb(0.58, 0.60, 0.61)


def octagon():
    return [(WALL_R * math.cos(math.radians(22.5 + 45 * k)), WALL_R * math.sin(math.radians(22.5 + 45 * k)))
            for k in range(8)]


def catmull(points, per=6):
    """A smooth curve through `points` (Catmull-Rom), `per` samples per span."""
    pts = [Vector(p) for p in points]
    out = []
    for i in range(len(pts) - 1):
        p0 = pts[max(i - 1, 0)]
        p1, p2 = pts[i], pts[i + 1]
        p3 = pts[min(i + 2, len(pts) - 1)]
        for k in range(per):
            t = k / per
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                              (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return [tuple(p) for p in out]


# ------------------------------------------------------------------------------------------------ the anchor


def anchor_block(b):
    for i, (w0, w1, z0, h) in enumerate(TIERS):
        P.frustum(b, (0, 0, z0), w0, w0, w1, w1, h, mat=FORMWORK, tint=CONC, cell=24.0)
        batter = (w0 - w1) / 2
        tilt = math.degrees(math.atan2(batter, h))
        for k in range(4):
            a = -90 + 90 * k
            ar = math.radians(a)
            with b.frame((math.cos(ar) * w0 / 2, math.sin(ar) * w0 / 2, z0), (0, 0, a)):
                # Buttress ribs up the battered face, every 24 m.
                n = int((w1 - 16) / 24)
                for j in range(n + 1):
                    t = -(n * 24) / 2 + j * 24
                    b.extrude([(0, 0), (3.6, 0), (-batter + 1.0, h - 1.0), (-batter, h - 1.0)], 2.6, (0, t, 0),
                              (0, 0, 0), mat=FORMWORK, tint=CONC, solid=False)
                # Pour joints: dark grooves every 8 m up the face.
                zz = 8.0
                while zz < h - 2:
                    wz = w0 + (w1 - w0) * zz / h
                    b.box((0.5, wz - 2.0, 0.35), (-batter * zz / h + 0.1, 0, zz), (0, -tilt, 0),
                          mat=PAINT, tint=CONC_DARK, solid=False, vary=0)
                    zz += 8.0
                # A livery band under each terrace's edge.
                b.box((0.6, w1 - 1.0, 1.6), (-batter * (h - 2.6) / h + 0.2, 0, h - 2.6), (0, -tilt, 0),
                      mat=PAINT, tint=ORANGE if i == 0 else TEAL, solid=False, vary=0)
                # Darker cast panels in alternate bays, rain streaks under the terrace edge, ladders up some ribs.
                for j in range(n):
                    t = -(n * 24) / 2 + j * 24 + 12
                    if (j + i + k) % 2 == 0:
                        zc = h * 0.45
                        b.box((0.4, 17.0, h * 0.6), (-batter * zc / h + 0.12, t, zc), (0, -tilt, 0), mat=FORMWORK,
                              tint=CONC_DARK, solid=False, vary=0.04)
                    for q in range(2):
                        zs = h - 6.0 - b.rng.uniform(0, 4)
                        hs = b.rng.uniform(5.0, 12.0)
                        zc = zs - hs / 2
                        b.box((0.3, b.rng.uniform(0.6, 1.6), hs), (-batter * zc / h + 0.16, t + b.rng.uniform(-9, 9), zc),
                              (0, -tilt, 0), mat=MATTE, tint=srgb(0.36, 0.35, 0.33), solid=False, vary=0.1)
                if i == 0:
                    for q, wdt in enumerate((5.0, 2.0, 5.0, 5.0)):
                        b.box((0.5, wdt, 8.0), (-batter * 0.35 + 0.25, -14.0 + q * 7.5 + wdt / 2, 11.0), (0, -tilt, 0),
                              mat=PAINT, tint=srgb(0.2, 0.2, 0.21), solid=False, vary=0)
            # Parapet and rail round each terrace's edge.
            for k2 in range(4):
                a2 = math.radians(90 * k2)
                c = Vector((math.cos(a2), math.sin(a2), 0))
                s = Vector((-math.sin(a2), math.cos(a2), 0))
                p = c * (w1 / 2 - 0.3) + Vector((0, 0, z0 + h))
                b.box((0.6 if k2 % 2 == 0 else w1, w1 if k2 % 2 == 0 else 0.6, 1.1), (p.x, p.y, p.z + 0.55),
                      mat=CONCRETE, tint=CONC, solid=False)
                railing(b, p + s * (w1 / 2 - 1.0) + Vector((0, 0, 1.1)) - c * 0.0, p - s * (w1 / 2 - 1.0) +
                        Vector((0, 0, 1.1)), 0.6, spacing=4.0, toe=False)
    # Terraces: anchor heads at the corners of the first, vents and stair heads, lamps.
    for k in range(4):
        a = math.radians(45 + 90 * k)
        x, y = math.cos(a) * 160, math.sin(a) * 160
        with b.frame((x, y, 32.0), (0, 0, math.degrees(a))):
            b.box((22.0, 18.0, 9.0), (0, 0, 4.5), mat=FORMWORK, tint=CONC, bevel=0.3)
            b.extrude([(-11, 9.0), (11, 9.0), (6, 14.0), (-6, 14.0)], 14.0, (0, 0, 0), (0, 0, 0), mat=CONCRETE,
                      tint=CONC)
            b.cyl(5.0, 6.0, (-2.0, 0, 15.0), (0, 62, 0), mat=PAINT, tint=DARK, sides=24)
            b.box((4.0, 18.4, 1.2), (-9.0, 0, 7.0), mat=PAINT, tint=YELLOW, solid=False, vary=0)
            stripes(b, (-11.1, -9.0, 0), (-11.1, 9.0, 0), 1.2, width=1.0, at_z=1.0, normal=(-1, 0, 0))
            P.door(b, (11.05, 3.0, 0), 90.0, light=True)
        P.aviation_lamp(b, (x, y, 32.0 + 15.6), 0.9)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        r = 112.0
        x, y = math.cos(a) * r, math.sin(a) * r
        with b.frame((x, y, 32.0), (0, 0, math.degrees(a))):
            b.box((7.0, 9.0, 5.0), (0, 0, 2.5), mat=PANEL, tint=LIGHTGREY, bevel=0.1)
            for j in range(5):
                b.box((0.2, 7.0, 0.35), (3.55, 0, 1.0 + j * 0.8), (30, 0, 0), mat=PAINT, tint=GREY, solid=False, vary=0)
            P.door(b, (-3.5, 0, 0), -90.0, light=True, sign=False)
    for k in range(12):
        a = math.radians(15 + 30 * k)
        b.piece("lamp-post", (math.cos(a) * 124, math.sin(a) * 124, 32.0), (0, 0, math.degrees(a) + 90))
    # The processional stair up the south face to the first terrace, and the portal at its head.
    run = 32.0 / 0.18 * 0.28
    P.concrete_stair(b, (0, -128.0 - run, 0), 0.0, 16.0, 32.0, run, tint=CONC)
    for x in (-12.0, 12.0):
        b.box((4.0, 4.0, 6.0), (x, -128.0 - run - 3.0, 3.0), mat=FORMWORK, tint=CONC)
        b.box((4.6, 4.6, 0.6), (x, -128.0 - run - 3.0, 6.3), mat=PAINT, tint=ORANGE, solid=False)
    portal(b, (0, -100.0 + 4.0, 32.0))
    # Vehicle bays at ground level in the east and west faces.
    for sx in (-1, 1):
        for y in (-60.0, 60.0):
            with b.frame((sx * 140.0, y, 0), (0, 0, -90 if sx < 0 else 90)):
                b.box((26.0, 18.0, 18.0), (0, 1.0, 9.0), mat=FORMWORK, tint=CONC)
                P.bay_door(b, (0, -8.05, 0), 0.0, 14.0, 10.0, open_frac=0.25 + 0.4 * b.rng.random())
                P.door(b, (9.5, -8.05, 0), 0.0, light=True, marker=None)


def portal(b, at):
    """The anchor's main door: a deep concrete surround in the second tier's face, blast doors half open."""
    x, y, z = at
    with b.frame((x, y, z), (0, 0, 0)):
        b.box((34.0, 14.0, 24.0), (0, -4.0, 12.0), mat=FORMWORK, tint=CONC)
        b.box((26.0, 14.2, 3.0), (0, -4.0, 25.5), mat=CONCRETE, tint=CONC)
        b.box((18.0, 2.0, 14.0), (0, -11.1, 7.0), mat=MATTE, tint=P.SOOT, solid=False, vary=0)
        b.box((9.0, 2.2, 14.0), (-8.5, -12.4, 7.0), mat=PAINT, tint=srgb(0.42, 0.44, 0.45))
        b.box((9.0, 2.2, 14.0), (5.5, -13.6, 7.0), mat=PAINT, tint=srgb(0.42, 0.44, 0.45))
        for xx in (-8.5, 5.5):
            for k in range(5):
                b.box((9.2, 2.3, 0.5), (xx, -12.4 if xx < 0 else -13.6, 1.5 + k * 3.0), mat=PAINT, tint=YELLOW if k % 2
                      else BLACK, solid=False, vary=0)
        b.box((22.0, 0.3, 2.2), (0, -11.2, 16.6), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        P.sign_board(b, (0, -11.4, 18.3), 0.0, 16.0, 3.0, posts=False, rows=1)
        P.door(b, (13.0, -11.05, 0), 0.0, marker="anchor-block")
        for xx in (-15.0, 15.0):
            b.piece("lamp-post", (xx, -16.0, 0), (0, 0, 0))
        b.marker("loot", (0, -6.0, 0.2), table="military-crate")


def clamp(b):
    """The base clamp: a ribbed drum on the deck, a coned roof, eight hydraulic jaws round the tether's throat."""
    z0 = DECK
    R = CLAMP_R
    b.cyl(R + 7.0, 4.0, (0, 0, z0 + 2.0), mat=CONCRETE, tint=CONC, sides=64)
    b.cyl(R, 36.0, (0, 0, z0 + 22.0), mat=HULL, tint=OFFWHITE, sides=72)
    for k in range(36):
        a = 2 * math.pi * k / 36
        b.box((2.4, 2.4, 36.0), (math.cos(a) * (R + 1.0), math.sin(a) * (R + 1.0), z0 + 22.0),
              (0, 0, math.degrees(a)), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0.02)
    b.cyl(R + 2.4, 3.6, (0, 0, z0 + 31.0), mat=PAINT, tint=ORANGE, sides=72, solid=False, vary=0)
    b.cyl(R + 2.3, 1.4, (0, 0, z0 + 27.6), mat=PAINT, tint=TEAL, sides=72, solid=False, vary=0)
    b.cyl(R + 3.0, 2.2, (0, 0, z0 + 41.0), mat=PAINT, tint=DARK, sides=72, solid=False)
    # Gallery round the cornice.
    b.cyl(R + 5.5, 0.4, (0, 0, z0 + 39.6), mat=GRATE, tint=GREY, sides=72, solid=False)
    pts = [(math.cos(a) * (R + 5.3), math.sin(a) * (R + 5.3), z0 + 39.8) for a in
           (2 * math.pi * k / 48 for k in range(49))]
    for p, q in zip(pts, pts[1:]):
        railing(b, p, q, 1.1, spacing=3.0, toe=False)
    for k in range(24):
        a = 2 * math.pi * (k + 0.5) / 24
        b.beam((math.cos(a) * (R + 0.5), math.sin(a) * (R + 0.5), z0 + 36.0),
               (math.cos(a) * (R + 5.2), math.sin(a) * (R + 5.2), z0 + 39.4), 0.4, 0.4, mat=PAINT, tint=DARK)
    b.lathe([(R + 2.0, 0.0), (R - 4.0, 3.0), (17.0, 11.0), (0.0, 11.0)], (0, 0, z0 + 40.0), mat=PANEL,
            tint=srgb(0.62, 0.63, 0.62), sides=72)
    # Jaws and rams.
    for k in range(8):
        a = 22.5 + 45 * k
        with b.frame((0, 0, z0 + 51.0), (0, 0, a)):
            b.extrude([(9.0, 0.0), (17.0, 0.0), (15.0, 14.0), (9.0, 16.0)], 7.0, (0, 0, 0), (0, 0, 0), mat=PAINT,
                      tint=YELLOW)
            for j in range(4):
                b.box((0.3, 7.2, 1.2), (16.2 - j * 0.45, 0, 2.0 + j * 3.4), (0, -8, 0), mat=PAINT, tint=BLACK,
                      solid=False, vary=0)
            b.tube([(30.0, 0, -6.5), (17.5, 0, 6.0)], 1.3, mat=PAINT, tint=STEEL, sides=12)
            b.tube([(30.0, 0, -6.5), (24.0, 0, -0.5)], 1.8, mat=PAINT, tint=DARK, sides=12)
            b.box((4.0, 4.0, 2.0), (30.5, 0, -7.0), mat=PAINT, tint=DARK, solid=False)
    b.lathe([(11.0, 0.0), (11.5, 3.0), (10.0, 7.0), (8.4, 7.0), (8.4, 0.0), (11.0, 0.0)], (0, 0, z0 + 67.0),
            mat=PAINT, tint=DARK, sides=48, close=False)
    for k in range(8):
        a = math.radians(45 * k)
        P.aviation_lamp(b, (math.cos(a) * (R + 3.0), math.sin(a) * (R + 3.0), z0 + 43.0), 0.9)
    # Doors round the drum's foot, a winch house on the deck, the deck's machinery.
    for k in range(4):
        a = 45 + 90 * k
        ar = math.radians(a)
        P.door(b, (math.cos(ar) * (R + 0.4), math.sin(ar) * (R + 0.4), z0 + 4.0), a + 90.0, light=True)
    for k in range(4):
        a = math.radians(90 * k)
        x, y = math.cos(a) * 54, math.sin(a) * 54
        b.cyl(4.0, 9.0, (x, y, z0 + 4.5), (0, 90, math.degrees(a) + 90), mat=PAINT, tint=STEEL, sides=24)
        b.box((10.0, 6.0, 2.0), (x, y, z0 + 1.0), (0, 0, math.degrees(a) + 90), mat=PAINT, tint=DARK)
    b.marker("loot", (0, -R - 6.0, z0 + 4.2), table="crate")


def stays(b):
    """Four tie-down stays from the drum's rim to the anchor heads on the first terrace."""
    for k in range(4):
        a = math.radians(45 + 90 * k)
        top = Vector((math.cos(a) * (CLAMP_R + 1.0), math.sin(a) * (CLAMP_R + 1.0), DECK + 36.0))
        foot = Vector((math.cos(a) * 157.0, math.sin(a) * 157.0, 32.0 + 15.0))
        b.tube([tuple(top), tuple(foot)], 3.4, mat=PAINT, tint=DARK, sides=18, solid=True)
        d = foot - top
        n = int(d.length / 12.0)
        q = d.to_track_quat("Z", "Y").to_euler()
        e = tuple(math.degrees(x) for x in q)
        for i in range(1, n):
            p = top + d * (i / n)
            b.cyl(4.0, 1.0, tuple(p), e, mat=PAINT, tint=ORANGE if i % 4 == 0 else STEEL, sides=18, solid=False,
                  vary=0)
        b.box((10.0, 10.0, 8.0), tuple(top + Vector((0, 0, 1.0))), (0, 0, math.degrees(a)), mat=PAINT, tint=STEEL)


def sleeve(b):
    """The guide sleeve round the stump: a lattice tower from the clamp to 300 m with three docking decks."""
    z0, z1 = DECK + 66.0, 300.0
    lattice_tower(b, (0, 0, z0), z1 - z0, 32.0, 20.0, sections=11, member=0.9, tint=STEEL)
    for z in (190.0, 245.0, 300.0):
        wz = 32.0 + (20.0 - 32.0) * (z - z0) / (z1 - z0)
        W = wz + 16.0
        b.box((W, W, 1.0), (0, 0, z - 0.5), mat=GRATE, tint=GREY, solid=False)
        b.box((W + 0.4, W + 0.4, 0.8), (0, 0, z - 1.2), mat=PAINT, tint=DARK, solid=False)
        h = W / 2 - 0.5
        corners = [(-h, -h, z), (h, -h, z), (h, h, z), (-h, h, z)]
        for i in range(4):
            railing(b, corners[i], corners[(i + 1) % 4], 1.1, spacing=2.5)
        for cx, cy, _ in corners:
            P.aviation_lamp(b, (cx, cy, z + 2.2), 0.8)
            b.box((0.2, 0.2, 2.0), (cx, cy, z + 1.0), mat=PAINT, tint=GREY, solid=False)
        b.box((8.0, 5.0, 3.2), (h - 5.0, 0, z + 1.6), mat=PANEL, tint=OFFWHITE, bevel=0.1)
        b.box((8.1, 5.1, 0.6), (h - 5.0, 0, z + 2.6), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        for k in range(4):
            a = math.radians(45 + 90 * k)
            b.beam((math.cos(a) * wz * 0.7, math.sin(a) * wz * 0.7, z - 1.0),
                   (math.cos(a) * W * 0.68, math.sin(a) * W * 0.68, z - 9.0), 0.6, 0.6, mat=PAINT, tint=STEEL)
    b.marker("loot", (0, 18.0, 300.2), table="military-crate")


def stump(b):
    """The snapped tether standing out of the clamp: plated collars, rails, and a frayed end at 440 m."""
    fr = P.tether(b, [(0, 0, DECK + 20.0), (0, 0, SNAP)], ref=(0, -1, 0), collar_every=55.0, collar_at=95.0,
                  band_every=15.0, band_at=7.5, damage=0.15)
    p, t, u, w, s = fr[-1]
    P.fray(b, p, t, u, w, length=30.0, spread=1.0, droop=0.45)
    # A collar knocked loose, hanging askew below the break; a last aviation lamp.
    b.lathe([(8.0, -2.0), (9.6, -1.0), (9.6, 1.0), (8.0, 2.0), (7.6, 2.0), (7.6, -2.0), (8.0, -2.0)],
            (1.5, -0.5, SNAP - 22.0), (14, -9, 0), mat=HULL, tint=OFFWHITE, sides=28, close=False, solid=False)
    P.aviation_lamp(b, (0, -9.8, SNAP - 40.0), 1.2)
    P.aviation_lamp(b, (0, 9.8, SNAP - 40.0), 1.2)
    b.marker("landmark", (0, 0, SNAP + 10.0), kind_of="tether-anchor", visible_km=60)


# ------------------------------------------------------------------------------------------------ the compound


def ramp(b):
    """The funicular ramp that carried climbers from the hangars' apron up to the clamp deck, a transporter with a
    climber stalled on it halfway."""
    x0, x1, z1 = 345.0, 62.0, DECK
    run = x0 - x1
    ang = math.degrees(math.atan2(z1, run))
    spans = 9
    for i in range(spans):
        xa = x0 - run * i / spans
        xb = x0 - run * (i + 1) / spans
        za, zb = z1 * i / spans, z1 * (i + 1) / spans
        mid = Vector(((xa + xb) / 2, 0, (za + zb) / 2))
        L = math.hypot(xa - xb, za - zb)
        b.box((L - 0.4, 18.0, 3.6), tuple(mid - Vector((0, 0, 1.8))), (0, -ang, 180), mat=CONCRETE, tint=CONC)
        for s in (-1, 1):
            b.box((L - 0.4, 0.5, 1.2), tuple(mid + Vector((0, s * 8.75, 0.6))), (0, -ang, 180), mat=CONCRETE,
                  tint=CONC, solid=False)
            b.box((L - 0.4, 1.2, 0.9), tuple(mid + Vector((0, s * 3.2, 0.45))), (0, -ang, 180), mat=PAINT, tint=STEEL,
                  solid=False)
        b.box((L - 0.4, 0.8, 0.5), tuple(mid + Vector((0, 0, 0.25))), (0, -ang, 180), mat=PAINT, tint=DARK, solid=False)
        if i < spans - 1:
            px, pz = xb, zb
            if pz > 8:
                b.box((7.0, 12.0, pz - 3.6), (px, 0, (pz - 3.6) / 2), mat=FORMWORK, tint=CONC)
                b.box((9.0, 19.0, 2.4), (px, 0, pz - 4.8), mat=CONCRETE, tint=CONC)
    for i in range(spans * 3):
        f = (i + 0.5) / (spans * 3)
        x = x0 - run * f
        z = z1 * f
        for s in (-1, 1):
            b.box((0.15, 0.15, 1.1), (x, s * 8.6, z + 1.2 + 0.55), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    for s in (-1, 1):
        b.beam((x0, s * 8.6, 1.2 + 1.1), (x1, s * 8.6, z1 + 1.2 + 1.1), 0.08, 0.08, mat=PAINT, tint=YELLOW)
    f = 0.45
    xc, zc = x0 - run * f, z1 * f
    with b.frame((xc, 0, zc + 0.2), (0, ang, 0)):
        b.box((26.0, 16.0, 3.0), (0, 0, 1.5), mat=PAINT, tint=YELLOW)
        for s in (-1, 1):
            b.box((24.0, 3.0, 2.2), (0, s * 6.5, -0.6), mat=PAINT, tint=DARK)
        stripes(b, (-13.0, -8.05, 0), (13.0, -8.05, 0), 0.8, width=1.0, at_z=1.6)
        b.piece("climber-car", (0, 0, 3.0), (0, 0, 90))
    b.marker("loot", (xc + 6.0, 0, zc + 3.6), table="crate")


def hangars(b):
    """Two climber hangars east of the block, doors toward it; the north one crushed by the falling cable."""
    # South hangar, standing: a climber on its cradle inside the door, another on the apron.
    P.vault_hall(b, (330.0, -140.0, 0), -90.0, 70.0, 150.0, 30.0, 22.0, door_w=48.0, door_h=40.0, door_open=0.6,
                 ruin=0.2, missing=0.12)
    b.piece("climber-car", (285.0, -140.0, 2.0), (0, 0, -90))
    b.box((26.0, 20.0, 2.0), (285.0, -140.0, 1.0), mat=PAINT, tint=YELLOW)
    b.marker("loot", (300.0, -125.0, 0.3), table="crate")
    b.marker("loot", (330.0, -160.0, 0.3), table="barrel")
    b.marker("door", (254.0, -140.0, 0.0), kind_of="hangar")
    # North hangar: broken in two by the cable, the middle bays flattened under it.
    P.vault_hall(b, (277.5, 140.0, 0), -90.0, 70.0, 45.0, 30.0, 22.0, door_w=48.0, door_h=40.0, door_open=0.2,
                 ruin=0.5, missing=0.3, ends=(True, False))
    P.vault_hall(b, (382.5, 140.0, 0), -90.0, 70.0, 45.0, 30.0, 22.0, ruin=0.6, missing=0.45, ends=(False, True))
    crushed_bays(b, 300.0, 360.0, 140.0)
    b.marker("loot", (330.0, 112.0, 0.4), table="military-crate")
    # The apron between them, lamps, haulers.
    b.box((120.0, 420.0, 0.3), (225.0, 0, 0.15), mat=ASPHALT, tint=(1, 1, 1), solid=False)
    for y in range(-190, 200, 40):
        b.box((100.0, 0.25, 0.02), (225.0, y, 0.31), mat=PAINT, tint=YELLOW, solid=False, vary=0, wear=0.7)
    for x, y in ((180.0, -100.0), (180.0, 100.0), (270.0, -60.0), (270.0, 60.0)):
        P.light_mast(b, (x, y, 0.3), 26.0)
    for x, y, yaw in ((200.0, -170.0, 10), (240.0, -60.0, 95), (205.0, 150.0, -30)):
        b.piece("hauler-truck", (x, y, 0.3), (0, 0, yaw))


def crushed_bays(b, x0, x1, yc, w=70.0, wall_h=30.0, rise=22.0):
    """The bays of a vaulted hall the cable fell through, between x0 and x1: ribs snapped and leaning, sheets of
    the vault draped over the rubble, wall stubs, a climber thrown on its side."""
    def zarc(x):
        return wall_h + rise * (1 - (2 * x / w) ** 2)
    # Rubble the cable lies on.
    for k in range(6):
        x = x0 + (x1 - x0) * (k + 0.5) / 6 + b.rng.uniform(-4, 4)
        y = yc + b.rng.uniform(-28, 28)
        P.rubble_heap(b, (x, y, 0), b.rng.uniform(9.0, 15.0), b.rng.uniform(4.0, 8.0), tint=CONC, count=14)
    # Ribs: stumps standing from both walls, broken off; long pieces leaning on the cable.
    for k in range(5):
        x = x0 + (x1 - x0) * (k + 0.5) / 5
        for s in (-1, 1):
            hgt = b.rng.uniform(8.0, 26.0)
            lean = b.rng.uniform(-10, 18) * -s
            with b.frame((x, yc + s * (w / 2 - 1.0), 0), (lean, 0, 0)):
                b.box((1.4, 2.0, hgt), (0, 0, hgt / 2), mat=PAINT, tint=srgb(0.6, 0.61, 0.61), solid=False)
                b.box((1.6, 1.4, wall_h * 0.4), (0, 0, wall_h * 0.2), mat=PANEL, tint=OFFWHITE, solid=False)
        if k % 2 == 0:
            ang = b.rng.uniform(25, 40)
            with b.frame((x + b.rng.uniform(-3, 3), yc - w / 2 + 2.0, 0), (0, 0, 90)):
                b.box((1.4, 2.0, 44.0), (0, 0, 0), (0, ang - 90 + 90, 0), mat=PAINT, tint=srgb(0.6, 0.61, 0.61),
                      solid=False)
    # Sheets of the vault lying tilted over the wreck.
    for k in range(7):
        x = x0 + (x1 - x0) * b.rng.random()
        y = yc + b.rng.uniform(-30, 30)
        arc = [(-9.0 + 18.0 * i / 6, 2.2 * (1 - (2 * i / 6 - 1) ** 2)) for i in range(7)]
        outline = [(px, pz + 0.9) for px, pz in arc] + [(px, pz) for px, pz in reversed(arc)]
        b.extrude(outline, b.rng.uniform(8.0, 12.0), (x, y, b.rng.uniform(3.0, 9.0)),
                  (b.rng.uniform(-35, 35), b.rng.uniform(-25, 25), b.rng.uniform(0, 180)),
                  mat=CORRUGATED if k % 3 else PANEL, tint=OFFWHITE, solid=False)
    # Stubs of the side walls.
    for s in (-1, 1):
        x = x0
        while x < x1 - 2:
            seg = b.rng.uniform(6.0, 12.0)
            hgt = b.rng.uniform(2.0, 13.0)
            b.box((seg - 0.5, 1.2, hgt), (x + seg / 2, yc + s * (w / 2 - 0.6), hgt / 2),
                  (b.rng.uniform(-6, 6) * s, 0, b.rng.uniform(-3, 3)), mat=PANEL, tint=OFFWHITE)
            x += seg
    P.debris(b, ((x0 + x1) / 2, yc), 60.0, count=80, size=(1.0, 7.0), tints=(OFFWHITE, srgb(0.6, 0.62, 0.64), DARK),
             mats=(PANEL, CORRUGATED, PAINT))
    P.scorch(b, ((x0 + x1) / 2, yc + 10.0, YARD_Z), 34.0)
    b.piece("climber-car", (x0 - 8.0, yc - w / 2 - 22.0, 6.2), (82.0, 0, 20.0))


def fallen_cable(b):
    """The fallen tether inside the compound: its frayed end at the block's foot, a curve through the north
    hangar, out through the wall to the junction where the laid pieces begin."""
    d = Vector((math.cos(CABLE_DIR), math.sin(CABLE_DIR), 0))
    ctrl = [(205.0, -62.0, P.TETHER_Z), (240.0, -26.0, P.TETHER_Z), (266.0, 0.0, P.TETHER_Z),
            (292.0, 50.0, P.TETHER_Z + 1.0), (316.0, 112.0, P.TETHER_Z + 5.0), (338.0, 168.0, P.TETHER_Z + 4.0),
            (386.0, 218.0, P.TETHER_Z), (448.0, 247.0, P.TETHER_Z), tuple(JUNCTION - d * 40.0), tuple(JUNCTION)]
    path = catmull(ctrl, per=5)
    path = P.resample(path, 3.0)
    L = sum((Vector(path[i + 1]) - Vector(path[i])).length for i in range(len(path) - 1))
    # Its twist must arrive at the junction at a multiple of 60 degrees so the first laid piece matches.
    phase = -(P.TETHER_TWIST * L) % 60.0
    fr = P.tether(b, path, ref=(0, 0, 1), phase=phase, collar_every=60.0, collar_at=(L - 30.0) % 60.0,
                  band_every=15.0, band_at=(L - 7.5) % 15.0, damage=0.35)
    p, t, u, w, s = fr[0]
    P.fray(b, p, -t, u, -w, length=24.0, spread=0.85, droop=0.32)
    # The furrow it ploughed and the earth it threw up.
    for side in (-1, 1):
        berm = []
        for p, t, u, w, s in fr[::3]:
            n = Vector((-t.y, t.x, 0)).normalized()
            q = p + n * side * 12.8
            berm.append((q.x, q.y, 0.0))
        P.mound_along(b, berm, width=8.0, height=2.6, tint=P.EARTH, rough=0.35)
    for p, t, u, w, s in fr[::4]:
        n = Vector((-t.y, t.x, 0)).normalized()
        a = p - t * 6.2 - n * 10.5
        c = p + t * 6.2 - n * 10.5
        e = p + t * 6.2 + n * 10.5
        f = p - t * 6.2 + n * 10.5
        b.slab([(a.x, a.y), (c.x, c.y), (e.x, e.y), (f.x, f.y)], 0.06, (0, 0, YARD_Z), mat=CONCRETE, tint=P.EARTH_DARK,
               solid=False, wear=0.9)
    P.scorch(b, (196.0, -72.0, YARD_Z + 0.04), 24.0)
    P.debris(b, (200.0, -70.0), 40.0, count=36, size=(0.8, 4.0))
    b.marker("loot", (214.0, -86.0, 0.3), table="crate")


def laid_line(b):
    """The first 2.6 km of the fallen tether outside, laid from the kit pieces as the game will lay the rest, with
    what it crushed: a pump station, a power line, a road."""
    d = Vector((math.cos(CABLE_DIR), math.sin(CABLE_DIR), 0))
    n = Vector((-d.y, d.x, 0))
    yaw = math.degrees(CABLE_DIR)
    count = int(LINE / P.TETHER_SEGMENT)
    for i in range(count):
        c = JUNCTION + d * (P.TETHER_SEGMENT * (i + 0.5))
        torn = i in (3, 11, 12, 19, 27, 34, 40)
        b.piece("tether-segment-torn" if torn else "tether-segment", (c.x, c.y, 0.0), (0, 0, yaw))
    # A pump station crushed at 700 m.
    c = JUNCTION + d * 700.0
    for s in (-1, 1):
        P.hall(b, tuple(c + n * s * 26.0 - d * 4.0), yaw, 44.0, 22.0, 12.0, tint=OFFWHITE, ruin=0.6,
               roof_units=False, windows=True, pilaster=6.0)
    P.rubble_heap(b, tuple(c + n * 9.0), 9.0, 4.0, count=14)
    P.rubble_heap(b, tuple(c - n * 9.0 + d * 6.0), 8.0, 3.5, count=12)
    for k in range(3):
        tank(b, tuple(c + n * (40.0 + k * 9.0) + d * 20.0), 3.6, 7.0, tint=OFFWHITE, roof="dome", ladder_side=None,
             band=TEAL)
    P.debris(b, (c.x, c.y), 30.0, count=24, size=(0.8, 3.5), tints=(OFFWHITE, DARK, OFFWHITE),
             mats=(PANEL, PAINT, CORRUGATED))
    # A power line it fell across at 1.5 km: pylons standing either side, the middle one knocked flat.
    c = JUNCTION + d * 1500.0
    for s, standing in ((-1, True), (0, False), (1, True)):
        q = c + n * s * 160.0 + d * (s * 10.0)
        if standing:
            lattice_tower(b, (q.x, q.y, 0), 36.0, 7.0, 2.0, rot=(0, 0, yaw), sections=7, member=0.22, tint=STEEL)
            for z, span in ((30.0, 7.0), (35.0, 5.0)):
                b.beam(tuple(q - d * span + Vector((0, 0, z))), tuple(q + d * span + Vector((0, 0, z))), 0.25, 0.25,
                       mat=PAINT, tint=STEEL)
        else:
            lattice_tower(b, (q.x + n.x * 6.0, q.y + n.y * 6.0, 1.0), 36.0, 7.0, 2.0, rot=(84, 0, yaw + 180),
                          sections=7, member=0.22, tint=STEEL)
    for s in (-1, 1):
        q0 = c + n * s * 160.0 + d * (s * 10.0)
        for k, z in enumerate((30.0, 35.0)):
            for e in (-1, 1):
                a = q0 + d * e * (6.0 if z < 33 else 4.0) + Vector((0, 0, z - 1.5))
                g = c + n * s * 14.0 + d * e * 6.0
                cable(b, tuple(a), (g.x, g.y, 0.3), sag=0.08, radius=0.05)
    # The old road it cut, at 2.2 km.
    c = JUNCTION + d * 2200.0
    road(b, [tuple(c - n * 400.0 - d * 30.0), tuple(c - n * 22.0), ], width=8.0)
    road(b, [tuple(c + n * 22.0), tuple(c + n * 400.0 + d * 30.0)], width=8.0)
    b.piece("hauler-truck", tuple(c - n * 30.0 + Vector((0, 0, 0.1))), (0, 0, yaw + 95))


def power_plant(b):
    """The plant that fed the climbers: reactor hall, turbine hall, two cooling towers, switchyard, a stack."""
    cooling_tower(b, (-390.0, 270.0, 0), height=118.0, base=46.0, waist=29.0, top=32.0, tint=CONC)
    cooling_tower(b, (-262.0, 362.0, 0), height=118.0, base=46.0, waist=29.0, top=32.0, tint=CONC)
    for x, y in ((-390.0, 270.0), (-262.0, 362.0)):
        for k in range(4):
            a = math.radians(90 * k + 20)
            P.aviation_lamp(b, (x + math.cos(a) * 31.5, y + math.sin(a) * 31.5, 118.8), 0.9)
    # Reactor: a domed drum with an annex.
    rx, ry = -300.0, 140.0
    b.cyl(32.0, 44.0, (rx, ry, 22.0), mat=PANEL, tint=OFFWHITE, sides=64)
    b.lathe([(32.0, 0)] + [(32.0 * math.cos(t), 18.0 * math.sin(t)) for t in
                           (math.pi / 2 * k / 8 for k in range(1, 8))] + [(0, 18.0)], (rx, ry, 44.0), mat=PANEL,
            tint=OFFWHITE, sides=64)
    b.cyl(32.3, 3.0, (rx, ry, 36.0), mat=PAINT, tint=TEAL, sides=64, solid=False, vary=0)
    b.cyl(32.25, 1.2, (rx, ry, 33.6), mat=PAINT, tint=ORANGE, sides=64, solid=False, vary=0)
    for k in range(16):
        a = 2 * math.pi * k / 16
        b.box((1.6, 1.4, 44.0), (rx + math.cos(a) * 32.4, ry + math.sin(a) * 32.4, 22.0), (0, 0, math.degrees(a)),
              mat=PANEL, tint=LIGHTGREY, solid=False, vary=0)
    P.hall(b, (rx + 44.0, ry - 36.0, 0), 0.0, 40.0, 30.0, 22.0, ruin=0.2, doors=[("front", 8.0)],
           bay_doors=[("front", -8.0, 8.0, 7.0)])
    P.door(b, (rx, ry - 32.4, 0), 0.0, marker="reactor")
    # Turbine hall and switchyard.
    P.hall(b, (-190.0, 196.0, 0), 0.0, 116.0, 44.0, 30.0, ruin=0.3, louvres=5,
           doors=[("front", 30.0), ("front", -30.0)], bay_doors=[("front", 0.0, 12.0, 10.0)], stair=(60.0, -16.0, 0))
    chimney(b, (-170.0, 268.0, 0), 150.0, 7.0, 4.5)
    for k in range(3):
        y0 = 196.0 - 10.0 + k * 10.0
        P.pipe_run(b, [(-248.0, y0, 9.0), (-268.0, y0 + 3.0, 9.0), (rx + 32.0, ry + 20.0 + k * 4.0, 9.0)], 1.0,
                   tint=OFFWHITE if k != 1 else ORANGE)
    sx, sy = -440.0, 40.0
    b.box((90.0, 70.0, 0.15), (sx, sy, 0.075), mat=CONCRETE, tint=srgb(0.55, 0.54, 0.51), solid=False)
    for i in range(3):
        b.piece("transformer", (sx - 30.0 + i * 30.0, sy + 18.0, 0))
        for j in range(2):
            b.piece("breaker", (sx - 30.0 + i * 30.0 + (j - 0.5) * 8.0, sy - 6.0, 0))
    for x in (sx - 20.0, sx + 20.0):
        b.piece("gantry", (x, sy - 24.0, 0), (0, 0, 90))
    fence(b, [(sx - 45, sy - 35, 0), (sx + 45, sy - 35, 0), (sx + 45, sy + 35, 0), (sx - 45, sy + 35, 0),
              (sx - 45, sy - 35, 0)], height=2.6, post=4.0)
    # A line from the switchyard to the anchor block on lattice pylons.
    prev = None
    for k, (x, y) in enumerate(((-440.0, -10.0), (-350.0, -20.0), (-260.0, -30.0), (-170.0, -36.0))):
        lattice_tower(b, (x, y, 0), 30.0, 6.0, 1.8, sections=6, member=0.2, tint=STEEL)
        b.beam((x, y - 6.0, 26.0), (x, y + 6.0, 26.0), 0.25, 0.25, mat=PAINT, tint=STEEL)
        if prev:
            for e in (-5.5, 0.0, 5.5):
                cable(b, (prev[0], prev[1] + e, 25.0), (x, y + e, 25.0), sag=0.03, radius=0.05)
        prev = (x, y)
    b.marker("loot", (-190.0, 196.0 - 25.0, 0.3), table="crate")


def operations(b):
    """The operations block by the south-west: offices over a control hall, workshops, fuel, containers."""
    block_building(b, (-250.0, -300.0, 0), (0, 0, 0), 64.0, 20.0, 24.0, floors=6, broken=0.4)
    P.hall(b, (-250.0, -254.0, 0), 0.0, 90.0, 60.0, 18.0, tint=OFFWHITE, band=TEAL, band2=ORANGE, ruin=0.25,
           doors=[("front", 30.0), ("right", 0.0)], bay_doors=[("front", -20.0, 10.0, 7.0)])
    b.box((64.4, 20.4, 1.2), (-250.0, -300.0, 17.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    P.stair_tower(b, (-282.0 - 7.6, -300.0, 0), 0.0, 24.0)
    for i, x in enumerate((-370.0, -330.0)):
        shed(b, (x, -230.0, 0), (0, 0, 90), 36.0, 24.0, 10.0, doors=[("front", 0.0, 8.0, 6.0)], ruin=0.15 + 0.2 * i)
    for k in range(3):
        tank(b, (-150.0 + k * 22.0, -360.0, 0), 8.0, 12.0, tint=OFFWHITE, band=ORANGE if k != 1 else TEAL,
             ladder_side=200.0)
    for k in range(10):
        x = -340.0 + (k % 5) * 7.0
        y = -330.0 + (k // 5) * 3.0
        b.piece("container-20", (x, y, 0), (0, 0, 90 + b.rng.uniform(-4, 4)),
                tint=(ORANGE, TEAL, OFFWHITE, srgb(0.5, 0.2, 0.15))[k % 4])
    for k in range(3):
        b.piece("container-20", (-333.0 + k * 7.0, -330.0, 2.6), (0, 0, 90 + b.rng.uniform(-6, 6)),
                tint=(TEAL, OFFWHITE, ORANGE)[k])
    b.marker("loot", (-250.0, -312.0, 0.2), table="crate")
    b.marker("loot", (-336.0, -334.0, 0.1), table="barrel")


def climber_works(b):
    """North of the block: the hall where climbers were built, its crane yard and parts stores."""
    from monkit.structures import gantry_crane
    cx, cy = 40.0, 400.0
    P.hall(b, (cx, cy, 0), 0.0, 210.0, 80.0, 44.0, tint=OFFWHITE, band=ORANGE, band2=TEAL, ruin=0.3, pilaster=10.5,
           bay_doors=[("front", -60.0, 30.0, 34.0), ("front", 0.0, 30.0, 34.0), ("front", 60.0, 30.0, 34.0)],
           doors=[("front", -88.0), ("front", 88.0), ("left", 10.0)], stair=(107.0, 20.0, 0))
    yard_y = cy - 40.0 - 55.0
    b.box((220.0, 110.0, 0.3), (cx, yard_y, 0.15), mat=CONCRETE, tint=srgb(0.56, 0.55, 0.52), solid=False)
    for x in (-60.0, 0.0, 60.0):
        for s in (-1, 1):
            b.beam((cx + x + s * 7.0, yard_y - 55.0, 0.35), (cx + x + s * 7.0, cy - 40.0, 0.35), 0.3, 0.3,
                   mat=RUST, tint=(1, 1, 1))
    gantry_crane(b, (cx - 30.0, yard_y + 5.0, 0.3), (0, 0, 0), span=52.0, height=34.0, length=14.0,
                 tint=srgb(0.82, 0.55, 0.12))
    gantry_crane(b, (cx + 55.0, yard_y - 20.0, 0.3), (0, 0, 0), span=52.0, height=34.0, length=14.0,
                 tint=srgb(0.82, 0.55, 0.12))
    b.box((24.0, 20.0, 2.0), (cx - 30.0, yard_y - 6.0, 1.3), mat=PAINT, tint=YELLOW)
    b.piece("climber-car", (cx - 30.0, yard_y - 6.0, 2.3), (0, 0, 180))
    for k in range(5):
        b.cyl(4.0, 12.0, (cx + 20.0 + k * 10.0, yard_y + 30.0, 4.3), (90, 0, 0), mat=PAINT,
              tint=OFFWHITE if k % 2 else srgb(0.58, 0.6, 0.62), sides=24)
    for k in range(8):
        x = cx - 95.0 + (k % 4) * 7.0
        y = yard_y + 20.0 + (k // 4) * 3.0
        b.piece("container-20", (x, y, 0.3), (0, 0, 90), tint=(ORANGE, TEAL, OFFWHITE, srgb(0.5, 0.2, 0.15))[k % 4])
    b.marker("loot", (cx + 40.0, cy - 30.0, 0.3), table="crate")
    b.marker("loot", (cx - 40.0, yard_y + 10.0, 0.3), table="barrel")


def landing_field(b):
    """South-east: the old landing field, its control tower, two small hangars and a fuel farm."""
    cx, cy = 300.0, -330.0
    b.box((200.0, 130.0, 0.3), (cx, cy, 0.15), mat=ASPHALT, tint=(1, 1, 1), solid=False)
    for x in (-50.0, 30.0):
        r = 16.0
        pts = [(cx + x + math.cos(a) * r, cy + math.sin(a) * r, 0.32) for a in
               (2 * math.pi * k / 32 for k in range(33))]
        for p, q in zip(pts, pts[1:]):
            b.beam(p, q, 0.6, 0.04, mat=PAINT, tint=YELLOW)
        b.box((12.0, 1.4, 0.04), (cx + x, cy, 0.33), (0, 0, 45), mat=PAINT, tint=OFFWHITE, solid=False, vary=0)
        b.box((12.0, 1.4, 0.04), (cx + x, cy, 0.33), (0, 0, -45), mat=PAINT, tint=OFFWHITE, solid=False, vary=0)
    # Control tower.
    tx, ty = 210.0, -260.0
    b.box((14.0, 14.0, 8.0), (tx, ty, 4.0), mat=PANEL, tint=OFFWHITE, bevel=0.1)
    b.cyl(3.5, 30.0, (tx, ty, 23.0), mat=CONCRETE, tint=CONC, sides=20)
    b.cyl(8.0, 5.0, (tx, ty, 40.5), mat=PANEL, tint=OFFWHITE, sides=16, radius2=9.0)
    b.cyl(9.3, 3.0, (tx, ty, 44.5), mat=GLASS, sides=16, radius2=9.6, solid=False)
    b.cyl(10.0, 1.0, (tx, ty, 46.5), mat=PAINT, tint=DARK, sides=16)
    b.cyl(0.2, 8.0, (tx, ty, 51.0), mat=PAINT, tint=GREY, sides=6, solid=False)
    P.aviation_lamp(b, (tx, ty, 55.3), 0.7)
    b.cyl(8.1, 1.0, (tx, ty, 38.6), mat=PAINT, tint=ORANGE, sides=16, solid=False, vary=0)
    P.door(b, (tx, ty - 7.05, 0), 0.0, marker="tower")
    b.marker("loot", (tx, ty, 44.0), table="crate")
    for k, x in enumerate((250.0, 330.0)):
        P.vault_hall(b, (x, -435.0, 0), 0.0, 40.0, 46.0, 10.0, 8.0, tint=OFFWHITE, band=TEAL, door_w=30.0,
                     door_h=14.0, door_open=0.3 + 0.3 * k, ruin=0.3, missing=0.2 * k)
    for k in range(4):
        tank(b, (390.0, -250.0 - k * 14.0, 0), 5.5, 8.0, tint=OFFWHITE, band=ORANGE, ladder_side=180.0, roof="dome")
    b.piece("hauler-truck", (260.0, -300.0, 0.3), (0, 0, 30))
    b.piece("hauler-truck", (345.0, -372.0, 0.3), (0, 0, -60))


def yards(b):
    """Workshops along the west ring road, offices north-east of the works, a vehicle park, a scrap yard."""
    for k in range(4):
        y = -230.0 + k * 52.0
        shed(b, (-470.0, y, 0), (0, 0, 90), 40.0, 26.0, 9.0, doors=[("front", 0.0, 8.0, 6.0)],
             tint=(srgb(0.62, 0.64, 0.63), OFFWHITE, srgb(0.55, 0.6, 0.58), OFFWHITE)[k], ruin=0.1 + 0.12 * k)
    for k in range(3):
        P.hall(b, (190.0 + k * 0.0, 330.0 + k * 34.0, 0), 0.0, 60.0, 22.0, 14.0, tint=OFFWHITE, band=TEAL,
               band2=ORANGE, ruin=0.2 + 0.15 * k, roof_units=True, pilaster=6.0,
               doors=[("front", -20.0), ("front", 20.0)])
    b.box((90.0, 60.0, 0.25), (-130.0, -420.0, 0.12), mat=ASPHALT, tint=(1, 1, 1), solid=False)
    for k in range(6):
        b.piece("hauler-truck", (-160.0 + (k % 3) * 16.0, -405.0 - (k // 3) * 22.0, 0.25),
                (0, 0, 90 + b.rng.uniform(-6, 6)))
    for k in range(3):
        P.rubble_heap(b, (-40.0 + k * 22.0, -450.0, 0), 8.0, 4.0, tint=srgb(0.5, 0.48, 0.45), count=10)
    P.debris(b, (-20.0, -450.0), 30.0, count=30, size=(0.8, 3.5), tints=(DARK, OFFWHITE, srgb(0.5, 0.3, 0.2)),
             mats=(PAINT, CORRUGATED, RUST))


def outbuildings(b):
    """Habitat blocks by the south gate, a substation and stores north-east, a tank depot west, silos north-west:
    the compound's ordinary fabric."""
    for k, (x, y, yaw) in enumerate(((70.0, -410.0, 0), (130.0, -440.0, 0), (70.0, -470.0, 0), (130.0, -500.0, 0))):
        block_building(b, (x, y, 0), (0, 0, yaw), 40.0, 14.0, 15.0, floors=4, broken=0.35 + 0.1 * k,
                       tint=(OFFWHITE, srgb(0.82, 0.8, 0.74))[k % 2])
        b.box((40.4, 14.4, 1.0), (x, y, 11.6), mat=PAINT, tint=(TEAL, ORANGE)[k % 2], solid=False, vary=0)
    sx, sy = 230.0, 262.0
    b.box((60.0, 40.0, 0.2), (sx, sy, YARD_Z + 0.05), mat=CONCRETE, tint=srgb(0.6, 0.59, 0.56), solid=False)
    for i in range(3):
        b.piece("transformer", (sx - 18.0 + i * 18.0, sy + 6.0, YARD_Z))
        b.piece("breaker", (sx - 18.0 + i * 18.0, sy - 8.0, YARD_Z))
    fence(b, [(sx - 30, sy - 20, 0), (sx + 30, sy - 20, 0), (sx + 30, sy + 20, 0), (sx - 30, sy + 20, 0),
              (sx - 30, sy - 20, 0)], height=2.6, post=4.0)
    P.hall(b, (300.0, 290.0, 0), 20.0, 44.0, 26.0, 16.0, tint=OFFWHITE, band=ORANGE, ruin=0.35, pilaster=6.0,
           doors=[("front", 10.0)], bay_doors=[("front", -10.0, 8.0, 7.0)])
    for k in range(4):
        tank(b, (-330.0 + k * 34.0, -120.0, 0), 13.0, 16.0, tint=OFFWHITE, band=(ORANGE, TEAL)[k % 2],
             ladder_side=270.0, roof="cone")
    from monkit.structures import silo
    silo(b, (-110.0, 330.0, 0), 6.0, 24.0, cone=6.0, legs=7.0, count=4, spacing=15.0)
    P.hall(b, (-110.0, 290.0, 0), 0.0, 60.0, 20.0, 12.0, tint=srgb(0.66, 0.68, 0.66), band=YELLOW, ruin=0.3,
           pilaster=6.0, doors=[("front", 0.0)])
    P.sign_board(b, (-40.0, -200.0, 0), 0.0, 14.0, 4.0)
    for k in range(8):
        b.piece("lamp-post", (-120.0 + k * 30.0, -205.0, YARD_Z), (0, 0, 0))


def perimeter(b):
    """The octagonal wall, its bastions, the south and west gates, and the breach where the cable went out."""
    pts = octagon()
    d = Vector((math.cos(CABLE_DIR), math.sin(CABLE_DIR), 0))
    # Where the cable broke out: the first crossing of its line with the wall, outward from the junction.
    cross, best = None, 1e9
    for k in range(8):
        a = Vector((*pts[k], 0))
        e = Vector((*pts[(k + 1) % 8], 0)) - a
        den = e.x * d.y - e.y * d.x
        if abs(den) < 1e-6:
            continue
        q = Vector((JUNCTION.x, JUNCTION.y, 0)) - a
        f = (q.x * d.y - q.y * d.x) / den
        g = (q.x * e.y - q.y * e.x) / den
        if -0.01 <= f <= 1.01 and g > -1 and g < best:
            best, cross = g, a + e * f
    for k in range(8):
        a = Vector((*pts[k], 0))
        c = Vector((*pts[(k + 1) % 8], 0))
        e = c - a
        mid_ang = 22.5 + 45 * k + 22.5
        broken = []
        if cross is not None:
            # The breach: the stretch of this side within 38 m of the crossing.
            f = max(0.0, min(1.0, (cross - a).dot(e) / e.length_squared))
            dist = (a + e * f - cross).length
            if dist < 38.0:
                half = math.sqrt(38.0 ** 2 - dist ** 2) / e.length
                broken.append((f - half, f + half))
        gate = None
        if abs(((mid_ang - 270) + 180) % 360 - 180) < 1:
            gate = "south"
        elif abs(((mid_ang - 180) + 180) % 360 - 180) < 1:
            gate = "west"
        if gate:
            gap = 22.0 / e.length
            P.fortress_wall(b, a, a + e * (0.5 - gap), height=12.0, thick=3.0, broken=broken)
            P.fortress_wall(b, a + e * (0.5 + gap), c, height=12.0, thick=3.0, broken=broken)
            gatehouse(b, a + e * 0.5, math.degrees(math.atan2(e.y, e.x)))
        else:
            P.fortress_wall(b, a, c, height=12.0, thick=3.0, broken=broken)
        if cross is not None and (a - cross).length < 48.0:
            # The bastion the cable struck: a broken drum, its top lying beside it.
            P.rubble_heap(b, (a.x, a.y, 0), 16.0, 7.0, tint=CONC, count=26)
            b.cyl(11.0, 8.0, (a.x, a.y, 4.0), (6, -4, 0), mat=FORMWORK, tint=CONC, sides=32)
            b.cyl(10.5, 9.0, (a.x + 18.0, a.y - 14.0, 9.0), (0, 92, 30), mat=FORMWORK, tint=CONC, sides=32)
            continue
        P.bastion(b, (pts[k][0], pts[k][1], 0), radius=10.0, height=22.0, band=ORANGE if k % 2 else TEAL,
                  door_toward=22.5 + 45 * k + 180)
    # Roads: south gate to the stair, a ring inside the wall, spurs to the hangars and the plant.
    Y = YARD_Z
    ring = [(0.92 * x, 0.92 * y, Y) for x, y in pts] + [(0.92 * pts[0][0], 0.92 * pts[0][1], Y)]
    road(b, ring, width=10.0, lines=True)
    road(b, [(0, -575.0, Y), (0, -190.0, Y)], width=14.0)
    road(b, [(0, -700.0, 0), (0, -576.0, 0)], width=14.0)
    road(b, [(-575.0, 0, Y), (-160.0, 0, Y)], width=12.0)
    road(b, [(-760.0, 0, 0), (-576.0, 0, 0)], width=12.0)
    road(b, [(-160.0, 0, Y), (-160.0, -200.0, Y)], width=10.0)
    road(b, [(0, -300.0, Y), (225.0, -300.0, Y), (225.0, -210.0, Y)], width=10.0)
    road(b, [(-160.0, 0, Y), (-160.0, 120.0, Y), (-240.0, 120.0, Y)], width=10.0)
    road(b, [(0, -190.0, Y), (0, -160.0, Y)], width=24.0, lines=False)
    # The yard: old paving over the whole compound, patched, weeds in its cracks.
    inner = [(0.985 * x * math.cos(math.radians(22.5)) / math.cos(math.radians(22.5)), 0.985 * y) for x, y in pts]
    b.slab([(x * 0.93, y * 0.93) for x, y in pts], YARD_Z, (0, 0, 0), mat=CONCRETE, tint=srgb(0.50, 0.49, 0.46),
           solid=False, wear=0.7)
    for k in range(70):
        a = b.rng.random() * math.tau
        r = 520.0 * math.sqrt(b.rng.random())
        x, y = math.cos(a) * r, math.sin(a) * r
        if abs(x) < 160 and abs(y) < 200:
            continue
        P.scorch(b, (x, y, YARD_Z - 0.01), b.rng.uniform(4.0, 16.0), tint=(P.MOSS, P.LEAF, srgb(0.4, 0.4, 0.37))[k % 3],
                 wobble=0.5)
    for k in range(10):
        y = -560.0 + k * 38.0
        for s in (-1, 1):
            b.piece("lamp-post", (s * 9.0, y, 0), (0, 0, 90 if s < 0 else -90))


def gatehouse(b, at, yaw):
    """A gate in the wall: two towers, a bridge with a control cabin over the roadway, barriers, a booth, signs."""
    with b.frame(tuple(at), (0, 0, yaw)):
        for s in (-1, 1):
            b.box((14.0, 14.0, 20.0), (s * 23.0, 0, 10.0), mat=FORMWORK, tint=CONC)
            b.box((14.6, 14.6, 1.6), (s * 23.0, 0, 15.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
            b.box((15.0, 15.0, 1.0), (s * 23.0, 0, 20.5), mat=CONCRETE, tint=CONC, solid=False)
            P.aviation_lamp(b, (s * 23.0, 0, 21.8), 0.7)
            P.door(b, (s * 23.0 - s * 7.0, 4.0, 0), 90.0 if s > 0 else -90.0, light=True)
        b.box((32.0, 8.0, 5.0), (0, 0, 17.5), mat=PANEL, tint=OFFWHITE, bevel=0.1)
        b.box((30.0, 0.2, 1.6), (0, -4.05, 18.0), mat=GLASS, solid=False, vary=0)
        b.box((30.0, 0.2, 1.6), (0, 4.05, 18.0), mat=GLASS, solid=False, vary=0)
        b.box((32.4, 8.4, 0.8), (0, 0, 20.3), mat=PAINT, tint=DARK, solid=False)
        stripes(b, (-16.0, -4.1, 0), (16.0, -4.1, 0), 1.2, width=1.2, at_z=14.6)
        for x in (-9.0, -3.0, 3.0, 9.0):
            b.piece("jersey-barrier", (x + b.rng.uniform(-1, 1), 12.0 + b.rng.uniform(-2, 2), 0),
                    (0, 0, b.rng.uniform(-10, 10)))
        b.box((3.0, 3.0, 2.8), (-12.0, 9.0, 1.4), mat=PANEL, tint=OFFWHITE, bevel=0.05)
        b.box((3.1, 0.1, 1.0), (-12.0, 7.45, 1.9), mat=GLASS, solid=False, vary=0)
        b.box((8.0, 0.25, 0.25), (-6.0, 9.0, 1.1), (0, -12, 0), mat=PAINT, tint=RED_STRIPE, solid=False)
        P.sign_board(b, (24.0, -16.0, 0), 0.0, 9.0, 3.0)


RED_STRIPE = srgb(0.75, 0.12, 0.1)


def build(b):
    b.set_pad(("circle", 660.0), ground="concrete", blend=60.0, clear=20.0)
    anchor_block(b)
    clamp(b)
    stays(b)
    sleeve(b)
    stump(b)
    ramp(b)
    hangars(b)
    fallen_cable(b)
    laid_line(b)
    power_plant(b)
    operations(b)
    climber_works(b)
    landing_field(b)
    yards(b)
    outbuildings(b)
    perimeter(b)
    b.zone("no-build", (0, 0, 0), radius=780.0)
    b.zone("radiation", (-300.0, 140.0, 20.0), radius=45.0, level=3)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 60,
                   "line": {"pieces": ["tether-segment", "tether-segment-torn"], "length": P.TETHER_SEGMENT,
                            "start": [round(JUNCTION.x + math.cos(CABLE_DIR) * LINE, 2), round(P.TETHER_Z, 2),
                                      round(-(JUNCTION.y + math.sin(CABLE_DIR) * LINE), 2)],
                            "heading": round(math.degrees(CABLE_DIR), 2), "continues_km": 40}})


SUN = (22.0, 130.0)
FOG = 0.00012
GROUND = 70000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (560, 80, 165), "distance": 7000, "yaw": -42, "pitch": 0.6, "lens": 135, "fog": 0.0001},
    {"name": "overview", "target": (260, 60, 40), "distance": 2300, "yaw": -32, "pitch": 24, "lens": 32,
     "fog": 0.00007},
    {"name": "foot", "target": (-1.5, -34.5, 92.5), "distance": 200, "yaw": 5, "pitch": -27, "lens": 13,
     "fog": 0.00005, "people": [((10.0, -200.0, YARD_Z), 200.0), ((12.5, -201.5, YARD_Z), 150.0)],
     "ship": ((0.0, -199.0, YARD_Z), 60.0)},
]
