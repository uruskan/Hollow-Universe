"""Original, semantic IK choreography for the isolated character trial.

This module has no Blender dependency. ``sample`` returns the pose contract of
``art/characters/authored.py``; it never reads the legacy animation library.
Coordinates: Blender metres, +Z up, -Y forward, +X anatomical left.

Landmarks are measured in the armature's rest space: ``hip_z``, ``shoulder_z``,
``ankle_l`` and ``ankle_r`` (three-component positions), and either ``height``
or ``scale`` (height / 1.8). Defaults describe a 1.8 m adult. All motion distances
scale with height; the two measured heights retain the subject's proportions.

The locomotion is in place. During each support interval the foot translates
backwards at exactly ``ground_speed``; translating the character forwards at
that speed makes the supporting foot stationary in the world. Swing trajectories
preserve the support velocity at both boundaries. The ground is z=0.

Right-hand props use a PALM frame, never assumed hand-bone axes. The anatomical
upright frame has +X along the fingers, +Y along the palm normal and +Z along
``along cross normal`` (the thumb side). The former negative cross product put
the shaft upside down. Handle orientations below start from the saved static
grip study's actual palm basis. The exporter's calibrated rigid hand-to-prop
transform and finger quaternions are authoritative; the generic palm-centre
offset in ``grip_frame`` is an authoring aid, not a substitute for that fit.
For the rock, both palms are locked to a common 0.082 m contact radius. Its
centre is wrist + 0.065*along + (0.035+0.082)*normal for either hand. A prop
should be parented using this frame calculated from the ACTUAL solved hand
matrix, so IK reach clamping cannot detach it. ``grip_frame`` exposes the same
construction from semantic targets for authoring and measurements.

FPS is authored separately: its shoulder/root frame is stable, its action arcs
are compressed into the camera frustum, and its hands stay in front of the face.
It is deliberately not a crop of the TPS motion.
"""

from copy import deepcopy
import math


FPS = 30
GRIP = {"along": 0.065, "normal": 0.035, "rock_radius": 0.082}
HANDLE_ALONG = (.959585, -.267608, -.087079)
HANDLE_NORMAL = (.220192, .906655, -.359851)
# Source rig rest heads, normalized to 1.8 m. Passing rest_heads in landmarks
# replaces these with the current skeleton's exact measurements.
_REST_HEADS = {
    "pelvis": (0., -.005509277, .949346952),
    "spine_01": (0., .026844143, 1.036385006),
    "spine_02": (0., .018328765, 1.105042965),
    "spine_03": (0., .032115791, 1.165263490),
    "clavicle_l": (.023324818, -.026147551, 1.436172341),
    "clavicle_r": (-.023324818, -.026147551, 1.436172341),
    "upperarm_l": (.190028992, -.016671670, 1.407444136),
    "upperarm_r": (-.190028992, -.016671670, 1.407444136),
    "lowerarm_l": (.357369102, -.018882915, 1.218585474),
    "lowerarm_r": (-.357369102, -.018882915, 1.218585474),
    "hand_l": (.489363533, -.206476304, 1.092871402),
    "hand_r": (-.489363533, -.206476304, 1.092871402),
}
CLIPS = [
    {"name": "idle", "duration": 4.0, "loop": True, "prop": None,
     "contact": None, "ground_speed": 0.0, "description": "Quiet weight, breathing and relaxed hands"},
    {"name": "walk", "duration": 1.0, "loop": True, "prop": None,
     "contact": [0.0, 0.5], "ground_speed": 1.2, "description": "Heel-to-toe walk; opposite arm and leg"},
    {"name": "sprint", "duration": 22.0 / 30.0, "loop": True, "prop": None,
     "contact": [0.0, 11.0 / 30.0], "ground_speed": 4.0,
     "description": "Forefoot drive, flight phases and compact arm recovery"},
    {"name": "crouch_walk", "duration": 1.4, "loop": True, "prop": None,
     "contact": [0.0, 0.7], "ground_speed": 0.55,
     "description": "Low balanced gait, bent knees and long support"},
    {"name": "jump_land", "duration": 1.8, "loop": False, "prop": None,
     "contact": 1.0, "takeoff": 0.3, "ground_speed": 0.0,
     "description": "Countermovement, ballistic flight, compression and recovery"},
    {"name": "rock_strike", "duration": 1.5, "loop": False, "prop": "rock",
     "contact": 19.0 / 30.0, "ground_speed": 0.0,
     "description": "Two-handed overhead blow with one rigid grip frame"},
    {"name": "hatchet_chop", "duration": 1.4, "loop": False, "prop": "hatchet",
     "contact": 0.6, "ground_speed": 0.0,
     "description": "Shoulder-led diagonal chop, impact and weighted recovery"},
    {"name": "torch_equip", "duration": 1.6, "loop": False, "prop": "torch",
     "contact": None, "hold_start": 1.1, "ground_speed": 0.0,
     "description": "Raise from belt level, settle into an upright inspection hold"},
]
CLIP_BY_NAME = {clip["name"]: clip for clip in CLIPS}
for _clip in CLIPS:
    _clip["fps"] = FPS
    _clip["frames"] = round(_clip["duration"] * FPS)

