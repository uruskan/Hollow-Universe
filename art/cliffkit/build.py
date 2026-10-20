"""Builds the cliff kit (docs/WORLD_TERRAIN_PLAN.md §4.3, iteration 4) from the owner's scans, one Blender process:

    blender -b --factory-startup --python art/cliffkit/build.py                       # every piece
    blender -b --factory-startup --python art/cliffkit/build.py -- desert-xl-a nordic   # pieces, or whole families
    blender -b --factory-startup --python art/cliffkit/build.py -- --detail            # the families' detail maps
    blender -b --factory-startup --python art/cliffkit/build.py -- --assemble          # cliffkit.json from built pieces
    blender -b --factory-startup --python art/cliffkit/build.py -- --materials         # the materials alone, again

Each piece: its solid cut from the scan (shape.py), its levels, masks and collision shell (levels.py), its textures
baked from the scan (bake.py), then game/assets/surface/cliffkit/<key>.glb (meshes `<key>_lod0`..`_lod4`, the far
shell last, and empties `socket_left`/`socket_right`), its Godot material (materials/<key>.tres, cliffkit.gdshader),
its textures (textures/), its collision shell (<key>.collision.bin) and its entry in cliffkit.json (data.py). Pieces
may be built in several processes at once; each leaves its entry in art/.cache/cliffkit, and every run assembles
cliffkit.json from what is there.
"""
import hashlib
import json
import sys
import time
from pathlib import Path

ART = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ART))

import bpy  # noqa: E402

from hollow import GAME, REPO, SOURCES, log, res_path, script_args  # noqa: E402
from hollow.export import collection, export_glb, reset  # noqa: E402
from hollow.godot import merge_import  # noqa: E402

from cliffkit import bake, data, detail, levels, shape  # noqa: E402
from cliffkit.kit import FAMILIES, PIECES, SCANS  # noqa: E402

KIT = GAME / "assets" / "surface" / "cliffkit"
TEXTURES = KIT / "textures"
MATERIALS = KIT / "materials"
SHADER = GAME / "shaders" / "cliffkit.gdshader"
CACHE = REPO / "art" / ".cache" / "cliffkit"
VERSION = 1


def texture_import(path, normal_map=False):
    """Godot's import: VRAM-compressed with mipmaps; colour and packed maps BC7, the normal map RGTC."""
    merge_import(Path(str(path) + ".import"), ['importer="texture"', 'type="CompressedTexture2D"'], {
        "compress/mode": "2",
        "compress/high_quality": "false" if normal_map else "true",
        "compress/normal_map": "1" if normal_map else "2",
        "mipmaps/generate": "true",
        "process/fix_alpha_border": "false",
        "detect_3d/compress_to": "0",
    })


def write_material(key, family, albedo, normal, scan_colour):
    """materials/<key>.tres: the kit's shader with the piece's own maps and its family's detail."""
    fam = json.loads((CACHE / "_families.json").read_text(encoding="utf-8"))[family]
    textures = [albedo, normal, fam["albedo"], fam["normal"]]
    lines = [f'[gd_resource type="ShaderMaterial" load_steps={len(textures) + 2} format=3]', "",
             f'[ext_resource type="Shader" path="{res_path(SHADER)}" id="1"]']
    for i, t in enumerate(textures):
        lines.append(f'[ext_resource type="Texture2D" path="{t if str(t).startswith("res://") else res_path(t)}" '
                     f'id="{i + 2}"]')
    lines += ["", "[resource]", f'resource_name = "cliffkit_{key}"', 'shader = ExtResource("1")',
              'shader_parameter/albedo_map = ExtResource("2")', 'shader_parameter/normal_map = ExtResource("3")',
              'shader_parameter/detail_albedo = ExtResource("4")', 'shader_parameter/detail_normal = ExtResource("5")',
              "shader_parameter/scan_colour = Color(%.4f, %.4f, %.4f, 1)" % tuple(scan_colour),
              "shader_parameter/detail_mean = Color(%.4f, %.4f, %.4f, 1)" % tuple(fam["mean"]),
              "shader_parameter/cover_default = Color(%.4f, %.4f, %.4f, %.4f)" % tuple(FAMILIES[family]["cover"]),
              "shader_parameter/ground_default = Color(%.4f, %.4f, %.4f, %.4f)" % tuple(FAMILIES[family]["ground"]), ""]
    path = MATERIALS / f"{key}.tres"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    return path


def build_details():
    families = {}
    for family in FAMILIES:
        pa, pb, mean = detail.build(family, TEXTURES)
        texture_import(pa)
        texture_import(pb)
        families[family] = {"albedo": res_path(pa), "normal": res_path(pb), "mean": mean, "metres": 2.5,
                            "places": FAMILIES[family]["places"]}
    CACHE.mkdir(parents=True, exist_ok=True)
    (CACHE / "_families.json").write_text(json.dumps(families, indent=1), encoding="utf-8")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def socket_empty(name, position, direction, col):
    """An empty at a socket, its +X along the chain's way out and Z (Godot's +Y) up."""
    from mathutils import Matrix, Vector
    o = bpy.data.objects.new(name, None)
    x = Vector(direction).normalized()
    z = Vector((0, 0, 1))
    y = z.cross(x).normalized()
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = Vector(position)
    o.matrix_world = m
    col.objects.link(o)
    return o


