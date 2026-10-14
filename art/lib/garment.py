"""Garment geometry and texture helpers: shells cut from the body's surface, shaped, skinned and painted.

A garment starts as a copy of the body faces it covers (so it has the body's weights, UVs and regions), is cut with
planes for clean hems, smoothed so no anatomy shows through, pushed off the skin by its layer's offset and given a
turned-in hem. Textures are painted in UV space from each texel's 3D position (found by rasterising the mesh's UV
triangles), so seams, bands, laces and dirt can follow the body.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Vector

import hollow_art as H


# ---------------------------------------------------------------- geometry

def extract(body, name, keep_face):
    """A new object with the body faces for which keep_face(poly, region) is true (weights, UVs, regions kept)."""
    obj = H.duplicate(body, name)
    obj.hide_render = False
    me = obj.data
    regions = [0] * len(me.polygons)
    me.attributes["region"].data.foreach_get("value", regions)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    doomed = [f for f in bm.faces if not keep_face(f, regions[f.index])]
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me)
    bm.free()
    obj.data.materials.clear()
    return obj


def dilate(body, keep, rings):
    """Grows a set of face indices of the body by whole rings of neighbouring faces."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.faces.ensure_lookup_table()
    current = set(keep)
    for _ in range(rings):
        grown = set(current)
        for i in current:
            for v in bm.faces[i].verts:
                for f in v.link_faces:
                    grown.add(f.index)
        current = grown
    bm.free()
    return current


