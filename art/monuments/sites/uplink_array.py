"""Uplink Array (MONUMENTS.md §5.3, medium tier): one of the Seedward relay stations that talked to the colony
fleets until the Quiet. A 60 m dish on an az-el alidade over its equipment drum, three smaller dishes (one fallen),
a two-storey control block, a transformer yard, a generator shed and a lattice mast, all inside a security fence on
a plateau. The control block's amber door opens a records room; behind it the cyan room (chip and power cell) keeps
the sealed crate. The dish's old emitters still leak (contamination 1); a ladder up the alidade reaches the bearing
platforms 29 m up.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, fence, ladder, lamp, lattice_tower, railing, rubble, srgb, stairs)
from monkit.structures import dish, road, shed
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, PANEL

KEY = "uplink-array"
TITLE = "Uplink Array"
SEED = 2702
BUILDER = {"chunk": 48.0, "max_edge": 4.0}

DX, DY = -42.0, 18.0        # the big dish's drum
DRUM_R, DRUM_H = 10.0, 9.0
ROOF = DRUM_H + 0.4
CB = (12.0, -18.0)          # the control block
CW, CD = 28.0, 14.0


def control_block(b):
    f_h = 3.6
    info = pm.building(
        b, (CB[0], CB[1], 0), (0, 0, 0), CW, CD, floors=2, floor_h=f_h, mat=PANEL, tint=OFFWHITE,
        openings={
            "front": [(-9.5, 1.4, 2.3, 0, 0, {"kind_of": "entrance", "state": "ajar"}),
                      (-12.4, 1.4, 1.3, 1.0, 0), (-6.6, 1.4, 1.3, 1.0, 0)]
                     + [(x, 1.8, 1.4, 0.9, 1) for x in (-11.0, -7.0, -3.0, 1.0, 5.0, 9.0, 12.4)],
            "back": [(-10.0, 1.4, 1.3, 1.0, 0), (-6.0, 1.4, 1.3, 1.0, 0)]
                    + [(x, 2.6, 1.8, 0.8, 1) for x in (-10.5, -6.0, -1.5, 3.0, 7.5, 11.8)],
            "left": [(0.0, 1.4, 1.3, 1.0, 0), (-3.0, 1.6, 1.4, 0.9, 1), (3.0, 1.6, 1.4, 0.9, 1)],
            "right": [(-3.0, 1.6, 1.4, 0.9, 1), (3.0, 1.6, 1.4, 0.9, 1)],
        },
        holes=[(-13.4, 4.8, -7.8, 6.2, 1), (-12.6, -6.1, -11.4, -4.9, 2)],
        band=(ORANGE, f_h + 0.1, 0.45), roof_units=3, glass=0.5, door_tint=srgb(0.36, 0.5, 0.52))
    f0, f1 = info["floor"]
    roof = info["roof"]
    with b.frame((CB[0], CB[1], 0), (0, 0, 0)):
        # Rounded corners and a teal band under the parapet: the old civil livery.
        for x in (-CW / 2, CW / 2):
            for y in (-CD / 2, CD / 2):
                b.cyl(0.42, roof - 0.3, (x, y, 0.3 + (roof - 0.3) / 2), mat=PANEL, tint=OFFWHITE, sides=16)
        for y in (-CD / 2 - 0.17, CD / 2 + 0.17):
            b.box((CW + 0.3, 0.03, 0.35), (0, y, roof - 0.55), tint=TEAL, solid=False, vary=0, wear=0.5)
        # Big louvres over the equipment hall.
        with b.frame((1.0, -CD / 2 - 0.2, 0)):
            b.box((9.2, 0.12, 2.8), (0, 0.02, 2.0), tint=DARK, solid=False, vary=0)
            for k in range(11):
                b.box((9.0, 0.28, 0.08), (0, -0.08, 0.75 + k * 0.24), (38, 0, 0), tint=LIGHTGREY, solid=False,
                      vary=0)
        # Entrance canopy, plates and marks.
        b.box((3.4, 2.2, 0.2), (-9.5, -CD / 2 - 1.1, 3.0), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        for x in (-11.0, -8.0):
            b.box((0.14, 0.14, 2.7), (x, -CD / 2 - 2.0, 1.65), tint=DARK, vary=0)
        lamp(b, (-9.5, -CD / 2 - 0.25, 2.75), (0, 0, 0), size=(0.45, 0.25, 0.18))
        pm.code_plate(b, (-12.1, -CD / 2 - 0.19, 2.0), (0, 0, 0), 1.3, 0.42, base=TEAL, ink=OFFWHITE, text="UA-3")
        pm.stencil(b, (-CW / 2 - 0.17, 3.0, 2.0), (0, 0, -90), "C1", height=1.6, tint=TEAL)
        pm.mark(b, (5.0, -CD / 2 - 0.2, 1.3), (0, 0, 0), 1.6, "slash", pm.GRAFFITI[0])
        pm.mark(b, (CW / 2 + 0.2, -2.0, 1.4), (0, 0, 90), 1.3, "skull", pm.GRAFFITI[3])
        b.marker("guard", (-9.5, -CD / 2 - 3.5, 0.3), 180.0, role="reclaimer")
        # Ground floor: hall (stair up), equipment hall, the amber room, the cyan room behind it.
        ph = f_h
        pm.wall(b, (-4.0, -CD / 2 + 0.15, f0), (-4.0, CD / 2 - 0.15, f0), ph, 0.2, [(6.85, 2.2, 2.4, 0, None)],
                mat=PANEL, tint=srgb(0.82, 0.82, 0.8))
        pm.wall(b, (6.0, CD / 2 - 0.15, f0), (6.0, -CD / 2 + 0.15, f0), ph, 0.3,
                [(10.35, 1.2, 2.3, 0, {"kind_of": "room", "chip": "amber", "room": "amber"})],
                mat=CONCRETE, tint=srgb(0.78, 0.78, 0.76), door_tint=srgb(0.55, 0.45, 0.25))
        pm.wall(b, (6.15, 0.0, f0), (CW / 2 - 0.15, 0.0, f0), ph, 0.3,
                [(3.85, 1.2, 2.3, 0, {"kind_of": "room", "chip": "cyan", "cell": True, "room": "cyan"})],
                mat=CONCRETE, tint=srgb(0.78, 0.78, 0.76), door_tint=srgb(0.25, 0.42, 0.48))
        stairs(b, (-13.3, 5.5, f0), (0, 0, -90), 1.2, f1 - f0, 5.4)
        pm.roof_hatch(b, (-12.0, -5.5), (1.0, 1.0), f1, roof)
        for y, rot in ((-4.6, 0), (4.6, 180)):
            for x in (-1.9, 0.6, 3.1):
                b.piece("rack-row", (x, y, f0), (0, 0, rot))
        b.marker("loot", (-12.5, -4.5, f0), table="barrel")
        b.marker("loot", (4.6, -1.5, f0), table="crate")
        # Amber room: a records room.
        b.piece("console", (12.2, -3.2, f0), (0, 0, -90))
        b.piece("crate-stack", (7.3, -5.9, f0), (0, 0, 8))
        b.marker("loot", (9.0, -5.8, f0), table="military-crate")
        b.marker("loot", (12.6, -1.0, f0), table="crate")
        lamp(b, (10.0, -3.5, f0 + ph - 0.25), (90, 0, 0), size=(0.6, 0.2, 0.12))
        # Cyan room: the relay core and the sealed crate.
        b.piece("rack-row", (8.5, 6.2, f0), (0, 0, 180))
        b.piece("rack-row", (11.3, 6.2, f0), (0, 0, 180))
        b.cyl(0.8, 2.4, (12.8, 2.0, f0 + 1.2), tint=srgb(0.3, 0.32, 0.34), sides=16)
        b.cyl(0.82, 0.12, (12.8, 2.0, f0 + 1.9), mat=LAMP, tint=pm.CHIP["cyan"], sides=16, solid=False, vary=0)
        b.marker("loot", (9.5, 3.0, f0), table="sealed-crate")
        b.marker("loot", (7.2, 1.2, f0), table="military-crate")
        lamp(b, (10.0, 3.5, f0 + ph - 0.25), (90, 0, 0), colour=srgb(0.8, 0.95, 1.0), size=(0.6, 0.2, 0.12))
        # Upper floor: the control room facing the dish.
        for x in (-9.0, -5.0, -1.0, 3.0, 7.0):
            b.piece("console", (x, 5.6, f1), (0, 0, 180))
        b.piece("crate-stack", (12.3, -5.6, f1), (0, 0, -12))
        b.marker("loot", (10.5, -5.5, f1), table="crate")
        b.marker("loot", (-2.0, -5.6, f1), table="crate")
        b.marker("loot", (12.8, 1.0, f1), table="barrel")
        b.marker("guard", (2.0, 0.0, f1), 0.0, role="reclaimer")
        # Roof: a small dish, a whip antenna, the hatch.
        dish(b, (8.0, 2.0, roof), diameter=3.2, elevation=35.0, azimuth=200.0, mount_h=1.4, ribs=8,
             panels_missing=0.0)
        b.cyl(0.08, 7.0, (-4.0, 4.5, roof + 3.5), tint=GREY, sides=6, solid=False)
        pm.aviation_lamp(b, (-4.0, 4.5, roof + 7.0), size=0.25)


def big_dish_site(b):
    pm.drum_building(b, (DX, DY, 0), DRUM_R, DRUM_H,
                     doors=[(-35.0, 2.4, {"kind_of": "equipment", "state": "ajar"})], band=ORANGE)
    # Inside the drum: the azimuth king post, racks, loot.
    b.cyl(2.0, DRUM_H, (DX, DY, DRUM_H / 2), mat=CONCRETE, tint=srgb(0.7, 0.7, 0.68), sides=20)
    for k, a in enumerate((60.0, 100.0, 140.0, 180.0, 220.0, 260.0)):
        ar = math.radians(a)
        b.piece("rack-row", (DX + math.cos(ar) * 8.3, DY + math.sin(ar) * 8.3, 0), (0, 0, a + 90))
    b.marker("loot", (DX + 5.0, DY - 2.0, 0), table="crate")
    b.marker("loot", (DX - 4.0, DY - 5.5, 0), table="barrel")
    lamp(b, (DX + math.cos(math.radians(-35)) * 10.25, DY + math.sin(math.radians(-35)) * 10.25, 2.9),
         (0, 0, -35 + 90), size=(0.45, 0.25, 0.2))
    pm.stencil(b, (DX + math.cos(math.radians(-62)) * 10.23, DY + math.sin(math.radians(-62)) * 10.23, 4.2),
               (0, 0, -62 + 90), "D1", height=2.2, tint=TEAL)
    # Stair up the west side and a bridge onto the roof; rails round the roof.
    top, run = pm.switchback(b, (DX - 12.2, DY - 4.0, 0), (0, 0, 0), ROOF, width=1.0, flight_rise=2.4)
    pm.deck(b, DX - 11.1, DY - 5.2, DX - 8.8, DY - 4.0, ROOF, rails=("front", "back"))
    pm.ring_rail(b, (DX, DY), DRUM_R + 0.15, ROOF, sides=28, gaps=(16,))
    # The dish.
    out = pm.big_dish(b, (DX, DY, 0), diameter=60.0, elevation=48.0, azimuth=25.0, axis_h=31.0, base_z=ROOF,
                      yoke=7.0, leg=4.8, missing=0.07)
    with b.frame((DX, DY, 0), (0, 0, 25.0)):
        pz = 31.0 - 1.6 + 0.075
        for s in (-1, 1):
            xa, xb = s * 5.8, s * 9.0
            ladder(b, (s * 8.2, -2.2, ROOF), pz - ROOF, (0, 0, 0), cage=True)
            railing(b, (xa, -2.0, pz), (s * 7.7, -2.0, pz))
            railing(b, (s * 8.7, -2.0, pz), (xb, -2.0, pz))
            railing(b, (xb, -2.0, pz), (xb, 2.0, pz))
            railing(b, (xb, 2.0, pz), (xa, 2.0, pz))
        b.marker("loot", (7.6, 1.0, pz), table="crate")
        b.marker("guard", (0.0, -3.0, ROOF), 180.0, role="reclaimer-sentry")
    pm.aviation_lamp(b, tuple(out["rim_top"]), size=0.45)
    b.marker("landmark", tuple(out["rim_top"]), kind_of="uplink-dish", visible_km=10)
    # Panels fallen from the dish.
    for k in range(7):
        x = DX + 14.0 + b.rng.uniform(-6, 8)
        y = DY - 18.0 + b.rng.uniform(-6, 6)
        b.box((2.6, 1.9, 0.12), (x, y, 0.25), (b.rng.uniform(-12, 12), b.rng.uniform(-12, 12), b.rng.uniform(0, 360)),
              mat=PANEL, tint=OFFWHITE, solid=False)
    b.zone("radiation", (DX, DY, 22.0), radius=34.0, level=1, source="emitters")


def small_dishes(b):
    before = len(b.markers)
    for (x, y, d, el, az) in ((22.0, 50.0, 16.0, 38.0, 15.0), (50.0, 54.0, 13.0, 58.0, -20.0),
                              (76.0, 47.0, 11.0, 14.0, 40.0)):
        dish(b, (x, y, 0), diameter=d, elevation=el, azimuth=az, panels_missing=0.12)
        b.box((5.0, 5.0, 0.3), (x, y, 0.15), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68))
        b.piece("rack-row", (x + d * 0.35, y - d * 0.3, 0), (0, 0, 90))
    # Keep the big dish the only landmark.
    b.markers[before:] = [m for m in b.markers[before:] if m["kind"] != "landmark"]
    # The fallen one: its pedestal snapped, the reflector on its rim in the gravel.
    b.cyl(1.6, 3.0, (82.0, 14.0, 1.5), mat=CONCRETE, tint=CONCRETE_T, sides=20)
    rubble(b, (82.0, 14.0, 0.0), 3.5, count=8, tint=CONCRETE_T, max_size=1.0)
    with b.frame((88.0, 8.0, 6.2), (72.0, 0.0, 130.0)):
        pm.paraboloid_panels(b, 7.0, 14.0 * 0.4, 0.0, rings=5, sectors=24, missing=0.2)
        for j in range(10):
            a = 2 * math.pi * j / 10
            b.tube([(rr * math.cos(a), rr * math.sin(a), rr * rr / (4 * 5.6) - 0.3) for rr in (1.0, 3.0, 5.0, 7.0)],
                   0.1, tint=GREY, sides=5)
        b.cyl(1.0, 1.0, (0, 0, -0.5), tint=GREY, sides=14)
    b.marker("loot", (85.0, 10.0, 0.0), table="crate")
    b.marker("loot", (48.0, 47.0, 0.0), table="barrel")
    b.marker("loot", (26.0, 43.0, 0.0), table="barrel")
    b.marker("guard", (50.0, 40.0, 0.0), 180.0, role="reclaimer-patrol")


def yard(b):
    """The transformer yard (front left), the generator shed and its fuel tank (west)."""
    x0, x1, y0, y1 = -70.0, -40.0, -60.0, -42.0
    b.box((x1 - x0, y1 - y0, 0.08), ((x0 + x1) / 2, (y0 + y1) / 2, 0.04), mat=CONCRETE,
          tint=srgb(0.62, 0.6, 0.56), solid=False, wear=0.6)
    fence(b, [(x1, -48.0, 0), (x1, y0, 0), (x0, y0, 0), (x0, y1, 0), (x1, y1, 0), (x1, -45.0, 0)], post=3.5)
    b.piece("transformer", (-63.0, -51.5, 0))
    b.piece("transformer", (-55.0, -51.5, 0), tint=srgb(0.32, 0.30, 0.28))
    for x in (-64.0, -58.0, -52.0):
        b.piece("breaker", (x, -45.8, 0))
    b.piece("gantry", (-56.0, -57.0, 0))
    pm.building(b, (-46.0, -55.0, 0), (0, 0, 90), 4.0, 3.2, floors=1, floor_h=3.0, mat=CONCRETE, tint=CONCRETE_T,
                openings={"front": [(0.0, 1.0, 2.2, 0, 0)]}, parapet=0.3, roof_units=0, downpipes=False)
    b.marker("loot", (-44.0, -50.0, 0.0), table="barrel")
    b.marker("loot", (-68.0, -44.0, 0.0), table="barrel")
    b.marker("loot", (-46.0, -55.0, 0.3), table="crate")
    pm.cable_trench(b, [(-40.0, -46.5, 0), (-6.0, -46.5, 0), (-6.0, -26.0, 0)])
    # Generator shed and the day tank.
    shed(b, (-86.0, -18.0, 0), (0, 0, 90), 16.0, 10.0, 5.5, doors=[("front", 0.0, 4.0, 3.8)],
         tint=srgb(0.66, 0.70, 0.70), ruin=0.15)
    with b.frame((-86.0, -18.0, 0), (0, 0, 90)):
        for x in (-4.0, 3.5):
            b.box((5.0, 2.2, 2.2), (x, 1.0, 1.35), tint=srgb(0.28, 0.42, 0.36), bevel=0.04)
            b.box((1.2, 2.0, 1.6), (x + 3.0, 1.0, 1.05), tint=DARK)
            b.cyl(0.25, 3.5, (x - 1.5, 2.5, 5.5), tint=DARK, sides=8, solid=False)
        b.marker("loot", (-6.5, -3.0, 0.25), table="fuel-barrel")
        b.marker("loot", (6.5, -3.5, 0.25), table="fuel-barrel")
    for x in (-91.0, -86.0):
        b.box((0.6, 3.4, 1.0), (x, -36.0, 0.5), mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(1.5, 8.0, (-88.5, -36.0, 2.3), (0, 90, 0), tint=srgb(0.82, 0.83, 0.8), sides=20)
    b.box((8.2, 0.05, 0.5), (-88.5, -37.52, 2.3), tint=ORANGE, solid=False, vary=0)
    pm.code_plate(b, (-88.5, -37.56, 3.05), (0, 0, 0), 1.6, 0.4, base=YELLOW, ink=BLACK, text="F-2")
    b.piece("drum-stack", (-80.0, -33.0, 0), (0, 0, 15))
    b.piece("drum-stack", (-78.6, -34.4, 0), (0, 0, -10))


def mast(b):
    x, y = 52.0, -24.0
    lattice_tower(b, (x, y, 0), 38.0, 3.6, 1.0, sections=8, member=0.12, tint=srgb(0.7, 0.71, 0.71))
    for z, a in ((24.0, 200.0), (24.0, 290.0), (31.0, 230.0)):
        ar = math.radians(a)
        hw = (3.6 + (1.0 - 3.6) * z / 38.0) / 2
        with b.frame((x + math.cos(ar) * (hw + 0.6), y + math.sin(ar) * (hw + 0.6), z), (0, 0, a + 90)):
            b.cyl(0.9, 0.7, (0, 0, 0), (90, 0, 0), tint=LIGHTGREY, sides=18)
            b.cyl(0.92, 0.05, (0, -0.37, 0), (90, 0, 0), tint=OFFWHITE, sides=18, solid=False, vary=0)
    b.cyl(0.06, 5.0, (x, y, 40.5), tint=GREY, sides=6, solid=False)
    pm.aviation_lamp(b, (x, y, 43.0), size=0.35)
    pm.aviation_lamp(b, (x + 0.9, y, 22.0), size=0.3)
    pm.building(b, (x + 5.0, y + 5.0, 0), (0, 0, 0), 4.0, 3.0, floors=1, floor_h=2.8, mat=PANEL, tint=OFFWHITE,
                openings={"front": [(0.0, 1.0, 2.2, 0, 0)]}, parapet=0.3, roof_units=0, downpipes=False,
                band=(ORANGE, 2.2, 0.3))
    for k in range(3):
        cable(b, (x + 3.0, y + 5.0, 2.6), (x + 0.3, y + 0.3, 6.0 + k * 0.4), sag=0.08, radius=0.03)


def grounds(b):
    rng = b.rng
    hx, hy = 106.0, 76.0
    fence(b, [(6.0, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0), (-hx, -hy, 0), (-6.0, -hy, 0)], post=4.0)
    # Gate: posts, a broken barrier arm, a guard booth.
    for x in (-6.0, 6.0):
        b.box((1.0, 1.0, 3.2), (x, -hy, 1.6), mat=CONCRETE, tint=CONCRETE_T)
        pm.hazard_band(b, (x - 0.5, -hy - 0.52, 0), (x + 0.5, -hy - 0.52, 0), 2.4, 0.5)
    with b.frame((-5.4, -hy - 0.8, 1.1), (0, -18.0, 0)):
        for k in range(8):
            b.box((1.0, 0.14, 0.14), (0.5 + k * 1.0, 0, 0), tint=(RED, OFFWHITE)[k % 2], solid=False, vary=0)
    pm.building(b, (-11.0, -hy + 4.0, 0), (0, 0, 0), 3.4, 3.0, floors=1, floor_h=2.8, mat=PANEL, tint=OFFWHITE,
                openings={"front": [(0.6, 1.4, 1.1, 1.1, 0)], "right": [(0.0, 0.9, 2.2, 0, 0)],
                          "back": [(0.0, 1.4, 1.1, 1.1, 0)], "left": [(0.0, 1.4, 1.1, 1.1, 0)]},
                parapet=0.25, roof_units=0, downpipes=False, band=(TEAL, 2.2, 0.3))
    b.marker("guard", (-8.0, -hy + 1.0, 0.0), 180.0, role="reclaimer-sentry")
    # Roads.
    road(b, [(0.0, -hy - 8.0, 0.0), (0.0, -27.0, 0.0)], width=6.0)
    road(b, [(-3.0, -36.0, 0.0), (-33.0, -36.0, 0.0), (-33.0, 4.0, 0.0)], width=5.0, lines=False)
    pm.cable_trench(b, [(CB[0] - CW / 2, CB[1] + 3.0, 0), (-24.0, CB[1] + 3.0, 0), (-24.0, 8.0, 0),
                        (DX + 8.6, 8.0, 0)])
    pm.cable_trench(b, [(CB[0] + 4.0, CB[1] + CD / 2, 0), (CB[0] + 4.0, 38.0, 0), (76.0, 38.0, 0)])
    # Lamps, a truck, crates and drums, debris.
    for (x, y, a) in ((-6.0, -60.0, 180.0), (-30.0, -20.0, 90.0), (32.0, -4.0, -90.0), (40.0, 30.0, 0.0),
                      (-62.0, 42.0, 45.0), (88.0, -50.0, 180.0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, a))
    b.piece("utility-truck", (-10.0, -30.0, 0), (0, 0, 75), tint=srgb(0.92, 0.9, 0.86))
    b.piece("utility-truck", (-28.0, 12.0, 0), (0, 0, -4), tint=srgb(0.85, 0.45, 0.15))
    b.piece("cable-reel", (-22.0, 16.0, 0), (0, 0, 30))
    b.piece("cable-reel", (-19.5, 17.2, 0), (0, 0, 75))
    b.piece("crate-stack", (30.0, -30.0, 0), (0, 0, 20))
    b.piece("jersey-barrier", (-4.0, -hy + 6.0, 0), (0, 0, 70))
    b.piece("jersey-barrier", (4.5, -hy + 7.0, 0), (0, 0, 100))
    b.marker("loot", (31.5, -31.5, 0.0), table="barrel")
    b.marker("loot", (-24.0, 14.0, 0.0), table="crate")
    rubble(b, (-14.0, 30.0, 0.0), 3.0, count=8, tint=CONCRETE_T, max_size=0.9)
    rubble(b, (64.0, -6.0, 0.0), 2.5, count=6, tint=CONCRETE_T, max_size=0.8)
    for k in range(10):
        pm.shrub(b, (rng.uniform(-100, 100), rng.uniform(-70, 70), 0.0), rng.uniform(0.6, 1.2),
                 tints=(pm.LEAF_DRY, pm.LEAF3, pm.LEAF2))


def squat(b):
    """Reclaimers have moved into the back corner: scrap walls, tarps, a brazier, strung lights."""
    cx, cy = -84.0, 44.0
    pm.scrap_wall(b, (cx - 12.0, cy - 6.0, 0), (cx - 12.0, cy + 12.0, 0))
    pm.scrap_wall(b, (cx - 12.0, cy + 12.0, 0), (cx + 10.0, cy + 12.0, 0))
    pm.scrap_wall(b, (cx + 10.0, cy + 12.0, 0), (cx + 10.0, cy + 3.0, 0))
    pm.tarp_shelter(b, (cx - 7.0, cy + 7.5, 0), (0, 0, 0), 5.0, 4.0, tint=pm.TARP_BLUE)
    pm.tarp_shelter(b, (cx + 2.0, cy + 8.0, 0), (0, 0, 6), 6.0, 4.5, tint=pm.TARP_GREEN, tattered=True)
    pm.tarp_shelter(b, (cx - 8.5, cy - 1.0, 0), (0, 0, 90), 4.0, 3.2, tint=pm.TARP_ORANGE)
    b.piece("fire-barrel", (cx - 1.0, cy + 1.0, 0))
    pm.string_lights(b, (cx - 12.0, cy + 6.0, 2.8), (cx + 10.0, cy + 9.0, 2.9))
    pm.string_lights(b, (cx - 12.0, cy - 2.0, 2.7), (cx + 4.0, cy + 12.0, 3.0))
    pm.barricade(b, (cx + 4.0, cy - 8.0, 0), (0, 0, -15), 7.0)
    b.piece("container-20", (cx + 15.0, cy + 2.0, 0), (0, 0, 92), tint=srgb(0.55, 0.3, 0.2))
    b.piece("container-20", (cx + 15.2, cy + 2.2, 2.59), (0, 0, 87), tint=srgb(0.35, 0.47, 0.55))
    b.piece("crate-stack", (cx - 9.0, cy + 9.0, 0), (0, 0, 30))
    b.piece("drum-stack", (cx + 6.0, cy + 9.5, 0), (0, 0, 5))
    b.marker("loot", (cx - 5.5, cy + 9.5, 0.0), table="crate")
    b.marker("loot", (cx + 3.5, cy + 10.0, 0.0), table="barrel")
    b.marker("guard", (cx + 1.0, cy + 3.0, 0.0), 180.0, role="reclaimer")
    b.marker("guard", (cx + 4.0, cy - 10.0, 0.0), 180.0, role="reclaimer-sentry")


def utilities(b):
    """Power poles from the yard to the control block and the drum; the landing ring; staff hab cans."""
    from monkit.structures import hab_module
    lines = [[(-38.0, -40.0, 0), (-26.0, -40.0, 0), (-14.0, -40.0, 0), (-4.0, -32.0, 0)],
             [(-30.0, -28.0, 0), (-30.0, -14.0, 0), (-30.0, 0.0, 0)]]
    for line in lines:
        prev = None
        for i, p in enumerate(line):
            nxt = line[min(i + 1, len(line) - 1)]
            d = (nxt[0] - p[0], nxt[1] - p[1]) if i < len(line) - 1 else (p[0] - line[i - 1][0], p[1] - line[i - 1][1])
            yaw = math.degrees(math.atan2(d[1], d[0])) + 90
            pts = pm.power_pole(b, p, rot=(0, 0, yaw))
            if prev:
                for a, c in zip(prev, pts):
                    cable(b, tuple(a), tuple(c), sag=0.02, radius=0.015)
            prev = pts
    # The landing ring: a pad for the service shuttles.
    x, y = 70.0, -50.0
    b.cyl(11.0, 0.25, (x, y, 0.125), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.7), sides=32)
    b.lathe([(8.6, 0.26), (9.4, 0.26), (9.4, 0.27), (8.6, 0.27), (8.6, 0.26)], (x, y, 0), mat=PAINT, tint=ORANGE,
            sides=32, close=False, solid=False, vary=0, wear=0.6)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.box((3.0, 0.5, 0.02), (x + math.cos(a) * 4.5, y + math.sin(a) * 4.5, 0.265), (0, 0, math.degrees(a)),
              tint=TEAL, solid=False, vary=0, wear=0.6)
        b.box((0.4, 0.4, 0.3), (x + math.cos(a) * 10.4, y + math.sin(a) * 10.4, 0.4), mat=LAMP,
              tint=srgb(1.0, 0.8, 0.3), solid=False, vary=0)
    b.cyl(0.05, 5.0, (x + 13.0, y + 6.0, 2.5), tint=GREY, sides=6, solid=False)
    b.cyl(0.35, 1.6, (x + 13.8, y + 6.0, 4.6), (0, 80, 0), mat=PAINT, tint=ORANGE, sides=8, radius2=0.15,
          solid=False)
    for i, xx in enumerate((82.0, 88.0, 94.0)):
        hab_module(b, (xx, -18.0 + i * 1.0, 0), (0, 0, 90), length=9.0, tint=OFFWHITE if i != 1 else srgb(0.85, 0.86, 0.84),
                   band=(ORANGE, TEAL, ORANGE)[i])
    b.marker("loot", (85.0, -24.0, 0.0), table="crate")
    b.marker("loot", (91.0, -11.0, 0.0), table="barrel")
    b.piece("container-20", (-70.0, -2.0, 0), (0, 0, 2), tint=srgb(0.82, 0.82, 0.78))
    b.piece("container-20", (-70.2, 1.0, 0), (0, 0, -3), tint=srgb(0.72, 0.42, 0.2))


def relay_hall(b):
    """A high-bay hall of relay racks between the control block and the small dishes."""
    hx, hy, hw, hd = 46.0, 12.0, 36.0, 12.0
    pm.building(b, (hx, hy, 0), (0, 0, 0), hw, hd, floors=1, floor_h=6.2, mat=PANEL, tint=srgb(0.86, 0.87, 0.85),
                openings={"front": [(-10.0, 4.0, 4.2, 0, 0, {"kind_of": "shutter", "state": "gone"}),
                                    (6.0, 1.2, 2.3, 0, 0), (12.0, 2.4, 1.0, 4.4, 0), (0.0, 2.4, 1.0, 4.4, 0)],
                          "back": [(-12.0, 2.4, 1.0, 4.4, 0), (0.0, 2.4, 1.0, 4.4, 0), (12.0, 2.4, 1.0, 4.4, 0)],
                          "left": [(0.0, 1.2, 2.3, 0, 0)]},
                band=(TEAL, 5.0, 0.6), roof_units=4, glass=0.35, parapet=0.6)
    with b.frame((hx, hy, 0), (0, 0, 0)):
        for x in (-hw / 2, hw / 2):
            for y in (-hd / 2, hd / 2):
                b.cyl(0.42, 6.6, (x, y, 3.6), mat=PANEL, tint=srgb(0.86, 0.87, 0.85), sides=16)
        for k, x in enumerate((-4.0, -1.0, 2.0, 5.0, 8.0, 11.0, 14.0)):
            if k == 3:
                continue
            for y, rot in ((-2.2, 0), (2.4, 180)):
                b.piece("rack-row", (x, y, 0.3), (0, 0, rot))
        with b.frame((14.0, -hd / 2 - 0.2, 0)):
            b.box((5.0, 0.12, 3.0), (0, 0.02, 2.2), tint=DARK, solid=False, vary=0)
            for k in range(12):
                b.box((4.8, 0.28, 0.08), (0, -0.08, 0.85 + k * 0.24), (38, 0, 0), tint=LIGHTGREY, solid=False,
                      vary=0)
        pm.stencil(b, (-6.0, -hd / 2 - 0.18, 4.6), (0, 0, 0), "R2", height=1.4, tint=TEAL)
        pm.mark(b, (-1.5, -hd / 2 - 0.2, 1.4), (0, 0, 0), 1.4, "ring", pm.GRAFFITI[1])
        b.piece("utility-truck", (-10.0, -10.0, 0), (0, 0, 98), tint=srgb(0.92, 0.9, 0.86))
        b.marker("loot", (-14.0, 3.0, 0.3), table="crate")
        b.marker("loot", (9.5, 0.0, 0.3), table="barrel")
        b.marker("loot", (-16.0, -4.0, 0.3), table="barrel")
    before = len(b.markers)
    for k, (x, el, az) in enumerate(((62.0, 30.0, 10.0), (69.0, 45.0, -5.0), (76.0, 20.0, 25.0), (83.0, 60.0, 0.0))):
        dish(b, (x, 28.0, 0), diameter=4.4, elevation=el, azimuth=az, mount_h=2.2, ribs=8, panels_missing=0.1)
    b.markers[before:] = [m for m in b.markers[before:] if m["kind"] != "landmark"]


def build(b):
    b.set_pad(("rect", 224.0, 164.0), ground="gravel", blend=16.0, clear=6.0)
    control_block(b)
    big_dish_site(b)
    small_dishes(b)
    yard(b)
    mast(b)
    grounds(b)
    squat(b)
    utilities(b)
    relay_hall(b)
    b.zone("no-build", (0, 0, 0), size=(260.0, 200.0, 90.0))
    # The small dishes' own landmark markers go: the big dish is the site's landmark.
    b.markers = [m for m in b.markers if m["kind"] != "landmark" or m.get("kind_of") == "uplink-dish"]
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-civil", "spawn": "plateaus",
                   "contamination": 1})


SUN = (32.0, 225.0)
PEOPLE = [((1.5, -40.0, 0), 20.0), ((DX + 6.6, DY + 3.6, 29.48), 200.0)]
VIEWS = [
    {"name": "overview", "target": (-2, 2, 14), "distance": 205, "yaw": 34, "pitch": 21, "lens": 32},
    {"name": "gate", "target": (-6, -30, 13), "distance": 62, "yaw": 2, "pitch": -10, "lens": 24},
    {"name": "alidade", "target": (-40, 16, 30), "distance": 78, "yaw": 128, "pitch": 6, "lens": 30},
]
