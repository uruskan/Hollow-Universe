"""Mountain Redoubt (MONUMENTS.md §5.4, large tier, catalogue 42): a Bulwark fortress cut into a cliff. The rock
mass is part of the monument (the terrain round it comes later): a massif 450 m wide whose 90 m cliff faces south
and whose back rises to 150 m, 650 m deep. In the cliff stands an armoured concrete bastion with its main blast door
jammed half open between two gun towers; two casemates with heavy guns look out higher up; an observation post,
masts and vent shafts sit on the top. Before it, an apron of tank traps, revetments, sandbag nests and wrecks.

Inside, 600 m of tunnels: the entrance cavern with a catwalk to the casemate gallery, the vehicle tunnel to the motor
pool, the long north corridor with the barracks wing and the storage wing, and at its end the command bunker with
the armoury and the command vault (the violet room: violet chip and a power cell).

Gameplay: heavy Reclaimers hold the cavern, the motor pool, the storage and the bunker; military crates in the
armoury, the casemates and the storage, vault crates in the command vault; Warden turrets on the gun towers.
"""
import math

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OLIVE, ORANGE, RED, YELLOW, cable, ladder, lamp,
                        lattice_tower, railing, rubble, srgb, stairs, stripes)
from monkit.structures import dish, road, sandbags, wall
from monkit.surfaces import (CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST)

KEY = "mountain-redoubt"
TITLE = "Mountain Redoubt"
SEED = 4201
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [320.0, 1600.0, 26000.0]

FACE_Y = -230.0
BASTION = srgb(0.70, 0.70, 0.66)
XH = 224.0
BACK_Y = 425.0
GALLERY_Z = 30.0
MOSS = srgb(0.37, 0.39, 0.28)
ROCK_LIGHT = srgb(0.56, 0.53, 0.48)


def smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def height(x, y):
    """The massif's height at (x, y): the cliff top along the front, rising toward the back, falling to the ground
    at the sides and the back."""
    t = (y - FACE_Y) / (BACK_Y - FACE_Y)
    edge = smooth(0.0, 1.0, (XH - abs(x)) / 80.0)
    back = 1.0 - smooth(330.0, BACK_Y, y)
    n = P.fbm(x / 90.0, y / 90.0, 0.3, 11, 4)
    ridge = 1.0 - abs(P.vnoise(x / 60.0, y / 60.0, 3.3, 17) - 0.5) * 2.0
    front = 12.0 * (P.fbm(x / 45.0, 0.0, 8.1, 19, 3) - 0.5)
    return max(0.0, (88.0 + front + 62.0 * smooth(0.0, 0.62, t) + 36.0 * (n - 0.5) + 14.0 * (ridge - 0.5)) * edge
               * back)


def face_y(x, z):
    """The cliff face's y at (x, z): rough, leaning back toward the top, cut flat round the bastion."""
    n = P.fbm(x / 14.0, z / 45.0, 1.7, 5, 4)
    m = P.fbm(x / 40.0, z / 20.0, 6.2, 9, 3)
    rough = 16.0 * (n - 0.5) + 6.0 * (m - 0.5)
    if abs(x) < 40.0 and z < 34.0:
        rough *= 0.3
    return FACE_Y + rough + z * 0.14


