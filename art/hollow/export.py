"""Scene clean-up, vertex ambient occlusion, LODs, convex hulls and GLB export."""
import math
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from . import log
from .godot import write_scene_import

EXPORT_COLOUR = "Col"


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def collection(name):
    col = bpy.data.collections.get(name)
    if col is None:
        col = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(col)
    return col


def select_only(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.hide_set(False)
        o.hide_viewport = False
        o.select_set(True)
    if objects:
        bpy.context.view_layer.objects.active = objects[0]


def copy_object(source, name, target):
    """A copy of `source` (with its own mesh data) in collection `target`, unparented, same world transform."""
    o = source.copy()
    if source.data is not None:
        o.data = source.data.copy()
        if o.type == "MESH" and o.data.validate(verbose=False):
            # Some kit LOD meshes repeat a few faces; drop them here rather than in the exporter.
            log(f"{name}: repaired the mesh (duplicate faces)")
    o.name = name
    if o.data is not None:
        o.data.name = name
    o.animation_data_clear()
    for c in list(o.users_collection):
        c.objects.unlink(o)
    target.objects.link(o)
    o.parent = None
    o.matrix_world = source.matrix_world.copy()
    o.hide_render = False
    o.hide_set(False)
    o.hide_viewport = False
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    o.constraints.clear() if hasattr(o.constraints, "clear") else None
    return o


def apply_matrix(objects, matrix):
    """Moves objects by `matrix` (world space) and bakes rotation and scale into their meshes, leaving only a
    translation on each node (custom normals follow)."""
    for o in objects:
        o.matrix_world = matrix @ o.matrix_world
    meshes = [o for o in objects if o.type == "MESH"]
    if meshes:
        select_only(meshes)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for o in objects:
        if o.type != "MESH":
            loc, rot, _ = o.matrix_world.decompose()
            o.matrix_world = Matrix.LocRotScale(loc, rot, Vector((1, 1, 1)))


def keep_uv(o, name):
    """Leaves only UV map `name` (renamed UVMap), so Godot gets one UV set."""
    me = o.data
    keep = me.uv_layers.get(name) or (me.uv_layers[0] if me.uv_layers else None)
    for uv in list(me.uv_layers):
        if uv != keep:
            me.uv_layers.remove(uv)
    if keep is not None:
        keep.name = "UVMap"


def export_colour_from_ao(o, ao_name="AO", strength=0.42, tint=(1.0, 1.0, 1.0)):
    """The exported vertex colour: tint x lerp(1, AO, strength). Godot multiplies albedo by it."""
    me = o.data
    ao = me.color_attributes.get(ao_name)
    domain = ao.domain if ao else "POINT"
    count = len(me.vertices) if domain == "POINT" else len(me.loops)
    values = np.ones(count * 4, np.float32)
    if ao is not None:
        ao.data.foreach_get("color", values)
    values = values.reshape(-1, 4)
    shade = 1 - strength + strength * values[:, 0]
    out = np.empty((count, 4), np.float32)
    out[:, 0] = shade * tint[0]
    out[:, 1] = shade * tint[1]
    out[:, 2] = shade * tint[2]
    out[:, 3] = 1
    set_export_colour(o, out, domain)


def set_export_colour(o, rgba, domain="POINT"):
    me = o.data
    for attr in list(me.color_attributes):
        if attr.name == EXPORT_COLOUR:
            me.color_attributes.remove(attr)
    col = me.color_attributes.new(name=EXPORT_COLOUR, type="FLOAT_COLOR", domain=domain)
    col.data.foreach_set("color", np.ascontiguousarray(rgba, np.float32).ravel())
    for attr in list(me.color_attributes):
        if attr.name != EXPORT_COLOUR:
            me.color_attributes.remove(attr)
    me.color_attributes.active_color = me.color_attributes[EXPORT_COLOUR]
    me.color_attributes.render_color_index = 0


def hemisphere(n):
    """`n` deterministic unit directions spread over the +Z hemisphere, cosine-weighted."""
    out = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        r = math.sqrt((i + 0.5) / n)
        a = i * golden
        out.append(Vector((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1 - r * r)))))
    return out


