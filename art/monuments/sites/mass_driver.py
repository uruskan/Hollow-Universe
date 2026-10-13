"""Mass Driver (MONUMENTS.md §5.5, landmark tier): an electromagnetic launch rail that climbs a mountain for
eight kilometres to throw cargo into orbit. At its foot, a complex 760 x 490 m: the breech house where the rail
begins, four capacitor halls with capacitor yards between them, busways out along the rail, the fusion plant and
its cooling towers, a switchyard, the launch control tower. The rail leaves the breech almost flat and rises on
ever taller portal pylons; a cradle stands on it where it stopped. This site lays the first 1.5 km from the kit
pieces `driver-rail`, `driver-pylon` and `driver-pylon-section`; the game lays the rest up the mountain the same
way.

Look-dev only (never exported): the mountain, and the rest of the line to the summit laid from the same pieces,
so the far view shows the whole silhouette.
"""
import math

from mathutils import Vector

from monkit import kit
from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW, cable,
                        fence, lattice_tower, srgb, stripes, tank)
from monkit.structures import chimney, cooling_tower, road
from monkit.surfaces import ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "mass-driver"
TITLE = "Mass Driver"
SEED = 5201
BUILDER = {"chunk": 160.0, "max_edge": 14.0, "lod_min": (0.0, 2.0, 14.0)}
LODS = [450.0, 2800.0, 80000.0]

Y0 = -220.0             # where the rail begins, inside the breech house
LINE = 6400.0           # the whole line's run (to the muzzle above the summit)
BUILT = 1500.0          # how much of it this site lays
CONC = srgb(0.72, 0.71, 0.68)


def rail_z(s):
    """Height of the track's top at run s (metres north of its start)."""
    return 8.0 + 0.03 * s + 1400.0 * (max(0.0, s) / LINE) ** 1.9


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def mountain(x, y, seed=23):
    """The preview mountain the line climbs: a long spur rising to a summit about 1300 m up, rough and ridged."""
    t = (y - 300.0) / 5800.0
    ridge = 1300.0 * smooth(t) ** 1.5 * (1.0 if t <= 1.0 else max(0.0, 1.0 - (t - 1.0) * 3.0))
    spread = 1400.0 + 0.25 * max(0.0, y)
    fall = 1.0 - smooth(abs(x + 120.0 * math.sin(y / 900.0)) / spread)
    n = (P.fbm(x / 400.0, y / 400.0, 0.5, seed, 4) - 0.5) * 220.0 * smooth(t * 3.0)
    side = 260.0 * smooth((y + 400.0) / 2500.0) * (1.0 - smooth(abs(abs(x) - 1900.0) / 900.0))
    z = ridge * fall + n * fall + side * smooth(t * 2.0)
    return max(0.0, z) * smooth((7600.0 - y) / 900.0)


def line_points(s_end, step=P.DRIVER_SEGMENT):
    """Joints along the profile, one rail piece apart: [(s, z)]."""
    pts = [(0.0, rail_z(0.0))]
    while pts[-1][0] < s_end - 1.0:
        s0, z0 = pts[-1]
        lo, hi = s0, s0 + step
        for _ in range(40):
            mid = (lo + hi) / 2
            if math.hypot(mid - s0, rail_z(mid) - z0) < step:
                lo = mid
            else:
                hi = mid
        pts.append((lo, rail_z(lo)))
    return pts


def lay_line(b, pts, ground=lambda x, y: 0.0, first_pylon=1):
    """Rail pieces between joints, a pylon head under every joint and sections stacked down to the ground."""
    for i in range(len(pts) - 1):
        (s0, z0), (s1, z1) = pts[i], pts[i + 1]
        pitch = math.degrees(math.atan2(z1 - z0, s1 - s0))
        y = Y0 + (s0 + s1) / 2
        b.piece("driver-rail", (0.0, y, (z0 + z1) / 2), (0, -pitch, 90))
    for i in range(first_pylon, len(pts)):
        s, z = pts[i]
        y = Y0 + s
        head = z - P.DRIVER_DEPTH
        g = ground(0.0, y)
        if head - g < P.DRIVER_PYLON * 0.5:
            # Near the breech the girder rides on plinths.
            if head - g > 0.3:
                b.box((16.0, 8.0, head - g), (0.0, y, g + (head - g) / 2), mat=FORMWORK, tint=CONC)
            continue
        b.piece("driver-pylon", (0.0, y, head), (0, 0, 0))
        zz = head - P.DRIVER_PYLON
        while zz > g:
            b.piece("driver-pylon-section", (0.0, y, zz), (0, 0, 0))
            zz -= P.DRIVER_SECTION
        b.box((26.0, 10.0, 3.0), (0.0, y, g + 1.0), mat=CONCRETE, tint=CONC, solid=False)


