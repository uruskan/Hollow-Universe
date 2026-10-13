"""Parts for the roadside and small monuments (MONUMENTS.md §5.1, §5.2): lattice steel in angle sections, wheels,
trailers and vehicles, signs and stencilled codes, cables lying in the grass, ground scars (scorch, berms, debris),
and the fixed pieces they share, registered in `kit.PIECES` when this module is imported (import it before calling
`b.piece` with one of them).

Same conventions as kit.py and structures.py: metres, degrees, +Z up, a part's front toward -Y of its frame, colours
given in sRGB and stored linear.
"""
import math

from mathutils import Matrix, Vector

from . import kit
from .kit import (BLACK, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, YELLOW, srgb)
from .surfaces import (ASPHALT, CONCRETE, CORRUGATED, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST)

# ------------------------------------------------------------------------------------------------ colours

GALV = srgb(0.60, 0.62, 0.62)          # galvanised steel: pylons, masts, fences
EARTH = srgb(0.42, 0.34, 0.25)         # turned soil, crater rims, berms
EARTH_DARK = srgb(0.28, 0.22, 0.16)
SCORCH = srgb(0.075, 0.068, 0.06)      # burnt ground, soot
SOOT = srgb(0.30, 0.27, 0.22)          # the lighter edge of a burn
WOOD = srgb(0.58, 0.45, 0.30)
WOOD_GREY = srgb(0.50, 0.47, 0.42)     # weathered timber
RUBBER = srgb(0.055, 0.055, 0.06)
AMBER = srgb(1.0, 0.55, 0.08)          # beacon lamps
OLIVE_DARK = srgb(0.30, 0.33, 0.24)
BULWARK_GREY = srgb(0.45, 0.47, 0.45)
TARP_BLUE = srgb(0.18, 0.34, 0.56)
TARP_GREEN = srgb(0.30, 0.39, 0.27)
CANVAS = srgb(0.64, 0.58, 0.46)
ROCK = srgb(0.52, 0.50, 0.47)
COPPER = srgb(0.72, 0.42, 0.25)
ALU = srgb(0.74, 0.75, 0.76)           # bare aluminium: ladders, trim
SIGN_WHITE = srgb(0.92, 0.91, 0.88)

# ------------------------------------------------------------------------------------------------ stencilled codes

# A 3 x 5 stencil alphabet: the old expansion's codes on plating, crates and doors (MONUMENTS.md §3).
FONT = {
    "0": ["###", "#.#", "#.#", "#.#", "###"], "1": [".#.", "##.", ".#.", ".#.", "###"],
    "2": ["###", "..#", "###", "#..", "###"], "3": ["###", "..#", ".##", "..#", "###"],
    "4": ["#.#", "#.#", "###", "..#", "..#"], "5": ["###", "#..", "###", "..#", "###"],
    "6": ["###", "#..", "###", "#.#", "###"], "7": ["###", "..#", ".#.", ".#.", ".#."],
    "8": ["###", "#.#", "###", "#.#", "###"], "9": ["###", "#.#", "###", "..#", "###"],
    "A": [".#.", "#.#", "###", "#.#", "#.#"], "B": ["##.", "#.#", "##.", "#.#", "##."],
    "C": [".##", "#..", "#..", "#..", ".##"], "D": ["##.", "#.#", "#.#", "#.#", "##."],
    "E": ["###", "#..", "##.", "#..", "###"], "F": ["###", "#..", "##.", "#..", "#.."],
    "G": [".##", "#..", "#.#", "#.#", ".##"], "H": ["#.#", "#.#", "###", "#.#", "#.#"],
    "I": ["###", ".#.", ".#.", ".#.", "###"], "J": ["..#", "..#", "..#", "#.#", ".#."],
    "K": ["#.#", "#.#", "##.", "#.#", "#.#"], "L": ["#..", "#..", "#..", "#..", "###"],
    "M": ["#.#", "###", "###", "#.#", "#.#"], "N": ["##.", "#.#", "#.#", "#.#", "#.#"],
    "O": [".#.", "#.#", "#.#", "#.#", ".#."], "P": ["##.", "#.#", "##.", "#..", "#.."],
    "Q": [".#.", "#.#", "#.#", "##.", ".##"], "R": ["##.", "#.#", "##.", "#.#", "#.#"],
    "S": [".##", "#..", ".#.", "..#", "##."], "T": ["###", ".#.", ".#.", ".#.", ".#."],
    "U": ["#.#", "#.#", "#.#", "#.#", "###"], "V": ["#.#", "#.#", "#.#", "#.#", ".#."],
    "W": ["#.#", "#.#", "###", "###", "#.#"], "X": ["#.#", "#.#", ".#.", "#.#", "#.#"],
    "Y": ["#.#", "#.#", ".#.", ".#.", ".#."], "Z": ["###", "..#", ".#.", "#..", "###"],
    "-": ["...", "...", "###", "...", "..."], "/": ["..#", "..#", ".#.", "#..", "#.."],
    ".": ["...", "...", "...", "...", ".#."], "!": [".#.", ".#.", ".#.", "...", ".#."],
    "+": ["...", ".#.", "###", ".#.", "..."], "~": ["..#", ".#.", "###", ".#.", "#.."],
    " ": ["...", "...", "...", "...", "..."],
}


