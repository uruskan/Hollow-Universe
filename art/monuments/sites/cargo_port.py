"""Cargo Port (MONUMENTS.md §5.3, medium tier): a Seedward freight quay on a coast. The yard stands on a reclaimed
platform 2.5 m above the sea (the monument's ground is sea level): a quay with bollards and fenders, two ship-to-shore
cranes on their rails (one with its boom raised: the port's silhouette), a container ship moored alongside with its
deckhouse (the cyan room is the master's office on the bridge deck), container stacks under a yard gantry, a
warehouse, the harbour office, light masts, a recycler, and a floating landing stage with a gangway: the way in by
boat. Water in the renders is look-dev only; the game's sea is its own.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, fence, ladder, lamp, railing, rubble, srgb, stairs, walkway)
from monkit.structures import gantry_crane, road, shed, ship_hull
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "cargo-port"
TITLE = "Cargo Port"
SEED = 3131
BUILDER = {"chunk": 64.0, "max_edge": 6.0}

YARD = 2.5            # the platform's deck above the sea
QY = -60.0            # the quay edge
SHIP_Y = QY - 13.5    # the ship's centreline
CRANES = (-36.0, 46.0)
CTINTS = (srgb(0.55, 0.22, 0.16), srgb(0.20, 0.36, 0.52), srgb(0.24, 0.45, 0.42), srgb(0.84, 0.83, 0.78),
          srgb(0.78, 0.45, 0.16), srgb(0.36, 0.42, 0.28), srgb(0.62, 0.62, 0.6), srgb(0.7, 0.62, 0.2))


def platform(b):
    b.box((350.0, 145.0, YARD), (0.0, QY + 72.5, YARD / 2), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62))
    b.box((350.0, 1.2, YARD + 0.3), (0.0, QY - 0.4, (YARD + 0.3) / 2 - 0.15), mat=FORMWORK, tint=CONCRETE_T)
    b.box((350.0, 0.5, 0.3), (0.0, QY - 0.75, YARD + 0.05), mat=CONCRETE, tint=srgb(0.8, 0.78, 0.74), solid=False)
    # Rock armour round the landward edges, an access ramp up from the road.
    for (a, c) in (((-175.0, QY + 145.0), (-8.0, QY + 145.0)), ((8.0, QY + 145.0), (175.0, QY + 145.0)),
                   ((175.0, QY), (175.0, QY + 145.0)), ((-175.0, QY + 145.0), (-175.0, QY))):
        dx, dy = c[0] - a[0], c[1] - a[1]
        ln = math.hypot(dx, dy)
        b.extrude([(-3.5, 0.0), (0.0, 0.0), (0.0, YARD), (-0.6, YARD)], ln, ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2, 0),
                  (0, 0, math.degrees(math.atan2(dy, dx)) - 90), mat=CONCRETE, tint=srgb(0.5, 0.48, 0.44), wear=0.8)
    with b.frame((0.0, QY + 145.0 + 12.0, 0.0)):
        ang = math.degrees(math.atan2(YARD, 12.0))
        b.box((16.0, math.hypot(12.0, YARD), 0.3), (0, -6.0, YARD / 2), (ang, 0, 0), mat="M_Mon_Asphalt", tint=(1, 1, 1))
    road(b, [(0.0, 100.0, 0.0), (0.0, QY + 157.0, 0.0)], width=12.0)
    pm.preview_water([(-900.0, QY - 0.9), (900.0, QY - 0.9), (900.0, -1200.0), (-900.0, -1200.0)], 0.03,
                     name="sea")


def quay(b):
    rng = b.rng
    with b.frame((0, 0, YARD)):
        for y in (QY + 4.0, QY + 30.0):
            for dy in (-0.75, 0.75):
                b.box((340.0, 0.12, 0.16), (0.0, y + dy, 0.08), mat=RUST, tint=(1, 1, 1), solid=False)
        for k in range(23):
            x = -165.0 + k * 15.0
            b.cyl(0.32, 0.8, (x, QY + 1.2, 0.4), tint=DARK, sides=10)
            b.cyl(0.45, 0.12, (x, QY + 1.2, 0.86), tint=DARK, sides=10, solid=False)
        for k in range(34):
            x = -165.0 + k * 10.0
            b.box((1.6, 0.6, 2.0), (x, QY - 1.3, -1.0), mat=MATTE, tint=srgb(0.08, 0.08, 0.08))
        for x in (-150.0, -90.0, 0.0, 90.0, 150.0):
            ladder(b, (x + 4.0, QY - 1.1, -YARD), YARD + 0.1, (0, 0, 180), cage=False)
            b.marker("ladder", (x + 4.0, QY - 1.5, -YARD), height=YARD, kind_of="quay")
        pm.hazard_band(b, (-170.0, QY + 0.25, 0), (170.0, QY + 0.25, 0), 0.0, 0.05, normal=(0, 0, 1), width=1.0)
        # Mooring lines from bollards to the ship.
        for x in (-70.0, -55.0, 50.0, 65.0):
            cable(b, (x, QY + 1.2, 0.8), (x + rng.uniform(-6, 6), SHIP_Y + 10.0, 5.6), sag=0.06, radius=0.06,
                  tint=srgb(0.55, 0.5, 0.4), segments=8)


def sts_crane(b, at, tint, raised=0.0):
    """A ship-to-shore crane on its rails: four legs, the portal, the boom out over the water (or raised), the apex
    frame and stays, the machinery house, a trolley with its spreader, a ladder up a leg."""
    x0 = at[0]
    with b.frame((x0, QY + 17.0, YARD)):
        gy = 13.0       # half the rail gauge
        H = 38.0
        for x in (-9.0, 9.0):
            for y in (-gy, gy):
                b.box((1.4, 1.4, H), (x, y, H / 2), tint=tint)
                b.box((2.2, 4.0, 1.2), (x, y, 0.6), tint=DARK)
                for yy in (-1.3, 1.3):
                    b.cyl(0.5, 0.5, (x, y + yy, 0.5), (0, 90, 0), tint=BLACK, sides=12, solid=False)
            b.box((1.6, 2 * gy + 1.4, 2.2), (x, 0, H), tint=tint)
            b.beam((x, -gy, 6.0), (x, gy, H - 2.0), 0.6, 0.6, tint=tint)
            b.box((1.2, 2 * gy, 1.2), (x, 0, 9.0), tint=tint)
        for y in (-gy, gy):
            b.box((18.0 + 1.4, 1.6, 2.2), (0, y, H), tint=tint)
        b.box((18.0, 1.0, 1.0), (0, 0, 12.0), tint=tint, solid=False)
        # The boom: two box girders from the backreach to the outreach.
        zb = H + 3.0
        back, out = 30.0, -52.0
        for x in (-2.0, 2.0):
            b.box((1.2, back + 18.0, 2.6), (x, (back - 18.0) / 2, zb), tint=tint)
        hinge = -18.0
        with b.frame((0, hinge, zb), (-raised, 0, 0)):
            for x in (-2.0, 2.0):
                b.box((1.2, abs(out - hinge), 2.6), (x, (out - hinge) / 2, 0), tint=tint)
            for k in range(6):
                yk = (out - hinge) * (k + 0.5) / 6
                b.box((4.0, 0.3, 0.3), (0, yk, -1.1), tint=tint, solid=False)
            tip = b.world((0, out - hinge, 0.8))
            trolley_y = (out - hinge) * 0.45
            b.box((5.0, 4.0, 1.6), (0, trolley_y, -2.2), tint=OFFWHITE)
            b.box((2.4, 2.4, 2.2), (0, trolley_y + 2.5, -4.0), tint=OFFWHITE, bevel=0.05)
            b.box((2.0, 0.05, 1.0), (0, trolley_y + 1.27, -3.8), mat=GLASS, solid=False, vary=0)
            if raised < 30.0:
                for xx in (-1.0, 1.0):
                    cable(b, (xx, trolley_y, -3.0), (xx, trolley_y, -(zb - 9.0)), sag=0.0, radius=0.035)
                b.box((6.4, 2.6, 0.7), (0, trolley_y, -(zb - 8.4)), tint=YELLOW)
        apex = (0.0, -4.0, H + 22.0)
        for x in (-9.0, 9.0):
            for y in (-gy + 3.0, gy - 3.0):
                b.beam((x, y, H + 1.0), apex, 0.9, 0.9, tint=tint)
        b.box((3.0, 2.0, 1.6), apex, tint=tint)
        for xx in (-1.5, 1.5):
            b.tube([(xx, apex[1], apex[2]), (tip.x - x0 + xx, tip.y - (QY + 17.0), tip.z - YARD)], 0.12, tint=DARK,
                   sides=6)
            if raised < 30.0:
                b.tube([(xx, apex[1], apex[2]), (xx, -36.0, zb + 1.2)], 0.1, tint=DARK, sides=6)
            b.tube([(xx, apex[1], apex[2]), (xx, back, zb + 1.2)], 0.12, tint=DARK, sides=6)
        b.box((9.0, 12.0, 5.0), (0, back - 6.0, zb + 3.8), tint=OFFWHITE, bevel=0.05)
        b.box((9.2, 0.06, 1.2), (0, back - 12.03, zb + 4.8), tint=tint, solid=False, vary=0)
        pm.deck(b, -4.6, 6.0, 4.6, back, zb + 1.3, rails=("left", "right", "back"))
        ladder(b, (10.0, gy, 0), H + 2.0, (0, 0, 90), cage=True)
        pm.deck(b, 10.6, gy - 1.6, 13.4, gy + 0.8, H + 2.0, rails=("front", "right"))
        ladder(b, (12.6, gy + 1.0, H + 2.0), zb + 1.3 - H - 2.0, (0, 0, 90), cage=False)
        pm.deck(b, 4.6, gy + 1.2, 13.6, gy + 3.2, zb + 1.3, rails=("front", "right", "back"))
        pm.aviation_lamp(b, (apex[0], apex[1], apex[2] + 0.8), size=0.45)
        pm.aviation_lamp(b, (0.0, back, zb + 6.4), size=0.35)
        pm.stencil(b, (0.0, gy + 0.85, H), (0, 0, 180), "C1" if x0 < 0 else "C2", height=1.6, tint=OFFWHITE)
        b.marker("loot", (0.0, back - 3.0, zb + 1.3), table="crate")
        b.marker("guard", (0.0, 10.0, zb + 1.3), 180.0, role="reclaimer-sentry")
        b.marker("landmark", (apex[0], apex[1], apex[2] + 1.0), kind_of="quay-crane", visible_km=10)


def ship(b):
    rng = b.rng
    L, W, Hh = 150.0, 24.0, 16.0
    ship_hull(b, (0.0, SHIP_Y, 0.0), (0, 0, 90), length=L, width=W, height=Hh, stations=14, nose=0.18, tail=0.1,
              tint=srgb(0.28, 0.34, 0.40), band=RED, damage=0.04, n=20, buried=0.45)
    deck = Hh - 0.45 * Hh + 0.1
    with b.frame((0.0, SHIP_Y, deck)):
        b.box((L * 0.86, W * 0.94, 0.4), (-L * 0.04, 0, 0.0), mat=PAINT, tint=srgb(0.45, 0.24, 0.18))
        for y in (-W * 0.47, W * 0.47):
            for x0, x1 in ((-L * 0.47, L * 0.38),):
                railing(b, (x0, y, 0.2), (x1, y, 0.2), tint=srgb(0.85, 0.85, 0.8))
        # Containers in bays, two or three high, a few gone.
        k = 0
        for bay in range(11):
            x = -40.0 + bay * 7.4
            for row in range(7):
                y = -7.8 + row * 2.6
                hgt = 1 + (bay * 7 + row * 3) % 3
                if rng.random() < 0.1:
                    continue
                for lay in range(hgt):
                    b.piece("container-20", (x, y, 0.2 + lay * 2.59), (0, 0, rng.uniform(-0.8, 0.8)),
                            tint=CTINTS[k % len(CTINTS)])
                    k += 3
        b.marker("loot", (-44.0, 6.0, 0.2), table="crate")
        b.marker("loot", (32.0, -9.0, 0.2), table="crate")
        # The deckhouse at the stern: three decks and the bridge, an outside stair, the funnel.
        dx = -60.0
        info = pm.building(b, (dx, 0, 0.2), (0, 0, 0), 12.0, 18.0, floors=3, floor_h=3.0, mat=PANEL,
                           tint=OFFWHITE,
                           openings={"front": [(x, 1.2, 1.0, 1.1, k2) for k2 in (0, 1, 2) for x in (-3.0, 0.0, 3.0)],
                                     "back": [(x, 1.2, 1.0, 1.1, k2) for k2 in (0, 1) for x in (-3.0, 3.0)],
                                     "right": [(-6.0, 1.2, 2.2, 0, 0, {"kind_of": "deckhouse", "state": "ajar"}),
                                               (-6.0, 1.2, 2.2, 0, 2, {"kind_of": "bridge", "chip": "cyan",
                                                                       "cell": True, "room": "masters-office"})]
                                              + [(y, 1.6, 1.2, 1.0, 2) for y in (-1.5, 2.0, 5.5)]
                                              + [(y, 1.6, 1.0, 1.1, 1) for y in (0.0, 4.0)],
                                     "left": [(y, 1.6, 1.2, 1.0, 2) for y in (-6.0, -2.5, 1.0, 4.5)]},
                           plinth=0.0, parapet=1.0, band=(ORANGE, 8.4, 0.4), roof_units=1, glass=0.6, steps=False)
        f0, f1, f2 = info["floor"]
        # The outside stair up the starboard side to the bridge deck door.
        with b.frame((dx + 6.85, 0.0, 0.2)):
            stairs(b, (0, 6.0, 0), (0, 0, 180), 1.0, f1 - f0, 3.8)
            pm.deck(b, -0.7, 0.4, 0.7, 2.2, f1, rails=("right",))
            stairs(b, (0, 0.4, f1), (0, 0, 180), 1.0, f2 - f1, 3.8)
            pm.deck(b, -0.7, -7.0, 0.7, -3.4, f2, rails=("right", "front"))
        with b.frame((dx, 0, 0.2)):
            b.piece("console", (0.0, -7.6, f2), (0, 0, 0))
            b.piece("console", (-3.0, -7.6, f2), (0, 0, 0))
            b.piece("rack-row", (-4.6, 6.0, f2), (0, 0, 90))
            b.marker("loot", (2.0, 5.0, f2), table="sealed-crate")
            b.marker("loot", (-2.0, 2.0, f2), table="military-crate")
            b.marker("loot", (2.5, -3.0, f1), table="crate")
            b.marker("guard", (0.0, 0.0, f0), 180.0, role="reclaimer")
            b.box((4.0, 3.0, 0.2), (0.0, -10.5, f2 - 0.1), tint=OFFWHITE)
            b.box((4.0, 3.0, 0.2), (0.0, 10.5, f2 - 0.1), tint=OFFWHITE)
            b.box((3.4, 4.0, 9.0), (5.0, 0.0, info["roof"] + 3.0), tint=srgb(0.82, 0.82, 0.8))
            b.box((3.5, 4.1, 1.6), (5.0, 0.0, info["roof"] + 6.6), tint=RED, solid=False, vary=0)
            b.cyl(0.05, 6.0, (0.0, 0.0, info["roof"] + 3.0), tint=GREY, sides=6, solid=False)
            pm.aviation_lamp(b, (0.0, 0.0, info["roof"] + 6.0), size=0.3)
            pm.stencil(b, (6.0, -9.2, f0 + 1.6), (0, 0, 0), "S-7", height=1.2, tint=DARK)
    pm.stencil(b, (60.0, SHIP_Y - W / 2 - 0.2, 4.5), (0, 0, 0), "HV-1218", height=2.2, tint=OFFWHITE)
    # The gangway from the quay up to the deck.
    with b.frame((-46.0, QY + 2.0, YARD)):
        stairs(b, (0.0, 0.0, 0.0), (0, 0, 180), 1.2, deck + 0.2 - YARD, 9.6, tint=srgb(0.85, 0.85, 0.8))


def yard(b):
    rng = b.rng
    with b.frame((0, 0, YARD)):
        # Container blocks: rows of stacks one to four high, aisles between, some doors open (crates inside).
        k = 0
        blocks = ((-10.0, 0.0, 6, 5), (40.0, 0.0, 6, 5), (90.0, 2.0, 5, 6), (-60.0, 22.0, 4, 4), (100.0, 40.0, 4, 4))
        for (bx, by, nx, ny) in blocks:
            for i in range(nx):
                for j in range(ny):
                    hgt = 1 + (i * 5 + j * 3 + int(bx)) % 4
                    if rng.random() < 0.08:
                        continue
                    for lay in range(hgt):
                        b.piece("container-20", (bx + j * 2.6, by + i * 6.5, lay * 2.59), (0, 0, 90 + rng.uniform(-1, 1)),
                                tint=CTINTS[k % len(CTINTS)])
                        k += 5
            b.marker("loot", (bx - 2.5, by + 3.0, 0.0), table="crate", inside="container")
        gantry_crane(b, (52.0, 16.0, 0.0), (0, 0, 90), span=24.0, height=17.0, length=12.0, tint=srgb(0.9, 0.7, 0.15))
        b.markers = [m for m in b.markers if not (m["kind"] == "landmark" and m.get("kind_of") == "gantry-crane")]
        b.marker("loot", (35.0, 30.0, 0.0), table="military-crate", inside="container")
        # The warehouse, the harbour office, the recycler, light masts, a gate.
        shed(b, (-112.0, 40.0, 0), (0, 0, 0), 70.0, 30.0, 12.0, doors=[("front", -20.0, 8.0, 7.0),
                                                                     ("front", 12.0, 8.0, 7.0)],
             tint=srgb(0.58, 0.64, 0.66), ruin=0.1)
        for i in range(4):
            b.piece("container-20", (-130.0 + i * 8.0, 46.0, 0.25), (0, 0, 0), tint=CTINTS[i * 2 % 8])
        for (x, y) in ((-140.0, 32.0), (-100.0, 50.0), (-92.0, 30.0)):
            b.piece("crate-stack", (x, y, 0.25), (0, 0, x))
        b.marker("loot", (-138.0, 34.0, 0.25), table="crate")
        b.marker("loot", (-96.0, 34.0, 0.25), table="crate")
        b.marker("loot", (-118.0, 52.0, 0.25), table="barrel")
        b.marker("guard", (-112.0, 30.0, 0.25), 180.0, role="reclaimer")
        info = pm.building(b, (124.0, 64.0, 0), (0, 0, 0), 22.0, 12.0, floors=2, floor_h=3.4, mat=PANEL, tint=OFFWHITE,
                           openings={"front": [(-7.0, 1.4, 2.3, 0, 0, {"kind_of": "office", "state": "ajar"})]
                                              + [(x, 1.8, 1.3, 1.0, k2) for k2 in (0, 1) for x in (-2.0, 2.5, 7.0)],
                                     "back": [(x, 1.8, 1.3, 1.0, k2) for k2 in (0, 1) for x in (-6.0, 0.0, 6.0)]},
                           holes=[(8.6, -5.6, 9.8, -0.5, 1)], band=(TEAL, 3.4, 0.4), roof_units=2, glass=0.4)
        with b.frame((124.0, 64.0, 0)):
            stairs(b, (9.2, -5.6, info["floor"][0]), (0, 0, 0), 1.2, info["floor"][1] - info["floor"][0], 5.1)
            b.piece("console", (-6.0, 4.6, info["floor"][1]), (0, 0, 180))
            b.marker("loot", (-8.0, 4.0, info["floor"][0]), table="crate")
            b.marker("loot", (4.0, 4.0, info["floor"][1]), table="crate")
            pm.code_plate(b, (-3.5, -6.18, 2.6), (0, 0, 0), 2.6, 0.5, base=TEAL, ink=OFFWHITE, text="HM-31")
        pm.lean_to(b, (70.0, 66.0, 0), (0, 0, 0), 7.0, 5.0, h_front=3.2, h_back=3.6, missing=0.0)
        b.box((3.4, 2.4, 0.2), (70.0, 66.0, 0.1), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68))
        b.marker("recycler", (70.0, 66.0, 0.2), 180.0)
        pm.code_plate(b, (70.0, 68.4, 2.4), (0, 0, 0), 1.6, 0.5, base=YELLOW, ink=BLACK, text="RC-2")
        for (x, y) in ((-150.0, -20.0), (-20.0, 60.0), (150.0, 10.0), (80.0, -36.0)):
            b.cyl(0.5, 30.0, (x, y, 15.0), tint=srgb(0.7, 0.72, 0.72), sides=10, radius2=0.25)
            b.cyl(1.6, 0.3, (x, y, 30.0), tint=DARK, sides=12, solid=False)
            for k2 in range(6):
                a = math.radians(60 * k2)
                lamp(b, (x + math.cos(a) * 1.6, y + math.sin(a) * 1.6, 29.6), (-30, 0, 60 * k2 + 90), size=(0.7, 0.4, 0.35))
            pm.aviation_lamp(b, (x, y, 30.3), size=0.3)
        hy = QY + 144.0
        fence(b, [(-174.0, hy, 0), (-9.0, hy, 0)], post=5.0)
        fence(b, [(9.0, hy, 0), (174.0, hy, 0)], post=5.0)
        for x in (-9.0, 9.0):
            b.box((1.0, 1.0, 3.0), (x, hy, 1.5), mat=CONCRETE, tint=CONCRETE_T)
        b.marker("door", (0.0, hy, 0.0), 0.0, kind_of="gate", width=17.0, height=3.0)
        road(b, [(0.0, hy, 0.0), (0.0, QY + 34.0, 0.0), (-80.0, QY + 34.0, 0.0)], width=10.0)
        b.piece("utility-truck", (14.0, QY + 40.0, 0), (0, 0, 10), tint=srgb(0.95, 0.75, 0.2))
        b.piece("utility-truck", (-150.0, 10.0, 0), (0, 0, 80), tint=OFFWHITE)
        for (x, y) in ((-20.0, QY + 8.0), (120.0, QY + 10.0), (-160.0, QY + 40.0)):
            b.piece("drum-stack", (x, y, 0), (0, 0, x))
            b.marker("loot", (x + 1.5, y, 0), table="barrel")


def landing_stage(b):
    """A floating landing stage at the east end: pontoons, a gangway up to the quay, a boat tied alongside."""
    px = 140.0
    b.box((22.0, 6.0, 0.6), (px, QY - 9.0, 0.3), mat=PAINT, tint=srgb(0.5, 0.5, 0.48))
    for k in range(5):
        b.cyl(0.9, 5.6, (px - 9.0 + k * 4.5, QY - 9.0, -0.4), (90, 0, 0), tint=srgb(0.82, 0.5, 0.15), sides=12)
    for k in range(4):
        b.cyl(0.18, 4.0, (px - 9.0 + k * 6.0, QY - 6.3, 1.4), tint=GREY, sides=8)
    railing(b, (px - 11.0, QY - 12.0, 0.6), (px + 11.0, QY - 12.0, 0.6))
    stairs(b, (px - 6.0, QY - 6.1, 0.6), (0, 0, 0), 1.2, YARD - 0.6, 5.0)
    b.marker("landing", (px, QY - 9.0, 0.6), 0.0, kind_of="boat", size=20.0)
    with b.frame((px + 2.0, QY - 15.0, 0.0), (0, 0, 92.0)):
        ship_hull(b, (0, 0, 0), (0, 0, 0), length=8.0, width=2.6, height=1.6, stations=6, nose=0.35, tail=0.1,
                  tint=OFFWHITE, band=TEAL, n=10, buried=0.35)
    cable(b, (px + 6.0, QY - 12.0, 0.8), (px + 6.0, QY - 15.0, 0.7), sag=0.1, radius=0.02, tint=srgb(0.6, 0.55, 0.4))
    b.marker("loot", (px + 8.0, QY - 8.0, 0.6), table="barrel")


def build(b):
    b.set_pad(("rect", 350.0, 200.0), ground="concrete", blend=20.0, clear=8.0, sea="south", deck=YARD)
    platform(b)
    quay(b)
    for x, t, raised in ((CRANES[0], srgb(0.88, 0.42, 0.12), 0.0), (CRANES[1], srgb(0.16, 0.42, 0.45), 74.0)):
        sts_crane(b, (x, 0, 0), t, raised)
    ship(b)
    yard(b)
    landing_stage(b)
    b.zone("no-build", (0, 0, 0), size=(390.0, 240.0, 110.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-industrial", "spawn": "coasts",
                   "deck": YARD})


SUN = (30.0, 215.0)
PEOPLE = [((-46.0, QY + 6.0, YARD), 180.0), ((140.0, QY - 9.0, 0.6), 0.0)]
VIEWS = [
    {"name": "overview", "target": (0, 0, 10), "distance": 330, "yaw": 30, "pitch": 22, "lens": 32},
    {"name": "quay", "target": (-20, -66, 16), "distance": 60, "yaw": 115, "pitch": -10, "lens": 24},
    {"name": "stage", "target": (126, -60, 7), "distance": 50, "yaw": 40, "pitch": 5, "lens": 26},
]
