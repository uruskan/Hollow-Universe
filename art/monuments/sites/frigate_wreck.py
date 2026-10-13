"""Frigate Wreck (MONUMENTS.md §5.4, large tier, catalogue 43): a Bulwark frigate 420 m long that came down in the
Quiet and broke in two. The bow half (240 m) ploughed on and lies nose down in a mound of earth, rolled toward the
south, its bridge tower and turrets still on its back and its broken end raised over the field; the drive section
(180 m) lies 300 m behind it rolled on its side, its three great thruster bells and radiator fins at the west end.
Between them a scar of debris: plates, ribs, a torn-off hull section, a turret, scorch and furrows. The halves are
walkable: four decks with bulkheads and ladders, the hangar open on the bow's flank behind its framed door, the
bridge in the faceted command tower (a railed walkway round it, a sensor mast and radome on top). Missile cells on the
spine, point-defence mounts on the shoulders, reaction-control clusters and a thrust frame round the great bells give
the hull its detail.

Gameplay: ship parts (wrecks are their main source, §7.7) in crates on the decks, military crates in the bridge and
hangar, a sealed crate by the drive core where the contamination is 2; Reclaimers nest in the bow's broken end with
heavies in the bridge and at the core.
"""
import math

from mathutils import Vector

from monkit import parts_large as P
from monkit.kit import (BLACK, DARK, GREY, LIGHTGREY, OFFWHITE, OLIVE, ORANGE, RED, YELLOW, cable, ladder, lamp,
                        railing, rubble, srgb, stripes)
from monkit.surfaces import (CONCRETE, CORRUGATED, GLASS, GRATE, HULL, LAMP, MATTE, PAINT, PANEL, RUST)

KEY = "frigate-wreck"
TITLE = "Frigate Wreck"
SEED = 4301
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [320.0, 1500.0, 22000.0]

L = 420.0
BREAK = 0.43
# The hull's cross-section (y across, z up) at full size, counter-clockwise seen from the bow.
SECTION = [(0, -21), (10, -21), (24, -13), (30, -2), (29, 8), (22, 17), (12, 22), (0, 23), (-12, 22), (-22, 17),
           (-29, 8), (-30, -2), (-24, -13), (-10, -21)]
RIGHT = [(0, -21), (10, -21), (24, -13), (30, -2), (29, 8), (22, 17), (12, 22), (0, 23)]
DECKS = (-13.0, -5.0, 3.0, 11.0)

HULL_OLIVE = srgb(0.31, 0.34, 0.27)
HULL_GREY = srgb(0.39, 0.41, 0.40)
HULL_LIGHT = srgb(0.42, 0.44, 0.42)
HULL_DARK = srgb(0.22, 0.24, 0.21)
BURNT = srgb(0.10, 0.095, 0.09)

STERN_AT = ((-170.0, 30.0, 4.0), (24.0, 0.0, -8.0))
BOW_AT = ((135.0, -10.0, 2.0), (12.0, 9.0, 6.0))


def station(t):
    """Beam scale, height scale and keel lift at station t (0 stern .. 1 bow)."""
    if t > 0.78:
        k = (t - 0.78) / 0.22
        return 1.0 - 0.93 * k ** 1.25, 1.0 - 0.5 * k, 5.0 * k
    if t < 0.06:
        k = (0.06 - t) / 0.06
        return 1.0 - 0.1 * k, 1.0 - 0.08 * k, 0.0
    return 1.0, 1.0, 0.0


def ring(t, shift, jitter=None):
    sy, sz, dz = station(t)
    x = (t - 0.5) * L + shift
    return [(x + (jitter[j] if jitter else 0.0), y * sy, z * sz + dz) for j, (y, z) in enumerate(SECTION)]


def half_width(z, t):
    sy, sz, dz = station(t)
    zz = (z - dz) / sz
    for (y0, z0), (y1, z1) in zip(RIGHT, RIGHT[1:]):
        if z1 > z0 and z0 <= zz <= z1:
            return (y0 + (y1 - y0) * (zz - z0) / (z1 - z0)) * sy
    return 0.0


