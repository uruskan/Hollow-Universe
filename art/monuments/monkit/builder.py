"""The monument builder (MONUMENTS.md §8.3).

A monument script calls primitives (boxes, cylinders, lathes, tubes, extrusions, slabs, beams), places instances of
shared kit pieces, and adds markers (loot, guards, doors, hazards), collision and the terrain pad. `export()` then
writes:

- `game/assets/monuments/<key>.glb`: the monument's own geometry, split into chunks on a square grid, each with three
  levels of detail. A level is rebuilt from the primitives, not decimated: level 1 drops parts smaller than half a
  metre and the bevels and halves the sides of round parts, level 2 keeps only the big masses. Every vertex carries
  its tint (RGB) and baked occlusion (A) in COLOR_0 and UVs in metres (the surfaces repeat at their own size); the
  shader takes the height above the monument's ground from the vertex's own position.
- `game/assets/monuments/kit/<piece>.glb`: each kit piece used, once, in the same layout.
- `game/data/monuments/<key>.json`: the pieces' instances, the collision (boxes, cylinders and convex hulls), the
  markers and the pad, in the game's frame (metres, +Y up, the monument's front toward +Z).

Authoring frame (Blender): metres, +Z up, the monument's front toward -Y, its ground at z = 0 and its centre at the
origin. Angles in degrees.
"""
import json
import math
import random
from contextlib import contextmanager

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

from . import DATA, KIT, MONUMENTS, log
from .surfaces import MATERIALS, PAINT

WHITE = (1.0, 1.0, 1.0)

# Smallest part (largest dimension, metres) each level keeps, and its detail: (sides factor, bevel, subdivide).
LOD_MIN = (0.0, 0.5, 3.0)
SIDES = (1.0, 0.5, 0.25)

# Default wear (0 new .. 1 ruined) by material, for the shader's grime and rust.
WEAR = {"M_Mon_Concrete": 0.35, "M_Mon_Formwork": 0.35, "M_Mon_Paint": 0.45, "M_Mon_Rust": 0.8,
        "M_Mon_Corrugated": 0.5, "M_Mon_Asphalt": 0.3, "M_Mon_Grate": 0.5, "M_Mon_Hull": 0.4, "M_Mon_Panel": 0.25,
        "M_Mon_Glass": 0.2, "M_Mon_Lamp": 0.0}


def _matrix(at=(0, 0, 0), rot=(0, 0, 0), scale=None):
    return Matrix.LocRotScale(Vector(at), Euler([math.radians(a) for a in rot]), Vector(scale) if scale else None)


def to_game(p):
    """Blender (x, y, z) → game (x, z, -y): +Y up, the front (Blender -Y) toward +Z."""
    return [round(p[0], 4), round(p[2], 4), round(-p[1], 4)]


def quat_to_game(q):
    """A rotation in Blender's frame as a quaternion (w, x, y, z) in the game's frame."""
    # The change of frame C maps (x, y, z) → (x, z, -y); the rotation's axis maps the same way, its angle keeps.
    return [round(q.w, 6), round(q.x, 6), round(q.z, 6), round(-q.y, 6)]


class Prim:
    __slots__ = ("kind", "args", "matrix", "material", "tint", "wear", "solid", "size", "uv_seed", "smooth", "centre")

    def __init__(self, kind, args, matrix, material, tint, wear, solid, size, uv_seed, smooth=True):
        self.kind = kind
        self.args = args
        self.matrix = matrix
        self.material = material
        self.tint = tint
        self.wear = wear
        self.solid = solid
        self.size = size
        self.uv_seed = uv_seed
        self.smooth = smooth
        if kind == "tube":
            pts = args["points"]
            mid = Vector([sum(q[i] for q in pts) / len(pts) for i in range(3)])
            self.centre = matrix @ mid
        else:
            self.centre = matrix.translation.copy()


# ------------------------------------------------------------------------------------------------ geometry


