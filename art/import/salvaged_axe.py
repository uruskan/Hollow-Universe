"""The user's Woodsman heavy-duty hatchet -> the Salvaged Axe held item.

Extracts only the axe mesh and its three PBR images from the library GLB. The
display scene's stump, ground and presentation transforms never enter the game.
Source-local +X is the blade direction and +Z runs up the handle. The 0.62 m
tool has its palm socket 0.11 m above the butt, in Hollow's +X-forward frame.

    blender -b --factory-startup --python art/import/salvaged_axe.py
    blender -b --factory-startup --python art/import/salvaged_axe.py -- --preview .artifacts/salvaged-axe-model.png

The original file is read-only. Outputs are limited to salvaged-axe.glb and
salvaged-axe/ beneath game/assets/items, plus the optional preview path.
"""
import argparse
import hashlib
import json
import struct
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, log
from hollow.export import collection, convex_hull, decimated, export_glb, reset
from hollow.textures import convert, write_texture_import

SOURCE = Path("D:/GameDev/Assets/woodsman_collection_heavy_duty_hatchet_axe.glb")
OUT = ITEMS / "salvaged-axe"
HEIGHT = 0.62
GRIP_HEIGHT = 0.11
TEXTURE_SIZE = 4096
MATERIAL = "M_SalvagedAxe"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_source(path):
    raw = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", raw)
    if (magic, version, length) != (0x46546C67, 2, len(raw)):
        raise ValueError("Source is not a complete GLB 2 file")
    json_size, kind = struct.unpack_from("<II", raw, 12)
    if kind != 0x4E4F534A:
        raise ValueError("First GLB chunk is not JSON")
    document = json.loads(raw[20:20 + json_size])
    binary_size, kind = struct.unpack_from("<II", raw, 20 + json_size)
    if kind != 0x004E4942:
        raise ValueError("GLB has no BIN chunk")
    binary = memoryview(raw)[28 + json_size:28 + json_size + binary_size]
    return document, binary


def accessor(document, binary, index):
    record = document["accessors"][index]
    view = document["bufferViews"][record["bufferView"]]
    types = {5126: "<f4", 5125: "<u4", 5123: "<u2"}
    channels = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[record["type"]]
    dtype = np.dtype(types[record["componentType"]])
    offset = view.get("byteOffset", 0) + record.get("byteOffset", 0)
    stride = view.get("byteStride", channels * dtype.itemsize)
    return np.ndarray((record["count"], channels), dtype, binary, offset,
                      (stride, dtype.itemsize)).copy()


