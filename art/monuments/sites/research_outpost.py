"""Research Outpost (MONUMENTS.md §5.3, medium tier): a Seedward field laboratory on odd ground near an anomaly:
white prefab labs on stilts joined by enclosed bridges, a radome on a 22 m tower, a utility block, and a test range
of berms, targets and craters overlooked by a concrete bunker. Warden drones still patrol it.

The access puzzle runs through the labs: lab A's terminal shows a code; lab B's amber door opens the specimen lab
and the bridge to lab C; lab C's clean room needs its power back (a cell in the utility block's fuse box) and a cyan
chip; its vault opens to the code from lab A and holds the sealed crate with schematics.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, fence, ladder, lamp, railing, rubble, srgb, stairs, tank)
from monkit.structures import road, solar_array
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PANEL, RUST

KEY = "research-outpost"
TITLE = "Research Outpost"
SEED = 3001
BUILDER = {"chunk": 48.0, "max_edge": 4.0}

STILT = 2.4
LAB_T = srgb(0.93, 0.93, 0.91)
A = (-24.0, -22.0)      # entrance and offices (along X)
B = (-24.0, 6.0)        # labs (along X)
C = (14.0, 0.0)         # clean room module (along Y)
U = (-62.0, -2.0)       # utility block
RADOME = (-58.0, 42.0)


def lab(b, at, rot, w, d, openings, h=3.6, band=TEAL, stairs_at=None):
    """A prefab lab module raised on stilts: legs and bracing, a panelled shell with a window strip, rounded ends,
    a band of livery; `stairs_at`: (x along the front, side) for an entrance stair from the ground."""
    with b.frame((at[0], at[1], STILT), rot):
        info = pm.building(b, (0, 0, 0), (0, 0, 0), w, d, floors=1, floor_h=h, mat=PANEL, tint=LAB_T,
                           openings=openings, plinth=0.25, slab=0.25, parapet=0.4, band=(band, 2.4, 0.35),
                           roof_units=max(1, int(w / 9)), glass=0.7, steps=False, downpipes=False,
                           door_tint=srgb(0.82, 0.84, 0.84))
        for x in (-w / 2, w / 2):
            b.cyl(d / 2 + 0.02, h + 0.55, (x, 0, (h + 0.55) / 2), mat=PANEL, tint=LAB_T, sides=20)
        b.box((w + 0.3, d + 0.3, 0.06), (0, 0, 0.27), tint=GREY, solid=False, vary=0)
        nx = max(2, int(w / 4.0))
        for i in range(nx + 1):
            x = -w / 2 + 0.6 + (w - 1.2) * i / nx
            for y in (-d / 2 + 0.6, d / 2 - 0.6):
                b.box((0.3, 0.3, STILT), (x, y, -STILT / 2), tint=srgb(0.7, 0.72, 0.72))
                b.box((0.7, 0.7, 0.3), (x, y, -STILT + 0.15), mat=CONCRETE, tint=CONCRETE_T, vary=0)
            b.beam((x, -d / 2 + 0.6, -STILT + 0.4), (x, d / 2 - 0.6, -0.3), 0.08, 0.08, tint=GREY)
        for y in (-d / 2 + 0.6, d / 2 - 0.6):
            b.box((w - 1.0, 0.18, 0.25), (0, y, -0.3), tint=srgb(0.6, 0.62, 0.62), solid=False)
        if stairs_at:
            x, side = stairs_at
            sy = -d / 2 if side == "front" else d / 2
            rot_s = 0 if side == "front" else 180
            run = 4.3
            with b.frame((x, sy, 0), (0, 0, rot_s)):
                stairs(b, (0, -run - 1.4, -STILT), (0, 0, 0), 1.2, STILT + 0.25, run, tint=TEAL)
                pm.deck(b, -1.0, -1.4, 1.0, 0.0, 0.25, rails=("left", "right"), rail_tint=TEAL)
        b.marker("light", (0, -d / 2 - 0.4, 3.0), colour=[0.85, 0.93, 1.0], range=12.0)
    return info


def bridge(b, a, c, z, w=2.4, h=2.7):
    """An enclosed walkway between two lab doors: floor, roof, windowed walls, legs."""
    a, c = (a[0], a[1], z), (c[0], c[1], z)
    dx, dy = c[0] - a[0], c[1] - a[1]
    length = math.hypot(dx, dy)
    yaw = math.degrees(math.atan2(dy, dx))
    with b.frame(a, (0, 0, yaw)):
        b.box((length, w, 0.2), (length / 2, 0, -0.1), mat=PANEL, tint=LAB_T)
        b.box((length, w + 0.2, 0.2), (length / 2, 0, h + 0.1), mat=PANEL, tint=LAB_T)
        n = max(1, int(length / 2.2))
        ops = [((i + 0.5) * length / n, 1.2, 0.9, 1.0) for i in range(n)]
        pm.wall(b, (0, -w / 2, 0.0), (length, -w / 2, 0.0), h, 0.15, ops, mat=PANEL, tint=LAB_T, glass=0.6,
                extend=False)
        pm.wall(b, (length, w / 2, 0.0), (0, w / 2, 0.0), h, 0.15, ops, mat=PANEL, tint=LAB_T, glass=0.6,
                extend=False)
        b.box((length, 0.03, 0.3), (length / 2, -w / 2 - 0.09, 2.0), tint=ORANGE, solid=False, vary=0)
        k = max(1, int(length / 5.0))
        for i in range(1, k):
            x = length * i / k
            for y in (-w / 2 + 0.2, w / 2 - 0.2):
                b.box((0.22, 0.22, z + 0.05), (x, y, -z / 2 + 0.05), tint=srgb(0.7, 0.72, 0.72))


def labs(b):
    # Lab A: reception and offices; the code terminal.
    lab(b, A, (0, 0, 0), 26.0, 9.0,
        {"front": [(-8.0, 1.4, 2.3, 0, 0, {"kind_of": "entrance", "state": "ajar"})]
                  + [(x, 1.6, 1.0, 1.1, 0) for x in (-11.0, -4.5, -1.0, 2.5, 6.0, 9.5)],
         "back": [(0.0, 1.4, 2.3, 0, 0, {"kind_of": "bridge", "state": "open"})]
                 + [(x, 1.6, 1.0, 1.1, 0) for x in (-9.0, -5.0, 5.0, 9.0)]},
        stairs_at=(-8.0, "front"))
    with b.frame((A[0], A[1], STILT + 0.25)):
        pm.wall(b, (0.0, -4.35, 0), (0.0, 4.35, 0), 3.6, 0.15, [(2.0, 1.2, 2.2, 0, None)], mat=PANEL,
                tint=srgb(0.86, 0.87, 0.86))
        b.piece("console", (-11.0, 3.4, 0), (0, 0, 180))
        b.piece("console", (5.0, 3.5, 0), (0, 0, 180))
        b.piece("console", (9.5, -3.4, 0), (0, 0, 0))
        b.piece("rack-row", (11.5, 2.5, 0), (0, 0, 90))
        b.marker("terminal", (5.0, 2.9, 0), 0.0, gives="code", code_for="vault-c", lead=True)
        b.marker("loot", (-4.0, 3.2, 0), table="crate")
        b.marker("loot", (11.0, -3.2, 0), table="crate")
        b.marker("loot", (-12.0, -3.4, 0), table="barrel")
        pm.code_plate(b, (-10.2, -4.68, 1.6), (0, 0, 0), 1.4, 0.45, base=TEAL, ink=OFFWHITE, text="LAB-A")
        lamp(b, (-8.0, -4.75, 2.7), (0, 0, 0), size=(0.45, 0.25, 0.18))
    bridge(b, (A[0], A[1] + 4.5), (B[0], B[1] - 4.5), STILT + 0.25)
    # Lab B: two labs off a corridor; the specimen lab behind the amber door leads to the bridge to lab C.
    lab(b, B, (0, 0, 0), 26.0, 9.0,
        {"front": [(0.0, 1.4, 2.3, 0, 0, {"kind_of": "bridge", "state": "open"})]
                  + [(x, 1.6, 1.0, 1.1, 0) for x in (-10.0, -6.0, 6.0)],
         "back": [(x, 1.6, 1.0, 1.1, 0) for x in (-10.0, -6.0, -2.0)],
         "right": [(0.0, 1.4, 2.3, 0, 0, {"kind_of": "bridge", "state": "shut"})],
         "left": [(0.0, 1.2, 2.3, 0, 0, {"kind_of": "emergency", "state": "ajar"})]},
        band=ORANGE)
    with b.frame((B[0], B[1], STILT + 0.25)):
        # A corridor along the front; two lab rooms behind it; the amber room at the east end.
        pm.wall(b, (-12.85, -1.6, 0), (6.0, -1.6, 0), 3.6, 0.15, [(5.0, 1.2, 2.2, 0, None), (16.0, 1.2, 2.2, 0, None)],
                mat=PANEL, tint=srgb(0.86, 0.87, 0.86))
        pm.wall(b, (-1.0, -1.6, 0), (-1.0, 4.35, 0), 3.6, 0.15, [], mat=PANEL, tint=srgb(0.86, 0.87, 0.86))
        pm.wall(b, (6.0, 4.35, 0), (6.0, -4.35, 0), 3.6, 0.25,
                [(6.0, 1.2, 2.3, 0, {"kind_of": "room", "chip": "amber", "room": "specimen-lab"})],
                mat=PANEL, tint=srgb(0.86, 0.87, 0.86))
        for x in (-10.0, -6.0, 3.0):
            b.box((2.6, 0.9, 0.9), (x, 3.6, 0.45), tint=srgb(0.82, 0.84, 0.84))
            b.box((2.6, 0.5, 0.9), (x, 3.85, 1.7), mat=GLASS, solid=False, vary=0)
        b.piece("console", (-4.0, 1.5, 0), (0, 0, 90))
        b.marker("loot", (-11.5, 1.0, 0), table="crate")
        b.marker("loot", (4.0, 1.0, 0), table="crate")
        # Specimen lab: tanks, a rack, the military crate.
        for k, y in enumerate((-2.5, 0.0, 2.5)):
            b.cyl(0.55, 2.2, (11.4, y, 1.1), mat=GLASS, sides=14, solid=False, vary=0)
            b.cyl(0.6, 0.3, (11.4, y, 0.15), tint=DARK, sides=14)
            b.cyl(0.6, 0.3, (11.4, y, 2.35), tint=DARK, sides=14)
            b.cyl(0.4, 0.8, (11.4, y, 1.0), mat=MATTE, tint=(pm.LEAF, srgb(0.4, 0.3, 0.45), pm.MOSS)[k], sides=10,
                  solid=False)
        b.marker("loot", (8.0, 3.2, 0), table="military-crate")
        b.marker("guard", (2.0, -3.0, 0), 90.0, role="warden-drone")
        pm.code_plate(b, (-2.0, -4.68, 2.6), (0, 0, 0), 1.4, 0.45, base=ORANGE, ink=DARK, text="LAB-B")
    bridge(b, (B[0] + 13.0, B[1]), (C[0] - 4.5, B[1]), STILT + 0.25)
    # Lab C: the clean room module: an airlock (cyan chip and power cell), the clean room, the vault.
    lab(b, C, (0, 0, 90), 24.0, 9.0,
        {"back": [(6.0, 1.4, 2.3, 0, 0, {"kind_of": "airlock", "chip": "cyan", "cell": True, "room": "clean-room"})]
                 + [(x, 1.6, 0.8, 1.4, 0) for x in (-3.0, 1.0)],
         "front": [(x, 1.6, 0.8, 1.4, 0) for x in (-3.0, 1.0, 7.0)],
         "left": [(0.0, 1.2, 2.3, 0, 0, {"kind_of": "emergency", "state": "shut", "marker": False})]},
        band=srgb(0.55, 0.25, 0.75))
    with b.frame((C[0], C[1], STILT + 0.25), (0, 0, 90)):
        # Local x runs along the module (north is -x... the module is turned 90 degrees).
        pm.wall(b, (4.0, -4.35, 0), (4.0, 4.35, 0), 3.6, 0.2, [(4.35, 1.2, 2.2, 0, {"kind_of": "airlock-inner",
                                                                                       "state": "ajar"})],
                mat=PANEL, tint=srgb(0.86, 0.87, 0.86))
        pm.wall(b, (-7.0, 4.35, 0), (-7.0, -4.35, 0), 3.6, 0.3,
                [(4.35, 1.2, 2.3, 0, {"kind_of": "vault", "code": "vault-c", "state": "shut"})],
                mat=CONCRETE, tint=srgb(0.7, 0.72, 0.74), door_tint=srgb(0.45, 0.48, 0.5))
        b.box((0.2, 0.3, 0.4), (-6.8, 1.5, 1.3), tint=DARK, solid=False, vary=0)
        b.box((0.03, 0.2, 0.25), (-6.68, 1.5, 1.35), mat=LAMP, tint=srgb(0.3, 1.0, 0.5), solid=False, vary=0)
        b.marker("keypad", (-6.75, 1.5, 0.0), 90.0, opens="vault-c")
        for x in (-4.0, 0.0):
            for y in (-3.4, 3.4):
                b.box((2.8, 0.9, 0.95), (x, y, 0.475), tint=srgb(0.86, 0.88, 0.88))
                b.box((2.6, 0.7, 0.8), (x, y, 1.35), mat=GLASS, solid=False, vary=0)
                for k in (-0.6, 0.6):
                    b.cyl(0.09, 0.3, (x + k, y - 0.36 * (1 if y > 0 else -1), 1.3), (90, 0, 0), mat=MATTE,
                          tint=BLACK, sides=8, solid=False)
        b.box((2.0, 2.0, 0.1), (6.0, 0.0, 3.45), mat=LAMP, tint=srgb(0.85, 0.9, 1.0), solid=False, vary=0)
        b.marker("loot", (2.0, 0.0, 0), table="military-crate")
        b.marker("loot", (-9.5, 0.0, 0), table="sealed-crate", holds="schematics")
        b.marker("loot", (-10.5, 2.8, 0), table="crate")
        b.piece("rack-row", (-10.8, -2.8, 0), (0, 0, -90))
        pm.code_plate(b, (8.0, -4.68, 2.6), (0, 0, 0), 1.4, 0.45, base=srgb(0.55, 0.25, 0.75), ink=OFFWHITE,
                      text="LAB-C")
        pm.hazard_band(b, (-12.0, -4.68, 0), (12.0, -4.68, 0), 0.0, 0.3)
    # A drone dock on lab B's roof: pads with cyan lights.
    with b.frame((B[0], B[1], STILT + 0.25 + 3.6 + 0.25)):
        for x in (-6.0, 0.0):
            b.cyl(1.3, 0.12, (x, 0.0, 0.06), tint=DARK, sides=16)
            b.cyl(1.32, 0.05, (x, 0.0, 0.1), mat=LAMP, tint=pm.CHIP["cyan"], sides=16, radius2=1.28, solid=False,
                  vary=0)
            b.marker("guard", (x, 0.0, 0.2), 0.0, role="warden-drone", dock=True)


def utility(b):
    ux, uy = U
    pm.building(b, (ux, uy, 0), (0, 0, 90), 12.0, 8.0, floors=1, floor_h=4.2, mat=PANEL, tint=LAB_T,
                openings={"front": [(-3.0, 2.8, 3.0, 0, 0, {"kind_of": "shutter", "state": "ajar"}),
                                    (3.5, 1.2, 2.3, 0, 0)],
                          "back": [(-2.0, 1.4, 0.8, 2.6, 0)]},
                band=(TEAL, 3.2, 0.4), roof_units=2)
    with b.frame((ux, uy, 0), (0, 0, 90)):
        b.box((4.0, 2.2, 2.2), (-2.5, 1.4, 1.4), tint=srgb(0.28, 0.42, 0.36), bevel=0.04)
        b.box((1.2, 2.0, 1.8), (0.4, 1.4, 1.2), tint=DARK)
        b.box((0.9, 0.35, 1.3), (4.6, 3.6, 1.4), tint=srgb(0.5, 0.54, 0.52), bevel=0.02)
        b.box((0.5, 0.02, 0.1), (4.6, 3.42, 1.9), mat=LAMP, tint=pm.CHIP["cyan"], solid=False, vary=0)
        b.box((0.36, 0.14, 0.44), (4.6, 3.38, 1.0), tint=YELLOW, solid=False, vary=0)
        b.marker("fuse-box", (4.6, 3.3, 0.3), 180.0, takes="power-cell", powers="clean-room", id="fuse-u")
        b.marker("loot", (-5.0, -2.8, 0.3), table="fuel-barrel")
        b.marker("loot", (-3.0, -3.0, 0.3), table="fuel-barrel")
        pm.code_plate(b, (3.5, -4.17, 3.0), (0, 0, 0), 1.2, 0.4, base=YELLOW, ink=BLACK, text="U-1")
    tank(b, (ux - 9.0, uy - 10.0, 0), 2.2, 4.0, tint=srgb(0.82, 0.84, 0.82), band=TEAL, ladder_side=None)
    solar_array(b, (ux - 6.0, uy + 14.0, 0), (0, 0, 0), rows=2, cols=7, broken=0.2)
    for k in range(3):
        cable(b, (ux + 4.0, uy - 1.0 + k * 0.3, 3.8), (A[0] - 13.0, A[1] + 3.0 + k * 0.3, 5.0), sag=0.04,
              radius=0.03)
    pm.cable_trench(b, [(ux + 4.0, uy + 4.0, 0), (C[0] - 6.0, uy + 4.0, 0), (C[0] - 6.0, -12.0, 0)])


def radome(b):
    x, y = RADOME
    H = 22.0
    b.cyl(2.6, 1.0, (x, y, 0.5), mat=CONCRETE, tint=CONCRETE_T, sides=20)
    b.cyl(2.0, H, (x, y, H / 2), mat=CONCRETE, tint=srgb(0.86, 0.86, 0.84), sides=20, radius2=1.7)
    for z in (6.0, 12.0, 18.0):
        b.cyl(2.0 + (1.7 - 2.0) * z / H + 0.02, 0.6, (x, y, z), tint=(TEAL, ORANGE, TEAL)[int(z / 6) - 1], sides=20,
              solid=False, vary=0)
    pm.ring_deck(b, (x, y), 2.6, 6.0, H, sides=24)
    pm.ring_rail(b, (x, y), 5.9, H, sides=24)
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        b.beam((x + math.cos(a) * 1.9, y + math.sin(a) * 1.9, H - 4.0),
               (x + math.cos(a) * 5.6, y + math.sin(a) * 5.6, H - 0.15), 0.18, 0.18, tint=GREY)
    zc, rr = H + 5.2, 7.4
    prof = []
    for k in range(13):
        t = math.radians(-42.0 + (90.0 + 42.0) * k / 12)
        prof.append((rr * math.cos(t) if k < 12 else 0.0, rr * math.sin(t)))
    b.lathe([(rr * math.cos(math.radians(-42.0)), -5.0)] + prof[1:], (x, y, zc), mat=PANEL, tint=OFFWHITE, sides=32,
            solid="hull")
    pm.geodesic_dome(b, (x, y, zc), rr + 0.05, H + 0.2, freq=4, strut=0.09, tint=srgb(0.72, 0.74, 0.74), keep=0.0,
                     solid_below=0.0, panel_below=-1e9)
    ladder(b, (x, y - 2.05, 0), H, (0, 0, 0), cage=True)
    b.box((1.2, 0.12, 2.3), (x + 1.4, y - 1.6, 1.15 + 1.0), (0, 0, 35), tint=DARK, solid=False)
    pm.aviation_lamp(b, (x, y, H + 12.6), size=0.4)
    b.marker("loot", (x + 4.0, y + 0.5, H), table="crate")
    b.marker("landmark", (x, y, H + 12.6), kind_of="radome", visible_km=7)
    pm.stencil(b, (x, y - 1.98, 9.0), (0, 0, 0), "R0", height=1.6, tint=TEAL)


def test_range(b):
    rng = b.rng
    # The bunker: a squat concrete box under earth, slits toward the range, a steel door at the back.
    bx, by = 48.0, -30.0
    with b.frame((bx, by, 0), (0, 0, 90)):
        b.box((10.0, 7.0, 0.4), (0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        pm.wall(b, (-5.0, -3.3, 0.4), (5.0, -3.3, 0.4), 2.8, 0.6,
                [(2.5, 1.6, 0.25, 1.4), (5.0, 1.6, 0.25, 1.4), (7.5, 1.6, 0.25, 1.4)], mat=FORMWORK,
                tint=CONCRETE_T, glass=0.0)
        pm.wall(b, (5.0, 3.3, 0.4), (-5.0, 3.3, 0.4), 2.8, 0.6,
                [(5.0, 1.2, 2.2, 0, {"kind_of": "blast-door", "state": "ajar", "tint": srgb(0.4, 0.42, 0.4)})],
                mat=FORMWORK, tint=CONCRETE_T)
        for x in (-5.0, 5.0):
            pm.wall(b, (x, -3.3, 0.4), (x, 3.3, 0.4), 2.8, 0.6, [], mat=FORMWORK, tint=CONCRETE_T)
        b.box((12.4, 9.2, 1.0), (0, 0, 3.7), mat=FORMWORK, tint=CONCRETE_T)
        from monkit.structures import sandbags
        with b.frame((0, 0, 4.2)):
            sandbags(b, (-5.6, -4.0, 0), (5.6, -4.0, 0), height=2)
            sandbags(b, (5.6, -3.6, 0), (5.6, 3.8, 0), height=2)
            sandbags(b, (-5.6, -3.6, 0), (-5.6, 3.8, 0), height=2)
        b.piece("console", (0.0, -2.3, 0.4), (0, 0, 0))
        b.marker("loot", (-3.5, 1.8, 0.4), table="military-crate")
        b.marker("loot", (3.5, 2.0, 0.4), table="crate")
        lamp(b, (0.0, 3.95, 2.6), (0, 0, 180), size=(0.45, 0.25, 0.2))
        pm.hazard_band(b, (-5.3, -3.62, 0), (5.3, -3.62, 0), 0.4, 0.3)
    # The test stand, a blast wall, targets, craters, berms round the range.
    sx, sy = 80.0, -8.0
    for x in (-3.0, 3.0):
        b.box((0.5, 0.5, 7.0), (sx + x, sy, 3.5), tint=srgb(0.55, 0.57, 0.58))
    b.box((7.0, 0.6, 0.6), (sx, sy, 7.0), tint=srgb(0.55, 0.57, 0.58))
    cable(b, (sx, sy, 6.7), (sx, sy, 4.4), sag=0.0, radius=0.03)
    b.cyl(0.6, 1.8, (sx, sy, 3.5), tint=DARK, sides=14)
    b.cyl(0.62, 0.2, (sx, sy, 3.0), tint=YELLOW, sides=14, solid=False, vary=0)
    b.cyl(5.5, 0.03, (sx, sy, 0.02), mat=MATTE, tint=srgb(0.12, 0.11, 0.1), sides=24, solid=False, vary=0)
    b.box((12.0, 1.2, 4.0), (sx, sy + 9.0, 2.0), mat=FORMWORK, tint=CONCRETE_T)
    for k, (x, y) in enumerate(((70.0, -40.0), (78.0, -48.0), (88.0, -36.0), (66.0, -54.0))):
        b.box((0.12, 0.12, 2.2), (x - 0.7, y, 1.1), tint=DARK, solid=False)
        b.box((0.12, 0.12, 2.2), (x + 0.7, y, 1.1), tint=DARK, solid=False)
        b.box((1.6, 0.06, 1.6), (x, y, 1.6), (0, rng.uniform(-8, 8), rng.uniform(-10, 10)), mat=RUST if k % 2 else PAINT,
              tint=(1, 1, 1) if k % 2 else OFFWHITE)
    for (x, y, r) in ((74.0, -24.0, 3.0), (86.0, -56.0, 4.0), (62.0, -12.0, 2.5), (-80.0, -48.0, 5.0)):
        b.cyl(r, 0.02, (x, y, 0.02), mat=MATTE, tint=srgb(0.16, 0.14, 0.12), sides=18, solid=False, vary=0)
        for k in range(10):
            a = 2 * math.pi * k / 10 + rng.uniform(-0.2, 0.2)
            pm.blob(b, (x + math.cos(a) * r * 1.05, y + math.sin(a) * r * 1.05, 0.1), rng.uniform(0.4, 0.8),
                    rng.uniform(0.2, 0.4), srgb(0.38, 0.36, 0.33), sides=6)
    for (a, c) in (((40.0, -66.0), (96.0, -66.0)), ((98.0, -64.0), (98.0, 14.0)), ((40.0, 16.0), (96.0, 16.0))):
        dx, dy = c[0] - a[0], c[1] - a[1]
        ln = math.hypot(dx, dy)
        b.extrude([(-2.5, 0.0), (2.5, 0.0), (1.0, 2.2), (-1.0, 2.2)], ln, ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2, 0),
                  (0, 0, math.degrees(math.atan2(dy, dx)) - 90), mat=MATTE, tint=srgb(0.36, 0.33, 0.27))
    for (x, y) in ((44.0, -62.0), (94.0, 12.0), (94.0, -62.0)):
        pm.flag(b, (x, y, 0), 6.0, tint=RED, w=1.2, h=0.8)
    b.marker("guard", (70.0, -20.0, 0.0), 0.0, role="warden-drone")
    b.marker("guard", (60.0, 0.0, 0.0), 0.0, role="warden-drone")


def grounds(b):
    rng = b.rng
    hx, hy = 98.0, 72.0
    fence(b, [(6.0, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0), (-hx, -hy, 0), (-6.0, -hy, 0)], post=4.0)
    for x in (-6.0, 6.0):
        b.box((0.8, 0.8, 3.0), (x, -hy, 1.5), mat=CONCRETE, tint=CONCRETE_T)
    pm.code_plate(b, (-8.5, -hy - 0.1, 1.6), (0, 0, 0), 2.4, 0.6, base=OFFWHITE, ink=DARK, text="RS-30")
    road(b, [(0.0, -hy - 6.0, 0.0), (0.0, -40.0, 0.0), (-14.0, -32.0, 0.0)], width=6.0)
    b.piece("utility-truck", (-3.0, -46.0, 0), (0, 0, 80), tint=OFFWHITE)
    # Instrument masts and weather screens.
    for (x, y, h) in ((30.0, 44.0, 14.0), (-84.0, -30.0, 10.0), (60.0, 52.0, 12.0)):
        b.cyl(0.12, h, (x, y, h / 2), tint=GREY, sides=8, radius2=0.07)
        for z in (h * 0.5, h * 0.8, h):
            b.box((1.4, 0.06, 0.06), (x, y, z), tint=GREY, solid=False)
            b.cyl(0.18, 0.3, (x + 0.6, y, z + 0.15), tint=OFFWHITE, sides=10, solid=False)
        b.box((0.9, 0.9, 1.0), (x + 2.0, y, 1.2), tint=OFFWHITE, bevel=0.03)
        for k in range(3):
            b.box((0.12, 0.12, 0.7), (x + 2.0 + (k - 1) * 0.35, y + 0.35 * (k % 2), 0.35), tint=GREY, solid=False)
        pm.aviation_lamp(b, (x, y, h), size=0.25)
    for (x, y, a) in ((-36.0, -40.0, 135.0), (8.0, -26.0, 180.0), (-6.0, 22.0, 0.0), (30.0, 18.0, -45.0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, a))
    b.piece("crate-stack", (-40.0, -34.0, 0), (0, 0, 20))
    b.piece("drum-stack", (-54.0, -14.0, 0), (0, 0, 10))
    b.marker("loot", (-41.5, -36.0, 0.0), table="barrel")
    b.marker("guard", (-30.0, 20.0, 0.0), 0.0, role="warden-drone")
    for k in range(10):
        x, y = rng.uniform(-90, 90), rng.uniform(-66, 66)
        pm.blob(b, (x, y, 0.2), rng.uniform(0.6, 1.4), rng.uniform(0.3, 0.6), srgb(0.42, 0.4, 0.37), sides=7)


def extras(b):
    """A dormitory module on its own stilts, a garage with supplies, fuel tanks."""
    lab(b, (-26.0, 40.0), (0, 0, 0), 22.0, 8.0,
        {"front": [(-6.0, 1.3, 2.3, 0, 0, {"kind_of": "dormitory", "state": "ajar"})]
                  + [(x, 1.4, 1.0, 1.1, 0) for x in (-9.0, -2.0, 2.0, 6.0)],
         "back": [(x, 1.4, 1.0, 1.1, 0) for x in (-8.0, -4.0, 0.0, 4.0, 8.0)]},
        band=TEAL, stairs_at=(-6.0, "front"))
    with b.frame((-26.0, 40.0, STILT + 0.25)):
        for x in (-8.5, -5.0, -1.5, 2.0, 5.5):
            b.box((0.9, 2.0, 0.5), (x, 2.6, 0.45), tint=srgb(0.4, 0.45, 0.5))
            b.box((0.9, 2.0, 0.5), (x, 2.6, 1.6), tint=srgb(0.4, 0.45, 0.5), solid=False)
            for yy in (1.7, 3.5):
                b.box((0.06, 0.06, 1.9), (x - 0.42, yy, 0.95), tint=DARK, solid=False)
        b.marker("loot", (8.0, 2.5, 0), table="crate")
        b.marker("loot", (-9.5, -2.8, 0), table="barrel")
    from monkit.structures import shed
    shed(b, (40.0, 42.0, 0), (0, 0, 0), 18.0, 12.0, 5.5, doors=[("front", -3.0, 5.0, 4.0)], tint=LAB_T,
         frame_tint=srgb(0.55, 0.58, 0.6), ruin=0.08)
    b.piece("utility-truck", (37.0, 41.0, 0), (0, 0, 90), tint=OFFWHITE)
    b.marker("loot", (45.0, 45.0, 0.25), table="crate")
    for i in range(3):
        b.piece("container-20", (56.0, 34.0 + i * 3.0, 0), (0, 0, 2 - i * 2),
                tint=(OFFWHITE, srgb(0.2, 0.36, 0.52), srgb(0.78, 0.45, 0.16))[i])
    b.piece("crate-stack", (52.0, 28.0, 0), (0, 0, 15))
    for k in range(2):
        b.cyl(1.3, 6.0, (-74.0, -18.0 + k * 3.4, 1.8), (0, 90, 0), tint=srgb(0.82, 0.84, 0.82), sides=18)
        for x in (-76.0, -72.0):
            b.box((0.5, 2.2, 0.8), (x, -18.0 + k * 3.4, 0.4), mat=CONCRETE, tint=CONCRETE_T)


def build(b):
    b.set_pad(("rect", 204.0, 152.0), ground="gravel", blend=14.0, clear=6.0)
    labs(b)
    utility(b)
    radome(b)
    test_range(b)
    grounds(b)
    extras(b)
    b.zone("no-build", (0, 0, 0), size=(240.0, 190.0, 80.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-civil", "spawn": "odd-ground",
                   "puzzle": ["terminal lab A -> code", "amber: lab B specimen lab", "cell: fuse-u powers the clean room",
                              "cyan + power: lab C airlock", "code: vault"]})


SUN = (30.0, 210.0)
PEOPLE = [((-30.0, -32.0, 0), 20.0), ((RADOME[0] + 3.0, RADOME[1] - 3.0, 22.0), 160.0)]
VIEWS = [
    {"name": "overview", "target": (2, -4, 8), "distance": 185, "yaw": 30, "pitch": 26, "lens": 32},
    {"name": "labs", "target": (-20, -10, 5), "distance": 40, "yaw": 22, "pitch": -3, "lens": 24},
    {"name": "range", "target": (66, -26, 4), "distance": 46, "yaw": -50, "pitch": 6, "lens": 26},
]
