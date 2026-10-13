"""Stalled Crawler (MONUMENTS.md §5.1 no. 6, roadside tier): a Seedward six-wheeled overland crawler and its fuel tanker,
stopped mid-route on a dirt track and never moved again. The crew were changing the front wheel: the crawler stands on
a jack with the hub bare, the wheel lies in the grass beside a toolbox and cans; the cab door hangs open over its steps,
the hab's sliding door is pulled back, the light bar still points down the track. Fuel in the tanker, salvage in the
hab, a crate in the cab.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (DARK, GREY, RED, YELLOW, srgb)
from monkit.surfaces import MATTE, PAINT, RUST

KEY = "stalled-crawler"
TITLE = "Stalled Crawler"
SEED = 606
BUILDER = {"chunk": 32.0, "max_edge": 3.0}

C = Vector((-4.6, 0.4, 0.0))       # the crawler's centre; it faces -X, its right side (doors) toward -Y
YAW = -90.0


def build(b):
    b.set_pad(("rect", 30.0, 11.0), ground="dirt", blend=8.0, clear=2.0)

    # The track: two ruts worn into the dirt, a third set from an older pass.
    for off, w in ((-1.75, 0.7), (1.75, 0.7)):
        P.ribbon(b, [(-16.0, C.y + off + 0.2), (-4.0, C.y + off), (6.0, C.y + off - 0.15), (16.0, C.y + off + 0.1)],
                 w, 0.0, P.EARTH_DARK, mat="M_Mon_Concrete", step=2.0)
    for off in (-0.9, 0.9):
        P.ribbon(b, [(-16.0, 3.6 + off), (0.0, 3.3 + off), (16.0, 3.8 + off)], 0.4, 0.0, srgb(0.5, 0.42, 0.32),
                 mat="M_Mon_Concrete", step=2.0)

    # The crawler, its front right wheel off; the tanker on the hitch.
    cab, hitch = P.crawler(b, tuple(C), (0, 0, YAW), code="CR-09", missing=(1,))
    b.marker("loot", tuple(cab), table="crate")
    hub = C + Vector((-4.2, -1.75, 0))
    # Jack and block under the front arm, the wheel lying in the grass, tools and cans.
    b.box((0.5, 0.35, 0.2), (hub.x, hub.y + 0.55, 0.1), mat=PAINT, tint=P.WOOD)
    b.cyl(0.09, 0.42, (hub.x, hub.y + 0.55, 0.41), mat=PAINT, tint=RED, sides=10)
    b.cyl(0.035, 0.24, (hub.x, hub.y + 0.55, 0.74), mat=PAINT, tint=GREY, sides=8, solid=False)
    b.piece("wheel-crawler", (hub.x + 0.9, hub.y - 2.7, 0.31), (0, 90.0, 25.0))
    P.toolbox(b, (hub.x - 1.3, hub.y - 1.2, 0.0), 30.0, open_lid=True)
    b.box((0.5, 0.05, 0.03), (hub.x - 0.6, hub.y - 1.6, 0.02), (0, 0, 70), mat=PAINT, tint=GREY, solid=False)
    b.box((0.04, 0.6, 0.04), (hub.x - 0.2, hub.y - 0.9, 0.03), (0, 0, 15), mat=PAINT, tint=GREY, solid=False)
    for k, (dx, dy) in enumerate(((1.6, -1.3), (1.9, -1.0), (2.2, -1.5))):
        P.jerry_can(b, (hub.x + dx, hub.y + dy, 0.0), 80.0 + 25 * k, lying=k == 2)
    P.drape(b, (hub.x - 0.2, hub.y - 3.4, 0.0), (2.4, 1.6), 15.0, 0.08, 2.0, P.TARP_BLUE, cells=(6, 4))
    b.marker("loot", (hub.x + 0.9, hub.y - 2.7, 0.1), table="salvage")
    # A cold fire ring where they waited, a folding chair.
    fire = Vector((hub.x + 3.6, hub.y - 3.6, 0.0))
    for k in range(9):
        a = 2 * math.pi * k / 9
        b.box((0.26, 0.2, 0.16), (fire.x + math.cos(a) * 0.55, fire.y + math.sin(a) * 0.55, 0.07),
              (b.rng.uniform(-10, 10), 0, math.degrees(a)), mat="M_Mon_Concrete", tint=P.ROCK, solid=False)
    P.blob(b, (fire.x, fire.y, 0.0), 0.5, P.SCORCH, points=10, rough=0.2)
    for k in range(3):
        b.cyl(0.05, 0.7, (fire.x, fire.y, 0.08), (85, 0, 60 * k), mat=PAINT, tint=srgb(0.15, 0.12, 0.1), sides=6,
              solid=False)
    with b.frame((fire.x + 1.3, fire.y + 0.4, 0), (0, 0, 120)):
        for s in (-1, 1):
            b.beam((s * 0.22, -0.2, 0), (s * 0.22, 0.2, 0.45), 0.025, 0.025, mat=PAINT, tint=GREY)
            b.beam((s * 0.22, 0.2, 0), (s * 0.22, -0.2, 0.45), 0.025, 0.025, mat=PAINT, tint=GREY)
        b.box((0.46, 0.42, 0.02), (0, 0, 0.45), mat=MATTE, tint=srgb(0.2, 0.36, 0.42), solid=False)
        b.box((0.46, 0.02, 0.4), (0, 0.22, 0.68), (-10, 0, 0), mat=MATTE, tint=srgb(0.2, 0.36, 0.42), solid=False)
    # Salvage pulled out of the hab and left by its door.
    door = C + Vector((-1.0, -1.75, 0))
    b.box((0.8, 0.6, 0.5), (door.x + 0.3, door.y - 0.9, 0.25), (0, 0, 12), mat=PAINT, tint=srgb(0.3, 0.35, 0.3))
    b.box((0.6, 0.5, 0.35), (door.x - 0.5, door.y - 1.1, 0.18), (0, 0, -8), mat=PAINT, tint=GREY)
    b.marker("loot", (door.x, door.y - 1.4, 0.05), table="salvage")

    # The tanker, coupled and a few degrees off line.
    t_yaw = YAW + 5.0
    hl = 3.0
    d = Vector((math.sin(math.radians(t_yaw)), -math.cos(math.radians(t_yaw)), 0))     # its front (local -Y)
    centre = Vector((hitch.x, hitch.y, 0)) - d * (hl + 1.72)
    valve = P.tanker_trailer(b, tuple(centre), (0, 0, t_yaw), code="F-3")
    b.marker("loot", (valve.x, valve.y - 0.3, 0.05), table="fuel-barrel")
    b.marker("loot", (valve.x + 0.6, valve.y + 0.8, 0.05), table="fuel-barrel")
    b.marker("loot", (centre.x - 1.0, centre.y - 2.2, 0.05), table="barrel")
    P.blob(b, (valve.x, valve.y, 0.0), 0.9, srgb(0.04, 0.04, 0.04), points=12, rough=0.3, mat="M_Mon_Gloss")

    b.zone("no-build", (0, 0, 0), size=(40.0, 22.0, 20.0))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "track"})


SUN = (30.0, 200.0)
PEOPLE = [((-6.2, -3.6, 0.0), 10.0)]
VIEWS = [
    {"name": "track", "target": (0.5, 0.0, 1.6), "distance": 31, "yaw": -28, "pitch": 20, "lens": 30},
    {"name": "wheel", "target": (-7.6, -1.4, 1.6), "distance": 9.5, "yaw": -32, "pitch": 3, "lens": 24},
    {"name": "tanker", "target": (4.0, 0.3, 2.0), "distance": 15, "yaw": 68, "pitch": 4, "lens": 28},
]
