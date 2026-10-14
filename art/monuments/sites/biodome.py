"""Biodome (MONUMENTS.md §5.3, medium tier): a colonists' geodesic greenhouse 94 m across and 70 m tall on a
concrete ring wall, half its panes gone and its beds run wild under a dead irrigation mast. A stair tower and a
stepped catwalk up the outside of the frame lead to the crown, where the vent lantern keeps the best crates; the
air inside carries spores (contamination 2). Round it: the pump house and its nutrient tanks, the staff block, hab
cans, polytunnels, an elevated water tank, a broken wind turbine and a solar field.
"""
import math

from monkit import parts_medium as pm
from monkit.kit import (BLUE, CONCRETE, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, RED, TEAL,
                        YELLOW, cable, ladder, lamp, railing, rubble, srgb, stairs, tank, walkway)
from monkit.structures import hab_module, road, solar_array, wind_turbine
from monkit.surfaces import CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PANEL, RUST

KEY = "biodome"
TITLE = "Biodome"
SEED = 2601
BUILDER = {"chunk": 48.0, "max_edge": 4.0}

R = 50.0            # sphere radius
CZ = 20.0           # sphere centre height: apex at 70 m
CUT = 3.0           # the frame stands on a 3 m ring wall
RC = math.sqrt(R * R - (CZ - CUT) ** 2)   # 47.0 m: the dome's foot
RW = R + 1.0        # the outside catwalk runs 1 m above the frame
THETA = -35.0       # the climb's meridian (degrees from +X): front right
RT = 53.5           # the stair tower's distance from the axis
TOP = 45.5          # the tower's top deck, where the bridge leaves for the frame


def _gaps():
    """Openings in the ring wall: (angle, width): the front airlock, a back door, the pump house's pipe door."""
    return [(-90.0, 7.0), (90.0, 4.5), (180.0, 3.2)]


def ring_wall(b):
    gaps = sorted(_gaps())
    # Solid arcs between the gaps, each cut into segments of at most 4.8 m.
    edges = []
    for g, w in gaps:
        half = math.degrees((w / 2 + 0.6) / RC)
        edges.append((g - half, g + half))
    arcs = []
    for i, (lo, hi) in enumerate(edges):
        nxt = edges[(i + 1) % len(edges)][0] + (360.0 if i == len(edges) - 1 else 0.0)
        arcs.append((hi, nxt))
    vent = 0
    for a0, a1 in arcs:
        span = math.radians(a1 - a0) * RC
        n = max(1, int(math.ceil(span / 4.8)))
        for k in range(n):
            s0 = a0 + (a1 - a0) * k / n
            s1 = a0 + (a1 - a0) * (k + 1) / n
            mid = (s0 + s1) / 2
            ln = 2 * RC * math.sin(math.radians(s1 - s0) / 2) + 0.06
            m = math.radians(mid)
            b.box((ln, 0.8, CUT), (math.cos(m) * RC, math.sin(m) * RC, CUT / 2), (0, 0, mid + 90), mat=FORMWORK,
                  tint=CONCRETE_T)
            b.box((ln, 1.1, 0.25), (math.cos(m) * RC, math.sin(m) * RC, CUT - 0.12), (0, 0, mid + 90),
                  mat=CONCRETE, tint=srgb(0.8, 0.79, 0.76), solid=False)
            vent += 1
            if vent % 8 == 3:
                # Louvred vents along the foot.
                b.box((2.0, 0.1, 0.8), (math.cos(m) * (RC + 0.42), math.sin(m) * (RC + 0.42), 1.4),
                      (0, 0, mid + 90), tint=DARK, solid=False, vary=0)
                for j in range(4):
                    b.box((1.9, 0.12, 0.06), (math.cos(m) * (RC + 0.48), math.sin(m) * (RC + 0.48),
                                              1.15 + j * 0.18), (35, 0, mid + 90), tint=GREY, solid=False, vary=0)
    # Each gap gets lintel posts; the frame's ring beam spans it.
    for g, w in gaps:
        for s in (-1, 1):
            a = math.radians(g) + s * (w / 2 + 0.3) / RC
            b.box((0.6, 0.6, CUT + 0.3), (math.cos(a) * RC, math.sin(a) * RC, (CUT + 0.3) / 2),
                  (0, 0, math.degrees(a)), mat=CONCRETE, tint=CONCRETE_T)
    # The ring beam the frame stands on, and the orange band of the old colony livery.
    b.lathe([(RC - 0.45, CUT - 0.05), (RC + 0.45, CUT - 0.05), (RC + 0.45, CUT + 0.45), (RC - 0.45, CUT + 0.45),
             (RC - 0.45, CUT - 0.05)], (0, 0, 0), tint=OFFWHITE, sides=72, close=False, solid=False, vary=0)
    b.lathe([(RC + 0.46, CUT + 0.05), (RC + 0.48, CUT + 0.05), (RC + 0.48, CUT + 0.35), (RC + 0.46, CUT + 0.35),
             (RC + 0.46, CUT + 0.05)], (0, 0, 0), tint=ORANGE, sides=72, close=False, solid=False, vary=0, wear=0.5)


