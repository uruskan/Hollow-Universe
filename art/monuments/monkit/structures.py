"""Large parametric structures for monuments (MONUMENTS.md §8.4): steel sheds and hangars, quonset huts, domes,
dishes, cooling towers, chimneys, silos, cranes, conveyors, rail track, roads, walls, watchtowers, hab modules,
ship hulls and rockets. Each adds primitives to a builder in its current frame; positions in metres, angles in
degrees, the structure's front toward -Y unless said otherwise.
"""
import math

import numpy as np
from mathutils import Vector

from .kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, TEAL, YELLOW, WARM_LAMP, cable, ladder,
                  lamp, railing, srgb, walkway)
from .surfaces import (ASPHALT, CHAINLINK, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, HULL, LAMP, MATTE,
                       PAINT, PANEL, RUST)


# ------------------------------------------------------------------------------------------------ buildings


def shed(b, at, rot, w, d, h, roof="gable", pitch=12.0, tint=LIGHTGREY, frame_tint=DARK, cladding=CORRUGATED,
         doors=(), open_sides=(), skylights=True, ruin=0.0):
    """A steel-framed shed (workshop, warehouse, hangar): columns every 6 m, corrugated cladding, a gable or mono
    roof. `doors`: list of (side, x offset, width, height) with side "front"/"back"/"left"/"right"; `open_sides`:
    sides left unclad; `ruin` 0..1 leaves cladding panels missing."""
    t = 0.12
    bays = max(1, int(round(w / 6.0)))
    with b.frame(at, rot):
        b.box((w + 0.6, d + 0.6, 0.25), (0, 0, 0.125), mat=CONCRETE, tint=CONCRETE_T)
        rise = math.tan(math.radians(pitch)) * (d / 2 if roof == "gable" else d)
        for i in range(bays + 1):
            x = -w / 2 + w * i / bays
            for y in (-d / 2, d / 2):
                b.box((0.3, 0.3, h), (x, y, h / 2 + 0.25), mat=PAINT, tint=frame_tint)
            if roof == "gable":
                b.beam((x, -d / 2, h + 0.25), (x, 0, h + 0.25 + rise), 0.25, 0.35, mat=PAINT, tint=frame_tint)
                b.beam((x, d / 2, h + 0.25), (x, 0, h + 0.25 + rise), 0.25, 0.35, mat=PAINT, tint=frame_tint)
            else:
                b.beam((x, -d / 2, h + 0.25), (x, d / 2, h + 0.25 + rise), 0.25, 0.35, mat=PAINT, tint=frame_tint)

        def opening(side, x0, x1, z1):
            for s, ox, ow, oh in doors:
                if s == side:
                    lo, hi = ox - ow / 2, ox + ow / 2
                    if x1 > lo and x0 < hi and z1 > 0.25:
                        return True
            return False

        def clad_wall(side, length, place):
            n = max(1, int(round(length / 1.0)))
            for k in range(n):
                x0 = -length / 2 + length * k / n
                x1 = x0 + length / n
                if side in open_sides:
                    continue
                if b.rng.random() < ruin * 0.6:
                    continue
                for z0, z1 in ((0.25, h * 0.5 + 0.25), (h * 0.5 + 0.25, h + 0.25)):
                    if opening(side, x0, x1, z1) and z0 < max(oh for s, _, _, oh in doors if s == side) + 0.25:
                        continue
                    place((x0 + x1) / 2, x1 - x0, (z0 + z1) / 2, z1 - z0)

        clad_wall("front", w, lambda x, l, z, hh: b.box((l, t, hh), (x, -d / 2 - 0.15, z), mat=cladding, tint=tint,
                                                         solid=True))
        clad_wall("back", w, lambda x, l, z, hh: b.box((l, t, hh), (-x, d / 2 + 0.15, z), mat=cladding, tint=tint))
        clad_wall("left", d, lambda x, l, z, hh: b.box((t, l, hh), (-w / 2 - 0.15, x, z), mat=cladding, tint=tint))
        clad_wall("right", d, lambda x, l, z, hh: b.box((t, l, hh), (w / 2 + 0.15, -x, z), mat=cladding, tint=tint))
        # Gable ends above the eaves.
        if roof == "gable":
            for y in (-d / 2 - 0.15, d / 2 + 0.15):
                if (y < 0 and "front" in open_sides) or (y > 0 and "back" in open_sides):
                    continue
                # A lone face keeps its winding: the front gable faces -Y, the back one must face +Y.
                b.mesh([(-w / 2 - 0.15, y, h + 0.25), (w / 2 + 0.15, y, h + 0.25), (0, y, h + 0.25 + rise)],
                       [[0, 1, 2] if y < 0 else [0, 2, 1]], mat=cladding, tint=tint, solid="hull")
        # Roof sheets in strips (some missing when ruined), skylights.
        strips = max(2, int(round(w / 1.5)))
        for k in range(strips):
            x = -w / 2 - 0.3 + (w + 0.6) * (k + 0.5) / strips
            sw = (w + 0.6) / strips + 0.02
            if b.rng.random() < ruin:
                continue
            light = skylights and k % 5 == 2
            mat = GLASS if light else cladding
            if roof == "gable":
                half = math.hypot(d / 2 + 0.4, rise)
                ang = math.degrees(math.atan2(rise, d / 2))
                b.box((sw, half, 0.06), (x, -(d / 2 + 0.4) / 2, h + 0.3 + rise / 2), (ang, 0, 0), mat=mat, tint=tint)
                b.box((sw, half, 0.06), (x, (d / 2 + 0.4) / 2, h + 0.3 + rise / 2), (-ang, 0, 0), mat=mat, tint=tint)
            else:
                full = math.hypot(d + 0.8, rise)
                ang = math.degrees(math.atan2(rise, d))
                b.box((sw, full, 0.06), (x, 0, h + 0.3 + rise / 2), (-ang, 0, 0), mat=mat, tint=tint)
        if roof == "gable":
            b.box((w + 0.8, 0.4, 0.2), (0, 0, h + 0.35 + rise), mat=PAINT, tint=frame_tint, solid=False)
        # Doors: roller shutters, half open.
        for side, ox, ow, oh in doors:
            ys = {"front": -d / 2 - 0.2, "back": d / 2 + 0.2}
            if side in ys:
                y = ys[side]
                xx = ox if side == "front" else -ox
                b.box((ow + 0.4, 0.25, 0.4), (xx, y, oh + 0.45), mat=PAINT, tint=frame_tint, solid=False)
                open_h = oh * (0.3 + 0.5 * b.rng.random())
                b.box((ow, 0.08, oh - open_h), (xx, y, open_h + (oh - open_h) / 2 + 0.25), mat=CORRUGATED,
                      tint=srgb(0.55, 0.57, 0.6), solid=True)
                b.marker("door", (xx, y - 1.0, 0.25), kind_of="shutter")


