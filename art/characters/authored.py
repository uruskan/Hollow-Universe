"""Clips authored by script, for what the Quaternius libraries don't have (GROUND.md §4.8): lying asleep, the wounded
state (falling down, lying, crawling, getting up) and helping someone up.

A clip is a list of key poses at times. A pose sets, in the armature's space (Blender: +Z up, −Y forward, +X the
character's left):
- "root": where the pelvis moves from its rest position (metres) and "body": the pelvis' turn, either Euler degrees
  about the armature's axes or a basis (where the body's left, back and up point);
- "rot": per bone, Euler degrees about the armature's axes applied in the bone's rest-aligned frame (its parent's turn
  carried, then this): X bends the spine forward and swings a thigh back, Y leans to the left, Z turns to the left;
- "ik": per hand or foot, where it goes ("at"), where the elbow or knee points ("pole", a point) and, if given, how it
  is turned ("along": fingers or toes, "normal": palm or sole);
- "fingers": per hand, a curl: flat, relaxed, claw, grip or fist.

A key carries everything of the key before it that it doesn't set. Between keys every value eases (smoothstep);
the IK is solved on the eased targets each frame, so hands and feet move on straight paths. "ground": the key is lowered
or raised until the skinned body's lowest point touches the ground (z = 0) — lying poses rest on the body, not on its
bones.
"""
import math

import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector

FINGERS = ("thumb", "index", "middle", "ring", "pinky")
CHAINS = (("hand_l", "upperarm_l", "lowerarm_l"), ("hand_r", "upperarm_r", "lowerarm_r"),
          ("foot_l", "thigh_l", "calf_l"), ("foot_r", "thigh_r", "calf_r"))

# Curls of the three joints (degrees): (fingers, thumb).
CURLS = {
    "flat": ((4, 4, 3), (2, 2, 2)),
    "relaxed": ((18, 24, 14), (8, 10, 6)),
    "claw": ((30, 42, 30), (12, 16, 10)),
    "grip": ((52, 66, 44), (24, 30, 20)),
    "fist": ((68, 82, 52), (30, 40, 30)),
}


def ease(t):
    return t * t * (3.0 - 2.0 * t)


