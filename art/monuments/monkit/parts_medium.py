"""Parts for the medium monuments (MONUMENTS.md §5.3): walls with real door and window openings, doors with chip
readers and power-cell sockets, enterable buildings with floors and roof hatches, switchback stairs, ring decks and
rails, a geodesic dome, overgrowth, stencilled codes and plates, Reclaimer work (welded scrap walls, tarps, strung
lights, clan marks), pipe racks, process columns, earth and concrete embankments, canopies, and the kit pieces the
medium sites share (registered into `kit.PIECES` when this module is imported).

Same conventions as kit.py and structures.py: positions in metres in the builder's current frame, angles in degrees,
fronts toward -Y, colours linear (`srgb()` for readability).
"""
import math

from mathutils import Vector

from . import kit
from .kit import (BLACK, BLUE, CONCRETE_T, DARK, GREEN, GREY, LIGHTGREY, OFFWHITE, OLIVE, ORANGE, RED, RED_LAMP, SAND,
                  TEAL, WARM_LAMP, YELLOW, cable, ladder, lamp, railing, srgb, stairs)
from .surfaces import (ASPHALT, CHAINLINK, CONCRETE, CORRUGATED, FORMWORK, GLASS, GLOSS, GRATE, HULL, LAMP, MATTE,
                       PAINT, PANEL, RUST)

# ------------------------------------------------------------------------------------------------ colours

LEAF = srgb(0.20, 0.33, 0.11)
LEAF2 = srgb(0.29, 0.39, 0.13)
LEAF3 = srgb(0.36, 0.42, 0.17)
LEAF_DRY = srgb(0.48, 0.44, 0.22)
BARK = srgb(0.30, 0.25, 0.19)
SOIL = srgb(0.27, 0.21, 0.15)
MOSS = srgb(0.30, 0.37, 0.17)
KHAKI = srgb(0.53, 0.49, 0.35)
BULWARK = srgb(0.37, 0.41, 0.31)        # olive drab of the Bulwark
BULWARK_GREY = srgb(0.45, 0.47, 0.45)
PALE = srgb(0.84, 0.85, 0.84)           # Seedward pale grey plating
TARP_BLUE = srgb(0.16, 0.31, 0.50)
TARP_GREEN = srgb(0.26, 0.36, 0.22)
TARP_ORANGE = srgb(0.78, 0.40, 0.12)
TARP_RED = srgb(0.56, 0.15, 0.11)
TARP_TAN = srgb(0.62, 0.55, 0.40)
WOOD = srgb(0.55, 0.42, 0.28)
WATER = srgb(0.05, 0.09, 0.09)
CHIP = {"amber": srgb(1.0, 0.62, 0.08), "cyan": srgb(0.10, 0.85, 0.95), "violet": srgb(0.62, 0.25, 1.0)}
GRAFFITI = (srgb(0.85, 0.20, 0.15), srgb(0.95, 0.85, 0.30), srgb(0.20, 0.70, 0.80), srgb(0.92, 0.92, 0.88),
            srgb(0.55, 0.85, 0.30))

SIDES = ("front", "right", "back", "left")


def _yaw(d):
    return math.degrees(math.atan2(d.y, d.x))


# ------------------------------------------------------------------------------------------------ doors and walls


def reader(b, at, rot=(0, 0, 0), chip="amber", cell=False):
    """A chip reader on a wall, its face toward -Y of its frame: a dark box with a lit strip in the chip's colour, a
    card slot, and below it a striped power-cell socket when the door also takes a cell."""
    with b.frame(at, rot):
        b.box((0.24, 0.08, 0.34), (0, -0.04, 1.32), mat=PAINT, tint=DARK, solid=False, vary=0)
        b.box((0.17, 0.02, 0.05), (0, -0.09, 1.42), mat=LAMP, tint=CHIP[chip], solid=False, vary=0)
        b.box((0.12, 0.02, 0.06), (0, -0.09, 1.27), mat=PAINT, tint=LIGHTGREY, solid=False, vary=0)
        if cell:
            b.box((0.36, 0.14, 0.44), (0, -0.07, 0.76), mat=PAINT, tint=YELLOW, solid=False, vary=0)
            b.box((0.22, 0.02, 0.22), (0, -0.15, 0.79), mat=PAINT, tint=BLACK, solid=False, vary=0)
            b.box((0.1, 0.03, 0.04), (0, -0.16, 0.6), mat=LAMP, tint=CHIP[chip], solid=False, vary=0)
            b.tube([(0.0, -0.05, 1.15), (0.0, -0.05, 0.98)], 0.015, tint=BLACK, sides=4)


def door_leaf(b, hinge, width, height, angle=0.0, tint=GREY, thick=0.06, window=False):
    """A door leaf hinged at `hinge` (the hinge's foot), spanning +X when shut; `angle` swings it toward +Y."""
    with b.frame(hinge, (0, 0, angle)):
        b.box((width - 0.03, thick, height - 0.02), (width / 2, 0, height / 2), mat=PAINT, tint=tint, solid=False)
        b.box((0.04, thick + 0.04, 0.16), (width - 0.12, 0, height * 0.45), mat=PAINT, tint=DARK, solid=False,
              vary=0)
        if window:
            b.box((width * 0.4, thick + 0.01, height * 0.22), (width / 2, 0, height * 0.72), mat=GLASS, solid=False,
                  vary=0)
        else:
            b.box((width * 0.6, thick + 0.01, 0.05), (width / 2, 0, height * 0.75), mat=PAINT, tint=DARK,
                  solid=False, vary=0)


def wall(b, a, c, height, thick=0.3, openings=(), mat=CONCRETE, tint=CONCRETE_T, frame=DARK, glass=0.65,
         solid=True, extend=True, door_tint=GREY):
    """A wall from `a` to `c` (its foot at a's height), centred on the line, its outside to the right of a→c
    (local -Y). `openings`: (offset along the wall from a, width, height, sill[, door]) — sill 0 makes a doorway;
    `door` is None for a plain gap, or a dict for a door: kind_of, chip, cell, state ("shut", "ajar", "open",
    "gone"), marker (default True). `glass`: chance a window keeps its pane."""
    a, c = Vector(a), Vector(c)
    d = Vector((c.x - a.x, c.y - a.y, 0.0))
    length = d.length
    e = thick / 2 if extend else 0.0
    with b.frame(tuple(a), (0, 0, _yaw(d))):
        x = -e
        for op in sorted(openings, key=lambda o: o[0]):
            ox, ow, oh, sill = op[:4]
            door = op[4] if len(op) > 4 else None
            x0, x1 = ox - ow / 2, ox + ow / 2
            if x0 - x > 0.02:
                b.box((x0 - x, thick, height), ((x + x0) / 2, 0, height / 2), mat=mat, tint=tint, solid=solid)
            if sill > 0.02:
                b.box((ow, thick, sill), (ox, 0, sill / 2), mat=mat, tint=tint, solid=solid)
            top = min(height, sill + oh)
            if height - top > 0.02:
                b.box((ow, thick, height - top), (ox, 0, top + (height - top) / 2), mat=mat, tint=tint, solid=solid)
            ft = thick + 0.05
            for xx in (x0 + 0.04, x1 - 0.04):
                b.box((0.08, ft, oh), (xx, 0, sill + oh / 2), mat=PAINT, tint=frame, solid=False, vary=0)
            b.box((ow, ft, 0.08), (ox, 0, top - 0.04), mat=PAINT, tint=frame, solid=False, vary=0)
            if sill > 0.02:
                # A window: sill ledge outside, pane (or not), a mullion.
                b.box((ow + 0.12, 0.2, 0.06), (ox, -thick / 2 - 0.06, sill - 0.03), mat=CONCRETE, tint=tint,
                      solid=False, vary=0)
                b.box((0.05, 0.06, oh), (ox, 0, sill + oh / 2), mat=PAINT, tint=frame, solid=False, vary=0)
                if b.rng.random() < glass:
                    b.box((ow - 0.1, 0.02, oh - 0.1), (ox, 0, sill + oh / 2), mat=GLASS, solid=False, vary=0)
            elif door is not None:
                state = door.get("state", "shut" if door.get("chip") else "ajar")
                leaf_tint = door.get("tint", door_tint)
                if state == "shut":
                    door_leaf(b, (x0 + 0.04, 0, 0), ow - 0.08, oh - 0.04, 0, tint=leaf_tint)
                elif state in ("ajar", "open"):
                    ang = (35 + 40 * b.rng.random()) if state == "ajar" else (95 + 10 * b.rng.random())
                    door_leaf(b, (x0 + 0.04, 0.04, 0), ow - 0.08, oh - 0.04, ang, tint=leaf_tint)
                if door.get("chip"):
                    reader(b, (x1 + 0.35, -thick / 2, 0), (0, 0, 0), door["chip"], door.get("cell", False))
                if door.get("marker", True):
                    props = {k: v for k, v in door.items() if k in ("kind_of", "chip", "cell", "room", "code")}
                    props.setdefault("kind_of", "door")
                    b.marker("door", (ox, 0, sill), 180.0, width=round(ow, 2), height=round(oh, 2), **props)
            x = x1
        if length + e - x > 0.02:
            b.box((length + e - x, thick, height), ((x + length + e) / 2, 0, height / 2), mat=mat, tint=tint,
                  solid=solid)


def slab_holes(b, x0, y0, x1, y1, z, t, holes=(), mat=CONCRETE, tint=CONCRETE_T, solid=True):
    """A floor slab over (x0, y0)-(x1, y1), its top at z + t, with rectangular `holes` (x0, y0, x1, y1) left open."""
    xs = sorted({x0, x1, *[min(max(h[0], x0), x1) for h in holes], *[min(max(h[2], x0), x1) for h in holes]})
    ys = sorted({y0, y1, *[min(max(h[1], y0), y1) for h in holes], *[min(max(h[3], y0), y1) for h in holes]})
    for j in range(len(ys) - 1):
        cy = (ys[j] + ys[j + 1]) / 2
        row = []
        for i in range(len(xs) - 1):
            cx = (xs[i] + xs[i + 1]) / 2
            row.append(not any(h[0] <= cx <= h[2] and h[1] <= cy <= h[3] for h in holes))
        i = 0
        while i < len(row):
            if not row[i]:
                i += 1
                continue
            k = i
            while k + 1 < len(row) and row[k + 1]:
                k += 1
            xa, xb = xs[i], xs[k + 1]
            if xb - xa > 0.01 and ys[j + 1] - ys[j] > 0.01:
                b.box((xb - xa, ys[j + 1] - ys[j], t), ((xa + xb) / 2, cy, z + t / 2), mat=mat, tint=tint,
                      solid=solid)
            i = k + 1


