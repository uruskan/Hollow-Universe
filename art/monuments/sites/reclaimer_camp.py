"""Reclaimer Camp (MONUMENTS.md §5.3, medium tier, safe zone): the clans' market in a swamp, built into the
concrete frame of a ruined Seedward block and walled with welded scrap and a beached barge. Over the square turns
the Big Wheel, a gambling wheel 13 m across on a scrap tower, its rim of bulbs seen from far off at night; the bar
fills the ruin's ground floor, shacks climb its floors and stand on stilts round the pools, boardwalks cross the
mud. Safe: the camp's guards shoot anyone marked hostile (§7.4). Trades, gambling, a recycler.
"""
import math
import random

from monkit import parts_medium as pm
from monkit.kit import (BLACK, BLUE, CONCRETE, CONCRETE_T, DARK, GREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        WARM_LAMP, YELLOW, cable, ladder, lamp, lattice_tower, railing, rubble, srgb)
from monkit.structures import ship_hull, watchtower
from monkit.surfaces import CORRUGATED, FORMWORK, GRATE, LAMP, MATTE, RUST

KEY = "reclaimer-camp"
TITLE = "Reclaimer Camp"
SEED = 3611
BUILDER = {"chunk": 48.0, "max_edge": 4.0}

RUIN = (0.0, 30.0)
RW, RD, FH, LEVELS = 26.0, 14.0, 3.6, 4
WHEEL = (21.0, 14.0, 17.0)
WALL = [(-74.0, -4.0), (-70.0, -50.0), (-20.0, -55.0), (-6.5, -55.0), None, (6.5, -55.0), (22.0, -55.0),
        (66.0, -46.0), (74.0, -10.0), (74.0, -3.0), None, (74.0, 7.0), (72.0, 30.0), (60.0, 55.0), (10.0, 61.0),
        (-40.0, 58.0), (-68.0, 40.0), (-70.0, 36.0)]
STALLS = ("vendor-stall-blue", "vendor-stall-orange", "vendor-stall-green")


def walls(b):
    run = []
    for p in WALL + [None]:
        if p is None:
            for a, c in zip(run, run[1:]):
                pm.scrap_wall(b, (a[0], a[1], 0), (c[0], c[1], 0), height=3.4, gaps=0.04, marks=0.3)
            run = []
        else:
            run.append(p)
    # The beached barge closes the west side; a shack on its deck.
    ship_hull(b, (-73.0, 16.0, 0), (0, 0, 6.0), length=40.0, width=9.0, height=7.0, stations=10,
              tint=srgb(0.45, 0.33, 0.26), band=srgb(0.55, 0.2, 0.14), damage=0.12, buried=0.32, n=16)
    with b.frame((-73.0, 16.0, 0), (0, 0, 6.0)):
        b.box((7.0, 30.0, 0.2), (0, 0, 4.7), mat=RUST, tint=(1, 1, 1))
        pm.shack(b, (0.0, 4.0, 4.8), (0, 0, 90), 5.0, 4.0, 2.4, roof_tint=srgb(0.5, 0.42, 0.36))
        b.marker("guard", (0.0, -8.0, 4.8), 270.0, role="camp-guard")
        ladder(b, (4.9, -10.0, 0), 4.8, (0, 0, 90), cage=False)
    # Gates: scrap doors on hinges, lamps, clan banners; watchtowers at the corners.
    for (x, y, a) in ((0.0, -55.0, 0.0), (74.0, 2.0, 90.0)):
        with b.frame((x, y, 0), (0, 0, a)):
            for side in (-1, 1):
                b.box((0.5, 0.5, 4.6), (side * 6.7, 0, 2.3), mat=RUST, tint=(1, 1, 1))
                pm.banner(b, (side * 6.7, -0.4, 4.2), (0, 0, 0), 1.0, 2.8, tint=(pm.TARP_RED, pm.TARP_TAN)[side > 0])
                lamp(b, (side * 6.7, -0.4, 4.8), (-20, 0, 0), size=(0.45, 0.25, 0.2))
            b.box((13.8, 0.4, 0.5), (0, 0, 4.8), mat=RUST, tint=(1, 1, 1))
            with b.frame((-6.4, 0.2, 0), (0, 0, 70)):
                pm.scrap_wall(b, (0, 0, 0), (6.2, 0, 0), height=3.2, gaps=0.0, marks=0.5)
            pm.code_plate(b, (0, -0.25, 4.8), (0, 0, 0), 3.0, 0.45, base=srgb(0.2, 0.2, 0.2), ink=YELLOW)
            b.marker("door", (0, 0, 0), 180.0, kind_of="gate", width=12.0, height=4.6)
            b.marker("guard", (-4.0, 3.0, 0), 180.0, role="camp-guard")
            b.marker("guard", (4.0, 3.0, 0), 180.0, role="camp-guard")
    for (x, y, a) in ((-62.0, -44.0, 30.0), (59.0, -40.0, -30.0), (63.0, 47.0, 200.0), (-58.0, 50.0, 150.0)):
        watchtower(b, (x, y, 0), (0, 0, a), height=7.5, tint=srgb(0.42, 0.36, 0.30))
    for m in b.markers:
        if m["kind"] == "guard" and m.get("role") == "sentry":
            m["role"] = "camp-guard"


