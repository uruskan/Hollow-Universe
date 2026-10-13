"""Ice Station (MONUMENTS.md §5.4, large tier, catalogue 47): a Seedward research station on an ice sheet, 400 x 300 m,
in the civil livery against the snow (off-white modules, orange and teal bands, red for visibility). Its modules stand
on hydraulic legs with skis, raised so the wind scours the snow away beneath their chamfered skirts: the long main
module with the labs and its rounded end fairings, the accommodation block, the generator module, two domed
observatories; covered bridges join them and enclosed stair towers climb to their doors. Round them: a radar dome on
a lattice, a 60 m guyed met mast, an antenna field, a wind turbine, fuel tanks on sledges, the arched hangar with its
snowcats and a sledge train, a drill tower over the ice core shaft, a landing pad, the containers of the supply
depot, an early module half buried by its drift, flag lines, and drifts banked against everything.

Gameplay: the cyan room is the core store in the main module (cyan chip and a power cell), the violet room the
command deck in the big observatory (violet chip and a power cell); crates through the modules and in the hangar;
cold is the hazard everywhere outside (a cold zone over the whole site); Warden turrets on the main module's roof,
Reclaimers in the hangar.
"""
import math

from monkit import kit
from monkit import parts_large as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, WARM_LAMP, YELLOW, cable,
                        fence, ladder, lamp, lattice_tower, railing, srgb, stairs, stripes, tank)
from monkit.structures import dish, wind_turbine
from monkit.surfaces import (CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST)

KEY = "ice-station"
TITLE = "Ice Station"
SEED = 4701
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [300.0, 1400.0, 20000.0]

STILT = 6.0
MOD = srgb(0.92, 0.92, 0.90)
STATION_RED = srgb(0.72, 0.16, 0.12)
SNOW_T = srgb(0.95, 0.96, 0.99)
ICE_BLUE = srgb(0.80, 0.88, 0.95)
LEG = srgb(0.30, 0.32, 0.34)


