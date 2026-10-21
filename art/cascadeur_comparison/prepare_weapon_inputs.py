"""Isolated FPS weapon rigs from immutable v3 and our owned item GLBs.

Run with background Blender only. Nothing is written outside weapon-inputs.
The constant ready pose is an interchange input, not an authored attack clip.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / ".artifacts/character-trial-cascadeur-v3-comparison/weapon-inputs"


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    item = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(item)
    return item


P = module("weapon_input_helpers", Path(__file__).with_name("prepare_inputs.py"))
A = module("weapon_authored_helpers", ROOT / "art/characters/authored.py")
P.OUT = OUT
P.H.OUT = OUT
SOURCE = P.SOURCE
PROFILE = json.loads((ROOT / "art/character_trial/grip_profile.json").read_text())
C = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))
ASSETS = {"heavyaxe": "salvaged-axe", "pickaxe": "metal-pickaxe", "pistol": "pistol", "rifle": "rifle"}
PARTS = {"pistol": {"prop_magazine": "Magazine", "prop_slide": "Slide"},
         "rifle": {"prop_magazine": "Magazine", "prop_bolt": "Bolt"}}


def rows(matrix):
    return [list(row) for row in matrix]


def transform(position, rotation):
    result = rotation.to_4x4()
    result.translation = position
    return result


def unit(vector):
    return Vector(vector).normalized()


def elbow(shoulder, wrist, pole, lengths):
    l1, l2 = lengths
    delta = wrist - shoulder
    distance = min(max(delta.length, abs(l1-l2)+1e-5), l1+l2-1e-5)
    direction = delta.normalized()
    perpendicular = pole - shoulder
    perpendicular -= direction * perpendicular.dot(direction)
    if perpendicular.length < 1e-6:
        perpendicular = direction.orthogonal()
    perpendicular.normalize()
    a = (l1*l1 + distance*distance - l2*l2)/(2*distance)
    return shoulder + direction*a + perpendicular*math.sqrt(max(0, l1*l1-a*a))


def comfortable_elbow(shoulder, wrist, along, lengths, side):
    """Keep low outward arms, allowing modest bend instead of chasing zero."""
    pole = Vector((-.34 if side == "r" else .34, -.09, 1.24))
    low = elbow(shoulder, wrist, pole, lengths)
    d = unit(wrist-shoulder)
    center = shoulder+d*(low-shoulder).dot(d)
    radial = low-center
    best = None
    for degree in range(-180, 181, 10):
        candidate = center+Quaternion(d, math.radians(degree)) @ radial
        bend = math.degrees(unit(wrist-candidate).angle(along))
        cost = 20*max(0, bend-18)**2 + .01*degree*degree
        cost += 200000*max(0, candidate.z-1.46)**2
        cost += 10000*max(0, (candidate.x+.07) if side == "r" else (.07-candidate.x))**2
        if best is None or cost < best[0]:
            best = (cost, candidate)
    return best[1]


def arm_data(model, rig, side):
    names = ["upperarm_"+side, "lowerarm_"+side, "hand_"+side]
    points = [rig.matrix_world @ model.rest[name].translation for name in names]
    return points[0], ((points[1]-points[0]).length, (points[2]-points[1]).length)


def hand_direction(model, side, matrix):
    delta = matrix.to_3x3() @ model.rest["hand_"+side].to_3x3().inverted()
    return (delta @ model.hand_frames[side][0]).normalized()


def right_attachment(model):
    attachment = Matrix(PROFILE["hand_to_hatchet"])
    height = model.obj.data.bones["head"].tail_local.z + .035
    attachment.translation *= height / 1.8 / PROFILE["scale"]
    # Original trial tool local +Z shaft, -Y blade. Imported GLB axes:
    # +X blade/barrel, +Y shaft/up, +Z side. Preserve calibrated hand fit.
    glb_to_trial = Matrix(((0, 0, -1), (-1, 0, 0), (0, 1, 0))).to_4x4()
    return attachment @ Matrix.Rotation(math.radians(77.08), 4, "Z") @ glb_to_trial


def mirrored_grip_hand(model, weapon, right_hand, socket):
    local = weapon.inverted() @ right_hand
    weapon_mirror = Matrix.Diagonal((1, 1, -1))
    body_mirror = Matrix.Diagonal((-1, 1, 1))
    rotation = weapon.to_3x3() @ weapon_mirror @ local.to_3x3() @ model.rest["hand_r"].to_3x3().inverted() @ body_mirror @ model.rest["hand_l"].to_3x3()
    position = weapon @ (weapon_mirror @ local.translation + Vector(socket))
    return transform(position, rotation)


def choose_weapon_frame(model, rig, kind, sockets):
    """Search a small compositional range; choose a feasible natural right wrist."""
    shoulder, lengths = arm_data(model, rig, "r")
    attachment = right_attachment(model)
    base = Matrix(((0, 0, -1), (-1, 0, 0), (0, 1, 0)))
    best = None
    # These are FPS staging bounds in metres, not changes to human bone lengths.
    center = {"heavyaxe": (-.02, -.51, 1.31), "pickaxe": (-.02, -.51, 1.31),
              "pistol": (-.10, -.38, 1.43), "rifle": (-.13, -.24, 1.42)}[kind]
    for yaw in (-15, -5, 5, 15, 25):
        for pitch in ((-8, 0, 8) if kind in ("heavyaxe", "pickaxe") else (-12, 0, 12)):
            rotation = Matrix.Rotation(math.radians(yaw), 3, "Z") @ base @ Matrix.Rotation(math.radians(pitch), 3, "Z")
            for dx in (-.025, 0, .025):
                for dy in (-.03, 0, .03):
                    for dz in (-.025, 0, .025):
                        position = Vector((center[0]+dx, center[1]+dy, center[2]+dz))
                        weapon = transform(position, rotation)
                        hand = weapon @ attachment.inverted()
                        wrist = hand.translation
                        along = hand_direction(model, "r", hand)
                        # Closest feasible elbow to a straight anatomical wrist.
                        actual = comfortable_elbow(shoulder, wrist, along, lengths, "r") if kind in ("heavyaxe", "pickaxe") else elbow(shoulder, wrist, wrist-along*lengths[1], lengths)
                        forearm = unit(wrist-actual)
                        bend = math.degrees(forearm.angle(along))
                        reach = (wrist-shoulder).length/sum(lengths)
                        # Keep elbow below the shoulder and on the right side.
                        score = (10*max(0, bend-20)**2 if kind in ("heavyaxe", "pickaxe") else bend*bend) + 1200*max(0, reach-.94)**2
                        score += 8000*max(0, actual.x+.05)**2 + 8000*max(0, actual.z-1.52)**2
                        score += 2000*(dx*dx+dy*dy+dz*dz) + .007*yaw*yaw + .01*pitch*pitch
                        if kind == "rifle":
                            left_shoulder, left_lengths = arm_data(model, rig, "l")
                            left_contact = weapon @ Vector(sockets["grip_l"])
                            score += 9000*max(0, (left_contact-left_shoulder).length-sum(left_lengths)-.06)**2
                        else:
                            lh = mirrored_grip_hand(model, weapon, hand, sockets["grip_l"])
                            ls, ll = arm_data(model, rig, "l")
                            lf = hand_direction(model, "l", lh)
                            le = comfortable_elbow(ls, lh.translation, lf, ll, "l") if kind in ("heavyaxe", "pickaxe") else elbow(ls, lh.translation, lh.translation-lf*ll[1], ll)
                            lb = math.degrees(unit(lh.translation-le).angle(lf))
                            score += (10*max(0, lb-20)**2 if kind in ("heavyaxe", "pickaxe") else lb*lb) + 2000*max(0, (lh.translation-ls).length/sum(ll)-.94)**2
                            score += 200000*max(0, le.z-1.46)**2
                        candidate = (score, weapon, hand, actual, bend, reach)
                        if best is None or score < best[0]:
                            best = candidate
    _, weapon, hand, actual, bend, reach = best
    return weapon, hand, actual, {"right_wrist_bend_deg": bend, "right_reach_fraction": reach}


def left_contact_pose(model, rig, contact, normal, kind, palm_offset=.057):
    shoulder, lengths = arm_data(model, rig, "l")
    pole = Vector((.34, -.10, 1.33))
    along = unit(contact-shoulder)
    normal = unit(normal)
    wrist = contact - along*palm_offset - normal*.023
    for _ in range(15):
        e = elbow(shoulder, wrist, pole, lengths)
        along = unit(wrist-e)
        n = normal-along*normal.dot(along)
        if n.length < 1e-5:
            n = along.orthogonal()
        n.normalize()
        wrist = contact-along*palm_offset-n*.023
    e = elbow(shoulder, wrist, pole, lengths)
    return {"at": tuple(rig.matrix_world.inverted() @ wrist),
            "pole": tuple(rig.matrix_world.inverted() @ e),
            "along": tuple(along), "normal": tuple(n)}, {
                "left_reach_fraction": (wrist-shoulder).length/sum(lengths),
                "left_wrist_bend_deg": math.degrees(unit(wrist-e).angle(along)),
                "left_contact_m": list(contact)}


def set_finger_quaternions(model, g, overrides):
    """Apply local finger rotations and rebuild each child's translated frame."""
    for name in model.order:
        if not any(name.startswith(f+"_") for f in A.FINGERS):
            continue
        parent = model.parent[name]
        local_rest = model.rest[parent].inverted() @ model.rest[name]
        if name in overrides:
            g[name] = g[parent] @ local_rest @ Quaternion(overrides[name]).to_matrix().to_4x4()


