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


def hem(obj, depth, bulge=0.0):
    """Turns every open edge inwards by `depth` (a visible thickness at cuffs and hems) and can roll the edge out."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    edges = [e for e in bm.edges if e.is_boundary]
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


def envelope(obj, origin, axis, *, t_step=0.01, sectors=48, blur_t=2, blur_theta=2, fill=1.0, drape=None,
             extra=0.0, select=None, reference=None, indices=None):
    """Makes cross-sections round and roomy, the way cloth hangs. Around an axis (a limb or the trunk), each vertex is
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
    # Empty cells take their neighbours' values (thin limbs leave gaps in the angle table).
    for _ in range(sectors):
        empty = table == 0
        if not empty.any():
            break
        neighbour = np.maximum(np.roll(table, 1, axis=1), np.roll(table, -1, axis=1))
        table[empty] = neighbour[empty]
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
