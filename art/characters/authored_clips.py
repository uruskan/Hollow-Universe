"""Key poses of the clips authored by script (authored.py has the conventions). Positions are the male body's, in
metres; build_animations.py scales them by the body's hip height, so the female body's are hers.

The lying clips match the server's lying hit shapes (Hitboxes.cs): head about 0.78 m ahead of the character's
position, the hips over it, the feet about 0.95 m behind.
"""

# Palm and finger directions used below (armature space: +X left, −Y forward, +Z up).
DOWN, UP, FORWARD, BACK = (0, 0, -1), (0, 0, 1), (0, -1, 0), (0, 1, 0)
LEFT, RIGHT = (1, 0, 0), (-1, 0, 0)


def stand():
    """Standing at ease, as the idle loop stands: hands hanging by the thighs, feet under the hips."""
    return {
        "root": (0.0, 0.0, 0.0), "body": (0.0, 0.0, 0.0),
        "rot": {"spine_01": (0, 0, 0), "spine_02": (0, 0, 0), "spine_03": (0, 0, 0), "neck_01": (0, 0, 0),
                "head": (0, 0, 0), "clavicle_l": (0, 0, 0), "clavicle_r": (0, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.25, -0.03, 0.87), "pole": (0.45, 0.35, 1.2), "along": (0.08, -0.1, -1.0), "normal": RIGHT},
            "hand_r": {"at": (-0.25, -0.03, 0.87), "pole": (-0.45, 0.35, 1.2), "along": (-0.08, -0.1, -1.0), "normal": LEFT},
            "foot_l": {"at": (0.13, -0.01, 0.073), "pole": (0.16, -0.6, 0.5), "along": FORWARD, "normal": DOWN},
            "foot_r": {"at": (-0.13, -0.01, 0.073), "pole": (-0.16, -0.6, 0.5), "along": FORWARD, "normal": DOWN},
        },
        "fingers": {"l": "relaxed", "r": "relaxed"},
    }


def prone(chest=1.0):
    """Lying face down along the facing direction, propped on the forearms, head up and looking ahead; the left knee
    drawn up a little. `chest` scales how far the chest is lifted."""
    return {
        "root": (0.0, 0.06, -0.82), "body": (88.0, 0.0, 0.0),
        "rot": {"spine_01": (-3 * chest, 0, 0), "spine_02": (-7 * chest, 0, 0), "spine_03": (-9 * chest, 0, 0),
                "neck_01": (-32, 0, 0), "head": (-28, 0, 0), "clavicle_l": (0, 0, 0), "clavicle_r": (0, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.11, -0.64, 0.03), "pole": (0.3, -0.36, -0.6), "along": (-0.3, -1.0, 0.0), "normal": DOWN},
            "hand_r": {"at": (-0.11, -0.64, 0.03), "pole": (-0.3, -0.36, -0.6), "along": (0.3, -1.0, 0.0), "normal": DOWN},
            "foot_l": {"at": (0.3, 0.7, 0.07), "pole": (0.75, 0.4, 0.1), "along": (0.3, 0.3, -1.0), "normal": BACK},
            "foot_r": {"at": (-0.15, 0.93, 0.08), "pole": (-0.2, 0.5, -0.6), "along": (0.0, 0.2, -1.0), "normal": BACK},
        },
        "fingers": {"l": "claw", "r": "claw"},
        "ground": True,
    }


def mirror_pose(pose):
    """The pose mirrored left to right (for a crawl's second half)."""
    def mx(v):
        return (-v[0], v[1], v[2])
    out = dict(pose)
    out["root"] = mx(pose["root"])
    rot = {}
    for bone, (x, y, z) in pose.get("rot", {}).items():
        other = bone[:-2] + ("_r" if bone.endswith("_l") else "_l") if bone[-2:] in ("_l", "_r") else bone
        rot[other] = (x, -y, -z)
    out["rot"] = rot
    ik = {}
    for end, spec in pose.get("ik", {}).items():
        other = end[:-2] + ("_r" if end.endswith("_l") else "_l")
        s = {"at": mx(spec["at"]), "pole": mx(spec["pole"])}
        if "along" in spec:
            s["along"], s["normal"] = mx(spec["along"]), mx(spec["normal"])
        ik[other] = s
    out["ik"] = ik
    out["fingers"] = {("r" if k == "l" else "l"): v for k, v in pose.get("fingers", {}).items()}
    return out


