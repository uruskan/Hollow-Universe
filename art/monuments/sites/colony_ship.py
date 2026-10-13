"""Grounded Colony Ship (MONUMENTS.md §5.5, landmark tier): one of the colony ships that were in transit when the
Quiet came, 2.4 km long, brought down on a plain. It struck at a shallow angle, ploughed a furrow two kilometres long
and broke in three: the stern with its five drive bells and its radiator wings, the long habitat section (cryo bays
and farm decks open to the sky where its hull tore), and the bow with the bridge, rolled on its side with its prow
dug into a wave of earth. Decks and ribs stand exposed at every break; plating, ribs and cargo lie strewn between the
pieces. A Reclaimer camp has grown at the habitat section's open end, where the climb into the decks begins.
"""
import math

from mathutils import Vector

from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW, cable, lamp,
                        railing, srgb)
from monkit.surfaces import CONCRETE, CORRUGATED, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "colony-ship"
TITLE = "Grounded Colony Ship"
SEED = 5001
BUILDER = {"chunk": 200.0, "max_edge": 16.0, "lod_min": (0.0, 2.0, 16.0)}
LODS = [500.0, 3000.0, 80000.0]

W, H = 220.0, 150.0     # the hull's section
SINK = 20.0             # how deep its belly lies under the plain
N = 48                  # points round a hull ring
HULL_T = srgb(0.80, 0.80, 0.78)
HULL_DARK = srgb(0.36, 0.37, 0.38)
SCORCH = srgb(0.10, 0.09, 0.085)
DECK_T = srgb(0.42, 0.43, 0.44)

# Where each piece lies: (centre, yaw, roll); its hull runs along local +Y toward the bow, its local +X to the south.
SW, SH = 250.0, 170.0   # the drive section's
MW, MH = 200.0, 140.0   # the habitat section's
BW, BH = 220.0, 150.0   # the bow's
STERN = ((-1150.0, 90.0, 0.0), -102.0, 6.0)
MID = ((-150.0, -40.0, 0.0), -76.0, -6.0)
BOW = ((880.0, 60.0, 0.0), -96.0, 24.0)
STERN_PITCH = 2.5
BOW_PITCH = -3.5


def zc_of(h):
    return h / 2 * 0.86 - SINK


def section(b, stations, front=None, back=None, tint=HULL_T, n=N, skeleton=None):
    """One piece's hull: `stations` [(y, w, h)] ascending; `back` / `front` = None for a closed end, or the length
    of torn, exposed hull at a break there. Plating is torn away toward each break (ribs and longerons show
    through), an inner skin gives the torn edge its thickness. Returns the rings."""
    rings, inner = [], []
    for y, w, h in stations:
        ring, hw = P.superellipse(w, h)
        rings.append(ring(n, y, zc_of(h)))
        ring_i, _ = P.superellipse(w - 3.0, h - 3.0)
        inner.append(ring_i(n, y, zc_of(h)))
    y0, y1 = stations[0][0], stations[-1][0]
    seed = b.rng.randrange(1 << 20)

    def exposure(y):
        e = 0.0
        if back:
            e = max(e, 1 - (y - y0) / back)
        if front:
            e = max(e, 1 - (y1 - y) / front)
        return max(0.0, e)

    def holes(i, j):
        ym = (stations[i][0] + stations[i + 1][0]) / 2
        if skeleton and skeleton[0] <= ym <= skeleton[1]:
            # The skin gone: only the buried belly and a few torn strips remain.
            return not (n * 0.66 < j < n * 0.84 or P._hash(i, j, 11, seed) < 0.1)
        e = exposure(ym)
        if e <= 0:
            return False
        if j > n * 0.62 and j < n * 0.88:
            e *= 0.5   # the buried belly keeps more of its plating
        return P._hash(i, j, 7, seed) < 0.9 * e ** 1.3
    P.hull_loft(b, rings, mat=HULL, tint=tint, holes=holes, ragged=(18.0 if back else 0.0, 18.0 if front else 0.0))
    # Inner skin where the hull is torn, dark, sharing the holes; ribs inside it, longerons along it.
    zones = []
    if back:
        zones.append([i for i, st in enumerate(stations) if st[0] - y0 < back])
    if front:
        zones.append([i for i, st in enumerate(stations) if y1 - st[0] < front])
    for idx in zones:
        if len(idx) < 2:
            continue
        lo, hi = min(idx), max(idx)
        sub = inner[lo:hi + 1]
        P.hull_loft(b, sub, mat=PAINT, tint=HULL_DARK, holes=lambda i, j, lo=lo: holes(i + lo, j), wear=0.7)
        for i in range(lo, hi + 1):
            if exposure(stations[i][0]) <= 0.05:
                continue
            r = inner[i]
            b.tube(list(r) + [r[0]], 0.9, mat=RUST, tint=(1, 1, 1), sides=6, cap=False)
        for j in range(0, n, 4):
            b.tube([inner[i][j] for i in range(lo, hi + 1)], 0.7, mat=PAINT, tint=DARK, sides=6, cap=False)
    return rings


