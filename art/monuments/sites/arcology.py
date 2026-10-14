"""Arcology (MONUMENTS.md §5.5, landmark tier): the megablock of an old colony capital, a stepped ziggurat 600 m
square at its foot and 400 m tall. Ten tiers of homes and halls step back from the plaza; a cross of canyon streets
cuts it into four quarters, tied together by sky bridges at every other tier. Its terraces have gone wild: trees,
scrub and vines spill over their parapets round the colonists' greenhouses, habs, solar racks and wind turbines. A
crown of fins rings the glass dome of the assembly hall on top. The ruined capital lies round its plaza.
"""
import math

from mathutils import Vector

from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW, srgb, tank)
from monkit.structures import hab_module, road, solar_array, wind_turbine
from monkit.surfaces import ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "arcology"
TITLE = "Arcology"
SEED = 5401
BUILDER = {"chunk": 160.0, "max_edge": 18.0, "lod_min": (0.0, 2.0, 16.0)}
LODS = [450.0, 2800.0, 80000.0]

BASE = 600.0
TIERS = 10
TIER_H = 40.0
STEP = 28.0              # each tier steps back this far on every side
CLEFT = 20.0             # half-width of the canyon streets
FLOOR = 4.0
WALL = srgb(0.84, 0.82, 0.77)
WALL2 = srgb(0.74, 0.73, 0.70)
BANDS = (ORANGE, TEAL, srgb(0.72, 0.30, 0.18), srgb(0.30, 0.48, 0.52))


def mbox(b, size, at, rot=(0, 0, 0), mat=PANEL, tint=WALL, solid=False, vary=0.02, wear=None):
    """A box as one plain mesh (eight corners, never subdivided): for long facade strips."""
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
    v = [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
         (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz), (-sx, sy, sz)]
    f = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    return b.mesh(v, f, at, rot, mat=mat, tint=tint, solid=solid, smooth=False, vary=vary, wear=wear)


def width(k):
    return BASE - 2 * STEP * k


def facade(b, k, a, c, normal, z0, h, band, ground=False, burnt=False):
    """One face of a quarter block from `a` to `c` (on the nominal face plane, at z0), facing `normal`: recessed
    glazing, a ledge at every floor, fins every 24 m, dark broken panes, vines from the terrace above."""
    a, c = Vector(a), Vector(c)
    d = c - a
    L = d.length
    n = Vector(normal)
    yaw = math.degrees(math.atan2(d.y, d.x))
    mid = (a + c) / 2
    zs = z0 + (8.0 if ground else 0.0)
    glass_h = h - (zs - z0) - 1.0
    gm = mid - n * 1.2
    mbox(b, (L - 2.0, 0.3, glass_h), (gm.x, gm.y, zs + glass_h / 2), (0, 0, yaw), mat=GLASS, tint=(1, 1, 1))
    z = zs + FLOOR
    q = 0
    while z < z0 + h - 0.5:
        lm = mid + n * 0.0
        tint = band if q % 3 == 2 else WALL
        mbox(b, (L, 2.6, 0.5), (lm.x, lm.y, z), (0, 0, yaw), tint=tint, vary=0.03)
        z += FLOOR
        q += 1
    nf = max(1, int(L / 24.0))
    for i in range(nf + 1):
        p = a + d * (i / nf) + n * 0.1
        mbox(b, (1.4, 3.2, h), (p.x, p.y, z0 + h / 2), (0, 0, yaw), tint=WALL2)
    if ground:
        for i in range(int(L / 8.0) + 1):
            p = a + d * (i / max(1, int(L / 8.0))) + n * 0.4
            mbox(b, (1.6, 1.6, 8.0), (p.x, p.y, z0 + 4.0), (0, 0, yaw), mat=CONCRETE, tint=CONCRETE_T)
        dm = mid - n * 2.0
        mbox(b, (L - 2.0, 0.4, 7.5), (dm.x, dm.y, z0 + 3.8), (0, 0, yaw), mat=MATTE, tint=P.SOOT)
    # Broken and burnt panes.
    for _ in range(int(L * h / 1400) + (8 if burnt else 0)):
        f = b.rng.uniform(0.05, 0.95)
        p = a + d * f - n * 0.9
        pw, ph = b.rng.uniform(6.0, 24.0), FLOOR * b.rng.randint(1, 3) - 1.0
        zz = b.rng.uniform(zs + ph / 2, z0 + h - ph / 2 - 0.5)
        mbox(b, (pw, 0.3, ph), (p.x, p.y, zz), (0, 0, yaw), mat=MATTE, tint=P.SOOT, vary=0.05)
    if burnt:
        f = b.rng.uniform(0.2, 0.8)
        p = a + d * f + n * 0.2
        mbox(b, (b.rng.uniform(30, 60), 0.4, h * 0.9), (p.x, p.y, z0 + h * 0.5), (0, 0, yaw), mat=MATTE,
             tint=srgb(0.12, 0.11, 0.1), vary=0.05)
    # Vines down from the terrace above.
    for _ in range(int(L / 30)):
        f = b.rng.uniform(0.03, 0.97)
        p = a + d * f + n * 1.4
        vl = b.rng.uniform(6.0, h * 0.8)
        mbox(b, (b.rng.uniform(2.0, 7.0), 0.4, vl), (p.x, p.y, z0 + h - vl / 2), (0, 0, yaw), mat=MATTE,
             tint=b.rng.choice((P.LEAF, P.MOSS, srgb(0.26, 0.36, 0.15))), vary=0.08)


