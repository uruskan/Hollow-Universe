"""Unaccepted, isolated TPS hatchet-chop blocking proposal.

Pure NumPy: no application API, file I/O, or changes to the Blender audition.
``build_poses(skeleton, baseline)`` returns 43 world-space poses at 30 fps.
Positions are centimetres in Cascadeur's Y-up, +Z-forward coordinates;
rotation matrices retain each imported joint's local axes (column vectors).

This is an authored comparison candidate, not a natural-motion or grip-quality
certification.  The right hand follows the forearm instead of compensating for
the old TPS blade yaw.  Its shaft contact offset and fitted finger rotations
come from baseline frame zero; only the prop's yaw about its own +Z shaft is
changed by +77.08 degrees.  Recheck the oval handle and all finger contact.
"""

from __future__ import annotations

import numpy as np


C = np.array(((1., 0., 0.), (0., 0., 1.), (0., -1., 0.)))
TIMES = np.array((0., .20, .40, .60, .70, 1.05, 1.40))


def _unit(value):
    value = np.asarray(value, dtype=float)
    norm = float(np.linalg.norm(value))
    if not np.isfinite(norm) or norm < 1.e-9:
        raise ValueError("Degenerate direction in proposed pose")
    return value / norm


def _rotation(axis, degrees):
    x, y, z = _unit(axis)
    a = np.deg2rad(degrees)
    c, s, t = np.cos(a), np.sin(a), 1. - np.cos(a)
    return np.array(((t*x*x+c, t*x*y-s*z, t*x*z+s*y),
                     (t*x*y+s*z, t*y*y+c, t*y*z-s*x),
                     (t*x*z-s*y, t*y*z+s*x, t*z*z+c)))


def _rx(a):
    return _rotation((1., 0., 0.), a)


def _ry(a):
    return _rotation((0., 1., 0.), a)


def _rz(a):
    return _rotation((0., 0., 1.), a)


def _orthogonal(matrix):
    """Remove the tiny float drift in captured imported rotation matrices."""
    u, _, vt = np.linalg.svd(np.asarray(matrix, dtype=float))
    if np.linalg.det(u @ vt) < 0.:
        u[:, -1] *= -1.
    return u @ vt


def _basis(first, second):
    a = _unit(first)
    b = _unit(np.asarray(second) - a * np.dot(a, second))
    return np.column_stack((a, b, _unit(np.cross(a, b))))


def _curve(values, time):
    """Shape-preserving C1 Hermite curve; keys are not universal full stops.

    Component extrema have zero slope, while monotone strike/recovery segments
    have shared nonzero tangents across their keys. Global endpoints settle.
    """
    v = np.asarray(values, dtype=float)
    if time <= TIMES[0]:
        return v[0].copy()
    if time >= TIMES[-1]:
        return v[-1].copy()
    h = np.diff(TIMES)
    expand = (slice(None),) + (None,) * (v.ndim - 1)
    secants = np.diff(v, axis=0) / h[expand]
    slopes = np.zeros_like(v)
    for i in range(1, len(v) - 1):
        a, b = secants[i-1], secants[i]
        same = a*b > 0.
        w1, w2 = 2.*h[i] + h[i-1], h[i] + 2.*h[i-1]
        denominator = w1 / np.where(same, a, 1.) + w2 / np.where(same, b, 1.)
        slopes[i] = np.where(same, (w1+w2) / np.where(same, denominator, 1.), 0.)
    k = int(np.searchsorted(TIMES, time, side="right") - 1)
    u = (time - TIMES[k]) / h[k]
    return ((2*u**3-3*u**2+1)*v[k] + (u**3-2*u**2+u)*h[k]*slopes[k]
            + (-2*u**3+3*u**2)*v[k+1] + (u**3-u**2)*h[k]*slopes[k+1])


def _direction_curve(values, time):
    """Interpolate heading/elevation, avoiding a normalized chord's speed spike."""
    directions = np.array([_unit(value) for value in values])
    elevation = np.arctan2(directions[:, 1], np.linalg.norm(directions[:, (0, 2)], axis=1))
    heading = np.unwrap(np.arctan2(directions[:, 0], directions[:, 2]))
    pitch, yaw = _curve(np.column_stack((elevation, heading)), time)
    return np.array((np.sin(yaw)*np.cos(pitch), np.sin(pitch), np.cos(yaw)*np.cos(pitch)))


