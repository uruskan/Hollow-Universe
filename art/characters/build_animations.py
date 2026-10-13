"""Retargets Quaternius' Universal Animation Library clips onto the player skeleton.

    blender -b --factory-startup --python art/characters/build_animations.py -- --ual1 <glb> --ual2 <glb>

Defaults for --ual1 and --ual2 are the downloaded Standard packs under D:/GameDev/Assets/Downloads (THIRD_PARTY.md).
Needs art/.cache/human_male.blend from build_body.py.

Writes game/assets/characters/animations.glb (the skeleton and one animation per clip) and animations.json (loop flag,
length, ground speed of locomotion loops, source clip).

Method: both skeletons have the same 53 bones (MPFB's game-engine rig and UAL's UE-style rig), but different rest
poses (MakeHuman's A-pose, UAL's T-pose) and proportions. The target is first posed into the source's rest pose by
swinging each bone, root to tips, onto the source bone's direction (a pure swing at each joint, so no false twist),
and each forearm is turned so the palm faces as it does in the source. That gives a constant offset per bone between
the two skeletons' frames. Every frame of a clip then copies each source bone's world rotation times that offset;
the pelvis moves by the source pelvis' motion scaled by the ratio of hip heights. Rotations only: bone lengths stay
the target's.
"""
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "lib"))

import bpy  # noqa: E402
from mathutils import Matrix, Quaternion, Vector  # noqa: E402

import hollow_art as H  # noqa: E402

DOWNLOADS = "D:/GameDev/Assets/Downloads"
UAL1 = DOWNLOADS + "/quaternius-ual1/Animation Library[Standard]/Godot/AnimationLibrary_Godot_Standard.glb"
UAL2 = DOWNLOADS + "/quaternius-ual2/Universal Animation Library 2 [Standard]/Unreal-Godot/UAL2_Standard.glb"

# Our clip name: (library, source clip, loops). Names are ours; the client refers to these.
CLIPS = {
    "idle": ("ual1", "Idle_Loop", True),
    "walk": ("ual1", "Walk_Loop", True),
    "jog": ("ual1", "Jog_Fwd_Loop", True),
    "sprint": ("ual1", "Sprint_Loop", True),
    "crouch_idle": ("ual1", "Crouch_Idle_Loop", True),
    "crouch_walk": ("ual1", "Crouch_Fwd_Loop", True),
    "jump_start": ("ual1", "Jump_Start", False),
    "fall": ("ual1", "Jump_Loop", True),
    "jump_land": ("ual1", "Jump_Land", False),
    "swim_idle": ("ual1", "Swim_Idle_Loop", True),
    "swim": ("ual1", "Swim_Fwd_Loop", True),
    "pistol_idle": ("ual1", "Pistol_Idle_Loop", True),
    "pistol_aim": ("ual1", "Pistol_Aim_Neutral", True),
    "pistol_aim_up": ("ual1", "Pistol_Aim_Up", True),
    "pistol_aim_down": ("ual1", "Pistol_Aim_Down", True),
    "pistol_fire": ("ual1", "Pistol_Shoot", False),
    "pistol_reload": ("ual1", "Pistol_Reload", False),
    "tool_idle": ("ual1", "Sword_Idle", True),
    "tool_swing": ("ual1", "Sword_Attack", False),
    "chop": ("ual2", "TreeChopping_Loop", True),
    "torch_idle": ("ual1", "Idle_Torch_Loop", True),
    "spell_idle": ("ual1", "Spell_Simple_Idle_Loop", True),
    "punch_jab": ("ual1", "Punch_Jab", False),
    "punch_cross": ("ual1", "Punch_Cross", False),
    "melee_hook": ("ual2", "Melee_Hook", False),
    "throw": ("ual2", "OverhandThrow", False),
    "hit_chest": ("ual1", "Hit_Chest", False),
    "hit_head": ("ual1", "Hit_Head", False),
    "death": ("ual1", "Death01", False),
    "interact": ("ual1", "Interact", False),
    "pick_up": ("ual1", "PickUp_Table", False),
    "consume": ("ual2", "Consume", False),
    "roll": ("ual1", "Roll", False),
    "carry_walk": ("ual2", "Walk_Carry_Loop", True),
    "fold_arms": ("ual2", "Idle_FoldArms_Loop", True),
    "talk": ("ual1", "Idle_Talking_Loop", True),
    "sit_idle": ("ual1", "Sitting_Idle_Loop", True),
}
LOCOMOTION = ("walk", "jog", "sprint", "crouch_walk", "carry_walk")
# Loops re-timed so the planted foot sweeps back at the speed the client plays them at (CharacterAnimator's blend
# points): the stance phases are stretched or squeezed and the rest of the cycle takes the time left. Without it a
# hand-keyed run's foot sweeps back twice as fast as the body moves during its short contact, and slides.
PLANTED_SPEED = {"jog": 2.8, "sprint": 5.5, "crouch_walk": 1.3}

