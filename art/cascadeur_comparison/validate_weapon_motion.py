"""Measure solved native weapon contacts without opening any application.

python art/cascadeur_comparison/validate_weapon_motion.py --clip pistol_reload --revision A01

Reads weapon-inputs/<weapon>/weapon-manifest.json and
weapon-native/<clip>-<revision>-{plan,transforms,validation}.json beneath --root.
Prefers weapon-native/<clip>-<revision>-source-manifest.json when present,
so later input-grip revisions cannot change an existing candidate's reference.
Writes a separate motion-validation receipt. Numeric success is not art approval.
Contact expectations come from source ready/grasp transforms, not the candidate's
first frame. Free hand transitions are excluded from rigid-contact checks.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / ".artifacts/character-trial-cascadeur-v3-comparison"
WEAPONS = {"heavyaxe_chop": "heavyaxe", "pickaxe_swing": "pickaxe", "pistol_fire": "pistol",
           "pistol_reload": "pistol", "rifle_fire": "rifle", "rifle_reload": "rifle"}
C = [[1., 0., 0.], [0., 0., 1.], [0., -1., 0.]]
FINGER_PREFIXES = ("thumb_", "index_", "middle_", "ring_", "pinky_")


def transpose(matrix):
    return [list(row) for row in zip(*matrix)]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def matvec(matrix, vector):
    return [dot(row, vector) for row in matrix]


def matmul(a, b):
    return [[dot(row, column) for column in zip(*b)] for row in a]


def subtract(a, b):
    return [x - y for x, y in zip(a, b)]


def norm(vector):
    return math.sqrt(dot(vector, vector))


def rotation_error_degrees(actual, expected):
    # A relative rotation's trace is 1 + 2 cos(theta).
    relative = matmul(transpose(expected), actual)
    return math.degrees(math.acos(max(-1., min(1., (sum(relative[i][i] for i in range(3)) - 1.) / 2.))))


def relative_pose(child, parent):
    inverse = transpose(parent["global_rotation"])
    return {"position_cm": matvec(inverse, subtract(child["global_position"], parent["global_position"])),
            "rotation": matmul(inverse, child["global_rotation"])}


def source_pose(position_m, rotation):
    return {"global_position": [100. * value for value in matvec(C, position_m)],
            "global_rotation": matmul(C, rotation)}


def ready_pose(manifest, bone):
    return source_pose(manifest["ready"]["bone_positions_m"][bone], manifest["ready"]["bone_rotations"][bone])


def contact_references(manifest):
    references = {"weapon": {"bone": "prop_hatchet", **relative_pose(ready_pose(manifest, "hand_l"), ready_pose(manifest, "prop_hatchet"))}}
    for name, grasp in manifest.get("grasps", {}).items():
        if name not in ("magazine", "rack"):
            continue
        bone = grasp.get("part_bone", "prop_magazine" if name == "magazine" else "prop_slide" if manifest["weapon_id"] == "pistol" else "prop_bolt")
        pose = source_pose(grasp["hand_position_m"], grasp["hand_rotation"])
        references[bone] = {"bone": bone, **relative_pose(pose, ready_pose(manifest, bone))}
    return references


def maximum_record():
    return {"samples": 0, "maximum_position_error_cm": 0., "maximum_position_error_frame": None,
            "maximum_rotation_error_degrees": 0., "maximum_rotation_error_frame": None}


def accumulate(record, frame, position_error=0., rotation_error=0., bone=None):
    record["samples"] += 1
    if record["maximum_position_error_frame"] is None or position_error > record["maximum_position_error_cm"]:
        record.update(maximum_position_error_cm=position_error, maximum_position_error_frame=frame)
        if bone:
            record["maximum_position_error_bone"] = bone
    if record["maximum_rotation_error_frame"] is None or rotation_error > record["maximum_rotation_error_degrees"]:
        record.update(maximum_rotation_error_degrees=rotation_error, maximum_rotation_error_frame=frame)
        if bone:
            record["maximum_rotation_error_bone"] = bone


def nonfinite_values(value, path=""):
    result = []
    if isinstance(value, dict):
        for key, child in value.items():
            result.extend(nonfinite_values(child, f"{path}/{key}"))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            result.extend(nonfinite_values(child, f"{path}/{index}"))
    elif isinstance(value, (float, int)) and not isinstance(value, bool) and not math.isfinite(value):
        result.append(path)
    return result


def validate(manifest, plan, raw_transforms, native_validation=None, contact_tolerance_cm=.5, contact_tolerance_degrees=5.,
             wrist_tolerance_degrees=30., key_control_tolerance_cm=.1):
    transforms = raw_transforms.get("joints", raw_transforms)
    poses = {int(frame): pose for frame, pose in plan["metadata"]["poses"].items()}
    keyed_frames = sorted(poses)
    frames = {int(frame): row for frame, row in transforms.items()}
    end = int(plan["end_frame"])
    errors, warnings = [], []
    missing = sorted(set(range(end + 1)) - set(frames))
    if missing:
        errors.append(f"Missing solved frames: {missing}")
    finite_errors = nonfinite_values(raw_transforms)
    if finite_errors:
        errors.append("Non-finite solved values")
        return {"clip": plan["metadata"].get("clip"), "weapon_id": manifest["weapon_id"], "passed": False,
                "visual_acceptance": False, "expected_frames": end + 1, "solved_frames": len(frames),
                "missing_frames": missing, "finite": False, "nonfinite_paths": finite_errors,
                "left_contacts": {}, "errors": errors, "warnings": ["Geometric measurements skipped because solved transforms contain non-finite values."]}
    if not keyed_frames or 0 not in frames:
        raise ValueError("Plan must contain poses and solved transforms must contain frame zero")
    native_control_error = native_validation.get("maximum_key_control_error_cm") if native_validation else None
    native_control_available = isinstance(native_control_error, (int, float)) and not isinstance(native_control_error, bool) and math.isfinite(native_control_error)
    native_control = {"available": native_control_available,
                      "maximum_key_control_error_cm": native_control_error if native_control_available else None,
                      "tolerance_cm": key_control_tolerance_cm,
                      "passed": native_control_available and native_control_error <= key_control_tolerance_cm}
    if not native_control["passed"]:
        errors.append("Native key-control error exceeds tolerance" if native_control_available else "Native key-control accuracy is unavailable")
    if native_validation and native_validation.get("finite") is False:
        errors.append("Native validation reports non-finite values")
    references = contact_references(manifest)
    # A contact is required across an interval only when BOTH endpoints declare
    # the same rigid contact. Each declared contact key is still checked alone.
    contact_frames = {label: set() for label in references}
    intervals = []
    for frame, pose in poses.items():
        contact = pose["hands"]["l"]["contact"]
        if contact == "rack":
            contact = "prop_slide" if manifest["weapon_id"] == "pistol" else "prop_bolt"
        if contact != "free":
            if contact not in references:
                errors.append(f"No source contact reference for {contact} at frame {frame}")
            else:
                contact_frames[contact].add(frame)
    for first, last in zip(keyed_frames, keyed_frames[1:]):
        left = poses[first]["hands"]["l"]["contact"]
        right = poses[last]["hands"]["l"]["contact"]
        if left == right and left != "free":
            label = ("prop_slide" if manifest["weapon_id"] == "pistol" else "prop_bolt") if left == "rack" else left
            if label in references:
                contact_frames[label].update(range(first, last + 1))
                intervals.append({"contact": label, "start_frame": first, "end_frame": last})
    contacts = {}
    for label, expected in references.items():
        record = maximum_record()
        record.update(parent_bone=expected["bone"], expected_relative_position_cm=expected["position_cm"],
                      expected_relative_rotation=expected["rotation"], frames=sorted(contact_frames[label]), per_frame=[])
        for frame in sorted(contact_frames[label] & set(frames)):
            actual = relative_pose(frames[frame]["hand_l"], frames[frame][expected["bone"]])
            position_error = norm(subtract(actual["position_cm"], expected["position_cm"]))
            rotation_error = rotation_error_degrees(actual["rotation"], expected["rotation"])
            accumulate(record, frame, position_error, rotation_error)
            record["per_frame"].append({"frame": frame, "position_error_cm": position_error, "rotation_error_degrees": rotation_error})
        record["passed"] = record["maximum_position_error_cm"] <= contact_tolerance_cm and record["maximum_rotation_error_degrees"] <= contact_tolerance_degrees
        if not record["passed"]:
            errors.append(f"Left-hand {label} contact exceeds tolerance")
        if record["samples"] == 1:
            warnings.append(f"{label} has only one declared contact frame; continuous grasp coverage is not established")
        contacts[label] = record
    key_hands = {side: maximum_record() for side in ("r", "l")}
    key_weapon_frame = maximum_record()
    key_weapon_local_hands = {side: maximum_record() for side in ("r", "l")}
    source_weapon = source_pose(manifest["weapon_frame"]["position_m"], manifest["weapon_frame"]["rotation"]) if "weapon_frame" in manifest else None
    weapon_in_prop = relative_pose(source_weapon, ready_pose(manifest, "prop_hatchet")) if source_weapon else None
    for frame, pose in poses.items():
        if frame not in frames:
            continue
        actual_weapon = None
        if weapon_in_prop and "weapon_position_cm" in pose and "weapon_rotation" in pose:
            actual_prop = frames[frame]["prop_hatchet"]
            rotated_offset = matvec(actual_prop["global_rotation"], weapon_in_prop["position_cm"])
            actual_weapon = {"global_position": [a + b for a, b in zip(actual_prop["global_position"], rotated_offset)],
                             "global_rotation": matmul(actual_prop["global_rotation"], weapon_in_prop["rotation"])}
            accumulate(key_weapon_frame, frame, norm(subtract(actual_weapon["global_position"], pose["weapon_position_cm"])),
                       rotation_error_degrees(actual_weapon["global_rotation"], pose["weapon_rotation"]))
        for side, expected in pose["hands"].items():
            actual = frames[frame]["hand_" + side]
            accumulate(key_hands[side], frame, norm(subtract(actual["global_position"], expected["wrist"])),
                       rotation_error_degrees(actual["global_rotation"], expected["hand_rotation"]))
            if actual_weapon and "weapon_local_wrist_cm" in expected:
                local_actual = relative_pose(actual, actual_weapon)
                local_expected_rotation = matmul(transpose(pose["weapon_rotation"]), expected["hand_rotation"])
                accumulate(key_weapon_local_hands[side], frame,
                           norm(subtract(local_actual["position_cm"], expected["weapon_local_wrist_cm"])),
                           rotation_error_degrees(local_actual["rotation"], local_expected_rotation))
    for side, record in key_hands.items():
        record["passed"] = record["maximum_position_error_cm"] <= .1 and record["maximum_rotation_error_degrees"] <= 1.
        if not record["passed"]:
            errors.append(f"{side} hand differs from a planned key pose")
    for label, record in [("weapon frame", key_weapon_frame), *[(f"{side} hand in weapon frame", value) for side, value in key_weapon_local_hands.items()]]:
        record["passed"] = record["maximum_position_error_cm"] <= .1 and record["maximum_rotation_error_degrees"] <= 1.
        if not record["passed"]:
            errors.append(f"Keyed {label} differs from the planned source-to-weapon frame")
    if not key_weapon_frame["samples"]:
        warnings.append("Source-to-weapon key checks unavailable: weapon_frame or keyed weapon metadata is absent")
    first = frames[0]
    main = first["prop_hatchet"]
    local_length = norm(main["local_position"])
    scale = norm(subtract(main["global_position"], first["hand_r"]["global_position"])) / local_length if local_length > 1e-8 else None
    if scale is None:
        hand_local_length = norm(first["hand_r"]["local_position"])
        if hand_local_length > 1e-8:
            scale = norm(subtract(first["hand_r"]["global_position"], first["lowerarm_r"]["global_position"])) / hand_local_length
    main_prop = maximum_record()
    main_prop.update(reference="Solved ready frame 0; source rigid grip checked separately", inferred_parent_scale_cm_per_local_unit=scale,
                     maximum_local_position_error_units=0.)
    right_grip = maximum_record()
    expected_right = relative_pose(ready_pose(manifest, "hand_r"), ready_pose(manifest, "prop_hatchet"))
    right_fingers = maximum_record()
    wrist = {side: {"maximum_degrees": 0., "frame": None, "degenerate_frames": [], "frames_exceeding_tolerance": []} for side in ("r", "l")}
    for frame in sorted(frames):
        row = frames[frame]
        actual_main = row["prop_hatchet"]
        local_delta = norm(subtract(actual_main["local_position"], main["local_position"]))
        main_prop["maximum_local_position_error_units"] = max(main_prop["maximum_local_position_error_units"], local_delta)
        accumulate(main_prop, frame, local_delta * scale if scale is not None else 0.,
                   rotation_error_degrees(actual_main["local_rotation"], main["local_rotation"]))
        actual_grip = relative_pose(row["hand_r"], actual_main)
        accumulate(right_grip, frame, norm(subtract(actual_grip["position_cm"], expected_right["position_cm"])),
                   rotation_error_degrees(actual_grip["rotation"], expected_right["rotation"]))
        for bone, transform in row.items():
            if bone.startswith(FINGER_PREFIXES) and bone.endswith("_r") and bone in first:
                accumulate(right_fingers, frame, rotation_error=rotation_error_degrees(transform["local_rotation"], first[bone]["local_rotation"]), bone=bone)
        for side in ("r", "l"):
            forearm = subtract(row["hand_" + side]["global_position"], row["lowerarm_" + side]["global_position"])
            metacarpal = subtract(row["middle_01_" + side]["global_position"], row["hand_" + side]["global_position"])
            denominator = norm(forearm) * norm(metacarpal)
            if not denominator:
                wrist[side]["degenerate_frames"].append(frame)
                errors.append(f"{side} wrist has a zero-length reference segment at frame {frame}")
                continue
            bend = math.degrees(math.acos(max(-1., min(1., dot(forearm, metacarpal) / denominator))))
            if bend > wrist_tolerance_degrees:
                wrist[side]["frames_exceeding_tolerance"].append(frame)
            if wrist[side]["frame"] is None or bend > wrist[side]["maximum_degrees"]:
                wrist[side].update(maximum_degrees=bend, frame=frame)
    for side, record in wrist.items():
        record["passed"] = not record["degenerate_frames"] and not record["frames_exceeding_tolerance"]
        if not record["passed"]:
            errors.append(f"{side} wrist exceeds {wrist_tolerance_degrees:g} degrees or has invalid reference segments")
    main_prop["passed"] = main_prop["maximum_position_error_cm"] <= .05 and main_prop["maximum_rotation_error_degrees"] <= 1.
    right_grip["passed"] = right_grip["maximum_position_error_cm"] <= contact_tolerance_cm and right_grip["maximum_rotation_error_degrees"] <= contact_tolerance_degrees
    right_fingers["passed"] = right_fingers["maximum_rotation_error_degrees"] <= 1.
    if scale is None:
        warnings.append("Cannot infer native parent scale; main prop local displacement is separately reported in raw units")
        main_prop["maximum_position_error_cm"] = None
        main_prop["passed"] &= main_prop["maximum_local_position_error_units"] < 1e-8
    if not main_prop["passed"]:
        errors.append("Main prop local transform drifts relative to right-hand parent")
    if not right_grip["passed"]:
        errors.append("Right main-weapon grip differs from the source ready contact")
    if not right_fingers["passed"]:
        errors.append("Right-hand finger local rotations changed")
    left_fingers = {"key_errors": maximum_record(), "constant_interval_errors": maximum_record(),
                    "maximum_intended_change_degrees": 0., "maximum_observed_change_degrees": 0., "intentional_change_bones": []}
    finger_keys = {int(frame): row for frame, row in plan.get("finger_keyframes", {}).items()}
    finger_frames = sorted(finger_keys)
    intended_bones = set()
    for frame, pose in finger_keys.items():
        if frame not in frames:
            continue
        for bone, target in pose.items():
            reference = finger_keys[finger_frames[0]][bone]["local_rotation"]
            intended = rotation_error_degrees(target["local_rotation"], reference)
            left_fingers["maximum_intended_change_degrees"] = max(left_fingers["maximum_intended_change_degrees"], intended)
            if intended > .01:
                intended_bones.add(bone)
            accumulate(left_fingers["key_errors"], frame, rotation_error=rotation_error_degrees(frames[frame][bone]["local_rotation"], target["local_rotation"]), bone=bone)
    for first_key, last_key in zip(finger_frames, finger_frames[1:]):
        for bone, target in finger_keys[first_key].items():
            if rotation_error_degrees(target["local_rotation"], finger_keys[last_key][bone]["local_rotation"]) > .01:
                continue
            for frame in range(first_key, last_key + 1):
                if frame in frames:
                    accumulate(left_fingers["constant_interval_errors"], frame, rotation_error=rotation_error_degrees(frames[frame][bone]["local_rotation"], target["local_rotation"]), bone=bone)
    for frame, row in frames.items():
        for bone, transform in row.items():
            if bone.startswith(FINGER_PREFIXES) and bone.endswith("_l") and bone in first:
                left_fingers["maximum_observed_change_degrees"] = max(left_fingers["maximum_observed_change_degrees"], rotation_error_degrees(transform["local_rotation"], first[bone]["local_rotation"]))
    left_fingers["intentional_change_bones"] = sorted(intended_bones)
    left_fingers["passed"] = left_fingers["key_errors"]["maximum_rotation_error_degrees"] <= 1. and left_fingers["constant_interval_errors"]["maximum_rotation_error_degrees"] <= 2.
    if not left_fingers["passed"]:
        errors.append("Left fingers miss planned key rotations or drift during a constant finger interval")
    covered = sorted(set().union(*contact_frames.values()))
    return {"clip": plan["metadata"].get("clip"), "weapon_id": manifest["weapon_id"], "passed": not errors,
            "visual_acceptance": False, "expected_frames": end + 1, "solved_frames": len(frames), "missing_frames": missing,
            "finite": not finite_errors, "nonfinite_paths": finite_errors, "contact_tolerance_cm": contact_tolerance_cm,
            "contact_tolerance_degrees": contact_tolerance_degrees, "left_contacts": contacts,
            "contact_checks_passed": all(record["passed"] for record in contacts.values()) and right_grip["passed"],
            "wrist_tolerance_degrees": wrist_tolerance_degrees, "native_key_control_check": native_control,
            "left_contact_intervals": intervals, "left_contact_frames_checked": covered,
            "free_transition_frames_excluded": sorted(set(range(end + 1)) - set(covered)),
            "key_hand_pose_errors": key_hands, "main_prop_local_drift": main_prop,
            "key_weapon_frame_errors": key_weapon_frame, "key_hand_weapon_local_errors": key_weapon_local_hands,
            "right_source_grip_error": right_grip, "right_finger_ready_drift": right_fingers,
            "left_fingers": left_fingers, "all_frame_wrist_bend": wrist,
            "native_validation_present": native_validation is not None, "errors": errors, "warnings": warnings,
            "limitations": ["Rigid contact is checked only at declared contact keys and intervals with matching endpoint contacts.",
                            "Free hand transitions are not assumed to interpolate linearly or with slerp; native Bezier interpolation is not reimplemented.",
                            "Wrist bend is measured from actual solved bones; no interpolated planned wrist angle is treated as ground truth.",
                            "Finger/handle penetration, visible magazine exchange, camera framing, mesh deformation and artistic quality require visual review."]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=OUTPUT)
    parser.add_argument("--clip", choices=tuple(WEAPONS), required=True)
    parser.add_argument("--revision", default="A01")
    parser.add_argument("--contact-tolerance-cm", type=float, default=.5)
    parser.add_argument("--contact-tolerance-degrees", type=float, default=5.)
    parser.add_argument("--wrist-tolerance-degrees", type=float, default=30.)
    parser.add_argument("--key-control-tolerance-cm", type=float, default=.1)
    args = parser.parse_args(argv)
    if not args.revision.isalnum():
        parser.error("Revision must be alphanumeric")
    folder = args.root.resolve()
    prefix = folder / "weapon-native" / f"{args.clip}-{args.revision}"
    snapshot_manifest = Path(str(prefix) + "-source-manifest.json")
    manifest_path = snapshot_manifest if snapshot_manifest.is_file() else folder / "weapon-inputs" / WEAPONS[args.clip] / "weapon-manifest.json"
    paths = {"manifest": manifest_path,
             "plan": Path(str(prefix) + "-plan.json"), "transforms": Path(str(prefix) + "-transforms.json"),
             "native_validation": Path(str(prefix) + "-validation.json")}
    data, receipts = {}, {}
    for name, path in paths.items():
        if name == "native_validation" and not path.is_file():
            data[name] = None
            continue
        contents = path.read_bytes()
        data[name] = json.loads(contents)
        receipts[name] = {"path": str(path), "sha256": hashlib.sha256(contents).hexdigest()}
    report = validate(data["manifest"], data["plan"], data["transforms"], data["native_validation"],
                      args.contact_tolerance_cm, args.contact_tolerance_degrees,
                      args.wrist_tolerance_degrees, args.key_control_tolerance_cm)
    report.update(checked_utc=datetime.now(timezone.utc).isoformat(), inputs=receipts,
                  source_manifest_is_candidate_snapshot=manifest_path == snapshot_manifest)
    destination = Path(str(prefix) + "-motion-validation.json")
    destination.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps({"path": str(destination), "passed": report["passed"], "frames": report["solved_frames"],
                      "contacts": {name: {key: value for key, value in result.items() if key.startswith("maximum_") or key == "samples"} for name, result in report["left_contacts"].items()},
                      "errors": report["errors"], "warnings": report["warnings"]}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
