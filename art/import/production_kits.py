"""The production packages' models, installed into the game (GROUND.md §9.1; 15 October 2026).

    python -I art/import/production_kits.py            # salvage pickups and loot containers
    python -I art/import/production_kits.py salvage    # one package

Plain Python (no Blender): the packages (D:\\GameDev\\Assets\\Hollow_<Name>_R01, made in separate sessions) already follow
the layout of §9.1: `<Part>_lod0..2`, `socket_use`, a lid under an empty at its pivot. What this adds:

- the GLB itself, copied to game/assets/items or game/assets/world, its wrapper node dropped (the game's models have
  their parts at the root) and, for a container, the convex hull of its closed proxy (`Body_hull0-convcolonly`: a box
  for the crates, a prism for the drum; the package leaves collision to its scenes);
- the package's textures (2K, in game/assets/textures/<set>/) and its materials (game/assets/materials/<name>.tres,
  the package's own tuning with the paths of the game), the GLB's `.import` mapping its materials to them;
- the pickups' icons (the package's engine renders, 128 and 256 px).

models.json, the catalogue, is edited by hand: the entries name these files. Rebuild a container's gibs after this
(`blender -b --factory-startup --python art/gibs/build.py -- loot-crate military-crate loot-barrel-1`).
"""
import json
import re
import shutil
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hollow import ASSETS, GAME, ITEMS, MATERIALS, SOURCES, TEXTURES, WORLD, log, res_path  # noqa: E402
from hollow.godot import merge_import, write_scene_import  # noqa: E402

ICONS = ITEMS / "icons"

# What each package installs. "hull": the closed proxy of the package's scene as a hull (centre, size) or a prism
# (centre, height, radius); a model that carries its own hulls has none.
KITS = {
    "salvage": {
        "root": SOURCES / "Hollow_Salvage_R01",
        "textures": "salvage",
        "materials": {"HS_SalvageTrim": "salvage_trim", "HS_RopeFibre": "salvage_rope", "HS_CorrugatedSteel": "salvage_sheet"},
        "models": {key: {"dest": ITEMS, "icons": True} for key in
                   ("gears", "springs", "metal-pipe", "sheet-metal", "tech-scrap", "rope")},
    },
    "loot": {
        "root": SOURCES / "Hollow_LootContainers_R01",
        "textures": "loot",
        "materials": {"LC_Wood": "loot_wood", "LC_Metal": "loot_metal"},
        "models": {
            "loot-crate": {"dest": WORLD, "hull": ("box", (0, 0.28, 0), (1.04, 0.56, 0.65))},
            "military-crate": {"dest": WORLD, "hull": ("box", (0, 0.30, 0), (1.32, 0.60, 0.72))},
            "loot-barrel-1": {"dest": WORLD, "hull": ("prism", (0, 0.45, 0), 0.90, 0.302)},
        },
    },
}

PRISM_SIDES = 24


# ---------------------------------------------------------------- GLB


def read_glb(path):
    data = Path(path).read_bytes()
    magic, version, _ = struct.unpack("<4sII", data[:12])
    assert magic == b"glTF" and version == 2, path
    json_len, _ = struct.unpack("<I4s", data[12:20])
    document = json.loads(data[20:20 + json_len])
    at = 20 + json_len
    binary = b""
    if at < len(data):
        bin_len, _ = struct.unpack("<I4s", data[at:at + 8])
        binary = data[at + 8:at + 8 + bin_len]
    return document, bytearray(binary)