def vertex_ao(objects, samples=32, distance=None, floor=True):
    """Ambient occlusion per vertex, by casting rays against all `objects` (and a ground plane under them when
    `floor`). Returns {object: array of occlusion 0..1 (1 = open)} in the POINT domain."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    verts, polys = [], []
    for o in objects:
        me = o.data
        m = o.matrix_world
        base = len(verts)
        verts += [m @ v.co for v in me.vertices]
        polys += [[base + i for i in p.vertices] for p in me.polygons]
    lo = Vector((min(v.x for v in verts), min(v.y for v in verts), min(v.z for v in verts)))
    hi = Vector((max(v.x for v in verts), max(v.y for v in verts), max(v.z for v in verts)))
    size = (hi - lo).length
    if distance is None:
        distance = max(0.02, size * 0.3)
    if floor:
        pad = size
        base = len(verts)
        verts += [Vector((lo.x - pad, lo.y - pad, lo.z)), Vector((hi.x + pad, lo.y - pad, lo.z)),
                  Vector((hi.x + pad, hi.y + pad, lo.z)), Vector((lo.x - pad, hi.y + pad, lo.z))]
        polys.append([base, base + 1, base + 2, base + 3])
    tree = BVHTree.FromPolygons(verts, polys, epsilon=0.0)
    dirs = hemisphere(samples)
    result = {}
    for o in objects:
        me = o.data
        m = o.matrix_world
        nm = m.to_3x3().inverted().transposed()
        values = np.ones(len(me.vertices), np.float32)
        for i, v in enumerate(me.vertices):
            p = m @ v.co
            n = (nm @ v.normal).normalized()
            t = n.orthogonal().normalized()
            b = n.cross(t)
            origin = p + n * max(0.0005, size * 0.0015)
            hits = 0.0
            for d in dirs:
                ray = t * d.x + b * d.y + n * d.z
                loc, _, _, dist = tree.ray_cast(origin, ray, distance)
                if loc is not None:
                    hits += 1.0 - (dist / distance) ** 2
            values[i] = 1.0 - hits / samples
        result[o] = values
    return result


def decimated(source, name, ratio, target):
    """A decimated copy of a mesh object (UVs and colours interpolated)."""
    o = copy_object(source, name, target)
    if ratio < 0.999 and len(o.data.polygons) > 24:
        mod = o.modifiers.new("lod", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
        select_only([o])
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


def convex_hull(objects, name, target, max_faces=96):
    """One convex hull around the vertices of `objects` (world space), simplified to about `max_faces` faces."""
    bm = bmesh.new()
    for o in objects:
        m = o.matrix_world
        for v in o.data.vertices:
            bm.verts.new(m @ v.co)
    bm.verts.ensure_lookup_table()
    result = bmesh.ops.convex_hull(bm, input=bm.verts, use_existing_faces=False)
    loose = {g for g in result["geom_interior"] + result["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
    bmesh.ops.delete(bm, geom=list(loose), context="VERTS")
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    target.objects.link(o)
    if len(me.polygons) > max_faces:
        mod = o.modifiers.new("simplify", "DECIMATE")
        mod.ratio = max_faces / len(me.polygons)
        select_only([o])
        bpy.ops.object.modifier_apply(modifier=mod.name)
        # Decimation can dent the hull; wrap it again.
        bm = bmesh.new()
        bm.from_mesh(me)
        res = bmesh.ops.convex_hull(bm, input=bm.verts, use_existing_faces=False)
        bmesh.ops.delete(bm, geom=[g for g in res["geom_interior"] if isinstance(g, bmesh.types.BMVert)], context="VERTS")
        bm.to_mesh(me)
        bm.free()
    me.materials.clear()
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    for uv in list(me.uv_layers):
        me.uv_layers.remove(uv)
    return o


def box_hull(name, centre, size, target, rotation=(0, 0, 0)):
    """A box-shaped convex collision hull."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bm.to_mesh(me := bpy.data.meshes.new(name))
    bm.free()
    o = bpy.data.objects.new(name, me)
    target.objects.link(o)
    o.scale = size
    o.rotation_euler = rotation
    o.location = centre
    select_only([o])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o


def export_glb(path, objects, materials, animations=False):
    """Exports `objects` (and only them) as GLB without images, then writes the Godot `.import` mapping each
    material to its shared .tres. `materials`: {material name in Blender: .tres path}."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    select_only(objects)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_image_format="NONE",
        export_materials="EXPORT", export_vertex_color="ACTIVE", export_all_vertex_colors=False,
        export_yup=True, export_apply=False, export_texcoords=True, export_normals=True, export_tangents=False,
        export_animations=animations, export_extras=False, export_cameras=False, export_lights=False,
        export_animation_mode="ACTIONS", export_force_sampling=True, export_skins=True)
    used = set()
    for o in objects:
        if o.type == "MESH":
            used.update(m.name for m in o.data.materials if m)
    missing = used - set(materials)
    if missing:
        raise RuntimeError(f"{path.name}: no Godot material for {sorted(missing)}")
    write_scene_import(path, {k: v for k, v in materials.items() if k in used}, animations)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects
               if o.type == "MESH" and o.name.lower().endswith("_lod0"))
    log(f"exported {path.name}: {len(objects)} nodes, LOD0 {tris} triangles, {path.stat().st_size / 1e6:.2f} MB")
    return path
