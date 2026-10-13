"""The monument kit (MONUMENTS.md §8.4): parametric parts that add primitives to a builder in its current frame
(railings, stairs, ladders, pipes, fences, tanks, towers, buildings...), and fixed pieces built once, exported to
game/assets/monuments/kit and placed as instances (`Builder.piece`).

Colours are given in sRGB for readability (`srgb`) and stored linear, as the shader multiplies them in.
"""
import json
import math

import bpy
import numpy as np
from mathutils import Vector

from . import DATA, KIT, log
from .builder import Builder, occlusion, _bake_colour
from .surfaces import (ASPHALT, CHAINLINK, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, HULL, LAMP,
                       MATTE, PAINT, PANEL, RUST)


def srgb(r, g, b):
    def lin(c):
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return (lin(r), lin(g), lin(b))


# The old expansion's livery (MONUMENTS.md §3.3) and the common colours of industry.
OFFWHITE = srgb(0.90, 0.89, 0.85)
ORANGE = srgb(0.88, 0.42, 0.12)
TEAL = srgb(0.16, 0.42, 0.45)
YELLOW = srgb(0.92, 0.70, 0.12)
GREY = srgb(0.60, 0.61, 0.62)
LIGHTGREY = srgb(0.78, 0.78, 0.77)
DARK = srgb(0.20, 0.21, 0.22)
BLACK = srgb(0.08, 0.08, 0.085)
OLIVE = srgb(0.40, 0.44, 0.30)
RED = srgb(0.70, 0.15, 0.11)
BLUE = srgb(0.20, 0.33, 0.55)
GREEN = srgb(0.38, 0.47, 0.40)
SAND = srgb(0.76, 0.70, 0.58)
CONCRETE_T = srgb(0.97, 0.96, 0.94)
WARM_LAMP = srgb(1.0, 0.82, 0.55)
COLD_LAMP = srgb(0.85, 0.93, 1.0)
RED_LAMP = srgb(1.0, 0.12, 0.05)


# ------------------------------------------------------------------------------------------------ small parts


def railing(b, a, c, height=1.1, spacing=1.6, tint=YELLOW, mat=PAINT, toe=True, post_end=True):
    """A guard rail from `a` to `c` (on the walking surface): posts, top and knee rails, a toe board."""
    a, c = Vector(a), Vector(c)
    d = c - a
    length = d.length
    if length < 0.05:
        return
    n = max(1, int(math.ceil(length / spacing)))
    for i in range(n + (1 if post_end else 0)):
        p = a + d * (i / n)
        b.box((0.05, 0.05, height), (p.x, p.y, p.z + height / 2), mat=mat, tint=tint, solid=False, vary=0.0)
    up = Vector((0, 0, 1))
    b.beam(a + up * height, c + up * height, 0.055, 0.055, mat=mat, tint=tint)
    b.beam(a + up * height * 0.52, c + up * height * 0.52, 0.04, 0.04, mat=mat, tint=tint)
    if toe:
        b.beam(a + up * 0.06, c + up * 0.06, 0.012, 0.12, mat=mat, tint=tint)
    # One collision slab along the rail so nobody walks off.
    mid = (a + c) / 2 + up * (height / 2)
    yaw = math.degrees(math.atan2(d.y, d.x))
    b.collide_box(mid, (length, 0.08, height), (0, 0, yaw))


def rail_loop(b, pts, height=1.1, tint=YELLOW, gap=None):
    """Railings round a closed outline; `gap` = index of a side left open (a stair or ladder)."""
    for i in range(len(pts)):
        if gap is not None and i == gap:
            continue
        railing(b, pts[i], pts[(i + 1) % len(pts)], height, tint=tint)


def ladder(b, at, height, rot=(0, 0, 0), cage=True, tint=YELLOW, width=0.5):
    """A fixed steel ladder from `at` up `height`, facing -Y; a safety cage above 2.5 m; a climb marker."""
    with b.frame(at, rot):
        for x in (-width / 2, width / 2):
            b.box((0.06, 0.03, height + 1.0), (x, 0, (height + 1.0) / 2), mat=PAINT, tint=tint, solid=False, vary=0)
        for i in range(int(height / 0.3)):
            b.cyl(0.014, width, (0, 0, 0.3 * (i + 0.5)), (0, 90, 0), mat=PAINT, tint=GREY, sides=6, solid=False,
                  vary=0)
        if cage and height > 3.0:
            z = 2.4
            while z < height + 0.8:
                b.tube([(-width / 2, 0, z), (-width / 2 - 0.1, -0.5, z), (0, -0.75, z), (width / 2 + 0.1, -0.5, z),
                        (width / 2, 0, z)], 0.02, mat=PAINT, tint=tint, sides=6, cap=False)
                z += 1.0
            for x, y in ((-width / 2 - 0.1, -0.5), (0, -0.75), (width / 2 + 0.1, -0.5)):
                b.box((0.04, 0.012, height + 0.8 - 2.4), (x, y, 2.4 + (height + 0.8 - 2.4) / 2), mat=PAINT, tint=tint,
                      solid=False, vary=0)
        b.marker("ladder", (0, -0.35, 0), height=round(height, 2))


