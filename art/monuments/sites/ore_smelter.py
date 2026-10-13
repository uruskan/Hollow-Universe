"""Ore Smelter (MONUMENTS.md §5.4, large tier, catalogue 45): a Seedward smelting works 500 x 300 m near the mines,
in the industrial livery (board-formed concrete, painted steel, corrugated cladding, yellow rails, hazard stripes).
Ore arrives on the rail spur at the tippler house (east), goes up the crusher house and along covered conveyors to
the stockyard and the bins, then up the skip bridge to two blast furnaces standing in their steel frames with their
downcomers, dust catchers and stoves; molten metal ran from the cast house to the ladle cars; slag went west to the
heaps that now smoke beside the works. A tall stack, the gas holder, the power house and the offices complete it.

Gameplay: refined metal makings in the cast house and the stockyard; the furnace control room behind the cast house
is the cyan room (cyan chip and a power cell); heat hazard round the furnaces and the slag (burns); crates on the
furnace platforms reached by stairs; Reclaimers in the cast house and on the conveyors.
"""
import math

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, YELLOW, cable, fence, ladder,
                        lamp, railing, rubble, srgb, stairs, stripes, tank)
from monkit.structures import chimney, conveyor, road, shed, silo
from monkit.surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL,
                             RUST)

KEY = "ore-smelter"
TITLE = "Ore Smelter"
SEED = 4501
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [320.0, 1500.0, 22000.0]

STEEL = srgb(0.38, 0.40, 0.42)
STEEL_DK = srgb(0.26, 0.27, 0.28)
CLAD = srgb(0.56, 0.55, 0.50)
CLAD_RED = srgb(0.55, 0.30, 0.22)
SLAG = srgb(0.24, 0.22, 0.21)
SLAG_HOT = srgb(1.0, 0.42, 0.10)
ORE_T = srgb(0.46, 0.30, 0.24)
FURNACES = ((-40.0, 30.0), (40.0, 30.0))