def crawl_key(reach_r, pull_l, knee_l, lift_l=0.0):
    """One moment of the crawl: the right forearm reaching `reach_r` metres ahead, the left one pulled back to
    `pull_l` (lifted by `lift_l` while it travels), the left knee drawn up by `knee_l` (0–1)."""
    p = prone(chest=0.6)
    p["root"] = (0.0, 0.06, -0.82)
    p["rot"] = dict(p["rot"])
    p["rot"]["spine_02"] = (-6, 4 * (knee_l - 0.5), -3 * (knee_l - 0.5))
    p["rot"]["spine_03"] = (-7, 4 * (knee_l - 0.5), -4 * (knee_l - 0.5))
    p["ik"] = {
        "hand_r": {"at": (-0.11, -0.42 - reach_r, 0.03), "pole": (-0.55, -0.45, -0.3), "along": (0.25, -1.0, 0.0), "normal": DOWN},
        "hand_l": {"at": (0.15, -0.42 - pull_l, 0.03 + lift_l), "pole": (0.55, -0.1, -0.2), "along": (-0.25, -1.0, 0.0), "normal": DOWN},
        "foot_l": {"at": (0.18 + 0.2 * knee_l, 0.93 - 0.32 * knee_l, 0.08), "pole": (0.35 + 0.45 * knee_l, 0.45, -0.3 + 0.35 * knee_l),
                   "along": (0.2 * knee_l, 0.25, -1.0), "normal": BACK},
        "foot_r": {"at": (-0.16, 0.94, 0.08), "pole": (-0.25, 0.5, -0.6), "along": (0.0, 0.2, -1.0), "normal": BACK},
    }
    return p


def crawl():
    """Elbow crawl, 1 s a cycle at 0.8 m/s: each forearm reaches 0.4 m ahead, plants and pulls back while the body
    slides over it (0.4 m in 0.5 s, so the planted forearm keeps still on the ground), the opposite knee drawing up and
    pushing."""
    a = crawl_key(0.4, 0.0, 1.0)
    b = crawl_key(0.2, 0.2, 0.5, lift_l=0.07)
    return [(0.0, a), (0.25, b), (0.5, mirror_pose(a)), (0.75, mirror_pose(b))]


def down_idle():
    """Lying wounded: propped on the forearms, breathing hard, the head sinking and lifting."""
    a = prone()
    b = prone(chest=0.85)
    b["rot"] = dict(b["rot"])
    b["rot"]["head"] = (-20, 4, 0)
    b["rot"]["neck_01"] = (-26, 0, 2)
    return [(0.0, a), (1.4, b)]