def _box_into(verts, faces, lo, hi):
    """Appends a closed box from corner `lo` to corner `hi` to a vertex and face list."""
    x0, y0, z0 = lo
    x1, y1, z1 = hi
    base = len(verts)
    verts += [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
              (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        faces.append([base + i for i in f])


def stencil(b, text, at=(0, 0, 0), rot=(0, 0, 0), height=0.3, tint=BLACK, align="left", depth=0.004, mat=PAINT,
            wear=0.55, jitter=0.0):
    """Stencilled characters on a surface, as one primitive: `at` is the text's baseline (left end, centre or right
    end by `align`) on the surface, the letters facing -Y of the frame `rot`, `height` metres tall. `jitter` > 0
    shifts strokes a little (hand-painted signs)."""
    px = height / 5.0
    adv = 4 * px
    width = len(text) * adv - px
    x0 = {"centre": -width / 2, "right": -width}.get(align, 0.0)
    verts, faces = [], []
    for k, ch in enumerate(text.upper()):
        glyph = FONT.get(ch, FONT[" "])
        for row, line in enumerate(glyph):
            z1 = height - row * px
            col = 0
            while col < 3:
                if line[col] != "#":
                    col += 1
                    continue
                start = col
                while col < 3 and line[col] == "#":
                    col += 1
                jx = (b.rng.random() - 0.5) * jitter * px if jitter else 0.0
                jz = (b.rng.random() - 0.5) * jitter * px if jitter else 0.0
                _box_into(verts, faces, (x0 + k * adv + start * px + jx, -depth, z1 - px + jz),
                          (x0 + k * adv + col * px + jx, 0.0, z1 + jz))
    if faces:
        b.mesh(verts, faces, at, rot, mat=mat, tint=tint, solid=False, vary=0, wear=wear, smooth=False)


def plate(b, at, rot=(0, 0, 0), size=(0.6, 0.4), tint=YELLOW, border=None, text=None, text_tint=BLACK,
          text_h=None, depth=0.012):
    """A sign plate on a surface (its face toward -Y of `rot`), with an optional border and stencilled text."""
    w, h = size
    with b.frame(at, rot):
        if border:
            b.box((w + 0.04, depth, h + 0.04), (0, depth / 2, 0), mat=PAINT, tint=border, solid=False, vary=0)
        b.box((w, depth, h), (0, depth / 2 - 0.004, 0), mat=PAINT, tint=tint, solid=False, vary=0, wear=0.5)
        if text:
            th = text_h or h * 0.45
            stencil(b, text, (0, -0.0045, -th / 2), (0, 0, 0), th, text_tint, align="centre")


# ------------------------------------------------------------------------------------------------ panels


def grid_box(b, size, at=(0, 0, 0), rot=(0, 0, 0), step=0.9, mat=PANEL, tint=OFFWHITE, solid=True, wear=None,
             vary=0.03):
    """A box whose faces are cut into cells about `step` metres across, so the occlusion baked per vertex has points
    inside big flat faces (walls, doors, hull plates) however coarse the site's max_edge is. Made by the builder's own
    box code, added as a mesh primitive (its collision: the hull, i.e. the box)."""
    from .builder import _box_bm, _canonical
    verts, faces = _canonical(_box_bm(tuple(size), 0.0, 0, step))
    return b.mesh([tuple(v) for v in verts], faces, at, rot, mat=mat, tint=tint, solid="hull" if solid else False,
                  wear=wear, vary=vary, smooth=False)


# ------------------------------------------------------------------------------------------------ steel


def angle(b, a, c, w=0.1, t=0.012, tint=GALV, mat=PAINT, roll=0.0, solid=False, wear=None):
    """A steel angle (L section, legs `w` wide and `t` thick) from point `a` to point `c` of the current frame."""
    a, c = Vector(a), Vector(c)
    d = c - a
    length = d.length
    if length < 1e-4:
        return
    q = d.to_track_quat("Z", "Y" if abs(d.normalized().z) < 0.99 else "X")
    rot = (q.to_matrix().to_4x4() @ Matrix.Rotation(math.radians(roll), 4, "Z")).to_euler()
    mid = (a + c) / 2
    with b.frame(tuple(mid), tuple(math.degrees(x) for x in rot)):
        b.box((w, t, length), (0, -w / 2 + t / 2, 0), mat=mat, tint=tint, solid=solid, vary=0, wear=wear)
        b.box((t, w - t, length), (-w / 2 + t / 2, t / 2, 0), mat=mat, tint=tint, solid=False, vary=0, wear=wear)


def lattice(b, z0, z1, w0, w1, sections, member=0.1, tint=GALV, skip=0.0, legs=True, top_girt=True, solid=False):
    """A square lattice section in the current frame from height `z0` (width `w0`) to `z1` (width `w1`): four legs,
    girts and X bracing in angle steel. `skip` (0..1) leaves out braces and girts (damage). Returns corner(level, i)
    for attaching more steel (level 0..sections, i 0..3 counter-clockwise from -x-y)."""
    def corner(level, i):
        t = level / sections
        hw = (w0 + (w1 - w0) * t) / 2
        sx = -1 if i in (0, 3) else 1
        sy = -1 if i in (0, 1) else 1
        return Vector((sx * hw, sy * hw, z0 + (z1 - z0) * t))
    if legs:
        for i in range(4):
            for s in range(sections):
                angle(b, corner(s, i), corner(s + 1, i), member * 1.4, member * 0.13, tint, roll=90 * i,
                      solid=solid)
    for s in range(sections + (1 if top_girt else 0)):
        for i in range(4):
            if 0 < s < sections and b.rng.random() < skip * 0.5:
                continue
            angle(b, corner(s, i), corner(s, (i + 1) % 4), member, member * 0.12, tint)
    for s in range(sections):
        for i in range(4):
            j = (i + 1) % 4
            if b.rng.random() >= skip:
                angle(b, corner(s, i), corner(s + 1, j), member * 0.8, member * 0.1, tint)
            if b.rng.random() >= skip:
                angle(b, corner(s, j), corner(s + 1, i), member * 0.8, member * 0.1, tint)
    return corner


def ball(b, at, radius, tint=ORANGE, mat=GLOSS, sides=12, rings=6, solid=False):
    """A sphere (an aviation marker ball, a float, a dome lamp)."""
    prof = [(0.0, -radius)] + [(radius * math.sin(math.pi * k / rings), -radius * math.cos(math.pi * k / rings))
                               for k in range(1, rings)] + [(0.0, radius)]
    b.lathe(prof, at, mat=mat, tint=tint, sides=sides, solid="hull" if solid else False, vary=0)


# ------------------------------------------------------------------------------------------------ ground


def spline(pts, step):
    """Points along a Catmull-Rom curve through `pts` (Vectors), about `step` metres apart."""
    out = []
    n = len(pts)
    for i in range(n - 1):
        p0, p1, p2 = pts[max(i - 1, 0)], pts[i], pts[i + 1]
        p3 = pts[min(i + 2, n - 1)]
        k = max(1, int((p2 - p1).length / step))
        for j in range(k):
            t = j / k
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1].copy())
    return out