def stairs(b, at, rot, width, rise, run, tint=YELLOW, grate=True, rails=True):
    """A straight steel stair from `at` (bottom of the first step, centre) climbing `rise` over `run` toward +Y."""
    steps = max(2, int(round(rise / 0.19)))
    h = rise / steps
    d = run / steps
    ang = math.degrees(math.atan2(rise, run))
    length = math.hypot(rise, run)
    with b.frame(at, rot):
        for x in (-width / 2 - 0.05, width / 2 + 0.05):
            b.box((0.08, length, 0.25), (x, run / 2, rise / 2 + 0.06), (ang, 0, 0), mat=PAINT, tint=tint, solid=False,
                  vary=0)
        for i in range(steps):
            b.box((width, d + 0.03, 0.04), (0, d * (i + 0.5), h * (i + 1) - 0.02), mat=GRATE if grate else PAINT,
                  tint=GREY, solid=False, vary=0)
        if rails:
            for x in (-width / 2 - 0.05, width / 2 + 0.05):
                railing(b, (x, 0, 0.0), (x, run, rise), 1.0, spacing=1.4, tint=tint, toe=False)
        b.collide_box(Vector((0, run / 2, rise / 2 - 0.05)), (width, length, 0.1), (ang, 0, 0))


def walkway(b, a, c, width=1.0, height=None, tint=YELLOW, rails=(True, True), legs=True):
    """A grated catwalk from `a` to `c` (its walking surface), on legs down to the ground when `legs`."""
    a, c = Vector(a), Vector(c)
    d = c - a
    length = d.length
    yaw = math.degrees(math.atan2(d.y, d.x))
    mid = (a + c) / 2
    side = Vector((-d.y, d.x, 0)).normalized() * (width / 2)
    b.box((length, width, 0.05), (mid.x, mid.y, mid.z - 0.025), (0, 0, yaw), mat=GRATE, tint=GREY, solid=False,
          vary=0)
    for s in (side, -side):
        b.beam(a + s - Vector((0, 0, 0.12)), c + s - Vector((0, 0, 0.12)), 0.08, 0.18, mat=PAINT, tint=DARK)
    b.collide_box(mid - Vector((0, 0, 0.05)), (length, width, 0.1), (0, 0, yaw))
    if rails[0]:
        railing(b, a + side, c + side, tint=tint)
    if rails[1]:
        railing(b, a - side, c - side, tint=tint)
    if legs and min(a.z, c.z) > 0.6:
        n = max(1, int(length / 4.0))
        for i in range(n + 1):
            p = a + d * (i / n)
            for s in (side, -side):
                q = p + s * 0.9
                b.box((0.12, 0.12, q.z), (q.x, q.y, q.z / 2), mat=PAINT, tint=DARK, vary=0)


def pipe(b, points, radius, tint=GREY, mat=PAINT, flanges=True, supports=0.0):
    """A pipe along `points` with flanges at each joint; `supports` > 0 adds a stand every that many metres down to
    the ground."""
    b.tube(points, radius, mat=mat, tint=tint, sides=12 if radius < 0.3 else 20, cap=True)
    pts = [Vector(p) for p in points]
    if flanges:
        for i, p in enumerate(pts):
            if i == 0 or i == len(pts) - 1 or True:
                nxt = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
                if nxt.length < 1e-6:
                    continue
                q = nxt.to_track_quat("Z", "Y")
                e = q.to_euler()
                b.cyl(radius * 1.35, radius * 0.25 + 0.03, tuple(p), tuple(math.degrees(x) for x in e), mat=mat,
                      tint=tint, sides=16, solid=False, vary=0)
    if supports > 0:
        for a, c in zip(pts, pts[1:]):
            seg = c - a
            n = int(seg.length / supports)
            for i in range(1, n + 1):
                p = a + seg * (i / (n + 1))
                if p.z > radius + 0.2:
                    b.box((0.14, 0.14, p.z - radius), (p.x, p.y, (p.z - radius) / 2), mat=PAINT, tint=DARK, vary=0)
                    b.box((radius * 2.6, 0.18, 0.12), (p.x, p.y, p.z - radius - 0.06), mat=PAINT, tint=DARK, vary=0)


