"""Drainage Works (MONUMENTS.md §5.3, medium tier): the storm-water pumping station below a colony town. The works
stand on a deck 7 m above the outfall: a tall gabled pump house with three vertical pumps and an overhead crane,
whose discharge mains run out over the edge and down the headwall; a 32 m surge tower (the silhouette); switchgear.
Under the deck are the drains: three box culverts from the headwall's arched mouths to a collector gallery, the wet
well beneath the pumps (crossed on a grated walkway: the parkour route), the town's main sewer coming in through a
barred arch on the north, and a valve gallery behind an amber door. Below the headwall, a stilling basin with baffle
blocks spills into a concrete flood channel. Water in the renders is look-dev only.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, ladder, lamp, railing, rubble, srgb, stairs)
from monkit.structures import road
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PANEL, RUST

KEY = "drainage-works"
TITLE = "Drainage Works"
SEED = 3434
BUILDER = {"chunk": 48.0, "max_edge": 4.0}

UP = 7.0                  # the deck's surface
CULVERTS = (-20.0, 0.0, 20.0)
CW, CH = 4.0, 4.0         # culvert inside width and height
GY0, GY1 = 28.0, 32.0     # the collector gallery's inside (y)
WELL = (-12.0, 12.0, 32.5, 43.4)
DX0, DX1, DY0, DY1 = -60.0, 60.0, 0.0, 44.0   # the deck
CONC = srgb(0.74, 0.74, 0.71)
OLD = srgb(0.62, 0.46, 0.38)      # the pump house's old brick-red render


def headwall(b):
    """The outfall headwall along the deck's south edge: three arched culvert mouths, flap gates, a parapet."""
    t = 0.9
    edges = [DX0] + [e for c in CULVERTS for e in (c - CW / 2, c + CW / 2)] + [DX1]
    for k in range(0, len(edges), 2):
        x0, x1 = edges[k], edges[k + 1]
        if x1 > 44.0 > x0:
            for (a, c_, h) in ((x0, 44.0, UP + 1.1), (44.0, 46.4, UP), (46.4, x1, UP + 1.1)):
                b.box((c_ - a, t, h), ((a + c_) / 2, 0.0, h / 2), mat=FORMWORK, tint=CONC)
            continue
        b.box((x1 - x0, t, UP + 1.1), ((x0 + x1) / 2, 0.0, (UP + 1.1) / 2), mat=FORMWORK, tint=CONC)
    for c in CULVERTS:
        b.box((CW, t, UP + 1.1 - CH), (c, 0.0, CH + (UP + 1.1 - CH) / 2), mat=FORMWORK, tint=CONC)
        ring = [(c + (CW / 2 + 0.4) * math.cos(math.pi * i / 12), -t / 2 - 0.05, CH - 1.0 + (1.0 + 0.4) * math.sin(math.pi * i / 12))
                for i in range(13)]
        b.tube(ring, 0.22, mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62), sides=6)
        for sx in (-1, 1):
            b.box((0.5, 0.4, CH - 1.0), (c + sx * (CW / 2 + 0.4), -t / 2 - 0.2, (CH - 1.0) / 2), mat=CONCRETE,
                  tint=srgb(0.66, 0.65, 0.62))
        # A flap gate hinged at the top of the mouth: one shut, one hanging open, one gone.
        k = CULVERTS.index(c)
        if k != 2:
            ang = 0.0 if k == 0 else 38.0
            with b.frame((c, -t / 2 - 0.15, CH - 0.1), (ang, 0, 0)):
                b.box((CW - 0.2, 0.12, CH - 0.2), (0, 0, -(CH - 0.2) / 2), mat=RUST, tint=(1, 1, 1))
                for x in (-1.2, 0.0, 1.2):
                    b.box((0.15, 0.2, CH - 0.4), (x, -0.12, -(CH - 0.2) / 2), mat=RUST, tint=(1, 1, 1), solid=False)
                b.cyl(0.12, CW, (0, 0.0, 0.0), (0, 90, 0), tint=DARK, sides=8, solid=False)
        pm.stencil(b, (c, -t / 2 - 0.02, CH + 1.8), (0, 0, 0), "C" + str(k + 1), height=0.9, tint=DARK)
    for (a, c_) in ((DX0 - 0.2, 44.0), (46.4, DX1 + 0.2)):
        b.box((c_ - a, t + 0.3, 0.25), ((a + c_) / 2, 0.0, UP + 1.22), mat=CONCRETE, tint=srgb(0.82, 0.81, 0.78),
              solid=False)
    for k in range(int((DX1 - DX0) / 8.0)):
        x = DX0 + 4.0 + k * 8.0
        b.box((1.2, 0.06, 0.2), (x, -t / 2 - 0.03, 1.5 + (k % 3) * 0.1), tint=srgb(0.3, 0.28, 0.22), solid=False,
              vary=0.2, wear=0.9)
    pm.mark(b, (-40.0, -t / 2 - 0.02, 2.4), (0, 0, 0), 2.6, "slash", pm.GRAFFITI[2])
    pm.mark(b, (34.0, -t / 2 - 0.02, 2.0), (0, 0, 0), 2.2, "ring", pm.GRAFFITI[1])
    pm.mark(b, (-8.0, -t / 2 - 0.02, 5.8), (0, 0, 0), 1.6, "arrow", pm.GRAFFITI[3])
    pm.code_plate(b, (48.0, -t / 2 - 0.05, 5.2), (0, 0, 0), 3.0, 0.8, base=OFFWHITE, ink=DARK, text="DW-34")