def building(b, at, rot, w, d, floors=1, floor_h=3.4, mat=CONCRETE, tint=CONCRETE_T, openings=None, wall_t=0.3,
             plinth=0.3, slab=0.25, parapet=0.9, holes=(), band=None, roof_units=1, glass=0.6, frame=DARK,
             roof_mat=CONCRETE, roof_tint=None, downpipes=True, steps=True, door_tint=GREY):
    """An enterable flat-roofed building `w` x `d`, `floors` storeys of `floor_h`, on a plinth: walls with real
    openings, floor slabs, a roof with a parapet. `openings`: {side: [(offset, width, height, sill, floor[, door])]}
    with sides "front" (-Y), "right" (+X), "back" (+Y), "left" (-X), offsets along X for front and back and along Y
    for the sides (from the centre); `door` as in `wall`. `holes`: (x0, y0, x1, y1, floor) left open in the slab of
    that floor (`floors` = the roof) for stairs and hatches. `band`: (tint, height above the plinth, depth) painted
    round the walls. Returns {"floor": [floor levels], "roof": roof level}."""
    openings = openings or {}
    H = floors * floor_h
    corners = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    info = {"floor": [plinth + k * floor_h + (slab if k else 0.0) for k in range(floors)],
            "roof": plinth + H + slab}
    with b.frame(at, rot):
        if plinth > 0:
            # Flush with the walls' outer faces: a plinth sticking out under a wall puts the wall's lowest vertices
            # inside it, and the baked occlusion then turns the whole face black.
            b.box((w + wall_t - 0.04, d + wall_t - 0.04, plinth), (0, 0, plinth / 2), mat=CONCRETE,
                  tint=srgb(0.72, 0.71, 0.68))
        for si, side in enumerate(SIDES):
            a, c = corners[si], corners[(si + 1) % 4]
            for k in range(floors):
                ops = []
                for o in openings.get(side, ()):
                    off, ow, oh, sill, fl = o[:5]
                    if fl != k:
                        continue
                    along = {"front": off + w / 2, "right": off + d / 2, "back": w / 2 - off,
                             "left": d / 2 - off}[side]
                    door = o[5] if len(o) > 5 else ({} if sill <= 0.02 else None)
                    ops.append((along, ow, oh, sill + (slab if k else 0.0), door))
                    if k == 0 and sill <= 0.02 and steps and plinth > 0.12:
                        # A step up to the door.
                        n = {"front": (0, -1), "right": (1, 0), "back": (0, 1), "left": (-1, 0)}[side]
                        px = off if side in ("front", "back") else n[0] * (w / 2 + 0.75)
                        py = off if side in ("right", "left") else n[1] * (d / 2 + 0.75)
                        sx, sy = (ow + 0.8, 1.0) if side in ("front", "back") else (1.0, ow + 0.8)
                        b.box((sx, sy, plinth), (px, py, plinth / 2), mat=CONCRETE, tint=srgb(0.72, 0.71, 0.68))
                z0 = plinth + k * floor_h
                wall(b, (a[0], a[1], z0), (c[0], c[1], z0), floor_h, wall_t, ops, mat, tint, frame=frame,
                     glass=glass, door_tint=door_tint)
        for k in range(1, floors + 1):
            z = plinth + k * floor_h
            hs = [h[:4] for h in holes if h[4] == k]
            if k == floors:
                slab_holes(b, -w / 2, -d / 2, w / 2, d / 2, z, slab, hs, mat=roof_mat, tint=roof_tint or tint)
            else:
                slab_holes(b, -w / 2 + wall_t / 2, -d / 2 + wall_t / 2, w / 2 - wall_t / 2, d / 2 - wall_t / 2, z,
                           slab, hs, mat=CONCRETE, tint=srgb(0.7, 0.69, 0.66))
        top = plinth + H + slab
        if parapet:
            for y in (-d / 2 + 0.12, d / 2 - 0.12):
                b.box((w, 0.24, parapet), (0, y, top + parapet / 2), mat=mat, tint=tint)
            for x in (-w / 2 + 0.12, w / 2 - 0.12):
                b.box((0.24, d - 0.48, parapet), (x, 0, top + parapet / 2), mat=mat, tint=tint)
            b.box((w + 0.08, d + 0.08, 0.07), (0, 0, top + parapet + 0.035), mat=PAINT, tint=GREY, solid=False,
                  vary=0)
        if band:
            bt, bz, bh = band
            z = plinth + bz
            for y in (-d / 2 - 0.16, d / 2 + 0.16):
                b.box((w + 0.34, 0.03, bh), (0, y, z), mat=PAINT, tint=bt, solid=False, vary=0, wear=0.5)
            for x in (-w / 2 - 0.16, w / 2 + 0.16):
                b.box((0.03, d + 0.3, bh), (x, 0, z), mat=PAINT, tint=bt, solid=False, vary=0, wear=0.5)
        for i in range(roof_units):
            ux = -w / 2 + (i + 0.5) * w / roof_units + b.rng.uniform(-0.5, 0.5)
            uy = b.rng.uniform(-d / 4, d / 4)
            b.box((1.8, 1.2, 1.0), (ux, uy, top + 0.5), mat=PAINT, tint=LIGHTGREY, bevel=0.03)
            b.cyl(0.42, 0.18, (ux, uy, top + 1.09), mat=PAINT, tint=GREY, sides=14, solid=False)
            b.box((1.6, 0.06, 0.6), (ux, uy - 0.62, top + 0.5), mat=PAINT, tint=DARK, solid=False, vary=0)
        if downpipes:
            for x, y in ((w / 2 + 0.1, -d / 2 + 0.3), (-w / 2 - 0.1, d / 2 - 0.3)):
                b.tube([(x, y, top + 0.1), (x, y, 0.35), (x + (0.3 if x > 0 else -0.3), y, 0.12)], 0.06,
                       tint=GREY, sides=6)
    return info


def roof_hatch(b, at, size=(1.0, 1.0), floor_z=0.0, roof_z=3.4, tint=YELLOW):
    """A hatch kerb on a roof over a hole, with a ladder up from the floor below."""
    x, y = at
    sx, sy = size
    for dx in (-sx / 2, sx / 2):
        b.box((0.1, sy + 0.1, 0.4), (x + dx, y, roof_z + 0.2), mat=PAINT, tint=GREY, solid=False)
    for dy in (-sy / 2, sy / 2):
        b.box((sx + 0.1, 0.1, 0.4), (x, y + dy, roof_z + 0.2), mat=PAINT, tint=GREY, solid=False)
    b.box((sx, 0.05, sy * 0.95), (x, y + sy / 2 + 0.05, roof_z + 0.4 + sy * 0.45), (-12, 0, 0), mat=PAINT,
          tint=GREY, solid=False)
    ladder(b, (x, y + sy / 2 - 0.05, floor_z), roof_z - floor_z + 0.2, (0, 0, 0), cage=False, tint=tint)


# ------------------------------------------------------------------------------------------------ stairs and decks


def switchback(b, at, rot, rise, width=1.0, flight_rise=2.6, tread=0.27, landing=1.2, gap=0.15, tint=YELLOW,
               rails=True, base_landing=False, landing_rails=True):
    """A switchback stair climbing `rise` from `at`: flights alternate up +Y and back down -Y, side by side (the
    first on the -X side), with grated landings at both ends. Returns the top landing's centre (current frame)."""
    n = max(1, int(math.ceil(rise / flight_rise - 1e-6)))
    fr = rise / n
    steps = max(2, int(round(fr / 0.19)))
    run = steps * tread
    xa, xb = -(width + gap) / 2, (width + gap) / 2
    full = 2 * width + gap
    top = None
    with b.frame(at, rot):
        for i in range(n):
            z = i * fr
            if i % 2 == 0:
                stairs(b, (xa, 0, z), (0, 0, 0), width, fr, run, tint=tint, rails=rails)
                ly = run + landing / 2
            else:
                stairs(b, (xb, run, z), (0, 0, 180), width, fr, run, tint=tint, rails=rails)
                ly = -landing / 2
            lz = z + fr
            b.box((full, landing, 0.06), (0, ly, lz - 0.03), mat=GRATE, tint=GREY, vary=0)
            b.box((full, 0.12, 0.16), (0, ly + (landing / 2 if ly > 0 else -landing / 2), lz - 0.1), mat=PAINT,
                  tint=DARK, solid=False, vary=0)
            edge = ly + (landing / 2 if ly > 0 else -landing / 2)
            if rails and landing_rails:
                railing(b, (-full / 2, edge, lz), (full / 2, edge, lz), tint=tint)
                near = ly - (landing / 2 if ly > 0 else -landing / 2)
                for sx in (-full / 2, full / 2):
                    railing(b, (sx, near, lz), (sx, edge, lz), tint=tint, post_end=False)
            top = b.world((0, ly, lz))
        if base_landing:
            b.box((full, landing, 0.06), (0, -landing / 2, -0.03), mat=GRATE, tint=GREY, vary=0)
    return top, run


def ring_deck(b, centre, r_in, r_out, z, sides=32, mat=GRATE, tint=GREY, thick=0.08, solid=True):
    """An annular deck round a vertical axis at `centre` (x, y), its walking surface at z; collision as a box per
    side."""
    cx, cy = centre[0], centre[1]
    b.lathe([(r_in, z - thick), (r_out, z - thick), (r_out, z), (r_in, z), (r_in, z - thick)], (cx, cy, 0),
            mat=mat, tint=tint, sides=sides, close=False, solid=False, vary=0)
    b.lathe([(r_out, z - thick - 0.2), (r_out + 0.02, z - thick - 0.2), (r_out + 0.02, z), (r_out, z),
             (r_out, z - thick - 0.2)], (cx, cy, 0), mat=PAINT, tint=DARK, sides=sides, close=False, solid=False,
            vary=0)
    if solid:
        rm = (r_in + r_out) / 2
        chord = 2 * r_out * math.sin(math.pi / sides) + 0.05
        for i in range(sides):
            a = 2 * math.pi * (i + 0.5) / sides
            b.collide_box(Vector((cx + math.cos(a) * rm, cy + math.sin(a) * rm, z - 0.05)), (r_out - r_in, chord, 0.1),
                          (0, 0, math.degrees(a)))


def ring_rail(b, centre, r, z, sides=32, height=1.1, tint=YELLOW, gaps=(), posts=2):
    """A guard rail round a circle (radius r at height z), leaving the sides in `gaps` open."""
    cx, cy = centre[0], centre[1]

    def pt(i, h=0.0):
        a = 2 * math.pi * i / sides
        return Vector((cx + math.cos(a) * r, cy + math.sin(a) * r, z + h))

    runs, cur = [], []
    for i in range(sides):
        if i in gaps:
            if cur:
                runs.append(cur)
                cur = []
            continue
        if not cur:
            cur = [i]
        cur.append(i + 1)
    if cur:
        runs.append(cur)
    if len(runs) > 1 and runs[0][0] == 0 and runs[-1][-1] == sides:
        runs[0] = runs[-1][:-1] + runs[0]
        runs.pop()
    for run in runs:
        b.tube([pt(i, height) for i in run], 0.028, tint=tint, sides=6, cap=False)
        b.tube([pt(i, height * 0.5) for i in run], 0.02, tint=tint, sides=5, cap=False)
        for k, i in enumerate(run):
            if k % posts == 0 or k == len(run) - 1:
                p = pt(i)
                b.box((0.05, 0.05, height), (p.x, p.y, z + height / 2), mat=PAINT, tint=tint, solid=False, vary=0)
        for i, j in zip(run, run[1:]):
            p, q = pt(i), pt(j)
            b.collide_box((p + q) / 2 + Vector((0, 0, height / 2)), ((q - p).length, 0.08, height), (0, 0, _yaw(q - p)))


def deck(b, x0, y0, x1, y1, z, mat=GRATE, tint=GREY, rails=(), beams=True, thick=0.06, rail_tint=YELLOW):
    """A rectangular platform, its surface at z, with rails on the listed edges ("front", "right", "back",
    "left") and edge beams under it."""
    b.box((x1 - x0, y1 - y0, thick), ((x0 + x1) / 2, (y0 + y1) / 2, z - thick / 2), mat=mat, tint=tint, vary=0)
    if beams:
        for y in (y0, y1):
            b.box((x1 - x0, 0.1, 0.2), ((x0 + x1) / 2, y, z - thick - 0.1), mat=PAINT, tint=DARK, solid=False,
                  vary=0)
        for x in (x0, x1):
            b.box((0.1, y1 - y0, 0.2), (x, (y0 + y1) / 2, z - thick - 0.1), mat=PAINT, tint=DARK, solid=False,
                  vary=0)
    edges = {"front": ((x0, y0), (x1, y0)), "right": ((x1, y0), (x1, y1)), "back": ((x1, y1), (x0, y1)),
             "left": ((x0, y1), (x0, y0))}
    for e in rails:
        (ax, ay), (cx, cy) = edges[e]
        railing(b, (ax, ay, z), (cx, cy, z), tint=rail_tint)


def concrete_steps(b, at, rot, width, rise, run, tint=srgb(0.72, 0.71, 0.68), mat=CONCRETE):
    """Solid concrete steps from `at` climbing `rise` toward +Y over `run`, with a collision ramp."""
    n = max(1, int(round(rise / 0.18)))
    h = rise / n
    dd = run / n
    with b.frame(at, rot):
        for i in range(n):
            b.box((width, run - dd * i, h), (0, dd * i + (run - dd * i) / 2, h * i + h / 2), mat=mat, tint=tint,
                  solid=False, vary=0.02)
        ang = math.degrees(math.atan2(rise, run))
        b.collide_box(Vector((0, run / 2, rise / 2 - 0.05)), (width, math.hypot(rise, run), 0.1), (ang, 0, 0))


def stair_tower(b, at, rot, height, w=3.0, d=7.0, stair_to=None, tint=srgb(0.62, 0.64, 0.64), stair_tint=YELLOW,
                girt=3.0, flight_rise=2.5):
    """An open steel stair tower `w` x `d` (long side along Y): four columns, girts every `girt` metres, X braces on
    the long faces, single braces on the short ones, and a switchback stair inside from the ground (entered from
    the -Y face) up to `stair_to` (default: the top). Returns (top landing centre, stair run, switchback y offset)."""
    stair_to = stair_to or height
    with b.frame(at, rot):
        for x in (-w / 2, w / 2):
            for y in (-d / 2, d / 2):
                b.box((0.26, 0.26, height), (x, y, height / 2), tint=tint)
                b.box((0.6, 0.6, 0.3), (x, y, 0.15), mat=CONCRETE, tint=CONCRETE_T, vary=0)
        levels = max(1, int(round(height / girt)))
        for k in range(1, levels + 1):
            z = height * k / levels
            for (x0, y0), (x1, y1) in (((-w / 2, -d / 2), (w / 2, -d / 2)), ((w / 2, -d / 2), (w / 2, d / 2)),
                                       ((w / 2, d / 2), (-w / 2, d / 2)), ((-w / 2, d / 2), (-w / 2, -d / 2))):
                b.beam((x0, y0, z), (x1, y1, z), 0.16, 0.16, tint=tint)
        for k in range(levels):
            z0, z1 = height * k / levels, height * (k + 1) / levels
            for x in (-w / 2, w / 2):
                b.beam((x, -d / 2, z0), (x, d / 2, z1), 0.1, 0.1, tint=tint)
                b.beam((x, d / 2, z0), (x, -d / 2, z1), 0.1, 0.1, tint=tint)
            y = d / 2
            if k % 2:
                b.beam((-w / 2, y, z0), (w / 2, y, z1), 0.1, 0.1, tint=tint)
            else:
                b.beam((w / 2, y, z0), (-w / 2, y, z1), 0.1, 0.1, tint=tint)
        n = max(1, int(math.ceil(stair_to / flight_rise - 1e-6)))
        steps = max(2, int(round(stair_to / n / 0.19)))
        run = steps * 0.27
        y0 = -(run + 1.2) / 2 + 1.2 / 2 - 0.0
        y0 = -(run) / 2
        top, run = switchback(b, (0, y0, 0), (0, 0, 0), stair_to, width=1.0, flight_rise=stair_to / n,
                              tint=stair_tint)
    return top, run, y0