_CURL = {
    "relaxed": ((14., 20., 12.), (8., 10., 6.)),
    "ready": ((24., 31., 20.), (12., 17., 10.)),
    "rock": ((38., 48., 32.), (25., 31., 22.)),
    "handle": ((58., 72., 47.), (31., 37., 26.)),
}
_FORWARD = (0., -1., 0.)
_DOWN = (0., 0., -1.)


def _clamp(x, lo=0., hi=1.):
    return max(lo, min(hi, x))


def _smooth(x):
    x = _clamp(x)
    return x * x * (3. - 2. * x)


def _add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def _sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def _mul(a, v):
    return tuple(x * v for x in a)


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def _unit(v):
    length = math.sqrt(_dot(v, v))
    if length < 1.e-9:
        raise ValueError("Degenerate character-trial orientation")
    return _mul(v, 1. / length)


def _turn(v, xyz):
    """XYZ Euler rotation in world axes, degrees; no mathutils dependency."""
    x, y, z = v
    a, b, c = (math.radians(value) for value in xyz)
    x, y, z = x, math.cos(a) * y - math.sin(a) * z, math.sin(a) * y + math.cos(a) * z
    x, y, z = math.cos(b) * x + math.sin(b) * z, y, -math.sin(b) * x + math.cos(b) * z
    return (math.cos(c) * x - math.sin(c) * y, math.sin(c) * x + math.cos(c) * y, z)


def _mix(a, b, u):
    if isinstance(a, dict):
        return {k: _mix(a[k], b[k], u) for k in a}
    if isinstance(a, (tuple, list)):
        return tuple(_mix(x, y, u) for x, y in zip(a, b))
    return a + (b - a) * u


def _keys(keys, time):
    if time <= keys[0][0]:
        return deepcopy(keys[0][1])
    if time >= keys[-1][0]:
        return deepcopy(keys[-1][1])
    for (start, a), (end, b) in zip(keys, keys[1:]):
        if time <= end:
            return _mix(a, b, _smooth((time - start) / (end - start)))
    return deepcopy(keys[-1][1])


def _measure(landmarks):
    s = float(landmarks.get("scale", float(landmarks.get("height", 1.8)) / 1.8))
    if not math.isfinite(s) or s <= 0.:
        raise ValueError("Character scale must be finite and positive")
    result = {"scale": s, "hip": float(landmarks.get("hip_z", .96 * s)) / s,
              "shoulder": float(landmarks.get("shoulder_z", 1.48 * s)) / s}
    for side, sign in (("l", 1.), ("r", -1.)):
        result["ankle_" + side] = tuple(float(v) / s for v in
            landmarks.get("ankle_" + side, (.10 * sign * s, .025 * s, .075 * s)))
    result["rest"] = {name: tuple(float(v) / s for v in point)
                      for name, point in landmarks.get("rest_heads", {}).items()}
    for name, point in _REST_HEADS.items():
        if name not in result["rest"]:
            # Preserve measured hip/shoulder proportions even without rest_heads.
            fraction = _clamp((point[2] - _REST_HEADS["pelvis"][2]) /
                              (_REST_HEADS["upperarm_l"][2] - _REST_HEADS["pelvis"][2]))
            delta = ((result["hip"] - _REST_HEADS["pelvis"][2]) * (1. - fraction) +
                     (result["shoulder"] - _REST_HEADS["upperarm_l"][2]) * fraction)
            result["rest"][name] = (point[0], point[1], point[2] + delta)
    return result


def _turn_chain(vector, rotations):
    for rotation in reversed(rotations):
        vector = _turn(vector, rotation)
    return vector


def _shoulder(pose, m, side):
    """Exact authored.Rig pre-IK shoulder FK, without importing Blender."""
    rest = m["rest"]
    point = _add(rest["pelvis"], pose["root"])
    rotations = [pose["body"]]
    parent = "pelvis"
    for bone in ("spine_01", "spine_02", "spine_03", "clavicle_" + side, "upperarm_" + side):
        point = _add(point, _turn_chain(_sub(rest[bone], rest[parent]), rotations))
        rotations.append(pose["rot"].get(bone, (0., 0., 0.)))
        parent = bone
    return point, rotations


