"""Colony Ruins (MONUMENTS.md §5.4, large tier, catalogue 44): a colonists' prefab town 600 x 500 m in a river valley,
abandoned in the Quiet and burnt since. A grid of streets with burnt cars and Reclaimer barricades; blocks of
two- to four-storey prefab flats with outside galleries, shops, clusters of hab cans; a plaza with the landing
memorial and a market of Reclaimer stalls; the clinic; the communal hall; two domes, the big one's glass half gone;
the water tower, three wind turbines (one broken) and a comms mast on the skyline; the fuel cell works in the
south-east corner where the contamination is worst.

Gameplay: many low-tier crates and barrels through the buildings; contamination 1 in the west streets, 2 in the
middle and east, 3 at the fuel cell works; Reclaimers on the streets and roofs, a heavy at the works.
"""
import math

from mathutils import Vector

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, cable, fence,
                        ladder, lamp, railing, rubble, srgb, stairs, stripes, tank)
from monkit.structures import road, sandbags, solar_array, wind_turbine
from monkit.surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL,
                             RUST)

KEY = "colony-ruins"
TITLE = "Colony Ruins"
SEED = 4401
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [300.0, 1400.0, 20000.0]

AVENUES = (-160.0, -80.0, 0.0, 80.0, 160.0)          # along x
STREETS = (-225.0, -135.0, -45.0, 45.0, 135.0, 225.0)  # along y
FH = 3.2
PALETTE = [P.CREAM, srgb(0.72, 0.80, 0.78), srgb(0.86, 0.70, 0.52), srgb(0.70, 0.74, 0.84), srgb(0.84, 0.82, 0.70),
           srgb(0.80, 0.62, 0.55), OFFWHITE]
ACCENTS = [ORANGE, TEAL, srgb(0.70, 0.20, 0.16), srgb(0.22, 0.36, 0.60), srgb(0.40, 0.55, 0.30)]
SOOT_T = srgb(0.07, 0.065, 0.06)


def soot(b, x, y, z, w=1.6, h=1.4):
    """Burn marks above an opening on a wall facing -Y of the current frame."""
    b.box((w, 0.03, h), (x, y - 0.02, z + h / 2), mat=MATTE, tint=SOOT_T, solid=False, vary=0.1)
    b.box((w * 0.6, 0.035, h * 0.6), (x + b.rng.uniform(-0.3, 0.3), y - 0.03, z + h + h * 0.3), mat=MATTE,
          tint=srgb(0.14, 0.13, 0.12), solid=False, vary=0.1)


