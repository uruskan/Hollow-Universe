"""Bucket-Wheel Pit (MONUMENTS.md §5.4, large tier, catalogue 46): an open pit 800 m across on an ore-rich plain,
worked by a Seedward bucket-wheel excavator that still stands at the face. The pit itself becomes terrain later; the
monument builds its stepped benches upward from the pit floor (six benches to a rim 66 m up, as slab cells, their own
collision), a haul ramp across them, the machine, and the works on the rim.

Outside the rim the overburden lies in terraced spoil banks down to the plain, so the pit reads as a sunken bowl in a
low ring of spoil from afar, the machine's mast and wheel boom rising out of it.

The excavator, 120 m to the top of its mast: three crawler units under a turntable, a deck with the machinery house
and its control room, a lattice mast with the stays, an 80 m wheel boom with its walkway to a bucket wheel 24 m
across biting into the first bench, a counterweight boom, and a discharge bridge to the belt that climbs to the
crusher on the rim.

Gameplay: fill the machine's tank and it digs (a slow, loud producer of ore at the crusher's bin, §5.4); vault
crates in the control room up on the machine; Reclaimers on the rim and the machine; haul trucks, ore piles and a
workshop with crates.
"""
import math

from mathutils import Vector

from monkit import kit
from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, YELLOW, block_building,
                        cable, ladder, lamp, lattice_tower, railing, rubble, srgb, stairs, stripes, tank)
from monkit.structures import conveyor, road, shed, silo
from monkit.surfaces import (CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST)

KEY = "bucket-wheel-pit"
TITLE = "Bucket-Wheel Pit"
SEED = 4601
BUILDER = {"chunk": 128.0, "max_edge": 14.0, "lod_min": (0.0, 1.2, 7.0)}
LODS = [380.0, 1800.0, 28000.0]

A_FLOOR, B_FLOOR = 220.0, 130.0
A_RIM, B_RIM = 400.0, 300.0
BENCHES = 4
BENCH_H = 9.0
RIM_Z = BENCHES * BENCH_H
PLATEAU = 70.0
MACHINE = (118.0, 18.0)
MYAW = 0.0
STEEL_Y = srgb(0.86, 0.66, 0.14)
STEEL_O = srgb(0.84, 0.42, 0.12)
ORE = srgb(0.42, 0.30, 0.24)
SPOIL = srgb(0.50, 0.44, 0.36)


def ellipse(a, b, ang, wobble=0.0, seed=0):
    k = 1.0 + wobble * (P.fbm(math.cos(ang) * 2.0 + 5.0, math.sin(ang) * 2.0, seed * 0.37, 3, 3) - 0.5)
    return a * math.cos(ang) * k, b * math.sin(ang) * k