def _sagittal_arm(pose, m, side, swing, flex, outward=4., curl="relaxed"):
    """Author shoulder swing and elbow flexion, then give IK the actual elbow.

    Positive swing carries the upper arm back; flex folds the forearm forward.
    The elbow is therefore coherent throughout a stride instead of being chosen
    by a nearly collinear, fixed world-space pole at the passing pose.
    """
    sign = 1. if side == "l" else -1.
    shoulder, rotations = _shoulder(pose, m, side)
    rest = m["rest"]
    upper_length = math.sqrt(_dot(_sub(rest["lowerarm_" + side], rest["upperarm_" + side]),
                                  _sub(rest["lowerarm_" + side], rest["upperarm_" + side])))
    lower_length = math.sqrt(_dot(_sub(rest["hand_" + side], rest["lowerarm_" + side]),
                                  _sub(rest["hand_" + side], rest["lowerarm_" + side])))
    def direction(angle):
        a, out = math.radians(angle), math.radians(outward)
        local = (sign * math.sin(out), math.sin(a) * math.cos(out), -math.cos(a) * math.cos(out))
        return _turn_chain(local, rotations)
    upper, lower = direction(swing), direction(swing - flex)
    elbow = _add(shoulder, _mul(upper, upper_length))
    wrist = _add(elbow, _mul(lower, lower_length))
    palm = _turn_chain((-sign, 0., 0.), rotations)
    _hand(pose, side, wrist, elbow, lower, palm, curl)


def _low_elbow(pose, m, side, back=.05, down=.25, outward=.025):
    shoulder, rotations = _shoulder(pose, m, side)
    sign = 1. if side == "l" else -1.
    return _add(shoulder, _turn_chain((sign * outward, back, -down), rotations))


def _arm_lengths(m, side):
    rest = m["rest"]
    upper = _sub(rest["lowerarm_" + side], rest["upperarm_" + side])
    lower = _sub(rest["hand_" + side], rest["lowerarm_" + side])
    return math.sqrt(_dot(upper, upper)), math.sqrt(_dot(lower, lower))


def _solve_elbow(shoulder, wrist, pole, lengths):
    """Same geometric elbow as authored.Rig; used to retain the saved ready pose."""
    upper, lower = lengths
    delta = _sub(wrist, shoulder)
    distance = math.sqrt(_dot(delta, delta))
    direction = _unit(delta)
    bend = _sub(pole, shoulder)
    bend = _unit(_sub(bend, _mul(direction, _dot(bend, direction))))
    cosine = _clamp((upper * upper + distance * distance - lower * lower) /
                    (2. * upper * distance), -1., 1.)
    return _add(shoulder, _add(_mul(direction, upper * cosine),
                               _mul(bend, upper * math.sqrt(1. - cosine * cosine))))


def _local_direction(vector, rotations):
    return tuple(_dot(vector, _turn_chain(axis, rotations))
                 for axis in ((1., 0., 0.), (0., 1., 0.), (0., 0., 1.)))


def _hatchet_arm(pose, m, time):
    """Rotate the two arm segments, avoiding wrist-path IK pole singularities."""
    neutral = _base(m)
    _action_body(neutral, m, lean=3., twist=-3., shift=.025 * -3. / 24.)
    shoulder0, rotations0 = _shoulder(neutral, m, "r")
    wrist0 = (-.227678, -.304101, m["hip"] + .21879)
    pole0 = (-.37690, -.09046, m["shoulder"] - .19230)
    lengths = _arm_lengths(m, "r")
    elbow0 = _solve_elbow(shoulder0, wrist0, pole0, lengths)
    ready = (_local_direction(_unit(_sub(elbow0, shoulder0)), rotations0),
             _local_direction(_unit(_sub(wrist0, elbow0)), rotations0))
    upper, lower = _keys([
        (0., ready),
        (.18, ((-.65, -.10, -.75), (.45, -.86, .24))),
        # Raise the elbow in front of the shoulder and fold the forearm back.
        # A rearward/down upper arm with an upward/front forearm reverses the
        # anatomical bend plane and makes the following strike unwind its roll.
        (.39, ((-.20, -.90, .38), (-.10, .70, .708))),
        (.47, ((-.20, -.91, .36), (-.10, .68, .726))),
        (.60, ((-.25, -.67, -.699), (.04, -.82, .571))),
        (.69, ((-.20, -.67, -.715), (.04, -.96, .277))),
        (.86, ((-.40, -.40, -.825), (.25, -.92, .30))),
        (1.16, ready), (1.4, ready),
    ], time)
    shoulder, rotations = _shoulder(pose, m, "r")
    elbow = _add(shoulder, _mul(_turn_chain(_unit(upper), rotations), lengths[0]))
    wrist = _add(elbow, _mul(_turn_chain(_unit(lower), rotations), lengths[1]))
    return wrist, elbow


