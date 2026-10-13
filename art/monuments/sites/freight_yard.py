"""Freight Yard (MONUMENTS.md §5.4, large tier, catalogue 40): a Seedward rail yard 700 x 300 m on the old main line.
Twelve sorting tracks between two ladders fill the middle, still crowded with rakes of boxcars, tank wagons, hoppers
and flatcars, two locomotives, a derailed boxcar and one on its side; signal gantries span them. North of the main
line the container terminal works under two gantry cranes; south of the yard stand the goods shed and its platform,
the yard office with its strongroom, the signal tower, and the grain silos at the east end with their loading shed;
the engine shed and its turntable close the west end.

Gameplay: crates in the boxcars (§5.4), the strongroom is the cyan room (cyan chip and a power cell) behind Warden
turrets, a recycler in the engine shed, Reclaimers working the yard and holding the signal tower.
"""
import math

from mathutils import Vector

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, cable, fence,
                        ladder, lamp, lattice_tower, railing, rubble, srgb, stairs, stripes)
from monkit.structures import conveyor, gantry_crane, road, silo
from monkit.surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST)

KEY = "freight-yard"
TITLE = "Freight Yard"
SEED = 4001
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [320.0, 1500.0, 22000.0]

YARD_Y = [-5.0 - 5.0 * k for k in range(12)]       # -5 .. -60
BALLAST = srgb(0.43, 0.41, 0.37)
IND_GREY = srgb(0.62, 0.63, 0.62)
STEEL_Y = srgb(0.86, 0.66, 0.14)
WAGON_TINTS = [srgb(0.55, 0.30, 0.20), srgb(0.36, 0.40, 0.42), srgb(0.62, 0.58, 0.48), srgb(0.30, 0.38, 0.30),
               srgb(0.50, 0.50, 0.50)]


def track(b, a, c, ballast=True):
    """Track pieces every 10 m from `a` to `c` (2D), on a ballast bed of its own unless the yard's slab is under it."""
    a, c = Vector((a[0], a[1], 0)), Vector((c[0], c[1], 0))
    d = c - a
    L = d.length
    yaw = math.degrees(math.atan2(d.y, d.x))
    n = max(1, int(math.ceil(L / 10.0 - 0.05)))
    for k in range(n):
        t = min((k * 10.0 + 5.0) / L, 1.0 - 5.0 / L) if L > 10.0 else 0.5
        p = a + d * t
        b.piece("track-10", (p.x, p.y, 0.0), (0, 0, yaw))
    if ballast:
        m = (a + c) / 2
        b.extrude([(-1.7, 0.0), (1.7, 0.0), (1.25, 0.32), (-1.25, 0.32)], L, (m.x, m.y, 0.0), (0, 0, yaw - 90),
                  mat=CONCRETE, tint=BALLAST, solid="hull", wear=0.8)


def rake(b, y, x0, kinds, tints=None, derail=None):
    """A row of wagons on the track at `y` from `x0` eastward; `derail` = index of one off the rails."""
    lengths = {"boxcar": 14.0, "flatcar": 14.0, "hopper-wagon": 12.0, "tank-wagon": 13.0, "locomotive": 18.0}
    x = x0
    placed = []
    for k, kind in enumerate(kinds):
        L = lengths[kind]
        cx = x + L / 2
        tint = None if kind == "locomotive" else (tints[k % len(tints)] if tints else None)
        if derail is not None and k == derail:
            b.piece(kind, (cx, y + 1.4, -0.3), (4.0, 0, 7.0), tint=tint)
        else:
            b.piece(kind, (cx, y, 0.0), (0, 0, 0), tint=tint)
        placed.append((kind, cx))
        x += L + 0.9
    return placed