# ------------------------------------------------------------------------------------------------ the complex


def breech(b):
    """The breech house where the rail begins, the portal it leaves by, the loading bay."""
    P.hall(b, (0, -235.0, 0), 0.0, 80.0, 130.0, 36.0, tint=OFFWHITE, band=ORANGE, band2=TEAL, mat=PANEL, ruin=0.25,
           pilaster=10.0, doors=[("front", -25.0), ("left", -30.0), ("right", 30.0)],
           bay_doors=[("front", 12.0, 16.0, 12.0)], roof_units=True, stair=(41.0, -40.0, 0))
    with b.frame((0, -169.5, 0), (0, 0, 0)):
        for s in (-1, 1):
            b.box((6.0, 4.0, 30.0), (s * 13.0, 0, 15.0), mat=FORMWORK, tint=CONC)
            stripes(b, (s * 13.0 - 3.0, 2.05, 0), (s * 13.0 + 3.0, 2.05, 0), 6.0, width=1.0, at_z=1.0,
                    normal=(0, 1, 0))
        b.box((32.0, 4.0, 6.0), (0, 0, 27.0), mat=FORMWORK, tint=CONC)
        b.box((20.0, 1.0, 18.0), (0, -0.6, 13.0), mat=MATTE, tint=P.SOOT, solid=False, vary=0)
        P.sign_board(b, (0, 2.2, 31.0), 180.0, 18.0, 3.0, posts=False, rows=1)
        P.aviation_lamp(b, (0, 0, 31.0), 0.8)
    b.marker("door", (-25.0, -301.0, 0), kind_of="breech")
    b.marker("loot", (0, -240.0, 0.3), table="military-crate")


def capacitor_halls(b):
    """Four capacitor halls along the rail's start, capacitor yards between and beside them, busways to the rail."""
    for k, x in enumerate((-150.0, -75.0, 75.0, 150.0)):
        P.hall(b, (x, -150.0, 0), 0.0, 36.0, 220.0, 22.0, tint=(OFFWHITE, srgb(0.78, 0.8, 0.79))[k % 2],
               band=TEAL if k % 2 else ORANGE, band2=ORANGE if k % 2 else TEAL, mat=PANEL, ruin=0.2 + 0.1 * (k % 3),
               pilaster=9.0, louvres=6, doors=[("front", 0.0), ("left", 60.0), ("right", -60.0)],
               bay_doors=[("left" if x > 0 else "right", 0.0, 10.0, 8.0)])
        for q in range(6):
            yy = -240.0 + q * 36.0
            b.box((38.0, 3.0, 2.4), (x, yy, 23.4), mat=PAINT, tint=DARK, solid=False)
        b.marker("loot", (x, -150.0 + 40.0 * (k % 2 * 2 - 1), 0.3), table="crate")
    for x in (-112.5, 112.5, -205.0, 205.0):
        for j in range(15):
            y = -250.0 + j * 14.0
            for i in (-1, 1) if abs(x) < 150 else (0,):
                b.piece("capacitor-bank", (x + i * 8.0, y, 0.0), (0, 0, 90))
        for e in (-1, 1):
            b.cyl(0.5, 210.0, (x + e * 15.0 if abs(x) < 150 else x + e * 9.0, -152.0, 8.0), (90, 0, 0), mat=PAINT,
                  tint=srgb(0.7, 0.42, 0.2), sides=8, solid=False)
        for j in range(8):
            y = -250.0 + j * 28.0
            for e in (-1, 1):
                b.box((0.4, 0.4, 8.0), (x + e * (15.0 if abs(x) < 150 else 9.0), y, 4.0), mat=PAINT, tint=DARK,
                      solid=False)
    # Busways: two heavy conductor bundles out of the breech along the rail to where it lifts off.
    for e in (-1, 1):
        x = e * 24.0
        P.pipe_run(b, [(x, -170.0, 5.0), (x, 60.0, 5.0), (x * 0.6, 160.0, 14.0)], 1.6, tint=srgb(0.66, 0.4, 0.2),
                   supports=14.0)
        P.pipe_run(b, [(x + e * 4.0, -170.0, 5.0), (x + e * 4.0, 60.0, 5.0)], 1.0, tint=DARK, supports=0.0)


