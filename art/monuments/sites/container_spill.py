"""Container Spill (MONUMENTS.md §5.1 no. 5, roadside tier): a Seedward freight hauler that lost the road. Skid marks run
along the lane to where it broke through the crash barrier; the tractor lies on its side in the verge with its trailer
jackknifed behind it, one container still locked on. The rest of the load is strewn along the verge: a container upside
down in the grass, one burst open with its cargo spilled, one lying across the far lane. Crates and barrels in and
around them.
"""
import math

from mathutils import Matrix, Vector

from monkit import parts_roadside as P
from monkit.kit import (BLUE, OFFWHITE, ORANGE, TEAL, srgb)
from monkit.structures import road
from monkit.surfaces import GLASS, MATTE, PAINT

KEY = "container-spill"
TITLE = "Container Spill"
SEED = 505
BUILDER = {"chunk": 32.0, "max_edge": 3.0}

ROAD_Y = 3.2
TRUCK = (-13.6, -5.0)
TRUCK_YAW = -70.0
TRAILER_YAW = -97.0
LIFT = 1.27         # height of a rolled vehicle's frame origin (half its width)


def rolled(yaw):
    """The rotation part of a vehicle lying on its right side, turned `yaw` degrees."""
    return (Matrix.Rotation(math.radians(yaw), 4, "Z") @ Matrix.Translation((0, 0, LIFT)) @
            Matrix.Rotation(math.radians(90.0), 4, "Y"))