# UAL 1 uses Rigify deform names; UAL 2 uses UE-style names like ours.
UAL1_NAMES = {
    "Root": "root", "pelvis": "DEF-hips", "spine_01": "DEF-spine.001", "spine_02": "DEF-spine.002",
    "spine_03": "DEF-spine.003", "neck_01": "DEF-neck", "head": "DEF-head",
}
for side, s in (("l", "L"), ("r", "R")):
    UAL1_NAMES.update({
        "clavicle_" + side: "DEF-shoulder." + s, "upperarm_" + side: "DEF-upper_arm." + s,
        "lowerarm_" + side: "DEF-forearm." + s, "hand_" + side: "DEF-hand." + s,
        "thigh_" + side: "DEF-thigh." + s, "calf_" + side: "DEF-shin." + s, "foot_" + side: "DEF-foot." + s,
        "ball_" + side: "DEF-toe." + s,
    })
    for finger, theirs in (("thumb", "thumb"), ("index", "f_index"), ("middle", "f_middle"), ("ring", "f_ring"),
                           ("pinky", "f_pinky")):
        for k in (1, 2, 3):
            UAL1_NAMES["%s_0%d_%s" % (finger, k, side)] = "DEF-%s.0%d.%s" % (theirs, k, s)
UAL2_NAMES = {"Root": "root", "head": "Head"}
ARM_CHAIN = ("clavicle_", "upperarm_", "lowerarm_", "hand_", "thumb_", "index_", "middle_", "ring_", "pinky_")


def main():
    a = H.args()
    out = os.path.abspath(a.get("out", H.CHARACTERS))
    cache = os.path.join(H.CACHE, "human_male.blend")
    bpy.ops.wm.open_mainfile(filepath=cache)
    scene = bpy.context.scene
    scene.render.fps = 30
    rig = bpy.data.objects["Rig"]
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and not obj.name.endswith("lod0"):
            bpy.data.objects.remove(obj)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    sources = {"ual1": import_library(a.get("ual1", UAL1)), "ual2": import_library(a.get("ual2", UAL2))}
    names = {"ual1": UAL1_NAMES, "ual2": UAL2_NAMES}

    rig.rotation_mode = 'QUATERNION'
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    offsets = {}
    meta = {}
    made = []
    for clip, (library, source_name, loops) in CLIPS.items():
        source_rig, actions = sources[library]
        if source_name not in actions:
            H.log("missing clip", library, source_name)
            continue
        key = (library,)
        if key not in offsets:
            offsets[key] = rest_offsets(rig, source_rig, names[library])
        action, info = retarget(rig, source_rig, actions[source_name], offsets[key], names[library], clip, loops)
        info["source"] = "%s/%s" % ("UAL1" if library == "ual1" else "UAL2", source_name)
        meta[clip] = info
        made.append(action)

    # Only our rig and its actions go into the file.
    for library in sources.values():
        remove_hierarchy(library[0])
    for action in list(bpy.data.actions):
        if action not in made:
            bpy.data.actions.remove(action)
    rig.animation_data_clear()
    rig.animation_data_create()
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for m in meshes:
        bpy.data.objects.remove(m)
    H.export_glb(os.path.join(out, "animations.glb"), [rig], animations=True)
    H.write_json(os.path.join(out, "animations.json"), {
        "source": "Quaternius Universal Animation Library 1 and 2, Standard (CC0), retargeted by "
                  "art/characters/build_animations.py",
        "fps": scene.render.fps,
        "clips": meta,
    })
    if a.get("sheet"):
        contact_sheet(a["sheet"], os.path.join(out, "animations.glb"), list(meta.keys()), meta)


def import_library(path):
    before = set(bpy.data.objects)
    before_actions = set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    armature = next(o for o in new if o.type == 'ARMATURE')
    for o in new:
        if o.type == 'MESH':
            bpy.data.objects.remove(o)
    actions = {act.name: act for act in bpy.data.actions if act not in before_actions}
    # Blender's importer may suffix names (Idle_Loop_Rig); strip anything after the clip name we know.
    cleaned = {}
    for name, act in actions.items():
        base = name
        for suffix in ("_" + armature.name, "_Armature", "_Rig"):
            if base.endswith(suffix):
                base = base[: -len(suffix)]
        cleaned[base] = act
    if armature.animation_data:
        armature.animation_data.action = None
    H.log("imported", os.path.basename(path), len(cleaned), "clips")
    return armature, cleaned