def yard(b):
    """The sorting tracks between two ladders, the main line, the leads, the wagons, the signal gantries."""
    b.slab([(-292.0, -1.5), (302.0, -1.5), (186.0, -63.5), (-176.0, -63.5)], 0.3, (0, 0, 0), mat=CONCRETE,
           tint=BALLAST, wear=0.8)
    for y in YARD_Y:
        xw = -280.0 + (-5.0 - y) * 2.0
        xe = 290.0 - (-5.0 - y) * 2.0
        track(b, (xw, y), (xe, y), ballast=False)
    track(b, (-280.0, -5.0), (-170.0, -60.0), ballast=False)
    track(b, (290.0, -5.0), (180.0, -60.0), ballast=False)
    track(b, (-372.0, -5.0), (-280.0, -5.0))
    track(b, (290.0, -5.0), (372.0, -5.0))
    for y in (70.0, 75.0):
        track(b, (-372.0, y), (372.0, y))
    # The rakes.
    rake(b, -10.0, -70.0, ["locomotive"] + ["boxcar"] * 6, WAGON_TINTS)
    rake(b, -20.0, -10.0, ["tank-wagon"] * 8, [srgb(0.2, 0.21, 0.22), srgb(0.26, 0.26, 0.26)])
    rake(b, -30.0, -160.0, ["hopper-wagon"] * 7, [srgb(0.55, 0.32, 0.22), srgb(0.48, 0.30, 0.22)])
    placed = rake(b, -40.0, 4.0, ["flatcar"] * 6, [srgb(0.40, 0.36, 0.30)])
    for k, (kind, cx) in enumerate(placed):
        if k == 3:
            continue
        b.piece("container-40", (cx, -40.0, 2.06), (0, 0, 0), tint=WAGON_TINTS[(k + 2) % 5])
    rake(b, -50.0, -110.0, ["boxcar"] * 5, WAGON_TINTS[1:], derail=3)
    rake(b, -50.0, 2.0, ["boxcar", "hopper-wagon", "boxcar", "boxcar"], WAGON_TINTS[2:])
    rake(b, -55.0, 140.0, ["locomotive"])
    rake(b, -15.0, 120.0, ["boxcar", "boxcar", "tank-wagon", "boxcar"], WAGON_TINTS[2:])
    # One boxcar on its side by the east ladder, a burnt tank wagon in the west ladder.
    b.piece("boxcar", (215.0, -66.0, 1.6), (88.0, 0, 18.0), tint=WAGON_TINTS[0])
    b.piece("tank-wagon", (-205.0, -44.0, 0.0), (0, 0, -26.6), tint=srgb(0.08, 0.075, 0.07))
    P.scorch(b, (-205.0, -44.0, 0.31), 7.0, blobs=4)
    # Loot inside some boxcars (their doors are open), barrels by the rakes.
    for (x, y) in ((-70.0 + 18.9 + 7.0, -10.0), (-70.0 + 18.9 + 3 * 14.9 + 7.0, -10.0), (-110.0 + 7.0, -50.0),
                   (120.0 + 7.0, -15.0)):
        b.marker("loot", (x - 3.0, y, 2.0), table="crate")
    for (x, y) in ((-30.0, -62.5), (60.0, -2.5), (-140.0, -2.5)):
        b.marker("loot", (x, y, 0.32), table="barrel")
    # Signal gantries across the yard.
    for x in (-150.0, 165.0):
        for y in (2.0, -64.0):
            lattice_tower(b, (x, y, 0.0), 8.0, 1.4, 0.9, sections=2, member=0.12, tint=IND_GREY)
        P.truss(b, (x, -64.0, 8.0), (x, 2.0, 8.0), w=1.0, h=1.2, chord=0.14, web=0.08, tint=IND_GREY)
        for y in YARD_Y[::2]:
            b.box((0.5, 0.4, 1.4), (x + 0.6, y, 7.2), mat=PAINT, tint=BLACK, solid=False)
            b.box((0.05, 0.22, 0.22), (x + 0.86, y, 7.5), mat=LAMP, tint=srgb(1.0, 0.2, 0.1), solid=False, vary=0)
    for x in (-120.0, -20.0, 80.0, 200.0):
        b.piece("flood-mast", (x, -2.0, 0.3), (0, 0, 180))
        b.piece("flood-mast", (x + 50.0, -64.0, 0.3), (0, 0, 0))