def fence(b, points, height=2.4, post=3.0, tint=GREY, wire=True):
    """A security fence along `points`: posts, rails, mesh, and barbed wire on outriggers."""
    pts = [Vector(p) for p in points]
    for a, c in zip(pts, pts[1:]):
        d = c - a
        length = d.length
        n = max(1, int(math.ceil(length / post)))
        yaw = math.degrees(math.atan2(d.y, d.x))
        for i in range(n + 1):
            p = a + d * (i / n)
            b.cyl(0.035, height + 0.4, (p.x, p.y, p.z + (height + 0.4) / 2), mat=PAINT, tint=tint, sides=8,
                  solid=False, vary=0)
            if wire:
                b.box((0.03, 0.4, 0.03), (p.x, p.y, p.z + height + 0.25), (35, 0, yaw + 90), mat=PAINT, tint=tint,
                      solid=False, vary=0)
        mid = (a + c) / 2
        b.box((length, 0.01, height - 0.1), (mid.x, mid.y, mid.z + height / 2), (0, 0, yaw), mat=CHAINLINK,
              tint=GREY, solid=False, vary=0, wear=0.6)
        for z in (0.1, height):
            b.beam(a + Vector((0, 0, z)), c + Vector((0, 0, z)), 0.04, 0.04, mat=PAINT, tint=tint)
        if wire:
            off = Vector((-d.y, d.x, 0)).normalized() * -0.18
            for k, z in enumerate((height + 0.2, height + 0.33, height + 0.46)):
                o = off * (k / 2.0)
                b.tube([a + o + Vector((0, 0, z)), c + o + Vector((0, 0, z))], 0.008, mat=RUST, tint=(1, 1, 1),
                       sides=4, cap=False)
        b.collide_box(mid + Vector((0, 0, height / 2)), (length, 0.1, height), (0, 0, yaw))


def insulator(b, at, length=1.0, rot=(0, 0, 0), discs=None, tint=srgb(0.55, 0.30, 0.20)):
    """A stack of porcelain sheds on a rod: glazed brown, as on old grid equipment."""
    discs = discs or max(3, int(length / 0.14))
    prof = [(0.0, 0.0), (0.03, 0.0)]
    step = length / discs
    for i in range(discs):
        z = i * step
        prof += [(0.035, z + step * 0.1), (0.09, z + step * 0.35), (0.085, z + step * 0.5), (0.035, z + step * 0.7)]
    prof += [(0.03, length), (0.0, length)]
    b.lathe(prof, at, rot, mat=GLOSS, tint=tint, sides=14, solid=False, vary=0.02, wear=0.15)


def lamp(b, at, rot=(0, 0, 0), colour=WARM_LAMP, size=(0.5, 0.25, 0.12)):
    """A flood lamp head: a housing with a lit face toward -Y of its frame."""
    with b.frame(at, rot):
        b.box(size, (0, 0, 0), mat=PAINT, tint=DARK, bevel=0.02, solid=False, vary=0)
        b.box((size[0] * 0.86, 0.02, size[2] * 0.7), (0, -size[1] / 2 - 0.005, 0), mat=LAMP, tint=colour,
              solid=False, vary=0)
        b.marker("light", (0, -size[1] / 2 - 0.2, 0), colour=[round(c, 3) for c in colour], range=18.0)


def stripes(b, a, c, height, width=0.2, colours=(YELLOW, BLACK), thick=0.02, at_z=0.0, normal=(0, -1, 0)):
    """Hazard stripes painted along a wall from `a` to `c`: alternating diagonal bands, as thin boxes."""
    a, c = Vector(a), Vector(c)
    d = c - a
    n = max(1, int(d.length / width))
    yaw = math.degrees(math.atan2(d.y, d.x))
    off = Vector(normal).normalized() * thick / 2
    for i in range(n):
        p = a + d * ((i + 0.5) / n) + off
        b.box((d.length / n, thick, height), (p.x, p.y, at_z + height / 2), (0, 0, yaw), mat=PAINT,
              tint=colours[i % 2], solid=False, vary=0, wear=0.5)


