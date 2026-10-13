"""Parts for the large monuments (MONUMENTS.md §5.4): walls with openings, rooms, decks, switchback stairs, steel
frames and trusses, industrial halls, blast doors, pipe racks, tanks, raised blocks and ramps, rock masses, scrap and
scorch marks, stencilled codes, and fixed pieces (flood masts, wagons, locomotives, wrecked vehicles) registered into
the kit so sites can place them with `Builder.piece`.

Positions in metres in the builder's current frame, angles in degrees, fronts toward -Y unless said otherwise. Import
this module before placing its pieces (`from monkit import parts_large`).
"""
import math

from mathutils import Vector

from . import kit
from .kit import (BLACK, CONCRETE_T, DARK, GREY, LIGHTGREY, OFFWHITE, OLIVE, ORANGE, RED, RED_LAMP, TEAL, WARM_LAMP,
                  YELLOW, cable, ladder, lamp, railing, srgb, stairs, stripes)
from .surfaces import (ASPHALT, CHAINLINK, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, HULL, LAMP, MATTE,
                       PAINT, PANEL, RUST)

# ------------------------------------------------------------------------------------------------ colours

# Bulwark (MONUMENTS.md §3): olive and grey, armoured concrete.
OLIVE_DARK = srgb(0.29, 0.32, 0.23)
OLIVE_LIGHT = srgb(0.52, 0.55, 0.40)
BW_GREY = srgb(0.46, 0.48, 0.46)
BW_CONCRETE = srgb(0.80, 0.80, 0.76)
# Seedward industrial and common materials.
STEEL = srgb(0.42, 0.44, 0.46)
DARK_STEEL = srgb(0.26, 0.27, 0.28)
RUSTY = srgb(0.48, 0.30, 0.20)
SOOT = srgb(0.05, 0.045, 0.04)
ROCK = srgb(0.46, 0.43, 0.39)
ROCK_DARK = srgb(0.33, 0.31, 0.29)
EARTH = srgb(0.36, 0.30, 0.22)
SNOW = srgb(0.93, 0.95, 0.98)
TARP_BLUE = srgb(0.20, 0.35, 0.52)
TARP_GREEN = srgb(0.30, 0.40, 0.27)
TARP_ORANGE = srgb(0.80, 0.42, 0.14)
CREAM = srgb(0.86, 0.82, 0.70)
CHIP = {"amber": srgb(1.0, 0.62, 0.10), "cyan": srgb(0.15, 0.85, 1.0), "violet": srgb(0.62, 0.25, 1.0)}

UP = Vector((0, 0, 1))


def _yaw(d):
    return math.degrees(math.atan2(d[1], d[0]))


def _v(p, z=None):
    return Vector((p[0], p[1], p[2] if z is None and len(p) > 2 else (z or 0.0)))


# ------------------------------------------------------------------------------------------------ noise


def _hash(ix, iy, iz, seed):
    n = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    n &= 0xFFFFFFFF
    n = (((n >> 16) ^ n) * 0x45D9F3B) & 0xFFFFFFFF
    n = (((n >> 16) ^ n) * 0x45D9F3B) & 0xFFFFFFFF
    n = (n >> 16) ^ n
    return (n & 0xFFFFFF) / float(0xFFFFFF)


def vnoise(x, y, z, seed=0):
    """Value noise in [0, 1] (smooth, deterministic)."""
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


def fbm(x, y, z, seed=0, octaves=4, gain=0.5):
    s, a, f, norm = 0.0, 1.0, 1.0, 0.0
    for o in range(octaves):
        s += a * vnoise(x * f, y * f, z * f, seed + o * 101)
        norm += a
        a *= gain
        f *= 2.03
    return s / norm


# ------------------------------------------------------------------------------------------------ walls and rooms


def wall_run(b, a, c, height, thick=0.3, openings=(), z0=0.0, mat=CONCRETE, tint=CONCRETE_T, solid=True, wear=None,
             vary=0.03):
    """A straight wall from `a` to `c` (2D, its base at `z0`) with rectangular openings, each (centre along the wall
    from `a`, width, height, sill). Returns the openings' centres at their sills."""
    a = Vector((a[0], a[1], z0))
    c = Vector((c[0], c[1], z0))
    d = c - a
    length = d.length
    if length < 1e-3:
        return []
    u = d / length
    yaw = _yaw(d)
    out = []

    def part(s0, s1, lo, hi):
        if s1 - s0 < 0.02 or hi - lo < 0.02:
            return
        m = a + u * ((s0 + s1) / 2)
        b.box((s1 - s0, thick, hi - lo), (m.x, m.y, z0 + (lo + hi) / 2), (0, 0, yaw), mat=mat, tint=tint,
              solid=solid, wear=wear, vary=vary)

    s = 0.0
    for oc, ow, oh, sill in sorted(openings, key=lambda o: o[0]):
        o0, o1 = max(s, oc - ow / 2), min(length, oc + ow / 2)
        part(s, o0, 0.0, height)
        part(o0, o1, min(height, sill + oh), height)
        if sill > 0:
            part(o0, o1, 0.0, sill)
        p = a + u * oc
        out.append(Vector((p.x, p.y, z0 + sill)))
        s = max(s, o1)
    part(s, length, 0.0, height)
    return out


def room(b, at, rot, w, d, h, thick=0.35, openings=None, mat=CONCRETE, tint=CONCRETE_T, roof=True, floor=True,
         roof_mat=None, roof_tint=None, floor_tint=None, roof_thick=None):
    """A walled room `w` x `d` (outside) and `h` high, its front toward -Y. `openings`: {side: [(offset from the
    side's centre along +X or +Y, width, height, sill)]} for sides front, back, left, right."""
    openings = openings or {}
    t = thick
    with b.frame(at, rot):
        hw, hd = w / 2, d / 2

        def ops(side, length):
            return [(length / 2 + o, ow, oh, sill) for o, ow, oh, sill in openings.get(side, [])]

        wall_run(b, (-hw, -hd + t / 2), (hw, -hd + t / 2), h, t, ops("front", w), mat=mat, tint=tint)
        wall_run(b, (-hw, hd - t / 2), (hw, hd - t / 2), h, t, ops("back", w), mat=mat, tint=tint)
        wall_run(b, (-hw + t / 2, -hd + t), (-hw + t / 2, hd - t), h, t, ops("left", d - 2 * t), mat=mat, tint=tint)
        wall_run(b, (hw - t / 2, -hd + t), (hw - t / 2, hd - t), h, t, ops("right", d - 2 * t), mat=mat, tint=tint)
        if floor:
            b.box((w - 0.05, d - 0.05, 0.12), (0, 0, 0.0), mat=CONCRETE, tint=floor_tint or srgb(0.62, 0.61, 0.58))
        if roof:
            rt = roof_thick or t
            b.box((w + 0.3, d + 0.3, rt), (0, 0, h + rt / 2), mat=roof_mat or mat, tint=roof_tint or tint)


def deck(b, x0, y0, x1, y1, z, thick=0.1, mat=GRATE, tint=GREY, rails="", gaps=None, hole=None, rail_tint=YELLOW,
         edge=True, solid=True, floor=True):
    """A floor (x0, y0)-(x1, y1) with its top at `z`, rails along the sides named in `rails` ('s' y0, 'n' y1, 'w' x0,
    'e' x1) except the `gaps` ({side: [(from, to)]} along the side's axis) and a `hole` (hx0, hy0, hx1, hy1) left
    open (a stair well)."""
    def slab(a0, b0, a1, b1):
        if floor and a1 - a0 > 0.05 and b1 - b0 > 0.05:
            b.box((a1 - a0, b1 - b0, thick), ((a0 + a1) / 2, (b0 + b1) / 2, z - thick / 2), mat=mat, tint=tint,
                  solid=solid, vary=0)

    if hole:
        hx0, hy0, hx1, hy1 = hole
        slab(x0, y0, x1, hy0)
        slab(x0, hy1, x1, y1)
        slab(x0, hy0, hx0, hy1)
        slab(hx1, hy0, x1, hy1)
    else:
        slab(x0, y0, x1, y1)
    if edge:
        for (a0, b0), (a1, b1) in (((x0, y0), (x1, y0)), ((x0, y1), (x1, y1)), ((x0, y0), (x0, y1)),
                                   ((x1, y0), (x1, y1))):
            b.beam((a0, b0, z - thick - 0.15), (a1, b1, z - thick - 0.15), 0.12, 0.3, mat=PAINT, tint=DARK_STEEL)
    gaps = gaps or {}
    sides = {"s": ((x0, y0), (x1, y0), 0), "n": ((x0, y1), (x1, y1), 0), "w": ((x0, y0), (x0, y1), 1),
             "e": ((x1, y0), (x1, y1), 1)}
    for s in rails:
        (pa, pb, axis) = sides[s]
        lo, hi = pa[axis], pb[axis]
        cuts = sorted(gaps.get(s, []))
        cur = lo
        spans = []
        for g0, g1 in cuts:
            if g0 > cur:
                spans.append((cur, min(g0, hi)))
            cur = max(cur, g1)
        if cur < hi:
            spans.append((cur, hi))
        for s0, s1 in spans:
            if s1 - s0 < 0.3:
                continue
            if axis == 0:
                railing(b, (s0, pa[1], z), (s1, pa[1], z), tint=rail_tint)
            else:
                railing(b, (pa[0], s0, z), (pa[0], s1, z), tint=rail_tint)


def switchback(b, at, rot, height, width=1.2, max_rise=3.0, landing=1.4, tint=YELLOW, top_landing=False,
               rails=True, flights=None, steps=None):
    """Switchback stairs from `at` climbing `height`: flights alternate north (+Y, west column) and south (east
    column), with landings at both ends. The first flight starts at y = 0 in the west column (x < 0); an even number
    of flights ends on the south landing (y < 0), an odd number on the north one. Returns (width, length, run): the
    well spans x ±width/2 and y from -landing to run + landing."""
    flights = flights or max(1, math.ceil(height / max_rise - 1e-6))
    rise = height / flights
    steps = steps or max(2, int(round(rise / 0.19)))
    run = steps * 0.27
    xa, xb = -(width / 2 + 0.08), width / 2 + 0.08
    W = 2 * width + 0.16
    with b.frame(at, rot):
        for i in range(flights):
            z = i * rise
            last = i == flights - 1
            if i % 2 == 0:
                stairs(b, (xa, 0, z), (0, 0, 0), width, rise, run, tint=tint, rails=rails)
                ly0, ly1 = run, run + landing
            else:
                stairs(b, (xb, run, z), (0, 0, 180), width, rise, run, tint=tint, rails=rails)
                ly0, ly1 = -landing, 0.0
            if not last or top_landing:
                zt = z + rise
                b.box((W, landing, 0.08), (0, (ly0 + ly1) / 2, zt - 0.04), mat=GRATE, tint=GREY, vary=0)
                b.beam((-W / 2, ly0, zt - 0.2), (-W / 2, ly1, zt - 0.2), 0.1, 0.2, mat=PAINT, tint=DARK_STEEL)
                b.beam((W / 2, ly0, zt - 0.2), (W / 2, ly1, zt - 0.2), 0.1, 0.2, mat=PAINT, tint=DARK_STEEL)
                if rails:
                    yo = ly1 if i % 2 == 0 else ly0
                    railing(b, (-W / 2, yo, zt), (W / 2, yo, zt), tint=tint)
        # Posts carrying the landings.
        for x in (-W / 2, W / 2):
            for y in (-landing, run + landing):
                b.box((0.12, 0.12, height), (x, y, height / 2), mat=PAINT, tint=DARK_STEEL, solid=False, vary=0)
    return W, run + 2 * landing, run


