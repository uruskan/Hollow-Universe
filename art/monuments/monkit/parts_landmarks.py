"""Parts for the landmark monuments (MONUMENTS.md §5.5): rock and earth masses, debris and scorch, human-scale doors
and stairs, big Seedward halls and hangars, fortress walls, trusses, and the fixed pieces the game lays along the
landmarks' long lines (the fallen tether's segments, the mass driver's rail and pylons). Every part adds primitives
to a builder in its current frame (metres, +Z up, front toward -Y, angles in degrees), like `kit` and `structures`.

The fixed pieces are registered in `kit.PIECES` when this module is imported, so a site imports it before it calls
`b.piece`.
"""
import math

from mathutils import Matrix, Vector

from . import kit
from .kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED_LAMP, TEAL, WARM_LAMP, YELLOW,
                  ladder, lamp, railing, srgb, stairs)
from .surfaces import (ASPHALT, CONCRETE, CORRUGATED, FORMWORK, GLASS, GRATE, HULL, LAMP, MATTE, PAINT, PANEL,
                       RUST)

# Colours the landmarks add to the kit's (sRGB, stored linear).
CARBON = srgb(0.15, 0.155, 0.16)      # the tether's composite strands
EARTH = srgb(0.40, 0.33, 0.25)        # turned soil, spoil
EARTH_DARK = srgb(0.27, 0.22, 0.17)
ROCK = srgb(0.56, 0.53, 0.49)         # grey-brown rock (on the concrete texture)
ROCK_DARK = srgb(0.40, 0.38, 0.35)
SOOT = srgb(0.07, 0.065, 0.06)
STEEL = srgb(0.50, 0.52, 0.54)
WARM_GREY = srgb(0.66, 0.64, 0.60)
HULL_GREY = srgb(0.70, 0.71, 0.70)
MOSS = srgb(0.30, 0.36, 0.20)
LEAF = srgb(0.22, 0.32, 0.14)


# ------------------------------------------------------------------------------------------------ noise


def _hash(ix, iy, iz, seed):
    h = (ix * 374761393 + iy * 668265263 + iz * 1274126177 + seed * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 3266489917) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0xFFFFFF)


def vnoise(x, y, z, seed=0):
    """Smooth value noise in [0, 1]."""
    ix, iy, iz = math.floor(x), math.floor(y), math.floor(z)
    fx, fy, fz = x - ix, y - iy, z - iz
    ux, uy, uz = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy), fz * fz * (3 - 2 * fz)

    def c(dx, dy, dz):
        return _hash(ix + dx, iy + dy, iz + dz, seed)
    x00 = c(0, 0, 0) + (c(1, 0, 0) - c(0, 0, 0)) * ux
    x10 = c(0, 1, 0) + (c(1, 1, 0) - c(0, 1, 0)) * ux
    x01 = c(0, 0, 1) + (c(1, 0, 1) - c(0, 0, 1)) * ux
    x11 = c(0, 1, 1) + (c(1, 1, 1) - c(0, 1, 1)) * ux
    y0 = x00 + (x10 - x00) * uy
    y1 = x01 + (x11 - x01) * uy
    return y0 + (y1 - y0) * uz


def fbm(x, y, z, seed=0, octaves=4):
    """Fractal value noise in about [0, 1]."""
    total, amp, norm, f = 0.0, 0.5, 0.0, 1.0
    for k in range(octaves):
        total += vnoise(x * f, y * f, z * f, seed + 31 * k) * amp
        norm += amp
        amp *= 0.5
        f *= 2.03
    return total / norm


# ------------------------------------------------------------------------------------------------ rock and earth


def _cube_sphere(n):
    """A cube subdivided n x n per face, projected on the unit sphere: (verts, quads)."""
    index = {}
    verts = []
    faces = []

    def vid(key):
        if key not in index:
            v = Vector(key)
            index[key] = len(verts)
            verts.append(v.normalized())
        return index[key]
    for axis in range(3):
        for sign in (-1, 1):
            u_ax, v_ax = [a for a in range(3) if a != axis]
            for i in range(n):
                for j in range(n):
                    quad = []
                    for di, dj in ((0, 0), (1, 0), (1, 1), (0, 1)):
                        k = [0, 0, 0]
                        k[axis] = sign * n
                        k[u_ax] = -n + 2 * (i + di)
                        k[v_ax] = -n + 2 * (j + dj)
                        quad.append(vid(tuple(k)))
                    faces.append(quad if sign > 0 else quad[::-1])
    return verts, faces


def rock(b, at, size, rot=(0, 0, 0), tint=ROCK, n=6, rough=0.32, freq=1.6, sink=0.25, seed=None, mat=CONCRETE,
         solid="hull", smooth=False, flat_top=None, wear=0.6):
    """A rough rock (or earth) mass `size` = (x, y, z) across, its base cut flat `sink` of its height below z = 0 of
    its frame; `flat_top` (fraction of height) planes off its top, for benches and abutments."""
    seed = b.rng.randrange(1 << 20) if seed is None else seed
    verts, faces = _cube_sphere(n)
    sx, sy, sz = size
    out = []
    for v in verts:
        d = 1.0 + rough * (fbm(v.x * freq + 7.1, v.y * freq + 3.3, v.z * freq + 1.7, seed, 4) - 0.5) * 2.0
        p = Vector((v.x * sx / 2 * d, v.y * sy / 2 * d, v.z * sz / 2 * d))
        p.z += sz / 2 * (1 - 2 * sink)
        if p.z < 0:
            p.z = -0.3
        if flat_top is not None and p.z > sz * flat_top:
            p.z = sz * flat_top + (p.z - sz * flat_top) * 0.08
        out.append(tuple(p))
    return b.mesh(out, faces, at, rot, mat=mat, tint=tint, solid=solid, smooth=smooth, wear=wear, vary=0.06)


def mound_along(b, path, width=8.0, height=2.5, tint=EARTH, seed=None, rough=0.35, mat=CONCRETE, solid=False,
                taper_ends=False, step=4.0):
    """A heap (berm, spoil bank) along `path` (points on the ground): a noisy rounded profile lofted along it,
    closed underneath. With `taper_ends` it runs out to nothing at both ends; otherwise both end profiles are the
    same clean shape, so lengths laid end to end meet."""
    seed = b.rng.randrange(1 << 20) if seed is None else seed
    pts = [Vector(p) for p in resample(path, step)]
    m = 9
    rings = []
    total = sum((pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)) or 1.0
    s = 0.0
    for i, p in enumerate(pts):
        if i > 0:
            s += (pts[i] - pts[i - 1]).length
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)])
        t.z = 0
        t = t.normalized() if t.length > 1e-6 else Vector((1, 0, 0))
        side = Vector((-t.y, t.x, 0))
        env = math.sin(math.pi * s / total)
        k = env if taper_ends else 1.0
        amp = rough * (env if not taper_ends else 1.0)
        ring = []
        for j in range(m):
            x = -1 + 2 * j / (m - 1)
            n = fbm(s * 0.08 + j * 0.37, j * 0.5, 0.0, seed, 3) - 0.5
            hgt = height * k * max(0.0, (1 - x * x)) ** 0.7 * (1 + amp * 2 * n)
            off = side * (x * width / 2 * (0.6 + 0.4 * k)) + side * (amp * 0.8 * n)
            q = p + off
            ring.append((q.x, q.y, p.z + max(-0.3, hgt - 0.3)))
        rings.append(ring)
    verts = [v for r in rings for v in r]
    faces = []
    for i in range(len(rings) - 1):
        a0, a1 = i * m, (i + 1) * m
        for j in range(m - 1):
            faces.append([a0 + j, a0 + j + 1, a1 + j + 1, a1 + j])
        faces.append([a0 + m - 1, a0, a1, a1 + m - 1])  # underside
    faces.append(list(range(m)))
    last = (len(rings) - 1) * m
    faces.append([last + j for j in range(m)][::-1])
    return b.mesh(verts, faces, mat=mat, tint=tint, solid=solid, smooth=True, wear=0.7, vary=0.04)


def scorch(b, at, radius, tint=SOOT, seed=None, z=0.02, wobble=0.35):
    """A burnt patch on the ground (or on a roof at height `at.z`): a thin irregular dark slab."""
    seed = b.rng.randrange(1 << 20) if seed is None else seed
    k = 18
    outline = []
    for i in range(k):
        a = 2 * math.pi * i / k
        r = radius * (1 + wobble * 2 * (fbm(math.cos(a) * 1.3, math.sin(a) * 1.3, 0.5, seed, 3) - 0.5))
        outline.append((at[0] + math.cos(a) * r, at[1] + math.sin(a) * r))
    return b.slab(outline, 0.03, (0, 0, at[2] + z), mat=MATTE, tint=tint, solid=False, vary=0.1)