def ruin(b):
    """The concrete frame of a four-storey block: columns, slabs with collapsed bays, block infill below, scrap
    above, a stair in the west bay, the bar on the ground floor."""
    rng = b.rng
    rx, ry = RUIN
    xs = (-13.0, -6.5, 0.0, 6.5, 13.0)
    ys = (-7.0, 0.0, 7.0)
    top = LEVELS * FH
    with b.frame((rx, ry, 0)):
        for i, x in enumerate(xs):
            for j, y in enumerate(ys):
                h = top + 0.3
                if (i, j) in ((4, 2), (3, 2)):
                    h = top - FH + 1.2 + rng.uniform(0, 1.0)
                b.box((0.5, 0.5, h), (x, y, h / 2), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62))
                if h < top:
                    for k in range(4):
                        b.tube([(x - 0.15 + 0.1 * k, y, h), (x - 0.2 + 0.13 * k, y + 0.1, h + rng.uniform(0.4, 0.9))],
                               0.012, mat=RUST, tint=(1, 1, 1), sides=4)
        stair = (-10.9, -3.15, -8.6, 2.2)
        for k in range(1, LEVELS + 1):
            holes = []
            if k < LEVELS:
                holes.append(stair)
            else:
                holes += [(6.5, 0.0, 13.25, 7.25), (-9.0, 4.2, -8.0, 5.2)]
            if k == 2:
                holes.append((0.0, -7.25, 6.5, 0.0))
            if k == 3:
                holes.append((6.5, -7.25, 13.25, 0.0))
            pm.slab_holes(b, -RW / 2 - 0.25, -RD / 2 - 0.25, RW / 2 + 0.25, RD / 2 + 0.25, k * FH - 0.3, 0.3, holes,
                          mat=CONCRETE, tint=srgb(0.68, 0.67, 0.64))
            for y in (-RD / 2 - 0.25, RD / 2 + 0.25):
                b.box((RW + 0.5, 0.12, 0.35), (0, y, k * FH - 0.15), mat=CONCRETE, tint=srgb(0.6, 0.59, 0.56),
                      solid=False)
        # The collapsed roof bay: a slab hanging from its edge, rubble under it.
        with b.frame((9.8, 7.0, top - 0.15), (-58.0, 0, 3.0)):
            b.box((6.6, 7.2, 0.3), (0, -3.6, 0), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62))
        rubble(b, (10.0, 3.5, FH * 3), 2.5, count=6, tint=srgb(0.66, 0.65, 0.62), max_size=1.0)
        rubble(b, (10.0, 9.5, 0.0), 3.0, count=10, tint=srgb(0.66, 0.65, 0.62), max_size=1.4)
        rubble(b, (3.0, -9.0, 0.0), 2.0, count=6, tint=srgb(0.66, 0.65, 0.62), max_size=0.9)
        # Block infill: the back and sides of the two lower storeys, windows punched through.
        for k in range(2):
            z = k * FH
            pm.wall(b, (RW / 2, RD / 2, z), (-RW / 2, RD / 2, z), FH - 0.3, 0.25,
                    [(4.0, 1.4, 1.2, 1.0), (10.0, 1.4, 1.2, 1.0), (16.0, 1.4, 1.2, 1.0), (22.0, 1.4, 1.2, 1.0)],
                    mat=CONCRETE, tint=srgb(0.62, 0.62, 0.6), glass=0.1)
            pm.wall(b, (-RW / 2, RD / 2, z), (-RW / 2, -RD / 2, z), FH - 0.3, 0.25,
                    [(10.0, 1.2, 2.2, 0)] if k == 0 else [(7.0, 1.4, 1.2, 1.0)], mat=CONCRETE,
                    tint=srgb(0.62, 0.62, 0.6), glass=0.1)
            pm.wall(b, (RW / 2, -RD / 2, z), (RW / 2, RD / 2, z), FH - 0.3, 0.25, [(7.0, 1.4, 1.2, 1.0)],
                    mat=CONCRETE, tint=srgb(0.62, 0.62, 0.6), glass=0.1)
        # Scrap rooms on the upper floors, a lookout on the roof.
        pm.scrap_wall(b, (-6.5, -RD / 2, 2 * FH), (0.0, -RD / 2, 2 * FH), height=2.6)
        pm.scrap_wall(b, (-13.0, -RD / 2, 2 * FH), (-6.5, -RD / 2, 2 * FH), height=2.4)
        pm.shack(b, (9.75, 3.5, 2 * FH), (0, 0, 0), 5.5, 6.0, 2.5)
        pm.shack(b, (-3.25, 3.5, 3 * FH), (0, 0, 180), 5.5, 6.0, 2.4)
        pm.tarp_shelter(b, (-3.0, -2.5, top), (0, 0, 0), 6.0, 4.5, tint=pm.TARP_ORANGE)
        pm.barricade(b, (-3.0, -RD / 2 + 0.6, top), (0, 0, 0), 9.0)
        pm.flag(b, (-12.0, -6.0, top), 7.0, tint=pm.TARP_RED)
        b.marker("guard", (-3.0, -3.0, top), 180.0, role="camp-guard")
        b.marker("guard", (2.0, 0.0, 2 * FH), 180.0, role="camp-guard")
        pm.switchback(b, (-9.75, -1.9, 0), (0, 0, 0), 3 * FH, width=1.0, flight_rise=FH / 2, tint=srgb(0.5, 0.4, 0.3),
                      landing_rails=False)
        ladder(b, (-8.5, 5.15, 3 * FH), FH, (0, 0, 0), cage=False)
        # Front slab edges: rails of rope and pipe on the floors above the square.
        for k in (1, 2, 3):
            for x0, x1 in ((-13.0, -6.5), (-6.5, 0.0), (0.0, 6.5)):
                if (k, x0) in ((2, 0.0),):
                    continue
                railing(b, (x0, -RD / 2 - 0.2, k * FH), (x1, -RD / 2 - 0.2, k * FH), tint=srgb(0.5, 0.4, 0.3))
        # The bar on the ground floor, its terrace under a tarp in front.
        b.box((12.0, 0.9, 1.1), (6.0, -2.0, 0.55), mat=PAINT, tint=srgb(0.38, 0.3, 0.24))
        b.box((12.2, 1.0, 0.06), (6.0, -2.0, 1.13), mat=MATTE, tint=pm.WOOD, solid=False)
        for x in (1.0, 4.0, 7.0, 10.0):
            b.box((1.6, 0.4, 1.8), (x, 2.0, 1.0), mat=MATTE, tint=pm.WOOD, solid=False)
            for z in (0.6, 1.2, 1.8):
                for j in range(4):
                    b.cyl(0.04, 0.28, (x - 0.6 + j * 0.4, 2.0, z + 0.15), mat=pm.GLASS, sides=6, solid=False, vary=0)
        b.marker("vendor", (6.0, -1.0, 0.0), 180.0, sells="drinks")
        b.box((7.0, 0.1, 1.4), (6.0, -RD / 2 - 0.3, 3.0), mat=PAINT, tint=srgb(0.18, 0.16, 0.14))
        pm.stencil(b, (6.0, -RD / 2 - 0.37, 3.0), (0, 0, 0), "BAR", height=1.0, tint=srgb(1.0, 0.65, 0.2), mat=LAMP)
        pm.tarp_shelter(b, (6.0, -12.0, 0), (0, 0, 0), 14.0, 8.0, h_front=3.0, h_back=3.4, tint=pm.TARP_TAN,
                        sag=0.4)
        for k, (tx, ty) in enumerate(((0.0, -10.0), (4.0, -13.0), (8.0, -9.5), (12.0, -13.5), (-1.0, -14.5))):
            b.cyl(0.6, 0.06, (tx, ty, 0.95), mat=MATTE, tint=pm.WOOD, sides=12, solid=False)
            b.cyl(0.08, 0.92, (tx, ty, 0.46), tint=DARK, sides=6, solid=False)
            for j in range(3):
                a = 2 * math.pi * j / 3 + k
                b.cyl(0.22, 0.45, (tx + math.cos(a) * 1.0, ty + math.sin(a) * 1.0, 0.225), mat=RUST, tint=(1, 1, 1),
                      sides=10, solid=False)
        pm.string_lights(b, (-1.0, -RD / 2 - 0.1, 3.4), (13.0, -16.0, 3.2))
        pm.string_lights(b, (13.0, -RD / 2 - 0.1, 3.4), (-1.0, -16.0, 3.2))
        for k, (x, kind) in enumerate(((-11.0, "skull"), (-4.0, "slash"), (11.5, "ring"))):
            pm.mark(b, (x, -RD / 2 - 0.2, 1.6 + (k % 2) * 4.0), (0, 0, 0), 2.0, kind)