def quarters(b):
    """The tiers: four quarter blocks each, cut apart by the canyon streets, faced all round."""
    for k in range(TIERS):
        Wk = width(k)
        z0 = k * TIER_H
        half = Wk / 2
        q = half - CLEFT
        band = BANDS[k % len(BANDS)]
        for sx in (-1, 1):
            for sy in (-1, 1):
                cx, cy = sx * (CLEFT + q / 2), sy * (CLEFT + q / 2)
                b.box((q - 3.0, q - 3.0, TIER_H), (cx, cy, z0 + TIER_H / 2), mat=FORMWORK, tint=WALL2)
                xo, xi = sx * half, sx * CLEFT
                yo, yi = sy * half, sy * CLEFT
                burnt = (k in (3, 4) and sx > 0 and sy < 0) or (k == 6 and sx < 0 and sy > 0)
                facade(b, k, (xi, yo, 0), (xo, yo, 0), (0, sy, 0), z0, TIER_H, band, ground=k == 0, burnt=burnt)
                facade(b, k, (xo, yi, 0), (xo, yo, 0), (sx, 0, 0), z0, TIER_H, band, ground=k == 0, burnt=burnt)
                facade(b, k, (xi, yi, 0), (xi, yo, 0), (-sx, 0, 0), z0, TIER_H, band, ground=k == 0)
                facade(b, k, (xi, yi, 0), (xo, yi, 0), (0, -sy, 0), z0, TIER_H, band, ground=k == 0)
                # The tier's roof: a cornice and parapet round the quarter.
                top = z0 + TIER_H
                for (p0, p1) in (((xi, yo), (xo, yo)), ((xo, yi), (xo, yo)), ((xi, yi), (xi, yo)), ((xi, yi), (xo, yi))):
                    m = ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2)
                    L = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
                    yaw = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0]))
                    mbox(b, (L + 1.5, 1.2, 1.4), (m[0], m[1], top + 0.7), (0, 0, yaw), mat=CONCRETE, tint=WALL)
                    mbox(b, (L + 2.6, 3.4, 1.0), (m[0], m[1], top - 0.5), (0, 0, yaw), tint=band)
        if k < TIERS - 1:
            terraces(b, k)