def module(b, at, yaw, w, d, h=4.2, tint=MOD, band=ORANGE, openings=None, loot=(), stilt=STILT, label=None,
           stair_x=None, tower=True, roof_kit=True, glazed=0.7):
    """A station module raised on hydraulic legs with skis, built to let the wind scour the snow away beneath it: a
    panelled box with rounded end fairings and a band of windows, a chamfered skirt under the floor, the legs in
    braced pairs (jack cylinder, chromed rod, head bracket, hose, ski), roof plant behind a rail, snow on the roof,
    icicles on the eaves, and an enclosed stair tower (or open stairs) up from the snow to the door."""
    with b.frame(at, (0, 0, yaw)):
        # Legs in braced pairs.
        nx = max(2, int(round((w - 4.0) / 9.0)))
        ys = (-d / 2 + 1.8, d / 2 - 1.8)
        for i in range(nx + 1):
            x = -w / 2 + 2.0 + (w - 4.0) * i / nx
            for y in ys:
                b.box((3.4, 1.0, 0.28), (x, y, 0.24), mat=PAINT, tint=LEG)
                b.box((0.9, 1.0, 0.24), (x + 1.95, y, 0.42), (0, -28, 0), mat=PAINT, tint=LEG, solid=False)
                b.box((0.9, 1.0, 0.24), (x - 1.95, y, 0.42), (0, 28, 0), mat=PAINT, tint=LEG, solid=False)
                lo = stilt * 0.52
                b.cyl(0.42, lo, (x, y, 0.38 + lo / 2), mat=PAINT, tint=YELLOW, sides=12)
                b.cyl(0.5, 0.3, (x, y, 0.38 + lo), mat=PAINT, tint=LEG, sides=12, solid=False)
                b.cyl(0.22, stilt - lo - 1.1, (x, y, 0.38 + lo + (stilt - lo - 1.1) / 2), mat=GLOSS,
                      tint=srgb(0.70, 0.72, 0.74), sides=10)
                b.box((1.3, 1.3, 0.8), (x, y, stilt - 1.3), mat=PAINT, tint=LEG, bevel=0.04)
                b.tube([(x + 0.45, y, 0.9), (x + 0.6, y, lo * 0.6), (x + 0.5, y, stilt - 1.4)], 0.04, mat=PAINT,
                       tint=BLACK, sides=5)
            b.beam((x, ys[0], stilt * 0.42), (x, ys[1], stilt * 0.42), 0.26, mat=PAINT, tint=LEG)
            b.beam((x, ys[0], 1.0), (x, ys[1], stilt * 0.42), 0.16, mat=PAINT, tint=LEG)
            if i < nx:
                x1 = -w / 2 + 2.0 + (w - 4.0) * (i + 1) / nx
                for y in ys:
                    b.beam((x, y, 1.2), (x1, y, stilt * 0.42), 0.16, mat=PAINT, tint=LEG)
        # The skirt: a chamfered underside the length of the module, its end faces closed.
        with b.frame((0, 0, 0), (0, 0, 90)):
            b.extrude([(-d / 2 + 1.6, stilt - 1.6), (d / 2 - 1.6, stilt - 1.6), (d / 2 + 0.15, stilt - 0.06),
                       (-d / 2 - 0.15, stilt - 0.06)], w, (0, 0, 0), (0, 0, 0), mat=PANEL,
                      tint=srgb(0.55, 0.57, 0.58))
        ops = openings or {"front": [(-w / 2 + 3.0, 1.2, 2.3, 0.0)] +
                           [(x, 1.6, 1.0, 1.2) for x in [-w / 2 + 8.0 + 4.0 * k for k in range(int((w - 10) / 4))]],
                           "back": [(x, 1.6, 1.0, 1.2) for x in [-w / 2 + 6.0 + 4.0 * k for k in range(int((w - 8) / 4))]]}
        P.room(b, (0, 0, stilt), (0, 0, 0), w, d, h, thick=0.3, mat=PANEL, tint=tint, openings=ops,
               roof_mat=PANEL, roof_tint=tint, floor_tint=srgb(0.55, 0.57, 0.58))
        # Glass in most windows (the rest broken), dark frames.
        for side, sy in (("front", -d / 2 + 0.05), ("back", d / 2 - 0.05)):
            for ox, ow, oh, sill in ops.get(side, []):
                if sill <= 0.0:
                    continue
                x = ox
                if b.rng.random() < glazed:
                    b.box((ow - 0.06, 0.04, oh - 0.06), (x, sy, stilt + sill + oh / 2), mat=GLASS, solid=False,
                          vary=0)
                b.box((ow + 0.16, 0.08, 0.1), (x, sy * 1.02, stilt + sill - 0.05), mat=PAINT, tint=LEG,
                      solid=False, vary=0)
        b.box((w + 0.1, d + 0.1, 0.5), (0, 0, stilt + h * 0.66), mat=PAINT, tint=band, solid=False, vary=0)
        b.box((w + 0.1, d + 0.1, 0.12), (0, 0, stilt + h * 0.66 + 0.4), mat=PAINT, tint=TEAL if band != TEAL else
              ORANGE, solid=False, vary=0)
        # Rounded end fairings with a strip of glazing.
        r = d / 2
        for sgn in (-1, 1):
            cx = sgn * w / 2
            a0, a1 = (-90.0, 90.0) if sgn > 0 else (90.0, 270.0)
            P.arc_wall(b, (cx, 0.0), r, a0, a1, stilt - 0.06, stilt + h, thick=0.3, piece_deg=30.0, mat=PANEL,
                       tint=tint)
            P.arc_wall(b, (cx, 0.0), r + 0.03, a0 + 25.0, a1 - 25.0, stilt + 1.3, stilt + 2.3, thick=0.05,
                       piece_deg=40.0, mat=GLASS, tint=(1, 1, 1), solid=False)
            P.arc_wall(b, (cx, 0.0), r + 0.06, a0 + 2.0, a1 - 2.0, stilt + h * 0.66 - 0.25, stilt + h * 0.66 + 0.25,
                       thick=0.05, piece_deg=40.0, mat=PAINT, tint=band, solid=False)
            half = [(cx + sgn * r * math.cos(math.radians(t)), r * math.sin(math.radians(t)))
                    for t in range(-90, 91, 15)]
            if sgn < 0:
                half = half[::-1]
            b.slab(half, 0.3, (0, 0, stilt + h), mat=PANEL, tint=tint)
            b.slab(half, 0.12, (0, 0, stilt - 0.12), mat=PANEL, tint=srgb(0.55, 0.57, 0.58))
            b.slab([(x, y) for x, y in half], 0.35, (0, 0, stilt + h + 0.3), mat=MATTE, tint=SNOW_T, solid=False)
        # Roof: a rail, plant boxes, vents, an antenna, snow, icicles.
        b.box((w - 0.2, d - 0.4, 0.35), (0, 0.2, stilt + h + 0.47), mat=MATTE, tint=SNOW_T, solid=False, vary=0.02)
        if roof_kit:
            for k in range(max(1, int(w / 14.0))):
                x = -w / 2 + 7.0 + k * 14.0
                b.box((3.0, 2.2, 1.4), (x, d / 2 - 2.6, stilt + h + 0.3 + 0.7), mat=PAINT, tint=LIGHTGREY,
                      bevel=0.05)
                b.cyl(0.6, 0.25, (x, d / 2 - 2.6, stilt + h + 0.3 + 1.5), mat=GRATE, tint=GREY, sides=12,
                      solid=False)
                b.cyl(0.18, 1.6, (x + 2.6, -d / 2 + 2.0, stilt + h + 0.3 + 0.8), mat=PAINT, tint=GREY, sides=8)
            b.cyl(0.06, 5.0, (w / 2 - 3.0, 0.0, stilt + h + 0.3 + 2.5), mat=PAINT, tint=GREY, sides=6, solid=False)
            P.beacon(b, (w / 2 - 3.0, 0.0, stilt + h + 0.3 + 5.0), 0.25)
        for k in range(int(w / 1.6)):
            if b.rng.random() < 0.5:
                continue
            L = 0.3 + 0.6 * b.rng.random()
            b.cyl(0.05, L, (-w / 2 + 0.8 + k * 1.6, -d / 2 - 0.12, stilt + h - L / 2), mat=GLOSS, tint=ICE_BLUE,
                  sides=5, radius2=0.005, solid=False)
        for k, (x, y, t) in enumerate(loot):
            b.marker("loot", (x, y, stilt + 0.1), table=t)
        if label:
            P.stencil(b, label, (w / 2 - 7.0, -d / 2 - 0.32, stilt + 0.5), (0, 0, 0), 1.4, tint=STATION_RED)
        # The way up: an enclosed stair tower in front of the door, entered from beneath the module.
        sx = (-w / 2 + 3.0) if stair_x is None else stair_x
        if tower:
            P.stair_tower(b, (sx, -d / 2 - 1.4, 0.0), (0, 0, 180), [0.0, stilt], width=1.1, clad=PANEL,
                          clad_tint=tint, steps=8)
            # The tower's livery band, a window slot, its sign and a snow cap.
            yb = -d / 2 - 5.47
            zb = stilt + h * 0.66
            b.box((3.4, 0.06, 0.5), (sx, yb, zb), mat=PAINT, tint=band, solid=False, vary=0)
            for sg in (-1, 1):
                b.box((0.06, 5.75, 0.5), (sx + sg * 1.69, -d / 2 - 2.6, zb), mat=PAINT, tint=band, solid=False,
                      vary=0)
            b.box((0.5, 0.05, stilt - 1.5), (sx, yb, 1.0 + (stilt - 1.5) / 2), mat=GLASS, solid=False, vary=0)
            P.plate(b, (sx + 1.0, yb - 0.03, 2.0), (0, 0, 0), 0.7, 0.5, tint=STATION_RED, mark=OFFWHITE, text="S")
            b.box((3.2, 5.4, 0.3), (sx, -d / 2 - 2.6, stilt + 2.95), mat=MATTE, tint=SNOW_T, solid=False)
            b.marker("door", (sx, -d / 2 - 0.6, stilt), kind_of="module")
        else:
            stairs(b, (sx, -d / 2 - 1.2 - stilt / 0.7, 0.0), (0, 0, 0), 1.2, stilt, stilt / 0.7, tint=YELLOW)
            P.deck(b, sx - 1.0, -d / 2 - 1.2, sx + 1.0, -d / 2, stilt, mat=GRATE, rails="we", edge=False)
            b.marker("door", (sx, -d / 2 - 0.6, stilt), kind_of="module")


