"""Drop Pod (MONUMENTS.md §5.1 no. 4, roadside tier; also the Supply Drop event, §6 E2): a Bulwark supply pod that came
down hard in open ground and sank to its waist in a scorched crater. Its four petals blew open on landing: three lie
spread on the crater floor, burnt black outside and olive inside, the fourth tore off and was thrown over the rim. The
cargo core stands in the middle with the military crate still strapped in; the top cap keeps its parachute housing,
retro nozzles and a beacon that still blinks. The canopy lies collapsed beyond the rim, its lines run back to the pod.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, DARK, GREY, OLIVE, ORANGE, YELLOW, srgb)
from monkit.surfaces import HULL, LAMP, MATTE, PAINT, RUST

KEY = "drop-pod"
TITLE = "Drop Pod"
SEED = 404
BUILDER = {"chunk": 32.0, "max_edge": 2.0}

R = 1.35            # pod radius
PETAL_H = 2.5       # petal height
DECK = 0.28         # the pod's floor, at ground level (its lower half is buried)
CAP = 2.9           # underside of the top cap
BURNT = srgb(0.16, 0.13, 0.11)
OLIVE_IN = srgb(0.40, 0.47, 0.22)


def petal(b, centre_deg, opened, tint_out=BURNT, tint_in=OLIVE_IN, code=None):
    """One petal: a quarter of the pod's shell, hinged at its foot on the deck rim, fallen outward by `opened`
    degrees. Built in a frame whose -Y is the petal's outward direction."""
    with b.frame((0, 0, 0), (0, 0, centre_deg + 90.0)):
        with b.frame((0, -R, DECK), (opened, 0, 0)):
            with b.frame((0, R, 0), (0, 0, 0)):
                # Outer heat-shield skin and the olive liner, ribs and a stiffened edge.
                P.arc_panel(b, R, 225.0, 315.0, 0.0, PETAL_H, 0.05, 10, mat=HULL, tint=tint_out, wear=0.8)
                P.arc_panel(b, R - 0.05, 226.0, 314.0, 0.05, PETAL_H - 0.05, 0.03, 10, mat=PAINT, tint=tint_in)
                for z in (0.5, 1.25, 2.0):
                    P.arc_panel(b, R - 0.08, 228.0, 312.0, z - 0.04, z + 0.04, 0.09, 8, mat=PAINT, tint=GREY)
                for a in (236.0, 304.0):
                    ra = math.radians(a)
                    b.box((0.08, 0.1, PETAL_H - 0.2), ((R - 0.12) * math.cos(ra), (R - 0.12) * math.sin(ra),
                                                       PETAL_H / 2), (0, 0, a + 90.0), mat=PAINT, tint=GREY,
                          solid=False)
                # Hinge knuckles at the foot, a hazard band at the tip, the stencilled code.
                for a in (240.0, 300.0):
                    ra = math.radians(a)
                    b.cyl(0.07, 0.3, (R * math.cos(ra), R * math.sin(ra), 0.04), (0, 90, a + 90.0), mat=PAINT,
                          tint=DARK, sides=8, solid=False)
                P.arc_panel(b, R - 0.08, 232.0, 308.0, PETAL_H - 0.35, PETAL_H - 0.12, 0.012, 8, mat=PAINT,
                            tint=YELLOW)
                if code:
                    P.stencil(b, code, (0, -(R - 0.085), 1.55), (0, 0, 180.0), 0.2, ORANGE, align="centre")
                # Its ram, still braced against the deck.
                b.cyl(0.035, 1.1, (0, -(R - 0.3), 0.9), (35, 0, 0), mat=PAINT, tint=srgb(0.75, 0.76, 0.76),
                      sides=8, solid=False)
                b.cyl(0.05, 0.8, (0, -(R - 0.55), 0.45), (35, 0, 0), mat=PAINT, tint=DARK, sides=8, solid=False)


