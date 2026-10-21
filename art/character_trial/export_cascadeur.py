"""Export an isolated Cascadeur audition, then validate in a fresh Blender process.

blender -b --factory-startup --python-exit-code 1 --python this_file -- export
blender -b --factory-startup --python-exit-code 1 --python this_file -- validate

The source .blend is never saved. The FBXs contain the TPS deform rig, its
skinned character meshes and the original hatchet; no FPS geometry or studio.
The animation range has 42 intervals / 1.4 seconds, with 43 endpoint samples.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / ".artifacts/character-trial-v2/Survivor-Audition.blend"
OUT = ROOT / ".artifacts/character-trial-cascadeur"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, data):
    Path(path).write_text(json.dumps(data, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def bounds(meshes, evaluated=False):
    graph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in meshes:
        item = obj.evaluated_get(graph) if evaluated else obj
        points.extend(item.matrix_world @ vertex.co for vertex in item.data.vertices)
    low = [min(v[i] for v in points) for i in range(3)]
    high = [max(v[i] for v in points) for i in range(3)]
    return {"min": low, "max": high, "size": [high[i] - low[i] for i in range(3)]}


def mesh_record(obj):
    group_names = {group.index: group.name for group in obj.vertex_groups}
    weighted = [sum(group.weight for group in vertex.groups) for vertex in obj.data.vertices]
    return {
        "name": obj.name,
        "vertices": len(obj.data.vertices),
        "polygons": len(obj.data.polygons),
        "weight_sum_min": min(weighted),
        "weight_sum_max": max(weighted),
        "influencing_bones": sorted({group_names[group.group] for vertex in obj.data.vertices
                                    for group in vertex.groups if group.weight > 0}),
    }


def portable_textures(meshes):
    target = OUT / "textures"
    target.mkdir(parents=True, exist_ok=True)
    images = set()
    for obj in meshes:
        for material in obj.data.materials:
            if material and material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type == "TEX_IMAGE" and node.image:
                        images.add(node.image)
    records = []
    for image in sorted(images, key=lambda item: item.name):
        safe_name = "".join(c if c.isalnum() or c in "-_." else "_" for c in image.name)
        path = target / (Path(safe_name).stem + ".png")
        image.filepath_raw = str(path)
        image.file_format = "PNG"
        image.save()
        records.append({"name": image.name, "path": str(path.relative_to(OUT)),
                        "bytes": path.stat().st_size, "sha256": digest(path)})
    return records


def select_objects(rig, meshes):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in [rig, *meshes]:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig


def select_action(rig, name):
    action = bpy.data.actions[name]
    rig.animation_data_create()
    rig.animation_data.use_nla = False
    for track in rig.animation_data.nla_tracks:
        track.mute = True
        track.is_solo = False
    rig.animation_data.action = action
    slots = [slot for slot in action.slots if slot.target_id_type == "OBJECT"]
    if len(slots) != 1:
        raise ValueError(f"Expected one object slot in {name}, got {len(slots)}")
    rig.animation_data.action_slot = slots[0]
    return action


def export_one(path, animated):
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, use_visible=False,
        object_types={"ARMATURE", "MESH"}, global_scale=1.0,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        use_space_transform=True, bake_space_transform=False,
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
        mesh_smooth_type="FACE", use_armature_deform_only=True,
        add_leaf_bones=False, armature_nodetype="NULL",
        primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=animated, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode="RELATIVE", embed_textures=False,
        use_custom_props=False)


def export():
    OUT.mkdir(parents=True, exist_ok=True)
    source_hash = digest(SOURCE)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    scene = bpy.data.scenes["TPS — Survivor audition"]
    bpy.context.window.scene = scene
    rig = bpy.data.objects["Survivor_TPS_Rig"]
    meshes = [obj for obj in scene.objects if obj.type == "MESH"
              and any(mod.type == "ARMATURE" and mod.object == rig for mod in obj.modifiers)
              and not obj.name.startswith(("Trial_Rock", "Trial_Torch"))]
    if not meshes or not any(obj.name == "Survivor_Body" for obj in meshes):
        raise ValueError("TPS body is absent")
    if not any(obj.name == "Trial_Hatchet" for obj in meshes):
        raise ValueError("Hatchet is absent")
    # Unused prop bones have no selected weighted meshes or descendants. The
    # exporter's deform-only filter can omit them without touching human joints.
    for name in ("prop_rock", "prop_torch"):
        rig.data.bones[name].use_deform = False
    rig.animation_data_clear()
    for pose in rig.pose.bones:
        pose.matrix_basis = Matrix.Identity(4)
    rig.data.pose_position = "REST"
    scene.render.fps = 30
    scene.render.fps_base = 1.0
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.frame_start, scene.frame_end = 1, 43
    scene.frame_set(1)
    bpy.context.view_layer.update()
    select_objects(rig, meshes)
    textures = portable_textures(meshes)
    skeleton = [{"name": bone.name, "parent": bone.parent.name if bone.parent else None,
                 "deform": bone.use_deform,
                 "head_m": list(rig.matrix_world @ bone.head_local),
                 "tail_m": list(rig.matrix_world @ bone.tail_local),
                 "matrix_local": [list(row) for row in bone.matrix_local]}
                for bone in rig.data.bones if bone.use_deform or bone.children]
    write_json(OUT / "skeleton-rest.json", {
        "coordinate_system": "Blender: metres; Z up; character forward -Y; FBX: Y up, -Z forward",
        "rig": rig.name, "bones": skeleton})
    inventory = {"source": str(SOURCE), "source_sha256": source_hash,
                 "rig": rig.name, "mesh_count": len(meshes),
                 "meshes": [mesh_record(obj) for obj in meshes],
                 "bone_count": len(skeleton), "bones": [bone["name"] for bone in skeleton],
                 "rest_bounds_m": bounds(meshes), "textures": textures,
                 "export_settings": {"up": "Y", "forward": "-Z", "units": "metres",
                                     "deform_only": True, "leaf_bones": False,
                                     "all_actions": False, "nla": False}}
    export_one(OUT / "survivor-tps-bind.fbx", False)
    rig.data.pose_position = "POSE"
    action = select_action(rig, "TPS_hatchet_chop")
    if tuple(action.frame_range) != (1.0, 43.0):
        raise ValueError(f"Unexpected clip range: {tuple(action.frame_range)}")
    scene.frame_set(1)
    bpy.context.view_layer.update()
    sample_frames = [1, 7, 13, 19, 25, 31, 37, 43]
    inventory["animation"] = {"name": action.name, "start": 1, "end": 43,
                              "fps": 30, "intervals": 42, "duration_seconds": 1.4,
                              "sampled_bounds_m": {}}
    for frame in sample_frames:
        scene.frame_set(frame)
        inventory["animation"]["sampled_bounds_m"][str(frame)] = bounds(meshes, evaluated=True)
    scene.frame_set(1)
    export_one(OUT / "survivor-tps-hatchet-chop.fbx", True)
    if digest(SOURCE) != source_hash:
        raise ValueError("Source .blend changed unexpectedly")
    inventory["files"] = [{"path": path.name, "bytes": path.stat().st_size,
                           "sha256": digest(path)} for path in sorted(OUT.glob("*.fbx"))]
    inventory["limitations"] = [
        "FBX material conversion carries image textures and Principled fallback values; procedural Blender bump nodes are not baked here.",
        "The bind-pose hatchet retains its original prop-bone bind transform near the origin; the animation positions it in the hand.",
        "No new motion, skin weights, or character proportions were authored by this interchange export.",
        "The endpoint-inclusive chop has 43 samples across 42 frame intervals (1.4 seconds at 30 fps)."]
    write_json(OUT / "export-manifest.json", inventory)
    print("CASCADEUR_EXPORT", json.dumps({"out": str(OUT), "meshes": len(meshes),
                                          "bones": len(skeleton), "files": inventory["files"]}))


def finite(values):
    return all(math.isfinite(float(value)) for value in values)


def fbx_file_metadata(path):
    # Read the interchange bytes, independently of the imported scene's unit UI.
    from io_scene_fbx import parse_fbx
    document, version = parse_fbx.parse(str(path))
    global_settings = next(element for element in document.elems if element.id == b"GlobalSettings")
    properties = next(element for element in global_settings.elems if element.id == b"Properties70")
    relevant = {b"UnitScaleFactor", b"OriginalUnitScaleFactor", b"UpAxis", b"UpAxisSign",
                b"FrontAxis", b"FrontAxisSign", b"CoordAxis", b"CoordAxisSign", b"TimeMode"}
    settings = {element.props[0].decode(): element.props[-1] for element in properties.elems
                if element.id == b"P" and element.props[0] in relevant}
    paths = []
    objects = next(element for element in document.elems if element.id == b"Objects")
    for element in objects.elems:
        if element.id == b"Video":
            filename = next((child.props[0].decode() for child in element.elems
                             if child.id == b"RelativeFilename"), None)
            if filename:
                texture = path.parent / filename.replace("\\", "/")
                paths.append({"path": filename, "exists": texture.is_file()})
    return {"fbx_version": version, "settings": settings, "texture_paths": paths}


def validate():
    source = json.loads((OUT / "export-manifest.json").read_text(encoding="utf-8"))
    report = {"passed": True, "files": [], "validation": "Fresh Blender FBX reimport; Cascadeur UI import is separate."}
    for filename in ("survivor-tps-bind.fbx", "survivor-tps-hatchet-chop.fbx"):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.render.fps = 30
        bpy.ops.import_scene.fbx(filepath=str(OUT / filename), use_anim=True, anim_offset=0.0,
                                automatic_bone_orientation=False, ignore_leaf_bones=False)
        rigs = [obj for obj in scene.objects if obj.type == "ARMATURE"]
        meshes = [obj for obj in scene.objects if obj.type == "MESH"]
        problems = []
        if len(rigs) != 1:
            raise ValueError(f"{filename}: expected one rig, found {len(rigs)}")
        rig = rigs[0]
        interchange = fbx_file_metadata(OUT / filename)
        if interchange["settings"].get("UnitScaleFactor") != 100.0:
            problems.append("FBX metre unit metadata is unexpected")
        if any(not item["exists"] for item in interchange["texture_paths"]):
            problems.append("FBX references a missing texture")
        if len(meshes) != source["mesh_count"]:
            problems.append("mesh count changed")
        if set(b.name for b in rig.data.bones) != set(source["bones"]):
            problems.append("bone names/count changed")
        original_meshes = {item["name"]: item for item in source["meshes"]}
        mesh_comparison = []
        for obj in meshes:
            original = original_meshes.get(obj.name)
            record = mesh_record(obj)
            mesh_comparison.append(record)
            if not original or record["vertices"] != original["vertices"]:
                problems.append(f"{obj.name}: vertex count changed")
            if record["weight_sum_min"] < 0.999 or record["weight_sum_max"] > 1.001:
                problems.append(f"{obj.name}: unexpected weight sums")
            if not any(mod.type == "ARMATURE" and mod.object == rig for mod in obj.modifiers):
                problems.append(f"{obj.name}: skin binding absent")
        rig.data.pose_position = "REST"
        bpy.context.view_layer.update()
        rest = bounds(meshes, evaluated=True)
        rest_error = max(abs(rest[key][axis] - source["rest_bounds_m"][key][axis])
                         for key in ("min", "max") for axis in range(3))
        if rest_error > 0.0001:
            problems.append(f"rest bounds differ by {rest_error:g} metres")
        rig.data.pose_position = "POSE"
        action = rig.animation_data.action if rig.animation_data else None
        expected_animated = "chop" in filename
        action_range = list(action.frame_range) if action else None
        if expected_animated and action_range != [1.0, 43.0]:
            problems.append(f"unexpected animation range: {action_range}")
        if not expected_animated and action:
            problems.append("bind FBX unexpectedly contains an animation")
        max_bounds_error = 0.0
        max_matrix_element = 0.0
        if expected_animated:
            for step in range(85):
                frame = 1 + step / 2
                scene.frame_set(int(frame), subframe=frame % 1)
                graph = bpy.context.evaluated_depsgraph_get()
                evaluated = rig.evaluated_get(graph)
                for bone in evaluated.pose.bones:
                    values = [value for row in bone.matrix for value in row]
                    if not finite(values):
                        problems.append(f"{bone.name}: nonfinite matrix at {frame}")
                    max_matrix_element = max(max_matrix_element, max(abs(v) for v in values))
            for frame, expected in source["animation"]["sampled_bounds_m"].items():
                scene.frame_set(int(frame))
                actual = bounds(meshes, evaluated=True)
                max_bounds_error = max(max_bounds_error, max(abs(actual[key][axis] - expected[key][axis])
                                           for key in ("min", "max") for axis in range(3)))
            if max_bounds_error > 0.001:
                problems.append(f"sampled animated bounds differ by {max_bounds_error:g} metres")
        result = {"path": filename, "sha256": digest(OUT / filename), "passed": not problems,
                  "mesh_count": len(meshes), "bone_count": len(rig.data.bones),
                  "meshes": mesh_comparison, "action_range": action_range,
                  "duration_seconds": (action_range[1] - action_range[0]) / 30 if action_range else None,
                  "rest_bounds_m": rest, "rest_bounds_error_m": rest_error,
                  "max_sampled_animated_bounds_error_m": max_bounds_error,
                  "finite_matrix_samples": 85 * len(rig.data.bones) if expected_animated else 0,
                  "max_matrix_element": max_matrix_element, "problems": problems}
        result["interchange"] = interchange
        report["files"].append(result)
        report["passed"] &= result["passed"]
    write_json(OUT / "roundtrip-validation.json", report)
    print("CASCADEUR_VALIDATION", json.dumps(report))
    if not report["passed"]:
        raise ValueError("Cascadeur FBX roundtrip validation failed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("export", "validate"))
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    export() if args.mode == "export" else validate()