def _hand(pose, side, at, pole=None, along=None, normal=None, curl="relaxed"):
    sign = 1. if side == "l" else -1.
    pose["ik"]["hand_" + side] = {
        "at": at,
        "pole": pole if pole is not None else (sign * .24, .10, at[2] - .08),
        "along": along if along is not None else (0., -.08, -1.),
        "normal": normal if normal is not None else (-sign, -.06, 0.),
    }
    pose["fingers"][side] = _CURL[curl]


def _foot(pose, side, at, pitch=0., outward=5.):
    sign = 1. if side == "l" else -1.
    orient = (pitch, 0., sign * outward)
    pose["ik"]["foot_" + side] = {
        "at": at, "pole": (sign * .20, -.90, .45),
        "along": _turn(_FORWARD, orient), "normal": _turn(_DOWN, orient),
    }


def _base(m, fp=False):
    hip, shoulder = m["hip"], m["shoulder"]
    pose = {"root": (0., 0., -.018), "body": (0., 0., 0.),
            "rot": {"spine_01": (1., 0., 0.), "spine_02": (1., 0., 0.),
                    "spine_03": (0., 0., 0.), "neck_01": (-1., 0., 0.),
                    "head": (-1., 0., 0.), "clavicle_l": (0., 0., 0.),
                    "clavicle_r": (0., 0., 0.)}, "ik": {}, "fingers": {}}
    for side, sign in (("l", 1.), ("r", -1.)):
        a = m["ankle_" + side]
        _foot(pose, side, (a[0] + sign * .015, a[1], a[2]))
        if fp:
            _hand(pose, side, (sign * .18, -.43, 1.39),
                  (sign * .43, -.04, shoulder - .26),
                  along=(0., -1., -.08), normal=(-sign * .8, 0., -.6), curl="ready")
        else:
            _sagittal_arm(pose, m, side, 9., 25., outward=3.)
    return pose


def _idle(t, m, fp):
    p = _base(m, fp)
    a = math.tau * t / 4.
    breath = math.sin(a)
    drift = math.sin(a - .55) - math.sin(-.55)
    if fp:
        for side, sign in (("l", 1.), ("r", -1.)):
            h = p["ik"]["hand_" + side]
            h["at"] = _add(h["at"], (sign * .002 * breath, .004 * breath, .005 * breath))
    else:
        p["root"] = (.007 * drift, .002 * breath, -.018 + .0025 * breath)
        p["body"] = (0., -.7 * drift, .4 * breath)
        p["rot"]["spine_02"] = (1. + .55 * breath, .7 * drift, 0.)
        p["rot"]["spine_03"] = (-.35 * breath, 0., -.4 * breath)
        p["rot"]["head"] = (-1., .4 * drift, -.3 * breath)
        for side, sign in (("l", 1.), ("r", -1.)):
            _sagittal_arm(p, m, side, 9. + .45 * breath, 25. + .5 * drift, outward=3.)
    return p


def _stride(q, span, duty, lift, heel=-7., toe=24., running=False):
    """Return fore/aft, lift and pitch; C1 translation at stance boundaries."""
    if q < duty:
        u = q / duty
        y = -span * .5 + span * u
        pitch = heel * (1. - _smooth(u / .18)) + toe * _smooth((u - .70) / .30)
        z = .14 * max(0., math.sin(math.radians(pitch)))
        return y, z, pitch
    u = (q - duty) / (1. - duty)
    u2, u3 = u * u, u * u * u
    tangent = span * (1. - duty) / duty
    y = (2. * u3 - 3. * u2 + 1.) * span * .5
    y += (u3 - 2. * u2 + u) * tangent
    y += (-2. * u3 + 3. * u2) * -span * .5 + (u3 - u2) * tangent
    pitch = toe + (heel - toe) * _smooth(u) - 18. * math.sin(math.pi * u)
    clearance = (_smooth(u / .28) * (1. - _smooth((u - .89) / .11))
                 if running else math.sin(math.pi * u) ** 1.5)
    z = lift * clearance + .14 * max(0., math.sin(math.radians(pitch)))
    return y, z, pitch