def _rest_data(skeleton):
    bones = {b["name"]: b for b in skeleton["bones"]}
    if len(bones) != 54 or len(bones) != len(skeleton["bones"]):
        raise ValueError("Expected the preserved 54-joint TPS skeleton")
    positions = {n: C @ np.asarray(b["head_m"], dtype=float) * 100.
                 for n, b in bones.items()}
    rotations = {n: _orthogonal(C @ np.asarray(b["matrix_local"], dtype=float)[:3, :3])
                 for n, b in bones.items()}
    parents = {n: b["parent"] for n, b in bones.items()}
    offsets = {n: rotations[p].T @ (positions[n] - positions[p])
               for n, p in parents.items() if p is not None}
    locals_ = {n: rotations[p].T @ rotations[n]
               for n, p in parents.items() if p is not None}
    return bones, positions, rotations, parents, offsets, locals_


def build_poses(skeleton, baseline):
    """Return every imported joint's position/rotation for frames 0 through 42.

    ``skeleton`` is skeleton-rest.json; ``baseline`` is the parsed
    baseline-animation-transforms.json. Only its world frame-zero transforms
    are used. Local translations in that capture are metres and deliberately
    are not mistaken for centimetres.
    """
    bones, rest_p, rest_r, parent, offsets, rest_local = _rest_data(skeleton)
    base = baseline["joints"]
    missing = sorted(set(bones) - set(base))
    if missing:
        raise ValueError("Baseline is missing joints: " + ", ".join(missing))
    base_p = {n: np.asarray(base[n]["0"]["global_position"], dtype=float) for n in bones}
    base_r = {n: _orthogonal(base[n]["0"]["global_rotation"]) for n in bones}
    # These are measured hand-relative coordinates, independent of world pose.
    prop_offset = base_r["hand_r"].T @ (base_p["prop_hatchet"] - base_p["hand_r"])
    prop_local = base_r["hand_r"].T @ base_r["prop_hatchet"]
    # props.py builds canonical vertices (shaft +Z, edge -Y), then multiplies
    # EVERY vertex by the prop bone rest matrix before binding. Thus skinning
    # is pose @ inverse(rest) @ rest @ canonical_vertex = pose @ canonical.
    # The bone's head-to-tail +Y axis is NOT the mesh shaft. In the exported
    # armature-space bind mesh the shaft points along rest[:, 2] (roughly -Y
    # in Blender), not Blender-world +Z. Explicitly derive the local axis from
    # that actual construction convention instead of inferring it from tail.
    bind_shaft_world = rest_r["prop_hatchet"] @ np.array((0., 0., 1.))
    shaft_bone = _unit(rest_r["prop_hatchet"].T @ bind_shaft_world)
    prop_local = prop_local @ _rotation(shaft_bone, 77.08)
    fingers = {n for n in bones if n.startswith(("thumb_", "index_", "middle_", "ring_", "pinky_"))}
    finger_local = {n: base_r[parent[n]].T @ base_r[n] for n in fingers}
    hand_along = {side: _unit(rest_r["hand_"+side].T @
                              (rest_p["middle_01_"+side] - rest_p["hand_"+side]))
                  for side in ("l", "r")}
    right_reference = _basis(hand_along["r"], prop_local @ shaft_bone)

    pelvis_keys = ((0., 92.5, 0.), (-2., 91., -3.), (-3., 91., -4.),
                   (2., 89., 5.), (3., 88.5, 7.), (1., 91., 2.), (0., 92.5, 0.))
    yaw_keys = (-3., -8., -16., 10., 16., 3., -3.)
    lean_keys = (3., 2., 1., 8., 10., 4., 3.)
    upper_keys = ((-.50, -.80, .33), (-.60, -.35, .72), (-.55, -.05, .834),
                  (-.35, -.70, .62), (-.28, -.80, .53), (-.47, -.76, .45), (-.50, -.80, .33))
    # The raised forearm remains slightly forward. This places the axe behind
    # the hand without folding the wrist or sending the hand behind the skull.
    lower_keys = ((.45, -.17, .88), (.20, .72, .66), (.14, .96, .24),
                  (.28, -.65, .706), (.34, -.77, .54), (.50, -.22, .84), (.45, -.17, .88))
    left_upper = ((.45, -.85, .26), (.45, -.79, .41), (.45, -.75, .48),
                  (.45, -.65, .61), (.45, -.70, .55), (.45, -.82, .35), (.45, -.85, .26))
    left_lower = ((-.25, -.15, .96), (-.35, .03, .94), (-.45, .10, .89),
                  (-.15, .15, .977), (-.15, .05, .987), (-.25, -.10, .96), (-.25, -.15, .96))
    feet = {"l": np.array((20., rest_p["foot_l"][1], 12.)),
            "r": np.array((-20., rest_p["foot_r"][1], -10.))}
    poses = []
    right_wrist_local = None
    for frame in range(43):
        t = frame / 30.
        p, r = {}, {}

        def fk(name, local=None):
            par = parent[name]
            p[name] = p[par] + r[par] @ offsets[name]
            r[name] = r[par] @ (rest_local[name] if local is None else local)

        def incremental(name, yaw, lean):
            par = parent[name]
            fk(name, rest_r[par].T @ _ry(yaw) @ _rx(lean) @ rest_r[name])

        def orient_limb(name, rest_direction, direction, old_plane, new_plane):
            r[name] = (_basis(direction, new_plane) @ _basis(rest_direction, old_plane).T @ rest_r[name])

        pelvis = _curve(pelvis_keys, t)
        # Root translation carries the hip displacement; root-to-pelvis offset
        # remains the preserved rest offset, rather than stretching that link.
        r["Root"] = rest_r["Root"]
        p["Root"] = pelvis - (rest_p["pelvis"] - rest_p["Root"])
        yaw, lean = float(_curve(yaw_keys, t)), float(_curve(lean_keys, t))
        incremental("pelvis", yaw*.35, lean*.20)
        incremental("spine_01", yaw*.20, lean*.25)
        incremental("spine_02", yaw*.25, lean*.30)
        incremental("spine_03", yaw*.20, lean*.25)
        for name in ("clavicle_l", "clavicle_r", "neck_01"):
            fk(name)
        # Keep gaze near the target, countering the larger torso rotation.
        r["neck_01"] = _ry(yaw*.15) @ _rx(3.) @ rest_r["neck_01"]
        fk("head")
        r["head"] = _ry(yaw*.08) @ _rx(5.) @ rest_r["head"]
        torso_delta = r["spine_03"] @ rest_r["spine_03"].T

        for side in ("r", "l"):
            upper, lower, hand = "upperarm_"+side, "lowerarm_"+side, "hand_"+side
            fk(upper)
            a0, b0 = rest_p[lower] - rest_p[upper], rest_p[hand] - rest_p[lower]
            upper_direction = _direction_curve(upper_keys if side == "r" else left_upper, t)
            lower_direction = _direction_curve(lower_keys if side == "r" else left_lower, t)
            u = torso_delta @ upper_direction
            v = torso_delta @ lower_direction
            plane0, plane = _unit(np.cross(a0, b0)), _unit(np.cross(u, v))
            orient_limb(upper, a0, u, plane0, plane)
            fk(lower)
            orient_limb(lower, b0, v, plane0, plane)
            fk(hand)
            if side == "r":
                # Sagittal perpendicular follows the forearm continuously: up
                # at ready, behind at anticipation, forward at follow-through.
                # Independent shaft interpolation can pass nearly parallel to
                # the forearm and flip its projected orientation mid-strike.
                helper = torso_delta @ _unit((0., lower_direction[2], -lower_direction[1]))
                r[hand] = _basis(v, helper) @ right_reference.T
                if right_wrist_local is None:
                    right_wrist_local = r[lower].T @ r[hand]
                # The hand and forearm share axial roll, retaining the aligned
                # ready wrist's fixed local relationship. Otherwise the elbow
                # plane and independent shaft plane can twist the wrist ~45
                # degrees while its metacarpal direction still looks straight.
                r[lower] = r[hand] @ right_wrist_local.T
                p[hand] = p[lower] + r[lower] @ offsets[hand]
            else:
                # Relaxed free-hand shape is retained; aim its metacarpals with
                # the forearm, keeping its palm generally inward/downward.
                local_reference = _basis(hand_along[side], rest_r[hand].T @ np.array((1., 0., 0.)))
                r[hand] = _basis(v, torso_delta @ np.array((-1., -.2, 0.))) @ local_reference.T

        for side in ("l", "r"):
            thigh, calf, foot, toe = "thigh_"+side, "calf_"+side, "foot_"+side, "ball_"+side
            fk(thigh)
            a0, b0 = rest_p[calf] - rest_p[thigh], rest_p[foot] - rest_p[calf]
            length_a, length_b = np.linalg.norm(a0), np.linalg.norm(b0)
            target = feet[side]
            reach = target - p[thigh]
            distance = float(np.linalg.norm(reach))
            if distance >= length_a + length_b - 1.e-6 or distance <= abs(length_a-length_b) + 1.e-6:
                raise ValueError(f"Unreachable planted {side} foot at frame {frame}")
            line = reach / distance
            pole = np.array((.08 if side == "l" else -.08, 0., 1.))
            bend = _unit(pole - line*np.dot(pole, line))
            along = (length_a**2 + distance**2 - length_b**2) / (2.*distance)
            height = np.sqrt(max(0., length_a**2 - along**2))
            knee = p[thigh] + line*along + bend*height
            u, v = _unit(knee-p[thigh]), _unit(target-knee)
            plane0, plane = _unit(np.cross(a0, b0)), _unit(np.cross(u, v))
            orient_limb(thigh, a0, u, plane0, plane)
            fk(calf)
            orient_limb(calf, b0, v, plane0, plane)
            fk(foot)
            r[foot] = _ry(3. if side == "l" else -3.) @ rest_r[foot]
            fk(toe)

        pending = set(fingers)
        while pending:
            available = sorted(n for n in pending if parent[n] in p)
            if not available:
                raise ValueError("Cyclic or missing finger hierarchy")
            for name in available:
                fk(name, finger_local[name])
                pending.remove(name)
        p["prop_hatchet"] = p["hand_r"] + r["hand_r"] @ prop_offset
        r["prop_hatchet"] = r["hand_r"] @ prop_local
        if set(p) != set(bones) or set(r) != set(bones):
            raise ValueError("Pose does not contain exactly the preserved 54 joints")
        poses.append({name: {"position": p[name].tolist(), "rotation": r[name].tolist()} for name in bones})
    return poses


