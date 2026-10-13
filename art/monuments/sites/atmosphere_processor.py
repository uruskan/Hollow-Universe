"""Atmosphere Processor (MONUMENTS.md §5.5, landmark tier): one of the old expansion's terraforming towers, 860 m
tall, standing in a walled works 1.3 km across. Four buttresses carry a tapering shaft; intake halls ring its foot;
a crown of emitters at the top still blinks. The shaft is a vertical dungeon (the climb, §5.5); the works round it
hold processing halls, pipe bridges, a power yard and a landing apron. Seen from 40+ km in thick air.
"""
import math

import numpy as np
from mathutils import Vector

from monkit import parts_landmarks as P
from monkit.kit import (CONCRETE, CONCRETE_T, DARK, FORMWORK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW,
                        cable, fence, lattice_tower, rail_loop, railing, srgb, stripes, tank)
from monkit.structures import chimney, conveyor, road, shed, silo, wall
from monkit.surfaces import CORRUGATED, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "atmosphere-processor"
TITLE = "Atmosphere Processor"
SEED = 51
BUILDER = {"chunk": 160.0, "max_edge": 14.0, "lod_min": (0.0, 2.0, 14.0)}
LODS = [400.0, 2500.0, 80000.0]

H = 860.0       # shaft top
BASE_R = 95.0   # shaft radius at the foot
TOP_R = 26.0
SHELL = "M_Mon_Hull"


def shaft_radius(z):
    """The shaft's radius at height z: a long concave taper (wide skirt, slim neck)."""
    t = z / H
    return TOP_R + (BASE_R - TOP_R) * (1 - t) ** 2.2