def ground_cable(b, pts, radius=0.03, tint=BLACK, wobble=0.5, step=0.9, mat=PAINT, height=None, sides=5,
                 wave=9.0):
    """A cable lying in the grass along a smooth curve through `pts` ((x, y) or (x, y, z)), snaking sideways by up
    to `wobble` with a wavelength of about `wave` metres; on the ground unless the points give a height or
    `height(x, y)` lifts it. The first and last points stay where they are."""
    p = [Vector((q[0], q[1], q[2] if len(q) > 2 else 0.0)) for q in pts]
    curve = spline(p, step)
    ph1, ph2 = b.rng.random() * 6.28, b.rng.random() * 6.28
    out = []
    run = 0.0
    for i, q in enumerate(curve):
        if i:
            run += (q - curve[i - 1]).length
        d = curve[min(i + 1, len(curve) - 1)] - curve[max(i - 1, 0)]
        side = Vector((-d.y, d.x, 0)).normalized() if d.xy.length > 1e-6 else Vector((1, 0, 0))
        fade = min(1.0, i / 4.0, (len(curve) - 1 - i) / 4.0)
        off = wobble * fade * (0.75 * math.sin(run / wave * 6.283 + ph1) + 0.25 * math.sin(run / wave * 13.8 + ph2))
        q = q + side * off
        z = max(q.z, radius + 0.01)
        if height:
            z = max(z, height(q.x, q.y) + radius)
        out.append((q.x, q.y, z))
    b.tube(out, radius, mat=mat, tint=tint, sides=sides, cap=True)
    return out


def coil(b, centre, radius=0.7, turns=3.5, wire=0.025, tint=BLACK, mat=PAINT, spread=0.18, per_turn=12):
    """A loose coil of cable or wire lying on the ground, one primitive."""
    cx, cy, cz = centre
    pts = []
    n = int(turns * per_turn)
    ph = b.rng.random() * 6.28
    for i in range(n + 1):
        a = 2 * math.pi * i / per_turn + ph
        r = radius * (1 + spread * math.sin(a * 0.5 + i * 0.37) * (0.6 + 0.4 * b.rng.random()))
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a), cz + wire + 1.6 * wire * (i / n) * turns * 0.5))
    b.tube(pts, wire, mat=mat, tint=tint, sides=5, cap=True)


def blob(b, centre, radius, tint=SCORCH, points=14, rough=0.35, thick=0.006, mat=MATTE, wear=0.0, squash=1.0,
         yaw=0.0):
    """An irregular flat patch on the ground (soot, oil, a burn), one thin primitive."""
    cx, cy, cz = centre
    pts = []
    ph = b.rng.random() * 6.28
    for k in range(points):
        a = 2 * math.pi * k / points
        r = radius * (1 + rough * (0.6 * math.sin(3 * a + ph) + 0.4 * (b.rng.random() - 0.5) * 2))
        x, y = r * math.cos(a), r * math.sin(a) * squash
        ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        pts.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    b.slab(pts, thick, (0, 0, cz), mat=mat, tint=tint, solid=False, vary=0, wear=wear)


def scorch(b, centre, radius, rough=0.35):
    """A burn on the ground: a black core inside a sooty ring."""
    x, y, z = centre
    blob(b, (x, y, z + 0.012), radius * 1.35, SOOT, points=18, rough=rough * 1.3)
    blob(b, (x, y, z + 0.018), radius * 0.8, SCORCH, points=16, rough=rough * 1.4)


def berm(b, pts, width=2.0, height=0.6, tint=EARTH, mat=CONCRETE, jag=0.25, solid=True):
    """A low ridge of earth along the polyline `pts` (x, y): a rounded cross-section, ragged along its length."""
    p = [Vector((q[0], q[1], 0)) for q in pts]
    rings = []
    prof = [(-1.0, 0.0), (-0.6, 0.55), (-0.2, 0.95), (0.25, 0.9), (0.65, 0.45), (1.0, 0.0)]
    for i, q in enumerate(p):
        d = (p[min(i + 1, len(p) - 1)] - p[max(i - 1, 0)])
        side = Vector((-d.y, d.x, 0)).normalized()
        taper = math.sin(math.pi * (i / (len(p) - 1))) ** 0.6 if len(p) > 2 else 1.0
        hh = height * taper * (1 + jag * (b.rng.random() - 0.5) * 2)
        ww = width * (0.5 + 0.5 * taper) * (1 + jag * 0.5 * (b.rng.random() - 0.5))
        rings.append([q + side * (u * ww / 2) + Vector((0, 0, v * hh - 0.05)) for u, v in prof])
    n = len(prof)
    verts = [tuple(v) for r in rings for v in r]
    faces = []
    for i in range(len(rings) - 1):
        for j in range(n - 1):
            a = i * n + j
            faces.append([a, a + 1, a + n + 1, a + n])
    faces.append(list(range(n))[::-1])
    last = (len(rings) - 1) * n
    faces.append([last + j for j in range(n)])
    b.mesh(verts, faces, mat=mat, tint=tint, solid="hull" if solid else False, wear=0.7, vary=0.04)


def clods(b, centre, radius, count=10, tint=EARTH, size=0.5, mat=CONCRETE, inner=0.0):
    """Lumps of earth or stone thrown out round a point (between `inner` and `radius`)."""
    cx, cy, cz = centre
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = inner + (radius - inner) * math.sqrt(b.rng.random())
        s = size * (0.35 + 0.65 * b.rng.random())
        b.box((s, s * (0.6 + 0.4 * b.rng.random()), s * (0.35 + 0.3 * b.rng.random())),
              (cx + math.cos(a) * r, cy + math.sin(a) * r, cz + s * 0.08),
              (b.rng.uniform(-25, 25), b.rng.uniform(-25, 25), b.rng.uniform(0, 360)), mat=mat, tint=tint,
              solid=False, vary=0.08, wear=0.8)


def debris(b, centre, radius, count=8, size=0.8, tint=GREY, mat=PAINT, thick=0.03, solid=False):
    """Bent plates and broken panels lying round a point."""
    cx, cy, cz = centre
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        s = size * (0.4 + 0.6 * b.rng.random())
        b.box((s, s * (0.3 + 0.5 * b.rng.random()), thick), (cx + math.cos(a) * r, cy + math.sin(a) * r, cz + 0.04),
              (b.rng.uniform(-12, 12), b.rng.uniform(-12, 12), b.rng.uniform(0, 360)), mat=mat, tint=tint,
              solid=solid, vary=0.06)


# ------------------------------------------------------------------------------------------------ road things


