"""Helpers for large assembled models (the Iron Wake, docs/IRON_WAKE.md §9): contact occlusion baked per vertex by
rays, carried between levels of detail, faces seen from outside, material slots, and deterministic JSON.

The occlusion is the monument kit's (art/monuments/monkit/builder.py): rays over the hemisphere of each vertex, a hit
weighted by (1 - (d / distance)^2), and a hit on a face seen from behind walked through, so a vertex buried in a
neighbouring solid (a bolt in a plate, a rib in a wall) reads the geometry outside that solid rather than black.
"""
import json
import math
from pathlib import Path

import bmesh
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


def outside_faces(objects, blockers, directions=48, reach=500.0, corners=False):
    """For each object, a bool per polygon: True when a ray from the face's centre over its front hemisphere escapes
    `blockers` (a Triangles) to `reach`, i.e. the face can be seen from outside. `corners`: from points half-way to
    each corner too, so a big face seen only in part counts as seen."""
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
            centre = mw @ p.center
            points = [centre]
            if corners:
                points += [(centre + mw @ me.vertices[v].co) * 0.5 for v in p.vertices]
            t = n.orthogonal().normalized()
            b = n.cross(t)
            for q in points:
                c = q + n * 0.01
                for dx, dy, dz in dirs.tolist():
                    d = t * dx + b * dy + n * dz
                    if rc(c, d, reach)[0] is None:
                        seen[k] = True
                        break
                if seen[k]:
                    break
        result[o] = seen
    return result


# ------------------------------------------------------------------------------------------------ closed, outward shells
#
# Godot draws a material's front faces only (cull back), and a glTF's front faces are its counter-clockwise ones:
# Blender's own winding. Blender's renders draw both sides, so a piece wound inside out, or a single skin seen from
# behind, looks whole there and is a hole in the game. Every piece of a model is therefore made a closed solid facing
# out before it is exported (repair_shells), and the import looks for back faces from all round (backfaces_seen).


def face_islands(bm):
    """The face islands of a bmesh (faces joined by shared edges), each a list of faces, in face order."""
    bm.faces.ensure_lookup_table()
    seen = np.zeros(len(bm.faces), bool)
    out = []
    for f in bm.faces:
        if seen[f.index]:
            continue
        stack, comp = [f], []
        seen[f.index] = True
        while stack:
            x = stack.pop()
            comp.append(x)
            for e in x.edges:
                for y in e.link_faces:
                    if not seen[y.index]:
                        seen[y.index] = True
                        stack.append(y)
        comp.sort(key=lambda f: f.index)
        out.append(comp)
    return out


def island_facts(comp):
    """(open edges, edges wound the same way by both their faces, signed volume) of a face island."""
    inside = set(comp)
    edges = {e for f in comp for e in f.edges}
    open_edges = [e for e in edges if sum(1 for x in e.link_faces if x in inside) == 1]
    same = 0
    for e in edges:
        loops = [lp for lp in e.link_loops if lp.face in inside]
        if len(loops) == 2 and loops[0].vert == loops[1].vert:
            same += 1
    vol = 0.0
    for f in comp:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1):
            vol += vs[0].dot(vs[i].cross(vs[i + 1]))
    return open_edges, same, vol / 6.0


def _escapes(tree, comp, sign, rays=8, reach=2.0, faces=12):
    """How many rays leave from the sides of an island's biggest faces that its normals (sign 1) or their backs
    (sign -1) point to, without meeting `tree` within `reach`."""
    dirs = hemisphere(rays).tolist()
    count = 0
    for f in sorted(comp, key=lambda f: -f.calc_area())[:faces]:
        n = f.normal * sign
        if n.length < 0.5:
            continue
        t = n.orthogonal().normalized()
        b = n.cross(t)
        c = f.calc_center_median() + n * 0.0005
        for dx, dy, dz in dirs:
            if tree.ray_cast(c, t * dx + b * dy + n * dz, reach)[0] is None:
                count += 1
    return count


