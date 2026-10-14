"""Deep Bore (MONUMENTS.md §5.5, landmark tier): a planetary drill rig over a shaft a kilometre across, on a
metal-rich world. Four lattice legs stand on the rim and lean in to a crown 520 m above the pit, where the hoist
sheaves turn; the drill string hangs from it into the dark, its top drive stopped halfway down. Round the rim: the
concrete collar of the shaft and its parapet, the winch hall the hoist ropes run down to, the power hall, a mud
plant, crushers and ore bins at the head of the spiral haul road, conveyors out to spoil heaps a hundred metres
high. The pit is terrain in the game; this site models its rim, the shaft's first stretch and the top two turns of
the haul road cut into its wall.

Look-dev only: a preview build cuts a hole in the preview's ground plane over the shaft (by wrapping
`preview.ground` for this site's renders) and carries the wall on down to a dark bottom.
"""
import math

from mathutils import Vector

from monkit import kit
from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW, cable,
                        lattice_tower, railing, srgb, stripes, tank)
from monkit.structures import conveyor, road, shed, silo
from monkit.surfaces import ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "deep-bore"
TITLE = "Deep Bore"
SEED = 5101
BUILDER = {"chunk": 160.0, "max_edge": 14.0, "lod_min": (0.0, 2.0, 14.0)}
LODS = [450.0, 2800.0, 80000.0]

R0 = 500.0               # the shaft's radius at the rim
BENCH = 34.0             # width of the haul road's benches
TURN = 240.0             # depth of one turn of the haul road
APEX = 520.0             # the crown's height
LEG_R = 590.0            # where the legs stand
STEEL = srgb(0.85, 0.58, 0.14)
STEEL_DARK = srgb(0.40, 0.36, 0.30)
CONC = srgb(0.66, 0.65, 0.62)
STRATA = (srgb(0.48, 0.42, 0.36), srgb(0.40, 0.36, 0.32), srgb(0.55, 0.50, 0.44), srgb(0.36, 0.33, 0.30),
          srgb(0.50, 0.44, 0.38))


def ramp_z(th, turn=0):
    """Height of the haul road at angle `th` (0..2pi) on turn `turn`: it leaves the rim at th = 0 and spirals down
    clockwise seen from above."""
    return -TURN * (turn + th / (2 * math.pi))


def _strip(b, th0, th1, r, z_top, z_bot, tint, n=96, mat=CONCRETE, wear=0.7):
    """A band of the shaft wall at radius r between angles th0..th1 (radians), from z_top(th) down to z_bot(th)."""
    verts, faces = [], []
    for k in range(n + 1):
        th = th0 + (th1 - th0) * k / n
        x, y = math.cos(-th) * r, math.sin(-th) * r
        zt, zb = z_top(th), z_bot(th)
        verts += [(x, y, zt), (x, y, zb)]
    for k in range(n):
        a = 2 * k
        faces.append([a, a + 2, a + 3, a + 1])
    b.mesh(verts, faces, mat=mat, tint=tint, solid=False, smooth=True, wear=wear, vary=0.03)