def stair_tower(b, at, rot, levels, width=1.2, tint=YELLOW, clad=None, clad_tint=LIGHTGREY, frame_tint=None,
                steps=12):
    """An external stair tower: four switchback flights between each pair of `levels` (heights above `at`), a landing
    at every level on the south side (y from -1.4 to 0, x within the well), a braced steel frame, a roof and an
    optional cladding skin on the back and sides. Returns the frame's footprint (x0, y0, x1, y1)."""
    frame_tint = frame_tint or DARK_STEEL
    with b.frame(at, rot):
        W = run = 0.0
        for z0, z1 in zip(levels, levels[1:]):
            W, L, run = switchback(b, (0, 0, z0), (0, 0, 0), z1 - z0, width, flights=4, steps=steps, tint=tint,
                                   top_landing=True)
        height = levels[-1]
        x0, x1 = -W / 2 - 0.3, W / 2 + 0.3
        y0, y1 = -1.4 - 0.3, run + 1.4 + 0.3
        for x in (x0, x1):
            for y in (y0, y1):
                b.box((0.3, 0.3, height + 2.6), (x, y, (height + 2.6) / 2), mat=PAINT, tint=frame_tint)
        for z0, z1 in zip(levels, levels[1:]):
            for (pa, pb) in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
                b.beam((pa[0], pa[1], z1), (pb[0], pb[1], z1), 0.2, 0.3, mat=PAINT, tint=frame_tint)
            # Diagonals rise toward the south so the landings stay clear.
            b.beam((x0, y1, z0), (x1, y1, z1), 0.12, mat=PAINT, tint=frame_tint)
            b.beam((x0, y1, z0), (x0, y0, z1), 0.12, mat=PAINT, tint=frame_tint)
            b.beam((x1, y1, z0), (x1, y0, z1), 0.12, mat=PAINT, tint=frame_tint)
        b.box((x1 - x0 + 0.4, y1 - y0 + 0.4, 0.2), (0, (y0 + y1) / 2, height + 2.7), mat=CORRUGATED, tint=clad_tint)
        if clad:
            for x in (x0 - 0.15, x1 + 0.15):
                b.box((0.08, y1 - y0, height + 2.4), (x, (y0 + y1) / 2, (height + 2.4) / 2), mat=clad,
                      tint=clad_tint)
            b.box((x1 - x0, 0.08, height + 2.4), (0, y1 + 0.15, (height + 2.4) / 2), mat=clad, tint=clad_tint)
    return x0, y0, x1, y1


def steel_frame(b, at, rot, w, d, levels, col=0.45, tint=DARK_STEEL, brace=("s", "n", "w", "e"), bays=(1, 1),
                beam=(0.3, 0.45), brace_w=0.22, solid_cols=True):
    """A braced steel frame `w` x `d` centred on `at`: columns at the corners and every bay (`bays` = divisions along
    x and y), ring beams at every level in `levels` (heights), X bracing on the faces in `brace`."""
    nx, ny = bays
    with b.frame(at, rot):
        xs = [-w / 2 + w * i / nx for i in range(nx + 1)]
        ys = [-d / 2 + d * j / ny for j in range(ny + 1)]
        cols = [(x, y) for x in xs for y in ys if x in (xs[0], xs[-1]) or y in (ys[0], ys[-1])]
        for z0, z1 in zip(levels, levels[1:]):
            for x, y in cols:
                b.box((col, col, z1 - z0), (x, y, (z0 + z1) / 2), mat=PAINT, tint=tint, solid=solid_cols, vary=0.01)
            faces = {"s": [((xs[i], ys[0]), (xs[i + 1], ys[0])) for i in range(nx)],
                     "n": [((xs[i], ys[-1]), (xs[i + 1], ys[-1])) for i in range(nx)],
                     "w": [((xs[0], ys[j]), (xs[0], ys[j + 1])) for j in range(ny)],
                     "e": [((xs[-1], ys[j]), (xs[-1], ys[j + 1])) for j in range(ny)]}
            for f, segs in faces.items():
                for pa, pb in segs:
                    b.beam((pa[0], pa[1], z1), (pb[0], pb[1], z1), beam[0], beam[1], mat=PAINT, tint=tint)
                    if f in brace:
                        b.beam((pa[0], pa[1], z0), (pb[0], pb[1], z1), brace_w, mat=PAINT, tint=tint)
                        b.beam((pb[0], pb[1], z0), (pa[0], pa[1], z1), brace_w, mat=PAINT, tint=tint)


def truss(b, a, c, w=1.6, h=1.6, chord=0.2, web=0.12, tint=DARK_STEEL, panels=None, deck_rails=False,
          deck_tint=GREY):
    """A box truss from `a` to `c` (its bottom centre line): four chords, verticals and diagonals; `deck_rails` adds
    a grated walkway on the bottom chords with rails (a bridge or a swing arm)."""
    a, c = Vector(a), Vector(c)
    dvec = c - a
    length = dvec.length
    yaw = _yaw(dvec)
    pitch = math.degrees(math.atan2(dvec.z, math.hypot(dvec.x, dvec.y)))
    n = panels or max(1, int(round(length / max(h, 1.0))))
    with b.frame(tuple(a), (0, -pitch, yaw)):
        for y in (-w / 2, w / 2):
            for z in (0.0, h):
                b.beam((0, y, z), (length, y, z), chord, mat=PAINT, tint=tint)
        for i in range(n + 1):
            x = length * i / n
            for y in (-w / 2, w / 2):
                b.beam((x, y, 0), (x, y, h), web, mat=PAINT, tint=tint)
            b.beam((x, -w / 2, h), (x, w / 2, h), web, mat=PAINT, tint=tint)
            b.beam((x, -w / 2, 0), (x, w / 2, 0), web, mat=PAINT, tint=tint)
            if i < n:
                x1 = length * (i + 1) / n
                for y in (-w / 2, w / 2):
                    if i % 2 == 0:
                        b.beam((x, y, 0), (x1, y, h), web, mat=PAINT, tint=tint)
                    else:
                        b.beam((x, y, h), (x1, y, 0), web, mat=PAINT, tint=tint)
                b.beam((x, -w / 2, h), (x1, w / 2, h), web * 0.8, mat=PAINT, tint=tint)
        if deck_rails:
            b.box((length, w - 0.1, 0.06), (length / 2, 0, 0.12), mat=GRATE, tint=deck_tint, vary=0)
            for y in (-w / 2 + 0.05, w / 2 - 0.05):
                railing(b, (0, y, 0.15), (length, y, 0.15), tint=YELLOW)


# ------------------------------------------------------------------------------------------------ buildings


