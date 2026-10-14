"""Mining Camp (MONUMENTS.md §5.2 no. 25, small tier): a Seedward mine at the foot of a mountain. A steel headframe 24 m
tall stands over the shaft, its sheaves turning the ropes down from the winding house; a raised ore bin beside it
over the truck bay. Into the rock face behind goes the adit, its concrete portal timbered inside, a vent duct and the
narrow-gauge track coming out of it to the bins with ore cars on it, one tipped. The workers' huts, the mess hut and
an outhouse on the west side, a compressor, timber for props, a spoil heap against the cliff, a recycler, the light
tower. Ore and gears; an amber chip in the winding house.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, WARM_LAMP, YELLOW,
                        cable, ladder, lamp, railing, srgb, stairs, stripes)
from monkit.surfaces import CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST

KEY = "mining-camp"
TITLE = "Mining Camp"
SEED = 2525
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

ROCK = (srgb(0.5, 0.47, 0.43), srgb(0.44, 0.41, 0.38), srgb(0.56, 0.53, 0.48))
ORE = srgb(0.36, 0.3, 0.27)
HF = Vector((6.0, 6.0, 0.0))        # the headframe's centre, over the shaft
HT = 22.0                           # the sheave deck's height
WH = Vector((6.0, -17.0, 0.0))      # the winding house's centre
PORTAL = Vector((-18.0, 26.0, 0.0))  # the adit's portal, its face line at y = 26
OPEN_W, OPEN_H = 3.6, 4.2
FACE_Y = 26.0


CLIFF_X = (-48.0, 48.0)
BOX = 5.5                           # how far the adit's portal box stands out of the face
# The cliff's cross-section (y, z), front foot to crest to back; points on the face get the strongest noise.
CLIFF = [(FACE_Y, -0.3), (FACE_Y + 0.3, 2.6), (FACE_Y + 0.9, 5.6), (FACE_Y + 1.6, 8.2), (FACE_Y + 2.9, 11.0),
         (FACE_Y + 4.6, 13.6), (FACE_Y + 7.4, 16.2), (FACE_Y + 11.0, 18.0), (FACE_Y + 16.0, 19.2),
         (FACE_Y + 22.0, 19.6), (FACE_Y + 22.0, -0.3)]


def _noise(x, j, f, ph):
    """A deterministic sum of sines along x for profile point j (sections then join seamlessly)."""
    return sum(a * math.sin(x * k + p + j * g) for (a, k, g), p in zip(f, ph))


def cliff(b):
    """The mountain's foot behind the camp: one closed rock mass swept along X through CLIFF, every section displaced
    by layered noise (broad bulges, ledges, small facets) so the face reads as broken rock, its two ends tapered
    down into the ground; quieter where the adit's portal box meets it. Collision as boxes; scree and boulders."""
    x0, x1 = CLIFF_X
    segs = 64
    n = len(CLIFF)
    ph = [b.rng.random() * 6.28 for _ in range(8)]
    fy = ((1.4, 0.21, 0.7), (0.8, 0.53, 1.9), (0.45, 1.37, 2.7), (0.25, 2.9, 4.1))      # (amplitude, x freq, row)
    fz = ((1.6, 0.17, 1.1), (0.9, 0.61, 2.3), (0.4, 1.7, 3.3))
    verts, faces = [], []
    for i in range(segs + 1):
        x = x0 + (x1 - x0) * i / segs
        e = min(x - x0, x1 - x) / 12.0
        taper = 1.0 if e >= 1.0 else 0.5 - 0.5 * math.cos(math.pi * max(0.0, e))
        quiet = 1.0 - math.exp(-((x - PORTAL.x) / 4.5) ** 2)
        for j, (y, z) in enumerate(CLIFF):
            if z <= 0.0:
                verts.append((x, y + (1 - taper) * 6.0 if y < FACE_Y + 1 else y, z))
                continue
            face = y < FACE_Y + 12.0
            dy = _noise(x, j, fy, ph[:4]) * (1.0 if face else 0.3)
            dz = _noise(x, j, fz, ph[4:7])
            if y < FACE_Y + 2.0:
                dy *= 0.35 + 0.65 * quiet
            yy = y + max(-1.0, dy) + (1 - taper) * (6.0 + 0.3 * (y - FACE_Y))
            zz = max(0.4, (z + dz * (0.6 if face else 1.0)) * (0.25 + 0.75 * taper))
            verts.append((x, yy, zz))
    for i in range(segs):
        for j in range(n):
            a = i * n + j
            c = i * n + (j + 1) % n
            faces.append([a, c, c + n, a + n])
    faces.append(list(range(n))[::-1])
    faces.append([segs * n + j for j in range(n)])
    b.mesh(verts, faces, mat=CONCRETE, tint=ROCK[1], solid=False, wear=0.85, vary=0.0, smooth=True)
    # Collision: a box of rock per 6 m, as tall as the cliff there.
    for k in range(16):
        a = x0 + (x1 - x0) * k / 16
        c = x0 + (x1 - x0) * (k + 1) / 16
        e = min((a + c) / 2 - x0, x1 - (a + c) / 2) / 12.0
        h = 17.0 * (0.25 + 0.75 * (1.0 if e >= 1 else 0.5 - 0.5 * math.cos(math.pi * max(0.0, e))))
        b.collide_box(Vector(((a + c) / 2, FACE_Y + 12.0, h / 2)), (c - a, 20.0, h), (0, 0, 0))
    # Scree fans at the foot of the face, boulders fallen from it.
    for k in range(7):
        x = -42.0 + 14.0 * k + b.rng.uniform(-3, 3)
        if abs(x - PORTAL.x) < 7.0 or abs(x - 30.0) < 6.0:
            continue
        P.mound(b, (x, FACE_Y - 0.6, 0.0), b.rng.uniform(3.0, 4.5), b.rng.uniform(1.4, 2.4), tint=ROCK[k % 3],
                rough=0.35, mat=CONCRETE, squash=0.55)
    for k in range(14):
        x = b.rng.uniform(x0 + 6, x1 - 6)
        if abs(x - PORTAL.x) < 6.0:
            continue
        s = b.rng.uniform(0.6, 1.8)
        b.box((s * 1.3, s, s * 0.8), (x, FACE_Y - b.rng.uniform(1.0, 5.0), s * 0.3),
              (b.rng.uniform(-20, 20), b.rng.uniform(-20, 20), b.rng.uniform(0, 90)), mat=CONCRETE,
              tint=ROCK[k % 3], solid=s > 1.0, vary=0.06, wear=0.85)