def underground(b):
    """The drains under the deck: culverts, the collector gallery, the wet well, the valve gallery, earth fill."""
    wall = srgb(0.6, 0.6, 0.57)
    for c in CULVERTS:
        for sx in (-1, 1):
            b.box((0.5, GY0 - 0.45, CH + 0.5), (c + sx * (CW / 2 + 0.25), (GY0 + 0.45) / 2, (CH + 0.5) / 2),
                  mat=FORMWORK, tint=wall)
        b.box((CW + 1.0, GY0 - 0.45, 0.5), (c, (GY0 + 0.45) / 2, CH + 0.25), mat=CONCRETE, tint=wall)
        b.box((CW, GY0, 0.25), (c, GY0 / 2, 0.125), mat=CONCRETE, tint=srgb(0.45, 0.44, 0.41), wear=0.9)
        for k in range(4):
            y = 4.0 + k * 7.0
            b.box((0.25, 0.25, 0.3), (c + CW / 2 - 0.1, y, CH - 0.5), mat=LAMP, tint=srgb(1.0, 0.6, 0.25), solid=False,
                  vary=0)
        b.marker("light", (c, 14.0, CH - 0.6), colour=[1.0, 0.6, 0.25], range=10.0)
    # The collector gallery along the culverts' heads, into the well and on east to the valve gallery.
    gx0, gx1 = CULVERTS[0] - CW / 2 - 0.5, 33.0
    pm.wall(b, (gx0, GY0 - 0.25, 0.0), (gx1, GY0 - 0.25, 0.0), CH + 0.5, 0.5,
            [(c - gx0, CW, CH, 0, None) for c in CULVERTS], mat=FORMWORK, tint=wall, extend=False)
    pm.wall(b, (gx1, GY1 + 0.25, 0.0), (gx0, GY1 + 0.25, 0.0), CH + 0.5, 0.5, [(gx1 - 0.0, 8.0, CH, 0, None)],
            mat=FORMWORK, tint=wall, extend=False)
    pm.wall(b, (gx0, GY1 + 0.5, 0.0), (gx0, GY0 - 0.5, 0.0), CH + 0.5, 0.5, [], mat=FORMWORK, tint=wall,
            extend=False)
    pm.wall(b, (gx1, GY0 - 0.5, 0.0), (gx1, GY1 + 0.5, 0.0), CH + 0.5, 0.5, [], mat=FORMWORK, tint=wall,
            extend=False)
    pm.slab_holes(b, gx0, GY0 - 0.5, gx1, GY1 + 0.5, CH, 0.5, [(-10.6, 29.4, -9.4, 30.6), (13.4, 29.4, 14.6, 30.6)],
                  mat=CONCRETE, tint=wall)
    b.box((gx1 - gx0, GY1 - GY0, 0.25), ((gx0 + gx1) / 2, (GY0 + GY1) / 2, 0.125), mat=CONCRETE,
          tint=srgb(0.45, 0.44, 0.41), wear=0.9)
    # The valve gallery behind its amber door: valves on a header, the military crate.
    pm.wall(b, (24.5, GY1, 0.25), (24.5, GY0, 0.25), CH, 0.4,
            [(2.0, 1.2, 2.3, 0, {"kind_of": "valve-gallery", "chip": "amber", "room": "valves"})],
            mat=FORMWORK, tint=wall, extend=False, door_tint=srgb(0.5, 0.42, 0.25))
    pm.pipe_run(b, [(25.5, GY1 - 0.6, 1.4), (32.5, GY1 - 0.6, 1.4)], 0.35, tint=TEAL)
    for x in (27.0, 30.0):
        b.cyl(0.55, 0.3, (x, GY1 - 0.6, 2.2), (90, 0, 0), tint=RED, sides=12, solid=False)
        b.cyl(0.05, 0.9, (x, GY1 - 0.6, 1.9), tint=DARK, sides=6, solid=False)
    b.marker("loot", (29.0, GY0 + 0.8, 0.25), table="military-crate")
    # The wet well: tall walls under the pump house, a grated walkway across it, ladders, the pumps' suction pipes,
    # the screen across the sewer inlet.
    wx0, wx1, wy0, wy1 = WELL
    for x in (wx0 - 0.25, wx1 + 0.25):
        b.box((0.5, wy1 - wy0 + 0.5, UP - 0.5), (x, (wy0 + wy1) / 2, (UP - 0.5) / 2), mat=FORMWORK, tint=wall)
    b.box((wx1 - wx0, wy1 - wy0, 0.25), ((wx0 + wx1) / 2, (wy0 + wy1) / 2, 0.125), mat=CONCRETE,
          tint=srgb(0.36, 0.35, 0.33), wear=0.95)
    pm.deck(b, wx0, 33.4, wx1, 34.6, 3.0, rails=("back",))
    ladder(b, (-8.0, 34.65, 0.0), 3.0, (0, 0, 180), cage=False)
    for x in (-6.0, 0.0, 6.0):
        b.cyl(0.6, UP - 1.5, (x, 38.0, 1.0 + (UP - 1.5) / 2), tint=srgb(0.36, 0.42, 0.48), sides=14)
        b.lathe([(0.6, 0.0), (1.0, -0.6), (1.0, -0.8), (0.0, -0.8)], (x, 38.0, 1.0), mat=PAINT,
                tint=srgb(0.36, 0.42, 0.48), sides=14)
    for k in range(14):
        b.cyl(0.04, 4.4, (-3.25 + k * 0.5, wy1 - 0.4, 2.2), tint=DARK, sides=5, solid=False, vary=0)
    b.box((7.0, 0.2, 0.2), (0.0, wy1 - 0.4, 4.4), tint=DARK, solid=False)
    b.marker("loot", (9.0, 41.5, 0.25), table="crate")
    b.marker("guard", (-4.0, 30.0, 0.25), 90.0, role="reclaimer")
    # Earth fill between the drains and over them, under the deck.
    fill = srgb(0.42, 0.38, 0.32)
    h = UP - 0.5
    pm.slab_holes(b, DX0, 0.45, DX1, GY1 + 0.5, CH + 0.5, h - CH - 0.5,
                  [(-10.6, 29.4, -9.4, 30.6), (13.4, 29.4, 14.6, 30.6)], mat=MATTE, tint=fill)
    for (x0, x1, y0, y1) in ((DX0, CULVERTS[0] - CW / 2 - 0.5, 0.45, GY0 - 0.5),
                             (CULVERTS[0] + CW / 2 + 0.5, CULVERTS[1] - CW / 2 - 0.5, 0.45, GY0 - 0.5),
                             (CULVERTS[1] + CW / 2 + 0.5, CULVERTS[2] - CW / 2 - 0.5, 0.45, GY0 - 0.5),
                             (CULVERTS[2] + CW / 2 + 0.5, DX1, 0.45, GY0 - 0.5),
                             (DX0, gx0, GY0 - 0.5, DY1), (gx0, wx0 - 0.5, GY1 + 0.5, DY1),
                             (wx1 + 0.5, DX1, GY1 + 0.5, DY1), (gx1, DX1, GY0 - 0.5, GY1 + 0.5)):
        b.box((x1 - x0, y1 - y0, h), ((x0 + x1) / 2, (y0 + y1) / 2, h / 2), mat=MATTE, tint=fill, vary=0.0)