def benches(b):
    """Six benches from the pit floor to the rim and a plateau beyond, as slab cells; tinted by layer."""
    n = 96
    rows = []
    for k in range(BENCHES + 1):
        a = A_FLOOR + (A_RIM - A_FLOOR) * k / BENCHES
        bb = B_FLOOR + (B_RIM - B_FLOOR) * k / BENCHES
        z = k * BENCH_H
        rows.append((a, bb, z))
        if k < BENCHES:
            rows.append((a + 5.0, bb + 4.5, z + BENCH_H))
    rows.append((A_RIM + PLATEAU, B_RIM + PLATEAU, RIM_Z))
    # The spoil banks outside: two terraces down to the plain.
    rows += [(A_RIM + PLATEAU + 14.0, B_RIM + PLATEAU + 14.0, RIM_Z * 0.55),
             (A_RIM + PLATEAU + 30.0, B_RIM + PLATEAU + 30.0, RIM_Z * 0.55),
             (A_RIM + PLATEAU + 48.0, B_RIM + PLATEAU + 48.0, 0.0)]
    grid = []
    for (a, bb, z) in rows:
        row = []
        for j in range(n + 1):
            ang = 2 * math.pi * j / n
            x, y = ellipse(a, bb, ang, 0.08, 7)
            row.append((x, y, z + (1.2 * (P.vnoise(x / 40.0, y / 40.0, z, 9) - 0.5) if 0 < z < RIM_Z - 0.1 else 0.0)))
        grid.append(row)

    def tint(i, j):
        layer = i // 2
        shades = [srgb(0.46, 0.40, 0.33), srgb(0.52, 0.47, 0.39), srgb(0.43, 0.35, 0.29), srgb(0.55, 0.50, 0.42),
                  srgb(0.47, 0.42, 0.35), srgb(0.40, 0.36, 0.31), srgb(0.45, 0.42, 0.36)]
        base = shades[min(layer, len(shades) - 1)]
        if i >= 2 * BENCHES:
            base = SPOIL if i == 2 * BENCHES else srgb(0.47, 0.44, 0.37)
        elif i % 2 == 0:
            base = tuple(c * 0.86 for c in base)
        n_ = P.fbm(j * 0.25, i * 0.5, 1.3, 13, 2)
        return tuple(c * (0.9 + 0.2 * n_) for c in base)

    P.skin(b, grid, (0, 0, -1), thick=3.0, mat=CONCRETE, tint_fn=tint, wear=0.7)
    # Safety berms along each bench's edge (low windrows), broken here and there.
    for k in range(1, BENCHES + 1):
        a = A_FLOOR + (A_RIM - A_FLOOR) * k / BENCHES
        bb = B_FLOOR + (B_RIM - B_FLOOR) * k / BENCHES
        for j in range(0, n, 2):
            if b.rng.random() < 0.25:
                continue
            ang = 2 * math.pi * (j + 0.5) / n
            x, y = ellipse(a + 7.0, bb + 6.5, ang, 0.08, 7)
            P.rock(b, (x, y, k * BENCH_H), (14.0, 3.0, 1.6), seed=k * 100 + j, rough=0.25, rings=4, sides=8,
                   mat=MATTE, tint=SPOIL, solid=False)


def on_rim(ang, frac):
    """A point on the rim plateau at angle `ang` (degrees), `frac` of the way from the pit's edge outward."""
    return ellipse(A_RIM + 6.0 + (PLATEAU - 12.0) * frac, B_RIM + 6.0 + (PLATEAU - 12.0) * frac, math.radians(ang),
                   0.08, 7)


def ramp_and_belt(b):
    """The haul ramp across the south benches to the rim, the belt beside it to the crusher."""
    a0 = Vector(ellipse(A_FLOOR - 4.0, B_FLOOR - 4.0, math.radians(262.0)) + (0.0,))
    a1 = Vector(ellipse(A_RIM + 8.0, B_RIM + 8.0, math.radians(222.0)) + (0.0,))
    P.ramp(b, (a0.x, a0.y), (a1.x, a1.y), 18.0, 0.0, RIM_Z + 0.4, mat=CONCRETE, tint=srgb(0.50, 0.46, 0.40),
           wear=0.8)
    d = a1 - a0
    side = Vector((-d.y, d.x, 0)).normalized()
    for k in range(18):
        t = (k + 0.5) / 18
        q = a0 + d * t + side * 9.4
        b.box((6.0, 0.8, 1.0), (q.x, q.y, (RIM_Z + 0.4) * t + 0.5), (0, 0, math.degrees(math.atan2(d.y, d.x))),
              mat=CONCRETE, tint=CONCRETE_T, solid=False)
    # Outside: the approach ramp down the spoil banks to the plain.
    o0 = Vector(on_rim(214.0, 0.85) + (0.0,))
    o1 = Vector(ellipse(A_RIM + PLATEAU + 120.0, B_RIM + PLATEAU + 120.0, math.radians(240.0)) + (0.0,))
    P.ramp(b, (o0.x, o0.y), (o1.x, o1.y), 14.0, RIM_Z + 0.4, 0.0, mat=CONCRETE, tint=srgb(0.50, 0.46, 0.40),
           wear=0.8)
    cz = Vector(on_rim(228.0, 0.5) + (0.0,))
    conveyor(b, (-58.0, -46.0, 6.0), (-120.0, -170.0, 24.0), width=1.6, tint=STEEL_Y)
    conveyor(b, (-120.0, -170.0, 24.0), (cz.x + 14.0, cz.y + 8.0, RIM_Z + 14.0), width=1.6, tint=STEEL_Y)
    b.box((8.0, 6.0, 7.0), (-120.0, -170.0, 24.0 - 3.5 + 0.5), mat=PAINT, tint=STEEL_Y, bevel=0.1)


