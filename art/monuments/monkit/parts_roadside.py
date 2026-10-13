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
from .kit import (BLACK, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, TEAL, WARM_LAMP, YELLOW, srgb, stripes)
from .surfaces import (ASPHALT, CHAINLINK, CONCRETE, CORRUGATED, GLASS, GLOSS, GRATE, LAMP, MATTE, PAINT, PANEL, RUST)

# ------------------------------------------------------------------------------------------------ colours

GALV = srgb(0.60, 0.62, 0.62)          # galvanised steel: pylons, masts, fences
EARTH = srgb(0.60, 0.46, 0.31)         # turned soil, crater rims, berms (saturated: ground grime greys it)
EARTH_DARK = srgb(0.42, 0.32, 0.22)
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
RUST_RED = srgb(0.50, 0.21, 0.13)      # container red
CONTAINER_BLUE = srgb(0.20, 0.32, 0.46)

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


def rod(b, a, c, radius, mat=PAINT, tint=GREY, sides=8, solid=False, trim=0.0, cap=True):
    """A round bar or pipe from `a` to `c` as a cylinder, which the builder subdivides along its length (a two-point
    tube has vertices only at its ends, and ends buried in a column bake black). `trim` shortens it at both ends."""
    a, c = Vector(a), Vector(c)
    d = c - a
    if d.length < 2 * trim + 1e-3:
        return None
    u = d.normalized()
    a, c = a + u * trim, c - u * trim
    q = u.to_track_quat("Z", "Y" if abs(u.z) < 0.99 else "X")
    rot = tuple(math.degrees(v) for v in q.to_euler())
    return b.cyl(radius, (c - a).length, tuple((a + c) / 2), rot, mat=mat, tint=tint, sides=sides, cap=cap,
                 solid=solid, vary=0)


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
        taper = max(0.15, math.sin(math.pi * (i / (len(p) - 1))) ** 0.6) if len(p) > 2 else 1.0
        hh = height * taper * (1 + jag * (b.rng.random() - 0.5) * 2)
        ww = width * (0.5 + 0.5 * taper) * (1 + jag * 0.5 * (b.rng.random() - 0.5))
        rings.append([q + side * (u * ww / 2) + Vector((0, 0, v * hh - 0.05)) for u, v in prof])
    n = len(prof)
    verts = [tuple(v) for r in rings for v in r]
    faces = []
    for i in range(len(rings) - 1):
        for j in range(n):           # round the whole profile, the flat underside included: a closed solid
            a = i * n + j
            c = i * n + (j + 1) % n
            faces.append([a, c, c + n, a + n])
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


def _wave(a, ph, k=(3, 7), w=(0.6, 0.4)):
    return w[0] * math.sin(k[0] * a + ph[0]) + w[1] * math.sin(k[1] * a + ph[1])


def crater(b, centre, rim_r, rim_h, inner_r=None, outer_r=None, rough=0.3, sectors=32, tint=EARTH, mat=CONCRETE,
           walk=True):
    """A crater's rim thrown up round a bowl (the bowl's floor is the ground): a closed ring of earth, ragged in
    height and radius. `walk` adds collision ramps on both slopes so the rim can be walked over."""
    cx, cy, cz = centre
    inner_r = inner_r or rim_r * 0.35
    outer_r = outer_r or rim_r * 1.45
    rs = [inner_r, inner_r + (rim_r - inner_r) * 0.45, rim_r * 0.88, rim_r, rim_r + (outer_r - rim_r) * 0.22,
          rim_r + (outer_r - rim_r) * 0.55, outer_r]
    hs = [0.01, 0.12, 0.6, 1.0, 0.72, 0.24, 0.0]
    ph = [b.rng.random() * 6.28 for _ in range(4)]
    rings = []
    for j, (r, h) in enumerate(zip(rs, hs)):
        ring = []
        for i in range(sectors):
            a = 2 * math.pi * i / sectors
            m = 1 + 0.07 * _wave(a, ph[2:], (2, 5))
            n = max(0.25, 1 + rough * _wave(a, ph[:2]))
            ring.append((cx + r * m * math.cos(a), cy + r * m * math.sin(a), cz + rim_h * h * n))
        rings.append(ring)
    bottom_in = [(x, y, cz - 0.3) for x, y, _ in rings[0]]
    bottom_out = [(x, y, cz - 0.3) for x, y, _ in rings[-1]]
    all_rings = [bottom_in] + rings + [bottom_out]
    verts = [v for ring in all_rings for v in ring]
    faces = []
    nr = len(all_rings)
    for j in range(nr):
        for i in range(sectors):
            a = j * sectors + i
            c = j * sectors + (i + 1) % sectors
            jn = (j + 1) % nr
            faces.append([a, c, jn * sectors + (i + 1) % sectors, jn * sectors + i])
    b.mesh(verts, faces, mat=mat, tint=tint, solid=False, wear=0.8, vary=0.03)
    if walk:
        for i in range(0, sectors, 2):
            a = 2 * math.pi * (i + 1) / sectors
            arc = 2 * math.pi * rim_r / (sectors / 2) + 0.2
            for r0, r1 in ((rs[1], rs[3]), (rs[3], rs[5])):
                h0 = rim_h * hs[rs.index(r0)]
                h1 = rim_h * hs[rs.index(r1)]
                run = r1 - r0
                slope = math.degrees(math.atan2(h1 - h0, run))
                rm = (r0 + r1) / 2
                with b.frame((cx + rm * math.cos(a), cy + rm * math.sin(a), cz + (h0 + h1) / 2 - 0.06),
                             (0, 0, math.degrees(a))):
                    b.collide_box(Vector((0, 0, 0)), (math.hypot(run, h1 - h0), arc, 0.12), (0, -slope, 0))


def drape(b, centre, size, yaw=0.0, height=0.35, folds=5.0, tint=ORANGE, mat=MATTE, cells=(12, 5), part=(0.0, 1.0),
          seed=None, taper=0.0, bend=0.0):
    """A heap of cloth lying on the ground (a collapsed canopy, a tarp): a closed slab whose top is a field of folds
    `height` high, `size` (length, width) metres, turned `yaw` degrees. `part` (v0, v1) keeps a strip of the width,
    so several strips of different colours can make one striped canopy with a continuous surface. `taper` narrows
    both ends (a canopy bunched at its skirt and apex), `bend` curves it sideways (metres at the far end)."""
    cx, cy, cz = centre
    L, W = size
    nu, nv = cells
    ph = [(seed if seed is not None else b.rng.random()) * 6.28 * k for k in (1.0, 1.7, 2.3, 3.1)]
    v0, v1 = part
    ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))

    def h(u, v):
        edge = min(1.0, 5 * min(u, 1 - u), 6 * min(v, 1 - v)) ** 0.6
        ridge = abs(math.sin(u * math.pi * folds + 1.6 * math.sin(v * 3.1 + ph[0])))
        lump = 0.5 + 0.5 * math.sin(u * 9.1 + ph[1]) * math.sin(v * 6.7 + ph[2])
        crumple = 0.5 + 0.5 * math.sin(u * 23.0 + v * 11.0 + ph[3])
        return cz + 0.02 + height * edge * (0.45 * ridge + 0.35 * lump + 0.2 * crumple)

    def pos(u, v, z):
        s = 1.0 - taper + taper * math.sin(math.pi * (0.08 + 0.84 * u)) ** 0.7
        x, y = (u - 0.5) * L, (v - 0.5) * W * s + bend * u * u
        return (cx + x * ca - y * sa, cy + x * sa + y * ca, z)
    top, bot = [], []
    for j in range(nv + 1):
        v = v0 + (v1 - v0) * j / nv
        for i in range(nu + 1):
            u = i / nu
            top.append(pos(u, v, h(u, v)))
            bot.append(pos(u, v, cz + 0.005))
    n = len(top)
    verts = top + bot
    faces = []
    for j in range(nv):
        for i in range(nu):
            a = j * (nu + 1) + i
            faces.append([a, a + 1, a + nu + 2, a + nu + 1])
            faces.append([n + a, n + a + nu + 1, n + a + nu + 2, n + a + 1])
    for i in range(nu):
        a, c = i, i + 1
        faces.append([a, n + a, n + c, c])
        a, c = nv * (nu + 1) + i, nv * (nu + 1) + i + 1
        faces.append([c, n + c, n + a, a])
    for j in range(nv):
        a, c = j * (nu + 1), (j + 1) * (nu + 1)
        faces.append([c, n + c, n + a, a])
        a, c = j * (nu + 1) + nu, (j + 1) * (nu + 1) + nu
        faces.append([a, n + a, n + c, c])
    b.mesh(verts, faces, mat=mat, tint=tint, solid=False, wear=0.5, vary=0.02)
    return pos


def arc_panel(b, radius, a0, a1, z0, z1, thick=0.05, segs=8, mat=PAINT, tint=GREY, solid=False, wear=None):
    """A curved panel: part of a cylinder of `radius` about the frame's Z axis from angle `a0` to `a1` (degrees,
    0 = +X) and height `z0` to `z1`, `thick` metres thick inward. One closed primitive."""
    outer, inner = [], []
    for k in range(segs + 1):
        a = math.radians(a0 + (a1 - a0) * k / segs)
        for z in (z0, z1):
            outer.append((radius * math.cos(a), radius * math.sin(a), z))
            inner.append(((radius - thick) * math.cos(a), (radius - thick) * math.sin(a), z))
    n = len(outer)
    verts = outer + inner
    faces = []
    for k in range(segs):
        a = 2 * k
        faces.append([a, a + 2, a + 3, a + 1])
        faces.append([n + a, n + a + 1, n + a + 3, n + a + 2])
        faces.append([a, n + a, n + a + 2, a + 2])
        faces.append([a + 1, a + 3, n + a + 3, n + a + 1])
    faces.append([0, 1, n + 1, n])
    last = 2 * segs
    faces.append([last, n + last, n + last + 1, last + 1])
    return b.mesh(verts, faces, mat=mat, tint=tint, solid="hull" if solid else False, wear=wear, vary=0.02)


# ------------------------------------------------------------------------------------------------ road things


def long_road(b, a, c, width=7.0, seg=7.0, lines=True, tint=(1, 1, 1)):
    """A straight asphalt road from `a` to `c` ((x, y)), cut into pieces about `seg` metres long so the texture's
    random offset changes along it (a long road made of one box repeats its 6 m tile visibly), each cut in three across
    so no vertex lies under the centre dashes (it would bake black under them); the pieces overlap by
    2 cm at alternating heights so they never fight. Centre dashes and edge lines as structures.road paints them."""
    a, c = Vector((a[0], a[1], 0)), Vector((c[0], c[1], 0))
    d = c - a
    n = max(1, int(round(d.length / seg)))
    yaw = math.degrees(math.atan2(d.y, d.x))
    side = Vector((-d.y, d.x, 0)).normalized() * (width / 2 - 0.3)
    for k in range(n):
        p = a + d * ((k + 0.5) / n)
        grid_box(b, (d.length / n + 0.02, width, 0.1 + 0.0015 * (k % 2)), (p.x, p.y, 0.05 + 0.00075 * (k % 2)),
                 (0, 0, yaw), step=width / 2.6, mat=ASPHALT, tint=tint, vary=0.02)
    if lines:
        line = srgb(0.85, 0.83, 0.75)
        m = int(d.length / 6)
        for k in range(m):
            p = a + d * ((k + 0.5) / m)
            b.box((3.0, 0.15, 0.012), (p.x, p.y, 0.108), (0, 0, yaw), mat=PAINT, tint=line, solid=False, vary=0,
                  wear=0.7)
        for s in (side, -side):
            for k in range(n):
                p = a + d * ((k + 0.5) / n) + s
                b.box((d.length / n, 0.12, 0.012), (p.x, p.y, 0.108), (0, 0, yaw), mat=PAINT, tint=line, solid=False,
                      vary=0, wear=0.7)


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


# ------------------------------------------------------------------------------------------------ trucks and freight

C20 = (6.06, 2.44, 2.59)      # a 20-foot container, as the kit's container-20 piece


def hauler(b, at, rot=(0, 0, 0), tint=OFFWHITE, band=ORANGE, stripe=TEAL, code="HX-214", broken=0.3, mirrors=True,
           lights=True, frame_tint=DARK):
    """A Seedward cab-over hauler (tractor unit): ladder chassis on three axles, a tall cab with a raked windscreen
    and rounded corners, livery band and codes, fuel tanks, exhaust stack and intake, a catwalk and the fifth wheel.
    Front toward -Y of its frame, the ground at z = 0. `broken` (0..1) leaves windows out. Returns the fifth
    wheel's position (where a trailer's king pin goes) in the monument's frame."""
    with b.frame(at, rot):
        # Chassis, engine, axles and wheels.
        for x in (-0.45, 0.45):
            b.box((0.2, 7.3, 0.3), (x, -0.05, 0.95), mat=PAINT, tint=frame_tint)
        for y in (-3.0, -1.0, 0.6, 3.4):
            b.box((0.7, 0.12, 0.18), (0, y, 0.95), mat=PAINT, tint=frame_tint, solid=False)
        b.box((1.0, 1.7, 0.8), (0, -2.55, 0.95), mat=PAINT, tint=srgb(0.25, 0.25, 0.26))
        for y in (-2.6, 1.6, 2.95):
            b.cyl(0.07, 1.8, (0, y, 0.54), (0, 90, 0), mat=PAINT, tint=frame_tint, sides=8, solid=False)
            for s in (-1, 1):
                b.piece("wheel-road", (s * 1.06, y, 0), (0, 0, 0 if s < 0 else 180))
        # The cab: a side profile extruded across, rounded corners at the front.
        prof = [(-3.62, 1.15), (-1.35, 1.15), (-1.35, 3.62), (-3.25, 3.62), (-3.5, 3.45), (-3.66, 2.4), (-3.7, 1.35)]
        with b.frame((0, 0, 0), (0, 0, 90)):
            b.extrude(prof, 2.46, (0, 0, 0), (0, 0, 0), mat=PANEL, tint=tint, bevel=0.06)
        for s in (-1, 1):
            b.cyl(0.11, 2.2, (s * 1.13, -3.58, 2.35), mat=PANEL, tint=tint, sides=10, solid=False)
        b.box((2.5, 0.3, 0.12), (0, -3.42, 3.66), (12, 0, 0), mat=PAINT, tint=frame_tint, solid=False)
        # Glass: windscreen and door windows, some panes gone.
        if b.rng.random() > broken:
            b.box((2.12, 0.03, 1.0), (0, -3.59, 2.95), (-9.0, 0, 0), mat=GLASS, solid=False)
        else:
            b.box((2.12, 0.03, 1.0), (0, -3.55, 2.95), (-9.0, 0, 0), mat=MATTE, tint=srgb(0.05, 0.05, 0.05),
                  solid=False)
        for s in (-1, 1):
            if b.rng.random() > broken:
                b.box((0.03, 1.0, 0.75), (s * 1.245, -2.75, 2.95), mat=GLASS, solid=False)
            else:
                b.box((0.03, 1.0, 0.75), (s * 1.235, -2.75, 2.95), mat=MATTE, tint=srgb(0.04, 0.04, 0.04),
                      solid=False)
            # Door seams, handle, steps, the band, the code.
            for y in (-3.4, -2.05):
                b.box((0.02, 0.03, 2.2), (s * 1.24, y, 2.3), mat=PAINT, tint=frame_tint, solid=False, vary=0)
            b.box((0.04, 0.2, 0.04), (s * 1.26, -2.25, 2.35), mat=PAINT, tint=GREY, solid=False)
            for z in (0.62, 1.02):
                b.box((0.32, 0.55, 0.05), (s * 1.08, -2.75, z), mat=GRATE, tint=GREY, solid=False, vary=0)
            b.box((0.012, 2.25, 0.32), (s * 1.236, -2.48, 2.05), mat=PAINT, tint=band, solid=False, vary=0, wear=0.5)
            b.box((0.012, 2.25, 0.06), (s * 1.236, -2.48, 1.82), mat=PAINT, tint=stripe, solid=False, vary=0,
                  wear=0.5)
            stencil(b, code, (s * 1.25, -2.6 if s < 0 else -3.52, 1.45), (0, 0, 90 * s), 0.2, DARK)
            if mirrors:
                b.beam((s * 1.2, -3.35, 2.9), (s * 1.55, -3.45, 3.05), 0.04, 0.04, mat=PAINT, tint=frame_tint)
                b.box((0.07, 0.24, 0.48), (s * 1.6, -3.45, 2.95), mat=PAINT, tint=frame_tint, solid=False)
        b.box((2.3, 0.012, 0.32), (0, -3.712, 2.05), mat=PAINT, tint=band, solid=False, vary=0, wear=0.5)
        # Grille, bumper, lamps.
        b.box((1.8, 0.05, 0.62), (0, -3.72, 1.55), mat=PAINT, tint=frame_tint, solid=False)
        for k in range(5):
            b.box((1.7, 0.03, 0.04), (0, -3.75, 1.3 + k * 0.12), mat=PAINT, tint=GREY, solid=False, vary=0)
        b.box((2.5, 0.3, 0.38), (0, -3.82, 0.95), mat=PAINT, tint=frame_tint, bevel=0.03)
        for s in (-1, 1):
            b.box((0.36, 0.05, 0.16), (s * 0.85, -3.75, 1.85), mat=GLOSS, tint=srgb(0.9, 0.9, 0.85), solid=False,
                  vary=0)
            b.box((0.16, 0.05, 0.1), (s * 1.1, -3.98, 1.0), mat=GLOSS, tint=srgb(1.0, 0.6, 0.1), solid=False, vary=0)
        if lights:
            for x in (-0.5, 0.5):
                b.box((0.18, 0.12, 0.1), (x, -3.2, 3.72), mat=LAMP, tint=AMBER, solid=False, vary=0)
            b.marker("light", (0, -3.2, 3.9), colour=[round(c, 3) for c in AMBER], range=14.0, blink=0.7)
        # Behind the cab: stack and intake, catwalk, fuel tanks, the fifth wheel, mudguards, rear lamps.
        b.cyl(0.09, 2.7, (0.95, -1.18, 2.75), mat=PAINT, tint=GREY, sides=10, solid=False)
        b.cyl(0.1, 0.25, (0.95, -1.18, 4.15), mat=RUST, tint=(1, 1, 1), sides=10, solid=False)
        b.box((0.4, 0.35, 1.4), (-0.95, -1.15, 2.4), mat=PAINT, tint=tint, bevel=0.03, solid=False)
        b.box((1.6, 1.3, 0.05), (0, -0.55, 1.26), mat=GRATE, tint=GREY, solid=False, vary=0)
        for s in (-1, 1):
            b.cyl(0.32, 1.35, (s * 0.95, -0.4, 0.78), (90, 0, 0), mat=PAINT, tint=ALU, sides=14)
            for y in (-0.85, 0.05):
                b.cyl(0.335, 0.05, (s * 0.95, y, 0.78), (90, 0, 0), mat=PAINT, tint=frame_tint, sides=14,
                      solid=False, vary=0)
            b.box((0.42, 2.5, 0.05), (s * 1.06, 2.28, 1.18), mat=PAINT, tint=frame_tint, solid=False)
            b.box((0.2, 0.04, 0.1), (s * 0.9, 3.7, 1.1), mat=GLOSS, tint=RED, solid=False, vary=0)
        b.box((1.1, 1.1, 0.14), (0, 2.1, 1.2), mat=PAINT, tint=GREY)
        b.box((0.16, 0.5, 0.15), (0, 2.45, 1.22), mat=PAINT, tint=frame_tint, solid=False)
        fifth = b.world((0, 2.1, 1.28))
    return fifth


def skel_trailer(b, at, rot=(0, 0, 0), length=12.4, tint=srgb(0.32, 0.33, 0.34), legs_down=True):
    """A skeletal container trailer: two main beams, cross members, a gooseneck with the king pin at the front
    (-Y), landing legs, a tandem axle at the rear, twistlocks for two 20-foot containers. Returns the deck height."""
    deck = 1.38
    hl = length / 2
    with b.frame(at, rot):
        for x in (-0.5, 0.5):
            b.box((0.16, length - 0.4, 0.38), (x, 0.2, deck - 0.19), mat=PAINT, tint=tint)
        b.box((1.2, 2.0, 0.22), (0, -hl + 1.0, deck - 0.11), mat=PAINT, tint=tint)
        b.cyl(0.06, 0.12, (0, -hl + 0.9, deck - 0.28), mat=PAINT, tint=GREY, sides=8, solid=False)
        for k in range(9):
            y = -hl + 1.5 + k * (length - 2.0) / 8
            b.box((2.44, 0.1, 0.14), (0, y, deck - 0.07), mat=PAINT, tint=tint, solid=False)
        for y in (-hl + 0.15, 0.0, hl - 0.15):
            for s in (-1, 1):
                b.box((0.16, 0.16, 0.12), (s * 1.12, y, deck + 0.02), mat=PAINT, tint=GREY, solid=False)
        if legs_down:
            for s in (-1, 1):
                b.box((0.12, 0.12, deck - 0.2), (s * 0.55, -hl + 2.6, (deck - 0.2) / 2 + 0.1), mat=PAINT, tint=GREY)
                b.box((0.3, 0.3, 0.05), (s * 0.55, -hl + 2.6, 0.04), mat=PAINT, tint=GREY, solid=False)
            b.beam((-0.55, -hl + 2.6, 0.4), (0.55, -hl + 2.6, 0.9), 0.05, 0.05, mat=PAINT, tint=GREY)
        for y in (hl - 2.4, hl - 1.1):
            b.cyl(0.07, 2.0, (0, y, 0.54), (0, 90, 0), mat=PAINT, tint=DARK, sides=8, solid=False)
            for s in (-1, 1):
                b.piece("wheel-road", (s * 1.06, y, 0), (0, 0, 0 if s < 0 else 180))
                b.box((0.42, 1.1, 0.05), (s * 1.06, y, 1.16), mat=PAINT, tint=DARK, solid=False)
        b.box((2.44, 0.12, 0.25), (0, hl - 0.05, 0.75), mat=PAINT, tint=DARK, solid=False)
        for s in (-1, 1):
            b.box((0.25, 0.05, 0.12), (s * 0.9, hl + 0.02, 0.75), mat=GLOSS, tint=RED, solid=False, vary=0)
        stripes(b, (-1.2, hl + 0.02, 0), (1.2, hl + 0.02, 0), 0.12, width=0.2, colours=(RED, SIGN_WHITE),
                at_z=0.88, normal=(0, 1, 0))
    return deck


def container_open(b, at, rot=(0, 0, 0), tint=RUST_RED, doors=(100.0, 170.0), torn=True, floor_tint=WOOD):
    """A 20-foot container burst open (the kit piece's twin, with an inside): corrugated walls, posts and rails, a
    timber floor, its doors (at +X) swung open and, when `torn`, a side wall split open and peeled out. Returns the
    floor's centre in the monument's frame."""
    w, d, h = C20
    hw, hd = w / 2, d / 2
    with b.frame(at, rot):
        b.box((w - 0.1, d - 0.1, 0.12), (0, 0, 0.06), mat=PAINT, tint=floor_tint)
        # The back wall and the roof; the -Y side either whole or split, the +Y side whole.
        grid_box(b, (0.05, d - 0.2, h - 0.25), (-hw + 0.06, 0, h / 2), mat=CORRUGATED, tint=tint, step=1.2)
        grid_box(b, (w - 0.2, d - 0.06, 0.05), (0, 0, h - 0.04), mat=CORRUGATED, tint=tint, step=1.2)
        grid_box(b, (w - 0.2, 0.05, h - 0.25), (0, hd - 0.03, h / 2), mat=CORRUGATED, tint=tint, step=1.2)
        if torn:
            grid_box(b, (2.4, 0.05, h - 0.25), (-hw + 1.3, -hd + 0.03, h / 2), mat=CORRUGATED, tint=tint, step=1.2)
            grid_box(b, (1.2, 0.05, h - 0.25), (hw - 0.7, -hd + 0.03, h / 2), mat=CORRUGATED, tint=tint, step=1.2)
            with b.frame((-hw + 2.5, -hd + 0.03, 0.15), (0, 0, -55.0)):
                grid_box(b, (1.3, 0.05, h - 0.5), (0.65, 0, (h - 0.5) / 2), mat=CORRUGATED, tint=tint, step=1.2)
            with b.frame((hw - 1.3, -hd + 0.03, 0.4), (14.0, 0, 62.0)):
                grid_box(b, (1.0, 0.05, h - 0.9), (-0.5, 0, (h - 0.9) / 2), mat=CORRUGATED, tint=tint, step=1.2)
        else:
            grid_box(b, (w - 0.2, 0.05, h - 0.25), (0, -hd + 0.03, h / 2), mat=CORRUGATED, tint=tint, step=1.2)
        for x in (-hw + 0.07, hw - 0.07):
            for y in (-hd + 0.07, hd - 0.07):
                b.box((0.15, 0.15, h), (x, y, h / 2), mat=PAINT, tint=tint, solid=False)
        for z in (0.08, h - 0.08):
            for y in (-hd + 0.06, hd - 0.06):
                b.box((w, 0.13, 0.16), (0, y, z), mat=PAINT, tint=tint, solid=False)
            b.box((0.13, d, 0.16), (-hw + 0.06, 0, z), mat=PAINT, tint=tint, solid=False)
            b.box((0.13, d, 0.16), (hw - 0.06, 0, z), mat=PAINT, tint=tint, solid=False)
        # Doors at +X, each half the width, hinged at the corners, with lock bars.
        for s, ang in ((-1, -doors[0]), (1, doors[1])):
            with b.frame((hw, s * (hd - 0.05), 0.1), (0, 0, ang)):
                grid_box(b, (0.05, hd - 0.06, h - 0.3), (0.03, -s * (hd / 2), (h - 0.2) / 2), mat=CORRUGATED,
                         tint=tint, step=1.2)
                for yy in (0.3, 0.75):
                    b.cyl(0.022, h - 0.35, (0.08, -s * yy, (h - 0.2) / 2), mat=PAINT, tint=GREY, sides=6,
                          solid=False, vary=0)
        stencil(b, "SW 40 117", (-hw + 0.4, hd + 0.005, h - 0.55), (0, 0, 180), 0.22, SIGN_WHITE)
        centre = b.world((0, 0, 0.12))
    return centre


