"""A piece's drawn levels from its cut solid (docs/WORLD_TERRAIN_PLAN.md §4.3): the bottom below the skirt dropped,
the masks painted per vertex, LOD0 decimated to its budget with its top lip and ends protected, LOD1-3 at a half, a
quarter and an eighth of it, a far shell of a few hundred triangles, and the closed collision shell.

Masks (the exported vertex colour, linear): red is the base's dirt or wetness (the lowest metres), green where moss,
snow or grass may grow (faces that look up, out of the fractures), blue the fracture surfaces of the cut ends and
slopes, which have no scan and show the family's detail material instead (`cliffkit.gdshader`).
"""
import bmesh
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

from hollow import log
from hollow.export import EXPORT_COLOUR, select_only

from .shape import BOTTOM, END, arrays

PROTECT = "protect"


def copy(obj, name, collection):
    o = obj.copy()
    o.data = obj.data.copy()
    o.name = name
    o.data.name = name
    collection.objects.link(o)
    return o


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def cut_kinds(obj):
    me = obj.data
    kinds = np.zeros(len(me.polygons), np.int32)
    if "cut" in me.attributes:
        me.attributes["cut"].data.foreach_get("value", kinds)
    return kinds


def visible(shape, collection):
    """The solid without its bottom below the skirt, which nothing ever sees. The back stays: it is nearly flat, so the
    decimation spends few triangles on it, and a piece seen from behind (where the ground behind it falls lower than
    planned, or on the viewer's turntable) is rock rather than an open shell."""
    o = copy(shape.solid, shape.spec["key"] + "_visible", collection)
    kinds = cut_kinds(o)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    gone = [f for f in bm.faces if kinds[f.index] == BOTTOM or
            (kinds[f.index] != END and f.normal.z < -0.7 and f.calc_center_median().z < -shape.skirt + 0.3)]
    bmesh.ops.delete(bm, geom=gone, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    return o


def paint_masks(obj, top):
    """The masks as the exported vertex colour (point domain, linear), and the decimation's protection group: the top
    lip (the highest metre and a half at each place along the piece) and the fractures."""
    me = obj.data
    co, nrm = arrays(obj)
    kinds = cut_kinds(obj)
    n = len(co)
    frac = np.zeros(n, np.float32)
    for p in me.polygons:
        if kinds[p.index] == END:
            frac[list(p.vertices)] = 1.0
    # One ring of softening, so the fracture's edge blends into the scan.
    soft = frac.copy()
    for e in me.edges:
        a, b = e.vertices
        if frac[a] != frac[b]:
            soft[a] = soft[b] = max(soft[a], soft[b], 0.5)
    z = co[:, 2]
    red = np.clip(1.0 - z / 3.0, 0.0, 1.0) ** 1.5
    green = np.clip((nrm[:, 2] - 0.35) / 0.45, 0.0, 1.0) * (1.0 - soft)
    rgba = np.stack([red, green, soft, np.ones(n)], axis=1).astype(np.float32)
    for attr in list(me.color_attributes):
        me.color_attributes.remove(attr)
    col = me.color_attributes.new(name=EXPORT_COLOUR, type="FLOAT_COLOR", domain="POINT")
    col.data.foreach_set("color", rgba.ravel())
    me.color_attributes.active_color = col
    me.color_attributes.render_color_index = 0

    # Protection: the lip (per 1 m slice of y, within 1.5 m of the top there) and the fractures.
    slices = np.round(co[:, 1]).astype(int)
    tops = {s: z[slices == s].max() for s in np.unique(slices)}
    lip = np.array([z[i] > tops[slices[i]] - 1.5 for i in range(n)])
    weight = np.maximum(lip.astype(np.float32), frac)
    # Blender's collapse never touches a vertex whose (inverted) weight is 0, so the protected ones get 0.75: inverted,
    # a quarter of the others' freedom, which makes their edges dearer to collapse rather than fixed.
    group = obj.vertex_groups.get(PROTECT) or obj.vertex_groups.new(name=PROTECT)
    idx = [int(i) for i in np.nonzero(weight > 0.5)[0]]
    if idx:
        group.add(idx, 0.75, "REPLACE")


def decimate(obj, target, protect=2.0):
    """Collapses `obj` to about `target` triangles, the protection group's vertices (lip and fractures) kept longest."""
    have = triangles(obj)
    if have <= target:
        return obj
    mod = obj.modifiers.new("lod", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = target / have
    mod.use_collapse_triangulate = True
    if PROTECT in obj.vertex_groups and protect > 0:
        mod.vertex_group = PROTECT
        mod.invert_vertex_group = True
        mod.vertex_group_factor = protect
    select_only([obj])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.validate(verbose=False)
    return obj


def unwrap(obj):
    select_only([obj])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.004, area_weight=0.0, correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = obj.data
    for uv in list(me.uv_layers)[1:]:
        me.uv_layers.remove(uv)
    me.uv_layers[0].name = "UVMap"


def collision(shape, collection, max_vertices=1000):
    """The closed collision shell: the solid (back and bottom included) decimated to at most `max_vertices`."""
    o = copy(shape.solid, shape.spec["key"] + "_collision", collection)
    target = 2 * max_vertices - 80
    decimate(o, target, protect=0)
    while len(o.data.vertices) > max_vertices:
        decimate(o, int(triangles(o) * 0.92), protect=0)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    # Collapsing can leave a pinhole or two; closed again.
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=12)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    open_edges = sum(1 for e in bm.edges if not e.is_manifold)
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    return o, open_edges


def deviation(drawn, shell):
    """How far LOD0's vertices (above the skirt) lie from the collision shell: (95th percentile, largest), metres."""
    tree = BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    d = []
    for v in drawn.data.vertices:
        if v.co.z > -1.0:
            hit = tree.find_nearest(v.co)
            if hit[0] is not None:
                d.append(hit[3])
    d = np.array(d) if d else np.zeros(1)
    return float(np.percentile(d, 95)), float(d.max())


def build(shape, collection):
    """LOD0..LOD3 and the far shell (named `<key>_lod0`..`_lod4`), and the collision shell; with their numbers."""
    key = shape.spec["key"]
    budget = shape.spec["lod0"]
    vis = visible(shape, collection)
    paint_masks(vis, shape.top)
    lod0 = decimate(vis, budget)
    lod0.name = lod0.data.name = key + "_lod0"
    unwrap(lod0)
    lods = [lod0]
    for i, ratio in enumerate((0.5, 0.25, 0.125), start=1):
        o = decimate(copy(lod0, f"{key}_lod{i}", collection), int(triangles(lod0) * ratio), protect=2.0 / i)
        lods.append(o)
    shell_budget = int(min(600, max(120, triangles(lod0) / 30)))
    shell = decimate(copy(lod0, f"{key}_lod4", collection), shell_budget, protect=0)
    lods.append(shell)
    col, open_edges = collision(shape, collection)
    p95, worst = deviation(lod0, col)
    for o in lods + [col]:
        if "cut" in o.data.attributes:
            o.data.attributes.remove(o.data.attributes["cut"])
        for g in list(o.vertex_groups):
            o.vertex_groups.remove(g)
    log(f"{key}: LODs {[triangles(o) for o in lods]} triangles; collision {len(col.data.vertices)} vertices, "
        f"{triangles(col)} triangles, {open_edges} open edges, LOD0 within {p95:.2f} m (95%), {worst:.2f} m at most")
    return lods, col, {"collision_p95": p95, "collision_max": worst, "collision_open": open_edges}