def break_decks(b, y_end, direction, w, h, depth=80.0, cryo=False):
    """Decks and bulkheads standing out of a break at `y_end` (`direction` +1 if the break faces +Y): decks at
    uneven heights, cut into bays by bulkheads, each bay torn off at its own length, some sagging or gone, cables
    hanging, darkness deeper in."""
    _, hw = P.superellipse(w - 4.0, h - 4.0)
    zc = zc_of(h)
    levels = []
    z = -SINK + 6.0
    while z < zc + h / 2 - 10.0:
        levels.append(z)
        z += b.rng.choice((6.0, 7.0, 8.0, 8.0, 9.0, 13.0, 17.0))
    xs = [-w / 2]
    x = -w / 2 + b.rng.uniform(16.0, 34.0)
    while x < w / 2 - 12.0:
        xs.append(x)
        x += b.rng.uniform(16.0, 34.0)
    xs.append(w / 2)
    for li, z in enumerate(levels):
        half = hw(z - zc) * 0.98
        if half < 8:
            continue
        for xa, xb in zip(xs, xs[1:]):
            xa2, xb2 = max(xa, -half), min(xb, half)
            if xb2 - xa2 < 3.0 or b.rng.random() < 0.08:
                continue
            out = b.rng.uniform(-26.0, 10.0)
            L = depth + out
            yc = y_end - direction * depth / 2 + direction * out / 2
            sag = b.rng.uniform(-10.0, 2.0) if out > 2 else 0.0
            xm, xw = (xa2 + xb2) / 2, xb2 - xa2 - 0.4
            b.box((xw, L, 0.9), (xm, yc, z), (sag * direction, 0, 0), mat=PAINT, tint=DECK_T, solid=z < 10)
            b.box((xw, 0.6, 0.7), (xm, y_end + direction * out, z - 0.75), mat=PAINT, tint=DARK, solid=False)
            if b.rng.random() < 0.35:
                x0 = b.rng.uniform(xa2 + 1, xb2 - 1)
                drop = b.rng.uniform(3.0, 14.0)
                b.tube([(x0, y_end + direction * (out - 0.5), z - 0.5),
                        (x0 + b.rng.uniform(-1, 1), y_end + direction * (out + 0.8), z - drop)], 0.14, mat=PAINT,
                       tint=BLACK, sides=5)
            if cryo and 0 < li < 7 and out > -8 and abs(sag) < 3:
                q = 0
                while xa2 + 7.0 + q * 11.5 < xb2 - 6.0:
                    b.piece("cryo-rack", (xa2 + 7.0 + q * 11.5, y_end + direction * (out - 5.5), z + 0.45),
                            (0, 0, 0 if direction > 0 else 180))
                    q += 1
            if out > 0 and abs(sag) < 2 and b.rng.random() < 0.45:
                railing(b, (xa2 + 0.6, y_end + direction * (out - 0.4), z + 0.45),
                        (xb2 - 0.6, y_end + direction * (out - 0.4), z + 0.45), 1.1, spacing=2.0)
    for x in xs[1:-1]:
        for z0, z1 in zip(levels, levels[1:]):
            if abs(x) > hw((z0 + z1) / 2 - zc) - 2 or b.rng.random() < 0.3:
                continue
            out = b.rng.uniform(-26.0, 2.0)
            L = depth + out
            b.box((1.0, L, z1 - z0 - 0.9), (x, y_end - direction * depth / 2 + direction * out / 2, (z0 + z1) / 2),
                  mat=PAINT, tint=HULL_DARK, solid=False)
    for f in (0.45, 0.8):
        b.box((w * 0.8, 1.2, h * 0.8), (0, y_end - direction * depth * f, zc), mat=MATTE, tint=srgb(0.12, 0.12, 0.12),
              solid=False, vary=0)
    b.box((w * 0.95, 1.0, h * 0.95), (0, y_end - direction * depth, zc), mat=MATTE, tint=srgb(0.04, 0.04, 0.04),
          solid=False, vary=0)


def petals(b, y_end, direction, w, h, count=16, n=N):
    """Plating torn and bent outward round a break's rim."""
    ring, _ = P.superellipse(w, h)
    zc = zc_of(h)
    pts = ring(n, y_end, zc)
    for k in range(count):
        j = b.rng.randrange(0, n)
        p = Vector(pts[j])
        if p.z < 2.0:
            continue
        rad = Vector((p.x, 0, p.z - zc)).normalized()
        d = (rad * b.rng.uniform(0.4, 1.0) + Vector((0, direction, 0)) * b.rng.uniform(0.3, 1.0)).normalized()
        L = b.rng.uniform(8.0, 22.0)
        b.beam(tuple(p - Vector((0, direction * 2.0, 0))), tuple(p + d * L), b.rng.uniform(6.0, 12.0), 0.6, mat=HULL,
               tint=b.rng.choice((HULL_T, HULL_T, HULL_DARK)), roll=b.rng.uniform(-20, 20))


def hull_strip(b, stations, j0, j1, scale, tint, mat=PAINT, wear=0.5):
    """A band of paint (or plating) following the hull between ring points j0..j1 (may wrap), over `stations`."""
    rings = []
    for y, w, h in stations:
        ring, _ = P.superellipse(w + scale, h + scale)
        r = ring(N, y, zc_of(h))
        idx = [(j0 + k) % N for k in range(((j1 - j0) % N) + 1)]
        rings.append([r[i] for i in idx])
    m = len(rings[0])
    verts = [v for r in rings for v in r]
    faces = []
    for i in range(len(rings) - 1):
        for j in range(m - 1):
            a = i * m + j
            faces.append([a, a + 1, a + 1 + m, a + m])
    b.mesh(verts, faces, mat=mat, tint=tint, solid=False, smooth=True, wear=wear, vary=0.02)


def livery(b, stations):
    """The hull's paint over a stretch of `stations`: the lower flanks scorched dark by the descent, the belly
    burnt black, an orange and a teal stripe high on both flanks."""
    hull_strip(b, stations, 40, 47, 0.4, srgb(0.30, 0.30, 0.30))
    hull_strip(b, stations, 25, 32, 0.4, srgb(0.30, 0.30, 0.30))
    hull_strip(b, stations, 32, 40, 0.5, SCORCH, mat=MATTE)
    hull_strip(b, stations, 47, 1, 0.45, srgb(0.42, 0.42, 0.41))
    hull_strip(b, stations, 2, 3, 0.5, ORANGE)
    hull_strip(b, stations, 21, 22, 0.5, ORANGE)
    hull_strip(b, stations, 4, 5, 0.5, TEAL)
    hull_strip(b, stations, 19, 20, 0.5, TEAL)


def collar(b, y, w=W, h=H, grow=7.0, length=12.0, tint=srgb(0.62, 0.63, 0.63)):
    """A segment collar round the hull: a thick ring band proud of the plating."""
    r0, _ = P.superellipse(w, h)
    r1, _ = P.superellipse(w + grow, h + grow)
    zc = zc_of(h)
    rings = [r0(N, y - length / 2, zc), r1(N, y - length / 2 + 1.5, zc), r1(N, y + length / 2 - 1.5, zc),
             r0(N, y + length / 2, zc)]
    P.hull_loft(b, rings, mat=HULL, tint=tint)
    for k in range(0, N, 3):
        p = Vector(rings[1][k])
        if p.z < 1.0:
            continue
        b.box((3.0, length + 1.0, 2.4), tuple(p), (0, math.degrees(math.atan2(p.x, p.z - zc)), 0), mat=PAINT,
              tint=DARK, solid=False)