def guard_rail(b, pts, gap=None, bent=None, tint=GALV):
    """A W-beam crash barrier on posts along the polyline `pts` (x, y), facing -Y of each segment's direction.
    `gap` (a0, a1): the distance along it where the rail is torn away; `bent` (a0, a1): where it is pushed over."""
    p = [Vector((q[0], q[1], 0)) for q in pts]
    run = 0.0
    for a, c in zip(p, p[1:]):
        d = c - a
        length = d.length
        n = max(1, int(length / 2.0))
        yaw = math.degrees(math.atan2(d.y, d.x))
        side = Vector((-d.y, d.x, 0)).normalized()
        for k in range(n):
            s0, s1 = run + length * k / n, run + length * (k + 1) / n
            q0, q1 = a + d * (k / n), a + d * ((k + 1) / n)
            mid = (q0 + q1) / 2
            if gap and gap[0] < (s0 + s1) / 2 < gap[1]:
                continue
            lean = 0.0
            if bent and bent[0] < (s0 + s1) / 2 < bent[1]:
                lean = 35.0 + 30.0 * b.rng.random()
            with b.frame((q0.x, q0.y, 0), (0, 0, yaw)):
                with b.frame((0, 0, 0), (lean, 0, 0)):
                    b.box((0.1, 0.16, 1.0), (0, -0.1, 0.42), mat=PAINT, tint=tint, solid=lean == 0)
            with b.frame((mid.x, mid.y, 0), (0, 0, yaw)):
                with b.frame((0, 0, 0.0), (lean, 0, 0)):
                    b.box((length / n + 0.05, 0.08, 0.32), (0, 0.02, 0.68), mat=PAINT, tint=tint, solid=False)
                    b.box((length / n + 0.05, 0.1, 0.06), (0, 0.05, 0.68), mat=PAINT, tint=tint, solid=False)
            if not lean:
                b.collide_box(mid + Vector((0, 0, 0.45)), (length / n, 0.12, 0.9), (0, 0, yaw))
        run += length


def ribbon(b, pts, width=0.3, z=0.0, tint=SCORCH, mat=MATTE, step=1.0):
    """A thin strip lying on a surface along a smooth curve through `pts` (x, y): tyre marks, paint lines."""
    curve = spline([Vector((q[0], q[1], 0)) for q in pts], step)
    left, right = [], []
    for i, q in enumerate(curve):
        d = curve[min(i + 1, len(curve) - 1)] - curve[max(i - 1, 0)]
        s = Vector((-d.y, d.x, 0)).normalized() * (width / 2)
        left.append((q + s).to_tuple())
        right.append((q - s).to_tuple())
    n = len(curve)
    verts = [(x, y, z + 0.004) for x, y, _ in left] + [(x, y, z + 0.004) for x, y, _ in right]
    verts += [(x, y, z) for x, y, _ in left] + [(x, y, z) for x, y, _ in right]
    faces = []
    for i in range(n - 1):
        faces.append([i, i + 1, n + i + 1, n + i])
        faces.append([2 * n + i, 3 * n + i, 3 * n + i + 1, 2 * n + i + 1])
        faces.append([i, 2 * n + i, 2 * n + i + 1, i + 1])
        faces.append([n + i, n + i + 1, 3 * n + i + 1, 3 * n + i])
    faces.append([0, n, 3 * n, 2 * n])
    faces.append([n - 1, 3 * n - 1, 4 * n - 1, 2 * n - 1])
    b.mesh(verts, faces, mat=mat, tint=tint, solid=False, vary=0, smooth=False)


def _pallet(b, boxes=True):
    """A timber pallet, with or without a load of cartons strapped on."""
    b.box((1.2, 1.0, 0.03), (0, 0, 0.135), mat=PAINT, tint=WOOD)
    for y in (-0.42, 0.0, 0.42):
        b.box((1.2, 0.1, 0.09), (0, y, 0.075), mat=PAINT, tint=WOOD, solid=False)
    for x in (-0.5, 0.0, 0.5):
        b.box((0.1, 1.0, 0.02), (x, 0, 0.02), mat=PAINT, tint=WOOD, solid=False)
    if boxes:
        k = 0
        for z in range(3):
            for x in (-0.3, 0.3):
                for y in (-0.25, 0.25):
                    k += 1
                    b.box((0.56, 0.46, 0.33), (x + 0.01 * (k % 3 - 1), y, 0.15 + 0.165 + z * 0.34),
                          (0, 0, 2.0 * (k % 3 - 1)), mat=MATTE,
                          tint=(srgb(0.62, 0.5, 0.34), srgb(0.56, 0.45, 0.31))[k % 2], solid=False, vary=0.05)
        for x in (-0.3, 0.3):
            b.box((0.04, 1.0, 0.012), (x, 0, 1.18), mat=PAINT, tint=srgb(0.2, 0.4, 0.6), solid=False, vary=0)
            for y in (-0.5, 0.5):
                b.box((0.04, 0.012, 1.03), (x, y, 0.665), mat=PAINT, tint=srgb(0.2, 0.4, 0.6), solid=False, vary=0)
        b.collide_box(Vector((0, 0, 0.6)), (1.2, 1.0, 1.2), (0, 0, 0))
    else:
        b.collide_box(Vector((0, 0, 0.08)), (1.2, 1.0, 0.15), (0, 0, 0))


kit.PIECES["pallet-boxes"] = lambda b: _pallet(b, True)


# ------------------------------------------------------------------------------------------------ tanks


def storage_tank(b, at, radius, height, tint=OFFWHITE, band=None, roof="cone", ladder_at=None, code=None,
                 code_at=0.0, rings=True, mat=PAINT, rail=True, legs=0.0):
    """A vertical storage tank built so its baked occlusion stays clean: an open-topped shell (no vertex shared with
    a cap), a roof that starts a hair above it with its own rim angle, weld rings, a livery band, a stencilled code,
    a caged ladder at angle `ladder_at` (degrees, 0 = +X) with a short rail at the top. On `legs` metres of steel
    when given. Returns the roof's top height."""
    x, y, z0 = at
    z = z0 + legs
    sides = 40 if radius > 3 else 28
    if legs:
        for k in range(4):
            a = math.radians(45 + 90 * k)
            b.box((0.3, 0.3, legs), (x + math.cos(a) * radius * 0.75, y + math.sin(a) * radius * 0.75, z0 + legs / 2),
                  mat=PAINT, tint=DARK)
        b.cyl(radius, 0.12, (x, y, z + 0.06), mat=PAINT, tint=DARK, sides=sides)
    b.cyl(radius, height, (x, y, z + height / 2), mat=mat, tint=tint, sides=sides, cap=False)
    b.cyl(radius - 0.05, 0.05, (x, y, z + 0.03), mat=PAINT, tint=DARK, sides=sides, solid=False)
    if rings:
        n = max(1, int(height / 2.4))
        for i in range(1, n):
            b.cyl(radius + 0.012, 0.05, (x, y, z + height * i / n), mat=mat, tint=tint, sides=sides, solid=False,
                  vary=0)
    if band:
        b.cyl(radius + 0.008, min(0.6, height * 0.14), (x, y, z + height * 0.7), mat=PAINT, tint=band, sides=sides,
              solid=False, vary=0)
    b.cyl(radius + 0.06, 0.1, (x, y, z + height - 0.03), mat=PAINT, tint=GREY, sides=sides, cap=False, solid=False)
    top = z + height + 0.02
    if roof == "cone":
        rise = radius * 0.2
        b.lathe([(radius + 0.04, 0.0), (radius * 0.55, rise * 0.5), (0.35, rise), (0.0, rise)], (x, y, top), mat=mat,
                tint=tint, sides=sides)
        roof_top = top + rise
    elif roof == "dome":
        rise = radius * 0.32
        prof = [(radius + 0.03, 0.0)] + [(radius * math.cos(t), rise * math.sin(t))
                                         for t in (0.25, 0.55, 0.85, 1.15, 1.4)] + [(0.0, rise)]
        b.lathe(prof, (x, y, top), mat=mat, tint=tint, sides=sides)
        roof_top = top + rise
    else:
        b.cyl(radius + 0.04, 0.08, (x, y, top + 0.04), mat=mat, tint=tint, sides=sides)
        roof_top = top + 0.08
    b.cyl(0.3, 0.3, (x, y, roof_top + 0.1), mat=PAINT, tint=GREY, sides=12, solid=False)
    b.cyl(0.36, 0.06, (x, y, roof_top + 0.28), mat=PAINT, tint=GREY, sides=12, solid=False)
    if code:
        a = math.radians(code_at)
        cx, cy = x + math.cos(a) * (radius + 0.005), y + math.sin(a) * (radius + 0.005)
        stencil(b, code, (cx, cy, z + height * 0.42), (0, 0, code_at + 90.0), min(0.6, radius * 0.22), DARK,
                align="centre")
    if ladder_at is not None:
        from .kit import ladder
        a = math.radians(ladder_at)
        lx, ly = x + math.cos(a) * (radius + 0.28), y + math.sin(a) * (radius + 0.28)
        ladder(b, (lx, ly, z0), legs + height, (0, 0, ladder_at - 90.0 + 180.0), cage=True)
        if rail:
            from .kit import railing
            pts = []
            for k in range(9):
                t = a - math.radians(28) + math.radians(56) * k / 8
                pts.append((x + math.cos(t) * (radius - 0.12), y + math.sin(t) * (radius - 0.12), top + 0.05))
            for p, q in zip(pts, pts[1:]):
                railing(b, p, q, 1.0, spacing=2.0, toe=False)
    return roof_top


# ------------------------------------------------------------------------------------------------ crawler, tanker


def jerry_can(b, at, yaw=0.0, tint=srgb(0.62, 0.16, 0.10), lying=False):
    """A 20-litre fuel can, standing or on its side."""
    x, y, z = at
    rot = (0, 0, yaw) if not lying else (90, 0, yaw)
    with b.frame((x, y, z + (0.08 if lying else 0)), rot):
        b.box((0.17, 0.35, 0.47), (0, 0, 0.235), mat=PAINT, tint=tint, bevel=0.015, solid=False)
        b.box((0.04, 0.12, 0.06), (0, -0.08, 0.5), mat=PAINT, tint=tint, solid=False)
        b.cyl(0.025, 0.06, (0, 0.11, 0.49), (-35, 0, 0), mat=PAINT, tint=GREY, sides=6, solid=False)


def toolbox(b, at, yaw=0.0, tint=RED, open_lid=False):
    """A steel toolbox with a handle (lid open or shut)."""
    with b.frame(at, (0, 0, yaw)):
        b.box((0.55, 0.25, 0.24), (0, 0, 0.12), mat=PAINT, tint=tint, solid=False)
        if open_lid:
            b.box((0.55, 0.25, 0.03), (0, 0.22, 0.32), (70, 0, 0), mat=PAINT, tint=tint, solid=False)
        else:
            b.box((0.4, 0.03, 0.03), (0, 0, 0.3), mat=PAINT, tint=DARK, solid=False)


def _wheel_crawler(b):
    """A crawler's road wheel: a 1.6 m flotation tyre with chunky staggered lugs on a deep steel rim with twelve wheel
    nuts. On the ground with its axle along X, outer face toward -X."""
    r, w = 0.8, 0.62
    hw = w / 2
    prof = [(0.42, -hw + 0.02), (0.5, -hw - 0.015), (0.66, -hw - 0.03), (0.75, -hw + 0.0), (0.77, -hw + 0.06),
            (0.77, hw - 0.06), (0.75, hw), (0.66, hw + 0.03), (0.5, hw + 0.015), (0.42, hw - 0.02)]
    with b.frame((0, 0, r), (0, 90, 0)):
        b.lathe(prof, (0, 0, 0), mat=MATTE, tint=RUBBER, sides=32, close=False, solid="hull", wear=0.6)
        for k in range(22):
            a = 2 * math.pi * k / 22
            for s in (-1, 1):
                if (k % 2 == 0) == (s > 0):
                    continue
                c = Vector((math.cos(a) * 0.785, math.sin(a) * 0.785, s * hw * 0.45))
                b.box((0.09, 0.2, hw * 0.95), tuple(c), (0, 0, math.degrees(a)), mat=MATTE, tint=RUBBER,
                      solid=False, vary=0)
        b.cyl(0.425, w - 0.06, (0, 0, 0), mat=PAINT, tint=srgb(0.32, 0.33, 0.33), sides=20, solid=False)
        b.lathe([(0.42, 0.0), (0.33, -0.08), (0.2, -0.1), (0.18, -0.16), (0.0, -0.17)], (0, 0, -hw + 0.04),
                mat=PAINT, tint=srgb(0.82, 0.8, 0.76), sides=20, close=False, solid=False)
        for k in range(12):
            a = 2 * math.pi * k / 12
            b.cyl(0.024, 0.05, (math.cos(a) * 0.15, math.sin(a) * 0.15, -hw - 0.12), mat=PAINT, tint=GREY, sides=6,
                  solid=False, vary=0)
        b.cyl(0.09, 0.08, (0, 0, -hw - 0.14), mat=PAINT, tint=GREY, sides=12, solid=False)


kit.PIECES["wheel-crawler"] = _wheel_crawler


def crawler(b, at, rot=(0, 0, 0), tint=OFFWHITE, band=ORANGE, stripe=TEAL, code="CR-09", missing=(), door_open=72.0,
            lights=True):
    """A Seedward six-wheeled overland crawler: a high tub on trailing arms, a hollow cab with a raked windscreen and
    a door (right side) swung open over its steps, a long hab module behind with a roof rack, a light bar, a winch,
    fenders, livery. Front toward -Y. `missing`: wheel indices (0..5, front left, front right, ...) taken off.
    Returns (cab floor centre, hitch point) in the monument's frame."""
    W, floor = 2.9, 1.78
    hw = W / 2
    axles = (-4.2, -0.4, 3.3)
    with b.frame(at, rot):
        # Tub, trailing arms, struts, wheels.
        b.box((2.5, 11.0, 0.62), (0, 0.1, floor - 0.33), mat=PAINT, tint=srgb(0.27, 0.28, 0.28))
        b.box((2.7, 11.3, 0.12), (0, 0.1, floor - 0.04), mat=PAINT, tint=DARK)
        for i, y in enumerate(axles):
            for s in (-1, 1):
                k = 2 * i + (0 if s < 0 else 1)
                b.beam((s * 1.15, y + 0.9, floor - 0.45), (s * 1.38, y, 0.82), 0.16, 0.2, mat=PAINT, tint=DARK)
                b.cyl(0.06, 0.8, (s * 1.25, y - 0.35, 1.3), (12 * s, 0, 0), mat=PAINT, tint=YELLOW, sides=8,
                      solid=False)
                if k in missing:
                    b.cyl(0.3, 0.06, (s * 1.62, y, 0.8), (0, 90, 0), mat=RUST, tint=(1, 1, 1), sides=16, solid=False)
                    b.cyl(0.14, 0.18, (s * 1.7, y, 0.8), (0, 90, 0), mat=PAINT, tint=GREY, sides=12, solid=False)
                    for j in range(8):
                        a = 2 * math.pi * j / 8
                        b.cyl(0.02, 0.09, (s * 1.67, y + math.cos(a) * 0.17, 0.8 + math.sin(a) * 0.17),
                              (0, 90, 0), mat=PAINT, tint=GREY, sides=6, solid=False, vary=0)
                else:
                    b.piece("wheel-crawler", (s * 1.75, y, 0), (0, 0, 0 if s < 0 else 180))
                # Fender over the wheel.
                with b.frame((s * 1.75, y, 0.8), (0, 90, 0)):
                    arc_panel(b, 1.0, 100.0, 260.0, -0.36, 0.36,
                                      0.04, 8, mat=PAINT, tint=srgb(0.3, 0.31, 0.3))
        # The cab: floor, nose, roof, walls, raked windscreen between its pillars, the door.
        y0, y1 = -5.85, -2.2
        b.box((W, y1 - y0, 0.1), (0, (y0 + y1) / 2, floor + 0.05), mat=PAINT, tint=GREY)
        b.box((W, 0.5, 0.62), (0, y0 + 0.2, floor + 0.36), mat=PANEL, tint=tint)
        roof_y0 = -4.95
        grid_box(b, (W, y1 - roof_y0, 0.1), (0, (roof_y0 + y1) / 2, floor + 1.95), mat=PANEL, tint=tint)
        ws = math.degrees(math.atan2(roof_y0 - (y0 + 0.05), 1.95 - 0.67))
        with b.frame((0, y0 + 0.05, floor + 0.67), (-ws, 0, 0)):
            length = math.hypot(roof_y0 - (y0 + 0.05), 1.95 - 0.67)
            b.box((W - 0.3, 0.03, length), (0, 0, length / 2), mat=GLASS, solid=False)
            for s in (-1, 1):
                b.box((0.14, 0.1, length + 0.1), (s * (hw - 0.07), 0, length / 2), mat=PANEL, tint=tint)
        b.box((W - 0.1, 0.4, 0.3), (0, y0 + 0.75, floor + 0.85), mat=PAINT, tint=DARK, solid=False)   # dash
        door0, door1 = -3.2, -2.25
        f0, f1 = floor + 0.05, floor + 1.95

        def side(points, x):
            """A cab side wall: an outline in (y, z), 6 cm thick at x."""
            with b.frame((x, 0, 0), (0, 0, 90)):
                b.extrude(points, 0.06, (0, 0, 0), (0, 0, 0), mat=PANEL, tint=tint)

        for s in (-1, 1):
            wx = s * (hw - 0.03)
            if s < 0:
                side([(y0 + 0.05, f0), (y1, f0), (y1, f1), (roof_y0, f1), (y0 + 0.05, floor + 0.67)], wx)
                b.box((0.03, 1.4, 0.8), (wx - 0.03, -3.5, floor + 1.35), mat=GLASS, solid=False)
            else:
                side([(y0 + 0.05, f0), (door0, f0), (door0, f1), (roof_y0, f1), (y0 + 0.05, floor + 0.67)], wx)
                side([(door1, f0), (y1, f0), (y1, f1), (door1, f1)], wx)
                b.box((0.03, 0.9, 0.7), (wx + 0.03, -3.9, floor + 1.35), mat=GLASS, solid=False)
                with b.frame((wx + 0.03, door0, floor + 0.05), (0, 0, -door_open)):
                    grid_box(b, (0.06, door1 - door0 - 0.04, 1.85), (0.03, (door1 - door0) / 2, 0.95), mat=PANEL,
                             tint=tint)
                    b.box((0.03, 0.7, 0.6), (0.07, (door1 - door0) / 2, 1.35), mat=GLASS, solid=False)
                    b.box((0.012, door1 - door0 - 0.1, 0.24), (0.065, (door1 - door0) / 2, 0.75), mat=PAINT,
                          tint=band, solid=False, vary=0)
                from .kit import stairs
                stairs(b, (wx + 1.12, (door0 + door1) / 2, 0), (0, 0, 90), 0.8, floor + 0.05, 1.1, tint=GREY,
                       rails=False)
        grid_box(b, (W - 0.12, 0.06, 1.9), (0, y1 - 0.03, floor + 1.0), mat=PANEL, tint=tint)
        for x in (-0.6, 0.6):           # seats
            b.box((0.55, 0.55, 0.45), (x, -4.6, floor + 0.32), mat=MATTE, tint=srgb(0.2, 0.22, 0.24), solid=False)
            b.box((0.55, 0.12, 0.7), (x, -4.25, floor + 0.85), (-10, 0, 0), mat=MATTE, tint=srgb(0.2, 0.22, 0.24),
                  solid=False)
        cab_floor = b.world((0.0, -3.4, floor + 0.1))
        # The hab module: a long box with windows, a sliding door half open, a rear door and ladder.
        hy0, hy1 = -2.2, 5.75
        hh = 2.05
        grid_box(b, (W, hy1 - hy0, hh), (0, (hy0 + hy1) / 2, floor + hh / 2), mat=PANEL, tint=tint, step=1.0)
        for s in (-1, 1):
            for k, y in enumerate((-1.2, 1.0, 3.6)):
                if s > 0 and k == 1:
                    continue
                b.box((0.03, 1.1, 0.6), (s * (hw + 0.012), y, floor + 1.35), mat=GLASS, solid=False)
            b.box((0.012, hy1 - hy0 - 0.3, 0.36), (s * (hw + 0.008), (hy0 + hy1) / 2, floor + 0.62), mat=PAINT,
                  tint=band, solid=False, vary=0, wear=0.5)
            b.box((0.012, hy1 - hy0 - 0.3, 0.07), (s * (hw + 0.008), (hy0 + hy1) / 2, floor + 0.36), mat=PAINT,
                  tint=stripe, solid=False, vary=0, wear=0.5)
            stencil(b, code, (s * (hw + 0.015), 2.4 if s < 0 else 4.3, floor + 1.0), (0, 0, 90 * s), 0.3, DARK)
        # Sliding door (right side), slid back and open: a dark doorway, the leaf over the wall.
        b.box((0.03, 1.2, 1.9), (hw + 0.012, 1.0, floor + 0.97), mat=MATTE, tint=srgb(0.03, 0.03, 0.035),
              solid=False, vary=0)
        b.box((0.05, 1.25, 1.95), (hw + 0.06, 2.15, floor + 0.99), mat=PANEL, tint=tint, solid=False)
        b.box((0.04, 0.04, 2.0), (hw + 0.09, 1.55, floor + 1.0), mat=PAINT, tint=DARK, solid=False)
        # Front: bumper, winch, lamps; light bar and antenna on the roof; rear ladder and hitch.
        b.box((W + 0.1, 0.35, 0.42), (0, y0 - 0.15, 1.22), mat=PAINT, tint=DARK, bevel=0.03)
        b.cyl(0.17, 0.9, (0, y0 - 0.38, 1.22), (0, 90, 0), mat=PAINT, tint=GREY, sides=14, solid=False)
        b.cyl(0.19, 0.12, (-0.5, y0 - 0.38, 1.22), (0, 90, 0), mat=PAINT, tint=YELLOW, sides=14, solid=False)
        b.cyl(0.19, 0.12, (0.5, y0 - 0.38, 1.22), (0, 90, 0), mat=PAINT, tint=YELLOW, sides=14, solid=False)
        b.box((0.12, 0.2, 0.12), (0, y0 - 0.62, 1.12), mat=PAINT, tint=YELLOW, solid=False)
        for s in (-1, 1):
            b.box((0.42, 0.06, 0.2), (s * 0.95, y0 - 0.02, floor + 0.45), mat=GLOSS, tint=srgb(0.9, 0.9, 0.85),
                  solid=False, vary=0)
        b.box((2.2, 0.25, 0.14), (0, roof_y0 + 0.3, floor + 2.1), mat=PAINT, tint=DARK, solid=False)
        if lights:
            from .kit import lamp
            for x in (-0.75, -0.25, 0.25, 0.75):
                lamp(b, (x, roof_y0 + 0.22, floor + 2.26), (-12, 0, 0), size=(0.36, 0.18, 0.2))
        b.cyl(0.02, 3.0, (0.9, 4.8, floor + hh + 1.5), mat=PAINT, tint=BLACK, sides=5, solid=False)
        # Roof rack with cargo cases, cans and a bundle under a tarp.
        top = floor + hh
        for y in (-1.8, 1.0, 3.8):
            b.box((W - 0.2, 0.06, 0.06), (0, y, top + 0.22), mat=PAINT, tint=GREY, solid=False)
        for s in (-1, 1):
            b.box((0.06, 6.0, 0.06), (s * (hw - 0.15), 1.0, top + 0.22), mat=PAINT, tint=GREY, solid=False)
            for y in (-1.8, 1.0, 3.8):
                b.box((0.05, 0.05, 0.22), (s * (hw - 0.15), y, top + 0.11), mat=PAINT, tint=GREY, solid=False)
        b.box((1.0, 0.7, 0.45), (-0.6, -1.0, top + 0.47), mat=PAINT, tint=srgb(0.3, 0.35, 0.3), solid=False)
        b.box((0.8, 0.6, 0.4), (0.6, -0.9, top + 0.45), mat=PAINT, tint=ORANGE, solid=False)
        drape(b, (0.0, 2.6, top + 0.24), (2.2, 2.0), 90.0, 0.4, 3.0, TARP_GREEN, cells=(8, 6))
        for k in range(3):
            jerry_can(b, (-0.9 + 0.25 * k, 4.6, top + 0.25), 90.0)
        # Rear: ladder up to the rack, the hitch.
        for s in (-1, 1):
            b.box((0.05, 0.05, top + 0.2 - 0.6), (s * 0.25 - 0.6, hy1 + 0.06, (top + 0.8) / 2), mat=PAINT,
                  tint=GREY, solid=False)
        for k in range(int((top - 0.6) / 0.3)):
            b.box((0.5, 0.04, 0.04), (-0.6, hy1 + 0.06, 0.7 + k * 0.3), mat=PAINT, tint=GREY, solid=False, vary=0)
        b.marker("ladder", (-0.6, hy1 + 0.4, 0.0), height=round(top, 2))
        b.box((0.2, 0.9, 0.2), (0, hy1 + 0.4, 0.95), mat=PAINT, tint=DARK)
        hitch = b.world((0, hy1 + 0.85, 0.95))
    return cab_floor, hitch


