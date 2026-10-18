"""Read-only geometry for an isolated native Cascadeur hatchet candidate.

No Blender or Cascadeur imports. Values are Cascadeur Y-up centimetres.
Source animation samples come from the immutable v3 export receipt. Native
controller offsets are restored to the source hand orientation before posing.
"""
from __future__ import annotations

import math


def add(a, b):
    return [x + y for x, y in zip(a, b)]


def sub(a, b):
    return [x - y for x, y in zip(a, b)]


def scale(s, a):
    return [s * x for x in a]


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def length(a):
    return math.sqrt(dot(a, a))


def unit(a):
    magnitude = length(a)
    if magnitude < 1e-9:
        raise ValueError("Degenerate direction")
    return scale(1 / magnitude, a)


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]]


def transpose(matrix):
    return [list(column) for column in zip(*matrix)]


def mv(matrix, vector):
    return [dot(row, vector) for row in matrix]


def mm(a, b):
    return [[dot(row, column) for column in zip(*b)] for row in a]


def angle(a, b):
    return math.degrees(math.acos(max(-1, min(1, dot(unit(a), unit(b))))))


def alignment(a, b):
    """Minimal rotation from a to b, retaining roll about the hand axis."""
    a, b = unit(a), unit(b)
    v = cross(a, b)
    c = max(-1, min(1, dot(a, b)))
    identity = [[1., 0., 0.], [0., 1., 0.], [0., 0., 1.]]
    if c > 1 - 1e-12:
        return identity
    if c < -1 + 1e-8:
        axis = unit(cross(a, [1., 0., 0.] if abs(a[0]) < .8 else [0., 1., 0.]))
        return [[2 * axis[i] * axis[j] - identity[i][j] for j in range(3)] for i in range(3)]
    skew = [[0., -v[2], v[1]], [v[2], 0., -v[0]], [-v[1], v[0], 0.]]
    squared = mm(skew, skew)
    return [[identity[i][j] + skew[i][j] + squared[i][j] / (1 + c)
             for j in range(3)] for i in range(3)]


def elbow(shoulder, wrist, pole, upper, lower):
    delta = sub(wrist, shoulder)
    reach = length(delta)
    if reach >= upper + lower or reach <= abs(upper - lower):
        raise ValueError(f"Impossible reach: {reach:g} cm")
    direction = unit(delta)
    along = (upper * upper - lower * lower + reach * reach) / (2 * reach)
    radius = math.sqrt(max(0., upper * upper - along * along))
    transverse = sub(pole, shoulder)
    transverse = unit(sub(transverse, scale(dot(transverse, direction), direction)))
    return add(shoulder, add(scale(along, direction), scale(radius, transverse)))


CONVERSION = [[1., 0., 0.], [0., 0., 1.], [0., -1., 0.]]