class Rig:
    """The rig's rest pose and hierarchy, and the skinned body for ground contact."""

    def __init__(self, rig, body=None):
        self.obj = rig
        bones = rig.data.bones
        order = [b.name for b in bones if b.parent is None]
        queue = list(order)
        order = []
        while queue:
            n = queue.pop(0)
            order.append(n)
            queue.extend(c.name for c in bones[n].children)
        self.order = order
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.hand_frames = {s: self._hand_frame(s) for s in ("l", "r")}
        self.finger_axes = {s: self._finger_axes(s) for s in ("l", "r")}
        self.skin = self._skin(body) if body is not None else None

    def head(self, bone):
        return self.rest[bone].translation.copy()

    # ------------------------------------------------------------ hands

    def _hand_frame(self, side):
        """The hand's rest direction along the fingers and its palm's normal (armature space)."""
        wrist = self.head("hand_" + side)
        middle = self.head("middle_01_" + side)
        index = self.head("index_01_" + side)
        pinky = self.head("pinky_01_" + side)
        f = (middle - wrist).normalized()
        t = index - pinky
        t = (t - f * t.dot(f)).normalized()
        z = f.cross(t).normalized()
        palm = -z if side == "r" else z
        return f, palm, t

    def _finger_axes(self, side):
        """For each finger bone, the axis in its rest frame that bends it towards the palm."""
        f, palm, t = self.hand_frames[side]
        axes = {}
        for name in FINGERS:
            for k in (1, 2, 3):
                bone = "%s_0%d_%s" % (name, k, side)
                if bone not in self.rest:
                    continue
                g = self.rest[bone]
                child = "%s_0%d_%s" % (name, k + 1, side)
                d = (self.head(child) - g.translation).normalized() if child in self.rest else g.to_3x3().col[1].normalized()
                towards = (palm * 0.6 - t * 0.8).normalized() if name == "thumb" else palm
                axis = d.cross(towards)
                if axis.length < 1e-6:
                    continue
                axes[bone] = (g.to_3x3().inverted() @ axis.normalized()).normalized()
        return axes

    # ------------------------------------------------------------ skinning (for the ground)

    def _skin(self, body):
        me = body.data
        co = np.array([body.matrix_world @ v.co for v in me.vertices], np.float64)
        names = {g.index: g.name for g in body.vertex_groups if g.name in self.rest}
        bone_index = {n: i for i, n in enumerate(self.order)}
        weights = np.zeros((len(co), len(self.order)), np.float64)
        for v in me.vertices:
            for g in v.groups:
                if g.group in names:
                    weights[v.index, bone_index[names[g.group]]] += g.weight
        total = weights.sum(axis=1)
        weights[total > 0] /= total[total > 0][:, None]
        keep = total > 0
        return co[keep], weights[keep]

    def lowest(self, globals_):
        """The lowest point (z) of the skinned body for a pose's global bone matrices."""
        co, weights = self.skin
        mats = np.array([np.array(globals_[n] @ self.rest[n].inverted()) for n in self.order])   # bones × 4 × 4
        homo = np.concatenate([co, np.ones((len(co), 1))], axis=1)
        moved = np.einsum('vb,bij,vj->vi', weights, mats, homo)
        return float(moved[:, 2].min())

    # ------------------------------------------------------------ evaluation

    def evaluate(self, pose):
        """Global bone matrices (armature space) of a pose."""
        g = {}
        rot = pose.get("rot", {})
        ik = pose.get("ik", {})
        root = Vector(pose.get("root", (0.0, 0.0, 0.0)))
        for n in self.order:
            rest = self.rest[n]
            p = self.parent[n]
            if p is None:
                g[n] = rest.copy()
                continue
            delta = g[p].to_3x3() @ self.rest[p].to_3x3().inverted()
            head = g[p] @ (self.rest[p].inverted() @ rest.translation)
            r = Matrix.Identity(3)
            if n == "pelvis":
                head = rest.translation + root
                body = pose.get("body")
                if body is not None:
                    r = basis(body)
            elif n in rot:
                r = Euler([math.radians(a) for a in rot[n]], 'XYZ').to_matrix()
            m = delta @ r @ rest.to_3x3()
            g[n] = Matrix.Translation(head) @ m.to_4x4()
            # Two-bone IK: solved when the chain's upper bone is reached; the lower bone follows in its turn.
            for end, upper, lower in CHAINS:
                if n == upper and end in ik:
                    self._two_bone(g, upper, lower, end, ik[end])
                if n == lower and ("_ik_" + lower) in g:
                    g[n] = g.pop("_ik_" + lower)
            if n in ("hand_l", "hand_r", "foot_l", "foot_r") and n in ik:
                self._orient_end(g, n, ik[n])
        self._fingers(g, pose.get("fingers", {}))
        return {k: v for k, v in g.items() if not k.startswith("_")}

    def _two_bone(self, g, upper, lower, end, spec):
        a = g[upper].translation.copy()
        rest_u, rest_l, rest_e = self.rest[upper], self.rest[lower], self.rest[end]
        l1 = (rest_l.translation - rest_u.translation).length
        l2 = (rest_e.translation - rest_l.translation).length
        target = Vector(spec["at"])
        pole = Vector(spec["pole"])
        to = target - a
        dist = min(max(to.length, abs(l1 - l2) + 1e-4), l1 + l2 - 1e-4)
        d = to.normalized()
        bend = pole - a
        bend = bend - d * bend.dot(d)
        if bend.length < 1e-6:
            bend = d.orthogonal()
        bend.normalize()
        cos_a = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
        elbow = a + d * (l1 * cos_a) + bend * (l1 * math.sqrt(max(0.0, 1 - cos_a * cos_a)))
        wrist = a + d * dist
        # The upper bone: its own turn (from its parent) and the arc that points it at the elbow.
        cur_u = g[upper].to_3x3()
        cur_dir = (g[upper] @ (rest_u.inverted() @ rest_l.translation) - a).normalized()
        arc = cur_dir.rotation_difference((elbow - a).normalized()).to_matrix()
        new_u = arc @ cur_u
        g[upper] = Matrix.Translation(a) @ new_u.to_4x4()
        # The lower bone, from the upper's new turn, pointed at the wrist.
        delta = new_u @ rest_u.to_3x3().inverted()
        lower_m = delta @ rest_l.to_3x3()
        lower_dir = (delta @ (rest_e.translation - rest_l.translation)).normalized()
        arc2 = lower_dir.rotation_difference((wrist - elbow).normalized()).to_matrix()
        g["_ik_" + lower] = Matrix.Translation(elbow) @ (arc2 @ lower_m).to_4x4()

    def _orient_end(self, g, n, spec):
        """A hand or foot turned so its fingers (toes) point `along` and its palm (sole) faces `normal`."""
        if "along" not in spec or "normal" not in spec:
            return
        side = n[-1]
        if n.startswith("hand"):
            f0, n0, _ = self.hand_frames[side]
        else:
            ankle = self.head("foot_" + side)
            ball = self.head("ball_" + side)
            f0 = (ball - ankle)
            f0 = Vector((f0.x, f0.y, 0.0)).normalized()
            n0 = Vector((0.0, 0.0, -1.0))
        f1 = Vector(spec["along"]).normalized()
        n1 = Vector(spec["normal"])
        n1 = (n1 - f1 * n1.dot(f1)).normalized()
        src = Matrix((f0, n0, f0.cross(n0))).transposed()
        dst = Matrix((f1, n1, f1.cross(n1))).transposed()
        turn = dst @ src.inverted()
        head = g[n].translation.copy()
        g[n] = Matrix.Translation(head) @ (turn @ self.rest[n].to_3x3()).to_4x4()

    def _fingers(self, g, spec):
        for side in ("l", "r"):
            curl = spec.get(side)
            if curl is None:
                continue
            fingers, thumb = CURLS[curl] if isinstance(curl, str) else curl
            for name in FINGERS:
                for k in (1, 2, 3):
                    bone = "%s_0%d_%s" % (name, k, side)
                    if bone not in g:
                        continue
                    angle = (thumb if name == "thumb" else fingers)[k - 1]
                    axis = self.finger_axes[side].get(bone)
                    if axis is None:
                        continue
                    parent = self.parent[bone]
                    delta = g[parent].to_3x3() @ self.rest[parent].to_3x3().inverted()
                    head = g[parent] @ (self.rest[parent].inverted() @ self.rest[bone].translation)
                    m = delta @ self.rest[bone].to_3x3() @ Quaternion(axis, math.radians(angle)).to_matrix()
                    g[bone] = Matrix.Translation(head) @ m.to_4x4()

    def local(self, g):
        """Each bone's local rotation and location (Blender's matrix_basis) from global matrices."""
        out = {}
        for n in self.order:
            p = self.parent[n]
            rest_rel = (self.rest[p].inverted() @ self.rest[n]) if p else self.rest[n]
            parent = g[p] if p else Matrix.Identity(4)
            basis_m = rest_rel.inverted() @ parent.inverted() @ g[n]
            loc, q, _ = basis_m.decompose()
            out[n] = (q, loc)
        return out