def terminal(b):
    """The container terminal north of the main line: two tracks, stacks, two gantry cranes, a reach truck."""
    b.box((300.0, 56.0, 0.25), (0.0, 115.0, 0.125), mat=CONCRETE, tint=srgb(0.55, 0.54, 0.51), wear=0.6)
    for y in (100.0, 112.0):
        track(b, (-150.0, y), (150.0, y), ballast=False)
    for x in (-60.0, 50.0):
        gantry_crane(b, (x, 116.0, 0.25), (0, 0, 90), span=44.0, height=26.0, length=14.0, tint=STEEL_Y)
    tints = [srgb(0.55, 0.30, 0.20), srgb(0.20, 0.33, 0.55), srgb(0.30, 0.42, 0.30), OFFWHITE,
             srgb(0.70, 0.55, 0.20), srgb(0.45, 0.45, 0.48)]
    k = 0
    for row in range(4):
        y = 124.0 + row * 3.0
        for col in range(14):
            x = -130.0 + col * 13.0
            h = (row * 7 + col * 3) % 4
            for lvl in range(h):
                b.piece("container-40", (x, y, 0.25 + lvl * 2.6), (0, 0, b.rng.uniform(-1.5, 1.5)),
                        tint=tints[k % len(tints)])
                k += 1
    rake(b, 100.0, -120.0, ["flatcar"] * 5, [srgb(0.40, 0.36, 0.30)])
    for k2, x in enumerate((-120.0 + 7.0, -120.0 + 21.9, -120.0 + 51.7)):
        b.piece("container-40", (x, 100.0, 2.06), (0, 0, 0), tint=tints[k2])
    for p in ((-100.0, 118.0), (20.0, 120.0), (110.0, 119.0)):
        b.marker("loot", (p[0], p[1], 0.3), table="crate")
    b.marker("guard", (-10.0, 118.0, 0.3), role="reclaimer")
    for x in (-140.0, 140.0):
        b.piece("flood-mast", (x, 92.0, 0.25), (0, 0, 180))