def deck(b):
    """The deck over the drains with its retaining walls, manholes, rails, the sewer inlet arch on the north."""
    holes = [(-10.6, 29.4, -9.4, 30.6), (13.4, 29.4, 14.6, 30.6), (9.3, 34.6, 10.7, 40.2)]
    pm.slab_holes(b, DX0, DY0 + 0.45, DX1, DY1, UP - 0.5, 0.5, holes, mat=CONCRETE, tint=srgb(0.68, 0.67, 0.64))
    for (x, y) in ((-10.0, 30.0), (14.0, 30.0)):
        b.cyl(0.75, 0.12, (x, y, UP + 0.06), tint=DARK, sides=12, cap=False, solid=False)
        ladder(b, (x, y + 0.45, 0.25), UP - 0.25, (0, 0, 180), cage=False)
        with b.frame((x + 1.1, y, UP), (0, 0, 0)):
            b.cyl(0.62, 0.05, (0, 0, 0.03), tint=DARK, sides=12, solid=False)
    for (a, c) in (((DX0, DY0), (DX0, DY1)), ((DX1, DY1), (DX1, DY0))):
        b.box((0.6, DY1 - DY0, UP + 1.0), (a[0], (DY0 + DY1) / 2, (UP + 1.0) / 2), mat=FORMWORK, tint=CONC)
    # The north wall with the town's sewer coming in under an arch, behind bars.
    t = 0.6
    for (x0, x1) in ((DX0, -3.0), (3.0, DX1)):
        b.box((x1 - x0, t, UP + 1.0), ((x0 + x1) / 2, DY1, (UP + 1.0) / 2), mat=FORMWORK, tint=CONC)
    b.box((6.0, t, UP + 1.0 - 4.6), (0.0, DY1, 4.6 + (UP + 1.0 - 4.6) / 2), mat=FORMWORK, tint=CONC)
    ring = [(3.4 * math.cos(math.pi * i / 12), DY1 + t / 2 + 0.05, 3.0 + 1.6 * math.sin(math.pi * i / 12)) for i in range(13)]
    b.tube(ring, 0.25, mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62), sides=6)
    for k in range(10):
        b.cyl(0.05, 4.4, (-2.7 + k * 0.6, DY1 + 0.1, 2.2), mat=RUST, tint=(1, 1, 1), sides=5, solid=False, vary=0)
    b.box((6.2, 0.2, 0.25), (0.0, DY1 + 0.1, 4.45), mat=RUST, tint=(1, 1, 1), solid=False)
    b.box((7.0, 3.0, 0.3), (0.0, DY1 + 1.5, 0.15), mat=CONCRETE, tint=srgb(0.4, 0.39, 0.36), solid=False)
    pm.mark(b, (12.0, DY1 + t / 2 + 0.02, 2.4), (0, 0, 180), 2.2, "skull", pm.GRAFFITI[0])
    # Rails round the deck's edges.
    for (a, c) in (((DX0 + 0.4, DY1 - 0.3), (DX0 + 0.4, 0.6)), ((DX1 - 0.4, 0.6), (DX1 - 0.4, DY1 - 0.3)),
                   ((DX1 - 0.4, DY1 - 0.3), (DX0 + 0.4, DY1 - 0.3))):
        railing(b, (a[0], a[1], UP), (c[0], c[1], UP), tint=YELLOW)