def _box_bm(size, bevel, detail, max_edge):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if detail == 0 and max_edge:
        for axis in range(3):
            n = int(size[axis] / max_edge)
            if n >= 1:
                edges = [e for e in bm.edges if abs(e.verts[0].co[axis] - e.verts[1].co[axis]) > 1e-6]
                bmesh.ops.subdivide_edges(bm, edges=edges, cuts=min(n, 24), use_grid_fill=True)
    if bevel > 0 and detail == 0:
        bm.normal_update()
        rim = [e for e in bm.edges if len(e.link_faces) == 2
               and e.link_faces[0].normal.angle(e.link_faces[1].normal, 0) > math.radians(60)]
        bmesh.ops.bevel(bm, geom=rim, offset=min(bevel, min(size) * 0.3), segments=2, profile=0.5, affect="EDGES",
                        clamp_overlap=True)
    return bm


def _cyl_bm(radius, depth, sides, radius2, cap, detail, max_edge):
    n = max(4 if detail else 6, int(round(sides * SIDES[detail])))
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=cap, cap_tris=False, segments=n, radius1=radius,
                          radius2=radius if radius2 is None else radius2, depth=depth)
    if detail == 0 and max_edge and depth > max_edge:
        edges = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-6]
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=min(int(depth / max_edge), 24), use_grid_fill=True)
    return bm