def flats(b, at, yaw, w, floors, tint, accent, ruin=0.3, d=10.0, loot=True):
    """A colonists' prefab block: storeys of rooms with windows front and back (most panes gone), an outside gallery
    on each upper floor reached by a stair from the street and ladders between the galleries, coloured panels, soot,
    roof tanks and panels; the top floor burnt out to wall stubs when ruined."""
    with b.frame(at, (0, 0, yaw)):
        cols = max(2, int(w / 4.2))
        xs = [-w / 2 + w * (i + 0.5) / cols for i in range(cols)]
        gutted = b.rng.random() < ruin
        for k in range(floors):
            z = k * FH
            top = k == floors - 1
            h = FH if not (top and gutted) else 1.4
            front = [(-w / 2 + 1.6, 1.1, 2.2, 0.0)] + [(x, 1.5, 1.3, 0.9) for x in xs[1:]]
            if top and gutted:
                front = [(x, 1.5, 0.6, 0.9) for x in xs[1:]]
            back = [(x, 1.5, 1.3, 0.9) for x in xs] if not (top and gutted) else []
            P.room(b, (0, 0, z), (0, 0, 0), w, d, h, thick=0.25, mat=PANEL, tint=tint,
                   openings={"front": front, "back": back}, roof=(top and not gutted), roof_mat=CONCRETE,
                   roof_tint=srgb(0.55, 0.55, 0.52))
            for x in xs[1:]:
                if b.rng.random() < 0.3 and not (top and gutted):
                    b.box((1.45, 0.03, 1.25), (x, -d / 2 + 0.08, z + 0.9 + 0.65), mat=GLASS, solid=False, vary=0)
                if b.rng.random() < 0.25 + 0.4 * ruin:
                    soot(b, x, -d / 2, z + 2.2, 1.8, 1.0)
            b.box((w + 0.3, 0.3, 0.3), (0, -d / 2 - 0.1, z + 0.05), mat=PAINT, tint=accent, solid=False, vary=0)
            if k > 0:
                x_w = -w / 2 - (1.6 if k == 1 else 0.2)
                hole = (w / 2 - 1.2, -d / 2 - 1.5, w / 2, -d / 2 - 0.3) if k >= 2 else None
                gaps = {"s": [(-w / 2 - 1.6, -w / 2 - 0.2)]} if k == 1 else {}
                P.deck(b, x_w, -d / 2 - 1.5, w / 2, -d / 2, z, thick=0.15, mat=GRATE, tint=GREY, rails="s",
                       rail_tint=accent, edge=False, hole=hole, gaps=gaps)
                if k < floors - 1 and not (k == floors - 2 and gutted):
                    ladder(b, (w / 2 - 0.6, -d / 2 - 1.0, z), FH, (0, 0, 0), cage=False, tint=accent)
            if loot and (k == 0 or b.rng.random() < 0.5):
                b.marker("loot", (b.rng.uniform(-w / 2 + 2, w / 2 - 2), b.rng.uniform(-d / 2 + 1.5, d / 2 - 1.5),
                                  z + 0.1), table="crate" if b.rng.random() < 0.6 else "barrel")
        if floors > 1:
            stairs(b, (-w / 2 - 0.9, -d / 2 - 1.5 - 4.4, 0.0), (0, 0, 0), 1.1, FH, 4.4, tint=accent)
        b.marker("door", (-w / 2 + 1.6, -d / 2 - 0.6, 0), kind_of="building")
        # Colour panels between the windows, tanks and solar panels on the roof.
        for i, x in enumerate(xs):
            if i % 2 == 0:
                b.box((w / cols * 0.4, 0.04, floors * FH * 0.9), (x - w / cols * 0.5, -d / 2 - 0.03,
                                                                  floors * FH * 0.45), mat=PAINT,
                      tint=accent if i % 4 == 0 else tint, solid=False, vary=0.05)
        rz = floors * FH + 0.3
        if not gutted:
            b.cyl(0.9, 1.8, (w / 2 - 2.0, d / 2 - 2.0, rz + 0.9), mat=PAINT, tint=LIGHTGREY, sides=12)
            for k in range(max(1, cols // 2)):
                b.box((2.0, 1.6, 0.06), (-w / 2 + 2.0 + k * 3.0, 0.5, rz + 0.6), (-25, 0, 0), mat=GLOSS,
                      tint=srgb(0.12, 0.16, 0.28), solid=False)
        else:
            rubble(b, (0.0, 0.0, (floors - 1) * FH + 0.1), min(w, d) * 0.35, count=8, tint=tint, mat=PANEL,
                   max_size=1.4)
            P.scorch(b, (0.0, 0.0, (floors - 1) * FH + 0.15), 3.0, blobs=3)


def shop(b, at, yaw, w, tint, accent, d=12.0, sign=None):
    """A one-storey shop: a wide broken shop front under an awning, a back door, a painted sign."""
    with b.frame(at, (0, 0, yaw)):
        P.room(b, (0, 0, 0), (0, 0, 0), w, d, 3.8, thick=0.25, mat=PANEL, tint=tint,
               openings={"front": [(0.0, w - 3.0, 2.6, 0.4)], "back": [(w / 4, 1.1, 2.2, 0.0)]},
               roof_mat=CONCRETE, roof_tint=srgb(0.55, 0.55, 0.52))
        b.box((w - 2.6, 1.8, 0.12), (0, -d / 2 - 0.9, 3.0), (-12, 0, 0), mat=PAINT, tint=accent, solid=False)
        b.box((w * 0.7, 0.06, 0.8), (0, -d / 2 - 0.05, 3.4), mat=PAINT, tint=P.CREAM, solid=False)
        if sign:
            n = len(sign)
            hh = 0.55
            tw = n * 0.86 * hh
            P.stencil(b, sign, (-tw / 2, -d / 2 - 0.09, 3.12), (0, 0, 0), hh, tint=accent, depth=0.02)
        soot(b, 0.0, -d / 2, 2.4, w * 0.5, 1.2)
        b.marker("door", (0, -d / 2 - 0.8, 0), kind_of="shop")
        b.marker("loot", (b.rng.uniform(-w / 3, w / 3), 1.0, 0.1), table="crate")
        for k in range(2):
            b.box((w * 0.6, 0.6, 1.2), (0, -1.0 + k * 3.0, 0.6), mat=PAINT, tint=srgb(0.5, 0.48, 0.44), solid=False)


def dome(b, at, r, glass=True, tint=OFFWHITE, missing=0.35, door_yaw=-90.0):
    """A geodesic-looking dome of panels (glass ones go missing) on a ring wall, with a door."""
    x, y, z = at
    rings = []
    for k in range(7):
        t = (math.pi / 2) * k / 6.0
        rr = r * math.cos(t) if k < 6 else r * 0.08
        zz = r * math.sin(t) if k < 6 else r * 0.997
        rings.append([(x + rr * math.cos(2 * math.pi * j / 20), y + rr * math.sin(2 * math.pi * j / 20), z + 1.2 + zz)
                      for j in range(20)])

    def keep(i, j):
        if not glass:
            return b.rng.random() > 0.05
        return b.rng.random() > missing or i == 0

    P.panel_loft(b, rings, thick=0.06, mat=GLASS if glass else PANEL, tint_fn=lambda i, j: tint, keep_fn=keep)
    for j in range(10):
        pts = [(x + r * 1.005 * math.cos(math.pi / 2 * k / 6) * math.cos(2 * math.pi * j / 10),
                y + r * 1.005 * math.cos(math.pi / 2 * k / 6) * math.sin(2 * math.pi * j / 10),
                z + 1.2 + r * 1.005 * math.sin(math.pi / 2 * k / 6)) for k in range(7)]
        b.tube(pts, 0.12, mat=PAINT, tint=LIGHTGREY, sides=6)
    for k in range(1, 6):
        t = (math.pi / 2) * k / 6.0
        rr = r * 1.005 * math.cos(t)
        b.tube([(x + rr * math.cos(2 * math.pi * j / 24), y + rr * math.sin(2 * math.pi * j / 24),
                 z + 1.2 + r * 1.005 * math.sin(t)) for j in range(25)], 0.1, mat=PAINT, tint=LIGHTGREY, sides=6,
               cap=False)
    a = math.radians(door_yaw)
    P.arc_wall(b, (x, y), r + 0.2, door_yaw + 6.0, door_yaw + 354.0, z, z + 1.2, thick=0.4, piece_deg=20.0,
               mat=CONCRETE, tint=CONCRETE_T)
    b.marker("door", (x + math.cos(a) * (r + 1.0), y + math.sin(a) * (r + 1.0), z), kind_of="dome")
    b.cyl(r - 0.2, 0.15, (x, y, z + 0.08), mat=CONCRETE, tint=srgb(0.5, 0.48, 0.42), sides=24)


def streets(b):
    """The street grid, sidewalks, burnt cars, barricades, lamps (some fallen), power poles."""
    for y in AVENUES:
        road(b, [(-262.0, y, 0), (262.0, y, 0)], width=10.0)
    for x in STREETS:
        road(b, [(x, -192.0, 0), (x, 192.0, 0)], width=9.0)
    road(b, [(0.0, -192.0, 0), (0.0, -250.0, 0)], width=10.0)
    for y in AVENUES:
        for s in (-1, 1):
            b.box((524.0, 2.2, 0.18), (0.0, y + s * 6.1, 0.09), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62), solid=False)
    # Burnt cars and trucks, scattered as they were left.
    cars = [(-180.0, -158.0, 12), (-90.0, -83.0, 170), (-60.0, -76.0, 95), (20.0, 2.0, 30), (100.0, 4.0, 190),
            (150.0, 78.0, 80), (-200.0, 82.0, 5), (-130.0, 30.0, 92), (-48.0, 120.0, 268), (48.0, -120.0, 85),
            (138.0, -40.0, 275), (228.0, 60.0, 92), (180.0, -163.0, 10), (-10.0, 158.0, 170), (60.0, 162.0, 5),
            (-225.0, -40.0, 88), (-140.0, -150.0, 270), (110.0, -78.0, 3)]
    for x, y, yaw in cars:
        b.piece("wreck-car", (x, y, 0.1), (0, 0, yaw + b.rng.uniform(-8, 8)))
    for x, y, yaw in ((-40.0, -2.0, 15), (130.0, 158.0, 85), (230.0, -100.0, 92)):
        b.piece("wreck-truck", (x, y, 0.1), (0, 0, yaw), tint=srgb(0.3, 0.28, 0.26))
    # Reclaimer barricades: scrap walls and barriers across some streets.
    for (x, y, yaw) in ((-135.0, -110.0, 0), (45.0, 110.0, 0), (180.0, 0.0, 90), (-180.0, 0.0, 90),
                        (-28.0, -80.0, 90)):
        with b.frame((x, y, 0), (0, 0, yaw)):
            for k in range(4):
                b.box((2.6, 0.12, 2.2 + 0.6 * b.rng.random()), (-4.5 + k * 3.0, b.rng.uniform(-0.4, 0.4), 1.2),
                      (b.rng.uniform(-5, 5), 0, b.rng.uniform(-10, 10)), mat=CORRUGATED,
                      tint=[RUSTY, P.TARP_BLUE, LIGHTGREY, srgb(0.5, 0.42, 0.3)][k], solid=True)
            b.piece("jersey-barrier", (3.5, 1.5, 0), (0, 0, 12))
            P.stencil(b, "X", (-4.0, -0.2, 1.0), (0, 0, 0), 1.2, tint=RED)
            b.marker("guard", (0.0, 3.0, 0.1), role="reclaimer")
    # Street lamps, a few leaning; power poles along the avenues.
    k = 0
    for y in AVENUES[1:4]:
        for x in (-180.0, -90.0, 0.0, 90.0, 180.0):
            lean = 0.0 if k % 4 else 14.0
            b.piece("lamp-post", (x + 10.0, y - 7.0, 0.1), (lean, 0, 0))
            k += 1
    for y in (-80.0, 80.0):
        prev = None
        for x in range(-240, 260, 40):
            b.cyl(0.14, 9.0, (x, y + 7.5, 4.5), mat=PAINT, tint=srgb(0.45, 0.38, 0.30), sides=8, solid=False)
            b.box((2.0, 0.12, 0.12), (x, y + 7.5, 8.6), mat=PAINT, tint=DARK, solid=False)
            tips = [(x - 0.9, y + 7.5, 8.75), (x + 0.9, y + 7.5, 8.75)]
            if prev:
                for pa, pb in zip(prev, tips):
                    cable(b, pa, pb, sag=0.04, radius=0.015)
            prev = tips


def blocks(b):
    """Fill the town blocks: flats, shops and hab clusters, each block a little different."""
    special = {(2, 1), (3, 2), (0, 3), (1, 3), (4, 0), (2, 3)}
    k = 0
    signs = ["FOOD", "HAB", "AIR", "TOOL", "CAFE", "PARTS", "SEED", "WATER", "LAUNDRY"]
    for ci in range(5):
        for ri in range(4):
            if (ci, ri) in special:
                continue
            x0, x1 = STREETS[ci] + 7.0, STREETS[ci + 1] - 7.0
            y0, y1 = AVENUES[ri] + 9.0, AVENUES[ri + 1] - 9.0
            cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
            tint = PALETTE[k % len(PALETTE)]
            accent = ACCENTS[k % len(ACCENTS)]
            kind = (ci * 3 + ri * 5) % 4
            # Front row on the south avenue: flats and shops; back row: flats or habs.
            floors = 2 + (k % 3)
            flats(b, (x0 + 14.0, y0 + 6.0, 0), 0.0, 22.0, floors, tint, accent, ruin=0.35)
            shop(b, (x1 - 14.0, y0 + 7.0, 0), 0.0, 18.0, PALETTE[(k + 3) % len(PALETTE)], ACCENTS[(k + 2) % 5],
                 sign=signs[k % len(signs)])
            if kind == 0:
                for j in range(4):
                    b.piece("hab-can", (x0 + 10.0 + j * 11.0, y1 - 8.0, 0), (0, 0, 90 if j % 2 else 0),
                            tint=PALETTE[(k + j) % len(PALETTE)])
                b.piece("hab-can", (x0 + 21.0, y1 - 8.0, 3.8), (0, 0, 0), tint=ACCENTS[k % 5])
                b.marker("loot", (x0 + 30.0, y1 - 14.0, 0.1), table="barrel")
            elif kind == 1:
                flats(b, (cx, y1 - 7.0, 0), 180.0, 30.0, 6 + (k % 2), PALETTE[(k + 1) % len(PALETTE)],
                      ACCENTS[(k + 3) % 5], ruin=0.5, d=12.0)
            elif kind == 2:
                flats(b, (x0 + 14.0, y1 - 6.0, 0), 180.0, 22.0, 2, PALETTE[(k + 2) % len(PALETTE)], accent, ruin=0.6)
                tank(b, (x1 - 12.0, y1 - 10.0, 0), 3.0, 5.0, tint=PALETTE[(k + 4) % len(PALETTE)], roof="dome",
                     ladder_side=180.0)
                P.scrap(b, (cx, cy), 6.0, count=6)
            else:
                rubble(b, (cx, y1 - 10.0, 0.0), 10.0, count=18, tint=tint, mat=PANEL, max_size=2.4)
                P.scorch(b, (cx, y1 - 10.0, 0.0), 9.0, blobs=4)
                for j in range(2):
                    b.piece("hab-can", (x0 + 12.0 + j * 30.0, y1 - 6.0, 0), (0, 0, 15 - 30 * j), tint=tint)
            # Side rows along the cross streets: flats facing west and east, shorter where the block is busy.
            flats(b, (x0 + 6.0, cy + 1.0, 0), -90.0, 24.0, 2 + (k + 1) % 2, PALETTE[(k + 5) % len(PALETTE)],
                  ACCENTS[(k + 1) % 5], ruin=0.4)
            if kind != 1:
                flats(b, (x1 - 6.0, cy - 1.0, 0), 90.0, 24.0, 2 + k % 3, PALETTE[(k + 4) % len(PALETTE)],
                      ACCENTS[(k + 4) % 5], ruin=0.45)
            else:
                shop(b, (x1 - 7.0, cy, 0), 90.0, 16.0, PALETTE[(k + 4) % len(PALETTE)], ACCENTS[(k + 4) % 5],
                     sign=signs[(k + 4) % len(signs)])
            # Back-yard dressing: overgrowth, a fence.
            for j in range(4):
                gx = b.rng.uniform(x0 + 16.0, x1 - 16.0)
                gy = b.rng.uniform(y0 + 16.0, y1 - 16.0)
                s_ = 1.5 + 2.5 * b.rng.random()
                P.rock(b, (gx, gy, 0.0), (s_ * 1.4, s_, s_ * 0.8), seed=k * 10 + j, rough=0.45, rings=4, sides=7,
                       mat=MATTE, tint=[srgb(0.24, 0.30, 0.15), srgb(0.30, 0.34, 0.18), srgb(0.36, 0.34, 0.20)][j % 3],
                       solid=False, squash=0.2)
            fence(b, [(x0 + 12.0, cy + 6.0, 0), (x1 - 12.0, cy + 6.0, 0)], height=1.6, post=4.0, wire=False,
                  tint=accent)
            k += 1


def plaza(b):
    """The plaza: paving, the landing memorial (a colony lander on a plinth), a fountain, dead trees, benches,
    a market of Reclaimer stalls, the communal hall north of it."""
    px, py = 0.0, -40.0
    b.box((76.0, 64.0, 0.2), (px, py, 0.1), mat=CONCRETE, tint=srgb(0.70, 0.68, 0.64), wear=0.5)
    for k in range(-3, 4):
        b.box((76.0, 0.25, 0.02), (px, py + k * 9.0, 0.21), mat=PAINT, tint=srgb(0.55, 0.53, 0.5), solid=False,
              vary=0)
    # The memorial: a stepped plinth, a lander with its legs out, a plaque.
    for k, s in enumerate((12.0, 9.0, 6.5)):
        b.box((s, s, 0.6), (px, py, 0.5 + 0.6 * k), mat=CONCRETE, tint=CONCRETE_T, bevel=0.03)
    b.lathe([(2.2, 0.0), (2.4, 1.6), (1.6, 3.6), (0.8, 4.6), (0.0, 5.0)], (px, py, 2.0 + 1.2), mat=PANEL,
            tint=OFFWHITE, sides=20)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.beam((px + math.cos(a) * 1.8, py + math.sin(a) * 1.8, 3.6), (px + math.cos(a) * 3.0, py + math.sin(a) * 3.0,
                                                                         2.1), 0.15, mat=PAINT, tint=GREY)
    b.cyl(2.45, 0.4, (px, py, 4.4), mat=PAINT, tint=ORANGE, sides=20, cap=False, solid=False, vary=0)
    P.plate(b, (px, py - 6.05, 1.0), (0, 0, 0), 2.4, 0.7, tint=srgb(0.55, 0.45, 0.25), mark=P.CREAM, text="SEED-1")
    b.marker("landmark", (px, py, 8.0), kind_of="memorial")
    # Fountain, benches, dead trees in planters.
    b.cyl(5.0, 0.6, (px, py + 20.0, 0.5), mat=CONCRETE, tint=CONCRETE_T, sides=24, solid=False)
    P.arc_wall(b, (px, py + 20.0), 5.0, 0.0, 360.0, 0.2, 0.9, thick=0.4, piece_deg=30.0, mat=CONCRETE,
               tint=CONCRETE_T)
    b.cyl(0.5, 2.0, (px, py + 20.0, 1.2), mat=CONCRETE, tint=CONCRETE_T, sides=10)
    for k in range(8):
        a = 2 * math.pi * k / 8
        x, y = px + math.cos(a) * 24.0, py + math.sin(a) * 22.0
        b.box((2.4, 2.4, 0.6), (x, y, 0.5), mat=CONCRETE, tint=CONCRETE_T)
        trunk = [(x, y, 0.8), (x + 0.2, y, 3.0), (x - 0.1, y + 0.2, 5.0)]
        b.tube(trunk, 0.18, mat=PAINT, tint=srgb(0.30, 0.25, 0.20), sides=6, radii=[0.22, 0.16, 0.08])
        for j in range(3):
            aa = a + j * 2.1
            b.tube([(x, y, 3.0 + j * 0.6), (x + math.cos(aa) * 1.6, y + math.sin(aa) * 1.6, 4.4 + j * 0.5)], 0.06,
                   mat=PAINT, tint=srgb(0.30, 0.25, 0.20), sides=5)
        if k % 2:
            b.box((2.2, 0.6, 0.45), (x + math.cos(a) * 3.0, y + math.sin(a) * 3.0, 0.45),
                  (0, 0, math.degrees(a) + 90), mat=PAINT, tint=TEAL, solid=False)
    # Market stalls under tarps, a fire barrel, strung lights.
    for k in range(6):
        x = px - 28.0 + k * 11.0
        y = py - 24.0
        tint = [P.TARP_BLUE, P.TARP_ORANGE, P.TARP_GREEN, RUSTY, P.TARP_BLUE, LIGHTGREY][k]
        for dx, dy in ((-2.0, -1.5), (2.0, -1.5), (-2.0, 1.5), (2.0, 1.5)):
            b.cyl(0.06, 2.6, (x + dx, y + dy, 1.4), mat=PAINT, tint=DARK, sides=6, solid=False)
        b.box((4.8, 3.8, 0.04), (x, y, 2.7), (6, 0, 0), mat=MATTE, tint=tint, solid=False)
        b.box((3.6, 1.2, 0.9), (x, y - 1.0, 0.65), mat=PAINT, tint=srgb(0.5, 0.42, 0.32), solid=False)
        b.marker("loot", (x, y + 0.5, 0.2), table="crate" if k % 2 else "barrel")
    for k in range(5):
        x0 = px - 28.0 + k * 11.0
        cable(b, (x0, py - 22.5, 3.0), (x0 + 11.0, py - 22.5, 3.0), sag=0.08, radius=0.015)
        b.box((0.18, 0.18, 0.25), (x0 + 5.5, py - 22.5, 2.5), mat=LAMP, tint=srgb(1.0, 0.8, 0.5), solid=False, vary=0)
        b.marker("light", (x0 + 5.5, py - 22.5, 2.4), colour=[1.0, 0.8, 0.5], range=8.0)
    b.marker("guard", (px + 10.0, py - 10.0, 0.2), role="reclaimer")
    # The communal hall north of the plaza across the avenue.
    P.hall(b, (0.0, 120.0, 0), (0, 0, 0), 46.0, 26.0, 8.0, bay=7.6, plinth=1.2, roof="gable", pitch=20.0,
           clad=PANEL, clad_tint=P.CREAM, roof_tint=srgb(0.62, 0.36, 0.28), frame_tint=srgb(0.40, 0.36, 0.30),
           band=TEAL, ruin=0.18, windows=("front", "back"), win_h=1.6,
           doors={"front": [(0.0, 3.0, 3.0, "door"), (-14.0, 1.2, 2.3, "door")]})
    P.stencil(b, "HALL", (-6.8, 106.55, 6.2), (0, 0, 0), 1.4, tint=TEAL)
    for p in ((-12.0, 120.0), (10.0, 126.0), (16.0, 114.0)):
        b.marker("loot", (p[0], p[1], 0.4), table="crate")


def clinic(b):
    """The clinic: two storeys of prefab wards round a stair, a canopy, the medical sign, an ambulance."""
    cx, cy = 90.0, 34.0
    for k in range(2):
        P.room(b, (cx, cy, k * 3.6), (0, 0, 0), 36.0, 18.0, 3.6, thick=0.3, mat=PANEL, tint=OFFWHITE,
               openings={"front": [(0.0, 3.0, 2.6, 0.0)] + [(x, 1.6, 1.4, 1.0) for x in (-14, -10, -6, 6, 10, 14)]
                         if k == 0 else [(x, 1.6, 1.4, 1.0) for x in (-14, -10, -6, -2, 2, 6, 10, 14)],
                         "back": [(x, 1.6, 1.4, 1.0) for x in (-12, -4, 4, 12)],
                         "right": [(0.0, 1.2, 2.3, 0.0)] if k == 1 else []},
               roof=(k == 1), roof_mat=CONCRETE, roof_tint=srgb(0.6, 0.6, 0.58))
        b.box((36.4, 0.2, 0.5), (cx, cy - 9.1, k * 3.6 + 3.3), mat=PAINT, tint=TEAL, solid=False, vary=0)
    stairs(b, (cx + 19.4, cy - 6.4, 0.0), (0, 0, 0), 1.2, 3.6, 5.2, tint=TEAL)
    P.deck(b, cx + 18.0, cy - 1.2, cx + 20.2, cy + 1.2, 3.6, mat=GRATE, rails="ne", edge=False)
    b.box((12.0, 5.0, 0.25), (cx, cy - 11.5, 3.2), mat=PAINT, tint=OFFWHITE)
    for x in (-5.5, 5.5):
        b.box((0.25, 0.25, 3.1), (cx + x, cy - 13.6, 1.6), mat=PAINT, tint=GREY, solid=False)
    b.box((3.6, 0.1, 3.6), (cx, cy - 9.25, 5.6), mat=PAINT, tint=OFFWHITE, solid=False)
    b.box((2.6, 0.12, 0.8), (cx, cy - 9.32, 5.6), mat=PAINT, tint=TEAL, solid=False, vary=0)
    b.box((0.8, 0.12, 2.6), (cx, cy - 9.32, 5.6), mat=PAINT, tint=TEAL, solid=False, vary=0)
    b.piece("wreck-truck", (cx - 20.0, cy - 16.0, 0.1), (0, 0, 8), tint=OFFWHITE)
    b.marker("door", (cx, cy - 10.0, 0), kind_of="building")
    for p in ((cx - 12.0, cy + 3.0, 0.1), (cx + 8.0, cy + 5.0, 0.1), (cx - 6.0, cy - 2.0, 3.7), (cx + 4.0, cy + 4.0, 3.7)):
        b.marker("loot", p, table="crate")
    for k in range(5):
        b.box((2.0, 0.9, 0.6), (cx - 14.0 + k * 3.6, cy + 6.5, 0.6), mat=PAINT, tint=LIGHTGREY, solid=False)
    soot(b, cx - 10.0, cy - 9.0, 5.8, 3.0, 1.2)


def landmarks(b):
    """The water tower and its pump house with hab cans, the two domes, the comms mast, the wind turbines and the
    solar field."""
    wx, wy = -180.0, 120.0
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.beam((wx + math.cos(a) * 7.0, wy + math.sin(a) * 7.0, 0.0), (wx + math.cos(a) * 4.5, wy + math.sin(a) * 4.5,
                                                                        30.0), 0.6, mat=PAINT, tint=GREY, solid=True)
    for z in (10.0, 20.0):
        k2 = 1 - z / 30.0 * 0.36
        pts = [(wx + math.cos(math.radians(45 + 90 * k)) * 7.0 * k2, wy + math.sin(math.radians(45 + 90 * k)) * 7.0 * k2,
                z) for k in range(5)]
        for pa, pb in zip(pts, pts[1:]):
            b.beam(pa, pb, 0.3, mat=PAINT, tint=GREY)
    b.cyl(8.0, 9.0, (wx, wy, 34.5), mat=PAINT, tint=P.CREAM, sides=28)
    b.cyl(8.3, 0.8, (wx, wy, 39.4), mat=PAINT, tint=TEAL, sides=28, radius2=7.0, solid="hull")
    b.cyl(8.05, 1.4, (wx, wy, 33.0), mat=PAINT, tint=ORANGE, sides=28, cap=False, solid=False, vary=0)
    P.stencil(b, "SEED", (wx, wy - 8.07, 35.2), (0, 0, 0), 2.6, tint=srgb(0.36, 0.30, 0.25), wrap=8.07)
    ladder(b, (wx, wy - 6.2, 0.0), 30.0, (0, 0, 0), cage=True, tint=GREY)
    P.beacon(b, (wx, wy, 40.0), 0.4)
    b.marker("landmark", (wx, wy, 40.5), kind_of="water-tower")
    b.marker("loot", (wx + 2.0, wy - 2.0, 30.2), table="crate")
    b.box((14.0, 14.0, 0.25), (wx, wy, 30.1), mat=GRATE, tint=GREY)
    P.room(b, (wx + 22.0, wy - 14.0, 0), (0, 0, 0), 10.0, 7.0, 3.6, mat=FORMWORK, tint=CONCRETE_T,
           openings={"front": [(2.0, 1.2, 2.3, 0.0)]})
    for j in range(3):
        b.piece("hab-can", (wx - 18.0, wy - 26.0 + j * 9.0, 0), (0, 0, 0), tint=PALETTE[j + 2])
    # Domes: the big habitat dome with its glass half gone, a smaller solid dome.
    dome(b, (-95.0, 125.0, 0.0), 26.0, glass=True, tint=OFFWHITE, missing=0.45, door_yaw=-90.0)
    dome(b, (-60.0, 98.0, 0.0), 9.0, glass=False, tint=P.CREAM, door_yaw=-90.0)
    for k in range(5):
        x = -105.0 + k * 6.0
        b.box((4.6, 1.6, 0.8), (x, 125.0, 0.6), mat=CONCRETE, tint=srgb(0.45, 0.40, 0.32), solid=False)
        b.box((4.4, 1.4, 0.4), (x, 125.0, 1.1), mat=MATTE, tint=srgb(0.30, 0.34, 0.20), solid=False)
    b.marker("loot", (-95.0, 118.0, 0.2), table="crate")
    b.marker("loot", (-60.0, 98.0, 0.2), table="crate")
    b.marker("landmark", (-95.0, 125.0, 27.0), kind_of="dome")
    # Comms mast north-east, wind turbines on the north edge, the solar field south-west.
    from monkit.kit import lattice_tower
    lattice_tower(b, (240.0, 200.0, 0.0), 48.0, 4.5, 1.2, sections=9, member=0.14, tint=srgb(0.70, 0.30, 0.20))
    P.beacon(b, (240.0, 200.0, 48.4), 0.5)
    b.marker("landmark", (240.0, 200.0, 48.5), kind_of="comms-mast")
    for k, (x, broken) in enumerate(((-150.0, False), (-60.0, True), (40.0, False))):
        wind_turbine(b, (x, 215.0, 0.0), height=58.0, blade=22.0, yaw=180.0 + 10.0 * k, broken=broken,
                     tint=OFFWHITE)
    solar_array(b, (-235.0, -215.0, 0.0), (0, 0, 0), rows=4, cols=10, tilt=32.0, broken=0.3)
    solar_array(b, (-150.0, -215.0, 0.0), (0, 0, 0), rows=4, cols=8, tilt=32.0, broken=0.4)


def works(b):
    """The fuel cell works in the south-east block: a cell hall burnt out, stacks of cell modules, tanks, toxic
    pools; the worst of the contamination."""
    wx, wy = 180.0, -120.0
    P.hall(b, (wx - 8.0, wy + 4.0, 0), (0, 0, 0), 44.0, 24.0, 10.0, bay=7.3, plinth=2.0, clad_tint=srgb(0.66, 0.68, 0.66),
           frame_tint=srgb(0.30, 0.30, 0.30), band=ORANGE, ruin=0.45, windows=("front",), skylights=False,
           doors={"front": [(-10.0, 6.0, 5.0, "open"), (10.0, 1.2, 2.3, "door")]})
    for k in range(6):
        x = wx - 24.0 + k * 6.0
        b.box((3.0, 2.4, 2.6), (x, wy + 4.0, 1.6), mat=PAINT, tint=srgb(0.30, 0.40, 0.38), bevel=0.05)
        b.box((0.4, 2.0, 0.4), (x, wy + 4.0, 3.1), mat=LAMP, tint=srgb(0.4, 1.0, 0.5), solid=False, vary=0)
    for k in range(3):
        tank(b, (wx + 26.0, wy - 10.0 + k * 9.0, 0), 3.2, 7.0, tint=srgb(0.86, 0.80, 0.30), roof="cone",
             ladder_side=180.0)
    for k, (x, y, r) in enumerate(((wx - 4.0, wy - 22.0, 6.0), (wx + 10.0, wy - 26.0, 4.0))):
        b.cyl(r, 0.1, (x, y, 0.08 + 0.01 * k), mat=GLOSS, tint=srgb(0.30, 0.45, 0.12), sides=16, solid=False)
    P.scorch(b, (wx - 8.0, wy + 4.0, 0.3), 12.0, blobs=5)
    for k in range(3):
        P.stencil(b, "TOX", (wx - 30.0 + k * 22.0, wy - 8.45, 4.0), (0, 0, 0), 1.6, tint=YELLOW)
    b.marker("loot", (wx - 16.0, wy + 8.0, 0.4), table="crate")
    b.marker("loot", (wx + 4.0, wy + 10.0, 0.4), table="military-crate")
    b.marker("guard", (wx, wy - 12.0, 0.1), role="reclaimer-heavy")
    fence(b, [(wx - 38.0, wy - 34.0, 0), (wx + 38.0, wy - 34.0, 0), (wx + 38.0, wy + 30.0, 0)], height=2.4, post=4.0)


def fringe(b):
    """Outside the grid: the south gate on the approach road, greenhouses on the north edge, a vehicle depot in the
    south-east corner."""
    for x in (-9.0, 9.0):
        b.box((1.6, 1.6, 7.0), (x, -205.0, 3.5), mat=PANEL, tint=P.CREAM, bevel=0.05)
        b.box((2.0, 2.0, 0.4), (x, -205.0, 7.2), mat=PAINT, tint=ORANGE)
    b.box((20.0, 1.0, 1.6), (0.0, -205.0, 6.4), mat=PANEL, tint=P.CREAM)
    P.stencil(b, "SEED 1", (-6.2, -205.52, 5.85), (0, 0, 0), 1.1, tint=TEAL)
    b.box((7.0, 0.8, 1.6), (6.0, -205.6, 3.0), (0, 0, -32), mat=PANEL, tint=P.CREAM, solid=False)
    for k, x in enumerate((60.0, 120.0, 180.0)):
        from monkit.structures import shed
        shed(b, (x, 195.0, 0), (0, 0, 0), 40.0, 14.0, 3.2, roof="gable", pitch=28.0, tint=srgb(0.85, 0.9, 0.9),
             frame_tint=LIGHTGREY, cladding=GLASS, skylights=False, ruin=0.45, doors=[("front", -14.0, 2.0, 2.3)])
        for j in range(4):
            b.box((32.0, 1.2, 0.5), (x, 191.0 + j * 2.6, 0.5), mat=CONCRETE, tint=srgb(0.45, 0.40, 0.32), solid=False)
            P.rock(b, (x - 10.0 + 7.0 * j, 191.0 + j * 2.6, 0.6), (9.0, 1.4, 1.4), seed=500 + k * 10 + j, rough=0.5,
                   rings=4, sides=7, mat=MATTE, tint=srgb(0.26, 0.32, 0.16), solid=False)
        b.marker("loot", (x + 8.0, 193.0, 0.3), table="crate")
    from monkit.structures import shed
    shed(b, (230.0, -205.0, 0), (0, 0, 0), 36.0, 18.0, 6.0, tint=srgb(0.62, 0.64, 0.60), frame_tint=DARK,
         doors=[("front", -9.0, 7.0, 5.0), ("front", 9.0, 7.0, 5.0)], ruin=0.3)
    for k, (x, y, yaw) in enumerate(((216.0, -226.0, 80), (238.0, -228.0, 95), (252.0, -186.0, 10))):
        b.piece("wreck-truck", (x, y, 0.0), (0, 0, yaw), tint=[P.CREAM, ORANGE, srgb(0.40, 0.45, 0.35)][k])
    b.piece("drum-stack", (205.0, -196.0, 0.25), (0, 0, 15))
    b.marker("loot", (203.0, -199.0, 0.1), table="fuel-barrel")
    b.marker("loot", (232.0, -205.0, 0.35), table="crate")


def build(b):
    b.set_pad(("rect", 600.0, 500.0), ground="dirt", blend=40.0, clear=10.0)
    streets(b)
    blocks(b)
    plaza(b)
    clinic(b)
    landmarks(b)
    works(b)
    fringe(b)
    b.zone("radiation", (-170.0, 0.0, 30.0), size=(260.0, 500.0, 80.0), level=1, source="ash")
    b.zone("radiation", (40.0, 0.0, 30.0), size=(180.0, 500.0, 80.0), level=2, source="ash")
    b.zone("radiation", (190.0, 50.0, 30.0), size=(130.0, 400.0, 80.0), level=2, source="ash")
    b.zone("radiation", (180.0, -125.0, 30.0), size=(110.0, 90.0, 80.0), level=3, source="fuel-cells")
    b.zone("no-build", (0, 0, 0), size=(680.0, 580.0, 200.0))
    b.marker("spawn", (0.0, -262.0, 0), kind_of="approach")
    b.meta.update({"tier": "large", "landmark": False, "spawn": "valley", "visible_km": 12})


RUSTY = srgb(0.48, 0.30, 0.20)
SUN = (30.0, 210.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((-68.0, -84.0, 0.1), 80.0), ((-20.0, -70.0, 0.2), 200.0)]
VIEWS = [
    {"name": "far", "target": (0.0, 40.0, 25.0), "distance": 2600, "yaw": 15, "pitch": -0.35, "lens": 100},
    {"name": "overview", "target": (0.0, 0.0, 5.0), "distance": 700, "yaw": 25, "pitch": 32, "lens": 30},
    {"name": "street", "target": (-10.0, -60.0, 6.3), "distance": 75, "yaw": -70, "pitch": -3.5, "lens": 24},
]