def pump_house(b):
    w, d, H = 30.0, 14.0, 11.0
    px, py = 0.0, 36.0
    with b.frame((px, py, UP)):
        info = pm.building(b, (0, 0, 0), (0, 0, 0), w, d, floors=1, floor_h=H, mat=CONCRETE, tint=OLD,
                           openings={"front": [(x, 2.0, 5.0, 3.0, 0) for x in (-10.5, -3.5, 3.5, 10.5)],
                                     "back": [(x, 2.0, 5.0, 3.0, 0) for x in (-10.5, -3.5, 3.5, 10.5)],
                                     "right": [(-1.5, 4.0, 4.5, 0, 0, {"kind_of": "pump-hall", "state": "gone"}),
                                               (4.0, 1.2, 2.3, 0, 0)],
                                     "left": [(0.0, 2.0, 4.0, 4.0, 0)]},
                           plinth=0.0, parapet=0.0, roof_units=0, glass=0.55, holes=[], downpipes=True,
                           frame=srgb(0.2, 0.24, 0.24))
        pm.slab_holes(b, -w / 2, -d / 2, w / 2, d / 2, 0.0, 0.3, [(9.3, -1.4, 10.7, 4.2)], mat=CONCRETE,
                      tint=srgb(0.6, 0.6, 0.57))
        top = info["roof"]
        # A pitched roof of sheet on steel trusses, a ridge vent, pilasters on the long walls.
        rise = 3.2
        for side in (-1, 1):
            ang = math.degrees(math.atan2(rise, d / 2 + 0.6))
            ln = math.hypot(d / 2 + 0.6, rise)
            b.box((w + 1.0, ln, 0.12), (0, side * (d / 2 + 0.6) / 2, top + rise / 2 + 0.05), (side * -ang, 0, 0),
                  mat=CORRUGATED, tint=srgb(0.38, 0.42, 0.42))
        b.extrude([(-d / 2, 0.0), (d / 2, 0.0), (0.0, rise)], 0.4, (-w / 2, 0, top), (0, 0, 90), mat=CONCRETE,
                  tint=OLD, solid=False)
        b.extrude([(-d / 2, 0.0), (d / 2, 0.0), (0.0, rise)], 0.4, (w / 2, 0, top), (0, 0, 90), mat=CONCRETE,
                  tint=OLD, solid=False)
        b.box((w * 0.6, 1.2, 1.0), (0, 0, top + rise + 0.4), mat=CORRUGATED, tint=srgb(0.5, 0.52, 0.52))
        for x in (-14.0, -7.0, 0.0, 7.0, 14.0):
            for y in (-d / 2 - 0.2, d / 2 + 0.2):
                b.box((0.8, 0.4, H + 0.2), (x, y, 0.3 + (H + 0.2) / 2), mat=CONCRETE, tint=srgb(0.56, 0.4, 0.33))
        b.box((w + 0.6, d + 0.6, 0.5), (0, 0, top - 0.1), mat=CONCRETE, tint=srgb(0.82, 0.8, 0.76), solid=False)
        pm.stencil(b, (-w / 2 - 0.22, 0.0, 9.0), (0, 0, -90), "P-34", height=1.6, tint=OFFWHITE)
        # Inside: three vertical pumps with motors, the discharge mains, an overhead crane.
        for k, x in enumerate((-6.0, 0.0, 6.0)):
            b.box((2.6, 2.6, 0.6), (x, 2.0, 0.6), mat=CONCRETE, tint=CONCRETE_T)
            b.cyl(0.8, 1.6, (x, 2.0, 1.7), tint=srgb(0.36, 0.42, 0.48), sides=16)
            b.cyl(1.0, 2.4, (x, 2.0, 3.7), tint=srgb(0.36, 0.42, 0.48) if k != 1 else srgb(0.55, 0.3, 0.2), sides=16)
            b.cyl(0.7, 0.4, (x, 2.0, 5.1), tint=DARK, sides=14)
            for j in range(8):
                a = 2 * math.pi * j / 8
                b.box((0.06, 0.3, 2.0), (x + math.cos(a) * 1.02, 2.0 + math.sin(a) * 1.02, 3.7), (0, 0, math.degrees(a)),
                      tint=DARK, solid=False, vary=0)
            pm.pipe_run(b, [(x, 1.0, 1.7), (x, -2.5, 1.7), (x, -d / 2 - 1.0, 1.7)], 0.55, tint=srgb(0.3, 0.42, 0.5),
                        flanges=True)
            b.cyl(0.9, 0.12, (x, -3.6, 2.9), (0, 0, 0), tint=RED, sides=14, cap=False, solid=False)
            b.cyl(0.08, 1.2, (x, -3.6, 2.3), tint=DARK, sides=6, solid=False)
        for y in (-d / 2 + 0.6, d / 2 - 0.6):
            b.box((w - 1.0, 0.4, 0.6), (0, y, 8.6), tint=YELLOW)
        b.box((1.0, d - 1.2, 0.9), (4.0, 0, 9.1), tint=YELLOW)
        b.box((1.4, 1.4, 0.8), (4.0, 1.0, 8.4), tint=DARK)
        cable(b, (4.0, 1.0, 8.0), (4.0, 1.0, 5.6), sag=0.0, radius=0.03)
        b.box((0.6, 0.4, 0.3), (4.0, 1.0, 5.4), tint=YELLOW, solid=False)
        b.piece("console", (-11.0, 5.0, 0.3), (0, 0, 180))
        b.piece("rack-row", (-12.6, -4.0, 0.3), (0, 0, 90))
        b.marker("loot", (11.0, 4.5, 0.3), table="crate")
    # The stair from the hall down to the well walkway (through the deck).
    stairs(b, (10.0, 34.6, 3.0), (0, 0, 0), 1.2, UP + 0.3 - 3.0, 5.6)
    # The discharge mains across the deck and down the headwall into the basin.
    for k, x in enumerate((-6.0, 0.0, 6.0)):
        xe = (-10.0, 8.0, 12.0)[k]
        pts = [(x, 28.0, UP + 1.7), (x, 6.0, UP + 1.7), (xe, 3.0, UP + 1.7), (xe, -0.9, UP + 1.7), (xe, -1.4, UP + 0.6),
               (xe, -1.4, 1.6), (xe, -3.6, 1.0)]
        pm.pipe_run(b, pts, 0.55, tint=srgb(0.3, 0.42, 0.5), flanges=True, supports=0.0)
        for y in (24.0, 16.0, 9.0):
            b.box((1.8, 0.6, 1.2), (x, y, UP + 0.6), mat=CONCRETE, tint=CONCRETE_T)
        for z in (2.6, 5.2):
            b.box((1.4, 1.0, 0.2), (xe, -0.95, z), tint=DARK, solid=False)


