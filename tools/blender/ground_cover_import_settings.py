"""Receipt-authenticated GPU import policy for one explicitly selected cover family.

Standard library only. This does not run Godot, touch source images/models, change
resolution, or qualify compressed appearance. Root coordinates all invocations:
  python -I tools/blender/ground_cover_import_settings.py --check --family fern \
      --from-receipt <immutable-v1-receipt> --from-receipt-sha256 <caller-pin>
  python -I tools/blender/ground_cover_import_settings.py --apply --family fern \
      --from-receipt <immutable-v1-receipt> --from-receipt-sha256 <caller-pin> \
      --output .artifacts/environment-ownership/cover-lossless-fern-NEW

Version 1 apply writes five existing PNG .import [params] sections. It first creates
an exclusive backup directory, preserving exact import bytes and currently mapped
CTEX files for renderer A/B and reversal. Every write is atomically replaced;
failure rolls back already-written imports, refusing to overwrite concurrent edits.
Reversal restores the receipt's original .import backups, then reimports through
Godot; original CTEX snapshots remain available for verification/A-B. Reversal
must not blindly overwrite import settings edited by somebody else since apply.

Version 2 requires --from-receipt and --from-receipt-sha256. It authenticates the
immutable v1 history and changes only the packed billboard N-T compress/mode byte
from 2 to 0. All five imports are held under Windows write/delete-denying handles;
the changed file is written through its locked handle, with a durable preimage
journal and CTEX snapshot. Godot import and exact all-mip equality remain separate
root-owned steps. A v2 receipt is a migration journal, not a replacement v1 baseline.
"""
from __future__ import annotations

import argparse
from contextlib import ExitStack
from dataclasses import dataclass, replace
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import struct
import uuid
import zlib

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).resolve().parent / "data/ground_cover_texture_imports.json"
FAMILIES = ("wild", "cotton", "tundra", "fern")
PREFIX = "assets/surface/ground-cover/"
MAX_JSON, MAX_GLB = 4 * 1024 * 1024, 32 * 1024 * 1024
MAX_PNG, MAX_IMPORT, MAX_CTEX = 16 * 1024 * 1024, 128 * 1024, 32 * 1024 * 1024
MAX_BACKUP = 160 * 1024 * 1024
SCHEMA_V1 = "hollow.cover-texture-import-policy.v1"
SCHEMA_V2 = "hollow.cover-texture-import-policy.v2"
ROLES = {"normal_xy", "albedo_cutout", "packed_ort", "billboard_normal_alpha"}
COMMON = {"compress/mode": "2", "mipmaps/generate": "true", "detect_3d/compress_to": "0"}
PRESERVE = {
    "mipmaps/generate": "true", "mipmaps/limit": "-1", "process/size_limit": "0",
    "process/channel_remap/red": "0", "process/channel_remap/green": "1",
    "process/channel_remap/blue": "2", "process/channel_remap/alpha": "3",
    "process/fix_alpha_border": "true", "process/premult_alpha": "false",
    "process/normal_map_invert_y": "false", "roughness/mode": "0", "roughness/src_normal": '""',
}


class ImportPolicyError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise ImportPolicyError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n").encode("utf-8")


def integer(value, minimum, maximum, label):
    require(type(value) is int and minimum <= value <= maximum, "Invalid " + label)
    return value


def bounded_read(path, maximum):
    require(path.is_file() and path.stat().st_size <= maximum, "Missing/oversized input: " + str(path))
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    require(len(data) <= maximum, "Input grew beyond byte budget: " + str(path))
    return data


def strict_json(data):
    require(len(data) <= MAX_JSON, "JSON exceeds byte budget")
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "Duplicate JSON field: " + key)
            result[key] = value
        return result
    def nonfinite(value):
        raise ImportPolicyError("Non-finite JSON number: " + value)
    try:
        return json.loads(data.decode("utf-8-sig"), object_pairs_hook=pairs, parse_constant=nonfinite)
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise ImportPolicyError("Invalid bounded JSON") from error


def inside(base, relative):
    require(isinstance(relative, str) and relative and "\\" not in relative and ":" not in relative,
            "Expected a relative POSIX path")
    pieces = PurePosixPath(relative)
    require(not pieces.is_absolute() and all(p not in (".", "..") for p in relative.split("/")),
            "Path traversal is not allowed")
    path = (base / relative).resolve()
    require(path.is_relative_to(base.resolve()), "Resolved path escapes its owner: " + relative)
    return path


def file_digest(path, maximum, offset=0, length=None):
    size = path.stat().st_size
    require(path.is_file() and size <= maximum, "Missing/oversized authenticated input: " + str(path))
    integer(offset, 0, size, "file offset")
    amount = size - offset if length is None else integer(length, 0, size - offset, "file length")
    result = hashlib.sha256()
    with path.open("rb") as stream:
        stream.seek(offset)
        remaining = amount
        while remaining:
            block = stream.read(min(256 * 1024, remaining))
            require(block, "Authenticated input was truncated: " + str(path))
            result.update(block)
            remaining -= len(block)
        if length is None:
            require(not stream.read(1), "Authenticated input grew during inspection")
    return result.hexdigest()