def _lathe_bm(profile, sides, detail, close):
    n = max(4 if detail else 6, int(round(sides * SIDES[detail])))
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r <= 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z))
                          for a in (2 * math.pi * i / n for i in range(n))])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for i in range(n):
                bm.faces.new((a[0], b[i], b[(i + 1) % n]))
        elif len(b) == 1:
            for i in range(n):
                bm.faces.new((a[i], b[0], a[(i + 1) % n]))
        else:
            for i in range(n):
                bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
    if close:
        if len(rings[0]) > 1:
            bm.faces.new(list(reversed(rings[0])))
        if len(rings[-1]) > 1:
            bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def _tube_bm(points, radius, sides, detail, cap, radii):
    n = max(3 if detail else 5, int(round(sides * SIDES[detail])))
    pts = [Vector(p) for p in points]
    bm = bmesh.new()
    rings = []
    prev_u = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if prev_u is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            u = t.cross(ref).normalized()
        else:
            u = (prev_u - t * prev_u.dot(t)).normalized()
        w = t.cross(u)
        prev_u = u
        r = radii[i] if radii else radius
        rings.append([bm.verts.new(p + r * (math.cos(a) * u + math.sin(a) * w))
                      for a in (2 * math.pi * j / n for j in range(n))])
    for a, b in zip(rings, rings[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def _extrude_bm(outline, width, bevel, detail):
    """`outline`: (x, z) points counter-clockwise seen from -Y, extruded `width` along Y, centred."""
    bm = bmesh.new()
    front = [bm.verts.new((x, -width / 2, z)) for x, z in outline]
    back = [bm.verts.new((x, width / 2, z)) for x, z in outline]
    k = len(outline)
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(k):
        j = (i + 1) % k
        bm.faces.new((front[j], front[i], back[i], back[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if bevel > 0 and detail == 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, profile=0.5, affect="EDGES",
                        clamp_overlap=True)
    return bm


def _slab_bm(outline, thickness):
    """`outline`: (x, y) points, extruded from z = 0 up by `thickness`."""
    bm = bmesh.new()
    bot = [bm.verts.new((x, y, 0)) for x, y in outline]
    top = [bm.verts.new((x, y, thickness)) for x, y in outline]
    k = len(outline)
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    for i in range(k):
        j = (i + 1) % k
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def _mesh_bm(verts, faces):
    bm = bmesh.new()
    vs = [bm.verts.new(v) for v in verts]
    for f in faces:
        bm.faces.new([vs[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def _orient_outward(bm):
    """A closed part whose faces came out wound inside-out is turned the right way (the game draws front faces
    only). Open parts (single faces, shells with edges) are left as authored."""
    if not bm.faces or any(not e.is_manifold for e in bm.edges):
        return bm
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        a = vs[0]
        for b, c in zip(vs[1:], vs[2:]):
            vol += a.dot(b.cross(c))
    if vol < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    return bm


def _geometry(p, detail, max_edge):
    return _orient_outward(_raw_geometry(p, detail, max_edge))


def _raw_geometry(p, detail, max_edge):
    a = p.args
    k = p.kind
    if k == "box":
        return _box_bm(a["size"], a.get("bevel", 0.0), detail, max_edge)
    if k == "cyl":
        return _cyl_bm(a["radius"], a["depth"], a.get("sides", 24), a.get("radius2"), a.get("cap", True), detail,
                       max_edge)
    if k == "lathe":
        return _lathe_bm(a["profile"], a.get("sides", 24), detail, a.get("close", True))
    if k == "tube":
        return _tube_bm(a["points"], a["radius"], a.get("sides", 10), detail, a.get("cap", True), a.get("radii"))
    if k == "extrude":
        return _extrude_bm(a["outline"], a["width"], a.get("bevel", 0.0), detail)
    if k == "slab":
        return _slab_bm(a["outline"], a["thickness"])
    if k == "mesh":
        return _mesh_bm(a["verts"], a["faces"])
    raise ValueError(k)


def _uvs(kind, verts, faces, args, seed):
    """Per-loop UVs in metres for one primitive in its own frame, with a random offset so copies differ."""
    r = random.Random(seed)
    ou, ov = r.random() * 8.0, r.random() * 8.0
    out = []
    if kind in ("cyl", "lathe"):
        for f in faces:
            pts = [verts[i] for i in f]
            n = _newell(pts)
            cap = abs(n.z) > 0.85
            if cap:
                out += [(v.x + ou, v.y + ov) for v in pts]
                continue
            cx = sum(v.x for v in pts) / len(pts)
            cy = sum(v.y for v in pts) / len(pts)
            centre = math.atan2(cy, cx)
            for v in pts:
                ang = math.atan2(v.y, v.x)
                if ang - centre > math.pi:
                    ang -= 2 * math.pi
                elif centre - ang > math.pi:
                    ang += 2 * math.pi
                rad = max(math.hypot(v.x, v.y), 1e-3)
                out.append(((ang + math.pi) * rad + ou, v.z + ov))
        return out
    for f in faces:
        pts = [verts[i] for i in f]
        n = _newell(pts)
        ax = max(range(3), key=lambda i: abs(n[i]))
        for v in pts:
            if ax == 2:      # up or down: u = x, v = y
                out.append((v.x + ou, (v.y if n.z > 0 else -v.y) + ov))
            elif ax == 0:    # facing ±x: u along ±y, v up
                out.append(((v.y if n.x > 0 else -v.y) + ou, v.z + ov))
            else:            # facing ±y: u along ∓x, v up
                out.append(((-v.x if n.y > 0 else v.x) + ou, v.z + ov))
    return out


def _newell(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i, a in enumerate(pts):
        b = pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))


def _canonical(bm):
    """Vertices in creation order; faces each starting at their lowest vertex, sorted (bmesh operators can emit faces
    in memory order, which would change the file on every build)."""
    bm.verts.index_update()
    verts = [v.co.copy() for v in bm.verts]
    faces = []
    for f in bm.faces:
        vs = [v.index for v in f.verts]
        k = vs.index(min(vs))
        faces.append(vs[k:] + vs[:k])
    faces.sort()
    bm.free()
    return verts, faces


class _Accumulator:
    """Geometry of one chunk at one level, gathered as arrays and turned into a single mesh at the end."""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.uv = []
        self.tint = []
        self.mats = []
        self.smooth = []

    def add(self, verts, faces, uvs, height_base, wear, tint, mat_index, smooth):
        base = len(self.verts)
        self.verts += verts
        self.tint += [tint] * len(verts)
        for f in faces:
            self.faces.append([base + i for i in f])
            self.mats.append(mat_index)
            self.smooth.append(smooth)
        self.uv += uvs

    def mesh(self, name, materials):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        uv = me.uv_layers.new(name="UVMap")
        uv.data.foreach_set("uv", np.asarray(self.uv, np.float32).ravel())
        col = me.color_attributes.new(name="Tint", type="FLOAT_COLOR", domain="POINT")
        rgba = np.ones((len(self.verts), 4), np.float32)
        rgba[:, :3] = np.asarray(self.tint, np.float32)
        col.data.foreach_set("color", rgba.ravel())
        for m in materials:
            me.materials.append(_material(m))
        me.polygons.foreach_set("material_index", np.asarray(self.mats, np.int32))
        me.polygons.foreach_set("use_smooth", np.asarray(self.smooth, bool))
        me.set_sharp_from_angle(angle=math.radians(38))
        me.update()
        return me


def _material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    return m


def occlusion(targets, occluders, samples=24, distance=4.0, floor=0.0):
    """Ambient occlusion per vertex of each target (1 open .. 0 closed), by rays against every occluder's faces and a
    ground plane at z = `floor` (None: no ground)."""
    verts, polys = [], []
    for o in occluders:
        me = o.data
        m = o.matrix_world
        base = len(verts)
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        mw = np.array(m, np.float64)
        co = co @ mw[:3, :3].T + mw[:3, 3]
        verts += [Vector(c) for c in co]
        polys += [[base + i for i in p.vertices] for p in me.polygons]
    if floor is not None and verts:
        xs = [v.x for v in verts]
        ys = [v.y for v in verts]
        pad = 50.0
        base = len(verts)
        verts += [Vector((min(xs) - pad, min(ys) - pad, floor)), Vector((max(xs) + pad, min(ys) - pad, floor)),
                  Vector((max(xs) + pad, max(ys) + pad, floor)), Vector((min(xs) - pad, max(ys) + pad, floor))]
        polys.append([base, base + 1, base + 2, base + 3])
    tree = BVHTree.FromPolygons(verts, polys, epsilon=0.0)
    golden = math.pi * (3 - math.sqrt(5))
    dirs = []
    for i in range(samples):
        rr = math.sqrt((i + 0.5) / samples)
        a = i * golden
        dirs.append((rr * math.cos(a), rr * math.sin(a), math.sqrt(max(0.0, 1 - rr * rr))))
    result = {}
    for o in targets:
        me = o.data
        m = o.matrix_world
        nm = m.to_3x3().inverted().transposed()
        values = np.ones(len(me.vertices), np.float32)
        for i, v in enumerate(me.vertices):
            p = m @ v.co
            n = (nm @ v.normal).normalized()
            t = n.orthogonal().normalized()
            b = n.cross(t)
            origin = p + n * 0.02
            hits = 0.0
            for dx, dy, dz in dirs:
                ray = t * dx + b * dy + n * dz
                start, left, travelled = origin, distance, 0.0
                # A hit seen from behind means the ray started inside a solid that touches this one (a plinth under
                # a wall, a roof on it): walk through such faces and count only the first one met from outside.
                for _ in range(4):
                    loc, normal, _, dist = tree.ray_cast(start, ray, left)
                    if loc is None:
                        break
                    if normal.dot(ray) > 0:
                        start = loc + ray * 0.003
                        travelled += dist + 0.003
                        left -= dist + 0.003
                        if left <= 0:
                            break
                        continue
                    d = travelled + dist
                    hits += 1.0 - (d / distance) ** 2
                    break
            values[i] = 1.0 - hits / samples
        result[o] = values
    return result


# ------------------------------------------------------------------------------------------------ the builder


class Builder:
    """Gathers one monument (or one kit piece). Use `frame()` to place sub-assemblies; every primitive takes `at` and
    `rot` relative to the current frame."""

    folder = MONUMENTS

    def __init__(self, key, seed=1, chunk=40.0, max_edge=3.0, lod_min=LOD_MIN, title=None):
        self.key = key
        self.title = title or key
        self.seed = seed
        self.rng = random.Random(seed)
        self.chunk = chunk
        self.max_edge = max_edge
        self.lod_min = lod_min
        self.prims = []
        self.frames = [Matrix.Identity(4)]
        self.instances = []
        self.markers = []
        self.extra_collision = []
        self.pad = None
        self.meta = {}

    # ---------------------------------------------------------------- frames

    @property
    def m(self):
        return self.frames[-1]

    @contextmanager
    def frame(self, at=(0, 0, 0), rot=(0, 0, 0), scale=None):
        self.frames.append(self.frames[-1] @ _matrix(at, rot, scale))
        try:
            yield self
        finally:
            self.frames.pop()

    def world(self, p):
        """A point of the current frame in the monument's frame."""
        return self.m @ Vector(p)

    # ---------------------------------------------------------------- primitives

    def _prim(self, kind, args, at, rot, mat, tint, wear, solid, size, vary, smooth=True):
        if vary:
            k = 1.0 + (self.rng.random() - 0.5) * 2 * vary
            tint = tuple(min(1.0, c * k) for c in tint)
        p = Prim(kind, args, self.m @ _matrix(at, rot), mat, tuple(tint), WEAR.get(mat, 0.4) if wear is None else wear,
                 solid, size, self.rng.randrange(1 << 30), smooth)
        self.prims.append(p)
        return p

    def box(self, size, at=(0, 0, 0), rot=(0, 0, 0), mat=PAINT, tint=WHITE, bevel=0.0, solid=True, wear=None,
            vary=0.03):
        return self._prim("box", {"size": tuple(size), "bevel": bevel}, at, rot, mat, tint, wear, solid,
                          max(size), vary)

    def cyl(self, radius, depth, at=(0, 0, 0), rot=(0, 0, 0), mat=PAINT, tint=WHITE, sides=24, radius2=None, cap=True,
            solid=True, wear=None, vary=0.03):
        return self._prim("cyl", {"radius": radius, "depth": depth, "sides": sides, "radius2": radius2, "cap": cap},
                          at, rot, mat, tint, wear, solid, max(2 * radius, depth), vary)

    def lathe(self, profile, at=(0, 0, 0), rot=(0, 0, 0), mat=PAINT, tint=WHITE, sides=24, close=True, solid="hull",
              wear=None, vary=0.03):
        rmax = max(r for r, _ in profile)
        h = max(z for _, z in profile) - min(z for _, z in profile)
        return self._prim("lathe", {"profile": list(profile), "sides": sides, "close": close}, at, rot, mat, tint,
                          wear, solid, max(2 * rmax, h), vary)

    def tube(self, points, radius, mat=PAINT, tint=WHITE, sides=10, cap=True, radii=None, solid=False, wear=None,
             vary=0.03):
        pts = [tuple(p) for p in points]
        lo = [min(p[i] for p in pts) for i in range(3)]
        hi = [max(p[i] for p in pts) for i in range(3)]
        size = max(max(hi[i] - lo[i] for i in range(3)), 2 * radius)
        return self._prim("tube", {"points": pts, "radius": radius, "sides": sides, "cap": cap, "radii": radii},
                          (0, 0, 0), (0, 0, 0), mat, tint, wear, solid, size, vary)

    def extrude(self, outline, width, at=(0, 0, 0), rot=(0, 0, 0), mat=PAINT, tint=WHITE, bevel=0.0, solid="hull",
                wear=None, vary=0.03):
        xs = [p[0] for p in outline]
        zs = [p[1] for p in outline]
        size = max(max(xs) - min(xs), max(zs) - min(zs), width)
        return self._prim("extrude", {"outline": [tuple(p) for p in outline], "width": width, "bevel": bevel}, at,
                          rot, mat, tint, wear, solid, size, vary)

    def slab(self, outline, thickness, at=(0, 0, 0), rot=(0, 0, 0), mat=PAINT, tint=WHITE, solid="hull", wear=None,
             vary=0.03):
        xs = [p[0] for p in outline]
        ys = [p[1] for p in outline]
        size = max(max(xs) - min(xs), max(ys) - min(ys), thickness)
        return self._prim("slab", {"outline": [tuple(p) for p in outline], "thickness": thickness}, at, rot, mat,
                          tint, wear, solid, size, vary)

    def mesh(self, verts, faces, at=(0, 0, 0), rot=(0, 0, 0), mat=PAINT, tint=WHITE, solid="hull", wear=None,
             vary=0.03, smooth=True):
        vs = [tuple(v) for v in verts]
        lo = [min(v[i] for v in vs) for i in range(3)]
        hi = [max(v[i] for v in vs) for i in range(3)]
        return self._prim("mesh", {"verts": vs, "faces": [list(f) for f in faces]}, at, rot, mat, tint, wear, solid,
                          max(hi[i] - lo[i] for i in range(3)), vary, smooth)

    def beam(self, a, b, w, h=None, mat=PAINT, tint=WHITE, solid=False, wear=None, vary=0.0, roll=0.0):
        """A box of section w x h from point `a` to point `b` (current frame)."""
        a, b = Vector(a), Vector(b)
        d = b - a
        length = d.length
        if length < 1e-6:
            return None
        h = w if h is None else h
        q = d.to_track_quat("Z", "Y" if abs(d.normalized().z) < 0.99 else "X")
        rot = (q.to_matrix().to_4x4() @ Matrix.Rotation(math.radians(roll), 4, "Z")).to_euler()
        mid = (a + b) / 2
        return self.box((w, h, length), tuple(mid), tuple(math.degrees(x) for x in rot), mat=mat, tint=tint,
                        solid=solid, wear=wear, vary=vary)

    # ---------------------------------------------------------------- kit pieces, markers, pad

    def collide_box(self, centre, size, rot=(0, 0, 0)):
        """An invisible collision box (stairs as a ramp, a rail as a wall) in the current frame."""
        loc, q, _ = (self.m @ _matrix(centre, rot)).decompose()
        self.extra_collision.append({"box": to_game(loc), "half": [size[0] / 2, size[2] / 2, size[1] / 2],
                                     "rot": quat_to_game(q)})

    def piece(self, key, at=(0, 0, 0), rot=(0, 0, 0), tint=None):
        """An instance of kit piece `key` (built once, shared by every monument)."""
        from . import kit
        kit.ensure(key)
        self.instances.append((key, self.m @ _matrix(at, rot), tint))

    def marker(self, kind, at=(0, 0, 0), yaw=0.0, **props):
        """A gameplay point: loot (props: table), guard, door, ladder, light, recycler, spawn... in the current frame."""
        p = self.world(at)
        # The frame's own heading: the angle of its +Y about Z.
        fwd = self.m.to_3x3() @ Vector((0, 1, 0))
        heading = math.degrees(math.atan2(-fwd.x, fwd.y)) + yaw
        self.markers.append({"kind": kind, "at": to_game(p), "yaw": round(heading % 360, 2), **props})

    def zone(self, kind, centre, radius=None, size=None, **props):
        """A volume: radiation (props: level), safe, no-build, puzzle... A sphere (`radius`) or a box (`size`)."""
        c = self.world(centre)
        z = {"kind": kind, "at": to_game(c), **props}
        if radius is not None:
            z["radius"] = radius
        if size is not None:
            z["size"] = [size[0], size[2], size[1]]
        self.markers.append(z)

    def set_pad(self, shape, ground="gravel", blend=12.0, clear=6.0, height=0.0, **props):
        """The terrain under the monument: flattened to the monument's ground inside `shape` (("rect", w, d),
        ("circle", r) or ("poly", [(x, y)...]), monument frame) and blended back to nature over `blend` metres;
        `ground` paints the terrain's material, `clear` keeps plants that far outside."""
        self.pad = {"shape": shape, "ground": ground, "blend": blend, "clear": clear, "height": height, **props}

    # ---------------------------------------------------------------- export

    def _materials(self):
        names = sorted({p.material for p in self.prims})
        return names, {n: i for i, n in enumerate(names)}

    def _chunk_of(self, p):
        c = p.centre
        return int(math.floor(c.x / self.chunk)), int(math.floor(c.y / self.chunk))

    def build_level(self, level, names, index):
        """{chunk: mesh} for one level of detail."""
        acc = {}
        for p in self.prims:
            if p.size < self.lod_min[level]:
                continue
            bm = _geometry(p, level, self.max_edge if level == 0 else self.max_edge * 3)
            verts, faces = _canonical(bm)
            uvs = _uvs(p.kind, verts, faces, p.args, p.uv_seed)
            world = [p.matrix @ v for v in verts]
            # A negative scale or mirror would flip faces; primitives never use one.
            key = self._chunk_of(p)
            acc.setdefault(key, _Accumulator()).add(world, faces, uvs, 0.0, p.wear, p.tint, index[p.material],
                                                    p.smooth)
        return {k: a.mesh(f"C{k[0]}_{k[1]}_lod{level}", names) for k, a in sorted(acc.items())}

    def _collision(self):
        out = []
        for p in self.prims:
            if not p.solid:
                continue
            loc, rot, _ = p.matrix.decompose()
            if p.kind == "box" and p.solid is True:
                s = p.args["size"]
                out.append({"box": to_game(loc), "half": [s[0] / 2, s[2] / 2, s[1] / 2], "rot": quat_to_game(rot)})
            elif p.kind == "cyl" and p.solid is True and p.args.get("radius2") is None:
                # Jolt's cylinder runs along its local Y; ours along local Z, which the game frame turns into Y.
                out.append({"cyl": to_game(loc), "radius": p.args["radius"], "half": p.args["depth"] / 2,
                            "rot": quat_to_game(rot)})
            else:
                bm = _geometry(p, 2, 0)
                verts, _ = _canonical(bm)
                pts = _hull_points([p.matrix @ v for v in verts])
                out.append({"hull": [to_game(v) for v in pts]})
        return out + self.extra_collision

    def bounds(self):
        lo = Vector((1e9, 1e9, 1e9))
        hi = Vector((-1e9, -1e9, -1e9))
        for p in self.prims:
            c = p.centre
            h = p.size / 2
            for i in range(3):
                lo[i] = min(lo[i], c[i] - h)
                hi[i] = max(hi[i], c[i] + h)
        return lo, hi

    def realise(self, collection, level=0):
        """Builds the monument into `collection` as objects (its chunks and its pieces' instances) and returns
        (chunk objects, instance objects)."""
        from . import kit
        names, index = self._materials()
        chunks = []
        for k, me in self.build_level(level, names, index).items():
            o = bpy.data.objects.new(me.name, me)
            collection.objects.link(o)
            chunks.append(o)
        inst = []
        for key, mat, tint in self.instances:
            me = kit.mesh(key, level)
            o = bpy.data.objects.new(f"{key}.inst", me)
            o.matrix_world = mat
            if tint:
                o.color = (*tint, 1.0)
            o["piece_tint"] = 1 if tint else 0
            collection.objects.link(o)
            inst.append(o)
        return chunks, inst

    def export(self, lod_distances=None, ao_samples=24, ao_distance=4.0):
        from hollow.export import collection, export_glb, select_only
        from .godot_mats import godot_materials
        work = collection(f"MON_{self.key}")
        names, index = self._materials()
        objects = []
        lod0 = None
        for level in range(3):
            meshes = self.build_level(level, names, index)
            objs = []
            for k, me in meshes.items():
                o = bpy.data.objects.new(me.name, me)
                work.objects.link(o)
                objs.append(o)
            if level == 0:
                lod0 = objs
                from . import kit
                occl = list(objs)
                for key, mat, _ in self.instances:
                    o = bpy.data.objects.new("occluder", kit.mesh(key, 0))
                    o.matrix_world = mat
                    work.objects.link(o)
                    occl.append(o)
                ao = occlusion(objs, occl, ao_samples, ao_distance, 0.0)
                for o in occl[len(objs):]:
                    bpy.data.objects.remove(o)
            elif level == 1:
                ao = occlusion(objs, objs, max(8, ao_samples // 2), ao_distance, 0.0)
            else:
                ao = {o: np.ones(len(o.data.vertices), np.float32) for o in objs}
            for o in objs:
                _bake_colour(o, ao[o])
            objects += objs
        path = export_glb(self.folder / f"{self.key}.glb", objects, godot_materials(names))
        lo, hi = _mesh_bounds(lod0) if lod0 else self.bounds()
        size = hi - lo
        data = {
            "key": self.key,
            "title": self.title,
            "source": "Hollow (art/monuments)",
            "model": f"res://assets/monuments/{self.key}.glb",
            "bounds": {"min": to_game(Vector((lo.x, hi.y, lo.z))), "max": to_game(Vector((hi.x, lo.y, hi.z)))},
            "height": round(hi.z, 2),
            "chunk": self.chunk,
            "lods": lod_distances or _default_lods(max(size.x, size.y, size.z)),
            "pieces": [{"piece": key, "at": to_game(m.translation), "rot": quat_to_game(m.to_quaternion()),
                        **({"tint": [round(c, 3) for c in tint]} if tint else {})}
                       for key, m, tint in self.instances],
            "collision": self._collision(),
            "markers": self.markers,
            "pad": _pad_to_game(self.pad),
            **self.meta,
        }
        DATA.mkdir(parents=True, exist_ok=True)
        (DATA / f"{self.key}.json").write_text(json.dumps(data, indent=1) + "\n", encoding="utf-8", newline="\n")
        select_only([])
        log(f"{self.key}: {len(self.prims)} primitives, {len(self.instances)} pieces, {len(self.markers)} markers")
        return path


def rounded(v, places=4):
    """Floats in nested data rounded (so files stay short and stable)."""
    if isinstance(v, float):
        r = round(v, places)
        return 0.0 if r == 0 else r
    if isinstance(v, dict):
        return {k: rounded(x, places) for k, x in v.items()}
    if isinstance(v, (list, tuple)):
        return [rounded(x, places) for x in v]
    return v


def _mesh_bounds(objs):
    lo = np.full(3, 1e9)
    hi = np.full(3, -1e9)
    for o in objs:
        co = np.empty(len(o.data.vertices) * 3, np.float32)
        o.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        if len(co):
            lo = np.minimum(lo, co.min(axis=0))
            hi = np.maximum(hi, co.max(axis=0))
    return Vector(lo.tolist()), Vector(hi.tolist())


def _default_lods(size):
    near = max(60.0, size * 1.2)
    return [round(near, 1), round(near * 4, 1), round(max(1500.0, size * 30), 1)]


def _pad_to_game(pad):
    if not pad:
        return None
    out = dict(pad)
    s = pad["shape"]
    if s[0] == "rect":
        out["shape"] = {"rect": [s[1], s[2]]}
    elif s[0] == "circle":
        out["shape"] = {"circle": s[1]}
    else:
        out["shape"] = {"poly": [[round(x, 3), round(-y, 3)] for x, y in s[1]]}
    return out


def _bake_colour(o, ao):
    from hollow.export import set_export_colour
    me = o.data
    tint = np.empty(len(me.vertices) * 4, np.float32)
    me.color_attributes["Tint"].data.foreach_get("color", tint)
    tint = tint.reshape(-1, 4)
    tint[:, 3] = np.clip(ao, 0, 1)
    set_export_colour(o, tint, "POINT")


def _hull_points(points, max_points=48):
    """A convex hull's vertices (at most `max_points`, the extremes along fixed directions)."""
    bm = bmesh.new()
    for v in points:
        bm.verts.new(v)
    res = bmesh.ops.convex_hull(bm, input=bm.verts, use_existing_faces=False)
    hull = [v.co.copy() for v in bm.verts if v.is_valid and v.link_faces]
    del res
    bm.free()
    if len(hull) <= max_points:
        return sorted(hull, key=lambda v: (round(v.x, 4), round(v.y, 4), round(v.z, 4)))
    keep = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(max_points):
        z = 1 - 2 * (i + 0.5) / max_points
        rr = math.sqrt(1 - z * z)
        a = i * golden
        d = Vector((rr * math.cos(a), rr * math.sin(a), z))
        best = max(hull, key=lambda v: v.dot(d))
        if all((best - k).length > 1e-4 for k in keep):
            keep.append(best)
    return sorted(keep, key=lambda v: (round(v.x, 4), round(v.y, 4), round(v.z, 4)))