def material(document, binary, primitive):
    source = document["materials"][primitive["material"]]
    pbr = source["pbrMetallicRoughness"]
    maps = {
        "albedo": pbr["baseColorTexture"]["index"],
        "orm": pbr["metallicRoughnessTexture"]["index"],
        "normal": source["normalTexture"]["index"],
    }
    if source["occlusionTexture"]["index"] != maps["orm"]:
        raise ValueError("Expected combined occlusion/roughness/metallic image")
    image_info = {}
    OUT.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="hollow-salvaged-axe-") as scratch:
        for key, texture_index in maps.items():
            image = document["images"][document["textures"][texture_index]["source"]]
            view = document["bufferViews"][image["bufferView"]]
            start = view.get("byteOffset", 0)
            original = binary[start:start + view["byteLength"]]
            path = Path(scratch) / (key + (".png" if image["mimeType"] == "image/png" else ".jpg"))
            path.write_bytes(original)
            source_image = bpy.data.images.load(str(path), check_existing=False)
            image_info[key] = {"sourceSize": list(source_image.size),
                               "sourceSha256": hashlib.sha256(original).hexdigest()}
            bpy.data.images.remove(source_image)
            target = OUT / f"{key}.png"
            convert(path, target, TEXTURE_SIZE, normal=key == "normal")
            write_texture_import(target, normal_map=key == "normal")

    tres = OUT / "salvaged-axe.tres"
    tres.write_text('''[gd_resource type="ORMMaterial3D" load_steps=4 format=3]

[ext_resource type="Texture2D" path="res://assets/items/salvaged-axe/albedo.png" id="1"]
[ext_resource type="Texture2D" path="res://assets/items/salvaged-axe/orm.png" id="2"]
[ext_resource type="Texture2D" path="res://assets/items/salvaged-axe/normal.png" id="3"]

[resource]
resource_name = "M_SalvagedAxe"
vertex_color_use_as_albedo = false
albedo_texture = ExtResource("1")
orm_texture = ExtResource("2")
ao_enabled = true
normal_enabled = true
normal_texture = ExtResource("3")
texture_filter = 5
''' + f"cull_mode = {2 if source.get('doubleSided', False) else 0}\n", encoding="utf-8", newline="\n")

    # The Blender material is for the offline preview; exported GLB only carries
    # its name, which the .import remaps to the Godot PBR material above.
    mat = bpy.data.materials.new(MATERIAL)
    mat.use_nodes = True
    mat.use_backface_culling = not source.get("doubleSided", False)
    tree = mat.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    nodes = {}
    for key in maps:
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(str(OUT / f"{key}.png"))
        if key != "albedo":
            node.image.colorspace_settings.name = "Non-Color"
        nodes[key] = node
    tree.links.new(nodes["albedo"].outputs["Color"], bsdf.inputs["Base Color"])
    separate = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(nodes["orm"].outputs["Color"], separate.inputs["Color"])
    tree.links.new(separate.outputs["Green"], bsdf.inputs["Roughness"])
    tree.links.new(separate.outputs["Blue"], bsdf.inputs["Metallic"])
    normal = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(nodes["normal"].outputs["Color"], normal.inputs["Color"])
    tree.links.new(normal.outputs["Normal"], bsdf.inputs["Normal"])
    return mat, tres, image_info


def add_socket(target, name, point):
    obj = bpy.data.objects.new("socket_" + name, None)
    target.objects.link(obj)
    obj.location = point
    return obj


