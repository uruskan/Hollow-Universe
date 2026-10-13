"""Transit Station (MONUMENTS.md §5.3, medium tier): a Seedward rail station on the old line. An island platform
between two tracks under a 110 m barrel-vault canopy on branching steel columns (its silhouette, with the clock
tower), a ticket hall joined to the platform by a covered footbridge, an abandoned train, a signal box, and at the
east end the portal where the line goes underground: a concrete headwall in a built embankment, the tunnel's first
30 m descending inside it to where the tunnel network begins (§5.9). Reclaimers hold the tunnel mouth.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, ladder, lamp, railing, rubble, srgb, stairs)
from monkit.structures import rail_track, road
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PANEL, RUST

KEY = "transit-station"
TITLE = "Transit Station"
SEED = 2828
BUILDER = {"chunk": 48.0, "max_edge": 4.0}

TRACKS = (-6.4, 6.4)
PLAT = 1.1            # platform height
PX0, PX1 = -66.0, 36.0  # platform ends
PORTAL_X = 62.0
HALL = (-24.0, -27.0)


def tracks(b):
    for y in TRACKS:
        rail_track(b, [(-100.0, y, 0.0), (PORTAL_X, y, 0.0)])
        # Inside the tunnel the track runs on down the grade.
        rail_track(b, [(PORTAL_X, y, 0.0), (PORTAL_X + 34.0, y, -1.4)], ballast=True)
    # The island platform: a slab with copings, yellow edge lines, benches, lamps, signs.
    b.box((PX1 - PX0, 9.0, PLAT), ((PX0 + PX1) / 2, 0.0, PLAT / 2), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
    for y in (-4.6, 4.6):
        b.box((PX1 - PX0, 0.5, 0.12), ((PX0 + PX1) / 2, y, PLAT + 0.0), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        b.box((PX1 - PX0, 0.3, 0.01), ((PX0 + PX1) / 2, y * 0.88, PLAT + 0.005), tint=YELLOW, solid=False, vary=0,
              wear=0.6)
    pm.concrete_steps(b, (PX0 - 2.6, 0.0, 0.0), (0, 0, -90), 3.0, PLAT, 2.6)
    for k, x in enumerate(range(int(PX0) + 8, int(PX1) - 4, 14)):
        with b.frame((x, 0.0, PLAT)):
            b.box((2.4, 0.5, 0.08), (0, 1.6, 0.45), mat=MATTE, tint=pm.WOOD, solid=False)
            for xx in (-1.0, 1.0):
                b.box((0.08, 0.4, 0.45), (xx, 1.6, 0.22), tint=DARK, solid=False)
            pm.code_plate(b, (0, -1.8, 3.4), (0, 0, 0), 1.4, 0.45, base=TEAL, ink=OFFWHITE, text="TS-28")
            b.box((0.08, 0.08, 1.4), (0, -1.8, 2.5), tint=DARK, solid=False)
        if k % 2 == 0:
            b.marker("loot", (x + 3.0, -2.5, PLAT), table="barrel")
    for (x, a) in ((-40.0, 0.0), (6.0, 15.0)):
        pm.barricade(b, (x, 0.5, PLAT), (0, 0, a), 6.0)
    b.marker("guard", (-34.0, 1.0, PLAT), 0.0, role="reclaimer")
    b.marker("guard", (20.0, -1.0, PLAT), 180.0, role="reclaimer")


def canopy(b):
    """A barrel vault over the platform and both tracks on branching steel columns; bays of glass and sheet."""
    w, z0, crown = 26.0, 7.0, 11.5
    n = 16
    R = (w * w / 4 + (crown - z0) ** 2) / (2 * (crown - z0))
    zc = crown - R
    half = math.asin((w / 2) / R)
    outer = [(R * math.sin(-half + 2 * half * i / n), zc + R * math.cos(-half + 2 * half * i / n)) for i in range(n + 1)]
    outer = [(-x, z) for x, z in outer]
    outer.sort(key=lambda p: -p[0])
    inner = [(x * 0.995, z - 0.12) for x, z in reversed(outer)]
    bays = 17
    length = 110.0
    x0 = -70.0
    for k in range(bays):
        x = x0 + length * (k + 0.5) / bays
        glass = k % 3 == 1
        if glass and b.rng.random() < 0.4:
            continue
        b.extrude(outer + inner, length / bays - 0.08, (x, 0.0, 0.0), (0, 0, 90), mat=GLASS if glass else CORRUGATED,
                  tint=(1, 1, 1) if glass else srgb(0.62, 0.68, 0.7), solid=False)
    steel = srgb(0.30, 0.42, 0.45)
    for k in range(bays + 1):
        x = x0 + length * k / bays
        b.tube([(x, xx, z) for xx, z in outer], 0.18, tint=steel, sides=6)
        b.beam((x, -w / 2, z0), (x, w / 2, z0), 0.2, 0.3, tint=steel)
    for y in (-w / 2, w / 2):
        b.box((length, 0.4, 0.6), (x0 + length / 2, y, z0 - 0.2), tint=steel)
        b.box((length, 0.05, 0.4), (x0 + length / 2, y * 1.02, z0 - 0.2), tint=ORANGE, solid=False, vary=0)
    # Tree columns on the platform: a trunk branching into four struts.
    for k in range(9):
        x = x0 + 8.0 + k * 12.0
        if x < PX0 + 1.0 or x > PX1 - 1.0:
            continue
        b.cyl(0.32, 5.4, (x, 0.0, PLAT + 2.7), tint=steel, sides=12)
        b.cyl(0.5, 0.4, (x, 0.0, PLAT + 0.2), mat=CONCRETE, tint=CONCRETE_T, sides=12)
        top = (x, 0.0, PLAT + 5.4)
        for dx, dy in ((-4.0, -9.0), (4.0, -9.0), (-4.0, 9.0), (4.0, 9.0)):
            zz = zc + math.sqrt(max(R * R - dy * dy, 0.0)) - 0.2
            b.beam(top, (x + dx, dy, zz), 0.22, 0.22, tint=steel)
        lamp(b, (x, 0.0, PLAT + 5.0), (90, 0, 0), size=(0.7, 0.3, 0.2))
    b.marker("landmark", (x0 + length / 2, 0.0, crown + 0.5), kind_of="station-canopy", visible_km=5)


def hall(b):
    hx, hy = HALL
    w, d = 32.0, 14.0
    info = pm.building(b, (hx, hy, 0), (0, 0, 0), w, d, floors=2, floor_h=4.2, mat=PANEL, tint=OFFWHITE,
                       openings={"front": [(-4.0, 3.0, 2.8, 0, 0, {"kind_of": "entrance", "state": "open"}),
                                           (4.0, 3.0, 2.8, 0, 0, {"kind_of": "entrance", "state": "gone"})]
                                          + [(x, 2.6, 2.0, 0.9, 0) for x in (-12.0, -9.0, 9.0, 12.0)]
                                          + [(x, 2.6, 2.2, 0.8, 1) for x in (-12.0, -6.0, 0.0, 6.0, 12.0)],
                                 "back": [(-8.0, 2.4, 2.6, 0, 0, {"kind_of": "platform", "state": "ajar"}),
                                          (2.0, 2.4, 2.4, 0, 1, {"kind_of": "footbridge", "state": "open"})]
                                         + [(x, 2.6, 2.0, 0.9, 0) for x in (4.0, 10.0)]
                                         + [(x, 2.6, 2.2, 0.8, 1) for x in (-12.0, -6.0, 8.0, 12.0)],
                                 "left": [(0.0, 2.0, 2.0, 0.9, 1)], "right": [(0.0, 2.0, 2.0, 0.9, 1)]},
                       holes=[(9.4, -6.4, 10.8, -0.6, 1)], band=(TEAL, 4.4, 0.5), roof_units=3, glass=0.45)
    f0, f1 = info["floor"]
    with b.frame((hx, hy, 0)):
        stairs(b, (10.1, -6.3, f0), (0, 0, 0), 1.3, f1 - f0, 5.6)
        for k, x in enumerate((-10.0, -6.0, -2.0)):
            b.box((3.0, 0.8, 1.1), (x, 3.0, f0 + 0.55), mat=PANEL, tint=srgb(0.8, 0.8, 0.78))
            b.box((3.0, 0.05, 0.8), (x, 2.6, f0 + 1.6), mat=GLASS, solid=False, vary=0)
        for x in (0.0, 4.0):
            with b.frame((x, -2.0, f0)):
                b.box((2.4, 0.5, 0.08), (0, 0, 0.45), mat=MATTE, tint=pm.WOOD, solid=False)
        b.box((8.0, 0.1, 2.2), (2.0, d / 2 - 0.25, f0 + 2.6), mat=PAINT, tint=srgb(0.18, 0.2, 0.22))
        for k in range(6):
            b.box((1.0, 0.04, 0.22), (-0.5 + (k % 3) * 2.6, d / 2 - 0.32, f0 + 3.2 - (k // 3) * 0.5), mat=LAMP,
                  tint=srgb(1.0, 0.7, 0.2), solid=False, vary=0)
        b.marker("loot", (-12.0, 3.5, f0), table="crate")
        b.marker("loot", (-2.0, 4.5, f0), table="crate")
        b.marker("loot", (6.0, 4.0, f1), table="barrel")
        b.marker("loot", (-10.0, -4.0, f1), table="crate")
        pm.code_plate(b, (0.0, -d / 2 - 0.2, 3.6), (0, 0, 0), 4.0, 0.7, base=TEAL, ink=OFFWHITE, text="TS-28")
        b.box((12.0, 3.0, 0.3), (0.0, -d / 2 - 1.5, 3.4), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        pm.mark(b, (-14.0, -d / 2 - 0.2, 1.6), (0, 0, 0), 1.8, "slash", pm.GRAFFITI[0])
        # The clock tower at the west corner.
        tx, ty, H = -w / 2 - 2.6, -d / 2 + 2.6, 26.0
        b.box((5.2, 5.2, H), (tx, ty, H / 2), mat=PANEL, tint=OFFWHITE)
        for z in (9.0, 17.0):
            b.box((5.3, 5.3, 0.6), (tx, ty, z), tint=TEAL, solid=False, vary=0)
        for a in range(4):
            with b.frame((tx, ty, H - 3.2), (0, 0, 90 * a)):
                b.cyl(1.6, 0.12, (0, -2.66, 0), (90, 0, 0), tint=OFFWHITE, sides=24)
                b.cyl(1.66, 0.1, (0, -2.62, 0), (90, 0, 0), tint=DARK, sides=24, solid=False)
                b.box((0.12, 0.05, 1.2), (0, -2.75, 0.45), (0, 25.0, 0), tint=DARK, solid=False, vary=0)
                b.box((0.1, 0.05, 0.9), (0, -2.75, -0.2), (0, -70.0, 0), tint=DARK, solid=False, vary=0)
        b.box((6.0, 6.0, 0.5), (tx, ty, H + 0.25), tint=TEAL)
        b.cyl(0.6, 2.4, (tx, ty, H + 1.7), tint=OFFWHITE, sides=10)
        b.cyl(0.5, 0.6, (tx, ty, H + 3.2), mat=LAMP, tint=srgb(0.4, 1.0, 0.5), sides=10, solid=False, vary=0)
        b.marker("light", (tx, ty, H + 3.4), colour=[0.4, 1.0, 0.5], range=200.0, blink=1.0)
        b.marker("landmark", (tx, ty, H + 3.6), kind_of="clock-tower", visible_km=5)
    # The covered footbridge over the south track to the platform, a stair down at its end.
    fy0, fy1 = hy + d / 2, -3.0
    fx = hx + 2.0
    zf = f1
    with b.frame((fx, fy0, zf)):
        ln = fy1 - fy0
        b.box((3.0, ln, 0.3), (0, ln / 2, -0.15), mat=CONCRETE, tint=CONCRETE_T)
        b.box((3.4, ln, 0.2), (0, ln / 2, 3.0), mat=CORRUGATED, tint=srgb(0.62, 0.68, 0.7), solid=False)
        for x in (-1.5, 1.5):
            pm.wall(b, (x, 0.0, 0.0), (x, ln, 0.0), 2.9, 0.12, [(ln * (k + 0.5) / 4, 2.0, 1.2, 1.0) for k in range(4)],
                    mat=PANEL, tint=OFFWHITE, glass=0.3, extend=False)
        for yy in (ln * 0.35, ln * 0.7):
            for x in (-1.6, 1.6):
                b.box((0.3, 0.3, zf), (x, yy, -zf / 2), tint=srgb(0.3, 0.42, 0.45))
    pm.deck(b, fx - 1.5, fy1, fx + 1.5, fy1 + 1.8, zf, rails=("back", "right"))
    stairs(b, (fx - 1.5 - 7.2, fy1 + 0.9, PLAT), (0, 0, -90), 1.6, zf - PLAT, 7.2)


def portal(b):
    """The embankment, the headwall with two bores, the tunnel descending inside, the Reclaimers' camp at the
    mouth."""
    px = PORTAL_X
    # The embankment: a prism of earth over the tunnel, rock-faced at the headwall.
    outline = [(0.4, 0.0), (38.0, 0.0), (38.0, 8.4), (19.0, 10.2), (0.4, 9.6)]
    cap = [(0.4, 8.1), (38.0, 8.1), (38.0, 8.4), (19.0, 10.2), (0.4, 9.6)]
    earth = srgb(0.34, 0.38, 0.22)
    edges = [-33.0] + [e for ty in sorted(TRACKS) for e in (ty - 3.6, ty + 3.6)] + [33.0]
    for k in range(0, len(edges), 2):
        y0, y1 = edges[k], edges[k + 1]
        b.extrude(outline, y1 - y0, (px, (y0 + y1) / 2, 0.0), (0, 0, 0), mat=MATTE, tint=earth, solid=False)
    for ty in TRACKS:
        b.extrude(cap, 7.2, (px, ty, 0.0), (0, 0, 0), mat=MATTE, tint=earth, solid=False)
    b.collide_box((px + 19.0, 0.0, 9.6), (38.0, 66.0, 0.4), (0, 0, 0))
    for y in (-33.0, 33.0):
        b.collide_box((px + 19.0, y, 4.8), (38.0, 0.4, 9.6), (0, 0, 0))
    for k in range(12):
        pm.shrub(b, (px + b.rng.uniform(6.0, 36.0), b.rng.uniform(-30.0, 30.0), 9.6), b.rng.uniform(0.8, 1.6))
    # The headwall: concrete with two arched bores and wing walls.
    hw = 0.9
    with b.frame((px, 0.0, 0.0)):
        edges = [-33.0] + [e for ty in sorted(TRACKS) for e in (ty - 3.0, ty + 3.0)] + [33.0]
        for k in range(0, len(edges), 2):
            y0, y1 = edges[k], edges[k + 1]
            b.box((hw, y1 - y0, 9.6), (0.0, (y0 + y1) / 2, 4.8), mat=FORMWORK, tint=CONCRETE_T)
        for y in TRACKS:
            b.box((hw, 6.0, 2.8), (0.0, y, 8.2), mat=FORMWORK, tint=CONCRETE_T)
            arch = [(3.0 * math.cos(math.pi * i / 10), 6.8 + 1.4 * math.sin(math.pi * i / 10)) for i in range(11)]
            b.extrude([(3.0, 6.8)] + arch[1:-1] + [(-3.0, 6.8), (-3.0, 6.75), (3.0, 6.75)], hw, (0.0, y, 0.0),
                      (0, 0, 90), mat=FORMWORK, tint=CONCRETE_T, solid=False)
            b.box((hw + 0.2, 6.4, 0.6), (0.0, y, 9.9), mat=CONCRETE, tint=srgb(0.6, 0.58, 0.55))
        b.box((hw + 0.6, 46.0, 0.6), (0.0, 0.0, 9.9), mat=CONCRETE, tint=srgb(0.62, 0.6, 0.57))
        pm.stencil(b, (-0.5, 0.0, 7.8), (0, 0, -90), "T-28", height=1.4, tint=OFFWHITE)
        pm.mark(b, (-0.5, -18.0, 2.0), (0, 0, -90), 2.6, "skull", pm.GRAFFITI[0])
        pm.mark(b, (-0.5, 16.0, 2.4), (0, 0, -90), 2.0, "slash", pm.GRAFFITI[2])
        for sgn in (-1, 1):
            with b.frame((0.0, sgn * 33.0, 0.0), (0, 0, 180.0 - sgn * 18.0)):
                b.extrude([(0.0, 0.0), (14.0, 0.0), (14.0, 1.2), (0.0, 9.6)], 0.8, (0, 0, 0), (0, 0, 0),
                          mat=FORMWORK, tint=CONCRETE_T)
    # The tunnel: two concrete boxes running 34 m into the embankment and down 1.4 m.
    ang = math.degrees(math.atan2(1.4, 34.0))
    for y in TRACKS:
        with b.frame((px + 17.0, y, -0.7), (0, ang, 0)):
            b.box((34.0, 6.8, 0.4), (0.0, 0.0, -0.2), mat=CONCRETE, tint=srgb(0.5, 0.5, 0.48))
            for yy in (-3.6, 3.6):
                b.box((34.0, 0.6, 7.2), (0.0, yy, 3.6), mat=FORMWORK, tint=srgb(0.55, 0.55, 0.52))
            b.box((34.0, 7.8, 0.6), (0.0, 0.0, 7.5), mat=FORMWORK, tint=srgb(0.55, 0.55, 0.52))
            for k in range(5):
                b.box((0.2, 0.3, 0.2), (-14.0 + k * 7.0, 3.2, 6.0), mat=LAMP, tint=srgb(1.0, 0.6, 0.25),
                      solid=False, vary=0)
        b.marker("tunnel-link", (px + 34.0, y, -1.4), 270.0, leads="tunnel-network", line="seedward-main")
    for y in TRACKS:
        b.box((0.8, 7.0, 8.0), (px + 34.6, y, 2.6), mat=FORMWORK, tint=srgb(0.5, 0.5, 0.48))
        rubble(b, (px + 32.0, y, -1.2), 2.6, count=8, tint=srgb(0.55, 0.54, 0.5), max_size=1.2)
    # The Reclaimers' camp in the mouth.
    pm.tarp_shelter(b, (px + 6.0, -8.0, 0.4), (0, 0, 90), 4.0, 3.5, tint=pm.TARP_BLUE)
    b.piece("fire-barrel", (px + 4.0, -4.6, 0.3))
    pm.barricade(b, (px - 3.0, 8.0, 0.0), (0, 0, 90), 5.0)
    pm.barricade(b, (px - 3.0, -8.0, 0.0), (0, 0, 90), 5.0)
    pm.string_lights(b, (px + 1.0, -11.0, 6.4), (px + 1.0, 11.0, 6.4), sag=0.05)
    b.marker("guard", (px + 8.0, -8.0, 0.0), 270.0, role="reclaimer")
    b.marker("guard", (px + 14.0, 8.0, -0.6), 270.0, role="reclaimer")
    b.marker("guard", (px + 26.0, -8.0, -1.0), 270.0, role="reclaimer-heavy")
    b.marker("loot", (px + 10.0, -6.0, 0.0), table="military-crate")
    b.marker("loot", (px + 22.0, 7.0, -0.9), table="crate")
    b.marker("loot", (px + 30.0, -6.0, -1.2), table="crate")


def rolling_stock(b):
    tints = (srgb(0.9, 0.45, 0.15), srgb(0.88, 0.88, 0.84), srgb(0.2, 0.42, 0.46))
    for k, x in enumerate((-48.0, -27.6)):
        b.piece("railcar", (x, TRACKS[1], 0.5), (0, 0, 0), tint=tints[k % 3])
    b.piece("railcar", (-6.6, TRACKS[1] + 0.8, 0.9), (4.0, 0, 3.0), tint=tints[2])
    for k in range(2):
        b.marker("loot", (-48.0 + k * 20.4, TRACKS[1], 1.7), table="crate")
    # A signal box on stilts by the line, a signal gantry over the tracks.
    sx, sy = 44.0, 22.0
    for x in (-2.0, 2.0):
        for y in (-1.6, 1.6):
            b.box((0.3, 0.3, 4.0), (sx + x, sy + y, 2.0), tint=DARK)
    pm.building(b, (sx, sy, 4.0), (0, 0, 180), 5.6, 4.2, floors=1, floor_h=2.8, mat=PANEL, tint=srgb(0.84, 0.84, 0.8),
                openings={"front": [(x, 1.4, 1.0, 1.0, 0) for x in (-1.6, 0.0, 1.6)], "left": [(0.0, 0.9, 2.1, 0, 0)]},
                plinth=0.2, parapet=0.3, roof_units=0, downpipes=False, steps=False, band=(ORANGE, 2.2, 0.3))
    stairs(b, (sx + 4.6, sy + 4.0, 0.0), (0, 0, 180), 1.0, 4.2, 5.0)
    pm.deck(b, sx + 2.8, sy - 2.0, sx + 5.4, sy + 0.4, 4.2, rails=("front", "right"))
    b.marker("loot", (sx, sy, 4.2), table="crate")
    gx = 50.0
    for y in (-14.0, 14.0):
        b.box((0.4, 0.4, 7.0), (gx, y, 3.5), tint=DARK)
    b.box((0.5, 28.4, 0.6), (gx, 0.0, 7.0), tint=DARK)
    for y in TRACKS:
        b.box((0.6, 0.8, 1.6), (gx - 0.4, y, 6.0), tint=BLACK)
        for k, c in enumerate((srgb(1.0, 0.15, 0.08), srgb(0.2, 1.0, 0.4))):
            b.box((0.05, 0.3, 0.3), (gx - 0.72, y, 6.4 - k * 0.6), mat=LAMP if k == 0 else PAINT, tint=c, solid=False,
                  vary=0)


def grounds(b):
    road(b, [(HALL[0], -60.0, 0.0), (HALL[0], HALL[1] - 9.0, 0.0)], width=7.0)
    b.box((60.0, 14.0, 0.1), (HALL[0], -44.0, 0.05), mat="M_Mon_Asphalt", tint=(1, 1, 1))
    for k in range(10):
        b.box((0.15, 5.0, 0.012), (HALL[0] - 27.0 + k * 6.0, -44.0, 0.106), tint=OFFWHITE, solid=False, vary=0, wear=0.7)
    b.piece("utility-truck", (HALL[0] + 18.0, -44.0, 0), (0, 0, 90), tint=OFFWHITE)
    for (x, y, a) in ((-60.0, -18.0, 180.0), (10.0, -18.0, 180.0), (-80.0, 18.0, 0.0), (30.0, 20.0, 0.0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, a))
    b.piece("cable-reel", (20.0, -20.0, 0), (0, 0, 20))
    b.piece("crate-stack", (-62.0, -24.0, 0), (0, 0, 10))
    b.marker("loot", (-60.5, -26.0, 0.0), table="crate")
    rubble(b, (34.0, -24.0, 0.0), 2.5, count=6, tint=CONCRETE_T, max_size=0.8)
    for k in range(10):
        x, y = b.rng.uniform(-98, 58), b.rng.choice((-1, 1)) * b.rng.uniform(16.0, 38.0)
        if -45.0 < x < -2.0 and y < -14.0:
            continue
        pm.shrub(b, (x, y, 0.0), b.rng.uniform(0.6, 1.3))


def build(b):
    b.set_pad(("rect", 204.0, 84.0), ground="gravel", blend=14.0, clear=6.0)
    tracks(b)
    canopy(b)
    hall(b)
    portal(b)
    rolling_stock(b)
    grounds(b)
    b.zone("no-build", (0, 0, 0), size=(240.0, 120.0, 60.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-civil", "spawn": "rail-lines",
                   "underground": "tunnel-link"})


SUN = (30.0, 215.0)
PEOPLE = [((-30.0, -2.0, PLAT), 20.0), ((HALL[0] + 2.0, -44.0, 0), 0.0)]
VIEWS = [
    {"name": "overview", "target": (-8, 0, 6), "distance": 190, "yaw": 32, "pitch": 24, "lens": 32},
    {"name": "platform", "target": (-14, 0, 6), "distance": 46, "yaw": 272, "pitch": -4, "lens": 22},
    {"name": "portal", "target": (64, 0, 5), "distance": 46, "yaw": 248, "pitch": 2, "lens": 26},
]