def hall(b, at, rot, w, d, h, bay=7.5, plinth=3.0, roof="flat", pitch=7.0, clad=CORRUGATED, clad_tint=LIGHTGREY,
         plinth_mat=FORMWORK, plinth_tint=CONCRETE_T, frame_tint=DARK_STEEL, roof_tint=None, ruin=0.1, doors=None,
         windows=("front", "back"), skylights=True, parapet=1.0, panel=2.6, patch=0.12, patch_tints=None,
         band=None, downpipes=True, win_h=1.8, open_sides=()):
    """A steel-framed industrial hall `w` x `d` with its eaves at `h`: a slab, a concrete plinth wall, columns every
    `bay` metres, cladding panels above the plinth (some missing with `ruin`, some patched), a band of clerestory
    windows on the sides in `windows`, roof girders and purlins, a flat roof (parapet, skylight strips) or a gable
    roof. `doors`: {side: [(offset from the side's centre, width, height, kind)]}, kind "shutter" (a roller door,
    part open), "open" (a gap) or "door" (a personnel door). Doors are marked."""
    doors = doors or {}
    roof_tint = roof_tint or clad_tint
    patch_tints = patch_tints or [srgb(0.55, 0.57, 0.55), srgb(0.62, 0.50, 0.36), srgb(0.70, 0.71, 0.70)]
    nx = max(1, int(round(w / bay)))
    ny = max(1, int(round(d / bay)))
    z0 = 0.3
    top = z0 + h
    with b.frame(at, rot):
        hw, hd = w / 2, d / 2
        b.box((w + 1.0, d + 1.0, 0.3), (0, 0, 0.15), mat=CONCRETE, tint=srgb(0.66, 0.65, 0.62))
        for i in range(nx + 1):
            x = -hw + w * i / nx
            for y in (-hd, hd):
                b.box((0.5, 0.6, h), (x, y, z0 + h / 2), mat=PAINT, tint=frame_tint, vary=0.01)
        for j in range(1, ny):
            y = -hd + d * j / ny
            for x in (-hw, hw):
                b.box((0.6, 0.5, h), (x, y, z0 + h / 2), mat=PAINT, tint=frame_tint, vary=0.01)
        # Roof structure: a girder over every column line, purlins between.
        if roof == "flat":
            for i in range(nx + 1):
                x = -hw + w * i / nx
                b.box((0.4, d, 1.4), (x, 0, top - 0.7), mat=PAINT, tint=frame_tint, solid=False, vary=0)
            for k in range(2 * ny + 1):
                y = -hd + d * k / (2 * ny)
                b.box((w, 0.18, 0.3), (0, y, top + 0.05), mat=PAINT, tint=frame_tint, solid=False, vary=0)
        rise = math.tan(math.radians(pitch)) * hd if roof == "gable" else 0.0
        if roof == "gable":
            for i in range(nx + 1):
                x = -hw + w * i / nx
                b.beam((x, -hd, top), (x, 0, top + rise), 0.35, 0.8, mat=PAINT, tint=frame_tint)
                b.beam((x, hd, top), (x, 0, top + rise), 0.35, 0.8, mat=PAINT, tint=frame_tint)
                b.beam((x, -hd * 0.95, top - 0.2), (x, hd * 0.95, top - 0.2), 0.12, mat=PAINT, tint=frame_tint)

        # Walls: plinth and cladding, side by side.
        sides = {"front": ((-hw - 0.5, -hd - 0.35), (hw + 0.5, -hd - 0.35)),
                 "back": ((-hw - 0.5, hd + 0.35), (hw + 0.5, hd + 0.35)),
                 "left": ((-hw - 0.35, -hd - 0.15), (-hw - 0.35, hd + 0.15)),
                 "right": ((hw + 0.35, -hd - 0.15), (hw + 0.35, hd + 0.15))}
        for side, (pa, pb) in sides.items():
            pa, pb = Vector((pa[0], pa[1], 0)), Vector((pb[0], pb[1], 0))
            length = (pb - pa).length
            u = (pb - pa) / length
            yaw = _yaw(u)
            if side == "front":
                out = Vector((0, -1, 0))
            elif side == "back":
                out = Vector((0, 1, 0))
            elif side == "left":
                out = Vector((-1, 0, 0))
            else:
                out = Vector((1, 0, 0))
            ds = [(length / 2 + o, dw, dh, kind) for o, dw, dh, kind in doors.get(side, [])]
            if side in open_sides:
                continue
            wall_run(b, pa, pb, plinth, 0.35, [(s, dw, dh, 0.0) for s, dw, dh, _ in ds], z0=z0, mat=plinth_mat,
                     tint=plinth_tint)
            gable_h = rise if (roof == "gable" and side in ("left", "right")) else 0.0
            n = max(1, int(round(length / panel)))
            win = side in windows
            for k in range(n):
                s0, s1 = length * k / n, length * (k + 1) / n
                mid = pa + u * ((s0 + s1) / 2)
                lo = z0 + plinth
                hi = top
                # Over a door the cladding starts above the door.
                for s, dw, dh, _ in ds:
                    if s1 > s - dw / 2 and s0 < s + dw / 2:
                        lo = max(lo, z0 + dh + 0.6)
                if gable_h:
                    t = 1.0 - abs((s0 + s1) / 2 - length / 2) / (length / 2)
                    hi = top + gable_h * t
                r = b.rng.random()
                if r < ruin:
                    # A missing panel; sometimes only its lower half.
                    if b.rng.random() < 0.5:
                        continue
                    lo = lo + (hi - lo) * 0.55
                tint = clad_tint
                mat = clad
                if b.rng.random() < patch:
                    tint = patch_tints[b.rng.randrange(len(patch_tints))]
                    if b.rng.random() < 0.25:
                        mat = RUST
                        tint = (1, 1, 1)
                p = mid + out * 0.0
                if win and hi - lo > win_h + 2.0:
                    wl, wh = top - win_h - 1.0, top - 1.0
                    for zl, zh in ((lo, wl), (wh, hi)):
                        if zh - zl > 0.05:
                            b.box((s1 - s0 + 0.02, 0.1, zh - zl), (p.x, p.y, (zl + zh) / 2), (0, 0, yaw), mat=mat,
                                  tint=tint)
                    gp = p - out * 0.02
                    if b.rng.random() > 0.3:
                        b.box((s1 - s0 - 0.1, 0.03, win_h), (gp.x, gp.y, (wl + wh) / 2), (0, 0, yaw), mat=GLASS,
                              solid=False, vary=0)
                    fp = p + out * 0.06
                    b.box((0.1, 0.12, win_h), (fp.x + u.x * (s1 - s0) / 2, fp.y + u.y * (s1 - s0) / 2,
                                                (wl + wh) / 2), (0, 0, yaw), mat=PAINT, tint=DARK, solid=False,
                          vary=0)
                    b.collide_box((gp.x, gp.y, (wl + wh) / 2), (s1 - s0, 0.1, win_h), (0, 0, yaw))
                elif hi - lo > 0.05:
                    b.box((s1 - s0 + 0.02, 0.1, hi - lo), (p.x, p.y, (lo + hi) / 2), (0, 0, yaw), mat=mat, tint=tint)
            # Fascia band and gutter along the top of the long sides.
            if not gable_h:
                fz = top - 0.6
                fp = pa + u * (length / 2) + out * 0.12
                b.box((length, 0.12, 1.2), (fp.x, fp.y, fz), (0, 0, yaw), mat=PAINT, tint=band or frame_tint,
                      solid=False, vary=0)
                if downpipes:
                    for k in range(1, n, 6):
                        q = pa + u * (length * k / n) + out * 0.25
                        b.box((0.16, 0.16, top - 0.6), (q.x, q.y, (top - 0.6) / 2), mat=PAINT, tint=DARK_STEEL,
                              solid=False, vary=0)
            # Doors.
            for s, dw, dh, kind in ds:
                c = pa + u * s
                if kind == "shutter":
                    hp = c + out * 0.25
                    b.box((dw + 0.6, 0.6, 0.7), (hp.x, hp.y, z0 + dh + 0.35), (0, 0, yaw), mat=PAINT, tint=DARK,
                          solid=False)
                    open_h = dh * (0.25 + 0.55 * b.rng.random())
                    lp = c + out * 0.1
                    b.box((dw, 0.1, dh - open_h), (lp.x, lp.y, z0 + open_h + (dh - open_h) / 2), (0, 0, yaw),
                          mat=CORRUGATED, tint=srgb(0.50, 0.52, 0.54))
                    stripes(b, (c - u * (dw / 2 + 0.6)) + out * 0.3, (c - u * dw / 2) + out * 0.3, 2.0, width=0.3,
                            at_z=z0, normal=tuple(out))
                elif kind == "door":
                    lp = c + out * 0.02
                    b.box((dw, 0.08, dh), (lp.x, lp.y, z0 + dh / 2), (0, 0, yaw), mat=PAINT, tint=TEAL, solid=False)
                    hp = c + out * 0.5
                    b.box((dw + 0.8, 1.0, 0.12), (hp.x, hp.y, z0 + dh + 0.4), (0, 0, yaw), mat=PAINT, tint=DARK,
                          solid=False)
                mp = c + out * 1.2
                b.marker("door", (mp.x, mp.y, z0), kind_of=kind, width=dw, height=dh)
        # Roof.
        if roof == "flat":
            strips = max(2, int(round(w / (panel * 1.6))))
            for k in range(strips):
                x = -hw + w * (k + 0.5) / strips
                sw = w / strips + 0.02
                if b.rng.random() < ruin * 0.8:
                    continue
                light = skylights and k % 4 == 2
                if light:
                    if b.rng.random() < 0.75:
                        b.box((sw * 0.8, d - 2.0, 0.12), (x, 0, top + 0.3), mat=GLASS, solid=False, vary=0)
                    for y in (-hd + 0.5, hd - 0.5):
                        b.box((sw, 1.0, 0.2), (x, y, top + 0.3), mat=CORRUGATED, tint=roof_tint)
                    continue
                b.box((sw, d + 0.6, 0.2), (x, 0, top + 0.3), mat=CORRUGATED, tint=roof_tint)
            if parapet:
                for y in (-hd - 0.45, hd + 0.45):
                    b.box((w + 1.3, 0.3, parapet + 0.4), (0, y, top + (parapet + 0.4) / 2), mat=plinth_mat,
                          tint=plinth_tint)
                for x in (-hw - 0.5, hw + 0.5):
                    b.box((0.3, d + 0.6, parapet + 0.4), (x, 0, top + (parapet + 0.4) / 2), mat=plinth_mat,
                          tint=plinth_tint)
        elif roof == "gable":
            strips = max(2, int(round(w / (panel * 1.6))))
            half = math.hypot(hd + 0.6, rise)
            ang = math.degrees(math.atan2(rise, hd))
            for k in range(strips):
                x = -hw - 0.4 + (w + 0.8) * (k + 0.5) / strips
                sw = (w + 0.8) / strips + 0.02
                for sgn in (-1, 1):
                    if b.rng.random() < ruin * 0.8:
                        continue
                    light = skylights and k % 4 == 2
                    mat = GLASS if light else CORRUGATED
                    b.box((sw, half, 0.12), (x, sgn * (hd + 0.6) / 2, top + 0.45 + rise / 2 - 0.05),
                          (-sgn * ang, 0, 0), mat=mat, tint=roof_tint, solid=not light)
            b.box((w + 1.0, 0.5, 0.3), (0, 0, top + 0.5 + rise), mat=PAINT, tint=frame_tint, solid=False)
    return top + (rise if roof == "gable" else parapet)


def blast_door(b, at, rot, w=4.0, h=3.8, depth=1.4, frame=1.2, open=0.0, tint=OLIVE, frame_mat=FORMWORK,
               frame_tint=BW_CONCRETE, chip=None, cell=False, kind="blast-door", reader=True):
    """A blast door facing -Y: a deep concrete portal round a `w` x `h` opening, a thick steel leaf slid `open`
    (0..1) to the side, hazard stripes on the lintel, a chip reader with its coloured lamp (and a power cell box when
    `cell`), and the door marker. The leaf is drawn but not solid: the game's door prop takes its place."""
    with b.frame(at, rot):
        for s in (-1, 1):
            b.box((frame, depth, h + frame), (s * (w / 2 + frame / 2), 0, (h + frame) / 2), mat=frame_mat,
                  tint=frame_tint, bevel=0.04)
        b.box((w + 2 * frame, depth, frame), (0, 0, h + frame / 2), mat=frame_mat, tint=frame_tint, bevel=0.04)
        lx = open * w * 0.95
        b.box((w + 0.2, 0.5, h + 0.1), (lx, 0.1, h / 2), mat=PAINT, tint=tint, bevel=0.04, solid=False)
        for z in (h * 0.22, h * 0.5, h * 0.78):
            b.box((w * 0.86, 0.1, 0.22), (lx, -0.2, z), mat=PAINT, tint=tint, solid=False, vary=0)
        for x in (-w * 0.3, w * 0.3):
            b.box((0.22, 0.1, h * 0.9), (lx + x, -0.2, h / 2), mat=PAINT, tint=tint, solid=False, vary=0)
        stripes(b, (-w / 2 - frame, -depth / 2 - 0.01, 0), (w / 2 + frame, -depth / 2 - 0.01, 0), frame * 0.5,
                width=0.45, at_z=h + frame * 0.25)
        stripes(b, (-w / 2, -0.3, 0), (w / 2, -0.3, 0), 0.35, width=0.35, at_z=0.05)
        if reader:
            rx = w / 2 + frame * 0.5
            b.box((0.32, 0.14, 0.5), (rx, -depth / 2 - 0.07, 1.4), mat=PAINT, tint=DARK, solid=False, vary=0)
            colour = CHIP.get(chip, srgb(0.3, 0.9, 0.3))
            b.box((0.2, 0.03, 0.08), (rx, -depth / 2 - 0.15, 1.55), mat=LAMP, tint=colour, solid=False, vary=0)
            if cell:
                fuse_box(b, (-w / 2 - frame * 0.5, -depth / 2 - 0.1, 1.1))
        props = {"kind_of": kind, "width": w, "height": h}
        if chip:
            props["chip"] = chip
        if cell:
            props["cell"] = True
        b.marker("door", (0, -depth / 2 - 0.6, 0), **props)


def fuse_box(b, at, rot=(0, 0, 0)):
    """A power cell slot on a wall (its face toward -Y): a grey box, a yellow band, the marker."""
    with b.frame(at, rot):
        b.box((0.5, 0.2, 0.7), (0, 0, 0.35), mat=PAINT, tint=srgb(0.55, 0.57, 0.56), bevel=0.02, solid=False)
        b.box((0.5, 0.02, 0.1), (0, -0.11, 0.6), mat=PAINT, tint=YELLOW, solid=False, vary=0)
        b.marker("fuse-box", (0, -0.3, 0.3), takes="power-cell")


def loopholes(b, a, c, z, count, w=0.9, h=0.35, depth=0.6, tint=BLACK):
    """Dark gun slits along a wall face from `a` to `c` at height `z` (drawn as recessed dark boxes)."""
    a, c = Vector(a), Vector(c)
    d = c - a
    yaw = _yaw(d)
    for i in range(count):
        p = a + d * ((i + 0.5) / count)
        b.box((w, depth, h), (p.x, p.y, z), (0, 0, yaw), mat=MATTE, tint=tint, solid=False, vary=0)


# ------------------------------------------------------------------------------------------------ industry