def rotate_finger_chain(g, names, axis, degrees):
    center = g[names[0]].translation
    around = Matrix.Translation(center) @ Quaternion(axis, math.radians(degrees)).to_matrix().to_4x4() @ Matrix.Translation(-center)
    for name in names:
        g[name] = around @ g[name]


def pose_weapon(model, rig, kind, sockets, parts):
    weapon, right_hand, right_elbow, metrics = choose_weapon_frame(model, rig, kind, sockets)
    delta = right_hand.to_3x3() @ model.rest["hand_r"].to_3x3().inverted()
    right = {"at": tuple(rig.matrix_world.inverted() @ right_hand.translation),
             "pole": tuple(rig.matrix_world.inverted() @ right_elbow),
             "along": tuple(delta @ model.hand_frames["r"][0]),
             "normal": tuple(delta @ model.hand_frames["r"][1])}
    contact = weapon @ Vector(sockets["grip_l"])
    # Left support faces up under rifles and inward around vertical handles.
    normal = weapon.to_3x3() @ Vector((0, 1, 0) if kind == "rifle" else (0, 0, -1))
    if kind != "rifle":
        lh = mirrored_grip_hand(model, weapon, right_hand, sockets["grip_l"])
        ls, ll = arm_data(model, rig, "l")
        lf = hand_direction(model, "l", lh)
        le = comfortable_elbow(ls, lh.translation, lf, ll, "l") if kind in ("heavyaxe", "pickaxe") else elbow(ls, lh.translation, lh.translation-lf*ll[1], ll)
        ld = lh.to_3x3() @ model.rest["hand_l"].to_3x3().inverted()
        left = {"at": tuple(rig.matrix_world.inverted() @ lh.translation),
                "pole": tuple(rig.matrix_world.inverted() @ le), "along": tuple(lf),
                "normal": tuple(ld @ model.hand_frames["l"][1])}
        left_metrics = {"left_reach_fraction": (lh.translation-ls).length/sum(ll),
                        "left_wrist_bend_deg": math.degrees(unit(lh.translation-le).angle(lf)),
                        "left_contact_m": list(contact)}
    else:
        left, left_metrics = left_contact_pose(model, rig, contact, normal, kind)
    metrics.update(left_metrics)
    pose = {"ik": {"hand_r": right, "hand_l": left},
            "fingers": {"r": ((58, 72, 47), (31, 37, 26)),
                        "l": ((48, 66, 45), (24, 32, 22))}}
    g = model.evaluate(pose)
    overrides = dict(PROFILE["finger_quaternions"])
    if kind in ("pistol", "rifle"):
        # Trigger finger curls independently; three remaining fingers hold grip.
        for k, degrees in enumerate((23, 46, 35), 1):
            name = f"index_0{k}_r"
            overrides[name] = list(Quaternion(model.finger_axes["r"][name], math.radians(degrees)))
    set_finger_quaternions(model, g, overrides)
    if kind != "rifle":
        # Mirror the measured right grip's complete finger hierarchy, preserving
        # the left rest axes rather than copying quaternion components blindly.
        wm = Matrix.Diagonal((1, 1, -1))
        bm = Matrix.Diagonal((-1, 1, 1))
        mirrored_source = {name: matrix.copy() for name, matrix in g.items()}
        if kind == "pistol":
            # The offhand wraps the primary hand; it does not get a second
            # trigger finger. Rebuild it from the calibrated closed grip.
            set_finger_quaternions(model, mirrored_source, PROFILE["finger_quaternions"])
        for finger in A.FINGERS:
            for k in (1, 2, 3):
                rn, ln = f"{finger}_0{k}_r", f"{finger}_0{k}_l"
                rr = (weapon.inverted() @ rig.matrix_world @ mirrored_source[rn]).to_3x3()
                lr = weapon.to_3x3() @ wm @ rr @ model.rest[rn].to_3x3().inverted() @ bm @ model.rest[ln].to_3x3()
                parent = model.parent[ln]
                position = g[parent] @ (model.rest[parent].inverted() @ model.rest[ln].translation)
                g[ln] = transform(position, lr)
    if kind == "pistol":
        # The v3 cylindrical handle profile buried this fingertip inside the
        # pistol's rectangular lower grip. Open its middle/distal arc 40deg.
        rotate_finger_chain(g, ["pinky_02_r", "pinky_03_r"], weapon.to_3x3() @ Vector((0, 1, 0)), -40)
    elif kind == "heavyaxe":
        # The flared pommel is wider than the upper handle; fit the bottom
        # pinky independently instead of copying a constant cylinder curl.
        rotate_finger_chain(g, ["pinky_01_r", "pinky_02_r", "pinky_03_r"], weapon.to_3x3() @ Vector((0, 1, 0)), -25)
        rotate_finger_chain(g, ["pinky_02_r", "pinky_03_r"], weapon.to_3x3() @ Vector((0, 1, 0)), -35)
    elif kind == "rifle":
        # Keep the thumb on the outside of the upper gas-tube wood.
        rotate_finger_chain(g, ["thumb_02_l", "thumb_03_l"], weapon.to_3x3() @ Vector((1, 0, 0)), -45)
    g["prop_hatchet"] = rig.matrix_world.inverted() @ weapon
    for name, part in parts.items():
        g[name] = g["prop_hatchet"] @ Matrix.Translation(part["pivot_weapon_m"])
    grasps = {}
    for grasp_name, part_bone in (("magazine", "prop_magazine"), ("rack", "prop_slide" if kind == "pistol" else "prop_bolt")):
        if part_bone not in parts:
            continue
        contact = weapon @ Vector(parts[part_bone]["contact_weapon_m"])
        normal = weapon.to_3x3() @ Vector((0, 0, -1 if part_bone == "prop_bolt" else 1))
        spec, quality = left_contact_pose(model, rig, contact, normal, kind, palm_offset=.052)
        if part_bone == "prop_bolt":
            # Reach over the receiver and hook the external charging handle.
            # A neutral forearm-only fit passes straight through the receiver.
            wrist = weapon @ (Vector(parts[part_bone]["contact_weapon_m"])+Vector((0, .050, -.075)))
            hint = Vector((.30, -.27, 1.67))
            spec = {"at": tuple(rig.matrix_world.inverted() @ wrist),
                    "pole": tuple(rig.matrix_world.inverted() @ hint),
                    "along": tuple(weapon.to_3x3() @ Vector((0, 0, 1))),
                    "normal": tuple(weapon.to_3x3() @ Vector((0, -1, 0)))}
            quality = {"method": "External above-receiver hook; native reload staging must preserve clearance"}
        mag_pose = {"ik": {"hand_r": right, "hand_l": spec},
                    "fingers": {"l": ((48, 66, 45), (24, 32, 22))}}
        mg = model.evaluate(mag_pose)
        matrix = rig.matrix_world @ mg["hand_l"]
        grasps[grasp_name] = {"hand_position_m": list(matrix.translation), "hand_rotation": rows(matrix.to_3x3()),
                 "finger_local_rotations": {n: rows(mg[model.parent[n]].to_3x3().transposed() @ mg[n].to_3x3()) for n in mg if n.endswith("_l")
                                            and any(n.startswith(f+"_") for f in A.FINGERS)},
                 "contact_weapon_m": parts[part_bone]["contact_weapon_m"],
                 "method": "Anatomical neutral-wrist contact fit; visual animation review remains required",
                 "quality": quality, "part_bone": part_bone}
        if part_bone == "prop_bolt":
            grasps[grasp_name]["elbow_hint_m"] = list(hint)
    return g, weapon, metrics, grasps