def window_wall(b, w, h, at, rot, cols, rows, frame_t=0.08, broken=0.25, mat=CONCRETE, tint=CONCRETE_T,
                thick=0.3, sill=0.9, win_h=1.4, win_w=1.2):
    """A wall `w` x `h` (its outside face toward -Y) with a grid of windows: the solid parts as boxes around the
    openings, frames, dark glass (some panes missing)."""
    with b.frame(at, rot):
        xs = [-w / 2 + w * (i + 0.5) / cols for i in range(cols)]
        floor_h = h / rows
        # Piers between windows and spandrels above and below, as one strip per row.
        for r in range(rows):
            z0 = r * floor_h
            b.box((w, thick, sill), (0, 0, z0 + sill / 2), mat=mat, tint=tint)
            top = z0 + sill + win_h
            b.box((w, thick, floor_h - sill - win_h), (0, 0, top + (floor_h - sill - win_h) / 2), mat=mat, tint=tint)
            edges = [-w / 2] + [x for x0 in xs for x in (x0 - win_w / 2, x0 + win_w / 2)] + [w / 2]
            for k in range(0, len(edges), 2):
                x0, x1 = edges[k], edges[k + 1]
                if x1 - x0 > 0.01:
                    b.box((x1 - x0, thick, win_h), ((x0 + x1) / 2, 0, z0 + sill + win_h / 2), mat=mat, tint=tint)
            for x0 in xs:
                cz = z0 + sill + win_h / 2
                for dx in (-win_w / 2, win_w / 2):
                    b.box((frame_t, 0.1, win_h), (x0 + dx * 0.96, -thick * 0.25, cz), mat=PAINT, tint=DARK,
                          solid=False, vary=0)
                for dz in (-win_h / 2, win_h / 2):
                    b.box((win_w, 0.1, frame_t), (x0, -thick * 0.25, cz + dz * 0.96), mat=PAINT, tint=DARK,
                          solid=False, vary=0)
                b.box((0.05, 0.06, win_h), (x0, -thick * 0.25, cz), mat=PAINT, tint=DARK, solid=False, vary=0)
                if b.rng.random() > broken:
                    b.box((win_w, 0.02, win_h), (x0, -thick * 0.15, cz), mat=GLASS, solid=False, vary=0)
                b.box((win_w + 0.15, 0.22, 0.06), (x0, -thick * 0.5 - 0.06, z0 + sill - 0.03), mat=CONCRETE,
                      tint=tint, solid=False, vary=0)


def block_building(b, at, rot, w, d, h, floors=1, mat=CONCRETE, tint=CONCRETE_T, door_side=True, roof_units=True,
                   parapet=0.6, windows=True, broken=0.3):
    """A plain flat-roofed block: walls with windows on the long sides, a door in front (-Y), parapet, roof plant."""
    t = 0.3
    with b.frame(at, rot):
        cols = max(1, int(w / 3.2))
        if windows:
            window_wall(b, w, h, (0, -d / 2 + t / 2, 0), (0, 0, 0), cols, floors, mat=mat, tint=tint, broken=broken)
            window_wall(b, w, h, (0, d / 2 - t / 2, 0), (0, 0, 180), cols, floors, mat=mat, tint=tint, broken=broken)
        else:
            b.box((w, t, h), (0, -d / 2 + t / 2, h / 2), mat=mat, tint=tint)
            b.box((w, t, h), (0, d / 2 - t / 2, h / 2), mat=mat, tint=tint)
        b.box((t, d - 2 * t, h), (-w / 2 + t / 2, 0, h / 2), mat=mat, tint=tint)
        b.box((t, d - 2 * t, h), (w / 2 - t / 2, 0, h / 2), mat=mat, tint=tint)
        b.box((w, d, 0.3), (0, 0, h + 0.15), mat=mat, tint=tint)
        b.box((w - 0.1, d - 0.1, 0.12), (0, 0, 0.06), mat=CONCRETE, tint=srgb(0.7, 0.7, 0.68))
        if parapet:
            for y in (-d / 2 + 0.12, d / 2 - 0.12):
                b.box((w, 0.24, parapet), (0, y, h + 0.3 + parapet / 2), mat=mat, tint=tint)
            for x in (-w / 2 + 0.12, w / 2 - 0.12):
                b.box((0.24, d - 0.48, parapet), (x, 0, h + 0.3 + parapet / 2), mat=mat, tint=tint)
            b.box((w + 0.1, d + 0.1, 0.08), (0, 0, h + 0.3 + parapet + 0.04), mat=PAINT, tint=GREY, solid=False)
        if door_side:
            b.box((1.4, 0.12, 2.3), (w * 0.25, -d / 2 - 0.02, 1.15), mat=PAINT, tint=TEAL, bevel=0.01, solid=False)
            b.box((1.8, 1.2, 0.18), (w * 0.25, -d / 2 - 0.6, 0.09), mat=CONCRETE, tint=tint)
            b.box((2.0, 1.4, 0.12), (w * 0.25, -d / 2 - 0.7, 2.75), mat=CONCRETE, tint=tint, solid=False)
            b.marker("door", (w * 0.25, -d / 2 - 0.3, 0), kind_of="building")
        if roof_units:
            for i in range(max(1, int(w / 8))):
                x = -w / 2 + 3 + i * 8 * (w - 6) / max(1, w - 6)
                b.box((2.0, 1.4, 1.1), (min(x, w / 2 - 2), d * 0.15, h + 0.3 + 0.55), mat=PAINT, tint=LIGHTGREY,
                      bevel=0.03)
                b.cyl(0.45, 0.2, (min(x, w / 2 - 2), d * 0.15, h + 0.3 + 1.2), mat=PAINT, tint=GREY, sides=16,
                      solid=False)
        b.marker("loot", (-w / 4, 0, 0.12), table="crate")