def buttress_detail(b, outline):
    """A buttress's flanks and spine: pour joints every 12 m, darker cast panels, the climbing spine up its
    sloping edge (a grated stair-ladder between rails, rest platforms every 24 m of height), lamps."""
    (x0, _), (x1, _), (_, z1), (x2, z2), (x3, z3), _ = outline

    def edge_x(z):
        if z <= z1:
            return x1
        if z <= z2:
            return x1 + (x2 - x1) * (z - z1) / (z2 - z1)
        return x2 + (x3 - x2) * (z - z2) / (z3 - z2)
    for s in (-1, 1):
        z = 12.0
        while z < z3 - 4:
            xa, xb = x0 + 2.0, edge_x(z) - 1.5
            if xb - xa > 4:
                b.box((xb - xa, 0.3, 0.45), ((xa + xb) / 2, s * 11.1, z), mat=PAINT, tint=srgb(0.6, 0.6, 0.58),
                      solid=False, vary=0)
            z += 12.0
        for k in range(5):
            z = 30.0 + k * 55.0
            xa, xb = x0 + 6.0, edge_x(z + 20.0) - 6.0
            if xb - xa > 10:
                b.box((xb - xa, 0.25, 22.0), ((xa + xb) / 2, s * 11.05, z + 11.0), mat=FORMWORK,
                      tint=srgb(0.70, 0.69, 0.66), solid=False, vary=0.03)
    # The spine along the sloping edge.
    a = Vector((x1 - 0.5, 0, z1 + 0.8))
    c = Vector((x2 - 0.5, 0, z2 + 0.8))
    d = c - a
    L = d.length
    pitch = math.degrees(math.atan2(d.z, -d.x))
    mid = (a + c) / 2
    b.box((L, 1.4, 0.12), tuple(mid), (0, pitch, 180), mat=GRATE, tint=GREY, solid=False, vary=0)
    for e in (-0.9, 0.9):
        b.beam(tuple(a + Vector((0, e, 1.1))), tuple(c + Vector((0, e, 1.1))), 0.07, 0.07, mat=PAINT, tint=YELLOW)
        b.beam(tuple(a + Vector((0, e, 0.1))), tuple(c + Vector((0, e, 0.1))), 0.12, 0.2, mat=PAINT, tint=DARK)
        n = int(L / 3.0)
        for k in range(n + 1):
            p = a.lerp(c, k / n)
            b.box((0.06, 0.06, 1.1), (p.x, e, p.z + 0.55), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    zp = z1 + 24.0
    while zp < z2 - 4:
        xp = edge_x(zp)
        b.box((4.0, 5.0, 0.2), (xp - 1.0, 0, zp + 0.8), mat=GRATE, tint=GREY, solid=False, vary=0)
        corners = [(xp - 3.0, -2.5, zp + 0.9), (xp + 1.0, -2.5, zp + 0.9), (xp + 1.0, 2.5, zp + 0.9),
                   (xp - 3.0, 2.5, zp + 0.9)]
        for i in range(3):
            railing(b, corners[i], corners[i + 1], 1.1, spacing=1.6, toe=False)
        b.box((0.5, 0.4, 0.3), (xp + 0.8, 0, zp + 3.2), mat=LAMP, tint=srgb(1.0, 0.85, 0.6), solid=False, vary=0)
        zp += 24.0


def shaft_slope(z):
    """The shaft wall's lean at height z (degrees from vertical, top leaning in)."""
    t = z / H
    dr = (BASE_R - TOP_R) * 2.2 * (1 - t) ** 1.2 / H
    return math.degrees(math.atan(dr))


def on_shaft(b, ang, z, size, out=0.2, mat=PAINT, tint=DARK, solid=False, vary=0.0):
    """A plate lying on the shaft's surface at angle `ang` (radians) and height z: `size` = (thickness, width,
    height), leaning with the taper."""
    with b.frame((0, 0, 0), (0, 0, math.degrees(ang))):
        b.box(size, (shaft_radius(z) + out, 0, z), (0, -shaft_slope(z), 0), mat=mat, tint=tint, solid=solid,
              vary=vary)


def shaft_detail(b):
    """The shaft's skin: pilasters up its taper, louvre rings, service doors at the galleries, missing and burnt
    panels, rust runs under the bands, a climber car stopped on the spine."""
    zs = list(np.linspace(80.0, H - 24.0, 26))
    for k in range(32):
        ang = 2 * math.pi * (k + 0.5) / 32
        pts = [(math.cos(ang) * (shaft_radius(z) + 0.9), math.sin(ang) * (shaft_radius(z) + 0.9), z) for z in zs]
        b.tube(pts, 1.1, mat=PAINT, tint=srgb(0.74, 0.74, 0.72), sides=6, cap=False)
    for z in (110.0, 215.0, 405.0, 705.0):
        r = shaft_radius(z)
        n = 56
        for k in range(n):
            ang = 2 * math.pi * k / n
            on_shaft(b, ang, z, (0.6, 2 * math.pi * r / n * 0.62, 7.0), out=0.3, mat=PAINT, tint=srgb(0.18, 0.19, 0.2))
            for q in range(4):
                on_shaft(b, ang, z - 2.6 + q * 1.7, (1.4, 2 * math.pi * r / n * 0.62, 0.3), out=0.8, mat=PAINT,
                         tint=GREY)
    for _ in range(55):
        ang = b.rng.uniform(0, 2 * math.pi)
        z = b.rng.uniform(80.0, H - 30.0)
        w, h = b.rng.uniform(4.0, 14.0), b.rng.uniform(3.0, 10.0)
        burnt = b.rng.random() < 0.4
        on_shaft(b, ang, z, (0.35, w, h), out=0.25, mat=MATTE, tint=srgb(0.1, 0.095, 0.09) if burnt else
                 srgb(0.22, 0.23, 0.24), vary=0.08)
    for zb in list(np.arange(140.0, H - 40, 120.0)) + [180.0, 330.0, 480.0, 630.0, 780.0]:
        for _ in range(10):
            ang = b.rng.uniform(0, 2 * math.pi)
            L = b.rng.uniform(10.0, 45.0)
            on_shaft(b, ang, zb - L / 2 - 2.0, (0.3, b.rng.uniform(0.8, 2.5), L), out=0.2, mat=MATTE,
                     tint=srgb(0.42, 0.27, 0.17), vary=0.1)
    # Service doors and lamps where each gallery meets the shaft.
    for z in (180.0, 330.0, 480.0, 630.0, 780.0):
        for k in range(4):
            ang = math.radians(45 + 90 * k)
            with b.frame((0, 0, 0), (0, 0, math.degrees(ang))):
                b.box((1.2, 3.4, 4.0), (shaft_radius(z) + 0.6, 0, z + 2.6), mat=PAINT, tint=TEAL, solid=False)
                b.box((1.4, 4.6, 0.6), (shaft_radius(z) + 0.8, 0, z + 4.9), mat=PAINT, tint=DARK, solid=False)
                b.box((0.6, 0.8, 0.4), (shaft_radius(z) + 1.3, 0, z + 5.4), mat=LAMP, tint=srgb(1.0, 0.85, 0.6),
                      solid=False)
            b.marker("door", (math.cos(ang) * (shaft_radius(z) + 1.6), math.sin(ang) * (shaft_radius(z) + 1.6),
                              z + 0.6), kind_of="gallery")
    # A climber car stopped on the spine at 420 m.
    zc = 420.0
    y = shaft_radius(zc) + 8.0
    b.box((10.0, 7.0, 14.0), (0, y + 1.0, zc), mat=HULL, tint=OFFWHITE, bevel=0.4)
    b.box((10.3, 7.3, 1.6), (0, y + 1.0, zc + 3.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.box((8.0, 0.2, 2.0), (0, y + 4.6, zc + 0.5), mat=GLASS, solid=False, vary=0)
    for zz in (zc - 6.0, zc + 6.0):
        b.box((6.0, 3.0, 1.6), (0, y - 3.4, zz), mat=PAINT, tint=DARK, solid=False)


def crown(b):
    """The crown: the neck flares into a capital with a gallery; twelve emitter vanes lean out from it, braced by
    a ring; a beacon dome and a lamp-hung mast stand in the middle."""
    prof = [(shaft_radius(H - 80.0) + 0.5, H - 80.0), (TOP_R + 3.0, H - 44.0), (TOP_R + 8.0, H - 22.0),
            (TOP_R + 15.0, H - 8.0), (TOP_R + 17.0, H), (TOP_R + 12.0, H + 3.0), (0.0, H + 3.0)]
    b.lathe(prof, (0, 0, 0), mat=HULL, tint=srgb(0.78, 0.78, 0.76), sides=64, solid="hull")
    b.cyl(TOP_R + 17.4, 2.4, (0, 0, H - 2.2), mat=PAINT, tint=ORANGE, sides=64, solid=False, vary=0)
    b.cyl(TOP_R + 16.2, 1.2, (0, 0, H - 6.4), mat=PAINT, tint=TEAL, sides=64, solid=False, vary=0)
    for k in range(24):
        ang = 2 * math.pi * k / 24
        b.box((1.0, 3.0, 16.0), (math.cos(ang) * (TOP_R + 10.5), math.sin(ang) * (TOP_R + 10.5), H - 22.0),
              (0, -30.0, math.degrees(ang)), mat=PAINT, tint=DARK, solid=False)
    b.cyl(TOP_R + 21.0, 0.6, (0, 0, H + 3.3), mat=GRATE, tint=GREY, sides=64, solid=False)
    pts = [(math.cos(a) * (TOP_R + 20.5), math.sin(a) * (TOP_R + 20.5), H + 3.6) for a in
           (2 * math.pi * k / 48 for k in range(49))]
    for p, q in zip(pts, pts[1:]):
        railing(b, p, q, 1.1, spacing=3.0, toe=False)
    # Emitter vanes.
    tips = []
    for k in range(12):
        ang = 2 * math.pi * k / 12
        with b.frame((math.cos(ang) * (TOP_R + 9.0), math.sin(ang) * (TOP_R + 9.0), H + 3.0),
                     (0, 0, math.degrees(ang))):
            with b.frame((0, 0, 0), (0, 14.0, 0)):
                b.extrude([(-4.0, 0.0), (4.0, 0.0), (2.4, 58.0), (0.6, 66.0), (-1.8, 58.0)], 3.0, (0, 0, 0),
                          (0, 0, 0), mat=HULL, tint=OFFWHITE)
                b.box((3.2, 3.2, 4.0), (0.3, 0, 30.0), mat=PAINT, tint=ORANGE if k % 2 else TEAL, solid=False,
                      vary=0)
                b.box((1.8, 1.8, 1.8), (0.6, 0, 66.6), mat=LAMP, tint=srgb(1.0, 0.12, 0.05), solid=False, vary=0)
                tips.append(b.world((0.6, 0, 40.0)))
        b.marker("light", tuple(Vector(b.world((0, 0, 0))) + Vector((math.cos(ang) * (TOP_R + 25.0),
                                                                          math.sin(ang) * (TOP_R + 25.0), H + 70.0))),
                 colour=[1.0, 0.12, 0.05], range=1500.0, blink=1.5)
    ring = [tuple(t) for t in tips] + [tuple(tips[0])]
    b.tube(ring, 0.9, mat=PAINT, tint=GREY, sides=8, cap=False)
    # Beacon dome and mast.
    b.lathe([(15.0, 0.0), (14.5, 6.0), (12.0, 13.0), (7.0, 19.0), (0.0, 21.0)], (0, 0, H + 3.0), mat=PANEL,
            tint=OFFWHITE, sides=40)
    b.cyl(15.3, 2.2, (0, 0, H + 7.0), mat=GLASS, sides=40, solid=False)
    b.cyl(3.0, 120.0, (0, 0, H + 24.0 + 60.0), mat=PAINT, tint=GREY, sides=16, radius2=1.1)
    for k, z in enumerate((H + 60.0, H + 95.0, H + 125.0)):
        span = 16.0 - 4.0 * k
        b.box((span * 2, 0.8, 0.8), (0, 0, z), mat=PAINT, tint=GREY, solid=False)
        b.box((0.8, span * 2, 0.8), (0, 0, z), mat=PAINT, tint=GREY, solid=False)
        for sx, sy in ((span, 0), (-span, 0), (0, span), (0, -span)):
            b.box((1.4, 1.4, 1.4), (sx, sy, z + 1.0), mat=LAMP, tint=srgb(1.0, 0.12, 0.05), solid=False, vary=0)
    b.box((3.4, 3.4, 3.4), (0, 0, H + 145.0), mat=LAMP, tint=srgb(1.0, 0.12, 0.05), solid=False)
    b.marker("light", (0, 0, H + 147.0), colour=[1.0, 0.12, 0.05], range=3000.0, blink=1.5)
    b.marker("landmark", (0, 0, H + 147.0), kind_of="atmosphere-processor", visible_km=60)
    b.marker("loot", (0, 18.0, H + 3.6), table="vault-crate")


def works_detail(b):
    """More of the works: halls and tank farms between the roads, pipe racks to the intake halls, lamp posts along
    the roads, gate posts, haulers, containers and debris, a control house on the apron."""
    R = 640.0
    for g in (0, 90, 180, 270):
        for side in (-1, 1):
            a = math.radians(g + side * 17.0)
            P.hall(b, (math.cos(a) * 470.0, math.sin(a) * 470.0, 0), g + 90.0, 70.0, 36.0, 20.0,
                   tint=(OFFWHITE, srgb(0.72, 0.74, 0.74))[side > 0], band=ORANGE, band2=TEAL, ruin=0.3,
                   pilaster=7.0, doors=[("front", -22.0)], bay_doors=[("front", 14.0, 10.0, 8.0)], louvres=2)
        ga = math.radians(g)
        for k in range(14):
            r = BASE_R + 110.0 + k * 34.0
            for s in (-1, 1):
                x = math.cos(ga) * r - math.sin(ga) * s * 9.0
                y = math.sin(ga) * r + math.cos(ga) * s * 9.0
                b.piece("lamp-post", (x, y, 0.1), (0, 0, g + (90 if s < 0 else -90)))
        with b.frame((math.cos(ga) * (R + 22.0), math.sin(ga) * (R + 22.0), 0), (0, 0, g + 90)):
            for x in (-12.0, -6.0, 6.0, 12.0):
                b.piece("jersey-barrier", (x + b.rng.uniform(-0.6, 0.6), b.rng.uniform(-2, 2), 0),
                        (0, 0, b.rng.uniform(-8, 8)))
            b.box((3.0, 3.0, 2.8), (-19.0, 8.0, 1.4), mat=PANEL, tint=OFFWHITE, bevel=0.05)
            b.box((3.1, 0.1, 1.0), (-19.0, 6.45, 1.9), mat=GLASS, solid=False, vary=0)
            P.sign_board(b, (24.0, -12.0, 0), 0.0, 10.0, 3.0)
    for i in range(4):
        a = math.radians(90 * i + 45)
        for side in (-1, 1):
            ta = a + side * math.radians(14.0)
            for k in range(3):
                r = 545.0 + k * 26.0
                tank(b, (math.cos(ta) * r, math.sin(ta) * r, 0), 10.0, 14.0, tint=OFFWHITE if k % 2 else LIGHTGREY,
                     band=ORANGE if side > 0 else TEAL, ladder_side=math.degrees(ta) + 180.0, roof="dome")
        # A pipe rack beside each pipe bridge, on its own trestles.
        p0 = Vector((math.cos(a) * 380.0, math.sin(a) * 380.0, 8.0))
        p1 = Vector((math.cos(a) * (BASE_R + 62.0), math.sin(a) * (BASE_R + 62.0), 8.0))
        off = Vector((-math.sin(a), math.cos(a), 0)) * 14.0
        for e in (-1.2, 0.0, 1.2):
            o = Vector((-math.sin(a), math.cos(a), 0)) * e
            b.tube([tuple(p0 + off + o), tuple(p1 + off + o)], 0.5, mat=PAINT, tint=(YELLOW, OFFWHITE, GREY)[int(e + 1.2) % 3],
                   sides=8)
        n = int((p0 - p1).length / 12.0)
        for k in range(n + 1):
            q = p0.lerp(p1, k / n) + off
            b.box((0.4, 4.0, 7.6), (q.x, q.y, 3.8), (0, 0, math.degrees(a)), mat=PAINT, tint=DARK, solid=False)
            b.box((0.6, 4.4, 0.4), (q.x, q.y, 7.4), (0, 0, math.degrees(a)), mat=PAINT, tint=DARK, solid=False)
    for k in range(14):
        a = b.rng.uniform(0, 2 * math.pi)
        r = b.rng.uniform(250.0, 600.0)
        roll = b.rng.random()
        x, y = math.cos(a) * r, math.sin(a) * r
        if roll < 0.35:
            b.piece("hauler-truck", (x, y, 0.1), (0, 0, b.rng.uniform(0, 360)))
        elif roll < 0.7:
            b.piece("container-20", (x, y, 0.0), (0, 0, b.rng.uniform(0, 360)),
                    tint=b.rng.choice((ORANGE, TEAL, OFFWHITE)))
        else:
            P.rubble_heap(b, (x, y, 0), b.rng.uniform(5, 10), b.rng.uniform(2, 4), count=8)
    # Apron control house.
    P.hall(b, (-480.0, -60.0, 0), 0.0, 24.0, 14.0, 8.0, tint=OFFWHITE, band=TEAL, ruin=0.2, pilaster=6.0,
           doors=[("front", 4.0)], roof_units=True)
    P.light_mast(b, (-430.0, -170.0, 0.0), 24.0)
    P.light_mast(b, (-530.0, -70.0, 0.0), 24.0)


def build(b):
    b.set_pad(("circle", 680.0), ground="concrete", blend=60.0, clear=20.0)
    # The shaft: one long lathe of hull plating, bands of the old livery every 120 m.
    prof = []
    for z in np.linspace(0, H, 40):
        prof.append((shaft_radius(z), z))
    prof.append((0.0, H))
    b.lathe([(BASE_R + 2.0, 0.0)] + prof, (0, 0, 0), mat=SHELL, tint=OFFWHITE, sides=96, solid="hull", wear=0.35)
    b.lathe([(BASE_R + 4.0, 0.0), (shaft_radius(70) + 3.0, 70.0), (shaft_radius(74), 74.0), (0.0, 74.0)], (0, 0, 0),
            mat=FORMWORK, tint=srgb(0.82, 0.81, 0.78), sides=96, solid=False, wear=0.5)
    for z in (255.0, 555.0):
        r = shaft_radius(z)
        b.cyl(r + 1.5, 26.0, (0, 0, z), mat=PAINT, tint=DARK, sides=96, radius2=shaft_radius(z + 26) + 1.5,
              solid=False, vary=0)
        for k in range(48):
            ang = 2 * math.pi * k / 48
            b.box((3.0, 1.0, 22.0), (math.cos(ang) * (r + 2.2), math.sin(ang) * (r + 2.2), z + 13.0),
                  (0, 0, math.degrees(ang) + 90), mat=PAINT, tint=GREY, solid=False, vary=0)
    for k, z in enumerate(np.arange(140.0, H - 40, 120.0)):
        r = shaft_radius(z)
        b.cyl(r + 0.6, 9.0, (0, 0, z), mat=PAINT, tint=ORANGE if k % 2 == 0 else TEAL, sides=96,
              radius2=shaft_radius(z + 9) + 0.6, solid=False, vary=0, wear=0.5)
    # Four buttresses: tapered fins from the skirt out to 230 m, each with a stair spine.
    for i in range(4):
        a = math.radians(45 + 90 * i)
        with b.frame((0, 0, 0), (0, 0, math.degrees(a))):
            outline = [(BASE_R - 10, 0), (235.0, 0), (235.0, 18.0), (BASE_R + 40, 210.0), (shaft_radius(330) - 4, 330.0),
                       (BASE_R - 10, 330.0)]
            b.extrude(outline, 22.0, (0, 0, 0), (0, 0, 0), mat=FORMWORK, tint=CONCRETE_T, solid="hull")
            # Intake hall between buttresses (placed by the next loop), louvres on the buttress flanks.
            for k in range(8):
                z = 24.0 + k * 22.0
                x = 235.0 - (235.0 - BASE_R - 40) * (z - 18) / 192.0 - 6
                b.box((6.0, 23.0, 1.2), (x - 4, 0, z), mat=PAINT, tint=DARK, solid=False)
            b.box((12.0, 26.0, 20.0), (238.0, 0, 10.0), mat=CONCRETE, tint=CONCRETE_T)
            buttress_detail(b, outline)
            for z in (12.0, 16.0):
                b.box((0.6, 6.0, 0.6), (244.3, 0, z), mat=LAMP, tint=srgb(1.0, 0.85, 0.6), solid=False)
    # Intake halls: four big louvred blocks between the buttresses at the foot.
    for i in range(4):
        a = math.radians(90 * i)
        with b.frame((math.cos(a) * (BASE_R + 52), math.sin(a) * (BASE_R + 52), 0), (0, 0, math.degrees(a) + 90)):
            b.box((120.0, 60.0, 46.0), (0, 0, 23.0), mat=PANEL, tint=LIGHTGREY)
            for k in range(10):
                x = -54 + k * 12
                b.box((9.0, 2.0, 30.0), (x, -31.0, 22.0), mat=PAINT, tint=DARK, solid=False)
                for z in range(6):
                    b.box((9.0, 2.6, 0.5), (x, -32.2, 9.0 + z * 5.0), (40, 0, 0), mat=PAINT, tint=GREY, solid=False)
            b.box((124.0, 64.0, 3.0), (0, 0, 47.5), mat=CONCRETE, tint=CONCRETE_T)
            for x in (-40.0, 0.0, 40.0):
                b.cyl(9.0, 14.0, (x, 8.0, 55.0), mat=PAINT, tint=OFFWHITE, sides=32)
                b.cyl(10.0, 1.2, (x, 8.0, 62.6), mat=GRATE, tint=GREY, sides=32, solid=False)
            stripes(b, (-60, -32.1, 0), (60, -32.1, 0), 3.0, width=1.6, at_z=0.0)
            b.marker("door", (0, -33.0, 0), kind_of="intake-hall")
    # Rings of galleries up the shaft: the climb's rest stops, each a grated deck with a rail and a lamp ring.
    for z in (180.0, 330.0, 480.0, 630.0, 780.0):
        r = shaft_radius(z)
        b.cyl(r + 7.0, 1.2, (0, 0, z), mat=GRATE, tint=GREY, sides=64, solid="hull")
        b.cyl(r + 7.2, 1.0, (0, 0, z + 1.1), mat=PAINT, tint=YELLOW, sides=64, solid=False, vary=0)
        for k in range(24):
            ang = 2 * math.pi * k / 24
            b.box((0.6, 0.6, 7.5), (math.cos(ang) * (r + 3.5), math.sin(ang) * (r + 3.5), z - 3.6),
                  (0, 0, math.degrees(ang)), mat=PAINT, tint=DARK, solid=False)
        b.marker("loot", (r + 4.0, 0, z + 0.6), table="military-crate")
    # An elevator spine on the north face: a lattice mast hugging the shaft up to the top gallery.
    spine_r = BASE_R + 14
    for k in range(0, 13):
        z0 = k * 65.0
        z1 = min(H - 60, z0 + 65.0)
        r0, r1 = shaft_radius(z0) + 8, shaft_radius(z1) + 8
        lattice_tower(b, (0, r0, z0), z1 - z0, 9.0 * (r0 / spine_r), 9.0 * (r1 / spine_r), rot=(0, 0, 0),
                      sections=6, member=0.5, tint=srgb(0.55, 0.57, 0.58))
    shaft_detail(b)
    crown(b)

    # The works: a perimeter wall 1.3 km across with four gates, roads to the halls, processing sheds, tanks.
    R = 640.0
    gates = [math.radians(a) for a in (0, 90, 180, 270)]
    n = 48
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        mid = (a0 + a1) / 2
        if any(abs(math.atan2(math.sin(mid - g), math.cos(mid - g))) < 0.05 for g in gates):
            continue
        wall(b, (math.cos(a0) * R, math.sin(a0) * R, 0), (math.cos(a1) * R, math.sin(a1) * R, 0), height=6.0,
             thick=0.8, panel=12.0, broken=0.12)
    for g in gates:
        with b.frame((math.cos(g) * R, math.sin(g) * R, 0), (0, 0, math.degrees(g) + 90)):
            for x in (-18.0, 18.0):
                b.box((8.0, 10.0, 14.0), (x, 0, 7.0), mat=FORMWORK, tint=CONCRETE_T)
            b.box((44.0, 8.0, 3.0), (0, 0, 15.5), mat=CONCRETE, tint=CONCRETE_T)
            stripes(b, (-14, -5.1, 0), (14, -5.1, 0), 1.2, width=1.0, at_z=13.6)
        road(b, [(math.cos(g) * (R + 60), math.sin(g) * (R + 60), 0), (math.cos(g) * (BASE_R + 90), math.sin(g) * (BASE_R + 90), 0)],
             width=12.0)
    for i in range(4):
        a = math.radians(90 * i + 45)
        cx, cy = math.cos(a) * 420, math.sin(a) * 420
        with b.frame((cx, cy, 0), (0, 0, math.degrees(a) + 90)):
            shed(b, (0, 0, 0), (0, 0, 0), 60.0, 30.0, 14.0, doors=[("front", -15, 10, 9), ("front", 15, 10, 9)],
                 tint=srgb(0.72, 0.74, 0.74), ruin=0.12)
            silo(b, (0, 40, 0), 5.0, 22.0, cone=5.0, legs=6.0, count=3, spacing=13.0)
            conveyor(b, (0, 30.0, 32.0), (0, 18.0, 16.0), 2.0)
            b.marker("loot", (-20, 0, 0.3), table="crate")
            b.marker("loot", (20, 5, 0.3), table="barrel")
            b.marker("guard", (0, -25, 0), role="warden-patrol")
    # Pipe bridges from the halls to the shaft, on trestles.
    for i in range(4):
        a = math.radians(90 * i + 45)
        p0 = (math.cos(a) * 395, math.sin(a) * 395, 18.0)
        p1 = (math.cos(a) * (BASE_R + 60), math.sin(a) * (BASE_R + 60), 30.0)
        for off in (-3.0, 0.0, 3.0):
            o = (-math.sin(a) * off, math.cos(a) * off)
            b.tube([(p0[0] + o[0], p0[1] + o[1], p0[2]), (p1[0] + o[0], p1[1] + o[1], p1[2])], 1.2, mat=PAINT,
                   tint=OFFWHITE if off else ORANGE, sides=16)
        for k in range(1, 9):
            t = k / 9
            x, y = p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t
            z = p0[2] + (p1[2] - p0[2]) * t
            b.box((2.0, 9.0, z - 1.2), (x, y, (z - 1.2) / 2), (0, 0, math.degrees(a)), mat=CONCRETE, tint=CONCRETE_T)
    # Landing apron and a tall vent stack by the west gate.
    b.box((90.0, 90.0, 0.4), (-480.0, -120.0, 0.2), mat="M_Mon_Asphalt", tint=(1, 1, 1))
    for x, y in ((-500.0, -140.0), (-460.0, -100.0)):
        b.cyl(9.0, 0.06, (x, y, 0.43), mat=PAINT, tint=YELLOW, sides=48, solid=False, vary=0)
    chimney(b, (300.0, -330.0, 0), 140.0, 7.0, 4.5)
    works_detail(b)
    b.zone("radiation", (0, 0, 120.0), radius=150.0, level=4)
    b.zone("no-build", (0, 0, 0), radius=760.0)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 60})


SUN = (22.0, 200.0)
FOG = 0.00022
GROUND = 40000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (0, 0, 380), "distance": 3200, "yaw": 30, "pitch": 4, "lens": 35},
    {"name": "works", "target": (0, 0, 120), "distance": 1100, "yaw": 50, "pitch": 14, "lens": 30},
    {"name": "foot", "target": (0, 0, 220), "distance": 520, "yaw": 15, "pitch": -2, "lens": 22,
     "people": [((0, -470, 0), 0)], "ship": ((40, -480, 0), 30)},
]