def _build_targets(manifest, native_reference, clip_name, pose_builder, closed):
    """Return {keyframes: {int: {controller: xyz}}, metadata: {...}}.

    Keyframes are native Cascadeur zero-based times. The v3 samples are one-based.
    The result only touches right shoulder, elbow, and the three hand controls.
    """
    record = next(item for item in manifest["files"]
                  if item["path"] == f"survivor-fps-{clip_name.replace('_', '-')}.fbx")
    samples = record["samples"]

    def source(frame):
        sample = samples[str(frame + 1)]
        positions = {name: scale(100., mv(CONVERSION, value))
                     for name, value in sample["bone_positions_m"].items()}
        rotations = {name: mm(CONVERSION, matrix)
                     for name, matrix in sample["bone_rotations"].items()}
        return positions, rotations

    start, start_rotations = source(0)
    native_start = native_reference["joints"]["0"]["hand_r"]["global_rotation"]
    start_rotation = start_rotations["hand_r"]
    conjugated = mm(start_rotation, transpose(CONVERSION))
    difference = lambda a, b: max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(3))
    correct_error = difference(start_rotation, native_start)
    conjugated_error = difference(conjugated, native_start)
    if correct_error > .1 or correct_error >= conjugated_error:
        raise ValueError("Native joint basis does not match C @ source_rotation")
    upper = length(sub(start["lowerarm_r"], start["upperarm_r"]))
    lower = length(sub(start["hand_r"], start["lowerarm_r"]))
    native_start_joints = native_reference["joints"]["0"]
    native_shoulder = native_start_joints["upperarm_r"]["global_position"]
    native_elbow = native_start_joints["lowerarm_r"]["global_position"]
    native_wrist = native_start_joints["hand_r"]["global_position"]
    native_hinge_offset = sub(native_reference["points"]["0"]["lowerarm_AdditionalPoint_r"],
                              native_elbow)
    hinge_radius = length(native_hinge_offset)
    native_hinge_axis = unit(cross(sub(native_elbow, native_shoulder),
                                  sub(native_wrist, native_elbow)))
    hinge_sign = 1. if dot(native_hinge_offset, native_hinge_axis) >= 0 else -1.
    hinge_reference_error = length(sub(native_hinge_offset,
                                       scale(hinge_sign * hinge_radius, native_hinge_axis)))
    if hinge_reference_error > .01:
        raise ValueError("Native elbow additional point does not match the shared hinge axis")

    poses = pose_builder(source, start)
    result = {"keyframes": {}, "metadata": {
        "basis": "Cascadeur Y-up cm; Blender frame = Cascadeur frame + 1",
        "rotation_conversion": "C @ R; bone-local axes are unchanged by FBX",
        "C_times_R_error_frame0": correct_error,
        "C_times_R_times_C_transpose_error_frame0": conjugated_error,
        "upper_arm_cm": upper, "forearm_cm": lower,
        "wrist_policy": "Preserve source forearm-to-metacarpal bend, clamp at 10 degrees, minimal global reorientation; finger/prop locals unchanged",
        "elbow_additional_policy": "Shared elbow hinge: E + sign * radius * normalize(cross(E-S, W-E)); perpendicular to both limb segments",
        "elbow_hinge_radius_cm": hinge_radius, "elbow_hinge_sign": hinge_sign,
        "native_hinge_reference_error_cm": hinge_reference_error,
        "poses": {}}}
    for frame, (shoulder, wrist, pole) in poses.items():
        reference_frame = 0 if closed and frame in (36, 42) else frame
        original, rotations = source(reference_frame)
        bend = elbow(shoulder, wrist, pole, upper, lower)
        original_forearm = sub(original["hand_r"], original["lowerarm_r"])
        desired_forearm = sub(wrist, bend)
        original_long = sub(original["middle_01_r"], original["hand_r"])
        preserve_bend = alignment(original_forearm, desired_forearm)
        desired_long = mv(preserve_bend, original_long)
        wrist_angle = angle(desired_forearm, desired_long)
        if wrist_angle > 10.:
            axis = unit(cross(unit(desired_forearm), unit(desired_long)))
            # Target direction on the same bend plane, at exactly 10 degrees.
            tangent = cross(axis, unit(desired_forearm))
            target_long = add(scale(math.cos(math.radians(10)), unit(desired_forearm)),
                              scale(math.sin(math.radians(10)), tangent))
            preserve_bend = mm(alignment(desired_long, target_long), preserve_bend)
            desired_long = mv(preserve_bend, original_long)
            wrist_angle = angle(desired_forearm, desired_long)
        desired_rotation = mm(preserve_bend, rotations["hand_r"])
        native_hand = native_reference["joints"][str(reference_frame)]["hand_r"]
        # Undo native import/control-generation distortion before candidate posing.
        correction = mm(desired_rotation, transpose(native_hand["global_rotation"]))
        points = {"upperarm_MainPoint_r": list(shoulder),
                  "lowerarm_MainPoint_r": bend,
                  "hand_MainPoint_r": list(wrist)}
        for name in ("hand_DirectionPoint_r", "hand_AdditionalPoint_r"):
            relative = sub(native_reference["points"][str(reference_frame)][name],
                           native_hand["global_position"])
            points[name] = add(wrist, mv(correction, relative))
        # This is a shared hinge control, not an independent forearm-twist
        # marker. Its offset must be perpendicular to both limb segments.
        hinge_axis = unit(cross(sub(bend, shoulder), desired_forearm))
        points["lowerarm_AdditionalPoint_r"] = add(bend, scale(hinge_sign * hinge_radius, hinge_axis))
        reach = length(sub(wrist, shoulder))
        result["keyframes"][frame] = points
        result["metadata"]["poses"][frame] = {
            "shoulder": list(shoulder), "elbow": bend, "wrist": list(wrist),
            "forearm_unit": unit(desired_forearm), "metacarpal_unit": unit(desired_long),
            "hand_rotation": desired_rotation,
            "reach_cm": reach, "reach_fraction": reach / (upper + lower),
            "wrist_angle_degrees": wrist_angle,
            "original_v3_wrist_angle_degrees": angle(original_forearm, original_long),
            "native_reference_wrist_angle_degrees": angle(
                sub(native_hand["global_position"], native_reference["joints"][str(reference_frame)]["lowerarm_r"]["global_position"]),
                sub(native_reference["joints"][str(reference_frame)]["middle_01_r"]["global_position"], native_hand["global_position"]))}
    # End pose uses exactly the same source frame/control offsets as the start.
    if closed and result["keyframes"][0] != result["keyframes"][42]:
        raise AssertionError("Candidate loop endpoints differ")
    return result