def big_wheel(b):
    """The Big Wheel: a gambling wheel 13 m across on a scrap lattice tower, segments painted in colours, a rim of
    bulbs, a pointer at the top; the betting counter at its foot."""
    x, y, z = WHEEL
    R = 6.5
    lattice_tower(b, (x, y + 2.6, 0), z + 1.0, 4.2, 2.6, sections=5, member=0.16, tint=srgb(0.42, 0.34, 0.28))
    b.cyl(0.25, 3.2, (x, y + 1.0, z), (90, 0, 0), tint=DARK, sides=10)
    pm.deck(b, x - 1.8, y + 1.4, x + 1.8, y + 3.8, z + 1.0, rails=("back", "left"))
    ladder(b, (x + 2.2, y + 2.6, 0), z + 1.0, (0, 0, 90), cage=True)
    b.beam((x, y + 2.0, z + 1.0), (x, y - 0.3, z + R + 1.6), 0.3, 0.3, tint=srgb(0.42, 0.34, 0.28))
    b.marker("guard", (x, y + 2.8, z + 1.0), 180.0, role="camp-guard")
    colours = (srgb(0.80, 0.16, 0.12), srgb(0.95, 0.80, 0.22), srgb(0.16, 0.36, 0.62), OFFWHITE,
               srgb(0.22, 0.55, 0.30), srgb(0.10, 0.10, 0.10))
    digits = ("1", "5", "2", "9", "3", "0", "4", "7", "2", "5", "8", "3")
    with b.frame((x, y - 0.4, z), (90, 0, 8.0)):
        n = 12
        pts = [(R * math.cos(2 * math.pi * k / 32), R * math.sin(2 * math.pi * k / 32), 0.0) for k in range(33)]
        b.tube(pts, 0.2, mat=PAINT, tint=srgb(0.85, 0.7, 0.2), sides=8, cap=False)
        pts = [(R * 0.86 * math.cos(2 * math.pi * k / 32), R * 0.86 * math.sin(2 * math.pi * k / 32), 0.0)
               for k in range(33)]
        b.tube(pts, 0.1, tint=DARK, sides=6, cap=False)
        for k in range(n):
            a0 = 2 * math.pi * k / n
            a1 = 2 * math.pi * (k + 1) / n
            b.beam((0, 0, 0), (R * math.cos(a0), R * math.sin(a0), 0), 0.14, 0.14, tint=DARK)
            grid = []
            for j in range(2):
                rr = 1.0 + (R * 0.86 - 1.0) * j
                grid.append([(rr * math.cos(a0 + (a1 - a0) * t), rr * math.sin(a0 + (a1 - a0) * t), -0.05)
                             for t in (0.04, 0.5, 0.96)])
            pm.sheet(b, grid, thick=0.04, mat=PAINT, tint=colours[k % len(colours)])
            am = (a0 + a1) / 2
            with b.frame((R * 0.62 * math.cos(am), R * 0.62 * math.sin(am), 0.03), (-90, 0, 0)):
                with b.frame((0, 0, 0), (0, math.degrees(am) - 90, 0)):
                    pm.stencil(b, (0, 0, 0), (0, 0, 0), digits[k], height=0.9,
                               tint=OFFWHITE if k % len(colours) not in (1, 3) else DARK, thick=0.02)
        for k in range(24):
            a = 2 * math.pi * (k + 0.5) / 24
            b.box((0.22, 0.22, 0.22), (R * 1.0 * math.cos(a), R * 1.0 * math.sin(a), 0.2), mat=LAMP,
                  tint=(WARM_LAMP, srgb(1.0, 0.3, 0.2))[k % 2], solid=False, vary=0)
            if k % 6 == 0:
                b.marker("light", (R * math.cos(a), R * math.sin(a), 0.4), colour=[1.0, 0.75, 0.4], range=14.0,
                         chase=True)
        b.cyl(0.8, 0.6, (0, 0, 0.1), tint=srgb(0.85, 0.7, 0.2), sides=16)
        b.cyl(0.35, 0.3, (0, 0, 0.5), mat=LAMP, tint=WARM_LAMP, sides=12, solid=False, vary=0)
    # The pointer above the wheel.
    b.extrude([(-0.5, 0.0), (0.5, 0.0), (0.0, -1.1)], 0.3, (x, y - 0.6, z + R + 1.3), (0, 0, 0), mat=PAINT, tint=RED)
    b.box((0.3, 0.3, 1.4), (x, y + 0.2, z + R + 0.9), tint=DARK)
    b.marker("landmark", (x, y, z + R + 1.6), kind_of="big-wheel", visible_km=6)
    # The betting counter at the foot.
    with b.frame((x, y - 3.0, 0)):
        b.box((6.0, 1.0, 1.1), (0, 0, 0.55), mat=PAINT, tint=srgb(0.55, 0.15, 0.12))
        b.box((6.2, 1.1, 0.06), (0, 0, 1.13), mat=MATTE, tint=pm.WOOD, solid=False)
        pm.tarp_shelter(b, (0, 0.8, 0), (0, 0, 0), 7.0, 3.0, h_front=2.8, h_back=3.0, tint=pm.TARP_RED, sag=0.1)
        b.marker("gambling-wheel", (0, 0.8, 0.0), 180.0, wheel=[round(v, 2) for v in WHEEL])
        pm.code_plate(b, (0, -0.52, 0.75), (0, 0, 0), 3.0, 0.4, base=srgb(0.95, 0.8, 0.22), ink=DARK)


