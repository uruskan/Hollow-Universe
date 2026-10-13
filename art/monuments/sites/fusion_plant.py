"""Fusion Plant (MONUMENTS.md §5.4, large tier, catalogue 38): one of the old expansion's power stations on a river,
600 x 400 m, in the Seedward civil livery (off-white plating, orange and teal bands, stencilled codes, big louvres).
Two 120 m cooling towers stand west of the works, one with a jagged bite out of its rim and its rubble below; a long
turbine hall joins the reactor drum, whose shallow dome hides a tokamak still faintly glowing; a banded chimney, the
main transformers behind fire walls, a switchyard sending its line south-east over lattice pylons, an intake on the
river, the admin block and the gate.

Gameplay: the reactor's control vault is the violet room (violet chip and a power cell) beside the core, where the
contamination reaches 4 (3 in the drum); military crates in the turbine hall's control room and the switchyard
house; a recycler in the workshop; Reclaimers in the turbine hall and at the gate, Warden turrets at the drum.
"""
import math

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, block_building,
                        cable, fence, insulator, ladder, lamp, lattice_tower, railing, rubble, srgb, stairs, stripes)
from monkit.kit import tank
from monkit.structures import chimney, road, shed
from monkit.surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL,
                             RUST)

KEY = "fusion-plant"
TITLE = "Fusion Plant"
SEED = 3801
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [320.0, 1500.0, 22000.0]

CT1 = (-200.0, 40.0)
CT2 = (-95.0, 118.0)
DRUM = (152.0, 12.0)
DR = 36.0
DH = 42.0
HALL = (25.0, 10.0)        # turbine hall centre
HW, HD, HH = 150.0, 42.0, 30.0
DECK = 9.0
CIVIL = srgb(0.86, 0.86, 0.83)
CIVIL_DARK = srgb(0.70, 0.71, 0.70)


def cooling_towers(b):
    """Two hyperboloid towers; the second broken at its rim, its rubble fallen inside and out."""
    P.cooling_shell(b, (CT1[0], CT1[1], 0), 120.0, 42.0, 26.0, 29.0, seed=3)
    P.cooling_shell(b, (CT2[0], CT2[1], 0), 116.0, 41.0, 25.5, 28.0, breach=(300.0, 30.0, 36.0), seed=8)
    a = math.radians(300.0)
    for dist, n, size in ((14.0, 14, 3.2), (32.0, 10, 2.6), (52.0, 9, 2.8), (64.0, 6, 2.0)):
        cx, cy = CT2[0] + math.cos(a) * dist, CT2[1] + math.sin(a) * dist
        for k in range(n):
            s = size * (0.5 + b.rng.random())
            ox, oy = b.rng.uniform(-9, 9), b.rng.uniform(-9, 9)
            P.rock(b, (cx + ox, cy + oy, s * 0.15), (s * 1.6, s * 1.2, s * 0.7), seed=k + int(dist), rough=0.25,
                   rings=5, sides=8, tint=srgb(0.70, 0.69, 0.66), wear=0.5)
        rubble(b, (cx, cy, 0.0), 10.0, count=8, tint=srgb(0.72, 0.71, 0.68), max_size=1.6)
    # Aviation lamps on both rims.
    for (cx, cy), h, r in ((CT1, 120.0, 29.0), (CT2, 116.0, 28.0)):
        for k in range(4):
            ang = math.radians(45 + 90 * k)
            if (cx, cy) == CT2 and abs(math.atan2(math.sin(ang - a), math.cos(ang - a))) < math.radians(40):
                continue
            P.beacon(b, (cx + math.cos(ang) * r, cy + math.sin(ang) * r, h), 0.6)
    # A caged ladder up the first tower to a service gallery round its throat.
    lx, ly = CT1[0], CT1[1] - 43.5
    ladder(b, (lx, ly, 0.0), 9.6, (0, 0, 0), cage=True, tint=GREY)
    b.marker("loot", (CT1[0] + 5.0, CT1[1], 0.6), table="barrel")
    b.marker("loot", (CT2[0] - 6.0, CT2[1] + 4.0, 0.6), table="crate")


def circulating_pipes(b):
    """Two big cooling water mains from the turbine hall's west end to the tower basins, on concrete saddles."""
    for pts in ([(-52.0, 3.0), (-118.0, 3.0), (-156.5, 26.0)], [(-52.0, 24.0), (-62.0, 42.0), (-77.0, 79.0)]):
        line = [(x, y, 2.6) for x, y in pts]
        b.tube(line, 1.6, mat=PAINT, tint=CIVIL_DARK, sides=20)
        for p, q in zip(pts, pts[1:]):
            L = math.hypot(q[0] - p[0], q[1] - p[1])
            yaw = math.degrees(math.atan2(q[1] - p[1], q[0] - p[0]))
            n = max(1, int(L / 9.0))
            for k in range(1, n):
                t = k / n
                x, y = p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t
                b.box((1.6, 4.2, 1.2), (x, y, 0.6), (0, 0, yaw + 90), mat=CONCRETE, tint=CONCRETE_T)
            for k in range(int(L / 6.0)):
                t = (k + 0.5) / int(L / 6.0)
                x, y = p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t
                b.cyl(1.64, 0.25, (x, y, 2.6), (0, 90, yaw), mat=PAINT, tint=CIVIL_DARK, sides=20, solid=False,
                      vary=0)
    # The pump house over the mains at the hall's west end.
    P.room(b, (-64.0, 13.0, 0), (0, 0, 0), 14.0, 30.0, 9.0, mat=FORMWORK, tint=CONCRETE_T,
           openings={"front": [(3.0, 1.2, 2.3, 0.0)], "left": [(-10.0, 4.0, 4.0, 0.0), (10.0, 4.0, 4.0, 0.0)],
                     "right": [(-10.0, 4.0, 4.0, 0.0), (10.0, 4.0, 4.0, 0.0)]})
    b.marker("door", (-61.0, -2.6, 0), kind_of="building")
    for y in (6.0, 20.0):
        b.box((0.4, 8.0, 2.0), (-71.4, y, 7.0), (0, 30, 0), mat=PAINT, tint=GREY, solid=False)