def terraces(b, k):
    """The terrace left on top of tier k where tier k+1 steps back: soil, scrub and trees gone wild round the
    colonists' greenhouses, habs and solar racks; a turbine or two up high."""
    top = (k + 1) * TIER_H
    outer = width(k) / 2
    inner = width(k + 1) / 2
    depth = outer - inner
    for sx in (-1, 1):
        for sy in (-1, 1):
            # Two strips per quarter: along its outer x edge and its outer y edge.
            for axis in (0, 1):
                if axis == 0:
                    cx, cy = sx * (inner + depth / 2), sy * (CLEFT + (outer - CLEFT) / 2)
                    sz = (depth - 3.0, outer - CLEFT - 3.0)
                else:
                    cx, cy = sx * (CLEFT + (inner - CLEFT) / 2), sy * (inner + depth / 2)
                    sz = (inner - CLEFT - 3.0, depth - 3.0)
                if min(sz) < 4:
                    continue
                mbox(b, (sz[0], sz[1], 0.6), (cx, cy, top + 0.3), mat=MATTE,
                     tint=b.rng.choice((srgb(0.26, 0.32, 0.16), srgb(0.30, 0.34, 0.18), srgb(0.34, 0.32, 0.22))),
                     vary=0.05)
                area = sz[0] * sz[1]
                for _ in range(int(area / 380)):
                    x = cx + b.rng.uniform(-sz[0] / 2 + 3, sz[0] / 2 - 3)
                    y = cy + b.rng.uniform(-sz[1] / 2 + 3, sz[1] / 2 - 3)
                    if b.rng.random() < 0.3:
                        P.tree(b, (x, y, top + 0.5), b.rng.uniform(7.0, 15.0))
                    else:
                        P.rock(b, (x, y, top + 0.4), (b.rng.uniform(6, 16), b.rng.uniform(5, 12), b.rng.uniform(2.5, 6)),
                               (0, 0, b.rng.uniform(0, 360)), tint=b.rng.choice((P.LEAF, P.MOSS, srgb(0.3, 0.4, 0.18))),
                               n=3, rough=0.4, sink=0.1, mat=MATTE, solid=False, smooth=True, wear=0.2)
                # Colonist works on the terrace.
                roll = b.rng.random()
                x = cx + b.rng.uniform(-sz[0] * 0.25, sz[0] * 0.25)
                y = cy + b.rng.uniform(-sz[1] * 0.25, sz[1] * 0.25)
                yaw = 0.0 if sz[0] > sz[1] else 90.0
                if roll < 0.3:
                    greenhouse(b, (x, y, top + 0.6), yaw)
                elif roll < 0.5:
                    hab_module(b, (x, y, top + 0.6), (0, 0, yaw), length=10.0, radius=1.8,
                               band=b.rng.choice((ORANGE, TEAL)))
                elif roll < 0.65:
                    solar_array(b, (x, y, top + 0.6), (0, 0, yaw), rows=2, cols=5, broken=0.3)
                elif roll < 0.75:
                    tank(b, (x, y, top + 0.6), 3.5, 6.0, tint=OFFWHITE, roof="dome", ladder_side=None,
                         band=b.rng.choice((ORANGE, TEAL)))
    if k in (5, 7):
        for sx, sy in ((1, -1), (-1, 1)) if k == 5 else ((-1, -1),):
            x = sx * (inner + depth / 2)
            y = sy * (CLEFT + (outer - CLEFT) * 0.55)
            wind_turbine(b, (x, y, top + 0.6), height=46.0, blade=18.0, yaw=b.rng.uniform(0, 360),
                         broken=k == 7)