def pipe_rack(b, points, width=4.0, height=6.0, bent=7.0, pipes=None, tint=DARK_STEEL, tray=True, z=0.0):
    """A pipe rack along `points` (2D, on the ground at `z`): portal bents every `bent` metres, pipes and a cable
    tray on top. `pipes`: [(radius, tint)] across the rack."""
    pipes = pipes or [(0.45, OFFWHITE), (0.3, GREY), (0.25, YELLOW), (0.35, OFFWHITE)]
    pts = [Vector((p[0], p[1], z)) for p in points]
    norms = []
    for i in range(len(pts)):
        segs = []
        if i > 0:
            segs.append((pts[i] - pts[i - 1]).normalized())
        if i < len(pts) - 1:
            segs.append((pts[i + 1] - pts[i]).normalized())
        ns = [Vector((-s.y, s.x, 0)) for s in segs]
        n = sum(ns, Vector()).normalized()
        k = 1.0 / max(0.5, n.dot(ns[0]))
        norms.append(n * k)
    for si, (a, c) in enumerate(zip(pts, pts[1:])):
        d = c - a
        n = max(1, int(round(d.length / bent)))
        side = Vector((-d.y, d.x, 0)).normalized()
        for i in range(0 if si == 0 else 1, n + 1):
            p = a + d * (i / n)
            for s in (-1, 1):
                q = p + side * (s * width / 2)
                b.box((0.35, 0.35, height), (q.x, q.y, z + height / 2), mat=PAINT, tint=tint, vary=0)
            l, r = p - side * (width / 2 + 0.3), p + side * (width / 2 + 0.3)
            b.beam(l + UP * height, r + UP * height, 0.3, 0.4, mat=PAINT, tint=tint)
            b.beam(l + UP * (height * 0.55), r + UP * (height * 0.55), 0.22, 0.3, mat=PAINT, tint=tint)
        b.beam(a + side * (width / 2) + UP * height, c + side * (width / 2) + UP * height, 0.2, 0.3, mat=PAINT,
               tint=tint)
        b.beam(a - side * (width / 2) + UP * height, c - side * (width / 2) + UP * height, 0.2, 0.3, mat=PAINT,
               tint=tint)
    for k, (r, ptint) in enumerate(pipes):
        off = -width / 2 + 0.6 + k * (width - 1.2) / max(1, len(pipes) - 1)
        line = [p + nn * off + UP * (height + 0.2 + r) for p, nn in zip(pts, norms)]
        b.tube(line, r, mat=PAINT, tint=ptint, sides=12 if r < 0.4 else 16)
    if tray:
        line = [p + nn * (width / 2 - 0.4) + UP * (height * 0.55 + 0.3) for p, nn in zip(pts, norms)]
        for a, c in zip(line, line[1:]):
            d = c - a
            m = (a + c) / 2
            b.box((d.length, 0.6, 0.12), (m.x, m.y, m.z), (0, 0, _yaw(d)), mat=GRATE, tint=GREY, solid=False, vary=0)


def sphere_tank(b, at, r, legs=10, tint=OFFWHITE, band=None, code=None, code_tint=BLACK, ladder_on=True):
    """A spherical pressure tank on legs with cross bracing, an equator catwalk and a ladder to it."""
    x, y, z0 = at
    zc = z0 + r * 1.15
    prof = [(0.0, -r)]
    for k in range(1, 12):
        t = math.pi * k / 12
        prof.append((r * math.sin(t), -r * math.cos(t)))
    prof.append((0.0, r))
    b.lathe(prof, (x, y, zc), mat=PAINT, tint=tint, sides=40, solid="hull")
    if band:
        b.cyl(r + 0.03, r * 0.16, (x, y, zc + r * 0.45), mat=PAINT, tint=band, sides=40, radius2=r * 0.86,
              solid=False, vary=0)
    for k in range(legs):
        a0 = 2 * math.pi * k / legs
        a1 = 2 * math.pi * (k + 1) / legs
        p0 = (x + math.cos(a0) * r, y + math.sin(a0) * r)
        p1 = (x + math.cos(a1) * r, y + math.sin(a1) * r)
        b.box((0.7, 0.7, zc - z0), (p0[0], p0[1], z0 + (zc - z0) / 2), (0, 0, math.degrees(a0)), mat=PAINT,
              tint=tint, vary=0)
        b.box((1.4, 1.4, 0.6), (p0[0], p0[1], z0 + 0.3), mat=CONCRETE, tint=CONCRETE_T)
        b.beam((p0[0], p0[1], z0 + 0.8), (p1[0], p1[1], zc - r * 0.35), 0.18, mat=PAINT, tint=DARK_STEEL)
        b.beam((p1[0], p1[1], z0 + 0.8), (p0[0], p0[1], zc - r * 0.35), 0.18, mat=PAINT, tint=DARK_STEEL)
    # Equator catwalk.
    rr = r + 0.9
    pts = [(x + math.cos(2 * math.pi * k / 24) * rr, y + math.sin(2 * math.pi * k / 24) * rr, zc)
           for k in range(25)]
    for p, q in zip(pts, pts[1:]):
        m = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2)
        L = math.hypot(q[0] - p[0], q[1] - p[1])
        b.box((L + 0.05, 1.0, 0.06), (m[0], m[1], zc - 0.03), (0, 0, _yaw((q[0] - p[0], q[1] - p[1]))), mat=GRATE,
              tint=GREY, vary=0)
        po = (x + (p[0] - x) * (rr + 0.5) / rr, y + (p[1] - y) * (rr + 0.5) / rr, zc)
        qo = (x + (q[0] - x) * (rr + 0.5) / rr, y + (q[1] - y) * (rr + 0.5) / rr, zc)
        railing(b, po, qo, spacing=2.2, toe=False)
    if ladder_on:
        lx, ly = x, y - rr - 0.6
        ladder(b, (lx, ly, z0), zc - z0, (0, 0, 0), cage=True)
    if code:
        stencil(b, code, (x - r * 0.35, y - r * 1.0 - 0.05, zc - r * 0.55), (0, 0, 0), r * 0.4, tint=code_tint)


def bullet_tank(b, at, rot, r, length, tint=OFFWHITE, band=None, saddles=3):
    """A horizontal pressure vessel ("bullet") on concrete saddles, with rounded heads, a manway and valves."""
    with b.frame(at, rot):
        zc = r + 1.2
        b.cyl(r, length, (0, 0, zc), (0, 90, 0), mat=PAINT, tint=tint, sides=32)
        for s in (-1, 1):
            prof = [(r, 0.0), (r * 0.94, r * 0.25), (r * 0.75, r * 0.5), (r * 0.42, r * 0.68), (0.0, r * 0.75)]
            b.lathe(prof, (s * length / 2, 0, zc), (0, 90 * s, 0), mat=PAINT, tint=tint, sides=32)
        if band:
            b.cyl(r + 0.02, 0.8, (0, 0, zc), (0, 90, 0), mat=PAINT, tint=band, sides=32, solid=False, vary=0)
        for k in range(saddles):
            x = -length / 2 + length * (k + 0.5) / saddles
            b.box((1.0, r * 1.6, zc - r * 0.5), (x, 0, (zc - r * 0.5) / 2), mat=CONCRETE, tint=CONCRETE_T)
        b.cyl(0.45, 0.6, (length * 0.25, 0, zc + r + 0.2), mat=PAINT, tint=tint, sides=16, solid=False)
        b.cyl(0.12, 1.2, (-length * 0.2, 0, zc + r + 0.5), mat=PAINT, tint=RED, sides=8, solid=False)


def block(b, x0, x1, y0, y1, h, slope=None, z0=0.0, mat=CONCRETE, tint=CONCRETE_T, solid="hull", wear=None):
    """A raised block from `z0` to `z0 + h` over (x0, y0)-(x1, y1) at its top, its sides sloping out by
    `slope[side]` metres at the foot ('w', 'e', 's', 'n'; 0 = vertical)."""
    s = slope or {}
    bw, be, bs, bn = s.get("w", 0.0), s.get("e", 0.0), s.get("s", 0.0), s.get("n", 0.0)
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2
    verts = [(x0 - bw, y0 - bs, z0), (x1 + be, y0 - bs, z0), (x1 + be, y1 + bn, z0), (x0 - bw, y1 + bn, z0),
             (x0, y0, z0 + h), (x1, y0, z0 + h), (x1, y1, z0 + h), (x0, y1, z0 + h)]
    verts = [(vx - mx, vy - my, vz) for vx, vy, vz in verts]
    faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
    return b.mesh(verts, faces, (mx, my, 0.0), mat=mat, tint=tint, solid=solid, smooth=False, wear=wear)


def ramp(b, a, c, width, za, zc, mat=CONCRETE, tint=CONCRETE_T, wear=None):
    """A solid sloped ramp (a wedge standing on the ground) from `a` (2D, top at `za`) to `c` (top at `zc`)."""
    a, c = Vector((a[0], a[1], 0)), Vector((c[0], c[1], 0))
    d = c - a
    side = Vector((-d.y, d.x, 0)).normalized() * (width / 2)
    verts = []
    for p, zt in ((a, za), (c, zc)):
        for s in (-1, 1):
            q = p + side * s
            verts += [(q.x, q.y, 0.0), (q.x, q.y, max(zt, 0.02))]
    # 0 a-l bottom, 1 a-l top, 2 a-r bottom, 3 a-r top, 4 c-l bottom, 5 c-l top, 6 c-r bottom, 7 c-r top
    m = (a + c) / 2
    verts = [(vx - m.x, vy - m.y, vz) for vx, vy, vz in verts]
    faces = [[0, 2, 6, 4], [1, 5, 7, 3], [0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6]]
    return b.mesh(verts, faces, (m.x, m.y, 0.0), mat=mat, tint=tint, solid="hull", smooth=False, wear=wear)


def rock(b, centre, size, seed=1, rough=0.35, freq=1.7, rings=9, sides=14, mat=CONCRETE, tint=ROCK, solid="hull",
         ground=0.0, squash=0.0, wear=0.55):
    """A boulder or rock mass: an ellipsoid of full extents `size` roughened by 3D value noise, flattened where it
    meets the ground at `ground`."""
    cx, cy, cz = centre
    rx, ry, rz = size[0] / 2, size[1] / 2, size[2] / 2
    verts = []

    def disp(dx, dy, dz):
        n = fbm(dx * freq + seed * 3.1, dy * freq + seed * 1.7, dz * freq, seed, 4)
        ridge = 1.0 - abs(vnoise(dx * freq * 2.2, dy * freq * 2.2, dz * freq * 2.2, seed + 7) - 0.5) * 2
        return 1.0 + rough * ((n - 0.5) * 2.0 + (ridge - 0.5) * 0.4)

    def place(dx, dy, dz):
        k = disp(dx, dy, dz)
        z = cz + dz * rz * k * (1 - squash * max(0.0, dz))
        return (dx * rx * k, dy * ry * k, max(ground, z) - cz)

    verts.append(place(0, 0, 1))
    for i in range(1, rings):
        th = math.pi * i / rings
        for j in range(sides):
            ph = 2 * math.pi * j / sides + (0.5 * (i % 2)) * (2 * math.pi / sides)
            verts.append(place(math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th)))
    verts.append(place(0, 0, -1))
    faces = []
    last = len(verts) - 1
    for j in range(sides):
        faces.append([0, 1 + j, 1 + (j + 1) % sides])
    for i in range(rings - 2):
        r0 = 1 + i * sides
        r1 = r0 + sides
        for j in range(sides):
            j1 = (j + 1) % sides
            if i % 2 == 0:
                faces.append([r0 + j, r1 + j, r0 + j1])
                faces.append([r0 + j1, r1 + j, r1 + j1])
            else:
                faces.append([r0 + j, r1 + j, r1 + j1])
                faces.append([r0 + j, r1 + j1, r0 + j1])
    base = 1 + (rings - 2) * sides
    for j in range(sides):
        faces.append([base + j, last, base + (j + 1) % sides])
    return b.mesh(verts, faces, (cx, cy, cz), mat=mat, tint=tint, solid=solid, smooth=True, wear=wear)


