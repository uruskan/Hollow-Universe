"""Crashed Skiff (MONUMENTS.md §5.1 no. 11, roadside tier; also an Orbital Fall's smallest wreck, §6 E1): a single-seat
utility skiff of the Tin Finch's class that came down short of somewhere. It ploughed a furrow across the ground and
stopped nose-first in the dirt it threw up, rolled onto one skid. Its right nacelle tore off and lies burnt further
on; plates are missing from the flanks, the canopy is smashed, the hatch hangs open. The cargo pod was thrown clear.
Ship parts in the wreck, a crate in the pod.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (DARK, GREY, ORANGE, TEAL, srgb)
from monkit.surfaces import GLASS, MATTE, PAINT, RUST

KEY = "crashed-skiff"
TITLE = "Crashed Skiff"
SEED = 1111
BUILDER = {"chunk": 32.0, "max_edge": 1.5}

YAW = 28.0          # the skiff's heading (its nose toward -Y turned this far)
PITCH = 21.0        # nose down
ROLL = -9.0
POS = (0.4, -1.0, -0.62)


def build(b):
    b.set_pad(("circle", 7.5), ground="dirt", blend=8.0, clear=2.0)
    fwd = Vector((math.sin(math.radians(YAW)), -math.cos(math.radians(YAW)), 0))     # the way it was going
    side = Vector((-fwd.y, fwd.x, 0))
    nose = Vector(POS[:2] + (0,)) + fwd * 2.3

    # The furrow behind it: two walls of thrown earth along a dark scar, the ground torn up ahead of the nose.
    start = nose - fwd * 15.0
    pts_l = [start + side * 0.9 + fwd * (k * 3.0) + side * (0.15 * math.sin(k * 1.3)) for k in range(6)]
    pts_r = [start - side * 0.9 + fwd * (k * 3.0) - side * (0.15 * math.sin(k * 1.7)) for k in range(6)]
    P.berm(b, [tuple(p.xy) for p in pts_l], width=1.1, height=0.3, tint=P.EARTH)
    P.berm(b, [tuple(p.xy) for p in pts_r], width=1.0, height=0.26, tint=P.EARTH)
    P.ribbon(b, [tuple((start + fwd * (k * 3.0)).xy) for k in range(6)], 1.3, 0.0, P.EARTH_DARK,
             mat="M_Mon_Concrete", step=1.0)
    ahead = [nose + fwd * 0.6 + side * s for s in (-2.2, -1.2, 0.0, 1.2, 2.2)]
    ahead = [a + fwd * (0.8 - 0.25 * abs(i - 2)) for i, a in enumerate(ahead)]
    P.berm(b, [tuple(p.xy) for p in ahead], width=1.8, height=0.65, tint=P.EARTH)
    P.clods(b, tuple(nose + fwd * 1.5), 4.5, count=18, tint=P.EARTH, size=0.45, inner=1.5)
    P.clods(b, tuple(start + fwd * 6.0), 4.0, count=10, tint=P.EARTH_DARK, size=0.35, inner=1.2)
    P.scorch(b, tuple(start + fwd * 4.0), 1.1)
    P.scorch(b, tuple(nose - fwd * 1.0), 1.6)

    # The skiff, nose in the dirt, rolled onto its left skid; the right nacelle and skid torn away.
    with b.frame(POS, (0, 0, YAW)):
        with b.frame((0, 0, 0), (-PITCH, ROLL, 0)):
            seat, stubs = P.skiff(b, (0, 0, 0), (0, 0, 0), code="SK-4 410", torn=("right",), hatch=58.0,
                                  panels_missing=0.3, burnt=0.6, skids=(True, False))
    b.marker("loot", tuple(seat), table="ship-parts")

    # The torn nacelle, burnt, at the end of its own scorched track; its pylon and skid pieces strewn between.
    nac = Vector(POS[:2] + (0,)) + fwd * 4.0 + side * 6.0
    with b.frame((nac.x, nac.y, 0.46), (0, 14.0, YAW + 64.0)):
        P.nacelle(b, (0, 0, 0), (0, 0, 0), burnt=True)
    P.scorch(b, (nac.x, nac.y, 0.0), 1.5)
    P.ribbon(b, [tuple((nac - fwd * 5.0 - side * 2.5).xy), tuple((nac - fwd * 2.0 - side * 0.8).xy),
                 tuple(nac.xy)], 0.6, 0.0, P.SOOT, step=1.0)
    b.marker("loot", (nac.x - 0.8, nac.y - 0.8, 0.05), table="ship-parts")
    with b.frame(tuple(nac - fwd * 2.4 - side * 1.6), (0, 0, YAW + 20.0)):
        b.tube([(1.0, -1.4, 0.1), (1.0, 1.0, 0.1), (1.0, 1.3, 0.22)], 0.09, mat=PAINT, tint=GREY, sides=8)
        b.beam((0.7, -0.4, 0.05), (1.0, -0.6, 0.18), 0.08, 0.08, mat=PAINT, tint=DARK)
    P.debris(b, tuple(nac - fwd * 3.0 - side * 2.0), 3.0, count=8, size=0.7, tint=P.HULL_WHITE, mat="M_Mon_Hull")
    P.debris(b, tuple(nose - fwd * 6.0), 3.5, count=8, size=0.6, tint=P.HULL_WHITE, mat="M_Mon_Hull")
    P.debris(b, tuple(nose - fwd * 3.0 + side * 1.5), 2.5, count=5, size=0.5, tint=P.BURNT, mat="M_Mon_Hull")
    # Glass from the canopy ahead of the nose.
    for k in range(14):
        a = b.rng.random() * math.tau
        r = 0.5 + 2.5 * b.rng.random()
        p = nose + fwd * 1.0 + Vector((math.cos(a), math.sin(a), 0)) * r
        b.box((0.05 + 0.1 * b.rng.random(), 0.04 + 0.08 * b.rng.random(), 0.006), (p.x, p.y, 0.03),
              (0, 0, b.rng.uniform(0, 360)), mat=GLASS, solid=False, vary=0)

    # The cargo pod, thrown clear and split: an orange case on its side, its lid off.
    pod = Vector(POS[:2] + (0,)) - fwd * 4.5 - side * 5.0
    with b.frame((pod.x, pod.y, 0.0), (0, 0, YAW - 35.0)):
        b.box((1.5, 0.8, 0.7), (0, 0, 0.35), (0, 0, 0), mat=PAINT, tint=ORANGE, bevel=0.04)
        b.box((1.54, 0.84, 0.08), (0, 0, 0.55), mat=PAINT, tint=DARK, solid=False)
        for x in (-0.6, 0.6):
            b.box((0.1, 0.86, 0.1), (x, 0, 0.35), mat=PAINT, tint=DARK, solid=False)
        P.stencil(b, "CARGO", (0, -0.405, 0.18), (0, 0, 0), 0.12, DARK, align="centre")
    with b.frame((pod.x + 1.3, pod.y - 0.6, 0.05), (4, 0, YAW + 20.0)):
        b.box((1.5, 0.8, 0.06), (0, 0, 0.03), mat=PAINT, tint=ORANGE, solid=False)
        b.box((1.3, 0.04, 0.03), (0, -0.2, 0.07), mat=PAINT, tint=DARK, solid=False)
    b.marker("loot", (pod.x, pod.y - 0.8, 0.05), table="crate")
    for k in range(3):
        b.box((0.45, 0.35, 0.3), (pod.x - 1.0 + 0.4 * k, pod.y + 0.9 + 0.3 * (k % 2), 0.15),
              (0, 0 if k != 1 else 90, 20 * k), mat=PAINT, tint=(srgb(0.3, 0.35, 0.3), GREY, TEAL)[k], solid=False)

    b.zone("no-build", (0, 0, 0), radius=16.0)
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "open", "event": "orbital-fall",
                   "family": "ships"})


SUN = (30.0, 230.0)
PEOPLE = [((-2.6, -6.0, 0.0), 40.0)]
VIEWS = [
    {"name": "furrow", "target": (0.0, -1.0, 1.0), "distance": 24, "yaw": -150, "pitch": 24, "lens": 30},
    {"name": "wreck", "target": (0.4, -1.2, 1.3), "distance": 10.5, "yaw": -104, "pitch": 7, "lens": 26},
    {"name": "finch", "target": (-3.5, 0.5, 1.4), "distance": 22, "yaw": -70, "pitch": 10, "lens": 30,
     "ship": ((-9.5, 3.5, 0.0), 120.0), "people": [((-6.4, 1.6, 0.0), 60.0)]},
]