def adit(b):
    """The adit: a concrete portal box standing 5.5 m out of the rock face (its roof slab, side walls, a headwall with
    wing walls and its timber doors open), timber sets and lagging inside, the dark heading where it enters the
    rock, a vent duct out to its fan, a lamp and the mine's name."""
    x, y = PORTAL.x, FACE_Y - BOX
    hw = OPEN_W / 2
    with b.frame((x, y, 0), (0, 0, 0)):
        for s in (-1, 1):
            P.grid_box(b, (0.6, BOX + 1.0, OPEN_H + 0.6), (s * (hw + 0.3), BOX / 2 + 0.5, (OPEN_H + 0.6) / 2),
                       mat=FORMWORK, tint=CONCRETE_T, step=1.0)
        P.grid_box(b, (OPEN_W + 1.2, BOX + 1.0, 0.6), (0, BOX / 2 + 0.5, OPEN_H + 0.3), mat=FORMWORK, tint=CONCRETE_T,
                   step=1.0)
        P.mound(b, (0, BOX * 0.75, OPEN_H + 0.55), 3.2, 1.4, tint=ROCK[0], rough=0.4, mat=CONCRETE, squash=0.75)
        P.grid_box(b, (OPEN_W + 2.4, 1.0, 1.2), (0, -0.4, OPEN_H + 0.6), mat=FORMWORK, tint=CONCRETE_T, step=1.0)
        for s in (-1, 1):
            P.grid_box(b, (1.2, 1.0, OPEN_H), (s * (hw + 0.6), -0.4, OPEN_H / 2), mat=FORMWORK, tint=CONCRETE_T,
                       step=1.0)
            with b.frame((s * (hw + 1.2), -0.9, 0), (0, 0, s * -25.0)):
                P.grid_box(b, (3.0, 0.6, 2.6), (s * 1.5, 0, 1.3), mat=FORMWORK, tint=CONCRETE_T, step=1.0)
        stripes(b, (-hw - 1.2, -0.92, 0), (hw + 1.2, -0.92, 0), 0.3, width=0.4, at_z=OPEN_H + 0.2)
        P.stencil(b, "ADIT 2", (0, -0.93, OPEN_H + 0.62), (0, 0, 0), 0.4, DARK, align="centre")
        lamp(b, (hw - 0.3, -1.0, OPEN_H - 0.1), (-30, 0, 0), size=(0.36, 0.2, 0.14))
        for s, ang in ((-1, -105.0), (1, 98.0)):
            with b.frame((s * hw, -0.95, 0.0), (0, 0, ang)):
                for k in range(6):
                    b.box((0.3, 0.07, OPEN_H - 0.3), (-s * (0.18 + 0.3 * k), 0, (OPEN_H - 0.3) / 2), mat=PAINT,
                          tint=P.WOOD_GREY, solid=False, vary=0.08)
                for z in (0.6, OPEN_H - 0.9):
                    b.box((1.8, 0.08, 0.16), (-s * 0.9, -0.06, z), mat=PAINT, tint=P.WOOD, solid=False)
        # Timber sets every 1.2 m, lagging boards between them, the dark heading where the box meets the rock.
        depth = BOX + 0.6
        for k in range(int(depth / 1.2) + 1):
            yy = 0.2 + 1.2 * k
            for s in (-1, 1):
                b.box((0.24, 0.24, OPEN_H - 0.25), (s * (hw - 0.15), yy, (OPEN_H - 0.25) / 2), (0, s * 3.0, 0),
                      mat=PAINT, tint=P.WOOD, solid=False, vary=0.08)
            b.box((OPEN_W, 0.26, 0.26), (0, yy, OPEN_H - 0.13), mat=PAINT, tint=P.WOOD, solid=False, vary=0.08)
        for s in (-1, 1):
            P.grid_box(b, (0.06, depth, OPEN_H), (s * (hw - 0.02), depth / 2 + 0.1, OPEN_H / 2), mat=PAINT,
                       tint=srgb(0.3, 0.26, 0.2), step=1.2)
        P.grid_box(b, (OPEN_W, depth, 0.06), (0, depth / 2 + 0.1, OPEN_H + 0.02), mat=PAINT, tint=srgb(0.3, 0.26, 0.2),
                   step=1.2)
        b.box((OPEN_W, 0.1, OPEN_H), (0, depth + 0.1, OPEN_H / 2), mat=MATTE, tint=srgb(0.02, 0.02, 0.02))
        b.box((OPEN_W, depth, 0.08), (0, depth / 2 + 0.1, 0.04), mat=CONCRETE, tint=srgb(0.3, 0.28, 0.26))
        for k in range(3):
            b.box((0.06, 0.06, 0.12), (hw - 0.5, 2.0 + 4.0 * k, OPEN_H - 0.4), mat=LAMP, tint=WARM_LAMP, solid=False,
                  vary=0)
        for s in (-1, 1):
            b.collide_box(Vector((s * hw, depth / 2, OPEN_H / 2)), (0.2, depth, OPEN_H), (0, 0, 0))
        b.collide_box(Vector((0, depth + 0.1, OPEN_H / 2)), (OPEN_W, 0.2, OPEN_H), (0, 0, 0))
        b.marker("door", (0, -1.6, 0.0), kind_of="adit")
        # The vent duct along the roof and out to its fan on a stand by the portal.
        b.tube([(hw - 0.5, depth - 0.2, OPEN_H - 0.55), (hw - 0.5, 0.0, OPEN_H - 0.55), (hw - 0.5, -2.0, OPEN_H - 0.55),
                (hw + 2.4, -3.2, 1.6)], 0.32, mat=MATTE, tint=srgb(0.82, 0.62, 0.2), sides=12)
        with b.frame((hw + 2.8, -3.6, 0.0), (0, 0, -30.0)):
            for xx in (-0.6, 0.6):
                b.box((0.12, 1.2, 0.9), (xx, 0, 0.45), mat=PAINT, tint=DARK, solid=False)
            b.cyl(0.55, 1.4, (0, 0, 1.55), (90, 0, 0), mat=PAINT, tint=srgb(0.3, 0.4, 0.36), sides=16)
            b.cyl(0.6, 0.08, (0, -0.72, 1.55), (90, 0, 0), mat=GRATE, tint=GREY, sides=16, solid=False)
            b.box((0.5, 0.5, 0.5), (0, 0.95, 1.55), mat=PAINT, tint=DARK)
    b.marker("loot", (x, y + 2.0, 0.1), table="ore")