def verge(b, x, y0, y1, side=1, width=1.4, tint=srgb(0.56, 0.53, 0.47), step=2.5):
    """A gravel shoulder along a road edge at `x` from `y0` to `y1`, `side` +1 or -1 (outward); ragged outer edge."""
    inner, outer = [], []
    n = max(2, int(abs(y1 - y0) / step))
    for k in range(n + 1):
        y = y0 + (y1 - y0) * k / n
        inner.append((x, y))
        outer.append((x + side * width * (0.7 + 0.5 * b.rng.random()), y))
    pts = inner + outer[::-1]
    if side < 0:
        pts = pts[::-1]
    b.slab(pts, 0.04, (0, 0, 0.0), mat=CONCRETE, tint=tint, solid=False, vary=0.02, wear=0.8)


def delineator(b, at, yaw=0.0, knocked=False, reflector=srgb(0.95, 0.55, 0.1)):
    """A roadside marker post: white with a black band and a reflector, standing or knocked flat."""
    x, y, z = at
    rot = (0, 0, yaw) if not knocked else (86, 0, yaw)
    with b.frame((x, y, z + (0.06 if knocked else 0)), rot):
        b.box((0.11, 0.11, 1.05), (0, 0, 0.52), mat=PAINT, tint=SIGN_WHITE, solid=False, vary=0, wear=0.5)
        b.box((0.115, 0.115, 0.22), (0, 0, 0.82), mat=PAINT, tint=BLACK, solid=False, vary=0)
        b.box((0.06, 0.02, 0.12), (0, -0.065, 0.82), mat=GLOSS, tint=reflector, solid=False, vary=0)


def generator(b, at, rot=(0, 0, 0), tint=ORANGE):
    """A portable generator in a tube frame, with a fuel cap and a socket panel."""
    with b.frame(at, rot):
        b.box((0.9, 0.55, 0.5), (0, 0, 0.38), mat=PAINT, tint=tint, bevel=0.03, solid=True)
        b.box((0.5, 0.4, 0.22), (-0.12, 0, 0.72), mat=PAINT, tint=tint, solid=False)
        b.cyl(0.05, 0.05, (-0.25, 0, 0.85), mat=PAINT, tint=DARK, sides=8, solid=False)
        b.box((0.3, 0.02, 0.22), (0.25, -0.285, 0.42), mat=PAINT, tint=DARK, solid=False)
        for x in (-0.48, 0.48):
            for y in (-0.31, 0.31):
                b.cyl(0.018, 0.85, (x, y, 0.42), mat=PAINT, tint=BLACK, sides=6, solid=False, vary=0)
            b.box((0.04, 0.66, 0.04), (x, 0, 0.86), mat=PAINT, tint=BLACK, solid=False, vary=0)
        for y in (-0.31, 0.31):
            b.box((1.0, 0.04, 0.04), (0, y, 0.06), mat=PAINT, tint=BLACK, solid=False, vary=0)


def work_light(b, at, yaw=0.0, height=2.6, lit=True, tilt=-18.0):
    """Two flood lamps on a telescopic tripod."""
    from .kit import lamp
    x, y, z = at
    with b.frame((x, y, z), (0, 0, yaw)):
        for k in range(3):
            a = math.radians(90 + 120 * k)
            b.beam((0.65 * math.cos(a), 0.65 * math.sin(a), 0), (0, 0, 0.9), 0.03, 0.03, mat=PAINT, tint=YELLOW)
        b.cyl(0.035, height - 0.2, (0, 0, (height - 0.2) / 2 + 0.1), mat=PAINT, tint=GREY, sides=8, solid=False)
        b.box((0.9, 0.06, 0.06), (0, 0, height), mat=PAINT, tint=DARK, solid=False)
        for dx in (-0.32, 0.32):
            if lit:
                lamp(b, (dx, -0.08, height + 0.12), (tilt, 0, 0), size=(0.34, 0.16, 0.26))
            else:
                b.box((0.34, 0.16, 0.26), (dx, -0.08, height + 0.12), (tilt, 0, 0), mat=PAINT, tint=DARK,
                      solid=False)


def cone(b, at, tint=ORANGE, knocked=False, yaw=0.0):
    """A traffic cone, standing or knocked over."""
    x, y, z = at
    rot = (b.rng.uniform(80, 95), 0, yaw) if knocked else (0, 0, yaw)
    with b.frame((x, y, z + (0.17 if knocked else 0)), rot):
        b.box((0.36, 0.36, 0.04), (0, 0, 0.02), mat=MATTE, tint=BLACK, solid=False, vary=0)
        b.cyl(0.14, 0.62, (0, 0, 0.35), mat=MATTE, tint=tint, sides=10, radius2=0.03, solid=False)
        b.cyl(0.098, 0.1, (0, 0, 0.36), mat=GLOSS, tint=SIGN_WHITE, sides=10, radius2=0.08, solid=False, vary=0)


def tripod_sign(b, at, yaw=0.0, tint=ORANGE, glyph="!", fallen=False):
    """A folding road sign on a tripod: a diamond board with a border and a glyph."""
    x, y, z = at
    rot = (0, 0, yaw) if not fallen else (82, 0, yaw)
    with b.frame((x, y, z + (0.12 if fallen else 0)), rot):
        for (ax, ay) in ((-0.45, -0.25), (0.45, -0.25), (0.0, 0.55)):
            b.beam((ax, ay, 0), (0, 0, 1.25), 0.03, 0.03, mat=PAINT, tint=GREY)
        with b.frame((0, -0.06, 1.15), (0, 45, 0)):
            b.box((0.66, 0.025, 0.66), (0, 0.01, 0), mat=PAINT, tint=SIGN_WHITE, solid=False, vary=0)
            b.box((0.58, 0.025, 0.58), (0, 0.0, 0), mat=PAINT, tint=tint, solid=False, vary=0, wear=0.4)
        stencil(b, glyph, (0, -0.075, 1.0), (0, 0, 0), 0.3, BLACK, align="centre")


# ------------------------------------------------------------------------------------------------ vehicles


def wheel(b, at, rot=(0, 0, 0), radius=0.4, width=0.22, tint=RUBBER, rim=LIGHTGREY, sides=16):
    """A small wheel (jockey wheels, carts) as plain geometry; axle along X."""
    with b.frame(at, rot):
        b.cyl(radius, width, (0, 0, 0), (0, 90, 0), mat=MATTE, tint=tint, sides=sides, solid=False)
        b.cyl(radius * 0.58, width + 0.02, (0, 0, 0), (0, 90, 0), mat=PAINT, tint=rim, sides=sides, solid=False)