def fuel_sledge(b, at, yaw, tint=STATION_RED):
    """A fuel tank on a sledge: two runners with upturned noses, a frame, a horizontal tank with domed ends, a
    hose reel and a towing bar."""
    with b.frame(at, (0, 0, yaw)):
        for y in (-1.1, 1.1):
            b.box((7.6, 0.3, 0.24), (0, y, 0.12), mat=PAINT, tint=LEG)
            b.box((0.9, 0.3, 0.22), (4.15, y, 0.32), (0, -32, 0), mat=PAINT, tint=LEG, solid=False)
            for x in (-2.8, 0.0, 2.8):
                b.box((0.2, 0.2, 0.7), (x, y, 0.55), mat=PAINT, tint=LEG, solid=False)
        b.box((7.2, 2.6, 0.2), (0, 0, 0.95), mat=PAINT, tint=LEG)
        b.cyl(1.2, 5.6, (0, 0, 2.25), (0, 90, 0), mat=PAINT, tint=tint, sides=20)
        for sg in (-1, 1):
            b.lathe([(1.2, 0.0), (1.0, 0.35), (0.55, 0.6), (0.0, 0.68)], (sg * 2.8, 0, 2.25), (0, 90 * sg, 0),
                    mat=PAINT, tint=tint, sides=20)
        b.cyl(1.22, 0.4, (1.0, 0, 2.25), (0, 90, 0), mat=PAINT, tint=OFFWHITE, sides=20, solid=False, vary=0)
        b.cyl(0.35, 0.35, (-1.5, 0, 3.55), mat=PAINT, tint=GREY, sides=10, solid=False)
        b.cyl(0.45, 0.5, (-3.4, 0.9, 1.5), (90, 0, 0), mat=PAINT, tint=DARK, sides=12, solid=False)
        b.beam((3.6, 0, 0.5), (5.6, 0, 0.45), 0.14, mat=PAINT, tint=LEG)
        b.box((7.0, 2.2, 0.25), (0, 0, 3.6), mat=MATTE, tint=SNOW_T, solid=False)


def cargo_sledge(b, at, yaw, tint=None):
    """A cargo sledge with a 20-foot container strapped on, snow on top."""
    with b.frame(at, (0, 0, yaw)):
        for y in (-1.0, 1.0):
            b.box((7.0, 0.3, 0.24), (0, y, 0.12), mat=PAINT, tint=LEG)
            b.box((0.9, 0.3, 0.22), (3.85, y, 0.32), (0, -32, 0), mat=PAINT, tint=LEG, solid=False)
        b.box((6.8, 2.6, 0.25), (0, 0, 0.6), mat=PAINT, tint=LEG)
        b.piece("container-20", (0, 0, 0.72), (0, 0, 0), tint=tint)
        b.box((5.8, 2.2, 0.25), (0, 0, 3.45), mat=MATTE, tint=SNOW_T, solid=False)
        b.beam((3.5, 0, 0.5), (5.2, 0, 0.45), 0.14, mat=PAINT, tint=LEG)


