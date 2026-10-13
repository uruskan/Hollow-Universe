"""Detention Block (MONUMENTS.md §5.3, medium tier): a Seedward penal compound in remote country, laid out on the
"telephone pole" plan: a central spine corridor runs north from the administration block, four cell wings branch off
it with exercise yards between them, and the control tower rises 30 m from the middle of the spine (the silhouette,
with the boiler-house chimney). A 6.5 m wall with an anti-climb coping and six guard towers closes it; vehicles come
in through the sally port. The east wall is breached where a truck went through; the north-west wing burnt. The
warden's office on the administration block's top floor is the violet room.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, fence, ladder, lamp, railing, rubble, srgb, stairs, tank)
from monkit.structures import chimney, road, shed, wall
from monkit.surfaces import ASPHALT, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PANEL, RUST

KEY = "detention-block"
TITLE = "Detention Block"
SEED = 3535
BUILDER = {"chunk": 48.0, "max_edge": 5.0}

HW = 100.0                       # half the wall's side
WALL_H = 6.5
CONC = srgb(0.78, 0.78, 0.75)    # the compound's grey concrete
STRIPE = srgb(0.86, 0.46, 0.14)  # penal orange
SPINE_X, SPINE_Y0, SPINE_Y1 = 0.0, -62.0, 74.0
WINGS = (-32.0, 32.0)            # wing centrelines (y)
FH = 3.4


def _clip(poly, a, b_, c):
    """The part of a convex polygon where a*x + b_*y <= c."""
    out = []
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        fp, fq = a * p[0] + b_ * p[1] - c, a * q[0] + b_ * q[1] - c
        if fp <= 0:
            out.append(p)
        if fp * fq < 0:
            t = fp / (fp - fq)
            out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return out


def holed_slab(b, poly, hole, z, thick, mat=CONCRETE, tint=CONC):
    """A convex floor slab with a rectangular hatch (x0, y0, x1, y1) cut out, as four convex slabs."""
    x0, y0, x1, y1 = hole
    for cuts in (((1, 0, x0),), ((-1, 0, -x1),), ((-1, 0, -x0), (1, 0, x1), (0, 1, y0)),
                 ((-1, 0, -x0), (1, 0, x1), (0, -1, -y1))):
        pc = poly
        for a, b_, c in cuts:
            pc = _clip(pc, a, b_, c)
        if len(pc) >= 3:
            b.slab(pc, thick, (0, 0, z), mat=mat, tint=tint)


def slits(b, a, c, z0, floors, step=3.2, normal=(0, -1, 0), start=1.6):
    """Narrow barred cell windows down a facade from a to c (on its outside face): a dark recess, a frame, a bar."""
    a = (a[0], a[1])
    dx, dy = c[0] - a[0], c[1] - a[1]
    length = math.hypot(dx, dy)
    yaw = math.degrees(math.atan2(dy, dx))
    nx, ny = normal[0] * 0.02, normal[1] * 0.02
    n = int((length - start) / step) + 1
    for fl in range(floors):
        z = z0 + fl * FH + 1.9
        for i in range(n):
            t = (start + i * step) / length
            if t > 0.98:
                break
            x, y = a[0] + dx * t + nx, a[1] + dy * t + ny
            b.box((0.5, 0.05, 1.5), (x, y, z), (0, 0, yaw), tint=srgb(0.62, 0.62, 0.6), solid=False, vary=0)
            b.box((0.3, 0.07, 1.2), (x + nx, y + ny, z), (0, 0, yaw), tint=srgb(0.06, 0.06, 0.07), solid=False, vary=0)


def perimeter(b):
    rng = b.rng
    breach = (HW, 14.0, 26.0)         # the east wall's gap: (x, y0, y1)
    for (a, c) in (((11.0, -HW), (HW, -HW)), ((HW, -HW), (HW, breach[1])), ((HW, breach[2]), (HW, HW)),
                   ((HW, HW), (-HW, HW)), ((-HW, HW), (-HW, -HW)), ((-HW, -HW), (-11.0, -HW))):
        wall(b, (a[0], a[1], 0), (c[0], c[1], 0), height=WALL_H, thick=0.6, panel=5.0, broken=0.0, tint=CONC,
             cap=False)
        b.tube([(a[0], a[1], WALL_H + 0.35), (c[0], c[1], WALL_H + 0.35)], 0.42, mat=CONCRETE, tint=CONC, sides=10)
        nseg = int(math.hypot(c[0] - a[0], c[1] - a[1]) / 3.0)
        yaw_w = math.degrees(math.atan2(c[1] - a[1], c[0] - a[0]))
        for i in range(nseg):
            t = (i + 0.5) / nseg
            b.piece("razor-coil", (a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t, WALL_H + 0.72), (0, 0, yaw_w))
        dx, dy = c[0] - a[0], c[1] - a[1]
        ln = math.hypot(dx, dy)
        nrm = (-dy / ln, dx / ln)          # inward for the wall's winding
        k = int(ln / 10.0)
        for i in range(1, k):
            t = i / k
            b.box((0.8, 1.6, WALL_H - 0.5), (a[0] + dx * t + nrm[0] * 1.0, a[1] + dy * t + nrm[1] * 1.0,
                                             (WALL_H - 0.5) / 2), (0, 0, math.degrees(math.atan2(dy, dx))),
                  mat=CONCRETE, tint=CONC)
        b.box((ln, 0.04, 0.5), ((a[0] + c[0]) / 2 - nrm[0] * 0.33, (a[1] + c[1]) / 2 - nrm[1] * 0.33, 1.4),
              (0, 0, math.degrees(math.atan2(dy, dx))), tint=STRIPE, solid=False, vary=0, wear=0.6)
        for i in range(1, int(ln / 30.0) + 1):
            t = i / (int(ln / 30.0) + 1)
            px, py = a[0] + dx * t + nrm[0] * 0.32, a[1] + dy * t + nrm[1] * 0.32
            b.piece("cctv", (px, py, WALL_H - 0.6), (0, 0, math.degrees(math.atan2(nrm[1], nrm[0])) - 90 + 180))
    # The breach: fallen panels, rubble, the truck that made it.
    for k in range(3):
        y = breach[1] + 2.0 + k * 4.0
        b.box((WALL_H - 0.5, 4.8, 0.6), (HW + 3.0 + k * 0.8, y, 0.5), (0, 8.0 - k * 9.0, 0.0), mat=FORMWORK,
              tint=CONC)
    rubble(b, (HW, 20.0, 0.0), 6.0, count=16, tint=CONC, max_size=1.6)
    b.piece("utility-truck", (HW - 4.0, 19.0, 0.4), (3.0, -4.0, 168.0), tint=srgb(0.2, 0.19, 0.18))
    pm.barricade(b, (HW - 9.0, 20.0, 0.0), (0, 0, 90), 9.0)
    # The sterile zone fence inside the wall and the patrol track.
    fence(b, [(-8.0, -HW + 9.0, 0), (-HW + 9.0, -HW + 9.0, 0), (-HW + 9.0, HW - 9.0, 0), (HW - 9.0, HW - 9.0, 0),
              (HW - 9.0, -HW + 9.0, 0), (8.0, -HW + 9.0, 0)], height=3.6, post=5.0)
    for (a, c) in (((-HW + 4.5, -HW + 4.5), (HW - 4.5, -HW + 4.5)), ((HW - 4.5, -HW + 4.5), (HW - 4.5, HW - 4.5)),
                   ((HW - 4.5, HW - 4.5), (-HW + 4.5, HW - 4.5)), ((-HW + 4.5, HW - 4.5), (-HW + 4.5, -HW + 4.5))):
        dx, dy = c[0] - a[0], c[1] - a[1]
        b.box((math.hypot(dx, dy) + 4.0, 4.0, 0.06), ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2, 0.03),
              (0, 0, math.degrees(math.atan2(dy, dx))), mat=MATTE, tint=srgb(0.5, 0.48, 0.44), solid=False, vary=0)


def guard_tower(b, x, y, H=12.0, face=0.0, cab_r=3.4):
    """A concrete guard tower: a square shaft with a door and slit windows, an octagonal cab of slanted glass with a
    catwalk round it, a pointed roof, a searchlight on its yoke."""
    with b.frame((x, y, 0), (0, 0, face)):
        s = 4.2
        b.box((s, s, H), (0, 0, H / 2), mat=FORMWORK, tint=CONC)
        b.box((s + 0.3, s + 0.3, 0.5), (0, 0, 0.25), mat=CONCRETE, tint=CONC)
        for z in (4.0, 8.0):
            b.box((0.3, 0.06, 1.0), (0, -s / 2 - 0.03, z), tint=srgb(0.06, 0.06, 0.07), solid=False, vary=0)
        b.box((s + 0.04, s + 0.04, 0.4), (0, 0, H - 2.5), tint=STRIPE, solid=False, vary=0, wear=0.6)
        ladder(b, (0.0, s / 2 + 0.25, 0), H + 0.35, (0, 0, 180), cage=True)
        octo = [((cab_r + 1.3) * math.cos(math.radians(22.5 + 45 * k)), (cab_r + 1.3) * math.sin(math.radians(22.5 + 45 * k)))
                for k in range(8)]
        holed_slab(b, octo, (-0.6, s / 2, 0.6, s / 2 + 1.0), H, 0.35)
        apo = cab_r * math.cos(math.pi / 8)
        side = 2 * cab_r * math.sin(math.pi / 8)
        for k in range(8):
            a = math.radians(45 * k)
            with b.frame((math.cos(a) * apo, math.sin(a) * apo, H + 0.35), (0, 0, math.degrees(a) + 90)):
                b.box((side, 0.25, 1.0), (0, 0, 0.5), mat=CONCRETE, tint=CONC)
                b.box((side - 0.15, 0.05, 1.7), (0, -0.2, 1.85), (-14, 0, 0), mat=GLASS, solid=False, vary=0)
                b.box((0.1, 0.3, 2.0), (side / 2, -0.1, 1.95), tint=DARK, solid=False, vary=0)
                b.collide_box((0, 0, 1.4), (side, 0.25, 2.8), (0, 0, 0))
        roof = [(cab_r + 0.9, 0.0), (cab_r + 0.9, 0.25), (1.0, 1.5), (0.0, 1.6)]
        b.lathe(roof, (0, 0, H + 3.15), (0, 0, 22.5), mat=PAINT, tint=srgb(0.36, 0.4, 0.38), sides=8)
        rr = cab_r + 1.2
        for k in range(8):
            a0, a1 = math.radians(22.5 + 45 * k), math.radians(22.5 + 45 * (k + 1))
            railing(b, (math.cos(a0) * rr, math.sin(a0) * rr, H + 0.35), (math.cos(a1) * rr, math.sin(a1) * rr, H + 0.35),
                    tint=DARK, toe=False)
        with b.frame((0, -cab_r - 0.6, H + 3.4)):
            b.box((0.12, 0.12, 0.6), (0, 0, 0.3), tint=DARK, solid=False)
            b.cyl(0.38, 0.7, (0, 0, 0.75), (90, 0, 0), tint=DARK, sides=12, solid=False)
            b.cyl(0.34, 0.04, (0, -0.37, 0.75), (90, 0, 0), mat=LAMP, tint=srgb(1.0, 0.95, 0.8), sides=12,
                  solid=False, vary=0)
            b.marker("light", (0, -0.6, 0.75), colour=[1.0, 0.95, 0.8], range=80.0, kind_of="searchlight")


def sally_port(b):
    """The vehicle lock through the front wall: two flanking blocks, the control room over the passage, an outer
    gate (one leaf hanging) and an inner gate (shut), a pedestrian door."""
    y = -HW
    with b.frame((0, y, 0)):
        for sx in (-1, 1):
            b.box((8.0, 18.0, 8.6), (sx * 7.0, 0.0, 4.3), mat=FORMWORK, tint=CONC)
            slits(b, (sx * 11.02, -6.0), (sx * 11.02, 6.0), 1.0, 2, step=3.0, normal=(sx, 0, 0), start=1.5)
            b.box((8.2, 18.2, 0.5), (sx * 7.0, 0.0, 8.85), mat=CONCRETE, tint=CONC)
        b.box((6.2, 18.0, 3.4), (0.0, 0.0, 6.9), mat=FORMWORK, tint=CONC)
        for yy in (-9.02, 9.02):
            b.box((5.4, 0.05, 1.3), (0.0, yy, 7.0), mat=GLASS, solid=False, vary=0)
            b.box((6.0, 0.1, 0.12), (0.0, yy * 1.002, 7.7), tint=DARK, solid=False, vary=0)
        pm.hazard_band(b, (-3.0, -9.05, 0), (3.0, -9.05, 0), 4.9, 0.6, width=0.6)
        b.box((6.0, 0.25, 5.2), (0.0, 8.6, 2.6), mat=PAINT, tint=srgb(0.4, 0.43, 0.41))
        with b.frame((-3.0, -8.8, 0.0), (0, 0, -62.0)):
            b.box((3.0, 0.2, 5.0), (1.5, 0, 2.5), mat=PAINT, tint=srgb(0.4, 0.43, 0.41))
            for k in range(5):
                b.box((0.08, 0.25, 4.8), (0.3 + k * 0.6, 0, 2.5), tint=DARK, solid=False, vary=0)
        with b.frame((0.2, -10.2, 0.05), (85.0, 0.0, 12.0)):
            b.box((3.0, 0.2, 5.0), (1.5, 0, 2.5), mat=PAINT, tint=srgb(0.4, 0.43, 0.41))
        b.box((1.2, 0.2, 2.3), (9.0, -9.05, 1.15), tint=srgb(0.35, 0.38, 0.36), solid=False)
        pm.code_plate(b, (6.0, -9.1, 3.2), (0, 0, 0), 2.6, 0.55, base=srgb(0.86, 0.86, 0.82), ink=DARK, text="DB-35")
        b.marker("door", (0.0, -9.0, 0.0), 180.0, kind_of="sally-port", width=6.0, height=5.0)
        lamp(b, (0.0, -9.2, 5.6), (-20, 0, 0), size=(0.7, 0.35, 0.3))
        # The pedestrian entrance: a canopy, a barred lobby door, an intercom, cameras, floodlights, wire.
        b.box((4.0, 2.4, 0.25), (9.0, -10.3, 2.9), mat=CONCRETE, tint=CONC, solid=False)
        for x in (7.2, 10.8):
            b.box((0.16, 0.16, 2.8), (x, -11.3, 1.4), tint=DARK, solid=False)
        b.piece("cell-front", (9.0, -9.25, 0.0), (0, 0, 0))
        b.box((0.3, 0.12, 0.45), (10.9, -9.1, 1.4), tint=DARK, solid=False, vary=0)
        b.box((0.12, 0.02, 0.08), (10.9, -9.17, 1.55), mat=LAMP, tint=srgb(0.3, 1.0, 0.5), solid=False, vary=0)
        for (x, yy, a) in ((-10.8, -9.1, 0.0), (10.8, -9.1, 0.0), (-3.2, -9.1, 0.0), (3.2, -9.1, 0.0)):
            b.piece("cctv", (x, yy, 7.6), (0, 0, a))
        for x in (-8.0, 8.0):
            lamp(b, (x, -9.3, 9.6), (-30, 0, 0), size=(0.9, 0.4, 0.4))
        for x0 in (-11.0, 3.0):
            for k in range(3):
                b.piece("razor-coil", (x0 + 1.5 + k * 3.0 - (0.0 if x0 < 0 else 0.0), -8.8, 9.1), (0, 0, 0))
        pm.code_plate(b, (-6.0, -9.1, 2.0), (0, 0, 0), 2.0, 0.6, base=STRIPE, ink=DARK, text="STOP")


def admin(b):
    w, d = 44.0, 16.0
    ax, ay = 0.0, -70.0
    win = lambda fl, xs, wd=2.2: [(x, wd, 1.5, 0.9, fl) for x in xs]
    info = pm.building(b, (ax, ay, 0), (0, 0, 0), w, d, floors=3, floor_h=3.6, mat=CONCRETE, tint=CONC,
                       openings={"front": [(0.0, 2.6, 2.6, 0, 0, {"kind_of": "admin", "state": "ajar"})]
                                          + win(0, (-18.0, -13.0, -8.0, 8.0, 13.0, 18.0))
                                          + win(1, (-18.0, -13.0, -8.0, -3.0, 3.0, 8.0, 13.0, 18.0))
                                          + win(2, (-18.0, -13.0, -8.0, -3.0, 3.0, 8.0, 13.0, 18.0)),
                                 "back": win(0, (-18.0, -12.0, 12.0, 18.0)) + win(1, (-18.0, -12.0, 12.0, 18.0))
                                         + win(2, (-18.0, -12.0, -6.0, 6.0, 12.0, 18.0)),
                                 "left": win(1, (-3.0, 3.0), 1.6) + win(2, (-3.0, 3.0), 1.6),
                                 "right": win(1, (-3.0, 3.0), 1.6) + win(2, (-3.0, 3.0), 1.6)},
                       holes=[(18.6, -7.4, 19.8, -2.3, 1), (18.6, 2.3, 19.8, 7.4, 2), (-20.6, 5.6, -19.6, 6.6, 3)],
                       band=(STRIPE, 3.7, 0.35), roof_units=3, glass=0.5, parapet=1.0)
    f0, f1, f2 = info["floor"]
    with b.frame((ax, ay, 0)):
        stairs(b, (19.2, -7.3, f0), (0, 0, 0), 1.2, f1 - f0, 5.0)
        stairs(b, (19.2, 7.3, f1), (0, 0, 180), 1.2, f2 - f1, 5.0)
        pm.roof_hatch(b, (-20.1, 6.1), (1.0, 1.0), f2, info["roof"])
        b.box((8.0, 4.0, 0.3), (0.0, -d / 2 - 2.0, 3.0), mat=CONCRETE, tint=CONC, solid=False)
        for x in (-3.6, 3.6):
            b.box((0.4, 0.4, 2.9), (x, -d / 2 - 3.6, 1.45), mat=CONCRETE, tint=CONC)
        pm.stencil(b, (-12.0, -d / 2 - 0.18, 8.6), (0, 0, 0), "DB-35", height=1.6, tint=DARK)
        # The guard room and armoury below, offices, the warden's office (the violet room) at the top.
        pm.wall(b, (-8.0, -d / 2 + 0.15, f0), (-8.0, d / 2 - 0.15, f0), 3.35, 0.25, [(4.0, 1.2, 2.2, 0, None)],
                mat=CONCRETE, tint=srgb(0.7, 0.7, 0.68))
        for x in (-20.4,):
            b.piece("weapon-rack", (x, 3.0, f0), (0, 0, -90))
        b.marker("loot", (-14.0, 5.6, f0), table="military-crate")
        b.piece("console", (-4.0, 5.6, f0), (0, 0, 180))
        for x in (-12.0, -4.0, 4.0, 12.0):
            b.piece("console", (x, -5.6, f1), (0, 0, 0))
        b.marker("loot", (8.0, 5.0, f1), table="crate")
        pm.wall(b, (-6.0, d / 2 - 0.15, f2), (-6.0, 0.0, f2), 3.35, 0.3, [], mat=CONCRETE, tint=srgb(0.68, 0.66, 0.6))
        pm.wall(b, (6.0, 0.0, f2), (6.0, d / 2 - 0.15, f2), 3.35, 0.3, [], mat=CONCRETE, tint=srgb(0.68, 0.66, 0.6))
        pm.wall(b, (-6.0, 0.0, f2), (6.0, 0.0, f2), 3.35, 0.3,
                [(6.0, 1.3, 2.3, 0, {"kind_of": "office", "chip": "violet", "cell": True, "room": "warden"})],
                mat=CONCRETE, tint=srgb(0.68, 0.66, 0.6), door_tint=srgb(0.35, 0.28, 0.4))
        b.box((3.0, 1.4, 0.8), (0.0, 5.0, f2 + 0.4), mat=MATTE, tint=srgb(0.38, 0.26, 0.18))
        b.box((1.2, 0.9, 1.6), (4.2, 6.8, f2 + 0.8), tint=srgb(0.3, 0.32, 0.33), bevel=0.03)
        b.marker("loot", (-3.0, 6.0, f2), table="vault-crate")
        b.marker("guard", (0.0, -4.0, f2), 180.0, role="reclaimer-heavy")


def spine(b):
    """The central corridor building from the administration block to the mess hall, with barred security gates
    along it, and the control tower rising from its middle."""
    length = SPINE_Y1 - SPINE_Y0
    cy = (SPINE_Y0 + SPINE_Y1) / 2
    ops = {"left": [], "right": [], "back": [(0.0, 2.4, 2.4, 0, 0, None)]}
    for wy in WINGS:
        for side in ("left", "right"):
            ops[side] += [(wy - cy, 2.4, 2.4, 0, 0, None), (wy - cy, 2.4, 2.4, 0, 1, None)]
    for side in ("left", "right"):
        for wy in (-12.0, 12.0):
            ops[side].append((wy - cy, 1.4, 2.3, 0, 0, {"kind_of": "yard", "state": "ajar"}))
        for yy in (-50.0, -14.0, 14.0, 50.0):
            ops[side].append((yy, 1.8, 1.2, 1.0, 1))
    pm.building(b, (SPINE_X, cy, 0), (0, 0, 0), 10.0, length, floors=2, floor_h=FH, mat=CONCRETE, tint=CONC,
                openings=ops, band=(STRIPE, 6.2, 0.35), roof_units=0, glass=0.3, plinth=0.3, parapet=0.8,
                holes=[(-1.2, -40.0, 1.2, -34.6, 1)])
    stairs(b, (SPINE_X, cy - 40.0, 0.3), (0, 0, 0), 1.6, FH + 0.25, 5.4)
    for sx in (-1, 1):
        for y0, y1 in ((-61.0, -35.0), (-29.0, -14.0), (-10.0, 10.0), (14.0, 29.0), (35.0, 73.0)):
            slits(b, (sx * 5.17, y0), (sx * 5.17, y1), 0.3, 2, step=4.0, normal=(sx, 0, 0), start=1.5)
    for k, gy in enumerate((-48.0, -18.0, 22.0, 50.0)):
        for z in (0.3, FH + 0.55):
            for x in (-3.2, 0.0, 3.2):
                b.piece("cell-front", (SPINE_X + x, gy, z), (0, 0, 0))
    # Roof vents along the spine.
    for k in range(9):
        b.box((1.6, 1.6, 0.9), (SPINE_X, SPINE_Y0 + 8.0 + k * 15.0, 2 * FH + 0.3 + 0.25 + 0.45), tint=LIGHTGREY,
              bevel=0.03)
    # The control tower: a square shaft through the spine's roof to an octagonal cab at 26 m.
    tx, ty, H = SPINE_X, 0.0, 26.0
    b.box((6.0, 6.0, H), (tx, ty, H / 2), mat=FORMWORK, tint=CONC)
    for z in (10.0, 16.0, 22.0):
        for (px, py, rot) in ((tx, ty - 3.02, 0), (tx, ty + 3.02, 180), (tx - 3.02, ty, -90), (tx + 3.02, ty, 90)):
            b.box((0.3, 0.06, 1.2), (px, py, z), (0, 0, rot), tint=srgb(0.06, 0.06, 0.07), solid=False, vary=0)
    for z, t in ((12.5, STRIPE), (19.0, STRIPE)):
        b.box((6.06, 6.06, 0.5), (tx, ty, z), tint=t, solid=False, vary=0, wear=0.6)
    R = 5.2
    octo = [((R + 1.6) * math.cos(math.radians(22.5 + 45 * k)), (R + 1.6) * math.sin(math.radians(22.5 + 45 * k)))
            for k in range(8)]
    with b.frame((tx, ty, 0)):
        holed_slab(b, octo, (0.6, 3.0, 1.8, 4.2), H, 0.45)
    apo, side = R * math.cos(math.pi / 8), 2 * R * math.sin(math.pi / 8)
    for k in range(8):
        a = math.radians(45 * k)
        with b.frame((tx + math.cos(a) * apo, ty + math.sin(a) * apo, H + 0.45), (0, 0, math.degrees(a) + 90)):
            b.box((side, 0.3, 1.0), (0, 0, 0.5), mat=CONCRETE, tint=CONC)
            if k != 3:
                b.box((side - 0.2, 0.05, 2.0), (0, -0.25, 2.05), (-15, 0, 0), mat=GLASS, solid=False, vary=0)
            else:
                b.box((side, 0.3, 2.2), (0, 0, 2.1), mat=CONCRETE, tint=CONC)
            b.box((0.12, 0.35, 2.3), (side / 2, -0.12, 2.1), tint=DARK, solid=False, vary=0)
            b.collide_box((0, 0, 1.6), (side, 0.3, 3.2), (0, 0, 0))
    b.lathe([(R + 1.2, 0.0), (R + 1.2, 0.35), (1.6, 2.2), (0.0, 2.3)], (tx, ty, H + 3.7), (0, 0, 22.5), mat=PAINT,
            tint=srgb(0.36, 0.4, 0.38), sides=8)
    rr = R + 1.5
    for k in range(8):
        a0, a1 = math.radians(22.5 + 45 * k), math.radians(22.5 + 45 * (k + 1))
        railing(b, (tx + math.cos(a0) * rr, ty + math.sin(a0) * rr, H + 0.45),
                (tx + math.cos(a1) * rr, ty + math.sin(a1) * rr, H + 0.45), tint=DARK, toe=False)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        with b.frame((tx + math.cos(a) * (R + 0.4), ty + math.sin(a) * (R + 0.4), H + 4.2), (0, 0, math.degrees(a) + 90)):
            b.cyl(0.45, 0.8, (0, -0.2, 0.4), (90, 0, 0), tint=DARK, sides=12, solid=False)
            b.cyl(0.4, 0.04, (0, -0.62, 0.4), (90, 0, 0), mat=LAMP, tint=srgb(1.0, 0.95, 0.8), sides=12, solid=False,
                  vary=0)
    b.cyl(0.06, 6.0, (tx, ty, H + 9.0), tint=GREY, sides=6, solid=False)
    pm.aviation_lamp(b, (tx, ty, H + 12.0), size=0.35)
    ladder(b, (tx + 1.2, ty + 3.3, 2 * FH + 0.55), H + 0.45 - 2 * FH - 0.55, (0, 0, 180), cage=True)
    b.marker("landmark", (tx, ty, H + 12.0), kind_of="control-tower", visible_km=7)


def wing(b, wy, side, burnt=False):
    """A two-storey cell wing off the spine: a corridor down its middle, cells either side behind barred fronts,
    slit windows outside, a stair core at its far end."""
    x0, x1 = 5.0, 50.0
    length = x1 - x0
    cx = side * (x0 + x1) / 2
    d = 14.0
    far, near = ("right", "left") if side > 0 else ("left", "right")
    pm.building(b, (cx, wy, 0), (0, 0, 0), length, d, floors=2, floor_h=FH, mat=CONCRETE, tint=CONC,
                openings={far: [(0.0, 1.3, 2.3, 0, 0, {"kind_of": "wing", "state": "shut"})],
                          near: [(0.0, 2.4, 2.4, 0, 0, None), (0.0, 2.4, 2.4, 0, 1, None)]},
                band=(STRIPE, 2 * FH - 0.4, 0.3), roof_units=2, glass=0.0, plinth=0.3, parapet=0.7,
                holes=([(-6.0, -4.0, 8.0, 5.0, 2)] if burnt else []))
    for fy, nrm in ((wy - d / 2 - 0.15, (0, -1, 0)), (wy + d / 2 + 0.15, (0, 1, 0))):
        slits(b, (side * (x0 + 1.0), fy), (side * (x1 - 2.0), fy), 0.3, 2, step=3.2, normal=nrm, start=1.6)
    pm.stencil(b, (side * (x1 + 0.17), wy, 5.0), (0, 0, 90 if side > 0 else -90),
               ("A" if wy < 0 else "C") + ("1" if side < 0 else "2"), height=2.4, tint=DARK)
    with b.frame((0, wy, 0.3)):
        for fl in range(2):
            z = fl * (FH + 0.25)
            n = int((length - 5.0) / 3.2)
            for i in range(n + 1):
                x = side * (x0 + 0.3 + i * 3.2)
                for sy in (-1, 1):
                    b.box((0.18, 5.4, FH - 0.05), (x, sy * 4.2, z + FH / 2), mat=CONCRETE, tint=srgb(0.72, 0.72, 0.7))
            for i in range(n):
                x = side * (x0 + 0.3 + (i + 0.5) * 3.2)
                for sy in (-1, 1):
                    b.piece("cell-front", (x, sy * 1.5, z), (0, 0, 0 if sy < 0 else 180))
                    if (i + fl + (sy > 0)) % 3 != 1:
                        b.piece("bunk", (x, sy * 5.5, z), (0, 0, 90 if sy > 0 else -90))
        b.marker("loot", (side * (x0 + 0.3 + 4.5 * 3.2), 4.6, 0.0), table="crate", room="cell")
        b.marker("loot", (side * (x0 + 0.3 + 9.5 * 3.2), -4.6, FH + 0.25), table=("crate", "military-crate")[wy > 0],
                 room="cell")
    # A steel stair up the far end to a fire door on the upper floor, a lamp over the ground-floor door.
    with b.frame((side * (x1 + 0.9), wy, 0), (0, 0, 0)):
        stairs(b, (0.0, -6.2, 0.0), (0, 0, 0), 1.0, FH + 0.55, 4.6, tint=STRIPE)
        pm.deck(b, -0.6, -1.6, 0.6, 0.4, FH + 0.55, rails=(("right",) if side > 0 else ("left",)), rail_tint=STRIPE)
        b.box((0.08, 1.2, 2.2), (-side * 0.62, -0.6, FH + 1.65), tint=srgb(0.36, 0.4, 0.38), solid=False)
    lamp(b, (side * (x1 + 0.3), wy, 2.9), (0, 0, 90 if side > 0 else -90), size=(0.45, 0.25, 0.2))
    if burnt:
        # Soot up the walls round the fire, a slab fallen into the upper floor, charred debris.
        for k in range(5):
            x = side * (x0 + 8.0 + k * 6.5)
            b.box((4.5, 0.03, 4.0 + (k % 2) * 1.5), (x, wy - d / 2 - 0.2, 4.6), mat=MATTE,
                  tint=srgb(0.07, 0.065, 0.06), solid=False, vary=0.2, wear=0.9)
        with b.frame((cx + 1.0, wy, FH + 0.5), (14.0, -9.0, 0.0)):
            b.box((13.0, 8.5, 0.25), (0, 0, 1.4), mat=CONCRETE, tint=srgb(0.32, 0.3, 0.28))
        rubble(b, (cx, wy, FH + 0.6), 4.0, count=10, tint=srgb(0.22, 0.2, 0.19), mat=MATTE, max_size=1.0)


def yards(b):
    """Exercise yards between the wings either side of the spine: a fence along the open side, a court with its
    hoops, benches, a shelter."""
    for side in (-1, 1):
        x0, x1 = side * 6.0, side * 50.0
        y0, y1 = WINGS[0] + 7.4, WINGS[1] - 7.4
        fence(b, [(x1, y0, 0), (x1, y1, 0)], height=4.2, post=3.5)
        cx = side * 30.0
        b.box((18.0, 30.0, 0.06), (cx, 0.0, 0.03), mat="M_Mon_Asphalt", tint=(1, 1, 1), solid=False)
        for (sx, sy, lx, ly) in ((0.0, -15.0, 18.0, 0.12), (0.0, 15.0, 18.0, 0.12), (-9.0, 0.0, 0.12, 30.0),
                                 (9.0, 0.0, 0.12, 30.0), (0.0, 0.0, 18.0, 0.12)):
            b.box((lx, ly, 0.012), (cx + sx, sy, 0.066), tint=OFFWHITE, solid=False, vary=0, wear=0.7)
        for sy in (-1, 1):
            with b.frame((cx, sy * 14.0, 0)):
                b.cyl(0.08, 3.6, (0, sy * 0.8, 1.8), tint=DARK, sides=8)
                b.box((1.8, 0.06, 1.1), (0, 0.0, 3.4), tint=OFFWHITE)
                b.cyl(0.24, 0.03, (0, -sy * 0.3, 3.0), tint=STRIPE, sides=10, cap=False, solid=False)
        for k in range(3):
            b.box((2.4, 0.5, 0.45), (side * 12.0, -10.0 + k * 10.0, 0.22), mat=CONCRETE, tint=CONC)
        pm.lean_to(b, (side * 44.0, 0.0, 0), (0, 0, -90 * side), 12.0, 4.0, h_front=2.8, h_back=3.2,
                   tint=srgb(0.6, 0.64, 0.62), missing=0.2)
        b.piece("lamp-post", (side * 20.0, -14.0, 0), (0, 0, 180))
    pm.tarp_shelter(b, (-36.0, 8.0, 0.0), (0, 0, 20), 5.0, 4.0, tint=pm.TARP_BLUE)
    b.piece("fire-barrel", (-33.0, 4.0, 0.0))
    pm.mark(b, (-8.0, -24.7, 1.6), (0, 0, 0), 2.0, "skull", pm.GRAFFITI[0])
    b.marker("guard", (-34.0, 6.0, 0.0), 180.0, role="reclaimer")


def services(b):
    """Mess hall at the head of the spine, boiler house and chimney, the workshop, a water tower."""
    pm.building(b, (0.0, SPINE_Y1 + 7.0, 0), (0, 0, 0), 34.0, 14.0, floors=1, floor_h=5.0, mat=CONCRETE, tint=CONC,
                openings={"front": [(0.0, 2.4, 2.4, 0, 0, None)],
                          "back": [(x, 2.4, 1.8, 1.6, 0) for x in (-12.0, -6.0, 6.0, 12.0)]
                                  + [(0.0, 2.4, 2.6, 0, 0, {"kind_of": "kitchen", "state": "ajar"})],
                          "left": [(0.0, 1.4, 2.3, 0, 0)], "right": [(2.0, 2.0, 1.4, 1.6, 0)]},
                band=(STRIPE, 4.2, 0.35), roof_units=4, glass=0.3)
    for k in range(6):
        b.box((2.6, 0.9, 0.8), (-10.0 + (k % 3) * 10.0, SPINE_Y1 + 5.0 + (k // 3) * 6.0, 0.7), mat=PAINT,
              tint=srgb(0.7, 0.72, 0.72))
    b.marker("loot", (12.0, SPINE_Y1 + 12.0, 0.3), table="crate")
    # The boiler house: old brick-red render, a coal bunker, the chimney with its bands.
    bx, by = -70.0, 68.0
    pm.building(b, (bx, by, 0), (0, 0, 0), 22.0, 14.0, floors=1, floor_h=8.0, mat=CONCRETE,
                tint=srgb(0.66, 0.46, 0.38),
                openings={"front": [(-4.0, 3.6, 4.2, 0, 0, {"kind_of": "boiler-house", "state": "ajar"}),
                                    (4.0, 1.8, 2.4, 3.6, 0), (8.0, 1.8, 2.4, 3.6, 0)],
                          "right": [(0.0, 1.8, 2.4, 3.6, 0)]},
                roof_units=1, glass=0.2, parapet=0.6)
    chimney(b, (bx - 14.0, by + 2.0, 0), height=42.0, r_base=2.4, r_top=1.5, tint=srgb(0.62, 0.42, 0.34),
            bands=(srgb(0.85, 0.85, 0.82), srgb(0.2, 0.2, 0.2)), ladder_on=True)
    b.markers = [m for m in b.markers if not (m["kind"] == "landmark" and m.get("kind_of") == "chimney")]
    for k in range(2):
        b.cyl(1.6, 7.0, (bx - 4.0 + k * 6.0, by + 1.0, 1.9), (0, 90, 0), tint=srgb(0.3, 0.3, 0.3), sides=16)
    b.box((8.0, 6.0, 3.0), (bx + 16.0, by + 2.0, 1.5), mat=CONCRETE, tint=srgb(0.5, 0.48, 0.45))
    pm.blob(b, (bx + 16.0, by + 2.0, 3.0), 3.4, 1.4, srgb(0.1, 0.1, 0.1), sides=10)
    pm.pipe_run(b, [(bx + 11.0, by - 4.0, 6.5), (-20.0, by - 4.0, 6.5), (-20.0, SPINE_Y1 - 6.0, 6.5),
                    (-5.2, SPINE_Y1 - 6.0, 6.5)], 0.35, tint=srgb(0.6, 0.6, 0.58), supports=8.0)
    shed(b, (70.0, 70.0, 0), (0, 0, 0), 28.0, 16.0, 7.0, doors=[("front", -6.0, 5.0, 4.5)],
         tint=srgb(0.6, 0.64, 0.62), ruin=0.12)
    b.marker("loot", (66.0, 72.0, 0.25), table="crate")
    tank(b, (76.0, -74.0, 0), 4.0, 6.0, tint=CONC, band=STRIPE, ladder_side=180.0, legs=12.0)


def outside(b):
    road(b, [(0.0, -126.0, 0.0), (0.0, -HW - 9.5, 0.0)], width=9.0)
    b.box((70.0, 14.0, 0.1), (-30.0, -112.0, 0.05), mat=ASPHALT, tint=(1, 1, 1))
    for k in range(12):
        b.box((0.15, 5.0, 0.012), (-62.0 + k * 5.5, -112.0, 0.106), tint=OFFWHITE, solid=False, vary=0, wear=0.7)
    b.piece("utility-truck", (-40.0, -112.0, 0), (0, 0, 92), tint=OFFWHITE)
    b.piece("jersey-barrier", (-4.0, -116.0, 0), (0, 0, 20))
    b.piece("jersey-barrier", (4.0, -114.0, 0), (0, 0, -15))
    for (x, y, a) in ((-HW - 1.5, -HW - 1.5, 225.0), (HW + 1.5, -HW - 1.5, 315.0), (HW + 1.5, HW + 1.5, 45.0),
                      (-HW - 1.5, HW + 1.5, 135.0), (-HW - 1.5, 0.0, 180.0), (HW + 1.5, -50.0, 0.0)):
        guard_tower(b, x, y, H=12.0, face=a + 90.0)
    b.marker("guard", (HW + 1.5, -HW - 1.5, 12.35), 315.0, role="reclaimer-sentry")


def build(b):
    b.set_pad(("rect", 226.0, 244.0), ground="gravel", blend=16.0, clear=6.0)
    perimeter(b)
    sally_port(b)
    admin(b)
    spine(b)
    for wy in WINGS:
        for side in (-1, 1):
            wing(b, wy, side, burnt=(wy > 0 and side < 0))
    yards(b)
    services(b)
    outside(b)
    b.zone("no-build", (0, 0, 0), size=(260.0, 270.0, 80.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-security", "spawn": "remote"})


SUN = (30.0, 220.0)
PEOPLE = [((3.0, -118.0, 0), 15.0), ((-30.0, 2.0, 0), 160.0)]
VIEWS = [
    {"name": "overview", "target": (0, 4, 8), "distance": 270, "yaw": 34, "pitch": 28, "lens": 32},
    {"name": "gate", "target": (0, -96, 7), "distance": 40, "yaw": 28, "pitch": -7, "lens": 24},
    {"name": "yard", "target": (0, 2, 14), "distance": 43.9, "yaw": 289.3, "pitch": -15.2, "lens": 22},
]
