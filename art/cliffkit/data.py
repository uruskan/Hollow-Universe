"""What the placement needs to know about each piece (docs/WORLD_TERRAIN_PLAN.md §4.3-4.4), in the frame Godot and the
engine-free code see it: metres, +Y up, the face toward +X, the pivot on the ground line at the middle of the face's
foot, the left end (as you face the rock) toward +Z. Blender's (x, y, z) is (x, z, -y) there.

The collision shell goes in a small binary file beside the GLB (`<key>.collision.bin`, little-endian):
"HCOL", u32 version (1), u32 vertex count, u32 triangle count, then f32 x, y, z per vertex, then u16 a, b, c per
triangle, counter-clockwise seen from outside.
"""
import hashlib
import math
import struct

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from .shape import SOCKET_INSET

GRID = 1.0       # the height grid's cell, metres


def godot(v):
    return [round(float(v[0]), 3), round(float(v[2]), 3), round(float(-v[1]), 3)]


def plan(v):
    """A Blender point's place on Godot's ground plane: (x, z)."""
    return [round(float(v[0]), 3), round(float(-v[1]), 3)]


def hull(points):
    """The convex hull of 2D points, counter-clockwise (Andrew's monotone chain)."""
    pts = sorted({(round(p[0], 3), round(p[1], 3)) for p in points})
    if len(pts) < 3:
        return [list(p) for p in pts]

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return [list(p) for p in lower[:-1] + upper[:-1]]


def simplify(polygon, most=24):
    """Drops the hull's points that turn it least until at most `most` remain."""
    poly = [list(p) for p in polygon]
    while len(poly) > most:
        best, at = None, 0
        for i in range(len(poly)):
            a, b, c = poly[i - 1], poly[i], poly[(i + 1) % len(poly)]
            area = abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1]))
            if best is None or area < best:
                best, at = area, i
        poly.pop(at)
    return poly


def write_collision(path, obj):
    """The closed shell in Godot's frame; returns (vertices, triangles, sha256)."""
    me = obj.data
    me.calc_loop_triangles()
    verts = [godot(v.co) for v in me.vertices]
    tris = [t.vertices for t in me.loop_triangles]
    data = b"HCOL" + struct.pack("<3I", 1, len(verts), len(tris))
    data += struct.pack(f"<{3 * len(verts)}f", *[c for v in verts for c in v])
    data += struct.pack(f"<{3 * len(tris)}H", *[i for t in tris for i in t])
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return len(verts), len(tris), hashlib.sha256(data).hexdigest()


def lod_distances(size):
    """Where each level hands over to the next (LOD1, LOD2, LOD3, the far shell) and where the piece goes (0: never)."""
    return [round(min(90.0, max(25.0, 2.2 * size)), 1), round(min(170.0, max(50.0, 4.5 * size)), 1),
            round(min(240.0, max(90.0, 7.0 * size)), 1), 250.0, 0.0 if size >= 8.0 else 600.0]


def _along(points, t):
    """The point a fraction `t` of the way along a polyline."""
    lengths = [(points[i + 1] - points[i]).length for i in range(len(points) - 1)]
    total = sum(lengths) or 1.0
    d = t * total
    for i, seg in enumerate(lengths):
        if d <= seg or i == len(lengths) - 1:
            return points[i].lerp(points[i + 1], min(1.0, d / seg) if seg > 0 else 0.0)
        d -= seg
    return points[-1]