def power(b):
    """The fusion plant west of the halls: reactor dome, turbine hall, cooling towers, a stack, the switchyard
    east."""
    P.hall(b, (-300.0, -170.0, 0), 0.0, 90.0, 60.0, 30.0, tint=OFFWHITE, band=ORANGE, band2=TEAL, ruin=0.3,
           pilaster=9.0, louvres=4, doors=[("front", 20.0), ("right", 0.0)])
    b.cyl(30.0, 36.0, (-300.0, -90.0, 18.0), mat=PANEL, tint=OFFWHITE, sides=56)
    b.lathe([(30.0, 0)] + [(30.0 * math.cos(t), 18.0 * math.sin(t)) for t in (math.pi / 2 * k / 8 for k in range(1, 8))]
            + [(0, 18.0)], (-300.0, -90.0, 36.0), mat=PANEL, tint=OFFWHITE, sides=56)
    b.cyl(30.3, 2.4, (-300.0, -90.0, 28.0), mat=PAINT, tint=TEAL, sides=56, solid=False, vary=0)
    cooling_tower(b, (-320.0, 40.0, 0), height=105.0, base=40.0, waist=25.0, top=28.0, tint=CONC)
    cooling_tower(b, (-215.0, 90.0, 0), height=105.0, base=40.0, waist=25.0, top=28.0, tint=CONC)
    chimney(b, (-250.0, -40.0, 0), 120.0, 6.0, 4.0)
    sx, sy = 280.0, -200.0
    b.box((90.0, 70.0, 0.3), (sx, sy, 0.15), mat=CONCRETE, tint=srgb(0.55, 0.54, 0.51), solid=False)
    for i in range(3):
        b.piece("transformer", (sx - 28.0 + i * 28.0, sy + 16.0, 0.3))
        for j in range(2):
            b.piece("breaker", (sx - 28.0 + i * 28.0 + (j - 0.5) * 8.0, sy - 8.0, 0.3))
    for x in (sx - 22.0, sx + 22.0):
        b.piece("gantry", (x, sy - 26.0, 0.3), (0, 0, 0))
    for k, (x, y) in enumerate(((280.0, -260.0), (280.0, -380.0), (300.0, -500.0))):
        lattice_tower(b, (x, y, 0), 32.0, 6.0, 1.8, sections=6, member=0.2, tint=srgb(0.58, 0.6, 0.61))
        b.beam((x - 6.0, y, 28.0), (x + 6.0, y, 28.0), 0.25, 0.25, mat=PAINT, tint=srgb(0.58, 0.6, 0.61))


def control(b):
    """Launch control: a tower overlooking the rail where it lifts off, a glazed cab, masts."""
    x, y = 70.0, 30.0
    b.box((22.0, 22.0, 46.0), (x, y, 23.0), mat=PANEL, tint=OFFWHITE, bevel=0.3)
    for k in range(4):
        b.box((22.4, 22.4, 1.2), (x, y, 10.0 + k * 10.0), mat=PAINT, tint=ORANGE if k % 2 else TEAL, solid=False,
              vary=0)
    b.box((30.0, 30.0, 8.0), (x, y, 50.0), mat=PANEL, tint=OFFWHITE, bevel=0.3)
    b.box((30.4, 30.4, 4.0), (x, y, 50.5), mat=GLASS, solid=False, vary=0)
    b.box((32.0, 32.0, 1.2), (x, y, 54.6), mat=PAINT, tint=DARK, solid=False)
    for dx, h in ((-8.0, 30.0), (8.0, 18.0)):
        b.cyl(0.6, h, (x + dx, y, 55.0 + h / 2), mat=PAINT, tint=GREY, sides=8, radius2=0.25)
        P.aviation_lamp(b, (x + dx, y, 55.5 + h), 0.8)
    P.door(b, (x, y - 11.05, 0), 0.0, marker="control")
    P.stair_tower(b, (x + 11.0, y - 4.0, 0), 0.0, 46.0)
    b.marker("loot", (x, y, 46.4), table="military-crate")


