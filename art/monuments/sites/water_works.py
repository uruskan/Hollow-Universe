"""Water Works (MONUMENTS.md §5.4, large tier, catalogue 41): a Seedward water treatment plant on a river, 400 x 300 m,
in the civil livery. Water comes in at the intake and its pump house on the river bank (north), runs through the
screen channel to the round clarifiers with their slowly turning bridges, on through rows of rectangular settling
basins crossed by catwalks, into the long filter hall, and out through the clear-water tanks and the high-lift pump
house to the town main. A tall water tower on its concrete shaft is the landmark; the chemical store, a sludge
thickener, the control building (with the cyan room), the workshop with the recycler, and the outfall complete it.

Gameplay: the control building's records vault is the cyan room (cyan chip and a power cell); a recycler in the
workshop; crates in the filter hall gallery and the pump houses; Reclaimers on the basin catwalks and at the gate;
chlorine at the chemical store as a hazard.
"""
import math

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, cable, fence,
                        ladder, lamp, railing, rubble, srgb, stairs, stripes, tank)
from monkit.structures import road, shed
from monkit.surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL,
                             RUST)

KEY = "water-works"
TITLE = "Water Works"
SEED = 4101
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [300.0, 1400.0, 20000.0]

CIVIL = srgb(0.86, 0.86, 0.83)
CIVIL_DARK = srgb(0.70, 0.71, 0.70)
WATER = srgb(0.18, 0.26, 0.26)
GREEN_WATER = srgb(0.20, 0.30, 0.18)
SCUM = srgb(0.30, 0.36, 0.22)
WALL_H = 3.6


def basin(b, x0, y0, x1, y1, depth=WALL_H, water=WATER, rails=True, walls=0.5, algae=True):
    """A rectangular basin with concrete walls standing `depth` above the ground, water a little below the rim,
    scum and algae on it, rails along the walls."""
    t = walls
    for (ax, ay), (cx, cy) in (((x0, y0), (x1, y0)), ((x0, y1), (x1, y1))):
        P.wall_run(b, (ax, ay), (cx, cy), depth, t, mat=CONCRETE, tint=CONCRETE_T)
    for x in (x0, x1):
        P.wall_run(b, (x, y0 + t / 2), (x, y1 - t / 2), depth, t, mat=CONCRETE, tint=CONCRETE_T)
    b.box((x1 - x0 - t, y1 - y0 - t, 0.1), ((x0 + x1) / 2, (y0 + y1) / 2, depth - 0.6), mat=GLOSS, tint=water,
          solid=False)
    if algae:
        for k in range(3):
            w = (x1 - x0) * (0.2 + 0.3 * b.rng.random())
            h = (y1 - y0) * (0.15 + 0.25 * b.rng.random())
            cx = b.rng.uniform(x0 + w / 2 + 1, x1 - w / 2 - 1)
            cy = b.rng.uniform(y0 + h / 2 + 1, y1 - h / 2 - 1)
            b.box((w, h, 0.04), (cx, cy, depth - 0.52), mat=MATTE, tint=SCUM, solid=False, vary=0.1)
    b.box((x1 - x0 + 0.4, y1 - y0 + 0.4, 0.15), ((x0 + x1) / 2, (y0 + y1) / 2, 0.075), mat=CONCRETE,
          tint=srgb(0.55, 0.55, 0.52), solid=False)
    if rails:
        for (ax, ay), (cx, cy) in (((x0, y0), (x1, y0)), ((x0, y1), (x1, y1)), ((x0, y0), (x0, y1)),
                                   ((x1, y0), (x1, y1))):
            railing(b, (ax, ay, depth), (cx, cy, depth), spacing=2.4, toe=False, tint=TEAL)