def arc_wall(b, centre, r, a0, a1, z0, z1, thick=0.6, seg_deg=6.0, piece_deg=15.0, mat=PANEL, tint=OFFWHITE,
             solid=True, wear=None):
    """A curved wall on a circle of radius `r` (its outer face) round `centre`, from angle `a0` to `a1` (degrees)
    and from `z0` to `z1`, built in pieces of at most `piece_deg` so each piece's convex hull hugs the wall."""
    cx, cy = centre[0], centre[1]
    total = a1 - a0
    if total <= 0.01 or z1 - z0 <= 0.01:
        return
    pieces = max(1, int(math.ceil(total / piece_deg - 1e-6)))
    for k in range(pieces):
        b0 = a0 + total * k / pieces
        b1 = a0 + total * (k + 1) / pieces
        n = max(1, int(math.ceil((b1 - b0) / seg_deg - 1e-6)))
        am = math.radians((b0 + b1) / 2)
        ox, oy = cx + math.cos(am) * r, cy + math.sin(am) * r
        verts = []
        for i in range(n + 1):
            a = math.radians(b0 + (b1 - b0) * i / n)
            for rr in (r, r - thick):
                for zz in (z0, z1):
                    verts.append((cx + math.cos(a) * rr - ox, cy + math.sin(a) * rr - oy, zz))
        faces = []
        for i in range(n):
            o, q = i * 4, (i + 1) * 4
            faces += [[o, q, q + 1, o + 1], [o + 2, o + 3, q + 3, q + 2], [o + 1, q + 1, q + 3, o + 3],
                      [o, o + 2, q + 2, q]]
        faces += [[0, 1, 3, 2], [n * 4, n * 4 + 2, n * 4 + 3, n * 4 + 1]]
        b.mesh(verts, faces, (ox, oy, 0.0), mat=mat, tint=tint, solid="hull" if solid else False, smooth=True,
               wear=wear)


def drum(b, centre, r, z0, z1, openings=(), thick=0.8, mat=PANEL, tint=OFFWHITE, wear=None):
    """A round building's wall with openings: each (angle in degrees, width in metres, height)."""
    ops = sorted(openings)
    if not ops:
        arc_wall(b, centre, r, 0.0, 360.0, z0, z1, thick, mat=mat, tint=tint, wear=wear)
        return
    for k, (ang, w, h) in enumerate(ops):
        half = math.degrees(w / 2 / r)
        nxt_ang, nxt_w, _ = ops[(k + 1) % len(ops)]
        nxt_half = math.degrees(nxt_w / 2 / r)
        start = ang + half
        end = nxt_ang - nxt_half + (360.0 if k == len(ops) - 1 else 0.0)
        arc_wall(b, centre, r, start, end, z0, z1, thick, mat=mat, tint=tint, wear=wear)
        arc_wall(b, centre, r, ang - half, ang + half, z0 + h, z1, thick, mat=mat, tint=tint, wear=wear)


def cooling_shell(b, at, height=120.0, base=42.0, waist=26.0, top=29.0, thick=0.5, sides=56, rings=20,
                  breach=None, legs=36, tint=CONCRETE_T, stain=srgb(0.42, 0.41, 0.38), seed=1):
    """A hyperboloid cooling tower: a concrete shell `thick` deep on a ring of diagonal legs, its basin, a stained
    band under the rim. `breach` = (angle in degrees, half width in degrees, depth in metres) bites a jagged gap out
    of the rim (the shell's broken edges are closed). Returns the shell's radius function (of height 0..1)."""
    x, y, z = at
    leg_h = height * 0.08

    def radius(t):
        tw = 0.72
        if t < tw:
            k = t / tw
            return waist + (base - waist) * (1 - k) ** 2
        k = (t - tw) / (1 - tw)
        return waist + (top - waist) * k ** 2

    zs = [leg_h + (height - leg_h) * i / rings for i in range(rings + 1)]
    rs = [radius(i / rings) for i in range(rings + 1)]
    keep = [[True] * sides for _ in range(rings)]
    if breach:
        ac, hw, depth = math.radians(breach[0]), math.radians(breach[1]), breach[2]
        for j in range(sides):
            a = 2 * math.pi * (j + 0.5) / sides
            dist = abs(math.atan2(math.sin(a - ac), math.cos(a - ac)))
            if dist >= hw:
                continue
            jag = 0.65 + 0.7 * vnoise(j * 0.9, 0.0, 0.0, seed)
            d = depth * (1 - (dist / hw) ** 1.6) * jag
            for i in range(rings):
                if zs[i + 1] > height - d:
                    keep[i][j] = False

    def shell(ring0, ring1, off, shade, outer_only=False):
        verts = []
        for i in range(ring0, ring1 + 1):
            for j in range(sides):
                a = 2 * math.pi * j / sides
                for rr in (rs[i] + off, rs[i] - thick):
                    verts.append((math.cos(a) * rr, math.sin(a) * rr, zs[i]))

        def vi(i, j, inner):
            return ((i - ring0) * sides + (j % sides)) * 2 + (1 if inner else 0)

        faces = []
        for i in range(ring0, ring1):
            for j in range(sides):
                if not keep[i][j]:
                    continue
                faces.append([vi(i, j, 0), vi(i, j + 1, 0), vi(i + 1, j + 1, 0), vi(i + 1, j, 0)])
                if outer_only:
                    continue
                faces.append([vi(i, j, 1), vi(i + 1, j, 1), vi(i + 1, j + 1, 1), vi(i, j + 1, 1)])
                # Closing faces where the shell ends: the rim, the foot, the breach's edges.
                if i == ring1 - 1 or not keep[i + 1][j]:
                    faces.append([vi(i + 1, j, 0), vi(i + 1, j + 1, 0), vi(i + 1, j + 1, 1), vi(i + 1, j, 1)])
                if i == ring0 or not keep[i - 1][j]:
                    faces.append([vi(i, j, 0), vi(i, j, 1), vi(i, j + 1, 1), vi(i, j + 1, 0)])
                if not keep[i][(j - 1) % sides]:
                    faces.append([vi(i, j, 0), vi(i + 1, j, 0), vi(i + 1, j, 1), vi(i, j, 1)])
                if not keep[i][(j + 1) % sides]:
                    faces.append([vi(i, j + 1, 0), vi(i, j + 1, 1), vi(i + 1, j + 1, 1), vi(i + 1, j + 1, 0)])
        used = sorted({v for f in faces for v in f})
        remap = {v: k for k, v in enumerate(used)}
        return b.mesh([verts[v] for v in used], [[remap[v] for v in f] for f in faces], (x, y, z), mat=CONCRETE,
                      tint=shade, solid=False, smooth=True)

    shell(0, rings, 0.0, tint)
    if stain:
        # A darker skin on the top rings (weathered concrete under the rim), outer side only.
        shell(rings - max(1, rings // 7), rings, 0.04, stain, outer_only=True)
    for i in range(legs):
        a = 2 * math.pi * i / legs
        a2 = a + 2 * math.pi / legs
        r0 = base
        b.beam((x + math.cos(a) * r0, y + math.sin(a) * r0, z),
               (x + math.cos(a2) * (r0 - 0.5), y + math.sin(a2) * (r0 - 0.5), z + leg_h + 0.2), 0.6, 0.6,
               mat=CONCRETE, tint=tint, solid=True)
        b.beam((x + math.cos(a2) * r0, y + math.sin(a2) * r0, z),
               (x + math.cos(a) * (r0 - 0.5), y + math.sin(a) * (r0 - 0.5), z + leg_h + 0.2), 0.6, 0.6,
               mat=CONCRETE, tint=tint, solid=True)
    # The basin: a floor and a low ring wall round it.
    b.cyl(base - 1.0, 0.5, (x, y, z + 0.25), mat=CONCRETE, tint=srgb(0.45, 0.46, 0.44), sides=48)
    arc_wall(b, (x, y), base + 2.5, 0.0, 360.0, z, z + 1.4, thick=0.5, piece_deg=20.0, mat=CONCRETE,
             tint=CONCRETE_T)
    # Collision for the shell's lower part: thin boxes round it (the legs carry the rest).
    for k in range(24):
        a = 2 * math.pi * (k + 0.5) / 24
        rr = radius(0.15) - thick / 2
        b.collide_box((x + math.cos(a) * rr, y + math.sin(a) * rr, z + leg_h + 10.0),
                      (2 * math.pi * rr / 24, thick, 20.0), (0, 0, math.degrees(a) + 90))
    b.marker("landmark", (x, y, z + height), kind_of="cooling-tower")
    return radius


def panel_loft(b, rings, thick=0.6, mat=HULL, tint_fn=None, keep_fn=None, solid=True, ground=-1.0):
    """Hull plating lofted through `rings` (closed loops of points in the current frame, all the same count): one
    convex slab per panel, `thick` deep toward its ring's centre, so each plate is its own collision hull. Each panel
    takes `tint_fn(i, j)` and is left out where `keep_fn(i, j)` is False (holes, a ragged break); panels wholly
    below `ground` (monument frame) are skipped."""
    n = len(rings[0])
    centres = [sum((Vector(p) for p in ring), Vector()) / n for ring in rings]
    for i in range(len(rings) - 1):
        for j in range(n):
            if keep_fn and not keep_fn(i, j):
                continue
            outer = [Vector(rings[i][j]), Vector(rings[i][(j + 1) % n]), Vector(rings[i + 1][(j + 1) % n]),
                     Vector(rings[i + 1][j])]
            if ground is not None and max(b.world(q).z for q in outer) < ground:
                continue
            cs = (centres[i], centres[i], centres[i + 1], centres[i + 1])
            inner = []
            for q, c in zip(outer, cs):
                d = c - q
                inner.append(q + d.normalized() * min(thick, d.length * 0.5))
            mid = sum(outer, Vector()) / 4
            verts = [tuple(q - mid) for q in outer + inner]
            faces = [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]]
            tint = tint_fn(i, j) if tint_fn else OFFWHITE
            b.mesh(verts, faces, tuple(mid), mat=mat, tint=tint, solid="hull" if solid else False, smooth=False)


def ribs(b, ring, radius=0.35, inset=(0.8, 2.6, 4.4), axis=(1, 0, 0), mat=RUST, tint=(1, 1, 1), gaps=0.15):
    """Exposed frames where a hull broke: tubes following `ring` (a closed loop), repeated at the `inset`
    distances back along -`axis`, some lengths missing."""
    ax = Vector(axis).normalized()
    pts = [Vector(p) for p in ring]
    n = len(pts)
    for d in inset:
        run = []
        for j in range(n + 1):
            q = pts[j % n] - ax * d
            if b.rng.random() < gaps and len(run) > 1:
                b.tube([tuple(v) for v in run], radius, mat=mat, tint=tint, sides=6, cap=True, solid=False)
                run = []
                continue
            run.append(q)
        if len(run) > 1:
            b.tube([tuple(v) for v in run], radius, mat=mat, tint=tint, sides=6, cap=True, solid=False)


def skin(b, grid, inward, thick=3.0, mat=CONCRETE, tint_fn=None, keep_fn=None, solid=True, wear=0.55):
    """A rock or earth surface from a grid of points (rows of the same length): each cell a convex slab `thick`
    deep along `inward`, so the skin is its own exact collision and gaps (portals) are just cells left out with
    `keep_fn(i, j)`; `tint_fn(i, j)` colours each cell."""
    ins = Vector(inward).normalized() * thick
    for i in range(len(grid) - 1):
        for j in range(len(grid[0]) - 1):
            if keep_fn and not keep_fn(i, j):
                continue
            outer = [Vector(grid[i][j]), Vector(grid[i][j + 1]), Vector(grid[i + 1][j + 1]), Vector(grid[i + 1][j])]
            inner = [q + ins for q in outer]
            mid = sum(outer, Vector()) / 4
            verts = [tuple(q - mid) for q in outer + inner]
            faces = [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]]
            b.mesh(verts, faces, tuple(mid), mat=mat, tint=tint_fn(i, j) if tint_fn else ROCK,
                   solid="hull" if solid else False, smooth=True, wear=wear)