def cradle(b, s):
    """The cradle stopped on the rail: a sled with clamp arms round a cargo canister, its shoes on the guide
    rails."""
    z0 = rail_z(s - 12.0)
    z1 = rail_z(s + 12.0)
    pitch = math.degrees(math.atan2(z1 - z0, 24.0))
    with b.frame((0.0, Y0 + s, (z0 + z1) / 2), (pitch, 0, 0)):
        b.box((10.0, 24.0, 2.0), (0, 0, 2.6), mat=PAINT, tint=YELLOW, bevel=0.2)
        for x in (-3.2, 3.2):
            b.box((1.8, 22.0, 1.4), (x, 0, 1.6), mat=PAINT, tint=DARK)
        b.cyl(3.2, 18.0, (0, 0, 7.0), (90, 0, 0), mat=HULL, tint=OFFWHITE, sides=24)
        for e in (-1, 1):
            b.lathe([(3.2, 0.0), (2.6, 1.6), (1.2, 2.6), (0.0, 2.9)], (0, e * 9.0, 7.0), (-90 * e, 0, 0), mat=HULL,
                    tint=OFFWHITE, sides=24)
        for q in range(4):
            yy = -7.5 + q * 5.0
            for s2 in (-1, 1):
                b.beam((s2 * 4.6, yy, 3.4), (s2 * 3.0, yy, 9.6), 0.8, 0.8, mat=PAINT, tint=YELLOW)
        b.box((6.6, 1.0, 1.0), (0, -9.0, 10.6), mat=PAINT, tint=ORANGE, solid=False, vary=0)
        stripes(b, (-5.0, -12.05, 0), (5.0, -12.05, 0), 1.6, width=1.0, at_z=1.8)
    b.marker("loot", (0.0, Y0 + s, (z0 + z1) / 2 + 4.0), table="vault-crate")


