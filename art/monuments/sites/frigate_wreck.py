"""Frigate Wreck (MONUMENTS.md §5.4, large tier, catalogue 43): a Bulwark frigate 420 m long that came down in the
Quiet and broke in two. Her hull is an armoured wedge lofted through hard-chined sections: a flat belly, flanks
flaring out to a chine carried by the armour belt, upper flanks leaning in hard to a crowned deck, an armoured
citadel along the spine, and a long bow tapering to a knife-edged prow. Olive above the belt, grey below, the belly
heat-shielded, the bow and the belly scorched black by the descent.

The bow half (240 m) ploughed on after the break and lies nose down, half buried in the wave of earth it pushed up,
its broken end reared off the ground and open: nine decks, bulkheads, frames and longerons, cables and pipes hanging.
On its back stand the stepped, faceted command tower with its mast, radomes and array faces, two gun turrets (the
second firing over the first) and the bow's missile cells; the hangar door gapes on its south flank. The stern half
(180 m) lies 130 m behind it, slewed round and rolled on its side with its broken end dug in, so its run lifts the
drive off the ground: three great bells in a row, radiator fins swept up from the deck over them and standing out of
its high flank, the barbette of its turret empty, the drive core laid open in its break (contamination 2). The
turret lies upside down in the scar between the halves among plates, frames, girders and burnt ground. Reclaimers
have put a scaffold and a ladder up into the bow's broken end; a hatch low on the bow's flank, where the hull meets
the ground, is the way in.
"""
import math

from mathutils import Euler, Matrix, Vector

from monkit import parts_landmarks as PL
from monkit import parts_large as P
from monkit.kit import BLACK, DARK, GREY, LIGHTGREY, YELLOW, cable, ladder, lamp, lattice_tower, railing, srgb
from monkit.surfaces import GLASS, GRATE, HULL, LAMP, MATTE, PAINT, PANEL, RUST

KEY = "frigate-wreck"
TITLE = "Frigate Wreck"
SEED = 4301
BUILDER = {"chunk": 96.0, "max_edge": 12.0, "lod_min": (0.0, 1.0, 6.0)}
LODS = [320.0, 1500.0, 22000.0]

# ------------------------------------------------------------------------------------------------ her lines

L = 420.0               # her length
BREAK = 0.43            # where she broke, a fraction of the length from the stern
T_BOW = 0.5             # where the bow's taper begins
T_PROW = 0.985          # the last ring; the prow cap runs on from it
T_RUN = 0.25            # where the stern's run begins: aft of it her keel rises and her beam narrows
KEEL, SPINE = -18.0, 17.0
# Half her section amidships (y across, z up) from the keel round to the spine: a flat belly, the lower flank flaring
# out to the chine (the widest line, under the armour belt), the upper flank leaning in hard to a crowned deck.
HALF = [(0.0, -18.0), (5.8, -18.0), (10.8, -17.4), (16.6, -12.8), (22.0, -7.6), (26.0, -2.0), (25.7, 3.0),
        (22.8, 7.6), (19.8, 12.0), (13.0, 16.0), (6.3, 16.8), (0.0, 17.0)]
NH = len(HALF)
NR = 2 * (NH - 1)       # a ring: the keel, the starboard side up to the spine, the port side down again
ORDER = [(y, z, -1.0) for y, z in HALF] + [(y, z, 1.0) for y, z in HALF[-2:0:-1]]
KINDS = ("belly", "belly", "lower", "lower", "lower", "belt", "upper", "upper", "shoulder", "deck", "deck")
DECKS = tuple(-15.2 + 3.6 * k for k in range(9))     # nine decks, 3.6 m apart
CITADEL = 4.5           # the armoured citadel's height over the spine
CODE = "F-412"          # her hull code, in the stencil's seven-segment glyphs

# Bulwark livery (MONUMENTS.md §3).
OLIVE = srgb(0.37, 0.40, 0.27)
OLIVE_DEEP = srgb(0.29, 0.32, 0.21)
GREY_HULL = srgb(0.45, 0.47, 0.46)
GREY_LIGHT = srgb(0.56, 0.58, 0.56)
BELT_T = srgb(0.29, 0.31, 0.31)
DECK_T = srgb(0.33, 0.345, 0.33)
SHIELD = srgb(0.25, 0.25, 0.245)
HULL_DARK = srgb(0.22, 0.23, 0.21)
BURNT = srgb(0.09, 0.085, 0.08)
MARK = srgb(0.84, 0.84, 0.78)
INSIDE = srgb(0.29, 0.30, 0.29)
VOID = srgb(0.045, 0.045, 0.045)
RUSTY = srgb(0.48, 0.30, 0.20)
POLE = srgb(0.50, 0.47, 0.40)
SOIL = srgb(0.45, 0.34, 0.23)
SOIL_DARK = srgb(0.31, 0.24, 0.17)

# Where the halves lie. The ship's own frame: x toward the prow (0 amidships), y to port, z up. The bow half turns
# about the keel of its broken end, the stern half about the middle of its own; (roll, pitch, yaw) in degrees.
BOW_AT = ((14.0, -10.0, 12.0), (12.0, 9.0, 6.0))
STERN_AT = ((-120.0, 2.0), (30.0, 5.0, 18.0))
STERN_SINK = 4.0        # how deep the stern's low side lies in the ground


def x_of(t):
    return (t - 0.5) * L


def t_of(x):
    return x / L + 0.5


def shape(t):
    """(beam scale, keel z, spine z) at station t (0 the stern .. 1 the prow): full amidships; aft of T_RUN the
    keel rising and the beam narrowing to the stern face; forward of T_BOW the bow narrowing to nothing while its
    keel rises and its deck falls to a knife edge."""
    if t > T_BOW:
        u = min(1.0, (t - T_BOW) / (1.0 - T_BOW))
        v = max(0.0, (u - 0.15) / 0.85)
        return 1.0 - u ** 1.5, KEEL + 15.0 * u ** 1.3, SPINE - 11.0 * v ** 1.7
    if t < T_RUN:
        k = (T_RUN - t) / T_RUN
        return 1.0 - 0.2 * k ** 1.5, KEEL + 9.0 * k ** 1.8, SPINE - 1.5 * k ** 2
    return 1.0, KEEL, SPINE


def mapped(t, y, z):
    """A point of the amidships section (y, z) moved to station t."""
    sb, zk, zs = shape(t)
    return y * sb, zk + (z - KEEL) / (SPINE - KEEL) * (zs - zk)


def ring(t, pull=None, grow=0.0):
    """The hull's ring at station t (NR points); `pull` moves each point along the hull (a torn edge), `grow` pushes
    the ring out from the section's middle (a collar)."""
    x = x_of(t)
    _, zk, zs = shape(t)
    zc = (zk + zs) / 2
    out = []
    for j, (y, z, s) in enumerate(ORDER):
        yy, zz = mapped(t, y, z)
        yy *= s
        if grow:
            d = Vector((yy, zz - zc))
            if d.length > 1e-6:
                d.normalize()
                yy, zz = yy + d.x * grow, zz + d.y * grow
        out.append((x + (pull[j] if pull else 0.0), yy, zz))
    return out


def kind(j):
    """What the strip of plating between ring points j and j + 1 is (both sides mirror)."""
    return KINDS[j if j < NH - 1 else NR - 1 - j]


def half_width(t, z):
    """Half the hull's width at height z at station t."""
    pts = [mapped(t, y, zz) for y, zz in HALF]
    for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
        if z1 - z0 > 1e-6 and z0 <= z <= z1:
            return y0 + (y1 - y0) * (z - z0) / (z1 - z0)
    return 0.0


def deck_z(t, y=0.0):
    """The deck's height at station t, |y| out from the spine."""
    pts = [mapped(t, yy, zz) for yy, zz in HALF[8:]]
    ay = abs(y)
    for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
        if y1 <= ay <= y0:
            return z0 + (z1 - z0) * (y0 - ay) / max(y0 - y1, 1e-6)
    return pts[-1][1] if ay < pts[-1][0] else pts[0][1]


def deck_edge(t):
    return mapped(t, *HALF[9])[0]


def euler_deg(m3):
    return tuple(math.degrees(a) for a in m3.to_euler())


def face_frame(t, j, f=0.5):
    """A frame on the plating at station t on strip j, f of the way up it: X along the hull (toward the prow on the
    starboard side, toward the stern on the port side, so text reads from outside), Z up the strip, -Y out of the
    hull. Returns (at, rot) for `Builder.frame`."""
    def point(tt):
        r = ring(tt)
        return Vector(r[j]).lerp(Vector(r[(j + 1) % NR]), f)
    dt = 0.003
    p = point(t)
    along = (point(min(t + dt, 1.0)) - point(t - dt)).normalized()
    r = ring(t)
    up = Vector(r[(j + 1) % NR]) - Vector(r[j])
    if j >= NH - 1:
        along, up = -along, -up
    up = (up - along * up.dot(along)).normalized()
    side = up.cross(along)
    return tuple(p), euler_deg(Matrix((along, side, up)).transposed())


def frame_matrix(at, rot, pivot):
    return Matrix.LocRotScale(Vector(at), Euler([math.radians(a) for a in rot]), None) @ Matrix.Translation(
        -Vector(pivot))


def bow_place():
    at, rot = BOW_AT
    return at, rot, (x_of(BREAK), 0.0, KEEL)


def stern_place():
    """The stern half's frame, its height found so its low side lies STERN_SINK metres in the ground."""
    (x, y), rot = STERN_AT
    pivot = (x_of(BREAK), 0.0, 0.0)
    m = frame_matrix((x, y, 0.0), rot, pivot)
    low = min((m @ Vector(p)).z for t in (0.0, 0.1, 0.2, 0.3, BREAK) for p in ring(t))
    return (x, y, -low - STERN_SINK), rot, pivot


def matrix_of(place):
    return frame_matrix(*place)


def hatch_site():
    """Where the bow's hatch is: the station where its starboard chine comes down to the ground, the door's world
    position and the way it faces."""
    m = matrix_of(bow_place())
    t = 0.52
    while t < 0.95 and (m @ Vector(ring(t)[5])).z > 1.0:
        t += 0.002
    at, rot = face_frame(t, 5, 0.08)
    fm = m @ Matrix.LocRotScale(Vector(at), Euler([math.radians(a) for a in rot]), None)
    return t, fm @ Vector((0.0, -2.6, 0.0)), (fm.to_3x3() @ Vector((0.0, -1.0, 0.0))).normalized()


def scaffold_site():
    """Where the Reclaimers' scaffold stands: its foot, the bow's heading on the ground and the side to port, and
    the height of the deck edge it climbs to."""
    mb = matrix_of(bow_place())
    edge = mb @ Vector((x_of(BREAK) - 1.0, -8.4, DECKS[0] + 0.2))
    a = mb @ Vector((x_of(BREAK), 0.0, KEEL))
    c = mb @ Vector((x_of(T_PROW), 0.0, KEEL))
    fwd = Vector((c.x - a.x, c.y - a.y, 0.0)).normalized()
    return Vector((edge.x, edge.y, 0.0)) - fwd * 3.6, fwd, Vector((-fwd.y, fwd.x, 0.0)), edge.z


# ------------------------------------------------------------------------------------------------ small helpers


