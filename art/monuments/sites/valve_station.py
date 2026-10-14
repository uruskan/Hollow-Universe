"""Valve Station (MONUMENTS.md §5.1 no. 12, roadside tier): where an old pipeline surfaces for its block valve. The ochre
line climbs out of the ground through a concrete thrust block, runs on saddles across a fenced yard and dives back
under. On it the main valve with its gear actuator and handwheel, a bypass loop with two smaller valves, a vent stack;
a pig launcher on its skid with the closure door on a davit and a pig on its trolley; a step-over stair across the
line; the valve house with its roller door half up. Pipes and gears in the house, lamp oil in drums.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, ORANGE, RED, YELLOW, fence, lamp, railing, srgb,
                        stairs, stripes)
from monkit.structures import shed
from monkit.surfaces import CONCRETE, FORMWORK, GRATE, MATTE, PAINT, RUST

KEY = "valve-station"
TITLE = "Valve Station"
SEED = 1212
BUILDER = {"chunk": 32.0, "max_edge": 2.0}

R = 0.5             # the pipeline's radius
Z = 1.1             # its centre height on the saddles
X0, X1 = -10.6, 10.6    # where it surfaces and dives
OCHRE = P.PIPE_OCHRE


def run(b, pts, radius=R, tint=OCHRE, sides=22):
    b.tube(pts, radius, mat=PAINT, tint=tint, sides=sides, cap=True, solid=True)


def build(b):
    b.set_pad(("rect", 28.0, 17.0), ground="gravel", blend=8.0, clear=2.0)

    # The pipeline: up out of the ground through a thrust block, along the yard, back down.
    run(b, P.bend((X0 - 2.2, 0, -1.4), (X0, 0, -1.4), (X0, 0, Z), 1.0, 5)[:-1] +
        P.bend((X0, 0, -1.4), (X0, 0, Z), (X1, 0, Z), 1.0, 5)[1:-1] +
        P.bend((X0, 0, Z), (X1, 0, Z), (X1, 0, -1.4), 1.0, 5)[1:-1] +
        P.bend((X1, 0, Z), (X1, 0, -1.4), (X1 + 2.2, 0, -1.4), 1.0, 5)[1:])
    for x in (X0, X1):
        P.grid_box(b, (2.2, 2.0, 0.7), (x, 0, 0.25), mat=FORMWORK, tint=CONCRETE_T, step=1.0)
        b.box((2.4, 2.2, 0.1), (x, 0, 0.62), mat=CONCRETE, tint=srgb(0.7, 0.7, 0.67), solid=False)
        stripes(b, (x - 1.1, -1.005, 0), (x + 1.1, -1.005, 0), 0.12, width=0.2, at_z=0.45)
    for x in (-7.6, -3.8, 3.2, 6.6):
        P.pipe_support(b, (x, 0, Z), R)
    for x in (-8.6, -4.6, -0.4, 0.6, 5.2, 8.6):
        P.flange(b, (x, 0, Z), (0, 90, 0), R)
    for x in (-6.0, 2.0, 8.0):
        b.cyl(R + 0.01, 0.3, (x, 0, Z), (0, 90, 0), mat=PAINT, tint=BLACK, sides=22, solid=False, vary=0)

    # The main valve and its gauge post; the bypass loop with two valves; the vent stack.
    P.gate_valve(b, (-2.5, 0, Z), 0.0, R)
    b.cyl(0.05, 1.5, (-2.5, -1.4, 0.75), mat=PAINT, tint=GREY, sides=8)
    b.box((0.4, 0.2, 0.5), (-2.5, -1.45, 1.6), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
    b.cyl(0.1, 0.04, (-2.5, -1.57, 1.68), (90, 0, 0), mat=PAINT, tint=P.SIGN_WHITE, sides=14, solid=False)
    b.tube([(-2.5, -1.35, 1.4), (-2.5, -0.9, 1.2), (-2.5, -0.55, Z)], 0.02, tint=DARK, sides=5)
    rb = 0.14
    by = 1.7
    bp = (P.bend((-5.0, R - 0.05, Z), (-5.0, by, Z), (-3.4, by, Z + 0.9), 0.4, 4)[:-1] +
          P.bend((-5.0, by, Z), (-4.6, by, Z + 0.9), (0.0, by, Z + 0.9), 0.4, 4)[1:-1] +
          P.bend((-4.6, by, Z + 0.9), (0.0, by, Z + 0.9), (0.0, R - 0.05, Z), 0.4, 4)[1:])
    run(b, bp, rb, OCHRE, 10)
    for x in (-3.6, -1.4):
        P.gate_valve(b, (x, by, Z + 0.9), 0.0, rb + 0.02, actuator=False)
    for x in (-3.0, -0.2):
        P.pipe_support(b, (x, by, Z + 0.9), rb + 0.03)
    vx = -7.0
    run(b, [(vx, 0, Z + R - 0.05), (vx, 0, 5.4)], 0.13, OCHRE, 10)
    b.cyl(0.24, 0.9, (vx, 0, 4.4), mat=PAINT, tint=GREY, sides=12)
    b.cyl(0.32, 0.08, (vx, 0, 5.45), mat=PAINT, tint=GREY, sides=12, solid=False)
    b.cyl(0.32, 0.18, (vx, 0, 5.7), mat=PAINT, tint=GREY, sides=12, radius2=0.04)
    for z in (5.5, 5.62):
        b.cyl(0.015, 0.25, (vx + 0.12, 0, z), mat=PAINT, tint=DARK, sides=4, solid=False)
    P.handwheel(b, (vx, -0.45, 2.3), (0, 0, 0), 0.22, RED)
    b.box((0.2, 0.3, 0.25), (vx, -0.2, 2.3), mat=PAINT, tint=P.VALVE_GREEN, solid=False)

    # The pig launcher: barrel on a skid parallel to the line, a reducer, the kicker into the line; its closure door
    # swung open on the davit; a pig on its trolley at the open end.
    ly = -2.7
    with b.frame((0, ly, 0), (0, 0, 0)):
        for x in (4.4, 6.6):
            b.box((0.5, 1.6, 0.35), (x, 0, 0.17), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.70))
            b.box((0.2, 1.2, Z - 0.75), (x, 0, 0.35 + (Z - 0.75) / 2), mat=PAINT, tint=GREY)
            b.box((0.3, 1.6, 0.08), (x, 0, Z - 0.66), mat=PAINT, tint=GREY, solid=False)
        b.cyl(0.66, 3.6, (5.7, 0, Z), (0, 90, 0), mat=PAINT, tint=OCHRE, sides=22)
        b.cyl(0.66, 0.6, (7.8, 0, Z), (0, 90, 0), mat=PAINT, tint=OCHRE, sides=22, radius2=0.42)
        P.flange(b, (3.9, 0, Z), (0, 90, 0), 0.66)
        P.flange(b, (7.5, 0, Z), (0, 90, 0), 0.66)
        run(b, P.bend((8.1, 0, Z), (9.4, 0, Z), (9.4, -ly - R + 0.05, Z), 0.6, 5), 0.38, OCHRE, 18)
        P.gate_valve(b, (9.4, 1.2, Z), 90.0, 0.4, actuator=False)
        # Closure door hung open on the davit, the davit post and arm.
        b.cyl(0.06, 2.4, (3.3, 0.95, 1.2), mat=PAINT, tint=GREY, sides=10)
        b.box((0.9, 0.12, 0.12), (3.3, 0.55, 2.3), (0, 0, 70), mat=PAINT, tint=GREY, solid=False)
        with b.frame((3.3, 0.95, Z), (0, 0, -68.0)):
            b.cyl(0.84, 0.22, (0, -0.85, 0), (0, 90, 0), mat=PAINT, tint=OCHRE, sides=22)
            b.cyl(0.3, 0.1, (-0.15, -0.85, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=14, solid=False)
            for k in range(3):
                b.box((0.06, 0.08, 1.2), (-0.14, -0.85, 0), (60 * k, 0, 0), mat=PAINT, tint=DARK, solid=False)
        b.cyl(0.6, 0.03, (3.78, 0, Z), (0, 90, 0), mat=MATTE, tint=srgb(0.04, 0.04, 0.04), sides=22, solid=False)
        # Gauge, vent and drain valves, a nameplate; the pig on its trolley.
        b.cyl(0.04, 0.4, (5.0, 0, Z + 0.82), mat=PAINT, tint=GREY, sides=6, solid=False)
        b.cyl(0.12, 0.06, (5.0, -0.03, Z + 1.05), (90, 0, 0), mat=PAINT, tint=LIGHTGREY, sides=14, solid=False)
        b.cyl(0.1, 0.02, (5.0, -0.065, Z + 1.05), (90, 0, 0), mat=PAINT, tint=P.SIGN_WHITE, sides=14, solid=False)
        P.handwheel(b, (6.3, 0, Z + 1.0), (90, 0, 0), 0.16, RED, spokes=3)
        b.cyl(0.04, 0.3, (6.3, 0, Z + 0.8), mat=PAINT, tint=GREY, sides=6, solid=False)
        P.handwheel(b, (6.8, -0.3, 0.3), (0, 0, 0), 0.14, RED, spokes=3)
        b.tube([(6.8, 0, Z - 0.66), (6.8, 0, 0.3), (6.8, -0.2, 0.3)], 0.04, tint=OCHRE, sides=6)
        P.plate(b, (5.7, -0.665, Z + 0.15), (0, 0, 0), (0.7, 0.24), P.SIGN_WHITE, DARK, "PIG L-2", text_h=0.1)
        with b.frame((1.6, -0.2, 0.0), (0, 0, 8.0)):
            for x in (-0.6, 0.6):
                for y in (-0.35, 0.35):
                    P.wheel(b, (x, y * 1.15, 0.12), (0, 0, 90), 0.12, 0.06, sides=8)
            b.box((1.5, 0.7, 0.08), (0, 0, 0.26), mat=PAINT, tint=YELLOW)
            for x in (-0.45, 0.45):
                b.box((0.1, 0.62, 0.22), (x, 0, 0.4), mat=PAINT, tint=YELLOW, solid=False)
            b.cyl(0.4, 1.3, (0, 0, 0.92), (0, 90, 0), mat=PAINT, tint=GREY, sides=16)
            for x in (-0.55, -0.15, 0.25):
                b.cyl(0.58, 0.08, (x, 0, 0.92), (0, 90, 0), mat=MATTE, tint=srgb(0.82, 0.38, 0.12), sides=18,
                      solid=False)
            b.lathe([(0.4, 0.0), (0.34, 0.18), (0.18, 0.3), (0.0, 0.34)], (0.65, 0, 0.92), (0, 90, 0), mat=PAINT,
                    tint=GREY, sides=16)
            b.cyl(0.05, 0.5, (-0.95, 0, 0.6), (0, 60, 0), mat=PAINT, tint=DARK, sides=6, solid=False)

    # The step-over stair across the line.
    sx = 1.5
    top = Z + R + 0.42
    run_len = 2.4
    stairs(b, (sx, -0.95 - run_len, 0.0), (0, 0, 0), 0.8, top, run_len, tint=YELLOW)
    stairs(b, (sx, 0.95 + run_len, 0.0), (0, 0, 180), 0.8, top, run_len, tint=YELLOW)
    b.box((1.0, 1.9, 0.05), (sx, 0, top - 0.025), mat=GRATE, tint=GREY, solid=True, vary=0)
    for x in (sx - 0.5, sx + 0.5):
        railing(b, (x, -0.95, top), (x, 0.95, top), 1.1, spacing=1.0, tint=YELLOW)
        for y in (-0.9, 0.9):
            b.box((0.08, 0.08, top), (x, y, top / 2), mat=PAINT, tint=DARK)

    # The valve house, its door toward the yard; a workbench, a pipe rack, gears and lamp-oil cans inside.
    hx, hy = -6.2, -4.6
    shed(b, (hx, hy, 0), (0, 0, 90), 6.0, 4.0, 3.0, roof="mono", pitch=8.0, tint=srgb(0.66, 0.68, 0.64),
         doors=[("front", 0.6, 2.4, 2.4)], skylights=False, ruin=0.12)
    P.stencil(b, "VS-23", (hx + 2.13, hy - 2.2, 2.75), (0, 0, 90), 0.36, DARK)
    lamp(b, (hx + 2.25, hy + 2.2, 2.85), (-20, 0, 90), size=(0.4, 0.22, 0.14))
    with b.frame((hx, hy, 0.25), (0, 0, 90)):
        b.box((3.4, 0.8, 0.06), (-0.6, 1.5, 0.9), mat=PAINT, tint=srgb(0.5, 0.42, 0.3), solid=False)
        for x in (-2.2, 1.0):
            b.box((0.06, 0.7, 0.9), (x, 1.5, 0.45), mat=PAINT, tint=DARK, solid=False)
        P.gate_valve(b, (-1.2, 1.5, 1.15), 0.0, 0.16, actuator=False)
        P.toolbox(b, (0.3, 1.5, 0.93), 10.0, open_lid=True)
        for k in range(4):
            a = 2 * math.pi * k / 4
            b.cyl(0.16 + 0.05 * k, 0.05, (-2.0 + 0.4 * k, 1.35, 0.95), mat=PAINT, tint=GREY, sides=14,
                  solid=False)
        for z in (0.5, 1.2):
            b.box((0.12, 3.0, 0.08), (2.6, 0.0, z), mat=PAINT, tint=DARK, solid=False)
        for k in range(5):
            b.cyl(0.07 + 0.02 * (k % 3), 3.1, (2.6, 0.0, 1.3 + 0.17 * k), (90, 0, 0), mat=PAINT,
                  tint=(OCHRE, GREY, OCHRE, GREY, GREY)[k], sides=10, solid=False)
        for k in range(3):
            b.box((0.2, 0.3, 0.4), (-2.5 + 0.25 * k, -1.4, 0.2), mat=PAINT, tint=srgb(0.62, 0.16, 0.1), solid=False)
    b.marker("loot", (hx - 1.0, hy + 0.6, 0.27), table="crate")
    b.marker("loot", (hx - 0.2, hy - 1.6, 0.27), table="pipe-parts")
    b.marker("door", (hx + 2.6, hy - 0.6, 0.0), kind_of="shutter")

    # Lamp-oil drums by the house, a drum stack, a spilled one.
    b.piece("drum-stack", (-9.6, -6.0, 0), (0, 0, 12.0))
    for x, y in ((-2.6, -6.2), (-1.6, -6.6)):
        b.cyl(0.29, 0.88, (x, y, 0.44), mat=PAINT, tint=srgb(0.62, 0.16, 0.1), sides=16)
        b.marker("loot", (x, y, 0.9), table="fuel-barrel")
    with b.frame((-3.8, -6.4, 0.29), (90.0, 0, 60.0)):
        b.cyl(0.29, 0.88, (0, 0, 0), mat=PAINT, tint=srgb(0.62, 0.16, 0.1), sides=16)
    P.blob(b, (-4.4, -6.9, 0.0), 0.9, srgb(0.06, 0.05, 0.04), points=12, rough=0.35)
    b.marker("loot", (9.0, -5.6, 0.05), table="barrel")
    # A weeping flange and its stain, an old handwheel in the gravel, offcuts.
    P.blob(b, (-4.6, 0.3, 0.0), 1.0, srgb(0.1, 0.08, 0.06), points=12, rough=0.4, squash=0.6)
    P.handwheel(b, (2.4, -5.8, 0.03), (90, 0, 30), 0.3, RED)
    for k in range(3):
        b.cyl(0.08 + 0.04 * k, 1.0 + 0.4 * k, (6.8 + 0.3 * k, 5.2 - 0.4 * k, 0.1 + 0.04 * k), (0, 90, 20 + 15 * k),
              mat=PAINT, tint=OCHRE if k != 1 else GREY, sides=10, solid=False)

    # The fence with its gate, pipeline marker posts beyond, warning plates; a lamp post.
    fx, fy = 13.0, 7.5
    gate = (2.4, 6.4)
    fence(b, [(gate[1], -fy, 0), (fx, -fy, 0), (fx, fy, 0), (-fx, fy, 0), (-fx, -fy, 0), (gate[0], -fy, 0)],
          height=2.2, tint=P.GALV)
    for x in gate:
        b.box((0.2, 0.2, 2.6), (x, -fy, 1.3), mat=PAINT, tint=GREY, bevel=0.02)
    with b.frame((gate[0], -fy, 0), (0, 0, -100.0)):
        for z in (0.2, 1.1, 2.0):
            b.box((2.0, 0.05, 0.05), (1.0, 0, z), mat=PAINT, tint=GREY, solid=False)
        b.box((2.0, 0.02, 1.9), (1.0, 0, 1.1), mat="M_Mon_Chainlink", tint=GREY, solid=False)
    for x in (-8.0, 9.0):
        P.plate(b, (x, -fy - 0.05, 1.5), (0, 0, 0), (0.6, 0.4), YELLOW, None, "HP", text_h=0.2)
    for x in (-16.0, 16.0):
        b.cyl(0.06, 1.4, (x, 0.0, 0.7), mat=PAINT, tint=P.SIGN_WHITE, sides=8, solid=False)
        b.box((0.3, 0.06, 0.3), (x, -0.04, 1.3), (0, 45, 0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.piece("lamp-post", (fx - 1.0, fy - 1.0, 0), (0, 0, -135.0))

    b.zone("no-build", (0, 0, 0), size=(38.0, 28.0, 20.0))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "pipeline"})


SUN = (30.0, 215.0)
PEOPLE = [((4.6, -8.6, 0.0), 20.0)]
VIEWS = [
    {"name": "yard", "target": (-0.5, -0.5, 1.4), "distance": 33, "yaw": 22, "pitch": 26, "lens": 30},
    {"name": "valve", "target": (-2.4, -0.4, 1.7), "distance": 6.8, "yaw": 20, "pitch": 6, "lens": 24},
    {"name": "launcher", "target": (3.4, -2.6, 1.0), "distance": 5.6, "yaw": -35, "pitch": 10, "lens": 22},
]
