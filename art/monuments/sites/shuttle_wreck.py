"""Shuttle Wreck (MONUMENTS.md §5.2 no. 23, small tier): a Seedward transport shuttle, 40 m long, that came down on its
belly and slid to a stop at the end of a long furrow. Its lifting-body hull of white plating with orange and teal
bands, a dark cockpit visor over the drooped nose, a swept low wing on the right; the left wing tore off and lies back
in the furrow, upside down and broken. Twin canted fins and two engine pods at the tail, soot behind them. A breach in
the left flank shows the ribs and the hold; the cargo ramp at the rear lies open on the ground. Inside, the hold: cargo
racks, spilled cases, the sealed crate. Ship parts in the wreck, two crates, the sealed crate in the hold.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (BLACK, DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED, RED_LAMP, TEAL, YELLOW, srgb)
from monkit.structures import hull_ring
from monkit.surfaces import CONCRETE, GLASS, GLOSS, GRATE, HULL, LAMP, MATTE, PAINT, PANEL, RUST

KEY = "shuttle-wreck"
TITLE = "Shuttle Wreck"
SEED = 2323
BUILDER = {"chunk": 40.0, "max_edge": 3.0}

L, W, HH = 40.0, 8.6, 6.4       # fuselage
ST = 26                          # stations
NR = 28                          # points round each ring
Z0 = HH / 2 - 0.6                # the ring centre's height (the belly is dug in 0.6 m)
DECK = 0.55                      # the hold's floor
YAW, PITCH, ROLL = -14.0, 2.0, 3.5         # nose down, tail up, rolled a little to the left
POS = (3.0, -2.0, 0.0)
WHITE = P.HULL_WHITE


def station(i):
    """(y, width, height, z centre) of station i: a drooped nose, the straight body, a slightly tapered tail."""
    t = i / ST
    if t < 0.24:
        k = t / 0.24
        f = math.sqrt(max(0.0, 1 - (1 - k) ** 2))
        w, h = W * (0.22 + 0.78 * f), HH * (0.35 + 0.65 * f)
        zc = Z0 - (1 - f) * 1.4
    elif t > 0.86:
        k = (t - 0.86) / 0.14
        w, h = W * (1 - 0.12 * k), HH * (1 - 0.14 * k)
        zc = Z0 + 0.35 * k
    else:
        w, h, zc = W, HH, Z0
    return -L / 2 + L * t, w, h, zc


def breach(i, k):
    """Whether face (station i, ring point k) is torn out: a ragged hole in the left flank amidships."""
    if not (9 <= i <= 13):
        return False
    lo, hi = 11, 18                     # around -X (k = NR / 2 = 14 is the left side's middle)
    if i in (9, 13):
        lo, hi = 13, 16
    return lo <= k <= hi


def fuselage(b):
    """The hull as a closed double skin (outer plating, the inner lining 12 cm in, rims round the open rear and the
    breach), so it shows from inside the hold as well as outside: the game culls back faces, and a single open skin
    would vanish from within."""
    rings, inner, profile = [], [], []
    for i in range(ST + 1):
        y, w, h, zc = station(i)
        rings.append(hull_ring(w, h, y, zc, n=NR, keel=0.06, flat=0.42))
        inner.append(hull_ring(w - 0.24, h - 0.24, y, zc, n=NR, keel=0.06, flat=0.42))
        profile.append((y, w, h, zc))
    n_o = (ST + 1) * NR
    verts = [v for ring in rings for v in ring] + [v for ring in inner for v in ring]

    def o(i, k):
        return i * NR + k % NR

    def n(i, k):
        return n_o + i * NR + k % NR

    faces = []
    for i in range(ST):
        for k in range(NR):
            if breach(i, k):
                # Rims along the hole's edges, joining the plating to the lining.
                if not breach(i, k - 1):
                    faces.append([o(i, k), o(i + 1, k), n(i + 1, k), n(i, k)])
                if not breach(i, k + 1):
                    faces.append([o(i, k + 1), n(i, k + 1), n(i + 1, k + 1), o(i + 1, k + 1)])
                if not breach(i - 1, k):
                    faces.append([o(i, k), n(i, k), n(i, k + 1), o(i, k + 1)])
                if not breach(i + 1, k):
                    faces.append([o(i + 1, k), o(i + 1, k + 1), n(i + 1, k + 1), n(i + 1, k)])
                continue
            faces.append([o(i, k), o(i, k + 1), o(i + 1, k + 1), o(i + 1, k)])
            faces.append([n(i, k), n(i + 1, k), n(i + 1, k + 1), n(i, k + 1)])
    for k in range(NR):                       # the rim round the open rear
        faces.append([o(ST, k), o(ST, k + 1), n(ST, k + 1), n(ST, k)])
    # Nose caps: the plating to a point a little ahead of the first ring, the lining to one just behind it.
    y0, w0, h0, z00 = profile[0]
    tip = len(verts)
    verts.append((0.0, y0 - 1.2, z00 - 0.3))
    verts.append((0.0, y0 - 0.9, z00 - 0.3))
    for k in range(NR):
        faces.append([o(0, k), tip, o(0, k + 1)])
        faces.append([n(0, k), n(0, k + 1), tip + 1])
    b.mesh(verts, faces, mat=HULL, tint=WHITE, solid=False, wear=0.55, vary=0.0, smooth=True)
    # Livery: an orange band and a teal pinstripe down both flanks (broken by the breach), the code.
    for side in (-1, 1):
        for (dz, tint, rad) in ((0.0, ORANGE, 0.16), (-0.42, TEAL, 0.07)):
            pts = []
            for i in range(3, ST):
                if side < 0 and 8 <= i <= 14:
                    if pts:
                        b.tube(pts, rad, mat=PAINT, tint=tint, sides=4, cap=True)
                        pts = []
                    continue
                y, w, h, zc = profile[i]
                pts.append((side * (w / 2 + 0.03), y, zc + 0.6 + dz))
            if pts:
                b.tube(pts, rad, mat=PAINT, tint=tint, sides=4, cap=True)
        y, w, h, zc = profile[17]
        P.stencil(b, "SW-T 0412", (side * (w / 2 + 0.05), y + (1.5 if side < 0 else -1.5), zc + 1.5),
                  (0, 0, 90 * side), 0.55, DARK, align="centre")
    # The cockpit visor: dark glass laid over the upper nose between stations 3 and 5.
    vis = []
    for i in (3, 4, 5):
        y, w, h, zc = profile[i]
        ring = hull_ring(w * 1.012, h * 1.012, y, zc, n=NR, keel=0.06, flat=0.42)
        vis.append([ring[k] for k in range(4, 11)])
    vv = [p for r in vis for p in r]
    vf = []
    for i in range(2):
        for k in range(6):
            a = i * 7 + k
            vf.append([a, a + 1, a + 8, a + 7])
    b.mesh(vv, vf, mat=GLASS, solid=False, smooth=False)
    # Ribs showing in the breach, and the hold's deck seen through it.
    for i in range(9, 14):
        y, w, h, zc = profile[i]
        ring = hull_ring(w * 0.965, h * 0.965, y, zc, n=NR, keel=0.06, flat=0.42)
        b.tube([ring[k] for k in range(10, 20)], 0.12, mat=RUST, tint=(1, 1, 1), sides=5, cap=True)
    # Soot where the nose struck and along the belly, plates gone from the upper hull (dark), scorched panels.
    def patch(i, k, size, tint, mat=MATTE, lift=0.015):
        y, w, h, zc = profile[i]
        ring = hull_ring(w, h, y, zc, n=NR, keel=0.06, flat=0.42)
        p, q = Vector(ring[k]), Vector(ring[(k + 1) % NR])
        n = Vector((p.x + q.x, 0, p.z + q.z - 2 * zc)).normalized()
        c = (p + q) / 2 + n * lift
        rot = (0, -math.degrees(math.atan2(n.z, n.x)) + 90.0, 0)
        b.box((0.012, size[0], size[1]), tuple(c), (rot[0], rot[1] - 90.0, 0), mat=mat, tint=tint, solid=False,
              vary=0.15)

    for k in range(14):
        i = b.rng.randrange(1, 8)
        patch(i, b.rng.choice((16, 17, 18, 19, 20, 21, 22, 23, 24, 25)), (1.5 + 2.0 * b.rng.random(),
                                                                         0.8 + 1.0 * b.rng.random()), P.SCORCH)
    for k in range(10):
        patch(b.rng.randrange(6, 24), b.rng.randrange(3, 11), (1.0 + 0.8 * b.rng.random(), 0.8 + 0.5 * b.rng.random()),
              srgb(0.06, 0.06, 0.065))
    for k in range(8):
        patch(b.rng.randrange(14, 26), b.rng.choice((0, 1, 2, 26, 27, 12, 13)), (2.0 + 2.0 * b.rng.random(),
                                                                                 1.0 + b.rng.random()), P.SOOT)
    # Torn plate edges curling out of the breach.
    for k in range(6):
        y = profile[9][0] + (profile[14][0] - profile[9][0]) * b.rng.random()
        z = Z0 + b.rng.uniform(-1.5, 1.8)
        b.box((0.04, 0.6 + 0.8 * b.rng.random(), 0.5 + 0.6 * b.rng.random()), (-W / 2 - 0.25, y, z),
              (b.rng.uniform(-20, 20), b.rng.uniform(-30, 30), b.rng.uniform(-35, 35)), mat=HULL, tint=WHITE,
              solid=False)
    return profile


def wing(b, side, torn=False):
    """A swept low wing on `side` (+1 right, -1 left) in the fuselage frame: a slab with a leading-edge strip and a
    winglet; `torn` leaves only a ragged stub."""
    y_root0, y_root1 = -3.0, 9.5
    span, sweep, tip_chord = 10.5, 7.0, 4.0
    z = 1.0
    if torn:
        outline = [(0.0, y_root0), (0.0, y_root1), (1.6, y_root1 - 0.5), (1.1, y_root1 - 3.5), (2.2, y_root1 - 6.0),
                   (1.4, y_root0 + 2.5), (1.9, y_root0 + 0.7)]
    else:
        outline = [(0.0, y_root0), (0.0, y_root1), (span, y_root0 + sweep + tip_chord), (span, y_root0 + sweep)]
    pts = [(side * (W / 2 - 0.4 + x), y) for x, y in outline]
    if side < 0:
        pts = pts[::-1]
    b.slab(pts, 0.55, (0, 0, z), mat=HULL, tint=WHITE, solid="hull", wear=0.5)
    if not torn:
        b.beam((side * (W / 2 - 0.4), y_root0, z + 0.27), (side * (W / 2 - 0.4 + span), y_root0 + sweep, z + 0.27), 0.3,
               0.55, mat=PAINT, tint=srgb(0.35, 0.36, 0.37))
        tx = side * (W / 2 - 0.4 + span)
        b.slab([(tx - side * 0.15, y_root0 + sweep + 0.5), (tx + side * 0.15, y_root0 + sweep + 0.5),
                (tx + side * 0.15, y_root0 + sweep + tip_chord), (tx - side * 0.15, y_root0 + sweep + tip_chord)],
               1.8, (0, 0, z + 0.4), mat=HULL, tint=WHITE, solid=False)
        b.box((0.25, 0.3, 0.2), (tx, y_root0 + sweep + 0.4, z + 2.1), mat=LAMP, tint=srgb(0.2, 0.9, 0.3),
              solid=False)
        for k in range(3):
            y = y_root0 + 3.0 + 3.0 * k
            b.box((side * 0 + 3.5, 0.04, 0.02), (side * (W / 2 + 3.0), y, z + 0.56), mat=PAINT, tint=DARK,
                  solid=False, vary=0)
    else:
        for k in range(4):
            b.tube([(side * (W / 2 + 1.2), y_root0 + 1.0 + 2.6 * k, z + 0.27),
                    (side * (W / 2 + 2.2 + b.rng.random()), y_root0 + 1.3 + 2.6 * k, z + 0.1)], 0.07, mat=RUST,
                   tint=(1, 1, 1), sides=4, cap=True)


def tail(b, profile):
    """The twin canted fins, the engine pods under them, soot."""
    y, w, h, zc = profile[ST - 2]
    for side in (-1, 1):
        with b.frame((side * 1.6, y - 4.5, zc + h / 2 - 0.3), (0, side * 22.0, 0)):
            b.extrude([(0.0, 0.0), (6.5, 0.0), (6.8, 5.0), (4.0, 5.0)], 0.35, (0, 0, 0), (0, 0, 90), mat=HULL,
                      tint=WHITE, solid="hull")
            b.box((0.4, 2.8, 0.45), (0, 5.4, 4.8), mat=PAINT, tint=ORANGE, solid=False)
            b.box((0.36, 0.3, 0.3), (0, 6.75, 4.85), mat=LAMP, tint=RED_LAMP, solid=False)
        P.nacelle(b, (side * (w / 2 + 1.0), y - 3.0, zc - 0.4), (0, 0, 0), radius=1.35, length=7.0,
                  burnt=side < 0)
        b.beam((side * (w / 2 - 0.2), y - 4.0, zc - 0.2), (side * (w / 2 + 0.6), y - 3.0, zc - 0.4), 0.5, 1.2,
               mat=HULL, tint=WHITE)
    for k in range(6):
        b.box((0.05, 1.2 + b.rng.random(), 0.8 + b.rng.random()), (-W / 2 - 0.02, y - 4.0 + 1.6 * k, zc - 0.5),
              (0, 0, b.rng.uniform(-6, 6)), mat=MATTE, tint=P.SCORCH, solid=False, vary=0.2)


def hold(b, profile):
    """The hold: the deck, cargo racks along the right wall, spilled cases, the sealed crate, the open ramp, the
    cockpit's bulkhead with its door."""
    y0, y1 = profile[6][0], profile[ST][0]
    P.grid_box(b, (W - 1.3, y1 - y0, 0.15), (0, (y0 + y1) / 2, DECK - 0.075), mat=PAINT, tint=srgb(0.4, 0.42, 0.42),
               step=1.5)
    for k in range(int((y1 - y0) / 1.2)):
        b.box((W - 1.4, 0.06, 0.02), (0, y0 + 0.6 + 1.2 * k, DECK + 0.01), mat=PAINT, tint=DARK, solid=False,
              vary=0)
    # Bulkhead with a door toward the cockpit.
    yb = profile[6][0]
    P.grid_box(b, (2.6, 0.12, 4.4), (-2.3, yb, DECK + 2.2), mat=PANEL, tint=LIGHTGREY, step=0.8)
    P.grid_box(b, (2.6, 0.12, 4.4), (2.3, yb, DECK + 2.2), mat=PANEL, tint=LIGHTGREY, step=0.8)
    b.box((2.0, 0.12, 2.1), (0, yb, DECK + 2.3 + 1.05), mat=PANEL, tint=LIGHTGREY)
    with b.frame((-1.0, yb - 0.06, DECK), (0, 0, -70.0)):
        b.box((1.0, 0.06, 2.25), (0.5, 0, 1.13), mat=PAINT, tint=TEAL, solid=False)
    # Racks along the right wall, cases fallen from them across the deck.
    for k in range(4):
        y = profile[8][0] + 4.5 * k
        with b.frame((2.6, y, DECK), (0, 0, 0)):
            for x in (-0.5, 0.5):
                for yy in (-1.8, 1.8):
                    b.box((0.08, 0.08, 3.0), (x, yy, 1.5), mat=PAINT, tint=DARK, solid=False, vary=0)
            for z in (0.4, 1.4, 2.4):
                b.box((1.1, 3.7, 0.06), (0, 0, z), mat=GRATE, tint=GREY, solid=False, vary=0)
                if b.rng.random() < 0.6:
                    b.box((0.8, 1.2, 0.6), (0, b.rng.uniform(-1.0, 1.0), z + 0.33), mat=PAINT,
                          tint=(ORANGE, srgb(0.3, 0.35, 0.3), GREY)[k % 3], solid=False)
    for k in range(7):
        b.box((0.8, 1.1, 0.6), (b.rng.uniform(-2.5, 1.5), b.rng.uniform(profile[9][0], profile[20][0]),
                                 DECK + 0.3 + 0.6 * (k % 2) * 0), (b.rng.uniform(-6, 6), 0, b.rng.uniform(0, 360)),
              mat=PAINT, tint=(ORANGE, srgb(0.3, 0.35, 0.3), GREY)[k % 3], solid=False)
    b.marker("loot", (1.0, profile[11][0], DECK + 0.05), table="crate")
    # The sealed crate: a heavy case on a skid, strapped, its lock panel lit.
    sy = profile[17][0]
    with b.frame((-1.4, sy, DECK), (0, 0, 8.0)):
        b.box((2.2, 1.4, 0.15), (0, 0, 0.075), mat=PAINT, tint=DARK)
        b.box((2.0, 1.2, 1.2), (0, 0, 0.75), mat=PAINT, tint=srgb(0.32, 0.36, 0.38), bevel=0.05)
        for x in (-0.6, 0.6):
            b.box((0.12, 1.24, 1.24), (x, 0, 0.75), mat=PAINT, tint=YELLOW, solid=False, vary=0)
        b.box((0.4, 0.04, 0.3), (0, -0.62, 0.85), mat=PAINT, tint=BLACK, solid=False)
        b.box((0.08, 0.05, 0.08), (0.1, -0.645, 0.9), mat=LAMP, tint=RED_LAMP, solid=False, vary=0)
        P.stencil(b, "SEALED", (0, -0.62, 1.15), (0, 0, 0), 0.12, YELLOW, align="centre")
    b.marker("loot", (-1.4, sy - 1.0, DECK + 0.05), table="sealed-crate")
    # The ramp, open: hinged at the deck's rear edge, its foot on the ground behind.
    yr = profile[ST][0]
    ln = 6.5
    p = math.radians(PITCH)
    hz = DECK * math.cos(p) + yr * math.sin(p)          # the hinge's height above the ground
    ang = PITCH + math.degrees(math.asin(min(1.0, max(0.0, hz - 0.05) / ln)))   # its foot on the ground
    with b.frame((0, yr, DECK - 0.05), (-ang, 0, 0)):
        P.grid_box(b, (5.6, ln, 0.25), (0, ln / 2, -0.12), mat=HULL, tint=WHITE, step=1.5)
        for k in range(8):
            b.box((5.2, 0.08, 0.06), (0, 0.5 + 0.75 * k, 0.03), mat=PAINT, tint=DARK, solid=False, vary=0)
        for s in (-1, 1):
            b.box((0.15, ln, 0.4), (s * 2.7, ln / 2, 0.1), mat=PAINT, tint=YELLOW, solid=False)
    b.collide_box(Vector((0, yr + ln / 2 * math.cos(math.radians(ang)), (DECK - 0.6) / 2)), (5.6, ln, 0.25),
                  (-ang, 0, 0))
    # Collision for the hold: the deck, the walls (the breach left open), the roof, the nose.
    b.collide_box(Vector((0, (y0 + y1) / 2, DECK - 0.1)), (W - 1.3, y1 - y0, 0.2), (0, 0, 0))
    b.collide_box(Vector((W / 2 - 0.3, (y0 + y1) / 2, Z0)), (0.4, y1 - y0, HH - 1.0), (0, 0, 0))
    ya, yb2 = profile[9][0], profile[14][0]
    for (a, c) in ((y0, ya), (yb2, y1)):
        b.collide_box(Vector((-W / 2 + 0.3, (a + c) / 2, Z0)), (0.4, c - a, HH - 1.0), (0, 0, 0))
    b.collide_box(Vector((-W / 2 + 0.3, (ya + yb2) / 2, DECK - 0.3)), (0.4, yb2 - ya, 0.6), (0, 0, 0))
    b.collide_box(Vector((0, (y0 + y1) / 2, Z0 + HH / 2 - 0.4)), (W - 1.0, y1 - y0, 0.4), (0, 0, 0))
    yn0 = profile[0][0] - 1.2
    b.collide_box(Vector((0, (yn0 + y0) / 2, Z0 - 0.4)), (W * 0.75, y0 - yn0, HH * 0.75), (0, 0, 0))


