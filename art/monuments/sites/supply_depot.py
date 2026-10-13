"""Supply Depot (MONUMENTS.md §5.2 no. 22, small tier): a Seedward logistics warehouse on a town road. The steel shed,
40 x 24 m and 8 m to the eaves, stands on a 1.2 m dock plinth so trucks back up to its doors: five dock doors with
levellers, bumpers and padded shelters, two still with trailers at them; stairs and a forklift ramp at the ends.
Inside, five rows of pallet racking (one row collapsed across the aisle), a forklift with a pallet still on its forks,
a charging corner. A two-storey office on the east end, a gatehouse and boom at the yard's gate, a recycler, a waste
skip, lamp posts, the fence. Crates on the racks, an amber chip in the office.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW,
                        block_building, fence, lamp, railing, srgb, stairs, stripes)
from monkit.structures import shed
from monkit.surfaces import ASPHALT, CONCRETE, CORRUGATED, GLASS, MATTE, PAINT, PANEL

KEY = "supply-depot"
TITLE = "Supply Depot"
SEED = 2222
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

W, D, H = 40.0, 24.0, 8.0       # the shed
PZ = 1.2                         # the dock plinth's height
S = Vector((-4.0, 10.0, 0.0))    # the shed's centre
DOORS = [-14.0, -7.0, 0.0, 7.0, 14.0]


def dock(b):
    """The plinth and the dock face: bumpers, levellers, shelters round the doors, lamps over them, the stairs and
    the forklift ramp, the yard's kerb in front."""
    x, y = S.x, S.y
    P.grid_box(b, (W + 1.2, D + 1.2, PZ), (x, y, PZ / 2), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), step=2.5)
    b.box((W + 1.3, 0.3, 0.12), (x, y - D / 2 - 0.55, PZ - 0.06), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    for dx in DOORS:
        dxw = x + dx
        fy = y - D / 2 - 0.6
        for s in (-1, 1):
            b.box((0.35, 0.25, 0.5), (dxw + s * 1.3, fy - 0.12, PZ - 0.45), mat=MATTE, tint=P.RUBBER)
        b.box((2.2, 0.5, 0.04), (dxw, fy + 0.2, PZ + 0.0), (-4, 0, 0), mat=PAINT, tint=GREY, solid=False)
        stripes(b, (dxw - 1.1, fy - 0.02, 0), (dxw + 1.1, fy - 0.02, 0), 0.12, width=0.25, at_z=PZ - 0.16)
        # Padded shelter round the door: two side pads and a head curtain.
        for s in (-1, 1):
            b.box((0.45, 0.5, 3.4), (dxw + s * 1.75, fy - 0.1, PZ + 1.7), mat=MATTE, tint=srgb(0.15, 0.15, 0.16),
                  solid=False)
        b.box((3.9, 0.5, 0.7), (dxw, fy - 0.1, PZ + 3.75), mat=MATTE, tint=srgb(0.15, 0.15, 0.16), solid=False)
        b.box((0.4, 0.05, 0.3), (dxw - 1.75, fy - 0.38, PZ + 2.2), mat=PAINT, tint=YELLOW, solid=False, vary=0)
        lamp(b, (dxw + 1.5, fy - 0.5, PZ + 4.3), (-35, 0, 0), size=(0.3, 0.2, 0.12))
        P.stencil(b, str(DOORS.index(dx) + 1), (dxw, fy - 0.36, PZ + 4.6), (0, 0, 0), 0.5, OFFWHITE, align="centre")
    # People stairs at the west end, the forklift ramp at the east end.
    stairs(b, (x - W / 2 - 1.8, y - D / 2 - 0.6 + 0.4 + 1.6, 0.0), (0, 0, 180), 1.2, PZ, 1.6, tint=YELLOW)
    ln = math.hypot(9.0, PZ)
    ang = math.degrees(math.atan2(PZ, 9.0))
    with b.frame((x + W / 2 + 0.6 + 4.5, y - 6.0, PZ / 2), (0, -ang, 0)):
        P.grid_box(b, (ln, 3.6, 0.3), (0, 0, -0.1), mat=CONCRETE, tint=srgb(0.68, 0.67, 0.64), step=1.8)
        for s in (-1, 1):
            b.box((ln, 0.15, 0.25), (0, s * 1.85, 0.1), mat=PAINT, tint=YELLOW, solid=False)
    b.collide_box(Vector((x + W / 2 + 0.6 + 4.5, y - 6.0, PZ / 2 - 0.15)), (ln, 3.6, 0.3), (0, -ang, 0))


def warehouse(b):
    x, y = S.x, S.y
    doors = [("front", dx, 3.0, 3.6) for dx in DOORS] + [("right", 6.0, 3.6, 3.8)]
    shed(b, (x, y, PZ - 0.25), (0, 0, 0), W, D, H, roof="gable", pitch=7.0, tint=srgb(0.66, 0.69, 0.68),
         frame_tint=srgb(0.3, 0.32, 0.34), doors=doors, ruin=0.07)
    P.stencil(b, "SUPPLY DEPOT 2", (x - 6.0, y - D / 2 - 0.25, PZ + H - 1.6), (0, 0, 0), 0.9, DARK, align="centre")
    b.box((14.0, 0.06, 1.2), (x - 6.0, y - D / 2 - 0.23, PZ + H - 1.15), mat=PAINT, tint=OFFWHITE, solid=False)
    b.box((14.0, 0.07, 0.25), (x - 6.0, y - D / 2 - 0.235, PZ + H - 0.42), mat=PAINT, tint=ORANGE, solid=False,
          vary=0)
    # Inside: racking, the collapsed row, the forklift, the charging corner, floor markings.
    fz = PZ + 0.0
    for k, ry in enumerate((y + 8.2, y + 4.4, y + 0.6, y - 3.2)):
        P.rack_row(b, (x - 4.5, ry, fz), 0.0, bays=10, levels=3, collapsed=6 if k == 2 else None,
                   fill=0.6 + 0.08 * k)
    for (yy) in (y + 6.3, y + 2.5, y - 1.3):
        b.box((28.0, 0.12, 0.012), (x - 4.5, yy, fz + 0.006), mat=PAINT, tint=YELLOW, solid=False, vary=0, wear=0.7)
    P.forklift(b, (x + 12.5, y - 1.0, fz), 80.0, fork_h=0.9, load=True)
    with b.frame((x + 16.5, y + 8.0, fz), (0, 0, 0)):
        for k in range(3):
            b.box((0.8, 0.5, 1.4), (-1.2 + 1.2 * k, 3.0, 0.7), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
            b.box((0.3, 0.02, 0.2), (-1.2 + 1.2 * k, 2.74, 1.1), mat="M_Mon_Lamp",
                  tint=srgb(0.2, 0.9, 0.4) if k != 1 else RED, solid=False, vary=0)
            b.tube([(-1.2 + 1.2 * k + 0.3, 2.74, 0.6), (-1.0 + 1.2 * k, 2.2, 0.05), (-0.6 + 0.6 * k, 1.4, 0.05)],
                   0.02, mat=PAINT, tint=BLACK, sides=4, cap=False)
        P.forklift(b, (0.4, 0.6, 0.0), 180.0, fork_h=0.0)
    b.marker("loot", (x - 10.0, y + 6.3, fz + 0.05), table="crate")
    b.marker("loot", (x + 2.0, y - 1.3, fz + 0.05), table="crate")
    b.marker("loot", (x - 1.0, y + 0.6 - 1.4, fz + 0.05), table="crate")


def build(b):
    b.set_pad(("rect", 84.0, 64.0), ground="asphalt", blend=10.0, clear=3.0)
    P.long_road(b, (-42.0, -30.0), (42.0, -30.0), width=8.0)
    P.grid_box(b, (70.0, 22.0, 0.08), (0.0, -14.0, 0.04), mat=ASPHALT, tint=srgb(0.92, 0.92, 0.92), step=6.0)
    for dx in DOORS:
        for s in (-1, 1):
            b.box((0.12, 14.0, 0.012), (S.x + dx + s * 2.1, S.y - D / 2 - 8.0, 0.086), mat=PAINT,
                  tint=srgb(0.85, 0.83, 0.75), solid=False, vary=0, wear=0.7)
    dock(b)
    warehouse(b)

    # Trailers at doors 2 and 4, a third parked across the yard.
    for k, dx in enumerate((DOORS[1], DOORS[3])):
        P.box_trailer(b, (S.x + dx, S.y - D / 2 - 0.9 - 6.6, 0.0), 180.0, code="DP-4 11" + str(k))
    P.box_trailer(b, (-22.0, -12.0, 0.0), 90.0, tint=srgb(0.58, 0.62, 0.64), band=TEAL, code="DP-4 097",
                  doors_open=False)

    # The office on the east end: two storeys, glazed, its entrance to the yard.
    block_building(b, (S.x + W / 2 + 7.0, S.y + 4.0, 0.0), (0, 0, 0), 10.0, 12.0, 7.0, floors=2,
                   mat=PANEL, tint=OFFWHITE, broken=0.45)
    b.marker("loot", (S.x + W / 2 + 7.0, S.y + 6.0, 3.6), table="chip-amber", chance=0.5)

    # The gatehouse and boom, the fence, the recycler, the skip, the lamp posts.
    gx = 30.0
    P.prefab_hut(b, (gx + 4.6, -21.0, 0.0), (0, 0, -90.0), w=3.0, d=2.4, h=2.5, code="GATE", door_open=50.0,
                 windows=1, band=ORANGE)
    P.boom_barrier(b, (gx - 3.6, -24.0, 0.1), 0.0, 6.6, raised=0.0)
    fence(b, [(gx - 4.0, -25.0, 0), (-41.0, -25.0, 0), (-41.0, 31.0, 0), (41.0, 31.0, 0), (41.0, -25.0, 0),
              (gx + 4.0, -25.0, 0)], height=2.4, tint=P.GALV)
    b.piece("recycler", (S.x + W / 2 + 3.0, -6.0, 0.0), (0, 0, 180.0))
    b.piece("dumpster", (S.x - W / 2 - 3.5, S.y + 4.0, 0.0), (0, 0, 90.0))
    b.piece("dumpster", (S.x - W / 2 - 3.5, S.y + 7.5, 0.0), (0, 0, 85.0))
    for k in range(5):
        b.piece("pallet-boxes", (S.x - W / 2 - 3.0 + b.rng.uniform(-1.0, 1.0), S.y - 6.0 + 1.3 * k, 0.0),
                (0, 0, b.rng.uniform(-8, 8)))
    for (x, y, yaw) in ((-38.0, -22.0, 135.0), (38.0, 28.0, -45.0), (-38.0, 28.0, -135.0), (5.0, -22.0, 180.0)):
        b.piece("lamp-post", (x, y, 0.0), (0, 0, yaw))
    b.piece("drum-stack", (S.x + W / 2 + 3.2, 0.5, 0.0), (0, 0, 10.0))
    b.marker("loot", (S.x + W / 2 + 4.4, 1.6, 0.05), table="barrel")
    for (x, y, s) in ((-40.0, -24.0, 0.8), (40.0, -10.0, 1.0), (-39.0, 30.0, 1.2), (20.0, 30.0, 0.9)):
        P.bush(b, (x, y, 0.0), s, P.LEAF, lumps=3)

    b.zone("no-build", (0, 0, 0), size=(96.0, 76.0, 30.0))
    b.meta.update({"tier": "small", "landmark": False, "spawn": "town-road"})


SUN = (30.0, 215.0)
PEOPLE = [((S.x + DOORS[2], S.y - D / 2 - 4.0, 0.0), 10.0)]
VIEWS = [
    {"name": "depot", "target": (0.0, 2.0, 4.0), "distance": 92, "yaw": 24, "pitch": 24, "lens": 30},
    {"name": "docks", "target": (S.x - 2.0, S.y - D / 2 - 2.0, 2.5), "distance": 24, "yaw": -30, "pitch": 4,
     "lens": 24},
    {"name": "racks", "target": (S.x - 2.0, S.y + 2.5, PZ + 2.5), "distance": 10.0, "yaw": 70, "pitch": 2,
     "lens": 18},
]