def preview(path, body, objects):
    for obj in objects:
        obj.hide_render = obj != body
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("Salvaged axe preview")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.07, 0.085, 0.11, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.45
    centre = Vector((0.055, 0, 0.2))
    cam_data = bpy.data.cameras.new("Preview camera")
    cam = bpy.data.objects.new("Preview camera", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.7, -1.7, 0.6)
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 0.76
    scene.camera = cam
    for name, location, power, size, colour in (
        ("Key", (0.5, -1.0, 1.0), 180, 1.0, (1.0, 0.91, 0.79)),
        ("Fill", (-0.8, -0.4, 0.35), 80, 1.2, (0.75, 0.85, 1.0)),
        ("Rim", (0.4, 0.7, 0.8), 210, 0.7, (1.0, 0.93, 0.85)),
    ):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.size, data.color = power, size, colour
        lamp = bpy.data.objects.new(name, data)
        scene.collection.objects.link(lamp)
        lamp.location = location
        lamp.rotation_euler = (centre - lamp.location).to_track_quat("-Z", "Y").to_euler()
    path.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(path.resolve())
    bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=SOURCE)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    reset()
    document, binary = read_source(args.source)
    mesh = next(m for m in document["meshes"] if m["name"] == "axe.002_low_axe_0")
    if len(mesh["primitives"]) != 1:
        raise ValueError("Unexpected axe mesh primitives; inspect source before rebuilding")
    primitive = mesh["primitives"][0]
    if primitive.get("mode", 4) != 4:
        raise ValueError("Expected triangle primitive")
    attributes = primitive["attributes"]
    points = accessor(document, binary, attributes["POSITION"])
    normals = accessor(document, binary, attributes["NORMAL"])
    uvs = accessor(document, binary, attributes["TEXCOORD_0"])
    indices = accessor(document, binary, primitive["indices"]).reshape(-1, 3)
    source_min, source_max = points.min(axis=0), points.max(axis=0)
    scale = HEIGHT / float(source_max[2] - source_min[2])
    grip_z = float(source_min[2]) + GRIP_HEIGHT / scale
    handle = points[np.abs(points[:, 2] - grip_z) < 0.035]
    if len(handle) < 4:
        raise ValueError("No lower-handle cross section at the intended grip")
    grip = np.array([(handle[:, 0].min() + handle[:, 0].max()) * 0.5,
                     (handle[:, 1].min() + handle[:, 1].max()) * 0.5, grip_z])
    points = (points - grip) * scale

    target = collection("HOLLOW_EXPORT")
    data = bpy.data.meshes.new("Body_lod0")
    data.from_pydata(points.tolist(), [], indices.tolist())
    data.update()
    layer = data.uv_layers.new(name="UVMap")
    # Raw glTF texture coordinates count downward; Blender's UV V counts upward.
    for loop in data.loops:
        uv = uvs[loop.vertex_index]
        layer.data[loop.index].uv = (float(uv[0]), 1.0 - float(uv[1]))
    for face in data.polygons:
        face.use_smooth = True
    data.normals_split_custom_set_from_vertices(normals.tolist())
    body = bpy.data.objects.new("Body_lod0", data)
    target.objects.link(body)
    mat, tres, images = material(document, binary, primitive)
    data.materials.append(mat)
    lods = [body] + [decimated(body, f"Body_lod{i}", ratio, target)
                    for i, ratio in enumerate((0.5, 0.15), 1)]
    hull = convex_hull([body], "Body_hull0-convcolonly", target)
    impact = points[points[:, 0].argmax()].copy()
    impact[1] = 0
    sockets = [add_socket(target, "grip_r", (0, 0, 0)), add_socket(target, "impact", impact)]
    objects = lods + [hull] + sockets
    output = export_glb(ITEMS / "salvaged-axe.glb", objects, {MATERIAL: tres})

    def xyz(v):
        return [round(float(x), 7) for x in v]

    counts = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in lods + [hull]}
    receipt = {
        "asset": "salvaged-axe", "sourceFile": str(args.source.resolve()),
        "originalTitle": "Woodsman Collection Heavy Duty Hatchet Axe",
        "originalAuthor": "Gerardo Justel",
        "originalListing": "https://www.fab.com/listings/24947c95-1253-44a9-9b1f-0d057ed1ff72",
        "sourceGenerator": document.get("asset", {}).get("generator"),
        "sourceSha256": digest(args.source), "sourceMesh": mesh["name"],
        "excludedMeshes": [m["name"] for m in document["meshes"] if m != mesh],
        "sourceLocalBounds": {"min": xyz(source_min), "max": xyz(source_max)},
        "sourceLocalGrip": xyz(grip), "scale": scale,
        "dimensionsMetres": {"height": HEIGHT, "width": float(np.ptp(points[:, 0])),
                             "thickness": float(np.ptp(points[:, 1]))},
        "godotFrame": "+X blade forward; +Y shaft up; +Z across thickness; right palm at origin",
        "gripHeightAboveButtMetres": GRIP_HEIGHT,
        "godotBounds": {"min": xyz([points[:, 0].min(), points[:, 2].min(), -points[:, 1].max()]),
                        "max": xyz([points[:, 0].max(), points[:, 2].max(), -points[:, 1].min()])},
        "triangleCounts": counts, "textures": images,
        "textureSize": TEXTURE_SIZE, "normalConvention": "OpenGL (source glTF), renormalized after filtering",
        "materialDoubleSided": document["materials"][primitive["material"]].get("doubleSided", False),
        "geometryValidation": "Explicit LOD0/1/2, convex hull, grip and impact sockets; no embedded images",
        "engineValidation": "Performed separately by the integration workflow",
        "licence": "Owner confirmed D:/GameDev/Assets paid and fully licensed for game use on 2026-10-15; see docs/handoff-ground.md and game/assets/THIRD_PARTY.md. No redistribution licence is inferred.",
        "outputsSha256": {str(p.relative_to(ITEMS)).replace('\\', '/'): digest(p)
                          for p in [output, tres] + sorted(OUT.glob("*.png"))},
    }
    (OUT / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8", newline="\n")
    log(json.dumps(receipt, indent=2))
    if args.preview:
        preview(args.preview, body, objects)


if __name__ == "__main__":
    main()