# ------------------------------------------------------------------------------------------------ the dome


def geodesic(freq):
    """A geodesic sphere of radius 1: an icosahedron with a vertex at the top, each face split `freq` times.
    Returns (vertices as Vectors, triangles as index triples)."""
    t = (1 + 5 ** 0.5) / 2
    base = [Vector(v).normalized() for v in ((-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t),
                                             (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1))]
    tris = ((0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2),
            (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11),
            (6, 2, 10), (8, 6, 7), (9, 8, 1))
    q = base[0].rotation_difference(Vector((0, 0, 1)))
    base = [q @ v for v in base]
    verts, index, faces = [], {}, []

    def vid(p):
        p = p.normalized()
        key = (int(round(p.x * 20000)), int(round(p.y * 20000)), int(round(p.z * 20000)))
        if key not in index:
            index[key] = len(verts)
            verts.append(p)
        return index[key]

    for ia, ib, ic in tris:
        A, B, C = base[ia], base[ib], base[ic]
        grid = {}
        for i in range(freq + 1):
            for j in range(freq + 1 - i):
                grid[i, j] = vid(A + (B - A) * (i / freq) + (C - A) * (j / freq))
        for i in range(freq):
            for j in range(freq - i):
                faces.append((grid[i, j], grid[i + 1, j], grid[i, j + 1]))
                if i + j + 1 < freq:
                    faces.append((grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
    return verts, faces


def geodesic_dome(b, centre, radius, cut_z, freq=9, strut=0.34, tint=OFFWHITE, keep=0.5, solid_below=7.0,
                  panel_below=0.0, panel_tint=PALE, skip=None, holes=(), panel_keep=0.85):
    """A geodesic frame dome: a sphere of `radius` centred `centre` (x, y, z), cut flat at height `cut_z` (the
    vertices below it are moved down the meridians onto the cut circle). Struts as square beams; triangular panes
    of glass (a `keep` share of them), opaque panels below `panel_below` (a `panel_keep` share). `skip(p)` → True
    leaves the struts and panes round point p out (doors). `holes`: (azimuth, polar angle, angular radius) in degrees
    where the frame has torn open: struts and panes inside are gone, struts across the edge hang bent. Returns
    (vertex positions, kept faces, edges, fallen) with `fallen` the ground points under the holes."""
    verts, faces = geodesic(freq)
    cx, cy, cz = centre
    zr = cut_z - cz
    rc = math.sqrt(max(radius * radius - zr * zr, 0.0))
    pos, below = [], []
    for v in verts:
        p = Vector((cx + v.x * radius, cy + v.y * radius, cz + v.z * radius))
        if p.z < cut_z - 1e-6:
            h = Vector((v.x, v.y, 0.0))
            h = h.normalized() if h.length > 1e-6 else Vector((1.0, 0.0, 0.0))
            p = Vector((cx + h.x * rc, cy + h.y * rc, cut_z))
            below.append(True)
        else:
            below.append(False)
        pos.append(p)
    kept = []
    for f in faces:
        if all(below[i] for i in f):
            continue
        a, bb, c = (pos[i] for i in f)
        if (bb - a).cross(c - a).length < 0.05:
            continue
        kept.append(f)
    edges = sorted({tuple(sorted((f[i], f[(i + 1) % 3]))) for f in kept for i in range(3)})
    centre_v = Vector(centre)
    dirs = []
    for az, pol, rad in holes:
        a, p_ = math.radians(az), math.radians(pol)
        dirs.append((Vector((math.sin(p_) * math.cos(a), math.sin(p_) * math.sin(a), math.cos(p_))),
                     math.cos(math.radians(rad))))

    def torn(p):
        v = (p - centre_v).normalized()
        return any(v.dot(d) > c for d, c in dirs)

    fallen = []
    for d, c in dirs:
        g = centre_v + d * radius
        fallen.append(Vector((g.x * 0.92, g.y * 0.92, 0.0)))
    for i, j in edges:
        p, q = pos[i], pos[j]
        if (q - p).length < 0.2:
            continue
        mid = (p + q) / 2
        if skip and skip(mid):
            continue
        tp, tq = torn(p), torn(q)
        if tp and tq:
            continue
        if tp or tq:
            if tp:
                p, q = q, p
            # Bent: hanging from its sound end.
            ln = (q - p).length * b.rng.uniform(0.4, 0.75)
            dvec = ((q - p).normalized() * 0.45 + Vector((0, 0, -1)) * 0.55).normalized()
            b.beam(p, p + dvec * ln, strut, strut, tint=tint, solid=False)
            continue
        b.beam(p, q, strut, strut, tint=tint, solid=max(p.z, q.z) < solid_below)
    for f in kept:
        pts = [pos[i] for i in f]
        cen = (pts[0] + pts[1] + pts[2]) / 3
        if skip and skip(cen):
            continue
        if dirs and (torn(pts[0]) or torn(pts[1]) or torn(pts[2])):
            continue
        panel = cen.z < panel_below
        if b.rng.random() > (panel_keep if panel else keep):
            continue
        n = (cen - centre_v).normalized()
        inner = []
        for p in pts:
            dd = (cen - p)
            k = min(0.45, (strut * 0.75) / max(dd.length, 1e-3))
            inner.append(p + dd * k)
        th = 0.05 if panel else 0.03
        vs = [p + n * (th / 2) for p in inner] + [p - n * (th / 2) for p in inner]
        fs = [[0, 1, 2], [5, 4, 3], [0, 3, 4, 1], [1, 4, 5, 2], [2, 5, 3, 0]]
        if panel:
            b.mesh(vs, fs, mat=PANEL, tint=panel_tint, solid="hull" if cen.z < solid_below else False, vary=0.04,
                   smooth=False)
        else:
            b.mesh(vs, fs, mat=GLASS, solid="hull" if cen.z < solid_below else False, vary=0, smooth=False)
    return pos, kept, edges, fallen


def paraboloid_panels(b, r, f, z_off=0.0, rings=8, sectors=40, r_in=0.12, missing=0.08, tint=OFFWHITE,
                      thick=0.12):
    """The reflector of a dish: panels on the paraboloid z = z_off + rho^2 / (4 f) out to radius `r`, opening toward
    local +Z; a `missing` share gone (more toward the rim), a few in a replacement shade."""
    for i in range(rings):
        r0 = r * (r_in + (1 - r_in) * i / rings)
        r1 = r * (r_in + (1 - r_in) * (i + 1) / rings)
        for j in range(sectors):
            if b.rng.random() < missing * (0.4 + 1.2 * i / rings):
                continue
            a0 = 2 * math.pi * j / sectors
            a1 = 2 * math.pi * (j + 1) / sectors
            pts = [(rr * math.cos(aa), rr * math.sin(aa), z_off + rr * rr / (4 * f))
                   for rr, aa in ((r0, a0), (r1, a0), (r1, a1), (r0, a1))]
            back = [(p[0], p[1], p[2] - thick) for p in pts]
            t = PALE if b.rng.random() < 0.06 else tint
            b.mesh(pts + back, [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]],
                   mat=PANEL, tint=t, solid=False, vary=0.025, smooth=False)


def big_dish(b, at, diameter=60.0, elevation=50.0, azimuth=0.0, axis_h=30.0, base_z=9.5, f_ratio=0.36, yoke=9.0,
             tint=OFFWHITE, steel=srgb(0.66, 0.68, 0.68), missing=0.08, rings=8, sectors=40, spokes=20, leg=6.0):
    """A large az-el dish: an alidade of two braced A-frame towers standing on the azimuth track at `base_z`, the
    elevation bearings at `axis_h`, the reflector (panels on a paraboloid of focal ratio `f_ratio`) on a back
    structure of radial trusses and hoops, a cradle and counterweight behind, a quadripod carrying the
    subreflector at the focus. The dish opens toward -Y turned by `azimuth`, tilted up by `elevation`.
    Returns {"bearing": world points of the two bearings, "platform": height of the bearing platforms, ...}."""
    r = diameter / 2
    f = diameter * f_ratio
    depth = r * r / (4 * f)
    z_off = 2.0
    out = {}
    with b.frame(at, (0, 0, azimuth)):
        # The alidade: two A-frames on bogies, braced together, with platforms at the bearings.
        for s in (-1, 1):
            x = s * yoke
            for y in (-leg, leg):
                b.beam((x, y, base_z), (x, 0.0, axis_h - 0.8), 0.9, 0.9, tint=steel, solid=True)
            b.box((2.4, 2 * leg + 1.6, 1.4), (x, 0, base_z + 0.7), tint=srgb(0.4, 0.42, 0.42))
            for y in (-leg + 0.4, leg - 0.4):
                b.cyl(0.55, 0.5, (x, y, base_z + 0.3), (0, 90, 0), tint=DARK, sides=12, solid=False)
            for zz in (base_z + 6.0, base_z + 12.0):
                k = 1 - (zz - base_z) / (axis_h - base_z)
                b.beam((x, -leg * k, zz), (x, leg * k, zz), 0.4, 0.4, tint=steel)
            b.cyl(1.5, 1.6, (x, 0, axis_h), (0, 90, 0), tint=DARK, sides=20)
            b.box((3.2, 4.0, 0.15), (x + s * 0.4, 0, axis_h - 1.6), mat=GRATE, tint=GREY)
        for zz in (base_z + 1.6, base_z + 8.0):
            k = 1 - (zz - base_z) / (axis_h - base_z)
            for y in (-leg * k, leg * k):
                b.beam((-yoke, y, zz), (yoke, y, zz), 0.5, 0.5, tint=steel)
        b.beam((-yoke, -leg * 0.8, base_z + 1.6), (yoke, leg * 0.8, base_z + 1.6), 0.35, 0.35, tint=steel)
        out["bearing"] = [b.world((-yoke, 0, axis_h)), b.world((yoke, 0, axis_h))]
        out["platform"] = axis_h - 1.6
        with b.frame((0, 0, axis_h), (90 - elevation, 0, 0)):
            paraboloid_panels(b, r, f, z_off, rings, sectors, 0.12, missing, tint)
            # Back structure: radial trusses (two chords and a web), hoops.
            for j in range(spokes):
                a = 2 * math.pi * (j + 0.5) / spokes
                ca, sa = math.cos(a), math.sin(a)
                top, bot = [], []
                for k in range(6):
                    rho = r * (0.12 + 0.88 * k / 5)
                    zs = z_off + rho * rho / (4 * f) - 0.25
                    top.append((rho * ca, rho * sa, zs))
                    bot.append((rho * ca, rho * sa, zs - (3.2 - 2.4 * k / 5)))
                b.tube(top, 0.13, tint=steel, sides=6)
                b.tube(bot, 0.11, tint=steel, sides=6)
                for k in range(5):
                    b.beam(top[k], bot[k + 1], 0.08, 0.08, tint=steel)
                    b.beam(bot[k], top[k], 0.08, 0.08, tint=steel)
            for frac in (0.45, 0.75, 1.0):
                rho = r * frac
                zs = z_off + rho * rho / (4 * f) - 0.3
                pts = [(rho * math.cos(2 * math.pi * k / 40), rho * math.sin(2 * math.pi * k / 40), zs)
                       for k in range(41)]
                b.tube(pts, 0.12, tint=steel, sides=6, cap=False)
            # Hub, cradle to the bearings, counterweight.
            b.cyl(r * 0.13, 3.4, (0, 0, z_off - 1.2), tint=tint, sides=24)
            b.cyl(r * 0.13 + 0.05, 0.5, (0, 0, z_off - 0.2), tint=ORANGE, sides=24, solid=False, vary=0)
            for s in (-1, 1):
                for yy in (-3.0, 3.0):
                    b.beam((s * yoke, 0, 0), (s * 2.8, yy, z_off - 2.6), 0.6, 0.6, tint=steel)
                b.beam((s * yoke, 0, 0), (s * yoke * 0.5, 0, -6.5), 0.7, 0.7, tint=steel)
            b.box((yoke * 1.1, 4.0, 4.5), (0, 0, -8.5), mat=CONCRETE, tint=srgb(0.62, 0.62, 0.6))
            b.box((yoke * 1.1 + 0.1, 0.05, 0.6), (0, -2.03, -7.2), tint=YELLOW, solid=False, vary=0)
            # Quadripod and subreflector at the focus, feed cone at the hub.
            fz = z_off + f
            for k in range(4):
                a = math.pi / 4 + k * math.pi / 2
                rho = r * 0.66
                b.beam((rho * math.cos(a), rho * math.sin(a), z_off + rho * rho / (4 * f)), (0, 0, fz - 1.2), 0.45,
                       0.6, tint=steel)
            b.lathe([(0.0, fz - 0.6), (2.6, fz - 0.2), (2.7, fz + 0.1), (0.0, fz + 0.4)], (0, 0, 0), tint=tint,
                    sides=24)
            b.cyl(0.9, 2.4, (0, 0, fz - 1.6), tint=DARK, sides=16, solid=False)
            b.cyl(1.6, 4.0, (0, 0, z_off + 2.0), tint=tint, sides=20, radius2=0.7)
            out["rim_top"] = b.world((0, r, z_off + depth))
            out["focus"] = b.world((0, 0, fz))
    return out


def drum_building(b, at, radius, height, doors=(), mat=FORMWORK, tint=CONCRETE_T, band=ORANGE, segments=28,
                  roof=True):
    """A round building of wall panels with real doorways (`doors`: (angle in degrees, width, door dict or None)),
    a roof slab, a painted band."""
    x, y, z = at
    gaps = []
    for ang, w, door in doors:
        half = math.degrees((w / 2 + 0.25) / radius)
        gaps.append((ang - half, ang + half, ang, w, door))
    gaps.sort(key=lambda g: g[0])
    for k in range(segments):
        a0, a1 = 360.0 * k / segments, 360.0 * (k + 1) / segments
        spans = [(a0, a1)]
        for g in gaps:
            g0, g1 = g[0], g[1]
            new = []
            for s0, s1 in spans:
                hit = False
                for lo, hi in ((g0, g1), (g0 + 360, g1 + 360), (g0 - 360, g1 - 360)):
                    if hi <= s0 or lo >= s1:
                        continue
                    if lo > s0:
                        new.append((s0, lo))
                    if hi < s1:
                        new.append((hi, s1))
                    hit = True
                    break
                if not hit:
                    new.append((s0, s1))
            spans = new
        for s0, s1 in spans:
            if s1 - s0 < 0.2:
                continue
            mid = math.radians((s0 + s1) / 2)
            ln = 2 * radius * math.sin(math.radians(s1 - s0) / 2) + 0.04
            b.box((ln, 0.4, height), (x + math.cos(mid) * radius, y + math.sin(mid) * radius, z + height / 2),
                  (0, 0, math.degrees(mid) + 90), mat=mat, tint=tint)
            if band:
                b.box((ln, 0.04, 0.6), (x + math.cos(mid) * (radius + 0.22), y + math.sin(mid) * (radius + 0.22),
                                         z + height - 1.2), (0, 0, math.degrees(mid) + 90), mat=PAINT, tint=band,
                      solid=False, vary=0, wear=0.5)
    for g0, g1, ang, w, door in gaps:
        a = math.radians(ang)
        cx, cy = x + math.cos(a) * radius, y + math.sin(a) * radius
        with b.frame((cx, cy, z), (0, 0, ang + 90)):
            b.box((w + 0.5, 0.42, height - 2.5), (0, 0, 2.5 + (height - 2.5) / 2), mat=mat, tint=tint)
            for xx in (-w / 2, w / 2):
                b.box((0.1, 0.5, 2.5), (xx, 0, 1.25), mat=PAINT, tint=DARK, solid=False, vary=0)
            if door is not None:
                st = door.get("state", "shut" if door.get("chip") else "ajar")
                if st == "shut":
                    door_leaf(b, (-w / 2, 0, 0), w, 2.45, 0, tint=door.get("tint", GREY))
                elif st != "gone":
                    door_leaf(b, (-w / 2, 0.1, 0), w, 2.45, 80.0, tint=door.get("tint", GREY))
                if door.get("chip"):
                    reader(b, (w / 2 + 0.4, -0.21, 0), (0, 0, 0), door["chip"], door.get("cell", False))
                props = {k: v for k, v in door.items() if k in ("kind_of", "chip", "cell", "room")}
                props.setdefault("kind_of", "door")
                b.marker("door", (0, 0, 0), 180.0, width=round(w, 2), height=2.45, **props)
    if roof:
        b.cyl(radius + 0.35, 0.4, (x, y, z + height + 0.2), mat=CONCRETE, tint=tint, sides=segments)


def cable_trench(b, points, width=0.8, tint=CONCRETE_T):
    """Precast covers over a cable trench along `points` (flush with the ground), every 1.5 m."""
    pts = [Vector(p) for p in points]
    for a, c in zip(pts, pts[1:]):
        d = c - a
        n = max(1, int(d.length / 1.5))
        yaw = _yaw(d)
        for i in range(n):
            p = a + d * ((i + 0.5) / n)
            b.box((d.length / n - 0.05, width, 0.12), (p.x, p.y, p.z + 0.06), (0, 0, yaw), mat=CONCRETE, tint=tint,
                  solid=False, vary=0.04, wear=0.6)


# ------------------------------------------------------------------------------------------------ overgrowth


def blob(b, at, rx, rz, tint=LEAF, sides=10, rot=(0, 0, 0)):
    """A rough ellipsoid of foliage (or a stone): `rx` across, `rz` up; smooth-shaded from 10 sides up, faceted
    below (cheap, for small ones)."""
    if sides >= 10:
        prof = [(0.0, -rz), (rx * 0.5, -rz * 0.87), (rx * 0.87, -rz * 0.5), (rx, 0.0), (rx * 0.87, rz * 0.5),
                (rx * 0.5, rz * 0.87), (0.0, rz)]
    else:
        prof = [(0.0, -rz), (rx * 0.7, -rz * 0.7), (rx, 0.0), (rx * 0.7, rz * 0.7), (0.0, rz)]
    return b.lathe(prof, at, rot, mat=MATTE, tint=tint, sides=sides, solid=False, vary=0.12, wear=0.2)


def tree(b, at, height=10.0, spread=3.5, trunk=0.3, leaves=True, lean=0.0, tints=(LEAF, LEAF2, LEAF3),
         blobs=6, solid=True):
    """A broadleaf tree: a tapering, slightly bent trunk, branches, and a crown of foliage clumps (or bare when
    `leaves` is False)."""
    rng = b.rng
    x, y, z = at
    ld = rng.uniform(0, 2 * math.pi)
    pts, radii = [], []
    for k in range(5):
        t = k / 4
        off = lean * height * t * t + rng.uniform(-0.15, 0.15) * t
        pts.append((x + math.cos(ld) * off, y + math.sin(ld) * off, z + height * 0.62 * t))
        radii.append(trunk * (1.0 - 0.55 * t) * (1.25 if k == 0 else 1.0))
    b.tube(pts, trunk, mat=MATTE, tint=BARK, sides=7, radii=radii, solid=False, vary=0.08)
    top = Vector(pts[-1])
    ends = []
    for k in range(rng.randint(3, 5)):
        a = rng.uniform(0, 2 * math.pi)
        el = math.radians(rng.uniform(25, 60))
        length = spread * rng.uniform(0.6, 1.0)
        start = top - Vector((0, 0, rng.uniform(0.0, height * 0.22)))
        end = start + Vector((math.cos(a) * math.cos(el) * length, math.sin(a) * math.cos(el) * length,
                              math.sin(el) * length))
        mid = start.lerp(end, 0.5) + Vector((0, 0, 0.3))
        b.tube([start, mid, end], trunk * 0.3, mat=MATTE, tint=BARK, sides=5,
               radii=[trunk * 0.42, trunk * 0.25, trunk * 0.08], solid=False, cap=False)
        ends.append(end)
    if leaves:
        for k in range(blobs):
            base = ends[k % len(ends)] if k < len(ends) else top + Vector((0, 0, height * 0.25))
            c = base + Vector((rng.uniform(-1, 1) * spread * 0.35, rng.uniform(-1, 1) * spread * 0.35,
                               rng.uniform(-0.2, 0.6) * spread * 0.4))
            r = spread * rng.uniform(0.38, 0.6)
            blob(b, tuple(c), r, r * rng.uniform(0.6, 0.85), tints[k % len(tints)],
                 rot=(0, 0, rng.uniform(0, 360)))
    if solid:
        b.collide_box(Vector((x, y, z + height * 0.3)), (trunk * 1.6, trunk * 1.6, height * 0.6), (0, 0, 0))


def shrub(b, at, size=1.2, tints=(LEAF, LEAF2, LEAF3, LEAF_DRY), count=2):
    """A low clump of bushes."""
    x, y, z = at
    for k in range(count):
        r = size * b.rng.uniform(0.5, 0.9)
        blob(b, (x + b.rng.uniform(-size, size) * 0.6, y + b.rng.uniform(-size, size) * 0.6, z + r * 0.45), r,
             r * b.rng.uniform(0.55, 0.8), tints[b.rng.randrange(len(tints))], sides=10,
             rot=(0, 0, b.rng.uniform(0, 360)))


def vines(b, top, length, count=3, tint=LEAF, spread=0.5):
    """Creepers hanging from `top`."""
    t = Vector(top)
    for k in range(count):
        dx, dy = b.rng.uniform(-spread, spread), b.rng.uniform(-spread, spread)
        ln = length * b.rng.uniform(0.5, 1.0)
        pts = [t + Vector((dx, dy, 0)), t + Vector((dx * 1.3, dy * 1.3, -ln * 0.5)),
               t + Vector((dx * 1.1 + 0.1, dy * 1.1, -ln))]
        b.tube(pts, 0.035, mat=MATTE, tint=tint, sides=4, cap=False, vary=0.15)
        blob(b, tuple(pts[-1]), 0.3, 0.45, tint, sides=5)


# ------------------------------------------------------------------------------------------------ signs and marks

# Seven-segment stencils: segments a (top), b (top right), c (bottom right), d (bottom), e (bottom left),
# f (top left), g (middle).
_SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc", "7": "abc",
        "8": "abcdefg", "9": "abcdfg", "A": "abcefg", "B": "cdefg", "C": "adef", "D": "bcdeg", "E": "adefg",
        "F": "aefg", "H": "bcefg", "J": "bcde", "L": "def", "N": "ceg", "O": "cdeg", "P": "abefg", "R": "eg",
        "S": "afgcd", "T": "defg", "U": "bcdef", "Y": "bcdfg", "-": "g"}


