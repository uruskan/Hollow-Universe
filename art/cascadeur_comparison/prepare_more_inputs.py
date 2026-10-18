"""Export the remaining six v3 motions to a separate Cascadeur input package.

Run a background Blender process with --python-exit-code 1 and this script,
then pass -- export or -- validate. Source .blend and prior inputs are read-only.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import sys

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / ".artifacts/character-trial-cascadeur-v3-comparison/expanded-inputs"
SOURCE = ROOT / ".artifacts/character-trial-v3/Survivor-Audition.blend"
HELPER = Path(__file__).with_name("prepare_inputs.py")
spec = importlib.util.spec_from_file_location("expanded_input_helpers", HELPER)
P = importlib.util.module_from_spec(spec)
spec.loader.exec_module(P)
P.OUT = OUT
P.H.OUT = OUT
CLIPS = ("idle", "walk", "sprint", "crouch_walk", "jump_land", "rock_strike")


def export():
    OUT.mkdir(parents=True, exist_ok=True)
    source_hash = P.H.digest(SOURCE)
    definitions = {clip["name"]: clip for clip in json.loads(
        (SOURCE.parent / "clips.json").read_text(encoding="utf-8"))}
    if any(name not in definitions for name in CLIPS):
        raise ValueError("Expected six remaining clips are absent from v3")
    manifest = {
        "schema": "hollow-cascadeur-v3-inputs-1", "source": str(SOURCE),
        "source_sha256": source_hash, "source_saved": False,
        "authoring": "Unmodified v3 sampled animations; not Cascadeur-authored candidates",
        "coordinates": "Blender metres Z up, forward -Y; FBX metres Y up, forward -Z",
        "export_settings": {"full_skeleton": True, "leaf_bones": False,
                            "sample_step_frames": 1, "simplification": 0,
                            "all_actions": False, "nla": False, "fps": 30},
        "views": {}, "files": [],
        "limitations": [
            "Blender procedural materials are not baked into FBX; image textures are extracted separately.",
            "FPS meshes remain cropped to arms, with the complete full-body skeleton retained.",
            "Bind files include rock, hatchet and torch meshes at their original prop-bone bind transforms.",
            "Unarmed motions contain no prop meshes; rock_strike contains the original rock mesh only.",
            "Camera transform/lens are exported; JSON records exact render framing.",
            "Fresh Blender reimport validation does not establish native Cascadeur controller fidelity or animation quality."]}
    for view in ("tps", "fps"):
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
        scene = next(item for item in bpy.data.scenes if item.name.startswith(view.upper()))
        bpy.context.window.scene = scene
        rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
        meshes = [obj for obj in scene.objects if obj.type == "MESH"
                  and any(mod.type == "ARMATURE" and mod.object == rig for mod in obj.modifiers)]
        if not any("Trial_Rock" in obj.name for obj in meshes):
            raise ValueError(f"{view}: original rock mesh is missing")
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
        P.write_json(OUT / f"{view}-skeleton-rest.json", skeleton)
        P.write_json(OUT / f"Survivor-{view.upper()}.qrigcasc", P.Q.build_template(skeleton))
        camera = P.camera_record(scene)
        P.write_json(OUT / f"{view}-camera.json", camera)
        textures = P.H.portable_textures(meshes)
        manifest["views"][view] = {"rig": rig.name, "full_bone_count": len(skeleton["bones"]),
                                  "bones": [bone["name"] for bone in skeleton["bones"]],
                                  "camera": camera, "textures": textures}
        for clip in ("bind", *CLIPS):
            prop = definitions[clip]["prop"] if clip != "bind" else None
            prefix = f"Trial_{prop.capitalize()}" if prop else None
            active = [obj for obj in meshes if clip == "bind" or "Trial_" not in obj.name
                      or (prefix is not None and prefix in obj.name)]
            rig.data.pose_position = "REST"
            scene.frame_set(1)
            bpy.context.view_layer.update()
            record = {"path": f"survivor-{view}-{clip.replace('_', '-')}.fbx", "view": view,
                      "clip": clip, "mesh_count": len(active),
                      "meshes": [P.mesh_record(obj, rig) for obj in active],
                      "rest_bounds_m": P.H.bounds(active, evaluated=True), "samples": {}}
            animated = clip != "bind"
            if animated:
                rig.data.pose_position = "POSE"
                action = P.H.select_action(rig, f"{view.upper()}_{clip}")
                first, last = map(round, action.frame_range)
                scene.frame_start, scene.frame_end = first, last
                duration = (last - first) / 30
                if abs(duration - definitions[clip]["duration"]) > 1e-6:
                    raise ValueError(f"{view}/{clip}: timing differs from v3 manifest")
                record.update(action=action.name, frame_range=[first, last],
                              sample_count=last - first + 1, duration_seconds=duration,
                              clip_metadata=definitions[clip])
                contacts = definitions[clip].get("contact")
                if not isinstance(contacts, list):
                    contacts = [contacts] if contacts is not None else []
                bounds_frames = {first, last, first + (last - first) // 2,
                                 *[first + round(t * 30) for t in contacts]}
                for frame in range(first, last + 1):
                    scene.frame_set(frame)
                    bpy.context.view_layer.update()
                    record["samples"][str(frame)] = {"bone_positions_m": P.bone_positions(rig),
                                                     "bone_rotations": P.bone_rotations(rig)}
                    if frame in bounds_frames:
                        record["samples"][str(frame)]["bounds_m"] = P.H.bounds(active, evaluated=True)
                scene.frame_set(first)
            else:
                record["frame_range"] = None
                scene.frame_start, scene.frame_end = 1, 1
            P.select(rig, active, scene.camera)
            P.export_fbx(OUT / record["path"], animated)
            record.update(bytes=(OUT / record["path"]).stat().st_size,
                          sha256=P.H.digest(OUT / record["path"]))
            manifest["files"].append(record)
    manifest["source_sha256_after"] = P.H.digest(SOURCE)
    if manifest["source_sha256_after"] != source_hash:
        raise RuntimeError("Source blend bytes changed")
    P.write_json(OUT / "cascadeur-cameras.json", {
        view: P.cascadeur_camera(data["camera"]) for view, data in manifest["views"].items()})
    P.write_json(OUT / "input-manifest.json", manifest)
    print("EXPANDED_INPUT_EXPORT", json.dumps({"source_unchanged": True,
          "files": [item["path"] for item in manifest["files"]]}), flush=True)


def validate():
    # The reused validator imports each FBX into a new empty Blender scene and
    # checks all-frame joint transforms plus selected evaluated mesh bounds.
    P.validate()
    receipt_path = OUT / "input-files.sha256.json"
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    receipt["reproduction_sources"].append({"path": str(Path(__file__).resolve().relative_to(ROOT)),
                                             "sha256": P.H.digest(Path(__file__))})
    P.write_json(receipt_path, receipt)
    print("EXPANDED_INPUT_VALIDATION_PASS", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("export", "validate"))
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    export() if args.mode == "export" else validate()