def tank(b, at, radius, height, tint=OFFWHITE, roof="cone", ladder_side=0.0, band=None, legs=0.0, mat=PAINT):
    """A storage tank: shell with weld rings, a cone or dome roof with a rail, a ladder up the side."""
    x, y, z0 = at
    z = z0 + legs
    if legs:
        for k in range(4):
            a = math.radians(45 + 90 * k)
            b.box((0.3, 0.3, legs), (x + math.cos(a) * radius * 0.8, y + math.sin(a) * radius * 0.8, z0 + legs / 2),
                  mat=PAINT, tint=DARK)
    b.cyl(radius, height, (x, y, z + height / 2), mat=mat, tint=tint, sides=48 if radius > 4 else 32)
    rings = max(1, int(height / 2.4))
    for i in range(1, rings):
        b.cyl(radius + 0.015, 0.06, (x, y, z + height * i / rings), mat=mat, tint=tint, sides=48, solid=False,
              vary=0)
    if band:
        b.cyl(radius + 0.01, height * 0.12, (x, y, z + height * 0.72), mat=PAINT, tint=band, sides=48, solid=False,
              vary=0)
    top = z + height
    if roof == "cone":
        b.cyl(radius + 0.05, radius * 0.18, (x, y, top + radius * 0.09), mat=mat, tint=tint, sides=48, radius2=0.3,
              solid="hull")
    elif roof == "dome":
        prof = [(radius + 0.03, 0)] + [(radius * math.cos(t), radius * 0.35 * math.sin(t))
                                       for t in np.linspace(0.15, math.pi / 2, 7)]
        b.lathe(prof + [(0.0, radius * 0.35)], (x, y, top), mat=mat, tint=tint, sides=48)
    if ladder_side is not None:
        ang = math.radians(ladder_side)
        lx, ly = x + math.cos(ang) * (radius + 0.25), y + math.sin(ang) * (radius + 0.25)
        ladder(b, (lx, ly, z0), legs + height, (0, 0, math.degrees(ang) - 90 + 180), cage=True)
        pts = []
        for k in range(13):
            a = ang - math.radians(35) + math.radians(70) * k / 12
            pts.append((x + math.cos(a) * (radius - 0.1), y + math.sin(a) * (radius - 0.1), top + 0.05))
        for p, q in zip(pts, pts[1:]):
            railing(b, p, q, 1.0, spacing=2.0, toe=False)


def lattice_tower(b, at, height, base, top, rot=(0, 0, 0), sections=None, tint=GREY, member=0.12, mat=PAINT):
    """A square lattice tower (pylon or mast) tapering from `base` to `top` metres wide: four legs, horizontal
    girts and X bracing in every section."""
    sections = sections or max(3, int(height / 5.5))
    with b.frame(at, rot):
        def corner(level, i):
            t = level / sections
            hw = (base + (top - base) * t) / 2
            sx = -1 if i in (0, 3) else 1
            sy = -1 if i in (0, 1) else 1
            return Vector((sx * hw, sy * hw, height * t))
        for i in range(4):
            for s in range(sections):
                b.beam(corner(s, i), corner(s + 1, i), member * 1.4, member * 1.4, mat=mat, tint=tint,
                       solid=s == 0)
        for s in range(sections + 1):
            for i in range(4):
                b.beam(corner(s, i), corner(s, (i + 1) % 4), member, member, mat=mat, tint=tint)
        for s in range(sections):
            for i in range(4):
                j = (i + 1) % 4
                b.beam(corner(s, i), corner(s + 1, j), member * 0.8, member * 0.8, mat=mat, tint=tint)
                b.beam(corner(s, j), corner(s + 1, i), member * 0.8, member * 0.8, mat=mat, tint=tint)
        for i in range(4):
            c = corner(0, i)
            b.box((0.9, 0.9, 0.5), (c.x, c.y, 0.1), mat=CONCRETE, tint=CONCRETE_T)


