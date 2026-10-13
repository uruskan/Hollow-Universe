"""Atmosphere Processor (MONUMENTS.md §5.5, landmark tier): one of the old expansion's terraforming towers, 860 m
tall, standing in a walled works 1.3 km across. Four buttresses carry a tapering shaft; intake halls ring its foot;
a crown of emitters at the top still blinks. The shaft is a vertical dungeon (the climb, §5.5); the works round it
hold processing halls, pipe bridges, a power yard and a landing apron. Seen from 40+ km in thick air.
"""
import math

import numpy as np

from monkit.kit import (CONCRETE, CONCRETE_T, DARK, FORMWORK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW,
                        cable, fence, lattice_tower, rail_loop, railing, srgb, stripes)
from monkit.structures import chimney, conveyor, road, shed, silo, wall
from monkit.surfaces import CORRUGATED, GLASS, GRATE, HULL, LAMP, PANEL, RUST

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
    # Crown: emitter ring, antenna mast, aviation lamps.
    b.cyl(TOP_R + 10.0, 14.0, (0, 0, H - 7.0), mat=PAINT, tint=DARK, sides=48)
    for k in range(16):
        ang = 2 * math.pi * k / 16
        b.box((4.0, 4.0, 22.0), (math.cos(ang) * (TOP_R + 6), math.sin(ang) * (TOP_R + 6), H + 11.0),
              (0, 0, math.degrees(ang)), mat=HULL, tint=OFFWHITE)
        b.box((2.0, 2.0, 2.0), (math.cos(ang) * (TOP_R + 6), math.sin(ang) * (TOP_R + 6), H + 23.0), mat=LAMP,
              tint=srgb(1.0, 0.12, 0.05), solid=False)
    b.cyl(3.0, 90.0, (0, 0, H + 45.0), mat=PAINT, tint=GREY, sides=16, radius2=1.2)
    b.box((3.0, 3.0, 3.0), (0, 0, H + 91.0), mat=LAMP, tint=srgb(1.0, 0.12, 0.05), solid=False)
    b.marker("light", (0, 0, H + 92.0), colour=[1.0, 0.12, 0.05], range=2000.0, blink=1.5)
    b.marker("landmark", (0, 0, H + 92.0), kind_of="atmosphere-processor", visible_km=60)

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