def crawler_unit(b, at, yaw):
    """A crawler unit: two track frames with their links, a bogie frame, a pivot column."""
    with b.frame(at, (0, 0, yaw)):
        for y in (-3.6, 3.6):
            b.box((18.0, 4.6, 4.4), (0, y, 2.4), mat=PAINT, tint=srgb(0.30, 0.30, 0.30), bevel=0.3)
            b.box((18.6, 4.8, 0.8), (0, y, 0.4), mat=RUST, tint=(1, 1, 1))
            for k in range(9):
                b.box((0.6, 4.9, 0.5), (-8.0 + 2.0 * k, y, 4.75), mat=RUST, tint=(1, 1, 1), solid=False)
            for x in (-8.6, 8.6):
                b.cyl(2.2, 4.6, (x, y, 2.4), (90, 0, 0), mat=PAINT, tint=DARK, sides=16)
        b.box((12.0, 11.6, 2.2), (0, 0, 5.6), mat=PAINT, tint=STEEL_Y, bevel=0.1)
        b.cyl(2.2, 3.0, (0, 0, 8.0), mat=PAINT, tint=STEEL_Y, sides=16)


def bucket_wheel(b, at, r=12.0, buckets=18):
    """The wheel: twin rims, spokes, a hub, buckets with teeth, in the frame's x-z plane (axle along y)."""
    with b.frame(at, (0, 0, 0)):
        for y in (-1.6, 1.6):
            pts = [(math.cos(2 * math.pi * k / 36) * r, y, math.sin(2 * math.pi * k / 36) * r) for k in range(37)]
            b.tube(pts, 0.6, mat=PAINT, tint=STEEL_O, sides=8, cap=False)
            for k in range(9):
                a = 2 * math.pi * k / 9
                b.beam((0, y, 0), (math.cos(a) * r, y, math.sin(a) * r), 0.5, 0.5, mat=PAINT, tint=STEEL_O)
        b.cyl(2.6, 5.0, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=DARK, sides=20)
        b.cyl(1.4, 9.0, (0, -1.0, 0), (90, 0, 0), mat=PAINT, tint=STEEL_Y, sides=16)
        for k in range(buckets):
            a = 2 * math.pi * k / buckets
            with b.frame((math.cos(a) * (r + 0.6), 0, math.sin(a) * (r + 0.6)), (0, -math.degrees(a), 0)):
                b.box((2.4, 4.4, 2.6), (0.6, 0, 0.0), mat=RUST, tint=srgb(0.85, 0.8, 0.75), bevel=0.15)
                for dy in (-1.6, -0.5, 0.5, 1.6):
                    b.box((0.9, 0.35, 0.35), (1.9, dy, 1.1), mat=PAINT, tint=DARK, solid=False)
        b.marker("landmark", (0, 0, r), kind_of="bucket-wheel")


