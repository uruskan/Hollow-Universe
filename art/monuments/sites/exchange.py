"""Exchange (MONUMENTS.md §5.3, medium tier, safe zone): an old Seedward logistics depot near a zone's landing
site, taken over by a Reclaimer trading clan and kept peaceful by Warden turrets they woke up. A wall of precast
panels and stacked containers round a plaza of stalls under the Warden mast, whose turret ring and cyan beacon read
from far off; the depot hall behind is the covered market with the workshop (workbench, research table) and the
place kept for the drone market; container shops along the walls, a recycler bay, a bar, an inn of hab cans.
Outside the gate: landing pads, a fuel bowser, the clan's convoy yard. Anyone who shoots inside is marked and the
turrets kill them (§7.4).
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, BLUE, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        WARM_LAMP, YELLOW, cable, lamp, rubble, srgb, tank)
from monkit.structures import hab_module, road, shed, wall
from monkit.surfaces import CORRUGATED, FORMWORK, GRATE, LAMP, MATTE, PANEL

KEY = "exchange"
TITLE = "Exchange"
SEED = 3707
BUILDER = {"chunk": 48.0, "max_edge": 4.5}

X0, X1, Y0, Y1 = -70.0, 70.0, -40.0, 70.0     # the compound's wall
MAST = (0.0, -6.0)
HALL = (0.0, 50.0)
CONTAINER_TINTS = (srgb(0.55, 0.22, 0.16), srgb(0.20, 0.36, 0.52), srgb(0.24, 0.45, 0.42), srgb(0.84, 0.83, 0.78),
                   srgb(0.78, 0.45, 0.16), srgb(0.36, 0.42, 0.28), srgb(0.62, 0.62, 0.6))
STALLS = ("vendor-stall-blue", "vendor-stall-orange", "vendor-stall-green")
GOODS = ("weapons", "components", "food", "medical", "clothing", "tools", "ammunition", "ship-parts", "electrical",
         "seeds", "scrap", "armour", "explosives", "maps")


def perimeter(b):
    rng = b.rng
    # The front: two-high stacks of containers either side of the gate.
    k = 0
    for side in (-1, 1):
        x = 9.6
        while x + 6.06 <= abs(X0) + 0.1:
            for layer in range(2):
                if layer == 1 and rng.random() < 0.12:
                    continue
                b.piece("container-20", (side * (x + 3.03), Y0 + rng.uniform(-0.15, 0.15), layer * 2.59),
                        (0, 0, rng.uniform(-1.5, 1.5)), tint=CONTAINER_TINTS[k % len(CONTAINER_TINTS)])
                k += 3
            x += 6.12
        if abs(X0) - x > 0.5:
            pm.scrap_wall(b, (side * x, Y0, 0), (side * abs(X0), Y0, 0), height=4.6)
    # Gate towers and the gate: a heavy frame with a sliding scrap door pulled open behind the left stack.
    for side in (-1, 1):
        b.box((3.0, 3.0, 7.2), (side * 8.1, Y0, 3.6), mat=FORMWORK, tint=CONCRETE_T)
        b.box((3.4, 3.4, 0.4), (side * 8.1, Y0, 7.4), mat=CONCRETE, tint=CONCRETE_T)
        pm.hazard_band(b, (side * 8.1 - 1.5, Y0 - 1.52, 0), (side * 8.1 + 1.5, Y0 - 1.52, 0), 2.6, 0.8, width=0.4)
        lamp(b, (side * 8.1, Y0 - 1.6, 6.2), (-20, 0, 0), size=(0.6, 0.3, 0.3))
        pm.banner(b, (side * 8.1, Y0 - 1.65, 6.9), (0, 0, 0), 1.2, 3.6, tint=pm.TARP_RED if side < 0 else pm.TARP_BLUE)
    b.box((19.4, 1.2, 1.2), (0, Y0, 7.8), tint=DARK)
    pm.code_plate(b, (0, Y0 - 0.65, 7.8), (0, 0, 0), 4.2, 0.9, base=YELLOW, ink=BLACK, text="E-04")
    with b.frame((-6.6, Y0 + 0.9, 0)):
        for z in (0.4, 2.2, 4.0):
            b.box((12.8, 0.1, 0.12), (-6.2, 0, z), tint=GREY, solid=False)
        pm.scrap_wall(b, (-12.6, 0.0, 0.0), (0.0, 0.0, 0.0), height=4.4, gaps=0.0, marks=0.3)
    b.box((14.0, 0.25, 0.3), (0, Y0 + 0.9, 0.15), tint=DARK, solid=False)
    b.marker("door", (0, Y0, 0), 180.0, kind_of="gate", width=12.6, height=6.8)
    # Sides and back: the depot's precast wall, patched with scrap where it fell; a back gate.
    for (a, c) in (((X1, Y0 + 1.5), (X1, Y1)), ((X1, Y1), (5.0, Y1)), ((-5.0, Y1), (X0, Y1)),
                   ((X0, Y1), (X0, Y0 + 1.5))):
        wall(b, (a[0], a[1], 0), (c[0], c[1], 0), height=4.5, thick=0.5, panel=4.0, broken=0.06)
    pm.scrap_wall(b, (X1 - 0.9, 20.0, 0), (X1 - 0.9, 30.0, 0), height=4.2)
    pm.scrap_wall(b, (-30.0, Y1 - 0.9, 0), (-20.0, Y1 - 0.9, 0), height=4.2)
    pm.scrap_wall(b, (X0 + 0.9, -14.0, 0), (X0 + 0.9, -4.0, 0), height=4.2)
    for side in (-1, 1):
        b.box((1.6, 1.6, 5.6), (side * 5.8, Y1, 2.8), mat=FORMWORK, tint=CONCRETE_T)
    pm.scrap_wall(b, (-5.0, Y1 + 0.8, 0), (2.0, Y1 + 0.8, 0), height=4.0, gaps=0.0)
    b.marker("door", (3.5, Y1, 0), 0.0, kind_of="gate", width=3.0, height=4.0)
    # Peacekeepers on masts round the wall.
    for (x, y, a) in ((X0 + 3.5, Y0 + 3.5, 225), (X1 - 3.5, Y0 + 3.5, 315), (-12.5, Y0 + 3.2, 270),
                      (12.5, Y0 + 3.2, 270), (X0 + 3.5, Y1 - 3.5, 135), (X1 - 3.5, Y1 - 3.5, 45),
                      (X0 + 3.5, 15.0, 180), (X1 - 3.5, 15.0, 0)):
        yaw = a + 90.0
        b.piece("turret-mast", (x, y, 0), (0, 0, yaw))
        b.marker("turret", (x, y, 8.8), yaw + 180.0, role="peacekeeper", range=80.0)


def warden_mast(b):
    x, y = MAST
    b.box((10.0, 10.0, 1.0), (x, y, 0.5), mat=FORMWORK, tint=CONCRETE_T)
    b.box((10.6, 10.6, 0.12), (x, y, 1.06), mat=CONCRETE, tint=srgb(0.8, 0.79, 0.76), solid=False)
    for side in range(4):
        with b.frame((x, y, 0), (0, 0, 90 * side)):
            pm.concrete_steps(b, (0, -6.8, 0), (0, 0, 0), 4.0, 1.0, 1.8)
            b.cyl(0.9, 0.7, (3.6, -3.6, 1.45), mat=CONCRETE, tint=CONCRETE_T, sides=12)
            pm.shrub(b, (3.6, -3.6, 1.8), 0.6)
    H = 34.0

    def face(z):
        return (3.4 + (2.1 - 3.4) * (z - 1.0) / H) * math.sqrt(0.5)

    b.cyl(3.4, H, (x, y, 1.0 + H / 2), (0, 0, 45), mat=PANEL, tint=OFFWHITE, sides=4, radius2=2.1)
    for z, t in ((9.0, ORANGE), (11.0, TEAL), (21.0, ORANGE)):
        r = 3.4 + (2.1 - 3.4) * (z - 1.0) / H + 0.03
        b.cyl(r, 1.2, (x, y, z), (0, 0, 45), mat=PAINT, tint=t, sides=4, radius2=r - 0.05, solid=False, vary=0)
    # Conduits up two faces, equipment cabinets at the foot, a door.
    for side in (-1, 1):
        pts = [(x + side * (face(z) + 0.18), y + 0.9, z) for z in (1.1, 12.0, 26.0)]
        b.tube(pts, 0.12, tint=GREY, sides=8)
        b.tube([(p[0], p[1] - 1.8, p[2]) for p in pts], 0.08, tint=DARK, sides=6)
        for z in (4.0, 9.5, 15.0, 20.5):
            b.box((0.3, 2.4, 0.12), (x + side * (face(z) + 0.12), y, z), tint=DARK, solid=False, vary=0)
        b.box((1.0, 1.6, 2.0), (x + side * (face(2.0) + 0.6), y - 1.0, 2.0), tint=srgb(0.5, 0.54, 0.52),
              bevel=0.03)
        b.box((0.02, 0.4, 0.1), (x + side * (face(2.0) + 1.12), y - 1.0, 2.6), mat=LAMP, tint=pm.CHIP["cyan"],
              solid=False, vary=0)
    b.box((1.5, 0.12, 2.4), (x, y - face(2.4) - 0.04, 2.4), tint=DARK, solid=False)
    pm.code_plate(b, (x, y - face(5.0) - 0.04, 5.0), (0, 0, 0), 2.4, 0.6, base=TEAL, ink=OFFWHITE, text="W-0")
    pm.banner(b, (x, y - face(16.0) - 0.08, 16.0), (0, 0, 0), 1.6, 4.5, tint=pm.TARP_RED)
    pm.banner(b, (x, y + face(16.0) + 0.08, 16.0), (0, 0, 180), 1.6, 4.5, tint=pm.TARP_BLUE)
    # The turret ring.
    zr = 27.0
    rr = 3.4 + (2.1 - 3.4) * (zr - 1.0) / H
    pm.ring_deck(b, (x, y), rr - 0.2, rr + 2.6, zr, sides=24, mat=GRATE, solid=False)
    pm.ring_rail(b, (x, y), rr + 2.5, zr, sides=24, tint=DARK)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        px, py = x + math.cos(a) * (rr + 1.4), y + math.sin(a) * (rr + 1.4)
        yaw = math.degrees(a) + 90.0
        pm.warden_turret(b, (px, py, zr), yaw, scale=1.25)
        b.marker("turret", (px, py, zr + 0.8), yaw + 180.0, role="peacekeeper", range=120.0)
        b.beam((px, py, zr - 0.1), (x + math.cos(a) * (rr - 0.1), y + math.sin(a) * (rr - 0.1), zr - 2.4), 0.25, 0.25,
               tint=DARK)
    for k in range(4):
        a = 90 * k
        lamp(b, (x + math.cos(math.radians(a)) * (face(30.5) + 0.3), y + math.sin(math.radians(a)) * (face(30.5) + 0.3),
                 30.5), (-35, 0, a - 90 + 180), size=(0.9, 0.4, 0.4))
    # The beacon and the aerial.
    zt = 1.0 + H
    b.cyl(2.6, 0.4, (x, y, zt + 0.2), tint=DARK, sides=20)
    b.cyl(2.0, 1.3, (x, y, zt + 1.05), mat=LAMP, tint=pm.CHIP["cyan"], sides=20, solid=False, vary=0)
    b.cyl(2.3, 0.35, (x, y, zt + 1.85), tint=OFFWHITE, sides=20)
    b.marker("light", (x, y, zt + 1.0), colour=[round(v, 3) for v in pm.CHIP["cyan"]], range=300.0, pulse=3.0)
    b.cyl(0.18, 8.0, (x, y, zt + 6.0), tint=GREY, sides=8, radius2=0.08)
    pm.aviation_lamp(b, (x, y, zt + 10.0), size=0.4)
    b.marker("landmark", (x, y, zt + 10.4), kind_of="exchange-beacon", visible_km=8)


def plaza(b):
    x0, y0 = MAST
    rng = b.rng
    n = 0
    for a in (-148.0, -118.0, -62.0, -32.0, 28.0, 58.0, 122.0, 152.0):
        ar = math.radians(a)
        px, py = x0 + math.cos(ar) * 16.0, y0 + math.sin(ar) * 16.0
        yaw = a - 90.0
        b.piece(STALLS[n % 3], (px, py, 0), (0, 0, yaw))
        with b.frame((px, py, 0), (0, 0, yaw)):
            b.marker("vendor", (0.0, 0.0, 0.0), 180.0, sells=GOODS[n])
            b.piece("crate-stack", (2.6, 0.6, 0), (0, 0, rng.uniform(-20, 20)))
        pm.string_lights(b, (x0 + math.cos(ar) * 3.0, y0 + math.sin(ar) * 3.0, 9.0),
                         (x0 + math.cos(ar) * 14.4, y0 + math.sin(ar) * 14.4, 3.0), sag=0.03)
        n += 1
    # Tents of the smaller traders between the stalls and the walls.
    for k, (px, py, a, t) in enumerate(((-30.0, -24.0, 20.0, pm.TARP_TAN), (31.0, -25.0, -15.0, pm.TARP_BLUE),
                                        (-34.0, 18.0, 70.0, pm.TARP_GREEN), (30.0, 16.0, -70.0, pm.TARP_RED))):
        pm.tarp_shelter(b, (px, py, 0), (0, 0, a), 4.5, 3.5, tint=t, tattered=k % 2 == 1)
        with b.frame((px, py, 0), (0, 0, a)):
            b.box((3.0, 0.7, 0.9), (0, -0.9, 0.45), mat=PAINT, tint=srgb(0.4, 0.42, 0.42))
            b.piece("crate-stack", (-1.2, 0.8, 0), (0, 0, 15))
            b.marker("vendor", (0.0, 0.0, 0.0), 180.0, sells=GOODS[8 + k])
    # Fire barrels, benches, flags, lamp posts round the plaza.
    for (px, py) in ((-8.0, -26.0), (9.0, 9.0), (-11.0, 10.0), (12.0, -22.0)):
        b.piece("fire-barrel", (px, py, 0))
    for (px, py, yaw) in ((-6.0, -30.0, 0.0), (6.5, -30.5, 10.0), (-13.5, 2.0, 80.0), (14.0, -2.0, 95.0)):
        with b.frame((px, py, 0), (0, 0, yaw)):
            b.box((2.4, 0.5, 0.08), (0, 0, 0.45), mat=MATTE, tint=pm.WOOD, solid=False)
            for xx in (-1.0, 1.0):
                b.box((0.3, 0.4, 0.42), (xx, 0, 0.21), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    for (px, py, t) in ((-22.0, -36.0, pm.TARP_ORANGE), (22.0, -36.0, pm.TARP_BLUE), (-24.0, 28.0, pm.TARP_GREEN),
                        (24.0, 28.0, pm.TARP_RED)):
        pm.flag(b, (px, py, 0), 9.0, tint=t)
    for (px, py, a) in ((-20.0, -24.0, 135.0), (20.0, -26.0, 225.0), (-20.0, 14.0, 45.0), (20.0, 12.0, -45.0)):
        b.piece("lamp-post", (px, py, 0), (0, 0, a))
    road(b, [(0.0, Y0 - 1.5, 0.0), (0.0, MAST[1] - 9.0, 0.0)], width=8.0, lines=False)
    road(b, [(0.0, MAST[1] + 9.0, 0.0), (0.0, HALL[1] - 16.0, 0.0)], width=8.0, lines=False)
    b.marker("guard", (-4.5, Y0 + 4.0, 0.0), 180.0, role="exchange-guard")
    b.marker("guard", (4.5, Y0 + 4.0, 0.0), 180.0, role="exchange-guard")


def container_shops(b):
    """Containers along the walls turned into shops: an awning, a counter, goods, a trader."""
    rng = b.rng
    k = 0
    for side, xs in ((-1, X0 + 3.0), (1, X1 - 3.0)):
        for y in (-28.0, -16.0, 26.0, 38.0):
            tint = CONTAINER_TINTS[(k * 2 + 1) % len(CONTAINER_TINTS)]
            b.piece("container-20", (xs, y, 0), (0, 0, 90), tint=tint)
            with b.frame((xs - side * 1.22, y, 0), (0, 0, -90 * side)):
                b.box((4.8, 0.7, 1.0), (0, -1.4, 0.5), mat=PAINT, tint=srgb(0.42, 0.44, 0.44))
                b.box((4.9, 0.8, 0.06), (0, -1.4, 1.03), mat=MATTE, tint=pm.WOOD, solid=False)
                grid = [[(-3.0 + 6.0 * i / 3, -2.6 + 2.6 * j / 2, 2.9 - 0.5 * (1 - j / 2) - 0.1 * math.sin(math.pi * i / 3))
                         for i in range(4)] for j in range(3)]
                pm.sheet(b, grid, tint=(pm.TARP_RED, pm.TARP_BLUE, pm.TARP_ORANGE, pm.TARP_GREEN)[k % 4])
                for xx in (-2.8, 2.8):
                    b.cyl(0.04, 2.4, (xx, -2.5, 1.2), tint=GREY, sides=6, solid=False, vary=0)
                pm.code_plate(b, (0, -0.03, 2.25), (0, 0, 0), 2.0, 0.4, base=srgb(0.86, 0.82, 0.7), ink=DARK)
                b.marker("vendor", (0, -0.6, 0), 180.0, sells=GOODS[(k + 3) % len(GOODS)])
                b.box((0.15, 0.15, 0.2), (0, -1.5, 2.6), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
            k += 1


def hall(b):
    hx, hy = HALL
    w, d, h = 64.0, 30.0, 10.0
    shed(b, (hx, hy, 0), (0, 0, 0), w, d, h, roof="gable",
         doors=[("front", -18.0, 9.0, 7.0), ("front", 0.0, 7.0, 6.0), ("front", 18.0, 9.0, 7.0),
                ("back", 0.0, 8.0, 6.5)], tint=srgb(0.62, 0.66, 0.66), ruin=0.06)
    with b.frame((hx, hy, 0), (0, 0, 0)):
        pm.stencil(b, (-9.0, -d / 2 - 0.25, 8.0), (0, 0, 0), "D7", height=2.6, tint=srgb(0.95, 0.75, 0.2))
        pm.mark(b, (0.0, -d / 2 - 0.25, 12.0), (0, 0, 0), 3.2, "ring", OFFWHITE)
        for x in (-18.0, 18.0):
            lamp(b, (x, -d / 2 - 0.4, 8.0), (-25, 0, 0), size=(0.8, 0.35, 0.3))
        # Inside: stalls along the back wall and down the middle, the workshop at the west end, the drone market.
        n = 0
        for k, x in enumerate((-8.0, 0.0, 8.0, 16.0)):
            b.piece(STALLS[k % 3], (x, d / 2 - 1.4, 0.25), (0, 0, 0))
            b.marker("vendor", (x, d / 2 - 1.4, 0.25), 180.0, sells=GOODS[n % len(GOODS)])
            n += 1
        for k, x in enumerate((-4.0, 8.0)):
            b.piece(STALLS[(k + 1) % 3], (x, -3.0, 0.25), (0, 0, 0))
            b.marker("vendor", (x, -3.0, 0.25), 180.0, sells=GOODS[(n + 4) % len(GOODS)])
            n += 1
        wx = -w / 2 + 6.0
        for y in (-6.0, 0.0, 6.0):
            b.box((3.0, 1.0, 0.9), (wx + 2.5, y, 0.7), mat=PAINT, tint=srgb(0.45, 0.47, 0.46))
            b.box((3.1, 1.1, 0.06), (wx + 2.5, y, 1.18), mat=MATTE, tint=pm.WOOD, solid=False)
        b.box((0.1, 12.0, 2.4), (-w / 2 + 0.6, 0.0, 2.0), mat=PAINT, tint=srgb(0.32, 0.36, 0.34), solid=False)
        for k in range(14):
            b.box((0.05, 0.12, 0.5), (-w / 2 + 0.7, -5.5 + k * 0.8, 2.2 + (k % 3) * 0.3), tint=GREY, solid=False,
                  vary=0.2)
        b.marker("workbench", (wx, -3.0, 0.25), 90.0, tier=2)
        b.marker("research-table", (wx, 3.0, 0.25), 90.0)
        b.marker("workbench", (wx, 9.0, 0.25), 90.0, tier=1)
        for y in (-9.0, 9.0):
            lamp(b, (wx + 2.0, y, 7.5), (90, 0, 0), size=(0.8, 0.3, 0.2))
        b.box((7.0, 7.0, 0.02), (w / 2 - 7.0, 6.0, 0.26), tint=TEAL, solid=False, vary=0, wear=0.6)
        b.box((5.6, 5.6, 0.025), (w / 2 - 7.0, 6.0, 0.265), tint=srgb(0.22, 0.24, 0.25), solid=False, vary=0)
        b.marker("drone-market", (w / 2 - 7.0, 6.0, 0.26), 0.0, later=True)
        for k, x in enumerate((-20.0, -4.0, 12.0, 26.0)):
            pm.banner(b, (x, 0.0, h - 0.4), (0, 0, 0), 1.4, 4.0,
                      tint=(pm.TARP_RED, pm.TARP_BLUE, pm.TARP_GREEN, pm.TARP_ORANGE)[k])
        for x in (-22.0, -6.0, 10.0, 24.0):
            b.box((0.2, 0.2, 0.4), (x, -6.0, h - 0.8), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
            b.marker("light", (x, -6.0, h - 1.0), colour=[round(v, 3) for v in WARM_LAMP], range=16.0)


def services(b):
    rng = b.rng
    # The recycler bay east of the plaza.
    rx, ry = 46.0, -6.0
    pm.lean_to(b, (rx, ry, 0), (0, 0, -90), 9.0, 6.0, h_front=3.6, h_back=4.2, missing=0.0)
    b.box((3.6, 2.6, 0.25), (rx - 0.5, ry, 0.125), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68))
    pm.hazard_band(b, (rx - 2.3, ry - 1.4, 0), (rx + 1.3, ry - 1.4, 0), 0.25, 0.25)
    b.marker("recycler", (rx - 0.5, ry, 0.25), 90.0)
    pm.code_plate(b, (rx + 2.9, ry, 2.6), (0, 0, -90), 1.6, 0.5, base=YELLOW, ink=BLACK, text="RC-1")
    lamp(b, (rx, ry, 3.6), (90, 0, 0), size=(0.6, 0.3, 0.2))
    b.piece("drum-stack", (rx + 1.0, ry + 3.8, 0), (0, 0, 20))
    b.piece("crate-stack", (rx - 1.8, ry + 3.6, 0), (0, 0, -10))
    # The bar: two containers and a tarp terrace with tables, west of the plaza.
    bx, by = -44.0, 2.0
    b.piece("container-20", (bx - 4.0, by + 4.0, 0), (0, 0, 90), tint=CONTAINER_TINTS[0])
    b.piece("container-20", (bx - 1.4, by + 4.0, 0), (0, 0, 90), tint=CONTAINER_TINTS[1])
    b.box((4.6, 0.9, 1.1), (bx - 2.7, by - 0.6, 0.55), mat=PAINT, tint=srgb(0.4, 0.42, 0.42))
    b.box((4.8, 1.0, 0.06), (bx - 2.7, by - 0.6, 1.13), mat=MATTE, tint=pm.WOOD, solid=False)
    b.box((4.4, 0.08, 1.2), (bx - 2.7, by + 0.9, 3.4), mat=PAINT, tint=srgb(0.2, 0.22, 0.24))
    for xx in (bx - 4.6, bx - 0.8):
        b.box((0.1, 0.1, 1.0), (xx, by + 0.95, 2.6), tint=DARK, solid=False)
    pm.stencil(b, (bx - 2.7, by + 0.84, 3.4), (0, 0, 0), "BAR", height=0.8, tint=srgb(0.95, 0.8, 0.3), mat=MATTE)
    pm.tarp_shelter(b, (bx - 2.7, by - 5.0, 0), (0, 0, 180), 10.0, 7.0, h_front=2.8, h_back=3.0, tint=pm.TARP_TAN)
    for k, (tx, ty) in enumerate(((bx - 4.0, by - 4.0), (bx + 1.0, by - 3.0), (bx - 2.0, by - 8.0),
                                  (bx + 3.0, by - 7.5))):
        b.cyl(0.55, 0.06, (tx, ty, 0.95), mat=MATTE, tint=pm.WOOD, sides=12, solid=False)
        b.cyl(0.08, 0.92, (tx, ty, 0.46), tint=DARK, sides=6, solid=False)
        for j in range(3):
            a = 2 * math.pi * j / 3 + k
            b.box((0.4, 0.4, 0.45), (tx + math.cos(a) * 1.0, ty + math.sin(a) * 1.0, 0.225), mat=PAINT,
                  tint=(RED, BLUE, OFFWHITE)[j], solid=False, vary=0.1)
    pm.string_lights(b, (bx - 7.7, by - 1.5, 2.9), (bx + 2.3, by - 8.5, 3.0))
    pm.string_lights(b, (bx + 2.3, by - 1.5, 2.9), (bx - 7.7, by - 8.5, 3.0))
    b.piece("fire-barrel", (bx + 4.0, by - 4.0, 0))
    b.marker("vendor", (bx - 2.7, by + 0.3, 0.0), 180.0, sells="drinks")
    pm.mark(b, (bx - 4.2, by + 0.94, 1.6), (0, 0, 0), 1.2, "ring", OFFWHITE)
    # The inn: hab cans in the north-east; a water tank and generator in the north-west; storage at the back.
    for i, (hx, hy, a) in enumerate(((50.0, 36.0, 0.0), (50.0, 44.0, 3.0), (50.0, 52.0, -2.0), (60.0, 60.0, 0.0))):
        hab_module(b, (hx, hy, 0), (0, 0, a), length=9.0, tint=(OFFWHITE, srgb(0.84, 0.85, 0.82))[i % 2],
                   band=(ORANGE, TEAL)[i % 2])
    pm.tarp_shelter(b, (52.0, 28.0, 0), (0, 0, 0), 6.0, 4.0, tint=pm.TARP_GREEN)
    b.marker("vendor", (52.0, 28.0, 0.0), 180.0, sells="beds")
    tank(b, (-60.0, 60.0, 0), 3.2, 7.0, tint=srgb(0.82, 0.84, 0.82), band=TEAL, ladder_side=None, legs=6.0)
    b.box((5.0, 2.4, 2.4), (-40.0, 52.0, 1.2), tint=srgb(0.28, 0.42, 0.36), bevel=0.05)
    b.box((1.5, 2.2, 1.8), (-36.6, 52.0, 0.9), tint=DARK)
    for i in range(3):
        for layer in range(1 + (i % 2)):
            b.piece("container-20", (-56.0 + i * 3.0, 44.0, layer * 2.59), (0, 0, 90 + rng.uniform(-3, 3)),
                    tint=CONTAINER_TINTS[(i * 2 + layer) % len(CONTAINER_TINTS)])
    for (px, py) in ((36.0, -32.0), (-38.0, -33.0), (60.0, 8.0)):
        b.piece("crate-stack", (px, py, 0), (0, 0, rng.uniform(0, 90)))


def outside(b):
    """Landing pads, the fuel bowser, the convoy yard, the road in."""
    for (x, y) in ((-40.0, -72.0), (40.0, -72.0), (-100.0, 10.0)):
        b.cyl(12.0, 0.25, (x, y, 0.125), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.7), sides=32)
        b.lathe([(9.6, 0.26), (10.4, 0.26), (10.4, 0.27), (9.6, 0.27), (9.6, 0.26)], (x, y, 0), tint=YELLOW,
                sides=32, close=False, solid=False, vary=0, wear=0.6)
        for k in range(8):
            a = math.radians(22.5 + 45 * k)
            b.box((0.4, 0.4, 0.3), (x + math.cos(a) * 11.4, y + math.sin(a) * 11.4, 0.4), mat=LAMP,
                  tint=srgb(1.0, 0.8, 0.3), solid=False, vary=0)
        for k in range(3):
            b.box((1.0, 3.0, 0.02), (x - 2.0 + k * 2.0, y, 0.265), tint=OFFWHITE, solid=False, vary=0, wear=0.6)
        b.marker("landing", (x, y, 0.25), 0.0, size=20.0)
    road(b, [(0.0, -100.0, 0.0), (0.0, Y0 - 1.5, 0.0)], width=8.0)
    road(b, [(-112.0, -50.0, 0.0), (112.0, -50.0, 0.0)], width=7.0)
    b.cyl(1.4, 6.0, (16.0, -64.0, 1.9), (0, 90, 0), tint=srgb(0.82, 0.62, 0.2), sides=18)
    for x in (13.6, 18.4):
        b.box((0.5, 2.6, 0.9), (x, -64.0, 0.45), mat=CONCRETE, tint=CONCRETE_T)
    b.box((0.8, 0.6, 1.6), (20.0, -61.5, 0.8), tint=srgb(0.82, 0.62, 0.2), bevel=0.04)
    b.tube([(19.8, -61.2, 1.2), (19.0, -60.0, 0.3), (17.0, -58.0, 0.1)], 0.04, tint=BLACK, sides=6)
    b.marker("vendor", (20.0, -60.5, 0.0), 0.0, sells="ship-fuel")
    for (px, py, a) in ((-24.0, -60.0, 180.0), (24.0, -86.0, 0.0), (-84.0, -6.0, 90.0), (84.0, -6.0, -90.0)):
        b.piece("lamp-post", (px, py, 0), (0, 0, a))
    for (x, y) in ((-70.0, -90.0), (70.0, -92.0)):
        b.marker("loot", (x, y, 0.0), table="barrel")
    b.piece("jersey-barrier", (-6.0, -96.0, 0), (0, 0, 90))
    b.piece("jersey-barrier", (6.0, -96.0, 0), (0, 0, 90))
    # The convoy yard east of the wall: the clan's trucks, containers, a scrap pile under a tarp.
    for k, (x, y, a, t) in enumerate(((90.0, -24.0, 90.0, srgb(0.85, 0.45, 0.15)), (96.0, -24.0, 88.0, OFFWHITE),
                                      (102.0, -26.0, 95.0, srgb(0.4, 0.5, 0.42)), (92.0, 20.0, 180.0, OFFWHITE))):
        b.piece("utility-truck", (x, y, 0), (0, 0, a), tint=t)
    for k in range(4):
        b.piece("container-20", (110.0, -10.0 + k * 7.0, 0), (0, 0, 90), tint=CONTAINER_TINTS[(k * 3) % 7])
    b.piece("container-20", (110.2, -6.5, 2.59), (0, 0, 92), tint=CONTAINER_TINTS[4])
    pm.tarp_shelter(b, (95.0, 0.0, 0), (0, 0, 90), 8.0, 6.0, h_front=3.2, h_back=3.4, tint=pm.TARP_BLUE)
    rubble(b, (95.0, 0.0, 0.0), 2.5, count=8, tint=srgb(0.45, 0.42, 0.38), mat=PAINT, max_size=1.0)
    pm.barricade(b, (84.0, -40.0, 0), (0, 0, 0), 8.0)
    b.marker("guard", (98.0, -32.0, 0.0), 180.0, role="exchange-guard")
    b.piece("utility-truck", (-96.0, 34.0, 0), (0, 0, 10), tint=srgb(0.6, 0.62, 0.62))
    b.piece("crate-stack", (-92.0, 40.0, 0), (0, 0, 40))


def build(b):
    b.set_pad(("rect", 250.0, 200.0), ground="concrete", blend=16.0, clear=8.0)
    perimeter(b)
    warden_mast(b)
    plaza(b)
    container_shops(b)
    hall(b)
    services(b)
    outside(b)
    b.zone("safe", ((X0 + X1) / 2, (Y0 + Y1) / 2, 15.0), size=(X1 - X0, Y1 - Y0, 40.0), hostile_minutes=10)
    b.zone("no-build", (0, 0, 0), size=(290.0, 240.0, 90.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "reclaimer", "spawn": "near-landing",
                   "safe": True})


SUN = (34.0, 220.0)
PEOPLE = [((3.0, -56.0, 0), 15.0), ((-5.0, -20.0, 0), 200.0), ((2.5, -28.0, 0), 160.0), ((-12.0, -18.0, 0), 60.0)]
SHIP = ((-40.0, -72.0, 0.25), 35.0)
VIEWS = [
    {"name": "overview", "target": (0, 4, 6), "distance": 215, "yaw": 32, "pitch": 27, "lens": 32},
    {"name": "gate", "target": (0, -26, 10), "distance": 50, "yaw": 8, "pitch": -9, "lens": 24},
    {"name": "plaza", "target": (-12, 0, 5), "distance": 29.6, "yaw": 35, "pitch": -6, "lens": 22},
]