def _island_tree(comp):
    from mathutils.bvhtree import BVHTree
    index = {}
    verts, polys = [], []
    for f in comp:
        ring = []
        for v in f.verts:
            if v not in index:
                index[v] = len(verts)
                verts.append(v.co.copy())
            ring.append(index[v])
        polys.append(ring)
    return BVHTree.FromPolygons(verts, polys, epsilon=0.0)


def repair_shells(o, thickness=0.008):
    """Makes every piece of mesh object `o` a closed solid facing out, as Godot needs (see above):
    - a closed island is wound one way (Blender's recalculation, where its faces disagree) and turned outside in when
      rays get out from the backs of its faces rather than from their fronts: alone, a closed solid lets nothing out
      from inside, so this holds for pieces that cross themselves too, where the sign of the volume does not (a part
      mirrored without reversing its faces is the usual cause);
    - an open island (a single skin: a panel, a jacket sector) is wound so that its front faces the open side, the side
      rays leave from among the object's other pieces, and is given a back skin `thickness` behind it, joined to it
      round its open edges.
    Shading follows: a reversed face keeps its custom normals turned to its new front, new faces are flat. Returns
    {"reversed": islands, "rewound": islands, "closed": islands, "faces": faces added}."""
    from mathutils.bvhtree import BVHTree
    me = o.data
    n_loops = len(me.loops)
    cn = np.empty(n_loops * 3, np.float64)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    loop_vert = np.empty(n_loops, np.int32)
    me.loops.foreach_get("vertex_index", loop_vert)
    starts = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("loop_start", starts)
    counts = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("loop_total", counts)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    origin = bm.faces.layers.int.new("repair_origin")
    tag = bm.faces.layers.int.new("repair_tag")      # 0 untouched, 1 reversed, 2 new
    for f in bm.faces:
        f[origin] = f.index
    stats = {"reversed": 0, "rewound": 0, "closed": 0, "faces": 0}
    tree = None
    for comp in face_islands(bm):
        open_edges, same, vol = island_facts(comp)
        before = [f.normal.copy() for f in comp]
        if same:
            bmesh.ops.recalc_face_normals(bm, faces=comp)
            for f in comp:
                f.normal_update()
        if not open_edges:
            own = _island_tree(comp)
            if _escapes(own, comp, -1, rays=6, faces=8) > _escapes(own, comp, 1, rays=6, faces=8):
                bmesh.ops.reverse_faces(bm, faces=comp)
                for f in comp:
                    f.normal_update()
            changed = [f for f, n in zip(comp, before) if f.normal.dot(n) < 0]
            for f in changed:
                f[tag] = 1
            if changed:
                stats["rewound" if same else "reversed"] += 1
            continue
        # A single skin: its front where rays get out among the object's pieces, then a back skin behind it.
        if tree is None:
            tree = BVHTree.FromBMesh(bm)
        if _escapes(tree, comp, -1) > _escapes(tree, comp, 1):
            bmesh.ops.reverse_faces(bm, faces=comp)
            for f in comp:
                f.normal_update()
        for f, n in zip(comp, before):
            if f.normal.dot(n) < 0:
                f[tag] = 1
        added = _back_skin(bm, comp, open_edges, thickness, tag)
        stats["closed"] += 1
        stats["faces"] += added
    if not any(stats.values()):
        bm.free()
        return stats
    bm.to_mesh(me)
    bm.free()
    # Normals: the snapshot's by (original face, vertex); reversed faces turned to their front; new faces flat.
    snap = {}
    for p in range(len(starts)):
        for li in range(starts[p], starts[p] + counts[p]):
            snap[(p, int(loop_vert[li]))] = cn[li]
    orig = me.attributes["repair_origin"].data
    tags = me.attributes["repair_tag"].data
    out = np.zeros((len(me.loops), 3), np.float64)
    for p in me.polygons:
        fn = np.array(p.normal)
        t = tags[p.index].value
        src = orig[p.index].value
        for li in p.loop_indices:
            v = me.loops[li].vertex_index
            if t == 2:
                out[li] = fn
                continue
            c = snap.get((src, v))
            if c is None:
                out[li] = fn
            elif t == 1 and float(np.dot(c, fn)) < 0:
                out[li] = -c
            else:
                out[li] = c
    me.attributes.remove(me.attributes["repair_origin"])
    me.attributes.remove(me.attributes["repair_tag"])
    me.normals_split_custom_set(out.tolist())
    me.update()
    return stats


