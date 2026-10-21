"""Evaluate the saved audition actions in Blender without saving or rendering.

Root-run example (Blender 5.2.2):
  blender --background --factory-startup --python-exit-code 1 --python
    art/character_trial/validate_animation.py -- --blend PATH --out REPORT.json

Optional --tps-clips and --fps-clips select per-view JSON manifests. By default
these are clips.json and fps-clips.json beside the blend (FPS falls back to TPS).
All integer and half frames are evaluated; --substeps 1 selects integer frames.
This is a bone/attachment/camera-data check, not a visual-quality certification.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector


EXPECTED = ("idle", "walk", "sprint", "crouch_walk", "jump_land",
            "rock_strike", "hatchet_chop", "torch_equip")
LIMITS = {
    "quaternion_squared_norm_error": .002,
    "normalized_matrix_orthogonality_error": .002,
    "minimum_active_scale_component": .0001,
    "minimum_active_matrix_abs_determinant": 1.e-8,
    "loop_position_m": .0001,
    "loop_rotation_degrees": .1,
    "loop_scale_component": .0001,
    "attachment_position_m": .001,
    "attachment_rotation_degrees": .25,
    "warning_local_rotation_speed_degrees_s": 1200.,
    "error_local_rotation_speed_degrees_s": 3600.,
    "warning_world_bone_speed_m_s": 15.,
    "error_world_bone_speed_m_s": 40.,
    "warning_supplied_hand_target_error_m": .002,
    "error_supplied_hand_target_error_m": .01,
    "camera_image_border_tolerance": .001,
}
BOUNDARY = (
    "Evaluates saved action bone transforms and camera-space hand landmarks at the stated samples. "
    "Does not inspect deformed mesh surfaces, finger/object penetration, cloth collisions, anatomy, "
    "aesthetic quality, foot sliding, shading, or engine playback. Constant hand/prop transforms "
    "prove rigid attachment only, not correct grip contact. Reach figures, when supplied, are "
    "authoring diagnostics rather than independent reconstruction of intended IK targets. "
    "Discontinuity thresholds catch gross jumps; they do not establish natural motion."
)


def finite_matrix(matrix):
    return all(math.isfinite(value) for row in matrix for value in row)


def normalized_matrix_is_orthogonal(matrix):
    columns = [matrix.to_3x3().col[i].copy() for i in range(3)]
    if any(column.length < 1.e-8 for column in columns):
        return False
    columns = [column.normalized() for column in columns]
    tolerance = LIMITS["normalized_matrix_orthogonality_error"]
    return all(abs(columns[a].dot(columns[b])) <= tolerance for a, b in ((0, 1), (0, 2), (1, 2)))


def json_safe(value):
    # Invalid/unevaluated metrics remain representable even in a failed report.
    if isinstance(value, float) and not math.isfinite(value):
        return None
    if isinstance(value, dict):
        return {key: json_safe(child) for key, child in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_safe(child) for child in value]
    return value


def quaternion_angle(a, b):
    # Avoid q/-q treating the same rotation as a discontinuity.
    dot = min(1., max(0., abs(a.normalized().dot(b.normalized()))))
    return math.degrees(2. * math.acos(dot))


def transform_error(a, b):
    pa, qa, sa = a.decompose()
    pb, qb, sb = b.decompose()
    return ((pa - pb).length, quaternion_angle(qa, qb), max(abs(x - y) for x, y in zip(sa, sb)))


def finding(result, code, severity, message, frame=None):
    item = result["_findings"].setdefault(code, {
        "code": code, "severity": severity, "message": message,
        "occurrences": 0, "first_frame": frame, "last_frame": frame})
    item["occurrences"] += 1
    item["last_frame"] = frame


def finish(result):
    result["findings"] = list(result.pop("_findings").values())
    result["passed"] = not any(item["severity"] == "error" for item in result["findings"])
    return result


def read_clips(path):
    clips = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(clips, list) or {c.get("name") for c in clips} != set(EXPECTED) or len(clips) != 8:
        raise ValueError(f"{path}: expected exactly the eight agreed animation names")
    for clip in clips:
        duration = clip.get("duration")
        if not isinstance(duration, (int, float)) or not math.isfinite(duration) or duration <= 0:
            raise ValueError(f"{path}: invalid duration for {clip['name']}")
    return clips


def choose_action(rig, action):
    rig.animation_data_create()
    slot = None
    for track in rig.animation_data.nla_tracks:
        track.mute = True
        track.is_solo = False
        for strip in track.strips:
            if strip.action == action and strip.action_slot is not None:
                slot = strip.action_slot
    rig.animation_data.use_nla = False
    rig.animation_data.action = action
    if slot is None:
        eligible = [s for s in action.slots if s.target_id_type == "OBJECT"]
        if len(eligible) != 1:
            raise ValueError(f"Action {action.name} has no unambiguous OBJECT slot for {rig.name}")
        slot = eligible[0]
    rig.animation_data.action_slot = slot


def inactive_prop_bone(bone, active_prop):
    while bone is not None:
        if bone.name.startswith("prop_"):
            return bone.name != "prop_" + str(active_prop)
        bone = bone.parent
    return False


def hand_landmarks(rig, side):
    bones = rig.pose.bones
    wrist = bones.get("hand_" + side)
    middle = bones.get("middle_01_" + side)
    if wrist is None or middle is None:
        raise ValueError(f"Missing wrist/middle-finger landmarks for {side}")
    points = [("wrist_" + side, wrist.head, True),
              ("palm_" + side, (wrist.head + middle.head) * .5, True)]
    for finger in ("thumb", "index", "middle", "ring", "pinky"):
        bone = bones.get(finger + "_03_" + side)
        if bone is not None:
            points.append((finger + "_tip_" + side, bone.tail, False))
    return [(name, rig.matrix_world @ point, required) for name, point, required in points]


def camera_policy(clip):
    policy = dict(clip.get("fps_camera", {}))
    policy.setdefault("required_hands", ["l", "r"])
    # The torch's authored entry from below the image is permitted for 0.25 s.
    policy.setdefault("visible_from_seconds", .25 if clip["name"] == "torch_equip" else 0.)
    policy.setdefault("visible_until_seconds", clip["duration"])
    return policy


def check_camera(result, scene, rig, camera, clip, frame, time):
    policy = camera_policy(clip)
    required_hands = policy.get("required_hands", ["l", "r"])
    start = float(policy.get("visible_from_seconds", 0.))
    end = float(policy.get("visible_until_seconds", clip["duration"]))
    if (not required_hands or not set(required_hands).issubset({"l", "r"})
            or not 0. <= start <= end <= clip["duration"]):
        raise ValueError(f"Invalid FPS visibility policy for {clip['name']}")
    visible_required = start - 1.e-6 <= time <= end + 1.e-6
    border = LIMITS["camera_image_border_tolerance"]
    info = result["fps_camera"]
    for side in ("l", "r"):
        for name, world, core in hand_landmarks(rig, side):
            ndc = world_to_camera_view(scene, camera, world)
            info["landmark_samples"] += 1
            if not all(math.isfinite(v) for v in ndc):
                finding(result, "camera_nonfinite_" + name, "error", f"Non-finite camera projection of {name}", frame)
                continue
            info["minimum_depth_m"] = min(info["minimum_depth_m"], ndc.z)
            if ndc.z <= camera.data.clip_start:
                info["near_plane_failures"] += 1
                finding(result, "camera_near_" + name, "error",
                        f"{name} reaches/crosses the camera near plane ({camera.data.clip_start:g} m)", frame)
            outside = not (-border <= ndc.x <= 1. + border and -border <= ndc.y <= 1. + border)
            if outside:
                if core and side in required_hands and visible_required:
                    info["required_landmarks_outside"] += 1
                    finding(result, "camera_frame_" + name, "error", f"Required {name} is outside the image", frame)
                elif not core and side in required_hands and visible_required:
                    info["fingertips_outside"] += 1
                    finding(result, "camera_frame_" + name, "warning", f"{name} is outside the image", frame)
                else:
                    info["outside_during_authored_exemption"] += 1


def check_supplied_reach(result, diagnostics, name):
    matches = [d for d in diagnostics if d.get("clip") == name]
    if len(matches) != 1 or "max_hand_target_error_m" not in matches[0]:
        result["supplied_reach_diagnostic"] = None
        finding(result, "reach_not_supplied", "warning", "No unique supplied maximum hand-target-error diagnostic")
        return
    value = matches[0]["max_hand_target_error_m"]
    result["supplied_reach_diagnostic"] = {"max_hand_target_error_m": value,
                                          "independently_recomputed": False}
    if not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        finding(result, "reach_invalid", "error", "Supplied hand-target-error diagnostic is invalid")
    elif value > LIMITS["error_supplied_hand_target_error_m"]:
        finding(result, "reach_error", "error", f"Supplied hand-target error is {value:.6g} m")
    elif value > LIMITS["warning_supplied_hand_target_error_m"]:
        finding(result, "reach_warning", "warning", f"Supplied hand-target error is {value:.6g} m")


def validate_clip(scene, rig, action, clip, prefix, substeps, diagnostics):
    result = {"action": action.name, "clip": clip["name"], "_findings": {}}
    choose_action(rig, action)
    fps = scene.render.fps / scene.render.fps_base
    first, last = map(float, action.frame_range)
    result["frame_range"] = [first, last]
    result["duration_seconds"] = (last - first) / fps
    result["sample_interval_frames"] = 1. / substeps
    if abs(first - 1.) > 1.e-6:
        finding(result, "frame_start", "error", "Standalone action must begin at frame 1")
    if abs(result["duration_seconds"] - clip["duration"]) > .51 / fps:
        finding(result, "duration", "error", "Saved action duration differs from the clip manifest")
    if not all(math.isfinite(f) for f in (first, last)) or last <= first or last - first > fps * 60:
        finding(result, "frame_range", "error", "Invalid, empty, or over-60-second audition action range")
        return finish(result)
    active_prop = clip.get("prop")
    active_names = [b.name for b in rig.data.bones if not inactive_prop_bone(b, active_prop)]
    for b in rig.data.bones:
        if not finite_matrix(b.matrix_local):
            finding(result, "rest_" + b.name, "error", "Rest matrix is non-finite: " + b.name)
    maximum = {"world_speed_m_s": 0., "local_rotation_speed_degrees_s": 0.,
               "raw_quaternion_squared_norm_error": 0.,
               "attachment_position_drift_m": 0., "attachment_rotation_drift_degrees": 0.}
    peaks = {}
    initial = previous = prop_initial = None
    sample_count = int(round((last - first) * substeps))
    frames = [first + i / substeps for i in range(sample_count + 1)]
    if frames[-1] < last - 1.e-6:
        frames.append(last)
    result["evaluated_sample_count"] = len(frames)
    result["active_bone_count"] = len(active_names)
    result["ignored_inactive_prop_bones"] = [b.name for b in rig.data.bones if b.name not in active_names]
    if active_prop in ("hatchet", "torch") and rig.pose.bones.get("prop_" + active_prop) is None:
        finding(result, "missing_active_prop", "error", "Active held prop has no prop bone")
    camera = scene.camera if prefix == "FPS" else None
    if prefix == "FPS":
        if camera is None:
            finding(result, "camera_missing", "error", "FPS scene has no active camera")
        else:
            result["fps_camera"] = {"camera": camera.name, "near_plane_m": camera.data.clip_start,
                                    "required_points": "wrist and palm centers",
                                    "policy": camera_policy(clip),
                                    "landmark_samples": 0, "required_landmarks_outside": 0,
                                    "fingertips_outside": 0, "near_plane_failures": 0,
                                    "outside_during_authored_exemption": 0, "minimum_depth_m": math.inf}
    previous_frame = None
    moving = set()
    for frame in frames:
        whole = math.floor(frame)
        scene.frame_set(whole, subframe=frame - whole)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = rig.evaluated_get(depsgraph)
        world = evaluated.matrix_world
        if not finite_matrix(world):
            finding(result, "rig_matrix", "error", "Rig world matrix is non-finite", frame)
            continue
        current = {}
        for name in active_names:
            bone = evaluated.pose.bones[name]
            matrix, basis = bone.matrix.copy(), bone.matrix_basis.copy()
            if not finite_matrix(matrix) or not finite_matrix(basis):
                finding(result, "finite_" + name, "error", "Non-finite evaluated bone transform: " + name, frame)
                continue
            if abs(matrix.to_3x3().determinant()) < LIMITS["minimum_active_matrix_abs_determinant"]:
                finding(result, "singular_" + name, "error", "Singular/near-singular active bone matrix: " + name, frame)
            if min(abs(v) for v in bone.scale) < LIMITS["minimum_active_scale_component"]:
                finding(result, "scale_" + name, "error", "Active bone has near-zero scale: " + name, frame)
            if bone.rotation_mode == "QUATERNION":
                q = bone.rotation_quaternion
                norm = sum(v * v for v in q)
                if not all(math.isfinite(v) for v in q) or norm < 1.e-8:
                    finding(result, "quaternion_" + name, "error", "Non-finite/zero evaluated quaternion: " + name, frame)
                else:
                    maximum["raw_quaternion_squared_norm_error"] = max(
                        maximum["raw_quaternion_squared_norm_error"], abs(norm - 1.))
                    if abs(norm - 1.) > LIMITS["quaternion_squared_norm_error"]:
                        integer_key = abs(frame - round(frame)) <= 1.e-6
                        applied_orthogonal = normalized_matrix_is_orthogonal(matrix)
                        severity = "error" if integer_key or not applied_orthogonal else "warning"
                        message = ("Non-unit quaternion at an integer sample or non-orthogonal applied transform: "
                                   if severity == "error" else
                                   "Interpolated raw quaternion is non-unit; applied rotation remains orthogonal: ")
                        finding(result, "quaternion_" + severity + "_" + name, severity, message + name, frame)
            current[name] = (matrix, basis, world @ matrix.translation)
            if initial is not None and name in initial:
                displacement, angle, _ = transform_error(initial[name][1], basis)
                if displacement > 1.e-6 or angle > .001:
                    moving.add(name)
            if previous is not None and name in previous:
                dt = (frame - previous_frame) / fps
                speed = (current[name][2] - previous[name][2]).length / dt
                angular = transform_error(previous[name][1], basis)[1] / dt
                for metric, value in (("world_speed_m_s", speed), ("local_rotation_speed_degrees_s", angular)):
                    if value > maximum[metric]:
                        maximum[metric] = value
                        peaks[metric] = {"bone": name, "frame": frame}
                for value, warning, error, code, label in (
                    (speed, LIMITS["warning_world_bone_speed_m_s"], LIMITS["error_world_bone_speed_m_s"], "speed_", "world speed"),
                    (angular, LIMITS["warning_local_rotation_speed_degrees_s"], LIMITS["error_local_rotation_speed_degrees_s"], "rotation_speed_", "local angular speed")):
                    if value > warning:
                        severity = "error" if value > error else "warning"
                        finding(result, code + severity + "_" + name, severity,
                                f"High {label} on {name}; inspect reported peak for a discontinuity", frame)
        if initial is None:
            initial = current
        if active_prop in ("hatchet", "torch") and "hand_r" in current and "prop_" + active_prop in current:
            hand, prop = current["hand_r"][0], current["prop_" + active_prop][0]
            if abs(hand.determinant()) > 1.e-8:
                attachment = hand.inverted() @ prop
                if prop_initial is None:
                    prop_initial = attachment.copy()
                drift, turn, scale = transform_error(prop_initial, attachment)
                maximum["attachment_position_drift_m"] = max(maximum["attachment_position_drift_m"], drift)
                maximum["attachment_rotation_drift_degrees"] = max(maximum["attachment_rotation_drift_degrees"], turn)
                if drift > LIMITS["attachment_position_m"] or turn > LIMITS["attachment_rotation_degrees"] or scale > .001:
                    finding(result, "attachment_drift", "error", "Right-hand-to-prop transform is not constant", frame)
        if camera is not None:
            check_camera(result, scene, evaluated, camera.evaluated_get(depsgraph), clip,
                         frame, (frame - first) / fps)
        previous, previous_frame = current, frame
    if not moving:
        finding(result, "no_motion", "error", "No active bone changes from the first evaluated pose")
    if clip.get("loop") and initial and previous:
        loop_max = {"position_m": 0., "rotation_degrees": 0., "scale_component": 0.}
        for name in active_names:
            if name not in initial or name not in previous:
                continue
            position, rotation, scale = transform_error(initial[name][1], previous[name][1])
            for key, value in (("position_m", position), ("rotation_degrees", rotation), ("scale_component", scale)):
                loop_max[key] = max(loop_max[key], value)
            if position > LIMITS["loop_position_m"] or rotation > LIMITS["loop_rotation_degrees"] or scale > LIMITS["loop_scale_component"]:
                finding(result, "loop_" + name, "error", "Loop endpoints do not match for " + name)
        result["loop_endpoint_maxima"] = loop_max
    result["moving_bones"] = sorted(moving)
    result["maxima"] = maximum
    result["peak_locations"] = peaks
    result["rigid_attachment_checked"] = active_prop in ("hatchet", "torch") and prop_initial is not None
    if active_prop == "rock":
        result["rock_contact_check"] = "Not checked: two-hand surface contact requires mesh/contact measurements and visual review"
    check_supplied_reach(result, diagnostics, clip["name"])
    return finish(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--blend", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--tps-clips", type=Path)
    parser.add_argument("--fps-clips", type=Path)
    parser.add_argument("--substeps", type=int, choices=(1, 2, 4), default=2)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parser.parse_args(argv)
    report = {"schema_version": 1, "blend": str(args.blend.resolve()), "passed": False,
              "validation_boundary": BOUNDARY, "thresholds": LIMITS,
              "substeps_per_frame": args.substeps, "views": {}, "errors": []}
    try:
        tps_path = args.tps_clips or args.blend.parent / "clips.json"
        fps_path = args.fps_clips or args.blend.parent / "fps-clips.json"
        if args.fps_clips is None and not fps_path.exists():
            fps_path = tps_path
        manifests = {"TPS": read_clips(tps_path), "FPS": read_clips(fps_path)}
        report["manifests"] = {"TPS": str(tps_path.resolve()), "FPS": str(fps_path.resolve())}
        bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()))
        report["blender_version"] = bpy.app.version_string
        for prefix in ("TPS", "FPS"):
            scenes = [scene for scene in bpy.data.scenes if scene.name.startswith(prefix)]
            if len(scenes) != 1:
                raise ValueError(f"Expected one {prefix} scene; found {len(scenes)}")
            scene = scenes[0]
            bpy.context.window.scene = scene
            rigs = [obj for obj in scene.objects if obj.type == "ARMATURE"]
            if len(rigs) != 1:
                raise ValueError(f"Expected one armature in {scene.name}; found {len(rigs)}")
            rig = rigs[0]
            diag_path = args.blend.parent / (prefix.lower() + "-ik.json")
            diagnostics = json.loads(diag_path.read_text(encoding="utf-8")) if diag_path.exists() else []
            view = {"scene": scene.name, "rig": rig.name, "clips": [], "errors": []}
            report["views"][prefix] = view
            expected_actions = {prefix + "_" + name for name in EXPECTED}
            actual_actions = {action.name for action in bpy.data.actions if action.name.startswith(prefix + "_")}
            if actual_actions != expected_actions:
                view["errors"].append(f"Action names differ: missing={sorted(expected_actions - actual_actions)}, extra={sorted(actual_actions - expected_actions)}")
            for clip in manifests[prefix]:
                action = bpy.data.actions.get(prefix + "_" + clip["name"])
                if action is None:
                    continue
                try:
                    result = validate_clip(scene, rig, action, clip, prefix, args.substeps, diagnostics)
                except Exception as exc:
                    result = {"action": action.name, "clip": clip["name"], "_findings": {}}
                    finding(result, "evaluation_error", "error", f"{type(exc).__name__}: {exc}")
                    finish(result)
                view["clips"].append(result)
                print(f"VALIDATE {action.name}: {'PASS' if result['passed'] else 'FAIL'} ({len(result['findings'])} findings)", flush=True)
            view["passed"] = not view["errors"] and len(view["clips"]) == 8 and all(c["passed"] for c in view["clips"])
        report["passed"] = all(view["passed"] for view in report["views"].values())
    except Exception as exc:
        report["errors"].append(f"{type(exc).__name__}: {exc}")
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(json_safe(report), indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "report": str(args.out.resolve()), "errors": report["errors"]}), flush=True)
    if not report["passed"]:
        raise RuntimeError("Animation validation failed; see JSON report")


if __name__ == "__main__":
    main()