def greenhouse(b, at, yaw):
    """A colonists' greenhouse: a glass house on a frame, some panes gone, growth inside."""
    x, y, z = at
    with b.frame((x, y, z), (0, 0, yaw)):
        w, d, h = 22.0, 10.0, 5.0
        mbox(b, (w, d, 0.4), (0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        for i in range(7):
            xx = -w / 2 + w * i / 6
            for s in (-1, 1):
                mbox(b, (0.2, 0.2, h), (xx, s * d / 2, h / 2), mat=PAINT, tint=OFFWHITE)
            b.beam((xx, -d / 2, h), (xx, 0, h + 2.4), 0.2, 0.2, mat=PAINT, tint=OFFWHITE)
            b.beam((xx, d / 2, h), (xx, 0, h + 2.4), 0.2, 0.2, mat=PAINT, tint=OFFWHITE)
        for s in (-1, 1):
            if b.rng.random() > 0.3:
                mbox(b, (w, 0.1, h), (0, s * d / 2, h / 2), mat=GLASS, tint=(1, 1, 1))
            mbox(b, (w, math.hypot(d / 2, 2.4), 0.1), (0, s * d / 4, h + 1.2),
                 (math.degrees(math.atan2(2.4, d / 2)) * -s, 0, 0), mat=GLASS, tint=(1, 1, 1))
        P.bushes(b, (0, 0), 6.0, count=5, height=(1.5, 3.5), z=0.4)


def bridges(b):
    """Sky bridges across the canyon streets at every other tier: glazed decks between the quarters; one fallen."""
    for k in (1, 3, 5, 7):
        Wk = width(k + 1)
        z = k * TIER_H + 22.0
        for arm in range(4):
            for j in range(2 if k < 5 else 1):
                pos = CLEFT + 10.0 + (Wk / 2 - CLEFT - 20.0) * b.rng.uniform(0.15, 0.85)
                ang = 90.0 * arm
                a = math.radians(ang)
                cxy = Vector((math.cos(a) * pos, math.sin(a) * pos, 0))
                yaw = ang + 90.0
                fallen = k == 3 and arm == 2 and j == 0
                with b.frame((cxy.x, cxy.y, z if not fallen else 6.0), (0 if not fallen else 18.0, 0, yaw)):
                    span = 2 * CLEFT + 6.0
                    mbox(b, (span, 9.0, 1.2), (0, 0, 0), mat=CONCRETE, tint=WALL, solid=True)
                    mbox(b, (span, 9.4, 0.8), (0, 0, 6.4), tint=WALL2)
                    for s in (-1, 1):
                        mbox(b, (span - 1.0, 0.2, 5.0), (0, s * 4.4, 3.2), mat=GLASS, tint=(1, 1, 1))
                        mbox(b, (span, 0.8, 1.0), (0, s * 4.5, 0.6), tint=BANDS[(k + arm) % 4])
                    for i in range(9):
                        x = -span / 2 + span * i / 8
                        for s in (-1, 1):
                            mbox(b, (0.4, 0.5, 6.0), (x, s * 4.5, 3.4), mat=PAINT, tint=DARK)
                    if fallen:
                        P.rubble_heap(b, (0, 0, -6.0), 14.0, 6.0, tint=WALL, count=16)


def crown(b):
    """The crown: a platform over the top tier, the glass dome of the assembly hall, a ring of fins, the mast."""
    z = TIERS * TIER_H
    W = width(TIERS - 1)
    mbox(b, (W + 4.0, W + 4.0, 3.0), (0, 0, z + 1.5), mat=CONCRETE, tint=WALL, solid=True)
    mbox(b, (W + 5.0, W + 5.0, 1.4), (0, 0, z + 2.2), tint=ORANGE)
    R = 36.0
    prof = [(R, 0.0)] + [(R * math.cos(t), R * 0.85 * math.sin(t)) for t in (math.pi / 2 * i / 10 for i in range(1, 10))]
    b.lathe(prof + [(0.0, R * 0.85)], (0, 0, z + 3.0), mat=GLASS, tint=(1, 1, 1), sides=40)
    for i in range(16):
        a = 2 * math.pi * i / 16
        pts = [(math.cos(a) * r, math.sin(a) * r, z + 3.0 + hh) for r, hh in prof[::2] + [(0.0, R * 0.85)]]
        b.tube(pts, 0.5, mat=PAINT, tint=OFFWHITE, sides=6, cap=False)
    b.cyl(R + 1.0, 5.0, (0, 0, z + 5.5), mat=PANEL, tint=OFFWHITE, sides=40)
    for i in range(8):
        a = math.radians(22.5 + 45 * i)
        with b.frame((math.cos(a) * (R + 10.0), math.sin(a) * (R + 10.0), z + 3.0), (0, 0, math.degrees(a))):
            b.extrude([(-5.0, 0.0), (5.0, 0.0), (1.2, 74.0), (-0.6, 80.0), (-3.0, 40.0)], 2.4, (0, 0, 0), (0, 0, 90),
                      mat=HULL, tint=WALL)
            b.box((2.6, 10.2, 2.0), (0, 0, 20.0), mat=PAINT, tint=BANDS[i % 4], solid=False, vary=0)
            P.aviation_lamp(b, (0, -0.6, 80.8), 0.9)
    b.cyl(1.6, 90.0, (0, 0, z + 3.0 + R * 0.85 + 45.0), mat=PAINT, tint=GREY, sides=10, radius2=0.6)
    P.aviation_lamp(b, (0, 0, z + 3.0 + R * 0.85 + 91.0), 1.4, reach=4000.0)
    b.marker("landmark", (0, 0, z + 125.0), kind_of="arcology", visible_km=60)
    b.marker("loot", (8.0, 0, z + 3.2), table="vault-crate")
    b.marker("door", (0, -R - 1.2, z + 3.0), kind_of="assembly-hall")


def streets_and_plaza(b):
    """The canyon streets at ground level, the plaza round the foot, the portals into the streets, and the ruined
    capital round it."""
    half = BASE / 2
    b.box((BASE + 220.0, BASE + 220.0, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.6, 0.59, 0.56), solid=False)
    for arm in range(4):
        a = math.radians(90 * arm)
        # The street: asphalt down the canyon, wrecks, scrub, rubble.
        c = Vector((math.cos(a) * half / 2, math.sin(a) * half / 2, 0))
        road(b, [tuple(Vector((math.cos(a), math.sin(a), 0)) * 10.0 + Vector((0, 0, 0.3))),
                 tuple(Vector((math.cos(a), math.sin(a), 0)) * (half + 100.0) + Vector((0, 0, 0.3)))], width=24.0)
        for _ in range(5):
            r = b.rng.uniform(30.0, half - 10.0)
            side = b.rng.uniform(-12.0, 12.0)
            p = Vector((math.cos(a) * r - math.sin(a) * side, math.sin(a) * r + math.cos(a) * side, 0.3))
            b.piece("hauler-truck", tuple(p), (0, 0, 90 * arm + b.rng.uniform(-30, 30)))
        for _ in range(8):
            r = b.rng.uniform(30.0, half - 10.0)
            side = b.rng.choice((-1, 1)) * b.rng.uniform(12.0, 18.0)
            p = (math.cos(a) * r - math.sin(a) * side, math.sin(a) * r + math.cos(a) * side)
            P.bushes(b, p, 4.0, count=2, height=(1.5, 4.0), z=0.3)
        r = b.rng.uniform(80.0, 200.0)
        P.rubble_heap(b, (math.cos(a) * r, math.sin(a) * r, 0.3), 10.0, 5.0, tint=WALL, count=14)
        # The portal where the street enters the block.
        with b.frame((math.cos(a) * half, math.sin(a) * half, 0), (0, 0, 90 * arm - 90)):
            for s in (-1, 1):
                mbox(b, (6.0, 10.0, 34.0), (s * (CLEFT + 1.0), -2.0, 17.0), mat=FORMWORK, tint=WALL, solid=True)
            mbox(b, (2 * CLEFT + 14.0, 10.0, 6.0), (0, -2.0, 37.0), mat=FORMWORK, tint=WALL, solid=True)
            mbox(b, (2 * CLEFT + 14.5, 10.5, 2.0), (0, -2.0, 34.0), tint=BANDS[arm])
            P.sign_board(b, (0, -7.2, 31.0), 0.0, 20.0, 4.0, posts=False, rows=1, band=BANDS[arm])
            for s in (-1, 1):
                b.piece("lamp-post", (s * (CLEFT - 3.0), -12.0, 0.3), (0, 0, 0))
            P.door(b, (CLEFT + 9.0, -0.5, 0.3), 0.0, marker="arcology" if arm == 3 else None)
    # Plaza lamps, planters gone wild, a dry fountain basin before each face.
    for k in range(48):
        a = 2 * math.pi * (k + 0.5) / 48
        r = half + 60.0
        x, y = math.cos(a) * r * 1.0, math.sin(a) * r * 1.0
        x = max(-half - 60, min(half + 60, x * 1.25))
        y = max(-half - 60, min(half + 60, y * 1.25))
        if abs(x) < 40 or abs(y) < 40:
            continue
        if k % 3 == 0:
            b.piece("lamp-post", (x, y, 0.3), (0, 0, math.degrees(a) + 90))
        else:
            mbox(b, (8.0, 8.0, 1.0), (x, y, 0.8), mat=CONCRETE, tint=WALL)
            if k % 3 == 1:
                P.tree(b, (x, y, 1.3), b.rng.uniform(8.0, 14.0))
            else:
                P.bushes(b, (x, y), 3.0, count=2, height=(1.5, 3.0), z=1.3)
    # The ruined capital round the plaza.
    for k in range(26):
        a = 2 * math.pi * k / 26 + b.rng.uniform(-0.06, 0.06)
        r = b.rng.uniform(480.0, 760.0)
        x, y = math.cos(a) * r, math.sin(a) * r
        if min(abs(x), abs(y)) < 50:
            continue
        w, d = b.rng.uniform(30.0, 70.0), b.rng.uniform(20.0, 40.0)
        h = b.rng.uniform(10.0, 34.0)
        P.hall(b, (x, y, 0), math.degrees(a) + 90 + b.rng.choice((0, 90)), w, d, h,
               tint=b.rng.choice((WALL, OFFWHITE, srgb(0.78, 0.74, 0.66))), band=b.rng.choice(BANDS),
               band2=b.rng.choice(BANDS), ruin=b.rng.uniform(0.3, 0.8), pilaster=6.0, roof_units=b.rng.random() < 0.5,
               doors=[("front", 0.0)])
        if b.rng.random() < 0.4:
            P.rubble_heap(b, (x + b.rng.uniform(-30, 30), y + b.rng.uniform(-30, 30), 0), b.rng.uniform(8, 14),
                          b.rng.uniform(3, 6), tint=WALL, count=10)
        if b.rng.random() < 0.6:
            P.tree(b, (x + b.rng.uniform(-40, 40), y + b.rng.uniform(-40, 40), 0), b.rng.uniform(9.0, 16.0))
    ring = [(math.cos(a) * 880.0, math.sin(a) * 880.0, 0.0) for a in (2 * math.pi * i / 40 for i in range(41))]
    road(b, ring, width=14.0)
    city(b)
    for k in range(6):
        b.marker("loot", (b.rng.uniform(-200, 200), b.rng.choice((-1, 1)) * b.rng.uniform(5, 15), 0.3),
                 table=("crate", "barrel")[k % 2])


def ruin_block(b, at, yaw, w, d, h, tint):
    """A ruined city block, cheaply: a shell, window bands round it, a roof edge, a broken top on some."""
    x, y, _ = at
    with b.frame((x, y, 0), (0, 0, yaw)):
        broken = b.rng.random() < 0.45
        top = h * (b.rng.uniform(0.55, 0.85) if broken else 1.0)
        mbox(b, (w, d, top), (0, 0, top / 2), mat=CONCRETE, tint=tint, solid=True, vary=0.06)
        floors = int(top / 4.0)
        for f in range(1, floors):
            z = f * 4.0 - 1.6
            for (sx, sy, L, yw) in ((0, -d / 2, w, 0), (0, d / 2, w, 0), (-w / 2, 0, d, 90), (w / 2, 0, d, 90)):
                mbox(b, (L - 1.0, 0.3, 1.6), (sx, sy, z), (0, 0, yw), mat=MATTE if b.rng.random() < 0.4 else GLASS,
                     tint=P.SOOT, vary=0)
        mbox(b, (w + 0.6, d + 0.6, 0.8), (0, 0, top + 0.4), mat=CONCRETE, tint=tint, vary=0.04)
        if broken:
            P.rubble_heap(b, (b.rng.uniform(-w / 3, w / 3), -d / 2 - 8.0, 0), min(w, 14.0), 4.0, tint=tint, count=8)


def city(b):
    """The old capital round the plaza: a grid of streets and ruined blocks out to 1.4 km, trees in the gaps."""
    blocks = 0
    step = 110.0
    for i in range(-13, 14):
        for j in range(-13, 14):
            cx, cy = i * step, j * step
            r = math.hypot(cx, cy)
            if max(abs(cx), abs(cy)) < BASE / 2 + 170 or r > 1450 or (abs(cx) < 60 or abs(cy) < 60):
                continue
            if b.rng.random() < 0.18:
                P.tree(b, (cx + b.rng.uniform(-30, 30), cy + b.rng.uniform(-30, 30), 0), b.rng.uniform(9, 16))
                continue
            for q in range(b.rng.choice((1, 2, 2, 3))):
                w, d = b.rng.uniform(24.0, 60.0), b.rng.uniform(18.0, 40.0)
                h = b.rng.uniform(8.0, 30.0) * (1.6 - r / 1450.0)
                ox, oy = b.rng.uniform(-25, 25), b.rng.uniform(-25, 25)
                ruin_block(b, (cx + ox, cy + oy, 0), b.rng.choice((0, 90)) + b.rng.uniform(-3, 3), w, d, h,
                           b.rng.choice((WALL, OFFWHITE, srgb(0.78, 0.74, 0.66), srgb(0.70, 0.70, 0.68))))
                blocks += 1
            if b.rng.random() < 0.3:
                P.bushes(b, (cx + b.rng.uniform(-40, 40), cy + b.rng.uniform(-40, 40)), 8.0, count=3,
                         height=(2.0, 5.0))
    for k in range(-6, 7):
        y = k * step * 2 + step
        if abs(y) > 1350:
            continue
        L = math.sqrt(max(0.0, 1450.0 ** 2 - y * y))
        road(b, [(-L, y - step + 55.0, 0.0), (L, y - step + 55.0, 0.0)], width=10.0, lines=False)
        road(b, [(y - step + 55.0, -L, 0.0), (y - step + 55.0, L, 0.0)], width=10.0, lines=False)


def build(b):
    b.set_pad(("rect", BASE + 240.0, BASE + 240.0), ground="concrete", blend=60.0, clear=20.0)
    quarters(b)
    bridges(b)
    crown(b)
    streets_and_plaza(b)
    for k in (2, 5, 8):
        b.marker("loot", (CLEFT + 30.0, -(width(k) / 2 - 10.0), k * TIER_H + 0.5), table="military-crate")
    b.zone("no-build", (0, 0, 0), radius=950.0)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 60})


SUN = (26.0, 135.0)
FOG = 0.0001
GROUND = 80000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (0, 0, 190), "distance": 8000, "yaw": -28, "pitch": 1.4, "lens": 120, "fog": 0.00009},
    {"name": "overview", "target": (0, 0, 110), "distance": 2300, "yaw": -34, "pitch": 27, "lens": 30,
     "fog": 0.00005},
    {"name": "foot", "target": (-4.1, -163.2, 101.7), "distance": 200, "yaw": 4, "pitch": -30, "lens": 14,
     "fog": 0.00003, "people": [((6.0, -318.0, 0.3), 190.0), ((8.5, -320.0, 0.3), 140.0)],
     "ship": ((-8.0, -318.0, 0.3), 70.0)},
]