def furnace(b, fx, fy, tag):
    """A blast furnace: the shell on its hearth, bands, the bustle main, a steel frame round it with platforms every
    10 m and a stair tower, the top with its charging gear and uptakes, the downcomer to the dust catcher, three
    stoves, tuyere stock."""
    b.cyl(9.0, 6.0, (fx, fy, 3.0), mat=CONCRETE, tint=srgb(0.48, 0.46, 0.43), sides=28)
    prof = [(7.4, 0.0), (8.0, 8.0), (8.4, 14.0), (7.6, 22.0), (6.2, 34.0), (5.0, 42.0), (4.2, 46.0), (0.0, 46.5)]
    b.lathe(prof, (fx, fy, 6.0), mat=RUST, tint=srgb(0.75, 0.70, 0.66), sides=28)
    for z in (10.0, 16.0, 22.0, 28.0, 34.0, 40.0):
        t = z
        r = 8.0 if z < 16 else 8.4 - (z - 14.0) * 0.07
        b.cyl(r + 0.12, 0.5, (fx, fy, 6.0 + z), mat=PAINT, tint=STEEL_DK, sides=28, cap=False, solid=False, vary=0)
    pts = [(fx + math.cos(a) * 10.0, fy + math.sin(a) * 10.0, 16.0) for a in [2 * math.pi * k / 20 for k in range(21)]]
    b.tube(pts, 0.9, mat=RUST, tint=(1, 1, 1), sides=10, cap=False)
    for k in range(12):
        a = 2 * math.pi * k / 12
        b.tube([(fx + math.cos(a) * 10.0, fy + math.sin(a) * 10.0, 15.2), (fx + math.cos(a) * 8.2,
                                                                          fy + math.sin(a) * 8.2, 12.0)],
               0.3, mat=RUST, tint=(1, 1, 1), sides=6)
    levels = [0.0, 12.0, 22.0, 32.0, 42.0, 52.0]
    P.steel_frame(b, (fx, fy, 0), (0, 0, 0), 26.0, 26.0, levels, col=0.8, tint=STEEL, bays=(2, 2),
                  beam=(0.4, 0.6), brace_w=0.3, brace=("w", "e"))
    for z in levels[1:]:
        r_in = {12.0: 8.8, 22.0: 8.4, 32.0: 7.6, 42.0: 6.4, 52.0: 0.0}[z]
        hole = (fx - r_in, fy - r_in, fx + r_in, fy + r_in) if r_in else None
        P.deck(b, fx - 13.0, fy - 13.0, fx + 13.0, fy + 13.0, z, mat=GRATE, rails="snwe",
               gaps={"e": [(fy - 13.0, fy - 11.2)]}, hole=hole, edge=False)
    P.stair_tower(b, (fx + 15.6, fy - 11.4, 0.0), (0, 0, 0), levels, width=1.1, frame_tint=STEEL_DK)
    for z in levels[1:]:
        P.deck(b, fx + 13.0, fy - 12.8, fx + 14.4, fy - 11.4, z, mat=GRATE, rails="", edge=False)
    # The top: charging hopper, uptakes and the downcomer to the dust catcher.
    top = 52.0
    b.cyl(3.2, 5.0, (fx, fy, top + 2.5), mat=PAINT, tint=STEEL, sides=16)
    b.cyl(2.0, 3.0, (fx, fy, top + 6.5), mat=PAINT, tint=STEEL, sides=12, radius2=3.0)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.tube([(fx + math.cos(a) * 3.6, fy + math.sin(a) * 3.6, top - 2.0),
                (fx + math.cos(a) * 3.6, fy + math.sin(a) * 3.6, top + 10.0),
                (fx + math.cos(a) * 1.0, fy + math.sin(a) * 1.0, top + 14.0)], 0.9, mat=RUST, tint=(1, 1, 1),
               sides=10)
    b.tube([(fx, fy, top + 14.0), (fx, fy + 6.0, top + 16.0), (fx, fy + 24.0, 30.0), (fx, fy + 30.0, 22.0)], 1.3,
           mat=RUST, tint=(1, 1, 1), sides=12)
    b.cyl(4.5, 10.0, (fx, fy + 30.0, 17.0), mat=PAINT, tint=STEEL_DK, sides=20)
    b.cyl(4.5, 5.0, (fx, fy + 30.0, 9.5), mat=PAINT, tint=STEEL_DK, sides=20, radius2=1.0, solid="hull")
    for dx in (-3.0, 3.0):
        for dy in (-3.0, 3.0):
            b.box((0.6, 0.6, 12.0), (fx + dx, fy + 30.0 + dy, 6.0), mat=PAINT, tint=STEEL_DK)
    P.beacon(b, (fx, fy, top + 9.5), 0.5)
    # Three hot-blast stoves north of it, joined by the hot-blast main.
    for k in range(3):
        sx = fx - 14.0 + k * 14.0
        sy = fy + 48.0
        b.cyl(5.5, 34.0, (sx, sy, 17.0), mat=PAINT, tint=srgb(0.50, 0.48, 0.45), sides=20)
        b.lathe([(5.5, 0.0), (4.8, 2.4), (3.0, 4.2), (0.0, 5.0)], (sx, sy, 34.0), mat=PAINT,
                tint=srgb(0.50, 0.48, 0.45), sides=20)
        for z in (8.0, 16.0, 24.0):
            b.cyl(5.6, 0.4, (sx, sy, z), mat=PAINT, tint=STEEL_DK, sides=20, cap=False, solid=False, vary=0)
        b.tube([(sx, sy - 5.5, 14.0), (sx, fy + 11.0, 16.0)], 0.8, mat=RUST, tint=(1, 1, 1), sides=10)
    b.tube([(fx - 16.0, fy + 42.0, 14.0), (fx + 16.0, fy + 42.0, 14.0)], 1.0, mat=RUST, tint=(1, 1, 1), sides=12)
    P.stencil(b, tag, (fx - 4.0, fy - 9.0, 22.0), (0, 0, 0), 5.0, tint=YELLOW, wrap=8.1)
    b.marker("loot", (fx - 10.0, fy - 10.0, 22.1), table="crate")
    b.marker("loot", (fx + 10.0, fy + 10.0, 42.1), table="military-crate")
    b.marker("landmark", (fx, fy, top + 14.0), kind_of="blast-furnace", visible_km=15)
    b.zone("heat", (fx, fy, 12.0), radius=16.0, damage=6, kind_of="furnace")