def restore_material(material):
    name = material.name.split(".")[0]
    texture_root = ROOT / "game/assets/textures"
    mapping = {
        "M_SalvagedAxe": (ROOT / "game/assets/items/salvaged-axe", "albedo.png", "orm.png", "normal.png", False),
        "M_PSG_Surface": (texture_root/"psg", "T_PSG_Surface_BaseColor.png", "T_PSG_Surface_ORM.png", "T_PSG_Surface_NormalGL.png", True),
        "M_PSG_Markings": (texture_root/"psg", "T_PSG_Markings_BaseColor.png", None, None, True),
        "M_FrontierAK_Metal": (texture_root/"frontier", "T_FrontierAK_Metal_BaseColor.png", "T_FrontierAK_Metal_ORM.png", "T_FrontierAK_Metal_NormalGL.png", False),
        "M_FrontierAK_Furniture": (texture_root/"frontier", "T_FrontierAK_Furniture_BaseColor.png", "T_FrontierAK_Furniture_ORM.png", "T_FrontierAK_Furniture_NormalGL.png", False),
    }
    if name not in mapping:
        raise ValueError(f"No owned material mapping for {name}")
    directory, albedo, orm, normal, vertex_color = mapping[name]
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = .69
    def texture(filename, noncolor=False):
        image = bpy.data.images.load(str(directory/filename), check_existing=True)
        if noncolor:
            image.colorspace_settings.name = "Non-Color"
        node = nodes.new("ShaderNodeTexImage")
        node.image = image
        return node
    base = texture(albedo)
    if vertex_color:
        attr = nodes.new("ShaderNodeVertexColor")
        attr.layer_name = "Color"
        multiply = nodes.new("ShaderNodeMixRGB")
        multiply.blend_type = "MULTIPLY"
        multiply.inputs[0].default_value = 1
        links.new(base.outputs["Color"], multiply.inputs[1])
        links.new(attr.outputs["Color"], multiply.inputs[2])
        links.new(multiply.outputs[0], bsdf.inputs["Base Color"])
    else:
        links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    if orm:
        channel = nodes.new("ShaderNodeSeparateColor")
        links.new(texture(orm, True).outputs["Color"], channel.inputs["Color"])
        links.new(channel.outputs["Green"], bsdf.inputs["Roughness"])
        links.new(channel.outputs["Blue"], bsdf.inputs["Metallic"])
    if normal:
        bump = nodes.new("ShaderNodeNormalMap")
        links.new(texture(normal, True).outputs["Color"], bump.inputs["Color"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    material.use_backface_culling = False


def import_weapon(rig, kind):
    source = ROOT / "game/assets/items" / (ASSETS[kind]+".glb")
    old = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(source))
    imported = set(bpy.data.objects)-old
    bpy.context.view_layer.update()
    meshes = [obj for obj in imported if obj.type == "MESH" and obj.name.split(".")[0].endswith("_lod0")]
    sockets = {obj.name.removeprefix("socket_").split(".")[0]: list(C @ obj.matrix_world.translation)
               for obj in imported if obj.name.startswith("socket_")}
    if "grip_r" not in sockets:
        raise ValueError("Missing right grip")
    origin = Vector(sockets["grip_r"])
    sockets = {name: list(Vector(value)-origin) for name, value in sockets.items()}
    if kind in ("heavyaxe", "pickaxe"):
        # Cross-section centres measured from the actual LOD0 shaft vertices at
        # y=.105m; both handles bend away from the lower grip's x=0 centre.
        sockets["grip_l"] = [-.022 if kind == "heavyaxe" else .010, .105, 0]
    elif kind == "pistol":
        sockets["asset_grip_l"] = sockets["grip_l"]
        # Fit the support around the firing hand below the trigger guard.
        sockets["grip_l"] = [.020, -.015, -.028]
    parts = {}
    for bone, part_name in PARTS.get(kind, {}).items():
        parent = next(obj for obj in imported if obj.name == part_name)
        pivot = C @ parent.matrix_world.translation-origin
        objects = [obj for obj in meshes if obj.name.startswith(part_name+"_")]
        points = [C @ (obj.matrix_world @ vertex.co)-origin for obj in objects for vertex in obj.data.vertices]
        lo = Vector([min(point[i] for point in points) for i in range(3)])
        hi = Vector([max(point[i] for point in points) for i in range(3)])
        contact = (lo+hi)*.5
        contact.z = lo.z
        if "magazine" in bone:
            contact.y = lo.y+(hi.y-lo.y)*.23
        elif bone == "prop_bolt":
            outer = [point for point in points if point.z > hi.z-.003]
            contact = sum(outer, Vector())/len(outer)  # actual outer lever surface
        elif bone == "prop_slide":
            contact.x = lo.x+.025  # rear slide serrations
        parts[bone] = {"parent": "prop_hatchet", "pivot_weapon_m": list(pivot),
                       "contact_weapon_m": list(contact), "bounds_weapon_m": {"min": list(lo), "max": list(hi)},
                       "kind": bone.removeprefix("prop_"),
                       "movement_axis_weapon": [0, -1, 0] if "magazine" in bone else [-1, 0, 0]}
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    prop = rig.data.edit_bones["prop_hatchet"]
    prop.head, prop.tail, prop.roll = (0, 0, 0), (0, .1, 0), 0
    for name, part in parts.items():
        bone = rig.data.edit_bones.new(name)
        bone.head = part["pivot_weapon_m"]
        bone.tail = Vector(bone.head)+Vector((0, .05, 0))
        bone.parent = prop
        bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    restored = set()
    for obj in meshes:
        world = obj.matrix_world.copy()
        for vertex in obj.data.vertices:
            vertex.co = C @ (world @ vertex.co)-origin
        obj.parent = rig
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_basis = Matrix.Identity(4)
        obj.vertex_groups.clear()
        stem = obj.name.split("_")[0]
        bone = next((name for name, part_name in PARTS.get(kind, {}).items() if part_name == stem), "prop_hatchet")
        group = obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
        mod = obj.modifiers.new("Rigid weapon part", "ARMATURE")
        mod.object = rig
        obj.name = kind+"_"+obj.name
        if obj.data.color_attributes:
            obj.data.color_attributes[0].name = "Color"
        for material in obj.data.materials:
            if material and material not in restored:
                restore_material(material)
                restored.add(material)
    for obj in imported-set(meshes):
        bpy.data.objects.remove(obj, do_unlink=True)
    return meshes, sockets, parts, source


def apply_pose(model, rig, g):
    rig.data.pose_position = "POSE"
    for name, (rotation, position) in model.local(g).items():
        bone = rig.pose.bones[name]
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = rotation
        bone.location = position
        bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def diagnostic_views(kind):
    """Render oblique inspection copies without changing the saved FPS camera."""
    directory = OUT/kind
    bpy.ops.wm.open_mainfile(filepath=str(directory/"Weapon-Ready.blend"))
    scene = next(s for s in bpy.data.scenes if s.name.startswith("FPS"))
    bpy.context.window.scene = scene
    camera = scene.camera
    manifest = json.loads((directory/"weapon-manifest.json").read_text())
    weapon = Vector(manifest["weapon_frame"]["position_m"])
    target = weapon+Vector((0, -.10 if kind == "rifle" else 0, .08))
    scene.render.resolution_x, scene.render.resolution_y = 896, 630
    scene.eevee.taa_render_samples = 16
    for side, x in (("left", .72), ("right", -.72)):
        camera.location = target+Vector((x, .12, .12))
        camera.rotation_mode = "QUATERNION"
        camera.rotation_quaternion = (target-camera.location).to_track_quat("-Z", "Y")
        camera.data.lens = 40
        scene.render.filepath = str(directory/f"ready-side-{side}.png")
        bpy.ops.render.render(write_still=True)
    rig = next(o for o in scene.objects if o.type == "ARMATURE")
    rig.animation_data_clear()
    model = A.Rig(rig)
    for name, grasp in manifest["grasps"].items():
        source = manifest["ready"]
        g = {n: rig.matrix_world.inverted() @ transform(Vector(source["bone_positions_m"][n]), Matrix(source["bone_rotations"][n])) for n in source["bone_positions_m"]}
        hand = transform(Vector(grasp["hand_position_m"]), Matrix(grasp["hand_rotation"]))
        delta = hand.to_3x3() @ model.rest["hand_l"].to_3x3().inverted()
        pose = {"ik": {"hand_l": {"at": tuple(rig.matrix_world.inverted() @ hand.translation),
                  "pole": tuple(rig.matrix_world.inverted() @ Vector(grasp.get("elbow_hint_m", (.34, -.10, 1.33)))),
                  "along": tuple(delta @ model.hand_frames["l"][0]),
                  "normal": tuple(delta @ model.hand_frames["l"][1])}}}
        alternate = model.evaluate(pose)
        for n in ("upperarm_l", "lowerarm_l", "hand_l"):
            g[n] = alternate[n]
        for n in model.order:
            if n in grasp["finger_local_rotations"]:
                parent = model.parent[n]
                position = g[parent] @ (model.rest[parent].inverted() @ model.rest[n].translation)
                g[n] = transform(position, g[parent].to_3x3() @ Matrix(grasp["finger_local_rotations"][n]))
        apply_pose(model, rig, g)
        camera.location = hand.translation+Vector((-.60 if kind == "rifle" and name == "rack" else .60, .10, .18))
        camera.rotation_quaternion = (hand.translation+Vector((0, -.03, .03))-camera.location).to_track_quat("-Z", "Y")
        camera.data.lens = 48
        scene.render.filepath = str(directory/f"grasp-{name}-diagnostic.png")
        bpy.ops.render.render(write_still=True)
    print("WEAPON_DIAGNOSTICS", kind, flush=True)


def refresh_contacts(kind):
    directory = OUT/kind
    bpy.ops.wm.open_mainfile(filepath=str(directory/"Weapon-Ready.blend"))
    scene = next(s for s in bpy.data.scenes if s.name.startswith("FPS"))
    bpy.context.window.scene = scene
    rig = next(o for o in scene.objects if o.type == "ARMATURE")
    manifest = json.loads((directory/"weapon-manifest.json").read_text())
    parts = manifest["parts"]
    for name, part in parts.items():
        if name == "prop_bolt":
            # Measured centroid of the ready GLB's outermost charging-handle
            # vertices (z > max_z-.003), retained in original GLB metres.
            part["contact_weapon_m"] = [.14909650, .07803948, .05256063]
        elif name == "prop_slide":
            part["contact_weapon_m"][0] = part["bounds_weapon_m"]["min"][0]+.025
    _, weapon, _, grasps = pose_weapon(A.Rig(rig), rig, kind, manifest["sockets_weapon_m"], parts)
    previous = transform(Vector(manifest["weapon_frame"]["position_m"]), Matrix(manifest["weapon_frame"]["rotation"]))
    if max(abs(weapon[i][j]-previous[i][j]) for i in range(4) for j in range(4)) > 1e-6:
        raise ValueError("Contact-only refresh would use another ready weapon frame")
    manifest["grasps"] = grasps
    manifest["contact_revision"] = "2: correct outside surface/palm normal and rear slide contact"
    P.write_json(directory/"weapon-manifest.json", manifest)
    print("WEAPON_CONTACTS_REFRESHED", kind, flush=True)


def process(kind, export_files=True):
    directory = OUT/kind
    directory.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    scene = next(s for s in bpy.data.scenes if s.name.startswith("FPS"))
    bpy.context.window.scene = scene
    rig = next(o for o in scene.objects if o.type == "ARMATURE")
    rig.animation_data_clear()
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    arms = [o for o in scene.objects if o.name in ("FPS_Survivor_Body", "FPS_Workwear")]
    if len(arms) != 2:
        raise ValueError(f"FPS source arms/clothing not found: {[o.name for o in arms]}")
    for obj in list(scene.objects):
        if obj.type == "MESH" and obj not in arms:
            obj.hide_render = True
            obj.hide_set(True)
    meshes, sockets, parts, source = import_weapon(rig, kind)
    active = arms+meshes
    model = A.Rig(rig)
    g, weapon, metrics, grasps = pose_weapon(model, rig, kind, sockets, parts)
    scene.render.fps, scene.render.fps_base = 30, 1.0
    scene.unit_settings.system, scene.unit_settings.scale_length = "METRIC", 1.0
    scene.frame_start, scene.frame_end = 1, 2
    apply_pose(model, rig, g)
    action = bpy.data.actions.new("FPS_"+kind+"_ready")
    rig.animation_data_create()
    rig.animation_data.action = action
    for frame in (1, 2):
        for bone in rig.pose.bones:
            bone.keyframe_insert("location", frame=frame, group=bone.name)
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            bone.keyframe_insert("scale", frame=frame, group=bone.name)
    scene.frame_set(1)
    bpy.context.view_layer.update()
    camera = P.camera_record(scene)
    local = model.local(g)
    manifest = {
        "schema": "hollow-cascadeur-fps-weapon-input-1", "weapon_id": kind,
        "input_revision": "2: measured curved-shaft contacts, independently curled pistol support, external rack contacts",
        "source_blend": str(SOURCE), "source_blend_sha256": P.H.digest(SOURCE),
        "source_glb": str(source), "source_glb_sha256": P.H.digest(source),
        "provenance": "Project-owned ready GLB; original project-created or owner-licensed art, no other-game files",
        "ready": {"bone_positions_m": P.bone_positions(rig), "bone_rotations": P.bone_rotations(rig)},
        "weapon_frame": {"position_m": list(weapon.translation), "rotation": rows(weapon.to_3x3()),
                         "axes": "Original GLB +X forward, +Y up, +Z side mapped to Blender world metres"},
        "sockets_weapon_m": sockets, "parts": parts, "hands": ["r", "l"],
        "grip_method": "Right calibrated v3 handle relation; left anatomical neutral-wrist contact fit",
        "virtual_sockets": ["grip_l"] if kind in ("heavyaxe", "pickaxe") else [],
        "quality": metrics, "grasps": grasps,
        "camera": camera, "cascadeur_camera": P.cascadeur_camera(camera),
        "paths": {name: str(directory/filename) for name, filename in {
            "bind": "survivor-fps-bind.fbx", "ready": "survivor-fps-ready.fbx", "qrig": "Survivor-FPS.qrigcasc",
            "skeleton": "fps-skeleton-rest.json", "preview": "ready.png", "blend": "Weapon-Ready.blend"}.items()},
        "ready_local_transforms": {name: {"position": list(loc), "rotation_wxyz": list(q)} for name, (q, loc) in local.items()},
        "hand_relative_to_weapon": {side: rows(weapon.inverted() @ (rig.matrix_world @ g["hand_"+side])) for side in ("r", "l")},
        "limitations": ["Ready input only; attack/reload authored separately in native Cascadeur.",
                        "Analytic grip fitting needs visual review in motion; numerical wrist metrics alone do not prove contact quality.",
                        "FBX standard material conversion cannot represent all Blender/Godot shader nodes; portable texture files supplied."]}
    P.write_json(directory/"weapon-manifest.json", manifest)
    # Render before export so the parent can inspect staging immediately.
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 16
    scene.eevee.use_raytracing = False
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 896, 630, 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(directory/"ready.png")
    bpy.ops.render.render(write_still=True)
    if not export_files:
        print("WEAPON_PREVIEW", kind, str(directory/"ready.png"), json.dumps(metrics), flush=True)
        return None
    # Texture paths are rewritten only inside this disposable in-memory source.
    P.H.OUT = directory
    textures = P.H.portable_textures(active)
    P.H.OUT = OUT
    for texture in textures:
        texture["path"] = kind+"/"+texture["path"]
    skeleton = {"rig": rig.name, "matrix_world": rows(rig.matrix_world),
                "bones": [{"name": b.name, "parent": b.parent.name if b.parent else None,
                           "deform": b.use_deform, "head_m": list(rig.matrix_world @ b.head_local),
                           "tail_m": list(rig.matrix_world @ b.tail_local), "matrix_local": rows(b.matrix_local)} for b in rig.data.bones]}
    P.write_json(directory/"fps-skeleton-rest.json", skeleton)
    P.write_json(directory/"Survivor-FPS.qrigcasc", P.Q.build_template(skeleton))
    P.write_json(directory/"fps-camera.json", camera)
    result = {"view": {"rig": rig.name, "full_bone_count": len(rig.data.bones), "bones": list(rig.data.bones.keys()),
                       "camera": camera, "textures": textures}, "files": []}
    for clip in ("bind", "ready"):
        rig.data.pose_position = "REST"
        bpy.context.view_layer.update()
        record = {"path": f"{kind}/survivor-fps-{clip}.fbx", "view": kind, "clip": clip,
                  "mesh_count": len(active), "meshes": [P.mesh_record(o, rig) for o in active],
                  "rest_bounds_m": P.H.bounds(active, evaluated=True), "samples": {}, "frame_range": None}
        if clip == "ready":
            rig.data.pose_position = "POSE"
            record.update(action=action.name, frame_range=[1, 2], sample_count=2, duration_seconds=1/30)
            for frame in (1, 2):
                scene.frame_set(frame)
                bpy.context.view_layer.update()
                record["samples"][str(frame)] = {"bone_positions_m": P.bone_positions(rig),
                    "bone_rotations": P.bone_rotations(rig), "bounds_m": P.H.bounds(active, evaluated=True)}
        scene.frame_set(1)
        P.select(rig, active, scene.camera)
        P.export_fbx(OUT/record["path"], clip == "ready")
        record.update(sha256=P.H.digest(OUT/record["path"]), bytes=(OUT/record["path"]).stat().st_size)
        result["files"].append(record)
    rig.data.pose_position = "POSE"
    scene.frame_set(1)
    bpy.context.view_layer.update()
    # The edit copy is self-contained and contains the preserved source scenes;
    # only this NEW file receives changes. The immutable source is never saved.
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(directory/"Weapon-Ready.blend"), check_existing=False)
    P.write_json(directory/"export-record.json", result)
    print("WEAPON_READY", kind, str(directory/"weapon-manifest.json"), json.dumps(metrics), flush=True)
    return result