def square(b):
    rng = b.rng
    # Stalls round the square, strung lights criss-crossing it.
    n = 0
    goods = ("weapons", "components", "food", "medical", "clothing", "tools", "ammunition", "scrap", "boats",
             "electrical", "seeds", "armour")
    for (x, y) in ((-26.0, -32.0), (-16.0, -38.0), (16.0, -38.0), (27.0, -31.0), (-32.0, -14.0), (34.0, -12.0),
                   (-28.0, 4.0), (38.0, 4.0)):
        a = math.degrees(math.atan2(-18.0 - y, 2.0 - x)) + 90.0
        b.piece(STALLS[n % 3], (x, y, 0), (0, 0, a))
        with b.frame((x, y, 0), (0, 0, a)):
            b.marker("vendor", (0.0, 0.0, 0.0), 180.0, sells=goods[n])
            b.piece("crate-stack", (2.6, 0.5, 0), (0, 0, rng.uniform(-30, 30)))
        n += 1
    poles = [(-24.0, -24.0), (-8.0, -30.0), (10.0, -30.0), (26.0, -22.0), (24.0, -2.0), (-22.0, -4.0)]
    for (x, y) in poles:
        b.cyl(0.08, 6.0, (x, y, 3.0), mat=RUST, tint=(1, 1, 1), sides=6, solid=False)
    for i in range(len(poles)):
        for j in (i + 2, i + 3):
            if j < len(poles):
                a, c = poles[i], poles[j]
                pm.string_lights(b, (a[0], a[1], 5.8), (c[0], c[1], 5.8), sag=0.04,
                                 colour=(WARM_LAMP, srgb(1.0, 0.5, 0.3), srgb(0.6, 0.9, 1.0))[(i + j) % 3])
    for (x, y) in ((-10.0, -16.0), (12.0, -18.0), (-4.0, -40.0), (30.0, -42.0), (-36.0, -26.0)):
        b.piece("fire-barrel", (x, y, 0))
    for (x, y, a) in ((-6.0, -22.0, 10.0), (6.0, -26.0, -20.0), (-16.0, -10.0, 75.0)):
        with b.frame((x, y, 0), (0, 0, a)):
            b.box((2.4, 0.5, 0.08), (0, 0, 0.45), mat=MATTE, tint=pm.WOOD, solid=False)
            for xx in (-1.0, 1.0):
                b.cyl(0.25, 0.42, (xx, 0, 0.21), mat=RUST, tint=(1, 1, 1), sides=10, solid=False)
    # The recycler under a scrap roof by the gate.
    rx, ry = -16.0, -47.0
    pm.lean_to(b, (rx, ry, 0), (0, 0, 0), 6.0, 5.0, h_front=3.2, h_back=3.6, tint=srgb(0.55, 0.45, 0.38),
               missing=0.1)
    b.box((3.2, 2.4, 0.2), (rx, ry, 0.1), mat=CONCRETE, tint=srgb(0.6, 0.58, 0.55))
    b.marker("recycler", (rx, ry, 0.2), 180.0)
    pm.mark(b, (rx, ry + 2.7, 1.6), (0, 0, 0), 1.2, "arrow", pm.GRAFFITI[1])
    pm.barricade(b, (rx - 6.0, ry + 2.0, 0), (0, 0, 80), 4.0)