def debris(b, centre, radius, count=20, size=(0.6, 5.0), tints=(OFFWHITE, HULL_GREY, DARK), mats=(HULL, PAINT, RUST),
           z=0.0, plates=0.55, beams=0.3, sink=0.3):
    """Wreckage strewn round `centre`: bent plates, beams and blocks, half sunk into the ground."""
    cx, cy = centre[0], centre[1]
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        s = size[0] + (size[1] - size[0]) * b.rng.random() ** 2
        x, y = cx + math.cos(a) * r, cy + math.sin(a) * r
        roll = b.rng.random()
        k = b.rng.randrange(len(mats))
        mat, tint = mats[k], tints[k % len(tints)]
        rot = (b.rng.uniform(-35, 35), b.rng.uniform(-35, 35), b.rng.uniform(0, 360))
        if roll < plates:
            b.box((s, s * b.rng.uniform(0.4, 0.9), max(0.06, s * 0.03)), (x, y, z + s * 0.12 - sink * s * 0.1), rot,
                  mat=mat, tint=tint, solid=s > 3.0, vary=0.08)
        elif roll < plates + beams:
            w = max(0.15, s * 0.06)
            b.box((w, w * 1.6, s * 1.4), (x, y, z + w), (b.rng.uniform(70, 110), rot[1] * 0.3, rot[2]), mat=mat,
                  tint=tint, solid=False, vary=0.08)
        else:
            b.box((s * 0.5, s * 0.4, s * 0.35), (x, y, z + s * 0.1), rot, mat=mat, tint=tint, solid=s > 2.0,
                  vary=0.08, bevel=0.02)