def basis(body):
    """A turn from Euler degrees, or from where the body's left, back and up point."""
    if len(body) == 3 and not hasattr(body[0], "__len__"):
        return Euler([math.radians(a) for a in body], 'XYZ').to_matrix()
    left, back, up = (Vector(v).normalized() for v in body)
    return Matrix((left, back, up)).transposed()


# ---------------------------------------------------------------- clips

def merged(keys):
    """Each key with what it doesn't set carried from the keys before it."""
    out = []
    state = {"root": (0.0, 0.0, 0.0), "body": None, "rot": {}, "ik": {}, "fingers": {}}
    for t, pose in keys:
        s = {"root": pose.get("root", state["root"]), "body": pose.get("body", state["body"]),
             "rot": dict(state["rot"]), "ik": dict(state["ik"]), "fingers": dict(state["fingers"]),
             "ground": pose.get("ground", False), "lift": pose.get("lift", 0.0)}
        s["rot"].update(pose.get("rot", {}))
        for k, v in pose.get("ik", {}).items():
            s["ik"][k] = None if v is None else dict(v)
        s["ik"] = {k: v for k, v in s["ik"].items() if v is not None}
        s["fingers"].update(pose.get("fingers", {}))
        out.append((t, s))
        state = s
    return out


def grounded(rig, keys):
    """Keys marked "ground" are moved up or down until the skinned body touches z = lift (default 0)."""
    out = []
    for t, s in keys:
        if s.get("ground") and rig.skin is not None:
            low = rig.lowest(rig.evaluate(s))
            r = Vector(s["root"])
            r.z += s.get("lift", 0.0) - low
            s = dict(s)
            s["root"] = tuple(r)
        out.append((t, s))
    return out