def shanty(b):
    """Rows of shacks on the west and north with alleys between, stilted shacks round the pool on the east joined by
    boardwalks, container homes, wrecks, boats; pools of swamp water (look-dev only); trees round the camp."""
    rng = b.rng
    roofs = (srgb(0.6, 0.55, 0.5), srgb(0.55, 0.38, 0.3), srgb(0.42, 0.5, 0.52), srgb(0.62, 0.6, 0.55),
             srgb(0.5, 0.45, 0.32))
    sells = ("fish", "rope", "ammunition", "cloth", "fuel", "parts", "medicine", "food")
    k = 0
    for row_x, rot in ((-63.0, 90.0), (-53.0, -90.0), (-43.0, 90.0)):
        y = -42.0
        while y < 31.0:
            w = rng.uniform(3.8, 5.4)
            if rng.random() > 0.14 and not (row_x == -43.0 and -22.0 < y < -4.0):
                pm.shack(b, (row_x + rng.uniform(-0.6, 0.6), y + w / 2, 0), (0, 0, rot + rng.uniform(-4, 4)), w,
                         rng.uniform(3.4, 4.4), rng.uniform(2.3, 2.7), roof_tint=roofs[k % len(roofs)])
                if k % 4 == 1:
                    with b.frame((row_x, y + w / 2, 0), (0, 0, rot)):
                        b.marker("vendor", (0.0, 0.0, 0.12), 180.0, sells=sells[(k // 4) % len(sells)])
                k += 1
            y += w + rng.uniform(0.6, 1.6)
    for x0, x1 in ((-38.0, -16.0), (30.0, 50.0)):
        x = x0
        while x < x1:
            w = rng.uniform(3.8, 5.2)
            pm.shack(b, (x + w / 2, 50.5 + rng.uniform(-0.5, 0.5), 0), (0, 0, rng.uniform(-4, 4)), w,
                     rng.uniform(3.4, 4.2), rng.uniform(2.3, 2.7), roof_tint=roofs[k % len(roofs)])
            k += 1
            x += w + rng.uniform(0.8, 2.0)
    # Strung lights down the alleys, braziers.
    for x in (-58.0, -48.0):
        pm.string_lights(b, (x, -40.0, 3.4), (x, -10.0, 3.2), sag=0.02)
        pm.string_lights(b, (x, -8.0, 3.2), (x, 28.0, 3.4), sag=0.02)
    for (x, y) in ((-58.0, -20.0), (-48.0, 12.0), (-20.0, 44.0), (40.0, 45.0)):
        b.piece("fire-barrel", (x, y, 0))
    # The east side: stilted shacks round the pool and the boardwalks between them.
    for k2, (x, y, a, st) in enumerate(((44.0, -30.0, -20.0, 1.6), (52.0, -18.0, -70.0, 1.6), (58.0, 2.0, -95.0, 1.4),
                                       (52.0, 24.0, -110.0, 1.5), (64.0, -26.0, -60.0, 1.2),
                                       (64.0, 18.0, -120.0, 1.2))):
        pm.shack(b, (x, y, 0), (0, 0, a), rng.uniform(3.6, 5.0), rng.uniform(3.2, 4.2), 2.4, stilts=st,
                 roof_tint=roofs[k2 % len(roofs)])
        if k2 in (1, 3):
            with b.frame((x, y, 0), (0, 0, a)):
                b.marker("vendor", (0.0, 0.0, st + 0.12), 180.0, sells=("boats", "fish")[k2 // 3])
    pm.boardwalk(b, (36.0, -20.0, 0.3), (44.0, -27.0, 1.6))
    pm.boardwalk(b, (44.0, -26.0, 1.6), (52.0, -16.0, 1.6))
    pm.boardwalk(b, (52.0, -15.0, 1.6), (57.0, 0.0, 1.4))
    pm.boardwalk(b, (57.0, 3.0, 1.4), (52.0, 22.0, 1.5))
    pm.boardwalk(b, (52.0, 25.0, 1.5), (40.0, 30.0, 0.3))
    pm.boardwalk(b, (58.0, 0.0, 1.4), (66.0, 2.0, 0.3))
    # Container homes in the north-east with awnings, wrecks pushed against the walls, boats by the pool.
    for i, (x, y, a) in enumerate(((56.0, 40.0, 130.0), (62.0, 34.0, 120.0), (20.0, 52.0, 0.0))):
        b.piece("container-20", (x, y, 0), (0, 0, a), tint=(srgb(0.55, 0.22, 0.16), srgb(0.2, 0.36, 0.52),
                                                             srgb(0.36, 0.42, 0.28))[i])
        with b.frame((x, y, 0), (0, 0, a)):
            pm.tarp_shelter(b, (0.0, -2.6, 0), (0, 0, 0), 5.0, 2.6, h_front=2.2, h_back=2.5,
                            tint=(pm.TARP_BLUE, pm.TARP_ORANGE, pm.TARP_GREEN)[i], sag=0.15)
    for (x, y, a) in ((-30.0, 32.0, 75.0), (34.0, -46.0, 10.0)):
        b.piece("utility-truck", (x, y, 0), (0, 0, a), tint=srgb(0.36, 0.3, 0.26))
    for (x, y, a) in ((42.0, -6.0, 30.0), (46.0, 8.0, -60.0), (60.0, -10.0, 100.0)):
        with b.frame((x, y, 0.0), (0, 4.0, a)):
            prof = ship_hull(b, (0, 0, 0), (0, 0, 0), length=5.0, width=1.8, height=0.9, stations=6, nose=0.35,
                             tail=0.1, tint=(srgb(0.3, 0.42, 0.36), srgb(0.6, 0.3, 0.18), OFFWHITE)[int(x) % 3],
                             band=DARK, n=10, buried=0.2)
    rubble(b, (-36.0, 40.0, 0.0), 2.5, count=8, tint=srgb(0.4, 0.36, 0.32), mat=PAINT, max_size=1.0)
    rubble(b, (66.0, -40.0, 0.0), 2.5, count=8, tint=srgb(0.45, 0.32, 0.25), mat=PAINT, max_size=1.0)
    # Pools of standing water (the game's swamp draws its own); dead trees and live ones round the camp.
    prng = random.Random(SEED)
    for (cx, cy, r) in ((50.0, -4.0, 9.0), (-86.0, -40.0, 12.0), (84.0, 52.0, 10.0), (-30.0, 68.0, 9.0),
                        (88.0, -60.0, 8.0), (-88.0, 60.0, 9.0)):
        pts = []
        for i in range(16):
            a = 2 * math.pi * i / 16
            rr = r * (0.75 + 0.5 * prng.random())
            pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr * 0.8))
        pm.preview_water(pts, 0.04, name=f"pool_{int(cx)}_{int(cy)}")
    for (x, y, h, leaves) in ((-84.0, -20.0, 14.0, True), (-90.0, 20.0, 11.0, False), (-82.0, 44.0, 13.0, True),
                              (-20.0, 70.0, 12.0, True), (30.0, 70.0, 10.0, False), (84.0, 30.0, 15.0, True),
                              (88.0, -30.0, 12.0, True), (60.0, -64.0, 9.0, False), (-50.0, -66.0, 12.0, True),
                              (-92.0, -64.0, 10.0, False), (92.0, 6.0, 13.0, True), (-26.0, -70.0, 11.0, True),
                              (70.0, 66.0, 12.0, True), (-70.0, 66.0, 10.0, False)):
        pm.tree(b, (x, y, 0.0), h, spread=h * 0.3, trunk=0.2 + h * 0.012, leaves=leaves, lean=0.06,
                tints=(pm.LEAF, pm.LEAF2, pm.MOSS))
    for i in range(20):
        x, y = rng.uniform(-96, 96), rng.uniform(-72, 72)
        if abs(x) < 76 and -56 < y < 62:
            continue
        pm.shrub(b, (x, y, 0.0), rng.uniform(0.8, 1.6), tints=(pm.LEAF, pm.LEAF2, pm.MOSS, pm.LEAF_DRY))


def build(b):
    b.set_pad(("rect", 200.0, 150.0), ground="dirt", blend=14.0, clear=4.0)
    walls(b)
    ruin(b)
    big_wheel(b)
    square(b)
    shanty(b)
    b.zone("safe", (0.0, 3.0, 15.0), size=(150.0, 118.0, 40.0), hostile_minutes=10, enforcers="camp-guard")
    b.zone("no-build", (0, 0, 0), size=(230.0, 180.0, 80.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "reclaimer", "spawn": "swamps-forests",
                   "safe": True})


SUN = (26.0, 235.0)
PEOPLE = [((2.0, -62.0, 0), 10.0), ((-4.0, -30.0, 0), 200.0), ((8.0, -12.0, 0), 160.0), ((19.0, 9.0, 0), 20.0)]
VIEWS = [
    {"name": "overview", "target": (0, 0, 6), "distance": 175, "yaw": 28, "pitch": 30, "lens": 32},
    {"name": "gate", "target": (0, -20, 10), "distance": 60, "yaw": 0, "pitch": -7, "lens": 24},
    {"name": "wheel", "target": (19, 10, 13), "distance": 32, "yaw": -15, "pitch": -16, "lens": 24},
]