def climb(b):
    """The stair tower, the bridge to the frame and the stepped catwalk up the meridian to the crown."""
    th = math.radians(THETA)
    alpha = THETA + 90.0
    with b.frame((math.cos(th) * RT, math.sin(th) * RT, 0), (0, 0, alpha)):
        # Local frame: +Y toward the dome's axis, which stands at (0, RT).
        tower_tint = srgb(0.74, 0.75, 0.74)
        top, run, y0 = pm.stair_tower(b, (0, 0, 0), (0, 0, 0), TOP, w=3.0, d=7.0, stair_to=22.5, tint=tower_tint)
        # Service deck at 22.5 (a hole over the last flight), then caged ladders to the rest deck and the top.
        pm.slab_holes(b, -1.62, -3.62, 1.62, 3.62, 22.44, 0.06, [(-1.62, -3.62, 0.0, y0 + run)], mat=GRATE,
                      tint=GREY)
        railing(b, (-1.5, -3.5, 22.5), (1.5, -3.5, 22.5))
        railing(b, (1.5, -3.5, 22.5), (1.5, 3.5, 22.5))
        railing(b, (1.5, 3.5, 22.5), (-1.5, 3.5, 22.5))
        railing(b, (-1.5, 3.5, 22.5), (-1.5, y0 + run, 22.5))
        b.marker("loot", (0.8, 1.8, 22.5), table="crate")
        lamp(b, (0, -3.62, 24.2), (0, 0, 180), size=(0.4, 0.25, 0.25))
        # Ladder 1 up the outer (-Y) face to the rest deck at 34.
        ladder(b, (0.0, -3.66, 22.5), 34.0 - 22.5, (0, 0, 0), cage=True)
        pm.slab_holes(b, -1.62, -4.6, 1.62, 3.62, 33.94, 0.06, [(-0.6, -4.6, 0.6, -3.62)], mat=GRATE, tint=GREY)
        railing(b, (-1.62, -4.6, 34.0), (-0.6, -4.6, 34.0))
        railing(b, (0.6, -4.6, 34.0), (1.62, -4.6, 34.0))
        railing(b, (1.62, -4.6, 34.0), (1.62, 3.62, 34.0))
        railing(b, (1.62, 3.62, 34.0), (-1.62, 3.62, 34.0))
        railing(b, (-1.62, 3.62, 34.0), (-1.62, -4.6, 34.0))
        # Ladder 2 up the +X face to the top deck.
        ladder(b, (1.66, 0.0, 34.0), TOP - 34.0, (0, 0, 90), cage=True)
        pm.slab_holes(b, -1.62, -3.62, 2.6, 3.62, TOP - 0.06, 0.06, [(1.62, -0.6, 2.6, 0.6)], mat=GRATE, tint=GREY)
        railing(b, (-1.62, -3.62, TOP), (2.6, -3.62, TOP))
        railing(b, (2.6, -3.62, TOP), (2.6, -0.6, TOP))
        railing(b, (2.6, 0.6, TOP), (2.6, 3.62, TOP))
        railing(b, (-1.62, 3.62, TOP), (-1.62, -3.62, TOP))
        railing(b, (2.6, 3.62, TOP), (0.6, 3.62, TOP))
        pm.aviation_lamp(b, (-1.5, -3.5, TOP + 1.1))
        pm.code_plate(b, (0, -3.75, 2.6), (0, 0, 0), 1.4, 0.5, base=OFFWHITE, ink=DARK, text="T1", stripe=ORANGE)
        # The bridge to the frame.
        r0 = RW * math.sin(math.radians(60.0))
        y_start = RT - r0
        walkway(b, (-0.0, 3.62, TOP), (0.0, y_start, TOP), width=1.2, legs=False)
        b.beam((0.0, y_start - 0.2, TOP - 0.2), (0.0, RT - R * math.sin(math.radians(58)), CZ + R * math.cos(math.radians(58))),
               0.2, 0.2, tint=tower_tint)
        # The catwalk: stair flights between points of the meridian, steep near the foot, gentle at the top.
        phis = [60.0, 50.0, 40.0, 31.0, 22.0, 14.0, 7.5]

        def pt(phi):
            p = math.radians(phi)
            return (0.0, RT - RW * math.sin(p), CZ + RW * math.cos(p))

        for k, (pa, pb) in enumerate(zip(phis, phis[1:])):
            a, c = pt(pa), pt(pb)
            if k:
                a = (0.0, a[1] + 0.9, a[2])
            rise, run_ = c[2] - a[2], c[1] - a[1]
            stairs(b, a, (0, 0, 0), 1.0, rise, run_)
            if pa >= 50.0:
                b.marker("ladder", (0, a[1] + 0.3, a[2]), height=round(rise, 2), incline=round(math.degrees(math.atan2(rise, run_)), 1))
            # A landing at the top of each flight and two struts down to the frame.
            pm.deck(b, -0.7, c[1], 0.7, c[1] + 0.9, c[2], rails=("left", "right"))
            pf = math.radians(pb)
            for s in (-0.55, 0.55):
                b.beam((s, c[1] + 0.45, c[2] - 0.1),
                       (s * 1.4, RT - (R + 0.2) * math.sin(pf), CZ + (R + 0.2) * math.cos(pf)), 0.12, 0.12,
                       tint=tower_tint)
    # The crown: a ring deck round the vent lantern at the apex.
    pz = CZ + RW * math.cos(math.radians(7.5))
    r_out = RW * math.sin(math.radians(7.5)) + 0.9
    pm.ring_deck(b, (0, 0), 2.9, r_out, pz, sides=24)
    gap = int(((THETA % 360.0) / 360.0) * 24)
    pm.ring_rail(b, (0, 0), r_out - 0.1, pz, sides=24, gaps=(gap,))
    for k in range(8):
        a = 2 * math.pi * (k + 0.5) / 8
        rr = r_out - 0.6
        zf = CZ + math.sqrt(R * R - rr * rr)
        b.beam((math.cos(a) * rr, math.sin(a) * rr, zf), (math.cos(a) * rr, math.sin(a) * rr, pz - 0.1), 0.18,
               0.18, tint=OFFWHITE)
    # The vent lantern: a louvred drum with a cap and the aviation lamp.
    z0 = CZ + R - 1.5
    b.cyl(2.5, pz + 3.4 - z0, (0, 0, (z0 + pz + 3.4) / 2), mat=PAINT, tint=OFFWHITE, sides=20)
    for k in range(16):
        a = 2 * math.pi * k / 16
        b.box((0.9, 0.12, 2.2), (math.cos(a) * 2.55, math.sin(a) * 2.55, pz + 2.1), (0, 0, math.degrees(a) + 90),
              tint=DARK, solid=False, vary=0)
    b.cyl(3.0, 0.35, (0, 0, pz + 3.55), mat=PAINT, tint=TEAL, sides=20)
    b.cyl(1.2, 0.8, (0, 0, pz + 4.1), mat=PAINT, tint=GREY, sides=12, solid=False)
    pm.aviation_lamp(b, (0, 0, pz + 4.5), blink=1.2, size=0.5)
    pm.stencil(b, (0, -2.53, pz + 0.55), (0, 0, 0), "07", height=0.7, tint=DARK)
    b.marker("landmark", (0, 0, pz + 5.0), kind_of="dome", visible_km=8)
    for k, (a, table) in enumerate(((150.0, "military-crate"), (230.0, "military-crate"), (60.0, "crate"))):
        rr = (2.9 + r_out) / 2
        b.marker("loot", (math.cos(math.radians(a)) * rr, math.sin(math.radians(a)) * rr, pz), yaw=a + 90,
                 table=table)
    b.piece("crate-stack", (math.cos(math.radians(110)) * 4.6, math.sin(math.radians(110)) * 4.6, pz), (0, 0, 20))