def cut(obj, point, normal, keep_side=1, select=None):
    """Bisects the mesh with a plane and deletes everything on the other side (keep_side 1 = along the normal).
    select(v) limits the cut to faces whose vertices all pass (e.g. only the torso, not the arms)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    faces = [f for f in bm.faces if select is None or all(select(v.co) for v in f.verts)]
    geom = list({v for f in faces for v in f.verts}) + list({e for f in faces for e in f.edges}) + faces
    p, n = Vector(point), Vector(normal).normalized()
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=p, plane_no=n)
    doomed = [f for f in bm.faces
              if (select is None or all(select(v.co) for v in f.verts))
              and (f.calc_center_median() - p).dot(n) * keep_side < 0]
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bm.to_mesh(me)
    bm.free()


def cut_height(obj, height, select=None):
    """Cuts along the curved surface z = height(x, y) and deletes what is above it (select(co) limits the cut, as in
    cut()). The mesh is sheared so the surface becomes the plane z = 0, bisected there, and sheared back, so the new
    edge lies exactly on the surface."""
    me = obj.data
    for v in me.vertices:
        v.co.z -= float(height(v.co.x, v.co.y))
    flat = None if select is None else (lambda co: select(Vector((co.x, co.y, co.z + float(height(co.x, co.y))))))
    cut(obj, (0.0, 0.0, 0.0), (0.0, 0.0, -1.0), select=flat)
    for v in me.vertices:
        v.co.z += float(height(v.co.x, v.co.y))


def round_opening(obj, centre, rx, ry, low, height, blend=0.6):
    """Cuts a clean round opening (a neckline): deletes the faces inside a vertical elliptic cylinder above `low`, then
    moves the new edge onto the ellipse, at height(theta) (blended with its own height)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    cx, cy = centre

    def inside(p):
        return ((p.x - cx) / rx) ** 2 + ((p.y - cy) / ry) ** 2 < 1.0 and p.z > low

    doomed = [f for f in bm.faces if inside(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    for v in boundary_verts(bm):
        p = v.co
        e = math.hypot((p.x - cx) / rx, (p.y - cy) / ry)
        if p.z < low - 0.03 or e > 1.7:
            continue
        theta = math.atan2((p.y - cy) / ry, (p.x - cx) / rx)
        v.co = Vector((cx + rx * math.cos(theta), cy + ry * math.sin(theta), p.z + (height(theta) - p.z) * blend))
    bm.to_mesh(me)
    bm.free()


def collar_band(obj, body, centre, on_neckline, rise, steps=2, lean=0.004, clearance=0.009):
    """A band standing up from a neckline: the open edges on it (on_neckline(co)) are extruded `rise` metres up in
    `steps` rows, each row drawn in towards the neck by `lean` but kept at least `clearance` off the skin, so it hugs
    the neck without touching it. The new faces get real UVs, so the band can be textured."""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get(), deform=False)
    mpu = metres_per_uv(obj)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    cx, cy = centre
    edges = [e for e in bm.edges if e.is_boundary and all(on_neckline(v.co) for v in e.verts)]
    for _ in range(steps):
        result = bmesh.ops.extrude_edge_only(bm, edges=edges)
        new_verts = [g for g in result["geom"] if isinstance(g, bmesh.types.BMVert)]
        for v in new_verts:
            p = v.co + Vector((0.0, 0.0, rise / steps))
            towards = Vector((cx - p.x, cy - p.y, 0.0))
            if towards.length > 1e-6:
                p += towards.normalized() * lean
            loc, normal, _, _ = tree.find_nearest(p)
            if loc is not None:
                out = (p - loc).dot(normal)
                if out < clearance:
                    p += normal * (clearance - out)
            v.co = p
        fresh = set(new_verts)
        extend_uvs(bm, [g for g in result["geom"] if isinstance(g, bmesh.types.BMFace)], fresh, mpu)
        edges = [g for g in result["geom"] if isinstance(g, bmesh.types.BMEdge) and all(x in fresh for x in g.verts)]
    bm.to_mesh(me)
    bm.free()


def extend_uvs(bm, faces, new_verts, mpu):
    """Gives faces extruded from an open edge real UVs: each new vertex moves off its source vertex's UV, outwards
    from the island and perpendicular to its edge, by its 3D distance (so the band is textured, not a smear)."""
    uv = bm.loops.layers.uv.active
    if uv is None:
        return

    def key(loop):
        return loop.vert.index, round(loop[uv].uv.x, 5), round(loop[uv].uv.y, 5)

    bm.verts.index_update()
    outward = {}
    for f in faces:
        old = [lp for lp in f.loops if lp.vert not in new_verts]
        if len(old) != 2:
            continue
        edge = next((e for e in old[0].vert.link_edges if e.other_vert(old[0].vert) is old[1].vert), None)
        inner = [g for g in edge.link_faces if g is not f and g not in faces] if edge else []
        if not inner:
            continue
        g = inner[0]
        corners = {lp.vert: lp[uv].uv.copy() for lp in g.loops}
        if old[0].vert not in corners or old[1].vert not in corners:
            continue
        centre = sum((lp[uv].uv for lp in g.loops), Vector((0.0, 0.0))) / len(g.loops)
        u0, u1 = corners[old[0].vert], corners[old[1].vert]
        t = u1 - u0
        if t.length < 1e-9:
            continue
        perp = Vector((-t.y, t.x)).normalized()
        if perp.dot((u0 + u1) / 2 - centre) < 0:
            perp = -perp
        for lp in old:
            k = key(lp)
            outward[k] = outward.get(k, Vector((0.0, 0.0))) + perp
    for f in faces:
        for lp in f.loops:
            if lp.vert not in new_verts:
                continue
            src = lp.link_loop_next if lp.link_loop_next.vert not in new_verts else lp.link_loop_prev
            d = outward.get(key(src))
            if d is None or d.length < 1e-9:
                continue
            lp[uv].uv = src[uv].uv + d.normalized() * ((lp.vert.co - src.vert.co).length / mpu)


def boundary_verts(bm):
    return {v for e in bm.edges if e.is_boundary for v in e.verts}


def smooth(obj, iterations, factor=0.5, keep_boundary=True, weight=None):
    """Laplacian smoothing (with a Taubin back-step so it does not shrink); weight(v) scales it per vertex."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    edge = boundary_verts(bm) if keep_boundary else set()
    verts = [v for v in bm.verts if v not in edge]
    w = {v: (weight(v.co) if weight else 1.0) for v in verts}
    for _ in range(iterations):
        for lam in (factor, -factor * 1.04):
            moved = {}
            for v in verts:
                ring = [e.other_vert(v).co for e in v.link_edges]
                if not ring:
                    continue
                avg = sum(ring, Vector()) / len(ring)
                moved[v] = v.co + (avg - v.co) * lam * w[v]
            for v, co in moved.items():
                v.co = co
    bm.to_mesh(me)
    bm.free()


def offset(obj, amount):
    """Pushes every vertex along its smooth normal; amount(co, normal) gives metres per vertex."""
    me = obj.data
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    moves = [(v, v.normal.copy() * amount(v.co.copy(), v.normal.copy())) for v in bm.verts]
    for v, d in moves:
        v.co += d
    bm.to_mesh(me)
    bm.free()


def radial(obj, axis_point, axis_dir, amount):
    """Pushes vertices away from an axis (a limb's bone line); amount(co, t) in metres, t = position along the axis."""
    a = Vector(axis_point)
    d = Vector(axis_dir).normalized()
    for v in obj.data.vertices:
        rel = v.co - a
        t = rel.dot(d)
        out = rel - d * t
        if out.length > 1e-6:
            v.co += out.normalized() * amount(v.co.copy(), t)


def hem(obj, depth, bulge=0.0, select=None):
    """Turns open edges inwards by `depth` (a visible thickness at cuffs and hems) and can roll the edge out.
    select(co) limits it to the open edges whose vertices pass."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    edges = [e for e in bm.edges if e.is_boundary and (select is None or all(select(v.co) for v in e.verts))]
    if not edges:
        bm.free()
        return
    normals = {v: v.normal.copy() for e in edges for v in e.verts}
    if bulge:
        for v, n in normals.items():
            v.co += n * bulge
    result = bmesh.ops.extrude_edge_only(bm, edges=edges)
    new_verts = [g for g in result["geom"] if isinstance(g, bmesh.types.BMVert)]
    for v in new_verts:
        # The new vertex sits on the old one; find which, and move it under the surface.
        src = min(normals.keys(), key=lambda o: (o.co - v.co).length_squared)
        v.co = v.co - normals[src] * depth
    bm.to_mesh(me)
    bm.free()


def solidify(obj, thickness):
    mod = obj.modifiers.new("solid", 'SOLIDIFY')
    mod.thickness = thickness
    mod.offset = -1.0
    mod.use_rim = True
    mod.use_even_offset = True
    H.apply_modifier(obj, mod.name)


def ripples(obj, origin, axis, t0, t1, wavelength, depth, twist=0.0, sharp=2.0, mask=None):
    """Folds: ridges around an axis (a limb or the trunk) between t0 and t1 along it, `wavelength` apart, bulging out
    by up to `depth` along the vertex normal. `twist` slants them (radians of phase per radian around the axis)."""
    me = obj.data
    me.update()
    o = Vector(origin)
    d = Vector(axis).normalized()
    a = d.orthogonal().normalized()
    b = d.cross(a)
    for v in me.vertices:
        if mask is not None and not mask(v.co):
            continue
        rel = v.co - o
        t = rel.dot(d)
        if not t0 < t < t1:
            continue
        fade = math.sin(math.pi * (t - t0) / (t1 - t0))
        theta = math.atan2(rel.dot(b), rel.dot(a))
        wave = 0.5 + 0.5 * math.sin(t / wavelength * math.tau + twist * theta + 0.7 * math.sin(theta * 3.0))
        v.co += v.normal * depth * fade * wave ** sharp


def _hull(points):
    """Convex hull of 2D points (monotone chain), counter-clockwise."""
    pts = sorted(set(points))
    if len(pts) < 3:
        return pts

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
    return lower[:-1] + upper[:-1]


def _ray_to_hull(hull, theta):
    """Distance from the origin along angle theta to the hull's boundary (0 if the origin is outside it)."""
    dx, dy = math.cos(theta), math.sin(theta)
    best = 0.0
    n = len(hull)
    for i in range(n):
        (x1, y1), (x2, y2) = hull[i], hull[(i + 1) % n]
        ex, ey = x2 - x1, y2 - y1
        det = dx * (-ey) - dy * (-ex)
        if abs(det) < 1e-12:
            continue
        t = (x1 * (-ey) - y1 * (-ex)) / det
        u = (dx * y1 - dy * x1) / det
        if t > 0 and -1e-6 <= u <= 1 + 1e-6:
            best = max(best, t)
    return best


def envelope(obj, origin, axis, *, t_step=0.01, sectors=48, blur_t=2, blur_theta=2, fill=1.0, drape=None,
             extra=0.0, select=None, reference=None, indices=None, hull=False):
    """Makes cross-sections round and roomy, the way cloth hangs. hull=True makes every cross-section convex first (cloth
    spans hollows such as the cleft between the pecs instead of following them). Around an axis (a limb or the trunk), each vertex is
    pushed out to at least `fill` times the largest radius found in its cell (position along the axis × angle around
    it), after blurring that table; concave places (the spine groove, between the toes, under the pecs) fill in.
    drape=(start_t, taper): from start_t onwards (along the axis) the radius never drops faster than `taper` metres per
    metre, so cloth falls from the widest point above it instead of following the body in. `reference` (a list of
    points) can add the body's own surface to the table. `extra` adds metres everywhere."""
    me = obj.data
    o = Vector(origin)
    d = Vector(axis).normalized()
    a = d.orthogonal().normalized()
    b = d.cross(a)
    verts = [v for v in me.vertices if (select is None or select(v.co)) and (indices is None or v.index in indices)]
    if not verts:
        return
    coords = []
    for co in [v.co for v in verts] + list(reference or []):
        rel = co - o
        t = rel.dot(d)
        x, y = rel.dot(a), rel.dot(b)
        coords.append((t, math.atan2(y, x), math.hypot(x, y)))
    ts = np.array([c[0] for c in coords])
    t0 = ts.min()
    nt = int((ts.max() - t0) / t_step) + 2
    table = np.zeros((nt, sectors))
    for t, th, r in coords:
        i = int((t - t0) / t_step)
        j = int((th + math.pi) / math.tau * sectors) % sectors
        table[i, j] = max(table[i, j], r)
    if hull:
        rows = [[] for _ in range(nt)]
        for t, th, r in coords:
            rows[int((t - t0) / t_step)].append((r * math.cos(th), r * math.sin(th)))
        for i in range(nt):
            near = rows[i] + (rows[i - 1] if i > 0 else []) + (rows[i + 1] if i + 1 < nt else [])
            if len(near) < 3:
                continue
            h = _hull([(round(x, 5), round(y, 5)) for x, y in near])
            if len(h) < 3:
                continue
            for j in range(sectors):
                theta = (j + 0.5) / sectors * math.tau - math.pi
                table[i, j] = max(table[i, j], _ray_to_hull(h, theta))
    # Empty cells: rows are thinner than the mesh's spacing in places, so a cell first takes the same sector a row or
    # three up or down; what is still empty (thin limbs leave whole arcs) is interpolated round the ring between the
    # nearest filled cells, and empty rows copy the nearest filled row. (Taking the larger angular neighbour instead
    # spread the wide sides of the hips round to the small of the back, where the trousers stood out like a shelf.)
    for k in range(1, 4):
        empty = table == 0
        if not empty.any():
            break
        up = np.vstack([np.zeros((k, sectors)), table[:-k]])
        down = np.vstack([table[k:], np.zeros((k, sectors))])
        near = np.where(up > 0, np.where(down > 0, (up + down) / 2, up), down)
        take = empty & (near > 0)
        table[take] = near[take]
    ring = np.arange(sectors)
    for i in range(nt):
        filled = np.nonzero(table[i] > 0)[0]
        if 0 < len(filled) < sectors:
            xp = np.concatenate([filled - sectors, filled, filled + sectors])
            fp = np.tile(table[i, filled], 3)
            table[i] = np.where(table[i] > 0, table[i], np.interp(ring, xp, fp))
    rows_filled = np.nonzero(table.max(axis=1) > 0)[0]
    for i in range(nt):
        if table[i].max() == 0 and len(rows_filled):
            table[i] = table[rows_filled[np.argmin(np.abs(rows_filled - i))]]
    for _ in range(blur_theta):
        table = np.maximum(table, (np.roll(table, 1, axis=1) + np.roll(table, -1, axis=1)) * 0.5)
    for _ in range(blur_t):
        up = np.vstack([table[:1], table[:-1]])
        down = np.vstack([table[1:], table[-1:]])
        table = np.maximum(table, (up + down) * 0.5)
    if drape is not None:
        start, taper = drape
        for i in range(1, nt):
            if t0 + i * t_step > start:
                table[i] = np.maximum(table[i], table[i - 1] - taper * t_step)
    for v in verts:
        rel = v.co - o
        t = rel.dot(d)
        x, y = rel.dot(a), rel.dot(b)
        r = math.hypot(x, y)
        if r < 1e-6:
            continue
        i = min(nt - 1, max(0, int((t - t0) / t_step)))
        j = int((math.atan2(y, x) + math.pi) / math.tau * sectors) % sectors
        want = max(r, fill * table[i, j]) + extra
        scale = want / r
        v.co = o + d * t + a * (x * scale) + b * (y * scale)


def vertices_in_regions(obj, body, regions):
    """Indices of the garment's vertices whose nearest body face is in one of the given regions."""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get(), deform=False)
    body_regions = [0] * len(body.data.polygons)
    body.data.attributes["region"].data.foreach_get("value", body_regions)
    found = set()
    for v in obj.data.vertices:
        _, _, index, _ = tree.find_nearest(v.co)
        if index is not None and body_regions[index] in regions:
            found.add(v.index)
    return found


def smooth_weights(obj, repeat, factor=0.5):
    """Blurs the skin weights over the mesh, so loose cloth moves as a whole instead of creasing where the body's
    weights change quickly (the edge of the chest muscles, say). Then four influences at most, normalised."""
    me = obj.data
    n = len(me.vertices)
    groups = [g for g in obj.vertex_groups if H.is_deform_group(g.name)]
    column = {g.index: k for k, g in enumerate(groups)}
    w = np.zeros((n, len(groups)))
    for v in me.vertices:
        for entry in v.groups:
            k = column.get(entry.group)
            if k is not None:
                w[v.index, k] = entry.weight
    edges = np.array([e.vertices[:] for e in me.edges], np.int64).reshape(-1, 2)
    degree = np.maximum(np.bincount(edges.ravel(), minlength=n), 1).astype(float)
    for _ in range(repeat):
        total = np.zeros_like(w)
        np.add.at(total, edges[:, 0], w[edges[:, 1]])
        np.add.at(total, edges[:, 1], w[edges[:, 0]])
        w = w * (1 - factor) + total / degree[:, None] * factor
    for k, g in enumerate(groups):
        g.remove(list(range(n)))
        for vi in np.nonzero(w[:, k] > 1e-4)[0]:
            g.add([int(vi)], float(w[vi, k]), 'REPLACE')
    H.limit_and_normalise_weights(obj)


def weights_from_body(obj, body):
    """Skin weights from the nearest body surface, then four influences at most."""
    mod = obj.modifiers.new("weights", 'DATA_TRANSFER')
    mod.object = body
    mod.use_vert_data = True
    mod.data_types_verts = {'VGROUP_WEIGHTS'}
    mod.vert_mapping = 'POLYINTERP_NEAREST'
    mod.layers_vgroup_select_src = 'ALL'
    mod.layers_vgroup_select_dst = 'NAME'
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        bpy.ops.object.datalayout_transfer(modifier=mod.name)
    H.apply_modifier(obj, mod.name)
    H.remove_non_deform_groups(obj)
    H.limit_and_normalise_weights(obj)


def regions_from_body(obj, body):
    """Each garment face takes the region of the nearest body face (for hiding inner layers and first person)."""
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get(), deform=False)
    body_regions = [0] * len(body.data.polygons)
    body.data.attributes["region"].data.foreach_get("value", body_regions)
    regions = []
    for p in obj.data.polygons:
        _, _, index, _ = tree.find_nearest(p.center)
        regions.append(body_regions[index] if index is not None else 0)
    H.set_region_uv(obj, regions)
    return regions