def slab(b, quad, thick, toward, tint, mat=HULL, solid="hull"):
    """A plate over `quad` (four points), `thick` deep toward the point `toward`: a closed slab, so it shows from
    both sides."""
    outer = [Vector(q) for q in quad]
    c = Vector(toward)
    inner = [q + (c - q).normalized() * thick for q in outer]
    mid = sum(outer, Vector()) / 4
    verts = [tuple(q - mid) for q in outer + inner]
    faces = [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]]
    return b.mesh(verts, faces, tuple(mid), mat=mat, tint=tint, solid=solid, smooth=False)


def faceted(b, at, base, top, h, chamfer=(2.0, 2.0), shift=(0.0, 0.0), rot=(0, 0, 0), mat=HULL, tint=OLIVE,
            solid="hull"):
    """A faceted armoured block: a rectangle `base` (x, y) with chamfered corners at its foot and `top` h higher
    (moved by `shift`), joined by sloped faces."""
    def outline(sx, sy, c, z, dx, dy):
        hx, hy = sx / 2, sy / 2
        c = max(0.02, min(c, hx * 0.9, hy * 0.9))
        pts = [(hx - c, -hy), (hx, -hy + c), (hx, hy - c), (hx - c, hy), (-hx + c, hy), (-hx, hy - c),
               (-hx, -hy + c), (-hx + c, -hy)]
        return [(x + dx, y + dy, z) for x, y in pts]
    lo = outline(base[0], base[1], chamfer[0], 0.0, 0.0, 0.0)
    hi = outline(top[0], top[1], chamfer[1], h, shift[0], shift[1])
    faces = [list(range(8))[::-1], list(range(8, 16))] + [[i, (i + 1) % 8, 8 + (i + 1) % 8, 8 + i] for i in range(8)]
    return b.mesh(lo + hi, faces, at, rot, mat=mat, tint=tint, solid=solid, smooth=False)


def inset(points, t, amount):
    """Ring points moved `amount` metres in toward the section's middle."""
    _, zk, zs = shape(t)
    zc = (zk + zs) / 2
    out = []
    for x, y, z in points:
        d = Vector((-y, zc - z))
        if d.length > 1e-6:
            d.normalize()
        out.append((x, y + d.x * amount, z + d.y * amount))
    return out


# ------------------------------------------------------------------------------------------------ the hull


def plating(b, ts, rings, seed, holes=None):
    """The plates between consecutive rings, each a closed slab 0.8 m thick (so the hull shows its inside where it
    is torn, and every plate is its own collision): olive above the belt, grey below, the belly heat-shielded,
    scorched by the descent most on the belly and toward the prow, a few plates replaced in other shades."""
    heat = {"belly": 0.3, "lower": 0.16, "belt": 0.08, "upper": 0.0, "shoulder": -0.03, "deck": -0.07}
    base = {"belly": SHIELD, "lower": GREY_HULL, "belt": BELT_T, "upper": OLIVE, "shoulder": OLIVE, "deck": DECK_T}

    def tint(i, j):
        t = (ts[i] + ts[i + 1]) / 2
        k = kind(j)
        u = max(0.0, (t - T_BOW) / (1.0 - T_BOW))
        # Scorch streaks run along her length: the noise is stretched along the hull, blended in by degrees.
        burn = P.fbm(x_of(t) * 0.011, j * 0.62, 0.3, seed, 3) + heat[k] + 0.2 * u
        f = min(1.0, max(0.0, (burn - 0.6) / 0.36))
        f = f * f * (3 - 2 * f) * 0.92
        c = base[k]
        if P._hash(i, j, 3, seed) < 0.05:
            c = OLIVE_DEEP if k in ("upper", "shoulder", "deck") else GREY_LIGHT
        s = 0.93 + 0.14 * P._hash(i, j, 9, seed)
        return tuple(v * s * (1 - f) + w * f for v, w in zip(c, BURNT))

    thick = 0.8
    centres = [sum((Vector(p) for p in r), Vector()) / NR for r in rings]
    kept = {}
    for i in range(len(rings) - 1):
        for j in range(NR):
            if holes and holes(i, j):
                continue
            outer = [Vector(rings[i][j]), Vector(rings[i][(j + 1) % NR]), Vector(rings[i + 1][(j + 1) % NR]),
                     Vector(rings[i + 1][j])]
            if max(b.world(q).z for q in outer) < -0.8:
                continue
            kept[(i, j)] = outer
            cs = (centres[i], centres[i], centres[i + 1], centres[i + 1])
            inner = [q + (c - q).normalized() * min(thick, (c - q).length * 0.5) for q, c in zip(outer, cs)]
            mid = sum(outer, Vector()) / 4
            verts = [tuple(q - mid) for q in outer + inner]
            faces = [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]]
            b.mesh(verts, faces, tuple(mid), mat=HULL, tint=tint(i, j), solid=False, smooth=False)
    # Collision: a box under each run of up to three plates along a strip where her lines run straight.
    for j in range(NR):
        i = 0
        while i < len(rings) - 1:
            if (i, j) not in kept:
                i += 1
                continue
            k = i
            while (k + 1, j) in kept and k - i < 2 and T_RUN <= ts[i] and ts[k + 2] <= T_BOW:
                k += 1
            a0, a1 = kept[(i, j)][0], kept[(i, j)][1]
            c0, c1 = kept[(k, j)][3], kept[(k, j)][2]
            u = ((c0 + c1) / 2 - (a0 + a1) / 2)
            v = ((a1 + c1) / 2 - (a0 + c0) / 2)
            if u.length > 1e-3 and v.length > 1e-3:
                n = u.cross(v).normalized()
                inward = (centres[i] - (a0 + a1 + c0 + c1) / 4)
                if n.dot(inward) < 0:
                    n = -n
                uu = u.normalized()
                vv = n.cross(uu).normalized()
                centre = (a0 + a1 + c0 + c1) / 4 + n * (thick / 2)
                rot = euler_deg(Matrix((uu, vv, n)).transposed())
                b.collide_box(tuple(centre), (u.length, abs(v.dot(vv)), thick), rot)
            i = k + 1


def belts(b, ts, t_max=0.86, end=None):
    """The armour belt: plates a metre thick proud of the hull along both chines, a seam between each; `end` (a
    station) stops them short of a break."""
    for i in range(len(ts) - 1):
        t0, t1 = ts[i], ts[i + 1]
        if t0 >= t_max or (end is not None and min(abs(t0 - end), abs(t1 - end)) * L < 6.0):
            continue
        r0, r1 = ring(t0, grow=0.6), ring(t1, grow=0.6)
        g = 0.18 / max(1e-3, (t1 - t0) * L)
        for j0, j1 in ((5, 6), (16, 17)):
            a0, a1 = Vector(r0[j0]).lerp(Vector(r1[j0]), g), Vector(r0[j1]).lerp(Vector(r1[j1]), g)
            c0, c1 = Vector(r1[j0]).lerp(Vector(r0[j0]), g), Vector(r1[j1]).lerp(Vector(r0[j1]), g)
            mid = (a0 + c1) / 2
            _, zk, zs = shape((t0 + t1) / 2)
            slab(b, (a0, a1, c1, c0), 1.1, (mid.x, 0.0, (zk + zs) / 2), BELT_T, solid=False)


def edge_bars(b, ts, t_max=0.92, skip=None):
    """Rubbing bars along both deck edges: a hard line down her length."""
    for i in range(len(ts) - 1):
        if ts[i] >= t_max or (skip and skip(i)):
            continue
        r0, r1 = ring(ts[i]), ring(ts[i + 1])
        for j in (9, 13):
            b.beam(r0[j], r1[j], 0.7, 0.7, mat=HULL, tint=HULL_DARK)