def write_glb(path, document, binary):
    document["buffers"] = [{"byteLength": len(binary)}]
    text = json.dumps(document, separators=(",", ":")).encode("utf-8")
    text += b" " * (-len(text) % 4)
    binary = bytes(binary) + b"\0" * (-len(binary) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    Path(path).write_bytes(b"glTF" + struct.pack("<II", 2, total) + struct.pack("<I4s", len(text), b"JSON") + text +
                           struct.pack("<I4s", len(binary), b"BIN\0") + binary)


def drop_wrapper(document):
    """The package wraps a model in one empty named after it; the game's models have their parts at the root."""
    scene = document["scenes"][document.get("scene", 0)]
    roots = scene["nodes"]
    if len(roots) != 1:
        return
    node = document["nodes"][roots[0]]
    if "mesh" in node or any(k in node for k in ("translation", "rotation", "scale", "matrix")) or not node.get("children"):
        return
    scene["nodes"] = list(node["children"])
    node["children"] = []


def hull_mesh(spec):
    """Vertices and triangle indices of a hull: a box (centre, size) or a prism (centre, height, radius), Y up."""
    import math
    if spec[0] == "box":
        (cx, cy, cz), (sx, sy, sz) = spec[1], spec[2]
        verts = [(cx + dx * sx / 2, cy + dy * sy / 2, cz + dz * sz / 2)
                 for dx in (-1, 1) for dy in (-1, 1) for dz in (-1, 1)]
        quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        tris = []
        for a, b, c, d in quads:
            tris += [(a, b, c), (a, c, d)]
        return verts, tris
    (cx, cy, cz), height, radius = spec[1], spec[2], spec[3]
    verts = []
    for y in (-height / 2, height / 2):
        for i in range(PRISM_SIDES):
            angle = 2 * math.pi * i / PRISM_SIDES
            verts.append((cx + radius * math.cos(angle), cy + y, cz + radius * math.sin(angle)))
    tris = []
    for i in range(PRISM_SIDES):
        j = (i + 1) % PRISM_SIDES
        tris += [(i, j, PRISM_SIDES + j), (i, PRISM_SIDES + j, PRISM_SIDES + i)]
    for i in range(1, PRISM_SIDES - 1):
        tris += [(0, i, i + 1), (PRISM_SIDES, PRISM_SIDES + i + 1, PRISM_SIDES + i)]
    return verts, tris


def add_hull(document, binary, spec):
    verts, tris = hull_mesh(spec)
    while len(binary) % 4:
        binary.append(0)
    pos_at = len(binary)
    for v in verts:
        binary += struct.pack("<3f", *v)
    idx_at = len(binary)
    for t in tris:
        binary += struct.pack("<3H", *t)
    views = document.setdefault("bufferViews", [])
    accessors = document.setdefault("accessors", [])
    views.append({"buffer": 0, "byteOffset": pos_at, "byteLength": len(verts) * 12, "target": 34962})
    views.append({"buffer": 0, "byteOffset": idx_at, "byteLength": len(tris) * 6, "target": 34963})
    accessors.append({"bufferView": len(views) - 2, "componentType": 5126, "count": len(verts), "type": "VEC3",
                      "min": [min(v[i] for v in verts) for i in range(3)], "max": [max(v[i] for v in verts) for i in range(3)]})
    accessors.append({"bufferView": len(views) - 1, "componentType": 5123, "count": len(tris) * 3, "type": "SCALAR"})
    document.setdefault("meshes", []).append({"name": "Body_hull0-convcolonly", "primitives": [
        {"attributes": {"POSITION": len(accessors) - 2}, "indices": len(accessors) - 1, "mode": 4}]})
    document["nodes"].append({"name": "Body_hull0-convcolonly", "mesh": len(document["meshes"]) - 1})
    document["scenes"][document.get("scene", 0)]["nodes"].append(len(document["nodes"]) - 1)


# ---------------------------------------------------------------- materials and textures


def texture_import(png):
    """The `.import` of a texture used on models: VRAM compressed with mipmaps, normal maps as normal maps."""
    params = {"compress/mode": "2", "mipmaps/generate": "true", "detect_3d/compress_to": "0",
              "compress/normal_map": "1" if "Normal" in png.name else "2"}
    merge_import(png.with_name(png.name + ".import"), ['importer="texture"', 'type="CompressedTexture2D"'], params)


def install_materials(kit):
    folder = TEXTURES / kit["textures"]
    folder.mkdir(parents=True, exist_ok=True)
    for png in sorted((kit["root"] / "Textures").glob("*.png")):
        if "Checker" in png.name:
            continue
        target = folder / png.name
        if not target.exists() or target.read_bytes() != png.read_bytes():
            shutil.copyfile(png, target)
        texture_import(target)
    mapping = {}
    for glb_name, game_name in kit["materials"].items():
        source = (kit["root"] / "Materials" / f"{glb_name}.tres").read_text(encoding="utf-8")
        text = re.sub(r'path="res://Textures/([^"]+)"',
                      lambda m: f'path="{res_path(folder / m.group(1))}"', source)
        target = MATERIALS / f"{game_name}.tres"
        target.write_text(text, encoding="utf-8", newline="\n")
        mapping[glb_name] = target
        log(f"wrote material {target.name}")
    return mapping


# ---------------------------------------------------------------- the packages


def install(name):
    kit = KITS[name]
    materials = install_materials(kit)
    for key, spec in kit["models"].items():
        document, binary = read_glb(kit["root"] / "Models" / f"{key}.glb")
        drop_wrapper(document)
        if "hull" in spec:
            add_hull(document, binary, spec["hull"])
        target = spec["dest"] / f"{key}.glb"
        write_glb(target, document, binary)
        used = {m["name"] for m in document.get("materials", [])}
        write_scene_import(target, {n: p for n, p in materials.items() if n in used})
        log(f"wrote {target.relative_to(GAME)} ({len(binary) // 1024} KB)")
        if spec.get("icons"):
            for size in (128, 256):
                (ICONS / str(size)).mkdir(parents=True, exist_ok=True)
                shutil.copyfile(kit["root"] / "Icons" / str(size) / f"{key}.png", ICONS / str(size) / f"{key}.png")


def main():
    names = sys.argv[1:] or list(KITS)
    for name in names:
        install(name)


if __name__ == "__main__":
    main()
