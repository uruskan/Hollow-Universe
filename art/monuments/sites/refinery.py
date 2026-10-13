"""Refinery (MONUMENTS.md §5.3, medium tier): a Seedward fuel refinery on a coast. Three distillation columns with
platform rings stand over the process unit beside a fired heater and its stack; a pipe rack runs the length of the
site to a bunded tank farm; the flare stack still burns 70 m up (the silhouette); a blast-resistant control room
keeps the cyan room (chip and power cell); a truck loading rack and a sulphur unit (gunpowder makings). The process
unit leaks (contamination 2). Fuel and gunpowder makings.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, fence, ladder, lamp, lattice_tower, railing, rubble, srgb, stairs, tank)
from monkit.structures import road, shed
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PANEL, RUST

KEY = "refinery"
TITLE = "Refinery"
SEED = 2909
BUILDER = {"chunk": 48.0, "max_edge": 5.0}

STEEL = srgb(0.66, 0.68, 0.68)
COLUMNS = ((-64.0, 10.0, 3.2, 52.0), (-50.0, 14.0, 2.6, 44.0), (-37.0, 9.0, 2.2, 36.0))
FLARE = (-118.0, 66.0)
RACK_Y = -26.0


def column(b, x, y, r, h, tint=srgb(0.80, 0.80, 0.78)):
    """A distillation column: skirt, shell with insulation bands, a dished head, platform rings every 9 m with rails,
    caged ladders between them alternating sides, nozzles and a vapour line over the top."""
    b.cyl(r + 0.4, 4.0, (x, y, 2.0), mat=CONCRETE, tint=CONCRETE_T, sides=24)
    b.cyl(r, h - 4.0, (x, y, 4.0 + (h - 4.0) / 2), tint=tint, sides=24)
    for z in range(8, int(h), 6):
        b.cyl(r + 0.03, 0.12, (x, y, z), tint=GREY, sides=24, solid=False, vary=0)
    b.lathe([(r, 0.0), (r * 0.9, r * 0.35), (r * 0.5, r * 0.55), (0.0, r * 0.6)], (x, y, h), tint=tint, sides=24)
    levels = [z for z in range(10, int(h) - 2, 9)] + [h - 0.6]
    prev = 0.0
    for i, z in enumerate(levels):
        pm.ring_deck(b, (x, y), r + 0.05, r + 1.5, z, sides=20)
        nxt = (i + 1) % 2
        pm.ring_rail(b, (x, y), r + 1.45, z, sides=20, gaps=((4, 5) if i % 2 == 0 else (14, 15)) +
                     (((4, 5) if nxt == 0 else (14, 15)) if i + 1 < len(levels) else ()))
        a = math.radians(90 if i % 2 == 0 else 270)
        lx, ly = x + math.cos(a) * (r + 1.75), y + math.sin(a) * (r + 1.75)
        ladder(b, (lx, ly, prev), z - prev, (0, 0, math.degrees(a) - 90 + 180), cage=True)
        for k in range(6):
            aa = 2 * math.pi * k / 6 + 0.3
            b.beam((x + math.cos(aa) * (r + 0.05), y + math.sin(aa) * (r + 0.05), z - 1.1),
                   (x + math.cos(aa) * (r + 1.4), y + math.sin(aa) * (r + 1.4), z - 0.12), 0.1, 0.1, tint=DARK)
        prev = z
    for k in range(4):
        z = 8.0 + k * (h - 14.0) / 3
        a = math.radians(200 + k * 25)
        b.cyl(0.35, 1.2, (x + math.cos(a) * (r + 0.5), y + math.sin(a) * (r + 0.5), z), (0, 90, math.degrees(a)),
              tint=tint, sides=10, solid=False)
    b.marker("light", (x, y, h + r * 0.6 + 0.4), colour=[1.0, 0.12, 0.05], range=60.0, blink=1.0)
    b.box((0.3, 0.3, 0.3), (x, y, h + r * 0.6 + 0.15), mat=LAMP, tint=srgb(1.0, 0.12, 0.05), solid=False)


def process_unit(b):
    rng = b.rng
    for (x, y, r, h) in COLUMNS:
        column(b, x, y, r, h)
    # Vapour lines from the column tops down to the condensers on the rack.
    for (x, y, r, h) in COLUMNS:
        b.tube([(x, y, h + r * 0.55), (x, y - r - 1.0, h + 1.0), (x, y - r - 1.0, 16.5), (x, RACK_Y + 1.0, 14.6)],
               0.45, tint=srgb(0.78, 0.78, 0.76), sides=10)
        # An air cooler bank on top of the rack: a box of tubes under three fans.
        with b.frame((x, RACK_Y, 10.7)):
            b.box((5.6, 5.0, 1.2), (0, 0, 0.6), tint=srgb(0.6, 0.62, 0.62))
            b.box((5.4, 4.8, 0.3), (0, 0, 1.35), mat=GRATE, tint=GREY, solid=False)
            for k in range(3):
                b.cyl(0.8, 0.5, (-1.8 + k * 1.8, 0, 1.75), tint=DARK, sides=14, cap=False, solid=False)
                b.box((1.4, 0.12, 0.05), (-1.8 + k * 1.8, 0, 1.75), (0, 0, k * 40), tint=GREY, solid=False, vary=0)
            b.box((5.8, 0.6, 3.4), (0, 2.6, 2.6), tint=srgb(0.7, 0.72, 0.72), solid=False)
            b.cyl(0.6, 2.4, (0, 2.6, 3.9), (0, 90, 0), tint=srgb(0.78, 0.78, 0.76), sides=12)
    # The fired heater: a tall box with burners below and its stack.
    hx, hy = -78.0, -8.0
    b.box((10.0, 7.0, 14.0), (hx, hy, 7.0), tint=srgb(0.52, 0.5, 0.48))
    for z in (4.0, 8.0, 12.0):
        b.box((10.2, 7.2, 0.3), (hx, hy, z), tint=DARK, solid=False, vary=0)
    for k in range(5):
        b.cyl(0.4, 1.2, (hx - 4.0 + k * 2.0, hy - 3.7, 1.0), (90, 0, 0), tint=DARK, sides=8, solid=False)
        b.box((0.3, 0.05, 0.3), (hx - 4.0 + k * 2.0, hy - 4.32, 1.0), mat=LAMP, tint=srgb(1.0, 0.45, 0.12),
              solid=False, vary=0)
    b.cyl(1.4, 34.0, (hx, hy, 14.0 + 17.0), tint=srgb(0.45, 0.43, 0.42), sides=16, radius2=1.1)
    for z in (30.0, 46.0):
        b.cyl(1.4 - 0.3 * (z - 14) / 34 + 0.04, 1.2, (hx, hy, z), tint=(RED, OFFWHITE)[z > 40], sides=16, solid=False,
              vary=0)
    pm.aviation_lamp(b, (hx, hy, 48.0), size=0.35)
    pm.switchback(b, (hx + 7.0, hy - 1.0, 0), (0, 0, 0), 14.0, width=0.9, flight_rise=2.8)
    pm.slab_holes(b, hx - 5.0, hy - 3.5, hx + 8.2, hy + 3.5, 13.94, 0.06, [(hx + 5.85, hy - 1.1, hx + 8.15, hy + 3.05)],
                  mat=GRATE, tint=GREY)
    railing(b, (hx - 5.0, hy - 3.5, 14.0), (hx + 8.2, hy - 3.5, 14.0))
    railing(b, (hx - 5.0, hy + 3.5, 14.0), (hx - 5.0, hy - 3.5, 14.0))
    # Exchangers on saddles, pumps, a steel structure of decks between the columns.
    for k, (x, y) in enumerate(((-58.0, -10.0), (-50.0, -10.0), (-42.0, -10.0), (-58.0, -16.0), (-46.0, -16.0))):
        b.cyl(1.1, 7.0, (x, y, 2.2), (0, 90, 0), tint=(srgb(0.76, 0.76, 0.74), srgb(0.3, 0.42, 0.55))[k % 2], sides=16)
        for dx in (-2.5, 2.5):
            b.box((0.6, 1.8, 1.2), (x + dx, y, 0.6), mat=CONCRETE, tint=CONCRETE_T)
    for k in range(6):
        x = -66.0 + k * 5.0
        b.box((1.6, 0.8, 0.9), (x, -20.0, 0.6), tint=srgb(0.3, 0.42, 0.36), bevel=0.03)
        b.cyl(0.35, 1.0, (x + 1.0, -20.0, 0.7), (0, 90, 0), tint=srgb(0.3, 0.42, 0.36), sides=10, solid=False)
        b.box((2.2, 1.2, 0.3), (x + 0.3, -20.0, 0.15), mat=CONCRETE, tint=CONCRETE_T)
    for z in (6.0, 12.0, 18.0):
        holes = [(-69.4, 1.9, -68.0, 3.0)] if z > 6.0 else []
        pm.slab_holes(b, -70.0, -1.0, -32.0, 3.0, z - 0.06, 0.06, holes, mat=GRATE, tint=GREY)
        railing(b, (-70.0, -1.0, z), (-32.0, -1.0, z))
        railing(b, (-32.0, 3.0, z), (-70.0, 3.0, z))
        if z < 18.0:
            ladder(b, (-68.7, 3.05, z), 6.0, (0, 0, 0), cage=False)
    for x in (-70.0, -57.0, -44.0, -32.0):
        for yy in (-1.0, 3.0):
            b.box((0.4, 0.4, 18.0), (x, yy, 9.0), tint=STEEL)
    stairs(b, (-25.0, 1.0, 0), (0, 0, 90), 1.0, 6.0, 7.0)
    b.marker("loot", (-60.0, 1.0, 12.0), table="crate")
    b.marker("loot", (-40.0, -18.0, 0.0), table="fuel-barrel")
    b.marker("loot", (-64.0, 10.0 + 4.0, 19.0), table="military-crate")
    b.marker("guard", (-52.0, 1.0, 6.0), 180.0, role="reclaimer")
    b.marker("guard", (-36.0, 1.0, 18.0), 180.0, role="reclaimer-sentry")
    b.zone("radiation", (-52.0, 0.0, 10.0), radius=36.0, level=2, source="leaks")
    b.marker("landmark", (COLUMNS[0][0], COLUMNS[0][1], COLUMNS[0][3] + 2.5), kind_of="distillation-column",
             visible_km=8)


def pipe_rack(b):
    """The main rack: portal frames every 6 m, three tiers of pipes, a branch to the tank farm."""
    tints = (srgb(0.78, 0.78, 0.76), YELLOW, srgb(0.3, 0.42, 0.55), srgb(0.62, 0.3, 0.18), OFFWHITE, srgb(0.4, 0.5, 0.42))

    def rack(a, c, width=6.0):
        dx, dy = c[0] - a[0], c[1] - a[1]
        length = math.hypot(dx, dy)
        yaw = math.degrees(math.atan2(dy, dx))
        with b.frame((a[0], a[1], 0), (0, 0, yaw)):
            n = int(length / 6.0)
            for i in range(n + 1):
                x = length * i / n
                for y in (-width / 2, width / 2):
                    b.box((0.35, 0.35, 10.0), (x, y, 5.0), tint=STEEL)
                    b.box((0.9, 0.9, 0.4), (x, y, 0.2), mat=CONCRETE, tint=CONCRETE_T, vary=0)
                for z in (5.0, 7.5, 10.0):
                    b.box((0.25, width + 0.6, 0.3), (x, 0, z), tint=STEEL, solid=False)
                if i % 3 == 0:
                    b.beam((x, -width / 2, 0.6), (x, width / 2, 4.6), 0.12, 0.12, tint=STEEL)
            for y in (-width / 2, width / 2):
                b.box((length, 0.25, 0.3), (length / 2, y, 10.2), tint=STEEL, solid=False)
            k = 0
            for z, ys in ((5.4, (-2.2, -1.2, 0.2, 1.6)), (7.9, (-2.0, -0.4, 1.2, 2.2)), (10.4, (-1.6, 0.6, 2.0))):
                for y in ys:
                    rr = 0.18 + 0.12 * ((k * 5) % 3)
                    b.tube([(0.0, y, z + rr), (length, y, z + rr)], rr, tint=tints[k % len(tints)], sides=8, cap=False)
                    k += 1
    rack((-120.0, RACK_Y), (110.0, RACK_Y))
    rack((30.0, RACK_Y + 3.0), (30.0, 74.0), width=5.0)
    rack((-60.0, RACK_Y + 3.0), (-60.0, -4.0), width=4.0)


def tank_farm(b):
    tanks = ((60.0, 18.0, 11.0, 13.0), (92.0, 18.0, 12.0, 14.0), (124.0, 20.0, 10.0, 12.0),
             (60.0, 54.0, 11.0, 12.0), (92.0, 54.0, 12.0, 13.0), (124.0, 56.0, 10.0, 11.0))
    for i, (x, y, r, h) in enumerate(tanks):
        tank(b, (x, y, 0), r, h, tint=(srgb(0.86, 0.86, 0.84), srgb(0.78, 0.8, 0.8))[i % 2],
             band=(ORANGE, TEAL)[i % 2], ladder_side=-90.0 if i % 3 == 0 else None,
             roof=("cone", "dome")[i % 2])
        pm.stencil(b, (x, y - r - 0.03, h * 0.55), (0, 0, 0), "T" + str(i + 1), height=2.6, tint=DARK)
    for (a, c) in (((44.0, 2.0), (138.0, 2.0)), ((138.0, 2.0), (138.0, 70.0)), ((138.0, 70.0), (44.0, 70.0)),
                   ((44.0, 70.0), (44.0, 2.0)), ((44.0, 36.0), (138.0, 36.0))):
        dx, dy = c[0] - a[0], c[1] - a[1]
        ln = math.hypot(dx, dy)
        b.box((ln + 0.5, 0.5, 1.5), ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2, 0.75),
              (0, 0, math.degrees(math.atan2(dy, dx))), mat=CONCRETE, tint=CONCRETE_T)
    for x in (52.0, 100.0):
        pm.concrete_steps(b, (x, 0.4, 0), (0, 0, 0), 2.0, 1.5, 1.2)
    for k, (x, y) in enumerate(((60.0, 18.0), (92.0, 18.0), (60.0, 54.0), (92.0, 54.0))):
        pm.pipe_run(b, [(30.0, y - 3.0 if y < 40 else y + 3.0, 5.6), (x - 12.0, y - 3.0 if y < 40 else y + 3.0, 5.6),
                        (x - 12.0, y - 3.0 if y < 40 else y + 3.0, 1.2), (x - 8.0, y, 1.2)], 0.3,
                    tint=(YELLOW, srgb(0.78, 0.78, 0.76))[k % 2], supports=0.0)
    b.marker("loot", (50.0, 6.0, 0.0), table="fuel-barrel")
    b.marker("loot", (132.0, 66.0, 0.0), table="fuel-barrel")
    b.marker("loot", (108.0, 38.0, 0.0), table="barrel")


def flare(b):
    x, y = FLARE
    H = 70.0
    lattice_tower(b, (x, y, 0), H, 6.0, 1.8, sections=12, member=0.16, tint=srgb(0.62, 0.32, 0.18))
    b.cyl(0.7, H + 2.0, (x, y, (H + 2.0) / 2), tint=srgb(0.5, 0.48, 0.46), sides=12)
    b.cyl(1.0, 3.0, (x, y, H + 3.0), mat=RUST, tint=(1, 1, 1), sides=14)
    b.cyl(1.2, 0.4, (x, y, H + 4.6), tint=DARK, sides=14, solid=False)
    for k, (s, hgt) in enumerate(((1.1, 5.0), (0.7, 7.5), (0.4, 9.0))):
        b.cyl(s, hgt, (x + 0.3 * k, y, H + 4.8 + hgt / 2), mat=LAMP, tint=(srgb(1.0, 0.5, 0.1), srgb(1.0, 0.7, 0.25),
                                                                        srgb(1.0, 0.9, 0.6))[k], sides=10,
              radius2=0.05, solid=False, vary=0)
    b.marker("light", (x, y, H + 8.0), colour=[1.0, 0.55, 0.15], range=400.0, flicker=True)
    b.marker("landmark", (x, y, H + 12.0), kind_of="flare-stack", visible_km=12)
    pm.aviation_lamp(b, (x + 1.2, y, H * 0.6), size=0.35)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        cable(b, (x, y, H * 0.75), (x + math.cos(a) * 40.0, y + math.sin(a) * 40.0, 0.6), sag=0.01, radius=0.03)
        b.box((1.6, 1.6, 1.2), (x + math.cos(a) * 40.0, y + math.sin(a) * 40.0, 0.6), mat=CONCRETE, tint=CONCRETE_T)
    pm.pipe_run(b, [(-110.0, RACK_Y + 3.0, 10.4), (-110.0, y - 6.0, 10.4), (x + 2.0, y - 6.0, 10.4), (x + 2.0, y - 1.0, 2.0)],
                0.6, tint=srgb(0.78, 0.78, 0.76), supports=6.0)
    b.cyl(2.0, 6.0, (-110.0, y - 14.0, 3.0), (90, 0, 0), tint=srgb(0.8, 0.8, 0.78), sides=16)
    b.zone("hazard", (x, y, H + 6.0), radius=12.0, kind_of="flame", damage=40)


def control_room(b):
    cx, cy = -6.0, -60.0
    w, d = 26.0, 14.0
    pm.building(b, (cx, cy, 0), (0, 0, 0), w, d, floors=1, floor_h=4.2, mat=FORMWORK, tint=CONCRETE_T,
                openings={"front": [(-9.0, 1.4, 2.4, 0, 0, {"kind_of": "blast-door", "state": "ajar",
                                                            "tint": srgb(0.5, 0.54, 0.52)}),
                                    (-3.0, 1.0, 0.5, 1.6, 0), (3.0, 1.0, 0.5, 1.6, 0)],
                          "back": [(-6.0, 1.0, 0.5, 1.6, 0)], "left": [(3.0, 1.2, 2.3, 0, 0)]},
                wall_t=0.6, band=(ORANGE, 3.4, 0.4), roof_units=3, glass=0.3, parapet=0.8)
    with b.frame((cx, cy, 0.3)):
        pm.wall(b, (4.0, d / 2 - 0.3, 0), (4.0, -d / 2 + 0.3, 0), 4.2, 0.4,
                [(8.0, 1.2, 2.3, 0, {"kind_of": "room", "chip": "cyan", "cell": True, "room": "lab-vault"})],
                mat=FORMWORK, tint=srgb(0.7, 0.7, 0.68))
        for k, x in enumerate((-10.0, -6.0, -2.0)):
            b.piece("console", (x, 5.6, 0), (0, 0, 180))
        b.piece("console", (-11.5, 0.0, 0), (0, 0, -90))
        b.piece("rack-row", (8.0, 5.8, 0), (0, 0, 180))
        b.piece("rack-row", (11.0, 1.0, 0), (0, 0, 90))
        b.marker("loot", (-4.0, -4.5, 0), table="crate")
        b.marker("loot", (8.0, -4.0, 0), table="sealed-crate")
        b.marker("loot", (11.0, -5.0, 0), table="military-crate")
        b.marker("guard", (-6.0, 0.0, 0), 180.0, role="reclaimer")
        lamp(b, (8.0, 0.0, 3.9), (90, 0, 0), colour=srgb(0.8, 0.95, 1.0), size=(0.6, 0.25, 0.15))
        pm.code_plate(b, (-6.0, -d / 2 - 0.34, 3.0), (0, 0, 0), 3.0, 0.5, base=TEAL, ink=OFFWHITE, text="CR-29")
        pm.mark(b, (6.0, -d / 2 - 0.35, 1.6), (0, 0, 0), 1.5, "skull", pm.GRAFFITI[0])


def loading_and_sulphur(b):
    # The truck loading rack: a canopy over three bays, loading arms, tankers.
    lx, ly = 66.0, -62.0
    for x in (-15.0, -5.0, 5.0, 15.0):
        b.box((0.5, 0.5, 7.0), (lx + x, ly, 3.5), tint=STEEL)
    b.box((34.0, 12.0, 0.4), (lx, ly, 7.2), mat=CORRUGATED, tint=srgb(0.8, 0.8, 0.78))
    b.box((34.4, 0.5, 0.8), (lx, ly - 6.0, 7.0), tint=YELLOW, solid=False, vary=0)
    for k, x in enumerate((-10.0, 0.0, 10.0)):
        b.beam((lx + x, ly, 6.6), (lx + x + 1.5, ly - 3.0, 4.4), 0.25, 0.25, tint=YELLOW)
        b.tube([(lx + x + 1.5, ly - 3.0, 4.4), (lx + x + 1.5, ly - 4.0, 3.2)], 0.12, tint=DARK, sides=8)
        b.piece("utility-truck", (lx + x, ly - 4.2, 0), (0, 0, 90 + (k - 1) * 3), tint=(OFFWHITE, YELLOW, OFFWHITE)[k])
    pm.pipe_run(b, [(lx, RACK_Y - 3.0, 5.4), (lx, ly + 0.5, 5.4), (lx - 12.0, ly + 0.5, 6.4), (lx + 12.0, ly + 0.5, 6.4)],
                0.25, tint=YELLOW, supports=0.0)
    for x in (-14.0, 14.0):
        b.marker("loot", (lx + x, ly + 5.0, 0.0), table="fuel-barrel")
    # The sulphur unit: a reactor, a condenser and yellow sulphur blocks.
    sx, sy = -112.0, -46.0
    b.cyl(2.4, 10.0, (sx, sy, 5.0), tint=srgb(0.7, 0.68, 0.6), sides=18)
    b.cyl(1.2, 9.0, (sx + 7.0, sy, 1.8), (0, 90, 0), tint=srgb(0.76, 0.76, 0.74), sides=14)
    b.tube([(sx, sy, 10.0), (sx + 3.0, sy, 11.0), (sx + 7.0, sy, 3.0)], 0.4, tint=srgb(0.76, 0.76, 0.74), sides=10)
    for k in range(5):
        b.box((4.0 + k * 0.4, 3.0, 1.0 + (k % 3) * 0.6), (sx + 4.0 + k * 4.6, sy - 9.0, 0.5 + (k % 3) * 0.3),
              (0, 0, k * 7.0), mat=MATTE, tint=srgb(0.92, 0.80, 0.18), vary=0.08)
    pm.blob(b, (sx - 6.0, sy - 10.0, 0.0), 4.0, 2.2, srgb(0.9, 0.78, 0.16))
    pm.blob(b, (sx + 26.0, sy - 9.0, 0.0), 3.0, 1.6, srgb(0.9, 0.78, 0.16))
    b.marker("loot", (sx + 2.0, sy - 6.0, 0.0), table="crate", holds="sulphur")
    b.marker("loot", (sx + 16.0, sy - 6.0, 0.0), table="crate", holds="sulphur")
    pm.code_plate(b, (sx, sy - 2.45, 2.6), (0, 0, 0), 1.6, 0.5, base=YELLOW, ink=BLACK, text="S-RU")


def grounds(b):
    rng = b.rng
    hx, hy = 146.0, 96.0
    fence(b, [(7.0, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0), (-hx, -hy, 0), (-7.0, -hy, 0)], post=5.0)
    for x in (-7.0, 7.0):
        b.box((1.0, 1.0, 3.2), (x, -hy, 1.6), mat=CONCRETE, tint=CONCRETE_T)
    pm.code_plate(b, (-10.0, -hy - 0.1, 1.8), (0, 0, 0), 2.6, 0.6, base=YELLOW, ink=BLACK, text="RF-29")
    road(b, [(0.0, -hy - 8.0, 0.0), (0.0, -42.0, 0.0)], width=8.0)
    road(b, [(-130.0, -40.0, 0.0), (140.0, -40.0, 0.0)], width=8.0)
    road(b, [(140.0, -40.0, 0.0), (140.0, 80.0, 0.0)], width=7.0, lines=False)
    shed(b, (-112.0, 18.0, 0), (0, 0, 90), 26.0, 14.0, 7.0, doors=[("front", 0.0, 6.0, 5.0)],
         tint=srgb(0.66, 0.70, 0.70), ruin=0.1)
    b.marker("loot", (-108.0, 12.0, 0.25), table="crate")
    b.marker("loot", (-114.0, 26.0, 0.25), table="barrel")
    b.piece("drum-stack", (-104.0, 30.0, 0), (0, 0, 20))
    for (x, y, a) in ((-90.0, -36.0, 180.0), (-20.0, -36.0, 180.0), (40.0, -36.0, 180.0), (110.0, -36.0, 180.0),
                      (-90.0, 40.0, 0.0), (10.0, 40.0, 0.0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, a))
    for (x, y) in ((20.0, -48.0), (-30.0, -50.0), (100.0, -80.0)):
        b.piece("drum-stack", (x, y, 0), (0, 0, x))
        b.marker("loot", (x + 1.5, y + 1.0, 0.0), table="barrel")
    b.marker("guard", (0.0, -88.0, 0.0), 180.0, role="reclaimer-sentry")
    b.marker("guard", (90.0, 38.0, 0.0), 180.0, role="reclaimer-patrol")
    b.marker("guard", (-100.0, -30.0, 0.0), 180.0, role="reclaimer-patrol")
    # Oily stains and puddles round the process unit.
    for k in range(8):
        x, y = -52.0 + rng.uniform(-28, 28), rng.uniform(-22, 20)
        b.cyl(rng.uniform(1.5, 4.0), 0.02, (x, y, 0.02), mat=pm.GLOSS, tint=srgb(0.04, 0.04, 0.035), sides=14,
              solid=False, vary=0)


def build(b):
    b.set_pad(("rect", 300.0, 200.0), ground="concrete", blend=18.0, clear=8.0)
    process_unit(b)
    pipe_rack(b)
    tank_farm(b)
    flare(b)
    control_room(b)
    loading_and_sulphur(b)
    grounds(b)
    b.zone("no-build", (0, 0, 0), size=(340.0, 240.0, 110.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-industrial", "spawn": "coasts",
                   "contamination": 2})


SUN = (30.0, 215.0)
PEOPLE = [((-2.0, -70.0, 0), 20.0), ((-50.0, 1.0, 6.0), 180.0)]
VIEWS = [
    {"name": "overview", "target": (0, 4, 16), "distance": 300, "yaw": 30, "pitch": 20, "lens": 32},
    {"name": "columns", "target": (-52, 4, 22), "distance": 57.8, "yaw": 36, "pitch": -19.7, "lens": 22},
    {"name": "tanks", "target": (86, 34, 10), "distance": 78, "yaw": 300, "pitch": 8, "lens": 28},
]