def stencil(b, at, rot, text, height=1.0, tint=DARK, thick=0.015, mat=PAINT):
    """Stencilled characters (seven-segment, in our own style) painted on a surface whose face is toward -Y of
    `rot`; `at` is the text's centre."""
    w = height * 0.55
    gap = height * 0.22
    sw = height * 0.13
    total = len(text) * w + (len(text) - 1) * gap
    with b.frame(at, rot):
        for i, ch in enumerate(text.upper()):
            segs = _SEG.get(ch, "")
            cx = -total / 2 + i * (w + gap) + w / 2
            h2 = height / 2
            for s in segs:
                if s in "adg":
                    z = {"a": h2 - sw / 2, "g": 0.0, "d": -h2 + sw / 2}[s]
                    b.box((w - sw * 0.6, thick, sw), (cx, -thick / 2, z), mat=mat, tint=tint, solid=False, vary=0,
                          wear=0.6)
                else:
                    xx = cx + (w / 2 - sw / 2) * (1 if s in "bc" else -1)
                    z = h2 / 2 if s in "bf" else -h2 / 2
                    b.box((sw, thick, h2 - sw * 0.3), (xx, -thick / 2, z), mat=mat, tint=tint, solid=False, vary=0,
                          wear=0.6)


def code_plate(b, at, rot=(0, 0, 0), w=1.2, h=0.45, base=OFFWHITE, ink=DARK, text=None, stripe=None, thick=0.03):
    """A stencilled plate (its face toward -Y of `rot`): a coloured board, an optional stripe along the bottom, and
    a code in stencil characters (random glyph strokes when `text` is None)."""
    with b.frame(at, rot):
        b.box((w, thick, h), (0, 0, 0), mat=PAINT, tint=base, solid=False, vary=0, wear=0.55)
        if stripe:
            b.box((w, thick + 0.006, h * 0.2), (0, -0.003, -h * 0.4), mat=PAINT, tint=stripe, solid=False, vary=0)
        if text:
            stencil(b, (0, -thick / 2 - 0.004, h * 0.06), (0, 0, 0), text, height=h * 0.55, tint=ink, thick=0.008)
        else:
            n = max(2, int(w / (h * 0.5)))
            gw = w * 0.8 / n
            for i in range(n):
                x = -w * 0.4 + gw * (i + 0.5)
                for s in range(1 + b.rng.randrange(2)):
                    if b.rng.random() < 0.5:
                        b.box((gw * 0.18, 0.008, h * 0.45), (x + b.rng.uniform(-0.2, 0.2) * gw, -thick / 2 - 0.004,
                                                            h * 0.05), mat=PAINT, tint=ink, solid=False, vary=0)
                    else:
                        b.box((gw * 0.6, 0.008, h * 0.08), (x, -thick / 2 - 0.004, h * b.rng.choice((-0.15, 0.05, 0.25))),
                              mat=PAINT, tint=ink, solid=False, vary=0)