def rock(b):
    """The massif: the cliff face and the top surface as slab cells (their own collision), the bastion's and the
    casemates' openings left in the face."""
    xs = [-XH + 16.0 * k for k in range(29)]
    rows = 12
    hf = [height(x, FACE_Y) for x in xs]
    face = [[(x, face_y(x, hf[j] * r / rows), hf[j] * r / rows) for j, x in enumerate(xs)] for r in range(rows + 1)]

    def keep_face(i, j):
        if hf[j] < 0.5 and hf[j + 1] < 0.5:
            return False
        x0, x1 = xs[j], xs[j + 1]
        if -33.0 <= x0 and x1 <= 33.0 and i < 4:
            return False
        if i in (4, 5) and ((-97.0 <= x0 and x1 <= -63.0) or (63.0 <= x0 and x1 <= 97.0)):
            return False
        return True

    def tint_face(i, j):
        # Darker in the gullies (where the face lies furthest back), faint strata, a little light rock.
        x = (xs[j] + xs[j + 1]) / 2
        z = hf[j] * (i + 0.5) / rows
        recess = (face_y(x, z) - FACE_Y - z * 0.14) / 11.0
        k = max(0.0, min(1.0, 0.5 + recess))
        base = [P.ROCK[c] * (1.08 - 0.32 * k) for c in range(3)]
        if i % 4 == 2:
            base = [v * 0.93 for v in base]
        return tuple(base)

    P.skin(b, face, (0, 1, 0), thick=4.0, mat=CONCRETE, tint_fn=tint_face, keep_fn=keep_face)
    ys = [None] + [FACE_Y + 30.0 + 22.0 * (k - 1) for k in range(1, 30)]
    top = []
    for k, y in enumerate(ys):
        if k == 0:
            top.append([face[rows][j] for j in range(len(xs))])
            continue
        top.append([(x, y, height(x, y)) for x in xs])

    def keep_top(i, j):
        zs = (top[i][j][2], top[i][j + 1][2], top[i + 1][j][2], top[i + 1][j + 1][2])
        return max(zs) > 0.3

    def tint_top(i, j):
        # Scrub on the gentle ground, bare rock where it is steep.
        zs = (top[i][j][2], top[i][j + 1][2], top[i + 1][j][2], top[i + 1][j + 1][2])
        slope = (max(zs) - min(zs)) / 22.0
        n = P.fbm(j * 0.12, i * 0.12, 4.1, 23, 3)
        g = max(0.0, min(1.0, 1.4 - slope * 2.2 + (n - 0.5)))
        return tuple(P.ROCK[c] * (1 - g) + MOSS[c] * g for c in range(3))

    P.skin(b, top, (0, 0, -1), thick=3.0, mat=CONCRETE, tint_fn=tint_top, keep_fn=keep_top)
    # A talus of scree along the foot of the cliff, clear of the bastion.
    talus = []
    for r, (dy, zz) in enumerate(((-30.0, 0.0), (0.0, 13.0))):
        row = []
        for j, x in enumerate(xs):
            hh = min(zz, hf[j] * 0.16)
            if r == 0:
                row.append((x, FACE_Y - 22.0 - 10.0 * P.vnoise(x / 30.0, 0, 0, 31), 0.0))
            else:
                row.append((x, face_y(x, hh) + 2.0, hh))
        talus.append(row)

    def keep_talus(i, j):
        return (xs[j + 1] < -50.0 or xs[j] > 50.0) and hf[j] > 2.0 and hf[j + 1] > 2.0

    P.skin(b, talus, (0, 0, -1), thick=2.0, mat=CONCRETE, keep_fn=keep_talus,
           tint_fn=lambda i, j: srgb(0.41, 0.38, 0.34))
    # Boulders fallen at the cliff's foot.
    for k in range(26):
        x = b.rng.uniform(-200.0, 200.0)
        if abs(x) < 50.0:
            continue
        s = 3.0 + 7.0 * b.rng.random()
        P.rock(b, (x, FACE_Y - 6.0 - b.rng.random() * 14.0, s * 0.3), (s * 1.4, s, s * 0.8), seed=k + 3, rough=0.35,
               rings=6, sides=9, tint=P.ROCK_DARK if k % 3 else P.ROCK)