def excavator(b):
    """The machine at the face."""
    mx, my = MACHINE
    with b.frame((mx, my, 0), (0, 0, MYAW)):
        for (x, y, yaw) in ((16.0, 0.0, 0.0), (-14.0, 17.0, 0.0), (-14.0, -17.0, 0.0)):
            crawler_unit(b, (x, y, 0), yaw)
        # Base frame and turntable.
        for (p0, p1) in (((16.0, 0.0), (-14.0, 17.0)), ((-14.0, 17.0), (-14.0, -17.0)), ((-14.0, -17.0), (16.0, 0.0))):
            b.beam((p0[0], p0[1], 9.5), (p1[0], p1[1], 9.5), 2.4, 2.8, mat=PAINT, tint=STEEL_Y, solid=True)
        b.lathe([(15.0, 0.0), (17.0, 0.0), (17.0, 2.2), (15.0, 2.2)], (0, 0, 9.0), mat=PAINT, tint=DARK, sides=48,
                close=True, solid="hull")
        b.cyl(14.5, 2.0, (0, 0, 10.0), mat=PAINT, tint=srgb(0.36, 0.36, 0.34), sides=40)
        dz = 11.0
        b.box((46.0, 26.0, 2.6), (-4.0, 0, dz + 1.3), mat=PAINT, tint=STEEL_Y, bevel=0.2)
        P.deck(b, -27.0, -13.0, 19.0, 13.0, dz + 2.62, thick=0.02, rails="snwe", gaps={"s": [(-25.0, -21.0)]},
               edge=False, floor=False)
        stripes(b, (-27.0, -13.1, 0), (19.0, -13.1, 0), 0.8, width=1.0, at_z=dz + 0.3)
        # Machinery house with the control room on top.
        hz = dz + 2.6
        P.room(b, (-14.0, 0.0, hz), (0, 0, 0), 22.0, 18.0, 10.0, thick=0.3, mat=CORRUGATED, tint=LIGHTGREY,
               openings={"front": [(-6.0, 1.4, 2.3, 0.0), (4.0, 6.0, 1.6, 6.0)], "right": [(4.0, 1.4, 2.3, 0.0)],
                         "back": [(0.0, 8.0, 1.6, 6.0)]}, roof_mat=PAINT, roof_tint=GREY)
        b.marker("door", (-20.0, -9.6, hz), kind_of="building")
        for k in range(4):
            b.box((3.2, 2.4, 3.0), (-20.0 + k * 4.2, 3.0, hz + 1.5), mat=PAINT, tint=srgb(0.30, 0.42, 0.40), bevel=0.1)
        P.room(b, (-10.0, -3.0, hz + 10.3), (0, 0, 0), 10.0, 8.0, 3.4, thick=0.25, mat=PAINT, tint=OFFWHITE,
               openings={"front": [(0.0, 7.0, 1.4, 1.0)], "right": [(0.0, 5.0, 1.4, 1.0)], "left": [(1.5, 1.2, 2.3, 0.0)]})
        b.box((7.0, 0.04, 1.4), (-10.0, -7.05, hz + 12.0), mat=GLASS, solid=False)
        b.marker("loot", (-12.5, -1.0, hz + 10.4), table="vault-crate")
        b.marker("loot", (-7.5, -1.0, hz + 10.4), table="vault-crate")
        b.marker("guard", (-10.0, -4.5, hz + 10.4), role="reclaimer")
        b.marker("door", (-15.6, -1.5, hz + 10.3), kind_of="cab")
        ladder(b, (-16.2, -1.5, hz + 10.3), 0.1, (0, 0, -90), cage=False)
        ladder(b, (-25.6, 8.0, hz), 10.3, (0, 0, 90), cage=True)
        # Fuel tank and inlet, generator stacks.
        b.cyl(2.4, 9.0, (-24.0, -11.0, hz + 2.4), (0, 90, 0), mat=PAINT, tint=RED, sides=20)
        b.marker("fuel-inlet", (-19.0, -13.6, hz + 0.1), takes="fuel", puzzle="excavator")
        P.plate(b, (-19.0, -13.45, hz + 1.6), (0, 0, 0), 1.2, 0.6, text="F1")
        for x in (-20.0, -16.0):
            b.cyl(0.5, 5.0, (x, 7.0, hz + 12.5), mat=PAINT, tint=DARK, sides=10)
        # Stair tower from the ground to the deck on the south side.
        P.stair_tower(b, (-23.0, -18.0, 0.0), (0, 0, 180), [0.0, 6.8, dz + 2.62], width=1.1)
        P.deck(b, -25.0, -16.6, -21.0, -13.0, dz + 2.62, rails="we")
        # The mast: a tapered lattice to 120 m, a head frame, beacons, a caged ladder.
        lattice_tower(b, (-6.0, 0.0, hz), 120.0 - hz, 11.0, 3.6, sections=18, member=0.45, tint=STEEL_Y)
        b.box((8.0, 8.0, 2.0), (-6.0, 0, 120.0), mat=PAINT, tint=STEEL_Y)
        for x, y in ((-9.5, -3.5), (-2.5, 3.5)):
            P.beacon(b, (x, y, 121.0), 0.6)
        b.marker("landmark", (-6.0, 0, 122.0), kind_of="excavator-mast", visible_km=18)
        for k in range(5):
            z0 = hz + k * 21.0
            ladder(b, (-6.0 + 3.6, -0.8, z0), 21.0, (0, 0, 90), cage=True, tint=STEEL_Y)
        # The wheel boom: a box truss rising to the wheel, a belt inside, a walkway on top.
        p0 = Vector((10.0, 0.0, dz + 6.0))
        p1 = Vector((84.0, 0.0, 44.0))
        P.truss(b, tuple(p0), tuple(p1), w=7.0, h=6.0, chord=0.7, web=0.35, tint=STEEL_Y)
        dvec = (p1 - p0).normalized()
        b.beam(p0 + Vector((0, 0, 1.0)), p1 + Vector((0, 0, 1.0)), 2.0, 0.6, mat=PAINT, tint=DARK)
        top0, top1 = p0 + Vector((0, 0, 6.2)), p1 + Vector((0, 0, 6.2))
        b.beam(top0, top1, 2.2, 0.15, mat=GRATE, tint=GREY, solid=True)
        for s in (-1.05, 1.05):
            railing(b, top0 + Vector((0, s, 0.1)), top1 + Vector((0, s, 0.1)), spacing=2.4, toe=False)
        bucket_wheel(b, (p1.x + 6.0, -5.2, p1.z), 12.0)
        b.box((5.0, 3.0, 4.0), (p1.x + 1.0, 4.6, p1.z + 4.0), mat=PAINT, tint=OFFWHITE, bevel=0.1)
        b.box((4.6, 0.04, 1.6), (p1.x + 1.0, 3.08, p1.z + 4.6), mat=GLASS, solid=False)
        b.marker("loot", (p1.x + 1.0, 4.6, p1.z + 2.1), table="crate")
        # Counterweight boom and its weight.
        q0 = Vector((-26.0, 0.0, dz + 6.0))
        q1 = Vector((-72.0, 0.0, 34.0))
        P.truss(b, tuple(q0), tuple(q1), w=6.0, h=5.0, chord=0.6, web=0.3, tint=STEEL_Y)
        b.box((14.0, 11.0, 12.0), (q1.x - 4.0, 0, q1.z - 2.0), mat=CONCRETE, tint=CONCRETE_T, bevel=0.2)
        stripes(b, (q1.x - 11.0, -5.55, 0), (q1.x + 3.0, -5.55, 0), 1.6, width=1.6, at_z=q1.z - 8.0)
        # Stays from the mast head to both booms.
        head = Vector((-6.0, 0.0, 119.0))
        for s in (-2.5, 2.5):
            for t in (0.55, 1.0):
                cable(b, tuple(head + Vector((0, s, 0))), tuple(p0.lerp(p1, t) + Vector((0, s * 1.2, 6.0))), sag=0.004,
                      radius=0.18, tint=DARK)
            cable(b, tuple(head + Vector((0, s, 0))), tuple(q1 + Vector((0, s, 5.0))), sag=0.004, radius=0.18,
                  tint=DARK)
        # Discharge bridge to the hopper over the ground belt.
        d0 = Vector((-20.0, -12.0, dz + 4.0))
        d1 = Vector((-60.0, -62.0, 12.0))
        P.truss(b, tuple(d0), tuple(d1), w=4.0, h=3.6, chord=0.45, web=0.25, tint=STEEL_Y)
        crawler_unit(b, (d1.x, d1.y, 0.0), 51.0)
        b.cyl(2.4, 4.0, (d1.x, d1.y, 9.0), mat=PAINT, tint=STEEL_Y, sides=12)
        for k in range(4):
            lamp(b, (-26.0 + k * 13.0, 13.4, dz + 6.0), (25, 0, 180), size=(0.8, 0.4, 0.5))
        b.marker("guard", (6.0, 6.0, dz + 2.7), role="reclaimer")
    # The hopper and the ore output where the bridge drops onto the belt.
    hx, hy = mx - 60.0, my - 62.0
    for dx in (-2.4, 2.4):
        for dy in (-2.4, 2.4):
            b.box((0.4, 0.4, 7.0), (hx + 2.0 + dx, hy + 16.0 + dy, 3.5), mat=PAINT, tint=STEEL_Y, vary=0)
    b.cyl(3.6, 4.0, (hx + 2.0, hy + 16.0, 8.0), mat=PAINT, tint=P.DARK_STEEL, sides=4, radius2=4.6, solid="hull")
    b.cyl(1.0, 2.0, (hx + 2.0, hy + 16.0, 5.0), mat=RUST, tint=(1, 1, 1), sides=8, radius2=1.6, solid=False)
    stripes(b, (hx - 1.2, hy + 12.75, 0), (hx + 5.2, hy + 12.75, 0), 0.6, width=0.5, at_z=9.6)


