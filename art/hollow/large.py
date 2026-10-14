"""Helpers for large assembled models (the Iron Wake, docs/IRON_WAKE.md §9): contact occlusion baked per vertex by
rays, carried between levels of detail, faces seen from outside, material slots, and deterministic JSON.

The occlusion is the monument kit's (art/monuments/monkit/builder.py): rays over the hemisphere of each vertex, a hit
weighted by (1 - (d / distance)^2), and a hit on a face seen from behind walked through, so a vertex buried in a
neighbouring solid (a bolt in a plate, a rib in a wall) reads the geometry outside that solid rather than black.
"""
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import log


def hemisphere(n):
    """`n` deterministic unit directions over the +Z hemisphere, cosine-weighted (golden spiral), as an n x 3 array."""
    i = np.arange(n, dtype=np.float64)
    r = np.sqrt((i + 0.5) / n)
    a = i * math.pi * (3 - math.sqrt(5))
    return np.stack((r * np.cos(a), r * np.sin(a), np.sqrt(np.maximum(0.0, 1 - r * r))), axis=1)


def sphere(n):
    """`n` deterministic unit directions over the whole sphere (Fibonacci lattice), as an n x 3 array."""
    i = np.arange(n, dtype=np.float64)
    z = 1 - 2 * (i + 0.5) / n
    r = np.sqrt(np.maximum(0.0, 1 - z * z))
    a = i * math.pi * (3 - math.sqrt(5))
    return np.stack((r * np.cos(a), r * np.sin(a), z), axis=1)


def mesh_arrays(o):
    """World-space vertex positions and normals, loop triangles (vertex indices) and each triangle's material name."""
    me = o.data
    me.calc_loop_triangles()
    n = len(me.vertices)
    co = np.empty(n * 3, np.float64)
    me.vertices.foreach_get("co", co)
    nr = np.empty(n * 3, np.float64)
    me.vertex_normals.foreach_get("vector", nr)
    mw = np.array(o.matrix_world, np.float64)
    co = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    nr = nr.reshape(-1, 3) @ np.linalg.inv(mw[:3, :3])
    nr /= np.maximum(np.linalg.norm(nr, axis=1, keepdims=True), 1e-12)
    t = len(me.loop_triangles)
    tv = np.empty(t * 3, np.int32)
    me.loop_triangles.foreach_get("vertices", tv)
    tm = np.empty(t, np.int32)
    me.loop_triangles.foreach_get("material_index", tm)
    names = [m.name if m else "" for m in me.materials] or [""]
    return co, nr, tv.reshape(-1, 3), [names[i] if i < len(names) else "" for i in tm]


class Triangles:
    """Every triangle of some objects in world space, in one BVH tree; `owner[i]` is the object of triangle i."""

    def __init__(self, objects, skip_materials=()):
        verts, tris, owners, ranges, materials = [], [], [], {}, []
        base = 0
        for o in objects:
            co, _, tv, mats = mesh_arrays(o)
            keep = np.array([m not in skip_materials for m in mats], bool) if len(mats) else np.zeros(0, bool)
            ranges[o] = (base, len(co))
            verts.append(co)
            if keep.any():
                tris.append(tv[keep] + base)
                owners += [o] * int(keep.sum())
                materials += [m for m, k in zip(mats, keep) if k]
            base += len(co)
        self.verts = np.concatenate(verts) if verts else np.zeros((0, 3))
        self.tris = np.concatenate(tris) if tris else np.zeros((0, 3), np.int32)
        self.owner = owners
        self.materials = materials
        self.ranges = ranges
        self.tree = BVHTree.FromPolygons(self.verts.tolist(), self.tris.tolist(), epsilon=0.0)


