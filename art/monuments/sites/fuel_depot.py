"""Fuel Depot (MONUMENTS.md §5.2 no. 15, small tier): a Seedward fuel and charge stop at a road junction. A broad canopy
on six columns shelters two pump islands, its front corner fallen where a column buckled; the shop behind has a glass
front mostly shot out, gondolas of empty shelves, a counter and a store room; the buried tanks show as a pad of fill
points with two vent stacks; a tanker trailer stands where it was left; charge posts, a price pylon at the junction, a
car still at a pump. A recycler beside the shop. Crates and barrels, an amber chip at the counter.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, WARM_LAMP, YELLOW,
                        lamp, srgb, stripes)

from monkit.surfaces import ASPHALT, CONCRETE, GLASS, GLOSS, LAMP, MATTE, PAINT, PANEL

KEY = "fuel-depot"
TITLE = "Fuel Depot"
SEED = 1515
BUILDER = {"chunk": 40.0, "max_edge": 2.5}

ROAD_Y = -21.0      # the main road along X
SIDE_X = 29.5       # the side road along Y
CZ = 5.0            # the canopy's underside
SHOP = (-16.0, 4.0, 3.0, 12.0)      # x0, x1, y0, y1
SH = 4.2            # the shop's wall height


def canopy(b):
    """Six columns on two islands, the roof in four quarters; the front right quarter has dropped onto a buckled
    column. Pumps on the islands."""
    x0, x1, y0, y1 = -17.0, 9.0, -12.6, 0.6
    xm, ym = -4.0, -6.0
    for y in (-9.2, -2.8):
        P.grid_box(b, (17.0, 1.3, 0.22), (-4.0, y, 0.11), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71), step=2.0)
        stripes(b, (-12.5, y - 0.655, 0), (4.5, y - 0.655, 0), 0.2, width=0.4, at_z=0.0)
        for x in (-12.4, 4.4):
            b.cyl(0.12, 1.0, (x, y, 0.72), mat=PAINT, tint=YELLOW, sides=10)
        for x in (-8.0, 0.0):
            b.piece("fuel-pump", (x, y, 0.22), (0, 0, 0))
    cols = [(-11.0, -9.2), (-4.0, -9.2), (3.0, -9.2), (-11.0, -2.8), (-4.0, -2.8), (3.0, -2.8)]
    for x, y in cols:
        # A concrete crash plinth round every column's foot, striped.
        P.grid_box(b, (0.8, 0.8, 0.9), (x, y, 0.22 + 0.45), mat=CONCRETE, tint=srgb(0.8, 0.79, 0.76), step=0.5)
        stripes(b, (x - 0.4, y - 0.405, 0), (x + 0.4, y - 0.405, 0), 0.25, width=0.2, at_z=0.82)
        if (x, y) == (3.0, -9.2):
            # Buckled: the lower half leans out of its cracked plinth, the upper half kinks back under the roof.
            b.beam((x, y, 1.0), (x + 0.5, y - 0.3, 2.6), 0.45, 0.45, mat=PANEL, tint=OFFWHITE, solid=True)
            b.beam((x + 0.5, y - 0.3, 2.6), (x + 0.2, y - 0.1, CZ - 1.05), 0.45, 0.45, mat=PANEL, tint=OFFWHITE,
                   solid=True)
            P.clods(b, (x + 0.4, y - 0.5, 0.22), 1.2, count=5, tint=srgb(0.8, 0.79, 0.76), size=0.3, inner=0.5)
            continue
        b.box((0.45, 0.45, CZ - 1.12), (x, y, 1.12 + (CZ - 1.12) / 2), mat=PANEL, tint=OFFWHITE, bevel=0.03)
        b.box((0.47, 0.47, 0.3), (x, y, 1.5), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        b.box((0.47, 0.47, 0.06), (x, y, 1.3), mat=PAINT, tint=TEAL, solid=False, vary=0)
        b.cyl(0.06, CZ - 1.2, (x + 0.29, y + 0.29, 1.12 + (CZ - 1.2) / 2), mat=PAINT, tint=GREY, sides=8,
              solid=False)
    # Drainage channels along the canopy's long edges.
    for y in (-12.9, 0.9):
        b.box((24.0, 0.3, 0.012), (-4.0, y, 0.086), mat="M_Mon_Grate", tint=GREY, solid=False, vary=0)
        b.box((24.0, 0.34, 0.01), (-4.0, y, 0.082), mat=PAINT, tint=DARK, solid=False, vary=0)

    def quarter(qx0, qx1, qy0, qy1, front, right, lights=True):
        w, d = qx1 - qx0, qy1 - qy0
        cx, cy = (qx0 + qx1) / 2, (qy0 + qy1) / 2
        P.grid_box(b, (w, d, 0.12), (cx, cy, CZ + 0.06), mat=PAINT, tint=LIGHTGREY, step=2.5)
        P.grid_box(b, (w, d, 0.1), (cx, cy, CZ + 0.85), mat=PAINT, tint=GREY, step=2.5)
        if front:
            P.grid_box(b, (w, 0.12, 0.95), (cx, qy0 + 0.06, CZ + 0.47), mat=PANEL, tint=OFFWHITE, step=1.5)
            b.box((w, 0.02, 0.22), (cx, qy0 - 0.01, CZ + 0.62), mat=PAINT, tint=ORANGE, solid=False, vary=0)
            b.box((w, 0.02, 0.06), (cx, qy0 - 0.01, CZ + 0.42), mat=PAINT, tint=TEAL, solid=False, vary=0)
        else:
            P.grid_box(b, (w, 0.12, 0.95), (cx, qy1 - 0.06, CZ + 0.47), mat=PANEL, tint=OFFWHITE, step=1.5)
            b.box((w, 0.02, 0.22), (cx, qy1 + 0.01, CZ + 0.62), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        xs = qx1 if right else qx0
        P.grid_box(b, (0.12, d, 0.95), (xs + (-0.06 if right else 0.06), cy, CZ + 0.47), mat=PANEL, tint=OFFWHITE,
                   step=1.5)
        b.box((0.02, d, 0.22), (xs + (0.01 if right else -0.01), cy, CZ + 0.62), mat=PAINT, tint=ORANGE,
              solid=False, vary=0)
        for k in range(2):
            lx = qx0 + w * (k + 0.5) / 2
            b.box((1.4, 0.6, 0.06), (lx, cy, CZ - 0.03), mat=PAINT, tint=DARK, solid=False)
            b.box((1.3, 0.5, 0.02), (lx, cy, CZ - 0.065), mat=LAMP, tint=srgb(0.92, 0.95, 1.0), solid=False, vary=0)
            if lights:
                b.marker("light", (lx, cy, CZ - 0.3), colour=[0.92, 0.95, 1.0], range=16.0)

    quarter(x0, xm, y0, ym, True, False)
    quarter(x0, xm, ym, y1, False, False)
    quarter(xm, x1, ym, y1, False, True)
    with b.frame((0, ym, CZ), (-9.5, 0, 0)):            # hinged at its back edge, the front dropped
        with b.frame((0, -ym, -CZ), (0, 0, 0)):
            quarter(xm, x1, y0, ym, True, True, lights=False)
    # Where it tore: the steel purlins showing in the gap, soffit panels hanging, a lamp on its cable.
    for k in range(5):
        x = xm + 1.0 + k * 2.6
        b.box((0.16, 1.4, 0.3), (x, ym - 0.4, CZ + 0.45), (-5.0, 0, 0), mat=PAINT, tint=DARK)
    b.box((13.0, 0.25, 0.35), ((xm + x1) / 2, ym, CZ + 0.45), mat=PAINT, tint=DARK)
    for k, (x, ang) in enumerate(((xm + 2.0, 62.0), (xm + 6.5, 48.0), (xm + 10.5, 70.0))):
        with b.frame((x, ym - 0.1, CZ), (ang, 0, b.rng.uniform(-8, 8))):
            b.box((1.2, 1.4, 0.03), (0, -0.7, 0), mat=PAINT, tint=LIGHTGREY, solid=False)
    b.box((1.3, 0.5, 0.06), (xm + 8.4, ym - 2.4, CZ - 1.4), (18, 6, 0), mat=PAINT, tint=DARK, solid=False)
    b.tube([(xm + 8.4, ym - 1.0, CZ + 0.1), (xm + 8.45, ym - 1.8, CZ - 0.6), (xm + 8.4, ym - 2.3, CZ - 1.35)],
           0.012, mat=PAINT, tint=BLACK, sides=4, cap=False)
    P.stencil(b, "FUEL", (-10.5, y0 - 0.03, CZ + 0.52), (0, 0, 0), 0.5, DARK, align="centre")
    P.stencil(b, "~ CHARGE", (-10.5, y1 + 0.03, CZ + 0.52), (0, 0, 180), 0.4, DARK, align="centre")
    # Fallen fascia panels and glass from the lamps under the dropped corner.
    P.debris(b, (5.5, -11.5, 0.0), 2.5, count=6, size=1.2, tint=OFFWHITE, mat=PANEL)
    P.debris(b, (2.0, -8.0, 0.0), 1.5, count=4, size=0.5, tint=srgb(0.85, 0.88, 0.9), mat=GLOSS, thick=0.01)


def shop(b):
    """The shop: a glass front toward the canopy (most panes gone), the sales floor with gondolas, a counter by the
    doors, a chiller, a store room at the back with its own door out."""
    x0, x1, y0, y1 = SHOP
    w, d = x1 - x0, y1 - y0
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    t = 0.3
    P.grid_box(b, (w + 0.6, d + 0.6, 0.25), (cx, cy, 0.125), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), step=2.5)
    b.box((w - 0.4, d - 0.4, 0.04), (cx, cy, 0.27), mat=PAINT, tint=srgb(0.62, 0.6, 0.56))
    z0 = 0.25
    # Walls: back with a door, two sides, the store room's partition.
    door_x = x0 + 4.0
    P.grid_box(b, (door_x - 0.6 - x0, t, SH), ((x0 + door_x - 0.6) / 2, y1 - t / 2, z0 + SH / 2), mat=PANEL,
               tint=OFFWHITE, step=1.5)
    P.grid_box(b, (x1 - door_x - 0.6, t, SH), ((door_x + 0.6 + x1) / 2, y1 - t / 2, z0 + SH / 2), mat=PANEL,
               tint=OFFWHITE, step=1.5)
    b.box((1.2, t, SH - 2.3), (door_x, y1 - t / 2, z0 + 2.3 + (SH - 2.3) / 2), mat=PANEL, tint=OFFWHITE)
    with b.frame((door_x + 0.6, y1 + 0.02, z0), (0, 0, -105.0)):
        P.grid_box(b, (1.15, 0.06, 2.28), (-0.58, 0.03, 1.14), mat=PAINT, tint=GREY, step=0.8)
    b.marker("door", (door_x, y1 + 0.7, 0.0), kind_of="back")
    for x in (x0 + t / 2, x1 - t / 2):
        P.grid_box(b, (t, d - 2 * t, SH), (x, cy, z0 + SH / 2), mat=PANEL, tint=OFFWHITE, step=1.5)
    py = y1 - 3.2
    P.grid_box(b, (w - 2 * t - 1.2, 0.15, SH), (cx + 0.6, py, z0 + SH / 2), mat=PANEL, tint=LIGHTGREY, step=1.5)
    b.box((1.2, 0.15, SH - 2.3), (x0 + t + 0.6, py, z0 + 2.3 + (SH - 2.3) / 2), mat=PANEL, tint=LIGHTGREY)
    # The front: a low sill wall, glass panes between mullions (most gone), double doors (one open, one smashed).
    dxc = cx + 2.0
    b.box((w, t, 0.5), (cx, y0 + t / 2, z0 + 0.25), mat=PANEL, tint=OFFWHITE)
    b.box((w, t + 0.1, 0.6), (cx, y0 + t / 2, z0 + SH - 0.3), mat=PANEL, tint=OFFWHITE)
    n = 8
    for k in range(n + 1):
        x = x0 + w * k / n
        b.box((0.14, 0.2, SH - 1.1), (x, y0 + 0.12, z0 + 0.5 + (SH - 1.1) / 2), mat=PAINT, tint=DARK)
    for k in range(n):
        xa, xb = x0 + w * k / n, x0 + w * (k + 1) / n
        xm = (xa + xb) / 2
        if abs(xm - dxc) < 1.5:
            continue
        if b.rng.random() < 0.35:
            b.box((xb - xa - 0.14, 0.02, SH - 1.1), (xm, y0 + 0.12, z0 + 0.5 + (SH - 1.1) / 2), mat=GLASS,
                  solid=False)
        elif b.rng.random() < 0.5:
            b.box((xb - xa - 0.14, 0.02, (SH - 1.1) * 0.35), (xm, y0 + 0.12, z0 + 0.5 + (SH - 1.1) * 0.82),
                  mat=GLASS, solid=False)
    b.box((2.6, 0.2, 0.14), (dxc, y0 + 0.12, z0 + 2.3), mat=PAINT, tint=DARK, solid=False)
    with b.frame((dxc + 1.25, y0 - 0.02, z0), (0, 0, 80.0)):
        b.box((1.2, 0.05, 2.25), (-0.6, 0, 1.13), mat=PAINT, tint=DARK, solid=False)
        b.box((1.05, 0.02, 2.0), (-0.6, 0, 1.13), mat=GLASS, solid=False)
    b.box((1.2, 0.05, 0.12), (dxc - 0.62, y0 + 0.1, z0 + 0.06), mat=PAINT, tint=DARK, solid=False)
    b.marker("door", (dxc, y0 - 0.8, 0.0), kind_of="shop")
    b.collide_box(Vector((cx - 1.9, y0 + 0.12, z0 + SH / 2)), (w / 2 + 2.0 - 1.5, 0.2, SH), (0, 0, 0))
    b.collide_box(Vector((dxc + (x1 - dxc) / 2 + 0.7, y0 + 0.12, z0 + SH / 2)), (x1 - dxc - 1.4, 0.2, SH), (0, 0, 0))
    for k in range(18):
        p = Vector((b.rng.uniform(x0 + 0.5, x1 - 0.5), y0 + b.rng.uniform(-2.0, 1.5), 0.0))
        b.box((0.08 + 0.18 * b.rng.random(), 0.06 + 0.12 * b.rng.random(), 0.006),
              (p.x, p.y, 0.27 if p.y > y0 else 0.11), (0, 0, b.rng.uniform(0, 360)), mat=GLASS, solid=False, vary=0)
    # Roof: slab, parapet, plant, the sign box on the front edge.
    P.grid_box(b, (w, d, 0.25), (cx, cy, z0 + SH + 0.125), mat=CONCRETE, tint=CONCRETE_T, step=3.0)
    for (px, py2, sx, sy) in ((cx, y0 + 0.12, w, 0.24), (cx, y1 - 0.12, w, 0.24), (x0 + 0.12, cy, 0.24, d),
                              (x1 - 0.12, cy, 0.24, d)):
        b.box((sx, sy, 0.5), (px, py2, z0 + SH + 0.5), mat=PANEL, tint=OFFWHITE)
    for x in (cx - 5.0, cx + 1.5):
        b.box((2.0, 1.4, 1.0), (x, cy + 1.5, z0 + SH + 0.75), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
        b.cyl(0.45, 0.15, (x, cy + 1.5, z0 + SH + 1.32), mat=PAINT, tint=GREY, sides=16, solid=False)
    sx0 = cx - 3.5
    b.box((9.0, 0.5, 1.2), (sx0, y0 + 0.1, z0 + SH + 1.35), mat=PANEL, tint=OFFWHITE, bevel=0.03)
    b.box((9.02, 0.52, 0.25), (sx0, y0 + 0.1, z0 + SH + 1.82), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.box((9.02, 0.52, 0.08), (sx0, y0 + 0.1, z0 + SH + 0.9), mat=PAINT, tint=TEAL, solid=False, vary=0)
    P.stencil(b, "DEPOT 4", (sx0, y0 - 0.16, z0 + SH + 1.12), (0, 0, 0), 0.5, DARK, align="centre")
    # The sales floor: three gondolas, the counter and register, a chiller along the left wall.
    for k in range(3):
        gx = cx - 1.5 + 2.6 * k - 2.6
        with b.frame((gx, cy - 0.6, z0), (0, 0, 0)):
            b.box((0.9, 4.2, 0.12), (0, 0, 0.06), mat=PAINT, tint=GREY)
            b.box((0.06, 4.2, 1.6), (0, 0, 0.9), mat=PAINT, tint=LIGHTGREY)
            for z in (0.45, 0.9, 1.35):
                for s in (-1, 1):
                    b.box((0.38, 4.1, 0.03), (s * 0.24, 0, z), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
            for j in range(4):
                if b.rng.random() < 0.5:
                    s = 1 if j % 2 else -1
                    b.box((0.3, 0.4, 0.25), (s * 0.24, -1.6 + 1.0 * j, 0.6 + 0.45 * (j % 3)), mat=MATTE,
                          tint=(ORANGE, TEAL, RED, srgb(0.6, 0.55, 0.4))[j], solid=False)
        b.collide_box(Vector((gx, cy - 0.6, z0 + 0.8)), (0.9, 4.2, 1.6), (0, 0, 0))
    with b.frame((x1 - 3.0, y0 + 2.0, z0), (0, 0, 0)):
        b.box((3.4, 0.8, 1.05), (0, 0, 0.53), mat=PANEL, tint=OFFWHITE)
        b.box((3.5, 0.9, 0.05), (0, 0, 1.08), mat=PAINT, tint=srgb(0.5, 0.42, 0.32))
        b.box((0.45, 0.4, 0.3), (-0.9, 0.0, 1.25), mat=PAINT, tint=DARK, solid=False)
        b.box((0.4, 0.02, 0.22), (-0.9, -0.21, 1.3), mat=GLOSS, tint=srgb(0.05, 0.05, 0.05), solid=False, vary=0)
        b.box((1.8, 0.35, 1.6), (0.4, 1.3, 0.8), mat=PAINT, tint=LIGHTGREY, solid=False)
    b.marker("loot", (x1 - 3.6, y0 + 2.0, z0 + 1.12), table="chip-amber", chance=0.5)
    b.marker("loot", (x1 - 2.4, y0 + 3.0, z0 + 0.05), table="crate")
    with b.frame((x0 + 0.75, cy - 0.8, z0), (0, 0, 0)):
        b.box((0.9, 4.4, 2.1), (0, 0, 1.05), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
        for k in range(4):
            b.box((0.02, 1.0, 1.5), (0.46, -1.65 + 1.1 * k, 1.1), mat=GLASS if k != 2 else MATTE,
                  tint=(1, 1, 1) if k != 2 else srgb(0.04, 0.04, 0.04), solid=False)
    # The store room: racks, a crate, barrels.
    for k in range(3):
        rx = x0 + 3.0 + 4.0 * k
        if rx < x0 + 2.4:
            continue
        with b.frame((rx + 2.0, y1 - 0.6, z0), (0, 0, 0)):
            for x in (-1.2, 1.2):
                for y in (-0.3, 0.3):
                    b.box((0.06, 0.06, 2.4), (x, y, 1.2), mat=PAINT, tint=ORANGE, solid=False, vary=0)
            for z in (0.3, 1.2, 2.1):
                b.box((2.5, 0.7, 0.05), (0, 0, z), mat=PAINT, tint=GREY, solid=False, vary=0)
                if b.rng.random() < 0.6:
                    b.box((0.6, 0.5, 0.4), (b.rng.uniform(-0.8, 0.8), 0, z + 0.23), mat=MATTE,
                          tint=srgb(0.6, 0.48, 0.33), solid=False)
    b.marker("loot", (x0 + 6.0, y1 - 1.8, z0 + 0.05), table="crate")
    b.marker("loot", (x1 - 1.0, y1 - 1.5, z0 + 0.05), table="barrel")
    # Graffiti on the side wall: the Reclaimers' mark.
    P.stencil(b, "DRY", (x0 - 0.01, cy + 1.2, 1.4), (0, 0, -90), 1.0, srgb(0.72, 0.12, 0.1), align="centre",
              jitter=0.8)


def tank_field(b):
    """The buried tanks' pad: four fill points with coloured rings, bollards, two vent stacks with arrestor caps."""
    tx, ty = 14.0, -3.5
    P.grid_box(b, (9.0, 7.0, 0.2), (tx, ty, 0.1), mat=CONCRETE, tint=srgb(0.78, 0.77, 0.74), step=2.0)
    for k, col in enumerate((ORANGE, TEAL, YELLOW, RED)):
        x = tx - 3.0 + 2.0 * k
        b.cyl(0.42, 0.05, (x, ty - 1.0, 0.22), mat=PAINT, tint=GREY, sides=18)
        b.cyl(0.36, 0.04, (x, ty - 1.0, 0.255), mat=PAINT, tint=col, sides=18, solid=False, vary=0)
        b.cyl(0.18, 0.06, (x, ty - 1.0, 0.28), mat=PAINT, tint=DARK, sides=12, solid=False)
        b.box((0.7, 0.7, 0.02), (x, ty + 1.2, 0.21), mat=PAINT, tint=DARK, solid=False)
    for (x, y) in ((tx - 4.6, ty - 3.6), (tx + 4.6, ty - 3.6), (tx - 4.6, ty + 3.6), (tx + 4.6, ty + 3.6),
                   (tx, ty - 3.6)):
        b.cyl(0.11, 1.0, (x, y, 0.5), mat=PAINT, tint=YELLOW, sides=10)
        b.cyl(0.115, 0.12, (x, y, 0.8), mat=PAINT, tint=BLACK, sides=10, solid=False, vary=0)
    for k, x in enumerate((19.6, 20.6)):
        b.box((0.5, 0.5, 0.4), (x, 1.2, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        b.cyl(0.06, 4.6, (x, 1.2, 2.7), mat=PAINT, tint=OFFWHITE, sides=10)
        b.cyl(0.1, 0.25, (x, 1.2, 5.1), mat=PAINT, tint=GREY, sides=10)
        b.cyl(0.18, 0.1, (x, 1.2, 5.28), mat=PAINT, tint=GREY, sides=10, radius2=0.05, solid=False)
    b.box((0.4, 1.0, 0.04), (20.1, 1.2, 3.6), mat=PAINT, tint=GREY, solid=False)
    b.tube([(19.6, 1.2, 0.4), (19.6, 0.0, 0.05), (tx + 3.0, ty + 1.2, 0.22)], 0.05, mat=PAINT, tint=OFFWHITE,
           sides=8)
    P.plate(b, (tx, ty - 3.52, 0.5), (0, 0, 0), (1.0, 0.5), YELLOW, None, "NO FLAME", text_h=0.13)
    with b.frame((tx + 3.4, ty + 2.6, 0.2), (0, 0, 0)):
        b.box((1.0, 0.6, 0.8), (0, 0, 0.4), mat=PAINT, tint=YELLOW, bevel=0.02)
        b.box((1.02, 0.62, 0.06), (0, 0, 0.6), mat=PAINT, tint=BLACK, solid=False, vary=0)
    b.marker("loot", (tx - 3.5, ty + 2.6, 0.22), table="fuel-barrel")


def tyre_stack(b, at, n=4, yaw=0.0):
    """Old tyres stacked flat (a closed ring of rubber each)."""
    x, y, z = at
    prof = [(0.3, 0.0), (0.5, 0.0), (0.54, 0.08), (0.54, 0.24), (0.5, 0.31), (0.3, 0.31), (0.27, 0.155)]
    for k in range(n):
        b.lathe(prof, (x + b.rng.uniform(-0.05, 0.05), y + b.rng.uniform(-0.05, 0.05), z + 0.31 * k),
                (b.rng.uniform(-2, 2), b.rng.uniform(-2, 2), yaw), mat=MATTE, tint=P.RUBBER, sides=16, solid=False)
    b.collide_box(Vector((x, y, z + 0.155 * n)), (1.08, 1.08, 0.31 * n), (0, 0, 0))


def sign_pylon(b, at):
    """The price pylon at the junction: two legs and a tall panel with the depot's name, the services, lamps."""
    x, y, z = at
    for dx in (-1.1, 1.1):
        b.box((0.5, 0.5, 0.6), (x + dx, y, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        b.box((0.25, 0.25, 7.6), (x + dx, y, 3.8), mat=PAINT, tint=GREY)
    with b.frame((x, y, 0), (0, 0, 0)):
        P.grid_box(b, (2.9, 0.35, 3.2), (0, 0, 5.6), mat=PANEL, tint=OFFWHITE, step=0.8)
        b.box((2.92, 0.37, 0.4), (0, 0, 7.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        b.box((2.92, 0.37, 0.1), (0, 0, 4.1), mat=PAINT, tint=TEAL, solid=False, vary=0)
        P.stencil(b, "DEPOT", (0, -0.18, 6.15), (0, 0, 0), 0.32, DARK, align="centre")
        P.stencil(b, "4", (0, -0.18, 5.15), (0, 0, 0), 0.8, ORANGE, align="centre")
        P.stencil(b, "FUEL ~", (0, -0.18, 4.45), (0, 0, 0), 0.24, DARK, align="centre")
        for dx in (-0.9, 0.9):
            lamp(b, (dx, -0.6, 7.5), (-35, 0, 0), size=(0.36, 0.2, 0.12))
    b.marker("landmark", (x, y, 7.6), kind_of="fuel-sign")


def car_wash(b, at):
    """An automatic wash bay open at both ends: three steel portal frames, a monopitch roof of corrugated sheet,
    cladding on the outer side and half-height on the inner, a floor drain; inside, the wash gantry on its rails with
    two upright brushes, a top brush and nozzle arches; a pay post at the entry."""
    cx, cy = at[0], at[1]
    w, d, h = 5.0, 10.0, 4.2
    hw, hd = w / 2, d / 2
    with b.frame((cx, cy, 0.08), (0, 0, 0)):
        P.grid_box(b, (w + 0.4, d + 0.4, 0.15), (0, 0, 0.075), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.7), step=2.0)
        b.box((0.3, d - 0.6, 0.012), (0, 0, 0.156), mat="M_Mon_Grate", tint=GREY, solid=False, vary=0)
        for y in (-hd + 0.3, 0.0, hd - 0.3):
            for x in (-hw, hw):
                b.box((0.3, 0.3, h), (x, y, 0.15 + h / 2), mat=PAINT, tint=DARK)
            b.box((w + 0.3, 0.3, 0.45), (0, y, 0.15 + h + 0.1), mat=PAINT, tint=DARK)
        for k in range(8):
            x = -hw - 0.4 + (w + 0.8) * (k + 0.5) / 8
            b.box((w / 8 + 0.12, d + 0.8, 0.06), (x, 0, 0.15 + h + 0.42 + 0.05 * (x / w)), (0, 2.0, 0),
                  mat="M_Mon_Corrugated", tint=srgb(0.6, 0.63, 0.62))
        b.box((w + 0.9, 0.08, 0.7), (0, -hd - 0.4, 0.15 + h + 0.3), mat=PANEL, tint=OFFWHITE)
        b.box((w + 0.92, 0.1, 0.2), (0, -hd - 0.4, 0.15 + h + 0.48), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        P.stencil(b, "WASH", (0, -hd - 0.45, 0.15 + h + 0.08), (0, 0, 0), 0.3, DARK, align="centre")
        for k in range(10):
            y = -hd + d * (k + 0.5) / 10
            if b.rng.random() > 0.15:
                P.grid_box(b, (0.06, d / 10 - 0.02, h - 0.4), (-hw - 0.2, y, 0.35 + (h - 0.4) / 2),
                           mat="M_Mon_Corrugated", tint=srgb(0.6, 0.63, 0.62), step=1.5)
            if b.rng.random() > 0.2:
                P.grid_box(b, (0.06, d / 10 - 0.02, 1.5), (hw + 0.2, y, 0.35 + h - 0.75 - 0.4),
                           mat="M_Mon_Corrugated", tint=srgb(0.6, 0.63, 0.62), step=1.5)
        for x in (-1.3, 1.3):
            b.box((0.12, d - 1.0, 0.08), (x, 0, 0.19), mat=PAINT, tint=GREY, solid=False)
        # The gantry: two upright housings, a top beam, brushes, nozzle arches.
        with b.frame((0, 1.2, 0.15), (0, 0, 0)):
            for s in (-1, 1):
                b.box((0.55, 0.7, 3.2), (s * 1.95, 0, 1.6), mat=PANEL, tint=TEAL, bevel=0.04)
                b.box((0.56, 0.71, 0.2), (s * 1.95, 0, 2.6), mat=PAINT, tint=ORANGE, solid=False, vary=0)
                b.box((0.4, 0.5, 0.18), (s * 1.95, 0, 0.09), mat=PAINT, tint=DARK)
                b.cyl(0.45, 2.5, (s * 1.25, -0.1, 1.45), mat=MATTE, tint=srgb(0.18, 0.32, 0.55), sides=18)
                for z in (0.4, 1.2, 2.0, 2.6):
                    b.cyl(0.47, 0.06, (s * 1.25, -0.1, z), mat=MATTE, tint=srgb(0.14, 0.26, 0.46), sides=18,
                          solid=False, vary=0)
                b.cyl(0.06, 0.4, (s * 1.25, -0.1, 2.85), mat=PAINT, tint=GREY, sides=8, solid=False)
            b.box((4.45, 0.7, 0.55), (0, 0, 3.45), mat=PANEL, tint=TEAL, bevel=0.04)
            b.cyl(0.38, 2.6, (0, -0.05, 2.75), (0, 90, 0), mat=MATTE, tint=srgb(0.18, 0.32, 0.55), sides=18)
            for y in (-0.9, 0.9):
                pts = [(-1.7, y, 0.3), (-1.7, y, 3.0), (1.7, y, 3.0), (1.7, y, 0.3)]
                b.tube(pts, 0.03, mat=PAINT, tint=GREY, sides=5, cap=False)
            P.stencil(b, "WX 2", (0, -0.36, 3.33), (0, 0, 0), 0.18, P.SIGN_WHITE, align="centre")
        # Pay post at the entry.
        with b.frame((hw + 0.9, -hd - 1.6, 0.0), (0, 0, 0)):
            b.box((0.4, 0.4, 0.12), (0, 0, 0.06), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.7))
            b.box((0.3, 0.32, 1.35), (0, 0, 0.8), mat=PANEL, tint=OFFWHITE, bevel=0.04)
            b.box((0.22, 0.02, 0.16), (-0.152 * 0 + 0.0, -0.17, 1.2), mat=GLOSS, tint=srgb(0.05, 0.05, 0.05),
                  solid=False, vary=0)
            b.box((0.31, 0.33, 0.12), (0, 0, 1.4), mat=PAINT, tint=TEAL, solid=False, vary=0)


def bottle_cage(b, at):
    """A gas-bottle cage: a mesh cage on a slab with a sloped sheet roof, its door hanging, the bottles inside."""
    x, y = at[0], at[1]
    with b.frame((x, y, 0.08), (0, 0, 90)):
        b.box((2.3, 1.2, 0.12), (0, 0, 0.06), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.7))
        for sx in (-1.05, 1.05):
            for sy in (-0.5, 0.5):
                b.box((0.05, 0.05, 1.9), (sx, sy, 1.07), mat=PAINT, tint=GREY, solid=False)
        for (cx, cy, sx, sy) in ((0, 0.5, 2.1, 0.02), (-1.05, 0, 0.02, 1.0), (1.05, 0, 0.02, 1.0)):
            b.box((sx, sy, 1.75), (cx, cy, 1.0), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
        b.box((2.3, 1.3, 0.04), (0, 0.05, 2.05), (-8, 0, 0), mat="M_Mon_Corrugated", tint=srgb(0.6, 0.63, 0.62))
        with b.frame((-1.05, -0.5, 0.12), (0, 0, -70)):
            b.box((1.0, 0.03, 1.75), (0.52, 0, 0.9), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
            b.box((1.0, 0.04, 0.04), (0.52, 0, 1.75), mat=PAINT, tint=GREY, solid=False)
        for k in range(6):
            bx = -0.8 + 0.32 * k
            by = 0.15 if k % 2 else -0.15
            tint = ORANGE if k % 3 else P.SIGN_WHITE
            b.cyl(0.15, 1.0, (bx, by, 0.62), mat=PAINT, tint=tint, sides=12, solid=False)
            b.lathe([(0.15, 0.0), (0.12, 0.08), (0.05, 0.12), (0.0, 0.12)], (bx, by, 1.12), mat=PAINT, tint=tint,
                    sides=12, solid=False)
            b.cyl(0.03, 0.08, (bx, by, 1.27), mat=PAINT, tint=GREY, sides=6, solid=False)
        b.collide_box(Vector((0, 0, 1.0)), (2.1, 1.0, 2.0), (0, 0, 0))


def barricade(b, x, y0, y1):
    """A Reclaimer barricade closing a driveway: barriers along it, scrap sheets leaned and wired on, tyres, a
    painted mark."""
    n = 3
    for k in range(n):
        y = y0 + (y1 - y0) * (k + 0.5) / n
        b.piece("jersey-barrier", (x, y, 0.08), (0, 0, 90.0 + b.rng.uniform(-5, 5)))
        with b.frame((x - 0.35, y, 0.1), (0, -14.0 + b.rng.uniform(-4, 4), 90.0)):
            mat = "M_Mon_Corrugated" if k != 1 else "M_Mon_Rust"
            b.box((2.6 + 0.4 * b.rng.random(), 0.05, 1.7), (b.rng.uniform(-0.2, 0.2), 0, 0.9), mat=mat,
                  tint=(srgb(0.55, 0.3, 0.2), (1, 1, 1), srgb(0.5, 0.55, 0.5))[k], solid=False)
    P.stencil(b, "X", (x - 0.62, (y0 + y1) / 2 + 0.2, 0.9), (0, -14.0, 90.0), 0.9, srgb(0.75, 0.1, 0.08),
              align="centre", jitter=0.8)
    tyre_stack(b, (x - 1.2, y0 - 0.5, 0.08), 3, 15.0)
    tyre_stack(b, (x - 1.0, y1 + 0.8, 0.08), 2, 40.0)
    P.coil(b, (x - 1.5, (y0 + y1) / 2 - 1.5, 0.08), 0.5, 3.0, 0.01, srgb(0.5, 0.5, 0.5), spread=0.2)


def build(b):
    b.set_pad(("rect", 66.0, 46.0), ground="asphalt", blend=10.0, clear=3.0)

    # Roads: the main road along the front, the side road at the right; the forecourt and its kerbs.
    P.long_road(b, (-35.0, ROAD_Y), (35.0, ROAD_Y), width=8.0)
    P.long_road(b, (SIDE_X, ROAD_Y + 4.0), (SIDE_X, 23.0), width=7.0)
    b.box((51.0, 31.0, 0.08), (-1.0, -1.5, 0.04), mat=ASPHALT, tint=srgb(0.9, 0.9, 0.9), wear=0.4)
    for (ax, bx) in ((-26.5, -22.0), (-13.0, 9.0), (18.0, 24.5)):
        b.box((bx - ax, 0.3, 0.16), ((ax + bx) / 2, -17.0, 0.08), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
    for (ay, by) in ((-17.0, -9.0), (1.0, 14.0)):
        b.box((0.3, by - ay, 0.16), (24.5, (ay + by) / 2, 0.08), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
    for k in range(6):
        x = -15.3 + 2.8 * k
        b.box((0.12, 4.5, 0.012), (x, 0.4, 0.086), mat=PAINT, tint=srgb(0.85, 0.83, 0.75), solid=False, vary=0,
              wear=0.7)
    for x, y, r in ((-6.0, -6.0, 1.6), (2.0, -1.0, 1.0), (12.0, -12.0, 1.4)):
        P.blob(b, (x, y, 0.08), r, srgb(0.05, 0.05, 0.05), points=12, rough=0.4)

    canopy(b)
    shop(b)
    tank_field(b)
    sign_pylon(b, (21.0, -15.0, 0.0))

    # The recycler by the shop's right side, the charge posts on its left, the air post, the dumpster.
    b.piece("recycler", (7.6, 6.0, 0.08), (0, 0, 0))
    for y in (4.5, 7.5):
        with b.frame((-18.0, y, 0.08), (0, 0, 90)):
            b.box((0.5, 0.5, 0.12), (0, 0, 0.06), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
            b.box((0.35, 0.3, 1.7), (0, 0, 0.97), mat=PANEL, tint=OFFWHITE, bevel=0.04)
            b.box((0.36, 0.31, 0.22), (0, 0, 1.6), mat=PAINT, tint=TEAL, solid=False, vary=0)
            b.box((0.24, 0.02, 0.18), (0, -0.16, 1.3), mat=GLOSS, tint=srgb(0.05, 0.05, 0.05), solid=False,
                  vary=0)
            b.tube([(0.18, 0.0, 1.1), (0.3, -0.1, 0.6), (0.25, -0.2, 0.15), (0.05, -0.3, 0.1), (-0.1, -0.2, 0.9)],
                   0.025, mat=PAINT, tint=BLACK, sides=5, cap=False)
    with b.frame((6.0, -14.6, 0.08), (0, 0, 0)):
        b.box((0.3, 0.3, 1.2), (0, 0, 0.6), mat=PAINT, tint=RED)
        b.box((0.4, 0.3, 0.3), (0, 0, 1.3), mat=PAINT, tint=RED, bevel=0.02)
        P.coil(b, (0.3, -0.4, 0.0), 0.3, 3.0, 0.015, BLACK, spread=0.1)
    b.piece("dumpster", (1.8, 13.6, 0.08), (0, 0, 8.0))
    b.piece("pallet-boxes", (-10.0, 13.4, 0.08), (0, 0, 4.0))
    b.piece("pallet-boxes", (-8.6, 13.6, 0.08), (0, 0, -10.0))
    tyre_stack(b, (5.6, 12.0, 0.08), 4)
    tyre_stack(b, (6.8, 12.6, 0.08), 2, 30.0)
    b.piece("drum-stack", (9.0, 1.6, 0.08), (0, 0, 15.0))
    b.marker("loot", (3.2, 13.0, 0.1), table="barrel")
    b.marker("loot", (10.2, 1.0, 0.1), table="barrel")

    # The tanker trailer parked by the tanks, a car abandoned at a pump, another dead in a bay.
    valve = P.tanker_trailer(b, (13.0, 8.6, 0.08), (0, 0, 90.0), code="F-11")
    b.marker("loot", (valve.x, valve.y, 0.1), table="fuel-barrel")
    P.pod_car(b, (-8.0, -6.0, 0.08), (0, 0, 90.0), tint=srgb(0.7, 0.28, 0.2), wrecked=0.4, doors=(55.0, 0.0))
    P.pod_car(b, (-12.7, 0.9, 0.08), (0, 0, 180.0), tint=srgb(0.55, 0.62, 0.66), wrecked=0.9, wheels=False)
    b.marker("loot", (-8.0, -4.4, 0.1), table="crate")

    # The wash bay at the west end, the bottle cage behind the shop, the Reclaimers' barricade across the side
    # entrance, weeds in the cracks.
    car_wash(b, (-22.6, -6.0))
    bottle_cage(b, (-17.9, 10.6))
    barricade(b, 25.6, -8.6, 0.6)
    for (x, y, s) in ((-25.8, -15.6, 0.7), (-20.4, 13.4, 0.9), (22.8, -16.2, 0.6), (23.6, 5.0, 0.8),
                      (8.4, -15.9, 0.5), (-14.6, 13.9, 0.6), (-1.0, -16.4, 0.45)):
        P.bush(b, (x, y, 0.08), s, P.LEAF if s > 0.6 else P.LEAF_DRY, lumps=4)

    # Lamp posts at the forecourt corners, one of them down.
    for (x, y, yaw) in ((-25.5, -15.8, 135.0), (-25.5, 13.0, -135.0)):
        b.piece("lamp-post", (x, y, 0.08), (0, 0, yaw))
    b.piece("lamp-post", (23.6, 12.6, 0.42), (88.0, 0, -20.0))
    b.box((0.6, 0.6, 0.5), (22.9, 12.9, 0.2), (0, 14, 0), mat=CONCRETE, tint=CONCRETE_T)

    b.zone("no-build", (0, 0, 0), size=(80.0, 60.0, 30.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "junction"})


SUN = (30.0, 215.0)
PEOPLE = [((-2.0, -13.6, 0.08), 15.0)]
VIEWS = [
    {"name": "depot", "target": (-1.0, -1.0, 2.5), "distance": 66, "yaw": 26, "pitch": 24, "lens": 30},
    {"name": "pumps", "target": (-4.0, -5.0, 2.4), "distance": 17, "yaw": -24, "pitch": 3, "lens": 24},
    {"name": "shop", "target": (-8.0, 8.0, 1.3), "distance": 9.3, "yaw": 66, "pitch": 2.5, "lens": 18},
]