def quonset(b, at, rot, w, length, tint=srgb(0.55, 0.58, 0.48), door=True):
    """A half-round hut of corrugated arches on a slab, end walls with a door."""
    r = w / 2
    with b.frame(at, rot):
        b.box((w + 0.4, length + 0.4, 0.2), (0, 0, 0.1), mat=CONCRETE, tint=CONCRETE_T)
        prof = []
        n = 14
        for i in range(n + 1):
            a = math.pi * i / n
            prof.append((r * math.cos(a), r * math.sin(a) + 0.2))
        # The shell as an extruded thin arch.
        outer = [(x, z) for x, z in prof]
        inner = [(x * 0.97, (z - 0.2) * 0.97 + 0.2) for x, z in reversed(prof)]
        b.extrude(outer + inner, length, (0, 0, 0), (0, 0, 90), mat=CORRUGATED, tint=tint, solid="hull")
        for y in (-length / 2, length / 2):
            b.cyl(r, 0.15, (0, y, 0.2), (90, 0, 0), mat=PAINT, tint=srgb(0.5, 0.52, 0.45), sides=28, radius2=r,
                  cap=True, solid=False)
        if door:
            b.box((1.6, 0.2, 2.2), (0, -length / 2 - 0.1, 1.3), mat=PAINT, tint=DARK, solid=False)
            b.marker("door", (0, -length / 2 - 0.8, 0.2), kind_of="hut")
        b.collide_box(Vector((0, 0, r * 0.5 + 0.2)), (w * 0.9, length, r), (0, 0, 0))


def hab_module(b, at, rot, length=8.0, radius=1.6, tint=OFFWHITE, band=ORANGE, windows=True, legs=0.6):
    """A colonist's prefab hab: a horizontal can on short legs with windows, a hatch and a band of livery."""
    with b.frame(at, rot):
        z = legs + radius
        b.cyl(radius, length, (0, 0, z), (0, 90, 0), mat=PANEL, tint=tint, sides=24)
        for x in (-length / 2, length / 2):
            b.lathe([(radius, 0), (radius * 0.92, 0.25), (radius * 0.6, 0.4), (0, 0.45)],
                    (x, 0, z), (0, 90 if x > 0 else -90, 0), mat=PANEL, tint=tint, sides=24)
        b.cyl(radius + 0.01, 0.35, (length * 0.25, 0, z), (0, 90, 0), mat=PAINT, tint=band, sides=24, solid=False)
        for x in (-length / 2 + 1.0, length / 2 - 1.0):
            for y in (-radius * 0.6, radius * 0.6):
                b.box((0.2, 0.2, legs + 0.4), (x, y, (legs + 0.4) / 2), mat=PAINT, tint=DARK, solid=False)
        if windows:
            for x in np.linspace(-length / 2 + 1.6, length / 2 - 1.6, max(1, int(length / 2.5))):
                b.box((0.7, 0.1, 0.45), (x, -radius + 0.03, z + 0.35), mat=GLASS, solid=False)
        b.box((0.9, 0.2, 1.7), (-length / 2 + 0.25, -radius + 0.5, legs + 0.9), (0, 0, -90), mat=PAINT, tint=DARK,
              solid=False)
        b.marker("door", (-length / 2 - 0.7, 0, 0), kind_of="hatch")