def down_fall():
    """Knocked down: the knees give, the hands go to the body, down onto the knees, forward onto the hands, flat."""
    s = stand()
    buckle = stand()
    buckle["root"] = (0.0, 0.03, -0.14)
    buckle["rot"] = dict(buckle["rot"], spine_02=(12, 0, 3), spine_03=(14, 0, 2), neck_01=(8, 0, 0), head=(10, 0, 0))
    buckle["ik"] = dict(buckle["ik"])
    buckle["ik"]["hand_l"] = {"at": (0.06, -0.2, 0.98), "pole": (0.45, 0.1, 0.9), "along": (-0.6, -0.2, -0.6), "normal": BACK}
    buckle["ik"]["hand_r"] = {"at": (-0.04, -0.22, 1.02), "pole": (-0.45, 0.1, 0.9), "along": (0.6, -0.2, -0.6), "normal": BACK}
    buckle["ik"]["foot_l"] = {"at": (0.13, -0.01, 0.073), "pole": (0.2, -0.8, 0.45), "along": FORWARD, "normal": DOWN}
    buckle["ik"]["foot_r"] = {"at": (-0.13, -0.01, 0.073), "pole": (-0.2, -0.8, 0.45), "along": FORWARD, "normal": DOWN}
    kneel = {
        "root": (0.0, 0.02, -0.47), "body": (14.0, 0.0, 0.0),
        "rot": {"spine_02": (12, 0, 0), "spine_03": (10, 0, 0), "neck_01": (6, 0, 0), "head": (8, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.2, -0.38, 0.5), "pole": (0.5, 0.1, 0.7), "along": (0.0, -0.4, -1.0), "normal": (-0.3, 0.3, -0.2)},
            "hand_r": {"at": (-0.2, -0.38, 0.5), "pole": (-0.5, 0.1, 0.7), "along": (0.0, -0.4, -1.0), "normal": (0.3, 0.3, -0.2)},
            "foot_l": {"at": (0.14, 0.42, 0.08), "pole": (0.16, -0.4, 0.05), "along": (0.0, 0.3, -1.0), "normal": BACK},
            "foot_r": {"at": (-0.14, 0.42, 0.08), "pole": (-0.16, -0.4, 0.05), "along": (0.0, 0.3, -1.0), "normal": BACK},
        },
        "fingers": {"l": "relaxed", "r": "relaxed"},
    }
    hands = {
        "root": (0.0, 0.08, -0.57), "body": (52.0, 0.0, 0.0),
        "rot": {"spine_02": (0, 0, 0), "spine_03": (-4, 0, 0), "neck_01": (-12, 0, 0), "head": (-10, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.21, -0.6, 0.02), "pole": (0.6, -0.1, 0.4), "along": (-0.1, -1.0, 0.0), "normal": DOWN},
            "hand_r": {"at": (-0.21, -0.6, 0.02), "pole": (-0.6, -0.1, 0.4), "along": (0.1, -1.0, 0.0), "normal": DOWN},
            "foot_l": {"at": (0.15, 0.55, 0.08), "pole": (0.2, -0.2, -0.3), "along": (0.0, 0.3, -1.0), "normal": BACK},
            "foot_r": {"at": (-0.15, 0.55, 0.08), "pole": (-0.2, -0.2, -0.3), "along": (0.0, 0.3, -1.0), "normal": BACK},
        },
        "fingers": {"l": "flat", "r": "flat"},
    }
    return [(0.0, s), (0.22, buckle), (0.5, kneel), (0.78, hands), (1.15, prone())]