def rubble_heap(b, at, radius, height, tint=CONCRETE_T, count=30, mat=CONCRETE, seed=None):
    """A heap of broken concrete: a low earth-like core and slabs and blocks on it."""
    rock(b, at, (radius * 2, radius * 1.7, height), (0, 0, b.rng.uniform(0, 360)), tint=tint, n=4, rough=0.3,
         sink=0.35, mat=mat, seed=seed)
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        hz = height * 0.55 * max(0.0, 1 - (r / radius) ** 2)
        s = 0.6 + 2.8 * b.rng.random() ** 2
        b.box((s, s * b.rng.uniform(0.5, 1.0), s * b.rng.uniform(0.15, 0.45)),
              (at[0] + math.cos(a) * r, at[1] + math.sin(a) * r, at[2] + hz),
              (b.rng.uniform(-40, 40), b.rng.uniform(-40, 40), b.rng.uniform(0, 360)), mat=mat, tint=tint,
              solid=False, vary=0.1)
    for _ in range(count // 5):
        a = b.rng.random() * math.tau
        r = radius * 0.8 * math.sqrt(b.rng.random())
        p0 = Vector((at[0] + math.cos(a) * r, at[1] + math.sin(a) * r, at[2] + height * 0.3))
        d = Vector((b.rng.uniform(-1, 1), b.rng.uniform(-1, 1), b.rng.uniform(0.2, 1.0))).normalized()
        b.beam(p0, p0 + d * b.rng.uniform(2.0, 6.0), 0.05, 0.05, mat=RUST, tint=(1, 1, 1))  # rebar


# ------------------------------------------------------------------------------------------------ human scale


def door(b, at, yaw=0.0, w=1.2, h=2.3, tint=TEAL, frame_tint=DARK, light=True, sign=True, step=True, marker=None,
         hood=True):
    """A personnel door in a wall whose outside face is at `at` (the door faces -Y of the turned frame): frame,
    leaf, hood, lamp, a sign plate, a step. `marker` names the door's kind for a gameplay door marker."""
    with b.frame(at, (0, 0, yaw)):
        b.box((w + 0.3, 0.3, h + 0.2), (0, -0.05, (h + 0.2) / 2), mat=PAINT, tint=frame_tint, solid=False, vary=0)
        b.box((w, 0.08, h), (0, -0.22, h / 2), mat=PAINT, tint=tint, solid=False, bevel=0.01)
        b.box((0.05, 0.06, 0.25), (w * 0.38, -0.29, 1.05), mat=PAINT, tint=GREY, solid=False, vary=0)
        if hood:
            b.box((w + 1.2, 1.0, 0.14), (0, -0.6, h + 0.45), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        if light:
            lamp(b, (0, -0.3, h + 0.28), (-25, 0, 0), size=(0.36, 0.2, 0.12))
        if sign:
            b.box((0.62, 0.03, 0.36), (w / 2 + 0.55, -0.22, 1.65), mat=PAINT, tint=YELLOW, solid=False, vary=0)
            b.box((0.4, 0.035, 0.06), (w / 2 + 0.55, -0.24, 1.65), mat=PAINT, tint=BLACK, solid=False, vary=0)
        if step:
            b.box((w + 0.9, 1.1, 0.18), (0, -0.7, 0.09), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        if marker:
            b.marker("door", (0, -0.9, 0), kind_of=marker)


def bay_door(b, at, yaw, w, h, open_frac=0.3, tint=srgb(0.55, 0.57, 0.6), frame_tint=DARK, stripes_on=True):
    """A vehicle door (roller shutter) `w` x `h` in a wall face at `at` facing -Y, partly open: a dark opening, the
    shutter, a frame with hazard-striped jambs, bollards."""
    with b.frame(at, (0, 0, yaw)):
        b.box((w, 0.2, h), (0, 0.05, h / 2), mat=MATTE, tint=SOOT, solid=False, vary=0)
        shut = h * (1 - open_frac)
        b.box((w, 0.12, shut), (0, -0.12, h - shut / 2), mat=CORRUGATED, tint=tint, solid=False)
        b.box((w + 1.0, 0.5, 0.8), (0, -0.2, h + 0.4), mat=PAINT, tint=frame_tint, solid=False)
        for s in (-1, 1):
            b.box((0.5, 0.45, h), (s * (w / 2 + 0.25), -0.2, h / 2), mat=PAINT, tint=frame_tint, solid=False)
            if stripes_on:
                for k in range(int(min(h, 3.0) / 0.5)):
                    b.box((0.52, 0.47, 0.25), (s * (w / 2 + 0.25), -0.2, 0.125 + k * 0.5),
                          mat=PAINT, tint=YELLOW, solid=False, vary=0)
            b.cyl(0.14, 1.0, (s * (w / 2 + 0.9), -1.2, 0.5), mat=PAINT, tint=YELLOW, sides=10, solid=False)


def stair_tower(b, at, yaw, height, width=1.2, landing=1.4, tint=YELLOW, frame_tint=DARK, cage=True):
    """An external switchback stair from the ground to `height`: flights of 0.19 m risers, landings every 3.6 m, a
    steel frame round it. Its footprint runs along +X of the turned frame (about 7 m by 4 m)."""
    flight_rise = 3.6
    flights = max(1, int(math.ceil(height / flight_rise)))
    rise = height / flights
    run = rise / 0.19 * 0.26
    w2 = width * 2 + 0.3
    with b.frame(at, (0, 0, yaw)):
        for i in range(flights):
            z = i * rise
            y = -width / 2 - 0.15 if i % 2 == 0 else width / 2 + 0.15
            if i % 2 == 0:
                stairs(b, (landing, y, z), (0, 0, -90), width, rise, run, tint=tint, rails=False)
            else:
                stairs(b, (landing + run, y, z), (0, 0, 90), width, rise, run, tint=tint, rails=False)
            zl = z + rise
            xl = landing + run + landing / 2 if i % 2 == 0 else landing / 2
            b.box((landing, w2, 0.08), (xl, 0, zl - 0.04), mat=GRATE, tint=GREY, solid=False, vary=0)
            b.collide_box(Vector((xl, 0, zl - 0.05)), (landing, w2, 0.1))
        L = landing * 2 + run
        for x in (0.0, L):
            for y in (-w2 / 2, w2 / 2):
                b.box((0.16, 0.16, height + 1.2), (x, y, (height + 1.2) / 2), mat=PAINT, tint=frame_tint,
                      solid=False, vary=0)
        for k in range(1, flights + 1):
            z = k * rise + 1.1
            for y in (-w2 / 2, w2 / 2):
                b.box((L, 0.06, 0.06), (L / 2, y, z), mat=PAINT, tint=tint, solid=False, vary=0)
            for x in (0.0, L):
                b.box((0.06, w2, 0.06), (x, 0, z), mat=PAINT, tint=tint, solid=False, vary=0)
        if cage:
            for k in range(flights):
                z = k * rise + rise * 0.5
                for y in (-w2 / 2, w2 / 2):
                    b.beam((0, y, z - rise * 0.5), (L, y, z + rise * 0.5), 0.07, 0.07, mat=PAINT, tint=frame_tint)


def aviation_lamp(b, at, size=1.0, blink=1.5, reach=2000.0):
    """A red obstruction lamp on a small bracket, with its blinking light marker."""
    x, y, z = at
    b.box((size * 0.7, size * 0.7, size * 0.5), (x, y, z - size * 0.6), mat=PAINT, tint=DARK, solid=False, vary=0)
    b.box((size, size, size * 0.8), (x, y, z), mat=LAMP, tint=RED_LAMP, solid=False, vary=0)
    b.marker("light", (x, y, z + size * 0.5), colour=[1.0, 0.12, 0.05], range=reach, blink=blink)


def sign_board(b, at, yaw, w, h, tint=OFFWHITE, text_tint=DARK, band=ORANGE, posts=True, rows=2):
    """A painted sign on posts (or on a wall): a board, a livery stripe and stencilled blocks for lettering."""
    with b.frame(at, (0, 0, yaw)):
        base = 0.0
        if posts:
            for s in (-1, 1):
                b.box((0.12, 0.12, h + 1.6), (s * w * 0.35, 0.1, (h + 1.6) / 2), mat=PAINT, tint=GREY, solid=False,
                      vary=0)
            base = 1.6
        b.box((w, 0.08, h), (0, 0, base + h / 2), mat=PAINT, tint=tint, solid=False, vary=0)
        b.box((w, 0.09, h * 0.16), (0, 0, base + h * 0.1), mat=PAINT, tint=band, solid=False, vary=0)
        for r in range(rows):
            zz = base + h * (0.72 - r * 0.28)
            x = -w * 0.42
            while x < w * 0.38:
                lw = w * b.rng.uniform(0.04, 0.09)
                b.box((lw, 0.1, h * 0.14), (x + lw / 2, -0.01, zz), mat=PAINT, tint=text_tint, solid=False, vary=0)
                x += lw + w * 0.025


def light_mast(b, at, height=24.0, heads=4, tint=GREY):
    """A tall flood-light mast: a tapered pole and a ring of lamps."""
    x, y, z = at
    b.box((1.2, 1.2, 0.8), (x, y, z + 0.2), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    b.cyl(0.32, height, (x, y, z + height / 2), mat=PAINT, tint=tint, sides=10, radius2=0.18, solid=False)
    b.cyl(1.4, 0.2, (x, y, z + height), mat=GRATE, tint=GREY, sides=12, solid=False)
    for k in range(heads):
        a = 360.0 * k / heads
        r = math.radians(a)
        lamp(b, (x + math.cos(r) * 1.1, y + math.sin(r) * 1.1, z + height + 0.4), (-20, 0, a + 90),
             size=(0.7, 0.35, 0.5))


# ------------------------------------------------------------------------------------------------ steel


def truss(b, a, c, w=3.0, h=3.0, member=0.3, panel=None, tint=GREY, mat=PAINT, chords=None, solid=False):
    """A box truss from `a` to `c`: four chords, verticals and diagonals in every panel (section `w` wide, `h`
    deep)."""
    a, c = Vector(a), Vector(c)
    d = c - a
    L = d.length
    if L < 1e-6:
        return
    t = d.normalized()
    up = Vector((0, 0, 1)) if abs(t.z) < 0.95 else Vector((1, 0, 0))
    side = t.cross(up).normalized()
    up = side.cross(t).normalized()
    panel = panel or max(w, h)
    n = max(1, int(round(L / panel)))
    chords = chords or member * 1.5
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]

    def P(i, k):
        sx, sz = corners[k]
        return a + d * (i / n) + side * (sx * w / 2) + up * (sz * h / 2)
    for k in range(4):
        b.beam(P(0, k), P(n, k), chords, chords, mat=mat, tint=tint, solid=solid)
    for i in range(n + 1):
        for k in range(4):
            b.beam(P(i, k), P(i, (k + 1) % 4), member, member, mat=mat, tint=tint)
    for i in range(n):
        for k in range(4):
            if (i + k) % 2:
                b.beam(P(i, k), P(i + 1, (k + 1) % 4), member * 0.8, member * 0.8, mat=mat, tint=tint)
            else:
                b.beam(P(i, (k + 1) % 4), P(i + 1, k), member * 0.8, member * 0.8, mat=mat, tint=tint)


def pipe_run(b, points, radius, tint=GREY, mat=PAINT, supports=12.0, flange=True):
    """A pipe on stands (kit.pipe with its flanges every joint) with intermediate flanges every 12 m."""
    kit.pipe(b, points, radius, tint=tint, mat=mat, flanges=flange, supports=supports)


# ------------------------------------------------------------------------------------------------ buildings


def hall(b, at, yaw, w, d, h, tint=OFFWHITE, band=ORANGE, band2=TEAL, mat=PANEL, pilaster=9.0, plinth=1.6,
         windows=True, roof_units=True, ruin=0.0, doors=(), bay_doors=(), parapet=1.4, louvres=0, roof_tint=None,
         stair=None, solid=True):
    """A large Seedward building: a concrete plinth, panelled walls between pilasters, a livery band and a darker
    cornice, clerestory windows in every bay of the long sides, a parapet, roof plant. `doors`: (side, x) for
    personnel doors, `bay_doors`: (side, x, width, height); sides "front" (-Y), "back", "left" (-X), "right".
    `ruin` 0..1 punches dark holes in the walls and roof. Returns nothing; the box is solid."""
    roof_tint = roof_tint or srgb(0.55, 0.56, 0.55)
    with b.frame(at, (0, 0, yaw)):
        b.box((w, d, h), (0, 0, h / 2), mat=mat, tint=tint, solid=solid)
        b.box((w + 0.8, d + 0.8, plinth), (0, 0, plinth / 2), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        # Bands: livery high up, a thin second band under it.
        b.box((w + 0.25, d + 0.25, max(1.2, h * 0.05)), (0, 0, h * 0.84), mat=PAINT, tint=band, solid=False, vary=0)
        b.box((w + 0.22, d + 0.22, max(0.5, h * 0.015)), (0, 0, h * 0.84 - max(1.2, h * 0.05)), mat=PAINT,
              tint=band2, solid=False, vary=0)
        b.box((w + 0.6, d + 0.6, 1.0), (0, 0, h + parapet - 0.5), mat=PAINT, tint=DARK, solid=False)
        for (sw, sd, x0, y0) in ((w, 0.5, 0, -d / 2 + 0.25), (w, 0.5, 0, d / 2 - 0.25), (0.5, d, -w / 2 + 0.25, 0),
                                 (0.5, d, w / 2 - 0.25, 0)):
            b.box((sw, sd, parapet), (x0, y0, h + parapet / 2 - 0.5), mat=mat, tint=tint, solid=False)
        b.box((w - 1.0, d - 1.0, 0.3), (0, 0, h + 0.1), mat=CONCRETE, tint=roof_tint, solid=False)
        # Pilasters round the walls.
        sides = {"front": (w, -d / 2, 0), "back": (w, d / 2, 180), "left": (d, -w / 2, -90), "right": (d, w / 2, 90)}
        for name, (length, off, ang) in sides.items():
            n = max(1, int(round(length / pilaster)))
            for i in range(n + 1):
                s = -length / 2 + length * i / n
                if name in ("front", "back"):
                    p = (s, off + (-0.35 if name == "front" else 0.35), h / 2 + 0.2)
                    size = (1.1, 0.7, h + 0.4)
                else:
                    p = (off + (-0.35 if name == "left" else 0.35), s, h / 2 + 0.2)
                    size = (0.7, 1.1, h + 0.4)
                b.box(size, p, mat=mat, tint=tint, solid=False, vary=0.02)
            # Clerestory windows in each bay of the long sides.
            if windows and length >= 12:
                for i in range(n):
                    s = -length / 2 + length * (i + 0.5) / n
                    ww = length / n * 0.62
                    wh = min(4.0, h * 0.12)
                    z = h * 0.68
                    if name in ("front", "back"):
                        p = (s, off + (-0.06 if name == "front" else 0.06), z)
                        size = (ww, 0.12, wh)
                    else:
                        p = (off + (-0.06 if name == "left" else 0.06), s, z)
                        size = (0.12, ww, wh)
                    broken = b.rng.random() < 0.35 + ruin * 0.4
                    b.box(size, p, mat=MATTE if broken else GLASS, tint=SOOT if broken else (1, 1, 1),
                          solid=False, vary=0)
            # Louvre banks low on the walls.
            for i in range(louvres if name in ("left", "right") else 0):
                s = -length / 2 + length * (i + 0.5) / louvres
                for k in range(6):
                    z = plinth + 1.5 + k * 0.7
                    p = (off + (-0.3 if name == "left" else 0.3), s, z)
                    b.box((0.25, length / louvres * 0.5, 0.18), p, mat=PAINT, tint=GREY, solid=False, vary=0)
        # Ruin: dark holes in the walls and roof.
        if ruin > 0:
            for _ in range(int(ruin * (w + d) / 5)):
                side = b.rng.choice(list(sides))
                length, off, _ = sides[side]
                s = b.rng.uniform(-length / 2 + 3, length / 2 - 3)
                hw = b.rng.uniform(2.0, 7.0)
                hh = b.rng.uniform(2.0, min(8.0, h * 0.4))
                z = b.rng.uniform(plinth + hh / 2, h - hh / 2 - 1.0)
                if side in ("front", "back"):
                    b.box((hw, 0.14, hh), (s, off + (-0.04 if side == "front" else 0.04), z), mat=MATTE, tint=SOOT,
                          solid=False, vary=0)
                else:
                    b.box((0.14, hw, hh), (off + (-0.04 if side == "left" else 0.04), s, z), mat=MATTE, tint=SOOT,
                          solid=False, vary=0)
            for _ in range(int(ruin * w * d / 600)):
                x, y = b.rng.uniform(-w / 2 + 4, w / 2 - 4), b.rng.uniform(-d / 2 + 4, d / 2 - 4)
                scorch(b, (x, y, h + 0.25), b.rng.uniform(2.0, 6.0), tint=SOOT)
        # Roof plant.
        if roof_units:
            for _ in range(max(1, int(w * d / 900))):
                x, y = b.rng.uniform(-w / 2 + 5, w / 2 - 5), b.rng.uniform(-d / 2 + 5, d / 2 - 5)
                kind = b.rng.random()
                if kind < 0.4:
                    b.box((4.0, 2.6, 2.2), (x, y, h + 1.35), mat=PAINT, tint=LIGHTGREY, solid=False, bevel=0.05)
                    b.cyl(0.9, 0.4, (x - 0.9, y, h + 2.6), mat=PAINT, tint=GREY, sides=14, solid=False)
                    b.cyl(0.9, 0.4, (x + 0.9, y, h + 2.6), mat=PAINT, tint=GREY, sides=14, solid=False)
                elif kind < 0.7:
                    b.cyl(1.2, 3.5, (x, y, h + 2.0), mat=PAINT, tint=OFFWHITE, sides=16, solid=False)
                    b.cyl(1.5, 0.3, (x, y, h + 3.9), mat=PAINT, tint=DARK, sides=16, solid=False)
                else:
                    b.box((6.0, 1.2, 1.4), (x, y, h + 0.95), mat=PAINT, tint=GREY, solid=False)
                    b.box((6.2, 1.4, 0.15), (x, y, h + 1.75), mat=PAINT, tint=DARK, solid=False)
        for side, x in doors:
            length, off, ang = sides[side]
            if side in ("front", "back"):
                p = (x if side == "front" else -x, off + (-0.0 if side == "front" else 0.0), 0)
            else:
                p = (off, -x if side == "left" else x, 0)
            door(b, p, ang, marker="building" if side == "front" else None)
        for side, x, dw, dh in bay_doors:
            length, off, ang = sides[side]
            if side in ("front", "back"):
                p = (x if side == "front" else -x, off, 0)
            else:
                p = (off, -x if side == "left" else x, 0)
            bay_door(b, p, ang, dw, dh, open_frac=b.rng.uniform(0.15, 0.7))
        if stair:
            sx, sy, syaw = stair
            stair_tower(b, (sx, sy, 0), syaw, h + 0.3)


def vault_hall(b, at, yaw, w, length, wall_h, rise, tint=OFFWHITE, band=ORANGE, rib_tint=None, bays=None,
               door_w=None, door_h=None, door_open=0.55, back_door=False, ruin=0.0, missing=0.0, plinth=2.0,
               ends=(True, True)):
    """A barrel-vaulted hangar running along +Y of the turned frame, its door end toward -Y: side walls with
    buttresses, vault panels between arched ribs, end walls with a huge sliding door (partly open), a livery band.
    `missing` leaves vault panels out (the ribs stay); `ends` (front, back) False leaves that end open and ragged,
    for a hall broken in two."""
    rib_tint = rib_tint or srgb(0.62, 0.63, 0.63)
    bays = bays or max(3, int(round(length / 12.0)))
    door_w = door_w or w * 0.7
    door_h = door_h or (wall_h + rise * 0.6)
    t = 1.2
    arc = []
    m = 14
    for i in range(m + 1):
        u = i / m
        x = -w / 2 + w * u
        z = wall_h + rise * (1 - (2 * u - 1) ** 2)
        arc.append((x, z))
    with b.frame(at, (0, 0, yaw)):
        b.box((w + 4.0, length + 4.0, plinth), (0, 0, plinth / 2 - 0.4), mat=CONCRETE, tint=CONCRETE_T, solid=False)
        # Side walls with buttresses.
        for s in (-1, 1):
            b.box((t, length, wall_h), (s * (w / 2 - t / 2), 0, wall_h / 2), mat=PANEL, tint=tint)
            b.box((t + 0.3, length + 0.3, wall_h * 0.05), (s * (w / 2 - t / 2), 0, wall_h * 0.82), mat=PAINT,
                  tint=band, solid=False, vary=0)
            for i in range(bays + 1):
                y = -length / 2 + length * i / bays
                b.extrude([(0, 0), (3.2, 0), (0.0, wall_h + 0.5)], 1.6, (s * w / 2, y, 0), (0, 0, 0 if s > 0 else 180),
                          mat=CONCRETE, tint=CONCRETE_T, solid=False)
            for i in range(bays):
                y = -length / 2 + length * (i + 0.5) / bays
                bl = length / bays * 0.7
                broken = b.rng.random() < 0.3 + ruin * 0.4
                b.box((0.14, bl, wall_h * 0.12), (s * (w / 2 + 0.02), y, wall_h * 0.68), mat=MATTE if broken else GLASS,
                      tint=SOOT if broken else (1, 1, 1), solid=False, vary=0)
        # Vault: ribs at every bay line, panels between.
        outer = [(x * 1.0, z + t) for x, z in arc]
        inner = [(x * 0.985, z) for x, z in reversed(arc)]
        for i in range(bays + 1):
            y = -length / 2 + length * i / bays
            rib_o = [(x * 1.01, z + t + 0.9) for x, z in arc]
            rib_i = [(x * 0.975, z - 0.6) for x, z in reversed(arc)]
            b.extrude(rib_o + rib_i, 1.4, (0, y, 0), (0, 0, 0), mat=PAINT, tint=rib_tint, solid=False)
        for i in range(bays):
            y = -length / 2 + length * (i + 0.5) / bays
            if b.rng.random() < missing:
                # A panel gone: only a few purlins remain.
                for k in (3, 7, 11):
                    x, z = arc[k]
                    b.box((0.5, length / bays - 1.4, 0.5), (x, y, z + t * 0.5), mat=RUST, tint=(1, 1, 1), solid=False)
                continue
            b.extrude(outer + inner, length / bays - 1.4, (0, y, 0), (0, 0, 0), mat=CORRUGATED if i % 4 else PANEL,
                      tint=tint, solid=False)
            if ruin > 0 and b.rng.random() < ruin:
                k = b.rng.randrange(2, m - 2)
                x, z = arc[k]
                scorch(b, (x, y, z + t), b.rng.uniform(2, 5))
        # End walls: piers beside the door up to the vault, the lintel above the door; or a closed gable.
        def zarc(x):
            return wall_h + rise * (1 - (2 * x / w) ** 2)

        def arc_between(x0, x1, k=8):
            return [(x0 + (x1 - x0) * i / k, zarc(x0 + (x1 - x0) * i / k)) for i in range(k + 1)]
        for e in (-1, 1):
            if not ends[0 if e < 0 else 1]:
                continue
            y = e * length / 2
            has_door = e < 0 or back_door
            if not has_door:
                b.extrude([(-w / 2, 0.0), (w / 2, 0.0)] + arc_between(w / 2, -w / 2, 14), t, (0, y, 0), (0, 0, 0),
                          mat=PANEL, tint=tint, solid=False)
                continue
            dw = min(door_w, w - 4.0)
            dh = min(door_h, zarc(dw / 2) - 2.0)
            for sgn in (-1, 1):
                xo, xi = sgn * w / 2, sgn * dw / 2
                b.extrude([(xo, 0.0), (xi, 0.0)] + arc_between(xi, xo, 6), t, (0, y, 0), (0, 0, 0), mat=PANEL,
                          tint=tint, solid=False)
            b.extrude([(-dw / 2, dh), (dw / 2, dh)] + arc_between(dw / 2, -dw / 2, 10), t, (0, y, 0), (0, 0, 0),
                      mat=PANEL, tint=tint, solid=False)
            # The door: two leaves on a track, one slid aside.
            lw = dw / 2
            shift = lw * door_open
            for k, sgn in enumerate((-1, 1)):
                x = sgn * lw / 2 + (sgn * shift if k == 0 else 0.0)
                b.box((lw, 0.8, dh - 0.4), (x, y + e * 1.4, dh / 2), mat=CORRUGATED, tint=srgb(0.6, 0.62, 0.64))
                for zz in range(int(dh / 6)):
                    b.box((lw, 0.9, 0.35), (x, y + e * 1.4, 3 + zz * 6), mat=PAINT, tint=DARK, solid=False)
            b.box((dw + 3, 2.5, 1.2), (0, y + e * 1.4, dh + 0.2), mat=PAINT, tint=DARK, solid=False)
            b.box((dw + 6, 3.0, 0.3), (0, y + e * 2.0, 0.15), mat=PAINT, tint=DARK, solid=False)
            # Darkness inside, seen through the gap.
            b.box((w - 3, 0.3, dh), (0, y - e * 6.0, dh / 2), mat=MATTE, tint=SOOT, solid=False)
        if all(ends):
            b.collide_box(Vector((0, 0, wall_h / 2)), (w, length, wall_h))


def fortress_wall(b, a, c, height=10.0, thick=3.0, buttress=20.0, tint=CONCRETE_T, broken=(), walk=True,
                  panel=10.0):
    """A heavy perimeter wall from `a` to `c` (its outside to the right of a→c): board-formed concrete in panels,
    buttresses outside, a coping, a walkway with a rail inside. `broken`: (from, to) fractions along it that are
    breached (panels fallen, rubble)."""
    a, c = Vector(a), Vector(c)
    d = c - a
    L = d.length
    t = d.normalized()
    out = Vector((t.y, -t.x, 0))
    yaw = math.degrees(math.atan2(t.y, t.x))
    n = max(1, int(round(L / panel)))
    for i in range(n):
        f0, f1 = i / n, (i + 1) / n
        fm = (f0 + f1) / 2
        p = a + d * fm
        gone = any(lo <= fm <= hi for lo, hi in broken)
        if gone:
            if b.rng.random() < 0.6:
                q = p + out * b.rng.uniform(2, 9)
                b.box((L / n * 0.95, height * 0.9, thick), (q.x, q.y, thick / 2), (b.rng.uniform(-6, 6), 0,
                      yaw + b.rng.uniform(-12, 12)), mat=FORMWORK, tint=tint)
            rubble_heap(b, (p.x, p.y, 0), 6.0, 3.0, tint=tint, count=8)
            continue
        hh = height * (1 - 0.0 * b.rng.random())
        b.box((L / n - 0.06, thick, hh), (p.x, p.y, hh / 2), (0, 0, yaw), mat=FORMWORK, tint=tint)
        b.box((L / n - 0.06, thick + 0.5, 0.5), (p.x, p.y, hh + 0.25), (0, 0, yaw), mat=CONCRETE, tint=tint,
              solid=False)
        if walk:
            q = p - out * (thick / 2 + 0.75)
            b.box((L / n - 0.06, 1.5, 0.3), (q.x, q.y, hh - 1.6), (0, 0, yaw), mat=GRATE, tint=GREY, solid=False,
                  vary=0)
    nb = max(1, int(L / buttress))
    for i in range(nb + 1):
        fm = i / nb
        if any(lo <= fm <= hi for lo, hi in broken):
            continue
        p = a + d * fm + out * (thick / 2)
        b.extrude([(0, 0), (3.5, 0), (0.6, height * 0.9), (0, height * 0.9)], 1.8, (p.x, p.y, 0),
                  (0, 0, yaw - 90), mat=FORMWORK, tint=tint, solid=False)
    if walk:
        segs = []
        cur = None
        for i in range(n):
            fm = (i + 0.5) / n
            gone = any(lo <= fm <= hi for lo, hi in broken)
            if gone:
                if cur:
                    segs.append(cur)
                cur = None
            else:
                cur = (cur[0], (i + 1) / n) if cur else (i / n, (i + 1) / n)
        if cur:
            segs.append(cur)
        for f0, f1 in segs:
            p0 = a + d * f0 - out * (thick / 2 + 1.45)
            p1 = a + d * f1 - out * (thick / 2 + 1.45)
            p0.z = p1.z = height - 1.45
            railing(b, p0, p1, 1.1, spacing=2.4, toe=False)


def bastion(b, at, radius=9.0, height=20.0, tint=CONCRETE_T, band=ORANGE, lamp_on=True, door_toward=None):
    """A round corner tower of the perimeter wall: a battered drum, a band, a parapet with slots, a lamp mast."""
    x, y, z = at
    b.cyl(radius + 1.0, height, (x, y, z + height / 2), mat=FORMWORK, tint=tint, sides=32, radius2=radius)
    b.cyl(radius + 0.15, 1.6, (x, y, z + height * 0.75), mat=PAINT, tint=band, sides=32, solid=False, vary=0)
    b.cyl(radius + 0.5, 1.2, (x, y, z + height + 0.6), mat=CONCRETE, tint=tint, sides=32, solid=False)
    for k in range(12):
        a = 2 * math.pi * (k + 0.5) / 12
        b.box((2.4, 0.8, 1.6), (x + math.cos(a) * radius, y + math.sin(a) * radius, z + height + 2.0),
              (0, 0, math.degrees(a) + 90), mat=CONCRETE, tint=tint, solid=False)
    if lamp_on:
        b.cyl(0.25, 10.0, (x, y, z + height + 5.0), mat=PAINT, tint=GREY, sides=8, solid=False)
        aviation_lamp(b, (x, y, z + height + 10.4), 0.8)
    if door_toward is not None:
        a = math.radians(door_toward)
        door(b, (x + math.cos(a) * (radius + 0.6), y + math.sin(a) * (radius + 0.6), z), door_toward + 90.0,
             light=True, sign=False)


# ------------------------------------------------------------------------------------------------ the tether


TETHER_STRAND = 2.0       # radius of each of the seven strands
TETHER_RING = 4.2         # radius of the six outer strands' axes
TETHER_RAIL = 7.6         # radius of the climber rails' centres
TETHER_TWIST = 3.0        # degrees per metre (half a turn per 60 m segment, which the six-fold bundle repeats)
TETHER_SEGMENT = 60.0     # length of one laid piece
TETHER_Z = 4.6            # the bundle's axis above the ground where it lies (sunk about 1.6 m)


def _frames(path, ref):
    """Parallel-transport frames along a polyline: [(point, tangent, u, w, s)]."""
    pts = [Vector(p) for p in path]
    out = []
    s = 0.0
    u = None
    for i, p in enumerate(pts):
        if i > 0:
            s += (p - pts[i - 1]).length
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if u is None:
            r = Vector(ref)
            u = (r - t * r.dot(t))
            if u.length < 1e-6:
                u = t.orthogonal()
            u = u.normalized()
        else:
            u = (u - t * u.dot(t)).normalized()
        w = t.cross(u)
        out.append((p, t, u, w, s))
    return out


def resample(path, step):
    """`path` resampled at about `step` metres."""
    pts = [Vector(p) for p in path]
    out = [pts[0]]
    for a, c in zip(pts, pts[1:]):
        L = (c - a).length
        n = max(1, int(round(L / step)))
        for k in range(1, n + 1):
            out.append(a.lerp(c, k / n))
    return [tuple(p) for p in out]


def tether(b, path, ref=(0, 0, 1), phase=0.0, twist=TETHER_TWIST, collar_every=TETHER_SEGMENT, collar_at=30.0,
           band_every=15.0, band_at=7.5, rails=(0.0, 120.0, 240.0), strand_tint=CARBON, damage=0.0, step=3.0,
           skip_strands=(), end_cut=None):
    """The tether's bundle along `path` (current frame): a straight centre strand and six strands twisted round it,
    binding bands, climber-guide collars of Seedward plating, and three climber rails on brackets. `ref` gives the
    bundle's "up" (the first rail's direction, at 90 degrees from u); `damage` 0..1 leaves bands and collars out and
    tears collar plates. Strand k sits at angle phase + 60k + twist * s from u toward w. Returns the frames."""
    path = resample(path, step)
    fr = _frames(path, ref)
    L = fr[-1][4]
    R, r = TETHER_RING, TETHER_STRAND
    b.tube([tuple(p) for p, *_ in fr], r * 1.08, mat=PAINT, tint=strand_tint, sides=14, cap=True)
    for k in range(6):
        if k in skip_strands:
            continue
        pts = []
        for p, t, u, w, s in fr:
            th = math.radians(phase + 60 * k + twist * s)
            pts.append(tuple(p + u * (R * math.cos(th)) + w * (R * math.sin(th))))
        b.tube(pts, r, mat=PAINT, tint=strand_tint, sides=12, cap=True, vary=0.05)

    def at_s(s):
        for i in range(len(fr) - 1):
            if fr[i][4] <= s <= fr[i + 1][4]:
                f = (s - fr[i][4]) / max(1e-6, fr[i + 1][4] - fr[i][4])
                p = fr[i][0].lerp(fr[i + 1][0], f)
                t = fr[i][1].lerp(fr[i + 1][1], f).normalized()
                u = fr[i][2].lerp(fr[i + 1][2], f).normalized()
                return p, t, u, t.cross(u)
        p, t, u, w, _ = fr[-1]
        return p, t, u, w

    def orient(t, u):
        w = t.cross(u)
        m = Matrix((u, w, t)).transposed()  # columns: x = u, y = w, z = t
        return m.to_euler()

    # Binding bands.
    s = band_at
    while s < L - 0.5:
        if not (damage and b.rng.random() < damage * 0.6):
            p, t, u, w = at_s(s)
            e = orient(t, u)
            b.cyl(R + r + 0.25, 1.1, tuple(p), tuple(math.degrees(x) for x in e), mat=PAINT, tint=DARK, sides=28,
                  solid=False, vary=0.02)
        s += band_every
    # Collars: a ring housing of plating with a livery band and three rail shoes.
    s = collar_at
    while s < L - 1.0:
        p, t, u, w = at_s(s)
        e = tuple(math.degrees(x) for x in orient(t, u))
        torn = damage and b.rng.random() < damage
        with b.frame(tuple(p), e):
            prof = [(R + r + 0.3, -2.4), (R + r + 1.6, -1.8), (R + r + 1.9, -0.6), (R + r + 1.9, 0.6),
                    (R + r + 1.6, 1.8), (R + r + 0.3, 2.4)]
            if not torn:
                b.lathe(prof + [(R + r - 0.2, 2.4), (R + r - 0.2, -2.4), prof[0]], (0, 0, 0), mat=HULL, tint=OFFWHITE,
                        sides=32, close=False, solid=False, vary=0.03)
                b.cyl(R + r + 1.95, 1.0, (0, 0, 0), mat=PAINT, tint=ORANGE, sides=32, solid=False, vary=0)
            else:
                # A collar torn open: half its plating, a few plates hanging.
                for k in range(8):
                    if b.rng.random() < 0.5:
                        continue
                    a = 2 * math.pi * k / 8
                    b.box((4.6, 0.3, 4.6), ((R + r + 1.5) * math.cos(a), (R + r + 1.5) * math.sin(a), 0),
                          (b.rng.uniform(-20, 20), 0, math.degrees(a) + 90), mat=HULL, tint=OFFWHITE, solid=False)
            for ang in rails:
                a = math.radians(ang)
                b.box((2.8, 2.8, 4.4), (TETHER_RAIL * math.cos(a), TETHER_RAIL * math.sin(a), 0),
                      (0, 0, ang), mat=PAINT, tint=TEAL if not torn else DARK, solid=False, bevel=0.1)
        s += collar_every
    # Climber rails: straight lengths between bands, on brackets.
    for ang in rails:
        a = math.radians(ang)
        marks = [0.0]
        s = band_at
        while s < L:
            marks.append(s)
            s += band_every
        marks.append(L)
        for s0, s1 in zip(marks, marks[1:]):
            if s1 - s0 < 0.5:
                continue
            if damage and b.rng.random() < damage * 0.3:
                continue
            p0, t0, u0, w0 = at_s(s0)
            p1, t1, u1, w1 = at_s(s1)
            q0 = p0 + u0 * (TETHER_RAIL * math.cos(a)) + w0 * (TETHER_RAIL * math.sin(a))
            q1 = p1 + u1 * (TETHER_RAIL * math.cos(a)) + w1 * (TETHER_RAIL * math.sin(a))
            b.beam(q0, q1, 1.3, 1.7, mat=PAINT, tint=STEEL, roll=ang - 90)
        s = band_at
        while s < L:
            p, t, u, w = at_s(s)
            q0 = p + u * ((R + r + 0.3) * math.cos(a)) + w * ((R + r + 0.3) * math.sin(a))
            q1 = p + u * ((TETHER_RAIL - 0.6) * math.cos(a)) + w * ((TETHER_RAIL - 0.6) * math.sin(a))
            b.beam(q0, q1, 0.8, 0.8, mat=PAINT, tint=DARK)
            s += band_every
    return fr


def fray(b, p, t, u, w, length=18.0, strands=7, spread=1.0, tint=CARBON, droop=0.0):
    """A snapped end at point `p` (tangent `t`): the strands splay out of it like a broken rope, each fraying into
    fibre bundles at its tip."""
    p, t, u, w = Vector(p), Vector(t), Vector(u), Vector(w)
    for k in range(strands):
        th = 2 * math.pi * k / strands + b.rng.uniform(-0.3, 0.3)
        out = u * math.cos(th) + w * math.sin(th)
        base = p + out * (TETHER_RING if k else 0.0) - t * b.rng.uniform(0, 6)
        L = length * b.rng.uniform(0.5, 1.2)
        pts = []
        for i in range(7):
            f = i / 6
            q = base + t * (L * f) + out * (spread * L * 0.45 * f * f) - Vector((0, 0, droop * L * f * f))
            pts.append(tuple(q))
        b.tube(pts, TETHER_STRAND * 0.95, radii=[TETHER_STRAND * (1 - 0.55 * (i / 6) ** 1.5) for i in range(7)],
               mat=PAINT, tint=tint, sides=10, vary=0.05)
        tip = Vector(pts[-1])
        dirn = (Vector(pts[-1]) - Vector(pts[-2])).normalized()
        for j in range(5):
            dv = (dirn + Vector((b.rng.uniform(-0.9, 0.9), b.rng.uniform(-0.9, 0.9), b.rng.uniform(-0.6, 0.6)))
                  ).normalized()
            fl = b.rng.uniform(3.0, 9.0)
            mid = tip + dv * fl * 0.5 + Vector((0, 0, -droop * fl * 0.3))
            b.tube([tuple(tip), tuple(mid), tuple(tip + dv * fl - Vector((0, 0, droop * fl * 0.6)))], 0.35,
                   radii=[0.45, 0.3, 0.12], mat=PAINT, tint=tint, sides=5)


def _tether_segment(b):
    """Kit piece: 60 m of the fallen tether lying in its furrow, along X from -30 to +30, the bundle's axis 4.6 m
    up (sunk about 1.6 m), berms of thrown-up earth on both sides, the scraped ground between. Laid end to end
    (each piece turned by the next one's heading) the game continues the line for tens of kilometres."""
    half = TETHER_SEGMENT / 2
    path = [(-half, 0, TETHER_Z), (half, 0, TETHER_Z)]
    tether(b, path, ref=(0, 0, 1), phase=0.0, step=3.0)
    b.slab([(-half, -10.5), (half, -10.5), (half, 10.5), (-half, 10.5)], 0.06, (0, 0, 0.0), mat=CONCRETE,
           tint=EARTH_DARK, solid=False, wear=0.9)
    for s in (-1, 1):
        mound_along(b, [(-half, s * 12.5, 0), (0, s * 12.8, 0), (half, s * 12.5, 0)], width=8.0, height=2.6,
                    tint=EARTH, rough=0.3)
    for _ in range(6):
        x = b.rng.uniform(-half + 3, half - 3)
        s = b.rng.choice((-1, 1))
        rock(b, (x, s * b.rng.uniform(8.5, 15.0), 0), (b.rng.uniform(1.0, 2.6), b.rng.uniform(1.0, 2.2),
             b.rng.uniform(0.6, 1.4)), (0, 0, b.rng.uniform(0, 360)), tint=EARTH, n=3, rough=0.3, sink=0.3)
    b.collide_box(Vector((0, 0, TETHER_Z)), (TETHER_SEGMENT, 2 * (TETHER_RAIL + 0.8), 2 * (TETHER_RAIL - 1.0)))


def _tether_segment_torn(b):
    """Kit piece: 60 m of the fallen tether where it struck hardest: bands and collar torn, one outer strand
    snapped and its ends sprung out of the bundle, plating and debris thrown about, burnt ground."""
    half = TETHER_SEGMENT / 2
    path = [(-half, 0, TETHER_Z), (half, 0, TETHER_Z)]
    tether(b, path, ref=(0, 0, 1), phase=0.0, step=3.0, damage=0.8, skip_strands=(1,))
    # The snapped strand: present at both ends, sprung out of the bundle toward the break in the middle.
    R, r = TETHER_RING, TETHER_STRAND
    for side in (-1, 1):
        pts = []
        for i in range(10):
            s = i * 2.5 if side < 0 else TETHER_SEGMENT - i * 2.5
            th = math.radians(60 * 1 + TETHER_TWIST * s)
            spring = max(0.0, (i - 5) / 4.0) ** 2
            off = Vector((0, -math.sin(th), math.cos(th)))
            p = Vector((s - half, 0, TETHER_Z)) + off * (R + spring * 8.0)
            p.z += spring * 3.0
            pts.append(tuple(p))
        b.tube(pts, r, mat=PAINT, tint=CARBON, sides=12, vary=0.05)
        tip = Vector(pts[-1])
        d = (Vector(pts[-1]) - Vector(pts[-2])).normalized()
        for j in range(4):
            dv = (d + Vector((b.rng.uniform(-0.7, 0.7), b.rng.uniform(-0.7, 0.7), b.rng.uniform(-0.4, 0.6)))
                  ).normalized()
            b.tube([tuple(tip), tuple(tip + dv * b.rng.uniform(3, 7))], 0.3, radii=[0.4, 0.1], mat=PAINT,
                   tint=CARBON, sides=5)
    b.slab([(-half, -11.0), (half, -11.0), (half, 11.0), (-half, 11.0)], 0.06, (0, 0, 0.0), mat=CONCRETE,
           tint=EARTH_DARK, solid=False, wear=0.9)
    scorch(b, (4.0, 3.0, 0.04), 13.0)
    scorch(b, (-12.0, -6.0, 0.04), 7.0)
    for s in (-1, 1):
        mound_along(b, [(-half, s * 12.5, 0), (0, s * 14.0, 0), (half, s * 12.5, 0)], width=9.5, height=3.4,
                    tint=EARTH, rough=0.45)
    debris(b, (0, 0, 0), 26.0, count=26, size=(0.8, 4.5), tints=(OFFWHITE, OFFWHITE, DARK), mats=(HULL, HULL, PAINT))
    b.collide_box(Vector((0, 0, TETHER_Z)), (TETHER_SEGMENT, 2 * (TETHER_RAIL + 0.8), 2 * (TETHER_RAIL - 1.0)))


# ------------------------------------------------------------------------------------------------ masses


def frustum(b, at, w0, d0, w1, d1, h, rot=(0, 0, 0), mat=FORMWORK, tint=CONCRETE_T, cell=24.0, solid="hull",
            wear=None):
    """A battered block: a `w0` x `d0` rectangle at its base narrowing to `w1` x `d1` at height `h`, its sides
    gridded about every `cell` metres (so baked occlusion has somewhere to land)."""
    nz = max(1, int(math.ceil(h / cell)))
    nx = max(1, int(math.ceil(max(w0, w1) / cell)))
    ny = max(1, int(math.ceil(max(d0, d1) / cell)))
    rings = []
    for k in range(nz + 1):
        f = k / nz
        w, d, z = w0 + (w1 - w0) * f, d0 + (d1 - d0) * f, h * f
        ring = []
        for i in range(nx):
            ring.append((-w / 2 + w * i / nx, -d / 2, z))
        for i in range(ny):
            ring.append((w / 2, -d / 2 + d * i / ny, z))
        for i in range(nx):
            ring.append((w / 2 - w * i / nx, d / 2, z))
        for i in range(ny):
            ring.append((-w / 2, d / 2 - d * i / ny, z))
        rings.append(ring)
    m = len(rings[0])
    verts = [v for r in rings for v in r]
    faces = []
    for k in range(nz):
        for i in range(m):
            a, c = k * m + i, k * m + (i + 1) % m
            faces.append([a, c, c + m, a + m])
    faces.append(list(range(m))[::-1])
    faces.append([nz * m + i for i in range(m)])
    return b.mesh(verts, faces, at, rot, mat=mat, tint=tint, solid=solid, smooth=False, wear=wear, vary=0.02)


def concrete_stair(b, at, yaw, width, rise, run, tint=CONCRETE_T, lamps=True, handrail=YELLOW):
    """A broad poured stair climbing +Y of the turned frame from `at` (its foot, centre): the mass under the flight,
    0.18 m steps, cheek walls 1.1 m above the treads with a handrail inside, lamp posts at the foot."""
    steps = max(2, int(round(rise / 0.18)))
    h, d = rise / steps, run / steps
    with b.frame(at, (0, 0, yaw)):
        b.extrude([(0, 0), (run, 0), (run, rise)], width, (0, 0, 0), (0, 0, 90), mat=CONCRETE, tint=tint,
                  solid="hull")
        for i in range(steps):
            b.box((width, d + 0.02, h), (0, d * (i + 0.5), h * (i + 0.5)), mat=CONCRETE, tint=tint, solid=False,
                  vary=0.02)
        for s in (-1, 1):
            x = s * (width / 2 + 0.3)
            b.extrude([(0, 0), (run, rise), (run, rise + 1.1), (0, 1.1)], 0.6, (x, 0, 0), (0, 0, 90),
                      mat=FORMWORK, tint=tint, solid="hull")
            xi = s * (width / 2 - 0.15)
            b.beam((xi, 0.0, 0.95), (xi, run, rise + 0.95), 0.06, 0.06, mat=PAINT, tint=handrail)
            n = max(2, int(run / 3.0))
            for k in range(n + 1):
                yy = run * k / n
                b.box((0.05, 0.05, 0.95), (xi, yy, rise * k / n + 0.47), mat=PAINT, tint=handrail, solid=False,
                      vary=0)
            if lamps:
                light_mast(b, (s * (width / 2 + 2.0), -1.5, 0), 8.0, heads=1)
        b.collide_box(Vector((0, run / 2, rise / 2)), (width, math.hypot(run, rise), 0.2),
                      (math.degrees(math.atan2(rise, run)), 0, 0))


# ------------------------------------------------------------------------------------------------ machines (pieces)


def _climber_car(b):
    """Kit piece: a tether climber, the elevator car that rode the cable to orbit: a rounded cabin of Seedward
    plating 16 x 12 x 32 m, the drive spine on its back with paired traction wheels, window bands, radiator fins,
    a livery band and its code, landing legs."""
    body = OFFWHITE
    b.box((14.0, 10.0, 3.0), (0, 0, 2.5), mat=PAINT, tint=DARK, bevel=0.4)
    b.box((16.0, 12.0, 24.0), (0, 0, 16.0), mat=HULL, tint=body, bevel=1.2)
    b.box((12.0, 9.0, 4.0), (0, 0, 29.6), mat=HULL, tint=body, bevel=1.0)
    b.box((16.3, 12.3, 1.8), (0, 0, 24.5), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.box((16.25, 12.25, 0.6), (0, 0, 23.2), mat=PAINT, tint=TEAL, solid=False, vary=0)
    for z, hgt in ((19.5, 1.8), (12.0, 1.4)):
        b.box((13.0, 0.2, hgt), (0, -6.05, z), mat=GLASS, solid=False, vary=0)
        for x in (-8.05, 8.05):
            b.box((0.2, 9.0, hgt), (x, 0, z), mat=GLASS, solid=False, vary=0)
    for k in range(4):
        b.box((1.0, 0.25, 2.2), (-5.5 + k * 1.5, -6.15, 27.0), mat=PAINT, tint=DARK, solid=False, vary=0)
    # Drive spine and traction wheels on the back (+Y), the cable's channel between them.
    b.box((9.0, 3.6, 31.0), (0, 7.6, 16.5), mat=PAINT, tint=GREY, bevel=0.3)
    for z in (6.0, 13.0, 20.0, 27.0):
        for x in (-3.0, 3.0):
            b.cyl(2.3, 1.4, (x, 9.6, z), (0, 90, 0), mat=PAINT, tint=DARK, sides=20, solid=False)
            b.cyl(1.0, 1.6, (x, 9.6, z), (0, 90, 0), mat=PAINT, tint=YELLOW, sides=12, solid=False, vary=0)
        b.box((8.0, 2.4, 1.0), (0, 9.0, z + 3.2), mat=PAINT, tint=DARK, solid=False)
    # Radiator fins on the flanks.
    for x in (-8.3, 8.3):
        for k in range(9):
            b.box((0.9, 0.12, 9.0), (x, -4.0 + k * 1.0, 8.5), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
    # Legs.
    for x in (-6.0, 6.0):
        for y in (-4.5, 4.5):
            b.box((1.0, 1.0, 2.2), (x, y, 1.1), mat=PAINT, tint=DARK, solid=False)
            b.box((2.0, 2.0, 0.3), (x, y, 0.15), mat=PAINT, tint=DARK, solid=False)
    # Code, hatch, lamps, mast.
    for k, wdt in enumerate((1.4, 0.5, 1.4, 1.4)):
        b.box((wdt, 0.12, 2.0), (-3.2 + k * 1.9, -6.1, 8.0), mat=PAINT, tint=DARK, solid=False, vary=0)
    b.box((2.2, 0.3, 2.6), (4.5, -6.1, 5.3), mat=PAINT, tint=TEAL, solid=False, bevel=0.05)
    b.cyl(0.2, 6.0, (3.0, 0, 34.5), mat=PAINT, tint=GREY, sides=8, solid=False)
    b.box((0.6, 0.6, 0.5), (3.0, 0, 37.6), mat=LAMP, tint=RED_LAMP, solid=False, vary=0)
    b.collide_box(Vector((0, 0, 16.0)), (16.0, 12.0, 30.0))


def _hauler_truck(b):
    """Kit piece: a six-wheeled Seedward hauler, 11 m long, a cab, a flatbed with a tank skid, lamps."""
    b.box((10.4, 2.0, 0.5), (0, 0, 1.15), mat=PAINT, tint=DARK)
    b.box((2.8, 2.6, 2.7), (4.0, 0, 2.75), mat=PAINT, tint=YELLOW, bevel=0.15)
    b.box((0.08, 2.2, 1.0), (5.42, 0, 3.4), mat=GLASS, solid=False, vary=0)
    for y in (-1.31, 1.31):
        b.box((1.6, 0.06, 0.9), (4.2, y, 3.4), mat=GLASS, solid=False, vary=0)
    b.box((2.9, 2.7, 0.25), (4.0, 0, 4.2), mat=PAINT, tint=DARK, solid=False)
    for y in (-0.8, 0.8):
        b.box((0.1, 0.4, 0.2), (5.46, y, 1.9), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
    b.box((7.0, 2.6, 0.3), (-1.6, 0, 1.55), mat=PAINT, tint=GREY)
    for y in (-1.25, 1.25):
        b.box((7.0, 0.1, 0.6), (-1.6, y, 2.0), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    b.cyl(1.05, 5.6, (-1.6, 0, 2.85), (0, 90, 0), mat=PAINT, tint=OFFWHITE, sides=18)
    b.box((5.2, 0.3, 0.3), (-1.6, 0, 3.95), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    for x in (3.9, -1.0, -3.6):
        for y in (-1.1, 1.1):
            b.cyl(0.62, 0.5, (x, y, 0.62), (90, 0, 0), mat=PAINT, tint=BLACK, sides=16, solid=False)
            b.cyl(0.3, 0.52, (x, y, 0.62), (90, 0, 0), mat=PAINT, tint=GREY, sides=10, solid=False, vary=0)
    b.collide_box(Vector((0, 0, 1.9)), (10.6, 2.6, 3.8))


# ------------------------------------------------------------------------------------------------ hulls


def superellipse(w, h, p=3.6):
    """A rounded-box cross-section `w` x `h` (|x/a|^p + |z/b|^p = 1): returns (ring(n, y, zc), half_width(dz))."""
    a, bb = w / 2, h / 2

    def ring(n, y, zc, flat=0.86):
        pts = []
        for k in range(n):
            t = 2 * math.pi * k / n
            c, s = math.cos(t), math.sin(t)
            x = math.copysign(abs(c) ** (2 / p), c) * a
            z = math.copysign(abs(s) ** (2 / p), s) * bb
            z = max(z, -bb * flat)
            pts.append((x, y, zc + z))
        return pts

    def half_width(dz):
        t = min(1.0, abs(dz) / bb)
        return a * max(0.0, 1 - t ** p) ** (1 / p)
    return ring, half_width


def hull_loft(b, rings, at=(0, 0, 0), rot=(0, 0, 0), mat=HULL, tint=OFFWHITE, holes=None, cap_start=False,
              cap_end=False, ragged=(0.0, 0.0), solid=False, wear=0.45):
    """A hull skin lofted through `rings` (lists of points, the same count each, in order along the hull).
    `holes(i, j)` -> True leaves the panel between ring i and i+1 at point j out; `ragged` (start, end) metres pulls
    the first and last rings' points back by random amounts, so a break has a torn edge. Returns the rings used."""
    n = len(rings[0])
    rings = [list(r) for r in rings]
    for which, amount in ((0, ragged[0]), (len(rings) - 1, ragged[1])):
        if amount <= 0:
            continue
        nxt = rings[1] if which == 0 else rings[-2]
        new = []
        for j, p in enumerate(rings[which]):
            q = Vector(nxt[j])
            pv = Vector(p)
            d = (q - pv)
            f = b.rng.random() ** 1.5 * min(0.95, amount / max(d.length, 1e-6))
            new.append(tuple(pv + d * f))
        rings[which] = new
    verts = [v for r in rings for v in r]
    faces = []
    for i in range(len(rings) - 1):
        for j in range(n):
            if holes and holes(i, j):
                continue
            a, c = i * n + j, i * n + (j + 1) % n
            faces.append([a, c, c + n, a + n])
    if cap_start:
        faces.append(list(range(n))[::-1])
    if cap_end:
        last = (len(rings) - 1) * n
        faces.append([last + j for j in range(n)])
    b.mesh(verts, faces, at, rot, mat=mat, tint=tint, solid=solid, smooth=True, wear=wear, vary=0.02)
    return rings


def bushes(b, centre, radius, count=12, height=(1.5, 5.0), tints=(LEAF, MOSS, srgb(0.28, 0.38, 0.16)), z=0.0,
           spread_z=0.0):
    """A clump of growth gone wild: rounded low-poly masses in leaf greens."""
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        hgt = height[0] + (height[1] - height[0]) * b.rng.random()
        w = hgt * b.rng.uniform(1.0, 1.8)
        rock(b, (centre[0] + math.cos(a) * r, centre[1] + math.sin(a) * r, z + b.rng.uniform(0, spread_z)),
             (w, w * b.rng.uniform(0.7, 1.1), hgt), (0, 0, b.rng.uniform(0, 360)), tint=b.rng.choice(tints), n=2,
             rough=0.35, sink=0.15, mat=MATTE, solid=False, smooth=True, wear=0.2)


def tree(b, at, height=8.0, crown=None, tint=None, trunk=srgb(0.32, 0.26, 0.2)):
    """A wild tree: a leaning trunk and a lumpy crown or two."""
    x, y, z = at
    crown = crown or height * 0.55
    with b.frame((x, y, z), (b.rng.uniform(-8, 8), b.rng.uniform(-8, 8), b.rng.uniform(0, 360))):
        b.cyl(height * 0.035, height * 0.75, (0, 0, height * 0.375), mat=MATTE, tint=trunk, sides=6,
              radius2=height * 0.02, solid=False)
        for k in range(2):
            c = crown * (1.0 - 0.3 * k)
            rock(b, (b.rng.uniform(-0.15, 0.15) * c, b.rng.uniform(-0.15, 0.15) * c, height * (0.62 + 0.18 * k)),
                 (c, c * b.rng.uniform(0.8, 1.0), c * 0.7), (0, 0, b.rng.uniform(0, 360)),
                 tint=tint or b.rng.choice((LEAF, MOSS, srgb(0.26, 0.36, 0.15))), n=2, rough=0.35, sink=0.0,
                 mat=MATTE, solid=False, smooth=True, wear=0.2)


def scrap_wall(b, a, c, height=2.6, tints=(srgb(0.55, 0.5, 0.42), srgb(0.45, 0.47, 0.5), srgb(0.6, 0.35, 0.2))):
    """A Reclaimer barricade from `a` to `c`: overlapping sheets of corrugated and plate at odd angles, posts."""
    a, c = Vector(a), Vector(c)
    d = c - a
    L = d.length
    yaw = math.degrees(math.atan2(d.y, d.x))
    n = max(1, int(L / 1.6))
    for i in range(n):
        p = a + d * ((i + 0.5) / n)
        hh = height * b.rng.uniform(0.75, 1.1)
        b.box((L / n * 1.25, 0.06, hh), (p.x, p.y, p.z + hh / 2), (b.rng.uniform(-6, 6), 0, yaw + b.rng.uniform(-8, 8)),
              mat=CORRUGATED if i % 3 else PAINT, tint=b.rng.choice(tints), solid=False, vary=0.1, wear=0.8)
    for i in range(0, n + 1, 2):
        p = a + d * (i / n)
        b.box((0.12, 0.12, height + 0.4), (p.x, p.y, p.z + (height + 0.4) / 2), (0, 0, yaw), mat=RUST, tint=(1, 1, 1),
              solid=False, vary=0)
    b.collide_box(Vector(((a + c) / 2).to_tuple()) + Vector((0, 0, height / 2)), (L, 0.3, height), (0, 0, yaw))


def tarp(b, at, w, d, h, yaw=0.0, tint=srgb(0.45, 0.5, 0.35)):
    """A tarp shelter: four poles and a sagging sheet."""
    x, y, z = at
    with b.frame((x, y, z), (0, 0, yaw)):
        for sx in (-1, 1):
            for sy in (-1, 1):
                hh = h + (0.4 if sy > 0 else 0.0)
                b.box((0.08, 0.08, hh), (sx * w / 2, sy * d / 2, hh / 2), mat=RUST, tint=(1, 1, 1), solid=False,
                      vary=0)
        b.box((w + 0.4, d + 0.4, 0.04), (0, 0, h + 0.2), (math.degrees(math.atan2(0.4, d)), 0, 0), mat=MATTE,
              tint=tint, solid=False, vary=0.08)


def string_lights(b, pts, sag=0.06, every=3.0):
    """A cable of bulbs strung between points (Reclaimer lighting)."""
    for a, c in zip(pts, pts[1:]):
        kit.cable(b, a, c, sag=sag, radius=0.015)
        a, c = Vector(a), Vector(c)
        L = (c - a).length
        n = max(1, int(L / every))
        for i in range(1, n):
            t = i / n
            p = a.lerp(c, t)
            p.z -= 4 * sag * L * t * (1 - t) + 0.12
            b.box((0.14, 0.14, 0.18), tuple(p), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
    b.marker("light", tuple(Vector(pts[len(pts) // 2])), colour=[1.0, 0.8, 0.5], range=12.0, flicker=True)


def _cryo_rack(b):
    """Kit piece: a rack of twelve cryo pods in three tiers, 10 x 3.4 x 6 m: capsules with frosted lids, a service
    spine, cables."""
    for x in (-5.0, 5.0):
        for y in (-1.6, 1.6):
            b.box((0.2, 0.2, 6.0), (x, y, 3.0), mat=PAINT, tint=GREY, solid=False)
    for tier in range(3):
        z = 0.6 + tier * 1.9
        b.box((10.2, 3.4, 0.12), (0, 0, z - 0.1), mat=GRATE, tint=GREY, solid=False, vary=0)
        for k in range(4):
            x = -3.75 + k * 2.5
            b.cyl(0.62, 3.0, (x, 0, z + 0.62), (90, 0, 0), mat=PANEL, tint=OFFWHITE, sides=14, solid=False)
            b.box((0.9, 2.4, 0.3), (x, 0, z + 1.18), mat=GLASS, solid=False, vary=0)
            b.box((1.0, 0.25, 0.25), (x, 1.62, z + 0.62), mat=PAINT, tint=TEAL, solid=False, vary=0)
    b.box((10.0, 0.4, 0.6), (0, 1.8, 5.8), mat=PAINT, tint=DARK, solid=False)
    for k in range(4):
        x = -3.75 + k * 2.5
        b.tube([(x, 1.7, 5.6), (x + 0.3, 1.9, 3.5), (x, 1.75, 1.0)], 0.05, mat=PAINT, tint=BLACK, sides=5)
    b.collide_box(Vector((0, 0, 3.0)), (10.4, 3.4, 6.0))


kit.PIECES["cryo-rack"] = _cryo_rack


kit.PIECES["climber-car"] = _climber_car
kit.PIECES["hauler-truck"] = _hauler_truck


kit.PIECES["tether-segment"] = _tether_segment
kit.PIECES["tether-segment-torn"] = _tether_segment_torn