def tanks(b, y0, count, w=W, h=H, side=1, r=12.0, length=90.0, tint=srgb(0.76, 0.76, 0.74)):
    """A row of propellant tanks along the hull's upper shoulder, strapped on."""
    zc = zc_of(h)
    _, hw = P.superellipse(w, h)
    dz = h / 2 * 0.62
    x = side * (hw(dz) - 2.0)
    for k in range(count):
        y = y0 + k * (length + 12.0)
        b.cyl(r, length, (x, y, zc + dz + r * 0.6), (90, 0, 0), mat=HULL, tint=tint, sides=24)
        for e in (-1, 1):
            b.lathe([(r, 0.0), (r * 0.85, r * 0.4), (r * 0.5, r * 0.7), (0.0, r * 0.8)],
                    (x, y + e * length / 2, zc + dz + r * 0.6), (-90 * e, 0, 0), mat=HULL, tint=tint, sides=24)
        for q in range(3):
            b.cyl(r + 0.4, 2.0, (x, y - length / 3 + q * length / 3, zc + dz + r * 0.6), (90, 0, 0), mat=PAINT,
                  tint=DARK, sides=24, solid=False)
        b.box((r * 2.2, 2.0, 1.0), (x, y, zc + dz + r * 1.65), mat=PAINT, tint=ORANGE if k % 2 else TEAL,
              solid=False, vary=0)


def flank_detail(b, y0, y1, w, h, windows=0, bands=False, hatches=8, panels=30, scorch=0.5, sides=(1, -1)):
    """Plates of different shades, livery bands, frames, window rows, hatches, scorch and missing plates along the
    flanks of a straight stretch of hull from y0 to y1."""
    zc = zc_of(h)
    _, hw = P.superellipse(w, h)
    for s in sides:
        def X(dz, out=0.15):
            return s * (hw(dz) + out)
        if bands:
            for dz, hgt, tint in ((26.0, 5.0, ORANGE), (19.5, 2.2, TEAL), (-24.0, 7.0, HULL_DARK)):
                b.box((0.5, y1 - y0 - 6.0, hgt), (X(dz), (y0 + y1) / 2, zc + dz), mat=PAINT, tint=tint, solid=False,
                      vary=0, wear=0.55)
        for _ in range(panels):
            pl = b.rng.uniform(16.0, 60.0)
            ph = b.rng.uniform(6.0, 22.0)
            y = b.rng.uniform(y0 + pl / 2, y1 - pl / 2)
            dz = b.rng.uniform(-30.0, 40.0)
            if abs(dz - 22.0) < ph / 2 + 4:
                continue
            b.box((0.35, pl, ph), (X(dz, 0.05), y, zc + dz), mat=HULL,
                  tint=b.rng.choice((srgb(0.72, 0.72, 0.70), srgb(0.86, 0.86, 0.84), srgb(0.64, 0.65, 0.66))),
                  solid=False, vary=0.02)
        for k in range(windows):
            dz = 4.0 + k * 7.5
            b.box((0.4, y1 - y0 - 30.0, 1.6), (X(dz, 0.2), (y0 + y1) / 2, zc + dz), mat=GLASS, solid=False, vary=0)
            yy = y0 + 15.0
            while yy < y1 - 15.0:
                b.box((0.5, 0.8, 2.2), (X(dz, 0.3), yy, zc + dz), mat=PAINT, tint=DARK, solid=False, vary=0)
                yy += 9.0
            for _ in range(int((y1 - y0) / 60)):
                b.box((0.5, b.rng.uniform(5, 20), 1.7), (X(dz, 0.35), b.rng.uniform(y0 + 20, y1 - 20), zc + dz),
                      mat=MATTE, tint=P.SOOT, solid=False, vary=0)
        for k in range(hatches):
            y = y0 + (y1 - y0) * (k + 0.5) / hatches
            dz = -14.0 if k % 2 else 8.0
            b.box((0.6, 14.0, 10.0), (X(dz, 0.2), y, zc + dz), mat=PAINT, tint=srgb(0.55, 0.56, 0.57), solid=False,
                  bevel=0.2)
            b.box((0.7, 14.6, 1.0), (X(dz, 0.25), y, zc + dz + 5.5), mat=PAINT, tint=YELLOW, solid=False, vary=0)
        for _ in range(int((y1 - y0) / 40 * scorch)):
            y = b.rng.uniform(y0, y1)
            dz = b.rng.uniform(-34.0, 30.0)
            b.box((0.5, b.rng.uniform(10, 50), b.rng.uniform(6, 26)), (X(dz, 0.45), y, zc + dz),
                  (b.rng.uniform(-8, 8), 0, 0), mat=MATTE, tint=SCORCH, solid=False, vary=0.15)
    # Frames: rings proud of the hull every 50 m; the dorsal spine with its conduits.
    y = y0 + 25.0
    while y < y1 - 10:
        ring, _ = P.superellipse(w + 1.6, h + 1.6)
        r = ring(N, y, zc)
        b.tube(r + [r[0]], 0.9, mat=PAINT, tint=srgb(0.6, 0.6, 0.6), sides=6, cap=False)
        y += 50.0
    b.box((22.0, y1 - y0 - 4.0, 7.0), (0, (y0 + y1) / 2, zc + h / 2 + 2.0), mat=HULL, tint=srgb(0.7, 0.7, 0.69),
          bevel=0.5, solid=False)
    for x in (-8.0, 0.0, 8.0):
        b.cyl(1.4, y1 - y0 - 10.0, (x, (y0 + y1) / 2, zc + h / 2 + 6.4), (90, 0, 0), mat=PAINT,
              tint=(ORANGE if x == 0 else LIGHTGREY), sides=10, solid=False)


# ------------------------------------------------------------------------------------------------ the pieces


