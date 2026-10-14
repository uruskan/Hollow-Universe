"""Ring Fall (MONUMENTS.md §5.5, landmark tier): a kilometre of an orbital habitat ring that came down in the
Quiet. The fragment is a curved band of the ring's tube, 170 m wide and 100 m deep, an arc of a ring 7 km across,
lying on its outer hull tilted 38 degrees, so its sky windows face up the slope of it and its inner wall's wounds
open on the habitat floor inside: terraces of homes and trees gone wild. Both ends are torn off, decks hanging out
of them; the stump of a spoke stands 360 m out of its middle into the sky. Smaller pieces of the ring, plating,
frames, containers and cryo racks lie strewn for two kilometres round it, with the earth it threw up.
"""
import math

from mathutils import Matrix, Vector

from monkit import parts_landmarks as P
from monkit.kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, PAINT, TEAL, YELLOW, railing,
                        srgb)
from monkit.surfaces import CONCRETE, CORRUGATED, GLASS, GRATE, HULL, LAMP, MATTE, PANEL, RUST

KEY = "ring-fall"
TITLE = "Ring Fall"
SEED = 5501
BUILDER = {"chunk": 160.0, "max_edge": 16.0, "lod_min": (0.0, 2.0, 16.0)}
LODS = [450.0, 2800.0, 80000.0]

R = 3500.0              # the ring's radius
ARC = 1000.0            # the fragment's length along it
A, C = 50.0, 85.0       # half the tube's depth (radial) and half its width (axial)
TILT = -38.0            # roll about the chord: the sky windows face up
LIFT = 76.0             # the frame's height (its lowest edge sinks into the ground)
YAW = 14.0
N = 44
HULL_T = srgb(0.80, 0.80, 0.78)
HULL_DARK = srgb(0.36, 0.37, 0.38)
TEARS = ((-0.105, -0.07), (-0.01, 0.035), (0.075, 0.1))


def centre(phi):
    return Vector((R * math.sin(phi), R * math.cos(phi) - R, 0.0))


def rhat(phi):
    return Vector((math.sin(phi), math.cos(phi), 0.0))


def that(phi):
    return Vector((math.cos(phi), -math.sin(phi), 0.0))


def section(phi, a=A, c=C, p=3.2, n=N):
    """The tube's cross-section at angle phi: a rounded box `a` deep (along the radius) and `c` wide (axial)."""
    o, r, z = centre(phi), rhat(phi), Vector((0, 0, 1))
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        cu, cv = math.cos(t), math.sin(t)
        u = math.copysign(abs(cu) ** (2 / p), cu) * a
        v = math.copysign(abs(cv) ** (2 / p), cv) * c
        pts.append(tuple(o + r * u + z * v))
    return pts


def frame_at(phi):
    """(origin, euler degrees) of a frame at phi with x = axial (up the band), y = along the arc, z = outward."""
    x, y, z = Vector((0, 0, 1)), that(phi), rhat(phi)
    m = Matrix((x, y, z)).transposed()
    return tuple(centre(phi)), tuple(math.degrees(e) for e in m.to_euler())