def cast_house(b):
    """The cast house in front of the furnaces: a tall shed over the casting floor, runners in the floor, the
    tapholes, a ladle car on the tracks, the control room (the cyan room) at its east end."""
    cx, cy = 0.0, 2.0
    P.hall(b, (cx, cy, 0), (0, 0, 0), 120.0, 26.0, 20.0, bay=8.0, plinth=3.0, roof="gable", pitch=10.0, clad_tint=CLAD,
           roof_tint=srgb(0.45, 0.44, 0.42), frame_tint=STEEL_DK, band=YELLOW, ruin=0.25, windows=("front",),
           win_h=2.4, doors={"front": [(-40.0, 8.0, 7.0, "open"), (0.0, 8.0, 7.0, "shutter"), (40.0, 8.0, 7.0, "open")],
                             "left": [(0.0, 1.4, 2.3, "door")], "right": [(-6.0, 1.4, 2.3, "door")]})
    b.box((118.0, 24.0, 2.6), (cx, cy + 1.0, 1.6), mat=CONCRETE, tint=srgb(0.40, 0.38, 0.35))
    for fx, fy in FURNACES:
        for k, dx in enumerate((-10.0, 0.0, 10.0)):
            b.box((1.2, 18.0, 0.4), (fx + dx, cy + 2.0, 2.95), mat=MATTE, tint=SLAG, solid=False)
            b.box((0.5, 10.0, 0.06), (fx + dx, cy + 4.0, 3.12), mat=LAMP, tint=srgb(0.9, 0.35, 0.08), solid=False,
                  vary=0)
    stairs(b, (-50.0, cy - 12.0, 0.3), (0, 0, 0), 2.0, 2.6, 4.0, tint=YELLOW)
    for x in (-40.0, 40.0):
        b.piece("track-10", (x, cy - 22.0, 0.0), (0, 0, 90))
        b.piece("track-10", (x, cy - 32.0, 0.0), (0, 0, 90))
        b.cyl(2.6, 3.4, (x, cy - 22.0, 3.6), mat=RUST, tint=srgb(0.6, 0.55, 0.5), sides=16, radius2=3.0)
        b.box((2.6, 6.0, 1.0), (x, cy - 22.0, 1.4), mat=PAINT, tint=STEEL_DK)
        b.cyl(2.7, 0.3, (x, cy - 22.0, 5.4), mat=MATTE, tint=SLAG, sides=16, solid=False)
    for p in ((-30.0, cy + 6.0, 2.95), (20.0, cy + 8.0, 2.95), (-55.0, cy - 6.0, 0.4)):
        b.marker("loot", p, table="crate")
    b.marker("guard", (-10.0, cy, 2.95), role="reclaimer")
    b.marker("guard", (30.0, cy - 6.0, 2.95), role="reclaimer")
    # The control room: a block at the east end with a glazed front and the cyan door.
    rx, ry = 72.0, 2.0
    P.room(b, (rx, ry, 0), (0, 0, 0), 14.0, 12.0, 4.0, thick=0.5, mat=FORMWORK, tint=CONCRETE_T,
           openings={"left": [(-2.0, 1.4, 2.3, 0.0)], "front": [(0.0, 8.0, 1.2, 1.2)]})
    b.box((8.0, 0.06, 1.2), (rx, ry - 6.0, 1.8), mat=GLASS, solid=False)
    P.blast_door(b, (rx - 7.3, ry - 2.0, 0.0), (0, 0, -90), w=1.4, h=2.3, depth=0.9, frame=0.5, tint=srgb(0.15, 0.45, 0.50),
                 frame_tint=CONCRETE_T, chip="cyan", cell=True, kind="control-room")
    for k in range(4):
        b.box((2.4, 0.9, 1.0), (rx - 4.0 + 3.0 * k, ry + 4.5, 0.5), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
    b.marker("loot", (rx + 4.0, ry + 1.0, 0.12), table="military-crate")
    b.marker("loot", (rx - 3.0, ry + 1.0, 0.12), table="military-crate")
    b.marker("loot", (rx + 5.0, ry - 3.0, 0.12), table="sealed-crate")


def ore_line(b):
    """The rail spur and the tippler house in the east, the crusher house, conveyors to the stockyard and bins, and
    the inclined skip bridges to the furnace tops."""
    for k in range(9):
        b.piece("track-10", (250.0 - k * 10.0, -105.0, 0.0), (0, 0, 0))
    b.extrude([(-1.7, 0.0), (1.7, 0.0), (1.25, 0.32), (-1.25, 0.32)], 90.0, (210.0, -105.0, 0.0), (0, 0, -90),
              mat=CONCRETE, tint=srgb(0.43, 0.41, 0.37), solid="hull")
    for k in range(4):
        b.piece("hopper-wagon", (230.0 - k * 12.9, -105.0, 0.0), (0, 0, 0), tint=srgb(0.55, 0.32, 0.22))
    P.hall(b, (160.0, -105.0, 0), (0, 0, 0), 22.0, 16.0, 16.0, bay=5.5, plinth=2.0, clad_tint=CLAD_RED,
           frame_tint=STEEL_DK, band=YELLOW, ruin=0.2, windows=(), skylights=False,
           open_sides=("left", "right"))
    P.hall(b, (150.0, -45.0, 0), (0, 0, 0), 24.0, 20.0, 30.0, bay=6.0, plinth=4.0, clad_tint=CLAD,
           frame_tint=STEEL_DK, band=YELLOW, ruin=0.2, windows=("front", "back"), win_h=2.0, skylights=False,
           doors={"front": [(0.0, 4.0, 4.0, "shutter")]})
    b.box((8.0, 8.0, 4.0), (150.0, -45.0, 32.4), mat=CORRUGATED, tint=CLAD)
    conveyor(b, (160.0, -97.0, 6.0), (150.0, -55.0, 27.0), width=1.6, tint=CLAD)
    conveyor(b, (138.0, -45.0, 24.0), (100.0, -45.0, 14.0), width=1.6, tint=CLAD)
    # The stockyard: ore and coke heaps, a stacker on rails, the bin row.
    for k, (x, y, s, t) in enumerate(((95.0, -80.0, 22.0, ORE_T), (60.0, -82.0, 20.0, SLAG),
                                      (25.0, -84.0, 18.0, ORE_T), (-15.0, -82.0, 20.0, srgb(0.18, 0.17, 0.17)))):
        P.rock(b, (x, y, 0.0), (s * 1.6, s, s * 0.8), seed=k + 60, rough=0.18, rings=6, sides=12, mat=MATTE, tint=t,
               solid="hull", squash=0.1)
    for k in range(2):
        b.piece("track-10", (40.0 + (k - 0.5) * 60.0, -66.0, 0.0), (0, 0, 0))
    with b.frame((70.0, -66.0, 0.0), (0, 0, 0)):
        for x in (-4.0, 4.0):
            b.box((1.0, 6.0, 9.0), (x, 0, 4.5), mat=PAINT, tint=YELLOW)
        b.box((10.0, 6.0, 1.6), (0, 0, 9.5), mat=PAINT, tint=YELLOW)
        P.truss(b, (0.0, -2.0, 9.0), (0.0, -24.0, 4.0), w=2.2, h=1.8, chord=0.25, web=0.14, tint=YELLOW)
    for fx, _ in FURNACES:
        for k in range(3):
            silo(b, (fx - 16.0 + 8.0 * k, -48.0, 0.0), radius=3.4, height=8.0, cone=3.0, legs=4.0, tint=CLAD, count=1)
    conveyor(b, (100.0, -48.0, 15.0), (-70.0, -48.0, 19.0), width=1.4, tint=CLAD)
    # Skip bridges from the bins to each furnace top.
    for fx, fy in FURNACES:
        P.truss(b, (fx + 8.0, -48.0, 6.0), (fx + 3.0, fy - 3.0, 56.0), w=3.0, h=2.6, chord=0.4, web=0.2, tint=STEEL)
        b.box((2.2, 3.0, 2.4), (fx + 6.5, -36.0, 18.0), (0, -62.0, 90), mat=PAINT, tint=YELLOW, solid=False)
    b.marker("guard", (120.0, -45.0, 0.3), role="reclaimer")
    b.marker("loot", (150.0, -42.0, 0.4), table="crate")
    b.marker("loot", (95.0, -60.0, 0.1), table="barrel")


def slag(b):
    """The slag heaps west of the works: dark terraced heaps with glowing runs, a slag road, a slag pot car."""
    for k, (x, y, s) in enumerate(((-200.0, 40.0, 70.0), (-205.0, -50.0, 55.0), (-150.0, 110.0, 40.0))):
        P.rock(b, (x, y, 0.0), (s, s * 0.8, s * 0.62), seed=k + 80, rough=0.3, freq=1.4, rings=7, sides=14,
               mat=MATTE, tint=SLAG, solid="hull", squash=0.25)
        for j in range(4):
            a = b.rng.uniform(0, math.tau)
            r = s * (0.25 + 0.15 * b.rng.random())
            b.box((s * 0.18, 1.0, 0.08), (x + math.cos(a) * r, y + math.sin(a) * r, s * 0.62 * 0.3),
                  (b.rng.uniform(-15, 15), 0, math.degrees(a) + 90), mat=LAMP, tint=SLAG_HOT, solid=False, vary=0.1)
    b.zone("heat", (-200.0, 20.0, 5.0), size=(140.0, 160.0, 30.0), damage=3, kind_of="slag")
    road(b, [(-60.0, -30.0, 0), (-130.0, -20.0, 0), (-170.0, 20.0, 0)], width=8.0)
    b.piece("wreck-truck", (-140.0, -14.0, 0.1), (0, 0, 160), tint=srgb(0.30, 0.28, 0.26))
    b.cyl(2.8, 3.6, (-120.0, -26.0, 3.2), (0, 18, 30), mat=RUST, tint=srgb(0.6, 0.55, 0.5), sides=16, radius2=3.4)
    P.scorch(b, (-122.0, -28.0, 0.0), 6.0, blobs=4)


def works(b):
    """The stack, the gas holder, the power house, the offices, roads, the fence, lamps."""
    chimney(b, (110.0, 55.0, 0.0), height=110.0, r_base=5.0, r_top=3.2, bands=(RED, OFFWHITE))
    b.box((10.0, 10.0, 3.0), (110.0, 55.0, 1.5), mat=CONCRETE, tint=CONCRETE_T)
    b.tube([(40.0, 75.0, 10.0), (100.0, 62.0, 10.0), (106.0, 57.0, 6.0)], 1.2, mat=RUST, tint=(1, 1, 1), sides=12)
    # Gas holder: a tall drum in a lattice of guide columns.
    gx, gy = 170.0, 60.0
    b.cyl(18.0, 26.0, (gx, gy, 13.0), mat=PAINT, tint=srgb(0.40, 0.46, 0.40), sides=32)
    b.lathe([(18.0, 0.0), (14.0, 2.4), (0.0, 3.4)], (gx, gy, 26.0), mat=PAINT, tint=srgb(0.40, 0.46, 0.40), sides=32)
    for k in range(10):
        a = 2 * math.pi * k / 10
        b.box((0.8, 0.8, 34.0), (gx + math.cos(a) * 20.0, gy + math.sin(a) * 20.0, 17.0), mat=PAINT, tint=STEEL)
    for z in (11.0, 22.0, 33.0):
        pts = [(gx + math.cos(a) * 20.0, gy + math.sin(a) * 20.0, z) for a in [2 * math.pi * k / 10 for k in range(11)]]
        b.tube(pts, 0.3, mat=PAINT, tint=STEEL, sides=6, cap=False)
    b.marker("landmark", (gx, gy, 34.0), kind_of="gas-holder")
    # Power house and offices.
    P.hall(b, (190.0, 0.0, 0), (0, 0, 0), 40.0, 22.0, 14.0, bay=8.0, plinth=3.0, clad_tint=CLAD_RED,
           frame_tint=STEEL_DK, band=YELLOW, ruin=0.15, windows=("front", "back"), win_h=3.0,
           doors={"front": [(-10.0, 1.4, 2.3, "door"), (8.0, 6.0, 6.0, "shutter")]})
    for k in range(3):
        b.box((6.0, 4.0, 4.0), (178.0 + k * 12.0, 4.0, 2.3), mat=PAINT, tint=srgb(0.30, 0.42, 0.40), bevel=0.1)
    b.marker("loot", (196.0, -4.0, 0.4), table="crate")
    from monkit.kit import block_building
    block_building(b, (-120.0, -110.0, 0), (0, 0, 0), 30.0, 12.0, 7.4, floors=2, broken=0.5, mat=FORMWORK,
                   tint=CONCRETE_T)
    b.marker("guard", (-110.0, -125.0, 0.1), role="reclaimer")
    road(b, [(0.0, -170.0, 0), (0.0, -130.0, 0), (230.0, -130.0, 0)], width=9.0)
    road(b, [(0.0, -130.0, 0), (-230.0, -130.0, 0)], width=8.0)
    road(b, [(230.0, -130.0, 0), (230.0, 130.0, 0)], width=7.0)
    fence(b, [(-8.0, -148.0, 0), (-245.0, -148.0, 0), (-245.0, 145.0, 0), (245.0, 145.0, 0), (245.0, -80.0, 0)],
          height=2.6, post=5.0)
    fence(b, [(245.0, -125.0, 0), (245.0, -148.0, 0), (8.0, -148.0, 0)], height=2.6, post=5.0)
    for x in (-8.0, 8.0):
        b.box((0.4, 0.4, 3.0), (x, -148.0, 1.5), mat=PAINT, tint=GREY, bevel=0.02)
    for x, y, yaw in ((-80.0, -125.0, 0), (60.0, -125.0, 0), (140.0, -125.0, 0), (225.0, -40.0, 90),
                      (-90.0, 60.0, 0), (90.0, 100.0, 180)):
        b.piece("flood-mast", (x, y, 0.0), (0, 0, yaw))
    for x, y, yaw, tint in ((200.0, -70.0, 0, CLAD_RED), (-60.0, 110.0, 80, srgb(0.4, 0.42, 0.44))):
        b.piece("container-40", (x, y, 0.0), (0, 0, yaw), tint=tint)
    P.scrap(b, (-80.0, 80.0), 14.0, count=14, tints=[RUSTY_T, STEEL, SLAG, CLAD])
    P.scrap(b, (200.0, 100.0), 12.0, count=10, tints=[RUSTY_T, STEEL, CLAD_RED])
    for p, t in (((-60.0, -120.0), "barrel"), ((120.0, -120.0), "barrel"), ((220.0, 80.0), "crate"),
                 ((-90.0, 100.0), "barrel")):
        b.marker("loot", (p[0], p[1], 0.1), table=t)
    b.marker("spawn", (0.0, -180.0, 0), kind_of="approach")


RUSTY_T = srgb(0.48, 0.30, 0.20)


def build(b):
    b.set_pad(("rect", 500.0, 300.0), ground="dirt", blend=35.0, clear=12.0)
    for fx, fy in FURNACES:
        furnace(b, fx, fy, "B1" if fx < 0 else "B2")
    cast_house(b)
    ore_line(b)
    slag(b)
    works(b)
    b.zone("no-build", (0, 0, 0), size=(580.0, 380.0, 200.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "mines", "visible_km": 15})


SUN = (26.0, 220.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((-86.0, -14.0, 0.0), 120.0), ((-70.0, -6.0, 0.0), 200.0)]
VIEWS = [
    {"name": "far", "target": (0.0, 20.0, 40.0), "distance": 2500, "yaw": 28, "pitch": -0.5, "lens": 100},
    {"name": "overview", "target": (0.0, 0.0, 15.0), "distance": 620, "yaw": 30, "pitch": 28, "lens": 30},
    {"name": "casthouse", "target": (-40.0, 10.0, 20.0), "distance": 69, "yaw": -63, "pitch": -14, "lens": 22},
]
