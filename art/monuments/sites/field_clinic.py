"""Field Clinic (MONUMENTS.md §5.2 no. 24, small tier): a Seedward field hospital set up near an old town and abandoned
in the Quiet. Two prefab ward modules on skids, linked by a canvas canopy, beds still made inside; two ridge tents with
their flaps tied up, cots and lockers; a lab built into a 20-foot container, its door open over a step; a helipad with
its painted mark, edge lamps and a windsock; the mobile clinic crawler parked by the gate; a generator, fuel, a water
bladder, oxygen bottles in a rack, a flag. A chain-link fence along the road. Medical supplies in the wards and tents.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, cable, fence,
                        lamp, srgb, stairs)
from monkit.surfaces import CONCRETE, GLASS, GLOSS, LAMP, MATTE, PAINT, PANEL

KEY = "field-clinic"
TITLE = "Field Clinic"
SEED = 2424
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

CANVAS = srgb(0.84, 0.82, 0.74)
MED = TEAL                      # the clinic's own colour: a teal diamond with a white plus (no protected emblem)
ROAD_Y = -28.0


def med_mark(b, at, rot=(0, 0, 0), size=1.0):
    """The clinic's mark on a surface facing -Y of `rot`: a teal diamond with a white plus."""
    with b.frame(at, rot):
        b.box((size * 0.72, 0.012, size * 0.72), (0, 0.0, 0), (0, 45, 0), mat=PAINT, tint=MED, solid=False, vary=0,
              wear=0.45)
        b.box((size * 0.46, 0.014, size * 0.14), (0, -0.003, 0), mat=PAINT, tint=P.SIGN_WHITE, solid=False, vary=0)
        b.box((size * 0.14, 0.014, size * 0.46), (0, -0.003, 0), mat=PAINT, tint=P.SIGN_WHITE, solid=False, vary=0)