def goods_shed(b):
    """The goods shed with its loading platform and canopy along a siding, the yard office and the strongroom."""
    track(b, (-170.0, -84.2), (-20.0, -84.2))
    track(b, (-20.0, -84.2), (10.0, -68.0))
    P.hall(b, (-95.0, -104.0, 0), (0, 0, 0), 120.0, 22.0, 9.0, bay=8.0, plinth=1.5, roof="gable", pitch=14.0,
           clad_tint=IND_GREY, roof_tint=srgb(0.50, 0.50, 0.48), frame_tint=srgb(0.36, 0.38, 0.40),
           band=STEEL_Y, ruin=0.15, windows=(), skylights=True,
           doors={"back": [(x, 6.0, 5.0, "shutter") for x in (-45.0, -15.0, 15.0, 45.0)],
                  "left": [(0.0, 1.4, 2.3, "door")]})
    b.box((120.0, 6.0, 1.2), (-95.0, -89.0, 0.6), mat=CONCRETE, tint=CONCRETE_T)
    stripes(b, (-155.0, -86.0, 0), (-35.0, -86.0, 0), 0.25, width=1.2, at_z=1.2, normal=(0, 1, 0))
    for k in range(9):
        x = -150.0 + k * 13.75
        b.box((0.3, 0.3, 4.2), (x, -86.6, 3.3), mat=PAINT, tint=srgb(0.36, 0.38, 0.40), vary=0)
    b.box((121.0, 6.6, 0.15), (-95.0, -89.0, 5.45), (4.0, 0, 0), mat=CORRUGATED, tint=srgb(0.5, 0.5, 0.48))
    stairs(b, (-157.5, -91.0, 0.0), (0, 0, -90), 2.0, 1.2, 1.8, tint=YELLOW)
    for x in (-130.0, -80.0, -40.0):
        b.piece("lamp-post", (x, -86.2, 1.2), (0, 0, 180))
    for p, t in (((-120.0, -104.0), "crate"), ((-80.0, -108.0), "crate"), ((-60.0, -100.0), "barrel")):
        b.marker("loot", (p[0], p[1], 0.4), table=t)
    P.crates(b, (-100.0, -100.0, 0.3), (0, 0, 0), n=6, tint=srgb(0.45, 0.40, 0.30))
    P.crates(b, (-140.0, -108.0, 0.3), (0, 0, 10), n=5, tint=srgb(0.36, 0.40, 0.42))
    b.marker("guard", (-90.0, -95.0, 1.3), role="reclaimer")
    # The yard office and its strongroom (the cyan room).
    ox, oy = 0.0, -112.0
    P.room(b, (ox, oy, 0), (0, 0, 0), 26.0, 14.0, 4.0, mat=FORMWORK, tint=CONCRETE_T,
           openings={"front": [(-8.0, 1.4, 2.3, 0.0), (2.0, 2.0, 1.2, 1.0), (7.0, 2.0, 1.2, 1.0)],
                     "back": [(-3.0, 2.0, 1.2, 1.0), (3.0, 2.0, 1.2, 1.0)]})
    b.marker("door", (ox - 8.0, oy - 7.6, 0), kind_of="building")
    P.room(b, (ox + 7.0, oy + 1.0, 0.06), (0, 0, 0), 9.0, 8.0, 3.2, thick=0.5, mat=CONCRETE, tint=srgb(0.6, 0.6, 0.57),
           openings={"left": [(0.0, 1.4, 2.3, 0.0)]}, floor=False)
    P.blast_door(b, (ox + 2.1, oy + 1.0, 0.06), (0, 0, -90), w=1.4, h=2.3, depth=0.8, frame=0.5, tint=TEAL,
                 frame_tint=srgb(0.6, 0.6, 0.57), chip="cyan", cell=True, kind="strongroom-door")
    b.marker("loot", (ox + 9.0, oy + 3.0, 0.12), table="military-crate")
    b.marker("loot", (ox + 9.0, oy - 1.5, 0.12), table="military-crate")
    b.marker("loot", (ox + 5.5, oy + 3.5, 0.12), table="crate")
    for x in (-6.0, 6.0):
        b.box((0.6, 0.6, 2.6), (ox + x, oy - 10.0, 1.3), mat=CONCRETE, tint=CONCRETE_T)
        b.box((1.0, 1.0, 0.6), (ox + x, oy - 10.0, 2.9), mat=PAINT, tint=DARK, bevel=0.05)
        b.marker("guard", (ox + x, oy - 10.0, 3.2), role="warden-turret")
    for k in range(3):
        b.box((2.2, 0.8, 0.8), (ox - 9.0 + 3.0 * k, oy + 4.0, 0.46), mat=PAINT, tint=IND_GREY, bevel=0.03)


