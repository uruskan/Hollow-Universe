"""Fits each building piece's drawn mesh to its logical slot (GROUND.md §6.3, "Fit"; decisions J1-J9).

Used by building_kit.py inside Blender: numpy on the mesh's vertices, nothing else of Blender's. The slot is where the
rules put the piece (game/data/ground/building.json's module sizes: the footprint, a foundation's 1 m and a floor's
20 cm, a wall's height and ends, its opening); its collision, sockets and stability are made from the same numbers and
never move. Only the drawn mesh does.

One rule for every piece, grade and level of detail (art/import/building_fit.json):
  1. Measure each face of the slot on the mesh: in 1 cm bins along the face, how far past the logical face the mesh
     reaches (positive beyond it, negative short of it); the face's reach is a percentile of its bins.
  2. Map the mesh, axis by axis, through knots (measured face -> logical face + its target, + the grade's overlap on
     faces that meet a neighbour): piecewise linear in a wall's x and y (ends, the opening's sides, sill and head, base
     and top), linear in a slab's x, z and y (edges, top); a triangle's footprint by one scale and a shift.
  3. Walls are drawn a few millimetres lower, and lower still toward their ends, so whatever stands on a wall is the
     drawn face where they meet and walls meeting at a corner or a T don't share the plane of their tops; their end faces
     lean a little about their middle, so a slab's side never lies in a wall end's plane, while walls in line (either
     way round) still meet exactly.
"""
import json
import math
import pathlib

import numpy as np

HERE = pathlib.Path(__file__).resolve().parent
RULE = json.loads((HERE / "building_fit.json").read_text(encoding="utf-8"))
ROOT3 = math.sqrt(3)


# ---------------------------------------------------------------- the mesh, in the game's axes

def read(o):
    """Vertex positions in the game's axes (Blender x, z, -y) and the mesh's triangles."""
    me = o.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    me.calc_loop_triangles()
    tri = np.empty(len(me.loop_triangles) * 3, np.int64)
    me.loop_triangles.foreach_get("vertices", tri)
    return np.c_[co[:, 0], co[:, 2], -co[:, 1]], tri.reshape(-1, 3)


def write(o, points):
    co = np.c_[points[:, 0], -points[:, 2], points[:, 1]]
    o.data.vertices.foreach_set("co", co.ravel())
    o.data.update()


def samples(points, tris, step):
    """Points covering every triangle no further apart than `step`: a lattice along the triangle's shortest edge and the
    edge beside it, so a long, thin triangle (a branch's side) costs points for its area, not its length squared."""
    p = points.astype(np.float32)[tris]
    lengths = np.stack([np.linalg.norm(p[:, (k + 1) % 3] - p[:, k], axis=1) for k in range(3)], axis=1)
    # Corner k sits between edges k (k -> k+1) and k-1; start at the corner the shortest edge leaves from.
    k = np.argmin(lengths, axis=1)
    rows = np.arange(len(p))
    a = p[rows, k]
    c = p[rows, (k + 1) % 3]
    b = p[rows, (k + 2) % 3]
    na = np.maximum(1, np.ceil(np.linalg.norm(b - a, axis=1) / step)).astype(int)
    nc = np.maximum(1, np.ceil(np.linalg.norm(c - a, axis=1) / step)).astype(int)
    out = [points.astype(np.float32)]
    pairs = np.stack([na, nc], axis=1)
    for ia, ic in np.unique(pairs, axis=0):
        sel = (na == ia) & (nc == ic)
        i, j = np.meshgrid(np.arange(ia + 1), np.arange(ic + 1), indexing="ij")
        u, v = (i / ia).ravel().astype(np.float32), (j / ic).ravel().astype(np.float32)
        keep = u + v <= 1 + 1e-9
        u, v = u[keep][None, :, None], v[keep][None, :, None]
        aa, bb, cc = a[sel][:, None, :], b[sel][:, None, :], c[sel][:, None, :]
        out.append((aa + (bb - aa) * u + (cc - aa) * v).reshape(-1, 3))
    return np.concatenate(out)


# ---------------------------------------------------------------- the slot

def footprint(shape, width):
    """The footprint's corners (x, z), counter-clockwise seen from above (PieceGeometry.Footprint)."""
    half = width / 2
    if shape == "triangle":
        alt = width * ROOT3 / 2
        return np.array([(-half, alt / 3), (half, alt / 3), (0, -2 * alt / 3)])
    return np.array([(-half, half), (half, half), (half, -half), (-half, -half)])


