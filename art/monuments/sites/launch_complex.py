"""Launch Complex (MONUMENTS.md §5.4, large tier, catalogue 39): a Bulwark launch site on open plains, 700 x 500 m.
A heavy launcher still stands on its mount beside a 140 m service tower on a raised hardstand, its flame trench
running east; swing arms reach from the tower to the rocket, a hammerhead crane tops it. A crawlerway runs from the
assembly hall (66 m high, its tall door half open, a stage still on its cradle inside) to the ramp, a crawler
transporter abandoned on it; a propellant farm of spheres and bullets feeds the pad over a pipe rack; a blockhouse
watches from the north-west behind its berms; lightning masts, a deluge water tower and flood masts stand round.

Gameplay: vault crates at the top of the tower (the climb is exposed and the pad is contaminated, level 3), military
crates on the swing arms and in the blockhouse, crates in the hall, fuel at the farm; a Sentinel Crawler patrols the
crawlerway and the perimeter road; Warden turrets hold the guard towers, Reclaimers work the hall.
"""
import math

from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, OLIVE, ORANGE, RED, YELLOW, block_building,
                        cable, ladder, lamp, railing, rubble, srgb, stairs, stripes)
from monkit.structures import dish, road, wall
from monkit.surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL,
                             RUST)

KEY = "launch-complex"
TITLE = "Launch Complex"
SEED = 3901
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [350.0, 1600.0, 24000.0]

H0 = 12.0                      # hardstand top
ROCKET = (52.0, 90.0)          # rocket axis
TOWER = (30.0, 90.0)           # service tower centre
TW = 14.0                      # tower width
LEVELS = [H0 + 9.0 * k for k in range(14)]   # 12 .. 129
MOUNT_TOP = 18.0
TOWER_TINT = srgb(0.36, 0.39, 0.30)
ARM_TINT = srgb(0.86, 0.43, 0.13)


# ------------------------------------------------------------------------------------------------ the pad


