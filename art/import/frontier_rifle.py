"""PSG Frontier AK R05 -> game/assets/items/rifle.glb, the rifle (GROUND.md §9).

The static pickup meshes LOD1-LOD3 as lod0-lod2 (the 66,652-triangle LOD0 adds nothing seen in a hand; the character
package made the same choice), the six UCX hulls and the socket contract (muzzle, eject, grips, sight) with the eye at
the kit's aiming camera. The source's rigid bone weights split the identical pickup topology into Body, Magazine and
Bolt at each LOD, preserving positions, UVs, materials and custom corner normals. The original rig and animations
stay in the read-only source; the game animates the two part nodes. Its two texture sets go to 2K. The rifle keeps
the kit's frame (+X along the barrel, Godot +Y up) with its pivot at the right hand's grip.

    blender -b --factory-startup --python art/import/frontier_rifle.py
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from hollow import ITEMS, RUSTMVP_ASSETS, TEXTURES  # noqa: E402
from hollow.godot import write_material  # noqa: E402
from hollow.export import collection, copy_object  # noqa: E402
from hollow.kit import TURN_HELD, Conversion, mount_sockets, objects_in  # noqa: E402
from hollow.textures import convert, write_texture_import  # noqa: E402

SOURCE = RUSTMVP_ASSETS / "PSG_FrontierAK_R05"
OUT = TEXTURES / "frontier"


def rigid_labels(static, rigged):
    """Use the owned source rig only as topology labels; never evaluate a posed/animated mesh."""
    if len(static.data.vertices) != len(rigged.data.vertices) or len(static.data.polygons) != len(rigged.data.polygons):
        raise RuntimeError(f"{static.name}: static/rigged topology differs")
    names = {group.index: group.name for group in rigged.vertex_groups}
    labels = []
    for a, b in zip(static.data.vertices, rigged.data.vertices):
        if (a.co - b.co).length > 1e-7:
            raise RuntimeError(f"{static.name}: static/rigged vertex positions differ")
        weights = sorted(b.groups, key=lambda weight: weight.weight, reverse=True)
        if not weights or weights[0].weight < 0.999:
            raise RuntimeError(f"{static.name}: expected a rigid vertex weight")
        name = names[weights[0].group]
        labels.append(name if name in ("Magazine", "Bolt") else "Body")
    for a, b in zip(static.data.polygons, rigged.data.polygons):
        if tuple(a.vertices) != tuple(b.vertices) or len({labels[i] for i in a.vertices}) != 1:
            raise RuntimeError(f"{static.name}: a face crosses rigid parts or topology differs")
    return labels


def split_part(source, labels, part, target):
    """Delete only the other rigid components from a private copy; preserve all original loop data and normals."""
    mesh = source.data.copy()
    result = bpy.data.objects.new(f"{source.name}_{part}", mesh)
    target.objects.link(result)
    result.matrix_world = source.matrix_world.copy()
    normals = [normal.vector.copy() for normal in source.data.corner_normals]
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    original_corner = bm.loops.layers.int.new("hollow_source_corner")
    for face, polygon in zip(bm.faces, source.data.polygons):
        corners_by_vertex = {source.data.loops[index].vertex_index: index for index in polygon.loop_indices}
        if set(corners_by_vertex) != {vertex.index for vertex in face.verts}:
            raise RuntimeError(f"{source.name}: BMesh changed face identity")
        for loop in face.loops:
            loop[original_corner] = corners_by_vertex[loop.vert.index]
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if labels[v.index] != part], context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    corners = mesh.attributes["hollow_source_corner"]
    kept_normals = [normals[value.value] for value in corners.data]
    mesh.attributes.remove(corners)
    mesh.update()
    expected = sum(len(p.vertices) - 2 for p in source.data.polygons if labels[p.vertices[0]] == part)
    actual = sum(len(p.vertices) - 2 for p in mesh.polygons)
    if expected != actual or actual == 0:
        raise RuntimeError(f"{source.name}/{part}: split changed topology ({expected} -> {actual} triangles)")
    return result, kept_normals


def materials():
    result = {}
    for part in ("Metal", "Furniture"):
        maps = {}
        for name, normal in (("BaseColor", False), ("ORM", False), ("NormalGL", True)):
            target = OUT / f"T_FrontierAK_{part}_{name}.png"
            if not target.exists():
                convert(SOURCE / "Textures" / f"T_FrontierAK_{part}_{name}.png", target, 2048, normal=normal)
            write_texture_import(target, normal_map=normal)
            maps[name] = target
        result[f"M_FrontierAK_{part}"] = write_material(
            f"frontier_{part.lower()}", f"M_FrontierAK_{part}", albedo=maps["BaseColor"], orm=maps["ORM"],
            normal=maps["NormalGL"], vertex_colour=False)
    return result


def main():
    mats = materials()
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "Source" / "FrontierAK_R05_Game.blend"))
    static = {o.name: o for o in objects_in("05_STATIC_PICKUP")}
    chain = [static["SM_FrontierAK_R05_LOD1"], static["SM_FrontierAK_R05_LOD2"], static["SM_FrontierAK_R05_LOD3"]]
    hulls = sorted((o for o in objects_in("06_PICKUP_COLLISION") if o.type == "MESH"), key=lambda o: o.name)
    conv = Conversion("rifle")
    pieces = collection("HOLLOW_RIFLE_PARTS")
    split = {part: [] for part in ("Body", "Magazine", "Bolt")}
    split_normals = {part: [] for part in split}
    for lod, source in enumerate(chain, 1):
        labels = rigid_labels(source, bpy.data.objects[f"SK_FrontierAK_R05_LOD{lod}"])
        # Match the existing importer's mesh validation before separating components, so the custom-normal fan
        # sees the same repaired topology as the previous static export.
        source = copy_object(source, f"Rifle_source_lod{lod}", pieces)
        for part in split:
            piece, normals = split_part(source, labels, part, pieces)
            split[part].append(piece)
            split_normals[part].append(normals)
        counts = {part: sum(len(p.vertices) - 2 for p in split[part][-1].data.polygons) for part in split}
        print(f"[hollow] rifle source LOD{lod}: {counts}; total {sum(counts.values())}", flush=True)
    armature = bpy.data.objects["Armature"]
    conv.add_part("Body", Vector((0, 0, 0)), split["Body"], hulls)
    for part in ("Magazine", "Bolt"):
        pivot = armature.matrix_world @ armature.data.bones[part].head_local
        conv.add_part(part, pivot, split[part], [])
    contract = json.loads((SOURCE / "Exports" / "socket_contract.json").read_text(encoding="utf-8"))
    names = {"socket_muzzle": "muzzle", "socket_eject": "eject", "socket_grip_r": "grip_r", "socket_grip_l": "grip_l",
             "socket_ads": "sight"}
    grip = None
    for s in contract["sockets"]:
        forward = Vector(s["forward_blender"]).normalized()
        up = Vector(s["up_blender"]).normalized()
        side = up.cross(forward).normalized()
        # Socket frames: +X forward, +Z up (Godot +Y).
        frame = Matrix((forward, side, forward.cross(side))).transposed().to_4x4()
        frame.translation = Vector(s["position_blender_m"])
        conv.add_socket(names.get(s["name"], s["name"]), frame)
        if s["name"] == "socket_grip_r":
            grip = Vector(s["position_blender_m"])
    cameras = {o.name: o for o in bpy.data.objects if o.type == "CAMERA"}
    conv.add_socket("eye", Matrix.Translation(cameras["CAM_ADS"].matrix_world.translation))
    conv.finish(TURN_HELD @ Matrix.Translation(-grip))
    # Attachment mounts (models.json sockets): optics on the receiver's top cover, a light under the handguard.
    mount_sockets(conv, rail_x=0.07, under_x=0.33)
    for part, record in conv.parts.items():
        for o, normals in zip(record["lods"], split_normals[part]):
            # Deleting other components can reorder Blender's encoded normal fans. Restore their measured corner
            # directions after validation/transforms, once, to avoid shading changes on the low-detail magazine.
            if len(o.data.loops) != len(normals):
                raise RuntimeError(f"{o.name}: conversion changed the split corner count")
            o.data.normals_split_custom_set(normals)
            for attr in list(o.data.color_attributes):
                o.data.color_attributes.remove(attr)
    conv.export(ITEMS / "rifle.glb", mats)


main()