def get_up():
    """Getting up: pushing up on the hands, onto hands and knees, up on one knee with a hand reaching ahead for a
    helper's (or the knee), then standing."""
    push = {
        "root": (0.0, 0.08, -0.74), "body": (70.0, 0.0, 0.0),
        "rot": {"spine_01": (-4, 0, 0), "spine_02": (-8, 0, 0), "spine_03": (-8, 0, 0), "neck_01": (-24, 0, 0), "head": (-18, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.22, -0.5, 0.02), "pole": (0.6, 0.0, 0.3), "along": (-0.1, -1.0, 0.0), "normal": DOWN},
            "hand_r": {"at": (-0.22, -0.5, 0.02), "pole": (-0.6, 0.0, 0.3), "along": (0.1, -1.0, 0.0), "normal": DOWN},
            "foot_l": {"at": (0.15, 0.9, 0.08), "pole": (0.2, 0.5, -0.6), "along": (0.0, 0.2, -1.0), "normal": BACK},
            "foot_r": {"at": (-0.15, 0.9, 0.08), "pole": (-0.2, 0.5, -0.6), "along": (0.0, 0.2, -1.0), "normal": BACK},
        },
        "fingers": {"l": "flat", "r": "flat"},
        "ground": True,
    }
    fours = {
        "root": (0.0, 0.055, -0.45), "body": (84.0, 0.0, 0.0),
        "rot": {"spine_01": (2, 0, 0), "spine_02": (4, 0, 0), "spine_03": (2, 0, 0), "neck_01": (-14, 0, 0), "head": (-12, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.2, -0.45, 0.02), "pole": (0.5, 0.1, 0.4), "along": (-0.1, -1.0, 0.0), "normal": DOWN},
            "hand_r": {"at": (-0.2, -0.45, 0.02), "pole": (-0.5, 0.1, 0.4), "along": (0.1, -1.0, 0.0), "normal": DOWN},
            "foot_l": {"at": (0.14, 0.48, 0.08), "pole": (0.2, -0.2, -0.3), "along": (0.0, 0.3, -1.0), "normal": BACK},
            "foot_r": {"at": (-0.14, 0.48, 0.08), "pole": (-0.2, -0.2, -0.3), "along": (0.0, 0.3, -1.0), "normal": BACK},
        },
        "fingers": {"l": "flat", "r": "flat"},
    }
    knee = {
        "root": (0.0, 0.1, -0.42), "body": (8.0, 0.0, 0.0),
        "rot": {"spine_01": (0, 0, 0), "spine_02": (4, 0, 0), "spine_03": (2, 0, 0), "neck_01": (-6, 0, 0), "head": (-6, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.2, -0.32, 0.58), "pole": (0.55, 0.1, 0.8), "along": (0.0, -0.5, -1.0), "normal": (-0.2, 0.2, -1.0)},
            "hand_r": {"at": (-0.18, -0.58, 1.08), "pole": (-0.55, 0.0, 0.8), "along": (0.1, -1.0, 0.3), "normal": UP},
            "foot_l": {"at": (0.15, -0.36, 0.073), "pole": (0.2, -1.0, 0.6), "along": FORWARD, "normal": DOWN},
            "foot_r": {"at": (-0.14, 0.55, 0.08), "pole": (-0.2, -0.3, 0.0), "along": (0.0, 0.3, -1.0), "normal": BACK},
        },
        "fingers": {"l": "relaxed", "r": "grip"},
    }
    rise = {
        "root": (0.0, 0.0, -0.12), "body": (14.0, 0.0, 0.0),
        "rot": {"spine_01": (0, 0, 0), "spine_02": (4, 0, 0), "spine_03": (2, 0, 0), "neck_01": (-6, 0, 0), "head": (-4, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.24, -0.12, 0.85), "pole": (0.5, 0.3, 1.1), "along": (0.08, -0.2, -1.0), "normal": RIGHT},
            "hand_r": {"at": (-0.2, -0.38, 1.1), "pole": (-0.5, 0.2, 1.0), "along": (0.1, -1.0, 0.0), "normal": UP},
            "foot_l": {"at": (0.14, -0.2, 0.073), "pole": (0.2, -1.0, 0.6), "along": FORWARD, "normal": DOWN},
            "foot_r": {"at": (-0.13, 0.12, 0.09), "pole": (-0.2, -1.0, 0.6), "along": FORWARD, "normal": DOWN},
        },
        "fingers": {"l": "relaxed", "r": "grip"},
    }
    return [(0.0, prone()), (0.35, push), (0.72, fours), (1.08, knee), (1.42, rise), (1.75, stand())]