def hull(b, t0, t1, shift, broken, hangar=None, seed=1):
    """The plating from station t0 to t1, its ragged break (`broken` = "start" or "end"), burns, a gash, holes."""
    n = max(2, int(round((t1 - t0) * L / 12.0)))
    ts = [t0 + (t1 - t0) * k / n for k in range(n + 1)]
    if t1 > 0.9:
        ts = [t for t in ts if t < 0.9] + [0.9, 0.94, 0.97, 0.99, 1.0]
    jit = [b.rng.uniform(-6.0, 6.0) for _ in SECTION]
    rings = []
    for k, t in enumerate(ts):
        edge = (broken == "end" and k == len(ts) - 1) or (broken == "start" and k == 0)
        rings.append(ring(t, shift, jit if edge else None))
    last = len(rings) - 2

    def keep(i, j):
        near = (last - i) if broken == "end" else i
        r = P.vnoise(i * 0.7, j * 0.9, 0.0, seed)
        if near == 0 and r < 0.45:
            return False
        if near == 1 and r < 0.18:
            return False
        if hangar and hangar[0] <= i <= hangar[1] and j in hangar[2]:
            return False
        if j == 9 and 0.2 < (i / max(1, last)) < 0.45 and r < 0.6:
            return False
        return b.rng.random() > 0.03

    def tint(i, j):
        burn = P.fbm(i * 0.22, j * 0.3, 0.5, seed + 3, 3)
        if burn > 0.64:
            return BURNT
        if burn > 0.58:
            return HULL_DARK
        if j in (6, 7, 8):
            return HULL_LIGHT if i % 7 else HULL_GREY
        return HULL_GREY if i % 6 == 3 else HULL_OLIVE

    P.panel_loft(b, rings, thick=0.7, mat=HULL, tint_fn=tint, keep_fn=keep)
    # Armour belt edges along the flanks (where the section is full size), gun ports in the upper flank.
    xa = (max(t0, 0.02) - 0.5) * L + shift + (8.0 if broken == "start" else 0.0)
    xb = (min(t1, 0.78) - 0.5) * L + shift - (8.0 if broken == "end" else 0.0)
    if xb - xa > 10.0:
        for (y, z) in ((-22.0, 17.0), (22.0, 17.0), (-29.0, 8.0), (29.0, 8.0), (-12.0, 22.0), (12.0, 22.0)):
            b.box((xb - xa, 0.7, 0.5), ((xa + xb) / 2, y * 1.01, z * 1.01), mat=HULL, tint=HULL_DARK, solid=False)
        for k in range(int((xb - xa) / 13.0)):
            x = xa + 6.5 + k * 13.0
            for sgn in (-1, 1):
                if b.rng.random() < 0.2:
                    continue
                b.box((3.4, 0.14, 1.5), (x, sgn * 25.55, 12.55), (sgn * 37.9, 0, 0), mat=MATTE,
                      tint=srgb(0.04, 0.04, 0.045), solid=False, vary=0)
            if k % 2 == 0:
                b.box((4.0, 3.0, 1.0), (x, 6.0 * (1 if k % 4 else -1), 23.4), mat=HULL, tint=HULL_GREY, bevel=0.1)
                for dx in (-1.2, 0.0, 1.2):
                    b.box((0.3, 2.6, 0.1), (x + dx, 6.0 * (1 if k % 4 else -1), 23.95), mat=PAINT, tint=BLACK,
                          solid=False, vary=0)
    end = rings[-1] if broken == "end" else rings[0]
    P.ribs(b, [(x, y * 0.97, z * 0.97) for x, y, z in end], radius=0.4,
           inset=(1.5, 4.0, 6.5) if broken == "end" else (-1.5, -4.0, -6.5), gaps=0.18)
    return ts, rings


def decks(b, t0, t1, shift, hangar_x=None, skip=None, broken=None):
    """Four decks through the half with bulkheads every 24 m (doors alternate sides), a ladder well in each
    compartment. Decks under the ground are left out. Returns the compartments' (x0, x1) list."""
    x0 = (t0 - 0.5) * L + shift + 3.0
    x1 = (t1 - 0.5) * L + shift - 3.0
    n = max(1, int(round((x1 - x0) / 24.0)))
    bays = [(x0 + (x1 - x0) * k / n, x0 + (x1 - x0) * (k + 1) / n) for k in range(n)]
    for k, (a, c) in enumerate(bays):
        ta = (a - shift) / L + 0.5
        tc = (c - shift) / L + 0.5
        for d, zd in enumerate(DECKS):
            hw = min(half_width(zd, ta), half_width(zd, tc)) - 0.9
            if hw < 2.5:
                continue
            if b.world(((a + c) / 2, 0, zd)).z < 0.6:
                continue
            if skip and skip(k, d):
                continue
            hole = (c - 4.0, -1.2, c - 1.6, 1.2) if d < 3 else None
            da, dc = a, c
            if broken == "start" and k == 0:
                da = a - 3.0 + 6.0 * b.rng.random()
            if broken == "end" and k == len(bays) - 1:
                dc = c + 3.0 - 6.0 * b.rng.random()
            P.deck(b, da, -hw, dc, hw, zd, thick=0.3, mat=PAINT, tint=srgb(0.34, 0.35, 0.33), hole=hole, edge=False)
            if d < 3:
                zn = DECKS[d + 1]
                if half_width(zn, tc) > 3.0:
                    ladder(b, (c - 2.8, 1.3, zd), zn - zd, (0, 0, 180), cage=False, tint=YELLOW)
            # The bulkhead at the compartment's aft end, a door in it.
            if d < 3:
                zn = DECKS[d + 1]
                hb = min(half_width(zd, ta), half_width(zn - 0.5, ta)) - 0.9
                open_end = broken == "start" and k == 0
                if hb > 2.0 and not open_end and not (hangar_x and hangar_x[0] <= a <= hangar_x[1]):
                    door = 3.0 if (k + d) % 2 else -3.0
                    P.wall_run(b, (a, -hb), (a, hb), zn - zd - 0.3, 0.35, [(hb + door, 1.6, 2.3, 0.0)], z0=zd,
                               mat=PAINT, tint=srgb(0.42, 0.44, 0.42))
            if (k + d) % 3 == 0:
                lamp(b, ((a + c) / 2, 0, (DECKS[d + 1] if d < 3 else zd + 5.0) - 0.5), (90, 0, 0),
                     colour=srgb(1.0, 0.75, 0.45), size=(0.6, 0.3, 0.2))
    return bays