def wall_regions(piece, module):
    """The solid parts of a wall as (x0, x1, y0, y1) (PieceShapes.WallRegions)."""
    half, h = module["width"] / 2, piece["height"]
    opening = piece.get("opening")
    if opening == "window":
        ow, sill = module["windowOpening"][0] / 2, module["windowSill"]
        top = sill + module["windowOpening"][1]
        return [(-half, -ow, 0, h), (ow, half, 0, h), (-ow, ow, 0, sill), (-ow, ow, top, h)]
    if opening in ("door", "double"):
        size = module["doorOpening" if opening == "door" else "doubleOpening"]
        ow, top = size[0] / 2, size[1]
        return [(-half, -ow, 0, h), (ow, half, 0, h), (-ow, ow, top, h)]
    return [(-half, half, 0, h)]


class Face:
    """A face of the slot: along `axis` ("x", "y", "z") at `coord`, its outside toward `sign`; `kind` names its rule."""

    def __init__(self, kind, axis, coord, sign, reach):
        self.kind, self.axis, self.coord, self.sign = kind, axis, coord, sign
        self.reach = reach  # (points) -> per-bin reach, metres

    def rule(self):
        return RULE["faces"][self.kind]


AXIS = {"x": 0, "y": 1, "z": 2}


def binned(keys, values, lo, hi, reduce, bin=None):
    """Per bin of `keys` over [lo, hi), the max (or min) of `values`; bins without a point are left out."""
    bin = bin or RULE["measure"]["bin"]
    count = max(1, int(round((hi - lo) / bin)))
    idx = np.floor((keys - lo) / bin).astype(int)
    ok = (idx >= 0) & (idx < count)
    out = np.full(count, -np.inf if reduce == "max" else np.inf)
    (np.maximum if reduce == "max" else np.minimum).at(out, idx[ok], values[ok])
    return out[np.isfinite(out)]


def slab_faces(piece, module):
    """A foundation's or floor's: its top (the walking surface) and each footprint edge (all heights, corners left out).
    Its base is nobody's joint (underground, or a ceiling free of anything)."""
    depth = module["foundationDepth"] if piece["family"] == "foundation" else module["floorThickness"]
    poly = footprint(piece["shape"], module["width"])
    margin = RULE["measure"]["cornerMargin"]
    lo = poly.min(axis=0)

    def top(p):
        inside = np.ones(len(p), bool)
        for a, n, _, _ in edges(poly):
            inside &= (p[:, [0, 2]] - a) @ n <= -0.005
        sel = (p[:, 1] > -0.4) & inside
        cells = np.floor((p[sel][:, [0, 2]] - lo) / RULE["measure"]["bin"]).astype(int)
        return per_key(cells[:, 0] * 100000 + cells[:, 1], p[sel][:, 1], "max")

    faces = [Face("slab top", "y", 0.0, 1, top)]
    for a, n, u, length in edges(poly):
        def reach(p, a=a, n=n, u=u, length=length):
            xz = p[:, [0, 2]] - a
            out = xz @ n
            sel = (out > -0.5) & (p[:, 1] > -depth - 0.05) & (p[:, 1] < 0.05)
            return binned(xz[sel] @ u, out[sel], margin, length - margin, "max")
        faces.append(Face("slab edge", n, None, 1, reach))
    return faces


def edges(poly):
    """(start, outward normal, direction, length) for each edge of a footprint."""
    out = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        u = (b - a) / np.linalg.norm(b - a)
        n = np.array([u[1], -u[0]])
        if np.dot(n, (a + b) / 2 - poly.mean(axis=0)) < 0:
            n = -n
        out.append((a, n, u, np.linalg.norm(b - a)))
    return out


def per_key(key, values, reduce):
    order = np.argsort(key, kind="stable")
    key, values = key[order], values[order]
    starts = np.r_[0, np.flatnonzero(np.diff(key)) + 1]
    return (np.maximum if reduce == "max" else np.minimum).reduceat(values, starts)