def rim_works(b):
    """On the rim: the crusher and its bin (the ore output), the control house, a workshop, fuel tanks, a lamp mast,
    fences, a haul truck at the ramp top."""
    rz = RIM_Z
    cx, cy = on_rim(228.0, 0.5)
    P.hall(b, (cx, cy, rz), (0, 0, 228.0 - 180.0 + 90.0), 28.0, 20.0, 22.0, bay=7.0, plinth=3.0, clad_tint=srgb(0.70, 0.68, 0.60),
           frame_tint=srgb(0.40, 0.38, 0.32), band=STEEL_Y, ruin=0.2, windows=(), skylights=False,
           doors={"front": [(0.0, 6.0, 6.0, "shutter")], "left": [(4.0, 1.4, 2.3, "door")]})
    sx, sy = on_rim(219.0, 0.45)
    silo(b, (sx, sy, rz), radius=5.0, height=18.0, cone=5.0, legs=7.0, tint=srgb(0.66, 0.64, 0.58), count=2,
         spacing=12.0)
    b.marker("producer", (sx, sy, rz + 0.5), yields="ore", needs="excavator", puzzle="excavator")
    for ang, frac, yaw in ((236.0, 0.75, 20.0), (205.0, 0.3, 60.0)):
        x, y = on_rim(ang, frac)
        b.piece("haul-truck", (x, y, rz + 0.2), (0, 0, yaw))
    x, y = on_rim(240.0, 0.4)
    block_building(b, (x, y, rz), (0, 0, 240.0 - 90.0), 16.0, 9.0, 3.8, broken=0.5, tint=srgb(0.8, 0.79, 0.74))
    b.marker("guard", (x + 4.0, y - 6.0, rz + 0.2), role="reclaimer")
    x, y = on_rim(252.0, 0.5)
    shed(b, (x, y, rz), (0, 0, 252.0 - 90.0), 30.0, 18.0, 8.0, tint=srgb(0.66, 0.64, 0.58),
         frame_tint=srgb(0.40, 0.38, 0.32), doors=[("front", -6.0, 7.0, 6.0)], ruin=0.2)
    b.marker("loot", (x, y, rz + 0.3), table="crate")
    b.marker("loot", (x + 4.0, y + 3.0, rz + 0.3), table="crate")
    b.marker("loot", (x - 4.0, y - 2.0, rz + 0.3), table="barrel")
    for k, frac in enumerate((0.3, 0.6)):
        x, y = on_rim(262.0, frac)
        tank(b, (x, y, rz), 4.0, 7.0, tint=srgb(0.70, 0.30, 0.18), roof="dome", ladder_side=0.0)
    x, y = on_rim(266.0, 0.85)
    b.marker("loot", (x, y, rz + 0.1), table="fuel-barrel")
    b.marker("loot", (x + 2.0, y, rz + 0.1), table="fuel-barrel")
    b.piece("drum-stack", (x - 2.0, y + 1.0, rz), (0, 0, 20))
    for ang in (232.0, 20.0, 110.0, 300.0):
        x, y = on_rim(ang, 0.15)
        b.piece("flood-mast", (x, y, rz), (0, 0, ang + 90.0))
    pts = [on_rim(a, 0.9) for a in (214.0, 230.0, 246.0, 262.0)]
    road(b, [(x, y, rz) for x, y in pts], width=10.0)
    x, y = on_rim(224.0, 0.85)
    b.marker("guard", (x, y, rz + 0.2), role="reclaimer")
    o1 = ellipse(A_RIM + PLATEAU + 130.0, B_RIM + PLATEAU + 130.0, math.radians(240.0))
    b.marker("spawn", (o1[0], o1[1], 0.0), kind_of="approach")