def _back_skin(bm, comp, open_edges, thickness, tag):
    """A back skin `thickness` behind an open island's faces (against their normals), joined to them round the open
    edges: the island becomes a closed plate. Returns the faces added."""
    verts = sorted({v for f in comp for v in f.verts}, key=lambda v: v.index)
    inside = set(comp)
    offset = {}
    for v in verts:
        n = Vector((0.0, 0.0, 0.0))
        for f in v.link_faces:
            if f in inside:
                n += f.normal * f.calc_area()
        offset[v] = n.normalized() if n.length > 1e-12 else Vector((0.0, 0.0, 0.0))
    twin = {v: bm.verts.new(v.co - offset[v] * thickness) for v in verts}
    made = 0
    for f in comp:
        ring = [twin[v] for v in f.verts][::-1]
        try:
            g = bm.faces.new(ring, f)
        except ValueError:
            continue
        g[tag] = 2
        made += 1
    for e in open_edges:
        f = next(x for x in e.link_faces if x in inside)
        lp = next(lp for lp in f.loops if lp.edge == e)
        a, b = lp.vert, lp.link_loop_next.vert          # the face runs a -> b along this edge
        try:
            g = bm.faces.new([b, a, twin[a], twin[b]], f)
        except ValueError:
            continue
        g[tag] = 2
        made += 1
    bm.faces.index_update()
    bm.verts.index_update()
    return made


def split_twisted(o, tolerance=0.0002):
    """Splits every face of `o` that is not flat (a vertex more than `tolerance` off its plane) into triangles that
    bulge along its normal. A twisted quad split along its other diagonal is a surface on the face's inner side, and
    the two sides of a thin plate (a window pane) split that way cross, each showing its back through the other;
    split outward, they move apart. A face is fanned from the corner that leaves each next corner under the triangle
    before it (for a quad: the diagonal leaving the fourth corner under the first triangle). Custom normals stay the
    corners'. Returns the faces split."""
    me = o.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    bad = []
    for p in me.polygons:
        if p.loop_total < 4:
            continue
        pts = co[list(p.vertices)]
        if np.abs((pts - pts.mean(0)) @ np.array(p.normal)).max() > tolerance:
            bad.append(p.index)
    if not bad:
        return 0
    cn = np.empty(len(me.loops) * 3)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    keep = {}
    for i in bad:
        p = me.polygons[i]
        for li in p.loop_indices:
            keep[(i, me.loops[li].vertex_index)] = cn[li]
    loops_before = cn
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    origin = bm.faces.layers.int.new("split_origin")
    for f in bm.faces:
        f[origin] = f.index
    uv_layers = list(bm.loops.layers.uv.values())
    for f in [bm.faces[i] for i in bad]:
        vs = list(f.verts)
        best, score = 0, None
        for s in range(len(vs)):
            ring = vs[s:] + vs[:s]
            sc = 0.0
            for k in range(1, len(ring) - 2):
                a, b, c = ring[0].co, ring[k].co, ring[k + 1].co
                tn = (b - a).cross(c - a)
                if tn.length > 1e-12:
                    sc += max(0.0, tn.normalized().dot(ring[k + 2].co - a))
            if score is None or sc < score - 1e-12:
                best, score = s, sc
        ring = vs[best:] + vs[:best]
        mat, smooth, src = f.material_index, f.smooth, f[origin]
        uvs = {lp.vert: [lp[layer].uv.copy() for layer in uv_layers] for lp in f.loops}
        bm.faces.remove(f)
        for k in range(1, len(ring) - 1):
            try:
                g = bm.faces.new((ring[0], ring[k], ring[k + 1]))
            except ValueError:
                continue
            g.material_index, g.smooth = mat, smooth
            g[origin] = src
            for lp in g.loops:
                for layer, uv in zip(uv_layers, uvs[lp.vert]):
                    lp[layer].uv = uv
    # Every face's corners keep their normals: unsplit faces by their own index, split ones by (face, vertex).
    starts = {}
    for p in me.polygons:
        starts[p.index] = (p.loop_start, list(p.vertices))
    bm.to_mesh(me)
    bm.free()
    orig = me.attributes["split_origin"].data
    split = set(bad)
    out = np.zeros((len(me.loops), 3))
    for p in me.polygons:
        src = orig[p.index].value
        if src in split:
            for li in p.loop_indices:
                c = keep.get((src, me.loops[li].vertex_index))
                out[li] = c if c is not None else np.array(p.normal)
        else:
            start, verts = starts[src]
            for li in p.loop_indices:
                v = me.loops[li].vertex_index
                out[li] = loops_before[start + verts.index(v)]
    me.attributes.remove(me.attributes["split_origin"])
    me.normals_split_custom_set(out.tolist())
    me.update()
    return len(bad)