def headframe(b):
    """The headframe: a four-legged steel tower in angle lattice over the shaft collar, the backstay raking toward the
    winding house, the sheave deck with its two wheels, ladders and landings, the cage at the collar, the ore bin and
    its chute on the east side."""
    x, y = HF.x, HF.y
    for (dx, dy) in ((-2.4, -2.4), (2.4, -2.4), (2.4, 2.4), (-2.4, 2.4)):
        P.grid_box(b, (1.4, 1.4, 0.8), (x + dx, y + dy, 0.2), mat=CONCRETE, tint=CONCRETE_T, step=0.7)
    with b.frame((x, y, 0.6), (0, 0, 0)):
        P.lattice(b, 0.0, HT - 0.6, 4.8, 3.0, 7, member=0.2, tint=srgb(0.3, 0.33, 0.34), solid=True)
    # Backstay: two raking legs from the deck down toward the winding house.
    for s in (-1, 1):
        top = Vector((x + s * 1.4, y - 1.4, HT - 0.4))
        foot = Vector((x + s * 2.2, y - 13.5, 0.6))
        P.rod(b, foot, top, 0.22, tint=srgb(0.3, 0.33, 0.34), sides=8, solid=True)
        P.grid_box(b, (1.4, 1.4, 0.8), (foot.x, foot.y, 0.2), mat=CONCRETE, tint=CONCRETE_T, step=0.7)
        for t in (0.33, 0.66):
            p = foot.lerp(top, t)
            P.rod(b, p, Vector((x + s * (2.4 - 0.6 * t * 2), y - 2.4 + 0.8 * t, p.z)), 0.1,
                  tint=srgb(0.3, 0.33, 0.34), sides=6)
    # The sheave deck, its rails, the two wheels, the roof over them.
    b.box((4.4, 4.4, 0.12), (x, y, HT + 0.06), mat=GRATE, tint=GREY, solid=True, vary=0)
    from monkit.kit import rail_loop
    rail_loop(b, [(x - 2.2, y - 2.2, HT + 0.12), (x + 2.2, y - 2.2, HT + 0.12), (x + 2.2, y + 2.2, HT + 0.12),
                  (x - 2.2, y + 2.2, HT + 0.12)], tint=YELLOW, gap=3)
    for s in (-1, 1):
        P.sheave(b, (x + s * 0.9, y - 0.1, HT + 1.8), 90.0, radius=1.6)
        b.box((0.3, 2.6, 0.3), (x + s * 0.9, y - 0.1, HT + 0.3), mat=PAINT, tint=DARK)
    b.box((4.6, 4.0, 0.08), (x, y, HT + 3.9), (6, 0, 0), mat=CORRUGATED, tint=srgb(0.5, 0.52, 0.5))
    for (dx, dy) in ((-2.2, -1.9), (2.2, -1.9), (-2.2, 1.9), (2.2, 1.9)):
        b.box((0.12, 0.12, 3.8), (x + dx, y + dy, HT + 2.0), mat=PAINT, tint=DARK, solid=False)
    b.box((0.3, 0.3, 0.3), (x, y + 2.0, HT + 4.2), mat=LAMP, tint=srgb(1.0, 0.12, 0.05), solid=False)
    b.marker("light", (x, y + 2.0, HT + 4.4), colour=[1.0, 0.12, 0.05], range=500.0, blink=1.0)
    b.marker("landmark", (x, y, HT + 4.0), kind_of="headframe")
    # Ropes: from the winding house drums up over the sheaves, and down into the shaft to the cage.
    for s in (-1, 1):
        rx = x + s * 0.9
        cable(b, (rx, WH.y + 4.2, 5.2), (rx, y - 1.7, HT + 2.2), sag=0.005, radius=0.03, tint=BLACK, segments=6)
        cable(b, (rx, y + 1.5, HT + 1.8), (rx, y + 1.5, 3.4), sag=0.0, radius=0.03, tint=BLACK, segments=2)
    # Ladder up the south face with landings every 7 m.
    for z0, z1 in ((0.0, 7.0), (7.0, 14.0), (14.0, HT)):
        ladder(b, (x - 1.2, y - 2.75, z0), z1 - z0 + 0.12, (0, 0, 0), cage=True)
        if z1 < HT:
            b.box((1.6, 1.2, 0.06), (x - 1.2, y - 3.2, z1 + 0.09), mat=GRATE, tint=GREY, solid=True, vary=0)
            railing(b, (x - 2.0, y - 3.8, z1 + 0.12), (x - 0.4, y - 3.8, z1 + 0.12), 1.1, spacing=1.0, tint=YELLOW)
    # The shaft collar, the cage at it, gates.
    P.grid_box(b, (4.2, 4.2, 0.9), (x, y, 0.45), mat=FORMWORK, tint=CONCRETE_T, step=0.7)
    b.box((2.4, 3.0, 0.02), (x, y + 0.6, 0.91), mat=MATTE, tint=srgb(0.02, 0.02, 0.02), solid=False, vary=0)
    with b.frame((x, y + 1.5, 0.9), (0, 0, 0)):
        for (cx, cy) in ((-0.8, -1.0), (0.8, -1.0), (-0.8, 1.0), (0.8, 1.0)):
            b.box((0.08, 0.08, 2.5), (cx, cy, 1.25), mat=PAINT, tint=srgb(0.6, 0.55, 0.3), solid=False, vary=0)
        b.box((1.7, 2.1, 0.1), (0, 0, 0.05), mat=GRATE, tint=GREY, solid=False, vary=0)
        b.box((1.7, 2.1, 0.12), (0, 0, 2.5), mat=PAINT, tint=srgb(0.6, 0.55, 0.3), solid=False)
        for (cx, cy, sx, sy) in ((-0.82, 0, 0.02, 2.0), (0.82, 0, 0.02, 2.0), (0, 1.02, 1.6, 0.02)):
            b.box((sx, sy, 2.3), (cx, cy, 1.25), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
        b.box((0.3, 0.3, 0.4), (0, 0, 2.75), mat=PAINT, tint=DARK, solid=False)
    for s in (-1, 1):
        b.box((0.08, 0.08, 1.2), (x + s * 0.9, y - 0.2, 1.5), mat=PAINT, tint=YELLOW, solid=False)
    b.box((1.9, 0.06, 0.06), (x, y - 0.2, 2.0), mat=PAINT, tint=YELLOW, solid=False)
    b.marker("door", (x, y - 1.2, 0.9), kind_of="cage")
    # The ore bin on the east side, its chute over the truck bay.
    bx, by = x + 6.5, y
    for (dx, dy) in ((-1.8, -1.8), (1.8, -1.8), (-1.8, 1.8), (1.8, 1.8)):
        b.box((0.35, 0.35, 8.0), (bx + dx, by + dy, 4.0), mat=PAINT, tint=srgb(0.3, 0.33, 0.34))
    for (ax, ay, cx, cy) in ((-1.8, -1.8, 1.8, -1.8), (-1.8, 1.8, 1.8, 1.8)):
        P.rod(b, (bx + ax, by + ay, 0.4), (bx + cx, by + cy, 4.5), 0.06, tint=GREY, sides=5, trim=0.2)
    P.grid_box(b, (4.0, 4.0, 3.2), (bx, by, 8.0 + 1.6), mat=CORRUGATED, tint=srgb(0.55, 0.42, 0.3), step=1.2)
    b.cyl(0.4, 1.4, (bx, by, 7.3), (0, 0, 45), mat=PAINT, tint=srgb(0.55, 0.42, 0.3), sides=4, radius2=2.8, cap=False,
          solid=False)
    b.box((0.9, 2.4, 0.1), (bx + 1.0, by, 5.6), (0, 30, 90), mat=PAINT, tint=GREY)
    P.mound(b, (bx, by, 11.2), 1.8, 0.7, tint=ORE, rough=0.3, mat=CONCRETE)
    b.box((3.0, 0.15, 0.1), (x + 3.6, y, HT - 6.0), (0, 32, 0), mat=PAINT, tint=GREY, solid=False)
    P.stencil(b, "ORE", (bx, by - 2.02, 9.9), (0, 0, 0), 0.6, OFFWHITE, align="centre")
    for s in (-1, 1):
        b.box((0.4, 6.0, 0.35), (bx + 3.0, by + s * 2.6, 0.17), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
    b.marker("loot", (bx, by - 3.0, 0.05), table="ore")


def winding_house(b):
    """The winding house: a steel-framed shed of corrugated sheet with a gable roof, a high window toward the headframe
    for the ropes, a door; inside, the two drums with their brakes, the motor, the driver's chair and levers."""
    x, y = WH.x, WH.y
    w, d, h = 9.0, 9.0, 6.0
    hw, hd = w / 2, d / 2
    P.grid_box(b, (w + 0.6, d + 0.6, 0.3), (x, y, 0.15), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), step=2.0)
    z0 = 0.3
    clad = srgb(0.5, 0.53, 0.5)
    for s in (-1, 1):
        P.grid_box(b, (0.1, d, h), (x + s * hw, y, z0 + h / 2), mat=CORRUGATED, tint=clad, step=1.5)
    P.grid_box(b, (w, 0.1, h), (x, y - hd, z0 + h / 2), mat=CORRUGATED, tint=clad, step=1.5)
    # The +Y wall toward the headframe: the rope window over the drums, a door at its side.
    rw0, rw1 = -2.0, 2.0
    P.grid_box(b, (w, 0.1, 3.6), (x, y + hd, z0 + 1.8), mat=CORRUGATED, tint=clad, step=1.5)
    P.grid_box(b, (w, 0.1, h - 5.2), (x, y + hd, z0 + 5.2 + (h - 5.2) / 2), mat=CORRUGATED, tint=clad, step=1.5)
    for (a, c) in ((-hw, rw0), (rw1, hw)):
        P.grid_box(b, (c - a, 0.1, 1.6), (x + (a + c) / 2, y + hd, z0 + 4.4), mat=CORRUGATED, tint=clad, step=1.5)
    b.box((rw1 - rw0, 0.12, 0.12), (x, y + hd, z0 + 3.6), mat=PAINT, tint=DARK, solid=False)
    rise = 2.2
    for s in (-1, 1):
        ln = math.hypot(hw + 0.4, rise)
        ang = math.degrees(math.atan2(rise, hw + 0.4))
        P.grid_box(b, (ln, d + 0.6, 0.07), (x + s * (hw + 0.4) / 2, y, z0 + h + rise / 2), (0, s * ang, 0),
                   mat=CORRUGATED, tint=srgb(0.4, 0.42, 0.42), step=1.5)
    for yy in (y - hd, y + hd):
        b.mesh([(x - hw, yy, z0 + h), (x + hw, yy, z0 + h), (x, yy, z0 + h + rise)],
               [[0, 1, 2] if yy < y else [0, 2, 1]], mat=CORRUGATED, tint=clad, solid="hull")
    # The door in the -Y wall (cut as a dark doorway with the leaf open), its lamp.
    b.box((1.2, 0.12, 2.3), (x - 2.5, y - hd - 0.02, z0 + 1.15), mat=MATTE, tint=srgb(0.03, 0.03, 0.03),
          solid=False, vary=0)
    with b.frame((x - 1.9, y - hd - 0.06, z0), (0, 0, -100.0)):
        P.grid_box(b, (1.2, 0.05, 2.28), (-0.6, 0, 1.14), mat=PAINT, tint=srgb(0.55, 0.2, 0.14), step=0.8)
    lamp(b, (x - 2.5, y - hd - 0.25, z0 + 2.8), (-25, 0, 0), size=(0.3, 0.16, 0.12))
    b.marker("door", (x - 2.5, y - hd - 0.8, 0.0), kind_of="winding-house")
    P.stencil(b, "WIND 1", (x + 1.8, y - hd - 0.06, z0 + 4.4), (0, 0, 0), 0.4, DARK, align="centre")
    # The drums, their brakes and motor, the driver's stand.
    for s in (-1, 1):
        dx = x + s * 0.9
        with b.frame((dx, y + 1.5, z0 + 1.6), (0, 0, 0)):
            b.cyl(1.2, 1.3, (0, 0, 0), (0, 90, 0), mat=PAINT, tint=srgb(0.3, 0.33, 0.34), sides=24)
            for k in range(2):
                b.cyl(1.4, 0.1, (s * (0.65 - 1.3 * k), 0, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=24, solid=False)
            b.cyl(1.22, 1.1, (0, 0, 0), (0, 90, 0), mat=MATTE, tint=srgb(0.08, 0.08, 0.08), sides=24, solid=False)
            b.box((0.5, 0.5, 1.6), (0, 0, -0.8), mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(0.12, 3.6, (x, y + 1.5, z0 + 1.6), (0, 90, 0), mat=PAINT, tint=GREY, sides=10, solid=False)
    b.box((1.6, 1.2, 1.4), (x + 3.0, y + 1.5, z0 + 0.7), mat=PAINT, tint=srgb(0.25, 0.4, 0.5), bevel=0.05)
    with b.frame((x - 2.0, y - 1.6, z0), (0, 0, 0)):
        b.box((1.6, 1.2, 0.6), (0, 0, 0.3), mat=PAINT, tint=DARK)
        b.box((0.5, 0.5, 0.1), (0, -0.1, 0.95), mat=MATTE, tint=srgb(0.2, 0.2, 0.22), solid=False)
        b.box((0.5, 0.1, 0.6), (0, 0.2, 1.25), mat=MATTE, tint=srgb(0.2, 0.2, 0.22), solid=False)
        for k, xx in enumerate((-0.5, 0.5)):
            b.box((0.05, 0.05, 0.9), (xx, -0.4, 1.0), (-15 + 30 * k, 0, 0), mat=PAINT, tint=RED, solid=False)
        b.box((1.2, 0.3, 0.8), (0, 0.55, 1.0), mat=PAINT, tint=LIGHTGREY, solid=False)
        for k in range(3):
            b.cyl(0.1, 0.04, (-0.35 + 0.35 * k, 0.39, 1.15), (90, 0, 0), mat=PAINT, tint=P.SIGN_WHITE, sides=12,
                  solid=False)
    b.marker("loot", (x - 2.8, y - 2.6, z0 + 0.05), table="chip-amber", chance=0.4)
    b.marker("loot", (x + 3.0, y - 2.6, z0 + 0.05), table="gears")


def hut(b, at, yaw, label, tint=OFFWHITE, band=ORANGE):
    floor = P.prefab_hut(b, at, (0, 0, yaw), w=5.0, d=3.0, h=2.5, code=label, door_open=b.rng.uniform(30, 100),
                         windows=2, band=band, tint=tint)
    return floor


def build(b):
    b.set_pad(("rect", 96.0, 66.0), ground="dirt", blend=12.0, clear=4.0)
    cliff(b)
    adit(b)
    headframe(b)
    winding_house(b)

    # The track from the adit to the bins, ore cars along it, one tipped and its load spilled; a buffer stop.
    track = [(PORTAL.x, FACE_Y - 0.3), (PORTAL.x, FACE_Y - BOX - 1.0), (PORTAL.x + 1.0, 16.5), (PORTAL.x + 6.0, 11.5),
             (0.0, 9.0), (8.0, 9.6), (11.5, 9.6)]
    P.narrow_track(b, track)
    for (cx, cy, yaw, tip, load) in ((PORTAL.x, 22.0, 0.0, 0.0, ORE), (PORTAL.x + 0.6, 18.4, 8.0, 0.0, ORE),
                                     (PORTAL.x + 3.0, 14.4, 35.0, 0.0, None), (-4.0, 9.6, 78.0, 0.0, ORE)):
        P.ore_cart(b, (cx, cy, 0.12), yaw, tip, load)
    with b.frame((-9.0, 12.6, 0.0), (0, 0, 60.0)):
        P.ore_cart(b, (1.6, 0.0, 0.15), 0.0, 0.0, None)
    with b.frame((-10.4, 14.0, 0.5), (0, 72.0, 30.0)):
        P.ore_cart(b, (0, 0, 0), 0.0, 0.0, None)
    P.mound(b, (-11.8, 15.8, 0.0), 1.6, 0.6, tint=ORE, rough=0.4, mat=CONCRETE)
    with b.frame((12.2, 9.6, 0.0), (0, 0, 90.0)):
        b.box((1.2, 0.3, 0.6), (0, 0, 0.4), mat=PAINT, tint=RED)
        stripes(b, (-0.6, -0.16, 0), (0.6, -0.16, 0), 0.6, width=0.2, colours=(RED, P.SIGN_WHITE), at_z=0.1)

    # The camp: three huts, the mess hut, the outhouse, a washing line, a fire barrel.
    hut(b, (-36.0, -6.0, 0.0), 90.0, "HUT 1")
    hut(b, (-36.0, -14.0, 0.0), 90.0, "HUT 2", tint=srgb(0.82, 0.8, 0.72))
    hut(b, (-29.0, -22.0, 0.0), 0.0, "HUT 3", band=TEAL)
    mess = P.prefab_hut(b, (-25.0, -8.0, 0.0), (0, 0, 180.0), w=8.0, d=4.0, h=2.7, code="MESS", door_open=80.0,
                        windows=3, band=TEAL)
    b.marker("loot", tuple(mess), table="food")
    with b.frame((-42.0, -24.0, 0.0), (0, 0, 20.0)):
        P.grid_box(b, (1.2, 1.2, 2.2), (0, 0, 1.1), mat=PAINT, tint=P.WOOD_GREY, step=0.6)
        b.box((1.4, 1.4, 0.08), (0, 0.1, 2.25), (-8, 0, 0), mat=CORRUGATED, tint=srgb(0.5, 0.52, 0.5))
        with b.frame((0.45, -0.62, 0.05), (0, 0, -70)):
            b.box((0.9, 0.05, 2.0), (-0.45, 0, 1.0), mat=PAINT, tint=P.WOOD_GREY, solid=False)
    for (px, py) in ((-31.0, -12.0), (-31.0, -4.0)):
        b.cyl(0.05, 2.2, (px, py, 1.1), mat=PAINT, tint=P.WOOD_GREY, sides=6, solid=False)
    cable(b, (-31.0, -12.0, 2.1), (-31.0, -4.0, 2.1), sag=0.02, radius=0.005, tint=srgb(0.8, 0.8, 0.78), segments=5)
    for k, (yy, tint) in enumerate(((-10.5, srgb(0.3, 0.38, 0.5)), (-9.0, ORANGE), (-7.0, srgb(0.4, 0.42, 0.34)))):
        b.box((0.02, 0.6, 0.8), (-31.0, yy, 1.6), mat=MATTE, tint=tint, solid=False)
    with b.frame((-27.5, -14.0, 0.0), (0, 0, 0)):
        b.cyl(0.3, 0.9, (0, 0, 0.45), mat=RUST, tint=(1, 1, 1), sides=12, cap=False)
        P.blob(b, (0, 0, 0.0), 0.8, P.SCORCH, points=10, rough=0.3)
        for k in range(4):
            a = math.radians(90 * k + 20)
            b.box((0.35, 0.35, 0.4), (1.2 * math.cos(a), 1.2 * math.sin(a), 0.2), (0, 0, 40 * k), mat=PAINT,
                  tint=P.WOOD, solid=False)

    # Work yard: compressor, timber props, gas bottles, a spoil heap against the cliff, the recycler, light tower.
    with b.frame((-12.0, 21.0, 0.0), (0, 0, 15.0)):
        b.box((2.6, 1.4, 1.3), (0, 0, 0.85), mat=PAINT, tint=P.MINE_YELLOW, bevel=0.05)
        for s in (-1, 1):
            P.wheel(b, (s * 0.8, 0.0, 0.32), (0, 0, 0), 0.32, 0.2, sides=10)
        b.beam((-1.3, 0, 0.6), (-2.4, 0, 0.4), 0.1, 0.1, mat=PAINT, tint=DARK)
        b.cyl(0.08, 0.5, (0.6, 0.3, 1.7), mat=PAINT, tint=DARK, sides=8, solid=False)
    P.ground_cable(b, [(-10.8, 21.5, 0.6), (-14.0, 20.0, 0.03), (PORTAL.x + 1.2, FACE_Y - BOX - 0.5, 0.03),
                       (PORTAL.x + 1.2, FACE_Y - 1.0, 0.2)], 0.035, BLACK, wobble=0.3, step=0.8)
    for k in range(14):
        layer = k // 5
        b.cyl(0.16, 3.6, (-4.0 + 0.36 * (k % 5) + 0.18 * layer, 18.0, 0.18 + 0.3 * layer), (90, 0, 3 * (k % 3)),
              mat=PAINT, tint=P.WOOD, sides=8, solid=False, vary=0.08)
    b.collide_box(Vector((-3.2, 18.0, 0.5)), (2.2, 3.6, 1.0), (0, 0, 0))
    P.mound(b, (30.0, 21.0, 0.0), 8.0, 5.0, tint=ROCK[1], rough=0.3, mat=CONCRETE, squash=0.7)
    P.clods(b, (30.0, 21.0, 0.0), 11.0, count=14, tint=ROCK[0], size=0.6, inner=7.0)
    b.piece("recycler", (20.0, -16.0, 0.0), (0, 0, 180.0))
    b.piece("drum-stack", (17.0, -10.0, 0.0), (0, 0, 15.0))
    b.marker("loot", (16.0, -8.8, 0.05), table="barrel")
    with b.frame((-4.0, -4.0, 0.0), (0, 0, 30.0)):
        b.box((1.4, 3.0, 1.1), (0, 0, 0.95), mat=PAINT, tint=P.MINE_YELLOW, bevel=0.05)
        for s in (-1, 1):
            P.wheel(b, (s * 0.85, 0.3, 0.38), (0, 0, 0), 0.38, 0.22, sides=12)
        b.cyl(0.12, 5.5, (0, 0.9, 4.2), mat=PAINT, tint=GREY, sides=10)
        b.box((1.8, 0.12, 0.12), (0, 0.9, 7.0), mat=PAINT, tint=DARK, solid=False)
        for dx in (-0.65, -0.22, 0.22, 0.65):
            lamp(b, (dx, 0.75, 7.25), (-25, 0, 0), size=(0.36, 0.2, 0.3))
    for (x, y, yaw) in ((-44.0, -30.0, 135.0), (40.0, -30.0, 45.0)):
        b.piece("lamp-post", (x, y, 0.0), (0, 0, yaw))
    # The haul road in from the south to the truck bay.
    for off in (-1.3, 1.3):
        P.ribbon(b, [(16.0 + off, -34.0), (16.0 + off, -20.0), (15.0 + off, -4.0), (15.0 + off, 6.0)], 0.8, 0.0,
                 P.EARTH_DARK, mat=CONCRETE, step=2.0)
    for (x, y, s) in ((-46.0, 10.0, 1.4), (44.0, -6.0, 1.2), (-20.0, -30.0, 1.0), (30.0, -28.0, 1.1),
                      (-44.0, 22.0, 1.6), (42.0, 12.0, 1.5)):
        P.bush(b, (x, y, 0.0), s, P.LEAF if s > 1.1 else P.LEAF_DRY, lumps=3)

    b.zone("no-build", (0, 0, 0), size=(106.0, 80.0, 40.0))
    b.meta.update({"tier": "small", "landmark": True, "spawn": "mountains"})


SUN = (32.0, 210.0)
PEOPLE = [((2.0, -4.0, 0.0), 20.0)]
VIEWS = [
    {"name": "camp", "target": (0.0, 4.0, 6.0), "distance": 100, "yaw": 22, "pitch": 22, "lens": 30},
    {"name": "headframe", "target": (6.0, 0.0, 12.5), "distance": 44, "yaw": -34, "pitch": 4, "lens": 28},
    {"name": "adit", "target": (-17.0, 22.0, 2.4), "distance": 15, "yaw": -18, "pitch": 4, "lens": 24},
]
