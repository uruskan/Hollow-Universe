"""Survey Marker (MONUMENTS.md §5.1 no. 7, roadside tier): one of the old expansion's survey stations on a summit or open
plain: a six-sided concrete plinth with its pillar and plate, an aluminium tripod over it carrying a beacon dome and a
pennant vane, guyed to stakes; a solar panel on a post feeding a battery box; and the sealed orange cache the surveyors
restocked. Using the station reveals the area on the map; the cache refills once per cycle.
"""
import math

from monkit import parts_roadside as P
from monkit.kit import (CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, TEAL, cable, srgb)
from monkit.surfaces import CONCRETE, GLOSS, LAMP, MATTE, PAINT, PANEL

KEY = "survey-marker"
TITLE = "Survey Marker"
SEED = 707
BUILDER = {"chunk": 32.0, "max_edge": 0.6}


def build(b):
    b.set_pad(("circle", 6.0), ground="gravel", blend=6.0, clear=1.5)

    # Plinth, pillar and the mounting plate.
    hexa = [(0.85 * math.cos(math.radians(60 * k + 30)), 0.85 * math.sin(math.radians(60 * k + 30))) for k in range(6)]
    b.slab(hexa, 0.42, (0, 0, -0.1), mat=CONCRETE, tint=CONCRETE_T, solid="hull")
    hexb = [(0.95 * x / 0.85, 0.95 * y / 0.85) for x, y in hexa]
    b.slab(hexb, 0.12, (0, 0, -0.06), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), solid=False)
    b.cyl(0.26, 0.97, (0, 0, 0.82), mat=CONCRETE, tint=CONCRETE_T, sides=16, radius2=0.2)
    b.cyl(0.24, 0.05, (0, 0, 1.345), mat=PAINT, tint=srgb(0.62, 0.5, 0.3), sides=16)
    b.cyl(0.04, 0.06, (0, 0, 1.39), mat=PAINT, tint=GREY, sides=8, solid=False)
    for k in range(3):
        a = math.radians(120 * k + 90)
        b.box((0.16, 0.03, 0.02), (0.12 * math.cos(a), 0.12 * math.sin(a), 1.375), (0, 0, math.degrees(a)),
              mat=PAINT, tint=DARK, solid=False, vary=0)
    P.plate(b, (0, -0.73, 0.21), (0, 0, 0), (0.6, 0.18), OFFWHITE, ORANGE, "SV 2-417", text_h=0.09)

    # The tripod: three aluminium legs to a head plate, the beacon dome, a vane and its pennant.
    top = 3.6
    feet = []
    for k in range(3):
        a = math.radians(120 * k - 90)
        foot = (1.55 * math.cos(a), 1.55 * math.sin(a), 0.0)
        feet.append(foot)
        b.beam(foot, (0.12 * math.cos(a), 0.12 * math.sin(a), top), 0.07, 0.07, mat=PAINT, tint=P.ALU, solid=True)
        b.box((0.3, 0.3, 0.06), (foot[0], foot[1], 0.03), (0, 0, math.degrees(a)), mat=PAINT, tint=GREY, solid=False)
        b.cyl(0.03, 0.4, (foot[0] * 1.05, foot[1] * 1.05, 0.12), mat=PAINT, tint=DARK, sides=6, solid=False)
        # A brace halfway up between each pair of legs.
        a2 = math.radians(120 * (k + 1) - 90)
        t = 0.45
        p0 = (foot[0] * (1 - t) + 0.12 * math.cos(a) * t, foot[1] * (1 - t) + 0.12 * math.sin(a) * t, top * t)
        p1 = (1.55 * math.cos(a2) * (1 - t) + 0.12 * math.cos(a2) * t,
              1.55 * math.sin(a2) * (1 - t) + 0.12 * math.sin(a2) * t, top * t)
        b.beam(p0, p1, 0.035, 0.035, mat=PAINT, tint=P.ALU)
    b.cyl(0.3, 0.08, (0, 0, top + 0.04), mat=PAINT, tint=GREY, sides=16)
    b.cyl(0.22, 0.3, (0, 0, top + 0.23), mat=PAINT, tint=OFFWHITE, sides=16)
    b.cyl(0.225, 0.08, (0, 0, top + 0.3), mat=PAINT, tint=ORANGE, sides=16, solid=False, vary=0)
    b.lathe([(0.22, 0.0), (0.2, 0.08), (0.14, 0.17), (0.0, 0.21)], (0, 0, top + 0.38), mat=MATTE,
            tint=P.SIGN_WHITE, sides=16)
    b.box((0.08, 0.08, 0.08), (0, 0, top + 0.62), mat=LAMP, tint=srgb(1.0, 0.45, 0.05), solid=False)
    b.marker("light", (0, 0, top + 0.75), colour=[1.0, 0.45, 0.05], range=40.0, blink=2.0)
    b.cyl(0.015, 1.4, (0.16, 0.0, top + 1.05), mat=PAINT, tint=GREY, sides=5, solid=False)
    b.box((0.02, 0.36, 0.22), (0.16, 0.2, top + 1.6), (0, 0, 8), mat=MATTE, tint=ORANGE, solid=False)
    b.box((0.02, 0.36, 0.06), (0.16, 0.2, top + 1.6), (0, 0, 8), mat=MATTE, tint=P.SIGN_WHITE, solid=False)
    b.marker("reveal", (0, -0.9, 0.0), scope="area", radius=900.0)
    # Guy lines from the head to three stakes with flagging tape.
    for k in range(3):
        a = math.radians(120 * k - 30)
        stake = (3.6 * math.cos(a), 3.6 * math.sin(a), 0.0)
        b.cyl(0.025, 0.55, (stake[0], stake[1], 0.2), (12, 0, math.degrees(a) + 90), mat=PAINT, tint=P.WOOD,
              sides=6, solid=False)
        b.box((0.02, 0.05, 0.18), (stake[0], stake[1] + 0.03, 0.42), (0, 0, math.degrees(a)), mat=MATTE, tint=ORANGE,
              solid=False)
        cable(b,(stake[0], stake[1], 0.42), (0.1 * math.cos(a), 0.1 * math.sin(a), top - 0.1), sag=0.01,
              radius=0.008, tint=srgb(0.85, 0.82, 0.7), segments=4)

    # Solar panel on a post, the battery box at its foot, the cable to the beacon.
    sp = (2.3, 1.6, 0.0)
    b.box((0.4, 0.4, 0.3), (sp[0], sp[1], 0.05), mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(0.05, 1.7, (sp[0], sp[1], 1.0), mat=PAINT, tint=GREY, sides=8)
    with b.frame((sp[0], sp[1], 1.9), (-35.0, 0, 200.0)):
        b.box((1.25, 0.82, 0.04), (0, 0, 0), mat=PAINT, tint=P.ALU, solid=False)
        b.box((1.18, 0.75, 0.02), (0, 0, 0.025), mat=GLOSS, tint=srgb(0.1, 0.13, 0.24), solid=False, vary=0)
        for x in (-0.3, 0.0, 0.3):
            b.box((0.01, 0.75, 0.005), (x, 0, 0.037), mat=PAINT, tint=GREY, solid=False, vary=0)
    with b.frame((sp[0] - 0.55, sp[1] - 0.2, 0.0), (0, 0, 20.0)):
        b.box((0.62, 0.42, 0.5), (0, 0, 0.28), mat=PANEL, tint=OFFWHITE, bevel=0.02)
        b.box((0.63, 0.43, 0.08), (0, 0, 0.4), mat=PAINT, tint=TEAL, solid=False, vary=0)
        b.box((0.66, 0.46, 0.04), (0, 0, 0.55), mat=PAINT, tint=LIGHTGREY, solid=False)
        P.stencil(b, "PWR", (0, -0.215, 0.12), (0, 0, 0), 0.1, DARK, align="centre")
    P.ground_cable(b, [(sp[0] - 0.4, sp[1] - 0.3, 0.3), (1.0, 0.9, 0.02), (0.15, 0.25, 0.02), (0.1, 0.12, 0.6)], 0.012,
                   DARK, wobble=0.1, step=0.4)

    # The sealed cache: an orange case on its own slab, latched, with a keypad.
    cx, cy = -2.0, 1.4
    b.box((1.3, 0.9, 0.16), (cx, cy, 0.04), mat=CONCRETE, tint=srgb(0.78, 0.77, 0.74))
    with b.frame((cx, cy, 0.12), (0, 0, -18.0)):
        b.box((1.0, 0.6, 0.52), (0, 0, 0.26), mat=PAINT, tint=ORANGE, bevel=0.03)
        b.box((1.04, 0.64, 0.06), (0, 0, 0.42), mat=PAINT, tint=DARK, solid=False)
        b.box((1.02, 0.62, 0.05), (0, 0, 0.545), mat=PAINT, tint=ORANGE, solid=False)
        for x in (-0.3, 0.3):
            b.box((0.1, 0.05, 0.14), (x, -0.32, 0.4), mat=PAINT, tint=GREY, solid=False)
        b.box((0.16, 0.02, 0.12), (0.0, -0.31, 0.25), mat=GLOSS, tint=DARK, solid=False, vary=0)
        b.box((0.04, 0.025, 0.02), (0.0, -0.315, 0.29), mat=LAMP, tint=srgb(0.2, 1.0, 0.4), solid=False, vary=0)
        P.stencil(b, "SV CACHE", (0, -0.305, 0.08), (0, 0, 0), 0.09, DARK, align="centre")
        for x in (-0.55, 0.55):
            b.box((0.08, 0.2, 0.05), (x, 0, 0.3), mat=PAINT, tint=DARK, solid=False)
    b.marker("loot", (cx, cy - 0.7, 0.05), table="survey-cache", respawn="cycle")

    # Marker stakes and a cairn, an old instrument case left open.
    for k, (x, y) in enumerate(((4.2, -1.0), (-1.2, -4.0), (-4.3, -0.8), (1.0, 4.1))):
        b.cyl(0.03, 1.0, (x, y, 0.45), mat=PAINT, tint=OFFWHITE, sides=6, solid=False)
        b.cyl(0.032, 0.18, (x, y, 0.85), mat=PAINT, tint=ORANGE, sides=6, solid=False, vary=0)
    for k in range(10):
        a = 2 * math.pi * k / 7
        r = 0.35 if k < 7 else 0.12
        b.box((0.32, 0.28, 0.2), (3.2 + math.cos(a) * r, -2.6 + math.sin(a) * r, 0.1 + (0.18 if k >= 7 else 0)),
              (b.rng.uniform(-10, 10), b.rng.uniform(-10, 10), b.rng.uniform(0, 90)), mat=CONCRETE, tint=P.ROCK,
              solid=False, vary=0.08)
    with b.frame((-1.0, -2.3, 0.0), (0, 0, 35.0)):
        b.box((0.55, 0.35, 0.18), (0, 0, 0.09), mat=PAINT, tint=srgb(0.22, 0.25, 0.25), solid=False)
        b.box((0.55, 0.35, 0.05), (0, 0.3, 0.2), (75, 0, 0), mat=PAINT, tint=srgb(0.22, 0.25, 0.25), solid=False)
        b.box((0.48, 0.28, 0.04), (0, 0, 0.17), mat=MATTE, tint=srgb(0.55, 0.12, 0.1), solid=False)

    b.zone("no-build", (0, 0, 0), radius=10.0)
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "summit"})


SUN = (30.0, 230.0)
PEOPLE = [((1.6, -2.4, 0.0), 30.0)]
VIEWS = [
    {"name": "station", "target": (0.0, 0.3, 2.0), "distance": 12.5, "yaw": 24, "pitch": 16, "lens": 30},
    {"name": "cache", "target": (-1.0, 0.8, 1.0), "distance": 5.5, "yaw": -38, "pitch": 6, "lens": 26},
    {"name": "summit", "target": (0.0, 0.0, 2.3), "distance": 15, "yaw": 150, "pitch": 2, "lens": 30},
]