def pack_uvs(obj, margin=0.01):
    """Packs the garment's UV islands (from the body's layout) into the full square."""
    H.select_only([obj])
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    obj.data.uv_layers.active_index = 0
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=margin)
    bpy.ops.object.mode_set(mode='OBJECT')


def metres_per_uv(obj):
    me = obj.data
    uv = me.uv_layers[0].data
    area3, area2 = 0.0, 0.0
    for p in me.polygons:
        area3 += p.area
        pts = [uv[li].uv for li in p.loop_indices]
        a = 0.0
        for i in range(len(pts)):
            x1, y1 = pts[i]
            x2, y2 = pts[(i + 1) % len(pts)]
            a += x1 * y2 - x2 * y1
        area2 += abs(a) / 2
    return math.sqrt(area3 / max(area2, 1e-9))


# ---------------------------------------------------------------- textures

def rasterise(obj, size):
    """Per texel of the first UV map: covered mask, 3D position, normal and face index (rows top to bottom)."""
    me = obj.data
    me.calc_loop_triangles()
    uv = me.uv_layers[0].data
    mask = np.zeros((size, size), bool)
    pos = np.zeros((size, size, 3), np.float32)
    nor = np.zeros((size, size, 3), np.float32)
    face = np.full((size, size), -1, np.int32)
    co = np.array([v.co[:] for v in me.vertices], np.float32)
    vn = np.array([v.normal[:] for v in me.vertices], np.float32)
    for tri in me.loop_triangles:
        uvs = np.array([uv[l].uv[:] for l in tri.loops], np.float32) * size
        uvs[:, 1] = size - uvs[:, 1]
        x0, y0 = np.floor(uvs.min(axis=0)).astype(int)
        x1, y1 = np.ceil(uvs.max(axis=0)).astype(int)
        x0, y0 = max(x0 - 1, 0), max(y0 - 1, 0)
        x1, y1 = min(x1 + 1, size - 1), min(y1 + 1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        px = xs + 0.5
        py = ys + 0.5
        (ax, ay), (bx, by), (cx, cy) = uvs
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(det) < 1e-12:
            continue
        w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / det
        w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / det
        w2 = 1 - w0 - w1
        inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
        if not inside.any():
            continue
        idx = list(tri.vertices)
        p = (w0[..., None] * co[idx[0]] + w1[..., None] * co[idx[1]] + w2[..., None] * co[idx[2]])
        n = (w0[..., None] * vn[idx[0]] + w1[..., None] * vn[idx[1]] + w2[..., None] * vn[idx[2]])
        sub = (slice(y0, y1 + 1), slice(x0, x1 + 1))
        m = inside & ~mask[sub] | (inside & (np.minimum(np.minimum(w0, w1), w2) >= 0))
        pos[sub][m] = p[m]
        nor[sub][m] = n[m]
        face[sub][m] = tri.polygon_index
        mask[sub] |= inside
    return mask, pos, nor, face


def bleed(image, mask, steps=8):
    """Extends painted texels outwards so mipmaps never pull in the empty background."""
    img = image.copy()
    m = mask.copy()
    for _ in range(steps):
        grown = m.copy()
        acc = np.zeros_like(img)
        cnt = np.zeros(m.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sm = np.roll(m, (dy, dx), axis=(0, 1))
            si = np.roll(img, (dy, dx), axis=(0, 1))
            add = sm & ~m
            acc[add] += si[add]
            cnt[add] += 1
            grown |= sm
        new = grown & ~m
        img[new] = acc[new] / np.maximum(cnt[new], 1)[..., None] if img.ndim == 3 else acc[new] / np.maximum(cnt[new], 1)
        m = grown
    return img


def edge_distance(mask, limit=24):
    """Distance in texels from each covered texel to the nearest uncovered one (island borders), up to a limit."""
    dist = np.where(mask, limit, 0).astype(np.float32)
    for _ in range(limit):
        shrunk = np.minimum.reduce([np.roll(dist, s, axis=a) for a in (0, 1) for s in (1, -1)]) + 1
        dist = np.minimum(dist, shrunk)
        dist[~mask] = 0
    return dist


def stitches(along, distance, spacing, width):
    """A dashed stitch line: texels at `distance` from an edge, dashed by `along` (any coordinate along the edge)."""
    line = np.clip(1.0 - np.abs(distance) / width, 0, 1)
    dash = (np.sin(along / spacing * math.tau) > -0.2).astype(np.float32)
    return line * dash