def turret(b, at, yaw, wrecked=False, tilt=0.0):
    """A dorsal gun turret: an armoured barbette, a faceted house and twin barrels."""
    with b.frame(at, (tilt, 0, yaw)):
        b.cyl(6.5, 2.4, (0, 0, 1.2), mat=HULL, tint=HULL_GREY, sides=20)
        b.extrude([(-6.0, 0.0), (6.0, 0.0), (6.0, 2.2), (2.5, 4.6), (-5.0, 4.6), (-6.0, 3.4)], 9.0, (0, 0, 2.4),
                  (0, 0, 0), mat=HULL, tint=BURNT if wrecked else HULL_OLIVE, bevel=0.1)
        for y in (-2.0, 2.0):
            if wrecked and y > 0:
                b.cyl(0.6, 7.0, (9.0, y, 4.4), (0, 72, 0), mat=PAINT, tint=DARK, sides=10)
                continue
            b.cyl(0.6, 18.0, (14.5, y, 4.2), (0, 90 - (6 if not wrecked else -4), 0), mat=PAINT, tint=DARK, sides=10)
            b.cyl(0.85, 2.2, (6.6, y, 4.0), (0, 90, 0), mat=PAINT, tint=HULL_DARK, sides=10)
        b.box((3.0, 2.4, 1.4), (-3.5, 0, 7.6), mat=HULL, tint=HULL_GREY, bevel=0.1)


def pd_turret(b, at, yaw, lean=0.0):
    """A point-defence mount: an armoured base, a domed head and a rotary cannon pointing along +X of its frame;
    `lean` tips it outward (about its own Y) to sit on a sloping shoulder."""
    with b.frame(at, (0, lean, yaw)):
        b.cyl(1.6, 1.0, (0, 0, 0.5), mat=HULL, tint=HULL_GREY, sides=14)
        b.lathe([(1.4, 0.0), (1.35, 0.6), (1.0, 1.2), (0.4, 1.5), (0.0, 1.55)], (0, 0, 1.0), mat=HULL,
                tint=HULL_LIGHT, sides=14)
        e = math.radians(lean + 5.0)
        b.cyl(0.32, 3.2, (1.0 + 1.9 * math.cos(e), 0, 1.6 + 1.9 * math.sin(e)), (0, 90 - math.degrees(e), 0),
              mat=PAINT, tint=DARK, sides=8)
        b.cyl(0.42, 0.8, (1.0, 0, 1.6), (0, 90 - math.degrees(e), 0), mat=PAINT, tint=HULL_DARK, sides=8)


def rcs_block(b, at, rot):
    """A reaction-control cluster: an armoured housing with three nozzles (out along -Y, up, down)."""
    with b.frame(at, rot):
        b.box((3.2, 2.0, 2.8), (0, 0, 0), mat=HULL, tint=HULL_DARK, bevel=0.1)
        b.lathe([(0.25, 0.0), (0.45, 0.5), (0.6, 0.9)], (0, -1.0, 0), (90, 0, 0), mat=RUST, tint=(1, 1, 1), sides=10,
                close=False, solid=False)
        for sg in (-1, 1):
            b.lathe([(0.2, 0.0), (0.35, 0.4), (0.48, 0.7)], (0.8, -0.2, sg * 1.4), (0 if sg > 0 else 180, 0, 0),
                    mat=RUST, tint=(1, 1, 1), sides=10, close=False, solid=False)


def missile_cells(b, x0, x1, z, w=11.0, rows=3, open_=(1, 6, 10, 15)):
    """Vertical launch cells on the spine: a raised deck with a grid of hatches, some blown open."""
    b.box((x1 - x0 + 1.0, w + 1.0, 0.6), ((x0 + x1) / 2, 0, z + 0.3), mat=HULL, tint=HULL_DARK)
    cols = int((x1 - x0) / 2.4)
    k = 0
    for i in range(cols):
        for j in range(rows):
            x = x0 + 1.2 + i * 2.4
            y = -w / 2 + w * (j + 0.5) / rows
            if k in open_:
                b.box((2.0, w / rows - 0.4, 0.05), (x, y, z + 0.63), mat=MATTE, tint=BLACK, solid=False, vary=0)
                with b.frame((x - 1.0, y, z + 0.7), (0, -105.0, 0)):
                    b.box((2.0, w / rows - 0.5, 0.12), (1.0, 0, 0), mat=HULL, tint=BURNT, solid=False)
            else:
                b.box((2.1, w / rows - 0.35, 0.12), (x, y, z + 0.66), mat=HULL,
                      tint=HULL_GREY if (i + j) % 3 else HULL_LIGHT, solid=False, vary=0.05)
            k += 1