def hardstand(b):
    """The raised pad: three sloped concrete blocks round a flame trench running east from under the rocket, the
    launch mount over the flame hole, a deflector, the ramp up from the crawlerway, stairs for people."""
    rx, ry = ROCKET
    t0, t1 = ry - 7.0, ry + 7.0          # trench walls
    x_w = rx - 10.0                       # trench's west end
    top = dict(x0=-15.0, x1=95.0, y0=45.0, y1=135.0)
    tint = srgb(0.80, 0.79, 0.75)
    P.block(b, top["x0"], x_w, top["y0"], top["y1"], H0, {"w": 20, "s": 20, "n": 20}, mat=CONCRETE, tint=tint)
    P.block(b, x_w, top["x1"], t1, top["y1"], H0, {"n": 20, "e": 20}, mat=CONCRETE, tint=tint)
    P.block(b, x_w, top["x1"], top["y0"], t0, H0, {"s": 20, "e": 20}, mat=CONCRETE, tint=tint)
    # The deck's skin: slabs with joints, darker where the exhaust swept it.
    for x0, x1, y0, y1 in ((top["x0"], x_w, top["y0"], top["y1"]), (x_w, top["x1"], t1, top["y1"]),
                           (x_w, top["x1"], top["y0"], t0)):
        b.box((x1 - x0, y1 - y0, 0.1), ((x0 + x1) / 2, (y0 + y1) / 2, H0 + 0.05), mat=CONCRETE,
              tint=srgb(0.72, 0.71, 0.68), wear=0.5)
    # Trench floor and walls lined with refractory brick (dark), the deflector wedge.
    b.box((top["x1"] + 20 - x_w, t1 - t0, 0.3), ((x_w + top["x1"] + 20) / 2, ry, 0.15), mat=CONCRETE,
          tint=srgb(0.30, 0.28, 0.26), wear=0.8)
    for y in (t0 + 0.25, t1 - 0.25):
        b.box((top["x1"] - x_w, 0.5, H0 - 0.5), ((x_w + top["x1"]) / 2, y, H0 / 2), mat=CONCRETE,
              tint=srgb(0.36, 0.33, 0.30), wear=0.9)
    b.mesh([(x_w, t0, 0.3), (x_w + 22, t0, 0.3), (x_w + 22, t1, 0.3), (x_w, t1, 0.3), (x_w, t0, H0 - 1.5),
            (x_w, t1, H0 - 1.5)], [[0, 3, 2, 1], [0, 1, 4], [3, 5, 2], [1, 2, 5, 4], [0, 4, 5, 3]], mat=RUST,
           tint=srgb(0.55, 0.5, 0.45), solid="hull", smooth=False)
    for k in range(6):
        P.scorch(b, (x_w + 8 + k * 14, ry, 0.3), 7.0, blobs=3)
    # Deluge pipes along the trench walls.
    for y in (t0 + 0.9, t1 - 0.9):
        b.tube([(x_w + 1, y, 8.0), (top["x1"] + 4, y, 8.0)], 0.45, mat=RUST, tint=(1, 1, 1), sides=12)
        for k in range(8):
            b.cyl(0.18, 0.8, (x_w + 6 + k * 9.5, y + (0.5 if y < ry else -0.5), 8.0), (90, 0, 0), mat=RUST,
                  tint=(1, 1, 1), sides=8, solid=False)
    # The launch mount: four pedestals and a ring deck over the flame hole, hold-down arms, a scorched skirt.
    mx0, mx1, my0, my1 = rx - 12, rx + 12, ry - 12, ry + 12
    for x in (mx0 + 2, mx1 - 2):
        for y in (my0 + 2, my1 - 2):
            b.box((4.0, 4.0, MOUNT_TOP - 2.6 - H0), (x, y, H0 + (MOUNT_TOP - 2.6 - H0) / 2), mat=FORMWORK,
                  tint=srgb(0.72, 0.71, 0.68), bevel=0.05)
    mt = srgb(0.40, 0.42, 0.40)
    hole = 5.5
    b.box((mx1 - mx0, (my1 - my0) / 2 - hole, 2.6), (rx, my0 + ((my1 - my0) / 2 - hole) / 2, MOUNT_TOP - 1.3),
          mat=PAINT, tint=mt)
    b.box((mx1 - mx0, (my1 - my0) / 2 - hole, 2.6), (rx, my1 - ((my1 - my0) / 2 - hole) / 2, MOUNT_TOP - 1.3),
          mat=PAINT, tint=mt)
    for x in (mx0 + (12 - hole) / 2, mx1 - (12 - hole) / 2):
        b.box((12 - hole, 2 * hole, 2.6), (x, ry, MOUNT_TOP - 1.3), mat=PAINT, tint=mt)
    P.scorch(b, (rx, ry, MOUNT_TOP), 10.0, blobs=5)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        with b.frame((rx + math.cos(a) * 7.2, ry + math.sin(a) * 7.2, MOUNT_TOP), (0, 0, math.degrees(a))):
            b.box((2.4, 1.2, 2.2), (0, 0, 1.1), mat=PAINT, tint=DARK, bevel=0.05)
            b.box((1.4, 0.8, 0.8), (-1.2, 0, 2.0), mat=PAINT, tint=srgb(0.5, 0.5, 0.48), solid=False)
    P.deck(b, mx0, my0, mx1, my1, MOUNT_TOP, rails="snwe", gaps={"s": [(rx - 1.6, rx + 1.6)]}, edge=False,
           floor=False)
    # Stairs from the deck to the mount, the ramp from the crawlerway, people's stairs up the west slope.
    stairs(b, (rx, my0 - 9.0, H0), (0, 0, 0), 2.0, MOUNT_TOP - H0, 8.6, tint=YELLOW)
    P.ramp(b, (40.0, 45.0), (40.0, -75.0), 30.0, H0, 0.0, mat=CONCRETE, tint=srgb(0.70, 0.69, 0.65))
    for x in (25.4, 54.6):
        b.beam((x, -75.0, 0.45), (x, 45.0, H0 + 0.45), 0.8, 0.9, mat=CONCRETE, tint=CONCRETE_T)
    stairs(b, (-35.6, 70.0, 0.0), (0, 0, -90), 2.4, H0, 20.6, tint=YELLOW)
    # Pad furniture: cable masts and junction boxes, a scorched hardstand round the mount.
    for x, y in ((0, 52), (88, 52), (88, 128), (0, 128)):
        b.box((2.0, 1.2, 2.2), (x, y, H0 + 1.1), mat=PAINT, tint=srgb(0.55, 0.56, 0.52), bevel=0.04)
        P.plate(b, (x, y - 0.62, H0 + 1.5), (0, 0, 0), 0.8, 0.6)
    for k in range(4):
        P.scorch(b, (rx - 18 + 12 * k, ry + (14 if k % 2 else -16), H0 + 0.1), 6.0, blobs=3)
    # Painted safety lanes and cable trench covers on the deck.
    lane = srgb(0.85, 0.66, 0.12)
    for (ax, ay), (cx, cy) in (((-10.0, 50.0), (90.0, 50.0)), ((-10.0, 130.0), (90.0, 130.0)),
                               ((-10.0, 50.0), (-10.0, 130.0)), ((40.0, 45.0), (40.0, 72.0))):
        L = math.hypot(cx - ax, cy - ay)
        b.box((L if ay == cy else 0.3, 0.3 if ay == cy else L, 0.02), ((ax + cx) / 2, (ay + cy) / 2, H0 + 0.11),
              mat=PAINT, tint=lane, solid=False, vary=0, wear=0.7)
    for k in range(24):
        b.box((1.4, 0.9, 0.12), (-6.0 + k * 1.5, 66.0, H0 + 0.16), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    P.scrap(b, (70.0, 60.0), 9.0, count=8, z=H0 + 0.1)
    P.scrap(b, (5.0, 120.0), 7.0, count=6, z=H0 + 0.1)
    rubble(b, (90.0, 75.0, H0 + 0.1), 4.0, count=6, tint=srgb(0.7, 0.69, 0.66))
    # The burn fan beyond the trench mouth: blackened ground.
    for k in range(7):
        P.scorch(b, (118.0 + k * 9.0, ry + b.rng.uniform(-6, 6), 0.0), 9.0 + k * 1.6, blobs=4)


def tower(b):
    """The service tower: a braced olive steel frame 14 m square on the hardstand, a grated deck every 9 m with a
    switchback stair inside, a lift shaft, swing arms to the rocket, a hammerhead crane and a lightning rod on top."""
    tx, ty = TOWER
    P.steel_frame(b, (tx, ty, 0), (0, 0, 0), TW, TW, LEVELS, col=0.9, tint=TOWER_TINT, bays=(2, 2),
                  beam=(0.5, 0.7), brace_w=0.35)
    b.box((TW + 3, TW + 3, 1.2), (tx, ty, H0 + 0.6), mat=CONCRETE, tint=CONCRETE_T)
    sx, sy = tx - 3.4, ty - 3.2         # stair origin
    zs = [H0 + 1.2] + LEVELS[1:]
    for z0, z1 in zip(zs, zs[1:]):
        W, L, run = P.switchback(b, (sx, sy, z0), (0, 0, 0), z1 - z0, 1.2, flights=4, steps=12, tint=YELLOW)
    hole = (sx - W / 2 - 0.05, sy, sx + W / 2 + 0.05, sy + run + 1.4)
    arm_levels = {LEVELS[3]: "core", LEVELS[6]: "core", LEVELS[8]: "interstage", LEVELS[9]: "upper",
                  LEVELS[11]: "payload"}
    for i, z in enumerate(LEVELS[1:]):
        gaps = {"e": [(ty - 1.3, ty + 1.3)]} if z in arm_levels else {}
        P.deck(b, tx - TW / 2, ty - TW / 2, tx + TW / 2, ty + TW / 2, z, mat=GRATE, tint=GREY, rails="snwe",
               gaps=gaps, hole=hole, rail_tint=YELLOW)
        for (a0, a1) in (((hole[0], hole[1]), (hole[0], hole[3])), ((hole[2], hole[1]), (hole[2], hole[3])),
                         ((hole[0], hole[3]), (hole[2], hole[3]))):
            railing(b, (a0[0], a0[1], z), (a1[0], a1[1], z), tint=YELLOW)
        if i % 3 == 1:
            lamp(b, (tx + TW / 2 + 0.3, ty - TW / 2 + 1.0, z + 3.5), (30, 0, 90), size=(0.5, 0.3, 0.3))
    # Lift shaft in the north-west quadrant, its car stuck halfway.
    lx, ly = tx - 3.6, ty + 4.2
    for x in (lx - 1.5, lx + 1.5):
        for y in (ly - 1.5, ly + 1.5):
            b.box((0.25, 0.25, LEVELS[-1] - H0), (x, y, (LEVELS[-1] + H0) / 2), mat=PAINT, tint=DARK, vary=0)
    b.box((2.6, 2.6, 2.6), (lx, ly, LEVELS[5] + 1.6), mat=PAINT, tint=LIGHTGREY, bevel=0.05)
    b.box((2.4, 0.05, 2.2), (lx, ly - 1.31, LEVELS[5] + 1.5), mat=GRATE, tint=GREY, solid=False)
    cable(b, (lx, ly, LEVELS[-1] + 2.0), (lx, ly, LEVELS[5] + 2.9), sag=0.0, radius=0.04)
    # Cable and pipe risers on the east face.
    for k, (r, tint) in enumerate(((0.35, OFFWHITE), (0.28, YELLOW), (0.22, GREY), (0.3, OFFWHITE))):
        x = tx + TW / 2 + 0.6
        y = ty - TW / 2 + 1.0 + k * 0.9
        b.tube([(x, y, H0 + 1.2), (x, y, LEVELS[12] + 1.0)], r, mat=PAINT, tint=tint, sides=10)
    # Swing arms: orange trusses with walkways from the east face to the rocket, umbilicals hanging below.
    for z, kind in arm_levels.items():
        rr = {"upper": 3.7, "payload": 4.6}.get(kind, 4.2)
        x0, x1 = tx + TW / 2, ROCKET[0] - rr - 0.6
        P.truss(b, (x0, ty, z - 0.2), (x1, ty, z - 0.2), w=2.4, h=2.4, tint=ARM_TINT, deck_rails=True)
        b.box((1.6, 3.2, 3.4), (x1 + 0.4, ty, z + 1.5), mat=PAINT, tint=OFFWHITE if kind == "payload" else ARM_TINT,
              bevel=0.05, solid=False)
        for dy in (-0.8, 0.0, 0.8):
            pts = [(x0 + 1.0, ty + dy, z - 0.3), ((x0 + x1) / 2, ty + dy, z - 2.4 - abs(dy)), (x1, ty + dy, z - 0.8)]
            b.tube(pts, 0.14, mat=PAINT, tint=BLACK if dy else srgb(0.75, 0.75, 0.7), sides=8)
        b.marker("loot", (x1 - 1.0, ty, z), table="military-crate" if kind in ("interstage", "payload") else "crate")
    # The top: an armoured hut with the vault crates, the hammerhead crane, the lightning rod.
    zt = LEVELS[-1]
    P.room(b, (tx + 2.5, ty + 2.5, zt), (0, 0, 0), 7.0, 7.0, 3.2, thick=0.3, mat=PAINT, tint=P.OLIVE_DARK,
           openings={"front": [(-1.0, 1.4, 2.3, 0.0)], "left": [(0.0, 2.0, 0.6, 1.6)]}, floor=False,
           roof_mat=PAINT, roof_tint=P.OLIVE_DARK)
    b.marker("door", (tx + 1.5, ty - 1.4, zt), kind_of="hatch")
    b.marker("loot", (tx + 1.0, ty + 3.6, zt + 0.1), table="vault-crate")
    b.marker("loot", (tx + 4.4, ty + 3.6, zt + 0.1), table="vault-crate")
    P.crates(b, (tx + 3.5, ty + 1.2, zt), (0, 0, 0), n=3, tint=P.OLIVE_DARK)
    b.marker("guard", (tx - 6.0, ty - 6.0, zt), role="warden-turret")
    # Crane: a slewing base, a cab, the jib east over the rocket and a counter-jib with its weight.
    cz = zt + 4.0
    b.cyl(2.2, 4.0, (tx + 3.5, ty - 3.5, zt + 2.0), mat=PAINT, tint=TOWER_TINT, sides=16)
    with b.frame((tx + 3.5, ty - 3.5, cz), (0, 0, 9.0)):
        b.box((4.0, 3.0, 1.2), (0, 0, 0.6), mat=PAINT, tint=TOWER_TINT)
        b.box((2.2, 2.0, 2.2), (-0.6, -2.4, 1.4), mat=PAINT, tint=OFFWHITE, bevel=0.06)
        b.box((2.0, 0.05, 1.0), (-0.6, -3.42, 1.8), mat=GLASS, solid=False)
        P.truss(b, (0, 0, 1.2), (32.0, 0, 1.2), w=1.8, h=2.0, tint=ARM_TINT)
        P.truss(b, (0, 0, 1.2), (-12.0, 0, 1.2), w=1.8, h=2.0, tint=ARM_TINT)
        b.box((4.0, 3.2, 3.0), (-11.0, 0, 0.2), mat=CONCRETE, tint=CONCRETE_T)
        b.box((1.0, 1.0, 8.0), (0, 0, 5.0), mat=PAINT, tint=ARM_TINT)
        cable(b, (0, 0, 9.0), (30.0, 0, 3.2), sag=0.0, radius=0.05)
        cable(b, (0, 0, 9.0), (-11.0, 0, 3.2), sag=0.0, radius=0.05)
        b.box((1.6, 1.2, 0.8), (23.0, 0, 0.8), mat=PAINT, tint=DARK, solid=False)
        cable(b, (23.0, 0, 0.4), (23.0, 0, -18.0), sag=0.0, radius=0.04)
        b.box((1.2, 0.8, 1.4), (23.0, 0, -18.6), mat=PAINT, tint=YELLOW, solid=False)
        P.beacon(b, (32.0, 0, 3.4), 0.4)
        P.beacon(b, (0, 0, 9.0), 0.5)
    b.cyl(0.25, 10.0, (tx + 6.6, ty + 6.6, zt + 5.0), mat=PAINT, tint=GREY, sides=8, radius2=0.08)
    P.beacon(b, (tx + 6.6, ty + 6.6, zt + 10.0), 0.4)
    for x, y in ((tx - TW / 2, ty - TW / 2), (tx + TW / 2, ty + TW / 2)):
        P.beacon(b, (x, y, zt + 1.1), 0.35)
    b.marker("landmark", (tx, ty, zt + 12.0), kind_of="launch-tower", visible_km=18)
    # A sign board on the south face, up high, with the pad's code.
    b.box((12.0, 0.25, 7.6), (tx, ty - TW / 2 - 0.6, LEVELS[10] + 4.4), mat=PAINT, tint=P.OLIVE_DARK)
    P.stencil(b, "A7", (tx - 4.2, ty - TW / 2 - 0.75, LEVELS[10] + 1.6), (0, 0, 0), 5.4, tint=OFFWHITE)
    # Weather cladding on the upper levels' west and north faces, panels missing.
    for z0 in LEVELS[10:13]:
        for k in range(5):
            if b.rng.random() < 0.25:
                continue
            y = ty - TW / 2 + 1.4 + k * 2.8
            b.box((0.12, 2.7, 8.6), (tx - TW / 2 - 0.55, y, z0 + 4.5), mat=CORRUGATED, tint=srgb(0.62, 0.64, 0.58))
        for k in range(5):
            if b.rng.random() < 0.3:
                continue
            x = tx - TW / 2 + 1.4 + k * 2.8
            b.box((2.7, 0.12, 8.6), (x, ty + TW / 2 + 0.55, z0 + 4.5), mat=CORRUGATED, tint=srgb(0.62, 0.64, 0.58))


def rocket(b):
    """The launcher: an off-white core with olive bands, four strap-on boosters, an interstage with a roll pattern,
    an upper stage and a wide payload fairing; engine bells down into the flame hole."""
    rx, ry = ROCKET
    z0 = MOUNT_TOP
    R = 4.2
    core_top = z0 + 62.0
    b.lathe([(R + 0.4, 0.0), (R + 0.1, 3.0), (R, 3.6)], (rx, ry, z0), mat=PANEL, tint=srgb(0.5, 0.52, 0.5), sides=40)
    b.cyl(R, core_top - z0 - 3.6, (rx, ry, (z0 + 3.6 + core_top) / 2), mat=PANEL, tint=OFFWHITE, sides=40)
    for z, h, t in ((z0 + 10, 1.4, P.OLIVE_DARK), (z0 + 36, 3.0, P.OLIVE_DARK), (core_top - 2.0, 1.2, BLACK)):
        b.cyl(R + 0.03, h, (rx, ry, z), mat=PAINT, tint=t, sides=40, solid=False, vary=0)
    for k in range(10):
        b.cyl(R + 0.015, 0.12, (rx, ry, z0 + 6 + k * 5.6), mat=PAINT, tint=srgb(0.7, 0.7, 0.68), sides=40,
              solid=False, vary=0)
    for ang in (90.0, 270.0):
        a = math.radians(ang)
        b.box((0.5, 0.7, core_top - z0 - 6), (rx + math.cos(a) * (R + 0.3), ry + math.sin(a) * (R + 0.3),
                                               (z0 + 3 + core_top - 3) / 2), (0, 0, ang), mat=PANEL,
              tint=LIGHTGREY, solid=False)
    b.tube([(rx - R - 0.35, ry - 0.9, z0 + 4.0), (rx - R - 0.35, ry - 0.9, core_top - 4.0)], 0.32, mat=PAINT,
           tint=srgb(0.62, 0.6, 0.55), sides=10)
    for k in range(8):
        b.box((0.9, 0.5, 0.25), (rx - R - 0.2, ry - 0.9, z0 + 8 + k * 6.5), mat=PAINT, tint=GREY, solid=False)
    # Interstage: alternating black and white quarters.
    for k in range(4):
        a0 = 90 * k
        verts, faces = [], []
        n = 8
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            for z in (core_top, core_top + 6.0):
                verts.append((rx + math.cos(a) * (R + 0.04), ry + math.sin(a) * (R + 0.04), z))
        for i in range(n):
            faces.append([2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1])
        b.mesh(verts, faces, mat=PAINT, tint=BLACK if k % 2 else OFFWHITE, solid=False, smooth=True)
    b.cyl(R, 6.0, (rx, ry, core_top + 3.0), mat=PANEL, tint=OFFWHITE, sides=40, solid=True)
    b.cyl(R, 2.0, (rx, ry, core_top + 7.0), mat=PANEL, tint=OFFWHITE, sides=40, radius2=3.7, solid="hull")
    up0, up1 = core_top + 8.0, core_top + 22.0
    b.cyl(3.7, up1 - up0, (rx, ry, (up0 + up1) / 2), mat=PANEL, tint=OFFWHITE, sides=36)
    b.cyl(3.73, 1.0, (rx, ry, up0 + 4.0), mat=PAINT, tint=P.OLIVE_DARK, sides=36, solid=False, vary=0)
    # Fairing: wider than the stage below, an ogive to a point.
    fz = up1
    prof = [(3.7, 0.0), (4.6, 2.0), (4.6, 9.0)]
    for k in range(1, 9):
        t = k / 8
        prof.append((4.6 * math.cos(t * math.pi / 2) ** 0.8, 9.0 + 9.0 * math.sin(t * math.pi / 2)))
    prof[-1] = (0.0, 18.0)
    b.lathe(prof, (rx, ry, fz), mat=PANEL, tint=srgb(0.94, 0.93, 0.9), sides=40)
    b.cyl(4.63, 0.6, (rx, ry, fz + 5.0), mat=PAINT, tint=ORANGE, sides=40, solid=False, vary=0)
    b.cyl(0.12, 3.0, (rx, ry, fz + 19.4), mat=PAINT, tint=GREY, sides=6, solid=False)
    P.beacon(b, (rx, ry, fz + 20.9), 0.3, range_m=900.0)
    # The code on the core above the boosters, bent round it, facing south-east.
    a = math.radians(-60.0)
    P.stencil(b, "A7", (rx + math.cos(a) * (R + 0.02), ry + math.sin(a) * (R + 0.02), z0 + 53.0),
              (0, 0, math.degrees(a) + 90), 4.5, tint=P.OLIVE_DARK, depth=0.02, wrap=R + 0.02)
    # Boosters.
    for k in range(4):
        a = math.radians(45 + 90 * k)
        bx, by = rx + math.cos(a) * 6.4, ry + math.sin(a) * 6.4
        bh = 44.0
        b.lathe([(2.3, 0.0), (2.0, 2.0)], (bx, by, z0), mat=PANEL, tint=srgb(0.5, 0.52, 0.5), sides=24)
        b.cyl(2.0, bh - 2.0, (bx, by, z0 + 2.0 + (bh - 2.0) / 2), mat=PANEL, tint=OFFWHITE, sides=24)
        b.lathe([(2.0, 0.0), (1.8, 2.5), (1.2, 5.0), (0.4, 6.8), (0.0, 7.2)], (bx, by, z0 + bh), mat=PANEL,
                tint=OFFWHITE, sides=24)
        b.cyl(2.03, 1.0, (bx, by, z0 + 30.0), mat=PAINT, tint=P.OLIVE_DARK, sides=24, solid=False, vary=0)
        for z in (z0 + 6.0, z0 + bh - 4.0):
            b.beam((bx, by, z), (rx + math.cos(a) * (R - 0.2), ry + math.sin(a) * (R - 0.2), z + 1.2), 0.35,
                   mat=PAINT, tint=GREY)
        b.lathe([(0.35, 0.0), (0.9, -1.6), (1.25, -3.0)], (bx, by, z0), mat=RUST, tint=(1, 1, 1), sides=16,
                solid=False)
    for k in range(5):
        if k == 4:
            ex, ey = rx, ry
        else:
            a = math.radians(90 * k)
            ex, ey = rx + math.cos(a) * 2.3, ry + math.sin(a) * 2.3
        b.lathe([(0.45, 0.0), (1.0, -1.8), (1.35, -3.4)], (ex, ey, z0), mat=RUST, tint=srgb(0.8, 0.8, 0.8),
                sides=16, solid=False)
    b.marker("landmark", (rx, ry, fz + 20.0), kind_of="rocket", visible_km=15)


# ------------------------------------------------------------------------------------------------ the works


def assembly_hall(b):
    """The assembly hall: 120 x 80 m and 66 m to the eaves, olive cladding over a concrete plinth, its tall door on
    the east side half open; inside, work platforms, a stage on its cradle and a bridge crane."""
    hx, hy = -190.0, -120.0
    w, d, h = 120.0, 80.0, 66.0
    P.hall(b, (hx, hy, 0), (0, 0, 0), w, d, h, bay=10.0, plinth=6.0, clad_tint=OLIVE, roof_tint=P.BW_GREY,
           plinth_tint=P.BW_CONCRETE, frame_tint=P.OLIVE_DARK, band=P.BW_GREY, ruin=0.08, panel=3.4,
           windows=("front", "back"), win_h=3.0, parapet=1.4,
           doors={"right": [(0.0, 26.0, 58.0, "open")], "front": [(-30.0, 8.0, 7.0, "shutter"),
                                                                  (10.0, 1.4, 2.3, "door")],
                  "left": [(-15.0, 1.4, 2.3, "door")], "back": [(25.0, 10.0, 8.0, "shutter")]})
    # The tall door's upper leaves, raised and stacked, a broken one hanging.
    ex = hx + w / 2 + 0.9
    for k in range(4):
        z = 34.0 + k * 6.2
        b.box((0.8, 27.0, 6.0), (ex + 0.25 * (k % 2), hy, z + 3.0), mat=CORRUGATED, tint=srgb(0.42, 0.45, 0.36))
        b.box((0.9, 27.2, 0.4), (ex + 0.25 * (k % 2), hy, z + 0.2), mat=PAINT, tint=P.OLIVE_DARK, solid=False)
    b.box((0.8, 12.0, 6.0), (ex + 1.5, hy - 6.5, 30.5), (0, 8, 4), mat=CORRUGATED, tint=srgb(0.42, 0.45, 0.36),
          solid=False)
    P.stencil(b, "07", (hx - 22.0, hy - d / 2 - 0.42, 30.0), (0, 0, 0), 20.0, tint=OFFWHITE)
    P.stencil(b, "07", (hx + w / 2 + 0.42, hy + 22.0, 46.0), (0, 0, 90), 10.0, tint=OFFWHITE)
    stripes(b, (ex + 0.5, hy - 14.0, 0), (ex + 0.5, hy + 14.0, 0), 1.5, width=1.4, at_z=58.5, normal=(1, 0, 0))
    # Inside: work platforms on the north wall, five decks with a stair tower.
    zs = [0.3, 12.0, 22.0, 32.0, 42.0, 52.0]
    P.steel_frame(b, (hx - 10.0, hy + d / 2 - 7.0, 0), (0, 0, 0), 70.0, 10.0, zs, col=0.6, tint=YELLOW,
                  bays=(7, 1), brace=("n",), beam=(0.3, 0.5), brace_w=0.25)
    for z in zs[1:]:
        P.deck(b, hx - 45.0, hy + d / 2 - 12.0, hx + 25.0, hy + d / 2 - 2.0, z, mat=GRATE, tint=GREY, rails="s",
               rail_tint=YELLOW)
    st = (hx + 30.0, hy + d / 2 - 8.0)
    x0, y0, x1, y1 = P.stair_tower(b, (st[0], st[1], 0.3), (0, 0, 0), [z - 0.3 for z in zs], width=1.2)
    for k, z in enumerate(zs[1:]):
        P.deck(b, hx + 25.0, st[1] - 1.4, st[0] + x0 + 0.3, st[1], z, rails="sn")
        b.marker("loot", (hx - 40.0 + 15 * k, hy + d / 2 - 6.0, z + 0.1), table="crate" if k % 2 else "barrel")
    # A core stage on its cradle, a second one's tank segment upright in a fixture.
    sx, sy = hx - 5.0, hy - 12.0
    b.cyl(4.2, 40.0, (sx, sy, 6.2), (0, 90, 0), mat=PANEL, tint=OFFWHITE, sides=40)
    b.cyl(4.23, 1.4, (sx + 6, sy, 6.2), (0, 90, 0), mat=PAINT, tint=P.OLIVE_DARK, sides=40, solid=False, vary=0)
    for x in (sx - 14.0, sx, sx + 14.0):
        b.box((2.0, 10.0, 2.6), (x, sy, 1.6), mat=PAINT, tint=YELLOW, bevel=0.05)
        b.box((2.0, 1.5, 3.0), (x, sy - 4.2, 3.6), mat=PAINT, tint=YELLOW, solid=False)
        b.box((2.0, 1.5, 3.0), (x, sy + 4.2, 3.6), mat=PAINT, tint=YELLOW, solid=False)
    b.cyl(4.2, 14.0, (hx - 40.0, hy - 15.0, 7.3), mat=PANEL, tint=OFFWHITE, sides=40)
    P.steel_frame(b, (hx - 40.0, hy - 15.0, 0), (0, 0, 0), 12.0, 12.0, [0.3, 7.0, 14.0], col=0.4, tint=YELLOW,
                  brace=("s", "n"))
    # Bridge crane high in the bay.
    cy = hy + 4.0
    for y in (cy - 2.0, cy + 2.0):
        b.box((w - 2.0, 1.2, 3.0), (hx, y, 60.0), mat=PAINT, tint=YELLOW)
    b.box((6.0, 7.0, 2.4), (hx + 12.0, cy, 62.6), mat=PAINT, tint=YELLOW, bevel=0.06)
    cable(b, (hx + 12.0, cy, 61.4), (hx + 12.0, cy, 20.0), sag=0.0, radius=0.06)
    b.box((2.2, 1.6, 2.0), (hx + 12.0, cy, 19.0), mat=PAINT, tint=DARK, solid=False)
    for x in (hx - 20.0, hx + 20.0, hx + 45.0):
        lamp(b, (x, hy - 10.0, 58.0), (90, 0, 0), size=(0.9, 0.5, 0.4))
    # Loot and guards.
    for p in ((hx + 40.0, hy - 25.0), (hx - 50.0, hy + 10.0)):
        b.marker("loot", (p[0], p[1], 0.3), table="crate")
    for p in ((hx + 46.0, hy + 20.0), (hx - 30.0, hy - 30.0), (hx + 10.0, hy + 25.0)):
        b.marker("loot", (p[0], p[1], 0.3), table="barrel")
    b.marker("guard", (hx + 35.0, hy - 5.0, 0.3), role="reclaimer")
    b.marker("guard", (hx - 20.0, hy + 28.0, 22.1), role="reclaimer")
    b.marker("guard", (hx + 50.0, hy + 10.0, 0.3), role="reclaimer")
    P.crates(b, (hx + 44.0, hy - 28.0, 0.3), (0, 0, 15), n=5)
    P.crates(b, (hx - 48.0, hy + 14.0, 0.3), (0, 0, -10), n=4, tint=P.BW_GREY)
    P.scrap(b, (hx + 20.0, hy + 10.0), 10.0, count=10, z=0.3)


def crawler(b, at, yaw):
    """The crawler transporter: a 40 x 36 m deck on four double-tracked trucks, cabs at two corners, jacking
    cylinders, a stair to the deck."""
    with b.frame(at, (0, 0, yaw)):
        dz = 7.2
        for x in (-14.0, 14.0):
            for y in (-13.0, 13.0):
                for dy in (-2.6, 2.6):
                    b.box((12.0, 3.2, 2.6), (x, y + dy, 1.3), mat=RUST, tint=srgb(0.7, 0.68, 0.64), bevel=0.2)
                    for k in range(5):
                        b.cyl(1.0, 3.4, (x - 4.8 + 2.4 * k, y + dy, 1.2), (90, 0, 0), mat=PAINT, tint=DARK,
                              sides=12, solid=False)
                b.box((8.0, 9.0, 2.2), (x, y, 3.7), mat=PAINT, tint=srgb(0.62, 0.62, 0.58), bevel=0.1)
                for dx in (-2.5, 2.5):
                    b.cyl(0.7, 2.4, (x + dx, y, 5.6), mat=PAINT, tint=LIGHTGREY, sides=12)
        b.box((40.0, 36.0, 3.2), (0, 0, dz - 1.6), mat=PAINT, tint=srgb(0.64, 0.64, 0.6), bevel=0.15)
        b.box((40.4, 36.4, 0.25), (0, 0, dz + 0.1), mat=GRATE, tint=GREY, vary=0)
        for x in (-16.0, 16.0):
            b.box((5.0, 3.0, 3.0), (x, -19.0, dz - 0.5), mat=PAINT, tint=OFFWHITE, bevel=0.08)
            b.box((4.6, 0.05, 1.2), (x, -20.52, dz), mat=GLASS, solid=False)
        b.box((6.0, 8.0, 2.8), (0, 8.0, dz + 1.6), mat=PAINT, tint=srgb(0.55, 0.57, 0.52), bevel=0.06)
        for k in range(3):
            b.cyl(0.8, 3.0, (-6.0 + k * 1.9, 8.0, dz + 4.4), mat=PAINT, tint=DARK, sides=10)
        P.deck(b, -20.0, -18.0, 20.0, 18.0, dz + 0.22, thick=0.02, mat=GRATE, rails="snwe",
               gaps={"s": [(-12.0, -8.0)]}, edge=False, solid=False)
        stairs(b, (-10.0, -24.6, 0.0), (0, 0, 0), 1.6, dz, 6.6, tint=YELLOW)
        P.stencil(b, "C2", (6.0, -18.05, dz - 2.7), (0, 0, 0), 2.2, tint=BLACK)
        b.marker("loot", (12.0, 10.0, dz + 0.3), table="crate")
        b.marker("loot", (-15.0, -19.5, dz - 0.3), table="barrel")


def propellant_farm(b):
    """Two spherical tanks, four bullets, a vaporiser bank, a pump house and the rack to the pad."""
    P.sphere_tank(b, (205.0, 135.0, 0), 10.0, legs=10, tint=OFFWHITE, band=P.OLIVE_DARK, code="02")
    P.sphere_tank(b, (245.0, 135.0, 0), 10.0, legs=10, tint=OFFWHITE, band=ORANGE, code="03")
    for k in range(4):
        P.bullet_tank(b, (180.0 + k * 13.0, 70.0, 0), (0, 0, 90), 2.6, 24.0, tint=OFFWHITE if k % 2 else LIGHTGREY,
                      band=P.OLIVE_DARK if k % 2 else None)
    # Vaporisers: finned columns in two rows.
    for i in range(6):
        for j in range(2):
            x, y = 240.0 + i * 4.0, 66.0 + j * 7.0
            b.box((1.2, 1.2, 8.0), (x, y, 4.0), mat=PAINT, tint=LIGHTGREY, solid=True, vary=0.02)
            for a in (0, 90):
                b.box((2.6, 0.08, 7.6), (x, y, 4.2), (0, 0, a), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
        b.tube([(240.0 + i * 4.0, 66.0, 8.3), (240.0 + i * 4.0, 73.0, 8.3)], 0.2, mat=PAINT, tint=GREY, sides=8)
    b.tube([(238.0, 69.5, 8.6), (262.0, 69.5, 8.6)], 0.35, mat=PAINT, tint=OFFWHITE, sides=12)
    # Pump house.
    P.room(b, (225.0, 100.0, 0), (0, 0, 90), 16.0, 10.0, 5.5, mat=FORMWORK, tint=P.BW_CONCRETE,
           openings={"front": [(3.0, 1.2, 2.3, 0.0), (-3.0, 2.0, 1.2, 1.4)], "back": [(0.0, 3.0, 3.2, 0.0)]})
    b.marker("door", (230.6, 103.0, 0), kind_of="building")
    b.marker("loot", (224.0, 97.0, 0.1), table="fuel-barrel")
    b.marker("loot", (226.0, 104.0, 0.1), table="fuel-barrel")
    b.piece("drum-stack", (232.0, 92.0, 0), (0, 0, 12))
    b.piece("drum-stack", (233.6, 93.4, 0), (0, 0, -20))
    b.marker("loot", (234.0, 90.0, 0.1), table="fuel-barrel")
    # Manifold pipes from the tanks to the rack, the rack west to the pad, pipes up the slope to the tower.
    for x in (205.0, 245.0):
        b.tube([(x, 122.0, 1.2), (x, 108.0, 1.2), (x, 108.0, 6.8)], 0.4, mat=PAINT, tint=OFFWHITE, sides=12)
    b.tube([(205.0, 108.0, 6.8), (245.0, 108.0, 6.8)], 0.4, mat=PAINT, tint=OFFWHITE, sides=12)
    for k in range(4):
        b.tube([(180.0 + k * 13.0, 82.0, 4.0), (180.0 + k * 13.0, 101.0, 4.0), (180.0 + k * 13.0, 101.0, 6.6)], 0.3,
               mat=PAINT, tint=GREY, sides=10)
    P.pipe_rack(b, [(200.0, 104.0), (118.0, 104.0)], width=4.0, height=6.0, bent=8.0,
                pipes=[(0.45, OFFWHITE), (0.4, OFFWHITE), (0.3, YELLOW), (0.3, GREY)])
    for k, (r, tint) in enumerate(((0.45, OFFWHITE), (0.4, OFFWHITE), (0.3, YELLOW), (0.3, GREY))):
        y = 102.6 + k * 0.93
        z = 6.2 + r
        b.tube([(118.0, y, z), (113.0, y, z), (96.0, y, H0 + 0.6 + r), (TOWER[0] + TW / 2 + 1.2, y, H0 + 0.6 + r)],
               r, mat=PAINT, tint=tint, sides=12)
        for x in range(42, 94, 8):
            b.box((0.3, 1.0, 0.6), (x, y, H0 + 0.3), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    b.zone("radiation", (225.0, 110.0, 10.0), radius=55.0, level=1, source="propellant")


def blockhouse(b):
    """The blockhouse: an armoured concrete block under a shallow dome, bermed on three sides, a baffled blast door
    on the south, the control room's slits facing the pad, consoles and a store inside."""
    bx, by = -170.0, 150.0
    w, d, h = 34.0, 26.0, 4.6
    t = 1.6
    P.room(b, (bx, by, 0), (0, 0, 0), w, d, h, thick=t, mat=FORMWORK, tint=P.BW_CONCRETE, roof_thick=2.4,
           openings={"front": [(-8.0, 2.4, 2.6, 0.0)], "right": [(-6.0, 3.0, 0.4, 2.0), (0.0, 3.0, 0.4, 2.0),
                                                                  (6.0, 3.0, 0.4, 2.0)]})
    with b.frame((bx, by, h + 2.4), (0, 0, 0), (1.0, 0.76, 1.0)):
        b.lathe([(17.0, 0.0), (15.6, 2.0), (11.8, 4.0), (6.0, 5.2), (0.0, 5.5)], (0, 0, 0), mat=CONCRETE,
                tint=P.BW_CONCRETE, sides=36, solid="hull")
    for x in (bx - 6.0, bx + 6.0):
        b.cyl(0.25, 4.0, (x, by + 2.0, h + 2.4 + 6.0), mat=PAINT, tint=DARK, sides=8)
        b.box((0.8, 0.4, 0.5), (x, by + 1.8, h + 2.4 + 8.0), mat=PAINT, tint=DARK, solid=False)
    b.cyl(0.08, 12.0, (bx + 10.0, by + 6.0, h + 2.4 + 6.0), mat=PAINT, tint=GREY, sides=6, solid=False)
    # Berms on the north, west and east (earth), leaving the slits and the door clear.
    P.block(b, bx - w / 2 - 0.2, bx + w / 2 + 0.2, by + d / 2, by + d / 2 + 0.2, h + 1.0, {"n": 12.0}, mat=MATTE,
            tint=P.EARTH)
    P.block(b, bx - w / 2 - 0.2, bx - w / 2, by - d / 2, by + d / 2, h + 1.0, {"w": 12.0, "s": 6.0, "n": 6.0},
            mat=MATTE, tint=P.EARTH)
    for k in range(3):
        y = by - 6.0 + 6.0 * k
        b.box((0.6, 3.2, 0.1), (bx + w / 2 + 0.31, y, 2.2), mat=GLASS, solid=False, vary=0)
        b.box((1.6, 3.6, 0.2), (bx + w / 2 + 0.8, y, 2.65), mat=CONCRETE, tint=P.BW_CONCRETE, solid=False)
    # The baffle in front of the door, the blast door.
    P.blast_door(b, (bx - 8.0, by - d / 2 - 0.7, 0), (0, 0, 0), w=2.4, h=2.6, depth=1.4, frame=0.9, open=0.35,
                 tint=P.OLIVE_DARK, kind="blast-door")
    b.box((12.0, 1.2, 3.6), (bx - 8.0, by - d / 2 - 6.0, 1.8), mat=FORMWORK, tint=P.BW_CONCRETE)
    b.box((1.2, 5.0, 3.6), (bx - 14.4, by - d / 2 - 3.5, 1.8), mat=FORMWORK, tint=P.BW_CONCRETE)
    stripes(b, (bx - 14.0, by - d / 2 - 6.62, 0), (bx - 2.0, by - d / 2 - 6.62, 0), 0.5, width=0.4, at_z=3.0)
    P.stencil(b, "BH1", (bx - 13.0, by - d / 2 - 6.62, 0.8), (0, 0, 0), 1.6, tint=BLACK)
    # Inside: a corridor along the south wall, the control room on the east, a store on the west.
    P.wall_run(b, (bx - w / 2 + t, by - d / 2 + t + 3.0), (bx + w / 2 - t, by - d / 2 + t + 3.0), h, 0.3,
               [(4.0, 1.2, 2.3, 0.0), (22.0, 1.2, 2.3, 0.0)], mat=CONCRETE, tint=P.BW_CONCRETE)
    P.wall_run(b, (bx - 2.0, by - d / 2 + t + 3.15), (bx - 2.0, by + d / 2 - t), h, 0.3, mat=CONCRETE,
               tint=P.BW_CONCRETE)
    for row in range(3):
        for k in range(4):
            x = bx + 6.0 + row * 3.0
            y = by - 4.5 + k * 3.0
            b.box((0.9, 2.4, 1.0), (x, y, 0.5), mat=PAINT, tint=P.BW_GREY, bevel=0.03)
            b.box((0.5, 2.3, 0.6), (x + 0.4, y, 1.25), (0, -25, 0), mat=PAINT, tint=DARK, solid=False)
    b.marker("loot", (bx - 12.0, by + 4.0, 0.1), table="military-crate")
    b.marker("loot", (bx - 6.0, by + 8.0, 0.1), table="military-crate")
    b.marker("loot", (bx + 12.0, by + 6.0, 0.1), table="crate")
    P.crates(b, (bx - 10.0, by + 8.0, 0.06), (0, 0, 0), n=5, tint=P.OLIVE_DARK)
    lamp(b, (bx + 4.0, by, h - 0.2), (90, 0, 0), colour=srgb(1.0, 0.25, 0.15), size=(0.6, 0.3, 0.2))
    lamp(b, (bx - 9.0, by + 2.0, h - 0.2), (90, 0, 0), colour=srgb(1.0, 0.25, 0.15), size=(0.6, 0.3, 0.2))
    b.marker("guard", (bx - 4.0, by - d / 2 - 9.0, 0), role="reclaimer")
    b.marker("guard", (bx + 9.0, by + 2.0, 0.1), role="reclaimer-heavy")


def guard_tower(b, at, yaw=0.0):
    """A Bulwark guard tower: a concrete shaft with a ladder inside a cage, a steel cab with slits, a Warden turret
    mount on its roof."""
    with b.frame(at, (0, 0, yaw)):
        b.box((3.4, 3.4, 9.0), (0, 0, 4.5), mat=FORMWORK, tint=P.BW_CONCRETE, bevel=0.05)
        b.box((5.0, 6.4, 0.4), (0, 0.7, 9.2), mat=CONCRETE, tint=P.BW_CONCRETE)
        P.room(b, (0, 0, 9.4), (0, 0, 0), 4.6, 4.6, 2.6, thick=0.2, mat=PAINT, tint=P.OLIVE_DARK, floor=False,
               openings={"front": [(0.0, 3.4, 0.5, 1.2)], "left": [(0.0, 3.4, 0.5, 1.2)],
                         "right": [(0.0, 3.4, 0.5, 1.2)], "back": [(-1.2, 1.0, 2.1, 0.0)]})
        b.box((1.4, 1.4, 0.8), (0, 0, 12.6), mat=PAINT, tint=DARK, bevel=0.05)
        ladder(b, (1.2, 4.0, 0), 9.4, (0, 0, 180), cage=True, tint=GREY)
        lamp(b, (2.4, -2.4, 11.9), (25, 0, 45), size=(0.5, 0.3, 0.3))
        b.marker("guard", (0, 0, 13.1), role="warden-turret")


def lightning_mast(b, at, height):
    """A tapered tubular lightning mast on a concrete footing with a fibreglass tip and a beacon."""
    x, y, z = at
    b.box((4.0, 4.0, 1.2), (x, y, z + 0.6), mat=CONCRETE, tint=CONCRETE_T)
    b.cyl(1.1, height * 0.9, (x, y, z + height * 0.45), mat=PAINT, tint=P.BW_GREY, sides=16, radius2=0.45)
    for k in range(1, 6):
        zz = z + height * 0.9 * k / 6
        b.cyl(1.15 - 0.65 * k / 6, 0.3, (x, y, zz), mat=PAINT, tint=DARK, sides=16, solid=False, vary=0)
    b.cyl(0.2, height * 0.1, (x, y, z + height * 0.95), mat=GLOSS, tint=srgb(0.9, 0.85, 0.6), sides=8,
          radius2=0.05, solid=False)
    P.beacon(b, (x, y, z + height * 0.9), 0.5)
    b.marker("landmark", (x, y, z + height), kind_of="lightning-mast")
    return (x, y, z + height)


def water_tower(b, at):
    """The deluge water tower: a concrete column 55 m tall with a spherical tank, a spiral of rungs, the outlet main
    toward the pad."""
    x, y, z = at
    b.cyl(3.0, 56.0, (x, y, z + 28.0), mat=FORMWORK, tint=P.BW_CONCRETE, sides=24, radius2=2.2)
    b.cyl(5.0, 1.0, (x, y, z + 0.5), mat=CONCRETE, tint=CONCRETE_T, sides=24)
    prof = [(2.0, -8.2)]
    for k in range(1, 12):
        t = math.pi * k / 12
        prof.append((9.0 * math.sin(t), -9.0 * math.cos(t)))
    prof.append((0.0, 9.0))
    b.lathe(prof, (x, y, z + 64.0), mat=PAINT, tint=OFFWHITE, sides=36)
    b.cyl(9.04, 2.5, (x, y, z + 66.0), mat=PAINT, tint=P.OLIVE_DARK, sides=36, radius2=8.6, solid=False, vary=0)
    P.stencil(b, "W1", (x - 3.0, y - 9.3, z + 60.0), (0, 0, 0), 4.0, tint=BLACK)
    b.cyl(9.6, 0.25, (x, y, z + 64.0), mat=GRATE, tint=GREY, sides=36, solid=False)
    pts = [(x + math.cos(a) * 10.0, y + math.sin(a) * 10.0, z + 64.0) for a in
           [2 * math.pi * k / 24 for k in range(25)]]
    for p, q in zip(pts, pts[1:]):
        railing(b, p, q, 1.0, spacing=3.0, toe=False)
    ladder(b, (x, y - 2.9, z), 56.0, (0, 0, 0), cage=True, tint=GREY)
    P.beacon(b, (x, y, z + 73.0), 0.5)
    b.marker("landmark", (x, y, z + 73.0), kind_of="water-tower")
    b.tube([(x - 3.5, y, z + 1.0), (x - 30.0, y, z + 1.0), (x - 30.0, y + 45.0, z + 1.0)], 0.9, mat=PAINT,
           tint=OFFWHITE, sides=16)
    for k in range(8):
        b.box((1.0, 2.4, 0.6), (x - 6.0 - k * 3.2, y, z + 0.3), mat=CONCRETE, tint=CONCRETE_T, solid=False)


def perimeter(b):
    """The perimeter: Bulwark T-walls round 700 x 500 m with breaches, a gate on the south with a booth, barrier
    and sandbags, guard towers at the corners, the inner road."""
    X, Y = 345.0, 245.0
    gate = 120.0
    pts = [(gate + 9.0, -Y), (X, -Y), (X, Y), (-X, Y), (-X, -Y), (gate - 9.0, -Y)]
    for a, c in zip(pts, pts[1:]):
        wall(b, (a[0], a[1], 0), (c[0], c[1], 0), height=4.0, thick=0.5, panel=6.0, broken=0.14,
             mat=FORMWORK, tint=P.BW_CONCRETE)
    for x, y, yaw in ((-X + 6, -Y + 6, 45), (X - 6, -Y + 6, -45), (X - 6, Y - 6, -135), (-X + 6, Y - 6, 135)):
        guard_tower(b, (x, y, 0), yaw)
    # Gate: posts, a raised barrier arm, a booth, sandbag chicane, jersey barriers.
    for x in (gate - 9.0, gate + 9.0):
        b.box((1.6, 1.6, 5.0), (x, -Y, 2.5), mat=FORMWORK, tint=P.BW_CONCRETE, bevel=0.05)
    b.box((0.4, 0.4, 1.2), (gate - 7.8, -Y - 1.0, 0.6), mat=PAINT, tint=DARK)
    b.box((0.25, 0.25, 9.0), (gate - 7.8, -Y - 1.0, 5.5), (0, 62, 0), mat=PAINT, tint=RED, solid=False)
    P.room(b, (gate + 14.0, -Y + 6.0, 0), (0, 0, 0), 4.0, 4.0, 2.8, thick=0.25, mat=PAINT, tint=P.OLIVE_DARK,
           openings={"front": [(0.0, 2.8, 1.0, 1.0)], "left": [(0.5, 1.0, 2.2, 0.0)]})
    b.marker("loot", (gate + 14.5, -Y + 6.5, 0.1), table="military-crate")
    for k, (x, y, yaw) in enumerate(((gate - 4.0, -Y - 12.0, 8), (gate + 3.0, -Y - 20.0, -6),
                                     (gate - 2.0, -Y + 14.0, 3))):
        b.piece("jersey-barrier", (x, y, 0), (0, 0, yaw))
        b.piece("jersey-barrier", (x + 3.1, y + 0.2, 0), (0, 0, yaw + 4))
    from monkit.structures import sandbags
    sandbags(b, (gate + 10.0, -Y + 12.0, 0), (gate + 16.0, -Y + 12.0, 0), height=3)
    sandbags(b, (gate + 16.0, -Y + 12.0, 0), (gate + 16.0, -Y + 17.0, 0), height=3)
    guard_tower(b, (gate + 22.0, -Y + 14.0, 0), 0.0)
    # Roads: from the gate north to the pad ramp and west to the hall, a ring road east to the farm.
    road(b, [(gate, -Y - 40.0, 0), (gate, -150.0, 0), (60.0, -95.0, 0)], width=9.0)
    road(b, [(gate, -150.0, 0), (gate + 45.0, -60.0, 0), (165.0, 40.0, 0), (165.0, 165.0, 0)], width=8.0)
    road(b, [(165.0, 165.0, 0), (-60.0, 190.0, 0), (-150.0, 120.0, 0)], width=8.0)
    road(b, [(-140.0, 128.0, 0), (-260.0, 40.0, 0), (-260.0, -60.0, 0)], width=8.0)
    b.marker("spawn", (gate, -Y - 30.0, 0), kind_of="approach")


def crawlerway(b):
    """The crawlerway from the hall's tall door to the ramp: two gravel lanes on a raised bed, a median."""
    pts = [(-128.0, -120.0), (40.0, -120.0), (40.0, -76.0)]
    for (ax, ay), (cx, cy) in zip(pts, pts[1:]):
        L = math.hypot(cx - ax, cy - ay)
        yaw = math.degrees(math.atan2(cy - ay, cx - ax))
        mx, my = (ax + cx) / 2, (ay + cy) / 2
        b.box((L + 30.0, 36.0, 0.5), (mx, my, 0.25), (0, 0, yaw), mat=CONCRETE, tint=srgb(0.52, 0.50, 0.46),
              wear=0.8)
        for s in (-1, 1):
            ox, oy = -math.sin(math.radians(yaw)) * s * 11.0, math.cos(math.radians(yaw)) * s * 11.0
            b.box((L + 26.0, 11.0, 0.12), (mx + ox, my + oy, 0.56), (0, 0, yaw), mat=ASPHALT,
                  tint=srgb(0.95, 0.92, 0.85), solid=False)


def build(b):
    b.set_pad(("rect", 700.0, 500.0), ground="gravel", blend=40.0, clear=15.0)
    hardstand(b)
    tower(b)
    rocket(b)
    assembly_hall(b)
    crawlerway(b)
    crawler(b, (-40.0, -120.0, 0.5), 0.0)
    propellant_farm(b)
    blockhouse(b)
    perimeter(b)
    # Lightning masts on the hardstand corners, wired to the tower's rod; the water tower; flood masts.
    tops = [lightning_mast(b, (88.0, 128.0, H0), 104.0), lightning_mast(b, (88.0, 52.0, H0), 104.0)]
    rod = (TOWER[0] + 6.6, TOWER[1] + 6.6, LEVELS[-1] + 10.0)
    for t in tops:
        cable(b, t, rod, sag=0.035, radius=0.05)
    cable(b, tops[0], tops[1], sag=0.04, radius=0.05)
    water_tower(b, (135.0, 0.0, 0.0))
    for x, y, yaw in ((-30.0, 30.0, 135), (115.0, 30.0, 225), (115.0, 150.0, -45), (-30.0, 150.0, 45),
                      (-110.0, -80.0, 0), (60.0, -160.0, 180)):
        b.piece("flood-mast", (x, y, 0), (0, 0, yaw))
    # Concrete aprons under the works.
    for (x, y, w, d) in ((-190.0, -120.0, 150.0, 104.0), (225.0, 100.0, 112.0, 104.0), (-170.0, 140.0, 60.0, 50.0),
                         (230.0, -185.0, 70.0, 50.0), (-40.0, -200.0, 64.0, 46.0), (60.0, -192.0, 44.0, 50.0)):
        b.box((w, d, 0.25), (x, y, 0.125), mat=CONCRETE, tint=srgb(0.60, 0.59, 0.56), wear=0.6)
    from monkit.kit import fence
    fence(b, [(176.0, 52.0, 0), (282.0, 52.0, 0), (282.0, 152.0, 0), (176.0, 152.0, 0), (176.0, 112.0, 0)], height=2.4,
          post=4.0)
    fence(b, [(176.0, 98.0, 0), (176.0, 52.0, 0)], height=2.4, post=4.0)
    for y in (98.0, 112.0):
        b.box((0.3, 0.3, 2.8), (176.0, y, 1.4), tint=GREY, bevel=0.02)
    # Payload processing hall by the crawlerway.
    P.hall(b, (-40.0, -200.0, 0), (0, 0, 0), 44.0, 30.0, 18.0, bay=7.3, plinth=3.0, clad_tint=srgb(0.72, 0.73, 0.70),
           roof_tint=P.BW_GREY, frame_tint=P.OLIVE_DARK, band=OLIVE, ruin=0.15, panel=2.9, windows=("front",),
           doors={"back": [(6.0, 10.0, 12.0, "shutter")], "front": [(-12.0, 1.4, 2.3, "door")],
                  "left": [(0.0, 6.0, 5.0, "shutter")]})
    P.stencil(b, "P2", (-36.0, -215.42, 9.5), (0, 0, 0), 5.0, tint=P.OLIVE_DARK)
    b.marker("loot", (-50.0, -205.0, 0.4), table="crate")
    b.marker("loot", (-30.0, -192.0, 0.4), table="military-crate")
    b.marker("guard", (-40.0, -198.0, 0.4), role="reclaimer")
    P.crates(b, (-28.0, -206.0, 0.3), (0, 0, 5), n=6, tint=P.BW_GREY)
    # The complex's substation in the north-west corner, and poles carrying its line to the hall.
    for x in (-300.0, -288.0):
        b.piece("transformer", (x, 205.0, 0), (0, 0, 0))
    for x in (-302.0, -294.0, -286.0):
        b.piece("breaker", (x, 192.0, 0), (0, 0, 0))
    b.piece("gantry", (-294.0, 216.0, 0), (0, 0, 0))
    b.box((40.0, 40.0, 0.1), (-294.0, 202.0, 0.05), mat=CONCRETE, tint=srgb(0.55, 0.54, 0.5), wear=0.6)
    fence(b, [(-314.0, 182.0, 0), (-274.0, 182.0, 0), (-274.0, 222.0, 0), (-314.0, 222.0, 0), (-314.0, 186.0, 0)],
          height=2.4,
          post=4.0)
    poles = [(-270.0, 180.0), (-265.0, 120.0), (-262.0, 60.0), (-250.0, -5.0), (-245.0, -65.0)]
    prev = None
    for x, y in poles:
        b.cyl(0.18, 11.0, (x, y, 5.5), mat=CONCRETE, tint=CONCRETE_T, sides=8, radius2=0.13)
        b.box((2.6, 0.16, 0.16), (x, y, 10.4), mat=PAINT, tint=DARK, solid=False)
        tips = [(x - 1.1, y, 10.6), (x + 1.1, y, 10.6)]
        if prev:
            for pa, pb in zip(prev, tips):
                cable(b, pa, pb, sag=0.03, radius=0.02)
        prev = tips
    # A tracking dish by the west road.
    dish(b, (-275.0, 10.0, 0), diameter=18.0, elevation=32.0, azimuth=60.0, tint=OFFWHITE, panels_missing=0.12)
    P.room(b, (-292.0, -22.0, 0), (0, 0, 0), 12.0, 8.0, 4.0, mat=FORMWORK, tint=P.BW_CONCRETE,
           openings={"front": [(2.0, 1.2, 2.3, 0.0), (-3.0, 2.0, 1.0, 1.2)]})
    b.marker("door", (-290.0, -26.6, 0), kind_of="building")
    b.marker("loot", (-294.0, -21.0, 0.1), table="crate")
    # Barracks and a motor pool by the gate.
    block_building(b, (60.0, -205.0, 0), (0, 0, 0), 30.0, 10.0, 3.6, broken=0.5, mat=FORMWORK, tint=P.BW_CONCRETE)
    block_building(b, (60.0, -180.0, 0), (0, 0, 0), 30.0, 10.0, 3.6, broken=0.6, mat=FORMWORK, tint=P.BW_CONCRETE)
    P.hall(b, (230.0, -180.0, 0), (0, 0, 0), 48.0, 24.0, 7.0, bay=8.0, plinth=1.2, roof="gable", pitch=10.0,
           clad_tint=srgb(0.45, 0.48, 0.38), roof_tint=srgb(0.40, 0.42, 0.36), frame_tint=P.OLIVE_DARK,
           ruin=0.25, windows=(), skylights=False, doors={"front": [(-14.0, 7.0, 5.0, "shutter"),
                                                                     (0.0, 7.0, 5.0, "open"),
                                                                     (14.0, 7.0, 5.0, "shutter")]})
    for k, (x, y, yaw) in enumerate(((216.0, -205.0, 10), (232.0, -210.0, -80), (250.0, -200.0, 95),
                                     (190.0, -150.0, 30))):
        b.piece("wreck-truck", (x, y, 0), (0, 0, yaw))
    for x, y, yaw, tint in ((150.0, -120.0, 15, srgb(0.42, 0.46, 0.34)), (156.0, -108.0, 18, srgb(0.6, 0.6, 0.58)),
                            (-110.0, 60.0, 80, srgb(0.42, 0.46, 0.34)), (290.0, 0.0, 0, srgb(0.55, 0.30, 0.2))):
        b.piece("container-40", (x, y, 0), (0, 0, yaw), tint=tint)
    b.marker("loot", (152.0, -114.0, 0.1), table="crate")
    b.marker("loot", (-108.0, 64.0, 0.1), table="barrel")
    # Debris round the pad and the hall, a burnt-out vehicle.
    P.scrap(b, (110.0, 20.0), 18.0, count=14)
    P.scrap(b, (-60.0, 60.0), 14.0, count=10)
    rubble(b, (-120.0, -60.0, 0), 8.0, count=10, tint=P.BW_CONCRETE)
    b.piece("wreck-car", (-95.0, -150.0, 0), (0, 0, 25))
    b.piece("wreck-car", (90.0, -160.0, 0), (0, 0, -70))
    for p in ((70.0, -60.0), (140.0, 60.0), (-90.0, 140.0), (230.0, -120.0), (0.0, 200.0)):
        b.marker("loot", (p[0], p[1], 0.1), table="barrel")
    # The Sentinel Crawler's round: the crawlerway, the ramp foot, the farm, the north road, the blockhouse, back.
    route = [(-120.0, -120.0), (0.0, -120.0), (60.0, -95.0), (150.0, -90.0), (165.0, 40.0), (165.0, 165.0),
             (60.0, 175.0), (-60.0, 190.0), (-150.0, 120.0), (-260.0, 40.0), (-260.0, -60.0), (-200.0, -170.0)]
    for i, (x, y) in enumerate(route):
        b.marker("patrol", (x, y, 0.5), role="sentinel-crawler", route="crawler", index=i)
    b.zone("radiation", (ROCKET[0] - 10.0, ROCKET[1], 70.0), size=(180.0, 160.0, 150.0), level=3, source="propellant")
    b.zone("no-build", (0, 0, 0), size=(780.0, 580.0, 300.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "plains", "visible_km": 18})


SUN = (24.0, 210.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((70.0, 57.0, H0 + 0.1), 215.0), ((49.0, 73.0, H0 + 0.1), 180.0)]
SHIP = ((82.0, 72.0, H0 + 0.1), 230.0)
VIEWS = [
    {"name": "far", "target": (20.0, 40.0, 40.0), "distance": 2600, "yaw": 32, "pitch": -0.3, "lens": 100},
    {"name": "overview", "target": (10.0, 10.0, 15.0), "distance": 780, "yaw": 30, "pitch": 27, "lens": 30},
    {"name": "pad", "target": (50.0, 88.0, 40.0), "distance": 57, "yaw": 36, "pitch": -27, "lens": 14},
]
