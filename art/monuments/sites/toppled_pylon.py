"""Toppled Pylon (MONUMENTS.md §5.1 no. 2, roadside tier): a lattice transmission tower of an old power line, fallen
along the line and across the road, folded where it struck the crown. Two legs tore out of their footings and two
buckled; the cross arms dug into the verge, conductors lie snaking through the grass for the length of the site, cut
coils of wire wait to be carried off. A line crew got as far as closing the road: their service trailer stands at the
tower with its doors open, cable drums beside it, barriers and cones across the lanes.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, OFFWHITE, ORANGE, TEAL, insulator, rubble, srgb)
from monkit.structures import road
from monkit.surfaces import ASPHALT, CONCRETE

KEY = "toppled-pylon"
TITLE = "Toppled Pylon"
SEED = 202
BUILDER = {"chunk": 32.0, "max_edge": 4.0}

H = 26.0            # tower height
BASE, TOP = 5.2, 1.7
SECTIONS = 7
FOOT = Vector((-21.0, 2.2, 0.0))    # centre of the four footings
ROAD_X = -6.5       # the road runs along Y here
ROLL = 12.0
LINE = srgb(0.56, 0.57, 0.58)   # aluminium conductor


def width_at(z):
    return BASE + (TOP - BASE) * z / H


def head(b, low_side_bent=True):
    """Cross arms and earth peak in the tower's own frame (arms along local X). Returns the arm tips by side."""
    tips = []
    for z, span in ((H - 4.0, 6.2), (H - 0.5, 4.4)):
        hw = width_at(z) / 2
        for s in (-1, 1):
            bend = 24.0 if (s < 0 and low_side_bent) else 0.0
            with b.frame((s * hw, 0, z), (0, 0, 0)):
                with b.frame((0, 0, 0), (0, -s * 4.0, s * bend)):
                    reach = span - hw
                    for y in (-0.45, 0.45):
                        P.angle(b, (0, y, 0), (s * reach, y * 0.25, 0), 0.12, 0.012)
                        P.angle(b, (0, y, 1.5), (s * reach, y * 0.25, 0.1), 0.1, 0.01)
                    for k in range(1, 5):
                        x = s * reach * k / 5
                        P.angle(b, (x, -0.4 + 0.3 * k / 5, 0), (x, 0.4 - 0.3 * k / 5, 0), 0.07, 0.008)
                        P.angle(b, (x, 0, 0.05), (x + s * reach / 5, 0, 1.5 - 1.4 * (k + 1) / 5), 0.07, 0.008)
                    tips.append((s, z, b.world((s * reach, 0, -0.05))))
    peak = width_at(H) / 2
    for i in range(4):
        sx = -1 if i in (0, 3) else 1
        sy = -1 if i in (0, 1) else 1
        P.angle(b, (sx * peak, sy * peak, H), (0, 0, H + 1.8), 0.08, 0.009)
    tips.append((0, H + 1.8, b.world((0, 0, H + 1.8))))
    return tips