def service_trailer(b, at, rot=(0, 0, 0), length=5.4, width=2.3, height=2.2, tint=OFFWHITE, band=ORANGE,
                    stripe=TEAL, code="LN-07", doors=(112.0, 158.0), beacon=True, ladder_rack=True):
    """A two-axle box trailer of the old grid service (Seedward civil): composite body with rounded corners, a
    livery band, stencilled codes, rear doors swung open over a fold-down step, shelving inside, a ladder rack and
    a beacon bar on the roof. Drawbar toward +Y, the doors at -Y. Returns (floor height, the floor's centre in the
    monument's frame) so callers can put loot inside."""
    floor = 1.24
    hl, hw = length / 2, width / 2
    t = 0.05
    with b.frame(at, rot):
        # Chassis: two rails, cross members, the A-frame drawbar, a jockey wheel.
        for x in (-0.55, 0.55):
            b.box((0.12, length - 0.2, 0.2), (x, 0, floor - 0.12), mat=PAINT, tint=DARK)
        for k in range(5):
            y = -hl + 0.35 + k * (length - 0.7) / 4
            b.box((width - 0.12, 0.08, 0.1), (0, y, floor - 0.07), mat=PAINT, tint=DARK, solid=False)
        for x in (-0.55, 0.55):
            b.beam((x, hl - 0.3, floor - 0.12), (0, hl + 1.55, floor - 0.3), 0.1, 0.12, mat=PAINT, tint=DARK)
        b.box((0.16, 0.34, 0.14), (0, hl + 1.7, floor - 0.32), mat=PAINT, tint=GREY, solid=False)
        b.cyl(0.045, 0.95, (0.32, hl + 0.8, floor - 0.62), mat=PAINT, tint=GREY, sides=8, solid=False)
        wheel(b, (0.32, hl + 0.8, 0.14), (0, 0, 0), 0.14, 0.08, sides=10)
        # Axles, leaf springs and wheels (pieces), mudguards over the wheels.
        for y in (-0.6, 0.6):
            b.cyl(0.055, width - 0.3, (0, y, 0.54), (0, 90, 0), mat=PAINT, tint=DARK, sides=8, solid=False)
            for s in (-1, 1):
                b.piece("wheel-road", (s * (hw - 0.2), y, 0), (0, 0, 0 if s < 0 else 180))
                b.box((0.08, 0.9, 0.08), (s * 0.55, y, 0.82), mat=PAINT, tint=DARK, solid=False)
        # The body: floor, sides, front, roof; rounded corners; rear frame.
        b.box((width, length, 0.1), (0, 0, floor + 0.05), mat=PAINT, tint=GREY)
        for s in (-1, 1):
            b.box((t, length - 0.16, height), (s * (hw - t / 2), 0, floor + 0.1 + height / 2), mat=PANEL, tint=tint)
        b.box((width - 0.16, t, height), (0, hl - t / 2, floor + 0.1 + height / 2), mat=PANEL, tint=tint)
        b.box((width - 0.08, length - 0.08, 0.06), (0, 0, floor + 0.13 + height), mat=PANEL, tint=tint)
        for s in (-1, 1):
            b.cyl(0.08, height, (s * (hw - 0.08), hl - 0.08, floor + 0.1 + height / 2), mat=PANEL, tint=tint,
                  sides=10, solid=False)
            b.cyl(0.07, length - 0.16, (s * (hw - 0.07), 0, floor + 0.1 + height), (90, 0, 0), mat=PANEL, tint=tint,
                  sides=10, solid=False)
            b.box((0.1, 0.1, height + 0.1), (s * (hw - 0.05), -hl + 0.05, floor + 0.1 + height / 2), mat=PAINT,
                  tint=GREY, solid=False)
        b.box((width, 0.1, 0.12), (0, -hl + 0.05, floor + 0.12 + height), mat=PAINT, tint=GREY, solid=False)
        b.box((width + 0.06, 0.16, 0.12), (0, -hl - 0.02, floor - 0.08), mat=PAINT, tint=DARK, solid=False)
        # Livery: an orange band and a teal pinstripe down each side, codes stencilled near the front.
        for s in (-1, 1):
            b.box((0.012, length - 0.3, 0.32), (s * (hw + 0.004), 0, floor + 0.1 + height * 0.42), mat=PAINT,
                  tint=band, solid=False, vary=0, wear=0.5)
            b.box((0.012, length - 0.3, 0.06), (s * (hw + 0.004), 0, floor + 0.1 + height * 0.42 - 0.26),
                  mat=PAINT, tint=stripe, solid=False, vary=0, wear=0.5)
            x = s * (hw + 0.01)
            stencil(b, code, (x, hl - 0.6 if s < 0 else -hl + 0.6, floor + 0.1 + height * 0.72),
                    (0, 0, 90 * s), 0.22, DARK)
            # A side locker hatch with a handle.
            for dz in (0.0, 0.8):
                b.box((0.014, 1.2, 0.03), (s * (hw + 0.006), 0.9, floor + 0.35 + dz), mat=PAINT, tint=DARK,
                      solid=False, vary=0)
            for dy in (0.3, 1.5):
                b.box((0.014, 0.03, 0.8), (s * (hw + 0.006), dy, floor + 0.75), mat=PAINT, tint=DARK, solid=False,
                      vary=0)
            b.box((0.03, 0.14, 0.03), (s * (hw + 0.02), 0.9, floor + 0.75), mat=PAINT, tint=GREY, solid=False)
            # Tail lights and reflectors.
            b.box((0.2, 0.03, 0.1), (s * (hw - 0.25), -hl - 0.1, floor - 0.08), mat=GLOSS, tint=RED, solid=False,
                  vary=0)
        # Rear doors, swung open on their hinges.
        dh = height - 0.05
        for s, ang in ((-1, -doors[0]), (1, doors[1])):
            with b.frame((s * hw, -hl, floor + 0.12), (0, 0, ang)):
                b.box((hw - 0.02, 0.05, dh), (-s * (hw / 2), -0.025, dh / 2), mat=PANEL, tint=tint)
                b.box((0.03, 0.03, dh - 0.1), (-s * (hw * 0.55), -0.07, dh / 2), mat=PAINT, tint=GREY, solid=False)
                b.box((hw - 0.1, 0.012, 0.3), (-s * (hw / 2), -0.055, dh * 0.42), mat=PAINT, tint=band, solid=False,
                      vary=0)
                if s < 0:
                    stencil(b, code, (-s * (hw / 2), -0.058, dh * 0.62), (0, 0, 0), 0.16, DARK, align="centre")
        # Fold-down step under the doors.
        from .kit import stairs
        stairs(b, (0, -hl - 1.05, 0), (0, 0, 0), 0.8, floor + 0.1, 1.0, tint=GREY, rails=False)
        # Inside: shelving down the left wall, cases on the right, a cable spool.
        for z in (0.0, 0.7, 1.4):
            b.box((0.5, length - 1.0, 0.04), (-hw + 0.3, 0.3, floor + 0.5 + z), mat=PAINT, tint=GREY, solid=False)
        for y in (-1.6, 0.0, 1.6, 2.4):
            b.box((0.04, 0.04, 1.6), (-hw + 0.55, min(y, hl - 0.3), floor + 0.9), mat=PAINT, tint=DARK, solid=False)
        for k, y in enumerate((-1.0, -0.2, 0.7, 1.6)):
            b.box((0.42, 0.55, 0.3 + 0.1 * (k % 2)), (-hw + 0.3, y, floor + 0.73 + 0.7 * (k % 3) - 0.0), mat=PAINT,
                  tint=(ORANGE, DARK, TEAL, GREY)[k], solid=False)
        b.box((0.6, 0.45, 0.45), (hw - 0.4, 1.8, floor + 0.33), mat=PAINT, tint=DARK, solid=False)
        b.box((0.55, 0.4, 0.35), (hw - 0.4, 1.8, floor + 0.73), mat=PAINT, tint=ORANGE, solid=False)
        b.cyl(0.35, 0.4, (hw - 0.5, 0.9, floor + 0.36), mat=PAINT, tint=WOOD, sides=14, solid=False)
        b.cyl(0.22, 0.42, (hw - 0.5, 0.9, floor + 0.36), mat=PAINT, tint=COPPER, sides=14, solid=False)
        # Roof: a ladder rack with a ladder, a beacon bar.
        top = floor + 0.16 + height
        if ladder_rack:
            for y in (-hl + 0.8, hl - 0.8):
                b.box((width - 0.3, 0.06, 0.06), (0, y, top + 0.1), mat=PAINT, tint=GREY, solid=False)
                for x in (-hw + 0.2, hw - 0.2):
                    b.box((0.05, 0.05, 0.12), (x, y, top + 0.04), mat=PAINT, tint=GREY, solid=False)
            for x in (0.25, 0.65):
                b.box((0.05, length - 0.9, 0.08), (x, 0, top + 0.17), mat=PAINT, tint=ALU, solid=False, vary=0)
            for k in range(14):
                b.box((0.4, 0.03, 0.03), (0.45, -hl + 0.7 + k * (length - 1.4) / 13, top + 0.17), mat=PAINT,
                      tint=ALU, solid=False, vary=0)
        if beacon:
            b.box((1.1, 0.25, 0.1), (-0.3, hl - 0.4, top + 0.05), mat=PAINT, tint=DARK, solid=False)
            for x in (-0.65, 0.05):
                b.box((0.26, 0.2, 0.14), (x, hl - 0.4, top + 0.17), mat=LAMP, tint=AMBER, solid=False, vary=0)
                b.marker("light", (x, hl - 0.4, top + 0.3), colour=[round(c, 3) for c in AMBER], range=12.0,
                         blink=0.8)
        centre = b.world((0.35, 0.2, floor + 0.1))
    return floor, centre