def stern(b):
    """The drive section, wider than the rest: five bells at the stern, two radiator sails standing from its back
    and a third snapped off and lying beside it, the reactor's dome, its front torn open and reared up."""
    at, yaw, roll = STERN
    w, h = SW, SH
    stations = [(-320.0, w - 26.0, h - 16.0), (-308.0, w - 10.0, h - 6.0), (-292.0, w, h)]
    y = -270.0
    while y < 250.0:
        stations.append((y, w, h))
        y += 26.0
    y = 250.0
    while y <= 320.0:
        stations.append((y, w, h))
        y += 7.0
    with b.frame(at, (STERN_PITCH, roll, yaw)):
        section(b, stations, front=70.0)
        zc = zc_of(h)
        livery(b, [st for st in stations if st[0] < 250.0])
        for y in (-150.0, 40.0, 200.0):
            collar(b, y, w, h)
        petals(b, 320.0, 1, w, h, count=22)
        # The stern bulkhead, fairings and the drive.
        ring, _ = P.superellipse(w - 26.0, h - 16.0)
        b.mesh(ring(N, -320.0, zc_of(h - 16.0)), [list(range(N))[::-1]], mat=PAINT, tint=HULL_DARK, solid=False)
        for x, z, rr in ((0.0, zc + 12.0, 38.0), (-72.0, zc + 46.0, 28.0), (72.0, zc + 46.0, 28.0),
                         (-72.0, zc - 28.0, 28.0), (72.0, zc - 28.0, 28.0)):
            with b.frame((x, -322.0, z), (90, 0, 0)):
                prof = [(rr * 0.45, 0.0), (rr * 0.5, 6.0), (rr * 0.62, rr * 0.9), (rr * 0.82, rr * 1.7),
                        (rr, rr * 2.3), (rr * 0.96, rr * 2.3), (rr * 0.78, rr * 1.7), (rr * 0.58, rr * 0.9),
                        (rr * 0.4, 6.0), (rr * 0.38, 0.0), (rr * 0.45, 0.0)]
                b.lathe(prof, (0, 0, 0), mat=RUST, tint=(1, 1, 1), sides=40, close=False, solid="hull", wear=0.9)
                b.cyl(rr * 1.02, 3.0, (0, 0, rr * 2.3), mat=PAINT, tint=DARK, sides=40, solid=False)
                for k in range(8):
                    a = 2 * math.pi * k / 8
                    b.beam((math.cos(a) * rr * 0.5, math.sin(a) * rr * 0.5, 2.0),
                           (math.cos(a) * rr * 0.75, math.sin(a) * rr * 0.75, rr * 1.4), 1.4, 1.4, mat=PAINT,
                           tint=DARK)
            b.cyl(rr * 0.62, 14.0, (x, -315.0, z), (90, 0, 0), mat=PAINT, tint=HULL_DARK, sides=24, solid=False)
        for z in (zc - 56.0, zc + 76.0):
            b.box((200.0, 9.0, 7.0), (0, -320.0, z), mat=PAINT, tint=DARK, solid=False)
        for s in (-1, 1):
            b.extrude([(0, 0), (40.0, 0), (34.0, 60.0), (0, 70.0)], 120.0, (s * (w / 2 - 6.0), -250.0, zc - 20.0),
                      (0, 0, 0 if s > 0 else 180), mat=HULL, tint=srgb(0.66, 0.67, 0.67), solid=False)
        flank_detail(b, -280.0, 240.0, w, h, windows=0, hatches=6, panels=26, scorch=1.4)
        # Reactor dome on the back.
        b.lathe([(50.0, 0.0), (48.0, 10.0), (40.0, 24.0), (24.0, 33.0), (0.0, 36.0)], (0, -190.0, zc + h / 2 - 4.0),
                mat=HULL, tint=srgb(0.74, 0.74, 0.72), sides=48)
        b.cyl(50.5, 3.0, (0, -190.0, zc + h / 2 + 1.0), mat=PAINT, tint=ORANGE, sides=48, solid=False, vary=0)
        for k in range(6):
            a = 2 * math.pi * k / 6
            P.aviation_lamp(b, (math.cos(a) * 48.0, -190.0 + math.sin(a) * 48.0, zc + h / 2 + 4.0), 1.0)
        # Radiator sails: two standing high off the back, one snapped and lying beside the hull.
        radiator(b, (52.0, -20.0, zc + h / 2 - 12.0), (0, 24.0, 0), 300.0, 165.0, broken=0.12)
        radiator(b, (-52.0, 10.0, zc + h / 2 - 12.0), (0, -30.0, 0), 300.0, 165.0, broken=0.28, snapped=0.62)
        radiator(b, (w / 2 + 110.0, 150.0, 6.0), (0, 86.0, 10.0), 250.0, 150.0, broken=0.45)
        b.box((16.0, 40.0, 22.0), (w / 2 + 6.0, 150.0, 6.0), (0, 30, 0), mat=PAINT, tint=DARK)
        break_decks(b, 320.0, 1, w, h, depth=70.0)
        b.marker("loot", (0, 270.0, -SINK + 7.0), table="military-crate")
        b.zone("radiation", (0, -190.0, zc), radius=100.0, level=4)


def radiator(b, at, rot, length, height, broken=0.1, snapped=None):
    """A radiator sail: a box spar at its root, ribs every 15 m, a frame along its edge, dark panels between the
    ribs, some gone; `snapped` (0..1) breaks it off at that fraction of its height, a jagged top."""
    top = height * (snapped or 1.0)
    with b.frame(at, rot):
        # Local frame: the sail runs along Y, rises along +Z from its root at z = 0, thin in X.
        b.box((7.0, length, 9.0), (0, 0, 2.0), mat=PAINT, tint=DARK)
        if not snapped:
            b.box((3.4, length, 3.0), (0, 0, height), mat=PAINT, tint=GREY, solid=False)
        ribs = int(length / 15)
        for i in range(ribs + 1):
            y = -length / 2 + length * i / ribs
            hh = top * (b.rng.uniform(0.75, 1.0) if snapped else 1.0)
            b.box((2.8, 1.8, hh), (0, y, hh / 2), mat=PAINT, tint=GREY, solid=False)
        for i in range(ribs):
            y = -length / 2 + length * (i + 0.5) / ribs
            rows = 4
            for k in range(rows):
                z = 8.0 + (height - 10.0) * (k + 0.5) / rows
                if z > top - 6 or b.rng.random() < broken:
                    continue
                b.box((0.9, length / ribs - 2.0, (height - 10.0) / rows - 1.2), (0, y, z), mat=PAINT,
                      tint=srgb(0.14, 0.15, 0.16), solid=False, vary=0.06)
            b.beam((0, y - length / ribs / 2, 8.0), (0, y + length / ribs / 2, min(top, height)), 0.9, 0.9,
                   mat=PAINT, tint=GREY)