def authenticated(path, expected, maximum):
    require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{64}", expected), "Missing SHA-256 receipt")
    require(file_digest(path, maximum) == expected, "SHA-256 mismatch: " + str(path))


def section(data, name):
    # Values outside the params section (including Godot's multiline dictionaries)
    # are preserved as opaque bytes. Section headers themselves must be unique.
    pattern = rb"(?m)^\[" + re.escape(name.encode()) + rb"\][ \t]*\r?$"
    matches = list(re.finditer(pattern, data))
    require(len(matches) == 1, "Missing/duplicate import section: " + name)
    start = matches[0].end()
    next_section = re.search(rb"(?m)^\[[^\]\r\n]+\][ \t]*\r?$", data[start:])
    end = start + next_section.start() if next_section else len(data)
    return start, end, data[start:end]


def params(data):
    start, _, block = section(data, "params")
    result = {}
    for match in re.finditer(rb"(?m)^([A-Za-z_][A-Za-z0-9_/]*)=([^\r\n]*)", block):
        key = match[1].decode("ascii")
        require(key not in result, "Duplicate import parameter: " + key)
        result[key] = (match[2].decode("utf-8"), start + match.start(2), start + match.end(2))
    return result


def field(data, owner, key):
    _, _, block = section(data, owner)
    found = list(re.finditer(rb"(?m)^" + re.escape(key.encode()) + rb"=([^\r\n]*)", block))
    require(len(found) == 1, "Missing/duplicate import field: " + key)
    return found[0][1].decode("utf-8")


def policy_settings(role, schema=SCHEMA_V1):
    require(role in ROLES, "Unknown texture role")
    require(schema in (SCHEMA_V1,SCHEMA_V2),"Unknown role policy schema")
    return dict(COMMON, **{
        "compress/mode": "0" if schema == SCHEMA_V2 and role == "billboard_normal_alpha" else "2",
        "compress/high_quality": "false" if role == "normal_xy" else "true",
        "compress/normal_map": "1" if role == "normal_xy" else "2",
    })


def policy_bytes(original, role, schema=SCHEMA_V1):
    # The default remains the historical v1 policy for receipt reconstruction.
    found = params(original)
    for key, value in PRESERVE.items():
        require(key in found and found[key][0] == value, "Unexpected preserved setting: " + key)
    wanted = policy_settings(role,schema)
    changed = original
    replacements = []
    for key, value in wanted.items():
        require(key in found, "Missing import parameter: " + key)
        before, start, end = found[key]
        replacements.append((start, end, value.encode("ascii")))
    for start, end, value in sorted(replacements, reverse=True):
        changed = changed[:start] + value + changed[end:]
    return changed, wanted


def glb_layout(path):
    size = path.stat().st_size
    require(28 <= size <= MAX_GLB, "GLB size exceeds policy")
    with path.open("rb") as stream:
        require(stream.read(4) == b"glTF", "Not a GLB")
        version, total, length, kind = struct.unpack("<4I", stream.read(16))
        require(version == 2 and total == size and kind == 0x4E4F534A and length % 4 == 0,
                "Unsupported GLB framing")
        integer(length, 2, min(MAX_JSON, size - 28), "GLB JSON size")
        document = strict_json(stream.read(length))
        header = stream.read(8)
        require(len(header) == 8, "Missing GLB binary chunk")
        binary_length, binary_kind = struct.unpack("<2I", header)
        binary_offset = stream.tell()
        require(binary_kind == 0x004E4942 and binary_length % 4 == 0
                and binary_offset + binary_length == size, "GLB must have exactly one bounded binary chunk")
    require(document.get("asset", {}).get("version") == "2.0", "Expected glTF 2.0")
    buffers = document.get("buffers", [])
    require(len(buffers) == 1 and "uri" not in buffers[0], "Only the authenticated embedded buffer is supported")
    declared = integer(buffers[0].get("byteLength"), 1, binary_length, "GLB buffer length")
    require(binary_length - declared <= 3, "Unexpected binary padding")
    return document, binary_offset, declared


