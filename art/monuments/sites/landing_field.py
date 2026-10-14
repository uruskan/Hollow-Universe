"""Landing Field (MONUMENTS.md §5.3, medium tier): the Seedward field where colony shuttles came down, on flat ground:
an apron of five pads, two arched hangars (a shuttle still on its stands in one), a 30 m control tower whose glazed
cab keeps the flight records room (cyan chip and power cell), an operations block, a bunded fuel farm, a radar mast,
and a lander that crashed short of the pads, broken in two at the end of its furrow. Ship parts and fuel.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLACK, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, fence, ladder, lamp, railing, rubble, srgb, stairs, tank)
from monkit.structures import dish, road, ship_hull
from monkit.surfaces import ASPHALT, CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "landing-field"
TITLE = "Landing Field"
SEED = 3303
BUILDER = {"chunk": 64.0, "max_edge": 5.0}

TOWER = (40.0, 22.0)
HANGARS = ((-112.0, 46.0), (-48.0, 46.0))
CRASH = (138.0, -6.0)


def apron(b):
    b.box((310.0, 64.0, 0.12), (0.0, -72.0, 0.06), mat=ASPHALT, tint=(1, 1, 1))
    for k, x in enumerate((-124.0, -62.0, 0.0, 62.0, 124.0)):
        y = -72.0
        b.cyl(14.0, 0.06, (x, y, 0.15), mat=CONCRETE, tint=srgb(0.70, 0.69, 0.66), sides=32, solid=False, vary=0)
        b.lathe([(11.4, 0.181), (12.2, 0.181), (12.2, 0.19), (11.4, 0.19), (11.4, 0.181)], (x, y, 0), tint=YELLOW,
                sides=32, close=False, solid=False, vary=0, wear=0.6)
        pm.stencil(b, (x, y - 6.0, 0.19), (-90, 0, 0), str(k + 1), height=5.0, tint=OFFWHITE, thick=0.012)
        for j in range(8):
            a = math.radians(22.5 + 45 * j)
            b.box((0.4, 0.4, 0.3), (x + math.cos(a) * 13.4, y + math.sin(a) * 13.4, 0.3), mat=LAMP,
                  tint=srgb(1.0, 0.75, 0.3), solid=False, vary=0)
        b.marker("landing", (x, y, 0.19), 0.0, size=24.0)
        b.box((0.3, 18.0, 0.012), (x, y + 23.0, 0.126), tint=YELLOW, solid=False, vary=0, wear=0.7)
    b.box((290.0, 0.35, 0.012), (0.0, -46.0, 0.126), tint=YELLOW, solid=False, vary=0, wear=0.7)
    for k in range(30):
        x = -150.0 + k * 10.0
        for y in (-104.5, -39.5):
            b.cyl(0.08, 0.6, (x, y, 0.3), tint=GREY, sides=6, solid=False, vary=0)
            b.box((0.25, 0.25, 0.18), (x, y, 0.65), mat=LAMP, tint=(srgb(0.3, 0.6, 1.0), srgb(1.0, 0.8, 0.3))[y > -50],
                  solid=False, vary=0)
    b.piece("utility-truck", (-20.0, -52.0, 0), (0, 0, 180), tint=srgb(0.95, 0.75, 0.2))
    b.piece("utility-truck", (88.0, -60.0, 0), (0, 0, 30), tint=OFFWHITE)
    b.piece("crate-stack", (-8.0, -52.0, 0), (0, 0, 10))
    b.piece("crate-stack", (-6.4, -53.0, 0), (0, 0, -15))
    b.marker("loot", (-10.0, -50.0, 0.12), table="crate")
    b.marker("loot", (60.0, -48.0, 0.12), table="barrel")


def hangar(b, at, w=52.0, d=60.0, h=21.0, ship=False, damage=0.0):
    """An arched hangar: a corrugated shell on steel ribs, a back wall, a front gable over sliding doors."""
    rng = b.rng
    n = 18
    outer = [((w / 2) * math.cos(math.pi * i / n), 0.3 + h * math.sin(math.pi * i / n)) for i in range(n + 1)]
    inner = [(x * 0.99, 0.3 + (z - 0.3) * 0.99) for x, z in reversed(outer)]
    segs = 10
    with b.frame(at):
        b.box((w + 2.0, d + 2.0, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
        for k in range(segs):
            y = -d / 2 + d * (k + 0.5) / segs
            if damage and rng.random() < damage:
                # A torn section: only the lower parts of the shell remain.
                lo = [p for p in outer if p[1] < h * 0.55]
                lo_r = [p for p in lo if p[0] > 0]
                lo_l = [p for p in lo if p[0] < 0]
                for part in (lo_r, lo_l):
                    if len(part) >= 2:
                        inn = [(x * 0.99, 0.3 + (z - 0.3) * 0.99) for x, z in reversed(part)]
                        b.extrude(part + inn, d / segs - 0.08, (0, y, 0), (0, 0, 0), mat=CORRUGATED,
                                  tint=srgb(0.66, 0.68, 0.66), solid=False)
                continue
            b.extrude(outer + inner, d / segs - 0.08, (0, y, 0), (0, 0, 0), mat=CORRUGATED,
                      tint=srgb(0.66, 0.68, 0.66), solid=False)
        for k in range(segs + 1):
            y = -d / 2 + d * k / segs
            b.tube([(x * 1.005, y, z + 0.1) for x, z in outer], 0.22, tint=srgb(0.4, 0.42, 0.42), sides=6)
        for side in (-1, 1):
            b.box((0.6, d, 1.2), (side * w / 2, 0, 0.6), mat=CONCRETE, tint=CONCRETE_T)
            for k in range(n // 2):
                p0, p1 = outer[k] if side > 0 else outer[n - k], outer[k + 1] if side > 0 else outer[n - k - 1]
                cx, cz = (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2
                ln = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
                ang = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0]))
                b.collide_box((cx, 0, cz), (ln, d, 0.3), (0, -ang, 0))
        # Back wall and the front gable above the doors.
        b.extrude(outer, 0.3, (0, d / 2, 0), (0, 0, 0), mat=CORRUGATED, tint=srgb(0.6, 0.62, 0.6), solid=False)
        b.collide_box((0, d / 2, h / 2), (w, 0.3, h), (0, 0, 0))
        door_h = 15.0
        gable = [p for p in outer if p[1] > door_h]
        xr = (w / 2) * math.cos(math.asin(min(1.0, (door_h - 0.3) / h)))
        b.extrude([(xr, door_h)] + gable + [(-xr, door_h)], 0.3, (0, -d / 2, 0), (0, 0, 0), mat=CORRUGATED,
                  tint=srgb(0.6, 0.62, 0.6), solid=False)
        b.box((2 * xr, 0.8, 1.0), (0, -d / 2, door_h - 0.2), tint=DARK)
        # Sliding doors: leaves stacked at both sides, one hanging off its track.
        for side in (-1, 1):
            for j in range(2):
                x = side * (xr - 4.5 - j * 1.2)
                b.box((8.6, 0.3, door_h - 0.6), (x, -d / 2 - 0.4 - j * 0.35, (door_h - 0.6) / 2 + 0.3), mat=CORRUGATED,
                      tint=srgb(0.55, 0.58, 0.6))
        with b.frame((6.0, -d / 2 - 1.6, 0.3), (0, 0, 8.0)):
            b.box((8.6, 0.3, door_h - 1.2), (0, 0, (door_h - 1.2) / 2), (0, -4.0, 0), mat=CORRUGATED,
                  tint=srgb(0.55, 0.58, 0.6))
        pm.hazard_band(b, (-xr, -d / 2 - 0.45, 0), (xr, -d / 2 - 0.45, 0), door_h - 0.8, 0.7, width=0.8)
        b.marker("door", (0, -d / 2, 0.3), 180.0, kind_of="hangar", width=round(2 * xr - 18, 1), height=door_h)
        for x in (-w / 4, w / 4):
            lamp(b, (x, -d / 2 - 0.9, door_h + 0.8), (-20, 0, 0), size=(0.9, 0.4, 0.35))
        # Inside: work lights, crates, a gantry beam; the shuttle on stands in one of them.
        for k in range(3):
            y = -d / 4 + k * d / 4
            b.box((w * 0.8, 0.5, 0.8), (0, y, h * 0.82), tint=srgb(0.55, 0.3, 0.12))
            b.box((1.0, 0.5, 1.6), (rng.uniform(-w * 0.3, w * 0.3), y, h * 0.82 - 1.2), tint=YELLOW)
        b.piece("crate-stack", (-w / 2 + 3.0, d / 2 - 3.0, 0.3), (0, 0, 10))
        b.piece("crate-stack", (-w / 2 + 4.4, d / 2 - 3.4, 0.3), (0, 0, -20))
        b.piece("drum-stack", (w / 2 - 3.0, d / 2 - 3.0, 0.3), (0, 0, 0))
        b.marker("loot", (-w / 2 + 3.0, d / 2 - 6.0, 0.3), table="crate")
        b.marker("loot", (w / 2 - 3.5, d / 2 - 6.0, 0.3), table="fuel-barrel")
        if ship:
            for x in (-3.0, 3.0):
                for y in (-8.0, 2.0, 10.0):
                    b.box((0.8, 0.8, 2.6), (x, y, 1.6), tint=YELLOW)
            ship_hull(b, (0, 2.0, 2.6), (0, 0, 180), length=34.0, width=11.0, height=6.5, stations=12, tint=OFFWHITE,
                      band=TEAL, damage=0.06, n=18)
            for x in (-7.0, 7.0):
                pm.stair_tower(b, (x, -6.0, 0.3), (0, 0, 0), 7.5, w=2.6, d=7.0, stair_to=7.5,
                               tint=srgb(0.6, 0.62, 0.6))
            b.marker("loot", (0.0, -12.0, 0.3), table="crate", holds="ship-parts")
            b.marker("loot", (7.0, -14.0, 0.3), table="military-crate")
            b.marker("guard", (0.0, -20.0, 0.3), 180.0, role="reclaimer")


def _clip(poly, a, b, c):
    """The part of a convex polygon where a*x + b*y <= c."""
    out = []
    for i in range(len(poly)):
        p, q = poly[i], poly[(i + 1) % len(poly)]
        fp, fq = a * p[0] + b * p[1] - c, a * q[0] + b * q[1] - c
        if fp <= 0:
            out.append(p)
        if fp * fq < 0:
            t = fp / (fp - fq)
            out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    return out


def control_tower(b):
    x, y = TOWER
    sw, sd, H = 7.0, 9.0, 27.0
    with b.frame((x, y, 0)):
        b.box((sw + 1.0, sd + 1.0, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=CONCRETE_T)
        # The shaft: four walls with a slot window per storey, a door at the foot.
        corners = [(-sw / 2, -sd / 2), (sw / 2, -sd / 2), (sw / 2, sd / 2), (-sw / 2, sd / 2)]
        for i in range(4):
            a, c = corners[i], corners[(i + 1) % 4]
            ln = math.hypot(c[0] - a[0], c[1] - a[1])
            ops = [(ln / 2 + (1.2 if i % 2 else 0.0), 0.8, 1.6, 1.4 + k * 3.4) for k in range(1, 7)]
            if i == 0:
                ops = [(1.8, 1.3, 2.3, 0, {"kind_of": "tower", "state": "ajar"})] + ops
            pm.wall(b, (a[0], a[1], 0.3), (c[0], c[1], 0.3), H - 0.3, 0.35, ops, mat=FORMWORK, tint=CONCRETE_T,
                    glass=0.4)
        for z, t in ((8.0, ORANGE), (16.0, TEAL)):
            for (px, py, sx, sy) in ((0, -sd / 2 - 0.19, sw + 0.4, 0.03), (0, sd / 2 + 0.19, sw + 0.4, 0.03),
                                     (-sw / 2 - 0.19, 0, 0.03, sd + 0.4), (sw / 2 + 0.19, 0, 0.03, sd + 0.4)):
                b.box((sx, sy, 0.8), (px, py, z), tint=t, solid=False, vary=0, wear=0.5)
        top, run = pm.switchback(b, (0.0, 1.9, 0.3), (0, 0, 180), H + 0.1, width=1.0, flight_rise=2.65,
                                 tint=YELLOW, landing_rails=False)
        pm.stencil(b, (0.0, -sd / 2 - 0.2, 21.0), (0, 0, 0), "T-3", height=2.0, tint=OFFWHITE)
        b.marker("guard", (0.0, -sd / 2 - 3.0, 0.0), 180.0, role="reclaimer")
        # The cab: an octagonal floor over the shaft, slanted glazing, the records room at the back.
        R = 7.2
        octo = [((R + 0.4) * math.cos(math.radians(22.5 + 45 * k)), (R + 0.4) * math.sin(math.radians(22.5 + 45 * k)))
                for k in range(8)]
        hx0, hy0, hx1, hy1 = -1.12, -3.15, 1.12, 2.85
        for cuts in (((1, 0, hx0),), ((-1, 0, -hx1),), ((-1, 0, -hx0), (1, 0, hx1), (0, 1, hy0)),
                     ((-1, 0, -hx0), (1, 0, hx1), (0, -1, -hy1))):
            poly = octo
            for a_, b_, c_ in cuts:
                poly = _clip(poly, a_, b_, c_)
            if len(poly) >= 3:
                b.slab(poly, 0.4, (0, 0, H), mat=CONCRETE, tint=CONCRETE_T)
        apo = R * math.cos(math.pi / 8)
        for k in range(8):
            a = math.radians(45 * k)
            with b.frame((math.cos(a) * apo, math.sin(a) * apo, H + 0.4), (0, 0, math.degrees(a) + 90)):
                side = 2 * R * math.sin(math.pi / 8)
                b.box((side, 0.3, 0.9), (0, 0, 0.45), mat=PANEL, tint=OFFWHITE)
                if k != 2:
                    b.box((side - 0.2, 0.05, 2.4), (0, -0.25, 2.1), (-12, 0, 0), mat=GLASS, solid=False, vary=0)
                else:
                    b.box((side, 0.3, 3.3), (0, 0, 2.55), mat=PANEL, tint=OFFWHITE)
                b.box((0.12, 0.4, 3.4), (side / 2, -0.15, 2.1), tint=DARK, solid=False, vary=0)
                b.collide_box((0, 0, 1.6), (side, 0.3, 3.2), (0, 0, 0))
        b.cyl(R + 0.9, 0.5, (0, 0, H + 4.25), (0, 0, 22.5), mat=PANEL, tint=OFFWHITE, sides=8, solid="hull")
        b.cyl(R + 0.92, 0.2, (0, 0, H + 4.0), (0, 0, 22.5), tint=ORANGE, sides=8, solid=False, vary=0)
        # The records room: a chord wall across the back of the cab with the cyan door.
        pm.wall(b, (4.2, 3.0, H + 0.4), (-4.2, 3.0, H + 0.4), 3.6, 0.25,
                [(4.2, 1.2, 2.3, 0, {"kind_of": "records", "chip": "cyan", "cell": True, "room": "flight-records"})],
                mat=PANEL, tint=srgb(0.86, 0.86, 0.84))
        b.piece("rack-row", (-2.0, 5.6, H + 0.4), (0, 0, 180))
        b.piece("rack-row", (2.0, 5.6, H + 0.4), (0, 0, 180))
        b.marker("loot", (0.0, 4.4, H + 0.4), table="sealed-crate")
        b.marker("loot", (-3.0, 4.0, H + 0.4), table="military-crate")
        for k, a in enumerate((-90.0, -45.0, -135.0, 0.0, 180.0)):
            ar = math.radians(a)
            b.piece("console", (math.cos(ar) * 5.4, math.sin(ar) * 5.4, H + 0.4), (0, 0, a + 90))
        b.marker("loot", (3.0, -2.0, H + 0.4), table="crate")
        b.marker("guard", (-2.0, -1.0, H + 0.4), 180.0, role="reclaimer")
        # Roof: radar, aerials, beacon, a ladder up from the cab.
        ladder(b, (-5.0, 1.5, H + 0.4), 4.2, (0, 0, 90), cage=False)
        dish(b, (2.0, -1.0, H + 4.5), diameter=3.0, elevation=20.0, azimuth=30.0, mount_h=1.2, ribs=8,
             panels_missing=0.0)
        for (ax, ay) in ((-4.0, -3.0), (-3.0, 4.0)):
            b.cyl(0.05, 6.0, (ax, ay, H + 7.5), tint=GREY, sides=6, solid=False)
        pm.aviation_lamp(b, (-4.0, -3.0, H + 10.5), size=0.35)
        b.cyl(0.6, 0.6, (4.5, 4.0, H + 4.8), mat=LAMP, tint=srgb(0.4, 1.0, 0.5), sides=10, solid=False, vary=0)
        b.marker("light", (4.5, 4.0, H + 5.2), colour=[0.4, 1.0, 0.5], range=400.0, blink=2.0)
        b.marker("landmark", (0.0, 0.0, H + 10.5), kind_of="control-tower", visible_km=8)
    b.markers = [m for m in b.markers if not (m["kind"] == "landmark" and m.get("kind_of") == "dish")]
    # The operations block at the tower's foot.
    ox, oy = x + 24.0, y + 2.0
    info = pm.building(b, (ox, oy, 0), (0, 0, 0), 30.0, 13.0, floors=2, floor_h=3.5, mat=PANEL, tint=OFFWHITE,
                       openings={"front": [(-10.0, 2.4, 2.4, 0, 0, {"kind_of": "entrance", "state": "open"})]
                                          + [(xx, 2.0, 1.4, 0.9, 0) for xx in (-5.0, 0.0, 5.0, 10.0)]
                                          + [(xx, 2.0, 1.4, 0.9, 1) for xx in (-10.0, -5.0, 0.0, 5.0, 10.0)],
                                 "back": [(xx, 2.0, 1.4, 0.9, k) for k in (0, 1) for xx in (-9.0, -3.0, 3.0, 9.0)],
                                 "left": [(0.0, 1.2, 2.3, 0, 0)]},
                       holes=[(11.6, -5.9, 12.8, -0.8, 1), (-13.5, 4.6, -12.5, 5.6, 2)], band=(TEAL, 3.4, 0.4),
                       roof_units=3, glass=0.5)
    with b.frame((ox, oy, 0)):
        f0, f1 = info["floor"]
        stairs(b, (12.2, -5.9, f0), (0, 0, 0), 1.2, f1 - f0, 5.1)
        pm.roof_hatch(b, (-13.0, 5.1), (1.0, 1.0), f1, info["roof"])
        for xx in (-8.0, -2.0, 4.0):
            b.piece("console", (xx, 4.8, f1), (0, 0, 180))
        b.marker("loot", (-12.0, -4.5, f0), table="crate")
        b.marker("loot", (6.0, 4.0, f0), table="barrel")
        b.marker("loot", (-10.0, -4.5, f1), table="crate")
        pm.code_plate(b, (-6.5, -6.68, 2.6), (0, 0, 0), 2.6, 0.5, base=TEAL, ink=OFFWHITE, text="OPS-1")


def fuel_farm(b):
    fx, fy = 136.0, 64.0
    for (a, c) in (((fx - 32.0, fy - 22.0), (fx + 32.0, fy - 22.0)), ((fx + 32.0, fy - 22.0), (fx + 32.0, fy + 22.0)),
                   ((fx + 32.0, fy + 22.0), (fx - 32.0, fy + 22.0)), ((fx - 32.0, fy + 22.0), (fx - 32.0, fy - 22.0))):
        dx, dy = c[0] - a[0], c[1] - a[1]
        ln = math.hypot(dx, dy)
        b.box((ln + 0.5, 0.5, 1.4), ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2, 0.7),
              (0, 0, math.degrees(math.atan2(dy, dx))), mat=CONCRETE, tint=CONCRETE_T)
    concrete_steps = pm.concrete_steps
    concrete_steps(b, (fx - 10.0, fy - 23.6, 0), (0, 0, 0), 2.0, 1.4, 1.2)
    for i, (dx, dy) in enumerate(((-20.0, -9.0), (0.0, -9.0), (20.0, -9.0), (-20.0, 10.0), (0.0, 10.0), (20.0, 10.0))):
        tank(b, (fx + dx, fy + dy, 0), 7.0, 9.0 + (i % 2) * 1.5, tint=(OFFWHITE, srgb(0.84, 0.85, 0.82))[i % 2],
             band=(ORANGE, TEAL)[i % 2], ladder_side=(0.0 if i == 1 else None))
        pm.stencil(b, (fx + dx, fy + dy - 7.03, 6.0), (0, 0, 0), "F" + str(i + 1), height=2.2, tint=DARK)
    for k in range(3):
        pm.pipe_run(b, [(fx - 30.0, fy + 1.0 + k * 0.6, 0.8), (fx + 30.0, fy + 1.0 + k * 0.6, 0.8)], 0.2,
                    tint=(YELLOW, OFFWHITE, YELLOW)[k], supports=4.0)
    pm.pipe_run(b, [(fx - 32.0, fy + 1.6, 0.8), (fx - 40.0, fy + 1.6, 0.8), (fx - 40.0, -36.0, 0.8)], 0.25,
                tint=YELLOW, supports=4.0)
    pm.building(b, (fx - 44.0, fy + 10.0, 0), (0, 0, 90), 8.0, 6.0, floors=1, floor_h=3.4, mat=PANEL, tint=OFFWHITE,
                openings={"front": [(0.0, 1.2, 2.3, 0, 0)]}, parapet=0.4, roof_units=1, band=(ORANGE, 2.6, 0.3))
    b.marker("loot", (fx - 44.0, fy + 10.0, 0.3), table="fuel-barrel")
    b.marker("loot", (fx - 28.0, fy - 18.0, 0.0), table="fuel-barrel")
    b.marker("loot", (fx + 28.0, fy + 18.0, 0.0), table="fuel-barrel")
    b.piece("utility-truck", (fx - 52.0, fy - 6.0, 0), (0, 0, 90), tint=srgb(0.95, 0.75, 0.2))


def crash(b):
    cx, cy = CRASH
    rng = b.rng
    # The furrow ploughed by the lander, then the broken hull and its tail section.
    for k in range(7):
        t = k / 6
        px, py = cx + 24.0 + 26.0 * t, cy - 24.0 - 34.0 * t
        b.box((14.0 - 6.0 * t, 9.0, 0.05), (px, py, 0.03), (0, 0, 49.0 + rng.uniform(-4, 4)), mat=MATTE,
              tint=srgb(0.24, 0.2, 0.15), solid=False, vary=0.1)
        rubble(b, (px + 6.0, py + 3.0, 0.0), 2.0, count=3, tint=srgb(0.36, 0.3, 0.24), mat=MATTE, max_size=1.0)
    ship_hull(b, (cx, cy, 0), (0, 0, 229.0), length=48.0, width=14.0, height=10.0, stations=14, tint=OFFWHITE,
              band=ORANGE, damage=0.16, broken_at=0.66, buried=0.28, n=20)
    with b.frame((cx - 26.0, cy + 30.0, 0), (0, 0, 196.0)):
        ship_hull(b, (0, 0, 0), (0, 0, 0), length=18.0, width=11.0, height=8.0, stations=6, nose=0.0, tail=0.4,
                  tint=srgb(0.82, 0.82, 0.8), band=ORANGE, damage=0.12, buried=0.35, n=16)
        for k in range(3):
            b.lathe([(0.6, 0.0), (1.4, 1.6), (1.8, 3.0)], (-3.0 + k * 3.0, 9.5, 2.0), (90, 0, 0), mat=RUST,
                    tint=(1, 1, 1), sides=14, solid=False)
    b.cyl(9.0, 0.03, (cx - 6.0, cy + 8.0, 0.02), mat=MATTE, tint=srgb(0.1, 0.09, 0.08), sides=24, solid=False, vary=0)
    for k in range(16):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(6.0, 26.0)
        b.box((rng.uniform(1.2, 3.5), rng.uniform(0.8, 2.4), 0.12), (cx + math.cos(a) * r, cy + math.sin(a) * r, 0.2),
              (rng.uniform(-20, 20), rng.uniform(-20, 20), rng.uniform(0, 360)), mat=HULL, tint=OFFWHITE)
    for k in range(4):
        a = rng.uniform(0, 2 * math.pi)
        b.cyl(0.6, 0.9, (cx + math.cos(a) * 9.0, cy + math.sin(a) * 9.0, 0.45), mat=LAMP,
              tint=srgb(1.0, 0.45, 0.1), sides=8, radius2=0.1, solid=False)
    b.marker("loot", (cx - 8.0, cy + 10.0, 0.0), table="sealed-crate", holds="ship-parts")
    b.marker("loot", (cx + 4.0, cy - 4.0, 0.0), table="crate", holds="ship-parts")
    b.marker("loot", (cx - 24.0, cy + 26.0, 0.0), table="military-crate")
    b.marker("guard", (cx - 12.0, cy - 8.0, 0.0), 0.0, role="reclaimer-patrol")
    b.zone("radiation", (cx - 6.0, cy + 8.0, 4.0), radius=16.0, level=1, source="drive")


def grounds(b):
    rng = b.rng
    hx, hy = 196.0, 121.0
    fence(b, [(8.0, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0), (-hx, -hy, 0), (-8.0, -hy, 0)], post=5.0)
    for x in (-8.0, 8.0):
        b.box((1.0, 1.0, 3.0), (x, -hy, 1.5), mat=CONCRETE, tint=CONCRETE_T)
    road(b, [(0.0, -hy - 8.0, 0.0), (0.0, -105.0, 0.0)], width=8.0)
    road(b, [(10.0, -40.0, 0.0), (TOWER[0], TOWER[1] - 6.0, 0.0)], width=7.0, lines=False)
    road(b, [(-48.0, -40.0, 0.0), (-48.0, 15.0, 0.0)], width=14.0, lines=False)
    road(b, [(-112.0, -40.0, 0.0), (-112.0, 15.0, 0.0)], width=14.0, lines=False)
    road(b, [(100.0, -40.0, 0.0), (100.0, 64.0, 0.0), (102.0, 64.0, 0.0)], width=7.0, lines=False)
    # The radar mast, a windsock, lamp posts.
    from monkit.kit import lattice_tower
    lattice_tower(b, (6.0, 84.0, 0), 22.0, 3.0, 1.2, sections=5, member=0.12, tint=srgb(0.7, 0.71, 0.71))
    with b.frame((6.0, 84.0, 22.4), (0, 0, 35.0)):
        b.box((9.0, 0.3, 1.6), (0, 0, 0.8), tint=OFFWHITE)
        for k in range(8):
            b.box((0.1, 0.5, 1.4), (-4.0 + k * 1.1, -0.3, 0.8), tint=GREY, solid=False, vary=0)
        b.cyl(0.4, 0.6, (0, 0, -0.1), tint=DARK, sides=10)
    pm.aviation_lamp(b, (6.0, 84.0, 24.6), size=0.35)
    b.cyl(0.06, 7.0, (60.0, -112.0, 3.5), tint=GREY, sides=6, solid=False)
    b.cyl(0.45, 2.4, (61.1, -112.0, 6.6), (0, 82, 0), mat=PAINT, tint=ORANGE, sides=10, radius2=0.18, solid=False)
    for (x, y, a) in ((-160.0, -30.0, 180.0), (-80.0, -30.0, 180.0), (0.0, -30.0, 180.0), (80.0, -30.0, 180.0),
                      (160.0, -30.0, 180.0), (20.0, 50.0, 0.0), (-150.0, 90.0, 0.0), (150.0, 100.0, 0.0)):
        b.piece("lamp-post", (x, y, 0), (0, 0, a))
    for k in range(4):
        b.piece("container-20", (-160.0, 90.0 + k * 3.0, 0), (0, 0, 2 - k), tint=(OFFWHITE, srgb(0.2, 0.36, 0.52),
                                                                              srgb(0.78, 0.45, 0.16), OFFWHITE)[k])
    b.marker("loot", (-156.0, 86.0, 0.0), table="crate")
    b.marker("guard", (0.0, -100.0, 0.0), 180.0, role="reclaimer-sentry")
    b.marker("guard", (-80.0, 10.0, 0.0), 180.0, role="reclaimer-patrol")


def build(b):
    b.set_pad(("rect", 400.0, 250.0), ground="concrete", blend=20.0, clear=8.0)
    apron(b)
    hangar(b, (HANGARS[0][0], HANGARS[0][1], 0), ship=False, damage=0.25)
    hangar(b, (HANGARS[1][0], HANGARS[1][1], 0), ship=True)
    control_tower(b)
    fuel_farm(b)
    crash(b)
    grounds(b)
    b.zone("no-build", (0, 0, 0), size=(440.0, 290.0, 100.0))
    b.meta.update({"tier": "medium", "landmark": False, "family": "seedward-industrial", "spawn": "flat-ground"})


SUN = (30.0, 225.0)
PEOPLE = [((14.0, -46.0, 0), 20.0), ((TOWER[0] + 2.0, TOWER[1] - 2.0, 27.4), 180.0)]
SHIP = ((8.0, -36.0, 0.12), 200.0)
VIEWS = [
    {"name": "overview", "target": (0, 10, 10), "distance": 360, "yaw": 26, "pitch": 24, "lens": 32},
    {"name": "apron", "target": (0, 20, 12), "distance": 75, "yaw": 15, "pitch": -4, "lens": 26},
    {"name": "crash", "target": (128, 6, 6), "distance": 60, "yaw": 300, "pitch": 8, "lens": 28},
]