def _wheel_road(b):
    """A road wheel (trucks, trailers): a 1.08 m tyre with ribbed tread and rounded shoulders on a steel disc rim
    with ten wheel nuts and a hub cap. Standing on the ground with its axle along X, outer face toward -X."""
    r, w = 0.54, 0.31
    hw = w / 2
    prof = [(0.30, -hw + 0.012), (0.345, -hw - 0.004), (0.43, -hw - 0.014), (0.50, -hw + 0.004), (0.528, -hw + 0.03),
            (r, -hw + 0.062), (r, -0.078), (0.524, -0.068), (0.524, -0.046), (r, -0.036), (r, 0.036),
            (0.524, 0.046), (0.524, 0.068), (r, 0.078), (r, hw - 0.062), (0.528, hw - 0.03), (0.50, hw - 0.004),
            (0.43, hw + 0.014), (0.345, hw + 0.004), (0.30, hw - 0.012)]
    with b.frame((0, 0, r), (0, 90, 0)):
        b.lathe(prof, (0, 0, 0), mat=MATTE, tint=RUBBER, sides=28, close=False, solid="hull", wear=0.55)
        b.cyl(0.302, w - 0.03, (0, 0, 0), mat=PAINT, tint=LIGHTGREY, sides=20, solid=False, wear=0.6)
        b.lathe([(0.29, 0.0), (0.24, -0.03), (0.14, -0.035), (0.13, -0.06), (0.0, -0.062)], (0, 0, -hw + 0.02),
                mat=PAINT, tint=LIGHTGREY, sides=20, close=False, solid=False, wear=0.6)
        for k in range(10):
            a = 2 * math.pi * k / 10
            b.cyl(0.02, 0.05, (math.cos(a) * 0.105, math.sin(a) * 0.105, -hw - 0.045), mat=PAINT, tint=GREY,
                  sides=6, solid=False, vary=0)
        b.cyl(0.07, 0.07, (0, 0, -hw - 0.06), mat=PAINT, tint=GREY, sides=12, solid=False)
        for k in range(6):
            a = 2 * math.pi * (k + 0.5) / 6
            b.box((0.07, 0.04, 0.01), (math.cos(a) * 0.2, math.sin(a) * 0.2, -hw - 0.012), (0, 0, math.degrees(a)),
                  mat=PAINT, tint=DARK, solid=False, vary=0)


kit.PIECES["wheel-road"] = _wheel_road


# ------------------------------------------------------------------------------------------------ huts and masts