def fix_folds(o, max_fold=30.0):
    """Turns the diagonal of every quad that is two triangles folded inward. Two triangles that share their longest
    edge and lie within `max_fold` degrees of each other are a quad split along a diagonal; when the far corner of one
    stands above the other (along its normal), the quad was split along its inner diagonal, and the two sides of a thin
    plate split so (the bridge's port window, mirrored from the starboard one) cross each other: each shows its back
    through the other, as holes from both sides. Split along the other diagonal, the quad bulges out and the sides
    move apart. Custom normals and UVs stay the corners'. Returns the quads turned."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    origin = bm.faces.layers.int.new("fold_origin")
    other = bm.faces.layers.int.new("fold_other")
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        f[origin] = f.index
        f[other] = -1
    bm.normal_update()
    cos_max = math.cos(math.radians(max_fold))
    turns = []
    used = set()
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        t1, t2 = e.link_faces
        if len(t1.verts) != 3 or len(t2.verts) != 3 or t1 in used or t2 in used:
            continue
        if t1.normal.dot(t2.normal) < cos_max:
            continue
        length = e.calc_length()
        if any(x.calc_length() > length + 1e-9 for x in list(t1.edges) + list(t2.edges)):
            continue
        lp = next(x for x in t1.loops if x.edge == e)
        u, w = lp.vert, lp.link_loop_next.vert              # t1 runs u -> w
        p = next(v for v in t1.verts if v not in (u, w))
        q = next(v for v in t2.verts if v not in (u, w))
        lift = t1.normal.dot(q.co - u.co)
        if lift <= 2e-4:
            continue                                        # flat or bulging out already
        # The other diagonal must lie inside the quad: u and w on either side of p-q.
        n = (t1.normal + t2.normal).normalized()
        side_u = (q.co - p.co).cross(u.co - p.co).dot(n)
        side_w = (q.co - p.co).cross(w.co - p.co).dot(n)
        if side_u * side_w >= 0:
            continue
        turns.append((t1, t2, u, w, p, q))
        used.update((t1, t2))
    if not turns:
        bm.free()
        return 0
    cn = np.empty(len(me.loops) * 3)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    snap = {}
    for poly in me.polygons:
        for li in poly.loop_indices:
            snap[(poly.index, me.loops[li].vertex_index)] = cn[li]
    uv_layers = list(bm.loops.layers.uv.values())
    done = 0
    for t1, t2, u, w, p, q in turns:
        # The other diagonal must not be an edge already (a thin shape folded on itself): turning would double a face.
        if bm.edges.get((p, q)) is not None:
            continue
        done += 1
        uv = {}
        for t in (t2, t1):
            for lp in t.loops:
                uv[lp.vert] = [lp[layer].uv.copy() for layer in uv_layers]
        mat, smooth, a, b = t1.material_index, t1.smooth, t1[origin], t2[origin]
        bm.faces.remove(t1)
        bm.faces.remove(t2)
        loose = bm.edges.get((u, w))
        if loose is not None and not loose.link_faces:
            bm.edges.remove(loose)
        for tri in ((u, q, p), (q, w, p)):
            g = bm.faces.new(tri)
            g.material_index, g.smooth = mat, smooth
            g[origin], g[other] = a, b
            for lp in g.loops:
                for layer, value in zip(uv_layers, uv[lp.vert]):
                    lp[layer].uv = value
    bm.to_mesh(me)
    bm.free()
    org = me.attributes["fold_origin"].data
    oth = me.attributes["fold_other"].data
    out = np.zeros((len(me.loops), 3))
    for poly in me.polygons:
        a, b = org[poly.index].value, oth[poly.index].value
        for li in poly.loop_indices:
            v = me.loops[li].vertex_index
            c = snap.get((a, v))
            if c is None and b >= 0:
                c = snap.get((b, v))
            out[li] = c if c is not None else np.array(poly.normal)
    me.attributes.remove(me.attributes["fold_origin"])
    me.attributes.remove(me.attributes["fold_other"])
    me.normals_split_custom_set(out.tolist())
    me.update()
    return done


def shell_report(o):
    """(open islands, islands wound inside out or inconsistently, islands) of a mesh object, by repair_shells' rules:
    what it leaves must be 0, 0."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.normal_update()
    opened = inverted = 0
    islands = face_islands(bm)
    for comp in islands:
        open_edges, same, _ = island_facts(comp)
        if open_edges:
            opened += 1
        elif same:
            inverted += 1
        else:
            own = _island_tree(comp)
            if _escapes(own, comp, -1, rays=6, faces=8) > _escapes(own, comp, 1, rays=6, faces=8):
                inverted += 1
    bm.free()
    return opened, inverted, len(islands)