def remove_hierarchy(obj):
    for child in list(obj.children):
        remove_hierarchy(child)
    bpy.data.objects.remove(obj)


def world_rest(rig, bone_name):
    return rig.matrix_world @ rig.data.bones[bone_name].matrix_local


def source_name(names, ours):
    return names.get(ours, ours)


def rest_offsets(rig, source_rig, names):
    """Per bone, the constant rotation O with  target_world = source_world @ O  for the same pose."""
    bones = rig.data.bones
    order = [b.name for b in bones if b.parent is None]
    queue = list(order)
    order = []
    while queue:
        n = queue.pop(0)
        order.append(n)
        queue.extend(c.name for c in bones[n].children)
    # Current (aligned) world rotation and head/tail of every target bone, starting from its rest.
    rot = {n: world_rest(rig, n).to_3x3().normalized() for n in order}
    head = {n: rig.matrix_world @ bones[n].head_local for n in order}
    tail = {n: rig.matrix_world @ bones[n].tail_local for n in order}

    def subtree(n):
        result, stack = [], [n]
        while stack:
            m = stack.pop()
            result.append(m)
            stack.extend(c.name for c in bones[m].children)
        return result

    def rotate_subtree(n, q, pivot):
        m3 = q.to_matrix()
        for m in subtree(n):
            rot[m] = m3 @ rot[m]
            head[m] = pivot + q @ (head[m] - pivot)
            tail[m] = pivot + q @ (tail[m] - pivot)

    src_bones = source_rig.data.bones
    for n in order:
        s = source_name(names, n)
        if s not in src_bones or n == "Root" or not n.startswith(ARM_CHAIN):
            # Legs, spine, neck and head stand upright in both rest poses: rest corresponds to rest. Only the arms
            # differ (MakeHuman's A-pose against UAL's T-pose) and are swung onto the source's directions.
            continue
        sm = world_rest(source_rig, s)
        want = (source_rig.matrix_world @ src_bones[s].tail_local - source_rig.matrix_world @ src_bones[s].head_local).normalized()
        have = (tail[n] - head[n]).normalized()
        q = have.rotation_difference(want)
        rotate_subtree(n, q, head[n].copy())
        if n.startswith("lowerarm_"):
            # Turn the forearm about its own axis so the palm faces as in the source rest pose.
            side = n[-1]
            palm_t = palm_normal(head, "hand_" + side, "index_01_" + side, "pinky_01_" + side)
            sp = {b: source_rig.matrix_world @ src_bones[source_name(names, b)].head_local
                  for b in ("hand_" + side, "index_01_" + side, "pinky_01_" + side)}
            palm_s = palm_normal(sp, "hand_" + side, "index_01_" + side, "pinky_01_" + side)
            axis = (tail[n] - head[n]).normalized()
            a_t = (palm_t - axis * palm_t.dot(axis)).normalized()
            a_s = (palm_s - axis * palm_s.dot(axis)).normalized()
            angle = math.atan2(axis.dot(a_t.cross(a_s)), a_t.dot(a_s))
            rotate_subtree(n, Quaternion(axis, angle), head[n].copy())
    offsets = {}
    for n in order:
        s = source_name(names, n)
        if s not in src_bones:
            continue
        rs = world_rest(source_rig, s).to_3x3().normalized()
        offsets[n] = rs.inverted() @ rot[n]
    return offsets


def palm_normal(points, hand, index, pinky):
    a = points[index] - points[hand]
    b = points[pinky] - points[hand]
    return a.cross(b).normalized()


def hip_height(rig, names=None):
    l = source_name(names or {}, "thigh_l")
    r = source_name(names or {}, "thigh_r")
    return ((rig.matrix_world @ rig.data.bones[l].head_local).z + (rig.matrix_world @ rig.data.bones[r].head_local).z) / 2


