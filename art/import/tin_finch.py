"""Tin Finch TF-01 -> game/assets/world/tin-finch.glb, the starter ship (GROUND.md §1, §9).

Hull and hatch, each with LOD0-LOD2 (LOD3 is dropped), the 26 convex hulls (the hatch's move with it), the nine
attach points of the socket manifest and the hatch's open and close clips (0 to -78 degrees about the hinge in one
second). The nose faces Blender -Y, Godot +Z (the model front), like every world object; sockets keep the native
frame, +X along the nose. Surface atlas to 2K, markings to 1K; glass and lamps are plain material values.

    blender -b --factory-startup --python art/import/tin_finch.py
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from hollow import SOURCES, TEXTURES, WORLD  # noqa: E402
from hollow.godot import write_material  # noqa: E402
from hollow.kit import TURN_WORLD, Conversion, objects_in, snake  # noqa: E402
from hollow.textures import convert, write_texture_import  # noqa: E402

SOURCE = SOURCES / "TinFinch_TF01"
OUT = TEXTURES / "tin-finch"


def principled(material):
    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            return node
    return None


def materials():
    result = {}
    for part, size in (("Surface", 2048), ("Markings", 1024)):
        maps = {}
        for name, normal in (("BaseColor", False), ("ORM", False), ("NormalGL", True), ("Emissive", False)):
            src = SOURCE / "Textures" / f"T_TinFinch_{part}_{name}.png"
            if not src.exists():
                continue
            target = OUT / f"T_TinFinch_{part}_{name}.png"
            if not target.exists():
                # The surface normals are fine noise: half the colour map's size keeps them, at a quarter the bytes.
                convert(src, target, size // 2 if normal else size, normal=normal)
            write_texture_import(target, normal_map=normal)
            maps[name] = target
        result[f"M_TinFinch_{part}"] = write_material(
            f"tin_finch_{part.lower()}", f"M_TinFinch_{part}", albedo=maps["BaseColor"], orm=maps["ORM"],
            normal=maps["NormalGL"], emission=maps.get("Emissive"), emission_energy=2.0, vertex_colour=False)
    glass = principled(bpy.data.materials["M_TinFinch_Glass.002"])
    lamps = principled(bpy.data.materials["M_TinFinch_Lamps.002"])
    g = glass.inputs["Base Color"].default_value
    result["M_TinFinch_Glass"] = write_material(
        "tin_finch_glass", "M_TinFinch_Glass", colour=(g[0], g[1], g[2], 0.35), roughness=0.08, metallic=0.0,
        vertex_colour=False, transparent=True)
    e = lamps.inputs["Emission Color"].default_value
    strength = lamps.inputs["Emission Strength"].default_value
    result["M_TinFinch_Lamps"] = write_material(
        "tin_finch_lamps", "M_TinFinch_Lamps", colour=tuple(lamps.inputs["Base Color"].default_value), roughness=0.3,
        vertex_colour=False, extra=["emission_enabled = true", "emission = Color(%g, %g, %g, 1)" % (e[0], e[1], e[2]),
                                    "emission_energy_multiplier = %g" % max(1.0, strength)])
    return result


def hatch_clips(hatch):
    """Hatch_Open and Hatch_Close on the hatch's pivot empty, as NLA strips so both are exported."""
    hatch.animation_data_create()
    for name, angles in (("Hatch_Open", (0.0, -78.0)), ("Hatch_Close", (-78.0, 0.0))):
        action = bpy.data.actions.new(name)
        hatch.animation_data.action = action
        for frame, angle in ((1, angles[0]), (31, angles[1])):
            hatch.rotation_euler = (0.0, 0.0, math.radians(angle))
            hatch.keyframe_insert("rotation_euler", index=2, frame=frame)
        track = hatch.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, action)
        strip.extrapolation = "NOTHING"
        hatch.animation_data.action = None
    hatch.rotation_euler = (0.0, 0.0, 0.0)
    # Before the strips start nothing drives the hatch, so the exported rest pose is closed.
    bpy.context.scene.frame_set(0)


def main():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "TinFinch_TF01.blend"))
    mats = materials()
    for m in list(bpy.data.materials):
        if m.name.startswith("M_TinFinch_") and m.name.endswith(".002"):
            m.name = m.name[:-4]
    meshes = {o.name: o for o in objects_in("01_GAME_LOD0") + objects_in("04_GAME_LOD1_2_3") if o.type == "MESH"}
    hulls = sorted((o for o in objects_in("03_UCX_COLLISION") if o.type == "MESH"), key=lambda o: o.name)
    conv = Conversion("tin-finch")
    for part, base in (("Body", "SM_TinFinch"), ("Hatch", "SM_TinFinch_Hatch")):
        chain = [meshes[base], meshes[f"{base}_LOD1"], meshes[f"{base}_LOD2"]]
        own = [h for h in hulls if (h.name.startswith("UCX_SM_TinFinch_Hatch_") == (part == "Hatch"))]
        conv.add_part(part, meshes[base].matrix_world.translation, chain, own)
    manifest = json.loads((SOURCE / "Documentation" / "socket_manifest.json").read_text(encoding="utf-8"))
    for name, position in manifest["sockets_native_m"].items():
        conv.add_socket(snake(name).replace("__", "_"), Matrix.Translation(Vector(position)))
    if "HatchHinge" not in manifest["sockets_native_m"]:
        conv.add_socket("hatch_hinge", Matrix.Translation(Vector(manifest["hatch_pivot_native_m"])))
    conv.finish(TURN_WORLD)
    for record in conv.parts.values():
        for o in record["lods"]:
            for attr in list(o.data.color_attributes):
                o.data.color_attributes.remove(attr)
    hatch = next(o for o in conv.extra if o.name == "Hatch")
    hatch_clips(hatch)
    conv.export(WORLD / "tin-finch.glb", mats, animations=True)


main()