def interior(b):
    """Beds run wild, paths, the pond round the dead irrigation mast, trees, creepers hanging from the frame."""
    rng = b.rng
    path_t = srgb(0.70, 0.68, 0.64)
    # Cross paths and the ring path.
    for a in (0.0, 90.0):
        b.box((2 * RC - 2.0, 3.0, 0.12), (0, 0, 0.06), (0, 0, a), mat=CONCRETE, tint=path_t, solid=False, wear=0.7)
    n = 28
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n
        b.box((2 * 27.0 * math.sin(math.pi / n) + 0.1, 2.6, 0.12), (math.cos(a) * 27.0, math.sin(a) * 27.0, 0.06),
              (0, 0, math.degrees(a) + 90), mat=CONCRETE, tint=path_t, solid=False, wear=0.7)
    # The pond and the irrigation mast in it.
    b.lathe([(7.4, 0.0), (8.1, 0.0), (8.1, 0.65), (7.4, 0.65), (7.4, 0.0)], (0, 0, 0), mat=CONCRETE,
            tint=srgb(0.74, 0.73, 0.7), sides=32, close=False, solid=False)
    for k in range(16):
        a = 2 * math.pi * (k + 0.5) / 16
        b.collide_box((math.cos(a) * 7.75, math.sin(a) * 7.75, 0.32), (0.7, 3.3, 0.65), (0, 0, math.degrees(a)))
    b.cyl(7.45, 0.05, (0, 0, 0.32), mat=GLOSS, tint=pm.WATER, sides=32, solid=False, vary=0)
    for k in range(9):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(2.5, 6.5)
        pm.blob(b, (math.cos(a) * rr, math.sin(a) * rr, 0.36), rng.uniform(0.5, 0.9), 0.08, pm.MOSS, sides=6)
    from monkit.kit import lattice_tower
    lattice_tower(b, (0, 0, 0), 30.0, 2.4, 1.4, sections=6, member=0.12, tint=srgb(0.7, 0.72, 0.72))
    b.box((3.2, 3.2, 0.3), (0, 0, 30.1), tint=srgb(0.7, 0.72, 0.72))
    for k, a in enumerate((20.0, 110.0, 200.0, 290.0)):
        ar = math.radians(a)
        if k == 2:
            # One boom has fallen and leans from the mast to the beds.
            pts = [(math.cos(ar) * 1.2, math.sin(ar) * 1.2, 29.0), (math.cos(ar) * 15.0, math.sin(ar) * 15.0, 14.0),
                   (math.cos(ar) * 24.0, math.sin(ar) * 24.0, 0.5)]
        else:
            rr = 33.0
            pts = [(math.cos(ar) * 1.2, math.sin(ar) * 1.2, 29.6), (math.cos(ar) * rr, math.sin(ar) * rr, 36.0)]
            b.beam((math.cos(ar) * 1.2, math.sin(ar) * 1.2, 32.5), (math.cos(ar) * rr * 0.6, math.sin(ar) * rr * 0.6, 34.2),
                   0.06, 0.06, tint=GREY)
            for j in range(1, 6):
                t = j / 6
                p = (math.cos(ar) * (1.2 + (rr - 1.2) * t), math.sin(ar) * (1.2 + (rr - 1.2) * t), 29.6 + 6.4 * t)
                b.cyl(0.08, 0.3, (p[0], p[1], p[2] - 0.3), tint=DARK, sides=6, solid=False, vary=0)
        pm.pipe_run(b, pts, 0.16, tint=OFFWHITE)
    lamp(b, (0, -1.3, 12.0), (0, 0, 0), size=(0.5, 0.3, 0.3))
    pm.code_plate(b, (0, -1.25, 3.0), (0, 0, 0), 1.0, 0.4, base=TEAL, ink=OFFWHITE, text="M4")
    # Beds in the quadrants, inside and outside the ring path.
    for q in range(4):
        base = 90.0 * q + 45.0
        for k, (rr, da, w, d) in enumerate(((17.0, -15.0, 9.0, 3.6), (17.0, 15.0, 9.0, 3.6),
                                             (36.0, -24.0, 10.0, 4.0), (36.0, 0.0, 10.0, 4.0),
                                             (36.0, 24.0, 10.0, 4.0))):
            a = math.radians(base + da)
            pm.planter(b, (math.cos(a) * rr, math.sin(a) * rr, 0), (0, 0, base + da + 90), w, d, h=0.55,
                       plants=0.5 if (q + k) % 3 else 0.3, dry=(q + k) % 4 == 0)
    # Trees gone big, shrubs gone wild.
    for x, y, h in ((-20.0, 12.0, 17.0), (14.0, 22.0, 12.0), (24.0, -12.0, 14.0), (-12.0, -22.0, 10.0),
                    (-34.0, -15.0, 13.0), (31.0, 30.0, 11.0), (-26.0, 33.0, 20.0), (5.0, 38.0, 9.0)):
        pm.tree(b, (x, y, 0.0), h, spread=h * 0.32, trunk=0.18 + h * 0.012, lean=0.04)
    for k in range(20):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(9.0, 44.0)
        x, y = math.cos(a) * rr, math.sin(a) * rr
        if min(abs(x), abs(y)) < 2.2 or abs(rr - 27.0) < 1.8:
            continue
        pm.shrub(b, (x, y, 0.0), rng.uniform(0.8, 1.8))
    # Creepers hanging from the frame.
    for k in range(22):
        a = rng.uniform(0, 2 * math.pi)
        phi = math.radians(rng.uniform(25.0, 70.0))
        rr = (R - 0.6) * math.sin(phi)
        z = CZ + (R - 0.6) * math.cos(phi)
        pm.vines(b, (math.cos(a) * rr, math.sin(a) * rr, z), rng.uniform(3.0, 9.0), count=2)
    # Fallen panes and a fallen strut on the floor.
    for k in range(18):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(10.0, 44.0)
        b.box((rng.uniform(1.0, 2.6), rng.uniform(0.6, 1.6), 0.03), (math.cos(a) * rr, math.sin(a) * rr, 0.14),
              (rng.uniform(-6, 6), rng.uniform(-6, 6), rng.uniform(0, 360)), mat=GLASS, solid=False, vary=0)
    b.beam((-8.0, -36.0, 0.2), (-3.0, -31.0, 2.4), 0.34, 0.34, tint=OFFWHITE)
    rubble(b, (-5.0, -33.0, 0.1), 2.0, count=6, tint=srgb(0.6, 0.6, 0.58), max_size=0.7)
    # Loot inside: by the pond, in the beds, at the fallen boom.
    b.marker("loot", (5.5, -9.0, 0.12), table="crate")
    b.marker("loot", (-9.0, 4.5, 0.12), table="barrel")
    b.marker("loot", (22.0, -22.0, 0.12), table="barrel")
    b.marker("loot", (-30.0, 22.0, 0.12), table="crate")
    b.marker("loot", (math.cos(math.radians(200)) * 23.0, math.sin(math.radians(200)) * 23.0, 0.6), table="barrel")
    b.piece("sack-pallet", (8.0, -10.5, 0.12), (0, 0, 15))
    b.piece("drum-stack", (-10.5, 6.0, 0.12), (0, 0, -20))