def wall_faces(piece, module):
    """A wall's: its base and top along its length, its two ends up its height, its opening's sides, head and sill."""
    half, h, t = module["width"] / 2, piece["height"], module["wallThickness"] / 2
    regions = wall_regions(piece, module)
    near = lambda p: np.abs(p[:, 2]) < t + 0.15

    def along_regions(p, values, regions_here, reduce):
        m = RULE["measure"]["regionMargin"]
        parts = []
        for x0, x1, _, _ in regions_here:
            sel = near(p) & (p[:, 0] >= x0) & (p[:, 0] < x1)
            parts.append(binned(p[sel][:, 0], values[sel], x0 + m, x1 - m, reduce))
        return np.concatenate(parts) if parts else np.empty(0)

    standing = [r for r in regions if r[2] == 0]
    topped = [r for r in regions if r[3] == h]

    def base(p):
        low = p[:, 1] < 0.4
        return -along_regions(p[low], p[low][:, 1], standing, "min")

    def top(p):
        high = p[:, 1] > h - 0.4
        return along_regions(p[high], p[high][:, 1], topped, "max") - h

    def end(sign):
        def reach(p):
            sel = near(p) & (sign * p[:, 0] > half - 0.4)
            return binned(p[sel][:, 1], sign * p[sel][:, 0], 0.05, h - 0.05, "max") - half
        return reach

    faces = [Face("wall bottom", "y", 0.0, -1, base), Face("wall top", "y", h, 1, top),
             Face("wall end", "x", -half, -1, end(-1)), Face("wall end", "x", half, 1, end(1))]
    opening = piece.get("opening")
    if opening:
        x0, x1, y0, y1 = opening_box(piece, module)
        ow = x1
        for sign in (-1, 1):
            # The jamb on this side: its face toward the opening, up the opening's height.
            def side(p, sign=sign):
                sel = near(p) & (sign * p[:, 0] > 0) & (sign * p[:, 0] < ow + 0.4) & (p[:, 1] > y0) & (p[:, 1] < y1)
                return ow - binned(p[sel][:, 1], sign * p[sel][:, 0], y0 + 0.05, y1 - 0.05, "min")
            faces.append(Face("opening side", "x", sign * ow, -sign, side))

        def head(p):
            sel = near(p) & (np.abs(p[:, 0]) < ow - 0.05) & (p[:, 1] > (y0 + y1) / 2)
            return y1 - binned(p[sel][:, 0], p[sel][:, 1], -ow + 0.05, ow - 0.05, "min")
        faces.append(Face("opening head", "y", y1, -1, head))
        if y0 > 0:
            def sill(p):
                sel = near(p) & (np.abs(p[:, 0]) < ow - 0.05) & (p[:, 1] < (y0 + y1) / 2)
                return binned(p[sel][:, 0], p[sel][:, 1], -ow + 0.05, ow - 0.05, "max") - y0
            faces.append(Face("opening sill", "y", y0, 1, sill))
    return faces


def opening_box(piece, module):
    """The opening's (x0, x1, y0, y1): -half its width to +half, its sill (0 for doors) to its head."""
    opening = piece["opening"]
    if opening == "window":
        ow, sill = module["windowOpening"][0] / 2, module["windowSill"]
        return -ow, ow, sill, sill + module["windowOpening"][1]
    size = module["doorOpening" if opening == "door" else "doubleOpening"]
    return -size[0] / 2, size[0] / 2, 0.0, size[1]


def stairs_faces(piece, module):
    """A spiral's column: its foot on the slab, its top where the next module's column carries on."""
    if piece.get("stairs") != "spiral":
        return []
    h = piece["height"]
    axis = np.array([0.0, module["width"] * ROOT3 / 6]) if piece["shape"] == "triangle" else np.zeros(2)
    column = lambda p: np.linalg.norm(p[:, [0, 2]] - axis, axis=1) < 0.16
    return [Face("column bottom", "y", 0.0, -1, lambda p: np.array([-p[column(p)][:, 1].min()])),
            Face("column top", "y", h, 1, lambda p: np.array([p[column(p) & (p[:, 1] < h + 0.3)][:, 1].max() - h]))]


SLOTS = {"foundation": slab_faces, "floor": slab_faces, "wall": wall_faces, "stairs": stairs_faces, "roof": lambda piece, module: []}


# ---------------------------------------------------------------- the fit

def target(face, grade):
    rule = face.rule()
    return rule["target"] + (RULE["grades"][grade]["overlap"] if rule.get("overlap") else 0.0)


def through(knots, values):
    """A piecewise linear map through (from, to) knots, carried on past the ends with the end segments' slopes."""
    if not knots:
        return values
    knots = sorted(knots)
    v = np.array([k[0] for k in knots])
    w = np.array([k[1] for k in knots])
    if len(knots) == 1:
        return values + (w[0] - v[0])
    out = np.interp(values, v, w)
    lo, hi = values < v[0], values > v[-1]
    out[lo] = w[0] + (values[lo] - v[0]) * (w[1] - w[0]) / (v[1] - v[0])
    out[hi] = w[-1] + (values[hi] - v[-1]) * (w[-1] - w[-2]) / (v[-1] - v[-2])
    return out


def fit(o, piece, module, grade):
    """Fits a drawn mesh (a Blender object, one level of detail) to its piece's slot. Returns the report's lines."""
    for x in cuts(piece, module):
        cut(o, x)
    points, tris = read(o)
    fitted, lines = fit_points(points, tris, piece, module, grade)
    write(o, fitted)
    return lines