def watchtower(b, at, rot, height=7.0, tint=srgb(0.45, 0.47, 0.40)):
    """A steel watchtower: four legs, a cab with a roof and windows, a ladder."""
    with b.frame(at, rot):
        for x in (-1.2, 1.2):
            for y in (-1.2, 1.2):
                b.beam((x * 1.3, y * 1.3, 0), (x, y, height), 0.2, mat=PAINT, tint=tint, solid=True)
        for z in (height * 0.35, height * 0.7):
            k = 1.0 + 0.3 * (1 - z / height)
            for (x0, y0), (x1, y1) in (((-1.2, -1.2), (1.2, -1.2)), ((1.2, -1.2), (1.2, 1.2)), ((1.2, 1.2), (-1.2, 1.2)),
                                       ((-1.2, 1.2), (-1.2, -1.2))):
                b.beam((x0 * k, y0 * k, z), (x1 * k, y1 * k, z), 0.1, mat=PAINT, tint=tint)
        b.box((3.0, 3.0, 0.15), (0, 0, height), mat=PAINT, tint=tint)
        for (x, y, sx, sy) in ((0, -1.45, 3.0, 0.1), (0, 1.45, 3.0, 0.1), (-1.45, 0, 0.1, 3.0), (1.45, 0, 0.1, 3.0)):
            b.box((sx, sy, 1.0), (x, y, height + 0.55), mat=PAINT, tint=tint)
        for x, y in ((-1.45, -1.45), (1.45, -1.45), (1.45, 1.45), (-1.45, 1.45)):
            b.box((0.12, 0.12, 2.3), (x, y, height + 1.2), mat=PAINT, tint=tint, solid=False)
        b.box((3.4, 3.4, 0.15), (0, 0, height + 2.4), mat=CORRUGATED, tint=tint)
        lamp(b, (0, -1.6, height + 2.2), (-20, 0, 0), size=(0.4, 0.3, 0.3))
        ladder(b, (1.0, 1.6, 0), height, (0, 0, 180), cage=False)
        b.marker("guard", (0, 0, height + 0.1), role="sentry")


def wall(b, a, c, height=3.0, thick=0.4, mat=FORMWORK, tint=CONCRETE_T, panel=4.0, broken=0.0, cap=True):
    """A perimeter wall of precast panels from `a` to `c`; `broken` leaves panels fallen or missing."""
    a, c = Vector(a), Vector(c)
    d = c - a
    n = max(1, int(round(d.length / panel)))
    yaw = math.degrees(math.atan2(d.y, d.x))
    for i in range(n):
        p = a + d * ((i + 0.5) / n)
        roll = b.rng.random()
        if roll < broken * 0.5:
            continue
        if roll < broken:
            b.box((d.length / n - 0.05, height, thick), (p.x, p.y, thick / 2 + 0.05), (0, 0, yaw + b.rng.uniform(-8, 8)),
                  mat=mat, tint=tint)
            continue
        b.box((d.length / n - 0.05, thick, height), (p.x, p.y, height / 2), (0, 0, yaw), mat=mat, tint=tint)
        if cap:
            b.box((d.length / n - 0.05, thick + 0.1, 0.12), (p.x, p.y, height + 0.06), (0, 0, yaw), mat=CONCRETE,
                  tint=tint, solid=False)


def sandbags(b, a, c, height=3, tint=srgb(0.55, 0.50, 0.38)):
    """A sandbag wall `height` bags high from `a` to `c`."""
    a, c = Vector(a), Vector(c)
    d = c - a
    n = max(1, int(d.length / 0.62))
    yaw = math.degrees(math.atan2(d.y, d.x))
    for row in range(height):
        off = 0.31 if row % 2 else 0.0
        for i in range(n):
            t = (i * 0.62 + off + 0.31) / d.length
            if t > 1:
                continue
            p = a + d * t
            b.box((0.6, 0.38, 0.17), (p.x, p.y, 0.09 + row * 0.16), (0, 0, yaw + b.rng.uniform(-6, 6)), mat=MATTE,
                  tint=tint, bevel=0.06, solid=False, vary=0.08, wear=0.6)
    mid = (a + c) / 2
    b.collide_box(mid + Vector((0, 0, height * 0.08)), (d.length, 0.4, height * 0.16), (0, 0, yaw))


# ------------------------------------------------------------------------------------------------ towers and tanks


def cooling_tower(b, at, height=90.0, base=38.0, waist=24.0, top=27.0, tint=CONCRETE_T, legs=True, stain=True):
    """A hyperboloid cooling tower: a thin concrete shell (open at the top) on diagonal legs, a stained rim."""
    x, y, z = at
    leg_h = height * 0.08
    n = 18
    prof_out, prof_in = [], []
    for i in range(n + 1):
        t = i / n
        # Radius along the height: a hyperbola through base, waist (at 70%) and top.
        zz = leg_h + (height - leg_h) * t
        tw = 0.72
        if t < tw:
            k = t / tw
            r = waist + (base - waist) * (1 - k) ** 2
        else:
            k = (t - tw) / (1 - tw)
            r = waist + (top - waist) * k ** 2
        prof_out.append((r, zz))
        prof_in.append((r - 0.4, zz))
    shell = prof_out + list(reversed(prof_in))
    b.lathe(shell, (x, y, z), mat=CONCRETE, tint=tint, sides=56, close=False,
            solid=False)
    # Collision: rings of boxes would be heavy; a few hulls of the lower shell keep players out of the walls.
    if legs:
        m = 36
        for i in range(m):
            a = 2 * math.pi * i / m
            a2 = a + math.pi / m * 2
            r0 = base
            p0 = (x + math.cos(a) * r0, y + math.sin(a) * r0, z)
            p1 = (x + math.cos(a2) * (r0 - 0.5), y + math.sin(a2) * (r0 - 0.5), z + leg_h + 0.2)
            b.beam(p0, p1, 0.5, 0.5, mat=CONCRETE, tint=tint, solid=True)
            p2 = (x + math.cos(a2) * r0, y + math.sin(a2) * r0, z)
            b.beam(p2, (x + math.cos(a) * (r0 - 0.5), y + math.sin(a) * (r0 - 0.5), z + leg_h + 0.2), 0.5, 0.5,
                   mat=CONCRETE, tint=tint, solid=True)
    b.cyl(base - 1.0, 0.6, (x, y, z + 0.3), mat=CONCRETE, tint=srgb(0.5, 0.52, 0.5), sides=56, solid=True)
    b.marker("landmark", (x, y, z + height), kind_of="cooling-tower")