def entrances(b):
    # The front airlock: a glazed vestibule over the wide gap.
    pm.building(b, (0, -RC - 3.4, 0), (0, 0, 0), 9.0, 6.2, floors=1, floor_h=3.8, mat=PANEL, tint=OFFWHITE,
                openings={"front": [(0, 2.6, 2.5, 0, 0, {"kind_of": "airlock", "state": "open"})],
                          "back": [(0, 2.6, 2.5, 0, 0, {"kind_of": "airlock", "state": "ajar"})],
                          "left": [(0.0, 3.4, 1.6, 1.0, 0)], "right": [(0.0, 3.4, 1.6, 1.0, 0)]},
                plinth=0.15, parapet=0.45, band=(ORANGE, 3.1, 0.38), roof_units=0, glass=0.4)
    pm.code_plate(b, (0, -RC - 6.68, 3.25), (0, 0, 0), 2.4, 0.55, base=TEAL, ink=OFFWHITE, text="BD-2")
    for side, txt in ((-1, "07"), (1, "07")):
        a = math.radians(-90.0 + side * 18.0)
        pm.stencil(b, (math.cos(a) * (RC + 0.42), math.sin(a) * (RC + 0.42), 1.55), (0, 0, math.degrees(a) + 90),
                   txt, height=1.8, tint=TEAL)
    b.piece("lamp-post", (-6.5, -RC - 8.0, 0), (0, 0, 180))
    b.piece("lamp-post", (6.5, -RC - 8.0, 0), (0, 0, 180))
    # The back door: a steel door in a concrete porch.
    b.box((5.6, 3.0, 0.3), (0, RC + 1.6, 3.2), mat=CONCRETE, tint=CONCRETE_T)
    for x in (-2.6, 2.6):
        b.box((0.4, 3.0, 3.2), (x, RC + 1.6, 1.6), mat=CONCRETE, tint=CONCRETE_T)
    pm.door_leaf(b, (-1.1, RC + 0.3, 0), 2.2, 2.4, 70.0, tint=srgb(0.45, 0.5, 0.48))
    b.marker("door", (0, RC, 0), 0.0, kind_of="service", width=2.2, height=2.4)
    # The pipe door on the west, where the pump house feeds the beds.
    b.marker("door", (-RC, 0, 0), 90.0, kind_of="service", width=3.2, height=2.6)