def dome(b, at, r, stilt=STILT, tint=MOD, slit=True, doors=(-90.0,), gap=-90.0):
    """An observatory dome on a round base drum, on stilts, with its slit and a ring gallery."""
    x, y, z = at
    for k in range(8):
        a = 2 * math.pi * k / 8
        b.box((0.7, 0.7, stilt), (x + math.cos(a) * r * 0.75, y + math.sin(a) * r * 0.75, stilt / 2), mat=PAINT,
              tint=srgb(0.36, 0.38, 0.40))
    b.cyl(r + 1.6, 0.4, (x, y, stilt - 0.2), mat=GRATE, tint=GREY, sides=32)
    for k in range(24):
        a0, a1 = 2 * math.pi * k / 24, 2 * math.pi * (k + 1) / 24
        mid = math.degrees((a0 + a1) / 2)
        if gap is not None and abs((mid - gap + 180.0) % 360.0 - 180.0) < 16.0:
            continue
        railing(b, (x + math.cos(a0) * (r + 1.5), y + math.sin(a0) * (r + 1.5), stilt),
                (x + math.cos(a1) * (r + 1.5), y + math.sin(a1) * (r + 1.5), stilt), spacing=2.4, toe=False,
                tint=YELLOW)
    P.drum(b, (x, y), r, stilt, stilt + 4.0, [((a + 360.0) % 360.0, 1.4 if a == -90.0 else 2.0, 2.3) for a in doors],
           thick=0.4, mat=PANEL, tint=tint)
    b.cyl(r + 0.03, 0.5, (x, y, stilt + 3.0), mat=PAINT, tint=TEAL, sides=32, cap=False, solid=False, vary=0)
    prof = [(r + 0.2, 0.0)]
    for k in range(1, 9):
        t = (math.pi / 2) * k / 8
        prof.append(((r + 0.2) * math.cos(t), (r + 0.2) * math.sin(t)))
    b.lathe(prof, (x, y, stilt + 4.0), mat=PANEL, tint=srgb(0.86, 0.86, 0.84), sides=32)
    if slit:
        b.box((1.6, r * 0.9, 0.12), (x, y - r * 0.45 + 0.2, stilt + 4.0 + r * 0.82), (-35.0, 0, 0), mat=MATTE,
              tint=srgb(0.08, 0.08, 0.09), solid=False)
    b.cyl(r * 0.85, 0.3, (x, y, stilt + 4.0 + r * 0.5 + 0.2), mat=MATTE, tint=SNOW_T, sides=24, radius2=r * 0.5,
          solid=False)
    b.marker("landmark", (x, y, stilt + 4.0 + r), kind_of="observatory")


def bridge(b, a, c, z=STILT, w=2.4, h=2.6, tint=MOD):
    """A covered bridge between modules: a corridor box on stilts with small windows."""
    ax, ay = a
    cx, cy = c
    L = math.hypot(cx - ax, cy - ay)
    yaw = math.degrees(math.atan2(cy - ay, cx - ax))
    with b.frame(((ax + cx) / 2, (ay + cy) / 2, 0.0), (0, 0, yaw)):
        b.box((L, w, 0.3), (0, 0, z + 0.15), mat=PAINT, tint=srgb(0.55, 0.57, 0.58))
        for s in (-1, 1):
            P.wall_run(b, (-L / 2, s * (w / 2 - 0.1)), (L / 2, s * (w / 2 - 0.1)), h, 0.2,
                       [(L * (k + 0.5) / max(1, int(L / 4.0)), 0.8, 0.5, 1.4) for k in range(max(1, int(L / 4.0)))],
                       z0=z + 0.3, mat=PANEL, tint=tint)
        b.box((L, w + 0.2, 0.25), (0, 0, z + 0.3 + h + 0.12), mat=PANEL, tint=tint)
        b.box((L, w - 0.2, 0.25), (0, 0, z + 0.3 + h + 0.4), mat=MATTE, tint=SNOW_T, solid=False)
        for k in range(max(1, int(L / 8.0)) + 1):
            x = -L / 2 + L * k / max(1, int(L / 8.0))
            b.box((0.4, 0.4, z), (x, 0, z / 2), mat=PAINT, tint=srgb(0.36, 0.38, 0.40))