def bastion(b):
    """The armoured facade in the cliff: the main blast door jammed half open, a personnel door, two gun towers,
    loopholes, a blast canopy, the code, searchlights."""
    fy = FACE_Y - 3.0
    P.wall_run(b, (-34.0, fy + 1.5), (34.0, fy + 1.5), 33.0, 3.0, [(34.0, 14.0, 11.0, 0.0), (56.0, 2.4, 3.0, 0.0)],
               mat=FORMWORK, tint=BASTION)
    for x in (-30.0, 30.0):
        P.block(b, x - 5.0, x + 5.0, fy - 8.0, fy + 3.0, 36.0, {"s": 2.0}, mat=FORMWORK, tint=BASTION)
        P.loopholes(b, (x - 4.0, fy - 8.4, 0), (x + 4.0, fy - 8.4, 0), 18.0, 3, w=1.0, h=0.4)
        P.loopholes(b, (x - 4.0, fy - 8.4, 0), (x + 4.0, fy - 8.4, 0), 28.0, 3, w=1.0, h=0.4)
        b.box((11.0, 12.0, 1.2), (x, fy - 2.5, 36.6), mat=CONCRETE, tint=BASTION)
        b.box((1.6, 1.6, 1.0), (x, fy - 5.0, 37.7), mat=PAINT, tint=DARK, bevel=0.05)
        b.marker("guard", (x, fy - 5.0, 38.2), role="warden-turret")
        lamp(b, (x + (4.0 if x < 0 else -4.0), fy - 8.5, 14.0), (25, 0, 15 if x < 0 else -15),
             size=(0.9, 0.5, 0.6))
    for z in (16.0, 24.0):
        P.loopholes(b, (-24.0, fy - 0.1, 0), (24.0, fy - 0.1, 0), z, 8, w=1.4, h=0.45)
    # Blast canopy over the door, its stripes and the code.
    b.box((40.0, 7.0, 2.0), (0, fy - 3.5, 13.0), mat=FORMWORK, tint=BASTION)
    stripes(b, (-20.0, fy - 7.05, 0), (20.0, fy - 7.05, 0), 1.0, width=1.2, at_z=12.5)
    P.stencil(b, "04", (-7.5, fy - 0.05, 17.5), (0, 0, 0), 11.0, tint=P.OLIVE_DARK)
    for z in (7.5, 29.0):
        b.box((68.0, 0.3, 0.7), (0, fy - 0.12, z), mat=CONCRETE, tint=srgb(0.55, 0.55, 0.52), solid=False, vary=0)
    for k in range(16):
        x = -24.0 + 48.0 * (k % 8 + 0.5) / 8
        z = 16.0 if k < 8 else 24.0
        L = 1.5 + 4.0 * b.rng.random()
        b.box((0.25, 0.05, L), (x + b.rng.uniform(-0.3, 0.3), fy - 0.06, z - 0.3 - L / 2), mat=RUST, tint=(1, 1, 1),
              solid=False, vary=0.1, wear=0.9)
    for (x, z, r) in ((-20.0, 21.0, 2.6), (14.0, 26.0, 2.0), (-27.0, 9.0, 1.8), (24.0, 13.0, 2.2)):
        for k in range(3):
            ox, oz = b.rng.uniform(-0.6, 0.6) * r, b.rng.uniform(-0.6, 0.6) * r
            b.cyl(r * (0.5 + 0.4 * b.rng.random()), 0.03 + 0.01 * k, (x + ox, fy - 0.07 - 0.01 * k, z + oz), (90, 0, 0),
                  mat=MATTE, tint=srgb(0.36, 0.35, 0.33), sides=9, solid=False, vary=0.1)
        b.cyl(r * 0.3, 0.06, (x, fy - 0.11, z), (90, 0, 0), mat=MATTE, tint=srgb(0.16, 0.15, 0.14), sides=8,
              solid=False)
    for sgn in (-1, 1):
        sandbags(b, (sgn * 8.0, fy - 9.0, 0), (sgn * 15.0, fy - 7.0, 0), height=3)
    # The main door: two leaves, one slid open, the other jammed askew.
    b.box((7.4, 1.4, 11.0), (-9.0, fy + 1.0, 5.5), mat=PAINT, tint=P.OLIVE_DARK, bevel=0.05, solid=False)
    b.box((7.4, 1.4, 11.0), (4.2, fy + 1.4, 5.5), (0, 0, 7.0), mat=PAINT, tint=P.OLIVE_DARK, bevel=0.05, solid=False)
    for x in (-9.0, 4.2):
        stripes(b, (x - 3.6, fy + 0.25, 0), (x + 3.6, fy + 0.25, 0), 0.8, width=0.6, at_z=0.1)
    b.collide_box((-9.0, fy + 1.0, 5.5), (7.4, 1.4, 11.0))
    b.marker("door", (0.0, fy - 1.5, 0), kind_of="main-blast-door", width=14.0, height=11.0, state="jammed")
    # Personnel door with its own small blast door.
    P.blast_door(b, (22.0, fy - 1.0, 0), (0, 0, 0), w=2.4, h=3.0, depth=1.2, frame=0.8, open=0.0, tint=P.OLIVE_DARK,
                 frame_tint=BASTION, kind="personnel-door")
    P.scorch(b, (-2.0, fy - 6.0, 0.0), 5.0, blobs=3)