def surge_tower(b):
    x, y = -38.0, 30.0
    H = 32.0
    b.cyl(5.0, 1.0, (x, y, UP + 0.5), mat=CONCRETE, tint=CONCRETE_T, sides=24)
    b.cyl(4.0, H, (x, y, UP + 1.0 + H / 2), mat=FORMWORK, tint=CONC, sides=28, radius2=3.6)
    for z, tint in ((UP + 8.0, OLD), (UP + 20.0, OLD)):
        r = 4.0 - 0.4 * (z - UP - 1.0) / H + 0.03
        b.cyl(r, 1.0, (x, y, z), tint=tint, sides=28, solid=False, vary=0)
    zt = UP + 1.0 + H
    pm.ring_deck(b, (x, y), 3.6, 5.0, zt, sides=24)
    pm.ring_rail(b, (x, y), 4.9, zt, sides=24, gaps=(18,))
    for k in range(12):
        a = 2 * math.pi * (k + 0.5) / 12
        b.beam((x + math.cos(a) * 3.62, y + math.sin(a) * 3.62, zt - 1.6), (x + math.cos(a) * 4.9, y + math.sin(a) * 4.9,
                                                                           zt - 0.1), 0.15, 0.15, tint=DARK)
    b.lathe([(3.9, 0.0), (3.9, 0.3), (0.4, 3.0), (0.0, 3.1)], (x, y, zt), mat=PAINT, tint=srgb(0.36, 0.42, 0.42),
            sides=28)
    b.cyl(0.5, 1.2, (x, y, zt + 3.6), tint=DARK, sides=10, solid=False)
    ladder(b, (x, y - 5.25, UP + 1.0), H, (0, 0, 0), cage=True)
    for z in range(4, int(H), 6):
        rz = 4.0 - 0.4 * z / H
        b.beam((x, y - rz, UP + 1.0 + z), (x, y - 5.2, UP + 1.0 + z), 0.12, 0.12, tint=DARK)
    pm.aviation_lamp(b, (x, y, zt + 4.2), size=0.35)
    pm.stencil(b, (x, y - 3.85, UP + 14.0), (0, 0, 0), "D-34", height=2.6, tint=DARK)
    pm.pipe_run(b, [(-15.2, 33.0, UP + 2.0), (-28.0, 33.0, UP + 2.0), (-33.6, 31.5, UP + 2.0)], 0.6,
                tint=srgb(0.3, 0.42, 0.5), flanges=True, supports=4.0)
    b.marker("loot", (x + 4.2, y, zt), table="crate")
    b.marker("landmark", (x, y, zt + 4.2), kind_of="surge-tower", visible_km=7)