def clarifier(b, cx, cy, r=18.0, broken=False):
    """A round clarifier: a ring wall, the water, a central drive column, a bridge from the rim to the centre with
    its scraper arms, a walkway with rails along it."""
    P.arc_wall(b, (cx, cy), r, 0.0, 360.0, 0.0, WALL_H, thick=0.5, piece_deg=15.0, mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(r - 0.5, 0.1, (cx, cy, WALL_H - 0.6), mat=GLOSS, tint=WATER, sides=40, solid=False)
    b.cyl(r * 0.25, 0.04, (cx, cy, WALL_H - 0.53), mat=MATTE, tint=SCUM, sides=20, solid=False)
    b.cyl(1.6, WALL_H + 1.4, (cx, cy, (WALL_H + 1.4) / 2), mat=CONCRETE, tint=CONCRETE_T, sides=16)
    b.cyl(2.2, 1.6, (cx, cy, WALL_H + 2.2), mat=PAINT, tint=CIVIL_DARK, sides=16)
    yaw = b.rng.uniform(0, 360)
    with b.frame((cx, cy, WALL_H + 1.4), (0, 0, yaw)):
        P.truss(b, (0.0, 0.0, 0.0), (r + 0.4, 0.0, 0.0 - 1.4 + 0.25), w=2.0, h=1.2, chord=0.18, web=0.1,
                tint=srgb(0.36, 0.38, 0.40))
        b.box((r + 0.6, 1.4, 0.06), ((r + 0.6) / 2, 0, 1.25 - 0.7), (0, math.degrees(math.atan2(1.15, r)), 0),
              mat=GRATE, tint=GREY, solid=False)
        for k in range(3):
            x = r * (0.3 + 0.25 * k)
            b.box((0.2, 0.2, 2.6), (x, 0.0, -2.2), mat=PAINT, tint=DARK, solid=False)
        b.box((r * 0.85, 0.3, 0.5), (r * 0.5, 0.0, -3.4), (0, 2.0, 0), mat=RUST, tint=(1, 1, 1), solid=False)
        b.box((2.0, 1.6, 1.4), (r - 0.4, 0.0, 0.0), mat=PAINT, tint=CIVIL_DARK, bevel=0.05)


def pump_house(b, at, yaw, w, d, h, label, doors=None):
    """A civil pump house: panelled walls on a plinth, a teal band, louvres, a door, pumps and pipes inside."""
    P.hall(b, at, (0, 0, yaw), w, d, h, bay=6.0, plinth=1.6, clad=PANEL, clad_tint=CIVIL, roof_tint=LIGHTGREY,
           frame_tint=srgb(0.36, 0.38, 0.40), band=TEAL, ruin=0.08, windows=("front", "back"), win_h=1.6,
           skylights=False, doors=doors or {"front": [(-w / 4, 1.4, 2.3, "door"), (w / 4, 4.0, 4.0, "shutter")]})
    with b.frame(at, (0, 0, yaw)):
        for k in range(max(2, int(w / 7))):
            x = -w / 2 + 4.0 + k * 6.0
            if x > w / 2 - 3.0:
                break
            b.box((2.4, 1.6, 1.4), (x, d / 4, 1.0), mat=PAINT, tint=TEAL, bevel=0.06)
            b.cyl(0.55, 2.2, (x, d / 4, 2.3), mat=PAINT, tint=CIVIL_DARK, sides=14)
            b.tube([(x, d / 4 - 0.8, 1.0), (x, -d / 4, 1.0), (x, -d / 4, 0.3)], 0.3, mat=PAINT, tint=GREY, sides=10)
        P.stencil(b, label, (-w / 2 + 1.5, -d / 2 - 0.42, h - 2.6), (0, 0, 0), 1.8, tint=TEAL)
        b.marker("loot", (w / 2 - 3.0, d / 4, 0.4), table="crate")


def build(b):
    b.set_pad(("rect", 400.0, 300.0), ground="gravel", blend=35.0, clear=12.0)
    # The river bank along the north edge: a quay wall, the intake with its screens, the outfall.
    b.box((400.0, 3.0, 2.4), (0.0, 148.0, 1.2), mat=FORMWORK, tint=CONCRETE_T)
    b.box((400.0, 6.0, 0.05), (0.0, 153.0, -0.4), mat=GLOSS, tint=srgb(0.20, 0.28, 0.30), solid=False)
    for k in range(6):
        x = -60.0 + k * 9.0
        b.box((7.0, 3.6, 5.0), (x, 147.0, 2.5), mat=CONCRETE, tint=CONCRETE_T)
        b.box((6.0, 0.2, 4.0), (x, 149.0, 2.4), mat=GRATE, tint=RUSTY, solid=False)
        b.box((1.2, 1.2, 1.4), (x, 146.0, 5.7), mat=PAINT, tint=TEAL, bevel=0.05)
    pump_house(b, (-35.0, 128.0, 0), 0.0, 44.0, 18.0, 11.0, "P1")
    # The screen channel and its bridge from the intake to the clarifiers.
    basin(b, -60.0, 92.0, -12.0, 104.0, depth=2.8, water=GREEN_WATER, algae=False)
    for k in range(4):
        b.box((0.2, 11.0, 3.0), (-50.0 + k * 10.0, 98.0, 1.6), mat=GRATE, tint=GREY, solid=False)
    # Four clarifiers in the north-east quarter (one with its bridge fallen in).
    for k, (cx, cy) in enumerate(((40.0, 110.0), (90.0, 110.0), (40.0, 62.0), (90.0, 62.0))):
        clarifier(b, cx, cy, 20.0)
    # Settling basins in two rows, crossed by catwalks.
    xs = [-170.0 + k * 34.0 for k in range(6)]
    for row, (y0, y1) in enumerate(((20.0, 70.0), (-46.0, 4.0))):
        for k, x in enumerate(xs):
            water = GREEN_WATER if (k + row) % 3 == 0 else WATER
            basin(b, x, y0, x + 30.0, y1, water=water, rails=False)
            if (k + row) % 4 == 1:
                # A drained basin: dry, a sludge floor, a ladder down.
                b.box((29.0, y1 - y0 - 1.0, 0.25), (x + 15.0, (y0 + y1) / 2, 0.4), mat=MATTE,
                      tint=srgb(0.30, 0.27, 0.22), solid=False)
                ladder(b, (x + 2.0, y0 + 0.6, 0.15), WALL_H, (0, 0, 180), cage=False, tint=TEAL)
        P.deck(b, -172.0, (y0 + y1) / 2 - 0.8, 34.0, (y0 + y1) / 2 + 0.8, WALL_H + 0.1, mat=GRATE, rails="sn",
               rail_tint=TEAL, edge=False)
        for x in xs:
            b.box((0.5, 0.5, WALL_H), (x + 15.0, (y0 + y1) / 2, WALL_H / 2), mat=CONCRETE, tint=CONCRETE_T)
        stairs(b, (-177.2, (y0 + y1) / 2, 0.0), (0, 0, -90), 1.4, WALL_H + 0.1, 5.2, tint=TEAL)
        # Lifebuoys on posts along the catwalk, plates on the walls, valve wheels at each basin's outlet.
        for k, x in enumerate(xs):
            yy = (y0 + y1) / 2 + (0.9 if k % 2 else -0.9)
            b.tube([(x + 6.0 + math.cos(a) * 0.35, yy, WALL_H + 0.85 + math.sin(a) * 0.35) for a in
                    [2 * math.pi * j / 10 for j in range(11)]], 0.07, mat=PAINT, tint=ORANGE, sides=6, cap=False)
            P.plate(b, (x + 24.0, y0 - 0.27, 2.2), (0, 0, 0), 1.0, 0.5, tint=OFFWHITE, mark=TEAL,
                    text="S" + str(k + 1 + 6 * row))
            b.cyl(0.35, 0.05, (x + 28.0, y0 - 0.6, 1.1), (90, 0, 0), mat=PAINT, tint=RED, sides=10, solid=False)
            b.cyl(0.06, 0.6, (x + 28.0, y0 - 0.35, 1.1), (90, 0, 0), mat=PAINT, tint=GREY, sides=6, solid=False)
        b.marker("guard", (-80.0, (y0 + y1) / 2, WALL_H + 0.2), role="reclaimer")
        b.marker("loot", (-130.0, (y0 + y1) / 2, WALL_H + 0.2), table="barrel")
    # The filter hall: a long hall over twelve filter beds, a gallery along the beds, the pipe gallery below.
    fx, fy = 0.0, -88.0
    P.hall(b, (fx, fy, 0), (0, 0, 0), 120.0, 30.0, 12.0, bay=7.5, plinth=3.0, clad=PANEL, clad_tint=CIVIL,
           roof_tint=LIGHTGREY, frame_tint=srgb(0.36, 0.38, 0.40), band=TEAL, ruin=0.12, windows=("front", "back"),
           win_h=2.4, doors={"front": [(-40.0, 1.4, 2.3, "door"), (30.0, 6.0, 5.0, "shutter")],
                             "left": [(0.0, 1.4, 2.3, "door")], "right": [(4.0, 1.4, 2.3, "door")]})
    for k in range(12):
        x = fx - 55.0 + k * 10.0
        basin(b, x - 4.6, fy - 2.0, x + 4.6, fy + 12.0, depth=1.8, water=WATER if k % 3 else GREEN_WATER,
              rails=False, walls=0.35, algae=False)
        b.box((8.6, 13.0, 0.3), (x, fy + 5.0, 0.7), mat=MATTE, tint=srgb(0.66, 0.60, 0.46), solid=False)
    P.deck(b, fx - 58.0, fy - 6.0, fx + 58.0, fy - 2.2, 1.8, mat=GRATE, rails="n", rail_tint=TEAL, edge=False)
    for k in range(6):
        b.box((2.0, 1.0, 1.2), (fx - 50.0 + k * 20.0, fy - 7.5, 0.9), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
    P.stencil(b, "F3", (fx + 40.0, fy - 15.42, 8.0), (0, 0, 0), 3.6, tint=TEAL)
    for p, t in (((fx - 30.0, fy - 5.0, 1.9), "crate"), ((fx + 20.0, fy - 5.0, 1.9), "crate"),
                 ((fx + 50.0, fy + 13.0, 0.4), "barrel")):
        b.marker("loot", p, table=t)
    b.marker("guard", (fx, fy - 5.0, 1.9), role="reclaimer")
    # Clear-water tanks: two low round reservoirs with domed roofs, a valve house.
    for k, x in enumerate((95.0, 140.0)):
        P.arc_wall(b, (x, -40.0), 18.0, 0.0, 360.0, 0.0, 6.0, thick=0.6, piece_deg=15.0, mat=CONCRETE, tint=CIVIL)
        b.lathe([(18.2, 0.0), (17.0, 1.2), (12.0, 2.8), (0.0, 4.0)], (x, -40.0, 6.0), mat=CONCRETE, tint=CIVIL_DARK,
                sides=36)
        b.cyl(18.25, 0.8, (x, -40.0, 5.2), mat=PAINT, tint=TEAL, sides=40, cap=False, solid=False, vary=0)
        b.cyl(1.0, 1.0, (x, -40.0, 10.4), mat=PAINT, tint=GREY, sides=10)
        ladder(b, (x, -58.4, 0.0), 6.0, (0, 0, 0), cage=True, tint=TEAL)
    # The high-lift pump house and the main to town.
    pump_house(b, (150.0, -105.0, 0), 0.0, 36.0, 16.0, 10.0, "P2")
    b.tube([(150.0, -114.0, 1.2), (150.0, -150.0, 1.2)], 1.0, mat=PAINT, tint=CIVIL_DARK, sides=16)
    # The water tower: a fluted concrete shaft 50 m tall, a spherical tank, a gallery, a stair inside the shaft.
    tx, ty = 175.0, 55.0
    b.cyl(5.0, 1.2, (tx, ty, 0.6), mat=CONCRETE, tint=CONCRETE_T, sides=24)
    b.cyl(3.6, 52.0, (tx, ty, 26.0), mat=FORMWORK, tint=CIVIL, sides=24, radius2=3.0)
    for k in range(12):
        a = 2 * math.pi * k / 12
        b.box((0.5, 0.6, 50.0), (tx + math.cos(a) * 3.4, ty + math.sin(a) * 3.4, 26.0), (0, 0, math.degrees(a)),
              mat=CONCRETE, tint=CIVIL_DARK, solid=False)
    prof = [(3.0, -2.0)]
    for k in range(1, 12):
        t = math.pi * k / 12
        prof.append((11.0 * math.sin(t), -11.0 * math.cos(t) + 9.0))
    prof.append((0.0, 20.0))
    b.lathe(prof, (tx, ty, 52.0), mat=PANEL, tint=OFFWHITE, sides=40)
    b.cyl(11.05, 2.6, (tx, ty, 61.0), mat=PAINT, tint=TEAL, sides=40, cap=False, solid=False, vary=0)
    b.cyl(11.05, 0.9, (tx, ty, 63.8), mat=PAINT, tint=ORANGE, sides=40, cap=False, solid=False, vary=0)
    P.stencil(b, "W2", (tx, ty - 11.06, 57.0), (0, 0, 0), 3.4, tint=srgb(0.30, 0.34, 0.36), wrap=11.06)
    b.cyl(12.6, 0.25, (tx, ty, 61.0 - 1.4), mat=GRATE, tint=GREY, sides=40, solid=False)
    pts = [(tx + math.cos(a) * 12.4, ty + math.sin(a) * 12.4, 61.0 - 1.27) for a in
           [2 * math.pi * k / 28 for k in range(29)]]
    for pa, pb in zip(pts, pts[1:]):
        railing(b, pa, pb, spacing=2.5, toe=False, tint=TEAL)
    b.marker("door", (tx, ty - 4.2, 0), kind_of="building")
    ladder(b, (tx, ty - 3.9, 1.2), 58.4, (0, 0, 0), cage=True, tint=TEAL)
    b.marker("loot", (tx + 8.0, ty, 59.9), table="military-crate")
    P.beacon(b, (tx, ty, 72.0), 0.5)
    b.marker("landmark", (tx, ty, 72.0), kind_of="water-tower", visible_km=12)
    # Control building with the records vault (the cyan room), a radio mast on its roof.
    cx, cy = -120.0, -100.0
    for k in range(2):
        P.room(b, (cx, cy, k * 3.8), (0, 0, 0), 30.0, 16.0, 3.8, thick=0.3, mat=PANEL, tint=CIVIL,
               openings={"front": ([(-10.0, 1.4, 2.3, 0.0)] if k == 0 else []) + [(x, 2.0, 1.4, 1.0)
                                                                              for x in (-4, 0, 4, 8, 12)],
                         "back": [(x, 2.0, 1.4, 1.0) for x in (-10, -4, 4, 10)],
                         "right": [(0.0, 1.2, 2.3, 0.0)] if k == 1 else []},
               roof=(k == 1), roof_mat=CONCRETE, roof_tint=srgb(0.6, 0.6, 0.58))
        b.box((30.4, 0.2, 0.5), (cx, cy - 8.1, k * 3.8 + 3.5), mat=PAINT, tint=TEAL, solid=False, vary=0)
    stairs(b, (cx + 16.4, cy - 4.2, 0.0), (0, 0, 0), 1.2, 3.8, 5.4, tint=TEAL)
    P.deck(b, cx + 15.6, cy + 1.2, cx + 17.2, cy + 3.0, 3.8, mat=GRATE, rails="ne", edge=False)
    b.marker("door", (cx - 10.0, cy - 8.6, 0), kind_of="building")
    P.room(b, (cx - 9.0, cy + 2.5, 0.06), (0, 0, 0), 9.0, 8.0, 3.2, thick=0.5, mat=CONCRETE, tint=srgb(0.6, 0.6, 0.57),
           openings={"right": [(0.0, 1.4, 2.3, 0.0)]}, floor=False)
    P.blast_door(b, (cx - 4.1, cy + 2.5, 0.06), (0, 0, 90), w=1.4, h=2.3, depth=0.8, frame=0.5, tint=TEAL,
                 frame_tint=srgb(0.6, 0.6, 0.57), chip="cyan", cell=True, kind="records-vault")
    b.marker("loot", (cx - 11.0, cy + 4.5, 0.12), table="military-crate")
    b.marker("loot", (cx - 11.0, cy + 0.5, 0.12), table="crate")
    for k in range(3):
        b.box((2.4, 0.9, 1.0), (cx + 2.0 + 3.4 * k, cy + 5.0, 0.5), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
    from monkit.kit import lattice_tower
    lattice_tower(b, (cx + 8.0, cy + 3.0, 7.9), 18.0, 2.0, 0.8, sections=4, member=0.1, tint=GREY)
    P.beacon(b, (cx + 8.0, cy + 3.0, 26.0), 0.35)
    b.marker("guard", (cx, cy - 14.0, 0.1), role="reclaimer")
    # Chemical store with chlorine drums, a bunded tank yard; the sludge thickener; the workshop (recycler).
    P.hall(b, (-150.0, 110.0, 0), (0, 0, 0), 30.0, 16.0, 7.0, bay=7.5, plinth=1.2, clad_tint=srgb(0.80, 0.78, 0.60),
           frame_tint=DARK, band=YELLOW, ruin=0.2, windows=(), skylights=False,
           doors={"front": [(-6.0, 4.0, 4.0, "shutter"), (8.0, 1.4, 2.3, "door")]})
    for k in range(3):
        P.bullet_tank(b, (-150.0 + (k - 1) * 9.0, 85.0, 0.3), (0, 0, 90), 1.8, 10.0, tint=srgb(0.92, 0.86, 0.30))
    b.box((30.0, 16.0, 0.3), (-150.0, 85.0, 0.15), mat=CONCRETE, tint=srgb(0.55, 0.55, 0.52))
    for (ax, ay), (cx2, cy2) in (((-165.0, 77.0), (-135.0, 77.0)), ((-165.0, 93.0), (-135.0, 93.0)),
                                 ((-165.0, 77.0), (-165.0, 93.0)), ((-135.0, 77.0), (-135.0, 93.0))):
        P.wall_run(b, (ax, ay), (cx2, cy2), 1.0, 0.3, mat=CONCRETE, tint=CONCRETE_T)
    for k in range(3):
        P.stencil(b, "CL", (-160.0 + k * 9.0, 102.0 - 0.42, 3.0), (0, 0, 0), 1.6, tint=BLACK)
    b.zone("toxic", (-150.0, 95.0, 2.0), radius=22.0, kind_of="chlorine", damage=4)
    P.arc_wall(b, (-100.0, 115.0), 12.0, 0.0, 360.0, 0.0, 4.4, thick=0.5, piece_deg=20.0, mat=CONCRETE,
               tint=CONCRETE_T)
    b.cyl(11.5, 0.1, (-100.0, 115.0, 3.6), mat=MATTE, tint=srgb(0.26, 0.22, 0.17), sides=32, solid=False)
    with b.frame((-100.0, 115.0, 4.4), (0, 0, 30.0)):
        b.box((24.6, 1.6, 0.8), (0, 0, 0.4), mat=PAINT, tint=srgb(0.36, 0.38, 0.40))
        b.box((24.6, 1.4, 0.06), (0, 0, 0.85), mat=GRATE, tint=GREY, solid=False)
    b.cyl(1.4, 2.0, (-100.0, 115.0, 5.8), mat=PAINT, tint=CIVIL_DARK, sides=12)
    shed(b, (-165.0, -40.0, 0), (0, 0, 90), 26.0, 16.0, 7.0, tint=CIVIL, frame_tint=srgb(0.36, 0.38, 0.40),
         doors=[("front", 0.0, 6.0, 5.0)], ruin=0.15)
    b.marker("recycler", (-162.0, -36.0, 0.25))
    b.box((2.6, 1.8, 2.2), (-160.0, -36.0, 1.35), mat=PAINT, tint=srgb(0.30, 0.45, 0.40), bevel=0.06)
    b.piece("drum-stack", (-170.0, -48.0, 0.25), (0, 0, 10))
    b.marker("loot", (-168.0, -32.0, 0.25), table="crate")
    b.marker("loot", (-172.0, -46.0, 0.25), table="barrel")
    # Pipes on racks linking the stages.
    P.pipe_rack(b, [(-14.0, 98.0), (16.0, 98.0), (16.0, 80.0)], width=2.6, height=5.0, bent=7.0,
                pipes=[(0.6, CIVIL_DARK), (0.45, TEAL)], tray=False)
    P.pipe_rack(b, [(40.0, 38.0), (40.0, -10.0), (60.0, -30.0), (60.0, -70.0)], width=3.0, height=5.0, bent=8.0,
                pipes=[(0.6, CIVIL_DARK), (0.5, CIVIL_DARK), (0.3, TEAL)])
    P.pipe_rack(b, [(64.0, -88.0), (116.0, -88.0), (116.0, -60.0)], width=3.0, height=5.0, bent=8.0,
                pipes=[(0.6, CIVIL_DARK), (0.3, ORANGE)])
    # Roads, the gate, lamps, the fence, a substation corner, the approach.
    road(b, [(-10.0, -175.0, 0), (-10.0, -125.0, 0), (180.0, -125.0, 0)], width=8.0)
    road(b, [(-10.0, -125.0, 0), (-190.0, -125.0, 0), (-190.0, 90.0, 0), (-120.0, 140.0, 0), (60.0, 140.0, 0)],
         width=7.0)
    road(b, [(180.0, -125.0, 0), (190.0, 20.0, 0), (130.0, 140.0, 0)], width=7.0)
    fence(b, [(-4.0, -150.0, 0), (200.0, -150.0, 0), (200.0, 120.0, 0)], height=2.6, post=5.0)
    fence(b, [(-16.0, -150.0, 0), (-200.0, -150.0, 0), (-200.0, 140.0, 0)], height=2.6, post=5.0)
    for x in (-16.0, -4.0):
        b.box((0.4, 0.4, 3.0), (x, -150.0, 1.5), mat=PAINT, tint=GREY, bevel=0.02)
    P.room(b, (8.0, -144.0, 0), (0, 0, 0), 4.0, 4.0, 2.8, thick=0.25, mat=PANEL, tint=CIVIL,
           openings={"front": [(0.0, 3.0, 1.0, 1.0)], "left": [(0.5, 1.0, 2.2, 0.0)]})
    b.marker("guard", (2.0, -140.0, 0.1), role="reclaimer")
    b.marker("spawn", (-10.0, -180.0, 0), kind_of="approach")
    for x, y, yaw in ((-60.0, -120.0, 0), (60.0, -120.0, 0), (-190.0, 20.0, 90), (185.0, -30.0, -90),
                      (0.0, 135.0, 180), (-90.0, 75.0, 0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, yaw))
    for k in range(2):
        b.piece("transformer", (185.0 - k * 9.0, -140.0, 0), (0, 0, 0))
    b.piece("wreck-truck", (30.0, -132.0, 0), (0, 0, 175), tint=CIVIL)
    b.piece("wreck-car", (-60.0, -132.0, 0), (0, 0, 8))
    P.scrap(b, (120.0, 10.0), 10.0, count=10)
    for p, t in (((-185.0, -110.0), "barrel"), ((120.0, -130.0), "barrel"), ((150.0, 30.0), "crate")):
        b.marker("loot", (p[0], p[1], 0.1), table=t)
    b.zone("no-build", (0, 0, 0), size=(480.0, 380.0, 200.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "river", "visible_km": 12})


RUSTY = srgb(0.48, 0.30, 0.20)
SUN = (28.0, 215.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((-60.0, 45.0, WALL_H + 0.1), 90.0), ((-10.0, -140.0, 0.0), 0.0)]
VIEWS = [
    {"name": "far", "target": (60.0, 20.0, 30.0), "distance": 2400, "yaw": 25, "pitch": -0.4, "lens": 100},
    {"name": "overview", "target": (0.0, 0.0, 5.0), "distance": 560, "yaw": 25, "pitch": 32, "lens": 30},
    {"name": "basins", "target": (-20.0, 45.0, 9.0), "distance": 48, "yaw": -90, "pitch": -4, "lens": 22},
]
