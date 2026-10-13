"""Great Dam (MONUMENTS.md §5.5, landmark tier): a concrete arch dam 180 m tall across a mountain valley, its crest
an arc about 750 m long, curving into the reservoir. A gated overflow spillway at the crown sends its water down a
stepped chute into a stilling basin at the toe; two powerhouse halls stand either side of the basin, fed by six
penstocks down the face; four intake towers rise out of the reservoir on the upstream side with bridges to the
crest; the crest road carries two gantry cranes, gate hoist houses, elevator towers and a tracked crawler. The rock
abutments the arch thrusts into are part of the monument; the valley walls beyond them, the reservoir's water and
the river are terrain and water in the game, so they are built only for the look-dev renders (never exported).
"""
import math

from mathutils import Vector

from monkit import kit
from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW, cable,
                        ladder, lattice_tower, railing, srgb, stripes)
from monkit.structures import gantry_crane, road
from monkit.surfaces import ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "great-dam"
TITLE = "Great Dam"
SEED = 5301
BUILDER = {"chunk": 160.0, "max_edge": 14.0, "lod_min": (0.0, 2.0, 14.0)}
LODS = [450.0, 2800.0, 80000.0]

RC = 450.0               # radius of the arch's upstream face (its centre lies downstream, at (0, -RC))
CREST = 180.0            # crest height above the river bed
T_TOP, T_BASE = 14.0, 62.0
X0, X1, HV = 110.0, 510.0, 250.0   # the valley: floor half-width, where its walls top out, their height
EMBED = 6.0              # how deep the dam is keyed into the rock
WATER = 168.0            # reservoir level (preview)
DAM = srgb(0.72, 0.71, 0.68)
DAM_DARK = srgb(0.50, 0.49, 0.47)
ROCK_T = srgb(0.50, 0.47, 0.43)
GRASS_T = srgb(0.30, 0.34, 0.20)
SPILL = (-0.09, 0.09)    # the spillway's span, in radians of the arch
WEIR = CREST - 18.0      # its sill


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def valley(x):
    """The valley's cross-section: a flat floor, walls rising to HV."""
    return HV * smooth((abs(x) - X0) / (X1 - X0))


def thickness(z):
    d = max(0.0, CREST - z)
    return T_TOP + (T_BASE - T_TOP) * (d / CREST) ** 1.35


def arch(s, th, z):
    return Vector((s * math.sin(th), -RC + s * math.cos(th), z))


def up_face(th, z, off=0.0):
    return arch(RC + off, th, z)


def down_face(th, z, off=0.0):
    return arch(RC - thickness(z) - off, th, z)


def found(th):
    return valley(RC * math.sin(th)) - EMBED


def theta_end():
    lo, hi = 0.0, 1.4
    for _ in range(40):
        mid = (lo + hi) / 2
        if found(mid) + EMBED < CREST:
            lo = mid
        else:
            hi = mid
    return lo


TH0 = theta_end()


def terrain_height(x, y, seed=17):
    """The valley walls round the dam: the cross-section plus rough noise, calm where the dam keys in."""
    base = valley(x)
    near = abs(Vector((x, y + RC)).length - RC)
    calm = smooth(near / 140.0)
    n = (P.fbm(x / 220.0, y / 220.0, 0.3, seed, 4) - 0.5) * 70.0
    ridge = (P.fbm(x / 600.0, y / 600.0, 2.1, seed + 5, 3) - 0.5) * 90.0
    s = smooth((abs(x) - X0) / 80.0)
    fade = smooth((y + 2400.0) / 900.0) * smooth((3600.0 - y) / 900.0) * smooth((1500.0 - abs(x)) / 500.0)
    return max(0.0, (base + (n * calm + ridge * smooth((abs(x) - 200) / 300.0)) * s) * fade)


