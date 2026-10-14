"""Godot materials for the monuments' surfaces: one ShaderMaterial per surface on `shaders/monument.gdshader`
(MONUMENTS.md §8.2), written to game/assets/materials/monument_<set>.tres."""
from hollow import MATERIALS as MATERIAL_DIR, res_path

from .surfaces import MATERIALS, texture_paths

SHADER = "res://shaders/monument.gdshader"


def _tres(name, set_name, tile, opts):
    lines = []
    ext = [("Shader", SHADER)]
    from .builder import WEAR
    params = {"tile_metres": f"{tile:g}", "wear": f"{WEAR.get(name, 0.4):g}"}
    if set_name:
        albedo, orm, normal = texture_paths(set_name)
        ext += [("Texture2D", res_path(albedo)), ("Texture2D", res_path(orm)), ("Texture2D", res_path(normal))]
        params.update({"albedo_tex": 'ExtResource("2")', "orm_tex": 'ExtResource("3")',
                       "normal_tex": 'ExtResource("4")', "textured": "true"})
    else:
        params["textured"] = "false"
    if opts.get("alpha"):
        params["alpha_cut"] = "true"
    if opts.get("glass"):
        params.update({"plain_roughness": "0.08", "plain_metallic": "0.0", "plain_specular": "0.9",
                       "plain_albedo": "Color(0.05, 0.06, 0.07, 1)"})
    if opts.get("plain"):
        params.update({"plain_roughness": f"{opts['plain']:g}", "plain_albedo": "Color(0.8, 0.8, 0.8, 1)"})
    if opts.get("lamp"):
        params.update({"emission_strength": "6.0", "plain_roughness": "0.4", "plain_albedo": "Color(1, 1, 1, 1)"})
    lines.append(f'[gd_resource type="ShaderMaterial" load_steps={len(ext) + 1} format=3]')
    lines.append("")
    for i, (kind, path) in enumerate(ext):
        lines.append(f'[ext_resource type="{kind}" path="{path}" id="{i + 1}"]')
    lines += ["", "[resource]", f'resource_name = "{name}"', 'shader = ExtResource("1")']
    for k, v in params.items():
        lines.append(f"shader_parameter/{k} = {v}")
    path = MATERIAL_DIR / f"monument_{(set_name or name[6:].lower())}.tres"
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return path


def godot_materials(names=None):
    """{Blender material name: .tres path} for `names` (default: every surface), writing the files."""
    out = {}
    for name, (set_name, tile, opts) in MATERIALS.items():
        if names and name not in names:
            continue
        out[name] = _tres(name, set_name, tile, opts)
    return out