def backfaces_seen(objects, rooms=(), directions=64, spacing=0.2, room_rays=1200, skip_materials=()):
    """Where back faces are the first thing a ray meets, which Godot would cull, showing what lies behind or the sky:
    parallel rays from `directions` all round the objects on a grid `spacing` apart, and rays all round from points in
    each room (`rooms`: (name, lo, hi) boxes in the objects' frame; points inside solids are skipped). Returns
    {"outside": rays, "outsideBack": n, "outsideSky": n, "inside": rays, "insideBack": n, "worst": [(where, object,
    material, rays, centre)]}."""
    from mathutils.bvhtree import BVHTree
    verts, tris, owner, mats = [], [], [], []
    base = 0
    for o in objects:
        co, _, tv, ms = mesh_arrays(o)
        keep = np.array([m not in skip_materials for m in ms], bool) if len(ms) else np.zeros(0, bool)
        verts.append(co)
        tris.append(tv[keep] + base)
        owner += [o.name] * int(keep.sum())
        mats += [m for m, k in zip(ms, keep) if k]
        base += len(co)
    v = np.concatenate(verts)
    t = np.concatenate(tris)
    nrm = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-20)
    tree = BVHTree.FromPolygons(v.tolist(), t.tolist(), epsilon=0.0)
    found = {}

    def march(p, d, reach):
        """(first triangle, shown triangle or -1 for the sky) when the first hit is a back face, else None."""
        dn = np.array(d)
        hit = tree.ray_cast(p, d, reach)
        if hit[0] is None or float(nrm[hit[2]] @ dn) <= 0:
            return None
        first, q, left = hit[2], hit[0], reach - hit[3]
        for _ in range(64):
            h = tree.ray_cast(q + d * 1e-4, d, left)
            if h[0] is None:
                return first, -1
            if float(nrm[h[2]] @ dn) <= 0:
                return first, h[2]
            left -= h[3] + 1e-4
            q = h[0]
        return first, -1

    def note(where, tri, point):
        key = (where, owner[tri], mats[tri])
        rec = found.setdefault(key, [0, np.zeros(3)])
        rec[0] += 1
        rec[1] += np.asarray(point)
    lo, hi = v.min(0), v.max(0)
    c = (lo + hi) / 2
    R = float(np.linalg.norm(hi - lo)) / 2 + 1.0
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    out_rays = out_back = out_sky = 0
    for di, d in enumerate(sphere(directions)):
        u = np.cross(d, [0.0, 0.0, 1.0] if abs(d[2]) < 0.9 else [1.0, 0.0, 0.0])
        u /= np.linalg.norm(u)
        w = np.cross(d, u)
        pu, pw = (corners - c) @ u, (corners - c) @ w
        dv = Vector(d.tolist())
        # A grid offset by a fraction of a step per direction, so the directions together sample finer.
        k = di % 7
        for a in np.arange(pu.min() + spacing * k / 7, pu.max() + spacing, spacing):
            for b in np.arange(pw.min() + spacing * ((k * 3) % 7) / 7, pw.max() + spacing, spacing):
                p = Vector((c - d * R + u * a + w * b).tolist())
                hit = tree.ray_cast(p, dv, 2 * R)
                if hit[0] is None:
                    continue
                out_rays += 1
                r = march(p, dv, 2 * R)
                if r is not None:
                    out_back += 1
                    out_sky += r[1] < 0
                    note("outside", r[0], hit[0])
    in_rays = in_back = 0
    dirs = [Vector(x) for x in sphere(room_rays).tolist()]
    probes = [Vector(x) for x in sphere(26).tolist()]
    down = np.array([0.0, 0.0, -1.0])
    for name, rlo, rhi in rooms:
        for fx in (0.2, 0.5, 0.8):
            for fy in (0.2, 0.5, 0.8):
                for fz in (0.35, 0.7):
                    p = Vector([rlo[i] + (rhi[i] - rlo[i]) * f for i, f in enumerate((fx, fy, fz))])
                    h = tree.ray_cast(p, Vector(down.tolist()), 40.0)
                    if h[0] is None or float(nrm[h[2]] @ down) > 0:
                        continue          # under nothing, or inside a solid
                    if tree.find_nearest(p)[3] < 0.35:
                        continue          # in or under furniture (a dais, a console), not in the room's air
                    # Inside a solid whose bottom lies on the floor: most ways out meet a back face first.
                    probe = [march(p, d, 120.0) for d in probes]
                    if sum(1 for r in probe if r is not None) >= 0.6 * len(probes):
                        continue
                    for d in dirs:
                        hit = tree.ray_cast(p, d, 120.0)
                        if hit[0] is None:
                            continue
                        in_rays += 1
                        r = march(p, d, 120.0)
                        if r is not None:
                            in_back += 1
                            note(name, r[0], hit[0])
    worst = sorted(((w, o, m, n, [round(float(x), 2) for x in s / n]) for (w, o, m), (n, s) in found.items()),
                   key=lambda r: -r[3])
    return {"outside": out_rays, "outsideBack": out_back, "outsideSky": out_sky, "inside": in_rays,
            "insideBack": in_back, "worst": worst[:24]}