def retarget(rig, source_rig, source_action, offsets, names, clip, loops):
    scene = bpy.context.scene
    source_rig.animation_data_create()
    source_rig.animation_data.action = source_action
    if hasattr(source_rig.animation_data, "action_slot") and source_action.slots:
        source_rig.animation_data.action_slot = source_action.slots[0]
    first, last = (int(round(x)) for x in source_action.frame_range)
    bones = rig.data.bones
    order = []
    queue = [b.name for b in bones if b.parent is None]
    while queue:
        n = queue.pop(0)
        order.append(n)
        queue.extend(c.name for c in bones[n].children)
    k = hip_height(rig) / hip_height(source_rig, names)
    pelvis_src = source_name(names, "pelvis")
    rest_pelvis_s = (source_rig.matrix_world @ source_rig.data.bones[pelvis_src].head_local)
    rest_pelvis_t = rig.matrix_world @ bones["pelvis"].head_local
    tw_inv = rig.matrix_world.inverted()

    frames = list(range(first, last + 1))
    keys = {n: {"rot": [], "loc": []} for n in order}
    foot_track = {"l": [], "r": []}
    for f in frames:
        scene.frame_set(f)
        pose = {}
        for n in order:
            b = bones[n]
            s = source_name(names, n)
            parent_pose = pose[b.parent.name] if b.parent else Matrix.Identity(4)
            rest_rel = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
            fk = parent_pose @ rest_rel
            if n in offsets and s in source_rig.pose.bones and n != "Root":
                gs = (source_rig.matrix_world @ source_rig.pose.bones[s].matrix).to_3x3().normalized()
                gt = (tw_inv.to_3x3() @ (gs @ offsets[n])).normalized()
                position = fk.translation
                if n == "pelvis":
                    ps = source_rig.matrix_world @ source_rig.pose.bones[s].matrix.translation
                    position = tw_inv @ (rest_pelvis_t + (ps - rest_pelvis_s) * k)
                m = gt.to_4x4()
                m.translation = position
            else:
                m = fk
            pose[n] = m
            basis = rest_rel.inverted() @ parent_pose.inverted() @ m
            loc, q, _ = basis.decompose()
            keys[n]["rot"].append(q)
            keys[n]["loc"].append(loc)
        for side in ("l", "r"):
            foot_track[side].append(rig.matrix_world @ pose["ball_" + side].translation)

    warped = None
    if clip in PLANTED_SPEED:
        keys, warped = warp_to_speed(keys, foot_track, scene.render.fps, PLANTED_SPEED[clip])
    action = bpy.data.actions.new(clip)
    action.use_fake_user = True
    write_keys(rig, action, frames, keys)
    info = {"loop": loops, "frames": len(frames), "length": round((last - first) / scene.render.fps, 4)}
    if warped is not None:
        info["ground_speed"] = PLANTED_SPEED[clip]
        info["stance"] = warped
    elif clip in LOCOMOTION:
        info["ground_speed"] = round(ground_speed(foot_track, scene.render.fps), 3)
    H.log("clip %-14s %3d frames %s" % (clip, len(frames), info.get("ground_speed", "")))
    return action, info


def write_keys(rig, action, frames, keys):
    from bpy_extras import anim_utils
    slot = action.slots.new(id_type='OBJECT', name=rig.name)
    channelbag = anim_utils.action_ensure_channelbag_for_slot(action, slot)
    for n, k in keys.items():
        # Make the quaternions continuous, so interpolation never takes the long way round.
        qs = k["rot"]
        for i in range(1, len(qs)):
            if qs[i].dot(qs[i - 1]) < 0:
                qs[i] = -qs[i]
        path = 'pose.bones["%s"].rotation_quaternion' % n
        for axis in range(4):
            fc = channelbag.fcurves.new(path, index=axis, group_name=n)
            fc.keyframe_points.add(len(frames))
            co = []
            for f, q in zip(frames, qs):
                co += [f - frames[0], q[axis]]
            fc.keyframe_points.foreach_set("co", co)
            fc.update()
        if n in ("pelvis",):
            path = 'pose.bones["%s"].location' % n
            for axis in range(3):
                fc = channelbag.fcurves.new(path, index=axis, group_name=n)
                fc.keyframe_points.add(len(frames))
                co = []
                for f, l in zip(frames, k["loc"]):
                    co += [f - frames[0], l[axis]]
                fc.keyframe_points.foreach_set("co", co)
                fc.update()
    for fc in channelbag.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'