def _gait(name, t, m, fp):
    meta = CLIP_BY_NAME[name]
    phase = t / meta["duration"]
    theta = math.tau * phase
    sin1, cos1 = math.sin(theta), math.cos(theta)
    p = _base(m, fp)
    sprint, crouch = name == "sprint", name == "crouch_walk"
    duty = .28 if sprint else .74 if crouch else .60
    span = meta["ground_speed"] * meta["duration"] * duty
    lift = .33 if sprint else .075 if crouch else .105
    for side, sign, offset in (("l", 1., 0.), ("r", -1., .5)):
        q = (phase + offset) % 1.
        y, z, pitch = _stride(q, span, duty, lift, heel=1. if sprint else -7.,
                              toe=30. if sprint else 24., running=sprint)
        a = m["ankle_" + side]
        width = .025 if crouch else .012
        _foot(p, side, (a[0] + sign * width, a[1] + y, a[2] + z), pitch, 3. if sprint else 5.)
    if fp:
        # Visual hand bob tracks footfalls without moving the eye or the shoulders.
        amp = .043 if sprint else .013 if crouch else .022
        for side, sign in (("l", 1.), ("r", -1.)):
            h = p["ik"]["hand_" + side]
            h["at"] = (sign * (.18 + .012 * cos1), -.43 + sign * amp * cos1,
                       1.39 - sign * amp * .85 * cos1 + .009 * math.cos(2. * theta))
            p["fingers"][side] = _CURL["ready" if sprint else "relaxed"]
        return p
    if sprint:
        half = phase % .5
        if half <= duty:
            u = half / duty
            bounce = -.095 - .035 * math.sin(math.pi * u) + .015 * u
        else:
            u = (half - duty) / (.5 - duty)
            bounce = -.080 - .015 * u + .075 * math.sin(math.pi * u)
        p["root"] = (.014 * sin1, -.025, bounce - .030)
        p["body"] = (10., 2.2 * sin1, 5. * cos1)
        bend, counter = 3., -9. * cos1
    elif crouch:
        p["root"] = (.022 * sin1, .10, -.30 - .012 * math.cos(2. * theta))
        p["body"] = (7., 2. * sin1, 3. * cos1)
        bend, counter = 6., -5. * cos1
    else:
        p["root"] = (.018 * sin1, 0., -.075 - .023 * math.cos(2. * theta))
        p["body"] = (1.5, 1.2 * sin1, 4. * cos1)
        bend, counter = 1.2, -7. * cos1
    p["rot"]["spine_01"] = (bend, -1.2 * sin1, counter * .30)
    p["rot"]["spine_02"] = (bend, -.8 * sin1, counter * .40)
    p["rot"]["spine_03"] = (bend * .4, -.5 * sin1, counter * .30)
    p["rot"]["neck_01"] = (-bend * 1.2, 0., 0.)
    p["rot"]["head"] = (-bend * .8, 0., -.8 * cos1)
    for side, sign in (("l", 1.), ("r", -1.)):
        swing = sign * cos1
        if sprint:
            _sagittal_arm(p, m, side, 8. + 43. * swing, 80. - 18. * swing,
                          outward=4., curl="ready")
        elif crouch:
            _sagittal_arm(p, m, side, -28. + 12. * swing, 80. - 5. * swing,
                          outward=5., curl="ready")
        else:
            _sagittal_arm(p, m, side, 3. + 22. * swing, 16. - 5. * swing,
                          outward=3.)
    return p