def casemate(b, x, gun_yaw=0.0):
    """A gun casemate protruding from the face at the gallery's height: armoured walls, a wide slit, a heavy gun."""
    y0, y1 = FACE_Y - 14.0, -208.0
    z0, z1 = 27.0, 49.0
    cy = (y0 + y1) / 2
    P.block(b, x - 13.0, x + 13.0, y0, y1, z1 - z0, {"s": 3.0}, z0=z0, mat=FORMWORK, tint=P.BW_CONCRETE,
            solid=False)
    # Collision and the inside: a room at the gallery's floor.
    P.room(b, (x, cy, GALLERY_Z), (0, 0, 0), 20.0, y1 - y0 - 4.0, 5.0, thick=1.5, mat=CONCRETE, tint=P.BW_CONCRETE,
           openings={"front": [(0.0, 9.0, 1.2, 1.4)], "back": [(0.0, 2.4, 3.0, 0.0)]}, roof_thick=1.0)
    b.box((26.0, y1 - y0, GALLERY_Z - z0), (x, cy, (z0 + GALLERY_Z) / 2), mat=CONCRETE, tint=P.BW_CONCRETE)
    b.box((10.0, 1.0, 1.8), (x, y0 - 3.05, GALLERY_Z + 2.0), mat=MATTE, tint=srgb(0.04, 0.04, 0.045), solid=False)
    with b.frame((x, y0 + 2.0, GALLERY_Z + 2.0), (0, 0, gun_yaw)):
        b.cyl(1.8, 3.0, (0, -1.0, 0), (90, 0, 0), mat=PAINT, tint=P.OLIVE_DARK, sides=16)
        b.cyl(0.55, 16.0, (0, -9.0, 0.2), (90 - 3.0, 0, 0), mat=PAINT, tint=DARK, sides=12)
        b.cyl(0.75, 1.4, (0, -16.6, 0.6), (90 - 3.0, 0, 0), mat=PAINT, tint=DARK, sides=12)
    b.marker("loot", (x - 6.0, cy + 3.0, GALLERY_Z + 0.1), table="military-crate")
    P.crates(b, (x + 6.0, cy + 4.0, GALLERY_Z + 0.06), (0, 0, 0), n=4, tint=P.OLIVE_DARK)