def mark(b, at, rot, size=1.5, kind="slash", tint=None, thick=0.012):
    """A Reclaimer mark sprayed on a surface (face toward -Y of `rot`): "slash" (their three-stroke tag), "ring",
    "tally", "arrow", "skull" (a warning: two eyes and a jaw), "zigzag"."""
    tint = tint or GRAFFITI[b.rng.randrange(len(GRAFFITI))]
    s = size
    with b.frame(at, rot):
        def stroke(x0, z0, x1, z1, wd=0.09):
            dx, dz = x1 - x0, z1 - z0
            ln = math.hypot(dx, dz)
            ang = math.degrees(math.atan2(dz, dx))
            b.box((ln, thick, wd * s), ((x0 + x1) / 2, -thick / 2, (z0 + z1) / 2), (0, -ang, 0), mat=MATTE,
                  tint=tint, solid=False, vary=0.05, wear=0.3)
        if kind == "slash":
            for k in range(3):
                stroke(-0.35 * s + k * 0.3 * s, -0.4 * s, -0.15 * s + k * 0.3 * s, 0.4 * s)
            stroke(-0.45 * s, -0.05 * s, 0.45 * s, 0.05 * s, 0.06)
        elif kind == "ring":
            n = 10
            for k in range(n):
                a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
                stroke(math.cos(a0) * 0.4 * s, math.sin(a0) * 0.4 * s, math.cos(a1) * 0.4 * s, math.sin(a1) * 0.4 * s,
                       0.07)
            stroke(-0.3 * s, -0.3 * s, 0.3 * s, 0.3 * s, 0.07)
        elif kind == "tally":
            for k in range(4):
                stroke(-0.3 * s + k * 0.2 * s, -0.35 * s, -0.3 * s + k * 0.2 * s, 0.35 * s, 0.06)
            stroke(-0.45 * s, -0.3 * s, 0.45 * s, 0.3 * s, 0.06)
        elif kind == "arrow":
            stroke(-0.5 * s, 0, 0.4 * s, 0, 0.1)
            stroke(0.15 * s, 0.25 * s, 0.45 * s, 0, 0.1)
            stroke(0.15 * s, -0.25 * s, 0.45 * s, 0, 0.1)
        elif kind == "skull":
            for x in (-0.18 * s, 0.18 * s):
                b.box((0.2 * s, thick, 0.2 * s), (x, -thick / 2, 0.1 * s), (0, 45, 0), mat=MATTE, tint=tint,
                      solid=False, vary=0)
            stroke(-0.3 * s, -0.25 * s, 0.3 * s, -0.25 * s, 0.07)
            for k in range(4):
                x = -0.22 * s + k * 0.15 * s
                stroke(x, -0.35 * s, x, -0.17 * s, 0.04)
        else:
            pts = [(-0.5 * s + k * 0.2 * s, (0.2 if k % 2 else -0.2) * s) for k in range(6)]
            for p, q in zip(pts, pts[1:]):
                stroke(p[0], p[1], q[0], q[1], 0.07)


def hazard_band(b, a, c, z, height=0.3, normal=(0, -1, 0), width=0.3):
    """Hazard stripes along a wall from a to c (kit.stripes, at height z)."""
    kit.stripes(b, a, c, height, width=width, at_z=z, normal=normal)


def aviation_lamp(b, at, blink=1.0, size=0.35):
    """A red obstruction lamp on top of something tall; blinks at night."""
    x, y, z = at
    b.cyl(size * 0.4, size * 0.5, (x, y, z + size * 0.25), mat=PAINT, tint=DARK, sides=8, solid=False, vary=0)
    b.cyl(size * 0.5, size * 0.6, (x, y, z + size * 0.8), mat=LAMP, tint=RED_LAMP, sides=10, solid=False, vary=0)
    b.marker("light", (x, y, z + size), colour=[round(v, 3) for v in RED_LAMP], range=60.0, blink=blink)


# ------------------------------------------------------------------------------------------------ small structures


def polytunnel(b, at, rot, w=5.0, length=14.0, torn=0.35, tint=srgb(0.86, 0.88, 0.84)):
    """A plastic-covered growing tunnel: steel hoops every 1.5 m, plastic sheeting in strips (some torn away), end
    frames with a door."""
    r = w / 2
    with b.frame(at, rot):
        n = int(length / 1.5)
        for k in range(n + 1):
            y = -length / 2 + length * k / n
            pts = [(r * math.cos(math.pi * i / 10), y, r * math.sin(math.pi * i / 10)) for i in range(11)]
            b.tube(pts, 0.03, tint=GREY, sides=5, cap=False)
        strips = int(length / 2.0)
        for k in range(strips):
            if b.rng.random() < torn:
                continue
            y = -length / 2 + length * (k + 0.5) / strips
            outer = [(r * 1.01 * math.cos(math.pi * i / 10), r * 1.01 * math.sin(math.pi * i / 10)) for i in range(11)]
            inner = [(x * 0.985, z * 0.985) for x, z in reversed(outer)]
            b.extrude(outer + inner, length / strips + 0.02, (0, y, 0), (0, 0, 0), mat=PANEL, tint=tint,
                      solid=False, vary=0.05, wear=0.5)
        for y in (-length / 2, length / 2):
            for x in (-0.6, 0.6):
                b.box((0.06, 0.06, 2.1), (x, y, 1.05), mat=PAINT, tint=GREY, solid=False)
        b.collide_box(Vector((0, 0, r * 0.45)), (w * 0.95, length, r * 0.9), (0, 0, 0))


def planter(b, at, rot, w, d, h=0.5, soil=SOIL, kerb=srgb(0.70, 0.69, 0.65), plants=0.6, dry=False):
    """A raised planter bed: concrete kerbs, soil, and shrubs gone wild in it."""
    with b.frame(at, rot):
        for y in (-d / 2 + 0.1, d / 2 - 0.1):
            b.box((w, 0.2, h), (0, y, h / 2), mat=CONCRETE, tint=kerb)
        for x in (-w / 2 + 0.1, w / 2 - 0.1):
            b.box((0.2, d - 0.4, h), (x, 0, h / 2), mat=CONCRETE, tint=kerb)
        b.box((w - 0.4, d - 0.4, 0.06), (0, 0, h - 0.1), mat=MATTE, tint=soil, solid=False, vary=0.05)
        n = int(w * d * plants / 3.0)
        tints = (LEAF_DRY, LEAF3, SAND) if dry else (LEAF, LEAF2, LEAF3, LEAF_DRY)
        for k in range(n):
            x = b.rng.uniform(-w / 2 + 0.5, w / 2 - 0.5)
            y = b.rng.uniform(-d / 2 + 0.5, d / 2 - 0.5)
            r = b.rng.uniform(0.35, 0.8)
            blob(b, (x, y, h - 0.05 + r * 0.35), r, r * b.rng.uniform(0.5, 0.9), tints[b.rng.randrange(len(tints))],
                 sides=10, rot=(0, 0, b.rng.uniform(0, 360)))


def tank_low(b, at, radius, height, tint=OFFWHITE, band=None, ladder_side=None):
    """A storage tank (kit.tank with a cone roof)."""
    kit.tank(b, at, radius, height, tint=tint, band=band, ladder_side=ladder_side)


def pipe_run(b, points, radius=0.2, tint=GREY, flanges=False, supports=0.0):
    """A pipe along `points` (kit.pipe), flanges off by default to save triangles."""
    kit.pipe(b, points, radius, tint=tint, flanges=flanges, supports=supports)


def lean_to(b, at, rot, w, d, h_front=3.0, h_back=3.6, tint=srgb(0.6, 0.62, 0.62), frame=DARK, missing=0.15,
            mat=CORRUGATED, posts=True):
    """An open-fronted corrugated roof on posts (front toward -Y), sloping up to the back."""
    with b.frame(at, rot):
        nx = max(1, int(round(w / 4.0)))
        for i in range(nx + 1):
            x = -w / 2 + w * i / nx
            if posts:
                b.box((0.15, 0.15, h_front), (x, -d / 2, h_front / 2), mat=PAINT, tint=frame)
                b.box((0.15, 0.15, h_back), (x, d / 2, h_back / 2), mat=PAINT, tint=frame)
            b.beam((x, -d / 2 - 0.2, h_front), (x, d / 2 + 0.2, h_back), 0.12, 0.2, tint=frame)
        strips = max(2, int(round(w / 1.2)))
        ang = math.degrees(math.atan2(h_back - h_front, d))
        ln = math.hypot(d + 0.6, h_back - h_front)
        for k in range(strips):
            if b.rng.random() < missing:
                continue
            x = -w / 2 + w * (k + 0.5) / strips
            b.box((w / strips + 0.03, ln, 0.04), (x, 0, (h_front + h_back) / 2 + 0.12), (ang, 0, 0), mat=mat,
                  tint=tint, solid=False, vary=0.06)
        b.collide_box(Vector((0, 0, (h_front + h_back) / 2 + 0.12)), (w, ln, 0.1), (ang, 0, 0))


def wire_fence(b, points, post=4.0, height=1.3, broken=0.15, tint=WOOD, wire=srgb(0.35, 0.33, 0.3)):
    """A farm fence of timber posts and two wires along `points`; `broken` leaves posts leaning or gone and spans
    of wire down."""
    pts = [Vector(p) for p in points]
    for a, c in zip(pts, pts[1:]):
        d = c - a
        n = max(1, int(math.ceil(d.length / post)))
        tops = []
        for i in range(n + 1):
            p = a + d * (i / n)
            r = b.rng.random()
            if r < broken * 0.4:
                tops.append(None)
                continue
            lean = b.rng.uniform(-25, 25) if r < broken else b.rng.uniform(-3, 3)
            with b.frame(tuple(p), (lean, 0, _yaw(d) + b.rng.uniform(-10, 10))):
                b.box((0.12, 0.12, height + 0.1), (0, 0, (height + 0.1) / 2), mat=MATTE, tint=tint, solid=False,
                      vary=0.1, wear=0.6)
            tops.append(p.copy())
        for i in range(n):
            if tops[i] is None or tops[i + 1] is None or b.rng.random() < broken * 0.5:
                continue
            for z in (height * 0.45, height * 0.9):
                cable(b, tops[i] + Vector((0, 0, z)), tops[i + 1] + Vector((0, 0, z)), sag=0.015, radius=0.008,
                      tint=wire, segments=3)


# ------------------------------------------------------------------------------------------------ Reclaimer work

SCRAP = ((CORRUGATED, srgb(0.62, 0.62, 0.6)), (CORRUGATED, srgb(0.70, 0.45, 0.3)), (RUST, (1.0, 1.0, 1.0)),
         (RUST, (1.0, 1.0, 1.0)), (PAINT, srgb(0.36, 0.48, 0.55)), (PAINT, srgb(0.62, 0.30, 0.18)),
         (PAINT, srgb(0.78, 0.74, 0.62)), (HULL, srgb(0.75, 0.75, 0.73)), (PAINT, srgb(0.38, 0.44, 0.3)))


def scrap_wall(b, a, c, height=2.6, gaps=0.06, posts=2.6, marks=0.25):
    """A Reclaimer wall of welded scrap from `a` to `c`: pipe and rail posts, overlapping sheets of corrugated,
    rusted and painted steel and hull plate of uneven sizes, some tilted; `gaps` leaves holes; sprayed marks."""
    a, c = Vector(a), Vector(c)
    d = Vector((c.x - a.x, c.y - a.y, 0.0))
    length = d.length
    rng = b.rng
    with b.frame(tuple(a), (0, 0, _yaw(d))):
        n = max(1, int(math.ceil(length / posts)))
        for i in range(n + 1):
            x = length * i / n
            h = height + rng.uniform(0.1, 0.6)
            b.box((0.12, 0.12, h), (x, 0.1, h / 2), (rng.uniform(-3, 3), rng.uniform(-3, 3), 0), mat=RUST,
                  tint=(1, 1, 1), solid=False, vary=0.05)
        x = 0.0
        while x < length - 0.05:
            w = min(rng.uniform(0.9, 2.2), length - x)
            z0 = 0.0
            for row in range(2):
                h = rng.uniform(1.1, 1.6) if row == 0 else max(0.4, height - z0 + rng.uniform(-0.35, 0.25))
                if rng.random() >= gaps:
                    mat, tint = SCRAP[rng.randrange(len(SCRAP))]
                    b.box((w + rng.uniform(0.1, 0.3), 0.04, h), (x + w / 2, rng.uniform(-0.07, 0.07), z0 + h / 2),
                          (0, rng.uniform(-5, 5), rng.uniform(-1.5, 1.5)), mat=mat, tint=tint, solid=False,
                          vary=0.05, wear=0.7)
                z0 += h - rng.uniform(0.05, 0.2)
            if rng.random() < marks:
                mark(b, (x + w / 2, -0.12, rng.uniform(0.8, 1.6)), (0, 0, 0), rng.uniform(0.7, 1.2),
                     rng.choice(("slash", "ring", "tally", "arrow", "zigzag")))
            x += w
        b.collide_box(Vector((length / 2, 0, height / 2)), (length, 0.25, height), (0, 0, 0))


def sheet(b, grid, thick=0.03, mat=MATTE, tint=TARP_BLUE, solid=False):
    """A thin closed sheet (tarp, canvas, membrane) through a grid of points (rows of equal length)."""
    ny, nx = len(grid), len(grid[0])
    up = Vector((0, 0, thick / 2))
    verts = [Vector(p) + up for row in grid for p in row] + [Vector(p) - up for row in grid for p in row]
    off = nx * ny
    faces = []
    for j in range(ny - 1):
        for i in range(nx - 1):
            q = [j * nx + i, j * nx + i + 1, (j + 1) * nx + i + 1, (j + 1) * nx + i]
            faces.append(q)
            faces.append([v + off for v in reversed(q)])
    ring = [i for i in range(nx)] + [j * nx + nx - 1 for j in range(1, ny)] + \
           [(ny - 1) * nx + i for i in range(nx - 2, -1, -1)] + [j * nx for j in range(ny - 2, 0, -1)]
    for k in range(len(ring)):
        u, v = ring[k], ring[(k + 1) % len(ring)]
        faces.append([u, v, v + off, u + off])
    return b.mesh(verts, faces, mat=mat, tint=tint, solid=solid, vary=0.06, smooth=True, wear=0.6)