def fit_points(points, tris, piece, module, grade):
    """The fit on a mesh's vertices in the game's axes: the fitted vertices and the report's lines."""
    pts = samples(points, tris, RULE["measure"]["step"])
    faces = SLOTS[piece["family"]](piece, module)
    knots = {"x": [], "y": [], "z": []}
    footprint_edges = []
    lines = []
    limit = RULE["limit"]
    for face in faces:
        bins = face.reach(pts)
        if len(bins) == 0:
            lines.append(f"{face.kind}: nothing to measure")
            continue
        reach = float(np.percentile(bins, face.rule()["percentile"]))
        goal = target(face, grade)
        move = goal - reach
        flag = ""
        if abs(move) > limit:
            flag = f"  (over the {limit * 100:.0f} cm limit: left alone)"
        lines.append(f"{face.kind:13} {face_name(face):>9}  reach {reach * 1000:+7.1f} mm  -> {goal * 1000:+5.1f} mm  move {move * 1000:+6.1f} mm{flag}")
        if flag:
            continue
        if face.coord is None:
            footprint_edges.append((face.axis, reach, goal))
        else:
            knots[face.axis].append((face.coord + face.sign * reach, face.coord + face.sign * goal))
    del pts
    fitted = points.copy()
    if footprint_edges and all(abs(n[0]) < 1e-9 or abs(n[1]) < 1e-9 for n, _, _ in footprint_edges):
        # Square: each pair of opposite edges sets its axis.
        half = module["width"] / 2
        for n, reach, goal in footprint_edges:
            axis = "x" if abs(n[0]) > 0.5 else "z"
            sign = n[0] if axis == "x" else n[1]
            knots[axis].append((sign * (half + reach), sign * (half + goal)))
    elif footprint_edges:
        # Triangle: one scale about the centroid and one shift, edge by edge: s (r + reach) + n.t = r + goal.
        r = module["width"] * ROOT3 / 6
        a = np.array([[r + reach, n[0], n[1]] for n, reach, _ in footprint_edges])
        b = np.array([r + goal for _, _, goal in footprint_edges])
        s, tx, tz = np.linalg.lstsq(a, b, rcond=None)[0]
        fitted[:, 0] = s * fitted[:, 0] + tx
        fitted[:, 2] = s * fitted[:, 2] + tz
        lines.append(f"footprint scale {s:.4f}, shift {tx * 1000:+.1f}, {tz * 1000:+.1f} mm")
    for axis, ks in knots.items():
        if ks:
            if not monotonic(ks):
                lines.append(f"{axis} knots out of order, left alone: {ks}")
                continue
            k = AXIS[axis]
            fitted[:, k] = through(ks, fitted[:, k])
    if piece["family"] == "wall":
        walls = RULE["walls"]
        half = module["width"] / 2
        ramp = np.clip((np.abs(fitted[:, 0]) - (half - walls["endLength"])) / walls["endLength"], 0, 1)
        fitted[:, 1] -= walls["drop"] + walls["endSag"] * ramp
        fitted[:, 0] += walls["endShear"] * fitted[:, 2] * ramp
    elif piece["family"] == "foundation":
        # Preserve the walking cap; the footing must reach the same depth as placement/collision.
        old_depth = -fitted[:, 1].min()
        cap = min(0.2, old_depth * 0.5)
        below = fitted[:, 1] < -cap
        fitted[below, 1] = -cap + (fitted[below, 1] + cap) * (module["foundationDepth"] - cap) / (old_depth - cap)
        lines.append(f"foundation skirt: {old_depth:.3f} -> {module['foundationDepth']:.3f} m; top {cap:.3f} m preserved")
    return fitted, lines


def cuts(piece, module):
    """Where a wall's level faces are cut across (x) so its ends can sink: a rail's or a board's top spanning the whole
    wall has vertices only at its ends, and would sink whole."""
    if piece["family"] != "wall":
        return []
    x = module["width"] / 2 - RULE["walls"]["endLength"]
    return [-x, x]


def cut(o, x):
    """Cuts a Blender mesh's level faces (tops and undersides) across the plane game x (Blender x) = `x`; a face standing
    upright only gains the cut's vertex on its edge, and stays in its plane as the ends sink."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.normal_update()
    level = [f for f in bm.faces if abs(f.normal.z) > 0.9]
    edges = {e for f in level for e in f.edges}
    verts = {v for f in level for v in f.verts}
    bmesh.ops.bisect_plane(bm, geom=list(verts) + list(edges) + level, dist=1e-6, plane_co=(x, 0, 0), plane_no=(1, 0, 0))
    bm.to_mesh(o.data)
    bm.free()


def monotonic(knots):
    knots = sorted(knots)
    return all(b[0] > a[0] + 1e-4 and b[1] > a[1] + 1e-4 for a, b in zip(knots, knots[1:]))


def face_name(face):
    if face.coord is None:
        n = face.axis
        return f"({n[0]:+.2f},{n[1]:+.2f})"
    return f"{face.axis}={face.coord:+.2f}"