def tunnel(b, a, c, width=5.0, height=4.2, left=(), right=(), wall=0.6, mat=CONCRETE, tint=BW_CONCRETE,
           ribs=6.0, lamps=15.0, z0=0.0, ends=(False, False)):
    """A lined tunnel from `a` to `c` (2D, floor at `z0`): floor, walls, ceiling, steel arches every `ribs` metres,
    lamps along the ceiling, a cable tray and a pipe on the walls. `left`/`right`: openings in the walls as (distance
    from `a`, width, height); `ends` closes either end with a wall."""
    a = Vector((a[0], a[1], z0))
    c = Vector((c[0], c[1], z0))
    d = c - a
    length = d.length
    u = d / length
    side = Vector((-u.y, u.x, 0))
    yaw = _yaw(u)
    m = (a + c) / 2
    b.box((length, width + 2 * wall, 0.4), (m.x, m.y, z0 - 0.2), (0, 0, yaw), mat=mat, tint=srgb(0.55, 0.55, 0.52))
    b.box((length, width + 2 * wall, wall), (m.x, m.y, z0 + height + wall / 2), (0, 0, yaw), mat=mat, tint=tint)
    for sgn, ops in ((1, left), (-1, right)):
        off = side * sgn * (width / 2 + wall / 2)
        wall_run(b, a + off, c + off, height, wall, [(s, w, h, 0.0) for s, w, h in ops], z0=z0, mat=mat, tint=tint)
    for k, closed in enumerate(ends):
        if closed:
            p = a if k == 0 else c
            b.box((wall, width + 2 * wall, height), (p.x, p.y, z0 + height / 2), (0, 0, yaw), mat=mat, tint=tint)
    if ribs:
        for k in range(1, int(length / ribs)):
            p = a + u * (k * ribs)
            for sgn in (-1, 1):
                q = p + side * sgn * (width / 2 - 0.12)
                b.box((0.2, 0.25, height - 0.1), (q.x, q.y, z0 + height / 2), (0, 0, yaw), mat=PAINT, tint=DARK_STEEL,
                      solid=False, vary=0)
            b.box((0.2, width - 0.1, 0.25), (p.x, p.y, z0 + height - 0.15), (0, 0, yaw), mat=PAINT, tint=DARK_STEEL,
                  solid=False, vary=0)
    if lamps:
        for k in range(int(length / lamps)):
            p = a + u * ((k + 0.5) * lamps)
            lamp(b, (p.x, p.y, z0 + height - 0.3), (90, 0, yaw), colour=WARM_LAMP, size=(0.5, 0.25, 0.12))
    tray = side * (width / 2 - 0.3)
    b.box((length, 0.5, 0.08), (m.x + tray.x, m.y + tray.y, z0 + height - 0.8), (0, 0, yaw), mat=GRATE, tint=GREY,
          solid=False, vary=0)
    pp = side * -(width / 2 - 0.35)
    b.tube([tuple(a + pp + UP * (height - 1.0)), tuple(c + pp + UP * (height - 1.0))], 0.18, mat=PAINT,
           tint=srgb(0.55, 0.45, 0.30), sides=8)


# ------------------------------------------------------------------------------------------------ wear and dressing


def scorch(b, at, radius, tint=SOOT, blobs=4, z=None):
    """A burn mark on the ground (or a deck at `at`'s height): dark overlapping discs."""
    x, y, zz = at
    for k in range(blobs):
        a = b.rng.random() * math.tau
        r = radius * (0.25 + 0.45 * b.rng.random())
        o = radius * 0.45 * b.rng.random()
        b.cyl(r, 0.02, (x + math.cos(a) * o, y + math.sin(a) * o, zz + 0.012 + 0.004 * k), mat=MATTE, tint=tint,
              sides=14, solid=False, vary=0.15, wear=0.9)


def scrap(b, centre, radius, count=12, max_size=2.0, tints=None, z=0.0):
    """Scattered metal junk: bent sheets, beams, pipe ends and boxes lying round a point."""
    tints = tints or [GREY, RUSTY, OLIVE, LIGHTGREY, DARK]
    cx, cy = centre[0], centre[1]
    for _ in range(count):
        a = b.rng.random() * math.tau
        r = radius * math.sqrt(b.rng.random())
        p = (cx + math.cos(a) * r, cy + math.sin(a) * r)
        s = max_size * (0.3 + 0.7 * b.rng.random())
        t = tints[b.rng.randrange(len(tints))]
        k = b.rng.random()
        yaw = b.rng.uniform(0, 360)
        if k < 0.4:
            b.box((s, s * 0.6, 0.05), (p[0], p[1], z + 0.1 + s * 0.1), (b.rng.uniform(-25, 25),
                                                                       b.rng.uniform(-25, 25), yaw),
                  mat=CORRUGATED if b.rng.random() < 0.5 else PAINT, tint=t, solid=False, vary=0.1)
        elif k < 0.65:
            b.box((s * 1.6, 0.22, 0.32), (p[0], p[1], z + 0.16), (0, b.rng.uniform(-8, 8), yaw), mat=PAINT,
                  tint=t, solid=s > 1.2, vary=0.1)
        elif k < 0.82:
            b.cyl(0.12 + 0.15 * b.rng.random(), s * 1.4, (p[0], p[1], z + 0.25), (90, 0, yaw), mat=RUST,
                  tint=(1, 1, 1), sides=10, solid=False)
        else:
            b.box((s * 0.5, s * 0.4, s * 0.4), (p[0], p[1], z + s * 0.2), (0, 0, yaw), mat=PAINT, tint=t,
                  bevel=0.03, solid=s > 1.0, vary=0.1)