def build(b):
    b.set_pad(("rect", 48.0, 20.0), ground="gravel", blend=10.0, clear=3.0)

    # The road, its shoulders, the crash barrier torn open where the hauler went through, the skid marks.
    road(b, [(-24.0, ROAD_Y, 0), (24.0, ROAD_Y, 0)], width=7.0)
    with b.frame((0, 0, 0), (0, 0, -90.0)):        # the shoulders, along X (verge() runs along its frame's Y)
        P.verge(b, -(ROAD_Y - 3.5), -24.0, 24.0, 1)
        P.verge(b, -(ROAD_Y + 3.5), -24.0, 24.0, -1)
    P.guard_rail(b, [(-23.0, ROAD_Y - 4.2), (23.0, ROAD_Y - 4.2)], gap=(12.5, 17.5), bent=(9.0, 21.5))
    for off in (-0.85, 0.85):
        P.ribbon(b, [(23.0, ROAD_Y - 0.9 + off), (12.0, ROAD_Y - 1.3 + off), (2.0, ROAD_Y - 2.4 + off * 0.9),
                     (-4.5, ROAD_Y - 4.2 + off * 0.8), (-8.0, ROAD_Y - 6.0 + off * 0.7)], 0.32, 0.105,
                 srgb(0.06, 0.06, 0.06))
    for x in (-19.0, 6.0, 15.0):
        P.delineator(b, (x, ROAD_Y - 4.7, 0.02), 0.0, knocked=x == 6.0)

    # The tractor on its side, slid off the road; a scraped furrow behind it.
    with b.frame((TRUCK[0], TRUCK[1], 0), (0, 0, TRUCK_YAW)):
        with b.frame((0, 0, LIFT), (0, 90.0, 0)):
            fifth = P.hauler(b, (0, 0, 0), (0, 0, 0), code="HX-214", broken=0.2, mirrors=False)
    P.berm(b, [(-6.5, -2.4), (-8.8, -3.4), (-11.0, -4.4)], width=2.2, height=0.16, tint=P.EARTH)
    P.berm(b, [(-5.8, -4.6), (-8.4, -6.0), (-10.6, -7.2)], width=2.0, height=0.14, tint=P.EARTH)
    P.blob(b, (-9.0, -4.6, 0), 2.6, P.EARTH_DARK, points=16, rough=0.3, squash=0.45, yaw=30.0, thick=0.01)
    P.blob(b, (-12.4, -3.6, 0.012), 1.7, srgb(0.03, 0.03, 0.03), points=14, rough=0.35, squash=0.6, yaw=20.0,
           mat="M_Mon_Gloss")
    for k in range(12):
        a = b.rng.random() * math.tau
        r = 1.0 + 3.0 * b.rng.random()
        b.box((0.06 + 0.1 * b.rng.random(), 0.05 + 0.08 * b.rng.random(), 0.006),
              (TRUCK[0] - 2.5 + math.cos(a) * r, TRUCK[1] - 1.0 + math.sin(a) * r, 0.03),
              (0, 0, b.rng.uniform(0, 360)), mat=GLASS, solid=False, vary=0)
    P.debris(b, (TRUCK[0] + 1.0, TRUCK[1] + 2.0, 0), 3.0, count=5, size=0.6, tint=OFFWHITE, mat="M_Mon_Panel")

    # The trailer, jackknifed and on its side, the first container still locked on.
    hl = 6.2
    kp_local = Vector((0, -hl + 0.9, 1.1))
    rt = rolled(TRAILER_YAW)
    kp_offset = (rt @ kp_local.to_4d()).to_3d()
    origin = Vector((fifth.x - kp_offset.x + 0.6, fifth.y - kp_offset.y - 0.4, 0))
    with b.frame(tuple(origin), (0, 0, TRAILER_YAW)):
        with b.frame((0, 0, LIFT), (0, 90.0, 0)):
            deck = P.skel_trailer(b, (0, 0, 0), (0, 0, 0), length=2 * hl, legs_down=False)
            b.piece("container-20", (0, -hl + 3.25, deck), (0, 0, 90.0), tint=TEAL)
    b.marker("loot", (origin.x - 1.0, origin.y + 2.6, 0.1), table="crate")

    # The load along the verge: one upside down, one burst open with its cargo out, one across the far lane.
    b.piece("container-20", (0.4, -6.6, P.C20[2]), (180.0, 0, 34.0), tint=ORANGE)
    b.marker("loot", (3.6, -3.4, 0.1), table="crate")
    inside = P.container_open(b, (9.6, -5.0, 0.04), (0, 3.0, -24.0), tint=P.RUST_RED, doors=(104.0, 168.0))
    b.marker("loot", tuple(inside), table="crate")
    b.piece("container-20", (17.2, ROAD_Y - 0.2, 0), (0, 0, 58.0), tint=P.CONTAINER_BLUE)
    b.marker("loot", (19.0, ROAD_Y - 3.0, 0.1), table="barrel")
    # Spilled cargo: pallets of cartons, a tipped pallet, drums, loose cartons.
    b.piece("pallet-boxes", (8.4, -7.7, 0), (0, 0, 14.0))
    b.piece("pallet-boxes", (11.4, -8.3, 0.5), (0, 88.0, 40.0))
    b.piece("pallet-boxes", (13.6, -6.0, 0), (0, 0, -22.0))
    b.piece("drum-stack", (14.2, -2.6, 0), (0, 0, 31.0))
    for x, y, yaw, tint in ((10.6, -9.4, 20.0, BLUE), (12.9, -9.8, 75.0, ORANGE),
                            (15.6, -8.2, -30.0, srgb(0.7, 0.15, 0.11))):
        with b.frame((x, y, 0.29), (90.0, 0, yaw)):
            b.cyl(0.29, 0.88, (0, 0, 0), mat=PAINT, tint=tint, sides=16, solid=True)
            for z in (-0.15, 0.15):
                b.cyl(0.3, 0.03, (0, 0, z), mat=PAINT, tint=tint, sides=16, solid=False, vary=0)
        b.marker("loot", (x, y + 0.9, 0.05), table="barrel")
    for k in range(9):
        a = b.rng.uniform(-1.2, 0.6)
        r = b.rng.uniform(2.0, 6.0)
        b.box((0.56, 0.46, 0.33), (10.5 + math.cos(a) * r, -6.5 + math.sin(a) * r, 0.16 if k % 3 else 0.2),
              (b.rng.uniform(-8, 8), b.rng.uniform(-8, 8) + (90 if k % 4 == 0 else 0), b.rng.uniform(0, 360)),
              mat=MATTE, tint=srgb(0.6, 0.48, 0.33), solid=False, vary=0.06)
    P.debris(b, (6.0, -2.4, 0), 3.0, count=5, size=0.9, tint=P.RUST_RED, mat="M_Mon_Corrugated")

    b.zone("no-build", (0, 0, 0), size=(60.0, 34.0, 24.0))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "road"})


SUN = (28.0, 210.0)
PEOPLE = [((3.5, -9.5, 0.0), 20.0)]
VIEWS = [
    {"name": "spill", "target": (-2.0, -3.0, 1.0), "distance": 42, "yaw": -30, "pitch": 26, "lens": 30},
    {"name": "cab", "target": (-12.0, -4.6, 1.4), "distance": 15, "yaw": -146, "pitch": 7, "lens": 26},
    {"name": "road", "target": (2.0, -2.5, 1.2), "distance": 25, "yaw": -107, "pitch": 1.5, "lens": 30},
]