def sensor_mast(b, at, height=14.0):
    """A tapered lattice mast with flat array faces, a yard with antennas, a small dish and a lamp."""
    from monkit.kit import lattice_tower
    x, y, z = at
    lattice_tower(b, (x, y, z), height, 2.6, 0.9, sections=4, member=0.12, tint=GREY)
    for a in (0.0, 90.0, 180.0, 270.0):
        r = math.radians(a)
        b.box((1.8, 0.2, 2.6), (x + math.cos(r) * 1.25, y + math.sin(r) * 1.25, z + height * 0.55), (8, 0, a + 90),
              mat=PANEL, tint=HULL_LIGHT, solid=False)
    b.box((0.15, 6.0, 0.15), (x, y, z + height - 1.0), mat=PAINT, tint=GREY, solid=False)
    for dy in (-2.8, -1.2, 1.2, 2.8):
        b.cyl(0.04, 2.2, (x, y + dy, z + height), mat=PAINT, tint=GREY, sides=5, solid=False)
    b.lathe([(0.0, 0.0), (0.9, 0.25), (1.3, 0.55)], (x + 1.4, y, z + height * 0.75), (0, -70, 0), mat=PANEL,
            tint=HULL_LIGHT, sides=12, close=False, solid=False)
    P.beacon(b, (x, y, z + height + 0.2), 0.4)


