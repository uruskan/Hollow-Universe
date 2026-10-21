"""Bounded, textured review of eight explicit authored-cover root candidates.

Run only in the root agent's serialized Blender window, for example:
  blender --background --factory-startup --python tools/blender/ground_cover_root_preview.py -- \
    --mesh fern/fern_v0_lod0 --view three-quarter --overlay \
    --output-directory .artifacts/environment-ownership/fern-v0-review-NEW

One mesh/view per process; no full catalog import, exports, source writes, admission,
network or provider calls. --check-only authenticates/validates without bpy or writes.
The actual selected game GLB supplies embedded PBR images/material settings. Source
POSITION/index/UV streams retain source triangle/corner identity. The preview uses
geometric face normals because those authenticated source meshes have no NORMAL
stream; it is not a Godot lighting or imported-normal parity proof. Images are scaled
only in memory, one at a time, and neither source geometry nor source images are saved.
"""
from __future__ import annotations

import argparse
import colorsys
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
import ground_cover_roots as roots

DEFAULT_PROPOSALS = ROOT / ".artifacts/environment-ownership/cover-root-proposals-r1"
DEFAULT_RECIPE = HERE / "data/ground_cover_root_review_candidates.json"
CATALOG = ROOT / "game/assets/surface/ground-cover/catalog.json"
MAX_GLB_BYTES = 64 * 1024 * 1024
MAX_IMAGE_BYTES = 16 * 1024 * 1024


def one(items, predicate, description):
    matches = [item for item in items if predicate(item)]
    roots.require(len(matches) == 1, "Missing or ambiguous " + description)
    return matches[0]


def checked_json(path, expected, budget):
    return roots.strict_json(roots.authenticated(path, expected, 16 * 1024 * 1024, budget))


def corner_position(proposal, corner):
    roots.integer(corner, 1, 3 * len(proposal["triangles"]), "support corner ID")
    triangle, local = divmod(corner - 1, 3)
    return roots.vector(proposal["positions"][proposal["triangles"][triangle][local]])