def break_end(b, t_b, d, access=(), omit=()):
    """The open end where she broke, at station t_b; d = -1 if it faces the stern (the bow half), +1 if it faces the
    prow (the stern half). Nine decks cut into bays by bulkheads, each bay torn off at its own length, plates bent
    down out of it; frames and longerons standing out of the torn plating; cables and pipes hanging; plates of the
    rim bent outward; a dark bulkhead 26 m in. `access`: bays (deck, y) kept whole for a way in or loot; `omit`:
    bays left out."""
    xb = x_of(t_b)
    x_in = xb - d * 26.0
    _, zk, zs = shape(t_b)
    zc = (zk + zs) / 2
    # The bulkhead deep inside, everything beyond it dark.
    wall = inset(ring(t_b), t_b, 1.0)
    verts = [(x_in, y, z) for _, y, z in wall] + [(x_in - d * 0.6, y, z) for _, y, z in wall]
    n = len(wall)
    faces = [list(range(n)), list(range(n, 2 * n))[::-1]] + [[k, (k + 1) % n, n + (k + 1) % n, n + k]
                                                              for k in range(n)]
    b.mesh(verts, faces, mat=PAINT, tint=VOID, solid="hull", smooth=False)
    lines = (-16.0, -4.5, 4.5, 16.0)
    shaft = (5.0, 15.5, 3, 6)       # a column of bays blown out through decks 3 to 6
    for di, z in enumerate(DECKS):
        hw = half_width(t_b, z) - 1.3
        if hw < 3.0:
            continue
        edges = [-hw] + [y for y in lines if -hw + 2.0 < y < hw - 2.0] + [hw]
        for a, c in zip(edges, edges[1:]):
            keep = any(dk == di and a <= yk <= c for dk, yk in access)
            gone = b.rng.random() < 0.08 or (shaft[2] <= di <= shaft[3] and a >= shaft[0] - 0.6 and c <= shaft[1] + 0.6)
            if any(dk == di and a <= yk <= c for dk, yk in omit) or (gone and not keep):
                continue
            out = 1.0 if keep else b.rng.uniform(-5.0, 2.0)
            x_end = xb + d * out
            ym, w = (a + c) / 2, c - a - 0.3
            b.box((abs(x_end - x_in), w, 0.35), ((x_end + x_in) / 2, ym, z), mat=PAINT, tint=INSIDE, vary=0.1)
            b.box((0.5, w, 0.7), (x_end, ym, z - 0.5), mat=PAINT, tint=DARK, solid=False)
            if not keep and b.rng.random() < 0.3:
                ang = math.radians(b.rng.uniform(25.0, 70.0))
                p0 = Vector((x_end, ym, z))
                p1 = p0 + Vector((d * math.cos(ang), 0.0, -math.sin(ang))) * b.rng.uniform(2.5, 6.0)
                b.beam(p0, p1, 0.3, w - 0.4, mat=PAINT, tint=INSIDE, vary=0.05)
            if di > 0 and b.rng.random() < 0.35:
                # A dead lamp fitting under the deck above, a red emergency light still glowing in some.
                b.box((1.2, 0.3, 0.15), (xb - d * b.rng.uniform(3.0, 18.0), ym, z - 0.3), mat=LAMP if b.rng.random()
                      < 0.3 else PAINT, tint=srgb(0.9, 0.15, 0.08), solid=False, vary=0)
    for z0, z1 in zip(DECKS, DECKS[1:]):
        for y in lines:
            if abs(y) > min(half_width(t_b, z0), half_width(t_b, z1)) - 2.5 or b.rng.random() < 0.35:
                continue
            if abs(y) > 10.0 and shaft[2] <= DECKS.index(z0) <= shaft[3] and y > 0:
                continue
            out = b.rng.uniform(-7.0, -0.5)
            x_end = xb + d * out
            b.box((abs(x_end - x_in), 0.3, z1 - z0 - 0.35), ((x_end + x_in) / 2, y, (z0 + z1) / 2), mat=PAINT,
                  tint=srgb(0.30, 0.31, 0.30), vary=0.08)
    # Ducts along the deckheads, two of them torn and hanging out.
    for k, (y, z) in enumerate(((-9.0, DECKS[2] - 1.1), (10.0, DECKS[4] - 1.1), (-12.0, DECKS[6] - 1.1),
                                (2.0, DECKS[7] - 1.1))):
        p0, p1 = (x_in, y, z), (xb + d * 0.5, y, z)
        pts = [p0, p1] if k % 2 else [p0, p1, (xb + d * 3.0, y + 1.0, z - 4.5)]
        b.tube(pts, 0.75, mat=PAINT, tint=srgb(0.45, 0.46, 0.44), sides=10)
    # The blown-out shaft: scorched walls round it, a crumpled deck hanging into it.
    zs0, zs1 = DECKS[shaft[2]], DECKS[shaft[3]]
    for y in shaft[:2]:
        b.box((14.0, 0.3, zs1 - zs0 + 3.2), (xb - d * 7.0, y, (zs0 + zs1) / 2 + 1.4), mat=PAINT, tint=BURNT,
              solid=False)
    b.beam((xb - d * 3.0, shaft[0] + 1.0, zs1 + 0.2), (xb + d * 2.0, shaft[1] - 2.0, zs0 + 2.0), 0.3, 6.0, mat=PAINT,
           tint=BURNT, roll=30.0)
    # The keel girder along her bottom, standing out of the break.
    b.box((30.0, 3.2, 2.6), (xb - d * 13.0, 0.0, KEEL + 1.9), mat=PAINT, tint=DARK)
    # Frames inside the torn plating, longerons along it, standing out of the break.
    frames = inset(ring(t_b), t_b, 1.0)
    P.ribs(b, frames, radius=0.45, inset=(1.5, 5.0, 8.5, 12.0), axis=(d, 0, 0), mat=RUST, gaps=0.12)
    for j in range(0, NR, 2):
        p = Vector(frames[j])
        a = p - Vector((d * 22.0, 0.0, 0.0))
        out = b.rng.uniform(-1.0, 7.0)
        if out > 2.0:
            mid = p + Vector((d * out * 0.55, 0.0, 0.0))
            tip = p + Vector((d * out, b.rng.uniform(-1.0, 1.0), -out * b.rng.uniform(0.2, 0.7)))
            b.tube([tuple(a), tuple(p), tuple(mid), tuple(tip)], 0.35, mat=PAINT, tint=DARK, sides=6)
        else:
            b.tube([tuple(a), tuple(p + Vector((d * out, 0.0, 0.0)))], 0.35, mat=PAINT, tint=DARK, sides=6)
    # Tongues of torn plating standing out past the break, flared out and narrowing to ragged ends.
    rim = ring(t_b)
    picks = sorted({b.rng.randrange(NR) for _ in range(11)})
    for j in picks:
        p0, p1 = Vector(rim[j]), Vector(rim[(j + 1) % NR])
        ln = b.rng.uniform(3.0, 9.0)
        rad = Vector((0.0, (p0.y + p1.y) / 2, (p0.z + p1.z) / 2 - zc)).normalized()
        flare = rad * ln * b.rng.uniform(0.1, 0.45)
        q0 = p0.lerp(p1, b.rng.uniform(0.1, 0.4)) + Vector((d * ln, 0.0, 0.0)) + flare
        q1 = p0.lerp(p1, b.rng.uniform(0.6, 0.9)) + Vector((d * ln * b.rng.uniform(0.5, 0.9), 0.0, 0.0)) + flare
        back = Vector((d * 2.0, 0.0, 0.0))
        tint = OLIVE if kind(j) in ("upper", "shoulder", "deck") else (GREY_HULL if kind(j) != "belly" else SHIELD)
        slab(b, (p0 - back, p1 - back, q1, q0), 0.6, (p0.x, 0.0, zc), tint, solid=False)
    # Cables and pipes hanging out of it.
    for k in range(16):
        z = b.rng.choice(DECKS[1:]) - 0.45
        hw = half_width(t_b, z) - 2.0
        y = b.rng.uniform(-hw, hw)
        x0 = xb + d * b.rng.uniform(-1.0, 1.5)
        cable(b, (x0, y, z), (x0 + d * b.rng.uniform(0.5, 2.5), y + b.rng.uniform(-1.5, 1.5),
                              z - b.rng.uniform(3.0, 11.0)), sag=0.08, radius=0.07)
    for k in range(7):
        z = b.rng.choice(DECKS[2:]) - 0.9
        hw = half_width(t_b, z) - 2.5
        y = b.rng.uniform(-hw, hw)
        p1 = (xb + d * b.rng.uniform(0.5, 2.5), y, z)
        p2 = (p1[0] + d * b.rng.uniform(1.0, 3.0), y + b.rng.uniform(-2.0, 2.0), z - b.rng.uniform(2.0, 6.0))
        b.tube([(xb - d * 10.0, y, z), p1, p2], b.rng.uniform(0.25, 0.45), mat=PAINT,
               tint=b.rng.choice((GREY, RUSTY, DARK)), sides=8)


def citadel(b, x0, x1, width=22.0, rake=(0.0, 8.0), tint=OLIVE):
    """The armoured citadel on the spine from x0 to x1: CITADEL high over the deck, chamfered along its top,
    following the deck as it falls toward the prow; `rake` slopes its back and front faces back by so much."""
    n = max(2, int(abs(x1 - x0) / 10.0))
    rings = []
    for k in range(n + 1):
        x = x0 + (x1 - x0) * k / n
        t = t_of(x)
        hw = min(width / 2, deck_edge(t) - 1.5)
        zb = deck_z(t, hw) - 0.5
        zt = deck_z(t, 0.0) + CITADEL
        dx = rake[0] if k == 0 else (-rake[1] if k == n else 0.0)
        rings.append([(x, -hw, zb), (x, hw, zb), (x + dx, hw, zt - 1.8), (x + dx, hw - 2.2, zt),
                      (x + dx, -hw + 2.2, zt), (x + dx, -hw, zt - 1.8)])
    m = 6
    verts = [p for r in rings for p in r]
    faces = [list(range(m)), [len(rings) * m - m + q for q in range(m)][::-1]]
    for i in range(len(rings) - 1):
        for q in range(m):
            a, c = i * m + q, i * m + (q + 1) % m
            faces.append([a, c, c + m, a + m])
    b.mesh(verts, faces, mat=HULL, tint=tint, solid="hull", smooth=False)
    # Armour bands across it, every 12 m.
    k = 1
    while abs(12.0 * k) < abs(x1 - x0) - 4.0:
        x = x0 + math.copysign(12.0 * k, x1 - x0)
        t = t_of(x)
        hw = min(width / 2, deck_edge(t) - 1.5)
        zt = deck_z(t, 0.0) + CITADEL
        zb = deck_z(t, hw) - 0.5
        band = [(-hw - 0.35, zb), (hw + 0.35, zb), (hw + 0.35, zt - 1.8), (hw - 2.1, zt + 0.35),
                (-hw + 2.1, zt + 0.35), (-hw - 0.35, zt - 1.8)]
        b.extrude([(y, z) for y, z in band], 1.3, (x, 0, 0), (0, 0, 90), mat=HULL, tint=HULL_DARK, solid=False)
        k += 1
    # Its top edge bars, access hatches.
    for k in range(n):
        xa, xc = x0 + (x1 - x0) * k / n, x0 + (x1 - x0) * (k + 1) / n
        if k % 2 == 0:
            xm = (xa + xc) / 2
            t = t_of(xm)
            zt = deck_z(t, 0.0) + CITADEL
            hw = min(width / 2, deck_edge(t) - 1.5) - 3.6
            b.box((2.2, 1.6, 0.2), (xm, hw * (1 if k % 4 else -1), zt + 0.08), mat=PAINT, tint=HULL_DARK, solid=False)
            b.box((2.4, 1.8, 0.12), (xm, hw * (1 if k % 4 else -1), zt + 0.04), mat=PAINT, tint=YELLOW, solid=False,
                  vary=0)


def tower(b, x0, z0):
    """The command tower on the citadel: an armoured base and a second tier with sloped, chamfered faces, the bridge
    behind a band of windows under its visor, a sensor deck, the mast with its yards and a beacon, two radomes,
    four array faces, bridge wings with rails."""
    faceted(b, (x0, 0, z0 - 1.9), (42.0, 24.0), (36.0, 20.0), 11.4, (5.0, 4.0), (-2.0, 0.0), tint=OLIVE)
    z1 = z0 + 9.5
    faceted(b, (x0 - 2.5, 0, z1 - 0.2), (31.0, 19.0), (26.0, 16.5), 7.8, (4.0, 3.2), (-2.0, 0.0), tint=OLIVE_DEEP)
    z2 = z1 + 7.5
    bx, bl, bw, bh = x0 - 4.0, (24.0, 18.0), (19.0, 14.5), 6.6
    faceted(b, (bx, 0, z2 - 0.2), bl, bw, bh, (3.4, 2.7), (-1.5, 0.0), tint=GREY_HULL)

    def at_h(h):
        f = h / bh
        return (bl[0] + (bw[0] - bl[0]) * f + 0.35, bl[1] + (bw[1] - bl[1]) * f + 0.35), 3.4 - 0.7 * f, -1.5 * f
    (s0, c0, d0), (s1, c1, d1) = at_h(2.4), at_h(4.4)
    faceted(b, (bx + d0, 0, z2 - 0.2 + 2.4), s0, s1, 2.0, (c0, c1), (d1 - d0, 0.0), mat=GLASS, solid=False)
    b.box((3.8, bl[1] - 1.2, 0.4), (bx + bl[0] / 2 - 0.8, 0, z2 + 4.8), (0, 16.0, 0), mat=HULL, tint=HULL_DARK,
          solid=False)
    for s in (-1, 1):
        y = s * (bl[1] / 2 + 1.8)
        b.box((4.6, 3.8, 0.3), (bx + 4.5, y, z2 - 0.05), mat=GRATE, tint=GREY)
        b.box((4.6, 3.8, 0.25), (bx + 4.5, y, z2 - 0.35), mat=PAINT, tint=HULL_DARK, solid=False)
        railing(b, (bx + 2.2, y + s * 1.8, z2 + 0.1), (bx + 6.8, y + s * 1.8, z2 + 0.1), tint=GREY)
        railing(b, (bx + 6.8, y + s * 1.8, z2 + 0.1), (bx + 6.8, y - s * 1.6, z2 + 0.1), tint=GREY)
        b.box((0.35, 0.35, 0.35), (bx + 6.8, y + s * 1.8, z2 + 1.4), mat=LAMP,
              tint=srgb(0.2, 1.0, 0.3) if s < 0 else srgb(1.0, 0.15, 0.1), solid=False, vary=0)
    # Array faces on the second tier's chamfered corners.
    for sx, sy in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        yaw = math.degrees(math.atan2(sy, sx))
        px = x0 - 2.5 - 1.0 + sx * (31.0 / 2 - 2.0 - 1.1)
        py = sy * (19.0 / 2 - 2.0 - 0.6)
        b.cyl(2.9, 0.4, (px + sx * 0.6, py + sy * 0.6, z1 + 3.9), (0, 90 - 22.0, yaw), mat=PANEL, tint=GREY_LIGHT,
              sides=8, solid=False)
    z3 = z2 + bh - 0.2
    faceted(b, (x0 - 6.5, 0, z3 - 0.1), (11.0, 10.0), (9.0, 8.5), 3.8, (2.0, 1.6), (-0.5, 0.0), tint=OLIVE)
    for s in (-1, 1):
        b.cyl(1.2, 1.6, (x0 - 10.0, s * 5.6, z3 + 0.7), mat=PAINT, tint=HULL_DARK, sides=12)
        b.lathe([(3.1, 0.0), (3.0, 1.1), (2.4, 2.2), (1.3, 2.9), (0.0, 3.1)], (x0 - 10.0, s * 5.6, z3 + 1.5),
                mat=PANEL, tint=LIGHTGREY, sides=20)
    z4 = z3 + 3.6
    mx = x0 - 7.0
    b.cyl(1.0, 21.0, (mx, 0, z4 + 10.5), radius2=0.35, mat=PAINT, tint=GREY, sides=10)
    b.box((0.35, 11.0, 0.35), (mx, 0, z4 + 11.0), mat=PAINT, tint=GREY, solid=False)
    b.box((0.3, 6.5, 0.3), (mx, 0, z4 + 16.0), mat=PAINT, tint=GREY, solid=False)
    for dy in (-5.0, -2.8, 2.8, 5.0):
        b.cyl(0.06, 3.0, (mx, dy, z4 + 12.4), mat=PAINT, tint=GREY, sides=5, solid=False)
    for a in range(4):
        r = math.radians(45 + 90 * a)
        b.box((2.0, 0.18, 2.4), (mx + math.cos(r) * 1.2, math.sin(r) * 1.2, z4 + 6.0), (6, 0, math.degrees(r) + 90),
              mat=PANEL, tint=GREY_LIGHT, solid=False)
    dish(b, (mx + 1.9, 0, z4 + 3.6), (0, -65, 0), 1.9)
    P.beacon(b, (mx, 0, z4 + 21.1), 0.6)
    # Whip antennas and a small dish along the base's roof.
    for k in range(5):
        b.cyl(0.07, 6.0 + k % 2 * 2.5, (x0 - 16.0 + k * 2.0, 9.0, z1 + 3.5), mat=PAINT, tint=GREY, sides=5,
              solid=False)
    dish(b, (x0 + 12.0, -7.8, z1 + 0.9), (0, -50, -30), 2.2)


