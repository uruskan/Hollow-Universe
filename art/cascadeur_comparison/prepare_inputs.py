"""Export read-only v3 inputs for the isolated Cascadeur comparison.

Run Blender in a background process with ``--python-exit-code 1 --python
art/cascadeur_comparison/prepare_inputs.py -- export`` and then in a fresh
process with ``-- validate``. Never saves or modifies a source blend.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / ".artifacts/character-trial-v3/Survivor-Audition.blend"
OUT = ROOT / ".artifacts/character-trial-cascadeur-v3-comparison/inputs"


def load_helper(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "art/character_trial" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


H = load_helper("comparison_export_helpers", "export_cascadeur.py")
H.OUT = OUT
Q = load_helper("comparison_quickrig_helpers", "cascadeur_setup.py")


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def bone_positions(rig):
    return {bone.name: list((rig.matrix_world @ bone.matrix).translation)
            for bone in rig.pose.bones}


def bone_rotations(rig):
    return {bone.name: [list(row) for row in (rig.matrix_world @ bone.matrix).to_3x3()]
            for bone in rig.pose.bones}


def mesh_record(obj, rig):
    record = H.mesh_record(obj)
    group_names = {group.index: group.name for group in obj.vertex_groups}
    bone_names = set(rig.data.bones.keys())
    weighted = [sum(group.weight for group in vertex.groups
                    if group_names[group.group] in bone_names) for vertex in obj.data.vertices]
    record["non_skin_selection_groups"] = sorted(set(record["influencing_bones"]) - bone_names)
    record["influencing_bones"] = sorted(set(record["influencing_bones"]) & bone_names)
    record["weight_sum_min"], record["weight_sum_max"] = min(weighted), max(weighted)
    return record


def camera_record(scene):
    camera = scene.camera
    return {"name": camera.name, "matrix_world": [list(row) for row in camera.matrix_world],
            "type": camera.data.type, "lens_mm": camera.data.lens,
            "sensor_width_mm": camera.data.sensor_width,
            "sensor_height_mm": camera.data.sensor_height,
            "sensor_fit": camera.data.sensor_fit,
            "shift_x": camera.data.shift_x, "shift_y": camera.data.shift_y,
            "clip_start_m": camera.data.clip_start, "clip_end_m": camera.data.clip_end,
            "resolution": [scene.render.resolution_x, scene.render.resolution_y],
            "pixel_aspect": [scene.render.pixel_aspect_x, scene.render.pixel_aspect_y]}


def cascadeur_camera(camera):
    def convert(vector):
        return [vector[0], vector[2], -vector[1]]
    matrix = camera["matrix_world"]
    position = convert([row[3] for row in matrix[:3]])
    forward = convert([-row[2] for row in matrix[:3]])
    up = convert([row[1] for row in matrix[:3]])
    aspect = camera["resolution"][0] * camera["pixel_aspect"][0] / (
        camera["resolution"][1] * camera["pixel_aspect"][1])
    if camera["sensor_fit"] not in ("AUTO", "HORIZONTAL") or aspect < 1:
        raise ValueError("Explicit camera conversion required for vertical sensor fit")
    horizontal = 2 * math.atan(camera["sensor_width_mm"] / (2 * camera["lens_mm"]))
    vertical = 2 * math.atan(math.tan(horizontal / 2) / aspect)
    return {"coordinate_conversion": "Blender metre (x,y,z) to Cascadeur Y-up centimetre (100x,100z,-100y); verify import axes before applying",
            "position_cm": [100 * value for value in position],
            "forward_unit": forward, "up_unit": up,
            "target_100cm_forward": [100 * (a + b) for a, b in zip(position, forward)],
            "horizontal_fov_degrees": math.degrees(horizontal),
            "vertical_fov_degrees": math.degrees(vertical), "aspect_ratio": aspect,
            "resolution": [896, 630], "near_clip_cm": camera["clip_start_m"] * 100,
            "far_clip_cm": camera["clip_end_m"] * 100}


def select(rig, meshes, camera):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in [rig, *meshes, camera]:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig


def export_fbx(path, animated):
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, use_visible=False,
        object_types={"ARMATURE", "MESH", "CAMERA"}, global_scale=1.0,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        use_space_transform=True, bake_space_transform=False,
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
        mesh_smooth_type="FACE", use_armature_deform_only=False,
        add_leaf_bones=False, armature_nodetype="NULL",
        primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=animated, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode="RELATIVE", embed_textures=False,
        use_custom_props=False)


def export():
    OUT.mkdir(parents=True, exist_ok=True)
    before = H.digest(SOURCE)
    manifest = {
        "schema": "hollow-cascadeur-v3-inputs-1", "source": str(SOURCE),
        "source_sha256": before, "source_saved": False,
        "authoring": "Unmodified v3 sampled animations; not Cascadeur-authored candidates",
        "coordinates": "Blender metres Z up, forward -Y; FBX metres Y up, forward -Z",
        "export_settings": {"full_skeleton": True, "leaf_bones": False,
                            "sample_step_frames": 1, "simplification": 0,
                            "all_actions": False, "nla": False, "fps": 30},
        "views": {}, "files": [],
        "limitations": [
            "Blender procedural materials are not baked into FBX; image textures are extracted separately.",
            "All full-body joints are present in FPS exports, but its mesh remains intentionally cropped to arms.",
            "Bind files include both tools at their original prop-bone bind transforms, not in a posed grip.",
            "Camera transform/lens are exported. JSON records sensor fit, shifts and exact render framing for later Blender comparison.",
            "Validation checks fresh Blender FBX reimport, not Cascadeur control generation or export eligibility."]}
    for view in ("tps", "fps"):
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
        scene = next(scene for scene in bpy.data.scenes if scene.name.startswith(view.upper()))
        bpy.context.window.scene = scene
        rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
        meshes = [obj for obj in scene.objects if obj.type == "MESH"
                  and any(mod.type == "ARMATURE" and mod.object == rig for mod in obj.modifiers)
                  and "Trial_Rock" not in obj.name]
        scene.render.fps, scene.render.fps_base = 30, 1.0
        scene.unit_settings.system, scene.unit_settings.scale_length = "METRIC", 1.0
        rig.animation_data_clear()
        for bone in rig.pose.bones:
            bone.matrix_basis = Matrix.Identity(4)
        rig.data.pose_position = "REST"
        scene.frame_start, scene.frame_end = 1, 1
        scene.frame_set(1)
        bpy.context.view_layer.update()
        skeleton = {"rig": rig.name, "matrix_world": [list(row) for row in rig.matrix_world],
                    "bones": [{"name": bone.name, "parent": bone.parent.name if bone.parent else None,
                               "deform": bone.use_deform,
                               "head_m": list(rig.matrix_world @ bone.head_local),
                               "tail_m": list(rig.matrix_world @ bone.tail_local),
                               "matrix_local": [list(row) for row in bone.matrix_local]}
                              for bone in rig.data.bones]}
        write_json(OUT / f"{view}-skeleton-rest.json", skeleton)
        write_json(OUT / f"Survivor-{view.upper()}.qrigcasc", Q.build_template(skeleton))
        camera = camera_record(scene)
        write_json(OUT / f"{view}-camera.json", camera)
        textures = H.portable_textures(meshes)
        manifest["views"][view] = {"rig": rig.name, "full_bone_count": len(skeleton["bones"]),
                                  "bones": [b["name"] for b in skeleton["bones"]],
                                  "camera": camera, "textures": textures}
        for clip in ("bind", "hatchet_chop", "torch_equip"):
            active = [obj for obj in meshes if clip == "bind" or
                      ("Trial_Hatchet" not in obj.name if clip == "torch_equip"
                       else "Trial_Torch" not in obj.name)]
            rig.data.pose_position = "REST"
            scene.frame_set(1)
            bpy.context.view_layer.update()
            record = {"path": f"survivor-{view}-{clip.replace('_', '-')}.fbx",
                      "view": view, "clip": clip,
                      "mesh_count": len(active), "meshes": [mesh_record(obj, rig) for obj in active],
                      "rest_bounds_m": H.bounds(active, evaluated=True), "samples": {}}
            animated = clip != "bind"
            if animated:
                rig.data.pose_position = "POSE"
                action = H.select_action(rig, f"{view.upper()}_{clip}")
                first, last = map(round, action.frame_range)
                scene.frame_start, scene.frame_end = first, last
                record.update(action=action.name, frame_range=[first, last],
                              sample_count=last - first + 1, duration_seconds=(last - first) / 30)
                for frame in range(first, last + 1):
                    scene.frame_set(frame)
                    bpy.context.view_layer.update()
                    record["samples"][str(frame)] = {"bone_positions_m": bone_positions(rig),
                                                     "bone_rotations": bone_rotations(rig)}
                    if frame in {first, last, first + round(.6 * 30), first + round(.46 * 30)}:
                        record["samples"][str(frame)]["bounds_m"] = H.bounds(active, evaluated=True)
                scene.frame_set(first)
            else:
                record["frame_range"] = None
                scene.frame_start, scene.frame_end = 1, 1
            select(rig, active, scene.camera)
            export_fbx(OUT / record["path"], animated)
            record.update(bytes=(OUT / record["path"]).stat().st_size,
                          sha256=H.digest(OUT / record["path"]))
            manifest["files"].append(record)
    manifest["source_sha256_after"] = H.digest(SOURCE)
    if manifest["source_sha256_after"] != before:
        raise RuntimeError("Source bytes changed")
    write_json(OUT / "cascadeur-cameras.json",
               {view: cascadeur_camera(data["camera"]) for view, data in manifest["views"].items()})
    write_json(OUT / "input-manifest.json", manifest)
    print("COMPARISON_INPUTS", json.dumps({"out": str(OUT), "source_unchanged": True,
                                            "fbx": [r["path"] for r in manifest["files"]]}), flush=True)


def validate():
    manifest = json.loads((OUT / "input-manifest.json").read_text(encoding="utf-8"))
    report = {"passed": True, "method": "Fresh Blender FBX reimport, all frame joint samples and selected evaluated mesh bounds", "files": []}
    for original in manifest["files"]:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.render.fps, scene.render.fps_base = 30, 1.0
        bpy.ops.import_scene.fbx(filepath=str(OUT / original["path"]), use_anim=True,
                                anim_offset=0.0, automatic_bone_orientation=False,
                                ignore_leaf_bones=False)
        rigs = [obj for obj in scene.objects if obj.type == "ARMATURE"]
        meshes = [obj for obj in scene.objects if obj.type == "MESH"]
        cameras = [obj for obj in scene.objects if obj.type == "CAMERA"]
        problems = []
        if len(rigs) != 1 or len(cameras) != 1:
            raise RuntimeError(f"{original['path']}: expected one rig and camera; got {len(rigs)}, {len(cameras)}")
        rig = rigs[0]
        view = manifest["views"][original["view"]]
        if set(rig.data.bones.keys()) != set(view["bones"]):
            problems.append("Bone names/count differ")
        originals = {item["name"]: item for item in original["meshes"]}
        if set(obj.name for obj in meshes) != set(originals):
            problems.append("Mesh names/count differ")
        for obj in meshes:
            record = mesh_record(obj, rig)
            if record["vertices"] != originals[obj.name]["vertices"]:
                problems.append(f"{obj.name}: vertex count differs")
            if record["influencing_bones"] != originals[obj.name]["influencing_bones"]:
                problems.append(f"{obj.name}: vertex-group bone bindings differ")
            if record["weight_sum_min"] < .999 or record["weight_sum_max"] > 1.001:
                problems.append(f"{obj.name}: weight sums differ")
        rig.data.pose_position = "REST"
        bpy.context.view_layer.update()
        actual_rest = H.bounds(meshes, evaluated=True)
        rest_error = max(abs(actual_rest[key][i] - original["rest_bounds_m"][key][i])
                         for key in ("min", "max") for i in range(3))
        if rest_error > .0001:
            problems.append(f"Rest bounds error {rest_error:g}m")
        rig.data.pose_position = "POSE"
        action = rig.animation_data.action if rig.animation_data else None
        actual_range = list(action.frame_range) if action else None
        if actual_range != original["frame_range"]:
            problems.append(f"Action range differs: {actual_range}")
        max_joint_error = max_bounds_error = max_rotation_error = 0.0
        for frame, expected in original["samples"].items():
            scene.frame_set(int(frame))
            bpy.context.view_layer.update()
            actual = bone_positions(rig)
            rotations = bone_rotations(rig)
            for bone, xyz in expected["bone_positions_m"].items():
                max_joint_error = max(max_joint_error, max(abs(a-b) for a, b in zip(actual[bone], xyz)))
                if not all(math.isfinite(v) for v in actual[bone]):
                    problems.append(f"Nonfinite joint {bone} frame {frame}")
                max_rotation_error = max(max_rotation_error,
                                         max(abs(rotations[bone][i][j] - expected["bone_rotations"][bone][i][j])
                                             for i in range(3) for j in range(3)))
            if "bounds_m" in expected:
                measured = H.bounds(meshes, evaluated=True)
                max_bounds_error = max(max_bounds_error,
                                      max(abs(measured[key][i] - expected["bounds_m"][key][i])
                                          for key in ("min", "max") for i in range(3)))
        if max_joint_error > .0001:
            problems.append(f"Joint-position error {max_joint_error:g}m")
        if max_bounds_error > .001:
            problems.append(f"Animated mesh bounds error {max_bounds_error:g}m")
        if max_rotation_error > .0001:
            problems.append(f"Bone rotation matrix error {max_rotation_error:g}")
        camera = cameras[0]
        camera_error = max(abs(camera.matrix_world[i][j] - view["camera"]["matrix_world"][i][j])
                           for i in range(4) for j in range(4))
        lens_error = abs(camera.data.lens - view["camera"]["lens_mm"])
        if camera_error > .0001 or lens_error > .001:
            problems.append(f"Camera differs: matrix {camera_error:g}, lens {lens_error:g}")
        metadata = H.fbx_file_metadata(OUT / original["path"])
        if metadata["settings"].get("UnitScaleFactor") != 100.0:
            problems.append("Unit metadata not metres")
        if any(not texture["exists"] for texture in metadata["texture_paths"]):
            problems.append("Missing referenced texture")
        if H.digest(OUT / original["path"]) != original["sha256"]:
            problems.append("FBX bytes differ from export receipt")
        result = {"path": original["path"], "passed": not problems, "problems": problems,
                  "meshes": len(meshes), "bones": len(rig.data.bones), "cameras": len(cameras),
                  "frame_range": actual_range, "joint_samples": len(original["samples"]) * len(rig.data.bones),
                  "rest_bounds_error_m": rest_error, "max_joint_position_error_m": max_joint_error,
                  "max_animated_bounds_error_m": max_bounds_error,
                  "max_bone_rotation_matrix_error": max_rotation_error,
                  "max_camera_matrix_error": camera_error, "lens_error_mm": lens_error,
                  "interchange": metadata}
        report["files"].append(result)
        report["passed"] &= not problems
    report["source_unchanged"] = H.digest(SOURCE) == manifest["source_sha256"]
    report["passed"] &= report["source_unchanged"]
    write_json(OUT / "roundtrip-validation.json", report)
    write_json(OUT / "input-files.sha256.json", {
        "source_blend_sha256": manifest["source_sha256"],
        "reproduction_sources": [{"path": str(path.relative_to(ROOT)), "sha256": H.digest(path)}
                                 for path in (Path(__file__).resolve(),
                                              ROOT / "art/character_trial/export_cascadeur.py",
                                              ROOT / "art/character_trial/cascadeur_setup.py")],
        "files": [{"path": path.relative_to(OUT).as_posix(), "bytes": path.stat().st_size,
                   "sha256": H.digest(path)} for path in sorted(OUT.rglob("*"))
                  if path.is_file() and path.name != "input-files.sha256.json"]})
    print("COMPARISON_VALIDATION", json.dumps(report), flush=True)
    if not report["passed"]:
        raise RuntimeError("Input roundtrip validation failed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("export", "validate"))
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    export() if args.mode == "export" else validate()