def tarp_shelter(b, at, rot, w=4.0, d=3.0, h_front=2.3, h_back=2.0, tint=TARP_BLUE, sag=0.25, tattered=False):
    """A tarp stretched over four poles, sagging in the middle (front toward -Y)."""
    rng = b.rng
    with b.frame(at, rot):
        for x in (-w / 2, w / 2):
            for y, hh in ((-d / 2, h_front), (d / 2, h_back)):
                b.cyl(0.04, hh + 0.1, (x, y, (hh + 0.1) / 2), mat=PAINT, tint=GREY, sides=6, solid=False, vary=0)
        nx, ny = 5, 4
        grid = []
        for j in range(ny + 1):
            v = j / ny
            row = []
            for i in range(nx + 1):
                u = i / nx
                z = h_front + (h_back - h_front) * v - sag * math.sin(math.pi * u) * math.sin(math.pi * v)
                edge = tattered and j == 0
                row.append((-w / 2 - 0.15 + (w + 0.3) * u, -d / 2 - 0.25 + (d + 0.4) * v,
                            z - (rng.uniform(0.0, 0.5) if edge else 0.0)))
            grid.append(row)
        sheet(b, grid, tint=tint)
        for x in (-w / 2, w / 2):
            cable(b, (x, -d / 2, h_front), (x + (0.8 if x > 0 else -0.8), -d / 2 - 1.2, 0.05), sag=0.0, radius=0.01,
                  tint=srgb(0.5, 0.45, 0.35), segments=1)
        b.collide_box(Vector((0, 0, (h_front + h_back) / 2)), (w, d, 0.1), (0, 0, 0))


def string_lights(b, a, c, sag=0.06, spacing=1.2, colour=WARM_LAMP, marker_every=3):
    """Bulbs strung on a cable between two points; a light marker every few bulbs."""
    a, c = Vector(a), Vector(c)
    cable(b, a, c, sag=sag, radius=0.012, tint=BLACK, segments=8)
    ln = (c - a).length
    n = max(1, int(ln / spacing))
    for i in range(1, n):
        t = i / n
        p = a.lerp(c, t)
        p.z -= 4 * sag * ln * t * (1 - t) + 0.08
        b.box((0.08, 0.08, 0.11), tuple(p), mat=LAMP, tint=colour, solid=False, vary=0)
        if i % (marker_every * 2) == marker_every:
            b.marker("light", tuple(p), colour=[round(v, 3) for v in colour], range=8.0)


def barricade(b, at, rot, length=6.0, kind="mixed"):
    """A Reclaimer barricade: tyres, sandbags, a sheet, a jersey barrier."""
    rng = b.rng
    with b.frame(at, rot):
        x = -length / 2
        while x < length / 2:
            k = rng.random()
            if k < 0.35:
                for z in range(rng.randint(2, 4)):
                    b.cyl(0.42, 0.26, (x + 0.45, rng.uniform(-0.05, 0.05), 0.13 + z * 0.26), mat=MATTE,
                          tint=srgb(0.09, 0.09, 0.09), sides=12, solid=False, vary=0.05)
                x += 0.95
            elif k < 0.7:
                for row in range(3):
                    for i in range(2):
                        b.box((0.6, 0.38, 0.17), (x + 0.32 + i * 0.6 + (0.3 if row % 2 else 0), 0, 0.09 + row * 0.16),
                              (0, 0, rng.uniform(-6, 6)), mat=MATTE, tint=srgb(0.55, 0.50, 0.38), bevel=0.06,
                              solid=False, vary=0.08)
                x += 1.3
            else:
                mat, tint = SCRAP[rng.randrange(len(SCRAP))]
                b.box((1.6, 0.05, 1.3), (x + 0.8, 0.1, 0.62), (rng.uniform(-10, 10), 0, rng.uniform(-8, 8)), mat=mat,
                      tint=tint, solid=False)
                x += 1.6
        b.collide_box(Vector((0, 0, 0.6)), (length, 0.6, 1.2), (0, 0, 0))