def _in_box(p, box, margin=0.0):
    return all(box[1][i] - margin <= p[i] <= box[2][i] + margin for i in range(3))


def _crosses(a, b, box):
    """Whether the segment a-b passes through the box (name, lo, hi): the slab test."""
    t0, t1 = 0.0, 1.0
    for i in range(3):
        d = b[i] - a[i]
        if abs(d) < 1e-12:
            if a[i] < box[1][i] or a[i] > box[2][i]:
                return False
            continue
        u, v = (box[1][i] - a[i]) / d, (box[2][i] - a[i]) / d
        t0, t1 = max(t0, min(u, v)), min(t1, max(u, v))
        if t0 > t1:
            return False
    return True


def inside_seen(objects, inside=(), air=(), allow=(), glass=(), directions=64, spacing=0.2, axis_spacing=0.1):
    """Openings that show the inside of a hull from outside, which back faces do not reveal (IRON_WAKE.md §9.1 "As
    built: the bow and the stern closed"): parallel rays from `directions` all round on a grid `spacing` apart, and
    from the six sides (+-X, +-Y, below and above) on a grid `axis_spacing` apart, whose first hit is a triangle of
    an object named in `inside` (the hold's mesh, wherever it is met) or a surface whose air side (a centimetre back
    along the ray) lies in one of the `air` boxes ((name, lo, hi): the spaces inside the hull, in the objects'
    frame); and a ray that meets nothing at all but crosses one of those spaces (through a crack). A ray that passed
    through one of the `allow` boxes (a doorway, an airlock) on its way is not counted, nor one that meets a pane of
    `glass` (material names) first: a window shows the inside on purpose. Returns {"rays": n, "inside": n, "worst":
    [(space, object, material, rays, centre)] by place (2 m cells), "examples": [(origin, direction)] one ray of
    each}."""
    from mathutils.bvhtree import BVHTree
    verts, tris, owner, mats = [], [], [], []
    base = 0
    for o in objects:
        co, _, tv, ms = mesh_arrays(o)
        verts.append(co)
        tris.append(tv + base)
        owner += [o.name] * len(tv)
        mats += ms
        base += len(co)
    v = np.concatenate(verts)
    t = np.concatenate(tris)
    tree = BVHTree.FromPolygons(v.tolist(), t.tolist(), epsilon=0.0)
    inside = set(inside)
    lo, hi = v.min(0), v.max(0)
    c = (lo + hi) / 2
    R = float(np.linalg.norm(hi - lo)) / 2 + 1.0
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    axes = np.array([[1.0, 0, 0], [-1.0, 0, 0], [0, 1.0, 0], [0, -1.0, 0], [0, 0, 1.0], [0, 0, -1.0]])
    sets = [(d, spacing) for d in sphere(directions)] + [(d, axis_spacing) for d in axes]
    found = {}
    rays = seen = 0
    for di, (d, step) in enumerate(sets):
        u = np.cross(d, [0.0, 0.0, 1.0] if abs(d[2]) < 0.9 else [1.0, 0.0, 0.0])
        u /= np.linalg.norm(u)
        w = np.cross(d, u)
        pu, pw = (corners - c) @ u, (corners - c) @ w
        dv = Vector(d.tolist())
        k = di % 7
        for a in np.arange(pu.min() + step * k / 7, pu.max() + step, step):
            for b in np.arange(pw.min() + step * ((k * 3) % 7) / 7, pw.max() + step, step):
                p = Vector((c - d * R + u * a + w * b).tolist())
                hit = tree.ray_cast(p, dv, 2 * R)
                if hit[0] is None:
                    # A ray that meets nothing at all but crosses a space inside went through a crack, unless rays
                    # 2 mm to each side meet something: then it slipped along an edge two faces share, which a
                    # rasteriser closes (a ray cast is not watertight there).
                    end = p + dv * (2 * R)
                    space = next((n for n, *box in air if _crosses(p, end, (n, *box))), None)
                    if space is None or any(_crosses(p, end, box) for box in allow):
                        continue
                    if any(tree.ray_cast(p + Vector(o.tolist()), dv, 2 * R)[0] is not None
                           for o in (u * 0.002, -u * 0.002, w * 0.002, -w * 0.002)):
                        continue
                    rays += 1
                    seen += 1
                    box = next(box for n, *box in air if n == space)
                    mid = (Vector(box[0]) + Vector(box[1])) / 2
                    q = p + dv * dv.dot(mid - p)
                    cell = tuple(int(math.floor(x / 2.0)) for x in q)
                    rec = found.setdefault((space + " (through)", "-", "-", cell),
                                           [0, np.zeros(3), (tuple(p), tuple(dv))])
                    rec[0] += 1
                    rec[1] += np.asarray(q)
                    continue
                rays += 1
                tri = hit[2]
                if mats[tri] in glass:
                    continue
                air_side = hit[0] - dv * 0.01
                space = next((n for n, *box in air if _in_box(air_side, (n, *box))), None)
                if space is None and owner[tri] in inside:
                    space = "hold"
                if space is None or any(_crosses(p, hit[0], box) for box in allow):
                    continue
                seen += 1
                # By place (cells of 2 m), so separate openings are reported apart.
                cell = tuple(int(math.floor(x / 2.0)) for x in hit[0])
                rec = found.setdefault((space, owner[tri], mats[tri], cell), [0, np.zeros(3), (tuple(p), tuple(dv))])
                rec[0] += 1
                rec[1] += np.asarray(hit[0])
    worst = sorted(((s, o, m, n, [round(float(x), 2) for x in q / n], ray)
                    for (s, o, m, _), (n, q, ray) in found.items()), key=lambda r: -r[3])
    return {"rays": rays, "inside": seen, "worst": [w[:5] for w in worst[:24]],
            "examples": [w[5] for w in worst[:24]]}


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