def placement(shape, lods, col):
    """The piece's placement data (sockets, caps, anchors and checks, footprint, heights, LOD distances)."""
    spec = shape.spec
    lod0 = lods[0]
    co = np.array([v.co for v in lod0.data.vertices])
    above = co[co[:, 2] > -0.5]
    lo, hi = above.min(axis=0), above.max(axis=0)
    size = float(max(hi - lo))
    shape.size = size
    rubble = spec["role"] == "rubble"

    sockets, caps = [], {"left": False, "right": False}
    feet = {}
    for side in ("left", "right"):
        foot, out, kind = shape.ends[side]
        feet[side] = foot
        if rubble:
            continue
        if spec["role"] == "end" and kind in ("taper", "natural"):
            caps[side] = True
            continue
        p = foot - out * SOCKET_INSET
        p.z = 0.0
        sockets.append({"name": side, "position": godot(p), "direction": godot(out.normalized())})

    tree = BVHTree.FromObject(col, bpy.context.evaluated_depsgraph_get())

    def top_at(x, y):
        hit = tree.ray_cast(Vector((x, y, hi[2] + 20.0)), Vector((0, 0, -1)), hi[2] + 40.0)
        return hit[0].z if hit[0] is not None else None

    anchors, checks = [], []
    if rubble:
        ring = hull([(p[0], p[1]) for p in co[(co[:, 2] > -0.3) & (co[:, 2] < 0.6)]])
        anchors = [[0.0, 0.0, 0.0]] + [godot((x * 0.7, y * 0.7, 0.0)) for x, y in ring[::max(1, len(ring) // 4)][:4]]
    else:
        line = [feet["left"], Vector((0, 0, 0)), feet["right"]]
        for t in (0.1, 0.3, 0.5, 0.7, 0.9):
            anchors.append(godot(_along(line, t)))
    for a in anchors:
        checks.append({"kind": "foot", "position": a, "below": 1.5, "above": 2.0})
    if spec["fill"] and not rubble:
        # Two metres behind the lip at three places along the piece: the ground behind should stand near the top there,
        # or the open back would show.
        line = [feet["left"], Vector((0, 0, 0)), feet["right"]]
        for t in (0.2, 0.5, 0.8):
            p = _along(line, t)
            near = co[np.abs(co[:, 1] - p.y) < 1.0]
            if not len(near):
                continue
            ztop = near[:, 2].max()
            lip_x = near[near[:, 2] > ztop - 2.0][:, 0].max()
            h = top_at(lip_x - 2.0, p.y)
            if h is not None:
                checks.append({"kind": "top", "position": godot((lip_x - 2.0, p.y, h)), "below": 4.0, "above": 1.0})

    base = co[(co[:, 2] > -0.5) & (co[:, 2] < 1.5)]
    footprint = simplify(hull([plan(p) for p in base])) if len(base) >= 3 else []
    outline = simplify(hull([plan(p) for p in co]))

    # The rock's top over a grid on the ground plane (Godot x, z), from the closed shell: what a stamp fits the ground
    # to, and where the rock stands at all.
    x0, z0 = math.floor(lo[0]), math.floor(-hi[1])
    nx, nz = int(math.ceil(hi[0]) - x0), int(math.ceil(-lo[1]) - z0)
    heights = []
    for j in range(nz):
        for i in range(nx):
            gx, gz = x0 + (i + 0.5) * GRID, z0 + (j + 0.5) * GRID
            h = top_at(gx, -gz)
            heights.append(round(h, 2) if h is not None else None)

    return {
        # Depth (x), height above the ground line (y) and length (z).
        "size": [round(float(hi[0] - lo[0]), 3), round(float(hi[2]), 3), round(float(hi[1] - lo[1]), 3)],
        "bounds": {"min": [round(float(lo[0]), 3), round(float(co[:, 2].min()), 3), round(float(-hi[1]), 3)],
                   "max": [round(float(hi[0]), 3), round(float(hi[2]), 3), round(float(-lo[1]), 3)]},
        "top": round(float(shape.top), 3),
        "skirt": round(shape.skirt, 3),
        "sockets": sockets,
        "caps": caps,
        "scale": list(spec["range"]),
        "anchors": anchors,
        "checks": checks,
        "footprint": footprint,
        "outline": outline,
        "grid": {"cell": GRID, "origin": [x0, z0], "columns": nx, "rows": nz, "top": heights},
        "lods": [{"triangles": sum(len(p.vertices) - 2 for p in o.data.polygons)} for o in lods],
        "lodDistances": lod_distances(size),
        "shadowLod": 2,
    }