def hangar(b, at, yaw, w=26.0, L=44.0, tint=srgb(0.80, 0.80, 0.78)):
    """An arched hangar you can walk into: a slab, the arch of corrugated segments (each its own collision box),
    ribs, a closed back wall and a front with a tall door opening and its leaf half drawn across."""
    r = w / 2
    with b.frame(at, (0, 0, yaw)):
        b.box((w + 1.0, L + 1.0, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.6, 0.6, 0.58))
        n = 14
        for i in range(n):
            a0, a1 = math.pi * i / n, math.pi * (i + 1) / n
            p0 = (r * math.cos(a0), r * math.sin(a0))
            p1 = (r * math.cos(a1), r * math.sin(a1))
            dx, dz = p1[0] - p0[0], p1[1] - p0[1]
            seg = math.hypot(dx, dz)
            th = math.degrees(math.atan2(dz, dx))
            b.box((seg + 0.05, L, 0.15), ((p0[0] + p1[0]) / 2, 0, 0.3 + (p0[1] + p1[1]) / 2), (0, -th, 0),
                  mat=CORRUGATED, tint=tint)
            if i in (5, 6, 7, 8):
                b.box((seg, L + 0.2, 0.16), ((p0[0] + p1[0]) / 2 * 1.005, 0,
                                             0.3 + (p0[1] + p1[1]) / 2 * 1.005 + 0.12),
                      (0, -th, 0), mat=MATTE, tint=SNOW_T, solid=False)
        for k in range(int(L / 4.0) + 1):
            yy = -L / 2 + L * k / int(L / 4.0)
            b.tube([(r * 1.01 * math.cos(math.pi * j / 16), yy, 0.3 + r * 1.01 * math.sin(math.pi * j / 16))
                    for j in range(17)], 0.12, mat=PAINT, tint=srgb(0.36, 0.38, 0.40), sides=6, cap=False)
        strips = 10
        for side, yy in (("back", L / 2), ("front", -L / 2)):
            for j in range(strips):
                x0, x1 = -r + w * j / strips, -r + w * (j + 1) / strips
                xm = (x0 + x1) / 2
                hgt = math.sqrt(max(0.0, r * r - (max(abs(x0), abs(x1))) ** 2)) + 0.5
                lo = 0.0
                if side == "front" and abs(xm) < r * 0.62:
                    lo = 9.0
                if hgt - lo > 0.1:
                    b.box((x1 - x0 + 0.02, 0.2, hgt - lo), (xm, yy, 0.3 + lo + (hgt - lo) / 2), mat=CORRUGATED,
                          tint=STATION_RED if side == "front" else tint)
        b.box((r * 0.62 * 2 * 0.55, 0.3, 8.8), (-r * 0.3, -L / 2 - 0.3, 0.3 + 4.4), mat=CORRUGATED,
              tint=STATION_RED)
        stripes(b, (-r * 0.62, -L / 2 - 0.12, 0), (r * 0.62, -L / 2 - 0.12, 0), 0.8, width=0.8, at_z=9.3)
        P.stencil(b, "H1", (r * 0.62 + 0.4, -L / 2 - 0.12, 4.0), (0, 0, 0), 2.4, tint=OFFWHITE)
        b.marker("door", (r * 0.3, -L / 2 - 1.5, 0.3), kind_of="hangar", width=r * 1.24, height=9.0)
        for x in (-6.0, 6.0):
            lamp(b, (x, 0.0, r - 1.2), (90, 0, 0), size=(0.8, 0.4, 0.3))


def drift(b, centre, size, seed):
    """A snow drift: a long smooth mound."""
    P.rock(b, centre, size, seed=seed, rough=0.12, freq=0.9, rings=6, sides=12, mat=MATTE, tint=SNOW_T, solid="hull",
           squash=0.35, wear=0.1)