def interior(b):
    """The tunnels: entrance cavern with the stair and catwalk to the casemate gallery, the vehicle tunnel, the motor
    pool, the north corridor with its wings, the command bunker."""
    fy = FACE_Y - 3.0
    # The entrance cavern behind the facade.
    hy0, hy1 = fy + 3.0, -195.0
    for x in (-16.3, 16.3):
        P.wall_run(b, (x, hy0), (x, hy1), 34.0, 0.6, [(hy1 - hy0 - 10.0 + 2.5, 5.0, 3.4, GALLERY_Z)] if True else [],
                   mat=CONCRETE, tint=P.BW_CONCRETE)
    P.wall_run(b, (-16.6, hy1 + 0.3), (16.6, hy1 + 0.3), 34.0, 0.6, [(16.6, 12.0, 9.0, 0.0)], mat=CONCRETE,
               tint=P.BW_CONCRETE)
    b.box((33.8, hy1 - hy0 + 0.6, 1.2), (0, (hy0 + hy1) / 2, 34.6), mat=CONCRETE, tint=P.BW_CONCRETE)
    b.box((33.0, hy1 - hy0, 0.3), (0, (hy0 + hy1) / 2, -0.15), mat=CONCRETE, tint=srgb(0.5, 0.5, 0.47))
    gy = hy1 - 10.0 + 2.5 - 2.5 + 0.0
    gyc = hy0 + (hy1 - hy0 - 10.0 + 2.5)
    # Stair tower up to the gallery (its landings face north), the catwalk across the cavern.
    P.stair_tower(b, (-11.5, gyc - 6.0, 0.0), (0, 0, 180), [0.0, 7.5, 15.0, 22.5, GALLERY_Z], width=1.2)
    P.deck(b, -13.0, gyc - 6.0, -10.0, gyc - 2.5, GALLERY_Z, rails="we")
    P.deck(b, -16.0, gyc - 2.5, 16.0, gyc + 2.5, GALLERY_Z, rails="sn", rail_tint=YELLOW,
           gaps={"s": [(-13.0, -10.0)]})
    for x in (-8.0, 0.0, 8.0):
        b.box((0.4, 0.4, GALLERY_Z), (x, gyc + 2.2, GALLERY_Z / 2), mat=PAINT, tint=P.OLIVE_DARK, vary=0)
    # The checkpoint in the cavern: booths, barriers, a burnt truck, loot.
    for x in (-8.0, 8.0):
        P.room(b, (x, hy0 + 12.0, 0), (0, 0, 0), 4.0, 4.0, 2.8, thick=0.25, mat=PAINT, tint=P.OLIVE_DARK,
               openings={"front": [(0.0, 3.0, 1.0, 1.0)], "back": [(0.0, 1.0, 2.2, 0.0)]})
    b.piece("jersey-barrier", (-2.0, hy0 + 16.0, 0), (0, 0, 0))
    b.piece("jersey-barrier", (2.5, hy0 + 16.5, 0), (0, 0, 10))
    b.piece("wreck-truck", (4.0, hy0 + 24.0, 0), (0, 0, 80), tint=srgb(0.2, 0.2, 0.19))
    b.marker("loot", (-8.0, hy0 + 12.0, 0.1), table="crate")
    b.marker("guard", (0.0, hy0 + 20.0, 0.1), role="reclaimer-heavy")
    for x in (-12.0, 12.0):
        lamp(b, (x, (hy0 + hy1) / 2, 33.6), (90, 0, 0), size=(0.9, 0.5, 0.4))
    # Casemate gallery: west and east from the cavern to the casemates.
    P.tunnel(b, (-16.6, gyc), (-81.5, gyc), 5.0, 3.4, z0=GALLERY_Z, ends=(False, True),
             left=[(80.0 - 16.6, 2.4, 3.0)])
    P.tunnel(b, (16.6, gyc), (81.5, gyc), 5.0, 3.4, z0=GALLERY_Z, ends=(False, True),
             right=[(80.0 - 16.6, 2.4, 3.0)])
    for x in (-80.0, 80.0):
        P.tunnel(b, (x, gyc - 3.1), (x, -209.6), 2.4, 3.0, z0=GALLERY_Z, ribs=0, lamps=0)
    b.marker("guard", (-50.0, gyc, GALLERY_Z + 0.1), role="reclaimer")
    # Vehicle tunnel, motor pool.
    P.tunnel(b, (0.0, hy1 + 0.6), (0.0, -40.0), 12.0, 9.0, lamps=18.0)
    P.room(b, (0.0, -20.0, 0), (0, 0, 0), 50.0, 40.0, 12.0, thick=1.0, mat=CONCRETE, tint=P.BW_CONCRETE,
           openings={"front": [(0.0, 12.0, 9.0, 0.0)], "back": [(0.0, 5.0, 4.2, 0.0)]})
    for k, (x, y, yaw) in enumerate(((-14.0, -28.0, 90), (-14.0, -14.0, 92), (14.0, -24.0, -88))):
        b.piece("wreck-truck", (x, y, 0), (0, 0, yaw))
    b.piece("drum-stack", (18.0, -8.0, 0), (0, 0, 10))
    for p, t in (((16.0, -6.0), "fuel-barrel"), ((19.0, -10.0), "fuel-barrel"), ((-20.0, -4.0), "barrel"),
                 ((0.0, -30.0), "barrel"), ((10.0, -4.0), "crate")):
        b.marker("loot", (p[0], p[1], 0.1), table=t)
    b.marker("guard", (6.0, -18.0, 0.1), role="reclaimer-heavy")
    for x in (-12.0, 12.0):
        b.box((0.6, 38.0, 0.8), (x, -20.0, 11.0), mat=PAINT, tint=YELLOW, solid=False)
        lamp(b, (x, -20.0, 11.6), (90, 0, 0), size=(0.9, 0.5, 0.4))
    # The north corridor, the barracks wing (west) and the storage wing (east).
    P.tunnel(b, (0.0, 0.0), (0.0, 330.0), 5.0, 4.2, left=[(82.5, 4.0, 3.8)], right=[(172.5, 4.0, 3.8)])
    for wy, sgn, label, tables in ((82.5, -1, "barracks", ("crate", "barrel", "crate")),
                                   (172.5, 1, "storage", ("military-crate", "crate", "military-crate"))):
        x_end = sgn * 120.0
        rooms_n = []
        k = 0
        for x in [sgn * (14.0 + 18.0 * i) for i in range(6)]:
            rooms_n.append(abs(x) - 3.1)
        P.tunnel(b, (sgn * 3.1, wy), (x_end, wy), 4.0, 3.8, ends=(False, True),
                 left=[(s, 1.4, 2.3) for s in rooms_n], right=[(s, 1.4, 2.3) for s in rooms_n])
        for i, x in enumerate([sgn * (14.0 + 18.0 * i) for i in range(6)]):
            for side in (-1, 1):
                ry = wy + side * 9.1
                P.room(b, (x, ry, 0), (0, 0, 0 if side > 0 else 180), 14.0, 13.0, 3.6, thick=0.4, mat=CONCRETE,
                       tint=P.BW_CONCRETE, roof=True, openings={"front": [(0.0, 1.4, 2.3, 0.0)]})
                if label == "barracks":
                    for kk in range(3):
                        b.box((2.0, 0.9, 0.5), (x - 4.0 + 4.0 * kk, ry + side * 4.5, 0.6), mat=PAINT,
                              tint=P.OLIVE_DARK, solid=False)
                else:
                    P.crates(b, (x - 3.0, ry + side * 4.0, 0.06), (0, 0, 0), n=5, tint=P.OLIVE_DARK)
                if (i + (side > 0)) % 3 == 0:
                    b.marker("loot", (x + 3.0, ry + side * 3.0, 0.1), table=tables[(i // 2) % 3])
        b.marker("guard", (sgn * 60.0, wy, 0.1), role="reclaimer-heavy" if label == "storage" else "reclaimer")
    # The command bunker at the end: a hall, the armoury, the comms room and the command vault (violet).
    P.room(b, (0.0, 361.0, 0), (0, 0, 0), 50.0, 62.0, 6.0, thick=1.2, mat=CONCRETE, tint=P.BW_CONCRETE,
           openings={"front": [(0.0, 5.0, 4.2, 0.0)]})
    P.room(b, (-16.0, 345.0, 0), (0, 0, 0), 14.0, 14.0, 5.0, thick=0.5, mat=CONCRETE, tint=P.BW_CONCRETE,
           openings={"right": [(0.0, 2.4, 2.6, 0.0)]})
    P.room(b, (16.0, 345.0, 0), (0, 0, 0), 14.0, 14.0, 5.0, thick=0.5, mat=CONCRETE, tint=P.BW_CONCRETE,
           openings={"left": [(0.0, 2.4, 2.6, 0.0)]})
    P.room(b, (0.0, 380.0, 0), (0, 0, 0), 22.0, 18.0, 5.0, thick=0.9, mat=FORMWORK, tint=P.BW_CONCRETE,
           openings={"front": [(0.0, 3.0, 3.0, 0.0)]})
    P.blast_door(b, (0.0, 370.4, 0), (0, 0, 0), w=3.0, h=3.0, depth=1.0, frame=0.8, tint=srgb(0.28, 0.22, 0.38),
                 frame_tint=P.BW_CONCRETE, chip="violet", cell=True, kind="vault-door")
    P.plate(b, (3.6, 369.85, 2.6), (0, 0, 0), 1.6, 0.5, tint=srgb(0.62, 0.25, 1.0), mark=LIGHTGREY, text="C1")
    for x in (-6.0, 6.0):
        b.marker("loot", (x, 384.0, 0.1), table="vault-crate")
    for k in range(4):
        b.box((3.0, 1.0, 1.1), (-6.0 + 4.0 * k, 386.5, 0.55), mat=PAINT, tint=P.BW_GREY, bevel=0.03)
        b.box((2.8, 0.1, 1.6), (-6.0 + 4.0 * k, 388.0, 2.0), mat=PAINT, tint=DARK, solid=False)
    lamp(b, (0.0, 380.0, 4.9), (90, 0, 0), colour=srgb(0.62, 0.25, 1.0), size=(0.6, 0.3, 0.2))
    b.box((12.0, 6.0, 0.9), (0.0, 352.0, 0.45), mat=PAINT, tint=P.BW_GREY, bevel=0.05)
    b.box((11.0, 5.0, 0.05), (0.0, 352.0, 0.93), mat=GLASS, solid=False)
    for p in ((-20.0, 342.0), (-12.0, 348.0)):
        b.marker("loot", (p[0], p[1], 0.1), table="military-crate")
    P.crates(b, (-16.0, 350.0, 0.06), (0, 0, 0), n=6, tint=P.OLIVE_DARK)
    b.marker("loot", (18.0, 342.0, 0.1), table="crate")
    for p in ((-8.0, 360.0), (10.0, 364.0)):
        b.marker("guard", (p[0], p[1], 0.1), role="reclaimer-heavy")
    for x, y in ((-10.0, 345.0), (10.0, 345.0), (0.0, 360.0)):
        lamp(b, (x, y, 5.9), (90, 0, 0), size=(0.6, 0.3, 0.2))


def top_works(b):
    """On the top: the observation post with its mast and dish, vent shafts over the tunnels, a flag mast."""
    ox, oy = 70.0, -196.0
    oz = height(ox, oy)
    P.room(b, (ox, oy, oz - 1.0), (0, 0, 0), 10.0, 7.0, 4.0, thick=0.6, mat=FORMWORK, tint=P.BW_CONCRETE,
           openings={"front": [(0.0, 6.0, 0.5, 2.0)], "back": [(2.5, 1.2, 2.3, 0.0)]}, roof_thick=0.8)
    b.box((12.0, 9.0, 1.2), (ox, oy, oz - 1.4), mat=CONCRETE, tint=P.BW_CONCRETE)
    b.marker("loot", (ox - 2.0, oy + 1.0, oz - 0.9), table="crate")
    lattice_tower(b, (ox + 12.0, oy + 6.0, oz - 1.0), 30.0, 3.0, 1.0, sections=6, member=0.12, tint=GREY)
    P.beacon(b, (ox + 12.0, oy + 6.0, oz + 29.2), 0.5)
    dish(b, (ox - 14.0, oy + 8.0, oz - 1.0), diameter=7.0, elevation=25.0, azimuth=-20.0, tint=LIGHTGREY,
         panels_missing=0.15)
    b.marker("landmark", (ox + 12.0, oy + 6.0, oz + 30.0), kind_of="redoubt-mast", visible_km=14)
    for (x, y) in ((0.0, 80.0), (0.0, 250.0), (-70.0, 82.5), (75.0, 172.5), (0.0, 361.0)):
        z = height(x, y)
        b.cyl(2.6, 6.0, (x, y, z + 1.5), mat=CONCRETE, tint=P.BW_CONCRETE, sides=16)
        b.cyl(2.2, 0.2, (x, y, z + 4.6), mat=GRATE, tint=GREY, sides=16, solid=False)
    fx, fy = -40.0, -205.0
    fz = height(fx, fy)
    b.cyl(0.12, 14.0, (fx, fy, fz + 6.0), mat=PAINT, tint=GREY, sides=8, solid=False)
    b.box((0.05, 3.6, 2.2), (fx, fy - 1.8, fz + 11.6), (0, 0, 8), mat=MATTE, tint=OLIVE, solid=False)


def apron(b):
    """The approach: the road, an apron, revetments, tank traps, sandbag nests, a checkpoint, wrecks, craters."""
    road(b, [(0.0, -400.0, 0), (0.0, -300.0, 0)], width=9.0)
    b.box((96.0, 62.0, 0.3), (0.0, -266.0, 0.15), mat=CONCRETE, tint=srgb(0.58, 0.57, 0.53), wear=0.6)
    for sgn in (-1, 1):
        wall(b, (sgn * 46.0, -238.0, 0), (sgn * 62.0, -300.0, 0), height=5.0, thick=1.0, panel=5.0, broken=0.15,
             mat=FORMWORK, tint=P.BW_CONCRETE)
    # Tank traps in two staggered rows, a gap for the road.
    for row, y in enumerate((-318.0, -330.0)):
        for k in range(14):
            x = -95.0 + k * 14.0 + (7.0 if row else 0.0)
            if abs(x) < 9.0:
                continue
            with b.frame((x, y + b.rng.uniform(-2, 2), 0), (0, 0, b.rng.uniform(0, 90))):
                for rot in ((0, 55, 0), (0, -55, 0), (55, 0, 90)):
                    b.box((0.35, 0.35, 3.4), (0, 0, 1.0), rot, mat=RUST, tint=(1, 1, 1))
    # Sandbag nests either side of the apron, a checkpoint on the road.
    for sgn in (-1, 1):
        cx, cy = sgn * 30.0, -290.0
        pts = [(cx + math.cos(a) * 4.0, cy + math.sin(a) * 4.0, 0) for a in
               [math.radians(200 + 35 * k) for k in range(5)]]
        for pa, pb in zip(pts, pts[1:]):
            sandbags(b, pa, pb, height=3)
        b.marker("guard", (cx, cy, 0.1), role="reclaimer")
    P.room(b, (12.0, -345.0, 0), (0, 0, 0), 4.0, 4.0, 2.8, thick=0.25, mat=PAINT, tint=P.OLIVE_DARK,
           openings={"front": [(0.0, 3.0, 1.0, 1.0)], "left": [(0.5, 1.0, 2.2, 0.0)]})
    b.box((0.4, 0.4, 1.2), (-6.0, -350.0, 0.6), mat=PAINT, tint=DARK)
    b.box((0.25, 0.25, 9.0), (-1.5, -350.0, 1.1), (0, 90, 0), mat=PAINT, tint=RED, solid=False)
    b.piece("wreck-truck", (-22.0, -258.0, 0), (0, 0, 35), tint=srgb(0.18, 0.18, 0.17))
    b.piece("wreck-truck", (38.0, -312.0, 0), (0, 0, -60))
    b.piece("container-40", (-60.0, -340.0, 0), (0, 0, 20), tint=P.OLIVE_DARK)
    for x, y, yaw in ((-44.0, -238.0, 30), (44.0, -238.0, -30)):
        b.piece("flood-mast", (x, y, 0), (0, 0, yaw))
    for k in range(9):
        x = b.rng.uniform(-110.0, 110.0)
        y = b.rng.uniform(-370.0, -250.0)
        P.scorch(b, (x, y, 0.0), 4.0 + 4.0 * b.rng.random(), blobs=3)
        rubble(b, (x, y, 0.0), 4.0, count=5, tint=P.ROCK, max_size=1.2)
    for p, t in (((-26.0, -262.0), "barrel"), ((26.0, -250.0), "crate"), ((-58.0, -336.0), "barrel")):
        b.marker("loot", (p[0], p[1], 0.1), table=t)
    b.marker("spawn", (0.0, -420.0, 0), kind_of="approach")


def build(b):
    b.set_pad(("poly", [(-110.0, -400.0), (110.0, -400.0), (110.0, -236.0), (226.0, -236.0), (226.0, 428.0),
                        (-226.0, 428.0), (-226.0, -236.0), (-110.0, -236.0)]), ground="gravel", blend=40.0,
              clear=12.0)
    rock(b)
    bastion(b)
    casemate(b, -80.0, 8.0)
    casemate(b, 80.0, -10.0)
    interior(b)
    top_works(b)
    apron(b)
    b.zone("no-build", (0, 10.0, 0), size=(500.0, 860.0, 260.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "cliffs", "visible_km": 16})


SUN = (26.0, 220.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((6.0, -250.0, 0.3), 200.0), ((16.0, -276.0, 0.3), 160.0)]
SHIP = ((40.0, -305.0, 0.3), 30.0)
VIEWS = [
    {"name": "far", "target": (0.0, 0.0, 70.0), "distance": 2800, "yaw": 18, "pitch": -0.4, "lens": 85},
    {"name": "overview", "target": (0.0, -120.0, 40.0), "distance": 640, "yaw": 28, "pitch": 24, "lens": 30},
    {"name": "gate", "target": (0.0, -233.0, 13.0), "distance": 64, "yaw": 20, "pitch": -10, "lens": 24},
]