def _jump(t, m, fp):
    base = _base(m, fp)
    keys = [(0., deepcopy(base))]
    # z is pelvis displacement; hand values are absolute, measured about the shoulder.
    for time, drop, lean, hand_y, hand_z, spread in (
        (.17, -.22, 12., .13, m["hip"] - .04, .31),
        (.30, -.035, 4., -.28, m["shoulder"] - .10, .25),
        (.48, .37, -2., -.20, m["shoulder"] + .21, .30),
        (.65, .44, -3., -.12, m["shoulder"] + .16, .34),
        (.86, .25, 6., -.20, m["shoulder"] -.02, .34),
        (1.0, -.035, 9., -.26, m["hip"] + .13, .33),
        (1.12, -.24, 15., -.30, m["hip"] -.02, .32),
        (1.40, -.045, 3., -.10, m["hip"] + .005, .24),
        (1.8, -.018, 0., -.035, m["hip"], .225),
    ):
        p = deepcopy(base)
        if fp:
            # Camera-space anticipation and landing shock; wrists remain in view.
            amount = -drop * .14
            for side, sign in (("l", 1.), ("r", -1.)):
                _hand(p, side, (sign * (.18 + min(.035, abs(drop) * .08)),
                               -.43 - max(0., drop) * .07, 1.39 + amount),
                      (sign * .43, -.04, m["shoulder"] - .25),
                      (0., -1., -.06), (-sign * .8, 0., -.6), "ready")
        else:
            p["root"] = (0., .045 if drop < -.1 else 0., drop)
            p["body"] = (lean * .35, 0., 0.)
            p["rot"]["spine_01"] = (lean * .25, 0., 0.)
            p["rot"]["spine_02"] = (lean * .25, 0., 0.)
            p["rot"]["spine_03"] = (lean * .15, 0., 0.)
            p["rot"]["head"] = (-lean * .45, 0., 0.)
            for side, sign in (("l", 1.), ("r", -1.)):
                _hand(p, side, (sign * spread, hand_y, hand_z),
                      (sign * (.24 + .20 * abs(spread - .22)), .075, hand_z - .10),
                      (0., -.5, -.86), (-sign, 0., 0.), "relaxed")
        keys.append((time, p))
    if not fp:
        keys[-1] = (1.8, deepcopy(base))
    p = _keys(keys, t)
    if not fp and .30 < t < 1.0:
        u = (t - .30) / .70
        flight = 4. * .49 * u * (1. - u)
        p["root"] = (0., 0., -.035 + flight)
        tuck = .14 * math.sin(math.pi * u) ** 2
        for side, sign in (("l", 1.), ("r", -1.)):
            a = m["ankle_" + side]
            _foot(p, side, (a[0] + sign * .015, a[1] + .07 * math.sin(math.pi * u),
                           a[2] + flight + tuck), 16. * math.sin(math.pi * u), 5.)
    if not fp:
        swing, flex, outward = _keys([
            (0., (9., 25., 3.)), (.17, (35., 35., 5.)),
            (.30, (-55., 45., 5.)), (.48, (-65., 30., 6.)),
            (.65, (-52., 30., 7.)), (.86, (-25., 45., 7.)),
            (1.0, (-10., 40., 6.)), (1.12, (-25., 70., 6.)),
            (1.4, (4., 35., 4.)), (1.8, (9., 25., 3.)),
        ], t)
        for side in ("l", "r"):
            _sagittal_arm(p, m, side, swing, flex, outward=outward)
    return p


def _action_body(p, m, drop=0., lean=0., twist=0., shift=0., fp=False):
    if fp:
        return
    p["root"] = (shift, .015, -.035 + drop)
    p["body"] = (lean * .25, 0., twist * .30)
    p["rot"]["spine_01"] = (lean * .25, 0., twist * .25)
    p["rot"]["spine_02"] = (lean * .30, 0., twist * .25)
    p["rot"]["spine_03"] = (lean * .20, 0., twist * .20)
    p["rot"]["neck_01"] = (-lean * .25, 0., -twist * .30)
    p["rot"]["head"] = (-lean * .35, 0., -twist * .45)
    for side, sign in (("l", 1.), ("r", -1.)):
        a = m["ankle_" + side]
        _foot(p, side, (a[0] + sign * .035, a[1] - sign * .075, a[2]), outward=7.)


def _rock_pose(m, fp, center, rotation=(0., 0., 0.), lean=0., drop=0., twist=0.):
    p = _base(m, fp)
    _action_body(p, m, drop, lean, twist, -.012 * twist / 15., fp)
    along = _turn((0., -.35, .93675), rotation)
    for side, sign in (("l", 1.), ("r", -1.)):
        normal = _turn((-sign, 0., 0.), rotation)
        at = _add(center, _add(_mul(along, -GRIP["along"]),
                              _mul(normal, -(GRIP["normal"] + GRIP["rock_radius"]))))
        pole = ((sign * .42, .015 if center[2] > 1.45 else -.015,
                 min(m["shoulder"] - .14, center[2] - .10)) if fp else
                _low_elbow(p, m, side, back=-.015, down=.25, outward=.04))
        _hand(p, side, at, pole, along, normal, "rock")
    return p