def services(b):
    rng = b.rng
    # Pump house (west), its front toward the dome.
    pm.building(b, (-62.0, -6.0, 0), (0, 0, 90), 15.0, 9.0, floors=1, floor_h=4.6, mat=CONCRETE, tint=CONCRETE_T,
                openings={"front": [(-3.0, 3.6, 3.4, 0, 0, {"kind_of": "shutter", "state": "gone"}),
                                    (4.5, 1.2, 2.3, 0, 0)],
                          "back": [(-4.0, 2.2, 1.2, 2.2, 0), (2.0, 2.2, 1.2, 2.2, 0)],
                          "left": [(1.5, 1.2, 2.3, 0, 0)], "right": [(0.0, 2.4, 1.2, 2.2, 0)]},
                band=(TEAL, 3.5, 0.5), roof_units=2)
    with b.frame((-62.0, -6.0, 0), (0, 0, 90)):
        for x in (-4.5, -1.0):
            b.cyl(0.9, 1.6, (x, 1.5, 1.1), tint=srgb(0.25, 0.42, 0.55), sides=16)
            b.box((1.8, 1.0, 0.3), (x, 1.5, 0.45), tint=DARK)
            b.box((0.6, 0.6, 0.8), (x + 1.2, 1.5, 0.7), tint=srgb(0.25, 0.42, 0.55), bevel=0.03)
        pm.pipe_run(b, [(-4.5, 1.5, 1.9), (-4.5, 1.5, 3.8), (5.0, 1.5, 3.8), (5.0, -4.8, 3.8)], 0.18, tint=TEAL)
        b.piece("sack-pallet", (4.0, 2.5, 0.3), (0, 0, 5))
        b.piece("sack-pallet", (5.4, 2.6, 0.3), (0, 0, -8))
        pm.stencil(b, (-3.0, -4.68, 4.3), (0, 0, 0), "P1", height=0.9, tint=DARK)
        lamp(b, (4.5, -4.75, 3.2), (0, 0, 0), size=(0.45, 0.25, 0.2))
        b.marker("loot", (-6.0, 3.0, 0.3), table="barrel")
        b.marker("loot", (2.5, 3.2, 0.3), table="crate")
        b.marker("guard", (2.0, -8.0, 0.0), 180.0, role="reclaimer-scavenger")
    # Nutrient tanks behind the pump house, piped to the dome's west door.
    for y, t in ((-22.0, OFFWHITE), (-14.0, srgb(0.82, 0.84, 0.8))):
        tank(b, (-72.0, y, 0), 3.2, 7.5, tint=t, band=TEAL, ladder_side=0.0 if y < -20 else None)
    pm.pipe_run(b, [(-57.2, -1.0, 3.8), (-53.0, -1.0, 3.8), (-53.0, -1.0, 1.4), (-RC - 0.5, -1.0, 1.4)], 0.24,
                tint=TEAL, supports=3.0)
    pm.pipe_run(b, [(-57.2, 0.8, 1.0), (-RC - 0.5, 0.8, 1.0)], 0.16, tint=OFFWHITE, supports=3.0)
    pm.pipe_run(b, [(-69.0, -18.0, 2.0), (-66.5, -18.0, 2.0), (-66.5, -10.5, 2.0)], 0.2, tint=TEAL, supports=2.5)
    # The staff block (front left): two floors, a stair inside, a hatch to the roof.
    sw, sd = 18.0, 9.0
    info = pm.building(b, (-34.0, -64.0, 0), (0, 0, 8), sw, sd, floors=2, floor_h=3.4, mat=PANEL,
                       tint=srgb(0.86, 0.86, 0.84),
                       openings={"front": [(-5.0, 1.3, 2.3, 0, 0), (-1.5, 1.6, 1.3, 1.0, 0), (2.5, 1.6, 1.3, 1.0, 0),
                                           (6.0, 1.6, 1.3, 1.0, 0), (-5.0, 1.6, 1.3, 1.0, 1),
                                           (-1.5, 1.6, 1.3, 1.0, 1), (2.5, 1.6, 1.3, 1.0, 1), (6.0, 1.6, 1.3, 1.0, 1)],
                                 "back": [(-6.0, 1.6, 1.3, 1.0, 0), (0.0, 1.6, 1.3, 1.0, 0), (6.0, 1.6, 1.3, 1.0, 0),
                                          (-6.0, 1.6, 1.3, 1.0, 1), (0.0, 1.6, 1.3, 1.0, 1), (6.0, 1.6, 1.3, 1.0, 1)],
                                 "right": [(0.0, 1.2, 2.3, 0, 0)], "left": [(1.0, 1.6, 1.3, 1.0, 1)]},
                       holes=[(5.4, -3.9, 6.8, 1.0, 1), (-7.6, 2.6, -6.4, 3.8, 2)], band=(ORANGE, 3.2, 0.3),
                       roof_units=2, glass=0.45)
    with b.frame((-34.0, -64.0, 0), (0, 0, 8)):
        f0, f1 = info["floor"]
        stairs(b, (6.1, -3.8, f0), (0, 0, 0), 1.2, f1 - f0, 4.7)
        pm.roof_hatch(b, (-7.0, 3.2), (1.0, 1.0), f1, info["roof"])
        pm.wall(b, (-1.5, -sd / 2 + 0.15, f0), (-1.5, sd / 2 - 0.15, f0), 3.2, 0.12,
                [(3.0, 1.0, 2.2, 0, None)], mat=PANEL, tint=srgb(0.8, 0.8, 0.78))
        b.piece("crate-stack", (-7.5, -2.8, f0), (0, 0, 10))
        b.marker("loot", (-6.0, 2.5, f0), table="crate")
        b.marker("loot", (3.0, 2.8, f1), table="crate")
        b.marker("loot", (-4.0, -3.0, f1), table="barrel")
        pm.code_plate(b, (-5.0, -sd / 2 - 0.2, 2.9), (0, 0, 0), 1.6, 0.42, base=OFFWHITE, ink=DARK, text="S2")
        pm.mark(b, (2.0, -sd / 2 - 0.17, 1.2), (0, 0, 0), 1.4, "slash", pm.GRAFFITI[0])
    # Hab cans and the polytunnels (front right), a wrecked garden.
    hab_module(b, (-14.0, -76.0, 0), (0, 0, 4), length=9.0, tint=OFFWHITE, band=ORANGE)
    hab_module(b, (17.0, -69.0, 0), (0, 0, -6), length=8.0, tint=srgb(0.84, 0.85, 0.82), band=TEAL)
    b.marker("loot", (17.0, -72.0, 0.0), table="barrel")
    for k, x in enumerate((58.0, 65.0, 72.0)):
        pm.polytunnel(b, (x, 4.0 + k * 1.5, 0), (0, 0, 2 - k * 2), 5.2, 18.0 - k * 2.0, torn=0.3 + 0.15 * k)
        for j in range(3):
            pm.shrub(b, (x + rng.uniform(-1.2, 1.2), 4.0 + rng.uniform(-6, 6), 0.0), 0.9)
    # Elevated water tank (back left) and the broken turbine and the solar field (back right).
    tank(b, (-50.0, 46.0, 0), 3.6, 6.0, tint=srgb(0.82, 0.84, 0.82), band=ORANGE, ladder_side=-90.0, legs=9.0)
    pm.stencil(b, (-50.0, 42.33, 12.6), (0, 0, 0), "W3", height=1.6, tint=DARK)
    wind_turbine(b, (52.0, 56.0, 0), height=32.0, blade=13.0, yaw=200.0, broken=True)
    solar_array(b, (-2.0, 60.0, 0), (0, 0, 0), rows=3, cols=10, broken=0.25)
    # The colony's sign by the road: a painted board on two posts.
    with b.frame((-8.5, -78.0, 0), (0, 0, -12)):
        for x in (-1.6, 1.6):
            b.box((0.16, 0.16, 3.4), (x, 0, 1.7), mat=MATTE, tint=pm.WOOD, vary=0.1)
        b.box((3.8, 0.08, 1.6), (0, -0.1, 2.5), mat=PAINT, tint=srgb(0.86, 0.84, 0.76), wear=0.6)
        b.box((3.8, 0.09, 0.3), (0, -0.1, 1.85), mat=PAINT, tint=srgb(0.30, 0.48, 0.30), wear=0.6, vary=0)
        for k in range(9):
            a0, a1 = math.pi * k / 9, math.pi * (k + 1) / 9
            p0 = (-0.9 + math.cos(a0) * 0.55, 2.25 + math.sin(a0) * 0.55)
            p1 = (-0.9 + math.cos(a1) * 0.55, 2.25 + math.sin(a1) * 0.55)
            ln = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
            ang = math.degrees(math.atan2(p1[1] - p0[1], p1[0] - p0[0]))
            b.box((ln + 0.02, 0.02, 0.09), ((p0[0] + p1[0]) / 2, -0.15, (p0[1] + p1[1]) / 2), (0, -ang, 0),
                  mat=MATTE, tint=srgb(0.30, 0.48, 0.30), solid=False, vary=0)
        b.box((1.2, 0.02, 0.07), (-0.9, -0.15, 2.25), mat=MATTE, tint=srgb(0.30, 0.48, 0.30), solid=False, vary=0)
        pm.stencil(b, (0.75, -0.16, 2.6), (0, 0, 0), "07", height=0.8, tint=srgb(0.72, 0.34, 0.12), mat=MATTE)
    # The road in, lamps, debris, a guard.
    road(b, [(0.0, -84.0, 0.0), (0.0, -RC - 7.0, 0.0)], width=6.0)
    b.piece("lamp-post", (-45.0, -40.0, 0), (0, 0, 135))
    b.piece("lamp-post", (40.0, 52.0, 0), (0, 0, -45))
    b.piece("jersey-barrier", (4.5, -80.0, 0), (0, 0, 80))
    b.piece("drum-stack", (-24.0, -60.0, 0), (0, 0, 30))
    b.marker("guard", (-3.0, -60.0, 0.0), 180.0, role="reclaimer-scavenger")
    b.marker("loot", (-22.5, -58.5, 0.0), table="fuel-barrel")
    for k in range(10):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(RC + 1.5, RC + 7.0)
        b.box((rng.uniform(1.0, 2.4), rng.uniform(0.6, 1.4), 0.03), (math.cos(a) * rr, math.sin(a) * rr, 0.03),
              (rng.uniform(-5, 5), rng.uniform(-5, 5), rng.uniform(0, 360)), mat=GLASS, solid=False, vary=0)
    rubble(b, (30.0, -40.0, 0.0), 2.5, count=7, tint=srgb(0.7, 0.7, 0.68), max_size=0.8)
    rubble(b, (-38.0, 28.0, 0.0), 2.5, count=6, tint=srgb(0.7, 0.7, 0.68), max_size=0.8)
    # A ring path round the dome, and growth that escaped it.
    n = 40
    for k in range(n):
        if k in (29, 30, 10):
            continue
        a = 2 * math.pi * (k + 0.5) / n
        rr = RC + 5.0
        b.box((2 * rr * math.sin(math.pi / n) - 0.15, 2.2, 0.1), (math.cos(a) * rr, math.sin(a) * rr, 0.05),
              (0, 0, math.degrees(a) + 90), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62), solid=False, wear=0.7)
    for k in range(18):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(RC + 1.0, RC + 4.0) if k < 11 else rng.uniform(RC + 8.0, 78.0)
        pm.shrub(b, (math.cos(a) * rr, math.sin(a) * rr, 0.0), rng.uniform(0.7, 1.6))
    for k, (x, y, h) in enumerate(((-66.0, 30.0, 9.0), (66.0, -38.0, 8.0), (-70.0, -40.0, 7.0), (24.0, 72.0, 10.0))):
        pm.tree(b, (x, y, 0.0), h, spread=h * 0.33, trunk=0.2 + h * 0.012, lean=0.05)
    # Containers by the pump house, and the colony's farm fence round it all (broken in places).
    b.piece("container-20", (-56.0, -26.0, 0), (0, 0, 8), tint=srgb(0.35, 0.47, 0.55))
    b.piece("container-20", (-55.0, -33.0, 0), (0, 0, -4), tint=srgb(0.72, 0.42, 0.2))
    b.piece("container-20", (-55.6, -29.5, 2.59), (0, 0, 2), tint=srgb(0.82, 0.82, 0.78))
    b.marker("loot", (-52.0, -30.0, 0.0), table="crate")
    pts = []
    for k in range(57):
        a = math.radians(-86.0 + 352.0 * k / 56)
        pts.append((math.cos(a) * 80.0, math.sin(a) * 80.0, 0.0))
    pm.wire_fence(b, pts, post=5.0, broken=0.2)