def turbine_set(b, x0, y, z):
    """A turbine-generator on the operating floor: a high-pressure casing, a low-pressure casing, the generator and
    its exciter on a plinth, with railings round."""
    b.box((30.0, 9.0, 1.0), (x0 + 15.0, y, z + 0.5), mat=CONCRETE, tint=srgb(0.66, 0.66, 0.63))
    b.cyl(2.0, 6.0, (x0 + 3.5, y, z + 3.2), (0, 90, 0), mat=PAINT, tint=CIVIL, sides=24)
    b.lathe([(2.0, 0.0), (1.4, 0.8), (0.0, 1.0)], (x0 + 0.5, y, z + 3.2), (0, -90, 0), mat=PAINT, tint=CIVIL,
            sides=24)
    b.box((9.0, 8.0, 2.4), (x0 + 12.0, y, z + 2.2), mat=PAINT, tint=CIVIL, bevel=0.1)
    b.cyl(3.8, 9.0, (x0 + 12.0, y, z + 3.4), (0, 90, 0), mat=PAINT, tint=CIVIL, sides=32)
    b.box((9.2, 0.3, 0.6), (x0 + 12.0, y - 4.0, z + 3.2), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.cyl(2.5, 10.0, (x0 + 23.0, y, z + 3.4), (0, 90, 0), mat=PAINT, tint=TEAL, sides=28)
    for dx in (-5.2, 5.2):
        b.lathe([(2.5, 0.0), (2.2, 0.6), (1.0, 1.0), (0.0, 1.0)], (x0 + 23.0 + dx, y, z + 3.4),
                (0, 90 if dx > 0 else -90, 0), mat=PAINT, tint=TEAL, sides=28)
    b.box((2.6, 3.2, 3.0), (x0 + 30.0, y, z + 2.5), mat=PAINT, tint=LIGHTGREY, bevel=0.05)
    for k in range(3):
        b.tube([(x0 + 3.5, y - 1.0 + k, z + 5.2), (x0 + 3.5, y - 1.0 + k, z + 7.5), (x0 + 12.0, y - 1.0 + k, z + 7.5),
                (x0 + 12.0, y - 1.0 + k, z + 7.0)], 0.35, mat=PAINT, tint=GREY, sides=10)
    P.stencil(b, "T" + str(int((x0 + 60) // 37) + 1), (x0 + 20.0, y - 2.55, z + 2.0), (0, 0, 0), 1.8, tint=OFFWHITE,
              depth=0.02)


def turbine_hall(b):
    """The turbine hall: 150 x 42 m, 30 m high, a concrete operating floor at 9 m with three turbine sets, condensers
    below, an overhead crane, the control room at the east end, stairs from the ground aisle."""
    hx, hy = HALL
    P.hall(b, (hx, hy, 0), (0, 0, 0), HW, HD, HH, bay=7.5, plinth=4.0, clad=PANEL, clad_tint=CIVIL,
           roof_tint=LIGHTGREY, plinth_tint=CONCRETE_T, frame_tint=srgb(0.36, 0.38, 0.40), band=TEAL, ruin=0.07,
           panel=3.0, windows=("front", "back"), win_h=3.2, parapet=1.2,
           patch_tints=[srgb(0.72, 0.74, 0.74), srgb(0.80, 0.62, 0.45), srgb(0.62, 0.66, 0.66)],
           doors={"front": [(-55.0, 8.0, 8.0, "shutter"), (-20.0, 1.4, 2.3, "door"), (30.0, 1.4, 2.3, "door"),
                            (55.0, 6.0, 6.0, "shutter")],
                  "left": [(-8.0, 3.0, 4.0, "open"), (13.0, 3.0, 4.0, "open")], "right": [(2.0, 8.0, 9.0, "open")],
                  "back": [(-40.0, 8.0, 8.0, "shutter")]})
    # Louvre bands high on the long sides, an orange stripe under the eaves.
    x0, x1 = hx - HW / 2, hx + HW / 2
    for y, sgn in ((hy - HD / 2 - 0.5, -1), (hy + HD / 2 + 0.5, 1)):
        b.box((HW + 1.0, 0.15, 0.9), (hx, y, HH - 1.6), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        for k in range(10):
            x = x0 + 10.0 + k * 14.0
            for z in range(5):
                b.box((6.0, 0.5, 0.18), (x, y + sgn * 0.15, 14.0 + z * 0.6), (sgn * 35, 0, 0), mat=PAINT,
                      tint=srgb(0.40, 0.42, 0.44), solid=False, vary=0)
    P.stencil(b, "F2", (hx + 45.0, hy - HD / 2 - 0.42, 18.0), (0, 0, 0), 7.0, tint=TEAL)
    # Operating floor on columns, the aisle along the south wall, condensers under each set.
    dy0, dy1 = hy - HD / 2 + 5.0, hy + HD / 2 - 2.0
    b.box((HW - 4.0, dy1 - dy0, 0.6), (hx, (dy0 + dy1) / 2, DECK - 0.3), mat=CONCRETE, tint=srgb(0.62, 0.62, 0.6))
    for k in range(14):
        x = x0 + 4.0 + k * (HW - 8.0) / 13
        for y in (dy0 + 1.0, dy1 - 1.0, (dy0 + dy1) / 2):
            b.box((0.9, 0.9, DECK - 0.9), (x, y, (DECK - 0.6) / 2 + 0.3), mat=CONCRETE, tint=CONCRETE_T)
    railing(b, (x0 + 2.0, dy0, DECK), (x0 + 30.0, dy0, DECK))
    railing(b, (x0 + 36.0, dy0, DECK), (x1 - 2.0, dy0, DECK))
    sets = (-45.0, -8.0, 29.0)
    for x in sets:
        turbine_set(b, hx + x, hy + 12.0, DECK)
        b.box((22.0, 13.0, 6.5), (hx + x + 14.0, hy + 12.0, 0.3 + 3.25), mat=PAINT, tint=CIVIL_DARK, bevel=0.08)
        b.tube([(hx + x + 6.0, hy + 5.0, 3.0), (hx + x + 6.0, hy + 1.0, 3.0), (hx + x + 6.0, hy + 1.0, 8.4)], 0.6,
               mat=PAINT, tint=GREY, sides=12)
    # Stairs from the aisle to the floor (climbing east), with their landings.
    for sx in (x0 + 18.0, x0 + 98.0):
        stairs(b, (sx, dy0 - 2.6, 0.3), (0, 0, -90), 1.6, DECK - 0.3, 12.4, tint=YELLOW)
        P.deck(b, sx + 12.4, dy0 - 3.5, sx + 15.4, dy0, DECK, mat=GRATE, rails="se")
    # Overhead crane on rails along the columns.
    for y in (hy - HD / 2 + 0.6, hy + HD / 2 - 0.6):
        b.box((HW, 0.6, 0.8), (hx, y, 24.0), mat=PAINT, tint=srgb(0.36, 0.38, 0.40), solid=False)
    cx = hx + 10.0
    b.box((3.0, HD - 1.0, 2.2), (cx, hy, 25.5), mat=PAINT, tint=YELLOW)
    b.box((4.0, 4.0, 1.8), (cx, hy + 6.0, 27.4), mat=PAINT, tint=YELLOW, bevel=0.05)
    cable(b, (cx, hy + 6.0, 24.4), (cx, hy + 6.0, 14.0), sag=0.0, radius=0.05)
    b.box((1.6, 1.0, 1.2), (cx, hy + 6.0, 13.4), mat=PAINT, tint=DARK, solid=False)
    # The control room at the east end of the floor, glazed toward the turbines.
    P.room(b, (x1 - 10.0, hy + 10.0, DECK), (0, 0, 0), 16.0, 22.0, 4.2, thick=0.3, mat=PANEL, tint=CIVIL,
           openings={"left": [(-6.0, 4.0, 1.6, 1.0), (0.0, 4.0, 1.6, 1.0), (6.0, 4.0, 1.6, 1.0)],
                     "front": [(-5.0, 1.2, 2.3, 0.0)]}, floor=False)
    for k in range(3):
        b.box((0.06, 3.8, 1.5), (x1 - 17.9, hy + 4.0 + 6.0 * k, DECK + 1.75), mat=GLASS, solid=False)
    for k in range(4):
        b.box((1.0, 2.6, 1.0), (x1 - 13.5, hy + 2.0 + 4.5 * k, DECK + 0.5), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
        b.box((0.5, 2.5, 0.6), (x1 - 13.1, hy + 2.0 + 4.5 * k, DECK + 1.25), (0, -25, 0), mat=PAINT, tint=DARK,
              solid=False)
    b.marker("door", (x1 - 15.0, hy - 1.6, DECK), kind_of="building")
    b.marker("loot", (x1 - 6.0, hy + 16.0, DECK + 0.1), table="military-crate")
    b.marker("loot", (x1 - 6.0, hy + 4.0, DECK + 0.1), table="crate")
    # Loot and guards on the floor and in the aisle.
    for p, t in (((hx - 30.0, hy + 3.0, DECK + 0.1), "crate"), ((hx + 5.0, hy + 24.0, DECK + 0.1), "barrel"),
                 ((hx + 50.0, hy + 2.0, DECK + 0.1), "barrel"), ((hx - 60.0, hy - 14.0, 0.4), "barrel"),
                 ((hx + 20.0, hy - 14.0, 0.4), "crate")):
        b.marker("loot", p, table=t)
    for p, r in (((hx - 10.0, hy + 2.0, DECK + 0.1), "reclaimer"), ((hx + 40.0, hy + 20.0, DECK + 0.1), "reclaimer"),
                 ((hx - 50.0, hy - 14.0, 0.4), "reclaimer")):
        b.marker("guard", p, role=r)
    P.crates(b, (hx - 64.0, hy - 13.0, 0.3), (0, 0, 0), n=5, tint=TEAL)
    P.scrap(b, (hx + 10.0, hy - 14.0), 6.0, count=8, z=0.3)
    for x in (hx - 40.0, hx, hx + 40.0):
        lamp(b, (x, hy, HH - 1.0), (90, 0, 0), colour=srgb(0.85, 0.93, 1.0), size=(0.9, 0.5, 0.4))


def reactor(b):
    """The reactor drum: a round hall 72 m across and 42 m high under a shallow dome, joined to the turbine hall;
    inside, the tokamak on its plinth with its coils and a faint glow, a ring catwalk, and the control vault."""
    cx, cy = DRUM
    # The connector from the turbine hall.
    for y in (cy - 11.0, cy + 11.0):
        P.wall_run(b, (100.4, y), (119.0, y), 22.0, 0.5, mat=FORMWORK, tint=CONCRETE_T)
    P.wall_run(b, (100.6, cy - 11.0), (100.6, cy + 11.0), 22.0, 0.4, [(11.0, 8.0, 9.0, 0.0)], mat=FORMWORK,
               tint=CONCRETE_T)
    b.box((19.0, 22.6, 1.0), (109.7, cy, 22.5), mat=CONCRETE, tint=CONCRETE_T)
    b.box((18.0, 21.4, 0.3), (109.7, cy, 0.15), mat=CONCRETE, tint=srgb(0.6, 0.6, 0.58))
    # The drum: a curved wall with three openings, its floor, bands, the dome.
    P.drum(b, (cx, cy), DR, 0.0, DH, [(180.0, 9.0, 10.0), (270.0, 3.0, 3.0), (90.0, 8.0, 8.0)], thick=0.8,
           mat=PANEL, tint=CIVIL)
    b.cyl(DR - 0.5, 0.3, (cx, cy, 0.15), mat=CONCRETE, tint=srgb(0.58, 0.58, 0.56), sides=48)
    for z, h, t in ((30.0, 2.4, ORANGE), (34.0, 1.4, TEAL), (DH - 0.6, 1.2, srgb(0.36, 0.38, 0.40))):
        b.cyl(DR + 0.06, h, (cx, cy, z), mat=PAINT, tint=t, sides=64, cap=False, solid=False, vary=0)
    # Big louvres between the bands round the drum.
    for k in range(24):
        ang = math.radians(7.5 + 15 * k)
        if abs(math.cos(ang) + 1.0) < 0.05:
            continue
        with b.frame((cx + math.cos(ang) * (DR + 0.25), cy + math.sin(ang) * (DR + 0.25), 0),
                     (0, 0, math.degrees(ang) + 90)):
            b.box((5.0, 0.2, 7.0), (0, 0, 21.0), mat=PAINT, tint=srgb(0.40, 0.42, 0.44), solid=False, vary=0)
            for z in range(8):
                b.box((5.2, 0.6, 0.16), (0, -0.25, 18.0 + z * 0.8), (30, 0, 0), mat=PAINT, tint=CIVIL_DARK,
                      solid=False, vary=0)
            # Rust run down from the louvre, longer on some.
            for dx in (-1.6, 0.4, 1.9):
                L = 2.0 + 7.0 * b.rng.random()
                b.box((0.12 + 0.16 * b.rng.random(), 0.04, L), (dx, -0.08, 17.4 - L / 2), mat=RUST, tint=(1, 1, 1),
                      solid=False, vary=0.1, wear=0.9)
    # Missing wall panels (dark holes into the structure).
    for ang, z in ((-35.0, 12.0), (-20.0, 30.5), (210.0, 8.0), (300.0, 26.0), (35.0, 14.0), (160.0, 31.0)):
        a = math.radians(ang)
        with b.frame((cx + math.cos(a) * (DR + 0.04), cy + math.sin(a) * (DR + 0.04), 0), (0, 0, ang + 90)):
            b.box((2.4, 0.03, 2.4 + 1.2 * b.rng.random()), (0, 0, z), mat=MATTE, tint=srgb(0.07, 0.07, 0.08),
                  solid=False, vary=0)
            b.box((0.15, 0.06, 3.2), (-1.25, -0.02, z), mat=PAINT, tint=srgb(0.36, 0.38, 0.40), solid=False, vary=0)
    P.scorch(b, (cx + 2.0, cy - DR - 4.0, 0.0), 3.5, blobs=3)
    P.scrap(b, (cx - 14.0, cy - DR - 6.0), 6.0, count=7)
    rubble(b, (cx + 18.0, cy - DR + 3.0, 0.0), 4.0, count=7, tint=CIVIL)
    prof = [(DR + 0.8, 0.0), (DR + 0.8, 1.0), (DR - 2.0, 3.2)]
    for k in range(1, 7):
        t = k / 6
        prof.append(((DR - 2.0) * (1 - t ** 1.8), 3.2 + 7.0 * math.sin(t * math.pi / 2)))
    b.lathe(prof, (cx, cy, DH), mat=PANEL, tint=LIGHTGREY, sides=64)
    b.cyl(4.0, 3.0, (cx, cy, DH + 11.5), mat=PAINT, tint=CIVIL_DARK, sides=24)
    b.cyl(4.6, 0.6, (cx, cy, DH + 13.2), mat=PAINT, tint=CIVIL_DARK, sides=24, solid=False)
    P.beacon(b, (cx, cy, DH + 13.6), 0.6)
    for k in range(6):
        ang = math.radians(30 + 60 * k)
        rr = DR - 8.0
        b.box((3.0, 2.0, 1.6), (cx + math.cos(ang) * rr, cy + math.sin(ang) * rr, DH + 7.0 - 3.0),
              (0, 0, math.degrees(ang)), mat=PAINT, tint=LIGHTGREY, bevel=0.05)
    a = math.radians(-70.0)
    P.stencil(b, "F2", (cx + math.cos(a) * (DR + 0.07), cy + math.sin(a) * (DR + 0.07), 6.0),
              (0, 0, math.degrees(a) + 90), 9.0, tint=TEAL, depth=0.03, wrap=DR + 0.07)
    # The south door: a Seedward blast door in a portal, a Warden turret on each side.
    with b.frame((cx, cy - DR - 0.7, 0), (0, 0, 0)):
        P.blast_door(b, (0, 0, 0), (0, 0, 0), w=3.0, h=3.0, depth=1.6, frame=1.0, open=0.5, tint=CIVIL_DARK,
                     frame_tint=CONCRETE_T, kind="blast-door")
        stripes(b, (-3.0, -1.0, 0), (3.0, -1.0, 0), 0.2, width=0.3, at_z=0.01)
        for x in (-5.0, 5.0):
            b.box((1.4, 1.4, 1.2), (x, -2.0, 0.6), mat=CONCRETE, tint=CONCRETE_T, bevel=0.04)
            b.marker("guard", (x, -2.0, 1.2), role="warden-turret")
    # Inside: the tokamak on its plinth, its coils and solenoid, the glow, the catwalk ring.
    tz = 11.0
    b.cyl(16.0, 5.0, (cx, cy, 2.8), mat=CONCRETE, tint=srgb(0.55, 0.55, 0.53), sides=48)
    prof = [(13.0 + 6.0 * math.cos(2 * math.pi * k / 20), 6.0 * math.sin(2 * math.pi * k / 20)) for k in range(21)]
    b.lathe(prof, (cx, cy, tz), mat=PAINT, tint=srgb(0.30, 0.31, 0.33), sides=48, close=False, solid="hull")
    b.cyl(19.06, 0.5, (cx, cy, tz), mat=LAMP, tint=srgb(0.55, 0.45, 1.0), sides=64, solid=False, vary=0)
    b.cyl(3.4, 22.0, (cx, cy, tz), mat=PAINT, tint=srgb(0.45, 0.46, 0.48), sides=24)
    dshape = [(5.0, -9.0), (12.0, -10.5), (19.0, -8.0), (21.5, -3.0), (22.0, 0.0), (21.5, 3.0), (19.0, 8.0),
              (12.0, 10.5), (5.0, 9.0), (5.0, -9.0)]
    for k in range(16):
        ang = 2 * math.pi * k / 16
        pts = [(cx + math.cos(ang) * r, cy + math.sin(ang) * r, tz + z) for r, z in dshape]
        b.tube(pts, 0.75, mat=PAINT, tint=ORANGE if k % 4 == 0 else srgb(0.62, 0.64, 0.66), sides=8, cap=False)
    zc = 15.0
    n = 32
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        for r_in in (24.0,):
            p0 = (cx + math.cos(a0) * (r_in + 1.0), cy + math.sin(a0) * (r_in + 1.0))
            p1 = (cx + math.cos(a1) * (r_in + 1.0), cy + math.sin(a1) * (r_in + 1.0))
            L = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
            b.box((L + 0.05, 2.0, 0.08), ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, zc - 0.04),
                  (0, 0, math.degrees((a0 + a1) / 2) + 90), mat=GRATE, tint=GREY, vary=0)
            for rr in (r_in, r_in + 2.0):
                q0 = (cx + math.cos(a0) * rr, cy + math.sin(a0) * rr, zc)
                q1 = (cx + math.cos(a1) * rr, cy + math.sin(a1) * rr, zc)
                railing(b, q0, q1, spacing=2.4, toe=False)
        if k % 4 == 0:
            b.box((0.3, 0.3, zc), (cx + math.cos(a0) * 25.0, cy + math.sin(a0) * 25.0, zc / 2), mat=PAINT,
                  tint=srgb(0.36, 0.38, 0.40), vary=0)
    # A stair tower from the floor to the catwalk, on the south side inside.
    sx, sy = cx + 10.0, cy - 29.0
    P.stair_tower(b, (sx, sy, 0.3), (0, 0, 90), [0.0, 7.35, zc - 0.3], width=1.1)
    P.deck(b, sx - 2.0, sy + 1.4, sx, cy - 25.4, zc, rails="we")
    # The control vault on the north side: the violet room.
    vx, vy = cx, cy + 25.0
    P.room(b, (vx, vy, 0.3), (0, 0, 0), 16.0, 9.0, 4.6, thick=0.6, mat=FORMWORK, tint=CONCRETE_T,
           openings={"front": [(0.0, 3.0, 3.0, 0.0)]})
    P.blast_door(b, (vx, vy - 5.2, 0.3), (0, 0, 0), w=3.0, h=3.0, depth=1.2, frame=0.8, tint=srgb(0.28, 0.22, 0.38),
                 frame_tint=CONCRETE_T, chip="violet", cell=True, kind="vault-door")
    for x in (-5.0, 5.0):
        b.marker("loot", (vx + x, vy + 2.0, 0.42), table="vault-crate")
    for k in range(3):
        b.box((2.4, 0.9, 1.0), (vx - 3.0 + 3.0 * k, vy + 3.2, 0.8), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
    lamp(b, (vx, vy, 4.6), (90, 0, 0), colour=srgb(0.62, 0.25, 1.0), size=(0.6, 0.3, 0.2))
    P.plate(b, (vx + 3.4, vy - 5.95, 3.0), (0, 0, 0), 1.6, 0.5, tint=srgb(0.62, 0.25, 1.0), mark=OFFWHITE, text="C0")
    b.marker("guard", (vx + 6.0, vy - 9.0, 0.3), role="reclaimer-heavy")
    # Cable trays and pipes from the machine to the wall.
    for k in range(6):
        ang = math.radians(20 + 60 * k)
        p0 = (cx + math.cos(ang) * 19.5, cy + math.sin(ang) * 19.5, 9.0)
        p1 = (cx + math.cos(ang) * (DR - 1.2), cy + math.sin(ang) * (DR - 1.2), 9.0)
        b.tube([p0, p1, (p1[0], p1[1], 1.0)], 0.45, mat=PAINT, tint=GREY if k % 2 else ORANGE, sides=10)
    for k in range(4):
        ang = math.radians(45 + 90 * k)
        lamp(b, (cx + math.cos(ang) * 30.0, cy + math.sin(ang) * 30.0, 36.0), (90, 0, 0),
             colour=srgb(0.85, 0.93, 1.0), size=(0.9, 0.5, 0.4))
    b.marker("loot", (cx - 20.0, cy - 20.0, 0.4), table="crate")
    b.marker("loot", (cx + 26.0, cy + 10.0, 0.4), table="barrel")
    P.scrap(b, (cx - 24.0, cy + 14.0), 5.0, count=6, z=0.3)
    # An outside stair to the roof on the south-east, a ladder on to the dome.
    with b.frame((cx + 26.0, cy - 28.5, 0), (0, 0, 42.0)):
        P.stair_tower(b, (0, 0, 0), (0, 0, 0), [0.0, 10.5, 21.0, 31.5, DH + 1.0], width=1.1, clad=CORRUGATED,
                      clad_tint=CIVIL)
    b.zone("radiation", (cx, cy, 12.0), radius=24.0, level=4, source="core")
    b.zone("radiation", (cx, cy, 22.0), size=(2 * DR + 4, 2 * DR + 4, 48.0), level=3, source="core")
    b.marker("landmark", (cx, cy, DH + 14.0), kind_of="reactor-dome")


def chimney_and_duct(b):
    """The vent stack north-east of the drum and the duct that feeds it."""
    chimney(b, (210.0, 80.0, 0), height=120.0, r_base=6.2, r_top=4.0, bands=(ORANGE, OFFWHITE))
    b.box((5.0, 5.0, 4.0), (210.0, 80.0, 2.0), mat=CONCRETE, tint=CONCRETE_T)
    pts = [(176.0, 40.0), (196.0, 66.0)]
    b.beam((pts[0][0], pts[0][1], 26.0), (pts[1][0], pts[1][1], 22.0), 4.0, 4.0, mat=PAINT, tint=CIVIL_DARK,
           solid=True)
    b.beam((pts[1][0], pts[1][1], 22.0), (205.0, 76.0, 18.0), 4.0, 4.0, mat=PAINT, tint=CIVIL_DARK, solid=True)
    for t in (0.25, 0.6):
        x = pts[0][0] + (pts[1][0] - pts[0][0]) * t
        y = pts[0][1] + (pts[1][1] - pts[0][1]) * t
        z = 26.0 - 4.0 * t - 2.0
        P.steel_frame(b, (x, y, 0), (0, 0, 52.0), 4.0, 4.0, [0.0, z / 2, z], col=0.5, tint=srgb(0.36, 0.38, 0.40))


def switchyard(b):
    """The main transformers behind fire walls at the turbine hall's south wall, the switchyard south of them with
    breakers and gantries, its control house, and the line leaving south-east over lattice pylons."""
    hx, hy = HALL
    yt = hy - HD / 2 - 9.0
    for k, x in enumerate((hx - 10.0, hx + 15.0, hx + 40.0)):
        b.piece("main-transformer", (x, yt, 0), (0, 0, 0))
        b.box((1.0, 12.0, 9.0), (x + 12.5, yt + 1.0, 4.5), mat=FORMWORK, tint=CONCRETE_T)
        b.tube([(x - 2.0, yt - 0.8, 9.0), (x - 2.0, yt - 0.8, 12.0), (x - 2.0, hy - HD / 2 - 1.0, 12.0)], 0.4,
               mat=PAINT, tint=GREY, sides=10)
    b.box((1.0, 12.0, 9.0), (hx - 22.5, yt + 1.0, 4.5), mat=FORMWORK, tint=CONCRETE_T)
    # The yard: gravel, a fence, breakers and gantries in bays, two small transformers.
    yx, yy = hx + 15.0, -78.0
    b.box((120.0, 50.0, 0.25), (yx, yy, 0.125), mat=CONCRETE, tint=srgb(0.50, 0.49, 0.46), wear=0.7)
    fence(b, [(yx - 50.0, yy + 25.0, 0), (yx - 60.0, yy + 25.0, 0), (yx - 60.0, yy - 25.0, 0),
              (yx + 60.0, yy - 25.0, 0), (yx + 60.0, yy + 25.0, 0), (yx - 40.0, yy + 25.0, 0)], height=2.4, post=4.0)
    for i in range(3):
        for j in range(3):
            b.piece("breaker", (yx - 36.0 + 36.0 * i + (j - 1) * 8.0, yy + 8.0, 0), (0, 0, 0))
        b.piece("gantry", (yx - 36.0 + 36.0 * i, yy - 6.0, 0), (0, 0, 0))
        b.piece("transformer", (yx - 36.0 + 36.0 * i, yy - 16.0, 0), (0, 0, 90))
    block_building(b, (yx + 48.0, yy + 14.0, 0), (0, 0, 180), 10.0, 7.0, 3.6, broken=0.5)
    b.marker("loot", (yx + 48.0, yy + 15.0, 0.12), table="military-crate")
    # The line out: two pylons south-east, cables from the gantries.
    tips_prev = [(yx - 36.0 + 36.0 * i, yy - 6.0, 8.5) for i in range(3)]
    for k, (px, py) in enumerate(((yx + 95.0, yy - 75.0), (yx + 180.0, yy - 150.0))):
        lattice_tower(b, (px, py, 0), 34.0, 7.0, 2.0, rot=(0, 0, 40), sections=7, member=0.18,
                      tint=srgb(0.55, 0.57, 0.58))
        tips = []
        with b.frame((px, py, 0), (0, 0, 40)):
            for z, span in ((28.0, 8.0), (33.5, 5.0)):
                b.beam((-span, 0, z), (span, 0, z), 0.2, 0.2, tint=srgb(0.55, 0.57, 0.58))
                for s in (-1, 1):
                    b.beam((s * span, 0, z), (s * 1.0, 0, z + 2.0), 0.14, 0.14, tint=srgb(0.55, 0.57, 0.58))
                    insulator(b, (s * (span - 0.4), 0, z - 2.0), 2.0, tint=srgb(0.82, 0.82, 0.8))
            tips = [b.world((-7.6, 0, 26.0)), b.world((7.6, 0, 26.0)), b.world((-4.6, 0, 31.5))]
        for pa, pb in zip(tips_prev, tips):
            cable(b, tuple(pa), tuple(pb), sag=0.04, radius=0.04)
        tips_prev = tips
        b.marker("landmark", (px, py, 36.0), kind_of="pylon")
    for t in tips_prev:
        cable(b, tuple(t), (t.x + 60.0, t.y - 55.0, t.z - 2.0), sag=0.03, radius=0.04)


def works(b):
    """Admin block, gate, workshop with the recycler, coolant tanks, the intake on the river, water treatment,
    roads, lamps, the perimeter fence."""
    # Admin block by the gate, its car park.
    block_building(b, (-170.0, -125.0, 0), (0, 0, 0), 44.0, 14.0, 7.6, floors=2, broken=0.45, mat=PANEL, tint=CIVIL)
    b.box((44.4, 0.3, 1.2), (-170.0, -132.2, 8.3), mat=PAINT, tint=TEAL, solid=False, vary=0)
    P.stencil(b, "F2", (-188.0, -132.42, 3.6), (0, 0, 0), 2.6, tint=TEAL)
    b.box((8.0, 4.0, 0.3), (-159.0, -134.5, 3.2), mat=PAINT, tint=CIVIL, solid=False)
    for x in (-162.0, -156.0):
        b.box((0.25, 0.25, 3.1), (x, -136.2, 1.55), mat=PAINT, tint=GREY, solid=False)
    for k in range(3):
        b.cyl(0.08, 10.0, (-196.0 + 3.0 * k, -138.0, 5.0), mat=PAINT, tint=GREY, sides=6, solid=False)
    b.marker("loot", (-180.0, -125.0, 0.12), table="crate")
    b.marker("guard", (-150.0, -140.0, 0.1), role="reclaimer")
    b.box((70.0, 34.0, 0.2), (-110.0, -150.0, 0.1), mat=ASPHALT, tint=(1, 1, 1))
    for k in range(10):
        b.box((0.15, 5.0, 0.02), (-140.0 + k * 6.5, -142.0, 0.21), mat=PAINT, tint=srgb(0.85, 0.83, 0.75),
              solid=False, vary=0, wear=0.7)
    for k, (x, y, yaw) in enumerate(((-136.0, -143.0, 92), (-117.0, -142.0, 86), (-98.0, -158.0, 70),
                                     (-124.0, -160.0, 115))):
        b.piece("wreck-car", (x, y, 0.2), (0, 0, yaw))
    # Gate and guard house on the south fence.
    gx = -110.0
    P.room(b, (gx + 14.0, -192.0, 0), (0, 0, 0), 5.0, 4.0, 2.8, thick=0.25, mat=PANEL, tint=CIVIL,
           openings={"front": [(0.0, 3.4, 1.1, 1.0)], "right": [(0.5, 1.0, 2.2, 0.0)]})
    b.box((0.4, 0.4, 1.2), (gx - 6.0, -199.0, 0.6), mat=PAINT, tint=DARK)
    b.box((0.25, 0.25, 9.0), (gx - 1.5, -199.0, 1.1), (0, 90, 0), mat=PAINT, tint=RED, solid=False)
    b.piece("wreck-truck", (gx + 4.0, -212.0, 0), (0, 0, 75))
    b.marker("guard", (gx + 10.0, -186.0, 0), role="reclaimer")
    b.marker("spawn", (gx, -230.0, 0), kind_of="approach")
    # Workshop with the recycler, containers.
    shed(b, (225.0, -115.0, 0), (0, 0, 0), 40.0, 22.0, 9.0, tint=CIVIL, frame_tint=srgb(0.36, 0.38, 0.40),
         doors=[("front", -8.0, 8.0, 6.0), ("front", 10.0, 8.0, 6.0)], ruin=0.15)
    b.marker("recycler", (232.0, -110.0, 0.25))
    b.box((2.6, 1.8, 2.2), (232.0, -108.0, 1.35), mat=PAINT, tint=srgb(0.30, 0.45, 0.40), bevel=0.06)
    b.marker("loot", (215.0, -112.0, 0.25), table="crate")
    b.marker("loot", (240.0, -120.0, 0.25), table="barrel")
    b.piece("drum-stack", (205.0, -128.0, 0), (0, 0, 15))
    b.marker("loot", (203.0, -131.0, 0.1), table="fuel-barrel")
    for x, y, yaw, tint in ((262.0, -100.0, 90, TEAL), (262.0, -128.0, 88, srgb(0.70, 0.40, 0.2)),
                            (190.0, -150.0, 10, CIVIL)):
        b.piece("container-40", (x, y, 0), (0, 0, yaw), tint=tint)
    # A pipe rack from the drum to the coolant tanks, auxiliary transformers by the drum.
    P.pipe_rack(b, [(189.0, 4.0), (218.0, 4.0), (218.0, 34.0)], width=3.0, height=5.0, bent=7.0,
                pipes=[(0.4, CIVIL), (0.3, TEAL), (0.3, ORANGE)])
    for k, x in enumerate((118.0, 130.0)):
        b.piece("transformer", (x, -32.0, 0), (0, 0, 0))
    # Coolant tanks east of the drum.
    for k in range(3):
        P.bullet_tank(b, (232.0, -10.0 + k * 12.0, 0), (0, 0, 0), 2.8, 26.0, tint=CIVIL, band=TEAL if k != 1 else ORANGE)
    tank(b, (262.0, 40.0, 0), 7.0, 14.0, tint=CIVIL, roof="dome", ladder_side=180.0, band=TEAL)
    # Water treatment: two clarifiers north of the hall.
    for x, y in ((-20.0, 110.0), (25.0, 125.0)):
        b.cyl(14.0, 3.5, (x, y, 1.75), mat=CONCRETE, tint=CONCRETE_T, sides=48, solid=False)
        b.cyl(13.4, 0.2, (x, y, 2.6), mat=GLOSS, tint=srgb(0.20, 0.26, 0.24), sides=48, solid=False)
        b.box((28.0, 1.4, 0.2), (x, y, 3.7), mat=GRATE, tint=GREY, solid=False)
        railing(b, (x - 14.0, y + 0.7, 3.8), (x + 14.0, y + 0.7, 3.8))
        railing(b, (x - 14.0, y - 0.7, 3.8), (x + 14.0, y - 0.7, 3.8))
        P.arc_wall(b, (x, y), 14.0, 0.0, 360.0, 0.0, 3.6, thick=0.4, piece_deg=20.0, mat=CONCRETE, tint=CONCRETE_T)
    # The intake on the river: a quay, the screen house, the outfall.
    b.box((240.0, 4.0, 2.0), (40.0, 196.0, 1.0), mat=FORMWORK, tint=CONCRETE_T)
    for k in range(12):
        b.cyl(0.35, 0.8, (-70.0 + k * 20.0, 194.6, 2.4), mat=PAINT, tint=DARK, sides=10, solid=False)
    P.hall(b, (60.0, 178.0, 0), (0, 0, 0), 48.0, 20.0, 11.0, bay=8.0, plinth=3.0, clad=PANEL, clad_tint=CIVIL,
           frame_tint=srgb(0.36, 0.38, 0.40), band=TEAL, ruin=0.12, windows=("front",), skylights=False,
           doors={"front": [(-12.0, 1.4, 2.3, "door"), (10.0, 6.0, 5.0, "shutter")]})
    for k in range(5):
        b.box((6.0, 2.0, 6.0), (40.0 + k * 10.0, 190.5, 3.0), mat=CONCRETE, tint=CONCRETE_T)
        b.box((4.6, 0.2, 5.0), (40.0 + k * 10.0, 191.6, 2.6), mat=GRATE, tint=GREY, solid=False)
    b.marker("loot", (55.0, 176.0, 0.4), table="barrel")
    # Roads.
    road(b, [(gx, -240.0, 0), (gx, -175.0, 0), (-60.0, -50.0, 0), (60.0, -42.0, 0), (190.0, -55.0, 0),
             (230.0, -95.0, 0)], width=9.0)
    road(b, [(-60.0, -50.0, 0), (-150.0, -60.0, 0), (-250.0, -30.0, 0)], width=7.0)
    road(b, [(190.0, -55.0, 0), (215.0, 30.0, 0), (215.0, 140.0, 0), (60.0, 160.0, 0), (-150.0, 175.0, 0)], width=7.0)
    for x, y, yaw in ((-80.0, -55.0, 0), (0.0, -45.0, 0), (120.0, -48.0, 180), (205.0, 0.0, 90),
                      (210.0, 120.0, 90), (-60.0, 165.0, 0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, yaw))
    for x, y, yaw in ((-45.0, -32.0, 45), (110.0, -30.0, 135), (100.0, 60.0, -45), (-60.0, 60.0, 0)):
        b.piece("flood-mast", (x, y, 0), (0, 0, yaw))
    # Perimeter fence with a gate and two breaches.
    X, Y = 296.0, 198.0
    runs = [[(gx + 7.0, -Y), (X, -Y), (X, 40.0)], [(X, 60.0), (X, Y - 8.0)], [(-X, Y - 8.0), (-X, -60.0)],
            [(-X, -80.0), (-X, -Y), (gx - 7.0, -Y)]]
    for run in runs:
        fence(b, [(x, y, 0) for x, y in run], height=2.6, post=5.0)
    for x in (gx - 7.0, gx + 7.0):
        b.box((0.4, 0.4, 3.0), (x, -Y, 1.5), mat=PAINT, tint=GREY, bevel=0.02)
    P.scrap(b, (X, 50.0), 8.0, count=8)
    P.scrap(b, (-X, -70.0), 8.0, count=7)
    for p, t in (((-30.0, -60.0, 0.1), "barrel"), ((150.0, -60.0, 0.1), "barrel"), ((-140.0, 60.0, 0.1), "barrel"),
                 ((250.0, 70.0, 0.1), "crate"), ((20.0, 150.0, 0.1), "barrel")):
        b.marker("loot", p, table=t)




def build(b):
    b.set_pad(("rect", 600.0, 400.0), ground="gravel", blend=40.0, clear=15.0)
    cooling_towers(b)
    circulating_pipes(b)
    turbine_hall(b)
    reactor(b)
    chimney_and_duct(b)
    switchyard(b)
    works(b)
    b.zone("no-build", (0, 0, 0), size=(680.0, 480.0, 260.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "river", "visible_km": 18})


SUN = (26.0, 220.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((150.0, -33.0, 0), 160.0), ((162.0, -46.0, 0), 200.0)]
SHIP = ((182.0, -64.0, 0), 300.0)
VIEWS = [
    {"name": "far", "target": (-20.0, 40.0, 58.0), "distance": 2600, "yaw": 38, "pitch": -0.75, "lens": 135},
    {"name": "overview", "target": (0.0, 10.0, 15.0), "distance": 760, "yaw": 34, "pitch": 27, "lens": 30},
    {"name": "door", "target": (140.0, -20.0, 14.0), "distance": 46, "yaw": 36, "pitch": -15, "lens": 20},
]