def image_uses(document):
    images, textures, materials = document.get("images", []), document.get("textures", []), document.get("materials", [])
    require(len(images) == 5 and 1 <= len(textures) <= 16 and 1 <= len(materials) <= 16,
            "Unexpected cover image/material count")
    result = [[] for _ in images]
    for number, material in enumerate(materials):
        require("extensions" not in material, "Extended material mapping requires explicit review")
        pbr = material.get("pbrMetallicRoughness", {})
        fields = {"baseColor": pbr.get("baseColorTexture"), "metallicRoughness": pbr.get("metallicRoughnessTexture"),
                  "normal": material.get("normalTexture"), "occlusion": material.get("occlusionTexture"),
                  "emissive": material.get("emissiveTexture")}
        for role, mapping in fields.items():
            if mapping is None:
                continue
            require("extensions" not in mapping, "Extended texture mapping requires explicit review")
            texture = textures[integer(mapping.get("index"), 0, len(textures) - 1, "texture index")]
            require("extensions" not in texture, "Compressed/extended image mapping requires explicit review")
            index = integer(texture.get("source"), 0, len(images) - 1, "image index")
            result[index].append(f"{number}/{role}")
    return [sorted(values) for values in result]


def png_header(path, colour_type):
    with path.open("rb") as stream:
        header = stream.read(33)
    require(len(header) == 33 and header[:16] == b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR", "Invalid PNG header")
    width, height, depth, colour, compression, filtering, interlace = struct.unpack(">2I5B", header[16:29])
    require((width, height, depth, colour, compression, filtering, interlace) == (2048, 2048, 8, colour_type, 0, 0, 0),
            "PNG dimensions/channels changed")
    require(zlib.crc32(header[12:29]) == struct.unpack(">I", header[29:33])[0], "Invalid PNG IHDR checksum")


@dataclass(frozen=True)
class Edit:
    path: Path
    relative: str
    before: bytes
    after: bytes
    role: str
    uid: str
    settings: dict
    ctex: Path
    ctex_relative: str
    ctex_sha256: str
    ctex_bytes: int


@dataclass(frozen=True)
class Plan:
    game: Path
    family: str
    edits: tuple[Edit, ...]
    sources: tuple[tuple[Path, str, int], ...]
    manifest_sha256: str
    policy_schema: str = SCHEMA_V1
    migration: dict | None = None


def previous_policy(manifest):
    history = manifest.get("previousPolicy")
    require(type(history) is dict and set(history) == {"schema","sha256","utf8"} and
            history["schema"] == SCHEMA_V1 and type(history["utf8"]) is str,"Missing exact v1 policy history")
    raw = history["utf8"].encode("utf-8")
    require(len(raw) <= MAX_JSON and digest(raw) == history["sha256"],"V1 policy history bytes/hash differ")
    old = strict_json(raw)
    require(old.get("schema") == SCHEMA_V1 and old.get("runtimeAppearanceApproved") is False and
            old.get("families") == manifest.get("families") and old.get("catalogSha256") == manifest.get("catalogSha256"),
            "Migration must preserve the historical source catalog/model/image identities")
    require(manifest.get("billboardSettings") == policy_settings("billboard_normal_alpha",SCHEMA_V2),
            "V2 packed billboard policy is not the explicit lossless correction")
    return history


def build_plan(game, family, manifest_path=MANIFEST):
    game = Path(game).resolve()
    require(game.is_dir() and family in FAMILIES, "Select one known family and an existing game directory")
    manifest_raw = bounded_read(Path(manifest_path), MAX_JSON)
    manifest = strict_json(manifest_raw)
    schema = manifest.get("schema")
    require(schema in (SCHEMA_V1,SCHEMA_V2), "Unknown import-policy schema")
    if schema == SCHEMA_V2: previous_policy(manifest)
    require(manifest.get("runtimeAppearanceApproved") is False, "Import policy must not claim visual acceptance")
    catalog_path = inside(game, PREFIX + "catalog.json")
    catalog_raw = bounded_read(catalog_path, MAX_JSON)
    require(digest(catalog_raw) == manifest.get("catalogSha256"), "Catalog receipt mismatch")
    catalog = strict_json(catalog_raw)
    rows = catalog.get("families", [])
    recipes = manifest.get("families", [])
    require(len(rows) == 4 and {r.get("key") for r in rows} == set(FAMILIES)
            and len(recipes) == 4 and {r.get("key") for r in recipes} == set(FAMILIES), "Ambiguous family catalog")
    row = next(r for r in rows if r["key"] == family)
    recipe = next(r for r in recipes if r["key"] == family)
    model_relative = PREFIX + family + ".glb"
    require(row.get("model") == recipe.get("model") == "res://" + model_relative
            and row.get("outputSha256") == recipe.get("modelSha256")
            and row.get("outputBytes") == recipe.get("modelBytes"), "Model receipt mismatch")
    model = inside(game, model_relative)
    authenticated(model, recipe["modelSha256"], MAX_GLB)
    require(model.stat().st_size == recipe["modelBytes"], "Model size changed")
    model_import_path = inside(game, model_relative + ".import")
    model_import = bounded_read(model_import_path, MAX_IMPORT)
    model_params = params(model_import)
    require(all(model_params.get(k, (None,))[0] == "1" for k in ("gltf/embedded_image_handling", "gltf/texture_map_mode")),
            "Preserve scene extraction/remapping mode before texture import")
    document, binary_offset, declared = glb_layout(model)
    uses = image_uses(document)
    expected_images = recipe.get("images", [])
    require(len(expected_images) == 5 and {i.get("imageIndex") for i in expected_images} == set(range(5)),
            "Image recipe must cover exactly five distinct image indices")
    sources = [(catalog_path, digest(catalog_raw), MAX_JSON), (model, recipe["modelSha256"], MAX_GLB),
               (model_import_path, digest(model_import), MAX_IMPORT), (Path(manifest_path).resolve(), digest(manifest_raw), MAX_JSON)]
    edits, seen_paths, backup_bytes = [], set(), 0
    views = document.get("bufferViews", [])
    require(1 <= len(views) <= 512, "Unexpected buffer view count")
    for expected in sorted(expected_images, key=lambda i: i["imageIndex"]):
        index = expected["imageIndex"]
        image = document["images"][index]
        require(image.get("name") == expected.get("imageName") and image.get("mimeType") == "image/png"
                and "uri" not in image and "extensions" not in image, "Image identity/layout changed")
        require(uses[index] == expected.get("uses"), "Material-to-image mapping changed")
        role = expected.get("role")
        require(role in ROLES, "Unknown explicit image role")
        required_use = {"normal_xy": ["0/normal"], "albedo_cutout": ["0/baseColor", "1/baseColor"],
                        "packed_ort": ["0/metallicRoughness"], "billboard_normal_alpha": ["1/normal"]}[role]
        require(len(uses[index]) == 1 and uses[index][0] in required_use, "Image has incompatible shared roles")
        view = views[integer(image.get("bufferView"), 0, len(views) - 1, "image buffer view")]
        require(view.get("buffer") == 0 and "extensions" not in view and "byteStride" not in view,
                "Image must use an ordinary embedded byte range")
        offset = integer(view.get("byteOffset", 0), 0, declared, "image offset")
        length = integer(view.get("byteLength"), 33, min(MAX_PNG, declared - offset), "image byte length")
        require(length == expected.get("bytes") and file_digest(model, MAX_GLB, binary_offset + offset, length) == expected.get("sha256"),
                "Embedded PNG does not match reviewed hash")
        relative = expected.get("path")
        require(relative == PREFIX + family + "_" + image["name"] + ".png" and relative not in seen_paths,
                "Unexpected/duplicate extracted PNG path")
        seen_paths.add(relative)
        path = inside(game, relative)
        authenticated(path, expected["sha256"], MAX_PNG)
        require(path.stat().st_size == length, "Extracted PNG size differs from embedded source")
        png_header(path, 2 if role in ("normal_xy", "packed_ort") else 6)
        sources.append((path, expected["sha256"], MAX_PNG))
        import_relative = relative + ".import"
        import_path = inside(game, import_relative)
        before = bounded_read(import_path, MAX_IMPORT)
        require(field(before, "remap", "importer") == '"texture"'
                and field(before, "remap", "type") == '"CompressedTexture2D"'
                and field(before, "deps", "source_file") == '"res://' + relative + '"', "Texture importer/source changed")
        uid = field(before, "remap", "uid")
        require(re.fullmatch(r'"uid://[a-z0-9]+"', uid), "Missing import UID")
        _, _, remap = section(before, "remap")
        require(len(re.findall(rb'"md5":\s*"[0-9a-f]{32}"', remap)) == 1, "Missing extracted-image generator identity")
        paths = re.findall(rb'(?m)^path(?:\.[a-z0-9_]+)?="res://([^"\r\n]+\.ctex)"\r?$', remap)
        require(len(paths) == 1, "Expected one desktop texture remap; review platform variants explicitly")
        ctex_relative = paths[0].decode("utf-8")
        require(ctex_relative.startswith(".godot/imported/"), "Texture remap must reference the import cache")
        ctex = inside(game, ctex_relative)
        size = ctex.stat().st_size
        require(52 <= size <= MAX_CTEX, "Baseline CTEX is missing/oversized")
        with ctex.open("rb") as stream:
            head = stream.read(52)
        require(head[:4] == b"GST2" and struct.unpack_from("<2I", head, 8) == (2048, 2048),
                "Baseline CTEX dimensions/layout changed")
        after, settings = policy_bytes(before, role,schema)
        backup_bytes += len(before) + size
        require(backup_bytes <= MAX_BACKUP, "Baseline backup byte budget exceeded")
        edits.append(Edit(import_path, import_relative, before, after, role, uid, settings,
                          ctex, ctex_relative, file_digest(ctex, MAX_CTEX), size))
    require(sum(e.role == "normal_xy" for e in edits) == 1 and sum(e.role == "albedo_cutout" for e in edits) == 2,
            "Expected one pure normal and two alpha/color maps")
    return Plan(game, family, tuple(edits), tuple(sources), digest(manifest_raw),schema)


def summary(plan):
    result = {"family": plan.family, "selectedImages": len(plan.edits), "changedImports": sum(e.before != e.after for e in plan.edits),
            "policySha256": plan.manifest_sha256, "policySchema": plan.policy_schema,
            "sourceAuthenticated": True, "runtimeAppearanceApproved": False,
            "nativeImportRequired": True, "files": [{"path": e.relative, "role": e.role, "uid": e.uid,
                "beforeSha256": digest(e.before), "afterSha256": digest(e.after), "settings": e.settings,
                "baselineCtex": e.ctex_relative, "baselineCtexBytes": e.ctex_bytes, "baselineCtexSha256": e.ctex_sha256}
                for e in plan.edits]}
    if plan.migration is not None:
        result["migration"] = plan.migration
        result["exclusiveWindowsHandlesRequired"] = True
    return result


def build_migration_plan(game,family,parent_receipt,parent_sha256,manifest_path=MANIFEST):
    plan = build_plan(game,family,manifest_path)
    require(plan.policy_schema == SCHEMA_V2,"Migration requires policy v2")
    history = previous_policy(strict_json(bounded_read(Path(manifest_path),MAX_JSON)))
    parent_path = Path(parent_receipt).resolve()
    artifacts = (plan.game.parent/".artifacts").resolve()
    require(parent_path.is_relative_to(artifacts),"Parent receipt must belong to this project's artifacts")
    authenticated(parent_path,parent_sha256,MAX_JSON)
    raw = bounded_read(parent_path,MAX_JSON)
    require(digest(raw) == parent_sha256,"Parent receipt changed while reading")
    parent = strict_json(raw)
    require(parent.get("schema") == "hollow.cover-texture-import-receipt.v1" and
            parent.get("status") == "applied-awaiting-native-import-and-visual-review" and
            parent.get("family") == family and Path(parent.get("game","")).resolve() == plan.game and
            parent.get("policySha256") == history["sha256"] and parent.get("runtimeAppearanceApproved") is False,
            "Wrong v1 parent receipt, family, policy, game or lifecycle state")
    files = parent.get("files")
    require(type(files) is list and len(files) == 5 and
            {row.get("path") for row in files} == {edit.relative for edit in plan.edits},"Parent import membership differs")
    old_sources = parent.get("sources")
    require(type(old_sources) is list and len(old_sources) == len(plan.sources) and
            len({row.get("path") for row in old_sources}) == len(old_sources),"Parent source receipt membership differs")
    old_sources = {row["path"]:row.get("sha256") for row in old_sources}
    for path,pin,_ in plan.sources:
        expected = history["sha256"] if path == Path(manifest_path).resolve() else pin
        require(old_sources.get(str(path)) == expected,"V1 source identity changed: "+str(path))
    sources = list(plan.sources)+[(parent_path,parent_sha256,MAX_JSON)]
    backup_bytes = 0
    for edit in plan.edits:
        row = next(row for row in files if row["path"] == edit.relative)
        require(row.get("role") == edit.role and row.get("uid") == edit.uid and
                row.get("backupImport") == "imports/"+edit.path.name,"Parent texture role/UID/backup differs")
        old_import = inside(parent_path.parent,row["backupImport"])
        authenticated(old_import,row.get("beforeSha256"),MAX_IMPORT)
        old_bytes = bounded_read(old_import,MAX_IMPORT)
        v1_bytes,v1_settings = policy_bytes(old_bytes,edit.role,SCHEMA_V1)
        require(digest(v1_bytes) == row.get("afterSha256") and row.get("settings") == v1_settings,
                "Parent v1 post-edit bytes/settings do not reproduce")
        require(section(edit.before,"params")[2] == section(v1_bytes,"params")[2],
                "Current params are not the exact v1 candidate; already migrated or independently edited: "+edit.relative)
        for owner,key in (("remap","importer"),("remap","type"),("remap","uid"),("deps","source_file")):
            require(field(edit.before,owner,key) == field(v1_bytes,owner,key),"Stable import identity changed: "+key)
        generator = lambda data: re.findall(rb'"md5":\s*"([0-9a-f]{32})"',section(data,"remap")[2])
        require(generator(edit.before) == generator(v1_bytes) and len(generator(v1_bytes)) == 1,
                "Generated PNG import identity changed")
        baseline_relative = row.get("baselineCtex")
        require(type(baseline_relative) is str and baseline_relative.startswith(".godot/imported/") and
                baseline_relative.endswith(".ctex"),"Invalid original CTEX owner")
        suffix = ".s3tc.ctex" if edit.role == "normal_xy" else ".bptc.ctex"
        require(edit.ctex_relative == baseline_relative[:-5]+suffix,"Unexpected current v1 CTEX variant")
        require(row.get("backupCtex") == "ctex/"+PurePosixPath(baseline_relative).name,"Parent CTEX snapshot path changed")
        baseline = inside(parent_path.parent,row["backupCtex"])
        authenticated(baseline,row.get("baselineCtexSha256"),MAX_CTEX)
        require(baseline.stat().st_size == row.get("baselineCtexBytes"),"Original baseline CTEX size differs")
        backup_bytes += len(old_bytes)+baseline.stat().st_size
        require(backup_bytes <= MAX_BACKUP,"Parent baseline admission exceeds aggregate byte cap")
        sources.extend(((old_import,row["beforeSha256"],MAX_IMPORT),(baseline,row["baselineCtexSha256"],MAX_CTEX)))
    changed = [edit for edit in plan.edits if edit.before != edit.after]
    require(len(changed) == 1 and changed[0].role == "billboard_normal_alpha",
            "V2 migration must change exactly one packed billboard import")
    edit = changed[0]
    require(len(edit.before) == len(edit.after),"Lossless correction must not change import length")
    differences = [i for i,(a,b) in enumerate(zip(edit.before,edit.after)) if a != b]
    require(len(differences) == 1 and edit.before[differences[0]] == ord("2") and
            edit.after[differences[0]] == ord("0") and params(edit.before)["compress/mode"][1] == differences[0],
            "Migration may change only compress/mode's 2-to-0 byte")
    migration = dict(parentReceipt=parent_path.relative_to(plan.game.parent).as_posix(),parentReceiptSha256=parent_sha256,
        previousPolicySha256=history["sha256"],changedImport=edit.relative,changedByteOffset=differences[0],
        baselineFiles=files,postImportRequirement="Original v1 baseline RGBA bytes identical in every mip; other four imported maps unchanged")
    return replace(plan,sources=tuple(sources),migration=migration)


class WindowsImportGuard:
    """One bounded existing import handle; sharing permits readers but denies writers and deletion.

    A v2 migration writes one existing byte, so it does not need to close its guard
    for a rename. The durable journal is the crash-recovery authority; this is not
    advertised as an atomic five-file replacement or a cross-platform lock.
    """
    def __init__(self,path,writable=False):
        self.path,self.writable,self.stream = Path(path),writable,None

    def __enter__(self):
        require(os.name == "nt","Exclusive migration is supported only by the verified Windows handle path")
        import ctypes
        from ctypes import wintypes
        import msvcrt
        api = ctypes.WinDLL("kernel32",use_last_error=True)
        api.CreateFileW.argtypes = (wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p,
                                   wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE)
        api.CreateFileW.restype = wintypes.HANDLE
        api.CloseHandle.argtypes,api.CloseHandle.restype = (wintypes.HANDLE,),wintypes.BOOL
        api.GetFinalPathNameByHandleW.argtypes = (wintypes.HANDLE,wintypes.LPWSTR,wintypes.DWORD,wintypes.DWORD)
        api.GetFinalPathNameByHandleW.restype = wintypes.DWORD
        handle = api.CreateFileW(str(self.path),0x80000000 | (0x40000000 if self.writable else 0),
                                 0x1,None,3,0x80,None)  # GENERIC_READ/WRITE, FILE_SHARE_READ, OPEN_EXISTING.
        if handle == ctypes.c_void_p(-1).value: raise ctypes.WinError(ctypes.get_last_error())
        descriptor = None
        try:
            final = ctypes.create_unicode_buffer(32768)
            length = api.GetFinalPathNameByHandleW(handle,final,len(final),0)
            require(0 < length < len(final),"Cannot authenticate locked import path")
            resolved = final.value[4:] if final.value.startswith("\\\\?\\") else final.value
            require(Path(resolved).resolve() == self.path.resolve(),"Locked import resolves to another owner")
            descriptor = msvcrt.open_osfhandle(handle,os.O_BINARY | (os.O_RDWR if self.writable else os.O_RDONLY))
            handle = None  # Descriptor owns the native handle now.
            self.stream = os.fdopen(descriptor,"r+b" if self.writable else "rb",buffering=0)
            descriptor = None
            return self
        except BaseException:
            if descriptor is not None: os.close(descriptor)
            if handle is not None: api.CloseHandle(handle)
            raise

    def read(self):
        self.stream.seek(0)
        data = self.stream.read(MAX_IMPORT+1)
        require(len(data) <= MAX_IMPORT,"Locked import exceeds byte cap")
        return data

    def write_byte(self,offset,value):
        require(self.writable and type(offset) is int and 0 <= offset < MAX_IMPORT and
                type(value) is bytes and len(value) == 1,"Only one bounded mode byte may be written")
        self.stream.seek(offset)
        require(self.stream.write(value) == 1,"Incomplete locked import write")
        self.stream.flush(); os.fsync(self.stream.fileno())

    def __exit__(self,*_):
        if self.stream is not None: self.stream.close(); self.stream = None


def atomic_write(path, data):
    temporary = path.with_name(path.name + ".cover-import-" + uuid.uuid4().hex + ".tmp")
    created = False
    try:
        with temporary.open("xb") as stream:
            created = True
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if created and temporary.exists():
            temporary.unlink()


def copy_authenticated(source, destination, expected, maximum,*,durable=False):
    require(source.stat().st_size <= maximum, "Backup exceeds file budget")
    actual, total = hashlib.sha256(), 0
    with source.open("rb") as reader, destination.open("xb") as writer:
        while block := reader.read(256 * 1024):
            total += len(block)
            require(total <= maximum, "Backup input grew beyond byte budget")
            actual.update(block)
            writer.write(block)
        if durable: writer.flush(); os.fsync(writer.fileno())
    require(actual.hexdigest() == expected, "Baseline changed during backup: " + str(source))


def apply_plan(plan, output):
    require(plan.policy_schema == SCHEMA_V1 and plan.migration is None,
            "V2 application requires the explicit authenticated migration transaction")
    destination = Path(output).resolve()
    artifacts = (plan.game.parent / ".artifacts").resolve()
    require(destination.is_relative_to(artifacts) and destination != artifacts
            and not destination.is_relative_to(plan.game) and not destination.exists(),
            "Backup destination must be a fresh directory inside this project's .artifacts")
    for path, expected, maximum in plan.sources:
        authenticated(path, expected, maximum)
    for edit in plan.edits:
        require(inside(plan.game, edit.relative) == edit.path and bounded_read(edit.path, MAX_IMPORT) == edit.before,
                "Import changed since inspection: " + edit.relative)
    # All validation precedes directory creation; backups precede the first asset mutation.
    destination.mkdir(parents=True, exist_ok=False)
    (destination / "imports").mkdir()
    (destination / "ctex").mkdir()
    receipt = dict(summary(plan), schema="hollow.cover-texture-import-receipt.v1", status="preparing", game=str(plan.game))
    for entry, edit in zip(receipt["files"], plan.edits):
        entry["backupImport"] = "imports/" + edit.path.name
        entry["backupCtex"] = "ctex/" + edit.ctex.name
        with (destination / entry["backupImport"]).open("xb") as stream:
            stream.write(edit.before)
        copy_authenticated(edit.ctex, destination / entry["backupCtex"], edit.ctex_sha256, MAX_CTEX)
    receipt["sources"] = [{"path": str(path), "sha256": expected} for path, expected, _ in plan.sources]
    receipt["status"] = "prepared"
    receipt_path = destination / "receipt.json"
    atomic_write(receipt_path, canonical(receipt))
    written = []
    try:
        # Recheck source receipts after copying baseline data and just before mutation.
        for path, expected, maximum in plan.sources:
            authenticated(path, expected, maximum)
        for edit in plan.edits:
            require(inside(plan.game, edit.relative) == edit.path and bounded_read(edit.path, MAX_IMPORT) == edit.before,
                    "Concurrent import edit: " + edit.relative)
            if edit.before != edit.after:
                atomic_write(edit.path, edit.after)
                written.append(edit)
        receipt["status"] = "applied-awaiting-native-import-and-visual-review"
        atomic_write(receipt_path, canonical(receipt))
    except BaseException as error:
        rollback_failures = []
        for edit in reversed(written):
            try:
                require(inside(plan.game, edit.relative) == edit.path and bounded_read(edit.path, MAX_IMPORT) == edit.after,
                        "Concurrent edit prevents rollback: " + edit.relative)
                atomic_write(edit.path, edit.before)
            except Exception as rollback:
                rollback_failures.append(str(rollback))
        receipt.update(status="rollback-incomplete" if rollback_failures else "rolled-back", error=str(error),
                       rollbackFailures=rollback_failures)
        atomic_write(receipt_path, canonical(receipt))
        raise
    return receipt_path


def apply_migration(plan,output,*,guard_factory=WindowsImportGuard):
    require(plan.policy_schema == SCHEMA_V2 and plan.migration is not None,"Missing authenticated v2 migration plan")
    destination = Path(output).resolve()
    artifacts = (plan.game.parent/".artifacts").resolve()
    require(destination.is_relative_to(artifacts) and destination != artifacts and not destination.exists() and
            not destination.is_relative_to(plan.game),"Migration backup needs a fresh project artifact directory")
    for path,pin,maximum in plan.sources: authenticated(path,pin,maximum)
    changed = next(edit for edit in plan.edits if edit.relative == plan.migration["changedImport"])
    with ExitStack() as stack:
        guards = {}
        for edit in sorted(plan.edits,key=lambda edit:edit.relative):
            require(inside(plan.game,edit.relative) == edit.path,"Import path owner changed")
            guard = stack.enter_context(guard_factory(edit.path,writable=edit is changed))
            guards[edit.relative] = guard
            require(guard.read() == edit.before,"Import changed before exclusive acquisition: "+edit.relative)
        # All imports remain locked until final receipt publication. Only N-T is writable.
        for edit in plan.edits: authenticated(edit.ctex,edit.ctex_sha256,MAX_CTEX)
        destination.mkdir(parents=True,exist_ok=False)
        (destination/"imports").mkdir(); (destination/"ctex").mkdir()
        receipt = dict(summary(plan),schema="hollow.cover-texture-import-receipt.v2",status="preparing",game=str(plan.game),
            toolSha256=file_digest(Path(__file__),MAX_JSON),
            unchangedImports=[dict(path=edit.relative,sha256=digest(edit.before),ctex=edit.ctex_relative,ctexSha256=edit.ctex_sha256)
                              for edit in plan.edits if edit is not changed],
            transaction="Exclusive Windows import handles; durable preimages; one-byte in-place mode update; crash recovery via journal",
            nativeImportRequired=True,allMipEqualityVerified=False,policySnapshot="policy.json",
            sources=[dict(path=str(path),sha256=pin) for path,pin,_ in plan.sources])
        for entry,edit in zip(receipt["files"],plan.edits):
            entry["backupImport"] = "imports/"+edit.path.name
            with (destination/entry["backupImport"]).open("xb") as stream:
                stream.write(edit.before); stream.flush(); os.fsync(stream.fileno())
            if edit is changed:
                entry["backupCtex"] = "ctex/"+edit.ctex.name
                copy_authenticated(edit.ctex,destination/entry["backupCtex"],edit.ctex_sha256,MAX_CTEX,durable=True)
        parent_path = inside(plan.game.parent,plan.migration["parentReceipt"])
        copy_authenticated(parent_path,destination/"parent-receipt.json",plan.migration["parentReceiptSha256"],MAX_JSON,durable=True)
        manifest_sources = [(path,pin,maximum) for path,pin,maximum in plan.sources if pin == plan.manifest_sha256]
        require(len(manifest_sources) == 1,"Migration policy source identity is ambiguous")
        policy_path,policy_pin,policy_maximum = manifest_sources[0]
        copy_authenticated(policy_path,destination/"policy.json",policy_pin,policy_maximum,durable=True)
        receipt["status"] = "prepared"
        receipt_path = destination/"receipt.json"
        atomic_write(receipt_path,canonical(receipt))
        guard = guards[changed.relative]
        offset = plan.migration["changedByteOffset"]
        attempted = False
        try:
            for path,pin,maximum in plan.sources: authenticated(path,pin,maximum)
            for edit in plan.edits:
                require(guards[edit.relative].read() == edit.before,"Locked preimage changed: "+edit.relative)
                authenticated(edit.ctex,edit.ctex_sha256,MAX_CTEX)
            attempted = True
            guard.write_byte(offset,b"0")
            require(guard.read() == changed.after,"Locked migration readback differs")
            for edit in plan.edits:
                require(guards[edit.relative].read() == edit.after,"Unchanged import was altered during migration")
            receipt["status"] = "applied-awaiting-native-lossless-equality"
            atomic_write(receipt_path,canonical(receipt))
        except BaseException as error:
            failures = []
            if attempted:
                try:
                    current = guard.read()
                    require(current in (changed.before,changed.after),"Unexpected bytes prevent migration rollback")
                    if current == changed.after: guard.write_byte(offset,b"2")
                    require(guard.read() == changed.before,"Migration rollback readback differs")
                except Exception as rollback: failures.append(str(rollback))
            receipt.update(status="rollback-incomplete" if failures else "rolled-back",error=str(error),rollbackFailures=failures)
            atomic_write(receipt_path,canonical(receipt))
            raise
        return receipt_path


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--check", action="store_true")
    action.add_argument("--apply", action="store_true")
    parser.add_argument("--family", choices=FAMILIES, required=True)
    parser.add_argument("--game", type=Path, default=ROOT / "game")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--from-receipt",type=Path)
    parser.add_argument("--from-receipt-sha256")
    options = parser.parse_args(argv)
    require(options.output is None if options.check else options.output is not None,
            "Check writes nothing; apply requires an explicit fresh --output directory")
    require(options.from_receipt is not None and options.from_receipt_sha256 is not None,
            "Policy v2 needs an explicit immutable v1 --from-receipt and caller SHA-256")
    plan = build_migration_plan(options.game,options.family,options.from_receipt,options.from_receipt_sha256)
    result = summary(plan)
    if options.apply:
        result["receipt"] = str(apply_migration(plan, options.output))
    print(canonical(result).decode("utf-8"), end="")


if __name__ == "__main__":
    try:
        main()
    except (ImportPolicyError, OSError, KeyError, TypeError, struct.error) as error:
        raise SystemExit("Cover import policy refused: " + str(error)) from error
