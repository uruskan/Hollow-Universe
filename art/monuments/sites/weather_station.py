"""Weather Station (MONUMENTS.md §5.1 no. 13, roadside tier): one of the Seedward survey's automatic weather stations on
open ground or a coast. A guyed instrument mast with cup anemometers and a vane, two louvred instrument screens (one
blown over), a rain gauge and an evaporation pan; a small radar dome on a lattice tower that once watched for storms;
the solar shed with panels on its roof, batteries and the logger. Inside a low wire fence. Tech scrap from the logger;
its terminal still predicts storms on the map.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED_LAMP, TEAL, YELLOW, cable, ladder, rail_loop,
                        srgb)
from monkit.surfaces import CONCRETE, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL

KEY = "weather-station"
TITLE = "Weather Station"
SEED = 1313
BUILDER = {"chunk": 32.0, "max_edge": 1.5}

WHITE = P.SIGN_WHITE


def screen(b, at, yaw=0.0, toppled=False):
    """A louvred instrument screen on four legs: a white box with slanted louvres on every side, a double roof, a
    door at the front; or, `toppled`, lying on its side with its legs in the air."""
    x, y, z = at
    rot = (0, 0, yaw) if not toppled else (0, 88.0, yaw)
    lift = 0.0 if not toppled else 0.42
    with b.frame((x, y, z + lift), rot):
        hz = 1.1
        for sx in (-0.33, 0.33):
            for sy in (-0.25, 0.25):
                b.box((0.05, 0.05, hz), (sx, sy, hz / 2), mat=PAINT, tint=WHITE, solid=False, vary=0)
        w, d, h = 0.8, 0.6, 0.62
        z0 = hz
        b.box((w, d, 0.04), (0, 0, z0 + 0.02), mat=PAINT, tint=WHITE)
        for (cx, cy, length, yaw2) in ((0, -d / 2, w, 0), (0, d / 2, w, 180), (-w / 2, 0, d, 90), (w / 2, 0, d, -90)):
            with b.frame((cx, cy, z0), (0, 0, yaw2)):
                for k in range(7):
                    b.box((length - 0.04, 0.012, 0.09), (0, 0.0, 0.07 + k * 0.08), (-40, 0, 0), mat=PAINT,
                          tint=WHITE, solid=False, vary=0)
                for sx in (-length / 2 + 0.02, length / 2 - 0.02):
                    b.box((0.04, 0.04, h), (sx, 0, h / 2), mat=PAINT, tint=WHITE, solid=False, vary=0)
        b.box((w + 0.12, d + 0.14, 0.04), (0, 0, z0 + h + 0.03), mat=PAINT, tint=WHITE)
        b.box((w + 0.16, d + 0.2, 0.04), (0, 0.02, z0 + h + 0.12), (-6, 0, 0), mat=PAINT, tint=WHITE)
        b.box((0.06, 0.03, 0.03), (0.3, -d / 2 - 0.03, z0 + h * 0.5), mat=PAINT, tint=DARK, solid=False)
        b.box((0.14, 0.14, 0.4), (0, 0, z0 + 0.3), mat=PAINT, tint=GREY, solid=False)
    if not toppled:
        b.collide_box(Vector((x, y, z + 0.9)), (0.9, 0.7, 1.8), (0, 0, yaw))


def anemometer(b, at, cups=True, vane_yaw=0.0):
    """A cup anemometer (three arms, three cups) or a wind vane, on a short spindle at `at`."""
    x, y, z = at
    b.cyl(0.025, 0.3, (x, y, z + 0.15), mat=PAINT, tint=GREY, sides=6, solid=False)
    b.cyl(0.05, 0.08, (x, y, z + 0.32), mat=PAINT, tint=DARK, sides=8, solid=False)
    if cups:
        for k in range(3):
            a = math.radians(120 * k + 20)
            tip = (x + math.cos(a) * 0.22, y + math.sin(a) * 0.22, z + 0.36)
            b.beam((x, y, z + 0.36), tip, 0.012, 0.012, mat=PAINT, tint=GREY)
            b.cyl(0.055, 0.07, tip, (0, 90, math.degrees(a) + 90), mat=PAINT, tint=WHITE, sides=8, radius2=0.02,
                  solid=False)
    else:
        with b.frame((x, y, z + 0.36), (0, 0, vane_yaw)):
            b.box((0.5, 0.012, 0.012), (0.05, 0, 0), mat=PAINT, tint=GREY, solid=False)
            b.box((0.16, 0.01, 0.14), (0.26, 0, 0.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
            b.cyl(0.03, 0.08, (-0.2, 0, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=6, solid=False)


def build(b):
    b.set_pad(("rect", 17.0, 15.0), ground="gravel", blend=8.0, clear=2.0)

    # Low fence: timber posts and three wires, a gate gap, the posts a little out of true.
    fx, fy = 7.5, 6.5
    corners = [(1.4, -fy), (fx, -fy), (fx, fy), (-fx, fy), (-fx, -fy), (-0.4, -fy)]
    for a, c in zip(corners, corners[1:]):
        a, c = Vector(a + (0,)), Vector(c + (0,))
        n = max(1, int((c - a).length / 2.5))
        for k in range(n + 1):
            p = a + (c - a) * (k / n)
            b.box((0.1, 0.1, 1.3), (p.x, p.y, 0.6), (b.rng.uniform(-3, 3), b.rng.uniform(-3, 3), 0), mat=PAINT,
                  tint=P.WOOD_GREY, solid=False, vary=0.05)
        for z in (0.35, 0.75, 1.1):
            cable(b, (a.x, a.y, z), (c.x, c.y, z), sag=0.01, radius=0.006, tint=srgb(0.5, 0.5, 0.5), segments=4)
        mid = (a + c) / 2
        yaw = math.degrees(math.atan2(c.y - a.y, c.x - a.x))
        b.collide_box(Vector((mid.x, mid.y, 0.65)), ((c - a).length, 0.1, 1.3), (0, 0, yaw))
    P.plate(b, (4.0, -fy - 0.06, 0.9), (0, 0, 0), (0.7, 0.3), WHITE, ORANGE, "WX-07", text_h=0.13)

    # The instrument mast, guyed, its anemometers and vane on a crossarm; a lightning rod and a red lamp.
    mx, my, H = -4.0, 2.6, 10.0
    b.box((0.8, 0.8, 0.4), (mx, my, 0.1), mat=CONCRETE, tint=srgb(0.78, 0.77, 0.74))
    b.box((0.3, 0.3, 0.3), (mx, my, 0.45), mat=PAINT, tint=GREY, solid=False)
    b.cyl(0.08, 0.3, (mx, my, 0.45), (0, 90, 0), mat=PAINT, tint=DARK, sides=10, solid=False)
    b.cyl(0.07, H - 0.3, (mx, my, 0.3 + (H - 0.3) / 2), mat=PAINT, tint=P.GALV, sides=10, radius2=0.05)
    for z in (5.0, 9.0):
        for k in range(3):
            a = math.radians(120 * k + 30)
            ax, ay = mx + math.cos(a) * 4.0, my + math.sin(a) * 4.0
            cable(b, (ax, ay, 0.15), (mx, my, z), sag=0.003, radius=0.006, tint=srgb(0.55, 0.55, 0.55), segments=4)
            if z == 5.0:
                b.cyl(0.04, 0.5, (ax, ay, 0.12), (20, 0, math.degrees(a) + 90), mat=PAINT, tint=GREY, sides=6,
                      solid=False)
    with b.frame((mx, my, H - 0.3), (0, 0, 35.0)):
        b.box((1.6, 0.05, 0.05), (0, 0, 0), mat=PAINT, tint=GREY, solid=False)
        anemometer(b, (0.78, 0, 0.02), cups=True)
        anemometer(b, (-0.78, 0, 0.02), cups=False, vane_yaw=20.0)
    anemometer(b, (mx, my, H), cups=True)
    b.cyl(0.012, 1.2, (mx + 0.08, my, H + 0.8), mat=PAINT, tint=srgb(0.72, 0.45, 0.28), sides=5, solid=False)
    b.box((0.12, 0.12, 0.14), (mx - 0.1, my, H - 0.6), mat=LAMP, tint=RED_LAMP, solid=False)
    b.marker("light", (mx - 0.2, my, H - 0.6), colour=[1.0, 0.12, 0.05], range=200.0, blink=1.0)
    for z, yaw in ((2.2, 0.0), (6.5, 120.0)):
        with b.frame((mx, my, z), (0, 0, yaw)):
            b.box((0.5, 0.04, 0.04), (0.25, 0, 0), mat=PAINT, tint=GREY, solid=False)
            b.cyl(0.07, 0.3, (0.5, 0, -0.1), mat=PAINT, tint=WHITE, sides=10, solid=False)
            for k in range(4):
                b.cyl(0.085, 0.02, (0.5, 0, -0.2 + 0.07 * k), mat=PAINT, tint=WHITE, sides=10, solid=False, vary=0)
    b.tube([(mx + 0.08, my, 3.0), (mx + 0.08, my, 0.6), (mx + 0.3, my - 0.4, 0.05), (2.0, -1.8, 0.05)], 0.015,
           tint=DARK, sides=4)

    # The screens, one standing, one blown over with louvres in the gravel; rain gauge and evaporation pan.
    screen(b, (-3.0, -2.6, 0.0), 10.0)
    screen(b, (-0.6, -4.2, 0.0), 63.0, toppled=True)
    P.debris(b, (0.2, -4.6, 0.0), 1.2, count=6, size=0.5, tint=WHITE, thick=0.012)
    b.cyl(0.13, 0.4, (0.8, 0.6, 0.3), mat=PAINT, tint=srgb(0.7, 0.7, 0.68), sides=14)
    b.cyl(0.1, 0.05, (0.8, 0.6, 0.52), mat=PAINT, tint=srgb(0.7, 0.7, 0.68), sides=14, radius2=0.13, cap=False,
          solid=False)
    b.box((0.4, 0.4, 0.1), (0.8, 0.6, 0.05), mat=CONCRETE, tint=srgb(0.78, 0.77, 0.74))
    with b.frame((-5.4, -3.6, 0.0), (0, 0, 0)):
        b.box((1.8, 1.8, 0.15), (0, 0, 0.075), mat=PAINT, tint=P.WOOD_GREY)
        b.cyl(0.62, 0.26, (0, 0, 0.28), mat=PAINT, tint=srgb(0.8, 0.8, 0.79), sides=24, cap=False)
        b.cyl(0.6, 0.02, (0, 0, 0.32), mat=GLOSS, tint=srgb(0.15, 0.2, 0.22), sides=24, solid=False)
        b.cyl(0.6, 0.02, (0, 0, 0.17), mat=PAINT, tint=srgb(0.7, 0.7, 0.68), sides=24, solid=False)
        b.cyl(0.03, 0.3, (0.3, 0.2, 0.35), mat=PAINT, tint=GREY, sides=6, solid=False)

    # The radar dome on its lattice tower, a platform and ladder; two of the dome's panels gone.
    rx, ry, th = 3.6, 2.8, 4.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((0.6, 0.6, 0.4), (rx + sx * 1.0, ry + sy * 1.0, 0.1), mat=CONCRETE, tint=srgb(0.78, 0.77, 0.74))
    with b.frame((rx, ry, 0.3), (0, 0, 0)):
        P.lattice(b, 0.0, th - 0.3, 2.0, 1.7, 3, member=0.09, tint=LIGHTGREY, solid=True)
    b.box((2.6, 2.6, 0.06), (rx, ry, th + 0.03), mat=GRATE, tint=GREY, solid=True, vary=0)
    rail_loop(b, [(rx - 1.3, ry - 1.3, th + 0.06), (rx + 1.3, ry - 1.3, th + 0.06), (rx + 1.3, ry + 1.3, th + 0.06),
                  (rx - 1.3, ry + 1.3, th + 0.06)], tint=YELLOW, gap=0)
    ladder(b, (rx - 0.5, ry - 1.12, 0.0), th + 0.06, (0, 0, 0), cage=True)
    b.cyl(0.95, 0.4, (rx, ry, th + 0.26), mat=PAINT, tint=GREY, sides=20)
    zc = th + 0.46 + 1.25
    P.ball(b, (rx, ry, zc), 1.42, WHITE, mat=PANEL, sides=20, rings=10, solid=True)
    b.cyl(1.43, 0.06, (rx, ry, zc - 0.2), mat=PAINT, tint=TEAL, sides=20, solid=False, vary=0)
    for a, el in ((40.0, 25.0), (65.0, 5.0)):
        ra, re = math.radians(a), math.radians(el)
        n = Vector((math.cos(ra) * math.cos(re), math.sin(ra) * math.cos(re), math.sin(re)))
        c = Vector((rx, ry, zc)) + n * 1.425
        rot = n.to_track_quat("Y", "Z").to_euler()
        b.box((0.55, 0.02, 0.5), tuple(c), tuple(math.degrees(v) for v in rot), mat=MATTE,
              tint=srgb(0.04, 0.04, 0.04), solid=False, vary=0)
    b.marker("landmark", (rx, ry, zc + 1.4), kind_of="radar-dome")

    # The solar shed: panels on the roof, batteries and the logger inside; an antenna on a pole.
    floor = P.prefab_hut(b, (3.6, -3.4, 0), (0, 0, 0), w=3.4, d=2.6, h=2.5, code="WX-07", door_open=70.0,
                         band=TEAL, windows=1)
    top = 0.3 + 2.5 + 0.12
    with b.frame((3.6, -3.4, top), (0, 0, 0)):
        for x in (-1.3, 1.3):
            for y in (-0.9, 0.9):
                b.box((0.06, 0.06, 0.25 + (0.5 if y > 0 else 0.0)), (x, y, (0.25 + (0.5 if y > 0 else 0)) / 2),
                      mat=PAINT, tint=GREY, solid=False)
        for k, x in enumerate((-0.85, 0.0, 0.85)):
            with b.frame((x, 0.0, 0.52), (17.0, 0, 0)):
                b.box((0.82, 2.0, 0.04), (0, 0, 0), mat=PAINT, tint=P.ALU, solid=False)
                if k != 1:
                    b.box((0.76, 1.94, 0.02), (0, 0, 0.025), mat=GLOSS, tint=srgb(0.1, 0.13, 0.24), solid=False,
                          vary=0)
                else:
                    b.box((0.76, 1.1, 0.02), (0, -0.4, 0.025), mat=GLOSS, tint=srgb(0.1, 0.13, 0.24), solid=False,
                          vary=0)
    with b.frame((3.6, -3.4, 0.3), (0, 0, 0)):
        for k in range(4):
            b.box((0.36, 0.26, 0.32), (-1.2 + 0.4 * k, 0.9, 0.16), mat=PAINT, tint=DARK, solid=False)
            b.box((0.08, 0.04, 0.04), (-1.2 + 0.4 * k, 0.9, 0.34), mat=PAINT, tint=ORANGE,
                  solid=False, vary=0)
        b.box((0.7, 0.35, 1.2), (1.1, 0.95, 0.9), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
        b.box((0.4, 0.02, 0.25), (1.1, 0.77, 1.2), mat=LAMP, tint=srgb(0.4, 0.95, 0.6), solid=False, vary=0)
        b.marker("terminal", (1.1, 0.4, 0.0), gives="storm-forecast")
    b.marker("loot", (2.6, -2.9, 0.32), table="tech-scrap")
    b.marker("loot", (3.9, -3.9, 0.32), table="crate")
    b.cyl(0.04, 4.0, (5.9, -2.2, 2.0), mat=PAINT, tint=GREY, sides=6)
    P.microwave_dish(b, (5.9, -2.5, 3.6), yaw=200.0, radius=0.3)
    b.cyl(0.01, 1.4, (5.9, -2.2, 4.7), mat=PAINT, tint=DARK, sides=4, solid=False)
    b.marker("loot", (-6.4, 5.4, 0.05), table="barrel")

    b.zone("no-build", (0, 0, 0), radius=14.0)
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "open-coast"})


SUN = (32.0, 220.0)
PEOPLE = [((1.2, -5.4, 0.0), 20.0)]
VIEWS = [
    {"name": "station", "target": (0.0, 0.5, 4.0), "distance": 25, "yaw": 28, "pitch": 15, "lens": 30},
    {"name": "screens", "target": (-1.8, -2.4, 1.4), "distance": 7.0, "yaw": -18, "pitch": 6, "lens": 26},
    {"name": "dome", "target": (2.0, 0.0, 3.4), "distance": 14, "yaw": 150, "pitch": 6, "lens": 28},
]