def chimney(b, at, height=60.0, r_base=3.0, r_top=2.0, tint=CONCRETE_T, bands=(srgb(0.75, 0.15, 0.1), OFFWHITE),
            ladder_on=True):
    """A tapered stack with painted bands near the top, a platform and a lamp."""
    x, y, z = at
    b.cyl(r_base, height, (x, y, z + height / 2), mat=CONCRETE, tint=tint, sides=36, radius2=r_top)
    for k in range(4):
        zz = z + height - 2.0 - k * 4.0
        t = (zz - z) / height
        rr = r_base + (r_top - r_base) * t + 0.03
        b.cyl(rr, 4.0, (x, y, zz - 2.0 + 2.0), mat=PAINT, tint=bands[k % 2], sides=36, radius2=rr - (r_base - r_top) * 4 / height,
              solid=False, vary=0)
    b.cyl(r_top + 0.9, 0.2, (x, y, z + height - 6.0), mat=GRATE, tint=GREY, sides=36, solid=False)
    pts = [(x + math.cos(a) * (r_top + 0.85), y + math.sin(a) * (r_top + 0.85), z + height - 5.9)
           for a in np.linspace(0, 2 * math.pi, 17)]
    for p, q in zip(pts, pts[1:]):
        railing(b, p, q, 1.0, spacing=3.0, toe=False)
    b.cyl(r_top + 0.05, 0.6, (x, y, z + height + 0.3), mat=RUST, tint=(1, 1, 1), sides=36, solid=False)
    b.box((0.4, 0.4, 0.3), (x, y + r_top, z + height + 0.6), mat=LAMP, tint=srgb(1.0, 0.1, 0.05), solid=False)
    b.marker("light", (x, y + r_top, z + height + 0.8), colour=[1.0, 0.1, 0.05], range=60.0, blink=1.0)
    if ladder_on:
        ladder(b, (x, y - r_base - 0.3, z), height - 6.0, (0, 0, 0), cage=True)
    b.marker("landmark", (x, y, z + height), kind_of="chimney")


def silo(b, at, radius=3.0, height=12.0, cone=3.0, legs=4.0, tint=LIGHTGREY, count=1, spacing=None):
    """Silos on legs with conical hoppers; `count` in a row along X."""
    spacing = spacing or radius * 2.3
    for i in range(count):
        x = at[0] + (i - (count - 1) / 2) * spacing
        y, z = at[1], at[2]
        for k in range(4):
            a = math.radians(45 + 90 * k)
            b.box((0.35, 0.35, legs + cone), (x + math.cos(a) * radius * 0.85, y + math.sin(a) * radius * 0.85,
                                              z + (legs + cone) / 2), mat=PAINT, tint=DARK)
        b.cyl(radius, cone, (x, y, z + legs + cone / 2), mat=PAINT, tint=tint, sides=32, radius2=radius,
              solid=False)
        b.cyl(0.4, cone, (x, y, z + legs + cone / 2), mat=PAINT, tint=tint, sides=16, radius2=radius, solid="hull")
        b.cyl(radius, height, (x, y, z + legs + cone + height / 2), mat=CORRUGATED, tint=tint, sides=32)
        b.cyl(radius + 0.05, radius * 0.3, (x, y, z + legs + cone + height + radius * 0.15), mat=PAINT, tint=tint,
              sides=32, radius2=0.4, solid="hull")
    if count > 1:
        top = at[2] + legs + cone + height + radius * 0.3
        walkway(b, (at[0] - (count - 1) / 2 * spacing, at[1], top + 0.2), (at[0] + (count - 1) / 2 * spacing, at[1],
                                                                           top + 0.2), 1.0, legs=False)