def help_up():
    """Helping someone up: a step and down on one knee, the right hand down to theirs, a grip, and up again pulling
    back."""
    s = stand()
    kneel = {
        "root": (0.0, 0.08, -0.43), "body": (24.0, 0.0, 0.0),
        "rot": {"spine_01": (0, 0, 0), "spine_02": (8, 0, 0), "spine_03": (8, 0, 0), "neck_01": (6, 0, 0), "head": (8, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.2, -0.42, 0.56), "pole": (0.55, 0.1, 0.8), "along": (0.0, -0.5, -1.0), "normal": (-0.2, 0.2, -1.0)},
            "hand_r": {"at": (-0.1, -0.82, 0.28), "pole": (-0.55, -0.2, 0.6), "along": (0.0, -1.0, -0.4), "normal": DOWN},
            "foot_l": {"at": (0.15, -0.45, 0.073), "pole": (0.2, -1.2, 0.6), "along": FORWARD, "normal": DOWN},
            "foot_r": {"at": (-0.14, 0.5, 0.08), "pole": (-0.2, -0.4, 0.0), "along": (0.0, 0.3, -1.0), "normal": BACK},
        },
        "fingers": {"l": "relaxed", "r": "relaxed"},
    }
    grip = dict(kneel, fingers={"l": "relaxed", "r": "grip"})
    pull = {
        "root": (0.0, 0.05, -0.2), "body": (-4.0, 0.0, 0.0),
        "rot": {"spine_01": (-2, 0, 0), "spine_02": (-2, 0, 4), "spine_03": (-2, 0, 4), "neck_01": (0, 0, 0), "head": (6, 0, 0)},
        "ik": {
            "hand_l": {"at": (0.26, -0.1, 0.82), "pole": (0.5, 0.3, 1.1), "along": (0.08, -0.2, -1.0), "normal": RIGHT},
            "hand_r": {"at": (-0.2, -0.42, 0.92), "pole": (-0.55, 0.3, 1.0), "along": (0.1, -1.0, -0.3), "normal": DOWN},
            "foot_l": {"at": (0.15, -0.38, 0.073), "pole": (0.2, -1.2, 0.6), "along": FORWARD, "normal": DOWN},
            "foot_r": {"at": (-0.13, 0.1, 0.073), "pole": (-0.2, -1.0, 0.5), "along": FORWARD, "normal": DOWN},
        },
        "fingers": {"l": "relaxed", "r": "grip"},
    }
    return [(0.0, s), (0.48, kneel), (0.75, grip), (1.25, pull), (1.8, stand())]


def sleep():
    """Asleep on the left side: knees drawn up, the head resting on the left arm, the right hand on the ground in
    front, breathing slowly."""
    body = ((0.0, 0.0, -1.0), (1.0, 0.0, 0.0), (0.0, -1.0, 0.0))     # left side down, the front towards −X
    a = {
        "root": (0.0, 0.05, -0.8), "body": body,
        "rot": {"spine_01": (2, 0, 0), "spine_02": (5, 0, 0), "spine_03": (6, 0, 0), "neck_01": (6, 6, 0), "head": (6, 14, 0),
                "clavicle_l": (0, 0, 0), "clavicle_r": (0, 0, 0)},
        "ik": {
            "hand_l": {"at": (-0.12, -0.84, 0.06), "pole": (-0.25, -0.5, -0.3), "along": (0.2, -1.0, 0.0), "normal": UP},
            "hand_r": {"at": (-0.38, -0.5, 0.03), "pole": (-0.2, -0.3, 0.6), "along": (-0.4, -1.0, 0.0), "normal": DOWN},
            "foot_l": {"at": (-0.3, 0.55, 0.06), "pole": (-0.9, 0.2, 0.05), "along": (-0.4, 0.2, 0.0), "normal": (0.0, 0.0, -1.0)},
            "foot_r": {"at": (-0.42, 0.45, 0.17), "pole": (-1.0, 0.1, 0.2), "along": (-0.4, 0.2, 0.0), "normal": (0.0, 0.0, -1.0)},
        },
        "fingers": {"l": "relaxed", "r": "relaxed"},
        "ground": True,
    }
    b = dict(a)
    b["rot"] = dict(a["rot"], spine_02=(6.5, 0, 0), spine_03=(7.5, 0, 0))
    return [(0.0, a), (2.0, b)]


# name: (length in seconds, loops, keys function, ground speed of a loop)
CLIPS = {
    "sleep": (4.0, True, sleep, 0.0),
    "down_idle": (2.8, True, down_idle, 0.0),
    "crawl": (1.0, True, crawl, 0.8),
    "down_fall": (1.15, False, down_fall, 0.0),
    "get_up": (1.75, False, get_up, 0.0),
    "help_up": (1.8, False, help_up, 0.0),
}