def tanker_trailer(b, at, rot=(0, 0, 0), tint=OFFWHITE, band=ORANGE, code="F-3", length=6.0, radius=1.05):
    """A two-axle fuel tanker trailer: drawbar toward -Y, a round tank on saddles with a walkway, hatches and a
    ladder, a valve cabinet and hose reel at the rear. Returns the tank's rear valve position (fuel loot goes
    there) in the monument's frame."""
    zc = 1.25 + radius
    hl = length / 2
    with b.frame(at, rot):
        for x in (-0.5, 0.5):
            b.box((0.14, length + 0.4, 0.24), (x, 0.2, 1.12), mat=PAINT, tint=DARK)
            b.beam((x, -hl + 0.2, 1.1), (0, -hl - 1.6, 0.95), 0.1, 0.12, mat=PAINT, tint=DARK)
        b.box((0.18, 0.3, 0.14), (0, -hl - 1.7, 0.95), mat=PAINT, tint=GREY, solid=False)
        wheel(b, (0.3, -hl - 0.8, 0.14), (0, 0, 0), 0.14, 0.08, sides=10)
        b.cyl(0.045, 0.85, (0.3, -hl - 0.8, 0.6), mat=PAINT, tint=GREY, sides=8, solid=False)
        for y in (hl - 2.0, hl - 0.8):
            b.cyl(0.06, 2.0, (0, y, 0.54), (0, 90, 0), mat=PAINT, tint=DARK, sides=8, solid=False)
            for s in (-1, 1):
                b.piece("wheel-road", (s * 1.06, y, 0), (0, 0, 0 if s < 0 else 180))
        for s in (-1, 1):
            b.box((0.42, 2.6, 0.05), (s * 1.06, hl - 1.4, 1.18), mat=PAINT, tint=DARK, solid=False)
        # The tank: shell, domed ends, saddles, bands.
        b.cyl(radius, length, (0, 0.2, zc), (90, 0, 0), mat=PAINT, tint=tint, sides=28)
        for s in (-1, 1):
            b.lathe([(radius, 0.0), (radius * 0.9, 0.18), (radius * 0.6, 0.3), (0.0, 0.34)],
                    (0, 0.2 + s * hl, zc), (-90 * s, 0, 0), mat=PAINT, tint=tint, sides=28)
        for y in (-hl + 0.8, 0.2, hl - 0.4):
            b.box((1.6, 0.25, 0.5), (0, y, 1.42), mat=PAINT, tint=DARK, solid=False)
            b.cyl(radius + 0.02, 0.12, (0, y, zc), (90, 0, 0), mat=PAINT, tint=DARK, sides=28, solid=False, vary=0)
        b.cyl(radius + 0.012, 0.6, (0, -0.8, zc), (90, 0, 0), mat=PAINT, tint=band, sides=28, solid=False, vary=0)
        for s in (-1, 1):
            stencil(b, code, (s * (radius + 0.02), 1.4 if s < 0 else -1.0 + 0.0, zc - 0.15), (0, 0, 90 * s), 0.34,
                    DARK, align="left")
            plate(b, (s * (radius + 0.03), 2.4, zc + 0.1), (0, 0, 90 * s), (0.4, 0.3), ORANGE, BLACK, "33",
                  text_h=0.12)
        # Walkway, rails, hatches, the ladder at the rear.
        b.box((0.6, length - 0.8, 0.05), (0, 0.2, zc + radius + 0.06), mat=GRATE, tint=GREY, solid=True, vary=0)
        for y in (-1.5, 0.4, 2.2):
            b.cyl(0.25, 0.14, (0, y, zc + radius + 0.12), mat=PAINT, tint=GREY, sides=14, solid=False)
        for s in (-1, 1):
            b.box((0.04, length - 1.0, 0.04), (s * 0.35, 0.2, zc + radius + 1.0), mat=PAINT, tint=YELLOW,
                  solid=False)
            for y in (-hl + 0.7, 0.2, hl - 0.6):
                b.box((0.04, 0.04, 0.95), (s * 0.35, y, zc + radius + 0.55), mat=PAINT, tint=YELLOW, solid=False)
        for k in range(int((zc + radius - 0.4) / 0.3)):
            b.box((0.45, 0.04, 0.04), (0.45, hl + 0.55, 0.5 + k * 0.3), mat=PAINT, tint=GREY, solid=False, vary=0)
        for x in (0.22, 0.68):
            b.box((0.05, 0.05, zc + radius), (x, hl + 0.55, (zc + radius) / 2), mat=PAINT, tint=GREY, solid=False)
        b.marker("ladder", (0.45, hl + 0.9, 0.0), height=round(zc + radius, 2))
        # Rear cabinet with valves and a hose reel.
        b.box((1.4, 0.5, 0.7), (-0.4, hl + 0.35, 1.0), mat=PAINT, tint=GREY, solid=False)
        for x in (-0.8, -0.4, 0.0):
            b.cyl(0.07, 0.2, (x, hl + 0.65, 1.05), (90, 0, 0), mat=PAINT, tint=YELLOW, sides=10, solid=False)
        b.cyl(0.35, 0.3, (-0.4, hl + 0.7, 1.75), (0, 90, 0), mat=PAINT, tint=RED, sides=14, solid=False)
        b.cyl(0.28, 0.32, (-0.4, hl + 0.7, 1.75), (0, 90, 0), mat=PAINT, tint=BLACK, sides=14, solid=False)
        for s in (-1, 1):
            b.box((0.2, 0.04, 0.1), (s * 0.9, hl + 0.62, 0.85), mat=GLOSS, tint=RED, solid=False, vary=0)
        valve = b.world((-0.4, hl + 1.1, 0.0))
    return valve


# ------------------------------------------------------------------------------------------------ Bulwark

SANDBAG = srgb(0.56, 0.51, 0.39)
OLIVE_DRAB = srgb(0.36, 0.40, 0.26)


def _sandbags_2m(b):
    """A 2 m run of sandbags six rows high (a metre of cover), staggered and battered, along X."""
    for row in range(6):
        inset = row * 0.025
        bags = ((-0.66, 0.62), (0.0, 0.62), (0.66, 0.62)) if row % 2 == 0 else \
            ((-0.84, 0.3), (-0.34, 0.62), (0.32, 0.62), (0.84, 0.3))
        for x, w in bags:
            b.box((w, 0.4 - inset, 0.17), (x, 0, 0.085 + row * 0.158), (0, 0, b.rng.uniform(-5, 5)), mat=MATTE,
                  tint=SANDBAG, bevel=0.05, solid=False, vary=0.1, wear=0.6)
    # A core inside the bags: hidden up close, it is what the farthest level keeps (it drops parts under 1 m).
    b.box((1.96, 0.26, 0.85), (0, 0, 0.43), mat=MATTE, tint=SANDBAG, solid=False, vary=0, wear=0.6)
    b.collide_box(Vector((0, 0, 0.5)), (2.0, 0.42, 1.0), (0, 0, 0))


def _concertina_5m(b):
    """Five metres of concertina wire along X on two angle pickets: a helix of razor wire 0.9 m across."""
    r, pitch, per = 0.45, 0.32, 10
    turns = int(5.0 / pitch)
    pts = []
    for i in range(turns * per + 1):
        a = 2 * math.pi * i / per
        x = -2.5 + pitch * i / per
        pts.append((x, r * math.cos(a) * (1 + 0.05 * math.sin(i * 1.7)), r + r * math.sin(a)))
    b.tube(pts, 0.007, mat=PAINT, tint=srgb(0.55, 0.56, 0.57), sides=4, cap=False, solid=False)
    for x in (-2.0, 1.0):
        b.box((0.05, 0.05, 1.2), (x, 0.1, 0.5), mat=RUST, tint=(1, 1, 1), solid=False)
    b.collide_box(Vector((0, 0, 0.45)), (5.0, 0.9, 0.9), (0, 0, 0))


kit.PIECES["sandbags-2m"] = _sandbags_2m
kit.PIECES["concertina-5m"] = _concertina_5m