def terrain(b, x0, x1, y0, y1, step, skip=None):
    """A heightfield patch of the valley walls: steep faces in rock, gentle tops in grass; cells on the floor and
    cells inside `skip` (x0, x1, y0, y1) are left out."""
    nx = int(round((x1 - x0) / step))
    ny = int(round((y1 - y0) / step))
    hs = [[terrain_height(x0 + i * step, y0 + j * step) for i in range(nx + 1)] for j in range(ny + 1)]
    rock_v, rock_f, grass_v, grass_f = [], [], [], []
    for j in range(ny):
        for i in range(nx):
            xa, ya = x0 + i * step, y0 + j * step
            if skip and skip[0] <= xa + step / 2 <= skip[1] and skip[2] <= ya + step / 2 <= skip[3]:
                continue
            q = [hs[j][i], hs[j][i + 1], hs[j + 1][i + 1], hs[j + 1][i]]
            if max(q) < 0.6:
                continue
            pts = [(xa, ya, q[0] - 0.4), (xa + step, ya, q[1] - 0.4), (xa + step, ya + step, q[2] - 0.4),
                   (xa, ya + step, q[3] - 0.4)]
            slope = max(q) - min(q)
            zm = sum(q) / 4
            rocky = slope > step * 0.8 or zm < 70.0 + 60.0 * P.fbm(xa / 300.0, ya / 300.0, 0.7, 3, 2)
            vs, fs = (rock_v, rock_f) if rocky else (grass_v, grass_f)
            base = len(vs)
            vs += pts
            fs.append([base, base + 1, base + 2, base + 3])
    if rock_f:
        b.mesh(rock_v, rock_f, mat=CONCRETE, tint=ROCK_T, solid=False, smooth=True, wear=0.7)
    if grass_f:
        b.mesh(grass_v, grass_f, mat=MATTE, tint=GRASS_T, solid=False, smooth=True, wear=0.3)


# ------------------------------------------------------------------------------------------------ the dam


def dam_body(b):
    """The arch: lofted radial sections from abutment to abutment, keyed into the rock."""
    M = 64
    K = 14
    rings = []
    eps = 0.0005
    ths = sorted(set([-TH0 + 2 * TH0 * i / M for i in range(M + 1)] +
                     [SPILL[0] - eps, SPILL[0] + eps, SPILL[1] - eps, SPILL[1] + eps]))
    M = len(ths) - 1
    for th in ths:
        top = WEIR if SPILL[0] < th < SPILL[1] else CREST
        zf = min(found(th), top - 2.0)
        zs = [top - (top - zf) * k / K for k in range(K + 1)]
        ring = [tuple(up_face(th, z)) for z in zs] + [tuple(down_face(th, z)) for z in reversed(zs)]
        rings.append(ring)
    n = len(rings[0])
    verts = [v for r in rings for v in r]
    faces = []
    for i in range(M):
        for j in range(n):
            a, c = i * n + j, i * n + (j + 1) % n
            faces.append([a, c, c + n, a + n])
    faces.append(list(range(n)))
    faces.append([M * n + j for j in range(n)][::-1])
    b.mesh(verts, faces, mat=FORMWORK, tint=DAM, solid=False, smooth=False, wear=0.45)
    for i in range(24):
        th = -TH0 * 0.92 + 2 * TH0 * 0.92 * (i + 0.5) / 24
        zf = found(th) + EMBED
        zm = (CREST + zf) / 2
        c = (up_face(th, zm) + down_face(th, zm)) / 2
        b.collide_box(c, (RC * 2 * TH0 / 24, (up_face(th, zm) - down_face(th, zm)).length, CREST - zf),
                      (0, 0, math.degrees(-th)))
    # Monolith joints down the downstream face and lift lines across it; stains under the crest.
    for i in range(1, 30):
        th = -TH0 + 2 * TH0 * i / 30
        if SPILL[0] - 0.02 < th < SPILL[1] + 0.02:
            continue
        zf = found(th) + EMBED
        pts = [tuple(down_face(th, z, 0.25)) for z in [CREST - (CREST - zf) * k / 10 for k in range(11)]]
        b.tube(pts, 0.35, mat=PAINT, tint=DAM_DARK, sides=4, cap=False, vary=0)
    for z in range(20, int(CREST) - 4, 16):
        ths = [t for t in [-TH0 + 2 * TH0 * k / 80 for k in range(81)] if found(t) + EMBED < z - 2]
        runs, cur = [], []
        for t in ths:
            if SPILL[0] - 0.01 < t < SPILL[1] + 0.01:
                if cur:
                    runs.append(cur)
                cur = []
            else:
                cur.append(t)
        if cur:
            runs.append(cur)
        for run in runs:
            if len(run) > 1:
                b.tube([tuple(down_face(t, z, 0.2)) for t in run], 0.25, mat=PAINT, tint=DAM_DARK, sides=4, cap=False,
                       vary=0)
    for k in range(90):
        th = b.rng.uniform(-TH0 * 0.9, TH0 * 0.9)
        if SPILL[0] - 0.03 < th < SPILL[1] + 0.03:
            continue
        zf = found(th) + EMBED
        z0 = CREST - b.rng.uniform(2.0, 6.0)
        z1 = max(zf + 2, z0 - b.rng.uniform(12.0, 80.0))
        half = b.rng.uniform(0.6, 2.4) / RC
        verts, faces = [], []
        for q in range(6):
            z = z0 - (z0 - z1) * q / 5
            wq = half * (1.0 - 0.6 * q / 5)
            verts += [tuple(down_face(th - wq, z, 0.12)), tuple(down_face(th + wq, z, 0.12))]
        for q in range(5):
            faces.append([2 * q, 2 * q + 1, 2 * q + 3, 2 * q + 2])
        b.mesh(verts, faces, mat=MATTE, tint=srgb(0.46, 0.45, 0.42), solid=False, vary=0.12)


