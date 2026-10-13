"""Water Tower (MONUMENTS.md §5.2 no. 17, small tier): a Seedward elevated tank serving an old town or farm district. A
steel tank 12 m across with a cone bottom and roof stands 24 m up on six steel columns with struts and tension rods, a
ring girder and a balcony round its foot, the riser pipe down the middle; a caged ladder with two rest platforms climbs a
column to the balcony and a second ladder goes up the shell to a walkway over the roof to the vent and lamp. Below, a
concrete pump house with its pumps, the main running to the riser on saddles, the overflow down a column to a sump, a
traveller's standpipe and trough by the road. Water, barrels, and a crate at the top of the climb.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, RED_LAMP, TEAL, YELLOW,
                        fence, ladder, lamp, railing, srgb, stripes)
from monkit.surfaces import CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST

KEY = "water-tower"
TITLE = "Water Tower"
SEED = 1717
BUILDER = {"chunk": 40.0, "max_edge": 2.5}

T = Vector((-8.0, 4.0, 0.0))    # the tower's centre
RL = 5.4                         # legs' radius
LEG_TOP = 24.0
RT = 6.0                         # tank radius
SHELL = (24.0, 31.0)
BLUE_PIPE = srgb(0.22, 0.38, 0.58)
LEG_ANG = [math.radians(30 + 60 * k) for k in range(6)]
LADDER_ANG = math.radians(270.0)  # the ladder faces the front (-Y), between two legs


def annulus(b, centre, r0, r1, a0, a1, z, thick=0.06, segs=8, mat=GRATE, tint=GREY, solid=True):
    """A flat ring sector (a balcony deck) from radius r0 to r1 and angle a0 to a1 (degrees) at height z."""
    cx, cy = centre[0], centre[1]
    verts = []
    for k in range(segs + 1):
        a = math.radians(a0 + (a1 - a0) * k / segs)
        for r in (r0, r1):
            verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z))
    n = len(verts)
    verts += [(x, y, z - thick) for x, y, _ in verts]
    faces = []
    for k in range(segs):
        i = 2 * k
        faces.append([i, i + 1, i + 3, i + 2])
        faces.append([n + i, n + i + 2, n + i + 3, n + i + 1])
        faces.append([i, i + 2, n + i + 2, n + i])
        faces.append([i + 1, n + i + 1, n + i + 3, i + 3])
    faces.append([0, n, n + 1, 1])
    last = 2 * segs
    faces.append([last, last + 1, n + last + 1, n + last])
    b.mesh(verts, faces, mat=mat, tint=tint, solid="hull" if solid else False, vary=0, smooth=False)


def tower(b):
    cx, cy = T.x, T.y
    # Foundations and the six columns, struts at 8 and 16 m, X rods in every panel.
    for a in LEG_ANG:
        x, y = cx + RL * math.cos(a), cy + RL * math.sin(a)
        P.grid_box(b, (1.6, 1.6, 0.9), (x, y, 0.25), mat=CONCRETE, tint=CONCRETE_T, step=0.8)
        b.cyl(0.55, 0.06, (x, y, 0.73), mat=PAINT, tint=GREY, sides=12)
        b.cyl(0.4, LEG_TOP - 0.7, (x, y, 0.7 + (LEG_TOP - 0.7) / 2), mat=PAINT, tint=OFFWHITE, sides=14)
        for z in (8.0, 16.0):
            b.cyl(0.46, 0.25, (x, y, z), mat=PAINT, tint=OFFWHITE, sides=14, solid=False)
    for i in range(6):
        a0, a1 = LEG_ANG[i], LEG_ANG[(i + 1) % 6]
        p0 = Vector((cx + RL * math.cos(a0), cy + RL * math.sin(a0), 0))
        p1 = Vector((cx + RL * math.cos(a1), cy + RL * math.sin(a1), 0))
        for z in (8.0, 16.0):
            P.rod(b, (p0.x, p0.y, z), (p1.x, p1.y, z), 0.16, tint=OFFWHITE, sides=10, trim=0.38, solid=True)
        for z0, z1 in ((1.0, 8.0), (8.0, 16.0), (16.0, 23.6)):
            if i == 4 and z0 == 8.0:
                # One rod snapped, its end hanging.
                P.rod(b, (p0.x, p0.y, z0), ((p0.x + p1.x) / 2 + 0.3, (p0.y + p1.y) / 2, (z0 + z1) / 2 - 0.6), 0.035,
                      sides=5, trim=0.3)
                P.rod(b, (p1.x, p1.y, z0), (p0.x, p0.y, z1), 0.035, sides=5, trim=0.42)
                continue
            P.rod(b, (p0.x, p0.y, z0), (p1.x, p1.y, z1), 0.035, sides=5, trim=0.42)
            P.rod(b, (p1.x, p1.y, z0), (p0.x, p0.y, z1), 0.035, sides=5, trim=0.42)
    # Ring girder, the cone bottom, the riser and its base.
    b.lathe([(RL - 0.4, 0.0), (RT + 0.15, 0.0), (RT + 0.15, 0.9), (RL - 0.4, 0.9), (RL - 0.4, 0.0)],
            (cx, cy, LEG_TOP - 0.9), mat=PAINT, tint=OFFWHITE, sides=36, close=False, solid="hull")
    b.lathe([(0.8, 0.0), (RT * 0.55, 1.4), (RT, 2.7)], (cx, cy, LEG_TOP - 2.7), mat=PAINT, tint=OFFWHITE, sides=36,
            close=False, solid="hull")
    b.cyl(0.75, LEG_TOP - 2.7 - 0.9, (cx, cy, 0.9 + (LEG_TOP - 2.7 - 0.9) / 2), mat=PAINT, tint=OFFWHITE, sides=18)
    for z in (4.0, 12.0, 18.0):
        P.flange(b, (cx, cy, z), (0, 0, 0), 0.75, tint=OFFWHITE)
    P.grid_box(b, (2.6, 2.6, 0.9), (cx, cy, 0.45), mat=CONCRETE, tint=CONCRETE_T, step=0.8)
    for k in range(3):
        a = math.radians(120 * k)
        P.rod(b, (cx, cy, 6.0), (cx + RL * math.cos(a + 0.52), cy + RL * math.sin(a + 0.52), 8.0), 0.06, sides=6,
              trim=0.75)
    # The tank: shell, bands, the code, rust weeping from a seam; the roof cone with a walkway, the vent and lamp.
    z0, z1 = SHELL
    b.cyl(RT, z1 - z0, (cx, cy, (z0 + z1) / 2), mat=PAINT, tint=OFFWHITE, sides=48, cap=False, solid=True)
    for z in (z0 + 2.3, z0 + 4.6):
        b.cyl(RT + 0.012, 0.05, (cx, cy, z), mat=PAINT, tint=OFFWHITE, sides=48, cap=False, solid=False, vary=0)
    b.cyl(RT + 0.02, 0.7, (cx, cy, z0 + 1.0), mat=PAINT, tint=TEAL, sides=48, cap=False, solid=False, vary=0)
    b.cyl(RT + 0.02, 0.35, (cx, cy, z1 - 0.4), mat=PAINT, tint=ORANGE, sides=48, cap=False, solid=False, vary=0)
    P.stencil(b, "WT-2", (cx, cy - RT - 0.03, z0 + 2.2), (0, 0, 0), 1.7, DARK, align="centre")
    P.stencil(b, "RC", (cx + 3.6, cy - RT * 0.8 - 0.03, z0 + 0.75), (0, 0, -37), 0.6, srgb(0.75, 0.12, 0.1),
              align="centre", jitter=0.8)
    for k, (ang, ln) in enumerate(((-58.0, 4.5), (-122.0, 3.2), (20.0, 5.5), (-75.0, 2.4))):
        for j in range(3):
            a = math.radians(ang + j * 0.9 + b.rng.uniform(-0.3, 0.3))
            lj = ln * (1.0 - 0.25 * j) * (0.8 + 0.4 * b.rng.random())
            b.box((0.05 + 0.04 * b.rng.random(), 0.012, lj), (cx + (RT + 0.015) * math.cos(a),
                                                             cy + (RT + 0.015) * math.sin(a), z1 - 0.6 - lj / 2),
                  (0, 0, math.degrees(a) + 90), mat=PAINT, tint=srgb(0.42, 0.24, 0.14), solid=False, vary=0.1,
                  wear=0.9)
    b.lathe([(RT + 0.12, 0.0), (RT * 0.6, 0.85), (0.9, 1.5), (0.0, 1.55)], (cx, cy, z1), mat=PAINT, tint=OFFWHITE,
            sides=48)
    b.cyl(0.35, 0.8, (cx, cy, z1 + 1.9), mat=PAINT, tint=GREY, sides=14)
    b.cyl(0.6, 0.25, (cx, cy, z1 + 2.45), mat=PAINT, tint=GREY, sides=14, radius2=0.12)
    b.cyl(0.04, 1.4, (cx, cy, z1 + 3.2), mat=PAINT, tint=GREY, sides=6, solid=False)
    b.box((0.22, 0.22, 0.26), (cx, cy, z1 + 3.95), mat=LAMP, tint=RED_LAMP, solid=False)
    b.marker("light", (cx, cy, z1 + 4.1), colour=[1.0, 0.12, 0.05], range=400.0, blink=1.0)
    b.marker("landmark", (cx, cy, z1 + 4.0), kind_of="water-tower")
    # The roof walkway up the cone from the roof ladder, treads and a handrail; the hatch beside the vent.
    la = LADDER_ANG + math.radians(18.0)
    d = Vector((math.cos(la), math.sin(la), 0))
    side = Vector((-d.y, d.x, 0))

    def roof_z(r):
        """The roof cone's height above the shell's top at radius r (the lathe profile below)."""
        if r >= RT * 0.6:
            return 0.85 * (RT + 0.12 - r) / (RT + 0.12 - RT * 0.6)
        return 0.85 + 0.65 * (RT * 0.6 - r) / (RT * 0.6 - 0.9)

    for k in range(9):
        r = RT - 0.2 - (k / 8) * (RT - 1.2)
        p = Vector((cx, cy, 0)) + d * r
        b.box((0.7, 0.3, 0.05), (p.x, p.y, z1 + roof_z(r) + 0.06), (0, 0, math.degrees(la) + 90), mat=GRATE,
              tint=GREY, solid=False, vary=0)
    pts = []
    for k in range(7):
        r = RT - 0.2 - (k / 6) * (RT - 1.4)
        pts.append(Vector((cx, cy, 0)) + d * r + side * 0.45 + Vector((0, 0, z1 + roof_z(r) + 1.0)))
    for p, q in zip(pts, pts[1:]):
        b.beam(tuple(p), tuple(q), 0.04, 0.04, mat=PAINT, tint=YELLOW)
        b.box((0.04, 0.04, 1.0), (p.x, p.y, p.z - 0.5), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    hp = Vector((cx, cy, 0)) + d * 1.6
    b.box((0.8, 0.8, 0.3), (hp.x, hp.y, z1 + 1.35), (0, 0, math.degrees(la)), mat=PAINT, tint=GREY)
    b.marker("loot", (cx + 0.9, cy - 0.6, z1 + 1.62), table="crate")

    # Balcony: a grated ring on brackets with its railing, open where the ladders arrive; one rail section bent.
    gap0, gap1 = math.degrees(LADDER_ANG) - 7.0, math.degrees(LADDER_ANG) + 7.0
    annulus(b, (cx, cy), RT + 0.15, RT + 1.3, gap1, gap0 + 360.0, LEG_TOP + 0.06, segs=34)
    annulus(b, (cx, cy), RT + 0.15, RT + 0.6, gap0, gap1, LEG_TOP + 0.06, segs=2)
    for k in range(18):
        a = math.radians(20 * k + 10)
        b.beam((cx + (RT + 0.1) * math.cos(a), cy + (RT + 0.1) * math.sin(a), LEG_TOP - 0.9),
               (cx + (RT + 1.25) * math.cos(a), cy + (RT + 1.25) * math.sin(a), LEG_TOP - 0.05), 0.08, 0.12,
               mat=PAINT, tint=OFFWHITE)
    rr = RT + 1.25
    n = 24
    for k in range(n):
        a0 = math.radians(gap1 + (360.0 - (gap1 - gap0)) * k / n)
        a1 = math.radians(gap1 + (360.0 - (gap1 - gap0)) * (k + 1) / n)
        p0 = (cx + rr * math.cos(a0), cy + rr * math.sin(a0), LEG_TOP + 0.06)
        p1 = (cx + rr * math.cos(a1), cy + rr * math.sin(a1), LEG_TOP + 0.06)
        if k == 9:
            with b.frame(p0, (0, 0, 0)):
                b.beam((0, 0, 0), (p1[0] - p0[0] + 0.3, p1[1] - p0[1] - 0.6, -0.4), 0.05, 0.05, mat=PAINT,
                       tint=YELLOW)
            continue
        railing(b, p0, p1, 1.1, spacing=1.6, tint=YELLOW)
    # The caged ladder up the front, with rest platforms at 8 and 16 m; the ladder up the shell to the roof.
    lp = Vector((cx, cy, 0)) + Vector((math.cos(LADDER_ANG), math.sin(LADDER_ANG), 0)) * (RT + 0.85)
    for z0_, z1_ in ((0.0, 8.0), (8.0, 16.0), (16.0, LEG_TOP)):
        ladder(b, (lp.x, lp.y, z0_), z1_ - z0_ + 0.06, (0, 0, math.degrees(LADDER_ANG) + 90.0), cage=True)
        if z1_ < LEG_TOP:
            pz = z1_ + 0.06
            b.box((1.4, 1.2, 0.05), (lp.x, lp.y - 0.55, pz - 0.025), mat=GRATE, tint=GREY, solid=True, vary=0)
            for sx in (-0.7, 0.7):
                railing(b, (lp.x + sx, lp.y - 1.15, pz), (lp.x + sx, lp.y + 0.05, pz), 1.1, spacing=1.2,
                        tint=YELLOW)
            railing(b, (lp.x - 0.7, lp.y - 1.15, pz), (lp.x + 0.7, lp.y - 1.15, pz), 1.1, spacing=1.4, tint=YELLOW)
    for z in (4.0, 12.0, 20.0):
        for sx in (-0.3, 0.3):
            b.beam((lp.x + sx, lp.y + 0.05, z), (lp.x + sx * 2.5, lp.y + (RT + 0.85 - RL) * 0.9, z), 0.06, 0.06,
                   mat=PAINT, tint=GREY)
    sp = Vector((cx, cy, 0)) + Vector((math.cos(la), math.sin(la), 0)) * (RT + 0.3)
    ladder(b, (sp.x, sp.y, LEG_TOP + 0.06), z1 - LEG_TOP + 0.3, (0, 0, math.degrees(la) + 90.0), cage=True)
    # The overflow down a column to a sump with a splash plate.
    oa = LEG_ANG[5]
    op = Vector((cx, cy, 0)) + Vector((math.cos(oa), math.sin(oa), 0)) * (RL + 0.6)
    b.tube([(cx + (RT + 0.1) * math.cos(oa), cy + (RT + 0.1) * math.sin(oa), z1 - 0.6), (op.x, op.y, z1 - 0.6),
            (op.x, op.y, 0.9), (op.x + 0.6 * math.cos(oa), op.y + 0.6 * math.sin(oa), 0.5)], 0.14, mat=PAINT,
           tint=OFFWHITE, sides=10)
    P.grid_box(b, (1.6, 1.6, 0.4), (op.x + 0.9 * math.cos(oa), op.y + 0.9 * math.sin(oa), 0.1), mat=CONCRETE,
               tint=srgb(0.7, 0.69, 0.66), step=0.8)
    P.blob(b, (op.x + 1.6 * math.cos(oa), op.y + 1.6 * math.sin(oa), 0.0), 1.6, srgb(0.2, 0.24, 0.16), points=12,
           rough=0.4)


def pump_house(b, at):
    """A board-formed concrete pump house with a corrugated gable roof and a ridge vent, its door open: two pumps on
    plinths with their motors and valves, a control panel; the main out through the wall."""
    x, y = at
    w, d, h = 8.0, 6.0, 3.6
    hw, hd = w / 2, d / 2
    t = 0.3
    with b.frame((x, y, 0), (0, 0, 0)):
        P.grid_box(b, (w + 0.4, d + 0.4, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), step=2.0)
        dx = -1.8
        P.grid_box(b, (w, t, h), (0, hd - t / 2, 0.3 + h / 2), mat=FORMWORK, tint=CONCRETE_T, step=1.2)
        for sx in (-1, 1):
            P.grid_box(b, (t, d - 2 * t, h), (sx * (hw - t / 2), 0, 0.3 + h / 2), mat=FORMWORK, tint=CONCRETE_T,
                       step=1.2)
        P.grid_box(b, (dx - 0.6 + hw, t, h), ((-hw + dx - 0.6) / 2, -hd + t / 2, 0.3 + h / 2), mat=FORMWORK,
                   tint=CONCRETE_T, step=1.2)
        P.grid_box(b, (hw - dx - 0.6, t, h), ((dx + 0.6 + hw) / 2, -hd + t / 2, 0.3 + h / 2), mat=FORMWORK,
                   tint=CONCRETE_T, step=1.2)
        b.box((1.2, t, h - 2.3), (dx, -hd + t / 2, 0.3 + 2.3 + (h - 2.3) / 2), mat=FORMWORK, tint=CONCRETE_T)
        with b.frame((dx + 0.6, -hd - 0.02, 0.3), (0, 0, 95.0)):
            P.grid_box(b, (1.15, 0.06, 2.28), (-0.58, 0.03, 1.14), mat=PAINT, tint=TEAL, step=0.8)
        b.marker("door", (dx, -hd - 0.7, 0.0), kind_of="pump-house")
        for k in range(2):
            vx = 1.0 + 1.8 * k
            b.box((1.2, 0.08, 0.7), (vx, -hd - 0.02, 2.6), mat=PAINT, tint=DARK, solid=False)
            for j in range(5):
                b.box((1.1, 0.1, 0.03), (vx, -hd - 0.06, 2.35 + j * 0.13), (35, 0, 0), mat=PAINT, tint=GREY,
                      solid=False, vary=0)
        # Gable roof of corrugated sheet on purlins, a ridge vent.
        rise = 1.1
        for s in (-1, 1):
            ln = math.hypot(hd + 0.4, rise)
            ang = math.degrees(math.atan2(rise, hd + 0.4))
            P.grid_box(b, (w + 0.6, ln, 0.06), (0, s * (hd + 0.4) / 2, 0.3 + h + rise / 2 + 0.05), (-s * ang, 0, 0),
                       mat=CORRUGATED, tint=srgb(0.5, 0.56, 0.52), step=1.5)
        for sx in (-1, 1):
            b.mesh([(sx * hw, -hd, 0.3 + h), (sx * hw, hd, 0.3 + h), (sx * hw, 0, 0.3 + h + rise)], [[0, 1, 2]],
                   mat=FORMWORK, tint=CONCRETE_T, solid="hull")
        b.box((1.6, 0.6, 0.35), (0, 0, 0.3 + h + rise + 0.2), mat=PAINT, tint=GREY, solid=False)
        P.stencil(b, "PUMP 1", (2.2, -hd - 0.02, 1.4), (0, 0, 0), 0.25, DARK, align="centre")
        # Inside: two pump sets on plinths, the header pipe, valves, the panel.
        for k, px in enumerate((-1.5, 1.6)):
            b.box((1.6, 0.8, 0.3), (px, 1.2, 0.45), mat=CONCRETE, tint=CONCRETE_T)
            b.cyl(0.32, 0.8, (px - 0.35, 1.2, 0.95), (0, 90, 0), mat=PAINT, tint=srgb(0.25, 0.4, 0.5), sides=14)
            b.cyl(0.36, 0.3, (px + 0.4, 1.2, 0.95), (0, 90, 0), mat=PAINT, tint=BLUE_PIPE, sides=14)
            b.tube([(px + 0.4, 1.2, 1.25), (px + 0.4, 1.2, 2.2), (px + 0.4, 2.2, 2.2)], 0.12, mat=PAINT, tint=BLUE_PIPE,
                   sides=10)
            P.handwheel(b, (px + 0.4, 0.95, 1.8), (0, 0, 0), 0.18, RED, spokes=3)
        b.tube([(-2.6, 2.2, 2.2), (hw + 1.2, 2.2, 2.2)], 0.18, mat=PAINT, tint=BLUE_PIPE, sides=12)
        b.box((0.9, 0.3, 1.4), (-3.2, -1.5, 1.3), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
        b.box((0.5, 0.02, 0.3), (-3.2, -1.66, 1.6), mat=PAINT, tint=YELLOW, solid=False, vary=0)
        b.marker("loot", (-2.6, -0.4, 0.32), table="barrel")


def build(b):
    b.set_pad(("rect", 64.0, 44.0), ground="gravel", blend=10.0, clear=3.0)
    P.long_road(b, (-34.0, -19.0), (34.0, -19.0), width=7.0)
    tower(b)

    # The fence round the tower's foot, a gate toward the road.
    fx0, fx1, fy0, fy1 = T.x - 10.0, T.x + 10.0, T.y - 10.0, T.y + 10.0
    g0, g1 = T.x - 2.5, T.x + 2.5
    fence(b, [(g1, fy0, 0), (fx1, fy0, 0), (fx1, fy1, 0), (fx0, fy1, 0), (fx0, fy0, 0), (g0, fy0, 0)], height=2.2,
          tint=P.GALV)
    for gx in (g0, g1):
        b.box((0.2, 0.2, 2.6), (gx, fy0, 1.3), mat=PAINT, tint=GREY, bevel=0.02)
    with b.frame((g1, fy0, 0), (0, 0, 72.0)):
        for z in (0.2, 1.1, 2.0):
            b.box((5.0, 0.05, 0.05), (-2.5, 0, z), mat=PAINT, tint=GREY, solid=False)
        b.box((5.0, 0.02, 1.9), (-2.5, 0, 1.1), mat="M_Mon_Chainlink", tint=GREY, solid=False, vary=0)

    pump_house(b, (14.0, -5.0))
    # The main from the pump house to the riser on saddles, a valve pit at the tower's foot.
    pts = [(18.25, -2.8, 2.4), (20.0, -2.8, 2.4), (20.0, -2.8, 0.7), (20.0, 4.0, 0.7), (T.x + 1.6, 4.0, 0.7),
           (T.x + 0.9, T.y, 0.7)]
    b.tube(pts, 0.2, mat=PAINT, tint=BLUE_PIPE, sides=12, solid=False)
    for x in (16.0, 10.0, 4.0, -1.0):
        P.pipe_support(b, (x, 4.0, 0.7), 0.2, 0.0)
    P.gate_valve(b, (12.0, 4.0, 0.7), 0.0, 0.2, actuator=False)
    P.grid_box(b, (1.6, 1.6, 0.12), (T.x + 2.4, T.y - 2.0, 0.06), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), step=0.8)
    b.box((1.2, 1.2, 0.04), (T.x + 2.4, T.y - 2.0, 0.13), mat=PAINT, tint=DARK, solid=False)
    # The traveller's standpipe and trough by the road; barrels; a diesel generator by the pump house.
    with b.frame((6.0, -14.5, 0.0), (0, 0, 0)):
        b.box((2.4, 1.4, 0.15), (0, 0, 0.075), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
        b.cyl(0.08, 1.6, (-0.8, 0.3, 0.95), mat=PAINT, tint=BLUE_PIPE, sides=10)
        b.tube([(-0.8, 0.3, 1.75), (-0.8, 0.1, 1.9), (-0.8, -0.2, 1.75), (-0.8, -0.25, 1.5)], 0.06, mat=PAINT,
               tint=BLUE_PIPE, sides=8)
        P.handwheel(b, (-0.66, 0.3, 1.2), (0, 0, 90), 0.14, RED, spokes=3)
        b.box((1.6, 0.6, 0.55), (0.4, -0.1, 0.42), mat=CONCRETE, tint=CONCRETE_T)
        b.box((1.45, 0.45, 0.04), (0.4, -0.1, 0.62), mat="M_Mon_Gloss", tint=srgb(0.15, 0.2, 0.22), solid=False)
        b.marker("refill", (0.4, -0.9, 0.0), liquid="water")
    b.piece("drum-stack", (8.6, -8.6, 0.0), (0, 0, 15.0))
    b.marker("loot", (9.6, -7.6, 0.05), table="barrel")
    P.generator(b, (19.8, -7.4, 0.3), (0, 0, 90.0), tint=srgb(0.35, 0.5, 0.4))
    b.piece("lamp-post", (20.6, 0.2, 0.0), (0, 0, -135.0))
    for (x, y, s) in ((-21.0, -12.0, 1.0), (24.0, 12.0, 1.2), (-24.0, 14.0, 0.9), (2.0, 15.0, 0.8)):
        P.bush(b, (x, y, 0.0), s, P.LEAF, lumps=4)

    b.zone("no-build", (0, 0, 0), size=(76.0, 56.0, 40.0))
    b.meta.update({"tier": "small", "landmark": True, "spawn": "town-farm"})


SUN = (30.0, 220.0)
PEOPLE = [((T.x + 1.0, T.y - 9.0, 0.0), 20.0)]
VIEWS = [
    {"name": "tower", "target": (0.0, 0.0, 13.0), "distance": 72, "yaw": 26, "pitch": 10, "lens": 30},
    {"name": "foot", "target": (T.x, T.y - 2.0, 6.0), "distance": 16, "yaw": -18, "pitch": 14, "lens": 20},
    {"name": "top", "target": (T.x, T.y, 28.0), "distance": 22, "yaw": -28, "pitch": 10, "lens": 28},
]
