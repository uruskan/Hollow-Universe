"""Strip Mine (MONUMENTS.md §5.2 no. 20, small tier), the design shared by its three variants: strip_mine_stone.py,
strip_mine_metal.py and strip_mine_sulphur.py (this file starts with an underscore, so the site loader skips it).

A Seedward bench mine cut into a hill: a horseshoe of three 4 m benches round a pit floor about 60 x 44 m, a ring of
spoil outside, the haul road coming in through a cut at the front. On the floor, the fuel-driven bench miner chews at
the lowest face and throws its cut onto a conveyor to a hopper; a second conveyor climbs to the output bin over the
truck bay at the entrance. Steel stairs up the benches, a light tower, the site office, fuel tanks, a haul truck,
stockpiles of the ore. Fill the miner with fuel and the bin slowly fills with ore. The variants differ in the rock's
colours and the ore, and in what lies about.

Everything stands on the pad (z = 0 is the pit floor), so the terrain needs no carving: the spoil ring is the hill.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, ladder, lamp,
                        railing, srgb, stairs, stripes)
from monkit.structures import conveyor
from monkit.surfaces import CONCRETE, CORRUGATED, GLASS, GRATE, MATTE, PAINT, RUST

CX, CY = 0.0, 6.0           # the pit floor's centre
A, B = 30.0, 22.0           # its half-axes
BENCH = 4.0                 # bench height
STEP = 6.0                  # horizontal offset from one bench's foot to the next
BATTER = 0.8                # how far a face leans back over its height
RIM = 3 * STEP + 3.7        # the rim plateau's outer edge
FOOT = 31.0                 # where the spoil's outer slope meets the ground
GAP = 28.0                  # half the entrance cut, degrees either side of the front (-Y)
SEGS = 76

VARIANTS = {
    "stone": {"walls": (srgb(0.74, 0.72, 0.68), srgb(0.66, 0.64, 0.6), srgb(0.78, 0.76, 0.71)),
              "benches": srgb(0.6, 0.57, 0.52), "spoil": srgb(0.52, 0.46, 0.36), "ore": srgb(0.72, 0.7, 0.66),
              "ore2": srgb(0.6, 0.58, 0.55), "code": "QS-1", "label": "STONE"},
    "metal": {"walls": (srgb(0.56, 0.34, 0.24), srgb(0.4, 0.28, 0.24), srgb(0.6, 0.4, 0.28)),
              "benches": srgb(0.46, 0.34, 0.27), "spoil": srgb(0.5, 0.38, 0.29), "ore": srgb(0.34, 0.24, 0.22),
              "ore2": srgb(0.5, 0.3, 0.22), "code": "QM-4", "label": "ORE"},
    "sulphur": {"walls": (srgb(0.74, 0.7, 0.5), srgb(0.86, 0.76, 0.3), srgb(0.66, 0.64, 0.56)),
                "benches": srgb(0.56, 0.54, 0.48), "spoil": srgb(0.55, 0.5, 0.4), "ore": srgb(0.88, 0.78, 0.22),
                "ore2": srgb(0.8, 0.72, 0.3), "code": "QY-7", "label": "SULPHUR"},
}


def pt(theta, off, z):
    """A point on the pit's outline `off` metres out from the floor's edge at angle `theta` (degrees, 0 = +X)."""
    t = math.radians(theta)
    return (CX + (A + off) * math.cos(t), CY + (B + off) * math.sin(t), z)


def strip(b, rows, tint, rough=0.35, mat=CONCRETE, wear=0.8):
    """A surface swept round the horseshoe through `rows` [(offset, z, jitter)...] (inside to outside), its ends
    capped by the end rows' polygon; jitter roughens a row radially and in height. Visual only (collision is added
    separately)."""
    t0, t1 = -90.0 + GAP, 270.0 - GAP
    verts, faces = [], []
    nr = len(rows)
    ph = [b.rng.random() * 6.28 for _ in range(3)]
    for i in range(SEGS + 1):
        th = t0 + (t1 - t0) * i / SEGS
        for (off, z, jit) in rows:
            n = jit * (0.6 * math.sin(math.radians(th) * 9 + ph[0]) + 0.4 * (b.rng.random() - 0.5) * 2)
            m = jit * 0.5 * math.sin(math.radians(th) * 5 + ph[1])
            verts.append(pt(th, off + n, z + m))
    for i in range(SEGS):
        for j in range(nr - 1):
            a = i * nr + j
            faces.append([a, a + nr, a + nr + 1, a + 1])
    b.mesh(verts, faces, mat=mat, tint=tint, solid=False, wear=wear, vary=0.02, smooth=True)


def pit(b, v):
    """The benches, their faces and talus, the rim and the spoil slope, with collision boxes for the rock."""
    walls = v["walls"]
    for i in range(3):
        o0, z0 = i * STEP, i * BENCH
        strip(b, [(o0 - 0.1, z0 - 0.05, 0.0), (o0 + BATTER * 0.45, z0 + BENCH * 0.5, 0.35),
                  (o0 + BATTER, z0 + BENCH, 0.25)], walls[i])
        top = [(o0 + BATTER, z0 + BENCH, 0.25), ((o0 + STEP) * 0.5 + (o0 + BATTER) * 0.5, z0 + BENCH + 0.05, 0.15),
               (o0 + STEP - 0.1, z0 + BENCH - 0.02, 0.0)] if i < 2 else \
            [(o0 + BATTER, z0 + BENCH, 0.25), (RIM - 1.0, z0 + BENCH + 0.3, 0.3), (RIM, z0 + BENCH + 0.1, 0.2)]
        strip(b, top, v["benches"])
    strip(b, [(RIM, 3 * BENCH + 0.1, 0.2), (RIM + 3.0, 3 * BENCH - 1.6, 0.6), (RIM + 6.0, 6.0, 0.8),
              (FOOT - 3.0, 1.6, 0.6), (FOOT, -0.1, 0.0)], v["spoil"], rough=0.5)
    # The two cut faces of the entrance, where the horseshoe ends.
    for th in (-90.0 + GAP, 270.0 - GAP):
        prof = [(0.0, 0.0), (BATTER, BENCH), (STEP, BENCH), (STEP + BATTER, 2 * BENCH), (2 * STEP, 2 * BENCH),
                (2 * STEP + BATTER, 3 * BENCH), (RIM, 3 * BENCH + 0.1), (RIM + 3.0, 3 * BENCH - 1.6),
                (RIM + 6.0, 6.0), (FOOT, 0.0)]
        verts = [pt(th, o, z) for o, z in prof]
        face = list(range(len(verts)))
        if th > 90:
            face = face[::-1]
        b.mesh(verts, [face], mat=CONCRETE, tint=walls[1], solid=False, wear=0.8, vary=0.02, smooth=False)
    # Talus along the foot of every face, boulders on the benches.
    for i in range(3):
        o0, z0 = i * STEP, i * BENCH
        for k in range(4):
            th0 = -90.0 + GAP + 4 + k * 76
            pts = [pt(th0 + d, o0 + 1.1, z0)[:2] for d in range(0, 60, 12)]
            P.berm(b, pts, width=2.0, height=0.7, tint=walls[i], mat=CONCRETE, solid=False)
        for k in range(10):
            th = b.rng.uniform(-90.0 + GAP + 4, 270.0 - GAP - 4)
            p = pt(th, o0 + BATTER + b.rng.uniform(0.8, STEP - 1.5) if i < 2 else o0 + b.rng.uniform(2, 6),
                   z0 + BENCH)
            s = 0.5 + 1.0 * b.rng.random()
            b.box((s, s * 0.8, s * 0.6), (p[0], p[1], p[2] + s * 0.2), (b.rng.uniform(-20, 20), b.rng.uniform(-20, 20),
                                                                       b.rng.uniform(0, 360)),
                  mat=CONCRETE, tint=walls[i], solid=s > 1.0, vary=0.08, wear=0.8)
    # Collision: per segment, a box of rock under each level, from that level's face to the rim, and the outer slope.
    t0, t1 = -90.0 + GAP, 270.0 - GAP
    n = 38
    for k in range(n):
        th = t0 + (t1 - t0) * (k + 0.5) / n
        arc = math.radians((t1 - t0) / n) * (A + B) / 2
        for i in range(3):
            o_in = i * STEP + BATTER * 0.5
            o_out = RIM
            p0, p1 = Vector(pt(th, o_in, 0.0)), Vector(pt(th, o_out, 0.0))
            mid = (p0 + p1) / 2
            yaw = math.degrees(math.atan2(p1.y - p0.y, p1.x - p0.x))
            h = (i + 1) * BENCH
            b.collide_box(Vector((mid.x, mid.y, h / 2)), ((p1 - p0).length, arc * (1 + (o_in + o_out) / 60), h),
                          (0, 0, yaw))
        p0, p1 = Vector(pt(th, RIM, 3 * BENCH)), Vector(pt(th, FOOT, 0.0))
        d = p1 - p0
        mid = (p0 + p1) / 2
        yaw = math.degrees(math.atan2(d.y, d.x))
        slope = math.degrees(math.atan2(-d.z, d.xy.length))
        b.collide_box(mid - Vector((0, 0, 0.3)), (d.length, arc * 1.8, 0.6), (0, slope, yaw))


def bench_miner(b, at, yaw, v):
    """The bench miner: a tracked rig with a cutter drum on a boom against the face, the engine house with its
    stacks, the operator's cab, the fuel tank with its filler, and the discharge conveyor's head behind. Front (the
    drum) toward -Y of its frame. Returns the discharge point in the monument's frame."""
    with b.frame(at, (0, 0, yaw)):
        for s in (-1, 1):
            P.track_frame(b, (s * 2.1, 0.0, 0.0), 6.5, 1.4, 1.1)
        P.grid_box(b, (3.4, 6.0, 0.9), (0, 0, 1.75), mat=PAINT, tint=DARK, step=1.2)
        P.grid_box(b, (4.6, 7.0, 0.3), (0, 0.3, 2.35), mat=PAINT, tint=P.MINE_YELLOW, step=1.2)
        # Engine house at the rear, two exhaust stacks, radiator louvres.
        P.grid_box(b, (4.0, 3.0, 2.4), (0, 2.1, 3.7), mat=PAINT, tint=P.MINE_YELLOW, step=1.0)
        for s in (-1, 1):
            for k in range(6):
                b.box((0.02, 2.2, 0.1), (s * 2.01, 2.1, 3.0 + 0.25 * k), (0, 0, 0), mat=PAINT, tint=DARK,
                      solid=False, vary=0)
        for x in (-0.9, 0.9):
            b.cyl(0.16, 1.6, (x, 2.8, 5.6), mat=PAINT, tint=DARK, sides=10)
            b.cyl(0.2, 0.25, (x, 2.8, 6.45), mat=RUST, tint=(1, 1, 1), sides=10, solid=False)
        stripes(b, (-2.0, 3.61, 0), (2.0, 3.61, 0), 0.4, width=0.3, at_z=2.55, normal=(0, 1, 0))
        # Cab on the left front, ladder and rails along the deck.
        P.grid_box(b, (1.6, 1.8, 2.0), (-1.3, -1.6, 3.5), mat=PAINT, tint=P.MINE_YELLOW, step=0.7)
        b.box((1.7, 1.9, 0.1), (-1.3, -1.6, 4.55), mat=PAINT, tint=DARK)
        b.box((1.4, 0.02, 1.0), (-1.3, -2.51, 3.8), mat=GLASS, solid=False)
        b.box((0.02, 1.4, 0.9), (-2.11, -1.6, 3.8), mat=GLASS, solid=False)
        for (a, c) in (((-2.3, -2.9, 2.5), (2.3, -2.9, 2.5)), ((2.3, -2.9, 2.5), (2.3, 3.8, 2.5))):
            railing(b, a, c, 1.0, spacing=1.2, tint=YELLOW, toe=False)
        ladder(b, (2.55, 0.5, 0.0), 2.5, (0, 0, -90), cage=False)
        # The fuel tank and its filler on the right side.
        b.cyl(0.7, 2.4, (1.6, -0.4, 3.2), (90, 0, 0), mat=PAINT, tint=srgb(0.62, 0.16, 0.1), sides=16)
        b.cyl(0.12, 0.3, (1.6, -0.4, 4.0), mat=PAINT, tint=YELLOW, sides=10, solid=False)
        P.stencil(b, "FUEL", (2.31, -0.4, 3.1), (0, 0, 90), 0.25, OFFWHITE, align="centre")
        b.marker("fuel-input", (3.2, -0.4, 0.0), takes="fuel")
        # The boom and the cutter drum against the face.
        with b.frame((0, -3.2, 2.2), (28.0, 0, 0)):
            for s in (-1, 1):
                b.box((0.5, 4.2, 0.8), (s * 1.0, -2.0, 0), mat=PAINT, tint=P.MINE_YELLOW, bevel=0.04)
            b.cyl(1.1, 2.6, (0, -4.2, 0), (0, 90, 0), mat=PAINT, tint=srgb(0.32, 0.32, 0.3), sides=20)
            for k in range(36):
                a = 2 * math.pi * k / 12
                x = -1.15 + 2.3 * (k % 12) / 11 + (0.08 if k // 12 == 1 else 0)
                ang = a + (k // 12) * 0.7
                b.box((0.12, 0.18, 0.32), (x, -4.2 + 1.18 * math.cos(ang), 1.18 * math.sin(ang)),
                      (math.degrees(ang), 0, 0), mat=RUST, tint=(1, 1, 1), solid=False, vary=0)
            b.cyl(1.25, 0.12, (1.36, -4.2, 0), (0, 90, 0), mat=PAINT, tint=P.MINE_YELLOW, sides=20, solid=False)
            b.cyl(1.25, 0.12, (-1.36, -4.2, 0), (0, 90, 0), mat=PAINT, tint=P.MINE_YELLOW, sides=20, solid=False)
            b.cyl(0.3, 2.0, (0, -1.6, 0.5), (90, 0, 0), mat=PAINT, tint=srgb(0.8, 0.8, 0.8), sides=10, solid=False)
        # Discharge conveyor's head behind, the cut falling on it.
        head = b.world((0.0, 5.6, 3.4))
        P.mound(b, (0, -4.6, 0.0), 2.0, 1.0, tint=v["ore"], rough=0.35, mat=CONCRETE)
        b.collide_box(Vector((0, 0.2, 2.2)), (4.8, 7.4, 4.4), (0, 0, 0))
    return head


def hopper(b, at, v):
    """The hopper: a steel tower on four legs with bracing, an inverted pyramid with a grizzly of bars, a feeder at
    its foot, a walkway round its rim."""
    x, y = at
    h = 7.0
    for sx in (-1.6, 1.6):
        for sy in (-1.6, 1.6):
            P.grid_box(b, (0.8, 0.8, 0.4), (x + sx, y + sy, 0.2), mat=CONCRETE, tint=CONCRETE_T, step=0.5)
            b.box((0.3, 0.3, h), (x + sx, y + sy, 0.4 + h / 2), mat=PAINT, tint=TEAL)
    for (ax, ay, bx, by) in ((-1.6, -1.6, 1.6, -1.6), (1.6, -1.6, 1.6, 1.6), (1.6, 1.6, -1.6, 1.6),
                             (-1.6, 1.6, -1.6, -1.6)):
        for z in (2.5, 5.0):
            P.rod(b, (x + ax, y + ay, z), (x + bx, y + by, z), 0.08, tint=TEAL, sides=6, trim=0.15)
        P.rod(b, (x + ax, y + ay, 0.6), (x + bx, y + by, 2.4), 0.04, tint=GREY, sides=5, trim=0.15)
    b.cyl(0.5, 2.6, (x, y, h - 0.6), (0, 0, 45), mat=PAINT, tint=TEAL, sides=4, radius2=3.0, cap=False,
          solid="hull")
    b.cyl(3.0, 0.8, (x, y, h + 1.1), (0, 0, 45), mat=PAINT, tint=TEAL, sides=4, cap=False, solid=False)
    for k in range(7):
        b.box((0.08, 4.2, 0.12), (x - 1.8 + 0.6 * k, y, h + 1.5), mat=PAINT, tint=DARK, solid=False)
    P.mound(b, (x, y, h + 0.8), 1.8, 0.6, tint=v["ore"], rough=0.3, mat=CONCRETE)
    b.box((1.2, 1.6, 0.8), (x, y, h - 2.3), mat=PAINT, tint=DARK)
    b.box((5.2, 0.9, 0.05), (x, y - 2.6, h + 0.6), mat=GRATE, tint=GREY, solid=True, vary=0)
    railing(b, (x - 2.6, y - 3.05, h + 0.6), (x + 2.6, y - 3.05, h + 0.6), 1.1, spacing=1.3, tint=YELLOW)
    ladder(b, (x + 2.0, y - 2.0, 0.0), h + 0.6, (0, 0, -90), cage=True)
    return Vector((x, y, h - 2.7))


def output_bin(b, at, v):
    """The output bin over the truck bay: a steel bin on four legs, a sloped bottom and a chute with a gate, the
    loading bay's kerbs below, a ladder to its roof hatch."""
    x, y = at
    legs, bh = 4.6, 3.6
    for sx in (-2.0, 2.0):
        for sy in (-2.0, 2.0):
            P.grid_box(b, (0.9, 0.9, 0.5), (x + sx, y + sy, 0.25), mat=CONCRETE, tint=CONCRETE_T, step=0.5)
            b.box((0.35, 0.35, legs + bh), (x + sx, y + sy, 0.5 + (legs + bh) / 2), mat=PAINT, tint=P.MINE_YELLOW)
    for (ax, ay, bx, by) in ((-2.0, -2.0, 2.0, -2.0), (-2.0, 2.0, 2.0, 2.0)):
        P.rod(b, (x + ax, y + ay, 0.8), (x + bx, y + by, legs), 0.05, tint=GREY, sides=5, trim=0.2)
        P.rod(b, (x + bx, y + by, 0.8), (x + ax, y + ay, legs), 0.05, tint=GREY, sides=5, trim=0.2)
    P.grid_box(b, (4.4, 4.4, bh), (x, y, 0.5 + legs + bh / 2), mat=CORRUGATED, tint=srgb(0.6, 0.62, 0.6), step=1.2)
    b.box((4.6, 4.6, 0.2), (x, y, 0.5 + legs + bh + 0.1), mat=PAINT, tint=DARK)
    b.cyl(0.4, 1.6, (x, y, 0.5 + legs - 0.6), (0, 0, 45), mat=PAINT, tint=srgb(0.6, 0.62, 0.6), sides=4,
          radius2=3.1, cap=False, solid=False)
    b.box((1.0, 1.0, 1.0), (x, y, legs - 1.2), mat=PAINT, tint=P.MINE_YELLOW)
    b.box((0.9, 1.6, 0.1), (x, y - 0.9, legs - 1.8), (30, 0, 0), mat=PAINT, tint=GREY)
    P.stencil(b, v["label"], (x, y - 2.22, 0.5 + legs + 1.6), (0, 0, 0), 0.55, DARK, align="centre")
    b.marker("output", (x, y - 1.6, 0.0), gives=v["label"].lower())
    ladder(b, (x - 2.45, y + 1.2, 0.0), 0.5 + legs + bh + 0.2, (0, 0, 90), cage=True)
    for s in (-1, 1):
        b.box((0.4, 7.0, 0.35), (x + s * 2.9, y - 1.0, 0.17), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
        stripes(b, (x + s * 2.9, y - 4.5, 0), (x + s * 2.9, y + 2.5, 0), 0.35, width=0.5, at_z=0.0,
                normal=(s, 0, 0))
    return Vector((x, y, 0.5 + legs + bh + 0.2))


def light_tower(b, at, yaw):
    """A mobile light tower: a trailer with outriggers, a generator box, a telescopic mast with four floods."""
    with b.frame(at, (0, 0, yaw)):
        b.box((1.4, 3.0, 1.1), (0, 0, 0.95), mat=PAINT, tint=P.MINE_YELLOW, bevel=0.05)
        for s in (-1, 1):
            P.wheel(b, (s * 0.85, 0.3, 0.38), (0, 0, 0), 0.38, 0.22, sides=12)
            b.beam((s * 0.7, -1.2, 0.6), (s * 2.0, -1.2, 0.1), 0.1, 0.1, mat=PAINT, tint=DARK)
            b.beam((s * 0.7, 1.2, 0.6), (s * 2.0, 1.2, 0.1), 0.1, 0.1, mat=PAINT, tint=DARK)
            b.box((0.3, 0.3, 0.05), (s * 2.0, 1.2 * s, 0.03), mat=PAINT, tint=DARK, solid=False)
        b.beam((0, -1.5, 0.7), (0, -2.6, 0.45), 0.1, 0.1, mat=PAINT, tint=DARK)
        b.cyl(0.12, 5.5, (0, 0.9, 4.2), mat=PAINT, tint=GREY, sides=10)
        b.cyl(0.08, 3.0, (0, 0.9, 8.2), mat=PAINT, tint=LIGHTGREY, sides=8)
        b.box((1.8, 0.12, 0.12), (0, 0.9, 9.6), mat=PAINT, tint=DARK, solid=False)
        for dx in (-0.65, -0.22, 0.22, 0.65):
            lamp(b, (dx, 0.75, 9.85), (-25, 0, 0), size=(0.36, 0.2, 0.3))


def build_mine(b, variant):
    v = VARIANTS[variant]
    poly = [pt(th, -0.5, 0)[:2] for th in range(-62, 243, 8)] + [pt(242.0, 30.0, 0)[:2], pt(-62.0, 30.0, 0)[:2]]
    b.set_pad(("poly", poly), ground="gravel" if variant != "metal" else "dirt", blend=12.0, clear=6.0)
    pit(b, v)

    # The haul road in through the cut, ruts across the floor, puddles.
    for off in (-1.3, 1.3):
        P.ribbon(b, [(2.0 + off, -50.0), (1.0 + off, -30.0), (0.0 + off, -12.0), (-3.0 + off, 2.0),
                     (-6.0 + off, 12.0)], 0.8, 0.0, P.EARTH_DARK, mat=CONCRETE, step=2.0)
    for (x, y, r) in ((-8.0, -2.0, 1.8), (10.0, 18.0, 1.2), (4.0, -8.0, 1.0)):
        P.blob(b, (x, y, 0.0), r, srgb(0.16, 0.18, 0.18), points=12, rough=0.35, thick=0.008, mat="M_Mon_Gloss")

    # The miner at the north face, its conveyor to the hopper, the long conveyor up to the output bin.
    head = bench_miner(b, (-6.0, 21.2, 0.0), 180.0, v)
    feed = hopper(b, (8.0, 8.0), v)
    conveyor(b, tuple(head), (feed.x - 0.2, feed.y + 2.0, feed.z + 4.5), width=1.0)
    top = output_bin(b, (11.0, -4.0), v)
    conveyor(b, (feed.x + 0.3, feed.y - 1.0, feed.z - 0.3), (top.x - 0.5, top.y + 1.5, top.z + 0.4), width=1.0)

    # Steel stairs up the three faces on the east side; the light tower; the site office; fuel; the haul truck.
    for i in range(3):
        th = 8.0
        o0, z0 = i * STEP, i * BENCH
        p = Vector(pt(th, o0 - 5.0, z0))
        d = Vector(pt(th, o0 + 1.0, z0)) - p
        yaw = math.degrees(math.atan2(d.y, d.x)) - 90.0
        stairs(b, tuple(p), (0, 0, yaw), 1.0, BENCH, 5.2, tint=YELLOW)
    light_tower(b, (-4.0, 1.0, 0.0), 30.0)
    P.prefab_hut(b, (-17.0, -6.0, 0.0), (0, 0, 20.0), w=4.6, d=2.6, h=2.6, code=v["code"], door_open=60.0,
                 windows=1, band=ORANGE)
    b.marker("loot", (-17.4, -5.6, 0.32), table="crate")
    with b.frame((-20.0, 4.0, 0.0), (0, 0, 80.0)):
        for x in (-1.6, 1.6):
            b.box((0.4, 2.2, 0.6), (x, 0, 0.3), mat=CONCRETE, tint=CONCRETE_T)
        b.cyl(1.0, 4.6, (0, 0, 1.6), (0, 90, 0), mat=PAINT, tint=srgb(0.62, 0.16, 0.1), sides=20)
        for s in (-1, 1):
            b.lathe([(1.0, 0.0), (0.85, 0.22), (0.5, 0.34), (0.0, 0.38)], (s * 2.3, 0, 1.6), (0, 90 * s, 0),
                    mat=PAINT, tint=srgb(0.62, 0.16, 0.1), sides=20)
        b.box((0.4, 0.4, 0.3), (0.8, 0, 2.7), mat=PAINT, tint=GREY, solid=False)
        P.plate(b, (0, -1.02, 1.6), (0, 0, 0), (0.8, 0.5), YELLOW, None, "FUEL", text_h=0.18)
    b.marker("loot", (-19.0, 1.8, 0.05), table="fuel-barrel")
    b.piece("drum-stack", (-22.4, 0.6, 0.0), (0, 0, 20.0))
    P.haul_truck(b, (11.0, -6.2, 0.0), 0.0, tip=0.0, code="HT 0" + v["code"][-1])

    # Stockpiles of the ore by the bin, a pile of rejects by the miner.
    P.mound(b, (24.0, -2.0, 0.0), 5.0, 3.0, tint=v["ore"], rough=0.25, mat=CONCRETE)
    P.mound(b, (26.0, 6.0, 0.0), 3.8, 2.2, tint=v["ore2"], rough=0.3, mat=CONCRETE)
    P.mound(b, (-16.0, 16.0, 0.0), 4.0, 2.0, tint=v["walls"][1], rough=0.35, mat=CONCRETE)
    P.clods(b, (24.0, -2.0, 0.0), 7.0, count=12, tint=v["ore"], size=0.6, inner=4.5)

    # Scrub and young trees taking the old spoil and the rim.
    for k in range(26):
        th = b.rng.uniform(-90.0 + GAP + 2, 270.0 - GAP - 2)
        off = b.rng.uniform(RIM - 2.0, FOOT - 1.0)
        if off < RIM:
            z = 3 * BENCH + 0.1
        else:
            z = max(0.0, (3 * BENCH) * (1.0 - (off - RIM) / (FOOT - RIM)) ** 0.9 - 0.3)
        p = pt(th, off, z)
        P.bush(b, p, b.rng.uniform(0.8, 1.8), P.LEAF if b.rng.random() < 0.7 else P.LEAF_DRY, lumps=3)

    # The variant's own clutter.
    if variant == "stone":
        for k in range(9):
            x, y = -24.0 + 1.6 * (k % 3), 10.0 + 1.3 * (k // 3)
            b.box((1.4, 1.1, 0.8), (x, y, 0.4 + 0.82 * (k // 6)), (0, 0, b.rng.uniform(-4, 4)), mat=CONCRETE,
                  tint=v["walls"][k % 3], vary=0.05, wear=0.7)
        for k in range(3):
            P.rod(b, (2.0 + 0.6 * k, 26.5, 0.0), (2.4 + 0.6 * k, 26.6, 3.6), 0.05, tint=srgb(0.5, 0.5, 0.52), sides=6)
    elif variant == "metal":
        for k in range(14):
            p = (24.0 + b.rng.uniform(-4, 4), -2.0 + b.rng.uniform(-4, 4), b.rng.uniform(0.3, 2.4))
            b.box((0.25, 0.2, 0.15), p, (b.rng.uniform(0, 90), 0, b.rng.uniform(0, 90)), mat=PAINT,
                  tint=srgb(0.55, 0.55, 0.58), solid=False, vary=0.1)
        for k in range(4):
            P.blob(b, (b.rng.uniform(-20, 20), b.rng.uniform(-5, 20), 0.0), b.rng.uniform(1.0, 2.0),
                   srgb(0.45, 0.2, 0.12), points=12, rough=0.4)
    else:
        for k in range(5):
            x, y = b.rng.uniform(-22, 22), b.rng.uniform(-4, 22)
            P.mound(b, (x, y, 0.0), 0.8, 0.5, tint=srgb(0.9, 0.82, 0.2), rough=0.4, mat=CONCRETE)
            P.blob(b, (x, y, 0.0), 1.8, srgb(0.82, 0.74, 0.3), points=12, rough=0.4)
        P.plate(b, (-16.0, -8.6, 1.5), (0, 0, 20.0), (0.9, 0.6), YELLOW, None, "GAS", text_h=0.25)

    b.zone("no-build", (CX, CY, 0), size=(140.0, 120.0, 40.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "hills-ore", "variant": variant})


SUN = (30.0, 210.0)
PEOPLE = [((4.0, -14.0, 0.0), 20.0)]
VIEWS = [
    {"name": "pit", "target": (0.0, 6.0, 3.0), "distance": 120, "yaw": 22, "pitch": 30, "lens": 30},
    {"name": "miner", "target": (0.0, 14.0, 3.5), "distance": 28, "yaw": -24, "pitch": 7, "lens": 26},
    {"name": "bin", "target": (10.0, -5.0, 4.0), "distance": 20, "yaw": 150, "pitch": 8, "lens": 26},
]