def build_hatchet_targets(manifest, native_reference):
    """Ten sparse hatchet poses, ending at the exact starting pose."""
    def poses(source, start):
        # The raised wind-up stays inside the unchanged 22 mm v3 FPS camera.
        # The contact shoulder advances ~5.3 cm and lifts ~2 cm for reach.
        shoulder = start["upperarm_r"]
        return {
            0: (shoulder, start["hand_r"], start["lowerarm_r"]),
            6: (shoulder, [-25., 154., 38.], [-40., 151., 19.]),
            12: (shoulder, [-22., 174., 36.], [-42., 171., 18.]),
            15: ([-19., 161., 11.], [-21., 171., 40.], [-40., 170., 20.]),
            18: ([-19., 162., 15.], [-14., 146., 61.], [-40., 145., 25.]),
            21: ([-19., 161., 16.], [-12., 138., 56.], [-40., 145., 25.]),
            25: ([-19., 160.5, 12.], [-17., 140., 47.], [-36., 146., 23.]),
            31: (shoulder, [-22., 144., 43.], [-36., 143., 20.]),
            36: (shoulder, start["hand_r"], start["lowerarm_r"]),
            42: (shoulder, start["hand_r"], start["lowerarm_r"]),
        }
    return _build_targets(manifest, native_reference, "hatchet_chop", poses, True)


def build_torch_targets(manifest, native_reference):
    """A gradual forward presentation, restrained overshoot and settled hold."""
    def poses(source, start):
        result = {}
        # Source shoulder and elbow plane are retained. The forward wrist
        # distance gradually increases 12%; high overshoot settles to +1 cm.
        for frame, amount, lift in ((0, 0., 0.), (6, .25, .3), (10, .5, 1.),
                                    (15, .85, 2.), (18, 1., 1.5), (23, 1., .6),
                                    (30, 1., 1.), (36, 1., 1.), (48, 1., 1.)):
            positions, rotations = source(frame)
            shoulder, wrist = positions["upperarm_r"], list(positions["hand_r"])
            wrist[2] += .12 * amount * (wrist[2] - shoulder[2])
            wrist[1] += lift
            result[frame] = (shoulder, wrist, positions["lowerarm_r"])
        return result
    result = _build_targets(manifest, native_reference, "torch_equip", poses, False)
    # The source is settled from frame 33; the final two targets must match.
    if result["keyframes"][36] != result["keyframes"][48]:
        # Native control captures can differ by solver epsilon; retain the
        # exact same authored hold target for the final endpoint.
        result["keyframes"][48] = {name: list(value) for name, value in result["keyframes"][36].items()}
        result["metadata"]["poses"][48] = dict(result["metadata"]["poses"][36])
    return result
