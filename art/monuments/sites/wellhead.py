"""Wellhead (MONUMENTS.md §5.1 no. 8, roadside tier): a pump jack still nodding over an old well on the plains, in the
Seedward industrial yellow: Samson post, walking beam and horsehead, the bridle and polished rod into the wellhead, the
cranks and counterweights turning on the gear box, the motor and its belt guard. A flowline runs to a storage tank,
and from the tank a pipe on sleepers to a filling stand by the track, where travellers refill water (lamp oil on oil
worlds). Barrels lie about.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, RED, TEAL, YELLOW, cable, lamp, pipe,
                        railing, srgb, stripes)
from monkit.surfaces import CONCRETE, GRATE, MATTE, PAINT, RUST

KEY = "wellhead"
TITLE = "Wellhead"
SEED = 808
BUILDER = {"chunk": 32.0, "max_edge": 1.5}

J = Vector((-2.4, 1.2, 0.0))      # the pump jack's Samson post foot; the beam runs along X, horsehead at +X
PIVOT_H = 4.6
TILT = 11.0                        # the beam's nose down, degrees
FRONT, BACK = 3.6, 3.0             # beam lengths ahead of and behind the pivot


def pump_jack(b):
    yel = YELLOW
    with b.frame(tuple(J), (0, 0, 0)):
        b.box((8.6, 2.8, 0.4), (0.0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T, bevel=0.03)
        for x in (-3.8, 3.8):
            b.box((0.3, 2.6, 0.2), (x, 0, 0.5), mat=PAINT, tint=DARK)
        b.box((8.0, 0.25, 0.25), (0, -0.9, 0.525), mat=PAINT, tint=DARK)
        b.box((8.0, 0.25, 0.25), (0, 0.9, 0.525), mat=PAINT, tint=DARK)
        # Samson post: two raked front legs and a back brace to the saddle.
        top = Vector((0, 0, PIVOT_H))
        for y in (-0.85, 0.85):
            b.beam((0.9, y, 0.65), (0.05, y * 0.25, PIVOT_H - 0.25), 0.22, 0.22, mat=PAINT, tint=yel, solid=True)
            b.beam((-1.0, y, 0.65), (-0.05, y * 0.25, PIVOT_H - 0.25), 0.18, 0.18, mat=PAINT, tint=yel, solid=True)
            b.beam((0.55, y * 0.8, 2.2), (-0.6, y * 0.8, 2.2), 0.1, 0.1, mat=PAINT, tint=yel)
        b.box((0.5, 0.7, 0.3), (0, 0, PIVOT_H - 0.12), mat=PAINT, tint=DARK)
        b.cyl(0.12, 0.8, (0, 0, PIVOT_H + 0.05), (90, 0, 0), mat=PAINT, tint=GREY, sides=12, solid=False)
        # Ladder up the post to a small service platform.
        P.rungs(b, (-0.25, -1.05, 0.6), PIVOT_H - 1.2, width=0.4, rot=(0, 0, 0), rail_tint=yel)
        b.box((0.9, 0.7, 0.05), (-0.25, -1.1, PIVOT_H - 0.65), mat=GRATE, tint=GREY, solid=True, vary=0)
        railing(b, (-0.7, -1.45, PIVOT_H - 0.65), (0.2, -1.45, PIVOT_H - 0.65), 1.0, spacing=0.9, tint=yel)
        # The walking beam with the horsehead and, behind, the equaliser.
        with b.frame((0, 0, PIVOT_H + 0.25), (0, TILT, 0)):
            b.box((FRONT + BACK, 0.32, 0.55), ((FRONT - BACK) / 2, 0, 0), mat=PAINT, tint=yel)
            b.box((FRONT + BACK, 0.36, 0.05), ((FRONT - BACK) / 2, 0, 0.3), mat=PAINT, tint=yel, solid=False)
            b.box((FRONT + BACK, 0.36, 0.05), ((FRONT - BACK) / 2, 0, -0.3), mat=PAINT, tint=yel, solid=False)
            stripes(b, (FRONT - 2.0, -0.17, -0.2), (FRONT - 0.6, -0.17, -0.2), 0.4, width=0.2, at_z=-0.2,
                    normal=(0, -1, 0))
            # Horsehead: a band of plate curved about the pivot, at the nose.
            r = FRONT + 0.2
            arc = []
            for k in range(9):
                a = math.radians(-24 + 30 * k / 8)
                arc.append((r * math.cos(a), r * math.sin(a)))
            inner = [(x - 0.6 * math.cos(math.atan2(z, x)), z - 0.6 * math.sin(math.atan2(z, x))) for x, z in arc]
            b.extrude(arc + inner[::-1], 0.5, (0, 0, 0), (0, 0, 0), mat=PAINT, tint=yel, solid="hull")
            nose = b.world((r * math.cos(math.radians(-24)), 0, r * math.sin(math.radians(-24))))
            b.box((0.5, 0.55, 0.4), (-BACK + 0.1, 0, -0.1), mat=PAINT, tint=DARK)
            back = b.world((-BACK + 0.1, 0, -0.35)) - J          # in the jack's frame
        # Gear box, cranks and counterweights, pitman arms, motor and belt guard.
        gx = -3.2
        b.box((1.5, 1.2, 0.9), (gx, 0, 1.15), mat=PAINT, tint=yel, bevel=0.04)
        b.box((1.0, 1.4, 0.4), (gx, 0, 0.75), mat=PAINT, tint=DARK)
        shaft = Vector((gx, 0, 1.9))
        b.cyl(0.14, 2.2, tuple(shaft), (90, 0, 0), mat=PAINT, tint=GREY, sides=12, solid=False)
        crank_a = math.radians(140.0)
        pins = []
        for y in (-0.95, 0.95):
            with b.frame((shaft.x, y, shaft.z), (0, -math.degrees(crank_a), 0)):
                b.box((1.25, 0.12, 0.34), (0.55, 0, 0), mat=PAINT, tint=DARK)
                b.box((0.9, 0.16, 1.15), (-0.55, 0, 0), mat=PAINT, tint=srgb(0.32, 0.33, 0.33), bevel=0.03)
                stripes(b, (-0.95, -0.085, -0.45), (-0.15, -0.085, -0.45), 0.9, width=0.2, at_z=-0.45,
                        normal=(0, -1, 0))
                pins.append(b.world((1.05, 0, 0)) - J)
        for pin in pins:
            top_end = Vector((back.x, pin.y, back.z))
            b.beam(tuple(pin), tuple(top_end), 0.12, 0.12, mat=PAINT, tint=GREY)
        b.box((0.2, 2.1, 0.2), (back.x, 0, back.z), mat=PAINT, tint=GREY, solid=False)
        b.cyl(0.32, 0.7, (gx - 1.6, 0.6, 1.0), (0, 90, 0), mat=PAINT, tint=srgb(0.25, 0.4, 0.5), sides=16)
        b.box((0.9, 0.9, 0.18), (gx - 1.6, 0.6, 0.62), mat=PAINT, tint=DARK)
        b.box((1.6, 0.2, 0.9), (gx - 0.8, 0.95, 1.25), (0, 18, 0), mat=PAINT, tint=yel, solid=False)
        # A low guard rail round the cranks.
        for (x0, y0), (x1, y1) in (((-4.3, -1.4), (-2.0, -1.4)), ((-2.0, -1.4), (-2.0, 1.4)), ((-2.0, 1.4), (-4.3, 1.4))):
            railing(b, (x0, y0, 0.4), (x1, y1, 0.4), 1.1, spacing=1.2, tint=yel, toe=False)
    return nose


def build(b):
    b.set_pad(("rect", 22.0, 18.0), ground="dirt", blend=8.0, clear=2.0)

    nose = pump_jack(b)
    # The wellhead under the horsehead: cellar grating, casing, tree of valves, stuffing box and polished rod.
    w = Vector((nose.x, J.y, 0.0))
    b.box((2.2, 2.2, 0.12), (w.x, w.y, 0.06), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
    b.box((1.6, 1.6, 0.05), (w.x, w.y, 0.13), mat=GRATE, tint=GREY, solid=False, vary=0)
    b.cyl(0.24, 0.5, (w.x, w.y, 0.4), mat=PAINT, tint=srgb(0.3, 0.45, 0.35), sides=14)
    b.cyl(0.36, 0.08, (w.x, w.y, 0.68), mat=PAINT, tint=GREY, sides=14)
    b.cyl(0.14, 0.9, (w.x, w.y, 1.15), mat=PAINT, tint=srgb(0.3, 0.45, 0.35), sides=12)
    for z, x in ((0.95, 0.35), (1.35, -0.35)):
        b.cyl(0.08, 0.6, (w.x + x * 0.5, w.y, z), (0, 90, 0), mat=PAINT, tint=srgb(0.3, 0.45, 0.35), sides=10,
              solid=False)
        b.cyl(0.18, 0.04, (w.x + x * 1.05, w.y, z), (0, 90, 0), mat=PAINT, tint=RED, sides=12, solid=False)
    b.cyl(0.06, 0.2, (w.x, w.y, 1.7), mat=PAINT, tint=DARK, sides=8, solid=False)
    rod_top = nose.z - 1.1
    b.cyl(0.03, rod_top - 1.7, (w.x, w.y, (rod_top + 1.7) / 2), mat=PAINT, tint=srgb(0.8, 0.8, 0.82), sides=6,
          solid=False)
    b.box((0.5, 0.12, 0.1), (w.x, w.y, rod_top), mat=PAINT, tint=DARK, solid=False)
    for dy in (-0.18, 0.18):
        cable(b, (nose.x, w.y + dy, nose.z), (w.x, w.y + dy, rod_top), sag=0.0, radius=0.015, segments=2)
    P.blob(b, (w.x - 0.4, w.y - 0.6, 0.12), 1.1, srgb(0.04, 0.04, 0.035), points=12, rough=0.35, mat="M_Mon_Gloss")

    # Flowline to the tank; the tank; the pipe on sleepers to the filling stand by the track.
    tank_c = (6.4, 4.6, 0.0)
    b.cyl(2.6, 0.3, (tank_c[0], tank_c[1], 0.15), mat=CONCRETE, tint=CONCRETE_T, sides=32)
    P.storage_tank(b, (tank_c[0], tank_c[1], 0.3), 2.2, 3.8, tint=OFFWHITE, band=TEAL, ladder_at=200.0, code="WH-12",
                   code_at=255.0)
    pipe(b, [(w.x + 0.6, w.y, 0.95), (w.x + 2.0, w.y, 0.95), (w.x + 2.0, tank_c[1] - 1.6, 0.95),
             (tank_c[0] - 1.6, tank_c[1] - 1.6, 0.95), (tank_c[0] - 1.55, tank_c[1] - 1.55, 0.9)], 0.07,
         tint=srgb(0.3, 0.45, 0.35), supports=2.0)
    stand = Vector((7.4, -6.2, 0.0))
    pipe(b, [(tank_c[0] + 0.6, tank_c[1] - 2.25, 0.5), (tank_c[0] + 0.6, tank_c[1] - 3.2, 0.3),
             (tank_c[0] + 0.6, -3.0, 0.3), (stand.x, -3.0, 0.3), (stand.x, stand.y + 0.3, 0.3)], 0.06,
         tint=srgb(0.3, 0.45, 0.35))
    for y in (2.0, 0.0, -2.0):
        b.box((0.8, 0.22, 0.14), (tank_c[0] + 0.6, y, 0.08), mat=PAINT, tint=P.WOOD_GREY)
    for x in (tank_c[0] + 1.8, stand.x - 0.2):
        b.box((0.22, 0.8, 0.14), (x, -3.0, 0.08), mat=PAINT, tint=P.WOOD_GREY)
    # The filling stand: a standpipe with a swan neck, a wheel valve, a hose on a hook, a trough and a sign.
    with b.frame(tuple(stand), (0, 0, 0)):
        b.box((1.6, 1.6, 0.15), (0, 0, 0.075), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
        b.cyl(0.09, 2.6, (0, 0.3, 1.45), mat=PAINT, tint=srgb(0.3, 0.45, 0.35), sides=12)
        b.tube([(0, 0.3, 2.7), (0, 0.15, 2.95), (0, -0.25, 2.95), (0, -0.45, 2.7), (0, -0.45, 2.45)], 0.07,
               tint=srgb(0.3, 0.45, 0.35), sides=10)
        b.cyl(0.22, 0.04, (0.14, 0.3, 1.3), (0, 90, 0), mat=PAINT, tint=RED, sides=14, solid=False)
        b.box((1.2, 0.5, 0.45), (0, -0.55, 0.37), mat=PAINT, tint=GREY)
        b.box((1.1, 0.4, 0.05), (0, -0.55, 0.58), mat=MATTE, tint=srgb(0.18, 0.24, 0.26), solid=False)
        b.tube([(-0.09, 0.3, 1.0), (-0.3, 0.2, 0.7), (-0.5, -0.1, 0.25), (-0.7, -0.6, 0.05), (-0.4, -1.0, 0.05)],
               0.03, tint=BLACK, sides=6)
        b.cyl(0.03, 1.8, (0.8, 0.5, 0.9), mat=PAINT, tint=GREY, sides=6, solid=False)
        P.plate(b, (0.8, 0.46, 1.65), (0, 0, 0), (0.7, 0.45), TEAL, P.SIGN_WHITE, "H2O", text_tint=P.SIGN_WHITE,
                text_h=0.16)
        b.marker("refill", (0, -0.9, 0.0), liquid="water", alt="lamp-oil")
    # The track along the front of the site.
    for off in (-0.85, 0.85):
        P.ribbon(b, [(-12.0, -8.0 + off), (0.0, -7.7 + off), (12.0, -8.1 + off)], 0.6, 0.0, P.EARTH_DARK,
                 mat="M_Mon_Concrete", step=2.0)

    # Control panel on a post, its conduit to the motor; a pole lamp; barrels and a drum stack.
    cp = (J.x - 6.0, J.y - 2.2, 0.0)
    b.cyl(0.06, 1.8, (cp[0], cp[1], 0.9), mat=PAINT, tint=GREY, sides=8)
    b.box((0.6, 0.3, 0.8), (cp[0], cp[1] - 0.2, 1.3), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
    b.box((0.4, 0.02, 0.12), (cp[0], cp[1] - 0.36, 1.55), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    P.ground_cable(b, [(cp[0], cp[1], 0.9), (cp[0] + 0.5, cp[1] + 0.8, 0.02), (J.x - 4.8, J.y + 0.6, 0.5)], 0.02,
                   DARK, wobble=0.15, step=0.5)
    b.cyl(0.08, 6.0, (2.6, -3.4, 3.0), mat=PAINT, tint=GREY, sides=8)
    lamp(b, (2.6, -3.6, 6.0), (-30, 0, 0), size=(0.45, 0.25, 0.14))
    b.piece("drum-stack", (-6.4, 4.6, 0), (0, 0, 15.0))
    for x, y in ((-5.0, 5.4), (9.4, 0.6), (-7.6, -4.6)):
        b.marker("loot", (x, y, 0.05), table="barrel")
    for x, y, tilt in ((-5.0, 5.4, False), (9.4, 0.6, True)):
        with b.frame((x, y, 0.29 if tilt else 0.0), (90.0 if tilt else 0, 0, 30.0)):
            b.cyl(0.29, 0.88, (0, 0, 0 if tilt else 0.44), mat=PAINT, tint=srgb(0.2, 0.33, 0.55), sides=16)
    b.zone("no-build", (0, 0, 0), size=(34.0, 30.0, 20.0))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "plains"})


SUN = (34.0, 220.0)
PEOPLE = [((5.6, -7.2, 0.0), 20.0)]
VIEWS = [
    {"name": "well", "target": (0.5, 0.5, 2.0), "distance": 25, "yaw": 25, "pitch": 18, "lens": 30},
    {"name": "jack", "target": (-1.6, 1.0, 3.0), "distance": 12.5, "yaw": -22, "pitch": 2, "lens": 24},
    {"name": "stand", "target": (4.5, -3.0, 1.8), "distance": 13, "yaw": 55, "pitch": 4, "lens": 26},
]