def bullet_holes(b, at, rot, w, h, count=12, size=0.025, tint=srgb(0.05, 0.05, 0.05)):
    """Bullet holes on a surface: dark pits (with a bright ring of bare metal) scattered over `w` x `h` metres of the
    surface at `at` facing -Y of `rot`, clustered as from bursts."""
    with b.frame(at, rot):
        verts, faces = [], []
        bursts = max(1, count // 5)
        centres = [((b.rng.random() - 0.5) * w * 0.8, (b.rng.random() - 0.5) * h * 0.8) for _ in range(bursts)]
        for k in range(count):
            cx, cz = centres[k % bursts]
            x = max(-w / 2, min(w / 2, cx + b.rng.gauss(0, w * 0.12)))
            z = max(-h / 2, min(h / 2, cz + b.rng.gauss(0, h * 0.12)))
            s = size * (0.7 + 0.6 * b.rng.random())
            _box_into(verts, faces, (x - s, -0.006, z - s), (x + s, 0.0, z + s))
        b.mesh(verts, faces, mat=MATTE, tint=tint, solid=False, vary=0, smooth=False)


def ammo_box(b, at, yaw=0.0, tint=OLIVE_DRAB, lid_open=False):
    """A steel ammunition box with a carry handle and a stencilled lot number."""
    with b.frame(at, (0, 0, yaw)):
        b.box((0.3, 0.16, 0.2), (0, 0, 0.1), mat=PAINT, tint=tint, solid=False)
        if lid_open:
            b.box((0.3, 0.02, 0.17), (0, 0.09, 0.28), (-20, 0, 0), mat=PAINT, tint=tint, solid=False)
        else:
            b.box((0.31, 0.17, 0.03), (0, 0, 0.21), mat=PAINT, tint=tint, solid=False)
            b.box((0.12, 0.03, 0.02), (0, 0, 0.235), mat=PAINT, tint=DARK, solid=False)
        stencil(b, "7.6", (0, -0.081, 0.06), (0, 0, 0), 0.05, srgb(0.9, 0.85, 0.3), align="centre")


def boom_barrier(b, at, yaw=0.0, length=6.5, raised=0.0, broken=False):
    """A road boom: a post with a counterweighted pivot, a red and white arm across the road (along the frame's +X)
    resting on a fork post; `raised` degrees lifts it, `broken` snaps it halfway and drops the end."""
    with b.frame(at, (0, 0, yaw)):
        b.box((0.7, 0.7, 0.3), (0, 0, 0.1), mat=CONCRETE, tint=srgb(0.7, 0.7, 0.67))
        b.box((0.35, 0.35, 1.05), (0, 0, 0.75), mat=PAINT, tint=OLIVE_DRAB, bevel=0.02)
        b.box((0.36, 0.36, 0.12), (0, 0, 1.0), mat=PAINT, tint=YELLOW, solid=False, vary=0)
        with b.frame((0, 0, 1.1), (0, -raised, 0)):
            b.cyl(0.07, 0.5, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=GREY, sides=10, solid=False)
            b.box((0.7, 0.3, 0.35), (-0.55, 0, 0), mat=PAINT, tint=DARK)
            seg = 0.5
            n = int(length / seg)
            end = n if not broken else n // 2
            for k in range(end):
                b.box((seg, 0.1, 0.12), (0.25 + seg * (k + 0.5), 0, 0), mat=PAINT, tint=RED if k % 2 else SIGN_WHITE,
                      solid=False, vary=0, wear=0.55)
        if broken:
            # The far half fell and lies against the fork post.
            x0 = 0.25 + seg * (n // 2)
            with b.frame((x0 + 0.2, 0.1, 0.08), (0, -6, 4)):
                for k in range(n - n // 2):
                    b.box((seg, 0.1, 0.12), (seg * (k + 0.5), 0, 0), mat=PAINT, tint=RED if (k + n // 2) % 2 else
                          SIGN_WHITE, solid=False, vary=0, wear=0.6)
        # Fork rest at the far side.
        b.box((0.12, 0.12, 1.0), (length + 0.1, 0, 0.5), mat=PAINT, tint=OLIVE_DRAB)
        for y in (-0.1, 0.1):
            b.box((0.04, 0.04, 0.2), (length + 0.1, y, 1.08), mat=PAINT, tint=YELLOW, solid=False)
        if raised < 30 and not broken:
            b.collide_box(Vector((length / 2 + 0.3, 0, 1.1)), (length, 0.12, 0.14), (0, 0, 0))


def guard_booth(b, at, rot=(0, 0, 0), w=2.6, d=2.2, h=2.7, tint=BULWARK_GREY, trim=OLIVE_DRAB):
    """A Bulwark guard booth: armoured concrete walls with a band of firing slits on three sides, a heavy roof slab
    with a drip edge, a 2.3 m steel door in the back (+Y) wall, a desk with a radio inside. Returns the floor's
    centre in the monument's frame."""
    t = 0.25
    hw, hd = w / 2, d / 2
    sill, slit = 1.15, 0.3
    with b.frame(at, rot):
        b.box((w + 0.3, d + 0.3, 0.2), (0, 0, 0.1), mat=CONCRETE, tint=srgb(0.7, 0.7, 0.67))
        z0 = 0.2
        # Lower walls all round, upper walls above the slits; posts at the corners hold the roof.
        for (cx, cy, sx, sy, side) in ((0, -hd + t / 2, w, t, "f"), (-hw + t / 2, 0, t, d - 2 * t, "l"),
                                       (hw - t / 2, 0, t, d - 2 * t, "r")):
            grid_box(b, (sx, sy, sill), (cx, cy, z0 + sill / 2), mat=CONCRETE, tint=tint, step=0.8)
            grid_box(b, (sx, sy, h - sill - slit), (cx, cy, z0 + sill + slit + (h - sill - slit) / 2), mat=CONCRETE,
                     tint=tint, step=0.8)
        for sx in (-1, 1):
            for sy in (-1, 1):
                b.box((t, t, slit), (sx * (hw - t / 2), sy * (hd - t / 2), z0 + sill + slit / 2), mat=CONCRETE,
                      tint=tint)
        # Glass in the slits (some shot out), a steel frame.
        for (cx, cy, sx, sy) in ((0, -hd + 0.05, w - 2 * t, 0.03), (-hw + 0.05, 0, 0.03, d - 2 * t),
                                 (hw - 0.05, 0, 0.03, d - 2 * t)):
            if b.rng.random() < 0.6:
                b.box((sx, sy, slit - 0.04), (cx, cy, z0 + sill + slit / 2), mat=GLASS, solid=False)
        # Back wall with the door opening (door open, swung out).
        dw = 0.9
        dx = hw - t - dw / 2 - 0.15
        left = (dx - dw / 2) - (-hw)
        grid_box(b, (left, t, h), (-hw + left / 2, hd - t / 2, z0 + h / 2), mat=CONCRETE, tint=tint, step=0.8)
        right = hw - (dx + dw / 2)
        if right > 0.02:
            b.box((right, t, h), (hw - right / 2, hd - t / 2, z0 + h / 2), mat=CONCRETE, tint=tint)
        b.box((dw, t, h - 2.3), (dx, hd - t / 2, z0 + 2.3 + (h - 2.3) / 2), mat=CONCRETE, tint=tint)
        with b.frame((dx + dw / 2, hd + 0.02, z0), (0, 0, -100.0)):
            grid_box(b, (dw, 0.06, 2.28), (-dw / 2, 0.03, 1.14), mat=PAINT, tint=trim, step=0.8)
            b.box((0.04, 0.06, 0.14), (-dw + 0.1, 0.09, 1.05), mat=PAINT, tint=GREY, solid=False)
        b.marker("door", (dx, hd + 0.6, 0.0), kind_of="booth")
        # Roof slab with a drip edge, a vent and an antenna.
        b.box((w + 0.5, d + 0.5, 0.22), (0, 0, z0 + h + 0.11), mat=CONCRETE, tint=tint)
        b.box((w + 0.56, d + 0.56, 0.06), (0, 0, z0 + h + 0.02), mat=PAINT, tint=trim, solid=False)
        b.cyl(0.12, 0.3, (-0.6, 0.4, z0 + h + 0.37), mat=PAINT, tint=GREY, sides=10, solid=False)
        b.cyl(0.015, 2.4, (0.8, 0.6, z0 + h + 1.4), mat=PAINT, tint=BLACK, sides=5, solid=False)
        # Inside: a desk under the front slits, a radio, a chair.
        b.box((w - 2 * t - 0.1, 0.6, 0.06), (0, -hd + t + 0.3, z0 + 0.95), mat=PAINT, tint=srgb(0.35, 0.36, 0.33),
              solid=False)
        b.box((0.06, 0.55, 0.92), (-hw + t + 0.1, -hd + t + 0.3, z0 + 0.46), mat=PAINT, tint=DARK, solid=False)
        b.box((0.4, 0.25, 0.22), (-0.4, -hd + t + 0.3, z0 + 1.09), mat=PAINT, tint=OLIVE_DRAB, solid=False)
        b.box((0.3, 0.02, 0.1), (-0.4, -hd + t + 0.17, z0 + 1.12), mat=PAINT, tint=DARK, solid=False)
        b.box((0.45, 0.45, 0.05), (0.3, 0.0, z0 + 0.46), mat=PAINT, tint=DARK, solid=False)
        for x in (0.1, 0.5):
            for y in (-0.2, 0.2):
                b.box((0.03, 0.03, 0.45), (x, y, z0 + 0.22), mat=PAINT, tint=DARK, solid=False, vary=0)
        centre = b.world((0.0, 0.25, z0))
    return centre


def military_truck(b, at, rot=(0, 0, 0), tint=OLIVE_DRAB, canvas=CANVAS, code="BW 3-118", shot=True, torn=0.25,
                   flat=(), cab_open=True):
    """A Bulwark six-wheeled troop truck: a bonneted cab, a cargo bed with drop sides and a canvas tilt on hoops
    (`torn` leaves part of the canvas gone), spare wheel, jerry cans, a ring mount over the cab. Front toward -Y.
    `shot` riddles it with bullet holes and breaks the windscreen; `flat`: wheel indices (0..5) with flat tyres.
    Returns the bed's floor centre in the monument's frame."""
    hw = 1.2
    deck = 1.35
    with b.frame(at, rot):
        for x in (-0.48, 0.48):
            b.box((0.18, 7.0, 0.28), (x, 0.0, 0.98), mat=PAINT, tint=DARK)
        for i, y in enumerate((-2.55, 1.35, 2.75)):
            b.cyl(0.07, 1.9, (0, y, 0.54), (0, 90, 0), mat=PAINT, tint=DARK, sides=8, solid=False)
            for s in (-1, 1):
                k = 2 * i + (0 if s < 0 else 1)
                sink = 0.07 if k in flat else 0.0
                b.piece("wheel-road", (s * 1.0, y, -sink), (0, 0, 0 if s < 0 else 180))
        # Bonnet and front wings.
        prof = [(-3.7, 1.15), (-2.05, 1.15), (-2.05, 2.05), (-3.55, 1.95), (-3.72, 1.8)]
        with b.frame((0, 0, 0), (0, 0, 90)):
            b.extrude(prof, 1.3, (0, 0, 0), (0, 0, 0), mat=PAINT, tint=tint, bevel=0.03)
        for s in (-1, 1):
            b.box((0.5, 1.6, 0.06), (s * 0.95, -2.75, 1.65), mat=PAINT, tint=tint)
            b.box((0.5, 0.06, 0.5), (s * 0.95, -3.55, 1.4), mat=PAINT, tint=tint, solid=False)
            b.cyl(0.13, 0.08, (s * 0.95, -3.6, 1.82), (90, 0, 0), mat=PAINT, tint=DARK, sides=12, solid=False)
            b.cyl(0.1, 0.02, (s * 0.95, -3.645, 1.82), (90, 0, 0), mat=GLOSS, tint=srgb(0.8, 0.8, 0.75), sides=12,
                  solid=False, vary=0)
        b.box((1.1, 0.04, 0.7), (0, -3.73, 1.55), mat=PAINT, tint=DARK, solid=False)
        for k in range(7):
            b.box((0.05, 0.05, 0.62), (-0.42 + 0.14 * k, -3.76, 1.55), mat=PAINT, tint=tint, solid=False, vary=0)
        b.box((2.3, 0.22, 0.25), (0, -3.85, 1.0), mat=PAINT, tint=DARK, bevel=0.02)
        for s in (-1, 1):
            b.box((0.12, 0.2, 0.18), (s * 0.7, -3.99, 1.0), mat=PAINT, tint=YELLOW, solid=False)
        # Cab: walls and roof, windscreen panes, doors with windows, the ring mount.
        c0, c1 = -2.05, -0.5
        grid_box(b, (2.3, c1 - c0, 0.08), (0, (c0 + c1) / 2, 2.95), mat=PAINT, tint=tint, step=0.8)
        grid_box(b, (2.3, 0.08, 1.8), (0, c1 - 0.04, 2.05), mat=PAINT, tint=tint, step=0.8)
        b.box((2.3, c1 - c0, 0.1), (0, (c0 + c1) / 2, 1.2), mat=PAINT, tint=DARK)
        for x in (-1.12, 0.0, 1.12):
            b.box((0.08, 0.08, 0.9), (x, c0 + 0.04, 2.5), (-6, 0, 0), mat=PAINT, tint=tint, solid=False)
        b.box((2.3, 0.08, 0.16), (0, c0 + 0.04, 2.0), mat=PAINT, tint=tint, solid=False)
        for x in (-0.56, 0.56):
            if shot and x > 0:
                b.box((1.02, 0.02, 0.8), (x, c0 + 0.03, 2.48), (-6, 0, 0), mat=MATTE, tint=srgb(0.03, 0.03, 0.03),
                      solid=False)
                for k in range(5):
                    b.box((0.25, 0.01, 0.1), (x + b.rng.uniform(-0.4, 0.4), c0 + 0.02, 2.2 + b.rng.uniform(0, 0.5)),
                          (-6, b.rng.uniform(-40, 40), 0), mat=GLASS, solid=False, vary=0)
            else:
                b.box((1.02, 0.02, 0.8), (x, c0 + 0.03, 2.48), (-6, 0, 0), mat=GLASS, solid=False)
        for s in (-1, 1):
            wx = s * (hw - 0.04)
            if s > 0 and cab_open:
                with b.frame((wx, c0 + 0.1, 1.25), (0, 0, -65)):
                    grid_box(b, (0.06, 1.2, 1.1), (0.03, 0.6, 0.55), mat=PAINT, tint=tint, step=0.8)
                    b.box((0.03, 0.8, 0.5), (0.05, 0.55, 1.4), mat=GLASS, solid=False)
                    b.box((0.06, 0.08, 0.5), (0.03, 1.15, 1.4), mat=PAINT, tint=tint, solid=False)
                b.box((0.08, 0.3, 1.7), (wx, c1 - 0.15, 2.05), mat=PAINT, tint=tint)
            else:
                grid_box(b, (0.08, c1 - c0, 1.1), (wx, (c0 + c1) / 2, 1.8), mat=PAINT, tint=tint, step=0.8)
                b.box((0.03, 0.9, 0.55), (wx + s * 0.02, (c0 + c1) / 2 - 0.1, 2.6), mat=GLASS, solid=False)
                for y in (c0 + 0.05, c1 - 0.05):
                    b.box((0.08, 0.1, 0.65), (wx, y, 2.6), mat=PAINT, tint=tint, solid=False)
            b.box((0.4, 0.4, 0.05), (s * 1.0, (c0 + c1) / 2, 0.8), mat=GRATE, tint=GREY, solid=False, vary=0)
            stencil(b, code, (s * (hw + 0.005), -1.6 if s < 0 else -0.95, 1.55), (0, 0, 90 * s), 0.14,
                    srgb(0.85, 0.82, 0.7))
        b.cyl(0.42, 0.06, (0.4, -1.3, 3.06), mat=PAINT, tint=DARK, sides=16, cap=False, solid=False)
        b.cyl(0.04, 0.2, (0.4, -0.88, 3.0), mat=PAINT, tint=DARK, sides=6, solid=False)
        # Cargo bed with drop sides, a spare wheel and can rack behind the cab.
        y0, y1 = -0.35, 3.45
        b.box((2.4, y1 - y0, 0.1), (0, (y0 + y1) / 2, deck), mat=PAINT, tint=srgb(0.4, 0.35, 0.27))
        for s in (-1, 1):
            grid_box(b, (0.05, y1 - y0, 0.55), (s * 1.2, (y0 + y1) / 2, deck + 0.3), mat=PAINT, tint=tint, step=0.9)
        grid_box(b, (2.4, 0.05, 0.55), (0, y1, deck + 0.3), mat=PAINT, tint=tint, step=0.9)
        grid_box(b, (2.4, 0.05, 0.9), (0, y0, deck + 0.5), mat=PAINT, tint=tint, step=0.9)
        b.piece("wheel-road", (0.0, y0 - 0.17, 1.55), (90, 0, 90))
        # Canvas tilt: hoops, and the canvas over them, its rear part torn away.
        hoops = (y0 + 0.05, 0.9, 2.2, y1 - 0.05)
        top = deck + 0.6
        r = 1.18
        for y in hoops:
            pts = [(r * math.cos(math.pi * k / 10), y, top + 1.0 * math.sin(math.pi * k / 10) + 0.0)
                   for k in range(11)]
            pts = [(-r, y, deck + 0.55)] + pts[::-1] + [(r, y, deck + 0.55)]
            pts = [(x, y_, z) for x, y_, z in pts]
            b.tube(pts, 0.025, mat=PAINT, tint=DARK, sides=5, cap=False)
        cover = y1 - (y1 - y0) * torn
        outline = []
        for k in range(13):
            a = math.pi * k / 12
            outline.append(((r + 0.03) * math.cos(a), top + 1.03 * math.sin(a)))
        outline = [(r + 0.03, deck + 0.55)] + outline + [(-r - 0.03, deck + 0.55)]
        inner = [(x * 0.97, deck + 0.55 + (z - deck - 0.55) * 0.97) for x, z in outline[::-1]]
        with b.frame((0, (y0 + cover) / 2, 0), (0, 0, 0)):
            b.extrude(outline + inner, cover - y0, (0, 0, 0), (0, 0, 0), mat=MATTE, tint=canvas, solid="hull")
        if torn > 0:
            for k in range(4):
                a = math.pi * (0.15 + 0.2 * k)
                b.box((0.5, 0.02, 0.6 + 0.3 * b.rng.random()),
                      ((r + 0.02) * math.cos(a), cover + 0.15, top + 1.0 * math.sin(a) - 0.25),
                      (b.rng.uniform(-15, 15), 0, 0), mat=MATTE, tint=canvas, solid=False)
        if shot:
            bullet_holes(b, (hw + 0.012, -1.3, 1.9), (0, 0, 90), 1.4, 1.2, count=14)
            bullet_holes(b, (0.0, -3.66, 1.7), (0, 0, 0), 1.0, 0.6, count=6)
            bullet_holes(b, (hw + 0.015, 1.5, deck + 0.9), (0, 0, 90), 3.0, 1.0, count=12)
        bed = b.world((0.0, 1.6, deck + 0.06))
    return bed


# ------------------------------------------------------------------------------------------------ pipework

PIPE_OCHRE = srgb(0.80, 0.62, 0.22)
VALVE_GREEN = srgb(0.24, 0.36, 0.30)


def bend(a, corner, c, radius, steps=6):
    """Points of a pipe run from `a` through a rounded corner at `corner` to `c` (the bend's radius in metres)."""
    a, corner, c = Vector(a), Vector(corner), Vector(c)
    d0 = (corner - a).normalized()
    d1 = (c - corner).normalized()
    ang = d0.angle(d1, 0.0)
    if ang < 1e-3:
        return [a.to_tuple(), c.to_tuple()]
    cut = radius * math.tan(ang / 2)
    p0 = corner - d0 * cut
    p1 = corner + d1 * cut
    pts = [a.to_tuple()]
    for k in range(steps + 1):
        t = k / steps
        q = (1 - t) ** 2 * p0 + 2 * (1 - t) * t * corner + t * t * p1     # a quadratic Bezier through the corner
        pts.append(q.to_tuple())
    pts.append(c.to_tuple())
    return pts


def flange(b, at, axis_rot, radius, tint=PIPE_OCHRE, bolts=8):
    """A bolted pipe flange at `at`, its axis along the frame `axis_rot`'s Z, for a pipe of `radius`."""
    with b.frame(at, axis_rot):
        b.cyl(radius * 1.32, 0.09, (0, 0, -0.05), mat=PAINT, tint=tint, sides=20, solid=False, vary=0)
        b.cyl(radius * 1.32, 0.09, (0, 0, 0.05), mat=PAINT, tint=tint, sides=20, solid=False, vary=0)
        for k in range(bolts):
            a = 2 * math.pi * (k + 0.5) / bolts
            b.cyl(0.022, 0.26, (math.cos(a) * radius * 1.17, math.sin(a) * radius * 1.17, 0), mat=PAINT, tint=DARK,
                  sides=6, solid=False, vary=0)


def handwheel(b, at, rot=(0, 0, 0), radius=0.35, tint=RED, spokes=4, rim=0.025, knob=True):
    """A valve handwheel facing -Y of its frame (its axis along Y): a round rim, spokes, a hub and a spinner knob."""
    with b.frame(at, rot):
        pts = [(radius * math.cos(2 * math.pi * k / 16), 0.0, radius * math.sin(2 * math.pi * k / 16))
               for k in range(17)]
        b.tube(pts, rim, mat=PAINT, tint=tint, sides=6, cap=False)
        b.cyl(radius * 0.16, 0.09, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=tint, sides=10, solid=False)
        for k in range(spokes):
            a = 2 * math.pi * k / spokes + 0.3
            b.beam((radius * 0.15 * math.cos(a), 0, radius * 0.15 * math.sin(a)),
                   (radius * math.cos(a), 0, radius * math.sin(a)), rim * 1.2, rim * 1.6, mat=PAINT, tint=tint)
        if knob:
            b.cyl(rim * 0.9, 0.12, (radius * 0.98, -0.07, 0.0), (90, 0, 0), mat=PAINT, tint=DARK, sides=6,
                  solid=False)


def gate_valve(b, at, yaw=0.0, radius=0.5, tint=VALVE_GREEN, wheel=RED, actuator=True, flange_tint=PIPE_OCHRE):
    """A line valve on a pipe of `radius` running along the frame's X: a cast body with flanges, a bonnet, a yoke and
    a gear actuator with a handwheel on its side (or a plain rising stem and wheel when `actuator` is False)."""
    r = radius
    with b.frame(at, (0, 0, yaw)):
        b.box((r * 1.9, r * 2.0, r * 2.2), (0, 0, 0), mat=PAINT, tint=tint, bevel=0.06)
        b.cyl(r * 0.95, r * 2.05, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=tint, sides=18, solid=False)
        for s in (-1, 1):
            flange(b, (s * r * 1.05, 0, 0), (0, 90, 0), r, tint=flange_tint)
        b.cyl(r * 0.75, r * 1.6, (0, 0, r * 1.1 + r * 0.8), mat=PAINT, tint=tint, sides=16)
        b.cyl(r * 0.95, 0.1, (0, 0, r * 1.9 + 0.05), mat=PAINT, tint=tint, sides=16, solid=False)
        top = r * 1.9 + 0.1
        if actuator:
            for s in (-1, 1):
                b.box((0.08, r * 0.6, 0.9), (s * r * 0.45, 0, top + 0.45), mat=PAINT, tint=tint, solid=False)
            b.cyl(0.05, 0.9, (0, 0, top + 0.45), mat=PAINT, tint=srgb(0.75, 0.75, 0.74), sides=8, solid=False)
            b.box((0.62, 0.5, 0.5), (0, 0, top + 1.15), mat=PAINT, tint=tint, bevel=0.04)
            b.cyl(0.2, 0.32, (0, -0.37, top + 1.15), (90, 0, 0), mat=PAINT, tint=tint, sides=12, solid=False)
            handwheel(b, (0, -0.62, top + 1.15), (0, 0, 0), 0.42, wheel)
            b.box((0.3, 0.02, 0.15), (0, 0.26, top + 1.25), mat=PAINT, tint=YELLOW, solid=False, vary=0)
            return top + 1.4
        b.cyl(0.035, 0.6, (0, 0, top + 0.3), mat=PAINT, tint=srgb(0.75, 0.75, 0.74), sides=8, solid=False)
        handwheel(b, (0, 0, top + 0.55), (90, 0, 0), min(0.45, r * 1.4), wheel)
        return top + 0.6


def pipe_support(b, at, radius, yaw=0.0, height=None, tint=GREY):
    """A pipe support under a pipe of `radius` whose centre is at `at` (axis along the frame's X turned `yaw`): a
    concrete sleeper, a steel saddle and a strap over the top."""
    x, y, z = at
    h = height if height is not None else z - radius
    with b.frame((x, y, 0), (0, 0, yaw)):
        b.box((0.6, radius * 2.6 + 0.3, 0.3), (0, 0, 0.1), mat=CONCRETE, tint=srgb(0.74, 0.73, 0.70), bevel=0.02)
        if h > 0.3:
            b.box((0.25, radius * 1.4, h - 0.25), (0, 0, 0.25 + (h - 0.25) / 2), mat=PAINT, tint=tint)
        b.box((0.3, radius * 2.2, 0.08), (0, 0, h - 0.04), mat=PAINT, tint=tint, solid=False)
        arc =[(0.0, (radius + 0.02) * math.cos(math.pi * k / 8), z + (radius + 0.02) * math.sin(math.pi * k / 8))
               for k in range(9)]
        b.tube(arc, 0.012, mat=PAINT, tint=DARK, sides=4, cap=False)
        for s in (-1, 1):
            b.cyl(0.018, 0.1, (0, s * (radius + 0.02), h + 0.04), mat=PAINT, tint=DARK, sides=6, solid=False)


# ------------------------------------------------------------------------------------------------ ships

HULL_WHITE = srgb(0.84, 0.84, 0.81)
BURNT = srgb(0.16, 0.13, 0.11)


def nacelle(b, at, rot=(0, 0, 0), radius=0.48, length=2.7, tint=HULL_WHITE, band=TEAL, burnt=False):
    """A small ship's engine pod along +Y of its frame: intake lip at -Y, the can with service panels and a band,
    a bell nozzle at +Y."""
    t = BURNT if burnt else tint
    with b.frame(at, rot):
        b.cyl(radius, length, (0, 0, 0), (90, 0, 0), mat="M_Mon_Hull", tint=t, sides=18, wear=0.6 if burnt else None)
        b.cyl(radius * 1.04, 0.16, (0, -length / 2 + 0.08, 0), (90, 0, 0), mat=PAINT, tint=GREY, sides=18,
              cap=False, solid=False)
        b.cyl(radius * 0.72, 0.1, (0, -length / 2 - 0.02, 0), (90, 0, 0), mat=MATTE, tint=srgb(0.06, 0.06, 0.06),
              sides=16, solid=False)
        b.cyl(radius * 1.01, 0.35, (0, length * 0.12, 0), (90, 0, 0), mat=PAINT, tint=band if not burnt else
              srgb(0.12, 0.2, 0.2), sides=18, solid=False, vary=0)
        a = 2 * math.pi / 4 + 0.4         # a service panel's outline on the upper flank
        with b.frame((math.cos(a) * radius, -0.4, math.sin(a) * radius), (0, math.degrees(-a) + 90.0, 0)):
            b.box((0.5, 0.62, 0.012), (0, 0, 0.004), mat=PAINT, tint=DARK, solid=False, vary=0)
        b.lathe([(radius * 0.7, 0.0), (radius * 0.78, 0.25), (radius * 0.98, 0.7), (radius * 1.08, 0.95)],
                (0, length / 2, 0), (-90, 0, 0), mat=RUST, tint=(1, 1, 1), sides=16, close=False, solid=False)
        b.cyl(radius * 0.4, 0.3, (0, length / 2 + 0.05, 0), (90, 0, 0), mat=PAINT, tint=DARK, sides=12,
              solid=False)


def skiff(b, at, rot=(0, 0, 0), tint=HULL_WHITE, band=TEAL, stripe=ORANGE, code="SK-4 410", torn=(), canopy_broken=True,
          hatch=None, panels_missing=0.25, burnt=0.4, skids=(True, True), dorsal=True):
    """A single-seat utility skiff of the Tin Finch's class, about 6 m long: a boxy cab with a raked two-pane canopy,
    hull plates over a frame of ribs (`panels_missing` leaves plates out and shows the ribs), two engine nacelles on
    pylons at the rear quarters (`torn`: "left"/"right" pylons left as stubs), skids, a dorsal tank, livery, a gull-wing
    hatch on the left (`hatch` degrees open, or None for shut). Nose toward -Y, the skids' underside at z = 0. Returns
    (seat position, the torn pylons' stub ends) in the monument's frame."""
    W = 2.5
    hw = W / 2
    y_nose, y_tail = -2.6, 2.0
    z_floor, z_roof = 0.75, 2.75
    stubs = {}
    with b.frame(at, rot):
        # Frame: ribs round the cab every 0.9 m, longerons.
        prof_y = [y_nose + 0.4, -1.5, -0.6, 0.3, 1.2, y_tail - 0.1]
        for y in prof_y:
            ztop = z_roof if y > -1.2 else z_roof - (-1.2 - y) * 0.95
            pts = [(-hw + 0.08, y, z_floor), (-hw + 0.08, y, ztop - 0.15), (-hw + 0.3, y, ztop - 0.05),
                   (hw - 0.3, y, ztop - 0.05), (hw - 0.08, y, ztop - 0.15), (hw - 0.08, y, z_floor)]
            b.tube(pts, 0.05, mat=PAINT, tint=DARK, sides=5, cap=False)
        for x in (-hw + 0.1, hw - 0.1):
            b.beam((x, y_nose + 0.3, z_floor + 0.1), (x, y_tail - 0.1, z_floor + 0.1), 0.1, 0.12, mat=PAINT,
                   tint=DARK)
        # Floor and belly, the nose block, roof, back wall.
        b.box((W, y_tail - y_nose - 0.3, 0.12), (0, (y_nose + y_tail) / 2 + 0.15, z_floor), mat="M_Mon_Hull",
              tint=srgb(0.5, 0.5, 0.48))
        nose_prof = [(y_nose, z_floor - 0.1), (y_nose + 0.9, z_floor - 0.1), (y_nose + 0.9, z_floor + 0.95),
                     (y_nose + 0.05, z_floor + 0.75), (y_nose - 0.12, z_floor + 0.3)]
        with b.frame((0, 0, 0), (0, 0, 90)):
            b.extrude(nose_prof, W, (0, 0, 0), (0, 0, 0), mat="M_Mon_Hull", tint=tint, bevel=0.03)
        grid_box(b, (W - 0.1, y_tail + 1.1, 0.08), (0, (y_tail - 1.1) / 2, z_roof), mat="M_Mon_Hull", tint=tint,
                 step=0.8)
        grid_box(b, (W - 0.1, 0.08, z_roof - z_floor), (0, y_tail - 0.04, (z_roof + z_floor) / 2), mat="M_Mon_Hull",
                 tint=tint, step=0.8)
        # The canopy: a raked frame with two panes (or broken shards) from the nose block to the roof.
        c0 = Vector((0, y_nose + 0.9 - 0.85, z_floor + 0.95))
        c1 = Vector((0, -1.1, z_roof))
        d = c1 - c0
        ang = math.degrees(math.atan2(d.z, d.y))
        length = d.length
        with b.frame(tuple((c0 + c1) / 2), (ang, 0, 0)):
            for x in (-hw + 0.06, 0.0, hw - 0.06):
                b.box((0.1, length + 0.1, 0.1), (x, 0, 0), mat=PAINT, tint=DARK)
            for s in (-1, 1):
                if canopy_broken and s > 0:
                    for k in range(4):
                        b.box((0.3 + 0.2 * b.rng.random(), 0.25 + 0.3 * b.rng.random(), 0.02),
                              (s * (0.25 + 0.6 * b.rng.random()), (b.rng.random() - 0.5) * length * 0.8, 0.0),
                              (0, 0, b.rng.uniform(-30, 30)), mat=GLASS, solid=False, vary=0)
                else:
                    b.box((hw - 0.12, length - 0.05, 0.02), (s * hw / 2, 0, 0), mat=GLASS, solid=False)
        # Side walls: plates over the ribs, some missing; a window each side; the hatch on the left.
        def plates(side):
            x = side * (hw + 0.01)
            ys = [y_nose + 0.9, -1.5, -0.6, 0.3, 1.2, y_tail]
            for y0, y1 in zip(ys, ys[1:]):
                for z0, z1 in ((z_floor, z_floor + 0.85), (z_floor + 0.85, z_roof)):
                    top1 = z1
                    if y0 < -1.1 and z0 > z_floor + 0.5:
                        continue      # the canopy's side windows go here
                    if side < 0 and hatch is not None and -1.5 <= y0 < 0.3:
                        continue      # the hatch opening
                    if b.rng.random() < panels_missing:
                        continue
                    grid_box(b, (0.05, y1 - y0 - 0.02, top1 - z0 - 0.02), (x, (y0 + y1) / 2, (z0 + top1) / 2),
                             mat="M_Mon_Hull", tint=tint, step=0.7)
        for side in (-1, 1):
            plates(side)
            # Side window under the canopy's edge.
            b.box((0.03, 1.2, 0.8), (side * (hw + 0.005), -1.75, z_floor + 1.35), (side * 0, 0, 0), mat=GLASS,
                  solid=False)
            # Livery: band and stripe on the lower plates, the code, the nose stripe.
            b.box((0.012, y_tail - y_nose - 1.0, 0.28), (side * (hw + 0.04), 0.0, z_floor + 0.5), mat=PAINT,
                  tint=band, solid=False, vary=0, wear=0.5)
            b.box((0.012, y_tail - y_nose - 1.0, 0.06), (side * (hw + 0.04), 0.0, z_floor + 0.3), mat=PAINT,
                  tint=stripe, solid=False, vary=0, wear=0.5)
            stencil(b, code, (side * (hw + 0.05), 1.1 if side < 0 else -0.2, z_floor + 1.2), (0, 0, 90 * side), 0.16,
                    DARK, align="centre")
        if hatch is not None:
            with b.frame((-hw - 0.02, -0.6, z_roof - 0.05), (0, -hatch, 0)):
                grid_box(b, (0.06, 1.8, z_roof - z_floor - 0.15), (0, 0, -(z_roof - z_floor - 0.15) / 2),
                         mat="M_Mon_Hull", tint=tint, step=0.7)
                b.box((0.03, 0.9, 0.5), (-0.04, 0.0, -0.6), mat=GLASS, solid=False)
        # Seat and stick inside.
        b.box((0.6, 0.6, 0.12), (0, -0.4, z_floor + 0.45), mat=MATTE, tint=srgb(0.25, 0.27, 0.28), solid=False)
        b.box((0.6, 0.14, 0.85), (0, -0.05, z_floor + 0.9), (-12, 0, 0), mat=MATTE, tint=srgb(0.25, 0.27, 0.28),
              solid=False)
        b.box((0.7, 0.35, 0.4), (0, y_nose + 1.2, z_floor + 0.9), (20, 0, 0), mat=PAINT, tint=DARK, solid=False)
        seat = b.world((0, -0.4, z_floor + 0.55))
        # Nacelles on pylons at the rear quarters.
        for side, name in ((-1, "left"), (1, "right")):
            root = Vector((side * hw, 1.1, z_floor + 0.9))
            tip = Vector((side * (hw + 0.95), 1.3, z_floor + 0.65))
            if name in torn:
                stub = root + (tip - root) * 0.35
                b.beam(tuple(root), tuple(stub), 0.22, 0.5, mat=PAINT, tint=DARK)
                for k in range(3):
                    b.beam(tuple(stub), tuple(stub + Vector((side * 0.25, b.rng.uniform(-0.3, 0.3),
                                                             b.rng.uniform(-0.3, 0.3)))), 0.04, 0.04, mat=RUST,
                           tint=(1, 1, 1))
                stubs[name] = b.world(tuple(stub))
                continue
            b.beam(tuple(root), tuple(tip), 0.2, 0.5, mat="M_Mon_Hull", tint=tint)
            nacelle(b, tuple(tip + Vector((side * 0.3, 0.0, 0.0))), (0, 0, 0), burnt=side > 0 and burnt > 0.3)
        # Skids on struts.
        for side, keep in zip((-1, 1), skids):
            x = side * 1.0
            if keep:
                b.tube([(x, y_nose + 0.2, 0.25), (x, y_nose + 0.5, 0.1), (x, y_tail - 0.3, 0.1), (x, y_tail, 0.2)],
                       0.09, mat=PAINT, tint=GREY, sides=8)
            else:
                b.tube([(x, -0.6, 0.1), (x, y_tail - 0.3, 0.1), (x, y_tail, 0.2)], 0.09, mat=PAINT, tint=GREY,
                       sides=8)
            for y in (-1.2, 1.0):
                b.beam((x * 0.7, y, z_floor - 0.05), (x, y, 0.15), 0.08, 0.08, mat=PAINT, tint=DARK)
        # Dorsal tank, antenna, lamps.
        if dorsal:
            b.cyl(0.42, 1.3, (0, 1.0, z_roof + 0.48), (90, 0, 0), mat=PAINT, tint=srgb(0.7, 0.7, 0.68), sides=16)
            for s in (-1, 1):
                ball(b, (0, 1.0 + s * 0.65, z_roof + 0.48), 0.42, srgb(0.7, 0.7, 0.68), mat=PAINT, sides=16, rings=6)
            for y in (0.6, 1.4):
                b.box((0.9, 0.12, 0.3), (0, y, z_roof + 0.15), mat=PAINT, tint=DARK, solid=False)
        b.cyl(0.015, 1.2, (0.6, 1.6, z_roof + 0.6), mat=PAINT, tint=BLACK, sides=5, solid=False)
        for s in (-1, 1):
            b.box((0.12, 0.06, 0.08), (s * (hw - 0.2), y_nose - 0.08, z_floor + 0.55), mat=GLOSS,
                  tint=srgb(0.9, 0.9, 0.85), solid=False, vary=0)
        # Burns: soot over the nose and the right side.
        if burnt:
            for k in range(int(6 * burnt) + 1):
                side = 1 if b.rng.random() < 0.7 else -1
                b.box((0.02, 0.5 + 0.8 * b.rng.random(), 0.3 + 0.5 * b.rng.random()),
                      (side * (hw + 0.045), b.rng.uniform(y_nose + 1.0, y_tail - 0.3),
                       b.rng.uniform(z_floor + 0.3, z_roof - 0.3)), mat=MATTE, tint=SCORCH, solid=False, vary=0.2)
    return seat, stubs


# ------------------------------------------------------------------------------------------------ colonist

LEAF = srgb(0.30, 0.42, 0.16)
LEAF_DRY = srgb(0.52, 0.48, 0.24)
SOIL = srgb(0.36, 0.27, 0.19)


def hab_pod(b, at, rot=(0, 0, 0), length=7.0, radius=1.8, legs=0.5, tint=OFFWHITE, band=ORANGE, stripe=TEAL,
            door_open=70.0, windows=3, code=None, interior="bunk", steps=True, porthole=True):
    """A colonist's prefab hab pod you can walk into: a horizontal can of composite panels on saddles, ribs and a
    livery band, a flat floor inside, an end wall with a 2.3 m door (at +X) swung open over a few steps, a porthole
    in the other end, windows down both flanks; furnished (`interior`: "bunk", "kitchen", "store" or None).
    Returns the floor's centre in the monument's frame."""
    r, L = radius, length
    hl = L / 2
    zc = legs + r
    zf = legs + 0.5
    rw = r - 0.06
    with b.frame(at, rot):
        # Saddles and feet.
        for x in (-hl + 1.0, hl - 1.0):
            b.box((0.3, r * 1.5, 0.3), (x, 0, legs + 0.05), mat=PAINT, tint=DARK)
            for y in (-r * 0.6, r * 0.6):
                b.box((0.18, 0.18, legs), (x, y, legs / 2), mat=PAINT, tint=DARK)
                b.box((0.4, 0.4, 0.06), (x, y, 0.03), mat=PAINT, tint=GREY, solid=False)
        # The shell: twelve curved panels (local angle 0 = down, 90 = +Y, 180 = up, 270 = -Y).
        with b.frame((0, 0, zc), (0, 90, 0)):
            for k in range(12):
                arc_panel(b, r, 30.0 * k + 0.4, 30.0 * (k + 1) - 0.4, -hl + 0.02, hl - 0.02, 0.06, 3, mat=PANEL,
                          tint=tint, solid=True)
            # The livery band and its pinstripe down both flanks, below the windows.
            for lo, hi, s_lo, s_hi in ((60.0, 74.0, 56.0, 58.0), (286.0, 300.0, 302.0, 304.0)):
                arc_panel(b, r + 0.012, lo, hi, -hl + 0.3, hl - 0.3, 0.012, 3, mat=PAINT, tint=band)
                arc_panel(b, r + 0.012, s_lo, s_hi, -hl + 0.3, hl - 0.3, 0.012, 1, mat=PAINT, tint=stripe)
        for x in (-hl + 0.12, -hl / 3, hl / 3, hl - 0.12):
            b.cyl(r + 0.035, 0.12, (x, 0, zc), (0, 90, 0), mat=PAINT, tint=GREY, sides=24, cap=False, solid=False)
        # Windows: glass and a frame laid on each flank, tilted to the shell.
        for side, ang in ((-1, -15.0), (1, 15.0)):
            for k in range(windows):
                x = -hl + L * (k + 1) / (windows + 1) - 0.3
                n = Vector((0, side * math.cos(math.radians(15)), math.sin(math.radians(15))))
                c = Vector((x, 0, zc)) + n * (r + 0.015)
                b.box((0.8, 0.02, 0.5), tuple(c), (side * 15.0, 0, 0), mat=GLASS, solid=False)
                b.box((0.9, 0.015, 0.6), tuple(c - n * 0.005), (side * 15.0, 0, 0), mat=PAINT, tint=DARK,
                      solid=False)
        # Floor inside.
        fw = 2 * math.sqrt(max(0.1, rw * rw - (zf - zc) ** 2)) - 0.1
        b.box((L - 0.1, fw, 0.08), (0, 0, zf - 0.04), mat=PAINT, tint=srgb(0.45, 0.42, 0.36))
        # End walls: the door end at +X, the porthole end at -X.
        jamb = 0.5
        zb = zc - math.sqrt(rw * rw - jamb * jamb)
        top = zf + 2.3
        arc = []
        a0 = math.atan2(zb - zc, jamb)
        a1 = math.atan2(zb - zc, -jamb) + 2 * math.pi
        for k in range(19):
            a = a0 + (a1 - a0) * k / 18
            arc.append((rw * math.cos(a), zc + rw * math.sin(a)))
        door_outline = arc + [(-jamb, top), (jamb, top)]
        end_wall(b, (hl - 0.05, 0, 0), 90.0, door_outline, door=(-jamb, jamb, top), tint=tint, thick=0.08)
        with b.frame((hl - 0.05, 0, 0), (0, 0, 0)):
            for y in (-jamb - 0.04, jamb + 0.04):
                b.box((0.14, 0.08, top - zf), (0.04, y, (zf + top) / 2), mat=PAINT, tint=DARK, solid=False)
            b.box((0.14, 2 * jamb + 0.16, 0.08), (0.04, 0, top + 0.04), mat=PAINT, tint=DARK, solid=False)
            b.box((0.4, 2 * jamb + 0.1, zf - zb + 0.02), (0.15, 0, (zf + zb) / 2), mat=PAINT, tint=GREY)
        with b.frame((hl + 0.02, jamb, zf), (0, 0, door_open)):
            grid_box(b, (0.06, 2 * jamb - 0.04, 2.26), (0.03, -jamb + 0.02, 1.13), mat=PANEL, tint=band, step=0.7)
            b.box((0.05, 0.04, 0.16), (0.08, -2 * jamb + 0.12, 1.05), mat=PAINT, tint=GREY, solid=False)
            b.box((0.02, 0.5, 0.35), (0.065, -jamb + 0.02, 1.7), mat=GLASS, solid=False)
        b.marker("door", (hl + 0.8, 0, 0.0), kind_of="pod")
        circle = [(rw * math.cos(2 * math.pi * k / 20), zc + rw * math.sin(2 * math.pi * k / 20)) for k in range(20)]
        with b.frame((-hl + 0.05, 0, 0), (0, 0, 90)):
            b.extrude(circle, 0.08, (0, 0, 0), (0, 0, 0), mat=PANEL, tint=tint, solid="hull")
        if porthole:
            b.cyl(0.32, 0.05, (-hl - 0.01, 0, zc + 0.3), (0, 90, 0), mat=PAINT, tint=DARK, sides=16, solid=False)
            b.cyl(0.26, 0.05, (-hl - 0.025, 0, zc + 0.3), (0, 90, 0), mat=GLASS, sides=16, solid=False)
        if code:
            stencil(b, code, (hl + 0.002, -0.95, zc + 0.75), (0, 0, 90), 0.18, DARK, align="centre")
        if steps:
            from .kit import stairs
            run = 1.1
            stairs(b, (hl + 0.36 + run, 0, 0), (0, 0, 90), 1.0, zf, run, tint=GREY, rails=False)
        # Furniture.
        if interior == "bunk":
            with b.frame((-hl + 1.3, fw / 2 - 0.45, zf), (0, 0, 0)):
                b.box((1.95, 0.8, 0.35), (0, 0, 0.18), mat=PAINT, tint=DARK)
                b.box((1.9, 0.75, 0.14), (0, 0, 0.42), mat=MATTE, tint=srgb(0.45, 0.5, 0.55), solid=False)
                b.box((0.5, 0.35, 0.1), (-0.65, 0.05, 0.54), mat=MATTE, tint=srgb(0.85, 0.83, 0.78), solid=False)
                b.box((1.2, 0.78, 0.05), (0.3, -0.02, 0.51), (0, 3, 6), mat=MATTE, tint=srgb(0.55, 0.3, 0.2),
                      solid=False)
            b.box((0.9, 0.6, 0.04), (0.6, -fw / 2 + 0.4, zf + 0.75), mat=PAINT, tint=srgb(0.55, 0.45, 0.32),
                  solid=False)
            b.box((0.06, 0.06, 0.73), (0.6, -fw / 2 + 0.4, zf + 0.37), mat=PAINT, tint=DARK, solid=False)
            b.box((0.4, 0.4, 0.45), (1.3, -fw / 2 + 0.55, zf + 0.23), mat=PAINT, tint=srgb(0.4, 0.42, 0.4),
                  solid=False)
            b.marker("bed", (-hl + 1.3, fw / 2 - 0.45, zf + 0.5), chance=0.4)
        elif interior == "kitchen":
            with b.frame((0.0, fw / 2 - 0.35, zf), (0, 0, 0)):
                b.box((3.0, 0.6, 0.9), (0, 0, 0.45), mat=PANEL, tint=LIGHTGREY)
                b.box((3.05, 0.65, 0.04), (0, 0, 0.92), mat=PAINT, tint=srgb(0.5, 0.45, 0.38), solid=False)
                b.box((0.5, 0.4, 0.04), (-0.6, -0.02, 0.945), mat=PAINT, tint=GREY, solid=False)
                for k in range(4):
                    b.cyl(0.06 + 0.02 * (k % 2), 0.16, (0.4 + 0.2 * k, 0, 1.02), mat=PAINT,
                          tint=(RED, TEAL, YELLOW, GREY)[k], sides=10, solid=False)
                b.box((3.0, 0.3, 0.04), (0, 0.12, 1.6), mat=PAINT, tint=GREY, solid=False)
            b.box((1.0, 0.7, 0.04), (-1.4, -fw / 2 + 0.5, zf + 0.75), mat=PAINT, tint=srgb(0.55, 0.45, 0.32),
                  solid=False)
            for x in (-1.85, -0.95):
                b.box((0.06, 0.6, 0.73), (x, -fw / 2 + 0.5, zf + 0.37), mat=PAINT, tint=DARK, solid=False)
        elif interior == "store":
            for k in range(3):
                b.box((0.5, fw - 0.6, 0.04), (-hl + 0.6 + k * 0.9, 0, zf + 0.6 + 0.4 * (k % 2)), mat=PAINT,
                      tint=GREY, solid=False)
                b.box((0.45, 0.5, 0.35), (-hl + 0.6 + k * 0.9, 0.3, zf + 0.18), mat=PAINT,
                      tint=(srgb(0.3, 0.35, 0.3), ORANGE, GREY)[k], solid=False)
        centre = b.world((0.6, 0, zf))
    return centre


def tarp(b, corners, sag=0.35, tint=TARP_BLUE, cells=(6, 4), thick=0.02, mat=MATTE):
    """A sheet stretched between four corner points (in order round the sheet), sagging `sag` metres in the middle;
    a closed thin slab (two-sided in the game). Returns its centre."""
    c = [Vector(p) for p in corners]
    nu, nv = cells
    top, bot = [], []
    for j in range(nv + 1):
        v = j / nv
        for i in range(nu + 1):
            u = i / nu
            p = (c[0] * (1 - u) + c[1] * u) * (1 - v) + (c[3] * (1 - u) + c[2] * u) * v
            p = p - Vector((0, 0, sag * 16 * u * (1 - u) * v * (1 - v)))
            top.append(p.to_tuple())
            bot.append((p.x, p.y, p.z - thick))
    n = len(top)
    faces = []
    for j in range(nv):
        for i in range(nu):
            a = j * (nu + 1) + i
            faces.append([a, a + 1, a + nu + 2, a + nu + 1])
            faces.append([n + a, n + a + nu + 1, n + a + nu + 2, n + a + 1])
    for i in range(nu):
        a, d = i, nv * (nu + 1) + i
        faces.append([a, n + a, n + a + 1, a + 1])
        faces.append([d + 1, n + d + 1, n + d, d])
    for j in range(nv):
        a, d = j * (nu + 1), j * (nu + 1) + nu
        faces.append([a + nu + 1, n + a + nu + 1, n + a, a])
        faces.append([d, n + d, n + d + nu + 1, d + nu + 1])
    b.mesh(top + bot, faces, mat=mat, tint=tint, solid=False, wear=0.5, vary=0.02)
    return sum(c, Vector()) / 4


def bush(b, at, size=1.0, tint=LEAF, lumps=5):
    """A rounded leafy bush of a few overlapping low lumps."""
    x, y, z = at
    for k in range(lumps):
        a = b.rng.random() * math.tau
        r = size * 0.35 * b.rng.random()
        s = size * (0.35 + 0.25 * b.rng.random())
        ball(b, (x + math.cos(a) * r, y + math.sin(a) * r, z + s * 0.7), s, tint, mat=MATTE, sides=10, rings=6)


def bean_poles(b, at, height=1.8, tint=LEAF_DRY):
    """A teepee of four poles with a dry vine wound up it."""
    x, y, z = at
    for k in range(4):
        a = math.radians(90 * k + 45)
        b.beam((x + 0.4 * math.cos(a), y + 0.4 * math.sin(a), z), (x, y, z + height), 0.03, 0.03, mat=PAINT,
               tint=WOOD_GREY)
    pts = []
    for i in range(25):
        t = i / 24
        a = t * 6 * math.pi
        rr = 0.4 * (1 - t) + 0.05
        pts.append((x + rr * math.cos(a), y + rr * math.sin(a), z + 0.1 + t * (height - 0.2)))
    b.tube(pts, 0.015, mat=MATTE, tint=tint, sides=4, cap=False)
    for i in range(0, 25, 3):
        p = pts[i]
        b.box((0.14, 0.02, 0.1), p, (0, 0, b.rng.uniform(0, 360)), mat=MATTE, tint=tint, solid=False, vary=0.1)


def small_turbine(b, at, height=6.0, yaw=0.0, tint=OFFWHITE, broken=False):
    """A homestead wind turbine: a guyed pole, a nacelle with a tail fin and a three-bladed rotor 1.6 m long."""
    x, y, z = at
    b.box((0.6, 0.6, 0.3), (x, y, z + 0.1), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
    b.cyl(0.07, height, (x, y, z + height / 2), mat=PAINT, tint=GREY, sides=10)
    for k in range(3):
        a = math.radians(120 * k)
        from .kit import cable
        cable(b, (x + 3.0 * math.cos(a), y + 3.0 * math.sin(a), z + 0.05), (x, y, z + height * 0.7), sag=0.003,
              radius=0.006, tint=srgb(0.5, 0.5, 0.5), segments=4)
    with b.frame((x, y, z + height + 0.15), (0, 0, yaw)):
        b.cyl(0.14, 0.7, (0, 0, 0), (90, 0, 0), mat=PAINT, tint=tint, sides=12)
        b.lathe([(0.14, 0.0), (0.1, 0.12), (0.0, 0.2)], (0, -0.35, 0), (90, 0, 0), mat=PAINT, tint=tint, sides=12)
        b.box((0.04, 0.9, 0.04), (0, 0.8, 0), mat=PAINT, tint=GREY, solid=False)
        b.box((0.02, 0.5, 0.45), (0, 1.25, 0.1), mat=PAINT, tint=tint, solid=False)
        for k in range(3):
            if broken and k == 2:
                continue
            ang = 120 * k + 15
            with b.frame((0, -0.42, 0), (0, ang, 0)):
                b.extrude([(-0.06, 0.08), (0.1, 0.08), (0.05, 1.6), (-0.02, 1.6)], 0.03, (0, 0, 0), (0, 0, 0),
                          mat=PAINT, tint=tint, solid=False)


# ------------------------------------------------------------------------------------------------ depots

RECYCLER_OLIVE = srgb(0.36, 0.40, 0.33)


def _fuel_pump(b):
    """A Seedward fuel dispenser: a plinth, an off-white cabinet with rounded corners and an orange band, a head with a
    dark display and price window, a nozzle in its holster on each side with its hose looping down."""
    b.box((1.1, 0.62, 0.16), (0, 0, 0.08), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), bevel=0.02)
    b.box((0.9, 0.5, 1.25), (0, 0, 0.16 + 0.625), mat=PANEL, tint=OFFWHITE, bevel=0.06)
    b.box((0.92, 0.52, 0.16), (0, 0, 0.62), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.box((0.92, 0.52, 0.04), (0, 0, 0.52), mat=PAINT, tint=TEAL, solid=False, vary=0)
    b.box((0.96, 0.44, 0.5), (0, 0, 1.41 + 0.25), mat=PANEL, tint=OFFWHITE, bevel=0.05)
    for s in (-1, 1):
        b.box((0.62, 0.02, 0.22), (0, s * 0.225, 1.72), mat=GLOSS, tint=srgb(0.04, 0.05, 0.05), solid=False, vary=0)
        b.box((0.3, 0.025, 0.08), (0.1, s * 0.226, 1.53), mat=LAMP, tint=srgb(0.9, 0.5, 0.15), solid=False, vary=0)
        b.box((0.24, 0.02, 0.16), (0.0, s * 0.255, 1.05), mat=PAINT, tint=DARK, solid=False, vary=0)
        # Nozzle in its holster, the hose looping down to the base and up into the cabinet.
        b.box((0.14, 0.12, 0.22), (-0.3, s * 0.3, 1.02), mat=PAINT, tint=DARK, solid=False)
        b.box((0.05, 0.22, 0.05), (-0.3, s * 0.38, 1.06), (s * 20, 0, 0), mat=PAINT, tint=srgb(0.25, 0.45, 0.25),
              solid=False)
        b.tube([(-0.3, s * 0.32, 0.92), (-0.32, s * 0.42, 0.55), (-0.25, s * 0.45, 0.3), (-0.05, s * 0.4, 0.28),
                (0.15, s * 0.34, 0.45), (0.3, s * 0.27, 1.2)], 0.02, mat=PAINT, tint=BLACK, sides=5, cap=False)
    b.collide_box(Vector((0, 0, 0.95)), (1.0, 0.55, 1.9), (0, 0, 0))


def _recycler(b):
    """A reclaimer press (the monuments' recycler, MONUMENTS.md §7): a squat olive machine on a skid, a striped
    intake funnel on top, a rumbling drum behind louvres, an output chute and tray at the side, a control box with a
    big lever and two lamps, cable to a junction box. Its marker sits at the controls."""
    b.box((2.4, 1.6, 0.18), (0, 0, 0.09), mat=PAINT, tint=DARK)
    for x in (-1.0, 1.0):
        b.box((0.16, 1.6, 0.12), (x, 0, 0.24), mat=PAINT, tint=DARK, solid=False)
    b.box((1.9, 1.3, 1.3), (0, 0, 0.18 + 0.65), mat=PAINT, tint=RECYCLER_OLIVE, bevel=0.05)
    b.box((1.92, 1.32, 0.12), (0, 0, 1.2), mat=PAINT, tint=DARK, solid=False)
    # Intake funnel: a frustum with a hazard-striped lip.
    b.cyl(0.62, 0.55, (-0.25, 0.05, 1.75), (0, 0, 45), mat=PAINT, tint=RECYCLER_OLIVE, sides=4, radius2=0.95,
          cap=False, solid="hull")
    b.cyl(0.66, 0.08, (-0.25, 0.05, 1.5), (0, 0, 45), mat=PAINT, tint=DARK, sides=4, solid=False)
    with b.frame((-0.25, 0.05, 2.05), (0, 0, 0)):
        for k in range(4):
            a = math.radians(90 * k)
            with b.frame((0.67 * math.cos(a), 0.67 * math.sin(a), 0), (0, 0, 90 * k + 90)):
                stripes(b, (-0.62, 0.0, 0), (0.62, 0.0, 0), 0.08, width=0.16, at_z=-0.06, normal=(0, -1, 0))
    b.box((1.0, 0.04, 0.6), (-0.25, -0.66, 0.85), mat=PAINT, tint=DARK, solid=False)
    for k in range(6):
        b.box((0.9, 0.05, 0.03), (-0.25, -0.69, 0.62 + k * 0.09), (30, 0, 0), mat=PAINT, tint=GREY, solid=False,
              vary=0)
    # Output chute and tray on the +X side.
    b.box((0.5, 0.6, 0.1), (1.12, 0.1, 0.72), (0, -25, 0), mat=PAINT, tint=GREY)
    b.box((0.7, 0.8, 0.3), (1.4, 0.1, 0.33), mat=PAINT, tint=GREY, solid=False)
    b.box((0.66, 0.76, 0.05), (1.4, 0.1, 0.46), mat=PAINT, tint=DARK, solid=False)
    # Controls: a box with a lever and lamps on the front (-Y) right.
    b.box((0.5, 0.25, 0.5), (0.6, -0.78, 1.0), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
    b.box((0.06, 0.06, 0.42), (0.75, -0.95, 1.12), (0, -35, 0), mat=PAINT, tint=RED, solid=False)
    b.cyl(0.05, 0.08, (0.87, -0.95, 1.29), mat=PAINT, tint=BLACK, sides=8, solid=False)
    for x, col in ((0.45, srgb(0.3, 1.0, 0.4)), (0.6, srgb(1.0, 0.25, 0.1))):
        b.box((0.07, 0.03, 0.07), (x, -0.91, 1.15), mat=LAMP, tint=col, solid=False, vary=0)
    stencil(b, "RECLAIM", (-0.25, -0.67, 1.27), (0, 0, 0), 0.08, YELLOW, align="centre")
    b.tube([(-0.95, 0.4, 0.4), (-1.3, 0.5, 0.15), (-1.7, 0.6, 0.05), (-2.1, 0.7, 0.05)], 0.035, mat=PAINT,
           tint=BLACK, sides=5)
    b.box((0.3, 0.2, 0.4), (-2.2, 0.75, 0.22), mat=PAINT, tint=GREY, solid=False)
    b.collide_box(Vector((0, 0, 0.9)), (2.0, 1.4, 1.8), (0, 0, 0))
    b.marker("recycler", (0.6, -1.3, 0.0))


def _dumpster(b):
    """A steel waste skip with two lids (one thrown back), side pockets and casters."""
    b.box((1.8, 1.1, 1.1), (0, 0, 0.2 + 0.55), mat=PAINT, tint=srgb(0.25, 0.4, 0.3), bevel=0.03)
    b.box((1.84, 1.14, 0.08), (0, 0, 1.28), mat=PAINT, tint=srgb(0.25, 0.4, 0.3), solid=False)
    for x in (-0.8, 0.8):
        for y in (-0.45, 0.45):
            b.cyl(0.08, 0.06, (x, y, 0.1), (90, 0, 0), mat=PAINT, tint=BLACK, sides=8, solid=False)
            b.box((0.12, 0.12, 0.1), (x, y, 0.2), mat=PAINT, tint=DARK, solid=False)
    b.box((0.88, 1.12, 0.05), (-0.45, 0, 1.33), mat=PAINT, tint=srgb(0.2, 0.2, 0.2), solid=False)
    b.box((0.88, 0.05, 0.9), (0.45, 0.6, 1.72), (-12, 0, 0), mat=PAINT, tint=srgb(0.2, 0.2, 0.2), solid=False)
    for x in (-0.95, 0.95):
        b.box((0.1, 0.5, 0.12), (x, 0, 0.95), mat=PAINT, tint=DARK, solid=False)
    b.collide_box(Vector((0, 0, 0.7)), (1.8, 1.1, 1.3), (0, 0, 0))


kit.PIECES["fuel-pump"] = _fuel_pump
kit.PIECES["recycler"] = _recycler
kit.PIECES["dumpster"] = _dumpster


def pod_car(b, at, rot=(0, 0, 0), tint=srgb(0.62, 0.70, 0.74), wrecked=0.5, doors=(0.0, 0.0), wheels=True,
            flat=False):
    """A colonist's small electric car, 3.6 m long: a rounded body (a side profile extruded across), a glasshouse,
    bumpers, lamps, four small wheels. `wrecked` (0..1) breaks glass, dents and rusts it; `doors` opens the front
    doors (left, right) by that many degrees; without `wheels` it sits on its hubs. Front toward -Y."""
    w = 1.7
    hw = w / 2
    z0 = 0.18 if wheels else 0.02
    with b.frame(at, rot):
        prof = [(-1.8, z0 + 0.2), (1.8, z0 + 0.2), (1.82, z0 + 0.62), (1.6, z0 + 0.9), (-1.1, z0 + 0.9),
                (-1.72, z0 + 0.72), (-1.84, z0 + 0.42)]
        with b.frame((0, 0, 0), (0, 0, 90)):
            b.extrude(prof, w, (0, 0, 0), (0, 0, 0), mat=PAINT, tint=tint, bevel=0.06)
        # Glasshouse: pillars and a roof, the glass (or the empty frames).
        roof = z0 + 1.48
        b.box((w - 0.16, 1.9, 0.08), (0, 0.45, roof), mat=PAINT, tint=tint, bevel=0.02)
        for y0, y1 in ((-1.0, -0.5), (1.38, 1.4)):
            for s in (-1, 1):
                b.beam((s * (hw - 0.1), y0, z0 + 0.9), (s * (hw - 0.1), y1, roof - 0.03), 0.07, 0.07, mat=PAINT,
                       tint=tint)
        glass = GLASS
        for (cx, cy, cz, sx, sy, sz, rx) in ((0, -0.75, z0 + 1.19, w - 0.3, 0.02, 0.74, -41.0),
                                             (0, 1.4, z0 + 1.19, w - 0.3, 0.02, 0.55, 0.0)):
            if b.rng.random() < wrecked:
                continue
            b.box((sx, sy, sz), (cx, cy, cz), (rx, 0, 0), mat=glass, solid=False)
        for s in (-1, 1):
            if b.rng.random() > wrecked * 0.8:
                b.box((0.02, 1.7, 0.5), (s * (hw - 0.1), 0.45, z0 + 1.17), mat=glass, solid=False)
        # Doors: seams, handles; opened doors swing on their front hinges.
        for i, s in enumerate((-1, 1)):
            ang = doors[i]
            if ang:
                with b.frame((s * hw, -0.45, z0 + 0.2), (0, 0, -s * ang)):
                    b.box((0.06, 1.1, 0.7), (0, 0.55, 0.35), mat=PAINT, tint=tint)
                    b.box((0.03, 0.9, 0.48), (s * 0.0, 0.5, 0.95), mat=glass, solid=False)
            else:
                b.box((0.012, 0.02, 0.68), (s * (hw + 0.002), -0.45, z0 + 0.55), mat=PAINT, tint=DARK, solid=False,
                      vary=0)
                b.box((0.012, 0.02, 0.68), (s * (hw + 0.002), 0.65, z0 + 0.55), mat=PAINT, tint=DARK, solid=False,
                      vary=0)
                b.box((0.03, 0.14, 0.03), (s * (hw + 0.01), 0.45, z0 + 0.75), mat=PAINT, tint=GREY, solid=False)
        # Bumpers, lamps, a livery stripe.
        for y in (-1.86, 1.86):
            b.box((w + 0.04, 0.12, 0.18), (0, y, z0 + 0.32), mat=PAINT, tint=DARK, solid=False)
        for s in (-1, 1):
            b.box((0.3, 0.04, 0.12), (s * 0.55, -1.84, z0 + 0.62), mat=GLOSS, tint=srgb(0.9, 0.9, 0.85), solid=False,
                  vary=0)
            b.box((0.28, 0.04, 0.1), (s * 0.58, 1.83, z0 + 0.66), mat=GLOSS, tint=RED, solid=False, vary=0)
            b.box((0.012, 3.0, 0.08), (s * (hw + 0.004), 0.0, z0 + 0.45), mat=PAINT, tint=OFFWHITE, solid=False,
                  vary=0)
        # Wheels in the arches (or the hubs on blocks).
        for y in (-1.2, 1.2):
            for s in (-1, 1):
                if wheels:
                    with b.frame((s * (hw - 0.12), y, 0.32 if not flat else 0.27), (0, 90, 0)):
                        b.cyl(0.32, 0.2, (0, 0, 0), mat=MATTE, tint=RUBBER, sides=14, solid=False)
                        b.cyl(0.2, 0.22, (0, 0, 0), mat=PAINT, tint=LIGHTGREY, sides=12, solid=False)
                else:
                    b.cyl(0.12, 0.1, (s * (hw - 0.1), y, z0 + 0.25), (0, 90, 0), mat=PAINT, tint=GREY, sides=10,
                          solid=False)
        # Wear: rust patches and dents as dark blotches; the interior seats.
        for k in range(int(wrecked * 6)):
            s = 1 if b.rng.random() < 0.5 else -1
            b.box((0.012, 0.3 + 0.5 * b.rng.random(), 0.2 + 0.3 * b.rng.random()),
                  (s * (hw + 0.006), b.rng.uniform(-1.4, 1.4), z0 + b.rng.uniform(0.35, 0.75)), mat="M_Mon_Rust",
                  tint=(1, 1, 1), solid=False, vary=0)
        for y in (-0.15, 0.75):
            b.box((1.3, 0.5, 0.4), (0, y, z0 + 0.62), mat=MATTE, tint=srgb(0.2, 0.22, 0.24), solid=False)
        b.collide_box(Vector((0, 0, z0 + 0.7)), (w, 3.7, 1.3), (0, 0, 0))


# ------------------------------------------------------------------------------------------------ scrap

SCRAP_TINTS = (srgb(0.55, 0.3, 0.2), srgb(0.45, 0.5, 0.52), srgb(0.62, 0.6, 0.55), srgb(0.3, 0.38, 0.45),
               srgb(0.6, 0.48, 0.2), srgb(0.35, 0.42, 0.3), srgb(0.7, 0.68, 0.64), srgb(0.5, 0.2, 0.15))


def track_frame(b, at, length=3.8, height=0.95, width=0.65, tint=DARK, shoes=True):
    """A crawler track along Y: the frame, the idler and sprocket at the ends, rollers, shoes over the top and ends."""
    x, y, z = at
    r = height / 2
    b.box((width * 0.7, length, height * 0.7), (x, y, z + r), mat=PAINT, tint=tint)
    for s in (-1, 1):
        b.cyl(r, width, (x, y + s * length / 2, z + r), (0, 90, 0), mat=PAINT, tint=tint, sides=14)
        b.cyl(r * 0.45, width + 0.04, (x, y + s * length / 2, z + r), (0, 90, 0), mat=PAINT, tint=GREY, sides=10,
              solid=False)
    for k in range(4):
        b.cyl(0.13, width * 0.8, (x, y - length * 0.35 + length * 0.7 * k / 3, z + 0.16), (0, 90, 0), mat=PAINT,
              tint=GREY, sides=8, solid=False)
    if shoes:
        n = int(length / 0.22)
        for k in range(n):
            b.box((width + 0.03, 0.17, 0.05), (x, y - length / 2 + length * (k + 0.5) / n, z + height + 0.02),
                  mat=PAINT, tint=BLACK, solid=False, vary=0.08)
            b.box((width + 0.03, 0.17, 0.05), (x, y - length / 2 + length * (k + 0.5) / n, z + 0.02), mat=PAINT,
                  tint=BLACK, solid=False, vary=0.08)


def material_handler(b, at, yaw=0.0, slew=0.0, boom=46.0, stick=72.0, tint=YELLOW, l1=9.5, l2=7.0, drop=1.6):
    """A crawler material handler with a lifting magnet: tracks, the slewing upper with its counterweight and engine
    house, a cab raised on a riser with a ladder and rails, the boom (`boom` degrees up) and stick (`stick` degrees
    down) with their rams, the magnet hanging on chains `drop` metres under the stick. Boom toward -Y."""
    with b.frame(at, (0, 0, yaw)):
        for s in (-1, 1):
            track_frame(b, (s * 1.35, 0, 0), 4.2, 1.0, 0.7)
        b.box((1.9, 2.3, 0.7), (0, 0, 0.8), mat=PAINT, tint=DARK)
        b.cyl(1.1, 0.25, (0, 0, 1.27), mat=PAINT, tint=GREY, sides=20)
        with b.frame((0, 0, 1.4), (0, 0, slew)):
            grid_box(b, (3.0, 5.0, 0.5), (0, 0.4, 0.25), mat=PAINT, tint=tint, step=1.2)
            b.box((3.0, 1.1, 1.4), (0, 2.6, 1.2), mat=PAINT, tint=tint, bevel=0.12)
            stripes(b, (-1.45, 3.16, 0), (1.45, 3.16, 0), 0.5, width=0.3, at_z=1.35, normal=(0, 1, 0))
            b.box((1.7, 2.3, 1.35), (0.55, 1.0, 1.17), mat=PAINT, tint=tint, bevel=0.05)
            for k in range(6):
                b.box((0.02, 1.6, 0.08), (1.41, 1.0, 0.75 + k * 0.14), (0, 0, 0), mat=PAINT, tint=DARK, solid=False,
                      vary=0)
            b.cyl(0.08, 1.3, (0.9, 1.6, 2.4), mat=PAINT, tint=DARK, sides=8, solid=False)
            b.cyl(0.1, 0.18, (0.9, 1.6, 3.1), mat=RUST, tint=(1, 1, 1), sides=8, solid=False)
            # Cab on its riser at the front left, a ladder, rails round the deck.
            b.box((0.5, 0.5, 2.5), (-0.95, -0.7, 1.75), mat=PAINT, tint=DARK)
            cab_z = 3.0
            grid_box(b, (1.35, 1.65, 1.75), (-0.95, -0.8, cab_z + 0.88), mat=PAINT, tint=tint, step=0.6)
            b.box((1.45, 1.75, 0.1), (-0.95, -0.8, cab_z + 1.8), mat=PAINT, tint=DARK)
            for (cx, cy, sx, sy) in ((-0.95, -1.64, 1.1, 0.02), (-1.63, -0.8, 0.02, 1.3), (-0.27, -0.8, 0.02, 1.3)):
                b.box((sx, sy, 0.9), (cx, cy, cab_z + 1.15), mat=GLASS, solid=False)
            b.box((1.4, 0.06, 0.06), (-0.95, -1.75, cab_z + 1.6), mat=PAINT, tint=DARK, solid=False)
            for k in range(6):
                b.box((0.06, 0.06, 0.4), (-1.6 + 0.26 * k, -1.75, cab_z + 1.4), mat=PAINT, tint=DARK, solid=False,
                      vary=0)
            rungs(b, (-1.55, 0.2, 0.5), cab_z - 0.4, width=0.4, rot=(0, 0, 90), rail_tint=tint)
            from .kit import railing
            railing(b, (-1.45, 0.6, 0.5), (-1.45, 2.0, 0.5), 1.0, spacing=1.0, tint=tint, toe=False)
            railing(b, (1.45, -1.6, 0.5), (1.45, 0.0, 0.5), 1.0, spacing=1.0, tint=tint, toe=False)
            # Boom and stick, their rams; the magnet hanging plumb on chains.
            foot = (0.35, -1.9, 0.9)
            with b.frame(foot, (-boom, 0, 0)):
                b.box((0.7, l1, 0.85), (0, -l1 / 2, 0), mat=PAINT, tint=tint, bevel=0.03)
                b.box((0.74, l1 * 0.7, 0.06), (0, -l1 * 0.45, 0.44), mat=PAINT, tint=tint, solid=False)
                b.cyl(0.32, 0.9, (0, 0, 0), (0, 90, 0), mat=PAINT, tint=GREY, sides=12, solid=False)
                for s in (-1, 1):
                    b.beam((s * 0.48, -0.4, -1.3), (s * 0.48, -l1 * 0.5, -0.42), 0.2, 0.2, mat=PAINT, tint=GREY)
                    b.beam((s * 0.48, -0.4, -1.3), (s * 0.48, -l1 * 0.25, -0.9), 0.26, 0.26, mat=PAINT, tint=tint)
                b.beam((0, -l1 * 0.35, 0.5), (0, -l1 + 0.3, 0.7), 0.24, 0.24, mat=PAINT, tint=tint)
                b.beam((0, -l1 * 0.55, 0.53), (0, -l1 + 0.3, 0.72), 0.16, 0.16, mat=PAINT, tint=GREY)
                with b.frame((0, -l1, 0), (boom + stick, 0, 0)):
                    b.box((0.55, l2, 0.65), (0, -l2 / 2 + 0.4, 0), mat=PAINT, tint=tint, bevel=0.03)
                    b.cyl(0.3, 0.8, (0, 0, 0), (0, 90, 0), mat=PAINT, tint=GREY, sides=12, solid=False)
                    with b.frame((0, -l2 + 0.3, 0), (-stick, 0, 0)):
                        b.box((0.4, 0.3, 0.3), (0, 0, -0.15), mat=PAINT, tint=DARK, solid=False)
                        for k in range(3):
                            a = math.radians(120 * k)
                            b.tube([(0, 0, -0.25), (0.45 * math.cos(a), 0.45 * math.sin(a), -drop + 0.3)], 0.025,
                                   mat=RUST, tint=(1, 1, 1), sides=4, cap=False)
                        b.cyl(0.8, 0.42, (0, 0, -drop + 0.05), mat=PAINT, tint=srgb(0.22, 0.22, 0.22), sides=20)
                        b.lathe([(0.8, 0.0), (0.6, 0.12), (0.25, 0.2), (0.0, 0.22)], (0, 0, -drop + 0.26), mat=PAINT,
                                tint=srgb(0.22, 0.22, 0.22), sides=20)
                        b.cyl(0.82, 0.08, (0, 0, -drop - 0.12), mat=PAINT, tint=YELLOW, sides=20, solid=False,
                              vary=0)
                        b.tube([(0.2, 0, -drop + 0.45), (0.3, 0.2, -drop + 1.2), (0.1, 0.25, -0.3)], 0.03,
                               mat=PAINT, tint=BLACK, sides=4, cap=False)
    b.marker("landmark", (at[0], at[1], at[2] + 12.0), kind_of="magnet-crane")


def baler(b, at, yaw=0.0, tint=srgb(0.86, 0.55, 0.12), lid=68.0):
    """A scrap baler: a ribbed charging box on a steel bed with its lid hinged open on two rams, the main ram's
    housing and cylinder at one end, the bale door at the other, a power pack with hoses, the operator's stand."""
    with b.frame(at, (0, 0, yaw)):
        grid_box(b, (13.5, 4.4, 0.3), (0.5, 0, 0.15), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), step=2.0)
        for x in (-3.5, -1.0, 1.5, 4.0):
            b.box((0.4, 3.0, 0.5), (x, 0, 0.55), mat=PAINT, tint=DARK)
        z0 = 0.8
        L, W, H = 7.6, 2.7, 1.7
        grid_box(b, (L, W, 0.25), (0, 0, z0 + 0.12), mat=PAINT, tint=tint, step=1.5)
        for s in (-1, 1):
            grid_box(b, (L, 0.2, H), (0, s * (W / 2 - 0.1), z0 + 0.25 + H / 2), mat=PAINT, tint=tint, step=1.0)
            for k in range(9):
                b.box((0.12, 0.2, H), (-L / 2 + 0.4 + k * (L - 0.8) / 8, s * (W / 2 + 0.08), z0 + 0.25 + H / 2),
                      mat=PAINT, tint=tint, solid=False)
            b.box((L, 0.26, 0.16), (0, s * (W / 2 + 0.03), z0 + 0.25 + H + 0.08), mat=PAINT, tint=tint, solid=False)
        stripes(b, (-L / 2, -W / 2 - 0.2, 0), (L / 2, -W / 2 - 0.2, 0), 0.25, width=0.4, at_z=z0 + 0.3)
        # The lid, hinged along the far side, open on two rams.
        with b.frame((0, W / 2, z0 + 0.25 + H + 0.1), (lid, 0, 0)):
            grid_box(b, (L - 0.1, W, 0.3), (0, -W / 2, 0.15), mat=PAINT, tint=tint, step=1.2)
            for k in range(5):
                b.box((0.16, W, 0.25), (-L / 2 + 0.8 + k * (L - 1.6) / 4, -W / 2, 0.42), mat=PAINT, tint=tint,
                      solid=False)
        for x in (-2.4, 2.4):
            b.beam((x, W / 2 + 0.5, z0 + 0.4), (x, W / 2 - 0.4, z0 + H + 1.6), 0.22, 0.22, mat=PAINT, tint=GREY)
            b.beam((x, W / 2 + 0.5, z0 + 0.4), (x, W / 2 + 0.05, z0 + H + 0.6), 0.3, 0.3, mat=PAINT, tint=tint)
        # Main ram housing at -X, its cylinder out the back; the bale door at +X with a chute.
        grid_box(b, (2.2, W + 0.3, H + 1.1), (-L / 2 - 1.1, 0, z0 + (H + 1.1) / 2), mat=PAINT, tint=tint, step=1.0)
        b.cyl(0.42, 3.2, (-L / 2 - 3.6, 0, z0 + 1.0), (0, 90, 0), mat=PAINT, tint=GREY, sides=16)
        b.cyl(0.2, 1.0, (-L / 2 - 5.4, 0, z0 + 1.0), (0, 90, 0), mat=PAINT, tint=srgb(0.8, 0.8, 0.8), sides=12,
              solid=False)
        b.box((0.6, 1.2, 1.2), (-L / 2 - 5.6, 0, z0 + 0.6), mat=PAINT, tint=DARK)
        grid_box(b, (0.3, W, H + 0.2), (L / 2 + 0.15, 0, z0 + 0.25 + (H + 0.2) / 2), mat=PAINT, tint=tint, step=1.0)
        b.box((1.6, W - 0.2, 0.12), (L / 2 + 1.2, 0, z0 - 0.1), (0, 12, 0), mat=PAINT, tint=GREY)
        # Power pack and hoses; the operator's stand with an awning.
        with b.frame((-1.5, -W / 2 - 1.9, 0.3), (0, 0, 0)):
            b.box((2.4, 1.3, 1.4), (0, 0, 0.7), mat=PAINT, tint=srgb(0.32, 0.38, 0.42), bevel=0.04)
            b.cyl(0.35, 0.9, (-0.6, -0.7, 0.7), (0, 90, 0), mat=PAINT, tint=srgb(0.25, 0.4, 0.5), sides=14)
            b.box((0.8, 0.6, 0.6), (0.6, -0.6, 1.7), mat=PAINT, tint=GREY, solid=False)
        for k in range(3):
            b.tube([(-1.0 + 0.4 * k, -W / 2 - 1.2, 1.3), (-1.0 + 0.5 * k, -W / 2 - 0.6, 1.9),
                    (-0.6 + 0.6 * k, -W / 2 - 0.15, z0 + 1.2)], 0.04, mat=PAINT, tint=BLACK, sides=5, cap=False)
        with b.frame((2.5, -W / 2 - 2.0, 0.3), (0, 0, 0)):
            b.box((1.6, 1.4, 0.8), (0, 0, 0.4), mat=PAINT, tint=DARK)
            b.box((0.6, 0.4, 1.0), (0.2, -0.3, 1.3), mat=PAINT, tint=LIGHTGREY, bevel=0.02)
            for x in (-0.75, 0.75):
                b.box((0.06, 0.06, 2.4), (x, 0.65, 2.0), mat=PAINT, tint=GREY, solid=False)
            b.box((1.8, 1.6, 0.05), (0, 0.1, 3.2), (-8, 0, 0), mat="M_Mon_Corrugated", tint=srgb(0.6, 0.62, 0.6))
        return b.world((0, 0, z0 + 0.25))


def mound(b, centre, radius, height, tint=srgb(0.5, 0.36, 0.26), rough=0.25, sectors=20, mat=RUST, squash=1.0):
    """A heap's body: a rounded cone, ragged in radius and height, closed underneath."""
    cx, cy, cz = centre
    prof = [(1.0, 0.0), (0.88, 0.22), (0.68, 0.5), (0.45, 0.78), (0.2, 0.96)]
    ph = [b.rng.random() * 6.28 for _ in range(4)]
    verts = []
    for j, (fr, fz) in enumerate(prof):
        for i in range(sectors):
            a = 2 * math.pi * i / sectors
            n = 1 + rough * (0.6 * math.sin(3 * a + ph[0] + j) + 0.4 * math.sin(7 * a + ph[1] - j * 0.7))
            verts.append((cx + radius * fr * n * math.cos(a), cy + radius * fr * n * squash * math.sin(a),
                          cz + height * fz * (1 + rough * 0.5 * math.sin(5 * a + ph[2] + j) if j else 1.0)))
    top = len(verts)
    verts.append((cx + radius * 0.05, cy, cz + height))
    faces = []
    for j in range(len(prof) - 1):
        for i in range(sectors):
            a, c = j * sectors + i, j * sectors + (i + 1) % sectors
            faces.append([a, c, c + sectors, a + sectors])
    last = (len(prof) - 1) * sectors
    for i in range(sectors):
        faces.append([last + i, last + (i + 1) % sectors, top])
    faces.append(list(range(sectors))[::-1])
    b.mesh(verts, faces, mat=mat, tint=tint, solid="hull", wear=0.9, vary=0.03)


def scrap_heap(b, centre, radius, height, count=30, tints=SCRAP_TINTS):
    """A heap of mixed scrap: a mound with plates, beams, pipes, drums, wheels and panels thrown over its surface."""
    mound(b, centre, radius, height)
    cx, cy, cz = centre
    for k in range(count):
        a = b.rng.random() * math.tau
        f = math.sqrt(b.rng.random()) * 0.95
        z = cz + height * max(0.0, 1.0 - f ** 1.6) * 0.92
        p = (cx + math.cos(a) * radius * f, cy + math.sin(a) * radius * f, z)
        rot = (b.rng.uniform(-35, 35), b.rng.uniform(-35, 35), b.rng.uniform(0, 360))
        tint = tints[b.rng.randrange(len(tints))]
        kind = b.rng.random()
        if kind < 0.35:
            s = 0.8 + 1.6 * b.rng.random()
            b.box((s, s * (0.4 + 0.5 * b.rng.random()), 0.04), p, rot, mat=PAINT if b.rng.random() < 0.6 else RUST,
                  tint=tint, solid=False)
        elif kind < 0.55:
            ln = 1.5 + 3.0 * b.rng.random()
            b.box((0.18, ln, 0.3), p, rot, mat=PAINT if b.rng.random() < 0.5 else RUST, tint=tint, solid=False)
        elif kind < 0.7:
            b.cyl(0.1 + 0.15 * b.rng.random(), 1.2 + 2.5 * b.rng.random(), p, (90, rot[1], rot[2]), mat=PAINT,
                  tint=tint, sides=8, solid=False)
        elif kind < 0.8:
            b.cyl(0.29, 0.88, p, rot, mat=PAINT, tint=tint, sides=12, solid=False)
        elif kind < 0.9:
            b.cyl(0.42, 0.25, p, (90 + rot[0], rot[1], rot[2]), mat=MATTE, tint=RUBBER, sides=12, solid=False)
        else:
            b.box((1.0 + b.rng.random(), 0.8 + 0.6 * b.rng.random(), 0.5 + 0.5 * b.rng.random()), p, rot, mat=PAINT,
                  tint=tint, solid=False)


def flat_car(b, at, yaw=0.0, tint=srgb(0.55, 0.62, 0.66), tilt=(0.0, 0.0), hubs=True):
    """A car crushed flat for stacking, about half a metre thick: the sills and floor, the cabin folded down onto them
    (pushed a little off line), the crushed glasshouse as a dark band with shards, wheel wells, lamps at both ends,
    bumpers, door seams, rust. `hubs` adds the wheel hubs (left out where stacks hide them)."""
    with b.frame(at, (tilt[0], tilt[1], yaw)):
        b.box((1.72, 3.66, 0.26), (0, 0, 0.13), mat=PAINT, tint=tint, bevel=0.03)
        dx = b.rng.uniform(-0.1, 0.1)
        rz = b.rng.uniform(-5, 5)
        b.box((1.62, 2.3, 0.08), (dx * 0.5, 0.25, 0.3), (b.rng.uniform(-2, 2), 0, rz * 0.5), mat=MATTE,
              tint=srgb(0.05, 0.05, 0.05), solid=False, vary=0)
        b.box((1.5, 1.7, 0.1), (dx, 0.4, 0.38), (b.rng.uniform(-3, 3), b.rng.uniform(-2, 2), rz), mat=PAINT, tint=tint,
              solid=False)
        b.box((1.64, 1.0, 0.1), (0, -1.25, 0.31), (b.rng.uniform(-6, 2), 0, 0), mat=PAINT, tint=tint, solid=False)
        for s in (-1, 1):
            for y in (-1.15, 1.2):
                b.box((0.04, 0.62, 0.16), (s * 0.85, y, 0.1), mat=MATTE, tint=srgb(0.03, 0.03, 0.03), solid=False,
                      vary=0)
            b.box((0.012, 0.02, 0.24), (s * 0.865, -0.45, 0.14), mat=PAINT, tint=DARK, solid=False, vary=0)
            b.box((0.012, 0.02, 0.24), (s * 0.865, 0.55, 0.14), mat=PAINT, tint=DARK, solid=False, vary=0)
            b.box((0.3, 0.03, 0.06), (s * 0.55, -1.835, 0.19), mat=GLOSS, tint=srgb(0.85, 0.85, 0.8), solid=False,
                  vary=0)
            b.box((0.28, 0.03, 0.06), (s * 0.55, 1.835, 0.19), mat=GLOSS, tint=RED, solid=False, vary=0)
        for y in (-1.86, 1.86):
            b.box((1.76, 0.1, 0.1), (0, y, 0.09), mat=PAINT, tint=DARK, solid=False)
        for k in range(4):
            b.box((0.1 + 0.08 * b.rng.random(), 0.05, 0.004), (b.rng.uniform(-0.8, 0.8), b.rng.uniform(-0.5, 1.4),
                                                               0.345), (0, 0, b.rng.uniform(0, 180)), mat=GLASS,
                  solid=False, vary=0)
        for y in ((-1.15, 1.2) if hubs else ()):
            for s in (-1, 1):
                b.cyl(0.14, 0.06, (s * 0.84, y, 0.12), (0, 90, 0), mat=PAINT, tint=GREY, sides=8, solid=False)
        b.box((0.8 + 0.4 * b.rng.random(), 0.9, 0.012), (b.rng.uniform(-0.3, 0.3), b.rng.uniform(-1.4, 0.0), 0.265),
              mat=RUST, tint=(1, 1, 1), solid=False, vary=0)


def bale(b, at, yaw=0.0, tilt=0.0):
    """A pressed bale of scrap: a block of crushed metal, painted bits and rust."""
    tint = SCRAP_TINTS[b.rng.randrange(len(SCRAP_TINTS))]
    with b.frame(at, (tilt, 0, yaw)):
        b.box((1.1, 1.6, 1.0), (0, 0, 0.5), mat=RUST, tint=(1, 1, 1), bevel=0.05)
        for k in range(3):
            b.box((1.12, 0.3 + 0.5 * b.rng.random(), 0.2 + 0.3 * b.rng.random()),
                  (0, b.rng.uniform(-0.5, 0.5), b.rng.uniform(0.2, 0.8)), mat=PAINT,
                  tint=SCRAP_TINTS[b.rng.randrange(len(SCRAP_TINTS))], solid=False, vary=0.06)
        b.box((0.4, 1.62, 0.15), (b.rng.uniform(-0.3, 0.3), 0, b.rng.uniform(0.3, 0.7)), mat=PAINT, tint=tint,
              solid=False)


def _car_flat(b):
    """A crushed car as a kit piece: painted white so each instance's tint gives its colour (dark parts stay dark)."""
    flat_car(b, (0, 0, 0), 0.0, tint=(1.0, 1.0, 1.0), hubs=False)
    b.collide_box(Vector((0, 0, 0.22)), (1.75, 3.75, 0.44), (0, 0, 0))


def _car_wreck(b):
    """A stripped car wreck on its hubs as a kit piece, white for the instance tint."""
    pod_car(b, (0, 0, 0), (0, 0, 0), tint=(1.0, 1.0, 1.0), wrecked=1.0, wheels=False)


# Two crushed shapes (each piece draws its own random folds) and a wreck, for yards of stacked cars.
kit.PIECES["car-flat"] = _car_flat
kit.PIECES["car-flat-b"] = _car_flat
kit.PIECES["car-wreck"] = _car_wreck


# ------------------------------------------------------------------------------------------------ farm

BARN_RED = srgb(0.52, 0.2, 0.14)
STRAW = srgb(0.78, 0.66, 0.38)
WRAP_GREEN = srgb(0.36, 0.48, 0.32)
TRACTOR_TEAL = srgb(0.22, 0.46, 0.44)


def end_wall(b, at, yaw, outline, door=None, mat=PANEL, tint=OFFWHITE, thick=0.1):
    """A flat end wall in the plane through `at` turned `yaw` degrees (its face toward -Y at yaw 0): `outline` in
    (across, up) metres; `door` (x0, x1, top) is a notch from the bottom edge. Drawn as one extrusion but collided as
    boxes round the opening, so the door stays passable."""
    with b.frame(at, (0, 0, yaw)):
        b.extrude(outline, thick, (0, 0, 0), (0, 0, 0), mat=mat, tint=tint, solid=False)
        xs = [p[0] for p in outline]
        zs = [p[1] for p in outline]
        x0, x1, z1 = min(xs), max(xs), max(zs)
        if door:
            d0, d1, top = door
            for a, c in ((x0, d0), (d1, x1)):
                if c - a > 0.05:
                    b.collide_box(Vector(((a + c) / 2, 0, z1 / 2)), (c - a, thick, z1), (0, 0, 0))
            b.collide_box(Vector(((d0 + d1) / 2, 0, (top + z1) / 2)), (d1 - d0, thick, z1 - top), (0, 0, 0))
        else:
            b.collide_box(Vector(((x0 + x1) / 2, 0, z1 / 2)), (x1 - x0, thick, z1), (0, 0, 0))


def arch_barn(b, at, rot=(0, 0, 0), radius=6.0, length=24.0, tint=srgb(0.62, 0.66, 0.6), rib=DARK, end_tint=OFFWHITE,
              door_w=5.0, door_h=4.6, slid=2.6):
    """A colonist's prefab arched barn you can walk into: corrugated arch panels on ribs along X over a slab, an end
    wall at +X with a wide opening and its two sliding doors pushed aside, an end wall at -X with a person door and a
    window. Returns the floor's centre in the monument's frame."""
    r, L = radius, length
    hl = L / 2
    with b.frame(at, rot):
        grid_box(b, (L + 0.6, 2 * r + 0.6, 0.2), (0, 0, 0.1), mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66), step=3.0)
        with b.frame((0, 0, 0.2), (0, 90, 0)):
            for k in range(9):
                a0, a1 = 90.0 + 20.0 * k, 90.0 + 20.0 * (k + 1)
                if k == 6:          # one strip of sheeting torn away over the far end
                    arc_panel(b, r, a0 + 0.3, a1 - 0.3, -hl + 0.02, hl * 0.2, 0.05, 2, mat=CORRUGATED, tint=tint,
                              solid=True)
                    continue
                arc_panel(b, r, a0 + 0.3, a1 - 0.3, -hl + 0.02, hl - 0.02, 0.05, 2, mat=CORRUGATED, tint=tint,
                          solid=True)
        for k in range(int(L / 3.0) + 1):
            x = -hl + 0.15 + (L - 0.3) * k / int(L / 3.0)
            pts = [(x, (r + 0.05) * math.cos(math.radians(a)), 0.2 + (r + 0.05) * math.sin(math.radians(a)))
                   for a in range(0, 181, 15)]
            b.tube(pts, 0.06, mat=PAINT, tint=rib, sides=5, cap=False)
        # +X end: a wide opening, the doors slid aside on their track.
        jamb = door_w / 2
        top = 0.2 + door_h
        outline = [(r, 0.2), (jamb, 0.2), (jamb, top), (-jamb, top), (-jamb, 0.2), (-r, 0.2)] + \
            [(r * math.cos(math.pi * k / 16), 0.2 + r * math.sin(math.pi * k / 16)) for k in range(15, 0, -1)]
        end_wall(b, (hl - 0.05, 0, 0), 90.0, outline, door=(-jamb, jamb, top), tint=end_tint)
        b.box((0.12, 2 * r - 0.4, 0.14), (hl + 0.05, 0, top + 0.3), mat=PAINT, tint=DARK, solid=False)
        for s in (-1, 1):
            grid_box(b, (0.08, door_w / 2 + 0.1, door_h), (hl + 0.14, s * (jamb + slid - door_w / 4), 0.2 + door_h / 2),
                     mat=CORRUGATED, tint=srgb(0.55, 0.58, 0.55), step=1.2)
            b.box((0.1, door_w / 2 + 0.1, 0.12), (hl + 0.16, s * (jamb + slid - door_w / 4), 0.2 + door_h * 0.5),
                  mat=PAINT, tint=DARK, solid=False)
        b.marker("door", (hl + 1.2, 0, 0.2), kind_of="barn")
        # -X end: closed, a person door and a window.
        outline2 = [(r, 0.2), (0.5, 0.2), (0.5, 2.5), (-0.5, 2.5), (-0.5, 0.2), (-r, 0.2)] + \
            [(r * math.cos(math.pi * k / 16), 0.2 + r * math.sin(math.pi * k / 16)) for k in range(15, 0, -1)]
        end_wall(b, (-hl + 0.05, 0, 0), -90.0, outline2, door=(-0.5, 0.5, 2.5), tint=end_tint)
        with b.frame((-hl - 0.02, -0.5, 0.2), (0, 0, 110.0)):
            b.box((0.05, 1.0, 2.28), (0, 0.5, 1.14), mat=PAINT, tint=TEAL, solid=False)
        b.box((0.06, 1.4, 0.9), (-hl - 0.03, 2.4, 3.2), mat=GLASS, solid=False)
        b.box((0.07, 1.5, 1.0), (-hl - 0.025, 2.4, 3.2), mat=PAINT, tint=DARK, solid=False)
        centre = b.world((0, 0, 0.2))
    return centre


def greenhouse(b, at, rot=(0, 0, 0), w=8.0, l=20.0, eave=2.6, ridge=4.2, broken=0.3, beds=True):
    """A gable glasshouse along X: aluminium frames every 2.5 m on a kerb, glass panes in every bay (some gone, one
    hanging), a sliding door at +X; inside, raised beds run wild, and one plant has grown out through the roof."""
    hw, hl = w / 2, l / 2
    n = int(round(l / 2.5))
    with b.frame(at, rot):
        b.box((l + 0.3, w + 0.3, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68))
        z0 = 0.3
        for k in range(n + 1):
            x = -hl + l * k / n
            for s in (-1, 1):
                b.box((0.07, 0.09, eave), (x, s * hw, z0 + eave / 2), mat=PAINT, tint=ALU, solid=False, vary=0)
                b.beam((x, s * hw, z0 + eave), (x, 0, z0 + ridge), 0.07, 0.09, mat=PAINT, tint=ALU)
            b.box((0.05, w, 0.05), (x, 0, z0 + eave), mat=PAINT, tint=ALU, solid=False, vary=0)
        for s in (-1, 1):
            b.box((l, 0.09, 0.09), (0, s * hw, z0 + eave), mat=PAINT, tint=ALU, solid=False, vary=0)
            b.box((l, 0.06, 0.06), (0, s * hw, z0 + 1.0), mat=PAINT, tint=ALU, solid=False, vary=0)
        b.box((l, 0.1, 0.1), (0, 0, z0 + ridge), mat=PAINT, tint=ALU, solid=False, vary=0)
        roof_len = math.hypot(hw, ridge - eave)
        ang = math.degrees(math.atan2(ridge - eave, hw))
        holes = []
        for k in range(n):
            x = -hl + l * (k + 0.5) / n
            bw = l / n - 0.08
            for s in (-1, 1):
                for (zc, hh) in ((z0 + 0.5, 0.95), (z0 + 1.0 + (eave - 1.0) / 2, eave - 1.05)):
                    if b.rng.random() < broken:
                        continue
                    b.box((bw, 0.01, hh), (x, s * hw, zc), mat=GLASS, solid=False)
                if b.rng.random() < broken:
                    holes.append((x, s))
                    continue
                with b.frame((x, s * hw / 2, z0 + (eave + ridge) / 2), (-s * ang, 0, 0)):
                    b.box((bw, roof_len - 0.05, 0.01), (0, 0, 0), (0, 0, 0), mat=GLASS, solid=False)
        for s in (-1, 1):
            b.collide_box(Vector((0, s * hw, z0 + eave / 2)), (l, 0.1, eave), (0, 0, 0))
        # Gable ends: glass with a sliding door at +X.
        gable = [(-hw, 0.0), (hw, 0.0), (hw, eave), (0.0, ridge), (-hw, eave)]
        with b.frame((-hl, 0, z0), (0, 0, 90)):
            b.extrude(gable, 0.01, (0, 0, 0), (0, 0, 0), mat=GLASS, solid=False)
        b.collide_box(Vector((-hl, 0, z0 + eave / 2)), (0.1, w, eave), (0, 0, 0))
        with b.frame((hl, 0, z0), (0, 0, 90)):
            b.extrude([(-hw, eave), (hw, eave), (0.0, ridge)], 0.01, (0, 0, 0), (0, 0, 0), mat=GLASS, solid=False)
            for x0, x1 in ((-hw, -0.8), (0.8, hw)):
                b.box((x1 - x0, 0.01, eave), ((x0 + x1) / 2, 0, eave / 2), mat=GLASS, solid=False)
            b.box((1.6, 0.04, 2.25), (1.5, -0.08, 1.13), mat=PAINT, tint=ALU, solid=False)
            b.box((1.5, 0.01, 2.15), (1.5, -0.1, 1.13), mat=GLASS, solid=False)
        for (y0, y1) in ((-hw, -0.8), (0.8, hw)):
            b.collide_box(Vector((hl, (y0 + y1) / 2, z0 + eave / 2)), (0.1, y1 - y0, eave), (0, 0, 0))
        b.marker("door", (hl + 0.8, 0, 0.3), kind_of="greenhouse")
        # Raised beds run wild; one plant through a roof hole; a pane hanging off the frame.
        if beds:
            for y in (-2.4, 0.0, 2.4):
                if abs(y) < 0.1:
                    continue
                b.box((l - 2.0, 1.2, 0.45), (0, y, z0 + 0.225), mat=PAINT, tint=WOOD_GREY)
                b.box((l - 2.2, 1.0, 0.05), (0, y, z0 + 0.45), mat=CONCRETE, tint=SOIL, solid=False)
                for j in range(int((l - 2.0) / 2.3)):
                    xx = -hl + 1.8 + j * 2.3 + b.rng.uniform(-0.3, 0.3)
                    bush(b, (xx, y + b.rng.uniform(-0.2, 0.2), z0 + 0.45), 0.5 + 0.5 * b.rng.random(),
                         LEAF if b.rng.random() < 0.6 else LEAF_DRY, lumps=2)
            if holes:
                hx, hs = holes[0]
                for k in range(4):
                    bush(b, (hx + b.rng.uniform(-0.5, 0.5), hs * hw * 0.5, z0 + 0.8 + 0.9 * k), 1.0, LEAF, lumps=3)
        with b.frame((-hl + l * 1.5 / n, -hw - 0.1, z0 + eave - 0.1), (-58, 6, 0)):
            b.box((l / n - 0.1, 0.01, eave - 1.1), (0, 0, -(eave - 1.1) / 2), mat=GLASS, solid=False)


def tractor(b, at, yaw=0.0, tint=TRACTOR_TEAL):
    """A compact colonist tractor: lugged rear wheels (the crawler wheel piece), small steered front wheels, the
    engine hood and grille, fenders and seat under a rollover frame with a canopy, an exhaust stack, a rear hitch and
    front weights. Front toward -Y."""
    with b.frame(at, (0, 0, yaw)):
        for s in (-1, 1):
            b.piece("wheel-crawler", (s * 0.98, 0.7, 0.0), (0, 0, 0 if s < 0 else 180))
            wheel(b, (s * 0.82, -1.45, 0.42), (0, 0, 0), 0.42, 0.26, sides=14)
        b.box((0.62, 2.7, 0.45), (0, -0.3, 0.72), mat=PAINT, tint=DARK)
        b.cyl(0.06, 1.7, (0, -1.45, 0.42), (0, 90, 0), mat=PAINT, tint=DARK, sides=8, solid=False)
        b.box((0.82, 1.55, 0.7), (0, -0.95, 1.33), mat=PAINT, tint=tint, bevel=0.07)
        b.box((0.72, 0.06, 0.55), (0, -1.74, 1.3), mat=PAINT, tint=DARK, solid=False)
        for k in range(4):
            b.box((0.66, 0.02, 0.04), (0, -1.775, 1.12 + 0.12 * k), mat=PAINT, tint=GREY, solid=False, vary=0)
        for s in (-1, 1):
            b.box((0.14, 0.04, 0.1), (s * 0.3, -1.75, 1.55), mat=GLOSS, tint=srgb(0.9, 0.9, 0.85), solid=False,
                  vary=0)
            b.box((0.56, 1.35, 0.06), (s * 0.98, 0.7, 1.68), mat=PAINT, tint=tint)
            b.box((0.06, 1.35, 0.5), (s * 0.68, 0.7, 1.42), mat=PAINT, tint=tint, solid=False)
        b.box((0.9, 0.9, 0.1), (0, 0.6, 1.25), mat=PAINT, tint=DARK)
        b.box((0.5, 0.45, 0.12), (0, 0.65, 1.42), mat=MATTE, tint=srgb(0.15, 0.15, 0.16), solid=False)
        b.box((0.5, 0.1, 0.5), (0, 0.9, 1.7), (-10, 0, 0), mat=MATTE, tint=srgb(0.15, 0.15, 0.16), solid=False)
        b.cyl(0.03, 0.6, (0, 0.05, 1.6), (-30, 0, 0), mat=PAINT, tint=DARK, sides=6, solid=False)
        pts = [(0.2 * math.cos(2 * math.pi * k / 12), 0.12 + 0.2 * math.sin(2 * math.pi * k / 12) * 0.5,
                1.88 + 0.2 * math.sin(2 * math.pi * k / 12) * 0.87) for k in range(13)]
        b.tube(pts, 0.015, mat=PAINT, tint=DARK, sides=4, cap=False)
        for (x, y) in ((-0.62, 1.25), (0.62, 1.25), (-0.62, 0.05), (0.62, 0.05)):
            b.box((0.07, 0.07, 1.2), (x, y, 2.3), mat=PAINT, tint=DARK, solid=False)
        b.box((1.5, 1.55, 0.08), (0, 0.65, 2.92), mat=PAINT, tint=tint, bevel=0.02)
        b.cyl(0.05, 1.3, (0.32, -1.35, 2.2), mat=PAINT, tint=DARK, sides=8, solid=False)
        b.cyl(0.06, 0.12, (0.32, -1.35, 2.88), mat=RUST, tint=(1, 1, 1), sides=8, solid=False)
        for s in (-1, 1):
            b.beam((s * 0.3, 1.2, 0.6), (s * 0.35, 1.9, 0.5), 0.06, 0.06, mat=PAINT, tint=DARK)
        b.beam((0, 1.25, 1.1), (0, 1.9, 0.8), 0.05, 0.05, mat=PAINT, tint=DARK)
        for k in range(4):
            b.box((0.7, 0.12, 0.3), (0, -1.86 - 0.13 * k, 0.75), mat=PAINT, tint=DARK, solid=False)
        stencil(b, "C-7", (0.415, -1.0, 1.4), (0, 0, 90), 0.18, OFFWHITE)
        b.collide_box(Vector((0, -0.2, 1.1)), (2.0, 3.6, 2.2), (0, 0, 0))


def round_bale(b, at, yaw=0.0, tint=WRAP_GREEN, lying=True):
    """A round bale in its wrap, on its side (or on end)."""
    x, y, z = at
    rot = (0, 90, yaw) if lying else (0, 0, yaw)
    lift = 0.75 if lying else 0.6
    with b.frame((x, y, z + lift), rot):
        b.cyl(0.75, 1.2, (0, 0, 0), mat=MATTE, tint=tint, sides=16, solid=True)
        for s in (-1, 1):
            b.cyl(0.6, 0.02, (0, 0, s * 0.6), mat=MATTE, tint=STRAW, sides=16, solid=False, vary=0.05)


def wind_pump(b, at, yaw=0.0, height=9.5, vane_text="PLOT 12"):
    """A farm wind pump: a lattice tower on four footings, a small platform, the multi-bladed wheel on its head
    facing -Y, the tail vane, the pump rod down to the stand at the foot; a stock tank and a trough beside it."""
    x, y, z = at
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((0.5, 0.5, 0.4), (x + sx * 1.3, y + sy * 1.3, 0.1), mat=CONCRETE, tint=srgb(0.75, 0.74, 0.71))
    with b.frame((x, y, 0.3), (0, 0, 0)):
        lattice(b, 0.0, height - 0.3, 2.6, 0.7, 4, member=0.08, tint=GALV, solid=True)
    b.box((1.4, 1.4, 0.05), (x, y, height - 1.0), mat=GRATE, tint=GREY, solid=False, vary=0)
    rungs(b, (x, y - 0.75, 0.3), height - 1.3, width=0.36, rail_tint=GALV)
    with b.frame((x, y, height + 0.2), (0, 0, yaw)):
        b.box((0.5, 0.7, 0.5), (0, 0.1, 0), mat=PAINT, tint=DARK)
        b.cyl(0.08, 0.6, (0, -0.4, 0), (90, 0, 0), mat=PAINT, tint=GREY, sides=8, solid=False)
        with b.frame((0, -0.75, 0), (0, 0, 0)):
            for rr in (0.8, 1.9):
                pts = [(rr * math.cos(2 * math.pi * k / 24), 0.0, rr * math.sin(2 * math.pi * k / 24))
                       for k in range(25)]
                b.tube(pts, 0.025, mat=PAINT, tint=GALV, sides=4, cap=False)
            for k in range(18):
                a = 2 * math.pi * k / 18
                with b.frame((1.35 * math.cos(a), 0, 1.35 * math.sin(a)), (0, -math.degrees(a), 0)):
                    b.box((1.1, 0.012, 0.3), (0, 0, 0), (0, 0, 22), mat=PAINT, tint=GALV, solid=False, vary=0)
            for k in range(6):
                a = 2 * math.pi * k / 6
                b.beam((0, 0, 0), (1.9 * math.cos(a), 0, 1.9 * math.sin(a)), 0.03, 0.03, mat=PAINT, tint=GREY)
        b.box((0.06, 2.4, 0.06), (0, 1.4, 0.05), mat=PAINT, tint=GREY, solid=False)
        b.extrude([(0.0, -0.3), (1.6, -0.6), (1.6, 0.9), (0.0, 0.4)], 0.03, (0, 2.0, 0.0), (0, 0, 90),
                  mat=PAINT, tint=OFFWHITE, solid=False)
        stencil(b, vane_text, (-0.02, 2.75, -0.05), (0, 0, -90), 0.2, RED, align="centre", jitter=0.6)
    b.cyl(0.03, height - 0.5, (x, y, 0.3 + (height - 0.5) / 2), mat=PAINT, tint=srgb(0.7, 0.7, 0.7), sides=5,
          solid=False)
    b.box((0.5, 0.5, 0.7), (x, y, 0.65), mat=PAINT, tint=srgb(0.32, 0.4, 0.36))
    b.cyl(0.06, 0.4, (x + 0.4, y, 0.9), (0, 90, 0), mat=PAINT, tint=srgb(0.32, 0.4, 0.36), sides=8, solid=False)
    tx, ty = x + 4.2, y + 0.5
    b.cyl(2.4, 0.9, (tx, ty, 0.45), mat=CORRUGATED, tint=srgb(0.66, 0.68, 0.68), sides=28, cap=False)
    b.cyl(2.35, 0.04, (tx, ty, 0.72), mat=GLOSS, tint=srgb(0.12, 0.17, 0.18), sides=28, solid=False)
    b.cyl(2.35, 0.04, (tx, ty, 0.04), mat=CONCRETE, tint=srgb(0.6, 0.6, 0.58), sides=28, solid=False)
    b.tube([(x + 0.4, y, 0.9), (x + 1.5, y + 0.3, 1.1), (tx - 2.2, ty, 1.05)], 0.05, mat=PAINT, tint=GALV, sides=6)
    b.collide_box(Vector((tx, ty, 0.45)), (4.6, 4.6, 0.9), (0, 0, 0))
    b.marker("landmark", (x, y, height + 2.0), kind_of="wind-pump")


def post_rail(b, pts, height=1.25, post=2.4, tint=WOOD_GREY, rails=3, missing=0.08):
    """A timber post-and-rail fence along `pts`: square posts, rails between them, a few rails down."""
    p = [Vector((q[0], q[1], 0)) for q in pts]
    for a, c in zip(p, p[1:]):
        d = c - a
        n = max(1, int(math.ceil(d.length / post)))
        for k in range(n + 1):
            q = a + d * (k / n)
            b.box((0.12, 0.12, height + 0.1), (q.x, q.y, (height + 0.1) / 2),
                  (b.rng.uniform(-2, 2), b.rng.uniform(-2, 2), math.degrees(math.atan2(d.y, d.x))), mat=PAINT,
                  tint=tint, solid=False, vary=0.06)
        for k in range(n):
            q0, q1 = a + d * (k / n), a + d * ((k + 1) / n)
            for j in range(rails):
                z = 0.35 + (height - 0.45) * j / max(1, rails - 1)
                if b.rng.random() < missing:
                    if b.rng.random() < 0.5:
                        b.beam((q0.x, q0.y, z), (q1.x, q1.y, 0.08), 0.05, 0.11, mat=PAINT, tint=tint)
                    continue
                b.beam((q0.x, q0.y, z), (q1.x, q1.y, z), 0.05, 0.11, mat=PAINT, tint=tint)
        mid = (a + c) / 2
        b.collide_box(Vector((mid.x, mid.y, height / 2)), (d.length, 0.12, height),
                      (0, 0, math.degrees(math.atan2(d.y, d.x))))


# ------------------------------------------------------------------------------------------------ harbour

def bollard(b, at, tint=srgb(0.18, 0.18, 0.19)):
    """A cast mooring bollard: a waisted post with a cap, bolted down."""
    x, y, z = at
    b.lathe([(0.0, 0.0), (0.24, 0.0), (0.2, 0.08), (0.16, 0.35), (0.19, 0.5), (0.26, 0.55), (0.26, 0.62),
             (0.0, 0.64)], (x, y, z), mat=PAINT, tint=tint, sides=12, solid="hull")


def fishing_boat(b, at, yaw=0.0, length=10.0, width=3.4, tint=srgb(0.2, 0.34, 0.5), band=OFFWHITE, name="KESTREL",
                 roll=0.0, pitch=0.0, sink=0.0, wheelhouse=True, gantry=True):
    """A small fishing boat floating with its waterline at z = 0 of its frame (bow toward -Y): a lofted round-bilge
    hull with a rising sheer and a transom, a white sheer strake and the name at the bow, the wheelhouse forward with
    windows and a mast and boom, a gantry and net drum at the stern, tyre fenders. `sink` lowers it (half-sunk)."""
    L, hw = length, width / 2
    draft = 1.2
    rings = []
    for i in range(13):
        t = i / 12
        w = hw * (math.sin(math.pi / 2 * min(1.0, t / 0.5)) ** 0.8 if t < 0.5 else 1.0 - 0.1 * (t - 0.5) / 0.5)
        w = max(0.05, w)
        s = 1.1 + 0.6 * (1 - t) ** 2
        k = -draft + (1.2 * (1 - t / 0.2) if t < 0.2 else 0.0)
        y = -L / 2 + L * t
        rings.append([(-w, y, s), (-w * 0.98, y, k + 0.75 * draft), (-w * 0.62, y, k + 0.18 * draft), (0.0, y, k),
                      (w * 0.62, y, k + 0.18 * draft), (w * 0.98, y, k + 0.75 * draft), (w, y, s)])
    with b.frame(at, (pitch, roll, yaw)):
        with b.frame((0, 0, -sink), (0, 0, 0)):
            from .structures import loft
            loft(b, rings, mat=PAINT, tint=tint, solid="hull", wear=0.6)
            # Sheer strake and rubbing strip, the name, the deck's bulwark rail.
            for s_ in (-1, 1):
                pts = [(s_ * (abs(r[0][0]) + 0.012), r[0][1], r[0][2] + 0.08) for r in rings[1:]]
                b.tube(pts, 0.1, mat=PAINT, tint=band, sides=6, cap=False)
                pts2 = [(s_ * (abs(r[0][0]) + 0.02), r[0][1], r[0][2] - 0.45) for r in rings[2:]]
                b.tube(pts2, 0.05, mat=PAINT, tint=DARK, sides=5, cap=False)
                stencil(b, name, (s_ * (abs(rings[2][0][0]) + 0.03), -L / 2 + L * 2 / 12 + 0.3, rings[2][0][2] - 0.75),
                        (0, 0, 90 * s_), 0.24, band, align="centre")
                for k in range(3):
                    yy = -L * 0.1 + L * 0.2 * k
                    b.cyl(0.32, 0.22, (s_ * (hw + 0.12), yy, 0.75), (0, 90, 0), mat=MATTE, tint=RUBBER, sides=10,
                          cap=True, solid=False)
            if wheelhouse:
                wy = -L * 0.08
                ws = 1.1 + 0.6 * (1 - 0.42) ** 2
                grid_box(b, (2.2, 2.4, 2.0), (0, wy, ws + 1.0), mat=PAINT, tint=OFFWHITE, step=0.8)
                b.box((2.4, 2.6, 0.1), (0, wy, ws + 2.05), mat=PAINT, tint=tint)
                b.box((2.0, 0.02, 0.7), (0, wy - 1.21, ws + 1.4), (-10, 0, 0), mat=GLASS, solid=False)
                for s_ in (-1, 1):
                    b.box((0.02, 1.4, 0.6), (s_ * 1.11, wy - 0.2, ws + 1.4), mat=GLASS, solid=False)
                b.box((0.7, 0.04, 1.8), (0.6, wy + 1.21, ws + 0.9), mat=PAINT, tint=DARK, solid=False)
                b.cyl(0.07, 5.0, (0, wy + 0.4, ws + 4.6), mat=PAINT, tint=OFFWHITE, sides=8, solid=False)
                b.box((1.4, 0.06, 0.06), (0, wy + 0.4, ws + 5.4), mat=PAINT, tint=OFFWHITE, solid=False)
                b.beam((0, wy + 0.5, ws + 2.6), (0, wy + 4.0, ws + 1.4), 0.08, 0.08, mat=PAINT, tint=OFFWHITE)
                b.box((0.18, 0.18, 0.16), (0, wy + 0.4, ws + 7.1), mat=LAMP, tint=WARM_LAMP, solid=False)
                for s_ in (-1, 1):
                    b.box((0.12, 0.1, 0.1), (s_ * 0.7, wy + 0.4, ws + 5.5), mat=GLOSS, tint=RED if s_ < 0 else
                          srgb(0.2, 0.7, 0.3), solid=False, vary=0)
                ball(b, (1.12, wy + 0.6, ws + 1.3), 0.3, ORANGE, mat=GLOSS, sides=10, rings=5)
            if gantry:
                gy = L / 2 - 0.6
                for s_ in (-1, 1):
                    b.beam((s_ * (hw - 0.25), gy, 1.15), (s_ * (hw - 0.6), gy - 0.4, 4.2), 0.14, 0.14, mat=PAINT,
                           tint=YELLOW)
                b.box((2.0, 0.16, 0.16), (0, gy - 0.4, 4.2), mat=PAINT, tint=YELLOW, solid=False)
                b.cyl(0.6, 1.8, (0, gy - 1.6, 1.85), (0, 90, 0), mat=PAINT, tint=srgb(0.25, 0.4, 0.3), sides=14)
                b.cyl(0.66, 0.06, (0.95, gy - 1.6, 1.85), (0, 90, 0), mat=PAINT, tint=DARK, sides=14, solid=False)
                b.cyl(0.66, 0.06, (-0.95, gy - 1.6, 1.85), (0, 90, 0), mat=PAINT, tint=DARK, sides=14, solid=False)
                b.cyl(0.62, 1.7, (0, gy - 1.6, 1.85), (0, 90, 0), mat=MATTE, tint=srgb(0.2, 0.3, 0.25), sides=14,
                      solid=False)




def pedestal_crane(b, at, yaw=0.0, jib=14.0, luff=38.0, drop=6.0, tint=srgb(0.85, 0.6, 0.15)):
    """A quayside pedestal crane: a steel column on a plinth, the slewing house with its cab and counterweight, a
    lattice jib luffed `luff` degrees up toward -Y, its stays, and the hook block `drop` metres under the jib tip."""
    with b.frame(at, (0, 0, yaw)):
        b.cyl(1.1, 0.5, (0, 0, 0.25), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68), sides=16)
        b.cyl(0.6, 3.6, (0, 0, 2.3), mat=PAINT, tint=tint, sides=16)
        b.cyl(0.9, 0.3, (0, 0, 4.25), mat=PAINT, tint=GREY, sides=16)
        grid_box(b, (2.4, 3.2, 1.9), (0, 0.6, 5.35), mat=PAINT, tint=tint, step=0.8)
        b.box((2.5, 1.2, 1.5), (0, 2.6, 5.1), mat=PAINT, tint=DARK, bevel=0.06)
        stripes(b, (-1.25, 3.21, 0), (1.25, 3.21, 0), 0.6, width=0.3, at_z=4.6, normal=(0, 1, 0))
        b.box((1.2, 0.02, 0.8), (-0.5, -1.01, 5.6), mat=GLASS, solid=False)
        b.box((0.02, 1.0, 0.8), (-1.21, -0.3, 5.6), mat=GLASS, solid=False)
        rungs(b, (1.25, 0.8, 0.5), 3.9, width=0.4, rot=(0, 0, -90), rail_tint=tint)
        foot = Vector((0, -0.9, 4.8))
        e = math.radians(luff)
        with b.frame(tuple(foot), (90 - luff, 0, 0)):
            lattice(b, 0.0, jib, 1.0, 0.5, max(4, int(jib / 1.8)), member=0.07, tint=tint, solid=True)
        tip = foot + Vector((0, -math.cos(e), math.sin(e))) * jib
        apex = Vector((0, 0.6, 7.6))
        b.box((0.3, 0.3, 1.6), (0, 0.6, 6.8), mat=PAINT, tint=tint)
        for s_ in (-0.15, 0.15):
            rod(b, apex + Vector((s_, 0, 0)), tip + Vector((s_, 0, 0.2)), 0.02, tint=GREY, sides=4)
        rod(b, tip, tip - Vector((0, 0, drop)), 0.02, tint=BLACK, sides=4)
        b.box((0.4, 0.3, 0.5), tuple(tip - Vector((0, 0, drop + 0.25))), mat=PAINT, tint=YELLOW, solid=False)
        b.tube([tuple(tip - Vector((0, 0, drop + 0.5))), tuple(tip - Vector((0.1, 0, drop + 0.75))),
                tuple(tip - Vector((0, 0, drop + 0.9)))], 0.04, mat=PAINT, tint=DARK, sides=5, cap=False)
    b.marker("landmark", (at[0], at[1], at[2] + 4.8 + jib * math.sin(math.radians(luff))), kind_of="crane")


def lobster_pot(b, at, yaw=0.0):
    """A D-shaped creel: a weighted base, three hoops, mesh over them, a float line."""
    with b.frame(at, (0, 0, yaw)):
        b.box((0.7, 0.5, 0.05), (0, 0, 0.025), mat=PAINT, tint=srgb(0.3, 0.25, 0.2), solid=False)
        for x in (-0.3, 0.0, 0.3):
            pts = [(x, 0.25 * math.cos(math.pi * k / 6), 0.05 + 0.32 * math.sin(math.pi * k / 6)) for k in range(7)]
            b.tube(pts, 0.012, mat=PAINT, tint=DARK, sides=4, cap=False)
        with b.frame((0, 0, 0.05), (0, 90, 0)):
            arc_panel(b, 0.25, 90.0, 270.0, -0.32, 0.32, 0.01, 4, mat=CHAINLINK, tint=srgb(0.3, 0.45, 0.4))
        b.collide_box(Vector((0, 0, 0.2)), (0.7, 0.5, 0.4), (0, 0, 0))


# ------------------------------------------------------------------------------------------------ mining

MINE_YELLOW = srgb(0.90, 0.66, 0.12)


def _wheel_haul(b):
    """A haul truck's wheel: a 2.1 m earthmover tyre with deep chevron lugs on a deep dished rim with a ring of
    nuts. On the ground, axle along X, outer face toward -X."""
    r, w = 1.05, 0.78
    hw = w / 2
    prof = [(0.56, -hw + 0.03), (0.66, -hw - 0.02), (0.88, -hw - 0.04), (1.0, -hw + 0.02), (1.02, -hw + 0.1),
            (1.02, hw - 0.1), (1.0, hw - 0.02), (0.88, hw + 0.04), (0.66, hw + 0.02), (0.56, hw - 0.03)]
    with b.frame((0, 0, r), (0, 90, 0)):
        b.lathe(prof, (0, 0, 0), mat=MATTE, tint=RUBBER, sides=36, close=False, solid="hull", wear=0.6)
        for k in range(26):
            a = 2 * math.pi * k / 26
            for s in (-1, 1):
                c = Vector((math.cos(a) * 1.035, math.sin(a) * 1.035, s * hw * 0.42))
                b.box((0.07, 0.22, hw * 0.92), tuple(c), (0, s * 18.0, math.degrees(a)), mat=MATTE, tint=RUBBER,
                      solid=False, vary=0)
        b.cyl(0.57, w - 0.06, (0, 0, 0), mat=PAINT, tint=MINE_YELLOW, sides=24, solid=False)
        b.lathe([(0.56, 0.0), (0.42, -0.1), (0.25, -0.13), (0.22, -0.2), (0.0, -0.21)], (0, 0, -hw + 0.05),
                mat=PAINT, tint=MINE_YELLOW, sides=24, close=False, solid=False)
        for k in range(16):
            a = 2 * math.pi * k / 16
            b.cyl(0.03, 0.06, (math.cos(a) * 0.36, math.sin(a) * 0.36, -hw - 0.09), mat=PAINT, tint=GREY, sides=6,
                  solid=False, vary=0)
        b.cyl(0.16, 0.12, (0, 0, -hw - 0.18), mat=PAINT, tint=DARK, sides=12, solid=False)


kit.PIECES["wheel-haul"] = _wheel_haul


def haul_truck(b, at, yaw=0.0, tint=MINE_YELLOW, body_tint=None, tip=0.0, code="HT 06"):
    """A rigid mining haul truck, about 10 m long and 4.6 m wide: two axles on 2.1 m tyres (twin at the rear), a
    deck over the engine with the offset cab on the left, a ladder up the front, the radiator grille between the
    front wheels; the dump body with its canopy over the cab, tipped `tip` degrees on its hinge by two hoist rams.
    Front toward -Y. Returns the body's floor centre in the monument's frame."""
    body_tint = body_tint or srgb(0.62, 0.5, 0.28)
    with b.frame(at, (0, 0, yaw)):
        # Frame rails, axles, wheels.
        for x in (-0.7, 0.7):
            b.box((0.35, 8.6, 0.8), (x, 0.3, 1.5), mat=PAINT, tint=DARK)
        for y, twin in ((-3.0, False), (2.6, True)):
            b.box((2.6, 0.7, 0.7), (0, y, 1.05), mat=PAINT, tint=DARK)
            for s in (-1, 1):
                b.piece("wheel-haul", (s * 1.65, y, 0.0), (0, 0, 0 if s < 0 else 180))
                if twin:
                    b.piece("wheel-haul", (s * 0.85, y, 0.0), (0, 0, 0 if s < 0 else 180))
        # The front: bumper, grille, the deck over the engine, the cab on the left, the ladder, rails.
        b.box((4.4, 0.5, 0.7), (0, -4.6, 1.3), mat=PAINT, tint=tint, bevel=0.05)
        b.box((2.0, 0.4, 1.6), (0, -4.15, 2.3), mat=PAINT, tint=DARK)
        for k in range(8):
            b.box((1.8, 0.05, 0.05), (0, -4.37, 1.65 + 0.18 * k), mat=PAINT, tint=GREY, solid=False, vary=0)
        grid_box(b, (4.6, 1.9, 0.2), (0, -3.55, 3.2), mat=PAINT, tint=tint, step=1.2)
        grid_box(b, (2.2, 1.8, 1.6), (0, -3.3, 2.3), mat=PAINT, tint=tint, step=1.0)
        cab = (-1.5, -3.4, 3.3)
        grid_box(b, (1.4, 1.9, 1.9), (cab[0], cab[1], cab[2] + 0.95), mat=PAINT, tint=tint, step=0.7)
        b.box((1.5, 2.0, 0.1), (cab[0], cab[1], cab[2] + 1.95), mat=PAINT, tint=DARK)
        b.box((1.2, 0.02, 0.9), (cab[0], cab[1] - 0.96, cab[2] + 1.25), mat=GLASS, solid=False)
        b.box((0.02, 1.4, 0.8), (cab[0] - 0.71, cab[1], cab[2] + 1.25), mat=GLASS, solid=False)
        b.box((0.02, 1.4, 0.8), (cab[0] + 0.71, cab[1], cab[2] + 1.25), mat=GLASS, solid=False)
        from .kit import railing, stairs
        for (a, c) in (((-2.25, -4.45, 3.3), (2.25, -4.45, 3.3)), ((2.25, -4.45, 3.3), (2.25, -2.0, 3.3))):
            railing(b, a, c, 1.0, spacing=1.0, tint=YELLOW, toe=False)
        stairs(b, (1.0, -5.1, 0.6), (0, 0, 180), 0.6, 2.7, 1.9, tint=GREY, rails=True)
        for s in (-1, 1):
            b.box((0.3, 0.06, 0.18), (s * 1.8, -4.86, 1.55), mat=GLOSS, tint=srgb(0.9, 0.9, 0.85), solid=False,
                  vary=0)
            b.box((0.5, 0.7, 0.05), (s * 1.75, -3.0, 2.32), mat=PAINT, tint=DARK, solid=False)
        b.cyl(0.12, 1.2, (1.4, -2.2, 4.0), mat=PAINT, tint=DARK, sides=8, solid=False)
        b.cyl(0.6, 1.8, (1.75, -1.2, 2.0), (90, 0, 0), mat=PAINT, tint=DARK, sides=14)
        stencil(b, code, (-2.31, -3.2, 2.7), (0, 0, -90), 0.3, DARK)
        # The dump body on its hinge at the rear, the hoist rams, the canopy over the cab.
        hinge = Vector((0, 4.4, 2.0))
        with b.frame(tuple(hinge), (-tip, 0, 0)):
            grid_box(b, (4.5, 6.6, 0.25), (0, -3.4, 0.35), mat=PAINT, tint=body_tint, step=1.2)
            for s in (-1, 1):
                b.extrude([(-6.7, 0.35), (-0.2, 0.35), (0.0, 1.0), (-0.2, 2.4), (-6.7, 2.4)], 0.18,
                          (s * 2.2, 0, 0), (0, 0, 90), mat=PAINT, tint=body_tint, solid="hull")
                for k in range(5):
                    b.box((0.12, 0.2, 2.0), (s * 2.32, -6.0 + 1.3 * k, 1.4), mat=PAINT, tint=body_tint, solid=False)
                b.box((0.14, 6.6, 0.25), (s * 2.32, -3.4, 2.4), mat=PAINT, tint=body_tint, solid=False)
            grid_box(b, (4.5, 0.2, 3.25), (0, -6.7, 1.98), mat=PAINT, tint=body_tint, step=1.0)
            grid_box(b, (4.7, 2.6, 0.18), (0, -7.9, 3.6), mat=PAINT, tint=body_tint, step=1.2)
            b.box((4.7, 0.2, 0.6), (0, -9.1, 3.35), mat=PAINT, tint=body_tint, solid=False)
            for s in (-1, 1):
                b.beam((s * 2.2, -6.7, 2.4), (s * 2.3, -8.9, 3.5), 0.15, 0.2, mat=PAINT, tint=body_tint)
            b.mesh([(-2.3, -0.2, 0.35), (2.3, -0.2, 0.35), (2.3, 0.4, 1.4), (-2.3, 0.4, 1.4)], [[0, 1, 2, 3]],
                   mat=PAINT, tint=body_tint, solid=False)
            floor = b.world((0, -3.4, 0.5))
        for s in (-1, 1):
            base = Vector((s * 1.1, 0.0, 1.6))
            top = hinge + Vector((0, -3.6 * math.cos(math.radians(tip)), -0.2 + 3.6 * math.sin(math.radians(tip))))
            top.x = s * 1.1
            rod(b, base, base.lerp(top, 0.55), 0.16, tint=DARK, sides=10)
            rod(b, base.lerp(top, 0.5), top, 0.1, tint=srgb(0.8, 0.8, 0.8), sides=10)
        b.collide_box(Vector((0, 0, 1.9)), (4.6, 10.0, 3.6), (0, 0, 0))
    return floor


# ------------------------------------------------------------------------------------------------ logistics

RACK_BLUE = srgb(0.18, 0.32, 0.55)
RACK_ORANGE = srgb(0.86, 0.42, 0.12)


def forklift(b, at, yaw=0.0, tint=srgb(0.86, 0.6, 0.12), fork_h=0.1, load=False):
    """A counterbalance forklift, 3.4 m long: the chassis with its rounded counterweight at the back, four small solid
    tyres, the seat and controls under an overhead guard of four posts and a grille, the mast with its carriage and
    forks raised `fork_h` metres at the front (-Y), a pallet on the forks when `load`."""
    with b.frame(at, (0, 0, yaw)):
        b.box((1.1, 2.3, 0.7), (0, 0.15, 0.55), mat=PAINT, tint=tint, bevel=0.06)
        b.cyl(0.55, 1.12, (0, 1.2, 0.85), (0, 90, 0), mat=PAINT, tint=srgb(0.25, 0.25, 0.26), sides=16)
        b.box((1.12, 0.6, 0.6), (0, 1.15, 0.6), mat=PAINT, tint=srgb(0.25, 0.25, 0.26))
        for (y, r, w) in ((-0.65, 0.3, 0.24), (0.95, 0.25, 0.2)):
            for s in (-1, 1):
                wheel(b, (s * 0.52, y, r), (0, 0, 0), r, w, sides=12)
        b.box((0.55, 0.5, 0.12), (0, 0.45, 1.05), mat=MATTE, tint=srgb(0.15, 0.15, 0.16), solid=False)
        b.box((0.55, 0.12, 0.55), (0, 0.72, 1.3), (-12, 0, 0), mat=MATTE, tint=srgb(0.15, 0.15, 0.16), solid=False)
        b.cyl(0.18, 0.04, (0, -0.15, 1.45), (-55, 0, 0), mat=PAINT, tint=BLACK, sides=12, cap=False, solid=False)
        b.box((0.08, 0.08, 0.5), (0, -0.25, 1.15), (-30, 0, 0), mat=PAINT, tint=BLACK, solid=False)
        for (x, y) in ((-0.5, -0.45), (0.5, -0.45), (-0.5, 0.95), (0.5, 0.95)):
            b.box((0.07, 0.07, 1.25), (x, y, 1.5), mat=PAINT, tint=DARK, solid=False)
        b.box((1.1, 1.5, 0.05), (0, 0.25, 2.14), mat=PAINT, tint=DARK, solid=False)
        for k in range(6):
            b.box((0.04, 1.4, 0.06), (-0.45 + 0.18 * k, 0.25, 2.1), mat=PAINT, tint=DARK, solid=False, vary=0)
        # The mast at the front, the carriage and forks.
        for s in (-1, 1):
            b.box((0.12, 0.14, 2.3), (s * 0.35, -0.95, 1.3), mat=PAINT, tint=DARK)
        b.box((0.82, 0.12, 0.1), (0, -0.95, 2.4), mat=PAINT, tint=DARK, solid=False)
        b.cyl(0.05, 1.6, (0, -0.82, 1.0), mat=PAINT, tint=srgb(0.8, 0.8, 0.8), sides=8, solid=False)
        z = 0.12 + fork_h
        b.box((0.9, 0.08, 0.6), (0, -1.06, z + 0.32), mat=PAINT, tint=DARK)
        for s in (-1, 1):
            b.box((0.12, 1.1, 0.05), (s * 0.3, -1.65, z), mat=PAINT, tint=DARK, solid=False)
            b.box((0.12, 0.05, 0.5), (s * 0.3, -1.12, z + 0.25), mat=PAINT, tint=DARK, solid=False)
        if load:
            b.piece("pallet-boxes", (0, -1.7, z + 0.02), (0, 0, 90))
        b.box((0.2, 0.06, 0.08), (0.42, 1.68, 0.8), mat=GLOSS, tint=RED, solid=False, vary=0)
        b.collide_box(Vector((0, 0.15, 1.05)), (1.2, 2.8, 2.1), (0, 0, 0))


def rack_row(b, at, yaw, bays=10, levels=3, bay_w=2.7, depth=1.1, beam_h=1.9, fill=0.65, collapsed=None,
             pallets=True):
    """A row of pallet racking along X of its frame: upright frames (two posts with zigzag bracing) between bays,
    orange beams at each level, pallets of cartons (kit pieces) on the beams. `collapsed` (from_bay) leans the
    rest of the row over and drops its pallets on the floor."""
    with b.frame(at, (0, 0, yaw)):
        L = bays * bay_w
        top = levels * beam_h + 0.6
        for k in range(bays + 1):
            x = -L / 2 + bay_w * k
            lean = 0.0
            if collapsed is not None and k > collapsed:
                lean = min(70.0, 18.0 + 12.0 * (k - collapsed))
            with b.frame((x, 0, 0), (lean, 0, 0)):
                for y in (-depth / 2, depth / 2):
                    b.box((0.09, 0.07, top), (0, y, top / 2), mat=PAINT, tint=RACK_BLUE, solid=False, vary=0)
                    b.box((0.18, 0.16, 0.02), (0, y, 0.01), mat=PAINT, tint=RACK_BLUE, solid=False, vary=0)
                for j in range(int(top / 0.9)):
                    z0 = 0.15 + 0.9 * j
                    rod(b, (0, -depth / 2, z0), (0, depth / 2, z0 + 0.9), 0.018, tint=RACK_BLUE, sides=4)
            if k < bays and (collapsed is None or k < collapsed):
                for lv in range(levels):
                    z = beam_h * (lv + 1) - 0.4 if lv else 0.18
                    for y in (-depth / 2, depth / 2):
                        b.box((bay_w - 0.09, 0.05, 0.12), (x + bay_w / 2, y, z), mat=PAINT, tint=RACK_ORANGE,
                              solid=False, vary=0)
                    if pallets:
                        for j in (0, 1):
                            if b.rng.random() < fill:
                                b.piece("pallet-boxes", (x + bay_w * (0.27 + 0.46 * j), 0, z + 0.06),
                                        (0, 0, 90 + b.rng.uniform(-3, 3)))
            elif k < bays and pallets:
                for j in range(2):
                    if b.rng.random() < 0.7:
                        b.piece("pallet-boxes", (x + bay_w * (0.3 + 0.4 * j) + b.rng.uniform(-0.5, 0.5),
                                                 depth / 2 + 0.9 + b.rng.uniform(0, 1.2), 0.0),
                                (b.rng.uniform(-8, 8), b.rng.uniform(-8, 8) + (90 if b.rng.random() < 0.3 else 0),
                                 b.rng.uniform(0, 360)))
        b.collide_box(Vector((0, 0, top / 2)), (L if collapsed is None else bay_w * collapsed, depth, top),
                      (0, 0, 0))


def box_trailer(b, at, yaw=0.0, length=13.0, tint=OFFWHITE, band=ORANGE, code="DP-4 112", doors_open=True):
    """A box semi-trailer: a tall insulated box on a frame, a tandem axle at the rear, landing legs, the rear doors
    swung open (or shut), livery and codes. Rear toward +Y, the king pin at -Y."""
    hl = length / 2
    floor = 1.35
    h = 2.7
    with b.frame(at, (0, 0, yaw)):
        for x in (-0.5, 0.5):
            b.box((0.16, length - 0.6, 0.3), (x, 0, floor - 0.2), mat=PAINT, tint=DARK)
        grid_box(b, (2.5, length, h), (0, 0, floor + h / 2), mat=PANEL, tint=tint, step=1.5)
        b.box((2.52, length, 0.12), (0, 0, floor + 0.06), mat=PAINT, tint=GREY, solid=False)
        b.box((2.54, length + 0.02, 0.12), (0, 0, floor + h - 0.06), mat=PAINT, tint=GREY, solid=False)
        for s in (-1, 1):
            b.box((0.012, length - 1.0, 0.4), (s * 1.257, 0, floor + 0.8), mat=PAINT, tint=band, solid=False, vary=0,
                  wear=0.5)
            stencil(b, code, (s * 1.258, -hl + 3.0 if s < 0 else hl - 3.0, floor + 1.7), (0, 0, 90 * s), 0.34, DARK)
            for y in (hl - 2.6, hl - 1.3):
                b.piece("wheel-road", (s * 1.06, y, 0.0), (0, 0, 0 if s < 0 else 180))
            b.box((0.12, 0.12, floor - 0.25), (s * 0.55, -hl + 2.4, (floor - 0.25) / 2 + 0.1), mat=PAINT, tint=GREY,
                  solid=False)
        b.box((2.5, 0.12, 0.3), (0, hl + 0.05, 0.9), mat=PAINT, tint=DARK, solid=False)
        if doors_open:
            for s, ang in ((-1, -100.0), (1, 100.0)):
                with b.frame((s * 1.25, hl, floor + 0.05), (0, 0, ang)):
                    grid_box(b, (1.24, 0.06, h - 0.1), (-s * 0.62, 0.03, (h - 0.1) / 2), mat=PANEL, tint=tint,
                             step=1.0)
            b.box((2.3, 0.02, h - 0.2), (0, hl + 0.012, floor + h / 2), mat=MATTE, tint=srgb(0.04, 0.04, 0.04),
                  solid=False)
        b.collide_box(Vector((0, 0, floor + h / 2)), (2.5, length, h), (0, 0, 0))
    return b.world((0, hl - 2.0, floor + 0.1))
