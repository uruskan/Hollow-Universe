"""Checkpoint (MONUMENTS.md §5.1 no. 9, roadside tier): a Bulwark control point where a road enters an old region. A
chicane of concrete barriers slows the approach; sandbagged firing positions flank the lane; the boom is down on the way
in and snapped on the way out; the armoured guard booth stands between them with its door open. A watchtower covers the
road, concertina wire closes the verges, a floodlight pole and a flag mast, and the troop truck that was shot up where
it stopped. Military crate and ammunition; Reclaimers sometimes hold it.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, DARK, GREY, OLIVE, ORANGE, YELLOW, lamp, srgb)
from monkit.structures import road, watchtower
from monkit.surfaces import CONCRETE, MATTE, PAINT

KEY = "checkpoint"
TITLE = "Checkpoint"
SEED = 909
BUILDER = {"chunk": 32.0, "max_edge": 2.0}

RX = 0.0        # the road runs along Y at x = RX; traffic comes in from -Y


def build(b):
    b.set_pad(("rect", 32.0, 34.0), ground="gravel", blend=10.0, clear=3.0)
    road(b, [(RX, -17.0, 0), (RX, 17.0, 0)], width=7.0)

    # The chicane of barriers on the approach, a spike strip, the inbound boom.
    for x, y, yaw in ((-1.9, -14.0, 0.0), (1.9, -10.6, 3.0), (-1.7, -7.4, -4.0)):
        b.piece("jersey-barrier", (RX + x, y, 0), (0, 0, 90.0 + yaw))
    for k in range(10):
        b.box((0.5, 0.25, 0.05), (RX - 2.3 + k * 0.52, -4.6, 0.12), (0, 0, 0), mat=PAINT, tint=DARK, solid=False,
              vary=0)
        for j in range(3):
            b.box((0.025, 0.025, 0.08), (RX - 2.45 + k * 0.52 + j * 0.15, -4.6, 0.18), (0, 0, 45), mat=PAINT,
                  tint=GREY, solid=False, vary=0)
    P.boom_barrier(b, (RX - 3.9, -2.4, 0.1), 0.0, 6.6, raised=0.0)
    P.plate(b, (RX - 4.5, -2.85, 1.4), (0, 0, 0), (0.9, 0.5), OLIVE, srgb(0.9, 0.86, 0.7), "HALT",
            text_tint=srgb(0.92, 0.88, 0.72), text_h=0.22)
    b.cyl(0.04, 1.6, (RX - 4.5, -2.8, 0.8), mat=PAINT, tint=GREY, sides=6, solid=False)
    # The outbound boom, snapped, and the far barriers pushed aside.
    P.boom_barrier(b, (RX + 3.9, 4.6, 0.1), 180.0, 6.6, broken=True)
    b.piece("jersey-barrier", (RX + 2.6, 9.0, 0), (0, 0, 64.0))
    b.piece("jersey-barrier", (RX - 4.6, 10.2, 0), (0, 0, 118.0))

    # The booth between the lanes' kerbs on the right, its crate; floodlight pole.
    floor = P.guard_booth(b, (RX + 5.6, 0.6, 0), (0, 0, 90.0), tint=srgb(0.64, 0.65, 0.61))
    b.marker("loot", (floor.x, floor.y, floor.z + 0.02), table="military-crate")
    b.cyl(0.1, 7.0, (RX + 5.2, -3.4, 3.5), mat=PAINT, tint=GREY, sides=10)
    b.box((1.4, 0.12, 0.12), (RX + 5.2, -3.4, 7.0), mat=PAINT, tint=GREY, solid=False)
    for dx in (-0.55, 0.55):
        lamp(b, (RX + 5.2 + dx, -3.55, 6.85), (-28, 0, 0), size=(0.5, 0.25, 0.14))

    # Sandbag positions flanking the lane before the boom: a U-shaped nest each side, one with an empty gun mount.
    for side, cx in ((-1, RX - 6.0), (1, RX + 6.4)):
        cy = -7.0
        b.piece("sandbags-2m", (cx, cy - 1.0, 0), (0, 0, 0))
        b.piece("sandbags-2m", (cx - 1.2, cy + 0.1, 0), (0, 0, 90.0))
        b.piece("sandbags-2m", (cx + 1.2, cy + 0.1, 0), (0, 0, 90.0))
        b.piece("sandbags-2m", (cx - 1.2 if side > 0 else cx + 1.2, cy + 2.0, 0), (0, 0, 90.0))
    # Gun mount in the left nest: a tripod with the cradle empty; ammo boxes; the right nest has a crate of ammo.
    gx, gy = RX - 6.0, -6.6
    for k in range(3):
        a = math.radians(120 * k + 90)
        b.beam((gx + 0.6 * math.cos(a), gy + 0.6 * math.sin(a), 0.0), (gx, gy, 0.75), 0.04, 0.04, mat=PAINT,
               tint=DARK)
    b.cyl(0.06, 0.3, (gx, gy, 0.9), mat=PAINT, tint=DARK, sides=8, solid=False)
    b.box((0.5, 0.12, 0.08), (gx, gy - 0.1, 1.06), mat=PAINT, tint=DARK, solid=False)
    for k, (dx, dy) in enumerate(((0.6, 0.3), (0.3, 0.6), (-0.5, 0.5))):
        P.ammo_box(b, (gx + dx, gy + dy, 0.0), 20.0 * k, lid_open=k == 1)
    b.marker("loot", (gx + 0.4, gy + 0.9, 0.05), table="ammo")
    b.marker("loot", (RX + 6.4, -6.4, 0.05), table="ammo")
    b.marker("guard", (RX + 6.4, -6.6, 0.05), role="reclaimer", chance=0.5)
    b.marker("guard", (gx, gy + 0.4, 0.05), role="reclaimer", chance=0.5)

    # Watchtower over the left verge, sandbags at its foot.
    watchtower(b, (RX - 9.0, 3.0, 0), (0, 0, 90.0), height=6.5, tint=srgb(0.40, 0.43, 0.32))
    b.piece("sandbags-2m", (RX - 9.0, 0.6, 0), (0, 0, 0))
    b.piece("sandbags-2m", (RX - 11.2, 1.8, 0), (0, 0, 90.0))

    # Concertina wire closing both verges from the nests outward, on the diagonal.
    for x, y, yaw in ((RX - 9.6, -8.4, 168.0), (RX - 14.4, -9.2, 180.0), (RX + 10.0, -8.4, 12.0),
                      (RX + 14.6, -9.2, 0.0)):
        b.piece("concertina-5m", (x, y, 0), (0, 0, yaw))
    for x, y, yaw in ((RX - 12.6, 8.0, 90.0), (RX + 12.0, 8.0, 90.0), (RX + 12.0, 3.0, 90.0)):
        b.piece("concertina-5m", (x, y, 0), (0, 0, yaw))

    # Flag mast by the booth, its flag torn.
    fx, fy = RX + 8.6, 3.8
    b.box((0.6, 0.6, 0.3), (fx, fy, 0.1), mat=CONCRETE, tint=srgb(0.7, 0.7, 0.67))
    b.cyl(0.05, 7.5, (fx, fy, 3.9), mat=PAINT, tint=GREY, sides=8)
    with b.frame((fx, fy, 7.0), (0, 0, 200.0)):
        for k in range(4):
            b.box((0.02, 0.4, 0.75 - 0.12 * k), (0.0, 0.25 + 0.4 * k, -0.12 * k * 0.5), (0, 0, 6 * k), mat=MATTE,
                  tint=OLIVE if k < 3 else srgb(0.62, 0.3, 0.12), solid=False)
        b.box((0.025, 1.25, 0.08), (0.0, 0.72, 0.1), mat=MATTE, tint=ORANGE, solid=False)

    # The troop truck, shot up where it stopped past the boom, rear tyres flat, canvas torn.
    bed = P.military_truck(b, (RX + 1.6, 11.6, 0.0), (0, 0, 172.0), code="BW 3-118", flat=(3, 5), torn=0.35)
    b.marker("loot", tuple(bed), table="crate")
    P.blob(b, (RX + 1.4, 12.6, 0.105), 1.4, srgb(0.06, 0.06, 0.055), points=12, rough=0.35)
    P.scorch(b, (RX - 2.2, 7.4, 0.105), 0.9)
    for x, y in ((RX + 9.6, -2.2), (RX - 8.0, 8.6)):
        b.marker("loot", (x, y, 0.05), table="barrel")
    b.piece("drum-stack", (RX + 9.4, -1.2, 0), (0, 0, 8.0))

    b.zone("no-build", (0, 0, 0), size=(44.0, 46.0, 24.0))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "road-entry", "family": "bulwark"})


SUN = (30.0, 215.0)
PEOPLE = [((RX - 2.4, -5.6, 0.1), 10.0)]
VIEWS = [
    {"name": "approach", "target": (RX, 0.0, 1.5), "distance": 40, "yaw": 22, "pitch": 24, "lens": 30},
    {"name": "boom", "target": (RX + 1.0, -2.0, 1.6), "distance": 13.5, "yaw": -14, "pitch": 2, "lens": 24},
    {"name": "truck", "target": (RX + 1.0, 9.0, 1.8), "distance": 15, "yaw": 145, "pitch": 5, "lens": 28},
]