def floor(b):
    """The pit floor: haul trucks, ore piles, spoil, a puddle, boulders, a lamp mast, old drill steel."""
    for x, y, yaw in ((40.0, -60.0, 140), (-80.0, 30.0, 70)):
        b.piece("haul-truck", (x, y, 0), (0, 0, yaw))
    for k, (x, y, s) in enumerate(((150.0, -70.0, 26.0), (175.0, 75.0, 22.0), (-30.0, -90.0, 30.0),
                                   (-150.0, 40.0, 24.0), (60.0, 90.0, 18.0))):
        P.rock(b, (x, y, 0.0), (s, s * 0.8, s * 0.42), seed=k + 200, rough=0.25, rings=6, sides=12, mat=MATTE,
               tint=ORE if k % 2 == 0 else SPOIL, solid="hull", squash=0.2)
    # The working face: unexcavated ground the wheel is cutting into, a scree of spoil below it.
    P.rock(b, (262.0, 18.0, 0.0), (110.0, 150.0, 104.0), seed=410, rough=0.22, freq=1.1, rings=10, sides=18,
           mat=CONCRETE, tint=srgb(0.48, 0.42, 0.35), solid="hull", squash=0.1, wear=0.7)
    P.rock(b, (205.0, 10.0, 0.0), (26.0, 40.0, 9.0), seed=411, rough=0.3, rings=6, sides=12, mat=MATTE,
           tint=SPOIL, solid="hull", squash=0.3)
    for k in range(24):
        a = b.rng.uniform(0, math.tau)
        x, y = ellipse(A_FLOOR - 10.0 - 30.0 * b.rng.random(), B_FLOOR - 8.0 - 20.0 * b.rng.random(), a)
        s = 2.0 + 4.0 * b.rng.random()
        P.rock(b, (x, y, s * 0.25), (s * 1.3, s, s * 0.7), seed=k + 300, rough=0.35, rings=5, sides=8,
               tint=srgb(0.45, 0.40, 0.34))
    b.piece("flood-mast", (0.0, 60.0, 0), (0, 0, 180))
    b.piece("flood-mast", (90.0, -100.0, 0), (0, 0, 60))
    for p, t in (((30.0, -50.0), "barrel"), ((-70.0, 40.0), "barrel"), ((100.0, 60.0), "crate")):
        b.marker("loot", (p[0], p[1], 0.1), table=t)
    b.marker("guard", (-20.0, -70.0, 0.1), role="reclaimer")


def build(b):
    pts = [ellipse(A_RIM + PLATEAU + 50.0, B_RIM + PLATEAU + 50.0, 2 * math.pi * k / 32, 0.08, 7) for k in range(32)]
    b.set_pad(("poly", [(round(x, 2), round(y, 2)) for x, y in pts]), ground="dirt", blend=30.0, clear=10.0,
              rim=RIM_Z)
    benches(b)
    ramp_and_belt(b)
    excavator(b)
    rim_works(b)
    floor(b)
    b.zone("no-build", (0, 0, 0), size=(1100.0, 900.0, 260.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "plains", "visible_km": 18, "pit": True})


SUN = (28.0, 220.0)
FOG = 0.00014
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((60.0, -14.0, 0.0), 230.0), ((96.0, -2.0, 0.0), 30.0)]
VIEWS = [
    {"name": "far", "target": (80.0, 0.0, 60.0), "distance": 3000, "yaw": 25, "pitch": -0.6, "lens": 100},
    {"name": "overview", "target": (20.0, -20.0, 20.0), "distance": 1150, "yaw": 30, "pitch": 30, "lens": 30},
    {"name": "machine", "target": (114.5, 12.6, 33.3), "distance": 92, "yaw": -60, "pitch": -20, "lens": 18},
]