def diagnose(poses, skeleton):
    """Numerical guardrails only; does not certify anatomy or surface contact."""
    bones, rest_p, _, parents, _, _ = _rest_data(skeleton)
    if len(poses) != 43:
        raise ValueError("Expected 43 frame poses")
    max_length_error = max_rotation_error = max_foot_drift = max_wrist_bend = 0.
    max_step_angle = 0.
    step_peak = None
    for i, pose in enumerate(poses):
        if set(pose) != set(bones):
            raise ValueError("Joint set changed")
        for name, entry in pose.items():
            p, r = np.asarray(entry["position"]), np.asarray(entry["rotation"])
            if not np.all(np.isfinite(p)) or not np.all(np.isfinite(r)):
                raise ValueError("Non-finite pose")
            max_rotation_error = max(max_rotation_error, float(np.max(np.abs(r.T@r-np.eye(3)))), abs(float(np.linalg.det(r))-1.))
            par = parents[name]
            if par and name != "prop_hatchet":
                actual = np.linalg.norm(p-np.asarray(pose[par]["position"]))
                expected = np.linalg.norm(rest_p[name]-rest_p[par])
                max_length_error = max(max_length_error, abs(float(actual-expected)))
            if name in ("foot_l", "foot_r", "ball_l", "ball_r"):
                max_foot_drift = max(max_foot_drift, float(np.linalg.norm(p-np.asarray(poses[0][name]["position"]))))
            if i:
                before = np.asarray(poses[i-1][name]["rotation"])
                angle = float(np.rad2deg(np.arccos(np.clip((np.trace(before.T@r)-1.)/2., -1., 1.))))
                if angle > max_step_angle:
                    max_step_angle, step_peak = angle, {"joint": name, "frame": i}
        forearm = _unit(np.asarray(pose["hand_r"]["position"])-np.asarray(pose["lowerarm_r"]["position"]))
        hand = _unit(np.asarray(pose["middle_01_r"]["position"])-np.asarray(pose["hand_r"]["position"]))
        max_wrist_bend = max(max_wrist_bend, float(np.rad2deg(np.arccos(np.clip(np.dot(forearm, hand), -1., 1.)))))
    return {"frames": len(poses), "joints": len(bones),
            "max_rest_link_length_error_cm": max_length_error,
            "max_rotation_orthogonality_or_determinant_error": max_rotation_error,
            "max_foot_or_toe_position_drift_cm": max_foot_drift,
            "max_right_forearm_to_metacarpal_angle_deg": max_wrist_bend,
            "max_world_rotation_step_deg": max_step_angle, "rotation_step_peak": step_peak,
            "boundary": "Mathematical pose checks only; no skin, finger, floor, collision, or visual acceptance."}