def bow(b):
    """The bow half: plating, decks, the hangar opening and Reclaimer nest, the dorsal spine, bridge tower and two
    turrets, the earth it ploughed up."""
    tc = (BREAK + 1.0) / 2
    shift = -(tc - 0.5) * L
    (at, rot) = BOW_AT
    with b.frame(at, rot):
        ts, rings = hull(b, BREAK, 1.0, shift, "start", hangar=(3, 6, (9, 10)), seed=7)
        hx0 = (ts[3] - 0.5) * L + shift
        hx1 = (ts[7] - 0.5) * L + shift
        bays = decks(b, BREAK, 0.9, shift, hangar_x=(hx0 - 1, hx1 + 1), broken="start")
        # Wreckage hanging out of the broken end: beams, cable bundles.
        x0 = (BREAK - 0.5) * L + shift
        for k in range(7):
            y = b.rng.uniform(-18, 18)
            z = b.rng.choice(DECKS) + b.rng.uniform(0.5, 5.0)
            b.tube([(x0 + 2.0, y, z), (x0 - 3.0 - 4.0 * b.rng.random(), y + b.rng.uniform(-3, 3),
                                       z - 3.0 - 4.0 * b.rng.random())], 0.25 + 0.2 * b.rng.random(), mat=RUST,
                   tint=(1, 1, 1), sides=6)
        for k in range(5):
            y = b.rng.uniform(-14, 14)
            z = b.rng.choice(DECKS[1:]) - 0.6
            cable(b, (x0 + 1.0, y, z), (x0 - 2.0, y + 1.0, z - 6.0 - 4.0 * b.rng.random()), sag=0.1, radius=0.06)
        # The nose cap.
        tip = ring(1.0, shift)
        cx = sum(p[0] for p in tip) / len(tip)
        cz = sum(p[2] for p in tip) / len(tip)
        b.mesh([(cx + 6.0, 0.0, cz)] + tip, [[0, j + 1, (j + 1) % len(tip) + 1] for j in range(len(tip))],
               mat=HULL, tint=HULL_DARK, solid="hull", smooth=False)
        # Dorsal spine with its access ribs, the missile cells aft of the tower.
        b.box((132.0, 16.0, 4.0), (-44.0, 0, 24.5), mat=HULL, tint=HULL_GREY)
        for k in range(9):
            b.box((1.0, 16.4, 4.2), (-104.0 + k * 15.0, 0, 24.5), mat=PAINT, tint=HULL_DARK, solid=False, vary=0)
        missile_cells(b, -76.0, -52.0, 26.5)
        # The command tower: an armoured lower block with sloped faces, the bridge on top behind a raked window band
        # under a visor, a railed walkway round it, the sensor mast and the radome.
        bx = -30.0
        b.extrude([(-17.0, 26.5), (13.0, 26.5), (17.0, 30.0), (13.5, 33.5), (-14.0, 33.5), (-18.0, 29.5)], 18.0,
                  (bx, 0, 0), (0, 0, 0), mat=HULL, tint=HULL_OLIVE, bevel=0.1)
        for sgn in (-1, 1):
            P.loopholes(b, (bx - 12.0, sgn * 9.04, 0), (bx + 10.0, sgn * 9.04, 0), 30.6, 6, w=1.6, h=0.5, depth=0.1)
        with b.frame((bx, 0, 33.5), (0, 0, 90)):
            P.room(b, (0, 0, 0), (0, 0, 0), 12.0, 20.0, 5.6, thick=0.4, mat=HULL, tint=HULL_OLIVE,
                   openings={"front": [(0.0, 9.6, 1.5, 1.6)], "left": [(8.0, 1.2, 2.3, 0.0)],
                             "right": [(x, 1.6, 0.8, 2.2) for x in (-6.0, -2.0, 2.0, 6.0)]},
                   roof_mat=HULL, roof_tint=HULL_GREY, floor=False)
        b.box((0.06, 9.5, 1.45), (bx + 9.85, 0, 33.5 + 1.6 + 0.75), mat=GLASS, solid=False)
        b.box((2.4, 13.0, 0.3), (bx + 10.9, 0, 33.5 + 3.6), (0, 32.0, 0), mat=HULL, tint=HULL_DARK, solid=False)
        b.box((2.2, 13.0, 0.3), (bx + 10.8, 0, 33.5 + 0.9), (0, -38.0, 0), mat=HULL, tint=HULL_OLIVE, solid=False)
        for sgn in (-1, 1):
            b.box((21.0, 0.4, 2.0), (bx, sgn * 6.4, 33.5 + 6.2), mat=HULL, tint=HULL_DARK, solid=False)
        for (a0, a1) in (((bx - 13.8, -8.8), (bx + 13.0, -8.8)), ((bx - 13.8, 8.8), (bx + 13.0, 8.8)),
                         ((bx - 13.8, -8.8), (bx - 13.8, 8.8))):
            railing(b, (a0[0], a0[1], 33.5), (a1[0], a1[1], 33.5), tint=YELLOW)
        b.marker("door", (bx - 8.0, -7.0, 33.5), kind_of="bridge")
        ladder(b, (bx - 12.0, -9.4, 26.5), 7.0, (0, 0, 0), cage=True, tint=YELLOW)
        sensor_mast(b, (bx - 4.0, 0.0, 39.5), 14.0)
        b.lathe([(3.0, 0.0), (2.8, 1.2), (2.0, 2.4), (0.0, 3.0)], (bx - 8.5, 0, 39.5), mat=PANEL, tint=LIGHTGREY,
                sides=20)
        b.cyl(0.35, 8.0, (bx + 4.0, 3.5, 43.5), mat=PAINT, tint=GREY, sides=8)
        b.marker("landmark", (bx, 0, 54.0), kind_of="frigate-wreck", visible_km=15)
        # The bridge inside: consoles, crates.
        for k in range(4):
            b.box((1.0, 2.4, 1.0), (bx + 6.5, -4.0 + k * 2.7, 34.0), mat=PAINT, tint=HULL_DARK, bevel=0.03)
        b.marker("loot", (bx - 4.0, 2.0, 33.6), table="military-crate", family="ship")
        b.marker("loot", (bx - 6.0, -3.0, 33.6), table="crate", family="ship")
        # Turrets fore and aft, point-defence mounts on the shoulders, RCS clusters.
        turret(b, (10.0, 0, 26.0), 8.0)
        turret(b, (-95.0, 0, 26.0), -125.0)
        for x in (-100.0, -55.0, -5.0, 20.0):
            for sgn in (-1, 1):
                pd_turret(b, (x, sgn * 22.6, 17.2), 90.0 * sgn, lean=38.0)
        for sgn in (-1, 1):
            rcs_block(b, (22.0, sgn * 29.6, 6.0), (0, 0, 0 if sgn < 0 else 180))
        # The hangar's frame round its opening in the south flank, a door leaf still hanging across half of it.
        b.beam((hx0, -22.44, 17.0), (hx1, -22.44, 17.0), 0.9, mat=HULL, tint=HULL_DARK)
        b.beam((hx0, -30.6, -2.0), (hx1, -30.6, -2.0), 0.9, mat=HULL, tint=HULL_DARK)
        for x in (hx0, hx1):
            b.beam((x, -22.4, 17.2), (x, -29.6, 8.0), 0.9, mat=HULL, tint=HULL_DARK)
            b.beam((x, -29.6, 8.0), (x, -30.6, -2.0), 0.9, mat=HULL, tint=HULL_DARK)
        b.beam((hx0, -22.6, 18.4), (hx1, -22.6, 18.4), 0.5, 0.7, mat=PAINT, tint=GREY)
        lx0 = (hx0 + hx1) / 2
        b.box((24.0, 0.5, 20.6), (lx0 + 12.0, -26.74, 7.81), (-22.8, 0, 0), mat=HULL, tint=HULL_GREY)
        for k in range(8):
            sgl = -9.4
            b.box((3.0, 0.55, 1.0), (lx0 + 1.5 + 3.0 * k, -26.74 + 0.388 * sgl - 0.28, 7.81 + 0.922 * sgl + 0.12),
                  (-22.8, 0, 0), mat=PAINT, tint=YELLOW if k % 2 else BLACK, solid=False, vary=0)
        P.stencil(b, "BV12", (2.0, -28.3, 9.4), (-37.9, 0, 0), 6.5, tint=HULL_LIGHT, depth=0.05)
        tipw = b.world((128.0, 0, 0))
        fwd = (b.m.to_3x3() @ Vector((1, 0, 0))).normalized()
        # Hangar: a big open bay on decks 2-3 behind the flank opening, the Reclaimers' nest.
        hx = (hx0 + hx1) / 2
        b.marker("loot", (hx - 10.0, -6.0, DECKS[1] + 0.2), table="military-crate", family="ship")
        b.marker("loot", (hx + 8.0, 4.0, DECKS[1] + 0.2), table="crate", family="ship")
        for k in range(3):
            P.crates(b, (hx - 14.0 + 12.0 * k, 8.0, DECKS[1]), (0, 0, b.rng.uniform(-20, 20)), n=4, tint=HULL_OLIVE)
        b.marker("guard", (hx, -2.0, DECKS[1] + 0.2), role="reclaimer")
        # Loot in the compartments.
        for k, (a, c) in enumerate(bays):
            if k % 2 == 0:
                d = 2 if k % 4 == 0 else 3
                b.marker("loot", ((a + c) / 2, -3.0, DECKS[d] + 0.2), table="crate", family="ship")
    # Earth ploughed up round the nose: a bank in front, ridges along both sides.
    side = Vector((-fwd.y, fwd.x, 0)).normalized()
    f2 = Vector((fwd.x, fwd.y, 0)).normalized()
    for k, (u, fw, size, h) in enumerate(((-0.9, -6.0, 30.0, 9.0), (-0.45, 2.0, 38.0, 14.0), (0.05, 6.0, 44.0, 17.0),
                                          (0.5, 1.0, 36.0, 12.0), (0.95, -8.0, 28.0, 8.0))):
        p = Vector((tipw.x, tipw.y, 0)) + f2 * (fw - 12.0) + side * (u * 36.0)
        P.rock(b, (p.x, p.y, 0.0), (size, size * 0.7, h), seed=k + 40, rough=0.45, freq=1.3, rings=7, sides=12,
               mat=MATTE, tint=SOIL, solid="hull", squash=0.2)
    for k in range(2):
        for sgn in (-1, 1):
            p = Vector((tipw.x, tipw.y, 0)) - f2 * (36.0 + k * 30.0 + b.rng.uniform(-6, 6)) + side * sgn * (
                31.0 + 3.0 * k + b.rng.uniform(-3, 3))
            P.rock(b, (p.x, p.y, 0.0), (34.0, 13.0, 5.0 - 1.2 * k), seed=k * 2 + (sgn > 0) + 60, rough=0.45,
                   freq=1.3, rings=6, sides=12, mat=MATTE, tint=SOIL, solid="hull", squash=0.3)


