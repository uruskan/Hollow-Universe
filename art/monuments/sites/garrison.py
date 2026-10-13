"""Garrison (MONUMENTS.md §5.3, medium tier): a Bulwark base on strategic ground, held now by Reclaimers. A blast
wall with watchtowers round a parade ground; the command keep (three armoured storeys under a 36 m radio mast, its
silhouette), two barracks, a motor pool of armoured carriers and trucks, a helipad, and the armoury: a gabion-banked
bunker whose blast door takes a cyan chip and a power cell. Military crates and weapons.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, OLIVE, ORANGE, PAINT, RED,
                        YELLOW, cable, ladder, lamp, lattice_tower, railing, rubble, srgb, stairs, tank)
from monkit.structures import road, shed, wall, watchtower
from monkit.surfaces import ASPHALT, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PANEL

KEY = "garrison"
TITLE = "Garrison"
SEED = 3203
BUILDER = {"chunk": 48.0, "max_edge": 4.5}

HX, HY = 110.0, 85.0
KEEP = (0.0, 42.0)
ARMOURY = (-58.0, 50.0)
OLIVE_T = srgb(0.52, 0.56, 0.42)      # olive paint, light enough to read on concrete
GREY_T = srgb(0.74, 0.76, 0.71)       # armoured concrete with a grey-green cast
OLIVE_DARK = pm.BULWARK
SAND = srgb(0.62, 0.56, 0.42)


def perimeter(b):
    for (a, c) in (((7.0, -HY), (HX, -HY)), ((HX, -HY), (HX, HY)), ((HX, HY), (4.5, HY)), ((-4.5, HY), (-HX, HY)),
                   ((-HX, HY), (-HX, -HY)), ((-HX, -HY), (-7.0, -HY))):
        wall(b, (a[0], a[1], 0), (c[0], c[1], 0), height=4.2, thick=0.6, panel=3.0, broken=0.05, tint=GREY_T)
    for (x, y, a) in ((-HX + 5.0, -HY + 5.0, -45.0), (HX - 5.0, -HY + 5.0, 45.0), (HX - 5.0, HY - 5.0, 135.0),
                      (-HX + 5.0, HY - 5.0, 225.0), (-HX + 5.0, 0.0, 270.0), (HX - 5.0, 0.0, 90.0)):
        watchtower(b, (x, y, 0), (0, 0, a), height=8.0, tint=OLIVE_T)
    for m in b.markers:
        if m["kind"] == "guard" and m.get("role") == "sentry":
            m["role"] = "reclaimer-sentry"
    # The front gate: towers of gabions, a barrier, a booth, sandbag nests and a chicane of barriers outside.
    for side in (-1, 1):
        for layer in range(2):
            b.piece("gabion-row", (side * 9.8, -HY + 0.2, layer * 1.4), (0, 0, 0))
        pm.stencil(b, (side * 9.8, -HY - 0.6, 3.2), (0, 0, 0), "B-32", height=0.8, tint=YELLOW, mat=PAINT)
        b.piece("sandbag-nest", (side * 12.0, -HY - 6.0, 0), (0, 0, 180))
        b.marker("guard", (side * 12.0, -HY - 6.5, 0), 180.0, role="reclaimer-sentry")
    with b.frame((-6.5, -HY - 0.5, 1.0), (0, -8.0, 0)):
        for k in range(7):
            b.box((1.0, 0.14, 0.14), (0.5 + k * 1.0, 0, 0), tint=(RED, OFFWHITE)[k % 2], solid=False, vary=0)
    pm.building(b, (-11.0, -HY + 6.0, 0), (0, 0, 0), 3.4, 3.0, floors=1, floor_h=2.8, mat=CONCRETE, tint=OLIVE_T,
                openings={"front": [(0.0, 1.6, 0.6, 1.4, 0)], "right": [(0.0, 0.9, 2.2, 0, 0)],
                          "left": [(0.0, 1.0, 0.6, 1.4, 0)]}, parapet=0.3, roof_units=0, downpipes=False)
    for (x, y, a) in ((-4.0, -HY - 14.0, 0.0), (4.5, -HY - 20.0, 0.0), (-3.5, -HY - 26.0, 0.0)):
        for k in range(2):
            b.piece("jersey-barrier", (x + k * 3.1, y, 0), (0, 0, a))
    b.marker("door", (0, -HY, 0), 180.0, kind_of="gate", width=12.0, height=4.0)
    b.marker("guard", (3.0, -HY + 4.0, 0), 180.0, role="reclaimer")


def keep(b):
    kx, ky = KEEP
    w, d, fh = 30.0, 16.0, 3.4
    slits = lambda fl, xs: [(x, 1.0, 0.45, 1.5, fl) for x in xs]
    info = pm.building(
        b, (kx, ky, 0), (0, 0, 0), w, d, floors=3, floor_h=fh, mat=FORMWORK, tint=GREY_T,
        openings={
            "front": [(-6.0, 1.6, 2.4, 0, 0, {"kind_of": "keep", "state": "ajar", "tint": OLIVE_T})]
                     + slits(0, (-12.0, -9.0, 3.0, 9.0, 12.0)) + slits(1, (-12.0, -6.0, 0.0, 6.0, 12.0))
                     + slits(2, (-12.0, -6.0, 0.0, 6.0, 12.0)),
            "back": slits(0, (-10.0, 0.0, 10.0)) + slits(1, (-10.0, -4.0, 4.0, 10.0)) + slits(2, (-10.0, 0.0, 10.0)),
            "left": slits(0, (0.0,)) + slits(1, (-3.0, 3.0)) + slits(2, (-3.0, 3.0)),
            "right": [(4.0, 1.2, 2.3, 0, 0, {"kind_of": "keep", "state": "shut", "tint": OLIVE_T})]
                     + slits(1, (-3.0, 3.0)) + slits(2, (-3.0, 3.0)),
        },
        wall_t=0.6, plinth=0.4, holes=[(10.6, -6.9, 12.0, -1.6, 1), (10.6, 1.6, 12.0, 6.9, 2),
                                         (-13.0, 5.4, -12.0, 6.4, 3)],
        band=(OLIVE_T, fh * 3 - 0.6, 0.7), roof_units=2, glass=0.0, parapet=1.2)
    f0, f1, f2 = info["floor"]
    roof = info["roof"]
    with b.frame((kx, ky, 0)):
        # Stairs up through the floors at the east end.
        stairs(b, (11.3, -6.8, f0), (0, 0, 0), 1.2, f1 - f0, 5.1)
        stairs(b, (11.3, 6.8, f1), (0, 0, 180), 1.2, f2 - f1, 5.1)
        pm.roof_hatch(b, (-12.5, 5.9), (1.0, 1.0), f2, roof)
        # Rooms: guard room and comms below, offices, the command room at the top.
        pm.wall(b, (-2.0, -d / 2 + 0.3, f0), (-2.0, d / 2 - 0.3, f0), fh - 0.25, 0.25, [(4.0, 1.2, 2.2, 0, None)],
                mat=CONCRETE, tint=srgb(0.6, 0.62, 0.58))
        for x in (3.0, 6.0):
            b.piece("rack-row", (x, 6.8, f0), (0, 0, 180))
        b.piece("weapon-rack", (-13.6, 2.0, f0), (0, 0, -90))
        b.piece("bunk", (-11.0, 6.5, f0), (0, 0, 0))
        b.marker("loot", (-8.0, 5.8, f0), table="crate")
        b.marker("loot", (5.0, -6.0, f0), table="military-crate")
        for x in (-10.0, -4.0, 2.0):
            b.piece("console", (x, 6.8, f1), (0, 0, 180))
        b.piece("crate-stack", (6.0, -6.6, f1), (0, 0, 10))
        b.marker("loot", (-11.0, -6.0, f1), table="crate")
        b.marker("loot", (0.0, -6.5, f1), table="barrel")
        b.box((5.0, 2.6, 0.9), (-4.0, 0.0, f2 + 0.45), tint=srgb(0.35, 0.38, 0.32))
        b.box((4.8, 2.4, 0.03), (-4.0, 0.0, f2 + 0.92), mat=PAINT, tint=srgb(0.62, 0.66, 0.52), solid=False)
        for k in range(6):
            b.box((0.3, 0.2, 0.05), (-5.8 + k * 0.7, -0.5 + (k % 3) * 0.4, f2 + 0.95), tint=(RED, DARK)[k % 2],
                  solid=False, vary=0)
        b.marker("loot", (6.0, 5.8, f2), table="military-crate")
        b.marker("loot", (-12.0, -5.5, f2), table="military-crate")
        b.marker("guard", (0.0, -3.0, f2), 180.0, role="reclaimer")
        b.marker("guard", (-6.0, -3.0, f0), 180.0, role="reclaimer")
        # The blast wall in front of the door, plates and stencils.
        for k in range(3):
            b.piece("sandbag-run", (-6.0 + (k - 1) * 3.0, -d / 2 - 3.2, 0), (0, 0, 0))
        b.piece("sandbag-run", (-10.6, -d / 2 - 1.8, 0), (0, 0, 90))
        lamp(b, (-6.0, -d / 2 - 0.35, 2.9), (0, 0, 0), size=(0.45, 0.25, 0.2))
        pm.stencil(b, (6.0, -d / 2 - 0.32, 5.5), (0, 0, 0), "C2", height=2.2, tint=YELLOW)
        pm.hazard_band(b, (-15.0, -d / 2 - 0.32, 0), (15.0, -d / 2 - 0.32, 0), 0.4, 0.4, width=0.5)
        pm.mark(b, (-1.0, -d / 2 - 0.34, 1.6), (0, 0, 0), 1.6, "slash", pm.GRAFFITI[0])
        # The roof: the radio mast, nests at two corners, aerials.
        lattice_tower(b, (6.0, 2.0, roof), 36.0, 3.2, 0.8, sections=9, member=0.12, tint=srgb(0.55, 0.58, 0.52))
        for z, a in ((roof + 20.0, 0.0), (roof + 28.0, 120.0)):
            for k in range(2):
                ang = math.radians(a + 180 * k)
                b.cyl(0.03, 2.4, (6.0 + math.cos(ang) * 1.2, 2.0 + math.sin(ang) * 1.2, z), (0, 90, a + 180 * k),
                      tint=GREY, sides=4, solid=False)
        b.cyl(0.05, 6.0, (6.0, 2.0, roof + 39.0), tint=GREY, sides=6, solid=False)
        pm.aviation_lamp(b, (6.0, 2.0, roof + 42.0), size=0.4)
        pm.aviation_lamp(b, (6.0 + 1.0, 2.0, roof + 18.0), size=0.3)
        b.marker("landmark", (6.0, 2.0, roof + 42.0), kind_of="radio-mast", visible_km=9)
        for (x, y, a) in ((-12.5, -5.5, 225.0), (12.5, -5.5, 135.0)):
            b.piece("sandbag-nest", (x, y, roof), (0, 0, a))
        b.marker("guard", (-12.0, -5.0, roof), 225.0, role="reclaimer-sentry")
        for x in (-6.0, -2.0):
            b.cyl(0.05, 5.0, (x, 6.0, roof + 2.5), tint=GREY, sides=6, solid=False)


def armoury(b):
    ax, ay = ARMOURY
    w, d = 16.0, 10.0
    with b.frame((ax, ay, 0)):
        b.box((w + 0.7, d + 0.7, 0.4), (0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        pm.wall(b, (-w / 2, -d / 2, 0.4), (w / 2, -d / 2, 0.4), 3.4, 0.8,
                [(w / 2, 2.0, 2.6, 0, {"kind_of": "armoury", "chip": "cyan", "cell": True, "room": "armoury",
                                       "tint": srgb(0.42, 0.45, 0.4)})], mat=FORMWORK, tint=GREY_T)
        pm.wall(b, (w / 2, -d / 2, 0.4), (w / 2, d / 2, 0.4), 3.4, 0.8, [], mat=FORMWORK, tint=GREY_T)
        pm.wall(b, (w / 2, d / 2, 0.4), (-w / 2, d / 2, 0.4), 3.4, 0.8, [], mat=FORMWORK, tint=GREY_T)
        pm.wall(b, (-w / 2, d / 2, 0.4), (-w / 2, -d / 2, 0.4), 3.4, 0.8, [], mat=FORMWORK, tint=GREY_T)
        b.box((w + 0.76, d + 0.76, 1.0), (0, 0, 4.3), mat=FORMWORK, tint=GREY_T)
        # Gabions banked against three sides, two high; a blast porch over the door.
        for side, (x, y, a, n) in enumerate(((0.0, d / 2 + 1.4, 0.0, 3), (-w / 2 - 1.4, 0.0, 90.0, 2),
                                             (w / 2 + 1.4, 0.0, 90.0, 2))):
            for k in range(n):
                off = (k - (n - 1) / 2) * 5.5
                px = x + (off if a == 0.0 else 0.0)
                py = y + (off if a == 90.0 else 0.0)
                b.piece("gabion-row", (px, py, 0), (0, 0, a))
                b.piece("gabion-row", (px, py, 1.4), (0, 0, a))
        for x in (-2.6, 2.6):
            b.box((0.8, 3.0, 3.6), (x, -d / 2 - 1.5, 1.8), mat=FORMWORK, tint=GREY_T)
        b.box((6.0, 3.4, 0.6), (0, -d / 2 - 1.5, 3.9), mat=FORMWORK, tint=GREY_T)
        pm.hazard_band(b, (-2.2, -d / 2 - 0.42, 0), (2.2, -d / 2 - 0.42, 0), 2.9, 0.3)
        lamp(b, (0.0, -d / 2 - 0.45, 3.2), (0, 0, 0), colour=srgb(1.0, 0.3, 0.2), size=(0.4, 0.2, 0.2))
        pm.code_plate(b, (0.0, -d / 2 - 3.25, 4.0), (0, 0, 0), 3.0, 0.5, base=YELLOW, ink=BLACK, text="A-07")
        for k, x in enumerate((-6.0, -2.5, 2.5, 6.0)):
            b.piece("weapon-rack", (x, d / 2 - 0.8, 0.4), (0, 0, 0))
        b.marker("loot", (-6.0, 0.0, 0.4), table="military-crate", holds="weapons")
        b.marker("loot", (-3.0, 0.5, 0.4), table="military-crate")
        b.marker("loot", (3.0, 0.5, 0.4), table="military-crate")
        b.marker("loot", (6.0, -1.0, 0.4), table="sealed-crate")
        lamp(b, (0.0, 0.0, 3.7), (90, 0, 0), size=(0.7, 0.25, 0.15))
        b.marker("guard", (4.0, -d / 2 - 5.0, 0.0), 180.0, role="reclaimer-heavy")


def barracks(b):
    for k, (x, y) in enumerate(((58.0, 30.0), (58.0, 56.0))):
        w, d = 36.0, 10.0
        pm.building(b, (x, y, 0), (0, 0, 0), w, d, floors=1, floor_h=3.4, mat=CONCRETE, tint=OLIVE_T,
                    openings={"front": [(-15.0, 1.3, 2.3, 0, 0), (0.0, 1.3, 2.3, 0, 0)]
                                       + [(xx, 1.5, 1.1, 1.1, 0) for xx in (-11.0, -7.0, -3.5, 3.5, 7.0, 11.0, 15.0)],
                              "back": [(xx, 1.5, 1.1, 1.1, 0) for xx in (-14.0, -10.0, -6.0, -2.0, 2.0, 6.0, 10.0, 14.0)],
                              "left": [(0.0, 1.3, 2.3, 0, 0)], "right": [(0.0, 1.3, 2.3, 0, 0)]},
                    band=(GREY_T, 2.8, 0.4), roof_units=3, glass=0.35, roof_mat=CONCRETE, roof_tint=GREY_T)
        with b.frame((x, y, 0.3)):
            for xx in (-12.0, -8.0, -4.0, 4.0, 8.0, 12.0):
                b.piece("bunk", (xx, 3.6, 0), (0, 0, 0))
                b.piece("bunk", (xx, -3.6, 0), (0, 0, 180))
            b.marker("loot", (-16.0, 3.0, 0), table="crate")
            b.marker("loot", (16.0, -3.0, 0), table=("military-crate", "crate")[k])
            b.marker("guard", (2.0, 0.0, 0), 90.0, role="reclaimer")
            pm.stencil(b, (12.0, -d / 2 - 0.32, 2.4), (0, 0, 0), ("B1", "B2")[k], height=1.2, tint=YELLOW)
        for xx in (-14.0, 0.0, 14.0):
            b.piece("sandbag-run", (x + xx, y - d / 2 - 2.5, 0), (0, 0, 0))


def motor_pool(b):
    mx, my = -62.0, -30.0
    shed(b, (mx, my, 0), (0, 0, 0), 40.0, 16.0, 6.5, roof="mono", pitch=6.0, open_sides=("right",),
         tint=srgb(0.48, 0.52, 0.44), frame_tint=srgb(0.32, 0.34, 0.3), skylights=False, ruin=0.1)
    for k, (x, y, a, piece, t) in enumerate(((-12.0, -3.0, 90.0, "apc", OLIVE_T), (-4.0, -3.0, 90.0, "apc", GREY_T),
                                             (6.0, -3.0, 90.0, "utility-truck", OLIVE_T),
                                             (13.0, 3.0, 95.0, "utility-truck", SAND))):
        b.piece(piece, (mx + x, my + y, 0.25), (0, 0, a), tint=t)
    b.piece("apc", (mx + 30.0, my - 14.0, 0), (0, 0, 35), tint=srgb(0.14, 0.13, 0.12))
    rubble(b, (mx + 30.0, my - 14.0, 0.0), 4.0, count=8, tint=srgb(0.2, 0.19, 0.18), mat=PAINT, max_size=0.9)
    b.cyl(5.0, 0.03, (mx + 30.0, my - 14.0, 0.02), mat=MATTE, tint=srgb(0.1, 0.09, 0.09), sides=20, solid=False,
          vary=0)
    for (x, y) in ((-16.0, 6.0), (-10.0, 6.5), (15.0, -6.5)):
        b.piece("drum-stack", (mx + x, my + y, 0.25), (0, 0, x * 7))
    pm.barricade(b, (mx + 2.0, my + 6.5, 0.25), (0, 0, 0), 5.0)
    b.marker("loot", (mx - 16.0, my + 3.0, 0.25), table="crate")
    b.marker("loot", (mx - 6.0, my + 6.0, 0.25), table="barrel")
    b.marker("loot", (mx + 10.0, my + 6.0, 0.25), table="fuel-barrel")
    b.marker("guard", (mx + 22.0, my, 0.0), 90.0, role="reclaimer-patrol")
    # The fuel point and storage containers.
    tank(b, (-94.0, -58.0, 0), 3.0, 5.0, tint=srgb(0.45, 0.5, 0.42), band=YELLOW, ladder_side=None)
    b.box((1.0, 0.8, 1.8), (-88.0, -54.0, 0.9), tint=YELLOW, bevel=0.04)
    b.tube([(-88.0, -53.6, 1.4), (-87.0, -52.0, 0.2), (-85.0, -50.0, 0.1)], 0.05, tint=BLACK, sides=6)
    b.marker("loot", (-90.0, -50.0, 0.0), table="fuel-barrel")
    for i in range(4):
        b.piece("container-20", (-94.0 + (i % 2) * 0.2, -16.0 + i * 3.0 if i < 3 else -13.0, 0 if i < 3 else 2.59),
                (0, 0, 0), tint=(OLIVE_T, GREY_T, SAND, OLIVE_T)[i])


def grounds(b):
    rng = b.rng
    # Parade ground, flagpole, roads, gabion lines.
    b.box((50.0, 34.0, 0.1), (0.0, -24.0, 0.05), mat=ASPHALT, tint=(1, 1, 1))
    for k in range(5):
        b.box((0.2, 30.0, 0.012), (-20.0 + k * 10.0, -24.0, 0.106), tint=srgb(0.85, 0.83, 0.75), solid=False, vary=0,
              wear=0.7)
    pm.flag(b, (0.0, -4.0, 0), 12.0, tint=OLIVE_T, w=2.4, h=1.4)
    b.box((2.0, 2.0, 0.6), (0.0, -4.0, 0.3), mat=CONCRETE, tint=CONCRETE_T)
    road(b, [(0.0, -HY - 30.0, 0.0), (0.0, -41.0, 0.0)], width=8.0)
    road(b, [(0.0, -7.0, 0.0), (0.0, KEEP[1] - 12.0, 0.0)], width=7.0, lines=False)
    road(b, [(-26.0, -30.0, 0.0), (-40.0, -30.0, 0.0)], width=8.0, lines=False)
    for side in (-1, 1):
        for k in range(4):
            b.piece("gabion-row", (side * 6.5, -HY + 12.0 + k * 6.0, 0), (0, 0, 90))
    for k in range(4):
        b.piece("gabion-row", (-16.0 + k * 11.0, KEEP[1] - 14.0, 0), (0, 0, 0))
    # The helipad.
    hx, hy = 62.0, -42.0
    b.cyl(12.0, 0.25, (hx, hy, 0.125), mat=CONCRETE, tint=srgb(0.66, 0.66, 0.62), sides=32)
    b.lathe([(9.6, 0.26), (10.4, 0.26), (10.4, 0.27), (9.6, 0.27), (9.6, 0.26)], (hx, hy, 0), tint=YELLOW, sides=32,
            close=False, solid=False, vary=0, wear=0.6)
    for a in (45.0, -45.0):
        b.box((8.0, 1.2, 0.02), (hx, hy, 0.265), (0, 0, a), tint=OFFWHITE, solid=False, vary=0, wear=0.6)
    for k in range(8):
        ang = math.radians(22.5 + 45 * k)
        b.box((0.4, 0.4, 0.3), (hx + math.cos(ang) * 11.4, hy + math.sin(ang) * 11.4, 0.4), mat=LAMP,
              tint=srgb(1.0, 0.75, 0.3), solid=False, vary=0)
    b.piece("sandbag-nest", (hx + 16.0, hy + 8.0, 0), (0, 0, 90))
    b.cyl(0.05, 5.0, (hx - 14.0, hy - 6.0, 2.5), tint=GREY, sides=6, solid=False)
    b.cyl(0.35, 1.6, (hx - 13.2, hy - 6.0, 4.6), (0, 80, 0), mat=PAINT, tint=ORANGE, sides=8, radius2=0.15,
          solid=False)
    # A water tank, lamp posts, nests, debris.
    tank(b, (90.0, 72.0, 0), 3.0, 5.0, tint=GREY_T, band=YELLOW, ladder_side=180.0, legs=8.0)
    for (x, y, a) in ((-30.0, -50.0, 180.0), (30.0, -50.0, 180.0), (-30.0, 10.0, 0.0), (30.0, 10.0, 0.0),
                      (-80.0, 20.0, 90.0), (84.0, 0.0, -90.0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, a))
    for (x, y, a) in ((-40.0, 70.0, 0.0), (40.0, -70.0, 180.0), (-90.0, -70.0, 225.0)):
        b.piece("sandbag-nest", (x, y, 0), (0, 0, a))
    b.marker("guard", (-40.0, 69.0, 0.0), 0.0, role="reclaimer")
    rubble(b, (HX - 2.0, 40.0, 0.0), 4.0, count=10, tint=GREY_T, max_size=1.4)
    for (x, y, r) in ((24.0, -64.0, 3.0), (-70.0, 74.0, 2.5), (86.0, -20.0, 3.5)):
        b.cyl(r, 0.02, (x, y, 0.02), mat=MATTE, tint=srgb(0.16, 0.14, 0.12), sides=18, solid=False, vary=0)
        rubble(b, (x, y, 0.0), r, count=6, tint=srgb(0.4, 0.37, 0.32), mat=MATTE, max_size=0.6)
    b.marker("loot", (26.0, 8.0, 0.0), table="barrel")
    b.marker("loot", (-28.0, 12.0, 0.0), table="crate")


def tents(b):
    """A row of field tents by the barracks: olive canvas on frames, cots inside."""
    for k in range(4):
        x, y = 30.0 + k * 9.0, -12.0
        pm.tarp_shelter(b, (x, y, 0), (0, 0, 0), 6.0, 5.0, h_front=2.4, h_back=2.4, tint=(OLIVE_DARK, OLIVE_T)[k % 2],
                        sag=0.0)
        grid = [[(x - 3.15 + 6.3 * i / 2, y - 2.7 + 5.4 * j / 2, 2.4 + 0.9 * (1 - abs(j - 1))) for i in range(3)]
                for j in range(3)]
        pm.sheet(b, grid, tint=(OLIVE_DARK, OLIVE_T)[k % 2])
        for side in (-1, 1):
            b.box((6.0, 0.04, 2.3), (x, y + side * 2.5, 1.15), mat=MATTE, tint=(OLIVE_DARK, OLIVE_T)[k % 2],
                  solid=False, vary=0.05)
        for j in range(2):
            b.box((0.8, 1.9, 0.4), (x - 1.5 + j * 3.0, y, 0.3), mat=MATTE, tint=pm.KHAKI, solid=False)
        b.marker("loot", (x + 0.0, y + 1.5, 0.0), table=("crate", "barrel")[k % 2])


def build(b):
    b.set_pad(("rect", 250.0, 200.0), ground="gravel", blend=16.0, clear=8.0)
    perimeter(b)
    keep(b)
    armoury(b)
    barracks(b)
    motor_pool(b)
    grounds(b)
    tents(b)
    b.zone("no-build", (0, 0, 0), size=(290.0, 240.0, 90.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "bulwark", "spawn": "strategic-ground"})


SUN = (30.0, 220.0)
PEOPLE = [((3.0, -96.0, 0), 15.0), ((ARMOURY[0] + 3.0, ARMOURY[1] - 9.0, 0), 160.0)]
VIEWS = [
    {"name": "overview", "target": (0, 6, 8), "distance": 240, "yaw": 32, "pitch": 25, "lens": 32},
    {"name": "gate", "target": (0, -70, 7), "distance": 40, "yaw": 0, "pitch": -6, "lens": 24},
    {"name": "armoury", "target": (-54, 46, 4), "distance": 30, "yaw": 34, "pitch": -3, "lens": 26},
]