def barrier(b, at, rot=(0, 0, 0), length=3.0, tint=CONCRETE_T, mat=CONCRETE):
    """A concrete road barrier (safety-shape profile)."""
    prof = [(-0.3, 0.0), (0.3, 0.0), (0.3, 0.08), (0.12, 0.33), (0.08, 0.81), (-0.08, 0.81), (-0.12, 0.33),
            (-0.3, 0.08)]
    with b.frame(at, rot):
        b.extrude(prof, length, (0, 0, 0), (0, 0, 90), mat=mat, tint=tint, bevel=0.01)


def cable(b, a, c, sag=0.04, radius=0.02, tint=BLACK, segments=10):
    """A hanging cable between two points (sag as a fraction of the span)."""
    a, c = Vector(a), Vector(c)
    pts = []
    for i in range(segments + 1):
        t = i / segments
        p = a.lerp(c, t)
        p.z -= 4 * sag * (a - c).length * t * (1 - t)
        pts.append(tuple(p))
    b.tube(pts, radius, mat=PAINT, tint=tint, sides=5, cap=False)


def rubble(b, centre, radius, count=12, tint=CONCRETE_T, mat=CONCRETE, max_size=1.2):
    """Broken concrete and debris lying round a point."""
    cx, cy, cz = centre
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        s = max_size * (0.25 + 0.75 * b.rng.random())
        b.box((s, s * (0.4 + 0.6 * b.rng.random()), s * (0.2 + 0.4 * b.rng.random())),
              (cx + math.cos(a) * r, cy + math.sin(a) * r, cz + s * 0.1),
              (b.rng.uniform(-20, 20), b.rng.uniform(-20, 20), b.rng.uniform(0, 360)), mat=mat, tint=tint,
              solid=s > 0.8)


# ------------------------------------------------------------------------------------------------ fixed pieces


class Piece(Builder):
    folder = KIT

    def __init__(self, key, seed=1):
        super().__init__(key, seed=seed, chunk=1e6, max_edge=2.0, lod_min=(0.0, 0.25, 1.0))

    def _chunk_of(self, p):
        # A piece is a single chunk.
        return (0, 0)