def main(args):
    before = P.H.digest(SOURCE)
    OUT.mkdir(parents=True, exist_ok=True)
    if args.mode == "validate":
        P.validate()
    elif args.mode == "diagnostic":
        for kind in args.weapons:
            diagnostic_views(kind)
    elif args.mode == "contacts":
        for kind in args.weapons:
            refresh_contacts(kind)
    else:
        for kind in args.weapons:
            process(kind, args.mode == "export")
        if args.mode == "export":
            manifest = {"schema": "hollow-cascadeur-weapon-inputs-1", "source": str(SOURCE),
                        "source_sha256": before, "source_saved": False, "views": {}, "files": [],
                        "weapons": {kind: str(OUT/kind/"weapon-manifest.json") for kind in ASSETS}}
            for kind in ASSETS:
                path = OUT/kind/"export-record.json"
                if path.exists():
                    record = json.loads(path.read_text())
                    manifest["views"][kind] = record["view"]
                    manifest["files"].extend(record["files"])
            manifest["source_sha256_after"] = P.H.digest(SOURCE)
            P.write_json(OUT/"input-manifest.json", manifest)
    if P.H.digest(SOURCE) != before:
        raise RuntimeError("Immutable v3 source changed")
    print("IMMUTABLE_V3_UNCHANGED", before, flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("preview", "export", "validate", "diagnostic", "contacts"))
    parser.add_argument("--weapons", nargs="+", choices=ASSETS, default=list(ASSETS))
    main(parser.parse_args(sys.argv[sys.argv.index("--")+1:]))
