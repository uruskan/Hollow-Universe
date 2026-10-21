"""A piece's closed solid from its scan (docs/WORLD_TERRAIN_PLAN.md §4.3): turned to face +X, cropped to its span, the
ground round it dropped, closed behind its face (or all round, for a free-standing rock), remeshed at 0.25 m, then cut
along diagonal fractures at its ends, along a slope at a slope end, behind its top and below its skirt.

The piece's frame is Blender's (metres, Z up): the face looks along +X, the pivot is on the ground line at the middle of
the face's foot, and the left end as you face the rock is -Y. `Shape.frame` takes the scan's own coordinates there, so
the scan can be brought in again for baking.
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector, noise
from mathutils.bvhtree import BVHTree

from hollow import SOURCES, log
from hollow.export import select_only

VOXEL = 0.25        # the solid's remesh, metres (finer for small rocks: Shape.voxel)
SKIRT = 3.0         # the deepest skirt below the ground line (a quarter of the height for low pieces: Shape.skirt)
MARGIN = 5.0        # scan kept beyond a span's ends for the cuts' slant and relief
SOCKET_INSET = 1.5  # sockets this far inside each end
TAPER = 0.12        # a slope end's height at its far end, against the piece's

# What a face of the cut solid was cut by (the `cut` face attribute): kept as rock, an end's fracture, (unused), or the
# buried back and bottom (dropped from the drawn levels, kept in the collision shell).
ROCK, END, SLOPE, BACK, BOTTOM = range(5)


class Shape:
    """The cut solid, and what the data file needs to know about where it came from."""

    def __init__(self, spec):
        self.spec = spec
        self.frame = Matrix.Identity(4)
        self.ends = {}       # "left"/"right": (point on the ground line, outward direction, kind)
        self.solid = None    # closed: the collision shell's source
        self.top = 0.0
        self.size = 0.0      # LOD0's largest extent above the skirt (data.placement)
        self.skirt = SKIRT
        self.voxel = VOXEL
        self.taper = None    # a slope end: (outer y, joint y), the height falling toward the outer end

    def bend(self, obj):
        """A slope end's fall: heights above the ground line squashed toward the outer end (smoothly, to TAPER of
        themselves there), so that the strata dip into the ground. Applied to the scan and to its bake source alike."""
        if self.taper is None:
            return
        outer, joint = self.taper
        span = (joint - outer) * 0.9
        me = obj.data
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        t = np.clip((co[:, 1] - outer) / span, 0.0, 1.0)
        f = TAPER + (1.0 - TAPER) * t * t * (3.0 - 2.0 * t)
        co[:, 2] = np.where(co[:, 2] > 0.0, co[:, 2] * f, co[:, 2])
        me.vertices.foreach_set("co", co.ravel())
        me.update()


# ---------------------------------------------------------------------------------------------------- the scan

def load_scan(name):
    """The scan's one mesh, its parents' transform (the 0.01 of a centimetre export) baked in, custom normals cleared."""
    path = SOURCES / name
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    mesh = next(o for o in new if o.type == "MESH")
    world = mesh.matrix_world.copy()
    mesh.parent = None
    mesh.matrix_world = Matrix.Identity(4)
    mesh.data.transform(world)
    for o in new:
        if o is not mesh:
            bpy.data.objects.remove(o, do_unlink=True)
    select_only([mesh])
    if mesh.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    # glTF splits vertices along every UV seam; joined again (the UVs stay per corner) the scan is one surface whose
    # only open edges are its real rims.
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bm.to_mesh(mesh.data)
    bm.free()
    mesh.data.shade_smooth()
    return mesh


def scan_frame(spec):
    """Scale, then the face turned from `yaw` to +X, then the mirror (across the face's length)."""
    m = Matrix.Rotation(-math.radians(spec["yaw"]), 4, "Z") @ Matrix.Scale(spec["scale"], 4)
    if spec["mirror"]:
        m = Matrix.Scale(-1, 4, Vector((0, 1, 0))) @ m
    return m


def transform(obj, m):
    obj.data.transform(m)
    if m.determinant() < 0:
        obj.data.flip_normals()
    obj.data.update()


def arrays(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", co)
    nrm = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("normal", nrm)
    return co.reshape(-1, 3), nrm.reshape(-1, 3)


def delete_faces(obj, test):
    """Deletes the faces for which test(centre, normal) holds, then loose vertices."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    gone = [f for f in bm.faces if test(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def drop_ground(obj, apron):
    """Drops the flat ground scanned round the rock: faces that look up, lie low and stand more than `apron` metres
    out from the rock's steep faces in plan, reached from the scan's rim through others like them. A fillet of talus
    along the foot stays, and ledges higher up. Returns the height of the rim left low down: the ground line."""
    from mathutils.kdtree import KDTree
    co, nrm = arrays(obj)
    floor = float(np.percentile(co[:, 2], 3))
    limit = floor + max(1.2, 0.15 * (co[:, 2].max() - floor))
    steep = co[(np.abs(nrm[:, 2]) < 0.55) & (co[:, 2] > floor + 1.5)]
    tree = KDTree(max(1, len(steep)))
    for i, c in enumerate(steep):
        tree.insert((c[0], c[1], 0.0), i)
    tree.balance()
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    def groundish(f):
        c = f.calc_center_median()
        if f.normal.z <= 0.7 or c.z >= limit:
            return False
        return len(steep) == 0 or tree.find((c.x, c.y, 0.0))[2] > apron

    seen = set()
    stack = [f for f in bm.faces if groundish(f) and any(e.is_boundary for e in f.edges)]
    seen.update(f.index for f in stack)
    dropped = []
    while stack:
        f = stack.pop()
        dropped.append(f)
        for e in f.edges:
            for g in e.link_faces:
                if g.index not in seen and groundish(g):
                    seen.add(g.index)
                    stack.append(g)
    bmesh.ops.delete(bm, geom=dropped, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    rim = [v.co.z for v in bm.verts if v.is_boundary and v.co.z < limit]
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return float(np.median(rim)) if rim else floor


def drop_islands(obj, keep_largest=False, min_faces=60):
    """Drops small separate pieces (crumbs of ground, a cavity's inner wall); with keep_largest, all but the largest."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    seen = set()
    islands = []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, island = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            island.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        islands.append(island)
    islands.sort(key=len, reverse=True)
    gone = [f for i, island in enumerate(islands) if i > 0 and (keep_largest or len(island) < min_faces) for f in island]
    if gone:
        bmesh.ops.delete(bm, geom=gone, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.to_mesh(obj.data)
        obj.data.update()
    bm.free()
    return len(islands)


# ---------------------------------------------------------------------------------------------------- the face's lines

def foot_line(co, y0, y1, step=1.0):
    """The face's foot along the span: per slice of y, the front-most x between 0.3 and 3 m up."""
    ys, xs = [], []
    band = co[(co[:, 2] > 0.3) & (co[:, 2] < 3.0)]
    for y in np.arange(y0, y1 + 1e-6, step):
        s = band[np.abs(band[:, 1] - y) < step * 0.75]
        if len(s) >= 3:
            ys.append(y)
            xs.append(np.percentile(s[:, 0], 97))
    return np.array(ys), np.array(xs)


def lip_line(co, y0, y1, step=1.0, depth=2.5):
    """The lip along the span: per slice of y, the front-most x within `depth` of the top, the top, the back, and the
    face's deepest point (the least front-most x over its height)."""
    rows = []
    for y in np.arange(y0, y1 + 1e-6, step):
        s = co[np.abs(co[:, 1] - y) < step * 0.75]
        if len(s) < 3:
            continue
        top = s[:, 2].max()
        near = s[s[:, 2] > top - depth]
        fronts = [band[:, 0].max() for z in np.arange(0.5, max(0.6, top - 0.5), 1.0)
                  if len(band := s[np.abs(s[:, 2] - z) < 0.6])]
        lip = np.percentile(near[:, 0], 95)
        rows.append((y, lip, top, s[:, 0].min(), min(fronts) if fronts else lip))
    return np.array(rows)


def smooth(values, width=2):
    if len(values) < 3:
        return values
    out = np.copy(values)
    for i in range(len(values)):
        out[i] = values[max(0, i - width):i + width + 1].mean()
    return out


def interp(ys, xs, y):
    return float(np.interp(y, ys, xs))


# ---------------------------------------------------------------------------------------------------- closing the scan

def rim_loops(bm):
    """The scan's openings: its boundary edges grouped by the loops they form."""
    loops, seen = [], set()
    for e in bm.edges:
        if not e.is_boundary or e in seen:
            continue
        loop, stack = [], [e]
        seen.add(e)
        while stack:
            f = stack.pop()
            loop.append(f)
            for v in f.verts:
                for g in v.link_edges:
                    if g.is_boundary and g not in seen:
                        seen.add(g)
                        stack.append(g)
        loops.append(loop)
    return loops


def close(obj, skirt, ground=0.0):
    """Closes the scan into a solid: its small holes filled, so that only its rims stay open, then every rim hung
    straight down to below the skirt and the bottom capped. A cliff's top edge behind hangs down as its back, the rim
    where the ground was dropped as its skirt; a free-standing rock is closed round. Only rims hang, so overhangs stay
    and no offset shell pokes through thin ledges."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    # Every opening but the scan's outer rim (the longest) is a hole in the rock: filled, whatever its size, unless it
    # opens on the ground (where the ground was dropped between rocks), which hangs down like the rim.
    loops = rim_loops(bm)
    loops.sort(key=len, reverse=True)
    for edges in loops[1:]:
        if min(min(v.co.z for v in e.verts) for e in edges) > ground + 1.0:
            bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    rims = [e for e in bm.edges if e.is_boundary]
    rim = {v for e in rims for v in e.verts}
    made = bmesh.ops.extrude_edge_only(bm, edges=rims)
    hung = [v for v in made["geom"] if isinstance(v, bmesh.types.BMVert)]
    tops = {}
    for v in hung:
        for e in v.link_edges:
            o = e.other_vert(v)
            if o in rim:
                tops[v] = o.co.copy()
    # The skirt hangs to a line along the rim smoothed over a few metres, so that a ragged rim (where the ground was
    # dropped) doesn't hang a comb of strips.
    pts = np.array([[c.x, c.y] for c in tops.values()])
    for v in hung:
        c = tops.get(v, v.co)
        near = pts[np.hypot(pts[:, 0] - c.x, pts[:, 1] - c.y) < 2.0]
        if len(near):
            v.co.x, v.co.y = float(near[:, 0].mean()), float(near[:, 1].mean())
        v.co.z = -skirt - 0.75
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def remesh(obj, voxel=VOXEL):
    obj.data.remesh_voxel_size = voxel
    obj.data.remesh_voxel_adaptivity = 0.0
    select_only([obj])
    bpy.ops.object.voxel_remesh()
    drop_islands(obj, keep_largest=True)


# ---------------------------------------------------------------------------------------------------- cutters

def relief(p, seed, size, amount):
    """A fracture's relief at p: blocky steps over a rough fractal, metres along the cut's normal."""
    q = Vector((p.x / size + seed * 17.3, p.y / size - seed * 9.1, p.z / size + seed * 3.7))
    rough = noise.fractal(q, 0.9, 2.1, 4, noise_basis="PERLIN_ORIGINAL")
    steps = noise.noise(q * 0.45 + Vector((seed, 0, 0)), noise_basis="VORONOI_F2F1")
    return amount * (0.75 * rough + 0.6 * min(1.0, steps * 2.2))


def cutter(name, origin, normal, half_u, half_v, depth, step, seed, rough, kind, collection, up_hint=None, size=3.0):
    """A closed block beyond a cut: its face through `origin` (spanning 2·half_u by 2·half_v, a grid of `step`) looks
    back along -normal at the piece and carries the fracture's relief; it reaches `depth` along +normal. Its faces carry
    the `cut` attribute `kind`."""
    n = normal.normalized()
    hint = up_hint or Vector((0, 0, 1))
    u = hint.cross(n)
    if u.length < 1e-3:
        u = Vector((1, 0, 0)).cross(n)
    u.normalize()
    v = n.cross(u).normalized()
    nu, nv = max(2, int(2 * half_u / step)), max(2, int(2 * half_v / step))
    bm = bmesh.new()
    grid = []
    for i in range(nu + 1):
        row = []
        for j in range(nv + 1):
            p = origin + u * (-half_u + 2 * half_u * i / nu) + v * (-half_v + 2 * half_v * j / nv)
            edge = i in (0, nu) or j in (0, nv)
            if rough > 0 and not edge:
                p = p + n * relief(p, seed, size, rough)
            row.append(bm.verts.new(p))
        grid.append(row)
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
    made = bmesh.ops.extrude_face_region(bm, geom=list(bm.faces))
    for v_ in made["geom"]:
        if isinstance(v_, bmesh.types.BMVert):
            v_.co += n * depth
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    attr = me.attributes.new("cut", "INT", "FACE")
    attr.data.foreach_set("value", [kind] * len(me.polygons))
    o = bpy.data.objects.new(name, me)
    collection.objects.link(o)
    return o


def back_cutter(name, lip, keep, z_lo, z_hi, collection):
    """The top kept behind the lip: everything further back than `keep` metres behind it goes (a prism along y), but
    never within a metre and a half of the face's deepest point, so that a recess under an overhanging lip isn't
    cut through."""
    ys = lip[:, 0]
    xs = np.minimum(smooth(lip[:, 1], 2) - keep, smooth(lip[:, 4], 2) - 1.5)
    bm = bmesh.new()
    far = xs.min() - 200.0
    front = [Vector((x, y, 0)) for x, y in zip(xs, ys)]
    front = [Vector((front[0].x, ys[0] - 200.0, 0))] + front + [Vector((front[-1].x, ys[-1] + 200.0, 0))]
    outline = front + [Vector((far, ys[-1] + 200.0, 0)), Vector((far, ys[0] - 200.0, 0))]
    lo = [bm.verts.new((p.x, p.y, z_lo)) for p in outline]
    hi = [bm.verts.new((p.x, p.y, z_hi)) for p in outline]
    n = len(outline)
    for k in range(n):
        bm.faces.new((lo[k], lo[(k + 1) % n], hi[(k + 1) % n], hi[k]))
    bm.faces.new(lo[::-1])
    bm.faces.new(hi)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    attr = me.attributes.new("cut", "INT", "FACE")
    attr.data.foreach_set("value", [BACK] * len(me.polygons))
    o = bpy.data.objects.new(name, me)
    collection.objects.link(o)
    return o


def subtract(solid, tool):
    before = len(solid.data.polygons)
    mod = solid.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "MANIFOLD"
    mod.object = tool
    select_only([solid])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    log(f"  {tool.name}: {before} -> {len(solid.data.polygons)} faces")
    bpy.data.objects.remove(tool, do_unlink=True)


# ---------------------------------------------------------------------------------------------------- the piece

def build(spec, collection):
    """The piece's closed solid (with its `cut` face attribute) and its frame, ends and top."""
    shape = Shape(spec)
    key = spec["key"]
    scan = load_scan(spec_scan(spec))
    for c in list(scan.users_collection):
        c.objects.unlink(scan)
    collection.objects.link(scan)
    scan.name = key + "_scan"
    first = scan_frame(spec)
    transform(scan, first)
    co, _ = arrays(scan)
    whole = spec["span"] is None
    y0, y1 = (co[:, 1].min(), co[:, 1].max()) if whole else spec["span"]
    if not whole:
        delete_faces(scan, lambda c, n: c.y < y0 - MARGIN or c.y > y1 + MARGIN)
    ground = drop_ground(scan, spec["apron"])
    drop_islands(scan)
    co, _ = arrays(scan)
    shape.voxel = float(np.clip((co.max(axis=0) - co.min(axis=0)).max() / 150.0, 0.04, VOXEL))

    # Turn the face's foot to run along y, and put the pivot at its middle on the ground line.
    ys, xs = foot_line(co, y0, y1)
    turn = 0.0
    if spec["align"] and len(ys) >= 4:
        a, _ = np.polyfit(ys, xs, 1)
        turn = math.atan(a)
    rot = Matrix.Rotation(turn, 4, "Z")
    if spec["role"] == "rubble":
        lo, hi = co.min(axis=0), co.max(axis=0)
        mid = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, 0))
        pivot = mid
        pivot.z = ground
    else:
        ym = (y0 + y1) / 2
        pivot = rot @ Vector((interp(ys, smooth(xs), ym), ym, 0))
        pivot.z = ground
    second = Matrix.Translation(-pivot) @ rot
    transform(scan, second)
    shape.frame = second @ first
    co, _ = arrays(scan)

    # The ends: where each meets the ground on the face's foot, and which way it looks out.
    for side, y, angle, kind in (("left", y0, spec["angles"][0], spec["ends"][0]),
                                  ("right", y1, spec["angles"][1], spec["ends"][1])):
        foot = second @ Vector((interp(ys, smooth(xs), y), y, 0))
        out = (second.to_3x3() @ Vector((math.cos(math.radians(angle)), math.sin(math.radians(angle)), 0))).normalized()
        if kind == "natural":
            # The rock's own end: its farthest point along the outward direction, near the ground.
            low = co[co[:, 2] < 2.0]
            along = low @ np.array(out)
            p = low[np.argmax(along)]
            foot = Vector((p[0], p[1], 0))
        foot.z = 0
        shape.ends[side] = (foot, out, kind)
    for side, other in (("left", "right"), ("right", "left")):
        if shape.ends[side][2] == "taper":
            shape.taper = (shape.ends[side][0].y, shape.ends[other][0].y)
    shape.bend(scan)
    co, _ = arrays(scan)
    shape.top = float(co[:, 2].max())
    shape.skirt = float(np.clip(0.25 * shape.top, 0.4, SKIRT))

    # Close it (swept back for a cliff, capped below for a free-standing rock), then remesh it.
    free = not spec["fill"]
    close(scan, shape.skirt)
    remesh(scan, shape.voxel)
    solid = scan
    solid.name = key + "_solid"
    attr = solid.data.attributes.new("cut", "INT", "FACE")
    attr.data.foreach_set("value", [ROCK] * len(solid.data.polygons))
    log(f"{key}: solid {len(solid.data.polygons)} faces after the remesh")

    # The cuts.
    co, _ = arrays(solid)
    lo, hi = co.min(axis=0), co.max(axis=0)
    span_u = max(hi[0] - lo[0], hi[1] - lo[1]) + 10
    height = hi[2] - lo[2] + 10
    seed = sum(ord(ch) for ch in key) % 97
    rough = float(np.clip(0.07 * shape.top, 0.4, 2.2))
    grain = float(np.clip(0.15 * shape.top, 1.2, 5.0))
    for side, (foot, out, kind) in shape.ends.items():
        if kind in ("cut", "taper"):
            # A diagonal fracture: turned in plan so that the end's front recedes behind its back (a neighbour's face
            # in front hides it), and leant back, with relief on the scale of the piece.
            face = Matrix.Rotation(math.radians(90 if side == "left" else -90), 3, "Z") @ out
            plan = math.radians(12 + (seed + len(side)) % 11)
            lean = math.radians(10 + (seed // 3) % 10)
            d = (out + face * math.tan(plan)).normalized()
            normal = (d * math.cos(lean) + Vector((0, 0, 1)) * math.sin(lean)).normalized()
            centre = foot + Vector((0, 0, height / 2 - 5))
            tool = cutter(f"{key}_cut_{side}", centre, normal, span_u, height / 2 + 4, 60.0, min(0.35, grain / 6),
                          seed + len(side), rough, END, collection, size=grain)
            subtract(solid, tool)
    if not free:
        lip = lip_line(arrays(solid)[0], lo[1] - 1, hi[1] + 1)
        tool = back_cutter(f"{key}_back", lip, spec["back"], -shape.skirt - 20, hi[2] + 20, collection)
        subtract(solid, tool)
    bottom = cutter(f"{key}_bottom", Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, -shape.skirt)),
                    Vector((0, 0, -1)),
                    span_u, span_u, 60.0, 4.0, 0, 0.0, BOTTOM, collection, up_hint=Vector((0, 1, 0)))
    subtract(solid, bottom)
    drop_islands(solid, keep_largest=True)
    shape.solid = solid
    log(f"{key}: cut solid {len(solid.data.polygons)} faces, top {shape.top:.1f} m, skirt {shape.skirt:.1f} m, "
        f"voxel {shape.voxel:.2f} m")
    return shape


def spec_scan(spec):
    from .kit import SCANS
    return SCANS[spec["scan"]]