def build(b):
    b.set_pad(("circle", 84.0), ground="gravel", blend=16.0, clear=8.0)
    ring_wall(b)
    _, _, _, fallen = pm.geodesic_dome(b, (0, 0, CZ), R, CUT, freq=9, strut=0.34, tint=OFFWHITE, keep=0.48,
                                       solid_below=8.0, panel_below=8.5, holes=[(-72.0, 50.0, 15.0), (150.0, 32.0, 8.0), (-128.0, 97.0, 7.5)])
    # What fell through the holes: struts and panes on the beds below.
    for g in fallen:
        for k in range(7):
            a = b.rng.uniform(0, 2 * math.pi)
            rr = b.rng.uniform(0.0, 6.0)
            p = (g.x + math.cos(a) * rr, g.y + math.sin(a) * rr, 0.25)
            ln = b.rng.uniform(3.0, 5.5)
            yaw = b.rng.uniform(0, 360)
            b.beam(p, (p[0] + math.cos(math.radians(yaw)) * ln, p[1] + math.sin(math.radians(yaw)) * ln,
                       b.rng.uniform(0.2, 1.6)), 0.34, 0.34, tint=OFFWHITE, solid=True)
        for k in range(6):
            a = b.rng.uniform(0, 2 * math.pi)
            rr = b.rng.uniform(0.0, 7.0)
            b.box((b.rng.uniform(1.0, 2.5), b.rng.uniform(0.6, 1.5), 0.03),
                  (g.x + math.cos(a) * rr, g.y + math.sin(a) * rr, 0.3),
                  (b.rng.uniform(-10, 10), b.rng.uniform(-10, 10), b.rng.uniform(0, 360)), mat=GLASS, solid=False,
                  vary=0)
    climb(b)
    interior(b)
    entrances(b)
    services(b)
    b.zone("radiation", (0, 0, 18.0), radius=RC - 1.0, level=2, source="spores")
    b.zone("radiation", (0, 0, CZ + RW * math.cos(math.radians(7.5)) + 1.0), radius=9.0, level=2, source="vent")
    b.zone("vegetation", (0, 0, 0), radius=RC - 1.0, density=0.8)
    b.zone("no-build", (0, 0, 0), radius=115.0)
    b.meta.update({"tier": "medium", "landmark": False, "family": "colonist", "spawn": "plains-near-towns",
                   "contamination": 2})


SUN = (30.0, 215.0)
PEOPLE = [((2.0, -58.0, 0), 10.0), ((math.cos(math.radians(150)) * 4.2, math.sin(math.radians(150)) * 4.2,
                                     CZ + RW * math.cos(math.radians(7.5))), 200.0)]
VIEWS = [
    {"name": "overview", "target": (0, -4, 20), "distance": 205, "yaw": 34, "pitch": 17, "lens": 32},
    {"name": "airlock", "target": (0, -47, 10), "distance": 36, "yaw": 8, "pitch": -12, "lens": 24},
    {"name": "climb", "target": (26, -22, 50), "distance": 62, "yaw": 72, "pitch": 6, "lens": 30},
]
