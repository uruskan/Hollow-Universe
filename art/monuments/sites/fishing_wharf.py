"""Fishing Wharf (MONUMENTS.md §5.2 no. 19, small tier): a colonist fishing harbour on a sheltered coast. A concrete quay
2.2 m above the water runs along the shore with its seawall, fenders and ladders; two timber piers on piles reach out,
one with a T-head and a pedestal crane; a boathouse stands over the slipway; three tall tarred net sheds and an open
fish-market shed line the back; a rubble breakwater with a light closes the harbour. Boats: one moored at the pier, one
half-sunk, one hauled up the slip. Crates of nets and pots on the quay.

The monument's z = 0 is the water level; the water surface drawn here is the harbour's (the sea takes its place in the
world when the site is set on a real coast).
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, RED_LAMP, TEAL, YELLOW,
                        ladder, lamp, railing, srgb)
from monkit.surfaces import CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, LAMP, MATTE, PAINT, RUST

KEY = "fishing-wharf"
TITLE = "Fishing Wharf"
SEED = 1919
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

QZ = 2.2            # the quay's top above the water
QY = 6.0            # the seawall's line; land behind it, water in front
DZ = 2.0            # the piers' deck height
TAR = srgb(0.12, 0.11, 0.1)
TIMBER = srgb(0.48, 0.4, 0.31)


def quay(b):
    """The land: a quay platform with its seawall face, capping, fenders, ladders, bollards and the slip's cut."""
    slip = (-41.0, -35.0)
    for (x0, x1) in ((-50.0, slip[0]), (slip[1], 50.0)):
        P.grid_box(b, (x1 - x0, 24.0, QZ), ((x0 + x1) / 2, QY + 12.0, QZ / 2), mat=FORMWORK, tint=CONCRETE_T,
                   step=3.0)
        b.box((x1 - x0, 0.5, 0.3), ((x0 + x1) / 2, QY + 0.1, QZ + 0.1), mat=CONCRETE, tint=srgb(0.8, 0.79, 0.76))
        b.box((x1 - x0, 0.03, 0.9), ((x0 + x1) / 2, QY - 0.02, 0.45), mat=MATTE, tint=srgb(0.16, 0.2, 0.14),
              solid=False, vary=0)
    P.grid_box(b, (slip[1] - slip[0], 24.0 - 10.0, QZ), ((slip[0] + slip[1]) / 2, QY + 17.0, QZ / 2), mat=FORMWORK,
               tint=CONCRETE_T, step=3.0)
    # The slipway: a concrete ramp from the quay down into the water inside the boathouse.
    ln = math.hypot(10.0 + 6.0, QZ + 1.2)
    ang = math.degrees(math.atan2(QZ + 1.2, 16.0))
    with b.frame(((slip[0] + slip[1]) / 2, QY + 2.0, (QZ - 1.2) / 2), (-ang, 0, 0)):
        P.grid_box(b, (slip[1] - slip[0], ln, 0.4), (0, 0, -0.2), mat=CONCRETE, tint=srgb(0.6, 0.6, 0.57), step=2.0)
        for x in (-1.0, 1.0):
            b.box((0.12, ln, 0.1), (x, 0, 0.05), mat=RUST, tint=(1, 1, 1), solid=False)
    # Fenders, ladders and bollards along the seawall.
    for x in range(-48, 50, 6):
        if slip[0] - 1 < x < slip[1] + 1:
            continue
        b.box((0.3, 0.35, 1.6), (x, QY - 0.18, 1.2), mat=MATTE, tint=P.RUBBER, solid=False)
        if x % 18 == 0:
            ladder(b, (x + 1.5, QY - 0.1, -1.0), QZ + 1.0, (0, 0, 0), cage=False, tint=srgb(0.6, 0.62, 0.62))
        P.bollard(b, (x + 3.0, QY + 0.6, QZ))
    # The quay's top: a track behind the sheds, kerbs.
    b.box((100.0, 0.25, 0.15), (0.0, QY + 0.45, QZ + 0.075), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71), solid=False)


