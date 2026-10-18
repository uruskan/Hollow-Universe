"""Verify and admit the owner's Compound Utilities R02 through a reproducible local adapter.

The legacy delivery has a closed SHA256 roster, not a game-dev canonical manifest.
No source is modified. Run without --install for a no-write admission plan.
"""
import argparse
import copy
import hashlib
import itertools
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
NAMES = ["large-furnace", "wooden-barricade", "wood-compound-wall", "wood-compound-gate",
         "stone-compound-wall", "stone-compound-gate", "windmill", "sam-site",
         "wood-spike-barricade", "metal-barricade", "auto-turret", "wooden-ladder"]


def digest(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def point(p):
    # Collision specifications, design pivots and sockets precede the delivery's
    # +90-degree Z source conversion. They use X across, -Y forward, Z up.
    # The GLB's final wrapper reverses that conversion, then uses Y up.
    return [round(float(p[0]), 6), round(float(p[2]), 6), round(-float(p[1]), 6)]


def glb(source):
    data = source.read_bytes()
    magic, version, length = struct.unpack_from("<III", data)
    assert magic == 0x46546C67 and version == 2 and length == len(data), source
    count, kind = struct.unpack_from("<II", data, 12)
    assert kind == 0x4E4F534A
    size, binary_kind = struct.unpack_from("<II", data, 20 + count)
    assert binary_kind == 0x004E4942
    return json.loads(data[20:20+count]), data[28+count:28+count+size]


def packed_glb(doc, binary):
    encoded = json.dumps(doc, separators=(",", ":")).encode("utf8")
    encoded += b" " * (-len(encoded) % 4)
    binary += b"\0" * (-len(binary) % 4)
    return (struct.pack("<III", 0x46546C67, 2, 28+len(encoded)+len(binary))
            + struct.pack("<II", len(encoded), 0x4E4F534A) + encoded
            + struct.pack("<II", len(binary), 0x004E4942) + binary)


def res(path):
    return "res://" + path.relative_to(ROOT / "game").as_posix()


def material_text(material, textures):
    """Translate glTF's linear PBR channels explicitly; textures remain original PNG bytes."""
    pbr = material.get("pbrMetallicRoughness", {})
    refs, properties = {}, []

    def texture(property_name, info):
        if info is None:
            return False
        if info.get("texCoord", 0) != 0 or info.get("extensions"):
            raise ValueError("Unexpected UV mapping in material " + material["name"])
        path = textures[info["index"]]
        identifier = refs.setdefault(path, str(len(refs) + 1))
        properties.append(f'{property_name} = ExtResource("{identifier}")')
        return True

    properties += ["resource_name = " + json.dumps(material["name"]),
                   "cull_mode = " + ("2" if material.get("doubleSided", False) else "0"),
                   "texture_filter = 5", "vertex_color_use_as_albedo = false"]
    colour = pbr.get("baseColorFactor", [1, 1, 1, 1])
    properties += ["albedo_color = Color(" + ", ".join(format(x, ".9g") for x in colour) + ")",
                   f'metallic = {pbr.get("metallicFactor", 1):.9g}',
                   f'roughness = {pbr.get("roughnessFactor", 1):.9g}']
    texture("albedo_texture", pbr.get("baseColorTexture"))
    if texture("normal_texture", material.get("normalTexture")):
        properties += ["normal_enabled = true", f'normal_scale = {material["normalTexture"].get("scale", 1):.9g}']
    if texture("ao_texture", material.get("occlusionTexture")):
        if material["occlusionTexture"].get("strength", 1) != 1:
            raise ValueError("Non-unit occlusion strength requires an explicit material adapter")
        properties += ["ao_enabled = true", "ao_texture_channel = 0", "ao_light_affect = 0.0"]
    if texture("roughness_texture", pbr.get("metallicRoughnessTexture")):
        texture("metallic_texture", pbr["metallicRoughnessTexture"])
        properties += ["roughness_texture_channel = 1", "metallic_texture_channel = 2"]
    emission = material.get("emissiveFactor", [0, 0, 0])
    if texture("emission_texture", material.get("emissiveTexture")) or any(emission):
        properties += ["emission_enabled = true", "emission = Color(" + ", ".join(format(x, ".9g") for x in emission) + ", 1)"]
    alpha = material.get("alphaMode", "OPAQUE")
    if alpha == "BLEND":
        properties.append("transparency = 1")
    elif alpha == "MASK":
        properties += ["transparency = 2", f'alpha_scissor_threshold = {material.get("alphaCutoff", .5):.9g}']
    if material.get("extensions"):
        raise ValueError("Unsupported material extension: " + material["name"])
    lines = [f'[gd_resource type="StandardMaterial3D" load_steps={len(refs)+1} format=3]', ""]
    lines += [f'[ext_resource type="Texture2D" path="{path}" id="{identifier}"]' for path, identifier in refs.items()]
    return ("\n".join(lines + ["", "[resource]"] + properties) + "\n").encode("utf-8")


def scene_import(mappings):
    text = '[remap]\nimporter="scene"\ntype="PackedScene"\n\n[params]\n'
    text += ('nodes/use_name_suffixes=true\nmeshes/generate_lods=true\nmeshes/create_shadow_meshes=true\n'
             'meshes/light_baking=0\nanimation/import=false\n_subresources={\n"materials": {\n')
    text += ",\n".join(json.dumps(name) + ': {\n"use_external/enabled": true,\n"use_external/path": '
                         + json.dumps(path) + "\n}" for name, path in mappings.items())
    return (text + "\n}\n}\n").encode("utf-8")


def texture_import(normal):
    return ('[remap]\nimporter="texture"\ntype="CompressedTexture2D"\n\n[params]\n'
            'compress/mode=2\ncompress/high_quality=false\n'
            f'compress/normal_map={1 if normal else 2}\n'
            'mipmaps/generate=true\nprocess/normal_map_invert_y=false\n'
            'process/size_limit=0\ndetect_3d/compress_to=0\n').encode("utf-8")


def merged_import(path, settings):
    """Godot owns import metadata; update only this adapter's explicit parameter assignments."""
    if not path.exists():
        return settings
    existing = path.read_text(encoding="utf-8")
    if "[params]" not in existing:
        raise SystemExit("Invalid Godot import sidecar: " + str(path))

    def assignments(text):
        result, lines, depth, quoted, escaped = {}, [], 0, False, False
        for line in text.splitlines():
            lines.append(line)
            for char in line:
                if quoted:
                    if escaped: escaped = False
                    elif char == "\\": escaped = True
                    elif char == '"': quoted = False
                elif char == '"': quoted = True
                elif char in "[{(": depth += 1
                elif char in "]})": depth -= 1
            if depth == 0 and not quoted:
                value = "\n".join(lines).strip()
                lines = []
                if value and "=" in value:
                    result[value.split("=", 1)[0].strip()] = value
        if lines or depth or quoted:
            raise SystemExit("Incomplete Godot import sidecar: " + str(path))
        return result
    header, prior = existing.split("[params]", 1)
    values = assignments(prior)
    values.update(assignments(settings.decode("utf-8").split("[params]", 1)[1]))
    return (header + "[params]\n\n" + "\n".join(values.values()) + "\n").encode("utf-8")


def adapted_glb(source, destination, outputs, texture_roles):
    doc, binary = glb(source)
    image_paths = []
    image_views = set()
    for image in doc.get("images", []):
        if image.get("mimeType") != "image/png" or "bufferView" not in image:
            raise ValueError("Expected embedded PNG: " + str(source))
        image_views.add(image["bufferView"])
        view = doc["bufferViews"][image["bufferView"]]
        offset = view.get("byteOffset", 0)
        data = binary[offset:offset+view["byteLength"]]
        assert data.startswith(b"\x89PNG\r\n\x1a\n")
        path = destination / "textures" / (hashlib.sha256(data).hexdigest() + ".png")
        outputs[path] = data
        image_paths.append(path)
    texture_paths = [image_paths[t["source"]] for t in doc.get("textures", [])]
    mappings = {}
    for material in doc.get("materials", []):
        name = material["name"]
        if not name.replace("_", "").replace("-", "").isalnum():
            raise ValueError("Unsafe material name " + name)
        path = destination / "materials" / (name + ".tres")
        outputs[path] = material_text(material, [res(p) for p in texture_paths])
        mappings[name] = res(path)
        for slot in ("normalTexture", "occlusionTexture", "emissiveTexture"):
            if slot in material:
                texture_roles.setdefault(texture_paths[material[slot]["index"]], set()).add("normal" if slot == "normalTexture" else "colour")
        for info in material.get("pbrMetallicRoughness", {}).values():
            if isinstance(info, dict) and "index" in info:
                texture_roles.setdefault(texture_paths[info["index"]], set()).add("colour")
        # Engine remaps own the complete material; keep a valid texture-free glTF fallback.
        for slot in ("normalTexture", "occlusionTexture", "emissiveTexture"):
            material.pop(slot, None)
        for slot in ("baseColorTexture", "metallicRoughnessTexture"):
            material.get("pbrMetallicRoughness", {}).pop(slot, None)
    for field in ("images", "textures", "samplers"):
        doc.pop(field, None)
    # Remove the embedded images physically as well, preserving all vertex/index bytes and accessor layouts.
    views, replacement, packed = [], {}, bytearray()
    for index, original in enumerate(doc["bufferViews"]):
        if index in image_views:
            continue
        view = copy.deepcopy(original)
        assert view.get("buffer", 0) == 0
        packed.extend(b"\0" * (-len(packed) % 4))
        offset = view.get("byteOffset", 0)
        view["byteOffset"] = len(packed)
        packed.extend(binary[offset:offset+view["byteLength"]])
        replacement[index] = len(views)
        views.append(view)
    for accessor in doc.get("accessors", []):
        if "bufferView" in accessor:
            accessor["bufferView"] = replacement[accessor["bufferView"]]
        for sparse in accessor.get("sparse", {}).values():
            if isinstance(sparse, dict) and "bufferView" in sparse:
                sparse["bufferView"] = replacement[sparse["bufferView"]]
    doc["bufferViews"] = views
    doc["buffers"] = [{"byteLength": len(packed)}]
    # Original part nodes remain at their authored pivots and retain all socket/part children.
    for node in list(doc["nodes"]):
        if "mesh" in node:
            mesh_node = {"name": node["name"] + "_lod0", "mesh": node.pop("mesh")}
            for field in ("skin", "weights"):
                if field in node:
                    mesh_node[field] = node.pop(field)
            node.setdefault("children", []).append(len(doc["nodes"]))
            doc["nodes"].append(mesh_node)
    for scene in doc["scenes"]:
        index = len(doc["nodes"])
        doc["nodes"].append({"name": "GameOrientation", "rotation": [0, -math.sqrt(.5), 0, math.sqrt(.5)],
                             "children": scene["nodes"]})
        scene["nodes"] = [index]
    return packed_glb(doc, bytes(packed)), scene_import(mappings)


def geometry(asset, key):
    hulls = []
    for name, centre, size, part, rotation in asset["collision_boxes"]:
        vertices = []
        for signs in itertools.product((-1, 1), repeat=3):
            p = [s * d * .5 for s, d in zip(signs, size)]
            if rotation:
                # Authored Blender rotation matrix, rows in source coordinates.
                p = [sum(row[i]*p[i] for i in range(3)) for row in rotation]
            vertices.append(point([p[i]+centre[i] for i in range(3)]))
        hulls.append({"name": name, "part": part, "points": vertices})
    for name, vertices, faces, part in asset["collision_custom"]:
        hulls.append({"name": name, "part": part, "points": [point(p) for p in vertices]})
    measured = asset["dimensions_m"] # Already in the delivered source's X-forward frame.
    return {"key": key, "source": asset["id"], "size": [round(measured[i]*.5, 6) for i in (1, 2, 0)],
            "parts": [{"name": p["name"], "mesh": p["object"], "parent": p["parent_part"],
                       "pivot": point(p["design_pivot_m"])} for p in asset["parts"]],
            "sockets": [{"name": s[0], "at": point(s[1]), "part": s[2]} for s in asset["sockets"]],
            "hulls": hulls}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/GameDev/Assets/PSG_Compound_Utilities_R02"))
    parser.add_argument("--install", action="store_true")
    args = parser.parse_args()
    source = args.source.resolve()
    manifest = source / "Reports/Delivery_contents.json"
    roster = json.loads(manifest.read_text(encoding="utf-8"))
    errors = []
    for entry in roster["files"]:
        path = (source / entry["path"]).resolve()
        if not path.is_relative_to(source) or path.is_symlink() or not path.is_file():
            errors.append(entry["path"] + ": missing/unsafe")
        elif path.stat().st_size != entry["bytes"] or digest(path) != entry["sha256"]:
            errors.append(entry["path"] + ": bytes/hash mismatch")
    if errors:
        raise SystemExit("Admission refused:\n" + "\n".join(errors))
    assets = json.loads((source / "Reports/Asset_manifest.json").read_text(encoding="utf-8"))["assets"]
    assert len(assets) == len(NAMES) == 12
    destination = ROOT / "game/assets/compound/r02"
    derived = [geometry(asset, key) for asset, key in zip(assets, NAMES)]
    plan = {"source": str(source), "destination": str(destination), "verifiedEntries": len(roster["files"]),
            "deliverySha256": digest(manifest), "models": NAMES, "adapter": "tools/integrate_compound.py",
            "provenance": "Owner-authored assemblies; Poly Haven / ambientCG CC0 materials. Source notices retained.",
            "boundary": "Byte integrity and deterministic orientation/collision conversion; runtime validation is separate."}
    if not args.install:
        print(json.dumps(plan, indent=2))
        return
    receipt_path = destination / "integration.json"
    old = {}
    if receipt_path.exists():
        old = json.loads(receipt_path.read_text(encoding="utf-8"))
        if old.get("deliverySha256") != plan["deliverySha256"]:
            raise SystemExit("Refusing to replace a different delivery")
    outputs, texture_roles, originals = {}, {}, {}
    for asset, key in zip(assets, NAMES):
        original = source / "Exports/GLB" / (asset["id"] + ".glb")
        target = destination / (key + ".glb")
        data, settings = adapted_glb(original, destination, outputs, texture_roles)
        outputs[target] = data
        outputs[target.with_suffix(".glb.import")] = settings
        originals[target] = {"source": str(original), "sourceSha256": digest(original)}
    for path, roles in texture_roles.items():
        if len(roles) > 1:
            raise SystemExit("A texture cannot be both a normal map and a colour/data map: " + str(path))
        outputs[path.with_suffix(".png.import")] = texture_import("normal" in roles)
    for path in list(outputs):
        if path.suffix == ".import":
            outputs[path] = merged_import(path, outputs[path])
    for name in ("README_Import_and_Assembly.md", "Reports/Texture_sources.json", "Reports/Delivery_contents.json"):
        outputs[destination / Path(name).name] = (source / name).read_bytes()
    derived_path = ROOT / "game/data/ground/compound.json"
    outputs[derived_path] = (json.dumps({"source": "PSG Compound Utilities R02; source coordinates adapted once by integrate_compound.py", "assemblies": derived}, indent=2)+"\n").encode("utf-8")
    previous = {ROOT / entry["path"].replace("\\", "/"): entry["sha256"] for entry in old.get("files", [])}
    if old.get("geometrySha256"):
        previous[derived_path] = old["geometrySha256"]
    # Validate the complete write set first. A prior receipt is permission only for exactly those installed bytes.
    for path, data in outputs.items():
        if (path != derived_path and not path.resolve().is_relative_to(destination.resolve())) or any(p.is_symlink() for p in (path, *path.parents)):
            raise SystemExit("Unsafe output path: " + str(path))
        if path.exists():
            present = digest(path)
            if path.suffix == ".import":
                # The engine rewrites these files itself. The adapter merges only its declared parameters above.
                pass
            elif path in previous:
                if present != previous[path]:
                    raise SystemExit("Refusing changed receipt-owned output: " + str(path))
            elif present != hashlib.sha256(data).hexdigest():
                raise SystemExit("Refusing differing unreceipted output: " + str(path))
    files = []
    for path, data in outputs.items():
        sha = hashlib.sha256(data).hexdigest()
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists() or digest(path) != sha:
            temporary = path.with_name(path.name + ".integrating")
            if temporary.exists():
                raise SystemExit("Previous incomplete write: " + str(temporary))
            temporary.write_bytes(data)
            temporary.replace(path)
        files.append({**originals.get(path, {}), "path": path.relative_to(ROOT).as_posix(), "sha256": sha, "bytes": len(data)})
    plan["files"] = files
    plan["geometrySha256"] = digest(derived_path)
    plan["adapterSha256"] = digest(Path(__file__))
    plan["formatVersion"] = 2
    plan["textures"] = {"count": len(texture_roles), "bytes": sum(len(outputs[p]) for p in texture_roles),
                        "policy": "Original PNG bytes, SHA256 deduplication, mipmapped VRAM compression; no resizing"}
    plan["lods"] = "One authored mesh child per named part; Godot generates internal mesh LODs at import"
    receipt_path.write_text(json.dumps(plan, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"installed": len(NAMES), "files": len(files), "textures": len(texture_roles),
                      "modelBytes": sum(len(outputs[p]) for p in originals),
                      "hulls": sum(len(a["hulls"]) for a in derived), "receipt": str(receipt_path)}))


if __name__ == "__main__":
    main()