def signal_tower(b):
    """The signal tower: a four-storey shaft, a glazed cabin on top with its levers, an outside stair."""
    sx, sy = 60.0, -98.0
    P.room(b, (sx, sy, 0), (0, 0, 0), 9.0, 9.0, 20.0, thick=0.5, mat=FORMWORK, tint=CONCRETE_T,
           openings={"front": [(0.0, 1.3, 2.3, 0.0)], "left": [(0.0, 1.0, 1.2, 8.0)], "back": [(0.0, 1.0, 1.2, 14.0)]})
    b.marker("door", (sx, sy - 5.0, 0), kind_of="building")
    P.room(b, (sx, sy, 20.5), (0, 0, 0), 9.0, 9.0, 3.4, thick=0.25, mat=PAINT, tint=IND_GREY,
           openings={"front": [(0.0, 7.6, 1.8, 1.0)], "back": [(0.0, 7.6, 1.8, 1.0)], "left": [(0.0, 7.6, 1.8, 1.0)],
                     "right": [(0.0, 1.2, 2.3, 0.0), (2.8, 2.4, 1.8, 1.0)]}, roof=False)
    b.box((11.0, 11.0, 0.4), (sx, sy, 24.1), mat=PAINT, tint=srgb(0.36, 0.38, 0.40))
    for (x, y, w, d) in ((0.0, -4.45, 7.6, 0.05), (0.0, 4.45, 7.6, 0.05), (-4.45, 0.0, 0.05, 7.6)):
        if b.rng.random() > 0.25:
            b.box((w, d, 1.8), (sx + x, sy + y, 22.4), mat=GLASS, solid=False)
    for k in range(10):
        b.box((0.12, 0.12, 1.2), (sx - 3.0 + k * 0.65, sy + 2.5, 21.1), (12, 0, 0), mat=PAINT,
              tint=RED if k % 3 == 0 else LIGHTGREY, solid=False)
    b.box((7.0, 0.8, 0.9), (sx, sy + 3.0, 20.95), mat=PAINT, tint=srgb(0.36, 0.38, 0.40))
    b.marker("loot", (sx - 2.0, sy - 2.0, 20.6), table="crate")
    b.marker("guard", (sx + 1.0, sy - 1.0, 20.6), role="reclaimer")
    P.stair_tower(b, (sx + 6.2, sy, 0.0), (0, 0, -90), [0.0, 5.0, 10.0, 15.0, 20.5], width=1.1)
    b.cyl(0.1, 8.0, (sx - 3.5, sy + 3.5, 28.3), mat=PAINT, tint=GREY, sides=6, solid=False)
    P.beacon(b, (sx - 3.5, sy + 3.5, 32.3), 0.35)
    P.stencil(b, "Y4", (sx - 2.6, sy - 4.53, 14.0), (0, 0, 0), 3.2, tint=BLACK)
    b.marker("landmark", (sx, sy, 32.0), kind_of="signal-tower")