def mid(b):
    """The habitat section: a slimmer hull, both ends torn open, window rows along its flanks. Its middle has lost
    its skin: the frames stand bare like ribs over four farm decks gone wild. Cryo racks stand at its open
    front, where the Reclaimers camp."""
    at, yaw, roll = MID
    w, h = MW, MH
    sk0, sk1 = -170.0, 150.0
    stations = []
    y = -480.0
    while y < -405.0:
        stations.append((y, w, h))
        y += 7.0
    y = -405.0
    while y < 405.0:
        stations.append((y, w, h))
        y += 15.0 if sk0 - 20 <= y <= sk1 + 20 else 27.0
    y = 405.0
    while y <= 480.0:
        stations.append((y, w, h))
        y += 7.0
    with b.frame(at, (0, roll, yaw)):
        section(b, stations, front=75.0, back=75.0, skeleton=(sk0, sk1))
        zc = zc_of(h)
        _, hw = P.superellipse(w, h)
        livery(b, [st for st in stations if -405.0 <= st[0] <= sk0 + 4.0])
        livery(b, [st for st in stations if sk1 - 4.0 <= st[0] <= 405.0])
        for y in (-330.0, -230.0, 230.0, 330.0):
            collar(b, y, w, h)
        flank_detail(b, -400.0, sk0 - 10.0, w, h, windows=4, hatches=4, panels=16, scorch=0.6)
        flank_detail(b, sk1 + 10.0, 400.0, w, h, windows=4, hatches=4, panels=16, scorch=0.6)
        tanks(b, -390.0, 2, w, h, side=-1, r=10.0, length=80.0)
        tanks(b, 200.0, 2, w, h, side=-1, r=10.0, length=80.0)
        petals(b, 480.0, 1, w, h, count=20)
        petals(b, -480.0, -1, w, h, count=20)
        # The bare frames: heavy rings every 10 m, longerons, collars at both ends of the bare stretch.
        ring, _ = P.superellipse(w - 1.0, h - 1.0)
        y = sk0 + 5.0
        while y < sk1 - 2:
            r = ring(N, y, zc)
            pts = [p for p in r if p[2] > -4.0]
            # The ring above ground, as one open run (it starts and ends in the earth).
            k0 = next(i for i in range(N) if r[i][2] <= -4.0 and r[(i + 1) % N][2] > -4.0)
            run = [r[(k0 + 1 + i) % N] for i in range(N) if r[(k0 + 1 + i) % N][2] > -4.0]
            gap = b.rng.random() < 0.18
            if gap:
                cut = b.rng.randrange(4, len(run) - 4)
                b.tube(run[:cut], 1.5, mat=PAINT, tint=srgb(0.5, 0.51, 0.52), sides=6, cap=True)
                b.tube(run[cut + 3:], 1.5, mat=PAINT, tint=srgb(0.5, 0.51, 0.52), sides=6, cap=True)
            else:
                b.tube(run, 1.5, mat=PAINT, tint=srgb(0.5, 0.51, 0.52), sides=6, cap=True)
            y += 10.0
        for j in range(0, N, 3):
            p0 = ring(N, sk0, zc)[j]
            if p0[2] < -2.0:
                continue
            b.tube([ring(N, sk0, zc)[j], ring(N, sk1, zc)[j]], 0.9, mat=RUST, tint=(1, 1, 1), sides=6)
        for y in (sk0, sk1):
            collar(b, y, w, h, grow=6.0, length=10.0, tint=srgb(0.55, 0.56, 0.56))
        # Farm decks inside the bare stretch, overgrown: bushes, trees, vines over their edges.
        for k, dz in enumerate((-34.0, -16.0, 2.0, 20.0)):
            z = zc + dz
            half = hw(dz) * 0.86
            b.box((half * 2, sk1 - sk0 - 6.0, 1.4), (0, (sk0 + sk1) / 2, z), mat=PAINT, tint=DECK_T, solid=z < 8)
            b.box((half * 2, sk1 - sk0 - 6.0, 0.4), (0, (sk0 + sk1) / 2, z + 0.9), mat=MATTE,
                  tint=srgb(0.24, 0.3, 0.14), solid=False, vary=0.06)
            for s in (-1, 1):
                for q in range(16):
                    yy = b.rng.uniform(sk0 + 6, sk1 - 6)
                    xx = s * (half - b.rng.uniform(1.0, 14.0))
                    if q % 4 == 0:
                        P.tree(b, (xx, yy, z + 1.0), b.rng.uniform(7.0, 13.0))
                    else:
                        P.bushes(b, (xx, yy), 2.0, count=1, height=(1.5, 4.0), z=z + 1.0)
                for q in range(10):
                    yy = b.rng.uniform(sk0 + 6, sk1 - 6)
                    drop = b.rng.uniform(4.0, 15.0)
                    xx = s * half
                    b.tube([(xx, yy, z), (xx + s * 0.8, yy + 0.4, z - drop * 0.5), (xx + s * 0.5, yy, z - drop)],
                           0.35, radii=[0.5, 0.35, 0.15], mat=MATTE, tint=P.LEAF, sides=5)
        # Growth spilling out of the bare frames onto the ground on the south side.
        for q in range(40):
            xx = hw(-SINK - zc) + b.rng.uniform(-6.0, 40.0)
            yy = b.rng.uniform(sk0 - 30.0, sk1 + 30.0)
            if q % 5 == 0:
                P.tree(b, (xx, yy, 0.0), b.rng.uniform(8.0, 16.0))
            else:
                P.bushes(b, (xx, yy), 3.0, count=1, height=(2.0, 6.0))
        # Front break: cryo bays; back break: plain decks.
        break_decks(b, 480.0, 1, w, h, depth=80.0, cryo=True)
        break_decks(b, -480.0, -1, w, h, depth=70.0)
        camp(b, 480.0, zc)
        b.marker("door", (hw(-SINK + 4.0 - zc) + 1.0, 380.0, 0.0), kind_of="airlock")
        airlock(b, (hw(-SINK + 4.0 - zc) + 0.2, 380.0, 0.0))
        b.zone("radiation", (0, 0, zc), radius=60.0, level=2)