def logical_groups(candidate, proposal):
    """Expand only the recipe's explicit groups; never infer a parent from _wind."""
    groups = []
    for item in candidate["groups"]:
        triangles = []
        for start, end in item["triangleRanges"]:
            roots.integer(start, 0, len(proposal["triangles"]) - 1, "triangle range start")
            roots.integer(end, start, len(proposal["triangles"]) - 1, "triangle range end")
            triangles.extend(range(start, end + 1))
        corners = item["supportCornerIds"]
        roots.require(0 < len(corners) <= 16, "Candidate support count exceeds 16")
        points = [corner_position(proposal, corner) for corner in corners]
        centre = tuple(sum(point[a] for point in points) / len(points) for a in range(3))
        groups.append({"key": item["key"], "triangles": triangles,
                       "supportCornerIds": corners, "rootLocal": centre,
                       "rootOrigin": "mean of explicitly selected source support corners"})
    for ordinal, key in enumerate(candidate.get("basalProposalComponents", [])):
        component = one(proposal["groups"], lambda group: group["key"] == key, key)
        roots.require(not component["ambiguities"] and component["candidateRootLocal"] is not None,
                      "Explicit basal candidate changed or became ambiguous: " + key)
        groups.append({"key": f"basal-blade-{ordinal:02d}", "sourceComponent": key,
                       "triangles": component["triangles"],
                       "supportCornerIds": component["candidateRootZoneCornerIds"],
                       "rootLocal": roots.vector(component["candidateRootLocal"]),
                       "rootOrigin": "unreviewed lowest-boundary suggestion of explicitly named basal component"})
    roots.require(0 < len(groups) <= 32, "Preview logical group count exceeds 32")
    assigned = set()
    keys = set()
    for group in groups:
        roots.require(group["key"] not in keys, "Duplicate logical group key")
        keys.add(group["key"])
        triangles = group["triangles"]
        roots.require(triangles and len(set(triangles)) == len(triangles), "Empty/duplicate group triangles")
        roots.require(not assigned.intersection(triangles), "Triangle assigned to more than one logical plant")
        assigned.update(triangles)
        corners = group["supportCornerIds"]
        roots.require(0 < len(corners) <= 16 and len(set(corners)) == len(corners), "Invalid bounded support corners")
        owned_triangles = set(triangles)
        roots.require(all((corner - 1) // 3 in owned_triangles for corner in corners), "Support outside its logical plant")
        points = [corner_position(proposal, corner) for corner in corners]
        group["supportLocal"] = points
        group["supportRadiusMetres"] = max(math.dist(p, group["rootLocal"]) for p in points)
        group["supportHeightSpreadMetres"] = max(p[1] for p in points) - min(p[1] for p in points)
        hue = int(hashlib.sha256(group["key"].encode()).hexdigest()[:8], 16) / 0xffffffff
        group["overlayColour"] = colorsys.hsv_to_rgb(hue, 0.78, 0.95)
    roots.require(assigned == set(range(len(proposal["triangles"]))), "Logical groups must partition every source triangle")
    roots.require(sum(1 + len(group["supportCornerIds"]) for group in groups) <= 128,
                  "Annotation count exceeds 128")
    return groups


def read_glb(path, expected, budget):
    raw = roots.authenticated(path, expected, MAX_GLB_BYTES, budget)
    roots.require(len(raw) >= 28, "Truncated game GLB")
    magic, version, length = struct.unpack_from("<III", raw)
    roots.require((magic, version, length) == (0x46546c67, 2, len(raw)), "Invalid game GLB header")
    at, doc, binary = 12, None, None
    while at < len(raw):
        roots.require(at + 8 <= len(raw), "Truncated game GLB chunk header")
        size, kind = struct.unpack_from("<II", raw, at)
        at += 8
        roots.require(size % 4 == 0 and at + size <= len(raw), "Invalid game GLB chunk range")
        if kind == 0x4e4f534a:
            roots.require(doc is None and at == 20, "Unexpected game GLB JSON chunk")
            doc = roots.strict_json(raw[at:at + size])
        elif kind == 0x004e4942:
            roots.require(doc is not None and binary is None, "Unexpected game GLB binary chunk")
            binary = memoryview(raw)[at:at + size]
        else:
            raise roots.RootError("Unsupported game GLB chunk")
        at += size
    roots.require(doc is not None and binary is not None, "Incomplete game GLB")
    return doc, binary


def material_images(doc, binary, node_name):
    node = one(doc["nodes"], lambda n: n.get("name") == node_name, "exported game node")
    mesh = doc["meshes"][node["mesh"]]
    roots.require(len(mesh["primitives"]) == 1, "Preview expects one game material")
    material = doc["materials"][mesh["primitives"][0]["material"]]
    roots.require(not material.get("extensions") and material.get("alphaMode") == "MASK",
                  "Only the current plain masked PBR game material is supported")
    pbr = material["pbrMetallicRoughness"]
    images = {}
    for role, info in (("base", pbr.get("baseColorTexture")),
                       ("normal", material.get("normalTexture")),
                       ("roughness", pbr.get("metallicRoughnessTexture"))):
        roots.require(info is not None and info.get("texCoord", 0) == 0 and not info.get("extensions"),
                      "Expected existing untransformed UV0 material map: " + role)
        texture = doc["textures"][info["index"]]
        roots.require(not texture.get("extensions"), "Extended textures require explicit preview support")
        sampler = doc.get("samplers", [{}])[texture.get("sampler", 0)]
        roots.require(sampler.get("wrapS", 10497) == 10497 and sampler.get("wrapT", 10497) == 10497,
                      "Preview requires the current repeat sampler")
        image = doc["images"][texture["source"]]
        roots.require(image.get("mimeType") == "image/png" and "uri" not in image,
                      "Preview reads only receipt-bound embedded PNGs")
        view = doc["bufferViews"][image["bufferView"]]
        roots.require(view.get("buffer", 0) == 0 and not view.get("extensions"), "Unsupported embedded image view")
        start = roots.integer(view.get("byteOffset", 0), 0, len(binary), "image offset")
        count = roots.integer(view["byteLength"], 24, min(MAX_IMAGE_BYTES, len(binary) - start), "image bytes")
        data = bytes(binary[start:start + count])
        roots.require(data[:8] == b"\x89PNG\r\n\x1a\n" and data[12:16] == b"IHDR", "Not a PNG image")
        width, height = struct.unpack_from(">II", data, 16)
        roots.require(0 < width <= 2048 and 0 < height <= 2048, "Game image exceeds 2048px preview input ceiling")
        images[role] = {"data": data, "name": image.get("name", role), "sha256": roots.digest(data),
                        "sourceSize": [width, height]}
    return material, images


def authenticate(options):
    budget = roots.Budget(120)
    recipe_bytes = roots.bounded_read(options.recipe, 1024 * 1024)
    recipe = roots.strict_json(recipe_bytes)
    roots.require(recipe.get("schema") == "hollow.cover-root-preview-candidates.v1"
                  and recipe.get("runtimeAdmitted") is False, "Not an unadmitted candidate recipe")
    inspection = checked_json(options.proposals / "inspection.json", recipe["inspectionSha256"], budget)
    catalog = checked_json(CATALOG, recipe["catalogSha256"], budget)
    roots.require(inspection["catalogSha256"] == recipe["catalogSha256"]
                  and inspection["runtimeAdmitted"] is False, "Inspection/catalog identity mismatch")
    candidate = one(recipe["meshes"], lambda m: m["meshKey"] == options.mesh, "explicit preview candidate")
    row = one(inspection["meshes"], lambda m: m["meshKey"] == options.mesh, "inspected mesh")
    roots.require(row["sha256"] == candidate["proposalSha256"], "Inspection/proposal recipe mismatch")
    proposal_path = (options.proposals / row["proposal"]).resolve()
    roots.require(proposal_path.parent == options.proposals.resolve(), "Proposal path leaves inspection directory")
    proposal = checked_json(proposal_path, candidate["proposalSha256"], budget)
    roots.require(proposal["meshKey"] == options.mesh and proposal["runtimeAdmitted"] is False
                  and not proposal["catalogBillboard"], "Only unadmitted near/mid candidates are supported")
    family_key, node_name = options.mesh.split("/")
    family = one(catalog["families"], lambda f: f["key"] == family_key, "catalog family")
    level = one([level for variant in family["variants"] for level in variant["levels"]],
                lambda level: level["node"] == node_name, "catalog source level")
    receipts = {Path(entry["path"]).resolve(): entry["sha256"] for entry in family["inputs"]}
    source_path = one(list(receipts), lambda p: p.name.endswith("_nonUE.gltf"), "source glTF")
    doc = checked_json(source_path, proposal["source"]["gltfSha256"], budget)
    roots.require(receipts[source_path] == proposal["source"]["gltfSha256"]
                  and family["archiveSha256"] == proposal["source"]["archiveSha256"], "Source receipt mismatch")
    roots.require(len(doc["buffers"]) <= 4, "Source buffer count exceeds four")
    buffers, buffer_receipts = [], []
    for entry in doc["buffers"]:
        uri = entry["uri"]
        path = (source_path.parent / uri).resolve()
        roots.require(path in receipts, "Source buffer is absent from existing catalog receipts")
        remaining = roots.MAX_SOURCE_BYTES - sum(map(len, buffers))
        buffers.append(roots.authenticated(path, receipts[path], remaining, budget))
        buffer_receipts.append({"uri": uri, "sha256": receipts[path]})
    roots.require(sum(map(len, buffers)) <= roots.MAX_SOURCE_BYTES, "Aggregate source buffers exceed 64MiB")
    source = {"archiveSha256": family["archiveSha256"], "gltfSha256": receipts[source_path],
              "buffers": buffer_receipts}
    node_index = proposal["source"]["nodeIndex"]
    roots.require(doc["nodes"][node_index]["name"] == level["sourceNode"], "Source node selection changed")
    mesh = roots.load_mesh(doc, buffers, node_index, source, budget)
    roots.require(mesh.geometry_sha256 == candidate["geometrySha256"] == proposal["geometrySha256"],
                  "Source geometry/attributes do not reproduce the exact proposal")
    roots.require(len(mesh.positions) <= 6000 and len(mesh.triangles) <= 3000, "Single preview mesh budget exceeded")
    primitive = doc["meshes"][mesh.source["meshIndex"]]["primitives"][0]
    uv, receipt = roots.accessor(doc, buffers, primitive["attributes"]["TEXCOORD_0"], 6000)
    roots.require(receipt == proposal["attributes"]["TEXCOORD_0"]
                  and receipt["componentType"] == 5126 and receipt["type"] == "VEC2"
                  and not receipt["normalized"], "UV0 does not match the source proposal")
    roots.require("NORMAL" not in primitive["attributes"], "Source normal handling needs explicit preview review")
    groups = logical_groups(candidate, proposal)
    game_path = ROOT / "game" / family["model"].removeprefix("res://")
    roots.require(game_path.resolve().parent == CATALOG.parent.resolve(), "Unexpected game model path")
    game_doc, game_binary = read_glb(game_path, family["outputSha256"], budget)
    material, images = material_images(game_doc, game_binary, node_name)
    return {"mesh": mesh, "uv": uv, "groups": groups, "material": material, "images": images,
            "receipt": {"schema": "hollow.cover-root-textured-preview.v1", "runtimeAdmitted": False,
                        "status": "unreviewed-candidate; no export/import/contact certification",
                        "meshKey": options.mesh, "proposalSha256": candidate["proposalSha256"],
                        "geometrySha256": mesh.geometry_sha256, "source": mesh.source,
                        "recipeSha256": roots.digest(recipe_bytes), "catalogSha256": recipe["catalogSha256"],
                        "inspectionSha256": recipe["inspectionSha256"], "gameModelSha256": family["outputSha256"],
                        "groups": groups, "billboardPlan": recipe["billboardPlan"],
                        "limitations": ["Preview textures are reduced-resolution derivatives in memory.",
                                        "Source face normals; not Godot imported-normal or lighting parity.",
                                        "Original source coordinates and Y=0 reference; no root snapping or terrain conformance.",
                                        "Root and support markers appear only in the optional annotation render."]}}


def blender_vector(point):
    return point[0], -point[2], point[1]


def build_material(bpy, prepared, destination, texture_size):
    material = bpy.data.materials.new("Existing PBR and alpha; candidate overlay disabled")
    material.use_nodes = True
    material.use_backface_culling = not prepared["material"].get("doubleSided", False)
    material.surface_render_method = 'DITHERED'
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    output = nodes.new('ShaderNodeOutputMaterial')
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    image_nodes = {}
    receipts = []
    for role, entry in prepared["images"].items():
        path = destination / ("input-" + role + ".png")
        with path.open("xb") as stream:
            stream.write(entry["data"])
        image = bpy.data.images.load(str(path), check_existing=False)
        image.alpha_mode = 'STRAIGHT'
        image.colorspace_settings.name = 'sRGB' if role == 'base' else 'Non-Color'
        width, height = image.size
        scale = min(1.0, texture_size / max(width, height))
        image.scale(max(1, round(width * scale)), max(1, round(height * scale)))
        texture = nodes.new('ShaderNodeTexImage')
        texture.image = image
        texture.interpolation = 'Linear'
        texture.extension = 'REPEAT'
        image_nodes[role] = texture
        receipts.append({"role": role, "name": entry["name"], "embeddedPngSha256": entry["sha256"],
                         "sourceSize": entry["sourceSize"], "previewSize": list(image.size)})
    pbr = prepared["material"]["pbrMetallicRoughness"]
    factor = pbr.get("baseColorFactor", [1, 1, 1, 1])
    base = nodes.new('ShaderNodeMixRGB')
    base.blend_type = 'MULTIPLY'
    base.inputs[0].default_value = 1
    base.inputs[2].default_value = tuple(factor)
    links.new(image_nodes['base'].outputs['Color'], base.inputs[1])
    colour = nodes.new('ShaderNodeVertexColor')
    colour.layer_name = 'candidate_logical_group'
    tint = nodes.new('ShaderNodeMixRGB')
    tint.inputs[0].default_value = 0
    links.new(base.outputs[0], tint.inputs[1])
    links.new(colour.outputs['Color'], tint.inputs[2])
    links.new(tint.outputs[0], shader.inputs['Base Color'])
    alpha = nodes.new('ShaderNodeMath')
    alpha.operation = 'MULTIPLY'
    alpha.inputs[1].default_value = factor[3]
    links.new(image_nodes['base'].outputs['Alpha'], alpha.inputs[0])
    cutout = nodes.new('ShaderNodeMath')
    cutout.operation = 'LESS_THAN'
    cutout.inputs[1].default_value = prepared["material"].get("alphaCutoff", 0.5)
    links.new(alpha.outputs[0], cutout.inputs[0])
    keep = nodes.new('ShaderNodeMath')
    keep.operation = 'SUBTRACT'
    keep.inputs[0].default_value = 1
    links.new(cutout.outputs[0], keep.inputs[1])
    links.new(keep.outputs[0], shader.inputs['Alpha'])
    normal = nodes.new('ShaderNodeNormalMap')
    normal.inputs['Strength'].default_value = prepared["material"]["normalTexture"].get('scale', 1)
    links.new(image_nodes['normal'].outputs['Color'], normal.inputs['Color'])
    links.new(normal.outputs['Normal'], shader.inputs['Normal'])
    separate = nodes.new('ShaderNodeSeparateColor')
    separate.mode = 'RGB'
    links.new(image_nodes['roughness'].outputs['Color'], separate.inputs['Color'])
    for channel, name, factor_name in [('Green', 'Roughness', 'roughnessFactor'), ('Blue', 'Metallic', 'metallicFactor')]:
        multiply = nodes.new('ShaderNodeMath')
        multiply.operation = 'MULTIPLY'
        multiply.inputs[1].default_value = pbr.get(factor_name, 1)
        links.new(separate.outputs[channel], multiply.inputs[0])
        links.new(multiply.outputs[0], shader.inputs[name])
    return material, tint, receipts


def simple_material(bpy, name, colour, emission=False):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*colour, 1)
    material.use_nodes = True
    shader = material.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*colour, 1)
    shader.inputs['Roughness'].default_value = 0.85
    if emission:
        shader.inputs['Emission Color'].default_value = (*colour, 1)
        shader.inputs['Emission Strength'].default_value = 1
    return material