def yard_level(b):
    """The rest of the deck: switchgear with transformers, the recycler, lamps, a truck, the stair down."""
    pm.building(b, (36.0, 36.0, UP), (0, 0, 0), 12.0, 7.0, floors=1, floor_h=4.0, mat=CONCRETE, tint=CONC,
                openings={"front": [(-3.0, 1.2, 2.3, 0, 0)], "back": [(2.0, 1.6, 0.8, 2.6, 0)]},
                band=(TEAL, 3.2, 0.3), roof_units=1, plinth=0.2)
    for x in (30.0, 40.0):
        b.piece("transformer", (x, 24.0, UP), (0, 0, 0))
    from monkit.kit import fence
    fence(b, [(26.0, 20.0, UP), (46.0, 20.0, UP), (46.0, 28.5, UP), (26.0, 28.5, UP), (26.0, 22.5, UP)], height=2.4,
          post=3.0)
    pm.lean_to(b, (-46.0, 10.0, UP), (0, 0, 0), 7.0, 5.0, h_front=3.2, h_back=3.6, missing=0.1)
    b.box((3.4, 2.4, 0.2), (-46.0, 10.0, UP + 0.1), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68))
    b.marker("recycler", (-46.0, 10.0, UP + 0.2), 180.0)
    b.piece("utility-truck", (40.0, 8.0, UP), (0, 0, 20), tint=OFFWHITE)
    b.piece("drum-stack", (50.0, 14.0, UP), (0, 0, 30))
    b.marker("loot", (51.5, 12.5, UP), table="barrel")
    for (x, y, a) in ((-54.0, 4.0, 180.0), (54.0, 40.0, 0.0), (20.0, 4.0, 180.0)):
        b.piece("lamp-post", (x, y, UP), (0, 0, a))
    # The stair down the headwall's face from the deck to the basin side.
    with b.frame((32.0, -2.0, 0.0)):
        stairs(b, (0.0, 0.0, 0.0), (0, 0, -90), 1.6, UP, 12.0, tint=YELLOW)
        pm.deck(b, 12.0, -0.8, 14.4, 2.4, UP, rails=("front", "right"))