def bow(b):
    """The bow: its torn back reared up, the bridge on its spine with sensor domes and masts, the prow and its dark
    shield, dug into a wave of earth, the whole piece rolled on its side."""
    at, yaw, roll = BOW
    w, h = BW, BH
    stations = []
    y = -340.0
    while y < -265.0:
        stations.append((y, w, h))
        y += 7.0
    y = -265.0
    while y < 140.0:
        stations.append((y, w, h))
        y += 25.0
    for k in range(9):
        f = (k + 1) / 9
        e = math.sqrt(max(0.0, 1 - f * f))
        stations.append((140.0 + 210.0 * f, w * (0.3 + 0.7 * e), h * (0.45 + 0.55 * e)))
    with b.frame(at, (BOW_PITCH, roll, yaw)):
        rings = section(b, stations, back=75.0)
        zc = zc_of(h)
        livery(b, [st for st in stations if -265.0 <= st[0] <= 300.0])
        for y in (-200.0, -60.0):
            collar(b, y, w, h)
        tanks(b, -240.0, 2, w, h, side=1, r=11.0, length=80.0)
        petals(b, -340.0, -1, w, h, count=20)
        flank_detail(b, -260.0, 130.0, w, h, windows=2, hatches=6, panels=24, scorch=0.8)
        # Prow shield: a dark dome over the nose.
        yn, wn, hn = stations[-1]
        b.lathe([(wn * 0.5, 0.0), (wn * 0.47, 7.0), (wn * 0.34, 15.0), (wn * 0.16, 20.0), (0.0, 21.0)],
                (0, yn - 6.0, zc_of(hn)), (-90, 0, 0), mat=PAINT, tint=srgb(0.22, 0.2, 0.19), sides=40)
        b.mesh(rings[-1], [list(range(N))], mat=PAINT, tint=HULL_DARK, solid=False)
        for s in (1, -1):
            for k, wdt in enumerate((14.0, 4.0, 14.0, 14.0, 9.0, 14.0)):
                b.box((0.6, wdt, 18.0), (s * (w / 2 + 0.3), -150.0 + k * 20.0, zc + 4.0), mat=PAINT,
                      tint=srgb(0.18, 0.18, 0.19), solid=False, vary=0)
        # The bridge: stepped blocks on the spine, window bands, sensor domes, masts.
        top = zc + h / 2 + 5.0
        blocks = ((40.0, 76.0, 130.0, 26.0), (62.0, 56.0, 76.0, 20.0), (74.0, 38.0, 38.0, 14.0))
        z = top
        for k, (yy, bw, bd, bh) in enumerate(blocks):
            b.box((bw, bd, bh), (0, yy, z + bh / 2), mat=HULL, tint=HULL_T, bevel=1.0)
            b.box((bw + 0.4, bd + 0.4, 2.2), (0, yy, z + bh * 0.62), mat=GLASS, solid=False, vary=0)
            b.box((bw + 0.5, bd + 0.5, 1.4), (0, yy, z + bh - 0.7), mat=PAINT, tint=ORANGE if k != 1 else TEAL,
                  solid=False, vary=0)
            z += bh
        for x, y0, r in ((-22.0, 10.0, 10.0), (20.0, -6.0, 8.0), (0.0, 110.0, 12.0)):
            b.lathe([(r, 0.0), (r * 0.95, r * 0.35), (r * 0.7, r * 0.75), (0.0, r)], (x, y0, top + 26.0), mat=PANEL,
                    tint=OFFWHITE, sides=24)
        for x, mh in ((-8.0, 80.0), (10.0, 52.0)):
            b.cyl(1.3, mh, (x, 74.0, z + mh / 2), mat=PAINT, tint=GREY, sides=8, radius2=0.5)
            for q in range(3):
                b.box((11.0 - q * 2.5, 0.4, 0.4), (x, 74.0, z + mh * (0.5 + q * 0.18)), mat=PAINT, tint=GREY,
                      solid=False)
            P.aviation_lamp(b, (x, 74.0, z + mh + 0.8), 1.0)
        b.marker("landmark", (-8.0, 74.0, z + 81.0), kind_of="colony-ship", visible_km=50)
        b.marker("loot", (0, 74.0, top + 47.0), table="vault-crate")
        b.marker("door", (0, -26.0, top), kind_of="bridge")
        break_decks(b, -340.0, -1, w, h, depth=70.0)
    # The wave of earth the prow pushed up.
    nose = Vector(at) + Vector((math.cos(math.radians(yaw + 90)), math.sin(math.radians(yaw + 90)), 0)) * 360.0
    P.rock(b, (nose.x + 30.0, nose.y, 0), (170.0, 280.0, 52.0), (0, 0, yaw + 90), tint=P.EARTH, n=8, rough=0.3,
           sink=0.2, solid="hull")
    P.rock(b, (nose.x - 40.0, nose.y + 130.0, 0), (130.0, 100.0, 30.0), (0, 0, 30), tint=P.EARTH, n=6, rough=0.35)
    P.rock(b, (nose.x - 50.0, nose.y - 140.0, 0), (120.0, 90.0, 26.0), (0, 0, -20), tint=P.EARTH, n=6, rough=0.35)