def crest(b):
    """The crest road: deck, parapets and a wave wall, lamps, rails for the gantry cranes, hoist houses over the
    spillway gates, elevator towers, end blocks, a crawler."""
    M = 48
    for i in range(M):
        t0 = -TH0 + 2 * TH0 * i / M
        t1 = -TH0 + 2 * TH0 * (i + 1) / M
        tm = (t0 + t1) / 2
        a0, a1 = up_face(t0, CREST), up_face(t1, CREST)
        d0, d1 = down_face(t0, CREST, 2.0), down_face(t1, CREST, 2.0)
        mid_up, mid_dn = up_face(tm, CREST), down_face(tm, CREST, 2.0)
        L = (a1 - a0).length + 0.3
        yaw = math.degrees(math.atan2(a1.y - a0.y, a1.x - a0.x))
        cm = (mid_up + mid_dn) / 2
        wdt = (mid_up - mid_dn).length
        b.box((L, wdt, 1.2), (cm.x, cm.y, CREST + 0.6), (0, 0, yaw), mat=CONCRETE, tint=DAM, solid=True)
        b.box((L, wdt - 5.0, 0.12), (cm.x, cm.y, CREST + 1.26), (0, 0, yaw), mat=ASPHALT, tint=(1, 1, 1), solid=False)
        wu = up_face(tm, CREST, -0.6)
        b.box((L, 1.2, 2.4), (wu.x, wu.y, CREST + 2.4), (0, 0, yaw), mat=CONCRETE, tint=DAM, solid=True)
        wd = down_face(tm, CREST, 2.0 - 0.4)
        b.box((L, 0.5, 1.15), (wd.x, wd.y, CREST + 1.8), (0, 0, yaw), mat=CONCRETE, tint=DAM, solid=True)
        # Corbels under the cantilevered downstream walkway.
        cb = down_face(tm, CREST, 1.0)
        b.box((1.2, 2.4, 3.0), (cb.x, cb.y, CREST - 1.0), (0, 0, yaw), mat=CONCRETE, tint=DAM, solid=False)
        for q, off in enumerate((-3.0, -1.6)):
            r = up_face(tm, CREST, off)
            b.box((L, 0.3, 0.3), (r.x, r.y, CREST + 1.4), (0, 0, yaw), mat=RUST, tint=(1, 1, 1), solid=False)
        if i % 2 == 0:
            lp = down_face(tm, CREST, 1.2)
            b.piece("lamp-post", (lp.x, lp.y, CREST + 1.2), (0, 0, yaw + 180))
    # The spillway's piers and gates at the crown, with the hoist house bridging them.
    bays = 6
    for k in range(bays + 1):
        th = SPILL[0] + (SPILL[1] - SPILL[0]) * k / bays
        p_up, p_dn = up_face(th, CREST - 14.0, 2.0), down_face(th, CREST - 14.0, -2.0)
        c = (p_up + p_dn) / 2
        yaw = math.degrees(-th)
        b.box((5.0, (p_up - p_dn).length + 10.0, 30.0), (c.x, c.y, CREST - 6.0), (0, 0, yaw), mat=FORMWORK, tint=DAM)
    for k in range(bays):
        th = SPILL[0] + (SPILL[1] - SPILL[0]) * (k + 0.5) / bays
        g = up_face(th, CREST - 9.0, -6.0)
        yaw = math.degrees(-th)
        open_ = k in (1, 4)
        gw = RC * (SPILL[1] - SPILL[0]) / bays - 5.4
        with b.frame((g.x, g.y, WEIR + 9.0 + (7.0 if open_ else 0.0)), (0, 0, yaw)):
            b.box((gw, 1.2, 18.0), (0, 0, 0), (-12, 0, 0), mat=RUST if k == 4 else PAINT,
                  tint=(1, 1, 1) if k == 4 else YELLOW, solid=False)
            for x in (-gw / 2 + 0.6, gw / 2 - 0.6):
                b.beam((x, 0.8, 7.0), (x, 14.0, -2.0), 0.9, 1.4, mat=PAINT, tint=DARK)
                b.beam((x, 0.8, -7.0), (x, 14.0, -2.0), 0.9, 1.4, mat=PAINT, tint=DARK)
    hoist_th = (SPILL[0] + SPILL[1]) / 2
    with b.frame(tuple(up_face(hoist_th, CREST, -6.0)), (0, 0, math.degrees(-hoist_th))):
        span = RC * (SPILL[1] - SPILL[0]) + 8.0
        b.box((span, 9.0, 7.0), (0, 0, 12.0), mat=PANEL, tint=OFFWHITE, bevel=0.2)
        b.box((span + 0.4, 9.4, 1.2), (0, 0, 14.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        b.box((span + 0.6, 9.6, 0.8), (0, 0, 15.9), mat=PAINT, tint=DARK, solid=False)
        b.box((span - 4.0, 0.2, 1.6), (0, -4.6, 11.0), mat=GLASS, solid=False, vary=0)
        for x in (-span / 2 + 2.0, span / 2 - 2.0):
            b.box((4.0, 8.0, 8.0), (x, 0, 4.0), mat=FORMWORK, tint=DAM)
        for q in range(bays):
            x = -span / 2 + 4.0 + (span - 8.0) * (q + 0.5) / bays
            cable(b, (x, 0, 8.5), (x, 2.0, -6.0), sag=0.0, radius=0.08)
        P.door(b, (-span / 2 + 2.0, -4.0, 0.0), 0.0, light=True)
    # Two gantry cranes on the crest; the crawler parked between them.
    for th, yaw_off in ((-0.32, 0.0), (0.42, 0.0)):
        c = (up_face(th, CREST) + down_face(th, CREST, 2.0)) / 2
        gantry_crane(b, (c.x, c.y, CREST + 1.2), (0, 0, math.degrees(-th) + 90 + yaw_off), span=16.0, height=22.0,
                     length=10.0, tint=srgb(0.86, 0.62, 0.12))
    c = (up_face(-0.12, CREST) + down_face(-0.12, CREST, 2.0)) / 2
    crawler(b, (c.x, c.y, CREST + 1.25), math.degrees(0.12) + 90)
    # Elevator towers on the downstream face either side of the spillway, standing above the crest.
    for th in (SPILL[0] - 0.035, SPILL[1] + 0.035):
        p = down_face(th, CREST, -2.0)
        yaw = math.degrees(-th)
        zf = 40.0
        with b.frame((p.x, p.y, 0), (0, 0, yaw)):
            b.box((14.0, 12.0, CREST + 28.0 - zf), (0, -5.0, zf + (CREST + 28.0 - zf) / 2), mat=PANEL,
                  tint=OFFWHITE)
            for q in range(4):
                b.box((14.4, 12.4, 1.4), (0, -5.0, CREST - 40.0 + q * 18.0), mat=PAINT, tint=ORANGE if q % 2 else TEAL,
                      solid=False, vary=0)
            b.box((15.0, 13.0, 1.6), (0, -5.0, CREST + 28.8), mat=PAINT, tint=DARK, solid=False)
            b.box((0.2, 1.6, CREST + 18.0 - zf), (-7.1, -9.0, zf + (CREST + 18.0 - zf) / 2), mat=GLASS, solid=False,
                  vary=0)
            P.aviation_lamp(b, (0, -5.0, CREST + 30.4), 0.9)
            P.door(b, (0, -11.05, CREST + 1.2), 0.0, marker="gallery")
    # End blocks where the crest meets the rock; control houses on them.
    for sgn in (-1, 1):
        th = sgn * TH0
        p = (up_face(th, CREST) + down_face(th, CREST)) / 2
        yaw = math.degrees(-th)
        with b.frame((p.x, p.y, CREST), (0, 0, yaw)):
            b.box((30.0, 40.0, 12.0), (sgn * 10.0, 0, -4.0), mat=FORMWORK, tint=DAM)
            P.hall(b, (sgn * 34.0, 6.0, 0), 0.0, 30.0, 18.0, 9.0, tint=OFFWHITE, band=ORANGE, ruin=0.3, pilaster=6.0,
                   doors=[("front", 0.0)], roof_units=True)
            P.aviation_lamp(b, (sgn * 34.0, 6.0, 12.5), 0.8)
    b.marker("landmark", (0, 0, CREST + 40.0), kind_of="great-dam", visible_km=40)


def crawler(b, at, yaw):
    """The crest crawler: a tracked gate-handling machine, cab and boom, stopped where it last worked."""
    with b.frame(at, (0, 0, yaw)):
        for s in (-1, 1):
            b.box((16.0, 3.0, 3.2), (0, s * 3.6, 1.6), mat=PAINT, tint=DARK, bevel=0.3)
            for k in range(7):
                b.cyl(1.2, 3.2, (-6.6 + k * 2.2, s * 3.6, 1.3), (90, 0, 0), mat=PAINT, tint=BLACK, sides=12,
                      solid=False)
        b.box((14.0, 9.0, 2.4), (0, 0, 4.4), mat=PAINT, tint=YELLOW, bevel=0.15)
        b.box((5.0, 4.0, 3.4), (4.0, -2.0, 7.3), mat=PAINT, tint=YELLOW, bevel=0.15)
        b.box((0.1, 3.4, 1.6), (6.55, -2.0, 7.8), mat=GLASS, solid=False, vary=0)
        b.box((6.0, 6.0, 3.0), (-4.0, 1.0, 7.1), mat=PAINT, tint=srgb(0.3, 0.3, 0.3))
        b.beam((0.0, 2.0, 6.0), (22.0, 2.0, 18.0), 1.4, 1.6, mat=PAINT, tint=YELLOW)
        cable(b, (22.0, 2.0, 18.0), (22.0, 2.0, 4.0), sag=0.0, radius=0.06)
        b.box((1.6, 1.0, 1.2), (22.0, 2.0, 3.6), mat=PAINT, tint=DARK, solid=False)
        stripes(b, (-7.0, -4.55, 0), (7.0, -4.55, 0), 0.6, width=0.7, at_z=3.6)


def spillway(b):
    """The stepped chute down the downstream face below the gates, training walls, and the stilling basin with
    its baffle blocks and end sill at the toe."""
    th0, th1 = SPILL
    nseg = 4
    z = WEIR
    while z > 4.0:
        z0 = max(0.0, z - 6.0)
        for k in range(nseg):
            ta = th0 + (th1 - th0) * k / nseg
            tb = th0 + (th1 - th0) * (k + 1) / nseg
            tm = (ta + tb) / 2
            outer = down_face(tm, z0)
            inner = down_face(tm, z)
            depth = (outer - inner).length + 1.0
            c = (outer + inner) / 2
            L = RC * (tb - ta) * 0.95
            b.box((L, depth, z - z0), (c.x, c.y, (z + z0) / 2), (0, 0, math.degrees(-tm)), mat=CONCRETE, tint=DAM,
                  solid=False)
        z -= 6.0
    for th in (th0 - 0.006, th1 + 0.006):
        pts = [down_face(th, max(0.0, WEIR - k * 14.0), 2.0) for k in range(13)]
        for a, c in zip(pts, pts[1:]):
            b.beam(tuple(a + Vector((0, 0, 3.0))), tuple(c + Vector((0, 0, 3.0))), 5.0, 9.0, mat=FORMWORK, tint=DAM,
                   solid=True)
    # Stilling basin at the toe in front of the chute.
    tm = (th0 + th1) / 2
    toe = down_face(tm, 0.0)
    yaw = math.degrees(-tm)
    with b.frame((toe.x, toe.y, 0), (0, 0, yaw)):
        b.box((96.0, 120.0, 2.0), (0, -60.0, 1.0), mat=CONCRETE, tint=DAM_DARK, solid=True)
        for s in (-1, 1):
            b.box((5.0, 126.0, 16.0), (s * 50.0, -60.0, 8.0), mat=FORMWORK, tint=DAM)
            railing(b, (s * 50.0, -120.0, 16.0), (s * 50.0, -2.0, 16.0), 1.1, spacing=2.4, toe=False)
        for row in range(2):
            for k in range(8):
                b.box((5.0, 4.0, 4.0), (-38.0 + k * 10.8 + row * 5.4, -42.0 - row * 14.0, 4.0), mat=CONCRETE,
                      tint=DAM)
        b.extrude([(0, 0), (8.0, 0), (8.0, 4.0), (3.0, 6.0)], 96.0, (0, -118.0, 0), (0, 0, 90), mat=CONCRETE,
                  tint=DAM)
        P.rubble_heap(b, (14.0, -80.0, 2.0), 9.0, 4.0, tint=DAM, count=10)


def powerhouses(b):
    """Two powerhouse halls at the toe, either side of the stilling basin: turbine halls with crane gantries and
    transformers on their roofs, tailrace arches at the river side, penstocks down the face into their backs."""
    tm = (SPILL[0] + SPILL[1]) / 2
    toe = down_face(tm, 0.0)
    for side, x_off in ((-1, -106.0), (1, 106.0)):
        cx = toe.x + x_off
        cy = down_face(x_off / (RC - T_BASE), 0.0).y - 25.0
        yaw = math.degrees(-tm)
        with b.frame((cx, cy, 0), (0, 0, yaw)):
            P.hall(b, (0, 0, 0), 0.0, 104.0, 46.0, 38.0, tint=OFFWHITE, band=ORANGE, band2=TEAL, ruin=0.35,
                   pilaster=8.0, roof_units=False, doors=[("front", -40.0), ("left", 10.0), ("right", -10.0)],
                   bay_doors=[("left", -8.0, 9.0, 8.0)])
            for k in range(4):
                x = -39.0 + k * 26.0
                b.box((14.0, 2.0, 9.0), (x, -24.0, 6.0), mat=MATTE, tint=P.SOOT, solid=False, vary=0)
                b.extrude([(-8.0, 0), (8.0, 0), (8.0, 9.0), (5.0, 12.5), (0.0, 13.5), (-5.0, 12.5), (-8.0, 9.0)], 2.4,
                          (x, -24.6, 0), (0, 0, 0), mat=FORMWORK, tint=DAM, solid=False)
                b.box((16.0, 3.0, 2.0), (x, -24.0, 11.5), mat=CONCRETE, tint=DAM, solid=False)
                b.piece("transformer", (x, -8.0, 38.4), (0, 0, 0))
            b.box((106.0, 4.0, 1.0), (0, 18.0, 46.0), mat=PAINT, tint=DARK, solid=False)
            gantry_crane(b, (8.0 * side, 4.0, 38.4), (0, 0, 0), span=40.0, height=14.0, length=8.0,
                         tint=srgb(0.86, 0.62, 0.12))
            P.stair_tower(b, (-52.0 - 7.6, -14.0, 0), 0.0, 38.4)
            b.marker("loot", (20.0, -10.0, 0.3), table="crate")
            b.marker("loot", (-20.0, 8.0, 38.6), table="military-crate")
        # Penstocks: three per hall, from intakes high on the face down into the hall's back.
        for k in range(3):
            th = tm + (x_off + (k - 1) * 26.0) / (RC - T_BASE)
            pts = [down_face(th, 92.0 - q * 13.0, 4.0) for q in range(7)]
            back = arch(RC - T_BASE - 8.0, th, 14.0)
            pts.append(back)
            b.tube([tuple(p) for p in pts], 3.6, mat=PAINT, tint=srgb(0.36, 0.38, 0.4), sides=16)
            for p in pts[:-1:]:
                b.cyl(4.2, 1.6, tuple(p), (90 + math.degrees(math.atan2(14.0, 30.0)), 0, math.degrees(-th)),
                      mat=PAINT, tint=DARK, sides=16, solid=False)
            top = down_face(th, 92.0, -2.0)
            b.box((14.0, 6.0, 12.0), (top.x, top.y, 96.0), (0, 0, math.degrees(-th)), mat=FORMWORK, tint=DAM)
    # The apron along the tailrace: paving, a river wall with a rail, lamps, a guard post.
    b.box((360.0, 70.0, 0.3), (0, -150.0, 0.15), mat=CONCRETE, tint=srgb(0.6, 0.59, 0.56), solid=False)
    for s in (-1, 1):
        b.box((2.0, 140.0, 2.6), (s * 40.0, -250.0, 1.3), mat=FORMWORK, tint=DAM, solid=True)
        railing(b, (s * 40.0, -320.0, 2.6), (s * 40.0, -186.0, 2.6), 1.1, spacing=2.4, toe=False)
    for k in range(8):
        b.piece("lamp-post", (-165.0 + k * 47.0, -182.0, 0.3), (0, 0, 180))
    P.hall(b, (-120.0, -170.0, 0), 0.0, 14.0, 9.0, 4.5, tint=OFFWHITE, band=ORANGE, ruin=0.2, pilaster=4.0,
           doors=[("front", 3.0)], roof_units=False, windows=False)
    for k in range(4):
        b.piece("jersey-barrier", (-100.0 + k * 3.4, -196.0, 0.3), (0, 0, b.rng.uniform(-8, 8)))
    b.piece("drum-stack", (-88.0, -160.0, 0.3), (0, 0, 15))
    b.piece("container-20", (-140.0, -195.0, 0.3), (0, 0, 80), tint=TEAL)
    b.marker("loot", (-90.0, -170.0, 0.3), table="barrel")
    # Switchyard on the west bank and the line leaving up the valley wall.
    sx, sy = -150.0, -240.0
    b.box((70.0, 50.0, 0.3), (sx, sy, 0.15), mat=CONCRETE, tint=srgb(0.55, 0.54, 0.51), solid=False)
    for i in range(3):
        b.piece("transformer", (sx - 20.0 + i * 20.0, sy + 10.0, 0.3))
        b.piece("breaker", (sx - 20.0 + i * 20.0, sy - 6.0, 0.3))
    b.piece("gantry", (sx, sy - 18.0, 0.3), (0, 0, 0))
    kit.fence(b, [(sx - 35, sy - 25, 0.3), (sx + 35, sy - 25, 0.3), (sx + 35, sy + 25, 0.3), (sx - 35, sy + 25, 0.3),
                  (sx - 35, sy - 25, 0.3)], height=2.6, post=4.0)
    prev = None
    for k, (x, y) in enumerate(((-150.0, -290.0), (-260.0, -330.0), (-380.0, -370.0), (-500.0, -410.0))):
        z = terrain_height(x, y)
        lattice_tower(b, (x, y, z), 34.0, 7.0, 2.0, sections=7, member=0.22, tint=srgb(0.58, 0.6, 0.61))
        b.beam((x, y - 7.0, z + 30.0), (x, y + 7.0, z + 30.0), 0.25, 0.25, mat=PAINT, tint=srgb(0.58, 0.6, 0.61))
        if prev:
            for e in (-6.0, 0.0, 6.0):
                cable(b, (prev[0], prev[1] + e, prev[2] + 29.0), (x, y + e, z + 29.0), sag=0.03, radius=0.06)
        prev = (x, y, z)
    # The access road down the east bank to the toe, a car park, haulers.
    pts = []
    for k in range(12):
        y = -520.0 + k * 40.0
        x = 150.0 + 6.0 * k
        pts.append((x, y, terrain_height(x, y) + 0.2))
    road(b, pts, width=9.0)
    for k in range(3):
        b.piece("hauler-truck", (124.0 + k * 6.0, -230.0, 0.2), (0, 0, 80 + k * 8))
    b.zone("no-build", (0, -100.0, 0), radius=900.0)


def intake_towers(b):
    """Four intake towers in the reservoir off the upstream face: fluted concrete shafts, trash-rack slots, crown
    houses, truss bridges to the crest."""
    for th in (-0.52, -0.36, 0.36, 0.52):
        p = up_face(th, 0.0, 46.0)
        zt = CREST + 16.0
        zb = max(0.0, valley(p.x) - 4.0)
        b.cyl(13.0, zt - zb, (p.x, p.y, zb + (zt - zb) / 2), mat=FORMWORK, tint=DAM, sides=40)
        for k in range(16):
            a = 2 * math.pi * k / 16
            b.box((2.6, 2.6, zt - zb), (p.x + math.cos(a) * 13.0, p.y + math.sin(a) * 13.0, zb + (zt - zb) / 2),
                  (0, 0, math.degrees(a)), mat=FORMWORK, tint=DAM, solid=False)
        for k in range(8):
            a = 2 * math.pi * (k + 0.5) / 8
            for z in range(int(zb) + 30, int(WATER) - 4, 34):
                b.box((0.8, 4.0, 16.0), (p.x + math.cos(a) * 13.1, p.y + math.sin(a) * 13.1, z),
                      (0, 0, math.degrees(a)), mat=MATTE, tint=P.SOOT, solid=False, vary=0)
        b.cyl(15.0, 2.0, (p.x, p.y, zt + 1.0), mat=CONCRETE, tint=DAM, sides=40)
        b.cyl(10.0, 10.0, (p.x, p.y, zt + 7.0), mat=PANEL, tint=OFFWHITE, sides=24)
        b.cyl(10.3, 1.4, (p.x, p.y, zt + 9.0), mat=PAINT, tint=ORANGE, sides=24, solid=False, vary=0)
        b.lathe([(11.0, 0.0), (8.0, 3.0), (0.0, 4.5)], (p.x, p.y, zt + 12.0), mat=PAINT, tint=DARK, sides=24)
        P.aviation_lamp(b, (p.x, p.y, zt + 17.3), 0.8)
        q = up_face(th, CREST, -0.5)
        P.truss(b, (p.x, p.y, CREST + 3.0), (q.x, q.y, CREST + 3.0), w=5.0, h=4.0, member=0.35,
                tint=srgb(0.62, 0.64, 0.64))
        mid = (Vector((p.x, p.y, CREST + 1.2)) + Vector((q.x, q.y, CREST + 1.2))) / 2
        dv = Vector((q.x - p.x, q.y - p.y, 0))
        b.box((dv.length, 4.0, 0.3), (mid.x, mid.y, CREST + 1.2), (0, 0, math.degrees(math.atan2(dv.y, dv.x))),
              mat=GRATE, tint=GREY, solid=False)
        b.marker("loot", (p.x, p.y, zt + 2.2), table="crate")


def abutments(b):
    """The rock the arch thrusts into, either side: patches of the valley walls round the dam's ends."""
    for sgn in (-1, 1):
        x0, x1 = (100.0, 600.0) if sgn > 0 else (-600.0, -100.0)
        terrain(b, x0, x1, -460.0, 300.0, 20.0)


def scenery(b):
    """Look-dev only (not exported): the valley walls beyond the abutments, the reservoir, the river."""
    terrain(b, -1500.0, 1500.0, -2400.0, 3600.0, 50.0, skip=(-600.0, 600.0, -460.0, 300.0))
    # Reservoir: its southern edge is the upstream face at the water line.
    edge = [up_face(-TH0 + 2 * TH0 * k / 40, WATER, -0.5) for k in range(41)]
    poly = [(-1500.0, edge[0].y)] + [(p.x, p.y) for p in edge] + [(1500.0, edge[-1].y), (1500.0, 3600.0),
                                                                     (-1500.0, 3600.0)]
    b.slab(poly, 0.2, (0, 0, WATER - 0.2), mat=GLASS, solid=False)
    # River from the basin and tailraces down the valley.
    pts = []
    for k in range(30):
        y = -60.0 - k * 80.0
        pts.append((22.0 * math.sin(k * 0.4), y))
    for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
        w = 70.0 if ya > -200 else 60.0
        b.slab([(xa - w / 2, ya), (xa + w / 2, ya), (xb + w / 2, yb), (xb - w / 2, yb)], 0.2, (0, 0, 0.9), mat=GLASS,
               solid=False)
    b.slab([(-170.0, -100.0), (170.0, -100.0), (170.0, -150.0), (-170.0, -150.0)], 0.2, (0, 0, 0.8), mat=GLASS,
           solid=False)


def build(b):
    b.set_pad(("rect", 360.0, 260.0), ground="concrete", blend=40.0, clear=10.0)
    dam_body(b)
    crest(b)
    spillway(b)
    powerhouses(b)
    intake_towers(b)
    abutments(b)
    if not kit.EXPORT["enabled"]:
        scenery(b)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 40,
                   "terrain": "valley: the abutments key into valley walls rising about 250 m either side"})


SUN = (26.0, 130.0)
FOG = 0.0001
GROUND = 80000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (0, -40, 110), "distance": 5600, "yaw": 3, "pitch": 4.2, "lens": 85, "fog": 0.00008},
    {"name": "overview", "target": (0, -40, 90), "distance": 1500, "yaw": 32, "pitch": 26, "lens": 30,
     "fog": 0.00005},
    {"name": "foot", "target": (-5.0, -75.5, 76.6), "distance": 200, "yaw": -17.9, "pitch": -22, "lens": 16,
     "fog": 0.00003, "people": [((-58.0, -236.0, 0.3), 200.0), ((-55.5, -238.0, 0.3), 140.0)],
     "ship": ((-66.0, -226.0, 0.3), 60.0)},
]