def contact_ao(targets, occluders, samples=40, distance=1.2, offset=0.006):
    """Ambient occlusion per vertex of each target (1 open .. 0 closed, POINT domain) by `samples` rays over each
    vertex's hemisphere against `occluders` (a Triangles). Returns {object: float32 array}."""
    dirs = hemisphere(samples)
    rc = occluders.tree.ray_cast
    result = {}
    for o in targets:
        co, nr, _, _ = mesh_arrays(o)
        # A tangent frame per vertex: t from the axis least aligned with the normal.
        helper = np.where(np.abs(nr[:, :1]) < 0.9, np.array([[1.0, 0.0, 0.0]]), np.array([[0.0, 1.0, 0.0]]))
        t = np.cross(nr, helper)
        t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-12)
        b = np.cross(nr, t)
        origins = (co + nr * offset).tolist()
        values = np.ones(len(co), np.float32)
        for i in range(len(co)):
            rays = (dirs[:, :1] * t[i] + dirs[:, 1:2] * b[i] + dirs[:, 2:] * nr[i]).tolist()
            origin = origins[i]
            hits = 0.0
            for d in rays:
                loc, normal, _, dist = rc(origin, d, distance)
                if loc is None:
                    continue
                if normal.dot(d) <= 0:
                    hits += 1.0 - (dist / distance) ** 2
                    continue
                # Seen from behind: the ray started inside a solid touching this one; walk through it.
                ray = Vector(d)
                travelled = dist + 0.003
                start = loc + ray * 0.003
                for _ in range(3):
                    left = distance - travelled
                    if left <= 0:
                        break
                    loc, normal, _, dist = rc(start, ray, left)
                    if loc is None:
                        break
                    if normal.dot(ray) <= 0:
                        hits += 1.0 - ((travelled + dist) / distance) ** 2
                        break
                    travelled += dist + 0.003
                    start = loc + ray * 0.003
            values[i] = 1.0 - hits / samples
        result[o] = values
        log(f"occlusion: {o.name} ({len(co)} vertices)")
    return result


def carry_ao(source, source_ao, targets):
    """Occlusion for other levels of detail: each target vertex takes the value at the nearest point of the `source`
    Triangles, interpolated across that triangle. `source_ao`: {object: per-vertex array} of the source's objects."""
    values = np.ones(len(source.verts), np.float32)
    for o, (base, n) in source.ranges.items():
        if o in source_ao:
            values[base:base + n] = source_ao[o]
    nearest = source.tree.find_nearest
    result = {}
    for o in targets:
        co, _, _, _ = mesh_arrays(o)
        out = np.ones(len(co), np.float32)
        for i, p in enumerate(co.tolist()):
            loc, _, idx, _ = nearest(p)
            if loc is None:
                continue
            a, b, c = source.tris[idx]
            pa, pb, pc = source.verts[a], source.verts[b], source.verts[c]
            v0, v1, v2 = pb - pa, pc - pa, np.array(loc) - pa
            d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
            d20, d21 = v2 @ v0, v2 @ v1
            den = d00 * d11 - d01 * d01
            if abs(den) < 1e-18:
                out[i] = (values[a] + values[b] + values[c]) / 3
                continue
            w1 = (d11 * d20 - d01 * d21) / den
            w2 = (d00 * d21 - d01 * d20) / den
            w0 = 1 - w1 - w2
            out[i] = w0 * values[a] + w1 * values[b] + w2 * values[c]
        result[o] = np.clip(out, 0, 1)
    return result


def outside_faces(objects, blockers, directions=48, reach=500.0):
    """For each object, a bool per polygon: True when a ray from the face's centre over its front hemisphere escapes
    `blockers` (a Triangles) to `reach`, i.e. the face can be seen from outside."""
    dirs = hemisphere(directions)
    rc = blockers.tree.ray_cast
    result = {}
    for o in objects:
        me = o.data
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        seen = np.zeros(len(me.polygons), bool)
        for k, p in enumerate(me.polygons):
            n = (nm @ p.normal).normalized()
            if n.length < 0.5:
                continue
            c = mw @ p.center + n * 0.01
            t = n.orthogonal().normalized()
            b = n.cross(t)
            for dx, dy, dz in dirs.tolist():
                d = t * dx + b * dy + n * dz
                if rc(c, d, reach)[0] is None:
                    seen[k] = True
                    break
        result[o] = seen
    return result