def pier(b, x, y0, y1, width=4.0, head=None):
    """A timber pier on paired piles from the seawall at y0 out to y1: pile bents every 3 m with cross braces, caps,
    stringers, a planked deck, a kerb rail, bollards and lamps. `head` (x0, x1, depth) adds a T-head."""
    hw = width / 2
    n = int(abs(y1 - y0) / 3.0)
    for k in range(n + 1):
        y = y0 + (y1 - y0) * k / n
        for s in (-1, 1):
            b.cyl(0.18, DZ + 3.2, (x + s * (hw - 0.3), y, (DZ - 3.2) / 2 - 0.1), mat=PAINT, tint=TAR, sides=8)
        b.box((width + 0.2, 0.3, 0.3), (x, y, DZ - 0.3), mat=PAINT, tint=TIMBER)
        if k % 2 == 0 and k:
            P.rod(b, (x - hw + 0.3, y, DZ - 0.5), (x + hw - 0.3, y, 0.2), 0.06, tint=TAR, sides=5)
    for s in (-1, 0, 1):
        b.box((0.2, abs(y1 - y0), 0.25), (x + s * (hw - 0.4), (y0 + y1) / 2, DZ - 0.12), mat=PAINT, tint=TIMBER)
    deck(b, x, (y0 + y1) / 2, width, abs(y1 - y0))
    for s in (-1, 1):
        b.box((0.2, abs(y1 - y0), 0.15), (x + s * (hw - 0.1), (y0 + y1) / 2, DZ + 0.13), mat=PAINT, tint=TIMBER,
              solid=False)
    for k in range(1, n, 3):
        y = y0 + (y1 - y0) * k / n
        P.bollard(b, (x + hw - 0.5, y, DZ + 0.05))
    for k in (n // 3, 2 * n // 3):
        y = y0 + (y1 - y0) * k / n
        b.cyl(0.07, 5.0, (x - hw + 0.25, y, DZ + 2.5), mat=PAINT, tint=GREY, sides=8, solid=False)
        lamp(b, (x - hw + 0.6, y, DZ + 4.9), (-30, 0, -90), size=(0.4, 0.22, 0.14))
    b.collide_box(Vector((x, (y0 + y1) / 2, DZ - 0.1)), (width, abs(y1 - y0), 0.3), (0, 0, 0))
    if head:
        hx0, hx1, depth = head
        yh = y1 - depth / 2 if y1 < y0 else y1 + depth / 2
        for xx in [hx0 + (hx1 - hx0) * k / 6 for k in range(7)]:
            for yy in (yh - depth / 2 + 0.3, yh + depth / 2 - 0.3):
                b.cyl(0.2, DZ + 3.2, (xx, yy, (DZ - 3.2) / 2 - 0.1), mat=PAINT, tint=TAR, sides=8)
            b.box((0.3, depth + 0.2, 0.3), (xx, yh, DZ - 0.3), mat=PAINT, tint=TIMBER)
        deck(b, (hx0 + hx1) / 2, yh, depth, hx1 - hx0, across=True)
        b.collide_box(Vector(((hx0 + hx1) / 2, yh, DZ - 0.1)), (hx1 - hx0, depth, 0.3), (0, 0, 0))
        for xx in (hx0 + 0.8, (hx0 + hx1) / 2, hx1 - 0.8):
            P.bollard(b, (xx, yh - depth / 2 + 0.5, DZ + 0.05))


def deck(b, x, y, width, length, across=False):
    """A planked deck: a run of boards with gaps, a few missing over the water."""
    n = int(length / 0.32)
    for k in range(n):
        if b.rng.random() < 0.035:
            continue
        t = -length / 2 + length * (k + 0.5) / n
        if across:
            b.box((length / n - 0.03, width, 0.06), (x + t, y, DZ + 0.03), mat=PAINT, tint=TIMBER, solid=False,
                  vary=0.08)
        else:
            b.box((width, length / n - 0.03, 0.06), (x, y + t, DZ + 0.03), mat=PAINT, tint=TIMBER, solid=False,
                  vary=0.08)


def net_shed(b, at, w=3.0, d=3.2, h=6.5, yaw=0.0):
    """A tall narrow net shed of tarred boards with a steep gable roof, a door with its upper half open."""
    with b.frame(at, (0, 0, yaw)):
        b.box((w + 0.3, d + 0.3, 0.2), (0, 0, 0.1), mat=CONCRETE, tint=srgb(0.6, 0.6, 0.58))
        P.grid_box(b, (w, d, h), (0, 0, 0.2 + h / 2), mat=MATTE, tint=TAR, step=1.0)
        for k in range(int(w / 0.25)):
            b.box((0.05, 0.03, h - 0.2), (-w / 2 + 0.12 + 0.25 * k, -d / 2 - 0.015, 0.2 + h / 2), mat=MATTE,
                  tint=TAR, solid=False, vary=0.1)
        rise = w * 0.75
        for s in (-1, 1):
            ln = math.hypot(w / 2 + 0.2, rise)
            ang = math.degrees(math.atan2(rise, w / 2 + 0.2))
            b.box((ln, d + 0.4, 0.07), (s * (w / 2 + 0.2) / 2, 0, 0.2 + h + rise / 2), (0, s * ang, 0),
                  mat=PAINT, tint=srgb(0.2, 0.19, 0.18))
        for y in (-d / 2, d / 2):
            b.mesh([(-w / 2, y, 0.2 + h), (w / 2, y, 0.2 + h), (0, y, 0.2 + h + rise)], [[0, 1, 2]], mat=MATTE,
                   tint=TAR, solid="hull")
        b.box((1.0, 0.06, 1.1), (0.4, -d / 2 - 0.04, 1.45), mat=PAINT, tint=srgb(0.2, 0.19, 0.18), solid=False)
        with b.frame((0.9, -d / 2 - 0.04, 2.0), (0, 0, 70.0)):
            b.box((1.0, 0.05, 1.0), (-0.5, 0, 0.5), mat=PAINT, tint=srgb(0.2, 0.19, 0.18), solid=False)
        b.box((0.9, 0.04, 0.9), (0.4, -d / 2 - 0.02, 2.55), mat=MATTE, tint=srgb(0.03, 0.03, 0.03), solid=False)
        b.box((0.5, 0.04, 0.7), (0, -d / 2 - 0.03, h - 0.4), mat=MATTE, tint=srgb(0.03, 0.03, 0.03), solid=False)


def boathouse(b, cx, y0, y1, w=8.0, h=6.0):
    """The boathouse over the slipway: a timber frame on piles where it stands over the water, corrugated walls and a
    gable roof, its big doors toward the water folded back."""
    hw = w / 2
    L = y1 - y0
    for k in range(5):
        y = y0 + L * k / 4
        bottom = -1.0 if y < QY else QZ          # piles down into the water where it stands over the slip
        top = QZ + h
        for s in (-1, 1):
            b.box((0.3, 0.3, top - bottom), (cx + s * hw, y, (top + bottom) / 2), mat=PAINT, tint=TIMBER)
    for s in (-1, 1):
        for k in range(int(L / 1.0)):
            y = y0 + 0.5 + k
            if b.rng.random() < 0.08:
                continue
            P.grid_box(b, (0.06, 0.98, h - 0.4), (cx + s * (hw + 0.18), y, QZ + 0.2 + (h - 0.4) / 2), mat=CORRUGATED,
                       tint=srgb(0.45, 0.5, 0.48), step=1.5)
    rise = 2.4
    for s in (-1, 1):
        ln = math.hypot(hw + 0.4, rise)
        ang = math.degrees(math.atan2(rise, hw + 0.4))
        P.grid_box(b, (ln, L + 0.8, 0.07), (cx + s * (hw + 0.4) / 2, (y0 + y1) / 2, QZ + h + rise / 2),
                   (0, s * ang, 0), mat=CORRUGATED, tint=srgb(0.35, 0.38, 0.38), step=1.5)
    b.mesh([(cx - hw, y1, QZ + h), (cx + hw, y1, QZ + h), (cx, y1, QZ + h + rise)], [[0, 1, 2]], mat=CORRUGATED,
           tint=srgb(0.45, 0.5, 0.48), solid="hull")
    P.grid_box(b, (w, 0.1, h), (cx, y1, QZ + h / 2), mat=CORRUGATED, tint=srgb(0.45, 0.5, 0.48), step=1.5)
    # The doors to the water, folded back against the walls (each hinged at its corner post).
    for s in (-1, 1):
        with b.frame((cx + s * hw, y0, 0.3), (0, 0, s * 100.0)):
            P.grid_box(b, (hw - 0.1, 0.06, h + 1.2), (-s * (hw - 0.1) / 2, 0, (h + 1.2) / 2), mat=CORRUGATED,
                       tint=srgb(0.45, 0.5, 0.48), step=1.5)
    b.mesh([(cx - hw, y0, QZ + h), (cx + hw, y0, QZ + h), (cx, y0, QZ + h + rise)], [[0, 1, 2]], mat=CORRUGATED,
           tint=srgb(0.45, 0.5, 0.48), solid="hull")
    P.stencil(b, "BOATS", (cx, y0 - 0.05, QZ + h + 0.5), (0, 0, 0), 0.5, OFFWHITE, align="centre", jitter=0.5)
    b.marker("door", (cx, y1 + 1.0, QZ), kind_of="boathouse")


def market_shed(b, at):
    """The fish market: a long roof on steel columns over the quay, slab tables, a hanging scale, crates."""
    x, y = at
    for xx in (-7.5, -2.5, 2.5, 7.5):
        for yy in (-3.5, 3.5):
            b.box((0.25, 0.25, 4.0), (x + xx, y + yy, QZ + 2.0), mat=PAINT, tint=TEAL)
    for s in (-1, 1):
        b.box((16.6, 4.3, 0.08), (x, y + s * 2.0, QZ + 4.35), (s * 8.0, 0, 0), mat=CORRUGATED,
              tint=srgb(0.6, 0.62, 0.6))
    b.box((16.6, 0.3, 0.3), (x, y, QZ + 4.65), mat=PAINT, tint=TEAL, solid=False)
    for k in range(3):
        with b.frame((x - 5.0 + 5.0 * k, y, QZ), (0, 0, 0)):
            b.box((3.2, 1.2, 0.12), (0, 0, 0.9), mat=CONCRETE, tint=srgb(0.82, 0.82, 0.8))
            for (dx, dy) in ((-1.3, -0.45), (1.3, -0.45), (-1.3, 0.45), (1.3, 0.45)):
                b.box((0.12, 0.12, 0.84), (dx, dy, 0.42), mat=PAINT, tint=GREY, solid=False)
            for j in range(3):
                b.box((0.6, 0.4, 0.22), (-0.9 + 0.8 * j, 0.0, 1.07), (0, 0, 8 * j), mat=GLOSS,
                      tint=(srgb(0.2, 0.4, 0.65), srgb(0.75, 0.2, 0.15), srgb(0.2, 0.4, 0.65))[j], solid=False)
    b.cyl(0.02, 1.0, (x + 6.0, y - 1.0, QZ + 3.6), mat=PAINT, tint=GREY, sides=4, solid=False)
    b.cyl(0.18, 0.12, (x + 6.0, y - 1.0, QZ + 3.0), (90, 0, 0), mat=PAINT, tint=LIGHTGREY, sides=12, solid=False)


def build(b):
    b.set_pad(("rect", 100.0, 60.0), ground="concrete", blend=10.0, clear=3.0)

    # The harbour's water.
    P.grid_box(b, (100.0, 36.0, 0.04), (0.0, -12.0, -0.02), mat=GLASS, solid=False, step=6.0)
    quay(b)
    pier(b, -20.0, QY, -24.0, 4.0)
    pier(b, 16.0, QY, -14.0, 5.0, head=(6.0, 26.0, 6.0))
    P.pedestal_crane(b, (21.0, -16.5, DZ), yaw=200.0, jib=13.0, luff=40.0, drop=5.0)

    # The breakwater: a rubble arm with a concrete crown and the harbour light at its end.
    for k in range(40):
        y = QY - 1.0 - 34.0 * k / 39
        for s in (-1, 1):
            r = 1.0 + 0.8 * b.rng.random()
            b.box((r * 1.4, r * 1.2, r), (46.5 + s * 2.2 + b.rng.uniform(-0.6, 0.6), y + b.rng.uniform(-0.4, 0.4),
                                          r * 0.3), (b.rng.uniform(-20, 20), b.rng.uniform(-20, 20), b.rng.uniform(0, 90)),
                  mat=CONCRETE, tint=P.ROCK, solid=k % 3 == 0, vary=0.1)
    P.grid_box(b, (3.0, 35.0, 1.6), (46.5, QY - 18.5, 1.0), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62), step=3.0)
    with b.frame((46.5, -28.5, 1.8), (0, 0, 0)):
        b.cyl(1.1, 0.4, (0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T, sides=16)
        b.cyl(0.6, 5.0, (0, 0, 2.9), mat=PAINT, tint=OFFWHITE, sides=14, radius2=0.45)
        for z in (1.5, 3.0, 4.5):
            b.cyl(0.62 - 0.03 * z, 0.5, (0, 0, 0.4 + z), mat=PAINT, tint=RED, sides=14, solid=False, vary=0)
        b.cyl(0.55, 0.12, (0, 0, 5.5), mat=PAINT, tint=DARK, sides=14)
        b.cyl(0.3, 0.5, (0, 0, 5.85), mat=GLASS, sides=12, solid=False)
        b.box((0.25, 0.25, 0.3), (0, 0, 5.85), mat=LAMP, tint=RED_LAMP, solid=False)
        b.cyl(0.35, 0.3, (0, 0, 6.25), mat=PAINT, tint=DARK, sides=12, radius2=0.08)
        b.marker("light", (0, 0, 5.9), colour=[1.0, 0.12, 0.05], range=800.0, blink=2.5)
        P.rungs(b, (0, -0.68, 0.4), 5.0, width=0.36, rail_tint=GREY)
    b.marker("landmark", (46.5, -28.5, 8.0), kind_of="harbour-light")

    # The boathouse over the slip, a boat hauled up inside on its cradle.
    boathouse(b, -38.0, QY - 6.0, QY + 10.0)
    with b.frame((-38.0, QY + 1.5, 1.6), (-8.0, 0, 180.0)):
        P.fishing_boat(b, (0, 0, 0), 0.0, length=8.0, width=2.9, tint=srgb(0.6, 0.22, 0.16), name="WREN",
                       wheelhouse=False, gantry=False)
        for y in (-2.0, 2.0):
            b.box((2.4, 0.3, 1.0), (0, y, -0.7), mat=PAINT, tint=TIMBER)

    # Boats: one moored at the long pier, one half-sunk off the T-head.
    P.fishing_boat(b, (-15.2, -12.0, 0.0), 0.0, length=11.0, width=3.6, tint=srgb(0.2, 0.34, 0.5), name="KESTREL")
    for (yy, dy) in ((-16.0, -1.0), (-8.0, 1.0)):
        P.ground_cable(b, [(-15.2 - 1.6, yy, 1.3), (-17.0, yy + dy * 0.5, 0.9), (-17.6, yy + dy, DZ + 0.4)], 0.025,
                       srgb(0.75, 0.7, 0.55), wobble=0.05, step=0.4)
    P.fishing_boat(b, (4.0, -9.0, 0.0), 35.0, length=10.0, width=3.4, tint=srgb(0.22, 0.42, 0.34), name="TERN",
                   roll=-24.0, pitch=7.0, sink=1.6, gantry=False)
    P.blob(b, (4.0, -9.0, 0.04), 3.0, srgb(0.12, 0.1, 0.08), points=14, rough=0.4, thick=0.004, mat=GLOSS)

    # The back of the quay: three net sheds, the market shed, an ice house, the winch for the slip.
    for k, x in enumerate((-8.0, -4.2, -0.4)):
        net_shed(b, (x, QY + 15.0, QZ), h=6.0 + 0.8 * k)
    b.marker("loot", (-4.2, QY + 15.0, QZ + 0.3), table="crate")
    market_shed(b, (24.0, QY + 9.0))
    b.marker("loot", (24.0, QY + 9.0, QZ + 1.2), table="food")
    with b.frame((8.0, QY + 16.0, QZ), (0, 0, 0)):
        P.grid_box(b, (5.0, 4.0, 3.0), (0, 0, 1.5), mat=PAINT, tint=OFFWHITE, step=1.0)
        b.box((5.3, 4.3, 0.2), (0, 0, 3.1), mat=CONCRETE, tint=CONCRETE_T)
        b.box((1.0, 0.1, 2.2), (-1.2, -2.04, 1.1), mat=PAINT, tint=TEAL, solid=False)
        P.stencil(b, "ICE", (1.0, -2.02, 1.8), (0, 0, 0), 0.5, TEAL, align="centre")
        b.box((1.2, 0.8, 0.9), (2.0, 2.5, 3.65), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
    with b.frame((-38.0, QY + 14.5, QZ), (0, 0, 0)):
        b.box((2.0, 1.4, 1.2), (0, 0, 0.6), mat=PAINT, tint=srgb(0.3, 0.38, 0.36))
        b.cyl(0.35, 1.4, (0, -0.4, 1.4), (0, 90, 0), mat=PAINT, tint=DARK, sides=12)
        b.tube([(0, -0.75, 1.4), (0, -2.5, 1.2), (0, -7.0, 1.6)], 0.02, mat=RUST, tint=(1, 1, 1), sides=4, cap=False)

    # Quay clutter: nets heaped, creels stacked, fish crates, buoys, drums, coiled warps.
    for (x, y, s) in ((-13.0, QY + 3.0, 1.2), (-26.0, QY + 2.5, 1.0), (32.0, QY + 3.0, 0.9)):
        P.drape(b, (x, y, QZ), (3.0 * s, 2.2 * s), b.rng.uniform(0, 180), 0.5, 4.0, srgb(0.2, 0.32, 0.26),
                cells=(8, 6))
    for k in range(9):
        P.lobster_pot(b, (-10.0 + 0.8 * (k % 3), QY + 4.5 + 0.6 * (k // 3), QZ + 0.4 * (k // 3) * 0), 4.0 * k)
    for k in range(10):
        b.box((0.6, 0.4, 0.25), (12.0 + 0.65 * (k % 4), QY + 3.2 + 0.45 * ((k // 4) % 2), QZ + 0.13 + 0.26 * (k // 8)),
              (0, 0, b.rng.uniform(-4, 4)), mat=GLOSS, tint=(srgb(0.2, 0.4, 0.65), srgb(0.75, 0.2, 0.15))[k % 2],
              solid=False)
    for k in range(6):
        P.ball(b, (-24.0 + 0.7 * k, QY + 4.4 + 0.3 * (k % 2), QZ + 0.3), 0.3, ORANGE if k % 2 else P.SIGN_WHITE,
               mat=GLOSS, sides=10, rings=5)
    b.piece("drum-stack", (36.0, QY + 5.0, QZ), (0, 0, 10.0))
    b.marker("loot", (37.2, QY + 6.2, QZ + 0.05), table="barrel")
    for (x, y) in ((-30.0, QY + 1.4), (2.0, QY + 1.6), (40.0, QY + 1.3)):
        P.coil(b, (x, y, QZ), 0.45, 3.0, 0.035, srgb(0.7, 0.62, 0.45), spread=0.15)
    for (x, y, yaw) in ((-46.0, QY + 20.0, -135.0), (0.0, QY + 3.0, 180.0), (44.0, QY + 20.0, -45.0)):
        b.piece("lamp-post", (x, y, QZ), (0, 0, yaw))

    b.zone("no-build", (0, 0, 0), size=(110.0, 70.0, 30.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "coast", "family": "colonist", "water_level": 0.0})


SUN = (30.0, 210.0)
PEOPLE = [((-18.5, -4.0, DZ), 10.0)]
VIEWS = [
    {"name": "harbour", "target": (0.0, -4.0, 2.5), "distance": 96, "yaw": -24, "pitch": 22, "lens": 30},
    {"name": "pier", "target": (-16.0, -10.0, 3.0), "distance": 18, "yaw": 22, "pitch": 6, "lens": 26},
    {"name": "sheds", "target": (-3.0, 17.0, 4.2), "distance": 19, "yaw": -24, "pitch": 4, "lens": 26},
]