def stern(b):
    """The drive section: plating, decks round the drive core, the stern face with three great thruster bells and
    two small ones, radiator fins on the flanks, a dorsal turret."""
    tc = BREAK / 2
    shift = -(tc - 0.5) * L
    (at, rot) = STERN_AT
    with b.frame(at, rot):
        ts, rings = hull(b, 0.0, BREAK, shift, "end", seed=13)
        xs = -tc * L
        face = ring(0.0, shift)
        b.mesh(face, [list(range(len(face)))[::-1]], mat=HULL, tint=HULL_DARK, solid="hull", smooth=False)
        core_x = xs + 50.0

        def skip(k, d):
            return 1 <= k <= 2 and d in (1, 2)
        decks(b, 0.03, BREAK, shift, skip=skip, broken="end")
        # The drive core: a reactor drum through two decks, glowing seams, the sealed crate.
        b.cyl(7.0, 15.0, (core_x, 0, DECKS[1] + 7.5), mat=PAINT, tint=srgb(0.34, 0.36, 0.38), sides=24)
        for z in (DECKS[1] + 3.0, DECKS[1] + 12.0):
            b.cyl(7.05, 0.4, (core_x, 0, z), mat=LAMP, tint=srgb(0.55, 0.85, 0.55), sides=24, cap=False, solid=False,
                  vary=0)
        b.marker("loot", (core_x + 10.0, -4.0, DECKS[1] + 0.2), table="sealed-crate", family="ship")
        b.marker("guard", (core_x + 14.0, 3.0, DECKS[1] + 0.2), role="reclaimer-heavy")
        b.zone("radiation", (core_x, 0, DECKS[1] + 7.0), radius=45.0, level=2, source="drive")
        # Thruster bells and nozzle ring.
        for (y, z, r) in ((0.0, 6.0, 9.0), (-14.0, -7.0, 8.0), (14.0, -7.0, 8.0), (-22.0, 10.0, 3.5),
                          (22.0, 10.0, 3.5)):
            b.lathe([(r * 0.62, 0.0), (r * 0.78, r * 0.5), (r, r * 1.3), (r * 1.06, r * 1.45)], (xs + 0.5, y, z),
                    (0, -90, 0), mat=RUST, tint=srgb(0.75, 0.72, 0.68), sides=28, close=False, solid="hull")
            b.cyl(r * 0.64, 1.2, (xs - 0.2, y, z), (0, 90, 0), mat=PAINT, tint=BURNT, sides=28, solid=False)
        # The thrust frame: a ring round each great bell's throat, gimbal actuators to the stern plate.
        for (y, z, r) in ((0.0, 6.0, 9.0), (-14.0, -7.0, 8.0), (14.0, -7.0, 8.0)):
            b.tube([(xs - 1.0, y + math.cos(a) * r * 0.66, z + math.sin(a) * r * 0.66) for a in
                    [2 * math.pi * k / 16 for k in range(17)]], 0.4, mat=PAINT, tint=HULL_DARK, sides=6, cap=False)
            for k in range(3):
                a = math.radians(90 + 120 * k)
                b.tube([(xs + 0.4, y + math.cos(a) * (r * 0.95), z + math.sin(a) * (r * 0.95)),
                        (xs - 1.2, y + math.cos(a) * (r * 0.7), z + math.sin(a) * (r * 0.7))], 0.3, mat=PAINT,
                       tint=GREY, sides=8)
        for sgn in (-1, 1):
            rcs_block(b, (xs + 8.0, sgn * 29.6, 6.0), (0, 0, 0 if sgn < 0 else 180))
            rcs_block(b, (xs + 8.0, sgn * 22.4, 18.0), (sgn * 45.0, 0, 0 if sgn < 0 else 180))
        sensor_mast(b, (xs + 70.0, 0.0, 28.0), 10.0)
        for k in range(3):
            b.cyl(0.08, 6.0 + 2.0 * k, (xs + 30.0 + k * 6.0, 6.0, 28.0 + 3.0 + k), mat=PAINT, tint=GREY, sides=5,
                  solid=False)
        # Radiator fins out of both flanks.
        for k in range(6):
            x = xs + 20.0 + k * 11.0
            if k == 4:
                b.box((8.0, 6.0, 0.5), (x, 32.0, 6.0), (8.0, 0, 0), mat=PANEL, tint=BURNT, solid=True)
                continue
            b.box((8.0, 14.0, 0.5), (x, 36.0, 6.0), (8.0, 0, 0), mat=PANEL, tint=HULL_LIGHT if k % 2 else HULL_GREY,
                  solid=True)
            b.box((8.4, 0.6, 0.8), (x, 43.5, 7.0), mat=PAINT, tint=HULL_DARK, solid=False)
            for z in (5.4, 6.6):
                b.tube([(x - 3.0, 29.0, z), (x - 3.0, 42.0, z + 1.6)], 0.15, mat=PAINT, tint=GREY, sides=6)
        b.box((95.0, 22.0, 6.0), (xs + 50.0, 0, 25.0), mat=HULL, tint=HULL_GREY)
        for k in range(8):
            b.box((4.0, 22.4, 1.0), (xs + 8.0 + k * 12.0, 0, 28.4), mat=PAINT, tint=HULL_DARK, solid=False, vary=0)
        turret(b, (xs + 125.0, 0, 26.0), 160.0, wrecked=True, tilt=4.0)
        for k, (a, c) in enumerate([(xs + 100.0, xs + 120.0), (xs + 130.0, xs + 150.0)]):
            b.marker("loot", ((a + c) / 2, 2.0, DECKS[2] + 0.2), table="crate", family="ship")
        # The hull code on the upper flank plates, facing south now she lies rolled.
        P.stencil(b, "BV12", (xs + 46.0, -28.3, 9.4), (-37.9, 0, 0), 6.5, tint=HULL_LIGHT, depth=0.05)