def blend(a, b, u):
    """A pose between two keys (u from 0 to 1, already eased)."""
    s = {"root": tuple(Vector(a["root"]).lerp(Vector(b["root"]), u)), "rot": {}, "ik": {}, "fingers": {}}
    ba, bb = a.get("body"), b.get("body")
    if ba is not None or bb is not None:
        qa = basis(ba).to_quaternion() if ba is not None else Quaternion()
        qb = basis(bb).to_quaternion() if bb is not None else Quaternion()
        m = qa.slerp(qb, u).to_matrix()
        s["body"] = (tuple(m.col[0]), tuple(m.col[1]), tuple(m.col[2]))
    for bone in set(a["rot"]) | set(b["rot"]):
        ea = Euler([math.radians(x) for x in a["rot"].get(bone, (0, 0, 0))], 'XYZ').to_quaternion()
        eb = Euler([math.radians(x) for x in b["rot"].get(bone, (0, 0, 0))], 'XYZ').to_quaternion()
        e = ea.slerp(eb, u).to_euler('XYZ')
        s["rot"][bone] = tuple(math.degrees(x) for x in e)
    for end in set(a["ik"]) | set(b["ik"]):
        ia, ib = a["ik"].get(end), b["ik"].get(end)
        if ia is None or ib is None:
            if u >= 0.5 and ib is not None:
                s["ik"][end] = ib
            elif ia is not None and u < 0.5:
                s["ik"][end] = ia
            continue
        spec = {"at": tuple(Vector(ia["at"]).lerp(Vector(ib["at"]), u)),
                "pole": tuple(Vector(ia["pole"]).lerp(Vector(ib["pole"]), u))}
        if "along" in ia and "along" in ib:
            spec["along"] = tuple(Vector(ia["along"]).lerp(Vector(ib["along"]), u))
            spec["normal"] = tuple(Vector(ia["normal"]).lerp(Vector(ib["normal"]), u))
        elif "along" in ib and u >= 0.5:
            spec["along"], spec["normal"] = ib["along"], ib["normal"]
        elif "along" in ia and u < 0.5:
            spec["along"], spec["normal"] = ia["along"], ia["normal"]
        s["ik"][end] = spec
    for side in set(a["fingers"]) | set(b["fingers"]):
        ca = a["fingers"].get(side, "relaxed")
        cb = b["fingers"].get(side, "relaxed")
        fa, ta = CURLS[ca] if isinstance(ca, str) else ca
        fb, tb = CURLS[cb] if isinstance(cb, str) else cb
        s["fingers"][side] = (tuple(x + (y - x) * u for x, y in zip(fa, fb)), tuple(x + (y - x) * u for x, y in zip(ta, tb)))
    return s


def sample(keys, t, loop_length=None):
    """The pose at time t between the merged keys around it."""
    if loop_length is not None:
        t = t % loop_length
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
            return blend(a, b, ease(u))
    return keys[-1][1] if t > keys[-1][0] else keys[0][1]


def bake(rig, keys, fps, length, loop=False):
    """Per-frame local rotations and locations of every bone ({bone: {"rot": [...], "loc": [...]}}), and the frames."""
    keys = grounded(rig, merged(keys))
    if loop:
        # A loop's last key is its first again.
        keys = keys + [(length, keys[0][1])] if keys[-1][0] < length else keys
    frames = int(round(length * fps))
    out = {n: {"rot": [], "loc": []} for n in rig.order}
    for f in range(frames + 1):
        pose = sample(keys, f / fps)
        local = rig.local(rig.evaluate(pose))
        for n, (q, loc) in local.items():
            out[n]["rot"].append(q)
            out[n]["loc"].append(loc)
    return out, list(range(frames + 1))
