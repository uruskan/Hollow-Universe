"""Scrapyard (MONUMENTS.md §5.2 no. 16, small tier): a salvage yard near an old town, Seedward machinery worked over by
Reclaimers. Behind a corrugated fence and a wall of stacked containers: rows of flattened cars stacked four high, wrecks
on top; mounds of mixed scrap; a ship's bow section and a stripped skiff; the yellow material handler with its magnet
holding a car over the baler, bales piled at the press's end; the weighbridge and its office at the gate; a workshop
shed; a tyre heap; a recycler. Salvage piles to pick over, crates in the shed.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, lamp, srgb,
                        stripes)
from monkit.structures import hull_ring, shed, ship_hull
from monkit.surfaces import CONCRETE, CORRUGATED, GRATE, MATTE, PAINT, RUST

KEY = "scrapyard"
TITLE = "Scrapyard"
SEED = 1616
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

FX, FY = 60.0, 45.0         # the fence's half extents
GATE = (-7.0, 7.0)
CAR_TINTS = (srgb(0.55, 0.62, 0.66), srgb(0.7, 0.28, 0.2), srgb(0.82, 0.8, 0.74), srgb(0.3, 0.4, 0.5),
             srgb(0.62, 0.55, 0.3), srgb(0.35, 0.45, 0.35), srgb(0.25, 0.25, 0.27), srgb(0.6, 0.3, 0.45))


def fence_run(b, a, c, height=2.6):
    """Corrugated sheet fencing on posts from `a` to `c`: one sheet per 2 m, sheets of several colours, a few
    leaning or gone."""
    a, c = Vector(a + (0,)), Vector(c + (0,))
    d = c - a
    n = max(1, int(d.length / 2.0))
    yaw = math.degrees(math.atan2(d.y, d.x))
    tints = (srgb(0.6, 0.63, 0.62), srgb(0.52, 0.3, 0.2), srgb(0.4, 0.46, 0.4), srgb(0.66, 0.66, 0.6))
    for k in range(n + 1):
        p = a + d * (k / n)
        b.box((0.1, 0.1, height + 0.3), (p.x, p.y, (height + 0.3) / 2), (0, 0, yaw), mat=PAINT, tint=DARK,
              solid=False, vary=0)
    for k in range(n):
        p = a + d * ((k + 0.5) / n)
        r = b.rng.random()
        if r < 0.05:
            continue
        lean = b.rng.uniform(8, 25) if r < 0.12 else b.rng.uniform(-1.5, 1.5)
        mat = CORRUGATED if b.rng.random() < 0.8 else RUST
        with b.frame((p.x, p.y, 0.0), (0, 0, yaw)):
            b.box((d.length / n + 0.06, 0.05, height), (0, 0, height / 2), (lean, 0, 0), mat=mat,
                  tint=tints[b.rng.randrange(len(tints))], solid=False, vary=0.05)
    mid = (a + c) / 2
    b.collide_box(Vector((mid.x, mid.y, height / 2)), (d.length, 0.12, height), (0, 0, yaw))


def car_row(b, x, y0, y1, heights=(3, 4), wrecks=0.35):
    """A row of flattened cars stacked along Y at `x`, each stack a little out of true; a wreck or two on top."""
    n = int((y1 - y0) / 4.0)
    for k in range(n):
        y = y0 + (y1 - y0) * (k + 0.5) / n
        h = b.rng.choice(heights)
        z = 0.0
        for j in range(h):
            b.piece(b.rng.choice(("car-flat", "car-flat-b")),
                    (x + b.rng.uniform(-0.15, 0.15), y + b.rng.uniform(-0.2, 0.2), z),
                    (b.rng.uniform(-2, 2), b.rng.uniform(-2, 2), b.rng.uniform(-6, 6) + (180 if b.rng.random() < 0.5
                                                                                         else 0)),
                    tint=CAR_TINTS[b.rng.randrange(len(CAR_TINTS))])
            z += 0.48
        if b.rng.random() < wrecks:
            b.piece("car-wreck", (x, y, z), (b.rng.uniform(-3, 3), 0, b.rng.uniform(-15, 15)),
                    tint=CAR_TINTS[b.rng.randrange(len(CAR_TINTS))])
        b.collide_box(Vector((x, y, z / 2)), (1.8, 3.8, z), (0, 0, 0))


def tyre_heap(b, centre, radius, height, count=26):
    """A heap of old tyres: a dark mound with tyres thrown over it."""
    P.mound(b, centre, radius, height, tint=srgb(0.07, 0.07, 0.075), rough=0.3, mat=MATTE)
    prof = [(0.3, 0.0), (0.5, 0.0), (0.54, 0.08), (0.54, 0.24), (0.5, 0.31), (0.3, 0.31), (0.27, 0.155)]
    cx, cy, cz = centre
    for k in range(count):
        a = b.rng.random() * math.tau
        f = math.sqrt(b.rng.random()) * 0.95
        z = cz + height * max(0.0, 1.0 - f ** 1.6) * 0.9
        b.lathe(prof, (cx + math.cos(a) * radius * f, cy + math.sin(a) * radius * f, z),
                (b.rng.uniform(-60, 60), b.rng.uniform(-60, 60), b.rng.uniform(0, 360)), mat=MATTE, tint=P.RUBBER,
                sides=14, solid=False)


def weighbridge(b, at):
    """The weighbridge at the gate: a steel deck on a concrete pit with ramps, kerbs, and the weigh office with its
    window to the deck and a boom."""
    x, y = at
    P.grid_box(b, (4.0, 17.0, 0.3), (x, y, 0.0), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), step=2.0)
    for k in range(5):
        P.grid_box(b, (3.2, 3.15, 0.08), (x, y - 6.4 + 3.2 * k, 0.17), mat=PAINT, tint=srgb(0.4, 0.42, 0.42),
                   step=1.6)
    for s in (-1, 1):
        b.box((0.25, 16.0, 0.25), (x + s * 1.85, y, 0.25), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
        stripes(b, (x + s * 1.85, y - 8.0, 0), (x + s * 1.85, y + 8.0, 0), 0.25, width=0.5, at_z=0.13,
                normal=(s, 0, 0))
    for s in (-1, 1):
        b.box((3.4, 1.2, 0.2), (x, y + s * 8.9, 0.04), (s * -6.0, 0, 0), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
    floor = P.prefab_hut(b, (x + 4.6, y + 1.0, 0), (0, 0, -90.0), w=3.6, d=2.6, h=2.5, code="WB-1", door_open=60.0,
                         windows=1, band=ORANGE)
    P.boom_barrier(b, (x - 2.4, y - 9.6, 0.0), 0.0, 4.4, raised=62.0)
    return floor


def skip(b, at, yaw, fill, label):
    """A roll-off skip: a tapered steel bin on two runners, ribbed sides, a hook frame at the front, heaped with one
    sorted metal (coils, sheet or mixed), its label painted on."""
    x, y, z = at
    with b.frame((x, y, z), (0, 0, yaw)):
        for sx in (-0.95, 0.95):
            b.box((0.18, 6.0, 0.22), (sx, 0, 0.11), mat=PAINT, tint=DARK)
        prof = [(-2.6, 0.22), (2.9, 0.22), (3.0, 1.7), (-3.0, 1.7)]
        with b.frame((0, 0, 0), (0, 0, 90)):
            b.extrude(prof, 2.3, (0, 0, 0), (0, 0, 0), mat=PAINT, tint=srgb(0.32, 0.4, 0.34), solid="hull")
        for k in range(6):
            for s in (-1, 1):
                b.box((0.1, 0.14, 1.45), (s * 1.18, -2.4 + 0.95 * k, 0.95), mat=PAINT, tint=srgb(0.32, 0.4, 0.34),
                      solid=False)
        b.box((2.3, 0.14, 0.14), (0, -2.95, 1.65), mat=PAINT, tint=srgb(0.32, 0.4, 0.34), solid=False)
        b.beam((0, -2.95, 1.6), (0, -3.3, 2.1), 0.16, 0.16, mat=PAINT, tint=DARK)
        b.beam((-0.7, -2.95, 1.0), (0, -3.3, 2.1), 0.1, 0.1, mat=PAINT, tint=DARK)
        b.beam((0.7, -2.95, 1.0), (0, -3.3, 2.1), 0.1, 0.1, mat=PAINT, tint=DARK)
        P.mound(b, (0, 0.1, 1.2), 1.45, 0.75, tint=fill, rough=0.35, mat=PAINT,
                squash=2.1)
        P.stencil(b, label, (1.16, 0.0, 0.7), (0, 0, 90), 0.42, srgb(0.92, 0.88, 0.7), align="centre")
        if label == "CU":
            for k in range(5):
                P.coil(b, (b.rng.uniform(-0.6, 0.6), b.rng.uniform(-1.8, 1.8), 1.7 + 0.1 * k), 0.35, 2.5, 0.025,
                       P.COPPER, spread=0.15)


def pipe_stack(b, at, yaw):
    """Lengths of old pipe stacked in a pyramid on timber bearers, a few rolled off."""
    x, y, z = at
    with b.frame((x, y, z), (0, 0, yaw)):
        for yy in (-2.5, 0.0, 2.5):
            b.box((3.4, 0.25, 0.2), (0, yy, 0.1), mat=PAINT, tint=P.WOOD_GREY)
        r = 0.28
        k = 0
        for row in range(4):
            n = 5 - row
            for j in range(n):
                xx = (j - (n - 1) / 2) * (2 * r + 0.02)
                zz = 0.2 + r + row * (2 * r * 0.87)
                b.cyl(r, 6.5 + 0.4 * b.rng.random(), (xx, b.rng.uniform(-0.3, 0.3), zz), (90, 0, 0), mat=RUST,
                      tint=(1, 1, 1), sides=12, cap=False, solid=False)
                b.cyl(r * 0.85, 6.6, (xx, 0, zz), (90, 0, 0), mat=MATTE, tint=srgb(0.06, 0.05, 0.05), sides=10,
                      cap=True, solid=False)
                k += 1
        for j in range(2):
            b.cyl(r, 6.5, (2.2 + 0.8 * j, 0.4, r), (90, 0, 8 + 10 * j), mat=RUST, tint=(1, 1, 1), sides=12,
                  solid=False)
        b.collide_box(Vector((0, 0, 1.1)), (3.0, 6.6, 2.2), (0, 0, 0))


def build(b):
    b.set_pad(("rect", 126.0, 110.0), ground="gravel", blend=12.0, clear=3.0)

    # The road out front and the driveway to the gate.
    P.long_road(b, (-63.0, -51.0), (63.0, -51.0), width=8.0)
    P.grid_box(b, (12.0, 4.0, 0.08), (0.0, -45.0, 0.04), mat="M_Mon_Asphalt", tint=(1, 1, 1), step=3.0)

    # The perimeter: corrugated fence on three sides, the gate, stacked containers along the back.
    fence_run(b, (-FX, -FY), (GATE[0], -FY))
    fence_run(b, (GATE[1], -FY), (FX, -FY))
    fence_run(b, (-FX, -FY), (-FX, FY - 3.0))
    fence_run(b, (FX, -FY), (FX, FY - 3.0))
    for x in GATE:
        b.box((0.35, 0.35, 3.4), (x, -FY, 1.7), mat=PAINT, tint=DARK)
    with b.frame((GATE[1], -FY, 0), (0, 0, 118.0)):
        for z in (0.25, 1.4, 2.5):
            b.box((6.8, 0.08, 0.08), (-3.4, 0, z), mat=PAINT, tint=GREY, solid=False)
        b.box((6.8, 0.03, 2.3), (-3.4, 0, 1.38), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
    with b.frame((GATE[0] + 3.0, -FY - 1.6, 0.06), (89.0, 0, 6.0)):
        for z in (0.25, 1.4, 2.5):
            b.box((6.8, 0.08, 0.08), (0, 0, z), mat=PAINT, tint=GREY, solid=False)
        b.box((6.8, 0.03, 2.3), (0, 0, 1.38), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
    P.plate(b, (GATE[0] - 2.5, -FY - 0.08, 1.7), (0, 0, 0), (2.2, 0.8), srgb(0.9, 0.85, 0.6), DARK, "YARD 3",
            text_h=0.32)
    ctints = (srgb(0.5, 0.21, 0.13), srgb(0.2, 0.32, 0.46), srgb(0.35, 0.45, 0.32), srgb(0.75, 0.55, 0.2),
              srgb(0.6, 0.6, 0.58), srgb(0.45, 0.25, 0.3))
    for k in range(19):
        x = -FX + 3.2 + k * 6.25
        b.piece("container-20", (x, FY - 1.3, 0), (0, 0, b.rng.uniform(-1.5, 1.5)),
                tint=ctints[b.rng.randrange(len(ctints))])
        if b.rng.random() < 0.8:
            b.piece("container-20", (x + b.rng.uniform(-0.3, 0.3), FY - 1.3, 2.59), (0, 0, b.rng.uniform(-3, 3)),
                    tint=ctints[b.rng.randrange(len(ctints))])

    # The weighbridge and its office inside the gate.
    weighbridge(b, (0.0, -34.0))

    # Rows of stacked flattened cars on the west side, aisles between them.
    car_row(b, -53.0, -36.0, 32.0)
    car_row(b, -46.0, -36.0, 32.0, heights=(3, 4))
    car_row(b, -39.0, -36.0, 32.0, heights=(2, 3))
    car_row(b, -32.0, -22.0, 26.0, heights=(2, 3, 4))
    car_row(b, -25.0, -12.0, 20.0, heights=(1, 2, 3))
    # Wrecks queued for the baler, roll-off skips of sorted metal, a stack of pipes, a stack of hull plates.
    for k in range(4):
        P.pod_car(b, (21.0 + 2.4 * k, -17.0 + b.rng.uniform(-0.6, 0.6), 0.0), (0, 0, 90.0 + b.rng.uniform(-12, 12)),
                  tint=CAR_TINTS[b.rng.randrange(len(CAR_TINTS))], wrecked=0.8 + 0.2 * b.rng.random(),
                  wheels=k % 2 == 0, flat=True)
    for k, (fill, label) in enumerate(((P.COPPER, "CU"), (srgb(0.7, 0.72, 0.74), "AL"), (srgb(0.45, 0.3, 0.22), "FE"),
                                       (srgb(0.2, 0.2, 0.22), "MIX"))):
        skip(b, (12.0 + 5.2 * k, -30.0, 0.0), b.rng.uniform(-3, 3), fill, label)
    pipe_stack(b, (-8.0, -27.0, 0.0), 20.0)
    with b.frame((-2.0, 34.0, 0.0), (0, 0, 12.0)):
        for x in (-2.0, 2.0):
            b.box((0.3, 3.2, 0.25), (x, 0, 0.12), mat=PAINT, tint=P.WOOD_GREY)
        for k in range(7):
            b.box((5.2, 2.6, 0.06), (b.rng.uniform(-0.2, 0.2), b.rng.uniform(-0.15, 0.15), 0.28 + 0.07 * k),
                  (0, 0, b.rng.uniform(-4, 4)), mat="M_Mon_Hull", tint=(P.HULL_WHITE, srgb(0.7, 0.7, 0.68),
                                                                       srgb(0.5, 0.48, 0.45))[k % 3], solid=False)
        b.collide_box(Vector((0, 0, 0.5)), (5.2, 2.6, 0.8), (0, 0, 0))
    P.scrap_heap(b, (22.0, 32.0, 0.0), 6.0, 3.0, count=22)
    P.scrap_heap(b, (-16.0, 12.0, 0.0), 5.0, 2.6, count=18)
    P.scrap_heap(b, (46.0, -30.0, 0.0), 6.0, 3.2, count=22)

    # The baler, the material handler holding a car over it, bales piled at the press's end.
    top = P.baler(b, (31.0, -8.0, 0.0), 0.0)
    P.material_handler(b, (17.1, -8.0, 0.0), 90.0, slew=0.0, boom=50.0, stick=28.0, drop=1.6)
    with b.frame((31.0, -8.0, 3.15), (0, -6.0, 82.0)):
        P.pod_car(b, (0, 0, 0), (0, 0, 0), tint=srgb(0.3, 0.4, 0.5), wrecked=1.0, wheels=False)
    for k, (dx, dy, dz) in enumerate(((0.0, 0.0, 0.0), (1.2, 0.1, 0.0), (2.4, -0.1, 0.0), (0.0, 1.7, 0.0),
                                      (1.2, 1.7, 0.0), (0.6, 0.2, 1.0), (1.8, 0.4, 1.0), (0.6, 1.6, 1.0),
                                      (1.2, 0.9, 2.0))):
        P.bale(b, (38.4 + dx, -10.0 + dy, 0.3 + dz), b.rng.uniform(-6, 6), b.rng.uniform(-3, 3))
    for k in range(6):
        P.bale(b, (39.0 + b.rng.uniform(-2.0, 3.0), -3.5 + b.rng.uniform(-1.5, 1.5), 0.0), b.rng.uniform(0, 90),
               b.rng.uniform(-8, 8))
    # A fuel bowser for the machines.
    with b.frame((12.0, -14.5, 0.0), (0, 0, 10.0)):
        for x in (-1.0, 1.0):
            b.box((0.2, 1.4, 0.3), (x, 0, 0.15), mat=PAINT, tint=DARK)
        b.cyl(0.65, 2.6, (0, 0, 0.95), (0, 90, 0), mat=PAINT, tint=srgb(0.62, 0.16, 0.1), sides=18)
        b.box((0.4, 0.4, 0.3), (0.6, 0, 1.7), mat=PAINT, tint=GREY, solid=False)
        P.coil(b, (1.6, 0.6, 0.0), 0.35, 3.0, 0.02, BLACK)

    # Scrap mounds, a ship's bow section, a stripped skiff, the tyre heap, a hauler cab on blocks.
    P.scrap_heap(b, (40.0, 12.0, 0.0), 8.0, 4.2, count=34)
    P.scrap_heap(b, (8.0, 24.0, 0.0), 7.0, 3.6, count=28)
    P.scrap_heap(b, (-8.0, 4.0, 0.0), 5.5, 2.8, count=20)
    b.marker("loot", (34.0, 7.0, 0.1), table="salvage")
    b.marker("loot", (3.0, 20.0, 0.1), table="salvage")
    b.marker("loot", (-12.0, 1.0, 0.1), table="salvage")
    prof = ship_hull(b, (-14.0, 26.0, 0.0), (0, 0, -32.0), length=30.0, width=7.0, height=5.2, stations=8,
                     damage=0.0, broken_at=0.5, n=16, buried=0.12, band=ORANGE)
    # Its broken end: a dark bulkhead just inside the break (the hull is a single skin, which the game draws from
    # outside only), so the cut reads as a dark interior and not as a hole through to the sky.
    with b.frame((-14.0, 26.0, 0.0), (0, 0, -32.0)):
        y, w, h, zc = prof[-1]
        ring = hull_ring(w * 0.98, h * 0.98, y - 0.4, zc, n=16)
        b.mesh(ring, [list(range(16))[::-1]], mat=MATTE, tint=srgb(0.04, 0.04, 0.045), solid=False, vary=0,
               smooth=False)
    P.skiff(b, (6.0, 8.0, 0.25), (0, 6.0, 140.0), torn=("left", "right"), hatch=None, panels_missing=0.5, burnt=0.2,
            skids=(False, True), dorsal=False)
    tyre_heap(b, (-18.0, -30.0, 0.0), 4.5, 2.2)
    with b.frame((-6.0, -16.0, 0.0), (0, 0, 25.0)):
        for y in (-2.6, 1.6, 2.9):
            for s in (-1, 1):
                b.box((0.5, 0.5, 0.55), (s * 1.0, y, 0.27), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        P.hauler(b, (0, 0, 0.0), (0, 0, 0), tint=srgb(0.78, 0.74, 0.66), band=srgb(0.5, 0.3, 0.2), code="HX-71",
                 broken=0.9, lights=False)

    # The workshop shed at the back, roller doors up; inside a car on stands, a bench, an engine hoist.
    shed(b, (38.0, 32.0, 0), (0, 0, 0), 24.0, 13.0, 7.0, roof="gable", pitch=12.0, tint=srgb(0.6, 0.63, 0.6),
         doors=[("front", -6.0, 5.0, 5.0), ("front", 6.0, 5.0, 5.0)], ruin=0.18)
    with b.frame((38.0, 32.0, 0.25), (0, 0, 0)):
        for x in (-7.5, -4.5):
            b.box((0.6, 0.6, 0.8), (x, -1.0, 0.4), mat=PAINT, tint=RED, solid=False)
            b.box((0.6, 0.6, 0.8), (x, 2.0, 0.4), mat=PAINT, tint=RED, solid=False)
        P.pod_car(b, (-6.0, 0.5, 0.65), (0, 0, 90.0), tint=srgb(0.7, 0.68, 0.6), wrecked=0.7, wheels=False)
        b.box((5.0, 0.9, 0.06), (6.0, 5.6, 0.95), mat=PAINT, tint=srgb(0.5, 0.42, 0.32), solid=False)
        for x in (3.8, 8.2):
            b.box((0.08, 0.8, 0.95), (x, 5.6, 0.47), mat=PAINT, tint=DARK, solid=False)
        P.toolbox(b, (5.0, 5.6, 0.98), 15.0, open_lid=True)
        for (ax, ay) in ((2.0, -1.0), (4.0, -1.0)):
            b.beam((ax, ay - 0.8, 0.0), (3.0, ay, 3.0), 0.12, 0.12, mat=PAINT, tint=YELLOW)
            b.beam((ax, ay + 0.8, 0.0), (3.0, ay, 3.0), 0.12, 0.12, mat=PAINT, tint=YELLOW)
        b.box((2.4, 0.15, 0.2), (3.0, -1.0, 3.0), mat=PAINT, tint=YELLOW, solid=False)
        b.tube([(3.0, -1.0, 2.9), (3.0, -1.0, 1.4)], 0.02, mat=RUST, tint=(1, 1, 1), sides=4, cap=False)
        b.box((0.7, 0.6, 0.6), (3.0, -1.0, 1.1), mat=PAINT, tint=srgb(0.3, 0.32, 0.33), solid=False)
    b.marker("loot", (44.0, 37.0, 0.3), table="crate")
    b.piece("recycler", (40.0, 22.0, 0.0), (0, 0, 0))
    b.piece("drum-stack", (29.0, 24.5, 0.0), (0, 0, 12.0))
    b.marker("loot", (27.6, 23.4, 0.05), table="barrel")
    b.piece("dumpster", (52.0, 22.0, 0.0), (0, 0, -20.0))

    # Lamp posts; a Reclaimer lookout on the containers by the gate.
    for (x, y, yaw) in ((-20.0, -40.0, 135.0), (22.0, -40.0, 45.0), (0.0, 0.0, 180.0), (-38.0, 36.0, -135.0)):
        b.piece("lamp-post", (x, y, 0.0), (0, 0, yaw))
    with b.frame((-FX + 3.2 + 6.25 * 8, FY - 1.3, 5.18), (0, 0, 0)):
        b.box((2.6, 2.2, 0.08), (0, 0, 0.04), mat=PAINT, tint=P.WOOD_GREY)
        for x in (-1.2, 1.2):
            b.box((0.08, 0.08, 1.9), (x, -1.0, 0.95), mat=PAINT, tint=P.WOOD_GREY, solid=False)
            b.box((0.08, 0.08, 1.9), (x, 1.0, 0.95), mat=PAINT, tint=P.WOOD_GREY, solid=False)
        P.tarp(b, [(-1.4, -1.2, 2.0), (1.4, -1.2, 2.0), (1.4, 1.2, 1.7), (-1.4, 1.2, 1.7)], sag=0.15,
               tint=P.TARP_GREEN, cells=(4, 3))
        for k in range(3):
            b.box((0.9, 0.05, 0.9), (-0.9 + 0.9 * k, -1.15, 0.5), (b.rng.uniform(-6, 6), 0, 0), mat=RUST,
                  tint=(1, 1, 1), solid=False)

    b.zone("no-build", (0, 0, 0), size=(140.0, 120.0, 30.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "near-town"})


SUN = (30.0, 215.0)
PEOPLE = [((3.5, -42.0, 0.0), 10.0)]
VIEWS = [
    {"name": "yard", "target": (0.0, -2.0, 3.0), "distance": 128, "yaw": 22, "pitch": 26, "lens": 30},
    {"name": "magnet", "target": (25.0, -8.0, 4.5), "distance": 24, "yaw": -38, "pitch": 6, "lens": 26},
    {"name": "stacks", "target": (-42.5, 6.0, 2.0), "distance": 28, "yaw": 0, "pitch": 3, "lens": 26},
]