def dish(b, at, diameter=24.0, elevation=40.0, azimuth=0.0, tint=OFFWHITE, mount_h=None, ribs=16, panels_missing=0.1):
    """A parabolic dish on an alt-azimuth mount: a pedestal, a yoke, the dish of radial panels with ribs behind, a
    feed on a tripod at its focus."""
    r = diameter / 2
    mount_h = mount_h or r * 0.9
    depth = r * 0.28
    with b.frame(at, (0, 0, azimuth)):
        b.cyl(r * 0.18, mount_h, (0, 0, mount_h / 2), mat=CONCRETE, tint=CONCRETE_T, sides=24, radius2=r * 0.12)
        b.cyl(r * 0.2, 1.0, (0, 0, mount_h + 0.5), mat=PAINT, tint=GREY, sides=24)
        for s in (-1, 1):
            b.box((0.8, r * 0.12, r * 0.45), (s * r * 0.32, 0, mount_h + 1.0 + r * 0.2), mat=PAINT, tint=GREY)
        with b.frame((0, 0, mount_h + 1.0 + r * 0.4), (0, 0, 0)):
            with b.frame((0, 0, 0), (90 - elevation, 0, 0)):
                # The reflector: rings of panels, paraboloid z = depth * (rho/r)^2, opening toward local +Z.
                rings = 6
                sectors = 32
                for i in range(rings):
                    r0, r1 = r * i / rings, r * (i + 1) / rings
                    for j in range(sectors):
                        if i > 1 and b.rng.random() < panels_missing:
                            continue
                        a0 = 2 * math.pi * j / sectors
                        a1 = 2 * math.pi * (j + 1) / sectors
                        pts = []
                        for rr, aa in ((r0, a0), (r1, a0), (r1, a1), (r0, a1)):
                            pts.append((rr * math.cos(aa), rr * math.sin(aa), depth * (rr / r) ** 2))
                        back = [(p[0], p[1], p[2] - 0.08) for p in pts]
                        b.mesh(pts + back, [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3],
                                            [3, 7, 4, 0]], mat=PANEL, tint=tint, solid=False, vary=0.02, smooth=False)
                for j in range(ribs):
                    a = 2 * math.pi * j / ribs
                    pts = [(rr * math.cos(a), rr * math.sin(a), depth * (rr / r) ** 2 - 0.3)
                           for rr in np.linspace(r * 0.12, r, 6)]
                    b.tube(pts, 0.12, mat=PAINT, tint=GREY, sides=6)
                b.cyl(r * 0.14, 1.2, (0, 0, -0.4), mat=PAINT, tint=GREY, sides=20)
                focus = r * r / (4 * depth)
                for j in range(3):
                    a = 2 * math.pi * j / 3
                    b.beam((r * 0.7 * math.cos(a), r * 0.7 * math.sin(a), depth * 0.49), (0, 0, focus - 0.6), 0.15,
                           mat=PAINT, tint=GREY)
                b.cyl(0.8, 1.6, (0, 0, focus), mat=PAINT, tint=OFFWHITE, sides=16)
        b.collide_box(Vector((0, 0, mount_h / 2)), (r * 0.36, r * 0.36, mount_h), (0, 0, 0))
    b.marker("landmark", (at[0], at[1], at[2] + mount_h + r), kind_of="dish")