def outfall(b):
    """The stilling basin with its baffle blocks and weir, the chute, the flood channel, the footbridge."""
    rng = b.rng
    bx0, bx1, by0, by1 = -30.0, 30.0, -14.0, -0.5
    b.box((bx1 - bx0, by1 - by0, 0.3), ((bx0 + bx1) / 2, (by0 + by1) / 2, 0.15), mat=CONCRETE,
          tint=srgb(0.5, 0.49, 0.46), wear=0.9)
    for x in (bx0, bx1):
        b.box((0.6, by1 - by0, 2.0), (x, (by0 + by1) / 2, 1.0), mat=FORMWORK, tint=CONC)
    b.box((bx1 - bx0 + 0.6, 0.6, 1.2), ((bx0 + bx1) / 2, by0, 0.6), mat=FORMWORK, tint=CONC)
    for row, y in enumerate((-4.5, -7.5, -10.5)):
        for k in range(12):
            x = bx0 + 3.0 + k * 4.8 + (2.4 if row % 2 else 0.0)
            if x > bx1 - 2.0:
                continue
            b.box((1.2, 1.0, 1.0), (x, y, 0.8), mat=CONCRETE, tint=srgb(0.6, 0.59, 0.56))
    # The chute down from the weir and the channel itself, its sloped walls and copings.
    b.box((bx1 - bx0, 6.0, 0.3), ((bx0 + bx1) / 2, -17.0, 0.15), mat=CONCRETE, tint=srgb(0.5, 0.49, 0.46))
    for (y, sgn, x0, x1) in ((-20.0, 1, -75.0, bx0 - 0.3), (-20.0, 1, bx1 + 0.3, 75.0), (-34.0, -1, -75.0, 75.0)):
        ln = x1 - x0
        with b.frame(((x0 + x1) / 2, y, 0.0), (0, 0, 90 if sgn > 0 else -90)):
            b.extrude([(0.0, 0.0), (3.2, 0.0), (3.2, 2.6), (0.7, 2.6)], ln, (0, 0, 0), (0, 0, 0), mat=CONCRETE,
                      tint=CONC, solid=False)
        b.collide_box(((x0 + x1) / 2, y + sgn * 1.6, 1.3), (ln, 3.2, 2.6), (0, 0, 0))
    b.box((150.0, 14.0, 0.3), (0.0, -27.0, 0.15), mat=CONCRETE, tint=srgb(0.48, 0.47, 0.44), wear=0.9)
    for k in range(10):
        x = -70.0 + k * 15.0
        b.box((1.6, 0.06, 0.25), (x, -20.0 + 0.0, 1.2), tint=srgb(0.3, 0.28, 0.22), solid=False, vary=0.2, wear=0.9)
    pm.preview_water([(-75.0, -33.6), (75.0, -33.6), (75.0, -20.4), (-75.0, -20.4)], 0.55, name="channel")
    pm.preview_water([(bx0 + 0.3, by0 + 0.3), (bx1 - 0.3, by0 + 0.3), (bx1 - 0.3, by1), (bx0 + 0.3, by1)], 0.75,
                     name="basin")
    # The footbridge over the channel: two trusses, a deck, rails.
    fx = 46.0
    for side in (-1, 1):
        x = fx + side * 0.9
        b.beam((x, -38.4, 2.6), (x, -15.6, 2.6), 0.18, 0.3, tint=srgb(0.3, 0.42, 0.45))
        b.beam((x, -38.4, 3.9), (x, -15.6, 3.9), 0.14, 0.14, tint=srgb(0.3, 0.42, 0.45))
        for k in range(8):
            y0 = -38.4 + k * 22.8 / 8
            b.beam((x, y0, 2.6), (x, y0 + 22.8 / 8, 3.9), 0.08, 0.08, tint=srgb(0.3, 0.42, 0.45))
    b.box((1.8, 22.8, 0.1), (fx, -27.0, 2.65), mat=GRATE, tint=GREY)
    stairs(b, (fx, -11.4, 0.0), (0, 0, 180), 1.6, 2.6, 4.2, tint=srgb(0.3, 0.42, 0.45))
    stairs(b, (fx, -42.6, 0.0), (0, 0, 0), 1.6, 2.6, 4.2, tint=srgb(0.3, 0.42, 0.45))
    # What the floods left: a wrecked truck, rubble, reeds along the banks.
    b.piece("utility-truck", (-48.0, -26.0, 0.3), (6.0, -3.0, 70.0), tint=srgb(0.34, 0.3, 0.26))
    rubble(b, (12.0, -28.0, 0.3), 4.0, count=10, tint=srgb(0.45, 0.42, 0.38), mat=PAINT, max_size=0.9)
    rubble(b, (-12.0, -8.0, 0.3), 3.0, count=6, tint=srgb(0.4, 0.38, 0.34), mat=PAINT, max_size=0.7)
    for k in range(22):
        x = rng.uniform(-72, 72)
        y = rng.choice((-17.0, -37.0)) + rng.uniform(-1.5, 1.5)
        pm.shrub(b, (x, y, 2.6 if abs(y + 27) > 9 else 0.3), rng.uniform(0.6, 1.3),
                 tints=(pm.LEAF, pm.LEAF2, pm.MOSS, pm.LEAF_DRY))
    b.marker("guard", (20.0, -8.0, 0.3), 180.0, role="reclaimer")
    b.marker("loot", (-26.0, -3.0, 0.3), table="barrel")


def build(b):
    b.set_pad(("rect", 156.0, 100.0), ground="concrete", blend=14.0, clear=6.0, deck=UP)
    headwall(b)
    underground(b)
    deck(b)
    pump_house(b)
    surge_tower(b)
    yard_level(b)
    outfall(b)
    b.zone("no-build", (0, 0, 0), size=(190.0, 130.0, 60.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-civil", "spawn": "below-towns",
                   "underground": "drains"})


SUN = (32.0, 215.0)
PEOPLE = [((6.0, -10.0, 0.3), 10.0), ((-30.0, 8.0, UP), 200.0)]
VIEWS = [
    {"name": "overview", "target": (0, 8, 8), "distance": 175, "yaw": 30, "pitch": 22, "lens": 32},
    {"name": "outfall", "target": (0, -2, 4.6), "distance": 34.5, "yaw": 10, "pitch": 0.5, "lens": 22},
    {"name": "inlet", "target": (-10, 32, 12), "distance": 45, "yaw": 140, "pitch": -8, "lens": 24},
]