def fragment(b):
    """The main fragment: its hull lofted along the arc, sky windows and livery following it, wounds in the inner
    wall, ribs and an inner skin round them, the habitat floor with homes and trees seen through them."""
    phi0 = ARC / R / 2
    stations = [-phi0 + 2 * phi0 * i / 60 for i in range(61)]
    rings = [section(ph) for ph in stations]
    seed = b.rng.randrange(1 << 20)
    inner_k = set(range(N // 2 - 7, N // 2 + 8))

    def torn(ph):
        return any(lo <= ph <= hi for lo, hi in TEARS)

    def holes(i, j):
        ph = (stations[i] + stations[i + 1]) / 2
        if torn(ph) and j in inner_k:
            return P._hash(i, j, 3, seed) < 0.85
        end = min(ph + phi0, phi0 - ph) * R
        if end < 60.0:
            return P._hash(i, j, 5, seed) < 0.75 * (1 - end / 60.0) ** 1.2
        return P._hash(i, j, 9, seed) < 0.015
    P.hull_loft(b, rings, mat=HULL, tint=HULL_T, holes=holes, ragged=(16.0, 16.0))
    inner = [section(ph, A - 2.5, C - 2.5) for ph in stations]
    P.hull_loft(b, inner, mat=PAINT, tint=HULL_DARK, holes=holes, wear=0.7)
    # Ribs where it is torn or near its ends; longerons.
    for i, ph in enumerate(stations):
        end = min(ph + phi0, phi0 - ph) * R
        if torn(ph) or end < 60.0:
            r = inner[i]
            b.tube(list(r) + [r[0]], 1.0, mat=RUST, tint=(1, 1, 1), sides=6, cap=False)
    for k in range(0, N, 4):
        b.tube([inner[i][k] for i in range(len(stations))], 0.8, mat=PAINT, tint=DARK, sides=6, cap=False)
    # Strips following the hull: sky windows on the inner wall, livery bands, frames every 50 m.
    def strip(k0, k1, grow, tint, mat=PAINT, skip=None, patchy=0.0):
        rs = []
        for ph in stations:
            s = section(ph, A + grow, C + grow)
            rs.append([s[(k0 + q) % N] for q in range(((k1 - k0) % N) + 1)])
        m = len(rs[0])
        verts = [v for r in rs for v in r]
        faces = []
        for i in range(len(rs) - 1):
            ph = (stations[i] + stations[i + 1]) / 2
            if skip and skip(ph):
                continue
            if patchy and P._hash(i, k0, 13, seed) < patchy:
                continue
            for j in range(m - 1):
                a = i * m + j
                faces.append([a, a + 1, a + 1 + m, a + m])
        if faces:
            b.mesh(verts, faces, mat=mat, tint=tint, solid=False, smooth=True, wear=0.4, vary=0.02)
    near_end = lambda ph: torn(ph) or min(ph + phi0, phi0 - ph) * R < 50.0
    for k0 in (N // 2 - 5, N // 2 - 1, N // 2 + 3):
        strip(k0, k0 + 1, 0.4, (1, 1, 1), mat=GLASS, skip=near_end)
    strip(N // 4 - 1, N // 4, 0.45, ORANGE, skip=near_end)
    strip(3 * N // 4, 3 * N // 4 + 1, 0.45, TEAL, skip=near_end)
    strip(N - 3, N + 2, 0.4, srgb(0.42, 0.42, 0.41), skip=near_end)
    strip(3, 7, 0.5, srgb(0.10, 0.09, 0.085), mat=MATTE, skip=near_end, patchy=0.35)
    strip(N - 8, N - 4, 0.5, srgb(0.12, 0.11, 0.10), mat=MATTE, skip=near_end, patchy=0.5)
    strip(N // 4 + 2, N // 4 + 4, 0.5, srgb(0.16, 0.15, 0.14), mat=MATTE, skip=near_end, patchy=0.6)
    for i in range(3, len(stations) - 3, 3):
        s = section(stations[i], A + 1.0, C + 1.0)
        b.tube(list(s) + [s[0]], 0.9, mat=PAINT, tint=srgb(0.6, 0.6, 0.6), sides=6, cap=False)
    # The habitat floor along the outer wall, homes and trees on it.
    for i in range(len(stations) - 1):
        ph = (stations[i] + stations[i + 1]) / 2
        org, eul = frame_at(ph)
        with b.frame(org, eul):
            seg = R * (stations[i + 1] - stations[i]) + 0.5
            b.box((2 * (C - 10.0), seg, 2.0), (0, 0, A - 7.0), mat=CONCRETE, tint=srgb(0.36, 0.38, 0.28), solid=False)
            if not torn(ph):
                continue
            for q in range(5):
                x = b.rng.uniform(-C + 16.0, C - 16.0)
                if b.rng.random() < 0.55:
                    hh = b.rng.uniform(6.0, 20.0)
                    w = b.rng.uniform(8.0, 16.0)
                    b.box((w, b.rng.uniform(8.0, 14.0), hh), (x, b.rng.uniform(-6, 6), A - 8.0 - hh / 2),
                          mat=PANEL, tint=b.rng.choice((OFFWHITE, srgb(0.78, 0.74, 0.66), srgb(0.7, 0.72, 0.7))))
                    b.box((w + 0.4, 0.3, 1.4), (x, b.rng.uniform(-6, 6), A - 8.0 - hh * 0.6), mat=GLASS,
                          solid=False)
                else:
                    P.rock(b, (x, b.rng.uniform(-6, 6), A - 8.0), (b.rng.uniform(6, 12), b.rng.uniform(6, 10),
                           b.rng.uniform(4, 9)), (180, 0, b.rng.uniform(0, 360)), tint=b.rng.choice((P.LEAF, P.MOSS)),
                           n=2, rough=0.35, sink=0.0, mat=MATTE, solid=False, smooth=True, wear=0.2)
    # Torn plating round the wounds, bent outward.
    for lo, hi in TEARS:
        for q in range(14):
            ph = b.rng.uniform(lo, hi)
            org, eul = frame_at(ph)
            with b.frame(org, eul):
                x = b.rng.choice((-1, 1)) * b.rng.uniform(C * 0.4, C * 0.8)
                b.box((b.rng.uniform(8, 16), b.rng.uniform(6, 12), 0.7), (x, b.rng.uniform(-6, 6), -A - 3.0),
                      (b.rng.uniform(-40, 40), b.rng.uniform(-40, 40), 0), mat=HULL, tint=HULL_T, solid=False)


def ends(b):
    """Both torn ends: decks hanging out, bulkheads, torn plates; a Reclaimer way in up a ramp of plating at the
    east end."""
    phi0 = ARC / R / 2
    for e in (-1, 1):
        ph = e * phi0
        org, eul = frame_at(ph)
        with b.frame(org, eul):
            # Decks at constant radius (floors of the ring), torn off at their own lengths.
            for u in range(-36, int(A) - 6, 9):
                for xa in range(-int(C) + 6, int(C) - 6, 28):
                    if b.rng.random() < 0.12:
                        continue
                    out = b.rng.uniform(-24.0, 8.0)
                    L = 70.0 + out
                    yc = -e * 35.0 + e * out / 2
                    b.box((26.0, L, 0.9), (xa + 13.0, yc, u), (e * b.rng.uniform(-8, 2) if out > 2 else 0, 0, 0),
                          mat=PAINT, tint=srgb(0.42, 0.43, 0.44), solid=False)
            for f in (0.4, 0.8):
                b.box((2 * C - 8.0, 1.0, 2 * A - 8.0), (0, -e * 70.0 * f, 0), mat=MATTE,
                      tint=srgb(0.06, 0.06, 0.06) if f > 0.5 else srgb(0.14, 0.14, 0.14), solid=False, vary=0)
            for q in range(20):
                t = 2 * math.pi * q / 20
                x = math.copysign(abs(math.sin(t)) ** 0.62, math.sin(t)) * C
                u = math.copysign(abs(math.cos(t)) ** 0.62, math.cos(t)) * A
                d = Vector((x * 0.02, e * b.rng.uniform(0.4, 1.0), u * 0.02)).normalized()
                p0 = Vector((x, 0, u))
                b.beam(tuple(p0), tuple(p0 + (d + Vector((x, 0, u)).normalized() * 0.6).normalized() *
                                       b.rng.uniform(8, 20)), b.rng.uniform(6, 11), 0.6, mat=HULL,
                       tint=b.rng.choice((HULL_T, HULL_DARK)))


def spoke(b):
    """The stump of a spoke standing out of the fragment's middle into the sky: a tube in a lattice of longerons
    and rings, its mount on the inner wall, its broken end splayed."""
    org, eul = frame_at(0.0)
    with b.frame(org, eul):
        with b.frame((0, 0, -A + 2.0), (180, 0, 0)):
            # Local +z now points inward, out of the inner wall, toward where the hub was.
            b.box((70.0, 70.0, 22.0), (0, 0, 11.0), mat=HULL, tint=srgb(0.7, 0.71, 0.7), bevel=1.0)
            b.box((72.0, 72.0, 3.0), (0, 0, 16.0), mat=PAINT, tint=ORANGE, solid=False, vary=0)
            # Bent at its mount: a crumpled collar, then the spoke leaning back toward the sky.
            b.cyl(20.0, 10.0, (0, 0, 24.0), (0, 18, 0), mat=PAINT, tint=DARK, sides=28)
            for q in range(10):
                a = 2 * math.pi * q / 10
                b.box((9.0, 6.0, 1.0), (math.cos(a) * 19.0, math.sin(a) * 19.0, 27.0),
                      (b.rng.uniform(-30, 30), b.rng.uniform(-30, 30), math.degrees(a)), mat=HULL, tint=HULL_T,
                      solid=False)
        with b.frame((0, 0, -A + 2.0 - 24.0), (180, -40, 0)):
            L = 360.0
            b.cyl(13.0, L, (0, 0, 22.0 + L / 2), mat=HULL, tint=HULL_T, sides=28)
            for k in range(4):
                a = math.radians(45 + 90 * k)
                b.cyl(2.2, L + 10.0, (math.cos(a) * 17.0, math.sin(a) * 17.0, 22.0 + L / 2), mat=PAINT, tint=GREY,
                      sides=8)
            z = 40.0
            q = 0
            while z < 22.0 + L - 10:
                b.cyl(18.5, 2.4, (0, 0, z), mat=PAINT, tint=DARK if q % 4 else ORANGE, sides=28, solid=False, vary=0)
                for k in range(4):
                    a = math.radians(45 + 90 * k)
                    a2 = math.radians(135 + 90 * k)
                    b.beam((math.cos(a) * 17.0, math.sin(a) * 17.0, z), (math.cos(a2) * 17.0, math.sin(a2) * 17.0,
                                                                          z + 24.0), 0.9, 0.9, mat=PAINT, tint=GREY)
                z += 24.0
                q += 1
            top = 22.0 + L
            for k in range(10):
                a = 2 * math.pi * k / 10 + b.rng.uniform(-0.2, 0.2)
                p0 = Vector((math.cos(a) * 12.0, math.sin(a) * 12.0, top - 2.0))
                d = Vector((math.cos(a) * b.rng.uniform(0.3, 0.9), math.sin(a) * b.rng.uniform(0.3, 0.9),
                            b.rng.uniform(0.2, 1.0))).normalized()
                b.beam(tuple(p0), tuple(p0 + d * b.rng.uniform(8, 24)), 1.4, 2.4, mat=HULL, tint=HULL_T)
            P.aviation_lamp(b, (0, 0, top + 1.2), 1.4, reach=4000.0)
            b.marker("landmark", (0, 0, top + 2.0), kind_of="ring-fall", visible_km=50)
        b.marker("loot", (0, 0, -A - 20.0), table="vault-crate")


def solar_wings(b):
    """Broken radiator and solar wings standing off the outer hull, one lying flat on the ground beside it."""
    for ph, ang in ((-0.09, 30.0), (-0.03, -20.0), (0.05, 25.0), (0.11, -35.0)):
        org, eul = frame_at(ph)
        with b.frame(org, eul):
            with b.frame((C * 0.3, 0, A + 1.0), (0, ang, 0)):
                L, H = 110.0, 60.0
                b.box((4.0, L, 5.0), (0, 0, 2.0), mat=PAINT, tint=DARK)
                for i in range(8):
                    y = -L / 2 + L * (i + 0.5) / 8
                    for k in range(3):
                        if b.rng.random() < 0.3:
                            continue
                        b.box((0.7, L / 8 - 1.5, H / 3 - 1.5), (0, y, 6.0 + H * (k + 0.5) / 3), mat=GLASS if ph < 0
                              else PAINT, tint=(1, 1, 1) if ph < 0 else srgb(0.15, 0.16, 0.17), solid=False)
                for i in range(9):
                    y = -L / 2 + L * i / 8
                    b.box((1.6, 1.4, H), (0, y, 6.0 + H / 2), mat=PAINT, tint=GREY, solid=False)


def field(b):
    """Earth thrown up along the fragment, smaller pieces of the ring, plating, frames, containers, cryo racks,
    scorch, out to two kilometres."""
    phi0 = ARC / R / 2
    rot = Matrix.Rotation(math.radians(YAW), 3, "Z")
    M = Matrix.Translation((0, 0, LIFT)) @ Matrix.Rotation(math.radians(YAW), 4, "Z") @         Matrix.Rotation(math.radians(TILT), 4, "X")
    lines = {-1: [], 1: []}
    for i in range(13):
        ph = -phi0 * 0.98 + 1.96 * phi0 * i / 12
        pts = [M @ Vector(p) for p in section(ph)]
        cross = []
        for k in range(N):
            a, c = pts[k], pts[(k + 1) % N]
            if (a.z > 0) != (c.z > 0):
                f = a.z / (a.z - c.z)
                cross.append(a.lerp(c, f))
        if len(cross) >= 2:
            d = rot @ that(ph)
            nrm = Vector((-d.y, d.x, 0))
            cross.sort(key=lambda q: q.dot(nrm))
            lines[-1].append(cross[0] - nrm * 14.0)
            lines[1].append(cross[-1] + nrm * 14.0)
    for side, pts in lines.items():
        if len(pts) > 2:
            P.mound_along(b, [(q.x, q.y, 0.0) for q in pts], width=46.0, height=12.0 if side > 0 else 8.0,
                          tint=P.EARTH, rough=0.45, taper_ends=True, step=20.0)
    # Smaller pieces of the ring.
    for k, (x, y, yaw, L, tilt) in enumerate(((-900.0, 420.0, 40.0, 160.0, -60.0), (780.0, -380.0, -25.0, 110.0, 20.0),
                                              (300.0, 700.0, 80.0, 70.0, -85.0), (-450.0, -620.0, 10.0, 90.0, 40.0))):
        with b.frame((x, y, 0), (0, 0, yaw)):
            with b.frame((0, 0, 30.0 if abs(tilt) < 50 else 55.0), (tilt, 0, 0)):
                ph = L / R / 2
                sts = [-ph + 2 * ph * i / 8 for i in range(9)]
                rs = [section(s, A * 0.8, C * 0.8) for s in sts]
                P.hull_loft(b, rs, mat=HULL, tint=HULL_T, holes=lambda i, j: b.rng.random() < 0.1, ragged=(14, 14))
                ins = [section(s, A * 0.8 - 2.5, C * 0.8 - 2.5) for s in sts]
                P.hull_loft(b, ins, mat=PAINT, tint=HULL_DARK, wear=0.7)
        P.rock(b, (x, y, 0), (L * 0.9, 120.0, 16.0), (0, 0, yaw), tint=P.EARTH, n=5, rough=0.35, sink=0.4)
        P.scorch(b, (x + 30.0, y - 20.0, 0.05), L * 0.6)
    P.debris(b, (0.0, -120.0), 1300.0, count=260, size=(1.5, 12.0), tints=(HULL_T, HULL_DARK, DARK),
             mats=(HULL, PAINT, RUST))
    P.debris(b, (0.0, -200.0), 500.0, count=120, size=(3.0, 18.0), tints=(HULL_T, HULL_T, HULL_DARK),
             mats=(HULL, HULL, PAINT))
    for k in range(40):
        a = b.rng.random() * math.tau
        r = b.rng.uniform(200.0, 1600.0)
        x, y = math.cos(a) * r, math.sin(a) * r - 150.0
        if k % 3 == 0:
            b.piece("cryo-rack", (x, y, 0.0), (b.rng.uniform(-60, 60), b.rng.uniform(-30, 30), b.rng.uniform(0, 360)))
        else:
            b.piece("container-20", (x, y, 0.0), (b.rng.uniform(-10, 10), b.rng.uniform(-10, 10),
                                                 b.rng.uniform(0, 360)),
                    tint=b.rng.choice((ORANGE, TEAL, OFFWHITE, srgb(0.5, 0.2, 0.15))))
    for k in range(9):
        a = b.rng.random() * math.tau
        r = b.rng.uniform(250.0, 1500.0)
        x, y = math.cos(a) * r, math.sin(a) * r - 150.0
        d = Vector((b.rng.uniform(-1, 1), b.rng.uniform(-1, 1), b.rng.uniform(0.0, 0.4))).normalized()
        L = b.rng.uniform(40.0, 90.0)
        P.truss(b, (x, y, 2.0), (x + d.x * L, y + d.y * L, 2.0 + d.z * L), w=6.0, h=6.0, member=0.45,
                tint=srgb(0.6, 0.6, 0.6))
    for k in range(9):
        a = b.rng.random() * math.tau
        r = b.rng.uniform(80.0, 420.0)
        P.scorch(b, (math.cos(a) * r, math.sin(a) * r - 120.0, 0.05), b.rng.uniform(25.0, 70.0),
                 tint=b.rng.choice((srgb(0.17, 0.15, 0.12), srgb(0.22, 0.19, 0.15), srgb(0.12, 0.11, 0.1))),
                 wobble=0.5)
    # A Reclaimer way into the east end: a ramp of plates, a lamp, crates.
    e = 1
    p = rot @ (centre(phi0) + Vector((0, 0, 0)))
    for q in range(4):
        b.box((14.0, 24.0, 0.6), (p.x + 30.0 + q * 2.0, p.y - 40.0 + q * 6.0, 2.0 + q * 4.0), (-12, 4, YAW + 20),
              mat=PAINT, tint=srgb(0.45, 0.42, 0.38), solid=True)
    P.scrap_wall(b, (p.x + 10.0, p.y - 80.0, 0), (p.x + 60.0, p.y - 70.0, 0))
    P.tarp(b, (p.x + 40.0, p.y - 60.0, 0), 7.0, 5.0, 2.6, 20.0)
    P.string_lights(b, [(p.x + 12.0, p.y - 78.0, 3.0), (p.x + 40.0, p.y - 58.0, 3.5), (p.x + 58.0, p.y - 70.0, 3.0)])
    b.marker("door", (p.x + 36.0, p.y - 30.0, 14.0), kind_of="ring-deck")
    for q in range(5):
        b.marker("loot", (p.x + 20.0 + q * 8.0, p.y - 66.0, 0.2), table="crate" if q % 2 else "barrel")


def build(b):
    yaw = math.radians(YAW)
    pad = [(x * math.cos(yaw) - y * math.sin(yaw), x * math.sin(yaw) + y * math.cos(yaw))
           for x, y in ((-680.0, -330.0), (680.0, -330.0), (680.0, 200.0), (-680.0, 200.0))]
    b.set_pad(("poly", [(round(x, 1), round(y, 1)) for x, y in pad]), ground="dirt", blend=80.0, clear=20.0)
    with b.frame((0, 0, LIFT), (0, 0, YAW)):
        with b.frame((0, 0, 0), (TILT, 0, 0)):
            fragment(b)
            ends(b)
            spoke(b)
            solar_wings(b)
    field(b)
    b.zone("no-build", (0, 0, 0), radius=1800.0)
    b.zone("radiation", (0, 0, 40.0), radius=120.0, level=2)
    b.meta.update({"tier": "landmark", "landmark": True, "spawn": "region", "visible_km": 50})


SUN = (24.0, 150.0)
FOG = 0.0001
GROUND = 80000.0
PREVIEW_AO = False
VIEWS = [
    {"name": "far", "target": (0, 0, 140), "distance": 7500, "yaw": -18, "pitch": 1.6, "lens": 85, "fog": 0.00009},
    {"name": "overview", "target": (0, -60, 60), "distance": 2200, "yaw": -26, "pitch": 26, "lens": 30,
     "fog": 0.00005},
    {"name": "foot", "target": (472.9, 100.3, 76.6), "distance": 200, "yaw": 64.3, "pitch": -22, "lens": 16,
     "fog": 0.00003, "people": [((628.0, 24.0, 0.0), 250.0), ((630.5, 22.0, 0.0), 190.0)],
     "ship": ((618.0, 30.0, 0.0), 80.0)},
]