def crates(b, at, rot=(0, 0, 0), n=4, tint=OLIVE, size=(1.2, 0.8, 0.8)):
    """A stack of supply crates (dressing; loot goes on markers)."""
    with b.frame(at, rot):
        for i in range(n):
            x = (i % 3) * (size[0] + 0.1) - size[0]
            z = (i // 3) * size[2]
            b.box(size, (x, 0, z + size[2] / 2), (0, 0, b.rng.uniform(-4, 4)), mat=PAINT, tint=tint, bevel=0.03,
                  solid=i < 3, vary=0.08)
            b.box((size[0] * 0.5, 0.02, size[2] * 0.25), (x, -size[1] / 2 - 0.01, z + size[2] * 0.6), mat=PAINT,
                  tint=CREAM, solid=False, vary=0)


def beacon(b, at, size=0.5, blink=1.0, range_m=1500.0):
    """A red aviation lamp on top of something tall (MONUMENTS.md §7.6)."""
    x, y, z = at
    b.box((size, size, size * 0.7), (x, y, z + size * 0.35), mat=LAMP, tint=RED_LAMP, solid=False, vary=0)
    b.marker("light", (x, y, z + size), colour=[1.0, 0.12, 0.05], range=range_m, blink=blink)


SEGMENTS = {
    "0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc", "7": "abc",
    "8": "abcdefg", "9": "abcdfg", "A": "abcefg", "B": "fgecd", "C": "afed", "D": "bcdeg", "E": "afged", "F": "afge",
    "H": "fbgec", "L": "fed", "P": "abfge", "U": "fedcb", "S": "afgcd", "-": "g", "O": "abcdef", "R": "eg",
    "T": "fged", "N": "ceg", "Y": "fgbcd", "J": "bcde", "G": "afedc", "I": "bc", "V": "fedcb", " ": "",
}


def stencil(b, text, at, rot=(0, 0, 0), height=2.0, tint=BLACK, depth=0.03, mat=PAINT, wear=0.6, wrap=None):
    """Text in stencilled seven-segment glyphs painted on a surface facing -Y; `at` is its bottom left corner. With
    `wrap` (a radius) the text is centred on `at` and bent round a cylinder whose axis is at +Y `wrap` metres."""
    w = 0.56 * height
    t = 0.14 * height
    g = t * 0.35
    adv = w + 0.3 * height
    text = str(text).upper()
    total = len(text) * adv - 0.3 * height

    def put(cx, cz, size):
        if wrap:
            phi = (cx - total / 2) / wrap
            b.box(size, (wrap * math.sin(phi), wrap - wrap * math.cos(phi) - depth / 2, cz),
                  (0, 0, math.degrees(phi)), mat=mat, tint=tint, solid=False, vary=0, wear=wear)
        else:
            b.box(size, (cx, -depth / 2, cz), mat=mat, tint=tint, solid=False, vary=0, wear=wear)

    with b.frame(at, rot):
        x = 0.0
        for ch in text:
            for s in SEGMENTS.get(ch, ""):
                if s in "agd":
                    z = {"a": height - t / 2, "g": height / 2, "d": t / 2}[s]
                    put(x + w / 2, z, (w - t - 2 * g, depth, t))
                else:
                    xx = x + (w - t / 2 if s in "bc" else t / 2)
                    zz = height * (0.75 if s in "bf" else 0.25)
                    put(xx, zz, (t, depth, height / 2 - t - g))
            x += adv
    return total


def plate(b, at, rot=(0, 0, 0), w=1.2, h=0.8, tint=YELLOW, mark=BLACK, text=None):
    """A sign plate facing -Y with a stencilled code or a hazard diamond."""
    with b.frame(at, rot):
        b.box((w, 0.04, h), (0, 0, 0), mat=PAINT, tint=tint, solid=False, vary=0, wear=0.55)
        if text:
            hh = h * 0.6
            n = len(text)
            tw = n * (0.86 * hh) - 0.3 * hh
            stencil(b, text, (-tw / 2, -0.021, -hh / 2), (0, 0, 0), hh, tint=mark, depth=0.01)
        else:
            b.box((h * 0.45, 0.02, h * 0.45), (0, -0.025, 0), (0, 45, 0), mat=PAINT, tint=mark, solid=False, vary=0)


# ------------------------------------------------------------------------------------------------ fixed pieces


def _flood_mast(b):
    """A 22 m lighting mast: a tapered lattice, a head frame with four flood lamps, a ladder, an aviation lamp."""
    from .kit import lattice_tower
    lattice_tower(b, (0, 0, 0), 22.0, 2.2, 0.9, sections=5, member=0.1, tint=STEEL)
    b.box((3.2, 1.0, 0.2), (0, 0, 22.1), mat=GRATE, tint=GREY)
    b.box((3.2, 0.08, 1.2), (0, 0.5, 22.8), mat=PAINT, tint=STEEL, solid=False)
    for x in (-1.2, -0.4, 0.4, 1.2):
        lamp(b, (x, -0.3, 23.2), (-30, 0, 0), size=(0.6, 0.3, 0.45))
    beacon(b, (0, 0.3, 23.4), 0.3, range_m=600.0)
    ladder(b, (0, -0.55, 0), 22.0, (0, 0, 0), cage=True, tint=STEEL)


def _container_40(b):
    """A 40-foot freight container: corrugated sides, corner posts, doors with lock bars."""
    w, d, h = 12.19, 2.44, 2.59
    b.box((w - 0.2, d - 0.04, h - 0.25), (0, 0, h / 2), mat=CORRUGATED, tint=(1, 1, 1))
    for x in (-w / 2 + 0.07, w / 2 - 0.07):
        for y in (-d / 2 + 0.07, d / 2 - 0.07):
            b.box((0.15, 0.15, h), (x, y, h / 2), mat=PAINT, tint=(1, 1, 1), solid=False)
    for z in (0.08, h - 0.08):
        for y in (-d / 2 + 0.06, d / 2 - 0.06):
            b.box((w, 0.13, 0.16), (0, y, z), mat=PAINT, tint=(1, 1, 1), solid=False)
    b.box((0.05, d - 0.3, h - 0.3), (w / 2 - 0.02, 0, h / 2), mat=PAINT, tint=(1, 1, 1), solid=False)
    for y in (-0.75, -0.35, 0.35, 0.75):
        b.cyl(0.022, h - 0.35, (w / 2 + 0.03, y, h / 2), mat=PAINT, tint=GREY, sides=6, solid=False)
    b.collide_box(Vector((0, 0, h / 2)), (w, d, h), (0, 0, 0))


def _wreck_car(b):
    """A burnt-out car: a rusted body shell, a crushed cabin with its glass gone, wheels on two corners."""
    b.box((4.3, 1.75, 0.65), (0, 0, 0.55), mat=RUST, tint=(1, 1, 1), bevel=0.08)
    b.extrude([(-1.2, 0.0), (1.1, 0.0), (0.7, 0.62), (-0.85, 0.6)], 1.6, (0.1, 0, 0.88), (0, 0, 0), mat=RUST,
              tint=srgb(0.8, 0.75, 0.7), bevel=0.04, solid=False)
    b.box((2.0, 1.5, 0.04), (0.0, 0, 1.52), (0, 2, 0), mat=PAINT, tint=SOOT, solid=False)
    for x, y in ((1.35, -0.8), (-1.35, 0.8)):
        b.cyl(0.33, 0.24, (x, y, 0.33), (90, 0, 0), mat=MATTE, tint=BLACK, sides=12, solid=False)
    for x, y in ((1.35, 0.85), (-1.35, -0.85)):
        b.cyl(0.24, 0.1, (x, y, 0.3), (90, 0, 0), mat=RUST, tint=(1, 1, 1), sides=10, solid=False)
    b.box((0.8, 1.6, 0.03), (2.0, 0, 0.9), (0, -14, 0), mat=RUST, tint=(1, 1, 1), solid=False)


def _wreck_truck(b):
    """An abandoned six-wheel utility truck: a cab with a sun-faded olive coat, a flatbed with a tarp frame."""
    b.box((7.6, 0.4, 0.35), (0, 0, 0.95), mat=PAINT, tint=DARK)
    b.box((2.2, 2.4, 2.0), (2.9, 0, 2.05), mat=PAINT, tint=OLIVE, bevel=0.08)
    b.box((0.04, 2.0, 0.8), (4.02, 0, 2.5), mat=GLASS, solid=False)
    b.box((2.2, 0.04, 0.6), (2.9, -1.22, 2.5), mat=GLASS, solid=False)
    b.box((1.0, 2.3, 0.9), (4.4, 0, 1.4), mat=PAINT, tint=OLIVE, bevel=0.06)
    b.box((5.0, 2.5, 0.2), (-1.0, 0, 1.25), mat=PAINT, tint=OLIVE_DARK)
    for x in (-3.4, -1.0):
        b.tube([(x, -1.2, 1.35), (x, -1.2, 3.0), (x, 1.2, 3.0), (x, 1.2, 1.35)], 0.05, mat=PAINT, tint=DARK,
               sides=6, cap=False)
    b.box((2.6, 2.6, 0.04), (-2.2, 0, 3.05), (0, 4, 0), mat=MATTE, tint=TARP_GREEN, solid=False)
    for x in (3.0, -1.6, -3.0):
        for y in (-1.05, 1.05):
            b.cyl(0.55, 0.42, (x, y, 0.55), (90, 0, 0), mat=MATTE, tint=BLACK, sides=14, solid=False)
            b.cyl(0.3, 0.44, (x, y, 0.55), (90, 0, 0), mat=PAINT, tint=GREY, sides=10, solid=False)
    b.collide_box(Vector((0.2, 0, 1.4)), (8.2, 2.5, 2.8), (0, 0, 0))


def _main_transformer(b):
    """A generator step-up transformer: a tank 7 m long on a bunded plinth, radiator banks with fans on both sides,
    three tall bushings and three short ones, a conservator on a frame, a control cabinet."""
    w, d, h = 7.0, 4.0, 4.6
    body = srgb(0.55, 0.58, 0.56)
    b.box((10.0, 8.0, 0.5), (0, 0, 0.25), mat=CONCRETE, tint=CONCRETE_T, bevel=0.04)
    for x in (-5.0, 5.0):
        b.box((0.3, 8.0, 0.9), (x, 0, 0.7), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    b.box((w, d, h), (0, 0, 0.5 + h / 2), mat=PAINT, tint=body, bevel=0.06)
    b.box((w + 0.2, d + 0.2, 0.2), (0, 0, 0.5 + h + 0.1), mat=PAINT, tint=body)
    for side in (-1, 1):
        for i in range(12):
            x = -w / 2 + 0.4 + i * (w - 0.8) / 11
            b.box((0.08, 1.2, h * 0.8), (x, side * (d / 2 + 0.75), 0.5 + h * 0.48), mat=PAINT, tint=body,
                  solid=False, vary=0)
        for x in (-2.2, 0.0, 2.2):
            b.cyl(0.55, 0.3, (x, side * (d / 2 + 1.5), 0.5 + h * 0.45), (90, 0, 0), mat=PAINT, tint=DARK, sides=14,
                  solid=False)
        b.box((w - 0.4, 0.2, 0.2), (0, side * (d / 2 + 0.75), 0.5 + h * 0.9), mat=PAINT, tint=body, solid=False)
    top = 0.5 + h + 0.2
    for i in range(3):
        x = -2.0 + 2.0 * i
        b.cyl(0.25, 0.4, (x, -0.8, top + 0.2), mat=PAINT, tint=GREY, sides=12, solid=False)
        kit.insulator(b, (x, -0.8, top + 0.4), 3.0, tint=srgb(0.55, 0.30, 0.20))
        b.cyl(0.09, 0.3, (x, -0.8, top + 3.55), mat=PAINT, tint=LIGHTGREY, sides=8, solid=False)
        kit.insulator(b, (x * 0.8, 0.9, top), 1.0, discs=4, tint=srgb(0.55, 0.30, 0.20))
    for x in (-2.6, 2.6):
        b.box((0.14, 0.14, 2.2), (x, 1.6, top + 1.1), mat=PAINT, tint=GREY, solid=False)
    b.cyl(0.8, 5.0, (0, 1.6, top + 2.6), (0, 90, 0), mat=PAINT, tint=body, sides=20)
    b.box((1.0, 0.5, 1.6), (w / 2 - 0.6, -d / 2 - 0.3, 1.5), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
    b.box((0.8, 0.02, 0.15), (w / 2 - 0.6, -d / 2 - 0.56, 2.0), mat=PAINT, tint=YELLOW, solid=False, vary=0)


def _haul_truck(b):
    """A mine haul truck: tyres taller than a person (dual at the rear), a chassis, the dump body with its canopy
    over the cab, the upper deck with rails and a stair up to it."""
    body = srgb(0.86, 0.66, 0.14)
    for x, ys in ((4.4, (-3.0, 3.0)), (-3.4, (-2.2, -3.6, 2.2, 3.6))):
        for y in ys:
            b.cyl(2.0, 1.3, (x, y, 2.0), (90, 0, 0), mat=MATTE, tint=srgb(0.07, 0.07, 0.07), sides=20, solid=False)
            b.cyl(0.9, 1.34, (x, y, 2.0), (90, 0, 0), mat=PAINT, tint=GREY, sides=12, solid=False)
    b.box((12.4, 3.0, 1.6), (0.2, 0, 2.6), mat=PAINT, tint=body)
    b.box((3.0, 4.4, 2.8), (5.4, 0, 3.6), mat=PAINT, tint=body, bevel=0.1)
    b.box((0.2, 3.6, 2.0), (6.95, 0, 3.6), mat=PAINT, tint=DARK, solid=False)
    b.box((3.4, 7.2, 0.25), (5.2, 0, 5.15), mat=GRATE, tint=GREY)
    railing(b, (3.5, -3.6, 5.3), (6.9, -3.6, 5.3), spacing=1.2, toe=False)
    railing(b, (6.9, -3.6, 5.3), (6.9, 3.6, 5.3), spacing=1.2, toe=False)
    b.box((2.4, 2.4, 2.4), (4.6, -2.0, 6.5), mat=PAINT, tint=body, bevel=0.06)
    b.box((0.04, 2.2, 1.2), (5.82, -2.0, 6.9), mat=GLASS, solid=False)
    b.box((2.2, 0.04, 1.0), (4.6, -3.22, 6.9), mat=GLASS, solid=False)
    b.extrude([(-7.4, 4.2), (3.0, 3.4), (3.2, 7.4), (6.8, 8.0), (6.8, 8.3), (2.4, 8.3), (-7.8, 6.6)], 6.8,
              (0, 0, 0), (0, 0, 0), mat=PAINT, tint=body, bevel=0.05)
    for x in (-5.0, -2.0, 1.0):
        b.box((0.3, 6.9, 0.4), (x, 0, 4.1 + (x + 7.4) * 0.06), mat=PAINT, tint=srgb(0.70, 0.52, 0.10), solid=False)
    stairs(b, (6.6, -4.4, 0.4), (0, 0, 90), 0.8, 4.75, 5.4, tint=YELLOW)
    b.collide_box(Vector((0, 0, 2.2)), (14.0, 7.6, 4.4), (0, 0, 0))


# Rail: the track's rails sit on sleepers on a ballast bed 0.3 m deep, so wheels stand at RAIL_TOP.
RAIL_TOP = 0.62
WHEEL_R = 0.46


def _track_10(b):
    """Ten metres of track on the ballast: fifteen concrete sleepers and two rails (standard gauge)."""
    for k in range(15):
        x = -5.0 + (k + 0.5) * 10.0 / 15
        b.box((0.26, 2.5, 0.16), (x, 0, 0.38), mat=CONCRETE, tint=srgb(0.62, 0.60, 0.56), solid=False, vary=0.05)
    for y in (-0.7535, 0.7535):
        b.box((10.0, 0.07, 0.16), (0, y, 0.54), mat=RUST, tint=(1, 1, 1), solid=False)
        b.box((10.0, 0.14, 0.03), (0, y, 0.475), mat=RUST, tint=(1, 1, 1), solid=False)


def _bogie(b, x, axles=2):
    """A two- or three-axle bogie under a wagon at `x`: a frame, wheels on the rails, springs."""
    zc = RAIL_TOP + WHEEL_R
    span = 1.8 if axles == 2 else 1.7
    b.box((span * (axles - 1) + 1.4, 2.0, 0.5), (x, 0, zc + 0.15), mat=PAINT, tint=srgb(0.22, 0.22, 0.22),
          solid=False)
    for k in range(axles):
        ax = x + (k - (axles - 1) / 2) * span
        for y in (-0.75, 0.75):
            b.cyl(WHEEL_R, 0.14, (ax, y, zc), (90, 0, 0), mat=RUST, tint=(1, 1, 1), sides=12, solid=False)
        b.cyl(0.08, 1.7, (ax, 0, zc), (90, 0, 0), mat=PAINT, tint=DARK, sides=6, solid=False)
    for y in (-1.05, 1.05):
        b.box((0.5, 0.25, 0.45), (x, y, zc + 0.3), mat=PAINT, tint=YELLOW, solid=False)


def _underframe(b, length, z=1.6):
    b.box((length, 2.7, 0.4), (0, 0, z), mat=PAINT, tint=srgb(0.25, 0.25, 0.25))
    for s in (-1, 1):
        b.box((0.5, 0.4, 0.35), (s * (length / 2 + 0.25), 0, z - 0.1), mat=PAINT, tint=DARK, solid=False)
        for y in (-1.0, 1.0):
            b.cyl(0.18, 0.6, (s * (length / 2 + 0.3), y, z), (0, 90, 0), mat=PAINT, tint=DARK, sides=8, solid=False)


def _boxcar(b):
    """A covered goods wagon 14 m long: corrugated walls with a sliding door open on both sides (it can be entered),
    a roof, corner ladders, a brake wheel."""
    L, W, H = 14.0, 2.9, 2.9
    z0 = 1.85
    tint = (1, 1, 1)
    _bogie(b, -4.8)
    _bogie(b, 4.8)
    _underframe(b, L, 1.6)
    b.box((L, W, 0.12), (0, 0, z0 + 0.06), mat=PAINT, tint=srgb(0.40, 0.36, 0.30))
    for y in (-W / 2, W / 2):
        b.box((L / 2 - 1.6, 0.08, H), (-(L / 4 + 0.8), y, z0 + H / 2), mat=CORRUGATED, tint=tint)
        b.box((L / 2 - 1.6, 0.08, H), (L / 4 + 0.8, y, z0 + H / 2), mat=CORRUGATED, tint=tint)
        b.box((3.0, 0.1, 0.5), (0, y, z0 + H - 0.25), mat=CORRUGATED, tint=tint)
        b.box((3.1, 0.08, H - 0.4), (3.0, y * 1.04, z0 + (H - 0.4) / 2 + 0.05), mat=CORRUGATED,
              tint=srgb(0.85, 0.85, 0.85), solid=False)
        b.box((3.4, 0.12, 0.12), (1.5, y * 1.07, z0 + H - 0.1), mat=PAINT, tint=DARK, solid=False)
    for x in (-L / 2, L / 2):
        b.box((0.08, W, H), (x, 0, z0 + H / 2), mat=CORRUGATED, tint=tint)
    b.lathe([(W / 2 + 0.05, 0.0), (W / 2 - 0.3, 0.25), (0.0, 0.32)], (0, 0, z0 + H), (0, 0, 0), mat=PAINT, tint=tint,
            sides=4, solid=False)
    b.box((L + 0.1, W + 0.1, 0.1), (0, 0, z0 + H + 0.05), mat=PAINT, tint=tint)
    for x in (-L / 2 - 0.06, L / 2 + 0.06):
        for k in range(6):
            b.box((0.05, 0.5, 0.04), (x, W / 2 - 0.35, 1.9 + k * 0.45), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    b.cyl(0.25, 0.04, (L / 2 + 0.08, -0.6, 3.6), (0, 90, 0), mat=PAINT, tint=DARK, sides=10, solid=False)
    b.collide_box(Vector((0, 0, 1.0)), (L, 2.6, 0.9), (0, 0, 0))


def _flatcar(b):
    """A flat wagon 14 m long with stakes along its deck (containers go on top as their own pieces)."""
    L = 14.0
    _bogie(b, -4.8)
    _bogie(b, 4.8)
    _underframe(b, L, 1.6)
    b.box((L, 2.8, 0.25), (0, 0, 1.93), mat=PAINT, tint=srgb(0.45, 0.40, 0.33))
    for k in range(6):
        x = -L / 2 + 1.2 + k * (L - 2.4) / 5
        for y in (-1.42, 1.42):
            b.box((0.12, 0.12, 0.5), (x, y, 2.3), mat=PAINT, tint=DARK, solid=False)
    b.collide_box(Vector((0, 0, 1.0)), (L, 2.6, 0.9), (0, 0, 0))


def _hopper_wagon(b):
    """An open hopper wagon 12 m long: a body with sloped ends and discharge chutes underneath, ribs on the sides."""
    L = 12.0
    _bogie(b, -4.0)
    _bogie(b, 4.0)
    _underframe(b, L, 1.6)
    with b.frame((0, 0, 0), (0, 0, 90)):
        b.extrude([(-1.45, 2.0), (1.45, 2.0), (1.45, 4.3), (-1.45, 4.3)], L - 2.0, (0, 0, 0), (0, 0, 0), mat=PAINT,
                  tint=srgb(0.55, 0.32, 0.22), bevel=0.03)
    for s in (-1, 1):
        b.extrude([(s * (L / 2 - 1.0), 2.0), (s * (L / 2 - 0.1), 4.3), (s * (L / 2 - 1.0), 4.3)][::s], 2.9, (0, 0, 0),
                  (0, 0, 0), mat=PAINT, tint=srgb(0.55, 0.32, 0.22))
    for k in range(7):
        x = -L / 2 + 1.5 + k * (L - 3.0) / 6
        for y in (-1.5, 1.5):
            b.box((0.15, 0.1, 2.3), (x, y, 3.15), mat=PAINT, tint=srgb(0.45, 0.26, 0.18), solid=False, vary=0)
    for x in (-2.0, 2.0):
        b.cyl(0.9, 0.6, (x, 0, 1.6), mat=PAINT, tint=srgb(0.40, 0.24, 0.16), sides=4, radius2=0.5, solid=False)
    b.box((L - 1.6, 2.6, 0.2), (0, 0, 4.0), mat=MATTE, tint=srgb(0.30, 0.25, 0.22), solid=False)


def _tank_wagon(b):
    """A tank wagon 13 m long: a cylindrical tank with domed ends, a filler dome, a walkway and ladders."""
    L = 13.0
    _bogie(b, -4.4)
    _bogie(b, 4.4)
    _underframe(b, L, 1.6)
    b.cyl(1.45, L - 2.2, (0, 0, 3.35), (0, 90, 0), mat=PAINT, tint=srgb(0.18, 0.19, 0.20), sides=24)
    for s in (-1, 1):
        b.lathe([(1.45, 0.0), (1.2, 0.5), (0.6, 0.9), (0.0, 1.0)], (s * (L / 2 - 1.1), 0, 3.35), (0, 90 * s, 0),
                mat=PAINT, tint=srgb(0.18, 0.19, 0.20), sides=24)
    b.cyl(0.5, 0.5, (0, 0, 5.0), mat=PAINT, tint=srgb(0.18, 0.19, 0.20), sides=12, solid=False)
    b.box((2.2, 1.2, 0.06), (0, 0, 4.82), mat=GRATE, tint=GREY, solid=False)
    b.cyl(1.46, 0.6, (2.5, 0, 3.35), (0, 90, 0), mat=PAINT, tint=ORANGE, sides=24, solid=False, vary=0)
    for y in (-1.6, 1.6):
        for k in range(6):
            b.box((0.4, 0.05, 0.04), (0.0, y, 2.2 + k * 0.45), mat=PAINT, tint=YELLOW, solid=False, vary=0)
    b.collide_box(Vector((0, 0, 1.0)), (L, 2.6, 0.9), (0, 0, 0))


def _locomotive(b):
    """A diesel road locomotive 18 m long in the yard's yellow and grey: two three-axle bogies, a long hood with
    louvres and an exhaust, the cab with its windows, a short hood, walkways with rails, striped ends."""
    L = 18.0
    body = srgb(0.86, 0.66, 0.14)
    grey = srgb(0.42, 0.44, 0.46)
    _bogie(b, -5.6, 3)
    _bogie(b, 5.6, 3)
    _underframe(b, L, 1.65)
    b.box((L - 0.2, 3.0, 0.15), (0, 0, 1.92), mat=GRATE, tint=GREY)
    b.box((10.5, 2.3, 2.6), (-2.4, 0, 3.3), mat=PAINT, tint=body, bevel=0.08)
    b.box((10.6, 2.32, 0.6), (-2.4, 0, 2.3), mat=PAINT, tint=grey, solid=False)
    for k in range(8):
        for y in (-1.16, 1.16):
            b.box((0.9, 0.04, 1.2), (-6.8 + k * 1.25, y, 3.4), mat=PAINT, tint=DARK, solid=False, vary=0)
    b.cyl(0.3, 0.7, (-4.0, 0, 4.9), mat=PAINT, tint=DARK, sides=8, solid=False)
    for x in (-6.5, 0.5):
        b.cyl(0.7, 0.2, (x, 0, 4.65), mat=GRATE, tint=GREY, sides=12, solid=False)
    b.box((3.0, 3.0, 3.1), (4.8, 0, 3.55), mat=PAINT, tint=body, bevel=0.08)
    b.box((3.1, 3.05, 0.15), (4.8, 0, 5.15), mat=PAINT, tint=grey)
    b.box((0.04, 2.4, 0.9), (6.31, 0, 4.2), mat=GLASS, solid=False)
    for y in (-1.51, 1.51):
        b.box((1.6, 0.04, 0.9), (4.8, y, 4.2), mat=GLASS, solid=False)
    b.box((2.4, 2.2, 1.6), (7.6, 0, 2.8), mat=PAINT, tint=body, bevel=0.06)
    for x in (L / 2 - 0.05, -L / 2 + 0.05):
        stripes(b, (x, -1.4, 0), (x, 1.4, 0), 0.8, width=0.35, at_z=1.5, normal=(1 if x > 0 else -1, 0, 0))
        b.box((0.1, 0.3, 0.2), (x, -1.1, 2.4), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
        b.box((0.1, 0.3, 0.2), (x, 1.1, 2.4), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
    for y in (-1.45, 1.45):
        railing(b, (-L / 2 + 0.6, y, 2.0), (3.2, y, 2.0), height=1.0, spacing=2.2, toe=False)
    b.collide_box(Vector((0, 0, 1.0)), (L, 2.6, 0.9), (0, 0, 0))
    b.collide_box(Vector((0.0, 0, 3.4)), (16.0, 2.4, 3.2), (0, 0, 0))


def _hab_can(b):
    """A colonist's prefab hab can, 8 m long on short legs, with windows, a hatch and a livery band."""
    from .structures import hab_module
    hab_module(b, (0, 0, 0), (0, 0, 0), length=8.0, radius=1.6, tint=CREAM, band=ORANGE, windows=True, legs=0.6)
    b.collide_box(Vector((0, 0, 2.2)), (9.0, 3.2, 3.2), (0, 0, 0))


kit.PIECES.update({
    "flood-mast": _flood_mast,
    "container-40": _container_40,
    "wreck-car": _wreck_car,
    "wreck-truck": _wreck_truck,
    "main-transformer": _main_transformer,
    "haul-truck": _haul_truck,
    "track-10": _track_10,
    "boxcar": _boxcar,
    "flatcar": _flatcar,
    "hopper-wagon": _hopper_wagon,
    "tank-wagon": _tank_wagon,
    "locomotive": _locomotive,
    "hab-can": _hab_can,
})