def build(b):
    b.set_pad(("rect", 44.0, 66.0), ground="dirt", blend=10.0, clear=3.0)
    fwd = Vector((math.sin(math.radians(YAW)), -math.cos(math.radians(YAW)), 0))     # the way it slid
    side = Vector((-fwd.y, fwd.x, 0))
    c = Vector(POS)

    # The furrow behind it: two banks of earth and the scar, scorch, panels and parts shed along it.
    tail_end = c - fwd * (L / 2 + 6.0)
    for s in (-1, 1):
        pts = [tuple((tail_end - fwd * (3.0 * k) + side * s * (4.4 + 0.5 * math.sin(k * 1.4))).xy) for k in range(10)]
        P.berm(b, pts, width=4.2, height=0.55, tint=P.EARTH, jag=0.4)
        P.clods(b, tuple(tail_end - fwd * 12.0 + side * s * 5.0), 9.0, count=10, tint=P.EARTH, size=0.45, inner=0.0)
    P.ribbon(b, [tuple((tail_end - fwd * (5.0 * k)).xy) for k in range(6)], 6.5, 0.0, P.EARTH_DARK, mat=CONCRETE,
             step=2.0)
    for s in (-1, 1):
        pts = [tuple((c + fwd * (L / 2 - 6.0 - 4.0 * k) + side * s * (W / 2 + 1.4 + 0.3 * math.sin(k))).xy)
               for k in range(9)]
        P.berm(b, pts, width=4.0, height=0.8, tint=P.EARTH, jag=0.4)
    nose = c + fwd * (L / 2 + 1.0)
    P.berm(b, [tuple((nose + side * s_).xy) for s_ in (-6.0, -3.0, 0.0, 3.0, 6.0)], width=3.0, height=1.6,
           tint=P.EARTH)
    P.clods(b, tuple(nose + fwd * 2.5), 7.0, count=20, tint=P.EARTH, size=0.6, inner=3.0)
    P.scorch(b, tuple(tail_end - fwd * 6.0), 2.2)
    P.scorch(b, tuple(c - fwd * (L / 2 - 2.0) - side * 6.0), 2.6)
    P.debris(b, tuple(tail_end - fwd * 10.0), 6.0, count=12, size=1.6, tint=WHITE, mat=HULL)
    P.debris(b, tuple(c - side * 8.0), 5.0, count=8, size=1.2, tint=WHITE, mat=HULL)

    # The torn left wing, upside down in the furrow, broken in two.
    wing_at = tail_end - fwd * 14.0 - side * 3.0
    with b.frame((wing_at.x, wing_at.y, 0.55), (180.0, 4.0, YAW + 38.0)):
        b.slab([(0.0, -3.0), (0.0, 9.5), (6.0, 11.0), (6.0, 2.0)], 0.55, (0, 0, 0), mat=HULL, tint=WHITE,
               solid="hull", wear=0.6)
    with b.frame((wing_at.x + 6.0 * side.x, wing_at.y + 6.0 * side.y, 0.6), (172.0, -8.0, YAW + 52.0)):
        b.slab([(0.0, 2.0), (0.0, 11.0), (4.5, 11.0), (4.5, 7.0)], 0.55, (0, 0, 0), mat=HULL, tint=WHITE,
               solid="hull", wear=0.6)
    P.scorch(b, (wing_at.x, wing_at.y, 0.0), 2.0)

    # The shuttle.
    with b.frame(POS, (0, 0, YAW)):
        with b.frame((0, 0, 0), (PITCH, ROLL, 0)):
            profile = fuselage(b)
            wing(b, 1)
            wing(b, -1, torn=True)
            tail(b, profile)
            hold(b, profile)
            # A landing leg torn half out of its well, antennae, a hatch on the right flank.
            y, w, h, zc = profile[5]
            b.beam((1.0, y, Z0 - HH / 2 + 0.6), (2.2, y + 1.6, -0.2), 0.3, 0.3, mat=PAINT, tint=GREY)
            P.wheel(b, (2.3, y + 1.8, 0.2), (0, 0, 20), 0.5, 0.35, sides=12)
            for k in range(2):
                yy = profile[12 + 4 * k][0]
                b.cyl(0.03, 1.4, (0.3, yy, Z0 + HH / 2 + 0.6), mat=PAINT, tint=DARK, sides=5, solid=False)
            y, w, h, zc = profile[8]
            with b.frame((w / 2 + 0.03, y, zc + 0.2), (0, 0, 0)):
                b.box((0.04, 1.2, 1.9), (0, 0, 0), mat=PAINT, tint=DARK, solid=False)
                b.box((0.05, 1.0, 0.18), (0.01, 0, 0.75), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    b.marker("loot", tuple(c + side * 7.0 + fwd * 4.0), table="ship-parts")
    b.marker("loot", tuple(tail_end - fwd * 9.0), table="ship-parts")
    b.marker("loot", tuple(c - side * 7.0 - fwd * 2.0), table="crate")

    b.zone("no-build", (0, 0, 0), size=(60.0, 80.0, 30.0))
    b.meta.update({"tier": "small", "landmark": True, "spawn": "open", "family": "ships"})


SUN = (30.0, 235.0)
PEOPLE = [((-4.5, -6.0, 0.0), 30.0)]
VIEWS = [
    {"name": "wreck", "target": (2.0, -2.0, 3.0), "distance": 68, "yaw": -38, "pitch": 22, "lens": 30},
    {"name": "breach", "target": (-1.5, -3.0, 2.6), "distance": 17, "yaw": -88, "pitch": 4, "lens": 24},
    {"name": "hold", "target": (5.4, 19.0, 2.2), "distance": 17, "yaw": 156, "pitch": 9, "lens": 26},
]