def shack(b, at, rot, w=4.0, d=3.5, h=2.5, stilts=0.0, door=True, roof_tint=srgb(0.6, 0.55, 0.5), pipe=True,
          window=True):
    """A Reclaimer shack (front toward -Y): a plank floor (on stilts with steps when `stilts` > 0), welded scrap
    walls with a doorway, a corrugated mono-pitch roof, a stovepipe. Returns the floor height."""
    rng = b.rng
    z = stilts
    with b.frame(at, rot):
        if stilts > 0:
            for x in (-w / 2 + 0.2, w / 2 - 0.2):
                for y in (-d / 2 + 0.2, d / 2 - 0.2):
                    b.box((0.18, 0.18, stilts + 0.1), (x, y, stilts / 2), mat=MATTE, tint=WOOD, solid=False,
                          vary=0.1)
            stairs(b, (-w / 2 + 0.8, -d / 2 - stilts * 1.4, 0), (0, 0, 0), 0.9, stilts, stilts * 1.4, tint=WOOD,
                   grate=False, rails=False)
        b.box((w + 0.2, d + 0.2, 0.12), (0, 0, z + 0.06), mat=MATTE, tint=WOOD, vary=0.08)
        for k in range(int(w / 0.6)):
            b.box((0.02, d + 0.2, 0.005), (-w / 2 + 0.3 + k * 0.6, 0, z + 0.122), mat=MATTE, tint=srgb(0.3, 0.24, 0.18),
                  solid=False, vary=0)
        door_w = 0.95
        dx = -w / 2 + 0.8 + door_w / 2
        hw, hd = w / 2, d / 2
        if door:
            scrap_wall(b, (-hw, -hd, z), (dx - door_w / 2, -hd, z), height=h, marks=0.0, posts=1.2)
            scrap_wall(b, (dx + door_w / 2, -hd, z), (hw, -hd, z), height=h, marks=0.4, posts=2.0)
            b.box((door_w + 0.2, 0.1, 0.12), (dx, -hd, z + 2.1), mat=RUST, tint=(1, 1, 1), solid=False)
            b.marker("door", (dx, -hd, z), 180.0, kind_of="shack", width=door_w, height=2.0)
        else:
            scrap_wall(b, (-hw, -hd, z), (hw, -hd, z), height=h, marks=0.4)
        scrap_wall(b, (hw, -hd, z), (hw, hd, z), height=h + 0.3, marks=0.2)
        scrap_wall(b, (hw, hd, z), (-hw, hd, z), height=h + 0.5, marks=0.2)
        scrap_wall(b, (-hw, hd, z), (-hw, -hd, z), height=h + 0.3, marks=0.2)
        ang = math.degrees(math.atan2(0.5, d))
        ln = math.hypot(d + 0.6, 0.5)
        strips = max(2, int(round((w + 0.6) / 0.9)))
        for k in range(strips):
            x = -w / 2 - 0.3 + (w + 0.6) * (k + 0.5) / strips
            b.box(((w + 0.6) / strips + 0.04, ln, 0.04), (x, 0, z + h + 0.3 + rng.uniform(-0.03, 0.03)),
                  (ang + rng.uniform(-2, 2), rng.uniform(-2, 2), 0), mat=CORRUGATED, tint=roof_tint, solid=False,
                  vary=0.08)
        b.collide_box(Vector((0, 0, z + h + 0.3)), (w + 0.6, ln, 0.1), (ang, 0, 0))
        if pipe:
            b.cyl(0.09, 1.6, (w / 2 - 0.6, d / 2 - 0.5, z + h + 0.9), mat=RUST, tint=(1, 1, 1), sides=8, solid=False)
            b.cyl(0.16, 0.08, (w / 2 - 0.6, d / 2 - 0.5, z + h + 1.72), mat=RUST, tint=(1, 1, 1), sides=8, solid=False)
        b.box((0.12, 0.12, 0.16), (dx, -hd - 0.3, z + 2.3), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
    return z


def boardwalk(b, a, c, width=1.4, posts=2.5, rails=True):
    """A timber boardwalk from `a` to `c` (its walking surface) on posts down to the ground, with rope rails."""
    a, c = Vector(a), Vector(c)
    d = c - a
    length = d.length
    yaw = _yaw(d)
    pitch = math.degrees(math.atan2(d.z, math.hypot(d.x, d.y)))
    mid = (a + c) / 2
    b.box((length, width, 0.1), (mid.x, mid.y, mid.z - 0.05), (0, -pitch, yaw), mat=MATTE, tint=WOOD, solid=False,
          vary=0.06)
    side = Vector((-d.y, d.x, 0)).normalized()
    for s in (-1, 1):
        o = side * (width / 2 - 0.05) * s
        b.beam(a + o - Vector((0, 0, 0.15)), c + o - Vector((0, 0, 0.15)), 0.1, 0.18, mat=MATTE,
               tint=srgb(0.36, 0.28, 0.2))
    n = max(1, int(length / posts))
    for i in range(n + 1):
        p = a + d * (i / n)
        for s in (-1, 1):
            q = p + side * (width / 2) * s
            if q.z > 0.2:
                b.box((0.14, 0.14, q.z + 0.3), (q.x, q.y, (q.z + 0.3) / 2 - 0.3), mat=MATTE, tint=srgb(0.34, 0.27, 0.2),
                      solid=False, vary=0.1)
            if rails:
                b.box((0.08, 0.08, 1.0), (q.x, q.y, q.z + 0.5), mat=MATTE, tint=srgb(0.4, 0.32, 0.24), solid=False,
                      vary=0.1)
    if rails:
        for s in (-1, 1):
            o = side * (width / 2) * s + Vector((0, 0, 0.95))
            cable(b, a + o, c + o, sag=0.01, radius=0.02, tint=srgb(0.55, 0.48, 0.36), segments=max(2, n))
    b.collide_box(mid - Vector((0, 0, 0.05)), (length, width, 0.1), (0, -pitch, yaw))


def power_pole(b, at, height=9.0, arm=1.4, tint=srgb(0.66, 0.65, 0.62), rot=(0, 0, 0)):
    """A concrete distribution pole with a cross arm and three insulators; returns the three wire points."""
    x, y, z = at
    b.cyl(0.16, height, (x, y, z + height / 2), mat=CONCRETE, tint=tint, sides=8, radius2=0.11)
    pts = []
    with b.frame(at, rot):
        b.box((arm * 2, 0.12, 0.14), (0, 0, height - 0.6), tint=DARK, solid=False)
        for xx in (-arm + 0.1, 0.0, arm - 0.1):
            zz = height - 0.45 + (0.25 if xx == 0.0 else 0.0)
            b.cyl(0.05, 0.22, (xx, 0, zz), mat=GLOSS, tint=srgb(0.6, 0.35, 0.22), sides=6, solid=False, vary=0)
            pts.append(b.world((xx, 0, zz + 0.12)))
    return pts


def warden_turret(b, at, yaw=0.0, tint=OFFWHITE, scale=1.0):
    """A Warden turret head (Seedward automated security): a domed body on a yaw ring, a gun pod each side, a cyan
    sensor strip across its face (toward -Y of `yaw`)."""
    k = scale
    with b.frame(at, (0, 0, yaw)):
        b.cyl(0.42 * k, 0.3 * k, (0, 0, 0.15 * k), tint=DARK, sides=14, vary=0)
        b.lathe([(0.0, 0.3 * k), (0.62 * k, 0.3 * k), (0.66 * k, 0.55 * k), (0.52 * k, 0.86 * k), (0.26 * k, 1.0 * k),
                 (0.0, 1.03 * k)], (0, 0, 0), tint=tint, sides=16, vary=0.02)
        b.cyl(0.67 * k, 0.08 * k, (0, 0, 0.5 * k), tint=TEAL, sides=16, solid=False, vary=0)
        for sx in (-1, 1):
            b.box((0.22 * k, 0.8 * k, 0.3 * k), (sx * 0.66 * k, -0.15 * k, 0.62 * k), tint=DARK, bevel=0.03 * k)
            b.cyl(0.05 * k, 0.7 * k, (sx * 0.66 * k, -0.85 * k, 0.62 * k), (90, 0, 0), tint=BLACK, sides=8,
                  solid=False, vary=0)
            b.cyl(0.08 * k, 0.12 * k, (sx * 0.66 * k, -1.2 * k, 0.62 * k), (90, 0, 0), tint=BLACK, sides=8,
                  solid=False, vary=0)
        b.box((0.44 * k, 0.06 * k, 0.09 * k), (0, -0.6 * k, 0.74 * k), (-12, 0, 0), mat=LAMP, tint=CHIP["cyan"],
              solid=False, vary=0)
        b.cyl(0.015 * k, 0.6 * k, (0.2 * k, 0.2 * k, 1.25 * k), tint=GREY, sides=4, solid=False, vary=0)


def banner(b, at, rot=(0, 0, 0), w=0.9, h=3.0, tint=TARP_RED, emblem=OFFWHITE, kind="ring"):
    """A cloth banner hanging from a bar (its face toward -Y of `rot`), with the trading clan's emblem."""
    with b.frame(at, rot):
        b.cyl(0.025, w + 0.3, (0, 0, 0), (0, 90, 0), tint=DARK, sides=6, solid=False, vary=0)
        grid = []
        for j in range(4):
            v = j / 3
            row = []
            for i in range(3):
                u = i / 2
                row.append((-w / 2 + w * u, -0.04 * math.sin(math.pi * u) * (1 - v) - 0.02,
                            -0.05 - h * v - (0.25 if j == 3 and i == 1 else 0.0)))
            grid.append(row)
        sheet(b, grid, thick=0.02, tint=tint)
        mark(b, (0, -0.06, -h * 0.42), (0, 0, 0), w * 0.8, kind, emblem)


def flag(b, at, height=8.0, tint=TARP_ORANGE, w=1.8, h=1.1):
    """A flag on a pole, stirring in the wind."""
    x, y, z = at
    b.cyl(0.05, height, (x, y, z + height / 2), tint=GREY, sides=6, solid=False)
    b.cyl(0.08, 0.1, (x, y, z + height + 0.05), tint=GREY, sides=6, solid=False, vary=0)
    grid = []
    for j in range(3):
        v = j / 2
        row = []
        for i in range(5):
            u = i / 4
            row.append((x + 0.06 + w * u, y + 0.18 * math.sin(math.pi * 1.5 * u) * u, z + height - 0.1 - h * v - 0.08 * u))
        grid.append(row)
    sheet(b, grid, thick=0.015, tint=tint)


# ------------------------------------------------------------------------------------------------ preview helpers


def preview_water(outline, z=0.03, name="preview_water"):
    """A flat water surface for look-dev renders only (the game draws its own sea): a polygon of (x, y) points at
    height z. Does nothing while exporting."""
    if kit.EXPORT["enabled"]:
        return None
    import bpy
    me = bpy.data.meshes.new(name)
    me.from_pydata([(x, y, z) for x, y in outline], [], [list(range(len(outline)))])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.012, 0.03, 0.035, 1)
    bsdf.inputs["Roughness"].default_value = 0.06
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.inputs["Scale"].default_value = 0.6
    n.inputs["Detail"].default_value = 6
    nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.08
    nt.links.new(n.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    me.materials.append(mat)
    return o


# ------------------------------------------------------------------------------------------------ kit pieces


def _crate_stack(b):
    """Dressing: a pallet of moulded freight crates and a tarp-wrapped bundle (not loot; loot spawns at markers)."""
    b.box((1.2, 1.0, 0.14), (0, 0, 0.07), mat=PAINT, tint=WOOD, solid=False)
    for x in (-0.5, 0.0, 0.5):
        b.box((0.1, 1.0, 0.1), (x, 0, 0.05), mat=PAINT, tint=srgb(0.45, 0.34, 0.22), solid=False, vary=0)
    cols = (OLIVE, srgb(0.30, 0.32, 0.34), ORANGE)
    for i, (x, y, z, s) in enumerate(((-0.28, -0.2, 0.14, 0.55), (0.3, -0.2, 0.14, 0.55), (0.0, 0.25, 0.14, 0.5),
                                      (-0.1, -0.15, 0.62, 0.5))):
        h = s * 0.85
        b.box((s, s * 0.8, h), (x, y, z + h / 2), mat=PAINT, tint=cols[i % 3], bevel=0.02, solid=False)
        b.box((s + 0.02, 0.06, 0.05), (x, y - s * 0.4, z + h * 0.7), mat=PAINT, tint=DARK, solid=False, vary=0)
        b.box((s * 0.3, 0.02, h * 0.25), (x, y - s * 0.4 - 0.01, z + h * 0.4), mat=PAINT, tint=OFFWHITE,
              solid=False, vary=0)
    b.collide_box(Vector((0, 0, 0.55)), (1.2, 1.0, 1.1), (0, 0, 0))


def _sack_pallet(b):
    """Dressing: a pallet of seed or cement sacks."""
    b.box((1.2, 1.0, 0.14), (0, 0, 0.07), mat=PAINT, tint=WOOD, solid=False)
    k = 0
    for row in range(3):
        for i in range(2):
            for j in range(2 if row < 2 else 1):
                x = -0.28 + 0.56 * i + (0.05 if row % 2 else 0)
                y = -0.24 + 0.48 * j if row < 2 else 0.0
                b.box((0.52, 0.42, 0.16), (x, y, 0.22 + row * 0.16), (0, 0, (k * 37) % 9 - 4), mat=MATTE,
                      tint=SAND if k % 3 else srgb(0.82, 0.80, 0.72), bevel=0.05, solid=False, vary=0.06)
                k += 1
    b.collide_box(Vector((0, 0, 0.4)), (1.2, 1.0, 0.8), (0, 0, 0))


def _rack_row(b):
    """Three equipment cabinets in a row with status lamps and a cable tray over them."""
    for i, x in enumerate((-0.62, 0.0, 0.62)):
        b.box((0.6, 0.9, 2.1), (x, 0, 1.05), mat=PAINT, tint=srgb(0.26, 0.28, 0.3), bevel=0.015, solid=False)
        b.box((0.52, 0.02, 1.8), (x, -0.46, 1.1), mat=PAINT, tint=srgb(0.18, 0.19, 0.2), solid=False, vary=0)
        for k in range(5):
            b.box((0.4, 0.025, 0.05), (x, -0.475, 0.4 + k * 0.32), mat=PAINT, tint=srgb(0.35, 0.37, 0.4),
                  solid=False, vary=0)
            col = (CHIP["cyan"], CHIP["amber"], srgb(0.2, 0.9, 0.3))[(i + k) % 3]
            b.box((0.04, 0.03, 0.03), (x + 0.15, -0.48, 0.42 + k * 0.32), mat=LAMP, tint=col, solid=False, vary=0)
    b.box((2.0, 0.4, 0.08), (0, 0, 2.3), mat=GRATE, tint=GREY, solid=False)
    for x in (-0.9, 0.9):
        b.box((0.04, 0.04, 0.2), (x, 0, 2.2), mat=PAINT, tint=GREY, solid=False)
    b.tube([(-1.0, 0.05, 2.36), (1.0, 0.05, 2.36)], 0.05, tint=BLACK, sides=6)
    b.collide_box(Vector((0, 0, 1.05)), (1.86, 0.9, 2.1), (0, 0, 0))


def _console(b):
    """A control desk: a sloped panel of switches and two screens."""
    b.box((1.8, 0.8, 0.75), (0, 0, 0.375), mat=PAINT, tint=srgb(0.80, 0.80, 0.78), bevel=0.02, solid=False)
    b.box((1.8, 0.5, 0.06), (0, -0.05, 0.92), (-25, 0, 0), mat=PAINT, tint=srgb(0.3, 0.32, 0.34), solid=False)
    for k in range(10):
        col = (CHIP["cyan"], CHIP["amber"], srgb(0.2, 0.9, 0.3), RED)[k % 4]
        b.box((0.05, 0.05, 0.03), (-0.75 + k * 0.16, -0.12, 0.98), (-25, 0, 0), mat=LAMP if k % 3 == 0 else PAINT,
              tint=col, solid=False, vary=0)
    for x in (-0.45, 0.45):
        b.box((0.7, 0.06, 0.45), (x, 0.25, 1.3), (8, 0, 0), mat=PAINT, tint=DARK, solid=False)
        b.box((0.62, 0.02, 0.38), (x, 0.21, 1.3), (8, 0, 0), mat=GLASS, solid=False, vary=0)
        b.box((0.06, 0.06, 0.4), (x, 0.3, 1.0), mat=PAINT, tint=DARK, solid=False)
    b.collide_box(Vector((0, 0, 0.5)), (1.8, 0.8, 1.0), (0, 0, 0))


def _utility_truck(b):
    """A six-wheeled utility truck: cab-over, a flatbed with a toolbox and a folded loader crane (tinted per
    instance)."""
    body = (1.0, 1.0, 1.0)
    b.box((7.2, 1.0, 0.35), (0, 0, 0.75), mat=PAINT, tint=DARK)
    for x in (-2.4, -1.2, 2.5):
        for y in (-1.05, 1.05):
            b.cyl(0.52, 0.4, (x, y, 0.52), (90, 0, 0), mat=MATTE, tint=srgb(0.1, 0.1, 0.1), sides=14, solid=False)
            b.cyl(0.28, 0.42, (x, y, 0.52), (90, 0, 0), mat=PAINT, tint=GREY, sides=10, solid=False, vary=0)
    b.box((2.0, 2.4, 2.1), (2.6, 0, 2.0), mat=PAINT, tint=body, bevel=0.08)
    b.box((0.06, 2.1, 0.9), (3.62, 0, 2.45), (0, -12, 0), mat=GLASS, solid=False, vary=0)
    for y in (-1.21, 1.21):
        b.box((1.0, 0.05, 0.7), (2.9, y, 2.5), mat=GLASS, solid=False, vary=0)
    b.box((0.25, 2.5, 0.4), (3.7, 0, 1.15), mat=PAINT, tint=DARK, solid=False)
    for y in (-0.8, 0.8):
        b.box((0.06, 0.3, 0.18), (3.84, y, 1.35), mat=LAMP, tint=srgb(1.0, 0.92, 0.75), solid=False, vary=0)
    b.box((1.8, 2.3, 0.12), (2.6, 0, 3.12), mat=PAINT, tint=ORANGE, solid=False, vary=0)
    b.box((4.8, 2.4, 0.18), (-1.3, 0, 1.05), mat=PAINT, tint=srgb(0.4, 0.42, 0.42))
    for y in (-1.15, 1.15):
        b.box((4.8, 0.08, 0.5), (-1.3, y, 1.4), mat=PAINT, tint=body, solid=False)
    b.box((1.2, 2.3, 0.9), (0.95, 0, 1.6), mat=PAINT, tint=srgb(0.55, 0.57, 0.58), bevel=0.03)
    b.cyl(0.35, 0.8, (0.2, 0.7, 1.55), mat=PAINT, tint=YELLOW, sides=12, solid=False)
    b.beam((0.2, 0.7, 1.9), (-3.2, 0.7, 2.3), 0.35, 0.4, tint=YELLOW)
    b.beam((-3.2, 0.7, 2.3), (-1.0, 0.7, 2.0), 0.28, 0.3, tint=YELLOW)
    b.collide_box(Vector((0, 0, 1.4)), (7.4, 2.5, 2.8), (0, 0, 0))


def _fire_barrel(b):
    """A rusted drum burning scrap wood: the Reclaimers' brazier, on a ring of ash."""
    b.cyl(0.65, 0.02, (0, 0, 0.01), mat=MATTE, tint=srgb(0.12, 0.11, 0.1), sides=12, solid=False, vary=0)
    b.cyl(0.3, 0.9, (0, 0, 0.45), mat=RUST, tint=(1, 1, 1), sides=14, cap=False, solid=False)
    b.cyl(0.29, 0.05, (0, 0, 0.3), mat=RUST, tint=(1, 1, 1), sides=14, solid=False)
    for k in range(6):
        a = 2 * math.pi * k / 6
        b.box((0.08, 0.02, 0.1), (math.cos(a) * 0.3, math.sin(a) * 0.3, 0.2), (0, 0, math.degrees(a) + 90),
              mat=PAINT, tint=BLACK, solid=False, vary=0)
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        b.beam((math.cos(a) * 0.15, math.sin(a) * 0.15, 0.6), (math.cos(a) * 0.05, math.sin(a) * 0.05, 1.05), 0.07,
               0.07, mat=MATTE, tint=srgb(0.2, 0.16, 0.12))
    b.cyl(0.24, 0.35, (0, 0, 0.98), mat=LAMP, tint=srgb(1.0, 0.45, 0.12), sides=8, radius2=0.04, solid=False)
    b.marker("light", (0, 0, 1.2), colour=[1.0, 0.45, 0.12], range=7.0, flicker=True)
    b.collide_box(Vector((0, 0, 0.45)), (0.6, 0.6, 0.9), (0, 0, 0))


def _stall(awning):
    def make(b):
        """A market stall: a welded counter with a plank top, shelves of goods behind, a tarp awning on four poles,
        a sign board, a lamp, side sheets."""
        rng = b.rng
        b.box((3.6, 0.8, 1.0), (0, -0.7, 0.5), mat=PAINT, tint=srgb(0.45, 0.47, 0.46))
        b.box((3.7, 0.95, 0.06), (0, -0.72, 1.03), mat=MATTE, tint=WOOD, solid=False)
        b.box((3.5, 0.04, 0.8), (0, -1.11, 0.5), mat=CORRUGATED, tint=srgb(0.66, 0.6, 0.52), solid=False)
        for x in (-1.7, 1.7):
            b.box((0.06, 0.4, 2.2), (x, 0.7, 1.1), mat=PAINT, tint=DARK, solid=False, vary=0)
        for z in (0.5, 1.15, 1.8):
            b.box((3.4, 0.45, 0.04), (0, 0.7, z), mat=MATTE, tint=WOOD, solid=False)
            x = -1.55
            while x < 1.5:
                kind = rng.random()
                if kind < 0.4:
                    s_ = rng.uniform(0.2, 0.4)
                    b.box((s_, 0.32, s_ * 0.8), (x + s_ / 2, 0.7, z + 0.02 + s_ * 0.4), mat=PAINT,
                          tint=(OLIVE, srgb(0.7, 0.62, 0.45), GREY, ORANGE)[rng.randrange(4)], solid=False, vary=0.1)
                    x += s_ + 0.05
                elif kind < 0.75:
                    for i in range(3):
                        b.cyl(0.05, 0.22, (x + 0.06 + i * 0.11, 0.7, z + 0.13), mat=PAINT,
                              tint=(RED, BLUE, YELLOW, OFFWHITE)[rng.randrange(4)], sides=8, solid=False, vary=0.1)
                    x += 0.36
                else:
                    b.cyl(0.04, 0.3, (x + 0.05, 0.7, z + 0.17), mat=GLASS, sides=6, solid=False, vary=0)
                    x += 0.15
        for x in (-1.85, 1.85):
            b.cyl(0.04, 2.5, (x, -1.25, 1.25), tint=GREY, sides=6, solid=False, vary=0)
            b.cyl(0.04, 2.95, (x, 1.0, 1.475), tint=GREY, sides=6, solid=False, vary=0)
            b.box((0.04, 2.0, 1.4), (x, 0.0, 0.75), mat=CORRUGATED, tint=srgb(0.58, 0.58, 0.56), solid=False)
        grid = []
        for j in range(3):
            v = j / 2
            row = []
            for i in range(5):
                u = i / 4
                row.append((-2.05 + 4.1 * u, -1.6 + 2.8 * v, 2.5 + 0.45 * v - 0.12 * math.sin(math.pi * u) - 0.1 *
                            math.sin(math.pi * v)))
            grid.append(row)
        sheet(b, grid, thick=0.03, tint=awning)
        for i in range(8):
            b.box((0.5, 0.02, 0.22), (-1.85 + i * 0.53, -1.62, 2.38), mat=MATTE, tint=awning if i % 2 else OFFWHITE,
                  solid=False, vary=0.04)
        b.box((1.6, 0.05, 0.45), (0, -1.3, 2.05), mat=PAINT, tint=srgb(0.86, 0.82, 0.7), solid=False)
        for i in range(4):
            b.box((0.18, 0.02, 0.24), (-0.5 + i * 0.33, -1.34, 2.06), mat=PAINT, tint=DARK, solid=False, vary=0)
        b.box((0.16, 0.16, 0.2), (0.0, -0.6, 2.35), mat=LAMP, tint=WARM_LAMP, solid=False, vary=0)
        b.marker("light", (0.0, -0.6, 2.2), colour=[round(v, 3) for v in WARM_LAMP], range=8.0)
        b.collide_box(Vector((0, -0.7, 0.5)), (3.6, 0.8, 1.0), (0, 0, 0))
        b.collide_box(Vector((0, 0.7, 1.1)), (3.5, 0.5, 2.2), (0, 0, 0))
    return make


def _turret_mast(b):
    """A Warden peacekeeper: a turret head on an 8 m mast with a cable box and a cyan beacon."""
    b.box((1.4, 1.4, 0.8), (0, 0, 0.4), mat=CONCRETE, tint=CONCRETE_T, bevel=0.03)
    b.cyl(0.2, 7.6, (0, 0, 4.4), tint=srgb(0.72, 0.74, 0.74), sides=12, radius2=0.15)
    b.box((0.5, 0.3, 0.7), (0, -0.32, 2.2), tint=OFFWHITE, bevel=0.02)
    b.box((0.3, 0.02, 0.1), (0, -0.48, 2.4), mat=LAMP, tint=CHIP["cyan"], solid=False, vary=0)
    b.tube([(0.0, -0.18, 1.85), (0.0, -0.18, 0.8)], 0.03, tint=BLACK, sides=5)
    b.cyl(0.7, 0.18, (0, 0, 8.1), tint=DARK, sides=12)
    warden_turret(b, (0, 0, 8.2), 0.0)
    b.cyl(0.09, 0.2, (0, 0.3, 9.4), mat=LAMP, tint=CHIP["cyan"], sides=8, solid=False, vary=0)
    b.marker("light", (0, 0.3, 9.5), colour=[round(v, 3) for v in CHIP["cyan"]], range=10.0, blink=2.0)


def _bunk(b):
    """A steel double bunk with two mattresses and a ladder; a locker beside it."""
    for x in (-0.95, 0.95):
        for y in (-0.42, 0.42):
            b.box((0.05, 0.05, 1.75), (x, y, 0.875), tint=BULWARK, solid=False, vary=0)
    for z in (0.35, 1.3):
        b.box((1.95, 0.88, 0.06), (0, 0, z), tint=BULWARK, solid=False, vary=0)
        b.box((1.85, 0.8, 0.14), (0, 0, z + 0.1), mat=MATTE, tint=KHAKI, bevel=0.03, solid=False, vary=0.08)
        b.box((0.45, 0.6, 0.1), (-0.65, 0, z + 0.22), mat=MATTE, tint=srgb(0.8, 0.78, 0.7), solid=False, vary=0.05)
    for z in (0.7, 1.0):
        b.box((0.04, 0.4, 0.04), (0.98, 0.0, z), tint=BULWARK, solid=False, vary=0)
    b.box((0.55, 0.5, 1.8), (1.35, 0.1, 0.9), tint=srgb(0.42, 0.46, 0.4), bevel=0.01)
    b.box((0.02, 0.15, 0.04), (1.35, -0.16, 1.2), tint=DARK, solid=False, vary=0)
    b.collide_box(Vector((0.2, 0, 0.9)), (2.4, 0.9, 1.8), (0, 0, 0))


def _gabion_row(b):
    """Five wire-mesh gabions filled with earth and stone: a Bulwark revetment section 5.5 m long."""
    for i in range(5):
        x = -2.2 + i * 1.1
        b.box((1.04, 1.04, 1.36), (x, 0, 0.68), mat=MATTE, tint=srgb(0.40, 0.36, 0.28), vary=0.08)
        b.box((1.1, 1.1, 1.4), (x, 0, 0.7), mat=CHAINLINK, tint=srgb(0.7, 0.7, 0.66), solid=False, vary=0)
        b.box((1.0, 1.0, 0.1), (x, 0, 1.42), mat=MATTE, tint=srgb(0.34, 0.38, 0.22), solid=False, vary=0.1)
    b.collide_box(Vector((0, 0, 0.7)), (5.5, 1.1, 1.4), (0, 0, 0))


def _sandbag_run(b):
    """Three courses of sandbags, 3 m long (instanced, so the bags can be shaped)."""
    k = 0
    for row in range(3):
        off = 0.3 if row % 2 else 0.0
        for i in range(5):
            x = -1.5 + 0.3 + i * 0.6 + off
            if x > 1.4:
                continue
            b.box((0.6, 0.38, 0.17), (x, ((k * 7) % 5 - 2) * 0.01, 0.09 + row * 0.16), (0, 0, (k * 13) % 9 - 4),
                  mat=MATTE, tint=srgb(0.55, 0.50, 0.38), bevel=0.06, solid=False, vary=0.08, wear=0.6)
            k += 1
    b.box((3.0, 0.3, 0.06), (0, 0, 0.03), mat=MATTE, tint=srgb(0.4, 0.36, 0.28), solid=False, vary=0)
    b.collide_box(Vector((0, 0, 0.25)), (3.0, 0.4, 0.5), (0, 0, 0))


def _sandbag_nest(b):
    """A U of sandbags four courses high round a gun position (open toward +Y)."""
    k = 0
    for side in range(3):
        for row in range(4):
            off = 0.3 if row % 2 else 0.0
            for i in range(5):
                t = -1.5 + 0.3 + i * 0.6 + off
                if t > 1.4:
                    continue
                if side == 0:
                    pos, yaw = (t, -1.5), 0
                elif side == 1:
                    pos, yaw = (-1.5, t), 90
                else:
                    pos, yaw = (1.5, t), 90
                b.box((0.6, 0.38, 0.17), (pos[0], pos[1], 0.09 + row * 0.16), (0, 0, yaw + (k * 13) % 9 - 4),
                      mat=MATTE, tint=srgb(0.55, 0.50, 0.38), bevel=0.06, solid=False, vary=0.08, wear=0.6)
                k += 1
    b.box((0.12, 0.6, 0.1), (0, -1.2, 0.75), tint=DARK, solid=False)
    b.cyl(0.03, 1.0, (0, -1.7, 0.75), (90, 0, 0), tint=BLACK, sides=6, solid=False)
    b.box((0.08, 0.08, 0.5), (0, -1.1, 0.5), tint=DARK, solid=False)
    b.collide_box(Vector((0, -1.5, 0.33)), (3.2, 0.4, 0.66), (0, 0, 0))
    b.collide_box(Vector((-1.5, 0, 0.33)), (0.4, 3.2, 0.66), (0, 0, 0))
    b.collide_box(Vector((1.5, 0, 0.33)), (0.4, 3.2, 0.66), (0, 0, 0))


def _apc(b):
    """An eight-wheeled Bulwark armoured carrier, angular hull, a small turret (tinted per instance)."""
    body = (1.0, 1.0, 1.0)
    prof = [(-3.6, 0.7), (3.0, 0.7), (3.9, 1.4), (3.4, 2.0), (-3.2, 2.15), (-3.7, 1.6)]
    b.extrude(prof, 2.5, (0, 0, 0), (0, 0, 0), mat=PAINT, tint=body)
    b.box((7.0, 0.3, 0.5), (0, 1.3, 1.25), mat=PAINT, tint=body, solid=False)
    b.box((7.0, 0.3, 0.5), (0, -1.3, 1.25), mat=PAINT, tint=body, solid=False)
    for x in (-2.6, -1.2, 1.0, 2.4):
        for y in (-1.25, 1.25):
            b.cyl(0.58, 0.45, (x, y, 0.6), (90, 0, 0), mat=MATTE, tint=srgb(0.1, 0.1, 0.1), sides=14, solid=False)
            b.cyl(0.3, 0.47, (x, y, 0.6), (90, 0, 0), mat=PAINT, tint=body, sides=10, solid=False, vary=0)
    b.cyl(0.75, 0.55, (-0.3, 0, 2.42), mat=PAINT, tint=body, sides=12)
    b.box((1.2, 1.0, 0.4), (0.2, 0, 2.5), mat=PAINT, tint=body)
    b.cyl(0.07, 2.0, (1.6, 0, 2.55), (0, 90, 0), tint=DARK, sides=8, solid=False)
    for y in (-0.7, 0.7):
        b.box((0.3, 0.4, 0.15), (3.45, y, 1.75), (0, 30, 0), mat=GLASS, solid=False, vary=0)
    b.box((0.8, 0.05, 0.7), (-3.6, 0.0, 1.35), tint=DARK, solid=False)
    b.box((0.6, 2.52, 0.08), (0.6, 0, 2.0), mat=PAINT, tint=YELLOW, solid=False, vary=0, wear=0.6)
    b.collide_box(Vector((0, 0, 1.4)), (7.8, 2.6, 2.6), (0, 0, 0))


def _weapon_rack(b):
    """A steel rack of rifles and a shelf of ammunition tins."""
    b.box((1.8, 0.45, 0.08), (0, 0, 0.3), tint=DARK, solid=False)
    b.box((1.8, 0.1, 1.6), (0, 0.2, 0.9), tint=srgb(0.36, 0.4, 0.34), solid=False)
    b.box((1.8, 0.35, 0.05), (0, 0.02, 1.75), tint=DARK, solid=False)
    for i in range(7):
        x = -0.75 + i * 0.25
        b.box((0.05, 0.05, 1.0), (x, 0.0, 0.85), (0, 6, 0), tint=BLACK, solid=False, vary=0)
        b.box((0.07, 0.14, 0.32), (x + 0.03, -0.02, 0.5), tint=srgb(0.3, 0.26, 0.2), solid=False, vary=0)
    for i in range(4):
        b.box((0.3, 0.2, 0.18), (-0.6 + i * 0.4, 0.0, 1.87), tint=OLIVE, solid=False, vary=0.05)
    b.collide_box(Vector((0, 0.05, 1.0)), (1.8, 0.5, 2.0), (0, 0, 0))


kit.PIECES.update({
    "bunk": _bunk,
    "gabion-row": _gabion_row,
    "sandbag-run": _sandbag_run,
    "sandbag-nest": _sandbag_nest,
    "apc": _apc,
    "weapon-rack": _weapon_rack,
    "vendor-stall-blue": _stall(TARP_BLUE),
    "vendor-stall-orange": _stall(TARP_ORANGE),
    "vendor-stall-green": _stall(TARP_GREEN),
    "turret-mast": _turret_mast,
    "fire-barrel": _fire_barrel,
    "crate-stack": _crate_stack,
    "sack-pallet": _sack_pallet,
    "rack-row": _rack_row,
    "console": _console,
    "utility-truck": _utility_truck,
})