def airlock(b, at):
    """A hull airlock at ground level on the south flank: a heavy frame, the outer door jammed half open, a
    Reclaimer ramp of plates up to it."""
    x, y, z = at
    with b.frame((x, y, z), (0, 0, 90)):
        b.box((12.0, 2.0, 12.0), (0, 0.5, 6.0), mat=PAINT, tint=srgb(0.5, 0.52, 0.53), bevel=0.2, solid=False)
        b.box((7.0, 2.2, 7.5), (0, 0.4, 4.2), mat=MATTE, tint=P.SOOT, solid=False, vary=0)
        b.box((4.0, 0.8, 7.5), (2.5, -0.6, 4.2), mat=PAINT, tint=srgb(0.42, 0.44, 0.45), solid=False)
        for k in range(5):
            b.box((12.4, 2.3, 0.4), (0, 0.4, 1.0 + k * 2.4), mat=PAINT, tint=YELLOW if k % 2 else BLACK, solid=False,
                  vary=0)
        b.box((7.0, 9.0, 0.3), (0, -4.5, 0.25), (-4, 0, 0), mat=PAINT, tint=srgb(0.45, 0.42, 0.38), solid=False)
        lamp(b, (0, -0.8, 9.5), (-30, 0, 0))
        P.sign_board(b, (8.0, -1.2, 0), 0.0, 4.0, 1.6, tint=srgb(0.8, 0.75, 0.6), band=srgb(0.6, 0.2, 0.1))


def camp(b, y_end, zc):
    """The Reclaimers' camp at the habitat section's open end: scrap walls, tarps, a fire, a lift up into the
    decks, lights strung on cables."""
    y = y_end + 30.0
    P.scrap_wall(b, (-60.0, y + 30.0, 0), (-10.0, y + 36.0, 0))
    P.scrap_wall(b, (10.0, y + 36.0, 0), (64.0, y + 28.0, 0))
    P.scrap_wall(b, (64.0, y + 28.0, 0), (70.0, y - 10.0, 0))
    P.scrap_wall(b, (-60.0, y + 30.0, 0), (-66.0, y - 12.0, 0))
    for k, (x, yy) in enumerate(((-36.0, y + 8.0), (-14.0, y + 14.0), (30.0, y + 10.0), (48.0, y - 4.0))):
        P.tarp(b, (x, yy, 0), 7.0, 5.0, 2.6, b.rng.uniform(-20, 20),
               tint=(srgb(0.45, 0.5, 0.35), srgb(0.55, 0.45, 0.3), srgb(0.35, 0.42, 0.5), srgb(0.5, 0.3, 0.25))[k])
    b.cyl(0.35, 0.9, (4.0, y + 4.0, 0.45), mat=RUST, tint=(1, 1, 1), sides=12, solid=False)
    b.box((0.5, 0.5, 0.4), (4.0, y + 4.0, 1.1), mat=LAMP, tint=srgb(1.0, 0.55, 0.2), solid=False, vary=0)
    b.marker("light", (4.0, y + 4.0, 1.6), colour=[1.0, 0.6, 0.25], range=10.0, flicker=True)
    P.string_lights(b, [(-58.0, y + 30.0, 3.2), (-20.0, y + 20.0, 4.0), (20.0, y + 22.0, 4.0), (62.0, y + 28.0, 3.2)])
    for k in range(5):
        b.piece("container-20", (-50.0 + k * 22.0, y - 22.0 + (k % 2) * 6.0, 0), (0, 0, b.rng.uniform(-30, 30)),
                tint=(ORANGE, TEAL, srgb(0.5, 0.2, 0.15), OFFWHITE, ORANGE)[k])
    # A lift tower of scaffold up to the third deck, a ramp of plates up to the first.
    from monkit.kit import lattice_tower, ladder
    lattice_tower(b, (24.0, y_end + 8.0, 0), 28.0, 3.0, 3.0, sections=7, member=0.12, tint=srgb(0.55, 0.5, 0.4))
    ladder(b, (24.0, y_end + 9.8, 0), 28.0, (0, 0, 180), cage=True)
    b.box((14.0, 30.0, 0.5), (-20.0, y_end + 12.0, 3.5), (-14, 0, 0), mat=PAINT, tint=srgb(0.45, 0.42, 0.38),
          solid=True)
    P.door(b, (-5.0, y_end + 0.4, 2.45), 180.0, light=True, sign=False, step=False)
    for k in range(6):
        b.marker("loot", (-40.0 + k * 16.0, y + b.rng.uniform(-6, 12), 0.1), table="barrel" if k % 2 else "crate")
    b.marker("guard", (0, y + 20.0, 0), role="reclaimer")
    b.marker("guard", (40.0, y + 6.0, 0), role="reclaimer")


# ------------------------------------------------------------------------------------------------ the field


def piece_axis(spec, y):
    at, yaw, _ = spec
    d = Vector((-math.sin(math.radians(yaw)), math.cos(math.radians(yaw)), 0))
    return Vector(at) + d * y, d