def build(b):
    b.set_pad(("circle", 8.0), ground="dirt", blend=8.0, clear=4.0)

    # The crater: a ragged rim of thrown earth, the floor burnt, streaks and clods flung out across the grass.
    P.crater(b, (0, 0, 0), 5.0, 0.62, inner_r=1.8, outer_r=6.5, rough=0.35)
    P.blob(b, (0, 0, 0.0), 4.4, P.SOOT, points=20, rough=0.22, thick=0.01)
    P.blob(b, (0, 0, 0.006), 3.4, P.SCORCH, points=18, rough=0.28, thick=0.01)
    P.clods(b, (0, 0, 0), 9.5, count=34, tint=P.EARTH, size=0.55, inner=5.4)
    P.clods(b, (0, 0, 0), 12.0, count=14, tint=P.EARTH_DARK, size=0.3, inner=8.0)
    P.clods(b, (0, 0, 0), 4.0, count=8, tint=srgb(0.2, 0.17, 0.14), size=0.4, inner=2.4)

    # The pod, tilted where it struck: deck, cargo core, top cap; petals spread; one thrown clear.
    with b.frame((0.2, -0.1, 0), (6.0, -4.0, 18.0)):
        b.cyl(R, 0.9, (0, 0, DECK - 0.45), mat=HULL, tint=BURNT, sides=28, wear=0.8)        # the buried hull's lip
        b.cyl(R - 0.06, 0.12, (0, 0, DECK - 0.06), mat=PAINT, tint=DARK, sides=28)          # the deck
        b.cyl(R + 0.04, 0.1, (0, 0, DECK - 0.12), mat=PAINT, tint=srgb(0.3, 0.3, 0.28), sides=28, solid=False)
        # Four posts of the cargo core between the petals, cross straps holding the load.
        for k in range(4):
            a = math.radians(90 * k)
            x, y = math.cos(a) * (R - 0.25), math.sin(a) * (R - 0.25)
            b.box((0.14, 0.14, CAP - DECK), (x, y, (DECK + CAP) / 2), (0, 0, 90 * k), mat=PAINT, tint=OLIVE)
            P.stencil(b, "BW", (x * 1.07, y * 1.07, 2.2), (0, 0, 90 * k + 90.0), 0.14, ORANGE, align="centre")
        for k in range(4):
            a0, a1 = math.radians(90 * k), math.radians(90 * (k + 1))
            for z in (0.75, 1.35):
                b.beam((math.cos(a0) * (R - 0.3), math.sin(a0) * (R - 0.3), z),
                       (math.cos(a1) * (R - 0.3), math.sin(a1) * (R - 0.3), z + 0.05), 0.05, 0.012, mat=MATTE,
                       tint=srgb(0.55, 0.5, 0.25))
        b.box((1.3, 1.0, 0.12), (0, 0, DECK + 0.06), mat=PAINT, tint=srgb(0.36, 0.3, 0.22), solid=False)
        b.marker("loot", (0, 0, DECK + 0.12), table="military-crate")
        # Top cap: parachute housing, retro nozzles, lifting eyes, the beacon and its whip.
        b.cyl(R + 0.02, 0.4, (0, 0, CAP + 0.2), mat=HULL, tint=srgb(0.3, 0.32, 0.26), sides=28, wear=0.7)
        b.cyl(R + 0.03, 0.12, (0, 0, CAP + 0.1), mat=PAINT, tint=YELLOW, sides=28, solid=False, vary=0)
        b.lathe([(R, 0.0), (R * 0.82, 0.3), (0.75, 0.42), (0.0, 0.45)], (0, 0, CAP + 0.4), mat=HULL,
                tint=srgb(0.26, 0.28, 0.22), sides=24)
        b.cyl(0.62, 0.5, (0, 0, CAP + 1.05), mat=PAINT, tint=OLIVE, sides=18)
        b.cyl(0.66, 0.06, (0, 0, CAP + 1.3), mat=PAINT, tint=DARK, sides=18, solid=False)
        for k in range(4):
            a = math.radians(45 + 90 * k)
            x, y = math.cos(a) * (R - 0.3), math.sin(a) * (R - 0.3)
            b.lathe([(0.06, 0.0), (0.12, 0.18), (0.2, 0.42)], (x, y, CAP + 0.52), mat=RUST, tint=(1, 1, 1),
                    sides=10, close=False, solid=False)
            b.box((0.05, 0.16, 0.14), (math.cos(a + 0.8) * 0.9, math.sin(a + 0.8) * 0.9, CAP + 0.82),
                  (0, 0, math.degrees(a + 0.8)), mat=PAINT, tint=GREY, solid=False)
        b.box((0.2, 0.2, 0.16), (0.3, 0.25, CAP + 1.38), mat=LAMP, tint=srgb(1.0, 0.35, 0.05), solid=False)
        b.marker("light", (0.3, 0.25, CAP + 1.55), colour=[1.0, 0.35, 0.05], range=60.0, blink=0.6)
        b.cyl(0.015, 2.2, (-0.3, -0.2, CAP + 2.4), mat=PAINT, tint=BLACK, sides=5, solid=False)
        riser = b.world((0.0, 0.0, CAP + 1.35))
        # Petals: three spread out, the fourth gone.
        for k, (deg, opened, code) in enumerate(((45.0, 64.0, "BW 7-11"), (135.0, 71.0, None),
                                                 (315.0, 58.0, "BW 7-11"))):
            petal(b, deg, opened, code=code)
        # The torn hinge where the fourth petal was.
        for a in (205.0, 245.0):
            ra = math.radians(a)
            b.box((0.14, 0.3, 0.12), (R * math.cos(ra), R * math.sin(ra), DECK + 0.05), (25, 0, a), mat=RUST,
                  tint=(1, 1, 1), solid=False)
    # The fourth petal, thrown over the rim and lying on its back in the grass.
    with b.frame((-6.2, -6.3, 0.06), (90.0, 5.0, 33.0)):
        with b.frame((0, R, 0), (0, 0, 0)):
            P.arc_panel(b, R, 225.0, 315.0, 0.0, PETAL_H, 0.05, 10, mat=HULL, tint=BURNT, wear=0.8, solid=True)
            P.arc_panel(b, R - 0.05, 226.0, 314.0, 0.05, PETAL_H - 0.05, 0.03, 10, mat=PAINT, tint=OLIVE_IN)
    P.scorch(b, (-6.6, -4.4, 0), 1.1)
    P.debris(b, (-3.5, -2.8, 0), 3.5, count=7, size=0.6, tint=BURNT, mat=HULL)
    P.debris(b, (2.0, 3.0, 0), 4.0, count=5, size=0.5, tint=srgb(0.4, 0.4, 0.38))

    # The canopy, collapsed beyond the rim to the north-east, its lines run back to the riser on the cap.
    c = Vector((9.6, 7.4, 0.0))
    yaw = 38.0
    W = 6.0
    strips = 6
    pos = None
    for k in range(strips):
        pos = P.drape(b, tuple(c), (11.0, W), yaw, 0.65, 6.0, ORANGE if k % 2 == 0 else P.SIGN_WHITE,
                      cells=(18, 2), part=(k / strips, (k + 1) / strips), seed=0.37, taper=0.6, bend=2.2)
    for k in range(9):
        v = 0.1 + 0.8 * k / 8
        start = Vector(pos(0.0, v, 0.06))
        mid = start.lerp(Vector((riser.x, riser.y, 0.0)), 0.55)
        P.ground_cable(b, [tuple(start), (mid.x, mid.y, 0.02), (riser.x + 1.5, riser.y + 1.2, 0.9),
                           (riser.x, riser.y, riser.z)], 0.012, srgb(0.85, 0.82, 0.72), wobble=0.25, step=0.8)

    b.zone("no-build", (0, 0, 0), radius=16.0)
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "open", "event": "supply-drop"})


SUN = (32.0, 235.0)
PEOPLE = [((-3.2, -7.6, 0.0), 30.0)]
VIEWS = [
    {"name": "crater", "target": (1.0, 1.0, 0.8), "distance": 27, "yaw": 22, "pitch": 30, "lens": 32},
    {"name": "rim", "target": (0.2, 0.0, 1.5), "distance": 12.5, "yaw": -38, "pitch": 6, "lens": 26},
    {"name": "canopy", "target": (2.0, 2.0, 1.0), "distance": 24, "yaw": 210, "pitch": 12, "lens": 30},
]