def engine_shed(b):
    """The engine shed at the west end: three roads through tall doors, pits, a locomotive under repair, the
    recycler, and the turntable outside."""
    ex, ey = -310.0, -100.0
    P.hall(b, (ex, ey, 0), (0, 0, 0), 70.0, 36.0, 13.0, bay=7.0, plinth=2.5, roof="gable", pitch=12.0,
           clad_tint=srgb(0.55, 0.52, 0.46), roof_tint=srgb(0.45, 0.44, 0.42), frame_tint=srgb(0.30, 0.30, 0.30),
           band=STEEL_Y, ruin=0.22, windows=("front", "back"), win_h=2.0,
           doors={"right": [(y, 5.6, 7.5, "open") for y in (-11.0, 0.0, 11.0)], "front": [(-20.0, 1.4, 2.3, "door")]})
    for y in (-11.0, 0.0, 11.0):
        track(b, (ex - 32.0, ey + y), (ex + 35.0, ey + y), ballast=False)
        b.box((40.0, 1.2, 0.05), (ex - 5.0, ey + y, 0.31), mat=MATTE, tint=srgb(0.05, 0.05, 0.05), solid=False)
        track(b, (ex + 35.0, ey + y), (-230.0 - 13.0 * math.cos(math.atan2(y, 50.0)), ey + y * 0.6), ballast=True)
    b.piece("locomotive", (ex - 4.0, ey, 0.0), (0, 0, 0))
    b.marker("recycler", (ex - 25.0, ey + 6.0, 0.35))
    b.box((2.6, 1.8, 2.2), (ex - 25.0, ey + 8.0, 1.4), mat=PAINT, tint=srgb(0.30, 0.45, 0.40), bevel=0.06)
    for x in (-28.0, -14.0):
        b.box((3.0, 1.0, 0.9), (ex + x, ey - 16.5, 0.75), mat=PAINT, tint=srgb(0.36, 0.38, 0.40), bevel=0.03)
    b.piece("drum-stack", (ex + 20.0, ey - 15.0, 0.3), (0, 0, 10))
    for p, t in (((ex + 22.0, ey - 12.0), "fuel-barrel"), ((ex - 20.0, ey - 14.0), "crate"),
                 ((ex + 10.0, ey + 15.0), "barrel")):
        b.marker("loot", (p[0], p[1], 0.35), table=t)
    b.marker("guard", (ex + 5.0, ey - 6.0, 0.35), role="reclaimer")
    # The turntable: a ring wall round a shallow pit floor, the bridge truss with its rails, a hut.
    tx, ty = -230.0, -100.0
    b.cyl(13.6, 0.12, (tx, ty, 0.06), mat=CONCRETE, tint=srgb(0.40, 0.39, 0.36), sides=40)
    P.arc_wall(b, (tx, ty), 14.2, 0.0, 360.0, 0.0, 0.7, thick=0.6, piece_deg=20.0, mat=CONCRETE, tint=CONCRETE_T)
    b.tube([(tx + math.cos(a) * 12.6, ty + math.sin(a) * 12.6, 0.2) for a in
            [2 * math.pi * k / 32 for k in range(33)]], 0.08, mat=RUST, tint=(1, 1, 1), sides=6, cap=False)
    with b.frame((tx, ty, 0.0), (0, 0, 24.0)):
        P.truss(b, (-13.2, 0, 0.25), (13.2, 0, 0.25), w=3.0, h=1.4, chord=0.25, web=0.12, tint=STEEL_Y)
        for x in (-10.0, 0.0, 10.0):
            b.piece("track-10", (x, 0, 0.0), (0, 0, 0))
        b.box((2.4, 2.0, 2.4), (13.0, 2.6, 1.5), mat=PAINT, tint=IND_GREY, bevel=0.05)
    b.cyl(1.2, 0.8, (tx, ty, 0.5), mat=PAINT, tint=DARK, sides=12)
    # Fuelling point by the lead: two tanks and a pump gantry.
    for k in range(2):
        P.bullet_tank(b, (-250.0, -30.0 + k * 9.0, 0.3), (0, 0, 0), 2.4, 16.0, tint=srgb(0.70, 0.30, 0.18))
    b.box((0.4, 0.4, 6.0), (-235.0, -14.0, 3.3), mat=PAINT, tint=STEEL_Y)
    b.box((6.0, 0.4, 0.4), (-232.0, -14.0, 6.2), mat=PAINT, tint=STEEL_Y, solid=False)
    b.tube([(-229.5, -14.0, 6.2), (-229.5, -12.0, 4.0), (-229.5, -9.0, 3.0)], 0.12, mat=PAINT, tint=BLACK, sides=6)


def silos(b):
    """The grain silos at the east end, their headhouse gallery and the loading shed over a siding."""
    track(b, (180.0, -75.0), (345.0, -75.0))
    silo(b, (292.0, -104.0, 0.0), radius=5.5, height=24.0, cone=5.0, legs=6.0, tint=srgb(0.80, 0.79, 0.74), count=5,
         spacing=12.0)
    b.box((60.0, 9.0, 6.0), (292.0, -104.0, 6.0 + 5.0 + 24.0 + 1.5 + 3.0), mat=CORRUGATED, tint=IND_GREY)
    P.stencil(b, "G7", (304.0, -109.62, 20.0), (0, 0, 0), 6.0, tint=srgb(0.36, 0.38, 0.40), wrap=5.62)
    P.hall(b, (292.0, -75.0, 0), (0, 0, 0), 44.0, 10.0, 10.0, bay=7.3, plinth=0.0, roof="flat", clad_tint=IND_GREY,
           frame_tint=srgb(0.36, 0.38, 0.40), ruin=0.2, windows=(), skylights=False, open_sides=("left", "right"),
           parapet=0.0)
    conveyor(b, (292.0, -99.0, 39.0), (292.0, -80.0, 13.0), width=1.8, tint=IND_GREY)
    rake(b, -75.0, 250.0, ["hopper-wagon", "hopper-wagon"], [srgb(0.55, 0.32, 0.22)])
    for p, t in (((270.0, -90.0), "barrel"), ((310.0, -92.0), "crate")):
        b.marker("loot", (p[0], p[1], 0.3), table=t)
    b.marker("landmark", (292.0, -104.0, 46.0), kind_of="grain-silos")
    P.beacon(b, (318.0, -104.0, 44.0), 0.4)