def furrow_and_field(b):
    """The furrow behind the stern, berms along the pieces, earth and wreckage between them."""
    s0, d0 = piece_axis(STERN, -330.0)
    # The furrow: two kilometres of berms and scraped ground behind the stern, curving slightly.
    path = []
    for k in range(18):
        t = k / 17
        p = s0 - d0 * (2000.0 * t)
        p += Vector((-d0.y, d0.x, 0)) * (120.0 * t * t)
        path.append(p)
    for side in (-1, 1):
        berm = []
        for i, p in enumerate(path):
            t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
            n = Vector((-t.y, t.x, 0))
            berm.append(tuple(p + n * side * (140.0 - 50.0 * i / len(path))))
        P.mound_along(b, berm, width=80.0, height=16.0, tint=P.EARTH, rough=0.4, taper_ends=True, step=25.0)
    for i in range(len(path) - 1):
        a, c = path[i], path[i + 1]
        t = (c - a).normalized()
        n = Vector((-t.y, t.x, 0))
        wdt = 230.0 - 90.0 * i / len(path)
        q = [a - n * wdt / 2, a + n * wdt / 2, c + n * wdt / 2, c - n * wdt / 2]
        b.slab([(p.x, p.y) for p in q], 0.1, (0, 0, 0.0), mat=CONCRETE, tint=P.EARTH_DARK, solid=False, wear=0.9)
    for k in range(30):
        p = path[b.rng.randrange(len(path))]
        off = Vector((b.rng.uniform(-90, 90), b.rng.uniform(-90, 90), 0))
        P.rock(b, tuple(p + off), (b.rng.uniform(8, 30), b.rng.uniform(8, 24), b.rng.uniform(3, 10)),
               (0, 0, b.rng.uniform(0, 360)), tint=P.EARTH, n=4, rough=0.35)
    # Berms along each piece: the earth it pushed aside as it slid.
    for spec, half, wd in ((STERN, 320.0, SW), (MID, 480.0, MW), (BOW, 300.0, BW)):
        for side in (-1, 1):
            pts = []
            for k in range(9):
                y = -half + 2 * half * k / 8
                p, d = piece_axis(spec, y)
                n = Vector((-d.y, d.x, 0))
                pts.append(tuple(p + n * side * (wd / 2 + 26.0)))
            P.mound_along(b, pts, width=50.0, height=12.0, tint=P.EARTH, rough=0.4, taper_ends=True, step=25.0)
    # Scraped ground under and between the pieces.
    a, _ = piece_axis(STERN, -320.0)
    c, _ = piece_axis(BOW, 350.0)
    d = (c - a).normalized()
    n = Vector((-d.y, d.x, 0))
    q = [a - d * 60 - n * 190, a - d * 60 + n * 190, c + d * 120 + n * 190, c + d * 120 - n * 190]
    b.slab([(p.x, p.y) for p in q], 0.08, (0, 0, 0.0), mat=CONCRETE, tint=P.EARTH_DARK, solid=False, wear=0.9)
    # Wreckage in the gaps and round the field.
    for g0, g1 in ((piece_axis(STERN, 320.0)[0], piece_axis(MID, -480.0)[0]),
                   (piece_axis(MID, 480.0)[0], piece_axis(BOW, -340.0)[0])):
        mid_pt = (g0 + g1) / 2
        span = (g1 - g0).length
        for k in range(14):
            p = g0.lerp(g1, b.rng.random()) + Vector((b.rng.uniform(-80, 80), b.rng.uniform(-80, 80), 0))
            pl = b.rng.uniform(14.0, 46.0)
            b.box((pl, pl * b.rng.uniform(0.3, 0.6), 1.2), (p.x, p.y, b.rng.uniform(2.0, 8.0)),
                  (b.rng.uniform(-40, 40), b.rng.uniform(-30, 30), b.rng.uniform(0, 180)), mat=HULL,
                  tint=b.rng.choice((HULL_T, HULL_T, HULL_DARK)), solid=pl > 30)
        for k in range(7):
            p = g0.lerp(g1, b.rng.random()) + Vector((b.rng.uniform(-60, 60), b.rng.uniform(-60, 60), 0))
            d = Vector((b.rng.uniform(-1, 1), b.rng.uniform(-1, 1), b.rng.uniform(0.05, 0.5))).normalized()
            L = b.rng.uniform(25.0, 60.0)
            kink = p + d * L * 0.55 + Vector((0, 0, b.rng.uniform(2, 8)))
            b.beam(tuple(p), tuple(kink), 2.2, 3.0, mat=PAINT, tint=srgb(0.32, 0.3, 0.28))
            d2 = (d + Vector((b.rng.uniform(-0.6, 0.6), b.rng.uniform(-0.6, 0.6), -0.6))).normalized()
            b.beam(tuple(kink), tuple(kink + d2 * L * 0.45), 2.2, 3.0, mat=PAINT, tint=srgb(0.32, 0.3, 0.28))
        P.debris(b, (mid_pt.x, mid_pt.y), span * 0.6, count=90, size=(1.5, 10.0),
                 tints=(HULL_T, HULL_DARK, DARK), mats=(HULL, PAINT, RUST))
        for k in range(6):
            p = g0.lerp(g1, b.rng.random()) + Vector((b.rng.uniform(-50, 50), b.rng.uniform(-50, 50), 0))
            b.piece("container-20", (p.x, p.y, 0.0), (b.rng.uniform(-6, 6), b.rng.uniform(-6, 6),
                                                     b.rng.uniform(0, 360)),
                    tint=b.rng.choice((ORANGE, TEAL, OFFWHITE, srgb(0.5, 0.2, 0.15))))
        P.scorch(b, (mid_pt.x, mid_pt.y, 0.12), span * 0.4)
    P.debris(b, (0.0, 0.0), 1500.0, count=160, size=(1.0, 8.0), tints=(HULL_T, HULL_DARK, DARK),
             mats=(HULL, PAINT, RUST))
    for k in range(12):
        a = b.rng.random() * math.tau
        r = b.rng.uniform(300, 1300)
        b.piece("cryo-rack", (math.cos(a) * r, math.sin(a) * r, 0.0), (b.rng.uniform(-50, 50), b.rng.uniform(-20, 20),
                                                                     b.rng.uniform(0, 360)))


def build(b):
    a, _ = piece_axis(STERN, -320.0)
    c, _ = piece_axis(BOW, 350.0)
    d = (c - a).normalized()
    n = Vector((-d.y, d.x, 0))
    pad = [a - d * 40 - n * 170, c + d * 100 - n * 170, c + d * 100 + n * 170, a - d * 40 + n * 170]
    b.set_pad(("poly", [(round(p.x, 1), round(p.y, 1)) for p in pad]), ground="dirt", blend=80.0, clear=30.0)
    stern(b)
    mid(b)
    bow(b)
    furrow_and_field(b)
    b.zone("no-build", (0, 0, 0), radius=1700.0)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 50})


SUN = (24.0, 150.0)
FOG = 0.0001
GROUND = 80000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (-250, 0, 110), "distance": 7200, "yaw": -62, "pitch": 1.0, "lens": 85, "fog": 0.00009},
    {"name": "overview", "target": (-120, 0, 50), "distance": 3100, "yaw": 26, "pitch": 21, "lens": 30,
     "fog": 0.00006},
    {"name": "foot", "target": (291.6, 103.1, 56.8), "distance": 200, "yaw": 41.9, "pitch": -16, "lens": 16,
     "fog": 0.00004, "people": [((408.0, -27.0, 0), 230.0), ((411.0, -29.0, 0), 160.0)],
     "ship": ((398.0, -36.0, 0), 80.0)},
]