def decimate_to(o, triangles_wanted):
    """Collapse-decimates a mesh object in place to about `triangles_wanted` triangles."""
    o.data.calc_loop_triangles()
    have = len(o.data.loop_triangles)
    if have <= triangles_wanted:
        return
    mod = o.modifiers.new("decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(1e-4, triangles_wanted / have)
    mod.use_collapse_triangulate = True
    select_only_one(o)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    o.data.validate(verbose=False)


def select_only_one(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.hide_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def voxel_shell(o, voxel, coarse):
    """Replaces a mesh object's geometry (world space, identity transform) by the closed surface round the union of its
    parts (a voxel remesh), decimated to about `coarse` triangles: a distant proxy's raw shape, its thin shells and
    overlapping pieces fused, nothing left to fold into slivers when it is decimated further."""
    mod = o.modifiers.new("shell", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = voxel
    mod.adaptivity = 0.0
    select_only_one(o)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    decimate_to(o, coarse)


def delete_faces(o, keep):
    """Deletes the faces of `o` whose entry in the bool array `keep` is False."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep[f.index]], context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(o.data)
    bm.free()


def face_colours(o, source, colour_of_triangle, offset=0.4):
    """A CORNER colour per face of `o` (a proxy wrapped round `source`): the mean colour of what is seen of the source
    looking in along the face's normal from just outside it, at the face's centre and half-way to each corner
    (`colour_of_triangle(index)` -> linear RGB; the nearest triangle where a ray finds nothing). Returns n_loops x 4."""
    me = o.data
    mw = o.matrix_world
    nm = mw.to_3x3().inverted().transposed()
    cast = source.tree.ray_cast
    nearest = source.tree.find_nearest
    out = np.ones((len(me.loops), 4), np.float32)
    for p in me.polygons:
        n = (nm @ p.normal).normalized()
        c = mw @ p.center
        points = [c] + [(c + mw @ me.vertices[v].co) * 0.5 for v in p.vertices]
        acc, count = np.zeros(3), 0
        for q in points:
            hit = cast(q + n * offset, -n, offset * 2 + 0.5)
            if hit[0] is None:
                hit = nearest(q)
            if hit[0] is not None:
                acc += np.asarray(colour_of_triangle(hit[2]), np.float64)
                count += 1
        rgb = acc / count if count else (0.2, 0.2, 0.2)
        for li in p.loop_indices:
            out[li, :3] = rgb
    return out


def area(o):
    """Total face area of a mesh object (its own scale)."""
    a = np.empty(len(o.data.polygons), np.float32)
    o.data.polygons.foreach_get("area", a)
    return float(a.sum())


def drop_unused_slots(o):
    """Removes material slots no face uses, keeping the faces' materials."""
    me = o.data
    idx = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("material_index", idx)
    used = sorted(set(idx.tolist()))
    if used == list(range(len(me.materials))):
        return
    keep = [me.materials[i] for i in used]
    remap = np.zeros(max(len(me.materials), 1), np.int32)
    for new, old in enumerate(used):
        remap[old] = new
    me.materials.clear()
    for m in keep:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", remap[idx])


def surfaces(o):
    """Material names an object's faces use, in slot order (one draw call each)."""
    me = o.data
    idx = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("material_index", idx)
    return [me.materials[i].name for i in sorted(set(idx.tolist())) if i < len(me.materials) and me.materials[i]]


def triangles(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


# ------------------------------------------------------------------------------------------------ deterministic JSON


def number(x, places):
    """A float rounded to `places` decimals, without -0."""
    v = round(float(x), places)
    return 0.0 if v == 0 else v


def vec(v, places=4):
    return [number(c, places) for c in v]


def _dump(value):
    return json.dumps(value, ensure_ascii=False, separators=(", ", ": "))


def write_json(path, data, inline=()):
    """Writes `data` (a dict) deterministically: one top-level key per line, lists of records one record per line,
    numbers as given (round them first). Keys listed in `inline` stay on one line whatever they hold."""
    out = ["{"]
    keys = list(data)
    for k, key in enumerate(keys):
        value = data[key]
        comma = "," if k < len(keys) - 1 else ""
        if isinstance(value, list) and value and isinstance(value[0], (dict, list)) and key not in inline:
            out.append(f'  "{key}": [')
            for i, item in enumerate(value):
                out.append("    " + _dump(item) + ("," if i < len(value) - 1 else ""))
            out.append("  ]" + comma)
        elif isinstance(value, dict) and value and key not in inline and any(
                isinstance(v, (dict, list)) for v in value.values()):
            out.append(f'  "{key}": {{')
            inner = list(value)
            for i, name in enumerate(inner):
                out.append(f"    {_dump(name)}: " + _dump(value[name]) + ("," if i < len(inner) - 1 else ""))
            out.append("  }" + comma)
        else:
            out.append(f'  "{key}": ' + _dump(value) + comma)
    out.append("}")
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    log(f"wrote {path.name} ({path.stat().st_size / 1e3:.0f} kB)")
    return path


def linear_to_srgb(c):
    c = max(0.0, float(c))
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def srgb_to_linear_array(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def batch_remove(ids):
    ids = [i for i in ids if i is not None]
    if ids:
        bpy.data.batch_remove(ids)