def crane_tower(b, at, height=40.0, jib=35.0, counter=12.0, yaw=0.0, tint=YELLOW):
    """A tower crane: a lattice mast, a slewing cab, the jib and counter-jib with a counterweight, a hook."""
    from .kit import lattice_tower
    lattice_tower(b, at, height, 2.0, 2.0, sections=max(4, int(height / 4)), member=0.14, tint=tint)
    x, y, z = at
    with b.frame((x, y, z + height), (0, 0, yaw)):
        b.box((2.4, 2.4, 1.0), (0, 0, 0.5), mat=PAINT, tint=tint)
        b.box((1.8, 1.6, 1.8), (1.3, -1.6, 1.2), mat=PAINT, tint=tint, bevel=0.05)
        b.box((1.6, 0.05, 0.9), (1.3, -2.42, 1.4), mat=GLASS, solid=False)
        for side in (-0.6, 0.6):
            b.beam((0, side, 1.0), (jib, side * 0.4, 1.0), 0.15, mat=PAINT, tint=tint)
            b.beam((0, side, 1.0), (-counter, side, 1.0), 0.15, mat=PAINT, tint=tint)
        b.beam((0, 0, 2.6), (jib, 0, 1.6), 0.12, mat=PAINT, tint=tint)
        for k in range(int(jib / 2)):
            xx = 2 * k
            b.beam((xx, -0.6 + 0.4 * xx / jib, 1.0), (xx + 1, 0, 1.6 + (jib - xx) / jib), 0.07, mat=PAINT, tint=tint)
            b.beam((xx, 0.6 - 0.4 * xx / jib, 1.0), (xx + 1, 0, 1.6 + (jib - xx) / jib), 0.07, mat=PAINT, tint=tint)
        b.box((0.4, 0.4, 3.5), (0, 0, 2.7), mat=PAINT, tint=tint, solid=False)
        cable(b, (0, 0, 4.4), (jib, 0, 1.6), sag=0.0, radius=0.03)
        cable(b, (0, 0, 4.4), (-counter, 0, 1.4), sag=0.0, radius=0.03)
        b.box((3.0, 2.2, 2.0), (-counter + 1.5, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        hx = jib * (0.4 + 0.5 * b.rng.random())
        hz = -height * (0.3 + 0.5 * b.rng.random())
        b.box((1.0, 0.6, 0.3), (hx, 0, 0.9), mat=PAINT, tint=DARK, solid=False)
        cable(b, (hx, 0, 0.8), (hx, 0, hz + 1.0), sag=0.0, radius=0.025)
        b.box((0.6, 0.5, 0.8), (hx, 0, hz + 0.6), mat=PAINT, tint=YELLOW, solid=False)
    b.marker("landmark", (x, y, z + height), kind_of="crane")


def gantry_crane(b, at, rot, span=30.0, height=20.0, length=12.0, tint=srgb(0.80, 0.30, 0.12)):
    """A portal crane straddling rails: two A-frame legs each side, a box girder, a trolley."""
    with b.frame(at, rot):
        for x in (-span / 2, span / 2):
            for y in (-length / 2, length / 2):
                b.beam((x, y, 0.5), (x, y * 0.3, height), 0.8, 0.8, mat=PAINT, tint=tint, solid=True)
            b.box((1.4, length + 1.0, 1.0), (x, 0, 0.5), mat=PAINT, tint=DARK)
            for y in (-length / 2 + 0.6, length / 2 - 0.6):
                b.cyl(0.4, 0.4, (x, y, 0.4), (0, 90, 0), mat=PAINT, tint=BLACK, sides=12, solid=False)
        for y in (-1.2, 1.2):
            b.box((span + 4.0, 1.4, 2.2), (0, y, height + 1.1), mat=PAINT, tint=tint, bevel=0.04)
        tx = span * (b.rng.random() - 0.5) * 0.7
        b.box((4.0, 3.6, 2.4), (tx, 0, height + 3.4), mat=PAINT, tint=tint, bevel=0.05)
        b.box((1.6, 1.4, 1.6), (tx + 2.6, -2.2, height - 0.5), mat=PAINT, tint=OFFWHITE, bevel=0.04)
        b.box((1.4, 0.05, 0.8), (tx + 2.6, -2.93, height - 0.3), mat=GLASS, solid=False)
        hz = height * (0.25 + 0.5 * b.rng.random())
        for dy in (-0.4, 0.4):
            cable(b, (tx, dy, height + 2.2), (tx, dy, hz + 1.2), sag=0.0, radius=0.03)
        b.box((3.0, 2.0, 0.8), (tx, 0, hz + 0.8), mat=PAINT, tint=YELLOW, solid=False)
        ladder(b, (span / 2 + 0.9, 0, 0), height, (0, 0, 90), cage=True)
    b.marker("landmark", (at[0], at[1], at[2] + height), kind_of="gantry-crane")


def conveyor(b, a, c, width=1.4, tint=srgb(0.55, 0.57, 0.52), covered=True):
    """A belt conveyor from `a` to `c` on trestles, with a hood."""
    a, c = Vector(a), Vector(c)
    d = c - a
    yaw = math.degrees(math.atan2(d.y, d.x))
    pitch = math.degrees(math.atan2(d.z, math.hypot(d.x, d.y)))
    mid = (a + c) / 2
    length = d.length
    b.box((length, width, 0.5), (mid.x, mid.y, mid.z), (0, -pitch, yaw), mat=PAINT, tint=tint)
    if covered:
        b.box((length, width + 0.3, 0.06), (mid.x, mid.y, mid.z + 0.9), (0, -pitch, yaw), mat=CORRUGATED,
              tint=tint, solid=False)
        for k in range(int(length / 3)):
            p = a + d * ((k + 0.5) / int(length / 3))
            for s in (-1, 1):
                off = Vector((-d.y, d.x, 0)).normalized() * (width / 2 + 0.1) * s
                b.box((0.08, 0.08, 0.9), (p.x + off.x, p.y + off.y, p.z + 0.45), mat=PAINT, tint=tint, solid=False)
    n = max(1, int(length / 8))
    for k in range(n + 1):
        p = a + d * (k / n)
        if p.z > 1.0:
            for s in (-1, 1):
                off = Vector((-d.y, d.x, 0)).normalized() * (width / 2) * s
                b.box((0.25, 0.25, p.z - 0.25), (p.x + off.x, p.y + off.y, (p.z - 0.25) / 2), mat=PAINT, tint=DARK)
    walkway(b, a + Vector((0, 0, 0.3)) + Vector((-d.y, d.x, 0)).normalized() * (width / 2 + 0.6),
            c + Vector((0, 0, 0.3)) + Vector((-d.y, d.x, 0)).normalized() * (width / 2 + 0.6), 0.7, legs=False,
            rails=(True, False))


def rail_track(b, points, gauge=1.435, sleepers=0.65, tint=srgb(0.45, 0.32, 0.22), ballast=True):
    """Railway track along `points` (on the ground): ballast bed, sleepers, two rails."""
    pts = [Vector(p) for p in points]
    for a, c in zip(pts, pts[1:]):
        d = c - a
        length = d.length
        yaw = math.degrees(math.atan2(d.y, d.x))
        mid = (a + c) / 2
        if ballast:
            b.extrude([(-1.6, 0.0), (1.6, 0.0), (1.1, 0.3), (-1.1, 0.3)], length, (mid.x, mid.y, mid.z), (0, 0, yaw - 90),
                      mat=CONCRETE, tint=srgb(0.5, 0.48, 0.44), solid="hull", wear=0.8)
        n = int(length / sleepers)
        for k in range(n):
            p = a + d * ((k + 0.5) / n)
            b.box((0.22, 2.4, 0.14), (p.x, p.y, p.z + 0.37), (0, 0, yaw), mat=CONCRETE, tint=srgb(0.6, 0.58, 0.55),
                  solid=False, vary=0.06)
        side = Vector((-d.y, d.x, 0)).normalized() * (gauge / 2)
        for s in (side, -side):
            b.beam(a + s + Vector((0, 0, 0.51)), c + s + Vector((0, 0, 0.51)), 0.07, 0.15, mat=RUST, tint=(1, 1, 1))


def road(b, points, width=7.0, lines=True, tint=(1, 1, 1), kerb=False):
    """An asphalt road along `points`, with centre and edge lines."""
    pts = [Vector(p) for p in points]
    for a, c in zip(pts, pts[1:]):
        d = c - a
        yaw = math.degrees(math.atan2(d.y, d.x))
        mid = (a + c) / 2
        b.box((d.length + width * 0.02, width, 0.1), (mid.x, mid.y, mid.z + 0.05), (0, 0, yaw), mat=ASPHALT,
              tint=tint)
        if lines:
            n = int(d.length / 6)
            for k in range(n):
                p = a + d * ((k + 0.5) / n)
                b.box((3.0, 0.15, 0.012), (p.x, p.y, p.z + 0.106), (0, 0, yaw), mat=PAINT,
                      tint=srgb(0.85, 0.83, 0.75), solid=False, vary=0, wear=0.7)
            side = Vector((-d.y, d.x, 0)).normalized() * (width / 2 - 0.3)
            for s in (side, -side):
                b.box((d.length, 0.12, 0.012), ((mid + s).x, (mid + s).y, mid.z + 0.106), (0, 0, yaw), mat=PAINT,
                      tint=srgb(0.85, 0.83, 0.75), solid=False, vary=0, wear=0.7)


def solar_array(b, at, rot, rows=3, cols=8, tilt=30.0, broken=0.15):
    """Ground-mounted solar panels in rows on frames, an inverter box; some panels broken or gone."""
    with b.frame(at, rot):
        for r in range(rows):
            y = r * 5.0
            for c in range(cols):
                x = (c - (cols - 1) / 2) * 2.1
                for dx in (-0.8, 0.8):
                    b.box((0.08, 0.08, 1.4), (x + dx, y + 0.5, 0.7), mat=PAINT, tint=GREY, solid=False)
                    b.box((0.08, 0.08, 0.7), (x + dx, y - 0.5, 0.35), mat=PAINT, tint=GREY, solid=False)
                if b.rng.random() < broken:
                    continue
                b.box((2.0, 2.2, 0.05), (x, y, 1.05), (-tilt + b.rng.uniform(-2, 2), 0, 0), mat=GLOSS,
                      tint=srgb(0.12, 0.16, 0.28), solid=False, vary=0.05)
            b.collide_box(Vector((0, y, 0.8)), (cols * 2.1, 2.0, 1.6), (0, 0, 0))
        b.box((1.2, 0.8, 1.8), ((cols / 2 + 1) * 2.1, 2.0, 0.9), mat=PAINT, tint=LIGHTGREY, bevel=0.02)


def wind_turbine(b, at, height=60.0, blade=24.0, yaw=0.0, broken=False, tint=OFFWHITE):
    """A three-bladed wind turbine; `broken` leaves one blade snapped and lying at the foot."""
    x, y, z = at
    b.cyl(2.2, height, (x, y, z + height / 2), mat=PAINT, tint=tint, sides=28, radius2=1.3)
    b.cyl(3.2, 1.0, (x, y, z + 0.5), mat=CONCRETE, tint=CONCRETE_T, sides=28)
    with b.frame((x, y, z + height), (0, 0, yaw)):
        b.box((2.4, 7.0, 2.6), (0, 1.0, 1.0), mat=PAINT, tint=tint, bevel=0.3)
        b.lathe([(0, 0), (0.8, 0.3), (1.2, 1.2), (1.2, 1.8)], (0, -2.5, 1.0), (90, 0, 0), mat=PAINT, tint=tint,
                sides=20)
        for k in range(3):
            if broken and k == 0:
                continue
            ang = 120 * k + 10
            with b.frame((0, -3.2, 1.0), (0, ang, 0)):
                b.extrude([(-0.1, 0.0), (1.4, 0.4), (0.6, blade), (0.2, blade), (-0.4, 0.5)], 0.35, (0, 0, 0),
                          (0, 0, 0), mat=PAINT, tint=tint, solid=False)
    if broken:
        with b.frame((x + blade * 0.4, y + 6.0, z + 0.4), (0, 92, 30)):
            b.extrude([(-0.1, 0.0), (1.4, 0.4), (0.6, blade * 0.7), (0.2, blade * 0.7), (-0.4, 0.5)], 0.35,
                      (0, 0, 0), (0, 0, 0), mat=PAINT, tint=tint)
    b.marker("landmark", (x, y, z + height), kind_of="turbine")


# ------------------------------------------------------------------------------------------------ ships and rockets


def loft(b, rings, mat=HULL, tint=OFFWHITE, holes=0.0, solid="hull", cap_start=True, cap_end=True, wear=None):
    """A hull lofted through `rings` (each a list of 3D points, all rings the same count, in order along the hull);
    `holes` 0..1 leaves panels out (ragged damage)."""
    n = len(rings[0])
    verts = [v for ring in rings for v in ring]
    faces = []
    for i in range(len(rings) - 1):
        for j in range(n):
            if holes and b.rng.random() < holes:
                continue
            a = i * n + j
            c = i * n + (j + 1) % n
            faces.append([a, c, c + n, a + n])
    if cap_start:
        faces.append(list(range(n))[::-1])
    if cap_end:
        last = (len(rings) - 1) * n
        faces.append([last + j for j in range(n)])
    return b.mesh(verts, faces, mat=mat, tint=tint, solid=solid, smooth=True, wear=wear)


def hull_ring(width, height, y, z0=0.0, n=16, keel=0.25, flat=0.35):
    """A ship's cross-section at station `y`: a rounded box `width` x `height` with a keel (points round the ring,
    counter-clockwise seen from the bow)."""
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        cx, cz = math.cos(a), math.sin(a)
        # Superellipse: flat sides, rounded corners.
        ex = 2.0 / (2.0 + flat * 6)
        x = math.copysign(abs(cx) ** ex, cx) * width / 2
        z = math.copysign(abs(cz) ** ex, cz) * height / 2
        if z < 0:
            z *= 1 + keel * (1 - abs(cx))
        pts.append((x, y, z0 + z))
    return pts


def ship_hull(b, at, rot, length=120.0, width=26.0, height=18.0, stations=14, nose=0.25, tail=0.12, tint=OFFWHITE,
              band=ORANGE, damage=0.0, broken_at=None, n=20, buried=0.0):
    """A ship's hull lying in the frame: bow toward -Y, its keel at z = -`buried` * height (partly sunk into the
    ground). `broken_at`: fraction of the length where it snapped (the stern part is left out; place it yourself).
    Returns the stations' (y, width, height) for decking and ribs."""
    rings = []
    profile = []
    end = broken_at if broken_at else 1.0
    with b.frame(at, rot):
        for i in range(stations + 1):
            t = end * i / stations
            if t < nose:
                k = t / nose
                f = math.sqrt(max(0.0, 1 - (1 - k) ** 2))
                w, h = width * (0.12 + 0.88 * f), height * (0.3 + 0.7 * f)
            elif t > 1 - tail:
                k = (t - (1 - tail)) / tail
                w, h = width * (1 - 0.35 * k), height * (1 - 0.25 * k)
            else:
                w, h = width, height
            y = -length / 2 + length * t
            zc = h / 2 - buried * height
            rings.append(hull_ring(w, h, y, zc, n=n))
            profile.append((y, w, h, zc))
        loft(b, rings, tint=tint, holes=damage, cap_start=False, cap_end=not broken_at)
        # Nose cap.
        y0 = -length / 2 - width * 0.05
        b.mesh([(0, y0, profile[0][3])] + rings[0], [[0, j + 1, (j + 1) % n + 1] for j in range(n)], mat=HULL,
               tint=tint, solid="hull")
        # A livery band along the flank, ribs exposed where it broke.
        for y, w, h, zc in profile[2:-1]:
            b.box((w + 0.12, 2.2, h * 0.08), (0, y, zc + h * 0.18), mat=PAINT, tint=band, solid=False, vary=0)
        if broken_at:
            y, w, h, zc = profile[-1]
            for k in range(3):
                yy = y - 2.5 * k
                pts = hull_ring(w * 0.97, h * 0.97, yy, zc, n=n)
                b.tube(pts + [pts[0]], 0.35, mat=RUST, tint=(1, 1, 1), sides=6, solid=False)
            for z in (zc - h * 0.25, zc + h * 0.1):
                b.box((w * 0.92, 6.0, 0.3), (0, y - 3.0, z), mat=GRATE, tint=GREY)
    return profile


def rocket(b, at, height=70.0, radius=3.6, tint=OFFWHITE, band=BLACK, boosters=2):
    """A launch vehicle standing on its pad: core stage, interstage, upper stage and fairing, two strap-on
    boosters, fins and engine bells."""
    x, y, z = at
    core = height * 0.62
    b.cyl(radius, core, (x, y, z + 3.0 + core / 2), mat=PANEL, tint=tint, sides=36)
    for k in range(4):
        b.cyl(radius + 0.02, 1.2, (x, y, z + 3.0 + core * (0.2 + 0.2 * k)), mat=PAINT, tint=band, sides=36,
              solid=False, vary=0)
    up = height * 0.2
    b.cyl(radius * 0.92, up, (x, y, z + 3.0 + core + up / 2), mat=PANEL, tint=tint, sides=36)
    fair = height - core - up - 3.0
    b.lathe([(radius * 0.95, 0), (radius * 0.95, fair * 0.35), (radius * 0.75, fair * 0.7), (radius * 0.35, fair * 0.93),
             (0, fair)], (x, y, z + 3.0 + core + up), mat=PANEL, tint=tint, sides=36)
    for k in range(5):
        a = 2 * math.pi * k / 5
        b.lathe([(0.3, 0), (0.9, 1.5), (1.2, 3.0)], (x + math.cos(a) * radius * 0.5, y + math.sin(a) * radius * 0.5,
                                                     z + 3.0), (180, 0, 0), mat=RUST, tint=(1, 1, 1), sides=16,
                solid=False)
    for k in range(boosters):
        a = math.pi * k
        bx, by = x + math.cos(a) * (radius + 1.6), y + math.sin(a) * (radius + 1.6)
        bh = core * 0.6
        b.cyl(1.5, bh, (bx, by, z + 3.0 + bh / 2), mat=PANEL, tint=tint, sides=24)
        b.lathe([(1.5, 0), (1.2, 2.0), (0, 3.6)], (bx, by, z + 3.0 + bh), mat=PANEL, tint=tint, sides=24)
        b.lathe([(0.3, 0), (0.8, 1.2), (1.0, 2.4)], (bx, by, z + 3.0), (180, 0, 0), mat=RUST, tint=(1, 1, 1),
                sides=16, solid=False)
    for k in range(4):
        a = 2 * math.pi * k / 4 + math.pi / 4
        with b.frame((x + math.cos(a) * radius, y + math.sin(a) * radius, z + 3.0), (0, 0, math.degrees(a))):
            b.extrude([(0, 0), (2.5, 0), (0, 7.0)], 0.25, (0, 0, 0), (0, 0, 0), mat=PAINT, tint=band)
    b.marker("landmark", (x, y, z + height), kind_of="rocket")
