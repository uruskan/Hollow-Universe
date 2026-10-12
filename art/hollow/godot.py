"""Godot-side files the pipeline writes: shared materials (.tres) and scene import settings (.import)."""
from pathlib import Path

from . import MATERIALS, log, res_path
from .textures import _existing_uid


def write_material(name, resource_name, albedo=None, orm=None, normal=None, emission=None, emission_energy=1.0,
                   colour=(1, 1, 1, 1), roughness=1.0, metallic=0.0, vertex_colour=True, transparent=False,
                   uv_scale=None, extra=None):
    """Writes game/assets/materials/<name>.tres: an ORMMaterial3D (with an ORM texture) or a StandardMaterial3D."""
    kind = "ORMMaterial3D" if orm else "StandardMaterial3D"
    textures = [t for t in (albedo, orm, normal, emission) if t]
    ids = {t: str(i + 1) for i, t in enumerate(textures)}
    out = [f'[gd_resource type="{kind}" load_steps={len(textures) + 1} format=3]', ""]
    for t in textures:
        out.append(f'[ext_resource type="Texture2D" path="{res_path(t)}" id="{ids[t]}"]')
    if textures:
        out.append("")
    out += ["[resource]", f'resource_name = "{resource_name}"']
    if transparent:
        out.append("transparency = 1")
    out.append("vertex_color_use_as_albedo = " + ("true" if vertex_colour else "false"))
    out.append("albedo_color = Color(%g, %g, %g, %g)" % tuple(colour))
    if albedo:
        out.append(f'albedo_texture = ExtResource("{ids[albedo]}")')
    if orm:
        out.append(f'orm_texture = ExtResource("{ids[orm]}")')
    else:
        out.append(f"roughness = {roughness:g}")
        out.append(f"metallic = {metallic:g}")
    if normal:
        out.append("normal_enabled = true")
        out.append(f'normal_texture = ExtResource("{ids[normal]}")')
    if emission:
        out.append("emission_enabled = true")
        out.append(f'emission_texture = ExtResource("{ids[emission]}")')
        out.append(f"emission_energy_multiplier = {emission_energy:g}")
    if uv_scale:
        out.append("uv1_scale = Vector3(%g, %g, %g)" % tuple(uv_scale))
    out.append("texture_filter = 5")
    for line in extra or []:
        out.append(line)
    path = MATERIALS / f"{name}.tres"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    log(f"wrote material {path.name}")
    return path


def write_scene_import(glb, materials, animations=False):
    """The `.import` beside a GLB: no generated LODs (the files carry their own), tangents, and every material
    mapped to a shared Godot material. `materials` maps the GLB's material names to .tres paths."""
    glb = Path(glb)
    target = glb.with_name(glb.name + ".import")
    uid = _existing_uid(target)
    lines = ["[remap]", "", 'importer="scene"', "importer_version=1", 'type="PackedScene"']
    if uid:
        lines.append(f'uid="{uid}"')
    lines += ["", "[params]", "",
              "meshes/ensure_tangents=true",
              "meshes/generate_lods=false",
              "meshes/create_shadow_meshes=true",
              "meshes/light_baking=1",
              "animation/import=" + ("true" if animations else "false"),
              "gltf/embedded_image_handling=0"]
    entries = []
    for name, tres in sorted(materials.items()):
        entries.append(f'"{name}": {{\n"use_external/enabled": true,\n"use_external/path": "{res_path(tres)}"\n}}')
    lines.append('_subresources={\n"materials": {\n' + ",\n".join(entries) + "\n}\n}")
    target.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