def render_preview(options, prepared):
    import bpy
    from mathutils import Vector
    roots.require(bpy.app.background, "Use a dedicated background Blender process, never a live editor")
    destination = options.output_directory.resolve()
    allowed = (ROOT / '.artifacts').resolve()
    roots.require(destination.is_relative_to(allowed) and destination != allowed,
                  "Preview output must be a new subdirectory under this project's .artifacts")
    roots.require(not destination.exists(), "Preview output already exists; use a fresh directory")
    destination.mkdir(parents=True, exist_ok=False)
    started = time.monotonic()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        scene = bpy.context.scene
        scene.render.engine = 'CYCLES'
        scene.cycles.device = 'CPU'
        scene.cycles.samples = options.samples
        scene.cycles.use_denoising = False
        scene.cycles.max_bounces = 4
        # The cotton head contains hundreds of small cutout cards. A low default
        # transparent-depth limit can turn their fully transparent regions dark.
        scene.cycles.transparent_max_bounces = 256
        scene.render.threads_mode = 'FIXED'
        scene.render.threads = 2
        scene.render.resolution_x = scene.render.resolution_y = options.tile_size
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        scene.render.image_settings.color_mode = 'RGBA'
        scene.render.use_file_extension = True
        scene.render.film_transparent = False
        scene.view_settings.view_transform = 'Standard'
        scene.view_settings.look = 'None'
        scene.world = bpy.data.worlds.new('Neutral review world')
        scene.world.use_nodes = True
        scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.10, 0.13, 0.17, 1)
        scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.5
        material, overlay, image_receipts = build_material(bpy, prepared, destination, options.texture_size)
        mesh = bpy.data.meshes.new(options.mesh)
        points = [blender_vector(point) for point in prepared['mesh'].positions]
        mesh.from_pydata(points, [], prepared['mesh'].triangles)
        mesh.update()
        roots.require(len(mesh.polygons) == len(prepared['mesh'].triangles), "Blender altered source triangle count")
        uv = mesh.uv_layers.new(name='UV0')
        colours = mesh.color_attributes.new(name='candidate_logical_group', type='FLOAT_COLOR', domain='CORNER')
        by_triangle = {}
        for group in prepared['groups']:
            for triangle in group['triangles']:
                by_triangle[triangle] = group['overlayColour']
        for polygon in mesh.polygons:
            roots.require(tuple(polygon.vertices) == tuple(prepared['mesh'].triangles[polygon.index]),
                          "Blender altered source triangle/corner order")
            polygon.use_smooth = False
            for loop in polygon.loop_indices:
                u, v = prepared['uv'][mesh.loops[loop].vertex_index]
                uv.data[loop].uv = (u, 1 - v)
                colours.data[loop].color = (*by_triangle[polygon.index], 1)
        plant = bpy.data.objects.new(options.mesh, mesh)
        scene.collection.objects.link(plant)
        plant.data.materials.append(material)
        ground_material = simple_material(bpy, 'Y=0 metre reference', (0.11, 0.095, 0.075))
        bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
        bpy.context.object.name = 'Original source Y=0 reference; no root snap'
        bpy.context.object.data.materials.append(ground_material)
        for location, energy, size in [((2, -3, 4), 450, 3), ((-2, 1, 2), 150, 2)]:
            data = bpy.data.lights.new('Review softbox', 'AREA')
            data.energy, data.shape, data.size = energy, 'DISK', size
            light = bpy.data.objects.new(data.name, data)
            scene.collection.objects.link(light)
            light.location = location
            light.rotation_euler = (Vector((0, 0, 0.3)) - light.location).to_track_quat('-Z', 'Y').to_euler()
        camera_data = bpy.data.cameras.new('Review camera')
        camera = bpy.data.objects.new(camera_data.name, camera_data)
        scene.collection.objects.link(camera)
        scene.camera = camera
        camera_data.type = 'ORTHO'
        camera_data.clip_start, camera_data.clip_end = 0.001, 100
        lo = Vector(tuple(min(point[a] for point in points) for a in range(3)))
        hi = Vector(tuple(max(point[a] for point in points) for a in range(3)))
        target = (lo + hi) * 0.5
        if options.view == 'roots':
            target = Vector(blender_vector(prepared['groups'][0]['rootLocal']))
            target.z = 0.035
            direction = Vector((0, -1, 0.20)).normalized()
            camera_data.ortho_scale = 0.34
        else:
            direction = {'front': Vector((0, -1, 0.12)), 'side': Vector((1, 0, 0.12)),
                         'three-quarter': Vector((1, -1.5, 0.65))}[options.view].normalized()
        camera.location = target + direction * 5
        camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
        if options.view != 'roots':
            inverse = camera.rotation_euler.to_quaternion().inverted()
            projected = [inverse @ (Vector(point) - target) for point in points]
            camera_data.ortho_scale = 1.22 * max(0.2, *(max(p[a] for p in projected) - min(p[a] for p in projected) for a in (0, 1)))
        caption_data = bpy.data.curves.new('Unreviewed caption', 'FONT')
        caption_data.body = (f'UNREVIEWED {options.mesh}\n{options.view}; ground Y=0; '
                             f'frame width {camera_data.ortho_scale:.3f}m')
        caption_data.size = camera_data.ortho_scale * 0.018
        caption = bpy.data.objects.new(caption_data.name, caption_data)
        scene.collection.objects.link(caption)
        caption.parent = camera
        caption.location = (-camera_data.ortho_scale * 0.48, -camera_data.ortho_scale * 0.44, -1)
        caption.data.materials.append(simple_material(bpy, 'Caption', (0.9, 0.94, 1), True))
        outputs = []
        def capture(name):
            roots.require(time.monotonic() - started < 300, 'Preview soft deadline reached between renders')
            path = destination / name
            roots.require(not path.exists(), 'Refusing preview overwrite')
            scene.render.filepath = str(path)
            bpy.ops.render.render(write_still=True)
            data = roots.bounded_read(path, 16 * 1024 * 1024)
            outputs.append({'file': name, 'sha256': roots.digest(data), 'bytes': len(data)})
        capture('textured.png')
        if options.overlay:
            overlay.inputs[0].default_value = 0.5
            for group in prepared['groups']:
                marker_material = simple_material(bpy, 'Candidate ' + group['key'], group['overlayColour'], True)
                for point, radius in [(group['rootLocal'], 0.006)] + [(p, 0.0025) for p in group['supportLocal']]:
                    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=radius,
                                                       location=blender_vector(point))
                    bpy.context.object.name = 'UNREVIEWED support ' + group['key']
                    bpy.context.object.data.materials.append(marker_material)
            caption_data.body += '\n50% group tint; large dot=centre, small dot=support'
            capture('candidate-groups.png')
        receipt = prepared['receipt']
        receipt.update({'outputs': outputs, 'images': image_receipts, 'view': options.view,
                        'sourceGameMaterial': prepared['material'], 'transparentBounceLimit': 256,
                        'tileSize': options.tile_size, 'textureSizeCeiling': options.texture_size,
                        'renderer': 'Cycles CPU, 2 threads', 'samples': options.samples,
                        'blenderVersion': bpy.app.version_string, 'elapsedSeconds': time.monotonic() - started,
                        'cameraPositionBlender': list(camera.location), 'cameraTargetBlender': list(target),
                        'orthographicScaleMetres': camera_data.ortho_scale,
                        'previewToolSha256': roots.digest(roots.bounded_read(Path(__file__), 1024 * 1024))})
        with (destination / 'preview.json').open('xb') as stream:
            stream.write(roots.canonical(receipt))
        print(json.dumps({'status': receipt['status'], 'output': str(destination), 'renders': outputs}), flush=True)
    finally:
        # Only a dedicated background process is allowed above. Retire every owned
        # native datablock even on image loading, render or receipt-write failure.
        bpy.ops.wm.read_factory_settings(use_empty=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mesh', required=True, help='Exact candidate meshKey, e.g. fern/fern_v0_lod0')
    parser.add_argument('--proposals', type=Path, default=DEFAULT_PROPOSALS)
    parser.add_argument('--recipe', type=Path, default=DEFAULT_RECIPE)
    parser.add_argument('--output-directory', type=Path)
    parser.add_argument('--view', choices=['three-quarter', 'front', 'side', 'roots'], default='three-quarter')
    parser.add_argument('--overlay', action='store_true', help='Also render group tint and explicit support markers')
    parser.add_argument('--texture-size', type=int, default=512)
    parser.add_argument('--tile-size', type=int, default=640)
    parser.add_argument('--samples', type=int, default=16)
    parser.add_argument('--check-only', action='store_true', help='Authenticate inputs/groups only; no bpy, files or renders')
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    options = parser.parse_args(argv)
    roots.require(256 <= options.texture_size <= 1024 and 384 <= options.tile_size <= 1024
                  and 8 <= options.samples <= 32, 'Preview size/sample budget exceeded')
    roots.require(options.check_only or options.output_directory is not None, 'A fresh output directory is required')
    prepared = authenticate(options)
    if options.check_only:
        print(json.dumps({'status': prepared['receipt']['status'], 'mesh': options.mesh,
                          'vertices': len(prepared['mesh'].positions), 'triangles': len(prepared['mesh'].triangles),
                          'groups': len(prepared['groups']), 'materialMaps': len(prepared['images']),
                          'proposalSha256': prepared['receipt']['proposalSha256']}), flush=True)
    else:
        render_preview(options, prepared)


if __name__ == '__main__':
    main()