def _transformer(b):
    """A grid transformer: oil tank on a plinth, cooling fins on two sides, three high and three low bushings, a
    conservator drum on a frame, a control cabinet."""
    w, d, h = 2.6, 1.8, 2.4
    b.box((3.4, 2.6, 0.4), (0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T, bevel=0.03)
    b.box((w, d, h), (0, 0, 0.4 + h / 2), mat=PAINT, tint=GREEN, bevel=0.04)
    b.box((w + 0.1, d + 0.1, 0.12), (0, 0, 0.4 + h + 0.06), mat=PAINT, tint=GREEN, bevel=0.02)
    for side in (-1, 1):
        for i in range(9):
            x = -w / 2 + 0.25 + i * (w - 0.5) / 8
            b.box((0.05, 0.7, h * 0.8), (x, side * (d / 2 + 0.35), 0.4 + h * 0.45), mat=PAINT, tint=GREEN,
                  solid=False, vary=0)
        b.box((w - 0.3, 0.12, 0.12), (0, side * (d / 2 + 0.35), 0.4 + h * 0.86), mat=PAINT, tint=GREEN,
              solid=False)
        b.box((w - 0.3, 0.12, 0.12), (0, side * (d / 2 + 0.35), 0.4 + h * 0.06), mat=PAINT, tint=GREEN,
              solid=False)
    top = 0.4 + h + 0.12
    for i in range(3):
        x = -0.8 + 0.8 * i
        b.cyl(0.12, 0.2, (x, -0.35, top + 0.1), mat=PAINT, tint=GREY, sides=12, solid=False)
        insulator(b, (x, -0.35, top + 0.2), 1.1)
        b.cyl(0.05, 0.12, (x, -0.35, top + 1.36), mat=PAINT, tint=LIGHTGREY, sides=8, solid=False)
        insulator(b, (x * 0.8, 0.45, top), 0.45, discs=3)
    b.box((0.08, 0.08, 1.4), (-1.0, 0.55, top + 0.7), mat=PAINT, tint=GREEN, solid=False)
    b.box((0.08, 0.08, 1.4), (1.0, 0.55, top + 0.7), mat=PAINT, tint=GREEN, solid=False)
    b.cyl(0.38, 2.0, (0, 0.55, top + 1.5), (0, 90, 0), mat=PAINT, tint=GREEN, sides=20)
    b.box((0.7, 0.35, 1.0), (w / 2 - 0.4, -d / 2 - 0.2, 1.1), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
    b.box((0.5, 0.02, 0.12), (w / 2 - 0.4, -d / 2 - 0.38, 1.45), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    b.tube([(-w / 2 - 0.05, -0.4, 1.0), (-w / 2 - 0.3, -0.4, 1.0), (-w / 2 - 0.3, -0.4, 0.4)], 0.04, tint=DARK)


def _breaker(b):
    """A circuit breaker on a steel stand: three pole units with long insulators, an operating cabinet."""
    for x in (-0.9, 0.9):
        for y in (-0.5, 0.5):
            b.box((0.12, 0.12, 2.4), (x, y, 1.2), mat=PAINT, tint=GREY, solid=False)
    b.box((2.2, 1.3, 0.14), (0, 0, 2.45), mat=PAINT, tint=GREY)
    b.box((0.7, 0.5, 0.9), (0, -0.6, 1.0), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
    for i in range(3):
        x = -0.8 + 0.8 * i
        b.cyl(0.18, 0.5, (x, 0, 2.77), mat=PAINT, tint=GREY, sides=14, solid=False)
        insulator(b, (x, 0, 3.0), 1.6, tint=srgb(0.82, 0.82, 0.8))
        b.box((0.28, 0.14, 0.12), (x, 0, 4.66), mat=PAINT, tint=LIGHTGREY, solid=False)
    b.box((0.25, 0.25, 0.4), (0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T)


def _gantry(b):
    """A switchyard gantry: two lattice columns and a lattice beam 12 m across, with strain insulators hanging."""
    span, hgt = 12.0, 10.0
    for x in (-span / 2, span / 2):
        lattice_tower(b, (x, 0, 0), hgt, 1.2, 0.8, sections=4, member=0.08)
    for z in (hgt - 0.5, hgt + 0.3):
        for y in (-0.4, 0.4):
            b.beam((-span / 2, y, z), (span / 2, y, z), 0.1, 0.1, mat=PAINT, tint=GREY)
    for i in range(12):
        x = -span / 2 + span * i / 11
        b.beam((x, -0.4, hgt - 0.5), (x, 0.4, hgt + 0.3), 0.06, 0.06, mat=PAINT, tint=GREY)
    for i in range(3):
        x = -3.5 + 3.5 * i
        insulator(b, (x, 0, hgt - 2.0), 1.5, tint=srgb(0.82, 0.82, 0.8))


def _jersey(b):
    barrier(b, (0, 0, 0), (0, 0, 0), 3.0)


def _container(b):
    """A 20-foot freight container: corrugated sides, corner posts and castings, doors with lock bars."""
    w, d, h = 6.06, 2.44, 2.59
    b.box((w - 0.2, d - 0.04, h - 0.25), (0, 0, h / 2), mat=CORRUGATED, tint=(1, 1, 1))
    for x in (-w / 2 + 0.07, w / 2 - 0.07):
        for y in (-d / 2 + 0.07, d / 2 - 0.07):
            b.box((0.15, 0.15, h), (x, y, h / 2), mat=PAINT, tint=(1, 1, 1), solid=False)
    for z in (0.08, h - 0.08):
        for y in (-d / 2 + 0.06, d / 2 - 0.06):
            b.box((w, 0.13, 0.16), (0, y, z), mat=PAINT, tint=(1, 1, 1), solid=False)
        for x in (-w / 2 + 0.06, w / 2 - 0.06):
            b.box((0.13, d, 0.16), (x, 0, z), mat=PAINT, tint=(1, 1, 1), solid=False)
    b.box((0.05, d - 0.3, h - 0.3), (w / 2 - 0.02, 0, h / 2), mat=PAINT, tint=(1, 1, 1), solid=False)
    for y in (-0.75, -0.35, 0.35, 0.75):
        b.cyl(0.022, h - 0.35, (w / 2 + 0.03, y, h / 2), mat=PAINT, tint=GREY, sides=6, solid=False)
    b.collide_box(Vector((0, 0, h / 2)), (w, d, h), (0, 0, 0))


def _lamp_post(b):
    """A 9 m lighting column with two flood lamps."""
    b.box((0.5, 0.5, 0.6), (0, 0, 0.1), mat=CONCRETE, tint=CONCRETE_T, bevel=0.02)
    b.cyl(0.11, 9.0, (0, 0, 4.7), mat=PAINT, tint=GREY, sides=12, radius2=0.07)
    b.box((1.6, 0.12, 0.12), (0, 0, 9.0), mat=PAINT, tint=GREY, solid=False)
    for x in (-0.7, 0.7):
        lamp(b, (x, -0.1, 8.85), (-25, 0, 0))


def _drum_stack(b):
    """Four oil drums on a pallet."""
    b.box((1.2, 1.2, 0.14), (0, 0, 0.07), mat=PAINT, tint=srgb(0.55, 0.42, 0.3), solid=False)
    for i, (x, y) in enumerate(((-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3))):
        tint = (BLUE, RED, OFFWHITE, BLUE)[i]
        b.cyl(0.29, 0.88, (x, y, 0.14 + 0.44), mat=PAINT, tint=tint, sides=16, solid=False)
        b.cyl(0.3, 0.03, (x, y, 0.14 + 0.29), mat=PAINT, tint=tint, sides=16, solid=False, vary=0)
        b.cyl(0.3, 0.03, (x, y, 0.14 + 0.59), mat=PAINT, tint=tint, sides=16, solid=False, vary=0)
    b.collide_box(Vector((0, 0, 0.5)), (1.2, 1.2, 1.0), (0, 0, 0))


def _cable_reel(b):
    """A wooden cable drum on its side with black cable wound on."""
    b.cyl(0.9, 0.08, (0, -0.55, 0.9), (90, 0, 0), mat=PAINT, tint=srgb(0.55, 0.42, 0.28), sides=24, solid=False)
    b.cyl(0.9, 0.08, (0, 0.55, 0.9), (90, 0, 0), mat=PAINT, tint=srgb(0.55, 0.42, 0.28), sides=24, solid=False)
    b.cyl(0.62, 1.02, (0, 0, 0.9), (90, 0, 0), mat=PAINT, tint=BLACK, sides=24, solid=False)
    b.collide_box(Vector((0, 0, 0.9)), (1.8, 1.2, 1.8), (0, 0, 0))


PIECES = {
    "transformer": _transformer,
    "breaker": _breaker,
    "gantry": _gantry,
    "jersey-barrier": _jersey,
    "container-20": _container,
    "lamp-post": _lamp_post,
    "drum-stack": _drum_stack,
    "cable-reel": _cable_reel,
}

_built = {}
_exported = set()
EXPORT = {"enabled": False}


def reset():
    """Forgets the built pieces (after the scene was emptied)."""
    _built.clear()


def ensure(key):
    """Builds piece `key` once per run (and exports it when exporting)."""
    if key in _built:
        return _built[key]
    if key not in PIECES:
        raise KeyError(f"no kit piece {key}")
    p = Piece(key, seed=hash_seed(key))
    PIECES[key](p)
    names, index = p._materials()
    levels = []
    for level in range(3):
        meshes = p.build_level(level, names, index)
        if not meshes:
            # Nothing of this piece is big enough for the level: it reuses the previous one.
            levels.append(levels[-1])
            continue
        # A piece is one chunk.
        me = next(iter(meshes.values()))
        me.name = f"{key}_lod{level}"
        o = bpy.data.objects.new(me.name, me)
        bpy.context.scene.collection.objects.link(o)
        ao = occlusion([o], [o], 24 if level == 0 else 8, 2.0, 0.0) if level < 2 else \
            {o: np.ones(len(me.vertices), np.float32)}
        _bake_colour(o, ao[o])
        bpy.context.scene.collection.objects.unlink(o)
        levels.append((me, o))
    _built[key] = {"piece": p, "levels": levels, "names": names}
    if EXPORT["enabled"] and key not in _exported:
        _export_piece(key)
        _exported.add(key)
    return _built[key]


def mesh(key, level=0):
    return ensure(key)["levels"][level][0]


def hash_seed(key):
    h = 2166136261
    for ch in key.encode():
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return h


def _export_piece(key):
    from hollow.export import collection, export_glb
    from .godot_mats import godot_materials
    rec = _built[key]
    col = collection("KIT_EXPORT")
    objs = []
    for me, o in rec["levels"]:
        col.objects.link(o)
        o.name = me.name.replace(key, "Piece")
        objs.append(o)
    export_glb(KIT / f"{key}.glb", objs, godot_materials(rec["names"]))
    for (me, o) in rec["levels"]:
        col.objects.unlink(o)
        o.name = me.name  # free the name "Piece_lodN" for the next piece
    p = rec["piece"]
    lo, hi = p.bounds()
    from .builder import to_game
    data = {"key": key, "model": f"res://assets/monuments/kit/{key}.glb", "collision": p._collision(),
            "markers": p.markers, "bounds": {"min": to_game(Vector((lo.x, hi.y, lo.z))),
                                             "max": to_game(Vector((hi.x, lo.y, hi.z)))}}
    (DATA / "kit").mkdir(parents=True, exist_ok=True)
    (DATA / "kit" / f"{key}.json").write_text(json.dumps(data, indent=1) + "\n", encoding="utf-8", newline="\n")
    log(f"kit piece {key} exported")