def bed(b, at, yaw=0.0, made=True):
    """A hospital bed: a steel frame on casters, a raised head section, a mattress and pillow, a sheet; an IV stand."""
    with b.frame(at, (0, 0, yaw)):
        for (x, y) in ((-0.42, -0.95), (0.42, -0.95), (-0.42, 0.95), (0.42, 0.95)):
            b.box((0.04, 0.04, 0.5), (x, y, 0.3), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
            b.cyl(0.05, 0.04, (x, y, 0.05), (0, 90, 0), mat=PAINT, tint=BLACK, sides=8, solid=False, vary=0)
        b.box((0.9, 2.0, 0.06), (0, 0, 0.55), mat=PAINT, tint=LIGHTGREY, solid=False)
        b.box((0.86, 1.4, 0.14), (0, 0.28, 0.65), mat=MATTE, tint=srgb(0.6, 0.66, 0.68), solid=False)
        b.box((0.86, 0.58, 0.14), (0, -0.66, 0.78), (-22, 0, 0), mat=MATTE, tint=srgb(0.6, 0.66, 0.68), solid=False)
        b.box((0.5, 0.3, 0.1), (0, -0.78, 0.95), (-22, 0, 0), mat=MATTE, tint=P.SIGN_WHITE, solid=False)
        if made:
            b.box((0.9, 1.2, 0.04), (0, 0.35, 0.74), (0, 2, 0), mat=MATTE, tint=srgb(0.72, 0.8, 0.82), solid=False)
        else:
            P.drape(b, (0.5, 0.6, 0.3), (1.2, 0.9), 70.0, 0.12, 2.0, srgb(0.72, 0.8, 0.82), cells=(5, 4))
        for y in (-1.0, 1.0):
            b.box((0.86, 0.04, 0.3), (0, y, 0.75), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
        b.cyl(0.015, 1.9, (0.5, -1.05, 0.95), mat=PAINT, tint=LIGHTGREY, sides=5, solid=False, vary=0)
        b.box((0.24, 0.02, 0.02), (0.5, -1.05, 1.88), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
        b.box((0.08, 0.04, 0.16), (0.4, -1.05, 1.78), mat=GLOSS, tint=srgb(0.8, 0.85, 0.9), solid=False, vary=0)
    b.collide_box(Vector((at[0], at[1], 0.4)), (0.95, 2.05, 0.8), (0, 0, yaw))


def ward(b, at, yaw, length=12.0, label="WARD A", beds=4, door_open=70.0):
    """A ward module: a prefab box 12 x 3.6 m on skids with windows down both sides, a door with steps at its +X end,
    the clinic's band and mark, air conditioners on the roof; beds along the back wall, a nurse's desk by the door."""
    w, h = 3.6, 2.8
    hl, hw = length / 2, w / 2
    zf = 0.55
    with b.frame(at, (0, 0, yaw)):
        for y in (-hw + 0.4, hw - 0.4):
            b.box((length + 0.3, 0.2, 0.25), (0, y, 0.12), mat=PAINT, tint=DARK)
        for x in (-hl + 0.6, 0.0, hl - 0.6):
            b.box((0.4, w, 0.3), (x, 0, 0.4), mat=PAINT, tint=DARK)
        P.grid_box(b, (length, w, 0.1), (0, 0, zf - 0.05), mat=PAINT, tint=srgb(0.55, 0.6, 0.6), step=1.5)
        for s in (-1, 1):
            P.grid_box(b, (length, 0.1, h), (0, s * (hw - 0.05), zf + h / 2), mat=PANEL, tint=OFFWHITE, step=1.0)
            for k in range(5):
                x = -hl + 1.4 + k * (length - 2.8) / 4
                b.box((1.0, 0.02, 0.8), (x, s * (hw + 0.005), zf + 1.6), mat=GLASS, solid=False)
                b.box((1.12, 0.03, 0.92), (x, s * (hw + 0.002), zf + 1.6), mat=PAINT, tint=DARK, solid=False)
            b.box((length - 0.2, 0.012, 0.22), (0, s * (hw + 0.008), zf + 2.45), mat=PAINT, tint=MED, solid=False,
                  vary=0, wear=0.5)
            b.box((length - 0.2, 0.012, 0.06), (0, s * (hw + 0.008), zf + 0.75), mat=PAINT, tint=ORANGE, solid=False,
                  vary=0, wear=0.5)
        med_mark(b, (-hl + 2.6, -hw - 0.015, zf + 0.9), (0, 0, 0), 1.0)
        P.stencil(b, label, (hl - 1.0, -hw - 0.016, zf + 0.95), (0, 0, 0), 0.24, DARK, align="right")
        # The -X end closed; the +X end with its door.
        P.grid_box(b, (0.1, w - 0.2, h), (-hl + 0.05, 0, zf + h / 2), mat=PANEL, tint=OFFWHITE, step=1.0)
        dy = -0.6
        P.grid_box(b, (0.1, (hw - 0.1) - (dy + 0.55), h), (hl - 0.05, (dy + 0.55 + hw - 0.1) / 2, zf + h / 2),
                   mat=PANEL, tint=OFFWHITE, step=1.0)
        P.grid_box(b, (0.1, (dy - 0.55) - (-hw + 0.1), h), (hl - 0.05, (dy - 0.55 - hw + 0.1) / 2, zf + h / 2),
                   mat=PANEL, tint=OFFWHITE, step=1.0)
        b.box((0.1, 1.1, h - 2.3), (hl - 0.05, dy, zf + 2.3 + (h - 2.3) / 2), mat=PANEL, tint=OFFWHITE)
        with b.frame((hl + 0.02, dy + 0.55, zf), (0, 0, door_open)):
            P.grid_box(b, (0.05, 1.08, 2.28), (0.0, -0.54, 1.14), mat=PAINT, tint=MED, step=0.8)
            b.box((0.02, 0.5, 0.4), (0.03, -0.54, 1.7), mat=GLASS, solid=False)
        stairs(b, (hl + 0.9, dy, 0.0), (0, 0, 90), 1.0, zf, 0.9, tint=GREY, rails=False)
        b.marker("door", (hl + 1.4, dy, 0.0), kind_of="ward")
        b.box((length + 0.3, w + 0.3, 0.12), (0, 0, zf + h + 0.06), mat=PAINT, tint=LIGHTGREY)
        for x in (-hl + 2.5, hl - 3.0):
            b.box((1.4, 1.0, 0.8), (x, 0.4, zf + h + 0.52), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
            b.cyl(0.36, 0.1, (x, 0.4, zf + h + 0.97), mat=PAINT, tint=GREY, sides=14, solid=False)
        lamp(b, (hl + 0.2, dy, zf + 2.6), (-25, 0, 90), size=(0.3, 0.16, 0.12))
        # Inside: beds along the back wall, a desk and a supply cabinet by the door.
        for k in range(beds):
            x = -hl + 1.4 + k * (length - 4.4) / max(1, beds - 1)
            bed(b, b.world((x, hw - 1.2, zf)), yaw + 0.0, made=k % 2 == 0)
        b.box((1.4, 0.6, 0.05), (hl - 1.6, -hw + 0.5, zf + 0.75), mat=PAINT, tint=srgb(0.55, 0.5, 0.42),
              solid=False)
        b.box((0.05, 0.55, 0.72), (hl - 2.25, -hw + 0.5, zf + 0.37), mat=PAINT, tint=DARK, solid=False)
        b.box((0.6, 0.4, 1.8), (-hl + 0.5, -hw + 0.35, zf + 0.9), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
        med_mark(b, (-hl + 0.5, -hw + 0.14, zf + 1.4), (0, 0, 0), 0.35)
        floor = b.world((0.0, -0.3, zf + 0.02))
    return floor


def tent(b, at, yaw, length=10.0, width=6.0, wall=1.8, ridge=3.4, flap=True):
    """A ridge tent of the field clinic: canvas side walls and roof on an aluminium frame (poles and rafters showing at
    the ends), the -Y end wall closed, the +Y end with its flaps tied back, guy ropes to pegs; cots inside."""
    hl, hw = length / 2, width / 2
    rise = ridge - wall
    with b.frame(at, (0, 0, yaw)):
        for s in (-1, 1):
            P.grid_box(b, (0.04, length, wall), (s * hw, 0, wall / 2), mat=MATTE, tint=CANVAS, step=1.5)
            ln = math.hypot(hw + 0.15, rise)
            ang = math.degrees(math.atan2(rise, hw + 0.15))
            P.grid_box(b, (ln, length + 0.3, 0.04), (s * (hw + 0.15) / 2, 0, wall + rise / 2 - 0.05),
                       (0, s * ang, 0), mat=MATTE, tint=CANVAS, step=1.5)
            b.box((0.02, length + 0.1, 0.3), (s * (hw + 0.17), 0, wall - 0.1), mat=MATTE, tint=MED, solid=False,
                  vary=0)
        for k in range(int(length / 2.5) + 1):
            y = -hl + length * k / int(length / 2.5)
            for s in (-1, 1):
                b.box((0.05, 0.05, wall), (s * (hw - 0.05), y, wall / 2), mat=PAINT, tint=P.ALU, solid=False, vary=0)
                P.rod(b, (s * (hw - 0.05), y, wall), (0, y, ridge - 0.08), 0.03, tint=P.ALU, sides=5)
            if k in (0, int(length / 2.5)):          # the ridge poles at the two ends
                b.box((0.05, 0.05, ridge), (0, y, ridge / 2), mat=PAINT, tint=P.ALU, solid=False, vary=0)
        # Gable ends: the back one closed, the front one with its door flaps tied back.
        gable = [(-hw, 0.0), (hw, 0.0), (hw, wall), (0.0, ridge), (-hw, wall)]
        P.end_wall(b, (0, -hl, 0), 0.0, gable, mat=MATTE, tint=CANVAS, thick=0.04)
        door = [(-hw, 0.0), (-0.9, 0.0), (-0.9, 2.2), (0.9, 2.2), (0.9, 0.0), (hw, 0.0), (hw, wall), (0.0, ridge),
                (-hw, wall)]
        P.end_wall(b, (0, hl, 0), 180.0, door, door=(-0.9, 0.9, 2.2), mat=MATTE, tint=CANVAS, thick=0.04)
        if flap:
            for s in (-1, 1):
                P.drape(b, (s * 1.25, hl + 0.12, 1.2), (0.5, 2.2), 90.0, 0.12, 2.0, CANVAS, cells=(4, 3))
        med_mark(b, (0, hl + 0.03, 2.65), (0, 0, 180), 0.8)
        b.marker("door", (0, hl + 0.8, 0.0), kind_of="tent")
        # Guy ropes and pegs along both sides.
        for k in range(3):
            y = -hl + 1.5 + k * (length - 3.0) / 2
            for s in (-1, 1):
                top = (s * (hw + 0.15), y, wall + 0.05)
                peg = (s * (hw + 1.8), y, 0.08)
                cable(b, top, peg, sag=0.0, radius=0.005, tint=srgb(0.8, 0.78, 0.7), segments=2)
                b.cyl(0.02, 0.3, peg, (s * 25, 0, 0), mat=PAINT, tint=GREY, sides=4, solid=False, vary=0)
        # Cots and lockers inside.
        for k in range(4):
            y = -hl + 1.4 + k * 2.2
            for s in (-1, 1):
                with b.frame((s * (hw - 1.1), y, 0.0), (0, 0, 90)):
                    for x in (-0.9, 0.9):
                        b.box((0.05, 0.7, 0.42), (x, 0, 0.21), mat=PAINT, tint=P.ALU, solid=False, vary=0)
                    b.box((1.9, 0.7, 0.05), (0, 0, 0.44), mat=MATTE, tint=srgb(0.36, 0.42, 0.32), solid=False)
                    if (k + s) % 3 == 0:
                        P.drape(b, (0.2, 0.0, 0.47), (1.4, 0.7), 0.0, 0.08, 2.0, srgb(0.55, 0.3, 0.2), cells=(4, 3))
        b.box((0.5, 0.5, 1.2), (0, -hl + 0.5, 0.6), mat=PAINT, tint=srgb(0.4, 0.45, 0.4), bevel=0.02)
        floor = b.world((0, 0, 0.05))
    return floor


def helipad(b, at, size=16.0):
    """A helipad: a raised concrete square with a painted circle and mark, edge lamps, tie-down rings, and a windsock
    on its mast at one corner."""
    x, y = at
    hs = size / 2
    P.grid_box(b, (size, size, 0.25), (x, y, 0.125), mat=CONCRETE, tint=srgb(0.46, 0.47, 0.47), step=2.0)
    b.box((size + 0.3, size + 0.3, 0.1), (x, y, 0.05), mat=CONCRETE, tint=srgb(0.6, 0.6, 0.58), solid=False)
    b.cyl(6.0, 0.012, (x, y, 0.256), mat=PAINT, tint=srgb(0.95, 0.82, 0.3), sides=40, solid=False, vary=0, wear=0.6)
    b.cyl(5.6, 0.014, (x, y, 0.258), mat=CONCRETE, tint=srgb(0.46, 0.47, 0.47), sides=40, solid=False, vary=0)
    P.stencil(b, "H", (x, y - 2.5, 0.262), (-90, 0, 0), 5.0, srgb(0.95, 0.82, 0.3), align="centre", depth=0.006)
    for k in range(12):
        t = k / 12
        for (px, py) in ((x - hs + size * t, y - hs), (x + hs, y - hs + size * t), (x + hs - size * t, y + hs),
                         (x - hs, y + hs - size * t)):
            b.cyl(0.1, 0.12, (px, py, 0.31), mat=PAINT, tint=DARK, sides=8, solid=False, vary=0)
            b.cyl(0.07, 0.06, (px, py, 0.4), mat=LAMP, tint=srgb(0.3, 0.6, 1.0), sides=8, solid=False, vary=0)
    for (dx, dy) in ((-3.0, -3.0), (3.0, -3.0), (-3.0, 3.0), (3.0, 3.0)):
        b.cyl(0.12, 0.03, (x + dx, y + dy, 0.26), mat=PAINT, tint=GREY, sides=10, cap=False, solid=False, vary=0)
    # The windsock.
    wx, wy = x + hs + 2.0, y + hs + 1.0
    b.box((0.6, 0.6, 0.3), (wx, wy, 0.15), mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(0.05, 6.0, (wx, wy, 3.15), mat=PAINT, tint=ORANGE, sides=8)
    for k in range(5):
        b.cyl(0.055, 0.6, (wx, wy, 0.9 + 1.2 * k), mat=PAINT, tint=P.SIGN_WHITE, sides=8, solid=False, vary=0)
    with b.frame((wx, wy, 6.0), (0, 0, 120.0)):
        b.cyl(0.3, 0.05, (0, 0, 0), (0, 90, 0), mat=PAINT, tint=GREY, sides=12, cap=False, solid=False)
        for k in range(5):
            r0 = 0.3 - 0.045 * k
            with b.frame((0.3 + 0.48 * k, 0, -0.12 * k * k * 0.25), (0, 8.0 + 6.0 * k, 0)):
                b.cyl(r0, 0.5, (0, 0, 0), (0, 90, 0), mat=MATTE, tint=ORANGE if k % 2 == 0 else P.SIGN_WHITE,
                      sides=12, radius2=r0 - 0.04, cap=False, solid=False)
    b.marker("landmark", (wx, wy, 6.5), kind_of="windsock")


def lab(b, at, yaw):
    """The lab container: a 20-foot container painted white, its doors at +X open over a step, windows cut in its
    flank, an air conditioner, a cable to the generator; benches, a fridge and instruments inside."""
    with b.frame(at, (0, 0, yaw)):
        b.box((6.4, 2.8, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
        floor = P.container_open(b, (0, 0, 0.3), (0, 0, 0), tint=OFFWHITE, doors=(150.0, 160.0), torn=False,
                                 floor_tint=srgb(0.5, 0.55, 0.55))
        for x in (-1.6, 0.2):
            b.box((1.2, 0.02, 0.7), (x, -1.235, 0.3 + 1.5), mat=GLASS, solid=False)
            b.box((1.32, 0.03, 0.82), (x, -1.232, 0.3 + 1.5), mat=PAINT, tint=DARK, solid=False)
        b.box((5.8, 0.012, 0.22), (0, -1.24, 0.3 + 2.25), mat=PAINT, tint=MED, solid=False, vary=0, wear=0.5)
        med_mark(b, (1.9, -1.245, 0.3 + 1.45), (0, 0, 0), 0.8)
        P.stencil(b, "LAB 1", (-1.0, -1.246, 0.3 + 0.75), (0, 0, 0), 0.26, DARK, align="centre")
        b.box((0.8, 0.5, 0.6), (-2.2, 0.0, 0.3 + 2.59 + 0.3), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
        b.cyl(0.25, 0.08, (-2.2, 0.0, 0.3 + 2.59 + 0.62), mat=PAINT, tint=GREY, sides=12, solid=False)
        stairs(b, (3.03 + 0.9, 0.0, 0.0), (0, 0, 90), 1.2, 0.42, 0.9, tint=GREY, rails=False)
        b.marker("door", (4.3, 0.0, 0.0), kind_of="lab")
        # Inside: a bench along the back wall with instruments, a fridge, a stool, a case on the floor.
        b.box((4.0, 0.6, 0.05), (-0.8, 0.85, 0.42 + 0.9), mat=PAINT, tint=srgb(0.82, 0.82, 0.8), solid=False)
        b.box((4.0, 0.55, 0.85), (-0.8, 0.88, 0.42 + 0.43), mat=PANEL, tint=LIGHTGREY, solid=False)
        for k, (dx, sz, tint) in enumerate(((-2.2, (0.5, 0.4, 0.35), DARK), (-1.2, (0.3, 0.3, 0.45), LIGHTGREY),
                                            (0.2, (0.6, 0.45, 0.3), srgb(0.3, 0.4, 0.45)))):
            b.box(sz, (dx, 0.85, 0.42 + 0.92 + sz[2] / 2), mat=PAINT, tint=tint, solid=False)
        b.box((0.7, 0.65, 1.8), (2.3, 0.75, 0.42 + 0.9), mat=PAINT, tint=P.SIGN_WHITE, bevel=0.03)
        b.box((0.02, 0.4, 0.02), (1.94, 0.6, 0.42 + 1.1), mat=PAINT, tint=GREY, solid=False)
        b.cyl(0.18, 0.6, (0.0, -0.3, 0.42 + 0.3), mat=PAINT, tint=DARK, sides=10, solid=False)
        b.box((0.6, 0.4, 0.3), (-2.0, -0.6, 0.42 + 0.15), mat=PAINT, tint=ORANGE, solid=False)
    return floor


def oxygen_rack(b, at, yaw):
    """A rack of medical gas bottles chained to a frame."""
    with b.frame(at, (0, 0, yaw)):
        b.box((2.0, 0.5, 0.06), (0, 0, 0.03), mat=PAINT, tint=GREY)
        for x in (-0.95, 0.95):
            b.box((0.05, 0.05, 1.5), (x, 0.2, 0.75), mat=PAINT, tint=GREY, solid=False)
        b.box((1.95, 0.04, 0.04), (0, 0.2, 1.1), mat=PAINT, tint=GREY, solid=False)
        for k in range(6):
            x = -0.8 + 0.32 * k
            tint = P.SIGN_WHITE if k % 3 else srgb(0.25, 0.45, 0.32)
            b.cyl(0.11, 1.25, (x, 0.0, 0.68), mat=PAINT, tint=tint, sides=10, solid=False)
            b.lathe([(0.11, 0.0), (0.08, 0.08), (0.03, 0.12), (0.0, 0.12)], (x, 0.0, 1.3), mat=PAINT, tint=tint,
                    sides=10, solid=False)
            b.cyl(0.025, 0.08, (x, 0.0, 1.46), mat=PAINT, tint=GREY, sides=6, solid=False)
        b.tube([(-0.95, 0.12, 1.0), (0.0, 0.14, 0.95), (0.95, 0.12, 1.0)], 0.01, mat=PAINT, tint=GREY, sides=4,
               cap=False)
        b.collide_box(Vector((0, 0, 0.75)), (2.0, 0.5, 1.5), (0, 0, 0))


def build(b):
    b.set_pad(("rect", 66.0, 56.0), ground="gravel", blend=10.0, clear=2.0)
    P.long_road(b, (-34.0, ROAD_Y), (34.0, ROAD_Y), width=7.0)
    for off in (-1.2, 1.2):
        P.ribbon(b, [(4.0 + off, ROAD_Y + 3.5), (3.0 + off, -18.0), (0.0 + off, -8.0)], 0.7, 0.0, P.EARTH_DARK,
                 mat=CONCRETE, step=2.0)

    # The wards linked by a canopy, the tents, the lab, the helipad.
    a = ward(b, (-15.0, 10.0, 0.0), 0.0, label="WARD A")
    c = ward(b, (-15.0, 2.0, 0.0), 0.0, label="WARD B", door_open=35.0)
    b.marker("loot", tuple(a), table="medical")
    b.marker("loot", tuple(c), table="medical")
    P.tarp(b, [(-21.0, 3.9, 3.2), (-9.0, 3.9, 3.2), (-9.0, 8.1, 3.3), (-21.0, 8.1, 3.3)], sag=0.25, tint=CANVAS,
           cells=(8, 3))
    for x in (-21.0, -15.0, -9.0):
        for y in (3.95, 8.05):
            b.box((0.06, 0.06, 3.2), (x, y, 1.6), mat=PAINT, tint=P.ALU, solid=False, vary=0)
    for k in range(3):
        with b.frame((-17.0 + 2.2 * k, 6.0, 0.0), (0, 0, 90)):
            for x in (-0.9, 0.9):
                b.box((0.05, 0.55, 0.12), (x, 0, 0.06), mat=PAINT, tint=P.ALU, solid=False, vary=0)
            b.box((2.0, 0.55, 0.04), (0, 0, 0.14), mat=MATTE, tint=srgb(0.2, 0.42, 0.42), solid=False)
    t1 = tent(b, (-18.0, -12.0, 0.0), 0.0)
    t2 = tent(b, (-6.0, -12.0, 0.0), 0.0)
    b.marker("loot", tuple(t1), table="medical")
    b.marker("loot", tuple(t2), table="cloth")
    lab(b, (5.0, 4.0, 0.0), 180.0)
    b.marker("loot", (4.6, 4.6, 0.45), table="medical")
    helipad(b, (19.0, 10.0))

    # The mobile clinic crawler by the gate, the generator and its fuel, the water bladder, gas bottles, the flag.
    P.crawler(b, (16.0, -12.0, 0.0), (0, 0, -90.0), tint=OFFWHITE, band=MED, stripe=ORANGE, code="MED-2", missing=())
    with b.frame((-27.0, -2.0, 0.0), (0, 0, 90)):
        b.box((2.6, 1.4, 0.2), (0, 0, 0.1), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
        b.box((2.4, 1.2, 1.4), (0, 0, 0.9), mat=PANEL, tint=OFFWHITE, bevel=0.04)
        b.box((2.41, 1.21, 0.16), (0, 0, 1.2), mat=PAINT, tint=MED, solid=False, vary=0)
        b.cyl(0.07, 0.9, (-0.8, 0.3, 2.0), mat=PAINT, tint=DARK, sides=8, solid=False)
        P.stencil(b, "GEN", (0.0, -0.61, 0.5), (0, 0, 0), 0.2, DARK, align="centre")
    b.piece("drum-stack", (-27.5, 2.6, 0.0), (0, 0, 15.0))
    b.marker("loot", (-26.0, 3.6, 0.05), table="fuel-barrel")
    for (pts) in ([(-26.0, -2.0, 0.6), (-24.0, 0.0, 0.03), (-21.5, 2.0, 0.03), (-20.4, 2.6, 0.6)],
                  [(-26.0, -2.0, 0.6), (-20.0, -5.0, 0.03), (-12.0, -8.0, 0.03), (-6.0, -6.6, 0.4)]):
        P.ground_cable(b, pts, 0.02, BLACK, wobble=0.3, step=0.8)
    P.mound(b, (-3.0, 16.0, 0.0), 3.0, 0.9, tint=srgb(0.18, 0.2, 0.22), rough=0.08, mat=MATTE, squash=0.62)
    b.tube([(-0.2, 16.0, 0.3), (2.0, 16.0, 0.25), (3.5, 14.0, 0.25)], 0.05, mat=PAINT, tint=MED, sides=6)
    oxygen_rack(b, (-6.6, 6.0, 0.0), 90.0)
    with b.frame((0.0, -5.0, 0.0), (0, 0, 0)):
        b.box((0.6, 0.6, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=CONCRETE_T)
        b.cyl(0.05, 7.0, (0, 0, 3.6), mat=PAINT, tint=GREY, sides=8)
        with b.frame((0, 0, 6.6), (0, 0, 204.0)):
            b.box((0.02, 1.4, 0.9), (0, 0.72, -0.4), mat=MATTE, tint=MED, solid=False)
            b.box((0.03, 0.5, 0.14), (0, 0.72, -0.4), mat=MATTE, tint=P.SIGN_WHITE, solid=False)
            b.box((0.03, 0.14, 0.5), (0, 0.72, -0.4), mat=MATTE, tint=P.SIGN_WHITE, solid=False)
    # Supply crates stacked by the lab, stretchers leaned on the ward, a body of litter.
    for k in range(5):
        b.box((0.8, 0.6, 0.5), (9.4 + 0.85 * (k % 3), -1.2 + 0.7 * (k // 3), 0.25 + 0.5 * (k // 4)),
              (0, 0, b.rng.uniform(-6, 6)), mat=PAINT, tint=(MED, OFFWHITE, srgb(0.3, 0.35, 0.3))[k % 3])
    b.marker("loot", (10.0, -2.2, 0.05), table="crate")
    for k in range(2):
        with b.frame((-8.7, 9.0 + 1.0 * k, 0.0), (0, -14.0, 0)):
            for y in (-0.28, 0.28):
                b.box((0.04, 0.04, 2.0), (0, y, 1.0), mat=PAINT, tint=P.ALU, solid=False, vary=0)
            b.box((0.02, 0.55, 1.8), (0.0, 0.0, 1.0), mat=MATTE, tint=srgb(0.2, 0.42, 0.42), solid=False)
    P.debris(b, (-2.0, 0.0, 0.0), 6.0, count=8, size=0.5, tint=OFFWHITE, mat=PAINT, thick=0.01)

    # The fence along the road with its gate, lamp posts.
    fence(b, [(-32.0, -24.0, 0), (-1.0, -24.0, 0)], height=2.2, tint=P.GALV)
    fence(b, [(9.0, -24.0, 0), (32.0, -24.0, 0)], height=2.2, tint=P.GALV)
    for x in (-1.0, 9.0):
        b.box((0.2, 0.2, 2.6), (x, -24.0, 1.3), mat=PAINT, tint=GREY, bevel=0.02)
    with b.frame((9.0, -24.0, 0), (0, 0, 115.0)):
        for z in (0.2, 1.1, 2.0):
            b.box((5.0, 0.05, 0.05), (-2.5, 0, z), mat=PAINT, tint=GREY, solid=False)
        b.box((5.0, 0.02, 1.9), (-2.5, 0, 1.1), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)
    P.plate(b, (-6.0, -24.08, 1.5), (0, 0, 0), (1.6, 0.6), OFFWHITE, MED, "CLINIC 3", text_h=0.22)
    for (x, y, yaw) in ((-30.0, -20.0, 135.0), (30.0, 22.0, -45.0), (-30.0, 22.0, -135.0)):
        b.piece("lamp-post", (x, y, 0.0), (0, 0, yaw))
    for (x, y, s) in ((-31.0, 24.0, 1.2), (28.0, -18.0, 1.0), (31.0, 0.0, 1.3), (6.0, 24.0, 1.0), (-28.0, -18.0, 0.9)):
        P.bush(b, (x, y, 0.0), s, P.LEAF, lumps=3)

    b.zone("no-build", (0, 0, 0), size=(76.0, 66.0, 30.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "near-town"})


SUN = (30.0, 220.0)
PEOPLE = [((2.0, -16.0, 0.0), 20.0)]
VIEWS = [
    {"name": "clinic", "target": (0.0, 0.0, 2.0), "distance": 78, "yaw": 24, "pitch": 26, "lens": 30},
    {"name": "wards", "target": (-10.0, 5.0, 1.8), "distance": 15, "yaw": 58, "pitch": 4, "lens": 24},
    {"name": "helipad", "target": (12.0, 4.0, 1.6), "distance": 28, "yaw": -42, "pitch": 10, "lens": 26},
]