def build(b):
    # The pad: the road's strip and the torn ground along the fallen tower; the conductors run on into the grass.
    r0, r1 = ROAD_X - 5.0, ROAD_X + 6.5
    b.set_pad(("poly", [(r0, -14.0), (r1, -14.0), (r1, -5.5), (13.0, -5.5), (13.0, 11.0), (r1, 11.0), (r1, 13.0),
                        (r0, 13.0), (r0, 11.0), (-26.0, 11.0), (-26.0, -5.5), (r0, -5.5)]),
              ground="dirt", blend=10.0, clear=2.0)

    # The road: two lanes along Y, gravel shoulders and marker posts, broken where the tower came down on it.
    road(b, [(ROAD_X, -14.0, 0), (ROAD_X, 13.0, 0)], width=7.0)
    P.verge(b, ROAD_X - 3.5, -14.0, 13.0, -1)
    P.verge(b, ROAD_X + 3.5, -14.0, 13.0, 1)
    for y, knocked in ((-13.0, False), (-4.5, True), (7.5, False)):
        P.delineator(b, (ROAD_X - 4.3, y, 0.02), 0.0, knocked)
    for y, knocked in ((-11.5, False), (9.0, False), (12.5, True)):
        P.delineator(b, (ROAD_X + 4.3, y, 0.02), 180.0, knocked)
    rubble(b, (ROAD_X + 0.8, 2.4, 0.06), 2.6, count=9, tint=(1, 1, 1), mat=ASPHALT, max_size=0.9)
    for x, y, r in ((ROAD_X + 1.5, 0.2, 0.9), (ROAD_X - 1.0, 4.8, 0.7)):
        P.blob(b, (x, y, 0.1), r, srgb(0.1, 0.1, 0.1), points=10, rough=0.4)

    # Footings: four concrete chimneys, the leg stubs torn (tension side) or buckled flat (compression side).
    hw0 = BASE / 2
    stubs = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = FOOT + Vector((sx * hw0, sy * hw0, 0))
            b.box((1.3, 1.3, 0.9), (c.x, c.y, 0.2), mat=CONCRETE, tint=CONCRETE_T, bevel=0.03)
            b.box((1.45, 1.45, 0.1), (c.x, c.y, 0.02), mat=CONCRETE, tint=srgb(0.6, 0.58, 0.55), solid=False)
            if sx < 0:      # torn out: short stubs bent toward the fall, ragged
                top = c + Vector((0.5, 0.05 * sy, 1.0))
                P.angle(b, c + Vector((0, 0, 0.6)), top, 0.16, 0.016)
                P.angle(b, top, top + Vector((0.35, 0.1 * sy, 0.25)), 0.16, 0.016)
            else:           # buckled: the leg folded over and lying
                top = c + Vector((1.1, 0.0, 0.75))
                P.angle(b, c + Vector((0, 0, 0.6)), top, 0.16, 0.016)
            stubs.append(top)
            for k in range(4):
                a = math.radians(45 + 90 * k)
                b.cyl(0.03, 0.12, (c.x + 0.25 * math.cos(a), c.y + 0.25 * math.sin(a), 0.71), mat="M_Mon_Rust",
                      tint=(1, 1, 1), sides=6, solid=False, vary=0)
    rubble(b, (FOOT.x - 2.6, FOOT.y + 2.6, 0.05), 1.2, count=4, max_size=0.5)

    # The tower, lying along +X (the line's direction) with its arms across, rolled onto a corner; folded at the
    # road, the upper part bent off line and dug in.
    base_c = Vector((FOOT.x + hw0 + 0.7, FOOT.y, 3.0))
    tips = []
    with b.frame(tuple(base_c), (0, 0, 90 + 4.0)):
        with b.frame((0, 0, 0), (93.5, 0, 0)):
            with b.frame((0, 0, 0), (0, 0, ROLL)):
                lower = 3
                z_k = H * lower / SECTIONS
                P.lattice(b, 0.0, z_k, BASE, width_at(z_k), lower, member=0.13, skip=0.12, solid=True)
                # The corners of the base, for the bent legs back to the footings.
                base_corners = [b.world(Vector((sx * hw0, sy * hw0, 0.0)))
                                for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
                with b.frame((0, 0, z_k), (2.5, 9.0, 9.0)):
                    up = SECTIONS - lower
                    P.lattice(b, 0.0, H - z_k, width_at(z_k), TOP, up, member=0.11, skip=0.2, solid=True)
                    with b.frame((0, 0, -z_k), (0, 0, 0)):
                        tips = head(b)
                # Twisted members at the fold.
                for i in range(5):
                    a = b.rng.uniform(0, math.tau)
                    hw = width_at(z_k) / 2
                    p0 = Vector((math.cos(a) * hw, math.sin(a) * hw, z_k - 1.2))
                    p1 = Vector((math.cos(a + 1.3) * hw * 0.8, math.sin(a + 1.3) * hw * 0.8, z_k + 1.4))
                    P.angle(b, p0, p1, 0.1, 0.01)

    # Buckled legs between the footing stubs and the tower's base.
    # Stubs 2 and 3 (the +X footings, front and back) meet the tower's two lowest base corners (local 0 and 1).
    for k, top in enumerate(stubs):
        if k >= 2:          # the compression side stayed attached, bent
            corner = base_corners[k - 2]
            mid = (top + corner) / 2 + Vector((0.3, 0, 0.5))
            P.angle(b, top, mid, 0.16, 0.016)
            P.angle(b, mid, corner, 0.16, 0.016)

    # Insulator strings hanging from the arm tips; conductors from there into the grass, both spans.
    ends = []
    for s, z, tip in tips:
        if s == 0:
            ends.append((s, z, tip, tip))
            continue
        length = 1.6
        bottom = Vector((tip.x, tip.y, max(0.25, tip.z - length)))
        if tip.z > 0.9:
            insulator(b, tuple(bottom), tip.z - bottom.z, tint=srgb(0.82, 0.82, 0.8))
        ends.append((s, z, tip, bottom))
    for s, z, tip, end in ends:
        y0 = end.y
        # The span back toward the last standing pylon: tensioned, nearly straight along the line.
        back = [(end.x, end.y, end.z), (end.x - 2.5, end.y - 0.2 * s, 0.05), (FOOT.x - 4.0, y0 + 0.6 * s, 0),
                (-33.0, y0 + 1.0 * s, 0)]
        P.ground_cable(b, back, 0.032 if s else 0.02, LINE, wobble=0.35, step=1.2)
    # The span ahead: slack, looping through the grass, one conductor snapped and coiled near the road.
    for k, (s, z, tip, end) in enumerate(ends):
        if s == 0:
            continue
        y0 = end.y
        pts = [(end.x, end.y, end.z), (end.x + 3.0, y0 + 1.5 * s, 0), (end.x + 8.0, y0 - 1.0 * s, 0),
               (end.x + 13.0, y0 + 2.2 * s, 0), (33.0, y0 + 0.5 * s, 0)]
        if k == 1:
            pts = pts[:3]
            P.coil(b, (pts[-1][0] + 0.4, pts[-1][1], 0), 0.9, 2.5, 0.028, LINE)
            P.scorch(b, (pts[-1][0], pts[-1][1], 0), 1.4)
        P.ground_cable(b, pts, 0.032, LINE, wobble=1.1)
    peak = [e for e in ends if e[0] == 0][0][2]
    P.ground_cable(b, [(peak.x, peak.y, peak.z), (peak.x + 4.0, peak.y + 0.8, 0), (33.0, peak.y + 3.0, 0)], 0.016,
                   srgb(0.5, 0.5, 0.5), wobble=0.8)
    P.ground_cable(b, [(peak.x, peak.y, peak.z), (peak.x - 3.0, peak.y - 0.5, 0.2), (-33.0, peak.y - 2.0, 0)],
                   0.016, srgb(0.5, 0.5, 0.5), wobble=0.4)
    # Aviation marker balls on the earth wire where it crossed the road.
    for x, dy in ((ROAD_X - 1.5, -0.3), (ROAD_X - 14.0, -1.4)):
        P.ball(b, (x, peak.y + dy, 0.33), 0.32, ORANGE, solid=True)
        b.box((0.08, 0.5, 0.06), (x, peak.y + dy, 0.66), mat="M_Mon_Paint", tint=GREY, solid=False)
    P.scorch(b, (ends[2][3].x + 3.5, ends[2][3].y + 1.0, 0), 1.0)
    P.scorch(b, (-1.0, -3.8, 0), 0.8)

    # The line crew: their service trailer on the road at the tower, doors open toward the barriers.
    floor, inside = P.service_trailer(b, (ROAD_X + 1.6, -6.4, 0), (0, 0, 4.0), code="LN-07")
    b.marker("loot", tuple(inside), table="crate")
    b.piece("cable-reel", (ROAD_X + 4.6, -8.0, 0), (0, 0, 75))
    b.marker("loot", (ROAD_X + 4.6, -8.0, 0.1), table="cable-reel")
    # A second drum tipped on its side in the verge, new cable run out toward the footings.
    b.piece("cable-reel", (-14.5, -6.5, 0.0), (90, 0, 20))
    b.marker("loot", (-14.5, -6.5, 0.2), table="cable-reel")
    P.ground_cable(b, [(-14.0, -5.6, 0.15), (-16.5, -3.0, 0), (-19.0, -0.6, 0), (FOOT.x - 0.5, FOOT.y - 2.6, 0.75)],
                   0.024, BLACK, wobble=0.5)
    # Coils of copper cut from the line, ready to carry off.
    for at in ((3.5, -6.8, 0), (10.5, 8.0, 0), (-12.0, 7.6, 0)):
        P.coil(b, at, 0.55, 4.0, 0.03, P.COPPER, spread=0.12)
        b.marker("loot", (at[0], at[1], 0.1), table="copper-wire")
    b.marker("loot", (ROAD_X + 4.2, -11.0, 0.05), table="barrel")
    b.marker("loot", (-16.0, 5.5, 0.05), table="barrel")

    # Barriers and cones closing both lanes, a sign on its tripod, another blown over.
    b.piece("jersey-barrier", (ROAD_X - 1.7, -11.6, 0), (0, 0, 2.0))
    b.piece("jersey-barrier", (ROAD_X + 1.8, -12.2, 0), (0, 0, -8.0))
    b.piece("jersey-barrier", (ROAD_X - 1.6, 9.2, 0), (0, 0, 4.0))
    b.piece("jersey-barrier", (ROAD_X + 1.9, 9.8, 0), (0, 0, 21.0))
    for k in range(6):
        t = k / 5
        P.cone(b, (ROAD_X - 3.0 + 3.6 * t, -13.6 + 2.2 * t, 0.1), knocked=k in (2, 5), yaw=k * 37.0)
    P.tripod_sign(b, (ROAD_X - 4.6, -12.6, 0.0), yaw=-8.0, glyph="!")
    P.tripod_sign(b, (ROAD_X + 4.4, 11.0, 0.0), yaw=170.0, glyph="~", fallen=True)
    # The crew's generator and a work light aimed at the fold, a cable between them.
    P.generator(b, (ROAD_X - 2.2, -4.6, 0.1), (0, 0, 12.0))
    P.work_light(b, (ROAD_X - 2.6, -2.2, 0.1), yaw=165.0)
    P.ground_cable(b, [(ROAD_X - 1.9, -4.4, 0.4), (ROAD_X - 2.3, -3.4, 0.11), (ROAD_X - 2.6, -2.3, 0.12)], 0.012,
                   BLACK, wobble=0.15, step=0.4)
    # Bits of the tower and the crew's kit lying about.
    P.debris(b, (2.0, 4.0, 0), 4.0, count=6, size=1.2, tint=P.GALV)
    P.debris(b, (-9.5, -2.0, 0), 2.0, count=3, size=0.8, tint=P.GALV)

    b.zone("no-build", (0, 0, 0), size=(80.0, 40.0, 30.0))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "line"})


SUN = (30.0, 220.0)
PEOPLE = [((ROAD_X - 1.2, -9.5, 0.1), 15.0)]
VIEWS = [
    {"name": "site", "target": (-4, 1, 1.5), "distance": 46, "yaw": -28, "pitch": 24, "lens": 30},
    {"name": "road", "target": (ROAD_X + 0.5, -1.0, 2.2), "distance": 19, "yaw": -14, "pitch": 1.5, "lens": 26,
     "people": [((ROAD_X - 1.6, -8.2, 0.1), 200.0)]},
    {"name": "head", "target": (1, 3, 2.2), "distance": 25, "yaw": 102, "pitch": 5, "lens": 28},
]