def turret(b, at, yaw=0.0, elev=3.0, barbette=2.0, wrecked=False, tint=OLIVE):
    """A twin gun turret, its frame's +X the barrels' way and z = 0 on the deck: an armoured barbette ring, a low
    faceted house with a sloped glacis, two long barrels in a mantlet, sight hoods and a hatch on its roof.
    `wrecked`: torn off its barbette, burnt, one barrel bent and one snapped."""
    with b.frame(at, (0, 0, yaw)):
        if barbette:
            b.cyl(7.0, barbette, (0, 0, barbette / 2), mat=HULL, tint=GREY_HULL, sides=28)
            b.cyl(7.25, 0.45, (0, 0, barbette - 0.3), mat=PAINT, tint=HULL_DARK, sides=28, solid=False)
        z = barbette
        if wrecked:
            b.cyl(6.0, 1.2, (0, 0, z - 0.4), mat=RUST, tint=(1, 1, 1), sides=24)
        faceted(b, (0.6, 0, z), (17.0, 13.0), (12.5, 10.4), 4.6, (3.2, 2.6), (-1.8, 0.0),
                tint=BURNT if wrecked else tint)
        b.box((2.4, 7.4, 3.0), (7.6, 0, z + 2.2), (0, -14, 0), mat=HULL, tint=HULL_DARK, bevel=0.15)
        for k, y in enumerate((-1.9, 1.9)):
            with b.frame((8.4, y, z + 2.3), (0, -elev, 0)):
                b.cyl(0.95, 2.6, (1.0, 0, 0), (0, 90, 0), mat=PAINT, tint=HULL_DARK, sides=14)
                if wrecked and k == 1:
                    b.cyl(0.6, 6.5, (5.2, 0, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=12)
                    continue
                if wrecked:
                    b.cyl(0.6, 11.0, (7.5, 0, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=12, radius2=0.55)
                    with b.frame((13.0, 0, 0), (0, 0, 24.0)):
                        b.cyl(0.55, 11.0, (5.5, 0, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=12, radius2=0.5)
                    continue
                b.cyl(0.62, 22.0, (13.0, 0, 0), (0, 90, 0), mat=PAINT, tint=DARK, sides=14, radius2=0.5)
                b.cyl(0.78, 1.8, (24.6, 0, 0), (0, 90, 0), mat=PAINT, tint=BLACK, sides=14)
        roof = z + 4.6
        for s in (-1, 1):
            b.box((3.0, 1.8, 1.2), (-3.0, s * 3.4, roof + 0.55), mat=HULL, tint=GREY_HULL, bevel=0.15)
            b.box((0.1, 1.4, 0.4), (-1.45, s * 3.4, roof + 0.7), mat=GLASS, solid=False, vary=0)
        b.cyl(0.6, 0.3, (-5.0, 0, roof + 0.1), mat=PAINT, tint=HULL_DARK, sides=12, solid=False)
        b.box((2.8, 6.0, 0.5), (-7.6, 0, z + 2.0), mat=PAINT, tint=BLACK, solid=False, vary=0)


def cells(b, x0, x1, z0, z1, w, rows=4, seed=1):
    """Vertical launch cells on a raised armoured plate following the deck from (x0, z0) to (x1, z1): a grid of
    hatches, a few blown open with their lids thrown back."""
    ang = math.degrees(math.atan2(z0 - z1, x1 - x0))
    length = math.hypot(x1 - x0, z1 - z0)
    with b.frame(((x0 + x1) / 2, 0, (z0 + z1) / 2), (0, ang, 0)):
        b.box((length + 1.2, w + 1.2, 1.0), (0, 0, 0.3), mat=HULL, tint=HULL_DARK, bevel=0.1)
        cols = int(length / 2.6)
        for i in range(cols):
            for j in range(rows):
                x = -length / 2 + 1.3 + i * 2.6
                y = -w / 2 + w * (j + 0.5) / rows
                cw = w / rows - 0.45
                if P._hash(i, j, 5, seed) < 0.13:
                    b.box((2.1, cw, 0.05), (x, y, 0.82), mat=MATTE, tint=VOID, solid=False, vary=0)
                    with b.frame((x - 1.05, y, 0.85), (0, -100.0 - 30.0 * P._hash(i, j, 6, seed), 0)):
                        b.box((2.1, cw, 0.14), (1.05, 0, 0), mat=HULL, tint=BURNT, solid=False)
                else:
                    b.box((2.2, cw, 0.16), (x, y, 0.85), mat=HULL, tint=BELT_T, solid=False, vary=0.08)


def pd_mount(b, t, j):
    """A point-defence mount on the shoulder plating: an armoured base, a domed head, a rotary cannon."""
    at, rot = face_frame(t, j, 0.35)
    with b.frame(at, rot):
        with b.frame((0, 0, 0), (90, 0, 0)):
            b.cyl(1.7, 0.9, (0, 0, 0.35), mat=HULL, tint=GREY_HULL, sides=14)
            b.lathe([(1.5, 0.0), (1.45, 0.7), (1.1, 1.4), (0.5, 1.75), (0.0, 1.8)], (0, 0, 0.8), mat=HULL,
                    tint=GREY_LIGHT, sides=14)
            b.cyl(0.32, 3.6, (0.0, 2.0, 1.6), (-75, 0, 0), mat=PAINT, tint=DARK, sides=8)


def ports(b, x0, x1, every=11.0, skip=()):
    """A row of armoured port covers along both upper flanks, some blown off (dark)."""
    k = 0
    x = x0
    while x < x1:
        if not any(a <= x <= c for a, c in skip):
            for j in (7, 14):
                at, rot = face_frame(t_of(x), j, 0.45)
                with b.frame(at, rot):
                    gone = P._hash(k, j, 3, 41) < 0.15
                    b.box((2.6, 0.3, 1.5), (0, -0.1, 0), mat=PAINT if gone else HULL, tint=VOID if gone else HULL_DARK,
                          solid=False, vary=0.04)
        x += every
        k += 1


def rcs(b, t, j, f=0.5):
    """A reaction-control block on the plating: an armoured housing with three nozzles."""
    at, rot = face_frame(t, j, f)
    with b.frame(at, rot):
        b.box((3.0, 1.6, 2.2), (0, -0.5, 0), mat=HULL, tint=HULL_DARK, bevel=0.12)
        for dx, dz in ((-0.75, -0.3), (0.75, -0.3), (0.0, 0.55)):
            b.lathe([(0.2, 0.0), (0.33, 0.35), (0.46, 0.7), (0.38, 0.7), (0.26, 0.35), (0.13, 0.0), (0.2, 0.0)],
                    (dx, -1.3, dz), (90, 0, 0), mat=HULL, tint=BURNT, sides=10, close=False, solid=False)


def dish(b, at, rot, r, tint=LIGHTGREY):
    """A small dish as a closed thin shell (it shows from behind too)."""
    b.lathe([(0.0, 0.0), (r * 0.55, r * 0.14), (r, r * 0.42), (r, r * 0.5), (r * 0.55, r * 0.24), (0.0, r * 0.12)],
            at, rot, mat=PANEL, tint=tint, sides=16, close=False, solid=False)


def stencil_on(b, t, j, text, height, f=0.18, tint=MARK):
    """Her hull code painted on the plating of strip j, centred on station t."""
    at, rot = face_frame(t, j, f)
    with b.frame(at, rot):
        w = len(text) * 0.86 * height - 0.3 * height
        P.stencil(b, text, (-w / 2, -0.12, 0.0), (0, 0, 0), height, tint=tint, depth=0.1, wear=0.7)


# ------------------------------------------------------------------------------------------------ the bow half


def bow(b):
    """The bow half: plating from the break to the prow, the break's open decks, the citadel, the tower, two
    turrets, the missile cells, the hangar, point-defence mounts, the hull code and the hatch."""
    at, rot, pivot = bow_place()
    seed = 7
    xs = ([x_of(BREAK), -22.0, -14.0, -5.33, 3.33, 12.0] + [12.0 + 9.0 * k for k in range(1, 20)]
          + [190.0, 196.0, 200.0, x_of(T_PROW)])
    ts = [t_of(x) for x in xs]
    ts[0], ts[-1] = BREAK, T_PROW
    pull = [5.5 * (0.08 + 0.92 * P._hash(j, 1, 2, seed) ** 2.0) for j in range(NR)]
    rings = [ring(t, pull if i == 0 else None) for i, t in enumerate(ts)]
    hangar = (2, 5)

    def holes(i, j):
        r = P._hash(i, j, 11, seed)
        if i == 0:
            return r < 0.42
        if i == 1:
            return r < 0.2
        if i == 2 and r < 0.07:
            return True
        if hangar[0] <= i < hangar[1] and j in (6, 7):
            return True
        return r < 0.01 and kind(j) in ("upper", "shoulder")

    with b.frame(at, rot):
        with b.frame(tuple(-c for c in pivot)):
            plating(b, ts, rings, seed, holes)
            # The prow: a dark armoured cap running on from the last ring to the knife edge.
            r = ring(T_PROW)
            _, zk, zs = shape(T_PROW)
            tip = (x_of(T_PROW) + 7.0, 0.0, zk + (zs - zk) * 0.42)
            b.mesh([tip] + r, [[0, j + 1, (j + 1) % NR + 1] for j in range(NR)] + [list(range(1, NR + 1))[::-1]],
                   mat=HULL, tint=BURNT, solid="hull", smooth=False)
            belts(b, ts, end=BREAK)
            edge_bars(b, ts, skip=lambda i: i == 0)
            break_end(b, BREAK, -1, access=((0, -8.4), (2, -8.4)))
            citadel(b, x_of(BREAK) + 3.0, 66.0, rake=(2.0, 9.0))
            ztop = SPINE + CITADEL
            tower(b, 4.0, ztop)
            turret(b, (48.0, 0, ztop), 0.0, elev=4.0, barbette=1.8)
            t1 = t_of(84.0)
            turret(b, (84.0, 0, deck_z(t1)), 28.0, elev=9.0, barbette=2.0)
            cells(b, 102.0, 128.0, deck_z(t_of(102.0)) - 0.2, deck_z(t_of(128.0)) - 0.2, 9.0, rows=4, seed=seed)
            hangar_bay(b, xs[hangar[0]], xs[hangar[1]])
            for x in (20.0, 66.0, 112.0):
                t = t_of(x)
                pd_mount(b, t, 8)
                pd_mount(b, t, 13)
            stencil_on(b, t_of(36.0), 6, CODE, 6.5)
            stencil_on(b, t_of(36.0), 15, CODE, 6.5)
            ports(b, -20.0, 150.0, skip=((-16.0, 14.0), (24.0, 50.0)))
            for x in (172.0, 180.0):
                rcs(b, t_of(x), 7, 0.5)
                rcs(b, t_of(x), 14, 0.5)
            # Recognition bands round the forward hull, light paint over the upper plating.
            for x in (118.0, 122.0):
                t = t_of(x)
                for j in (6, 7, 8, 13, 14, 15):
                    r0, r1 = ring(t - 0.8 / L, grow=0.05), ring(t + 0.8 / L, grow=0.05)
                    q = (r0[j], r0[(j + 1) % NR], r1[(j + 1) % NR], r1[j])
                    mid = sum((Vector(p) for p in q), Vector()) / 4
                    _, zk2, zs2 = shape(t)
                    slab(b, q, 0.2, (mid.x, 0.0, (zk2 + zs2) / 2), srgb(0.68, 0.69, 0.64), mat=PAINT,
                         solid=False)
            hatch(b)
            # The break's deck gets the bow's loot: ship parts in its compartments.
            b.marker("loot", (x_of(BREAK) + 8.0, -8.4, DECKS[0] + 0.25), table="crate", family="ship")
            b.marker("loot", (x_of(BREAK) + 14.0, -8.4, DECKS[2] + 0.25), table="military-crate", family="ship")


def hangar_bay(b, x0, x1):
    """The hangar behind the south flank: its door frame in the plating, the upper leaf still shut and buckled, the
    lower one hanging open; inside, two decks high, a launch on its skids and crates in the dark."""
    t = t_of((x0 + x1) / 2)
    sill, head = 2.8, 13.6
    # Inside: floor, ceiling, back and end walls (slabs, seen from within).
    hw_f, hw_c = half_width(t, sill) - 0.9, half_width(t, head) - 0.9
    b.box((x1 - x0, hw_f - 6.0, 0.4), ((x0 + x1) / 2, -(6.0 + hw_f) / 2, sill - 0.2), mat=PAINT, tint=INSIDE)
    b.box((x1 - x0, hw_c - 6.0, 0.4), ((x0 + x1) / 2, -(6.0 + hw_c) / 2, head + 0.2), mat=PAINT, tint=HULL_DARK,
          solid=False)
    b.box((x1 - x0, 0.4, head - sill), ((x0 + x1) / 2, -6.0, (sill + head) / 2), mat=PAINT, tint=srgb(0.2, 0.21, 0.2))
    for x in (x0, x1):
        b.box((0.4, hw_f - 6.0, head - sill), (x, -(6.0 + hw_f) / 2, (sill + head) / 2), mat=PAINT,
              tint=srgb(0.26, 0.27, 0.26))
    # A launch on its skids, crates.
    with b.frame(((x0 + x1) / 2 - 2.0, -15.0, sill), (0, 0, 8)):
        b.lathe([(0.0, 0.0), (1.2, 1.0), (2.0, 3.0), (2.2, 7.0), (1.9, 10.5), (1.2, 12.0), (0.0, 12.4)],
                (-6.0, 0, 2.9), (0, 90, 0), mat=PAINT, tint=GREY_LIGHT, sides=14)
        for s in (-1, 1):
            b.box((5.0, 4.5, 0.4), (-1.0, s * 3.6, 2.6), (0, 0, s * 8), mat=PAINT, tint=GREY_HULL)
            b.box((8.0, 0.2, 0.2), (0.0, s * 1.6, 0.4), mat=PAINT, tint=DARK, solid=False)
        b.box((1.6, 1.4, 0.8), (5.0, 0, 3.7), (0, 12, 0), mat=GLASS, solid=False)
    for k in range(3):
        P.crates(b, (x0 + 4.0 + 6.5 * k, -8.5, sill), (0, 0, b.rng.uniform(-10, 10)), n=5, tint=OLIVE_DEEP)
    b.marker("loot", (x1 - 5.0, -10.0, sill + 0.1), table="military-crate", family="ship")
    # The door frame round the opening in the plating, the leaves.
    r0, r1 = ring(t_of(x0)), ring(t_of(x1))
    for j in (6, 8):
        b.beam(r0[j], r1[j], 0.9, 0.9, mat=HULL, tint=HULL_DARK)
    for r in (r0, r1):
        b.beam(r[6], r[7], 0.9, 0.9, mat=HULL, tint=HULL_DARK)
        b.beam(r[7], r[8], 0.9, 0.9, mat=HULL, tint=HULL_DARK)
    at, rot = face_frame(t, 6, 0.0)
    height = (Vector(r0[8]) - Vector(r0[6])).length
    with b.frame(at, rot):
        b.box((x1 - x0 - 1.4, 0.5, height * 0.42), (0.6, 0.05, height * 0.78), (0, 4, 0), mat=HULL, tint=GREY_HULL)
        for k in range(6):
            b.box(((x1 - x0) / 6 - 0.2, 0.55, 0.9), (-(x1 - x0) / 2 + (k + 0.5) * (x1 - x0) / 6 + 0.6, 0.05,
                                                    height * 0.57), (0, 4, 0), mat=PAINT,
                  tint=YELLOW if k % 2 else BLACK, solid=False, vary=0)
        with b.frame((0, 0, 0.3), (-62.0, 0, 0)):
            b.box((x1 - x0 - 3.0, 0.45, height * 0.5), (0.8, 0, height * 0.25), (0, 0, 3.0), mat=HULL,
                  tint=OLIVE_DEEP)


def hatch(b):
    """A personnel hatch in a housing on the starboard belt, low where the hull meets the ground: walls round a
    1.3 by 2.3 m door, the leaf swung open, a lamp over it, a code plate. The interior begins here."""
    t, _, _ = hatch_site()
    at, rot = face_frame(t, 5, 0.08)
    with b.frame(at, rot):
        w, depth, h = 4.4, 2.6, 3.6
        b.box((w, depth + 0.6, 0.3), (0, -depth / 2 + 0.3, 0.15), mat=PAINT, tint=HULL_DARK)
        b.box((w + 0.4, depth + 1.0, 0.4), (0, -depth / 2 + 0.1, h + 0.2), mat=HULL, tint=OLIVE)
        for s in (-1, 1):
            b.box((0.3, depth + 0.6, h), (s * (w / 2 - 0.15), -depth / 2 + 0.3, h / 2), mat=HULL, tint=OLIVE)
        side = (w - 1.3) / 2
        for s in (-1, 1):
            b.box((side, 0.3, h), (s * (0.65 + side / 2), -depth, h / 2), mat=HULL, tint=OLIVE)
        b.box((1.3, 0.3, h - 2.3), (0, -depth, 2.3 + (h - 2.3) / 2), mat=HULL, tint=OLIVE)
        b.box((w - 0.6, 0.1, h - 0.2), (0, 0.25, h / 2), mat=MATTE, tint=VOID, solid=False, vary=0)
        with b.frame((0.65, -depth - 0.15, 0.3), (0, 0, -105.0)):
            b.box((1.3, 0.12, 2.25), (-0.65, 0, 1.12), mat=PAINT, tint=GREY_HULL, bevel=0.02)
            b.cyl(0.18, 0.08, (-0.65, -0.1, 1.2), (90, 0, 0), mat=PAINT, tint=DARK, sides=10, solid=False)
        for s in (-1, 1):
            for k in range(5):
                b.box((0.18, 0.34, 0.42), (s * 0.78, -depth - 0.02, 0.3 + 0.21 + k * 0.42), mat=PAINT,
                      tint=YELLOW if k % 2 == 0 else BLACK, solid=False, vary=0)
        lamp(b, (0, -depth - 0.35, 2.95), (-20, 0, 0), size=(0.42, 0.22, 0.14))
        P.plate(b, (1.7, -depth - 0.18, 1.8), (0, 0, 0), 0.9, 0.5, tint=YELLOW, text="07")
        b.marker("door", (0, -depth - 1.0, 0.35), kind_of="hatch")


# ------------------------------------------------------------------------------------------------ the stern half


def stern(b):
    """The stern half: plating from the stern face to the break, the drive (its collar, the stern bulkhead, three
    great bells and four small ones in their thrust frame), radiator fins on the high flank, the citadel with the
    empty barbette, missile cells and the aft control post, the break's open decks."""
    at, rot, pivot = stern_place()
    seed = 13
    xs = [-210.0 + 9.0 * k for k in range(20)] + [x_of(BREAK)]
    ts = [t_of(x) for x in xs]
    ts[0], ts[-1] = 0.0, BREAK
    pull = [-5.0 * (0.08 + 0.92 * P._hash(j, 2, 2, seed) ** 2.0) for j in range(NR)]
    rings = [ring(t, pull if i == len(ts) - 1 else None) for i, t in enumerate(ts)]
    last = len(ts) - 2

    def holes(i, j):
        r = P._hash(i, j, 11, seed)
        near = last - i
        if near == 0:
            return r < 0.42
        if near == 1:
            return r < 0.2
        if near == 2 and r < 0.07:
            return True
        return r < 0.01 and kind(j) in ("upper", "shoulder")

    with b.frame(at, rot):
        with b.frame(tuple(-c for c in pivot)):
            plating(b, ts, rings, seed, holes)
            belts(b, ts, end=BREAK)
            edge_bars(b, ts, skip=lambda i: i >= last)
            drive(b)
            for x in (-166.0, -149.0, -132.0):
                fin(b, x, -1 if STERN_AT[1][0] < 0 else 1)
            break_end(b, BREAK, 1, access=((0, 0.0), (3, 0.0)), omit=((1, 0.0), (2, 0.0)))
            # The drive core laid open behind the break: a drum along her keel, its seams still glowing.
            xb = x_of(BREAK)
            b.cyl(4.2, 18.0, (xb - 15.0, 0, -10.5), (0, 90, 0), mat=PAINT, tint=srgb(0.34, 0.36, 0.38), sides=24)
            for dx in (-6.0, 0.0, 6.0):
                b.cyl(4.28, 0.5, (xb - 15.0 + dx, 0, -10.5), (0, 90, 0), mat=LAMP, tint=srgb(0.5, 0.9, 0.5), sides=24,
                      cap=False, solid=False, vary=0)
            b.cyl(2.6, 0.6, (xb - 5.8, 0, -10.5), (0, 90, 0), mat=PAINT, tint=YELLOW, sides=20, solid=False)
            b.marker("light", (xb - 8.0, 0, -10.5), colour=[0.5, 1.0, 0.5], range=14.0)
            citadel(b, -176.0, x_of(BREAK) - 3.0, rake=(6.0, -2.0))
            ztop = SPINE + CITADEL
            barbette(b, (-96.0, 0, ztop))
            cells(b, -154.0, -126.0, ztop - 0.2, ztop - 0.2, 10.0, rows=4, seed=seed)
            # The aft control post: two faceted tiers, a band of windows, a lattice mast, a dish.
            faceted(b, (-62.0, 0, ztop - 1.0), (22.0, 16.0), (18.0, 13.0), 7.0, (3.0, 2.4), (-1.0, 0.0), tint=OLIVE)
            faceted(b, (-64.0, 0, ztop + 5.8), (12.0, 10.0), (9.5, 8.5), 4.5, (1.8, 1.5), (-0.6, 0.0),
                    tint=OLIVE_DEEP)
            b.box((0.1, 8.0, 1.2), (-58.4, 0, ztop + 7.6), (0, 16, 0), mat=GLASS, solid=False, vary=0)
            lattice_tower(b, (-66.0, 0, ztop + 10.2), 16.0, 2.0, 0.7, sections=5, member=0.14, tint=GREY)
            P.beacon(b, (-66.0, 0, ztop + 26.4), 0.5)
            dish(b, (-60.0, 3.8, ztop + 10.8), (0, -55, 40), 3.0)
            for k in range(4):
                b.cyl(0.06, 6.0 + 2.0 * (k % 2), (-120.0 + k * 9.0, 8.5, ztop + 3.0), mat=PAINT, tint=GREY,
                      sides=5, solid=False)
            for x in (-182.0, -120.0, -78.0):
                pd_mount(b, t_of(x), 8)
                pd_mount(b, t_of(x), 13)
            stencil_on(b, t_of(-150.0), 6, CODE, 6.5)
            ports(b, -180.0, -40.0, skip=((-160.0, -138.0),))
            for x in (-200.0, -190.0):
                rcs(b, t_of(x), 6, 0.5)
                rcs(b, t_of(x), 15, 0.5)
            b.marker("loot", (xb - 9.0, 0.0, DECKS[3] + 0.25), table="crate", family="ship")
            b.marker("loot", (xb - 4.0, 0.0, DECKS[0] + 0.25), table="sealed-crate", family="ship")
            b.zone("radiation", (xb - 15.0, 0.0, -10.5), radius=40.0, level=2, source="drive")


def drive(b):
    """The drive: an armoured collar round the hull where her run narrows to the stern, the stern bulkhead, three
    great bells in a row (double-skinned, so their throats show inside) and four small ones, rings and gimbal rams
    round the great bells' throats; two radiator fins splayed up from the deck over it."""
    t0, t1 = t_of(-196.0), t_of(-185.0)
    r0, r1 = ring(t0, grow=1.0), ring(t1, grow=1.0)
    for j in range(NR):
        q = (r0[j], r0[(j + 1) % NR], r1[(j + 1) % NR], r1[j])
        mid = sum((Vector(p) for p in q), Vector()) / 4
        slab(b, q, 1.4, (mid.x, 0.0, 0.0), BELT_T if j % 2 else HULL_DARK)
    xs = x_of(0.0)
    face = inset(ring(0.0), 0.0, 0.6)
    verts = [(xs + 0.4, y, z) for _, y, z in face] + [(xs + 1.6, y, z) for _, y, z in face]
    faces = [list(range(NR)), list(range(NR, 2 * NR))[::-1]] + [[k, (k + 1) % NR, NR + (k + 1) % NR, NR + k]
                                                                 for k in range(NR)]
    b.mesh(verts, faces, mat=HULL, tint=HULL_DARK, solid="hull", smooth=False)
    great = ((-14.0, 3.0, 7.0), (0.0, 3.0, 7.0), (14.0, 3.0, 7.0))
    small = ((-7.0, 12.0, 2.3), (7.0, 12.0, 2.3), (-7.0, -6.2, 2.1), (7.0, -6.2, 2.1))
    for y, z, r in great + small:
        ln = r * 1.7
        rt = r * 0.42
        prof = [(rt, 0.0), (rt * 1.08, ln * 0.12), (r * 0.66, ln * 0.45), (r * 0.9, ln * 0.82), (r, ln),
                (r - 0.35, ln), (r * 0.9 - 0.35, ln * 0.82), (r * 0.66 - 0.35, ln * 0.45), (rt * 1.08 - 0.35, ln * 0.12),
                (rt - 0.35, 0.0), (rt, 0.0)]
        b.lathe(prof, (xs + 0.5, y, z), (0, -90, 0), mat=HULL, tint=srgb(0.17, 0.16, 0.155), sides=32 if r > 5 else 18,
                close=False, solid="hull", wear=0.95)
        b.cyl(r * 1.005, 0.9, (xs + 0.5 - r * 1.7 + 0.45, y, z), (0, 90, 0), mat=RUST, tint=(1, 1, 1),
              sides=32 if r > 5 else 18, cap=False, solid=False)
        b.cyl(rt * 0.98, 0.6, (xs + 0.3, y, z), (0, 90, 0), mat=MATTE, tint=VOID, sides=24, solid=False, vary=0)
    for y, z, r in great:
        rt = r * 0.42
        loop = [(xs - 1.2, y + math.cos(a) * rt * 1.35, z + math.sin(a) * rt * 1.35)
                for a in [2 * math.pi * k / 16 for k in range(17)]]
        b.tube(loop, 0.4, mat=PAINT, tint=HULL_DARK, sides=6, cap=False)
        for k in range(4):
            a = math.radians(45 + 90 * k)
            b.tube([(xs + 0.6, y + math.cos(a) * rt * 2.1, z + math.sin(a) * rt * 2.1),
                    (xs - 3.4, y + math.cos(a) * rt * 1.45, z + math.sin(a) * rt * 1.45)], 0.32, mat=PAINT,
                   tint=GREY, sides=8)
    # Radiator fins splayed up from the deck over the drive.
    zd = deck_z(t_of(-186.0))
    for sg in (-1, 1):
        sail(b, (-186.0, sg * 5.0, zd - 0.6), (-sg * 17.0, 0, 0), seed=sg + 5)


def sail(b, at, rot, root=36.0, tip=13.0, height=25.0, sweep=15.0, seed=1):
    """A swept radiator fin standing on its root (its frame's +X toward the prow): a spar, dark panels between ribs
    (a few gone), a frame along its leading edge, tip and trailing edge."""
    lead = (root / 2, 0.0), (root / 2 - sweep, height)
    trail = (-root / 2, 0.0), (root / 2 - sweep - tip, height)

    def top(x):
        z = height
        if x > lead[1][0]:
            z = min(z, (lead[0][0] - x) / sweep * height)
        if x < trail[1][0]:
            z = min(z, (x - trail[0][0]) / (trail[1][0] - trail[0][0]) * height)
        return max(0.0, z)
    with b.frame(at, rot):
        b.box((root + 2.0, 2.6, 3.2), (0, 0, 1.0), mat=HULL, tint=HULL_DARK, bevel=0.1)
        ribs = 8
        xs = [-root / 2 + root * k / ribs for k in range(ribs + 1)]
        for k, (xa, xc) in enumerate(zip(xs, xs[1:])):
            for q in range(3):
                z0, z1 = 2.6 + q * (height - 2.6) / 3, 2.6 + (q + 1) * (height - 2.6) / 3
                za, zc = min(z1, top(xa)), min(z1, top(xc))
                if min(za, zc) - z0 < 1.0 or P._hash(k, q, seed, 17) < 0.12:
                    continue
                b.extrude([(xa + 0.4, z0 + 0.3), (xc - 0.4, z0 + 0.3), (xc - 0.4, zc - 0.3), (xa + 0.4, za - 0.3)],
                          0.45, (0, 0, 0), (0, 0, 0), mat=PANEL, tint=srgb(0.15, 0.16, 0.17), solid=False)
        for x in xs[1:-1]:
            h = top(x)
            if h > 2.0:
                b.box((0.8, 1.0, h - 1.0), (x, 0, 1.0 + (h - 1.0) / 2), mat=PAINT, tint=GREY_HULL, solid=False)
        b.beam((lead[0][0], 0, 1.0), (lead[1][0], 0, height), 1.0, 1.1, mat=PAINT, tint=GREY_HULL)
        b.beam((lead[1][0], 0, height), (trail[1][0], 0, height), 1.0, 1.1, mat=PAINT, tint=GREY_HULL)
        b.beam((trail[1][0], 0, height), (trail[0][0], 0, 1.0), 1.0, 1.1, mat=PAINT, tint=GREY_HULL)
        b.collide_box(Vector((0, 0, height / 2)), (root * 0.8, 1.0, height))


def fin(b, x, s):
    """A radiator fin standing out of the chine on side s (+1 port): a root fairing, a panel with ribs over and
    under it, coolant pipes, an edge frame."""
    yy, z = mapped(t_of(x), 26.0, 0.5)
    y0 = s * (yy + 0.4)
    span = 18.0
    yc = y0 + s * span / 2
    b.box((13.0, 3.2, 3.4), (x, y0 + s * 0.6, z), mat=HULL, tint=HULL_DARK, bevel=0.1)
    b.box((12.0, span, 0.5), (x, yc, z), mat=PANEL, tint=srgb(0.24, 0.25, 0.25))
    for k in range(4):
        dx = -4.5 + 3.0 * k
        for dz in (-0.45, 0.45):
            b.box((0.35, span, 0.4), (x + dx, yc, z + dz), mat=PAINT, tint=HULL_DARK, solid=False)
    b.box((12.6, 0.5, 0.9), (x, y0 + s * span, z), mat=PAINT, tint=HULL_DARK, solid=False)
    for dx in (-3.0, 3.0):
        b.tube([(x + dx, y0 + s * 1.0, z + 0.55), (x + dx, y0 + s * (span - 1.0), z + 0.55)], 0.18, mat=PAINT,
               tint=GREY, sides=6)


def barbette(b, at):
    """The stern turret's barbette, its turret torn away: an armoured ring round a dark well, the ring gear
    broken."""
    x, y, z = at
    b.lathe([(7.0, 0.0), (7.0, 2.0), (6.0, 2.0), (6.0, -1.6), (7.0, -1.6), (7.0, 0.0)], (x, y, z), mat=HULL,
            tint=GREY_HULL, sides=28, close=False, solid="hull")
    b.cyl(6.05, 0.3, (x, y, z - 1.2), mat=MATTE, tint=VOID, sides=24, solid=False, vary=0)
    b.cyl(7.25, 0.45, (x, y, z + 1.75), mat=PAINT, tint=HULL_DARK, sides=28, solid=False)
    for k in range(7):
        a = math.radians(k * 51.0 + 10.0)
        b.box((1.6, 0.7, 0.9), (x + math.cos(a) * 5.6, y + math.sin(a) * 5.6, z + 2.2 + 0.3 * (k % 2)),
              (b.rng.uniform(-30, 30), b.rng.uniform(-30, 30), math.degrees(a)), mat=RUST, tint=(1, 1, 1), solid=False)


# ------------------------------------------------------------------------------------------------ the field


def on_ground(m, p):
    q = m @ Vector(p)
    return Vector((q.x, q.y, 0.0))


def field(b):
    """The ground round her: the furrow she cut coming in, the scar between the halves with the torn-off turret,
    hull sections, plates, girders and burns; the earth the bow pushed up; the Reclaimers' scaffold into the bow's
    broken end and their camp at its foot; the ramp up to the hatch."""
    mb, ms = matrix_of(bow_place()), matrix_of(stern_place())
    tail = on_ground(ms, (x_of(0.0) - 16.0, 0, 0))
    s_head = on_ground(ms, (x_of(BREAK), 0, 0))
    b_tail = on_ground(mb, (x_of(BREAK), 0, KEEL))
    prow = on_ground(mb, (x_of(T_PROW) + 7.0, 0, 0))
    s_dir = (s_head - tail).normalized()
    b_dir = (prow - b_tail).normalized()

    def side(v):
        return Vector((-v.y, v.x, 0.0))
    # The furrow behind the stern: where she first touched down.
    path = [tail - s_dir * (100.0 * k / 6) + side(s_dir) * (8.0 * (k / 6) ** 2) for k in range(7)]
    for sg in (-1, 1):
        PL.mound_along(b, [tuple(p + side(s_dir) * sg * (30.0 - 8.0 * k / 6)) for k, p in enumerate(path)],
                       width=26.0, height=4.0, tint=SOIL, rough=0.6, taper_ends=True, step=8.0)
    for a, c in zip(path, path[1:]):
        wa, wc = 44.0 - 10.0 * (a - tail).length / 100.0, 44.0 - 10.0 * (c - tail).length / 100.0
        q = [a - side(s_dir) * wa / 2, a + side(s_dir) * wa / 2, c + side(s_dir) * wc / 2, c - side(s_dir) * wc / 2]
        b.slab([(p.x, p.y) for p in q], 0.1, (0, 0, 0.0), mat=MATTE, tint=SOIL_DARK,
               solid=False, wear=0.9)
    # The earth the stern pushed up on its low side as it rolled, and under it.
    low = side(s_dir) * (1.0 if STERN_AT[1][0] < 0 else -1.0)
    bank = [tail + low * 30.0 + s_dir * 10.0, (tail + s_head) / 2 + low * 34.0, s_head + low * 30.0 - s_dir * 8.0]
    PL.mound_along(b, [tuple(p) for p in bank], width=30.0, height=4.5, tint=SOIL, rough=0.6, taper_ends=True,
                   step=8.0)
    for k in range(9):
        c = bank[0].lerp(bank[2], (k + 0.5) / 9) + low * 4.0
        clods(b, c, 9.0, 2)
    # The scar between the halves.
    gap = [s_head - s_dir * 20.0, (s_head + b_tail) / 2 + side(b_dir) * 6.0, b_tail + b_dir * 60.0]
    for a, c in zip(gap, gap[1:]):
        d = (c - a).normalized()
        q = [a - side(d) * 30.0, a + side(d) * 30.0, c + side(d) * 26.0, c - side(d) * 26.0]
        b.slab([(p.x, p.y) for p in q], 0.1, (0, 0, 0.0), mat=MATTE, tint=SOIL_DARK, solid=False, wear=0.9)
    for sg in (-1, 1):
        PL.mound_along(b, [tuple(p + side(b_dir) * sg * 36.0) for p in gap], width=18.0, height=3.5, tint=SOIL,
                       rough=0.45, taper_ends=True, step=10.0)
    for k in range(9):
        p = s_head.lerp(b_tail, (k + 0.5) / 9) + side(b_dir) * b.rng.uniform(-22.0, 22.0)
        PL.scorch(b, (p.x, p.y, 0.0), b.rng.uniform(7.0, 15.0), tint=srgb(0.07, 0.06, 0.05))
    mid = (s_head + b_tail) / 2
    with b.frame((mid.x + 6.0, mid.y - 14.0, 3.6), (166.0, 9.0, 32.0)):
        turret(b, (0, 0, 0), 0.0, elev=0.0, barbette=0.0, wrecked=True)
    for k, (f, off, yaw) in enumerate(((0.22, 18.0, 30.0), (0.55, -20.0, 140.0), (0.78, 24.0, 250.0))):
        p = s_head.lerp(b_tail, f) + side(b_dir) * off
        hull_piece(b, (p.x, p.y, 0.0), yaw, (6, 7, 8, 9) if k != 1 else (2, 3, 4, 5))
    for k in range(34):
        p = s_head.lerp(b_tail, b.rng.random()) + side(b_dir) * b.rng.uniform(-34.0, 34.0)
        w, dpt = b.rng.uniform(3.0, 11.0), b.rng.uniform(2.5, 7.0)
        b.box((w, dpt, 0.5), (p.x, p.y, 0.5 + b.rng.random() * 0.8), (b.rng.uniform(-28, 28), b.rng.uniform(-28, 28),
                                                                       b.rng.uniform(0, 360)), mat=HULL,
              tint=b.rng.choice((OLIVE_DEEP, HULL_DARK, BURNT, BELT_T, SHIELD)), solid=w > 6.0)
    for k in range(7):
        p = s_head.lerp(b_tail, b.rng.random()) + side(b_dir) * b.rng.uniform(-28.0, 28.0)
        d = Vector((b.rng.uniform(-1, 1), b.rng.uniform(-1, 1), b.rng.uniform(0.05, 0.3))).normalized()
        ln = b.rng.uniform(8.0, 18.0)
        kink = p + d * ln * 0.55 + Vector((0, 0, b.rng.uniform(0.6, 2.5)))
        b.beam(p, kink, 0.5, 0.9, mat=PAINT, tint=srgb(0.24, 0.24, 0.23))
        d2 = (d + Vector((b.rng.uniform(-0.6, 0.6), b.rng.uniform(-0.6, 0.6), -0.7))).normalized()
        b.beam(kink, kink + d2 * ln * 0.45, 0.5, 0.9, mat=PAINT, tint=srgb(0.24, 0.24, 0.23))
    PL.debris(b, (mid.x, mid.y), 70.0, count=60, size=(0.8, 4.0), tints=(OLIVE_DEEP, HULL_DARK, DARK),
              mats=(HULL, PAINT, RUST))
    # A radiator fin torn off the stern's low side, lying bent beside it.
    p = s_head.lerp(tail, 0.7) + low * 44.0
    with b.frame((p.x, p.y, 1.2), (8.0, -6.0, math.degrees(math.atan2(s_dir.y, s_dir.x)) + 70.0)):
        b.box((12.0, 17.0, 0.5), (0, 0, 0), mat=PANEL, tint=GREY_LIGHT, solid=True)
        for k in range(4):
            b.box((0.35, 17.0, 0.4), (-4.5 + 3.0 * k, 0, 0.45), mat=PAINT, tint=HULL_DARK, solid=False)
        b.box((13.0, 3.0, 3.0), (0, -9.5, 0.6), (0, 0, 6), mat=HULL, tint=BURNT)
    # The wave of earth the prow pushed up: a crescent bank round its buried end, a lower bank and a fill inside
    # it, the walls it threw up along the bow, clods.
    yaw_b = math.degrees(math.atan2(b_dir.y, b_dir.x))
    head = prow - b_dir * 4.0
    for rr, h, w, a0, sd in ((40.0, 14.0, 36.0, 110.0, 3), (21.0, 9.0, 24.0, 80.0, 4)):
        pts = []
        for k in range(11):
            a = math.radians(-a0 + 2 * a0 * k / 10)
            r2 = rr * (0.85 + 0.3 * P._hash(k, sd, 1, 99))
            pts.append(tuple(head + b_dir * (math.cos(a) * r2) + side(b_dir) * (math.sin(a) * r2 * 1.3)))
        PL.mound_along(b, pts, width=w, height=h, tint=SOIL, rough=0.55, taper_ends=True, step=6.0)
    PL.rock(b, tuple(head - b_dir * 6.0), (46.0, 54.0, 9.0), (0, 0, yaw_b), tint=SOIL, n=6, rough=0.35, sink=0.3)
    for k in range(7):
        a = math.radians(-90.0 + 180.0 * (k + 0.5) / 7 + b.rng.uniform(-8, 8))
        c = head + b_dir * (math.cos(a) * 38.0) + side(b_dir) * (math.sin(a) * 49.0)
        PL.rock(b, (c.x, c.y, 0.0), (b.rng.uniform(16, 26), b.rng.uniform(12, 20), b.rng.uniform(9, 15)),
                (0, 0, yaw_b + math.degrees(a)), tint=b.rng.choice((SOIL, SOIL_DARK)), n=5, rough=0.45,
                sink=0.3)
    t_h, door, out = hatch_site()
    for sg in (-1, 1):
        start = 0.6 if sg > 0 else max(0.62, (t_h - BREAK) / (T_PROW - BREAK) + 0.12)
        pts = []
        for k in range(7):
            f = start + (1.02 - start) * k / 6
            c = b_tail.lerp(prow, f)
            pts.append(tuple(c + side(b_dir) * sg * (34.0 - 4.0 * k / 6)))
        PL.mound_along(b, pts, width=26.0, height=11.0, tint=SOIL, rough=0.55, taper_ends=True, step=8.0)
    clods(b, head, 62.0, 34, avoid=door)
    for sg in (-1, 1):
        for k in range(8):
            c = b_tail.lerp(prow, 0.62 + 0.38 * k / 7) + side(b_dir) * sg * 38.0
            clods(b, c, 14.0, 3, avoid=door)
    clods(b, tail - s_dir * 40.0, 50.0, 14, avoid=door)
    clods(b, (s_head + b_tail) / 2, 60.0, 12, avoid=door)
    # The ramp of plates up to the hatch.
    foot = door + Vector((out.x, out.y, 0.0)).normalized() * 5.5
    P.ramp(b, (foot.x, foot.y), (door.x, door.y), 2.4, 0.05, max(0.2, door.z - 0.05), mat=HULL, tint=HULL_DARK)
    reclaimers(b)


def clods(b, centre, radius, count, avoid=None):
    """Earth thrown up round `centre`: lumps of soil and torn turf, none within 46 m of `avoid`."""
    turf = srgb(0.30, 0.34, 0.18)
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        p = Vector((centre[0] + math.cos(a) * r, centre[1] + math.sin(a) * r, 0.0))
        size = (b.rng.uniform(2.5, 8.0), b.rng.uniform(2.0, 6.5), b.rng.uniform(1.0, 3.2))
        tint = b.rng.choice((SOIL, SOIL, SOIL_DARK, turf))
        if avoid is not None and (p - Vector((avoid[0], avoid[1], 0.0))).length < 46.0:
            continue
        PL.rock(b, tuple(p), size, (b.rng.uniform(-15, 15), b.rng.uniform(-15, 15), b.rng.uniform(0, 360)), tint=tint,
                n=3, rough=0.45, sink=0.35, solid="hull" if max(size) > 6.0 else False)


def hull_piece(b, at, yaw, strips, sink=0.3):
    """A torn-off piece of her plating (strips of the amidships section over three frames), lying outer face up
    where it fell, half sunk, its frames showing underneath."""
    r = ring(0.3)
    frame = inset(r, 0.3, 1.0)
    a, c = Vector(r[strips[0]]), Vector(r[strips[-1]])
    chord = c - a
    ang = math.degrees(math.atan2(chord.z, chord.y))
    rows, frames = [], []
    for k in range(4):
        dx = k * 8.5 - 12.75 - x_of(0.3)
        rows.append([Vector(r[j]) + Vector((dx, 0, 0)) for j in strips])
        frames.append([Vector(frame[j]) + Vector((dx, 0, 0)) for j in strips])
    mid = (a + c) / 2
    # Turn the piece about x so its chord lies flat, its outer face up.
    rot = Matrix.Rotation(math.radians(-ang), 4, "X")
    flip = 1.0 if (rot @ (Vector(r[strips[len(strips) // 2]]) - mid)).z > 0 else -1.0
    if flip < 0:
        rot = Matrix.Rotation(math.radians(180.0), 4, "X") @ rot
    rows = [[rot @ (p - Vector((0, mid.y, mid.z))) for p in row] for row in rows]
    frames = [[rot @ (p - Vector((0, mid.y, mid.z))) for p in row] for row in frames]
    zmax = max(p.z for row in rows for p in row)
    lift = Vector((0, 0, -zmax * sink))
    with b.frame(at, (b.rng.uniform(-6, 6), b.rng.uniform(-6, 6), yaw)):
        for i in range(3):
            for j in range(len(strips) - 1):
                if P._hash(i, j, 21, int(abs(at[0])) & 1023) < 0.12:
                    continue
                q = [p + lift for p in (rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])]
                m = sum(q, Vector()) / 4
                slab(b, q, 0.8, (m.x, 0.0, m.z - 30.0), b.rng.choice((OLIVE_DEEP, HULL_DARK, BELT_T, BURNT)))
        for k in range(4):
            b.tube([tuple(p + lift) for p in frames[k]], 0.35, mat=RUST, tint=(1, 1, 1), sides=6)


def reclaimers(b):
    """The Reclaimers' way into the bow: a scaffold of poles and planks up to the lowest deck of the break, a
    plank across, lamps; their camp at its foot."""
    base, fwd, side, top = scaffold_site()
    yaw = math.degrees(math.atan2(fwd.y, fwd.x))
    w = 3.6
    with b.frame((base.x, base.y, 0.0), (0, 0, yaw)):
        h = w / 2
        corners = [(-h, -h), (h, -h), (h, h), (-h, h)]
        for x, y in corners:
            b.cyl(0.07, top + 1.3, (x, y, (top + 1.3) / 2), mat=PAINT, tint=POLE, sides=6)
        z = 2.0
        while z < top + 0.6:
            for k in range(4):
                (x0, y0), (x1, y1) = corners[k], corners[(k + 1) % 4]
                b.beam((x0, y0, z), (x1, y1, z), 0.06, mat=PAINT, tint=POLE)
            z += 2.0
        for k in range(4):
            (x0, y0), (x1, y1) = corners[k], corners[(k + 1) % 4]
            zz = 0.2
            while zz + 4.0 < top:
                b.beam((x0, y0, zz), (x1, y1, zz + 4.0), 0.05, mat=PAINT, tint=POLE)
                zz += 4.0
        for zz in (top * 0.5, top):
            for k in range(5):
                b.box((0.62, w + 0.4, 0.06), (-h + 0.36 + k * 0.72, 0, zz), (0, 0, b.rng.uniform(-3, 3)),
                      mat=PAINT, tint=srgb(0.45, 0.36, 0.26), solid=k == 2, vary=0.1)
        ladder(b, (0.0, -h + 0.45, 0.0), top, (0, 0, 0), cage=False, tint=POLE)
        b.box((3.4, 0.9, 0.08), (h + 1.6, 0.0, top + 0.02), (0, -3, 0), mat=PAINT, tint=srgb(0.45, 0.36, 0.26))
        railing(b, (-h, h, top), (h, h, top), tint=POLE)
        railing(b, (-h, -h, top), (-h, h, top), tint=POLE)
        lamp(b, (h, -h, top + 2.2), (-25, 0, 90), colour=srgb(1.0, 0.85, 0.6))
        lamp(b, (-h, h, top * 0.5 + 2.0), (-25, 0, 180), colour=srgb(1.0, 0.85, 0.6))
        # A winch and its cable up into the break.
        b.box((1.4, 1.0, 1.0), (-4.5, 3.0, 0.5), mat=PAINT, tint=YELLOW, bevel=0.04)
        b.cyl(0.4, 1.2, (-4.5, 3.0, 1.1), (90, 0, 0), mat=PAINT, tint=DARK, sides=12, solid=False)
        b.tube([(-4.5, 3.0, 1.2), (4.5, 2.5, top + 4.0)], 0.03, mat=PAINT, tint=BLACK, sides=4)
    # The camp at the foot of the break.
    c = base - fwd * 14.0 - side * 10.0
    for k, (dx, dy, ww, dd) in enumerate(((0.0, 0.0, 6.0, 4.5), (-8.0, 5.0, 5.0, 4.0), (6.0, -6.5, 4.5, 4.0))):
        p = c + fwd * dx + side * dy
        PL.tarp(b, (p.x, p.y, 0.0), ww, dd, 2.5, yaw + b.rng.uniform(-15, 15),
                tint=(P.TARP_BLUE, P.TARP_ORANGE, P.TARP_GREEN)[k])
    PL.scrap_wall(b, tuple(c - fwd * 10.0 - side * 8.0), tuple(c - fwd * 10.0 + side * 9.0))
    PL.scrap_wall(b, tuple(c - fwd * 10.0 + side * 9.0), tuple(c + fwd * 2.0 + side * 13.0))
    f = c + fwd * 3.0 + side * 4.0
    b.cyl(0.5, 0.3, (f.x, f.y, 0.15), mat=MATTE, tint=srgb(0.06, 0.05, 0.05), sides=10, solid=False, vary=0)
    b.box((0.5, 0.5, 0.35), (f.x, f.y, 0.4), mat=LAMP, tint=srgb(1.0, 0.55, 0.2), solid=False, vary=0)
    b.marker("light", (f.x, f.y, 1.0), colour=[1.0, 0.6, 0.25], range=10.0, flicker=True)
    for k in range(3):
        p = c + fwd * (-4.0 + 3.0 * k) + side * -5.0
        P.crates(b, (p.x, p.y, 0.0), (0, 0, yaw + b.rng.uniform(-20, 20)), n=3 + k, tint=OLIVE_DEEP)
    for k in range(5):
        p = c + fwd * b.rng.uniform(-6, 6) + side * b.rng.uniform(4, 9)
        b.cyl(0.3, 0.9, (p.x, p.y, 0.45), mat=PAINT, tint=b.rng.choice((RUSTY, srgb(0.3, 0.4, 0.5), DARK)),
              sides=12, vary=0.1)
    PL.string_lights(b, [(base.x - side.x * 1.8, base.y - side.y * 1.8, 6.0), tuple(c + Vector((0, 0, 3.0))),
                         tuple(c - fwd * 10.0 + side * 9.0 + Vector((0, 0, 2.6)))])
    b.marker("loot", tuple(c + side * -5.0 + fwd * 6.0 + Vector((0, 0, 0.1))), table="crate", family="ship")


def build(b):
    b.set_pad(("rect", 700.0, 260.0), ground="dirt", blend=60.0, clear=15.0)
    stern(b)
    bow(b)
    field(b)
    b.zone("no-build", (0, 0, 0), size=(780.0, 320.0, 200.0))
    b.meta.update({"tier": "large", "landmark": False, "spawn": "plains", "visible_km": 14})


# ------------------------------------------------------------------------------------------------ look-dev views

_T_H, _DOOR, _OUT = hatch_site()
_FOOT, _FWD, _PORT, _ = scaffold_site()
_TOCAM = Vector((math.sin(math.radians(-64.7)), -math.cos(math.radians(-64.7)), 0.0))
_EYE = math.degrees(math.atan2(_OUT.x, -_OUT.y))
_HYAW = _EYE + 38.0
_CAM = Vector((math.sin(math.radians(_HYAW)), -math.cos(math.radians(_HYAW)), 0.0))
_RIGHT = Vector((-_CAM.y, _CAM.x, 0.0))

SUN = (26.0, 205.0)
FOG = 0.00016
GROUND = 30000.0
PREVIEW_AO = False
PEOPLE = [((_DOOR.x + _OUT.x * 7.0, _DOOR.y + _OUT.y * 7.0, 0), 200.0)]
SHIP = None
VIEWS = [
    {"name": "far", "target": (-20.0, 10.0, 30.0), "distance": 2600, "yaw": -30, "pitch": -0.4, "lens": 120},
    {"name": "overview", "target": (8.0, 4.0, 12.0), "distance": 590, "yaw": 22, "pitch": 28, "lens": 30},
    {"name": "break", "target": (8.0, -18.0, 17.0), "distance": 41.5, "yaw": -64.7, "pitch": -16.5, "lens": 20,
     "people": [((_FOOT.x + _TOCAM.x * 4.0 + 1.5, _FOOT.y + _TOCAM.y * 4.0, 0), -40.0),
                ((_FOOT.x + _TOCAM.x * 9.0 - 2.0, _FOOT.y + _TOCAM.y * 9.0 + 1.0, 0), 150.0)]},
    {"name": "hatch", "target": (_DOOR.x, _DOOR.y, 4.5), "distance": 32.0, "yaw": _HYAW,
     "pitch": math.degrees(math.asin((1.7 - 4.5) / 32.0)), "lens": 20,
     "people": [((_DOOR.x + _OUT.x * 6.5 + 1.0, _DOOR.y + _OUT.y * 6.5, 0), _HYAW + 160.0),
                ((_DOOR.x + _OUT.x * 5.0 - 1.2, _DOOR.y + _OUT.y * 5.0 - 0.6, 0), _HYAW - 20.0)],
     "ship": ((_DOOR.x + _CAM.x * 15.0 + _RIGHT.x * 9.0, _DOOR.y + _CAM.y * 15.0 + _RIGHT.y * 9.0, 0),
              _HYAW + 120.0)},
]