def _rock(t, m, fp):
    if fp:
        descriptors = [
            (0., (0., -.47, 1.43), (8., 0., 0.), 0., 0., 0.),
            (.18, (.025, -.43, 1.44), (0., -4., -3.), 0., 0., 0.),
            (.40, (.055, -.34, 1.55), (-24., -5., -6.), 0., 0., 0.),
            (.50, (.050, -.35, 1.55), (-21., -4., -5.), 0., 0., 0.),
            (19. / 30., (-.025, -.56, 1.39), (48., 4., 4.), 0., 0., 0.),
            (.72, (-.035, -.54, 1.34), (57., 4., 6.), 0., 0., 0.),
            (.90, (-.018, -.50, 1.38), (27., 2., 3.), 0., 0., 0.),
            (1.20, (0., -.47, 1.43), (8., 0., 0.), 0., 0., 0.),
            (1.50, (0., -.47, 1.43), (8., 0., 0.), 0., 0., 0.),
        ]
    else:
        descriptors = [
            (0., (0., -.34, m["hip"] + .20), (8., 0., 0.), 3., 0., 0.),
            (.18, (-.025, -.28, m["hip"] + .18), (0., -3., -3.), 0., -.035, -5.),
            (.40, (-.055, -.16, m["shoulder"] + .17), (-27., -4., -5.), -7., .005, -9.),
            (.50, (-.050, -.16, m["shoulder"] + .16), (-23., -3., -4.), -5., 0., -7.),
            (19. / 30., (.015, -.56, m["hip"] + .17), (48., 4., 3.), 21., -.07, 7.),
            (.72, (.030, -.50, m["hip"] + .03), (58., 4., 5.), 25., -.10, 9.),
            (.90, (.015, -.43, m["hip"] + .08), (28., 2., 2.), 13., -.065, 4.),
            (1.20, (0., -.34, m["hip"] + .20), (8., 0., 0.), 3., 0., 0.),
            (1.50, (0., -.34, m["hip"] + .20), (8., 0., 0.), 3., 0., 0.),
        ]
    # Interpolate the COMMON object frame, then construct both hands. Blending two
    # wrist targets separately would let the hands drift through the rock.
    d = _keys([(v[0], tuple(v[1:])) for v in descriptors], t)
    return _rock_pose(m, fp, *d)


def _handle(p, side, at, euler=(0., 0., 0.), pole=None):
    _hand(p, side, at, pole, _turn(HANDLE_ALONG, euler),
          _turn(HANDLE_NORMAL, euler), "handle")


def _hatchet(t, m, fp):
    if fp:
        rows = [
            (0., (-.18, -.43, 1.39), (5., -5., -7.), 0., 0., 0.),
            (.18, (-.20, -.39, 1.40), (-4., -8., -10.), 0., 0., 0.),
            (.39, (-.23, -.29, 1.53), (-33., -13., -18.), 0., 0., 0.),
            (.47, (-.225, -.30, 1.52), (-30., -12., -16.), 0., 0., 0.),
            (.60, (-.055, -.49, 1.39), (59., 6., 14.), 0., 0., 0.),
            (.69, (-.015, -.47, 1.33), (83., 9., 19.), 0., 0., 0.),
            (.86, (-.075, -.46, 1.35), (45., 0., 5.), 0., 0., 0.),
            (1.16, (-.18, -.43, 1.39), (5., -5., -7.), 0., 0., 0.),
            (1.40, (-.18, -.43, 1.39), (5., -5., -7.), 0., 0., 0.),
        ]
    else:
        ready = (-.227678, -.304101, m["hip"] + .21879)
        rows = [
            (0., ready, (0., 0., 0.), 3., 0., -3.),
            (.18, (-.27, -.23, m["hip"] + .20), (-12., -6., -4.), 0., -.025, -9.),
            (.39, (-.32, .015, m["shoulder"] + .10), (-58., -12., -14.), -5., -.005, -22.),
            (.47, (-.31, .005, m["shoulder"] + .09), (-53., -11., -12.), -3., -.005, -19.),
            (.60, (-.10, -.49, m["hip"] + .21), (58., 4., 12.), 15., -.06, 19.),
            (.69, (-.035, -.46, m["hip"] + .035), (85., 8., 18.), 20., -.08, 24.),
            (.86, (-.13, -.38, m["hip"] + .09), (39., 2., 8.), 11., -.04, 10.),
            (1.16, ready, (0., 0., 0.), 3., 0., -3.),
            (1.40, ready, (0., 0., 0.), 3., 0., -3.),
        ]
    at, rotation, lean, drop, twist = _keys([(v[0], tuple(v[1:])) for v in rows], t)
    p = _base(m, fp)
    _action_body(p, m, drop, lean, twist, .025 * twist / 24., fp)
    if fp:
        pole = (-.42, .07, m["shoulder"] - .20)
    else:
        at, pole = _hatchet_arm(p, m, t)
    _handle(p, "r", at, rotation, pole)
    if fp:
        _hand(p, "l", (.175, -.45, 1.375), (.42, -.04, m["shoulder"] - .27),
              (0., -1., -.08), (-.8, 0., -.6), "ready")
    else:
        balance = _clamp((twist + 22.) / 46.)
        _hand(p, "l", (.26 + .04 * balance, -.17 - .07 * balance, m["hip"] + .13),
              _low_elbow(p, m, "l", back=.025, down=.25),
              (0., -.55, -.84), (-1., 0., 0.), "ready")
    return p