def build(b):
    b.set_pad(("rect", 400.0, 300.0), ground="snow", blend=40.0, clear=0.0)
    # Snow over the pad: a white skin and the drifts (the planet's snow comes later; this is the station's own).
    b.box((400.0, 300.0, 0.15), (0.0, 0.0, 0.075), mat=MATTE, tint=SNOW_T, solid=False, wear=0.05)
    # Main module (labs, core store) and the accommodation block, joined by bridges to the domes.
    win_f = [(-33.0, 1.2, 2.3, 0.0)] + [(x, 1.4, 1.0, 1.2) for x in (-26.0, -20.0, -14.0, -6.0, 0.0, 8.0, 14.0, 20.0)]
    win_b = [(-12.0, 1.4, 2.3, 0.0)] + [(x, 1.4, 1.0, 1.2) for x in (-30.0, -24.0, -18.0, -4.0, 2.0, 10.0, 16.0)]
    module(b, (0.0, 0.0, 0.0), 0.0, 72.0, 16.0, h=4.6, band=ORANGE, label="ICE 3",
           openings={"front": win_f, "back": win_b, "left": [(0.0, 1.4, 2.3, 0.0)], "right": [(2.0, 1.4, 2.3, 0.0)]},
           loot=((-30.0, 4.0, "crate"), (-10.0, -4.0, "crate"), (14.0, 4.0, "barrel")))
    # Inner walls of the main module and the core store (the cyan room).
    z0 = STILT
    for x in (-18.0, 6.0, 22.0):
        P.wall_run(b, (x, -7.7), (x, 7.7), 4.6, 0.2, [(7.7, 1.2, 2.3, 0.0)], z0=z0, mat=PANEL, tint=LIGHTGREY)
    P.room(b, (30.0, 2.5, z0), (0, 0, 0), 10.0, 10.0, 3.6, thick=0.4, mat=FORMWORK, tint=srgb(0.7, 0.72, 0.74),
           openings={"left": [(0.0, 1.4, 2.3, 0.0)]}, floor=False)
    P.blast_door(b, (24.9, 2.5, z0), (0, 0, -90), w=1.4, h=2.3, depth=0.8, frame=0.5, tint=TEAL,
                 frame_tint=srgb(0.7, 0.72, 0.74), chip="cyan", cell=True, kind="core-store")
    for k in range(3):
        b.cyl(0.6, 2.0, (32.0, -0.5 + k * 2.2, z0 + 1.0), mat=PAINT, tint=srgb(0.7, 0.8, 0.9), sides=12)
    b.marker("loot", (28.0, 5.5, z0 + 0.1), table="military-crate")
    b.marker("loot", (33.0, 5.5, z0 + 0.1), table="sealed-crate")
    for k in range(6):
        b.box((3.0, 1.0, 1.0), (-32.0 + k * 9.0, 6.6, z0 + 0.5), mat=PAINT, tint=srgb(0.85, 0.86, 0.88), bevel=0.03)
        b.box((2.6, 0.06, 0.8), (-32.0 + k * 9.0, 6.0, z0 + 1.6), mat=GLASS, solid=False)
    for x in (-24.0, 24.0):
        b.box((1.4, 1.4, 1.0), (x, -4.0, z0 + 4.6 + 0.8), mat=PAINT, tint=DARK, bevel=0.05)
        b.marker("guard", (x, -4.0, z0 + 4.6 + 1.3), role="warden-turret")
    hab_f = [(-21.0, 1.2, 2.3, 0.0), (8.0, 1.4, 2.3, 0.0)] + [(x, 1.4, 1.0, 1.2) for x in (-14.0, -8.0, -2.0, 14.0)]
    hab_b = [(x, 1.4, 1.0, 1.2) for x in (-18.0, -12.0, -6.0, 0.0, 6.0, 12.0, 18.0)]
    module(b, (-30.0, 60.0, 0.0), 0.0, 48.0, 14.0, h=4.2, band=TEAL, label="HAB",
           openings={"front": hab_f, "back": hab_b},
           loot=((-15.0, 3.0, "crate"), (10.0, -3.0, "barrel"), (18.0, 3.0, "crate")))
    for k in range(6):
        b.box((2.0, 0.9, 0.6), (-50.0 + k * 7.0, 63.5, z0 + 0.6), mat=PAINT, tint=STATION_RED, solid=False)
    a_big = math.degrees(math.atan2(36.0 - 42.0, 54.0 - 65.0))
    a_small = math.degrees(math.atan2(-8.0 + 10.0, -62.0 + 70.0))
    dome(b, (65.0, 42.0, 0.0), 11.0, doors=(-90.0, a_big))
    dome(b, (-70.0, -10.0, 0.0), 7.0, doors=(a_small,), gap=None)
    bridge(b, (36.0, 2.0), (65.0 + math.cos(math.radians(a_big)) * 10.8, 42.0 + math.sin(math.radians(a_big)) * 10.8))
    bridge(b, (-36.0, 0.0), (-70.0 + math.cos(math.radians(a_small)) * 6.8,
                             -10.0 + math.sin(math.radians(a_small)) * 6.8))
    bridge(b, (-12.0, 8.0), (-22.0, 53.0))
    # The big observatory's command deck: the violet room.
    ox, oy = 65.0, 42.0
    P.room(b, (ox, oy + 2.0, STILT), (0, 0, 0), 9.0, 9.0, 3.4, thick=0.4, mat=FORMWORK,
           tint=srgb(0.7, 0.72, 0.74), openings={"front": [(0.0, 1.4, 2.3, 0.0)]}, floor=False)
    P.blast_door(b, (ox, oy - 2.9, STILT), (0, 0, 0), w=1.4, h=2.3, depth=0.8, frame=0.5,
                 tint=srgb(0.28, 0.22, 0.38), frame_tint=srgb(0.7, 0.72, 0.74), chip="violet", cell=True,
                 kind="command-deck")
    b.marker("loot", (ox - 2.5, oy + 4.5, STILT + 0.1), table="vault-crate")
    b.marker("loot", (ox + 2.5, oy + 4.5, STILT + 0.1), table="vault-crate")
    b.box((4.0, 1.0, 1.0), (ox, oy + 5.5, STILT + 0.5), mat=PAINT, tint=DARK, bevel=0.03)
    stairs(b, (ox, oy - 12.6 - 1.2 - 8.6, 0.0), (0, 0, 0), 1.2, STILT, 8.6, tint=YELLOW)
    P.deck(b, ox - 1.0, oy - 12.6 - 1.2, ox + 1.0, oy - 12.4, STILT, mat=GRATE, rails="we", edge=False)
    # Radar dome on a lattice, the met mast with its guys, a wind turbine, antennas.
    lattice_tower(b, (120.0, -40.0, 0.0), 18.0, 6.0, 4.0, sections=3, member=0.2, tint=srgb(0.36, 0.38, 0.40))
    b.box((6.0, 6.0, 0.4), (120.0, -40.0, 18.2), mat=GRATE, tint=GREY)
    b.lathe([(4.6, 0.0), (4.6, 2.0)] + [(4.6 * math.cos(math.pi / 2 * k / 6), 2.0 + 4.6 * math.sin(math.pi / 2 * k / 6))
                                        for k in range(1, 7)], (120.0, -40.0, 18.4), mat=PANEL, tint=OFFWHITE,
            sides=28)
    b.marker("landmark", (120.0, -40.0, 28.0), kind_of="radar-dome")
    ladder(b, (120.0, -43.2, 0.0), 18.2, (0, 0, 0), cage=True, tint=YELLOW)
    mx, my = -120.0, 70.0
    lattice_tower(b, (mx, my, 0.0), 60.0, 2.4, 1.2, sections=12, member=0.12, tint=STATION_RED)
    for k in range(3):
        a = math.radians(90 + 120 * k)
        for f in (0.5, 0.95):
            cable(b, (mx, my, 60.0 * f), (mx + math.cos(a) * 34.0, my + math.sin(a) * 34.0, 0.4), sag=0.01,
                  radius=0.03)
        b.box((1.2, 1.2, 0.8), (mx + math.cos(a) * 34.0, my + math.sin(a) * 34.0, 0.4), mat=CONCRETE,
              tint=CONCRETE_T)
    for k in range(4):
        b.box((1.6, 0.1, 0.1), (mx, my, 20.0 + k * 12.0), mat=PAINT, tint=GREY, solid=False)
        b.cyl(0.25, 0.3, (mx + 0.8, my, 20.2 + k * 12.0), mat=PAINT, tint=OFFWHITE, sides=8, solid=False)
    P.beacon(b, (mx, my, 60.3), 0.5)
    b.marker("landmark", (mx, my, 61.0), kind_of="met-mast", visible_km=14)
    wind_turbine(b, (-150.0, -90.0, 0.0), height=40.0, blade=16.0, yaw=200.0, broken=False, tint=OFFWHITE)
    dish(b, (95.0, -95.0, 0.0), diameter=10.0, elevation=20.0, azimuth=-30.0, tint=OFFWHITE, panels_missing=0.1)
    # The hangar: a big arched shed with its door half drifted shut, snow vehicles inside and out.
    hx, hy = -60.0, -90.0
    hangar(b, (hx, hy, 0.0), -90.0)
    b.piece("snowcat", (hx - 6.0, hy - 5.0, 0.2), (0, 0, 10))
    b.piece("snowcat", (hx + 8.0, hy + 4.0, 0.2), (0, 0, 175))
    b.piece("snowcat", (hx - 34.0, hy + 10.0, 0.2), (0, 0, 60))
    # A sledge train left where it stopped: the cat and three sledges in a line.
    for k in range(3):
        a = math.radians(60.0)
        cargo_sledge(b, (hx - 34.0 - math.cos(a) * (8.0 + 8.4 * k), hy + 10.0 - math.sin(a) * (8.0 + 8.4 * k), 0.15),
                     60.0, tint=[STATION_RED, srgb(0.20, 0.33, 0.55), OFFWHITE][k])
    b.marker("loot", (hx + 12.0, hy - 8.0, 0.3), table="crate")
    b.marker("loot", (hx - 12.0, hy + 8.0, 0.3), table="military-crate")
    b.marker("guard", (hx, hy, 0.3), role="reclaimer")
    b.marker("guard", (hx - 30.0, hy - 6.0, 0.2), role="reclaimer")
    # Fuel bladders and drums, the generator hut, the drill tower over the core shaft.
    for k in range(8):
        fuel_sledge(b, (64.0 + (k % 4) * 9.0, -56.0 - (k // 4) * 7.0, 0.15), 0.0,
                    tint=STATION_RED if k % 3 else ORANGE)
    b.tube([(64.0, -52.4, 0.3), (100.0, -52.4, 0.3), (100.0, -20.0, 0.3)], 0.08, mat=PAINT, tint=BLACK, sides=6)
    for k in range(4):
        b.piece("drum-stack", (64.0 + k * 1.4, -70.0, 0.15), (0, 0, k * 12))
    b.marker("loot", (66.0, -72.0, 0.2), table="fuel-barrel")
    b.marker("loot", (70.0, -72.0, 0.2), table="fuel-barrel")
    module(b, (100.0, -10.0, 0.0), 90.0, 18.0, 10.0, h=3.6, band=STATION_RED, label="GEN", stilt=2.4,
           loot=((0.0, 2.0, "barrel"),), tower=False, roof_kit=False)
    for k in range(2):
        b.cyl(0.4, 4.0, (96.0 + k * 3.0, -4.0, 8.0), mat=PAINT, tint=DARK, sides=8)
    dx, dy = 20.0, 80.0
    P.steel_frame(b, (dx, dy, 0.0), (0, 0, 0), 6.0, 6.0, [0.0, 8.0, 16.0, 24.0], col=0.4, tint=STATION_RED,
                  brace=("s", "n", "w", "e"))
    b.box((7.0, 7.0, 0.3), (dx, dy, 24.15), mat=GRATE, tint=GREY)
    b.cyl(0.4, 30.0, (dx, dy, 15.0), mat=PAINT, tint=GREY, sides=10)
    b.cyl(2.0, 0.6, (dx, dy, 0.4), mat=PAINT, tint=DARK, sides=16)
    ladder(b, (dx + 3.4, dy, 0.0), 24.0, (0, 0, 90), cage=True, tint=YELLOW)
    P.room(b, (dx + 10.0, dy, 0.0), (0, 0, 0), 8.0, 6.0, 3.0, thick=0.2, mat=PANEL, tint=MOD,
           openings={"front": [(-2.0, 1.2, 2.3, 0.0), (1.5, 2.0, 1.0, 1.0)]})
    b.marker("loot", (dx + 11.0, dy + 1.0, 0.2), table="crate")
    P.beacon(b, (dx, dy, 30.2), 0.35)
    # Drifts banked against the modules and the hangar, sastrugi ridges, flag poles marking the routes.
    for k, (x, y, sx, sy, sz) in enumerate(((0.0, 14.0, 70.0, 10.0, 3.6), (-30.0, 72.0, 46.0, 10.0, 3.0),
                                            (-60.0, -72.0, 50.0, 12.0, 4.0), (-85.0, -90.0, 14.0, 26.0, 5.0),
                                            (65.0, 58.0, 30.0, 12.0, 3.0), (110.0, 10.0, 16.0, 26.0, 2.6),
                                            (-150.0, 20.0, 80.0, 16.0, 4.5), (160.0, 130.0, 70.0, 18.0, 5.0),
                                            (40.0, -120.0, 90.0, 14.0, 3.0))):
        drift(b, (x, y, 0.0), (sx, sy, sz * 2.0), seed=900 + k)
    for k in range(14):
        x = -170.0 + k * 24.0
        b.cyl(0.04, 2.2, (x, -130.0, 1.1), mat=PAINT, tint=DARK, sides=5, solid=False)
        b.box((0.02, 0.5, 0.35), (x, -130.25, 1.95), mat=MATTE, tint=STATION_RED if k % 2 else ORANGE, solid=False)
    # The landing pad: a marked square on the snow with corner lamps and a windsock.
    lx, ly = 130.0, 80.0
    b.box((34.0, 34.0, 0.25), (lx, ly, 0.2), mat=CONCRETE, tint=srgb(0.55, 0.57, 0.58))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.box((0.5, 0.5, 0.4), (lx + math.cos(a) * 22.0, ly + math.sin(a) * 22.0, 0.4), mat=LAMP, tint=WARM_LAMP,
              solid=False)
    b.cyl(9.0, 0.03, (lx, ly, 0.34), mat=PAINT, tint=ORANGE, sides=40, solid=False, vary=0)
    b.cyl(8.2, 0.035, (lx, ly, 0.345), mat=PAINT, tint=srgb(0.55, 0.57, 0.58), sides=40, solid=False, vary=0)
    P.stencil(b, "H", (lx - 1.7, ly - 2.8, 0.36), (90, 0, 0), 5.6, tint=ORANGE, depth=0.02)
    b.cyl(0.06, 5.0, (lx + 20.0, ly - 20.0, 2.5), mat=PAINT, tint=GREY, sides=6, solid=False)
    b.cyl(0.4, 2.2, (lx + 21.1, ly - 20.0, 4.6), (0, 80, 0), mat=MATTE, tint=ORANGE, sides=8, radius2=0.15, solid=False)
    # The supply depot: containers in a row, snow on their roofs.
    for k in range(5):
        x = -150.0 + k * 3.0
        b.piece("container-40", (x, -20.0 + (k % 2) * 0.4, 0.15), (0, 0, 90),
                tint=[STATION_RED, srgb(0.20, 0.33, 0.55), ORANGE, OFFWHITE, STATION_RED][k])
        b.box((2.3, 11.8, 0.3), (x, -20.0, 2.9), mat=MATTE, tint=SNOW_T, solid=False)
    b.marker("loot", (-147.0, -27.0, 0.2), table="crate")
    # The old station: an early module on the ice, half buried by the drift that grew round it, its mast bent.
    with b.frame((150.0, -100.0, 0.0), (0, 0, 20.0)):
        b.box((24.0, 8.0, 3.2), (0, 0, 1.0), (0, 2.0, 0), mat=PANEL, tint=srgb(0.80, 0.62, 0.30))
        b.box((24.2, 8.2, 0.4), (0, 0, 2.2), mat=PAINT, tint=STATION_RED, solid=False)
        b.box((1.2, 0.2, 1.6), (-8.0, -4.05, 1.2), mat=PAINT, tint=DARK, solid=False)
        b.marker("door", (-8.0, -5.0, 0.2), kind_of="hatch")
        b.marker("loot", (-4.0, 0.0, 0.2), table="crate")
        b.tube([(6.0, 0.0, 2.6), (6.0, 0.0, 9.0), (8.0, 0.0, 13.0)], 0.1, mat=PAINT, tint=GREY, sides=6)
    drift(b, (150.0, -96.0, 0.0), (40.0, 24.0, 7.0), seed=990)
    # An antenna field: thin masts in a grid with wires between.
    for i in range(3):
        for j in range(3):
            x, y = 40.0 + i * 18.0, -70.0 + j * 14.0
            b.cyl(0.1, 12.0, (x, y, 6.0), mat=PAINT, tint=STATION_RED if (i + j) % 2 else OFFWHITE, sides=6,
                  solid=False)
            if i < 2:
                cable(b, (x, y, 11.6), (x + 18.0, y, 11.6), sag=0.03, radius=0.015)
    # Look-dev only: the ice sheet round the station for the renders (the planet's terrain will be the real one).
    if not kit.EXPORT["enabled"]:
        b.box((20000.0, 20000.0, 1.1), (0.0, 0.0, -0.45), mat=MATTE, tint=SNOW_T, solid=False, wear=0.0)
    # Lamps on the modules, a cold zone over the site, the approach.
    for x, y, yaw in ((-36.0, -9.0, 0), (36.0, -9.0, 0), (-30.0, 52.0, 0), (60.0, 30.0, 0)):
        lamp(b, (x, y, STILT + 4.0), (25, 0, yaw), size=(0.6, 0.3, 0.35))
    b.zone("cold", (0.0, 0.0, 10.0), size=(420.0, 320.0, 60.0), level=2)
    b.zone("no-build", (0, 0, 0), size=(480.0, 380.0, 200.0))
    b.marker("spawn", (0.0, -160.0, 0), kind_of="approach")
    b.meta.update({"tier": "large", "landmark": False, "spawn": "ice", "visible_km": 14})


SUN = (14.0, 200.0)
FOG = 0.00018
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((-20.0, -24.0, 0.15), 200.0), ((-34.0, -21.0, 0.15), 160.0)]
SHIP = ((130.0, 80.0, 0.33), 200.0)
VIEWS = [
    {"name": "far", "target": (0.0, 0.0, 22.0), "distance": 2400, "yaw": 25, "pitch": -0.35, "lens": 100},
    {"name": "overview", "target": (0.0, 0.0, 5.0), "distance": 520, "yaw": 25, "pitch": 30, "lens": 30},
    {"name": "stilts", "target": (-14.0, 0.0, 8.5), "distance": 48, "yaw": -34, "pitch": -7.5, "lens": 22},
]