def debris(b):
    """The scar between the halves: hull plates, broken ribs, the torn-off turret, containers, scorch, furrows,
    the Reclaimers' nest and a ramp of wreckage up into the bow's broken end."""
    # Furrows and scorch along the skid line.
    for k in range(14):
        t = k / 13
        x = -230.0 + 360.0 * t
        y = 26.0 - 40.0 * t + 8.0 * math.sin(k * 1.3)
        P.scorch(b, (x, y, 0.0), 10.0 + 6.0 * b.rng.random(), tint=srgb(0.11, 0.09, 0.07), blobs=3)
    for k in range(6):
        x = -110.0 + 22.0 * k
        P.rock(b, (x, 8.0 - 5.0 * k + b.rng.uniform(-6, 6), 0.6), (18.0, 7.0, 2.6), seed=k + 70, rough=0.3, rings=5,
               sides=10, mat=MATTE, tint=SOIL, solid="hull", squash=0.4)
    # Plates and ribs strewn about.
    for k in range(18):
        x = -105.0 + b.rng.random() * 110.0
        y = -30.0 + b.rng.random() * 60.0
        w, d = 6.0 + 6.0 * b.rng.random(), 4.0 + 5.0 * b.rng.random()
        b.box((w, d, 0.6), (x, y, 0.8 + b.rng.random()), (b.rng.uniform(-25, 25), b.rng.uniform(-30, 30),
                                                            b.rng.uniform(0, 360)), mat=HULL,
              tint=[HULL_OLIVE, HULL_GREY, BURNT, HULL_DARK][k % 4], solid=True)
    for k in range(6):
        x = -95.0 + k * 18.0
        y = -15.0 + b.rng.uniform(-15, 15)
        r = 8.0 + 6.0 * b.rng.random()
        a0 = b.rng.uniform(0, 1.2)
        pts = [(x + math.cos(a0 + 0.25 * j) * r * 0.3, y, -2.0 + math.sin(a0 + 0.25 * j) * r) for j in range(8)]
        b.tube(pts, 0.4, mat=RUST, tint=(1, 1, 1), sides=6, solid=False)
    P.scrap(b, (-60.0, 0.0), 40.0, count=26, max_size=3.0, tints=[HULL_OLIVE, HULL_GREY, BURNT, RUSTY_T])
    turret(b, (-40.0, 22.0, -1.5), 130.0, wrecked=True, tilt=-28.0)
    with b.frame((-80.0, -6.0, 0.6), (14.0, 0, 28.0)):
        R = 20.0
        thetas = [math.radians(-30.0 + 10.0 * k) for k in range(7)]
        rings = [[(x, R * math.sin(t), R - R * math.cos(t)) for t in thetas] for x in (-13.0, -4.0, 5.0, 13.0)]
        P.panel_loft(b, rings, thick=0.6, mat=HULL, tint_fn=lambda i, j: [HULL_OLIVE, HULL_GREY, BURNT][(i + j) % 3],
                     keep_fn=lambda i, j: j < 6 and not (i == 2 and j == 0), ground=None)
        for x in (-11.0, -2.0, 7.0):
            b.tube([(x, (R - 0.7) * math.sin(t), R - (R - 0.7) * math.cos(t)) for t in thetas], 0.3, mat=RUST,
                   tint=(1, 1, 1), sides=6)
    for x, y, yaw, tint in ((-80.0, -24.0, 35, HULL_OLIVE), (-20.0, 30.0, 110, srgb(0.6, 0.6, 0.58))):
        b.piece("container-40", (x, y, 0), (0, 0, yaw), tint=tint)
    # Ramp of wreckage up into the bow's broken end (its lowest deck).
    P.ramp(b, (-30.0, -38.0), (17.0, -19.8), 9.0, 0.0, 8.0, mat=HULL, tint=HULL_DARK)
    P.scrap(b, (-8.0, -26.0), 10.0, count=10, max_size=2.4, tints=[HULL_GREY, BURNT, RUSTY_T])
    # The nest: scrap walls, tarps on poles, a fire, lights on a cable.
    nx, ny = -2.0, -40.0
    for k in range(7):
        a = math.radians(200 + k * 22)
        b.box((5.0, 0.12, 2.6 + b.rng.random()), (nx + math.cos(a) * 12.0, ny + math.sin(a) * 12.0, 1.4),
              (b.rng.uniform(-6, 6), 0, math.degrees(a) + 90), mat=CORRUGATED,
              tint=[P.TARP_BLUE, RUSTY_T, LIGHTGREY, HULL_OLIVE][k % 4], solid=True)
    for (x, y, tint) in ((nx - 4.0, ny + 3.0, P.TARP_BLUE), (nx + 5.0, ny - 2.0, P.TARP_ORANGE)):
        for dx, dy in ((-2.5, -2.0), (2.5, -2.0), (-2.5, 2.0), (2.5, 2.0)):
            b.cyl(0.06, 2.6 if dy < 0 else 2.0, (x + dx, y + dy, 1.3 if dy < 0 else 1.0), mat=PAINT, tint=DARK,
                  sides=6, solid=False)
        b.box((5.6, 4.6, 0.04), (x, y, 2.35), (8, 0, 0), mat=MATTE, tint=tint, solid=False)
    P.scorch(b, (nx, ny, 0.0), 1.6, blobs=2)
    for k in range(6):
        a = 2 * math.pi * k / 6
        b.box((0.5, 0.3, 0.3), (nx + math.cos(a) * 0.9, ny + math.sin(a) * 0.9, 0.15), (0, 0, math.degrees(a)),
              mat=CONCRETE, tint=srgb(0.4, 0.4, 0.38), solid=False)
    b.marker("light", (nx, ny, 0.6), colour=[1.0, 0.55, 0.2], range=10.0, kind_of="fire")
    poles = [(nx - 10.0, ny - 6.0), (nx + 9.0, ny - 8.0), (nx + 12.0, ny + 8.0)]
    for x, y in poles:
        b.cyl(0.08, 4.0, (x, y, 2.0), mat=PAINT, tint=DARK, sides=6, solid=False)
    for (ax, ay), (cx, cy) in zip(poles, poles[1:]):
        cable(b, (ax, ay, 3.9), (cx, cy, 3.9), sag=0.06, radius=0.02)
        mx, my = (ax + cx) / 2, (ay + cy) / 2
        b.box((0.2, 0.2, 0.3), (mx, my, 3.4), mat=LAMP, tint=srgb(1.0, 0.8, 0.5), solid=False, vary=0)
        b.marker("light", (mx, my, 3.3), colour=[1.0, 0.8, 0.5], range=8.0)
    for p in ((nx - 3.0, ny + 6.0), (nx + 6.0, ny + 4.0), (nx - 7.0, ny - 3.0)):
        b.marker("loot", (p[0], p[1], 0.1), table="barrel")
    b.marker("loot", (nx + 2.0, ny + 8.0, 0.1), table="crate", family="ship")
    for p in ((nx + 3.0, ny - 6.0), (nx - 12.0, ny + 14.0), (-60.0, 10.0)):
        b.marker("guard", (p[0], p[1], 0.1), role="reclaimer")
    b.marker("spawn", (60.0, -110.0, 0), kind_of="approach")


RUSTY_T = srgb(0.48, 0.30, 0.20)
SOIL = srgb(0.40, 0.37, 0.32)


def build(b):
    b.set_pad(("rect", 600.0, 220.0), ground="dirt", blend=50.0, clear=12.0)
    stern(b)
    bow(b)
    debris(b)
    b.zone("no-build", (0, 0, 0), size=(680.0, 300.0, 200.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "plains", "visible_km": 14})


SUN = (24.0, 215.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((-14.0, -36.0, 0), 230.0), ((-4.0, -31.0, 0), 60.0)]
SHIP = ((-52.0, -72.0, 0), 20.0)
VIEWS = [
    {"name": "far", "target": (-20.0, 0.0, 28.0), "distance": 2600, "yaw": -30, "pitch": -0.4, "lens": 120},
    {"name": "overview", "target": (-10.0, 0.0, 10.0), "distance": 640, "yaw": 20, "pitch": 30, "lens": 30},
    {"name": "break", "target": (8.0, -14.0, 20.0), "distance": 78, "yaw": -50, "pitch": -13.5, "lens": 22},
]