def warp_to_speed(keys, track, fps, speed, contact=0.07):
    """Re-times a loop (keys per frame, the last frame closing the cycle) so that whenever a foot is planted (both
    ends of a frame interval within `contact` metres of its lowest) it sweeps back at `speed`; the other intervals
    share the time left, so the cycle keeps its length. Returns the new keys (same frames) and the stance share of
    the cycle."""
    from mathutils import Quaternion
    n = len(track["l"])
    period = (n - 1) / fps
    travel = [0.0] * (n - 1)
    for side in ("l", "r"):
        z = [p.z for p in track[side]]
        y = [p.y for p in track[side]]
        low = min(z) + contact
        for i in range(n - 1):
            back = y[i + 1] - y[i]          # the character faces −Y: a planted foot moves to +Y
            if max(z[i], z[i + 1]) < low and back > 0:
                travel[i] = max(travel[i], back)
    stance = [t / speed for t in travel]
    stance_time = sum(stance)
    if stance_time > 0.85 * period:
        stance = [t * 0.85 * period / stance_time for t in stance]
        stance_time = 0.85 * period
    others = [i for i in range(n - 1) if travel[i] == 0.0]
    share = (period - stance_time) / max(1, len(others))
    durations = [stance[i] if travel[i] > 0 else share for i in range(n - 1)]
    # Source time (in frames) at each output frame.
    starts = [0.0]
    for d in durations:
        starts.append(starts[-1] + d)
    warped = {name: {"rot": [], "loc": []} for name in keys}
    for k in range(n):
        t = min(k / fps, period)
        i = 0
        while i < n - 2 and starts[i + 1] <= t:
            i += 1
        u = 0.0 if durations[i] <= 0 else min(1.0, max(0.0, (t - starts[i]) / durations[i]))
        for name, k_ in keys.items():
            q0, q1 = Quaternion(k_["rot"][i]), Quaternion(k_["rot"][i + 1])
            if q0.dot(q1) < 0:
                q1 = -q1
            warped[name]["rot"].append(q0.slerp(q1, u))
            warped[name]["loc"].append(k_["loc"][i].lerp(k_["loc"][i + 1], u))
    return warped, round(stance_time / period, 3)


def ground_speed(track, fps):
    """In-place loops: speed from stride and cadence. Each foot travels its fore-aft range once per step (half a
    cycle); the 0.93 calibrates it against the walk, whose planted foot can be measured directly. Hand-keyed runs slide
    their feet during the short contact, so the contact speed alone would read too fast."""
    estimates = []
    for side in ("l", "r"):
        ys = [p.y for p in track[side]]
        half_cycle = (len(ys) - 1) / fps / 2
        estimates.append(0.93 * (max(ys) - min(ys)) / half_cycle)
    return sum(estimates) / len(estimates)


def contact_sheet(path, glb, clips, meta, tile=(150, 230)):
    """A grid of one pose per clip on the exported body (Workbench), for checking the retarget by eye. Both files are
    imported the way any client would read them, so this also checks the round trip through glTF."""
    import numpy as np
    H.fresh_scene()
    folder = os.path.dirname(glb)
    bpy.ops.import_scene.gltf(filepath=os.path.join(folder, "human_male.glb"))
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o.name not in ("body_lod0", "hair_lod0"):
            bpy.data.objects.remove(o)
    before = set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=glb)
    imported_rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o != rig)
    actions = {}
    for act in bpy.data.actions:
        if act not in before:
            actions[act.name.split("_" + imported_rig.name)[0]] = act
    remove_hierarchy(imported_rig)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'SINGLE'
    scene.display.shading.single_color = (0.75, 0.72, 0.68)
    scene.render.resolution_x, scene.render.resolution_y = tile
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 50
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (-2.6, -4.6, 1.3)
    cam.rotation_euler = (Vector((0, 0, 0.9)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    rig.animation_data_create()
    images = []
    order = []
    for clip in clips:
        act = actions.get(clip)
        if act is None:
            continue
        rig.animation_data.action = act
        if act.slots:
            rig.animation_data.action_slot = act.slots[0]
        first, last = act.frame_range
        scene.frame_set(int(first + (last - first) * (0.4 if meta[clip]["loop"] else 0.5)))
        scene.render.filepath = path + ".tile.png"
        bpy.ops.render.render(write_still=True)
        px, w, h = H.load_image_pixels(scene.render.filepath)
        images.append(px)
        order.append(clip)
    cols = 10
    rows = (len(images) + cols - 1) // cols
    th, tw = images[0].shape[:2]
    sheet = np.ones((rows * th, cols * tw, 4), np.float32)
    for i, im in enumerate(images):
        r, c = divmod(i, cols)
        sheet[(rows - 1 - r) * th:(rows - r) * th, c * tw:(c + 1) * tw] = im
    H.save_image(sheet.ravel(), cols * tw, rows * th, path)
    os.remove(path + ".tile.png")
    for i, clip in enumerate(order):
        r, c = divmod(i, cols)
        print("[hollow] sheet row %d col %d: %s" % (r, c, clip))


if __name__ == "__main__":
    main()