def prefab_hut(b, at, rot=(0, 0, 0), w=3.6, d=2.6, h=2.6, tint=OFFWHITE, band=TEAL, door=ORANGE, door_open=65.0,
               door_x=None, code=None, louvre=True, ac=True, lamp_on=True, floor=0.3, roof_tint=LIGHTGREY,
               windows=0, mat=PANEL):
    """A Seedward prefab cabin (relay huts, sheds, booths, site offices): composite walls on a steel skid, rounded
    corners, a band of livery, a 2.3 m door in the front (-Y) wall swung open, louvres in the left wall, an air
    conditioner on the right wall, a lamp over the door, optional windows in the back wall. Hollow (loot goes
    inside). Returns the floor's centre in the monument's frame."""
    t = 0.08
    hw, hd = w / 2, d / 2
    dx = (hw - 0.85) if door_x is None else door_x
    dw, dh = 1.0, 2.3
    with b.frame(at, rot):
        for x in (-hw + 0.3, hw - 0.3):
            b.box((0.16, d + 0.2, 0.2), (x, 0, 0.1), mat=PAINT, tint=DARK)
        b.box((w, d, 0.1), (0, 0, floor - 0.05), mat=PAINT, tint=GREY)
        z0 = floor
        e = 0.13       # walls stop at the corner posts

        def wall(a, c, fixed, along_x):
            """A wall from `a` to `c` along X (or Y) at `fixed` on the other axis."""
            if along_x:
                grid_box(b, (c - a, t, h - 0.02), ((a + c) / 2, fixed, z0 + h / 2), mat=mat, tint=tint)
            else:
                grid_box(b, (t, c - a, h - 0.02), (fixed, (a + c) / 2, z0 + h / 2), mat=mat, tint=tint)

        # Walls: back, left, right, and the front around the door.
        wall(-hw + e, hw - e, hd - t / 2, True)
        wall(-hd + e, hd - e, -hw + t / 2, False)
        wall(-hd + e, hd - e, hw - t / 2, False)
        left = (dx - dw / 2) - (-hw + e)
        right = (hw - e) - (dx + dw / 2)
        if left > 0.02:
            wall(-hw + e, dx - dw / 2, -hd + t / 2, True)
        if right > 0.02:
            wall(dx + dw / 2, hw - e, -hd + t / 2, True)
        b.box((dw, t, h - dh), (dx, -hd + t / 2, z0 + dh + (h - dh) / 2), mat=mat, tint=tint)
        for sx in (-1, 1):
            for sy in (-1, 1):
                b.cyl(0.09, h, (sx * (hw - 0.04), sy * (hd - 0.04), z0 + h / 2), mat=mat, tint=tint, sides=10,
                      solid=False)
        # Roof with a drip edge.
        b.box((w + 0.24, d + 0.24, 0.12), (0, 0, z0 + h + 0.06), mat=PAINT, tint=roof_tint)
        b.box((w + 0.28, d + 0.28, 0.05), (0, 0, z0 + h + 0.015), mat=PAINT, tint=DARK, solid=False)
        # Livery band round the walls (split at the door).
        bz = z0 + h * 0.66
        b.box((w - 0.1, 0.012, 0.2), (0, hd + 0.006, bz), mat=PAINT, tint=band, solid=False, vary=0, wear=0.5)
        for sx in (-1, 1):
            b.box((0.012, d - 0.1, 0.2), (sx * (hw + 0.006), 0, bz), mat=PAINT, tint=band, solid=False, vary=0,
                  wear=0.5)
        if left > 0.02:
            b.box((left, 0.012, 0.2), (-hw + e + left / 2, -hd - 0.006, bz), mat=PAINT, tint=band, solid=False,
                  vary=0, wear=0.5)
        if right > 0.02:
            b.box((right, 0.012, 0.2), (hw - e - right / 2, -hd - 0.006, bz), mat=PAINT, tint=band, solid=False,
                  vary=0, wear=0.5)
        # Door frame, the leaf swung out on its hinge (at the door's right edge), a step.
        for s in (-1, 1):
            b.box((0.06, 0.1, dh), (dx + s * (dw / 2 + 0.03), -hd - 0.01, z0 + dh / 2), mat=PAINT, tint=DARK,
                  solid=False)
        b.box((dw + 0.12, 0.1, 0.06), (dx, -hd - 0.01, z0 + dh + 0.03), mat=PAINT, tint=DARK, solid=False)
        with b.frame((dx + dw / 2, -hd - 0.02, z0), (0, 0, door_open)):
            b.box((dw - 0.02, 0.05, dh - 0.02), (-dw / 2, -0.025, dh / 2), mat=PAINT, tint=door)
            b.box((0.04, 0.06, 0.14), (-dw + 0.12, -0.07, 1.05), mat=PAINT, tint=GREY, solid=False)
            b.box((dw - 0.2, 0.012, 0.25), (-dw / 2, -0.056, 1.7), mat=PAINT, tint=SIGN_WHITE, solid=False, vary=0)
        b.box((dw + 0.3, 0.6, 0.18), (dx, -hd - 0.32, floor - 0.12), mat=PAINT, tint=GREY)
        b.marker("door", (dx, -hd - 0.6, 0.0), kind_of="hut")
        if code:
            stencil(b, code, (-hw + 0.25, -hd - 0.01, z0 + h * 0.4), (0, 0, 0), 0.22, DARK)
        if louvre:
            with b.frame((-hw - 0.01, 0.2, z0 + h * 0.5), (0, 0, 0)):
                b.box((0.04, 0.7, 0.7), (0, 0, 0), mat=PAINT, tint=GREY, solid=False)
                for k in range(6):
                    b.box((0.08, 0.64, 0.025), (-0.04, 0, -0.3 + k * 0.12), (0, 40, 0), mat=PAINT, tint=GREY,
                          solid=False, vary=0)
        if ac:
            with b.frame((hw + 0.26, 0.1, z0 + 0.9), (0, 0, 0)):
                b.box((0.45, 0.9, 0.7), (0, 0, 0), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
                b.cyl(0.26, 0.02, (0.23, 0, 0.02), (0, 90, 0), mat=PAINT, tint=DARK, sides=14, solid=False)
                for k in range(4):
                    b.box((0.02, 0.5, 0.02), (0.24, 0, -0.12 + k * 0.08), mat=PAINT, tint=GREY, solid=False, vary=0)
                b.tube([(-0.2, 0.3, 0.38), (-0.2, 0.3, 0.6), (-0.26, 0.3, 0.6)], 0.02, tint=DARK, sides=5)
        if lamp_on:
            from .kit import lamp
            lamp(b, (dx, -hd - 0.2, z0 + dh + 0.3), (-25, 0, 0), size=(0.3, 0.16, 0.14))
        for k in range(windows):
            x = -hw + (k + 1) * w / (windows + 1)
            b.box((0.9, 0.03, 0.7), (x, hd + 0.01, z0 + 1.5), mat=GLASS, solid=False)
            b.box((1.0, 0.04, 0.06), (x, hd + 0.02, z0 + 1.88), mat=PAINT, tint=DARK, solid=False)
            b.box((1.0, 0.04, 0.06), (x, hd + 0.02, z0 + 1.12), mat=PAINT, tint=DARK, solid=False)
        centre = b.world((0, 0.2, floor))
    return centre


def rungs(b, at, height, width=0.42, rot=(0, 0, 0), spacing=0.32, tint=GREY, rail_tint=YELLOW, marker=True):
    """A plain fixed ladder for long climbs (masts, towers), light on triangles: two rails and the rungs as a single
    primitive, facing -Y of `rot`; a climb marker at its foot."""
    with b.frame(at, rot):
        for x in (-width / 2, width / 2):
            b.box((0.05, 0.025, height + 1.0), (x, 0, (height + 1.0) / 2), mat=PAINT, tint=rail_tint, solid=False,
                  vary=0)
        verts, faces = [], []
        n = int(height / spacing)
        for i in range(n):
            z = spacing * (i + 0.5)
            _box_into(verts, faces, (-width / 2, -0.012, z - 0.012), (width / 2, 0.012, z + 0.012))
        b.mesh(verts, faces, mat=PAINT, tint=tint, solid=False, vary=0, smooth=False)
        if marker:
            b.marker("ladder", (0, -0.35, 0), height=round(height, 2))


def tri_mast(b, at, height, face=1.4, bay=1.9, bands=(ORANGE, OFFWHITE), band_h=7.5, leg_r=0.055,
             member=0.045, skip=0.0, orient=0.0):
    """A guyed lattice mast of triangular section (face `face` m): tube legs, girts and zigzag diagonals, painted in
    alternating aviation bands from the top down. Returns corner(z, i), a leg's position at height z."""
    rc = face / math.sqrt(3)
    angs = [math.radians(a + orient) for a in (270, 30, 150)]

    def corner(z, i):
        return Vector((at[0] + rc * math.cos(angs[i]), at[1] + rc * math.sin(angs[i]), at[2] + z))

    def tint_at(z):
        k = int((height - z) / band_h)
        return bands[k % len(bands)]

    z = 0.0
    while z < height - 1e-3:
        z1 = min(height, (math.floor(z / band_h + 1e-6) + 1) * band_h)
        for i in range(3):
            c0, c1 = corner(z, i), corner(z1, i)
            b.cyl(leg_r, z1 - z, (c0.x, c0.y, (c0.z + c1.z) / 2), mat=PAINT, tint=tint_at(z + 0.01), sides=6,
                  cap=False, solid=True, vary=0)
        z = z1
    n = int(height / bay)
    for k in range(n + 1):
        zz = min(height, k * bay)
        for i in range(3):
            if 0 < k < n and b.rng.random() < skip * 0.4:
                continue
            b.beam(corner(zz, i), corner(zz, (i + 1) % 3), member, member, mat=PAINT, tint=tint_at(zz + 0.01))
        if k < n:
            z2 = min(height, zz + bay)
            for i in range(3):
                if b.rng.random() < skip:
                    continue
                if k % 2 == 0:
                    a, c = corner(zz, i), corner(z2, (i + 1) % 3)
                else:
                    a, c = corner(zz, (i + 1) % 3), corner(z2, i)
                b.beam(a, c, member * 0.85, member * 0.85, mat=PAINT, tint=tint_at(zz + bay * 0.5))
    return corner


def microwave_dish(b, at, yaw=0.0, radius=0.6, pitch=0.0, cover=True, tint=LIGHTGREY, arm=0.0):
    """A microwave link antenna: a drum shroud with a flat radome (or the bare reflector when `cover` is False),
    facing -Y of its frame, a back housing, and a mounting arm `arm` metres back toward the mast."""
    with b.frame(at, (pitch, 0, yaw)):
        depth = radius * 0.6
        b.cyl(radius, depth, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=tint, sides=20, cap=not cover)
        if cover:
            b.cyl(radius * 1.01, 0.05, (0, -depth / 2 - 0.02, 0), (90, 0, 0), mat=MATTE, tint=SIGN_WHITE, sides=20,
                  solid=False)
            b.cyl(radius * 1.03, 0.04, (0, -depth / 2 + 0.03, 0), (90, 0, 0), mat=PAINT, tint=GREY, sides=20,
                  solid=False, vary=0)
        else:
            prof = [(radius * 0.97 * k / 6, -depth / 2 + depth * 0.8 * (k / 6) ** 2) for k in range(7)]
            b.lathe(prof, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=SIGN_WHITE, sides=20, close=False, solid=False)
            b.cyl(0.03, depth * 0.9, (0, -depth * 0.2, 0), (90, 0, 0), mat=PAINT, tint=GREY, sides=6, solid=False)
        b.box((radius * 0.7, depth * 0.6, radius * 0.7), (0, depth * 0.75, 0), mat=PAINT, tint=GREY, solid=False)
        if arm > 0:
            for x in (-radius * 0.3, radius * 0.3):
                b.beam((x, depth * 0.9, 0), (x * 0.3, depth + arm, 0), 0.05, 0.05, mat=PAINT, tint=GREY)


def guy_anchor(b, at, toward):
    """A guy anchor: a concrete block half in the ground, a steel plate and turnbuckles leaning toward the guys'
    attachment points `toward` (a list of points). Returns the point where the guys meet the anchor."""
    x, y, z = at
    d = Vector((toward[0][0] - x, toward[0][1] - y, 0)).normalized()
    yaw = math.degrees(math.atan2(d.y, d.x))
    b.box((2.4, 1.4, 0.9), (x, y, 0.2), (0, 0, yaw), mat=CONCRETE, tint=srgb(0.86, 0.85, 0.82), bevel=0.04)
    top = Vector((x, y, 0.75)) + d * 0.3
    b.beam((x - d.x * 0.3, y - d.y * 0.3, 0.6), top, 0.18, 0.04, mat=PAINT, tint=GALV)
    for t in toward:
        dirn = (Vector(t) - top).normalized()
        a = top + dirn * 0.25
        c = top + dirn * 1.1
        rot = tuple(math.degrees(v) for v in dirn.to_track_quat("Z", "Y").to_euler())
        b.cyl(0.035, (c - a).length, tuple((a + c) / 2), rot, mat=PAINT, tint=GALV, sides=6, solid=False, vary=0)
    return top