def compound(b):
    """The fence round the complex, its gates, roads, lamps, haulers, containers."""
    x0, x1, y0, y1 = -380.0, 380.0, -330.0, 160.0
    pts = [(-30.0, y0, 0), (x0, y0, 0), (x0, y1, 0), (-20.0, y1, 0)]
    fence(b, pts, height=3.0, post=8.0)
    fence(b, [(20.0, y1, 0), (x1, y1, 0), (x1, y0, 0), (30.0, y0, 0)], height=3.0, post=8.0)
    road(b, [(0.0, -800.0, 0.0), (0.0, -302.0, 0.0)], width=14.0)
    road(b, [(-330.0, -310.0, 0.0), (330.0, -310.0, 0.0)], width=10.0)
    road(b, [(-240.0, -310.0, 0.0), (-240.0, 120.0, 0.0)], width=10.0)
    road(b, [(240.0, -310.0, 0.0), (240.0, 120.0, 0.0)], width=10.0)
    b.box((760.0, 490.0, 0.2), ((x0 + x1) / 2, (y0 + y1) / 2, 0.1), mat=CONCRETE, tint=srgb(0.56, 0.55, 0.52),
          solid=False)
    for k in range(12):
        b.piece("lamp-post", (-8.0 if k % 2 else 8.0, -790.0 + k * 40.0, 0.2), (0, 0, 90 if k % 2 else -90))
    for k in range(10):
        b.piece("lamp-post", (-330.0 + k * 73.0, -316.0, 0.2), (0, 0, 0))
    for k in range(6):
        b.piece("hauler-truck", (-230.0 + (k % 3) * 14.0, 80.0 - (k // 3) * 20.0, 0.2), (0, 0, b.rng.uniform(-10, 10)))
    for k in range(8):
        b.piece("container-20", (200.0 + (k % 4) * 7.0, 100.0 + (k // 4) * 3.0, 0.2), (0, 0, 90),
                tint=(ORANGE, TEAL, OFFWHITE, YELLOW)[k % 4])
    for k in range(3):
        tank(b, (330.0, -40.0 + k * 30.0, 0.2), 10.0, 14.0, tint=OFFWHITE, band=ORANGE, ladder_side=180.0)
    for k in range(4):
        P.rubble_heap(b, (b.rng.uniform(-300, 300), b.rng.uniform(80, 150), 0), b.rng.uniform(6, 12), 4.0, count=8)
    b.zone("no-build", (0, 300.0, 0), radius=1200.0)


def scenery(b, pts_built):
    """Look-dev only: the mountain, and the rest of the line laid on up to the muzzle above its summit."""
    x0, x1, y0, y1, step = -4000.0, 4000.0, 200.0, 7600.0, 100.0
    nx, ny = int((x1 - x0) / step), int((y1 - y0) / step)
    hs = [[mountain(x0 + i * step, y0 + j * step) for i in range(nx + 1)] for j in range(ny + 1)]
    groups = {"rock": ([], []), "grass": ([], []), "snow": ([], [])}
    for j in range(ny):
        for i in range(nx):
            q = [hs[j][i], hs[j][i + 1], hs[j + 1][i + 1], hs[j + 1][i]]
            if max(q) < 0.8:
                continue
            xa, ya = x0 + i * step, y0 + j * step
            zm = sum(q) / 4
            slope = max(q) - min(q)
            n = P.fbm(xa / 500, ya / 500, 1.0, 5, 2)
            kind = "snow" if zm > 1000.0 + 120.0 * n else (
                "rock" if slope > step * 0.9 or zm > 560.0 + 220.0 * n else "grass")
            vs, fs = groups[kind]
            base = len(vs)
            vs += [(xa, ya, q[0] - 0.5), (xa + step, ya, q[1] - 0.5), (xa + step, ya + step, q[2] - 0.5),
                   (xa, ya + step, q[3] - 0.5)]
            fs.append([base, base + 1, base + 2, base + 3])
    for kind, mat, tint in (("rock", CONCRETE, srgb(0.50, 0.47, 0.43)), ("grass", MATTE, srgb(0.30, 0.34, 0.20)),
                            ("snow", MATTE, srgb(0.92, 0.93, 0.95))):
        vs, fs = groups[kind]
        if fs:
            b.mesh(vs, fs, mat=mat, tint=tint, solid=False, smooth=True, wear=0.4)
    pts = line_points(LINE)
    k0 = len(pts_built) - 1
    lay_line(b, pts[k0:], ground=mountain, first_pylon=1)
    s, z = pts[-1]
    with b.frame((0.0, Y0 + s, z), (0, 0, 0)):
        for q in range(3):
            b.lathe([(12.0, -2.0), (14.0, -1.0), (14.0, 1.0), (12.0, 2.0), (11.6, 2.0), (11.6, -2.0), (12.0, -2.0)],
                    (0, -q * 8.0, 5.0), (90, 0, 0), mat=PAINT, tint=srgb(0.26, 0.27, 0.28), sides=32, close=False,
                    solid=False)
        P.aviation_lamp(b, (0, 0, 20.0), 1.6, reach=6000.0)


def build(b):
    b.set_pad(("rect", 780.0, 510.0), ground="concrete", blend=40.0, clear=10.0)
    breech(b)
    capacitor_halls(b)
    power(b)
    control(b)
    compound(b)
    pts = line_points(BUILT)
    lay_line(b, pts, first_pylon=1)
    cradle(b, 760.0)
    s, z = pts[-1]
    b.marker("landmark", (0.0, Y0 + s, z + 16.0), kind_of="mass-driver", visible_km=50)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 50,
                   "line": {"pieces": ["driver-rail", "driver-pylon", "driver-pylon-section"],
                            "segment": P.DRIVER_SEGMENT, "pylon": P.DRIVER_PYLON, "section": P.DRIVER_SECTION,
                            "end": [0.0, round(z, 2), round(-(Y0 + s), 2)], "heading": 0.0,
                            "continues_m": round(LINE - s, 1),
                            "profile": "track height 8 + 0.03 s + 1400 (s / 6400)^1.9 m at run s from the breech"}})
    if not kit.EXPORT["enabled"]:
        scenery(b, pts)


SUN = (24.0, 125.0)
FOG = 0.00008
GROUND = 90000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (0, 2700, 560), "distance": 11000, "yaw": 42, "pitch": 3.4, "lens": 55,
     "fog": 0.00005},
    {"name": "overview", "target": (0, 350, 60), "distance": 2300, "yaw": -38, "pitch": 22, "lens": 30,
     "fog": 0.00004},
    {"name": "foot", "target": (16.6, 489.0, 43.3), "distance": 200, "yaw": -15, "pitch": -12, "lens": 16,
     "fog": 0.00003, "people": [((-24.0, 315.0, 0.0), 200.0), ((-21.5, 313.0, 0.0), 150.0)],
     "ship": ((-40.0, 322.0, 0.0), 60.0)},
]