def _torch(t, m, fp):
    if fp:
        rows = [(0., (-.245, -.22, 1.04), (12., -12., -15.)),
                (.18, (-.245, -.25, 1.08), (15., -13., -13.)),
                (.58, (-.205, -.39, 1.43), (-9., -6., -9.)),
                (.80, (-.20, -.43, 1.405), (4., -5., -7.)),
                (1.10, (-.205, -.43, 1.39), (0., -5., -8.)),
                (1.60, (-.205, -.43, 1.39), (0., -5., -8.))]
    else:
        rows = [(0., (-.255, .015, m["hip"] + .015), (17., -12., -15.)),
                (.18, (-.27, -.02, m["hip"] + .020), (22., -14., -17.)),
                (.58, (-.28, -.30, m["shoulder"] -.15), (-8., -8., -10.)),
                (.80, (-.27, -.34, m["shoulder"] -.18), (4., -5., -8.)),
                (1.10, (-.27, -.32, m["shoulder"] -.20), (0., -5., -9.)),
                (1.60, (-.27, -.32, m["shoulder"] -.20), (0., -5., -9.))]
    at, rotation = _keys([(v[0], tuple(v[1:])) for v in rows], t)
    p = _base(m, fp)
    if not fp:
        settling = math.sin(math.pi * _clamp(t / 1.1))
        p["body"] = (0., .7 * settling, -2.5 * settling)
        p["rot"]["spine_03"] = (0., 0., -2. * settling)
        p["rot"]["head"] = (-2. * settling, -3. * settling, 3. * settling)
    _handle(p, "r", at, rotation,
            (-.48, .03, m["hip"] + .20) if fp else _low_elbow(p, m, "r", back=.04, down=.25))
    return p


def sample(name, t, landmarks, first_person=False):
    """Sample one of the eight clips, in seconds, as an independent full pose.

    FPS retains leg targets for contract completeness; export the arm bones and
    their ancestor transforms for the matching viewmodel action. Loop endpoints
    are exact. Non-looping actions clamp to their deliberate ready/hold pose.
    """
    if name not in CLIP_BY_NAME:
        raise KeyError("Unknown character trial clip: " + str(name))
    if not math.isfinite(t):
        raise ValueError("Animation time must be finite")
    meta = CLIP_BY_NAME[name]
    t = t % meta["duration"] if meta["loop"] else _clamp(t, 0., meta["duration"])
    m = _measure(landmarks)
    if name == "idle":
        pose = _idle(t, m, first_person)
    elif name in ("walk", "sprint", "crouch_walk"):
        pose = _gait(name, t, m, first_person)
    elif name == "jump_land":
        pose = _jump(t, m, first_person)
    elif name == "rock_strike":
        pose = _rock(t, m, first_person)
    elif name == "hatchet_chop":
        pose = _hatchet(t, m, first_person)
    else:
        pose = _torch(t, m, first_person)
    if first_person:
        pose["root"] = (0., 0., 0.)
        pose["body"] = (0., 0., 0.)
        pose["rot"] = {bone: (0., 0., 0.) for bone in pose["rot"]}
    s = m["scale"]
    pose["root"] = _mul(pose["root"], s)
    for target in pose["ik"].values():
        target["at"] = _mul(target["at"], s)
        target["pole"] = _mul(target["pole"], s)
        target["along"] = _unit(target["along"])
        normal = target["normal"]
        target["normal"] = _unit(_add(normal, _mul(target["along"], -_dot(normal, target["along"]))))
    return pose


def grip_frame(pose, scale=1., prop="hatchet", side="r"):
    """World semantic prop frame: origin and x/y/z unit axes.

    This is the anatomical frame, not the exporter's fitted rigid attachment.
    A handled prop's generic origin is the grasped section and +Z is the thumb
    side (along cross normal). A rock's modelling
    origin is its centre. Use ``side='l'`` to verify its second palm contact.
    For a skinned solve, transform the along/normal through the actual hand
    matrix first and apply these exact offsets there instead.
    """
    hand = pose["ik"]["hand_" + side]
    along, normal = _unit(hand["along"]), _unit(hand["normal"])
    radius = GRIP["rock_radius"] if prop == "rock" else 0.
    origin = _add(hand["at"], _add(_mul(along, GRIP["along"] * scale),
                                  _mul(normal, (GRIP["normal"] + radius) * scale)))
    if prop == "rock":
        x = normal if side == "r" else _mul(normal, -1.)
        z = along
        y = _unit(_cross(z, x))
    else:
        x, y = along, normal
        z = _unit(_cross(x, y))
    return {"origin": origin, "x": x, "y": y, "z": z}