def shaft(b, depth_export=2 * TURN, preview=False):
    """The shaft wall with the haul road's benches: per turn an upper wall down to the road, the road, and the
    wall below it; strata in bands of rock; the concrete collar at the top."""
    tau = 2 * math.pi
    turns = 2
    for t in range(turns):
        r_up = R0 - BENCH * t
        r_dn = r_up - BENCH
        # Wall above the road, in strata bands of 24 m.
        for band in range(int(TURN * 2 / 24)):
            z_hi = -band * 24.0
            z_lo = z_hi - 24.0

            def top(th, z_hi=z_hi, t=t):
                return min(z_hi, ramp_z(th, t - 1) if t > 0 else 0.0) if t > 0 else min(z_hi, 0.0)

            def bot(th, z_lo=z_lo, t=t):
                return max(z_lo, ramp_z(th, t))
            ths = [k * tau / 96 for k in range(97)]
            if all(top(th) <= bot(th) + 0.1 for th in ths):
                continue
            _strip(b, 0.0, tau, r_up, lambda th: max(bot(th), top(th)), bot, STRATA[band % len(STRATA)])
        # The road itself: a bench sloping down round the turn, a berm along its open edge, lamps.
        verts, faces = [], []
        n = 192
        for k in range(n + 1):
            th = tau * k / n
            z = ramp_z(th, t)
            c, s = math.cos(-th), math.sin(-th)
            verts += [(c * r_up, s * r_up, z), (c * (r_dn + 2.0), s * (r_dn + 2.0), z)]
        for k in range(n):
            a = 2 * k
            faces.append([a, a + 1, a + 3, a + 2])
        b.mesh(verts, faces, mat=CONCRETE, tint=srgb(0.46, 0.43, 0.39), solid=False, smooth=True, wear=0.8)
        berm = []
        for k in range(n + 1):
            th = tau * k / n
            z = ramp_z(th, t)
            berm.append((math.cos(-th) * (r_dn + 3.0), math.sin(-th) * (r_dn + 3.0), z + 0.6))
        b.tube(berm, 1.4, mat=CONCRETE, tint=srgb(0.42, 0.39, 0.35), sides=6, cap=False)
        for k in range(0, n, 8):
            th = tau * (k + 0.5) / n
            z = ramp_z(th, t)
            b.piece("lamp-post", (math.cos(-th) * (r_up - 3.0), math.sin(-th) * (r_up - 3.0), z),
                    (0, 0, -math.degrees(th) + 180))
    # Where each turn's road starts below the last turn's end: the radial face of rock between them.
    for t in range(turns):
        r_up, r_dn = R0 - BENCH * t, R0 - BENCH * (t + 1)
        z_top = 0.0 if t == 0 else ramp_z(0.0, t - 1) + 0.0
        z_top = ramp_z(0.0, t)
        z_bot = ramp_z(2 * math.pi, t)
        b.mesh([(r_dn, 0.0, z_bot), (r_up, 0.0, z_bot), (r_up, 0.0, z_top), (r_dn, 0.0, z_top)], [[0, 1, 2, 3]],
               mat=CONCRETE, tint=STRATA[t], solid=False, smooth=False, wear=0.7)
    # The wall below the second turn (preview carries it down to the dark).
    deep = -(turns * TURN) - (420.0 if preview else 60.0)
    r_last = R0 - BENCH * turns
    for band in range(int(-deep / 30) + 1):
        z_hi = ramp_z(0.0, turns - 1) - band * 30.0 + 0.0
        z_lo = z_hi - 30.0
        if z_lo < deep:
            z_lo = deep
        if z_hi <= deep:
            break
        _strip(b, 0.0, tau, r_last, lambda th, zh=z_hi, t=turns: min(zh, ramp_z(th, t - 1)),
               lambda th, zl=z_lo: zl, STRATA[(band + 2) % len(STRATA)])
    if preview:
        b.cyl(r_last + 1.0, 1.0, (0, 0, deep), mat=MATTE, tint=srgb(0.02, 0.02, 0.02), sides=64, solid=False,
              vary=0)
    # The collar: a concrete lining round the top 30 m, its parapet and a lamp ring; ribs every 15 degrees.
    _strip(b, 0.0, tau, R0 - 0.6, lambda th: 1.4, lambda th: max(-30.0, ramp_z(th, 0)), CONC, n=128, mat=FORMWORK,
           wear=0.5)
    b.lathe([(R0 + 6.0, 0.0), (R0 + 6.0, 1.4), (R0 - 0.6, 1.4), (R0 - 0.6, 0.0), (R0 + 6.0, 0.0)], (0, 0, 0),
            mat=CONCRETE, tint=CONC, sides=128, close=False, solid=False)
    for k in range(24):
        th = 2 * math.pi * k / 24
        b.box((3.0, 2.0, 30.0), (math.cos(th) * (R0 - 1.2), math.sin(th) * (R0 - 1.2), -14.0), (0, 0, math.degrees(th)),
              mat=FORMWORK, tint=CONC, solid=False)
    pts = [(math.cos(a) * (R0 + 1.0), math.sin(a) * (R0 + 1.0), 1.4) for a in (2 * math.pi * k / 96 for k in range(97))]
    for p, q in zip(pts, pts[1:]):
        b.box(((Vector(q) - Vector(p)).length + 0.1, 0.6, 1.2), tuple((Vector(p) + Vector(q)) / 2 + Vector((0, 0, 0.6))),
              (0, 0, math.degrees(math.atan2(q[1] - p[1], q[0] - p[0]))), mat=CONCRETE, tint=CONC, solid=True)
    for k in range(0, 96, 2):
        a = 2 * math.pi * (k + 0.5) / 96
        b.box((0.4, 0.4, 0.5), (math.cos(a) * (R0 + 1.0), math.sin(a) * (R0 + 1.0), 2.85), mat=PAINT, tint=YELLOW,
              solid=False, vary=0)