def works(b):
    """Roads and the level crossings, the yard fence, junk, the approach."""
    road(b, [(-20.0, -175.0, 0), (-20.0, -128.0, 0), (60.0, -128.0, 0), (330.0, -128.0, 0)], width=8.0)
    road(b, [(330.0, -128.0, 0), (330.0, 140.0, 0)], width=8.0)
    road(b, [(-20.0, -128.0, 0), (-200.0, -128.0, 0), (-250.0, -70.0, 0)], width=7.0)
    for y in (-5.0, 70.0, 75.0):
        b.box((10.0, 4.0, 0.12), (330.0, y, 0.38), mat=ASPHALT, tint=(1, 1, 1), solid=False)
    for y, s in ((-12.0, -1), (84.0, 1)):
        b.box((0.3, 0.3, 1.2), (325.0, y, 0.6), mat=PAINT, tint=DARK)
        b.box((7.0, 0.15, 0.15), (328.5, y, 1.1), (0, 0, 0), mat=PAINT, tint=RED, solid=False)
        b.box((0.08, 0.6, 0.6), (325.0, y + s * 0.4, 2.2), (45, 0, 0), mat=PAINT, tint=OFFWHITE, solid=False)
    fence(b, [(-345.0, -146.0, 0), (-28.0, -146.0, 0)], height=2.4, post=5.0)
    fence(b, [(-12.0, -146.0, 0), (345.0, -146.0, 0)], height=2.4, post=5.0)
    for x in (-28.0, -12.0):
        b.box((0.4, 0.4, 3.0), (x, -146.0, 1.5), mat=PAINT, tint=GREY, bevel=0.02)
    for k in range(6):
        x = -60.0 + k * 1.2
        b.box((10.0, 0.15, 0.17), (x - 100.0, -136.0, 0.1 + 0.17 * (k % 3)), (0, 0, 0), mat=RUST, tint=(1, 1, 1))
    for k in range(4):
        b.box((2.6, 2.6, 0.3), (-120.0 + k * 3.0, -138.0, 0.15 + 0.3 * (k % 2)), mat=CONCRETE, tint=CONCRETE_T)
    P.scrap(b, (230.0, -60.0), 12.0, count=12)
    P.scrap(b, (-190.0, -20.0), 10.0, count=8)
    b.piece("wreck-truck", (40.0, -140.0, 0), (0, 0, 10))
    b.piece("wreck-car", (-35.0, -150.0, 0), (0, 0, 80))
    b.marker("spawn", (-20.0, -190.0, 0), kind_of="approach")
    b.marker("guard", (200.0, -30.0, 0.3), role="reclaimer")


def build(b):
    b.set_pad(("rect", 700.0, 300.0), ground="gravel", blend=35.0, clear=12.0)
    yard(b)
    terminal(b)
    goods_shed(b)
    signal_tower(b)
    engine_shed(b)
    silos(b)
    works(b)
    b.zone("no-build", (0, 0, 0), size=(780.0, 380.0, 200.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "rail", "visible_km": 12})


SUN = (27.0, 215.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((-8.0, -45.0, 0.32), 270.0), ((10.0, -44.0, 0.32), 80.0)]
VIEWS = [
    {"name": "far", "target": (0.0, 0.0, 18.0), "distance": 2600, "yaw": 30, "pitch": -0.35, "lens": 100},
    {"name": "overview", "target": (0.0, -10.0, 5.0), "distance": 760, "yaw": 22, "pitch": 30, "lens": 30},
    {"name": "tracks", "target": (60.0, -40.0, 9.2), "distance": 80.6, "yaw": -85.7, "pitch": -5.2, "lens": 22},
]