def build_piece(spec):
    started = time.time()
    key = spec["key"]
    reset()
    col = collection("kit")
    shp = shape.build(spec, col)
    lods, shell, stats = levels.build(shp, col)
    info = data.placement(shp, lods, shell)
    albedo, normal, scan_colour = bake.bake(shp, lods[0], col, TEXTURES)
    texture_import(albedo)
    texture_import(normal, normal_map=True)
    tres = write_material(key, spec["family"], albedo, normal, scan_colour)
    mat = bpy.data.materials.new(f"cliffkit_{key}")
    for o in lods:
        o.data.materials.clear()
        o.data.materials.append(mat)
        o.data.shade_smooth()
    empties = []
    for s in info["sockets"]:
        # Back from Godot's frame to Blender's for the empty: (x, y, z) -> (x, -z, y).
        p, d = s["position"], s["direction"]
        empties.append(socket_empty("socket_" + s["name"], (p[0], -p[2], p[1]), (d[0], -d[2], d[1]), col))
    glb = KIT / f"{key}.glb"
    export_glb(glb, lods + empties, {mat.name: tres})
    verts, tris, digest = data.write_collision(KIT / f"{key}.collision.bin", shell)
    scan = SOURCES / SCANS[spec["scan"]]
    entry = {"key": key, "family": spec["family"], "role": spec["role"], "file": res_path(glb), "material": res_path(tres)}
    entry.update(info)
    entry["collision"] = {"file": res_path(KIT / f"{key}.collision.bin"), "vertices": verts, "triangles": tris,
                          "sha256": digest, "within95": round(stats["collision_p95"], 3),
                          "within": round(stats["collision_max"], 3), "openEdges": stats["collision_open"]}
    entry["textures"] = {"albedo": res_path(albedo), "normal": res_path(normal), "size": 2048 if shp.size >= 30 else 1024,
                         "scanColour": [round(c, 4) for c in scan_colour]}
    entry["source"] = {"scan": SCANS[spec["scan"]], "sha256": sha256(scan), "yaw": spec["yaw"],
                       "span": list(spec["span"]) if spec["span"] else None, "scale": spec["scale"],
                       "mirror": spec["mirror"], "ends": list(spec["ends"])}
    CACHE.mkdir(parents=True, exist_ok=True)
    (CACHE / f"{key}.json").write_text(json.dumps(entry, indent=1), encoding="utf-8")
    log(f"{key}: built in {time.time() - started:.0f} s")


def assemble():
    families = json.loads((CACHE / "_families.json").read_text(encoding="utf-8"))
    pieces = []
    for spec in PIECES:
        frag = CACHE / f"{spec['key']}.json"
        if frag.exists():
            pieces.append(json.loads(frag.read_text(encoding="utf-8")))
    kit = {
        "version": VERSION,
        "source": "docs/WORLD_TERRAIN_PLAN.md §4.3; built by art/cliffkit/build.py from the owner's Megascans "
                  "(game/assets/THIRD_PARTY.md)",
        "frame": "metres; +Y up, the face toward +X, the pivot on the ground line at the middle of the face's foot; "
                 "the left end (as you face the rock) toward +Z. res:// is the game folder.",
        "sockets": "a chain joins a piece's right socket to the next one's left socket: same position, directions "
                   "opposite. Sockets sit 1.5 m inside each end, so neighbours overlap.",
        "checks": "the ground's height at a check's x, z must lie between its y minus `below` and its y plus `above`, "
                  "or the piece is dropped (foot: it would float or bury; top: the open back would show).",
        "collision": "<key>.collision.bin: 'HCOL', u32 version 1, u32 vertices, u32 triangles, f32 xyz per vertex, "
                     "u16 abc per triangle (counter-clockwise from outside), little-endian.",
        "skirtMax": shape.SKIRT,
        "families": families,
        "pieces": pieces,
    }
    path = KIT / "cliffkit.json"
    path.write_text(json.dumps(kit, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    log(f"wrote {path.name}: {len(pieces)} pieces")


def rewrite_materials():
    """The materials again from the built pieces' entries (after a change to the shader's parameters or the families)."""
    for spec in PIECES:
        frag = CACHE / f"{spec['key']}.json"
        if frag.exists():
            entry = json.loads(frag.read_text(encoding="utf-8"))
            t = entry["textures"]
            write_material(spec["key"], spec["family"], t["albedo"], t["normal"], t["scanColour"])
            log(f"{spec['key']}: material rewritten")


def main():
    args = script_args()
    if "--assemble" in args:
        assemble()
        return
    if "--materials" in args:
        rewrite_materials()
        return
    if "--detail" in args or not (CACHE / "_families.json").exists():
        build_details()
        if "--detail" in args and len(args) == 1:
            return
    wanted = [a for a in args if not a.startswith("--")]
    for spec in PIECES:
        if wanted and spec["key"] not in wanted and spec["family"] not in wanted and spec["role"] not in wanted:
            continue
        build_piece(spec)
    assemble()


main()