def headframe(b):
    """Four lattice legs from the rim to the crown, ring trusses tying them, the crown house with its sheaves, the
    drill string and the stopped top drive, the hoist ropes down one leg to the winch hall."""
    apex = Vector((0, 0, APEX))
    tops = []
    for k in range(4):
        a = math.radians(45 + 90 * k)
        foot = Vector((math.cos(a) * LEG_R, math.sin(a) * LEG_R, 30.0))
        top = Vector((math.cos(a) * 26.0, math.sin(a) * 26.0, APEX - 8.0))
        tops.append(top)
        P.truss(b, foot, top, w=26.0, h=26.0, member=1.1, panel=26.0, tint=STEEL, chords=2.2, solid=True)
        # Foundation and pin.
        with b.frame((foot.x, foot.y, 0), (0, 0, math.degrees(a))):
            b.box((70.0, 60.0, 30.0), (0, 0, 15.0), mat=FORMWORK, tint=CONC, bevel=0.5)
            b.extrude([(-35, 30), (35, 30), (22, 38), (-22, 38)], 60.0, (0, 0, 0), (0, 0, 0), mat=CONCRETE, tint=CONC)
            for s in (-1, 1):
                b.cyl(6.0, 12.0, (0, s * 18.0, 42.0), (90, 0, 0), mat=PAINT, tint=STEEL_DARK, sides=24)
            stripes(b, (-35.1, -30.0, 0), (-35.1, 30.0, 0), 2.0, width=1.6, at_z=1.0, normal=(-1, 0, 0))
            P.door(b, (35.05, 8.0, 0), 90.0, marker="leg-stair" if k == 0 else None)
            P.aviation_lamp(b, (0, 0, 39.0), 1.0)
        # A stair and ladder climb up the leg's chord: platforms every 52 m.
        for q in range(1, 13):
            f = q / 14
            p = foot.lerp(top, f)
            b.box((14.0, 14.0, 0.8), (p.x, p.y, p.z - 13.5), (0, 0, math.degrees(a)), mat=GRATE, tint=GREY,
                  solid=False)
    # Ring trusses at two levels.
    for f in (0.36, 0.7):
        pts = []
        for k in range(4):
            a = math.radians(45 + 90 * k)
            foot = Vector((math.cos(a) * LEG_R, math.sin(a) * LEG_R, 30.0))
            pts.append(foot.lerp(tops[k], f))
        for k in range(4):
            P.truss(b, pts[k], pts[(k + 1) % 4], w=12.0, h=14.0, member=0.8, panel=24.0, tint=STEEL, chords=1.4)
    # Crown house and sheaves.
    b.box((90.0, 90.0, 12.0), (0, 0, APEX - 2.0), mat=PAINT, tint=STEEL_DARK)
    b.box((70.0, 54.0, 34.0), (0, 0, APEX + 21.0), mat=CORRUGATED, tint=srgb(0.70, 0.71, 0.70))
    b.box((71.0, 55.0, 3.0), (0, 0, APEX + 30.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.box((72.0, 56.0, 2.0), (0, 0, APEX + 39.0), mat=PAINT, tint=DARK, solid=False)
    for x in (-18.0, 18.0):
        b.cyl(16.0, 5.0, (x, -10.0, APEX + 46.0), (0, 90, 0), mat=PAINT, tint=STEEL_DARK, sides=40)
        b.cyl(4.0, 7.0, (x, -10.0, APEX + 46.0), (0, 90, 0), mat=PAINT, tint=YELLOW, sides=16)
        for k in range(8):
            a = math.pi * k / 8
            b.box((0.8, 30.0, 1.2), (x, -10.0, APEX + 46.0), (math.degrees(a), 0, 0), mat=PAINT, tint=STEEL_DARK,
                  solid=False)
    b.box((60.0, 8.0, 6.0), (0, -10.0, APEX + 40.0), mat=PAINT, tint=DARK, solid=False)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        P.aviation_lamp(b, (math.cos(a) * 44.0, math.sin(a) * 44.0, APEX + 5.6), 1.2)
    b.cyl(1.2, 60.0, (20.0, 20.0, APEX + 70.0), mat=PAINT, tint=GREY, sides=8, radius2=0.5)
    P.aviation_lamp(b, (20.0, 20.0, APEX + 101.0), 1.4, reach=4000.0)
    b.marker("landmark", (0, 0, APEX + 101.0), kind_of="deep-bore", visible_km=60)
    b.marker("loot", (0, 20.0, APEX + 4.2), table="vault-crate")
    # The drill string, the top drive stopped on it, guide ropes to the legs.
    b.cyl(4.2, APEX + 600.0, (0, 0, (APEX - 600.0) / 2), mat=PAINT, tint=srgb(0.34, 0.33, 0.32), sides=20)
    for z in range(-560, int(APEX) - 20, 30):
        b.cyl(5.6, 2.4, (0, 0, z), mat=PAINT, tint=DARK, sides=20, solid=False, vary=0)
    td = 160.0
    b.box((34.0, 34.0, 40.0), (0, 0, td), mat=PAINT, tint=YELLOW, bevel=1.0)
    b.box((36.0, 36.0, 4.0), (0, 0, td + 14.0), mat=PAINT, tint=DARK, solid=False)
    for s in (-1, 1):
        b.box((10.0, 36.0, 24.0), (s * 21.0, 0, td - 2.0), mat=PAINT, tint=STEEL_DARK)
        b.cyl(3.0, 30.0, (s * 12.0, 12.0, td + 35.0), mat=PAINT, tint=GREY, sides=12)
    stripes(b, (-17.0, -17.05, 0), (17.0, -17.05, 0), 3.0, width=2.0, at_z=td - 20.0)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        cable(b, (math.cos(a) * 17.0, math.sin(a) * 17.0, td + 18.0), (math.cos(a) * 30.0, math.sin(a) * 30.0,
                                                                         APEX - 12.0), sag=0.0, radius=0.5)
        f = 0.36
        foot = Vector((math.cos(a) * LEG_R, math.sin(a) * LEG_R, 30.0))
        g = foot.lerp(tops[k], f)
        cable(b, (math.cos(a) * 17.0, math.sin(a) * 17.0, td - 16.0), tuple(g), sag=0.01, radius=0.4)
    # Hoist ropes down the south-west leg to the winch hall.
    a = math.radians(225)
    for e in (-6.0, 6.0):
        off = Vector((-math.sin(a), math.cos(a), 0)) * e
        cable(b, tuple(Vector((-18.0 if e < 0 else 18.0, -10.0, APEX + 46.0))), tuple(
            Vector((math.cos(a) * (LEG_R + 120.0), math.sin(a) * (LEG_R + 120.0), 30.0)) + off), sag=0.0, radius=0.6)


def rim_works(b):
    """Round the rim: the winch hall, the power hall, the mud plant, crushers and ore bins at the haul road's head,
    workshops, conveyors to the spoil heaps, the heaps themselves."""
    # Winch hall where the hoist ropes come down (south-west, beyond the leg).
    a = math.radians(225)
    c = Vector((math.cos(a) * (LEG_R + 150.0), math.sin(a) * (LEG_R + 150.0), 0))
    P.hall(b, tuple(c), -45.0, 110.0, 60.0, 40.0, tint=srgb(0.70, 0.71, 0.70), band=ORANGE, band2=TEAL, mat=CORRUGATED,
           ruin=0.3, pilaster=10.0, doors=[("front", -40.0), ("left", 0.0)], bay_doors=[("front", 20.0, 12.0, 10.0)],
           stair=(57.0, 10.0, 0))
    for x in (-25.0, 25.0):
        with b.frame(tuple(c), (0, 0, -45.0)):
            b.cyl(14.0, 30.0, (x, 34.0, 26.0), (0, 90, 0), mat=PAINT, tint=STEEL_DARK, sides=32)
    # Power hall and stacks (south-east).
    a = math.radians(300)
    c = Vector((math.cos(a) * 760.0, math.sin(a) * 760.0, 0))
    P.hall(b, tuple(c), 30.0, 120.0, 50.0, 30.0, tint=OFFWHITE, band=TEAL, band2=ORANGE, ruin=0.35, pilaster=10.0,
           louvres=4, doors=[("front", 30.0), ("front", -30.0)])
    from monkit.structures import chimney
    for k in range(2):
        q = c + Vector((math.cos(math.radians(30)) * (-30.0 + 60.0 * k), math.sin(math.radians(30)) * (-30.0 + 60 * k),
                        0)) + Vector((-math.sin(math.radians(30)), math.cos(math.radians(30)), 0)) * 46.0
        chimney(b, (q.x, q.y, 0), 110.0, 6.0, 4.0)
    for k in range(3):
        q = c + Vector((math.cos(math.radians(30)), math.sin(math.radians(30)), 0)) * (80.0 + k * 26.0)
        tank(b, (q.x, q.y, 0), 10.0, 14.0, tint=OFFWHITE, band=ORANGE, ladder_side=0.0, roof="dome")
    # Mud plant (north): tanks, a settling pond, pumps, a pipe to the crown.
    a = math.radians(100)
    c = Vector((math.cos(a) * 700.0, math.sin(a) * 700.0, 0))
    for k in range(6):
        q = c + Vector(((k % 3) * 30.0 - 30.0, (k // 3) * 30.0 - 15.0, 0))
        tank(b, (q.x, q.y, 0), 11.0, 16.0, tint=srgb(0.6, 0.62, 0.62), band=YELLOW, ladder_side=90.0)
    b.box((90.0, 60.0, 2.0), (c.x + 90.0, c.y + 10.0, 0.2), mat=CONCRETE, tint=CONC, solid=False)
    b.box((84.0, 54.0, 0.3), (c.x + 90.0, c.y + 10.0, 1.0), mat=GLASS, solid=False)
    P.pipe_run(b, [(c.x - 40.0, c.y - 30.0, 6.0), (math.cos(a) * (R0 + 30.0), math.sin(a) * (R0 + 30.0), 6.0)], 1.6,
               tint=YELLOW)
    # Crushers, ore bins and the conveyors at the head of the haul road (east, where the road leaves the rim).
    c = Vector((R0 + 90.0, -40.0, 0))
    b.box((60.0, 40.0, 36.0), (c.x, c.y, 18.0), mat=CORRUGATED, tint=srgb(0.64, 0.6, 0.5))
    b.box((61.0, 41.0, 2.0), (c.x, c.y, 30.0), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    b.extrude([(-20, 36), (20, 36), (12, 52), (-12, 52)], 30.0, (c.x, c.y, 0), (0, 0, 90), mat=PAINT,
              tint=STEEL_DARK)
    silo(b, (c.x + 60.0, c.y + 40.0, 0), 9.0, 26.0, cone=8.0, legs=10.0, count=3, spacing=22.0)
    road(b, [(R0 + 2.0, 0.0, 0.0), (R0 + 140.0, 30.0, 0.0), (R0 + 400.0, 60.0, 0.0)], width=24.0, lines=False)
    for k, (yy, rot) in enumerate(((-6.0, 10.0), (14.0, 175.0))):
        b.piece("ore-hauler", (R0 + 120.0 + k * 40.0, yy + 20.0, 0.1), (0, 0, rot))
    # Conveyors to the spoil heaps, the heaps.
    heaps = [(1050.0, -380.0, 120.0, 260.0), (1150.0, 200.0, 95.0, 220.0), (-300.0, 1080.0, 110.0, 250.0)]
    starts = [Vector((c.x + 30.0, c.y - 20.0, 30.0)), Vector((c.x + 30.0, c.y + 20.0, 30.0)),
              Vector((math.cos(math.radians(110)) * (R0 + 60.0), math.sin(math.radians(110)) * (R0 + 60.0), 20.0))]
    for (hx, hy, hh, hr), s0 in zip(heaps, starts):
        spoil_heap(b, (hx, hy), hr, hh)
        top = Vector((hx, hy, hh * 0.92))
        mid = s0.lerp(top, 0.5)
        mid.z = max(s0.z, top.z) * 0.85
        conveyor(b, tuple(s0), tuple(mid), 3.0)
        conveyor(b, tuple(mid), tuple(top), 3.0)
        b.box((14.0, 14.0, 10.0), tuple(mid + Vector((0, 0, 4.0))), mat=CORRUGATED, tint=srgb(0.62, 0.6, 0.52))
        for q in range(1, 6):
            p = s0.lerp(mid, q / 6) if q < 3 else mid.lerp(top, (q - 3) / 3)
            if p.z > 6:
                lattice_tower(b, (p.x, p.y, 0), p.z - 1.0, 6.0, 4.0, sections=max(3, int(p.z / 10)), member=0.3,
                              tint=STEEL_DARK)
    # Workshops and stores round the rim; lamp masts; haulers; the access road.
    for k, ang in enumerate((20.0, 160.0, 200.0, 250.0, 340.0)):
        a = math.radians(ang)
        q = Vector((math.cos(a) * 640.0, math.sin(a) * 640.0, 0))
        shed(b, tuple(q), (0, 0, ang + 90), 44.0, 26.0, 12.0, doors=[("front", 0.0, 10.0, 8.0)],
             tint=(srgb(0.62, 0.64, 0.63), OFFWHITE, srgb(0.6, 0.58, 0.5))[k % 3], ruin=0.15 + 0.1 * (k % 3))
    for k in range(16):
        a = 2 * math.pi * (k + 0.25) / 16
        P.light_mast(b, (math.cos(a) * (R0 + 16.0), math.sin(a) * (R0 + 16.0), 0), 30.0)
    road(b, [(R0 + 400.0, 60.0, 0.0), (1600.0, 90.0, 0.0)], width=14.0)
    ring = [(math.cos(a) * (R0 + 40.0), math.sin(a) * (R0 + 40.0), 0.0) for a in (2 * math.pi * k / 48 for k in range(49))]
    road(b, ring, width=14.0, lines=False)
    for k in range(8):
        a = 2 * math.pi * (k + 0.6) / 8
        b.piece("container-20", (math.cos(a) * (R0 + 70.0), math.sin(a) * (R0 + 70.0), 0.0),
                (0, 0, math.degrees(a) + b.rng.uniform(-10, 10)), tint=(ORANGE, TEAL, OFFWHITE)[k % 3])
    for k in range(6):
        a = b.rng.uniform(0, 2 * math.pi)
        r = b.rng.uniform(R0 + 30.0, R0 + 260.0)
        P.rubble_heap(b, (math.cos(a) * r, math.sin(a) * r, 0), b.rng.uniform(8, 18), b.rng.uniform(4, 9),
                      tint=P.EARTH, mat=CONCRETE, count=10)
    b.marker("loot", (R0 + 100.0, -60.0, 0.2), table="crate")
    b.marker("loot", (math.cos(math.radians(225)) * (LEG_R + 150.0), math.sin(math.radians(225)) * (LEG_R + 150.0),
                      0.2), table="military-crate")
    b.marker("door", (R0 + 3.0, 0.0, 0.0), kind_of="haul-road")
    b.marker("guard", (R0 + 80.0, 20.0, 0.0), role="warden-crawler")


def spoil_heap(b, at, r, h, layers=4):
    """A terraced spoil heap: benches of tipped rock stacked into a flat-topped cone, haul tracks on its flanks."""
    x, y = at
    yaw = b.rng.uniform(0, 180)
    tints = (srgb(0.56, 0.50, 0.42), srgb(0.50, 0.45, 0.38), srgb(0.60, 0.54, 0.46), srgb(0.47, 0.42, 0.36))
    z = 0.0
    for k in range(layers):
        f = 1.0 - 0.21 * k
        lh = h / layers
        P.rock(b, (x + b.rng.uniform(-8, 8), y + b.rng.uniform(-8, 8), z - 0.5), (r * 2 * f, r * 1.7 * f, lh * 1.6),
               (0, 0, yaw + k * 17.0), tint=tints[k % len(tints)], n=7, rough=0.12, sink=0.0, flat_top=0.62,
               solid="hull" if k == 0 else False, smooth=True)
        z += lh
    for k in range(3):
        a = math.radians(yaw + 120 * k)
        P.mound_along(b, [(x + math.cos(a) * r * 1.05, y + math.sin(a) * r * 1.05, 0),
                          (x + math.cos(a) * r * 0.6, y + math.sin(a) * r * 0.6, h * 0.4)], width=10.0, height=1.5,
                      tint=srgb(0.42, 0.38, 0.33), rough=0.1)


def more_works(b):
    """Stores, yards, tanks and a substation filling the ring round the rim."""
    for k, ang in enumerate((60.0, 75.0, 130.0, 145.0, 185.0, 280.0, 320.0, 355.0)):
        a = math.radians(ang)
        r = 720.0 + 40.0 * (k % 2)
        q = (math.cos(a) * r, math.sin(a) * r, 0)
        if k % 3 == 0:
            P.hall(b, q, ang + 90, 60.0, 30.0, 18.0, tint=(OFFWHITE, srgb(0.66, 0.68, 0.66))[k % 2], band=ORANGE,
                   band2=TEAL, ruin=0.25 + 0.1 * (k % 3), pilaster=7.5, doors=[("front", -18.0)],
                   bay_doors=[("front", 12.0, 9.0, 8.0)])
        elif k % 3 == 1:
            shed(b, q, (0, 0, ang + 90), 50.0, 30.0, 14.0, doors=[("front", 0.0, 12.0, 10.0)],
                 tint=srgb(0.6, 0.6, 0.55), ruin=0.2)
        else:
            for j in range(4):
                tq = (q[0] + math.cos(a + 1.57) * (j - 1.5) * 24.0, q[1] + math.sin(a + 1.57) * (j - 1.5) * 24.0, 0)
                tank(b, tq, 9.0, 12.0, tint=OFFWHITE if j % 2 else srgb(0.62, 0.64, 0.64), band=ORANGE,
                     ladder_side=ang + 180.0, roof="cone")
    for k in range(12):
        a = math.radians(200.0 + k * 3.0)
        b.piece("ore-hauler", (math.cos(a) * 820.0, math.sin(a) * 820.0, 0.1), (0, 0, 200.0 + k * 3.0 + 90))
    sx, sy = math.cos(math.radians(250)) * 800.0, math.sin(math.radians(250)) * 800.0
    b.box((70.0, 50.0, 0.3), (sx, sy, 0.15), mat=CONCRETE, tint=srgb(0.55, 0.54, 0.51), solid=False)
    for i in range(3):
        b.piece("transformer", (sx - 20.0 + i * 20.0, sy + 8.0, 0.3))
    for i in range(2):
        b.piece("gantry", (sx - 12.0 + i * 24.0, sy - 14.0, 0.3))
    for k in range(5):
        a = math.radians(160.0 + k * 2.5)
        b.piece("container-20", (math.cos(a) * 860.0, math.sin(a) * 860.0, 0.0), (0, 0, 160.0 + k * 2.5 + 90),
                tint=(ORANGE, TEAL, OFFWHITE, YELLOW, ORANGE)[k])


def _preview_hole(radius):
    """Look-dev only: makes this site's preview ground a ring with a hole over the shaft (the shared preview draws
    a flat plane, which would cover the pit)."""
    from monkit import preview
    preview._bore_hole = radius
    if getattr(preview, "_bore_wrapped", False):
        return
    orig = preview.ground

    def ground(size=6000.0, pad=None, pad_colour=(0.42, 0.40, 0.36)):
        g = orig(size=size, pad=pad, pad_colour=pad_colour)
        r = getattr(preview, "_bore_hole", None)
        preview._bore_hole = None
        if r:
            import bmesh
            bm = bmesh.new()
            n = 160
            radii = [r, r * 1.05, r * 1.15, r * 1.4, r * 2.0, r * 3.5, r * 7.0, r * 15.0, size * 0.75]
            rings = [[bm.verts.new((rr * math.cos(2 * math.pi * k / n), rr * math.sin(2 * math.pi * k / n), 0.0))
                      for k in range(n)] for rr in radii]
            for ra, rb in zip(rings, rings[1:]):
                for k in range(n):
                    bm.faces.new((ra[k], ra[(k + 1) % n], rb[(k + 1) % n], rb[k]))
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            for f in bm.faces:
                if f.normal.z < 0:
                    f.normal_flip()
            bm.to_mesh(g.data)
            bm.free()
        return g
    preview.ground = ground
    preview._bore_wrapped = True


def build(b):
    # The pad: the rim works in a ring round the shaft (a C, so it never covers the pit).
    outer = [(math.cos(math.radians(a)) * 900.0, math.sin(math.radians(a)) * 900.0) for a in range(4, 357, 8)]
    inner = [(math.cos(math.radians(a)) * (R0 + 4.0), math.sin(math.radians(a)) * (R0 + 4.0)) for a in range(356, 3, -8)]
    b.set_pad(("poly", outer + inner), ground="gravel", blend=60.0, clear=20.0)
    preview = not kit.EXPORT["enabled"]
    if preview:
        _preview_hole(R0)
    shaft(b, preview=preview)
    headframe(b)
    rim_works(b)
    more_works(b)
    b.zone("radiation", (0, 0, -300.0), radius=260.0, level=3)
    b.zone("no-build", (0, 0, 0), radius=1300.0)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 60,
                   "terrain": "a shaft 1 km across: the terrain must open a pit of radius 500 m under the site"})


SUN = (28.0, 140.0)
FOG = 0.0001
GROUND = 80000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (0, 0, 220), "distance": 9000, "yaw": -25, "pitch": 1.4, "lens": 85, "fog": 0.00009},
    {"name": "overview", "target": (0, 0, 40), "distance": 2400, "yaw": 30, "pitch": 34, "lens": 30,
     "fog": 0.00005},
    {"name": "foot", "target": (373.1, 62.9, 71.5), "distance": 200, "yaw": 45, "pitch": -20, "lens": 16,
     "fog": 0.00004, "people": [((500.0, -55.2, 1.4), 200.0), ((501.6, -53.0, 1.4), 150.0)],
     "ship": ((501.9, -40.4, 1.4), 100.0)},
]
